using NUnit.Framework;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Tests
{
    public sealed class HordeFlowFieldTests
    {
        [Test]
        public void FlowField_TurnsAroundRightAngleWithoutLeavingRoad()
        {
            var route = new[]
            {
                new Vector3(-8f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 8f)
            };
            var field = new HordeFlowField(route, null, 2.7f, 0.6f);
            var position = new Vector3(-7.5f, 0f, 1.8f);
            var previousDistance = field.GetDistanceToExit(position);

            for (var step = 0; step < 160; step++)
            {
                if (field.HasReachedExit(position))
                {
                    break;
                }

                var desired = position + field.GetDirection(position) * 0.18f;
                position = field.ConstrainMove(position, desired);
                Assert.That(field.IsWalkable(position), Is.True, $"Agent left the corridor at step {step}.");
                var distance = field.GetDistanceToExit(position);
                Assert.That(distance, Is.LessThanOrEqualTo(previousDistance + field.CellSize * 1.5f));
                previousDistance = distance;
            }

            Assert.That(field.HasReachedExit(position), Is.True);
            Assert.That(Mathf.Abs(position.x), Is.GreaterThan(0.5f), "The flow collapsed onto the exit center line.");
        }

        [Test]
        public void FlowField_UsesConnectedSecondaryRoute()
        {
            var primary = new[]
            {
                new Vector3(-8f, 0f, -2f),
                new Vector3(0f, 0f, -2f),
                new Vector3(8f, 0f, 0f)
            };
            var secondary = new[]
            {
                new Vector3(-8f, 0f, 2f),
                new Vector3(0f, 0f, 2f),
                new Vector3(8f, 0f, 0f)
            };
            var field = new HordeFlowField(primary, secondary, 1.5f, 0.5f);
            var position = new Vector3(-7f, 0f, 2f);

            Assert.That(field.IsWalkable(position), Is.True);
            Assert.That(field.GetDirection(position).x, Is.GreaterThan(0.25f));
            Assert.That(field.GetDistanceToExit(position), Is.LessThan(float.PositiveInfinity));
        }

        [Test]
        public void ConstrainMove_SlidesAlongWall()
        {
            var route = new[] { new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, 0f) };
            var field = new HordeFlowField(route, null, 2f, 0.5f);
            var current = new Vector3(-2f, 0f, 1.7f);
            var result = field.ConstrainMove(current, new Vector3(-1f, 0f, 3f));

            Assert.That(field.IsWalkable(result), Is.True);
            Assert.That(result.x, Is.GreaterThan(current.x));
            Assert.That(result.z, Is.LessThan(2.3f));
        }

        [Test]
        public void ExitCrossSection_DoesNotMarkEarlierRoadIntersectionsAsExit()
        {
            var route = new[]
            {
                new Vector3(-18f, 0f, -10f),
                new Vector3(-10f, 0f, -2f),
                new Vector3(-5f, 0f, -7f),
                new Vector3(5f, 0f, -6f),
                new Vector3(11f, 0f, -3f),
                new Vector3(16f, 0f, 3f),
                new Vector3(10f, 0f, 10f)
            };
            var field = new HordeFlowField(route, null, 2.31f, 0.62f);

            Assert.That(field.HasReachedExit(new Vector3(-6f, 0f, -3.5f)), Is.False);

            var position = route[0];
            for (var step = 0; step < 1200 && !field.HasReachedExit(position); step++)
            {
                position = field.ConstrainMove(position, position + field.GetDirection(position) * 0.12f);
            }

            Assert.That(field.HasReachedExit(position), Is.True);
            Assert.That(Vector3.Distance(position, route[^1]), Is.LessThan(2.5f));
        }

        [Test]
        public void VariableWidthCorridor_UsesAuthoredNarrowAndWideSections()
        {
            var route = new[]
            {
                new Vector3(-12f, 0f, 0f),
                Vector3.zero,
                new Vector3(12f, 0f, 0f)
            };
            var halfWidths = new[] { 5f, 1.5f, 5f };
            var field = new HordeFlowField(route, null, 5f, 0.5f, halfWidths);

            Assert.That(field.IsWalkable(new Vector3(-10f, 0f, 3.5f)), Is.True,
                "The entrance side should use the wide authored section.");
            Assert.That(field.IsWalkable(new Vector3(0f, 0f, 2.25f)), Is.False,
                "The middle should contract to the authored choke width.");
            Assert.That(field.IsWalkable(new Vector3(10f, 0f, 3.5f)), Is.True,
                "The exit side should expand back to the wide authored section.");
        }

        [Test]
        public void LevelTwoVariableWidthRoute_StaysOnRoadAndReachesExit()
        {
            var route = new[]
            {
                new Vector3(-56f, 0f, 14f),
                new Vector3(-34f, 0f, 14f),
                new Vector3(-24f, 0f, 14f),
                new Vector3(-24f, 0f, -5f),
                new Vector3(-24f, 0f, -17f),
                new Vector3(2f, 0f, -17f),
                new Vector3(10f, 0f, -17f),
                new Vector3(18f, 0f, -17f),
                new Vector3(36f, 0f, -17f),
                new Vector3(42f, 0f, -11f),
                new Vector3(42f, 0f, 10f),
                new Vector3(56f, 0f, 10f)
            };
            var halfWidths = new[] { 2.66f, 2.66f, 2.66f, 2.16f, 1.16f, 1.16f, 2.16f, 4.66f, 4.66f, 4.16f, 3.16f, 2.66f };
            var field = new HordeFlowField(route, null, 2.31f, 0.62f, halfWidths);
            var position = field.GetSpawnPoint(1.8f);

            for (var step = 0; step < 3000 && !field.HasReachedExit(position); step++)
            {
                var previous = position;
                position = field.ConstrainMove(position, position + field.GetDirection(position) * 0.08f);
                Assert.That(field.IsWalkable(position), Is.True, $"The Level 2 route was left at step {step}.");
                Assert.That(Vector3.Distance(previous, position), Is.LessThan(0.12f),
                    $"The Level 2 route teleported at step {step}.");
            }

            Assert.That(field.HasReachedExit(position), Is.True, "The Level 2 flow did not reach its real exit.");
        }
    }
}
