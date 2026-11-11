using System.Collections.Generic;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The tamed mammoth's model (the Meshy "Crimson Mammoth", rigged and keyed by
    /// Tools/Animation/meshy/build_mammoth.py) over a charge tower's beast (BeastCharge), and the
    /// pen's model (the Meshy "Palisade Camp") over the tower. Without the models (they are local,
    /// not in the repository) the greybox stays.
    ///
    /// The beast's own state drives the clips: standing in the pen, running out and along the road,
    /// a scoop of its tusks timed so the blow in the clip lands when the game hurts the goblins
    /// (the Scoop clip holds two scoops, left then right, 0.8 s each with the blow 0.2 s in), and its
    /// death: it rears up, flails and crashes over onto one side, turned a little at random, inside
    /// the road, with a tremor as it hits the ground. Raging, grey smoke pours off its back. Sizes
    /// TEMPORARY.
    /// </summary>
    public sealed class MammothVisual : MonoBehaviour
    {
        private const string Folder = "Meshy/";
        // TEMPORARY: shoulder-to-ground about 3 m, next to 1.35 m cavemen.
        public const float Height = 3f;
        // TEMPORARY: the pen's width on the map; it holds the 4.7 m mammoth with room (user, 2026-11-11).
        public const float PenWidth = 6.6f;
        // From build_mammoth.py's report: ground covered per run cycle, in model units.
        private const float RunGroundPerCycle = 0.65f;
        private const float ScoopHalfSeconds = 0.8f;
        private const float Blend = 0.2f;
        // Dying: it turns by one of these as it rears, then falls onto its left or right side: ten
        // ways down, picked at random (user, 2026-11-11).
        private static readonly float[] DeathTurns = { -40f, -20f, 0f, 20f, 40f };
        private const float DeathTurnSeconds = 0.8f;
        private const float TremorStrength = 0.004f;
        private const float TremorSeconds = 1f;
        // The horde is pushed from under the body from just before it lands, and kept off it this long
        // after; then it walks over it while it sinks (user, 2026-11-11). TEMPORARY.
        private const float PushLead = 0.35f;
        public const float HoldSeconds = 1f;
        private static readonly string[] DeathClips = { "DeathL", "DeathR" };

        private sealed class Death
        {
            public AnimationClip Clip;
            public float ImpactSeconds;
            // Points over the body as it lies at the end, in the beast's own space (metres, on the ground plane).
            public readonly List<Vector3> Lying = new();
            // Its footprint lying down: a middle, a long axis, and half its length and width (tusk and
            // leg tips left out).
            public Vector3 Centre;
            public Vector3 Axis = Vector3.forward;
            public float HalfLength;
            public float HalfWidth;
            // How high its flank or belly is as it lies: what the horde climbs onto.
            public float BackHeight;
        }

        private sealed class Kind
        {
            public GameObject Prefab;
            public Material Body;
            public readonly Dictionary<string, AnimationClip> Clips = new();
            public readonly Dictionary<string, Death> Deaths = new();
            public float Scale;
            public float Lift;
            public Quaternion Turn;
        }

        private static Kind loaded;
        private static bool tried;
        private static Material smokeMaterial;

        private BeastCharge beast;
        private Kind kind;
        private GameObject model;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly List<string> slots = new();

        private string clip;
        private float clipTime;
        private string previousClip;
        private float previousTime;
        private float blend = 1f;
        private Vector3 lastPosition;
        private bool wasScooping;
        private int scoopHalf;
        private float scoopTime;
        private bool wasFalling;
        private Transform pivot;
        private ParticleSystem smoke;

        // The death being played: its clip, the turn it takes, and how far the body slides (in the
        // beast's own space) so that it lands inside the road.
        private Death death;
        private float deathTurn;
        private Vector3 deathShift;
        private bool crashed;
        private float deathAge;

        /// <summary>True when the mammoth's model is installed.</summary>
        public static bool Installed => Load() != null;

        /// <summary>Puts the model over `beastRoot` and hides the greybox under it; null without the model.</summary>
        public static MammothVisual TryCreate(BeastCharge owner, Transform beastRoot)
        {
            var kind = Load();
            if (kind == null)
            {
                return null;
            }

            foreach (var renderer in beastRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            var visual = beastRoot.gameObject.AddComponent<MammothVisual>();
            visual.beast = owner;
            visual.kind = kind;
            visual.lastPosition = beastRoot.position;
            visual.Build();
            return visual;
        }

        /// <summary>
        /// The pen's model with its gate at `gate`, facing out along `outward`; null without the model.
        /// </summary>
        public static GameObject TryCreatePen(Transform parent, Vector3 gate, Vector3 outward)
        {
            var prefab = Resources.Load<GameObject>(Folder + "mammoth-pen");
            var shader = Shader.Find("TowerDefense/StyleSkinned");
            if (prefab == null || shader == null)
            {
                return null;
            }

            var material = new Material(shader) { name = "MammothPen" };
            material.SetTexture("_MainTex", Resources.Load<Texture2D>(Folder + "mammoth-pen-tex"));
            var penPivot = new GameObject("MammothPen");
            penPivot.transform.SetParent(parent, false);
            var pen = Instantiate(prefab, penPivot.transform, false);
            var bounds = new Bounds();
            var first = true;
            foreach (var renderer in pen.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (first)
                {
                    bounds = renderer.bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            // The model's middle over the pivot and its floor on the ground. Its gate faces +Z.
            pen.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            var scale = PenWidth / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
            penPivot.transform.localScale = Vector3.one * scale;
            outward.y = 0f;
            var facing = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
            penPivot.transform.SetPositionAndRotation(gate - facing * PenWidth * 0.5f, Quaternion.LookRotation(facing, Vector3.up));
            return penPivot.gameObject;
        }

        private static Kind Load()
        {
            if (tried)
            {
                return loaded;
            }

            tried = true;
            var prefab = Resources.Load<GameObject>(Folder + "mammoth");
            var shader = Shader.Find("TowerDefense/StyleSkinned");
            if (prefab == null || shader == null)
            {
                return null;
            }

            var kind = new Kind { Prefab = prefab, Body = new Material(shader) { name = "Mammoth" } };
            kind.Body.SetTexture("_MainTex", Resources.Load<Texture2D>(Folder + "mammoth-tex"));
            foreach (var each in Resources.LoadAll<AnimationClip>(Folder + "mammoth"))
            {
                if (each.name.StartsWith("__preview__"))
                {
                    continue;
                }

                // Exported actions are named after the rig: "MammothRig|Run" or just "Run".
                var bar = each.name.LastIndexOf('|');
                kind.Clips[bar >= 0 ? each.name.Substring(bar + 1) : each.name] = each;
            }

            if (!kind.Clips.ContainsKey("Run") || !kind.Clips.ContainsKey("Scoop") ||
                !kind.Clips.ContainsKey("DeathL") || !kind.Clips.ContainsKey("DeathR"))
            {
                return null;
            }

            // Size, ground and facing from a throwaway copy standing in the scoop's first pose: the
            // head is in front of the hips.
            var probe = Instantiate(prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            kind.Clips["Scoop"].SampleAnimation(probe, 0f);
            var low = float.MaxValue;
            var high = float.MinValue;
            foreach (var point in Bake(probe, Matrix4x4.identity, 1))
            {
                low = Mathf.Min(low, point.y);
                high = Mathf.Max(high, point.y);
            }

            kind.Scale = Height / Mathf.Max(0.01f, high - low);
            kind.Lift = -low * kind.Scale;
            var ahead = Find(probe.transform, "Head").position - Find(probe.transform, "Hips").position;
            ahead.y = 0f;
            kind.Turn = Quaternion.Inverse(Quaternion.LookRotation(ahead.sqrMagnitude > 1e-8f ? ahead.normalized : Vector3.forward, Vector3.up));

            // Each death: when it hits the ground (the chest comes down from its highest to within a
            // tenth of where it ends), and where the body lies at the end.
            var toBeast = Matrix4x4.TRS(new Vector3(0f, kind.Lift, 0f), kind.Turn, Vector3.one * kind.Scale);
            var chest = Find(probe.transform, "Chest");
            foreach (var name in DeathClips)
            {
                var entry = new Death { Clip = kind.Clips[name] };
                var length = entry.Clip.length;
                entry.Clip.SampleAnimation(probe, length);
                var end = chest.position.y;
                var top = float.MinValue;
                var topAt = 0f;
                for (var t = 0f; t < length; t += 1f / 30f)
                {
                    entry.Clip.SampleAnimation(probe, t);
                    if (chest.position.y > top)
                    {
                        top = chest.position.y;
                        topAt = t;
                    }
                }

                entry.ImpactSeconds = length;
                for (var t = topAt; t < length; t += 1f / 60f)
                {
                    entry.Clip.SampleAnimation(probe, t);
                    if (chest.position.y - end < 0.1f * (top - end))
                    {
                        entry.ImpactSeconds = t;
                        break;
                    }
                }

                entry.Clip.SampleAnimation(probe, length);
                var heights = new List<float>();
                foreach (var point in Bake(probe, toBeast, 40))
                {
                    entry.Lying.Add(new Vector3(point.x, 0f, point.z));
                    heights.Add(point.y);
                }

                heights.Sort();
                entry.BackHeight = heights.Count > 0 ? heights[Mathf.Clamp(Mathf.RoundToInt(0.7f * (heights.Count - 1)), 0, heights.Count - 1)] : 1f;

                Footprint(entry);
                kind.Deaths[name] = entry;
            }

            Destroy(probe);
            loaded = kind;
            return kind;
        }

        // The lying body's long axis (the main spread of its points on the ground) and its size along
        // and across it, from the 5th to the 95th percentile.
        private static void Footprint(Death entry)
        {
            var points = entry.Lying;
            var mean = Vector3.zero;
            foreach (var point in points)
            {
                mean += point / points.Count;
            }

            float xx = 0f, xz = 0f, zz = 0f;
            foreach (var point in points)
            {
                var d = point - mean;
                xx += d.x * d.x;
                xz += d.x * d.z;
                zz += d.z * d.z;
            }

            var angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
            var axis = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var side = new Vector3(-axis.z, 0f, axis.x);
            var along = new List<float>();
            var across = new List<float>();
            foreach (var point in points)
            {
                along.Add(Vector3.Dot(point - mean, axis));
                across.Add(Vector3.Dot(point - mean, side));
            }

            along.Sort();
            across.Sort();
            float At(List<float> sorted, float share) => sorted[Mathf.Clamp(Mathf.RoundToInt(share * (sorted.Count - 1)), 0, sorted.Count - 1)];
            var alongLow = At(along, 0.05f);
            var alongHigh = At(along, 0.95f);
            var acrossLow = At(across, 0.05f);
            var acrossHigh = At(across, 0.95f);
            entry.Axis = axis;
            entry.Centre = mean + axis * (alongLow + alongHigh) * 0.5f + side * (acrossLow + acrossHigh) * 0.5f;
            entry.HalfLength = (alongHigh - alongLow) * 0.5f;
            entry.HalfWidth = (acrossHigh - acrossLow) * 0.5f;
        }

        // Every `step`-th vertex of the posed model, through `transform`.
        private static List<Vector3> Bake(GameObject rig, Matrix4x4 transform, int step)
        {
            var points = new List<Vector3>();
            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                skin.BakeMesh(mesh);
                var toWorld = transform * skin.transform.localToWorldMatrix;
                var vertices = mesh.vertices;
                for (var i = 0; i < vertices.Length; i += step)
                {
                    points.Add(toWorld.MultiplyPoint3x4(vertices[i]));
                }

                Destroy(mesh);
            }

            return points;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>())
            {
                if (child.name == name || child.name.EndsWith(":" + name))
                {
                    return child;
                }
            }

            return root;
        }

        private void Build()
        {
            pivot = new GameObject("MammothModel").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0f, kind.Lift, 0f);
            pivot.localRotation = kind.Turn;
            pivot.localScale = Vector3.one * kind.Scale;
            model = Instantiate(kind.Prefab, pivot, false);
            model.name = "Mammoth";
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.sharedMaterial = kind.Body;
                skin.updateWhenOffscreen = true;
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }

            animator.applyRootMotion = false;
            graph = PlayableGraph.Create("Mammoth");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            slots.AddRange(kind.Clips.Keys);
            mixer = AnimationMixerPlayable.Create(graph, slots.Count);
            for (var i = 0; i < slots.Count; i++)
            {
                var playable = AnimationClipPlayable.Create(graph, kind.Clips[slots[i]]);
                playable.SetApplyFootIK(false);
                graph.Connect(playable, 0, mixer, i);
                mixer.SetInputWeight(i, 0f);
            }

            AnimationPlayableOutput.Create(graph, "Mammoth", animator).SetSourcePlayable(mixer);
            clip = "Scoop";
            clipTime = 0f;
            smoke = CreateSmoke(transform);
        }

        private void LateUpdate()
        {
            if (!graph.IsValid() || beast == null)
            {
                return;
            }

            var dt = Time.deltaTime;
            var position = transform.position;
            var moved = position - lastPosition;
            moved.y = 0f;
            var speed = dt > 0f ? moved.magnitude / dt : 0f;
            lastPosition = position;

            if (beast.IsFalling)
            {
                if (!wasFalling)
                {
                    BeginDeath();
                }

                deathAge += dt;
                clipTime = Mathf.Min(clipTime + dt, death.Clip.length - 0.001f);
                // It turns as it rears, and slides onto the road on its way down.
                var turn = Mathf.SmoothStep(0f, 1f, clipTime / DeathTurnSeconds);
                var fall = Mathf.SmoothStep(0f, 1f, clipTime / Mathf.Max(0.05f, death.ImpactSeconds));
                pivot.localRotation = Quaternion.Euler(0f, deathTurn * turn, 0f) * kind.Turn;
                pivot.localPosition = new Vector3(0f, kind.Lift, 0f) + deathShift * fall;
                if (!crashed && clipTime >= death.ImpactSeconds)
                {
                    crashed = true;
                    GroundTremor.Shake(TremorStrength, TremorSeconds);
                }
            }
            else if (beast.IsRoaring)
            {
                // The rage comes over it: it stops and roars.
                if (clip != "Rage")
                {
                    Play("Rage");
                }

                clipTime += dt / BeastCharge.RoarStretch;
                wasScooping = false;
            }
            else if (beast.IsFighting)
            {
                // A new scoop in the game starts the next half of the clip, so its blow lands with the game's.
                var progress = beast.ScoopProgress;
                var scooping = progress >= 0f;
                if (scooping && !wasScooping)
                {
                    scoopHalf = 1 - scoopHalf;
                    scoopTime = 0f;
                }

                wasScooping = scooping;
                scoopTime = Mathf.Min(ScoopHalfSeconds, scoopTime + dt * beast.ScoopClipSpeed);
                Play("Scoop");
                clipTime = scoopHalf * ScoopHalfSeconds + scoopTime;
            }
            else if (beast.IsOut && speed > 0.3f)
            {
                // The run's feet keep pace with the ground.
                Play("Run");
                var cycle = kind.Clips["Run"].length;
                var groundSpeed = RunGroundPerCycle * kind.Scale / Mathf.Max(0.01f, cycle);
                clipTime += dt * Mathf.Clamp(speed / Mathf.Max(0.01f, groundSpeed), 0.4f, 2.2f);
            }
            else if ((!beast.IsOut || beast.IsWaiting) && kind.Clips.ContainsKey("Idle"))
            {
                // Waiting in the pen or out on the road: it breathes, grazes and swings its trunk.
                Play("Idle");
                clipTime += dt;
                wasScooping = false;
            }
            else
            {
                // Standing: the scoop's first pose.
                Play("Scoop");
                clipTime = 0f;
                wasScooping = false;
            }

            if (!beast.IsFalling && wasFalling)
            {
                // The next mammoth, upright again.
                pivot.localPosition = new Vector3(0f, kind.Lift, 0f);
                pivot.localRotation = kind.Turn;
            }

            wasFalling = beast.IsFalling;
            UpdateSmoke();
            Evaluate(dt);
        }

        private void Evaluate(float dt)
        {
            blend = Mathf.Min(1f, blend + dt / Blend);
            previousTime += dt;
            for (var i = 0; i < slots.Count; i++)
            {
                var name = slots[i];
                var weight = 0f;
                var time = 0f;
                if (name == clip)
                {
                    weight = blend;
                    time = name.StartsWith("Death") || name == "Scoop" ? clipTime : Loop(name, clipTime);
                }
                else if (name == previousClip)
                {
                    weight = 1f - blend;
                    time = name.StartsWith("Death") ? previousTime : Loop(name, previousTime);
                }

                mixer.SetInputWeight(i, weight);
                if (weight > 0f)
                {
                    mixer.GetInput(i).SetTime(time);
                }
            }

            graph.Evaluate(0f);
        }

        // One of the ten ways down, and how far it must slide to lie inside the road.
        private void BeginDeath()
        {
            death = kind.Deaths[DeathClips[Random.Range(0, DeathClips.Length)]];
            deathTurn = DeathTurns[Random.Range(0, DeathTurns.Length)];
            Play(death.Clip == kind.Clips["DeathL"] ? "DeathL" : "DeathR");
            clipTime = 0f;
            deathAge = 0f;
            crashed = false;
            deathShift = ShiftOntoRoad();
        }

        // In the beast's own space: how far the lying body must slide across the road so that all of
        // it is on the road; centred on the road when the road is narrower than the body.
        private Vector3 ShiftOntoRoad()
        {
            FillsRoad = false;
            var route = beast.Route;
            if (route == null || route.TotalLength <= 0f)
            {
                return Vector3.zero;
            }

            var position = transform.position;
            position.y = 0f;
            var middle = route.GetNearestRoadPoint(position, out var tangent);
            middle.y = 0f;
            tangent.y = 0f;
            var across = Vector3.Cross(Vector3.up, tangent.sqrMagnitude > 1e-6f ? tangent.normalized : transform.forward).normalized;
            var halfWidth = 0f;
            while (halfWidth < 12f && route.IsInsideCorridor(middle + across * (halfWidth + 0.1f)) &&
                   route.IsInsideCorridor(middle - across * (halfWidth + 0.1f)))
            {
                halfWidth += 0.1f;
            }

            var low = float.MaxValue;
            var high = float.MinValue;
            var turn = Quaternion.Euler(0f, deathTurn, 0f);
            foreach (var point in death.Lying)
            {
                var at = Vector3.Dot(transform.TransformPoint(turn * point) - middle, across);
                low = Mathf.Min(low, at);
                high = Mathf.Max(high, at);
            }

            const float margin = 0.2f;
            var room = halfWidth - margin;
            // Lying across the whole road: the horde cannot get round it.
            FillsRoad = high - low > 2f * halfWidth - 0.4f;
            float shift;
            if (high - low > 2f * room)
            {
                shift = -(low + high) * 0.5f;
            }
            else if (high > room)
            {
                shift = room - high;
            }
            else if (low < -room)
            {
                shift = -room - low;
            }
            else
            {
                shift = 0f;
            }

            return transform.InverseTransformVector(across * shift);
        }

        /// <summary>
        /// While the body comes down and for HoldSeconds after it hits the ground: its footprint as a
        /// row of circles from `a` to `b`, `radius` wide, so the horde is pushed out from under it and
        /// goes round it. `landing` is true as it comes down (a harder push). Afterwards it is false,
        /// and the horde walks over the body while it sinks (user, 2026-11-11).
        /// </summary>
        public bool TryGetFootprint(out Vector3 a, out Vector3 b, out float radius, out bool landing)
        {
            a = b = transform.position;
            radius = 0f;
            landing = false;
            if (death == null || !beast.IsFalling || deathAge < death.ImpactSeconds - PushLead)
            {
                return false;
            }

            var turn = Quaternion.Euler(0f, deathTurn, 0f);
            var reach = Mathf.Max(0f, death.HalfLength - death.HalfWidth);
            a = transform.TransformPoint(turn * (death.Centre + death.Axis * reach) + deathShift);
            b = transform.TransformPoint(turn * (death.Centre - death.Axis * reach) + deathShift);
            a.y = b.y = 0f;
            radius = death.HalfWidth + 0.35f;
            landing = deathAge < death.ImpactSeconds + 0.3f;
            return true;
        }

        /// <summary>Seconds since the body hit the ground (negative before).</summary>
        public float SinceImpact => death != null && beast.IsFalling ? deathAge - death.ImpactSeconds : -1f;

        /// <summary>How high the lying body is: what the horde climbs onto.</summary>
        public float BackHeight => death != null ? death.BackHeight : 1f;

        /// <summary>True when the lying body fills the road from side to side.</summary>
        public bool FillsRoad { get; private set; }

        /// <summary>How long it lies before it starts to sink: the fall, then the horde held off it.</summary>
        public float LieSeconds
        {
            get
            {
                var latest = 0f;
                foreach (var each in kind.Deaths.Values)
                {
                    latest = Mathf.Max(latest, each.ImpactSeconds);
                }

                return latest + HoldSeconds;
            }
        }

        // Raging, grey smoke pours off its back: soft, overlapping puffs that grow and thin out.
        private static ParticleSystem CreateSmoke(Transform parent)
        {
            if (smokeMaterial == null)
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                if (shader == null)
                {
                    return null;
                }

                smokeMaterial = new Material(shader) { name = "MammothSmoke", mainTexture = SoftPuff() };
            }

            var go = new GameObject("RageSmoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, Height * 0.85f, -0.2f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.55f, 0.55f, 0.56f, 1f);
            main.maxParticles = 200;
            var emission = system.emission;
            emission.rateOverTime = 22f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.1f, 2.6f, 0.2f);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(new Color(0.6f, 0.6f, 0.61f), 0f), new GradientColorKey(new Color(0.72f, 0.72f, 0.73f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.42f, 0.15f), new GradientAlphaKey(0.25f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var drift = system.velocityOverLifetime;
            drift.enabled = true;
            drift.space = ParticleSystemSimulationSpace.World;
            drift.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            drift.y = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            drift.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        // A round, soft-edged, slightly lumpy puff.
        private static Texture2D SoftPuff()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftPuff" };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) / size * 2f - 1f;
                    var v = (y + 0.5f) / size * 2f - 1f;
                    var r = Mathf.Sqrt(u * u + v * v);
                    var lumps = 0.85f + 0.15f * Mathf.PerlinNoise(x * 0.11f, y * 0.11f) + 0.1f * Mathf.PerlinNoise(x * 0.27f + 13f, y * 0.27f + 7f);
                    var alpha = Mathf.Clamp01(1f - r) * lumps;
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void UpdateSmoke()
        {
            if (smoke == null)
            {
                return;
            }

            if (beast.IsRaging && !smoke.isEmitting)
            {
                smoke.Play();
            }
            else if (!beast.IsRaging && smoke.isEmitting)
            {
                smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void Play(string name)
        {
            if (name == clip)
            {
                return;
            }

            previousClip = clip;
            previousTime = clipTime;
            clip = name;
            clipTime = 0f;
            blend = previousClip != null && previousClip.StartsWith("Death") ? 1f : 0f;
        }

        private float Loop(string name, float time)
        {
            var length = kind.Clips[name].length;
            return length > 0f ? Mathf.Repeat(time, length) : 0f;
        }

        private void OnDestroy()
        {
            if (graph.IsValid())
            {
                graph.Destroy();
            }
        }
    }
}
