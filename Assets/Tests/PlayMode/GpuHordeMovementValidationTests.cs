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
    }
}
