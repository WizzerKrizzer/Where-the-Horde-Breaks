using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using static TowerDefense.Tests.PrehistoricTowerTests;

namespace TowerDefense.Tests
{
    /// <summary>
    /// The prehistoric era from docs/Handoff.md: its enemies, the empty Level 1, and who can be
    /// pushed (the mammoth stops at an Orc Brute until upgraded; nothing pushes the boss).
    /// </summary>
    public sealed class PrehistoricEraTests
    {
        private readonly List<Object> cleanup = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            foreach (var item in cleanup)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            cleanup.Clear();
            yield return null;
        }

        [Test]
        public void Enemies_BelongToTheirEras()
        {
            var content = SampleContent.Create();
            var enemies = new Dictionary<string, EnemyDefinition>();
            foreach (var level in content.Levels)
            {
                foreach (var entry in level.wave.entries)
                {
                    enemies[entry.enemy.id] = entry.enemy;
                }
            }

            foreach (var entry in content.LevelOneTestWave.entries)
            {
                enemies[entry.enemy.id] = entry.enemy;
            }

            foreach (var id in new[] { "harpy", "vampire", "zombie" })
            {
                Assert.That(enemies[id].eraIndex, Is.EqualTo((int)EraId.Medieval), id);
            }

            foreach (var id in new[] { "runner", "brute", "shaman" })
            {
                Assert.That(enemies[id].eraIndex, Is.EqualTo((int)EraId.Prehistoric), id);
            }

            Assert.That(enemies["shaman"].displayName, Is.EqualTo("Shaman"));
            Assert.That(enemies["shaman"].healsEnemies, Is.True);
            Assert.That(enemies["brute"].attackDamage * enemies["brute"].alliedDamageMultiplier,
                Is.GreaterThan(enemies["runner"].attackDamage * enemies["runner"].alliedDamageMultiplier),
                "The Orc Brute must hit harder than the Goblin Runner.");
        }

        [Test]
        public void SkillTree_HasTheRockAndFourTowersWithStatsSpecialsAndTheirUpgrades()
        {
            var nodes = SampleContent.Create().SkillTree.nodes;
            var byId = new Dictionary<string, SkillNodeDefinition>();
            foreach (var node in nodes)
            {
                byId[node.id] = node;
            }

            // The rock with its 12 upgrades, and four towers with 13 nodes each.
            Assert.That(nodes.Length, Is.EqualTo(65));
            foreach (var id in new[] { "spear_unlock", "mammoth_unlock", "cave_unlock" })
            {
                Assert.That(byId[id].costs[0].currency, Is.EqualTo(CurrencyType.VictorySigil), id);
            }
            Assert.That(byId["volley_core"].startsUnlocked && byId["slingers_unlock"].startsUnlocked, Is.True);

            // The Slingers are buildable from the start, with nothing bought.
            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            cleanup.Add(tree);
            tree.nodes = nodes;
            var progression = new TowerDefense.Progression.ProgressionService(tree, new TowerDefense.Save.PlayerProfile());
            Assert.That(progression.GetEffectTotal(UpgradeEffectType.UnlockTower, "slingers"), Is.GreaterThan(0f));
            Assert.That(progression.GetEffectTotal(UpgradeEffectType.UnlockTower, "mammoth"), Is.Zero);

            // Three Kill Essence stats each: damage, speed and range or roll.
            foreach (var id in new[] { "slingers_damage", "slingers_speed", "slingers_range", "rock_damage", "rock_cooldown", "rock_roll",
                         "spear_damage", "spear_speed", "spear_range", "mammoth_damage", "mammoth_health", "mammoth_herd",
                         "cave_damage", "cave_health", "cave_capacity" })
            {
                Assert.That(byId[id].costs[0].currency, Is.EqualTo(CurrencyType.KillEssence), id);
            }

            // Three specials each for Challenge Sigils, about 3 on average, each with two Kill
            // Essence upgrades of its own.
            var specials = new[]
            {
                "slingers_stun", "slingers_ricochet", "slingers_shrapnel", "rock_quake", "rock_fire", "rock_shatter",
                "spear_poison", "spear_pierce", "spear_big_game", "mammoth_rage", "mammoth_riders", "mammoth_armor",
                "cave_shields", "cave_chief", "cave_torches"
            };
            var sigils = 0;
            foreach (var id in specials)
            {
                Assert.That(byId[id].costs[0].currency, Is.EqualTo(CurrencyType.ChallengeToken), id);
                sigils += byId[id].costs[0].amount;
                var upgrades = 0;
                foreach (var node in nodes)
                {
                    if (node.prerequisiteNodeIds != null && System.Array.IndexOf(node.prerequisiteNodeIds, id) >= 0)
                    {
                        Assert.That(node.costs[0].currency, Is.EqualTo(CurrencyType.KillEssence), node.id);
                        upgrades++;
                    }
                }

                Assert.That(upgrades, Is.EqualTo(2), id);
            }

            Assert.That(sigils / (float)specials.Length, Is.EqualTo(3f));
        }

