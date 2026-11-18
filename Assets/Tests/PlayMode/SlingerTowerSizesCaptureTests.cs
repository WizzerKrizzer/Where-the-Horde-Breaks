using System.Collections;
using System.IO;
using NUnit.Framework;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// The slinger tipi for 2, 4, 6 and 8 slingers side by side on level 1's grass (user,
    /// 2026-11-18: narrow at first, wider as the crew grows). Writes
    /// Logs/Captures/SlingerTowerSizes/sizes.png and a closer view of the two smallest. Needs the
    /// local models (Tools/Animation/install_crew_models.sh).
    /// </summary>
    public sealed class SlingerTowerSizesCaptureTests
    {
        private const string Output = "Logs/Captures/SlingerTowerSizes";

        [UnityTest, Timeout(120000)]
        public IEnumerator FourCrewSizes_NarrowToWide()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            session.SelectLevel("level_01");
            yield return null;
            Object.FindFirstObjectByType<TowerManager>()?.RemoveAll();
            // The trees hid the widest tipi: the picture shows the towers only.
            foreach (var scenery in Object.FindObjectsByType<LevelScenery>(FindObjectsSortMode.None))
            {
                scenery.gameObject.SetActive(false);
            }

            var holder = new GameObject("Slinger tower sizes");
            var widths = new float[4];
            for (var i = 0; i < SlingerTowerVisual.CrewSizes.Length; i++)
            {
                var spot = new GameObject($"Spot {SlingerTowerVisual.CrewSizes[i]}");
                spot.transform.SetParent(holder.transform, false);
                spot.transform.position = new Vector3(-1f + i * 6f, 0f, 4f);
                var tower = spot.AddComponent<TowerActor>();
                var visual = SlingerTowerVisual.TryCreate(tower, Vector3.back, SlingerTowerVisual.CrewSizes[i]);
                if (visual == null)
                {
                    Assert.Ignore("The slinger models are not installed.");
                }

                Assert.That(visual.CrewCount, Is.EqualTo(SlingerTowerVisual.CrewSizes[i]));
                var bounds = new Bounds(spot.transform.position, Vector3.zero);
                foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
                {
                    bounds.Encapsulate(renderer.bounds);
                }

                widths[i] = bounds.size.x;
            }

            // A crew of 3 stands on the tipi for 4, one seat empty (user, 2026-11-18).
            var odd = new GameObject("Spot 3").AddComponent<TowerActor>();
            odd.transform.SetParent(holder.transform, false);
            odd.transform.position = new Vector3(-1f, 0f, 14f);
            var three = SlingerTowerVisual.TryCreate(odd, Vector3.back, 3);
            Assert.That(three.CrewCount, Is.EqualTo(3));
            three.gameObject.SetActive(false);
            Debug.Log($"SLINGER SIZES widths: 2 {widths[0]:F2} m, 4 {widths[1]:F2} m, 6 {widths[2]:F2} m, 8 {widths[3]:F2} m");
            for (var i = 1; i < widths.Length; i++)
            {
                Assert.That(widths[i], Is.GreaterThan(widths[i - 1]), "A bigger crew must have a wider tipi.");
            }

            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var camera = new GameObject("Sizes Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.farClipPlane = 300f;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            Directory.CreateDirectory(Output);
            for (var frame = 0; frame < 20; frame++)
            {
                yield return null;
            }

            camera.transform.position = new Vector3(8f, 11f, -11f);
            camera.transform.LookAt(new Vector3(8f, 2f, 4f));
            yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/sizes.png");
            camera.transform.position = new Vector3(2f, 6.5f, -4.5f);
            camera.transform.LookAt(new Vector3(2f, 2.4f, 4f));
            yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/small-two.png");
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(camera.gameObject);
            Object.Destroy(holder);
        }
    }
}
