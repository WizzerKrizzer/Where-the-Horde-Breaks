using System.Collections;
using System.IO;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Input;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// The tamed mammoth's model and its pen playing Level 2 of the real game (the Main scene), its
    /// road and wave as they are (user, 2026-11-11). A mammoth pen is added beside the road halfway
    /// along; nothing else in the level changes. Once the mammoth first runs out, a camera follows it
    /// (CAPTURE_HEIGHT, 20 m by default) for CAPTURE_SECONDS (30): out of the pen, along the road,
    /// scooping, dying, and the next one in the pen. CAPTURE_4K="1" for 3840x2160 and
    /// CAPTURE_DAMAGE_NUMBERS="1" for red numbers where goblins near it lose health. The player's
    /// profile is copied first and put back afterwards, byte for byte. Writes
    /// Logs/Captures/MammothLevelTwo.
    /// </summary>
    public sealed class MammothLevelTwoCaptureTests
    {
        private const string Output = "Logs/Captures/MammothLevelTwo";
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
            Time.captureFramerate = 0;
            BeastCharge.TestHealthMultiplier = 1f;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            if (profileBackup != null)
            {
                File.WriteAllBytes(profilePath, profileBackup);
            }
            else if (File.Exists(profilePath))
            {
                File.Delete(profilePath);
            }
        }

        [UnityTest, Timeout(900000), Explicit("Records the mammoth playing Level 2 to Logs/Captures/MammothLevelTwo.")]
        public IEnumerator RecordMammothOnLevelTwo()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            if (!MammothVisual.Installed)
            {
                Assert.Ignore("The mammoth's model is not installed. Build it with Tools/Animation/build_mammoth.sh.");
            }

            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var weapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            var enemies = Object.FindFirstObjectByType<EnemyManager>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            var towers = Object.FindFirstObjectByType<TowerManager>();
            Assert.That(session != null && router != null && enemies != null && towers != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));

            TowerDefinition mammoth = null;
            foreach (var definition in SampleContent.Create().Towers)
            {
                if (definition.id == "mammoth")
                {
                    mammoth = definition;
                }
            }

            Assert.That(mammoth, Is.Not.Null, "No mammoth in the game.");
            // CAPTURE_HEALTH_MULTIPLIER="4": the mammoth lives that many times longer (testing only, user 2026-11-11).
            BeastCharge.TestHealthMultiplier = CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_HEALTH_MULTIPLIER", 1f);

            // Beside the road halfway along, so it has road to charge up: the first free spot out from it.
            var route = enemies.Route;
            Assert.That(route != null && route.HasUsableRoute, Is.True, "Level 2 has no road.");
            var along = route.TotalLength * 0.5f;
            var onRoad = route.Sample(along);
            var tangent = route.Sample(along + 1f) - onRoad;
            tangent.y = 0f;
            var across = Vector3.Cross(Vector3.up, tangent.normalized);
            var placed = false;
            var spot = Vector3.zero;
            for (var reach = 2f; reach < 20f && !placed; reach += 0.5f)
            {
                foreach (var side in new[] { 1f, -1f })
                {
                    spot = onRoad + across * side * reach;
                    if (towers.TryPlace(mammoth, spot))
                    {
                        placed = true;
                        break;
                    }
                }
            }

            Assert.That(placed, Is.True, "No free spot for the mammoth beside the Level 2 road.");
            var tower = towers.Towers[towers.Towers.Count - 1];
            var beast = tower.GetComponent<BeastCharge>();
            Assert.That(beast, Is.Not.Null, "The mammoth's tower has no beast.");
            session.StartLevel();
            if (weapon != null)
            {
                weapon.DevAutoActiveEnabled = false;
            }

            var deadline = Time.realtimeSinceStartup + 120f;
            while (!beast.IsOut && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(beast.IsOut, Is.True, "The mammoth never ran out.");

            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Mammoth Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            var is4K = System.Environment.GetEnvironmentVariable("CAPTURE_4K") == "1";
            var width = is4K ? 3840 : StyleLowPolyTests.Width;
            var height = is4K ? 2160 : StyleLowPolyTests.Height;
            var target = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var cameraHeight = CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_HEIGHT", 20f);
            var seconds = CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_SECONDS", 30f);
            // CAPTURE_FIXED="1": the camera stays put, high over the pen and the road it charges up,
            // as a player would watch (user, 2026-11-11), instead of following the mammoth.
            var fixedView = System.Environment.GetEnvironmentVariable("CAPTURE_FIXED") == "1";
            var folder = $"{Output}/{(fixedView ? "fixed" : "follow")}-{cameraHeight:F0}m";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            var showDamage = System.Environment.GetEnvironmentVariable("CAPTURE_DAMAGE_NUMBERS") == "1";
            var goblinView = showDamage
                ? new CavemenLevelTwoCaptureTests.GoblinDamageView(horde, position =>
                {
                    var offset = position - beast.BeastPosition;
                    offset.y = 0f;
                    return beast.IsOut && offset.sqrMagnitude < 5f * 5f;
                })
                : null;
            var middle = beast.BeastPosition;
            middle.y = 0f;
            if (fixedView)
            {
                // Between the pen and the road 10 m up from it, where the charges end.
                var upRoad = route.Sample(Mathf.Max(0f, along - 10f));
                middle = (spot + upRoad) * 0.5f;
                middle.y = 0f;
            }
            var frames = Mathf.RoundToInt(seconds * 30f);
            // Real speed: the game steps exactly 1/120 s a frame however long a frame takes to draw, and
            // every fourth frame is kept (a slow 4K frame used to let the game run ahead, 2026-11-11).
            Time.timeScale = 1f;
            Time.captureFramerate = 120;
            var next = Time.time;
            for (var frame = 0; frame < frames; frame++)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += 1f / 30f;
                var at = beast.BeastPosition;
                at.y = 0f;
                if (!fixedView)
                {
                    middle = Vector3.Lerp(middle, at, 0.08f);
                }

                camera.transform.position = middle + new Vector3(0f, cameraHeight, -cameraHeight * 0.75f);
                camera.transform.LookAt(middle);
                camera.transform.position += GroundTremor.Offset(camera.transform);
                if (frame % 30 == 0)
                {
                    var state = beast.IsFalling ? "falling" : beast.IsFighting ? "fighting" : beast.IsCharging ? "charging" : beast.IsWaiting ? "waiting on the road" : beast.IsOut ? "running out" : "in the pen";
                    Debug.Log($"MAMMOTH frame {frame}: {state} at {at.x:F1},{at.z:F1}, health {beast.CurrentHealth:F0}/{beast.MaximumHealth:F0}, " +
                              $"scoops {beast.ScoopCount}, charges {beast.ChargeCount}, falls {beast.FallCount}, damage dealt {tower.DamageDealt:F0}");
                }

                if (beast.IsFighting && frame % 15 == 0 && horde != null)
                {
                    // Goblins drawn inside the mammoth's body while it fights: within its trunk-less
                    // outline (2 m ahead and behind its middle, 0.9 m to each side), and within its
                    // blocking capsule (1.4 m and 0.85 m).
                    var simulation = (GpuHordeSimulation)typeof(HordeEnemyManager).GetField("gpuSimulation",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(horde);
                    var count = horde.TotalSpawned;
                    var states = new GpuHordeSimulation.AgentState[count];
                    simulation.ReadStatesSynchronous(states, count);
                    var forward = beast.CombatAxis;
                    forward.y = 0f;
                    forward.Normalize();
                    var side = new Vector3(forward.z, 0f, -forward.x);
                    int inBody = 0, inFront = 0, inBack = 0;
                    foreach (var state in states)
                    {
                        if (state.Status != 1u)
                        {
                            continue;
                        }

                        var offset = new Vector3(state.Position.x, 0f, state.Position.y) - at;
                        var lengthwise = Vector3.Dot(offset, forward);
                        var sideways = Mathf.Abs(Vector3.Dot(offset, side));
                        if (Mathf.Abs(lengthwise) < 2f && sideways < 0.9f)
                        {
                            inBody++;
                            if (lengthwise > 1.4f) inFront++;
                            if (lengthwise < -1.4f) inBack++;
                        }
                    }

                    Debug.Log($"MAMMOTH OVERLAP frame {frame}: {inBody} goblins inside its outline ({inFront} by the head, {inBack} by the rump)");
                }

                if (beast.IsFalling && frame % 10 == 0)
                {
                    // Where the horde is kept out, against where the body is drawn.
                    var visual = Object.FindFirstObjectByType<MammothVisual>();
                    var body = new Bounds();
                    var any = false;
                    foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        if (!any)
                        {
                            body = skin.bounds;
                            any = true;
                        }
                        else
                        {
                            body.Encapsulate(skin.bounds);
                        }
                    }

                    var held = visual.TryGetFootprint(out var from, out var to, out var radius, out var landing);
                    Debug.Log($"MAMMOTH BODY frame {frame}: drawn {body.center.x:F1},{body.center.z:F1} size {body.size.x:F1}x{body.size.z:F1} h {body.size.y:F1}; " +
                              $"held {held} from {from.x:F1},{from.z:F1} to {to.x:F1},{to.z:F1} radius {radius:F1} landing {landing}");
                }

                goblinView?.Update(camera);
                CavemenLevelTwoCaptureTests.DrawHorde(horde);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
            }

            Time.timeScale = 1f;
            Time.captureFramerate = 0;
            goblinView?.Clear();
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
            Debug.Log($"MAMMOTH on Level 2: pen at {spot}, damage dealt {tower.DamageDealt:F0}, scoops {beast.ScoopCount}, charges {beast.ChargeCount}, falls {beast.FallCount}.");
            Assert.That(tower.DamageDealt, Is.GreaterThan(0f), "The mammoth never hurt a goblin.");
        }
    }
}
