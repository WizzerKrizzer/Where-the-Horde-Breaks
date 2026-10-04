using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class CrowdLabRunnerTests
    {
        [UnityTest]
        public IEnumerator FixedTickSchedule_IsIndependentOfPresentationBatch()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab deterministic schedule test");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.RightAngleBend, 1451);
                yield return null;
                var oneTickReport = runner.RunFixedTicks(120, presentationBatch: 1, writeReport: false);
                var oneTickFrames = runner.ReadSnapshot();

                runner.Configure(CrowdLabScenario.RightAngleBend, 1451);
                yield return null;
                var fourTickReport = runner.RunFixedTicks(120, presentationBatch: 4, writeReport: false);
                var fourTickFrames = runner.ReadSnapshot();

                Assert.That(fourTickFrames.Length, Is.EqualTo(oneTickFrames.Length));
                for (var i = 0; i < oneTickFrames.Length; i++)
                {
                    Assert.That(Vector2.Distance(oneTickFrames[i].Position, fourTickFrames[i].Position),
                        Is.LessThan(0.001f), $"Body {i} changed because presentation batching changed.");
                }
                Assert.That(fourTickReport.GridCapacityFailures, Is.EqualTo(oneTickReport.GridCapacityFailures));
                Assert.That(fourTickReport.ActiveConstraints, Is.EqualTo(oneTickReport.ActiveConstraints));
                Assert.That(fourTickReport.DeepOverlapPairs, Is.EqualTo(oneTickReport.DeepOverlapPairs));
                Assert.That(fourTickReport.PersistentDeepContactBodies,
                    Is.EqualTo(oneTickReport.PersistentDeepContactBodies));
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator AllScenarios_MatchAcrossRenderedFpsAndGameSpeedSchedules()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var scenarios = new[]
            {
                CrowdLabScenario.TwoBodies,
                CrowdLabScenario.QueueRelease,
                CrowdLabScenario.WallImpact,
                CrowdLabScenario.RightAngleBend,
                CrowdLabScenario.ThreeWayMerge,
                CrowdLabScenario.BlockedExit,
                CrowdLabScenario.ExplosionRecovery,
                CrowdLabScenario.StressTenThousand,
                CrowdLabScenario.StressFiftyThousand
            };
            var tickCounts = new[] { 120, 93, 120, 360, 360, 420, 282, 120, 60 };
            // A rendered frame presents this many exact 1/60-second ticks. At
            // 120 FPS and 1x, the extra render-only frame cannot alter physics,
            // so each simulated tick is presented exactly as in the 60 FPS run.
            var presentationBatches = new[] { 2, 1, 20, 10, 5 };
            var scheduleNames = new[] { "30 FPS / 1x", "120 FPS / 1x", "30 FPS / 10x",
                "60 FPS / 10x", "120 FPS / 10x" };
            var host = new GameObject("Crowd Lab complete schedule matrix");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                for (var scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
                {
                    var scenario = scenarios[scenarioIndex];
                    var seed = 23000 + scenarioIndex * 97;
                    runner.Configure(scenario, seed);
                    yield return null;
                    var baselineReport = runner.RunFixedTicks(tickCounts[scenarioIndex],
                        presentationBatch: 1, writeReport: false);
                    var baselineState = runner.ReadSnapshot();

                    for (var scheduleIndex = 0; scheduleIndex < presentationBatches.Length;
                         scheduleIndex++)
                    {
                        runner.Configure(scenario, seed);
                        yield return null;
                        var candidateReport = runner.RunFixedTicks(tickCounts[scenarioIndex],
                            presentationBatch: presentationBatches[scheduleIndex], writeReport: false);
                        var candidateState = runner.ReadSnapshot();
                        var context = $"{scenario} at {scheduleNames[scheduleIndex]}";

                        Assert.That(candidateState.Length, Is.EqualTo(baselineState.Length), context);
                        AssertMatchingPhysicalState(baselineState, candidateState, context);

                        AssertMatchingDiagnostics(baselineReport, candidateReport, context);
                    }
                }
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator TwoBodyLab_ReportsContactsWithoutGridLoss()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab two body test");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.TwoBodies, 9);
                yield return null;
                var report = runner.RunFixedTicks(120, writeReport: false);
                var finalState = runner.ReadSnapshot();

                Assert.That(report.GridCapacityFailures, Is.Zero);
                Assert.That(report.WalkabilityViolations, Is.Zero);
                Assert.That(report.DeepOverlapPairs, Is.Zero,
                    "A two-body contact must not remain deeply interpenetrating.");
                Assert.That(report.MaximumPenetrationFraction, Is.LessThan(0.05f));
                Assert.That(Vector2.Distance(finalState[0].Position, finalState[1].Position),
                    Is.GreaterThanOrEqualTo(0.42f));
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator QueueRelease_RestartsQueuedBodiesWithinThreeFixedTicks()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab queue release test");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.QueueRelease, 411);
                yield return null;
                runner.RunFixedTicks(90, writeReport: false);
                var beforeRelease = runner.ReadSnapshot();
                var report = runner.RunFixedTicks(3, writeReport: false);
                var afterRelease = runner.ReadSnapshot();

                var restarted = 0;
                for (var i = 0; i < afterRelease.Length; i++)
                {
                    if ((beforeRelease[i].Flags & 2u) != 0u &&
                        beforeRelease[i].Status == 1 && afterRelease[i].Status == 1 &&
                        afterRelease[i].Position.x - beforeRelease[i].Position.x > 0.005f)
                    {
                        restarted++;
                    }
                }

                Assert.That(report.GridCapacityFailures, Is.Zero);
                Assert.That(restarted, Is.GreaterThan(0),
                    "Removing the blocker did not restart any queued body within three fixed ticks.");
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator WallImpact_StaysInsideAndRecoversForwardMotion()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab wall impact test");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.WallImpact, 1701);
                yield return null;
                var initialState = runner.ReadSnapshot();
                // The preventive contact shell and gentle global propulsion avoid
                // penetration at the wall, then turn the row back into flow
                // gradually. Observe four seconds so this checks recovery,
                // not the old high-acceleration response time.
                var report = runner.RunFixedTicks(240, writeReport: true);
                var finalState = runner.ReadSnapshot();

                var correctedInward = 0;
                var resumedForward = 0;
                for (var i = 0; i < finalState.Length; i++)
                {
                    Assert.That(finalState[i].Status, Is.EqualTo(1u), $"Wall-impact body {i} was lost.");
                    if (finalState[i].Position.y < initialState[i].Position.y - 0.02f)
                    {
                        correctedInward++;
                    }
                    if (finalState[i].Position.x > initialState[i].Position.x + 1f)
                    {
                        resumedForward++;
                    }
                }

                Assert.That(report.GridCapacityFailures, Is.Zero);
                Assert.That(report.WalkabilityViolations, Is.Zero,
                    "A wall-impact body finished outside the navigable route.");
                Assert.That(report.PersistentDeepContactBodies, Is.Zero);
                Assert.That(report.DeepOverlapPairs, Is.Zero);
                Assert.That(correctedInward, Is.GreaterThanOrEqualTo(finalState.Length * 9 / 10),
                    "The wall constraint did not push the deliberately wall-bound bodies inward.");
                Assert.That(resumedForward, Is.GreaterThanOrEqualTo(finalState.Length * 9 / 10),
                    "Bodies did not recover from the wall impact into forward flow.");
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator InteractivePreview_InitializesStepsAndRendersPhysicalDisks()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab interactive preview smoke test");
            var target = new RenderTexture(960, 540, 24);
            var capture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                var camera = host.AddComponent<Camera>();
                camera.targetTexture = target;
                var runner = host.AddComponent<CrowdLabRunner>();
                var preview = host.AddComponent<CrowdLabPreview>();
                yield return null;

                preview.StepOneTick();
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                capture.Apply();
                RenderTexture.active = previous;

                var visiblePixels = 0;
                var pixels = capture.GetPixels32();
                for (var i = 0; i < pixels.Length; i += 32)
                {
                    if (pixels[i].r > 30 || pixels[i].g > 30 || pixels[i].b > 35) visiblePixels++;
                }

                var reportDirectory = Path.Combine("Logs", "CrowdRebuild");
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllBytes(Path.Combine(reportDirectory, "CrowdLab-preview-smoke.png"),
                    capture.EncodeToPNG());

                Assert.That(preview.Scenario, Is.EqualTo(CrowdLabScenario.WallImpact));
                Assert.That(runner.BodyCount, Is.EqualTo(32));
                Assert.That(runner.CurrentTick, Is.EqualTo(1));
                Assert.That(visiblePixels, Is.GreaterThan(100),
                    "The preview camera did not render the road and physical body disks.");
            }
            finally
            {
                target.Release();
                Object.Destroy(target);
                Object.Destroy(capture);
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator RightAngleBend_BuildsPressureAndMaintainsForwardFlow()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab right-angle bend gate");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.RightAngleBend, 1451);
                yield return null;
                var report = runner.RunFixedTicks(360, writeReport: true);
                var finalState = runner.ReadSnapshot();
                var throughBend = 0;
                for (var i = 0; i < finalState.Length; i++)
                {
                    if (finalState[i].Status == 1u && finalState[i].Position.y > -3.5f) throughBend++;
                }

                AssertStableCrowd(report);
                Assert.That(report.ActiveConstraints, Is.GreaterThan(20u),
                    "The test never developed contact pressure at the bend.");
                Assert.That(report.MaximumCellOccupancy, Is.GreaterThanOrEqualTo(3u),
                    "The test never became locally crowded.");
                Assert.That(report.MeanTravelSpeed, Is.GreaterThanOrEqualTo(0.8f));
                Assert.That(throughBend, Is.GreaterThanOrEqualTo(finalState.Length / 5),
                    "Too few bodies progressed through the right-angle turn.");
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator ThreeWayMerge_BuildsPressureAndSustainsOutletFlow()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab three-way merge gate");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.ThreeWayMerge, 761905);
                yield return null;
                var report = runner.RunFixedTicks(360, writeReport: true);
                var finalState = runner.ReadSnapshot();
                var inOutlet = 0;
                for (var i = 0; i < finalState.Length; i++)
                {
                    if (finalState[i].Status == 1u && finalState[i].Position.x > 4.5f) inOutlet++;
                }

                AssertStableCrowd(report);
                Assert.That(report.ActiveConstraints, Is.GreaterThan(100u),
                    "The three incoming streams never produced a crowded merge.");
                Assert.That(report.MaximumCellOccupancy, Is.GreaterThanOrEqualTo(4u));
                Assert.That(report.MeanTravelSpeed, Is.GreaterThanOrEqualTo(0.8f));
                Assert.That(inOutlet, Is.GreaterThanOrEqualTo(finalState.Length / 8),
                    "The crowded merge did not sustain flow into the outlet.");
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator BlockedExit_FormsAStableLocalQueueWithoutDeepPenetration()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab blocked-exit gate");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.BlockedExit, 8201);
                yield return null;
                runner.RunFixedTicks(300, writeReport: false);
                var queueStart = runner.ReadSnapshot();
                var report = runner.RunFixedTicks(120, writeReport: true);
                var queueEnd = runner.ReadSnapshot();
                var queued = 0;
                var slowQueued = 0;
                var retainedLocally = 0;
                for (var i = 0; i < queueEnd.Length; i++)
                {
                    if (!IsInBlockedExitQueue(queueEnd[i])) continue;
                    queued++;
                    if (queueEnd[i].Velocity.magnitude < 0.5f) slowQueued++;
                    if (IsInBlockedExitQueue(queueStart[i]) &&
                        Vector2.Distance(queueStart[i].Position, queueEnd[i].Position) < 1f)
                    {
                        retainedLocally++;
                    }
                }

                AssertStableCrowd(report);
                Assert.That(report.ActiveConstraints, Is.GreaterThan(50u),
                    "The blocked exit never developed queue pressure.");
                // With preventive contact skin, three physical bodies in one
                // compact-grid cell is already an intentionally dense queue.
                Assert.That(report.MaximumCellOccupancy, Is.GreaterThanOrEqualTo(3u));
                Assert.That(queued, Is.GreaterThanOrEqualTo(30),
                    "The blocker did not form a visible local queue.");
                Assert.That(slowQueued, Is.GreaterThanOrEqualTo(10),
                    "The queue was still being forced forward instead of settling.");
                Assert.That(retainedLocally, Is.GreaterThanOrEqualTo(10),
                    "The queue did not remain local during the observation window.");
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest]
        public IEnumerator ExplosionRecovery_PropagatesImpulseAndReturnsToStableFlow()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab explosion recovery gate");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.ExplosionRecovery, 9101);
                yield return null;
                var approachReport = runner.RunFixedTicks(90, writeReport: false);
                var beforeExplosion = runner.ReadSnapshot();
                var impulseReport = runner.RunFixedTicks(12, writeReport: false);
                var afterImpulse = runner.ReadSnapshot();
                var kickedOutward = 0;
                for (var i = 0; i < afterImpulse.Length; i++)
                {
                    var radial = beforeExplosion[i].Position;
                    if (radial.sqrMagnitude > 5.5f * 5.5f || radial.sqrMagnitude < 0.01f) continue;
                    radial.Normalize();
                    if (Vector2.Dot(afterImpulse[i].Position - beforeExplosion[i].Position, radial) > 0.02f)
                    {
                        kickedOutward++;
                    }
                }

                var recoveryReport = runner.RunFixedTicks(180, writeReport: true);
                AssertStableCrowd(approachReport);
                AssertStableCrowd(impulseReport);
                AssertStableCrowd(recoveryReport);
                // Preventive contact skin and stronger Jacobi relaxation clear
                // the packed disk centres quickly. Twenty simultaneous
                // constraints still proves that the impulse crossed the shared
                // contact system; the independent kicked-body assertion below
                // verifies that this pressure propagated through the group.
                Assert.That(impulseReport.ActiveConstraints, Is.GreaterThan(20u),
                    "The packed explosion scenario never developed contact pressure.");
                Assert.That(kickedOutward, Is.GreaterThanOrEqualTo(20),
                    "The explosion did not propagate an outward impulse through the packed group.");
                Assert.That(recoveryReport.RecoverySeconds, Is.GreaterThanOrEqualTo(0f));
                Assert.That(recoveryReport.RecoverySeconds, Is.LessThanOrEqualTo(3f),
                    "The crowd did not recover from the explosion within three seconds.");
                Assert.That(recoveryReport.MeanTravelSpeed, Is.GreaterThanOrEqualTo(0.8f));
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest, Explicit("10k lab performance/physics gate; requires the documented reference PC for acceptance timing.")]
        public IEnumerator TenThousandBodyStress_CompletesWithoutDeepContactsOrCapacityLoss()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab 10k stress test");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.StressTenThousand, 10001);
                yield return null;
                var report = runner.RunFixedTicks(120, writeReport: true);

                Assert.That(report.GridCapacityFailures, Is.Zero);
                Assert.That(report.WalkabilityViolations, Is.Zero);
                Assert.That(report.PersistentDeepContactBodies, Is.Zero);
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        [UnityTest, Explicit("Wide-field capacity/regression gate; records performance without a frame-rate requirement.")]
        public IEnumerator FiftyThousandBodyStress_CompletesWithoutCapacityLoss()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("GPU compute support is required for Crowd Lab.");
            }

            var host = new GameObject("Crowd Lab 50k stress test");
            var runner = host.AddComponent<CrowdLabRunner>();
            try
            {
                runner.Configure(CrowdLabScenario.StressFiftyThousand, 50001);
                yield return null;
                var report = runner.RunFixedTicks(60, writeReport: true);
                var finalState = runner.ReadSnapshot();

                Assert.That(report.GridCapacityFailures, Is.Zero);
                Assert.That(report.WalkabilityViolations, Is.Zero);
                Assert.That(finalState.Length, Is.EqualTo(50000));
                for (var i = 0; i < finalState.Length; i++)
                {
                    Assert.That(finalState[i].Status, Is.EqualTo(1u), $"Stress body {i} was lost.");
                }
            }
            finally
            {
                Object.Destroy(host);
            }
        }

        private static void AssertStableCrowd(CrowdLabReport report)
        {
            Assert.That(report.GridCapacityFailures, Is.Zero);
            Assert.That(report.WalkabilityViolations, Is.Zero);
            Assert.That(report.PersistentDeepContactBodies, Is.Zero);
            Assert.That(report.DeepContactDurationSeconds, Is.LessThanOrEqualTo(0.2f));
        }

        private static void AssertMatchingDiagnostics(
            CrowdLabReport baseline,
            CrowdLabReport candidate,
            string context)
        {
            Assert.That(candidate.ShallowContactPairs, Is.EqualTo(baseline.ShallowContactPairs), context);
            Assert.That(candidate.DeepOverlapPairs, Is.EqualTo(baseline.DeepOverlapPairs), context);
            Assert.That(candidate.DeepDiskContactPairs, Is.EqualTo(baseline.DeepDiskContactPairs), context);
            Assert.That(candidate.DeepBlockerContacts, Is.EqualTo(baseline.DeepBlockerContacts), context);
            Assert.That(candidate.DeepWallContacts, Is.EqualTo(baseline.DeepWallContacts), context);
            Assert.That(candidate.PersistentDeepContactBodies,
                Is.EqualTo(baseline.PersistentDeepContactBodies), context);
            Assert.That(candidate.MaximumPenetrationFraction,
                Is.EqualTo(baseline.MaximumPenetrationFraction), context);
            Assert.That(candidate.DeepContactDurationSeconds,
                Is.EqualTo(baseline.DeepContactDurationSeconds), context);
            Assert.That(candidate.ActiveConstraints, Is.EqualTo(baseline.ActiveConstraints), context);
            Assert.That(candidate.MaximumCellOccupancy, Is.EqualTo(baseline.MaximumCellOccupancy), context);
            Assert.That(candidate.GridCapacityFailures, Is.EqualTo(baseline.GridCapacityFailures), context);
            Assert.That(candidate.WalkabilityViolations, Is.EqualTo(baseline.WalkabilityViolations), context);
        }

        private static void AssertMatchingPhysicalState(
            GpuHordeSimulation.AgentState[] baseline,
            GpuHordeSimulation.AgentState[] candidate,
            string context)
        {
            // Crowd disks are physically interchangeable. Atomic compact-grid
            // scatter may exchange two otherwise identical bodies without
            // changing the physical state, so compare the position multiset
            // within each lifecycle state rather than assigning identity to a
            // contact permutation.
            for (uint status = 0u; status <= 3u; status++)
            {
                var baselinePositions = new List<Vector2>();
                var candidatePositions = new List<Vector2>();
                for (var body = 0; body < baseline.Length; body++)
                {
                    if (baseline[body].Status == status) baselinePositions.Add(baseline[body].Position);
                    if (candidate[body].Status == status) candidatePositions.Add(candidate[body].Position);
                }

                Assert.That(candidatePositions.Count, Is.EqualTo(baselinePositions.Count),
                    $"{context}, status {status} body count");
                baselinePositions.Sort(ComparePosition);
                candidatePositions.Sort(ComparePosition);
                for (var body = 0; body < baselinePositions.Count; body++)
                {
                    Assert.That(Vector2.Distance(candidatePositions[body], baselinePositions[body]),
                        Is.LessThan(0.001f), $"{context}, status {status} physical body {body}");
                }
            }
        }

        private static int ComparePosition(Vector2 first, Vector2 second)
        {
            var x = first.x.CompareTo(second.x);
            return x != 0 ? x : first.y.CompareTo(second.y);
        }

        private static bool IsInBlockedExitQueue(GpuHordeSimulation.AgentState state)
        {
            return state.Status == 1u && state.Position.x >= 0.5f && state.Position.x <= 4.4f &&
                   Mathf.Abs(state.Position.y) <= 1.5f;
        }
    }
}
