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
        private const float RoadHalfWidth = 2.65f;
        private const float VisualRadius = 0.34f;
        private const float SpatialCellSize = 1.2f;
        private const float CombatTargetCellSize = 4.5f;
        // The normalized enemy meshes use state.scale as their visible radius.
        // Keep contact only slightly outside the silhouette to avoid visible gaps.
        private const float CollisionDiameter = 0.72f;
        private const float FlowCellSize = 0.62f;
        private const float FlowAcceleration = 8.5f;
        private const float CollisionAcceleration = 18f;
        private const float WallAcceleration = 13f;
        private const float WallInfluenceDistance = 0.9f;
        private const float VelocityDamping = 1.15f;
        private const int OffscreenDetailStride = 8;
        private const int DetailedPerfSampleStride = 15;
        private const int MaxCollisionNeighbors = 16;

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
        private Vector3[] velocities;
        private float[] crowdSpeedFactors;
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
        private Vector3[] knockbackVelocities;
        private EnemyDefinition[] definitions;
        private bool[] alive;
        private Vector4[] gpuControls;
        private Vector2[] gpuImpulses;
        private GpuHordeSimulation.AgentState[] gpuSpawnStates;
        private GpuHordeSimulation gpuSimulation;
        private IReadOnlyList<ICombatTarget> combatTargets;
        private WaveDefinition wave;
        private PathRoute path;
        private HordeFlowField flowField;
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
        private float activeRoadHalfWidth = RoadHalfWidth;
        private float gpuSimulationAccumulator;

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
            gpuSimulation?.OverflowCellCount ?? 0u,
            gpuSimulation?.DroppedAgentCount ?? 0u,
            gpuSimulation?.MaximumCellOccupancy ?? 0u,
            gpuSimulation != null
                ? "GPU Compute / HordeIndirect"
                : material != null && material.shader != null ? material.shader.name : "none");

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
            positions = new Vector3[count];
            previousPositions = new Vector3[count];
            pathDistances = new float[count];
            velocities = new Vector3[count];
            crowdSpeedFactors = new float[count];
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
            knockbackVelocities = new Vector3[count];
            definitions = new EnemyDefinition[count];
            alive = new bool[count];
            gpuControls = new Vector4[count];
            gpuImpulses = new Vector2[count];
            gpuSpawnStates = new GpuHordeSimulation.AgentState[count];
            activeRoadHalfWidth = wave.roadHalfWidth > VisualRadius
                ? wave.roadHalfWidth
                : RoadHalfWidth;
            flowField = new HordeFlowField(path.Waypoints, path.SecondaryWaypoints, activeRoadHalfWidth - VisualRadius, FlowCellSize);
            var cursor = 0f;
            var windowDuration = Mathf.Max(0.01f, wave.spawnInterval);
            var packedSpawnSpan = Mathf.Max(0.02f, windowDuration * 0.42f);
            var packedSpawnStep = Mathf.Max(0.02f, windowDuration * 0.58f);
            var minBurst = Mathf.Max(1, wave.randomSpawnBurstMin);
            var maxBurst = Mathf.Max(minBurst, wave.randomSpawnBurstMax);
            var burstIndex = 0;
            while (burstIndex < count)
            {
                var burst = Mathf.Min(count - burstIndex, UnityEngine.Random.Range(minBurst, maxBurst + 1));
                for (var i = 0; i < burst; i++)
                {
                    spawnTimes[burstIndex + i] = cursor + UnityEngine.Random.Range(0f, packedSpawnSpan);
                }

                Array.Sort(spawnTimes, burstIndex, burst);
                burstIndex += burst;
                cursor += packedSpawnStep;
            }

            mesh = EnemyManager.GetDetailedEnemyMesh();
            material = BootstrapMaterials.Get(new Color(0.1f, 0.9f, 0.18f, 1f));
            material.enableInstancing = true;
            slowedMaterial = BootstrapMaterials.Get(new Color(0.2f, 0.62f, 1f, 1f));
            slowedMaterial.enableInstancing = true;
            properties ??= new MaterialPropertyBlock();
            if (!GpuHordeSimulation.TryCreate(count, flowField, mesh, out gpuSimulation))
            {
                running = false;
                return;
            }
            elapsed = 0f;
            totalSpawned = 0;
            activeCount = 0;
            totalResolved = 0;
            gpuSimulationAccumulator = 0f;
            running = true;
        }

        public void StopWave()
        {
            Clear();
        }

        private void OnDestroy()
        {
            gpuSimulation?.Dispose();
            gpuSimulation = null;
        }

        private void Clear()
        {
            gpuSimulation?.Dispose();
            gpuSimulation = null;
            running = false;
            wave = null;
            spawnSequence.Clear();
            material = null;
            slowedMaterial = null;
            positions = null;
            previousPositions = null;
            pathDistances = null;
            velocities = null;
            crowdSpeedFactors = null;
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
            knockbackVelocities = null;
            definitions = null;
            alive = null;
            gpuControls = null;
            gpuImpulses = null;
            gpuSpawnStates = null;
            flowField = null;
            spatialBuckets.Clear();
            combatTargetBuckets.Clear();
            nearbyIndices.Clear();
            nearbyCombatTargets.Clear();
            frameTargetBlockedMass.Clear();
            elapsed = 0f;
            totalSpawned = 0;
            activeCount = 0;
            totalResolved = 0;
            activeRoadHalfWidth = RoadHalfWidth;
            gpuSimulationAccumulator = 0f;
        }

        private void Update()
        {
            if (!running || wave == null || path == null)
            {
                return;
            }

            const float fixedGpuStep = 1f / 60f;
            const int maximumStepsPerFrame = 32;
            gpuSimulationAccumulator = Mathf.Min(
                gpuSimulationAccumulator + Mathf.Max(0f, Time.deltaTime),
                fixedGpuStep * maximumStepsPerFrame);
            var stepCount = Mathf.Min(
                maximumStepsPerFrame,
                Mathf.FloorToInt((gpuSimulationAccumulator + 0.000001f) / fixedGpuStep));
            if (stepCount <= 0)
            {
                return;
            }

            var spawnTicks = 0L;
            var simulationTicks = 0L;
            for (var step = 0; step < stepCount; step++)
            {
                elapsed += fixedGpuStep;
                var sectionStart = Stopwatch.GetTimestamp();
                SpawnDueEnemies();
                spawnTicks += Stopwatch.GetTimestamp() - sectionStart;
                sectionStart = Stopwatch.GetTimestamp();
                Simulate(fixedGpuStep);
                simulationTicks += Stopwatch.GetTimestamp() - sectionStart;
                gpuSimulationAccumulator -= fixedGpuStep;
            }

            lastSpawnMs = TicksToMilliseconds(spawnTicks);
            lastSimMs = Mathf.Max(0f, TicksToMilliseconds(simulationTicks) - lastBucketMs);
        }

        private void LateUpdate()
        {
            if (!running || mesh == null || positions == null)
            {
                return;
            }

            var drawStart = Stopwatch.GetTimestamp();
            if (gpuSimulation != null)
            {
                gpuSimulation.Draw(mesh, gameObject.layer);
                lastVisibleDrawn = (int)gpuSimulation.VisibleAgentCount;
            }
            else if (material != null)
            {
                DrawInstances();
            }
            lastDrawMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - drawStart);
        }

        private void SpawnDueEnemies()
        {
            var batchStart = totalSpawned;
            while (totalSpawned < spawnSequence.Count && elapsed >= spawnTimes[totalSpawned])
            {
                var definition = spawnSequence[totalSpawned];
                definitions[totalSpawned] = definition;
                health[totalSpawned] = Mathf.Max(1f, definition != null ? definition.maxHealth : 1f);
                crowdSpeedFactors[totalSpawned] = 1f;
                pathDistances[totalSpawned] = 0f;
                speeds[totalSpawned] = definition != null ? definition.speed : 4.8f;
                visualScales[totalSpawned] = (definition != null ? definition.visualScale : 0.45f) * UnityEngine.Random.Range(0.88f, 1.12f);
                visualPulsePhases[totalSpawned] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                visualPulseSpeeds[totalSpawned] = UnityEngine.Random.Range(1.7f, 3.8f);
                slowMultipliers[totalSpawned] = 1f;
                slowTimers[totalSpawned] = 0f;
                attackTimers[totalSpawned] = 0f;
                knockbackVelocities[totalSpawned] = Vector3.zero;
                var lateral = UnityEngine.Random.Range(-activeRoadHalfWidth + VisualRadius, activeRoadHalfWidth - VisualRadius);
                var position = flowField.GetSpawnPoint(lateral);
                position += flowField.GetDirection(position) * UnityEngine.Random.Range(-0.35f, 0.15f);
                position = flowField.ConstrainMove(flowField.GetSpawnPoint(lateral), position);
                positions[totalSpawned] = position;
                previousPositions[totalSpawned] = position;
                velocities[totalSpawned] = flowField.GetDirection(position) * speeds[totalSpawned];
                alive[totalSpawned] = true;
                gpuControls[totalSpawned] = new Vector4(speeds[totalSpawned], 1f, 0f, 0f);
                gpuSpawnStates[totalSpawned] = GpuHordeSimulation.CreateAgentState(
                    totalSpawned,
                    position,
                    velocities[totalSpawned],
                    GetVisualRadius(totalSpawned),
                    0f,
                    health[totalSpawned],
                    -flowField.GetDistanceToExit(position),
                    definition != null && definition.isFlying,
                    definition != null ? definition.mass : 1f,
                    definition);
                totalSpawned++;
                activeCount++;
            }

            var batchCount = totalSpawned - batchStart;
            if (batchCount > 0)
            {
                gpuSimulation?.SpawnBatch(gpuSpawnStates, gpuControls, batchStart, batchCount);
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

            if (gpuSimulation == null)
            {
                running = false;
                UnityEngine.Debug.LogError("The horde requires the GPU-authoritative compute backend; no legacy CPU simulation is available.");
                return;
            }

            SyncGpuShadow();
            SimulateGpuFast(deltaTime);
            return;

#if false // Removed from the runtime assembly: historical CPU prototype only.
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
                if (gpuSimulation == null && slowTimers[i] > 0f)
                {
                    slowTimers[i] -= deltaTime;
                    if (slowTimers[i] <= 0f)
                    {
                        slowMultipliers[i] = 1f;
                    }
                }

                if (gpuSimulation == null && burnTimers[i] > 0f)
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
                var crowdMultiplier = crowdSpeedFactors != null ? Mathf.Clamp(crowdSpeedFactors[i], 0.45f, 1f) : 1f;
                var desiredSpeed = speeds[i] * combatMultiplier * crowdMultiplier;
                if (gpuSimulation == null)
                {
                    desiredSpeed *= Mathf.Clamp(slowMultipliers[i], 0.05f, 1f);
                }
                if (gpuSimulation != null)
                {
                    gpuControls[i] = new Vector4(
                        desiredSpeed,
                        1f,
                        0f,
                        0f);
                    continue;
                }

                var flowDirection = flowField.GetDirection(positions[i]);
                var congestion = 0f;
                var separation = detailedTick ? CalculateSeparation(i, flowDirection, out congestion) : Vector3.zero;
                if (crowdSpeedFactors != null)
                {
                    var pressureSpeed = Mathf.Lerp(1f, 0.48f, congestion);
                    crowdSpeedFactors[i] = Mathf.MoveTowards(crowdSpeedFactors[i], pressureSpeed, deltaTime * 5f);
                }

                var desiredVelocity = flowDirection * desiredSpeed;
                var flowForce = (desiredVelocity - velocities[i]) * FlowAcceleration;
                var collisionForce = separation * CollisionAcceleration;
                var wallForce = flowField.GetWallRepulsion(positions[i], WallInfluenceDistance) * WallAcceleration;
                var acceleration = flowForce + collisionForce + wallForce;
                velocities[i] += acceleration * deltaTime;
                velocities[i] *= 1f / (1f + VelocityDamping * deltaTime);
                velocities[i] = Vector3.ClampMagnitude(velocities[i], Mathf.Max(0.25f, desiredSpeed * 1.12f));
                var knockbackVelocity = knockbackVelocities[i];
                var oldPosition = positions[i];
                var desiredPosition = oldPosition + (velocities[i] + knockbackVelocity) * deltaTime;
                positions[i] = flowField.ConstrainMove(oldPosition, desiredPosition);
                if ((positions[i] - desiredPosition).sqrMagnitude > 0.0001f)
                {
                    // Keep only the displacement the wall actually allowed. Otherwise
                    // the blocked normal velocity keeps pinning agents against corners.
                    velocities[i] = deltaTime > 0.0001f
                        ? Vector3.ClampMagnitude((positions[i] - oldPosition) / deltaTime, speeds[i])
                        : Vector3.zero;
                }

                knockbackVelocities[i] = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, deltaTime * 6f);
                pathDistances[i] = Mathf.Max(0f, path.TotalLength - flowField.GetDistanceToExit(positions[i]));
                if (sampleDetailedPerformance)
                {
                    lastMovementMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }

                if (flowField.HasReachedExit(positions[i]))
                {
                    alive[i] = false;
                    activeCount--;
                    totalResolved++;
                    continue;
                }

                if (sampleDetailedPerformance)
                {
                    lastSegmentMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }

                if (sampleDetailedPerformance)
                {
                    lastCrowdMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                    sectionStart = Stopwatch.GetTimestamp();
                }
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

                if (sampleDetailedPerformance)
                {
                    lastSampleMs += TicksToMilliseconds(Stopwatch.GetTimestamp() - sectionStart);
                }
            }

            if (gpuSimulation != null)
            {
                for (var i = 0; i < totalSpawned; i++)
                {
                    if (!alive[i])
                    {
                        gpuControls[i] = Vector4.zero;
                    }
                }

                var movementStart = Stopwatch.GetTimestamp();
                gpuSimulation.Dispatch(deltaTime, gpuControls, gpuImpulses);
                Array.Clear(gpuImpulses, 0, gpuImpulses.Length);
                lastMovementMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - movementStart);
            }

            var bucketStart = Stopwatch.GetTimestamp();
            RebuildSpatialBuckets();
            lastBucketMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - bucketStart);
