using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// Low-poly props built in code for the units' hands: the cavemen's big bone, the chief's
    /// mammoth tusk, the hide shield and the torch's stick. Each mesh lies along +Z from its grip end
    /// at the origin (the shield faces +Z), coloured by vertex, for the StyleSkinned shader with
    /// _VertexColor on.
    /// </summary>
    public static class UnitProps
    {
        private static readonly Color Bone = new(0.86f, 0.8f, 0.66f);
        private static readonly Color Worn = new(0.66f, 0.55f, 0.4f);
        private static readonly Color Ivory = new(0.93f, 0.89f, 0.78f);
        private static readonly Color Hide = new(0.72f, 0.58f, 0.4f);
        private static readonly Color HideDark = new(0.5f, 0.38f, 0.25f);
        private static readonly Color Wood = new(0.42f, 0.29f, 0.18f);
        private static readonly Color Cord = new(0.33f, 0.24f, 0.16f);
        private static readonly Color Char = new(0.16f, 0.11f, 0.08f);

        private sealed class Builder
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Color> Colors = new();
            public readonly List<int> Triangles = new();

            // A rounded lump: a lat-long sphere squashed to `radii`.
            public void Lump(Vector3 center, Vector3 radii, Color color, int rings = 5, int sides = 8)
            {
                var start = Vertices.Count;
                for (var r = 0; r <= rings; r++)
                {
                    var v = Mathf.PI * r / rings;
                    for (var s = 0; s < sides; s++)
                    {
                        var u = 2f * Mathf.PI * s / sides;
                        Vertices.Add(center + Vector3.Scale(radii, new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Sin(v) * Mathf.Sin(u), Mathf.Cos(v))));
                        Colors.Add(color);
                    }
                }

                for (var r = 0; r < rings; r++)
                {
                    for (var s = 0; s < sides; s++)
                    {
                        var a = start + r * sides + s;
                        var b = start + r * sides + (s + 1) % sides;
                        Triangles.AddRange(new[] { a, a + sides, b, b, a + sides, b + sides });
                    }
                }
            }

            // A tube through a list of centres, with a radius and colour at each.
            public void Tube(IList<Vector3> centers, IList<float> radii, IList<Color> colors, int sides = 8, bool capEnds = true)
            {
                var start = Vertices.Count;
                for (var i = 0; i < centers.Count; i++)
                {
                    var forward = (centers[Mathf.Min(i + 1, centers.Count - 1)] - centers[Mathf.Max(i - 1, 0)]).normalized;
                    var side = Vector3.Cross(forward, Mathf.Abs(forward.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                    var up = Vector3.Cross(side, forward);
                    for (var s = 0; s < sides; s++)
                    {
                        var u = 2f * Mathf.PI * s / sides;
                        Vertices.Add(centers[i] + (side * Mathf.Cos(u) + up * Mathf.Sin(u)) * radii[i]);
                        Colors.Add(colors[i]);
                    }
                }

                for (var i = 0; i < centers.Count - 1; i++)
                {
                    for (var s = 0; s < sides; s++)
                    {
                        var a = start + i * sides + s;
                        var b = start + i * sides + (s + 1) % sides;
                        Triangles.AddRange(new[] { a, b, a + sides, b, b + sides, a + sides });
                    }
                }

                if (!capEnds)
                {
                    return;
                }

                foreach (var (ring, flip) in new[] { (0, true), (centers.Count - 1, false) })
                {
                    var center = Vertices.Count;
                    Vertices.Add(centers[ring]);
                    Colors.Add(colors[ring]);
                    for (var s = 0; s < sides; s++)
                    {
                        var a = start + ring * sides + s;
                        var b = start + ring * sides + (s + 1) % sides;
                        Triangles.AddRange(flip ? new[] { center, b, a } : new[] { center, a, b });
                    }
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Vertices);
                mesh.SetColors(Colors);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>The cavemen's weapon: a big thigh bone, knobbed at both ends.</summary>
        public static Mesh BigBone(float length = 1.05f)
        {
            var b = new Builder();
            b.Tube(new[] { new Vector3(0f, 0f, 0.06f), new Vector3(0f, 0f, length * 0.5f), new Vector3(0f, 0f, length - 0.06f) },
                new[] { 0.05f, 0.036f, 0.058f }, new[] { Worn, Bone, Worn }, 8, false);
            const float knob = 0.085f;
            b.Lump(new Vector3(0.045f, 0f, 0.06f), new Vector3(knob * 0.8f, knob * 0.75f, knob), Worn);
            b.Lump(new Vector3(-0.045f, 0f, 0.05f), new Vector3(knob * 0.8f, knob * 0.75f, knob * 0.9f), Worn);
            b.Lump(new Vector3(0.05f, 0f, length - 0.07f), new Vector3(knob, knob * 0.85f, knob * 1.1f), Worn);
            b.Lump(new Vector3(-0.05f, 0f, length - 0.06f), new Vector3(knob * 0.95f, knob * 0.85f, knob), Worn);
            return b.Build("BigBone");
        }

        /// <summary>The chief's weapon: a mammoth tusk curving up (+Y), its root wrapped in hide.</summary>
        public static Mesh Tusk(float length = 1.3f)
        {
            var b = new Builder();
            const int steps = 10;
            var centers = new List<Vector3>();
            var radii = new List<float>();
            var colors = new List<Color>();
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                // An even arc of 60 degrees over its length, curling up from the fist.
                const float arc = 60f * Mathf.Deg2Rad;
                var bend = length / arc;
                var angle = t * arc;
                centers.Add(new Vector3(0f, bend * (1f - Mathf.Cos(angle)), bend * Mathf.Sin(angle)));
                radii.Add(Mathf.Lerp(0.07f, 0.014f, Mathf.Pow(t, 1.3f)));
                colors.Add(t < 0.22f ? HideDark : Color.Lerp(Ivory * 0.85f, Ivory, t));
            }

            b.Tube(centers, radii, colors, 9);
            // Cord lashings over the hide grip.
            foreach (var z in new[] { 0.04f, 0.16f, 0.27f })
            {
                b.Tube(new[] { new Vector3(0f, 0f, z - 0.015f), new Vector3(0f, 0f, z + 0.015f) }, new[] { 0.078f, 0.078f }, new[] { Cord, Cord }, 9);
            }

            return b.Build("Tusk");
        }

        /// <summary>A round shield, hide stretched over a wooden rim, facing +Z, its up along +Y.</summary>
        public static Mesh HideShield(float radius = 0.36f)
        {
            var b = new Builder();
            const int sides = 14;
            // The face: a shallow dome of hide, darker towards the rim.
            var start = b.Vertices.Count;
            b.Vertices.Add(new Vector3(0f, 0f, 0.06f));
            b.Colors.Add(Hide);
            foreach (var (r, z, color) in new[] { (0.55f, 0.045f, Hide), (1f, 0f, HideDark) })
            {
                for (var s = 0; s < sides; s++)
                {
                    var u = 2f * Mathf.PI * s / sides;
                    b.Vertices.Add(new Vector3(Mathf.Cos(u) * radius * r, Mathf.Sin(u) * radius * r, z));
                    b.Colors.Add(color);
                }
            }

            for (var s = 0; s < sides; s++)
            {
                var n = (s + 1) % sides;
                b.Triangles.AddRange(new[] { start, start + 1 + n, start + 1 + s });
                b.Triangles.AddRange(new[] { start + 1 + s, start + 1 + n, start + 1 + sides + s });
                b.Triangles.AddRange(new[] { start + 1 + n, start + 1 + sides + n, start + 1 + sides + s });
            }

            // The back, flat.
            var back = b.Vertices.Count;
            b.Vertices.Add(new Vector3(0f, 0f, -0.02f));
            b.Colors.Add(Wood);
            for (var s = 0; s < sides; s++)
            {
                var u = 2f * Mathf.PI * s / sides;
                b.Vertices.Add(new Vector3(Mathf.Cos(u) * radius, Mathf.Sin(u) * radius, -0.02f));
                b.Colors.Add(Wood);
            }

            for (var s = 0; s < sides; s++)
            {
                b.Triangles.AddRange(new[] { back, back + 1 + s, back + 1 + (s + 1) % sides });
            }

            // The wooden rim, and two cords crossing the face.
            var rim = new List<Vector3>();
            for (var s = 0; s <= sides; s++)
            {
                var u = 2f * Mathf.PI * s / sides;
                rim.Add(new Vector3(Mathf.Cos(u) * radius, Mathf.Sin(u) * radius, 0f));
            }

            for (var s = 0; s < sides; s++)
            {
                b.Tube(new[] { rim[s], rim[s + 1] }, new[] { 0.028f, 0.028f }, new[] { Wood, Wood }, 5, false);
            }

            foreach (var angle in new[] { 40f, -40f })
            {
                var dir = Quaternion.Euler(0f, 0f, angle) * Vector3.up * radius * 0.95f;
                b.Tube(new[] { -dir + Vector3.forward * 0.03f, Vector3.forward * 0.07f, dir + Vector3.forward * 0.03f },
                    new[] { 0.014f, 0.014f, 0.014f }, new[] { Cord, Cord, Cord }, 5);
            }

            return b.Build("HideShield");
        }

        /// <summary>The torch's stick: a branch along +Z, charred at the burning end.</summary>
        public static Mesh TorchStick(float length = 0.55f)
        {
            var b = new Builder();
            b.Tube(new[] { Vector3.zero, new Vector3(0f, 0.01f, length * 0.7f), new Vector3(0f, 0f, length) },
                new[] { 0.026f, 0.03f, 0.04f }, new[] { Wood, Wood, Char }, 7);
            b.Lump(new Vector3(0f, 0f, length), new Vector3(0.055f, 0.055f, 0.07f), Char, 4, 7);
            return b.Build("TorchStick");
        }
    }
}
