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
            for (var frame = 0; frame < 30 && manager.ActiveCount == 0; frame++)
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