#endif
        }

        private void SimulateGpuFast(float deltaTime)
        {
            ClearDetailedPerformance();
            lastBucketMs = 0f;
            lastNearCombatCount = 0;
            lastFullFidelityCount = (int)(gpuSimulation?.VisibleAgentCount ?? 0u);
            lastCheapFidelityCount = Mathf.Max(0, activeCount - lastFullFidelityCount);
            var movementStart = Stopwatch.GetTimestamp();
            gpuSimulation.SynchronizeDynamicTargets(combatTargets);
            gpuSimulation.Dispatch(deltaTime, null, null);
            lastMovementMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - movementStart);
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
            aimPoint = positions[index] + velocities[index].normalized * Mathf.Max(0f, speedLead - lookBackDistance);
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

        public bool QueueProjectile(
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
            return gpuSimulation != null && gpuSimulation.QueueProjectile(
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

        public float DamageInRadius(Vector3 center, float radius, float damage, int maxTargets, out int hitCount)
        {
            hitCount = 0;
            if (damage <= 0f || radius <= 0f || activeCount <= 0)
            {
                return 0f;
            }

            if (gpuSimulation != null && gpuSimulation.QueueAreaEffect(center, radius, damage, 0f, maxTargets, 0f, 0f, 0))
            {
                hitCount = maxTargets > 0 ? Mathf.Min(maxTargets, activeCount) : activeCount;
                return damage * hitCount;
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

            var multiplier = Mathf.Clamp01(1f - slowPercent);
            if (gpuSimulation != null && gpuSimulation.QueueAreaEffect(
                    center,
                    radius,
                    0f,
                    0f,
                    0,
                    0f,
                    0f,
                    0,
                    multiplier,
                    0.35f,
                    capacity))
            {
                return;
            }

            var radiusSq = radius * radius;
            var usedCapacity = 0f;
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
                gpuSimulation?.QueueStatus(i, multiplier, 0.35f, 0f, 0f, 0);
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

            var burnDamagePerSecond = burnDamagePerTick * burnTicksPerSecond;
            if (gpuSimulation != null && gpuSimulation.QueueAreaEffect(
                    center,
                    radius,
                    damage,
                    knockbackDistance,
                    maxTargets,
                    burnDamagePerSecond,
                    burnDuration,
                    maxBurnStacks))
            {
                hitCount = maxTargets > 0 ? Mathf.Min(maxTargets, activeCount) : activeCount;
                return damage * hitCount;
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
                    var direction = distanceSq > 0.0001f
                        ? offset.normalized
                        : Vector3.Cross(Vector3.up, flowField.GetDirection(positions[i])).normalized;
                    var falloff = 1f - Mathf.Clamp01(Mathf.Sqrt(distanceSq) / radius);
                    var impulse = direction * knockbackDistance * Mathf.Lerp(2.5f, 8f, falloff);
                    if (gpuSimulation != null)
                    {
                        gpuImpulses[i] += new Vector2(impulse.x, impulse.z);
                    }
                    else
                    {
                        knockbackVelocities[i] += impulse;
                    }
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
            if (gpuSimulation != null)
            {
                gpuSimulation.QueueStatus(index, 1f, 0f, stackDamagePerSecond, duration, stackCap);
                return;
            }

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

            if (gpuSimulation != null)
            {
                if (!gpuSimulation.TryGetCachedTarget(
                        position,
                        range,
                        canHitFlying,
                        targetingMode,
                        out var gpuIndex,
                        out var gpuPosition,
                        out var gpuVelocity))
                {
                    return -1;
                }

                if (gpuIndex >= 0 && gpuIndex < totalSpawned)
                {
                    previousPositions[gpuIndex] = positions[gpuIndex];
                    positions[gpuIndex] = gpuPosition;
                    velocities[gpuIndex] = gpuVelocity;
                }

                return gpuIndex >= 0 && gpuIndex < totalSpawned && alive[gpuIndex] && health[gpuIndex] > 0f && definitions[gpuIndex] != null
                    ? gpuIndex
                    : -1;
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
            if (index < 0 || index >= totalSpawned || !alive[index] || health[index] <= 0f || damage <= 0f)
            {
                return 0f;
            }

            var appliedDamage = Mathf.Min(health[index], damage);
            health[index] -= damage;
            if (gpuSimulation != null)
            {
                gpuSimulation.QueueDamage(index, damage);
                return appliedDamage;
            }

            if (health[index] <= 0f)
            {
                alive[index] = false;
                if (gpuControls != null)
                {
                    gpuControls[index] = Vector4.zero;
                }
                activeCount--;
                totalResolved++;
            }

            return appliedDamage;
        }

        private void SyncGpuShadow()
        {
            if (gpuSimulation == null)
            {
                return;
            }

            while (gpuSimulation.TryDequeueEvent(
                out var eventIndex,
                out var eventType,
                out var eventValue,
                out var generation,
                out var sourceIndex))
            {
                if (eventType == 1u || eventType == 2u)
                {
                    ResolveGpuAgent(eventIndex);
                    continue;
                }

                if ((eventType == 3u || eventType == 4u) &&
                    gpuSimulation.TryGetDynamicTarget(eventIndex, generation, out var target))
                {
                    var sourceDefinition = sourceIndex >= 0 && sourceIndex < definitions.Length
                        ? definitions[sourceIndex]
                        : null;
                    target.ApplyGpuCombatState(eventValue, eventType == 4u, sourceDefinition);
                }
            }
        }

        private void ResolveGpuAgent(int index)
        {
            if (index < 0 || index >= totalSpawned || !alive[index])
            {
                return;
            }

            alive[index] = false;
            health[index] = Mathf.Max(0f, health[index]);
            gpuControls[index] = Vector4.zero;
            activeCount--;
            totalResolved++;
        }

        private Vector3 CalculateSeparation(int index, Vector3 flowDirection, out float congestion)
        {
            congestion = 0f;
            if (activeCount <= 1)
            {
                return Vector3.zero;
            }

            var position = positions[index];
            // Buckets are unordered. Gather enough candidates to reach the genuinely
            // local bodies instead of stopping on the first unrelated bucket entries.
            CollectNearbyIndices(position, CollisionDiameter, nearbyIndices, 64);
            if (nearbyIndices.Count <= 1)
            {
                return Vector3.zero;
            }

            var push = Vector3.zero;
            var checks = 0;
            var radiusSq = CollisionDiameter * CollisionDiameter;
            for (var candidate = 0; candidate < nearbyIndices.Count && checks < MaxCollisionNeighbors; candidate++)
            {
                var other = nearbyIndices[candidate];
                if (other == index || !alive[other])
                {
                    continue;
                }

                var offset = position - positions[other];
                var distanceSq = offset.x * offset.x + offset.z * offset.z;
                if (distanceSq > radiusSq)
                {
                    continue;
                }

                Vector3 direction;
                float distance;
                if (distanceSq <= 0.0001f)
                {
                    // Stable per-pair fallback prevents coincident spawns forming a fixed lattice.
                    var angle = ((index * 73856093) ^ (other * 19349663)) * 0.0001f;
                    direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    distance = 0f;
                }
                else
                {
                    distance = Mathf.Sqrt(distanceSq);
                    direction = offset / distance;
                }

                var overlap = 1f - distance / CollisionDiameter;
                push += direction * overlap * 1.25f;
                if (Vector3.Dot(positions[other] - position, flowDirection) > 0f)
                {
                    congestion += overlap;
                }

                checks++;
            }

            if (checks <= 0)
            {
                return Vector3.zero;
            }

            congestion = Mathf.Clamp01(congestion / 3f);
            return Vector3.ClampMagnitude(push, 1.25f);
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

            return Mathf.Clamp(visualScales[index] * 0.78f, VisualRadius * 0.82f, VisualRadius * 1.45f);
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
            public readonly uint OverflowCells;
            public readonly uint DroppedAgents;
            public readonly uint MaxCellOccupancy;
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
                uint overflowCells,
                uint droppedAgents,
                uint maxCellOccupancy,
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
                OverflowCells = overflowCells;
                DroppedAgents = droppedAgents;
                MaxCellOccupancy = maxCellOccupancy;
                ShaderName = shaderName;
            }
        }
    }
}
