using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The rock bursting into small pieces at the end of its roll (user, 2026-11-12), instead of
    /// shrinking away: chips of its own stone (glowing ones from the burning rock) fly out, bounce,
    /// lie a moment and sink away, with a puff of dust. Only a look: it hurts nobody (the Shattering
    /// special's shards still do). Sizes TEMPORARY.
    /// </summary>
    public sealed class RockDebris : MonoBehaviour
    {
        private const int Pieces = 18;
        private const float Gravity = 16f;
        private const float Bounce = 0.32f;
        private const float LieSeconds = 1.1f;
        private const float SinkSeconds = 0.45f;
        private const int Shapes = 8;

        private struct Piece
        {
            public Transform Transform;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Half;
            public Vector3 Scale;
            public bool Resting;
        }

        // Chip shapes per rock material, each with its texture taken from a patch of real stone (the
        // texture's gaps between its islands are black).
        private static readonly Dictionary<Material, List<Mesh>> Chunks = new();
        private static readonly List<Mesh> PlainChunks = new();
        private readonly List<Piece> pieces = new();
        private float age;

        public static int BurstsShown { get; private set; }

        /// <summary>Bursts a rock of `radius` at `center` into chips drawn with `material`.</summary>
        public static void Burst(Vector3 center, float radius, Material material)
        {
            BurstsShown++;
            SpecialEffectVisuals.SpawnShatterBurst(new Vector3(center.x, 0f, center.z));
            var count = EffectsQuality.Pieces(Pieces);
            if (count <= 0)
            {
                return;
            }

            var shapes = ShapesFor(material);

            var root = new GameObject("RockDebris");
            var debris = root.AddComponent<RockDebris>();
            var fallback = material == null ? BootstrapMaterials.Get(PlaceholderPalette.Grey(.42f)) : null;
            for (var i = 0; i < count; i++)
            {
                var piece = new GameObject("Chip");
                piece.transform.SetParent(root.transform, false);
                piece.AddComponent<MeshFilter>().sharedMesh = shapes[Random.Range(0, shapes.Count)];
                var renderer = piece.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material != null ? material : fallback;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // A few bigger chunks among many small ones.
                var size = radius * (Random.value < 0.25f ? Random.Range(0.4f, 0.55f) : Random.Range(0.18f, 0.32f));
                piece.transform.localScale = new Vector3(Random.Range(0.8f, 1.2f), Random.Range(0.7f, 1f), Random.Range(0.8f, 1.2f)) * size;
                piece.transform.rotation = Random.rotation;
                var outward = Random.insideUnitCircle;
                var direction = new Vector3(outward.x, 0f, outward.y);
                piece.transform.position = center + direction * radius * 0.6f + Vector3.up * Random.Range(-0.3f, 0.3f) * radius;
                debris.pieces.Add(new Piece
                {
                    Transform = piece.transform,
                    Velocity = direction.normalized * Random.Range(1.5f, 4.5f) + Vector3.up * Random.Range(2f, 4.5f),
                    Spin = Random.onUnitSphere * Random.Range(300f, 900f),
                    Half = size * 0.4f,
                    Scale = piece.transform.localScale
                });
            }
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            age += dt;
            var sink = Mathf.Clamp01((age - LieSeconds) / SinkSeconds);
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (!piece.Resting)
                {
                    piece.Velocity += Vector3.down * Gravity * dt;
                    var position = piece.Transform.position + piece.Velocity * dt;
                    piece.Transform.Rotate(piece.Spin * dt, Space.World);
                    if (position.y < piece.Half && piece.Velocity.y < 0f)
                    {
                        position.y = piece.Half;
                        piece.Velocity = new Vector3(piece.Velocity.x * 0.5f, -piece.Velocity.y * Bounce, piece.Velocity.z * 0.5f);
                        piece.Spin *= 0.5f;
                        if (piece.Velocity.y < 0.6f)
                        {
                            piece.Resting = true;
                        }
                    }

                    piece.Transform.position = position;
                }

                if (sink > 0f)
                {
                    // Shrinks into the ground.
                    piece.Transform.localScale = piece.Scale * (1f - sink);
                    var position = piece.Transform.position;
                    position.y = piece.Half * (1f - sink);
                    piece.Transform.position = position;
                }

                pieces[i] = piece;
            }

            if (sink >= 1f)
            {
                Destroy(gameObject);
            }
        }

        private const int Probe = 64;
        private const float PatchSize = 0.01f;

        private static List<Mesh> ShapesFor(Material material)
        {
            if (material == null)
            {
                if (PlainChunks.Count == 0)
                {
                    for (var i = 0; i < Shapes; i++)
                    {
                        PlainChunks.Add(Chunk(i, new Vector2(0.5f, 0.5f)));
                    }
                }

                return PlainChunks;
            }

            if (Chunks.TryGetValue(material, out var cached))
            {
                return cached;
            }

            var spots = StoneSpots(material.GetTexture("_MainTex"), out var hot);
            var shapes = new List<Mesh>();
            for (var i = 0; i < Shapes; i++)
            {
                // A burning rock's chips: every other one glowing.
                var pool = hot.Count > 0 && i % 2 == 1 ? hot : spots;
                var spot = pool.Count > 0 ? pool[(i * 37 + 11) % pool.Count] : new Vector2(0.5f, 0.5f);
                shapes.Add(Chunk(i, spot));
            }

            Chunks[material] = shapes;
            return shapes;
        }

        // Points of the texture inside its stone (not in the black gaps), read once from a small copy;
        // `hot` are the molten ones.
        private static List<Vector2> StoneSpots(Texture texture, out List<Vector2> hot)
        {
            var spots = new List<Vector2>();
            hot = new List<Vector2>();
            if (texture == null)
            {
                return spots;
            }

            var target = RenderTexture.GetTemporary(Probe, Probe, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            var copy = new Texture2D(Probe, Probe, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, Probe, Probe), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            var pixels = copy.GetPixels();
            Object.Destroy(copy);
            bool Stone(int x, int y)
            {
                var c = pixels[y * Probe + x];
                return Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 0.12f;
            }

            for (var y = 1; y < Probe - 1; y++)
            {
                for (var x = 1; x < Probe - 1; x++)
                {
                    if (!Stone(x, y) || !Stone(x - 1, y) || !Stone(x + 1, y) || !Stone(x, y - 1) || !Stone(x, y + 1))
                    {
                        continue;
                    }

                    var uv = new Vector2((x + 0.5f) / Probe, (y + 0.5f) / Probe);
                    var c = pixels[y * Probe + x];
                    (c.r - c.b > 0.35f && c.r > 0.55f ? hot : spots).Add(uv);
                }
            }

            return spots;
        }

        // A low-poly chip: a squashed octahedron with its corners pulled about, its texture a small
        // patch of the rock's own around `uvBase`.
        private static Mesh Chunk(int seed, Vector2 uvBase)
        {
            var random = new System.Random(seed * 7919 + 17);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            var corners = new[]
            {
                new Vector3(R(0.35f, 0.5f), R(-0.1f, 0.1f), R(-0.1f, 0.1f)),
                new Vector3(-R(0.35f, 0.5f), R(-0.1f, 0.1f), R(-0.1f, 0.1f)),
                new Vector3(R(-0.1f, 0.1f), R(0.3f, 0.45f), R(-0.1f, 0.1f)),
                new Vector3(R(-0.1f, 0.1f), -R(0.25f, 0.4f), R(-0.1f, 0.1f)),
                new Vector3(R(-0.1f, 0.1f), R(-0.1f, 0.1f), R(0.35f, 0.5f)),
                new Vector3(R(-0.1f, 0.1f), R(-0.1f, 0.1f), -R(0.35f, 0.5f))
            };
            var faces = new[] { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };
            var vertices = new Vector3[faces.Length];
            var uvs = new Vector2[faces.Length];
            var triangles = new int[faces.Length];
            for (var i = 0; i < faces.Length; i++)
            {
                var corner = corners[faces[i]];
                vertices[i] = corner;
                uvs[i] = uvBase + new Vector2(corner.x + corner.y * 0.5f, corner.z - corner.y * 0.5f) * PatchSize;
                triangles[i] = i;
            }

            var mesh = new Mesh { name = "RockChip" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
