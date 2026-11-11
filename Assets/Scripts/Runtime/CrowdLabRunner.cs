using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public enum CrowdLabScenario
    {
        TwoBodies,
        QueueRelease,
        WallImpact,
        RightAngleBend,
        ThreeWayMerge,
        BlockedExit,
        ExplosionRecovery,
        StressTenThousand,
        StressFiftyThousand
    }

    [Serializable]
    public sealed class CrowdLabReport
    {
        public string Scenario;
        public int Seed;
        public int TickCount;
        public int BodyCount;
        public float FixedStep;
        public float MeanTravelSpeed;
        public float ForwardThroughput;
        public int BackwardBodies;
        public float MaximumBacktrack;
        public uint ShallowContactPairs;
        public uint DeepOverlapPairs;
        public uint DeepDiskContactPairs;
        public uint DeepBlockerContacts;
        public uint DeepWallContacts;
        public uint PersistentDeepContactBodies;
        public float MaximumPenetrationFraction;
        public float DeepContactDurationSeconds;
        public uint ActiveConstraints;
        public uint MaximumCellOccupancy;
        public uint GridCapacityFailures;
        public int WalkabilityViolations;
        public float RecoverySeconds;
        public float[] TickDispatchMilliseconds;
    }

    /// <summary>
    /// A deliberately art-free harness for the production GPU backend. It is a
    /// component so it can be dropped into an empty scene for visual inspection;
    /// its gizmos render physical disks and desired travel without sprite noise.
    /// </summary>
    public sealed class CrowdLabRunner : MonoBehaviour
    {
        private const float BodyScale = 0.34f;
        private const float BodyDiameter = 0.43f;
        private const int DiagnosticStride = 6;

        [SerializeField] private CrowdLabScenario scenario = CrowdLabScenario.RightAngleBend;
        [SerializeField] private int seed = 761905;
        [SerializeField] private bool drawTrails = true;

        private GpuHordeSimulation simulation;
        private HordeFlowField flowField;
        private GpuHordeSimulation.AgentState[] states;
        private Vector4[] controls;
        private Vector2[] initialPositions;
        private readonly List<Vector2[]> trailFrames = new();
        private readonly List<float> tickDispatchMilliseconds = new();
        private readonly uint[] diagnostics = new uint[19];
        private readonly ICombatTarget[] blockerTargets = new ICombatTarget[1];
        private CrowdSolverProfile solverProfile;
        private CrowdLabBlocker blocker;
        private int currentTick;
        private bool exploded;

        public CrowdLabScenario Scenario => scenario;
        public int BodyCount => states?.Length ?? 0;
        public float FixedStep => solverProfile != null ? solverProfile.FixedStep : 1f / 60f;
        internal int CurrentTick => currentTick;
        internal HordeFlowField FlowField => flowField;
        internal GpuHordeSimulation.AgentState[] LatestStates => states;
        internal CrowdLabBlocker ActiveBlocker => blocker != null && blocker.IsAlive ? blocker : null;

        internal void SetGizmoTrails(bool enabled)
        {
            drawTrails = enabled;
            if (!enabled) trailFrames.Clear();
        }

        public void Configure(CrowdLabScenario selectedScenario, int selectedSeed = 761905)
        {
            DisposeSimulation();
            scenario = selectedScenario;
            seed = selectedSeed;
            UnityEngine.Random.InitState(seed);
            solverProfile = CrowdSolverProfile.Default;
            BuildScenario(selectedScenario);
            if (!GpuHordeSimulation.TryCreate(states.Length, flowField, EnemyManager.GetLowEnemyMesh(), out simulation))
            {
                throw new InvalidOperationException("Crowd Lab requires GPU compute shader support.");
            }
            if (!simulation.SupportsXpbdGrid)
            {
                simulation.Dispose();
                simulation = null;
                throw new InvalidOperationException("Crowd Lab grid exceeds the current compact-scan hierarchy.");
            }

            simulation.SetCollisionDiameter(BodyDiameter);
            simulation.SetXpbdPerBodyRadius(false);
            simulation.SetCrowdSolverProfile(solverProfile);
            simulation.SpawnBatch(states, controls, 0, states.Length);
            if (UsesPhysicalBlocker(selectedScenario))
            {
                var blockerObject = new GameObject("Crowd Lab physical blocker")
                {
                    hideFlags = HideFlags.DontSave
                };
                blocker = blockerObject.AddComponent<CrowdLabBlocker>();
                blocker.Configure(BlockerPosition(selectedScenario),
                    selectedScenario == CrowdLabScenario.BlockedExit ? 1.05f : 0.62f);
                blockerTargets[0] = blocker;
                simulation.SynchronizeDynamicTargets(blockerTargets);
            }
            initialPositions = new Vector2[states.Length];
            for (var i = 0; i < states.Length; i++) initialPositions[i] = states[i].Position;
            currentTick = 0;
            exploded = false;
            trailFrames.Clear();
        }

        public CrowdLabReport RunFixedTicks(int tickCount, int presentationBatch = 1, bool writeReport = true)
        {
            if (simulation == null) Configure(scenario, seed);
            presentationBatch = Mathf.Max(1, presentationBatch);
            var deepDuration = 0f;
            var maximumDepth = 0f;
            var maximumShallow = 0u;
            var maximumDeep = 0u;
            var maximumPersistentDeepBodies = 0u;
            var maximumDeepDiskPairs = 0u;
            var maximumDeepBlockerContacts = 0u;
            var maximumDeepWallContacts = 0u;
            var maximumConstraints = 0u;
            var maximumOccupancy = 0u;
            var capacityFailures = 0u;
            var maximumWalkabilityViolations = 0;
            var recoverySeconds = -1f;
            if (tickDispatchMilliseconds.Capacity < tickCount) tickDispatchMilliseconds.Capacity = tickCount;
            tickDispatchMilliseconds.Clear();
            for (var tick = 0; tick < tickCount; tick++)
            {
                if (scenario == CrowdLabScenario.QueueRelease && currentTick == 90)
                {
                    // This goes through the same dynamic-target lifecycle that
                    // a destroyed barrier uses in a real wave. No queued body
                    // receives a special forward displacement on release.
                    blocker?.Release();
                }
                if (scenario == CrowdLabScenario.ExplosionRecovery && !exploded && currentTick == 90)
                {
                    simulation.QueueAreaEffect(Vector3.zero, 3.2f, 0f, 6f, 0, 0f, 0f, 1,
                        knockbackPropagationRadius: 5.5f);
                    exploded = true;
                }

                var present = (tick + 1) % presentationBatch == 0 || tick == tickCount - 1;
                simulation.SynchronizeDynamicTargets(blocker != null && blocker.IsAlive ? blockerTargets : null);
                var dispatchStart = Stopwatch.GetTimestamp();
                simulation.DispatchFixedTick(null, null, requestReadback: false, updatePresentation: present,
                    updateVisibility: false);
                tickDispatchMilliseconds.Add((float)((Stopwatch.GetTimestamp() - dispatchStart) * 1000.0 /
                    Stopwatch.Frequency));
                currentTick++;
                if (currentTick % DiagnosticStride != 0 && tick != tickCount - 1) continue;

                simulation.SynchronizeDiagnostics(diagnostics);
                maximumDepth = Mathf.Max(maximumDepth, diagnostics[6] / 1000000f);
                maximumShallow = Math.Max(maximumShallow, diagnostics[12]);
                maximumDeep = Math.Max(maximumDeep, diagnostics[13]);
                maximumPersistentDeepBodies = Math.Max(maximumPersistentDeepBodies, diagnostics[15]);
                maximumDeepDiskPairs = Math.Max(maximumDeepDiskPairs, diagnostics[16]);
                maximumDeepBlockerContacts = Math.Max(maximumDeepBlockerContacts, diagnostics[17]);
                maximumDeepWallContacts = Math.Max(maximumDeepWallContacts, diagnostics[18]);
                maximumConstraints = Math.Max(maximumConstraints, diagnostics[11]);
                maximumOccupancy = Math.Max(maximumOccupancy, diagnostics[2]);
                capacityFailures = Math.Max(capacityFailures, diagnostics[14]);
                if (diagnostics[15] > 0u) deepDuration += DiagnosticStride * FixedStep;
                if (scenario == CrowdLabScenario.ExplosionRecovery && exploded && currentTick >= 90 &&
                    diagnostics[13] == 0u && recoverySeconds < 0f)
                {
                    recoverySeconds = (currentTick - 90) * FixedStep;
                }
                maximumWalkabilityViolations = Mathf.Max(
                    maximumWalkabilityViolations, CaptureTrailAndCountWalkabilityViolations());
            }

            simulation.ReadStatesSynchronous(states, states.Length);
            var report = BuildReport(tickCount, maximumShallow, maximumDeep, maximumDepth, deepDuration,
                maximumPersistentDeepBodies, maximumDeepDiskPairs, maximumDeepBlockerContacts,
                maximumDeepWallContacts, maximumConstraints, maximumOccupancy, capacityFailures,
                maximumWalkabilityViolations, recoverySeconds);
            if (writeReport) WriteReport(report);
            return report;
        }

        internal GpuHordeSimulation.AgentState[] ReadSnapshot()
        {
            if (simulation == null) return Array.Empty<GpuHordeSimulation.AgentState>();
            var snapshot = new GpuHordeSimulation.AgentState[states.Length];
            simulation.ReadStatesSynchronous(snapshot, snapshot.Length);
            return snapshot;
        }

        private CrowdLabReport BuildReport(
            int tickCount,
            uint shallow,
            uint deep,
            float maximumDepth,
            float deepDuration,
            uint persistentDeepBodies,
            uint deepDiskPairs,
            uint deepBlockerContacts,
            uint deepWallContacts,
            uint constraints,
            uint occupancy,
            uint capacityFailures,
            int observedWalkabilityViolations,
            float recoverySeconds)
        {
            var travel = 0f;
            var forwardTravel = 0f;
            var moving = 0;
            var backward = 0;
            var maximumBacktrack = 0f;
            var walkabilityViolations = 0;
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].Status != 1) continue;
                var displacement = states[i].Position - initialPositions[i];
                travel += displacement.magnitude;
                var flow = flowField.GetDirection(new Vector3(initialPositions[i].x, 0f, initialPositions[i].y));
                forwardTravel += Mathf.Max(0f, Vector2.Dot(displacement, new Vector2(flow.x, flow.z)));
                moving++;
                if (displacement.x < -0.001f)
                {
                    backward++;
                    maximumBacktrack = Mathf.Max(maximumBacktrack, -displacement.x);
                }
                if (!flowField.IsWalkable(new Vector3(states[i].Position.x, 0f, states[i].Position.y)))
                {
                    walkabilityViolations++;
                }
            }

            var elapsedSeconds = Mathf.Max(FixedStep, currentTick * FixedStep);
            return new CrowdLabReport
            {
                Scenario = scenario.ToString(),
                Seed = seed,
                TickCount = tickCount,
                BodyCount = states.Length,
                FixedStep = FixedStep,
                MeanTravelSpeed = travel / Mathf.Max(1, moving) / elapsedSeconds,
                ForwardThroughput = forwardTravel / elapsedSeconds,
                BackwardBodies = backward,
                MaximumBacktrack = maximumBacktrack,
                ShallowContactPairs = shallow,
                DeepOverlapPairs = deep,
                PersistentDeepContactBodies = persistentDeepBodies,
                DeepDiskContactPairs = deepDiskPairs,
                DeepBlockerContacts = deepBlockerContacts,
                DeepWallContacts = deepWallContacts,
                MaximumPenetrationFraction = maximumDepth,
                DeepContactDurationSeconds = deepDuration,
                ActiveConstraints = constraints,
                MaximumCellOccupancy = occupancy,
                GridCapacityFailures = capacityFailures,
                WalkabilityViolations = Mathf.Max(walkabilityViolations, observedWalkabilityViolations),
                RecoverySeconds = recoverySeconds,
                TickDispatchMilliseconds = tickDispatchMilliseconds.ToArray()
            };
        }

        private void BuildScenario(CrowdLabScenario selectedScenario)
        {
            var primary = new[] { new Vector3(-12f, 0f, 0f), new Vector3(12f, 0f, 0f) };
            Vector3[] secondary = null;
            Vector3[] tertiary = null;
            float halfWidth = 2.8f;
            var count = 96;
            switch (selectedScenario)
            {
                case CrowdLabScenario.TwoBodies:
                    count = 2;
                    break;
                case CrowdLabScenario.RightAngleBend:
                    primary = new[] { new Vector3(-10f, 0f, -6f), new Vector3(0f, 0f, -6f), new Vector3(0f, 0f, 8f) };
                    halfWidth = 2.2f;
                    count = 180;
                    break;
                case CrowdLabScenario.WallImpact:
                    // Leave enough downstream road that the deliberately
                    // wall-bound row cannot be classified as escaped while the
                    // test is still measuring its wall recovery.
                    primary = new[] { new Vector3(-10f, 0f, 0f), new Vector3(20f, 0f, 0f) };
                    halfWidth = 1.35f;
                    count = 32;
                    break;
                case CrowdLabScenario.ThreeWayMerge:
                case CrowdLabScenario.BlockedExit:
                    primary = new[] { new Vector3(-12f, 0f, 0f), new Vector3(12f, 0f, 0f) };
                    secondary = new[] { new Vector3(-12f, 0f, 5f), new Vector3(-2f, 0f, 5f), new Vector3(4f, 0f, 0f), new Vector3(12f, 0f, 0f) };
                    tertiary = new[] { new Vector3(-12f, 0f, -5f), new Vector3(-2f, 0f, -5f), new Vector3(4f, 0f, 0f), new Vector3(12f, 0f, 0f) };
                    halfWidth = selectedScenario == CrowdLabScenario.BlockedExit ? 1.1f : 2.6f;
                    count = selectedScenario == CrowdLabScenario.BlockedExit ? 420 : 720;
                    break;
                case CrowdLabScenario.ExplosionRecovery:
                    count = 420;
                    halfWidth = 7.5f;
                    break;
                case CrowdLabScenario.StressTenThousand:
                    primary = new[] { new Vector3(-80f, 0f, 0f), new Vector3(80f, 0f, 0f) };
                    halfWidth = 28f;
                    count = 10000;
                    break;
                case CrowdLabScenario.StressFiftyThousand:
                    primary = new[] { new Vector3(-180f, 0f, 0f), new Vector3(180f, 0f, 0f) };
                    // Keep the field below the current 65,536-cell compact
                    // scan level while widening the route length for 50k.
                    halfWidth = 28f;
                    count = 50000;
                    break;
            }

            var primaryWidths = FillWidths(primary.Length, halfWidth);
            var secondaryWidths = secondary != null ? FillWidths(secondary.Length, halfWidth) : null;
            var tertiaryWidths = tertiary != null ? FillWidths(tertiary.Length, halfWidth) : null;
            if (selectedScenario == CrowdLabScenario.BlockedExit)
            {
                // Wide approach lanes collapse into one narrow, physically
                // blockable exit without spawning bodies outside their source.
                primaryWidths[0] = 2.8f;
                if (secondaryWidths != null)
                {
                    secondaryWidths[0] = 2.8f;
                    secondaryWidths[1] = 2.8f;
                    secondaryWidths[2] = halfWidth;
                }
                if (tertiaryWidths != null)
                {
                    tertiaryWidths[0] = 2.8f;
                    tertiaryWidths[1] = 2.8f;
                    tertiaryWidths[2] = halfWidth;
                }
            }
            flowField = new HordeFlowField(primary, secondary, halfWidth, 0.62f, primaryWidths,
                secondaryWidths, tertiary, tertiaryWidths);
            states = new GpuHordeSimulation.AgentState[count];
            controls = new Vector4[count];
            if (selectedScenario == CrowdLabScenario.TwoBodies)
            {
                // Start with a deliberate 16% overlap so this is a solver
                // assertion, not merely two bodies travelling side by side.
                CreateBody(0, new Vector2(-0.18f, 0f), Vector2.right, 2.4f);
                CreateBody(1, new Vector2(0.18f, 0f), Vector2.left, 2.4f);
                return;
            }

            var streamOffset = selectedScenario == CrowdLabScenario.ThreeWayMerge ||
                selectedScenario == CrowdLabScenario.BlockedExit;
            var streamCount = streamOffset ? 3 : 1;
            var bodiesPerStream = Mathf.CeilToInt(count / (float)streamCount);
            var spacing = BodyDiameter * 1.06f;
            var maximumRows = Mathf.Max(1, Mathf.FloorToInt(
                (halfWidth * 2f - BodyDiameter) / spacing) + 1);
            var columns = Mathf.Max(1, Mathf.Max(
                Mathf.CeilToInt(Mathf.Sqrt(bodiesPerStream * 0.65f)),
                Mathf.CeilToInt(bodiesPerStream / (float)maximumRows)));
            var rows = Mathf.Max(1, Mathf.CeilToInt(bodiesPerStream / (float)columns));
            for (var i = 0; i < count; i++)
            {
                var stream = streamOffset ? i % 3 : 0;
                var localIndex = streamOffset ? i / 3 : i;
                var row = localIndex / columns;
                var column = localIndex % columns;
                var x = primary[0].x + 0.5f + column * spacing;
                var sourceZ = primary[0].z;
                if (streamOffset)
                {
                    sourceZ = stream == 0 ? tertiary[0].z : stream == 2 ? secondary[0].z : primary[0].z;
                }
                var z = sourceZ + (row - (rows - 1) * 0.5f) * spacing;
                if (selectedScenario == CrowdLabScenario.ExplosionRecovery)
                {
                    var radius = Mathf.Sqrt(i / (float)count) * 5.6f;
                    var angle = i * 2.39996323f;
                    x = Mathf.Cos(angle) * radius;
                    z = Mathf.Sin(angle) * radius;
                }
                else if (selectedScenario == CrowdLabScenario.WallImpact)
                {
                    // A single non-overlapping row isolates the wall constraint.
                    // Reusing the normal row/column layout here would collapse
                    // multiple rows onto identical positions at the upper wall.
                    x = primary[0].x + 0.5f + i * spacing;
                    z = primary[0].z + halfWidth - BodyDiameter * 0.32f;
                }
                var initialVelocity = selectedScenario == CrowdLabScenario.WallImpact ? Vector2.up : Vector2.right;
                CreateBody(i, new Vector2(x, z), initialVelocity, 3.2f);
            }
        }

        private static bool UsesPhysicalBlocker(CrowdLabScenario selectedScenario) =>
            selectedScenario == CrowdLabScenario.QueueRelease ||
            selectedScenario == CrowdLabScenario.BlockedExit;

        private static Vector3 BlockerPosition(CrowdLabScenario selectedScenario) =>
            selectedScenario == CrowdLabScenario.QueueRelease
                ? new Vector3(-7f, 0f, 0f)
                : new Vector3(4.25f, 0f, 0f);

        private static float[] FillWidths(int length, float width)
        {
            var result = new float[length];
            for (var i = 0; i < result.Length; i++) result[i] = width;
            return result;
        }

        private void CreateBody(int index, Vector2 position, Vector2 velocity, float speed)
        {
            states[index] = new GpuHordeSimulation.AgentState
            {
                Position = position,
                StepStartPosition = position,
                Velocity = velocity.normalized * speed,
                Facing = velocity.normalized,
                Scale = BodyScale,
                Status = 1,
                Health = 100f,
                MaxHealth = 100f,
                SlowMultiplier = 1f,
                Mass = 1f
            };
            controls[index] = new Vector4(speed, 1f, 0f, 0f);
        }

        private int CaptureTrailAndCountWalkabilityViolations()
        {
            if (!drawTrails || simulation == null || states == null || states.Length > 1000) return 0;
            var snapshot = new GpuHordeSimulation.AgentState[states.Length];
            simulation.ReadStatesSynchronous(snapshot, snapshot.Length);
            var frame = new Vector2[snapshot.Length];
            var violations = 0;
            for (var i = 0; i < frame.Length; i++)
            {
                frame[i] = snapshot[i].Position;
                if (snapshot[i].Status == 1u && !flowField.IsWalkable(
                        new Vector3(snapshot[i].Position.x, 0f, snapshot[i].Position.y)))
                {
                    violations++;
                }
            }
            trailFrames.Add(frame);
            if (trailFrames.Count > 24) trailFrames.RemoveAt(0);
            return violations;
        }

        private void WriteReport(CrowdLabReport report)
        {
            var directory = Path.Combine("Logs", "Reports", "CrowdRebuild");
            Directory.CreateDirectory(directory);
            var baseName = $"{report.Scenario}-{report.Seed}";
            File.WriteAllText(Path.Combine(directory, $"{baseName}.json"), JsonUtility.ToJson(report, true));

            var csv = new StringBuilder("tick,dispatchMilliseconds\n");
            for (var tick = 0; tick < report.TickDispatchMilliseconds.Length; tick++)
            {
                csv.Append(tick)
                    .Append(',')
                    .Append(report.TickDispatchMilliseconds[tick].ToString("F4", CultureInfo.InvariantCulture))
                    .Append('\n');
            }
            File.WriteAllText(Path.Combine(directory, $"{baseName}.csv"), csv.ToString());
        }

        private void OnDrawGizmosSelected()
        {
            if (states == null) return;
            Gizmos.color = PlaceholderPalette.Grey(.667f, 0.85f);
            var radius = BodyDiameter * 0.5f;
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].Status != 1) continue;
                var position = new Vector3(states[i].Position.x, 0f, states[i].Position.y);
                Gizmos.DrawWireSphere(position, radius);
                var desired = flowField != null ? flowField.GetDirection(position) : Vector3.right;
                Gizmos.DrawLine(position, position + desired * radius * 1.6f);
            }
            if (!drawTrails) return;
            Gizmos.color = PlaceholderPalette.Grey(.768f, 0.35f);
            foreach (var frame in trailFrames)
            {
                for (var i = 0; i < frame.Length; i++)
                    Gizmos.DrawSphere(new Vector3(frame[i].x, 0f, frame[i].y), 0.018f);
            }
        }

        private void OnDestroy() => DisposeSimulation();

        private void DisposeSimulation()
        {
            simulation?.Dispose();
            simulation = null;
            if (blocker != null)
            {
                Destroy(blocker.gameObject);
                blocker = null;
            }
            blockerTargets[0] = null;
        }
    }
}
