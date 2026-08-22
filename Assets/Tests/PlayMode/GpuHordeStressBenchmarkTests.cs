using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private const int DenseAgentsPerCell = 24;

        [UnityTest, Explicit("Run explicitly for GPU capacity measurements.")]
        [Category("Performance")]
        public IEnumerator MeasureGpuHordeScalingFrom1KTo100K()
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True, "A compute-capable graphics device is required.");
            var field = new HordeFlowField(BuildBenchmarkRoute(), null, 2.31f, 0.62f);
            var spawnCells = CollectWalkableCells(field);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            var results = new List<BenchmarkResult>();

            foreach (var count in AgentCounts)
            {
                Assert.That(GpuHordeSimulation.TryCreate(count, field, mesh, out var simulation), Is.True);
                var states = BuildStates(count, spawnCells, field);
                var controls = Enumerable.Repeat(new Vector4(4.8f, 1f, 0f, 0f), count).ToArray();
                var impulses = new Vector2[count];
                simulation.SpawnBatch(states, count);
                simulation.Dispatch(1f / 60f, controls, impulses);
                yield return null;

                for (var frame = 0; frame < 20; frame++)
                {
                    simulation.Dispatch(1f / 60f, null, null);
                    simulation.Draw(mesh, 0);
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                }

                var sim = new Samples();
                for (var frame = 0; frame < 40; frame++)
                {
                    MeasureSubmit(sim, () => simulation.Dispatch(1f / 60f, null, null));
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    CaptureFrame(sim);
                }

                var rendered = new Samples();
                for (var frame = 0; frame < 60; frame++)
                {
                    var allocated = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    simulation.Dispatch(1f / 60f, null, null);
                    var dispatchMs = ElapsedMs(start);
                    start = Stopwatch.GetTimestamp();
                    simulation.Draw(mesh, 0);
                    var drawMs = ElapsedMs(start);
                    rendered.dispatchMs.Add(dispatchMs);
                    rendered.drawMs.Add(drawMs);
                    rendered.submitMs.Add(dispatchMs + drawMs);
                    rendered.allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    CaptureFrame(rendered);
                }

                var uploads = new List<double>();
                for (var frame = 0; frame < 20; frame++)
                {
                    var start = Stopwatch.GetTimestamp();
                    simulation.Dispatch(1f / 60f, controls, impulses);
                    uploads.Add(ElapsedMs(start));
                    yield return null;
                }

                var projectileBatchSubmitMs = new List<double>();
                var areaBatchSubmitMs = new List<double>();
                var commandPosition = spawnCells[0];
                for (var sample = 0; sample < 8; sample++)
                {
                    for (var command = 0; command < 256; command++)
                    {
                        Assert.That(simulation.QueueProjectile(
                            commandPosition, commandPosition + Vector3.right * 0.05f,
                            0.05f, 0.01f, 0f, 1, true, false), Is.True);
                    }
                    var start = Stopwatch.GetTimestamp();
                    simulation.Dispatch(1f / 60f, null, null, false);
                    projectileBatchSubmitMs.Add(ElapsedMs(start));
                    yield return null;
                }
                for (var sample = 0; sample < 8; sample++)
                {
                    for (var command = 0; command < 256; command++)
                    {
                        Assert.That(simulation.QueueAreaEffect(
                            commandPosition, 0.05f, 0.01f, 0f, 1, 0f, 0f, 1), Is.True);
                    }
                    var start = Stopwatch.GetTimestamp();
                    simulation.Dispatch(1f / 60f, null, null, false);
                    areaBatchSubmitMs.Add(ElapsedMs(start));
                    yield return null;
                }

                var diagnostics = new uint[4];
                simulation.SynchronizeDiagnostics(diagnostics);
                var stride = Marshal.SizeOf<GpuHordeSimulation.AgentState>();
                results.Add(new BenchmarkResult
                {
                    agents = count,
                    scenario = $"dense-{DenseAgentsPerCell}-per-cell",
                    stateStrideBytes = stride,
                    pingPongStateMb = count * stride * 2d / 1048576d,
                    cpuDispatchMs = Average(rendered.dispatchMs),
                    cpuDrawSubmitMs = Average(rendered.drawMs),
                    cpuSubmitP95Ms = Percentile(rendered.submitMs, 0.95),
                    cpuSubmitMaxMs = Max(rendered.submitMs),
                    cpuFullUploadSubmitMs = Average(uploads),
                    cpu256ProjectileSubmitMs = Percentile(projectileBatchSubmitMs, 0.5),
                    cpu256AreaEffectSubmitMs = Percentile(areaBatchSubmitMs, 0.5),
                    gcBytesPerFrame = Average(rendered.allocations),
                    gpuSimulationFrameMs = Average(sim.gpuMs),
                    gpuTotalFrameMs = Average(rendered.gpuMs),
                    gpuTotalP95Ms = Percentile(rendered.gpuMs, 0.95),
                    gpuTotalMaxMs = Max(rendered.gpuMs),
                    gpuFrameStdDevMs = StdDev(rendered.gpuMs),
                    cpuFrameMs = Average(rendered.cpuFrameMs),
                    cpuFrameP95Ms = Percentile(rendered.cpuFrameMs, 0.95),
                    maxCellOccupancy = diagnostics[2],
                    overflowCells = diagnostics[0],
                    droppedAgents = diagnostics[1]
                });
                simulation.Dispose();
                yield return null;
            }

            var report = new BenchmarkReport
            {
                device = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                unityVersion = Application.unityVersion,
                utcTimestamp = DateTime.UtcNow.ToString("O"),
                results = results.ToArray()
            };
            var output = Path.Combine(Application.persistentDataPath, "gpu-horde-detailed-benchmark.json");
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            TestContext.Progress.WriteLine($"GPU horde benchmark: {output}");
            foreach (var result in results)
            {
                TestContext.Progress.WriteLine(
                    $"{result.agents,6} | dispatch {result.cpuDispatchMs:0.000} ms | draw {result.cpuDrawSubmitMs:0.000} ms | " +
                    $"upload {result.cpuFullUploadSubmitMs:0.000} ms | GPU sim {result.gpuSimulationFrameMs:0.000} ms | " +
                    $"256 projectiles {result.cpu256ProjectileSubmitMs:0.000} ms | 256 areas {result.cpu256AreaEffectSubmitMs:0.000} ms | " +
                    $"GPU total {result.gpuTotalFrameMs:0.000} ms p95 {result.gpuTotalP95Ms:0.000} | " +
                    $"cell {result.maxCellOccupancy} overflow {result.overflowCells} dropped {result.droppedAgents}");
            }
        }

        private static void MeasureSubmit(Samples samples, Action action)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            action();
            samples.submitMs.Add(ElapsedMs(start));
            samples.allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
        }

        private static void CaptureFrame(Samples samples)
        {
            var timing = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, timing) == 0) return;
            if (timing[0].gpuFrameTime > 0) samples.gpuMs.Add(timing[0].gpuFrameTime);
            if (timing[0].cpuFrameTime > 0) samples.cpuFrameMs.Add(timing[0].cpuFrameTime);
        }

        private static double ElapsedMs(long start) =>
            (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;

        private static double Average(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Average();
        private static double Average(IReadOnlyList<long> values) => values.Count == 0 ? 0 : values.Average(x => (double)x);
        private static double Max(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Max();

        private static double Percentile(IReadOnlyList<double> values, double percentile)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(x => x).ToArray();
            return sorted[Mathf.Clamp(Mathf.CeilToInt((float)(percentile * sorted.Length)) - 1, 0, sorted.Length - 1)];
        }

        private static double StdDev(IReadOnlyList<double> values)
        {
            if (values.Count < 2) return 0;
            var average = Average(values);
            return Math.Sqrt(values.Sum(x => (x - average) * (x - average)) / values.Count);
        }

        private static Vector3[] BuildBenchmarkRoute()
        {
            var points = new List<Vector3>();
            for (var row = 0; row < 30; row++)
            {
                const float halfWidth = 100f;
                var z = row * 5f;
                var left = (row & 1) == 0;
                points.Add(new Vector3(left ? -halfWidth : halfWidth, 0, z));
                points.Add(new Vector3(left ? halfWidth : -halfWidth, 0, z));
            }
            return points.ToArray();
        }

        private static List<Vector3> CollectWalkableCells(HordeFlowField field)
        {
            var cells = new List<Vector3>();
            for (var y = 0; y < field.Height; y++)
            for (var x = 0; x < field.Width; x++)
            {
                var position = field.Origin + new Vector3((x + 0.5f) * field.CellSize, 0, (y + 0.5f) * field.CellSize);
                if (field.IsWalkable(position) && !field.HasReachedExit(position)) cells.Add(position);
            }
            cells.Sort((left, right) => field.GetDistanceToExit(right).CompareTo(field.GetDistanceToExit(left)));
            return cells;
        }

        private static GpuHordeSimulation.AgentState[] BuildStates(
            int count, IReadOnlyList<Vector3> cells, HordeFlowField field)
        {
            var states = new GpuHordeSimulation.AgentState[count];
            var usedCells = Mathf.Min(cells.Count, Mathf.CeilToInt(count / (float)DenseAgentsPerCell));
            for (var i = 0; i < count; i++)
            {
                var basePosition = cells[i % usedCells];
                var layer = i / usedCells;
                var angle = layer * 2.39996323f;
                var radius = Mathf.Sqrt((layer + 0.5f) / DenseAgentsPerCell) * field.CellSize * 0.42f;
                var jitter = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                var position = basePosition + new Vector3(jitter.x, 0, jitter.y);
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

        private sealed class Samples
        {
            public readonly List<double> submitMs = new List<double>();
            public readonly List<double> dispatchMs = new List<double>();
            public readonly List<double> drawMs = new List<double>();
            public readonly List<double> gpuMs = new List<double>();
            public readonly List<double> cpuFrameMs = new List<double>();
            public readonly List<long> allocations = new List<long>();
        }

        [Serializable]
        private sealed class BenchmarkReport
        {
            public string device;
            public string graphicsApi;
            public string unityVersion;
            public string utcTimestamp;
            public BenchmarkResult[] results;
        }

        [Serializable]
        private sealed class BenchmarkResult
        {
            public int agents;
            public string scenario;
            public int stateStrideBytes;
            public double pingPongStateMb;
            public double cpuDispatchMs;
            public double cpuDrawSubmitMs;
            public double cpuSubmitP95Ms;
            public double cpuSubmitMaxMs;
            public double cpuFullUploadSubmitMs;
            public double cpu256ProjectileSubmitMs;
            public double cpu256AreaEffectSubmitMs;
            public double gcBytesPerFrame;
            public double gpuSimulationFrameMs;
            public double gpuTotalFrameMs;
            public double gpuTotalP95Ms;
            public double gpuTotalMaxMs;
            public double gpuFrameStdDevMs;
            public double cpuFrameMs;
            public double cpuFrameP95Ms;
            public uint maxCellOccupancy;
            public uint overflowCells;
            public uint droppedAgents;
        }
    }
}
