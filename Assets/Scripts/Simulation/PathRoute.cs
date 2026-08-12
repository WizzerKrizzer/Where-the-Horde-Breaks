using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Simulation
{
    public sealed class PathRoute : MonoBehaviour
    {
        [SerializeField] private List<Vector3> waypoints = new();
        [SerializeField] private List<Vector3> secondaryWaypoints = new();

        public IReadOnlyList<Vector3> Waypoints => waypoints;
        public IReadOnlyList<Vector3> SecondaryWaypoints => secondaryWaypoints;
        public float TotalLength { get; private set; }
        public bool HasUsableRoute => waypoints.Count > 1;
        public Vector3 StartPoint => waypoints.Count > 0 ? waypoints[0] : transform.position;
        public Vector3 EndPoint => waypoints.Count > 0 ? waypoints[^1] : transform.position;

        private void Awake()
        {
            RecalculateLength();
        }

        public void SetWaypoints(IEnumerable<Vector3> points)
        {
            waypoints.Clear();
            waypoints.AddRange(points);
            RecalculateLength();
        }

        public void SetSecondaryWaypoints(IEnumerable<Vector3> points)
        {
            secondaryWaypoints.Clear();
            if (points != null)
            {
                secondaryWaypoints.AddRange(points);
            }
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
            FindNearestPointOnRoute(waypoints, position, ref bestPoint, ref bestTangent, ref bestDistanceSq);
            FindNearestPointOnRoute(secondaryWaypoints, position, ref bestPoint, ref bestTangent, ref bestDistanceSq);
            tangent = bestTangent.sqrMagnitude < 0.001f ? Vector3.forward : bestTangent.normalized;
            return bestPoint;
        }

        private static void FindNearestPointOnRoute(
            IReadOnlyList<Vector3> route,
            Vector3 position,
            ref Vector3 bestPoint,
            ref Vector3 bestTangent,
            ref float bestDistanceSq)
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
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
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
