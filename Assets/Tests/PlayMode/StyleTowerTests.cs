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
    /// Throwaway tower detail test: four prehistoric tower designs under 10K triangles each, in
    /// the low-poly style of StyleLowPolyTests. Renders a line-up at 14 m, a turntable per tower
    /// and each tower placed in the level at 20 and 60 m. Writes PNGs to Logs/StyleTowers.
    /// </summary>
    public sealed class StyleTowerTests
    {
        private const string Output = "Logs/StyleTowers";
        private const int TriangleLimit = 10000;
        private const int TurntableFrames = 48;
        private const string MeshyTipi = "Assets/Tests/PlayMode/StyleAssets/meshy-tipi.txt";

        [UnityTest, Explicit("Tower detail test")]
        [Category("Performance")]
        public IEnumerator RenderTowerVariants()
        {
            Directory.CreateDirectory(Output);
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            Assert.That(unitShader, Is.Not.Null);
            Assert.That(groundShader, Is.Not.Null);

            var variants = new[]
            {
                ("a-log-watchtower", TowerMeshes.LogWatchtower(true)),
                ("b-stone-tower", TowerMeshes.StoneTower()),
                ("c-tipi-tower", TowerMeshes.TipiTower()),
                ("d-log-watchtower-light", TowerMeshes.LogWatchtower(false)),
                ("e-meshy-tipi", TowerMeshes.Baked(MeshyTipi, "MeshyTipi")),
            };
            var report = new StringBuilder();
            foreach (var (name, mesh) in variants)
            {
                var triangles = (int)mesh.GetIndexCount(0) / 3;
                report.AppendLine($"{name}: {triangles} triangles, {mesh.vertexCount} vertices");
                Assert.That(triangles, Is.LessThan(TriangleLimit), name);
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

            // The game bootstrap builds a level map in every test scene; hide it.
            var hiddenRenderers = new List<Renderer>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    hiddenRenderers.Add(renderer);
                }
            }

            var cameraObject = new GameObject("Tower camera") { tag = "MainCamera" };
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
            var ground = new GameObject("Tower ground");
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundMaterial = new Material(groundShader);
            groundMaterial.SetColor("_Color", StyleLowPolyTests.Ground);
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var unitMaterial = new Material(unitShader) { enableInstancing = true };
            var staticMaterial = new Material(unitShader) { enableInstancing = true };
            staticMaterial.SetFloat("_Animate", 0f);

            var goblin = LowPolyMeshes.Goblin();
            var towerPositions = StyleLowPolyTests.TowerPositions();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, towerPositions);
            var crowd = StyleLowPolyTests.BuildCrowd();
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            Shader.SetGlobalFloat("_StyleTime", 0.4f);

            // Line-up at the closest gameplay zoom with the horde passing in front.
            {
                const float row = 18.5f;
                StyleLowPolyTests.PlaceCamera(camera, 14f, new Vector3(0f, 0f, row + 0.5f));
                for (var i = 0; i < 4; i++)
                {
                    Graphics.DrawMesh(variants[i].Item2, Matrix4x4.Translate(new Vector3(-9.9f + i * 6.6f, 0f, row)), staticMaterial, 0);
                }

                StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, 0.4f);
                yield return StyleLowPolyTests.Capture(camera, target, texture, Output + "/lineup-14m.png");
            }

            // The procedural tipi next to the Meshy one converted to flat per-face colours.
            {
                const float row = 18.5f;
                StyleLowPolyTests.PlaceCamera(camera, 11f, new Vector3(0f, 0f, row + 0.5f));
                Graphics.DrawMesh(variants[2].Item2, Matrix4x4.Translate(new Vector3(-4.4f, 0f, row)), staticMaterial, 0);
                Graphics.DrawMesh(variants[4].Item2, Matrix4x4.Translate(new Vector3(4.6f, 0f, row)), staticMaterial, 0);
                StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, 0.4f);
                yield return StyleLowPolyTests.Capture(camera, target, texture, Output + "/compare-tipi-11m.png");
            }

            var scaleMatrices = new List<Matrix4x4>
            {
                StyleLowPolyTests.Matrix(3.5f, -1.6f, 0.45f, 200f),
                StyleLowPolyTests.Matrix(-3.4f, -1.0f, 0.44f, 140f),
                StyleLowPolyTests.Matrix(3.2f, 2.0f, 0.43f, 260f),
            };
            var scaleSeeds = new List<float> { 0.1f, 0.4f, 0.7f };
            foreach (var (name, mesh) in variants)
            {
                Directory.CreateDirectory($"{Output}/{name}");

                // Turntable at 9 m with three goblins for scale.
                StyleLowPolyTests.PlaceCamera(camera, 9f, new Vector3(0f, 0f, 1.1f));
                for (var frame = 0; frame < TurntableFrames; frame++)
                {
                    var turn = Quaternion.Euler(0f, frame * 360f / TurntableFrames, 0f);
                    Graphics.DrawMesh(mesh, Matrix4x4.Rotate(turn), staticMaterial, 0);
                    StyleLowPolyTests.DrawInstanced(goblin, unitMaterial, scaleMatrices, scaleSeeds);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/{name}/turn_{frame:D3}.png");
                }

                foreach (var height in new[] { 20f, 60f })
                {
                    var lookAt = height < 30f ? towerPositions[0] + new Vector3(-4f, 0f, -6f) : StyleLowPolyTests.Target;
                    StyleLowPolyTests.PlaceCamera(camera, height, lookAt);
                    foreach (var position in towerPositions)
                    {
                        // Ladders and doors face the road.
                        var facing = position.z > StyleLowPolyTests.RoadCenter(position.x) ? 0f : 180f;
                        Graphics.DrawMesh(mesh, Matrix4x4.TRS(position, Quaternion.Euler(0f, facing, 0f), Vector3.one), staticMaterial, 0);
                    }

                    Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                    StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, 0.4f);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/{name}/scene-{height:F0}m.png");
                }
            }

            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(groundMesh);
            Object.DestroyImmediate(unitMaterial);
            Object.DestroyImmediate(staticMaterial);
            Object.DestroyImmediate(goblin);
            Object.DestroyImmediate(terrain);
            foreach (var (_, mesh) in variants) Object.DestroyImmediate(mesh);
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
    }

    /// <summary>
    /// Detailed prehistoric towers in metres, front (ladder or door) towards -Z. All vertices
    /// are static for StyleLowPoly.shader.
    /// </summary>
    internal static class TowerMeshes
    {
        private static readonly Color Stone = new Color(0.62f, 0.58f, 0.52f);
        private static readonly Color Mortar = new Color(0.22f, 0.19f, 0.17f);
        private static readonly Color Wood = new Color(0.42f, 0.27f, 0.15f);
        private static readonly Color Cut = new Color(0.8f, 0.62f, 0.4f);
        private static readonly Color Plank = new Color(0.66f, 0.47f, 0.28f);
        private static readonly Color Thatch = new Color(0.88f, 0.69f, 0.34f);
        private static readonly Color Rope = new Color(0.82f, 0.72f, 0.5f);
        private static readonly Color Hide = new Color(0.6f, 0.42f, 0.27f);
        private static readonly Color HideLight = new Color(0.78f, 0.62f, 0.44f);
        private static readonly Color Fur = new Color(0.74f, 0.66f, 0.54f);
        private static readonly Color Clay = new Color(0.64f, 0.37f, 0.24f);
        private static readonly Color Bone = new Color(0.92f, 0.88f, 0.72f);
        private static readonly Color PlayerBlue = new Color(0.22f, 0.5f, 0.95f);
        private static readonly Color PlayerBlueDark = new Color(0.13f, 0.3f, 0.68f);
        private static readonly Color Dark = new Color(0.1f, 0.08f, 0.07f);

        // Log watchtower: four splayed legs with rope-lashed braces, a plank deck ringed by a
        // palisade, a thatched canopy over one corner and a banner. The light version is the
        // same design with far fewer pieces, to compare how much the detail adds.
        public static Mesh LogWatchtower(bool rich)
        {
            var kit = new MeshKit(rich ? 101 : 102);
            var corners = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            Vector3 Leg(int corner, float y)
            {
                var spread = Mathf.Lerp(1.55f, 1.2f, (y + 0.1f) / 4.9f);
                return new Vector3(corners[corner].x * spread, y, corners[corner].y * spread);
            }

            var logSides = rich ? 8 : 6;
            const float deckY = 3.4f;
            const float deck = deckY + 0.18f;

            // Footing: boulders packed around each leg, loose stones around the base.
            for (var c = 0; c < 4; c++)
            {
                var foot = Leg(c, 0f);
                var count = rich ? 7 : 3;
                for (var k = 0; k < count; k++)
                {
                    var angle = k * Mathf.PI * 2f / count + kit.Range(0f, 0.6f);
                    var distance = kit.Range(0.32f, 0.55f);
                    kit.Rock(foot + new Vector3(Mathf.Cos(angle) * distance, kit.Range(0.04f, 0.12f), Mathf.Sin(angle) * distance),
                        new Vector3(kit.Range(0.2f, 0.32f), kit.Range(0.15f, 0.24f), kit.Range(0.2f, 0.32f)), kit.Vary(Stone, 0.1f),
                        null, rich ? 3 : 2, rich ? 6 : 5);
                }
            }

            if (rich)
            {
                for (var k = 0; k < 18; k++)
                {
                    var angle = kit.Range(0f, Mathf.PI * 2f);
                    var distance = kit.Range(1.9f, 2.7f);
                    kit.Rock(new Vector3(Mathf.Cos(angle) * distance, 0.02f, Mathf.Sin(angle) * distance),
                        new Vector3(kit.Range(0.07f, 0.15f), kit.Range(0.05f, 0.09f), kit.Range(0.07f, 0.15f)), kit.Vary(Stone, 0.12f), null, 2, 5);
                }
            }

            for (var c = 0; c < 4; c++)
            {
                kit.Tube(Leg(c, -0.1f), Leg(c, 4.75f), 0.21f, 0.17f, logSides, kit.Vary(Wood, 0.1f), 0.06f, Cut, 0.25f);
                if (!rich) continue;
                foreach (var y in new[] { 1.15f, 2.55f, deckY - 0.12f, deck + 0.3f, deck + 0.72f })
                {
                    Lash(kit, Leg(c, y), Leg(c, y + 1f) - Leg(c, y), 0.235f);
                }
            }

            // Rails and X braces on each side.
            for (var s = 0; s < 4; s++)
            {
                var c0 = s;
                var c1 = (s + 1) % 4;
                var outward = new Vector3(corners[c0].x + corners[c1].x, 0f, corners[c0].y + corners[c1].y) * 0.5f;
                foreach (var y in rich ? new[] { 1.15f, 2.55f } : new[] { 1.6f })
                {
                    Rail(kit, Leg(c0, y) + outward * 0.17f, Leg(c1, y) + outward * 0.17f, 0.1f, logSides - 1);
                }

                var low = rich ? 1.2f : 0.3f;
                var high = rich ? 2.5f : 1.5f;
                kit.Tube(Leg(c0, low) + outward * 0.3f, Leg(c1, high) + outward * 0.3f, 0.075f, 0.07f, logSides - 2, kit.Vary(Wood, 0.1f), 0.06f, Cut);
                kit.Tube(Leg(c1, low) + outward * 0.36f, Leg(c0, high) + outward * 0.36f, 0.075f, 0.07f, logSides - 2, kit.Vary(Wood, 0.1f), 0.06f, Cut);
                if (rich && s % 2 == 0)
                {
                    kit.Tube(Leg(c0, 0.1f) + outward * 0.3f, Leg(c1, 1.05f) + outward * 0.3f, 0.07f, 0.065f, logSides - 2, kit.Vary(Wood, 0.1f), 0.06f, Cut);
                }
            }

            // Deck: two bearers, joists, then planks of uneven width and length.
            foreach (var z in new[] { -1.15f, 1.15f })
            {
                Rail(kit, new Vector3(-1.7f, deckY - 0.12f, z), new Vector3(1.7f, deckY - 0.12f, z), 0.15f, logSides);
            }

            if (rich)
            {
                foreach (var x in new[] { -0.6f, 0.6f })
                {
                    kit.Tube(new Vector3(x, deckY - 0.02f, -1.6f), new Vector3(x, deckY - 0.02f, 1.6f), 0.1f, 0.1f, 6, kit.Vary(Wood, 0.1f), 0.05f, Cut);
                }
            }

            for (var x = -1.66f; x < 1.6f;)
            {
                var width = kit.Range(0.25f, 0.33f) * (rich ? 1f : 2f);
                kit.Block(new Vector3(x + width * 0.5f, deckY + 0.13f, kit.Range(-0.07f, 0.07f)), new Vector3(width - 0.04f, 0.09f, kit.Range(3.3f, 3.5f)),
                    Quaternion.Euler(kit.Range(-1f, 1f), kit.Range(-1.5f, 1.5f), kit.Range(-1.5f, 1.5f)), kit.Vary(Plank, 0.12f));
                x += width;
            }

            // Ladder up the front to a gap in the palisade.
            {
                var foot = new Vector3(-0.6f, 0f, -2.95f);
                var top = new Vector3(-0.6f, deck + 0.55f, -1.78f);
                foreach (var side in new[] { -1f, 1f })
                {
                    kit.Tube(foot + Vector3.right * 0.26f * side, top + Vector3.right * 0.26f * side, 0.06f, 0.055f, 6, kit.Vary(Wood, 0.08f), 0.05f, Cut);
                }

                var rungs = rich ? 11 : 6;
                for (var i = 1; i <= rungs; i++)
                {
                    var p = Vector3.Lerp(foot, top, i / (rungs + 1f));
                    kit.Tube(p - Vector3.right * 0.34f, p + Vector3.right * 0.34f, 0.04f, 0.04f, 5, kit.Vary(Plank, 0.1f), 0f, Cut);
                }
            }

            // Palisade of sharpened stakes with binding rails on the outside.
            var stakesPerSide = rich ? 13 : 7;
            for (var s = 0; s < 4; s++)
            {
                var a = new Vector3(corners[s].x * 1.66f, 0f, corners[s].y * 1.66f);
                var b = new Vector3(corners[(s + 1) % 4].x * 1.66f, 0f, corners[(s + 1) % 4].y * 1.66f);
                var outward = new Vector3(corners[s].x + corners[(s + 1) % 4].x, 0f, corners[s].y + corners[(s + 1) % 4].y) * 0.5f;
                for (var i = 1; i < stakesPerSide; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)stakesPerSide);
                    if (s == 0 && p.x > -0.98f && p.x < -0.22f) continue;
                    var height = kit.Range(0.85f, 1.05f) - (kit.Chance(0.12f) ? 0.3f : 0f);
                    var lean = outward * kit.Range(0f, 0.08f) + new Vector3(kit.Range(-0.04f, 0.04f), 0f, kit.Range(-0.04f, 0.04f));
                    kit.Tube(p + Vector3.up * (deck - 0.1f), p + lean + Vector3.up * (deck + height), rich ? 0.085f : 0.12f, rich ? 0.075f : 0.11f,
                        rich ? 6 : 5, kit.Vary(Wood, 0.12f), 0.08f, Cut, 0.18f);
                }

                foreach (var y in rich ? new[] { 0.3f, 0.72f } : new[] { 0.55f })
                {
                    var from = a + outward * 0.11f + Vector3.up * (deck + y);
                    var to = b + outward * 0.11f + Vector3.up * (deck + y);
                    if (s == 0)
                    {
                        Rail(kit, from, new Vector3(-0.98f, from.y, from.z), 0.05f, 6);
                        Rail(kit, new Vector3(-0.22f, from.y, from.z), to, 0.05f, 6);
                    }
                    else
                    {
                        Rail(kit, from, to, 0.05f, 6);
                    }
                }
            }

            // Thatched canopy on four poles over the back-left corner.
            var roof = new Vector3(-0.5f, deck, 0.45f);
            const float poleHeight = 1.75f;
            var poles = new[] { new Vector3(-0.75f, 0f, -0.7f), new Vector3(0.75f, 0f, -0.7f), new Vector3(0.75f, 0f, 0.75f), new Vector3(-0.75f, 0f, 0.75f) };
            foreach (var p in poles)
            {
                kit.Tube(roof + p, roof + p + Vector3.up * poleHeight, 0.08f, 0.07f, rich ? 7 : 5, kit.Vary(Wood, 0.1f), 0.05f, Cut);
            }

            if (rich)
            {
                for (var i = 0; i < 4; i++)
                {
                    Rail(kit, roof + poles[i] + Vector3.up * (poleHeight - 0.1f), roof + poles[(i + 1) % 4] + Vector3.up * (poleHeight - 0.1f), 0.06f, 6);
                }
            }

            var roofHeight = rich ? 1.9f : 1.6f;
            var eave = roof + Vector3.up * (poleHeight - 0.15f);
            kit.Thatch(eave, rich ? 1.35f : 1.3f, roofHeight, rich ? 16 : 10, rich ? 5 : 2, Thatch, rich);
            if (rich)
            {
                var apex = eave + Vector3.up * roofHeight;
                kit.Tube(apex - Vector3.up * 0.15f, apex + Vector3.up * 0.12f, 0.14f, 0.09f, 7, kit.Vary(Rope, 0.05f));
                for (var i = 0; i < 5; i++)
                {
                    var angle = i * Mathf.PI * 2f / 5f + 0.3f;
                    var direction = new Vector3(Mathf.Cos(angle), 2.4f, Mathf.Sin(angle)).normalized;
                    kit.Tube(apex - direction * 0.55f, apex + direction * 0.4f, 0.04f, 0.03f, 5, kit.Vary(Wood, 0.1f), 0f, Cut);
                }

                // Things on the open part of the deck, which is what the camera sees most.
                ArrowBasket(kit, new Vector3(0.95f, deck, 0.95f));
                ArrowBasket(kit, new Vector3(1.2f, deck, 0.4f));
                RockPile(kit, new Vector3(0.95f, deck, -0.75f), 8);
                Rug(kit, new Vector3(0.3f, deck + 0.01f, 0.05f), 1.0f, 0.75f, 12f);
                Pot(kit, new Vector3(-1.25f, deck, -1.2f));
                foreach (var x in new[] { 0.45f, 1.05f })
                {
                    Shield(kit, new Vector3(x, deck + 0.5f, -1.66f - 0.16f), new Vector3(0f, 0.7f, -1f), 0.28f);
                }

                Skull(kit, Leg(0, 4.95f), Vector3.back, 0.15f);
                Skull(kit, Leg(1, 4.95f), Vector3.back, 0.15f);
            }

            Banner(kit, new Vector3(0.95f, deck - 0.1f, -0.95f), rich ? 3.9f : 3.4f, Vector3.right, rich ? 1.1f : 0.9f, 1.5f, rich);
            return kit.ToMesh(rich ? "LogWatchtower" : "LogWatchtowerLight");
        }

        // Stone tower: dry-stacked courses around a dark core, a doorway framed by mammoth tusks,
        // cantilevered beams under a round plank deck, a stake ring, a hide awning and a banner.
        public static Mesh StoneTower()
        {
            var kit = new MeshKit(202);
            const float top = 4.3f;
            float Radius(float y) => Mathf.Lerp(2.0f, 1.6f, y / top);
            kit.Tube(new Vector3(0f, -0.1f, 0f), new Vector3(0f, top, 0f), Radius(0f) - 0.1f, Radius(top) - 0.1f, 16, Mortar, 0f, Mortar);

            for (var i = 0; i < 14; i++)
            {
                var angle = (i + kit.Range(-0.15f, 0.15f)) * Mathf.PI * 2f / 14f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (Vector3.Angle(outward, Vector3.back) < 14f) continue;
                kit.Rock(outward * 2.15f + Vector3.up * 0.18f, new Vector3(kit.Range(0.42f, 0.5f), kit.Range(0.3f, 0.38f), kit.Range(0.32f, 0.4f)),
                    kit.Vary(Stone * 0.9f, 0.1f), Quaternion.LookRotation(outward), 3, 7, 0.12f);
            }

            // Courses, each offset by half a stone; stones are left out where the doorway is.
            const float course = 0.42f;
            for (var k = 0; ; k++)
            {
                var y = 0.62f + k * course;
                if (y > top - 0.15f) break;
                var radius = Radius(y);
                var count = Mathf.RoundToInt(Mathf.PI * 2f * radius / 0.9f);
                for (var i = 0; i < count; i++)
                {
                    var angle = (i + (k % 2) * 0.5f + kit.Range(-0.05f, 0.05f)) * Mathf.PI * 2f / count;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    if (y < 2.0f && Vector3.Angle(outward, Vector3.back) < 16f) continue;
                    var halfWidth = Mathf.PI * radius / count * kit.Range(0.92f, 1.02f);
                    kit.Rock(outward * radius + Vector3.up * y, new Vector3(halfWidth, course * 0.5f * kit.Range(0.9f, 1.04f), 0.26f), kit.Vary(Stone, 0.12f),
                        Quaternion.LookRotation(outward) * Quaternion.Euler(kit.Range(-4f, 4f), 0f, kit.Range(-5f, 5f)), 3, 6, 0.08f);
                }
            }

            // Doorway: dark recess, lintel, doorstep, a hide curtain pulled to one side.
            kit.Block(new Vector3(0f, 0.95f, -(Radius(1f) - 0.05f)), new Vector3(0.95f, 1.9f, 0.3f), Quaternion.identity, Dark);
            kit.Block(new Vector3(0f, 2.02f, -(Radius(2f) + 0.02f)), new Vector3(1.5f, 0.28f, 0.6f), Quaternion.Euler(0f, 0f, 2f), kit.Vary(Stone * 0.95f, 0.05f));
            kit.Block(new Vector3(0f, 0.06f, -(Radius(0f) + 0.35f)), new Vector3(1.2f, 0.12f, 0.7f), Quaternion.Euler(0f, 3f, 0f), kit.Vary(Stone * 0.9f, 0.05f));
            {
                var grid = new Vector3[3, 4];
                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < 4; j++)
                    {
                        var x = Mathf.Lerp(-0.45f, -0.05f + j * 0.05f, i / 2f);
                        grid[i, j] = new Vector3(x, 1.85f - j * 0.52f, -(Radius(1f) + 0.13f) - Mathf.Sin(i * 1.7f + j) * 0.03f);
                    }
                }

                kit.Sheet(grid, (i, j) => Hide);
            }

            foreach (var side in new[] { -1f, 1f })
            {
                kit.Horn(new Vector3(side * 0.95f, -0.1f, -(Radius(0f) + 0.45f)), new Vector3(side * 0.35f, 1f, -0.35f), new Vector3(-side, -0.2f, -0.3f),
                    2.4f, 0.15f, 8, kit.Vary(Bone, 0.03f), 1.9f, 7);
            }

            // Cantilevered beams with cut ends, then a round deck.
            var deckY = top + 0.05f;
            for (var i = 0; i < 16; i++)
            {
                var angle = (i + 0.5f) * Mathf.PI * 2f / 16f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                kit.Tube(outward * 0.7f + Vector3.up * deckY, outward * 2.15f + Vector3.up * (deckY + kit.Range(-0.02f, 0.02f)), 0.1f, 0.095f, 6,
                    kit.Vary(Wood, 0.1f), 0.06f, Cut);
            }

            const float deckRadius = 2.0f;
            var deck = deckY + 0.2f;
            for (var x = -deckRadius + 0.05f; x < deckRadius - 0.1f;)
            {
                var width = kit.Range(0.26f, 0.32f);
                var center = x + width * 0.5f;
                var half = Mathf.Sqrt(Mathf.Max(0f, deckRadius * deckRadius - center * center)) - 0.02f;
                if (half > 0.3f)
                {
                    kit.Block(new Vector3(center, deckY + 0.15f, kit.Range(-0.04f, 0.04f)), new Vector3(width - 0.04f, 0.09f, 2f * half * kit.Range(0.94f, 1f)),
                        Quaternion.Euler(kit.Range(-1f, 1f), kit.Range(-1.5f, 1.5f), kit.Range(-1.5f, 1.5f)), kit.Vary(Plank, 0.12f));
                }

                x += width;
            }

            for (var i = 0; i < 14; i++)
            {
                var from = Polar(i * 360f / 14f, 1.95f, deck + 0.02f);
                var to = Polar((i + 1) * 360f / 14f, 1.95f, deck + 0.02f);
                var stretch = (to - from) * 0.04f;
                kit.Tube(from - stretch, to + stretch, 0.08f, 0.08f, 6, kit.Vary(Wood, 0.1f), 0.04f, Cut);
            }

            const int stakes = 26;
            for (var i = 0; i < stakes; i++)
            {
                var angle = i * 360f / stakes;
                var foot = Polar(angle, 1.86f, deck - 0.05f);
                var height = kit.Range(0.75f, 0.95f);
                var tip = foot + Vector3.up * height + Polar(angle, kit.Range(0f, 0.06f));
                kit.Tube(foot, tip, 0.075f, 0.068f, 6, kit.Vary(Wood, 0.12f), 0.08f, Cut, 0.16f);
                if (i % 6 == 3) Skull(kit, tip + Vector3.up * 0.1f, Polar(angle, 1f), 0.13f);
            }

            foreach (var y in new[] { 0.3f, 0.65f })
            {
                for (var i = 0; i < 13; i++)
                {
                    var from = Polar(i * 360f / 13f, 1.96f, deck + y);
                    var to = Polar((i + 1) * 360f / 13f, 1.96f, deck + y);
                    var stretch = (to - from) * 0.05f;
                    kit.Tube(from - stretch, to + stretch, 0.045f, 0.045f, 5, kit.Vary(Wood, 0.1f), 0f, Cut);
                }
            }

            // Hide awning over the back-left of the deck.
            {
                var backA = Polar(118f, 1.62f);
                var backB = Polar(172f, 1.62f);
                var inward = -(backA + backB).normalized;
                var frontA = backA + inward * 1.15f;
                var frontB = backB + inward * 1.15f;
                foreach (var (foot, height) in new[] { (backA, 1.9f), (backB, 1.9f), (frontA, 1.3f), (frontB, 1.3f) })
                {
                    kit.Tube(foot + Vector3.up * (deck - 0.05f), foot + Vector3.up * (deck + height + 0.05f), 0.06f, 0.05f, 6, kit.Vary(Wood, 0.1f), 0.04f, Cut);
                }

                var grid = new Vector3[5, 4];
                for (var i = 0; i < 5; i++)
                {
                    for (var j = 0; j < 4; j++)
                    {
                        var u = i / 4f;
                        var v = j / 3f;
                        var edge = i == 0 || i == 4 || j == 0 || j == 3;
                        var flat = Vector3.Lerp(Vector3.Lerp(backA, backB, u), Vector3.Lerp(frontA, frontB, u), v);
                        if (edge) flat += new Vector3(kit.Range(-0.05f, 0.05f), 0f, kit.Range(-0.05f, 0.05f));
                        var sag = 0.14f * Mathf.Sin(Mathf.PI * u) * Mathf.Sin(Mathf.PI * v);
                        grid[i, j] = flat + Vector3.up * (deck + Mathf.Lerp(1.9f, 1.3f, v) - sag);
                    }
                }

                kit.Sheet(grid, (i, j) => j == 0 ? Hide * 0.85f : (i % 2 == 0 ? HideLight : Color.Lerp(Hide, HideLight, 0.5f)));
            }

            Banner(kit, Polar(38f, 1.45f, deck - 0.1f), 3.6f, Vector3.right, 1.1f, 1.5f, true);
            ArrowBasket(kit, Polar(300f, 1.35f, deck));
            ArrowBasket(kit, Polar(330f, 1.4f, deck));
            RockPile(kit, Polar(250f, 1.3f, deck), 9);
            Pot(kit, Polar(200f, 1.35f, deck));
            Rug(kit, new Vector3(0.35f, deck + 0.01f, -0.2f), 1.1f, 0.8f, -20f);
            for (var i = 0; i < 3; i++)
            {
                var foot = Polar(-15f + i * 15f, 1.5f, deck);
                Spear(kit, foot, foot + Polar(-15f + i * 15f, 0.35f) + Vector3.up * 1.6f);
            }

            return kit.ToMesh("StoneTower");
        }

        // Tipi tower: a rock mound with six lashed stilts, a round deck with a walkway, a pleated
        // hide tipi with a painted band, a horned skull over the door and a fence of rib bones.
        public static Mesh TipiTower()
        {
            var kit = new MeshKit(303);
            const float deckY = 2.5f;
            const float deck = deckY + 0.2f;
            const float stiltRadius = 1.75f;

            for (var i = 0; i < 34; i++)
            {
                var angle = kit.Range(0f, Mathf.PI * 2f);
                var distance = Mathf.Sqrt(kit.Range(0.05f, 1f)) * 2.4f;
                var lift = (1f - distance / 2.6f) * 0.55f;
                kit.Rock(new Vector3(Mathf.Cos(angle) * distance, lift, Mathf.Sin(angle) * distance),
                    new Vector3(kit.Range(0.26f, 0.42f), kit.Range(0.2f, 0.3f), kit.Range(0.26f, 0.42f)), kit.Vary(Stone, 0.12f));
            }

            var feet = new Vector3[6];
            var tops = new Vector3[6];
            for (var i = 0; i < 6; i++)
            {
                var angle = (i + 0.5f) * 60f;
                feet[i] = Polar(angle, 2.0f, 0.15f);
                tops[i] = Polar(angle, stiltRadius, deckY + 0.05f);
                kit.Tube(feet[i], tops[i], 0.17f, 0.15f, 7, kit.Vary(Wood, 0.1f), 0.06f, Cut);
                Lash(kit, Vector3.Lerp(feet[i], tops[i], 0.45f), tops[i] - feet[i], 0.2f);
                Lash(kit, Vector3.Lerp(feet[i], tops[i], 0.93f), tops[i] - feet[i], 0.19f);
            }

            for (var i = 0; i < 6; i++)
            {
                var j = (i + 1) % 6;
                var outward = (Polar((i + 0.5f) * 60f, 1f) + Polar((j + 0.5f) * 60f, 1f)).normalized * 0.15f;
                Rail(kit, Vector3.Lerp(feet[i], tops[i], 0.45f) + outward, Vector3.Lerp(feet[j], tops[j], 0.45f) + outward, 0.09f, 6);
                if (i % 2 == 0)
                {
                    kit.Tube(feet[i] + outward * 1.6f, Vector3.Lerp(feet[j], tops[j], 0.9f) + outward * 1.6f, 0.07f, 0.065f, 6, kit.Vary(Wood, 0.1f), 0.05f, Cut);
                }

                Rail(kit, tops[i] - Vector3.up * 0.08f, tops[j] - Vector3.up * 0.08f, 0.13f, 7);
                var joist = Polar(i * 60f, 1f);
                kit.Tube(joist * 0.3f + Vector3.up * (deckY - 0.02f), joist * 2.05f + Vector3.up * (deckY - 0.02f), 0.09f, 0.09f, 6, kit.Vary(Wood, 0.1f), 0.04f, Cut);
            }

            const float deckRadius = 2.05f;
            for (var x = -deckRadius + 0.05f; x < deckRadius - 0.1f;)
            {
                var width = kit.Range(0.26f, 0.32f);
                var center = x + width * 0.5f;
                var half = Mathf.Sqrt(Mathf.Max(0f, deckRadius * deckRadius - center * center)) - 0.02f;
                if (half > 0.3f)
                {
                    kit.Block(new Vector3(center, deckY + 0.13f, kit.Range(-0.04f, 0.04f)), new Vector3(width - 0.04f, 0.09f, 2f * half * kit.Range(0.94f, 1f)),
                        Quaternion.Euler(kit.Range(-1f, 1f), kit.Range(-1.5f, 1.5f), kit.Range(-1.5f, 1.5f)), kit.Vary(Plank, 0.12f));
                }

                x += width;
            }

            // Tipi: poles crossing above the cover, a pleated hide cover between them.
            const int poles = 10;
            const float tipiRadius = 1.35f;
            const float tipiHeight = 3.7f;
            const float coverHeight = 3.0f;
            const int doorPanel = 7;
            var apex = new Vector3(0f, deck + tipiHeight, 0f);
            for (var i = 0; i < poles; i++)
            {
                var foot = Polar(i * 360f / poles, tipiRadius, deck);
                var direction = (apex - foot).normalized;
                var end = apex + direction * kit.Range(0.55f, 0.85f) + new Vector3(kit.Range(-0.05f, 0.05f), 0f, kit.Range(-0.05f, 0.05f));
                kit.Tube(foot, end, 0.06f, 0.045f, 6, kit.Vary(Wood, 0.1f), 0.05f, Cut);
            }

            Lash(kit, apex - Vector3.up * 0.05f, Vector3.up, 0.17f);

            const int rows = 7;
            const int columns = poles * 2;
            var cover = new Vector3[columns, rows + 1];
            for (var c = 0; c < columns; c++)
            {
                for (var r = 0; r <= rows; r++)
                {
                    var h = coverHeight * r / rows;
                    // Points between poles sit further in, so the hide sags into pleats.
                    var radius = (tipiRadius * (1f - h / tipiHeight) + 0.06f) * (c % 2 == 1 ? 0.92f : 1f);
                    var sag = r == 0 ? kit.Range(0f, 0.06f) : 0f;
                    cover[c, r] = Polar(c * 180f / poles, radius, deck + 0.08f + h - sag);
                }
            }

            var panelColors = new Color[poles];
            for (var p = 0; p < poles; p++)
            {
                panelColors[p] = kit.Vary(p % 3 == 0 ? HideLight : (p % 3 == 1 ? Hide : Color.Lerp(Hide, HideLight, 0.5f)), 0.06f);
            }

            for (var c = 0; c < columns; c++)
            {
                var n = (c + 1) % columns;
                var panel = c / 2;
                for (var r = 0; r < rows; r++)
                {
                    if (panel == doorPanel && r < 3) continue;
                    var a = cover[c, r];
                    var b = cover[n, r];
                    var d = cover[c, r + 1];
                    var e = cover[n, r + 1];
                    var inside = new Vector3(0f, (a.y + d.y) * 0.5f, 0f);
                    if (r == 3)
                    {
                        // Painted zigzag band in the player colour.
                        kit.Outward(a, b, e, PlayerBlue, inside);
                        kit.Outward(a, e, d, PlayerBlueDark, inside);
                    }
                    else
                    {
                        var shade = r == 0 ? 0.8f : (r == rows - 1 ? 0.7f : 1f);
                        kit.Outward(a, b, e, d, panelColors[panel] * shade, inside);
                    }
                }
            }

            // Dark inside seen through the doorway, a rolled-up flap and a horned skull above.
            var doorHeight = coverHeight * 3f / rows;
            kit.Tube(new Vector3(0f, deck + 0.02f, 0f), new Vector3(0f, deck + doorHeight + 0.2f, 0f), tipiRadius * 0.85f,
                tipiRadius * (1f - (doorHeight + 0.2f) / tipiHeight) * 0.85f, 10, Dark, 0f, Dark);
            var doorTop = tipiRadius * (1f - doorHeight / tipiHeight) + 0.1f;
            kit.Tube(new Vector3(-0.32f, deck + doorHeight, -doorTop), new Vector3(0.32f, deck + doorHeight, -doorTop), 0.075f, 0.075f, 7, Hide * 0.85f, 0.05f);
            {
                var skullHeight = doorHeight + 0.4f;
                var skullAt = new Vector3(0f, deck + skullHeight, -(tipiRadius * (1f - skullHeight / tipiHeight) + 0.22f));
                Skull(kit, skullAt, Vector3.back, 0.2f);
                foreach (var side in new[] { -1f, 1f })
                {
                    kit.Horn(skullAt + new Vector3(side * 0.16f, 0.1f, 0f), new Vector3(side, 0.25f, -0.1f), new Vector3(0f, 1f, -0.4f), 0.75f, 0.075f, 5, kit.Vary(Bone, 0.03f), 1.7f);
                }
            }

            const float bannerAngle = 40f;
            for (var i = 0; i < 16; i++)
            {
                var angle = (i + 0.5f) * 360f / 16f;
                var outward = Polar(angle, 1f);
                if (Vector3.Angle(outward, Vector3.back) < 20f || Mathf.Abs(Mathf.DeltaAngle(angle, bannerAngle)) < 12f) continue;
                kit.Horn(Polar(angle, 1.98f, deck - 0.08f), outward * 0.25f + Vector3.up, outward + Vector3.up * 0.2f, 0.85f, 0.065f, 4, kit.Vary(Bone, 0.04f), 0.9f, 5);
            }

            // Ladder to the front of the deck.
            {
                var foot = new Vector3(0f, 0f, -3.05f);
                var top = new Vector3(0f, deck + 0.45f, -2.0f);
                foreach (var side in new[] { -1f, 1f })
                {
                    kit.Tube(foot + Vector3.right * 0.26f * side, top + Vector3.right * 0.26f * side, 0.06f, 0.055f, 6, kit.Vary(Wood, 0.08f), 0.05f, Cut);
                }

                for (var i = 1; i <= 8; i++)
                {
                    var p = Vector3.Lerp(foot, top, i / 9f);
                    kit.Tube(p - Vector3.right * 0.34f, p + Vector3.right * 0.34f, 0.04f, 0.04f, 5, kit.Vary(Plank, 0.1f), 0f, Cut);
                }
            }

            Banner(kit, Polar(bannerAngle, 1.75f, deck - 0.1f), 4.6f, Vector3.right, 1.0f, 1.4f, true);
            RockPile(kit, Polar(305f, 1.72f, deck), 8);
            ArrowBasket(kit, Polar(235f, 1.72f, deck));
            Pot(kit, Polar(150f, 1.72f, deck));
            Firewood(kit, Polar(100f, 1.72f, deck), Polar(190f, 1f));
            return kit.ToMesh("TipiTower");
        }

        // A model converted outside Unity (Blender, bake_face_colors.py): one line per triangle,
        // 'ax ay az bx by bz cx cy cz nx ny nz r g b', in metres with the front towards -Z.
        public static Mesh Baked(string path, string name)
        {
            var kit = new MeshKit(0);
            AddBaked(kit, path);
            return kit.ToMesh(name);
        }

        // Triangles baked by a Blender script: 'ax ay az bx by bz cx cy cz nx ny nz r g b',
        // optionally followed by 'part skin item' for animated meshes.
        public static void AddBaked(MeshKit kit, string path)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var (part, skin, item) = (kit.Part, kit.Skin, kit.Item);
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split(' ');
                if (parts.Length < 15) continue;
                var v = new float[parts.Length];
                for (var i = 0; i < parts.Length; i++) v[i] = float.Parse(parts[i], culture);
                if (v.Length >= 18)
                {
                    kit.Part = v[15];
                    kit.Skin = v[16];
                    kit.Item = v[17];
                }

                kit.Face(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]), new Vector3(v[6], v[7], v[8]),
                    new Color(v[12], v[13], v[14]), new Vector3(v[9], v[10], v[11]));
            }

            (kit.Part, kit.Skin, kit.Item) = (part, skin, item);
        }

        private static Vector3 Polar(float degrees, float radius, float y = 0f)
        {
            var angle = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        // Log that runs a little past both ends, the way lashed timber does.
        private static void Rail(MeshKit kit, Vector3 from, Vector3 to, float radius, int sides)
        {
            var overhang = (to - from).normalized * 0.18f;
            kit.Tube(from - overhang, to + overhang, radius, radius * 0.92f, sides, kit.Vary(Wood, 0.1f), 0.06f, Cut);
        }

        private static void Lash(MeshKit kit, Vector3 at, Vector3 axis, float radius)
        {
            var half = axis.normalized * 0.075f;
            kit.Tube(at - half, at + half, radius, radius, 7, kit.Vary(Rope, 0.05f));
        }

        private static void Skull(MeshKit kit, Vector3 position, Vector3 facing, float size)
        {
            var forward = new Vector3(facing.x, 0f, facing.z).normalized;
            var turn = Quaternion.LookRotation(forward);
            var right = turn * Vector3.right;
            kit.Rock(position, new Vector3(size, size * 0.85f, size * 1.1f), kit.Vary(Bone, 0.04f), turn, 3, 7, 0.05f);
            kit.Block(position + forward * size * 0.75f - Vector3.up * size * 0.55f, new Vector3(size * 1.1f, size * 0.4f, size * 0.7f), turn, Bone * 0.9f);
            foreach (var side in new[] { -1f, 1f })
            {
                kit.Block(position + forward * size * 0.9f + Vector3.up * size * 0.05f + right * side * size * 0.42f, Vector3.one * size * 0.32f, turn, Dark);
            }
        }

        // Pole with a crossbar and a flag in the player colour that billows towards the camera,
        // so it reads from above; the rich version adds a horned skull and bone weights.
        private static void Banner(MeshKit kit, Vector3 foot, float height, Vector3 side, float width, float length, bool rich)
        {
            var top = foot + Vector3.up * height;
            kit.Tube(foot, top, 0.075f, 0.06f, rich ? 7 : 5, kit.Vary(Wood, 0.08f), 0.04f, Cut);
            var bar = height - 0.25f;
            kit.Tube(foot + Vector3.up * bar - side * 0.12f, foot + Vector3.up * bar + side * (width + 0.15f), 0.045f, 0.04f, 5, kit.Vary(Wood, 0.08f), 0f, Cut);
            var columns = rich ? 5 : 3;
            var rows = rich ? 7 : 3;
            var toward = Vector3.Cross(Vector3.up, side).normalized;
            var grid = new Vector3[columns, rows];
            for (var i = 0; i < columns; i++)
            {
                for (var j = 0; j < rows; j++)
                {
                    var u = i / (columns - 1f);
                    var v = j / (rows - 1f);
                    var drop = length * v;
                    if (j == rows - 1 && i == columns / 2) drop -= 0.35f;
                    var billow = v * 0.45f + Mathf.Sin(u * 3.1f + v * 2f) * 0.1f * v;
                    grid[i, j] = foot + Vector3.up * (bar - 0.05f - drop) + side * (0.06f + u * width) + toward * billow;
                }
            }

            kit.Sheet(grid, (i, j) => j == 0 || (rich && j == rows - 2) ? PlayerBlueDark : PlayerBlue);
            if (!rich) return;
            Skull(kit, top + Vector3.up * 0.1f, toward, 0.14f);
            foreach (var s in new[] { -1f, 1f })
            {
                kit.Horn(top + Vector3.up * 0.16f + side * s * 0.1f, side * s + Vector3.up * 0.6f, Vector3.up, 0.45f, 0.05f, 4, Bone, 1.2f, 5);
            }

            foreach (var i in new[] { 0, columns - 1 })
            {
                var corner = grid[i, rows - 1];
                kit.Tube(corner, corner - Vector3.up * 0.18f, 0.035f, 0f, 5, Bone);
            }
        }

        private static void ArrowBasket(MeshKit kit, Vector3 foot)
        {
            // The dark top cap reads as the open mouth of the basket.
            kit.Tube(foot, foot + Vector3.up * 0.45f, 0.19f, 0.24f, 9, kit.Vary(Rope * 0.8f, 0.05f), 0.04f, Wood * 0.55f);
            for (var i = 0; i < 9; i++)
            {
                var angle = kit.Range(0f, Mathf.PI * 2f);
                var distance = kit.Range(0f, 0.14f);
                var start = foot + new Vector3(Mathf.Cos(angle) * distance, 0.2f, Mathf.Sin(angle) * distance);
                var end = start + new Vector3(kit.Range(-0.12f, 0.12f), 0.75f, kit.Range(-0.12f, 0.12f));
                kit.Tube(start, end, 0.025f, 0.025f, 4, Cut);
                var along = (end - start).normalized;
                var across = Vector3.Cross(along, Vector3.forward).normalized * 0.075f;
                kit.TwoSided(end, end - along * 0.2f + across, end - along * 0.2f - across, Bone, Bone * 0.8f);
            }
        }

        private static void RockPile(MeshKit kit, Vector3 center, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var angle = kit.Range(0f, Mathf.PI * 2f);
                var distance = i < count * 0.7f ? kit.Range(0.05f, 0.28f) : kit.Range(0f, 0.12f);
                var size = kit.Range(0.08f, 0.13f);
                kit.Rock(center + new Vector3(Mathf.Cos(angle) * distance, i < count * 0.7f ? 0.07f : 0.19f, Mathf.Sin(angle) * distance),
                    new Vector3(size, size * 0.8f, size), kit.Vary(Stone, 0.12f), null, 2, 6);
            }
        }

        private static void Rug(MeshKit kit, Vector3 center, float width, float depth, float yaw)
        {
            const int n = 4;
            var turn = Quaternion.Euler(0f, yaw, 0f);
            var grid = new Vector3[n, n];
            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                {
                    var edge = i == 0 || j == 0 || i == n - 1 || j == n - 1;
                    var stretch = edge ? kit.Range(0.9f, 1.08f) : 1f;
                    grid[i, j] = center + turn * new Vector3((i / (n - 1f) - 0.5f) * width * stretch, kit.Range(0f, 0.015f), (j / (n - 1f) - 0.5f) * depth * stretch);
                }
            }

            kit.Sheet(grid, (i, j) => j == 1 ? Fur * 0.8f : Fur);
        }

        private static void Pot(MeshKit kit, Vector3 foot)
        {
            var clay = kit.Vary(Clay, 0.06f);
            kit.Tube(foot, foot + Vector3.up * 0.22f, 0.13f, 0.2f, 9, clay, 0f, null, 0f, 0f);
            kit.Tube(foot + Vector3.up * 0.22f, foot + Vector3.up * 0.38f, 0.2f, 0.12f, 9, clay, 0f, null, 0f, 0f);
            kit.Tube(foot + Vector3.up * 0.38f, foot + Vector3.up * 0.44f, 0.12f, 0.13f, 9, clay * 0.9f, 0f, Dark, 0f, 0f);
        }

        private static void Spear(MeshKit kit, Vector3 foot, Vector3 tip)
        {
            var along = (tip - foot).normalized;
            kit.Tube(foot, tip, 0.025f, 0.022f, 5, kit.Vary(Wood * 1.2f, 0.05f));
            kit.Tube(tip - along * 0.02f, tip + along * 0.22f, 0.05f, 0f, 5, Bone);
            Lash(kit, tip - along * 0.04f, along, 0.035f);
        }

        private static void Firewood(MeshKit kit, Vector3 center, Vector3 along)
        {
            var across = Vector3.Cross(Vector3.up, along).normalized;
            var offsets = new[] { new Vector2(-0.17f, 0.08f), new Vector2(0f, 0.08f), new Vector2(0.17f, 0.08f), new Vector2(-0.085f, 0.23f), new Vector2(0.085f, 0.23f) };
            foreach (var offset in offsets)
            {
                var middle = center + across * offset.x + Vector3.up * offset.y;
                var length = kit.Range(0.28f, 0.34f);
                kit.Tube(middle - along * length, middle + along * length, 0.08f, 0.08f, 6, kit.Vary(Wood, 0.1f), 0.05f, Cut);
            }
        }

        private static void Shield(MeshKit kit, Vector3 center, Vector3 facing, float radius)
        {
            var normal = facing.normalized;
            kit.Tube(center - normal * 0.03f, center + normal * 0.03f, radius, radius * 0.97f, 10, PlayerBlueDark, 0.02f, PlayerBlue);
            kit.Tube(center + normal * 0.02f, center + normal * 0.09f, radius * 0.25f, radius * 0.18f, 6, Bone, 0f, Bone);
        }
    }

    /// <summary>
    /// Flat-shaded triangle soup for the tower experiments. Every face has its own normal, and
    /// each closed primitive turns its faces away from a point inside it, so winding never has
    /// to be worked out by hand. Vertex colour alpha is 1 (static).
    /// </summary>
    internal sealed class MeshKit
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private readonly System.Random random;

        // Written into every new face: vertex colour alpha (animated part, 1 static) and
        // uv (x skin, y held item), as the unit shaders expect.
        public float Part = 1f;
        public float Skin;
        public float Item;

        public MeshKit(int seed)
        {
            random = new System.Random(seed);
        }

        public float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        public bool Chance(float probability) => random.NextDouble() < probability;

        // Small brightness and warmth change so neighbouring pieces of one material differ.
        public Color Vary(Color color, float amount = 0.08f)
        {
            var brightness = 1f + Range(-amount, amount);
            var warmth = Range(-amount, amount) * 0.5f;
            return new Color(color.r * brightness * (1f + warmth), color.g * brightness, color.b * brightness * (1f - warmth), 1f);
        }

        // One triangle whose front faces along facing.
        public void Face(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 facing)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-10f) return;
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
            var tint = new Color(color.r, color.g, color.b, Part);
            for (var i = 0; i < 3; i++)
            {
                normals.Add(normal);
                colors.Add(tint);
                uvs.Add(new Vector2(Skin, Item));
            }

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        public void Outward(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 inside)
        {
            Face(a, b, c, color, (a + b + c) / 3f - inside);
        }

        public void Outward(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 inside)
        {
            Outward(a, b, c, color, inside);
            Outward(a, c, d, color, inside);
        }

        public void TwoSided(Vector3 a, Vector3 b, Vector3 c, Color front, Color back)
        {
            var normal = Vector3.Cross(b - a, c - a);
            Face(a, b, c, front, normal);
            Face(a, b, c, back, -normal);
        }

        public void TwoSided(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color front, Color back)
        {
            var normal = Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a);
            Face(a, b, c, front, normal);
            Face(a, c, d, front, normal);
            Face(a, b, c, back, -normal);
            Face(a, c, d, back, -normal);
        }

        // Two-sided sheet through a grid of points (cloth, hides, flags).
        public void Sheet(Vector3[,] grid, System.Func<int, int, Color> color)
        {
            for (var i = 0; i < grid.GetLength(0) - 1; i++)
            {
                for (var j = 0; j < grid.GetLength(1) - 1; j++)
                {
                    var tint = color(i, j);
                    TwoSided(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1], tint, tint * 0.8f);
                }
            }
        }

        // Prism between two points with a radius at each end: logs, posts, poles, cones. A point
        // length adds a sharpened tip; cap colours the flat ends (cut wood).
        public void Tube(Vector3 from, Vector3 to, float fromRadius, float toRadius, int sides, Color color,
            float jitter = 0f, Color? cap = null, float point = 0f, float twist = -1f)
        {
            var axis = (to - from).normalized;
            var reference = Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right;
            var u = Vector3.Cross(axis, reference).normalized;
            var v = Vector3.Cross(axis, u);
            if (twist < 0f) twist = Range(0f, Mathf.PI * 2f);
            var a = new Vector3[sides];
            var b = new Vector3[sides];
            for (var i = 0; i < sides; i++)
            {
                var angle = twist + i * Mathf.PI * 2f / sides;
                var direction = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                a[i] = from + direction * fromRadius * (1f + Range(-jitter, jitter));
                b[i] = to + direction * toRadius * (1f + Range(-jitter, jitter));
            }

            var tip = to + axis * point;
            var inside = (from + (point > 0f ? tip : to)) * 0.5f;
            var end = cap ?? color;
            for (var i = 0; i < sides; i++)
            {
                var j = (i + 1) % sides;
                Outward(a[i], a[j], b[j], b[i], color, inside);
                if (point > 0f) Outward(b[i], b[j], tip, color, inside);
                else if (toRadius > 0f) Outward(b[i], b[j], to, end, inside);
                if (fromRadius > 0f) Outward(a[j], a[i], from, end, inside);
            }
        }

        // Lumpy ellipsoid: rings of jittered points between a top and bottom vertex.
        public void Rock(Vector3 center, Vector3 size, Color color, Quaternion? rotation = null, int rings = 3, int segments = 6, float rough = 0.15f)
        {
            var turn = rotation ?? Quaternion.Euler(Range(-10f, 10f), Range(0f, 360f), Range(-10f, 10f));
            var top = center + turn * new Vector3(0f, size.y * (1f + Range(-rough, rough * 0.5f)), 0f);
            var bottom = center - turn * new Vector3(0f, size.y, 0f);
            var ring = new Vector3[rings, segments];
            for (var r = 0; r < rings; r++)
            {
                var phi = Mathf.PI * (r + 1) / (rings + 1);
                for (var s = 0; s < segments; s++)
                {
                    var theta = Mathf.PI * 2f * (s + (r % 2) * 0.5f) / segments;
                    var k = 1f + Range(-rough, rough);
                    ring[r, s] = center + turn * new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta) * size.x * k, Mathf.Cos(phi) * size.y * k, Mathf.Sin(phi) * Mathf.Sin(theta) * size.z * k);
                }
            }

            for (var s = 0; s < segments; s++)
            {
                var n = (s + 1) % segments;
                Outward(top, ring[0, s], ring[0, n], color, center);
                Outward(bottom, ring[rings - 1, n], ring[rings - 1, s], color, center);
                for (var r = 0; r < rings - 1; r++)
                {
                    Outward(ring[r, s], ring[r, n], ring[r + 1, n], ring[r + 1, s], color, center);
                }
            }
        }

        public void Block(Vector3 center, Vector3 size, Quaternion rotation, Color color)
        {
            var half = size * 0.5f;
            var c = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                c[i] = center + rotation * new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            }

            Outward(c[0], c[1], c[3], c[2], color, center);
            Outward(c[4], c[5], c[7], c[6], color, center);
            Outward(c[0], c[1], c[5], c[4], color, center);
            Outward(c[2], c[3], c[7], c[6], color, center);
            Outward(c[0], c[2], c[6], c[4], color, center);
            Outward(c[1], c[3], c[7], c[5], color, center);
        }

        // Tapering horn or tusk that curls from direction towards bend by curl radians.
        public void Horn(Vector3 root, Vector3 direction, Vector3 bend, float length, float radius, int segments, Color color, float curl, int sides = 6)
        {
            var heading = direction.normalized;
            var step = length / segments;
            var p = root;
            for (var i = 0; i < segments; i++)
            {
                var next = p + heading * step;
                var r0 = radius * (1f - 0.85f * i / segments);
                var r1 = i == segments - 1 ? 0f : radius * (1f - 0.85f * (i + 1) / segments);
                Tube(p - heading * 0.02f, next, r0 * 1.05f, r1, sides, color, 0f, null, 0f, 0f);
                p = next;
                heading = Vector3.RotateTowards(heading, bend.normalized, curl / segments, 0f);
            }
        }

        // Layered thatch cone: each tier overhangs the one above it with a ragged eave, so from
        // above it reads as rings of straw.
        public void Thatch(Vector3 eave, float radius, float height, int sides, int tiers, Color color, bool tufts)
        {
            for (var t = 0; t < tiers; t++)
            {
                var f0 = t / (float)tiers;
                var f1 = Mathf.Min(1f, (t + 1.35f) / tiers);
                var shade = Vary(color, 0.05f) * (t % 2 == 0 ? 1f : 0.84f);
                var offset = Range(0f, Mathf.PI * 2f);
                var lower = new Vector3[sides];
                var upper = new Vector3[sides];
                var under = new Vector3[sides];
                for (var i = 0; i < sides; i++)
                {
                    var angle = offset + i * Mathf.PI * 2f / sides;
                    var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var edge = radius * (1f - f0) + 0.1f;
                    lower[i] = eave + direction * edge * Range(0.97f, 1.05f) + Vector3.up * (height * f0 - Range(0f, 0.1f));
                    upper[i] = eave + direction * radius * (1f - f1) + Vector3.up * (height * f1);
                    under[i] = eave + direction * edge * 0.8f + Vector3.up * (height * f0 + 0.14f);
                }

                var inside = eave + Vector3.up * (height * (f0 + f1) * 0.5f);
                for (var i = 0; i < sides; i++)
                {
                    var j = (i + 1) % sides;
                    // Every other face a little darker, so the straw reads as bundles.
                    Outward(lower[i], lower[j], upper[j], upper[i], shade * (i % 2 == 0 ? 1f : 0.9f), inside);
                    Face(lower[i], lower[j], under[j], shade * 0.45f, Vector3.down);
                    Face(lower[i], under[j], under[i], shade * 0.45f, Vector3.down);
                    if (tufts && t == 0)
                    {
                        var middle = (lower[i] + lower[j]) * 0.5f;
                        var outward = new Vector3(middle.x - eave.x, 0f, middle.z - eave.z).normalized;
                        TwoSided(lower[i], lower[j], middle + outward * 0.06f + Vector3.down * Range(0.12f, 0.22f), shade * 0.9f, shade * 0.6f);
                    }
                }
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
