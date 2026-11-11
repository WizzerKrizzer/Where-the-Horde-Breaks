using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// The tamed mammoth (the Meshy "Crimson Mammoth", widened, rigged and keyed by
    /// Tools/Animation/meshy/build_mammoth.py): its run, tusk scoop, rage and death, seen from the
    /// side and from the gameplay camera charging into the style crowd. Writes Logs/Captures/Mammoth.
    /// </summary>
    public sealed class MammothCaptureTests
    {
        private const string Output = "Logs/Captures/Mammoth";
        // TEMPORARY: shoulder-to-ground about 3 m, next to 1.95 m slingers.
        private const float MammothHeight = 3f;
        private const float Fps = 30f;
        // From build_mammoth.py's report: ground covered per run cycle, in model units.
        private const float RunGroundPerCycle = 0.65f;

        [UnityTest, Explicit("Records the mammoth's clips to Logs/Captures/Mammoth.")]
        public IEnumerator RecordMammoth()
        {
            var skinnedShader = Shader.Find("TowerDefense/StyleSkinned");
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            Assert.That(skinnedShader, Is.Not.Null);
            var prefab = Resources.Load<GameObject>("mammoth");
            if (prefab == null) Assert.Ignore("mammoth.fbx is missing (a local Meshy model). Build it with Tools/Animation/build_mammoth.sh.");
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var clip in Resources.LoadAll<AnimationClip>("mammoth"))
            {
                if (clip.name.StartsWith("__preview__")) continue;
                foreach (var name in new[] { "Run", "Scoop", "Rage", "Death" })
                {
                    if (clip.name.EndsWith(name)) clips[name] = clip;
                }
            }

            Assert.That(clips.Count, Is.EqualTo(4), "clips: " + string.Join(", ", clips.Keys));
            var rig = Object.Instantiate(prefab);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var renderer in rig.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            Transform Bone(string name)
            {
                foreach (var bone in rig.GetComponentsInChildren<Transform>())
                {
                    if (bone.name == name) return bone;
                }

                Assert.Fail("No bone " + name);
                return null;
            }

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

            // Size and facing from the run's first pose: the head is in front of the hips.
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var (probe, local) in Bake(clips["Scoop"], 0f))
            {
                foreach (var vertex in probe.vertices)
                {
                    var y = local.MultiplyPoint3x4(vertex).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            used = 0;
            var fit = MammothHeight / (maxY - minY);
            var forward = Bone("Head").position - Bone("Hips").position;
            forward.y = 0f;
            var facingOffset = -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            var unitsPerModel = fit * (maxY - minY) / 0.6124f;
            var runSpeed = RunGroundPerCycle * unitsPerModel / clips["Run"].length;
            File.WriteAllText(Directory.CreateDirectory(Output).FullName + "/report.txt",
                $"height {maxY - minY:F3} -> scale {fit:F3}, facing offset {facingOffset:F0}, run {clips["Run"].length:F2} s at {runSpeed:F2} m/s, " +
                $"scoop {clips["Scoop"].length:F2} s, rage {clips["Rage"].length:F2} s, death {clips["Death"].length:F2} s\n");

            foreach (var other in Camera.allCameras) other.enabled = false;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;
            var cameraObject = new GameObject("Mammoth camera");
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
            var bodyMaterial = new Material(skinnedShader);
            bodyMaterial.SetTexture("_MainTex", Resources.Load<Texture2D>("mammoth-tex"));
            bodyMaterial.SetFloat("_ShadowPlane", 0f);

            void DrawMammoth(AnimationClip clip, float time, Vector3 position, float yaw)
            {
                var placement = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw + facingOffset, 0f), Vector3.one * fit) *
                                Matrix4x4.Translate(new Vector3(0f, -minY, 0f));
                foreach (var (mesh, local) in Bake(clip, time))
                {
                    Graphics.DrawMesh(mesh, placement * local, bodyMaterial, 0);
                }
            }

            // Runs past, stops, two scoops, a rage, and it dies.
            var sequence = new List<(string clip, float seconds)>
            {
                ("Run", 1.4f), ("Scoop", clips["Scoop"].length * 2f), ("Rage", clips["Rage"].length), ("Death", clips["Death"].length + 0.6f)
            };
            // The same run from the side, low, and from high above at a slant (user, 2026-11-10).
            foreach (var (view, pitch, yawView, size) in new[] { ("side", 3f, 0f, 3.4f), ("high", 58f, -25f, 3.6f) })
            {
                var folder = Output + "/" + view;
                Directory.CreateDirectory(folder);
                camera.orthographic = true;
                var look = Quaternion.Euler(pitch, yawView, 0f);
                var aim = view == "side" ? new Vector3(-0.5f, 1.43f, 0f) : new Vector3(1f, 1.4f, -1.2f);
                camera.transform.SetPositionAndRotation(aim - look * Vector3.forward * 30f, look);
                camera.orthographicSize = size;
                var frame = 0;
                var start = new Vector3(-runSpeed * 1.4f + 2.5f, 0f, 0f);
                foreach (var (name, seconds) in sequence)
                {
                    for (var t = 0f; t < seconds; t += 1f / Fps)
                    {
                        Shader.SetGlobalFloat("_StyleTime", frame / Fps);
                        Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                        var clip = clips[name];
                        var time = name == "Death" ? Mathf.Min(t, clip.length - 0.001f) : Mathf.Repeat(t, clip.length);
                        var position = name == "Run" ? start + Vector3.right * runSpeed * t : start + Vector3.right * runSpeed * 1.4f;
                        DrawMammoth(clip, time, position, 90f);
                        yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame++:D3}.png");
                        used = 0;
                    }
                }
            }

            // The game camera: it charges up the road against the horde, stops in it and scoops.
            var goblin = LowPolyMeshes.Goblin();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, new List<Vector3>());
            var crowd = StyleLowPolyTests.BuildCrowd();
            for (var i = 0; i < crowd.Count; i++)
            {
                var walker = crowd[i];
                walker.Scale *= 0.7f;
                crowd[i] = walker;
            }

            foreach (var height in new[] { 11f, 16f, 40f })
            {
                var folder = $"{Output}/game-{height:F0}m";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, height, new Vector3(0f, 0f, StyleLowPolyTests.RoadCenter(0f) - height * 0.1f));
                const float runSeconds = 2f;
                var from = 7f;
                var stopX = from - runSpeed * runSeconds;
                for (var frame = 0; frame < 180; frame++)
                {
                    var time = frame / Fps;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    var running = time < runSeconds;
                    var x = running ? from - runSpeed * time : stopX;
                    var at = new Vector3(x, 0f, StyleLowPolyTests.RoadCenter(x));
                    // Goblins under its body are not drawn (in the game it shoves them aside).
                    var visible = crowd.FindAll(w => (StyleLowPolyTests.WalkerPosition(w, time) - at).sqrMagnitude > 2.4f * 2.4f);
                    StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, visible, time);
                    if (running) DrawMammoth(clips["Run"], Mathf.Repeat(time, clips["Run"].length), at, -90f);
                    else DrawMammoth(clips["Scoop"], Mathf.Repeat(time - runSeconds, clips["Scoop"].length), at, -90f);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                    used = 0;
                }
            }

            camera.targetTexture = null;
            target.Release();
            foreach (var item in new Object[] { target, texture, cameraObject, groundMesh, groundMaterial, unitMaterial, staticMaterial, bodyMaterial, goblin, terrain, rig })
            {
                Object.DestroyImmediate(item);
            }

            foreach (var mesh in pool) Object.DestroyImmediate(mesh);
        }
    }
}
