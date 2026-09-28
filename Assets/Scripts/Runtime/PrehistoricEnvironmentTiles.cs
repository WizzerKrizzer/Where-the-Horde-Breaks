using System.Collections.Generic;
using TowerDefense.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    public sealed class EnvironmentTilePackageInstance : MonoBehaviour
    {
        public EnvironmentTilePackageId PackageId { get; private set; }
        public EnvironmentTileKind PlacedTiles { get; private set; }

        internal void Initialize(EnvironmentTilePackage package)
        {
            PackageId = package.Id;
            PlacedTiles = package.Tiles;
            name = $"EnvironmentTiles_{package.Id}";
        }
    }

    /// <summary>
    /// Reusable procedural tiles for the prehistoric package. Placement changes
    /// per level; the visual recipes stay shared across every prehistoric map.
    /// </summary>
    public static class PrehistoricEnvironmentTiles
    {
        private static readonly Color Trunk = new(0.22f, 0.12f, 0.055f);
        private static readonly Color DarkTrunk = new(0.15f, 0.075f, 0.035f);
        private static readonly Color Leaf = new(0.15f, 0.30f, 0.11f);
        private static readonly Color LeafLight = new(0.24f, 0.39f, 0.15f);
        private static readonly Color Hide = new(0.42f, 0.29f, 0.15f);
        private static readonly Color Thatch = new(0.48f, 0.36f, 0.18f);
        private static readonly Color Stone = new(0.34f, 0.35f, 0.31f);
        private static readonly Color Water = new(0.10f, 0.34f, 0.39f);
        private static readonly Color Bank = new(0.25f, 0.20f, 0.11f);

        public static void Populate(Transform parent, LevelDefinition level)
        {
            var center = level.groundCenter;
            var halfX = level.groundSize.x * 0.5f;
            var halfZ = level.groundSize.z * 0.5f;
            var variant = Mathf.Max(1, level.decorVariant);

            var trees = Category(parent, "TreeTiles");
            var huts = Category(parent, "HutTiles");
            var fires = Category(parent, "FireTiles");
            var rivers = Category(parent, "RiverTiles");
            var stones = Category(parent, "StandingStoneTiles");

            var treeCenters = new[]
            {
                center + new Vector3(-halfX * .40f, 0f, halfZ * .32f),
                center + new Vector3( halfX * .41f, 0f, halfZ * .31f),
                center + new Vector3(-halfX * .38f, 0f,-halfZ * .23f),
                center + new Vector3( halfX * .37f, 0f,-halfZ * .31f)
            };
            for (var cluster = 0; cluster < treeCenters.Length; cluster++)
            {
                var count = 3 + ((variant + cluster) % 3);
                for (var i = 0; i < count; i++)
                {
                    var seed = variant * 29 + cluster * 7 + i;
                    var offset = new Vector3(
                        Mathf.Sin(seed * 1.73f) * Mathf.Min(3.2f, halfX * .08f), 0f,
                        Mathf.Cos(seed * 1.37f) * Mathf.Min(2.7f, halfZ * .10f));
                    CreateTreeTile(trees, treeCenters[cluster] + offset, .82f + (seed % 4) * .09f, seed);
                }
            }

            var firstHut = center + new Vector3(-halfX * .29f, 0f, halfZ * .27f);
            var secondHut = center + new Vector3(halfX * .31f, 0f, -halfZ * .27f);
            CreateHutTile(huts, firstHut, 1f + variant * .025f, 12f + variant * 9f, 0);
            CreateHutTile(huts, secondHut, .88f + variant * .03f, -19f - variant * 7f, 1);
            CreateFireTile(fires, firstHut + new Vector3(2.8f, 0f, -1.5f), .8f, 0);
            CreateFireTile(fires, secondHut + new Vector3(-2.5f, 0f, 1.4f), .68f, 1);

            var riverZ = center.z - halfZ * .43f;
            var riverLength = Mathf.Clamp(halfX * .58f, 14f, 70f);
            CreateRiverTile(rivers, new Vector3(center.x, 0f, riverZ), riverLength,
                Mathf.Clamp(2.2f + variant * .22f, 2.4f, 3.2f), variant);

            CreateStandingStoneTile(stones,
                center + new Vector3(-halfX * .10f, 0f, halfZ * .39f), 1.05f, variant * 13f, 0);
            CreateStandingStoneTile(stones,
                center + new Vector3(halfX * .08f, 0f, halfZ * .40f), .86f, -variant * 17f, 1);
        }

        public static GameObject CreateTreeTile(Transform parent, Vector3 position, float scale, int seed)
        {
            var root = TileRoot(parent, $"Tile_PrehistoricTree_{seed:00}", position, seed * 17f, scale);
            Primitive(root.transform, "Trunk", PrimitiveType.Cylinder, new Vector3(0f, 1.25f, 0f),
                new Vector3(.45f, 1.25f, .45f), Trunk);
            Primitive(root.transform, "Branch", PrimitiveType.Cylinder, new Vector3(.35f, 2.0f, 0f),
                new Vector3(.16f, .72f, .16f), DarkTrunk, new Vector3(0f, 0f, -35f));
            Primitive(root.transform, "CanopyA", PrimitiveType.Sphere, new Vector3(0f, 2.75f, 0f),
                new Vector3(2.0f, 1.65f, 1.85f), Leaf);
            Primitive(root.transform, "CanopyB", PrimitiveType.Sphere, new Vector3(.65f, 3.05f, .1f),
                new Vector3(1.45f, 1.25f, 1.35f), LeafLight);
            return root;
        }

        public static GameObject CreateHutTile(Transform parent, Vector3 position, float scale, float yaw, int index)
        {
            var root = TileRoot(parent, $"Tile_PrehistoricHut_{index:00}", position, yaw, scale);
            Primitive(root.transform, "HideWall", PrimitiveType.Cylinder, new Vector3(0f, .8f, 0f),
                new Vector3(2.35f, .8f, 2.35f), Hide);
            for (var i = 0; i < 6; i++)
            {
                var angle = i * Mathf.PI / 3f;
                Primitive(root.transform, $"TimberPost_{i}", PrimitiveType.Cylinder,
                    new Vector3(Mathf.Cos(angle) * 1.03f, .9f, Mathf.Sin(angle) * 1.03f),
                    new Vector3(.11f, 1.1f, .11f), DarkTrunk);
            }
            var roof = new GameObject("ThatchRoof");
            roof.transform.SetParent(root.transform, false);
            roof.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            roof.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            roof.AddComponent<MeshRenderer>().sharedMaterial = BootstrapMaterials.Get(Thatch);
            Primitive(root.transform, "Door", PrimitiveType.Cube, new Vector3(0f, .62f, -1.16f),
                new Vector3(.72f, 1.18f, .08f), DarkTrunk);
            return root;
        }

        public static GameObject CreateFireTile(Transform parent, Vector3 position, float scale, int index)
        {
            var root = TileRoot(parent, $"Tile_PrehistoricFire_{index:00}", position, index * 31f, scale);
            Primitive(root.transform, "LogA", PrimitiveType.Cube, new Vector3(0f, .12f, 0f),
                new Vector3(1.2f, .18f, .2f), DarkTrunk, new Vector3(0f, 28f, 0f));
            Primitive(root.transform, "LogB", PrimitiveType.Cube, new Vector3(0f, .13f, 0f),
                new Vector3(1.2f, .18f, .2f), Trunk, new Vector3(0f, -28f, 0f));
            Primitive(root.transform, "Ember", PrimitiveType.Sphere, new Vector3(0f, .34f, 0f),
                new Vector3(.62f, .40f, .62f), new Color(.92f, .25f, .035f));
            Primitive(root.transform, "Flame", PrimitiveType.Sphere, new Vector3(0f, .62f, 0f),
                new Vector3(.34f, .74f, .34f), new Color(1f, .62f, .07f));
            return root;
        }

        public static GameObject CreateRiverTile(Transform parent, Vector3 center, float length, float width, int seed)
        {
            var root = TileRoot(parent, $"Tile_PrehistoricRiver_{seed:00}", Vector3.zero, 0f, 1f);
            var points = new Vector3[7];
            for (var i = 0; i < points.Length; i++)
            {
                var t = i / (points.Length - 1f);
                points[i] = center + new Vector3((t - .5f) * length, .035f,
                    Mathf.Sin((t * 2.2f + seed * .31f) * Mathf.PI) * width * .42f);
            }
            AddLine(root.transform, "Water", points, width, Water, 5);
            var tangent = (points[^1] - points[0]).normalized;
            var side = new Vector3(-tangent.z, 0f, tangent.x) * width * .58f;
            var left = new Vector3[points.Length];
            var right = new Vector3[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                left[i] = points[i] + side + Vector3.up * .006f;
                right[i] = points[i] - side + Vector3.up * .006f;
            }
            AddLine(root.transform, "MudBankLeft", left, .42f, Bank, 2);
            AddLine(root.transform, "MudBankRight", right, .42f, Bank, 2);
            return root;
        }

        public static GameObject CreateStandingStoneTile(Transform parent, Vector3 position, float scale, float yaw, int index)
        {
            var root = TileRoot(parent, $"Tile_PrehistoricStandingStone_{index:00}", position, yaw, scale);
            Primitive(root.transform, "Megalith", PrimitiveType.Cube, new Vector3(0f, 1.05f, 0f),
                new Vector3(.72f, 2.1f, .48f), Stone, new Vector3(4f, 0f, -3f));
            Primitive(root.transform, "FootStone", PrimitiveType.Cube, new Vector3(.42f, .18f, .12f),
                new Vector3(.65f, .34f, .52f), Stone * .82f, new Vector3(0f, 19f, 7f));
            return root;
        }

        private static Transform Category(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        private static GameObject TileRoot(Transform parent, string name, Vector3 position, float yaw, float scale)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            root.transform.localScale = Vector3.one * scale;
            return root;
        }

        private static GameObject Primitive(Transform parent, string name, PrimitiveType type,
            Vector3 localPosition, Vector3 localScale, Color colour, Vector3 localEuler = default)
        {
            var result = GameObject.CreatePrimitive(type);
            result.name = name;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = localPosition;
            result.transform.localEulerAngles = localEuler;
            result.transform.localScale = localScale;
            result.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(colour);
            var components = result.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i].GetType().Name.Contains("Collider"))
                    Object.Destroy(components[i]);
            }
            return result;
        }

        private static void AddLine(Transform parent, string name, IReadOnlyList<Vector3> points,
            float width, Color colour, int cornerVertices)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var line = root.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Count;
            line.widthMultiplier = width;
            line.numCornerVertices = cornerVertices;
            line.numCapVertices = cornerVertices;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = true;
            line.sharedMaterial = BootstrapMaterials.Get(colour);
            for (var i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        }

        private static Mesh ConeMesh()
        {
            const int sides = 12;
            var vertices = new List<Vector3> { new(0f, 1.7f, 0f), new(0f, 0f, 0f) };
            for (var i = 0; i < sides; i++)
            {
                var angle = i * Mathf.PI * 2f / sides;
                vertices.Add(new Vector3(Mathf.Cos(angle) * 1.55f, 0f, Mathf.Sin(angle) * 1.55f));
            }
            var triangles = new List<int>();
            for (var i = 0; i < sides; i++)
            {
                var a = 2 + i;
                var b = 2 + (i + 1) % sides;
                triangles.Add(0); triangles.Add(a); triangles.Add(b);
                triangles.Add(1); triangles.Add(b); triangles.Add(a);
            }
            var mesh = new Mesh { name = "Reusable prehistoric thatch roof" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
