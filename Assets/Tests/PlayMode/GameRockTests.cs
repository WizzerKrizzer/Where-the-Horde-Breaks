using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Input;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Plays the rock in the real game (the Main scene, Level 2) with simulated mouse input: a click
    /// where it should fall, the pointer moved the way it should roll, a second click. Records it to
    /// Logs/GameRock. The player's profile is copied first and put back afterwards, byte for byte.
    /// </summary>
    public sealed class GameRockTests
    {
        private const string Output = "Logs/GameRock";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private string profilePath;
        private byte[] profileBackup;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            profilePath = Path.Combine(Application.persistentDataPath, "profile.json");
            profileBackup = File.Exists(profilePath) ? File.ReadAllBytes(profilePath) : null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            // Put the profile back only after the scene that saved it is gone.
            if (profileBackup != null)
            {
                File.WriteAllBytes(profilePath, profileBackup);
            }
            else if (File.Exists(profilePath))
            {
                File.Delete(profilePath);
            }
        }

        [UnityTest, Explicit("Plays the rock on Level 2 with simulated clicks and records it to Logs/GameRock.")]
        public IEnumerator Rock_ClickThenClickAgain_RollsTheChosenWay()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var weapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            var enemies = Object.FindFirstObjectByType<EnemyManager>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            Assert.That(session != null && router != null && weapon != null && enemies != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_02");
            yield return null;
            // Feed the input by hand from here on.
            router.enabled = false;
            var current = typeof(PlayerInputRouter).GetProperty("Current");
            void Feed(GameInputState state) => current.SetValue(router, state);
            Feed(default);
            session.StartLevel();
            Assert.That(weapon.Rock, Is.True, "Level 2 does not use the rock.");
            weapon.DevAutoActiveEnabled = false;

            // Wait for the horde to come along the road, then aim at its front.
            var aim = Vector3.zero;
            var deadline = Time.realtimeSinceStartup + 40f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (enemies.TotalSpawned > 60 && enemies.TryGetLeadEnemyAimPoint(weapon.Radius, out aim))
                {
                    break;
                }

                yield return null;
            }

            Assert.That(enemies.TotalSpawned, Is.GreaterThan(60), "The horde never came.");
            var route = enemies.Route;
            var onRoad = route.GetNearestRoadPoint(aim, out _);
            var pathDistance = enemies.EstimatePathDistance(onRoad);
            // Roll it back against the horde: towards the road's start.
            var against = route.Sample(Mathf.Max(0f, pathDistance - 4f)) - onRoad;
            against.y = 0f;
            if (against.sqrMagnitude < 1f)
            {
                // Right at the road's start: roll it on along the road instead.
                against = route.Sample(Mathf.Min(route.TotalLength, pathDistance + 4f)) - onRoad;
                against.y = 0f;
            }

            against = against.normalized;

            var camera = CreateCamera(onRoad);
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            if (Directory.Exists(Output))
            {
                Directory.Delete(Output, true);
            }

            Directory.CreateDirectory(Output);
            var frame = 0;
            IEnumerator Shot()
            {
                DrawHorde(horde);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/frame_{frame++:D4}.png");
            }

            Time.timeScale = 0.25f;
            // Click: press and let go on the same point.
            Feed(new GameInputState { FireActive = true, FireHeld = true, PointerWorld = onRoad });
            yield return null;
            Feed(new GameInputState { FireReleased = true, PointerWorld = onRoad });
            yield return null;
            Assert.That(weapon.IsAimingRock, Is.True, "A click did not start aiming the rock.");
            // Move the pointer the way it should roll; the rock waits for the second click.
            for (var i = 1; i <= 20; i++)
            {
                Feed(new GameInputState { PointerWorld = onRoad + against * (3.5f * i / 20f) });
                yield return Shot();
            }

            Assert.That(Object.FindFirstObjectByType<RollingRock>(), Is.Null, "The rock fell before the second click.");
            Feed(new GameInputState { FireActive = true, FireHeld = true, PointerWorld = onRoad + against * 3.5f });
            yield return null;
            Feed(default);
            var rock = Object.FindFirstObjectByType<RollingRock>();
            Assert.That(rock, Is.Not.Null, "The second click threw no rock.");
            var startDistance = -1f;
            var startPosition = Vector3.zero;
            var end = Vector3.zero;
            var next = Time.time;
            while (rock != null && frame < 400)
            {
                if (Time.time >= next)
                {
                    next += 1f / 30f;
                    yield return Shot();
                }
                else
                {
                    yield return null;
                }

                if (rock == null)
                {
                    break;
                }

                if (rock.IsRolling && startDistance < 0f)
                {
                    startDistance = enemies.EstimatePathDistance(rock.BallPosition);
                    startPosition = rock.BallPosition;
                }

                end = rock.BallPosition;
                if (!rock.IsRolling && startDistance >= 0f)
                {
                    break;
                }
            }

            // A little after the roll, to see the goblins settle.
            for (var i = 0; i < 30; i++)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += 1f / 30f;
                yield return Shot();
            }

            Time.timeScale = 1f;
            Object.Destroy(target);
            Object.Destroy(texture);
            var rolled = end - startPosition;
            rolled.y = 0f;
            var along = Vector3.Dot(rolled, against);
            Debug.Log($"Game rock: fell at {onRoad}, rolled {rolled.magnitude:F1} m, {along:F1} m the chosen way, {frame} frames.");
            Assert.That(startDistance, Is.GreaterThanOrEqualTo(0f), "The rock never rolled.");
            Assert.That(along, Is.GreaterThan(4f), "The rock did not roll the chosen way.");
            Assert.That(along, Is.GreaterThan(rolled.magnitude * 0.95f), "The rock rolled off the chosen line.");
        }

        // User, 2026-11-09: in a very big crowd the rock pushed nothing. Waits for the Level 2 horde
        // to pack, drops the rock on the densest spot and rolls it against the flow, then measures
        // how far the goblins under it were pushed and how many are left in its path.
        [UnityTest, Explicit("Drops the rock into the packed Level 2 horde and records it to Logs/GameRockDense.")]
        public IEnumerator Rock_InAPackedCrowd_PushesAndClearsAChannel()
        {
            return PackedCrowd(false, "Logs/GameRockDense");
        }

        // User, 2026-11-09: the same, but in the middle of a big crowd and rolled the way with the
        // most goblins ahead and beside it, so the thrown goblins land among others, not in the open.
        [UnityTest, Explicit("Rolls the rock through the thick of the Level 2 horde and records it to Logs/GameRockThick.")]
        public IEnumerator Rock_ThroughTheThickOfTheCrowd()
        {
            return PackedCrowd(true, "Logs/GameRockThick");
        }

        private IEnumerator PackedCrowd(bool intoTheThick, string folder)
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var weapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            var enemies = Object.FindFirstObjectByType<EnemyManager>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            var current = typeof(PlayerInputRouter).GetProperty("Current");
            void Feed(GameInputState state) => current.SetValue(router, state);
            Feed(default);
            session.StartLevel();
            weapon.DevAutoActiveEnabled = false;
            var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager).GetField("gpuSimulation", Private).GetValue(horde);
            var settle = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < settle)
            {
                yield return null;
            }

            // The densest spot: the goblin with the most others within 2.5 m (5 m for the thick of it).
            var spotRadiusSq = intoTheThick ? 25f : 6.25f;
            var count = enemies.TotalSpawned;
            var states = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(states, count);
            var best = -1;
            var bestNeighbours = 0;
            for (var i = 0; i < count; i += 7)
            {
                if (states[i].Status != 1u)
                {
                    continue;
                }

                var neighbours = 0;
                for (var j = 0; j < count; j++)
                {
                    neighbours += states[j].Status == 1u && (states[j].Position - states[i].Position).sqrMagnitude < spotRadiusSq ? 1 : 0;
                }

                if (neighbours > bestNeighbours)
                {
                    bestNeighbours = neighbours;
                    best = i;
                }
            }

            Assert.That(best, Is.GreaterThanOrEqualTo(0), "No goblins on the field.");
            var center2 = states[best].Position;
            var point = new Vector3(center2.x, 0f, center2.y);
            // Against the flow: opposite the mean walking direction around the spot.
            var flow = Vector2.zero;
            for (var j = 0; j < count; j++)
            {
                if (states[j].Status == 1u && (states[j].Position - center2).sqrMagnitude < 25f)
                {
                    flow += states[j].Velocity;
                }
            }

            var against = flow.sqrMagnitude > 0.0001f ? new Vector3(-flow.x, 0f, -flow.y).normalized : Vector3.left;
            if (intoTheThick)
            {
                // Aim so the whole roll stays in the crowd (user, 2026-11-09): try drop points on
                // goblins and 16 ways from each; the best roll has goblins all along its 8 m line
                // (every metre of it), then the most goblins within 3 m of the line.
                var rollLength = weapon.RockRollDistance;
                var bestCovered = -1;
                var bestAlong = -1;
                for (var c = 0; c < count; c += Mathf.Max(1, count / 160))
                {
                    if (states[c].Status != 1u)
                    {
                        continue;
                    }

                    var from = states[c].Position;
                    for (var k = 0; k < 16; k++)
                    {
                        var angle = k * Mathf.PI / 8f;
                        var way = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        var perMetre = new int[Mathf.Max(1, Mathf.CeilToInt(rollLength))];
                        var near = 0;
                        for (var j = 0; j < count; j++)
                        {
                            if (states[j].Status != 1u)
                            {
                                continue;
                            }

                            var offset = states[j].Position - from;
                            var along = Vector2.Dot(offset, way);
                            if (along < 0f || along > rollLength)
                            {
                                continue;
                            }

                            var side = Mathf.Abs(offset.x * way.y - offset.y * way.x);
                            if (side < 1f)
                            {
                                perMetre[Mathf.Min(perMetre.Length - 1, (int)along)]++;
                            }

                            near += side < 3f ? 1 : 0;
                        }

                        var covered = 0;
                        foreach (var inMetre in perMetre)
                        {
                            covered += inMetre >= 2 ? 1 : 0;
                        }

                        if (covered > bestCovered || (covered == bestCovered && near > bestAlong))
                        {
                            bestCovered = covered;
                            bestAlong = near;
                            center2 = from;
                            point = new Vector3(from.x, 0f, from.y);
                            against = new Vector3(way.x, 0f, way.y);
                        }
                    }
                }

                Debug.Log($"Thick rock: {bestCovered} of {Mathf.CeilToInt(rollLength)} metres of its roll in the crowd, {bestAlong} goblins within 3 m of the line.");
            }

            var camera = CreateCamera(point + against * 4f);
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            var frame = 0;
            IEnumerator Shot()
            {
                DrawHorde(horde);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame++:D4}.png");
            }

            Time.timeScale = 0.25f;
            Feed(new GameInputState { FireActive = true, FireHeld = true, PointerWorld = point });
            yield return null;
            Feed(new GameInputState { FireReleased = true, PointerWorld = point });
            yield return null;
            for (var i = 1; i <= 15; i++)
            {
                Feed(new GameInputState { PointerWorld = point + against * (3.5f * i / 15f) });
                yield return Shot();
            }

            // The channel through the packed part only: the rest of its roll runs out over open
            // ground, where the scattered goblins land.
            const float roll = 3f;
            var before = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(before, count);
            var inPathBefore = InPath(before, point, against, roll);
            var corpsesBefore = simulation.ReadCorpseCountSynchronous();
            Feed(new GameInputState { FireActive = true, FireHeld = true, PointerWorld = point + against * 3.5f });
            yield return null;
            Feed(default);
            var rock = Object.FindFirstObjectByType<RollingRock>();
            Assert.That(rock, Is.Not.Null, "The second click threw no rock.");
            var next = Time.time;
            GpuHordeSimulation.AgentState[] afterImpact = null;
            while (rock != null && frame < 400)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += 1f / 30f;
                yield return Shot();
                if (rock == null)
                {
                    break;
                }

                // The blast's effect, once the thrown goblins have come down (about a second).
                if (afterImpact == null && rock.IsRolling && rock.Rolled > 5f)
                {
                    afterImpact = new GpuHordeSimulation.AgentState[count];
                    simulation.ReadStatesSynchronous(afterImpact, count);
                }

                if (!rock.IsRolling && afterImpact != null)
                {
                    break;
                }
            }

            // The channel, as the rock comes to a stop.
            var after = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(after, count);
            for (var i = 0; i < 24; i++)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += 1f / 30f;
                yield return Shot();
            }
            Time.timeScale = 1f;
            Object.Destroy(target);
            Object.Destroy(texture);

            // Thrown goblins must come down on the road, not on the ground beside it.
            var settled = new GpuHordeSimulation.AgentState[count];
            simulation.ReadStatesSynchronous(settled, count);
            var field = (HordeFlowField)typeof(HordeEnemyManager).GetField("flowField", Private).GetValue(horde);
            var offRoad = 0;
            var alive = 0;
            for (var i = 0; i < count; i++)
            {
                if (settled[i].Status != 1u)
                {
                    continue;
                }

                alive++;
                var p = new Vector3(settled[i].Position.x, 0f, settled[i].Position.y);
                if (!field.IsWalkable(p))
                {
                    offRoad++;
                    if (offRoad <= 5)
                    {
                        Debug.Log($"Off road at {p}.");
                    }
                }
            }

            Debug.Log($"Rock landing: {offRoad} of {alive} living goblins off the road.");
            Assert.That(offRoad, Is.Zero, "Thrown goblins came down off the road.");

            // Every goblin killed leaves a body (user, 2026-11-09: they used to vanish).
            var died = 0;
            for (var i = 0; i < count; i++)
            {
                died += before[i].Status == 1u && settled[i].Status == 3u ? 1 : 0;
            }

            var corpses = simulation.ReadCorpseCountSynchronous() - corpsesBefore;
            // How many lie in the open, not under a living goblin, and how far from the drop.
            var uncovered = 0;
            var meanDistance = 0f;
            for (var c = corpsesBefore; c < corpsesBefore + corpses; c++)
            {
                var rest = simulation.ReadCorpseRestSynchronous(c);
                meanDistance += Vector2.Distance(rest, center2) / Mathf.Max(1, corpses);
                var covered = false;
                for (var i = 0; i < count && !covered; i++)
                {
                    covered = settled[i].Status == 1u && (settled[i].Position - rest).sqrMagnitude < 0.35f * 0.35f;
                }

                uncovered += covered ? 0 : 1;
            }

            Debug.Log($"Rock corpses: {died} goblins died, {corpses} corpses recorded, {uncovered} in the open, " +
                      $"mean {meanDistance:F2} m from the drop.");
            Assert.That(died, Is.GreaterThan(0), "The rock killed nobody.");
            Assert.That(corpses, Is.GreaterThanOrEqualTo(died), "Some goblins died without leaving a corpse.");

            var followed = 0;
            var pushedOut = 0;
            var change = 0f;
            afterImpact ??= after;
            for (var i = 0; i < count; i++)
            {
                var start = Vector2.Distance(before[i].Position, center2);
                if (before[i].Status != 1u || start > weapon.Radius || afterImpact[i].Status != 1u)
                {
                    continue;
                }

                var end = Vector2.Distance(afterImpact[i].Position, center2);
                followed++;
                change += end - start;
                pushedOut += end > start + 0.3f ? 1 : 0;
            }

            var inPathAfter = InPath(afterImpact, point, against, roll);
            Debug.Log($"Dense rock: {bestNeighbours} goblins within 2.5 m of the spot; impact pushed {pushedOut} of {followed} out, " +
                      $"mean {(followed > 0 ? change / followed : 0f):F2} m; in its path {inPathBefore} before, {inPathAfter} after.");
            if (intoTheThick)
            {
                yield break;
            }

            Assert.That(change / Mathf.Max(1, followed), Is.GreaterThan(0.5f), "The impact did not push the packed goblins away.");
            Assert.That(inPathAfter, Is.LessThan(inPathBefore * 0.6f), "The roll did not clear a channel through the packed crowd.");
        }

        // Living goblins within 0.9 m of the rock's roll line.
        private static int InPath(GpuHordeSimulation.AgentState[] states, Vector3 start, Vector3 direction, float length)
        {
            var a = new Vector2(start.x, start.z);
            var d = new Vector2(direction.x, direction.z);
            var inside = 0;
            foreach (var state in states)
            {
                if (state.Status != 1u)
                {
                    continue;
                }

                var along = Vector2.Dot(state.Position - a, d);
                if (along < 0f || along > length)
                {
                    continue;
                }

                var side = (state.Position - (a + d * along)).magnitude;
                inside += side < 0.9f ? 1 : 0;
            }

            return inside;
        }

        private static Camera CreateCamera(Vector3 lookAt)
        {
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Rock Test Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.transform.position = lookAt + new Vector3(0f, 16f, -6f);
            camera.transform.LookAt(lookAt);
            return camera;
        }

        private static void DrawHorde(HordeEnemyManager horde)
        {
            if (horde == null)
            {
                return;
            }

            var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager).GetField("gpuSimulation", Private).GetValue(horde);
            var mesh = (Mesh)typeof(HordeEnemyManager).GetField("mesh", Private).GetValue(horde);
            if (simulation == null || mesh == null)
            {
                return;
            }

            simulation.RefreshVisibilityForRendering();
            simulation.Draw(mesh, horde.gameObject.layer);
        }
    }
}
