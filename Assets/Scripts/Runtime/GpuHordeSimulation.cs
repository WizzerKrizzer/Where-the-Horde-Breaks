using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// GPU-authoritative horde movement and rendering. CPU receives a throttled shadow
    /// copy for gameplay queries; it never integrates movement while this backend is active.
    /// </summary>
    internal sealed class GpuHordeSimulation : IDisposable
    {
        internal const float ShockwavePropagationSpeed = 20f;
        private const int ThreadGroupSize = 64;
        private const int CellCapacity = 96;
        private const int MaxTargetQueries = 512;
        private const int MaxAreaCommands = 256;
        private const int MaxProjectileCommands = 256;
        private const int TrackedProjectileCapacity = 2048;
        private const int MaxDynamicTargets = 256;
        private const int DynamicTargetCellCapacity = 8;
        private const int MaxDynamicTargetCommands = MaxDynamicTargets * 2;
        private const int ExtraCombatEventCapacity = 262144;
        private const float ReadbackInterval = 0.08f;
        public const int DiagnosticsCount = 19;
        private const int XpbdPairsPerBody = 48;
        private const int XpbdScanGroupSize = 256;
        // All grid-clearing kernels use 64-thread one-dimensional dispatches.
        private const int MaximumGridCells = 65535 * ThreadGroupSize;

        [Serializable, StructLayout(LayoutKind.Sequential)]
        internal struct AgentState
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public Vector2 KnockbackVelocity;
            public float Scale;
            public float Tint;
            public uint Status;
            public float Padding;
            public float Health;
            public float Progress;
            public uint Flags;
            public float Padding2;
            public float SlowMultiplier;
            public float SlowTimer;
            public float BurnDamagePerSecond;
            public float BurnTimer;
            public float Mass;
            public float ShockwaveExposure;
            public Vector2 ShockwaveCenter;
            public float ShockwaveOutwardTimer;
            public float ShockwaveReturnTimer;
            public float MaxHealth;
            public float Armor;
            public float PhysicalResistance;
            public float FireResistance;
            public float SlowResistance;
            public float AttackDamage;
            public float AttackInterval;
            public float WallDamageMultiplier;
            public float AlliedDamageMultiplier;
            public float AttackTimer;
            public uint CombatFlags;
            public uint DefinitionIndex;
            // Resolved travel drives gait; a separate clock keeps braced bodies alive.
            public float AnimationDistance;
            public Vector2 Facing;
            public Vector2 StepStartPosition;
            public float LocomotionSpeed;
            public float PresentationTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XpbdPair
        {
            public uint First;
            public uint Second;
            public float Lambda;
            public float Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TargetQuery
        {
            public Vector2 Position;
            public float Range;
            public uint Mode;
            public uint CanHitFlying;
            public int ExcludedIndex;
            public Vector2 Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TargetResult
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public int Index;
            public float Score;
            public Vector2 SecondPosition;
            public Vector2 SecondVelocity;
            public int SecondIndex;
            public float SecondScore;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DamageCommand
        {
            public uint Index;
            public float Damage;
            public Vector2 Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HordeEvent
        {
            public uint Index;
            public uint Type;
            public float Value;
            public uint Generation;
            public int SourceIndex;
            public float Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DynamicTargetState
        {
            public Vector2 Position;
            public float Health;
            public float MaxHealth;
            public float Radius;
            public Vector2 Axis;
            public float SegmentHalfLength;
            public float HalfDepth;
            public float BlockCapacity;
            public float Armor;
            public float PhysicalResistance;
            public float FireResistance;
            public float SlowResistance;
            public float ThornsDamage;
            public float SlowMultiplier;
            public float SlowTimer;
            public float BurnDamagePerSecond;
            public float BurnTimer;
            public uint Generation;
            public uint Status;
            public uint Kind;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DynamicTargetCommand
        {
            public uint Slot;
            public uint Generation;
            public uint Operation;
            public uint Kind;
            public Vector2 Position;
            public float Health;
            public float MaxHealth;
            public float Radius;
            public Vector2 Axis;
            public float SegmentHalfLength;
            public float HalfDepth;
            public float BlockCapacity;
            public float Armor;
            public float PhysicalResistance;
            public float FireResistance;
            public float SlowResistance;
            public float ThornsDamage;
            public float Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StatusCommand
        {
            public uint Index;
            public float SlowMultiplier;
            public float SlowDuration;
            public float BurnDamagePerSecond;
            public float BurnDuration;
            public uint MaxBurnStacks;
            public uint BurnApplications;
            public float Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AreaEffectCommand
        {
            public Vector2 Center;
            public float Radius;
            public float Damage;
            public float Knockback;
            public uint MaxTargets;
            public float BurnDamagePerSecond;
            public float BurnDuration;
            public uint MaxBurnStacks;
            public float SlowMultiplier;
            public float SlowDuration;
            public float SlowCapacity;
            public float InnerRadius;
        }

        private struct PendingShockwave
        {
            public Vector2 Center;
            public float BaseRadius;
            public float CurrentRadius;
            public float MaximumRadius;
            public float Knockback;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProjectileCommand
        {
            public Vector2 Start;
            public Vector2 End;
            public float Radius;
            public float Damage;
            public float Knockback;
            public uint MaxHits;
            public uint CanHitFlying;
            public uint Mode;
            public float BurnDamagePerSecond;
            public float BurnDuration;
            public uint MaxBurnStacks;
            public uint ProjectileId;
            public uint ResetHitCount;
            public Vector2 Padding;
        }

        private readonly struct TargetQueryKey : IEquatable<TargetQueryKey>
        {
            private readonly int x;
            private readonly int y;
            private readonly int range;
            private readonly byte mode;
            private readonly byte canHitFlying;
            private readonly int excludedIndex;
            private readonly bool exact;

            public TargetQueryKey(Vector3 position, float queryRange, TowerTargetingMode targetingMode, bool hitsFlying, int excludedTarget, bool exact = false)
            {
                this.exact = exact;
                x = exact ? BitConverter.SingleToInt32Bits(position.x) : Mathf.RoundToInt(position.x * 4f);
                y = exact ? BitConverter.SingleToInt32Bits(position.z) : Mathf.RoundToInt(position.z * 4f);
                range = exact ? BitConverter.SingleToInt32Bits(queryRange) : Mathf.RoundToInt(queryRange * 4f);
                mode = (byte)targetingMode;
                canHitFlying = hitsFlying ? (byte)1 : (byte)0;
                excludedIndex = excludedTarget;
            }

            public bool Equals(TargetQueryKey other) =>
                x == other.x && y == other.y && range == other.range && mode == other.mode &&
                canHitFlying == other.canHitFlying && excludedIndex == other.excludedIndex && exact == other.exact;

            public override bool Equals(object obj) => obj is TargetQueryKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(x, y, range, mode, canHitFlying, excludedIndex, exact);
        }

        private readonly struct CachedTarget
        {
            public readonly int Index;
            public readonly int Frame;
            public readonly float UnscaledTime;
            public readonly Vector2 Position;
            public readonly Vector2 Velocity;
            public readonly int SecondIndex;
            public readonly Vector2 SecondPosition;
            public readonly Vector2 SecondVelocity;
            public readonly double TravelBudget;

            public CachedTarget(int index, int frame, float unscaledTime, Vector2 position, Vector2 velocity, int secondIndex, Vector2 secondPosition, Vector2 secondVelocity, double travelBudget)
            {
                Index = index;
                Frame = frame;
                UnscaledTime = unscaledTime;
                Position = position;
                Velocity = velocity;
                SecondIndex = secondIndex;
                SecondPosition = secondPosition;
                SecondVelocity = secondVelocity;
                TravelBudget = travelBudget;
            }
        }

        private readonly struct PendingTargetQuery
        {
            public readonly TargetQueryKey Key;
            public readonly TargetQuery Query;

            public PendingTargetQuery(TargetQueryKey key, TargetQuery query)
            {
                Key = key;
                Query = query;
            }
        }

        private readonly int capacity;
        private readonly int eventCapacity;
        private readonly int cellCount;
        private readonly ComputeShader compute;
        private readonly int clearKernel;
        private readonly int spawnKernel;
        private readonly GraphicsBuffer spawnStates;
        private readonly GraphicsBuffer spawnControls;
        private readonly int clearDiagnosticsKernel;
        private readonly int gridKernel;
        private readonly int simulateKernel;
        private bool stationaryArena;
        private float maximumDesiredSpeed = 0.25f;
        private float maximumMotionDistance;
        private double motionTravelBudget;
        private float MaximumPairDiameter => collisionDiameter * 1.55f;
        private const float FarLodVisibilityScale = 1f;
        // Above this height players can read the authored limb animation. Below it,
        // the compact LOD communicates only collective direction and travel.
        private const float SimplifiedGoblinLodMaxHeightPixels = 96f;
        private readonly int targetQueryKernel;
        private readonly int damageKernel;
        private readonly int statusKernel;
        private readonly int areaEffectKernel;
        private readonly int unlimitedAreaEffectsKernel;
        private readonly int clearVisibilityKernel;
        private readonly int cullVisibleKernel;
        private readonly int finalizeActiveKernel;
        private readonly int applyDynamicTargetCommandsKernel;
        private readonly int clearDynamicTargetGridKernel;
        private readonly int buildDynamicTargetGridKernel;
        private readonly int resolveDynamicTargetDamageKernel;
        private readonly int projectileKernel;
        private readonly int clearXpbdGridKernel;
        private readonly int countXpbdGridKernel;
        private readonly int scanXpbdGridBlocksKernel;
        private readonly int scanXpbdGridBlockTotalsKernel;
        private readonly int addXpbdGridBlockOffsetsKernel;
        private readonly int finalizeXpbdGridKernel;
        private readonly int scatterXpbdGridKernel;
        private readonly int clearXpbdPairsKernel;
        private readonly int buildXpbdPairsKernel;
        private readonly int finalizeXpbdPairDispatchKernel;
        private readonly int clearXpbdCorrectionsKernel;
        private readonly int solveXpbdPairsKernel;
        private readonly int solveXpbdWallsKernel;
        private readonly int applyXpbdCorrectionsKernel;
        private readonly int measureXpbdGridDiagnosticsKernel;
        private readonly int measureXpbdPairDiagnosticsKernel;
        private readonly int measureXpbdBodyDiagnosticsKernel;
        private readonly int finalizeXpbdPersistentContactsKernel;
        private readonly GraphicsBuffer stateA;
        private readonly GraphicsBuffer stateB;
        private readonly GraphicsBuffer spatialStates;
        private readonly GraphicsBuffer controls;
        private readonly GraphicsBuffer impulses;
        private readonly GraphicsBuffer flowVectors;
        private readonly GraphicsBuffer flowData;
        private readonly GraphicsBuffer cellCounts;
        private readonly GraphicsBuffer cellAgents;
        private readonly GraphicsBuffer indirectArgs;
        private readonly GraphicsBuffer diagnostics;
        private readonly GraphicsBuffer targetQueries;
        private readonly GraphicsBuffer targetResults;
        private readonly GraphicsBuffer damageCommands;
        private readonly GraphicsBuffer statusCommands;
        private readonly GraphicsBuffer areaEffectCommands;
        private readonly GraphicsBuffer projectileCommands;
        private readonly GraphicsBuffer projectileHitCounts;
        private readonly GraphicsBuffer agentLastProjectileIds;
        private readonly GraphicsBuffer hordeEvents;
        private readonly GraphicsBuffer eventCount;
        private readonly GraphicsBuffer dynamicTargets;
        private readonly GraphicsBuffer dynamicTargetCommands;
        private readonly GraphicsBuffer dynamicTargetCellCounts;
        private readonly GraphicsBuffer dynamicTargetCellIndices;
        private readonly GraphicsBuffer dynamicTargetActiveIndices;
        private readonly GraphicsBuffer dynamicTargetActiveCount;
        private readonly GraphicsBuffer dynamicTargetBlockedMass;
        private readonly GraphicsBuffer dynamicTargetDamage;
        private readonly GraphicsBuffer dynamicTargetLastAttacker;
        private readonly GraphicsBuffer visibleIndices;
        private readonly GraphicsBuffer lodIndirectArgs;
        private readonly GraphicsBuffer variantIndirectArgs;
        private readonly GraphicsBuffer variantLodIndirectArgs;
        private readonly GraphicsBuffer activeIndices;
        private readonly GraphicsBuffer activeDispatchArgs;
        private readonly GraphicsBuffer xpbdCellCounts;
        private readonly GraphicsBuffer xpbdCellOffsets;
        private readonly GraphicsBuffer xpbdCellCursors;
        private readonly GraphicsBuffer xpbdCellAgents;
        private readonly GraphicsBuffer xpbdBlockSums;
        private readonly GraphicsBuffer xpbdBlockOffsets;
        private readonly GraphicsBuffer xpbdPairs;
        private readonly GraphicsBuffer xpbdPairCount;
        private readonly GraphicsBuffer xpbdPairDispatchArgs;
        private readonly GraphicsBuffer xpbdCorrectionX;
        private readonly GraphicsBuffer xpbdCorrectionY;
        private readonly GraphicsBuffer xpbdCorrectionCounts;
        private readonly GraphicsBuffer xpbdWallLambdas;
        private readonly GraphicsBuffer xpbdGridFailures;
        private readonly GraphicsBuffer xpbdDeepContactMarked;
        private readonly GraphicsBuffer xpbdDeepContactTicks;
        private readonly int xpbdPairCapacity;
        private readonly int xpbdBlockCount;
        private readonly Material material;
        private readonly MaterialPropertyBlock properties = new();
        private readonly MaterialPropertyBlock lodProperties = new();
        private readonly MaterialPropertyBlock variantProperties = new();
        private readonly MaterialPropertyBlock variantLodProperties = new();
        private readonly Mesh lowDetailMesh;
        private readonly Mesh stableRenderMesh;
        private readonly Mesh variantRenderMesh;
        private readonly Mesh variantLodRenderMesh;
        private readonly Bounds drawBounds;
        private readonly uint[] drawArgs = new uint[5];
        private readonly uint[] lodDrawArgs = new uint[5];
        private readonly uint[] variantDrawArgs = new uint[5];
        private readonly uint[] variantLodDrawArgs = new uint[5];
        private readonly AgentState[] singleStateUpload = new AgentState[1];
        private readonly Vector4[] singleControlUpload = new Vector4[1];
        private readonly List<PendingTargetQuery> pendingTargetQueries = new(MaxTargetQueries);
        private readonly HashSet<TargetQueryKey> queuedTargetKeys = new();
        private readonly Dictionary<TargetQueryKey, CachedTarget> cachedTargets = new();
        private readonly TargetQuery[] targetQueryUpload = new TargetQuery[MaxTargetQueries];
        private readonly TargetQueryKey[] inFlightTargetKeys = new TargetQueryKey[MaxTargetQueries];
        private readonly Dictionary<int, float> queuedDamage = new();
        private readonly Dictionary<int, StatusCommand> queuedStatus = new();
        private readonly DamageCommand[] damageUpload;
        private readonly StatusCommand[] statusUpload;
        private readonly List<AreaEffectCommand> queuedAreaEffects = new(MaxAreaCommands);
        private readonly List<PendingShockwave> pendingShockwaves = new(32);
        private readonly AreaEffectCommand[] areaEffectUpload = new AreaEffectCommand[MaxAreaCommands];
        private readonly List<ProjectileCommand> queuedProjectiles = new(MaxProjectileCommands);
        private readonly ProjectileCommand[] projectileUpload = new ProjectileCommand[MaxProjectileCommands];
        private readonly Queue<HordeEvent> receivedEvents = new();
        private readonly Dictionary<ICombatTarget, int> dynamicTargetSlots = new();
        private readonly ICombatTarget[] dynamicTargetOwners = new ICombatTarget[MaxDynamicTargets];
        private readonly uint[] dynamicTargetGenerations = new uint[MaxDynamicTargets];
        private readonly Queue<int> freeDynamicTargetSlots = new();
        private readonly List<ICombatTarget> staleDynamicTargets = new(MaxDynamicTargets);
        private readonly HashSet<ICombatTarget> seenDynamicTargets = new();
        private readonly DynamicTargetCommand[] dynamicTargetCommandUpload = new DynamicTargetCommand[MaxDynamicTargetCommands];
        private int dynamicTargetCommandCount;
        private readonly Vector2 gridCenter;
        private readonly float gridDiagonal;
        private readonly float gridCellSize;
        private readonly int gridWidth;
        private readonly int gridHeight;
        private float collisionDiameter;
        private bool xpbdUsesPerBodyRadius;
        private GraphicsBuffer readStates;
        private GraphicsBuffer writeStates;
        private bool disposed;
        private bool diagnosticsReadbackPending;
        private bool targetReadbackPending;
        private bool eventCountReadbackPending;
        private bool eventDataReadbackPending;
        private uint processedEventCount;
        private float nextDiagnosticsReadbackTime;
        private int activeHighWaterMark;
        private float presentationAlpha = 1f;
        private float lastSimulationDelta;
        private uint simulationFrameIndex;
        private uint nextProjectileId = 1u;
        private bool centerSeekingArena;
        private float movementSpeedMultiplier = 1f;
        private CrowdSolverProfile solverProfile;
        private double simulationSeconds;
        private double lastDiagnosticsSimulationSeconds;

        public uint OverflowCellCount { get; private set; }
        public uint DroppedAgentCount { get; private set; }
        public uint MaximumCellOccupancy { get; private set; }
        public uint GpuActiveCount { get; private set; }
        public uint VisibleAgentCount { get; private set; }
        public uint OverlapPairCount { get; private set; }
        public float MaximumOverlapFraction { get; private set; }
        public uint OrdinaryBackwardStepCount { get; private set; }
        public float MaximumOrdinaryBacktrack { get; private set; }
        public float NormalizedNeighborSpacing { get; private set; }
        public uint ActiveContactPairCount { get; private set; }
        public uint ShallowContactPairCount { get; private set; }
        public uint DeepOverlapPairCount { get; private set; }
        public uint PersistentDeepContactBodyCount { get; private set; }
        public uint DeepDiskContactPairCount { get; private set; }
        public uint DeepBlockerContactCount { get; private set; }
        public uint DeepWallContactCount { get; private set; }
        public float DeepContactDurationSeconds { get; private set; }
        public uint GridCapacityFailureCount { get; private set; }
        public CrowdSolverDiagnostics SolverDiagnostics => new(
            ShallowContactPairCount,
            DeepOverlapPairCount,
            PersistentDeepContactBodyCount,
            MaximumOverlapFraction,
            DeepContactDurationSeconds,
            ActiveContactPairCount,
            MaximumCellOccupancy,
            GridCapacityFailureCount,
            DeepDiskContactPairCount,
            DeepBlockerContactCount,
            DeepWallContactCount);
        internal float FixedStep => solverProfile != null ? solverProfile.FixedStep : 1f / 60f;
        internal bool SupportsXpbdGrid => cellCount <= MaximumGridCells;
        internal int LastContactIterationCount { get; private set; }
        public void SetMovementSpeedMultiplier(float multiplier)
        {
            movementSpeedMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>Applies the one fixed-step crowd contract used by every level.</summary>
        public void SetCrowdSolverProfile(CrowdSolverProfile profile)
        {
            solverProfile = profile != null ? profile : CrowdSolverProfile.Default;
            compute.SetFloat("_XpbdContactCompliance", Mathf.Max(0f, solverProfile.contactCompliance));
            compute.SetFloat("_XpbdWallCompliance", Mathf.Max(0f, solverProfile.wallCompliance));
            compute.SetFloat("_XpbdMaximumSpeed", Mathf.Max(0.1f, solverProfile.maximumSpeed));
            compute.SetFloat("_XpbdMaximumTravel", Mathf.Max(0.005f, solverProfile.maximumTravelPerTick));
            compute.SetFloat("_XpbdBodyRadiusPerVisualScale", Mathf.Clamp(solverProfile.bodyRadiusPerVisualScale, 0.1f, 1f));
            compute.SetFloat("_FlowAcceleration", Mathf.Max(0.1f, solverProfile.propulsionAcceleration));
            compute.SetFloat("_VelocityDamping", Mathf.Max(0.01f, solverProfile.velocityDamping));
        }

        /// <summary>
        /// Advances one exact simulation tick. Tests use this instead of a render
        /// cadence so scheduling cannot alter contact behavior.
        /// </summary>
        internal void DispatchFixedTick(
            Vector4[] controlData,
            Vector2[] impulseData,
            bool requestReadback = true,
            bool updatePresentation = true,
            bool updateVisibility = true)
        {
            Dispatch(FixedStep, controlData, impulseData, requestReadback, 1f, updatePresentation, updateVisibility);
        }

        private GpuHordeSimulation(int agentCapacity, HordeFlowField field, Mesh mesh)
        {
            capacity = agentCapacity;
            eventCapacity = capacity + ExtraCombatEventCapacity;
            cellCount = field.Width * field.Height;
            if (cellCount > MaximumGridCells)
            {
                throw new InvalidOperationException(
                    $"XPBD compact grid has {cellCount} cells; one-dimensional dispatch supports at most " +
                    $"{MaximumGridCells}. Refusing to fall back to an incomplete solver.");
            }
            gridCellSize = field.CellSize;
            gridWidth = field.Width;
            gridHeight = field.Height;
            var sourceCompute = Resources.Load<ComputeShader>("HordeSimulation");
            var shader = Resources.Load<Shader>("HordeIndirect");
            if (sourceCompute == null || shader == null)
            {
                throw new InvalidOperationException("GPU horde resources were not found.");
            }

            compute = UnityEngine.Object.Instantiate(sourceCompute);
            material = new Material(shader) { enableInstancing = true };
            lowDetailMesh = EnemyManager.GetLowEnemyMesh();
            stableRenderMesh = lowDetailMesh;
            // Greybox placeholders: the horde reads as one neutral value; slow,
            // rare and speed bands differ only in brightness.
            material.SetColor("_BaseColor", PlaceholderPalette.Enemy);
            material.SetColor("_SlowColor", PlaceholderPalette.Grey(0.80f));
            material.SetColor("_RareColor", PlaceholderPalette.Accent);
            material.SetColor("_SpeedColor0", PlaceholderPalette.Grey(0.30f));
            material.SetColor("_SpeedColor1", PlaceholderPalette.Grey(0.42f));
            material.SetColor("_SpeedColor2", PlaceholderPalette.Grey(0.55f));
            material.SetColor("_SpeedColor3", PlaceholderPalette.Grey(0.68f));
            material.SetFloat("_UseSpeedColors", 0f);
            properties.SetFloat("_LightingVariation", 1f);
            lodProperties.SetFloat("_LightingVariation", 1f);
            variantProperties.SetFloat("_LightingVariation", 1f);
            variantLodProperties.SetFloat("_LightingVariation", 1f);
            properties.SetFloat("_VisualScaleMultiplier", 1f);
            // The low-poly model takes fewer pixels at distance. A modest visual
            // compensation preserves a readable crowd without increasing physics
            // radii or rendering the full mesh for every distant agent.
            lodProperties.SetFloat("_VisualScaleMultiplier", FarLodVisibilityScale);
            variantProperties.SetFloat("_VisualScaleMultiplier", 1f);
            variantLodProperties.SetFloat("_VisualScaleMultiplier", 1f);
            clearKernel = compute.FindKernel("ClearGrid");
            spawnKernel = compute.FindKernel("ApplySpawns");
            spawnStates = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Marshal.SizeOf<AgentState>());
            spawnControls = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 4);
            clearDiagnosticsKernel = compute.FindKernel("ClearDiagnostics");
            gridKernel = compute.FindKernel("BuildGrid");
            simulateKernel = compute.FindKernel("Simulate");
            targetQueryKernel = compute.FindKernel("QueryTargets");
            damageKernel = compute.FindKernel("ApplyDamageCommands");
            statusKernel = compute.FindKernel("ApplyStatusCommands");
            areaEffectKernel = compute.FindKernel("ApplyAreaEffect");
            unlimitedAreaEffectsKernel = compute.FindKernel("ApplyUnlimitedAreaEffects");
            clearVisibilityKernel = compute.FindKernel("ClearVisibility");
            cullVisibleKernel = compute.FindKernel("CullVisible");
            finalizeActiveKernel = compute.FindKernel("FinalizeActiveDispatch");
            applyDynamicTargetCommandsKernel = compute.FindKernel("ApplyDynamicTargetCommands");
            clearDynamicTargetGridKernel = compute.FindKernel("ClearDynamicTargetGrid");
            buildDynamicTargetGridKernel = compute.FindKernel("BuildDynamicTargetGrid");
            resolveDynamicTargetDamageKernel = compute.FindKernel("ResolveDynamicTargetDamage");
            projectileKernel = compute.FindKernel("ApplyProjectile");
            clearXpbdGridKernel = compute.FindKernel("ClearXpbdGrid");
            countXpbdGridKernel = compute.FindKernel("CountXpbdGrid");
            scanXpbdGridBlocksKernel = compute.FindKernel("ScanXpbdGridBlocks");
            scanXpbdGridBlockTotalsKernel = compute.FindKernel("ScanXpbdGridBlockTotals");
            addXpbdGridBlockOffsetsKernel = compute.FindKernel("AddXpbdGridBlockOffsets");
            finalizeXpbdGridKernel = compute.FindKernel("FinalizeXpbdGrid");
            scatterXpbdGridKernel = compute.FindKernel("ScatterXpbdGrid");
            clearXpbdPairsKernel = compute.FindKernel("ClearXpbdPairs");
            buildXpbdPairsKernel = compute.FindKernel("BuildXpbdPairs");
            finalizeXpbdPairDispatchKernel = compute.FindKernel("FinalizeXpbdPairDispatch");
            clearXpbdCorrectionsKernel = compute.FindKernel("ClearXpbdCorrections");
            solveXpbdPairsKernel = compute.FindKernel("SolveXpbdPairs");
            solveXpbdWallsKernel = compute.FindKernel("SolveXpbdWalls");
            applyXpbdCorrectionsKernel = compute.FindKernel("ApplyXpbdCorrections");
            measureXpbdGridDiagnosticsKernel = compute.FindKernel("MeasureXpbdGridDiagnostics");
            measureXpbdPairDiagnosticsKernel = compute.FindKernel("MeasureXpbdPairDiagnostics");
            measureXpbdBodyDiagnosticsKernel = compute.FindKernel("MeasureXpbdBodyDiagnostics");
            finalizeXpbdPersistentContactsKernel = compute.FindKernel("FinalizeXpbdPersistentContacts");

            var stateStride = Marshal.SizeOf<AgentState>();
            stateA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, stateStride);
            stateB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, stateStride);
            spatialStates = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 4);
            controls = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 4);
            impulses = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 2);
            flowVectors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(float) * 4);
            flowData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(float) * 4);
            cellCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));
            cellAgents = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount * CellCapacity, sizeof(uint));
            indirectArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 5, sizeof(uint));
            diagnostics = new GraphicsBuffer(GraphicsBuffer.Target.Structured, DiagnosticsCount, sizeof(uint));
            targetQueries = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTargetQueries, Marshal.SizeOf<TargetQuery>());
            targetResults = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTargetQueries, Marshal.SizeOf<TargetResult>());
            damageCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Marshal.SizeOf<DamageCommand>());
            statusCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Marshal.SizeOf<StatusCommand>());
            areaEffectCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxAreaCommands, Marshal.SizeOf<AreaEffectCommand>());
            projectileCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxProjectileCommands, Marshal.SizeOf<ProjectileCommand>());
            projectileHitCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, TrackedProjectileCapacity, sizeof(uint));
            agentLastProjectileIds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            hordeEvents = new GraphicsBuffer(GraphicsBuffer.Target.Structured, eventCapacity, Marshal.SizeOf<HordeEvent>());
            eventCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            dynamicTargets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, Marshal.SizeOf<DynamicTargetState>());
            dynamicTargetCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargetCommands, Marshal.SizeOf<DynamicTargetCommand>());
            dynamicTargetCellCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));
            dynamicTargetCellIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount * DynamicTargetCellCapacity, sizeof(uint));
            dynamicTargetActiveIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, sizeof(uint));
            dynamicTargetActiveCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            dynamicTargetBlockedMass = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, sizeof(uint));
            dynamicTargetDamage = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, sizeof(uint));
            dynamicTargetLastAttacker = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, sizeof(uint));
            // Four packed lists: primary near/far, then secondary near/far. Packing
            // avoids exceeding D3D11's eight-UAV limit in the visibility kernel.
            visibleIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 4, sizeof(uint));
            lodIndirectArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 5, sizeof(uint));
            variantIndirectArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 5, sizeof(uint));
            variantLodIndirectArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 5, sizeof(uint));
            activeIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            activeDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 3, sizeof(uint));
            xpbdBlockCount = DivideRoundUp(cellCount, XpbdScanGroupSize);
            xpbdPairCapacity = checked(capacity * XpbdPairsPerBody);
            xpbdCellCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));
            xpbdCellOffsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount + 1, sizeof(uint));
            xpbdCellCursors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));
            xpbdCellAgents = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            xpbdBlockSums = new GraphicsBuffer(GraphicsBuffer.Target.Structured, xpbdBlockCount, sizeof(uint));
            xpbdBlockOffsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, xpbdBlockCount, sizeof(uint));
            xpbdPairs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, xpbdPairCapacity, Marshal.SizeOf<XpbdPair>());
            xpbdPairCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            xpbdPairDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 3, sizeof(uint));
            xpbdCorrectionX = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(int));
            xpbdCorrectionY = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(int));
            xpbdCorrectionCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            xpbdWallLambdas = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float));
            xpbdGridFailures = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            xpbdDeepContactMarked = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            xpbdDeepContactTicks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            damageUpload = new DamageCommand[capacity];
            statusUpload = new StatusCommand[capacity];
            readStates = stateA;
            writeStates = stateB;

            var emptyStates = new AgentState[capacity];
            stateA.SetData(emptyStates);
            stateB.SetData(emptyStates);
            projectileHitCounts.SetData(new uint[TrackedProjectileCapacity]);
            agentLastProjectileIds.SetData(new uint[capacity]);
            controls.SetData(new Vector4[capacity]);
            impulses.SetData(new Vector2[capacity]);
            eventCount.SetData(new uint[1]);
            dynamicTargets.SetData(new DynamicTargetState[MaxDynamicTargets]);
            dynamicTargetActiveCount.SetData(new uint[1]);
            xpbdDeepContactTicks.SetData(new uint[capacity]);
            for (var slot = 0; slot < MaxDynamicTargets; slot++)
            {
                freeDynamicTargetSlots.Enqueue(slot);
            }
            field.BuildGpuData(out var vectorData, out var scalarData);
            flowVectors.SetData(vectorData);
            flowData.SetData(scalarData);
            drawArgs[0] = stableRenderMesh.GetIndexCount(0);
            drawArgs[1] = 0u;
            drawArgs[2] = stableRenderMesh.GetIndexStart(0);
            drawArgs[3] = (uint)stableRenderMesh.GetBaseVertex(0);
            indirectArgs.SetData(drawArgs);
            lodDrawArgs[0] = lowDetailMesh.GetIndexCount(0);
            lodDrawArgs[1] = 0u;
            lodDrawArgs[2] = lowDetailMesh.GetIndexStart(0);
            lodDrawArgs[3] = (uint)lowDetailMesh.GetBaseVertex(0);
            lodIndirectArgs.SetData(lodDrawArgs);
            if (variantRenderMesh != null)
            {
                variantDrawArgs[0] = variantRenderMesh.GetIndexCount(0);
                variantDrawArgs[2] = variantRenderMesh.GetIndexStart(0);
                variantDrawArgs[3] = (uint)variantRenderMesh.GetBaseVertex(0);
            }
            if (variantLodRenderMesh != null)
            {
                variantLodDrawArgs[0] = variantLodRenderMesh.GetIndexCount(0);
                variantLodDrawArgs[2] = variantLodRenderMesh.GetIndexStart(0);
                variantLodDrawArgs[3] = (uint)variantLodRenderMesh.GetBaseVertex(0);
            }
            variantIndirectArgs.SetData(variantDrawArgs);
            variantLodIndirectArgs.SetData(variantLodDrawArgs);

            var center = field.Origin + new Vector3(field.Width * field.CellSize * 0.5f, 0f, field.Height * field.CellSize * 0.5f);
            drawBounds = new Bounds(center, new Vector3(field.Width * field.CellSize + 4f, 20f, field.Height * field.CellSize + 4f));
            gridCenter = new Vector2(center.x, center.z);
            gridDiagonal = new Vector2(field.Width * field.CellSize, field.Height * field.CellSize).magnitude;

            compute.SetInt("_AgentCount", 0);
            compute.SetInt("_CellCount", cellCount);
            compute.SetInt("_CellCapacity", CellCapacity);
            compute.SetInts("_GridSize", field.Width, field.Height);
            compute.SetVector("_GridOrigin", new Vector4(field.Origin.x, field.Origin.z, 0f, 0f));
            compute.SetFloat("_CellSize", field.CellSize);
            SetCollisionDiameter(0.72f);
            compute.SetInt("_AnimatedGoblin", 0);
            compute.SetInt("_SpriteCardRendering", 0);
            compute.SetInt("_StationaryHorde", 0);
            compute.SetInt("_RandomArenaMovement", 0);
            compute.SetInt("_CenterSeekingArena", 0);
            compute.SetVector("_ArenaCenter", Vector4.zero);
            compute.SetInt("_ExitEnabled", 1);
            compute.SetInt("_EventCapacity", eventCapacity);
            compute.SetInt("_DynamicTargetCapacity", MaxDynamicTargets);
            compute.SetInt("_DynamicTargetCellCapacity", DynamicTargetCellCapacity);
            compute.SetBuffer(clearKernel, "_CellCounts", cellCounts);
            compute.SetBuffer(clearDiagnosticsKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(gridKernel, "_Controls", controls);
            compute.SetBuffer(gridKernel, "_CellCounts", cellCounts);
            compute.SetBuffer(gridKernel, "_CellAgents", cellAgents);
            compute.SetBuffer(gridKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(gridKernel, "_ActiveIndices", activeIndices);
            compute.SetBuffer(gridKernel, "_SpatialStates", spatialStates);
            compute.SetBuffer(finalizeActiveKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(finalizeActiveKernel, "_ActiveDispatchArgs", activeDispatchArgs);
            compute.SetBuffer(simulateKernel, "_Controls", controls);
            compute.SetBuffer(simulateKernel, "_Impulses", impulses);
            compute.SetBuffer(simulateKernel, "_FlowVectors", flowVectors);
            compute.SetBuffer(simulateKernel, "_FlowData", flowData);
            compute.SetBuffer(simulateKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(simulateKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(simulateKernel, "_ActiveIndicesRead", activeIndices);
            compute.SetBuffer(simulateKernel, "_DiagnosticsRead", diagnostics);
            compute.SetBuffer(simulateKernel, "_SpatialStatesRead", spatialStates);
            compute.SetBuffer(targetQueryKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(targetQueryKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(targetQueryKernel, "_TargetQueries", targetQueries);
            compute.SetBuffer(targetQueryKernel, "_TargetResults", targetResults);
            compute.SetBuffer(targetQueryKernel, "_Controls", controls);
            compute.SetBuffer(damageKernel, "_DamageCommands", damageCommands);
            compute.SetBuffer(damageKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(damageKernel, "_EventCount", eventCount);
            compute.SetBuffer(statusKernel, "_StatusCommands", statusCommands);
            compute.SetBuffer(areaEffectKernel, "_AreaEffectCommands", areaEffectCommands);
            compute.SetBuffer(areaEffectKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(areaEffectKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(areaEffectKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(areaEffectKernel, "_EventCount", eventCount);
            compute.SetBuffer(unlimitedAreaEffectsKernel, "_AreaEffectCommands", areaEffectCommands);
            compute.SetBuffer(unlimitedAreaEffectsKernel, "_ActiveIndicesRead", activeIndices);
            compute.SetBuffer(unlimitedAreaEffectsKernel, "_DiagnosticsRead", diagnostics);
            compute.SetBuffer(unlimitedAreaEffectsKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(unlimitedAreaEffectsKernel, "_FlowData", flowData);
            compute.SetBuffer(unlimitedAreaEffectsKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(unlimitedAreaEffectsKernel, "_EventCount", eventCount);
            compute.SetBuffer(projectileKernel, "_ProjectileCommands", projectileCommands);
            compute.SetBuffer(projectileKernel, "_ProjectileHitCounts", projectileHitCounts);
            compute.SetBuffer(projectileKernel, "_AgentLastProjectileIds", agentLastProjectileIds);
            compute.SetBuffer(projectileKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(projectileKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(projectileKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(projectileKernel, "_EventCount", eventCount);
            compute.SetBuffer(simulateKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(simulateKernel, "_EventCount", eventCount);
            compute.SetBuffer(applyDynamicTargetCommandsKernel, "_DynamicTargetCommands", dynamicTargetCommands);
            compute.SetBuffer(applyDynamicTargetCommandsKernel, "_DynamicTargets", dynamicTargets);
            compute.SetBuffer(clearDynamicTargetGridKernel, "_DynamicTargetCellCounts", dynamicTargetCellCounts);
            compute.SetBuffer(clearDynamicTargetGridKernel, "_DynamicTargetActiveCount", dynamicTargetActiveCount);
            compute.SetBuffer(clearDynamicTargetGridKernel, "_DynamicTargetBlockedMass", dynamicTargetBlockedMass);
            compute.SetBuffer(clearDynamicTargetGridKernel, "_DynamicTargetDamage", dynamicTargetDamage);
            compute.SetBuffer(clearDynamicTargetGridKernel, "_DynamicTargetLastAttacker", dynamicTargetLastAttacker);
            compute.SetBuffer(buildDynamicTargetGridKernel, "_DynamicTargetsRead", dynamicTargets);
            compute.SetBuffer(buildDynamicTargetGridKernel, "_DynamicTargetCellCounts", dynamicTargetCellCounts);
            compute.SetBuffer(buildDynamicTargetGridKernel, "_DynamicTargetCellIndices", dynamicTargetCellIndices);
            compute.SetBuffer(buildDynamicTargetGridKernel, "_DynamicTargetActiveIndices", dynamicTargetActiveIndices);
            compute.SetBuffer(buildDynamicTargetGridKernel, "_DynamicTargetActiveCount", dynamicTargetActiveCount);
            compute.SetBuffer(simulateKernel, "_DynamicTargetsRead", dynamicTargets);
            compute.SetBuffer(simulateKernel, "_DynamicTargetActiveIndicesRead", dynamicTargetActiveIndices);
            compute.SetBuffer(simulateKernel, "_DynamicTargetActiveCountRead", dynamicTargetActiveCount);
            compute.SetBuffer(simulateKernel, "_DynamicTargetCellCountsRead", dynamicTargetCellCounts);
            compute.SetBuffer(simulateKernel, "_DynamicTargetCellIndicesRead", dynamicTargetCellIndices);
            compute.SetBuffer(simulateKernel, "_DynamicTargetBlockedMass", dynamicTargetBlockedMass);
            compute.SetBuffer(simulateKernel, "_DynamicTargetDamage", dynamicTargetDamage);
            compute.SetBuffer(simulateKernel, "_DynamicTargetLastAttacker", dynamicTargetLastAttacker);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_DynamicTargets", dynamicTargets);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_StatesInput", writeStates);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_DynamicTargetDamage", dynamicTargetDamage);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_DynamicTargetLastAttacker", dynamicTargetLastAttacker);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_EventCount", eventCount);
            compute.SetBuffer(clearVisibilityKernel, "_IndirectArgs", indirectArgs);
            compute.SetBuffer(clearVisibilityKernel, "_LodIndirectArgs", lodIndirectArgs);
            compute.SetBuffer(clearVisibilityKernel, "_VariantIndirectArgs", variantIndirectArgs);
            compute.SetBuffer(clearVisibilityKernel, "_VariantLodIndirectArgs", variantLodIndirectArgs);
            compute.SetBuffer(cullVisibleKernel, "_IndirectArgs", indirectArgs);
            compute.SetBuffer(cullVisibleKernel, "_VisibleIndices", visibleIndices);
            compute.SetBuffer(cullVisibleKernel, "_LodIndirectArgs", lodIndirectArgs);
            compute.SetBuffer(cullVisibleKernel, "_VariantIndirectArgs", variantIndirectArgs);
            compute.SetBuffer(cullVisibleKernel, "_VariantLodIndirectArgs", variantLodIndirectArgs);
            compute.SetInt("_GoblinVariantCulling", 0);
            compute.SetInt("_VisibleListCapacity", capacity);
            properties.SetInt("_VisibleIndexOffset", 0);
            lodProperties.SetInt("_VisibleIndexOffset", capacity);
            variantProperties.SetInt("_VisibleIndexOffset", capacity * 2);
            variantLodProperties.SetInt("_VisibleIndexOffset", capacity * 3);
            compute.SetBuffer(cullVisibleKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(cullVisibleKernel, "_ActiveIndicesRead", activeIndices);
            compute.SetBuffer(cullVisibleKernel, "_DiagnosticsRead", diagnostics);

            compute.SetInt("_XpbdPairCapacity", xpbdPairCapacity);
            compute.SetInt("_XpbdBlockCount", xpbdBlockCount);
            SetCrowdSolverProfile(CrowdSolverProfile.Default);
            compute.SetInt("_XpbdUsePerBodyRadius", 0);
            foreach (var kernel in new[] { countXpbdGridKernel, scatterXpbdGridKernel, buildXpbdPairsKernel,
                         clearXpbdCorrectionsKernel, solveXpbdWallsKernel, applyXpbdCorrectionsKernel })
            {
                compute.SetBuffer(kernel, "_ActiveIndicesRead", activeIndices);
                compute.SetBuffer(kernel, "_DiagnosticsRead", diagnostics);
            }
            foreach (var kernel in new[] { clearXpbdGridKernel, countXpbdGridKernel, scanXpbdGridBlocksKernel,
                         scanXpbdGridBlockTotalsKernel, addXpbdGridBlockOffsetsKernel, finalizeXpbdGridKernel,
                         scatterXpbdGridKernel, buildXpbdPairsKernel })
            {
                compute.SetBuffer(kernel, "_XpbdCellCounts", xpbdCellCounts);
                compute.SetBuffer(kernel, "_XpbdCellOffsets", xpbdCellOffsets);
                compute.SetBuffer(kernel, "_XpbdCellCursors", xpbdCellCursors);
            }
            foreach (var kernel in new[] { scanXpbdGridBlocksKernel, scanXpbdGridBlockTotalsKernel,
                         addXpbdGridBlockOffsetsKernel })
            {
                compute.SetBuffer(kernel, "_XpbdBlockSums", xpbdBlockSums);
                compute.SetBuffer(kernel, "_XpbdBlockOffsets", xpbdBlockOffsets);
            }
            foreach (var kernel in new[] { scatterXpbdGridKernel, buildXpbdPairsKernel })
            {
                compute.SetBuffer(kernel, "_XpbdCellAgents", xpbdCellAgents);
            }
            foreach (var kernel in new[] { clearXpbdPairsKernel, buildXpbdPairsKernel,
                         finalizeXpbdPairDispatchKernel, solveXpbdPairsKernel })
            {
                compute.SetBuffer(kernel, "_XpbdPairs", xpbdPairs);
                compute.SetBuffer(kernel, "_XpbdPairCount", xpbdPairCount);
            }
            foreach (var kernel in new[] { clearXpbdPairsKernel, finalizeXpbdPairDispatchKernel })
            {
                compute.SetBuffer(kernel, "_XpbdPairDispatchArgs", xpbdPairDispatchArgs);
            }
            foreach (var kernel in new[] { clearXpbdCorrectionsKernel, solveXpbdPairsKernel,
                         solveXpbdWallsKernel, applyXpbdCorrectionsKernel })
            {
                compute.SetBuffer(kernel, "_XpbdCorrectionX", xpbdCorrectionX);
                compute.SetBuffer(kernel, "_XpbdCorrectionY", xpbdCorrectionY);
                compute.SetBuffer(kernel, "_XpbdCorrectionCounts", xpbdCorrectionCounts);
                compute.SetBuffer(kernel, "_XpbdWallLambdas", xpbdWallLambdas);
            }
            compute.SetBuffer(clearXpbdCorrectionsKernel, "_XpbdDeepContactMarked", xpbdDeepContactMarked);
            foreach (var kernel in new[] { clearXpbdGridKernel, scatterXpbdGridKernel,
                         measureXpbdGridDiagnosticsKernel })
            {
                compute.SetBuffer(kernel, "_XpbdGridFailures", xpbdGridFailures);
            }
            compute.SetBuffer(buildXpbdPairsKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(buildXpbdPairsKernel, "_DynamicTargetsRead", dynamicTargets);
            compute.SetBuffer(buildXpbdPairsKernel, "_DynamicTargetActiveIndices", dynamicTargetActiveIndices);
            compute.SetBuffer(buildXpbdPairsKernel, "_DynamicTargetActiveCount", dynamicTargetActiveCount);
            compute.SetBuffer(solveXpbdPairsKernel, "_StatesRead", readStates);
            compute.SetBuffer(solveXpbdPairsKernel, "_DynamicTargetsRead", dynamicTargets);
            compute.SetBuffer(solveXpbdWallsKernel, "_StatesRead", readStates);
            compute.SetBuffer(applyXpbdCorrectionsKernel, "_StatesRead", readStates);
            compute.SetBuffer(applyXpbdCorrectionsKernel, "_StatesWrite", writeStates);
            foreach (var kernel in new[] { solveXpbdWallsKernel, applyXpbdCorrectionsKernel })
            {
                compute.SetBuffer(kernel, "_FlowData", flowData);
                compute.SetBuffer(kernel, "_FlowVectors", flowVectors);
            }
            compute.SetBuffer(measureXpbdGridDiagnosticsKernel, "_XpbdCellCounts", xpbdCellCounts);
            compute.SetBuffer(measureXpbdGridDiagnosticsKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(measureXpbdPairDiagnosticsKernel, "_XpbdPairs", xpbdPairs);
            compute.SetBuffer(measureXpbdPairDiagnosticsKernel, "_XpbdPairCount", xpbdPairCount);
            compute.SetBuffer(measureXpbdPairDiagnosticsKernel, "_StatesRead", readStates);
            compute.SetBuffer(measureXpbdPairDiagnosticsKernel, "_DynamicTargetsRead", dynamicTargets);
            compute.SetBuffer(measureXpbdPairDiagnosticsKernel, "_XpbdDeepContactMarked", xpbdDeepContactMarked);
            compute.SetBuffer(measureXpbdPairDiagnosticsKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(measureXpbdBodyDiagnosticsKernel, "_ActiveIndicesRead", activeIndices);
            compute.SetBuffer(measureXpbdBodyDiagnosticsKernel, "_StatesRead", readStates);
            compute.SetBuffer(measureXpbdBodyDiagnosticsKernel, "_FlowData", flowData);
            compute.SetBuffer(measureXpbdBodyDiagnosticsKernel, "_FlowVectors", flowVectors);
            compute.SetBuffer(measureXpbdBodyDiagnosticsKernel, "_XpbdDeepContactMarked", xpbdDeepContactMarked);
            compute.SetBuffer(measureXpbdBodyDiagnosticsKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(finalizeXpbdPersistentContactsKernel, "_ActiveIndicesRead", activeIndices);
            compute.SetBuffer(finalizeXpbdPersistentContactsKernel, "_XpbdDeepContactMarked", xpbdDeepContactMarked);
            compute.SetBuffer(finalizeXpbdPersistentContactsKernel, "_XpbdDeepContactTicks", xpbdDeepContactTicks);
            compute.SetBuffer(solveXpbdPairsKernel, "_XpbdDeepContactTicks", xpbdDeepContactTicks);
            compute.SetBuffer(finalizeXpbdPersistentContactsKernel, "_Diagnostics", diagnostics);
        }

        public static bool TryCreate(int capacity, HordeFlowField field, Mesh mesh, out GpuHordeSimulation simulation)
        {
            simulation = null;
            if (!SystemInfo.supportsComputeShaders || capacity <= 0 || field == null || mesh == null)
            {
                return false;
            }

            try
            {
                simulation = new GpuHordeSimulation(capacity, field, mesh);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"GPU-authoritative horde backend unavailable. The wave cannot run. {exception.Message}");
                simulation?.Dispose();
                simulation = null;
                return false;
            }
        }

        public void Spawn(int index, Vector3 position, Vector3 velocity, float scale, float tint, float health, float progress, bool isFlying, float mass, EnemyDefinition definition)
        {
            if (disposed || index < 0 || index >= capacity)
            {
                return;
            }

            var state = CreateAgentState(index, position, velocity, scale, tint, health, progress, isFlying, mass, definition);
            singleStateUpload[0] = state;
            singleControlUpload[0] = new Vector4(Mathf.Max(0.25f, velocity.magnitude), 1f, 0f, 0f);
            UploadSpawns(singleStateUpload, singleControlUpload, 0, index, 1);
        }

        internal static AgentState CreateAgentState(
            int index,
            Vector3 position,
            Vector3 velocity,
            float scale,
            float tint,
            float health,
            float progress,
            bool isFlying,
            float mass,
            EnemyDefinition definition)
        {
            return new AgentState
            {
                Position = new Vector2(position.x, position.z),
                StepStartPosition = new Vector2(position.x, position.z),
                Facing = new Vector2(velocity.x, velocity.z).normalized,
                LocomotionSpeed = velocity.magnitude,
                Velocity = new Vector2(velocity.x, velocity.z),
                Scale = scale,
                Tint = tint,
                Status = 1,
                Health = health,
                Progress = progress,
                Flags = isFlying ? 1u : 0u,
                SlowMultiplier = 1f,
                Mass = Mathf.Max(0.1f, mass),
                MaxHealth = Mathf.Max(1f, health),
                Armor = Mathf.Max(0f, definition != null ? definition.armor : 0f),
                PhysicalResistance = Mathf.Clamp(definition != null ? definition.physicalResistance : 0f, 0f, 0.95f),
                FireResistance = Mathf.Clamp(definition != null ? definition.fireResistance : 0f, 0f, 0.95f),
                SlowResistance = Mathf.Clamp(definition != null ? definition.slowResistance : 0f, 0f, 0.95f),
                AttackDamage = Mathf.Max(0f, definition != null ? definition.attackDamage : 0f),
                AttackInterval = Mathf.Max(0.15f, definition != null ? definition.attackInterval : 1f),
                WallDamageMultiplier = Mathf.Max(0f, definition != null ? definition.wallDamageMultiplier : 1f),
                AlliedDamageMultiplier = Mathf.Max(0f, definition != null ? definition.alliedDamageMultiplier : 1f),
                CombatFlags = definition != null && definition.drainsAllies ? 2u : 0u,
                DefinitionIndex = (uint)index
            };
        }

        internal void SpawnBatch(AgentState[] states, int count)
        {
            SpawnBatch(states, null, 0, count);
        }

        internal void SpawnBatch(AgentState[] states, Vector4[] controlData, int startIndex, int count)
        {
            if (disposed || states == null)
            {
                return;
            }

            startIndex = Mathf.Clamp(startIndex, 0, capacity);
            if (startIndex >= states.Length)
            {
                return;
            }
            count = Mathf.Clamp(count, 0, Mathf.Min(capacity - startIndex, states.Length - startIndex));
            if (count <= 0)
            {
                return;
            }

            UploadSpawns(states, controlData, startIndex, startIndex, count);
        }

        private void UploadSpawns(AgentState[] states, Vector4[] controlData, int source, int destination, int count)
        {
            // Upload to read-only staging resources. CPU writes to the live UAVs
            // force the driver to synchronize with movement/contact/draw work.
            spawnStates.SetData(states, source, 0, count);
            var hasControls = controlData != null && controlData.Length >= source + count;
            if (hasControls)
                for (var i = source; i < source + count; i++)
                    maximumDesiredSpeed = Mathf.Max(maximumDesiredSpeed, controlData[i].x);
            if (hasControls) spawnControls.SetData(controlData, source, 0, count);
            compute.SetInt("_SpawnStart", destination);
            compute.SetInt("_SpawnCount", count);
            compute.SetInt("_SpawnHasControls", hasControls ? 1 : 0);
            compute.SetBuffer(spawnKernel, "_SpawnStates", spawnStates);
            compute.SetBuffer(spawnKernel, "_SpawnControls", spawnControls);
            compute.SetBuffer(spawnKernel, "_StatesRead", stateA);
            compute.SetBuffer(spawnKernel, "_StatesWrite", stateB);
            compute.SetBuffer(spawnKernel, "_SpawnControlsWrite", controls);
            compute.Dispatch(spawnKernel, DivideRoundUp(count, ThreadGroupSize), 1, 1);
            activeHighWaterMark = Mathf.Max(activeHighWaterMark, destination + count);
        }

        internal void ReadStatesSynchronous(AgentState[] destination, int count)
        {
            if (disposed || destination == null)
            {
                return;
            }

            count = Mathf.Clamp(count, 0, Mathf.Min(capacity, destination.Length));
            readStates.GetData(destination, 0, 0, count);
        }

        internal void SynchronizeDiagnostics(uint[] destination)
        {
            if (!disposed && destination != null && destination.Length >= 4)
            {
                diagnostics.GetData(destination, 0, 0, Mathf.Min(DiagnosticsCount, destination.Length));
            }
        }

        internal void ReadDrawArgsSynchronous(uint[] destination)
        {
            if (!disposed && destination != null && destination.Length >= 5)
            {
                indirectArgs.GetData(destination, 0, 0, 5);
            }
        }

        internal void ReadLodDrawArgsSynchronous(uint[] destination)
        {
            if (!disposed && destination != null && destination.Length >= 5)
            {
                lodIndirectArgs.GetData(destination, 0, 0, 5);
            }
        }

        public void SynchronizeDynamicTargets(IReadOnlyList<ICombatTarget> targets)
        {
            if (disposed)
            {
                return;
            }

            seenDynamicTargets.Clear();
            if (targets != null)
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (target == null || !target.IsAlive || !seenDynamicTargets.Add(target))
                    {
                        continue;
                    }

                    if (!dynamicTargetSlots.TryGetValue(target, out var slot))
                    {
                        if (freeDynamicTargetSlots.Count == 0)
                        {
                            Debug.LogWarning("GPU dynamic target capacity reached; the newest blocker was not registered.");
                            continue;
                        }

                        slot = freeDynamicTargetSlots.Dequeue();
                        var generation = ++dynamicTargetGenerations[slot];
                        if (generation == 0u)
                        {
                            generation = ++dynamicTargetGenerations[slot];
                        }
                        dynamicTargetSlots.Add(target, slot);
                        dynamicTargetOwners[slot] = target;
                        QueueDynamicTargetCommand(target, slot, generation, 1u);
                    }
                    else
                    {
                        QueueDynamicTargetCommand(target, slot, dynamicTargetGenerations[slot], 2u);
                    }
                }
            }

            staleDynamicTargets.Clear();
            foreach (var pair in dynamicTargetSlots)
            {
                if (!seenDynamicTargets.Contains(pair.Key) || pair.Key == null || !pair.Key.IsAlive)
                {
                    staleDynamicTargets.Add(pair.Key);
                }
            }

            for (var i = 0; i < staleDynamicTargets.Count; i++)
            {
                var target = staleDynamicTargets[i];
                if (!dynamicTargetSlots.TryGetValue(target, out var slot))
                {
                    continue;
                }
                QueueDynamicTargetCommand(target, slot, dynamicTargetGenerations[slot], 3u);
                dynamicTargetSlots.Remove(target);
                dynamicTargetOwners[slot] = null;
                freeDynamicTargetSlots.Enqueue(slot);
            }
        }

        public bool TryGetDynamicTarget(int slot, uint generation, out ICombatTarget target)
        {
            target = null;
            if (slot < 0 || slot >= MaxDynamicTargets || dynamicTargetGenerations[slot] != generation)
            {
                return false;
            }

            target = dynamicTargetOwners[slot];
            return target != null;
        }

        private void QueueDynamicTargetCommand(ICombatTarget target, int slot, uint generation, uint operation)
        {
            if (dynamicTargetCommandCount >= dynamicTargetCommandUpload.Length)
            {
                return;
            }

            var position = target != null ? target.Position : Vector3.zero;
            var radius = target != null ? Mathf.Max(0.05f, target.CombatRadius) : 0.05f;
            var axis = Vector2.zero;
            var segmentHalfLength = 0f;
            var halfDepth = radius;
            if (target is IOrientedCombatTarget oriented && oriented.CombatHalfLength > 0f)
            {
                var worldAxis = oriented.CombatAxis;
                axis = new Vector2(worldAxis.x, worldAxis.z);
                if (axis.sqrMagnitude > 0.0001f)
                {
                    axis.Normalize();
                    halfDepth = Mathf.Max(0.05f, oriented.CombatHalfDepth);
                    segmentHalfLength = Mathf.Max(0f, oriented.CombatHalfLength - halfDepth);
                    radius = halfDepth;
                }
            }
            dynamicTargetCommandUpload[dynamicTargetCommandCount++] = new DynamicTargetCommand
            {
                Slot = (uint)slot,
                Generation = generation,
                Operation = operation,
                Kind = target != null ? (uint)target.TargetKind : 0u,
                Position = new Vector2(position.x, position.z),
                Health = target != null ? Mathf.Max(0f, target.CurrentHealth) : 0f,
                MaxHealth = target != null ? Mathf.Max(1f, target.MaximumHealth) : 1f,
                Radius = radius,
                Axis = axis,
                SegmentHalfLength = segmentHalfLength,
                HalfDepth = halfDepth,
                BlockCapacity = target != null ? Mathf.Max(0f, target.BlockCapacity) : 0f,
                Armor = target != null ? Mathf.Max(0f, target.Armor) : 0f,
                PhysicalResistance = target != null ? Mathf.Clamp(target.PhysicalResistance, 0f, 0.95f) : 0f,
                FireResistance = target != null ? Mathf.Clamp(target.FireResistance, 0f, 0.95f) : 0f,
                SlowResistance = target != null ? Mathf.Clamp(target.SlowResistance, 0f, 0.95f) : 0f,
                ThornsDamage = target != null ? Mathf.Max(0f, target.ThornsDamage) : 0f
            };
        }

        public void Dispatch(
            float deltaTime,
            Vector4[] controlData,
            Vector2[] impulseData,
            bool requestReadback = true,
            float contactIterationScale = 1f,
            bool updatePresentation = true,
            bool updateVisibility = true)
        {
            if (disposed)
            {
                return;
            }

            simulationSeconds += Mathf.Max(0f, deltaTime);

            var uploadCount = Mathf.Min(activeHighWaterMark, capacity);
            if (controlData != null && uploadCount > 0)
            {
                controls.SetData(controlData, 0, 0, Mathf.Min(uploadCount, controlData.Length));
                for (var i = 0; i < Mathf.Min(uploadCount, controlData.Length); i++)
                    maximumDesiredSpeed = Mathf.Max(maximumDesiredSpeed, controlData[i].x);
            }

            if (impulseData != null && uploadCount > 0)
            {
                impulses.SetData(impulseData, 0, 0, Mathf.Min(uploadCount, impulseData.Length));
            }
            compute.SetInt("_AgentCount", uploadCount);
            compute.SetFloat("_DeltaTime", Mathf.Min(deltaTime, 0.1f));
            maximumMotionDistance = Mathf.Max(collisionDiameter * 0.4f,
                maximumDesiredSpeed * movementSpeedMultiplier * Mathf.Min(deltaTime, 0.1f) * 1.12f);
            lastSimulationDelta = Mathf.Min(deltaTime, 0.1f);
            compute.SetFloat("_MovementSpeedMultiplier", movementSpeedMultiplier);
            compute.SetFloat("_ShockwavePropagationSpeed", ShockwavePropagationSpeed);
            SetCameraParameters();
            compute.SetBuffer(gridKernel, "_StatesRead", readStates);
            compute.SetBuffer(gridKernel, "_StatesWrite", writeStates);
            DispatchDynamicTargetCommands();
            compute.Dispatch(clearDynamicTargetGridKernel,
                DivideRoundUp(Mathf.Max(cellCount, MaxDynamicTargets), ThreadGroupSize), 1, 1);
            compute.Dispatch(buildDynamicTargetGridKernel, DivideRoundUp(MaxDynamicTargets, ThreadGroupSize), 1, 1);
            DispatchDamageCommands();
            DispatchStatusCommands();
            compute.SetBuffer(simulateKernel, "_StatesInput", readStates);
            compute.SetBuffer(simulateKernel, "_StatesRead", readStates);
            compute.SetBuffer(simulateKernel, "_StatesWrite", writeStates);
            compute.Dispatch(clearKernel, DivideRoundUp(cellCount, ThreadGroupSize), 1, 1);
            compute.Dispatch(clearDiagnosticsKernel, 1, 1, 1);
            compute.Dispatch(gridKernel, Mathf.Max(1, DivideRoundUp(uploadCount, ThreadGroupSize)), 1, 1);
            compute.Dispatch(finalizeActiveKernel, 1, 1, 1);
            DispatchProjectiles();
            AdvanceShockwaves(Mathf.Min(deltaTime, 0.1f));
            DispatchAreaEffects();
            DispatchTargetQueries();
            // Queries observe the pre-movement state. Bound the cached answer by
            // every fixed-tick displacement before CPU consumption.
            motionTravelBudget += maximumMotionDistance;
            compute.DispatchIndirect(simulateKernel, activeDispatchArgs);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_StatesInput", writeStates);
            compute.Dispatch(resolveDynamicTargetDamageKernel, DivideRoundUp(MaxDynamicTargets, ThreadGroupSize), 1, 1);
            (readStates, writeStates) = (writeStates, readStates);
            ResolveXpbdContacts(
                uploadCount,
                CrowdSolverProfile.PositionIterations,
                measureOverlaps: true);
            if (updatePresentation)
            {
                if (updateVisibility)
                {
                    RefreshVisibility();
                }
                if (requestReadback)
                {
                    RequestDiagnosticsReadback();
                    RequestEventReadback();
                }
            }
        }

        /// <summary>
        /// Parallel XPBD contact solve. The broad phase uses a compact prefix-sum
        /// grid, so a dense cell cannot silently exclude bodies as the legacy
        /// fixed-capacity grid did. Constraints are generated once per fixed tick;
        /// each Jacobi iteration accumulates integer micro-metre corrections before
        /// applying them, making render-frame batching irrelevant to the result.
        /// </summary>
        private void ResolveXpbdContacts(int uploadCount, int iterationCount, bool measureOverlaps)
        {
            BuildXpbdGrid(uploadCount);
            BindXpbdStateBuffers();
            compute.Dispatch(clearXpbdPairsKernel, 1, 1, 1);
            compute.Dispatch(buildXpbdPairsKernel, Mathf.Max(1, DivideRoundUp(uploadCount, ThreadGroupSize)), 1, 1);
            compute.Dispatch(finalizeXpbdPairDispatchKernel, 1, 1, 1);

            LastContactIterationCount = iterationCount;
            for (var iteration = 0; iteration < iterationCount; iteration++)
            {
                // Each Jacobi pass must read the resolved state from the
                // preceding pass. Rebind before the solve rather than before
                // the swap; otherwise every other pass would read stale data.
                BindXpbdStateBuffers();
                compute.SetInt("_XpbdResetLambdas", iteration == 0 ? 1 : 0);
                compute.SetInt("_XpbdFinalizePresentation", iteration == iterationCount - 1 ? 1 : 0);
                compute.DispatchIndirect(clearXpbdCorrectionsKernel, activeDispatchArgs);
                compute.DispatchIndirect(solveXpbdPairsKernel, xpbdPairDispatchArgs);
                compute.DispatchIndirect(solveXpbdWallsKernel, activeDispatchArgs);
                compute.DispatchIndirect(applyXpbdCorrectionsKernel, activeDispatchArgs);
                (readStates, writeStates) = (writeStates, readStates);
            }

            if (measureOverlaps)
            {
                // Rebuild the capped gameplay-query/render grid, but measure
                // solver correctness from the uncapped compact XPBD buffers.
                RebuildContactGrid(uploadCount);
                BindXpbdStateBuffers();
                compute.Dispatch(measureXpbdGridDiagnosticsKernel,
                    Mathf.Max(1, DivideRoundUp(cellCount, ThreadGroupSize)), 1, 1);
                compute.DispatchIndirect(measureXpbdPairDiagnosticsKernel, xpbdPairDispatchArgs);
                compute.SetBuffer(measureXpbdBodyDiagnosticsKernel, "_StatesRead", readStates);
                compute.DispatchIndirect(measureXpbdBodyDiagnosticsKernel, activeDispatchArgs);
                compute.DispatchIndirect(finalizeXpbdPersistentContactsKernel, activeDispatchArgs);
            }
        }

        private void BuildXpbdGrid(int uploadCount)
        {
            BindXpbdStateBuffers();
            compute.Dispatch(clearXpbdGridKernel, Mathf.Max(1, DivideRoundUp(cellCount, ThreadGroupSize)), 1, 1);
            compute.Dispatch(countXpbdGridKernel, Mathf.Max(1, DivideRoundUp(uploadCount, ThreadGroupSize)), 1, 1);
            compute.Dispatch(scanXpbdGridBlocksKernel, xpbdBlockCount, 1, 1);
            compute.Dispatch(scanXpbdGridBlockTotalsKernel, 1, 1, 1);
            compute.Dispatch(addXpbdGridBlockOffsetsKernel, xpbdBlockCount, 1, 1);
            compute.Dispatch(finalizeXpbdGridKernel, 1, 1, 1);
            compute.Dispatch(scatterXpbdGridKernel, Mathf.Max(1, DivideRoundUp(uploadCount, ThreadGroupSize)), 1, 1);
        }

        private void BindXpbdStateBuffers()
        {
            foreach (var kernel in new[] { countXpbdGridKernel, scatterXpbdGridKernel, buildXpbdPairsKernel,
                         solveXpbdPairsKernel, solveXpbdWallsKernel, applyXpbdCorrectionsKernel })
            {
                compute.SetBuffer(kernel, "_StatesRead", readStates);
            }
            compute.SetBuffer(applyXpbdCorrectionsKernel, "_StatesWrite", writeStates);
            compute.SetBuffer(measureXpbdPairDiagnosticsKernel, "_StatesRead", readStates);
            compute.SetBuffer(measureXpbdPairDiagnosticsKernel, "_DynamicTargetsRead", dynamicTargets);
        }

        private void RebuildContactGrid(int uploadCount)
        {
            compute.SetBuffer(gridKernel, "_StatesRead", readStates);
            compute.SetBuffer(gridKernel, "_StatesWrite", writeStates);
            compute.Dispatch(clearKernel, DivideRoundUp(cellCount, ThreadGroupSize), 1, 1);
            compute.Dispatch(clearDiagnosticsKernel, 1, 1, 1);
            compute.Dispatch(gridKernel, Mathf.Max(1, DivideRoundUp(uploadCount, ThreadGroupSize)), 1, 1);
            compute.Dispatch(finalizeActiveKernel, 1, 1, 1);
        }


        private void DispatchDynamicTargetCommands()
        {
            compute.SetInt("_DynamicTargetCommandCount", dynamicTargetCommandCount);
            if (dynamicTargetCommandCount <= 0)
            {
                return;
            }

            dynamicTargetCommands.SetData(dynamicTargetCommandUpload, 0, 0, dynamicTargetCommandCount);
            compute.Dispatch(applyDynamicTargetCommandsKernel, DivideRoundUp(dynamicTargetCommandCount, ThreadGroupSize), 1, 1);
            dynamicTargetCommandCount = 0;
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
            if (disposed || queuedProjectiles.Count >= MaxProjectileCommands || damage <= 0f)
            {
                return false;
            }

            if (projectileId == 0u)
            {
                projectileId = CreateProjectileId();
                resetHitCount = true;
            }

            queuedProjectiles.Add(new ProjectileCommand
            {
                Start = new Vector2(start.x, start.z),
                End = new Vector2(end.x, end.z),
                Radius = Mathf.Max(splash ? 0.05f : 0.22f, radius),
                Damage = damage,
                Knockback = Mathf.Max(0f, knockback),
                MaxHits = (uint)Mathf.Max(1, maxHits),
                CanHitFlying = canHitFlying ? 1u : 0u,
                Mode = splash ? 1u : 0u,
                BurnDamagePerSecond = Mathf.Max(0f, burnDamagePerSecond),
                BurnDuration = Mathf.Max(0f, burnDuration),
                MaxBurnStacks = (uint)Mathf.Max(1, maxBurnStacks),
                ProjectileId = projectileId,
                ResetHitCount = resetHitCount ? 1u : 0u
            });
            return true;
        }

        public uint CreateProjectileId()
        {
            var id = nextProjectileId++;
            if (nextProjectileId == 0u)
            {
                nextProjectileId = 1u;
            }
            return id;
        }

        private void DispatchProjectiles()
        {
            var count = queuedProjectiles.Count;
            if (count <= 0)
            {
                compute.SetInt("_ProjectileCommandCount", 0);
                return;
            }

            queuedProjectiles.CopyTo(projectileUpload, 0);
            projectileCommands.SetData(projectileUpload, 0, 0, count);
            compute.SetBuffer(projectileKernel, "_StatesRead", readStates);
            compute.SetInt("_ProjectileCommandCount", count);
            compute.Dispatch(projectileKernel, 1, 1, 1);
            queuedProjectiles.Clear();
        }

        public bool TryGetCachedTarget(Vector3 position, float range, bool canHitFlying, TowerTargetingMode targetingMode, out int index)
        {
            return TryGetCachedTarget(position, range, canHitFlying, targetingMode, out index, out _, out _);
        }

        public bool TryGetSpawnClearance(
            Vector3 position,
            float clearanceRadius,
            int newerThanFrame,
            out bool isClear,
            float requiredSeparation = 0f)
        {
            isClear = false;
            if (disposed)
            {
                return false;
            }

            const bool boundedRoadMotion = false;
            // The answer describes the state before the following predictive tick.
            // Include that bounded travel in the observation disk, while preserving
            // clearanceRadius itself as the actual spawn-to-body separation.
            var queryRange = Mathf.Max(0.05f, clearanceRadius +
                (boundedRoadMotion ? maximumMotionDistance * 1.02f : 0f));
            // A targeting cache's quarter-metre bins are unsafe for occupancy queries.
            var key = new TargetQueryKey(position, queryRange, TowerTargetingMode.Closest, true, -1, true);
            var hasResult = cachedTargets.TryGetValue(key, out var cached);
            var resultAge = hasResult ? Time.unscaledTime - cached.UnscaledTime : float.PositiveInfinity;
            var separation = requiredSeparation > 0 ? requiredSeparation : MaximumPairDiameter;
            var travelSinceQuery = hasResult ? motionTravelBudget - cached.TravelBudget : double.PositiveInfinity;
            var hasFreshResult = hasResult && resultAge <= 0.5f && cached.Frame > newerThanFrame &&
                (!boundedRoadMotion || queryRange - travelSinceQuery >= separation * 1.002f);
            if (!queuedTargetKeys.Contains(key) && pendingTargetQueries.Count < MaxTargetQueries &&
                (!hasFreshResult || resultAge >= 0.05f))
            {
                pendingTargetQueries.Add(new PendingTargetQuery(key, new TargetQuery
                {
                    Position = new Vector2(position.x, position.z),
                    Range = queryRange,
                    Mode = ToGpuTargetingMode(TowerTargetingMode.Closest),
                    CanHitFlying = 1u,
                    ExcludedIndex = -1
                }));
                queuedTargetKeys.Add(key);
            }

            if (!hasFreshResult)
            {
                return false;
            }

            isClear = cached.Index < 0;
            return true;
        }

        public bool TryGetCachedTarget(
            Vector3 position,
            float range,
            bool canHitFlying,
            TowerTargetingMode targetingMode,
            out int index,
            out Vector3 targetPosition,
            out Vector3 targetVelocity,
            int excludedIndex = -1)
        {
            index = -1;
            targetPosition = Vector3.zero;
            targetVelocity = Vector3.zero;
            if (disposed)
            {
                return false;
            }

            var queryRange = float.IsInfinity(range)
                ? gridDiagonal + Vector2.Distance(new Vector2(position.x, position.z), gridCenter)
                : Mathf.Max(0.05f, range);
            var key = new TargetQueryKey(position, queryRange, targetingMode, canHitFlying, excludedIndex);
            var hasFreshResult = cachedTargets.TryGetValue(key, out var cached) && Time.frameCount - cached.Frame <= 8;
            if (!queuedTargetKeys.Contains(key) && pendingTargetQueries.Count < MaxTargetQueries &&
                (!hasFreshResult || Time.frameCount - cached.Frame >= 2))
            {
                pendingTargetQueries.Add(new PendingTargetQuery(key, new TargetQuery
                {
                    Position = new Vector2(position.x, position.z),
                    Range = queryRange,
                    Mode = ToGpuTargetingMode(targetingMode),
                    CanHitFlying = canHitFlying ? 1u : 0u,
                    ExcludedIndex = excludedIndex
                }));
                queuedTargetKeys.Add(key);
            }

            if (!hasFreshResult)
            {
                return false;
            }

            index = cached.Index;
            targetPosition = new Vector3(cached.Position.x, 0f, cached.Position.y);
            targetVelocity = new Vector3(cached.Velocity.x, 0f, cached.Velocity.y);
            return index >= 0;
        }

        public void InvalidateTargetCache()
        {
            cachedTargets.Clear();
        }

        public bool TryGetCachedSecondaryTarget(
            Vector3 position,
            float range,
            bool canHitFlying,
            TowerTargetingMode targetingMode,
            out int index,
            out Vector3 targetPosition,
            out Vector3 targetVelocity)
        {
            index = -1;
            targetPosition = Vector3.zero;
            targetVelocity = Vector3.zero;
            var queryRange = float.IsInfinity(range)
                ? gridDiagonal + Vector2.Distance(new Vector2(position.x, position.z), gridCenter)
                : Mathf.Max(0.05f, range);
            var key = new TargetQueryKey(position, queryRange, targetingMode, canHitFlying, -1);
            if (!cachedTargets.TryGetValue(key, out var cached) || Time.frameCount - cached.Frame > 8 || cached.SecondIndex < 0)
            {
                return false;
            }

            index = cached.SecondIndex;
            targetPosition = new Vector3(cached.SecondPosition.x, 0f, cached.SecondPosition.y);
            targetVelocity = new Vector3(cached.SecondVelocity.x, 0f, cached.SecondVelocity.y);
            return true;
        }

        public void QueueDamage(int index, float damage)
        {
            if (disposed || index < 0 || index >= capacity || damage <= 0f)
            {
                return;
            }

            queuedDamage[index] = queuedDamage.TryGetValue(index, out var existing) ? existing + damage : damage;
        }

        public void QueueStatus(int index, float slowMultiplier, float slowDuration, float burnDamagePerSecond, float burnDuration, int maxBurnStacks)
        {
            if (disposed || index < 0 || index >= capacity)
            {
                return;
            }

            queuedStatus.TryGetValue(index, out var command);
            command.Index = (uint)index;
            if (slowDuration > 0f)
            {
                command.SlowMultiplier = command.SlowDuration > 0f
                    ? Mathf.Min(command.SlowMultiplier, Mathf.Clamp(slowMultiplier, 0.05f, 1f))
                    : Mathf.Clamp(slowMultiplier, 0.05f, 1f);
                command.SlowDuration = Mathf.Max(command.SlowDuration, slowDuration);
            }

            if (burnDamagePerSecond > 0f && burnDuration > 0f)
            {
                command.BurnDamagePerSecond = burnDamagePerSecond;
                command.BurnDuration = Mathf.Max(command.BurnDuration, burnDuration);
                command.MaxBurnStacks = (uint)Mathf.Max(1, maxBurnStacks);
                command.BurnApplications++;
            }

            queuedStatus[index] = command;
        }

        public bool QueueAreaEffect(
            Vector3 center,
            float radius,
            float damage,
            float knockback,
            int maxTargets,
            float burnDamagePerSecond,
            float burnDuration,
            int maxBurnStacks,
            float slowMultiplier = 1f,
            float slowDuration = 0f,
            float slowCapacity = 0f,
            float knockbackPropagationRadius = 0f)
        {
            if (disposed || radius <= 0f || queuedAreaEffects.Count >= MaxAreaCommands)
            {
                return false;
            }

            var propagationRadius = Mathf.Max(0f, knockbackPropagationRadius);
            var command = new AreaEffectCommand
            {
                Center = new Vector2(center.x, center.z),
                Radius = radius,
                Damage = Mathf.Max(0f, damage),
                Knockback = Mathf.Max(0f, knockback),
                MaxTargets = (uint)Mathf.Max(0, maxTargets),
                BurnDamagePerSecond = Mathf.Max(0f, burnDamagePerSecond),
                BurnDuration = Mathf.Max(0f, burnDuration),
                MaxBurnStacks = (uint)Mathf.Max(1, maxBurnStacks),
                SlowMultiplier = Mathf.Clamp(slowMultiplier, 0.05f, 1f),
                SlowDuration = Mathf.Max(0f, slowDuration),
                SlowCapacity = Mathf.Max(0f, slowCapacity),
                InnerRadius = 0f
            };
            if (propagationRadius > 0f && knockback > 0f && maxTargets == 0)
            {
                var shockwaveKnockback = command.Knockback;
                // The core is a damage-only zone. Surviving enemies outside it are
                // reached by the expanding knockback ring on subsequent frames.
                command.Knockback = 0f;
                command.InnerRadius = -1f;
                pendingShockwaves.Add(new PendingShockwave
                {
                    Center = command.Center,
                    BaseRadius = radius,
                    CurrentRadius = radius,
                    MaximumRadius = radius + propagationRadius,
                    Knockback = shockwaveKnockback
                });
            }

            queuedAreaEffects.Add(command);
            return true;
        }

        public bool TryDequeueEvent(out int index, out uint type)
        {
            return TryDequeueEvent(out index, out type, out _, out _, out _);
        }

        public bool TryDequeueEvent(out int index, out uint type, out float value, out uint generation, out int sourceIndex)
        {
            if (receivedEvents.Count == 0)
            {
                index = -1;
                type = 0;
                value = 0f;
                generation = 0u;
                sourceIndex = -1;
                return false;
            }

            var hordeEvent = receivedEvents.Dequeue();
            index = (int)hordeEvent.Index;
            type = hordeEvent.Type;
            value = hordeEvent.Value;
            generation = hordeEvent.Generation;
            sourceIndex = hordeEvent.SourceIndex;
            return true;
        }

        internal void RefreshVisibilityForAudit()
        {
            RefreshVisibility();
        }

        // The player camera pans and zooms in LateUpdate.  Runtime rendering must
        // cull after that movement, otherwise the GPU visibility list describes
        // the previous view and bodies on a distant edge can blink out.
        internal void RefreshVisibilityForRendering()
        {
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            SetCameraParameters();
            compute.SetBuffer(cullVisibleKernel, "_StatesInput", readStates);
            compute.Dispatch(clearVisibilityKernel, 1, 1, 1);
            compute.DispatchIndirect(cullVisibleKernel, activeDispatchArgs);
        }

        public void SetPresentationInterpolation(float alpha)
        {
            presentationAlpha = Mathf.Clamp01(alpha);
        }

        public void Draw(Mesh mesh, int layer)
        {
            if (disposed || stableRenderMesh == null || material == null)
            {
                return;
            }

            material.SetFloat("_PresentationAlpha", presentationAlpha);
            material.SetFloat("_PresentationLag", (1f - presentationAlpha) * lastSimulationDelta);
            properties.SetBuffer("_AgentStates", readStates);
            properties.SetBuffer("_VisibleIndices", visibleIndices);
            lodProperties.SetBuffer("_AgentStates", readStates);
            lodProperties.SetBuffer("_VisibleIndices", visibleIndices);
            variantProperties.SetBuffer("_AgentStates", readStates);
            variantProperties.SetBuffer("_VisibleIndices", visibleIndices);
            variantLodProperties.SetBuffer("_AgentStates", readStates);
            variantLodProperties.SetBuffer("_VisibleIndices", visibleIndices);
            Graphics.DrawMeshInstancedIndirect(
                stableRenderMesh,
                0,
                material,
                drawBounds,
                indirectArgs,
                0,
                properties,
                ShadowCastingMode.Off,
                false,
                layer);
            Graphics.DrawMeshInstancedIndirect(
                lowDetailMesh,
                0,
                material,
                drawBounds,
                lodIndirectArgs,
                0,
                lodProperties,
                ShadowCastingMode.Off,
                false,
                layer);
        }

        public void SetVisualScaleMultiplier(float multiplier)
        {
            multiplier = Mathf.Clamp(multiplier, 0.1f, 2f);
            properties.SetFloat("_VisualScaleMultiplier", multiplier);
            lodProperties.SetFloat("_VisualScaleMultiplier", multiplier * FarLodVisibilityScale);
            variantProperties.SetFloat("_VisualScaleMultiplier", multiplier);
            variantLodProperties.SetFloat("_VisualScaleMultiplier", multiplier);
        }

        public void SetSpeedColorMode(bool enabled)
        {
            material.SetFloat("_UseSpeedColors", enabled ? 1f : 0f);
        }

        public void SetCollisionDiameter(float diameter)
        {
            collisionDiameter = Mathf.Max(0.1f, diameter);
            compute.SetFloat("_CollisionDiameter", collisionDiameter);
        }

        internal void SetXpbdPerBodyRadius(bool enabled)
        {
            xpbdUsesPerBodyRadius = enabled;
            compute.SetInt("_XpbdUsePerBodyRadius", enabled ? 1 : 0);
        }

        internal float GetPhysicalBodyRadius(AgentState state) => xpbdUsesPerBodyRadius
            ? Mathf.Max(collisionDiameter * 0.5f,
                state.Scale * Mathf.Clamp(solverProfile.bodyRadiusPerVisualScale, 0.1f, 1f))
            : collisionDiameter * 0.5f;

        public void SetStationaryArena(
            bool enabled,
            bool randomMovement = false,
            bool centerSeeking = false,
            Vector3 center = default)
        {
            centerSeekingArena = enabled && centerSeeking;
            stationaryArena = enabled;
            compute.SetInt("_StationaryHorde", enabled ? 1 : 0);
            compute.SetInt("_RandomArenaMovement", enabled && randomMovement ? 1 : 0);
            compute.SetInt("_CenterSeekingArena", enabled && centerSeeking ? 1 : 0);
            compute.SetVector("_ArenaCenter", new Vector4(center.x, center.z, 0f, 0f));
            compute.SetInt("_ExitEnabled", enabled ? 0 : 1);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            // Domain reload can otherwise release a GraphicsBuffer while one of
            // its callbacks is still copying native data into managed memory.
            AsyncGPUReadback.WaitAllRequests();
            stateA?.Dispose();
            stateB?.Dispose();
            spawnStates?.Dispose();
            spawnControls?.Dispose();
            spatialStates?.Dispose();
            controls?.Dispose();
            impulses?.Dispose();
            flowVectors?.Dispose();
            flowData?.Dispose();
            cellCounts?.Dispose();
            cellAgents?.Dispose();
            indirectArgs?.Dispose();
            diagnostics?.Dispose();
            targetQueries?.Dispose();
            targetResults?.Dispose();
            damageCommands?.Dispose();
            statusCommands?.Dispose();
            areaEffectCommands?.Dispose();
            projectileCommands?.Dispose();
            projectileHitCounts?.Dispose();
            agentLastProjectileIds?.Dispose();
            hordeEvents?.Dispose();
            eventCount?.Dispose();
            dynamicTargets?.Dispose();
            dynamicTargetCommands?.Dispose();
            dynamicTargetCellCounts?.Dispose();
            dynamicTargetCellIndices?.Dispose();
            dynamicTargetActiveIndices?.Dispose();
            dynamicTargetActiveCount?.Dispose();
            dynamicTargetBlockedMass?.Dispose();
            dynamicTargetDamage?.Dispose();
            dynamicTargetLastAttacker?.Dispose();
            visibleIndices?.Dispose();
            lodIndirectArgs?.Dispose();
            variantIndirectArgs?.Dispose();
            variantLodIndirectArgs?.Dispose();
            activeIndices?.Dispose();
            xpbdCellCounts?.Dispose();
            xpbdCellOffsets?.Dispose();
            xpbdCellCursors?.Dispose();
            xpbdCellAgents?.Dispose();
            xpbdBlockSums?.Dispose();
            xpbdBlockOffsets?.Dispose();
            xpbdPairs?.Dispose();
            xpbdPairCount?.Dispose();
            xpbdPairDispatchArgs?.Dispose();
            xpbdCorrectionX?.Dispose();
            xpbdCorrectionY?.Dispose();
            xpbdCorrectionCounts?.Dispose();
            xpbdWallLambdas?.Dispose();
            xpbdGridFailures?.Dispose();
            xpbdDeepContactMarked?.Dispose();
            xpbdDeepContactTicks?.Dispose();
            activeDispatchArgs?.Dispose();
            if (compute != null)
            {
                UnityEngine.Object.Destroy(compute);
            }

            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }
        }

        private void RequestDiagnosticsReadback()
        {
            if (Time.unscaledTime < nextDiagnosticsReadbackTime)
            {
                return;
            }

            nextDiagnosticsReadbackTime = Time.unscaledTime + ReadbackInterval;

            if (!diagnosticsReadbackPending)
            {
                diagnosticsReadbackPending = true;
                var sampleSimulationSeconds = simulationSeconds;
                AsyncGPUReadback.Request(diagnostics, request =>
                {
                    diagnosticsReadbackPending = false;
                    if (disposed || request.hasError)
                    {
                        return;
                    }

                    var data = request.GetData<uint>();
                    if (data.Length >= 4)
                    {
                        OverflowCellCount = data[0];
                        DroppedAgentCount = data[1];
                        MaximumCellOccupancy = data[2];
                        GpuActiveCount = data[3];
                        VisibleAgentCount = data.Length >= 5 ? data[4] : GpuActiveCount;
                        OverlapPairCount = data.Length >= 6 ? data[5] : 0u;
                        MaximumOverlapFraction = data.Length >= 7 ? data[6] / 1000000f : 0f;
                        OrdinaryBackwardStepCount = data.Length >= 8 ? data[7] : 0u;
                        MaximumOrdinaryBacktrack = data.Length >= 9 ? data[8] / 1000000f : 0f;
                        NormalizedNeighborSpacing = data.Length >= 11 && data[10] > 0u
                            ? data[9] / (data[10] * 1000f)
                            : 0f;
                        ActiveContactPairCount = data.Length >= 12 ? data[11] : 0u;
                        ShallowContactPairCount = data.Length >= 13 ? data[12] : 0u;
                        DeepOverlapPairCount = data.Length >= 14 ? data[13] : 0u;
                        GridCapacityFailureCount = data.Length >= 15 ? data[14] : 0u;
                        PersistentDeepContactBodyCount = data.Length >= 16 ? data[15] : 0u;
                        DeepDiskContactPairCount = data.Length >= 17 ? data[16] : 0u;
                        DeepBlockerContactCount = data.Length >= 18 ? data[17] : 0u;
                        DeepWallContactCount = data.Length >= 19 ? data[18] : 0u;
                        var elapsedSimulation = Mathf.Max(0f,
                            (float)(sampleSimulationSeconds - lastDiagnosticsSimulationSeconds));
                        DeepContactDurationSeconds = PersistentDeepContactBodyCount > 0u
                            ? DeepContactDurationSeconds + elapsedSimulation
                            : 0f;
                        lastDiagnosticsSimulationSeconds = sampleSimulationSeconds;
                    }
                });
            }

        }

        private void DispatchDamageCommands()
        {
            if (queuedDamage.Count == 0)
            {
                compute.SetInt("_DamageCommandCount", 0);
                return;
            }

            var count = 0;
            foreach (var pair in queuedDamage)
            {
                damageUpload[count++] = new DamageCommand { Index = (uint)pair.Key, Damage = pair.Value };
            }

            queuedDamage.Clear();
            damageCommands.SetData(damageUpload, 0, 0, count);
            compute.SetInt("_DamageCommandCount", count);
            compute.SetBuffer(damageKernel, "_StatesRead", readStates);
            compute.SetBuffer(damageKernel, "_StatesWrite", writeStates);
            compute.Dispatch(damageKernel, DivideRoundUp(count, ThreadGroupSize), 1, 1);
        }

        private void DispatchStatusCommands()
        {
            if (queuedStatus.Count == 0)
            {
                compute.SetInt("_StatusCommandCount", 0);
                return;
            }

            var count = 0;
            foreach (var pair in queuedStatus)
            {
                statusUpload[count++] = pair.Value;
            }

            queuedStatus.Clear();
            statusCommands.SetData(statusUpload, 0, 0, count);
            compute.SetInt("_StatusCommandCount", count);
            compute.SetBuffer(statusKernel, "_StatesRead", readStates);
            compute.Dispatch(statusKernel, DivideRoundUp(count, ThreadGroupSize), 1, 1);
        }

        private void DispatchAreaEffects()
        {
            var count = Mathf.Min(queuedAreaEffects.Count, MaxAreaCommands);
            if (count == 0)
            {
                compute.SetInt("_AreaCommandCount", 0);
                return;
            }

            for (var i = 0; i < count; i++)
            {
                areaEffectUpload[i] = queuedAreaEffects[i];
            }

            queuedAreaEffects.RemoveRange(0, count);
            areaEffectCommands.SetData(areaEffectUpload, 0, 0, count);
            compute.SetBuffer(areaEffectKernel, "_StatesRead", readStates);
            compute.SetBuffer(unlimitedAreaEffectsKernel, "_StatesRead", readStates);
            compute.SetInt("_AreaCommandCount", count);
            compute.Dispatch(areaEffectKernel, 1, 1, 1);
            compute.DispatchIndirect(unlimitedAreaEffectsKernel, activeDispatchArgs);
        }

        private void AdvanceShockwaves(float deltaTime)
        {
            // Level 7 spans 120 world units across. Starting at the 10-unit core,
            // this crosses the remaining 110 units in 5.5 seconds.
            const float catchupMargin = 1.5f;
            for (var i = pendingShockwaves.Count - 1; i >= 0; i--)
            {
                var wave = pendingShockwaves[i];
                var nextRadius = Mathf.Min(
                    wave.MaximumRadius,
                    wave.CurrentRadius + ShockwavePropagationSpeed * deltaTime);
                if (nextRadius > wave.CurrentRadius && queuedAreaEffects.Count < MaxAreaCommands)
                {
                    var progress = Mathf.InverseLerp(wave.BaseRadius, wave.MaximumRadius, nextRadius);
                    queuedAreaEffects.Add(new AreaEffectCommand
                    {
                        Center = wave.Center,
                        Radius = nextRadius,
                        Knockback = wave.Knockback * Mathf.Lerp(0.78f, 0.55f, progress),
                        MaxTargets = 0u,
                        MaxBurnStacks = 1u,
                        SlowMultiplier = 1f,
                        // Fast inward movement can cross a sub-frame-thin ring.
                        // A small trailing overlap guarantees one contact without
                        // changing the visible leading edge or maximum reach.
                        InnerRadius = Mathf.Max(wave.BaseRadius, wave.CurrentRadius - catchupMargin)
                    });
                }

                wave.CurrentRadius = nextRadius;
                if (nextRadius >= wave.MaximumRadius - 0.001f)
                {
                    pendingShockwaves.RemoveAt(i);
                }
                else
                {
                    pendingShockwaves[i] = wave;
                }
            }
        }

        private void RequestEventReadback()
        {
            if (eventCountReadbackPending || eventDataReadbackPending)
            {
                return;
            }

            eventCountReadbackPending = true;
            AsyncGPUReadback.Request(eventCount, request =>
            {
                eventCountReadbackPending = false;
                if (disposed || request.hasError)
                {
                    return;
                }

                var countData = request.GetData<uint>();
                if (countData.Length == 0)
                {
                    return;
                }

                var availableCount = Math.Min(countData[0], (uint)eventCapacity);
                if (availableCount <= processedEventCount)
                {
                    return;
                }

                var newCount = (int)(availableCount - processedEventCount);
                var stride = Marshal.SizeOf<HordeEvent>();
                eventDataReadbackPending = true;
                AsyncGPUReadback.Request(hordeEvents, newCount * stride, (int)processedEventCount * stride, eventRequest =>
                {
                    eventDataReadbackPending = false;
                    if (disposed || eventRequest.hasError)
                    {
                        return;
                    }

                    var events = eventRequest.GetData<HordeEvent>();
                    for (var i = 0; i < events.Length; i++)
                    {
                        receivedEvents.Enqueue(events[i]);
                    }

                    processedEventCount += (uint)events.Length;
                });
            });
        }

        private void DispatchTargetQueries()
        {
            if (targetReadbackPending || pendingTargetQueries.Count == 0)
            {
                return;
            }

            var queryCount = Mathf.Min(MaxTargetQueries, pendingTargetQueries.Count);
            for (var i = 0; i < queryCount; i++)
            {
                targetQueryUpload[i] = pendingTargetQueries[i].Query;
                inFlightTargetKeys[i] = pendingTargetQueries[i].Key;
            }

            pendingTargetQueries.RemoveRange(0, queryCount);
            targetQueries.SetData(targetQueryUpload, 0, 0, queryCount);
            compute.SetInt("_QueryCount", queryCount);
            compute.SetBuffer(targetQueryKernel, "_StatesInput", readStates);
            var queryFrame = Time.frameCount;
            var queryTime = Time.unscaledTime;
            var queryTravelBudget = motionTravelBudget;
            compute.Dispatch(targetQueryKernel, DivideRoundUp(queryCount, ThreadGroupSize), 1, 1);
            targetReadbackPending = true;
            AsyncGPUReadback.Request(targetResults, queryCount * Marshal.SizeOf<TargetResult>(), 0, request =>
            {
                targetReadbackPending = false;
                if (disposed || request.hasError)
                {
                    for (var i = 0; i < queryCount; i++)
                    {
                        queuedTargetKeys.Remove(inFlightTargetKeys[i]);
                    }

                    return;
                }

                var data = request.GetData<TargetResult>();
                var resultCount = Mathf.Min(queryCount, data.Length);
                for (var i = 0; i < resultCount; i++)
                {
                    var key = inFlightTargetKeys[i];
                    cachedTargets[key] = new CachedTarget(
                        data[i].Index,
                        queryFrame,
                        queryTime,
                        data[i].Position,
                        data[i].Velocity,
                        data[i].SecondIndex,
                        data[i].SecondPosition,
                        data[i].SecondVelocity,
                        queryTravelBudget);
                    queuedTargetKeys.Remove(key);
                }
            });
        }

        private static uint ToGpuTargetingMode(TowerTargetingMode mode)
        {
            return mode switch
            {
                TowerTargetingMode.First => 1u,
                TowerTargetingMode.Last => 2u,
                TowerTargetingMode.HighestHealth => 3u,
                _ => 0u
            };
        }

        private void SetCameraParameters()
        {
            var camera = Camera.main;
            compute.SetInt("_FrameIndex", (int)(simulationFrameIndex++ & 0x7fffffffu));
            if (camera == null)
            {
                compute.SetInt("_CullingEnabled", 0);
                compute.SetVector("_LodCameraPosition", Vector4.zero);
                compute.SetMatrix("_ViewProjection", Matrix4x4.identity);
                return;
            }

            var cameraPosition = camera.transform.position;
            var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            compute.SetInt("_CullingEnabled", 1);
            compute.SetVector("_LodCameraPosition", new Vector4(cameraPosition.x, cameraPosition.z, 0f, 0f));
            compute.SetVector("_LodProjection", new Vector4(cameraPosition.y,
                camera.orthographic ? camera.pixelHeight / (2f * camera.orthographicSize) :
                    camera.pixelHeight / (2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f)),
                camera.orthographic ? 1f : 0f, SimplifiedGoblinLodMaxHeightPixels));
            compute.SetMatrix("_ViewProjection", projection * camera.worldToCameraMatrix);
        }

        private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;
    }
}
