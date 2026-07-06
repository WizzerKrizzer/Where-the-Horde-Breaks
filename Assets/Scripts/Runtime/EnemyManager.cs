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
        private readonly List<ICombatTarget> combatTargets = new();
        private readonly Queue<EnemyActor> pool = new();
        private readonly List<EnemyDefinition> spawnSequence = new();
        private WaveDefinition wave;
        private PathRoute path;
        private EnemyCorpseManager corpseManager;
        private readonly List<EnemyDistance> damageCandidates = new();
        private float elapsed;
        private float spawnWindowStartTime;
        private float nextSpawnTime;
        private int burstPatternIndex;
        private int currentWindowSpawnCount;
        private int currentWindowSpawned;
        private int totalSpawned;
        private int totalResolved;

        public IReadOnlyList<EnemyActor> ActiveEnemies => activeEnemies;
        public int TotalSpawned => totalSpawned;
        public int TotalResolved => totalResolved;
        public bool HasWave => wave != null;
        public bool IsWaveComplete => wave != null && totalSpawned >= spawnSequence.Count && activeEnemies.Count == 0;
        public event Action<EnemyDefinition> EnemySpawned;
        public event Action<EnemyActor> EnemyKilled;
        public event Action<EnemyActor> EnemyEscaped;

        public void SetCorpseManager(EnemyCorpseManager manager)
        {
            corpseManager = manager;
        }

        public void BeginWave(WaveDefinition waveDefinition, PathRoute route)
        {
            ClearAll(clearCombatTargets: false);
            wave = waveDefinition;
            path = route;
            elapsed = 0f;
            spawnWindowStartTime = 0f;
            nextSpawnTime = 0f;
            burstPatternIndex = 0;
            currentWindowSpawnCount = GetNextWindowSpawnCount();
            currentWindowSpawned = 0;
            totalSpawned = 0;
            totalResolved = 0;
            BuildSpawnSequence();
        }

        public void StopWave()
        {
            wave = null;
            ClearAll(clearCombatTargets: true);
        }

        public void SpawnDebug(EnemyDefinition enemyDefinition, PathRoute route)
        {
            if (enemyDefinition == null || route == null)
            {
                return;
            }

            path = route;
            Spawn(enemyDefinition, 0f, countTowardWaveTotal: false);
        }

        public void SpawnConvertedEnemy(EnemyDefinition enemyDefinition, Vector3 position)
        {
            if (enemyDefinition == null || path == null)
            {
                return;
            }

            Spawn(enemyDefinition, EstimatePathDistance(position), countTowardWaveTotal: false);
        }

        private void Update()
        {
            if (wave == null || spawnSequence.Count == 0)
            {
                return;
            }

            elapsed += Time.deltaTime;
            while (elapsed >= nextSpawnTime && totalSpawned < spawnSequence.Count)
            {
                Spawn(spawnSequence[totalSpawned]);
                AdvanceSpawnSchedule();
            }
        }

        private void BuildSpawnSequence()
        {
            spawnSequence.Clear();
            if (wave?.entries == null)
            {
                return;
            }

            for (var i = 0; i < wave.entries.Length && spawnSequence.Count < wave.totalEnemyCount; i++)
            {
                var entry = wave.entries[i];
                if (entry.enemy == null || entry.count <= 0)
                {
                    continue;
                }

                var remaining = wave.totalEnemyCount - spawnSequence.Count;
                var count = Mathf.Min(entry.count, remaining);
                for (var j = 0; j < count; j++)
                {
                    spawnSequence.Add(entry.enemy);
                }
            }
        }

        private int GetNextWindowSpawnCount()
        {
            var pattern = wave.spawnBurstPattern;
            if (pattern != null && pattern.Length > 0)
            {
                var count = Mathf.Max(1, pattern[burstPatternIndex % pattern.Length]);
                burstPatternIndex++;
                return count;
            }

            return 1;
        }

        private void AdvanceSpawnSchedule()
        {
            currentWindowSpawned++;
            var windowDuration = Mathf.Max(0.01f, wave.spawnInterval);
            if (currentWindowSpawned < currentWindowSpawnCount)
            {
                nextSpawnTime = spawnWindowStartTime + windowDuration * currentWindowSpawned / currentWindowSpawnCount;
                return;
            }

            spawnWindowStartTime += windowDuration;
            currentWindowSpawnCount = GetNextWindowSpawnCount();
            currentWindowSpawned = 0;
            nextSpawnTime = spawnWindowStartTime;
        }

        public void NotifyEnemyKilled(EnemyActor enemy)
        {
            if (activeEnemies.Remove(enemy))
            {
                corpseManager?.SpawnCorpse(enemy);
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
            return GetNearestEnemy(position, range, canHitFlying: true);
        }

        public EnemyActor GetNearestEnemy(Vector3 position, float range, bool canHitFlying)
        {
            EnemyActor best = null;
            var bestDistance = range * range;
            foreach (var enemy in activeEnemies)
            {
                if (!enemy.IsAlive || (enemy.Definition.isFlying && !canHitFlying))
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

        public EnemyActor GetNearestEnemyExcept(Vector3 position, float range, bool canHitFlying, EnemyActor excludedEnemy)
        {
            EnemyActor best = null;
            var bestDistance = range * range;
            foreach (var enemy in activeEnemies)
            {
                if (!enemy.IsAlive || enemy == excludedEnemy || (enemy.Definition.isFlying && !canHitFlying))
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

        public ICombatTarget GetNearestCombatTarget(Vector3 position, float range, float enemyMass)
        {
            ICombatTarget best = null;
            var bestDistance = float.PositiveInfinity;
            for (var i = combatTargets.Count - 1; i >= 0; i--)
            {
                var target = combatTargets[i];
                if (target == null || !target.IsAlive)
                {
                    combatTargets.RemoveAt(i);
                    continue;
                }

                if (target.CurrentBlockedMass + enemyMass > target.BlockCapacity)
                {
                    continue;
                }

                var allowedRange = range + Mathf.Max(0f, target.CombatRadius);
                var distance = XzDistanceSq(target.Position, position);
                if (distance <= allowedRange * allowedRange && distance <= bestDistance)
                {
                    best = target;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public void RegisterCombatTarget(ICombatTarget target)
        {
            if (target != null && !combatTargets.Contains(target))
            {
                combatTargets.Add(target);
            }
        }

        public void UnregisterCombatTarget(ICombatTarget target)
        {
            combatTargets.Remove(target);
        }

        public Vector3 GetNearestPathPosition(Vector3 position)
        {
            if (path == null || path.TotalLength <= 0f)
            {
                return position;
            }

            return path.Sample(EstimatePathDistance(position));
        }

        public Vector3 GetPathSidePosition(Vector3 position, float sideDistance)
        {
            if (path == null || path.TotalLength <= 0f)
            {
                return position;
            }

            var distance = EstimatePathDistance(position);
            var center = path.Sample(distance);
            var before = path.Sample(Mathf.Max(0f, distance - 0.5f));
            var after = path.Sample(Mathf.Min(path.TotalLength, distance + 0.5f));
            var tangent = after - before;
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.001f)
            {
                tangent = Vector3.forward;
            }

            var side = Vector3.Cross(Vector3.up, tangent.normalized);
            var desiredSide = Vector3.Dot(position - center, side) >= 0f ? side : -side;
            return center + desiredSide * Mathf.Max(0f, sideDistance);
        }

        private static float XzDistanceSq(Vector3 a, Vector3 b)
        {
            var x = a.x - b.x;
            var z = a.z - b.z;
            return x * x + z * z;
        }

        public void HealEnemiesInRadius(Vector3 center, float radius, float amount, EnemyActor excludedEnemy)
        {
            if (amount <= 0f)
            {
                return;
            }

            var radiusSq = radius * radius;
            foreach (var enemy in activeEnemies)
            {
                if (!enemy.IsAlive || enemy == excludedEnemy || (enemy.transform.position - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                enemy.Heal(amount);
            }
        }

        public void ApplySlowAura(Vector3 center, float radius, float slowPercent, float capacity)
        {
            if (slowPercent <= 0f || capacity <= 0f)
            {
                return;
            }

            var radiusSq = radius * radius;
            var usedCapacity = 0f;
            foreach (var enemy in activeEnemies)
            {
                if (!enemy.IsAlive || (enemy.transform.position - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                var cost = Mathf.Max(0.1f, enemy.Definition.mass);
                if (usedCapacity + cost > capacity)
                {
                    continue;
                }

                enemy.ApplySlow(slowPercent, 0.15f);
                usedCapacity += cost;
            }
        }

        public float DamageInRadius(Vector3 center, float radius, float damage, int maxTargets, out int hitCount)
        {
            var radiusSq = radius * radius;
            hitCount = 0;
            var appliedDamage = 0f;
            damageCandidates.Clear();
            for (var i = activeEnemies.Count - 1; i >= 0; i--)
            {
                var enemy = activeEnemies[i];
                if (!enemy.IsAlive)
                {
                    continue;
                }

                var distanceSq = (enemy.transform.position - center).sqrMagnitude;
                if (distanceSq <= radiusSq)
                {
                    damageCandidates.Add(new EnemyDistance(enemy, distanceSq));
                }
            }

            damageCandidates.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));
            var targetCount = Mathf.Min(Mathf.Max(0, maxTargets), damageCandidates.Count);
            for (var i = 0; i < targetCount; i++)
            {
                appliedDamage += damageCandidates[i].enemy.ApplyDamage(damage);
                hitCount++;
            }

            return appliedDamage;
        }

        public float DamageAndKnockbackInRadius(
            Vector3 center,
            float radius,
            float damage,
            float knockbackDistance,
            out int hitCount,
            TowerActor burnSource = null,
            float burnDamagePerTick = 0f,
            float burnTicksPerSecond = 0f,
            float burnDuration = 0f,
            int maxBurnStacks = 0)
        {
            var radiusSq = radius * radius;
            hitCount = 0;
            var appliedDamage = 0f;
            for (var i = activeEnemies.Count - 1; i >= 0; i--)
            {
                var enemy = activeEnemies[i];
                if (!enemy.IsAlive)
                {
                    continue;
                }

                if ((enemy.transform.position - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                enemy.ApplyKnockback(center, knockbackDistance);
                appliedDamage += enemy.ApplyDamage(damage);
                enemy.ApplyBurn(burnSource, burnDamagePerTick, burnTicksPerSecond, burnDuration, maxBurnStacks);
                hitCount++;
            }

            return appliedDamage;
        }

        public void ClearAll(bool clearCombatTargets = true)
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
            if (clearCombatTargets)
            {
                combatTargets.Clear();
            }
        }

        private void Spawn(EnemyDefinition enemyDefinition)
        {
            Spawn(enemyDefinition, 0f, countTowardWaveTotal: true);
        }

        private void Spawn(EnemyDefinition enemyDefinition, float initialOffset, bool countTowardWaveTotal)
        {
            var actor = pool.Count > 0 ? pool.Dequeue() : CreateEnemyActor(enemyDefinition);
            actor.Initialize(enemyDefinition, path, this, Mathf.Clamp(initialOffset, 0f, path.TotalLength));
            activeEnemies.Add(actor);
            if (countTowardWaveTotal)
            {
                totalSpawned++;
            }
            EnemySpawned?.Invoke(enemyDefinition);
        }

        public float EstimatePathDistance(Vector3 position)
        {
            if (path == null || path.TotalLength <= 0f)
            {
                return 0f;
            }

            var bestDistance = 0f;
            var bestDistanceSq = float.PositiveInfinity;
            var step = Mathf.Max(0.5f, path.TotalLength / 90f);
            for (var distance = 0f; distance <= path.TotalLength; distance += step)
            {
                var distanceSq = (path.Sample(distance) - position).sqrMagnitude;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestDistance = distance;
                }
            }

            return bestDistance;
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

        private readonly struct EnemyDistance
        {
            public readonly EnemyActor enemy;
            public readonly float distanceSq;

            public EnemyDistance(EnemyActor enemy, float distanceSq)
            {
                this.enemy = enemy;
                this.distanceSq = distanceSq;
            }
        }
    }
}
