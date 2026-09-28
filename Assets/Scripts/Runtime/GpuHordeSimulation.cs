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
        private const int DiagnosticsCount = 12;

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
        private readonly int contactKernel;
        private readonly int contactCellsKernel;
        private readonly int prepareMotionKernel;
        private bool stationaryArena;
        private float maximumDesiredSpeed = 0.25f;
        private float maximumMotionDistance;
        private double motionTravelBudget;
        private float maximumGoblinDiameter;
        private float MaximumPairDiameter => Mathf.Max(collisionDiameter * 1.55f, maximumGoblinDiameter);
        private const int FullMotionProjectionIterations = 24;
        private const int FullMotionReservationSweeps = 16;
        private const int LargeCrowdThreshold = 2500;
        private const int LargeCrowdProjectionIterations = 6;
        private const int LargeCrowdReservationSweeps = 2;
        // The far sprite LOD is a deliberately simple horde token. It should
        // cover the crowd cleanly, not enlarge into a second shadow layer.
        private const float FarLodVisibilityScale = 1.08f;
        // Above this height players can read the authored limb animation. Below it,
        // the compact LOD communicates only collective direction and travel.
        private const float SimplifiedGoblinLodMaxHeightPixels = 96f;
        private readonly int constrainMotionKernel;
        private readonly int commitMotionKernel;
        private readonly int reserveMotionKernel;
        private readonly GraphicsBuffer acceptedMotion;
        private readonly GraphicsBuffer motionNeighbors;
        private readonly GraphicsBuffer motionNeighborCounts;
        private readonly GraphicsBuffer motionObstacles;
        private readonly GraphicsBuffer motionA;
        private readonly GraphicsBuffer motionB;
        private readonly int measureOverlapsKernel;
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
        private readonly GraphicsBuffer dynamicTargetBlockedMass;
        private readonly GraphicsBuffer dynamicTargetDamage;
        private readonly GraphicsBuffer dynamicTargetLastAttacker;
        private readonly GraphicsBuffer visibleIndices;
        private readonly GraphicsBuffer lodIndirectArgs;
        private readonly GraphicsBuffer variantIndirectArgs;
        private readonly GraphicsBuffer variantLodIndirectArgs;
        private readonly GraphicsBuffer activeIndices;
        private readonly GraphicsBuffer activeDispatchArgs;
        private readonly Material material;
        private readonly MaterialPropertyBlock properties = new();
        private readonly MaterialPropertyBlock lodProperties = new();
        private readonly MaterialPropertyBlock variantProperties = new();
        private readonly MaterialPropertyBlock variantLodProperties = new();
        private readonly Mesh lowDetailMesh;
        private readonly Mesh stableRenderMesh;
        private readonly Mesh variantRenderMesh;
        private readonly Mesh variantLodRenderMesh;
        private readonly Mesh hordeSpriteMesh;
        private readonly Texture2D hordeSpriteTexture;
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
        private float shockwaveContactBoostTimer;
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
        private bool referenceCrowd;
        private int referenceContactIterations;

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
        internal int LastContactIterationCount { get; private set; }
        // Test-only observation point. Null in the player; readbacks are supplied by the test.
        internal Action<string> MovementStageObserver { get; set; }
        internal bool ReverseMotionReservation { get; set; }
        internal bool SlidingMotionAcceptance { get; set; } = true;

        public void SetMovementSpeedMultiplier(float multiplier)
        {
            movementSpeedMultiplier = Mathf.Max(0f, multiplier);
        }

        public void SetHordePhysicsProfile(HordePhysicsProfile profile)
        {
            referenceCrowd = profile != null && profile.useReferenceCrowdRules;
            referenceContactIterations = referenceCrowd
                ? Mathf.Clamp(profile.contactIterations, 1, 32)
                : 0;
            var contactDiameterMultiplier = Mathf.Clamp(
                profile != null ? profile.contactDiameterMultiplier : 1f,
                0.55f,
                1.5f);
            compute.SetInt("_ReferenceCrowd", referenceCrowd ? 1 : 0);
            compute.SetFloat("_ReferenceLateralContactResponse", profile != null
                ? Mathf.Clamp01(profile.lateralContactResponse)
                : 1f);
            compute.SetFloat("_ReferenceExternalForceRecovery", profile != null
                ? Mathf.Max(0.01f, profile.externalForceRecovery)
                : 0.8f);
            compute.SetFloat("_MinimumContactDiameterMultiplier", hordeSpriteMesh != null
                ? HordeRaiderSpriteAssets.MinimumContactDiameterMultiplier * contactDiameterMultiplier
                : 0.82f * contactDiameterMultiplier);
            material.SetFloat("_PackingScale", profile != null
                ? Mathf.Clamp(profile.spritePackingScale, 0.9f, 1.35f)
                : 1.18f);

            if (referenceCrowd)
            {
                compute.SetFloat("_FlowAcceleration", Mathf.Max(0.1f, profile.flowAcceleration));
                compute.SetFloat("_CollisionAcceleration", Mathf.Max(0.1f, profile.collisionAcceleration));
                compute.SetFloat("_VelocityDamping", Mathf.Max(0.01f, profile.velocityDamping));
                compute.SetFloat("_FlowAlignment", 1f);
            }
        }

        private GpuHordeSimulation(int agentCapacity, HordeFlowField field, Mesh mesh)
        {
            capacity = agentCapacity;
            eventCapacity = capacity + ExtraCombatEventCapacity;
            cellCount = field.Width * field.Height;
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
            hordeSpriteTexture = Resources.Load<Texture2D>(HordeRaiderSpriteAssets.TextureResourcePath);
            if (hordeSpriteTexture != null)
            {
                // The card is an atlas, not a pixel-art texture. Bilinear sampling
                // keeps the painted outline smooth, while Clamp prevents a frame
                // from sampling an unrelated direction at the texture boundary.
                hordeSpriteTexture.wrapMode = TextureWrapMode.Clamp;
                hordeSpriteTexture.filterMode = FilterMode.Bilinear;
                hordeSpriteMesh = HordeRaiderSpriteAssets.CreateGroundCard();
                material.SetFloat("_HordeSpriteEnabled", 1f);
                material.SetTexture("_HordeSpriteAtlas", hordeSpriteTexture);
                material.SetFloat("_HordeSpriteStride", 5.5f);
                material.SetFloat("_PackingScale", 1.18f);
            }
            else
            {
                // Keep the game runnable if Unity has not yet imported the new art.
                // The next domain reload automatically takes the sprite path.
                hordeSpriteMesh = null;
                material.SetFloat("_HordeSpriteEnabled", 0f);
            }
            lowDetailMesh = hordeSpriteMesh != null ? hordeSpriteMesh : EnemyManager.GetLowEnemyMesh();
            stableRenderMesh = lowDetailMesh;
            // These colours are tuned against the cool road material: each is
            // readable at horde zoom without turning the ground into a second UI.
            material.SetColor("_BaseColor", new Color(0.08f, 0.82f, 0.29f, 1f));
            material.SetColor("_SlowColor", new Color(0.16f, 0.50f, 1f, 1f));
            material.SetColor("_RareColor", new Color(0.82f, 0.18f, 0.96f, 1f));
            material.SetColor("_SpeedColor0", new Color(0.96f, 0.16f, 0.10f, 1f));
            material.SetColor("_SpeedColor1", new Color(1f, 0.45f, 0.05f, 1f));
            material.SetColor("_SpeedColor2", new Color(1f, 0.86f, 0.12f, 1f));
            material.SetColor("_SpeedColor3", new Color(0.08f, 0.82f, 0.29f, 1f));
            material.SetFloat("_UseSpeedColors", 0f);
            properties.SetFloat("_LightingVariation", 1f);
            lodProperties.SetFloat("_LightingVariation", 1f);
            variantProperties.SetFloat("_LightingVariation", 1f);
            variantLodProperties.SetFloat("_LightingVariation", 1f);
            properties.SetFloat("_VisualScaleMultiplier", 1f);
            properties.SetFloat("_HordeSpriteFarLod", 0f);
            // Mipmapped authored art is stable at overview zoom. Do not replace it
            // with a green procedural token: that changed the enemy's colour and
            // made the horde read as terrain instead of individual opponents.
            lodProperties.SetFloat("_HordeSpriteFarLod", 0f);
            variantProperties.SetFloat("_HordeSpriteFarLod", 0f);
            variantLodProperties.SetFloat("_HordeSpriteFarLod", 0f);
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
            contactKernel = compute.FindKernel("ResolveContacts");
            contactCellsKernel = compute.FindKernel("ResolveContactCells");
            prepareMotionKernel = compute.FindKernel("PrepareMotion");
            constrainMotionKernel = compute.FindKernel("ConstrainMotion");
            commitMotionKernel = compute.FindKernel("CommitMotion");
            reserveMotionKernel = compute.FindKernel("ReserveMotionCells");
            acceptedMotion = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 2);
            motionNeighbors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 128, sizeof(float) * 4);
            motionNeighborCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            motionObstacles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 4);
            motionA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 2);
            motionB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 2);
            measureOverlapsKernel = compute.FindKernel("MeasureOverlaps");
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
            compute.SetInt("_CleanupCellStride", 1);
            // Sprite cards include transparent gutter around the painted enemy.
            // Its contact envelope therefore must be smaller than the old blob mesh.
            compute.SetFloat("_BodyRadiusPerScale", hordeSpriteMesh != null
                ? HordeRaiderSpriteAssets.ContactRadiusPerStateScale
                : 1f);
            // Keep the solver's hard minimum in sync with the visible sprite.
            // Without this, spawn packing can be tightened while contacts still
            // resolve back to the old, visibly detached diameter.
            compute.SetFloat("_MinimumContactDiameterMultiplier", hordeSpriteMesh != null
                ? HordeRaiderSpriteAssets.MinimumContactDiameterMultiplier
                : 0.82f);
            compute.SetInt("_AnimatedGoblin", 0);
            compute.SetFloat("_FlowAcceleration", 8.5f);
            compute.SetFloat("_CollisionAcceleration", 18f);
            compute.SetFloat("_WallAcceleration", 13f);
            compute.SetFloat("_WallInfluence", 0.9f);
            compute.SetFloat("_VelocityDamping", 1.15f);
            compute.SetFloat("_FlowAlignment", 1f);
            compute.SetInt("_StationaryHorde", 0);
            compute.SetInt("_RandomArenaMovement", 0);
            compute.SetInt("_CenterSeekingArena", 0);
            compute.SetInt("_UnidirectionalHordeContacts", 0);
            compute.SetInt("_ReferenceCrowd", 0);
            compute.SetFloat("_ReferenceLateralContactResponse", 1f);
            compute.SetFloat("_ReferenceExternalForceRecovery", 0.8f);
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
            compute.SetBuffer(contactKernel, "_FlowData", flowData);
            compute.SetBuffer(contactKernel, "_FlowVectors", flowVectors);
            compute.SetBuffer(contactKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(contactKernel, "_Controls", controls);
            compute.SetBuffer(contactKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(contactKernel, "_ActiveIndicesRead", activeIndices);
            compute.SetBuffer(contactKernel, "_DiagnosticsRead", diagnostics);
            // ResolveContacts records ordinary reverse travel for the OrcReference profile.
            // It therefore needs both read and write bindings for this buffer.
            compute.SetBuffer(contactKernel, "_Diagnostics", diagnostics);
            compute.SetBuffer(contactKernel, "_SpatialStatesRead", spatialStates);
            compute.SetBuffer(contactCellsKernel, "_FlowData", flowData);
            compute.SetBuffer(contactCellsKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(contactCellsKernel, "_CellAgentsRead", cellAgents);
            foreach (var kernel in new[] { prepareMotionKernel, constrainMotionKernel, commitMotionKernel })
            {
                compute.SetBuffer(kernel, "_ActiveIndicesRead", activeIndices);
                compute.SetBuffer(kernel, "_DiagnosticsRead", diagnostics);
            }
            compute.SetBuffer(constrainMotionKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(constrainMotionKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(constrainMotionKernel, "_SpatialStatesRead", spatialStates);
            compute.SetBuffer(constrainMotionKernel, "_FlowData", flowData);
            compute.SetBuffer(reserveMotionKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(reserveMotionKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(reserveMotionKernel, "_SpatialStatesRead", spatialStates);
            compute.SetBuffer(reserveMotionKernel, "_AcceptedMotion", acceptedMotion);
            compute.SetBuffer(reserveMotionKernel, "_FlowData", flowData);
            compute.SetBuffer(reserveMotionKernel, "_MotionObstacles", motionObstacles);
            compute.SetBuffer(prepareMotionKernel, "_AcceptedMotion", acceptedMotion);
            compute.SetBuffer(commitMotionKernel, "_AcceptedMotion", acceptedMotion);
            compute.SetBuffer(commitMotionKernel, "_FlowData", flowData);
            compute.SetBuffer(commitMotionKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(commitMotionKernel, "_EventCount", eventCount);
            compute.SetBuffer(prepareMotionKernel, "_SpatialStatesRead", spatialStates);
            compute.SetBuffer(prepareMotionKernel, "_MotionObstacles", motionObstacles);
            compute.SetBuffer(prepareMotionKernel, "_DynamicTargetsRead", dynamicTargets);
            compute.SetBuffer(constrainMotionKernel, "_MotionObstacles", motionObstacles);
            compute.SetBuffer(prepareMotionKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(prepareMotionKernel, "_CellAgentsRead", cellAgents);
            foreach (var kernel in new[] { prepareMotionKernel, constrainMotionKernel, reserveMotionKernel })
            {
                compute.SetBuffer(kernel, "_MotionNeighbors", motionNeighbors);
                compute.SetBuffer(kernel, "_MotionNeighborCounts", motionNeighborCounts);
            }
            compute.SetBuffer(measureOverlapsKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(measureOverlapsKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(measureOverlapsKernel, "_ActiveIndicesRead", activeIndices);
            compute.SetBuffer(measureOverlapsKernel, "_DiagnosticsRead", diagnostics);
            compute.SetBuffer(measureOverlapsKernel, "_SpatialStatesRead", spatialStates);
            compute.SetBuffer(measureOverlapsKernel, "_Diagnostics", diagnostics);
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
            compute.SetBuffer(clearDynamicTargetGridKernel, "_DynamicTargetBlockedMass", dynamicTargetBlockedMass);
            compute.SetBuffer(clearDynamicTargetGridKernel, "_DynamicTargetDamage", dynamicTargetDamage);
            compute.SetBuffer(clearDynamicTargetGridKernel, "_DynamicTargetLastAttacker", dynamicTargetLastAttacker);
            compute.SetBuffer(buildDynamicTargetGridKernel, "_DynamicTargetsRead", dynamicTargets);
            compute.SetBuffer(buildDynamicTargetGridKernel, "_DynamicTargetCellCounts", dynamicTargetCellCounts);
            compute.SetBuffer(buildDynamicTargetGridKernel, "_DynamicTargetCellIndices", dynamicTargetCellIndices);
            compute.SetBuffer(simulateKernel, "_DynamicTargetsRead", dynamicTargets);
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
                diagnostics.GetData(destination, 0, 0, Mathf.Min(7, destination.Length));
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
            // The exact predictive reservation path requires dozens of whole-horde
            // dispatches per tick. At 10,000 agents it becomes CPU-dispatch bound
            // and, crucially, it cannot repair a body that was already overlapping.
            // The regular contact pass is the production path: it resolves existing
            // overlaps every tick. Tight spawn clearance and convoy-speed controls
            // prevent the old stop/start behaviour before this pass is needed.
            var predictiveContacts = false;
            compute.SetInt("_PredictiveContacts", predictiveContacts ? 1 : 0);
            compute.SetFloat("_DeltaTime", Mathf.Min(deltaTime, 0.1f));
            maximumMotionDistance = Mathf.Max(collisionDiameter * 0.4f,
                maximumDesiredSpeed * movementSpeedMultiplier * Mathf.Min(deltaTime, 0.1f) * 1.12f);
            compute.SetFloat("_MaximumMotionDistance", maximumMotionDistance);
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
            // Queries observe the pre-movement state. Every predictive displacement is
            // bounded by this disk, including all catch-up ticks before CPU consumption.
            motionTravelBudget += maximumMotionDistance;
            compute.DispatchIndirect(simulateKernel, activeDispatchArgs);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_StatesInput", writeStates);
            compute.Dispatch(resolveDynamicTargetDamageKernel, DivideRoundUp(MaxDynamicTargets, ThreadGroupSize), 1, 1);
            (readStates, writeStates) = (writeStates, readStates);
            var iterationScale = Mathf.Clamp(contactIterationScale, 0.01f, 1f);
            MovementStageObserver?.Invoke("integrated");
            if (predictiveContacts)
            {
                ResolvePredictiveMotion(uploadCount, iterationScale);
                MovementStageObserver?.Invoke("constrained");
                // Presentation reads the resolved displacement. No sequential
                // positional pushes: they invalidate neighbouring constraints.
                compute.SetInt("_HardContactCleanup", 1);
                compute.SetInt("_FinalizePresentation", 1);
                compute.SetBuffer(contactKernel, "_StatesRead", readStates);
                compute.SetBuffer(contactKernel, "_StatesWrite", writeStates);
                compute.DispatchIndirect(contactKernel, activeDispatchArgs);
                (readStates, writeStates) = (writeStates, readStates);
                RebuildContactGrid(uploadCount);
                compute.DispatchIndirect(measureOverlapsKernel, activeDispatchArgs);
            }
            var baseContactIterations = referenceCrowd ? referenceContactIterations : centerSeekingArena ? 64 :
                (uploadCount <= 5000 ? 12 : 20);
            var unscaledMinimumContactIterations = centerSeekingArena
                ? (shockwaveContactBoostTimer > 0f ? 28 : 16)
                : 2;
            var minimumContactIterations = Mathf.Max(
                centerSeekingArena ? 2 : 1,
                Mathf.CeilToInt(unscaledMinimumContactIterations * iterationScale));
            if (centerSeekingArena)
            {
                var unscaledEmergencyContactIterations = MaximumOverlapFraction > 0.5f || OverlapPairCount > 5000
                    ? 96
                    : MaximumOverlapFraction > 0.15f || OverlapPairCount > 1000
                        ? 72
                        : 0;
                minimumContactIterations = Mathf.Max(
                    minimumContactIterations,
                    Mathf.CeilToInt(unscaledEmergencyContactIterations * iterationScale));
            }
            var contactIterations = Mathf.Max(
                minimumContactIterations,
                Mathf.CeilToInt(baseContactIterations * iterationScale));
            if (!predictiveContacts) ResolveHardContacts(uploadCount, contactIterations, updatePresentation);
            shockwaveContactBoostTimer = Mathf.Max(0f, shockwaveContactBoostTimer - Mathf.Min(deltaTime, 0.1f));
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

        private void ResolveHardContacts(int uploadCount, int iterationCount, bool measureOverlaps)
        {
            compute.SetBuffer(contactKernel, "_Controls", controls);
            compute.SetInt("_ContactIterationCount", Mathf.Max(1, iterationCount));
            // Catch-up ticks already run the normal contact projection. Polish
            // silhouettes once before presenting the final tick of the frame;
            // repeating every color sweep in all four ticks defeats the shared
            // contact budget and amplifies an isolated long frame.
            // The local sequential cleanup is intentionally disabled for a
            // 10,000-agent travelling horde. Its many tiny dispatches make the
            // frame CPU-bound and its large positional corrections are visible
            // as teleport-like stepping. The regular solver is the stable path.
            var cleanupStride = 0;
            var cleanupColors = cleanupStride * cleanupStride;
            var cleanupIterations = cleanupColors;
            var totalIterations = iterationCount + cleanupIterations;
            LastContactIterationCount = totalIterations;
            compute.SetInt("_CleanupCellStride", Mathf.Max(1, cleanupStride));
            compute.SetInt("_HardContactCleanup", 0);
            for (var iteration = 0; iteration < iterationCount; iteration++)
            {
                compute.SetInt("_FinalizePresentation", cleanupIterations == 0 && iteration == iterationCount - 1 ? 1 : 0);
                compute.SetInt("_ContactIteration", iteration);
                RebuildContactGrid(uploadCount);
                compute.SetBuffer(contactKernel, "_StatesRead", readStates);
                compute.SetBuffer(contactKernel, "_StatesWrite", writeStates);
                compute.DispatchIndirect(contactKernel, activeDispatchArgs);
                (readStates, writeStates) = (writeStates, readStates);
            }

            if (cleanupIterations > 0)
            {
                MovementStageObserver?.Invoke("projected");
                var coloredCellCount = DivideRoundUp(gridWidth, cleanupStride) * DivideRoundUp(gridHeight, cleanupStride);
                compute.SetBuffer(contactCellsKernel, "_StatesRead", readStates);
                for (var iteration = 0; iteration < cleanupIterations; iteration++)
                {
                    // Freeze cell ownership for a complete color cycle. Rebuilding
                    // between colors lets a corrected body enter a later color and
                    // move twice, exceeding the displacement used to separate
                    // concurrently processed cells. Neighbour positions are still
                    // read live by the in-place kernel.
                    if (iteration % cleanupColors == 0) RebuildContactGrid(uploadCount);
                    compute.SetInt("_CleanupCellPhase", iteration % cleanupColors);
                    compute.Dispatch(contactCellsKernel, DivideRoundUp(coloredCellCount, ThreadGroupSize), 1, 1);
                }
                // Finalize animation once, from the travel after all projections.
                compute.SetInt("_HardContactCleanup", 1);
                compute.SetInt("_FinalizePresentation", 1);
                compute.SetBuffer(contactKernel, "_StatesRead", readStates);
                compute.SetBuffer(contactKernel, "_StatesWrite", writeStates);
                compute.DispatchIndirect(contactKernel, activeDispatchArgs);
                (readStates, writeStates) = (writeStates, readStates);
            }

            if (measureOverlaps)
            {
                MovementStageObserver?.Invoke("cleanup");
                RebuildContactGrid(uploadCount);
                compute.DispatchIndirect(measureOverlapsKernel, activeDispatchArgs);
            }
        }

        private void ResolvePredictiveMotion(int uploadCount, float iterationScale)
        {
            compute.SetBuffer(prepareMotionKernel, "_StatesRead", readStates);
            compute.SetBuffer(prepareMotionKernel, "_MotionWrite", motionA);
            compute.DispatchIndirect(prepareMotionKernel, activeDispatchArgs);
            compute.SetInt("_SlidingMotionAcceptance", SlidingMotionAcceptance ? 1 : 0);
            var source = motionA;
            var destination = motionB;
            var largeCrowd = uploadCount >= LargeCrowdThreshold;
            var baseProjectionIterations = largeCrowd ? LargeCrowdProjectionIterations : FullMotionProjectionIterations;
            var baseReservationSweeps = largeCrowd ? LargeCrowdReservationSweeps : FullMotionReservationSweeps;
            // A slow rendered frame can contain up to four fixed ticks. Sharing
            // this budget prevents catch-up from multiplying the GPU work and
            // creating a feedback loop (slow frame -> more ticks -> slower frame).
            // The final hard-contact pass remains authoritative, so this reduces
            // only approach polish, never the collision envelope.
            // Smaller tactical tests retain their full precision budget. The
            // catch-up scaling is needed only once a horde is large enough for
            // the dispatch count itself to be a frame-time risk.
            var solverScale = largeCrowd ? iterationScale : 1f;
            var projectionIterations = Mathf.Max(largeCrowd ? 3 : 6,
                Mathf.CeilToInt(baseProjectionIterations * solverScale));
            var reservationSweeps = Mathf.Max(largeCrowd ? 1 : 2,
                Mathf.CeilToInt(baseReservationSweeps * solverScale));
            for (var iteration = 0; iteration < projectionIterations; iteration++)
            {
                compute.SetBuffer(constrainMotionKernel, "_MotionRead", source);
                compute.SetBuffer(constrainMotionKernel, "_MotionWrite", destination);
                compute.DispatchIndirect(constrainMotionKernel, activeDispatchArgs);
                (source, destination) = (destination, source);
            }
            compute.SetBuffer(commitMotionKernel, "_StatesRead", readStates);
            var stride = Mathf.CeilToInt((MaximumPairDiameter * 1.08f + maximumMotionDistance * 2f) / gridCellSize) + 1;
            var colors = stride * stride;
            compute.SetInt("_CleanupCellStride", stride);
            compute.SetBuffer(reserveMotionKernel, "_MotionRead", source);
            var cells = DivideRoundUp(gridWidth, stride) * DivideRoundUp(gridHeight, stride);
            LastContactIterationCount = projectionIterations + colors * reservationSweeps;
            for (var pass = 0; pass < colors * reservationSweeps; pass++)
            {
                compute.SetInt("_CleanupCellPhase", ReverseMotionReservation ? colors - 1 - pass % colors : pass % colors);
                compute.SetInt("_ReverseMotionReservation", ReverseMotionReservation ? 1 : 0);
                compute.Dispatch(reserveMotionKernel, DivideRoundUp(cells, ThreadGroupSize), 1, 1);
            }
            compute.DispatchIndirect(commitMotionKernel, activeDispatchArgs);
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
                shockwaveContactBoostTimer = Mathf.Max(
                    shockwaveContactBoostTimer,
                    propagationRadius / ShockwavePropagationSpeed + 2.5f);
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

        public void SetMovementDynamics(float inertiaMultiplier, float collisionPressureMultiplier)
        {
            inertiaMultiplier = Mathf.Clamp(inertiaMultiplier, 0.5f, 2f);
            collisionPressureMultiplier = Mathf.Clamp(collisionPressureMultiplier, 0.5f, 3f);
            compute.SetFloat("_FlowAcceleration", 8.5f / inertiaMultiplier);
            compute.SetFloat("_CollisionAcceleration", 18f * collisionPressureMultiplier);
            compute.SetFloat("_VelocityDamping", 1.15f / inertiaMultiplier);
            compute.SetFloat("_FlowAlignment", Mathf.Clamp01(1f / inertiaMultiplier));
        }

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
            // Tactical sprite units retain the one-way convoy rule. The Orc
            // reference horde does not: at a choke point both equal-mass bodies
            // must be able to yield and transmit pressure in either direction.
            // Otherwise the row that happened to reach the junction first gets
            // an invisible reservation and the incoming row tunnels into it.
            compute.SetInt("_UnidirectionalHordeContacts",
                hordeSpriteMesh != null && !centerSeekingArena && !referenceCrowd ? 1 : 0);
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
            motionA?.Dispose();
            motionB?.Dispose();
            acceptedMotion?.Dispose();
            motionNeighbors?.Dispose();
            motionNeighborCounts?.Dispose();
            motionObstacles?.Dispose();
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
            dynamicTargetBlockedMass?.Dispose();
            dynamicTargetDamage?.Dispose();
            dynamicTargetLastAttacker?.Dispose();
            visibleIndices?.Dispose();
            lodIndirectArgs?.Dispose();
            variantIndirectArgs?.Dispose();
            variantLodIndirectArgs?.Dispose();
            activeIndices?.Dispose();
            activeDispatchArgs?.Dispose();
            if (compute != null)
            {
                UnityEngine.Object.Destroy(compute);
            }

            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }
            if (hordeSpriteMesh != null)
            {
                UnityEngine.Object.Destroy(hordeSpriteMesh);
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
