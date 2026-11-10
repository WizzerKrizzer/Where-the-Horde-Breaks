using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using static TowerDefense.Tests.PrehistoricTowerTests;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Records the Spear Throwers' rock on the Level 1 road against goblins and brutes, with the
    /// poisoned tips, piercing and the big-game hunters on, from two cameras: the game's view and a
    /// close-up of the rock. Frames go to Logs/SpearRock. No game session or profile is involved.
    /// </summary>
    public sealed class SpearCaptureTests
    {
        private const string Output = "Logs/SpearRock";
        private readonly List<Object> cleanup = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            foreach (var item in cleanup)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            cleanup.Clear();
            yield return null;
        }

        [UnityTest, Explicit("Records the Spear Throwers' rock to Logs/SpearRock.")]
        public IEnumerator RecordSpearRock()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var spear = Object.Instantiate(world.Spear);
            cleanup.Add(spear);
            spear.pierce = 2;
            spear.pierceDamageKeep = 0.7f;
            spear.heavyDamageMultiplier = 2f;
            spear.poisonDamagePerSecond = 1f;
            spear.poisonSeconds = 5f;
            world.Towers.SetAvailableTowers(new[] { spear });
            var rock = new Vector3(-25f, 0f, 4.6f);
            Assert.That(world.Towers.TryPlace(spear, rock), Is.True);

            EnemyDefinition runner = null;
            EnemyDefinition brute = null;
            foreach (var entry in world.Wave.entries)
            {
                if (entry.enemy.id == "runner")
                {
                    runner = Object.Instantiate(entry.enemy);
                }
                else if (entry.enemy.id == "brute")
                {
                    brute = Object.Instantiate(entry.enemy);
                }
            }

            Assert.That(runner, Is.Not.Null);
            Assert.That(brute, Is.Not.Null);
            cleanup.Add(runner);
            cleanup.Add(brute);
            runner.maxHealth = 40f;
            brute.maxHealth = 300f;
            var wave = Object.Instantiate(world.Wave);
            cleanup.Add(wave);
            wave.entries = new[]
            {
                new WaveEntry { enemy = runner, count = 25 },
                new WaveEntry { enemy = brute, count = 2 },
                new WaveEntry { enemy = runner, count = 25 },
                new WaveEntry { enemy = brute, count = 2 },
                new WaveEntry { enemy = runner, count = 25 }
            };
            wave.totalEnemyCount = 79;

            var light = new GameObject("Test Sun").AddComponent<Light>();
            cleanup.Add(light.gameObject);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            light.shadows = LightShadows.Hard;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            cleanup.Add(ground);
            ground.transform.position = new Vector3(0f, -0.05f, 0f);
            ground.transform.localScale = new Vector3(10f, 1f, 6f);
            ground.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Ground);
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var wide = CreateCamera("Wide", rock + new Vector3(4f, 16f, -3f), rock + new Vector3(4f, 0f, 3f));
            var close = CreateCamera("Close", rock + new Vector3(2.5f, 8f, -3.5f), rock + new Vector3(2.5f, 0f, 2.2f));
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            cleanup.Add(target);
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            cleanup.Add(texture);
            wide.targetTexture = target;
            close.targetTexture = target;
            foreach (var name in new[] { "wide", "close" })
            {
                var folder = $"{Output}/{name}";
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }

                Directory.CreateDirectory(folder);
            }

            SpearVisuals.ResetCounters();
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            // Let the first goblins walk into range unrecorded.
            var deadline = Time.realtimeSinceStartup + 25f;
            while (SpearVisuals.DustCloudsSpawned < 1 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            const float frameSeconds = 1f / 30f;
            const int frames = 30 * 10;
            Time.timeScale = 0.2f;
            var next = Time.time;
            for (var frame = 0; frame < frames; frame++)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += frameSeconds;
                DrawHorde(world);
                yield return StyleLowPolyTests.Capture(wide, target, texture, $"{Output}/wide/frame_{frame:D4}.png");
                DrawHorde(world);
                yield return StyleLowPolyTests.Capture(close, target, texture, $"{Output}/close/frame_{frame:D4}.png");
            }

            Time.timeScale = 1f;
            File.WriteAllText($"{Output}/report.txt",
                $"dust {SpearVisuals.DustCloudsSpawned}, stuck {SpearVisuals.SpearsStuck}, heavy flashes {SpearVisuals.HeavyFlashesSpawned}, " +
                $"spawned {world.Enemies.TotalSpawned}, damage {world.Towers.Towers[0].DamageDealt:F0}");
        }

        private Camera CreateCamera(string name, Vector3 position, Vector3 lookAt)
        {
            var cameraObject = new GameObject($"Spear Camera {name}");
            cleanup.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
            return camera;
        }
    }
}
