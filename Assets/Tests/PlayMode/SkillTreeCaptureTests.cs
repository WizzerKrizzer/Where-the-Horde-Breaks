using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// A tool, not a check: opens the skill tree in the real game and saves pictures of it (the
    /// whole tree, the rock at the centre and each of the four tower branches) to
    /// Logs/SkillTreeShots, so the layout can be looked at away from the editor. Run it by name.
    /// </summary>
    public sealed class SkillTreeCaptureTests
    {
        private const int Width = 1920;
        private const int Height = 1080;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest, Explicit("Saves pictures of the skill tree; run by name.")]
        public IEnumerator CaptureSkillTree()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            var hud = Object.FindFirstObjectByType<RuntimeHud>();
            Assert.That(hud, Is.Not.Null, "No HUD in the Main scene.");

            // Draw the HUD through a camera into a texture: an overlay canvas is not drawn by any
            // camera, so it cannot be read back in batch mode.
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("SkillTreeShotCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.targetTexture = target;
            camera.enabled = false;
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            // Only the HUD: far from the level, and only the UI layer.
            const int uiLayer = 5;
            cameraObject.transform.position = new Vector3(0f, -5000f, 0f);
            camera.cullingMask = 1 << uiLayer;
            foreach (var child in hud.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = uiLayer;
            }

            typeof(RuntimeHud).GetMethod("ShowUpgradePanel", Private).Invoke(hud, null);
            yield return null;
            yield return null;

            var folder = Path.Combine(Application.dataPath, "..", "Logs", "SkillTreeShots");
            Directory.CreateDirectory(folder);
            var minimumZoom = (float)typeof(RuntimeHud).GetMethod("GetMinimumUpgradeTreeZoom", Private).Invoke(hud, null);
            // Branch centres in the HUD's tree space: the authored positions times 1.22.
            const float branch = 570f * 1.22f;
            const float closeZoom = 0.42f;
            // The game will not zoom out below 0.25, which is too close for the whole tree to fit
            // on a wide screen; the "whole" shots fit it for the picture only.
            var viewport = (RectTransform)typeof(RuntimeHud).GetField("upgradeTreeViewport", Private).GetValue(hud);
            var content = (RectTransform)typeof(RuntimeHud).GetField("upgradeTreeContent", Private).GetValue(hud);
            var fitZoom = Mathf.Min(viewport.rect.width / content.rect.width, viewport.rect.height / content.rect.height) * 0.95f;
            // The game shows a node once what it needs is bought; the "all" shots show every node
            // for this picture only (nothing is bought, the profile is not touched).
            var shots = new (string name, float zoom, Vector2 pan, bool all)[]
            {
                ("1_whole_tree_as_in_game", fitZoom, Vector2.zero, false),
                ("2_whole_tree_all_nodes", fitZoom, Vector2.zero, true),
                ("3_whole_tree_at_the_games_furthest_zoom", minimumZoom, Vector2.zero, true),
                ("4_centre_rock", 0.6f, Vector2.zero, true),
                ("5_left_slingers", closeZoom, new Vector2(branch * closeZoom, 0f), true),
                ("6_right_spear_throwers", closeZoom, new Vector2(-branch * closeZoom, 0f), true),
                ("7_up_mammoth", closeZoom, new Vector2(0f, -branch * closeZoom), true),
                ("8_down_cave", closeZoom, new Vector2(0f, branch * closeZoom), true),
                ("9_tooltip_fire_rock", 0.6f, Vector2.zero, false),
            };
            foreach (var shot in shots)
            {
                typeof(RuntimeHud).GetField("upgradeTreeZoom", Private).SetValue(hud, shot.zoom);
                typeof(RuntimeHud).GetField("upgradeTreePan", Private).SetValue(hud, shot.pan);
                typeof(RuntimeHud).GetMethod("ApplyUpgradeTreeTransform", Private).Invoke(hud, null);
                yield return null;
                if (shot.zoom < minimumZoom)
                {
                    content.localScale = Vector3.one * shot.zoom;
                    content.anchoredPosition = shot.pan;
                }
                if (shot.name.Contains("tooltip"))
                {
                    // Inspect the Fire Rock as if the mouse rested on it.
                    var session = Object.FindFirstObjectByType<GameSession>();
                    SkillNodeDefinition fire = null;
                    foreach (var node in session.UpgradeNodes)
                    {
                        fire = node.id == "rock_fire" ? node : fire;
                    }

                    typeof(RuntimeHud).GetField("hoveredUpgradeNode", Private).SetValue(hud, fire);
                    typeof(RuntimeHud).GetMethod("UpdateUpgradePanel", Private).Invoke(hud, null);
                }

                if (shot.all)
                {
                    foreach (var child in hud.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name.StartsWith("Node_") || child.name.StartsWith("NodeLabel_") || child.name.StartsWith("Link_"))
                        {
                            child.gameObject.SetActive(true);
                        }
                    }
                }

                Canvas.ForceUpdateCanvases();
                camera.Render();
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                RenderTexture.active = null;
                var path = Path.GetFullPath(Path.Combine(folder, shot.name + ".png"));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.Destroy(texture);
                Debug.Log($"SKILLTREESHOT {path}");
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Object.Destroy(cameraObject);
            target.Release();
        }
    }
}
