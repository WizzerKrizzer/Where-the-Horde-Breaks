#if GOBLIN_VISUAL_PREVIEW
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace TowerDefense.Runtime
{
    // Compiled only into the isolated visual-test player, never the normal game.
    [DefaultExecutionOrder(10000)]
    public sealed class GoblinPreviewSession : MonoBehaviour
    {
        private bool capturePending;
        private bool gallery;
        private string captureName = "in-game";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install() => new GameObject("Goblin Preview Controls").AddComponent<GoblinPreviewSession>();

        private IEnumerator Start()
        {
            yield return null;
            var session = FindAnyObjectByType<GameSession>();
            var camera = Camera.main;
            if (session == null || camera == null) yield break;
            // Keep the visual-test player uncapped so frame-rate checks are not
            // mistaken for preview throttling.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 40f;
            QualitySettings.shadowCascades = 1;
            foreach (var sun in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (sun.type != LightType.Directional) continue;
                sun.shadows = LightShadows.None;
                sun.shadowStrength = 0.12f;
                sun.shadowBias = 0.025f;
                sun.shadowNormalBias = 0.15f;
                sun.color = new Color(1f, 0.95f, 0.85f);
                sun.intensity = 1.1f;
                RenderSettings.sun = sun;
                break;
            }
            var route = session.Level.pathWaypoints;
            if (route != null && route.Length >= 2)
            {
                var focus = route[0] + (route[1] - route[0]).normalized * 8f;
                camera.transform.position = focus + new Vector3(0, 10, -2.6795f);
            }
            gallery = Array.IndexOf(Environment.GetCommandLineArgs(), "-captureGoblinGallery") >= 0;
            if (!gallery && Array.IndexOf(Environment.GetCommandLineArgs(), "-captureGoblinPreview") < 0) yield break;
            session.StartLevel();
            yield return new WaitForSeconds(2f);
            if (gallery)
            {
                // Capture-only camera changes; normal interactive gameplay is untouched.
                var controller = FindAnyObjectByType<TopDownCameraController>();
                if (controller != null) controller.enabled = false;
                var focus = camera.transform.position + camera.transform.forward * (camera.transform.position.y / -camera.transform.forward.y);
                var horde = FindAnyObjectByType<HordeEnemyManager>();
                if (horde != null && horde.TryGetLeadAimPoint(0, out var lead)) focus = lead;
                var distances = new[] { 24f / Mathf.Sin(75 * Mathf.Deg2Rad), 10f / Mathf.Sin(75 * Mathf.Deg2Rad), 3.2f };
                var pitches = new[] { 75f, 75f, 55f };
                var names = new[] { "gallery-wide", "gallery-gameplay", "gallery-close" };
                for (var i = 0; i < names.Length; i++)
                {
                    captureName = names[i];
                    camera.transform.rotation = Quaternion.Euler(pitches[i], 0, 0);
                    camera.transform.position = focus + Vector3.up * (i == 2 ? 0.4f : 0) - camera.transform.forward * distances[i];
                    // Allow the GPU visibility pass to use the new view before drawing.
                    yield return new WaitForSeconds(0.04f);
                    capturePending = true;
                    while (capturePending) yield return null;
                }
                Application.Quit();
                yield break;
            }
            capturePending = true;
        }

        private void LateUpdate()
        {
            if (!capturePending) return;
            capturePending = false;
            var camera = Camera.main;
            var horde = FindAnyObjectByType<HordeEnemyManager>();
            Debug.Log($"Goblin capture: spawned={horde?.TotalSpawned}, active={horde?.ActiveCount}, renderer={horde?.Performance}");
            var target = new RenderTexture(1280, 900, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            camera.targetTexture = target;
            // Run after HordeEnemyManager.LateUpdate has submitted indirect draws.
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1280, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
            image.Apply();
            Directory.CreateDirectory("Logs/GoblinPreview");
            File.WriteAllBytes($"Logs/GoblinPreview/{captureName}.png", image.EncodeToPNG());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release();
            Destroy(target);
            Destroy(image);
            if (!gallery) Application.Quit();
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(Screen.width / 2f - 250, Screen.height - 36, 500, 28),
                "GOBLIN VISUAL TEST | max 150 | Space: start | R: restart | wheel: zoom");
        }
    }
}
#endif
