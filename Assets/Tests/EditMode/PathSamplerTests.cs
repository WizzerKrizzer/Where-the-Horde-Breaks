using NUnit.Framework;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Tests
{
    public sealed class PathSamplerTests
    {
        [Test]
        public void GetLength_SumsPolylineSegments()
        {
            var points = new[]
            {
                Vector3.zero,
                new Vector3(3f, 0f, 4f),
                new Vector3(6f, 0f, 8f)
            };

            Assert.That(PathSampler.GetLength(points), Is.EqualTo(10f).Within(0.001f));
        }

        [Test]
        public void Sample_ReturnsPointAlongSegment()
        {
            var points = new[]
            {
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                new Vector3(10f, 0f, 10f)
            };

            Assert.That(PathSampler.Sample(points, 15f), Is.EqualTo(new Vector3(10f, 0f, 5f)));
        }
    }
}
