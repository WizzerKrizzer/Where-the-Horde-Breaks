using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// The spear hunter (Meshy model, Mixamo rig, Kevin Iglesias throw retargeted by
    /// Tools/Animation/meshy/build_hunter.py) on the Meshy rock, throwing spears by hand at the
    /// style crowd. The build mirrors the model (variant A, chosen 2026-11-10), so the bundle is
    /// over the right shoulder and the hunter throws right-handed. Writes Logs/Captures/SpearHunter.
    /// </summary>
    public sealed class SpearHunterCaptureTests
    {
        private const string Output = "Logs/Captures/SpearHunter";
        private const float HunterHeight = 1.95f;
        // Markers of the 74-frame Cycle clip at 30 fps, from build_hunter.py's report.
        private const float CycleFps = 30f;
        private const int ReleaseFrame = 35;
        private const float SpearLength = 2.0f;
        // The hand holds the spear a little behind its middle once it is out; in the bundle it
        // takes it just under the head.
        private const float GripFromButt = 0.9f;
        private const float GripUnderHead = 0.45f;
        private const float SpearSpeed = 24f;
        // The rock's size as Meshy made it, scaled to 4.6 m across (about 3 m high)...
        private const float RockWidth = 4.6f;
        // ...then widened, not raised: TEMPORARY, the 2.2 m flat top crowded three throwers (user,
        // 2026-11-10). Makes the rock about 8.3 m across with a 4 m flat top.
        private const float RockWiden = 1.8f;
        // Each hunter covers its own slice of the field, measured from the road direction (degrees,
        // right is positive), so their spears do not cross. The slices overlap a little.
        private static readonly (float from, float to)[] Sectors = { (-45f, -10f), (-15f, 15f), (10f, 45f) };

        private sealed class Flight
        {
            public Vector3 From;
            public Vector3 To;
            public float Start;
            public float Duration;
        }

        [UnityTest, Explicit("Records the spear hunter on its rock.")]
        public IEnumerator RecordSpearHunter()
        {
            var skinnedShader = Shader.Find("TowerDefense/StyleSkinned");
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            Assert.That(skinnedShader, Is.Not.Null);
            var prefab = Resources.Load<GameObject>("Mixamo/spearhunter");
            if (prefab == null) Assert.Ignore("Mixamo/spearhunter.fbx is missing. Build it with Tools/Animation/build_spear_hunter.sh.");
            AnimationClip cycle = null, idle = null;
            foreach (var clip in Resources.LoadAll<AnimationClip>("Mixamo/spearhunter"))
            {
                if (clip.name.StartsWith("__preview__")) continue;
                if (clip.name.EndsWith("Cycle")) cycle = clip;
                if (clip.name.EndsWith("Idle")) idle = clip;
            }

            Assert.That(cycle, Is.Not.Null);
            Assert.That(idle, Is.Not.Null);
            var cycleLength = cycle.length;

            var rig = Object.Instantiate(prefab);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var renderer in rig.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            Transform Bone(string name)
            {
                foreach (var bone in rig.GetComponentsInChildren<Transform>())
                {
                    if (bone.name.EndsWith(name)) return bone;
                }

                Assert.Fail("No bone " + name);
                return null;
            }

            var prop = Bone("RightHandProp");
            var hand = Bone("RightHand");
            var bundleBase = Bone("SpearBundle");
            var bundleTop = Bone("SpearBundleTip");
            var pool = new List<Mesh>();
            var used = 0;

            List<(Mesh mesh, Matrix4x4 local)> Bake(AnimationClip clip, float time)
            {
                clip.SampleAnimation(rig, time);
                var baked = new List<(Mesh, Matrix4x4)>();
                foreach (var skin in skins)
                {
                    if (used == pool.Count) pool.Add(new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
                    var mesh = pool[used++];
                    skin.BakeMesh(mesh);
                    baked.Add((mesh, skin.transform.localToWorldMatrix));
                }

                return baked;
            }

            // Standing height and facing from the idle's first pose.
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var (probe, local) in Bake(idle, 0f))
            {
                foreach (var vertex in probe.vertices)
                {
                    var y = local.MultiplyPoint3x4(vertex).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            used = 0;
            var fit = HunterHeight / (maxY - minY);

            // The spear lies along the prop bone axis that points where the hand flies at release.
            cycle.SampleAnimation(rig, (ReleaseFrame - 1) / CycleFps);
            var before = prop.position;
            cycle.SampleAnimation(rig, (ReleaseFrame + 1) / CycleFps);
            var swing = (prop.position - before).normalized;
            // A spear thrower stands side-on, so "facing" here is the throw: the yaw that turns the
            // hand's flight at release onto +Z.
            var facingOffset = -Mathf.Atan2(swing.x, swing.z) * Mathf.Rad2Deg;
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

            // The grab is where the empty hand comes closest to the bundle's top; the spear then
            // slides out along the bundle while the hand still rises along it.
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
                if (along <= highest) break;
                highest = along;
                pulledFrame = frame;
            }

            // The spear's lie in the rig, per cycle frame from the grab to the release. Drawn out, it
            // keeps the bundle's line; then it turns straight into the aim's grip. The source hand
            // prop rolls tip-down in between (a sword's draw), so the hand's own axis is used only
            // once it has settled in the aim.
            var settleFrame = pulledFrame + 1;
            Vector3 AxisAt(int frame)
            {
                cycle.SampleAnimation(rig, frame / CycleFps);
                return prop.TransformDirection(spearAxis).normalized;
            }

            while (settleFrame < ReleaseFrame - 2 && Vector3.Angle(AxisAt(settleFrame), AxisAt(settleFrame + 1)) > 3f) settleFrame++;
            var lies = new Quaternion[ReleaseFrame + 1];
            var helds = new float[ReleaseFrame + 1];
            Quaternion Lie(Vector3 along)
            {
                var up = Vector3.ProjectOnPlane(hand.up, along);
                if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
                return Quaternion.LookRotation(along, up.sqrMagnitude < 1e-6f ? Vector3.up : up);
            }

            cycle.SampleAnimation(rig, pulledFrame / CycleFps);
            var drawnLie = Lie((bundleTop.position - bundleBase.position).normalized);
            AxisAt(settleFrame);
            var aimLie = Lie(prop.TransformDirection(spearAxis));
            for (var frame = grabFrame; frame <= ReleaseFrame; frame++)
            {
                cycle.SampleAnimation(rig, frame / CycleFps);
                if (frame <= pulledFrame)
                {
                    lies[frame] = Lie((bundleTop.position - bundleBase.position).normalized);
                    helds[frame] = SpearLength - GripUnderHead;
                }
                else if (frame < settleFrame)
                {
                    var turn = Mathf.SmoothStep(0f, 1f, (frame - pulledFrame) / (float)(settleFrame - pulledFrame));
                    lies[frame] = Quaternion.Slerp(drawnLie, aimLie, turn);
                    helds[frame] = Mathf.Lerp(SpearLength - GripUnderHead, GripFromButt, turn);
                }
                else
                {
                    lies[frame] = Lie(prop.TransformDirection(spearAxis));
                    helds[frame] = GripFromButt;
                }
            }

            var spearMesh = BuildSpear();
            var rockPrefab = Resources.Load<GameObject>("spearrock");
            if (rockPrefab == null) Assert.Ignore("spearrock.fbx is missing (a local Meshy model). Install it with Tools/Animation/build_spear_hunter.sh.");
            var rockMesh = Upright(rockPrefab);
            var rockScale = RockWidth / Mathf.Max(rockMesh.bounds.size.x, rockMesh.bounds.size.z);

            Material Textured(string texture, float shadowPlane)
            {
                var material = new Material(skinnedShader);
                material.SetTexture("_MainTex", Resources.Load<Texture2D>(texture));
                material.SetFloat("_ShadowPlane", shadowPlane);
                return material;
            }

            Material Coloured(float shadowPlane)
            {
                var material = new Material(skinnedShader);
                material.SetFloat("_VertexColor", 1f);
                material.SetFloat("_ShadowPlane", shadowPlane);
                return material;
            }

            File.WriteAllText(Directory.CreateDirectory(Output).FullName + "/report.txt",
                $"hunter: height {maxY - minY:F3} -> scale {fit:F3}, facing offset {facingOffset:F0}, spear axis {spearAxis} (dot {best:F2}), " +
                $"grab frame {grabFrame} ({closest / (maxY - minY) * HunterHeight:F2} m from the bundle top), pulled out at {pulledFrame}, in the aim at {settleFrame}, cycle {cycleLength:F2} s, idle {idle.length:F2} s; rock {rockMesh.bounds.size} x {rockScale:F2}\n");

            foreach (var other in Camera.allCameras) other.enabled = false;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;
            var cameraObject = new GameObject("Hunter camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = StyleLowPolyTests.Ground;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2000f;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            camera.targetTexture = target;
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            var groundMesh = new Mesh
            {
                vertices = new[] { new Vector3(-1000f, 0f, -1000f), new Vector3(-1000f, 0f, 1000f), new Vector3(1000f, 0f, 1000f), new Vector3(1000f, 0f, -1000f) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var groundMaterial = new Material(groundShader);
            groundMaterial.SetColor("_Color", StyleLowPolyTests.Ground);
            var unitMaterial = new Material(unitShader) { enableInstancing = true };
            var staticMaterial = new Material(unitShader) { enableInstancing = true };
            staticMaterial.SetFloat("_Animate", 0f);

            // The rock: placed on the first tower spot, front to the road; feet heights by raycast.
            var towerPositions = StyleLowPolyTests.TowerPositions();
            var rockAt = towerPositions[0];
            var rockYaw = rockAt.z > StyleLowPolyTests.RoadCenter(rockAt.x) ? 180f : 0f;
            var rockMatrix = Matrix4x4.TRS(rockAt, Quaternion.Euler(0f, rockYaw, 0f), new Vector3(rockScale * RockWiden, rockScale, rockScale * RockWiden)) *
                             Matrix4x4.Translate(new Vector3(-rockMesh.bounds.center.x, -rockMesh.bounds.min.y, -rockMesh.bounds.center.z));
            // The rock's surface height under a point: the highest triangle over it (no physics here).
            var rockVertices = rockMesh.vertices;
            for (var i = 0; i < rockVertices.Length; i++) rockVertices[i] = rockMatrix.MultiplyPoint3x4(rockVertices[i]);
            var rockTriangles = rockMesh.triangles;
            float TopAt(Vector3 point)
            {
                var top = 0f;
                for (var i = 0; i < rockTriangles.Length; i += 3)
                {
                    var a = rockVertices[rockTriangles[i]];
                    var b = rockVertices[rockTriangles[i + 1]];
                    var c = rockVertices[rockTriangles[i + 2]];
                    var d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(d) < 1e-9f) continue;
                    var u = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / d;
                    var v = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / d;
                    if (u < 0f || v < 0f || u + v > 1f) continue;
                    top = Mathf.Max(top, u * a.y + v * b.y + (1f - u - v) * c.y);
                }

                return top;
            }
            var rockTop = TopAt(rockAt);
            // Three seats across the plateau's flat top, about
            // 1.3 m apart, left to right, the outer two a little back. Their sectors stay within 45°
            // of the road direction, since a wider turn carried their lunge over the side rim.
            var toRoad = rockYaw == 180f ? Vector3.back : Vector3.forward;
            var across = Vector3.Cross(Vector3.up, toRoad);
            var seats = new List<Vector3>();
            foreach (var (side, depth) in new[] { (-1.0f, -0.45f), (0.3f, -0.05f), (1.6f, -0.45f) })
            {
                var seat = rockAt + across * side + toRoad * depth;
                seat.y = TopAt(seat);
                seats.Add(seat);
                File.AppendAllText(Output + "/report.txt", $"seat {side:F2},{depth:F2}: {seat.y - rockTop:F2} m from the top" + System.Environment.NewLine);
            }

            var bodyGround = Textured("spearhunter-tex", 0f);
            var bodyRock = Textured("spearhunter-tex", rockTop + 0.02f);
            var rockMaterial = Textured("spearrock-tex", 0f);
            var spearGround = Coloured(0f);
            var spearRock = Coloured(rockTop + 0.02f);

            var goblin = LowPolyMeshes.Goblin();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, new List<Vector3> { rockAt });
            var crowd = StyleLowPolyTests.BuildCrowd();
            for (var i = 0; i < crowd.Count; i++)
            {
                var walker = crowd[i];
                walker.Scale *= 0.7f;
                crowd[i] = walker;
            }

            // One hunter: the body, and the spear from the grab until the release. The hand reaches
            // back empty, takes a spear under its head and draws it up out of the bundle along the
            // bundle's line, then turns it into the throwing grip at its balance point.
            void DrawHunter(AnimationClip clip, float time, Vector3 position, float yaw, bool onRock)
            {
                var placement = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw + facingOffset, 0f), Vector3.one * fit) *
                                Matrix4x4.Translate(new Vector3(0f, -minY, 0f));
                foreach (var (mesh, local) in Bake(clip, time))
                {
                    Graphics.DrawMesh(mesh, placement * local, onRock ? bodyRock : bodyGround, 0);
                }

                var cycleFrame = clip == cycle ? Mathf.Repeat(time, cycleLength) * CycleFps : -1f;
                if (cycleFrame < grabFrame || cycleFrame >= ReleaseFrame) return;
                var at = Mathf.FloorToInt(cycleFrame);
                var step = cycleFrame - at;
                var rotation = Quaternion.Euler(0f, yaw + facingOffset, 0f) * Quaternion.Slerp(lies[at], lies[at + 1], step);
                var held = Mathf.Lerp(helds[at], helds[at + 1], step);
                var grip = placement.MultiplyPoint3x4(prop.position);
                Graphics.DrawMesh(spearMesh, Matrix4x4.TRS(grip - rotation * Vector3.forward * held, rotation, Vector3.one), onRock ? spearRock : spearGround, 0);
            }

            // Close up: the throw seen from the throwing side and from behind the bundle, and one at rest.
            {
                var folder = Output + "/close";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, 4.5f, new Vector3(0f, 0f, 0.4f));
                for (var frame = 0; frame < 150; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    DrawHunter(cycle, time, new Vector3(-2.2f, 0f, 0f), 250f, false);
                    DrawHunter(cycle, time, new Vector3(0.6f, 0f, 0f), 160f, false);
                    DrawHunter(idle, time, new Vector3(3f, 0f, 0.8f), 200f, false);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                    used = 0;
                }
            }

            // A low side camera on one throw, to check the empty reach and the pull from the bundle:
            // seen from the throwing arm's side and from behind.
            {
                var folder = Output + "/side";
                Directory.CreateDirectory(folder);
                camera.transform.SetPositionAndRotation(new Vector3(0f, 2.4f, -12f), Quaternion.Euler(8f, 0f, 0f));
                camera.orthographicSize = 1.7f;
                for (var frame = 0; frame < 74; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    DrawHunter(cycle, time, new Vector3(-1.5f, 0f, 0f), 90f, false);
                    DrawHunter(cycle, time, new Vector3(1.5f, 0f, 0f), 0f, false);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                    used = 0;
                }
            }

            // On the rock against the horde, from the gameplay camera: each hunter turns to a goblin
            // when it draws, throws at where that goblin will be, and the spear sticks in the ground.
            var flights = new List<Flight>();
            foreach (var variant in new[] { "a" })
            {
                foreach (var (height, frames) in new[] { (11f, 180), (16f, 150), (40f, 180) })
                {
                    var folder = $"{Output}/rock-{variant}-{height:F0}m";
                    Directory.CreateDirectory(folder);
                    StyleLowPolyTests.PlaceCamera(camera, height, rockAt + new Vector3(0f, 0f, -height * 0.14f));
                    flights.Clear();
                    var yaws = new float[seats.Count];
                    var cycleIndex = new int[seats.Count];
                    for (var s = 0; s < seats.Count; s++)
                    {
                        cycleIndex[s] = -1;
                        yaws[s] = Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg;
                    }

                    for (var frame = 0; frame < frames; frame++)
                    {
                        var time = frame / 30f;
                        Shader.SetGlobalFloat("_StyleTime", time);
                        Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                        Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                        Graphics.DrawMesh(rockMesh, rockMatrix, rockMaterial, 0);
                        StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, time);
                        for (var s = 0; s < seats.Count; s++)
                        {
                            var local = time + s * cycleLength / seats.Count;
                            var index = Mathf.FloorToInt(local / cycleLength);
                            var phase = Mathf.Repeat(local, cycleLength);
                            if (index != cycleIndex[s])
                            {
                                // A new throw: pick the nearest goblin and lead it to the impact.
                                cycleIndex[s] = index;
                                var releaseAt = time + (ReleaseFrame / CycleFps - phase);
                                var aim = NearestWalker(crowd, seats[s], time, rockAt, toRoad, Sectors[s]);
                                var impact = StyleLowPolyTests.WalkerPosition(aim, releaseAt + Vector3.Distance(seats[s], StyleLowPolyTests.WalkerPosition(aim, releaseAt)) / SpearSpeed);
                                var toward = impact - seats[s];
                                yaws[s] = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                                flights.Add(new Flight
                                {
                                    From = seats[s] + Vector3.up * 1.9f,
                                    To = impact,
                                    Start = releaseAt,
                                    Duration = Vector3.Distance(seats[s], impact) / SpearSpeed
                                });
                            }

                            DrawHunter(cycle, phase, seats[s], yaws[s], true);
                        }

                        foreach (var flight in flights)
                        {
                            var t = (time - flight.Start) / flight.Duration;
                            if (t < 0f || t > 1f + 1.5f / flight.Duration) continue;
                            var stuck = t >= 1f;
                            t = Mathf.Min(t, 1f);
                            var flat = flight.To - flight.From;
                            flat.y = 0f;
                            var arc = flat.magnitude * 0.06f;
                            var point = Vector3.Lerp(flight.From, flight.To, t) + Vector3.up * (4f * arc * t * (1f - t));
                            var slope = (flight.To.y - flight.From.y) + 4f * arc * (1f - 2f * t);
                            var heading = (flat + Vector3.up * slope).normalized;
                            if (stuck) heading = (flat.normalized + Vector3.down * 0.55f).normalized;
                            var tip = stuck ? flight.To + heading * 0.35f : point;
                            Graphics.DrawMesh(spearMesh, Matrix4x4.TRS(tip - heading * SpearLength, Quaternion.LookRotation(heading), Vector3.one), spearGround, 0);
                        }

                        yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                        used = 0;
                    }
                }
            }

            camera.targetTexture = null;
            target.Release();
            foreach (var item in new Object[] { target, texture, cameraObject, groundMesh, groundMaterial, unitMaterial, staticMaterial, bodyGround, bodyRock,
                         rockMaterial, spearGround, spearRock, spearMesh, goblin, terrain, rig, rockMesh })
            {
                Object.DestroyImmediate(item);
            }

            foreach (var mesh in pool) Object.DestroyImmediate(mesh);
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

            // Keep the root's own rotation: a one-mesh Meshy FBX imports with its Z-up to Y-up
            // turn on the root itself, and dropping it laid the rock on its side.
            var toRoot = Matrix4x4.Translate(-prefab.transform.position) * node.localToWorldMatrix;
            var mesh = Object.Instantiate(source);
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++) vertices[i] = toRoot.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            if (toRoot.determinant < 0f)
            {
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var triangles = mesh.GetTriangles(sub);
                    for (var i = 0; i < triangles.Length; i += 3) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                    mesh.SetTriangles(triangles, sub);
                }
            }

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        // The nearest goblin whose bearing from the rock lies in the hunter's sector; the nearest
        // overall when the sector is empty.
        private static StyleLowPolyTests.Walker NearestWalker(List<StyleLowPolyTests.Walker> crowd, Vector3 from, float time, Vector3 rock,
            Vector3 toRoad, (float from, float to) sector)
        {
            StyleLowPolyTests.Walker best = crowd[0], fallback = crowd[0];
            float bestDistance = float.MaxValue, fallbackDistance = float.MaxValue;
            foreach (var walker in crowd)
            {
                var position = StyleLowPolyTests.WalkerPosition(walker, time);
                var distance = (position - from).sqrMagnitude;
                if (distance < fallbackDistance)
                {
                    fallbackDistance = distance;
                    fallback = walker;
                }

                var flat = position - rock;
                flat.y = 0f;
                var bearing = Vector3.SignedAngle(toRoad, flat, Vector3.up);
                if (bearing < sector.from || bearing > sector.to) continue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = walker;
                }
            }

            return bestDistance < float.MaxValue ? best : fallback;
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
                    var p = new[]
                    {
                        new Vector3(Mathf.Cos(a0) * r0, Mathf.Sin(a0) * r0, z0), new Vector3(Mathf.Cos(a1) * r0, Mathf.Sin(a1) * r0, z0),
                        new Vector3(Mathf.Cos(a1) * r1, Mathf.Sin(a1) * r1, z1), new Vector3(Mathf.Cos(a0) * r1, Mathf.Sin(a0) * r1, z1)
                    };
                    var b = vertices.Count;
                    vertices.AddRange(p);
                    colors.AddRange(new[] { color, color, color, color });
                    triangles.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
            }

            const float shaft = SpearLength - 0.2f;
            Ring(0f, shaft, 0.02f, 0.017f, wood, 6);
            Ring(shaft - 0.08f, shaft, 0.024f, 0.024f, binding, 6);
            // Leaf-shaped flat tip: widest a third of the way, four sides.
            Ring(shaft - 0.02f, shaft + 0.06f, 0.02f, 0.05f, stone, 4);
            Ring(shaft + 0.06f, SpearLength, 0.05f, 0.0f, stone, 4);
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
