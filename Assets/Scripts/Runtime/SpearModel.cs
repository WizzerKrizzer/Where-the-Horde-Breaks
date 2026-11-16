using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The thrown spear of the active weapon (user, 2026-11-12: "a spear texture, not just a shape"):
    /// a low-poly spear made in code with a painted texture. A six-sided wooden shaft with grain and
    /// knots, a leather wrap where the head is bound, and a knapped flint head of flat facets. The
    /// texture is painted once from noise: wood on the left half, leather in the third quarter, flint
    /// in the last. Thicker than a real spear so it reads from the game camera. The tip is at the origin and the spear lies along -Z, so it points along +Z.
    /// </summary>
    public static class SpearModel
    {
        public const float Length = 1.75f;
        private const int TextureSize = 256;
        private const int Sides = 6;
        private const float ShaftRadius = 0.042f;
        private const float ButtRadius = 0.036f;
        private const float WrapRadius = 0.054f;
        private const float HeadLength = 0.36f;
        private const float HeadWidth = 0.17f;
        private const float HeadThickness = 0.055f;

        private static Mesh mesh;
        private static Material material;
        private static Texture2D texture;

        public static Mesh Mesh
        {
            get
            {
                if (mesh == null)
                {
                    mesh = BuildMesh();
                }

                return mesh;
            }
        }

        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = Shader.Find("TowerDefense/StyleSkinned");
                    material = shader != null
                        ? new Material(shader) { name = "ThrownSpear" }
                        : new Material(BootstrapMaterials.Get(new Color(0.55f, 0.38f, 0.22f)));
                    material.SetTexture("_MainTex", Texture);
                }

                return material;
            }
        }

        public static Texture2D Texture
        {
            get
            {
                if (texture == null)
                {
                    texture = PaintTexture();
                }

                return texture;
            }
        }

        // A spear object: a renderer of the spear under `parent`, tip at its origin.
        public static Transform Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private static Mesh BuildMesh()
        {
            var builder = new Builder();

            // Flint head: a flat leaf with a diamond cross-section, widest a third of the way back.
            var widest = -HeadLength * 0.38f;
            var neck = -HeadLength;
            var tip = Vector3.zero;
            var ring = new[]
            {
                new Vector3(HeadWidth * 0.5f, 0f, widest), new Vector3(0f, HeadThickness * 0.5f, widest),
                new Vector3(-HeadWidth * 0.5f, 0f, widest), new Vector3(0f, -HeadThickness * 0.5f, widest)
            };
            var neckRing = new[]
            {
                new Vector3(WrapRadius * 0.9f, 0f, neck), new Vector3(0f, HeadThickness * 0.35f, neck),
                new Vector3(-WrapRadius * 0.9f, 0f, neck), new Vector3(0f, -HeadThickness * 0.35f, neck)
            };
            for (var i = 0; i < 4; i++)
            {
                var next = (i + 1) % 4;
                // Each facet shows its own patch of the flint, so neighbours differ.
                var u0 = 0.76f + 0.055f * i;
                builder.Triangle(tip, ring[next], ring[i],
                    new Vector2(u0 + 0.025f, 0.98f), new Vector2(u0 + 0.05f, 0.62f), new Vector2(u0, 0.62f));
                builder.Triangle(ring[i], ring[next], neckRing[next],
                    new Vector2(u0, 0.6f), new Vector2(u0 + 0.05f, 0.6f), new Vector2(u0 + 0.05f, 0.3f));
                builder.Triangle(ring[i], neckRing[next], neckRing[i],
                    new Vector2(u0, 0.6f), new Vector2(u0 + 0.05f, 0.3f), new Vector2(u0, 0.3f));
            }

            // Leather wrap binding the head: a fat band, bulging in the middle.
            var wrapStart = neck + 0.04f;
            var wrapEnd = neck - 0.14f;
            Tube(builder, wrapStart, (wrapStart + wrapEnd) * 0.5f, WrapRadius * 0.85f, WrapRadius, 0.5f, 0.75f, 0f, 0.5f);
            Tube(builder, (wrapStart + wrapEnd) * 0.5f, wrapEnd, WrapRadius, WrapRadius * 0.8f, 0.5f, 0.75f, 0.5f, 1f);

            // Shaft: from inside the wrap back to the butt, a little thinner there.
            var shaftStart = neck + 0.02f;
            var butt = -Length;
            Tube(builder, shaftStart, butt, ShaftRadius, ButtRadius, 0f, 0.5f, 1f, 0f);
            // The butt's end cap.
            for (var s = 0; s < Sides; s++)
            {
                builder.Triangle(new Vector3(0f, 0f, butt), Corner(s, ButtRadius, butt), Corner(s + 1, ButtRadius, butt),
                    new Vector2(0.25f, 0.02f), new Vector2(0.2f, 0.01f), new Vector2(0.3f, 0.01f));
            }

            return builder.Build("ThrownSpear");
        }

        // A six-sided tube from z0 (radius r0) to z1 (radius r1); each side gets its own strip of
        // the texture region [u0, u1], from v0 to v1.
        private static void Tube(Builder builder, float z0, float z1, float r0, float r1, float u0, float u1, float v0, float v1)
        {
            var step = (u1 - u0) / Sides;
            for (var s = 0; s < Sides; s++)
            {
                var a0 = Corner(s, r0, z0);
                var b0 = Corner(s + 1, r0, z0);
                var a1 = Corner(s, r1, z1);
                var b1 = Corner(s + 1, r1, z1);
                var ua = u0 + step * s;
                var ub = ua + step;
                builder.Triangle(a0, b0, b1, new Vector2(ua, v0), new Vector2(ub, v0), new Vector2(ub, v1));
                builder.Triangle(a0, b1, a1, new Vector2(ua, v0), new Vector2(ub, v1), new Vector2(ua, v1));
            }
        }

        private static Vector3 Corner(int side, float radius, float z)
        {
            var angle = (side % Sides) * Mathf.PI * 2f / Sides + Mathf.PI / Sides;
            return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z);
        }

        // Wood (u 0-0.5, along v), leather wrap (u 0.5-0.75) and flint (u 0.75-1).
        private static Texture2D PaintTexture()
        {
            var size = TextureSize;
            var pixels = new Color32[size * size];
            var random = new System.Random(1207);
            var knots = new List<Vector2>();
            for (var i = 0; i < 9; i++)
            {
                knots.Add(new Vector2((float)random.NextDouble() * 0.5f * size, (float)random.NextDouble() * size));
            }

            var flintCells = new List<(Vector2 center, float shade)>();
            for (var i = 0; i < 46; i++)
            {
                flintCells.Add((new Vector2((0.75f + (float)random.NextDouble() * 0.25f) * size, (float)random.NextDouble() * size),
                    0.8f + (float)random.NextDouble() * 0.4f));
            }

            var woodLight = new Color(0.66f, 0.47f, 0.28f);
            var woodDark = new Color(0.4f, 0.26f, 0.14f);
            var leather = new Color(0.45f, 0.29f, 0.17f);
            var leatherDark = new Color(0.24f, 0.14f, 0.08f);
            var flint = new Color(0.47f, 0.45f, 0.43f);
            var flintEdge = new Color(0.72f, 0.7f, 0.66f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    Color color;
                    var u = (float)x / size;
                    if (u < 0.5f)
                    {
                        // Grain runs along the shaft (v): streaks across x, gently wavering.
                        var waver = Mathf.PerlinNoise(x * 0.05f, y * 0.01f) * 6f;
                        var grain = Mathf.PerlinNoise((x + waver) * 0.55f, y * 0.02f);
                        var streak = Mathf.PerlinNoise(x * 0.18f + 40f, y * 0.004f);
                        color = Color.Lerp(woodDark, woodLight, 0.25f + 0.55f * grain + 0.2f * streak);
                        foreach (var knot in knots)
                        {
                            var d = new Vector2((x - knot.x) * 1.6f, (y - knot.y) * 0.6f).magnitude;
                            if (d < 5f)
                            {
                                color = Color.Lerp(color, woodDark * 0.7f, (1f - d / 5f) * 0.9f);
                            }
                        }
                    }
                    else if (u < 0.75f)
                    {
                        // Strips of hide wound round: diagonal bands with dark gaps.
                        var band = Mathf.Repeat(y + x * 0.7f, 22f);
                        var shade = 0.85f + 0.3f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f);
                        color = band < 3f ? leatherDark : leather * shade;
                        if (band > 3f && band < 6f)
                        {
                            color = Color.Lerp(color, leather * 1.35f, 0.5f);
                        }
                    }
                    else
                    {
                        // Knapped flint: flat scallops of slightly different shades, pale ridges between.
                        var p = new Vector2(x, y);
                        var nearest = float.MaxValue;
                        var second = float.MaxValue;
                        var shade = 1f;
                        foreach (var cell in flintCells)
                        {
                            var d = (cell.center - p).magnitude;
                            if (d < nearest)
                            {
                                second = nearest;
                                nearest = d;
                                shade = cell.shade;
                            }
                            else if (d < second)
                            {
                                second = d;
                            }
                        }

                        color = flint * shade * (0.92f + 0.12f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f));
                        if (second - nearest < 1.2f)
                        {
                            color = Color.Lerp(color, flintEdge, 0.35f);
                        }
                    }

                    color.a = 1f;
                    pixels[y * size + x] = color;
                }
            }

            var result = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "ThrownSpearTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            result.SetPixels32(pixels);
            result.Apply(true, true);
            return result;
        }

        private sealed class Builder
        {
            private readonly List<Vector3> vertices = new();
            private readonly List<Vector2> uvs = new();

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                uvs.Add(ua);
                uvs.Add(ub);
                uvs.Add(uc);
            }

            public Mesh Build(string name)
            {
                var result = new Mesh { name = name };
                result.SetVertices(vertices);
                result.SetUVs(0, uvs);
                var triangles = new int[vertices.Count];
                for (var i = 0; i < triangles.Length; i++)
                {
                    triangles[i] = i;
                }

                result.triangles = triangles;
                result.RecalculateNormals();
                result.RecalculateBounds();
                return result;
            }
        }
    }
}