        [Test]
        public void LevelOne_HasNoEnemiesForNow()
        {
            var content = SampleContent.Create();
            Assert.That(content.Level.id, Is.EqualTo("level_01"));
            Assert.That(content.Level.wave.entries, Is.Empty);
            Assert.That(content.LevelOneTestWave.totalEnemyCount, Is.EqualTo(215));
        }

        [UnityTest]
        public IEnumerator EmptyWave_IsCompleteAtOnce()
        {
            var world = CreateWorld(cleanup);
            world.Enemies.BeginWave(world.Level.wave, world.Route);
            yield return null;
            Assert.That(world.Enemies.IsWaveComplete, Is.True, "A wave without enemies must end at once.");
        }

        [UnityTest]
        public IEnumerator Mammoth_StopsAtAnOrcBruteUntilUpgraded()
        {
            RequireComputeShaders();
            var (stopped, thrownBefore) = default((float, int));
            yield return ChargeIntoBrutes(false, result => (stopped, thrownBefore) = result);
            yield return Clean();
            var (ran, thrownAfter) = default((float, int));
            yield return ChargeIntoBrutes(true, result => (ran, thrownAfter) = result);
            Debug.Log($"Mammoth into Orc Brutes: stopped after {stopped:F1} m with {thrownBefore} thrown; upgraded it ran {ran:F1} m and threw {thrownAfter}.");
            Assert.That(thrownBefore, Is.Zero, "The mammoth threw an Orc Brute without the upgrade.");
            // Each run starts when the Brutes come near, so the first one is a few metres ahead.
            Assert.That(stopped, Is.LessThan(ran - 4f), "The mammoth did not stop at the first Orc Brute.");
            Assert.That(thrownAfter, Is.GreaterThan(0), "The upgraded mammoth threw no Orc Brute.");
        }

        [UnityTest]
        public IEnumerator Blast_DoesNotMoveAnUnpushableEnemy()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var wave = OnlyEnemy(world, "runner", 30, out var runner);
            runner.cannotBePushed = true;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitForSpawned(world, 30);
            yield return new WaitForSeconds(1.5f);
            var center = world.Route.Sample(6f);
            Assert.That(AgentsWithin(world, center, 6f), Is.GreaterThan(3), "Too few goblins near the blast.");
            world.Enemies.DamageAndKnockbackInRadius(center, 6f, 0f, 3f, 200, out _, launch: 1.2f);
            var thrown = 0;
            var fastest = 0f;
            for (var frame = 0; frame < 60; frame++)
            {
                thrown = Mathf.Max(thrown, AirborneCount(world));
                fastest = Mathf.Max(fastest, FastestAgent(world));
                yield return null;
            }

