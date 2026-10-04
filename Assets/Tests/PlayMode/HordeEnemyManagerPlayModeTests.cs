using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class HordeEnemyManagerPlayModeTests
    {
        private readonly List<Object> cleanupObjects = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            foreach (var unityObject in cleanupObjects)
            {
                if (unityObject != null)
                {
                    Object.Destroy(unityObject);
                }
            }

            cleanupObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LevelTwoRealSpawnLoop_KeepsEveryGpuAgentOnRoad()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var points = new[]
            {
                new Vector3(-56f, 0f, 14f), new Vector3(-34f, 0f, 14f),
                new Vector3(-24f, 0f, 14f), new Vector3(-24f, 0f, -5f),
                new Vector3(-24f, 0f, -17f), new Vector3(2f, 0f, -17f),
                new Vector3(10f, 0f, -17f), new Vector3(18f, 0f, -17f),
                new Vector3(36f, 0f, -17f), new Vector3(42f, 0f, -11f),
                new Vector3(42f, 0f, 10f), new Vector3(56f, 0f, 10f)
            };
            var widths = new[] { 6f, 6f, 6f, 5f, 2.5f, 2.5f, 5f, 10f, 10f, 9f, 7f, 6f };
            var route = CreateRoute(points);
            route.SetWaypoints(points, widths);
            var enemy = CreateEnemy(speed: 4.8f, health: 10f);
            var wave = CreateWave(enemy, 500, 0.45f);
            wave.randomSpawnBurstMin = 5;
            wave.randomSpawnBurstMax = 12;
            var manager = CreateManager();
            manager.BeginWave(wave, route);
            var simulationField = typeof(HordeEnemyManager).GetField("gpuSimulation",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var flowFieldField = typeof(HordeEnemyManager).GetField("flowField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(simulationField, Is.Not.Null);
            Assert.That(flowFieldField, Is.Not.Null);
            var flow = (HordeFlowField)flowFieldField.GetValue(manager);
            var states = new GpuHordeSimulation.AgentState[500];
            Time.timeScale = 10f;

            for (var frame = 0; frame < 180; frame++)
            {
                yield return null;
                if (frame % 5 != 0 || manager.TotalSpawned == 0)
                {
                    continue;
                }

                var simulation = (GpuHordeSimulation)simulationField.GetValue(manager);
                simulation.ReadStatesSynchronous(states, manager.TotalSpawned);
                for (var i = 0; i < manager.TotalSpawned; i++)
                {
                    if (states[i].Status != 1)
                    {
                        continue;
                    }

                    var position = new Vector3(states[i].Position.x, 0f, states[i].Position.y);
                    Assert.That(flow.IsWalkable(position), Is.True,
                        $"Real Level 2 spawn-loop agent {i} is outside the road at frame {frame}: {position}.");
                }
            }
        }

        [Test]
        public void LevelTwoContent_UsesGpuAuthoritativeHordeBackend()
        {
            var content = SampleContent.Create();
            LevelDefinition levelTwo = null;
            for (var i = 0; i < content.Levels.Count; i++)
            {
                var level = content.Levels[i];
                cleanupObjects.Add(level);
                cleanupObjects.Add(level.wave);
                if (level.id == "level_02")
                {
                    levelTwo = level;
                }
            }
            cleanupObjects.Add(content.SkillTree);
            for (var i = 0; i < content.Towers.Count; i++)
            {
                cleanupObjects.Add(content.Towers[i]);
            }

            Assert.That(levelTwo, Is.Not.Null);
            Assert.That(levelTwo.useDataHordePrototype, Is.True,
                "Level 2 must not fall back to the legacy CPU path-distance enemy loop.");
            Assert.That(levelTwo.preventLifeLoss, Is.True,
                "Level 2 stress testing must continue even when enemies reach the finish.");
            Assert.That(levelTwo.wave.enemyScaleMultiplier, Is.EqualTo(0.7f).Within(0.001f),
                "Level 2 enemy bodies and models must be thirty percent smaller than their original size.");
            Assert.That(levelTwo.wave.totalEnemyCount, Is.EqualTo(10000));
            Assert.That(levelTwo.wave.enemyInertiaMultiplier, Is.EqualTo(1.35f).Within(0.001f));
            Assert.That(levelTwo.wave.enemyMassMultiplier, Is.EqualTo(1.3f).Within(0.001f));
            Assert.That(levelTwo.wave.collisionPressureMultiplier, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(levelTwo.secondaryPathWaypoints, Has.Length.EqualTo(levelTwo.pathWaypoints.Length));
            Assert.That(levelTwo.tertiaryPathWaypoints, Has.Length.EqualTo(levelTwo.pathWaypoints.Length));
            Assert.That(levelTwo.pathWaypointWidths[0], Is.EqualTo(30f));
            Assert.That(levelTwo.pathWaypointWidths[^1], Is.EqualTo(3f));
            Assert.That(levelTwo.secondaryPathWaypointWidths, Is.EqualTo(levelTwo.pathWaypointWidths));
            Assert.That(levelTwo.tertiaryPathWaypointWidths, Is.EqualTo(levelTwo.pathWaypointWidths));
        }

        [Test]
        public void DefaultEnemySpeedVariation_IsStableAndSubtle()
        {
            var firstPass = new float[64];
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            for (var i = 0; i < firstPass.Length; i++)
            {
                var factor = HordeEnemyManager.GetPersonalSpeedFactor(i);
                firstPass[i] = factor;
                minimum = Mathf.Min(minimum, factor);
                maximum = Mathf.Max(maximum, factor);
                Assert.That(factor, Is.InRange(0.84f, 1.16f));
            }

            for (var i = 0; i < firstPass.Length; i++)
            {
                Assert.That(HordeEnemyManager.GetPersonalSpeedFactor(i),
                    Is.EqualTo(firstPass[i]), "An enemy's locomotion identity must not change between ticks.");
            }

            Assert.That(minimum, Is.LessThan(0.90f));
            Assert.That(maximum, Is.GreaterThan(1.10f));
        }

        [UnityTest]
        public IEnumerator AllHordeLevels_UseOfficialSharedSolverProfile()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var content = SampleContent.Create();
            cleanupObjects.Add(content.SkillTree);
            foreach (var tower in content.Towers) cleanupObjects.Add(tower);
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var profileField = typeof(HordeEnemyManager).GetField("activeCrowdSolverProfile", fields);
            var diameterField = typeof(HordeEnemyManager).GetField("activeSolverCollisionDiameter", fields);
            var stepField = typeof(HordeEnemyManager).GetField("activeFixedSimulationStep", fields);
            var simulationField = typeof(HordeEnemyManager).GetField("gpuSimulation", fields);
            var perBodyField = typeof(GpuHordeSimulation).GetField("xpbdUsesPerBodyRadius", fields);
            var checkedLevels = 0;
            foreach (var level in content.Levels)
            {
                cleanupObjects.Add(level);
                cleanupObjects.Add(level.wave);
                foreach (var entry in level.wave.entries) cleanupObjects.Add(entry.enemy);
                if (!level.useDataHordePrototype)
                {
                    Assert.That(level.id, Is.EqualTo("level_01").Or.EqualTo("level_03"),
                        "Only the individual-actor levels retain their special-role gameplay backend.");
                    continue;
                }

                // Keep authored navigation and scale, but avoid allocating an entire
                // 100K wave merely to audit the production solver configuration.
                var wave = Object.Instantiate(level.wave);
                cleanupObjects.Add(wave);
                wave.totalEnemyCount = 16;
                wave.entries = new[] { new WaveEntry { enemy = level.wave.entries[0].enemy, count = 16 } };
                var route = CreateRoute(level.pathWaypoints, level.pathWaypointWidths);
                route.SetSecondaryWaypoints(level.secondaryPathWaypoints, level.secondaryPathWaypointWidths);
                route.SetTertiaryWaypoints(level.tertiaryPathWaypoints, level.tertiaryPathWaypointWidths);
                route.SetSquareCorners(level.useSquarePathCorners);
                var manager = CreateManager();
                manager.BeginWave(wave, route);
                Assert.That(manager.IsRunning, Is.True, level.id);
                Assert.That(profileField.GetValue(manager), Is.SameAs(CrowdSolverProfile.Default), level.id);
                var expectedDiameter = 0.56f * Mathf.Clamp(wave.enemyScaleMultiplier, 0.1f, 2f) * 0.56f;
                Assert.That((float)diameterField.GetValue(manager), Is.EqualTo(expectedDiameter).Within(0.0001f), level.id);
                Assert.That((float)stepField.GetValue(manager), Is.EqualTo(1f / 60f).Within(0.000001f), level.id);
                Assert.That((bool)perBodyField.GetValue(simulationField.GetValue(manager)), Is.True, level.id);
                yield return null;
                manager.StopWave();
                checkedLevels++;
            }
            Assert.That(checkedLevels, Is.EqualTo(6), "Audit levels 2, 2.1, 4, 5, 6 and 7.");
            Assert.That(CrowdSolverProfile.PositionIterations, Is.EqualTo(8));
        }

        [Test]
        public void LevelSixContent_UsesOrthogonalSquareCornerCollisionCourse()
        {
            var content = SampleContent.Create();
            LevelDefinition levelSix = null;
            for (var i = 0; i < content.Levels.Count; i++)
            {
                var candidate = content.Levels[i];
                cleanupObjects.Add(candidate);
                cleanupObjects.Add(candidate.wave);
                if (candidate.id == "level_06")
                {
                    levelSix = candidate;
                }
            }
            cleanupObjects.Add(content.SkillTree);
            for (var i = 0; i < content.Towers.Count; i++)
            {
                cleanupObjects.Add(content.Towers[i]);
            }

            Assert.That(levelSix, Is.Not.Null);
            Assert.That(levelSix.useDataHordePrototype, Is.True);
            Assert.That(levelSix.useSquarePathCorners, Is.True);
            Assert.That(levelSix.preventLifeLoss, Is.True);
            Assert.That(levelSix.roadWidth, Is.EqualTo(3f));
            Assert.That(levelSix.pathWaypointWidths, Has.Length.EqualTo(levelSix.pathWaypoints.Length));
            Assert.That(levelSix.pathWaypointWidths[0], Is.EqualTo(6f));
            Assert.That(levelSix.pathWaypointWidths[1], Is.EqualTo(6f));
            Assert.That(levelSix.pathWaypointWidths[2], Is.EqualTo(3f));
            Assert.That(levelSix.wave.totalEnemyCount, Is.EqualTo(10000));
            Assert.That(levelSix.wave.enforceSpawnClearance, Is.True);
            Assert.That(levelSix.wave.enemyScaleMultiplier, Is.EqualTo(0.567f).Within(0.001f));
            Assert.That(levelSix.wave.enemyInertiaMultiplier, Is.EqualTo(1.7f).Within(0.001f));
            Assert.That(levelSix.wave.spawnPackingMultiplier, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(levelSix.wave.randomEnemySpeedMin, Is.EqualTo(5f));
            Assert.That(levelSix.wave.randomEnemySpeedMax, Is.EqualTo(7f));
            Assert.That(levelSix.wave.colorEnemiesBySpeed, Is.True);
            for (var i = 1; i < levelSix.pathWaypoints.Length; i++)
            {
                var delta = levelSix.pathWaypoints[i] - levelSix.pathWaypoints[i - 1];
                var isHorizontal = Mathf.Abs(delta.x) > 0.001f && Mathf.Abs(delta.z) <= 0.001f;
                var isVertical = Mathf.Abs(delta.z) > 0.001f && Mathf.Abs(delta.x) <= 0.001f;
                Assert.That(isHorizontal || isVertical, Is.True,
                    $"Level 6 segment {i - 1} must be strictly axis aligned.");
            }
        }

        [Test]
        public void LevelSevenContent_ProvidesCenterSeekingCircularExplosionLab()
        {
            var content = SampleContent.Create();
            LevelDefinition levelSeven = null;
            for (var i = 0; i < content.Levels.Count; i++)
            {
                var candidate = content.Levels[i];
                cleanupObjects.Add(candidate);
                cleanupObjects.Add(candidate.wave);
                if (candidate.id == "level_07")
                {
                    levelSeven = candidate;
                }
            }
            cleanupObjects.Add(content.SkillTree);
            for (var i = 0; i < content.Towers.Count; i++)
            {
                cleanupObjects.Add(content.Towers[i]);
            }

            Assert.That(levelSeven, Is.Not.Null);
            Assert.That(levelSeven.useDataHordePrototype, Is.True);
            Assert.That(levelSeven.preventLifeLoss, Is.True);
            Assert.That(levelSeven.useCircularArena, Is.True);
            Assert.That(levelSeven.arenaRadius, Is.EqualTo(60f));
            Assert.That(levelSeven.roadWidth, Is.EqualTo(120f));
            Assert.That(levelSeven.pathWaypoints, Has.Length.EqualTo(2));
            Assert.That(levelSeven.pathWaypoints[0], Is.EqualTo(Vector3.zero));
            Assert.That(levelSeven.pathWaypoints[1], Is.EqualTo(Vector3.zero));
            Assert.That(levelSeven.pathWaypointWidths, Is.EqualTo(new[] { 120f, 120f }));
            Assert.That(levelSeven.wave.totalEnemyCount, Is.EqualTo(10000));
            Assert.That(levelSeven.wave.roadHalfWidth, Is.EqualTo(60f));
            Assert.That(levelSeven.wave.stationaryArena, Is.True);
            Assert.That(levelSeven.wave.randomArenaMovement, Is.False);
            Assert.That(levelSeven.wave.centerSeekingArena, Is.True);
            Assert.That(levelSeven.wave.enforceSpawnClearance, Is.True);
            Assert.That(levelSeven.wave.spawnPackingMultiplier, Is.EqualTo(1.05f).Within(0.001f));
            Assert.That(levelSeven.wave.spawnInterval, Is.EqualTo(0.02f));
            Assert.That(levelSeven.wave.enemySpeedMultiplier, Is.EqualTo(2f));
            Assert.That(levelSeven.wave.randomSpawnBurstMin, Is.EqualTo(6));
            Assert.That(levelSeven.wave.randomSpawnBurstMax, Is.EqualTo(10));
            var runnerCount = 0;
            var bruteCount = 0;
            for (var i = 0; i < levelSeven.wave.entries.Length; i++)
            {
                var entry = levelSeven.wave.entries[i];
                if (entry.enemy.id == "runner")
                {
                    runnerCount += entry.count;
                }
                else if (entry.enemy.id == "brute")
                {
                    bruteCount += entry.count;
                }
            }
            Assert.That(runnerCount, Is.EqualTo(8000));
            Assert.That(bruteCount, Is.EqualTo(2000));
        }

        [UnityTest]
        public IEnumerator DataHordeWave_SpawnsAndCompletes()
        {
            var route = CreateRoute(new[]
            {
                new Vector3(-4f, 0f, 0f),
                new Vector3(4f, 0f, 0f)
            });
            var enemy = CreateEnemy(speed: 12f, health: 4f);
            var wave = CreateWave(enemy, count: 18, spawnInterval: 0.01f, spawnImmediately: true);
            var manager = CreateManager();

            manager.BeginWave(wave, route);

            if (SystemInfo.supportsComputeShaders)
            {
                Assert.That(manager.Performance.ShaderName, Does.Contain("GPU Compute"));
            }

            // Allow the last body to clear the entrance queue and its asynchronous
            // escape event to arrive; two seconds races the final event with VAT
            // collision geometry enabled. A stuck body still fails this deadline.
            var timeout = Time.time + 4f;
            while (!manager.IsComplete && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(manager.TotalSpawned, Is.EqualTo(18));
            Assert.That(manager.TotalResolved, Is.EqualTo(18));
            Assert.That(manager.ActiveCount, Is.EqualTo(0));
            Assert.That(manager.IsComplete, Is.True);
        }

        [UnityTest]
        public IEnumerator DataHordeSpawnRate_FollowsTestSpeed()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var route = CreateRoute(
                new[] { Vector3.zero, Vector3.zero },
                new[] { 20f, 20f });
            var enemy = CreateEnemy(speed: 0.1f, health: 10f);
            var wave = CreateWave(enemy, count: 2000, spawnInterval: 0.12f);
            wave.randomSpawnBurstMin = 1;
            wave.randomSpawnBurstMax = 1;
            wave.stationaryArena = true;
            wave.centerSeekingArena = true;
            wave.enforceSpawnClearance = true;
            wave.roadHalfWidth = 20f;
            var manager = CreateManager();
            manager.BeginWave(wave, route);

            Time.timeScale = 1f;
            var normalDeadline = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < normalDeadline)
            {
                yield return null;
            }
            var normalSpeedSpawns = manager.TotalSpawned;

            Time.timeScale = 5f;
            var fastStart = manager.TotalSpawned;
            var fastDeadline = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < fastDeadline)
            {
                yield return null;
            }
            var acceleratedSpawns = manager.TotalSpawned - fastStart;

            Assert.That(normalSpeedSpawns, Is.GreaterThan(0));
            Assert.That(acceleratedSpawns, Is.GreaterThan(normalSpeedSpawns * 3),
                $"5x test speed spawned only {acceleratedSpawns} enemies versus " +
                $"{normalSpeedSpawns} during the same real-time interval at 1x.");
        }

        [UnityTest]
        public IEnumerator StationaryArena_SpawnsAtCenterAndNeverResolvesAsExit()
        {
            var route = CreateRoute(
                new[] { Vector3.zero, Vector3.zero },
                new[] { 20f, 20f });
            var enemy = CreateEnemy(speed: 5f, health: 20f);
            var wave = CreateWave(enemy, count: 24, spawnInterval: 0.02f);
            wave.stationaryArena = true;
            wave.roadHalfWidth = 10f;
            var manager = CreateManager();

            manager.BeginWave(wave, route);
            var timeout = Time.realtimeSinceStartup + 1f;
            while (manager.TotalSpawned < 12 && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(manager.TotalSpawned, Is.GreaterThanOrEqualTo(12));
            Assert.That(manager.TotalResolved, Is.EqualTo(0));
            Assert.That(manager.ActiveCount, Is.EqualTo(manager.TotalSpawned));

            manager.DamageAndKnockbackInRadius(Vector3.zero, 8f, 0.1f, 5f, 0, out var hitCount);
            Assert.That(hitCount, Is.EqualTo(manager.ActiveCount));
            for (var frame = 0; frame < 12; frame++)
            {
                yield return null;
            }

            Assert.That(manager.TotalResolved, Is.EqualTo(0));
            Assert.That(manager.ActiveCount, Is.EqualTo(manager.TotalSpawned));
        }

        [UnityTest]
        public IEnumerator DataHordeControlEffects_DoNotBreakRuntimeSimulation()
        {
            var route = CreateRoute(new[]
            {
                new Vector3(-5f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(5f, 0f, 1f)
            });
            var enemy = CreateEnemy(speed: 5f, health: 20f);
            var wave = CreateWave(enemy, count: 30, spawnInterval: 0.02f);
            var manager = CreateManager();

            manager.BeginWave(wave, route);
            var spawnTimeout = Time.realtimeSinceStartup + 0.5f;
            while (manager.ActiveCount == 0 && Time.realtimeSinceStartup < spawnTimeout)
            {
                yield return null;
            }

            manager.ApplySlowAura(Vector3.zero, 8f, 0.5f, 100f);
            var damage = manager.DamageAndKnockbackInRadius(Vector3.zero, 8f, 3f, 1.25f, maxTargets: 20, out var hitCount);

            Assert.That(hitCount, Is.GreaterThan(0));
            Assert.That(damage, Is.GreaterThan(0f));
            Assert.That(manager.ActiveCount, Is.GreaterThan(0));

            for (var frame = 0; frame < 40; frame++)
            {
                yield return null;
            }

            Assert.That(manager.TotalSpawned, Is.GreaterThan(0));
            Assert.That(manager.TotalResolved, Is.LessThanOrEqualTo(manager.TotalSpawned));
        }

        [UnityTest]
        public IEnumerator GpuEntity_TakesDamageDiesAndForwardsGameplayEvents()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var route = CreateRoute(new[] { new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, 0f) });
            var definition = CreateEnemy(speed: 0.5f, health: 12f);
            var wave = CreateWave(definition, 1, 0.01f, spawnImmediately: true);
            var horde = CreateManager();
            var wrapperObject = new GameObject("TestEnemyManager");
            cleanupObjects.Add(wrapperObject);
            var wrapper = wrapperObject.AddComponent<EnemyManager>();
            wrapper.SetHordePrototype(horde);
            var spawned = 0;
            EnemyDefinition killed = null;
            wrapper.EnemySpawned += _ => spawned++;
            wrapper.EnemyKilled += enemy => killed = enemy;

            wrapper.BeginWave(wave, route, useDataHordePrototype: true);
            var spawnDeadline = Time.realtimeSinceStartup + 1f;
            while (wrapper.TotalSpawned == 0 && Time.realtimeSinceStartup < spawnDeadline)
            {
                yield return null;
            }

            Assert.That(spawned, Is.EqualTo(1), "The GPU entity did not emit its compact spawn event.");
            wrapper.DamageAndKnockbackInRadius(route.StartPoint, 4f, 50f, 0f, out _);
            var deathDeadline = Time.realtimeSinceStartup + 2f;
            while (killed == null && Time.realtimeSinceStartup < deathDeadline)
            {
                yield return null;
            }

            Assert.That(killed, Is.SameAs(definition), "GPU death was not forwarded to gameplay rewards/state.");
            Assert.That(wrapper.ActiveEnemyCount, Is.EqualTo(0));
            Assert.That(wrapper.TotalResolved, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator GpuEntity_StopsAtAndDestroysDynamicBarricade()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var route = CreateRoute(new[] { new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, 0f) });
            var definition = CreateEnemy(speed: 4.8f, health: 100f);
            definition.attackDamage = 8f;
            definition.attackInterval = 0.15f;
            definition.wallDamageMultiplier = 1f;
            var wave = CreateWave(definition, 1, 0.01f, spawnImmediately: true);
            // The target must span this test corridor. A random lateral spawn
            // on the default wide road can correctly bypass the barricade.
            wave.roadHalfWidth = 0.8f;
            var horde = CreateManager();
            var wrapperObject = new GameObject("TestEnemyManagerWithBarricade");
            cleanupObjects.Add(wrapperObject);
            var wrapper = wrapperObject.AddComponent<EnemyManager>();
            wrapper.SetHordePrototype(horde);
            var barricade = new TestGpuCombatTarget(new Vector3(-3.8f, 0f, 0f), 12f);
            wrapper.RegisterCombatTarget(barricade);
            wrapper.BeginWave(wave, route, useDataHordePrototype: true);

            // The first GPU dispatch can include shader warm-up when this test
            // runs after the full movement suite, so keep the gameplay assertion
            // deterministic without relying on a three-second wall-clock race.
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!barricade.Destroyed && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!barricade.Destroyed)
            {
                var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager).GetField(
                    "gpuSimulation", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).GetValue(horde);
                var states = new GpuHordeSimulation.AgentState[1];
                var diagnostics = new uint[GpuHordeSimulation.DiagnosticsCount];
                simulation.ReadStatesSynchronous(states, 1);
                simulation.SynchronizeDiagnostics(diagnostics);
                Debug.Log(System.FormattableString.Invariant(
                    $"XPBD barricade timeout: position={states[0].Position.x:F4}:{states[0].Position.y:F4}, velocity={states[0].Velocity.x:F4}:{states[0].Velocity.y:F4}, status={states[0].Status}, blockerSlot={states[0].Padding2:F1}, attackTimer={states[0].AttackTimer:F3}, targetHealth={barricade.CurrentHealth:F3}, active={diagnostics[3]}, contacts={diagnostics[11]}, deep={diagnostics[13]}"));
            }

            Assert.That(barricade.WasDamaged, Is.True, "The GPU enemy never attacked the blocking target.");
            Assert.That(barricade.Destroyed, Is.True, "The GPU enemy did not destroy the barricade.");
            Assert.That(barricade.LastSource, Is.SameAs(definition));
        }

        [UnityTest]
        public IEnumerator GpuEntity_ReachingExitForwardsBaseDamageDefinition()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var route = CreateRoute(new[] { new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f) });
            var definition = CreateEnemy(speed: 12f, health: 20f);
            definition.lifeDamage = 3;
            var wave = CreateWave(definition, 1, 0.01f, spawnImmediately: true);
            var horde = CreateManager();
            var wrapperObject = new GameObject("TestEnemyManagerEscape");
            cleanupObjects.Add(wrapperObject);
            var wrapper = wrapperObject.AddComponent<EnemyManager>();
            wrapper.SetHordePrototype(horde);
            EnemyDefinition escaped = null;
            wrapper.EnemyEscaped += enemy => escaped = enemy;

            wrapper.BeginWave(wave, route, useDataHordePrototype: true);
            var deadline = Time.realtimeSinceStartup + 2f;
            while (escaped == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(escaped, Is.SameAs(definition),
                "The escaped GPU entity did not forward its life-damage definition to GameSession.");
            Assert.That(escaped.lifeDamage, Is.EqualTo(3));
            Assert.That(wrapper.TotalResolved, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DataHordeWave_BatchesLargeImmediateSpawn()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            const int count = 4096;
            var route = CreateRoute(new[]
            {
                new Vector3(-40f, 0f, 0f),
                new Vector3(40f, 0f, 0f)
            });
            var enemy = CreateEnemy(speed: 1f, health: 10f);
            var wave = CreateWave(enemy, count, spawnInterval: 0.01f, spawnImmediately: true);
            var manager = CreateManager();

            manager.BeginWave(wave, route);
            var spawnTimeout = Time.realtimeSinceStartup + 0.5f;
            while (manager.TotalSpawned < count && Time.realtimeSinceStartup < spawnTimeout)
            {
                yield return null;
            }

            Assert.That(manager.TotalSpawned, Is.EqualTo(count));
            Assert.That(manager.ActiveCount, Is.GreaterThan(0));
            Assert.That(manager.Performance.ShaderName, Does.Contain("GPU Compute"));
        }

        [Test]
        public void DataHordeSpawn_BurstCoversEntranceWithoutEmptyBands()
        {
            const int count = 64;
            var route = CreateRoute(new[]
            {
                new Vector3(-20f, 0f, 0f),
                new Vector3(20f, 0f, 0f)
            });
            var enemy = CreateEnemy(speed: 1f, health: 10f);
            var wave = CreateWave(enemy, count, spawnInterval: 0.1f, spawnImmediately: true);
            wave.roadHalfWidth = 10f;
            var manager = CreateManager();

            manager.BeginWave(wave, route);
            var field = typeof(HordeEnemyManager).GetField(
                "spawnLateralOffsets",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var offsets = (float[])field.GetValue(manager);
            Assert.That(offsets, Has.Length.EqualTo(count));
            System.Array.Sort(offsets);

            var largestGap = 0f;
            for (var i = 1; i < offsets.Length; i++)
            {
                largestGap = Mathf.Max(largestGap, offsets[i] - offsets[i - 1]);
            }

            Assert.That(offsets[^1] - offsets[0], Is.GreaterThan(18.5f));
            Assert.That(largestGap, Is.LessThan(0.65f),
                $"A single spawn burst left a {largestGap:0.00} metre empty lateral band.");
        }

        [Test]
        public void TowerPlacement_UsesCurrentVariableWidthRoadGeometry()
        {
            var points = new[]
            {
                new Vector3(-10f, 0f, 0f),
                Vector3.zero,
                new Vector3(10f, 0f, 0f)
            };
            var route = CreateRoute(points);
            route.SetWaypoints(points, new[] { 10f, 3f, 3f });
            var definition = ScriptableObject.CreateInstance<TowerDefinition>();
            cleanupObjects.Add(definition);
            definition.id = "placement_test";
            definition.displayName = "Placement Test Tower";
            definition.behavior = TowerBehavior.Projectile;
            definition.perTypeLimit = 10;
            var managerObject = new GameObject("TestTowerManager");
            cleanupObjects.Add(managerObject);
            var manager = managerObject.AddComponent<TowerManager>();
            manager.Initialize(null, route, new[] { definition });

            Assert.That(manager.CanPlace(definition, new Vector3(-8f, 0f, 4f)), Is.False,
                "A tower was allowed on the visible wide road.");
            Assert.That(manager.CanPlace(definition, new Vector3(8f, 0f, 1f)), Is.False,
                "A tower was allowed on the visible narrow road.");
            Assert.That(manager.CanPlace(definition, new Vector3(8f, 0f, 2.4f)), Is.True,
                "Grass beside the narrow road was blocked by the old uniform-width rule.");
        }

        [UnityTest, Explicit("Allocates the complete 100K Level 5 GPU stress configuration.")]
        [Category("Performance")]
        public IEnumerator LevelFiveStressWave_AllocatesAndStartsOneHundredThousandAgents()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore("Compute shaders are unavailable on this test device.");
            }

            var content = SampleContent.Create();
            LevelDefinition levelFive = null;
            for (var i = 0; i < content.Levels.Count; i++)
            {
                var level = content.Levels[i];
                cleanupObjects.Add(level);
                cleanupObjects.Add(level.wave);
                if (level.wave?.entries != null)
                {
                    for (var entryIndex = 0; entryIndex < level.wave.entries.Length; entryIndex++)
                    {
                        cleanupObjects.Add(level.wave.entries[entryIndex].enemy);
                    }
                }
                if (level.id == "level_05")
                {
                    levelFive = level;
                }
            }
            cleanupObjects.Add(content.SkillTree);
            TowerDefinition catapult = null;
            for (var i = 0; i < content.Towers.Count; i++)
            {
                var tower = content.Towers[i];
                cleanupObjects.Add(tower);
                if (tower.id == "catapult")
                {
                    catapult = tower;
                }
            }

            Assert.That(levelFive, Is.Not.Null);
            Assert.That(levelFive.wave.totalEnemyCount, Is.EqualTo(100000));
            Assert.That(levelFive.startingLives, Is.EqualTo(100000));
            Assert.That(levelFive.wave.spawnInterval, Is.EqualTo(0.155f));
            Assert.That(levelFive.wave.randomSpawnBurstMin, Is.EqualTo(55));
            Assert.That(levelFive.wave.randomSpawnBurstMax, Is.EqualTo(70));
            Assert.That(levelFive.wave.roadHalfWidth, Is.EqualTo(26.5f));
            Assert.That(levelFive.roadWidth, Is.EqualTo(54f));
            Assert.That(levelFive.pathWaypoints.Length, Is.GreaterThanOrEqualTo(20));
            Assert.That(levelFive.groundSize.x, Is.GreaterThanOrEqualTo(300f));
            Assert.That(levelFive.groundSize.z, Is.GreaterThanOrEqualTo(350f));
            Assert.That(catapult, Is.Not.Null);
            Assert.That(catapult.range, Is.EqualTo(9.5f));
            Assert.That(catapult.splashRadius, Is.EqualTo(1.75f));
            Assert.That(catapult.knockbackDistance, Is.EqualTo(1.15f));

            var route = CreateRoute(levelFive.pathWaypoints);
            var manager = CreateManager();
            manager.BeginWave(levelFive.wave, route);
            yield return null;

            Assert.That(manager.IsRunning, Is.True);
            Assert.That(manager.TotalSpawned, Is.GreaterThan(0));
            Assert.That(manager.Performance.ShaderName, Does.Contain("GPU Compute"));
        }

        private HordeEnemyManager CreateManager()
        {
            var gameObject = new GameObject("TestHordeEnemyManager");
            cleanupObjects.Add(gameObject);
            return gameObject.AddComponent<HordeEnemyManager>();
        }

        private PathRoute CreateRoute(Vector3[] points, float[] widths = null)
        {
            var gameObject = new GameObject("TestPathRoute");
            cleanupObjects.Add(gameObject);
            var route = gameObject.AddComponent<PathRoute>();
            route.SetWaypoints(points, widths);
            return route;
        }

        private WaveDefinition CreateWave(EnemyDefinition enemy, int count, float spawnInterval, bool spawnImmediately = false)
        {
            var wave = ScriptableObject.CreateInstance<WaveDefinition>();
            cleanupObjects.Add(wave);
            wave.totalEnemyCount = count;
            wave.spawnInterval = spawnInterval;
            wave.randomSpawnBurstMin = spawnImmediately ? count : 3;
            wave.randomSpawnBurstMax = spawnImmediately ? count : 6;
            wave.entries = new[]
            {
                new WaveEntry
                {
                    enemy = enemy,
                    count = count
                }
            };
            return wave;
        }

        private EnemyDefinition CreateEnemy(float speed, float health)
        {
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            cleanupObjects.Add(enemy);
            enemy.id = "test_runner";
            enemy.displayName = "Test Runner";
            enemy.maxHealth = health;
            enemy.speed = speed;
            enemy.mass = 1f;
            enemy.lifeDamage = 1;
            enemy.killReward = 1;
            enemy.color = Color.green;
            return enemy;
        }

        private sealed class TestGpuCombatTarget : ICombatTarget
        {
            public TestGpuCombatTarget(Vector3 position, float health)
            {
                Position = position;
                CurrentHealth = health;
                MaximumHealth = health;
            }

            public Vector3 Position { get; }
            public bool IsAlive => !Destroyed;
            public CombatTargetKind TargetKind => CombatTargetKind.Barrier;
            public float CombatRadius => 0.65f;
            public float BlockCapacity => 20f;
            public float CurrentBlockedMass => 0f;
            public float CurrentHealth { get; private set; }
            public float MaximumHealth { get; }
            public float Armor => 0f;
            public float PhysicalResistance => 0f;
            public float FireResistance => 0f;
            public float SlowResistance => 0f;
            public float ThornsDamage => 0f;
            public bool WasDamaged { get; private set; }
            public bool Destroyed { get; private set; }
            public EnemyDefinition LastSource { get; private set; }

            public bool TryAddBlocker(EnemyActor enemy) => true;
            public void RemoveBlocker(EnemyActor enemy) { }
            public void TakeDamage(float damage, EnemyActor source) { }

            public void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition)
            {
                WasDamaged = authoritativeHealth < MaximumHealth;
                CurrentHealth = authoritativeHealth;
                Destroyed = destroyed;
                LastSource = sourceDefinition;
            }
        }
    }
}
