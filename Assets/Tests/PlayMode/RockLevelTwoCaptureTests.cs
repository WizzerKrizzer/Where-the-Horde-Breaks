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
    /// The active weapon's rocks in Level 2 of the real game (user, 2026-11-12): once the horde comes
    /// near a point of the road, the plain rock and then the burning one are thrown on it and roll back
    /// against the horde, as the player throws them (no drag). A fixed game camera (75 degrees,
    /// CAPTURE_HEIGHT, 11 m by default) watches at real speed. The player's profile is copied first and
    /// put back afterwards, byte for byte. Writes Logs/Captures/RockLevelTwo.
    /// </summary>
    public sealed class RockLevelTwoCaptureTests
    {
        private const string Output = "Logs/Captures/RockLevelTwo";
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

        [UnityTest, Timeout(600000), Explicit("Records the plain and the burning rock on Level 2 to Logs/Captures/RockLevelTwo.")]
        public IEnumerator RecordRocksOnLevelTwo()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            if (!RollingRock.ModelsInstalled)
            {
                Assert.Ignore("The rocks are not installed. Install them with Tools/Animation/install_rocks.sh.");
            }

            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var weapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            var enemies = Object.FindFirstObjectByType<EnemyManager>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            Assert.That(session != null && router != null && weapon != null && enemies != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));
            session.StartLevel();
            weapon.DevAutoActiveEnabled = false;

            var route = enemies.Route;
            Assert.That(route != null && route.HasUsableRoute, Is.True, "Level 2 has no road.");
            // A third of the way along: the rock lands there and rolls back towards the road's start.
            var along = route.TotalLength * 0.35f;
            var point = route.Sample(along);
            point.y = 0f;
            var deadline = Time.realtimeSinceStartup + 120f;
            while (!HordeNear(horde, point, 9f) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(HordeNear(horde, point, 9f), Is.True, "The horde never came near the point.");

            var fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Rock Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = fieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var cameraHeight = CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_HEIGHT", 11f);
            var folder = $"{Output}/fixed-{cameraHeight:F0}m";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            // Between where it lands and where its roll ends.
            var middle = (point + route.Sample(Mathf.Max(0f, along - weapon.RockRollDistance))) * 0.5f;
            middle.y = 0f;
            CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, middle, cameraHeight);

            var throwRock = typeof(ActiveWeaponController).GetMethod("ThrowRock",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(throwRock, Is.Not.Null, "The weapon cannot throw a rock.");
            weapon.Spear = false;
            weapon.Rock = true;
            Time.timeScale = 1f;
            Time.captureFramerate = 120;
            var next = Time.time;
            var frame = 0;
            foreach (var fire in new[] { false, true })
            {
                weapon.RockFireTrail = fire;
                throwRock.Invoke(weapon, new object[] { point, Vector3.zero });
                var rock = Object.FindObjectsByType<RollingRock>(FindObjectsSortMode.None);
                Debug.Log($"ROCK thrown ({(fire ? "burning" : "plain")}), {rock.Length} rocks in the air.");
                for (var i = 0; i < 30 * 5; i++, frame++)
                {
                    while (Time.time < next)
                    {
                        yield return null;
                    }

                    next += 1f / 30f;
                    CavemenLevelTwoCaptureTests.DrawHorde(horde);
                    yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
                }
            }

            Time.timeScale = 1f;
            Time.captureFramerate = 0;
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
            Debug.Log($"ROCKS on Level 2: landed at {point}, damage dealt {weapon.TotalDamageDealt:F0}.");
            Assert.That(weapon.TotalDamageDealt, Is.GreaterThan(0f), "The rocks never hurt a goblin.");
        }

        // True when a live goblin is within `reach` of `point`.
        private static bool HordeNear(HordeEnemyManager horde, Vector3 point, float reach)
        {
            if (horde == null || horde.TotalSpawned == 0)
            {
                return false;
            }

            var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager).GetField("gpuSimulation",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(horde);
            if (simulation == null)
            {
                return false;
            }

            var count = horde.TotalSpawned;
            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            foreach (var state in states)
            {
                if (state.Status != 1u)
                {
                    continue;
                }

                var offset = new Vector2(state.Position.x - point.x, state.Position.y - point.z);
                if (offset.sqrMagnitude < reach * reach)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
