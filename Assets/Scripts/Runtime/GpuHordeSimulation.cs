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
        private const int ThreadGroupSize = 64;
        private const int CellCapacity = 96;
        private const int MaxTargetQueries = 512;
        private const int MaxAreaCommands = 256;
        private const int MaxProjectileCommands = 256;
        private const int MaxDynamicTargets = 256;
        private const int DynamicTargetCellCapacity = 8;
        private const int MaxDynamicTargetCommands = MaxDynamicTargets * 2;
        private const int ExtraCombatEventCapacity = 262144;
        private const float ReadbackInterval = 0.08f;

        [StructLayout(LayoutKind.Sequential)]
        internal struct AgentState
        {
            public Vector2 Position;
            public Vector2 Velocity;
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
            public float Padding3;
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
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TargetQuery
        {
            public Vector2 Position;
            public float Range;
            public uint Mode;
            public uint CanHitFlying;
            public Vector3 Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TargetResult
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public int Index;
            public float Score;
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
            public float Padding;
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
            public float Padding;
        }

        private readonly struct TargetQueryKey : IEquatable<TargetQueryKey>
        {
            private readonly int x;
            private readonly int y;
            private readonly int range;
            private readonly byte mode;
            private readonly byte canHitFlying;

            public TargetQueryKey(Vector3 position, float queryRange, TowerTargetingMode targetingMode, bool hitsFlying)
            {
                x = Mathf.RoundToInt(position.x * 4f);
                y = Mathf.RoundToInt(position.z * 4f);
                range = Mathf.RoundToInt(queryRange * 4f);
                mode = (byte)targetingMode;
                canHitFlying = hitsFlying ? (byte)1 : (byte)0;
            }

            public bool Equals(TargetQueryKey other) =>
                x == other.x && y == other.y && range == other.range && mode == other.mode && canHitFlying == other.canHitFlying;

            public override bool Equals(object obj) => obj is TargetQueryKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(x, y, range, mode, canHitFlying);
        }

        private readonly struct CachedTarget
        {
            public readonly int Index;
            public readonly int Frame;
            public readonly Vector2 Position;
            public readonly Vector2 Velocity;

            public CachedTarget(int index, int frame, Vector2 position, Vector2 velocity)
            {
                Index = index;
                Frame = frame;
                Position = position;
                Velocity = velocity;
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
        private readonly int clearDiagnosticsKernel;
        private readonly int gridKernel;
        private readonly int simulateKernel;
        private readonly int targetQueryKernel;
        private readonly int damageKernel;
        private readonly int statusKernel;
        private readonly int areaEffectKernel;
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
        private readonly GraphicsBuffer lodVisibleIndices;
        private readonly GraphicsBuffer lodIndirectArgs;
        private readonly GraphicsBuffer activeIndices;
        private readonly GraphicsBuffer activeDispatchArgs;
        private readonly Material material;
        private readonly MaterialPropertyBlock properties = new();
        private readonly MaterialPropertyBlock lodProperties = new();
        private readonly Mesh lowDetailMesh;
        private readonly Bounds drawBounds;
        private readonly uint[] drawArgs = new uint[5];
        private readonly uint[] lodDrawArgs = new uint[5];
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

        public uint OverflowCellCount { get; private set; }
        public uint DroppedAgentCount { get; private set; }
        public uint MaximumCellOccupancy { get; private set; }
        public uint GpuActiveCount { get; private set; }
        public uint VisibleAgentCount { get; private set; }

        private GpuHordeSimulation(int agentCapacity, HordeFlowField field, Mesh mesh)
        {
            capacity = agentCapacity;
            eventCapacity = capacity + ExtraCombatEventCapacity;
            cellCount = field.Width * field.Height;
            var sourceCompute = Resources.Load<ComputeShader>("HordeSimulation");
            var shader = Resources.Load<Shader>("HordeIndirect");
            if (sourceCompute == null || shader == null)
            {
                throw new InvalidOperationException("GPU horde resources were not found.");
            }

            compute = UnityEngine.Object.Instantiate(sourceCompute);
            material = new Material(shader) { enableInstancing = true };
            lowDetailMesh = EnemyManager.GetLowEnemyMesh();
            material.SetColor("_BaseColor", new Color(0.1f, 0.9f, 0.18f, 1f));
            material.SetColor("_SlowColor", new Color(0.2f, 0.62f, 1f, 1f));
            clearKernel = compute.FindKernel("ClearGrid");
            clearDiagnosticsKernel = compute.FindKernel("ClearDiagnostics");
            gridKernel = compute.FindKernel("BuildGrid");
            simulateKernel = compute.FindKernel("Simulate");
            targetQueryKernel = compute.FindKernel("QueryTargets");
            damageKernel = compute.FindKernel("ApplyDamageCommands");
            statusKernel = compute.FindKernel("ApplyStatusCommands");
            areaEffectKernel = compute.FindKernel("ApplyAreaEffect");
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
            diagnostics = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 5, sizeof(uint));
            targetQueries = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTargetQueries, Marshal.SizeOf<TargetQuery>());
            targetResults = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTargetQueries, Marshal.SizeOf<TargetResult>());
            damageCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Marshal.SizeOf<DamageCommand>());
            statusCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Marshal.SizeOf<StatusCommand>());
            areaEffectCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxAreaCommands, Marshal.SizeOf<AreaEffectCommand>());
            projectileCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxProjectileCommands, Marshal.SizeOf<ProjectileCommand>());
            hordeEvents = new GraphicsBuffer(GraphicsBuffer.Target.Structured, eventCapacity, Marshal.SizeOf<HordeEvent>());
            eventCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            dynamicTargets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, Marshal.SizeOf<DynamicTargetState>());
            dynamicTargetCommands = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargetCommands, Marshal.SizeOf<DynamicTargetCommand>());
            dynamicTargetCellCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));
            dynamicTargetCellIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount * DynamicTargetCellCapacity, sizeof(uint));
            dynamicTargetBlockedMass = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, sizeof(uint));
            dynamicTargetDamage = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, sizeof(uint));
            dynamicTargetLastAttacker = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDynamicTargets, sizeof(uint));
            visibleIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            lodVisibleIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            lodIndirectArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 5, sizeof(uint));
            activeIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
            activeDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 3, sizeof(uint));
            damageUpload = new DamageCommand[capacity];
            statusUpload = new StatusCommand[capacity];
            readStates = stateA;
            writeStates = stateB;

            var emptyStates = new AgentState[capacity];
            stateA.SetData(emptyStates);
            stateB.SetData(emptyStates);
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
            drawArgs[0] = mesh.GetIndexCount(0);
            drawArgs[1] = 0u;
            drawArgs[2] = mesh.GetIndexStart(0);
            drawArgs[3] = (uint)mesh.GetBaseVertex(0);
            indirectArgs.SetData(drawArgs);
            lodDrawArgs[0] = lowDetailMesh.GetIndexCount(0);
            lodDrawArgs[1] = 0u;
            lodDrawArgs[2] = lowDetailMesh.GetIndexStart(0);
            lodDrawArgs[3] = (uint)lowDetailMesh.GetBaseVertex(0);
            lodIndirectArgs.SetData(lodDrawArgs);

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
            compute.SetFloat("_CollisionDiameter", 0.84f);
            compute.SetFloat("_FlowAcceleration", 8.5f);
            compute.SetFloat("_CollisionAcceleration", 18f);
            compute.SetFloat("_WallAcceleration", 13f);
            compute.SetFloat("_WallInfluence", 0.9f);
            compute.SetFloat("_VelocityDamping", 1.15f);
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
            compute.SetBuffer(damageKernel, "_DamageCommands", damageCommands);
            compute.SetBuffer(damageKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(damageKernel, "_EventCount", eventCount);
            compute.SetBuffer(statusKernel, "_StatusCommands", statusCommands);
            compute.SetBuffer(areaEffectKernel, "_AreaEffectCommands", areaEffectCommands);
            compute.SetBuffer(areaEffectKernel, "_CellCountsRead", cellCounts);
            compute.SetBuffer(areaEffectKernel, "_CellAgentsRead", cellAgents);
            compute.SetBuffer(areaEffectKernel, "_HordeEvents", hordeEvents);
            compute.SetBuffer(areaEffectKernel, "_EventCount", eventCount);
            compute.SetBuffer(projectileKernel, "_ProjectileCommands", projectileCommands);
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
            compute.SetBuffer(cullVisibleKernel, "_IndirectArgs", indirectArgs);
            compute.SetBuffer(cullVisibleKernel, "_VisibleIndices", visibleIndices);
            compute.SetBuffer(cullVisibleKernel, "_LodVisibleIndices", lodVisibleIndices);
            compute.SetBuffer(cullVisibleKernel, "_LodIndirectArgs", lodIndirectArgs);
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
            stateA.SetData(singleStateUpload, 0, index, 1);
            stateB.SetData(singleStateUpload, 0, index, 1);
            singleControlUpload[0] = new Vector4(Mathf.Max(0.25f, velocity.magnitude), 1f, 0f, 0f);
            controls.SetData(singleControlUpload, 0, index, 1);
            activeHighWaterMark = Mathf.Max(activeHighWaterMark, index + 1);
            var spawnedCount = (uint)(index + 1);
            if (drawArgs[1] < spawnedCount)
            {
                drawArgs[1] = spawnedCount;
                indirectArgs.SetData(drawArgs);
            }
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

            stateA.SetData(states, startIndex, startIndex, count);
            stateB.SetData(states, startIndex, startIndex, count);
            if (controlData != null && controlData.Length >= startIndex + count)
            {
                controls.SetData(controlData, startIndex, startIndex, count);
            }
            activeHighWaterMark = Mathf.Max(activeHighWaterMark, startIndex + count);
            drawArgs[1] = (uint)Mathf.Max((int)drawArgs[1], startIndex + count);
            indirectArgs.SetData(drawArgs);
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
                diagnostics.GetData(destination, 0, 0, 4);
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
            dynamicTargetCommandUpload[dynamicTargetCommandCount++] = new DynamicTargetCommand
            {
                Slot = (uint)slot,
                Generation = generation,
                Operation = operation,
                Kind = target != null ? (uint)target.TargetKind : 0u,
                Position = new Vector2(position.x, position.z),
                Health = target != null ? Mathf.Max(0f, target.CurrentHealth) : 0f,
                MaxHealth = target != null ? Mathf.Max(1f, target.MaximumHealth) : 1f,
                Radius = target != null ? Mathf.Max(0.05f, target.CombatRadius) : 0.05f,
                BlockCapacity = target != null ? Mathf.Max(0f, target.BlockCapacity) : 0f,
                Armor = target != null ? Mathf.Max(0f, target.Armor) : 0f,
                PhysicalResistance = target != null ? Mathf.Clamp(target.PhysicalResistance, 0f, 0.95f) : 0f,
                FireResistance = target != null ? Mathf.Clamp(target.FireResistance, 0f, 0.95f) : 0f,
                SlowResistance = target != null ? Mathf.Clamp(target.SlowResistance, 0f, 0.95f) : 0f,
                ThornsDamage = target != null ? Mathf.Max(0f, target.ThornsDamage) : 0f
            };
        }

        public void Dispatch(float deltaTime, Vector4[] controlData, Vector2[] impulseData, bool requestReadback = true)
        {
            if (disposed)
            {
                return;
            }

            var uploadCount = Mathf.Min(activeHighWaterMark, capacity);
            if (controlData != null && uploadCount > 0)
            {
                controls.SetData(controlData, 0, 0, Mathf.Min(uploadCount, controlData.Length));
            }

            if (impulseData != null && uploadCount > 0)
            {
                impulses.SetData(impulseData, 0, 0, Mathf.Min(uploadCount, impulseData.Length));
            }
            compute.SetInt("_AgentCount", uploadCount);
            compute.SetFloat("_DeltaTime", Mathf.Min(deltaTime, 0.05f));
            SetCameraParameters();
            compute.SetBuffer(gridKernel, "_StatesRead", readStates);
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
            DispatchAreaEffects();
            DispatchTargetQueries();
            compute.DispatchIndirect(simulateKernel, activeDispatchArgs);
            compute.SetBuffer(resolveDynamicTargetDamageKernel, "_StatesInput", writeStates);
            compute.Dispatch(resolveDynamicTargetDamageKernel, DivideRoundUp(MaxDynamicTargets, ThreadGroupSize), 1, 1);
            (readStates, writeStates) = (writeStates, readStates);
            compute.SetBuffer(cullVisibleKernel, "_StatesInput", readStates);
            compute.Dispatch(clearVisibilityKernel, 1, 1, 1);
            compute.DispatchIndirect(cullVisibleKernel, activeDispatchArgs);
            if (requestReadback)
            {
                RequestDiagnosticsReadback();
                RequestEventReadback();
            }
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
            int maxBurnStacks = 1)
        {
            if (disposed || queuedProjectiles.Count >= MaxProjectileCommands || damage <= 0f)
            {
                return false;
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
                MaxBurnStacks = (uint)Mathf.Max(1, maxBurnStacks)
            });
            return true;
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

        public bool TryGetCachedTarget(
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
            if (disposed)
            {
                return false;
            }

            var queryRange = float.IsInfinity(range)
                ? gridDiagonal + Vector2.Distance(new Vector2(position.x, position.z), gridCenter)
                : Mathf.Max(0.05f, range);
            var key = new TargetQueryKey(position, queryRange, targetingMode, canHitFlying);
            var hasFreshResult = cachedTargets.TryGetValue(key, out var cached) && Time.frameCount - cached.Frame <= 8;
            if (!queuedTargetKeys.Contains(key) && pendingTargetQueries.Count < MaxTargetQueries &&
                (!hasFreshResult || Time.frameCount - cached.Frame >= 2))
            {
                pendingTargetQueries.Add(new PendingTargetQuery(key, new TargetQuery
                {
                    Position = new Vector2(position.x, position.z),
                    Range = queryRange,
                    Mode = ToGpuTargetingMode(targetingMode),
                    CanHitFlying = canHitFlying ? 1u : 0u
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
            float slowCapacity = 0f)
        {
            if (disposed || radius <= 0f || queuedAreaEffects.Count >= MaxAreaCommands)
            {
                return false;
            }

            queuedAreaEffects.Add(new AreaEffectCommand
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
                SlowCapacity = Mathf.Max(0f, slowCapacity)
            });
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

        public void Draw(Mesh mesh, int layer)
        {
            if (disposed || mesh == null || material == null)
            {
                return;
            }

            properties.SetBuffer("_AgentStates", readStates);
            properties.SetBuffer("_VisibleIndices", visibleIndices);
            lodProperties.SetBuffer("_AgentStates", readStates);
            lodProperties.SetBuffer("_VisibleIndices", lodVisibleIndices);
            Graphics.DrawMeshInstancedIndirect(
                mesh,
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

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stateA?.Dispose();
            stateB?.Dispose();
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
            lodVisibleIndices?.Dispose();
            lodIndirectArgs?.Dispose();
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
            compute.SetInt("_AreaCommandCount", count);
            compute.Dispatch(areaEffectKernel, 1, 1, 1);
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
                    cachedTargets[key] = new CachedTarget(data[i].Index, Time.frameCount, data[i].Position, data[i].Velocity);
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
            compute.SetInt("_FrameIndex", Time.frameCount);
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
            compute.SetMatrix("_ViewProjection", projection * camera.worldToCameraMatrix);
        }

        private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;
    }
}
