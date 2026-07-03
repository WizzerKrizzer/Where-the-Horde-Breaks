using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Simulation
{
    public static class PathSampler
    {
        public static float GetLength(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 2)
            {
                return 0f;
            }

            var length = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                length += Vector3.Distance(points[i - 1], points[i]);
            }

            return length;
        }

        public static Vector3 Sample(IReadOnlyList<Vector3> points, float distance)
        {
            if (points == null || points.Count == 0)
            {
                return Vector3.zero;
            }

            if (points.Count == 1 || distance <= 0f)
            {
                return points[0];
            }

            var remaining = distance;
            for (var i = 1; i < points.Count; i++)
            {
                var from = points[i - 1];
                var to = points[i];
                var segmentLength = Vector3.Distance(from, to);
                if (remaining <= segmentLength)
                {
                    return Vector3.Lerp(from, to, remaining / segmentLength);
                }

                remaining -= segmentLength;
            }

            return points[^1];
        }
    }
}
