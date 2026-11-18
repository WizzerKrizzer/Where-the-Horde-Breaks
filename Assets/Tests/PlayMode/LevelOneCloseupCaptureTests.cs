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
    /// Level 1 seen through the game's own camera at several heights, Full HD (user, 2026-11-18:
    /// the goblins looked low-detail from about 14 m, and close to a tower only the slingers' legs
    /// showed). A Slingers tower stands by the first turn while the stream comes. Writes
    /// Logs/Captures/LevelOneCloseup/&lt;height&gt;m.png. The game's camera (Camera.main) renders, so the
    /// horde picks its models for exactly that view.
    /// </summary>
    public sealed class LevelOneCloseupCaptureTests
    {
        private const string Output = "Logs/Captures/LevelOneCloseup";
        // Inside the first bend of layout A (2026-11-18), off the road.
        private static readonly Vector3 Tower = new(-6f, 0f, 1.5f);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Heights_GoblinsAndSlingersReadClose()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var towers = Object.FindFirstObjectByType<TowerManager>();
            var controller = Object.FindFirstObjectByType<TopDownCameraController>();
            session.SelectLevel("level_01");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));
            towers.RemoveAll();
            Slingers(session, towers);
            Assert.That(towers.TryPlace(towers.AvailableTowers[0], Tower), Is.True, "The Slingers could not be placed by the turn.");
            if (controller != null)
            {
                controller.enabled = false;
            }

            // Its range circle shows, centred on the tipi (user, 2026-11-18).
            towers.TrySelectNearest(Tower);
            Assert.That(towers.SelectedTower, Is.Not.Null);
            var center = towers.SelectedTower.RangeCenter;
            var tipi = towers.SelectedTower.GetComponentInChildren<MeshFilter>().transform.position;
            Debug.Log($"CLOSEUP range centre {center}, spot {towers.SelectedTower.Position}");
            session.StartLevel();
            var weapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            if (weapon != null)
            {
                weapon.DevAutoActiveEnabled = false;
            }

            var start = Time.time;
            while (Time.time - start < 13f)
            {
                yield return null;
            }

            var camera = Camera.main;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            var previous = camera.targetTexture;
            camera.targetTexture = target;
            Directory.CreateDirectory(Output);
            Time.timeScale = 0f;
            foreach (var height in new[] { 4f, 8f, 14f, 25f })
            {
                // The game's pitch (75 degrees), looking at the tower beside the turn.
                var lookAt = Tower + new Vector3(1.5f, 0f, 0f);
                var pitch = camera.transform.eulerAngles.x;
                camera.transform.position = lookAt + new Vector3(0f, height, -height / Mathf.Tan(pitch * Mathf.Deg2Rad));
                // The game's zoom: an orthographic view sized from the height.
                typeof(TopDownCameraController).GetMethod("UpdateOrthographicScale",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(controller, null);
                // Two frames: the horde picks each goblin's model for the camera of the frame before.
                yield return null;
                yield return null;
                CavemenLevelTwoCaptureTests.DrawHorde(Object.FindFirstObjectByType<HordeEnemyManager>());
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/{height:F0}m.png");
            }

            Time.timeScale = 1f;
            camera.targetTexture = previous;
            Object.Destroy(target);
            Object.Destroy(texture);
        }

        // One Slingers tower bought, as after 1 Kill Essence.
        private static void Slingers(GameSession session, TowerManager towers)
        {
            foreach (var definition in session.AllTowerDefinitions)
            {
                if (definition.id == "slingers")
                {
                    towers.SetAvailableTowers(new[] { definition });
                    towers.SetPerTypeLimitBonus("slingers", 1);
                    return;
                }
            }

            Assert.Fail("No Slingers in the game.");
        }
    }
}
