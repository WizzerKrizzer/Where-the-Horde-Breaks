using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// The prehistoric towers on the Level 1 road with the GPU horde: Spear Throwers (they shoot like
    /// the Ballista) and the Tamed Mammoth (it charges along the road). No game session or player
    /// profile is involved, so nothing is saved.
    /// </summary>
    public sealed class PrehistoricTowerTests
    {
        private const string Output = "Logs/PrehistoricTowers";
        private readonly List<Object> cleanup = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureFramerate = 0;
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
        public IEnumerator SpearThrowers_KillGoblinsOnTheRoad()
        {
            RequireComputeShaders();
            var world = CreateWorld();
            var spears = world.Towers.TryPlace(world.Spear, new Vector3(-25f, 0f, 4.6f));
            Assert.That(spears, Is.True, "The Spear Throwers could not be placed beside the road.");
            var tower = world.Towers.Towers[0];
            var kills = 0;
            world.Enemies.EnemyKilled += _ => kills++;
            world.Enemies.BeginWave(world.Level.wave, world.Route, useDataHordePrototype: true);

            var deadline = Time.realtimeSinceStartup + 25f;
            while (kills < 3 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(tower.DamageDealt, Is.GreaterThan(0f), "The Spear Throwers never hit a goblin.");
            Assert.That(kills, Is.GreaterThanOrEqualTo(3), "The Spear Throwers did not kill goblins.");
        }

        [UnityTest]
        public IEnumerator Mammoth_ChargesAlongTheRoadAndScattersTheHorde()
        {
            RequireComputeShaders();
            var world = CreateWorld();
            var placed = world.Towers.TryPlace(world.Mammoth, new Vector3(-17.6f, 0f, 2f));
            Assert.That(placed, Is.True, "The mammoth's pen could not be placed beside the road.");
            var tower = world.Towers.Towers[0];
            var charge = tower.GetComponent<BeastCharge>();
            Assert.That(charge, Is.Not.Null, "The mammoth tower has no beast.");
            world.Enemies.BeginWave(world.Level.wave, world.Route, useDataHordePrototype: true);

            // The fastest goblin while the mammoth runs: knocked goblins fly much faster than they walk.
            var fastest = 0f;
            var onRoad = true;
            // Frames out of the pen, and those with a goblin inside the beast's body.
            var outFrames = 0;
            var overlapFrames = 0;
            var deadline = Time.realtimeSinceStartup + 30f;
            while (charge.ChargeCount == 0 || charge.IsOut)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    break;
                }

                if (charge.IsCharging)
                {
                    fastest = Mathf.Max(fastest, FastestAgent(world));
                    onRoad &= world.Route.IsInsideCorridor(charge.BeastPosition, 0.5f);
                }

                if (charge.IsOut)
                {
                    outFrames++;
                    if (AgentsWithin(world, charge.BeastPosition, 0.7f) > 0)
                    {
                        overlapFrames++;
                    }
                }

                yield return null;
            }

            Assert.That(charge.ChargeCount, Is.GreaterThan(0), "The mammoth never charged.");
            Assert.That(onRoad, Is.True, "The mammoth left the road while charging.");
            Assert.That(tower.DamageDealt, Is.GreaterThan(0f), "The mammoth's charge hurt no goblin.");
            var walking = 0f;
            foreach (var entry in world.Level.wave.entries)
            {
                walking = Mathf.Max(walking, entry.enemy.speed);
            }

            Assert.That(fastest, Is.GreaterThan(walking * 1.5f), "The charge did not knock goblins aside.");
            Debug.Log($"Mammoth: {overlapFrames} of {outFrames} frames out of the pen had a goblin inside its body.");
            Assert.That(overlapFrames, Is.LessThanOrEqualTo(outFrames / 10), "Goblins walk through the mammoth.");
        }

        [UnityTest, Explicit("Records the prehistoric towers on the Level 1 road to Logs/PrehistoricTowers/normal.")]
        public IEnumerator RecordPrehistoricTowers()
        {
            return Record("normal", 1, 1f);
        }

        // Ten times the Level 1 horde, and the mammoth charging twice as often (user, 2026-11-07).
        [UnityTest, Explicit("Records the prehistoric towers against a ten times larger horde to Logs/PrehistoricTowers/dense.")]
        public IEnumerator RecordPrehistoricTowersDenseHorde()
        {
            return Record("dense", 10, 2f);
        }

        private IEnumerator Record(string name, int hordeFactor, float chargeRateFactor)
        {
            RequireComputeShaders();
            var world = CreateWorld();
            world.Mammoth.fireInterval /= chargeRateFactor;
            var wave = Object.Instantiate(world.Level.wave);
            cleanup.Add(wave);
            wave.totalEnemyCount *= hordeFactor;
            wave.randomSpawnBurstMin *= hordeFactor;
            wave.randomSpawnBurstMax *= hordeFactor;
            if (wave.spawnBurstPattern != null)
            {
                for (var i = 0; i < wave.spawnBurstPattern.Length; i++)
                {
                    wave.spawnBurstPattern[i] *= hordeFactor;
                }
            }

            wave.entries = (WaveEntry[])wave.entries.Clone();
            for (var i = 0; i < wave.entries.Length; i++)
            {
                wave.entries[i].count *= hordeFactor;
            }

            Assert.That(world.Towers.TryPlace(world.Spear, new Vector3(-25f, 0f, 4.6f)), Is.True);
            Assert.That(world.Towers.TryPlace(world.Mammoth, new Vector3(-17.6f, 0f, 2f)), Is.True);

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

            var cameraObject = new GameObject("Test Camera");
            cleanup.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.transform.position = new Vector3(-16f, 30f, -18f);
            camera.transform.LookAt(new Vector3(-16f, 0f, 2f));
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            cleanup.Add(target);
            camera.targetTexture = target;
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            cleanup.Add(texture);

            var folder = $"{Output}/{name}/frames";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            Time.captureFramerate = 30;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            for (var frame = 0; frame < 900; frame++)
            {
                // The horde draws itself in LateUpdate, after this coroutine; draw it for this capture.
                DrawHorde(world);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine($"horde x{hordeFactor}: {wave.totalEnemyCount} goblins, {world.Enemies.TotalSpawned} spawned in 30 s; mammoth charges every {world.Mammoth.fireInterval:F1} s");
            foreach (var tower in world.Towers.Towers)
            {
                var charge = tower.GetComponent<BeastCharge>();
                report.AppendLine($"{tower.Definition.displayName} at {tower.Position}: damage {tower.DamageDealt:F0}" +
                                  (charge != null ? $", charges {charge.ChargeCount}" : string.Empty));
            }

            File.WriteAllText($"{Output}/{name}/report.txt", report.ToString());
        }

        private sealed class World
        {
            public LevelDefinition Level;
            public PathRoute Route;
            public EnemyManager Enemies;
            public HordeEnemyManager Horde;
            public TowerManager Towers;
            public TowerDefinition Spear;
            public TowerDefinition Mammoth;
        }

        private World CreateWorld()
        {
            var content = SampleContent.Create();
            var world = new World { Level = content.Level };
            foreach (var definition in content.Towers)
            {
                if (definition.id == "spear_thrower")
                {
                    world.Spear = definition;
                }
                else if (definition.id == "mammoth")
                {
                    world.Mammoth = definition;
                }
            }

            Assert.That(world.Spear, Is.Not.Null, "No Spear Throwers in the game's towers.");
            Assert.That(world.Mammoth, Is.Not.Null, "No Tamed Mammoth in the game's towers.");

            var routeObject = new GameObject("Test Route");
            cleanup.Add(routeObject);
            world.Route = routeObject.AddComponent<PathRoute>();
            var widths = new float[world.Level.pathWaypoints.Length];
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = world.Level.roadWidth;
            }

            world.Route.SetWaypoints(world.Level.pathWaypoints, widths);
            world.Route.SetSquareCorners(world.Level.useSquarePathCorners);
            RoadSurface.Create(routeObject.transform, world.Route, world.Level);

            var hordeObject = new GameObject("Test Horde");
            cleanup.Add(hordeObject);
            world.Horde = hordeObject.AddComponent<HordeEnemyManager>();
            var enemiesObject = new GameObject("Test Enemies");
            cleanup.Add(enemiesObject);
            world.Enemies = enemiesObject.AddComponent<EnemyManager>();
            world.Enemies.SetHordePrototype(world.Horde);

            var towersObject = new GameObject("Test Towers");
            cleanup.Add(towersObject);
            world.Towers = towersObject.AddComponent<TowerManager>();
            world.Towers.Initialize(world.Enemies, world.Route, new[] { world.Spear, world.Mammoth });
            return world;
        }

        private static GpuHordeSimulation Simulation(World world)
        {
            return (GpuHordeSimulation)typeof(HordeEnemyManager)
                .GetField("gpuSimulation", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(world.Horde);
        }

        private static void DrawHorde(World world)
        {
            var simulation = Simulation(world);
            var mesh = (Mesh)typeof(HordeEnemyManager)
                .GetField("mesh", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(world.Horde);
            if (simulation == null || mesh == null)
            {
                return;
            }

            simulation.RefreshVisibilityForRendering();
            simulation.Draw(mesh, world.Horde.gameObject.layer);
        }

        private static int AgentsWithin(World world, Vector3 center, float radius)
        {
            var simulation = Simulation(world);
            var count = world.Enemies.TotalSpawned;
            if (simulation == null || count <= 0)
            {
                return 0;
            }

            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var inside = 0;
            foreach (var state in states)
            {
                if (state.Health > 0f && Vector2.Distance(state.Position, new Vector2(center.x, center.z)) < radius)
                {
                    inside++;
                }
            }

            return inside;
        }

        private static float FastestAgent(World world)
        {
            var simulation = Simulation(world);
            var count = world.Enemies.TotalSpawned;
            if (simulation == null || count <= 0)
            {
                return 0f;
            }

            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var fastest = 0f;
            foreach (var state in states)
            {
                fastest = Mathf.Max(fastest, (state.Velocity + state.KnockbackVelocity).magnitude);
            }

            return fastest;
        }

        private static void RequireComputeShaders()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }
        }
    }
}
