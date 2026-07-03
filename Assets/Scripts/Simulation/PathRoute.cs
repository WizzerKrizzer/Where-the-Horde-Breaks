using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Simulation
{
    public sealed class PathRoute : MonoBehaviour
    {
        [SerializeField] private List<Vector3> waypoints = new();

        public IReadOnlyList<Vector3> Waypoints => waypoints;
        public float TotalLength { get; private set; }

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

        public Vector3 Sample(float distance)
        {
            return PathSampler.Sample(waypoints, distance);
        }

        private void RecalculateLength()
        {
            TotalLength = PathSampler.GetLength(waypoints);
        }
    }
}
