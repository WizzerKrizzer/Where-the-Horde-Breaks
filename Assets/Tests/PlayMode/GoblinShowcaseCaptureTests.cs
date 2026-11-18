using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TowerDefense.Tests
{
    /// <summary>
    /// The goblin model looked at from everywhere (user, 2026-11-14: "go wild"): one goblin, a few,
    /// hundreds, thousands; the game camera from 2 m to 200 m; spawning, a narrow gap, a corner, a
    /// sprint, deaths and blasts; and cameras the game never uses (ground level, from the side, from
    /// behind, circling). Each shot builds its own road and wave with the real GPU horde, at real
    /// speed. GOBLIN_SHOTS picks shots by name ("count_1,heights"); without it all are recorded.
    /// Writes Logs/Captures/GoblinShowcase/&lt;shot&gt;/frame_####.png.
    /// </summary>
    public sealed class GoblinShowcaseCaptureTests
    {
        private const string Output = "Logs/Captures/GoblinShowcase";
        private readonly List<Object> cleanup = new();
        // Whatever the test runner's scene had in it (towers, the cave): not drawn while recording.
        // Only the renderers: the test runner itself lives in that scene, and turning its object off
        // stops the test.
        private readonly List<Renderer> hidden = new();

        private sealed class Shot
        {
            public string Name;
            public Vector3[] Road = Straight(120f);
            public float[] Widths;
            public float Width = 5.4f;
            public int Count = 1;
            // How many of them are Orc Brutes (spread through the wave), the rest Goblin Runners.
            public int Brutes;
            public float SpawnInterval = 0.05f;
            public int Burst = 1;
            // Seconds of game time before the first frame.
            public float WarmUp = 2f;
            public float Seconds = 6f;
            // Follow the middle of the living goblins; otherwise look at this distance along the road.
            public bool Follow = true;
            // Stop following at this time (keeps the camera still from a blast on).
            public float FollowUntil = float.MaxValue;
            public float At = 10f;
            // The game camera's height at a time, or null for a free camera.
            public Func<float, float> Height;
            // A free camera: time, the focus point and the road's direction there give where it stands
            // and what it looks at.
            public Func<float, Vector3, Vector3, (Vector3 from, Vector3 look)> Free;
            public Action<WaveDefinition> Tune;
            // Called once on the built world before the wave starts (to place towers).
            public Action<PrehistoricTowerTests.World> Setup;
            public Action<PrehistoricTowerTests.World, float, Vector3> Each;
        }

        // Bombs at given times (seconds) and places (metres from the focus: x along the road, z
        // across it); radius, damage (0 only throws) and push as the game's blasts take them.
        private static Action<PrehistoricTowerTests.World, float, Vector3> Bombs(params (float at, float x, float z, float radius, float damage)[] bombs)
        {
            return (world, t, focus) =>
            {
                var frame = Mathf.FloorToInt(t * 30f);
                foreach (var bomb in bombs)
                {
                    if (frame == Mathf.RoundToInt(bomb.at * 30f))
                    {
                        var at = focus + new Vector3(bomb.x, 0f, bomb.z);
                        if (bomb.damage <= 0f)
                        {
                            world.Enemies.DamageAndKnockbackInRadius(at, bomb.radius, 0f, 4f, 400, out _, launch: 1.6f);
                        }
                        else
                        {
                            // The test bomb (user, 2026-11-14), TEMPORARY numbers: everything in the
                            // core dies; a wave out to 50 m hurts (10 at the core's edge, a runner
                            // has 12, down to nothing) and pushes out, hardest near the core.
                            world.Enemies.Bomb(at, bomb.radius, 50f, bomb.damage, 10f, 4f, 6f);
                        }
                    }
                }
            };
        }

        private static Vector3[] Straight(float length)
        {
            return new[] { new Vector3(-length * 0.5f, 0f, 0f), new Vector3(length * 0.5f, 0f, 0f) };
        }

        private static List<Shot> Shots()
        {
            var shots = new List<Shot>();
            // ---- how many
            foreach (var (count, height) in new[] { (1, 2.6f), (2, 2.8f), (4, 3.2f), (6, 3.6f) })
            {
                shots.Add(new Shot { Name = $"count_{count}", Count = count, Burst = count, Height = _ => height });
            }

            shots.Add(new Shot { Name = "count_100", Count = 100, Burst = 12, WarmUp = 3f, Height = _ => 7f });
            shots.Add(new Shot { Name = "count_1000", Count = 1000, Burst = 40, Width = 12f, Road = Straight(200f), WarmUp = 5f, Height = _ => 22f });
            shots.Add(new Shot { Name = "count_10000", Count = 10000, Burst = 250, Width = 30f, Road = Straight(400f), WarmUp = 9f, Height = _ => 70f });
            shots.Add(new Shot { Name = "count_50000", Count = 50000, Burst = 900, Width = 60f, Road = Straight(700f), WarmUp = 14f, Height = _ => 170f });

            // ---- the game camera from 2 m to 200 m over one horde, 3 s a height
            var heights = new[] { 2f, 4f, 8f, 20f, 50f, 100f, 200f };
            shots.Add(new Shot
            {
                Name = "heights", Count = 4000, Burst = 120, Width = 18f, Road = Straight(400f), WarmUp = 8f,
                Seconds = heights.Length * 3f, Height = t => heights[Mathf.Min(heights.Length - 1, (int)(t / 3f))]
            });

            // ---- what they do
            shots.Add(new Shot { Name = "spawn", Count = 900, Burst = 6, SpawnInterval = 0.1f, WarmUp = 0f, Seconds = 10f, Follow = false, At = 4f, Height = _ => 10f });
            shots.Add(new Shot
            {
                Name = "bottleneck", Count = 1800, Burst = 40, WarmUp = 6f, Seconds = 14f, Follow = false, At = 24f, Height = _ => 14f,
                Road = new[] { new Vector3(-24f, 0f, 0f), new Vector3(-10f, 0f, 0f), new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(60f, 0f, 0f) },
                Widths = new[] { 12f, 12f, 3.4f, 3.4f, 12f, 12f }, Width = 12f
            });
            shots.Add(new Shot
            {
                Name = "bottleneck_low", Count = 1800, Burst = 40, WarmUp = 8f, Seconds = 10f, Follow = false, At = 24f,
                Road = new[] { new Vector3(-24f, 0f, 0f), new Vector3(-10f, 0f, 0f), new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(60f, 0f, 0f) },
                Widths = new[] { 12f, 12f, 3.4f, 3.4f, 12f, 12f }, Width = 12f,
                Free = (_, focus, forward) => (focus + forward * 9f + Vector3.up * 2.2f, focus + Vector3.up * 0.5f)
            });
            shots.Add(new Shot
            {
                Name = "corner", Count = 900, Burst = 20, WarmUp = 7f, Seconds = 10f, Follow = false, At = 30f, Height = _ => 15f, Width = 6f,
                Road = new[] { new Vector3(-30f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 30f), new Vector3(30f, 0f, 30f) }
            });
            // A sprint for the first 6 s of the wave (the wave's opening speed), then the usual walk.
            shots.Add(new Shot
            {
                Name = "sprint_then_walk", Count = 300, Burst = 30, WarmUp = 2.5f, Seconds = 9f, Height = _ => 8f,
                Tune = wave => { wave.openingSpeedDuration = 6f; wave.openingSpeedMultiplier = 2.5f; }
            });
            shots.Add(new Shot
            {
                Name = "slow_walkers", Count = 300, Burst = 30, WarmUp = 4f, Seconds = 7f, Height = _ => 6f,
                Tune = wave => wave.enemySpeedMultiplier = 0.35f
            });
            // Hits that kill, once a second, in the middle of the crowd: the bodies that fall.
            shots.Add(new Shot
            {
                Name = "deaths", Count = 700, Burst = 30, WarmUp = 6f, Seconds = 10f, Follow = false, At = 22f, Height = _ => 7f,
                Each = (world, t, focus) =>
                {
                    if (Mathf.FloorToInt(t * 30f) % 30 == 15)
                    {
                        world.Enemies.DamageInRadius(focus, 1.6f, 100f, 40, out _);
                    }
                }
            });
            // The bodies up close, from low down: a few killed in front of the camera.
            shots.Add(new Shot
            {
                Name = "deaths_close", Count = 60, Burst = 10, WarmUp = 6f, Seconds = 8f, Follow = false, At = 22f,
                Free = (_, focus, forward) => (focus - forward * 3.5f + Vector3.Cross(Vector3.up, forward) * 2.5f + Vector3.up * 2.4f, focus),
                Each = (world, t, focus) =>
                {
                    if (Mathf.FloorToInt(t * 30f) % 45 == 20)
                    {
                        world.Enemies.DamageInRadius(focus, 1.8f, 100f, 6, out _);
                    }
                }
            });
            // Blasts that throw goblins: first one that only throws, then ones that kill in the air.
            shots.Add(new Shot
            {
                Name = "blasts", Count = 900, Burst = 30, WarmUp = 6f, Seconds = 11f, Follow = false, At = 22f, Height = _ => 11f,
                Each = (world, t, focus) =>
                {
                    var frame = Mathf.FloorToInt(t * 30f);
                    if (frame == 45)
                    {
                        world.Enemies.DamageAndKnockbackInRadius(focus, 5f, 0f, 3f, 200, out _, launch: 1.3f);
                    }
                    else if (frame == 150 || frame == 240)
                    {
                        world.Enemies.DamageAndKnockbackInRadius(focus, 5f, 100f, 4f, 200, out _, launch: 1.6f);
                    }
                }
            });

            // One killing blast in the middle of a wide crowd, to see where each body goes.
            shots.Add(new Shot
            {
                Name = "one_blast", Count = 2000, Burst = 40, Width = 14f, WarmUp = 12f, Seconds = 9f, Follow = false, At = 14f, Height = _ => 9f,
                Each = (world, t, focus) =>
                {
                    if (Mathf.FloorToInt(t * 30f) == 30)
                    {
                        world.Enemies.DamageAndKnockbackInRadius(focus, 4f, 100f, 4f, 300, out _, launch: 1.6f);
                    }
                }
            });

            // ---- bombs of all kinds (user, 2026-11-14), the camera still from the first bomb on
            shots.Add(new Shot { Name = "bomb_one_goblin", Count = 1, WarmUp = 3f, Seconds = 5f, FollowUntil = 1f, Height = _ => 5f, Each = Bombs((1f, 0f, 0f, 2.5f, 100f)) });
            shots.Add(new Shot { Name = "bomb_two_goblins", Count = 2, Burst = 2, WarmUp = 3f, Seconds = 5f, FollowUntil = 1f, Height = _ => 5.5f, Each = Bombs((1f, 0f, 0f, 2.5f, 100f)) });
            shots.Add(new Shot { Name = "bomb_small_group", Count = 12, Burst = 12, WarmUp = 3f, Seconds = 5f, FollowUntil = 1f, Height = _ => 7f, Each = Bombs((1f, 0f, 0f, 3f, 100f)) });
            shots.Add(new Shot { Name = "bomb_behind_group", Count = 40, Burst = 20, WarmUp = 4f, Seconds = 5f, FollowUntil = 1f, Height = _ => 7f, Each = Bombs((1f, -2.5f, 0f, 3.5f, 100f)) });
            shots.Add(new Shot { Name = "bomb_in_front_of_group", Count = 40, Burst = 20, WarmUp = 4f, Seconds = 5f, FollowUntil = 1f, Height = _ => 7f, Each = Bombs((1f, 2.5f, 0f, 3.5f, 100f)) });
            shots.Add(new Shot { Name = "bomb_at_the_side", Count = 300, Burst = 30, WarmUp = 6f, Seconds = 6f, FollowUntil = 1f, Height = _ => 9f, Each = Bombs((1f, 0f, 2.5f, 3f, 100f)) });
            shots.Add(new Shot { Name = "bomb_only_throws", Count = 300, Burst = 30, WarmUp = 6f, Seconds = 6f, FollowUntil = 1f, Height = _ => 9f, Each = Bombs((1f, 0f, 0f, 3.5f, 0f)) });
            shots.Add(new Shot { Name = "bomb_close_4m", Count = 600, Burst = 40, Width = 10f, WarmUp = 7f, Seconds = 6f, FollowUntil = 1f, Height = _ => 4f, Each = Bombs((1f, 0f, 0f, 3f, 100f)) });
            shots.Add(new Shot
            {
                Name = "bomb_from_the_side_low", Count = 400, Burst = 30, WarmUp = 6f, Seconds = 6f, FollowUntil = 1f,
                Free = (_, focus, forward) => (focus + Vector3.Cross(forward, Vector3.up) * 11f + Vector3.up * 2f, focus + Vector3.up * 0.8f),
                Each = Bombs((1f, 0f, 0f, 3.5f, 100f))
            });
            shots.Add(new Shot
            {
                Name = "bomb_many_11m", Count = 2000, Burst = 40, Width = 12f, WarmUp = 12f, Seconds = 9f, FollowUntil = 1f, Height = _ => 11f,
                Each = Bombs((1f, 0f, 0f, 3f, 100f), (1.6f, 3.5f, -2.5f, 2.5f, 100f), (2.3f, -4f, 2f, 3f, 100f), (3f, 1f, 3f, 2f, 100f), (3.4f, -1.5f, -3f, 2.5f, 100f))
            });
            shots.Add(new Shot
            {
                Name = "bomb_many_25m", Count = 4000, Burst = 80, Width = 16f, Road = Straight(200f), WarmUp = 14f, Seconds = 9f, FollowUntil = 1f, Height = _ => 25f,
                Each = Bombs((1f, 0f, 0f, 4f, 100f), (1.5f, 9f, 4f, 3f, 100f), (2.2f, -10f, -3f, 4f, 100f), (2.8f, 4f, -5f, 2.5f, 100f), (3.3f, -5f, 5f, 3f, 100f), (3.9f, 14f, -2f, 4f, 100f))
            });

            // ---- the bomb with its wave (user, 2026-11-14): a 6 m deadly core, a push out to 50 m
            shots.Add(new Shot { Name = "wave_11m", Count = 2500, Burst = 50, Width = 14f, Road = Straight(200f), WarmUp = 14f, Seconds = 8f, FollowUntil = 1f, Height = _ => 11f, Each = Bombs((1f, 0f, 0f, 6f, 1000f)) });
            shots.Add(new Shot { Name = "wave_45m", Count = 6000, Burst = 120, Width = 16f, Road = Straight(260f), WarmUp = 16f, Seconds = 8f, FollowUntil = 1f, Height = _ => 45f, Each = Bombs((1f, 0f, 0f, 6f, 1000f)) });
            shots.Add(new Shot
            {
                Name = "wave_from_the_side_low", Count = 2500, Burst = 50, Width = 14f, Road = Straight(200f), WarmUp = 14f, Seconds = 7f, FollowUntil = 1f,
                Free = (_, focus, forward) => (focus + Vector3.Cross(forward, Vector3.up) * 16f + Vector3.up * 3f, focus + Vector3.up * 0.8f),
                Each = Bombs((1f, 0f, 0f, 6f, 1000f))
            });
            shots.Add(new Shot { Name = "wave_three_bombs_30m", Count = 6000, Burst = 120, Width = 16f, Road = Straight(260f), WarmUp = 16f, Seconds = 9f, FollowUntil = 1f, Height = _ => 30f, Each = Bombs((1f, -12f, 0f, 6f, 1000f), (2.2f, 14f, 3f, 6f, 1000f), (3.4f, 0f, -4f, 6f, 1000f)) });

            // ---- the Orc Brute (user, 2026-11-14): its own model, 2.5 m tall (was about 2 m)
            shots.Add(new Shot { Name = "brute_alone", Count = 1, Brutes = 1, WarmUp = 4f, Seconds = 6f, Height = _ => 5f });
            shots.Add(new Shot
            {
                Name = "brute_alone_side", Count = 1, Brutes = 1, WarmUp = 4f, Seconds = 6f,
                Free = (_, focus, forward) => (focus + Vector3.Cross(forward, Vector3.up) * 5f + Vector3.up * 1.4f, focus + Vector3.up * 1f)
            });
            // The same views of one goblin as of one brute (user, 2026-11-15): walking from the side
            // and from in front, a crowd from in front, and the blow at a barrier from the side and
            // from in front. Closer, as a goblin is smaller.
            shots.Add(new Shot
            {
                Name = "goblin_alone_side", Count = 1, WarmUp = 3f, Seconds = 6f,
                Free = (_, focus, forward) => (focus + Vector3.Cross(forward, Vector3.up) * 3.2f + Vector3.up * 0.9f, focus + Vector3.up * 0.65f)
            });
            shots.Add(new Shot
            {
                Name = "goblin_alone_front", Count = 1, WarmUp = 3f, Seconds = 6f, FollowUntil = 999f,
                Free = (_, focus, forward) => (focus + forward * 2f + Vector3.up * 0.85f, focus + Vector3.up * 0.65f)
            });
            shots.Add(new Shot
            {
                Name = "goblins_in_crowd_low", Count = 500, Burst = 25, WarmUp = 7f, Seconds = 8f,
                Free = (_, focus, forward) => (focus + forward * 9f + Vector3.up * 2.2f, focus + Vector3.up * 0.6f)
            });
            shots.Add(new Shot
            {
                Name = "goblin_swing", Count = 1, Width = 2.4f, WarmUp = 2f, Seconds = 8f, Follow = false, At = 10.5f,
                Setup = world =>
                {
                    world.Barrier.health = 1000000f;
                    Assert.That(world.Towers.TryPlace(world.Barrier, world.Route.Sample(11f)), Is.True, "The barrier could not be placed.");
                },
                Free = (_, focus, forward) => (focus + Vector3.Cross(forward, Vector3.up) * 3f + Vector3.up * 0.9f, focus + Vector3.up * 0.65f)
            });
            shots.Add(new Shot
            {
                Name = "goblin_swing_front", Count = 1, Width = 2.4f, WarmUp = 2f, Seconds = 8f, Follow = false, At = 10.5f,
                Setup = world =>
                {
                    world.Barrier.health = 1000000f;
                    Assert.That(world.Towers.TryPlace(world.Barrier, world.Route.Sample(11f)), Is.True, "The barrier could not be placed.");
                },
                Free = (_, focus, forward) => (focus + forward * 2.6f + Vector3.Cross(Vector3.up, forward) * 1.8f + Vector3.up * 2.3f, focus + Vector3.up * 0.5f)
            });
            // A brute first, a stream of goblins after it: they catch it up, go round it on both
            // sides and leave it behind, and it gives way (user, 2026-11-15).
            shots.Add(new Shot
            {
                Name = "brute_overtaken", Count = 16, Brutes = 1, Width = 3.2f, SpawnInterval = 0.35f, WarmUp = 5f, Seconds = 14f,
                Height = _ => 8f, Tune = wave => System.Array.Reverse(wave.entries)
            });
            // One brute stopped by a barrier, swinging its club at it, from the side (user, 2026-11-15).
            shots.Add(new Shot
            {
                Name = "brute_swing", Count = 1, Brutes = 1, Width = 2.4f, WarmUp = 3f, Seconds = 8f, Follow = false, At = 10.3f,
                Setup = world =>
                {
                    world.Barrier.health = 1000000f;
                    Assert.That(world.Towers.TryPlace(world.Barrier, world.Route.Sample(11f)), Is.True, "The barrier could not be placed.");
                },
                Free = (_, focus, forward) => (focus + Vector3.Cross(forward, Vector3.up) * 4.5f + Vector3.up * 1.4f, focus + Vector3.up * 1f)
            });
            // The same blow from in front and a little to the side, past the barrier.
            shots.Add(new Shot
            {
                Name = "brute_swing_front", Count = 1, Brutes = 1, Width = 2.4f, WarmUp = 3f, Seconds = 8f, Follow = false, At = 10.3f,
                Setup = world =>
                {
                    world.Barrier.health = 1000000f;
                    Assert.That(world.Towers.TryPlace(world.Barrier, world.Route.Sample(11f)), Is.True, "The barrier could not be placed.");
                },
                Free = (_, focus, forward) => (focus + forward * 4.5f + Vector3.Cross(Vector3.up, forward) * 2f + Vector3.up * 1.8f, focus + Vector3.up * 1f)
            });
            // One brute walking towards the camera, the whole body in view (user, 2026-11-15).
            shots.Add(new Shot
            {
                Name = "brute_alone_front", Count = 1, Brutes = 1, WarmUp = 4f, Seconds = 6f, FollowUntil = 999f,
                Free = (_, focus, forward) => (focus + forward * 3f + Vector3.up * 1.3f, focus + Vector3.up * 1f)
            });
            shots.Add(new Shot { Name = "brutes_in_crowd_11m", Count = 500, Brutes = 30, Burst = 25, WarmUp = 7f, Seconds = 8f, Height = _ => 11f });
            shots.Add(new Shot
            {
                Name = "brutes_in_crowd_low", Count = 500, Brutes = 30, Burst = 25, WarmUp = 7f, Seconds = 8f,
                Free = (_, focus, forward) => (focus + forward * 9f + Vector3.up * 2.2f, focus + Vector3.up * 0.6f)
            });
            // Corpse culling (optimization, 2026-11-16): bombs leave bodies along the road, and the
            // camera, at 40 m, slides along over them, so bodies cross the screen's edges.
            shots.Add(new Shot
            {
                Name = "corpses_pan", Count = 3000, Brutes = 150, Burst = 60, Width = 14f, Road = Straight(300f), WarmUp = 6f,
                Seconds = 9f, FollowUntil = 0.2f,
                Each = Bombs((0.3f, -14f, 0f, 4f, 1000f), (0.7f, 0f, 2f, 4f, 1000f), (1.1f, 14f, -2f, 4f, 1000f)),
                Free = (t, focus, _) =>
                {
                    var look = focus + Vector3.right * (Mathf.Clamp(t - 2f, 0f, 7f) * 7f - 24f);
                    return (look + new Vector3(0f, 38f, -14f), look);
                }
            });
            shots.Add(new Shot { Name = "brutes_bomb", Count = 500, Brutes = 30, Burst = 25, WarmUp = 7f, Seconds = 6f, FollowUntil = 1f, Height = _ => 9f, Each = Bombs((1f, 0f, 0f, 3.5f, 1000f)) });

            // ---- goblins and brutes together, for a look at the whole (user, 2026-11-16): one in
            // twenty a brute, from the game's 11, 50 and 100 m.
            shots.Add(new Shot { Name = "mix_thousands_11m", Count = 3000, Brutes = 150, Burst = 60, Width = 14f, Road = Straight(260f), WarmUp = 14f, Seconds = 8f, Height = _ => 11f });
            shots.Add(new Shot { Name = "mix_thousands_50m", Count = 8000, Brutes = 400, Burst = 160, Width = 20f, Road = Straight(360f), WarmUp = 18f, Seconds = 8f, Height = _ => 50f });
            shots.Add(new Shot { Name = "mix_thousands_100m", Count = 20000, Brutes = 1000, Burst = 400, Width = 34f, Road = Straight(600f), WarmUp = 20f, Seconds = 8f, Height = _ => 100f });
            shots.Add(new Shot
            {
                Name = "mix_thousands_low", Count = 3000, Brutes = 150, Burst = 60, Width = 14f, Road = Straight(260f), WarmUp = 14f, Seconds = 8f,
                Free = (_, focus, forward) => (focus + forward * 14f + Vector3.Cross(Vector3.up, forward) * 6f + Vector3.up * 3.5f, focus + Vector3.up * 0.6f)
            });
            // A bomb in the middle of a mixed crowd: the goblins fly, the heavier brutes less.
            shots.Add(new Shot { Name = "mix_bomb_11m", Count = 2000, Brutes = 100, Burst = 40, Width = 12f, WarmUp = 12f, Seconds = 8f, FollowUntil = 1f, Height = _ => 11f, Each = Bombs((1f, 0f, 0f, 4f, 1000f)) });
            shots.Add(new Shot { Name = "mix_bombs_50m", Count = 8000, Brutes = 400, Burst = 160, Width = 20f, Road = Straight(360f), WarmUp = 18f, Seconds = 9f, FollowUntil = 1f, Height = _ => 50f, Each = Bombs((1f, -15f, 0f, 6f, 1000f), (2.5f, 15f, 4f, 6f, 1000f), (4f, 0f, -5f, 6f, 1000f)) });
            // A mixed crowd held at a barrier: goblins and brutes at the front strike it, the rest
            // press up behind.
            shots.Add(new Shot
            {
                Name = "mix_at_barrier_11m", Count = 160, Brutes = 16, Burst = 8, Width = 3.2f, WarmUp = 12f, Seconds = 10f, Follow = false, At = 10f,
                Height = _ => 11f,
                Setup = world =>
                {
                    world.Barrier.health = 1000000f;
                    Assert.That(world.Towers.TryPlace(world.Barrier, world.Route.Sample(11f)), Is.True, "The barrier could not be placed.");
                }
            });
            shots.Add(new Shot
            {
                Name = "mix_at_barrier_low", Count = 160, Brutes = 16, Burst = 8, Width = 3.2f, WarmUp = 12f, Seconds = 10f, Follow = false, At = 9f,
                Setup = world =>
                {
                    world.Barrier.health = 1000000f;
                    Assert.That(world.Towers.TryPlace(world.Barrier, world.Route.Sample(11f)), Is.True, "The barrier could not be placed.");
                },
                Free = (_, focus, forward) => (focus + forward * 6f + Vector3.Cross(Vector3.up, forward) * 4f + Vector3.up * 2.5f, focus + Vector3.up * 0.8f)
            });

            // ---- cameras the game never uses
            shots.Add(new Shot
            {
                Name = "ground_front", Count = 500, Burst = 25, WarmUp = 5f, Seconds = 8f,
                Free = (_, focus, forward) => (focus + forward * 9f + Vector3.up * 0.7f, focus + Vector3.up * 0.7f)
            });
            shots.Add(new Shot
            {
                Name = "ground_side", Count = 500, Burst = 25, WarmUp = 5f, Seconds = 8f,
                Free = (_, focus, forward) => (focus + Vector3.Cross(Vector3.up, forward) * 7f + Vector3.up * 0.9f, focus + Vector3.up * 0.6f)
            });
            shots.Add(new Shot
            {
                Name = "chase_behind", Count = 500, Burst = 25, WarmUp = 5f, Seconds = 8f,
                Free = (_, focus, forward) => (focus - forward * 7f + Vector3.up * 3f, focus + forward * 3f + Vector3.up * 0.5f)
            });
            shots.Add(new Shot
            {
                Name = "circle_one", Count = 1, Seconds = 9f,
                Free = (t, focus, _) => (focus + Quaternion.Euler(0f, t * 40f, 0f) * new Vector3(0f, 1.1f, -2.6f), focus + Vector3.up * 0.65f)
            });
            shots.Add(new Shot
            {
                Name = "circle_group", Count = 14, Burst = 14, Seconds = 9f,
                Free = (t, focus, _) => (focus + Quaternion.Euler(0f, t * 40f, 0f) * new Vector3(0f, 2f, -4.6f), focus + Vector3.up * 0.6f)
            });
            shots.Add(new Shot
            {
                Name = "portrait", Count = 1, Seconds = 6f,
                Free = (_, focus, forward) => (focus + forward * 1.7f + Vector3.Cross(Vector3.up, forward) * 0.5f + Vector3.up * 0.95f, focus + Vector3.up * 0.85f)
            });
            // Faces up close (user, 2026-11-15: the player may stand in front of them and look).
            shots.Add(new Shot
            {
                Name = "face_goblin", Count = 1, Seconds = 4f, FollowUntil = 999f,
                Free = (_, focus, forward) => (focus + forward * 1.3f + Vector3.Cross(Vector3.up, forward) * 0.3f + Vector3.up * 0.8f, focus + Vector3.up * 0.75f)
            });
            shots.Add(new Shot
            {
                Name = "face_brute", Count = 1, Brutes = 1, Seconds = 4f, FollowUntil = 999f,
                Free = (_, focus, forward) => (focus + forward * 1.9f + Vector3.Cross(Vector3.up, forward) * 0.4f + Vector3.up * 1.35f, focus + Vector3.up * 1.3f)
            });
            // The hands round their weapons, from the club side (user, 2026-11-15).
            shots.Add(new Shot
            {
                Name = "hands_goblin", Count = 1, Seconds = 4f, FollowUntil = 999f,
                Free = (_, focus, forward) => (focus + forward * 0.5f + Vector3.Cross(Vector3.up, forward) * 1.0f + Vector3.up * 0.6f, focus + forward * 0.2f + Vector3.up * 0.35f)
            });
            shots.Add(new Shot
            {
                Name = "hands_brute", Count = 1, Brutes = 1, Seconds = 4f, FollowUntil = 999f,
                Free = (_, focus, forward) => (focus + forward * 0.8f + Vector3.Cross(Vector3.up, forward) * 1.6f + Vector3.up * 1.0f, focus + forward * 0.35f + Vector3.up * 0.8f)
            });
            return shots;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            GpuHordeSimulation.TinyLevelOffForTests = false;
            yield return Clean();
            foreach (var renderer in hidden)
            {
                if (renderer != null) renderer.enabled = true;
            }

            hidden.Clear();
        }

        [UnityTest, Timeout(3600000), Explicit("Records the goblin showcase shots to Logs/Captures/GoblinShowcase (GOBLIN_SHOTS picks them).")]
        public IEnumerator RecordShots()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            if (!GoblinModel.TryGet(out _))
            {
                Assert.Ignore("Resources/Meshy/goblin.bytes is missing (Meshy files stay local).");
            }

            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }
                }
            }

            // Without the tiny level of detail, to compare side by side (optimization, 2026-11-16).
            GpuHordeSimulation.TinyLevelOffForTests = Environment.GetEnvironmentVariable("HORDE_NO_TINY") == "1";
            var wanted = Environment.GetEnvironmentVariable("GOBLIN_SHOTS");
            var names = string.IsNullOrEmpty(wanted) ? null : new HashSet<string>(wanted.Split(','));
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Showcase Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.farClipPlane = 2000f;
            // The horde culls and picks its level of detail by the main camera.
            cameraObject.tag = "MainCamera";
            // Full HD (user, 2026-11-16).
            var target = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var recorded = 0;
            foreach (var shot in Shots())
            {
                if (names != null && !names.Contains(shot.Name))
                {
                    continue;
                }

                yield return Record(shot, camera, target, texture);
                yield return Clean();
                recorded++;
            }

            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
            Assert.That(recorded, Is.GreaterThan(0), "No shot has one of the names in GOBLIN_SHOTS.");
        }

        // Bombs every `every` seconds from 0.5 s on, at places spread along and across the crowd,
        // by real time (the frame rate decides how many frames pass between them).
        private static Action<PrehistoricTowerTests.World, float, Vector3> BombsEvery(float every, float spread, float radius)
        {
            var fired = 0;
            return (world, t, focus) =>
            {
                if (t < 0.5f + fired * every)
                {
                    return;
                }

                var x = Mathf.Sin(fired * 2.39f) * spread;
                var z = Mathf.Cos(fired * 1.71f) * Mathf.Min(spread, 3f) * 0.6f;
                world.Enemies.Bomb(focus + new Vector3(x, 0f, z), radius, 50f, 1000f, 10f, 4f, 6f);
                fired++;
            };
        }

        /// <summary>
        /// The frame rate (user, 2026-11-16): one goblin, one brute, a thousand and more goblins and
        /// brutes from the game's heights, with bombs going off, and a crowd fighting at a barrier.
        /// Each frame is everything the game does (the simulation, the horde drawn at 1920 by 1080
        /// with the game's camera) and waits for the graphics card to finish it, so the time is the
        /// whole frame's. Writes Logs/Reports/Performance/horde_fps.txt.
        /// </summary>
        [UnityTest, Timeout(3600000), Explicit("Measures frame rates of the horde and writes Logs/Reports/Performance/horde_fps.txt.")]
        public IEnumerator MeasureFrameRates()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            if (!GoblinModel.TryGet(out _))
            {
                Assert.Ignore("Resources/Meshy/goblin.bytes is missing (Meshy files stay local).");
            }

            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }
                }
            }

            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var cameraObject = new GameObject("Frame Rate Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.farClipPlane = 2000f;
            cameraObject.tag = "MainCamera";
            var target = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            camera.targetTexture = target;

            Action<PrehistoricTowerTests.World> barrier = world =>
            {
                world.Barrier.health = 1000000f;
                Assert.That(world.Towers.TryPlace(world.Barrier, world.Route.Sample(11f)), Is.True, "The barrier could not be placed.");
            };
            var cases = new List<Shot>
            {
                new Shot { Name = "1 goblin, 11 m", Count = 1, WarmUp = 3f, Height = _ => 11f },
                new Shot { Name = "1 brute, 11 m", Count = 1, Brutes = 1, WarmUp = 3f, Height = _ => 11f },
                new Shot { Name = "1000 (50 brutes), 11 m", Count = 1000, Brutes = 50, Burst = 40, Width = 12f, Road = Straight(260f), WarmUp = 12f, Height = _ => 11f },
                new Shot { Name = "1000 (50 brutes), 11 m, a bomb every 0.6 s", Count = 1000, Brutes = 50, Burst = 40, Width = 12f, Road = Straight(260f), WarmUp = 12f, Height = _ => 11f, Each = BombsEvery(0.6f, 6f, 4f) },
                new Shot { Name = "5000 (250 brutes), 50 m", Count = 5000, Brutes = 250, Burst = 120, Width = 18f, Road = Straight(360f), WarmUp = 16f, Height = _ => 50f },
                new Shot { Name = "5000 (250 brutes), 50 m, a bomb every 0.6 s", Count = 5000, Brutes = 250, Burst = 120, Width = 18f, Road = Straight(360f), WarmUp = 16f, Height = _ => 50f, Each = BombsEvery(0.6f, 20f, 6f) },
                new Shot { Name = "20000 (1000 brutes), 100 m", Count = 20000, Brutes = 1000, Burst = 400, Width = 34f, Road = Straight(600f), WarmUp = 20f, Height = _ => 100f },
                new Shot { Name = "20000 (1000 brutes), 100 m, a bomb every 0.6 s", Count = 20000, Brutes = 1000, Burst = 400, Width = 34f, Road = Straight(600f), WarmUp = 20f, Height = _ => 100f, Each = BombsEvery(0.6f, 40f, 6f) },
                new Shot { Name = "400 (40 brutes) fighting at a barrier, 11 m", Count = 400, Brutes = 40, Burst = 10, Width = 3.2f, WarmUp = 25f, Follow = false, At = 9f, Height = _ => 11f, Setup = barrier },
            };

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Horde frame rates, {DateTime.Now:yyyy-MM-dd HH:mm}, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}");
            report.AppendLine("Each frame: the simulation and the horde drawn at 1920x1080 (MSAA 4) with the game camera, waiting for the graphics card. 6 s each.");
            foreach (var shot in cases)
            {
                yield return Measure(shot, camera, target, pixel, report);
                yield return Clean();
            }

            Directory.CreateDirectory("Logs/Reports/Performance");
            File.WriteAllText("Logs/Reports/Performance/horde_fps.txt", report.ToString());
            Debug.Log("HORDE FPS\n" + report);
            Object.Destroy(target);
            Object.Destroy(pixel);
            Object.Destroy(cameraObject);
        }

        private sealed class StressCase
        {
            public string Name;
            public Shot Shot;
            public bool Draw = true;
            public bool Simulate = true;
            public bool Shadows = true;
            public int Msaa = 4;
            public int Width = 1920;
            public int Height = 1080;
            // Bombs this many times over the crowd before measuring (a road full of corpses), then
            // no more bombs while measuring.
            public int CorpseBombs;
        }

        /// <summary>
        /// The heavy test for optimization (user, 2026-11-16: a test hard enough for the RTX 5070 Ti,
        /// measured before and after). Sandboxes, not levels: 20 000 goblins and brutes on a long road
        /// with bombs, a crowd packing into a narrow pass, and a crowd fighting at a barrier. The
        /// camera renders the way the game does (at the end of each frame, without waiting for the
        /// graphics card), so a frame's time is what a player gets. The ablation cases of the first
        /// sandbox (no horde drawn, no simulation, no shadows, no MSAA, half resolution) show where
        /// the time goes. STRESS_CASES picks cases by name prefix. Writes
        /// Logs/Reports/Performance/stress.txt.
        /// </summary>
        [UnityTest, Timeout(3600000), Explicit("The heavy optimization benchmark; writes Logs/Reports/Performance/stress.txt.")]
        public IEnumerator MeasureStress()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            if (!GoblinModel.TryGet(out _))
            {
                Assert.Ignore("Resources/Meshy/goblin.bytes is missing (Meshy files stay local).");
            }

            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }
                }
            }

            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            Func<Shot> horde = () => new Shot
            {
                Name = "horde", Count = 20000, Brutes = 1000, Burst = 400, Width = 34f, Road = Straight(1400f),
                WarmUp = 25f, Follow = false, Height = _ => 100f, Each = BombsEvery(0.6f, 40f, 6f)
            };
            Func<Shot> pass = () => new Shot
            {
                Name = "pass", Count = 4000, Brutes = 200, Burst = 80, Width = 16f, WarmUp = 25f, Follow = false, At = 60f,
                Height = _ => 30f,
                Road = new[] { new Vector3(-60f, 0f, 0f), new Vector3(-6f, 0f, 0f), new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(200f, 0f, 0f) },
                Widths = new[] { 16f, 16f, 3.4f, 3.4f, 16f, 16f }
            };
            Func<Shot> barrier = () => new Shot
            {
                Name = "barrier", Count = 2000, Brutes = 100, Burst = 40, Width = 8f, Road = Straight(200f), WarmUp = 30f,
                Follow = false, At = 95f, Height = _ => 11f,
                Setup = world =>
                {
                    world.Barrier.health = 100000000f;
                    Assert.That(world.Towers.TryPlace(world.Barrier, world.Route.Sample(100f)), Is.True, "The barrier could not be placed.");
                }
            };
            Func<Shot> heavy = () => new Shot
            {
                Name = "heavy", Count = 50000, Brutes = 2500, Burst = 1500, Width = 80f, Road = Straight(2400f),
                WarmUp = 40f, Tune = wave => wave.enforceSpawnClearance = false, Follow = false, Height = _ => 100f, Each = BombsEvery(0.6f, 40f, 6f)
            };
            // The user's second heavy case (2026-11-16): 100 000 enemies with explosions.
            Func<Shot> huge = () => new Shot
            {
                Name = "huge", Count = 100000, Brutes = 5000, Burst = 3000, Width = 120f, Road = Straight(2400f),
                WarmUp = 50f, Tune = wave => wave.enforceSpawnClearance = false, Follow = false, Height = _ => 100f, Each = BombsEvery(0.6f, 40f, 6f)
            };
            var cases = new List<StressCase>
            {
                new StressCase { Name = "huge 100000 (5000 brutes), 100 m, bombs", Shot = huge() },
                new StressCase { Name = "huge - no bombs", Shot = NoBombs(huge()) },
                new StressCase { Name = "huge - no horde drawn", Shot = huge(), Draw = false },
                new StressCase { Name = "huge - no shadows", Shot = huge(), Shadows = false },
                new StressCase { Name = "huge - no simulation", Shot = huge(), Simulate = false },
                new StressCase { Name = "huge 100000, 160 m, bombs", Shot = Retarget(huge(), 160f) },
                new StressCase { Name = "huge 100000, 25 m, bombs", Shot = Retarget(huge(), 25f) },
                new StressCase { Name = "heavy 50000 (2500 brutes), 100 m, bombs", Shot = heavy() },
                new StressCase { Name = "heavy - no horde drawn", Shot = heavy(), Draw = false },
                new StressCase { Name = "heavy - no simulation", Shot = heavy(), Simulate = false },
                new StressCase { Name = "heavy - no shadows", Shot = heavy(), Shadows = false },
                new StressCase { Name = "heavy - no MSAA", Shot = heavy(), Msaa = 1 },
                new StressCase { Name = "heavy - 960x540", Shot = heavy(), Width = 960, Height = 540 },
                new StressCase { Name = "heavy 50000, 160 m, bombs", Shot = Retarget(heavy(), 160f) },
                new StressCase { Name = "heavy 50000, 25 m, bombs", Shot = Retarget(heavy(), 25f) },
                new StressCase { Name = "corpses: heavy after 40 big bombs, 100 m", Shot = heavy(), CorpseBombs = 40 },
                new StressCase { Name = "corpses: heavy after 40 big bombs, 25 m", Shot = Retarget(heavy(), 25f), CorpseBombs = 40 },
                new StressCase { Name = "horde 20000 (1000 brutes), 100 m, bombs", Shot = horde() },
                new StressCase { Name = "zoomed out 20000, 160 m, bombs", Shot = Retarget(horde(), 160f) },
                new StressCase { Name = "close 20000, 25 m, bombs", Shot = Retarget(horde(), 25f) },
                new StressCase { Name = "close 20000, 11 m, bombs", Shot = Retarget(horde(), 11f) },
                new StressCase { Name = "pass 4000 (200 brutes) into 3.4 m, 30 m", Shot = pass() },
                new StressCase { Name = "barrier 2000 (100 brutes) fighting, 11 m", Shot = barrier() },
            };

            var wanted = Environment.GetEnvironmentVariable("STRESS_CASES");
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Stress, {DateTime.Now:yyyy-MM-dd HH:mm}, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}");
            report.AppendLine("Each case 8 s; the camera renders at the end of each frame like the game, without waiting for the card.");
            foreach (var stress in cases)
            {
                if (!string.IsNullOrEmpty(wanted) && !Array.Exists(wanted.Split(','), w => stress.Name.StartsWith(w)))
                {
                    continue;
                }

                yield return MeasureStressCase(stress, report);
                yield return Clean();
            }

            Directory.CreateDirectory("Logs/Reports/Performance");
            File.WriteAllText("Logs/Reports/Performance/stress.txt", report.ToString());
            Debug.Log("STRESS\n" + report);
        }

        private IEnumerator MeasureStressCase(StressCase stress, System.Text.StringBuilder report)
        {
            var shot = stress.Shot;
            var cameraObject = new GameObject("Stress Camera");
            cleanup.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.farClipPlane = 2000f;
            cameraObject.tag = "MainCamera";
            var target = new RenderTexture(stress.Width, stress.Height, 24) { antiAliasing = stress.Msaa };
            cleanup.Add(target);
            camera.targetTexture = target;

            var world = Build(shot);
            shot.Setup?.Invoke(world);
            world.Enemies.BeginWave(world.Wave, world.Route, useDataHordePrototype: true);
            Time.timeScale = 1f;
            var focus = world.Route.Sample(shot.At);
            focus.y = 0f;
            CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, focus, shot.Height(0f));
            var started = Time.time;
            while (Time.time - started < shot.WarmUp || world.Enemies.TotalSpawned < shot.Count)
            {
                if (Time.time - started > shot.WarmUp + 40f)
                {
                    break;
                }

                yield return null;
            }

            var simulation = PrehistoricTowerTests.Simulation(world);
            if (shot.Name != "barrier" && simulation != null)
            {
                // The middle of the living crowd, once, before measuring.
                var states = new GpuHordeSimulation.AgentState[world.Enemies.TotalSpawned];
                simulation.ReadStatesSynchronous(states, states.Length);
                var sum = Vector2.zero;
                var alive = 0;
                foreach (var state in states)
                {
                    if (state.Status == 1u)
                    {
                        sum += state.Position;
                        alive++;
                    }
                }

                if (alive > 0)
                {
                    focus = new Vector3(sum.x / alive, 0f, sum.y / alive);
                }
            }

            CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, focus, shot.Height(0f));
            if (stress.CorpseBombs > 0)
            {
                for (var bomb = 0; bomb < stress.CorpseBombs; bomb++)
                {
                    var at = focus + new Vector3(Mathf.Sin(bomb * 2.39f) * 70f, 0f, Mathf.Cos(bomb * 1.71f) * 25f);
                    world.Enemies.Bomb(at, 9f, 30f, 1000f, 10f, 4f, 6f);
                    yield return null;
                }

                var settle = Time.time;
                while (Time.time - settle < 4f)
                {
                    yield return null;
                }

                shot.Each = null;
            }

            if (!stress.Draw)
            {
                camera.cullingMask &= ~(1 << world.Horde.gameObject.layer);
            }

            var shadowField = typeof(GpuHordeSimulation).GetField("airShadowMaterial",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            object shadow = null;
            if (!stress.Shadows && simulation != null)
            {
                shadow = shadowField.GetValue(simulation);
                shadowField.SetValue(simulation, null);
            }

            if (!stress.Simulate)
            {
                Time.timeScale = 0f;
            }

            var times = new List<double>();
            var gpu = new List<double>();
            var cpu = new List<double>();
            var timing = new FrameTiming[1];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var last = -1.0;
            var measureStart = Time.realtimeSinceStartup;
            var alivePeak = 0;
            var trace = Environment.GetEnvironmentVariable("STRESS_TRACE") == "1" ? new System.Text.StringBuilder() : null;
            // STRESS_PROFILE=1: wait for the graphics card after each part of a tick and time the
            // parts (the waits slow the frames; only the parts' shares count).
            var parts = new Dictionary<string, double>();
            var ticks = 0;
            if (Environment.GetEnvironmentVariable("STRESS_PROFILE") == "1" && simulation != null)
            {
                var syncBuffer = (GraphicsBuffer)typeof(GpuHordeSimulation).GetField("auxiliary",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(simulation);
                var one = new int[1];
                var partClock = System.Diagnostics.Stopwatch.StartNew();
                GpuHordeSimulation.ProfileMarkForTests = label =>
                {
                    syncBuffer.GetData(one, 0, 0, 1);
                    var elapsed = partClock.Elapsed.TotalMilliseconds;
                    partClock.Restart();
                    if (label == "before tick")
                    {
                        ticks++;
                        return;
                    }

                    parts[label] = (parts.TryGetValue(label, out var sum) ? sum : 0.0) + elapsed;
                };
            }

            while (Time.realtimeSinceStartup - measureStart < 8f)
            {
                if (stress.Simulate)
                {
                    shot.Each?.Invoke(world, Time.realtimeSinceStartup - measureStart, focus);
                }

                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                var now = clock.Elapsed.TotalMilliseconds;
                if (last >= 0.0)
                {
                    times.Add(now - last);
                    trace?.AppendLine($"{Time.realtimeSinceStartup - measureStart:F4} {now - last:F3} {world.Horde.SimulationTicksLastFrame} {world.Horde.Performance.SimMs:F3} {world.Horde.Performance.DrawMs:F3}");
                }

                last = now;
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                {
                    if (timing[0].gpuFrameTime > 0) gpu.Add(timing[0].gpuFrameTime);
                    if (timing[0].cpuMainThreadFrameTime > 0) cpu.Add(timing[0].cpuMainThreadFrameTime);
                }

                alivePeak = Mathf.Max(alivePeak, world.Enemies.ActiveEnemyCount);
            }

            GpuHordeSimulation.ProfileMarkForTests = null;
            foreach (var part in parts)
            {
                Debug.Log($"STRESSPART {stress.Name}: {part.Key} {part.Value / Mathf.Max(1, ticks):F3} ms a tick");
            }

            if (trace != null)
            {
                Directory.CreateDirectory("Logs/Reports/Performance");
                File.WriteAllText($"Logs/Reports/Performance/trace-{stress.Name.Split(',')[0].Replace(' ', '_')}.txt", trace.ToString());
            }

            Time.timeScale = 1f;
            if (shadow != null)
            {
                shadowField.SetValue(simulation, shadow);
            }

            times.Sort();
            var average = Mean(times);
            var worst = times.Count > 0 ? times[Mathf.Min(times.Count - 1, (int)(times.Count * 0.99))] : 0.0;
            var corpseCursor = 0;
            if (simulation != null)
            {
                var aux = (GraphicsBuffer)typeof(GpuHordeSimulation).GetField("auxiliary",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(simulation);
                var cursor = new int[1];
                aux.GetData(cursor, 0, 0, 1);
                corpseCursor = cursor[0];
            }

            var line = $"{stress.Name,-46} {1000.0 / average,5:F0} fps ({average:F2} ms), 1% low {1000.0 / worst:F0} fps ({worst:F2} ms)" +
                $" | GPU {Mean(gpu):F2} ms | CPU main {Mean(cpu):F2} ms | {world.Enemies.TotalSpawned} spawned, {alivePeak} alive, {corpseCursor} corpses, {times.Count} frames";
            report.AppendLine(line);
            Debug.Log("STRESS " + line);
        }

        private static Shot NoBombs(Shot shot)
        {
            shot.Each = null;
            return shot;
        }

        private static Shot Retarget(Shot shot, float height)
        {
            shot.Height = _ => height;
            return shot;
        }

        private static double Mean(List<double> values)
        {
            if (values.Count == 0)
            {
                return 0.0;
            }

            var sum = 0.0;
            foreach (var value in values)
            {
                sum += value;
            }

            return sum / values.Count;
        }

        private IEnumerator Measure(Shot shot, Camera camera, RenderTexture target, Texture2D pixel, System.Text.StringBuilder report)
        {
            var world = Build(shot);
            shot.Setup?.Invoke(world);
            world.Enemies.BeginWave(world.Wave, world.Route, useDataHordePrototype: true);
            Time.timeScale = 1f;
            var started = Time.time;
            while (Time.time - started < shot.WarmUp || world.Enemies.TotalSpawned == 0)
            {
                if (Time.time - started > shot.WarmUp + 30f)
                {
                    break;
                }

                yield return null;
            }

            var simulation = PrehistoricTowerTests.Simulation(world);
            var states = new GpuHordeSimulation.AgentState[Mathf.Max(1, shot.Count)];
            var focus = world.Route.Sample(shot.At);
            focus.y = 0f;
            var times = new List<double>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var last = -1.0;
            var measureStart = Time.time;
            var frame = 0;
            while (Time.time - measureStart < 6f)
            {
                // Where the crowd is, every 30 frames; that frame (it waits for the card) is not counted.
                var follow = shot.Follow && frame % 30 == 0 && simulation != null;
                if (follow)
                {
                    var count = Mathf.Min(states.Length, world.Enemies.TotalSpawned);
                    simulation.ReadStatesSynchronous(states, count);
                    var sum = Vector2.zero;
                    var alive = 0;
                    for (var i = 0; i < count; i++)
                    {
                        if (states[i].Status == 1u)
                        {
                            sum += states[i].Position;
                            alive++;
                        }
                    }

                    if (alive > 0)
                    {
                        focus = new Vector3(sum.x / alive, 0f, sum.y / alive);
                    }
                }

                CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, focus, shot.Height(0f));
                shot.Each?.Invoke(world, Time.time - measureStart, focus);
                PrehistoricTowerTests.DrawHorde(world);
                camera.Render();
                RenderTexture.active = target;
                pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                pixel.Apply();
                RenderTexture.active = null;
                yield return null;
                var now = clock.Elapsed.TotalMilliseconds;
                if (last >= 0.0 && !follow)
                {
                    times.Add(now - last);
                }

                last = now;
                frame++;
            }

            times.Sort();
            var average = 0.0;
            foreach (var t in times)
            {
                average += t;
            }

            average /= Mathf.Max(1, times.Count);
            var worst = times.Count > 0 ? times[Mathf.Min(times.Count - 1, (int)(times.Count * 0.99))] : 0.0;
            var line = $"{shot.Name,-50} {1000.0 / average,6:F0} fps average ({average:F1} ms), slowest 1%: {1000.0 / worst:F0} fps ({worst:F1} ms), {world.Enemies.TotalSpawned} spawned, {times.Count} frames";
            report.AppendLine(line);
            Debug.Log("HORDE FPS " + line);
        }

        private IEnumerator Record(Shot shot, Camera camera, RenderTexture target, Texture2D texture)
        {
            var world = Build(shot);
            shot.Setup?.Invoke(world);
            world.Enemies.BeginWave(world.Wave, world.Route, useDataHordePrototype: true);
            var started = Time.time;
            while (Time.time - started < shot.WarmUp || world.Enemies.TotalSpawned == 0)
            {
                if (Time.realtimeSinceStartup > 0f && Time.time - started > shot.WarmUp + 30f)
                {
                    break;
                }

                yield return null;
            }

            var folder = $"{Output}/{shot.Name}";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            var simulation = PrehistoricTowerTests.Simulation(world);
            var states = new GpuHordeSimulation.AgentState[Mathf.Max(1, shot.Count)];
            var focus = world.Route.Sample(shot.At);
            focus.y = 0f;
            var forward = Vector3.right;
            var first = true;
            const float frameSeconds = 1f / 30f;
            // The horde runs on real time scaled by Time.timeScale: slowed so each capture has time,
            // and everything moves at its true speed in the video.
            Time.timeScale = 0.2f;
            var next = Time.time;
            var frames = Mathf.RoundToInt(shot.Seconds * 30f);
            for (var frame = 0; frame < frames; frame++)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += frameSeconds;
                var t = frame * frameSeconds;
                if (shot.Follow && t < shot.FollowUntil && simulation != null)
                {
                    var count = Mathf.Min(states.Length, world.Enemies.TotalSpawned);
                    simulation.ReadStatesSynchronous(states, count);
                    var sum = Vector2.zero;
                    var heading = Vector2.zero;
                    var alive = 0;
                    for (var i = 0; i < count; i++)
                    {
                        if (states[i].Status == 1u)
                        {
                            sum += states[i].Position;
                            heading += states[i].Facing;
                            alive++;
                        }
                    }

                    if (alive > 0)
                    {
                        var middle = new Vector3(sum.x / alive, 0f, sum.y / alive);
                        // A bomb shot keeps the goblins exactly in the middle, so the bomb lands on them.
                        focus = first || shot.FollowUntil < float.MaxValue ? middle : Vector3.Lerp(focus, middle, 0.25f);
                        if (heading.sqrMagnitude > 0.01f)
                        {
                            var ahead = new Vector3(heading.x, 0f, heading.y).normalized;
                            forward = first ? ahead : Vector3.Slerp(forward, ahead, 0.1f);
                        }

                        first = false;
                    }
                }

                if (shot.Height != null)
                {
                    CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, focus, shot.Height(t));
                }
                else
                {
                    var (from, look) = shot.Free(t, focus, forward);
                    camera.transform.position = from;
                    camera.transform.LookAt(look);
                }

                shot.Each?.Invoke(world, t, focus);
                PrehistoricTowerTests.DrawHorde(world);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
            }

            Time.timeScale = 1f;
            Debug.Log($"GOBLIN SHOWCASE {shot.Name}: {world.Enemies.TotalSpawned} of {shot.Count} goblins spawned, {frames} frames.");
        }

        // A road of the shot's shape with the GPU horde on it and a wave of Goblin Runners only.
        private PrehistoricTowerTests.World Build(Shot shot)
        {
            var content = SampleContent.Create();
            var level = Object.Instantiate(content.Level);
            cleanup.Add(level);
            var widths = shot.Widths ?? new float[shot.Road.Length];
            if (shot.Widths == null)
            {
                for (var i = 0; i < widths.Length; i++)
                {
                    widths[i] = shot.Width;
                }
            }

            level.pathWaypoints = shot.Road;
            level.pathWaypointWidths = widths;
            level.secondaryPathWaypoints = null;
            level.tertiaryPathWaypoints = null;
            level.roadWidth = shot.Width;
            level.useSquarePathCorners = false;
            var low = shot.Road[0];
            var high = shot.Road[0];
            foreach (var point in shot.Road)
            {
                low = Vector3.Min(low, point);
                high = Vector3.Max(high, point);
            }

            level.groundCenter = new Vector3((low.x + high.x) * 0.5f, -0.08f, (low.z + high.z) * 0.5f);
            level.groundSize = new Vector3(high.x - low.x + 400f, 0.1f, high.z - low.z + 400f);

            EnemyDefinition runner = null;
            EnemyDefinition brute = null;
            foreach (var entry in content.LevelOneTestWave.entries)
            {
                if (entry.enemy.id == "runner")
                {
                    runner = entry.enemy;
                }
                else if (entry.enemy.id == "brute")
                {
                    brute = entry.enemy;
                }
            }

            Assert.That(runner, Is.Not.Null, "No Goblin Runner in the game's enemies.");
            var wave = Object.Instantiate(content.LevelOneTestWave);
            cleanup.Add(wave);
            wave.totalEnemyCount = shot.Count;
            if (shot.Brutes > 0)
            {
                Assert.That(brute, Is.Not.Null, "No Orc Brute in the game's enemies.");
                // Brutes spread through the wave: one entry of each in turn.
                var entries = new List<WaveEntry>();
                var runners = shot.Count - shot.Brutes;
                var brutes = shot.Brutes;
                while (runners > 0 || brutes > 0)
                {
                    var take = brutes > 0 ? Mathf.Max(1, runners / brutes) : runners;
                    if (runners > 0) entries.Add(new WaveEntry { enemy = runner, count = Mathf.Min(take, runners) });
                    runners -= Mathf.Min(take, runners);
                    if (brutes > 0) entries.Add(new WaveEntry { enemy = brute, count = 1 });
                    brutes--;
                }

                wave.entries = entries.ToArray();
            }
            else
            {
                wave.entries = new[] { new WaveEntry { enemy = runner, count = shot.Count } };
            }
            wave.spawnInterval = shot.SpawnInterval;
            wave.randomSpawnBurstMin = shot.Burst;
            wave.randomSpawnBurstMax = shot.Burst;
            wave.spawnBurstPattern = null;
            wave.roadHalfWidth = shot.Width * 0.5f;
            shot.Tune?.Invoke(wave);

            var world = new PrehistoricTowerTests.World { Level = level, Wave = wave };
            var routeObject = new GameObject("Showcase Route");
            cleanup.Add(routeObject);
            world.Route = routeObject.AddComponent<PathRoute>();
            world.Route.SetWaypoints(shot.Road, widths);
            world.Route.SetSquareCorners(false);
            RoadSurface.Create(routeObject.transform, world.Route, level);
            var hordeObject = new GameObject("Showcase Horde");
            cleanup.Add(hordeObject);
            world.Horde = hordeObject.AddComponent<HordeEnemyManager>();
            var enemiesObject = new GameObject("Showcase Enemies");
            cleanup.Add(enemiesObject);
            world.Enemies = enemiesObject.AddComponent<EnemyManager>();
            world.Enemies.SetHordePrototype(world.Horde);
            world.Enemies.SetLevelRoute(world.Route);
            if (shot.Setup != null)
            {
                // Towers only for the shots that place some.
                foreach (var definition in content.Towers)
                {
                    if (definition.id == "barrier")
                    {
                        world.Barrier = Object.Instantiate(definition);
                        cleanup.Add(world.Barrier);
                    }
                }

                Assert.That(world.Barrier, Is.Not.Null, "No Timber Barrier in the game's towers.");
                var towersObject = new GameObject("Showcase Towers");
                cleanup.Add(towersObject);
                world.Towers = towersObject.AddComponent<TowerManager>();
                world.Towers.Initialize(world.Enemies, world.Route, new[] { world.Barrier });
            }

            return world;
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
