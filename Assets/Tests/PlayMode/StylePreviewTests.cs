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
    /// Renders a small comparison of the plain greybox horde against a silhouette
    /// style (class shapes, vertical gradient, dark outline). Throwaway preview for
    /// choosing a direction; writes PNGs to Logs/StylePreview.
    /// </summary>
    public sealed class StylePreviewTests
    {
        private const int Width = 1920;
        private const int Height = 1080;
        private const float FieldOfView = 55f;
        private const float Pitch = 75f;
        private static readonly float[] Scales = { 0.30f, 0.45f, 0.70f, 1.00f };

        [UnityTest, Explicit("Visual style preview")]
        [Category("Performance")]
        public IEnumerator RenderStyleComparison()
        {
            Directory.CreateDirectory("Logs/StylePreview");
            var shader = Shader.Find("TowerDefense/StylePreview");
            Assert.That(shader, Is.Not.Null, "StylePreview shader not found.");

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

            var cameraObject = new GameObject("Style camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1500f;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
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
            var ground = new GameObject("Style ground");
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundMaterial = new Material(Shader.Find("Unlit/Color")) { color = PlaceholderPalette.Road };
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var plain = new Material(shader) { enableInstancing = true };
            plain.SetFloat("_OutlineWidth", 0f);
            plain.SetFloat("_GradientStrength", 0f);
            plain.SetColor("_Dark", PlaceholderPalette.Enemy);
            plain.SetColor("_Light", PlaceholderPalette.Enemy);
            var styled = new Material(shader) { enableInstancing = true };
            styled.SetColor("_Dark", PlaceholderPalette.Grey(0.30f));
            styled.SetColor("_Light", PlaceholderPalette.Grey(0.82f));
            styled.SetColor("_OutlineColor", PlaceholderPalette.Grey(0.03f));
            styled.SetFloat("_GradientStrength", 1f);

            var plainMesh = EnemyManager.GetLowEnemyMesh();
            var styledMeshes = new[]
            {
                StyleMeshes.Swarm(), StyleMeshes.Runner(), StyleMeshes.Brute(), StyleMeshes.Heavy()
            };
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            // Lineup: the four classes large, plain on the top row and styled below.
            {
                var ppm = PlaceCamera(camera, 14f);
                var plainList = new List<Matrix4x4>[4];
                var styledList = new List<Matrix4x4>[4];
                for (var i = 0; i < 4; i++)
                {
                    plainList[i] = new List<Matrix4x4> { Matrix(-9f + i * 6f, 3.5f, Scales[i]) };
                    styledList[i] = new List<Matrix4x4> { Matrix(-9f + i * 6f, -3.5f, Scales[i]) };
                }

                styled.SetFloat("_OutlineWidth", 1.5f / ppm);
                for (var i = 0; i < 4; i++)
                {
                    Draw(plainMesh, plain, plainList[i]);
                    Draw(styledMeshes[i], styled, styledList[i]);
                }

                yield return Capture(camera, target, texture, "Logs/StylePreview/lineup.png");
            }

            // Crowds at three zoom heights, same jittered layout for both styles.
            foreach (var height in new[] { 20f, 60f, 150f })
            {
                var ppm = PlaceCamera(camera, height);
                styled.SetFloat("_OutlineWidth", 1.2f / ppm);
                var matricesByClass = BuildCrowd(camera, height, 1.5f, out var classes);
                foreach (var style in new[] { "plain", "styled" })
                {
                    for (var type = 0; type < 4; type++)
                    {
                        if (style == "plain") Draw(plainMesh, plain, matricesByClass[type]);
                        else Draw(styledMeshes[type], styled, matricesByClass[type]);
                    }

                    yield return Capture(camera, target, texture, $"Logs/StylePreview/crowd-{height:F0}m-{style}.png");
                }
            }

            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(groundMesh);
            Object.DestroyImmediate(plain);
            Object.DestroyImmediate(styled);
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

        private static IEnumerator Capture(Camera camera, RenderTexture target, Texture2D texture, string path)
        {
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            yield return null;
        }

        private static void Draw(Mesh mesh, Material material, List<Matrix4x4> matrices)
        {
            for (var offset = 0; offset < matrices.Count; offset += 1023)
            {
                var count = Mathf.Min(1023, matrices.Count - offset);
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices.GetRange(offset, count));
            }
        }

        private static Matrix4x4 Matrix(float x, float z, float scale, float yaw = 0f)
        {
            return Matrix4x4.TRS(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
        }

        // Jittered grid covering the visible ground; class mix 70 / 18 / 10 / 2 percent.
        private static List<Matrix4x4>[] BuildCrowd(Camera camera, float height, float density, out int count)
        {
            var lists = new List<Matrix4x4>[4];
            for (var i = 0; i < 4; i++) lists[i] = new List<Matrix4x4>();
            var halfHeight = camera.orthographicSize / Mathf.Sin(Pitch * Mathf.Deg2Rad) * Mathf.Sin(Pitch * Mathf.Deg2Rad);
            var groundHalfDepth = camera.orthographicSize / Mathf.Sin(Pitch * Mathf.Deg2Rad);
            var halfWidth = camera.orthographicSize * camera.aspect;
            var centerZ = 0f;
            var step = Mathf.Sqrt(1f / density);
            var random = new System.Random(11);
            count = 0;
            for (var x = -halfWidth; x < halfWidth; x += step)
            {
                for (var z = centerZ - groundHalfDepth; z < centerZ + groundHalfDepth; z += step)
                {
                    var roll = random.NextDouble();
                    var type = roll < 0.70 ? 1 : roll < 0.88 ? 0 : roll < 0.98 ? 2 : 3;
                    var jitterX = ((float)random.NextDouble() - 0.5f) * step * 0.8f;
                    var jitterZ = ((float)random.NextDouble() - 0.5f) * step * 0.8f;
                    var yaw = (float)random.NextDouble() * 360f;
                    lists[type].Add(Matrix(x + jitterX, z + jitterZ, Scales[type], yaw));
                    count++;
                }
            }

            return lists;
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

    /// <summary>Procedural class meshes: extruded top-down silhouettes (radius about 1.2).</summary>
    internal static class StyleMeshes
    {
        public static Mesh Swarm()
        {
            var builder = new Builder();
            builder.Prism(new[] { V(0f, 1.2f), V(0.75f, -0.8f), V(0f, -0.35f), V(-0.75f, -0.8f) }, 0f, 0.5f);
            return builder.ToMesh("Swarm");
        }

        public static Mesh Runner()
        {
            var builder = new Builder();
            builder.Prism(new[]
            {
                V(-0.95f, 0.15f), V(-0.6f, 0.85f), V(0.6f, 0.85f), V(0.95f, 0.15f), V(0.5f, -0.95f), V(-0.5f, -0.95f)
            }, 0f, 0.8f);
            builder.Prism(Circle(0f, 0.45f, 0.42f, 8), 0.8f, 1.35f);
            return builder.ToMesh("Runner");
        }

        public static Mesh Brute()
        {
            var builder = new Builder();
            builder.Prism(new[]
            {
                V(-1.2f, 0.3f), V(-0.8f, 0.95f), V(0.8f, 0.95f), V(1.2f, 0.3f), V(0.9f, -1.0f), V(-0.9f, -1.0f)
            }, 0f, 1.0f);
            builder.Prism(Circle(0f, 0.65f, 0.36f, 8), 1.0f, 1.55f);
            return builder.ToMesh("Brute");
        }

        public static Mesh Heavy()
        {
            var builder = new Builder();
            builder.Prism(Circle(0f, 0f, 1.1f, 10), 0f, 1.2f);
            for (var i = 0; i < 4; i++)
            {
                var angle = (45f + i * 90f) * Mathf.Deg2Rad;
                var tip = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.35f;
                var side = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * 0.28f;
                var root = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.9f;
                builder.Prism(new[] { tip, root + side, root - side }, 0.6f, 1.4f);
            }

            builder.Prism(Circle(0f, 0.15f, 0.45f, 8), 1.2f, 1.7f);
            return builder.ToMesh("Heavy");
        }

        // Low-poly goblin seen from above: hunched shoulders, large head, two side ears,
        // two foot blocks and a short club pointing forward (+Z).
        public static Mesh Goblin()
        {
            var skin = new Color(0.373f, 0.639f, 0.227f);
            var shadow = new Color(0.247f, 0.478f, 0.141f);
            var wood = new Color(0.42f, 0.29f, 0.17f);
            var bone = new Color(0.89f, 0.85f, 0.66f);
            var builder = new Builder();
            builder.Prism(new[]
            {
                V(-0.85f, 0.1f), V(-0.55f, 0.7f), V(0.55f, 0.7f), V(0.85f, 0.1f), V(0.5f, -0.75f), V(-0.5f, -0.75f)
            }, 0.15f, 0.75f, skin);
            builder.Prism(Circle(0f, 0.35f, 0.55f, 8), 0.75f, 1.35f, skin);
            builder.Prism(new[] { V(-1.3f, 0.05f), V(-0.45f, 0.5f), V(-0.45f, 0.1f) }, 0.85f, 1.05f, shadow);
            builder.Prism(new[] { V(1.3f, 0.05f), V(0.45f, 0.1f), V(0.45f, 0.5f) }, 0.85f, 1.05f, shadow);
            builder.Prism(new[] { V(-0.5f, -0.65f), V(-0.15f, -0.65f), V(-0.15f, -1.05f), V(-0.5f, -1.05f) }, 0f, 0.2f, shadow);
            builder.Prism(new[] { V(0.15f, -0.65f), V(0.5f, -0.65f), V(0.5f, -1.05f), V(0.15f, -1.05f) }, 0f, 0.2f, shadow);
            builder.Prism(new[] { V(0.62f, 0.1f), V(0.82f, 0.1f), V(0.9f, 1.15f), V(0.7f, 1.15f) }, 0.5f, 0.7f, wood);
            builder.Prism(Circle(0.8f, 1.25f, 0.25f, 6), 0.45f, 0.8f, bone);
            return builder.ToMesh("Goblin");
        }

        // Goblin bust: big head with a face on top, wide shoulders and two stub feet. No arms,
        // elbows, knees or fingers; it can rotate freely about the vertical axis (+Z forward).
        public static Mesh GoblinBust()
        {
            var skin = new Color(0.373f, 0.639f, 0.227f);
            var shadow = new Color(0.247f, 0.478f, 0.141f);
            var dark = new Color(0.12f, 0.23f, 0.07f);
            var eye = new Color(0.95f, 0.76f, 0.19f);
            var bone = new Color(0.89f, 0.85f, 0.66f);
            var cloth = new Color(0.42f, 0.29f, 0.17f);
            var builder = new Builder();
            builder.Prism(new[]
            {
                V(-1.05f, 0.05f), V(-0.7f, 0.65f), V(0.7f, 0.65f), V(1.05f, 0.05f), V(0.7f, -0.6f), V(-0.7f, -0.6f)
            }, 0.15f, 0.8f, skin);
            builder.Prism(new[] { V(-0.45f, -0.6f), V(0.45f, -0.6f), V(0.35f, -0.95f), V(-0.35f, -0.95f) }, 0.15f, 0.45f, cloth);
            builder.Prism(new[] { V(-0.55f, 0.55f), V(-0.2f, 0.55f), V(-0.2f, 0.98f), V(-0.55f, 0.98f) }, 0f, 0.2f, shadow);
            builder.Prism(new[] { V(0.2f, 0.55f), V(0.55f, 0.55f), V(0.55f, 0.98f), V(0.2f, 0.98f) }, 0f, 0.2f, shadow);
            builder.Prism(Circle(0f, 0.15f, 0.68f, 10), 0.8f, 1.55f, skin);
            builder.Prism(new[] { V(-1.35f, -0.05f), V(-0.62f, 0.4f), V(-0.62f, 0.0f) }, 1.0f, 1.18f, shadow);
            builder.Prism(new[] { V(1.35f, -0.05f), V(0.62f, 0.0f), V(0.62f, 0.4f) }, 1.0f, 1.18f, shadow);
            builder.Prism(new[] { V(-0.42f, 0.36f), V(-0.12f, 0.36f), V(-0.12f, 0.6f), V(-0.42f, 0.6f) }, 1.55f, 1.62f, eye);
            builder.Prism(new[] { V(0.12f, 0.36f), V(0.42f, 0.36f), V(0.42f, 0.6f), V(0.12f, 0.6f) }, 1.55f, 1.62f, eye);
            builder.Prism(new[] { V(-0.5f, 0.62f), V(0.5f, 0.62f), V(0.45f, 0.74f), V(-0.45f, 0.74f) }, 1.55f, 1.66f, dark);
            builder.Prism(new[] { V(-0.05f, 0.1f), V(0.05f, 0.1f), V(0.0f, 0.3f) }, 1.55f, 1.66f, shadow);
            builder.Prism(new[] { V(-0.38f, -0.1f), V(0.38f, -0.1f), V(0.34f, 0.04f), V(-0.34f, 0.04f) }, 1.55f, 1.62f, dark);
            builder.Prism(new[] { V(-0.25f, -0.06f), V(-0.12f, -0.06f), V(-0.12f, 0.02f), V(-0.25f, 0.02f) }, 1.62f, 1.7f, bone);
            builder.Prism(new[] { V(0.12f, -0.06f), V(0.25f, -0.06f), V(0.25f, 0.02f), V(0.12f, 0.02f) }, 1.62f, 1.7f, bone);
            return builder.ToMesh("GoblinBust");
        }

        private static Vector2 V(float x, float z) => new Vector2(x, z);

        private static Vector2[] Circle(float cx, float cz, float radius, int segments)
        {
            var points = new Vector2[segments];
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                points[i] = new Vector2(cx + Mathf.Cos(angle) * radius, cz + Mathf.Sin(angle) * radius);
            }

            return points;
        }

        private sealed class Builder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();
            private readonly List<Color> colors = new List<Color>();

            // Extrudes a convex polygon (counter-clockwise seen from above) between two heights.
            public void Prism(Vector2[] outline, float bottom, float top, Color? color = null)
            {
                // Ensure counter-clockwise winding in the XZ plane.
                var area = 0f;
                for (var i = 0; i < outline.Length; i++)
                {
                    var next = outline[(i + 1) % outline.Length];
                    area += outline[i].x * next.y - next.x * outline[i].y;
                }

                var points = (Vector2[])outline.Clone();
                if (area < 0f) System.Array.Reverse(points);

                var center = Vector2.zero;
                foreach (var point in points) center += point;
                center /= points.Length;

                var topCenter = vertices.Count;
                vertices.Add(new Vector3(center.x, top, center.y));
                normals.Add(Vector3.up);
                var topStart = vertices.Count;
                foreach (var point in points)
                {
                    vertices.Add(new Vector3(point.x, top, point.y));
                    normals.Add(Vector3.up);
                }

                for (var i = 0; i < points.Length; i++)
                {
                    // Seen from above (looking down -Y) counter-clockwise XZ is clockwise on screen.
                    triangles.Add(topCenter);
                    triangles.Add(topStart + (i + 1) % points.Length);
                    triangles.Add(topStart + i);
                }

                for (var i = 0; i < points.Length; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Length];
                    var edge = new Vector3(b.x - a.x, 0f, b.y - a.y);
                    var normal = new Vector3(edge.z, 0f, -edge.x).normalized;
                    var start = vertices.Count;
                    vertices.Add(new Vector3(a.x, bottom, a.y));
                    vertices.Add(new Vector3(b.x, bottom, b.y));
                    vertices.Add(new Vector3(b.x, top, b.y));
                    vertices.Add(new Vector3(a.x, top, a.y));
                    for (var j = 0; j < 4; j++) normals.Add(normal);
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                    triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
                }

                var tint = color ?? Color.white;
                while (colors.Count < vertices.Count) colors.Add(tint);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
