using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// Every tower's border on the ground (user, 2026-11-11): a tower may not stand on the road or on
    /// another tower. For a tower built from a model the border is its real outline seen from above,
    /// measured from the model by TowerFootprintBakeTests and kept here as numbers, so the rule is the
    /// same on a machine without the local models; any other tower's border is the circle of its
    /// greybox. In the tower's frame: x across (to the right facing the road), y towards the road,
    /// metres from the tower's spot. Re-measure when a model or its size changes.
    /// </summary>
    public static class TowerFootprints
    {
        // TowerFootprintBakeTests, 2026-11-12: the tipi and the rock at 75% (TowerCrewVisual), the cave
        // 6.5 m deep (CaveVisual), the mammoth's pen 6.6 m wide (MammothVisual).
        private static readonly Dictionary<string, Vector2[]> Measured = new()
        {
            { "slingers", new[] { new Vector2(-2.35f, -3.54f), new Vector2(-2.32f, -3.62f), new Vector2(-1.83f, -4.31f), new Vector2(-1.03f, -5.12f), new Vector2(-0.98f, -5.17f), new Vector2(0.04f, -5.50f), new Vector2(1.00f, -5.19f), new Vector2(1.72f, -4.68f), new Vector2(1.82f, -4.60f), new Vector2(2.28f, -3.76f), new Vector2(2.32f, -3.68f), new Vector2(2.35f, -1.71f), new Vector2(2.30f, -1.65f), new Vector2(0.38f, 0.30f), new Vector2(0.26f, 0.30f), new Vector2(-0.26f, 0.30f), new Vector2(-0.45f, 0.27f), new Vector2(-2.25f, -1.67f), new Vector2(-2.31f, -1.73f), new Vector2(-2.33f, -2.43f) } },
            { "spear_thrower", new[] { new Vector2(-3.11f, -2.21f), new Vector2(-3.00f, -2.77f), new Vector2(-2.88f, -3.14f), new Vector2(-2.10f, -4.03f), new Vector2(-1.77f, -4.19f), new Vector2(-0.35f, -4.67f), new Vector2(0.24f, -4.68f), new Vector2(1.31f, -4.58f), new Vector2(2.04f, -4.25f), new Vector2(2.98f, -3.46f), new Vector2(3.11f, -3.08f), new Vector2(3.10f, -2.91f), new Vector2(2.90f, -1.63f), new Vector2(2.61f, -0.06f), new Vector2(2.58f, -0.02f), new Vector2(0.07f, 0.28f), new Vector2(-0.30f, 0.30f), new Vector2(-1.39f, 0.20f), new Vector2(-2.33f, -0.11f), new Vector2(-2.74f, -0.41f) } },
            { "cave", new[] { new Vector2(-2.90f, -2.80f), new Vector2(-2.74f, -4.28f), new Vector2(-2.67f, -4.48f), new Vector2(-2.02f, -5.51f), new Vector2(-1.68f, -5.85f), new Vector2(-0.66f, -6.18f), new Vector2(0.25f, -6.20f), new Vector2(0.72f, -6.07f), new Vector2(1.64f, -5.59f), new Vector2(1.97f, -5.35f), new Vector2(2.27f, -4.97f), new Vector2(2.71f, -3.64f), new Vector2(2.90f, -2.60f), new Vector2(2.87f, -2.26f), new Vector2(2.71f, -1.99f), new Vector2(1.71f, -0.48f), new Vector2(1.04f, 0.17f), new Vector2(-0.46f, 0.30f), new Vector2(-1.41f, -0.13f), new Vector2(-2.71f, -2.01f) } },
            { "mammoth", new[] { new Vector2(-3.26f, -2.38f), new Vector2(-3.13f, -3.77f), new Vector2(-3.07f, -3.98f), new Vector2(-2.73f, -4.69f), new Vector2(-2.38f, -5.15f), new Vector2(-1.84f, -5.64f), new Vector2(-0.94f, -6.03f), new Vector2(0.41f, -6.30f), new Vector2(1.67f, -5.90f), new Vector2(2.43f, -5.23f), new Vector2(3.20f, -4.15f), new Vector2(3.26f, -4.03f), new Vector2(3.22f, -3.26f), new Vector2(3.05f, -1.96f), new Vector2(2.99f, -1.75f), new Vector2(2.14f, -0.88f), new Vector2(0.72f, -0.22f), new Vector2(-2.35f, 0.30f), new Vector2(-2.93f, 0.05f), new Vector2(-3.15f, -0.40f) } },
        };

        private const int CircleCorners = 12;
        private static readonly Dictionary<TowerDefinition, Vector2[]> Greybox = new();
        // The road's edge is checked every this many metres along the border.
        private const float EdgeStep = 0.25f;

        /// <summary>The tower's border in its own frame (x across, y towards the road).</summary>
        public static Vector2[] Local(TowerDefinition definition)
        {
            if (definition != null && Measured.TryGetValue(definition.id, out var outline))
            {
                return outline;
            }

            if (definition != null && Greybox.TryGetValue(definition, out var cached))
            {
                return cached;
            }

            // The greybox: a cylinder (or a block) as wide as its scale.
            var probe = new GameObject("Footprint probe").transform;
            TowerVisualFactory.ApplyScale(probe, definition);
            var radius = Mathf.Max(probe.localScale.x, probe.localScale.z) * 0.5f;
            if (Application.isPlaying)
            {
                Object.Destroy(probe.gameObject);
            }
            else
            {
                Object.DestroyImmediate(probe.gameObject);
            }

            var circle = new Vector2[CircleCorners];
            for (var i = 0; i < CircleCorners; i++)
            {
                var angle = i * Mathf.PI * 2f / CircleCorners;
                circle[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            if (definition != null)
            {
                Greybox[definition] = circle;
            }

            return circle;
        }

        /// <summary>The tower's border on the map (x, z) for a tower on `position` facing the road.</summary>
        public static Vector2[] World(TowerDefinition definition, Vector3 position, PathRoute route)
        {
            var toRoad = ToRoad(position, route);
            var across = Vector3.Cross(Vector3.up, toRoad);
            var local = Local(definition);
            var world = new Vector2[local.Length];
            for (var i = 0; i < local.Length; i++)
            {
                var point = position + across * local[i].x + toRoad * local[i].y;
                world[i] = new Vector2(point.x, point.z);
            }

            return world;
        }

        /// <summary>Which way a tower on `position` faces: to the nearest point of the road.</summary>
        public static Vector3 ToRoad(Vector3 position, PathRoute route)
        {
            var toRoad = Vector3.forward;
            if (route != null && route.TotalLength > 0f)
            {
                toRoad = route.GetNearestRoadPoint(position, out _) - position;
            }

            toRoad.y = 0f;
            return toRoad.sqrMagnitude > 1e-6f ? toRoad.normalized : Vector3.forward;
        }

        /// <summary>True when any part of the border lies on the road.</summary>
        public static bool EntersRoad(Vector2[] border, PathRoute route, float y)
        {
            if (route == null || border == null)
            {
                return false;
            }

            for (var i = 0; i < border.Length; i++)
            {
                var a = border[i];
                var b = border[(i + 1) % border.Length];
                var steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / EdgeStep));
                for (var s = 0; s < steps; s++)
                {
                    var point = Vector2.Lerp(a, b, s / (float)steps);
                    if (route.IsInsideCorridor(new Vector3(point.x, y, point.y)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>True when two convex borders overlap (separating axes).</summary>
        public static bool Overlap(Vector2[] first, Vector2[] second)
        {
            return !HasSeparatingAxis(first, second) && !HasSeparatingAxis(second, first);
        }

        private static bool HasSeparatingAxis(Vector2[] shape, Vector2[] other)
        {
            for (var i = 0; i < shape.Length; i++)
            {
                var edge = shape[(i + 1) % shape.Length] - shape[i];
                var axis = new Vector2(-edge.y, edge.x);
                if (axis.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                Project(shape, axis, out var minA, out var maxA);
                Project(other, axis, out var minB, out var maxB);
                if (maxA <= minB || maxB <= minA)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Project(Vector2[] shape, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var point in shape)
            {
                var d = Vector2.Dot(point, axis);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
        }
    }
}
