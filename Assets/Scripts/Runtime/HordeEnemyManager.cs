using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private const float CombatTargetCellSize = 4.5f;
        private const float PressureRadius = 0.62f;
        private const float LaneDamping = 4.2f;
        private const float LaneWallBounce = 7.5f;
        private const int OffscreenDetailStride = 8;
        private const int DetailedPerfSampleStride = 15;
        private const int MaxPressureNeighbors = 8;

        private readonly List<EnemyDefinition> spawnSequence = new();
        private readonly Matrix4x4[] matrixBatch = new Matrix4x4[InstanceBatchSize];
        private readonly Dictionary<Vector2Int, List<int>> spatialBuckets = new();
        private readonly Dictionary<Vector2Int, List<ICombatTarget>> combatTargetBuckets = new();
        private readonly List<int> nearbyIndices = new(192);
        private readonly List<ICombatTarget> nearbyCombatTargets = new(32);
        private readonly Dictionary<ICombatTarget, float> frameTargetBlockedMass = new();
        private Vector3[] positions;
        private Vector3[] previousPositions;
        private float[] pathDistances;
        private float[] laneOffsets;
        private float[] laneDriftPhases;
        private float[] laneDriftSpeeds;
        private float[] laneDriftAmplitudes;
        private float[] laneVelocities;
        private float[] crowdSpeedFactors;
        private float[] forwardVisualOffsets;
        private float[] visualScales;
        private float[] visualPulsePhases;
        private float[] visualPulseSpeeds;
        private float[] speeds;
        private float[] slowMultipliers;
        private float[] slowTimers;
        private float[] attackTimers;
        private float[] spawnTimes;
        private float[] health;
        private float[] burnDamagePerSecond;
        private float[] burnTimers;
        private Vector3[] knockbackOffsets;
        private Vector3[] knockbackVelocities;
        private int[] segmentIndices;
        private EnemyDefinition[] definitions;
        private bool[] alive;
        private IReadOnlyList<ICombatTarget> combatTargets;
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
        private MaterialPropertyBlock properties;
        private float elapsed;
        private int totalSpawned;
        private int activeCount;
        private int totalResolved;
        private float lastSpawnMs;
        private float lastSimMs;
        private float lastBucketMs;
        private float lastDrawMs;
        private float lastStatusMs;
        private float lastTierMs;
        private float lastCombatMs;
        private float lastMovementMs;
        private float lastSegmentMs;
        private float lastCrowdMs;
        private float lastKnockbackMs;
        private float lastSampleMs;
        private int lastVisibleDrawn;
        private int lastFullFidelityCount;
        private int lastCheapFidelityCount;
        private int lastNearCombatCount;
        private bool running;

        public int TotalSpawned => totalSpawned;
        public int ActiveCount => activeCount;
        public int TotalResolved => totalResolved;
        public bool IsRunning => running;
        public bool IsComplete => running && totalSpawned >= spawnSequence.Count && activeCount == 0;
        public HordePerformanceSnapshot Performance => new(
            lastSpawnMs,
            lastSimMs,
            lastBucketMs,
            lastDrawMs,
            lastStatusMs,
            lastTierMs,
            lastCombatMs,
            lastMovementMs,
            lastSegmentMs,
            lastCrowdMs,
            lastKnockbackMs,
            lastSampleMs,
            lastVisibleDrawn,
            lastFullFidelityCount,
            lastCheapFidelityCount,
            lastNearCombatCount,
            material != null && material.shader != null ? material.shader.name : "none");

        public void SetCombatTargets(IReadOnlyList<ICombatTarget> targets)
        {
            combatTargets = targets;
        }

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
            laneDriftAmplitudes = new float[count];
            laneVelocities = new float[count];
            crowdSpeedFactors = new float[count];
            forwardVisualOffsets = new float[count];
            visualScales = new float[count];
            visualPulsePhases = new float[count];
            visualPulseSpeeds = new float[count];
            speeds = new float[count];
            slowMultipliers = new float[count];
            slowTimers = new float[count];
            attackTimers = new float[count];
            spawnTimes = new float[count];
            health = new float[count];
            burnDamagePerSecond = new float[count];
            burnTimers = new float[count];
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
            positions = null;
            previousPositions = null;
            pathDistances = null;
            laneOffsets = null;
            laneDriftPhases = null;
            laneDriftSpeeds = null;
            laneDriftAmplitudes = null;
            laneVelocities = null;
            crowdSpeedFactors = null;
            forwardVisualOffsets = null;
            visualScales = null;
            visualPulsePhases = null;
            visualPulseSpeeds = null;
            speeds = null;
            slowMultipliers = null;
            slowTimers = null;
            attackTimers = null;
            spawnTimes = null;
            health = null;
            burnDamagePerSecond = null;
            burnTimers = null;
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
            combatTargetBuckets.Clear();
            nearbyIndices.Clear();
            nearbyCombatTargets.Clear();
            frameTargetBlockedMass.Clear();
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
            var spawnStart = Stopwatch.GetTimestamp();
            SpawnDueEnemies();
            lastSpawnMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - spawnStart);
            var simStart = Stopwatch.GetTimestamp();
            Simulate(deltaTime);
            lastSimMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - simStart) - lastBucketMs;
            if (lastSimMs < 0f)
            {
                lastSimMs = 0f;
            }
        }

        private void LateUpdate()
        {
            if (!running || mesh == null || material == null || positions == null)
            {
                return;
            }

            var drawStart = Stopwatch.GetTimestamp();
            DrawInstances();
            lastDrawMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - drawStart);
        }

        private void SpawnDueEnemies()
        {
            while (totalSpawned < spawnSequence.Count && elapsed >= spawnTimes[totalSpawned])
            {
                var definition = spawnSequence[totalSpawned];
                definitions[totalSpawned] = definition;
                health[totalSpawned] = Mathf.Max(1f, definition != null ? definition.maxHealth : 1f);
                laneOffsets[totalSpawned] = UnityEngine.Random.Range(-RoadHalfWidth + 0.35f, RoadHalfWidth - 0.35f);
                var rhythm = UnityEngine.Random.Range(0, 10);
                laneDriftPhases[totalSpawned] = UnityEngine.Random.Range(0f, Mathf.PI * 2f) + rhythm * 0.63f;
                laneDriftSpeeds[totalSpawned] = Mathf.Lerp(0.55f, 1.55f, rhythm / 9f) * UnityEngine.Random.Range(0.92f, 1.08f);
                laneDriftAmplitudes[totalSpawned] = UnityEngine.Random.Range(0.14f, 0.42f);
                laneVelocities[totalSpawned] = UnityEngine.Random.Range(-0.18f, 0.18f);
                crowdSpeedFactors[totalSpawned] = 1f;
                forwardVisualOffsets[totalSpawned] = UnityEngine.Random.Range(-0.38f, 0.38f);
                pathDistances[totalSpawned] = 0f;
                segmentIndices[totalSpawned] = 0;
                speeds[totalSpawned] = definition != null ? definition.speed : 4.8f;
                visualScales[totalSpawned] = (definition != null ? definition.visualScale : 0.45f) * UnityEngine.Random.Range(0.88f, 1.12f);
                visualPulsePhases[totalSpawned] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                visualPulseSpeeds[totalSpawned] = UnityEngine.Random.Range(1.7f, 3.8f);
                slowMultipliers[totalSpawned] = 1f;
                slowTimers[totalSpawned] = 0f;
                attackTimers[totalSpawned] = 0f;
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
                lastBucketMs = 0f;
                lastFullFidelityCount = 0;
                lastCheapFidelityCount = 0;
                lastNearCombatCount = 0;
                ClearDetailedPerformance();
                return;
            }

            var camera = Camera.main;
            var frame = Time.frameCount;
            var sampleDetailedPerformance = frame % DetailedPerfSampleStride == 0;
            if (sampleDetailedPerformance)
            {
                ClearDetailedPerformance();
            }

            var cameraFocus = camera != null ? GetCameraGroundFocus(camera) : Vector3.zero;
            var highFidelityRadius = camera != null ? GetHighFidelityRadius(camera) : float.PositiveInfinity;
            var highFidelityRadiusSq = highFidelityRadius * highFidelityRadius;
            lastFullFidelityCount = 0;
            lastCheapFidelityCount = 0;
            lastNearCombatCount = 0;
            frameTargetBlockedMass.Clear();
            var targetIndexStart = sampleDetailedPerformance ? Stopwatch.GetTimestamp() : 0L;
            RebuildCombatTargetBuckets();
            if (sampleDetailedPerformance)
            {
                lastTierMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - targetIndexStart);
            }

            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i])
                {
                    continue;
                }

                var sectionStart = sampleDetailedPerformance ? Stopwatch.GetTimestamp() : 0L;
                previousPositions[i] = positions[i];
                if (slowTimers[i] > 0f)
                {
                    slowTimers[i] -= deltaTime;
                    if (slowTimers[i] <= 0f)
                    {
                        slowMultipliers[i] = 1f;
                    }
                }

                if (burnTimers[i] > 0f)
                {
                    burnTimers[i] -= deltaTime;
                    ApplyDamage(i, burnDamagePerSecond[i] * deltaTime);
                    if (!alive[i])
                    {
                        if (sampleDetailedPerformance)
                        {
                            lastStatusMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                        }

                        continue;
                    }

                    if (burnTimers[i] <= 0f)
                    {
                        burnDamagePerSecond[i] = 0f;
                    }
                }
                if (sampleDetailedPerformance)
                {
                    lastStatusMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }

                var nearFocus = IsNearCameraFocus(positions[i], cameraFocus, highFidelityRadiusSq);
                var staggeredTick = (i + frame) % OffscreenDetailStride == 0;
                var nearCombat = IsNearCombatTarget(positions[i]);
                var detailedTick = nearCombat || nearFocus || staggeredTick;
                if (detailedTick)
                {
                    lastFullFidelityCount++;
                    if (nearCombat)
                    {
                        lastNearCombatCount++;
                    }
                }
                else
                {
                    lastCheapFidelityCount++;
                }
                if (sampleDetailedPerformance)
                {
                    lastTierMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }

                var target = nearCombat || staggeredTick ? FindBlockingTarget(i) : null;
                if (target != null)
                {
                    AttackCombatTarget(i, target, deltaTime);
                }
                if (sampleDetailedPerformance)
                {
                    lastCombatMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }

                var combatMultiplier = target == null ? 1f : 0.08f;
                var crowdMultiplier = crowdSpeedFactors != null ? Mathf.Clamp(crowdSpeedFactors[i], 0.58f, 1.08f) : 1f;
                pathDistances[i] += speeds[i] * Mathf.Clamp(slowMultipliers[i], 0.05f, 1f) * combatMultiplier * crowdMultiplier * deltaTime;
                if (crowdSpeedFactors != null)
                {
                    crowdSpeedFactors[i] = Mathf.MoveTowards(crowdSpeedFactors[i], 1f, deltaTime * 2.4f);
                }
                if (sampleDetailedPerformance)
                {
                    lastMovementMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }

                if (pathDistances[i] >= path.TotalLength)
                {
                    alive[i] = false;
                    activeCount--;
                    totalResolved++;
                    continue;
                }

                AdvanceSegmentIndex(i);
                if (sampleDetailedPerformance)
                {
                    lastSegmentMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }

                if (detailedTick)
                {
                    ApplyCrowdPressure(i, deltaTime);
                    if (sampleDetailedPerformance)
                    {
                        lastCrowdMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                        sectionStart = Stopwatch.GetTimestamp();
                    }
                }
                ApplyLaneInertia(i, deltaTime);
                UpdateKnockback(i, deltaTime);
                if (sampleDetailedPerformance)
                {
                    if (!detailedTick)
                    {
                        lastCrowdMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                        sectionStart = Stopwatch.GetTimestamp();
                    }

                    lastKnockbackMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }

                positions[i] = SamplePosition(i);
                if (sampleDetailedPerformance)
                {
                    lastSampleMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                }
            }

            var bucketStart = Stopwatch.GetTimestamp();
            RebuildSpatialBuckets();
            lastBucketMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - bucketStart);
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

        public float DamageAndKnockbackInRadius(
            Vector3 center,
            float radius,
            float damage,
            float knockbackDistance,
            int maxTargets,
            out int hitCount,
            float burnDamagePerTick = 0f,
            float burnTicksPerSecond = 0f,
            float burnDuration = 0f,
            int maxBurnStacks = 0)
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

                ApplyBurn(i, burnDamagePerTick, burnTicksPerSecond, burnDuration, maxBurnStacks);

                hitCount++;
                if (targetLimit > 0 && hitCount >= targetLimit)
                {
                    break;
                }
            }

            return appliedDamage;
        }

        private void ApplyBurn(int index, float damagePerTick, float ticksPerSecond, float duration, int maxStacks)
        {
            if (index < 0 || index >= totalSpawned || !alive[index] || damagePerTick <= 0f || ticksPerSecond <= 0f || duration <= 0f)
            {
                return;
            }

            var stackCap = Mathf.Max(1, maxStacks);
            var stackDamagePerSecond = damagePerTick * ticksPerSecond;
            var currentStacks = burnDamagePerSecond[index] > 0f
                ? Mathf.RoundToInt(burnDamagePerSecond[index] / Mathf.Max(0.0001f, stackDamagePerSecond))
                : 0;
            var nextStacks = Mathf.Clamp(currentStacks + 1, 1, stackCap);
            burnDamagePerSecond[index] = stackDamagePerSecond * nextStacks;
            burnTimers[index] = Mathf.Max(burnTimers[index], duration);
            slowTimers[index] = Mathf.Max(slowTimers[index], 0.2f);
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
            var visualDistance = Mathf.Clamp(distance + forwardVisualOffsets[index], segmentStartDistances[segment], segmentEndDistances[segment]);
            var center = segmentStarts[segment] + segmentDirections[segment] * Mathf.Max(0f, visualDistance - segmentStartDistances[segment]);
            var side = segmentSides[segment];
            var rhythmTime = elapsed * laneDriftSpeeds[index];
            var longWave = Mathf.Sin(distance * 0.62f + rhythmTime + laneDriftPhases[index]) * laneDriftAmplitudes[index];
            var smallWave = Mathf.Sin(distance * 2.15f + rhythmTime * 1.73f + index * 0.41f) * 0.08f;
            var weave = longWave + smallWave;
            var effectiveHalfWidth = GetEffectiveRoadHalfWidth(index, segment);
            var minLane = -effectiveHalfWidth + 0.2f;
            var maxLane = effectiveHalfWidth - 0.2f;
            var lane = Mathf.Clamp(laneOffsets[index] + weave, minLane, maxLane);
            return center + side * lane + GetClampedKnockbackOffset(index, segment, lane, minLane, maxLane);
        }

        private float GetEffectiveRoadHalfWidth(int index, int segment)
        {
            var intoSegment = Mathf.Max(0f, pathDistances[index] - segmentStartDistances[segment]);
            var toSegmentEnd = Mathf.Max(0f, segmentEndDistances[segment] - pathDistances[index]);
            var cornerDistance = Mathf.Min(intoSegment, toSegmentEnd);
            var cornerBlend = Mathf.Clamp01(cornerDistance / 2.1f);
            return Mathf.Lerp(RoadHalfWidth * 0.68f, RoadHalfWidth, cornerBlend);
        }

        private Vector3 GetClampedKnockbackOffset(int index, int segment, float lane, float minLane, float maxLane)
        {
            var offset = knockbackOffsets[index];
            if (offset.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            var side = segmentSides[segment];
            var forward = segmentDirections[segment];
            var lateral = Mathf.Clamp(Vector3.Dot(offset, side), minLane - lane, maxLane - lane);
            var longitudinal = Mathf.Clamp(Vector3.Dot(offset, forward), -0.9f, 0.9f);
            return side * lateral + forward * longitudinal;
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
            var forwardPressure = 0f;
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
                var direction = offset / distance;
                var pressure = 1f - distance / PressureRadius;
                lateralPush += Vector3.Dot(direction, side) * pressure;
                forwardPressure += pressure;
                checks++;
            }

            if (checks <= 0)
            {
                return;
            }

            laneVelocities[index] += lateralPush * deltaTime * 5.2f;
            crowdSpeedFactors[index] = Mathf.Min(crowdSpeedFactors[index], Mathf.Lerp(1f, 0.68f, Mathf.Clamp01(forwardPressure / MaxPressureNeighbors)));
        }

        private void ApplyLaneInertia(int index, float deltaTime)
        {
            if (laneVelocities == null || segmentStarts == null)
            {
                return;
            }

            var segment = Mathf.Clamp(segmentIndices[index], 0, segmentStarts.Length - 1);
            var effectiveHalfWidth = GetEffectiveRoadHalfWidth(index, segment);
            var minLane = -effectiveHalfWidth + 0.24f;
            var maxLane = effectiveHalfWidth - 0.24f;
            var lane = laneOffsets[index];
            var velocity = laneVelocities[index];

            if (lane < minLane + 0.36f)
            {
                velocity += (minLane + 0.36f - lane) * LaneWallBounce * deltaTime;
            }
            else if (lane > maxLane - 0.36f)
            {
                velocity -= (lane - (maxLane - 0.36f)) * LaneWallBounce * deltaTime;
            }

            velocity = Mathf.MoveTowards(velocity, 0f, LaneDamping * deltaTime);
            lane = Mathf.Clamp(lane + velocity * deltaTime, minLane, maxLane);
            if ((lane <= minLane && velocity < 0f) || (lane >= maxLane && velocity > 0f))
            {
                velocity *= -0.24f;
            }

            laneOffsets[index] = lane;
            laneVelocities[index] = Mathf.Clamp(velocity, -2.2f, 2.2f);
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
            if (knockbackOffsets[index].sqrMagnitude > 3.24f)
            {
                knockbackOffsets[index] = knockbackOffsets[index].normalized * 1.8f;
            }
        }

        private ICombatTarget FindBlockingTarget(int index)
        {
            if (combatTargets == null || combatTargets.Count == 0 || definitions[index] == null)
            {
                return null;
            }

            var enemyMass = Mathf.Max(0.1f, definitions[index].mass);
            var enemyPosition = positions[index];
            ICombatTarget bestTarget = null;
            var bestDistanceSq = float.PositiveInfinity;
            CollectNearbyCombatTargets(enemyPosition, 3.4f, nearbyCombatTargets);
            for (var i = nearbyCombatTargets.Count - 1; i >= 0; i--)
            {
                var target = nearbyCombatTargets[i];
                if (target.BlockCapacity <= 0f)
                {
                    continue;
                }

                frameTargetBlockedMass.TryGetValue(target, out var blockedMass);
                if (blockedMass + enemyMass > target.BlockCapacity)
                {
                    continue;
                }

                var range = Mathf.Max(0.35f, target.CombatRadius + VisualRadius * 1.2f);
                var offset = target.Position - enemyPosition;
                var distanceSq = offset.x * offset.x + offset.z * offset.z;
                if (distanceSq > range * range || distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestTarget = target;
                bestDistanceSq = distanceSq;
            }

            if (bestTarget != null)
            {
                frameTargetBlockedMass.TryGetValue(bestTarget, out var blockedMass);
                frameTargetBlockedMass[bestTarget] = blockedMass + Mathf.Max(0.1f, definitions[index].mass);
            }

            return bestTarget;
        }

        private bool IsNearCombatTarget(Vector3 position)
        {
            if (combatTargets == null || combatTargets.Count == 0)
            {
                return false;
            }

            const float margin = 2.4f;
            CollectNearbyCombatTargets(position, margin + 1.4f, nearbyCombatTargets);
            for (var i = nearbyCombatTargets.Count - 1; i >= 0; i--)
            {
                var target = nearbyCombatTargets[i];
                var range = Mathf.Max(0.75f, target.CombatRadius + margin);
                var offset = target.Position - position;
                if (offset.x * offset.x + offset.z * offset.z <= range * range)
                {
                    return true;
                }
            }

            return false;
        }

        private void AttackCombatTarget(int index, ICombatTarget target, float deltaTime)
        {
            attackTimers[index] -= deltaTime;
            if (attackTimers[index] > 0f || definitions[index] == null)
            {
                return;
            }

            var multiplier = target.TargetKind == CombatTargetKind.Barrier
                ? definitions[index].wallDamageMultiplier
                : definitions[index].alliedDamageMultiplier;
            target.TakeDamage(definitions[index].attackDamage * Mathf.Max(0f, multiplier), null);
            attackTimers[index] = Mathf.Max(0.15f, definitions[index].attackInterval);
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

        private void RebuildCombatTargetBuckets()
        {
            foreach (var bucket in combatTargetBuckets.Values)
            {
                bucket.Clear();
            }

            if (combatTargets == null || combatTargets.Count == 0)
            {
                return;
            }

            for (var i = combatTargets.Count - 1; i >= 0; i--)
            {
                var target = combatTargets[i];
                if (target == null || !target.IsAlive)
                {
                    continue;
                }

                var key = GetCombatTargetKey(target.Position);
                if (!combatTargetBuckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<ICombatTarget>(4);
                    combatTargetBuckets.Add(key, bucket);
                }

                bucket.Add(target);
            }
        }

        private void CollectNearbyCombatTargets(Vector3 center, float radius, List<ICombatTarget> results)
        {
            results.Clear();
            if (combatTargetBuckets.Count == 0)
            {
                if (combatTargets == null)
                {
                    return;
                }

                for (var i = combatTargets.Count - 1; i >= 0; i--)
                {
                    var target = combatTargets[i];
                    if (target != null && target.IsAlive)
                    {
                        results.Add(target);
                    }
                }

                return;
            }

            var min = GetCombatTargetKey(center - new Vector3(radius, 0f, radius));
            var max = GetCombatTargetKey(center + new Vector3(radius, 0f, radius));
            for (var x = min.x; x <= max.x; x++)
            {
                for (var y = min.y; y <= max.y; y++)
                {
                    if (!combatTargetBuckets.TryGetValue(new Vector2Int(x, y), out var bucket))
                    {
                        continue;
                    }

                    for (var i = 0; i < bucket.Count; i++)
                    {
                        results.Add(bucket[i]);
                    }
                }
            }
        }

        private static Vector2Int GetSpatialKey(Vector3 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / SpatialCellSize), Mathf.FloorToInt(position.z / SpatialCellSize));
        }

        private static Vector2Int GetCombatTargetKey(Vector3 position)
        {
            return new Vector2Int(Mathf.FloorToInt(position.x / CombatTargetCellSize), Mathf.FloorToInt(position.z / CombatTargetCellSize));
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
            lastVisibleDrawn = 0;
            DrawInstancesForMaterial(camera, material, HordeDrawMode.Normal);
            if (slowedMaterial != null)
            {
                DrawInstancesForMaterial(camera, slowedMaterial, HordeDrawMode.Slowed);
            }

        }

        private void DrawInstancesForMaterial(Camera camera, Material drawMaterial, HordeDrawMode mode)
        {
            var batchCount = 0;
            var drawTime = Time.time;
            for (var i = 0; i < totalSpawned; i++)
            {
                if (!alive[i])
                {
                    continue;
                }

                var isSlowed = slowTimers != null && slowTimers[i] > 0f && slowMultipliers[i] < 0.995f;
                if (mode == HordeDrawMode.Normal && isSlowed)
                {
                    continue;
                }

                if (mode == HordeDrawMode.Slowed && !isSlowed)
                {
                    continue;
                }

                if (camera != null && !IsVisible(camera, positions[i], 0.08f))
                {
                    continue;
                }

                var scale = Vector3.one * GetVisualRadius(i);
                if (visualPulsePhases != null && visualPulseSpeeds != null)
                {
                    var pulse = 1f + Mathf.Sin(drawTime * visualPulseSpeeds[i] + visualPulsePhases[i]) * 0.045f;
                    scale *= pulse;
                }

                matrixBatch[batchCount++] = Matrix4x4.TRS(positions[i], Quaternion.identity, scale);
                lastVisibleDrawn++;
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

        private static bool IsVisible(Camera camera, Vector3 position, float margin)
        {
            var viewport = camera.WorldToViewportPoint(position);
            return viewport.z > 0f && viewport.x > -margin && viewport.x < 1f + margin && viewport.y > -margin && viewport.y < 1f + margin;
        }

        private static Vector3 GetCameraGroundFocus(Camera camera)
        {
            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var plane = new Plane(Vector3.up, Vector3.zero);
            return plane.Raycast(ray, out var enter) ? ray.GetPoint(enter) : camera.transform.position;
        }

        private static float GetHighFidelityRadius(Camera camera)
        {
            return Mathf.Clamp(camera.transform.position.y * 0.42f, 12f, 42f);
        }

        private static bool IsNearCameraFocus(Vector3 position, Vector3 focus, float radiusSq)
        {
            var offset = position - focus;
            return offset.x * offset.x + offset.z * offset.z <= radiusSq;
        }

        private void FlushBatch(Material drawMaterial, int count)
        {
            Graphics.DrawMeshInstanced(mesh, 0, drawMaterial, matrixBatch, count, properties, ShadowCastingMode.Off, false, gameObject.layer);
        }

        private float GetVisualRadius(int index)
        {
            if (visualScales == null || index < 0 || index >= visualScales.Length)
            {
                return VisualRadius;
            }

            return Mathf.Clamp(visualScales[index] * 0.62f, VisualRadius * 0.72f, VisualRadius * 1.55f);
        }

        private static float TicksToMilliseconds(long ticks)
        {
            return ticks * 1000f / Stopwatch.Frequency;
        }

        private void ClearDetailedPerformance()
        {
            lastStatusMs = 0f;
            lastTierMs = 0f;
            lastCombatMs = 0f;
            lastMovementMs = 0f;
            lastSegmentMs = 0f;
            lastCrowdMs = 0f;
            lastKnockbackMs = 0f;
            lastSampleMs = 0f;
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
            Slowed
        }

        public readonly struct HordePerformanceSnapshot
        {
            public readonly float SpawnMs;
            public readonly float SimMs;
            public readonly float BucketMs;
            public readonly float DrawMs;
            public readonly float StatusMs;
            public readonly float TierMs;
            public readonly float CombatMs;
            public readonly float MovementMs;
            public readonly float SegmentMs;
            public readonly float CrowdMs;
            public readonly float KnockbackMs;
            public readonly float SampleMs;
            public readonly int VisibleDrawn;
            public readonly int FullFidelity;
            public readonly int CheapFidelity;
            public readonly int NearCombat;
            public readonly string ShaderName;

            public HordePerformanceSnapshot(
                float spawnMs,
                float simMs,
                float bucketMs,
                float drawMs,
                float statusMs,
                float tierMs,
                float combatMs,
                float movementMs,
                float segmentMs,
                float crowdMs,
                float knockbackMs,
                float sampleMs,
                int visibleDrawn,
                int fullFidelity,
                int cheapFidelity,
                int nearCombat,
                string shaderName)
            {
                SpawnMs = spawnMs;
                SimMs = simMs;
                BucketMs = bucketMs;
                DrawMs = drawMs;
                StatusMs = statusMs;
                TierMs = tierMs;
                CombatMs = combatMs;
                MovementMs = movementMs;
                SegmentMs = segmentMs;
                CrowdMs = crowdMs;
                KnockbackMs = knockbackMs;
                SampleMs = sampleMs;
                VisibleDrawn = visibleDrawn;
                FullFidelity = fullFidelity;
                CheapFidelity = cheapFidelity;
                NearCombat = nearCombat;
                ShaderName = shaderName;
            }
        }
    }
}
