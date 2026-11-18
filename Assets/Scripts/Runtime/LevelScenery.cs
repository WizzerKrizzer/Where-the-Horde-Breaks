using System.Collections.Generic;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// Greybox scenery for a map (user, 2026-11-17): a river, rocks, trees and bushes, so the
    /// ground does not feel empty. Trees, rocks and the river keep towers off them; bushes do
    /// not. Everything stays clear of the road. Built once when the map loads.
    /// </summary>
    public sealed class LevelScenery : MonoBehaviour
    {
        // Every scenery built, so a tower check finds the one of its own road.
        private static readonly List<LevelScenery> All = new();
        // The ground that towers cannot use on this map: circles (x, z, radius).
        private readonly List<Vector3> Blocked = new();
        private PathRoute route;
        private static Mesh cone;
        private static Mesh rock;

        /// <summary>True when a tower footprint of `radius` at `position` touches a tree, rock or the river.</summary>
        public static bool BlocksTower(PathRoute road, Vector3 position, float radius)
        {
            foreach (var scenery in All)
            {
                if (scenery != null && scenery.route == road && scenery.Blocks(position, radius))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Blocks(Vector3 position, float radius)
        {
            for (var i = 0; i < Blocked.Count; i++)
            {
                var circle = Blocked[i];
                var dx = position.x - circle.x;
                var dz = position.z - circle.y;
                var reach = circle.z + radius;
                if (dx * dx + dz * dz < reach * reach)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDestroy()
        {
            All.Remove(this);
        }

        /// <summary>Level 1: a river in the lower left, woods along the edges, open ground at the turns.</summary>
        public static LevelScenery CreateLevelOne(Transform parent, PathRoute route, Rect area)
        {
            var root = new GameObject("LevelScenery").AddComponent<LevelScenery>();
            root.route = route;
            All.Add(root);
            root.transform.SetParent(parent, false);
            var random = new System.Random(20261117);

            // The river runs from the left edge to the bottom edge, below the road's first stretch
            // (layout A, 2026-11-18: that stretch now runs at z = -2, so the river is lower).
            var river = new[]
            {
                new Vector3(area.xMin - 14f, 0f, -7.5f),
                new Vector3(area.xMin + 1f, 0f, -8f),
                new Vector3(area.xMin + 6f, 0f, -9.5f),
                new Vector3(area.xMin + 8.5f, 0f, -11.5f),
                new Vector3(area.xMin + 9.5f, 0f, area.yMin - 14f)
            };
            root.BuildRiver(river, 3.2f, random);

            // Woods: thick past the map's edges, thin inside, never on the road.
            var outer = new Rect(area.xMin - 14f, area.yMin - 12f, area.width + 28f, area.height + 24f);
            for (var x = outer.xMin; x < outer.xMax; x += 2.6f)
            {
                for (var z = outer.yMin; z < outer.yMax; z += 2.6f)
                {
                    var p = new Vector3(x + Jitter(random, 1.1f), 0f, z + Jitter(random, 1.1f));
                    var inside = area.Contains(new Vector2(p.x, p.z));
                    var edge = Mathf.Min(p.x - area.xMin, area.xMax - p.x, p.z - area.yMin, area.yMax - p.z);
                    // Inside the map only a few trees near its edges; outside, a forest.
                    var chance = inside ? (edge < 3.5f ? 0.28f : 0.02f) : 0.8f;
                    if (random.NextDouble() > chance || route.IsInsideCorridor(p, 2.4f) || NearRiver(river, p, 2.6f))
                    {
                        continue;
                    }

                    root.Tree(p, 0.8f + (float)random.NextDouble() * 0.6f, random, inside);
                }
            }

            // Rocks: a few boulders on open ground and along the river.
            for (var i = 0; i < 26; i++)
            {
                var p = new Vector3(Mathf.Lerp(area.xMin - 4f, area.xMax + 4f, (float)random.NextDouble()), 0f,
                    Mathf.Lerp(area.yMin - 4f, area.yMax + 4f, (float)random.NextDouble()));
                if (route.IsInsideCorridor(p, 1.6f) || NearRiver(river, p, 1.8f))
                {
                    continue;
                }

                var size = i < 8 ? 1.2f + (float)random.NextDouble() * 0.9f : 0.4f + (float)random.NextDouble() * 0.5f;
                root.Rock(p, size, random, area.Contains(new Vector2(p.x, p.z)));
            }

            // Bushes: small, everywhere off the road; towers may stand on them.
            for (var i = 0; i < 70; i++)
            {
                var p = new Vector3(Mathf.Lerp(area.xMin - 2f, area.xMax + 2f, (float)random.NextDouble()), 0f,
                    Mathf.Lerp(area.yMin - 2f, area.yMax + 2f, (float)random.NextDouble()));
                if (route.IsInsideCorridor(p, 0.9f) || NearRiver(river, p, 1.0f))
                {
                    continue;
                }

                root.Bush(p, 0.5f + (float)random.NextDouble() * 0.5f, random);
            }

            return root;
        }

        private void BuildRiver(Vector3[] points, float width, System.Random random)
        {
            var water = BootstrapMaterials.Get(new Color(0.24f, 0.29f, 0.33f));
            var bank = BootstrapMaterials.Get(PlaceholderPalette.Grey(0.36f));
            var smooth = Smooth(points, 6);
            Strip(smooth, width + 1.2f, 0.012f, bank, "RiverBank");
            Strip(smooth, width, 0.02f, water, "River");
            for (var i = 0; i < smooth.Count; i++)
            {
                Blocked.Add(new Vector3(smooth[i].x, smooth[i].z, width * 0.5f + 0.3f));
                if (i % 3 == 0)
                {
                    var side = random.NextDouble() < 0.5 ? -1f : 1f;
                    var along = (smooth[Mathf.Min(i + 1, smooth.Count - 1)] - smooth[Mathf.Max(i - 1, 0)]).normalized;
                    var across = new Vector3(-along.z, 0f, along.x);
                    Rock(smooth[i] + across * side * (width * 0.5f + 0.3f), 0.35f + (float)random.NextDouble() * 0.3f, random, false);
                }
            }
        }

        private void Tree(Vector3 position, float size, System.Random random, bool blocks)
        {
            var tree = new GameObject("Tree").transform;
            tree.SetParent(transform, false);
            tree.position = position;
            tree.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            Part(tree, PrimitiveType.Cylinder, new Color(0.27f, 0.22f, 0.17f), new Vector3(0f, 0.6f * size, 0f), new Vector3(0.3f, 0.6f, 0.3f) * size);
            var shade = (float)random.NextDouble() * 0.05f;
            var leaves = new Color(0.2f + shade, 0.3f + shade, 0.2f + shade);
            Mesh(tree, Cone, leaves, new Vector3(0f, 1.1f * size, 0f), new Vector3(2.2f, 2.0f, 2.2f) * size);
            Mesh(tree, Cone, leaves, new Vector3(0f, 2.1f * size, 0f), new Vector3(1.6f, 1.7f, 1.6f) * size);
            if (blocks)
            {
                Blocked.Add(new Vector3(position.x, position.z, 1.0f * size));
            }
        }

        private void Rock(Vector3 position, float size, System.Random random, bool blocks)
        {
            var stone = new GameObject("Rock").transform;
            stone.SetParent(transform, false);
            stone.position = position;
            stone.rotation = Quaternion.Euler(Jitter(random, 12f), (float)random.NextDouble() * 360f, Jitter(random, 12f));
            Mesh(stone, RockMesh, PlaceholderPalette.Grey(0.4f + (float)random.NextDouble() * 0.08f),
                new Vector3(0f, 0.25f * size, 0f), new Vector3(1.3f, 0.75f, 1.0f) * size);
            if (blocks && size > 0.9f)
            {
                Blocked.Add(new Vector3(position.x, position.z, 0.65f * size));
            }
        }

        private void Bush(Vector3 position, float size, System.Random random)
        {
            var bush = new GameObject("Bush").transform;
            bush.SetParent(transform, false);
            bush.position = position;
            bush.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            var shade = (float)random.NextDouble() * 0.04f;
            // Dry and yellowish, so a bush never reads as a goblin.
            var colour = new Color(0.46f + shade, 0.42f + shade, 0.27f + shade);
            Mesh(bush, LowPolyShapes.Puff, colour, new Vector3(0f, 0.25f * size, 0f), new Vector3(1.2f, 0.7f, 1.0f) * size);
            Mesh(bush, LowPolyShapes.Puff, colour, new Vector3(0.35f * size, 0.18f * size, 0.2f * size), new Vector3(0.8f, 0.55f, 0.8f) * size);
        }

        private void Strip(IReadOnlyList<Vector3> points, float width, float height, Material material, string name)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < points.Count; i++)
            {
                var along = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                var across = new Vector3(-along.z, 0f, along.x) * (width * 0.5f);
                vertices.Add(points[i] - across + Vector3.up * height);
                vertices.Add(points[i] + across + Vector3.up * height);
                if (i > 0)
                {
                    var b = vertices.Count - 4;
                    triangles.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static List<Vector3> Smooth(Vector3[] points, int steps)
        {
            var result = new List<Vector3>();
            for (var i = 0; i < points.Length - 1; i++)
            {
                var p0 = points[Mathf.Max(i - 1, 0)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Mathf.Min(i + 2, points.Length - 1)];
                for (var s = 0; s < steps; s++)
                {
                    var t = s / (float)steps;
                    var t2 = t * t;
                    var t3 = t2 * t;
                    result.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }

            result.Add(points[^1]);
            return result;
        }

        private static bool NearRiver(Vector3[] river, Vector3 p, float reach)
        {
            var flat = new Vector2(p.x, p.z);
            for (var i = 0; i < river.Length - 1; i++)
            {
                var a = new Vector2(river[i].x, river[i].z);
                var b = new Vector2(river[i + 1].x, river[i + 1].z);
                var ab = b - a;
                var t = Mathf.Clamp01(Vector2.Dot(flat - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                if ((a + ab * t - flat).sqrMagnitude < (reach + 1.6f) * (reach + 1.6f))
                {
                    return true;
                }
            }

            return false;
        }

        private static float Jitter(System.Random random, float amount) => ((float)random.NextDouble() * 2f - 1f) * amount;

        private void Part(Transform parent, PrimitiveType type, Color colour, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(type);
            foreach (var component in part.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(colour);
        }

        private static void Mesh(Transform parent, Mesh mesh, Color colour, Vector3 position, Vector3 scale)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = BootstrapMaterials.Get(colour);
        }

        // A seven-sided cone one unit across and one tall, base at y 0, flat shaded.
        private static Mesh Cone
        {
            get
            {
                if (cone != null)
                {
                    return cone;
                }

                const int sides = 7;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                var tip = new Vector3(0f, 1f, 0f);
                for (var i = 0; i < sides; i++)
                {
                    var a = Ring(i, sides, 0.5f);
                    var b = Ring(i + 1, sides, 0.5f);
                    AddTriangle(vertices, triangles, a, tip, b);
                    AddTriangle(vertices, triangles, b, Vector3.zero, a);
                }

                cone = Build("SceneryCone", vertices, triangles);
                return cone;
            }
        }

        // A lumpy boulder: a squashed, uneven icosahedron, flat shaded.
        private static Mesh RockMesh
        {
            get
            {
                if (rock != null)
                {
                    return rock;
                }

                var source = LowPolyShapes.Puff;
                var src = source.vertices;
                var random = new System.Random(5);
                var lump = new Dictionary<Vector3, float>();
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                foreach (var v in src)
                {
                    if (!lump.ContainsKey(v))
                    {
                        lump[v] = 0.8f + (float)random.NextDouble() * 0.4f;
                    }

                    var p = v * lump[v];
                    vertices.Add(new Vector3(p.x, Mathf.Max(p.y, -0.1f), p.z));
                }

                triangles.AddRange(source.triangles);
                rock = Build("SceneryRock", vertices, triangles);
                return rock;
            }
        }

        private static Vector3 Ring(int i, int sides, float radius)
        {
            var angle = i * Mathf.PI * 2f / sides;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            triangles.Add(vertices.Count);
            vertices.Add(a);
            triangles.Add(vertices.Count);
            vertices.Add(b);
            triangles.Add(vertices.Count);
            vertices.Add(c);
        }

        private static Mesh Build(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
