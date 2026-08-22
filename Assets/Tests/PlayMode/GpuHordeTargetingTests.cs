using System.Collections;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class GpuHordeTargetingTests
    {
        [UnityTest]
        public IEnumerator OverflowFallback_KeepsAllAgentsMovingAndBreaksThePile()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 96;
            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(count, field, mesh, out var simulation), Is.True);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            for (var i = 0; i < count; i++)
            {
                states[i] = State(-5f, 100f, -15f, false);
                controls[i] = new Vector4(4.8f, 1f, 0f, 0f);
            }

            simulation.SpawnBatch(states, count);
            for (var frame = 0; frame < 90; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, new Vector2[count]);
                if (frame % 15 == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            simulation.Dispose();
            var minLateral = float.PositiveInfinity;
            var maxLateral = float.NegativeInfinity;
            var averageForward = 0f;
            for (var i = 0; i < count; i++)
            {
                Assert.That(states[i].Status, Is.EqualTo(1));
                Assert.That(float.IsNaN(states[i].Position.x) || float.IsNaN(states[i].Position.y), Is.False);
                minLateral = Mathf.Min(minLateral, states[i].Position.y);
                maxLateral = Mathf.Max(maxLateral, states[i].Position.y);
                averageForward += states[i].Position.x;
            }

            averageForward /= count;
            Assert.That(maxLateral - minLateral, Is.GreaterThan(0.5f), "The overloaded pile did not spread laterally.");
            Assert.That(averageForward, Is.GreaterThan(-4f), "Overflow fallback stopped forward movement.");
        }

        [UnityTest]
        public IEnumerator ContactProjection_SeparatesOverlappingAgents()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(2, field, mesh, out var simulation), Is.True);
            var states = new[]
            {
                State(-5f, 10f, -15f, false),
                State(-4.8f, 10f, -14.8f, false)
            };
            var controls = new[]
            {
                new Vector4(0f, 1f, 0f, 0f),
                new Vector4(0f, 1f, 0f, 0f)
            };

            simulation.SpawnBatch(states, states.Length);
            for (var frame = 0; frame < 8; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, new Vector2[2]);
                yield return null;
            }

            simulation.ReadStatesSynchronous(states, states.Length);
            simulation.Dispose();
            Assert.That(
                Vector2.Distance(states[0].Position, states[1].Position),
                Is.GreaterThanOrEqualTo(0.68f),
                "GPU contact projection left two enemy bodies visibly intersecting.");
        }

        [UnityTest]
        public IEnumerator ActiveCompaction_ExcludesKilledSlotsFromDispatchList()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(2, field, mesh, out var simulation), Is.True);
            simulation.SpawnBatch(
                new[] { State(-5f, 10f, -15f, false), State(-3f, 10f, -13f, false) },
                2);
            simulation.QueueDamage(0, 20f);
            simulation.Dispatch(
                1f / 60f,
                new[] { new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f) },
                new Vector2[2]);
            var diagnostics = new uint[4];
            simulation.SynchronizeDiagnostics(diagnostics);
            simulation.Dispose();
            Assert.That(diagnostics[3], Is.EqualTo(1u));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RenderingLod_SplitsNearAndFarVisibleAgents()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var cameraObject = new GameObject("GPU LOD test camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 60f;
            camera.aspect = 1f;
            camera.transform.position = new Vector3(0f, 20f, 0f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var field = new HordeFlowField(
                new[] { new Vector3(-60f, 0f, 0f), new Vector3(60f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var detailedMesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(2, field, detailedMesh, out var simulation), Is.True);
            simulation.SpawnBatch(
                new[] { State(0f, 10f, -60f, false), State(40f, 10f, -20f, false) },
                2);
            simulation.Dispatch(
                1f / 60f,
                new[] { new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f) },
                new Vector2[2]);
            yield return null;

            var nearArgs = new uint[5];
            var farArgs = new uint[5];
            simulation.ReadDrawArgsSynchronous(nearArgs);
            simulation.ReadLodDrawArgsSynchronous(farArgs);
            simulation.Dispose();
            Object.DestroyImmediate(cameraObject);
            Assert.That(nearArgs[1], Is.EqualTo(1u));
            Assert.That(farArgs[1], Is.EqualTo(1u));
        }

        [Test]
        public void RenderingLod_KeepsTheSameVisibleBodyRadius()
        {
            var detailed = EnemyManager.GetDetailedEnemyMesh();
            var low = EnemyManager.GetLowEnemyMesh();
            Assert.That(detailed.bounds.extents.x, Is.EqualTo(1f).Within(0.06f));
            Assert.That(detailed.bounds.extents.z, Is.EqualTo(1f).Within(0.06f));
            Assert.That(low.bounds.extents.x, Is.EqualTo(detailed.bounds.extents.x).Within(0.06f));
            Assert.That(low.bounds.extents.z, Is.EqualTo(detailed.bounds.extents.z).Within(0.06f));
            Assert.That(low.triangles.Length / 3, Is.GreaterThan(8), "The far LOD regressed to the visibly angular octahedron.");
        }

        [UnityTest]
        public IEnumerator CameraDistance_DoesNotChangeSimulationResult()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var cameraObject = new GameObject("GPU simulation distance test camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 30f;
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(8, field, mesh, out var nearSimulation), Is.True);
            Assert.That(GpuHordeSimulation.TryCreate(8, field, mesh, out var farSimulation), Is.True);
            var nearStates = new GpuHordeSimulation.AgentState[8];
            var farStates = new GpuHordeSimulation.AgentState[8];
            var controls = new Vector4[8];
            for (var i = 0; i < nearStates.Length; i++)
            {
                nearStates[i] = State(-7f + i * 0.12f, 10f, -17f + i * 0.12f, false);
                farStates[i] = nearStates[i];
                controls[i] = new Vector4(4.8f, 1f, 0f, 0f);
            }

            nearSimulation.SpawnBatch(nearStates, nearStates.Length);
            farSimulation.SpawnBatch(farStates, farStates.Length);
            for (var tick = 0; tick < 90; tick++)
            {
                camera.transform.position = new Vector3(-5f, 20f, 0f);
                nearSimulation.Dispatch(1f / 60f, controls, new Vector2[8], false);
                camera.transform.position = new Vector3(200f, 20f, 200f);
                farSimulation.Dispatch(1f / 60f, controls, new Vector2[8], false);
            }

            nearSimulation.ReadStatesSynchronous(nearStates, nearStates.Length);
            farSimulation.ReadStatesSynchronous(farStates, farStates.Length);
            nearSimulation.Dispose();
            farSimulation.Dispose();
            Object.DestroyImmediate(cameraObject);
            for (var i = 0; i < nearStates.Length; i++)
            {
                Assert.That(Vector2.Distance(nearStates[i].Position, farStates[i].Position), Is.LessThan(0.001f));
                Assert.That(nearStates[i].Status, Is.EqualTo(farStates[i].Status));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator AreaEffect_UsesGpuGridAndRespectsRadiusAndTargetLimit()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(3, field, mesh, out var simulation), Is.True);
            var states = new[]
            {
                State(-5f, 10f, -15f, false),
                State(-4f, 10f, -14f, false),
                State(-1f, 10f, -11f, false)
            };
            var controls = new[]
            {
                new Vector4(0f, 1f, 0f, 0f),
                new Vector4(0f, 1f, 0f, 0f),
                new Vector4(0f, 1f, 0f, 0f)
            };
            simulation.SpawnBatch(states, states.Length);
            Assert.That(simulation.QueueAreaEffect(new Vector3(-5f, 0f, 0f), 1.5f, 20f, 0f, 2, 0f, 0f, 0), Is.True);
            simulation.Dispatch(1f / 60f, controls, new Vector2[3]);
            simulation.ReadStatesSynchronous(states, 3);
            simulation.Dispose();
            Assert.That(states[0].Status, Is.EqualTo(3));
            Assert.That(states[1].Status, Is.EqualTo(3));
            Assert.That(states[2].Status, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator StatusCommands_ApplySlowAndBurnWithoutCpuTicks()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(1, field, mesh, out var simulation), Is.True);
            var states = new[] { State(-5f, 10f, -15f, false) };
            var controls = new[] { new Vector4(0f, 1f, 0f, 0f) };
            simulation.SpawnBatch(states, 1);
            simulation.QueueStatus(0, 0.5f, 0.25f, 60f, 1f, 3);

            var sawBurnDeath = false;
            for (var frame = 0; frame < 30 && !sawBurnDeath; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, new Vector2[1]);
                if (frame == 0)
                {
                    simulation.ReadStatesSynchronous(states, 1);
                    Assert.That(states[0].SlowMultiplier, Is.EqualTo(0.5f).Within(0.001f));
                    Assert.That(states[0].SlowTimer, Is.GreaterThan(0f));
                    Assert.That(states[0].Health, Is.LessThan(10f));
                }

                yield return null;
                while (simulation.TryDequeueEvent(out var index, out var type))
                {
                    sawBurnDeath |= index == 0 && type == 1u;
                }
            }

            simulation.ReadStatesSynchronous(states, 1);
            simulation.Dispose();
            Assert.That(sawBurnDeath, Is.True);
            Assert.That(states[0].Status, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator GridDiagnostics_ReportCellOverflowAndDroppedEntries()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 112;
            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(count, field, mesh, out var simulation), Is.True);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            for (var i = 0; i < count; i++)
            {
                states[i] = State(0f, 10f, -10f, false);
                controls[i] = new Vector4(0f, 1f, 0f, 0f);
            }

            simulation.SpawnBatch(states, count);
            simulation.Dispatch(1f / 60f, controls, new Vector2[count]);
            var diagnostics = new uint[4];
            simulation.SynchronizeDiagnostics(diagnostics);
            simulation.Dispose();
            Assert.That(diagnostics[0], Is.EqualTo(1u));
            Assert.That(diagnostics[1], Is.EqualTo(16u));
            Assert.That(diagnostics[2], Is.EqualTo((uint)count));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Culling_WritesOnlyVisibleAgentsToIndirectDrawArgs()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var cameraObject = new GameObject("GPU culling test camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4f;
            camera.aspect = 1f;
            camera.transform.position = new Vector3(0f, 20f, 0f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var field = new HordeFlowField(
                new[] { new Vector3(-12f, 0f, 0f), new Vector3(12f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(2, field, mesh, out var simulation), Is.True);
            var states = new[] { State(0f, 10f, -12f, false), State(9f, 10f, -3f, false) };
            simulation.SpawnBatch(states, 2);
            simulation.Dispatch(
                1f / 60f,
                new[] { new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f) },
                new Vector2[2]);
            yield return null;

            var args = new uint[5];
            simulation.ReadDrawArgsSynchronous(args);
            simulation.Dispose();
            Object.DestroyImmediate(cameraObject);
            Assert.That(args[1], Is.EqualTo(1u));
        }

        [UnityTest]
        public IEnumerator DamageAndEscape_ReturnCompactGpuEvents()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var route = new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) };
            var field = new HordeFlowField(route, null, 2.31f, 0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(2, field, mesh, out var simulation), Is.True);
            var states = new[]
            {
                State(-5f, 20f, -15f, false),
                State(8.5f, 20f, -1.5f, false)
            };
            states[1].Velocity = new Vector2(4.8f, 0f);
            var controls = new[] { new Vector4(0f, 1f, 0f, 0f), new Vector4(4.8f, 1f, 0f, 0f) };
            simulation.SpawnBatch(states, states.Length);
            simulation.QueueDamage(0, 25f);
            simulation.Dispatch(1f / 60f, controls, new Vector2[2]);

            var sawDeath = false;
            var sawEscape = false;
            for (var frame = 0; frame < 30 && (!sawDeath || !sawEscape); frame++)
            {
                yield return null;
                while (simulation.TryDequeueEvent(out var index, out var type))
                {
                    sawDeath |= index == 0 && type == 1u;
                    sawEscape |= index == 1 && type == 2u;
                }


                simulation.Dispatch(1f / 60f, controls, new Vector2[2]);
            }

            simulation.ReadStatesSynchronous(states, states.Length);
            simulation.Dispose();
            Assert.That(sawDeath, Is.True, "The GPU did not return a compact death event.");
            Assert.That(sawEscape, Is.True, "The GPU did not return a compact escape event.");
            Assert.That(states[0].Status, Is.EqualTo(3));
            Assert.That(states[1].Status, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator BatchedQueries_SelectAllModesAndRespectFlyingFilter()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var route = new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) };
            var field = new HordeFlowField(route, null, 2.31f, 0.62f);
            var mesh = EnemyManager.GetDetailedEnemyMesh();
            Assert.That(GpuHordeSimulation.TryCreate(4, field, mesh, out var simulation), Is.True);
            var states = new[]
            {
                State(-8f, 10f, -100f, false),
                State(-5f, 50f, -50f, false),
                State(-2f, 30f, -10f, false),
                State(-4f, 999f, -25f, true)
            };
            var controls = new Vector4[4];
            var impulses = new Vector2[4];
            for (var i = 0; i < controls.Length; i++)
            {
                controls[i] = new Vector4(0f, 1f, 0f, 0f);
            }

            simulation.SpawnBatch(states, states.Length);
            var queryPosition = new Vector3(-6f, 0f, 0f);
            simulation.TryGetCachedTarget(queryPosition, 20f, false, TowerTargetingMode.Closest, out _);
            simulation.TryGetCachedTarget(queryPosition, 20f, false, TowerTargetingMode.First, out _);
            simulation.TryGetCachedTarget(queryPosition, 20f, false, TowerTargetingMode.Last, out _);
            simulation.TryGetCachedTarget(queryPosition, 20f, false, TowerTargetingMode.HighestHealth, out _);
            simulation.Dispatch(1f / 60f, controls, impulses);

            for (var frame = 0; frame < 30; frame++)
            {
                yield return null;
                if (simulation.TryGetCachedTarget(queryPosition, 20f, false, TowerTargetingMode.Closest, out var closest) &&
                    simulation.TryGetCachedTarget(queryPosition, 20f, false, TowerTargetingMode.First, out var first) &&
                    simulation.TryGetCachedTarget(queryPosition, 20f, false, TowerTargetingMode.Last, out var last) &&
                    simulation.TryGetCachedTarget(queryPosition, 20f, false, TowerTargetingMode.HighestHealth, out var strongest))
                {
                    simulation.Dispose();
                    Assert.That(closest, Is.EqualTo(1));
                    Assert.That(first, Is.EqualTo(2));
                    Assert.That(last, Is.EqualTo(0));
                    Assert.That(strongest, Is.EqualTo(1), "The flying unit must be excluded from ground-only queries.");
                    yield break;
                }
            }

            simulation.Dispose();
            Assert.Fail("GPU targeting results were not returned within 30 frames.");
        }

        [UnityTest]
        public IEnumerator DynamicBlocker_IsSelectedAndDestroyedEntirelyOnGpu()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            Assert.That(GpuHordeSimulation.TryCreate(1, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            var state = State(-5f, 100f, -15f, false);
            state.Mass = 1f;
            state.MaxHealth = 100f;
            state.AttackDamage = 20f;
            state.AttackInterval = 0.15f;
            state.WallDamageMultiplier = 1f;
            state.AlliedDamageMultiplier = 1f;
            simulation.SpawnBatch(new[] { state }, 1);
            var blocker = new TestCombatTarget(new Vector3(-4.55f, 0f, 0f), 10f);
            simulation.SynchronizeDynamicTargets(new ICombatTarget[] { blocker });
            simulation.Dispatch(1f / 60f, new[] { new Vector4(4.8f, 1f, 0f, 0f) }, new Vector2[1]);

            var sawDestroyed = false;
            for (var frame = 0; frame < 30 && !sawDestroyed; frame++)
            {
                yield return null;
                while (simulation.TryDequeueEvent(out _, out var type, out _, out _, out _))
                {
                    sawDestroyed |= type == 4u;
                }
            }

            simulation.Dispose();
            Assert.That(sawDestroyed, Is.True, "GPU melee did not emit the compact blocker-destroyed event.");
        }

        [UnityTest]
        public IEnumerator ProjectileKernel_ResolvesSegmentPierceOnGpu()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            Assert.That(GpuHordeSimulation.TryCreate(3, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            var states = new[] { State(-5f, 10f, -15f, false), State(-4f, 10f, -14f, false), State(-3f, 10f, -13f, false) };
            simulation.SpawnBatch(states, states.Length);
            Assert.That(simulation.QueueProjectile(
                new Vector3(-6f, 0f, 0f), new Vector3(-2f, 0f, 0f), 0.25f, 20f, 0f, 2, true, false), Is.True);
            simulation.Dispatch(1f / 60f, new[]
            {
                new Vector4(0f, 1f, 0f, 0f),
                new Vector4(0f, 1f, 0f, 0f),
                new Vector4(0f, 1f, 0f, 0f)
            }, new Vector2[3]);
            simulation.ReadStatesSynchronous(states, states.Length);
            simulation.Dispose();

            var deaths = 0;
            for (var i = 0; i < states.Length; i++)
            {
                deaths += states[i].Status == 3u ? 1 : 0;
            }
            Assert.That(deaths, Is.EqualTo(2), "The GPU projectile did not respect its pierce hit limit.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator BatchedAreaAndProjectileCommands_ApplyEveryQueuedCommand()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null,
                2.31f,
                0.62f);
            var controls = new[] { new Vector4(0f, 1f, 0f, 0f) };
            var impulses = new Vector2[1];
            var states = new[] { State(-5f, 10f, -15f, false) };

            Assert.That(GpuHordeSimulation.TryCreate(1, field, EnemyManager.GetDetailedEnemyMesh(), out var areaSimulation), Is.True);
            areaSimulation.SpawnBatch(states, 1);
            Assert.That(areaSimulation.QueueAreaEffect(new Vector3(-5f, 0f, 0f), 1f, 6f, 0f, 1, 0f, 0f, 1), Is.True);
            Assert.That(areaSimulation.QueueAreaEffect(new Vector3(-5f, 0f, 0f), 1f, 6f, 0f, 1, 0f, 0f, 1), Is.True);
            areaSimulation.Dispatch(1f / 60f, controls, impulses);
            areaSimulation.ReadStatesSynchronous(states, 1);
            areaSimulation.Dispose();
            Assert.That(states[0].Status, Is.EqualTo(3u), "The area batch skipped a queued command.");

            states[0] = State(-5f, 10f, -15f, false);
            Assert.That(GpuHordeSimulation.TryCreate(1, field, EnemyManager.GetDetailedEnemyMesh(), out var projectileSimulation), Is.True);
            projectileSimulation.SpawnBatch(states, 1);
            for (var i = 0; i < 2; i++)
            {
                Assert.That(projectileSimulation.QueueProjectile(
                    new Vector3(-6f, 0f, 0f), new Vector3(-4f, 0f, 0f), 0.25f, 6f, 0f, 1, true, false), Is.True);
            }
            projectileSimulation.Dispatch(1f / 60f, controls, impulses);
            projectileSimulation.ReadStatesSynchronous(states, 1);
            projectileSimulation.Dispose();
            Assert.That(states[0].Status, Is.EqualTo(3u), "The projectile batch skipped a queued command.");
            yield return null;
        }

        private sealed class TestCombatTarget : ICombatTarget
        {
            public TestCombatTarget(Vector3 position, float health)
            {
                Position = position;
                CurrentHealth = health;
                MaximumHealth = health;
            }

            public Vector3 Position { get; }
            public bool IsAlive => CurrentHealth > 0f;
            public CombatTargetKind TargetKind => CombatTargetKind.Barrier;
            public float CombatRadius => 0.6f;
            public float BlockCapacity => 20f;
            public float CurrentBlockedMass => 0f;
            public float CurrentHealth { get; private set; }
            public float MaximumHealth { get; }
            public float Armor => 0f;
            public float PhysicalResistance => 0f;
            public float FireResistance => 0f;
            public float SlowResistance => 0f;
            public float ThornsDamage => 0f;
            public bool TryAddBlocker(EnemyActor enemy) => true;
            public void RemoveBlocker(EnemyActor enemy) { }
            public void TakeDamage(float damage, EnemyActor source) => CurrentHealth -= damage;
            public void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition) =>
                CurrentHealth = destroyed ? 0f : authoritativeHealth;
        }

        private static GpuHordeSimulation.AgentState State(float x, float health, float progress, bool flying)
        {
            return new GpuHordeSimulation.AgentState
            {
                Position = new Vector2(x, 0f),
                Scale = 0.34f,
                Status = 1,
                Health = health,
                Progress = progress,
                Flags = flying ? 1u : 0u,
                SlowMultiplier = 1f
            };
        }
    }
}
