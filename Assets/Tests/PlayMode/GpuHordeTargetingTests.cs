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
        private int visualPreference;
        private bool hadVisualPreference;
        [SetUp]
        public void UseOriginalCollisionGeometry()
        {
            hadVisualPreference = PlayerPrefs.HasKey(GoblinPreviewAssets.PreferenceKey);
            visualPreference = PlayerPrefs.GetInt(GoblinPreviewAssets.PreferenceKey);
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 0);
        }

        [TearDown]
        public void RestoreVisualPreference()
        {
            if (hadVisualPreference) PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, visualPreference);
            else PlayerPrefs.DeleteKey(GoblinPreviewAssets.PreferenceKey);
        }

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
            var averageForwardVelocity = 0f;
            var averageLateralVelocity = 0f;
            for (var i = 0; i < count; i++)
            {
                Assert.That(states[i].Status, Is.EqualTo(1));
                Assert.That(float.IsNaN(states[i].Position.x) || float.IsNaN(states[i].Position.y), Is.False);
                minLateral = Mathf.Min(minLateral, states[i].Position.y);
                maxLateral = Mathf.Max(maxLateral, states[i].Position.y);
                averageForward += states[i].Position.x;
                averageForwardVelocity += Mathf.Max(0f, states[i].Velocity.x);
                averageLateralVelocity += Mathf.Abs(states[i].Velocity.y);
            }

            averageForward /= count;
            averageForwardVelocity /= count;
            averageLateralVelocity /= count;
            Assert.That(maxLateral - minLateral, Is.GreaterThan(0.5f), "The overloaded pile did not spread laterally.");
            Assert.That(averageForward, Is.GreaterThan(-4f), "Overflow fallback stopped forward movement.");
            Assert.That(
                averageForwardVelocity,
                Is.GreaterThan(averageLateralVelocity * 1.5f),
                "Dense pressure turned the crowd into a sideways-moving wave.");
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
            for (var frame = 0; frame < 60; frame++)
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
        public IEnumerator SpriteTorsoContacts_PackWithoutTransparentCardGap()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            var field = new HordeFlowField(
                new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) },
                null, 3f, 0.62f);
            foreach (var secondScale in new[] { 0.203f, 0.29f })
            {
                Assert.That(GpuHordeSimulation.TryCreate(2, field,
                    EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
                using (simulation)
                {
                    var profile = CrowdSolverProfile.Default;
                    simulation.SetCrowdSolverProfile(profile);
                    simulation.SetCollisionDiameter(profile.bodyDiameterPerEnemyScale *
                        0.7f * profile.bodyRadiusPerVisualScale);
                    simulation.SetXpbdPerBodyRadius(true);
                    var scaleSum = 0.203f + secondScale;
                    var initialHalfSpacing = scaleSum * profile.bodyRadiusPerVisualScale * 0.45f;
                    var states = new[] { State(-initialHalfSpacing, 100f, 0f, false), State(initialHalfSpacing, 100f, 0f, false) };
                    states[0].Scale = 0.203f;
                    states[1].Scale = secondScale;
                    states[0].Velocity = states[1].Velocity = Vector2.zero;
                    var controls = new[] { new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f) };
                    simulation.SpawnBatch(states, controls, 0, states.Length);
                    simulation.Dispatch(profile.FixedStep, null, null, false);
                    simulation.ReadStatesSynchronous(states, states.Length);
                    var spacing = Vector2.Distance(states[0].Position, states[1].Position);
                    Assert.That(spacing, Is.GreaterThanOrEqualTo(scaleSum * profile.bodyRadiusPerVisualScale * 0.95f),
                        "Torso contacts must still resolve physical overlap.");
                    Assert.That(spacing, Is.LessThanOrEqualTo(scaleSum * 0.72f),
                        "Transparent card padding must not leave a gap between painted bodies.");
                    Assert.That(states[0].Scale, Is.EqualTo(0.203f));
                    Assert.That(states[1].Scale, Is.EqualTo(secondScale), "Packing must not resize the artwork.");
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator LargeGridScan_PreservesEveryBodyAcrossScanChunks()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            var field = HordeFlowField.CreateOpenField(Vector3.zero, new Vector2(200f, 200f),
                new Vector3(-99f, 0f, 0f), new Vector3(99f, 0f, 0f), 50f, 0.5f);
            var cellCount = field.Width * field.Height;
            Assert.That(cellCount, Is.GreaterThan(256 * 256 * 2), "Exercise more than two scan chunks.");
            Assert.That(GpuHordeSimulation.TryCreate(4, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
            using (simulation)
            {
                var states = new[] { State(-90f, 100f, 0f, false), State(90f, 100f, 0f, false),
                    State(-90f, 100f, 0f, false), State(90f, 100f, 0f, false) };
                states[0].Position.y = states[1].Position.y = -90f;
                states[2].Position.y = states[3].Position.y = 90f;
                var controls = new Vector4[4];
                for (var i = 0; i < 4; i++) controls[i] = new Vector4(0f, 1f, 0f, 0f);
                simulation.SpawnBatch(states, controls, 0, 4);
                simulation.DispatchFixedTick(null, null, requestReadback: false);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var counts = new uint[cellCount];
                var offsets = new uint[cellCount + 1];
                var agents = new uint[4];
                ((GraphicsBuffer)typeof(GpuHordeSimulation).GetField("xpbdCellCounts", flags).GetValue(simulation)).GetData(counts);
                ((GraphicsBuffer)typeof(GpuHordeSimulation).GetField("xpbdCellOffsets", flags).GetValue(simulation)).GetData(offsets);
                ((GraphicsBuffer)typeof(GpuHordeSimulation).GetField("xpbdCellAgents", flags).GetValue(simulation)).GetData(agents);
                uint sum = 0;
                for (var cell = 0; cell < cellCount; cell++)
                {
                    if (offsets[cell] != sum) Assert.Fail($"Cell {cell}: expected offset {sum}, got {offsets[cell]}.");
                    sum += counts[cell];
                }
                Assert.That(sum, Is.EqualTo(4));
                Assert.That(offsets[cellCount], Is.EqualTo(4));
                CollectionAssert.AreEquivalent(new uint[] { 0, 1, 2, 3 }, agents,
                    "Chunk carry must not overwrite or lose spatial-grid membership.");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator HardContactIterations_SeparateDenseMixedSizeGroup()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int side = 5;
            const int count = side * side;
            var field = new HordeFlowField(
                new[] { new Vector3(-12f, 0f, 0f), new Vector3(12f, 0f, 0f) },
                null,
                4f,
                0.5f);
            Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var index = y * side + x;
                    states[index] = State(-2f + x * 0.25f, 10f, -10f, false);
                    states[index].Position.y = -0.5f + y * 0.25f;
                    states[index].Scale = index % 3 == 0 ? 0.493f : 0.34f;
                    states[index].Mass = 1f;
                    controls[index] = new Vector4(0f, 1f, 0f, 0f);
                }
            }

            simulation.SpawnBatch(states, count);
            for (var frame = 0; frame < 90; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, new Vector2[count], false);
                if (frame % 15 == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            simulation.Dispose();
            for (var i = 0; i < count; i++)
            {
                for (var j = i + 1; j < count; j++)
                {
                    var requiredDistance = (states[i].Scale + states[j].Scale) * 1.035f;
                    Assert.That(Vector2.Distance(states[i].Position, states[j].Position),
                        Is.GreaterThanOrEqualTo(requiredDistance - 0.015f),
                        $"Dense GPU bodies {i} and {j} still visibly overlap.");
                }
            }
        }

        [UnityTest]
        public IEnumerator DenseQueue_FlowsThroughNarrowRightAngleWithoutDeadlock()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 20;
            var field = new HordeFlowField(
                new[]
                {
                    new Vector3(0f, 0f, 10f),
                    Vector3.zero,
                    new Vector3(20f, 0f, 0f)
                },
                null,
                0.85f,
                0.4f);
            Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            for (var i = 0; i < count; i++)
            {
                var row = i / 2;
                states[i] = State((i & 1) == 0 ? -0.34f : 0.34f, 100f, -20f + row * 0.7f, false);
                states[i].Position.y = 0.8f + row * 0.7f;
                states[i].Velocity = new Vector2(0f, -4.8f);
                states[i].Mass = 1f;
                controls[i] = new Vector4(4.8f, 1f, 0f, 0f);
            }

            simulation.SpawnBatch(states, count);
            for (var frame = 0; frame < 180; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, new Vector2[count], false);
                if ((frame & 15) == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            simulation.Dispose();
            var passedCorner = 0;
            for (var i = 0; i < count; i++)
            {
                passedCorner += states[i].Status == 2u || states[i].Position.x > 2f ? 1 : 0;
                for (var j = i + 1; j < count; j++)
                {
                    if (states[i].Status == 1u && states[j].Status == 1u)
                    {
                        Assert.That(Vector2.Distance(states[i].Position, states[j].Position),
                            Is.GreaterThanOrEqualTo(0.68f));
                    }
                }
            }

            Assert.That(passedCorner, Is.GreaterThanOrEqualTo(4),
                "The strict queue constraint deadlocked at a narrow right-angle turn.");
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
        public IEnumerator TargetedDamage_DoesNotKillTwelveHealthEnemyOrHitNeighbor()
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
            Assert.That(GpuHordeSimulation.TryCreate(2, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            var states = new[]
            {
                State(-5f, 12f, -15f, false),
                State(-4f, 12f, -14f, false)
            };
            simulation.SpawnBatch(states, states.Length);
            simulation.QueueDamage(0, 10.9f);
            simulation.Dispatch(
                1f / 60f,
                new[] { new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f) },
                new Vector2[2]);
            simulation.ReadStatesSynchronous(states, states.Length);
            simulation.Dispose();

            Assert.That(states[0].Status, Is.EqualTo(1u));
            Assert.That(states[0].Health, Is.EqualTo(1.1f).Within(0.001f));
            Assert.That(states[1].Status, Is.EqualTo(1u));
            Assert.That(states[1].Health, Is.EqualTo(12f).Within(0.001f));
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
            Assert.That(nearArgs[0], Is.EqualTo(farArgs[0]), "Near/far switching changes mesh topology and can visibly pop across the crowd.");
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
            Assert.That(low.triangles.Length / 3, Is.EqualTo(120), "The unified horde mesh no longer matches the balanced visual-quality tier.");
            var vertices = low.vertices;
            var normals = low.normals;
            var outwardNormalScore = 0f;
            for (var i = 0; i < vertices.Length; i++)
            {
                var radial = vertices[i] - Vector3.up;
                if (radial.sqrMagnitude > 0.001f)
                {
                    outwardNormalScore += Vector3.Dot(normals[i], radial.normalized);
                }
            }

            Assert.That(outwardNormalScore, Is.GreaterThan(0f), "The far LOD mesh is inside-out and will look transparent with back-face culling.");
        }

        [Test]
        public void HordeShader_ExposesRareEnemyColor()
        {
            var shader = Resources.Load<Shader>("HordeIndirect");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            Assert.That(material.HasProperty("_RareColor"), Is.True);
            Assert.That(material.HasProperty("_PackingScale"), Is.True);
            Assert.That(material.HasProperty("_VisualScaleMultiplier"), Is.True);
            Assert.That(material.GetFloat("_PackingScale"), Is.EqualTo(1.04f).Within(0.001f));
            Object.DestroyImmediate(material);
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
        public IEnumerator AgentInExitCell_EmitsOneEscapeAndStaysRetiredAcrossBufferSwaps()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            var field = new HordeFlowField(new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) }, null, 2.31f, 0.62f);
            Assert.That(field.HasReachedExit(new Vector3(10f, 0f, 0f)), Is.True);
            Assert.That(GpuHordeSimulation.TryCreate(1, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
            using (simulation)
            {
                var states = new[] { State(10f, 100f, 0f, false) };
                simulation.SpawnBatch(states, new[] { new Vector4(4f, 1f, 0f, 0f) }, 0, 1);
                var escapes = 0;
                var deadline = Time.realtimeSinceStartup + 0.5f;
                do
                {
                    simulation.Dispatch(1f / 60f, null, null);
                    yield return null;
                    while (simulation.TryDequeueEvent(out var index, out var type))
                        if (index == 0 && type == 2u) escapes++;
                } while (Time.realtimeSinceStartup < deadline);
                simulation.ReadStatesSynchronous(states, 1);
                var diagnostics = new uint[7];
                simulation.SynchronizeDiagnostics(diagnostics);
                Assert.That(escapes, Is.EqualTo(1), "An arrival before movement must forward one escape, including across empty active-grid swaps.");
                Assert.That(states[0].Status, Is.EqualTo(2u), "A retired body reappeared from a stale ping-pong buffer.");
                Assert.That(diagnostics[3], Is.Zero, "An escaped body still occupies the active collision grid.");
            }
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
        public IEnumerator DynamicBlocker_RemainsAHardPhysicalObstacleWhileAlive()
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
            state.Velocity = new Vector2(4.8f, 0f);
            state.Mass = 1f;
            state.MaxHealth = 100f;
            state.AttackDamage = 1f;
            state.AttackInterval = 0.15f;
            state.WallDamageMultiplier = 1f;
            simulation.SpawnBatch(new[] { state }, 1);
            var blocker = new TestCombatTarget(new Vector3(-3f, 0f, 0f), 10000f);
            var controls = new[] { new Vector4(4.8f, 1f, 0f, 0f) };
            var impulses = new Vector2[1];

            for (var frame = 0; frame < 150; frame++)
            {
                simulation.SynchronizeDynamicTargets(new ICombatTarget[] { blocker });
                simulation.Dispatch(1f / 60f, controls, impulses, requestReadback: false);
                if (frame % 15 == 0)
                {
                    yield return null;
                }
            }

            var states = new GpuHordeSimulation.AgentState[1];
            simulation.ReadStatesSynchronous(states, 1);
            simulation.Dispose();
            var blockerPosition = new Vector2(blocker.Position.x, blocker.Position.z);
            var contactDistance = Vector2.Distance(states[0].Position, blockerPosition);
            Assert.That(states[0].Position.x, Is.LessThan(blocker.Position.x),
                "The GPU entity crossed through the living barricade.");
            Assert.That(contactDistance, Is.GreaterThanOrEqualTo(blocker.CombatRadius + states[0].Scale - 0.025f),
                "The GPU entity occupied the barricade's physical body.");
        }

        [UnityTest]
        public IEnumerator OrientedBarricade_ContactsAtVisibleDepthAndAllowsSideBypass()
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
            const int count = 2;
            Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            var states = new[]
            {
                State(-5f, 100f, -15f, false),
                State(-5f, 100f, -15f, false)
            };
            states[0].Position.y = 0f;
            states[1].Position.y = 1.2f;
            var controls = new[]
            {
                new Vector4(4.8f, 1f, 0f, 0f),
                new Vector4(4.8f, 1f, 0f, 0f)
            };
            simulation.SpawnBatch(states, count);
            var blocker = new TestCombatTarget(
                new Vector3(-3f, 0f, 0f), 100000f, 100f,
                Vector3.forward, 0.675f, 0.26f);

            for (var frame = 0; frame < 150; frame++)
            {
                simulation.SynchronizeDynamicTargets(new ICombatTarget[] { blocker });
                simulation.Dispatch(1f / 60f, controls, new Vector2[count], requestReadback: false);
                if ((frame & 15) == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            simulation.Dispose();

            var expectedContactX = blocker.Position.x - (blocker.CombatHalfDepth + states[0].Scale * 1.045f);
            Assert.That(states[0].Position.x, Is.EqualTo(expectedContactX).Within(0.06f),
                "The front enemy stopped with a visible gap from the barricade.");
            Assert.That(states[1].Position.x, Is.GreaterThan(blocker.Position.x + 1f),
                "An enemy beside the barricade was incorrectly treated as blocked.");
        }

        [UnityTest]
        public IEnumerator DenseQueue_AtBarricade_PreservesEnemyBodySpacing()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 24;
            var field = new HordeFlowField(
                new[] { new Vector3(-12f, 0f, 0f), new Vector3(12f, 0f, 0f) },
                null,
                0.72f,
                0.62f);
            Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            var impulses = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var row = i / 2;
                states[i] = State(-10f + row * 0.71f, 100f, -22f + row * 0.71f, false);
                states[i].Position.y = (i & 1) == 0 ? -0.355f : 0.355f;
                states[i].Velocity = new Vector2(4.8f, 0f);
                states[i].Mass = 1f;
                states[i].MaxHealth = 100f;
                states[i].AttackDamage = 1f;
                states[i].AttackInterval = 0.15f;
                states[i].WallDamageMultiplier = 1f;
                controls[i] = new Vector4(4.8f, 1f, 0f, 0f);
            }

            simulation.SpawnBatch(states, count);
            var blocker = new TestCombatTarget(new Vector3(-1f, 0f, 0f), 100000f, 100f);
            for (var frame = 0; frame < 120; frame++)
            {
                simulation.SynchronizeDynamicTargets(new ICombatTarget[] { blocker });
                simulation.Dispatch(1f / 60f, controls, impulses, requestReadback: false);
                if ((frame & 15) == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            simulation.Dispose();

            var minimumDistance = float.MaxValue;
            var minimumX = float.PositiveInfinity;
            var maximumX = float.NegativeInfinity;
            for (var i = 0; i < count; i++)
            {
                minimumX = Mathf.Min(minimumX, states[i].Position.x);
                maximumX = Mathf.Max(maximumX, states[i].Position.x);
                for (var j = i + 1; j < count; j++)
                {
                    minimumDistance = Mathf.Min(minimumDistance,
                        Vector2.Distance(states[i].Position, states[j].Position));
                }
            }

            Assert.That(minimumDistance, Is.GreaterThanOrEqualTo(0.70f),
                $"Dense barricade queue compressed overlapping bodies to {minimumDistance:F3}m.");
            // This 1.44m-wide center domain can hold THREE 0.70m-spaced
            // columns. Requiring twelve two-column rows rejects a valid denser
            // arrangement; retain the all-pairs body-spacing assertion above.
            var minimumRows = Mathf.CeilToInt(count / 3f);
            Assert.That(maximumX - minimumX, Is.GreaterThanOrEqualTo((minimumRows - 1) * 0.70f),
                "The blocked crowd compressed beyond the corridor's packing capacity.");
        }

        [UnityTest]
        public IEnumerator PackedGoblinQueue_StartsTogetherOnFirstTickAfterBlockerRemoval()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            const int count = 24;
            var field = new HordeFlowField(
                new[] { new Vector3(-12f, 0f, 0f), new Vector3(12f, 0f, 0f) },
                null,
                0.95f,
                0.31f);
            Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
            using (simulation)
            {
                simulation.SetCollisionDiameter(0.29f);
                var states = new GpuHordeSimulation.AgentState[count];
                var controls = new Vector4[count];
                for (var i = 0; i < count; i++)
                {
                    states[i] = State(-7f + (i / 3) * 0.65f, 100f, 0f, false);
                    states[i].Position.y = ((i % 3) - 1) * 0.63f;
                    states[i].Scale = 0.29f;
                    states[i].Mass = 1f;
                    states[i].Facing = Vector2.right;
                    states[i].AttackDamage = 1f;
                    states[i].AttackInterval = 0.15f;
                    states[i].WallDamageMultiplier = 1f;
                    controls[i] = new Vector4(3.84f, 1f, 0f, 0f);
                }
                simulation.SpawnBatch(states, controls, 0, count);
                var blocker = new TestCombatTarget(Vector3.zero, 1000000f, 1000f,
                    Vector3.forward, 2f, 0.15f);
                var blockerList = new ICombatTarget[] { blocker };
                for (var frame = 0; frame < 300; frame++)
                {
                    simulation.SynchronizeDynamicTargets(blockerList);
                    simulation.Dispatch(1f / 60f, null, null, false);
                    if ((frame & 31) == 0) yield return null;
                }
                simulation.ReadStatesSynchronous(states, count);
                var stopped = new Vector2[count];
                var stoppedGait = new float[count];
                for (var i = 0; i < count; i++)
                {
                    stopped[i] = states[i].Position;
                    stoppedGait[i] = states[i].AnimationDistance;
                }

                simulation.SynchronizeDynamicTargets(System.Array.Empty<ICombatTarget>());
                simulation.Dispatch(1f / 60f, null, null, false);
                simulation.ReadStatesSynchronous(states, count);
                var moved = 0;
                var runningVisuals = 0;
                var minimumTravel = float.PositiveInfinity;
                for (var i = 0; i < count; i++)
                {
                    var travel = states[i].Position.x - stopped[i].x;
                    minimumTravel = Mathf.Min(minimumTravel, travel);
                    if (travel > 0.003f) moved++;
                    if (states[i].AnimationDistance > stoppedGait[i]) runningVisuals++;
                    Assert.That(states[i].LocomotionSpeed,
                        Is.EqualTo(Vector2.Distance(states[i].Position, stopped[i]) * 60f).Within(0.001f),
                        "The gait speed must reflect actual travel, including a slow first step.");
                }
                var metrics = $"Queue release first tick: moved={moved}/{count}, running={runningVisuals}/{count}, min={minimumTravel:F5}m";
                LogAssert.Expect(LogType.Log, metrics);
                Debug.Log(metrics);
                Assert.That(moved, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(count * 0.95f)),
                    "The released crowd must start as one body; a contact-bound edge body may fan sideways first.");
                Assert.That(runningVisuals, Is.EqualTo(count), "The brace-to-run presentation must react on the first moving tick.");

                var released = new Vector2[count];
                for (var i = 0; i < count; i++) released[i] = states[i].Position;
                // Exercise four-tick catch-up frames as well as the immediate
                // normal-speed release above. Spacing must survive presentation
                // on the final tick without polishing every intermediate tick.
                for (var frame = 0; frame < 120; frame++)
                    simulation.Dispatch(1f / 60f, null, null, false,
                        contactIterationScale: 0.5f, updatePresentation: (frame & 3) == 3);
                simulation.ReadStatesSynchronous(states, count);
                var meanForwardSpeed = 0f;
                var minimumForwardSpeed = float.PositiveInfinity;
                for (var i = 0; i < count; i++)
                {
                    var speed = (states[i].Position.x - released[i].x) / 2f;
                    meanForwardSpeed += speed / count;
                    minimumForwardSpeed = Mathf.Min(minimumForwardSpeed, speed);
                }
                var diagnostics = new uint[7];
                simulation.SynchronizeDiagnostics(diagnostics);
                Assert.That(diagnostics[5], Is.Zero,
                    $"A freely moving queue must preserve body spacing; measured {diagnostics[5]} collision pairs.");
                Assert.That(meanForwardSpeed, Is.GreaterThan(1.5f),
                    $"The released queue only averaged {meanForwardSpeed:F2}m/s.");
                Assert.That(minimumForwardSpeed, Is.GreaterThan(0.5f),
                    $"At least one free row remained nearly stopped at {minimumForwardSpeed:F2}m/s.");
            }
            LogAssert.NoUnexpectedReceived();
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
        public IEnumerator MovingProjectile_KeepsOneHitBudgetAcrossTravelSegments()
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
            var states = new[]
            {
                State(-5f, 10f, -15f, false),
                State(-4f, 10f, -14f, false),
                State(-3f, 10f, -13f, false)
            };
            var controls = new[]
            {
                new Vector4(0f, 1f, 0f, 0f),
                new Vector4(0f, 1f, 0f, 0f),
                new Vector4(0f, 1f, 0f, 0f)
            };
            simulation.SpawnBatch(states, states.Length);
            var projectileId = simulation.CreateProjectileId();
            Assert.That(simulation.QueueProjectile(
                new Vector3(-6f, 0f, 0f), new Vector3(-4.5f, 0f, 0f), 0.2f, 20f, 0f, 2, true, false,
                projectileId: projectileId, resetHitCount: true), Is.True);
            simulation.Dispatch(1f / 60f, controls, new Vector2[3]);
            Assert.That(simulation.QueueProjectile(
                new Vector3(-4.5f, 0f, 0f), new Vector3(-2f, 0f, 0f), 0.2f, 20f, 0f, 2, true, false,
                projectileId: projectileId, resetHitCount: false), Is.True);
            simulation.Dispatch(1f / 60f, controls, new Vector2[3]);
            simulation.ReadStatesSynchronous(states, states.Length);
            simulation.Dispose();

            var deaths = 0;
            for (var i = 0; i < states.Length; i++)
            {
                deaths += states[i].Status == 3u ? 1 : 0;
            }
            Assert.That(deaths, Is.EqualTo(2), "The moving projectile exceeded or reset its total pierce budget.");
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

        [UnityTest]
        public IEnumerator AreaKnockback_PreservesTheExternalImpulsePastMovementClamp()
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
            var states = new[] { State(-4.5f, 100f, -14.5f, false) };
            Assert.That(GpuHordeSimulation.TryCreate(1, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            simulation.SpawnBatch(states, 1);
            Assert.That(simulation.QueueAreaEffect(
                new Vector3(-5f, 0f, 0f), 1.75f, 0f, 12f, 1, 0f, 0f, 1), Is.True);

            simulation.Dispatch(1f / 60f, new[] { new Vector4(0f, 1f, 0f, 0f) }, new Vector2[1]);
            simulation.ReadStatesSynchronous(states, 1);
            simulation.Dispose();

            Assert.That(states[0].Padding, Is.GreaterThan(0f));
            Assert.That(states[0].KnockbackVelocity.x, Is.GreaterThan(10f),
                "The per-tick travel cap swallowed the remaining external impulse.");
            Assert.That(states[0].Position.x + 4.5f, Is.InRange(0.115f, 0.125f),
                "The strong area knockback did not use the configured XPBD travel cap.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AreaKnockback_TransfersThroughMassWithoutAgentTunnelling()
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
            const int count = 6;
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            var initialLastX = -5f + (count - 1) * 0.69f;
            for (var i = 0; i < count; i++)
            {
                states[i] = State(-5f + i * 0.69f, 100f, -15f + i * 0.69f, false);
                states[i].Mass = i == 3 ? 3f : 1f;
                controls[i] = new Vector4(0f, 1f, 0f, 0f);
            }

            Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetDetailedEnemyMesh(), out var simulation), Is.True);
            simulation.SpawnBatch(states, count);
            Assert.That(simulation.QueueAreaEffect(
                new Vector3(-5f, 0f, 0f), 0.3f, 0f, 12f, 1, 0f, 0f, 1), Is.True);

            for (var frame = 0; frame < 45; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, new Vector2[count]);
                if ((frame & 7) == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            simulation.Dispose();

            Assert.That(states[^1].Position.x, Is.GreaterThan(initialLastX + 0.12f),
                "The contact impulse did not propagate through the enemy chain.");
            for (var i = 1; i < count; i++)
            {
                Assert.That(states[i].Position.x, Is.GreaterThan(states[i - 1].Position.x),
                    $"Enemy {i - 1} tunnelled through enemy {i}.");
                Assert.That(states[i].Position.x - states[i - 1].Position.x, Is.GreaterThan(0.42f),
                    $"Enemies {i - 1} and {i} lost their physical collision spacing.");
            }
        }

        [UnityTest]
        public IEnumerator ArenaShockwave_ReleasesTheWallRowBackTowardCenter()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 256;
            const float arenaRadius = 12f;
            var field = new HordeFlowField(
                new[] { Vector3.zero, Vector3.zero },
                null,
                arenaRadius,
                0.45f);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            for (var i = 0; i < count; i++)
            {
                var normalizedRadius = (i + 0.5f) / count;
                var radius = Mathf.Lerp(1.1f, 3.4f, Mathf.Sqrt(normalizedRadius));
                var angle = i * 2.3999632f;
                states[i] = State(0f, 100f, 0f, false);
                states[i].Position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                states[i].Mass = i % 5 == 0 ? 3f : 1f;
                controls[i] = new Vector4(i % 5 == 0 ? 2.3f : 5.35f, 1f, 0f, 0f);
            }

            Assert.That(GpuHordeSimulation.TryCreate(
                count,
                field,
                EnemyManager.GetDetailedEnemyMesh(),
                out var simulation), Is.True);
            simulation.SetCollisionDiameter(0.5f);
            simulation.SetStationaryArena(true, centerSeeking: true, center: Vector3.zero);
            simulation.SpawnBatch(states, count);
            Assert.That(simulation.QueueAreaEffect(
                Vector3.zero,
                1f,
                0f,
                16f,
                0,
                0f,
                0f,
                1,
                knockbackPropagationRadius: arenaRadius), Is.True);

            var sawRiding = new bool[count];
            var releaseFrames = new int[count];
            var peakOverlapPairs = 0;
            var peakOverlapFraction = 0f;
            System.Array.Fill(releaseFrames, -1);
            for (var frame = 0; frame < 300; frame++)
            {
                simulation.Dispatch(
                    1f / 60f,
                    controls,
                    new Vector2[count],
                    requestReadback: false,
                    contactIterationScale: 1f);
                if ((frame & 3) == 0)
                {
                    simulation.ReadStatesSynchronous(states, count);
                    var sampledOverlapPairs = 0;
                    for (var i = 0; i < count; i++)
                    {
                        var activeShockwaveState = Mathf.Abs(states[i].ShockwaveOutwardTimer) > 0.0001f;
                        if (activeShockwaveState)
                        {
                            sawRiding[i] = true;
                        }
                        else if ((sawRiding[i] || states[i].ShockwaveExposure > 0.05f) &&
                            releaseFrames[i] < 0)
                        {
                            sawRiding[i] = true;
                            releaseFrames[i] = frame;
                        }

                        for (var other = i + 1; other < count; other++)
                        {
                            const float pairDiameter = 0.72f;
                            var distance = Vector2.Distance(states[i].Position, states[other].Position);
                            var overlapFraction = Mathf.Clamp01((pairDiameter - distance) / pairDiameter);
                            if (overlapFraction <= 0.005f)
                            {
                                continue;
                            }

                            sampledOverlapPairs++;
                            peakOverlapFraction = Mathf.Max(peakOverlapFraction, overlapFraction);
                        }
                    }
                    peakOverlapPairs = Mathf.Max(peakOverlapPairs, sampledOverlapPairs);
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            simulation.Dispose();
            var pinnedCount = 0;
            var ridingCount = 0;
            var wallCount = 0;
            var inwardMovingWallCount = 0;
            var wallInwardSpeed = 0f;
            var earliestReleaseFrame = int.MaxValue;
            var latestReleaseFrame = int.MinValue;
            var releasedCount = 0;
            for (var i = 0; i < count; i++)
            {
                if (releaseFrames[i] >= 0)
                {
                    earliestReleaseFrame = Mathf.Min(earliestReleaseFrame, releaseFrames[i]);
                    latestReleaseFrame = Mathf.Max(latestReleaseFrame, releaseFrames[i]);
                    releasedCount++;
                }

                var radius = states[i].Position.magnitude;
                if (states[i].ShockwaveOutwardTimer < -0.0001f)
                {
                    pinnedCount++;
                }
                else if (states[i].ShockwaveOutwardTimer > 0.0001f)
                {
                    ridingCount++;
                }

                var worldPosition = new Vector3(states[i].Position.x, 0f, states[i].Position.y);
                var wallSample = field.GetWallRepulsion(worldPosition, 100f).magnitude;
                var boundaryDistance = wallSample > 0f ? 100f * (1f - wallSample) : 100f;
                if (boundaryDistance >= 0.8f)
                {
                    continue;
                }

                wallCount++;
                var inward = radius > 0.0001f ? -states[i].Position / radius : Vector2.zero;
                var inwardSpeed = Vector2.Dot(states[i].Velocity, inward);
                wallInwardSpeed += inwardSpeed;
                if (inwardSpeed > 0.15f)
                {
                    inwardMovingWallCount++;
                }
            }

            TestContext.WriteLine(
                $"wall={wallCount}, pinned={pinnedCount}, riding={ridingCount}, " +
                $"inwardWall={inwardMovingWallCount}, averageWallInward=" +
                $"{(wallCount > 0 ? wallInwardSpeed / wallCount : 0f):0.000}, " +
                $"released={releasedCount}, releaseWindow=" +
                $"{(releasedCount > 0 ? latestReleaseFrame - earliestReleaseFrame : -1)} frames, " +
                $"sampledOverlapPairs={peakOverlapPairs}, deepest={peakOverlapFraction:P1}");

            Assert.That(pinnedCount, Is.EqualTo(0), "Shockwave enemies remained pinned after recovery.");
            Assert.That(wallCount, Is.LessThan(count / 20), "A persistent enemy row remained stuck to the arena wall.");
            Assert.That(
                releasedCount,
                Is.GreaterThanOrEqualTo(Mathf.CeilToInt(count * 0.95f)),
                "Too many shockwave enemies missed the synchronized return phase.");
            Assert.That(
                latestReleaseFrame - earliestReleaseFrame,
                Is.LessThanOrEqualTo(48),
                "Shockwave enemies still released in a visible radial sequence.");
            Assert.That(
                peakOverlapFraction,
                Is.LessThan(0.5f),
                "Shockwave agents merged through more than half of their physical diameter.");
        }

        [UnityTest]
        public IEnumerator AcceleratedTicks_ShareOneContactIterationBudget()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 32;
            var field = new HordeFlowField(
                new[] { Vector3.zero, Vector3.zero },
                null,
                12f,
                0.45f);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            for (var i = 0; i < count; i++)
            {
                var angle = i * Mathf.PI * 2f / count;
                states[i] = State(0f, 100f, 0f, false);
                states[i].Position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 4f;
                controls[i] = new Vector4(5f, 1f, 0f, 0f);
            }

            Assert.That(GpuHordeSimulation.TryCreate(
                count,
                field,
                EnemyManager.GetDetailedEnemyMesh(),
                out var simulation), Is.True);
            simulation.SetStationaryArena(true, centerSeeking: true, center: Vector3.zero);
            simulation.SpawnBatch(states, count);
            simulation.Dispatch(
                1f / 60f,
                controls,
                new Vector2[count],
                requestReadback: false,
                contactIterationScale: 0.5f,
                updatePresentation: false);

            Assert.That(
                simulation.LastContactIterationCount,
                Is.EqualTo(32),
                "Four accelerated ticks repeated the full per-tick contact budget.");
            simulation.Dispose();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArenaShockwave_WallImpactReflectsImmediatelyTowardCenter()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const float arenaRadius = 12f;
            var field = new HordeFlowField(
                new[] { Vector3.zero, Vector3.zero },
                null,
                arenaRadius,
                0.45f);
            var states = new[] { State(10.5f, 100f, 0f, false) };
            var controls = new[] { new Vector4(5f, 1f, 0f, 0f) };
            Assert.That(GpuHordeSimulation.TryCreate(
                1,
                field,
                EnemyManager.GetDetailedEnemyMesh(),
                out var simulation), Is.True);
            simulation.SetCollisionDiameter(0.5f);
            simulation.SetStationaryArena(true, centerSeeking: true, center: Vector3.zero);
            simulation.SpawnBatch(states, 1);
            Assert.That(simulation.QueueAreaEffect(
                Vector3.zero,
                1f,
                0f,
                16f,
                0,
                0f,
                0f,
                1,
                knockbackPropagationRadius: arenaRadius), Is.True);

            var sawRiding = false;
            var sawPinnedAtWall = false;
            var maximumImmediateInwardSpeed = 0f;
            var releaseFrame = -1;
            var reflectedFrame = -1;
            var releaseRadius = -1f;
            var radiusAfterRelease = -1f;
            for (var frame = 0; frame < 120; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, new Vector2[1], requestReadback: false);
                simulation.ReadStatesSynchronous(states, 1);
                sawRiding |= states[0].ShockwaveOutwardTimer > 0.0001f;
                if (sawRiding && states[0].ShockwaveOutwardTimer <= 0.0001f && releaseFrame < 0)
                {
                    releaseFrame = frame;
                    releaseRadius = states[0].Position.magnitude;
                }
                if (releaseFrame >= 0 && frame == releaseFrame + 12)
                {
                    radiusAfterRelease = states[0].Position.magnitude;
                }
                if (sawRiding)
                {
                    sawPinnedAtWall |= states[0].ShockwaveOutwardTimer < -0.0001f;
                    var inward = -states[0].Position.normalized;
                    var inwardSpeed = Vector2.Dot(
                        states[0].Velocity + states[0].KnockbackVelocity,
                        inward);
                    maximumImmediateInwardSpeed = Mathf.Max(maximumImmediateInwardSpeed, inwardSpeed);
                    if (inwardSpeed > 3f && reflectedFrame < 0)
                    {
                        reflectedFrame = frame;
                    }
                }

                if ((frame & 15) == 0)
                {
                    yield return null;
                }
            }

            simulation.Dispose();
            Assert.That(sawRiding, Is.True, "The test agent never met the shockwave.");
            Assert.That(sawPinnedAtWall, Is.False, "Wall impact still entered a stationary recovery state.");
            Assert.That(maximumImmediateInwardSpeed, Is.GreaterThan(3f),
                "Wall impact did not redirect meaningful momentum toward the arena center.");
            Assert.That(releaseFrame, Is.GreaterThanOrEqualTo(0),
                "The wall impact never released shockwave control.");
            Assert.That(reflectedFrame - releaseFrame, Is.InRange(0, 1),
                "The enemy paused at the wall before receiving its inward reflection.");
            Assert.That(radiusAfterRelease, Is.GreaterThanOrEqualTo(0f),
                "The test did not sample the enemy after its wall impact.");
            Assert.That(releaseRadius - radiusAfterRelease, Is.GreaterThan(0.65f),
                "The enemy received velocity but remained visually stuck to the wall.");
        }

        [UnityTest]
        public IEnumerator ArenaShockwave_BlockedRowReflectsBackThroughTheCrowd()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 8;
            const float arenaRadius = 12f;
            var field = new HordeFlowField(
                new[] { Vector3.zero, Vector3.zero },
                null,
                arenaRadius,
                0.45f);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            for (var i = 0; i < count; i++)
            {
                states[i] = State(6.1f + i * 0.69f, 100f, 0f, false);
                controls[i] = new Vector4(5f, 1f, 0f, 0f);
            }

            Assert.That(GpuHordeSimulation.TryCreate(
                count,
                field,
                EnemyManager.GetDetailedEnemyMesh(),
                out var simulation), Is.True);
            simulation.SetCollisionDiameter(0.5f);
            simulation.SetStationaryArena(true, centerSeeking: true, center: Vector3.zero);
            simulation.SpawnBatch(states, count);
            Assert.That(simulation.QueueAreaEffect(
                Vector3.zero,
                1f,
                0f,
                16f,
                0,
                0f,
                0f,
                1,
                knockbackPropagationRadius: arenaRadius), Is.True);

            var reflected = new bool[count];
            var sawPinnedState = false;
            var outerReflectionFrame = -1;
            var outerReflectionRadius = -1f;
            var outerRadiusAfterReflection = -1f;
            var rowRadiiAtReflection = new float[count];
            var rowRadiiAfterReflection = new float[count];
            for (var frame = 0; frame < 240; frame++)
            {
                simulation.Dispatch(1f / 60f, controls, new Vector2[count], requestReadback: false);
                simulation.ReadStatesSynchronous(states, count);
                for (var i = 0; i < count; i++)
                {
                    sawPinnedState |= states[i].ShockwaveOutwardTimer < -0.0001f;
                    if (states[i].ShockwaveExposure > 0.05f)
                    {
                        var inward = -states[i].Position.normalized;
                        var inwardSpeed = Vector2.Dot(
                            states[i].Velocity + states[i].KnockbackVelocity,
                            inward);
                        reflected[i] |= inwardSpeed > 2f;
                        if (i == count - 1 && inwardSpeed > 2f && outerReflectionFrame < 0)
                        {
                            outerReflectionFrame = frame;
                            outerReflectionRadius = states[i].Position.magnitude;
                            for (var row = 0; row < count; row++)
                            {
                                rowRadiiAtReflection[row] = states[row].Position.magnitude;
                            }
                        }
                    }
                }
                if (outerReflectionFrame >= 0 && frame == outerReflectionFrame + 12)
                {
                    outerRadiusAfterReflection = states[count - 1].Position.magnitude;
                    for (var row = 0; row < count; row++)
                    {
                        rowRadiiAfterReflection[row] = states[row].Position.magnitude;
                    }
                }

                if ((frame & 15) == 0)
                {
                    yield return null;
                }
            }

            simulation.Dispose();
            Assert.That(sawPinnedState, Is.False,
                "A blocked rider still entered the old waiting state.");
            Assert.That(reflected, Is.All.True,
                "The reflection stopped at the wall instead of travelling through the blocked row.");
            Assert.That(outerRadiusAfterReflection, Is.GreaterThanOrEqualTo(0f),
                "The test did not sample the wall enemy after reflection.");
            Assert.That(
                outerReflectionRadius - outerRadiusAfterReflection,
                Is.GreaterThan(0.35f),
                "The wall enemy waited for the row in front instead of pushing it inward.");
            for (var i = 0; i < count; i++)
            {
                Assert.That(
                    rowRadiiAtReflection[i] - rowRadiiAfterReflection[i],
                    Is.GreaterThan(0.15f),
                    $"Enemy {i} did not receive the inward push through the contact chain.");
            }
        }

        [UnityTest]
        public IEnumerator ArenaShockwave_ReturningCrowdReformsWithinTenSeconds()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 1024;
            const float arenaRadius = 60f;
            const float levelSevenBaseSpeed = 8.16f;
            var field = new HordeFlowField(
                new[] { Vector3.zero, Vector3.zero },
                null,
                arenaRadius,
                0.62f);
            var states = new GpuHordeSimulation.AgentState[count];
            var controls = new Vector4[count];
            for (var i = 0; i < count; i++)
            {
                var normalizedRadius = (i + 0.5f) / count;
                var radius = Mathf.Lerp(5f, 20f, Mathf.Sqrt(normalizedRadius));
                var angle = i * 2.3999632f;
                states[i] = State(0f, 100f, 0f, false);
                states[i].Position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                controls[i] = new Vector4(levelSevenBaseSpeed, 1f, 0f, 0f);
            }

            Assert.That(GpuHordeSimulation.TryCreate(
                count,
                field,
                EnemyManager.GetDetailedEnemyMesh(),
                out var simulation), Is.True);
            simulation.SetCollisionDiameter(0.5f);
            simulation.SetStationaryArena(true, centerSeeking: true, center: Vector3.zero);
            simulation.SpawnBatch(states, count);
            Assert.That(simulation.QueueAreaEffect(
                Vector3.zero,
                1f,
                0f,
                16f,
                0,
                0f,
                0f,
                1,
                knockbackPropagationRadius: arenaRadius), Is.True);

            // Three seconds for the wave to cross the 60 m arena, then ten
            // seconds for normal-speed crowd recovery.
            const int totalFrames = 13 * 60;
            for (var frame = 0; frame < totalFrames; frame++)
            {
                simulation.Dispatch(
                    1f / 60f,
                    controls,
                    new Vector2[count],
                    requestReadback: false,
                    contactIterationScale: 1f);
                if ((frame % 120) == 0)
                {
                    yield return null;
                }
            }

            simulation.ReadStatesSynchronous(states, count);
            simulation.Dispose();
            var reformedCount = 0;
            var touching = new bool[count];
            for (var i = 0; i < count; i++)
            {
                if (states[i].Position.magnitude <= 14f)
                {
                    reformedCount++;
                }

                for (var j = i + 1; j < count; j++)
                {
                    var pairDiameter = Mathf.Clamp(
                        (states[i].Scale + states[j].Scale) * 1.06f,
                        0.42f,
                        0.825f);
                    var touchingDistance = pairDiameter * 1.08f;
                    if ((states[i].Position - states[j].Position).sqrMagnitude <=
                        touchingDistance * touchingDistance)
                    {
                        touching[i] = true;
                        touching[j] = true;
                    }
                }
            }

            var touchingCount = 0;
            for (var i = 0; i < count; i++)
            {
                touchingCount += touching[i] ? 1 : 0;
            }

            Assert.That(reformedCount, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(count * 0.9f)),
                $"Only {reformedCount}/{count} enemies reformed near the center within ten seconds.");
            Assert.That(touchingCount, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(count * 0.8f)),
                $"Only {touchingCount}/{count} enemies physically touched the reformed crowd.");
        }

        [UnityTest]
        public IEnumerator MovingGoblinCrowd_TurnsWithoutContactJitter()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            const int count = 144;
            var field = new HordeFlowField(new[]
            {
                new Vector3(-18f, 0f, 0f), Vector3.zero, new Vector3(0f, 0f, 18f)
            }, null, 3.72f, 0.31f);
            Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
            using (simulation)
            {
                simulation.SetCollisionDiameter(0.29f);
                var states = new GpuHordeSimulation.AgentState[count];
                var controls = new Vector4[count];
                for (var i = 0; i < count; i++)
                {
                    states[i] = State(-9f + (i / 6) * 0.34f, 100f, 0f, false);
                    states[i].Position.y = ((i % 6) - 2.5f) * 0.34f;
                    states[i].Scale = 0.29f;
                    states[i].Mass = 1f;
                    states[i].Facing = Vector2.right;
                    controls[i] = new Vector4(3.84f, 1f, 0f, 0f);
                }
                simulation.SpawnBatch(states, controls, 0, count);
                for (var frame = 0; frame < 90; frame++)
                    simulation.Dispatch(1f / 60f, null, null, false);
                var corrections = new System.Collections.Generic.List<float>();
                var previousStep = new Vector2[count];
                var travel = 0f;
                var diagnostics = new uint[7];
                for (var frame = 0; frame < 60; frame++)
                {
                    simulation.Dispatch(1f / 60f, null, null, false);
                    simulation.ReadStatesSynchronous(states, count);
                    for (var i = 0; i < count; i++)
                    {
                        var step = states[i].Position - states[i].StepStartPosition;
                        // Solved velocity now equals actual displacement. Observe
                        // changes in body travel directly rather than subtracting
                        // that velocity and obtaining zero by construction.
                        if (frame > 0) corrections.Add((step - previousStep[i]).magnitude);
                        previousStep[i] = step;
                        travel += step.magnitude;
                    }
                    if (frame % 15 == 0) yield return null;
                }
                corrections.Sort();
                var meanJitter = 0f;
                foreach (var correction in corrections) meanJitter += correction / corrections.Count;
                var p95 = corrections[(int)(corrections.Count * 0.95f)];
                simulation.SynchronizeDiagnostics(diagnostics);
                Debug.Log($"Moving goblin turn: mean actual-step change={meanJitter:F5}m, p95={p95:F5}m, speed={travel / count:F2}m/s, collisions={diagnostics[5]}");
                Assert.That(meanJitter, Is.LessThan(0.006f), "Contact corrections must not bounce bodies between successive ticks.");
                Assert.That(p95, Is.LessThan(0.02f));
                Assert.That(travel / count, Is.GreaterThan(1f), "Low jitter must come from smooth travel, not a frozen crowd.");
                Assert.That(diagnostics[5], Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator RedGoblinQueue_TouchesBodiesAndSettlesWithoutRunningInPlace()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            var hadPreference = PlayerPrefs.HasKey(GoblinPreviewAssets.PreferenceKey);
            var preference = PlayerPrefs.GetInt(GoblinPreviewAssets.PreferenceKey);
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            GpuHordeSimulation simulation = null;
            try
            {
                const int count = 96;
                var field = new HordeFlowField(new[] { new Vector3(-28, 0, 0), new Vector3(12, 0, 0) }, null, 0.95f, 0.31f);
                Assert.That(GpuHordeSimulation.TryCreate(count, field, EnemyManager.GetLowEnemyMesh(), out simulation), Is.True);
                simulation.SetCollisionDiameter(0.29f);
                var states = new GpuHordeSimulation.AgentState[count];
                var controls = new Vector4[count];
                for (var i = 0; i < count; i++)
                {
                    states[i] = State(-23f + (i / 3) * 0.65f, 100f, 0, false);
                    states[i].Position.y = ((i % 3) - 1) * 0.63f;
                    states[i].Scale = 0.29f;
                    states[i].Mass = 1;
                    states[i].Facing = Vector2.right;
                    states[i].AttackInterval = 1f;
                    states[i].WallDamageMultiplier = 1f;
                    controls[i] = new Vector4(3.84f, 1, 0, 0);
                }
                simulation.SpawnBatch(states, count);
                var blocker = new TestCombatTarget(Vector3.zero, 1000000, 1000, Vector3.forward, 2, 0.15f);
                var targets = new ICombatTarget[] { blocker };
                var settled = new GpuHordeSimulation.AgentState[count];
                var maxStep = 0f;
                var maximumStepBody = -1;
                var maximumStepState = default(GpuHordeSimulation.AgentState);
                for (var frame = 0; frame < 600; frame++)
                {
                    simulation.SynchronizeDynamicTargets(targets);
                    simulation.Dispatch(1f / 60f, controls, null, false);
                    if (frame == 539) simulation.ReadStatesSynchronous(settled, count);
                    if (frame >= 540)
                    {
                        simulation.ReadStatesSynchronous(states, count);
                        for (var i = 0; i < count; i++)
                        {
                            var step = Vector2.Distance(states[i].Position, states[i].StepStartPosition);
                            if (step <= maxStep) continue;
                            maxStep = step;
                            maximumStepBody = i;
                            maximumStepState = states[i];
                        }
                    }
                    if (frame % 30 == 0) yield return null;
                }
                var nearest = new float[count];
                var animationTravel = 0f;
                for (var i = 0; i < count; i++)
                {
                    nearest[i] = float.MaxValue;
                    animationTravel += states[i].AnimationDistance - settled[i].AnimationDistance;
                    for (var j = 0; j < count; j++)
                        if (i != j) nearest[i] = Mathf.Min(nearest[i], Vector2.Distance(states[i].Position, states[j].Position));
                }
                System.Array.Sort(nearest);
                var heldCount = 0;
                foreach (var state in states) if (state.Padding2 >= 1f) heldCount++;
                Debug.Log($"Goblin queue: median spacing={nearest[count / 2]:F4}, minimum={nearest[0]:F4}, max settled step={maxStep:F5}, mean last-second gait travel={animationTravel / count:F4}, held={heldCount}/{count}, largest-step=#{maximumStepBody} p={maximumStepState.Position} start={maximumStepState.StepStartPosition} v={maximumStepState.Velocity} blocker={maximumStepState.Padding2} flags={maximumStepState.Flags}");
                var bodyDiameter = GoblinPreviewAssets.BodyDiameter(0.29f, 0.29f);
                Assert.That(nearest[count / 2], Is.GreaterThanOrEqualTo(bodyDiameter * 0.995f));
                Assert.That(nearest[count / 2], Is.LessThan(bodyDiameter * 1.025f),
                    "A held queue should make readable body contact, not preserve a large idle gap.");
                Assert.That(nearest[0], Is.GreaterThan(bodyDiameter * 0.995f));
                Assert.That(maxStep, Is.LessThan(0.015f));
                Assert.That(animationTravel / count, Is.LessThan(0.08f));
            }
            finally
            {
                simulation?.Dispose();
                if (hadPreference) PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, preference);
                else PlayerPrefs.DeleteKey(GoblinPreviewAssets.PreferenceKey);
            }
        }

        [UnityTest]
        public IEnumerator StagedSpawn_PreservesMovingAndDamagedAgentsAcrossBufferSwaps()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            var field = new HordeFlowField(new[] { new Vector3(-20, 0, 0), new Vector3(20, 0, 0) }, null, 3, 0.62f);
            Assert.That(GpuHordeSimulation.TryCreate(8, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
            using (simulation)
            {
                var states = new GpuHordeSimulation.AgentState[8];
                var controls = new Vector4[8];
                states[0] = State(-10, 100, 0, false);
                controls[0] = new Vector4(2, 1, 0, 0);
                simulation.SpawnBatch(states, controls, 0, 1);
                for (var step = 0; step < 20; step++) simulation.Dispatch(1f / 60, null, null, false);
                simulation.ReadStatesSynchronous(states, 1);
                var firstPosition = states[0].Position;
                simulation.QueueDamage(0, 25);
                var culledBeforeSpawn = new uint[5];
                var culledAfterSpawn = new uint[5];
                for (var index = 1; index < states.Length; index++)
                {
                    // A new range must not upload stale copies of earlier slots.
                    states[index] = State(-15 + index, 70 + index, 0, false);
                    states[index].Position.y = 2;
                    controls[index] = new Vector4(1, 1, 0, 0);
                    simulation.ReadDrawArgsSynchronous(culledBeforeSpawn);
                    simulation.SpawnBatch(states, controls, index, 1);
                    simulation.ReadDrawArgsSynchronous(culledAfterSpawn);
                    Assert.That(culledAfterSpawn, Is.EqualTo(culledBeforeSpawn),
                        "Spawning between simulation ticks must not replace culled draw counts with the total population.");
                    simulation.Dispatch(1f / 60, null, null, false);
                }
                simulation.ReadStatesSynchronous(states, states.Length);
                Assert.That(states[0].Position.x, Is.GreaterThan(firstPosition.x));
                Assert.That(states[0].Health, Is.EqualTo(75));
                for (var index = 1; index < states.Length; index++)
                {
                    Assert.That(states[index].Status, Is.EqualTo(1));
                    Assert.That(states[index].Health, Is.EqualTo(70 + index));
                    Assert.That(states[index].Position.x, Is.GreaterThan(-15 + index));
                }
            }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StationaryGoblin_KeepsPresentationClockAndAnimatesLateralPush()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            var field = new HordeFlowField(new[] { new Vector3(-20, 0, 0), new Vector3(20, 0, 0) }, null, 5, 0.62f);
            Assert.That(GpuHordeSimulation.TryCreate(1, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
            using (simulation)
            {
                var states = new[] { State(0, 100, 0, false) };
                var controls = new[] { new Vector4(0, 1, 0, 0) };
                states[0].Velocity = Vector2.zero;
                simulation.SpawnBatch(states, controls, 0, 1);
                for (var step = 0; step < 60; step++) simulation.Dispatch(1f / 60, null, null, false);
                simulation.ReadStatesSynchronous(states, 1);
                Assert.That(states[0].PresentationTime, Is.EqualTo(1).Within(0.001f));
                Assert.That(states[0].AnimationDistance, Is.LessThan(0.01f));
                Assert.That(states[0].LocomotionSpeed, Is.LessThan(0.08f));
                states[0].KnockbackVelocity = new Vector2(0, 2);
                states[0].Padding = 0.5f;
                var before = states[0].AnimationDistance;
                simulation.SpawnBatch(states, controls, 0, 1);
                for (var step = 0; step < 12; step++) simulation.Dispatch(1f / 60, null, null, false);
                simulation.ReadStatesSynchronous(states, 1);
                Assert.That(states[0].Position.y, Is.GreaterThan(0.05f));
                Assert.That(states[0].AnimationDistance, Is.GreaterThan(before + 0.02f));
                Assert.That(states[0].LocomotionSpeed, Is.GreaterThan(0.08f));
            }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SpawnClearance_RejectsMovedQueriesAndDifferentPositions()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required.");
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            var field = new HordeFlowField(new[] { new Vector3(-20, 0, 0), new Vector3(20, 0, 0) }, null, 3, 0.62f);
            Assert.That(GpuHordeSimulation.TryCreate(2, field, EnemyManager.GetLowEnemyMesh(), out var simulation), Is.True);
            using (simulation)
            {
                var states = new[] { State(-10, 100, 0, false), State(0, 100, 0, false) };
                states[0].Scale = states[1].Scale = 0.2f;
                var controls = new[] { new Vector4(4, 1, 0, 0), new Vector4(4, 1, 0, 0) };
                simulation.SpawnBatch(states, controls, 0, 1);
                var position = Vector3.zero;
                Assert.That(simulation.TryGetSpawnClearance(position, 1, -1, out _, 0.4f), Is.False);
                simulation.Dispatch(1f / 60, null, null, false);
                UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                yield return null;
                // The first request predates the motion bound established by the
                // first predictive dispatch, so it cannot authorize a spawn.
                Assert.That(simulation.TryGetSpawnClearance(position, 1, -1, out _, 0.4f), Is.False);
                simulation.Dispatch(1f / 60, null, null, false);
                UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                yield return null;
                Assert.That(simulation.TryGetSpawnClearance(position, 1, -1, out var clear, 0.4f), Is.True);
                Assert.That(clear, Is.True);
                // These positions shared the old quarter-metre targeting cache key.
                Assert.That(simulation.TryGetSpawnClearance(new Vector3(0.1f, 0, 0), 1, -1, out _, 0.4f), Is.False);
                // Several fixed ticks can execute inside one render frame. Wall-clock age
                // alone cannot establish that a formerly empty disk remains empty.
                for (var i = 0; i < 8; i++) simulation.Dispatch(1f / 60, null, null, false);
                Assert.That(simulation.TryGetSpawnClearance(position, 1, -1, out _, 0.4f), Is.False);
                simulation.Dispatch(1f / 60, null, null, false);
                UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                yield return null;
                // The caller's lane watermark invalidates observations of an earlier
                // population even if the spawn happens in the same render frame.
                simulation.SpawnBatch(states, controls, 1, 1);
                Assert.That(simulation.TryGetSpawnClearance(position, 1, Time.frameCount, out _, 0.4f), Is.False);
            }
            LogAssert.NoUnexpectedReceived();
        }

        private sealed class TestCombatTarget : ICombatTarget, IOrientedCombatTarget
        {
            public TestCombatTarget(
                Vector3 position,
                float health,
                float blockCapacity = 20f,
                Vector3 combatAxis = default,
                float combatHalfLength = 0f,
                float combatHalfDepth = 0f)
            {
                Position = position;
                CurrentHealth = health;
                MaximumHealth = health;
                BlockCapacity = blockCapacity;
                CombatAxis = combatAxis;
                CombatHalfLength = combatHalfLength;
                CombatHalfDepth = combatHalfDepth;
            }

            public Vector3 Position { get; }
            public bool IsAlive => CurrentHealth > 0f;
            public CombatTargetKind TargetKind => CombatTargetKind.Barrier;
            public float CombatRadius => 0.6f;
            public Vector3 CombatAxis { get; }
            public float CombatHalfLength { get; }
            public float CombatHalfDepth { get; }
            public float BlockCapacity { get; }
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
