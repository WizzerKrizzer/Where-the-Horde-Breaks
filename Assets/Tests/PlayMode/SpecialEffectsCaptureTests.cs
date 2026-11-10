using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using static TowerDefense.Tests.PrehistoricTowerTests;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Records the special upgrades' effects on the Level 1 road (user, 2026-11-09: "improve the
    /// effects"), one folder each under Logs/SpecialEffects: the slingers' stunning, bouncing and
    /// bursting stones, the rock's quake, fire trail and shattering, the raging mammoth, and the
    /// cave's torches, shields and chief. Frames are 1/30 s of game time apart.
    /// </summary>
    public sealed class SpecialEffectsCaptureTests
    {
        private const string Output = "Logs/SpecialEffects";
        private static readonly Vector3 TowerSpot = new(-25f, 0f, 4.6f);
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

        [UnityTest, Explicit("Records the slingers' specials to Logs/SpecialEffects/slingers.")]
        public IEnumerator RecordSlingerSpecials()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var slingers = FindTower("slingers");
            slingers.damage = 1f;
            slingers.stunChance = 0.5f;
            slingers.stunSeconds = 3f;
            slingers.ricochetBounces = 2;
            slingers.ricochetDamageFraction = 0.6f;
            slingers.shrapnelTargets = 3;
            slingers.shrapnelDamageFraction = 0.4f;
            world.Towers.SetAvailableTowers(new[] { slingers });
            Assert.That(world.Towers.TryPlace(slingers, TowerSpot), Is.True);
            var wave = OnlyEnemy(world, "runner", 90, 400f);
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitUntil(() => Stunned(world) > 0, 25f);
            var road = world.Route.GetNearestRoadPoint(TowerSpot, out _);
            var camera = Scene(road + new Vector3(0f, 7.5f, -5.5f), road);
            yield return Record(world, camera, "slingers", 30 * 6);
        }

        [UnityTest, Explicit("Records the rock's specials to Logs/SpecialEffects/rock.")]
        public IEnumerator RecordRockSpecials()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var wave = OnlyEnemy(world, "runner", 120, 400f);
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitUntil(() => world.Enemies.TotalSpawned >= 120, 25f);
            yield return new WaitForSeconds(1.5f);
            var point = world.Route.Sample(12f);
            var settings = new RollingRock.Settings
            {
                ImpactRadius = 2.5f, ImpactDamage = 1f, ImpactMaxTargets = 40, ImpactKnockback = 1f, ImpactLaunch = 0.6f,
                RollDistance = 6f, RollSpeed = 4f, RollRadius = 1.3f, RollDamage = 1f, RollKnockback = 0.6f,
                FireTrail = true, FireDamagePerTick = 1f, FireTicksPerSecond = 2f,
                QuakeRadius = 4.5f, QuakeSeconds = 3f,
                ShatterShards = 6, ShatterDamage = 5f
            };
            var ahead = world.Route.Sample(16f) - point;
            ahead.y = 0f;
            var camera = Scene(point + ahead * 0.4f + new Vector3(0f, 11f, -8f), point + ahead * 0.4f);
            var rock = RollingRock.Throw(world.Enemies, point, ahead.normalized * 3f, settings);
            cleanup.Add(rock.gameObject);
            yield return Record(world, camera, "rock", 30 * 6);
        }

        [UnityTest, Explicit("Records the raging mammoth to Logs/SpecialEffects/mammoth.")]
        public IEnumerator RecordMammothRage()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var mammoth = Object.Instantiate(world.Mammoth);
            cleanup.Add(mammoth);
            mammoth.riderCount = 2;
            mammoth.riderDamage = 3f;
            mammoth.rageBonus = 0.3f;
            mammoth.rageThreshold = 1f;
            world.Towers.SetAvailableTowers(new[] { mammoth });
            Assert.That(world.Towers.TryPlace(mammoth, new Vector3(-17.6f, 0f, 2f)), Is.True);
            var charge = world.Towers.Towers[0].GetComponent<BeastCharge>();
            var beast = (Transform)typeof(BeastCharge).GetField("beast", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(charge);
            var wave = OnlyEnemy(world, "runner", 220, 400f);
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitUntil(() => charge.IsRaging, 45f);
            Assert.That(charge.IsRaging, Is.True, "The mammoth never raged.");
            var camera = Scene(beast.position + new Vector3(0f, 9f, -7f), beast.position);
            yield return Record(world, camera, "mammoth", 30 * 4, frame =>
            {
                var at = beast.position;
                at.y = 0f;
                camera.transform.position = at + new Vector3(0f, 9f, -7f);
                camera.transform.LookAt(at);
            });
        }

        // User, 2026-11-09: the whole life of a plain mammoth, from the charge into a packed crowd
        // until it falls, before its attack is changed.
        [UnityTest, Explicit("Records a mammoth charging into a packed crowd until it falls to Logs/SpecialEffects/mammoth_life.")]
        public IEnumerator RecordMammothLife()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            // A long, packed horde of ordinary runners (the game's own health). The pen is built
            // only once the road is full, so the mammoth meets the thick of it.
            var wave = OnlyEnemy(world, "runner", 1500, -1f);
            wave.spawnInterval = 0.12f;
            wave.randomSpawnBurstMin = 10;
            wave.randomSpawnBurstMax = 16;
            wave.enforceSpawnClearance = false;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitUntil(() => world.Enemies.TotalSpawned >= 900, 60f);
            world.Towers.SetAvailableTowers(new[] { world.Mammoth });
            Assert.That(world.Towers.TryPlace(world.Mammoth, new Vector3(-17.6f, 0f, 2f)), Is.True);
            var charge = world.Towers.Towers[0].GetComponent<BeastCharge>();
            var beast = (Transform)typeof(BeastCharge).GetField("beast", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(charge);
            yield return WaitUntil(() => charge.IsOut, 60f);
            Assert.That(charge.IsOut, Is.True, "The mammoth never set out.");
            var camera = Scene(beast.position + new Vector3(-2.5f, 8f, -6.5f), beast.position);
            var folder = $"{Output}/mammoth_life";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            cleanup.Add(target);
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            cleanup.Add(texture);
            camera.targetTexture = target;
            var view = beast.position;
            Time.timeScale = 0.2f;
            var next = Time.time;
            var frame = 0;
            var fellAt = -1;
            // After it falls, the next mammoth walks from the back of the pen to the gate.
            var readyAt = -1;
            const int maximumFrames = 30 * 60;
            while (frame < maximumFrames && (readyAt < 0 || frame < readyAt + 60))
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += 1f / 30f;
                var at = beast.position;
                at.y = 0f;
                view = Vector3.Lerp(view, at, 0.15f);
                camera.transform.position = view + new Vector3(-2.5f, 8f, -6.5f);
                camera.transform.LookAt(view);
                DrawHorde(world);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
                if (fellAt < 0 && charge.FallCount > 0)
                {
                    fellAt = frame;
                }

                if (fellAt >= 0 && readyAt < 0 && frame > fellAt + 60 && charge.PenReadiness >= 1f)
                {
                    readyAt = frame;
                }

                frame++;
            }

            Time.timeScale = 1f;
            File.WriteAllText($"{folder}/report.txt", $"frames {frame}, fell at frame {fellAt}, ready again at frame {readyAt}, spawned {world.Enemies.TotalSpawned}");
        }

        [UnityTest, Explicit("Records the cave's specials to Logs/SpecialEffects/cave.")]
        public IEnumerator RecordCaveSpecials()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var cave = FindTower("cave");
            cave.barracksCapacity = 3;
            cave.barracksRespawnSeconds = 0.5f;
            cave.alliedUnitDamageReduction = 0.25f;
            cave.chiefAuraBonus = 0.25f;
            cave.chiefHealthBonus = 1f;
            cave.torchBurnDamagePerSecond = 1f;
            cave.torchBurnSeconds = 5f;
            world.Towers.SetAvailableTowers(new[] { cave });
            Assert.That(world.Towers.TryPlace(cave, TowerSpot), Is.True);
            var wave = OnlyEnemy(world, "runner", 60, 400f);
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            var tower = world.Towers.Towers[0];
            yield return WaitUntil(() => tower.DamageDealt > 0f, 30f);
            var units = tower.GetComponentsInChildren<AlliedUnitActor>();
            Assert.That(units.Length, Is.GreaterThan(0), "No cavemen came out.");
            var middle = Vector3.zero;
            foreach (var unit in units)
            {
                middle += unit.Position / units.Length;
            }

            middle.y = 0f;
            var camera = Scene(middle + new Vector3(0f, 4.5f, -4f), middle);
            yield return Record(world, camera, "cave", 30 * 5);
        }

        private Camera Scene(Vector3 position, Vector3 lookAt)
        {
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

            var cameraObject = new GameObject("Effects Camera");
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

        private IEnumerator Record(World world, Camera camera, string name, int frames, System.Action<int> beforeFrame = null)
        {
            var folder = $"{Output}/{name}";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            cleanup.Add(target);
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            cleanup.Add(texture);
            camera.targetTexture = target;
            yield return RecordFrames(world, camera, target, texture, folder, frames, beforeFrame);
        }

        private static IEnumerator WaitUntil(System.Func<bool> done, float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private TowerDefinition FindTower(string id)
        {
            foreach (var tower in SampleContent.Create().Towers)
            {
                if (tower.id == id)
                {
                    cleanup.Add(tower);
                    return tower;
                }
            }

            Assert.Fail($"No tower {id} in the game.");
            return null;
        }

        private WaveDefinition OnlyEnemy(World world, string id, int count, float health)
        {
            EnemyDefinition enemy = null;
            foreach (var entry in world.Wave.entries)
            {
                if (entry.enemy.id == id)
                {
                    enemy = Object.Instantiate(entry.enemy);
                    cleanup.Add(enemy);
                    break;
                }
            }

            Assert.That(enemy, Is.Not.Null, id);
            if (health > 0f)
            {
                enemy.maxHealth = health;
            }
            var wave = Object.Instantiate(world.Wave);
            cleanup.Add(wave);
            wave.totalEnemyCount = count;
            wave.entries = new[] { new WaveEntry { enemy = enemy, count = count } };
            return wave;
        }

        private static int Stunned(World world)
        {
            var simulation = PrehistoricTowerTests.Simulation(world);
            var count = world.Enemies.TotalSpawned;
            if (simulation == null || count <= 0)
            {
                return 0;
            }

            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var stunned = 0;
            foreach (var state in states)
            {
                stunned += state.Status == 1u && state.SlowTimer > 0f && state.SlowMultiplier < 0.1f ? 1 : 0;
            }

            return stunned;
        }
    }
}
