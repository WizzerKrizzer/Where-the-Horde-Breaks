using System.IO;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The horde goblin (user, 2026-11-13): the Meshy model with its walk baked by
    /// Tools/Animation/meshy/build_goblin.py into Resources/Meshy/goblin.bytes and goblin-tex.png.
    /// Both are git-ignored (Meshy files stay local for now); without them the horde keeps its
    /// grey placeholder bodies.
    ///
    /// Each level of detail is a mesh (standing still, for bounds and the corpses; uv2.x says what a
    /// vertex belongs to: 0 the body, 1 the club, 2 the bone, 3 the stone)
    /// and a pose texture: one row per pose, one texel per vertex, holding its position. The horde
    /// shader reads the row for the agent's point in its walk.
    /// </summary>
    public sealed class GoblinModel
    {
        public sealed class Level
        {
            public Mesh Mesh;
            public Texture2D Poses;
            public int PoseCount;
        }

        /// <summary>Walk poses a cycle. After them: standing still, the weapon raised, the weapon
        /// struck (HordeGoblin.cginc picks among them).</summary>
        public const int WalkPoses = 6;

        /// <summary>This model's own number of walk poses (the brute's animated walk has more than
        /// the goblin's six; a version 5 file says how many).</summary>
        public int WalkPoseCount { get; private set; } = WalkPoses;

        /// <summary>How many different blows the model has after its dead rows, each the same
        /// number of rows (a version 6 file says; 1 before).</summary>
        public int BlowCount { get; private set; } = 1;

        public Level Near { get; private set; }
        public Level Far { get; private set; }
        public Texture2D Texture { get; private set; }

        /// <summary>How far a goblin moves in one walk cycle, metres at the horde's goblin scale; the
        /// walk turns by the ground covered, so the feet keep pace with it.</summary>
        public float StrideMetres { get; private set; }

        /// <summary>Set by tests of the simulation itself, which were written for the placeholder
        /// bodies (one mesh near and far, split by distance): the horde then ignores the model.</summary>
        public static bool UsePlaceholderBodies;

        private static GoblinModel loaded;
        private static bool tried;
        private static GoblinModel loadedBrute;
        private static bool triedBrute;

        /// <summary>The Orc Brute (user, 2026-11-14): Tools/Animation/meshy/build_brute.py writes it
        /// in the goblin's format to Resources/Meshy/brute.bytes and brute-tex.png.</summary>
        public static bool TryGetBrute(out GoblinModel model)
        {
            if (UsePlaceholderBodies)
            {
                model = null;
                return false;
            }

            if (!triedBrute)
            {
                triedBrute = true;
                loadedBrute = Load("brute");
            }

            model = loadedBrute;
            return model != null;
        }

        public static bool TryGet(out GoblinModel model)
        {
            if (UsePlaceholderBodies)
            {
                model = null;
                return false;
            }

            if (!tried)
            {
                tried = true;
                loaded = Load("goblin");
            }

            model = loaded;
            return model != null;
        }

        private static GoblinModel Load(string name)
        {
            var data = Resources.Load<TextAsset>("Meshy/" + name);
            var texture = Resources.Load<Texture2D>("Meshy/" + name + "-tex");
            if (data == null || texture == null)
            {
                return null;
            }

            using var reader = new BinaryReader(new MemoryStream(data.bytes));
            var magic = new string(reader.ReadChars(4));
            var version = reader.ReadInt32();
            // Version 4 adds what each vertex is worn as (the goblin's hood, skull cap, hair bone, mantle).
            if (magic != "GOBN" || version < 3 || version > 6)
            {
                Debug.LogWarning($"Resources/Meshy/{name}.bytes is not a horde model; keeping the placeholder bodies.");
                return null;
            }

            var count = reader.ReadInt32();
            var stride = reader.ReadSingle();
            // Version 5 adds the number of walk poses.
            var walkPoses = version >= 5 ? reader.ReadInt32() : WalkPoses;
            // Version 6 adds the number of different blows.
            var blows = version >= 6 ? reader.ReadInt32() : 1;
            var levels = new Level[count];
            for (var i = 0; i < count; i++)
            {
                levels[i] = ReadLevel(reader, i, version >= 4, walkPoses);
            }

            return new GoblinModel
            {
                Near = levels[0],
                Far = levels[count > 1 ? 1 : 0],
                StrideMetres = stride,
                WalkPoseCount = walkPoses,
                BlowCount = Mathf.Max(1, blows),
                Texture = texture
            };
        }

        private static Level ReadLevel(BinaryReader reader, int index, bool hasWear, int walkPoses)
        {
            var vertexCount = reader.ReadInt32();
            var indexCount = reader.ReadInt32();
            var frameCount = reader.ReadInt32();
            var uvs = new Vector2[vertexCount];
            for (var v = 0; v < vertexCount; v++)
            {
                uvs[v] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }

            var held = new Vector2[vertexCount];
            for (var v = 0; v < vertexCount; v++)
            {
                held[v] = new Vector2(reader.ReadSingle(), 0f);
            }

            if (hasWear)
            {
                for (var v = 0; v < vertexCount; v++)
                {
                    held[v].y = reader.ReadSingle();
                }
            }

            var triangles = new int[indexCount];
            for (var t = 0; t < indexCount; t++)
            {
                triangles[t] = reader.ReadInt32();
            }

            var poses = new Texture2D(vertexCount, frameCount, TextureFormat.RGBAFloat, false, true)
            {
                name = "GoblinPoses" + index,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[vertexCount * frameCount];
            var first = new Vector3[vertexCount];
            for (var f = 0; f < frameCount; f++)
            {
                for (var v = 0; v < vertexCount; v++)
                {
                    var p = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    pixels[f * vertexCount + v] = new Color(p.x, p.y, p.z, 1f);
                    if (f == Mathf.Min(walkPoses, frameCount - 1))
                    {
                        first[v] = p;
                    }
                }
            }

            poses.SetPixels(pixels);
            poses.Apply(false, true);
            var mesh = new Mesh { name = "Goblin" + index };
            mesh.indexFormat = vertexCount > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = first;
            mesh.uv = uvs;
            mesh.uv2 = held;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.normals = WeldedNormals(first, mesh.normals);
            mesh.RecalculateBounds();
            return new Level { Mesh = mesh, Poses = poses, PoseCount = frameCount };
        }

        // Vertices split along the texture's seams share a place but not a normal, and the outline
        // (pushed out along the normals) tore open there. Vertices at one place get one normal.
        private static Vector3[] WeldedNormals(Vector3[] positions, Vector3[] normals)
        {
            var sums = new System.Collections.Generic.Dictionary<Vector3Int, Vector3>();
            var keys = new Vector3Int[positions.Length];
            for (var v = 0; v < positions.Length; v++)
            {
                keys[v] = Vector3Int.RoundToInt(positions[v] * 2000f);
                sums.TryGetValue(keys[v], out var sum);
                sums[keys[v]] = sum + normals[v];
            }

            var welded = new Vector3[positions.Length];
            for (var v = 0; v < positions.Length; v++)
            {
                var sum = sums[keys[v]];
                welded[v] = sum.sqrMagnitude > 1e-8f ? sum.normalized : normals[v];
            }

            return welded;
        }
    }
}
