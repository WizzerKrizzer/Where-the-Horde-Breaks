using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Throwaway defender test: four prehistoric tower defenders (archer, slinger, rock thrower,
    /// spear thrower) with a shoot-and-wait cycle. Renders a close-up line-up, a close walk
    /// cycle of the goblins, and the converted Meshy tipi manned by slingers that turn to their
    /// targets and throw stones at the passing horde, at 11 / 14 / 20 m. Writes PNGs to
    /// Logs/StyleDefenders.
    /// </summary>
    public sealed class StyleDefenderTests
    {
        private const string Output = "Logs/StyleDefenders";
        private const string MeshyTipi = "Assets/Tests/PlayMode/StyleAssets/meshy-tipi.txt";
        private const string TrellisSlingerPath = "Assets/Tests/PlayMode/StyleAssets/trellis-slinger.txt";
        private const string TrellisRigPath = "Assets/Tests/PlayMode/StyleAssets/trellis-slinger-rig.txt";
        private const string TrellisVatPath = "Assets/Tests/PlayMode/StyleAssets/trellis-slinger-vat.bytes";
        private const float Period = 2.6f;
        // Seconds into a slinger's cycle when the stone leaves the sling (StyleDefender.shader).
        private const float Release = 0.7f;
        private const float DeckHeight = 2.5f;
        private const float PostRadius = 2.4f;
        private const float DefenderScale = 1.6f;
        private const float ThrowerScale = 1.12f;
        private static readonly float[] PostAngles = { 215f, 250f, 290f, 325f };
        // Where each defender is in its cycle in the close-up: waiting, then mid-shot.
        private static readonly float[] WaitTimes = { 1.8f, 1.9f, 1.4f, 2.0f };
        private static readonly float[] ShotTimes = { 0.3f, 0.4f, 0.3f, 0.25f };

        [UnityTest, Explicit("Defender style test")]
        [Category("Performance")]
        public IEnumerator RenderDefenders()
        {
            Directory.CreateDirectory(Output);
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            var defenderShader = Shader.Find("TowerDefense/StyleDefender");
            Assert.That(unitShader, Is.Not.Null);
            Assert.That(groundShader, Is.Not.Null);
            Assert.That(defenderShader, Is.Not.Null);

            var defenders = new Mesh[4];
            var report = new StringBuilder();
            for (var type = 0; type < 4; type++)
            {
                defenders[type] = DefenderMeshes.Defender(type);
                report.AppendLine($"{defenders[type].name}: {defenders[type].GetIndexCount(0) / 3} triangles");
            }

            File.WriteAllText(Output + "/triangles.txt", report.ToString());

            var otherMainCameras = new List<Camera>();
            foreach (var other in Camera.allCameras)
            {
                if (other.enabled && other.CompareTag("MainCamera"))
                {
                    other.enabled = false;
                    otherMainCameras.Add(other);
                }
            }

            var hiddenRenderers = new List<Renderer>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    hiddenRenderers.Add(renderer);
                }
            }

            var cameraObject = new GameObject("Defender camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = StyleLowPolyTests.Ground;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2000f;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            camera.targetTexture = target;

            var groundMesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1000f, 0f, -1000f), new Vector3(-1000f, 0f, 1000f),
                    new Vector3(1000f, 0f, 1000f), new Vector3(1000f, 0f, -1000f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var ground = new GameObject("Defender ground");
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundMaterial = new Material(groundShader);
            groundMaterial.SetColor("_Color", StyleLowPolyTests.Ground);
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var unitMaterial = new Material(unitShader) { enableInstancing = true };
            var staticMaterial = new Material(unitShader) { enableInstancing = true };
            staticMaterial.SetFloat("_Animate", 0f);
            var onGround = new Material(defenderShader) { enableInstancing = true };
            onGround.SetFloat("_Period", Period);
            var onDeck = new Material(defenderShader) { enableInstancing = true };
            onDeck.SetFloat("_Period", Period);
            onDeck.SetFloat("_ShadowPlane", DeckHeight + 0.04f);

            var goblin = LowPolyMeshes.Goblin();
            var pebble = DefenderMeshes.Pebble();
            var tower = TowerMeshes.Baked(MeshyTipi, "MeshyTipi");
            var towerPositions = StyleLowPolyTests.TowerPositions();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, towerPositions);
            var crowd = StyleLowPolyTests.BuildCrowd();
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);

            // Close-up: top row waiting, bottom row mid-shot, turned three-quarters to the camera.
            {
                StyleLowPolyTests.PlaceCamera(camera, 6f, new Vector3(0f, 0f, 0.3f));
                Shader.SetGlobalFloat("_StyleTime", 0f);
                for (var type = 0; type < 4; type++)
                {
                    var x = -3.6f + type * 2.4f;
                    var scale = Scale(type);
                    var matrices = new List<Matrix4x4>
                    {
                        Matrix4x4.TRS(new Vector3(x, 0f, 1.3f), Quaternion.Euler(0f, 160f, 0f), Vector3.one * scale),
                        Matrix4x4.TRS(new Vector3(x, 0f, -1.5f), Quaternion.Euler(0f, 160f, 0f), Vector3.one * scale),
                    };
                    var seeds = new List<float> { type + WaitTimes[type] / Period, type + ShotTimes[type] / Period };
                    StyleLowPolyTests.DrawInstanced(defenders[type], onGround, matrices, seeds);
                }

                yield return StyleLowPolyTests.Capture(camera, target, texture, Output + "/closeup-defenders.png");
            }

            // Goblins walking past at close range, to judge the walk cycle.
            {
                var folder = Output + "/clip-goblin-walk";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, 5f, Vector3.zero);
                for (var frame = 0; frame < 90; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    var matrices = new List<Matrix4x4>();
                    var seeds = new List<float>();
                    for (var i = 0; i < 8; i++)
                    {
                        var row = i % 2 == 0 ? 0.9f : -0.9f;
                        matrices.Add(StyleLowPolyTests.Matrix(-7f + i * 1.7f + time * 1.05f, row, 0.45f, 90f));
                        seeds.Add(i * 0.17f);
                    }

                    StyleLowPolyTests.DrawInstanced(goblin, unitMaterial, matrices, seeds);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                }
            }

            // Slinger towers: every defender turns to the goblin it shoots and its stone flies
            // there. Recorded from the gameplay camera with the horde walking past.
            var posts = Posts(towerPositions);
            var targets = new Dictionary<long, int>();
            foreach (var (height, frames, lookAt) in new[]
            {
                (11f, 180, towerPositions[0] + new Vector3(0f, 0f, -3.5f)),
                (14f, 240, towerPositions[0] + new Vector3(0f, 0f, -5f)),
                (20f, 240, towerPositions[0] + new Vector3(0f, 0f, -5f)),
            })
            {
                var folder = $"{Output}/clip-slingers-{height:F0}m";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, height, lookAt);
                for (var frame = 0; frame < frames; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    foreach (var position in towerPositions)
                    {
                        Graphics.DrawMesh(tower, Matrix4x4.TRS(position, Quaternion.Euler(0f, Facing(position), 0f), Vector3.one), staticMaterial, 0);
                    }

                    DrawSlingers(defenders[DefenderMeshes.Slinger], pebble, onDeck, staticMaterial, posts, crowd, targets, time);
                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, time);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                }
            }

            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(groundMesh);
            Object.DestroyImmediate(unitMaterial);
            Object.DestroyImmediate(staticMaterial);
            Object.DestroyImmediate(onGround);
            Object.DestroyImmediate(onDeck);
            Object.DestroyImmediate(goblin);
            Object.DestroyImmediate(pebble);
            Object.DestroyImmediate(tower);
            Object.DestroyImmediate(terrain);
            foreach (var mesh in defenders) Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            foreach (var renderer in hiddenRenderers)
            {
                if (renderer != null) renderer.enabled = true;
            }

            foreach (var other in otherMainCameras)
            {
                if (other != null) other.enabled = true;
            }
        }

        // TRELLIS slinger (converted from a generated model) next to the approved procedural one:
        // same shader, same shot timing, its own pivots. Writes Logs/StyleDefenders/trellis.
        [UnityTest, Explicit("Defender style test")]
        [Category("Performance")]
        public IEnumerator RenderTrellisSlinger()
        {
            var output = Output + "/trellis";
            Directory.CreateDirectory(output);
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            var defenderShader = Shader.Find("TowerDefense/StyleDefender");
            Assert.That(defenderShader, Is.Not.Null);

            var oldSlinger = DefenderMeshes.Defender(DefenderMeshes.Slinger);
            var newSlinger = DefenderMeshes.TrellisSlinger(TrellisSlingerPath);
            var oldStone = DefenderMeshes.SlingStone(Release, DefenderMeshes.ProceduralShoulder, DefenderMeshes.ProceduralHand, DefenderMeshes.ProceduralHip, DefenderMeshes.ProceduralStone);
            var newStone = DefenderMeshes.SlingStone(Release, DefenderMeshes.TrellisShoulder, DefenderMeshes.TrellisHand, DefenderMeshes.TrellisHip, DefenderMeshes.TrellisStone);
            File.WriteAllText(output + "/report.txt",
                $"procedural: {oldSlinger.GetIndexCount(0) / 3} triangles, stone at release {oldStone * DefenderScale:F3} (constant used so far: (0.24, 1.52, 0.62))\n" +
                $"trellis: {newSlinger.GetIndexCount(0) / 3} triangles, stone at release {newStone * DefenderScale:F3}\n");
            Assert.That(newSlinger.GetIndexCount(0) / 3, Is.LessThan(6000));

            foreach (var other in Camera.allCameras)
            {
                if (other.enabled && other.CompareTag("MainCamera")) other.enabled = false;
            }

            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;

            var cameraObject = new GameObject("Trellis camera") { tag = "MainCamera" };
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

            Material Defenders(bool trellis, float shadowPlane)
            {
                var material = new Material(defenderShader) { enableInstancing = true };
                material.SetFloat("_Period", Period);
                material.SetFloat("_ShadowPlane", shadowPlane);
                if (trellis)
                {
                    material.SetVector("_Shoulder", DefenderMeshes.TrellisShoulder);
                    material.SetVector("_Hand", DefenderMeshes.TrellisHand);
                    material.SetFloat("_HipHeight", DefenderMeshes.TrellisHip);
                }

                return material;
            }

            var oldOnGround = Defenders(false, 0f);
            var newOnGround = Defenders(true, 0f);
            var newOnDeck = Defenders(true, DeckHeight + 0.04f);

            var goblin = LowPolyMeshes.Goblin();
            var pebble = DefenderMeshes.Pebble();
            var tower = TowerMeshes.Baked(MeshyTipi, "MeshyTipi");
            var towerPositions = StyleLowPolyTests.TowerPositions();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, towerPositions);
            var crowd = StyleLowPolyTests.BuildCrowd();

            // Line-up through one shot: procedural on the top row, TRELLIS below.
            {
                StyleLowPolyTests.PlaceCamera(camera, 7f, new Vector3(0f, 0f, 0.2f));
                Shader.SetGlobalFloat("_StyleTime", 0f);
                var phases = new[] { 1.9f, 0.1f, 0.4f, 0.72f, 0.9f };
                Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                for (var row = 0; row < 2; row++)
                {
                    var matrices = new List<Matrix4x4>();
                    var seeds = new List<float>();
                    for (var i = 0; i < phases.Length; i++)
                    {
                        matrices.Add(Matrix4x4.TRS(new Vector3(-4.4f + i * 2.2f, 0f, row == 0 ? 1.6f : -1.7f), Quaternion.Euler(0f, 160f, 0f), Vector3.one * DefenderScale));
                        seeds.Add(DefenderMeshes.Slinger + phases[i] / Period);
                    }

                    StyleLowPolyTests.DrawInstanced(row == 0 ? oldSlinger : newSlinger, row == 0 ? oldOnGround : newOnGround, matrices, seeds);
                }

                yield return StyleLowPolyTests.Capture(camera, target, texture, output + "/lineup.png");
            }

            // One full cycle of each, close up: procedural left, TRELLIS right.
            {
                var folder = output + "/clip-cycle";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, 4f, new Vector3(0f, 0f, 0.3f));
                for (var frame = 0; frame < 84; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    var seeds = new List<float> { DefenderMeshes.Slinger };
                    StyleLowPolyTests.DrawInstanced(oldSlinger, oldOnGround, new List<Matrix4x4> { Matrix4x4.TRS(new Vector3(-1.5f, 0f, 0f), Quaternion.Euler(0f, 150f, 0f), Vector3.one * DefenderScale) }, seeds);
                    StyleLowPolyTests.DrawInstanced(newSlinger, newOnGround, new List<Matrix4x4> { Matrix4x4.TRS(new Vector3(1.5f, 0f, 0f), Quaternion.Euler(0f, 150f, 0f), Vector3.one * DefenderScale) }, seeds);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                }
            }

            // The tipi manned by TRELLIS slingers with the horde passing, from the gameplay camera.
            var posts = Posts(towerPositions);
            var targets = new Dictionary<long, int>();
            foreach (var (height, frames, lookAt) in new[]
            {
                (11f, 180, towerPositions[0] + new Vector3(0f, 0f, -3.5f)),
                (14f, 180, towerPositions[0] + new Vector3(0f, 0f, -5f)),
            })
            {
                var folder = $"{output}/clip-slingers-{height:F0}m";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, height, lookAt);
                for (var frame = 0; frame < frames; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    foreach (var position in towerPositions)
                    {
                        Graphics.DrawMesh(tower, Matrix4x4.TRS(position, Quaternion.Euler(0f, Facing(position), 0f), Vector3.one), staticMaterial, 0);
                    }

                    DrawSlingers(newSlinger, pebble, newOnDeck, staticMaterial, posts, crowd, targets, time, newStone);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, time);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                }
            }

            camera.targetTexture = null;
            target.Release();
            foreach (var item in new Object[] { target, texture, cameraObject, groundMesh, groundMaterial, unitMaterial, staticMaterial, oldOnGround, newOnGround, newOnDeck, goblin, pebble, tower, terrain, oldSlinger, newSlinger })
            {
                Object.DestroyImmediate(item);
            }
        }

        // The rigged slinger (skeleton, eased keys, simulated sling, baked to a vertex animation
        // texture) next to the approved procedural one. Writes Logs/StyleDefenders/vat.
        [UnityTest, Explicit("Defender style test")]
        [Category("Performance")]
        public IEnumerator RenderVatSlinger()
        {
            var output = Output + "/vat";
            Directory.CreateDirectory(output);
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            var defenderShader = Shader.Find("TowerDefense/StyleDefender");
            var vatShader = Shader.Find("TowerDefense/StyleSlingerVat");
            Assert.That(vatShader, Is.Not.Null);

            var oldSlinger = DefenderMeshes.Defender(DefenderMeshes.Slinger);
            var newSlinger = DefenderMeshes.VatSlinger(TrellisRigPath, TrellisVatPath, out var clip, out var vatRelease, out var vatStone);
            File.WriteAllText(output + "/report.txt",
                $"vat: {newSlinger.vertexCount / 3} triangles, {clip.width} animated vertices x {clip.height} frames, release at {vatRelease:F2} s, stone at release {vatStone * DefenderScale:F3}\n");

            foreach (var other in Camera.allCameras)
            {
                if (other.enabled && other.CompareTag("MainCamera")) other.enabled = false;
            }

            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;

            var cameraObject = new GameObject("Vat camera") { tag = "MainCamera" };
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
            var oldOnGround = new Material(defenderShader) { enableInstancing = true };
            oldOnGround.SetFloat("_Period", Period);

            Material Vat(float shadowPlane)
            {
                var material = new Material(vatShader) { enableInstancing = true };
                material.SetTexture("_VatTex", clip);
                material.SetFloat("_Period", Period);
                material.SetFloat("_ShadowPlane", shadowPlane);
                return material;
            }

            var newOnGround = Vat(0f);
            var newOnDeck = Vat(DeckHeight + 0.04f);

            var goblin = LowPolyMeshes.Goblin();
            var pebble = DefenderMeshes.Pebble();
            var tower = TowerMeshes.Baked(MeshyTipi, "MeshyTipi");
            var towerPositions = StyleLowPolyTests.TowerPositions();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, towerPositions);
            var crowd = StyleLowPolyTests.BuildCrowd();

            // Key moments of the new throw, turned two ways.
            {
                StyleLowPolyTests.PlaceCamera(camera, 7f, new Vector3(0f, 0f, 0.2f));
                Shader.SetGlobalFloat("_StyleTime", 0f);
                var phases = new[] { 0.0f, 0.35f, 0.6f, 0.85f, 1.0f, 1.1f, 1.3f, 1.9f };
                Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                for (var row = 0; row < 2; row++)
                {
                    var matrices = new List<Matrix4x4>();
                    var seeds = new List<float>();
                    for (var i = 0; i < phases.Length; i++)
                    {
                        matrices.Add(Matrix4x4.TRS(new Vector3(-5.6f + i * 1.6f, 0f, row == 0 ? 1.6f : -1.7f), Quaternion.Euler(0f, row == 0 ? 160f : 250f, 0f), Vector3.one * DefenderScale));
                        seeds.Add(DefenderMeshes.Slinger + phases[i] / Period);
                    }

                    StyleLowPolyTests.DrawInstanced(newSlinger, newOnGround, matrices, seeds);
                }

                yield return StyleLowPolyTests.Capture(camera, target, texture, output + "/lineup.png");
            }

            // Two cycles close up: the approved procedural slinger left, the rigged one right.
            {
                var folder = output + "/clip-cycle";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, 4f, new Vector3(0f, 0f, 0.3f));
                for (var frame = 0; frame < 156; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    var seeds = new List<float> { DefenderMeshes.Slinger };
                    StyleLowPolyTests.DrawInstanced(oldSlinger, oldOnGround, new List<Matrix4x4> { Matrix4x4.TRS(new Vector3(-1.5f, 0f, 0f), Quaternion.Euler(0f, 150f, 0f), Vector3.one * DefenderScale) }, seeds);
                    StyleLowPolyTests.DrawInstanced(newSlinger, newOnGround, new List<Matrix4x4> { Matrix4x4.TRS(new Vector3(1.5f, 0f, 0f), Quaternion.Euler(0f, 150f, 0f), Vector3.one * DefenderScale) }, seeds);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                }
            }

            // The tipi manned by rigged slingers with the horde passing, from the gameplay camera.
            var posts = Posts(towerPositions);
            var targets = new Dictionary<long, int>();
            foreach (var (height, frames, lookAt) in new[]
            {
                (11f, 180, towerPositions[0] + new Vector3(0f, 0f, -3.5f)),
                (14f, 180, towerPositions[0] + new Vector3(0f, 0f, -5f)),
            })
            {
                var folder = $"{output}/clip-slingers-{height:F0}m";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, height, lookAt);
                for (var frame = 0; frame < frames; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    foreach (var position in towerPositions)
                    {
                        Graphics.DrawMesh(tower, Matrix4x4.TRS(position, Quaternion.Euler(0f, Facing(position), 0f), Vector3.one), staticMaterial, 0);
                    }

                    DrawSlingers(newSlinger, pebble, newOnDeck, staticMaterial, posts, crowd, targets, time, vatStone, vatRelease);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, time);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                }
            }

            camera.targetTexture = null;
            target.Release();
            foreach (var item in new Object[] { target, texture, cameraObject, groundMesh, groundMaterial, unitMaterial, staticMaterial, oldOnGround, newOnGround, newOnDeck, goblin, pebble, tower, terrain, oldSlinger, newSlinger, clip })
            {
                Object.DestroyImmediate(item);
            }
        }

        [UnityTest, Explicit("Defender style test")]
        [Category("Performance")]
        public IEnumerator RenderMeshyWarrior()
        {
            // The Meshy "Stonehide Warrior" (skinned, textured) with its Walking and Running clips
            // and the retargeted sling Throw (sling bones + cords mesh), drawn by baking the
            // skinned meshes on the CPU each frame. Writes Logs/StyleDefenders/meshy-warrior.
            const float throwRelease = 1.45f;
            // Target sizes (user, 2026-11-06): Neanderthal slingers about 1.95 m, goblins at most
            // about 1.2 m.
            const float slingerHeight = 1.95f;
            var output = Output + "/meshy-warrior";
            Directory.CreateDirectory(output);
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            var skinnedShader = Shader.Find("TowerDefense/StyleSkinned");
            Assert.That(skinnedShader, Is.Not.Null);
            // Its body motion comes from Mixamo, so the FBX is not in git (see Tools/Animation).
            var prefab = Resources.Load<GameObject>("Mixamo/stonehide");
            if (prefab == null) Assert.Ignore("Mixamo/stonehide.fbx is missing. Build it with Tools/Animation/build_slinger.sh.");
            var clips = new List<AnimationClip>();
            foreach (var item in Resources.LoadAll<AnimationClip>("Mixamo/stonehide"))
            {
                if (!item.name.StartsWith("__preview__")) clips.Add(item);
            }

            var walk = clips.Find(item => item.name.Contains("Walking"));
            var run = clips.Find(item => item.name.Contains("Running"));
            var hurl = clips.Find(item => item.name.Contains("Throw"));
            Assert.That(walk, Is.Not.Null);
            Assert.That(hurl, Is.Not.Null);

            var rig = Object.Instantiate(prefab);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var renderer in rig.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            Transform stoneBone = null;
            foreach (var bone in rig.GetComponentsInChildren<Transform>())
            {
                if (bone.name == "SlingStone") stoneBone = bone;
            }

            Assert.That(stoneBone, Is.Not.Null);
            var pool = new List<Mesh>();
            var used = 0;

            // Clip time for a time in the 2.6 s cycle (the exported clip may be a frame shorter).
            float ClipTime(AnimationClip clip, float time) => clip == hurl ? Mathf.Repeat(time, Period) / Period * clip.length : Mathf.Repeat(time, clip.length);

            List<(Mesh mesh, Matrix4x4 local, bool sling)> Bake(AnimationClip clip, float time)
            {
                clip.SampleAnimation(rig, ClipTime(clip, time));
                var baked = new List<(Mesh, Matrix4x4, bool)>();
                foreach (var skin in skins)
                {
                    if (used == pool.Count) pool.Add(new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
                    var mesh = pool[used++];
                    skin.BakeMesh(mesh);
                    baked.Add((mesh, skin.transform.localToWorldMatrix, skin.name.StartsWith("Sling")));
                }

                return baked;
            }

            // Fit the standing pose to the slinger height (1.6 m) with the feet on the ground.
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var (probe, probeMatrix, sling) in Bake(walk, 0f))
            {
                if (sling) continue;
                foreach (var vertex in probe.vertices)
                {
                    var y = probeMatrix.MultiplyPoint3x4(vertex).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            used = 0;
            var fit = slingerHeight / (maxY - minY);
            hurl.SampleAnimation(rig, ClipTime(hurl, throwRelease - 1f / 30f));
            var launch = (stoneBone.position - new Vector3(0f, minY, 0f)) * fit;
            File.WriteAllText(output + "/report.txt",
                $"meshy warrior: {skins.Length} skinned meshes, height {maxY - minY:F3} -> scale {fit:F3}, stone at release {launch:F3}, clips: {string.Join(", ", clips.ConvertAll(item => $"{item.name} {item.length:F2} s"))}\n");

            // The feet through one throw, for checking that they stay on the planks (feet-track.txt):
            // per frame, the heel and toe tip of each foot in metres, sole at 0, facing +Z.
            {
                var footBones = new List<Transform>();
                foreach (var boneName in new[] { "LeftFoot", "LeftToe_end", "RightFoot", "RightToe_end" })
                {
                    foreach (var bone in rig.GetComponentsInChildren<Transform>())
                    {
                        if (bone.name == boneName) footBones.Add(bone);
                    }
                }

                Assert.That(footBones.Count, Is.EqualTo(4));
                var track = new StringBuilder();
                for (var frame = 0; frame < 78; frame++)
                {
                    hurl.SampleAnimation(rig, ClipTime(hurl, frame * Period / 78f));
                    var row = new List<string>();
                    foreach (var bone in footBones)
                    {
                        var point = (bone.position - new Vector3(0f, minY, 0f)) * fit;
                        row.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F4} {1:F4} {2:F4}", point.x, point.y, point.z));
                    }

                    track.AppendLine(string.Join(" ", row));
                }

                File.WriteAllText(output + "/feet-track.txt", track.ToString());
            }

            Material body = null, rope = null, bodyDeck = null, ropeDeck = null;

            void Draw(AnimationClip clip, float time, Vector3 position, float yaw, bool deck)
            {
                foreach (var (mesh, local, sling) in Bake(clip, time))
                {
                    var place = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f), Vector3.one * fit) * Matrix4x4.Translate(new Vector3(0f, -minY, 0f)) * local;
                    Graphics.DrawMesh(mesh, place, sling ? (deck ? ropeDeck : rope) : (deck ? bodyDeck : body), 0);
                }
            }

            // LAYOUTS_ONLY=1 renders only the tipi with eight slingers.
            var layoutsOnly = System.Environment.GetEnvironmentVariable("LAYOUTS_ONLY") == "1";

            foreach (var other in Camera.allCameras)
            {
                if (other.enabled && other.CompareTag("MainCamera")) other.enabled = false;
            }

            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;

            var cameraObject = new GameObject("Meshy camera") { tag = "MainCamera" };
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
            var skinTexture = Resources.Load<Texture2D>("stonehide");

            Material Skinned(float shadowPlane, bool vertexColor)
            {
                var material = new Material(skinnedShader);
                material.SetTexture("_MainTex", skinTexture);
                material.SetFloat("_ShadowPlane", shadowPlane);
                material.SetFloat("_VertexColor", vertexColor ? 1f : 0f);
                return material;
            }

            body = Skinned(0f, false);
            rope = Skinned(0f, true);
            bodyDeck = Skinned(DeckHeight + 0.04f, false);
            ropeDeck = Skinned(DeckHeight + 0.04f, true);

            var goblin = LowPolyMeshes.Goblin();
            var pebble = DefenderMeshes.Pebble();
            var tower = TowerMeshes.Baked(MeshyTipi, "MeshyTipi");
            var towerPositions = StyleLowPolyTests.TowerPositions();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, towerPositions);
            var crowd = StyleLowPolyTests.BuildCrowd();
            var meanScale = 0f;
            foreach (var walker in crowd) meanScale += walker.Scale;
            meanScale /= crowd.Count;
            // Goblin size relative to the old crowd: 0.7 approved by the user (2026-11-06), about half a
            // slinger from above. GOBLIN_SCALE overrides it. From above their size reads from their
            // wide, hunched bodies rather than their height.
            var goblinFit = float.TryParse(System.Environment.GetEnvironmentVariable("GOBLIN_SCALE"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var envScale) ? envScale : 0.7f;
            for (var i = 0; i < crowd.Count; i++)
            {
                var walker = crowd[i];
                walker.Scale *= goblinFit;
                crowd[i] = walker;
            }

            File.AppendAllText(output + "/report.txt", $"goblin mesh {goblin.bounds.size.y:F3} m tall at scale 1, was {goblin.bounds.size.y * meanScale:F2} m, now {goblin.bounds.size.y * meanScale * goblinFit:F2} m (x{goblinFit:F2}), footprint {goblin.bounds.size.x * meanScale * goblinFit:F2} x {goblin.bounds.size.z * meanScale * goblinFit:F2} m; slinger {slingerHeight:F2} m\n");

            // Key moments of the throw, turned two ways.
            if (!layoutsOnly)
            {
                StyleLowPolyTests.PlaceCamera(camera, 7f, new Vector3(0f, 0f, 0.2f));
                Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                var phases = new[] { 0.0f, 0.3f, 0.6f, 0.85f, 1.1f, 1.35f, 1.5f, 1.9f };
                for (var i = 0; i < phases.Length; i++)
                {
                    Draw(hurl, phases[i], new Vector3(-5.6f + i * 1.6f, 0f, 1.6f), 160f, false);
                    Draw(hurl, phases[i], new Vector3(-5.6f + i * 1.6f, 0f, -1.7f), 250f, false);
                }

                yield return StyleLowPolyTests.Capture(camera, target, texture, output + "/lineup.png");
                used = 0;
            }

            // Two throw cycles close up, front-side and side; the walk next to them for reference.
            if (!layoutsOnly)
            {
                var folder = output + "/clip-close";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, 4f, new Vector3(0f, 0f, 0.3f));
                for (var frame = 0; frame < 156; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    Draw(hurl, time, new Vector3(-1.6f, 0f, 0f), 150f, false);
                    Draw(hurl, time, new Vector3(1.6f, 0f, 0f), 250f, false);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                    used = 0;
                }
            }

            // The tipi manned by throwing warriors with the horde passing, from the gameplay camera.
            var posts = Posts(towerPositions);
            var targets = new Dictionary<long, int>();
            foreach (var (height, frames, lookAt) in new[]
            {
                (11f, 180, towerPositions[0] + new Vector3(0f, 0f, -3.5f)),
                (14f, 180, towerPositions[0] + new Vector3(0f, 0f, -5f)),
            })
            {
                if (layoutsOnly) break;
                var folder = $"{output}/clip-tipi-{height:F0}m";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, height, lookAt);
                for (var frame = 0; frame < frames; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    foreach (var position in towerPositions)
                    {
                        Graphics.DrawMesh(tower, Matrix4x4.TRS(position, Quaternion.Euler(0f, Facing(position), 0f), Vector3.one), staticMaterial, 0);
                    }

                    DrawSlingers(null, pebble, null, staticMaterial, posts, crowd, targets, time, launch / DefenderScale, throwRelease,
                        (position, yaw, local) => Draw(hurl, local, position, yaw, true));
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, time);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                    used = 0;
                }
            }

            // The tipi with eight slingers, the most an upgrade will give, all on the one deck in an arc
            // (user's choice, 2026-11-07, over a two-storey tipi). Angles are in the tower's frame,
            // where 270 degrees is the ladder, facing the road. Each seat was picked with
            // Tools/Animation/meshy/footing.py so that the feet stay on the planks through the whole
            // throw, whichever way the slinger turns: not over the edge, not on a tusk. Heights are the
            // planks under each seat.
            var eightSeats = new (float angle, float radius, float height)[]
            {
                (165f, 2.4f, 2.54f), (190f, 2.05f, 2.52f), (222.5f, 2.15f, 2.54f), (250f, 2.05f, 2.53f),
                (297.5f, 2.05f, 2.54f), (325f, 2.2f, 2.54f), (352.5f, 2.05f, 2.53f), (17.5f, 2.3f, 2.53f),
            };
            var eightPosts = Posts(towerPositions, eightSeats);
            var eightTargets = new Dictionary<long, int>();
            foreach (var (height, lookAt) in new[] { (9f, towerPositions[0] + new Vector3(0f, 0f, -2.2f)), (13f, towerPositions[0] + new Vector3(0f, 0f, -4.5f)) })
            {
                var folder = $"{output}/eight-slingers-{height:F0}m";
                Directory.CreateDirectory(folder);
                StyleLowPolyTests.PlaceCamera(camera, height, lookAt);
                for (var frame = 0; frame < 156; frame++)
                {
                    var time = frame / 30f;
                    Shader.SetGlobalFloat("_StyleTime", time);
                    foreach (var position in towerPositions)
                    {
                        Graphics.DrawMesh(tower, Matrix4x4.TRS(position, Quaternion.Euler(0f, Facing(position), 0f), Vector3.one), staticMaterial, 0);
                    }

                    DrawSlingers(null, pebble, null, staticMaterial, eightPosts, crowd, eightTargets, time, launch / DefenderScale, throwRelease,
                        (position, yaw, local) => Draw(hurl, local, position, yaw, true));
                    Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0);
                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, time);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                    used = 0;
                }
            }

            camera.targetTexture = null;
            target.Release();
            foreach (var item in new Object[] { target, texture, cameraObject, groundMesh, groundMaterial, unitMaterial, staticMaterial, body, rope, bodyDeck, ropeDeck, goblin, pebble, tower, terrain, rig })
            {
                Object.DestroyImmediate(item);
            }

            foreach (var mesh in pool) Object.DestroyImmediate(mesh);
        }

        // Defenders are drawn larger than life (about 1.75 m) so they match the goblins' bulk.
        private static float Scale(int type) => DefenderScale * (type == DefenderMeshes.Thrower ? ThrowerScale : 1f);

        // Ladders face the road.
        private static float Facing(Vector3 tower) => tower.z > StyleLowPolyTests.RoadCenter(tower.x) ? 0f : 180f;

        // One slinger on a walkway: where it stands, which way is straight out, its cycle offset.
        private struct Post
        {
            public Vector3 Position;
            public float Outward;
            public float Offset;
        }

        // Posts at given places on each tower: angle in the tower's frame, distance from its axis, height.
        private static List<Post> Posts(List<Vector3> towers, (float angle, float radius, float height)[] seats)
        {
            var posts = new List<Post>();
            for (var i = 0; i < towers.Count; i++)
            {
                var place = Matrix4x4.TRS(towers[i], Quaternion.Euler(0f, Facing(towers[i]), 0f), Vector3.one);
                for (var slot = 0; slot < seats.Length; slot++)
                {
                    var angle = seats[slot].angle * Mathf.Deg2Rad;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var direction = place.MultiplyVector(outward);
                    posts.Add(new Post
                    {
                        Position = place.MultiplyPoint3x4(outward * seats[slot].radius + Vector3.up * seats[slot].height),
                        Outward = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                        Offset = Mathf.Repeat(slot * 0.29f + i * 0.37f, 1f) * 0.999f,
                    });
                }
            }

            return posts;
        }

        private static List<Post> Posts(List<Vector3> towers)
        {
            var posts = new List<Post>();
            for (var i = 0; i < towers.Count; i++)
            {
                var place = Matrix4x4.TRS(towers[i], Quaternion.Euler(0f, Facing(towers[i]), 0f), Vector3.one);
                for (var slot = 0; slot < PostAngles.Length; slot++)
                {
                    var angle = PostAngles[slot] * Mathf.Deg2Rad;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var direction = place.MultiplyVector(outward);
                    posts.Add(new Post
                    {
                        Position = place.MultiplyPoint3x4(outward * PostRadius + Vector3.up * DeckHeight),
                        Outward = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                        Offset = Mathf.Repeat(slot * 0.29f + i * 0.37f, 1f) * 0.999f,
                    });
                }
            }

            return posts;
        }

        // The goblin a slinger shoots in a given cycle: the nearest one at the moment of release.
        private static int Target(List<StyleLowPolyTests.Walker> crowd, Dictionary<long, int> targets, int post, Post slot, int cycle, float releaseTime = Release)
        {
            var key = ((long)post << 32) + cycle + 100000;
            if (targets.TryGetValue(key, out var index)) return index;
            var release = (cycle - slot.Offset) * Period + releaseTime;
            var best = float.MaxValue;
            for (var i = 0; i < crowd.Count; i++)
            {
                var position = StyleLowPolyTests.WalkerPosition(crowd[i], release);
                var distance = (position.x - slot.Position.x) * (position.x - slot.Position.x) + (position.z - slot.Position.z) * (position.z - slot.Position.z);
                if (distance < best)
                {
                    best = distance;
                    index = i;
                }
            }

            targets[key] = index;
            return index;
        }

        private static float AimAt(Post slot, StyleLowPolyTests.Walker walker, float release)
        {
            var direction = StyleLowPolyTests.WalkerPosition(walker, release) - slot.Position;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        // Slingers turned towards their targets, the stone of each shot in flight and a few
        // chips bursting off where it lands.
        private static void DrawSlingers(Mesh slinger, Mesh pebble, Material slingerMaterial, Material pebbleMaterial, List<Post> posts,
            List<StyleLowPolyTests.Walker> crowd, Dictionary<long, int> targets, float time, Vector3? stoneStart = null, float releaseTime = Release,
            System.Action<Vector3, float, float> drawBody = null)
        {
            // Where the stone leaves the sling, relative to the slinger, before turning to the aim.
            var launch = stoneStart.HasValue ? stoneStart.Value * DefenderScale : new Vector3(0.24f, 1.52f, 0.62f);
            var bodies = new List<Matrix4x4>();
            var seeds = new List<float>();
            var stones = new List<Matrix4x4>();
            for (var p = 0; p < posts.Count; p++)
            {
                var slot = posts[p];
                var cycle = time / Period + slot.Offset;
                var k = Mathf.FloorToInt(cycle);
                var local = (cycle - k) * Period;
                var release = (k - slot.Offset) * Period + releaseTime;
                var walker = crowd[Target(crowd, targets, p, slot, k, releaseTime)];
                var aim = AimAt(slot, walker, release);
                var previous = AimAt(slot, crowd[Target(crowd, targets, p, slot, k - 1, releaseTime)], release - Period);
                // Turn to the new target at the start of the cycle, while raising the sling.
                var yaw = Mathf.LerpAngle(previous, aim, Mathf.Clamp01(local / 0.15f));
                if (drawBody != null)
                {
                    drawBody(slot.Position, yaw, local);
                }
                else
                {
                    bodies.Add(Matrix4x4.TRS(slot.Position, Quaternion.Euler(0f, yaw, 0f), Vector3.one * DefenderScale));
                    seeds.Add(DefenderMeshes.Slinger + slot.Offset);
                }

                var since = time - release;
                if (since < 0f) continue;
                var start = slot.Position + Quaternion.Euler(0f, aim, 0f) * launch;
                var distance = Vector3.Distance(start, StyleLowPolyTests.WalkerPosition(walker, release));
                var flight = Mathf.Clamp(distance / 20f, 0.35f, 0.9f);
                // Aim where the goblin will be when the stone lands.
                var end = StyleLowPolyTests.WalkerPosition(walker, release + flight) + Vector3.up * 0.35f;
                if (since < flight)
                {
                    var u = since / flight;
                    var arc = 0.6f + distance * 0.05f;
                    var at = Vector3.Lerp(start, end, u) + Vector3.up * (arc * 4f * u * (1f - u));
                    stones.Add(Matrix4x4.TRS(at, Quaternion.Euler(since * 900f, since * 600f, 0f), Vector3.one * 0.12f));
                }
                else if (since < flight + 0.35f)
                {
                    var s = since - flight;
                    for (var c = 0; c < 5; c++)
                    {
                        var angle = (c * 72f + p * 37f) * Mathf.Deg2Rad;
                        var at = end + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (1.4f * s) + Vector3.up * (1.8f * s - 4.9f * s * s);
                        if (at.y > 0f) stones.Add(Matrix4x4.TRS(at, Quaternion.Euler(c * 50f, s * 700f, 0f), Vector3.one * 0.05f));
                    }
                }
            }

            if (bodies.Count > 0) StyleLowPolyTests.DrawInstanced(slinger, slingerMaterial, bodies, seeds);
            if (stones.Count > 0) StyleLowPolyTests.DrawInstanced(pebble, pebbleMaterial, stones, new List<float>(new float[stones.Count]));
        }
    }

    /// <summary>
    /// Prehistoric tower defenders in metres, about 1.1 m tall, facing +Z. Parts and items are
    /// encoded for StyleDefender.shader; shoulder and hand pivots must match it.
    /// </summary>
    internal static class DefenderMeshes
    {
        public const int Archer = 0;
        public const int Slinger = 1;
        public const int Thrower = 2;
        public const int Spearman = 3;

        private const float Upper = 0f;
        private const float LeftArm = 0.25f;
        private const float RightArm = 0.5f;
        private const float Static = 1f;
        private const float Hides = 0.25f;
        private const float Free = 0.5f;
        private const float FreeHides = 0.75f;

        private static readonly Vector3 HandL = new Vector3(-0.23f, 0.38f, 0.04f);
        private static readonly Vector3 HandR = new Vector3(0.23f, 0.38f, 0.04f);

        private static readonly Color SkinColor = new Color(0.8f, 0.58f, 0.42f);
        private static readonly Color Hair = new Color(0.22f, 0.14f, 0.09f);
        private static readonly Color Fur = new Color(0.76f, 0.67f, 0.54f);
        private static readonly Color FurDark = new Color(0.46f, 0.37f, 0.28f);
        private static readonly Color Leather = new Color(0.52f, 0.33f, 0.19f);
        private static readonly Color Rope = new Color(0.82f, 0.72f, 0.5f);
        private static readonly Color Wood = new Color(0.5f, 0.33f, 0.18f);
        private static readonly Color Bone = new Color(0.92f, 0.88f, 0.72f);
        private static readonly Color Flint = new Color(0.42f, 0.43f, 0.47f);
        private static readonly Color Stone = new Color(0.62f, 0.58f, 0.52f);
        private static readonly Color PlayerBlue = new Color(0.22f, 0.5f, 0.95f);
        private static readonly Color Dark = new Color(0.1f, 0.08f, 0.07f);

        // Pivots of the procedural defenders (StyleDefender.shader defaults) and of the TRELLIS
        // slinger (read off its silhouette, 1.2 tall), plus where the stone sits in each sling.
        public static readonly Vector3 ProceduralShoulder = new Vector3(0.21f, 0.8f, 0f);
        public static readonly Vector3 ProceduralHand = new Vector3(0.23f, 0.38f, 0.04f);
        public const float ProceduralHip = 0.45f;
        public static readonly Vector3 ProceduralStone = new Vector3(0f, -0.31f, 0f);
        public static readonly Vector3 TrellisShoulder = new Vector3(0.3f, 0.78f, 0f);
        public static readonly Vector3 TrellisHand = new Vector3(0.41f, 0.42f, 0.03f);
        public const float TrellisHip = 0.47f;
        public static readonly Vector3 TrellisStone = new Vector3(0f, -0.34f, 0f);

        // The TRELLIS model baked with parts, with the procedural slinger's sling hanging from
        // its right fist.
        public static Mesh TrellisSlinger(string path)
        {
            var kit = new MeshKit(410);
            TowerMeshes.AddBaked(kit, path);
            kit.Part = RightArm;
            kit.Item = Free;
            var hand = TrellisHand;
            foreach (var side in new[] { -1f, 1f })
            {
                kit.Tube(hand + new Vector3(side * 0.014f, -0.05f, 0f), hand + new Vector3(side * 0.035f, TrellisStone.y, 0f), 0.009f, 0.009f, 4, Rope);
            }

            kit.Rock(hand + TrellisStone + new Vector3(0f, -0.025f, 0f), new Vector3(0.055f, 0.033f, 0.055f), Leather, Quaternion.identity, 2, 6, 0.05f);
            kit.Item = FreeHides;
            kit.Rock(hand + TrellisStone, new Vector3(0.04f, 0.037f, 0.04f), Stone, Quaternion.identity, 2, 6, 0.1f);
            kit.Item = 0f;
            kit.Part = Static;
            return kit.ToMesh("TrellisSlinger");
        }

        // Where the sling stone is at time t of the slinger's cycle, in mesh space: the shader's
        // slinger pose (StyleDefender.shader Pose type 1 and Animate) without the idle sway.
        public static Vector3 SlingStone(float t, Vector3 shoulder, Vector3 hand, float hip, Vector3 stone)
        {
            t = Mathf.Floor(t * 16f) / 16f;
            var right0 = new Vector3(0.05f, -0.1f, 0f);
            var right = Vector3.Lerp(right0, new Vector3(-0.2f, -2.9f, 0f), Mathf.Clamp01(t / 0.15f));
            var pitch = Mathf.Lerp(0f, -1.5708f, Mathf.Clamp01(t / 0.15f));
            var spin = (Mathf.Clamp(t, 0.15f, 0.68f) - 0.15f) * 25.13f;
            right = Vector3.Lerp(right, new Vector3(-0.1f, -1.2f, 0f), Mathf.Clamp01((t - 0.68f) / 0.08f));
            var lean = Mathf.Lerp(0f, 0.12f, Mathf.Clamp01((t - 0.68f) / 0.08f));
            right = Vector3.Lerp(right, right0, Mathf.Clamp01((t - 0.76f) / 0.29f));
            lean = Mathf.Lerp(lean, 0f, Mathf.Clamp01((t - 0.76f) / 0.29f));
            pitch = Mathf.Lerp(pitch, 0f, Mathf.Clamp01((t - 0.76f) / 0.29f));

            Vector3 RotX(Vector3 p, float a) => new Vector3(p.x, p.y * Mathf.Cos(a) - p.z * Mathf.Sin(a), p.y * Mathf.Sin(a) + p.z * Mathf.Cos(a));
            Vector3 RotY(Vector3 p, float a) => new Vector3(p.x * Mathf.Cos(a) + p.z * Mathf.Sin(a), p.y, -p.x * Mathf.Sin(a) + p.z * Mathf.Cos(a));
            Vector3 RotZ(Vector3 p, float a) => new Vector3(p.x * Mathf.Cos(a) - p.y * Mathf.Sin(a), p.x * Mathf.Sin(a) + p.y * Mathf.Cos(a), p.z);
            var carried = RotX(RotZ(hand - shoulder, right.x), right.y) + shoulder + new Vector3(0f, 0f, right.z);
            var position = carried + RotY(RotX(stone, pitch), spin);
            var pivot = new Vector3(0f, hip, 0f);
            return RotX(position - pivot, lean) + pivot;
        }

        // The rigged TRELLIS slinger for StyleSlingerVat.shader: the clip is a texture of vertex
        // positions (one row per frame), the mesh one vertex per triangle corner carrying its
        // texture column and its triangle's other two columns. Header of the rig file:
        // "vat vertices frames fps period release stoneX stoneY stoneZ".
        public static Mesh VatSlinger(string rigPath, string vatPath, out Texture2D clip, out float release, out Vector3 stone)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var lines = File.ReadAllLines(rigPath);
            var head = lines[0].Split(' ');
            var count = int.Parse(head[1], culture);
            var frames = int.Parse(head[2], culture);
            release = float.Parse(head[5], culture);
            stone = new Vector3(float.Parse(head[6], culture), float.Parse(head[7], culture), float.Parse(head[8], culture));
            var bytes = File.ReadAllBytes(vatPath);
            Assert.That(bytes.Length, Is.EqualTo(count * frames * 8));
            clip = new Texture2D(count, frames, TextureFormat.RGBAHalf, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "SlingerClip",
            };
            clip.LoadRawTextureData(bytes);
            clip.Apply(false, false);

            var rest = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                rest[i] = new Vector3(
                    Mathf.HalfToFloat(System.BitConverter.ToUInt16(bytes, i * 8)),
                    Mathf.HalfToFloat(System.BitConverter.ToUInt16(bytes, i * 8 + 2)),
                    Mathf.HalfToFloat(System.BitConverter.ToUInt16(bytes, i * 8 + 4)));
            }

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var skins = new List<Vector2>();
            var corners = new List<Vector3>();
            var triangles = new List<int>();
            for (var l = 1; l < lines.Length; l++)
            {
                var v = lines[l].Split(' ');
                if (v.Length < 7) continue;
                var ids = new[] { int.Parse(v[0], culture), int.Parse(v[1], culture), int.Parse(v[2], culture) };
                var color = new Color(float.Parse(v[3], culture), float.Parse(v[4], culture), float.Parse(v[5], culture));
                var skin = float.Parse(v[6], culture);
                for (var k = 0; k < 3; k++)
                {
                    triangles.Add(vertices.Count);
                    vertices.Add(rest[ids[k]]);
                    colors.Add(color);
                    skins.Add(new Vector2(skin, 0f));
                    corners.Add(new Vector3(ids[k], ids[(k + 1) % 3], ids[(k + 2) % 3]));
                }
            }

            var mesh = new Mesh { name = "VatSlinger", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, skins);
            mesh.SetUVs(1, corners);
            mesh.SetTriangles(triangles, 0);
            // The clip reaches well outside the rest pose (sling overhead and forward).
            mesh.bounds = new Bounds(new Vector3(0f, 0.8f, 0.2f), new Vector3(2.4f, 2.4f, 2.4f));
            return mesh;
        }

        // Sling stone, about 1 unit across; scaled when drawn.
        public static Mesh Pebble()
        {
            var kit = new MeshKit(499);
            kit.Rock(Vector3.zero, new Vector3(1f, 0.85f, 1f), Stone * 1.1f, Quaternion.identity, 2, 6, 0.12f);
            return kit.ToMesh("Pebble");
        }

        public static Mesh Defender(int type)
        {
            var kit = new MeshKit(400 + type);

            // Legs stay planted; everything above the hip leans with the shot.
            kit.Part = Static;
            foreach (var side in new[] { -1f, 1f })
            {
                kit.Skin = 1f;
                kit.Tube(new Vector3(side * 0.09f, 0.06f, 0f), new Vector3(side * 0.1f, 0.47f, 0f), 0.06f, 0.075f, 6, SkinColor);
                kit.Skin = 0f;
                kit.Rock(new Vector3(side * 0.09f, 0.05f, 0.04f), new Vector3(0.07f, 0.055f, 0.11f), kit.Vary(FurDark, 0.05f), Quaternion.identity, 2, 6, 0.1f);
                kit.Tube(new Vector3(side * 0.09f, 0.1f, 0f), new Vector3(side * 0.09f, 0.17f, 0f), 0.07f, 0.068f, 6, kit.Vary(Leather, 0.05f));
            }

            if (type == Thrower)
            {
                // Spare rocks at his feet.
                kit.Rock(new Vector3(-0.22f, 0.07f, 0.18f), new Vector3(0.09f, 0.07f, 0.08f), kit.Vary(Stone, 0.1f), null, 2, 6);
                kit.Rock(new Vector3(-0.3f, 0.06f, 0.02f), new Vector3(0.08f, 0.06f, 0.08f), kit.Vary(Stone, 0.1f), null, 2, 6);
            }

            kit.Part = Upper;
            var bare = type == Slinger || type == Thrower;
            kit.Tube(new Vector3(0f, 0.32f, 0f), new Vector3(0f, 0.56f, 0f), 0.2f, 0.16f, 9, kit.Vary(type == Thrower ? FurDark : Fur, 0.05f), 0.12f);
            kit.Tube(new Vector3(0f, 0.52f, 0f), new Vector3(0f, 0.58f, 0f), 0.17f, 0.17f, 9, kit.Vary(Leather * 0.8f, 0.04f));
            kit.Skin = bare ? 1f : 0f;
            kit.Tube(new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.84f, 0f), 0.15f, 0.19f, 8, bare ? SkinColor : kit.Vary(Leather, 0.05f));
            kit.Skin = 0f;
            if (type == Slinger)
            {
                // Blue war paint across the chest.
                kit.Tube(new Vector3(0f, 0.62f, 0f), new Vector3(0f, 0.65f, 0f), 0.166f, 0.168f, 8, PlayerBlue);
                kit.Tube(new Vector3(0f, 0.71f, 0f), new Vector3(0f, 0.74f, 0f), 0.175f, 0.177f, 8, PlayerBlue);
                kit.Rock(new Vector3(0.17f, 0.5f, 0.06f), new Vector3(0.06f, 0.07f, 0.05f), kit.Vary(Leather, 0.05f), null, 2, 6);
            }
            else
            {
                // Fur mantle over the shoulders: the biggest shape seen from above.
                var size = type == Thrower ? new Vector3(0.31f, 0.09f, 0.2f) : new Vector3(0.27f, 0.075f, 0.17f);
                kit.Rock(new Vector3(0f, 0.83f, -0.01f), size, kit.Vary(type == Thrower ? FurDark : Fur, 0.05f), Quaternion.identity, 3, 8, 0.12f);
            }

            kit.Skin = 1f;
            kit.Tube(new Vector3(0f, 0.83f, 0f), new Vector3(0f, 0.9f, 0f), 0.065f, 0.06f, 6, SkinColor);
            kit.Rock(new Vector3(0f, 0.98f, 0.01f), new Vector3(0.125f, 0.135f, 0.13f), SkinColor, Quaternion.identity, 3, 8, 0.04f);
            kit.Block(new Vector3(0f, 0.97f, 0.135f), new Vector3(0.05f, 0.06f, 0.05f), Quaternion.identity, SkinColor);
            foreach (var side in new[] { -1f, 1f })
            {
                kit.Rock(new Vector3(side * 0.125f, 0.98f, 0f), new Vector3(0.025f, 0.035f, 0.025f), SkinColor, Quaternion.identity, 2, 5, 0.05f);
            }

            kit.Skin = 0f;
            foreach (var side in new[] { -1f, 1f })
            {
                kit.Block(new Vector3(side * 0.045f, 1.0f, 0.118f), new Vector3(0.03f, 0.025f, 0.02f), Quaternion.identity, Dark);
            }

            kit.Block(new Vector3(0f, 1.035f, 0.112f), new Vector3(0.17f, 0.03f, 0.04f), Quaternion.identity, Hair);
            Head(kit, type);

            foreach (var side in new[] { -1f, 1f })
            {
                kit.Part = side < 0f ? LeftArm : RightArm;
                var shoulder = new Vector3(side * 0.21f, 0.8f, 0f);
                // The rock thrower holds his rock in front of his belly with both hands.
                var hand = type == Thrower ? new Vector3(side * 0.11f, 0.43f, 0.17f) : (side < 0f ? HandL : HandR);
                var elbow = Vector3.Lerp(shoulder, hand, 0.5f) + new Vector3(side * 0.03f, 0f, -0.02f);
                kit.Skin = 1f;
                kit.Rock(shoulder, new Vector3(0.08f, 0.08f, 0.08f), SkinColor, Quaternion.identity, 2, 6, 0.05f);
                kit.Tube(shoulder, elbow, 0.062f, 0.052f, 6, SkinColor);
                kit.Tube(elbow, hand, 0.052f, 0.045f, 6, SkinColor);
                kit.Rock(hand, new Vector3(0.05f, 0.055f, 0.05f), SkinColor, Quaternion.identity, 2, 6, 0.05f);
                kit.Skin = 0f;
                kit.Tube(Vector3.Lerp(elbow, hand, 0.62f), Vector3.Lerp(elbow, hand, 0.82f), 0.056f, 0.052f, 6, kit.Vary(Leather, 0.05f));
            }

            Weapon(kit, type);
            return kit.ToMesh(new[] { "Archer", "Slinger", "RockThrower", "SpearThrower" }[type]);
        }

        // Hair and headgear: the other big shape from above, different for each type.
        private static void Head(MeshKit kit, int type)
        {
            switch (type)
            {
                case Archer:
                    kit.Rock(new Vector3(0f, 1.04f, -0.02f), new Vector3(0.135f, 0.09f, 0.14f), Hair, Quaternion.identity, 3, 8, 0.1f);
                    kit.Tube(new Vector3(0f, 0.99f, 0f), new Vector3(0f, 1.035f, 0f), 0.139f, 0.137f, 10, PlayerBlue);
                    kit.TwoSided(new Vector3(-0.07f, 1.03f, -0.1f), new Vector3(-0.05f, 1.32f, -0.17f), new Vector3(-0.12f, 1.29f, -0.14f), PlayerBlue, PlayerBlue * 0.8f);
                    kit.TwoSided(new Vector3(-0.09f, 1.02f, -0.08f), new Vector3(-0.15f, 1.24f, -0.12f), new Vector3(-0.19f, 1.19f, -0.08f), Bone, Bone * 0.8f);
                    // Quiver on the back.
                    kit.Tube(new Vector3(0.08f, 0.52f, -0.2f), new Vector3(0.15f, 0.92f, -0.17f), 0.065f, 0.075f, 7, kit.Vary(Leather, 0.05f), 0f, Dark);
                    for (var i = 0; i < 5; i++)
                    {
                        var start = new Vector3(0.12f + kit.Range(-0.03f, 0.03f), 0.8f, -0.18f + kit.Range(-0.03f, 0.03f));
                        var end = start + new Vector3(0.03f, 0.22f, 0.01f);
                        kit.Tube(start, end, 0.01f, 0.01f, 4, Wood);
                        kit.TwoSided(end, end + new Vector3(0.035f, -0.08f, 0f), end + new Vector3(-0.035f, -0.08f, 0f), i % 2 == 0 ? PlayerBlue : Bone, Bone * 0.8f);
                    }

                    break;
                case Slinger:
                    kit.Rock(new Vector3(0f, 1.04f, -0.02f), new Vector3(0.132f, 0.085f, 0.138f), Hair, Quaternion.identity, 3, 8, 0.08f);
                    kit.Rock(new Vector3(0f, 1.14f, -0.04f), new Vector3(0.055f, 0.06f, 0.055f), Hair, Quaternion.identity, 2, 6, 0.1f);
                    kit.Tube(new Vector3(-0.09f, 1.15f, -0.04f), new Vector3(0.09f, 1.15f, -0.03f), 0.012f, 0.012f, 4, Bone);
                    kit.Tube(new Vector3(0f, 1.1f, -0.04f), new Vector3(0f, 1.12f, -0.04f), 0.06f, 0.058f, 7, PlayerBlue);
                    break;
                case Thrower:
                    // Bald, bearded, a blue stripe painted over the scalp, a bone-tooth necklace.
                    kit.Block(new Vector3(0f, 1.105f, 0f), new Vector3(0.05f, 0.03f, 0.2f), Quaternion.identity, PlayerBlue);
                    kit.Rock(new Vector3(0f, 0.92f, 0.07f), new Vector3(0.11f, 0.08f, 0.09f), Hair, Quaternion.identity, 2, 7, 0.12f);
                    for (var i = 0; i < 7; i++)
                    {
                        var angle = Mathf.Lerp(200f, 340f, i / 6f) * Mathf.Deg2Rad;
                        var at = new Vector3(Mathf.Cos(angle) * 0.13f, 0.86f, -Mathf.Sin(angle) * 0.13f);
                        kit.Tube(at, at + new Vector3(0f, -0.06f, 0.02f), 0.018f, 0f, 4, Bone);
                    }

                    break;
                default:
                    // Wolf hood with ears and snout, blue band, spare spears on the back.
                    kit.Rock(new Vector3(0f, 1.03f, -0.03f), new Vector3(0.15f, 0.11f, 0.16f), kit.Vary(FurDark, 0.04f), Quaternion.identity, 3, 8, 0.08f);
                    kit.Block(new Vector3(0f, 1.08f, 0.1f), new Vector3(0.09f, 0.06f, 0.12f), Quaternion.Euler(-10f, 0f, 0f), FurDark * 0.9f);
                    foreach (var side in new[] { -1f, 1f })
                    {
                        kit.Tube(new Vector3(side * 0.07f, 1.11f, -0.02f), new Vector3(side * 0.09f, 1.22f, -0.03f), 0.04f, 0f, 4, FurDark);
                    }

                    kit.Tube(new Vector3(0f, 0.97f, -0.03f), new Vector3(0f, 1.0f, -0.03f), 0.152f, 0.15f, 9, PlayerBlue);
                    kit.Rock(new Vector3(0f, 0.86f, -0.16f), new Vector3(0.14f, 0.1f, 0.07f), kit.Vary(FurDark, 0.04f), Quaternion.identity, 2, 7, 0.1f);
                    foreach (var side in new[] { -1f, 1f })
                    {
                        var butt = new Vector3(side * 0.18f, 0.35f, -0.22f);
                        var tip = new Vector3(-side * 0.16f, 1.12f, -0.2f);
                        kit.Tube(butt, tip, 0.016f, 0.016f, 4, Wood);
                        kit.Tube(tip, tip + (tip - butt).normalized * 0.13f, 0.035f, 0f, 4, Flint);
                    }

                    break;
            }
        }

        private static void Weapon(MeshKit kit, int type)
        {
            switch (type)
            {
                case Archer:
                {
                    // Bow in the left hand, flat across X so it reads from above: belly at the
                    // grip, tips curving back, a string.
                    kit.Part = LeftArm;
                    kit.Item = Free;
                    var points = new Vector3[7];
                    for (var i = 0; i < points.Length; i++)
                    {
                        var t = i / (points.Length - 1f);
                        points[i] = HandL + new Vector3(Mathf.Lerp(-0.45f, 0.45f, t), 0.03f * Mathf.Sin(Mathf.PI * t), 0.12f * Mathf.Sin(Mathf.PI * t) - 0.12f);
                    }

                    for (var i = 0; i < points.Length - 1; i++)
                    {
                        var middle = Mathf.Abs(i - 2.5f) / 2.5f;
                        kit.Tube(points[i], points[i + 1], Mathf.Lerp(0.026f, 0.014f, middle), Mathf.Lerp(0.026f, 0.014f, Mathf.Abs(i + 1 - 2.5f) / 2.5f), 5, Wood, 0f, null, 0f, 0f);
                    }

                    kit.Tube(HandL + new Vector3(-0.05f, 0f, 0f), HandL + new Vector3(0.05f, 0f, 0f), 0.032f, 0.032f, 6, Leather);
                    kit.Tube(points[0], points[points.Length - 1], 0.006f, 0.006f, 4, Rope);

                    // Arrow in the right hand, shown from nocking to release.
                    kit.Part = RightArm;
                    kit.Item = FreeHides;
                    kit.Tube(HandR + new Vector3(0f, 0f, -0.05f), HandR + new Vector3(0f, 0f, 0.7f), 0.012f, 0.012f, 4, Wood);
                    kit.Tube(HandR + new Vector3(0f, 0f, 0.69f), HandR + new Vector3(0f, 0f, 0.8f), 0.026f, 0f, 4, Flint);
                    kit.TwoSided(HandR + new Vector3(0f, 0f, 0.1f), HandR + new Vector3(0f, 0.04f, -0.02f), HandR + new Vector3(0f, -0.04f, -0.02f), PlayerBlue, PlayerBlue * 0.8f);
                    kit.TwoSided(HandR + new Vector3(0f, 0f, 0.1f), HandR + new Vector3(0.04f, 0f, -0.02f), HandR + new Vector3(-0.04f, 0f, -0.02f), PlayerBlue, PlayerBlue * 0.8f);
                    break;
                }

                case Slinger:
                    // Sling hanging from the right hand: two cords, a pouch and the stone in it.
                    kit.Part = RightArm;
                    kit.Item = Free;
                    foreach (var side in new[] { -1f, 1f })
                    {
                        kit.Tube(HandR + new Vector3(side * 0.012f, -0.03f, 0f), HandR + new Vector3(side * 0.035f, -0.31f, 0f), 0.008f, 0.008f, 4, Rope);
                    }

                    kit.Rock(HandR + new Vector3(0f, -0.335f, 0f), new Vector3(0.05f, 0.03f, 0.05f), Leather, Quaternion.identity, 2, 6, 0.05f);
                    kit.Item = FreeHides;
                    kit.Rock(HandR + new Vector3(0f, -0.31f, 0f), new Vector3(0.036f, 0.034f, 0.036f), Stone, Quaternion.identity, 2, 6, 0.1f);
                    break;
                case Thrower:
                    // Rock held between the hands; carried by the right arm, both arms move as one.
                    kit.Part = RightArm;
                    kit.Item = Hides;
                    kit.Rock(new Vector3(0f, 0.46f, 0.25f), new Vector3(0.13f, 0.11f, 0.12f), kit.Vary(Stone, 0.06f), null, 3, 7, 0.14f);
                    break;
                default:
                    // Spear held upright in the right hand at rest, levelled for the throw.
                    kit.Part = RightArm;
                    kit.Item = FreeHides;
                    kit.Tube(HandR + new Vector3(0f, 0f, -0.35f), HandR + new Vector3(0f, 0f, 0.75f), 0.022f, 0.02f, 5, Wood * 1.15f);
                    kit.Tube(HandR + new Vector3(0f, 0f, 0.72f), HandR + new Vector3(0f, 0f, 0.95f), 0.05f, 0f, 5, Flint);
                    kit.Tube(HandR + new Vector3(0f, 0f, 0.7f), HandR + new Vector3(0f, 0f, 0.75f), 0.03f, 0.03f, 5, Rope);
                    break;
            }

            kit.Item = 0f;
        }
    }
}
