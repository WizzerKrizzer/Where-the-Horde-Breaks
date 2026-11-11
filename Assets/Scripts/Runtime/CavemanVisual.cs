using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The look of a caveman, or of their chief: the Meshy model rigged in Mixamo
    /// (Tools/Animation/build_caveman.sh puts it in Resources/Mixamo; the files stay out of git), with
    /// a big bone in his fist, or the chief's mammoth tusk, and the cave's specials: a hide shield on
    /// the left forearm and, with the torches, a burning torch on the shield. Without the model the
    /// unit keeps its greybox block.
    ///
    /// The visual is its own object that follows the unit (the unit's block is scaled unevenly). It
    /// reads what the unit does and plays a clip for it, blending between them: idle or a shield
    /// stance, a walk timed to the ground covered, a swing timed so the blow lands when the unit deals
    /// its damage, a flinch or a block when hurt, the chief's battle cry when a fight starts, and the
    /// death, which plays out after the unit itself is gone.
    /// </summary>
    public sealed class CavemanVisual : MonoBehaviour
    {
        // TEMPORARY sizes for the greybox game, where the goblins are about half a metre tall.
        private const float CavemanHeight = 1.35f;
        private const float ChiefHeight = 1.7f;
        private const float BoneLength = 0.85f;
        private const float BoneGripFromEnd = 0.13f;
        private const float TuskLength = 1.05f;
        private const float TuskGrip = 0.1f;
        // Ground covered per second by each walk at the model's own size, from the build report.
        private static readonly Dictionary<string, float> WalkGroundSpeed = new() { { "Walk", 0.55f }, { "WalkB", 0.49f }, { "WalkHeavy", 0.64f } };
        private const float FadeIn = 0.15f;
        // After an action's window the body stays in its last pose and eases back to standing over
        // this long, instead of snapping back (user, 2026-11-11). TEMPORARY.
        private const float Recovery = 0.3f;
        // A heavy swing: a long wind-up, played a little slower than Mixamo's, then the blow
        // (user, 2026-11-11). TEMPORARY.
        private const float SwingBeforeHit = 0.5f;
        private const float SwingAfterHit = 0.25f;
        private const float SlowestSwing = 0.8f;
        private const float TurnDegreesPerSecond = 200f;

        private sealed class Kind
        {
            public GameObject Prefab;
            public Material Body;
            public readonly Dictionary<string, AnimationClip> Clips = new();
            public readonly Dictionary<string, float> HitSeconds = new();
            public float ModelHeight;
        }

        private static readonly Dictionary<string, Kind> Kinds = new();
        private static Material propMaterial;

        private AlliedUnitActor unit;
        private Kind kind;
        private bool isChief;
        private GameObject model;
        private Animator animator;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly List<string> slots = new();
        private float scale;
        private Transform prop;
        private Transform leftForeArm;
        private Transform shield;
        private string walkClip;
        private int swingIndex;

        // Blending: the base loop (idle, stance or walk) and one action over it.
        private string baseClip;
        private string previousBase;
        private float baseTime;
        private float previousBaseTime;
        private float baseBlend = 1f;
        private string action;
        private float actionTime;
        private float actionEnd;
        private float actionSpeed = 1f;
        private float actionWeight;
        // A flinch can be cut short by a swing, and a swing can start while the last one eases out.
        private bool actionInterruptible;
        // The action a new one cut short, fading out from where it was.
        private string fadingAction;
        private float fadingTime;
        private float fadingWeight;
        // Testing only: how many swings the models started.
        internal static int TestSwings;
        private bool actionHolds;
        private bool recovering;

        private Vector3 lastPosition;
        private float lastHealth;
        private float hurtCooldown;
        private float idleSince;
        private bool dying;
        private float deathAge;
        private Vector3 facing = Vector3.forward;

        /// <summary>Gives `owner` a model if the cavemen's model is installed; null otherwise.</summary>
        public static CavemanVisual TryCreate(AlliedUnitActor owner, int index)
        {
            var kind = Load("caveman");
            if (kind == null)
            {
                return null;
            }

            var visual = new GameObject("CavemanVisual").AddComponent<CavemanVisual>();
            visual.unit = owner;
            visual.transform.position = owner.Position;
            visual.lastPosition = owner.Position;
            visual.lastHealth = owner.CurrentHealth;
            visual.walkClip = index % 2 == 0 ? "Walk" : "WalkB";
            visual.Show(kind, false);
            return visual;
        }

        /// <summary>The unit was made chief: swap to the chief's model and his tusk.</summary>
        public void MakeChief()
        {
            var chief = Load("caveman-chief");
            if (chief != null)
            {
                Show(chief, true);
            }
        }

        /// <summary>The unit was reused as an ordinary caveman.</summary>
        public void Reset(AlliedUnitActor owner)
        {
            unit = owner;
            dying = false;
            if (isChief)
            {
                Show(Load("caveman"), false);
            }
        }

        public float Height => (isChief ? ChiefHeight : CavemanHeight);

        private static Kind Load(string name)
        {
            if (Kinds.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var prefab = Resources.Load<GameObject>("Mixamo/" + name);
            var shader = Shader.Find("TowerDefense/StyleSkinned");
            if (prefab == null || shader == null)
            {
                Kinds[name] = null;
                return null;
            }

            var kind = new Kind { Prefab = prefab, Body = new Material(shader) { name = name } };
            kind.Body.SetTexture("_MainTex", Resources.Load<Texture2D>("Mixamo/" + name + "-tex"));
            foreach (var clip in Resources.LoadAll<AnimationClip>("Mixamo/" + name))
            {
                if (clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                // Exported actions are named after the rig: "caveman|AttackDown" or just "AttackDown".
                var bar = clip.name.LastIndexOf('|');
                kind.Clips[bar >= 0 ? clip.name.Substring(bar + 1) : clip.name] = clip;
            }

            if (!kind.Clips.ContainsKey("Idle"))
            {
                Kinds[name] = null;
                return null;
            }

            // The model's height and the moment of each blow (when the fist moves fastest) from a
            // throwaway copy.
            var probe = Instantiate(prefab);
            var hand = Find(probe.transform, "RightHandProp");
            kind.ModelHeight = MeasureHeight(probe, kind.Clips["Idle"]);
            foreach (var pair in kind.Clips)
            {
                if (!pair.Key.StartsWith("Attack") && pair.Key != "ShieldBlock" && pair.Key != "Battlecry")
                {
                    continue;
                }

                var best = 0f;
                var at = 0f;
                const float step = 1f / 30f;
                // The chief's mirrored swings are struck with the left hand.
                var striker = pair.Key.EndsWith("Left") ? Find(probe.transform, "LeftHand") ?? hand : hand;
                pair.Value.SampleAnimation(probe, 0f);
                var previous = striker.position;
                for (var t = step; t < pair.Value.length; t += step)
                {
                    pair.Value.SampleAnimation(probe, t);
                    var speed = (striker.position - previous).sqrMagnitude;
                    previous = striker.position;
                    if (speed > best)
                    {
                        best = speed;
                        at = t;
                    }
                }

                kind.HitSeconds[pair.Key] = at;
            }

            Destroy(probe);
            Kinds[name] = kind;
            return kind;
        }

        private static float MeasureHeight(GameObject rig, AnimationClip idle)
        {
            idle.SampleAnimation(rig, 0f);
            var low = float.MaxValue;
            var high = float.MinValue;
            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                skin.BakeMesh(mesh);
                var toWorld = skin.transform.localToWorldMatrix;
                foreach (var vertex in mesh.vertices)
                {
                    var y = toWorld.MultiplyPoint3x4(vertex).y;
                    low = Mathf.Min(low, y);
                    high = Mathf.Max(high, y);
                }

                Destroy(mesh);
            }

            return Mathf.Max(0.01f, high - low);
        }

        private static Transform Find(Transform root, string suffix)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>())
            {
                if (child.name.EndsWith(suffix))
                {
                    return child;
                }
            }

            return null;
        }

        private void Show(Kind newKind, bool chief)
        {
            if (newKind == null)
            {
                return;
            }

            if (graph.IsValid())
            {
                graph.Destroy();
            }

            if (model != null)
            {
                Destroy(model);
            }

            kind = newKind;
            isChief = chief;
            model = Instantiate(kind.Prefab, transform, false);
            model.name = chief ? "Chief" : "Caveman";
            scale = Height / kind.ModelHeight;
            model.transform.localScale = Vector3.one * scale;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.sharedMaterial = kind.Body;
                skin.updateWhenOffscreen = true;
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // The model faces wherever Mixamo left it; turn it to face along this object's forward.
            var aheadHere = transform.InverseTransformDirection(Ahead(model.transform));
            model.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(aheadHere, Vector3.up)) * model.transform.localRotation;
            animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }

            animator.applyRootMotion = false;
            BuildGraph();
            AttachProps();
            baseClip = null;
            previousBase = null;
            action = null;
            fadingAction = null;
            actionWeight = 0f;
            if (isChief)
            {
                PlayAction("Battlecry", 0f, kind.Clips.TryGetValue("Battlecry", out var cry) ? cry.length : 0f, 1f);
            }
        }

        private void BuildGraph()
        {
            graph = PlayableGraph.Create(name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            slots.Clear();
            slots.AddRange(kind.Clips.Keys);
            mixer = AnimationMixerPlayable.Create(graph, slots.Count);
            for (var i = 0; i < slots.Count; i++)
            {
                var playable = AnimationClipPlayable.Create(graph, kind.Clips[slots[i]]);
                playable.SetApplyFootIK(false);
                graph.Connect(playable, 0, mixer, i);
                mixer.SetInputWeight(i, 0f);
            }

            var output = AnimationPlayableOutput.Create(graph, "Caveman", animator);
            output.SetSourcePlayable(mixer);
        }

        private void AttachProps()
        {
            propMaterial ??= CreatePropMaterial();
            prop = Find(model.transform, "RightHandProp");
            leftForeArm = Find(model.transform, "LeftForeArm");
            // Props are placed in poses where their directions are plain, then ride on the bone: in
            // the bind pose (arms out, palms down) a weapon in the fist points straight ahead; in the
            // shield stance the shield faces ahead with its top up.
            var ahead = Ahead(model.transform);
            var weapon = new GameObject(isChief ? "Tusk" : "Bone").transform;
            weapon.SetParent(prop, false);
            weapon.gameObject.AddComponent<MeshFilter>().sharedMesh = isChief ? UnitProps.Tusk(TuskLength) : UnitProps.BigBone(BoneLength);
            weapon.gameObject.AddComponent<MeshRenderer>().sharedMaterial = propMaterial;
            weapon.rotation = Quaternion.LookRotation(ahead, Vector3.up);
            weapon.position = prop.position - ahead * (isChief ? TuskGrip : BoneGripFromEnd);
            weapon.localScale = Vector3.one / Mathf.Max(1e-4f, prop.lossyScale.x);

            // The chief carries a tusk in each fist (user, 2026-11-11): the left one is placed where
            // the build puts the right fist's prop, mirrored, 60% of the way from wrist to knuckles.
            var leftHand = Find(model.transform, "LeftHand");
            var leftKnuckles = Find(model.transform, "LeftHandMiddle1");
            if (isChief && leftHand != null && leftKnuckles != null)
            {
                var second = new GameObject("TuskLeft").transform;
                second.SetParent(leftHand, false);
                second.gameObject.AddComponent<MeshFilter>().sharedMesh = UnitProps.Tusk(TuskLength);
                second.gameObject.AddComponent<MeshRenderer>().sharedMaterial = propMaterial;
                second.rotation = Quaternion.LookRotation(ahead, Vector3.up);
                var grip = leftHand.position + (leftKnuckles.position - leftHand.position) * 0.6f;
                second.position = grip - ahead * TuskGrip;
                second.localScale = Vector3.one / Mathf.Max(1e-4f, leftHand.lossyScale.x);
            }

            shield = null;
            if (!isChief && unit != null && unit.HasShield && leftForeArm != null && kind.Clips.TryGetValue("ShieldIdle", out var stance))
            {
                stance.SampleAnimation(model, 0f);
                var shieldObject = new GameObject("Shield");
                shield = shieldObject.transform;
                shield.SetParent(leftForeArm, false);
                var arm = leftForeArm.GetChild(0).position - leftForeArm.position;
                var facingNow = Ahead(model.transform);
                shield.position = leftForeArm.position + arm * 0.55f + facingNow * 0.09f;
                shield.rotation = Quaternion.LookRotation(facingNow, Vector3.up);
                shield.localScale = Vector3.one / Mathf.Max(1e-4f, leftForeArm.lossyScale.x);
                shieldObject.AddComponent<MeshFilter>().sharedMesh = UnitProps.HideShield();
                shieldObject.AddComponent<MeshRenderer>().sharedMaterial = propMaterial;
                if (unit.HasTorch)
                {
                    // The torch is lashed up the back of the shield, its head over the top rim.
                    var torch = new GameObject("Torch").transform;
                    torch.SetParent(shield, false);
                    torch.localPosition = new Vector3(0.12f, -0.18f, -0.05f);
                    torch.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
                    torch.gameObject.AddComponent<MeshFilter>().sharedMesh = UnitProps.TorchStick();
                    torch.gameObject.AddComponent<MeshRenderer>().sharedMaterial = propMaterial;
                    FlameVisual.Attach(torch, new Vector3(0f, 0f, 0.56f), 0.42f, 0.24f);
                }
            }
        }

        // Where the model faces in its current pose: from the heels to the toes.
        private static Vector3 Ahead(Transform root)
        {
            var heels = Find(root, "LeftFoot").position + Find(root, "RightFoot").position;
            var toes = Find(root, "LeftToeBase").position + Find(root, "RightToeBase").position;
            var ahead = toes - heels;
            ahead.y = 0f;
            return ahead.sqrMagnitude > 1e-8f ? ahead.normalized : Vector3.forward;
        }

        private static Material CreatePropMaterial()
        {
            var material = new Material(Shader.Find("TowerDefense/StyleSkinned")) { name = "UnitProps" };
            material.SetFloat("_VertexColor", 1f);
            return material;
        }

        private void LateUpdate()
        {
            if (!graph.IsValid())
            {
                return;
            }

            var dt = Time.deltaTime;
            if (unit == null || !unit.isActiveAndEnabled)
            {
                Die(dt);
                Evaluate(dt);
                return;
            }

            var position = unit.Position;
            var velocity = dt > 0f ? (position - lastPosition) / dt : Vector3.zero;
            velocity.y = 0f;
            lastPosition = position;
            transform.position = position;
            var speed = velocity.magnitude;

            // Face the fight, or the way it walks.
            if (unit.HasFacingTarget)
            {
                var toTarget = unit.FacingTarget - position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    facing = toTarget.normalized;
                }
            }
            else if (speed > 0.2f)
            {
                facing = velocity / speed;
            }
            else if (unit.WatchDirection.sqrMagnitude > 0.01f)
            {
                // Standing between strolls: look up the road for what is coming.
                facing = Vector3.RotateTowards(facing, unit.WatchDirection, 1.2f * dt, 0f);
            }

            var want = Quaternion.LookRotation(facing, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnDegreesPerSecond * dt);

            // The base loop: a walk timed to the ground, or standing.
            var moving = speed > 0.25f && action == null;
            var walk = isChief ? "WalkHeavy" : walkClip;
            var stand = shield != null && kind.Clips.ContainsKey("ShieldIdle") ? "ShieldIdle" : "Idle";
            SetBase(moving && kind.Clips.ContainsKey(walk) ? walk : stand);
            var baseSpeed = 1f;
            if (moving && WalkGroundSpeed.TryGetValue(walk, out var ground))
            {
                baseSpeed = Mathf.Clamp(speed / (ground * scale), 0.5f, 2.2f);
            }

            baseTime += dt * baseSpeed;
            previousBaseTime += dt;
            baseBlend = Mathf.Min(1f, baseBlend + dt / 0.2f);

            // Hurt: a flinch, or a block behind the shield.
            hurtCooldown -= dt;
            var health = unit.CurrentHealth;
            if (health < lastHealth - 0.01f && hurtCooldown <= 0f && action == null)
            {
                var react = shield != null && kind.Clips.ContainsKey("ShieldBlock") ? "ShieldBlock" : "Hit";
                if (kind.Clips.TryGetValue(react, out var reactClip))
                {
                    PlayAction(react, 0f, reactClip.length, 1.2f);
                    actionInterruptible = true;
                    hurtCooldown = 1.5f;
                }
            }

            lastHealth = health;

            // The chief roars when a fight starts after a quiet spell.
            if (!unit.InStrikeRange)
            {
                idleSince += dt;
            }
            else if (isChief && idleSince > 4f && action == null && kind.Clips.TryGetValue("Battlecry", out var cry))
            {
                PlayAction("Battlecry", 0f, cry.length, 1.15f);
                idleSince = 0f;
            }
            else
            {
                idleSince = 0f;
            }

            // A swing that lands when the unit strikes. With the torch, every other blow is the
            // shield thrust into the goblins.
            // A swing never waits for a flinch or for the last swing to ease out: every blow is seen.
            if (unit.InStrikeRange && (action == null || actionInterruptible || recovering) && unit.SecondsToNextStrike <= SwingBeforeHit)
            {
                StartSwing();
            }

            Evaluate(dt);
        }

        private void StartSwing()
        {
            var choices = shield != null && unit.HasTorch
                ? new[] { "AttackDown", "ShieldBlock" }
                : isChief && kind.Clips.ContainsKey("AttackDownLeft") && kind.Clips.ContainsKey("AttackSideLeft")
                    // A tusk in each hand: he strikes right, left, right, left (user, 2026-11-11).
                    ? new[] { "AttackDown", "AttackSideLeft", "AttackSide", "AttackDownLeft" }
                    : new[] { "AttackDown", "AttackSide" };
            var name = choices[swingIndex++ % choices.Length];
            if (!kind.Clips.TryGetValue(name, out var clip) || !kind.HitSeconds.TryGetValue(name, out var hit))
            {
                return;
            }

            TestSwings++;

            var lead = Mathf.Max(0f, unit.SecondsToNextStrike);
            var interval = Mathf.Max(0.3f, unit.AttackInterval);
            // Long Mixamo swings are cut to the blow, played slower when the interval leaves room
            // (after the ease back to standing) and sped up when a short interval does not.
            var speedUp = Mathf.Max(SlowestSwing, (SwingBeforeHit + SwingAfterHit) / Mathf.Max(0.1f, interval - Recovery));
            var start = Mathf.Max(0f, hit - lead * speedUp);
            var end = Mathf.Min(clip.length, hit + SwingAfterHit);
            PlayAction(name, start, end, speedUp);
        }

        private void PlayAction(string name, float from, float to, float speed)
        {
            if (!kind.Clips.ContainsKey(name) || to <= from)
            {
                return;
            }

            // What was playing fades out from its pose while the new action fades in.
            if (action != null && action != name && actionWeight > 0f && !actionHolds)
            {
                fadingAction = action;
                fadingTime = actionTime;
                fadingWeight = actionWeight;
                actionWeight = 0f;
            }

            action = name;
            actionTime = from;
            actionEnd = to;
            actionSpeed = speed;
            actionHolds = name == "Death";
            actionInterruptible = false;
            recovering = false;
        }

        private void SetBase(string clip)
        {
            if (clip == baseClip)
            {
                return;
            }

            previousBase = baseClip;
            previousBaseTime = baseTime;
            baseClip = clip;
            baseTime = 0f;
            baseBlend = previousBase == null ? 1f : 0f;
        }

        private void Die(float dt)
        {
            if (!dying)
            {
                dying = true;
                deathAge = 0f;
                if (shield != null)
                {
                    foreach (var flame in shield.GetComponentsInChildren<FlameVisual>())
                    {
                        Destroy(flame.gameObject);
                    }
                }

                if (kind.Clips.TryGetValue("Death", out var death))
                {
                    PlayAction("Death", 0f, death.length, 1f);
                }
            }

            deathAge += dt;
            // Lie a moment, then sink into the ground and go.
            var deathLength = kind.Clips.TryGetValue("Death", out var clip) ? clip.length : 1f;
            if (deathAge > deathLength + 1.2f)
            {
                transform.position += Vector3.down * dt * 0.6f;
            }

            if (deathAge > deathLength + 2.4f)
            {
                Destroy(gameObject);
            }
        }

        private void Evaluate(float dt)
        {
            // The action fades in over the base, plays its window, then fades out (a death holds).
            if (fadingAction != null)
            {
                fadingWeight -= dt / Recovery;
                if (fadingWeight <= 0f)
                {
                    fadingAction = null;
                }
            }

            if (action != null && recovering)
            {
                // Holding the last pose while easing back to standing.
                actionWeight = Mathf.Max(0f, actionWeight - dt / Recovery);
                if (actionWeight <= 0f)
                {
                    action = null;
                    recovering = false;
                }
            }
            else if (action != null)
            {
                actionTime += dt * actionSpeed;
                actionWeight = Mathf.Min(1f, actionWeight + dt / FadeIn);
                if (actionTime >= actionEnd)
                {
                    actionTime = actionEnd;
                    recovering = !actionHolds;
                }
            }

            for (var i = 0; i < slots.Count; i++)
            {
                var name = slots[i];
                var weight = 0f;
                var time = 0f;
                // Eased, so the body leaves and returns to standing gently.
                var actionShare = action != null ? Mathf.SmoothStep(0f, 1f, actionWeight) : 0f;
                var fadingShare = fadingAction != null ? Mathf.SmoothStep(0f, 1f, fadingWeight) * (1f - actionShare) : 0f;
                var baseShare = 1f - actionShare - fadingShare;
                if (name == action)
                {
                    weight = actionShare;
                    time = actionTime;
                }
                else if (name == fadingAction)
                {
                    weight = fadingShare;
                    time = fadingTime;
                }
                else if (name == baseClip)
                {
                    weight = baseShare * baseBlend;
                    time = Loop(name, baseTime);
                }
                else if (name == previousBase)
                {
                    weight = baseShare * (1f - baseBlend);
                    time = Loop(name, previousBaseTime);
                }

                mixer.SetInputWeight(i, weight);
                if (weight > 0f)
                {
                    mixer.GetInput(i).SetTime(time);
                }
            }

            graph.Evaluate(0f);
        }

        private float Loop(string clip, float time)
        {
            var length = kind.Clips[clip].length;
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
