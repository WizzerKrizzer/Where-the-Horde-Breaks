using System;
using System.Collections.Generic;
using System.Diagnostics;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    [DefaultExecutionOrder(100)]
    public sealed class HordeEnemyManager : MonoBehaviour
    {
        private const int InstanceBatchSize = 1023;
        private const float RoadHalfWidth = 2.65f;
        private const float VisualRadius = 0.34f;
        private const float MaximumVisualRadius = VisualRadius * 1.45f;
        private const float SpatialCellSize = 1.2f;
        private const float CombatTargetCellSize = 4.5f;
        // The normalized enemy meshes use state.scale as their visible radius.
        // Keep contact only slightly outside the silhouette to avoid visible gaps.
        private const float CollisionDiameter = 0.58f;
        private const float FlowCellSize = 0.62f;
        private const float FlowAcceleration = 8.5f;
        private const float CollisionAcceleration = 18f;
        private const float WallAcceleration = 13f;
        private const float WallInfluenceDistance = 0.9f;
        private const float VelocityDamping = 1.15f;
        private const int OffscreenDetailStride = 8;
        private const int DetailedPerfSampleStride = 15;
        private const int MaxCollisionNeighbors = 16;
        private const int MinimumSpawnsPerWindow = 14;
        private const int MaximumSpawnsPerWindow = 16;
        // A family of enemies should share an authored base speed, but no crowd
        // should begin every stride and reach every corner at the same instant.
        // Large variation makes every rear runner constantly catch, brake and
        // re-catch the runner ahead. Animation phase already breaks the visual
        // grid, so ordinary horde members need nearly one shared convoy speed.
        private const float MinimumPersonalSpeedFactor = 0.97f;
        private const float MaximumPersonalSpeedFactor = 1.03f;

        [SerializeField, Tooltip("Diagnostic only: draw the cyan navigation boundary when a wave starts.")]
        private bool showNavigationBoundary;

        [NonSerialized] private readonly List<EnemyDefinition> spawnSequence = new();
        [NonSerialized] private readonly Matrix4x4[] matrixBatch = new Matrix4x4[InstanceBatchSize];
        [NonSerialized] private readonly Dictionary<Vector2Int, List<int>> spatialBuckets = new();
        [NonSerialized] private readonly Dictionary<Vector2Int, List<ICombatTarget>> combatTargetBuckets = new();
        [NonSerialized] private readonly List<int> nearbyIndices = new(192);
        [NonSerialized] private readonly List<ICombatTarget> nearbyCombatTargets = new(32);
        [NonSerialized] private readonly Dictionary<ICombatTarget, float> frameTargetBlockedMass = new();
        [NonSerialized] private readonly List<Vector3> spawnBatchPositions = new(64);
        [NonSerialized] private Vector3[] positions;
        [NonSerialized] private Vector3[] previousPositions;
        [NonSerialized] private float[] pathDistances;
        [NonSerialized] private Vector3[] velocities;
        [NonSerialized] private float[] crowdSpeedFactors;
        [NonSerialized] private float[] visualScales;
        [NonSerialized] private float[] visualPulsePhases;
        [NonSerialized] private float[] visualPulseSpeeds;
        [NonSerialized] private float[] speeds;
        [NonSerialized] private float[] slowMultipliers;
        [NonSerialized] private float[] slowTimers;
        [NonSerialized] private float[] attackTimers;
        [NonSerialized] private float[] spawnTimes;
        [NonSerialized] private float[] spawnLateralOffsets;
        [NonSerialized] private float[] health;
        [NonSerialized] private float[] reservedDamage;
        [NonSerialized] private float[] burnDamagePerSecond;
        [NonSerialized] private float[] burnTimers;
        [NonSerialized] private Vector3[] knockbackVelocities;
        [NonSerialized] private EnemyDefinition[] definitions;
        [NonSerialized] private bool[] alive;
        [NonSerialized] private Vector4[] gpuControls;
        [NonSerialized] private Vector2[] gpuImpulses;
        [NonSerialized] private GpuHordeSimulation.AgentState[] gpuSpawnStates;
        [NonSerialized] private GpuHordeSimulation gpuSimulation;
        [NonSerialized] private IReadOnlyList<ICombatTarget> combatTargets;
        [NonSerialized] private WaveDefinition wave;
        [NonSerialized] private HordePhysicsProfile activePhysicsProfile;
        [NonSerialized] private PathRoute path;
        [NonSerialized] private HordeFlowField flowField;
        [NonSerialized] private Mesh mesh;
        [NonSerialized] private Material material;
        [NonSerialized] private Material slowedMaterial;
        [NonSerialized] private MaterialPropertyBlock properties;
        [NonSerialized] private GameObject boundaryDebugObject;
        [NonSerialized] private Mesh boundaryDebugMesh;
        private float elapsed;
        private float spawnElapsed;
        [NonSerialized] private int spawnLaneCursor;
        [NonSerialized] private Vector3[] spawnLanePositions;
        [NonSerialized] private float[] spawnLaneBlockedUntil;
        [NonSerialized] private int[] spawnLaneLastSpawnFrame;
        private uint openFieldSpawnSequence;
        [NonSerialized] private int lastSpawnOccupancyFrame;
        [NonSerialized] private int spawnWindowRemaining;
        [NonSerialized] private float nextSpawnWindowTime;
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
        private float activeEnemyScaleMultiplier = 1f;
        private float activeEnemyMassMultiplier = 1f;
        private float activeMaximumVisualRadius = MaximumVisualRadius;
        private float activeNavigationWallInset = MaximumVisualRadius;
        private float activeCollisionDiameter = CollisionDiameter;
        private float activeSpriteContactDiameterMultiplier = HordeRaiderSpriteAssets.MinimumContactDiameterMultiplier;
        private bool usesOpenFieldNavigation;
        private float activeFixedSimulationStep = 1f / 60f;
        private float gpuSimulationAccumulator;
        private int gpuStepsThisFrame = 1;
        internal int SimulationTicksLastFrame { get; private set; }

        public int TotalSpawned => totalSpawned;
        public int ActiveCount => activeCount;
        public int TotalResolved => totalResolved;
        public bool IsRunning => running;
        public bool IsComplete => running && totalSpawned >= spawnSequence.Count && activeCount == 0;
        public event Action<EnemyDefinition> EnemySpawned;
        public event Action<EnemyDefinition> EnemyKilled;
        public event Action<EnemyDefinition> EnemyEscaped;
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
            gpuSimulation?.OverlapPairCount ?? 0u,
            gpuSimulation?.MaximumOverlapFraction ?? 0f,
            gpuSimulation?.OrdinaryBackwardStepCount ?? 0u,
            gpuSimulation?.MaximumOrdinaryBacktrack ?? 0f,
            gpuSimulation?.NormalizedNeighborSpacing ?? 0f,
            gpuSimulation?.ActiveContactPairCount ?? 0u,
            gpuSimulation != null
                ? "GPU Compute / HordeIndirect"
                : material != null && material.shader != null ? material.shader.name : "none");

        public void SetCombatTargets(IReadOnlyList<ICombatTarget> targets)
        {
            combatTargets = targets;
        }

        public void BeginWave(WaveDefinition waveDefinition, PathRoute route)
        {
            if (!running && gpuSimulation != null && wave == waveDefinition && path == route &&
                totalSpawned == 0 && activeCount == 0)
            {
                running = true;
                return;
            }

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
            spawnLateralOffsets = new float[count];
            health = new float[count];
            reservedDamage = new float[count];
            burnDamagePerSecond = new float[count];
            burnTimers = new float[count];
            knockbackVelocities = new Vector3[count];
            definitions = new EnemyDefinition[count];
            alive = new bool[count];
            gpuControls = new Vector4[count];
            gpuImpulses = new Vector2[count];
            gpuSpawnStates = new GpuHordeSimulation.AgentState[count];
            activeEnemyScaleMultiplier = Mathf.Clamp(wave.enemyScaleMultiplier, 0.1f, 2f);
            activeEnemyMassMultiplier = Mathf.Clamp(wave.enemyMassMultiplier, 0.5f, 3f);
            activeMaximumVisualRadius = EstimateMaximumVisualRadius(spawnSequence) * 1.04f;
            activeCollisionDiameter = CollisionDiameter * activeEnemyScaleMultiplier;
            activePhysicsProfile = wave.hordePhysicsProfile;
            activeNavigationWallInset = GetNavigationWallInset();
            usesOpenFieldNavigation = wave.hordeNavigationMode == HordeNavigationMode.OpenField &&
                wave.openFieldSize.x > 0.1f && wave.openFieldSize.y > 0.1f;
            activeFixedSimulationStep = 1f / Mathf.Clamp(
                activePhysicsProfile != null ? activePhysicsProfile.fixedSimulationHz : 60,
                30,
                120);
            activeSpriteContactDiameterMultiplier = HordeRaiderSpriteAssets.MinimumContactDiameterMultiplier *
                Mathf.Clamp(activePhysicsProfile != null ? activePhysicsProfile.contactDiameterMultiplier : 1f, 0.55f, 1.5f);
            activeRoadHalfWidth = usesOpenFieldNavigation
                ? Mathf.Max(activeNavigationWallInset, wave.openFieldGateHalfWidth)
                : wave.roadHalfWidth > VisualRadius * activeEnemyScaleMultiplier
                    ? wave.roadHalfWidth
                    : RoadHalfWidth;
            if (usesOpenFieldNavigation)
            {
                flowField = HordeFlowField.CreateOpenField(
                    wave.openFieldCenter,
                    wave.openFieldSize,
                    path.StartPoint,
                    path.EndPoint,
                    activeRoadHalfWidth,
                    FlowCellSize);
            }
            else
            {
                var contractedPrimaryWidths = ContractWaypointWidths(path.WaypointHalfWidths);
                var contractedSecondaryWidths = ContractWaypointWidths(path.SecondaryWaypointHalfWidths);
                var contractedTertiaryWidths = ContractWaypointWidths(path.TertiaryWaypointHalfWidths);
                flowField = new HordeFlowField(
                    path.Waypoints,
                    path.SecondaryWaypoints,
                    activeRoadHalfWidth - activeNavigationWallInset,
                    ResolveFlowCellSize(contractedPrimaryWidths, contractedSecondaryWidths, contractedTertiaryWidths),
                    contractedPrimaryWidths,
                    contractedSecondaryWidths,
                    path.TertiaryWaypoints,
                    contractedTertiaryWidths,
                    path.UseSquareCorners);
            }
            CreateBoundaryDebugVisual();
            if (!wave.enforceSpawnClearance)
            {
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
                    var lateralPhase = UnityEngine.Random.value;
                    for (var i = 0; i < burst; i++)
                    {
                        spawnTimes[burstIndex + i] = cursor + UnityEngine.Random.Range(0f, packedSpawnSpan);
                        // Cover the complete entrance cross-section in every burst.
                        // A random phase and sub-stratum jitter avoid visible rows while
                        // preventing pure random sampling from leaving large empty bands.
                        var lateral01 = Mathf.Repeat(
                            lateralPhase + (i + 0.5f + UnityEngine.Random.Range(-0.22f, 0.22f)) / burst,
                            1f);
                        spawnLateralOffsets[burstIndex + i] = Mathf.Lerp(
                            -activeRoadHalfWidth + activeNavigationWallInset,
                            activeRoadHalfWidth - activeNavigationWallInset,
                            lateral01);
                    }

                    Array.Sort(spawnTimes, burstIndex, burst);
                    burstIndex += burst;
                    cursor += packedSpawnStep;
                }
            }

            mesh = EnemyManager.GetLowEnemyMesh();
            material = BootstrapMaterials.Get(new Color(0.08f, 0.82f, 0.29f, 1f));
            material.enableInstancing = true;
            slowedMaterial = BootstrapMaterials.Get(new Color(0.16f, 0.50f, 1f, 1f));
            slowedMaterial.enableInstancing = true;
            properties ??= new MaterialPropertyBlock();
            if (!GpuHordeSimulation.TryCreate(count, flowField, mesh, out gpuSimulation))
            {
                running = false;
                return;
            }
            gpuSimulation.SetVisualScaleMultiplier(1f);
            gpuSimulation.SetCollisionDiameter(activeCollisionDiameter);
            gpuSimulation.SetMovementDynamics(wave.enemyInertiaMultiplier, wave.collisionPressureMultiplier);
            gpuSimulation.SetHordePhysicsProfile(activePhysicsProfile);
            gpuSimulation.SetStationaryArena(
                wave.stationaryArena,
                wave.randomArenaMovement,
                wave.centerSeekingArena,
                path.StartPoint);
            gpuSimulation.SetSpeedColorMode(wave.colorEnemiesBySpeed);
            spawnLanePositions = wave.enforceSpawnClearance
                ? CreateEntranceSpawnProbes()
                : null;
            spawnLaneBlockedUntil = wave.enforceSpawnClearance
                ? new float[spawnLanePositions.Length]
                : null;
            spawnLaneLastSpawnFrame = wave.enforceSpawnClearance
                ? new int[spawnLaneBlockedUntil.Length]
                : null;
            elapsed = 0f;
            spawnElapsed = 0f;
            spawnLaneCursor = 0;
            openFieldSpawnSequence = 0u;
            lastSpawnOccupancyFrame = 0;
            spawnWindowRemaining = 0;
            nextSpawnWindowTime = 0f;
            spawnBatchPositions.Clear();
            totalSpawned = 0;
            activeCount = 0;
            totalResolved = 0;
            gpuSimulationAccumulator = 0f;
            running = true;
        }

        public void PrepareWave(WaveDefinition waveDefinition, PathRoute route)
        {
            if (running || (gpuSimulation != null && wave == waveDefinition && path == route))
            {
                return;
            }

            BeginWave(waveDefinition, route);
            running = false;
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

        private void OnDisable()
        {
            Clear();
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
            spawnLateralOffsets = null;
            spawnLanePositions = null;
            spawnLaneBlockedUntil = null;
            spawnLaneLastSpawnFrame = null;
            openFieldSpawnSequence = 0u;
            lastSpawnOccupancyFrame = 0;
            spawnWindowRemaining = 0;
            nextSpawnWindowTime = 0f;
            spawnBatchPositions.Clear();
            health = null;
            reservedDamage = null;
            burnDamagePerSecond = null;
            burnTimers = null;
            knockbackVelocities = null;
            definitions = null;
            alive = null;
            gpuControls = null;
            gpuImpulses = null;
            gpuSpawnStates = null;
            flowField = null;
            if (boundaryDebugObject != null)
            {
                Destroy(boundaryDebugObject);
                boundaryDebugObject = null;
            }
            if (boundaryDebugMesh != null)
            {
                Destroy(boundaryDebugMesh);
                boundaryDebugMesh = null;
            }
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
            activeEnemyScaleMultiplier = 1f;
            activeEnemyMassMultiplier = 1f;
            activeMaximumVisualRadius = MaximumVisualRadius;
            activeNavigationWallInset = MaximumVisualRadius;
            activeCollisionDiameter = CollisionDiameter;
            activeSpriteContactDiameterMultiplier = HordeRaiderSpriteAssets.MinimumContactDiameterMultiplier;
            activePhysicsProfile = null;
            usesOpenFieldNavigation = false;
            activeFixedSimulationStep = 1f / 60f;
            gpuSimulationAccumulator = 0f;
            spawnElapsed = 0f;
        }

        private void Update()
        {
            SimulationTicksLastFrame = 0;
            if (!running || wave == null || path == null)
            {
                return;
            }

            var spawnTicks = 0L;
            var simulationTicks = 0L;
            spawnElapsed += Mathf.Max(0f, Time.unscaledDeltaTime) *
                Mathf.Max(0f, Time.timeScale);
            var sectionStart = Stopwatch.GetTimestamp();
            SpawnDueEnemies();
            spawnTicks += Stopwatch.GetTimestamp() - sectionStart;

            var fixedGpuStep = activeFixedSimulationStep;
            const int maximumStepsPerFrame = 4;
            // Never enlarge the collision horizon in response to a slow frame.
            // Bound catch-up work; excess wall time is dropped, rather than
            // creating deeper contacts and an ever more expensive next frame.
            gpuSimulationAccumulator = Mathf.Min(
                gpuSimulationAccumulator + Mathf.Max(0f, Time.deltaTime),
                fixedGpuStep * maximumStepsPerFrame);
            var pendingFixedSteps = Mathf.FloorToInt(
                (gpuSimulationAccumulator + 0.000001f) / fixedGpuStep);
            if (pendingFixedSteps <= 0)
            {
                lastSpawnMs = TicksToMilliseconds(spawnTicks);
                return;
            }

            var stepCount = Mathf.Min(maximumStepsPerFrame, pendingFixedSteps);
            var simulationStep = fixedGpuStep;
            gpuStepsThisFrame = Mathf.Max(1, stepCount);
            SimulationTicksLastFrame = stepCount;
            for (var step = 0; step < stepCount; step++)
            {
                elapsed += simulationStep;
                sectionStart = Stopwatch.GetTimestamp();
                Simulate(simulationStep, step == stepCount - 1);
                simulationTicks += Stopwatch.GetTimestamp() - sectionStart;
                gpuSimulationAccumulator -= simulationStep;
            }
            gpuSimulationAccumulator = Mathf.Max(0f, gpuSimulationAccumulator);

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
                gpuSimulation.SetPresentationInterpolation(gpuSimulationAccumulator / activeFixedSimulationStep);
                // This component runs after TopDownCameraController's LateUpdate.
                // Rebuild visibility from the final camera pose, not the pose from
                // the preceding simulation tick.
                gpuSimulation.RefreshVisibilityForRendering();
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
            if (totalSpawned >= spawnSequence.Count) return;
            if (wave.enforceSpawnClearance && spawnWindowRemaining <= 0)
            {
                if (spawnElapsed + 0.0001f < nextSpawnWindowTime)
                {
                    return;
                }

                var windowDuration = Mathf.Max(0.01f, wave.spawnInterval);
                var dueWindowCount = Mathf.Clamp(
                    Mathf.FloorToInt((spawnElapsed - nextSpawnWindowTime) / windowDuration) + 1,
                    1,
                    32);
                for (var window = 0; window < dueWindowCount; window++)
                {
                    var minimumBurst = wave.randomSpawnBurstMin > 0
                        ? wave.randomSpawnBurstMin
                        : MinimumSpawnsPerWindow;
                    var maximumBurst = wave.randomSpawnBurstMax >= minimumBurst
                        ? wave.randomSpawnBurstMax
                        : Mathf.Max(minimumBurst, MaximumSpawnsPerWindow);
                    spawnWindowRemaining += UnityEngine.Random.Range(
                        minimumBurst,
                        maximumBurst + 1);
                }
                nextSpawnWindowTime += windowDuration * dueWindowCount;
            }

            var batchStart = totalSpawned;
            var entranceWasEmpty = activeCount == 0;
            // The first batch is checked against itself below. Every later batch
            // checks the live GPU crowd even during fast-forward; skipping that
            // query at Time.timeScale > 1 created exact duplicate spawn positions.
            var bypassGpuSpawnClearance = entranceWasEmpty;
            var occupancyFrameAtBatchStart = lastSpawnOccupancyFrame;
            var spawnWindowTarget = wave.enforceSpawnClearance
                ? spawnWindowRemaining
                : int.MaxValue;
            var spawnDiameter = GetSpawnClearanceDiameter();
            spawnBatchPositions.Clear();
            while (totalSpawned < spawnSequence.Count &&
                   spawnElapsed >= spawnTimes[totalSpawned] &&
                   spawnBatchPositions.Count < spawnWindowTarget)
            {
                var definition = spawnSequence[totalSpawned];
                var spawnSpeed = (definition != null ? definition.speed : 4.8f) *
                    Mathf.Max(0.1f, wave.enemySpeedMultiplier);
                if (wave.randomEnemySpeedMin > 0f &&
                    wave.randomEnemySpeedMax >= wave.randomEnemySpeedMin)
                {
                    spawnSpeed = UnityEngine.Random.Range(
                        wave.randomEnemySpeedMin,
                        wave.randomEnemySpeedMax);
                }
                var hasAuthoredSpeedRange = wave.randomEnemySpeedMin > 0f &&
                    wave.randomEnemySpeedMax >= wave.randomEnemySpeedMin;
                var personalSpeedFactor = activePhysicsProfile != null && activePhysicsProfile.useReferenceCrowdRules
                    ? 1f
                    : hasAuthoredSpeedRange
                    ? 1f
                    : GetPersonalSpeedFactor(totalSpawned);
                var individualSpeed = spawnSpeed * personalSpeedFactor;
                var spawnLane = -1;
                Vector3 position;
                if (wave.stationaryArena && wave.centerSeekingArena && wave.enforceSpawnClearance)
                {
                    if (!TryFindFreeSpawnPosition(
                            spawnDiameter,
                            bypassGpuSpawnClearance,
                            occupancyFrameAtBatchStart,
                            out spawnLane,
                            out position))
                    {
                        break;
                    }
                }
                else if (wave.stationaryArena && wave.centerSeekingArena)
                {
                    var spawnAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    var spawnRadius = Mathf.Max(
                        1f,
                        activeRoadHalfWidth - activeMaximumVisualRadius - UnityEngine.Random.Range(0.4f, 1.2f));
                    position = path.StartPoint + new Vector3(
                        Mathf.Cos(spawnAngle) * spawnRadius,
                        0f,
                        Mathf.Sin(spawnAngle) * spawnRadius);
                }
                else if (wave.stationaryArena)
                {
                    position = path.StartPoint;
                }
                else if (wave.enforceSpawnClearance)
                {
                    if (!TryFindFreeSpawnPosition(
                            spawnDiameter,
                            bypassGpuSpawnClearance,
                            occupancyFrameAtBatchStart,
                            out spawnLane,
                            out position))
                    {
                        break;
                    }
                }
                else
                {
                    var lateral = spawnLateralOffsets != null && totalSpawned < spawnLateralOffsets.Length
                        ? spawnLateralOffsets[totalSpawned]
                        : UnityEngine.Random.Range(
                            -activeRoadHalfWidth + activeNavigationWallInset,
                            activeRoadHalfWidth - activeNavigationWallInset);
                    var entrance = flowField.GetSpawnPoint(lateral);
                    position = entrance + flowField.GetDirection(entrance) * UnityEngine.Random.Range(-0.35f, 0.15f);
                    position = flowField.ConstrainMove(entrance, position);
                }

                definitions[totalSpawned] = definition;
                health[totalSpawned] = Mathf.Max(1f, definition != null ? definition.maxHealth : 1f);
                crowdSpeedFactors[totalSpawned] = personalSpeedFactor;
                pathDistances[totalSpawned] = 0f;
                speeds[totalSpawned] = individualSpeed;
                // Keep members of one enemy family visually related. Broad scale
                // variation made ordinary runners read as separate enemy classes.
                visualScales[totalSpawned] = (definition != null ? definition.visualScale : 0.45f) *
                    (activePhysicsProfile != null && activePhysicsProfile.useReferenceCrowdRules
                        ? 1f
                        : UnityEngine.Random.Range(0.96f, 1.04f));
                visualPulsePhases[totalSpawned] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                visualPulseSpeeds[totalSpawned] = UnityEngine.Random.Range(1.7f, 3.8f);
                slowMultipliers[totalSpawned] = 1f;
                slowTimers[totalSpawned] = 0f;
                attackTimers[totalSpawned] = 0f;
                knockbackVelocities[totalSpawned] = Vector3.zero;
                positions[totalSpawned] = position;
                previousPositions[totalSpawned] = position;
                var arenaMovementAngle = wave.randomArenaMovement
                    ? UnityEngine.Random.Range(0f, Mathf.PI * 2f)
                    : 0f;
                var arenaMovementDirection = new Vector3(
                    Mathf.Cos(arenaMovementAngle),
                    0f,
                    Mathf.Sin(arenaMovementAngle));
                var centerDirection = (path.StartPoint - position).normalized;
                velocities[totalSpawned] = wave.centerSeekingArena
                    ? centerDirection * speeds[totalSpawned]
                    : wave.randomArenaMovement
                    ? arenaMovementDirection * speeds[totalSpawned]
                    : wave.stationaryArena
                        ? Vector3.zero
                        : flowField.GetDirection(position) * speeds[totalSpawned];
                alive[totalSpawned] = true;
                var speedColor = wave.colorEnemiesBySpeed ? GetSpeedColorIndex(individualSpeed) : 0f;
                gpuControls[totalSpawned] = new Vector4(
                    wave.stationaryArena && !wave.randomArenaMovement && !wave.centerSeekingArena
                        ? 0f
                        : speeds[totalSpawned],
                    1f,
                    speedColor,
                    arenaMovementAngle);
                var spawnMass = (definition != null ? definition.mass : 1f) * activeEnemyMassMultiplier;
                if (wave.randomEnemySpeedMax > wave.randomEnemySpeedMin)
                {
                    var speedWeight = Mathf.InverseLerp(
                        wave.randomEnemySpeedMin,
                        wave.randomEnemySpeedMax,
                        individualSpeed);
                    spawnMass *= Mathf.Lerp(1f, 1.65f, speedWeight);
                }
                gpuSpawnStates[totalSpawned] = GpuHordeSimulation.CreateAgentState(
                    totalSpawned,
                    position,
                    velocities[totalSpawned],
                    GetVisualRadius(totalSpawned),
                    speedColor,
                    health[totalSpawned],
                    -flowField.GetDistanceToExit(position),
                    definition != null && definition.isFlying,
                    spawnMass,
                    definition);
                EnemySpawned?.Invoke(definition);
                if (wave.enforceSpawnClearance)
                {
                    spawnBatchPositions.Add(position);
                    spawnLanePositions[spawnLane] = CreateRandomEntranceSpawnPosition();
                    spawnLaneBlockedUntil[spawnLane] = spawnElapsed;
                    spawnLaneLastSpawnFrame[spawnLane] = Time.frameCount;
                    spawnLaneCursor = (spawnLane + UnityEngine.Random.Range(
                        1,
                        spawnLaneBlockedUntil.Length + 1)) % spawnLaneBlockedUntil.Length;
                }
                totalSpawned++;
                activeCount++;
            }

            var batchCount = totalSpawned - batchStart;
            if (batchCount > 0)
            {
                gpuSimulation?.SpawnBatch(gpuSpawnStates, gpuControls, batchStart, batchCount);
                if (wave.enforceSpawnClearance)
                {
                    lastSpawnOccupancyFrame = Time.frameCount;
                    spawnWindowRemaining = Mathf.Max(0, spawnWindowRemaining - batchCount);
                }
            }
        }

        private int GetSpawnLaneCount()
        {
            var spawnDiameter = GetSpawnClearanceDiameter();
            var usableEntranceWidth = Mathf.Max(spawnDiameter,
                (activeRoadHalfWidth - activeNavigationWallInset) * 2f);
            return Mathf.Max(1, Mathf.FloorToInt(usableEntranceWidth / spawnDiameter) + 1);
        }

        internal static float GetPersonalSpeedFactor(int spawnIndex)
        {
            // A tiny integer hash gives each body a repeatable locomotion identity
            // without perturbing Unity's global random sequence (which also drives
            // spawn lanes, visuals, and reproducible balance runs).
            unchecked
            {
                var value = (uint)(spawnIndex + 1);
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                var normalized = (value & 0x00ffffffu) / 16777215f;
                return Mathf.Lerp(MinimumPersonalSpeedFactor, MaximumPersonalSpeedFactor, normalized);
            }
        }

        private Vector3[] CreateEntranceSpawnProbes()
        {
            var probeCount = Mathf.Clamp(GetSpawnLaneCount() * 8, 24, 128);
            var probes = new Vector3[probeCount];
            for (var i = 0; i < probes.Length; i++)
            {
                probes[i] = CreateRandomEntranceSpawnPosition();
            }

            return probes;
        }

        private Vector3 CreateRandomEntranceSpawnPosition()
        {
            if (wave != null && wave.stationaryArena && wave.centerSeekingArena)
            {
                var spawnAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var spawnRadius = Mathf.Max(
                    1f,
                    activeRoadHalfWidth - activeMaximumVisualRadius - UnityEngine.Random.Range(0.4f, 1.2f));
                return path.StartPoint + new Vector3(
                    Mathf.Cos(spawnAngle) * spawnRadius,
                    0f,
                    Mathf.Sin(spawnAngle) * spawnRadius);
            }

            var usableHalfWidth = Mathf.Max(0f, activeRoadHalfWidth - activeNavigationWallInset);
            if (usesOpenFieldNavigation)
            {
                // A low-discrepancy stream distributes probes across the gate
                // without a marching grid. GPU clearance is still authoritative,
                // so this is a blue-noise-like proposal pattern rather than a
                // source of physical overlap.
                var sample = openFieldSpawnSequence++;
                var lateral01 = Mathf.Repeat((sample + 0.5f) * 0.61803398875f, 1f);
                var depth01 = Hash01(sample, 0x68bc21ebu);
                var sideJitter = Hash01(sample, 0x02e5be93u) - 0.5f;
                var entrance = flowField.GetSpawnPoint(Mathf.Lerp(-usableHalfWidth, usableHalfWidth, lateral01));
                var direction = flowField.GetDirection(entrance).normalized;
                var side = Vector3.Cross(Vector3.up, direction);
                var candidate = entrance - direction * Mathf.Lerp(
                    GetSpawnClearanceDiameter() * 0.16f,
                    GetSpawnClearanceDiameter() * 2.4f,
                    depth01) + side * sideJitter * GetSpawnClearanceDiameter() * 0.16f;
                return flowField.IsWalkable(candidate)
                    ? candidate
                    : flowField.ConstrainMove(entrance, candidate);
            }

            var entranceDepth = usesOpenFieldNavigation
                ? GetSpawnClearanceDiameter() * 2.4f
                : Mathf.Max(GetSpawnClearanceDiameter() * 1.6f, activeRoadHalfWidth * 0.75f);
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var entrance = flowField.GetSpawnPoint(
                    UnityEngine.Random.Range(-usableHalfWidth, usableHalfWidth));
                var candidate = entrance - flowField.GetDirection(entrance).normalized *
                    UnityEngine.Random.Range(0f, entranceDepth);
                if (flowField.IsWalkable(candidate))
                {
                    return candidate;
                }
            }

            return flowField.GetSpawnPoint(UnityEngine.Random.Range(-usableHalfWidth, usableHalfWidth));
        }

        private static float Hash01(uint value, uint salt)
        {
            value ^= salt;
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return (value & 0x00ffffffu) / 16777215f;
        }

        private float GetSpawnClearanceDiameter()
        {
            // The GPU solver uses this minimum pair diameter for the compact
            // horde sprite. The former generic visual radius included transparent
            // card padding, so every spawn was deliberately born too far apart.
            // Keep a tiny physical clearance at birth. The render card is scaled
            // past this envelope, so the visible silhouettes still touch while
            // the contact solver avoids a large corrective push on the first tick.
            var baseDiameter = GetSpriteContactDiameter() * 1.04f;
            return baseDiameter * Mathf.Clamp(wave != null ? wave.spawnPackingMultiplier : 1f, 1f, 2f);
        }

        private float GetSpriteContactDiameter() => activeCollisionDiameter * activeSpriteContactDiameterMultiplier;

        private float GetNavigationWallInset()
        {
            // Collision uses a compact disk. The top-down visual is a rotated
            // rectangle, so its corner extends farther toward an angled wall.
            // Reserve that full footprint plus a small readable air gap.
            var packing = Mathf.Clamp(activePhysicsProfile != null
                ? activePhysicsProfile.spritePackingScale
                : 1.18f, 0.9f, 1.35f);
            var cardCornerRadius = activeMaximumVisualRadius *
                HordeRaiderSpriteAssets.GroundCardCornerRadius * packing;
            return Mathf.Max(activeMaximumVisualRadius, cardCornerRadius + 0.045f);
        }

        private static float GetSpeedColorIndex(float speed)
        {
            if (speed < 5.6f)
            {
                return 0f;
            }

            if (speed < 6.1f)
            {
                return 1f;
            }

            return speed < 6.6f ? 2f : 3f;
        }

        private bool TryFindFreeSpawnPosition(
            float spawnDiameter,
            bool skipGpuClearance,
            int newerThanFrame,
            out int laneIndex,
            out Vector3 position)
        {
            laneIndex = -1;
            position = Vector3.zero;
            if (spawnLanePositions == null || spawnLaneBlockedUntil == null ||
                spawnLanePositions.Length == 0 || spawnLaneBlockedUntil.Length == 0)
            {
                return false;
            }

            var laneCount = spawnLaneBlockedUntil.Length;
            for (var offset = 0; offset < laneCount; offset++)
            {
                var candidateLane = (spawnLaneCursor + offset) % laneCount;
                if (spawnElapsed + 0.0001f < spawnLaneBlockedUntil[candidateLane])
                {
                    continue;
                }

                var candidate = spawnLanePositions[candidateLane];
                var overlapsBatch = false;
                for (var i = 0; i < spawnBatchPositions.Count; i++)
                {
                    if ((spawnBatchPositions[i] - candidate).sqrMagnitude < spawnDiameter * spawnDiameter)
                    {
                        overlapsBatch = true;
                        break;
                    }
                }

                if (overlapsBatch)
                {
                    continue;
                }

                var isClear = true;
                if (!skipGpuClearance && activeCount > 0 &&
                    !gpuSimulation.TryGetSpawnClearance(
                        candidate,
                        spawnDiameter * 1.02f,
                        Mathf.Max(newerThanFrame, spawnLaneLastSpawnFrame[candidateLane]),
                        out isClear,
                        GetSpriteContactDiameter()))
                {
                    continue;
                }

                if (!skipGpuClearance && activeCount > 0 && !isClear)
                {
                    spawnLanePositions[candidateLane] = CreateRandomEntranceSpawnPosition();
                    spawnLaneLastSpawnFrame[candidateLane] = Time.frameCount;
                    continue;
                }

                if (laneIndex < 0)
                {
                    laneIndex = candidateLane;
                    position = candidate;
                }
            }

            return laneIndex >= 0;
        }

        private void Simulate(float deltaTime, bool updatePresentation = true)
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
            SimulateGpuFast(deltaTime, updatePresentation);
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
                var desiredSpeed = wave.stationaryArena && !wave.randomArenaMovement && !wave.centerSeekingArena
                    ? 0f
                    : speeds[i] * combatMultiplier * crowdMultiplier;
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
                        gpuControls[i].w);
                    continue;
                }

                var flowDirection = wave.centerSeekingArena
                    ? (path.StartPoint - positions[i]).normalized
                    : flowField.GetDirection(positions[i]);
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

                if (!wave.stationaryArena && flowField.HasReachedExit(positions[i]))
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

        private void SimulateGpuFast(float deltaTime, bool updatePresentation)
        {
            ClearDetailedPerformance();
            lastBucketMs = 0f;
            lastNearCombatCount = 0;
            lastFullFidelityCount = (int)(gpuSimulation?.VisibleAgentCount ?? 0u);
            lastCheapFidelityCount = Mathf.Max(0, activeCount - lastFullFidelityCount);
            var movementStart = Stopwatch.GetTimestamp();
            gpuSimulation.SynchronizeDynamicTargets(combatTargets);
            var openingSpeedMultiplier = wave != null && elapsed < wave.openingSpeedDuration
                ? Mathf.Max(1f, wave.openingSpeedMultiplier)
                : 1f;
            gpuSimulation.SetMovementSpeedMultiplier(openingSpeedMultiplier);
            // Time acceleration executes up to four physics ticks in one rendered
            // frame. Share the contact budget between them so catch-up cannot create
            // hundreds of grid rebuilds and feed an FPS collapse.
            var contactIterationScale = 1f / Mathf.Sqrt(Mathf.Max(1, gpuStepsThisFrame));
            gpuSimulation.Dispatch(
                deltaTime,
                null,
                null,
                requestReadback: updatePresentation,
                contactIterationScale: contactIterationScale,
                updatePresentation: updatePresentation,
                updateVisibility: false);
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

        public bool TryGetTargetSnapshot(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode, out int targetIndex, out Vector3 targetPosition, out Vector3 targetVelocity, int excludedIndex = -1)
        {
            targetPosition = Vector3.zero;
            targetVelocity = Vector3.zero;
            targetIndex = FindBestTarget(position, range, canHitFlying, targetingMode, excludedIndex);
            if (targetIndex < 0)
            {
                return false;
            }

            targetPosition = positions[targetIndex];
            targetVelocity = velocities[targetIndex];
            return true;
        }

        public bool TryGetNearestTargetExcept(Vector3 position, float range, bool canHitFlying, int excludedIndex, out int targetIndex, out Vector3 targetPosition, out Vector3 targetVelocity)
        {
            targetPosition = Vector3.zero;
            targetVelocity = Vector3.zero;
            targetIndex = FindBestTarget(position, range, canHitFlying, TowerTargetingMode.Closest, excludedIndex);
            if (targetIndex < 0)
            {
                return false;
            }

            targetPosition = positions[targetIndex];
            targetVelocity = velocities[targetIndex];
            return true;
        }

        public bool TryGetSecondaryTargetSnapshot(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode, out int targetIndex, out Vector3 targetPosition, out Vector3 targetVelocity)
        {
            targetIndex = -1;
            targetPosition = Vector3.zero;
            targetVelocity = Vector3.zero;
            if (gpuSimulation == null || !gpuSimulation.TryGetCachedSecondaryTarget(
                    position,
                    range,
                    canHitFlying,
                    targetingMode,
                    out targetIndex,
                    out targetPosition,
                    out targetVelocity))
            {
                return false;
            }

            if (!CanTarget(targetIndex))
            {
                gpuSimulation.InvalidateTargetCache();
                return false;
            }

            positions[targetIndex] = targetPosition;
            velocities[targetIndex] = targetVelocity;
            return true;
        }

        public bool TryReserveDamage(int targetIndex, float damage)
        {
            if (!CanTarget(targetIndex) || damage <= 0f)
            {
                return false;
            }

            var effectiveDamage = GetEffectivePhysicalDamage(targetIndex, damage);
            if (effectiveDamage <= 0f || health[targetIndex] - reservedDamage[targetIndex] <= 0.001f)
            {
                return false;
            }

            reservedDamage[targetIndex] += effectiveDamage;
            gpuControls[targetIndex].w = reservedDamage[targetIndex];
            return true;
        }

        public float ResolveReservedDamage(int targetIndex, float damage)
        {
            ReleaseReservedDamage(targetIndex, damage);
            return ApplyDamage(targetIndex, damage);
        }

        public void ReleaseReservedDamage(int targetIndex, float damage)
        {
            if (targetIndex < 0 || targetIndex >= totalSpawned || reservedDamage == null)
            {
                return;
            }

            reservedDamage[targetIndex] = Mathf.Max(0f, reservedDamage[targetIndex] - GetEffectivePhysicalDamage(targetIndex, damage));
            gpuControls[targetIndex].w = reservedDamage[targetIndex];
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

        public float DamageTarget(int targetIndex, float damage)
        {
            return ApplyDamage(targetIndex, damage);
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
            int maxBurnStacks = 1,
            uint projectileId = 0u,
            bool resetHitCount = true)
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
                maxBurnStacks,
                projectileId,
                resetHitCount);
        }

        public uint CreateProjectileId()
        {
            return gpuSimulation != null ? gpuSimulation.CreateProjectileId() : 0u;
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

                var cost = Mathf.Max(0.1f,
                    (definitions[i] != null ? definitions[i].mass : 1f) * activeEnemyMassMultiplier);
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
            int maxBurnStacks = 0,
            float knockbackPropagationRadius = 0f)
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
                    maxBurnStacks,
                    knockbackPropagationRadius: knockbackPropagationRadius))
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
                        : new Vector3(
                            Mathf.Cos((i + 1) * 2.3999632f),
                            0f,
                            Mathf.Sin((i + 1) * 2.3999632f));
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

        private int FindBestTarget(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode, int excludedIndex = -1)
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
                        out var gpuVelocity,
                        excludedIndex))
                {
                    return -1;
                }

                if (gpuIndex >= 0 && gpuIndex < totalSpawned)
                {
                    previousPositions[gpuIndex] = positions[gpuIndex];
                    positions[gpuIndex] = gpuPosition;
                    velocities[gpuIndex] = gpuVelocity;
                }

                if (CanTarget(gpuIndex) && gpuIndex != excludedIndex)
                {
                    return gpuIndex;
                }

                if (excludedIndex < 0 && gpuSimulation.TryGetCachedSecondaryTarget(
                        position,
                        range,
                        canHitFlying,
                        targetingMode,
                        out var secondaryIndex,
                        out var secondaryPosition,
                        out var secondaryVelocity) &&
                    CanTarget(secondaryIndex))
                {
                    previousPositions[secondaryIndex] = positions[secondaryIndex];
                    positions[secondaryIndex] = secondaryPosition;
                    velocities[secondaryIndex] = secondaryVelocity;
                    return secondaryIndex;
                }

                gpuSimulation.InvalidateTargetCache();
                return -1;
            }

            var rangeSq = range * range;
            var bestIndex = -1;
            var bestDistanceSq = float.PositiveInfinity;
            var bestScore = float.MinValue;
            for (var i = 0; i < totalSpawned; i++)
            {
                if (i == excludedIndex || !CanTarget(i) || (definitions[i].isFlying && !canHitFlying))
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

        private bool CanTarget(int index)
        {
            return index >= 0 && index < totalSpawned && alive[index] && definitions[index] != null &&
                   health[index] - (reservedDamage != null ? reservedDamage[index] : 0f) > 0.001f;
        }

        private float GetEffectivePhysicalDamage(int index, float damage)
        {
            var definition = index >= 0 && index < totalSpawned ? definitions[index] : null;
            return Mathf.Max(0f, damage - (definition != null ? definition.armor : 0f)) *
                   (1f - Mathf.Clamp01(definition != null ? definition.physicalResistance : 0f));
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
                    ResolveGpuAgent(eventIndex, eventType == 1u);
                    continue;
                }

                if (eventType == 5u && eventIndex >= 0 && eventIndex < totalSpawned && alive[eventIndex])
                {
                    health[eventIndex] = Mathf.Max(0f, health[eventIndex] - Mathf.Max(0f, eventValue));
                    continue;
                }

                if (eventType == 6u)
                {
                    HordeProjectileVisual.CompleteProjectile((uint)eventIndex);
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

        private void ResolveGpuAgent(int index, bool killed)
        {
            if (index < 0 || index >= totalSpawned || !alive[index])
            {
                return;
            }

            var definition = definitions[index];
            alive[index] = false;
            health[index] = 0f;
            reservedDamage[index] = 0f;
            gpuControls[index] = Vector4.zero;
            activeCount--;
            totalResolved++;
            if (killed)
            {
                EnemyKilled?.Invoke(definition);
            }
            else
            {
                EnemyEscaped?.Invoke(definition);
            }
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
            CollectNearbyIndices(position, activeCollisionDiameter, nearbyIndices, 64);
            if (nearbyIndices.Count <= 1)
            {
                return Vector3.zero;
            }

            var push = Vector3.zero;
            var checks = 0;
            var radiusSq = activeCollisionDiameter * activeCollisionDiameter;
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

                var overlap = 1f - distance / activeCollisionDiameter;
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

            var enemyMass = Mathf.Max(0.1f, definitions[index].mass * activeEnemyMassMultiplier);
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
                frameTargetBlockedMass[bestTarget] = blockedMass +
                    Mathf.Max(0.1f, definitions[index].mass * activeEnemyMassMultiplier);
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
                return VisualRadius * activeEnemyScaleMultiplier;
            }

            return Mathf.Clamp(visualScales[index] * 0.78f, VisualRadius * 0.82f, VisualRadius * 1.45f) *
                   activeEnemyScaleMultiplier;
        }

        private float EstimateMaximumVisualRadius(IReadOnlyList<EnemyDefinition> definitionsToSpawn)
        {
            var maximumRadius = VisualRadius * 0.82f;
            if (definitionsToSpawn == null)
            {
                return maximumRadius * activeEnemyScaleMultiplier;
            }

            for (var i = 0; i < definitionsToSpawn.Count; i++)
            {
                var definitionScale = definitionsToSpawn[i] != null
                    ? definitionsToSpawn[i].visualScale
                    : 0.45f;
                var radius = Mathf.Clamp(
                    definitionScale * 0.78f,
                    VisualRadius * 0.82f,
                    VisualRadius * 1.45f);
                maximumRadius = Mathf.Max(maximumRadius, radius);
            }

            return maximumRadius * activeEnemyScaleMultiplier;
        }

        private float[] ContractWaypointWidths(IReadOnlyList<float> halfWidths)
        {
            if (halfWidths == null || halfWidths.Count == 0)
            {
                return null;
            }

            var contracted = new float[halfWidths.Count];
            for (var i = 0; i < halfWidths.Count; i++)
            {
                contracted[i] = Mathf.Max(activeNavigationWallInset, halfWidths[i] - activeNavigationWallInset);
            }

            return contracted;
        }

        private static float ResolveFlowCellSize(
            IReadOnlyList<float> primaryWidths,
            IReadOnlyList<float> secondaryWidths,
            IReadOnlyList<float> tertiaryWidths)
        {
            var minimumWidth = float.PositiveInfinity;
            FindMinimum(primaryWidths);
            FindMinimum(secondaryWidths);
            FindMinimum(tertiaryWidths);
            return minimumWidth <= 1.25f ? 0.4f : FlowCellSize;

            void FindMinimum(IReadOnlyList<float> widths)
            {
                if (widths == null)
                {
                    return;
                }

                for (var i = 0; i < widths.Count; i++)
                {
                    minimumWidth = Mathf.Min(minimumWidth, widths[i]);
                }
            }
        }

        private void CreateBoundaryDebugVisual()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!showNavigationBoundary || flowField == null)
            {
                return;
            }

            var segments = new List<HordeFlowField.BoundarySegment>();
            flowField.GetBoundarySegments(segments, activeNavigationWallInset);
            if (segments.Count == 0)
            {
                return;
            }

            var vertices = new Vector3[segments.Count * 4];
            var triangles = new int[segments.Count * 6];
            const float halfThickness = 0.07f;
            for (var i = 0; i < segments.Count; i++)
            {
                var from = segments[i].From + Vector3.up * 0.32f;
                var to = segments[i].To + Vector3.up * 0.32f;
                var direction = (to - from).normalized;
                var side = Vector3.Cross(Vector3.up, direction) * halfThickness;
                var vertex = i * 4;
                vertices[vertex] = from + side;
                vertices[vertex + 1] = from - side;
                vertices[vertex + 2] = to + side;
                vertices[vertex + 3] = to - side;
                var triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 2;
            }

            boundaryDebugMesh = new Mesh { name = "HordeWalkableBoundaryDebugMesh" };
            boundaryDebugMesh.indexFormat = IndexFormat.UInt32;
            boundaryDebugMesh.vertices = vertices;
            boundaryDebugMesh.triangles = triangles;
            boundaryDebugMesh.RecalculateBounds();
            boundaryDebugObject = new GameObject("HordeWalkableBoundaryDebug");
            boundaryDebugObject.transform.SetParent(transform, false);
            boundaryDebugObject.AddComponent<MeshFilter>().sharedMesh = boundaryDebugMesh;
            var renderer = boundaryDebugObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BootstrapMaterials.GetUnlit(new Color(0.05f, 0.95f, 1f, 1f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
#endif
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
            public readonly uint OverlapPairs;
            public readonly float MaximumOverlapFraction;
            public readonly uint OrdinaryBackwardSteps;
            public readonly float MaximumOrdinaryBacktrack;
            public readonly float NormalizedNeighborSpacing;
            public readonly uint ActiveContactPairs;
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
                uint overlapPairs,
                float maximumOverlapFraction,
                uint ordinaryBackwardSteps,
                float maximumOrdinaryBacktrack,
                float normalizedNeighborSpacing,
                uint activeContactPairs,
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
                OverlapPairs = overlapPairs;
                MaximumOverlapFraction = maximumOverlapFraction;
                OrdinaryBackwardSteps = ordinaryBackwardSteps;
                MaximumOrdinaryBacktrack = maximumOrdinaryBacktrack;
                NormalizedNeighborSpacing = normalizedNeighborSpacing;
                ActiveContactPairs = activeContactPairs;
                ShaderName = shaderName;
            }
        }
    }
}
