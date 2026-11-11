using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Throwaway comparison: an AI pixel-art sprite against a low-poly toon 3D goblin with
    /// outline and pixelation, drawn into the same crowd at three zoom heights.
    /// Writes PNGs to Logs/Captures/StyleCompare.
    /// </summary>
    public sealed class StyleCompareTests
    {
        private const int Width = 1920;
        private const int Height = 1080;
        private const float FieldOfView = 55f;
        private const float Pitch = 75f;
        private const float RunnerScale = 0.45f;
        private const string SpritePath = "Assets/Tests/PlayMode/StyleAssets/gemini-runner.png";
        private static readonly Color Sand = new Color(0.788f, 0.647f, 0.42f);

        [UnityTest, Explicit("Visual style comparison")]
        [Category("Performance")]
        public IEnumerator RenderSpriteVersusToon3D()
        {
            Directory.CreateDirectory("Logs/Captures/StyleCompare");
            var toonShader = Shader.Find("TowerDefense/StyleToon");
            var spriteShader = Shader.Find("TowerDefense/StyleSprite");
            Assert.That(toonShader, Is.Not.Null);
            Assert.That(spriteShader, Is.Not.Null);
            Assert.That(File.Exists(SpritePath), Is.True, "Sprite file is missing.");

            var otherMainCameras = new List<Camera>();
            foreach (var other in Camera.allCameras)
            {
                if (other.enabled && other.CompareTag("MainCamera"))
                {
                    other.enabled = false;
                    otherMainCameras.Add(other);
                }
            }

            // The game bootstrap builds a level map in every test scene; hide it so only
            // the ground and units drawn here are visible.
            var hiddenRenderers = new List<Renderer>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    hiddenRenderers.Add(renderer);
                }
            }

            var cameraObject = new GameObject("Compare camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Sand;
            camera.allowMSAA = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1500f;
            var full = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            var groundMesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1000f, 0f, -1000f), new Vector3(-1000f, 0f, 1000f),
                    new Vector3(1000f, 0f, 1000f), new Vector3(1000f, 0f, -1000f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var ground = new GameObject("Compare ground");
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundMaterial = new Material(Shader.Find("Unlit/Color")) { color = Sand };
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var spriteTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            spriteTexture.LoadImage(File.ReadAllBytes(SpritePath));
            spriteTexture.filterMode = FilterMode.Point;
            var spriteAspect = spriteTexture.height / (float)spriteTexture.width;
            var spriteMaterial = new Material(spriteShader) { enableInstancing = true, mainTexture = spriteTexture };
            var spriteQuad = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
                },
                uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var toonMaterial = new Material(toonShader) { enableInstancing = true };
            toonMaterial.SetColor("_OutlineColor", new Color(0.12f, 0.23f, 0.07f));
            var goblinMesh = StyleMeshes.Goblin();
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            // Lineup at close range: sprite on the left, toon 3D on the right.
            {
                var ppm = PlaceCamera(camera, 8f);
                toonMaterial.SetFloat("_OutlineWidth", 1.0f / ppm * 4f);
                var scale = 1.9f;
                DrawSprite(spriteQuad, spriteMaterial, new List<Matrix4x4> { SpriteMatrix(-2.2f, 0f, scale, spriteAspect, 0f) });
                DrawToon(goblinMesh, toonMaterial, new List<Matrix4x4> { Matrix(2.2f, 0f, RunnerScale * scale, 0f) });
                yield return Render(camera, full, Width, Height, texture, "Logs/Captures/StyleCompare/lineup.png");
            }

            foreach (var height in new[] { 20f, 60f, 150f })
            {
                var ppm = PlaceCamera(camera, height);
                // One art pixel of a 16x16 goblin, in screen pixels (about 1.2 m wide).
                var pixelSize = Mathf.Max(1, Mathf.RoundToInt(ppm * 1.2f / 16f));
                var spriteList = BuildCrowd(camera, 1.5f, (x, z, yaw) => SpriteMatrix(x, z, 1f, spriteAspect, yaw));
                DrawSprite(spriteQuad, spriteMaterial, spriteList);
                yield return Render(camera, full, Width, Height, texture, $"Logs/Captures/StyleCompare/crowd-{height:F0}m-sprite.png");

                toonMaterial.SetFloat("_OutlineWidth", Mathf.Max(0.04f, pixelSize * 0.9f / ppm));
                var toonList = BuildCrowd(camera, 1.5f, (x, z, yaw) => Matrix(x, z, RunnerScale, yaw));
                DrawToon(goblinMesh, toonMaterial, toonList);
                var smallWidth = Mathf.Max(8, Width / pixelSize);
                var smallHeight = Mathf.Max(8, Height / pixelSize);
                yield return Render(camera, full, smallWidth, smallHeight, texture, $"Logs/Captures/StyleCompare/crowd-{height:F0}m-toon3d.png");
            }

            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(groundMesh);
            Object.DestroyImmediate(spriteMaterial);
            Object.DestroyImmediate(spriteTexture);
            Object.DestroyImmediate(spriteQuad);
            Object.DestroyImmediate(toonMaterial);
            Object.DestroyImmediate(goblinMesh);
            Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            full.Release();
            Object.DestroyImmediate(full);
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

        [UnityTest, Explicit("Goblin bust style preview")]
        [Category("Performance")]
        public IEnumerator RenderGoblinBust()
        {
            Directory.CreateDirectory("Logs/Captures/StyleCompare");
            var toonShader = Shader.Find("TowerDefense/StyleToon");
            Assert.That(toonShader, Is.Not.Null);

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

            var cameraObject = new GameObject("Bust camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Sand;
            camera.allowMSAA = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1500f;
            var full = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            var groundMesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1000f, 0f, -1000f), new Vector3(-1000f, 0f, 1000f),
                    new Vector3(1000f, 0f, 1000f), new Vector3(1000f, 0f, -1000f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var ground = new GameObject("Bust ground");
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundMaterial = new Material(Shader.Find("Unlit/Color")) { color = Sand };
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            var toonMaterial = new Material(toonShader) { enableInstancing = true };
            toonMaterial.SetColor("_OutlineColor", new Color(0.12f, 0.23f, 0.07f));
            var bust = StyleMeshes.GoblinBust();
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            // Turntable: the same goblin rotated 0, 45, ... 315 degrees.
            {
                var ppm = PlaceCamera(camera, 9f);
                toonMaterial.SetFloat("_OutlineWidth", 4f / ppm);
                var list = new List<Matrix4x4>();
                for (var i = 0; i < 8; i++)
                {
                    var column = i % 4;
                    var row = i / 4;
                    list.Add(Matrix(-4.5f + column * 3f, 1.9f - row * 4.2f, 0.9f, i * 45f));
                }

                DrawToon(bust, toonMaterial, list);
                yield return Render(camera, full, Width, Height, texture, "Logs/Captures/StyleCompare/bust-turntable.png");
            }

            foreach (var height in new[] { 20f, 60f, 150f })
            {
                var ppm = PlaceCamera(camera, height);
                var pixelSize = Mathf.Max(1, Mathf.RoundToInt(ppm * 1.2f / 16f));
                toonMaterial.SetFloat("_OutlineWidth", Mathf.Max(0.04f, pixelSize * 0.9f / ppm));
                DrawToon(bust, toonMaterial, BuildCrowd(camera, 1.5f, (x, z, yaw) => Matrix(x, z, RunnerScale, yaw), true));
                yield return Render(
                    camera, full, Mathf.Max(8, Width / pixelSize), Mathf.Max(8, Height / pixelSize), texture,
                    $"Logs/Captures/StyleCompare/bust-crowd-{height:F0}m.png");
            }

            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(groundMesh);
            Object.DestroyImmediate(toonMaterial);
            Object.DestroyImmediate(bust);
            Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            full.Release();
            Object.DestroyImmediate(full);
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

        // Renders at renderWidth x renderHeight (pixelated when smaller than the output), then
        // point-upscales into the full-size target and saves it.
        private static IEnumerator Render(
            Camera camera, RenderTexture full, int renderWidth, int renderHeight, Texture2D texture, string path)
        {
            RenderTexture small = null;
            if (renderWidth < Width)
            {
                small = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Point
                };
                camera.targetTexture = small;
            }
            else
            {
                camera.targetTexture = full;
            }

            camera.Render();
            if (small != null)
            {
                Graphics.Blit(small, full);
                camera.targetTexture = full;
            }

            RenderTexture.active = full;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            if (small != null)
            {
                small.Release();
                Object.DestroyImmediate(small);
            }

            yield return null;
        }

        private static void DrawToon(Mesh mesh, Material material, List<Matrix4x4> matrices)
        {
            for (var offset = 0; offset < matrices.Count; offset += 1023)
            {
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices.GetRange(offset, Mathf.Min(1023, matrices.Count - offset)));
            }
        }

        private static void DrawSprite(Mesh mesh, Material material, List<Matrix4x4> matrices) => DrawToon(mesh, material, matrices);

        private static Matrix4x4 Matrix(float x, float z, float scale, float yaw)
        {
            return Matrix4x4.TRS(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
        }

        // Sprite quad lying on the ground; width in metres is baseWidth * scale ratio.
        private static Matrix4x4 SpriteMatrix(float x, float z, float scale, float aspect, float yaw, float baseWidth = 1.2f)
        {
            var width = baseWidth * scale;
            return Matrix4x4.TRS(
                new Vector3(x, 0.01f, z), Quaternion.Euler(0f, yaw, 0f), new Vector3(width, 1f, width * aspect));
        }

        private static List<Matrix4x4> BuildCrowd(
            Camera camera, float density, System.Func<float, float, float, Matrix4x4> make, bool freeYaw = false)
        {
            var list = new List<Matrix4x4>();
            var halfDepth = camera.orthographicSize / Mathf.Sin(Pitch * Mathf.Deg2Rad);
            var halfWidth = camera.orthographicSize * camera.aspect;
            var step = Mathf.Sqrt(1f / density);
            var random = new System.Random(11);
            for (var x = -halfWidth; x < halfWidth; x += step)
            {
                for (var z = -halfDepth; z < halfDepth; z += step)
                {
                    var jitterX = ((float)random.NextDouble() - 0.5f) * step * 0.8f;
                    var jitterZ = ((float)random.NextDouble() - 0.5f) * step * 0.8f;
                    var yaw = freeYaw ? (float)random.NextDouble() * 360f : random.Next(0, 4) * 90f;
                    list.Add(make(x + jitterX, z + jitterZ, yaw));
                }
            }

            return list;
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
    }
}
