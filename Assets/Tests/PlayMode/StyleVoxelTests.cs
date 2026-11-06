using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Throwaway: inflates a 2D front-view pixel sprite into a rounded voxel model so the back,
    /// sides and a simple walk cycle can be derived by code. Writes PNGs to Logs/StyleVoxel.
    /// </summary>
    public sealed class StyleVoxelTests
    {
        private const int Width = 1920;
        private const int Height = 1080;
        private const string ArtPath = "Assets/Tests/PlayMode/StyleAssets/goblin-front-art.png";
        private static readonly Color Sand = new Color(0.788f, 0.647f, 0.42f);

        [UnityTest, Explicit("Voxel goblin preview")]
        [Category("Performance")]
        public IEnumerator RenderVoxelGoblin()
        {
            Directory.CreateDirectory("Logs/StyleVoxel");
            var toonShader = Shader.Find("TowerDefense/StyleToon");
            Assert.That(toonShader, Is.Not.Null);
            Assert.That(File.Exists(ArtPath), Is.True);

            var art = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            art.LoadImage(File.ReadAllBytes(ArtPath));
            var model = VoxelGoblin.Build(art);

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

            var cameraObject = new GameObject("Voxel camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Sand;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var material = new Material(toonShader) { enableInstancing = true };
            material.SetColor("_OutlineColor", new Color(0.12f, 0.23f, 0.07f));
            material.SetFloat("_OutlineWidth", 0.018f);

            // 1. Walk cycle: four facings (rows) by four frames (columns), low camera angle.
            Place(camera, 20f, 5.6f);
            var yaws = new[] { 180f, 90f, 0f, 270f };
            for (var row = 0; row < 4; row++)
            {
                for (var frame = 0; frame < 4; frame++)
                {
                    DrawPose(model, material, new Vector3(-3.6f + frame * 2.4f, 3.0f - row * 2.1f, 0f), yaws[row], frame);
                }
            }

            yield return Capture(camera, target, texture, "Logs/StyleVoxel/walk-grid.png");

            // 2. Turntable through eight directions, at a low angle and at the game's 75 degrees.
            foreach (var pitch in new[] { 20f, 75f })
            {
                Place(camera, pitch, 4.2f);
                for (var i = 0; i < 8; i++)
                {
                    DrawPose(model, material,
                        new Vector3(-5.25f + i * 1.5f, 0f, 0f), 180f + i * 45f, 1);
                }

                yield return Capture(camera, target, texture, $"Logs/StyleVoxel/turntable-{pitch:F0}.png");
            }

            Object.DestroyImmediate(material);
            Object.DestroyImmediate(art);
            Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            foreach (var mesh in model.Parts) Object.DestroyImmediate(mesh);
            foreach (var renderer in hiddenRenderers)
            {
                if (renderer != null) renderer.enabled = true;
            }

            foreach (var other in otherMainCameras)
            {
                if (other != null) other.enabled = true;
            }
        }

        private static void DrawPose(VoxelGoblin model, Material material, Vector3 position, float yaw, int frame)
        {
            var root = Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            var unit = model.Unit;
            var bob = (frame == 1 || frame == 3) ? 1.5f * unit : 0f;
            float leftLift = frame == 0 ? 3f * unit : 0f;
            float rightLift = frame == 2 ? 3f * unit : 0f;
            var body = root * Matrix4x4.Translate(new Vector3(0f, bob, 0f));
            var left = root * Matrix4x4.Translate(new Vector3(0f, leftLift, leftLift * 1.5f));
            var right = root * Matrix4x4.Translate(new Vector3(0f, rightLift, rightLift * 1.5f));
            Graphics.DrawMeshInstanced(model.Parts[0], 0, material, new List<Matrix4x4> { body });
            Graphics.DrawMeshInstanced(model.Parts[1], 0, material, new List<Matrix4x4> { left });
            Graphics.DrawMeshInstanced(model.Parts[2], 0, material, new List<Matrix4x4> { right });
        }

        private static void Place(Camera camera, float pitch, float orthographicSize)
        {
            camera.transform.position = new Vector3(0f, 0f, 0f) - Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward * 40f;
            camera.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
            camera.orthographicSize = orthographicSize;
            camera.aspect = Width / (float)Height;
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
    }

    /// <summary>
    /// Front-view pixel sprite inflated into a rounded voxel shell. The front keeps the art;
    /// the back repeats it without face details; sides come from the silhouette depth.
    /// Three parts (body, left leg, right leg) so legs can be moved for a walk cycle.
    /// </summary>
    internal sealed class VoxelGoblin
    {
        private const float ModelHeight = 2f;
        private const int LegStartRow = 57;
        private const float RoundRadius = 10f;
        private const float MaxDepth = 12f;

        public Mesh[] Parts;
        public float Unit;

        public static VoxelGoblin Build(Texture2D art)
        {
            var w = art.width;
            var h = art.height;
            var pixels = art.GetPixels32();
            Color32 At(int x, int y) => pixels[(h - 1 - y) * w + x];
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && At(x, y).a > 0;

            // Distance to the nearest empty pixel (Euclidean, brute force on a small grid).
            var distance = new float[w, h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (!Solid(x, y)) continue;
                    var best = 99f;
                    for (var dy = -14; dy <= 14; dy++)
                    {
                        for (var dx = -14; dx <= 14; dx++)
                        {
                            if (Solid(x + dx, y + dy)) continue;
                            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dy * dy));
                        }
                    }

                    distance[x, y] = best;
                }
            }

            var depth = new int[w, h];
            var part = new int[w, h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (!Solid(x, y)) continue;
                    var t = 1f - Mathf.Min(distance[x, y], RoundRadius) / RoundRadius;
                    depth[x, y] = Mathf.Max(1, Mathf.RoundToInt(MaxDepth * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t))));
                    var color = At(x, y);
                    var brown = color.r == 0x6B && color.g == 0x4A;
                    part[x, y] = (y >= LegStartRow && !brown) ? (x < w / 2 ? 1 : 2) : 0;
                }
            }

            var unit = ModelHeight / h;
            var parts = new Mesh[3];
            for (var p = 0; p < 3; p++)
            {
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var colors = new List<Color32>();
                var triangles = new List<int>();

                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color32 color)
                {
                    var start = vertices.Count;
                    vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                    for (var i = 0; i < 4; i++) { normals.Add(normal); colors.Add(color); }
                    // Unity treats clockwise triangles as front-facing: order them so the
                    // geometric normal agrees with the outward normal.
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0f)
                    {
                        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
                    }
                    else
                    {
                        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                        triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
                    }
                }

                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        if (!Solid(x, y) || part[x, y] != p) continue;
                        var color = At(x, y);
                        var back = BackColor(color, distance[x, y]);
                        var d = depth[x, y] * unit;
                        var x0 = (x - w * 0.5f) * unit;
                        var x1 = x0 + unit;
                        var y1 = (h - y) * unit;
                        var y0 = y1 - unit;
                        // Front (+Z) shows the art, back (-Z) the face-less copy.
                        Quad(new Vector3(x0, y0, d), new Vector3(x1, y0, d), new Vector3(x1, y1, d), new Vector3(x0, y1, d), Vector3.forward, color);
                        Quad(new Vector3(x1, y0, -d), new Vector3(x0, y0, -d), new Vector3(x0, y1, -d), new Vector3(x1, y1, -d), Vector3.back, back);
                        var shade = Shade(color, 0.8f);
                        var backShade = Shade(back, 0.8f);
                        // Sides where the neighbour is thinner or missing.
                        SideFaces(x - 1, y, -1, 0);
                        SideFaces(x + 1, y, 1, 0);
                        SideFaces(x, y - 1, 0, 1);
                        SideFaces(x, y + 1, 0, -1);

                        void SideFaces(int nx, int ny, int sx, int sy)
                        {
                            var dn = (Solid(nx, ny) && part[nx, ny] == p ? depth[nx, ny] : 0) * unit;
                            if (dn >= d) return;
                            if (sx != 0)
                            {
                                var xs = sx > 0 ? x1 : x0;
                                var normal = new Vector3(sx, 0f, 0f);
                                Quad(new Vector3(xs, y0, dn), new Vector3(xs, y0, d), new Vector3(xs, y1, d), new Vector3(xs, y1, dn), normal, shade);
                                Quad(new Vector3(xs, y0, -d), new Vector3(xs, y0, -dn), new Vector3(xs, y1, -dn), new Vector3(xs, y1, -d), normal, backShade);
                            }
                            else
                            {
                                var ys = sy > 0 ? y1 : y0;
                                var normal = new Vector3(0f, sy, 0f);
                                Quad(new Vector3(x0, ys, dn), new Vector3(x0, ys, d), new Vector3(x1, ys, d), new Vector3(x1, ys, dn), normal, shade);
                                Quad(new Vector3(x0, ys, -d), new Vector3(x0, ys, -dn), new Vector3(x1, ys, -dn), new Vector3(x1, ys, -d), normal, backShade);
                            }
                        }
                    }
                }

                var mesh = new Mesh
                {
                    name = "VoxelGoblinPart" + p,
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                parts[p] = mesh;
            }

            return new VoxelGoblin { Parts = parts, Unit = unit };
        }

        private static Color32 Shade(Color32 color, float factor)
        {
            return new Color32((byte)(color.r * factor), (byte)(color.g * factor), (byte)(color.b * factor), 255);
        }

        // The back keeps the silhouette outline but drops eyes, teeth, brow and mouth.
        private static Color32 BackColor(Color32 color, float distanceToEdge)
        {
            var skin = new Color32(0x5F, 0xA3, 0x3A, 255);
            var eye = color.r == 0xF2 && color.g == 0xC2;
            var tooth = color.r == 0xE2 && color.g == 0xD8;
            var darkInside = color.r == 0x1E && color.g == 0x3A && distanceToEdge > 1.6f;
            return (eye || tooth || darkInside) ? skin : color;
        }
    }
}
