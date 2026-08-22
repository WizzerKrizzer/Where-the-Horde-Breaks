using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class HordeEnemyManagerPlayModeTests
    {
        private readonly List<Object> cleanupObjects = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            foreach (var unityObject in cleanupObjects)
            {
                if (unityObject != null)
                {
                    Object.Destroy(unityObject);
                }
            }

            cleanupObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LevelTwoRealSpawnLoop_KeepsEveryGpuAgentOnRoad()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var points = new[]
            {
                new Vector3(-56f, 0f, 14f), new Vector3(-34f, 0f, 14f),
                new Vector3(-24f, 0f, 14f), new Vector3(-24f, 0f, -5f),
                new Vector3(-24f, 0f, -17f), new Vector3(2f, 0f, -17f),
                new Vector3(10f, 0f, -17f), new Vector3(18f, 0f, -17f),
                new Vector3(36f, 0f, -17f), new Vector3(42f, 0f, -11f),
                new Vector3(42f, 0f, 10f), new Vector3(56f, 0f, 10f)
            };
            var widths = new[] { 6f, 6f, 6f, 5f, 3f, 3f, 5f, 10f, 10f, 9f, 7f, 6f };
            var route = CreateRoute(points);
            route.SetWaypoints(points, widths);
            var enemy = CreateEnemy(speed: 4.8f, health: 10f);
            var wave = CreateWave(enemy, 500, 0.45f);
            wave.randomSpawnBurstMin = 5;
            wave.randomSpawnBurstMax = 12;
            var manager = CreateManager();
            manager.BeginWave(wave, route);
            var simulationField = typeof(HordeEnemyManager).GetField("gpuSimulation",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var flowFieldField = typeof(HordeEnemyManager).GetField("flowField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(simulationField, Is.Not.Null);
            Assert.That(flowFieldField, Is.Not.Null);
            var flow = (HordeFlowField)flowFieldField.GetValue(manager);
            var states = new GpuHordeSimulation.AgentState[500];
            Time.timeScale = 10f;

            for (var frame = 0; frame < 180; frame++)
            {
                yield return null;
                if (frame % 5 != 0 || manager.TotalSpawned == 0)
                {
                    continue;
                }

                var simulation = (GpuHordeSimulation)simulationField.GetValue(manager);
                simulation.ReadStatesSynchronous(states, manager.TotalSpawned);
                for (var i = 0; i < manager.TotalSpawned; i++)
                {
                    if (states[i].Status != 1)
                    {
                        continue;
                    }

                    var position = new Vector3(states[i].Position.x, 0f, states[i].Position.y);
                    Assert.That(flow.IsWalkable(position), Is.True,
                        $"Real Level 2 spawn-loop agent {i} is outside the road at frame {frame}: {position}.");
                }
            }
        }

        [Test]
        public void LevelTwoContent_UsesGpuAuthoritativeHordeBackend()
        {
            var content = SampleContent.Create();
            LevelDefinition levelTwo = null;
            for (var i = 0; i < content.Levels.Count; i++)
            {
                var level = content.Levels[i];
                cleanupObjects.Add(level);
                cleanupObjects.Add(level.wave);
                if (level.id == "level_02")
                {
                    levelTwo = level;
                }
            }
            cleanupObjects.Add(content.SkillTree);
            for (var i = 0; i < content.Towers.Count; i++)
            {
                cleanupObjects.Add(content.Towers[i]);
            }

            Assert.That(levelTwo, Is.Not.Null);
            Assert.That(levelTwo.useDataHordePrototype, Is.True,
                "Level 2 must not fall back to the legacy CPU path-distance enemy loop.");
        }

        [UnityTest]
        public IEnumerator DataHordeWave_SpawnsAndCompletes()
        {
            var route = CreateRoute(new[]
            {
                new Vector3(-4f, 0f, 0f),
                new Vector3(4f, 0f, 0f)
            });
            var enemy = CreateEnemy(speed: 12f, health: 4f);
            var wave = CreateWave(enemy, count: 18, spawnInterval: 0.01f, spawnImmediately: true);
            var manager = CreateManager();

            manager.BeginWave(wave, route);

            if (SystemInfo.supportsComputeShaders)
            {
                Assert.That(manager.Performance.ShaderName, Does.Contain("GPU Compute"));
            }

            var timeout = Time.time + 2f;
            while (!manager.IsComplete && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(manager.TotalSpawned, Is.EqualTo(18));
            Assert.That(manager.TotalResolved, Is.EqualTo(18));
            Assert.That(manager.ActiveCount, Is.EqualTo(0));
            Assert.That(manager.IsComplete, Is.True);
        }

        [UnityTest]
        public IEnumerator DataHordeControlEffects_DoNotBreakRuntimeSimulation()
        {
            var route = CreateRoute(new[]
            {
                new Vector3(-5f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(5f, 0f, 1f)
            });
            var enemy = CreateEnemy(speed: 5f, health: 20f);
            var wave = CreateWave(enemy, count: 30, spawnInterval: 0.02f);
            var manager = CreateManager();

            manager.BeginWave(wave, route);
            var spawnTimeout = Time.realtimeSinceStartup + 0.5f;
            while (manager.ActiveCount == 0 && Time.realtimeSinceStartup < spawnTimeout)
            {
                yield return null;
            }

            manager.ApplySlowAura(Vector3.zero, 8f, 0.5f, 100f);
            var damage = manager.DamageAndKnockbackInRadius(Vector3.zero, 8f, 3f, 1.25f, maxTargets: 20, out var hitCount);

            Assert.That(hitCount, Is.GreaterThan(0));
            Assert.That(damage, Is.GreaterThan(0f));
            Assert.That(manager.ActiveCount, Is.GreaterThan(0));

            for (var frame = 0; frame < 40; frame++)
            {
                yield return null;
            }

            Assert.That(manager.TotalSpawned, Is.GreaterThan(0));
            Assert.That(manager.TotalResolved, Is.LessThanOrEqualTo(manager.TotalSpawned));
        }

        [UnityTest]
        public IEnumerator DataHordeWave_BatchesLargeImmediateSpawn()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 4096;
            var route = CreateRoute(new[]
            {
                new Vector3(-40f, 0f, 0f),
                new Vector3(40f, 0f, 0f)
            });
            var enemy = CreateEnemy(speed: 1f, health: 10f);
            var wave = CreateWave(enemy, count, spawnInterval: 0.01f, spawnImmediately: true);
            var manager = CreateManager();

            manager.BeginWave(wave, route);
            var spawnTimeout = Time.realtimeSinceStartup + 0.5f;
            while (manager.TotalSpawned < count && Time.realtimeSinceStartup < spawnTimeout)
            {
                yield return null;
            }

            Assert.That(manager.TotalSpawned, Is.EqualTo(count));
            Assert.That(manager.ActiveCount, Is.GreaterThan(0));
            Assert.That(manager.Performance.ShaderName, Does.Contain("GPU Compute"));
        }

        [Test]
        public void DataHordeSpawn_BurstCoversEntranceWithoutEmptyBands()
        {
            const int count = 64;
            var route = CreateRoute(new[]
            {
                new Vector3(-20f, 0f, 0f),
                new Vector3(20f, 0f, 0f)
            });
            var enemy = CreateEnemy(speed: 1f, health: 10f);
            var wave = CreateWave(enemy, count, spawnInterval: 0.1f, spawnImmediately: true);
            wave.roadHalfWidth = 10f;
            var manager = CreateManager();

            manager.BeginWave(wave, route);
            var field = typeof(HordeEnemyManager).GetField(
                "spawnLateralOffsets",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var offsets = (float[])field.GetValue(manager);
            Assert.That(offsets, Has.Length.EqualTo(count));
            System.Array.Sort(offsets);

            var largestGap = 0f;
            for (var i = 1; i < offsets.Length; i++)
            {
                largestGap = Mathf.Max(largestGap, offsets[i] - offsets[i - 1]);
            }

            Assert.That(offsets[^1] - offsets[0], Is.GreaterThan(18.5f));
            Assert.That(largestGap, Is.LessThan(0.65f),
                $"A single spawn burst left a {largestGap:0.00} metre empty lateral band.");
        }

        [UnityTest, Explicit("Allocates the complete 100K Level 5 GPU stress configuration.")]
        [Category("Performance")]
        public IEnumerator LevelFiveStressWave_AllocatesAndStartsOneHundredThousandAgents()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var content = SampleContent.Create();
            LevelDefinition levelFive = null;
            for (var i = 0; i < content.Levels.Count; i++)
            {
                var level = content.Levels[i];
                cleanupObjects.Add(level);
                cleanupObjects.Add(level.wave);
                if (level.wave?.entries != null)
                {
                    for (var entryIndex = 0; entryIndex < level.wave.entries.Length; entryIndex++)
                    {
                        cleanupObjects.Add(level.wave.entries[entryIndex].enemy);
                    }
                }
                if (level.id == "level_05")
                {
                    levelFive = level;
                }
            }
            cleanupObjects.Add(content.SkillTree);
            TowerDefinition catapult = null;
            for (var i = 0; i < content.Towers.Count; i++)
            {
                var tower = content.Towers[i];
                cleanupObjects.Add(tower);
                if (tower.id == "catapult")
                {
                    catapult = tower;
                }
            }

            Assert.That(levelFive, Is.Not.Null);
            Assert.That(levelFive.wave.totalEnemyCount, Is.EqualTo(100000));
            Assert.That(levelFive.startingLives, Is.EqualTo(100000));
            Assert.That(levelFive.wave.spawnInterval, Is.EqualTo(0.155f));
            Assert.That(levelFive.wave.randomSpawnBurstMin, Is.EqualTo(55));
            Assert.That(levelFive.wave.randomSpawnBurstMax, Is.EqualTo(70));
            Assert.That(levelFive.wave.roadHalfWidth, Is.EqualTo(26.5f));
            Assert.That(levelFive.roadWidth, Is.EqualTo(54f));
            Assert.That(levelFive.pathWaypoints.Length, Is.GreaterThanOrEqualTo(20));
            Assert.That(levelFive.groundSize.x, Is.GreaterThanOrEqualTo(300f));
            Assert.That(levelFive.groundSize.z, Is.GreaterThanOrEqualTo(350f));
            Assert.That(catapult, Is.Not.Null);
            Assert.That(catapult.range, Is.EqualTo(9.5f));
            Assert.That(catapult.splashRadius, Is.EqualTo(1.75f));
            Assert.That(catapult.knockbackDistance, Is.EqualTo(1.15f));

            var route = CreateRoute(levelFive.pathWaypoints);
            var manager = CreateManager();
            manager.BeginWave(levelFive.wave, route);
            yield return null;

            Assert.That(manager.IsRunning, Is.True);
            Assert.That(manager.TotalSpawned, Is.GreaterThan(0));
            Assert.That(manager.Performance.ShaderName, Does.Contain("GPU Compute"));
        }

        private HordeEnemyManager CreateManager()
        {
            var gameObject = new GameObject("TestHordeEnemyManager");
            cleanupObjects.Add(gameObject);
            return gameObject.AddComponent<HordeEnemyManager>();
        }

        private PathRoute CreateRoute(Vector3[] points)
        {
            var gameObject = new GameObject("TestPathRoute");
            cleanupObjects.Add(gameObject);
            var route = gameObject.AddComponent<PathRoute>();
            route.SetWaypoints(points);
            return route;
        }

        private WaveDefinition CreateWave(EnemyDefinition enemy, int count, float spawnInterval, bool spawnImmediately = false)
        {
            var wave = ScriptableObject.CreateInstance<WaveDefinition>();
            cleanupObjects.Add(wave);
            wave.totalEnemyCount = count;
            wave.spawnInterval = spawnInterval;
            wave.randomSpawnBurstMin = spawnImmediately ? count : 3;
            wave.randomSpawnBurstMax = spawnImmediately ? count : 6;
            wave.entries = new[]
            {
                new WaveEntry
                {
                    enemy = enemy,
                    count = count
                }
            };
            return wave;
        }

        private EnemyDefinition CreateEnemy(float speed, float health)
        {
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            cleanupObjects.Add(enemy);
            enemy.id = "test_runner";
            enemy.displayName = "Test Runner";
            enemy.maxHealth = health;
            enemy.speed = speed;
            enemy.mass = 1f;
            enemy.lifeDamage = 1;
            enemy.killReward = 1;
            enemy.color = Color.green;
            return enemy;
        }
    }
}
