using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace TowerDefense.Tests
{
    public sealed class CrowdMergeValidationTests
    {
        [UnityTest, Explicit("Checks GPU timestamp support for acceptance profiling")]
        public IEnumerator HardwareFrameTiming_Probe()
        {
            var samples = new FrameTiming[1];
            var sampler = CustomSampler.Create("CrowdGpuProbe", true);
            var recorder = sampler.GetRecorder();
            recorder.enabled = true;
            var command = new CommandBuffer();
            var renderTarget = new RenderTexture(1920,1080,24);
            var camera = Camera.main;
            var previousTarget = camera.targetTexture;
            camera.targetTexture = renderTarget;
            var markerGpu = new List<float>();
            var cpu = new List<float>();
            var gpu = new List<float>();
            ulong last = 0;
            for (var i = 0; i < 180; i++)
            {
                command.Clear(); command.BeginSample(sampler); Graphics.ExecuteCommandBuffer(command);
                camera.Render();
                command.Clear(); command.EndSample(sampler); Graphics.ExecuteCommandBuffer(command);
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                if (recorder.gpuSampleBlockCount > 0) markerGpu.Add(recorder.gpuElapsedNanoseconds / 1000000f);
                if (FrameTimingManager.GetLatestTimings(1, samples) == 0 || samples[0].frameStartTimestamp == last) continue;
                last = samples[0].frameStartTimestamp;
                if (samples[0].cpuFrameTime > 0) cpu.Add((float)samples[0].cpuFrameTime);
                if (samples[0].gpuFrameTime > 0) gpu.Add((float)samples[0].gpuFrameTime);
            }
            Directory.CreateDirectory("Logs/Reports/MergeValidation");
            File.WriteAllText("Logs/Reports/MergeValidation/timing-probe.txt", $"Enabled={FrameTimingManager.IsFeatureEnabled()} CPU {Timing(cpu)} GPU {Timing(gpu)} MarkerGPU {Timing(markerGpu)}");
            camera.targetTexture = previousTarget;
            renderTarget.Release(); UnityEngine.Object.Destroy(renderTarget);
            command.Dispose(); recorder.enabled = false;
            Assert.That(gpu.Count + markerGpu.Count, Is.GreaterThan(0), "Hardware GPU timing must be available before the final acceptance run.");
        }

        [UnityTest, Timeout(480000), Explicit("One full real-wave validation, including the merge and the 1800-body tail")]
        public IEnumerator ActualTenThousand_ThroughMergeAndTail()
        {
            var preference = PlayerPrefs.GetInt(HordePreviewSettings.PreferenceKey, 1);
            var timeScale = Time.timeScale;
            var vsync = QualitySettings.vSyncCount;
            var frameRate = Application.targetFrameRate;
            var random = UnityEngine.Random.state;
            HordeEnemyManager horde = null;
            GameObject timerObject = null;
            CrowdGpuFrameTimer gpuTimer = null;
            RenderTexture target = null;
            try
            {
                PlayerPrefs.SetInt(HordePreviewSettings.PreferenceKey, 1);
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Time.timeScale = 1;
                UnityEngine.Random.InitState(761905);
                yield return SceneManager.LoadSceneAsync("Main");
                var level = SampleContent.Create().Levels[1];
                var bootstrap = UnityEngine.Object.FindAnyObjectByType<TowerDefenseBootstrap>();
                var route = (PathRoute)typeof(TowerDefenseBootstrap).GetMethod("LoadLevelMap", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, new object[] { level });
                horde = UnityEngine.Object.FindAnyObjectByType<HordeEnemyManager>();
                UnityEngine.Object.FindAnyObjectByType<TopDownCameraController>().enabled = false;
                var camera = Camera.main;
                camera.transform.rotation = Quaternion.Euler(75, 0, 0);
                camera.transform.position = new Vector3(43, 22, -6);
                target = new RenderTexture(1920, 1080, 24);
                camera.targetTexture = target;
                timerObject = new GameObject("Crowd GPU frame timer");
                gpuTimer = timerObject.AddComponent<CrowdGpuFrameTimer>();
                horde.BeginWave(level.wave, route);
                var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager).GetField("gpuSimulation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                var flowField = (HordeFlowField)typeof(HordeEnemyManager).GetField(
                    "flowField",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                var collisionDiameter = (float)typeof(HordeEnemyManager).GetField(
                    "activeSolverCollisionDiameter",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                var states = new GpuHordeSimulation.AgentState[10000];
                var previous = new GpuHordeSimulation.AgentState[10000];
                var controlsField = typeof(HordeEnemyManager).GetField(
                    "gpuControls", BindingFlags.Instance | BindingFlags.NonPublic);
                var persistentPreroll = new Queue<AuditSnapshot>(5);
                var frames = new List<float>();
                var allFrames = new List<float>();
                var cpuFrames = new List<float>();
                var gpuFrames = new List<float>();
                var closeFrames = new List<float>();
                var overviewFrames = new List<float>();
                var timing = new FrameTiming[1];
                ulong lastTiming = 0;
                var frameLog = new System.Text.StringBuilder("frame,wallMs,cpuMs,gpuScopeMs,gpuScopeFrame,ticks,close,observationPreviousFrame\n");
                var catchupFrames = 0;
                var largestTickCount = 0;
                var closeView = false;
                var previousView = false;
                var spawningFrames = new List<float>();
                var settledFrames = new List<float>();
                var tailFrames = new List<float>();
                var diagnostics = new uint[GpuHordeSimulation.DiagnosticsCount];
                var report = new System.Text.StringBuilder(
                    "elapsed,spawned,alive,pairs,depth,persistentBodies,deepDiskContacts,deepBlockerContacts,deepWallContacts,actualSpeed,forwardSpeed,spawnCpuMs\n");
                var persistentReport = new System.Text.StringBuilder();
                var persistentSnapshotCaptured = false;
                var begin = Time.realtimeSinceStartupAsDouble;
                var sampleTime = begin;
                var previousTime = begin;
                var maxPairs = 0u;
                var maxDropped = 0u;
                var maxOccupancy = 0u;
                var maxGridFailures = 0u;
                var maxPersistentDeepBodies = 0u;
                var maxDeepDiskContacts = 0u;
                var maxDeepBlockerContacts = 0u;
                var maxDeepWallContacts = 0u;
                var walkabilityViolations = 0;
                var maxCpuPairs = 0;
                var mergeSamples = 0;
                var tailSamples = 0;
                var forwardSum = 0f;
                var skipFrame = false;
                var capture = 0;
                var stagnantSamples = 0;
                var nextCheckpoint = begin + 10;
                Directory.CreateDirectory("Logs/Reports/MergeValidation");
                // A passing run must not leave a previous candidate's failure
                // capture looking current. The successful summary/result XML
                // are retained; these files are created again only if this run
                // observes a persistent deep contact.
                File.Delete("Logs/Reports/MergeValidation/persistent-deep-pairs.txt");
                File.Delete("Logs/Reports/MergeValidation/persistent-snapshot.json");
                for (var snapshotIndex = 0; snapshotIndex < 5; snapshotIndex++)
                {
                    File.Delete($"Logs/Reports/MergeValidation/persistent-preroll-{snapshotIndex}.json");
                }
                while (Time.realtimeSinceStartupAsDouble - begin < 420)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    previousView = closeView;
                    closeView = ((int)((Time.realtimeSinceStartupAsDouble - begin) / 5) & 1) != 0;
                    camera.transform.position = closeView ? new Vector3(43, 3.5f, -2.5f) : new Vector3(43, 22, -6);
                    camera.transform.LookAt(new Vector3(43, 0.2f, 0));
                    if (previousView != closeView) simulation.RefreshVisibilityForAudit();
                    simulation.Draw(EnemyManager.GetLowEnemyMesh(), 0);
                    camera.Render();
                    gpuTimer.End();
                    var now = Time.realtimeSinceStartupAsDouble;
                    float cpuMs = 0, gpuMs = gpuTimer.Milliseconds;
                    if (gpuMs > 0) gpuFrames.Add(gpuMs);
                    if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].frameStartTimestamp != lastTiming)
                    {
                        lastTiming = timing[0].frameStartTimestamp;
                        cpuMs = (float)timing[0].cpuFrameTime;
                        if (cpuMs > 0) cpuFrames.Add(cpuMs);
                    }
                    if (horde.TotalSpawned > 500)
                    {
                        allFrames.Add(Time.unscaledDeltaTime * 1000);
                        (previousView ? closeFrames : overviewFrames).Add(Time.unscaledDeltaTime * 1000);
                        largestTickCount = Math.Max(largestTickCount, horde.SimulationTicksLastFrame);
                        if (horde.SimulationTicksLastFrame > 1) catchupFrames++;
                        frameLog.AppendLine(FormattableString.Invariant($"{Time.frameCount},{Time.unscaledDeltaTime*1000:F4},{cpuMs:F4},{gpuMs:F4},{Time.frameCount-3},{horde.SimulationTicksLastFrame},{previousView},{skipFrame}"));
                    }
                    if (!skipFrame && horde.TotalSpawned > 500)
                    {
                        frames.Add(Time.unscaledDeltaTime * 1000);
                        if (horde.TotalSpawned < 10000) spawningFrames.Add(Time.unscaledDeltaTime * 1000);
                        else settledFrames.Add(Time.unscaledDeltaTime * 1000);
                        if (horde.TotalSpawned == 10000 && horde.ActiveCount < 2500) tailFrames.Add(Time.unscaledDeltaTime * 1000);
                    }
                    skipFrame = false;
                    if (now < sampleTime) continue;
                    sampleTime = now + 0.5;
                    if (now >= nextCheckpoint)
                    {
                        nextCheckpoint = now + 10;
                        File.WriteAllText("Logs/Reports/MergeValidation/frames.csv", frameLog.ToString());
                        File.WriteAllText("Logs/Reports/MergeValidation/timing-checkpoint.txt", $"elapsed={now-begin:F1}s\nAll {Timing(allFrames)}\nCPU {Timing(cpuFrames)}\nGPU scope {Timing(gpuFrames)}\nClose {Timing(closeFrames)}\nOverview {Timing(overviewFrames)}");
                    }
                    simulation.SynchronizeDiagnostics(diagnostics);
                    simulation.ReadStatesSynchronous(states, horde.TotalSpawned);
                    var sampledControls = (Vector4[])controlsField.GetValue(horde);
                    persistentPreroll.Enqueue(new AuditSnapshot
                    {
                        States = (GpuHordeSimulation.AgentState[])states.Clone(),
                        Controls = (Vector4[])sampledControls.Clone()
                    });
                    while (persistentPreroll.Count > 5) persistentPreroll.Dequeue();
                    var speed = 0f;
                    var forward = 0f;
                    var count = 0;
                    var atMerge = 0;
                    for (var i = 0; i < horde.TotalSpawned; i++)
                    {
                        if (states[i].Status != 1 || previous[i].Status != 1) continue;
                        var delta = states[i].Position - previous[i].Position;
                        speed += delta.magnitude / (float)(now - previousTime);
                        forward += delta.x / (float)(now - previousTime);
                        count++;
                        if (!flowField.IsWalkable(new Vector3(states[i].Position.x, 0f, states[i].Position.y)))
                        {
                            walkabilityViolations++;
                        }
                        if (states[i].Position.x > 35 && states[i].Position.x < 48) atMerge++;
                    }
                    speed /= Mathf.Max(1, count);
                    forward /= Mathf.Max(1, count);
                    // Compare occupied physical torso area, excluding sprite padding.
                    var occupiedMergeArea = atMerge * Mathf.Pow(
                        CrowdSolverProfile.Default.bodyRadiusPerVisualScale / 0.5f, 2);
                    if (occupiedMergeArea > 1000)
                    {
                        mergeSamples++;
                        forwardSum += forward;
                    }
                    if (horde.TotalSpawned == 10000 && horde.ActiveCount < 2500) tailSamples++;
                    maxPairs = Math.Max(maxPairs, diagnostics[13]);
                    maxDropped = Math.Max(maxDropped, diagnostics[1]);
                    maxOccupancy = Math.Max(maxOccupancy, diagnostics[2]);
                    maxGridFailures = Math.Max(maxGridFailures, diagnostics[14]);
                    maxPersistentDeepBodies = Math.Max(maxPersistentDeepBodies, diagnostics[15]);
                    maxDeepDiskContacts = Math.Max(maxDeepDiskContacts, diagnostics[16]);
                    maxDeepBlockerContacts = Math.Max(maxDeepBlockerContacts, diagnostics[17]);
                    maxDeepWallContacts = Math.Max(maxDeepWallContacts, diagnostics[18]);
                    if (diagnostics[15] > 0u)
                    {
                        persistentReport.AppendLine(FormattableString.Invariant(
                            $"elapsed={now - begin:F2}, persistentBodies={diagnostics[15]}, deepDiskContacts={diagnostics[16]}"));
                        persistentReport.Append(DescribeDeepDiskPairs(states, collisionDiameter, 0.05f,
                            CrowdSolverProfile.Default.bodyRadiusPerVisualScale));
                        File.WriteAllText("Logs/Reports/MergeValidation/persistent-deep-pairs.txt", persistentReport.ToString());
                        if (!persistentSnapshotCaptured)
                        {
                            persistentSnapshotCaptured = true;
                            var preroll = persistentPreroll.ToArray();
                            for (var snapshotIndex = 0; snapshotIndex < preroll.Length; snapshotIndex++)
                            {
                                File.WriteAllText(
                                    $"Logs/Reports/MergeValidation/persistent-preroll-{snapshotIndex}.json",
                                    JsonUtility.ToJson(preroll[snapshotIndex]));
                            }
                            var persistentSnapshot = preroll[preroll.Length - 1];
                            File.WriteAllText("Logs/Reports/MergeValidation/persistent-snapshot.json",
                                JsonUtility.ToJson(persistentSnapshot));
                        }
                    }
                    stagnantSamples = horde.TotalSpawned == 10000 && count > 1000 && speed < 0.05f
                        ? stagnantSamples + 1 : 0;
                    report.AppendLine(FormattableString.Invariant(
                        $"{now-begin:F2},{horde.TotalSpawned},{horde.ActiveCount},{diagnostics[5]},{diagnostics[6]/1000000f:F6},{diagnostics[15]},{diagnostics[16]},{diagnostics[17]},{diagnostics[18]},{speed:F3},{forward:F3},{horde.Performance.SpawnMs:F3}"));
                    File.WriteAllText("Logs/Reports/MergeValidation/actual.csv", report.ToString());
                    if ((capture == 0 && occupiedMergeArea > 1500) || (capture == 1 && horde.TotalSpawned == 10000) || (capture == 2 && tailSamples > 0))
                    {
                        maxCpuPairs = Math.Max(maxCpuPairs, Pairs(states, collisionDiameter,
                            CrowdSolverProfile.Default.bodyRadiusPerVisualScale).pairs);
                        CaptureViews(simulation, states, $"actual-{capture}");
                        if (capture == 1)
                        {
                            var snapshot = new AuditSnapshot { States = states, Controls = (Vector4[])typeof(HordeEnemyManager).GetField("gpuControls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde) };
                            File.WriteAllText("Logs/Reports/MergeValidation/peak-snapshot.json", JsonUtility.ToJson(snapshot));
                        }
                        capture++;
                    }
                    Array.Copy(states, previous, states.Length);
                    previousTime = now;
                    skipFrame = true; // synchronous observation is reported separately from rendered throughput
                    if (horde.TotalSpawned == 10000 && horde.ActiveCount < 1700) break;
                    if (stagnantSamples >= 10) break; // fail fast on a frozen but separated horde
                }
                File.WriteAllText("Logs/Reports/MergeValidation/actual.csv", report.ToString());
                File.WriteAllText("Logs/Reports/MergeValidation/frames.csv", frameLog.ToString());
                var summary = $"1920x1080, camera.Render on every measured frame\nspawned={horde.TotalSpawned}, remaining={horde.ActiveCount}, collisionDiameter={collisionDiameter:F3}, mergeSamples={mergeSamples}, tailSamples={tailSamples}, maxDeepPairs={maxPairs}, persistentDeepBodies={maxPersistentDeepBodies}, deepDiskContacts={maxDeepDiskContacts}, deepBlockerContacts={maxDeepBlockerContacts}, deepWallContacts={maxDeepWallContacts}, CPU={maxCpuPairs}, forward={forwardSum/Mathf.Max(1,mergeSamples):F3}m/s, occupancy={maxOccupancy}, dropped={maxDropped}, gridFailures={maxGridFailures}, walkabilityViolations={walkabilityViolations}\nAll including observation/capture stalls: {Timing(allFrames)}\nCPU hardware frame: {Timing(cpuFrames)}\nGPU timestamp scope (simulation + camera, 3-frame delay): {Timing(gpuFrames)}\nClose: {Timing(closeFrames)}\nOverview: {Timing(overviewFrames)}\nCatchup frames={catchupFrames}, maximum ticks/frame={largestTickCount}\nWithout observation/capture stalls: {Timing(frames)}\nSpawning: {Timing(spawningFrames)}\nAfter spawning: {Timing(settledFrames)}\nTail: {Timing(tailFrames)}\n";
                File.WriteAllText("Logs/Reports/MergeValidation/actual.txt", summary);
                var finalSnapshot = new AuditSnapshot { States = states, Controls = (Vector4[])typeof(HordeEnemyManager).GetField("gpuControls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde) };
                File.WriteAllText("Logs/Reports/MergeValidation/final-snapshot.json", JsonUtility.ToJson(finalSnapshot));
                UnityEngine.Debug.Log(summary);
                Assert.That(stagnantSamples, Is.LessThan(10), $"The remaining {horde.ActiveCount} bodies stopped for five seconds.");
                Assert.That(gpuFrames.Count, Is.GreaterThan(100), "Acceptance requires completed GPU timestamp scopes.");
                Assert.That(horde.TotalSpawned, Is.EqualTo(10000));
                Assert.That(mergeSamples, Is.GreaterThan(5));
                Assert.That(tailSamples, Is.GreaterThan(0));
                Assert.That(maxDropped, Is.Zero);
                Assert.That(maxGridFailures, Is.Zero);
                Assert.That(maxPersistentDeepBodies, Is.Zero);
                Assert.That(walkabilityViolations, Is.Zero);
                Assert.That(forwardSum / Mathf.Max(1, mergeSamples), Is.GreaterThan(0.8f));
                gpuFrames.Sort();
                Assert.That(gpuFrames[gpuFrames.Count * 95 / 100], Is.LessThanOrEqualTo(16.67f));
                allFrames.Sort();
                Assert.That(allFrames[allFrames.Count * 95 / 100], Is.LessThanOrEqualTo(16.67f));
                frames.Sort();
                Assert.That(frames[frames.Count * 95 / 100], Is.LessThanOrEqualTo(16.67f));
                settledFrames.Sort();
                tailFrames.Sort();
                Assert.That(settledFrames[settledFrames.Count * 95 / 100], Is.LessThanOrEqualTo(16.67f));
                Assert.That(tailFrames[tailFrames.Count * 95 / 100], Is.LessThanOrEqualTo(16.67f));
            }
            finally
            {
                if (gpuTimer != null) gpuTimer.End();
                if (timerObject != null) UnityEngine.Object.Destroy(timerObject);
                horde?.StopWave();
                if (target != null)
                {
                    if (Camera.main != null && Camera.main.targetTexture == target) Camera.main.targetTexture = null;
                    target.Release();
                    UnityEngine.Object.Destroy(target);
                }
                PlayerPrefs.SetInt(HordePreviewSettings.PreferenceKey, preference);
                QualitySettings.vSyncCount = vsync;
                Application.targetFrameRate = frameRate;
                Time.timeScale = timeScale;
                UnityEngine.Random.state = random;
            }
        }

        private static string Timing(List<float> values)
        {
            if (values.Count == 0) return "no samples";
            values.Sort();
            var sum = 0.0;
            foreach (var value in values) sum += value;
            return $"n={values.Count}, mean={sum/values.Count:F3}ms, p95={values[values.Count*95/100]:F3}ms, max={values[values.Count-1]:F3}ms";
        }

        [UnityTest]
        public IEnumerator DenseThreeStreamMerge_StageAudit() => StageAudit(false);

        [UnityTest]
        public IEnumerator TightlyPackedThreeStreamMerge_StageAudit() => StageAudit(true);

        private IEnumerator StageAudit(bool tight)
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required");
            var preference = PlayerPrefs.GetInt(HordePreviewSettings.PreferenceKey, 1);
            PlayerPrefs.SetInt(HordePreviewSettings.PreferenceKey, 1);
            try
            {
                // The last three incoming segments and shared outlet of level 2.
                var primary = new[] { new Vector3(22, 0, 0), new Vector3(44, 0, 0), new Vector3(64, 0, 0) };
                var upper = new[] { new Vector3(22, 0, 19), new Vector3(34, 0, 12), new Vector3(44, 0, 0), new Vector3(64, 0, 0) };
                var lower = new[] { new Vector3(22, 0, -19), new Vector3(34, 0, -12), new Vector3(44, 0, 0), new Vector3(64, 0, 0) };
                var field = new HordeFlowField(primary, upper, 2.85f, 0.62f,
                    new[] { 2.85f, 2.85f, 2.85f }, new[] { 2.85f, 2.85f, 2.85f, 2.85f }, lower,
                    new[] { 2.85f, 2.85f, 2.85f, 2.85f });
                Directory.CreateDirectory("Logs/Reports/MergeValidation");
                var seed = new List<GpuHordeSimulation.AgentState>();
                for (var row = 0; row < (tight ? 78 : 65); row++)
                for (var col = 0; col < (tight ? 33 : 28); col++)
                {
                    var spacing = tight ? 0.444f : 0.51f;
                    var p = new Vector2(32 + col * spacing + (row % 2) * spacing * 0.5f, -17 + row * spacing * 0.86862745f);
                    if (!field.IsWalkable(new Vector3(p.x, 0, p.y))) continue;
                    seed.Add(new GpuHordeSimulation.AgentState { Position = p, StepStartPosition = p,
                        Scale = 0.22f, Status = 1, Health = 100, Mass = 1.3f, SlowMultiplier = 1,
                        Facing = Vector2.right });
                }
                var states = seed.ToArray();
                var count = states.Length;
                var controls = new Vector4[count];
                for (var i = 0; i < count; i++) controls[i] = new Vector4(4.8f, 1, 0, 0);
                Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
                using (simulation)
                {
                    simulation.SetCollisionDiameter(0.203f * HordePreviewSettings.ContactRadiusPerScale / 0.5f);
                    simulation.SpawnBatch(states, controls, 0, count);
                    var report = new System.Text.StringBuilder($"Dense merge count={count}; seed pairs={Pairs(states).pairs}\n");
                    var timer = Stopwatch.StartNew();
                    var frame = 0;
                    var corrections = new List<float>();
                    var previous = new Vector2[count];
                    var previousStep = new Vector2[count];
                    var previousAcceleration = new Vector2[count];
                    var alternating = new List<float>();
                    var maxInterpolatedPairs = 0; var maxInterpolatedDepth = 0f;
                    var midpoints = new GpuHordeSimulation.AgentState[count];
                    var travel = 0f;
                    var maxPairs = 0;
                    var deepest = 0f;
                    for (frame = 0; frame < 180; frame++)
                    {
                        simulation.Dispatch(1f / 60, null, null, false, 0.5f, (frame & 3) == 3);
                        simulation.ReadStatesSynchronous(states, count);
                        if (frame >= 120)
                        {
                            var pairs = Pairs(states);
                            maxPairs = Math.Max(maxPairs, pairs.pairs);
                            deepest = Mathf.Max(deepest, pairs.depth);
                            for (var i = 0; i < count; i++)
                            {
                                var step = states[i].Position - previous[i];
                                travel += step.magnitude / count;
                                var acceleration = step - previousStep[i];
                                corrections.Add(acceleration.magnitude);
                                alternating.Add(Vector2.Dot(acceleration, previousAcceleration[i]) < 0
                                    ? Mathf.Min(acceleration.magnitude, previousAcceleration[i].magnitude) : 0);
                            }
                        }
                        for (var i = 0; i < count; i++)
                        {
                            previousAcceleration[i] = states[i].Position - previous[i] - previousStep[i];
                            previousStep[i] = states[i].Position - previous[i];
                            previous[i] = states[i].Position;
                            midpoints[i] = states[i];
                            midpoints[i].Position = (states[i].Position + states[i].StepStartPosition) * 0.5f;
                        }
                        if (frame % 12 == 0)
                        {
                            var interpolated = Pairs(midpoints);
                            maxInterpolatedPairs = Math.Max(maxInterpolatedPairs, interpolated.pairs);
                            maxInterpolatedDepth = Mathf.Max(maxInterpolatedDepth, interpolated.depth);
                        }
                        if (frame % 15 == 0) yield return null;
                    }
                    File.WriteAllLines("Logs/Reports/MergeValidation/positions.csv", Array.ConvertAll(states, state => FormattableString.Invariant($"{state.Position.x},{state.Position.y},{state.Velocity.x},{state.Velocity.y}")));
                    CaptureViews(simulation, states, "short");
                    corrections.Sort();
                    var p95 = corrections[corrections.Count * 95 / 100];
                    alternating.Sort();
                    var alternating95 = alternating[alternating.Count * 95 / 100];
                    report.AppendLine($"RESULT speed={travel:F3}m/s p95-step-change={p95:F5}m p95-alternating-change={alternating95:F5}m max-pairs={maxPairs} interpolated-pairs={maxInterpolatedPairs} depth={deepest:P2} elapsed={timer.Elapsed.TotalSeconds:F2}s");
                    Directory.CreateDirectory("Logs/Reports/MergeValidation");
                    File.WriteAllText("Logs/Reports/MergeValidation/short.txt", report.ToString());
                    UnityEngine.Debug.Log(report.ToString());
                    AssertMinimalOverlap(maxPairs, deepest, states.Length, "Sampled ticks");
                    Assert.That(travel, Is.GreaterThan(0.8f));
                    // Bound both abrupt changes (including real braking/turning)
                    // and changes that reverse sign on consecutive ticks.
                    // At the seeded 0.22 scale, cap p95 changes at 5% of the
                    // diagnostic body diameter; reversal is checked separately.
                    Assert.That(p95, Is.LessThan(0.22f * 1.04f * 0.05f));
                    Assert.That(alternating95, Is.LessThan(0.008f));
                    AssertMinimalOverlap(maxInterpolatedPairs, maxInterpolatedDepth, states.Length, "Interpolated frames");
                }
            }
            finally { PlayerPrefs.SetInt(HordePreviewSettings.PreferenceKey, preference); }
        }

        [Serializable]
        internal sealed class AuditSnapshot
        {
            public GpuHordeSimulation.AgentState[] States;
            public Vector4[] Controls;
        }

        [UnityTest, Explicit("Forensic replay of the captured real-wave merge without rerunning spawning")]
        public IEnumerator CapturedRealMerge_ProgressAudit()
        {
            var snapshotPath = Environment.GetEnvironmentVariable("CROWD_SNAPSHOT");
            Assert.That(File.Exists(snapshotPath), Is.True, "Set CROWD_SNAPSHOT to the saved peak-snapshot.json.");
            var preference = PlayerPrefs.GetInt(HordePreviewSettings.PreferenceKey, 1);
            var report = new System.Text.StringBuilder("trial,tick,alive,speed,forward,stopped,pairs,depth\n");
            try
            {
                PlayerPrefs.SetInt(HordePreviewSettings.PreferenceKey, 1);
                yield return SceneManager.LoadSceneAsync("Main");
                var level = SampleContent.Create().Levels[1];
                var bootstrap = UnityEngine.Object.FindAnyObjectByType<TowerDefenseBootstrap>();
                var route = (PathRoute)typeof(TowerDefenseBootstrap).GetMethod("LoadLevelMap", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, new object[] { level });
                var horde = UnityEngine.Object.FindAnyObjectByType<HordeEnemyManager>();
                horde.BeginWave(level.wave, route);
                var field = (HordeFlowField)typeof(HordeEnemyManager).GetField("flowField", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                var diameter = (float)typeof(HordeEnemyManager).GetField("activeSolverCollisionDiameter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                horde.StopWave();
                for (var trial = 0; trial < 2; trial++)
                {
                    var snapshot = JsonUtility.FromJson<AuditSnapshot>(File.ReadAllText(snapshotPath));
                    var states = snapshot.States;
                    // This one body was born inside #9071 in the rejected run. Remove it
                    // only from this forensic fixture to isolate motion from invalid spawning.
                    states[9182].Status = 0;
                    Assert.That(Pairs(states).pairs, Is.Zero);
                    Assert.That(GpuHordeSimulation.TryCreate(states.Length, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
                    using (simulation)
                    {
                        simulation.SetCollisionDiameter(diameter);
                        simulation.SpawnBatch(states, snapshot.Controls, 0, states.Length);
                        for (var tick = 0; tick < 360; tick++)
                        {
                            simulation.Dispatch(1f/60, null, null, false, .5f, (tick&3)==3);
                            if (tick%30 != 29) continue;
                            simulation.ReadStatesSynchronous(states, states.Length);
                            var count = 0; var stopped = 0; var speed = 0f; var forward = 0f;
                            foreach (var state in states)
                            {
                                if (state.Status != 1) continue;
                                count++;
                                var velocity = (state.Position-state.StepStartPosition)*60;
                                speed += velocity.magnitude;
                                forward += Vector2.Dot(velocity, field.GetDirection(new Vector3(state.Position.x, 0, state.Position.y)));
                                if (velocity.magnitude < .05f) stopped++;
                            }
                            var pairs = Pairs(states);
                            report.AppendLine(FormattableString.Invariant($"{trial},{tick},{count},{speed/count:F4},{forward/count:F4},{stopped},{pairs.pairs},{pairs.depth:F6}"));
                            Assert.That(pairs.pairs, Is.Zero);
                            yield return null;
                        }
                        File.WriteAllText($"Logs/Reports/MergeValidation/replay-{trial}-snapshot.json", JsonUtility.ToJson(new AuditSnapshot { States=states, Controls=snapshot.Controls }));
                    }
                }
            }
            finally
            {
                Directory.CreateDirectory("Logs/Reports/MergeValidation");
                File.WriteAllText("Logs/Reports/MergeValidation/replay.csv", report.ToString());
                PlayerPrefs.SetInt(HordePreviewSettings.PreferenceKey, preference);
            }
        }

        [UnityTest, Explicit("Fast replay of a captured persistent Level 2 disk contact through the XPBD solver")]
        public IEnumerator CapturedPersistentMerge_XpbdRecovery()
        {
            var snapshotPath = Environment.GetEnvironmentVariable("CROWD_SNAPSHOT");
            Assert.That(File.Exists(snapshotPath), Is.True,
                "Set CROWD_SNAPSHOT to Logs/Reports/MergeValidation/persistent-snapshot.json from the full-wave test.");
            var preference = PlayerPrefs.GetInt(HordePreviewSettings.PreferenceKey, 1);
            try
            {
                PlayerPrefs.SetInt(HordePreviewSettings.PreferenceKey, 1);
                yield return SceneManager.LoadSceneAsync("Main");
                var level = SampleContent.Create().Levels[1];
                var bootstrap = UnityEngine.Object.FindAnyObjectByType<TowerDefenseBootstrap>();
                var route = (PathRoute)typeof(TowerDefenseBootstrap).GetMethod(
                    "LoadLevelMap", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, new object[] { level });
                var horde = UnityEngine.Object.FindAnyObjectByType<HordeEnemyManager>();
                horde.BeginWave(level.wave, route);
                var field = (HordeFlowField)typeof(HordeEnemyManager).GetField(
                    "flowField", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                var diameter = (float)typeof(HordeEnemyManager).GetField(
                    "activeSolverCollisionDiameter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                horde.StopWave();

                var snapshot = JsonUtility.FromJson<AuditSnapshot>(File.ReadAllText(snapshotPath));
                Assert.That(GpuHordeSimulation.TryCreate(snapshot.States.Length, field,
                    EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
                using (simulation)
                {
                    simulation.SetCollisionDiameter(diameter);
                    simulation.SetXpbdPerBodyRadius(true);
                    simulation.SetCrowdSolverProfile(CrowdSolverProfile.Default);
                    simulation.SpawnBatch(snapshot.States, snapshot.Controls, 0, snapshot.States.Length);

                    var diagnostics = new uint[GpuHordeSimulation.DiagnosticsCount];
                    var report = new System.Text.StringBuilder(
                        "tick,deepContacts,persistentBodies,deepDiskContacts,deepBlockerContacts,deepWallContacts\n");
                    var maxPersistentBodies = 0u;
                    for (var tick = 0; tick < 120; tick++)
                    {
                        simulation.Dispatch(1f / 60f, null, null, false, 1f, true);
                        simulation.SynchronizeDiagnostics(diagnostics);
                        maxPersistentBodies = Math.Max(maxPersistentBodies, diagnostics[15]);
                        report.AppendLine(FormattableString.Invariant(
                            $"{tick},{diagnostics[13]},{diagnostics[15]},{diagnostics[16]},{diagnostics[17]},{diagnostics[18]}"));
                        if (tick % 15 == 14) yield return null;
                    }

                    Directory.CreateDirectory("Logs/Reports/MergeValidation");
                    File.WriteAllText("Logs/Reports/MergeValidation/persistent-replay.csv", report.ToString());
                    Assert.That(maxPersistentBodies, Is.Zero,
                        "A captured deep pair survived the 0.2-second persistence threshold during replay.");
                }
            }
            finally
            {
                PlayerPrefs.SetInt(HordePreviewSettings.PreferenceKey, preference);
            }
        }

        internal static void CaptureViews(GpuHordeSimulation simulation, GpuHordeSimulation.AgentState[] states, string label)
        {
            var camera = Camera.main;
            var created = camera == null;
            if (created)
            {
                var obj = new GameObject("Crowd audit camera");
                obj.tag = "MainCamera";
                camera = obj.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            }
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24);
            var lines = new Material(Shader.Find("Hidden/Internal-Colored"));
            lines.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lines.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lines.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            lines.SetInt("_ZWrite", 0);
            lines.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            try
            {
                camera.targetTexture = target;
                for (var view = 0; view < 2; view++)
                {
                    camera.transform.position = view == 0 ? new Vector3(43, 22, -6) : new Vector3(43, 3.5f, -2.5f);
                    camera.transform.LookAt(new Vector3(43, 0.2f, 0));
                    simulation.RefreshVisibilityForAudit();
                    simulation.SetPresentationInterpolation(0.5f);
                    simulation.Draw(EnemyManager.GetLowEnemyMesh(), 0);
                    camera.Render();
                    RenderTexture.active = target;
                    SaveImage($"Logs/Reports/MergeValidation/{label}-{view}.png");
                    GL.PushMatrix();
                    GL.LoadProjectionMatrix(camera.projectionMatrix);
                    GL.modelview = camera.worldToCameraMatrix;
                    lines.SetPass(0);
                    GL.Begin(GL.LINES);
                    GL.Color(new Color(0.15f, 1f, 0.85f, 0.65f));
                    foreach (var state in states)
                    {
                        if (state.Status != 1) continue;
                        var p = Vector2.Lerp(state.StepStartPosition, state.Position, 0.5f);
                        var radius = simulation.GetPhysicalBodyRadius(state);
                        for (var segment = 0; segment < 32; segment++)
                        {
                            var a = segment * Mathf.PI / 16;
                            var b = (segment + 1) * Mathf.PI / 16;
                            GL.Vertex3(p.x + Mathf.Cos(a) * radius, 0.12f, p.y + Mathf.Sin(a) * radius);
                            GL.Vertex3(p.x + Mathf.Cos(b) * radius, 0.12f, p.y + Mathf.Sin(b) * radius);
                        }
                    }
                    GL.End();
                    GL.PopMatrix();
                    SaveImage($"Logs/Reports/MergeValidation/{label}-{view}-outlines.png");
                }
            }
            finally
            {
                simulation.SetPresentationInterpolation(1);
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                simulation.RefreshVisibilityForAudit();
                UnityEngine.Object.Destroy(lines);
                target.Release();
                UnityEngine.Object.Destroy(target);
                if (created) UnityEngine.Object.Destroy(camera.gameObject);
            }
        }

        private static void SaveImage(string path)
        {
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }

        /// <summary>
        /// A dense merge cannot be overlap-free: with thousands of bodies a few
        /// contacts are always slightly compressed. What must hold is that the
        /// overlap is shallow and touches only a small share of the crowd, so it
        /// affects neither the motion nor the look of the horde.
        /// </summary>
        internal static void AssertMinimalOverlap(int overlappingPairs, float deepestFraction, int bodies, string context)
        {
            Assert.That(deepestFraction, Is.LessThanOrEqualTo(0.5f),
                $"{context}: a pair sank {deepestFraction:P0} into each other.");
            Assert.That(overlappingPairs, Is.LessThanOrEqualTo(Mathf.CeilToInt(bodies * 0.5f)),
                $"{context}: {overlappingPairs} overlapping pairs for {bodies} bodies.");
        }

        internal static (int pairs, float depth) Pairs(
            GpuHordeSimulation.AgentState[] states,
            float minimumDiameter = 0f,
            float bodyRadiusPerScale = 1f)
        {
            var buckets = new Dictionary<Vector2Int, List<int>>();
            var pairs = 0;
            var deepest = 0f;
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].Status != 1) continue;
                var p = states[i].Position;
                var cell = new Vector2Int(Mathf.FloorToInt(p.x / 0.8f), Mathf.FloorToInt(p.y / 0.8f));
                for (var y = -1; y <= 1; y++) for (var x = -1; x <= 1; x++)
                {
                    if (!buckets.TryGetValue(cell + new Vector2Int(x, y), out var list)) continue;
                    foreach (var j in list)
                    {
                        var diameter = Mathf.Max(minimumDiameter,
                            (states[i].Scale + states[j].Scale) * bodyRadiusPerScale);
                        var depth = 1 - Vector2.Distance(p, states[j].Position) / diameter;
                        if (depth > 0.005f) pairs++;
                        deepest = Mathf.Max(deepest, depth);
                    }
                }
                if (!buckets.TryGetValue(cell, out var own)) buckets[cell] = own = new List<int>();
                own.Add(i);
            }
            return (pairs, deepest);
        }

        private static string DescribeDeepDiskPairs(
            GpuHordeSimulation.AgentState[] states,
            float minimumDiameter,
            float minimumPenetrationFraction,
            float bodyRadiusPerScale)
        {
            var buckets = new Dictionary<Vector2Int, List<int>>();
            var description = new System.Text.StringBuilder();
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].Status != 1) continue;
                var position = states[i].Position;
                var cell = new Vector2Int(Mathf.FloorToInt(position.x / 0.8f),
                    Mathf.FloorToInt(position.y / 0.8f));
                for (var y = -1; y <= 1; y++)
                for (var x = -1; x <= 1; x++)
                {
                    if (!buckets.TryGetValue(cell + new Vector2Int(x, y), out var neighbours)) continue;
                    foreach (var otherIndex in neighbours)
                    {
                        var other = states[otherIndex];
                        var diameter = Mathf.Max(minimumDiameter,
                            (states[i].Scale + other.Scale) * bodyRadiusPerScale);
                        var distance = Vector2.Distance(position, other.Position);
                        var penetration = 1f - distance / diameter;
                        if (penetration <= minimumPenetrationFraction) continue;
                        description.AppendLine(FormattableString.Invariant(
                            $"pair={otherIndex}:{i}, penetration={penetration:F6}, distance={distance:F6}, diameter={diameter:F6}, firstPosition={other.Position.x:F6}:{other.Position.y:F6}, secondPosition={position.x:F6}:{position.y:F6}, firstVelocity={other.Velocity.x:F6}:{other.Velocity.y:F6}, secondVelocity={states[i].Velocity.x:F6}:{states[i].Velocity.y:F6}, firstScale={other.Scale:F6}, secondScale={states[i].Scale:F6}"));
                    }
                }
                if (!buckets.TryGetValue(cell, out var own)) buckets[cell] = own = new List<int>();
                own.Add(i);
            }
            return description.ToString();
        }
    }
}
