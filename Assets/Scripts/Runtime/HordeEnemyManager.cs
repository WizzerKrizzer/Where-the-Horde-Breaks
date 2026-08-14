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

        private readonly List<EnemyDefinition> spawnSequence = new();
        private readonly Matrix4x4[] matrixBatch = new Matrix4x4[InstanceBatchSize];
        private Vector3[] positions;
        private Vector3[] previousPositions;
        private float[] pathDistances;
        private float[] laneOffsets;
        private float[] laneDriftPhases;
        private float[] laneDriftSpeeds;
        private float[] speeds;
        private float[] spawnTimes;
        private float[] health;
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
            spawnTimes = new float[count];
            health = new float[count];
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
            positions = null;
            previousPositions = null;
            pathDistances = null;
            laneOffsets = null;
            laneDriftPhases = null;
            laneDriftSpeeds = null;
            speeds = null;
            spawnTimes = null;
            health = null;
            segmentIndices = null;
            definitions = null;
            alive = null;
            segmentStarts = null;
            segmentDirections = null;
            segmentSides = null;
            segmentStartDistances = null;
            segmentEndDistances = null;
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
                pathDistances[i] += speeds[i] * deltaTime;
                if (pathDistances[i] >= path.TotalLength)
                {
                    alive[i] = false;
                    activeCount--;
                    totalResolved++;
                    continue;
                }

                AdvanceSegmentIndex(i);
                positions[i] = SamplePosition(i);
            }
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
            for (var i = 0; i < totalSpawned; i++)
            {
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
            return center + side * Mathf.Clamp(laneOffsets[index] + weave, -RoadHalfWidth + 0.2f, RoadHalfWidth - 0.2f);
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
            var batchCount = 0;
            var scale = Vector3.one * VisualRadius;
            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i])
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
                    FlushBatch(batchCount);
                    batchCount = 0;
                }
            }

            if (batchCount > 0)
            {
                FlushBatch(batchCount);
            }
        }

        private static bool IsVisible(Camera camera, Vector3 position)
        {
            var viewport = camera.WorldToViewportPoint(position);
            return viewport.z > 0f && viewport.x > -0.08f && viewport.x < 1.08f && viewport.y > -0.08f && viewport.y < 1.08f;
        }

        private void FlushBatch(int count)
        {
            Graphics.DrawMeshInstanced(mesh, 0, material, matrixBatch, count, properties, ShadowCastingMode.Off, false, gameObject.layer);
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
    }
}
