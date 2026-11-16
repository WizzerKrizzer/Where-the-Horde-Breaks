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
    /// The thrown spear in Level 2 of the real game (user, 2026-11-12): once the horde comes near a
    /// point of the road, a simulated click lands on the goblin nearest that point every time the spear
    /// is ready, as the player clicks; first with the heavy spear, then the volley, then everything with
    /// precise strikes. A fixed game camera (75 degrees, CAPTURE_HEIGHT, 11 m by
    /// default) watches at real speed. The player's profile is copied first and put back afterwards,
    /// byte for byte. Writes Logs/Captures/SpearLevelTwo.
    /// </summary>
    public sealed class SpearLevelTwoCaptureTests
    {
        private const string Output = "Logs/Captures/SpearLevelTwo";
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

        [UnityTest, Timeout(600000), Explicit("Records the thrown spear on Level 2 to Logs/Captures/SpearLevelTwo.")]
        public IEnumerator RecordSpearsOnLevelTwo()
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
            Assert.That(session != null && router != null && weapon != null && enemies != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            var current = typeof(PlayerInputRouter).GetProperty("Current");
            void Feed(GameInputState state) => current.SetValue(router, state);
            Feed(default);
            session.StartLevel();
            weapon.DevAutoActiveEnabled = false;
            Assert.That(weapon.Spear, Is.True, "The level does not use the thrown spear.");

            var route = enemies.Route;
            Assert.That(route != null && route.HasUsableRoute, Is.True, "Level 2 has no road.");
            var point = route.Sample(route.TotalLength * 0.35f);
            point.y = 0f;
            var deadline = Time.realtimeSinceStartup + 120f;
            while (!HordeNear(horde, point, 7f) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(HordeNear(horde, point, 7f), Is.True, "The horde never came near the point.");

            var fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Spear Camera");
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
            CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, point, cameraHeight);

            Time.timeScale = 1f;
            Time.captureFramerate = 120;
            ThrownSpear.ResetCounters();
            ThrownSpear.ViewCamera = camera;
            var next = Time.time;
            // Three parts, 5 s each (user, 2026-11-12): the heavy spear, then the volley (3 spears),
            // then all of it with the precise strike at a 50% chance so it shows often.
            for (var frame = 0; frame < 30 * 15; frame++)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += 1f / 30f;
                var part = frame / (30 * 5);
                weapon.SpearSplashRadius = part != 1 ? 1.4f : 0f;
                weapon.SpearExtraSpears = part >= 1 ? 2 : 0;
                weapon.SpearCritChance = part == 2 ? 0.5f : 0f;
                weapon.SpearCritMultiplier = 3f;
                // Click the goblin nearest the point whenever the spear is ready.
                if (weapon.IsReady && enemies.TryGetHordeTargetSnapshot(point, 6f, true, TowerDefense.Data.TowerTargetingMode.Closest, out _, out var goblin, out _))
                {
                    Feed(new GameInputState { FireActive = true, FireHeld = true, PointerWorld = new Vector3(goblin.x, 0f, goblin.z) });
                    yield return null;
                    Feed(default);
                }

                CavemenLevelTwoCaptureTests.DrawHorde(horde);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
            }

            Time.timeScale = 1f;
            Time.captureFramerate = 0;
            ThrownSpear.ViewCamera = null;
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
            Debug.Log($"SPEARS on Level 2: thrown {ThrownSpear.Thrown}, hits {ThrownSpear.Hits}, shocks {ThrownSpear.Shocks}, crits {ThrownSpear.Crits}, damage {weapon.TotalDamageDealt:F0}.");
            Assert.That(ThrownSpear.Hits, Is.GreaterThan(0), "No spear hit a goblin.");
        }

        [UnityTest, Explicit("Renders the thrown spear up close to Logs/Captures/SpearLevelTwo/closeup.png.")]
        public IEnumerator RenderSpearCloseUp()
        {
            var root = new GameObject("Spear Close Up");
            var spear = SpearModel.Create(root.transform, "Spear");
            spear.localRotation = Quaternion.Euler(0f, 90f, 0f);
            spear.localPosition = new Vector3(SpearModel.Length * 0.5f, 0.6f, 0f);
            var head = SpearModel.Create(root.transform, "Head");
            head.localRotation = Quaternion.Euler(-20f, 125f, 0f);
            head.localPosition = new Vector3(0.45f, 0.45f, -0.45f);
            var camera = new GameObject("Close Up Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.fieldOfView = 30f;
            camera.transform.position = new Vector3(0f, 1.6f, -2.6f);
            camera.transform.LookAt(new Vector3(0f, 0.5f, 0f));
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            Directory.CreateDirectory(Output);
            yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/closeup.png");
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(camera.gameObject);
            Object.Destroy(root);
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
