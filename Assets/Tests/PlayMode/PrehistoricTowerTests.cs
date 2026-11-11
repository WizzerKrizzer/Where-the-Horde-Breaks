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
        private const string Output = "Logs/Captures/PrehistoricTowers";
        private readonly List<Object> cleanup = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureFramerate = 0;
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
        public IEnumerator SpearThrowers_KillGoblinsOnTheRoad()
        {
            RequireComputeShaders();
            var world = CreateWorld();
            var spears = world.Towers.TryPlace(world.Spear, new Vector3(-25f, 0f, 4.6f));
            Assert.That(spears, Is.True, "The Spear Throwers could not be placed beside the road.");
            var tower = world.Towers.Towers[0];
            var kills = 0;
            world.Enemies.EnemyKilled += _ => kills++;
            world.Enemies.BeginWave(world.Wave, world.Route, useDataHordePrototype: true);

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
            world.Enemies.BeginWave(world.Wave, world.Route, useDataHordePrototype: true);

            // The fastest goblin while the mammoth runs: knocked goblins fly much faster than they walk.
            var fastest = 0f;
            // The most goblins in the air at once: the trample throws them over the crowd.
            var thrown = 0;
            var onRoad = true;
            // Frames out of the pen, and those with a goblin inside the beast's body.
            var outFrames = 0;
            var overlapFrames = 0;
            // One whole turn: the charge, the fight and the fall.
            var deadline = Time.realtimeSinceStartup + 60f;
            var fought = false;
            while (charge.ChargeCount == 0 || charge.IsOut)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    break;
                }

                if (charge.IsCharging)
                {
                    fastest = Mathf.Max(fastest, FastestAgent(world));
                    thrown = Mathf.Max(thrown, AirborneCount(world));
                    onRoad &= world.Route.IsInsideCorridor(charge.BeastPosition, 0.5f);
                }

                fought |= charge.IsFighting;
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
            Debug.Log($"Mammoth: ran {charge.LastRunDistance:F1} m before it stopped to fight; fell {charge.FallCount} times.");
            Assert.That(fought, Is.True, "The mammoth never stopped to fight.");
            Assert.That(charge.FallCount, Is.GreaterThan(0), "The mammoth never fell.");
            Assert.That(onRoad, Is.True, "The mammoth left the road while charging.");
            Assert.That(tower.DamageDealt, Is.GreaterThan(0f), "The mammoth's charge hurt no goblin.");
            var walking = 0f;
            foreach (var entry in world.Wave.entries)
            {
                walking = Mathf.Max(walking, entry.enemy.speed);
            }

            // The throw tops out at about 8 m/s (1.5x walking), so the check leaves some room below it.
            Assert.That(fastest, Is.GreaterThan(walking * 1.3f), "The charge did not knock goblins aside.");
            Assert.That(thrown, Is.GreaterThan(0), "The charge threw no goblin into the air.");
            Debug.Log($"Mammoth: {overlapFrames} of {outFrames} frames out of the pen had a goblin inside its body.");
            Assert.That(overlapFrames, Is.LessThanOrEqualTo(outFrames / 10), "Goblins walk through the mammoth.");
        }

        [UnityTest, Explicit("Records the prehistoric towers on the Level 1 road to Logs/Captures/PrehistoricTowers/normal.")]
        public IEnumerator RecordPrehistoricTowers()
        {
            return Record("normal", 1, 1f);
        }

        // Ten times the Level 1 horde, and the mammoth charging twice as often (user, 2026-11-07).
        [UnityTest, Explicit("Records the prehistoric towers against a ten times larger horde to Logs/Captures/PrehistoricTowers/dense.")]
        public IEnumerator RecordPrehistoricTowersDenseHorde()
        {
            return Record("dense", 10, 2f);
        }

        private IEnumerator Record(string name, int hordeFactor, float chargeRateFactor)
        {
            RequireComputeShaders();
            var world = CreateWorld();
            world.Mammoth.fireInterval /= chargeRateFactor;
            var wave = Object.Instantiate(world.Wave);
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
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            // How far each charge ran before the crowd stopped it.
            var beast = world.Towers.Towers[1].GetComponent<BeastCharge>();
            var runs = new List<string>();
            var wasFighting = false;
            yield return RecordFrames(world, camera, target, texture, folder, 900, frame =>
            {
                if (beast.IsFighting && !wasFighting)
                {
                    runs.Add($"{beast.LastRunDistance:F1} m at {frame / 30f:F1} s");
                }

                wasFighting = beast.IsFighting;
            });

            var report = new System.Text.StringBuilder();
            report.AppendLine($"horde x{hordeFactor}: {wave.totalEnemyCount} goblins, {world.Enemies.TotalSpawned} spawned in 30 s; mammoth charges every {world.Mammoth.fireInterval:F1} s");
            foreach (var tower in world.Towers.Towers)
            {
                var charge = tower.GetComponent<BeastCharge>();
                report.AppendLine($"{tower.Definition.displayName} at {tower.Position}: damage {tower.DamageDealt:F0}" +
                                  (charge != null ? $", charges {charge.ChargeCount}, falls {charge.FallCount}" : string.Empty));
            }

            report.AppendLine("mammoth runs: " + string.Join(", ", runs));

            File.WriteAllText($"{Output}/{name}/report.txt", report.ToString());
        }

        internal sealed class World
        {
            public LevelDefinition Level;
            // The former Level 1 wave: 170 Goblin Runners and 45 Orc Brutes.
            public WaveDefinition Wave;
            public PathRoute Route;
            public EnemyManager Enemies;
            public HordeEnemyManager Horde;
            public TowerManager Towers;
            public TowerDefinition Spear;
            public TowerDefinition Mammoth;
            public TowerDefinition Barrier;
        }

        private World CreateWorld()
        {
            return CreateWorld(cleanup);
        }

        // The Level 1 road with the GPU horde and a tower manager offering the Spear Throwers, the
        // Tamed Mammoth and the Timber Barrier. Everything created is added to cleanup.
        internal static World CreateWorld(List<Object> cleanup)
        {
            var content = SampleContent.Create();
            var world = new World { Level = content.Level, Wave = content.LevelOneTestWave };
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
                else if (definition.id == "barrier")
                {
                    world.Barrier = definition;
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
            // As in the game, the road is known before the wave (cavemen walk out to it).
            world.Enemies.SetLevelRoute(world.Route);

            var towersObject = new GameObject("Test Towers");
            cleanup.Add(towersObject);
            world.Towers = towersObject.AddComponent<TowerManager>();
            world.Towers.Initialize(world.Enemies, world.Route, new[] { world.Spear, world.Mammoth, world.Barrier });
            return world;
        }

        // Captures frames 1/30 s of game time apart. The GPU horde runs on real time scaled by
        // Time.timeScale (not on Time.captureFramerate), so the game is slowed down to leave time for
        // each capture and everything moves at its true speed in the video.
        internal static IEnumerator RecordFrames(World world, Camera camera, RenderTexture target, Texture2D texture, string folder,
            int frames, System.Action<int> beforeFrame = null)
        {
            const float frameSeconds = 1f / 30f;
            Time.timeScale = 0.2f;
            var next = Time.time;
            for (var frame = 0; frame < frames; frame++)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += frameSeconds;
                beforeFrame?.Invoke(frame);
                // The horde draws itself in LateUpdate, after this coroutine; draw it for this capture.
                DrawHorde(world);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
            }

            Time.timeScale = 1f;
        }

        internal static GpuHordeSimulation Simulation(World world)
        {
            return (GpuHordeSimulation)typeof(HordeEnemyManager)
                .GetField("gpuSimulation", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(world.Horde);
        }

        internal static void DrawHorde(World world)
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

        internal static int AgentsWithin(World world, Vector3 center, float radius)
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

        internal static int AirborneCount(World world)
        {
            var simulation = Simulation(world);
            var count = world.Enemies.TotalSpawned;
            if (simulation == null || count <= 0)
            {
                return 0;
            }

            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var inAir = 0;
            foreach (var state in states)
            {
                if (state.Status == 1u && (state.AirHeight > 0f || state.AirSpeed > 0f))
                {
                    inAir++;
                }
            }

            return inAir;
        }

        internal static float FastestAgent(World world)
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

        internal static void RequireComputeShaders()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }
        }
    }
}
