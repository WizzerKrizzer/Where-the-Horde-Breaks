using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class CrowdSpawningPerformanceTests
    {
        [UnityTest, Explicit("10K live spawning throughput and frame stability benchmark")]
        public IEnumerator CollisionLab_LiveSpawningAndSettledFrames()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            var hadPreference = PlayerPrefs.HasKey(GoblinPreviewAssets.PreferenceKey);
            var preference = PlayerPrefs.GetInt(GoblinPreviewAssets.PreferenceKey);
            var oldScale = Time.timeScale;
            var oldVsync = QualitySettings.vSyncCount;
            var oldRate = Application.targetFrameRate;
            var randomState = Random.state;
            var root = new GameObject("Spawning benchmark");
            var cameraObject = new GameObject("Spawning benchmark camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            camera.transform.position = new Vector3(-40, 24, -2);
            camera.transform.rotation = Quaternion.Euler(75, 0, 0);
            camera.fieldOfView = 52;
            var horde = root.AddComponent<HordeEnemyManager>();
            var renderTarget = new RenderTexture(1280, 720, 24);
            camera.targetTexture = renderTarget;
            try
            {
                PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
                Time.timeScale = 1;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Random.InitState(761905);
                var level = SampleContent.Create().Levels[5];
                var route = root.AddComponent<PathRoute>();
                route.SetWaypoints(level.pathWaypoints, level.pathWaypointWidths);
                route.SetSecondaryWaypoints(level.secondaryPathWaypoints, level.secondaryPathWaypointWidths);
                route.SetSquareCorners(level.useSquarePathCorners);
                horde.BeginWave(level.wave, route);
                var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager)
                    .GetField("gpuSimulation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(horde);
                var frames = new List<float>(16000);
                var spawningCpu = new List<float>(16000);
                var begin = Time.realtimeSinceStartupAsDouble;
                while (horde.TotalSpawned < 10000 && Time.realtimeSinceStartupAsDouble - begin < 90)
                {
                    yield return null;
                    simulation.Draw(EnemyManager.GetLowEnemyMesh(), 0);
                    camera.Render();
                    if (horde.TotalSpawned > 500)
                    {
                        frames.Add(Time.unscaledDeltaTime * 1000);
                        spawningCpu.Add(horde.Performance.SpawnMs);
                    }
                }
                var duration = Time.realtimeSinceStartupAsDouble - begin;
                Assert.That(horde.TotalSpawned, Is.EqualTo(10000));
                var settled = new List<float>(240);
                for (var i = 0; i < 300; i++)
                {
                    yield return null;
                    simulation.Draw(EnemyManager.GetLowEnemyMesh(), 0);
                    camera.Render();
                    if (i >= 60) settled.Add(Time.unscaledDeltaTime * 1000);
                }
                var diagnostics = new uint[7];
                simulation.SynchronizeDiagnostics(diagnostics);
                var snapshot = new GpuHordeSimulation.AgentState[10000];
                simulation.ReadStatesSynchronous(snapshot, snapshot.Length);
                var deepestPair = DescribeDeepestPair(snapshot, 0.58f * level.wave.enemyScaleMultiplier *
                    GoblinPreviewAssets.ContactRadiusPerScale);
                Directory.CreateDirectory("Logs/CrowdSmooth");
                var report = $"10K spawn duration: {duration:F2}s\nSpawning frames: {Summarize(frames)}\nSpawn CPU: {Summarize(spawningCpu)}\nAfter spawning: {Summarize(settled)}\nGrid: max={diagnostics[2]}, overflow={diagnostics[0]}, dropped={diagnostics[1]}\nCollision pairs: {diagnostics[5]}, deepest: {diagnostics[6] / 10000f:F2}%\n{deepestPair}\n";
                File.WriteAllText("Logs/CrowdSmooth/spawning-metrics.txt", report);
                LogAssert.Expect(LogType.Log, report);
                Debug.Log(report);
                Assert.That(diagnostics[5], Is.Zero,
                    $"The settled collision-lab horde still has {diagnostics[5]} overlapping body pairs.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                horde.StopWave();
                Object.Destroy(root);
                Object.Destroy(cameraObject);
                renderTarget.Release();
                Object.Destroy(renderTarget);
                Time.timeScale = oldScale;
                QualitySettings.vSyncCount = oldVsync;
                Application.targetFrameRate = oldRate;
                Random.state = randomState;
                if (hadPreference) PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, preference);
                else PlayerPrefs.DeleteKey(GoblinPreviewAssets.PreferenceKey);
            }
        }

        private static string Summarize(List<float> values)
        {
            if (values.Count == 0) return "no samples";
            double sum = 0;
            foreach (var value in values) sum += value;
            values.Sort();
            return $"n={values.Count}, mean={sum / values.Count:F3}ms, p95={values[(values.Count - 1) * 95 / 100]:F3}ms, max={values[values.Count - 1]:F3}ms";
        }

        private static string DescribeDeepestPair(GpuHordeSimulation.AgentState[] states, float collisionDiameter)
        {
            var live = new List<int>(states.Length);
            for (var i = 0; i < states.Length; i++) if (states[i].Status == 1u) live.Add(i);
            live.Sort((a, b) => states[a].Position.x.CompareTo(states[b].Position.x));
            var bestFraction = 0f;
            var bestA = -1;
            var bestB = -1;
            for (var sortedA = 0; sortedA < live.Count; sortedA++)
            {
                var a = live[sortedA];
                for (var sortedB = sortedA + 1; sortedB < live.Count; sortedB++)
                {
                    var b = live[sortedB];
                    if (states[b].Position.x - states[a].Position.x > collisionDiameter * 1.6f) break;
                    var pairDiameter = Mathf.Clamp(
                        (states[a].Scale + states[b].Scale) * GoblinPreviewAssets.ContactRadiusPerScale * 1.04f,
                        collisionDiameter * 0.82f,
                        collisionDiameter * 1.55f);
                    var fraction = 1f - Vector2.Distance(states[a].Position, states[b].Position) / pairDiameter;
                    if (fraction <= bestFraction) continue;
                    bestFraction = fraction;
                    bestA = a;
                    bestB = b;
                }
            }
            return bestA < 0
                ? "Deepest CPU pair: none"
                : $"Deepest CPU pair: {bestFraction:P2}; A#{bestA} p={states[bestA].Position} v={states[bestA].Velocity}; B#{bestB} p={states[bestB].Position} v={states[bestB].Velocity}";
        }
    }
}
