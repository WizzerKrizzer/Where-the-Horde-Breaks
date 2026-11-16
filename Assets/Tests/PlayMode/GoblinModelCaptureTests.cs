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
    /// The Meshy goblin in the real horde (user, 2026-11-13): Level 2 is played until the horde comes
    /// near a point of the road, then a fixed game camera (75 degrees) watches it for 6 s from 11 m
    /// and 6 s from 50 m at real speed (CAPTURE_HEIGHTS changes the heights). Nothing is clicked. The player's profile is copied first and
    /// put back afterwards, byte for byte. Writes Logs/Captures/GoblinModel/fixed-11m and fixed-50m.
    /// </summary>
    public sealed class GoblinModelCaptureTests
    {
        private const string Output = "Logs/Captures/GoblinModel";
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

        [UnityTest, Timeout(600000), Explicit("Records the goblin model in the Level 2 horde to Logs/Captures/GoblinModel.")]
        public IEnumerator RecordGoblinsOnLevelTwo()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            if (!GoblinModel.TryGet(out _))
            {
                Assert.Ignore("Resources/Meshy/goblin.bytes is missing (Meshy files stay local).");
            }

            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var weapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            var enemies = Object.FindFirstObjectByType<EnemyManager>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            Assert.That(session != null && router != null && enemies != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));
            session.StartLevel();
            if (weapon != null) weapon.DevAutoActiveEnabled = false;

            var route = enemies.Route;
            Assert.That(route != null && route.HasUsableRoute, Is.True, "Level 2 has no road.");
            // CAPTURE_ROUTE: how far along the road the camera looks, 0.3 by default.
            var point = route.Sample(route.TotalLength * CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_ROUTE", 0.3f));
            point.y = 0f;
            var deadline = Time.realtimeSinceStartup + 120f;
            while (!HordeNear(horde, point, 6f, out _) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(HordeNear(horde, point, 6f, out _), Is.True, "The horde never came near the point.");

            var fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Goblin Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = fieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            Time.timeScale = 1f;
            Time.captureFramerate = 120;
            // CAPTURE_HEIGHTS picks the camera heights, "11,50" by default ("4" for a close-up).
            var heights = System.Array.ConvertAll(
                (System.Environment.GetEnvironmentVariable("CAPTURE_HEIGHTS") ?? "11,50").Split(','),
                text => float.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
            foreach (var height in heights)
            {
                var folder = $"{Output}/fixed-{height:F0}m";
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }

                Directory.CreateDirectory(folder);
                CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, point, height);
                var next = Time.time;
                for (var frame = 0; frame < 30 * 6; frame++)
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

            HordeNear(horde, point, 1000f, out var speed);
            Debug.Log($"GOBLIN MODEL: mean walking speed {speed:F2} m/s.");
            Time.captureFramerate = 0;
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
        }

        [UnityTest, Explicit("Renders the goblin model alone, with a plain textured material, to Logs/Captures/GoblinModel/alone_*.png.")]
        public IEnumerator RenderGoblinAlone()
        {
            if (!GoblinModel.TryGet(out var model))
            {
                Assert.Ignore("Resources/Meshy/goblin.bytes is missing (Meshy files stay local).");
            }

            var goblin = new GameObject("Goblin Alone");
            goblin.AddComponent<MeshFilter>().sharedMesh = model.Near.Mesh;
            goblin.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Texture")) { mainTexture = model.Texture };
            goblin.transform.localScale = Vector3.one * 0.36f;
            // Facing +x, as a goblin walking to the right of the screen.
            goblin.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var camera = new GameObject("Alone Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.fieldOfView = 30f;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            Directory.CreateDirectory(Output);
            var middle = new Vector3(0f, 0.5f, 0f);
            foreach (var (name, pitch, yaw) in new[] { ("game", 75f, 0f), ("front", 10f, 90f), ("side", 10f, 0f) })
            {
                camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                camera.transform.position = middle - camera.transform.forward * 4f;
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/alone_{name}.png");
            }

            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(camera.gameObject);
            Object.Destroy(goblin);
        }

        [UnityTest, Timeout(300000), Explicit("Logs how far Level 2's goblins really move against how far their walk turned.")]
        public IEnumerator MeasureWalkAgainstMovement()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));
            session.StartLevel();
            var deadline = Time.realtimeSinceStartup + 60f;
            while ((horde == null || horde.TotalSpawned < 50) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return new WaitForSeconds(4f);
            var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager).GetField("gpuSimulation",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(horde);
            var count = horde.TotalSpawned;
            var before = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(before, count);
            var started = Time.time;
            yield return new WaitForSeconds(3f);
            var seconds = Time.time - started;
            var after = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(after, count);
            float moved = 0f, walked = 0f, speed = 0f;
            var alive = 0;
            for (var i = 0; i < count; i++)
            {
                if (before[i].Status != 1u || after[i].Status != 1u)
                {
                    continue;
                }

                moved += (after[i].Position - before[i].Position).magnitude;
                walked += after[i].AnimationDistance - before[i].AnimationDistance;
                speed += after[i].LocomotionSpeed;
                alive++;
            }

            Assert.That(alive, Is.GreaterThan(0), "No goblin lived through the measurement.");
            Debug.Log($"GOBLIN WALK: {alive} goblins over {seconds:F2} s: really moved {moved / alive:F2} m ({moved / alive / seconds:F2} m/s), " +
                $"walk turned by {walked / alive:F2} m, reported speed {speed / alive:F2} m/s.");
        }

        // True when a live goblin is within `reach` of `point`; `speed` is the mean speed of the
        // moving ones.
        private static bool HordeNear(HordeEnemyManager horde, Vector3 point, float reach, out float speed)
        {
            speed = 0f;
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
            var near = false;
            var moving = 0;
            foreach (var state in states)
            {
                if (state.Status != 1u)
                {
                    continue;
                }

                if (state.LocomotionSpeed > 0.05f)
                {
                    speed += state.LocomotionSpeed;
                    moving++;
                }

                var offset = new Vector2(state.Position.x - point.x, state.Position.y - point.z);
                near |= offset.sqrMagnitude < reach * reach;
            }

            speed = moving > 0 ? speed / moving : 0f;
            return near;
        }
    }
}
