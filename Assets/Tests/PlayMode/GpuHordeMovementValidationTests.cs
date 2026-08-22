using System.Collections;
using NUnit.Framework;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class GpuHordeMovementValidationTests
    {
        [Test]
        public void WideStressField_FirstWaypointHasForwardGpuFlow()
        {
            var route = CreateWideStressRoute();
            var field = new HordeFlowField(route, null, 26.16f, 0.62f);
            field.BuildGpuData(out var vectors, out var data);
            var sampled = 0;
            for (var z = 125f; z <= 175f; z += 2f)
            {
                for (var x = -175f; x <= -80f; x += 2f)
                {
                    var cellX = Mathf.FloorToInt((x - field.Origin.x) / field.CellSize);
                    var cellY = Mathf.FloorToInt((z - field.Origin.z) / field.CellSize);
                    if (cellX < 0 || cellY < 0 || cellX >= field.Width || cellY >= field.Height)
                    {
                        continue;
                    }

                    var index = cellY * field.Width + cellX;
                    if (data[index].y <= 0.001f)
                    {
                        continue;
                    }

                    sampled++;
                    var direction = new Vector2(vectors[index].x, vectors[index].y);
                    Assert.That(direction.sqrMagnitude, Is.GreaterThan(0.01f), $"Zero GPU flow at ({x}, {z}).");
                    Assert.That(direction.x, Is.GreaterThan(0.05f), $"GPU flow does not cross the first waypoint at ({x}, {z}).");
                }
            }

            Assert.That(sampled, Is.GreaterThan(500));
        }

        [Test]
        public void WideStressField_LongStraightUsesTheWholeCrossSection()
        {
            var route = CreateWideStressRoute();
            var field = new HordeFlowField(route, null, 26.16f, 0.62f);
            field.BuildGpuData(out var vectors, out var data);
            var sampled = 0;
            var worstForward = 1f;
            var worstSideways = 0f;
            for (var z = 126f; z <= 174f; z += 2f)
            {
                for (var x = -150f; x <= 90f; x += 4f)
                {
                    var cellX = Mathf.FloorToInt((x - field.Origin.x) / field.CellSize);
                    var cellY = Mathf.FloorToInt((z - field.Origin.z) / field.CellSize);
                    if (cellX < 0 || cellY < 0 || cellX >= field.Width || cellY >= field.Height)
                    {
                        continue;
                    }

                    var index = cellY * field.Width + cellX;
                    if (data[index].y <= 0.001f)
                    {
                        continue;
                    }

                    sampled++;
                    worstForward = Mathf.Min(worstForward, vectors[index].x);
                    worstSideways = Mathf.Max(worstSideways, Mathf.Abs(vectors[index].y));
                }
            }

            Assert.That(sampled, Is.GreaterThan(1000));
            Assert.That(worstForward, Is.GreaterThan(0.72f),
                $"The broad straight pulls part of the horde sideways (worst forward {worstForward:0.000}).");
            Assert.That(worstSideways, Is.LessThan(0.7f),
                $"The broad straight collapses toward one edge (worst sideways {worstSideways:0.000}).");
        }

        [Test]
        public void WideStressField_StreamlinesReachExitWithoutCollapsing()
        {
            var route = CreateWideStressRoute();
            var field = new HordeFlowField(route, null, 26.16f, 0.62f);
            const int streamCount = 17;
            var positions = new Vector3[streamCount];
            var reached = new bool[streamCount];
            for (var stream = 0; stream < streamCount; stream++)
            {
                positions[stream] = field.GetSpawnPoint(Mathf.Lerp(-24f, 24f, stream / (streamCount - 1f)));
            }

            for (var step = 0; step < 12000; step++)
            {
                var allReached = true;
                for (var stream = 0; stream < streamCount; stream++)
                {
                    if (reached[stream])
                    {
                        continue;
                    }

                    allReached = false;
                    positions[stream] = field.ConstrainMove(
                        positions[stream],
                        positions[stream] + field.GetDirection(positions[stream]) * 0.22f);
                    reached[stream] = field.HasReachedExit(positions[stream]);
                }

                if (allReached)
                {
                    break;
                }
            }

            var exitForward = (route[^1] - route[^2]).normalized;
            var exitSide = Vector3.Cross(Vector3.up, exitForward);
            var minLateral = float.PositiveInfinity;
            var maxLateral = float.NegativeInfinity;
            for (var stream = 0; stream < streamCount; stream++)
            {
                Assert.That(reached[stream], Is.True, $"Wide-flow streamline {stream} did not reach the exit.");
                var lateral = Vector3.Dot(positions[stream] - route[^1], exitSide);
                minLateral = Mathf.Min(minLateral, lateral);
                maxLateral = Mathf.Max(maxLateral, lateral);
            }

            Assert.That(maxLateral - minLateral, Is.GreaterThan(35f),
                $"The 48 meter entrance cross-section collapsed to {maxLateral - minLateral:0.0} meters.");
        }

        private static Vector3[] CreateWideStressRoute()
        {
            return new[]
            {
                new Vector3(-180f, 0f, 150f),
                new Vector3(-110f, 0f, 150f),
                new Vector3(0f, 0f, 145f),
                new Vector3(110f, 0f, 150f),
                new Vector3(180f, 0f, 125f),
                new Vector3(180f, 0f, 78f),
                new Vector3(110f, 0f, 50f),
                new Vector3(0f, 0f, 55f),
                new Vector3(-110f, 0f, 50f),
                new Vector3(-180f, 0f, 25f),
                new Vector3(-180f, 0f, -28f),
                new Vector3(-110f, 0f, -50f),
                new Vector3(0f, 0f, -45f),
                new Vector3(110f, 0f, -50f),
                new Vector3(180f, 0f, -78f),
                new Vector3(180f, 0f, -128f),
                new Vector3(110f, 0f, -150f),
                new Vector3(0f, 0f, -145f),
                new Vector3(-110f, 0f, -150f),
                new Vector3(-180f, 0f, -150f)
            };
        }

        [UnityTest]
        public IEnumerator MultiTurnFlow_StaysWalkableReachesExitAndKeepsWidth()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var route = new[]
            {
                new Vector3(-18f, 0f, -8f),
                new Vector3(-5f, 0f, -8f),
                new Vector3(2f, 0f, -2f),
                new Vector3(13f, 0f, -1f),
                new Vector3(18f, 0f, 8f),
                new Vector3(9f, 0f, 17f)
            };
            var field = new HordeFlowField(route, null, 2.31f, 0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            const int columns = 21;
            const int rows = 10;
            const int count = columns * rows;
            Assert.That(GpuHordeSimulation.TryCreate(count, field, mesh, out var simulation), Is.True);
            var controls = new Vector4[count];
            var impulses = new Vector2[count];
            var states = new GpuHordeSimulation.AgentState[count];
            var startForward = (route[1] - route[0]).normalized;
            var startSide = Vector3.Cross(Vector3.up, startForward);
            for (var i = 0; i < count; i++)
            {
                var lateral = Mathf.Lerp(-2f, 2f, (i % columns) / (columns - 1f));
                var position = route[0] + startForward * (0.15f + i / columns * 0.48f) + startSide * lateral;
                states[i] = new GpuHordeSimulation.AgentState
                {
                    Position = new Vector2(position.x, position.z),
                    Velocity = new Vector2(startForward.x, startForward.z) * 4.8f,
                    Scale = 0.34f,
                    Status = 1,
                    Health = 100f,
                    Progress = -field.GetDistanceToExit(position),
                    SlowMultiplier = 1f
                };
                controls[i] = new Vector4(4.8f, 1f, 0f, 0f);
            }

            simulation.SpawnBatch(states, count);
            for (var frame = 0; frame < 1100; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, impulses);
                if (frame % 30 == 0)
                {
                    simulation.ReadStatesSynchronous(states, count);
                    for (var i = 0; i < count; i++)
                    {
                        if (states[i].Status != 1)
                        {
                            continue;
                        }

                        var position = new Vector3(states[i].Position.x, 0f, states[i].Position.y);
                        Assert.That(field.IsWalkable(position), Is.True, $"Agent {i} left the walkable field at frame {frame}.");
                    }
                }

                if (frame % 10 == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            var exitForward = (route[^1] - route[^2]).normalized;
            var exitSide = Vector3.Cross(Vector3.up, exitForward);
            var minLateral = float.PositiveInfinity;
            var maxLateral = float.NegativeInfinity;
            for (var i = 0; i < count; i++)
            {
                Assert.That(states[i].Status, Is.EqualTo(2), $"Agent {i} did not reach the true exit.");
                var position = new Vector3(states[i].Position.x, 0f, states[i].Position.y);
                var lateral = Vector3.Dot(position - route[^1], exitSide);
                minLateral = Mathf.Min(minLateral, lateral);
                maxLateral = Mathf.Max(maxLateral, lateral);
            }

            simulation.Dispose();
            Assert.That(maxLateral - minLateral, Is.GreaterThan(3f), "The GPU flow collapsed into a narrow exit line.");
        }

        [UnityTest]
        public IEnumerator LevelTwoVariableWidthRoute_DoesNotTeleportOffRoad()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var route = new[]
            {
                new Vector3(-56f, 0f, 14f), new Vector3(-34f, 0f, 14f),
                new Vector3(-24f, 0f, 14f), new Vector3(-24f, 0f, -5f),
                new Vector3(-24f, 0f, -17f), new Vector3(2f, 0f, -17f),
                new Vector3(10f, 0f, -17f), new Vector3(18f, 0f, -17f),
                new Vector3(36f, 0f, -17f), new Vector3(42f, 0f, -11f),
                new Vector3(42f, 0f, 10f), new Vector3(56f, 0f, 10f)
            };
            var halfWidths = new[] { 2.66f, 2.66f, 2.66f, 2.16f, 1.16f, 1.16f, 2.16f, 4.66f, 4.66f, 4.16f, 3.16f, 2.66f };
            var field = new HordeFlowField(route, null, 2.31f, 0.62f, halfWidths);
            const int count = 63;
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            var impulses = new Vector2[count];
            var forward = (route[1] - route[0]).normalized;
            var side = Vector3.Cross(Vector3.up, forward);
            for (var i = 0; i < count; i++)
            {
                var position = route[0] + forward * ((i / 9) * 0.6f) + side * Mathf.Lerp(-2f, 2f, (i % 9) / 8f);
                states[i] = new GpuHordeSimulation.AgentState
                {
                    Position = new Vector2(position.x, position.z),
                    Velocity = new Vector2(forward.x, forward.z) * 4.8f,
                    Scale = 0.34f,
                    Status = 1,
                    Health = 100f,
                    MaxHealth = 100f,
                    SlowMultiplier = 1f,
                    Mass = 1f
                };
                controls[i] = new Vector4(4.8f, 1f, 0f, 0f);
            }

            Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            simulation.SpawnBatch(states, count);
            for (var frame = 0; frame < 2600; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, impulses, requestReadback: false);
                if (frame % 20 == 0)
                {
                    simulation.ReadStatesSynchronous(states, count);
                    for (var i = 0; i < count; i++)
                    {
                        if (states[i].Status != 1)
                        {
                            continue;
                        }

                        var position = new Vector3(states[i].Position.x, 0f, states[i].Position.y);
                        Assert.That(field.IsWalkable(position), Is.True,
                            $"GPU agent {i} teleported outside Level 2 at frame {frame}: {position}.");
                    }
                }

                if (frame % 20 == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            for (var i = 0; i < count; i++)
            {
                Assert.That(states[i].Status, Is.EqualTo(2), $"GPU agent {i} never reached the Level 2 exit.");
            }
            simulation.Dispose();
        }
    }
}
