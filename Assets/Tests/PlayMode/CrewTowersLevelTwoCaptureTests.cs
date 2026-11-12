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
    /// The Slingers on their tipi and the Spear Throwers on their rock, with their models, playing
    /// Level 2 of the real game (the Main scene), its road and wave as they are (user, 2026-11-11).
    /// The tower is added beside the road a third of the way along; nothing else in the level changes.
    /// A camera that never moves (CAPTURE_HEIGHT, 11 m by default) looks at the tower and the road for
    /// CAPTURE_SECONDS (20). CAPTURE_DAMAGE_NUMBERS="1" puts red numbers where goblins lose health. The
    /// player's profile is copied first and put back afterwards, byte for byte. Writes
    /// Logs/Captures/CrewTowers.
    /// </summary>
    public sealed class CrewTowersLevelTwoCaptureTests
    {
        private const string Output = "Logs/Captures/CrewTowers";
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

        [UnityTest, Timeout(900000), Explicit("Records the Slingers playing Level 2 to Logs/Captures/CrewTowers/slingers.")]
        public IEnumerator RecordSlingersOnLevelTwo()
        {
            return Record("slingers");
        }

        [UnityTest, Timeout(900000), Explicit("Records the Spear Throwers playing Level 2 to Logs/Captures/CrewTowers/spear_thrower.")]
        public IEnumerator RecordSpearThrowersOnLevelTwo()
        {
            return Record("spear_thrower");
        }

        // Every tower with its border drawn on the ground (user, 2026-11-11): the four built from models
        // and a greybox one, packed as close beside the Level 2 road as their borders let them.
        [UnityTest, Timeout(900000), Explicit("Draws the towers' borders on Level 2 to Logs/Captures/CrewTowers/borders.png.")]
        public IEnumerator RecordTowerBorders()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var enemies = Object.FindFirstObjectByType<EnemyManager>();
            var towers = Object.FindFirstObjectByType<TowerManager>();
            Assert.That(session != null && router != null && enemies != null && towers != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));
            var definitions = new System.Collections.Generic.Dictionary<string, TowerDefinition>();
            foreach (var candidate in SampleContent.Create().Towers)
            {
                definitions[candidate.id] = candidate;
            }

            var route = enemies.Route;
            Assert.That(route != null && route.HasUsableRoute, Is.True, "Level 2 has no road.");
            // Along the road a third of the way, alternating sides, each as close as its border allows.
            var placedCount = 0;
            var along = route.TotalLength / 3f - 8f;
            var side = 1f;
            var lines = new System.Collections.Generic.List<GameObject>();
            var material = new Material(Shader.Find("Sprites/Default"));
            foreach (var id in new[] { "slingers", "spear_thrower", "cave", "mammoth", "archer" })
            {
                if (!definitions.TryGetValue(id, out var definition))
                {
                    continue;
                }

                definitions[id].perTypeLimit = 99;
                var placed = false;
                for (var step = 0f; step < 12f && !placed; step += 0.5f)
                {
                    var onRoad = route.Sample(along + step);
                    var tangent = route.Sample(along + step + 1f) - onRoad;
                    tangent.y = 0f;
                    var across = Vector3.Cross(Vector3.up, tangent.normalized);
                    for (var reach = 1f; reach < 12f && !placed; reach += 0.25f)
                    {
                        var spot = onRoad + across * side * reach;
                        if (towers.TryPlace(definition, spot))
                        {
                            placed = true;
                            along += step + 3f;
                            placedCount++;
                        }
                    }
                }

                side = -side;
                if (!placed)
                {
                    continue;
                }

                var tower = towers.Towers[towers.Towers.Count - 1];
                var border = towers.BorderOf(tower);
                var line = new GameObject($"Border {id}").AddComponent<LineRenderer>();
                line.loop = true;
                line.widthMultiplier = 0.08f;
                line.sharedMaterial = material;
                line.startColor = line.endColor = Color.yellow;
                line.positionCount = border.Length;
                for (var i = 0; i < border.Length; i++)
                {
                    line.SetPosition(i, new Vector3(border[i].x, 0.06f, border[i].y));
                }

                lines.Add(line.gameObject);
            }

            Assert.That(placedCount, Is.GreaterThan(0), "No tower could be placed.");
            yield return null;
            var fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Border Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = fieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var middle = Vector3.zero;
            foreach (var tower in towers.Towers)
            {
                middle += tower.transform.position;
            }

            middle /= Mathf.Max(1, towers.Towers.Count);
            middle.y = 0f;
            Directory.CreateDirectory(Output);
            CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, middle, CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_HEIGHT", 30f));
            yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/borders.png");

            // The same towers with a pen being placed over two of them: they turn red, as does the
            // preview (TowerPlacementFeedback, user 2026-11-12).
            var overlapped = new System.Collections.Generic.List<TowerActor>();
            var probe = middle;
            foreach (var tower in towers.Towers)
            {
                towers.GetOverlappingTowers(definitions["mammoth"], tower.Position + new Vector3(2.5f, 0f, 0f), overlapped);
                if (overlapped.Count > 0)
                {
                    probe = tower.Position + new Vector3(2.5f, 0f, 0f);
                    break;
                }
            }

            foreach (var tower in overlapped)
            {
                tower.SetOverlapTint(true);
            }

            var preview = TowerVisualFactory.CreateTowerVisual(definitions["mammoth"], "Preview");
            preview.transform.position = probe + Vector3.up * 0.35f;
            preview.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(1f, 0.12f, 0.1f, 0.78f));
            foreach (var line in lines)
            {
                line.SetActive(false);
            }

            yield return null;
            yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/overlap.png");
            Debug.Log($"OVERLAP: a pen at {probe} overlaps {overlapped.Count} towers.");
            Object.Destroy(preview);
            Debug.Log($"BORDERS: placed {placedCount} towers around {middle}.");
            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
            Object.Destroy(material);
            foreach (var line in lines)
            {
                Object.Destroy(line);
            }
        }

        // The tower being placed shows as its own see-through model (user, 2026-11-12), through the
        // game's own placement feedback: a tipi beside a rock (red, both of them), then on free ground.
        [UnityTest, Timeout(900000), Explicit("Records the placing ghost on Level 2 to Logs/Captures/CrewTowers/ghost-*.png.")]
        public IEnumerator RecordPlacementGhost()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var enemies = Object.FindFirstObjectByType<EnemyManager>();
            var towers = Object.FindFirstObjectByType<TowerManager>();
            Assert.That(session != null && router != null && enemies != null && towers != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            var definitions = new System.Collections.Generic.Dictionary<string, TowerDefinition>();
            foreach (var candidate in SampleContent.Create().Towers)
            {
                definitions[candidate.id] = candidate;
            }

            towers.RemoveAll();
            towers.SetAvailableTowers(new[] { definitions["slingers"], definitions["spear_thrower"] });
            var route = enemies.Route;
            var onRoad = route.Sample(route.TotalLength / 3f);
            var tangent = route.Sample(route.TotalLength / 3f + 1f) - onRoad;
            tangent.y = 0f;
            var across = Vector3.Cross(Vector3.up, tangent.normalized);
            var rockSpot = Vector3.zero;
            for (var reach = 1f; reach < 12f; reach += 0.25f)
            {
                rockSpot = onRoad + across * reach;
                if (towers.TryPlace(definitions["spear_thrower"], rockSpot))
                {
                    break;
                }
            }

            Assert.That(towers.Towers.Count, Is.EqualTo(1), "The rock could not be placed.");
            var fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Ghost Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = fieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var along = tangent.normalized;
            CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, rockSpot + along * 3f - across * 1.5f, 16f);
            Directory.CreateDirectory(Output);
            var current = typeof(PlayerInputRouter).GetProperty("Current");
            foreach (var (name, offset) in new[] { ("overlap", along * 4f), ("free", along * 9f) })
            {
                var state = default(GameInputState);
                state.SelectedTowerIndex = 0;
                state.PointerWorld = rockSpot + offset;
                current.SetValue(router, state);
                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                }

                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{Output}/ghost-{name}.png");
                Debug.Log($"GHOST {name}: at {state.PointerWorld}, can place {towers.CanPlace(definitions["slingers"], state.PointerWorld)}, rock red {towers.Towers[0].OverlapTinted}.");
            }

            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
        }

        private IEnumerator Record(string towerId)
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
            var towers = Object.FindFirstObjectByType<TowerManager>();
            Assert.That(session != null && router != null && enemies != null && towers != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_02");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));

            TowerDefinition definition = null;
            foreach (var candidate in SampleContent.Create().Towers)
            {
                if (candidate.id == towerId)
                {
                    definition = candidate;
                }
            }

            Assert.That(definition, Is.Not.Null, $"No {towerId} in the game.");
            var route = enemies.Route;
            Assert.That(route != null && route.HasUsableRoute, Is.True, "Level 2 has no road.");
            var along = route.TotalLength / 3f;
            var onRoad = route.Sample(along);
            var tangent = route.Sample(along + 1f) - onRoad;
            tangent.y = 0f;
            var across = Vector3.Cross(Vector3.up, tangent.normalized);
            var placed = false;
            var spot = Vector3.zero;
            // CAPTURE_SIDE="-1": the tower on the other side of the road (user, 2026-11-11).
            var wantedSide = CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_SIDE", 1f) < 0f ? -1f : 1f;
            for (var reach = 3f; reach < 20f && !placed; reach += 0.5f)
            {
                foreach (var side in new[] { wantedSide })
                {
                    spot = onRoad + across * side * reach;
                    if (towers.TryPlace(definition, spot))
                    {
                        placed = true;
                        break;
                    }
                }
            }

            Assert.That(placed, Is.True, $"No free spot for the {towerId} beside the Level 2 road.");
            var tower = towers.Towers[towers.Towers.Count - 1];
            if (tower.CrewVisual == null)
            {
                Assert.Ignore("The crew's models are not installed. Install them with Tools/Animation/install_crew_models.sh.");
            }

            session.StartLevel();
            if (weapon != null)
            {
                weapon.DevAutoActiveEnabled = false;
            }

            // From the first goblin in range; CAPTURE_FROM_START="1": from the start, the crew waiting.
            var deadline = System.Environment.GetEnvironmentVariable("CAPTURE_FROM_START") == "1" ? 0f : Time.realtimeSinceStartup + 120f;
            while (!enemies.TryGetHordeTargetSnapshot(tower.transform.position, definition.range, true, TowerTargetingMode.Closest, out _, out _, out _) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var fieldOfView = CavemenLevelTwoCaptureTests.GameFieldOfView();
            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Crew Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = fieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            var target = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
            var texture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var cameraHeight = CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_HEIGHT", 11f);
            var seconds = CavemenLevelTwoCaptureTests.EnvFloat("CAPTURE_SECONDS", 20f);
            var folder = $"{Output}/{towerId}-{cameraHeight:F0}m{(wantedSide < 0f ? "-other-side" : "")}";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            var showDamage = System.Environment.GetEnvironmentVariable("CAPTURE_DAMAGE_NUMBERS") == "1";
            var goblinView = showDamage
                ? new CavemenLevelTwoCaptureTests.GoblinDamageView(horde, position =>
                {
                    var offset = position - tower.transform.position;
                    offset.y = 0f;
                    return offset.sqrMagnitude < definition.range * definition.range * 1.5f;
                })
                : null;
            // Between the tower (built back from its spot) and the road beside it.
            var nearest = route.GetNearestRoadPoint(spot, out _);
            var middle = Vector3.Lerp(spot, nearest, 0.25f);
            middle.y = 0f;
            CavemenLevelTwoCaptureTests.PlaceGameCamera(camera, middle, cameraHeight);
            var frames = Mathf.RoundToInt(seconds * 30f);
            var damageAtStart = tower.DamageDealt;
            TowerActor.TestThrows = 0;
            TowerActor.TestThrowsOffFacing = 0;
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
                if (frame % 30 == 0)
                {
                    Debug.Log($"CREW {towerId} frame {frame}: damage dealt {tower.DamageDealt - damageAtStart:F0}");
                }

                goblinView?.Update(camera);
                CavemenLevelTwoCaptureTests.DrawHorde(horde);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
            }

            Time.captureFramerate = 0;
            goblinView?.Clear();
            Debug.Log($"CREW {towerId} on Level 2: tower at {spot}, damage dealt {tower.DamageDealt - damageAtStart:F0} in {seconds:F0} s; " +
                      $"{TowerActor.TestThrowsOffFacing} of {TowerActor.TestThrows} throws more than 45 degrees off the thrower's facing.");
            Assert.That(tower.DamageDealt, Is.GreaterThan(damageAtStart), $"The {towerId} never hit a goblin.");
            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
        }
    }
}
