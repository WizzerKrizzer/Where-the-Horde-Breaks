using System.Collections;
using System.IO;
using NUnit.Framework;
using TowerDefense.Input;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Level 1 (user, 2026-11-17): the map from the left edge to the wall on the right, with two
    /// right-angle turns, a continuous wave of about 200 goblins, and the wall with 20 health. With
    /// no towers the goblins break the wall and the first one through loses the run. Times are
    /// logged as LEVEL1 lines. The player's profile is copied first and put back byte for byte.
    /// CAPTURE=1 also writes frames from the level's own camera to Logs/Captures/LevelOne.
    /// </summary>
    public sealed class LevelOneMapTests
    {
        private const string Output = "Logs/Captures/LevelOne";
        private string profilePath;
        private byte[] profileBackup;

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
            Time.captureFramerate = 0;
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

        [UnityTest, Timeout(300000)]
        public IEnumerator NoTowers_GoblinsBreakTheWallAndTheFirstThroughLoses()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var towers = Object.FindFirstObjectByType<TowerManager>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            var weapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            Assert.That(session != null && router != null && towers != null && horde != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_01");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));
            towers.RemoveAll();
            yield return null;

            var wall = LevelWall.Active;
            Assert.That(wall, Is.Not.Null, "Level 1 has no wall.");
            // The wall is bought (user, 2026-11-18): this run has it. The profile is put back after.
            // (The player's own profile may own it already.)
            if (!wall.IsBuilt)
            {
                session.AddCurrency(TowerDefense.Data.CurrencyType.KillEssence, 1);
                Assert.That(session.TryPurchaseUpgrade("wall_unlock"), Is.True, "The wall could not be bought for 1 Kill Essence.");
            }

            Assert.That(wall.IsBuilt, Is.True, "The bought wall does not stand.");
            // 20 of its own; more when the player's profile has bought Thicker Stakes.
            Assert.That(wall.MaximumHealth, Is.GreaterThanOrEqualTo(20f));
            Assert.That(session.Lives, Is.GreaterThanOrEqualTo(1), "Level 1 has no life.");

            var capture = System.Environment.GetEnvironmentVariable("CAPTURE") == "1";
            Camera camera = null;
            RenderTexture target = null;
            Texture2D texture = null;
            if (capture)
            {
                var main = Camera.main;
                var position = main.transform.position;
                var rotation = main.transform.rotation;
                var fieldOfView = main.fieldOfView;
                foreach (var other in Camera.allCameras)
                {
                    other.enabled = false;
                }

                camera = new GameObject("Level One Camera").AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = fieldOfView;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = PlaceholderPalette.Background;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.farClipPlane = 400f;
                target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
                texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
                camera.targetTexture = target;
                if (Directory.Exists(Output))
                {
                    Directory.Delete(Output, true);
                }

                Directory.CreateDirectory(Output);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/overview.png");
                Time.captureFramerate = 30;
            }

            session.StartLevel();
            if (weapon != null)
            {
                weapon.DevAutoActiveEnabled = false;
            }

            var start = Time.time;
            float firstHit = -1f, broken = -1f;
            var frame = 0;
            // The stream (user, 2026-11-17): no long gap and no once-a-second rhythm while it spawns.
            var lastCount = 0;
            var lastSpawnTime = 0f;
            var longestGap = 0f;
            var lastSpawnDone = -1f;
            var perQuarter = new int[200];
            var lastSim = 0f;
            var nextLeaderLog = 0f;
            var leaders = new System.Text.StringBuilder();
            // 4 m/s for real (user, 2026-11-17): the leader on the first straight; the median.
            var straightSpeeds = new System.Collections.Generic.List<float>();
            var lastLeaderX = float.MaxValue;
            var lastLeaderTime = 0f;
            var enemyManager = Object.FindFirstObjectByType<EnemyManager>();
            while (!session.Finished && Time.time - start < 90f)
            {
                if (firstHit < 0f && wall.CurrentHealth < wall.MaximumHealth)
                {
                    firstHit = horde.SimulatedSeconds;
                }

                var spawned = horde.TotalSpawned;
                var now = horde.SimulatedSeconds;
                lastSim = Mathf.Max(lastSim, now);
                if (now >= nextLeaderLog && enemyManager != null &&
                    enemyManager.TryGetHordeTargetSnapshot(Vector3.zero, 500f, true, TowerDefense.Data.TowerTargetingMode.First, out _, out var leader, out var leaderVelocity))
                {
                    leaders.Append($"{now:F0}s x{leader.x:F1} z{leader.z:F1} v{new Vector2(leaderVelocity.x, leaderVelocity.z).magnitude:F1}; ");
                    nextLeaderLog += 1f;
                    // Ground covered along the road (x) in each second, while the same goblin leads on
                    // the first straight (the leader can change to a fresh one, still at its spawn speed).
                    var dx = leader.x - lastLeaderX;
                    if (now <= 10f && leader.x < -6f && dx > 2.5f && dx < 5.5f)
                    {
                        straightSpeeds.Add(dx / Mathf.Max(0.5f, now - lastLeaderTime));
                    }

                    lastLeaderX = leader.x;
                    lastLeaderTime = now;
                }
                if (spawned > lastCount)
                {
                    // Not the first 3 s: in batch mode the first frames are long while shaders load,
                    // and the entrance check waits for the GPU's answer, so spawns come every 0.75 s
                    // there (seen 2026-11-17 in two runs of three).
                    if (lastCount > 0 && lastSpawnTime >= 3f)
                    {
                        longestGap = Mathf.Max(longestGap, now - lastSpawnTime);
                    }

                    var quarter = Mathf.Min(perQuarter.Length - 1, Mathf.FloorToInt(now * 4f));
                    perQuarter[quarter] += spawned - lastCount;
                    lastSpawnTime = now;
                    lastCount = spawned;
                    if (spawned >= 200 && lastSpawnDone < 0f)
                    {
                        lastSpawnDone = now;
                    }
                }

                if (broken < 0f && !wall.IsAlive)
                {
                    broken = horde.SimulatedSeconds;
                }

                if (capture)
                {
                    CavemenLevelTwoCaptureTests.DrawHorde(horde);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/frame_{frame++:D4}.png");
                }
                else
                {
                    yield return null;
                }
            }

            var lost = lastSim;
            Debug.Log($"LEVEL1 no towers: first wall hit {firstHit:F1} s, wall broken {broken:F1} s, run over {lost:F1} s, " +
                      $"won {session.Won}, spawned {horde.TotalSpawned}.");
            if (capture)
            {
                Time.captureFramerate = 0;
                Object.Destroy(target);
                Object.Destroy(texture);
                Object.Destroy(camera.gameObject);
            }

            var quarters = new System.Text.StringBuilder();
            for (var i = 0; i < Mathf.Min(perQuarter.Length, Mathf.CeilToInt(Mathf.Max(lastSpawnDone, lastSpawnTime) * 4f)); i++)
            {
                quarters.Append(perQuarter[i]).Append(' ');
            }

            Debug.Log($"LEVEL1 stream: {lastCount} spawned, all out at {lastSpawnDone:F1} s, longest gap {longestGap:F2} s; per quarter second: {quarters}");
            Debug.Log($"LEVEL1 leader: {leaders}");
            Assert.That(longestGap, Is.LessThan(0.6f), "The stream stopped for too long.");
            Assert.That(straightSpeeds.Count, Is.GreaterThan(0), "No leader on the first straight.");
            straightSpeeds.Sort();
            Assert.That(straightSpeeds[straightSpeeds.Count / 2], Is.InRange(3.6f, 4.4f), "The goblins do not walk at 4 m/s.");
            Assert.That(session.Finished, Is.True, "The run never ended.");
            Assert.That(session.Won, Is.False, "With no towers the run should be lost.");
            Assert.That(firstHit, Is.GreaterThan(0f), "No goblin hit the wall.");
            Assert.That(broken, Is.GreaterThan(firstHit), "The wall never broke.");
            Assert.That(lost, Is.GreaterThanOrEqualTo(broken), "The run was lost before the wall broke.");
        }
    }
}
