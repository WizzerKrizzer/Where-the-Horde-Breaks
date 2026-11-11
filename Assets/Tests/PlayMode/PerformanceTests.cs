using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using TowerDefense.Input;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Measures frame times in the real game (the Main scene, Level 2, its full horde) with the
    /// game camera drawing into a 1920x1080 target every frame: the horde walking, then with the
    /// corpse and stun-star layers switched off (their cost), then under a rock with every special
    /// thrown each second (effects, corpses, stars). Writes Logs/Reports/Performance/report.txt. The
    /// player's profile is copied first and put back afterwards.
    /// </summary>
    public sealed class PerformanceTests
    {
        private const string Output = "Logs/Reports/Performance";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private string profilePath;
        private byte[] profileBackup;
        private RenderTexture target;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            profilePath = Path.Combine(Application.persistentDataPath, "profile.json");
            profileBackup = File.Exists(profilePath) ? File.ReadAllBytes(profilePath) : null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (Camera.main != null)
            {
                Camera.main.targetTexture = null;
            }

            if (target != null)
            {
                Object.Destroy(target);
            }

            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            if (profileBackup != null)
            {
                File.WriteAllBytes(profilePath, profileBackup);
            }
            else if (File.Exists(profilePath))
            {
                File.Delete(profilePath);
            }
        }

        [UnityTest, Explicit("Measures frame times on Level 2 and writes Logs/Reports/Performance/report.txt.")]
        public IEnumerator Level2_FrameTimes()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var weapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            var enemies = Object.FindFirstObjectByType<EnemyManager>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));
            session.StartLevel();
            weapon.DevAutoActiveEnabled = false;

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "No game camera.");
            target = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager).GetField("gpuSimulation", Private).GetValue(horde);
            var report = new StringBuilder();
            report.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB), {SystemInfo.graphicsDeviceType}");
            report.AppendLine($"CPU: {SystemInfo.processorType}, {SystemInfo.processorCount} threads");

            // Let the horde fill the road.
            var settle = Time.realtimeSinceStartup + 35f;
            while (Time.realtimeSinceStartup < settle)
            {
                yield return null;
            }

            yield return Measure("walking", 8f, enemies, report, null);

            // The corpse and star layers off, to see what they cost.
            var corpseField = typeof(GpuHordeSimulation).GetField("corpseMaterial", Private);
            var starField = typeof(GpuHordeSimulation).GetField("stunMarkMaterial", Private);
            var corpse = corpseField.GetValue(simulation);
            var stars = starField.GetValue(simulation);
            corpseField.SetValue(simulation, null);
            starField.SetValue(simulation, null);
            yield return Measure("walking, no corpses or stars", 8f, enemies, report, null);
            corpseField.SetValue(simulation, corpse);
            starField.SetValue(simulation, stars);

            // A rock with every special each second, on the front of the horde.
            var settings = new RollingRock.Settings
            {
                ImpactRadius = 2.5f, ImpactDamage = 30f, ImpactMaxTargets = 40, ImpactKnockback = 1f, ImpactLaunch = 0.6f,
                RollDistance = 6f, RollSpeed = 4f, RollRadius = 1.3f, RollDamage = 20f, RollKnockback = 0.6f,
                FireTrail = true, FireDamagePerTick = 2f, FireTicksPerSecond = 2f,
                QuakeRadius = 4.5f, QuakeSeconds = 3f,
                ShatterShards = 6, ShatterDamage = 10f
            };
            var nextRock = 0f;
            yield return Measure("rock with every special each second", 10f, enemies, report, elapsed =>
            {
                if (elapsed < nextRock || !enemies.TryGetLeadEnemyAimPoint(weapon.Radius, out var aim))
                {
                    return;
                }

                nextRock = elapsed + 1f;
                RollingRock.Throw(enemies, aim, Vector3.zero, settings);
            });

            foreach (var level in new[] { EffectsQuality.Level.Reduced, EffectsQuality.Level.Minimal })
            {
                EffectsQuality.Set(level);
                nextRock = 0f;
                yield return Measure($"rock each second, effects {EffectsQuality.Label(level)}", 8f, enemies, report, elapsed =>
                {
                    if (elapsed < nextRock || !enemies.TryGetLeadEnemyAimPoint(weapon.Radius, out var aim))
                    {
                        return;
                    }

                    nextRock = elapsed + 1f;
                    RollingRock.Throw(enemies, aim, Vector3.zero, settings);
                });
            }

            EffectsQuality.Set(EffectsQuality.Level.Full);
            report.AppendLine($"corpses recorded: {simulation.ReadCorpseCountSynchronous()}");
            Directory.CreateDirectory(Output);
            File.WriteAllText($"{Output}/report.txt", report.ToString());
            Debug.Log("PERFORMANCE\n" + report);
        }

        private static IEnumerator Measure(string name, float seconds, EnemyManager enemies, StringBuilder report,
            System.Action<float> eachFrame)
        {
            var times = new List<float>();
            var start = Time.realtimeSinceStartup;
            var last = start;
            yield return null;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                eachFrame?.Invoke(Time.realtimeSinceStartup - start);
                yield return null;
                var now = Time.realtimeSinceStartup;
                times.Add((now - last) * 1000f);
                last = now;
            }

            times.Sort();
            var sum = 0f;
            foreach (var time in times)
            {
                sum += time;
            }

            var mean = times.Count > 0 ? sum / times.Count : 0f;
            var p95 = times.Count > 0 ? times[Mathf.Min(times.Count - 1, (int)(times.Count * 0.95f))] : 0f;
            var worst = times.Count > 0 ? times[times.Count - 1] : 0f;
            report.AppendLine($"{name}: {times.Count} frames, mean {mean:F2} ms ({1000f / Mathf.Max(0.01f, mean):F0} fps), " +
                              $"95% {p95:F2} ms, worst {worst:F2} ms; {enemies.TotalSpawned} spawned, {enemies.ActiveEnemyCount} alive");
        }
    }
}