            Assert.That(thrown, Is.Zero, "A blast threw an enemy that cannot be pushed.");
            Assert.That(fastest, Is.LessThan(runner.speed * 1.3f), "A blast pushed an enemy that cannot be pushed.");
        }

        [UnityTest]
        public IEnumerator Shaman_HealsTheGoblinsAroundIt()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var wave = OnlyEnemy(world, "runner", 24, out var runner);
            var shaman = Object.Instantiate(LevelEnemies().shaman);
            cleanup.Add(shaman);
            Assert.That(shaman.healsEnemies && shaman.healAmount > 0f, Is.True, "The Shaman does not heal.");
            // Same speed as the runners, so it stays among them.
            shaman.speed = runner.speed;
            wave.totalEnemyCount = 25;
            wave.entries = new[] { new WaveEntry { enemy = shaman, count = 1 }, new WaveEntry { enemy = runner, count = 24 } };
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitForSpawned(world, 25);
            yield return new WaitForSeconds(0.5f);
            // Wound everyone without killing: runners have 12 health.
            world.Enemies.DamageAndKnockbackInRadius(world.Route.Sample(6f), 12f, 5f, 0f, 200, out _);
            var start = Time.realtimeSinceStartup;
            while (world.Horde.TotalHealed <= 0f && Time.realtimeSinceStartup - start < 6f)
            {
                yield return null;
            }

            Debug.Log($"Shaman healed {world.Horde.TotalHealed:F0} health in {Time.realtimeSinceStartup - start:F1} s.");
            Assert.That(world.Horde.TotalHealed, Is.GreaterThan(0f), "The Shaman healed no goblin in the GPU horde.");
        }

        [UnityTest]
        public IEnumerator Slingers_KillGoblinsOnTheRoad()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var slingers = FindTower("slingers");
            world.Towers.SetAvailableTowers(new[] { slingers });
            Assert.That(world.Towers.TryPlace(slingers, new Vector3(-25f, 0f, 4.6f)), Is.True, "The Slingers could not be placed.");
            var kills = 0;
            world.Enemies.EnemyKilled += _ => kills++;
            world.Enemies.BeginWave(world.Wave, world.Route, useDataHordePrototype: true);
            var deadline = Time.realtimeSinceStartup + 25f;
            while (kills < 3 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(kills, Is.GreaterThanOrEqualTo(3), "The Slingers did not kill goblins.");
        }

        [UnityTest]
        public IEnumerator Cave_BringsTheNextCavemanOnlyAfterOneFalls()
        {
            var world = CreateWorld(cleanup);
            var cave = FindTower("cave");
            cave.barracksCapacity = 2;
            cave.barracksRespawnSeconds = 1f;
            world.Towers.SetAvailableTowers(new[] { cave });
            Assert.That(world.Towers.TryPlace(cave, new Vector3(-17.6f, 0f, 2f)), Is.True, "The Cave could not be placed.");
            var tower = world.Towers.Towers[0];
            yield return new WaitForSeconds(2.5f);
            Assert.That(Alive(tower), Is.EqualTo(2), "The Cave did not fill up to its two cavemen.");

            // All out for a while: the timer must not have run in the meantime.
            yield return new WaitForSeconds(2f);
            Kill(tower);
            yield return new WaitForSeconds(0.5f);
            Assert.That(Alive(tower), Is.EqualTo(1), "A new caveman came out at once: the timer ran while the cave was full.");
            yield return new WaitForSeconds(0.8f);
            Assert.That(Alive(tower), Is.EqualTo(2), "No caveman came out after the respawn time.");

            // Two fall: they come back one at a time on the shared timer.
            Kill(tower);
            Kill(tower);
            yield return new WaitForSeconds(1.3f);
            Assert.That(Alive(tower), Is.EqualTo(1), "The cavemen did not come back one at a time.");
            yield return new WaitForSeconds(1.1f);
            Assert.That(Alive(tower), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Rock_FallsThenRollsAgainstTheHordeAndBurnsAsAFireRock()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var wave = OnlyEnemy(world, "runner", 60, out var runner);
            runner.maxHealth = 400f;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitForSpawned(world, 60);
            yield return new WaitForSeconds(1f);
            var point = world.Route.Sample(10f);
            var dealt = 0f;
            var settings = new RollingRock.Settings
            {
                ImpactRadius = 2.5f, ImpactDamage = 30f, ImpactKnockback = 2f, ImpactLaunch = 0.9f, ImpactMaxTargets = 40,
                RollDistance = 8f, RollSpeed = 6f, RollRadius = 1.3f, RollDamage = 15f, RollKnockback = 1.2f,
                FireTrail = true, FireDamagePerTick = 3f
            };
            var rock = RollingRock.Throw(world.Enemies, point, Vector3.zero, settings, amount => dealt += amount);
            cleanup.Add(rock.gameObject);
            var thrown = 0;
            var rolledStart = Vector3.zero;
            var deadline = Time.realtimeSinceStartup + 6f;
            while (rock != null && !rock.IsRolling && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(rock != null && rock.IsRolling, Is.True, "The rock never started rolling.");
            rolledStart = rock.BallPosition;
            while (rock != null && rock.IsRolling && Time.realtimeSinceStartup < deadline)
            {
                thrown = Mathf.Max(thrown, AirborneCount(world));
                yield return null;
            }

            var end = rock != null ? rock.BallPosition : rolledStart;
            var burning = Burning(world);
            Debug.Log($"Rock: dealt {dealt:F0}, threw {thrown} at most at once, rolled from path distance {world.Enemies.EstimatePathDistance(rolledStart):F1} to {world.Enemies.EstimatePathDistance(end):F1}, {burning} goblins burning.");
            Assert.That(dealt, Is.GreaterThan(0f), "The rock hurt no goblin.");
            Assert.That(world.Enemies.EstimatePathDistance(end), Is.LessThan(world.Enemies.EstimatePathDistance(rolledStart) - 5f),
                "The rock did not roll back along the road, against the horde.");
            Assert.That(burning, Is.GreaterThan(0), "The fire rock left no burning goblins.");
        }

        // User, 2026-11-08: the rock's blast must push the goblins away from where it fell, not pull
        // them in. The goblins around the landing point are followed through the blast.
        [UnityTest]
        public IEnumerator Rock_ImpactPushesTheGoblinsAwayFromWhereItFalls()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var wave = OnlyEnemy(world, "runner", 120, out var runner);
            runner.maxHealth = 4000f;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitForSpawned(world, 120);
            var point = world.Route.Sample(10f);
            var deadline = Time.realtimeSinceStartup + 20f;
            while (AgentsWithin(world, point, 2.5f) < 8 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var settings = new RollingRock.Settings
            {
                ImpactRadius = 2.5f, ImpactDamage = 30f, ImpactKnockback = 2f, ImpactLaunch = 0.9f, ImpactMaxTargets = 40,
                RollDistance = 0f
            };
            var rock = RollingRock.Throw(world.Enemies, point, Vector3.zero, settings);
            cleanup.Add(rock.gameObject);
            // The rock lands 0.35 s after the throw; take the crowd just before it lands.
            yield return new WaitForSeconds(0.3f);
            var simulation = PrehistoricTowerTests.Simulation(world);
            var count = world.Enemies.TotalSpawned;
            var before = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(before, count);
            var center = new Vector2(point.x, point.z);
            yield return new WaitForSeconds(0.6f);
            var after = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(after, count);
            var followed = 0;
            var pushedOut = 0;
            var change = 0f;
            for (var i = 0; i < count; i++)
            {
                var start = Vector2.Distance(before[i].Position, center);
                if (before[i].Status != 1u || start > 2.5f || after[i].Status != 1u)
                {
                    continue;
                }

                var end = Vector2.Distance(after[i].Position, center);
                followed++;
                change += end - start;
                pushedOut += end > start + 0.3f ? 1 : 0;
            }

            Debug.Log($"Rock blast: {followed} goblins under it, {pushedOut} pushed out, mean change {(followed > 0 ? change / followed : 0f):F2} m.");
            Assert.That(followed, Is.GreaterThanOrEqualTo(5), "Too few goblins were under the rock.");
            Assert.That(change / followed, Is.GreaterThan(0.5f), "The blast did not push the goblins away from where the rock fell.");
            Assert.That(pushedOut, Is.GreaterThan(followed / 2), "Most goblins under the rock were not pushed out.");
        }

        [UnityTest]
        public IEnumerator Slingers_SpecialsStunAndHitMoreGoblins()
        {
            RequireComputeShaders();
            var plain = 0;
            var special = 0;
            var stunned = 0;
            yield return SlingersAgainstRunners(false, (wounded, stuns) => plain = wounded);
            yield return Clean();
            yield return SlingersAgainstRunners(true, (wounded, stuns) =>
            {
                special = wounded;
                stunned = stuns;
            });
            Debug.Log($"Slingers: {plain} goblins wounded plain, {special} with ricochet and shrapnel; {stunned} stunned at most at once.");
            Assert.That(stunned, Is.GreaterThan(0), "The stunning stones stunned no goblin.");
            Assert.That(special, Is.GreaterThan(plain), "Ricochet and shrapnel hit no more goblins than plain stones.");
        }

        private IEnumerator SlingersAgainstRunners(bool specials, System.Action<int, int> done)
        {
            var world = CreateWorld(cleanup);
            var slingers = FindTower("slingers");
            // Stones that wound but never kill, so every hit goblin stays countable.
            slingers.damage = 1f;
            if (specials)
            {
                slingers.stunChance = 1f;
                slingers.stunSeconds = 3f;
                slingers.ricochetBounces = 2;
                slingers.ricochetDamageFraction = 0.6f;
                slingers.shrapnelTargets = 3;
                slingers.shrapnelDamageFraction = 0.4f;
            }

            world.Towers.SetAvailableTowers(new[] { slingers });
            Assert.That(world.Towers.TryPlace(slingers, new Vector3(-25f, 0f, 4.6f)), Is.True, "The Slingers could not be placed.");
            var wave = OnlyEnemy(world, "runner", 80, out var runner);
            runner.maxHealth = 400f;
            UnityEngine.Random.InitState(4242);
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitForSpawned(world, 80);
            var stunned = 0;
            var until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until)
            {
                stunned = Mathf.Max(stunned, Stunned(world));
                yield return new WaitForSeconds(0.25f);
            }

            done(Wounded(world), stunned);
        }

        [UnityTest]
        public IEnumerator Rock_QuakeStunsWhereItLandsAndShattersAtTheEnd()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var wave = OnlyEnemy(world, "runner", 60, out var runner);
            runner.maxHealth = 400f;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitForSpawned(world, 60);
            yield return new WaitForSeconds(1f);
            var settings = new RollingRock.Settings
            {
                ImpactRadius = 2.5f, ImpactDamage = 1f, ImpactMaxTargets = 40,
                RollDistance = 4f, RollSpeed = 6f, RollRadius = 1.3f, RollDamage = 1f,
                QuakeRadius = 5f, QuakeSeconds = 3f,
                ShatterShards = 6, ShatterDamage = 5f
            };
            var dealtAfterRoll = 0f;
            var rolling = false;
            RollingRock rock = null;
            rock = RollingRock.Throw(world.Enemies, world.Route.Sample(10f), Vector3.zero, settings, amount =>
            {
                if (rock != null && !rock.IsRolling && rolling)
                {
                    dealtAfterRoll += amount;
                }
            });
            cleanup.Add(rock.gameObject);
            var deadline = Time.realtimeSinceStartup + 6f;
            while (rock != null && !rock.IsRolling && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            rolling = true;
            yield return new WaitForSeconds(0.3f);
            var stunned = Stunned(world);
            var shards = 0;
            while (rock != null && Time.realtimeSinceStartup < deadline)
            {
                var count = 0;
                foreach (Transform child in rock.transform)
                {
                    count += child.name == "RockShard" ? 1 : 0;
                }

                shards = Mathf.Max(shards, count);
                yield return null;
            }

            Debug.Log($"Rock: {stunned} goblins stunned by the quake, {shards} shards, {dealtAfterRoll:F0} dealt after the roll.");
            Assert.That(stunned, Is.GreaterThan(0), "The quake stunned no goblin.");
            Assert.That(shards, Is.EqualTo(6), "The rock did not burst into its shards.");
            Assert.That(dealtAfterRoll, Is.GreaterThan(0f), "The shards hit nothing.");
        }

        [UnityTest]
        public IEnumerator Spear_SpecialsPierceHitHeavyHarderAndPoison()
        {
            RequireComputeShaders();
            var plain = (wounded: 0, lost: 0f, burning: 0);
            var special = (wounded: 0, lost: 0f, burning: 0);
            yield return SpearsAgainstBrutes(false, result => plain = result);
            yield return Clean();
            yield return SpearsAgainstBrutes(true, result => special = result);
            Debug.Log($"Spears: plain wounded {plain.wounded}, took {plain.lost:F0}; special wounded {special.wounded}, took {special.lost:F0}, {special.burning} poisoned.");
            Assert.That(special.wounded, Is.GreaterThan(plain.wounded), "Piercing spears hit no more goblins.");
            Assert.That(special.lost, Is.GreaterThan(plain.lost * 1.5f), "Big-game hunters did not hit the brutes much harder.");
            Assert.That(special.burning, Is.GreaterThan(0), "The poisoned tips poisoned no goblin.");
        }

        private IEnumerator SpearsAgainstBrutes(bool specials, System.Action<(int, float, int)> done)
        {
            var world = CreateWorld(cleanup);
            var spear = Object.Instantiate(world.Spear);
            cleanup.Add(spear);
            spear.damage = 20f;
            if (specials)
            {
                spear.pierce = 3;
                spear.pierceDamageKeep = 0.7f;
                spear.heavyDamageMultiplier = 2f;
                spear.poisonDamagePerSecond = 1f;
                spear.poisonSeconds = 5f;
            }

            world.Towers.SetAvailableTowers(new[] { spear });
            Assert.That(world.Towers.TryPlace(spear, new Vector3(-25f, 0f, 4.6f)), Is.True, "The Spear Throwers could not be placed.");
            var wave = OnlyEnemy(world, "brute", 40, out var brute);
            brute.maxHealth = 600f;
            UnityEngine.Random.InitState(4242);
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitForSpawned(world, 40);
            var burning = 0;
            var until = Time.realtimeSinceStartup + 9f;
            while (Time.realtimeSinceStartup < until)
            {
                burning = Mathf.Max(burning, Burning(world));
                yield return new WaitForSeconds(0.25f);
            }

            done((Wounded(world), HealthLost(world), burning));
        }

        // User, 2026-11-08: three hunters on the rock take turns, each throw raises dust, they aim at
        // the heaviest goblin first, the spears stick in the ground where they stop, big-game hits
        // flash red and poisoned goblins are marked as poisoned (drawn green).
        [UnityTest]
        public IEnumerator Spear_HuntersTakeTurnsAimAtTheHeaviestAndLeaveSpears()
        {
            RequireComputeShaders();
            SpearVisuals.ResetCounters();
            var world = CreateWorld(cleanup);
            var spear = Object.Instantiate(world.Spear);
            cleanup.Add(spear);
            spear.heavyDamageMultiplier = 1.5f;
            spear.poisonDamagePerSecond = 1f;
            spear.poisonSeconds = 5f;
            world.Towers.SetAvailableTowers(new[] { spear });
            Assert.That(world.Towers.TryPlace(spear, new Vector3(-25f, 0f, 4.6f)), Is.True, "The Spear Throwers could not be placed.");
            var tower = world.Towers.Towers[0];
            Assert.That(tower.TargetingMode, Is.EqualTo(TowerTargetingMode.Heaviest), "A new spear rock does not aim at the heaviest goblin.");
            var hunters = 0;
            foreach (Transform child in tower.transform)
            {
                hunters += child.name.StartsWith("Hunter_") ? 1 : 0;
            }
            Assert.That(hunters, Is.EqualTo(3), "The rock does not hold three hunters.");

            var wave = OnlyEnemy(world, "runner", 30, out var runner);
            runner.maxHealth = 400f;
            OnlyEnemy(world, "brute", 3, out var brute);
            brute.maxHealth = 600f;
            wave.totalEnemyCount = 33;
            wave.entries = new[] { new WaveEntry { enemy = runner, count = 30 }, new WaveEntry { enemy = brute, count = 3 } };
            UnityEngine.Random.InitState(4242);
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return WaitForSpawned(world, 33);

            var simulation = PrehistoricTowerTests.Simulation(world);
            var checks = 0;
            var wrongPicks = 0;
            var poisoned = 0;
            var until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until)
            {
                var count = world.Enemies.TotalSpawned;
                var states = new GpuHordeSimulation.AgentState[count];
                simulation.ReadStatesSynchronous(states, count);
                var bruteInRange = false;
                var poisonedNow = 0;
                foreach (var state in states)
                {
                    if (state.Status != 1u)
                    {
                        continue;
                    }

                    var offset = new Vector2(state.Position.x - tower.Position.x, state.Position.y - tower.Position.z);
                    bruteInRange |= state.Mass >= 2.5f && offset.magnitude < spear.range - 0.5f;
                    poisonedNow += state.BurnTimer > 0f && (state.Flags & 8u) != 0u ? 1 : 0;
                }

                poisoned = Mathf.Max(poisoned, poisonedNow);
                // A GPU target answer counts as fresh for only a few frames, and a batch run can go
                // through thousands of frames a second, so ask again each frame until one is fresh.
                var index = -1;
                var answered = false;
                for (var attempt = 0; bruteInRange && !answered && attempt < 120; attempt++)
                {
                    answered = world.Enemies.TryGetHordeTargetSnapshot(tower.Position, spear.range, spear.canHitFlying,
                        TowerTargetingMode.Heaviest, out index, out _, out _);
                    if (!answered)
                    {
                        yield return null;
                    }
                }

                if (answered && index >= 0 && index < count)
                {
                    checks++;
                    wrongPicks += states[index].Mass >= 2.5f ? 0 : 1;
                }

                yield return new WaitForSeconds(0.25f);
            }

            Debug.Log($"Spear rock: {checks} heaviest checks, {wrongPicks} wrong; {SpearVisuals.DustCloudsSpawned} dust clouds, " +
                      $"{SpearVisuals.SpearsStuck} spears stuck, {world.Horde.HeavyHits} heavy hits, {poisoned} poisoned.");
            Assert.That(checks, Is.GreaterThan(0), "No brute ever came in range of the rock.");
            Assert.That(wrongPicks, Is.LessThanOrEqualTo(checks / 5), "The hunters did not aim at the brutes first.");
            Assert.That(SpearVisuals.DustCloudsSpawned, Is.GreaterThanOrEqualTo(3), "The throws raised no dust.");
            Assert.That(SpearVisuals.SpearsStuck, Is.GreaterThanOrEqualTo(1), "No spear stuck in the ground.");
            Assert.That(world.Horde.HeavyHits, Is.GreaterThan(0), "No big-game hit on a brute.");
            Assert.That(SpearVisuals.HeavyFlashesSpawned, Is.GreaterThan(0), "Big-game hits did not flash.");
            Assert.That(poisoned, Is.GreaterThan(0), "No goblin was marked as poisoned.");
        }

        [UnityTest]
        public IEnumerator Mammoth_SpecialsArmourRidersAndRage()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var mammoth = Object.Instantiate(world.Mammoth);
            cleanup.Add(mammoth);
            mammoth.beastArmor = 100f;
            mammoth.riderCount = 2;
            mammoth.riderDamage = 3f;
            mammoth.rageBonus = 0.3f;
            // Raging from the first blow, so the test need not wait for it to be worn down.
            mammoth.rageThreshold = 1f;
            world.Towers.SetAvailableTowers(new[] { mammoth });
            Assert.That(world.Towers.TryPlace(mammoth, new Vector3(-17.6f, 0f, 2f)), Is.True);
            var charge = world.Towers.Towers[0].GetComponent<BeastCharge>();
            Assert.That(charge.MaximumHealth, Is.EqualTo(mammoth.beastHealth + 100f), "The armour did not add to the mammoth's health.");
            var wave = OnlyEnemy(world, "runner", 200, out var runner);
            runner.maxHealth = 400f;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            var raged = false;
            var deadline = Time.realtimeSinceStartup + 45f;
            while (!(raged && charge.RiderVolleys >= 3) && Time.realtimeSinceStartup < deadline)
            {
                raged |= charge.IsRaging;
                yield return null;
            }

            Debug.Log($"Mammoth: {charge.RiderVolleys} rider volleys, raged {raged}.");
            Assert.That(charge.RiderVolleys, Is.GreaterThanOrEqualTo(3), "The slingers on its back never threw.");
            Assert.That(raged, Is.True, "The mammoth never raged.");
        }

        [UnityTest]
        public IEnumerator Cave_SpecialsChiefShieldsAndTorches()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var cave = FindTower("cave");
            // Five cavemen at once: the fifth out is the chief (user, 2026-11-11).
            cave.barracksCapacity = 5;
            cave.barracksRespawnSeconds = 0.5f;
            cave.alliedUnitDamageReduction = 0.25f;
            cave.chiefAuraBonus = 0.25f;
            cave.chiefHealthBonus = 1f;
            cave.torchBurnDamagePerSecond = 1f;
            cave.torchBurnSeconds = 5f;
            world.Towers.SetAvailableTowers(new[] { cave });
            Assert.That(world.Towers.TryPlace(cave, new Vector3(-25f, 0f, 4.6f)), Is.True, "The Cave could not be placed.");
            var tower = world.Towers.Towers[0];
            yield return new WaitForSeconds(2.5f);
            var units = tower.GetComponentsInChildren<AlliedUnitActor>();
            var chiefs = 0;
            foreach (var unit in units)
            {
                chiefs += unit.IsChief ? 1 : 0;
                Assert.That(unit.PhysicalResistance, Is.EqualTo(0.25f), "The cavemen carry no shields.");
                Assert.That(unit.MaximumHealth, Is.EqualTo(unit.IsChief ? cave.alliedUnitHealth * 2f : cave.alliedUnitHealth), "Wrong health.");
                // Each caveman holds five goblins, the chief ten (user, 2026-11-11).
                Assert.That(unit.BlockCapacity, Is.EqualTo(unit.IsChief ? 10f : 5f), "Wrong number of goblins held.");
            }

            // With the cave's model installed it stands in for the greybox (user, 2026-11-11).
            if (Resources.Load<GameObject>("Meshy/cave") != null)
            {
                Assert.That(GameObject.Find("Cave"), Is.Not.Null, "The cave's model is missing.");
            }

            Assert.That(units.Length, Is.EqualTo(5), "The cavemen did not come out.");
            Assert.That(chiefs, Is.EqualTo(1), "There must be exactly one chief.");

            var wave = OnlyEnemy(world, "runner", 60, out var runner);
            runner.maxHealth = 400f;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            var burning = 0;
            var deadline = Time.realtimeSinceStartup + 30f;
            while (burning == 0 && Time.realtimeSinceStartup < deadline)
            {
                burning = Burning(world);
                yield return new WaitForSeconds(0.25f);
            }

            Debug.Log($"Cave: {burning} goblins burning from the torches; the cavemen dealt {tower.DamageDealt:F0}.");
            Assert.That(tower.DamageDealt, Is.GreaterThan(0f), "The cavemen never struck a goblin.");
            Assert.That(burning, Is.GreaterThan(0), "The torches set no goblin burning.");
        }

        private static float HealthLost(World world)
        {
            var simulation = PrehistoricTowerTests.Simulation(world);
            var count = world.Enemies.TotalSpawned;
            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var lost = 0f;
            foreach (var state in states)
            {
                lost += state.Status == 1u ? Mathf.Max(0f, state.MaxHealth - state.Health) : 0f;
            }

            return lost;
        }

        // Goblins slowed to a near stop: a stun.
        private static int Stunned(World world)
        {
            var simulation = PrehistoricTowerTests.Simulation(world);
            var count = world.Enemies.TotalSpawned;
            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var stunned = 0;
            foreach (var state in states)
            {
                stunned += state.Status == 1u && state.SlowTimer > 0f && state.SlowMultiplier < 0.1f ? 1 : 0;
            }

            return stunned;
        }

        private static int Wounded(World world)
        {
            var simulation = PrehistoricTowerTests.Simulation(world);
            var count = world.Enemies.TotalSpawned;
            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var wounded = 0;
            foreach (var state in states)
            {
                wounded += state.Status == 1u && state.Health < state.MaxHealth - 0.01f ? 1 : 0;
            }

            return wounded;
        }

        [UnityTest]
        public IEnumerator Boss_WalksThroughCavemenSlowedAndStopsTheMammoth()
        {
            RequireComputeShaders();
            var world = CreateWorld(cleanup);
            var boss = Object.Instantiate(SampleContent.Create().PrehistoricBoss);
            cleanup.Add(boss);
            var cave = FindTower("cave");
            cave.barracksCapacity = 3;
            world.Towers.SetAvailableTowers(new[] { cave, world.Mammoth });
            // The cave near the first bend; the mammoth's pen further down the road.
            Assert.That(world.Towers.TryPlace(cave, new Vector3(-25f, 0f, 4.6f)), Is.True, "The Cave could not be placed.");
            Assert.That(world.Towers.TryPlace(world.Mammoth, new Vector3(-17.6f, 0f, 2f)), Is.True);
            var caveTower = world.Towers.Towers[0];
            var charge = world.Towers.Towers[1].GetComponent<BeastCharge>();
            var wave = Object.Instantiate(world.Wave);
            cleanup.Add(wave);
            wave.totalEnemyCount = 1;
            wave.entries = new[] { new WaveEntry { enemy = boss, count = 1 } };
            yield return new WaitForSeconds(2.5f);
            Assert.That(Alive(caveTower), Is.EqualTo(3), "The cavemen did not come out.");
            // The boss spawns on a random lane of the road; a fixed seed puts it on the lane that
            // runs through the cavemen, so the test does not depend on luck.
            UnityEngine.Random.InitState(12345);
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);

            var simulation = PrehistoricTowerTests.Simulation(world);
            var states = new GpuHordeSimulation.AgentState[1];
            var freeSpeed = 0f;
            var slowest = float.MaxValue;
            var cavemenHurt = false;
            var mammothFought = false;
            var bossThrown = false;
            var previous = Vector2.zero;
            var previousTime = -1f;
            var spawnedAt = -1f;
            var deadline = Time.realtimeSinceStartup + 45f;
            // The boss strikes every 1.2 s, so it may pass the cavemen before its first blow lands:
            // keep watching until it has hit them and met the mammoth.
            while (Time.realtimeSinceStartup < deadline && !(mammothFought && cavemenHurt))
            {
                yield return null;
                if (world.Enemies.TotalSpawned < 1)
                {
                    continue;
                }

                simulation.ReadStatesSynchronous(states, 1);
                var position = states[0].Position;
                if (spawnedAt < 0f)
                {
                    spawnedAt = Time.time;
                }

                if (Time.time - spawnedAt < 1f)
                {
                    continue;
                }
                bossThrown |= states[0].AirHeight > 0f;
                if (previousTime > 0f && Time.time - previousTime > 0.25f)
                {
                    var speed = (position - previous).magnitude / (Time.time - previousTime);
                    var nearCavemen = NearestCaveman(caveTower, new Vector3(position.x, 0f, position.y)) < 1.2f;
                    if (nearCavemen && !mammothFought)
                    {
                        slowest = Mathf.Min(slowest, speed);
                    }
                    else if (charge.ChargeCount == 0)
                    {
                        freeSpeed = Mathf.Max(freeSpeed, speed);
                    }

                    previous = position;
                    previousTime = Time.time;
                }
                else if (previousTime < 0f)
                {
                    previous = position;
                    previousTime = Time.time;
                }

                foreach (var unit in caveTower.GetComponentsInChildren<AlliedUnitActor>())
                {
                    cavemenHurt |= unit.CurrentHealth < unit.MaximumHealth;
                }

                mammothFought |= charge.IsFighting;
            }

            Debug.Log($"Boss: free speed {freeSpeed:F2} m/s, slowest among cavemen {slowest:F2} m/s, cavemen hurt {cavemenHurt}; mammoth ran {charge.LastRunDistance:F1} m and fought {mammothFought}.");
            Assert.That(cavemenHurt, Is.True, "The boss did not strike the cavemen.");
            Assert.That(slowest, Is.LessThan(freeSpeed * 0.9f), "The cavemen did not slow the boss.");
            Assert.That(slowest, Is.GreaterThan(0.05f), "The cavemen stopped the boss.");
            Assert.That(mammothFought, Is.True, "The mammoth did not stop at the boss.");
            Assert.That(charge.LastRunDistance, Is.LessThan(world.Mammoth.chargeDistance - 1f), "The mammoth ran its whole charge through the boss.");
            Assert.That(bossThrown, Is.False, "The mammoth threw the boss.");
        }

        private static float NearestCaveman(TowerActor cave, Vector3 position)
        {
            var nearest = float.MaxValue;
            foreach (var unit in cave.GetComponentsInChildren<AlliedUnitActor>())
            {
                if (unit.IsAlive)
                {
                    var offset = unit.Position - position;
                    offset.y = 0f;
                    nearest = Mathf.Min(nearest, offset.magnitude);
                }
            }

            return nearest;
        }

        private static int Burning(World world)
        {
            var simulation = PrehistoricTowerTests.Simulation(world);
            var count = world.Enemies.TotalSpawned;
            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var burning = 0;
            foreach (var state in states)
            {
                burning += state.Status == 1u && state.BurnTimer > 0f ? 1 : 0;
            }

            return burning;
        }

        private static int Alive(TowerActor tower)
        {
            var alive = 0;
            foreach (var unit in tower.GetComponentsInChildren<AlliedUnitActor>())
            {
                alive += unit.IsAlive ? 1 : 0;
            }

            return alive;
        }

        private static void Kill(TowerActor tower)
        {
            foreach (var unit in tower.GetComponentsInChildren<AlliedUnitActor>())
            {
                if (unit.IsAlive)
                {
                    unit.ApplyGpuCombatState(0f, true, null);
                    return;
                }
            }
        }

        private TowerDefinition FindTower(string id)
        {
            foreach (var tower in SampleContent.Create().Towers)
            {
                if (tower.id == id)
                {
                    cleanup.Add(tower);
                    return tower;
                }
            }

            Assert.Fail($"No tower {id} in the game.");
            return null;
        }

        // The Shaman and the Brute as the levels use them.
        private static (EnemyDefinition shaman, EnemyDefinition brute) LevelEnemies()
        {
            var content = SampleContent.Create();
            EnemyDefinition shaman = null;
            EnemyDefinition brute = null;
            foreach (var level in content.Levels)
            {
                foreach (var entry in level.wave.entries)
                {
                    if (entry.enemy.id == "shaman") shaman = entry.enemy;
                    if (entry.enemy.id == "brute") brute = entry.enemy;
                }
            }

            return (shaman, brute);
        }

        private IEnumerator ChargeIntoBrutes(bool upgraded, System.Action<(float, int)> done)
        {
            var world = CreateWorld(cleanup);
            world.Mammoth.pushesHeavy = upgraded;
            var wave = OnlyEnemy(world, "brute", 24, out _);
            Assert.That(world.Towers.TryPlace(world.Mammoth, new Vector3(-17.6f, 0f, 2f)), Is.True);
            var charge = world.Towers.Towers[0].GetComponent<BeastCharge>();
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            var thrown = 0;
            var deadline = Time.realtimeSinceStartup + 45f;
            while (!charge.IsFighting && Time.realtimeSinceStartup < deadline)
            {
                if (charge.IsCharging)
                {
                    thrown = Mathf.Max(thrown, AirborneCount(world));
                }

                yield return null;
            }

            Assert.That(charge.IsFighting, Is.True, "The mammoth never charged into the Orc Brutes.");
            done((charge.LastRunDistance, thrown));
        }

        private WaveDefinition OnlyEnemy(World world, string id, int count, out EnemyDefinition enemy)
        {
            enemy = null;
            foreach (var entry in world.Wave.entries)
            {
                if (entry.enemy.id == id)
                {
                    enemy = Object.Instantiate(entry.enemy);
                    cleanup.Add(enemy);
                    break;
                }
            }

            Assert.That(enemy, Is.Not.Null, id);
            var wave = Object.Instantiate(world.Wave);
            cleanup.Add(wave);
            wave.totalEnemyCount = count;
            wave.entries = new[] { new WaveEntry { enemy = enemy, count = count } };
            return wave;
        }

        private static IEnumerator WaitForSpawned(World world, int count)
        {
            var deadline = Time.realtimeSinceStartup + 20f;
            while (world.Enemies.TotalSpawned < count && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private IEnumerator Clean()
        {
            foreach (var item in cleanup)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            cleanup.Clear();
            yield return null;
        }
    }
}
