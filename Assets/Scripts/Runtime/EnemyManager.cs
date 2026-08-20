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
        private readonly Dictionary<Vector2Int, List<EnemyActor>> spatialBuckets = new();
        private readonly List<EnemyActor> targetCandidates = new();
        private static Mesh sharedDetailedEnemyMesh;
        private static Mesh sharedLowEnemyMesh;
        private WaveDefinition wave;
        private PathRoute path;
        private EnemyCorpseManager corpseManager;
        private HordeEnemyManager hordePrototype;
        private readonly List<EnemyDistance> damageCandidates = new();
        private const float SpatialCellSize = 3.2f;
        private const int MaxNearbyEnemyResults = 42;
        private int spatialBucketsFrame = -1;
        private float elapsed;
        private float spawnWindowStartTime;
        private float nextSpawnTime;
        private int burstPatternIndex;
        private int currentWindowSpawnCount;
        private int currentWindowSpawned;
        private int totalSpawned;
        private int totalResolved;

        public IReadOnlyList<EnemyActor> ActiveEnemies => activeEnemies;
        public int ActiveEnemyCount => hordePrototype != null && hordePrototype.IsRunning ? hordePrototype.ActiveCount : activeEnemies.Count;
        public int TotalSpawned => hordePrototype != null && hordePrototype.IsRunning ? hordePrototype.TotalSpawned : totalSpawned;
        public int TotalResolved => hordePrototype != null && hordePrototype.IsRunning ? hordePrototype.TotalResolved : totalResolved;
        public HordeEnemyManager.HordePerformanceSnapshot HordePerformance => hordePrototype != null
            ? hordePrototype.Performance
            : default;
        public bool HasWave => wave != null || (hordePrototype != null && hordePrototype.IsRunning);
        public bool IsWaveComplete => hordePrototype != null && hordePrototype.IsRunning
            ? hordePrototype.IsComplete
            : wave != null && totalSpawned >= spawnSequence.Count && activeEnemies.Count == 0;
        public event Action<EnemyDefinition> EnemySpawned;
        public event Action<EnemyActor> EnemyKilled;
        public event Action<EnemyActor> EnemyEscaped;

        public void SetCorpseManager(EnemyCorpseManager manager)
        {
            corpseManager = manager;
        }

        public void SetHordePrototype(HordeEnemyManager manager)
        {
            hordePrototype = manager;
            hordePrototype?.SetCombatTargets(combatTargets);
        }

        public void SetLevelRoute(PathRoute route)
        {
            path = route;
        }

        public void BeginWave(WaveDefinition waveDefinition, PathRoute route, bool useDataHordePrototype = false)
        {
            ClearAll(clearCombatTargets: false);
            corpseManager?.ClearAllVisuals();
            hordePrototype?.StopWave();
            if (useDataHordePrototype)
            {
                wave = null;
                path = route;
                totalSpawned = 0;
                totalResolved = 0;
                hordePrototype?.BeginWave(waveDefinition, route);
                return;
            }

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
            if (path == null || !path.HasUsableRoute || spawnSequence.Count == 0)
            {
                wave = null;
            }
        }

        public void StopWave()
        {
            wave = null;
            hordePrototype?.StopWave();
            ClearAll(clearCombatTargets: true);
            corpseManager?.ClearAllVisuals();
        }

        public void SpawnDebug(EnemyDefinition enemyDefinition, PathRoute route)
        {
            if (enemyDefinition == null || route == null || !route.HasUsableRoute)
            {
                return;
            }

            path = route;
            Spawn(enemyDefinition, 0f, countTowardWaveTotal: false);
        }

        public void SpawnConvertedEnemy(EnemyDefinition enemyDefinition, Vector3 position)
        {
            if (enemyDefinition == null || path == null || !path.HasUsableRoute)
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

        public void CollectNearbyEnemies(Vector3 position, float radius, List<EnemyActor> results, int maxResults = MaxNearbyEnemyResults)
        {
            results.Clear();
            if (activeEnemies.Count == 0 || radius <= 0f)
            {
                return;
            }

            RebuildSpatialBucketsIfNeeded();
            var center = GetSpatialCell(position);
            var cellRadius = Mathf.CeilToInt(radius / SpatialCellSize);
            var radiusSq = radius * radius;
            for (var x = center.x - cellRadius; x <= center.x + cellRadius; x++)
            {
                for (var y = center.y - cellRadius; y <= center.y + cellRadius; y++)
                {
                    if (!spatialBuckets.TryGetValue(new Vector2Int(x, y), out var bucket))
                    {
                        continue;
                    }

                    for (var i = 0; i < bucket.Count; i++)
                    {
                        var enemy = bucket[i];
                        if (enemy == null || !enemy.IsAlive)
                        {
                            continue;
                        }

                        var offset = enemy.transform.position - position;
                        if (offset.x * offset.x + offset.z * offset.z <= radiusSq)
                        {
                            results.Add(enemy);
                            if (maxResults > 0 && results.Count >= maxResults)
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }

        private void RebuildSpatialBucketsIfNeeded()
        {
            var rebuildInterval = activeEnemies.Count >= 5000 ? 4 : activeEnemies.Count >= 2500 ? 3 : activeEnemies.Count >= 900 ? 2 : 1;
            if (spatialBucketsFrame >= 0 && Time.frameCount - spatialBucketsFrame < rebuildInterval)
            {
                return;
            }

            spatialBucketsFrame = Time.frameCount;
            foreach (var bucket in spatialBuckets.Values)
            {
                bucket.Clear();
            }

            for (var i = 0; i < activeEnemies.Count; i++)
            {
                var enemy = activeEnemies[i];
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                var cell = GetSpatialCell(enemy.transform.position);
                if (!spatialBuckets.TryGetValue(cell, out var bucket))
                {
                    bucket = new List<EnemyActor>(16);
                    spatialBuckets.Add(cell, bucket);
                }

                bucket.Add(enemy);
            }
        }

        private static Vector2Int GetSpatialCell(Vector3 position)
        {
            return new Vector2Int(
                Mathf.FloorToInt(position.x / SpatialCellSize),
                Mathf.FloorToInt(position.z / SpatialCellSize));
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
            if (wave.randomSpawnBurstMax >= wave.randomSpawnBurstMin && wave.randomSpawnBurstMin > 0)
            {
                return UnityEngine.Random.Range(wave.randomSpawnBurstMin, wave.randomSpawnBurstMax + 1);
            }

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
            return GetEnemyByTargetingMode(position, range, canHitFlying, TowerTargetingMode.Closest);
        }

        public bool TryGetLeadEnemyAimPoint(float radius, out Vector3 aimPoint)
        {
            aimPoint = Vector3.zero;
            if (hordePrototype != null && hordePrototype.IsRunning)
            {
                return hordePrototype.TryGetLeadAimPoint(radius, out aimPoint);
            }

            if (path == null || activeEnemies.Count == 0)
            {
                return false;
            }

            EnemyActor lead = null;
            var bestPathDistance = float.MinValue;
            foreach (var enemy in activeEnemies)
            {
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                if (enemy.PathDistance > bestPathDistance)
                {
                    lead = enemy;
                    bestPathDistance = enemy.PathDistance;
                }
            }

            if (lead == null)
            {
                return false;
            }

            var lookBack = Mathf.Max(0.2f, radius * 0.55f);
            var speedLead = Mathf.Clamp(lead.Definition != null ? lead.Definition.speed * 0.22f : 0f, 0f, radius * 0.28f);
            aimPoint = path.Sample(Mathf.Max(0f, lead.PathDistance + speedLead - lookBack));
            return true;
        }

        public EnemyActor GetEnemyByTargetingMode(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode)
        {
            if (hordePrototype != null && hordePrototype.IsRunning)
            {
                return null;
            }

            EnemyActor best = null;
            var bestDistance = range * range;
            var bestScore = float.MinValue;
            CollectNearbyEnemies(position, range, targetCandidates);
            foreach (var enemy in targetCandidates)
            {
                if (!IsValidTowerTarget(enemy, canHitFlying))
                {
                    continue;
                }

                var distance = (enemy.transform.position - position).sqrMagnitude;
                if (distance > range * range)
                {
                    continue;
                }

                var score = -distance;
                switch (targetingMode)
                {
                    case TowerTargetingMode.First:
                        score = enemy.PathDistance;
                        break;
                    case TowerTargetingMode.Last:
                        score = -enemy.PathDistance;
                        break;
                    case TowerTargetingMode.HighestHealth:
                        score = enemy.Health;
                        break;
                }

                if (best == null || score > bestScore || (Mathf.Approximately(score, bestScore) && distance < bestDistance))
                {
                    best = enemy;
                    bestScore = score;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public EnemyActor GetNearestEnemyExcept(Vector3 position, float range, bool canHitFlying, EnemyActor excludedEnemy)
        {
            EnemyActor best = null;
            var bestDistance = range * range;
            CollectNearbyEnemies(position, range, targetCandidates);
            foreach (var enemy in targetCandidates)
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

        private static bool IsValidTowerTarget(EnemyActor enemy, bool canHitFlying)
        {
            return enemy != null && enemy.IsAlive && (!enemy.Definition.isFlying || canHitFlying);
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
                hordePrototype?.SetCombatTargets(combatTargets);
            }
        }

        public void UnregisterCombatTarget(ICombatTarget target)
        {
            combatTargets.Remove(target);
            hordePrototype?.SetCombatTargets(combatTargets);
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
            CollectNearbyEnemies(center, radius, targetCandidates, maxResults: 96);
            foreach (var enemy in targetCandidates)
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
            if (hordePrototype != null && hordePrototype.IsRunning)
            {
                hordePrototype.ApplySlowAura(center, radius, slowPercent, capacity);
                return;
            }

            if (slowPercent <= 0f || capacity <= 0f)
            {
                return;
            }

            var radiusSq = radius * radius;
            var usedCapacity = 0f;
            CollectNearbyEnemies(center, radius, targetCandidates, maxResults: 96);
            foreach (var enemy in targetCandidates)
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
            if (hordePrototype != null && hordePrototype.IsRunning)
            {
                return hordePrototype.DamageInRadius(center, radius, damage, maxTargets, out hitCount);
            }

            var radiusSq = radius * radius;
            hitCount = 0;
            var appliedDamage = 0f;
            damageCandidates.Clear();
            CollectNearbyEnemies(center, radius, targetCandidates, maxResults: 128);
            for (var i = targetCandidates.Count - 1; i >= 0; i--)
            {
                var enemy = targetCandidates[i];
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
            if (hordePrototype != null && hordePrototype.IsRunning)
            {
                return hordePrototype.DamageAndKnockbackInRadius(
                    center,
                    radius,
                    damage,
                    knockbackDistance,
                    maxTargets: 160,
                    out hitCount,
                    burnDamagePerTick,
                    burnTicksPerSecond,
                    burnDuration,
                    maxBurnStacks);
            }

            var radiusSq = radius * radius;
            hitCount = 0;
            var appliedDamage = 0f;
            CollectNearbyEnemies(center, radius, targetCandidates, maxResults: 160);
            for (var i = targetCandidates.Count - 1; i >= 0; i--)
            {
                var enemy = targetCandidates[i];
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

        public bool TryGetHordeTargetPosition(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            return hordePrototype != null
                && hordePrototype.IsRunning
                && hordePrototype.TryGetTargetPosition(position, range, canHitFlying, targetingMode, out targetPosition);
        }

        public float DamageHordeTarget(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode, float damage, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            return hordePrototype != null && hordePrototype.IsRunning
                ? hordePrototype.DamageTarget(position, range, canHitFlying, targetingMode, damage, out targetPosition)
                : 0f;
        }

        public bool QueueHordeProjectile(
            Vector3 start,
            Vector3 end,
            float radius,
            float damage,
            float knockback,
            int maxHits,
            bool canHitFlying,
            bool splash,
            float burnDamagePerSecond = 0f,
            float burnDuration = 0f,
            int maxBurnStacks = 1)
        {
            return hordePrototype != null && hordePrototype.IsRunning && hordePrototype.QueueProjectile(
                start,
                end,
                radius,
                damage,
                knockback,
                maxHits,
                canHitFlying,
                splash,
                burnDamagePerSecond,
                burnDuration,
                maxBurnStacks);
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
            spatialBucketsFrame = -1;
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
            if (path == null || !path.HasUsableRoute)
            {
                return;
            }

            var actor = pool.Count > 0 ? pool.Dequeue() : CreateEnemyActor(enemyDefinition);
            actor.Initialize(enemyDefinition, path, this, Mathf.Clamp(initialOffset, 0f, path.TotalLength), wave != null && wave.useEndpointSeeking);
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
            var go = new GameObject($"Enemy_{enemyDefinition.id}");
            go.name = $"Enemy_{enemyDefinition.id}";
            go.transform.SetParent(transform);
            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GetDetailedEnemyMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BootstrapMaterials.Get(enemyDefinition.color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.AddComponent<EnemyActor>();
        }

        public static Mesh GetLowEnemyMesh()
        {
            if (sharedLowEnemyMesh != null)
            {
                return sharedLowEnemyMesh;
            }

            sharedLowEnemyMesh = new Mesh
            {
                name = "LowEnemyOctahedron",
                vertices = new[]
                {
                    new Vector3(0f, 2f, 0f),
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0.85f, 0f),
                    new Vector3(-1f, 0.85f, 0f),
                    new Vector3(0f, 0.85f, 1f),
                    new Vector3(0f, 0.85f, -1f)
                },
                triangles = new[]
                {
                    0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2,
                    1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5
                }
            };
            sharedLowEnemyMesh.RecalculateNormals();
            sharedLowEnemyMesh.RecalculateBounds();
            return sharedLowEnemyMesh;
        }

        public static Mesh GetDetailedEnemyMesh()
        {
            if (sharedDetailedEnemyMesh != null)
            {
                return sharedDetailedEnemyMesh;
            }

            var template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var templateMesh = template.GetComponent<MeshFilter>().sharedMesh;
            var vertices = templateMesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3(vertices[i].x * 1.05f, vertices[i].y * 1.18f + 0.82f, vertices[i].z * 1.05f);
            }

            sharedDetailedEnemyMesh = new Mesh
            {
                name = "DetailedEnemy",
                vertices = vertices,
                triangles = templateMesh.triangles
            };
            UnityEngine.Object.Destroy(template);
            sharedDetailedEnemyMesh.RecalculateNormals();
            sharedDetailedEnemyMesh.RecalculateBounds();
            return sharedDetailedEnemyMesh;
        }

        private static void RemovePrimitiveColliders(GameObject gameObject)
        {
            var components = gameObject.GetComponents<Component>();
            for (var i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }
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
