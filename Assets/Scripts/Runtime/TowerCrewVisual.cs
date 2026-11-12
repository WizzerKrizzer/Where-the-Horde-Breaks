using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// A tower whose crew throw by hand (user, 2026-11-11): the Spear Throwers' hunters on their rock
    /// and the Slingers on their tipi. When the models are installed (Tools/Animation/
    /// install_crew_models.sh, git-ignored Resources), they replace the greybox. The tower starts a
    /// throw when it has a target, and its shot leaves the thrower's hand at the throw's release
    /// (TowerActor).
    /// </summary>
    public abstract class TowerCrewVisual : MonoBehaviour
    {
        protected const string Meshy = "Meshy/";
        protected const string Mixamo = "Mixamo/";
        // The building and its crew together, about the tower's spot: 25% smaller than in the
        // approved captures, which were too big (user, 2026-11-11). TEMPORARY.
        protected const float VisualScale = 0.75f;
        // The building stands back from the tower's spot with its front this far out from it towards
        // the road, as the mammoth's pen and the cave: centred on the spot it reached into the road
        // (user, 2026-11-11). Placing keeps the spot off the road.
        protected const float FrontAhead = 0.3f;

        // How far back to build so that, once scaled about the spot, the front `reach` ahead of the
        // building's middle ends FrontAhead out from the spot.
        protected static float BackOff(float reach) => Mathf.Max(0f, reach - FrontAhead / VisualScale);

        // A height on the built tower once it is scaled about its spot.
        protected static float Scaled(float y, float baseY) => baseY + (y - baseY) * VisualScale;

        // The tower's root stands on its spot; everything is built at full size, then scaled about it.
        protected static T MakeRoot<T>(TowerActor tower, string name) where T : TowerCrewVisual
        {
            var root = new GameObject(name);
            root.transform.SetParent(tower.transform.parent, false);
            root.transform.position = tower.transform.position;
            return root.AddComponent<T>();
        }

        protected void FinishBuild()
        {
            transform.localScale = Vector3.one * VisualScale;
        }

        /// <summary>Seconds from the start of a throw to the release.</summary>
        public abstract float ReleaseLead { get; }

        /// <summary>A free thrower starts a throw at `aim`; his index, or -1 when none is free.</summary>
        public abstract int StartThrow(Vector3 aim);

        /// <summary>Thrower `index` turns after his goblin, now at `aim`.</summary>
        public void Aim(int index, Vector3 aim)
        {
            var thrower = Crew[index];
            thrower.TargetYaw = YawTo(thrower.Pivot.position, aim);
        }

        protected abstract List<Thrower> Crew { get; }

        /// <summary>Which way thrower `index` faces, in degrees about the vertical.</summary>
        public float FacingYaw(int index) => Crew[index].Yaw;

        /// <summary>Where the shot leaves thrower `index`'s hand now.</summary>
        public abstract Vector3 ReleasePoint(int index);

        /// <summary>Where thrower `index` stands.</summary>
        public abstract Vector3 Feet(int index);

        /// <summary>The crew for this tower, or null when it has none or its models are missing.</summary>
        public static TowerCrewVisual TryCreate(TowerActor tower, Data.TowerDefinition definition, Simulation.PathRoute route)
        {
            if (definition == null)
            {
                return null;
            }

            var toRoad = ToRoad(tower.transform.position, route);
            if (definition.throwsSpears)
            {
                return SpearRockVisual.TryCreate(tower, toRoad);
            }

            return definition.id == "slingers" ? SlingerTowerVisual.TryCreate(tower, toRoad) : null;
        }

        private static Vector3 ToRoad(Vector3 position, Simulation.PathRoute route)
        {
            var toRoad = Vector3.forward;
            if (route != null && route.TotalLength > 0f)
            {
                toRoad = route.GetNearestRoadPoint(position, out _) - position;
            }

            toRoad.y = 0f;
            return toRoad.sqrMagnitude > 1e-6f ? toRoad.normalized : Vector3.forward;
        }

        protected static Shader Skinned => Shader.Find("TowerDefense/StyleSkinned");

        protected static Material Textured(string name, Texture2D texture, float shadowPlane, bool vertexColor = false)
        {
            var material = new Material(Skinned) { name = name };
            if (texture != null)
            {
                material.SetTexture("_MainTex", texture);
            }

            material.SetFloat("_ShadowPlane", shadowPlane);
            material.SetFloat("_VertexColor", vertexColor ? 1f : 0f);
            return material;
        }

        protected static Transform Bone(GameObject rig, string suffix)
        {
            foreach (var bone in rig.GetComponentsInChildren<Transform>())
            {
                if (bone.name.EndsWith(suffix))
                {
                    return bone;
                }
            }

            return null;
        }

        protected static AnimationClip Clip(string path, string part)
        {
            foreach (var clip in Resources.LoadAll<AnimationClip>(path))
            {
                if (!clip.name.StartsWith("__preview__") && clip.name.Contains(part))
                {
                    return clip;
                }
            }

            return null;
        }

        // The lowest and highest point of a rig's skinned meshes in a pose, in the rig's own space.
        protected static (float min, float max) Heights(GameObject rig, AnimationClip clip, float time, System.Func<SkinnedMeshRenderer, bool> include = null)
        {
            clip.SampleAnimation(rig, time);
            var min = float.MaxValue;
            var max = float.MinValue;
            var mesh = new Mesh();
            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (include != null && !include(skin))
                {
                    continue;
                }

                skin.BakeMesh(mesh);
                var local = rig.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                foreach (var vertex in mesh.vertices)
                {
                    var y = local.MultiplyPoint3x4(vertex).y;
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }
            }

            Destroy(mesh);
            return (min, max);
        }

        // One thrower: a pivot on the ground where he stands, turned to his aim, holding the rig.
        protected sealed class Thrower
        {
            public Transform Pivot;
            public GameObject Rig;
            public Vector3 Seat;
            public float Yaw;
            public float TargetYaw;
            public bool Throwing;
            public float Time;
            public float IdleTime;
            // The way he faces at rest, and his own beat for looking about.
            public float RestYaw;
            public float Phase;
        }

        protected Thrower MakeThrower(GameObject prefab, Vector3 seat, float yaw, float fit, float minY, Material body, Material rope)
        {
            var pivot = new GameObject("Thrower").transform;
            pivot.SetParent(transform, false);
            pivot.SetPositionAndRotation(seat, Quaternion.Euler(0f, yaw, 0f));
            pivot.localScale = Vector3.one * fit;
            var rig = Instantiate(prefab, pivot, false);
            rig.transform.localPosition = new Vector3(0f, -minY, 0f);
            foreach (var renderer in rig.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = rope != null && renderer.name.StartsWith("Sling") ? rope : body;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                }
            }

            foreach (var animator in rig.GetComponentsInChildren<Animator>())
            {
                animator.enabled = false;
            }

            return new Thrower { Pivot = pivot, Rig = rig, Seat = seat, Yaw = yaw, TargetYaw = yaw, RestYaw = yaw, Phase = Random.value };
        }

        // The next free thrower after `last`, round the crew; -1 when all are throwing.
        protected static int NextFree(List<Thrower> crew, ref int last)
        {
            for (var step = 1; step <= crew.Count; step++)
            {
                var index = (last + step) % crew.Count;
                if (!crew[index].Throwing)
                {
                    last = index;
                    return index;
                }
            }

            return -1;
        }

        protected static float YawTo(Vector3 from, Vector3 to)
        {
            var direction = to - from;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// The Spear Throwers: the Meshy rock (widened, as approved 2026-11-10) with three hunters on its
    /// flat top. Each throws in turn: he reaches back, draws a spear out of the bundle on his back and
    /// hurls it at the goblin the tower picked; the spear leaves his hand at the release.
    /// </summary>
    public sealed class SpearRockVisual : TowerCrewVisual
    {
        private const float HunterHeight = 1.95f;
        // The 74-frame Cycle clip at 30 fps (build_hunter.py's report): the spear leaves at frame 35.
        private const float CycleFps = 30f;
        private const int ReleaseFrame = 35;
        private const float SpearLength = 2.0f;
        private const float GripFromButt = 0.9f;
        private const float GripUnderHead = 0.45f;
        // TEMPORARY sizes (approved in the capture, 2026-11-10): 4.6 m across, widened 1.8 times.
        private const float RockWidth = 4.6f;
        private const float RockWiden = 1.8f;
        private const float TurnDegreesPerSecond = 540f;
        // Three seats across the flat top: sideways, and towards the road (negative is back).
        private static readonly (float side, float depth)[] Seats = { (-1.0f, -0.45f), (0.3f, -0.05f), (1.6f, -0.45f) };

        private sealed class Kind
        {
            public GameObject Prefab;
            public AnimationClip Cycle;
            public AnimationClip Idle;
            public Material Body;
            public Material Spear;
            public Material Rock;
            public Mesh RockMesh;
            public Mesh SpearMesh;
            public float Fit;
            public float MinY;
            public float FacingOffset;
            public int GrabFrame;
            public Quaternion[] Lies;
            public float[] Helds;
        }

        private static Kind loaded;
        private static bool tried;
        private readonly List<Thrower> crew = new();
        private readonly List<Transform> spears = new();
        private readonly List<Transform> props = new();
        private Kind kind;
        private int last = -1;

        public override float ReleaseLead => ReleaseFrame / CycleFps;

        public static SpearRockVisual TryCreate(TowerActor tower, Vector3 toRoad)
        {
            var kind = Load();
            if (kind == null)
            {
                return null;
            }

            var visual = MakeRoot<SpearRockVisual>(tower, "SpearRock");
            visual.kind = kind;
            visual.Build(tower.transform.position, toRoad);
            visual.FinishBuild();
            return visual;
        }

        private void Build(Vector3 at, Vector3 toRoad)
        {
            var mesh = kind.RockMesh;
            var scale = RockWidth / Mathf.Max(0.01f, Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z));
            at -= toRoad * BackOff(mesh.bounds.size.z * 0.5f * scale * RockWiden);
            var rock = new GameObject("Rock");
            rock.transform.SetParent(transform, false);
            rock.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toRoad, Vector3.up));
            rock.transform.localScale = new Vector3(scale * RockWiden, scale, scale * RockWiden);
            var body = new GameObject("RockMesh");
            body.transform.SetParent(rock.transform, false);
            body.transform.localPosition = new Vector3(-mesh.bounds.center.x, -mesh.bounds.min.y, -mesh.bounds.center.z);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = kind.Rock;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The rock's surface under a point: the highest triangle over it (no physics here).
            var toWorld = body.transform.localToWorldMatrix;
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = toWorld.MultiplyPoint3x4(vertices[i]);
            }

            var triangles = mesh.triangles;
            float TopAt(Vector3 point)
            {
                var top = at.y;
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]];
                    var b = vertices[triangles[i + 1]];
                    var c = vertices[triangles[i + 2]];
                    var d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(d) < 1e-9f)
                    {
                        continue;
                    }

                    var u = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / d;
                    var v = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / d;
                    if (u < 0f || v < 0f || u + v > 1f)
                    {
                        continue;
                    }

                    top = Mathf.Max(top, u * a.y + v * b.y + (1f - u - v) * c.y);
                }

                return top;
            }

            var rockTop = TopAt(at);
            var bodyOnRock = new Material(kind.Body);
            bodyOnRock.SetFloat("_ShadowPlane", Scaled(rockTop, at.y) + 0.02f);
            var spearOnRock = new Material(kind.Spear);
            spearOnRock.SetFloat("_ShadowPlane", Scaled(rockTop, at.y) + 0.02f);
            var across = Vector3.Cross(Vector3.up, toRoad);
            var facing = Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg;
            foreach (var (side, depth) in Seats)
            {
                var seat = at + across * side + toRoad * depth;
                seat.y = TopAt(seat);
                var hunter = MakeThrower(kind.Prefab, seat, facing + kind.FacingOffset, kind.Fit, kind.MinY, bodyOnRock, null);
                hunter.Yaw = hunter.TargetYaw = facing;
                hunter.IdleTime = Random.Range(0f, kind.Idle.length);
                crew.Add(hunter);
                props.Add(Bone(hunter.Rig, "RightHandProp"));
                var spear = new GameObject("Spear");
                spear.transform.SetParent(transform, false);
                spear.AddComponent<MeshFilter>().sharedMesh = kind.SpearMesh;
                var spearRenderer = spear.AddComponent<MeshRenderer>();
                spearRenderer.sharedMaterial = spearOnRock;
                spearRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                spear.SetActive(false);
                spears.Add(spear.transform);
            }
        }

        public override int StartThrow(Vector3 aim)
        {
            var index = NextFree(crew, ref last);
            if (index < 0)
            {
                return -1;
            }

            var hunter = crew[index];
            hunter.Throwing = true;
            hunter.Time = 0f;
            hunter.TargetYaw = YawTo(hunter.Pivot.position, aim);
            return index;
        }

        public override Vector3 ReleasePoint(int index) => props[index] != null ? props[index].position : crew[index].Pivot.position + Vector3.up * 1.9f * VisualScale;

        protected override List<Thrower> Crew => crew;

        public override Vector3 Feet(int index) => crew[index].Pivot.position;

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            for (var i = 0; i < crew.Count; i++)
            {
                var hunter = crew[i];
                hunter.Yaw = Mathf.MoveTowardsAngle(hunter.Yaw, hunter.TargetYaw, TurnDegreesPerSecond * dt);
                hunter.Pivot.localRotation = Quaternion.Euler(0f, hunter.Yaw + kind.FacingOffset, 0f);
                if (hunter.Throwing)
                {
                    hunter.Time += dt;
                    if (hunter.Time >= kind.Cycle.length)
                    {
                        hunter.Throwing = false;
                        hunter.IdleTime = 0f;
                    }
                }

                if (hunter.Throwing)
                {
                    kind.Cycle.SampleAnimation(hunter.Rig, hunter.Time);
                }
                else
                {
                    hunter.IdleTime += dt;
                    kind.Idle.SampleAnimation(hunter.Rig, Mathf.Repeat(hunter.IdleTime, kind.Idle.length));
                }

                // The spear in his hand from the grab until the release.
                var frame = hunter.Throwing ? hunter.Time * CycleFps : -1f;
                var spear = spears[i];
                var held = frame >= kind.GrabFrame && frame < ReleaseFrame && props[i] != null;
                if (spear.gameObject.activeSelf != held)
                {
                    spear.gameObject.SetActive(held);
                }

                if (!held)
                {
                    continue;
                }

                var at = Mathf.FloorToInt(frame);
                var step = frame - at;
                var rotation = Quaternion.Euler(0f, hunter.Yaw + kind.FacingOffset, 0f) * Quaternion.Slerp(kind.Lies[at], kind.Lies[at + 1], step);
                var grip = Mathf.Lerp(kind.Helds[at], kind.Helds[at + 1], step);
                spear.SetPositionAndRotation(props[i].position - rotation * Vector3.forward * (grip * VisualScale), rotation);
            }
        }

        private static Kind Load()
        {
            if (tried)
            {
                return loaded;
            }

            tried = true;
            var prefab = Resources.Load<GameObject>(Mixamo + "spearhunter");
            var rockPrefab = Resources.Load<GameObject>(Meshy + "spearrock");
            var cycle = Clip(Mixamo + "spearhunter", "Cycle");
            var idle = Clip(Mixamo + "spearhunter", "Idle");
            if (prefab == null || rockPrefab == null || cycle == null || idle == null || Skinned == null)
            {
                return null;
            }

            var kind = new Kind
            {
                Prefab = prefab,
                Cycle = cycle,
                Idle = idle,
                Body = Textured("SpearHunter", Resources.Load<Texture2D>(Meshy + "spearhunter-tex"), 0f),
                Spear = Textured("Spear", null, 0f, true),
                Rock = Textured("SpearRock", Resources.Load<Texture2D>(Meshy + "spearrock-tex"), 0f),
                RockMesh = Upright(rockPrefab),
                SpearMesh = BuildSpear()
            };

            // Measured on a probe rig standing at the origin, as the capture test does.
            var rig = Instantiate(prefab);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var (minY, maxY) = Heights(rig, idle, 0f);
            kind.MinY = minY;
            kind.Fit = HunterHeight / Mathf.Max(0.01f, maxY - minY);
            var prop = Bone(rig, "RightHandProp");
            var hand = Bone(rig, "RightHand");
            var bundleBase = Bone(rig, "SpearBundle");
            var bundleTop = Bone(rig, "SpearBundleTip");
            if (prop == null || hand == null || bundleBase == null || bundleTop == null)
            {
                Destroy(rig);
                return null;
            }

            // A spear thrower stands side-on: "facing" is the yaw that turns the hand's flight at the
            // release onto +Z.
            cycle.SampleAnimation(rig, (ReleaseFrame - 1) / CycleFps);
            var before = prop.position;
            cycle.SampleAnimation(rig, (ReleaseFrame + 1) / CycleFps);
            var swing = (prop.position - before).normalized;
            kind.FacingOffset = -Mathf.Atan2(swing.x, swing.z) * Mathf.Rad2Deg;
            var spearAxis = Vector3.forward;
            var best = float.MinValue;
            foreach (var axis in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                var dot = Vector3.Dot(prop.TransformDirection(axis), swing);
                if (dot > best)
                {
                    best = dot;
                    spearAxis = axis;
                }
            }

            // The grab: where the empty hand comes closest to the bundle's top; then the spear slides
            // out along the bundle while the hand still rises along it.
            int grabFrame = 0, pulledFrame = 0;
            var closest = float.MaxValue;
            for (var frame = 0; frame < ReleaseFrame - 8; frame++)
            {
                cycle.SampleAnimation(rig, frame / CycleFps);
                var distance = Vector3.Distance(prop.position, bundleTop.position);
                if (distance < closest)
                {
                    closest = distance;
                    grabFrame = frame;
                }
            }

            var highest = float.MinValue;
            for (var frame = grabFrame; frame < ReleaseFrame - 8; frame++)
            {
                cycle.SampleAnimation(rig, frame / CycleFps);
                var along = Vector3.Dot(prop.position - bundleTop.position, (bundleTop.position - bundleBase.position).normalized);
                if (along <= highest)
                {
                    break;
                }

                highest = along;
                pulledFrame = frame;
            }

            Vector3 AxisAt(int frame)
            {
                cycle.SampleAnimation(rig, frame / CycleFps);
                return prop.TransformDirection(spearAxis).normalized;
            }

            Quaternion Lie(Vector3 along)
            {
                var up = Vector3.ProjectOnPlane(hand.up, along);
                if (Vector3.Dot(up, Vector3.up) < 0f)
                {
                    up = -up;
                }

                return Quaternion.LookRotation(along, up.sqrMagnitude < 1e-6f ? Vector3.up : up);
            }

            var settleFrame = pulledFrame + 1;
            while (settleFrame < ReleaseFrame - 2 && Vector3.Angle(AxisAt(settleFrame), AxisAt(settleFrame + 1)) > 3f)
            {
                settleFrame++;
            }

            kind.GrabFrame = grabFrame;
            kind.Lies = new Quaternion[ReleaseFrame + 2];
            kind.Helds = new float[ReleaseFrame + 2];
            cycle.SampleAnimation(rig, pulledFrame / CycleFps);
            var drawnLie = Lie((bundleTop.position - bundleBase.position).normalized);
            AxisAt(settleFrame);
            var aimLie = Lie(prop.TransformDirection(spearAxis));
            for (var frame = 0; frame <= ReleaseFrame + 1; frame++)
            {
                cycle.SampleAnimation(rig, frame / CycleFps);
                if (frame <= pulledFrame)
                {
                    kind.Lies[frame] = Lie((bundleTop.position - bundleBase.position).normalized);
                    kind.Helds[frame] = SpearLength - GripUnderHead;
                }
                else if (frame < settleFrame)
                {
                    var turn = Mathf.SmoothStep(0f, 1f, (frame - pulledFrame) / (float)(settleFrame - pulledFrame));
                    kind.Lies[frame] = Quaternion.Slerp(drawnLie, aimLie, turn);
                    kind.Helds[frame] = Mathf.Lerp(SpearLength - GripUnderHead, GripFromButt, turn);
                }
                else
                {
                    kind.Lies[frame] = Lie(prop.TransformDirection(spearAxis));
                    kind.Helds[frame] = GripFromButt;
                }
            }

            // The probe's lies are in its own scale: the grip distances are world metres, as drawn.
            Destroy(rig);
            loaded = kind;
            return kind;
        }

        // A Meshy model's mesh with its node and root transforms baked in, so it stands upright.
        internal static Mesh Upright(GameObject prefab)
        {
            Mesh source;
            Transform node;
            var filter = prefab.GetComponentInChildren<MeshFilter>();
            if (filter != null)
            {
                source = filter.sharedMesh;
                node = filter.transform;
            }
            else
            {
                var skinned = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
                source = skinned.sharedMesh;
                node = skinned.transform;
            }

            // Keep the root's own rotation: a one-mesh Meshy FBX imports with its Z-up to Y-up turn
            // on the root itself, and dropping it laid the rock on its side.
            var toRoot = Matrix4x4.Translate(-prefab.transform.position) * node.localToWorldMatrix;
            var mesh = Instantiate(source);
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = toRoot.MultiplyPoint3x4(vertices[i]);
            }

            mesh.vertices = vertices;
            if (toRoot.determinant < 0f)
            {
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var triangles = mesh.GetTriangles(sub);
                    for (var i = 0; i < triangles.Length; i += 3)
                    {
                        (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                    }

                    mesh.SetTriangles(triangles, sub);
                }
            }

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        // A plain spear: a hexagonal wooden shaft and a knapped stone tip, butt at the origin, along +Z.
        private static Mesh BuildSpear()
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var wood = new Color(0.55f, 0.38f, 0.22f);
            var stone = new Color(0.62f, 0.6f, 0.56f);
            var binding = new Color(0.36f, 0.24f, 0.15f);

            void Ring(float z0, float z1, float r0, float r1, Color color, int sides)
            {
                for (var i = 0; i < sides; i++)
                {
                    var a0 = i * Mathf.PI * 2f / sides;
                    var a1 = (i + 1) * Mathf.PI * 2f / sides;
                    var b = vertices.Count;
                    vertices.Add(new Vector3(Mathf.Cos(a0) * r0, Mathf.Sin(a0) * r0, z0));
                    vertices.Add(new Vector3(Mathf.Cos(a1) * r0, Mathf.Sin(a1) * r0, z0));
                    vertices.Add(new Vector3(Mathf.Cos(a1) * r1, Mathf.Sin(a1) * r1, z1));
                    vertices.Add(new Vector3(Mathf.Cos(a0) * r1, Mathf.Sin(a0) * r1, z1));
                    colors.AddRange(new[] { color, color, color, color });
                    triangles.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
            }

            const float shaft = SpearLength - 0.2f;
            Ring(0f, shaft, 0.02f, 0.017f, wood, 6);
            Ring(shaft - 0.08f, shaft, 0.024f, 0.024f, binding, 6);
            Ring(shaft - 0.02f, shaft + 0.06f, 0.02f, 0.05f, stone, 4);
            Ring(shaft + 0.06f, SpearLength, 0.05f, 0.0f, stone, 4);
            var mesh = new Mesh { name = "Spear" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// The Slingers: the approved one-deck tipi with eight slingers in an arc on its deck (user,
    /// 2026-11-07). They throw in turn: each whirls the sling over his head and lets the stone go at
    /// the goblin the tower picked; the stone leaves the sling at the release.
    /// </summary>
    public sealed class SlingerTowerVisual : TowerCrewVisual
    {
        private const float SlingerHeight = 1.95f;
        // The throw's cycle and the stone's release in it (approved 2026-11-06).
        private const float Period = 2.6f;
        private const float Release = 1.45f;
        private const float DeckHeight = 2.5f;
        private const float TurnDegreesPerSecond = 900f;
        // Waiting: a 3.4 s sway through the throw's first 0.15 s, and a 9 s look 25 degrees each way.
        private const float IdleSwaySeconds = 3.4f;
        private const float IdleSwayDepth = 0.15f;
        private const float IdleLookSeconds = 9f;
        private const float IdleLookDegrees = 25f;
        // Seats on the deck in the tipi's frame (270 degrees: the ladder, towards the road), picked so
        // the feet stay on the planks through the whole throw (Tools/Animation/meshy/footing.py).
        private static readonly (float angle, float radius, float height)[] Seats =
        {
            (165f, 2.4f, 2.54f), (190f, 2.05f, 2.52f), (222.5f, 2.15f, 2.54f), (250f, 2.05f, 2.53f),
            (297.5f, 2.05f, 2.54f), (325f, 2.2f, 2.54f), (352.5f, 2.05f, 2.53f), (17.5f, 2.3f, 2.53f),
        };

        private sealed class Kind
        {
            public GameObject Prefab;
            public AnimationClip Throw;
            public Texture2D Texture;
            public Mesh Tipi;
            public Material TipiMaterial;
            public float Fit;
            public float MinY;
        }

        private static Kind loaded;
        private static bool tried;
        private readonly List<Thrower> crew = new();
        private readonly List<Transform> stones = new();
        private Kind kind;
        private int last = -1;

        public override float ReleaseLead => Release;

        public static SlingerTowerVisual TryCreate(TowerActor tower, Vector3 toRoad)
        {
            var kind = Load();
            if (kind == null)
            {
                return null;
            }

            var visual = MakeRoot<SlingerTowerVisual>(tower, "SlingerTower");
            visual.kind = kind;
            visual.Build(tower.transform.position, toRoad);
            visual.FinishBuild();
            return visual;
        }

        private void Build(Vector3 at, Vector3 toRoad)
        {
            // The ladder (the tipi's -Z) faces the road.
            at -= toRoad * BackOff(-kind.Tipi.bounds.min.z);
            var place = Matrix4x4.TRS(at, Quaternion.LookRotation(-toRoad, Vector3.up), Vector3.one);
            var tipi = new GameObject("Tipi");
            tipi.transform.SetParent(transform, false);
            tipi.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-toRoad, Vector3.up));
            tipi.AddComponent<MeshFilter>().sharedMesh = kind.Tipi;
            var renderer = tipi.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = kind.TipiMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var deck = Scaled(at.y + DeckHeight, at.y) + 0.04f;
            var body = Textured("Slinger", kind.Texture, deck);
            var rope = Textured("Sling", kind.Texture, deck, true);
            var facing = Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg;
            foreach (var (angle, radius, height) in Seats)
            {
                var outward = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
                var seat = place.MultiplyPoint3x4(outward * radius + Vector3.up * height);
                var slinger = MakeThrower(kind.Prefab, seat, facing, kind.Fit, kind.MinY, body, rope);
                crew.Add(slinger);
                stones.Add(Bone(slinger.Rig, "SlingStone"));
            }
        }

        public override int StartThrow(Vector3 aim)
        {
            var index = NextFree(crew, ref last);
            if (index < 0)
            {
                return -1;
            }

            var slinger = crew[index];
            slinger.Throwing = true;
            slinger.Time = 0f;
            slinger.TargetYaw = YawTo(slinger.Pivot.position, aim);
            return index;
        }

        public override Vector3 ReleasePoint(int index) => stones[index] != null ? stones[index].position : crew[index].Pivot.position + Vector3.up * 1.6f * VisualScale;

        protected override List<Thrower> Crew => crew;

        public override Vector3 Feet(int index) => crew[index].Pivot.position;

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            foreach (var slinger in crew)
            {
                // He turns to his goblin while he raises the sling.
                // (Waiting, he turns back slowly.)
                slinger.Yaw = Mathf.MoveTowardsAngle(slinger.Yaw, slinger.TargetYaw, (slinger.Throwing ? TurnDegreesPerSecond : 120f) * dt);
                slinger.Pivot.localRotation = Quaternion.Euler(0f, slinger.Yaw, 0f);
                if (slinger.Throwing)
                {
                    slinger.Time += dt;
                    if (slinger.Time >= Period)
                    {
                        slinger.Throwing = false;
                        slinger.IdleTime = 0f;
                    }
                }

                // Between throws he waits ready (user, 2026-11-12): he shifts his weight back and
                // forth in the throw's first moments, and looks slowly up and down the road.
                float time;
                if (slinger.Throwing)
                {
                    time = slinger.Time;
                }
                else
                {
                    slinger.IdleTime += dt;
                    var beat = slinger.IdleTime / IdleSwaySeconds + slinger.Phase;
                    time = IdleSwayDepth * (0.5f - 0.5f * Mathf.Cos(beat * Mathf.PI * 2f));
                    var look = Mathf.Sin((slinger.IdleTime / IdleLookSeconds + slinger.Phase) * Mathf.PI * 2f);
                    slinger.TargetYaw = slinger.RestYaw + IdleLookDegrees * look;
                }

                kind.Throw.SampleAnimation(slinger.Rig, time / Period * kind.Throw.length);
            }
        }

        private static Kind Load()
        {
            if (tried)
            {
                return loaded;
            }

            tried = true;
            var prefab = Resources.Load<GameObject>(Mixamo + "stonehide");
            var hurl = Clip(Mixamo + "stonehide", "Throw");
            var walk = Clip(Mixamo + "stonehide", "Walking") ?? hurl;
            var tipi = Resources.Load<TextAsset>(Meshy + "slinger-tower");
            if (prefab == null || hurl == null || tipi == null || Skinned == null)
            {
                return null;
            }

            var kind = new Kind
            {
                Prefab = prefab,
                Throw = hurl,
                Texture = Resources.Load<Texture2D>(Meshy + "stonehide-tex"),
                Tipi = Triangles(tipi.text),
                TipiMaterial = Textured("Tipi", null, 0f, true)
            };

            // Fitted to 1.95 m in the walk's first pose, the slings left out, as in the approved capture.
            var rig = Instantiate(prefab);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var (minY, maxY) = Heights(rig, walk, 0f, skin => !skin.name.StartsWith("Sling"));
            Destroy(rig);
            kind.MinY = minY;
            kind.Fit = SlingerHeight / Mathf.Max(0.01f, maxY - minY);
            loaded = kind;
            return kind;
        }

        // Triangles baked by a Blender script, one per line: 'ax ay az bx by bz cx cy cz nx ny nz r g b'.
        private static Mesh Triangles(string text)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var culture = CultureInfo.InvariantCulture;
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split(' ');
                if (parts.Length < 15)
                {
                    continue;
                }

                var v = new float[15];
                for (var i = 0; i < 15; i++)
                {
                    v[i] = float.Parse(parts[i], culture);
                }

                var a = new Vector3(v[0], v[1], v[2]);
                var b = new Vector3(v[3], v[4], v[5]);
                var c = new Vector3(v[6], v[7], v[8]);
                var facing = new Vector3(v[9], v[10], v[11]);
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                if (Vector3.Dot(normal, facing) < 0f)
                {
                    (b, c) = (c, b);
                    normal = -normal;
                }

                normal.Normalize();
                var start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                var color = new Color(v[12], v[13], v[14], 1f);
                for (var i = 0; i < 3; i++)
                {
                    normals.Add(normal);
                    colors.Add(color);
                }

                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            var mesh = new Mesh { name = "Tipi", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
