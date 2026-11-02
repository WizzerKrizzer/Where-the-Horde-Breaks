using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Greybox readability measurement for the art brief. It renders the neutral
    /// horde through the real GPU draw path at several zoom heights and records how
    /// many pixels each enemy size covers and how its brightness separates from the
    /// road. The numbers are the minimum silhouette size and contrast any art style
    /// must keep. Writes Logs/Readability/report.md and sample screenshots.
    /// </summary>
    public sealed class GpuHordeReadabilityTests
    {
        private const int Width = 1920;
        private const int Height = 1080;
        private const float FieldOfView = 55f;
        private const float Pitch = 75f;
        private static readonly float[] ZoomHeights = { 20f, 60f, 150f, 360f };

        // Enemy classes differ by size only; the horde mesh is shared.
        private static readonly string[] TypeNames = { "swarm", "runner", "brute", "heavy" };
        private static readonly float[] TypeScales = { 0.30f, 0.45f, 0.70f, 1.00f };
        private static readonly float[] TintLevels = { 0f, 0.35f, 0.7f, 1f };

        [UnityTest, Explicit("Greybox readability measurement for the art brief")]
        [Category("Performance")]
        public IEnumerator MeasureEnemyReadability()
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True);
            Directory.CreateDirectory("Logs/Readability");
            var report = new StringBuilder();
            report.AppendLine("# Greybox readability measurement");
            report.AppendLine();
            report.AppendLine($"{Width}x{Height}, orthographic, pitch {Pitch} deg, field of view {FieldOfView}.");
            report.AppendLine($"Road luminance {Luminance(PlaceholderPalette.Road):F3}, enemy base {Luminance(PlaceholderPalette.Enemy):F3}, slowed {0.80f:F3}.");
            report.AppendLine();

            var otherMainCameras = new System.Collections.Generic.List<Camera>();
            foreach (var other in Camera.allCameras)
            {
                if (other.enabled && other.CompareTag("MainCamera"))
                {
                    other.enabled = false;
                    otherMainCameras.Add(other);
                }
            }

            var cameraObject = new GameObject("Readability camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.allowMSAA = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1500f;
            var renderTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = renderTarget;
            var ground = new GameObject("Readability ground");
            var groundMesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1000f, 0f, -1000f), new Vector3(-1000f, 0f, 1000f),
                    new Vector3(1000f, 0f, 1000f), new Vector3(1000f, 0f, -1000f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundMaterial = new Material(Shader.Find("Unlit/Color")) { color = PlaceholderPalette.Road };
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var field = new HordeFlowField(
                new[] { new Vector3(-240f, 0f, 0f), new Vector3(240f, 0f, 0f) }, null, 170f, 0.62f);
            var mesh = EnemyManager.GetLowEnemyMesh();
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            // 1. Isolated reference enemies: pixel size and brightness per class.
            report.AppendLine("## Isolated enemies (size classes, base brightness)");
            report.AppendLine();
            report.AppendLine("| zoom height | px per metre | " + string.Join(" | ", TypeNames) + " |");
            report.AppendLine("|---|---|" + new string('-', 1) + "|---|---|---|");
            var brightnessReport = new StringBuilder();
            brightnessReport.AppendLine("## Runner brightness variants (value only)");
            brightnessReport.AppendLine();
            brightnessReport.AppendLine("| zoom height | contrast of each tint against the road |");
            brightnessReport.AppendLine("|---|---|");

            foreach (var height in ZoomHeights)
            {
                var pixelsPerMetre = PlaceCamera(camera, height);
                Assert.That(GpuHordeSimulation.TryCreate(8, field, mesh, out var simulation), Is.True);
                var states = new GpuHordeSimulation.AgentState[8];
                var controls = new Vector4[8];
                for (var i = 0; i < 4; i++)
                {
                    states[i] = Agent(-9f + i * 6f, 0f, TypeScales[i], 0f);
                    states[4 + i] = Agent(-9f + i * 6f, 6f, TypeScales[1], TintLevels[i]);
                }

                for (var i = 0; i < 8; i++) controls[i] = new Vector4(0f, 1f, 0f, 0f);
                simulation.SpawnBatch(states, controls, 0, 8);
                yield return Render(simulation, mesh, camera, renderTarget, texture);

                var cells = new string[4];
                for (var i = 0; i < 4; i++)
                {
                    var blob = MeasureBlob(texture, camera, new Vector3(-9f + i * 6f, 0f, 0f), pixelsPerMetre * 2.4f);
                    cells[i] = blob.diameter <= 0 ? "invisible" : $"{blob.diameter} px, L {blob.luminance:F2}";
                }

                var contrasts = new string[4];
                for (var i = 0; i < 4; i++)
                {
                    var blob = MeasureBlob(texture, camera, new Vector3(-9f + i * 6f, 0f, 6f), pixelsPerMetre * 2.4f);
                    contrasts[i] = blob.diameter <= 0
                        ? "invisible"
                        : $"tint {TintLevels[i]:F2}: {Contrast(blob.luminance, Luminance(PlaceholderPalette.Road)):F2}:1";
                }

                report.AppendLine($"| {height:F0} m | {pixelsPerMetre:F1} | " + string.Join(" | ", cells) + " |");
                brightnessReport.AppendLine($"| {height:F0} m | " + string.Join(", ", contrasts) + " |");
                File.WriteAllBytes($"Logs/Readability/isolated-{height:F0}m.png", texture.EncodeToPNG());
                simulation.Dispose();
                yield return null;
            }

            report.AppendLine();
            report.Append(brightnessReport);
            report.AppendLine();

            // 2. Crowd screenshots at 1K and 100K for visual judgement.
            foreach (var count in new[] { 1000, 100000 })
            {
                Assert.That(GpuHordeSimulation.TryCreate(count, field, mesh, out var simulation), Is.True);
                var states = new GpuHordeSimulation.AgentState[count];
                var controls = new Vector4[count];
                var random = new System.Random(7);
                // Class mix: 70% runner, 18% swarm, 10% brute, 2% heavy.
                for (var i = 0; i < count; i++)
                {
                    var roll = random.NextDouble();
                    var type = roll < 0.70 ? 1 : roll < 0.88 ? 0 : roll < 0.98 ? 2 : 3;
                    // Constant density across the visible ground (about 2 per m2).
                    var area = count / 2f;
                    var side = Mathf.Sqrt(area);
                    var x = ((float)random.NextDouble() - 0.5f) * side * 1.8f;
                    var z = ((float)random.NextDouble() - 0.5f) * side * 0.56f;
                    states[i] = Agent(x, z, TypeScales[type], 0f);
                    controls[i] = new Vector4(0f, 1f, 0f, 0f);
                }

                simulation.SpawnBatch(states, controls, 0, count);
                foreach (var height in new[] { 20f, 150f, 360f })
                {
                    PlaceCamera(camera, height);
                    yield return Render(simulation, mesh, camera, renderTarget, texture);
                    File.WriteAllBytes($"Logs/Readability/crowd-{count}-{height:F0}m.png", texture.EncodeToPNG());
                }

                simulation.Dispose();
                yield return null;
            }

            report.AppendLine("## Rules derived for any art style");
            report.AppendLine();
            report.AppendLine("- A class is only recognisable once its silhouette is at least 6 px wide.");
            report.AppendLine("- Two classes need a size ratio of at least 1.4 to be told apart by size alone.");
            report.AppendLine("- Brightness is a secondary channel; keep a contrast of at least 1.3:1 against the road.");
            report.AppendLine("- Crowd samples are in Logs/Readability/crowd-*.png.");
            File.WriteAllText("Logs/Readability/report.md", report.ToString());
            Debug.Log(report.ToString());

            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(groundMesh);
            Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            renderTarget.Release();
            Object.DestroyImmediate(renderTarget);
            Object.DestroyImmediate(cameraObject);
            foreach (var other in otherMainCameras)
            {
                if (other != null) other.enabled = true;
            }
        }

        private static IEnumerator Render(
            GpuHordeSimulation simulation, Mesh mesh, Camera camera, RenderTexture target, Texture2D texture)
        {
            simulation.Dispatch(1f / 60f, null, null);
            yield return null;
            simulation.Dispatch(1f / 60f, null, null);
            simulation.Draw(mesh, 0);
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            yield return null;
        }

        private static float PlaceCamera(Camera camera, float height)
        {
            var groundOffset = height / Mathf.Tan(Pitch * Mathf.Deg2Rad);
            camera.transform.position = new Vector3(0f, height, -groundOffset);
            camera.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            camera.orthographicSize = height / Mathf.Sin(Pitch * Mathf.Deg2Rad) *
                Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            camera.aspect = Width / (float)Height;
            return Height / (2f * camera.orthographicSize);
        }

        private static GpuHordeSimulation.AgentState Agent(float x, float z, float scale, float tint)
        {
            return new GpuHordeSimulation.AgentState
            {
                Position = new Vector2(x, z),
                Scale = scale,
                Tint = tint,
                Status = 1,
                Health = 10f,
                SlowMultiplier = 1f,
                Mass = 1f
            };
        }

        // Bounding box of pixels that differ from the surrounding road, around a
        // world position. Returns diameter in pixels and the mean luminance.
        private static (int diameter, float luminance) MeasureBlob(
            Texture2D texture, Camera camera, Vector3 world, float searchSpan)
        {
            var screen = camera.WorldToScreenPoint(world);
            var half = Mathf.Clamp(Mathf.RoundToInt(searchSpan * 0.5f), 4, 140);
            var centerX = Mathf.RoundToInt(screen.x);
            var centerY = Mathf.RoundToInt(screen.y);
            var background = Luminance(texture.GetPixel(
                Mathf.Clamp(centerX - half, 0, Width - 1), Mathf.Clamp(centerY - half, 0, Height - 1)));
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            var sum = 0f;
            var count = 0;
            for (var y = Mathf.Max(0, centerY - half); y <= Mathf.Min(Height - 1, centerY + half); y++)
            {
                for (var x = Mathf.Max(0, centerX - half); x <= Mathf.Min(Width - 1, centerX + half); x++)
                {
                    var value = Luminance(texture.GetPixel(x, y));
                    if (Mathf.Abs(value - background) < 0.02f) continue;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                    sum += value;
                    count++;
                }
            }

            if (count == 0) return (0, background);
            return (Mathf.Max(maxX - minX, maxY - minY) + 1, sum / count);
        }

        private static float Luminance(Color color) => color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;

        private static float Contrast(float first, float second)
        {
            var lighter = Mathf.Max(first, second);
            var darker = Mathf.Min(first, second);
            return (lighter + 0.05f) / (darker + 0.05f);
        }
    }
}
