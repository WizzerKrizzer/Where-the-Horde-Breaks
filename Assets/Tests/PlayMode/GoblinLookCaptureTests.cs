using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Readability test for the goblin's colours and head mark (user, 2026-11-10): the low-poly
    /// crowd from the style test, recorded once per look at a close and a far camera height.
    /// Writes frames to Logs/GoblinLooks/{height}/{look}; ffmpeg lays them out side by side.
    /// </summary>
    public sealed class GoblinLookCaptureTests
    {
        private const string Output = "Logs/GoblinLooks";
        private const int Frames = 120;

        [UnityTest, Explicit("Records the goblin look variants in a dense crowd.")]
        public IEnumerator RecordGoblinLooks()
        {
            var unitShader = Shader.Find("TowerDefense/StyleLowPoly");
            var groundShader = Shader.Find("TowerDefense/StyleGround");
            Assert.That(unitShader, Is.Not.Null);
            Assert.That(groundShader, Is.Not.Null);

            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                renderer.enabled = false;
            }

            var cameraObject = new GameObject("Look camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = StyleLowPolyTests.Ground;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2000f;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24, RenderTextureFormat.ARGB32)
                { antiAliasing = 8 };
            camera.targetTexture = target;
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);

            var groundMesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1000f, 0f, -1000f), new Vector3(-1000f, 0f, 1000f),
                    new Vector3(1000f, 0f, 1000f), new Vector3(1000f, 0f, -1000f)
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var ground = new GameObject("Look ground");
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            var groundMaterial = new Material(groundShader);
            groundMaterial.SetColor("_Color", StyleLowPolyTests.Ground);
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var unitMaterial = new Material(unitShader) { enableInstancing = true };
            unitMaterial.SetFloat("_CycleRate", 1.9f);
            var staticMaterial = new Material(unitShader) { enableInstancing = true };
            staticMaterial.SetFloat("_Animate", 0f);
            var towerPositions = StyleLowPolyTests.TowerPositions();
            var tower = LowPolyMeshes.Tower();
            var terrain = LowPolyMeshes.Terrain(StyleLowPolyTests.RoadCenter, towerPositions);
            var crowd = StyleLowPolyTests.BuildCrowd();

            var brightSkin = new Color(0.58f, 0.8f, 0.22f);
            var brightSkinDark = new Color(0.4f, 0.6f, 0.14f);
            var darkHide = new Color(0.27f, 0.16f, 0.1f);
            var looks = new Dictionary<string, LowPolyMeshes.GoblinLook>
            {
                ["a"] = LowPolyMeshes.GoblinLook.Current,
                ["b"] = new LowPolyMeshes.GoblinLook { Skin = brightSkin, SkinDark = brightSkinDark, Hide = darkHide },
                ["c"] = new LowPolyMeshes.GoblinLook
                {
                    Skin = LowPolyMeshes.GoblinLook.Current.Skin, SkinDark = LowPolyMeshes.GoblinLook.Current.SkinDark,
                    Hide = LowPolyMeshes.GoblinLook.Current.Hide, Mark = new Color(0.86f, 0.16f, 0.1f), MarkShape = 1
                },
                ["d"] = new LowPolyMeshes.GoblinLook
                {
                    Skin = brightSkin, SkinDark = brightSkinDark, Hide = darkHide,
                    Mark = new Color(0.95f, 0.92f, 0.8f), MarkShape = 2
                }
            };

            foreach (var height in new[] { 20f, 50f })
            {
                StyleLowPolyTests.PlaceCamera(camera, height, StyleLowPolyTests.Target);
                foreach (var look in looks)
                {
                    var goblin = LowPolyMeshes.Goblin(look.Value);
                    var folder = $"{Output}/{height:F0}m/{look.Key}";
                    Directory.CreateDirectory(folder);
                    for (var frame = 0; frame < Frames; frame++)
                    {
                        var time = frame / 30f;
                        Shader.SetGlobalFloat("_StyleTime", time);
                        foreach (var position in towerPositions)
                        {
                            Graphics.DrawMesh(tower, Matrix4x4.Translate(position), staticMaterial, 0);
                        }

                        Graphics.DrawMesh(terrain, Matrix4x4.identity, staticMaterial, 0);
                        StyleLowPolyTests.DrawCrowd(goblin, unitMaterial, crowd, time);
                        yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D3}.png");
                    }

                    Object.DestroyImmediate(goblin);
                }
            }

            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(groundMesh);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(unitMaterial);
            Object.DestroyImmediate(staticMaterial);
            Object.DestroyImmediate(tower);
            Object.DestroyImmediate(terrain);
        }
    }
}
