using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class GpuHordeStressBenchmarkTests
    {
        private static readonly int[] AgentCounts = { 1000, 5000, 10000, 25000, 50000, 100000 };

        [UnityTest, Explicit("Run explicitly for GPU capacity measurements.")]
        [Category("Performance")]
        public IEnumerator MeasureGpuHordeScalingFrom1KTo100K()
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True, "A compute-capable graphics device is required.");
            var route = BuildBenchmarkRoute();
            var field = new HordeFlowField(route, null, 2.31f, 0.62f);
            var spawnCells = CollectWalkableCells(field);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            var results = new List<BenchmarkResult>();
            var timings = new FrameTiming[1];

            foreach (var count in AgentCounts)
            {
                Assert.That(GpuHordeSimulation.TryCreate(count, field, mesh, out var simulation), Is.True);
                var states = BuildStates(count, spawnCells, field);
                var controls = new Vector4[count];
                var impulses = new Vector2[count];
                for (var i = 0; i < count; i++)
                {
                    controls[i] = new Vector4(4.8f, 1f, 0f, 0f);
                }

                simulation.SpawnBatch(states, count);
                var submitTicks = 0L;
                var gpuMsSum = 0d;
                var gpuSamples = 0;
                var synchronousGpuMs = 0d;
                var synchronousSamples = 0;
                var diagnosticSync = new uint[4];
                const int warmupFrames = 20;
                const int measuredFrames = 60;
                for (var frame = 0; frame < warmupFrames + measuredFrames; frame++)
                {
                    var start = Stopwatch.GetTimestamp();
                    simulation.Dispatch(1f / 60f, controls, impulses);
                    simulation.Draw(mesh, 0);
                    if (frame >= warmupFrames)
                    {
                        submitTicks += Stopwatch.GetTimestamp() - start;
                        if ((frame - warmupFrames) % 10 == 0)
                        {
                            var syncStart = Stopwatch.GetTimestamp();
                            simulation.SynchronizeDiagnostics(diagnosticSync);
                            synchronousGpuMs += (Stopwatch.GetTimestamp() - syncStart) * 1000d / Stopwatch.Frequency;
                            synchronousSamples++;
                        }
                    }

                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    if (frame >= warmupFrames && FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0d)
                    {
                        gpuMsSum += timings[0].gpuFrameTime;
                        gpuSamples++;
                    }
                }

                results.Add(new BenchmarkResult
                {
                    agents = count,
                    cpuSubmitMs = submitTicks * 1000d / Stopwatch.Frequency / measuredFrames,
                    gpuFrameMs = gpuSamples > 0
                        ? gpuMsSum / gpuSamples
                        : synchronousSamples > 0 ? synchronousGpuMs / synchronousSamples : 0d,
                    maxCellOccupancy = simulation.MaximumCellOccupancy,
                    overflowCells = simulation.OverflowCellCount,
                    droppedAgents = simulation.DroppedAgentCount
                });
                simulation.Dispose();
                yield return null;
            }

            var report = new BenchmarkReport
            {
                device = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                utcTimestamp = DateTime.UtcNow.ToString("O"),
                results = results.ToArray()
            };
            var outputPath = Path.Combine(Application.persistentDataPath, "gpu-horde-benchmark.json");
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            TestContext.Progress.WriteLine($"GPU horde benchmark: {outputPath}");
            foreach (var result in results)
            {
                TestContext.Progress.WriteLine(
                    $"{result.agents,6} agents | CPU submit {result.cpuSubmitMs:0.000} ms | GPU frame {result.gpuFrameMs:0.000} ms | " +
                    $"cell max {result.maxCellOccupancy} | overflow {result.overflowCells} | dropped {result.droppedAgents}");
            }

            Assert.That(results.Count, Is.EqualTo(AgentCounts.Length));
        }

        private static Vector3[] BuildBenchmarkRoute()
        {
            var points = new List<Vector3>();
            const int rows = 30;
            const float width = 200f;
            const float spacing = 5f;
            for (var row = 0; row < rows; row++)
            {
                var z = row * spacing;
                var fromLeft = (row & 1) == 0;
                points.Add(new Vector3(fromLeft ? -width * 0.5f : width * 0.5f, 0f, z));
                points.Add(new Vector3(fromLeft ? width * 0.5f : -width * 0.5f, 0f, z));
            }

            return points.ToArray();
        }

        private static List<Vector3> CollectWalkableCells(HordeFlowField field)
        {
            var cells = new List<Vector3>(field.Width * field.Height / 2);
            for (var y = 0; y < field.Height; y++)
            {
                for (var x = 0; x < field.Width; x++)
                {
                    var position = field.Origin + new Vector3((x + 0.5f) * field.CellSize, 0f, (y + 0.5f) * field.CellSize);
                    if (field.IsWalkable(position) && !field.HasReachedExit(position))
                    {
                        cells.Add(position);
                    }
                }
            }

            return cells;
        }

        private static GpuHordeSimulation.AgentState[] BuildStates(int count, IReadOnlyList<Vector3> cells, HordeFlowField field)
        {
            var states = new GpuHordeSimulation.AgentState[count];
            for (var i = 0; i < count; i++)
            {
                var basePosition = cells[i % cells.Count];
                var layer = i / cells.Count;
                var jitter = new Vector2(
                    Mathf.Repeat(layer * 0.173f, 1f) - 0.5f,
                    Mathf.Repeat(layer * 0.317f, 1f) - 0.5f) * field.CellSize * 0.45f;
                var position = basePosition + new Vector3(jitter.x, 0f, jitter.y);
                var direction = field.GetDirection(position);
                states[i] = new GpuHordeSimulation.AgentState
                {
                    Position = new Vector2(position.x, position.z),
                    Velocity = new Vector2(direction.x, direction.z) * 4.8f,
                    Scale = 0.34f,
                    Status = 1,
                    Health = 100f,
                    Progress = -field.GetDistanceToExit(position),
                    SlowMultiplier = 1f
                };
            }

            return states;
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string device;
            public string graphicsApi;
            public string utcTimestamp;
            public BenchmarkResult[] results;
        }

        [Serializable]
        private sealed class BenchmarkResult
        {
            public int agents;
            public double cpuSubmitMs;
            public double gpuFrameMs;
            public uint maxCellOccupancy;
            public uint overflowCells;
            public uint droppedAgents;
        }
    }
}
