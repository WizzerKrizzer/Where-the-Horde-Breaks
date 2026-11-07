using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using static TowerDefense.Tests.PrehistoricTowerTests;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Blasts in a jammed road. A blast throws the goblins near it into the air: they fly over the
    /// crowd without contacts and land a few metres away, where their contacts come back gradually
    /// (HordeSimulation.compute, LaunchIntoAir). The jam is made by an indestructible Timber Barrier
    /// across the Level 1 road and a ten times larger Level 1 horde.
    /// </summary>
    public sealed class HordeBlastTests
    {
        private const string Output = "Logs/HordeBlast";
        // The barrier stands across the road below the top-left bend; the blast goes off in the
        // queue behind it.
        private static readonly Vector3 BarrierPosition = new(-13.2f, 0f, -4f);
        private static readonly Vector3 BlastCenter = new(-13.2f, 0f, 6f);
        private const float BlastRadius = 2.5f;
        private const float BlastKnockback = 1.6f;
        private const float BlastLaunch = 1.1f;

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

        [UnityTest]
        public IEnumerator Blast_ThrowsGoblinsOverAJammedRoadAndTheyLandSoftly()
        {
            RequireComputeShaders();
            var world = CreateJammedRoad();
            var deadline = Time.realtimeSinceStartup + 40f;
            while (AgentsWithin(world, BlastCenter, BlastRadius) < 12)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The road never jammed at the blast point.");
                yield return null;
            }

            var before = FastestAgent(world);
            Blast(world);
            var highest = 0f;
            var mostInAir = 0;
            var landedAt = -1f;
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 4f)
            {
                var (inAir, height) = Airborne(world);
                highest = Mathf.Max(highest, height);
                mostInAir = Mathf.Max(mostInAir, inAir);
                if (mostInAir > 0 && inAir == 0 && landedAt < 0f)
                {
                    landedAt = Time.realtimeSinceStartup - start;
                }

                yield return null;
            }

            var settled = FastestAgent(world);
            Debug.Log($"Blast: {mostInAir} goblins in the air at once, highest {highest:F2} m, all down after {landedAt:F2} s, fastest goblin before {before:F2} m/s and afterwards {settled:F2} m/s.");
            Assert.That(mostInAir, Is.GreaterThanOrEqualTo(5), "The blast threw too few goblins.");
            Assert.That(highest, Is.GreaterThan(0.5f), "Thrown goblins barely left the ground.");
            Assert.That(landedAt, Is.InRange(0f, 3f), "Thrown goblins did not all land.");
            // Landing on the crowd must not shove the neighbours away like a second blast.
            Assert.That(settled, Is.LessThan(before + 2f), "The crowd was still flying apart after the landing.");
        }

        [UnityTest, Explicit("Records blasts in a jammed road to Logs/HordeBlast.")]
        public IEnumerator RecordBlastsInAJammedRoad()
        {
            RequireComputeShaders();
            var world = CreateJammedRoad();
            var light = new GameObject("Test Sun").AddComponent<Light>();
            cleanup.Add(light.gameObject);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            cleanup.Add(ground);
            ground.transform.position = new Vector3(0f, -0.05f, 0f);
            ground.transform.localScale = new Vector3(10f, 1f, 6f);
            ground.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Ground);
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            // Low and from the side, so the arcs show.
            var cameraObject = new GameObject("Test Camera");
            cleanup.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.transform.position = BlastCenter + new Vector3(8f, 6.5f, -8f);
            camera.transform.LookAt(BlastCenter + new Vector3(0f, 0.5f, 0f));
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            cleanup.Add(target);
            camera.targetTexture = target;
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            cleanup.Add(texture);

            var folder = Output + "/frames";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            // Let the jam form unrecorded, then record a blast every 2.5 seconds.
            var deadline = Time.realtimeSinceStartup + 40f;
            while (AgentsWithin(world, BlastCenter, BlastRadius) < 12 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            // The front of the horde has reached the blast point; let the queue pack in behind it.
            var packed = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < packed)
            {
                yield return null;
            }

            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cleanup.Add(flash);
            flash.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Accent);
            flash.SetActive(false);
            var blastFrame = -100;
            yield return RecordFrames(world, camera, target, texture, folder, 30 * 12, frame =>
            {
                if (frame % 75 == 15)
                {
                    Blast(world);
                    blastFrame = frame;
                }

                // A white puff for a quarter of a second.
                var age = (frame - blastFrame) / 30f;
                flash.SetActive(age < 0.25f);
                flash.transform.position = BlastCenter + Vector3.up * 0.3f;
                flash.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, BlastRadius * 1.6f, age / 0.25f);
            });
        }

        private World CreateJammedRoad()
        {
            var world = CreateWorld(cleanup);
            world.Barrier.health = 1000000f;
            Assert.That(world.Towers.TryPlace(world.Barrier, BarrierPosition), Is.True, "The barrier could not be placed on the road.");
            var wave = Object.Instantiate(world.Level.wave);
            cleanup.Add(wave);
            wave.totalEnemyCount *= 10;
            wave.randomSpawnBurstMin *= 10;
            wave.randomSpawnBurstMax *= 10;
            wave.entries = (Data.WaveEntry[])wave.entries.Clone();
            for (var i = 0; i < wave.entries.Length; i++)
            {
                wave.entries[i].count *= 10;
            }

            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            return world;
        }

        private static void Blast(World world)
        {
            // Up to 200 goblins, like a catapult's splash: knockback speed falls from 8x to 2.5x the
            // knockback from the centre to the edge.
            world.Enemies.DamageAndKnockbackInRadius(BlastCenter, BlastRadius, 0f, BlastKnockback, 200, out _, launch: BlastLaunch);
        }

        private static (int inAir, float highest) Airborne(World world)
        {
            var simulation = PrehistoricTowerTests.Simulation(world);
            var count = world.Enemies.TotalSpawned;
            if (simulation == null || count <= 0)
            {
                return (0, 0f);
            }

            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var inAir = 0;
            var highest = 0f;
            foreach (var state in states)
            {
                if (state.Status == 1u && (state.AirHeight > 0f || state.AirSpeed > 0f))
                {
                    inAir++;
                    highest = Mathf.Max(highest, state.AirHeight);
                }
            }

            return (inAir, highest);
        }
    }
}
