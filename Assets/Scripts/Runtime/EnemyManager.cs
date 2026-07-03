using System;
using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class EnemyManager : MonoBehaviour
    {
        private readonly List<EnemyActor> activeEnemies = new();
        private readonly Queue<EnemyActor> pool = new();
        private WaveDefinition wave;
        private PathRoute path;
        private int[] spawnedByEntry;
        private float elapsed;
        private int totalSpawned;
        private int totalResolved;

        public IReadOnlyList<EnemyActor> ActiveEnemies => activeEnemies;
        public int TotalSpawned => totalSpawned;
        public int TotalResolved => totalResolved;
        public bool HasWave => wave != null;
        public bool IsWaveComplete => wave != null && totalSpawned >= wave.totalEnemyCount && totalResolved >= totalSpawned;
        public event Action<EnemyActor> EnemyKilled;
        public event Action<EnemyActor> EnemyEscaped;

        public void BeginWave(WaveDefinition waveDefinition, PathRoute route)
        {
            ClearAll();
            wave = waveDefinition;
            path = route;
            elapsed = 0f;
            totalSpawned = 0;
            totalResolved = 0;
            spawnedByEntry = wave.entries == null ? Array.Empty<int>() : new int[wave.entries.Length];
        }

        public void StopWave()
        {
            wave = null;
            ClearAll();
        }

        private void Update()
        {
            if (wave == null || wave.entries == null)
            {
                return;
            }

            elapsed += Time.deltaTime;
            for (var i = 0; i < wave.entries.Length; i++)
            {
                var entry = wave.entries[i];
                if (entry.enemy == null || entry.count <= 0 || totalSpawned >= wave.totalEnemyCount || elapsed < entry.startTime)
                {
                    continue;
                }

                var due = Mathf.FloorToInt((elapsed - entry.startTime) / Mathf.Max(0.01f, entry.spawnInterval)) + 1;
                while (spawnedByEntry[i] < entry.count && spawnedByEntry[i] < due && totalSpawned < wave.totalEnemyCount)
                {
                    Spawn(entry.enemy);
                    spawnedByEntry[i]++;
                }
            }
        }

        public void NotifyEnemyKilled(EnemyActor enemy)
        {
            if (activeEnemies.Remove(enemy))
            {
                totalResolved++;
                EnemyKilled?.Invoke(enemy);
            }
        }

        public void NotifyEnemyEscaped(EnemyActor enemy)
        {
            if (activeEnemies.Remove(enemy))
            {
                totalResolved++;
                EnemyEscaped?.Invoke(enemy);
            }
        }

        public EnemyActor GetNearestEnemy(Vector3 position, float range)
        {
            EnemyActor best = null;
            var bestDistance = range * range;
            foreach (var enemy in activeEnemies)
            {
                if (!enemy.IsAlive)
                {
                    continue;
                }

                var distance = (enemy.transform.position - position).sqrMagnitude;
                if (distance <= bestDistance)
                {
                    best = enemy;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public int DamageInRadius(Vector3 center, float radius, float damage)
        {
            var radiusSq = radius * radius;
            var hitCount = 0;
            for (var i = activeEnemies.Count - 1; i >= 0; i--)
            {
                var enemy = activeEnemies[i];
                if (!enemy.IsAlive || (enemy.transform.position - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                enemy.ApplyDamage(damage);
                hitCount++;
            }

            return hitCount;
        }

        public void ClearAll()
        {
            foreach (var enemy in activeEnemies)
            {
                if (enemy != null)
                {
                    enemy.gameObject.SetActive(false);
                    pool.Enqueue(enemy);
                }
            }

            activeEnemies.Clear();
        }

        private void Spawn(EnemyDefinition enemyDefinition)
        {
            var actor = pool.Count > 0 ? pool.Dequeue() : CreateEnemyActor(enemyDefinition);
            actor.Initialize(enemyDefinition, path, this, 0f);
            activeEnemies.Add(actor);
            totalSpawned++;
        }

        private EnemyActor CreateEnemyActor(EnemyDefinition enemyDefinition)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Enemy_{enemyDefinition.id}";
            go.transform.SetParent(transform);
            var renderer = go.GetComponent<Renderer>();
            renderer.material = BootstrapMaterials.Get(enemyDefinition.color);
            return go.AddComponent<EnemyActor>();
        }
    }
}
