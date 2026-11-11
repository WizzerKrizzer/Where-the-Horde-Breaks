using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// The caveman (Meshy model rigged in Mixamo, Mixamo club swings and the retargeted breathing
    /// idle, built by Tools/Animation/meshy/build_caveman.py) swinging a big bone, made here. Close up,
    /// then three of them blocking the road in front of the Meshy cave while the style crowd piles
    /// up against them, from 11, 16 and 40 m. Writes Logs/Captures/Caveman.
    /// </summary>
    public sealed class CavemanCaptureTests
    {
        private const string Output = "Logs/Captures/Caveman";
        // TEMPORARY: a little taller and much bulkier than the 1.95 m slingers.
        private const float CavemanHeight = 2.05f;
        // The big bone (user, 2026-11-11): a thigh bone about half his height, held near one knob.
        private const float BoneLength = 1.05f;
        private const float GripFromEnd = 0.16f;
        // TEMPORARY: the cave's width on the map.
        private const float CaveWidth = 6.5f;
        private const float Fps = 30f;

        [UnityTest, Explicit("Records the cavemen and their cave to Logs/Captures/Caveman.")]
        public IEnumerator RecordCaveman()
        {
            var skinnedShader = Shader.Find("TowerDefense/StyleSkinned");
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            Assert.That(skinnedShader, Is.Not.Null);
            var prefab = Resources.Load<GameObject>("Mixamo/caveman");
            if (prefab == null) Assert.Ignore("Mixamo/caveman.fbx is missing. Build it with Tools/Animation/build_caveman.sh.");
            var cavePrefab = Resources.Load<GameObject>("cave");
            if (cavePrefab == null) Assert.Ignore("cave.fbx is missing (a local Meshy model). Install it with Tools/Animation/build_caveman.sh.");
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var clip in Resources.LoadAll<AnimationClip>("Mixamo/caveman"))
            {
                if (clip.name.StartsWith("__preview__")) continue;
                foreach (var name in new[] { "Idle", "Fight", "AttackBack" })
                {
                    if (clip.name.EndsWith(name)) clips[name] = clip;
                }
            }

            Assert.That(clips.Count, Is.EqualTo(3), "clips: " + string.Join(", ", clips.Keys));
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
            // In the bind pose (arms out, palms down) a bone held in the fist points straight ahead.
            var across = Bone("LeftArm").position - Bone("RightArm").position;
            var ahead = Vector3.Cross(Vector3.up, across).normalized;
            var boneAxis = prop.InverseTransformDirection(ahead);
            var bonePalm = prop.InverseTransformDirection(Vector3.down);
            var facingOffset = -Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;

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

            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var (probe, local) in Bake(clips["Idle"], 0f))
            {
                foreach (var vertex in probe.vertices)
                {
                    var y = local.MultiplyPoint3x4(vertex).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            used = 0;
            var fit = CavemanHeight / (maxY - minY);
            var boneMesh = BuildBone();
            var caveMesh = SpearHunterCaptureTests.Upright(cavePrefab);
            var caveScale = CaveWidth / Mathf.Max(caveMesh.bounds.size.x, caveMesh.bounds.size.z);
            File.WriteAllText(Directory.CreateDirectory(Output).FullName + "/report.txt",
                $"height {maxY - minY:F3} -> scale {fit:F3}, facing offset {facingOffset:F0}, bone axis {boneAxis:F2}, " +
                $"idle {clips["Idle"].length:F2} s, fight {clips["Fight"].length:F2} s, backhand {clips["AttackBack"].length:F2} s; " +
                $"cave {caveMesh.bounds.size} x {caveScale:F2}\n");

            foreach (var other in Camera.allCameras) other.enabled = false;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;
            var cameraObject = new GameObject("Caveman camera");
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
            Material Textured(string name)
            {
                var material = new Material(skinnedShader);
                material.SetTexture("_MainTex", Resources.Load<Texture2D>(name));
                material.SetFloat("_ShadowPlane", 0f);
                return material;
            }

            var bodyMaterial = Textured("Mixamo/caveman-tex");
            var caveMaterial = Textured("cave-tex");
            var boneMaterial = new Material(skinnedShader);
            boneMaterial.SetFloat("_VertexColor", 1f);
            boneMaterial.SetFloat("_ShadowPlane", 0f);

            void DrawCaveman(AnimationClip clip, float time, Vector3 position, float yaw)
            {
                var placement = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw + facingOffset, 0f), Vector3.one * fit) *
                                Matrix4x4.Translate(new Vector3(0f, -minY, 0f));
                foreach (var (mesh, local) in Bake(clip, time))
                {
                    Graphics.DrawMesh(mesh, placement * local, bodyMaterial, 0);
                }

                var grip = placement.MultiplyPoint3x4(prop.position);
                var along = placement.MultiplyVector(prop.TransformDirection(boneAxis)).normalized;
                var palm = placement.MultiplyVector(prop.TransformDirection(bonePalm));
                var lay = Quaternion.LookRotation(along, Vector3.ProjectOnPlane(palm, along).sqrMagnitude > 1e-6f ? -palm : Vector3.up);
                Graphics.DrawMesh(boneMesh, Matrix4x4.TRS(grip - along * GripFromEnd, lay, Vector3.one), boneMaterial, 0);
            }

            // Close up: the fight loop from the side and from the front, the backhand, and one idle.
            {
                var folder = Output + "/close";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, 4.5f, new Vector3(0.3f, 0f, 0.5f));
                for (var frame = 0; frame < 170; frame++)
                {
                    var time = frame / Fps;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    DrawCaveman(clips["Fight"], Mathf.Repeat(time, clips["Fight"].length), new Vector3(-2.6f, 0f, 0f), 90f);
                    DrawCaveman(clips["Fight"], Mathf.Repeat(time + 1.3f, clips["Fight"].length), new Vector3(-0.2f, 0f, 0f), 200f);
                    DrawCaveman(clips["AttackBack"], Mathf.Repeat(time, clips["AttackBack"].length), new Vector3(2.2f, 0f, 0.6f), 160f);
                    DrawCaveman(clips["Idle"], Mathf.Repeat(time, clips["Idle"].length), new Vector3(4.4f, 0f, 1.2f), 200f);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                    used = 0;
                }
            }

            // The cave beside the road and five cavemen (the most an upgrade gives) across the road in
            // front of it, facing the horde that comes along +x. The road is far wider than five
            // men, so only the goblins that run into one of them pile up; the rest stream past.
            var towerPositions = StyleLowPolyTests.TowerPositions();
            var caveAt = towerPositions[0];
            var caveYaw = caveAt.z > StyleLowPolyTests.RoadCenter(caveAt.x) ? 180f : 0f;
            var caveMatrix = Matrix4x4.TRS(caveAt, Quaternion.Euler(0f, caveYaw, 0f), Vector3.one * caveScale) *
                             Matrix4x4.Translate(new Vector3(-caveMesh.bounds.center.x, -caveMesh.bounds.min.y, -caveMesh.bounds.center.z));
            var lineX = caveAt.x - 1f;
            var guards = new List<Vector3>();
            var guardLanes = new[] { -4f, -2f, 0f, 2f, 4f };
            foreach (var lane in guardLanes)
            {
                guards.Add(new Vector3(lineX + Mathf.Abs(lane) * 0.12f, 0f, StyleLowPolyTests.RoadCenter(lineX) + lane));
            }

            var goblin = LowPolyMeshes.Goblin();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, new List<Vector3> { caveAt });
            var crowd = StyleLowPolyTests.BuildCrowd();
            for (var i = 0; i < crowd.Count; i++)
            {
                var walker = crowd[i];
                walker.Scale *= 0.7f;
                crowd[i] = walker;
            }

            // Goblins stop short of the line, each at its own depth so they pack rather than stack.
            List<StyleLowPolyTests.Walker> Blocked(float time)
            {
                var list = new List<StyleLowPolyTests.Walker>(crowd.Count);
                for (var i = 0; i < crowd.Count; i++)
                {
                    var walker = crowd[i];
                    var x = walker.StartX + walker.Speed * time;
                    if (walker.StartX > lineX)
                    {
                        list.Add(walker); // already past when the recording starts
                        continue;
                    }

                    // Held up by a caveman whose reach covers its lane.
                    var held = false;
                    foreach (var lane in guardLanes) held |= Mathf.Abs(walker.Lane - lane) < 1.1f;
                    var stop = lineX - 0.75f - (i * 0.6180339f % 1f) * 3.5f;
                    if (held && x > stop) walker.StartX = stop - walker.Speed * time;
                    list.Add(walker);
                }

                return list;
            }

            // The far views in 4K (user, 2026-11-11), where a goblin is only a few pixels at 1080p.
            var uhdTarget = new RenderTexture(3840, 2160, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            var uhdTexture = new Texture2D(3840, 2160, TextureFormat.RGB24, false);
            // CAPTURE_HEIGHTS="1,2,4,8" records only those heights.
            var only = System.Environment.GetEnvironmentVariable("CAPTURE_HEIGHTS");
            var views = new[]
            {
                (1f, false, 120), (2f, false, 120), (4f, false, 120), (8f, false, 120), (11f, false, 180), (16f, false, 180), (40f, false, 180),
                (80f, true, 120), (160f, true, 120), (320f, true, 90), (640f, true, 90)
            };
            foreach (var (height, uhd, frames) in views)
            {
                if (only != null && System.Array.IndexOf(only.Split(','), height.ToString("F0")) < 0) continue;
                if (only == null && height < 10f) continue;
                var folder = $"{Output}/road-{height:F0}m{(uhd ? "-4k" : "")}";
                camera.targetTexture = uhd ? uhdTarget : target;
                var shot = uhd ? uhdTexture : texture;
                Directory.CreateDirectory(folder);
                // Close up the cavemen fill the view; from higher up the cave comes into it too.
                var between = StyleLowPolyTests.RoadCenter(lineX) + (caveAt.z - StyleLowPolyTests.RoadCenter(lineX)) * (height < 12f ? 0f : 0.45f);
                if (height < 10f)
                {
                    // The zoomed-in views centre on the middle caveman's chest. The camera is
                    // orthographic, so it is moved back along its view to stay above their heads.
                    StyleLowPolyTests.PlaceCamera(camera, height, guards[2] + Vector3.up * 1.2f);
                    camera.transform.position -= camera.transform.forward * 50f;
                }
                else
                {
                    StyleLowPolyTests.PlaceCamera(camera, height, new Vector3(lineX, 0f, between - height * 0.12f));
                }
                for (var frame = 0; frame < frames; frame++)
                {
                    var time = frame / Fps;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    Graphics.DrawMesh(caveMesh, caveMatrix, caveMaterial, 0);
                    StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, Blocked(time), time);
                    for (var g = 0; g < guards.Count; g++)
                    {
                        DrawCaveman(clips["Fight"], Mathf.Repeat(time + g * 1.7f, clips["Fight"].length), guards[g], -90f + (g - 2) * 6f);
                    }

                    yield return StyleLowPolyTests.Capture(camera, uhd ? uhdTarget : target, shot, $"{folder}/frame_{frame:D3}.png");
                    used = 0;
                }
            }

            camera.targetTexture = null;
            target.Release();
            uhdTarget.Release();
            foreach (var item in new Object[] { target, texture, uhdTarget, uhdTexture, cameraObject, groundMesh, groundMaterial, unitMaterial, staticMaterial, bodyMaterial, caveMaterial,
                         boneMaterial, boneMesh, caveMesh, goblin, terrain, rig })
            {
                Object.DestroyImmediate(item);
            }

            foreach (var mesh in pool) Object.DestroyImmediate(mesh);
        }

        // A big thigh bone along +Z from the origin: a shaft that narrows in the middle and two
        // knobbed ends, each a pair of rounded lumps side by side. Old bone colour, darker at the ends.
        private static Mesh BuildBone()
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var bone = new Color(0.86f, 0.8f, 0.66f);
            var worn = new Color(0.66f, 0.55f, 0.4f);

            void Lump(Vector3 center, Vector3 radii, Color color)
            {
                const int rings = 5, sides = 8;
                var start = vertices.Count;
                for (var r = 0; r <= rings; r++)
                {
                    var v = Mathf.PI * r / rings;
                    for (var s = 0; s < sides; s++)
                    {
                        var u = 2f * Mathf.PI * s / sides;
                        vertices.Add(center + Vector3.Scale(radii, new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Sin(v) * Mathf.Sin(u), Mathf.Cos(v))));
                        colors.Add(color);
                    }
                }

                for (var r = 0; r < rings; r++)
                {
                    for (var s = 0; s < sides; s++)
                    {
                        var a = start + r * sides + s;
                        var b = start + r * sides + (s + 1) % sides;
                        var c = a + sides;
                        var d = b + sides;
                        triangles.AddRange(new[] { a, c, b, b, c, d });
                    }
                }
            }

            void Shaft(float z0, float z1, float r0, float r1, Color c0, Color c1)
            {
                const int sides = 8;
                var start = vertices.Count;
                for (var s = 0; s < sides; s++)
                {
                    var u = 2f * Mathf.PI * s / sides;
                    vertices.Add(new Vector3(Mathf.Cos(u) * r0, Mathf.Sin(u) * r0, z0));
                    vertices.Add(new Vector3(Mathf.Cos(u) * r1, Mathf.Sin(u) * r1, z1));
                    colors.Add(c0);
                    colors.Add(c1);
                }

                for (var s = 0; s < sides; s++)
                {
                    var a = start + 2 * s;
                    var b = start + 2 * ((s + 1) % sides);
                    triangles.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
                }
            }

            const float knob = 0.085f;
            Shaft(0.06f, BoneLength * 0.5f, 0.05f, 0.036f, worn, bone);
            Shaft(BoneLength * 0.5f, BoneLength - 0.06f, 0.036f, 0.058f, bone, worn);
            Lump(new Vector3(0.045f, 0f, 0.06f), new Vector3(knob * 0.8f, knob * 0.75f, knob), worn);
            Lump(new Vector3(-0.045f, 0f, 0.05f), new Vector3(knob * 0.8f, knob * 0.75f, knob * 0.9f), worn);
            Lump(new Vector3(0.05f, 0f, BoneLength - 0.07f), new Vector3(knob, knob * 0.85f, knob * 1.1f), worn);
            Lump(new Vector3(-0.05f, 0f, BoneLength - 0.06f), new Vector3(knob * 0.95f, knob * 0.85f, knob), worn);
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
