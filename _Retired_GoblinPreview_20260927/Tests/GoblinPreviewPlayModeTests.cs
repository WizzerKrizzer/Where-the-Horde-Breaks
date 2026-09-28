using System.Collections;
using System.IO;
using NUnit.Framework;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class GoblinPreviewPlayModeTests
    {
        [UnityTest]
        public IEnumerator RunningBake_RendersAndMovesOnTheExistingGpuBackend()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            var hadPreference = PlayerPrefs.HasKey(GoblinPreviewAssets.PreferenceKey);
            var preference = PlayerPrefs.GetInt(GoblinPreviewAssets.PreferenceKey, GoblinPreviewAssets.DefaultEnabled);
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            var oldCameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            var enabled = new bool[oldCameras.Length];
            for (var i = 0; i < oldCameras.Length; i++) { enabled[i] = oldCameras[i].enabled; oldCameras[i].enabled = false; }
            var cameraObject = new GameObject("Goblin preview validation camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.12f, 0.15f);
            camera.transform.position = new Vector3(0, 8, -2.14f);
            camera.transform.rotation = Quaternion.Euler(75, 0, 0);
            camera.fieldOfView = 50;
            var target = new RenderTexture(1280, 900, 24);
            camera.targetTexture = target;
            GpuHordeSimulation simulation = null;
            try
            {
                using var assets = GoblinPreviewAssets.TryLoad();
                Assert.That(assets, Is.Not.Null);
                Assert.That(GoblinPreviewAssets.VariantCount, Is.EqualTo(1));
                Assert.That(assets.Mesh.vertexCount, Is.EqualTo(11218));
                Assert.That(assets.Mesh.GetIndexCount(0), Is.EqualTo(45525));
                Assert.That(assets.LodMesh.GetIndexCount(0), Is.LessThan(6000));
                Assert.That(assets.LodMesh.vertexCount, Is.LessThan(2200));
                foreach (var vertex in assets.LodMesh.uv2)
                    Assert.That(vertex.x, Is.InRange(0, assets.Mesh.vertexCount - 1));
                Assert.That(Resources.Load<Shader>("HordeIndirect").isSupported, Is.True);
                var field = new HordeFlowField(new[] { new Vector3(-12, 0, 0), new Vector3(12, 0, 0) }, null, 5, 0.62f);
                Assert.That(GpuHordeSimulation.TryCreate(64, field, EnemyManager.GetLowEnemyMesh(), out simulation), Is.True);
                var states = new GpuHordeSimulation.AgentState[64];
                var controls = new Vector4[64];
                for (var i = 0; i < states.Length; i++)
                {
                    states[i] = new GpuHordeSimulation.AgentState
                    {
                        Position = new Vector2(-3.5f + (i % 8) * 0.8f, -2.8f + (i / 8) * 0.8f),
                        Velocity = new Vector2(1, 0), Scale = 0.34f, Status = 1,
                        Health = 100, MaxHealth = 100, SlowMultiplier = 1, Mass = 1
                    };
                    controls[i] = new Vector4(1, 1, 0, 0);
                }
                simulation.SpawnBatch(states, states.Length);
                var output = Path.GetFullPath("Logs/GoblinPreview");
                Directory.CreateDirectory(output);
                for (var frame = 0; frame < 60; frame++)
                {
                    simulation.Dispatch(1f / 60, controls, new Vector2[64]);
                    simulation.Draw(assets.Mesh, 0);
                    if (frame == 10 || frame == 40)
                    {
                        camera.Render();
                        var previous = RenderTexture.active;
                        RenderTexture.active = target;
                        var image = new Texture2D(1280, 900, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
                        image.Apply();
                        File.WriteAllBytes(Path.Combine(output, $"running-{frame}.png"), image.EncodeToPNG());
                        Object.Destroy(image);
                        RenderTexture.active = previous;
                    }
                    yield return null;
                }
                simulation.ReadStatesSynchronous(states, states.Length);
                Assert.That(states[0].Position.x, Is.GreaterThan(-3.5f));
                // Force the far tier, verify it actually draws the textured VAT
                // goblin and save a reviewable image of the gameplay-distance LOD.
                simulation.Dispatch(1f / 60f, controls, null, false);
                // Runtime refreshes visibility after the camera's LateUpdate. Move
                // the camera after the simulation tick to exercise that exact path.
                camera.transform.position = new Vector3(0, 25f, -6.7f);
                simulation.RefreshVisibilityForAudit();
                var lodArgs = new uint[5];
                simulation.ReadLodDrawArgsSynchronous(lodArgs);
                Assert.That(lodArgs[0], Is.EqualTo(assets.LodMesh.GetIndexCount(0)));
                Assert.That(lodArgs[1], Is.GreaterThan(0));
                simulation.Draw(assets.Mesh, 0);
                camera.Render();
                var lodActive = RenderTexture.active;
                RenderTexture.active = target;
                var lodImage = new Texture2D(1280, 900, TextureFormat.RGB24, false);
                lodImage.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
                lodImage.Apply();
                File.WriteAllBytes(Path.Combine(output, "running-lod.png"), lodImage.EncodeToPNG());
                Object.Destroy(lodImage);
                RenderTexture.active = lodActive;
                camera.transform.position = new Vector3(0, 8, -2.14f);
                for (var i = 0; i < states.Length; i++) Assert.That(states[i].Status, Is.EqualTo(1u));
                // Force a spread of phases across both attack clips. The production
                // compute shader sets bit 1 only while an agent owns a melee slot.
                for (var i = 0; i < states.Length; i++)
                {
                    states[i].Flags |= 2u;
                    states[i].AttackInterval = 1f;
                    states[i].AttackTimer = (i % 8) / 8f;
                }
                simulation.SpawnBatch(states, states.Length);
                simulation.Draw(assets.Mesh, 0);
                camera.Render();
                var oldActive = RenderTexture.active;
                RenderTexture.active = target;
                var attackImage = new Texture2D(1280, 900, TextureFormat.RGB24, false);
                attackImage.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
                attackImage.Apply();
                File.WriteAllBytes(Path.Combine(output, "attacking-phases.png"), attackImage.EncodeToPNG());
                Object.Destroy(attackImage);
                RenderTexture.active = oldActive;

                var featured = new[] { 0, 1, 2, 3 };
                for (var i = 0; i < states.Length; i++) states[i].Status = 0u;
                for (var i = 0; i < featured.Length; i++)
                {
                    var index = featured[i];
                    states[index].Status = 1u;
                    states[index].Position = new Vector2(0f, -1.05f + i * 0.7f);
                    states[index].Velocity = new Vector2(1f, 0f);
                    states[index].AttackTimer = 1f;
                }
                simulation.SpawnBatch(states, states.Length);
                camera.transform.position = new Vector3(3.5f, 1.65f, 0f);
                camera.transform.LookAt(new Vector3(0f, 1.15f, 0f));
                simulation.Draw(assets.Mesh, 0);
                camera.Render();
                oldActive = RenderTexture.active;
                RenderTexture.active = target;
                var closeImage = new Texture2D(1280, 900, TextureFormat.RGB24, false);
                closeImage.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
                closeImage.Apply();
                File.WriteAllBytes(Path.Combine(output, "attacking-close.png"), closeImage.EncodeToPNG());
                Object.Destroy(closeImage);
                RenderTexture.active = oldActive;
                // Review the blocked pose at the new closest gameplay zoom.
                for (var i = 0; i < states.Length; i++)
                {
                    states[i].Status = i < 2 ? 1u : 0u;
                    states[i].Flags = 0;
                    states[i].Position = new Vector2(-0.4f + i * 0.8f, 0);
                    states[i].Velocity = Vector2.zero;
                    states[i].KnockbackVelocity = Vector2.zero;
                    states[i].Facing = Vector2.down;
                    controls[i] = new Vector4(0, 1, 0, 0);
                }
                simulation.SpawnBatch(states, controls, 0, states.Length);
                camera.transform.rotation = Quaternion.Euler(75, 0, 0);
                camera.transform.position = new Vector3(0, 2.5f, -0.67f);
                for (var frame = 0; frame < 90; frame++)
                {
                    simulation.Dispatch(1f / 60, null, null, false);
                    simulation.Draw(assets.Mesh, 0);
                    if (frame == 45 || frame == 89)
                    {
                        camera.Render();
                        oldActive = RenderTexture.active;
                        RenderTexture.active = target;
                        var braceImage = new Texture2D(1280, 900, TextureFormat.RGB24, false);
                        braceImage.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
                        braceImage.Apply();
                        File.WriteAllBytes(Path.Combine(output, $"braced-close-{frame}.png"), braceImage.EncodeToPNG());
                        Object.Destroy(braceImage);
                        RenderTexture.active = oldActive;
                    }
                    if (frame % 15 == 0) yield return null;
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                simulation?.Dispose();
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(cameraObject);
                for (var i = 0; i < oldCameras.Length; i++) if (oldCameras[i] != null) oldCameras[i].enabled = enabled[i];
                PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, preference);
                if (!hadPreference) PlayerPrefs.DeleteKey(GoblinPreviewAssets.PreferenceKey);
            }
        }
    }
}
