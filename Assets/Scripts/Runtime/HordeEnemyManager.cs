using System;
using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    public sealed class HordeEnemyManager : MonoBehaviour
    {
        private const int InstanceBatchSize = 1023;
        private const float RoadHalfWidth = 2.45f;
        private const float VisualRadius = 0.28f;
        private const float SpatialCellSize = 1.2f;
        private const float PressureRadius = 0.62f;
        private const int MaxPressureNeighbors = 8;

        private readonly List<EnemyDefinition> spawnSequence = new();
        private readonly Matrix4x4[] matrixBatch = new Matrix4x4[InstanceBatchSize];
        private readonly Dictionary<Vector2Int, List<int>> spatialBuckets = new();
        private readonly List<int> nearbyIndices = new(192);
        private Vector3[] positions;
        private Vector3[] previousPositions;
        private float[] pathDistances;
        private float[] laneOffsets;
        private float[] laneDriftPhases;
        private float[] laneDriftSpeeds;
        private float[] speeds;
        private float[] slowMultipliers;
        private float[] slowTimers;
        private float[] spawnTimes;
        private float[] health;
        private Vector3[] knockbackOffsets;
        private Vector3[] knockbackVelocities;
        private int[] segmentIndices;
        private EnemyDefinition[] definitions;
        private bool[] alive;
        private Vector3[] segmentStarts;
        private Vector3[] segmentDirections;
        private Vector3[] segmentSides;
        private float[] segmentStartDistances;
        private float[] segmentEndDistances;
        private WaveDefinition wave;
        private PathRoute path;
        private Mesh mesh;
        private Material material;
        private Material slowedMaterial;
        private Material knockedMaterial;
        private MaterialPropertyBlock properties;
        private float elapsed;
        private int totalSpawned;
        private int activeCount;
        private int totalResolved;
        private bool running;

        public int TotalSpawned => totalSpawned;
        public int ActiveCount => activeCount;
        public int TotalResolved => totalResolved;
        public bool IsRunning => running;
        public bool IsComplete => running && totalSpawned >= spawnSequence.Count && activeCount == 0;

        public void BeginWave(WaveDefinition waveDefinition, PathRoute route)
        {
            Clear();
            wave = waveDefinition;
            path = route;
            BuildSpawnSequence();
            if (wave == null || path == null || !path.HasUsableRoute || spawnSequence.Count == 0)
            {
                running = false;
                return;
            }

            var count = spawnSequence.Count;
            BuildRouteCache();
            if (segmentStarts == null || segmentStarts.Length == 0)
            {
                running = false;
                return;
            }

            positions = new Vector3[count];
            previousPositions = new Vector3[count];
            pathDistances = new float[count];
            laneOffsets = new float[count];
            laneDriftPhases = new float[count];
            laneDriftSpeeds = new float[count];
            speeds = new float[count];
            slowMultipliers = new float[count];
            slowTimers = new float[count];
            spawnTimes = new float[count];
            health = new float[count];
            knockbackOffsets = new Vector3[count];
            knockbackVelocities = new Vector3[count];
            segmentIndices = new int[count];
            definitions = new EnemyDefinition[count];
            alive = new bool[count];
            var cursor = 0f;
            var windowDuration = Mathf.Max(0.01f, wave.spawnInterval);
            var minBurst = Mathf.Max(1, wave.randomSpawnBurstMin);
            var maxBurst = Mathf.Max(minBurst, wave.randomSpawnBurstMax);
            var burstIndex = 0;
            while (burstIndex < count)
            {
                var burst = Mathf.Min(count - burstIndex, UnityEngine.Random.Range(minBurst, maxBurst + 1));
                for (var i = 0; i < burst; i++)
                {
                    spawnTimes[burstIndex + i] = cursor + UnityEngine.Random.Range(0f, windowDuration);
                }

                Array.Sort(spawnTimes, burstIndex, burst);
                burstIndex += burst;
                cursor += windowDuration;
            }

            mesh = EnemyManager.GetDetailedEnemyMesh();
            material = BootstrapMaterials.Get(new Color(0.1f, 0.9f, 0.18f, 1f));
            material.enableInstancing = true;
            slowedMaterial = BootstrapMaterials.Get(new Color(0.2f, 0.62f, 1f, 1f));
            slowedMaterial.enableInstancing = true;
            knockedMaterial = BootstrapMaterials.Get(new Color(1f, 0.78f, 0.18f, 1f));
            knockedMaterial.enableInstancing = true;
            properties ??= new MaterialPropertyBlock();
            elapsed = 0f;
            totalSpawned = 0;
            activeCount = 0;
            totalResolved = 0;
            running = true;
        }

        public void StopWave()
        {
            Clear();
        }

        private void Clear()
        {
            running = false;
            wave = null;
            spawnSequence.Clear();
            material = null;
            slowedMaterial = null;
            knockedMaterial = null;
            positions = null;
            previousPositions = null;
            pathDistances = null;
            laneOffsets = null;
            laneDriftPhases = null;
            laneDriftSpeeds = null;
            speeds = null;
            slowMultipliers = null;
            slowTimers = null;
            spawnTimes = null;
            health = null;
            knockbackOffsets = null;
            knockbackVelocities = null;
            segmentIndices = null;
            definitions = null;
            alive = null;
            segmentStarts = null;
            segmentDirections = null;
            segmentSides = null;
            segmentStartDistances = null;
            segmentEndDistances = null;
            spatialBuckets.Clear();
            nearbyIndices.Clear();
            elapsed = 0f;
            totalSpawned = 0;
            activeCount = 0;
            totalResolved = 0;
        }

        private void Update()
        {
            if (!running || wave == null || path == null)
            {
                return;
            }

            var deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            SpawnDueEnemies();
            Simulate(deltaTime);
        }

        private void LateUpdate()
        {
            if (!running || mesh == null || material == null || positions == null)
            {
                return;
            }

            DrawInstances();
        }

        private void SpawnDueEnemies()
        {
            while (totalSpawned < spawnSequence.Count && elapsed >= spawnTimes[totalSpawned])
            {
                var definition = spawnSequence[totalSpawned];
                definitions[totalSpawned] = definition;
                health[totalSpawned] = Mathf.Max(1f, definition != null ? definition.maxHealth : 1f);
                laneOffsets[totalSpawned] = UnityEngine.Random.Range(-RoadHalfWidth + 0.35f, RoadHalfWidth - 0.35f);
                laneDriftPhases[totalSpawned] = UnityEngine.Random.Range(0f, 100f);
                laneDriftSpeeds[totalSpawned] = UnityEngine.Random.Range(0.65f, 1.35f);
                pathDistances[totalSpawned] = 0f;
                segmentIndices[totalSpawned] = 0;
                speeds[totalSpawned] = definition != null ? definition.speed : 4.8f;
                slowMultipliers[totalSpawned] = 1f;
                slowTimers[totalSpawned] = 0f;
                knockbackOffsets[totalSpawned] = Vector3.zero;
                knockbackVelocities[totalSpawned] = Vector3.zero;
                var position = SamplePosition(totalSpawned);
                positions[totalSpawned] = position;
                previousPositions[totalSpawned] = position;
                alive[totalSpawned] = true;
                totalSpawned++;
                activeCount++;
            }
        }

        private void Simulate(float deltaTime)
        {
            if (activeCount <= 0)
            {
                return;
            }

            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i])
                {
                    continue;
                }

                previousPositions[i] = positions[i];
                if (slowTimers[i] > 0f)
                {
                    slowTimers[i] -= deltaTime;
                    if (slowTimers[i] <= 0f)
                    {
                        slowMultipliers[i] = 1f;
                    }
                }

                pathDistances[i] += speeds[i] * Mathf.Clamp(slowMultipliers[i], 0.05f, 1f) * deltaTime;
                if (pathDistances[i] >= path.TotalLength)
                {
                    alive[i] = false;
                    activeCount--;
                    totalResolved++;
                    continue;
                }

                AdvanceSegmentIndex(i);
                ApplyCrowdPressure(i, deltaTime);
                UpdateKnockback(i, deltaTime);
                positions[i] = SamplePosition(i);
            }

            RebuildSpatialBuckets();
        }

        public bool TryGetLeadAimPoint(float radius, out Vector3 aimPoint)
        {
            aimPoint = Vector3.zero;
            var index = FindBestTarget(Vector3.zero, float.PositiveInfinity, true, TowerTargetingMode.First);
            if (index < 0)
            {
                return false;
            }

            var lookBackDistance = Mathf.Max(0.2f, radius * 0.55f);
            var speedLead = Mathf.Clamp(speeds[index] * 0.22f, 0f, radius * 0.28f);
            var distance = Mathf.Max(0f, pathDistances[index] + speedLead - lookBackDistance);
            aimPoint = SampleRoute(distance, segmentIndices[index]);
            return true;
        }

        public bool TryGetTargetPosition(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            var index = FindBestTarget(position, range, canHitFlying, targetingMode);
            if (index < 0)
            {
                return false;
            }

            targetPosition = positions[index];
            return true;
        }

        public float DamageTarget(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode, float damage, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            var index = FindBestTarget(position, range, canHitFlying, targetingMode);
            if (index < 0)
            {
                return 0f;
            }

            targetPosition = positions[index];
            return ApplyDamage(index, damage);
        }

        public float DamageInRadius(Vector3 center, float radius, float damage, int maxTargets, out int hitCount)
        {
            hitCount = 0;
            if (damage <= 0f || radius <= 0f || activeCount <= 0)
            {
                return 0f;
            }

            var radiusSq = radius * radius;
            var appliedDamage = 0f;
            var targetLimit = Mathf.Max(0, maxTargets);
            CollectNearbyIndices(center, radius, nearbyIndices, targetLimit > 0 ? targetLimit * 3 : 0);
            for (var candidate = 0; candidate < nearbyIndices.Count; candidate++)
            {
                var i = nearbyIndices[candidate];
                if (!alive[i])
                {
                    continue;
                }

                var offset = positions[i] - center;
                if (offset.x * offset.x + offset.z * offset.z > radiusSq)
                {
                    continue;
                }

                appliedDamage += ApplyDamage(i, damage);
                hitCount++;
                if (targetLimit > 0 && hitCount >= targetLimit)
                {
                    break;
                }
            }

            return appliedDamage;
        }

        public void ApplySlowAura(Vector3 center, float radius, float slowPercent, float capacity)
        {
            if (slowPercent <= 0f || capacity <= 0f || activeCount <= 0)
            {
                return;
            }

            var radiusSq = radius * radius;
            var usedCapacity = 0f;
            var multiplier = Mathf.Clamp01(1f - slowPercent);
            CollectNearbyIndices(center, radius, nearbyIndices, 160);
            for (var candidate = 0; candidate < nearbyIndices.Count; candidate++)
            {
                var i = nearbyIndices[candidate];
                if (!alive[i])
                {
                    continue;
                }

                var offset = positions[i] - center;
                if (offset.x * offset.x + offset.z * offset.z > radiusSq)
                {
                    continue;
                }

                var cost = Mathf.Max(0.1f, definitions[i] != null ? definitions[i].mass : 1f);
                if (usedCapacity + cost > capacity)
                {
                    continue;
                }

                slowMultipliers[i] = Mathf.Min(slowMultipliers[i], multiplier);
                slowTimers[i] = Mathf.Max(slowTimers[i], 0.35f);
                usedCapacity += cost;
            }
        }

        public float DamageAndKnockbackInRadius(Vector3 center, float radius, float damage, float knockbackDistance, int maxTargets, out int hitCount)
        {
            hitCount = 0;
            if ((damage <= 0f && knockbackDistance <= 0f) || radius <= 0f || activeCount <= 0)
            {
                return 0f;
            }

            var radiusSq = radius * radius;
            var appliedDamage = 0f;
            var targetLimit = Mathf.Max(0, maxTargets);
            CollectNearbyIndices(center, radius, nearbyIndices, targetLimit > 0 ? targetLimit * 3 : 0);
            for (var candidate = 0; candidate < nearbyIndices.Count; candidate++)
            {
                var i = nearbyIndices[candidate];
                if (!alive[i])
                {
                    continue;
                }

                var offset = positions[i] - center;
                var distanceSq = offset.x * offset.x + offset.z * offset.z;
                if (distanceSq > radiusSq)
                {
                    continue;
                }

                if (knockbackDistance > 0f)
                {
                    var direction = distanceSq > 0.0001f ? offset.normalized : segmentSides[Mathf.Clamp(segmentIndices[i], 0, segmentSides.Length - 1)];
                    var falloff = 1f - Mathf.Clamp01(Mathf.Sqrt(distanceSq) / radius);
                    knockbackVelocities[i] += direction * knockbackDistance * Mathf.Lerp(2.5f, 8f, falloff);
                }

                if (damage > 0f)
                {
                    appliedDamage += ApplyDamage(i, damage);
                }

                hitCount++;
                if (targetLimit > 0 && hitCount >= targetLimit)
                {
                    break;
                }
            }

            return appliedDamage;
        }

        private int FindBestTarget(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode)
        {
            if (activeCount <= 0)
            {
                return -1;
            }

            var rangeSq = range * range;
            var bestIndex = -1;
            var bestDistanceSq = float.PositiveInfinity;
            var bestScore = float.MinValue;
            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i] || definitions[i] == null || (definitions[i].isFlying && !canHitFlying))
                {
                    continue;
                }

                var distanceSq = (positions[i] - position).sqrMagnitude;
                if (distanceSq > rangeSq)
                {
                    continue;
                }

                var score = -distanceSq;
                switch (targetingMode)
                {
                    case TowerTargetingMode.First:
                        score = pathDistances[i];
                        break;
                    case TowerTargetingMode.Last:
                        score = -pathDistances[i];
                        break;
                    case TowerTargetingMode.HighestHealth:
                        score = health[i];
                        break;
                }

                if (bestIndex < 0 || score > bestScore || (Mathf.Approximately(score, bestScore) && distanceSq < bestDistanceSq))
                {
                    bestIndex = i;
                    bestScore = score;
                    bestDistanceSq = distanceSq;
                }
            }

            return bestIndex;
        }

        private float ApplyDamage(int index, float damage)
        {
            if (index < 0 || index >= totalSpawned || !alive[index] || damage <= 0f)
            {
                return 0f;
            }

            var appliedDamage = Mathf.Min(health[index], damage);
            health[index] -= damage;
            if (health[index] <= 0f)
            {
                alive[index] = false;
                activeCount--;
                totalResolved++;
            }

            return appliedDamage;
        }

        private Vector3 SamplePosition(int index)
        {
            var distance = pathDistances[index];
            var segment = Mathf.Clamp(segmentIndices[index], 0, segmentStarts.Length - 1);
            var center = segmentStarts[segment] + segmentDirections[segment] * Mathf.Max(0f, distance - segmentStartDistances[segment]);
            var side = segmentSides[segment];
            var longWave = Mathf.Sin(distance * 0.72f + laneDriftPhases[index]) * 0.28f;
            var smallWave = Mathf.Sin(distance * 2.4f * laneDriftSpeeds[index] + index * 0.37f) * 0.1f;
            var weave = longWave + smallWave;
            return center + side * Mathf.Clamp(laneOffsets[index] + weave, -RoadHalfWidth + 0.2f, RoadHalfWidth - 0.2f) + knockbackOffsets[index];
        }

        private void ApplyCrowdPressure(int index, float deltaTime)
        {
            if (activeCount <= 1)
            {
                return;
            }

            var position = positions[index];
            CollectNearbyIndices(position, PressureRadius, nearbyIndices, MaxPressureNeighbors + 1);
            if (nearbyIndices.Count <= 1)
            {
                return;
            }

            var side = segmentSides[Mathf.Clamp(segmentIndices[index], 0, segmentSides.Length - 1)];
            var lateralPush = 0f;
            var checks = 0;
            var radiusSq = PressureRadius * PressureRadius;
            for (var candidate = 0; candidate < nearbyIndices.Count && checks < MaxPressureNeighbors; candidate++)
            {
                var other = nearbyIndices[candidate];
                if (other == index || !alive[other])
                {
                    continue;
                }

                var offset = position - positions[other];
                var distanceSq = offset.x * offset.x + offset.z * offset.z;
                if (distanceSq <= 0.0001f || distanceSq > radiusSq)
                {
                    continue;
                }

                var distance = Mathf.Sqrt(distanceSq);
                lateralPush += Vector3.Dot(offset / distance, side) * (1f - distance / PressureRadius);
                checks++;
            }

            if (checks <= 0)
            {
                return;
            }

            laneOffsets[index] = Mathf.Clamp(laneOffsets[index] + lateralPush * deltaTime * 1.1f, -RoadHalfWidth + 0.28f, RoadHalfWidth - 0.28f);
        }

        private void UpdateKnockback(int index, float deltaTime)
        {
            var velocity = knockbackVelocities[index];
            if (velocity.sqrMagnitude <= 0.0001f && knockbackOffsets[index].sqrMagnitude <= 0.0001f)
            {
                return;
            }

            knockbackOffsets[index] += velocity * deltaTime;
            knockbackVelocities[index] = Vector3.MoveTowards(velocity, Vector3.zero, deltaTime * 4.5f);
            knockbackOffsets[index] = Vector3.MoveTowards(knockbackOffsets[index], Vector3.zero, deltaTime * 1.25f);
            if (knockbackOffsets[index].sqrMagnitude > 9f)
            {
                knockbackOffsets[index] = knockbackOffsets[index].normalized * 3f;
            }
        }

        private void RebuildSpatialBuckets()
        {
            foreach (var bucket in spatialBuckets.Values)
            {
                bucket.Clear();
            }

            if (positions == null)
            {
                return;
            }

            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i])
                {
                    continue;
                }

                var key = GetSpatialKey(positions[i]);
                if (!spatialBuckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(16);
                    spatialBuckets.Add(key, bucket);
                }

                bucket.Add(i);
            }
        }

        private void CollectNearbyIndices(Vector3 center, float radius, List<int> results, int maxResults)
        {
            results.Clear();
            if (spatialBuckets.Count == 0)
            {
                var fallbackLimit = maxResults > 0 ? maxResults : totalSpawned;
                for (var i = 0; i < totalSpawned && results.Count < fallbackLimit; i++)
                {
                    if (alive[i])
                    {
                        results.Add(i);
                    }
                }

                return;
            }

            var min = GetSpatialKey(center - new Vector3(radius, 0f, radius));
            var max = GetSpatialKey(center + new Vector3(radius, 0f, radius));
            for (var x = min.x; x <= max.x; x++)
            {
                for (var y = min.y; y <= max.y; y++)
                {
                    if (!spatialBuckets.TryGetValue(new Vector2Int(x, y), out var bucket))
                    {
                        continue;
                    }

                    for (var i = 0; i < bucket.Count; i++)
                    {
                        results.Add(bucket[i]);
                        if (maxResults > 0 && results.Count >= maxResults)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private static Vector2Int GetSpatialKey(Vector3 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / SpatialCellSize), Mathf.FloorToInt(position.z / SpatialCellSize));
        }

        private Vector3 SampleRoute(float distance, int preferredSegment)
        {
            var segment = Mathf.Clamp(preferredSegment, 0, segmentStarts.Length - 1);
            while (segment < segmentEndDistances.Length - 1 && distance > segmentEndDistances[segment])
            {
                segment++;
            }

            while (segment > 0 && distance < segmentStartDistances[segment])
            {
                segment--;
            }

            return segmentStarts[segment] + segmentDirections[segment] * Mathf.Max(0f, distance - segmentStartDistances[segment]);
        }

        private void AdvanceSegmentIndex(int enemyIndex)
        {
            var segment = segmentIndices[enemyIndex];
            while (segment < segmentEndDistances.Length - 1 && pathDistances[enemyIndex] > segmentEndDistances[segment])
            {
                segment++;
            }

            segmentIndices[enemyIndex] = segment;
        }

        private void BuildRouteCache()
        {
            var points = path.Waypoints;
            if (points == null || points.Count < 2)
            {
                segmentStarts = null;
                return;
            }

            var segmentCount = points.Count - 1;
            segmentStarts = new Vector3[segmentCount];
            segmentDirections = new Vector3[segmentCount];
            segmentSides = new Vector3[segmentCount];
            segmentStartDistances = new float[segmentCount];
            segmentEndDistances = new float[segmentCount];
            var distance = 0f;
            for (var i = 0; i < segmentCount; i++)
            {
                var from = points[i];
                var to = points[i + 1];
                var delta = to - from;
                delta.y = 0f;
                var length = Mathf.Max(0.001f, delta.magnitude);
                var direction = delta / length;
                segmentStarts[i] = from;
                segmentDirections[i] = direction;
                segmentSides[i] = Vector3.Cross(Vector3.up, direction);
                segmentStartDistances[i] = distance;
                distance += length;
                segmentEndDistances[i] = distance;
            }
        }

        private void DrawInstances()
        {
            var camera = Camera.main;
            DrawInstancesForMaterial(camera, material, HordeDrawMode.Normal);
            if (slowedMaterial != null)
            {
                DrawInstancesForMaterial(camera, slowedMaterial, HordeDrawMode.Slowed);
            }

            if (knockedMaterial != null)
            {
                DrawInstancesForMaterial(camera, knockedMaterial, HordeDrawMode.Knocked);
            }
        }

        private void DrawInstancesForMaterial(Camera camera, Material drawMaterial, HordeDrawMode mode)
        {
            var batchCount = 0;
            var scale = Vector3.one * VisualRadius;
            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i])
                {
                    continue;
                }

                var isSlowed = slowTimers != null && slowTimers[i] > 0f && slowMultipliers[i] < 0.995f;
                var isKnocked = knockbackOffsets != null && knockbackOffsets[i].sqrMagnitude > 0.03f;
                if (mode == HordeDrawMode.Normal && (isSlowed || isKnocked))
                {
                    continue;
                }

                if (mode == HordeDrawMode.Slowed && (!isSlowed || isKnocked))
                {
                    continue;
                }

                if (mode == HordeDrawMode.Knocked && !isKnocked)
                {
                    continue;
                }

                if (camera != null && !IsVisible(camera, positions[i]))
                {
                    continue;
                }

                matrixBatch[batchCount++] = Matrix4x4.TRS(positions[i], Quaternion.identity, scale);
                if (batchCount >= InstanceBatchSize)
                {
                    FlushBatch(drawMaterial, batchCount);
                    batchCount = 0;
                }
            }

            if (batchCount > 0)
            {
                FlushBatch(drawMaterial, batchCount);
            }
        }

        private static bool IsVisible(Camera camera, Vector3 position)
        {
            var viewport = camera.WorldToViewportPoint(position);
            return viewport.z > 0f && viewport.x > -0.08f && viewport.x < 1.08f && viewport.y > -0.08f && viewport.y < 1.08f;
        }

        private void FlushBatch(Material drawMaterial, int count)
        {
            Graphics.DrawMeshInstanced(mesh, 0, drawMaterial, matrixBatch, count, properties, ShadowCastingMode.Off, false, gameObject.layer);
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

        private enum HordeDrawMode
        {
            Normal,
            Slowed,
            Knocked
        }
    }
}
