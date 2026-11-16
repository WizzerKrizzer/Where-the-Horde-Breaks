using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// Low-poly shapes for the effects (user, 2026-11-12: "too cubey, I want low poly"): a faceted
    /// flame, a puff of dust, a gem-like spark or chip and a ragged disc. Each facet has its own
    /// corners, so it shades flat. Built once and shared; all one unit across.
    /// </summary>
    public static class LowPolyShapes
    {
        private static Mesh flame;
        private static Mesh puff;
        private static Mesh gem;
        private static Mesh disc;

        /// <summary>
        /// A flame standing on its root at the origin, one unit across at its widest and one tall:
        /// six-sided rings, each turned half a side from the last, to a leaning tip. The vertex colour
        /// r is each facet's height (StyleLowPolyFlame colours it by that).
        /// </summary>
        public static Mesh Flame
        {
            get
            {
                if (flame != null)
                {
                    return flame;
                }

                const int sides = 6;
                var rings = new[] { (0f, 0.22f), (0.22f, 0.5f), (0.5f, 0.36f), (0.75f, 0.17f) };
                var points = new Vector3[rings.Length, sides];
                for (var r = 0; r < rings.Length; r++)
                {
                    for (var s = 0; s < sides; s++)
                    {
                        var angle = (s + 0.5f * (r % 2)) * Mathf.PI * 2f / sides;
                        points[r, s] = new Vector3(Mathf.Cos(angle) * rings[r].Item2, rings[r].Item1, Mathf.Sin(angle) * rings[r].Item2);
                    }
                }

                var tip = new Vector3(0.05f, 1f, 0f);
                var root = new Vector3(0f, -0.04f, 0f);
                var builder = new Builder();
                for (var s = 0; s < sides; s++)
                {
                    var next = (s + 1) % sides;
                    builder.Triangle(root, points[0, next], points[0, s]);
                    for (var r = 0; r < rings.Length - 1; r++)
                    {
                        builder.Triangle(points[r, s], points[r, next], points[r + 1, s]);
                        builder.Triangle(points[r, next], points[r + 1, next], points[r + 1, s]);
                    }

                    builder.Triangle(points[rings.Length - 1, s], points[rings.Length - 1, next], tip);
                }

                flame = builder.Build("LowPolyFlame", true);
                return flame;
            }
        }

        /// <summary>A puff of dust: an icosahedron one unit across.</summary>
        public static Mesh Puff
        {
            get
            {
                if (puff != null)
                {
                    return puff;
                }

                var t = (1f + Mathf.Sqrt(5f)) * 0.5f;
                var v = new[]
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
                };
                for (var i = 0; i < v.Length; i++)
                {
                    v[i] = v[i].normalized * 0.5f;
                }

                var faces = new[]
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
                };
                var builder = new Builder();
                for (var i = 0; i < faces.Length; i += 3)
                {
                    builder.Triangle(v[faces[i]], v[faces[i + 1]], v[faces[i + 2]]);
                }

                puff = builder.Build("LowPolyPuff", false);
                return puff;
            }
        }

        /// <summary>A gem: an octahedron one unit across (sparks, chips).</summary>
        public static Mesh Gem
        {
            get
            {
                if (gem != null)
                {
                    return gem;
                }

                var v = new[]
                {
                    new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f), new Vector3(0f, 0.5f, 0f),
                    new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, -0.5f)
                };
                var faces = new[] { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };
                var builder = new Builder();
                for (var i = 0; i < faces.Length; i += 3)
                {
                    builder.Triangle(v[faces[i]], v[faces[i + 1]], v[faces[i + 2]]);
                }

                gem = builder.Build("LowPolyGem", false);
                return gem;
            }
        }

        /// <summary>A flat ragged seven-sided disc on the ground, one unit across.</summary>
        public static Mesh Disc
        {
            get
            {
                if (disc != null)
                {
                    return disc;
                }

                const int sides = 7;
                var radii = new[] { 0.5f, 0.42f, 0.48f, 0.4f, 0.5f, 0.44f, 0.47f };
                var builder = new Builder();
                for (var s = 0; s < sides; s++)
                {
                    var a = s * Mathf.PI * 2f / sides;
                    var b = (s + 1) * Mathf.PI * 2f / sides;
                    builder.Triangle(Vector3.zero,
                        new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radii[(s + 1) % sides],
                        new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radii[s]);
                }

                disc = builder.Build("LowPolyDisc", false);
                return disc;
            }
        }

        private sealed class Builder
        {
            private readonly System.Collections.Generic.List<Vector3> vertices = new();
            private readonly System.Collections.Generic.List<Color> colors = new();

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                var height = new Color((a.y + b.y + c.y) / 3f, 0f, 0f, 1f);
                colors.Add(height);
                colors.Add(height);
                colors.Add(height);
            }

            public Mesh Build(string name, bool withHeights)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                if (withHeights)
                {
                    mesh.SetColors(colors);
                }

                var triangles = new int[vertices.Count];
                for (var i = 0; i < triangles.Length; i++)
                {
                    triangles[i] = i;
                }

                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                if (withHeights)
                {
                    // Room for the sway and the lean done in the shader.
                    mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(3f, 3f, 3f));
                }

                return mesh;
            }
        }
    }
}
