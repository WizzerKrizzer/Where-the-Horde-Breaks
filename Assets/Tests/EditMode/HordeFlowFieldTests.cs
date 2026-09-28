using NUnit.Framework;
using System.Collections.Generic;
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
        public void FlowField_UsesThreeConnectedRoutes()
        {
            var primary = new[]
            {
                new Vector3(-8f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(8f, 0f, 0f)
            };
            var secondary = new[]
            {
                new Vector3(-8f, 0f, 0f),
                new Vector3(0f, 0f, 4f),
                new Vector3(8f, 0f, 0f)
            };
            var tertiary = new[]
            {
                new Vector3(-8f, 0f, 0f),
                new Vector3(0f, 0f, -4f),
                new Vector3(8f, 0f, 0f)
            };
            var field = new HordeFlowField(primary, secondary, 1.5f, 0.5f, null, null, tertiary);

            Assert.That(field.IsWalkable(new Vector3(0f, 0f, 4f)), Is.True);
            Assert.That(field.IsWalkable(new Vector3(0f, 0f, 0f)), Is.True);
            Assert.That(field.IsWalkable(new Vector3(0f, 0f, -4f)), Is.True);
            Assert.That(field.GetDirection(new Vector3(0f, 0f, -4f)).x, Is.GreaterThan(0.25f));
        }

        [Test]
        public void FlowField_SquareCornersFillSharpOuterMiter()
        {
            var route = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 4f)
            };
            var rounded = new HordeFlowField(route, null, 1f, 0.25f);
            var square = new HordeFlowField(route, null, 1f, 0.25f, null, null, null, null, true);
            var outerMiter = new Vector3(4.8f, 0f, -0.8f);

            Assert.That(rounded.IsWalkable(outerMiter), Is.False);
            Assert.That(square.IsWalkable(outerMiter), Is.True);
        }

        [Test]
        public void FlowField_CoincidentRouteCreatesCircularArena()
        {
            var route = new[] { Vector3.zero, Vector3.zero };
            var field = new HordeFlowField(route, null, 10f, 0.25f);

            Assert.That(field.IsWalkable(Vector3.zero), Is.True);
            Assert.That(field.IsWalkable(new Vector3(9.8f, 0f, 0f)), Is.True);
            Assert.That(field.IsWalkable(new Vector3(7.2f, 0f, 7.2f)), Is.False);
            Assert.That(field.IsWalkable(new Vector3(10.2f, 0f, 0f)), Is.False);
        }

        [Test]
        public void FlowField_SquareCornersIgnoreCollinearWidthWaypoint()
        {
            var route = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(8f, 0f, 0f),
                new Vector3(8f, 0f, 4f)
            };
            var field = new HordeFlowField(route, null, 1f, 0.25f, null, null, null, null, true);

            Assert.That(field.IsWalkable(new Vector3(4f, 0f, 3f)), Is.False,
                "A collinear waypoint must not create a walkable strip across the map.");
        }

        [Test]
        public void FlowField_SquareCornersRemoveRoundedSegmentCap()
        {
            var route = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(3f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, -4f)
            };
            var halfWidths = new[] { 2.5f, 2.5f, 1f, 1f };
            var field = new HordeFlowField(
                route,
                null,
                1f,
                0.25f,
                halfWidths,
                null,
                null,
                null,
                true);

            Assert.That(field.IsWalkable(new Vector3(4.9f, 0f, 2.4f)), Is.True,
                "The square outer corner must remain filled.");
            Assert.That(field.IsWalkable(new Vector3(5.4f, 0f, 0f)), Is.False,
                "A collinear width waypoint must not leave a rounded cap past the narrow outgoing edge.");
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
        public void ContinuousBoundary_IsStraightAlongStraightRoadSection()
        {
            var route = new[] { new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f) };
            var field = new HordeFlowField(route, null, 2f, 0.5f);
            var boundary = new List<HordeFlowField.BoundarySegment>();

            field.GetBoundarySegments(boundary);

            Assert.That(field.IsWalkable(new Vector3(0f, 0f, 1.99f)), Is.True);
            Assert.That(field.IsWalkable(new Vector3(0f, 0f, 2.01f)), Is.False);
            foreach (var segment in boundary)
            {
                var midpoint = (segment.From + segment.To) * 0.5f;
                if (Mathf.Abs(midpoint.x) >= 6f)
                {
                    continue;
                }

                Assert.That(Mathf.Abs(segment.To.z - segment.From.z), Is.LessThan(0.001f),
                    "A straight road edge must not contain grid-shaped steps.");
            }
        }

        [Test]
        public void ContinuousBoundary_DoesNotTreatEdgeCellsAsExit()
        {
            var route = new[] { new Vector3(-8f, 0f, -8f), new Vector3(8f, 0f, 8f) };
            var field = new HordeFlowField(route, null, 2f, 0.5f);

            for (var z = -5f; z <= 5f; z += 0.1f)
            {
                for (var x = -5f; x <= 5f; x += 0.1f)
                {
                    var position = new Vector3(x, 0f, z);
                    if (field.IsWalkable(position))
                    {
                        Assert.That(field.HasReachedExit(position), Is.False,
                            $"A wall-adjacent point at {position} was incorrectly treated as a leak.");
                    }
                }
            }
        }

        [Test]
        public void ContinuousBoundary_ProjectsEscapedBodyBackInside()
        {
            var route = new[] { new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f) };
            var field = new HordeFlowField(route, null, 2f, 0.5f);

            var recovered = field.ConstrainMove(
                new Vector3(0f, 0f, 2.35f),
                new Vector3(0.1f, 0f, 2.4f));

            Assert.That(field.IsWalkable(recovered), Is.True,
                "An agent pushed beyond the wall must be projected back into the corridor.");
        }

        [Test]
        public void DebugBoundary_CanShowVisibleRoadEdgeOutsideBodyCenterLimit()
        {
            var route = new[] { new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f) };
            var field = new HordeFlowField(route, null, 1.5f, 0.5f);
            var boundary = new List<HordeFlowField.BoundarySegment>();

            field.GetBoundarySegments(boundary, 0.5f);

            var upperEdge = float.NegativeInfinity;
            foreach (var segment in boundary)
            {
                var midpoint = (segment.From + segment.To) * 0.5f;
                if (Mathf.Abs(midpoint.x) < 5f)
                {
                    upperEdge = Mathf.Max(upperEdge, midpoint.z);
                }
            }

            Assert.That(upperEdge, Is.EqualTo(2f).Within(0.01f));
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
            var halfWidths = new[] { 2.507f, 2.507f, 2.507f, 2.007f, 0.507f, 0.507f, 2.007f, 4.507f, 4.507f, 4.007f, 3.007f, 2.507f };
            var field = new HordeFlowField(route, null, 2.157f, 0.4f, halfWidths);
            var position = field.GetSpawnPoint(1.8f);

            var boundary = new List<HordeFlowField.BoundarySegment>();
            field.GetBoundarySegments(boundary);
            Assert.That(boundary, Is.Not.Empty, "The real walkable boundary must be available for debugging.");

            Assert.That(field.IsWalkable(new Vector3(-10f, 0f, 14f)), Is.False,
                "The empty ground beyond the first sharp turn must not become a shortcut.");
            Assert.That(field.IsWalkable(new Vector3(8f, 0f, 14f)), Is.False,
                "The top gap between disconnected road sections must remain blocked.");

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

        [Test]
        public void OpenField_UsesFullBoardAndMovesTowardExitGate()
        {
            var field = HordeFlowField.CreateOpenField(
                Vector3.zero,
                new Vector2(48f, 28f),
                new Vector3(-20f, 0f, 0f),
                new Vector3(20f, 0f, 0f),
                10f,
                0.5f);
            var position = new Vector3(-18f, 0f, 11f);
            var previousDistance = field.GetDistanceToExit(position);

            Assert.That(field.IsWalkable(new Vector3(0f, 0f, 12f)), Is.True,
                "Open-field navigation must not inherit the old corridor boundary.");
            Assert.That(field.IsWalkable(new Vector3(25f, 0f, 0f)), Is.False);

            for (var step = 0; step < 600 && !field.HasReachedExit(position); step++)
            {
                position = field.ConstrainMove(position, position + field.GetDirection(position) * 0.12f);
                Assert.That(field.IsWalkable(position), Is.True);
                var distance = field.GetDistanceToExit(position);
                Assert.That(distance, Is.LessThanOrEqualTo(previousDistance + field.CellSize * 1.5f));
                previousDistance = distance;
            }

            Assert.That(field.HasReachedExit(position), Is.True);
        }

        [Test]
        public void OpenField_RasterizesStaticObstacleIntoNavigationBoundary()
        {
            var obstacles = new[] { new Bounds(Vector3.zero, new Vector3(6f, 4f, 8f)) };
            var field = HordeFlowField.CreateOpenField(
                Vector3.zero,
                new Vector2(48f, 28f),
                new Vector3(-20f, 0f, 0f),
                new Vector3(20f, 0f, 0f),
                10f,
                0.5f,
                obstacles);

            Assert.That(field.IsWalkable(Vector3.zero), Is.False);
            Assert.That(field.IsWalkable(new Vector3(0f, 0f, 8f)), Is.True);
        }
    }
}
