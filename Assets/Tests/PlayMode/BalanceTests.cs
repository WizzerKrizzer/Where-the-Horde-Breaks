using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using static TowerDefense.Tests.PrehistoricTowerTests;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Balance runs: sets of prehistoric towers against the Level 1 wave (170 Goblin Runners and 45
    /// Orc Brutes, 10 lives) and a ten times larger one, on the GPU horde, sped up. Each run reports
    /// kills, escapes and lives lost to Logs/Balance/report.txt. The active weapon is not used, and
    /// the per-type tower limits are lifted so larger sets can be measured.
    /// </summary>
    public sealed class BalanceTests
    {
        private const string Output = "Logs/Balance";
        private const float Speed = 4f;
        private const float GameSecondsLimit = 180f;

        private readonly List<Object> cleanup = new();

        private readonly struct Setup
        {
            public readonly string Name;
            public readonly int Spears;
            public readonly int Mammoths;
            public readonly bool Upgraded;
            public readonly int Horde;

            public Setup(string name, int spears, int mammoths, bool upgraded, int horde)
            {
                Name = name;
                Spears = spears;
                Mammoths = mammoths;
                Upgraded = upgraded;
                Horde = horde;
            }
        }

        private static readonly Setup[] Setups =
        {
            new("1 spear", 1, 0, false, 1),
            new("1 mammoth", 0, 1, false, 1),
            new("1 spear + 1 mammoth (today's limit)", 1, 1, false, 1),
            new("1 spear + 1 mammoth, all upgrades", 1, 1, true, 1),
            new("3 spears", 3, 0, false, 1),
            new("6 spears", 6, 0, false, 1),
            new("3 spears + 2 mammoths", 3, 2, false, 1),
            new("6 spears + 2 mammoths, all upgrades", 6, 2, true, 1),
            new("x10 horde: 6 spears + 2 mammoths, all upgrades", 6, 2, true, 10),
            new("x10 horde: 12 spears + 3 mammoths, all upgrades", 12, 3, true, 10)
        };

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            yield return Clean();
        }

        [UnityTest, Timeout(1800000), Explicit("Plays tower sets against the Level 1 wave and writes Logs/Balance/report.txt.")]
        public IEnumerator PlayTowerSetsAgainstTheLevelOneWave()
        {
            RequireComputeShaders();
            Directory.CreateDirectory(Output);
            var report = new StringBuilder();
            report.AppendLine("Level 1 road, wave 170 Goblin Runners (12 hp, 5.35 m/s, 1 life) + 45 Orc Brutes (72 hp, 2.28 m/s, 2 lives), 10 lives.");
            report.AppendLine("Upgrades = every rank in the tree: spears +32% damage and pierce 4; mammoth +40% damage and +40% charge rate.");
            report.AppendLine();
            foreach (var setup in Setups)
            {
                yield return Play(setup, report);
                File.WriteAllText($"{Output}/report.txt", report.ToString());
                yield return Clean();
            }

            Debug.Log(report.ToString());
        }

        private IEnumerator Play(Setup setup, StringBuilder report)
        {
            var world = CreateWorld(cleanup);
            var wave = ScaledWave(world.Wave, setup.Horde);
            world.Towers.SetPerTypeLimitBonus(world.Spear.id, 20);
            world.Towers.SetPerTypeLimitBonus(world.Mammoth.id, 20);
            if (setup.Upgraded)
            {
                world.Towers.SetPerTypeDamageMultiplier(world.Spear.id, 1.32f);
                world.Spear.pierce = 4;
                world.Towers.SetPerTypeDamageMultiplier(world.Mammoth.id, 1.4f);
                world.Towers.SetPerTypeFireRateMultiplier(world.Mammoth.id, 1.4f);
            }

            var mammoths = PlaceMammoths(world, setup.Mammoths);
            var spears = PlaceSpears(world, setup.Spears);

            var killedRunners = 0;
            var killedBrutes = 0;
            var escapedRunners = 0;
            var escapedBrutes = 0;
            var livesLost = 0;
            world.Enemies.EnemyKilled += enemy =>
            {
                if (enemy.id == "brute") killedBrutes++; else killedRunners++;
            };
            world.Enemies.EnemyEscaped += enemy =>
            {
                if (enemy.id == "brute") escapedBrutes++; else escapedRunners++;
                livesLost += enemy.lifeDamage;
            };

            var total = 0;
            foreach (var entry in wave.entries)
            {
                total += entry.count;
            }

            Time.timeScale = Speed;
            var start = Time.time;
            var lostAt = -1f;
            world.Enemies.BeginWave(wave, world.Route, useDataHordePrototype: true);
            yield return null;
            while (Time.time - start < GameSecondsLimit)
            {
                if (lostAt < 0f && livesLost >= world.Level.startingLives)
                {
                    lostAt = Time.time - start;
                }

                if (killedRunners + killedBrutes + escapedRunners + escapedBrutes >= total)
                {
                    break;
                }

                yield return null;
            }

            var seconds = Time.time - start;
            Time.timeScale = 1f;
            var spearDamage = 0f;
            foreach (var tower in world.Towers.Towers)
            {
                if (tower.Definition == world.Spear)
                {
                    spearDamage += tower.DamageDealt;
                }
            }

            var falls = 0;
            var charges = 0;
            foreach (var tower in world.Towers.Towers)
            {
                var beast = tower.GetComponent<BeastCharge>();
                if (beast != null)
                {
                    falls += beast.FallCount;
                    charges += beast.ChargeCount;
                }
            }

            var killed = killedRunners + killedBrutes;
            var verdict = livesLost < world.Level.startingLives
                ? $"WON with {world.Level.startingLives - livesLost} of {world.Level.startingLives} lives"
                : $"LOST (out of lives at {lostAt:F0} s)";
            report.AppendLine($"== {setup.Name} ==  placed {spears} spears, {mammoths} mammoths");
            report.AppendLine($"   {verdict}; lives lost {livesLost}");
            report.AppendLine($"   killed {killed}/{total} ({100f * killed / Mathf.Max(1, total):F0}%): runners {killedRunners}, brutes {killedBrutes}");
            report.AppendLine($"   escaped: runners {escapedRunners}, brutes {escapedBrutes}; wave over after {seconds:F0} s");
            report.AppendLine($"   spear damage {spearDamage:F0}" + (mammoths > 0 ? $"; mammoth charges {charges}, falls {falls}" : string.Empty));
            report.AppendLine();
        }

        private WaveDefinition ScaledWave(WaveDefinition source, int factor)
        {
            var wave = Object.Instantiate(source);
            cleanup.Add(wave);
            wave.totalEnemyCount *= factor;
            wave.randomSpawnBurstMin *= factor;
            wave.randomSpawnBurstMax *= factor;
            if (wave.spawnBurstPattern != null)
            {
                wave.spawnBurstPattern = (int[])wave.spawnBurstPattern.Clone();
                for (var i = 0; i < wave.spawnBurstPattern.Length; i++)
                {
                    wave.spawnBurstPattern[i] *= factor;
                }
            }

            wave.entries = (WaveEntry[])wave.entries.Clone();
            for (var i = 0; i < wave.entries.Length; i++)
            {
                wave.entries[i].count *= factor;
            }

            return wave;
        }

        // Pens beside the road at least one full charge from the entrance, spread along the road so
        // each beast has its own stretch to run.
        private static int PlaceMammoths(World world, int count)
        {
            var placed = 0;
            var used = new List<float>();
            for (var i = 0; i < count; i++)
            {
                var bestScore = float.MaxValue;
                var best = Vector3.zero;
                var bestDistance = 0f;
                foreach (var candidate in Candidates())
                {
                    if (!world.Towers.CanPlace(world.Mammoth, candidate))
                    {
                        continue;
                    }

                    var road = world.Route.GetNearestRoadPoint(candidate, out _);
                    var offRoad = Vector3.Distance(candidate, road);
                    var along = PathDistance(world, road);
                    if (offRoad > 5f || along < world.Mammoth.chargeDistance)
                    {
                        continue;
                    }

                    var crowded = false;
                    foreach (var other in used)
                    {
                        crowded |= Mathf.Abs(other - along) < 18f;
                    }

                    if (crowded)
                    {
                        continue;
                    }

                    // The first pen right after the first full charge, the next ones further down.
                    var score = along + offRoad * 2f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                        bestDistance = along;
                    }
                }

                if (bestScore < float.MaxValue && world.Towers.TryPlace(world.Mammoth, best))
                {
                    used.Add(bestDistance);
                    placed++;
                }
            }

            return placed;
        }

        // How far along the road a road point is, to the nearest half metre.
        private static float PathDistance(World world, Vector3 roadPoint)
        {
            var best = 0f;
            var bestSq = float.MaxValue;
            for (var distance = 0f; distance < world.Route.TotalLength; distance += 0.5f)
            {
                var offset = world.Route.Sample(distance) - roadPoint;
                offset.y = 0f;
                if (offset.sqrMagnitude < bestSq)
                {
                    bestSq = offset.sqrMagnitude;
                    best = distance;
                }
            }

            return best;
        }

        // Greedy: each Spear Throwers goes where its range covers the most road.
        private static int PlaceSpears(World world, int count)
        {
            var samples = new List<Vector3>();
            for (var distance = 0f; distance < world.Route.TotalLength; distance += 0.5f)
            {
                samples.Add(world.Route.Sample(distance));
            }

            var placed = 0;
            var rangeSq = world.Spear.range * world.Spear.range;
            for (var i = 0; i < count; i++)
            {
                var bestScore = -1;
                var best = Vector3.zero;
                foreach (var candidate in Candidates())
                {
                    if (!world.Towers.CanPlace(world.Spear, candidate))
                    {
                        continue;
                    }

                    var score = 0;
                    foreach (var sample in samples)
                    {
                        var offset = sample - candidate;
                        offset.y = 0f;
                        if (offset.sqrMagnitude <= rangeSq)
                        {
                            score++;
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (bestScore >= 0 && world.Towers.TryPlace(world.Spear, best))
                {
                    placed++;
                }
            }

            return placed;
        }

        private static IEnumerable<Vector3> Candidates()
        {
            for (var x = -34f; x <= 30f; x += 1f)
            {
                for (var z = -16f; z <= 18f; z += 1f)
                {
                    yield return new Vector3(x, 0f, z);
                }
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
            yield return null;
        }
    }
}
