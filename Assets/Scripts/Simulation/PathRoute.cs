using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Simulation
{
    public sealed class PathRoute : MonoBehaviour
    {
        [SerializeField] private List<Vector3> waypoints = new();
        [SerializeField] private List<float> waypointHalfWidths = new();
        [SerializeField] private List<Vector3> secondaryWaypoints = new();
        [SerializeField] private List<float> secondaryWaypointHalfWidths = new();

        public IReadOnlyList<Vector3> Waypoints => waypoints;
        public IReadOnlyList<float> WaypointHalfWidths => waypointHalfWidths;
        public IReadOnlyList<Vector3> SecondaryWaypoints => secondaryWaypoints;
        public IReadOnlyList<float> SecondaryWaypointHalfWidths => secondaryWaypointHalfWidths;
        public float TotalLength { get; private set; }
        public bool HasUsableRoute => waypoints.Count > 1;
        public Vector3 StartPoint => waypoints.Count > 0 ? waypoints[0] : transform.position;
        public Vector3 EndPoint => waypoints.Count > 0 ? waypoints[^1] : transform.position;

        public bool IsInsideCorridor(Vector3 position, float extraRadius = 0f)
        {
            return IsInsideRouteCorridor(position, waypoints, waypointHalfWidths, extraRadius) ||
                   IsInsideRouteCorridor(position, secondaryWaypoints, secondaryWaypointHalfWidths, extraRadius);
        }

        private void Awake()
        {
            RecalculateLength();
        }

        public void SetWaypoints(IEnumerable<Vector3> points, IReadOnlyList<float> fullWidths = null)
        {
            waypoints.Clear();
            waypoints.AddRange(points);
            SetHalfWidths(waypointHalfWidths, waypoints.Count, fullWidths);
            RecalculateLength();
        }

        public void SetSecondaryWaypoints(IEnumerable<Vector3> points, IReadOnlyList<float> fullWidths = null)
        {
            secondaryWaypoints.Clear();
            if (points != null)
            {
                secondaryWaypoints.AddRange(points);
            }
            SetHalfWidths(secondaryWaypointHalfWidths, secondaryWaypoints.Count, fullWidths);
        }

        private static void SetHalfWidths(List<float> destination, int count, IReadOnlyList<float> fullWidths)
        {
            destination.Clear();
            if (fullWidths == null || fullWidths.Count != count)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                destination.Add(Mathf.Max(0.5f, fullWidths[i] * 0.5f));
            }
        }

        private static bool IsInsideRouteCorridor(
            Vector3 position,
            IReadOnlyList<Vector3> points,
            IReadOnlyList<float> halfWidths,
            float extraRadius)
        {
            if (points == null || points.Count < 2)
            {
                return false;
            }

            position.y = 0f;
            for (var i = 1; i < points.Count; i++)
            {
                var from = points[i - 1];
                var to = points[i];
                from.y = to.y = 0f;
                var segment = to - from;
                var segmentLengthSq = segment.sqrMagnitude;
                var t = segmentLengthSq > 0.0001f
                    ? Mathf.Clamp01(Vector3.Dot(position - from, segment) / segmentLengthSq)
                    : 0f;
                var fromHalfWidth = halfWidths != null && halfWidths.Count == points.Count
                    ? halfWidths[i - 1]
                    : 2.7f;
                var toHalfWidth = halfWidths != null && halfWidths.Count == points.Count
                    ? halfWidths[i]
                    : 2.7f;
                var radius = Mathf.Max(0f, Mathf.Lerp(fromHalfWidth, toHalfWidth, t) + extraRadius);
                var nearest = from + segment * t;
                if ((position - nearest).sqrMagnitude <= radius * radius)
                {
                    return true;
                }
            }

            return false;
        }

        public Vector3 Sample(float distance)
        {
            if (waypoints.Count == 0)
            {
                return transform.position;
            }

            return PathSampler.Sample(waypoints, distance);
        }

        public Vector3 GetNearestRoadPoint(Vector3 position, out Vector3 tangent)
        {
            var bestPoint = waypoints.Count > 0 ? waypoints[0] : position;
            var bestTangent = Vector3.forward;
            var bestDistanceSq = float.PositiveInfinity;
            FindNearestPointOnRoute(waypoints, position, Vector3.zero, ref bestPoint, ref bestTangent, ref bestDistanceSq);
            FindNearestPointOnRoute(secondaryWaypoints, position, Vector3.zero, ref bestPoint, ref bestTangent, ref bestDistanceSq);
            tangent = bestTangent.sqrMagnitude < 0.001f ? Vector3.forward : bestTangent.normalized;
            return bestPoint;
        }

        public Vector3 GetNearestRoadPointToward(Vector3 position, Vector3 target, out Vector3 tangent)
        {
            var bestPoint = waypoints.Count > 0 ? waypoints[0] : position;
            var bestTangent = Vector3.forward;
            var bestScore = float.PositiveInfinity;
            var directionToTarget = target - position;
            directionToTarget.y = 0f;
            if (directionToTarget.sqrMagnitude > 0.001f)
            {
                directionToTarget.Normalize();
            }

            FindNearestPointOnRoute(waypoints, position, directionToTarget, ref bestPoint, ref bestTangent, ref bestScore);
            FindNearestPointOnRoute(secondaryWaypoints, position, directionToTarget, ref bestPoint, ref bestTangent, ref bestScore);
            tangent = bestTangent.sqrMagnitude < 0.001f ? Vector3.forward : bestTangent.normalized;
            return bestPoint;
        }

        private static void FindNearestPointOnRoute(
            IReadOnlyList<Vector3> route,
            Vector3 position,
            Vector3 preferredDirection,
            ref Vector3 bestPoint,
            ref Vector3 bestTangent,
            ref float bestScore)
        {
            if (route == null || route.Count < 2)
            {
                return;
            }

            for (var i = 1; i < route.Count; i++)
            {
                var from = route[i - 1];
                var to = route[i];
                var segment = to - from;
                var segmentLengthSq = segment.sqrMagnitude;
                if (segmentLengthSq <= 0.001f)
                {
                    continue;
                }

                var t = Mathf.Clamp01(Vector3.Dot(position - from, segment) / segmentLengthSq);
                var point = from + segment * t;
                var distanceSq = (position - point).sqrMagnitude;
                var score = distanceSq;
                if (preferredDirection.sqrMagnitude > 0.001f)
                {
                    var tangent = segment.normalized;
                    score -= Mathf.Abs(Vector3.Dot(tangent, preferredDirection)) * 2.25f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPoint = point;
                    bestTangent = segment.normalized;
                }
            }
        }

        private void RecalculateLength()
        {
            TotalLength = PathSampler.GetLength(waypoints);
        }
    }
}
