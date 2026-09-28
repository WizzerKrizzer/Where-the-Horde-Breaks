using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class GoblinCrowdSceneTests
    {
        [UnityTest]
        public IEnumerator TenThousandRedGoblins_RenderOnTheActualThreeLaneMap()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            var hadPreference = PlayerPrefs.HasKey(GoblinPreviewAssets.PreferenceKey);
            var preference = PlayerPrefs.GetInt(GoblinPreviewAssets.PreferenceKey);
            var originalTimeScale = Time.timeScale;
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            HordeEnemyManager horde = null;
            try
            {
                yield return SceneManager.LoadSceneAsync("Main");
                var content = SampleContent.Create();
                var level = content.Levels[1];
                var bootstrap = Object.FindAnyObjectByType<TowerDefenseBootstrap>();
                var route = (PathRoute)typeof(TowerDefenseBootstrap).GetMethod("LoadLevelMap", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(bootstrap, new object[] { level });
                // Exercise the actual wave/manager without starting a rewarded
                // session, selecting/saving a level, or changing player progress.
                horde = Object.FindAnyObjectByType<HordeEnemyManager>();
                var camera = Camera.main;
                Object.FindAnyObjectByType<TopDownCameraController>().enabled = false;
                horde.BeginWave(level.wave, route);
                Time.timeScale = 1;
                var deadline = Time.realtimeSinceStartup + 120f;
                while (horde.TotalSpawned < 10000 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(horde.TotalSpawned, Is.EqualTo(10000));
                var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager)
                    .GetField("gpuSimulation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                var beforeMovement = new GpuHordeSimulation.AgentState[10000];
                simulation.ReadStatesSynchronous(beforeMovement, beforeMovement.Length);
                var movementStart = Time.time;
                yield return new WaitForSeconds(2f);
                var movementDuration = Time.time - movementStart;
                using var assets = GoblinPreviewAssets.TryLoad();
                var target = new RenderTexture(1280, 900, 24);
                var previousTarget = camera.targetTexture;
                camera.targetTexture = target;
                Directory.CreateDirectory("Logs/CrowdFix");
                try
                {
                    var stateSnapshot = new GpuHordeSimulation.AgentState[10000];
                    simulation.ReadStatesSynchronous(stateSnapshot, stateSnapshot.Length);
                    var livePositions = new System.Collections.Generic.List<Vector2>();
                    var movingCount = 0;
                    var liveTravelSpeedSum = 0f;
                    var measuredBodies = 0;
                    for (var i = 0; i < stateSnapshot.Length; i++)
                    {
                        var state = stateSnapshot[i];
                        if (state.Status != 1) continue;
                        livePositions.Add(state.Position);
                        if (beforeMovement[i].Status != 1) continue;
                        var travelSpeed = Vector2.Distance(state.Position, beforeMovement[i].Position) / movementDuration;
                        liveTravelSpeedSum += travelSpeed;
                        measuredBodies++;
                        if (travelSpeed > 0.75f) movingCount++;
                    }
                    var diagnostics = new uint[7];
                    simulation.SynchronizeDiagnostics(diagnostics);
                    var movingFraction = movingCount / Mathf.Max(1f, measuredBodies);
                    var meanTravelSpeed = liveTravelSpeedSum / Mathf.Max(1f, measuredBodies);
                    var motionMetrics = $"Three-lane motion: live={livePositions.Count}, measured={measuredBodies}, moving={movingFraction:P1}, actual-travel={meanTravelSpeed:F2}m/s, collisions={diagnostics[5]}";
                    LogAssert.Expect(LogType.Log, motionMetrics);
                    Debug.Log(motionMetrics);
                    Assert.That(movingFraction, Is.GreaterThan(0.7f), "Most of the unobstructed three-lane horde should visibly advance.");
                    Assert.That(meanTravelSpeed, Is.GreaterThan(1f), "The unobstructed horde is moving too slowly.");
                    Assert.That(diagnostics[5], Is.Zero, "The moving three-lane horde must remain non-overlapping.");
                    livePositions.Sort((a, b) => a.x.CompareTo(b.x));
                    Assert.That(livePositions.Count, Is.GreaterThan(1000));
                    var middle = livePositions[livePositions.Count / 2];
                    var front = livePositions[livePositions.Count * 9 / 10];
                    var focuses = new[] { new Vector3(0, 0, 0), new Vector3(middle.x, 0, middle.y), new Vector3(front.x, 0, front.y) };
                    var heights = new[] { 90f, 10f, 10f };
                    var names = new[] { "crowd-wide", "crowd-close", "crowd-exit" };
                    for (var view = 0; view < heights.Length; view++)
                    {
                        camera.transform.rotation = Quaternion.Euler(75, 0, 0);
                        camera.transform.position = focuses[view] - camera.transform.forward * (heights[view] / Mathf.Sin(75f * Mathf.Deg2Rad));
                        for (var warmup = 0; warmup < 30; warmup++) yield return null;
                        var begin = Time.realtimeSinceStartupAsDouble;
                        for (var sample = 0; sample < 180; sample++) yield return null;
                        var frameMs = (Time.realtimeSinceStartupAsDouble - begin) * 1000 / 180;
                        var nearArgs = new uint[5];
                        var farArgs = new uint[5];
                        simulation.ReadDrawArgsSynchronous(nearArgs);
                        simulation.ReadLodDrawArgsSynchronous(farArgs);
                        var metrics = $"{names[view]}: active={horde.ActiveCount}, near={nearArgs[1]}, lod={farArgs[1]}, editor-frame-ms={frameMs:F2}, contact-passes={simulation.LastContactIterationCount}, overlap={simulation.MaximumOverlapFraction:F4}";
                        LogAssert.Expect(LogType.Log, metrics);
                        Debug.Log(metrics);
                        File.AppendAllText("Logs/CrowdFix/scene-metrics.txt", metrics + "\n");
                        simulation.Draw(assets.Mesh, 0);
                        camera.Render();
                        var active = RenderTexture.active;
                        RenderTexture.active = target;
                        var image = new Texture2D(1280, 900, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
                        image.Apply();
                        File.WriteAllBytes($"Logs/CrowdFix/{names[view]}.png", image.EncodeToPNG());
                        RenderTexture.active = active;
                        Object.Destroy(image);
                    }
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    target.Release();
                    Object.Destroy(target);
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                horde?.StopWave();
                Time.timeScale = originalTimeScale;
                if (hadPreference) PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, preference);
                else PlayerPrefs.DeleteKey(GoblinPreviewAssets.PreferenceKey);
            }
        }
    }
}
