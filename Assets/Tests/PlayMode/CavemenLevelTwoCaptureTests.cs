using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    /// The cavemen's model playing Level 2 of the real game (the Main scene), its road and wave as
    /// they are (user, 2026-11-11). A cave is added beside the road, a third of the way along, with
    /// the cave's three specials (shields, torches, the chief) switched on for the recording; nothing
    /// in the level changes. The camera follows the cavemen from 4 m, then 11 m and 40 m. The player's
    /// profile is copied first and put back afterwards, byte for byte. Writes
    /// Logs/Captures/CavemenLevelTwo.
    /// </summary>
    public sealed class CavemenLevelTwoCaptureTests
    {
        private const string Output = "Logs/Captures/CavemenLevelTwo";
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
            Time.captureFramerate = 0;
            AlliedUnitActor.TestHealthMultiplier = 1f;
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

        [UnityTest, Explicit("Records the cavemen playing Level 2 to Logs/Captures/CavemenLevelTwo.")]
        public IEnumerator RecordCavemenOnLevelTwo()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            if (Resources.Load<GameObject>("Mixamo/caveman") == null)
            {
                Assert.Ignore("The cavemen's model is not installed. Build it with Tools/Animation/build_caveman.sh.");
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

            // The game's own cave definition, with its specials on for the recording.
            TowerDefinition cave = null;
            foreach (var definition in SampleContent.Create().Towers)
            {
                if (definition.id == "cave")
                {
                    cave = definition;
                }
            }

            Assert.That(cave, Is.Not.Null, "No cave in the game.");
            cave.barracksCapacity = 4;
            cave.alliedUnitDamageReduction = 0.25f;
            cave.chiefAuraBonus = 0.25f;
            cave.chiefHealthBonus = 1f;
            cave.torchBurnDamagePerSecond = 1f;
            cave.torchBurnSeconds = 5f;
            // CAPTURE_HEALTH_MULTIPLIER="10": every caveman (of any cave, the bot's too) lives that many
            // times longer, to watch them fight for longer (testing only, user 2026-11-11).
            AlliedUnitActor.TestHealthMultiplier = EnvFloat("CAPTURE_HEALTH_MULTIPLIER", 1f);

            // Beside the road a third of the way along: the first free spot out from the road.
            var route = enemies.Route;
            Assert.That(route != null && route.HasUsableRoute, Is.True, "Level 2 has no road.");
            var along = route.TotalLength * 0.33f;
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
                    if (towers.TryPlace(cave, spot))
                    {
                        placed = true;
                        break;
                    }
                }
            }

            Assert.That(placed, Is.True, "No free spot for the cave beside the Level 2 road.");
            var tower = towers.Towers[towers.Towers.Count - 1];
            session.StartLevel();
            if (weapon != null)
            {
                weapon.DevAutoActiveEnabled = false;
            }

            var deadline = Time.realtimeSinceStartup + 90f;
            while (tower.DamageDealt <= 0f && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(tower.DamageDealt, Is.GreaterThan(0f), "The cavemen never met the horde.");

            foreach (var other in Camera.allCameras)
            {
                other.enabled = false;
            }

            var cameraObject = new GameObject("Cavemen Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            // CAPTURE_4K="1": 3840x2160 instead of 1920x1080.
            var is4K = System.Environment.GetEnvironmentVariable("CAPTURE_4K") == "1";
            var width = is4K ? 3840 : StyleLowPolyTests.Width;
            var height4K = is4K ? 2160 : StyleLowPolyTests.Height;
            var target = new RenderTexture(width, height4K, 24) { antiAliasing = 4 };
            var texture = new Texture2D(width, height4K, TextureFormat.RGB24, false);
            // CAPTURE_HEALTH_LABELS="1": a health line with small numbers over each caveman (testing only).
            var showHealth = System.Environment.GetEnvironmentVariable("CAPTURE_HEALTH_LABELS") == "1";
            var labels = new Dictionary<AlliedUnitActor, HealthLabel>();
            // CAPTURE_DAMAGE_NUMBERS="1": small red numbers where a goblin by the cavemen loses health,
            // and from then on its health line over it (testing only, user 2026-11-11).
            var showDamage = System.Environment.GetEnvironmentVariable("CAPTURE_DAMAGE_NUMBERS") == "1";
            var goblinView = showDamage ? new GoblinDamageView(horde) : null;
            camera.targetTexture = target;
            // CAPTURE_FIXED_HEIGHT="20" CAPTURE_SECONDS="30": one long shot from a camera that never
            // moves, over where the fight starts (user, 2026-11-11). Otherwise the camera follows the
            // cavemen from 4 m, then 11 m and 40 m.
            var fixedHeight = float.TryParse(System.Environment.GetEnvironmentVariable("CAPTURE_FIXED_HEIGHT"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedHeight) ? parsedHeight : 0f;
            var seconds = float.TryParse(System.Environment.GetEnvironmentVariable("CAPTURE_SECONDS"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedSeconds) ? parsedSeconds : 12f;
            var folder = fixedHeight > 0f ? $"{Output}/fixed-{fixedHeight:F0}m" : $"{Output}/follow";
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            var middle = Middle(tower, spot);
            var frames = Mathf.RoundToInt(seconds * 30f);
            var framesPerHeight = frames / 3;
            if (fixedHeight > 0f)
            {
                camera.transform.position = middle + new Vector3(0f, fixedHeight, -fixedHeight * 0.75f);
                camera.transform.LookAt(middle);
            }

            // Real speed: the game steps exactly 1/120 s a frame however long a frame takes to draw, and
            // every fourth frame is kept (a slow 4K frame used to let the game run ahead, 2026-11-11).
            Time.timeScale = 1f;
            Time.captureFramerate = 120;
            AlliedUnitActor.TestStrikes = 0;
            CavemanVisual.TestSwings = 0;
            var next = Time.time;
            var fightSamples = 0;
            var sideSamples = 0;
            for (var frame = 0; frame < frames; frame++)
            {
                while (Time.time < next)
                {
                    yield return null;
                }

                next += 1f / 30f;
                if (fixedHeight <= 0f)
                {
                    middle = Vector3.Lerp(middle, Middle(tower, middle), 0.1f);
                    var height = frame < framesPerHeight ? 4f : frame < framesPerHeight * 2 ? 11f : 40f;
                    camera.transform.position = middle + new Vector3(0f, height, -height * 0.75f);
                    camera.transform.LookAt(middle);
                }

                // How far off his front each fighting caveman's goblin is (user, 2026-11-11: they went
                // for goblins beside them too often).
                foreach (var unit in Object.FindObjectsByType<AlliedUnitActor>(FindObjectsSortMode.None))
                {
                    if (unit.HasFacingTarget && unit.IsAlive)
                    {
                        var toTarget = unit.FacingTarget - unit.Position;
                        toTarget.y = 0f;
                        fightSamples++;
                        sideSamples += Vector3.Angle(toTarget, unit.WatchDirection) > 65f ? 1 : 0;
                    }
                }

                if (frame % 30 == 0)
                {
                    var alive = Object.FindObjectsByType<AlliedUnitActor>(FindObjectsSortMode.None);
                    var visuals = Object.FindObjectsByType<CavemanVisual>(FindObjectsSortMode.None);
                    var posts = "";
                    foreach (var unit in alive)
                    {
                        var own = unit.GetComponentInParent<TowerActor>() == tower ? "mine" : "yours";
                        posts += $" [{own} {unit.Slot}{(unit.IsChief ? " chief" : "")} at {unit.Position.x:F1},{unit.Position.z:F1} post {unit.Post.x:F1},{unit.Post.z:F1} hp {unit.CurrentHealth:F0}{(unit.HasFacingTarget ? " fighting" : "")}]";
                    }

                    Debug.Log($"CAVEMEN frame {frame}: {alive.Length} alive, {visuals.Length} models;{posts}");
                }

                if (showHealth)
                {
                    UpdateHealthLabels(labels, camera);
                }

                goblinView?.Update(camera);

                DrawHorde(horde);
                yield return StyleLowPolyTests.Capture(camera, target, texture, $"{folder}/frame_{frame:D4}.png");
            }

            Time.timeScale = 1f;
            Time.captureFramerate = 0;
            Object.Destroy(target);
            Object.Destroy(texture);
            Object.Destroy(cameraObject);
            foreach (var label in labels.Values)
            {
                Object.Destroy(label.Root);
            }

            goblinView?.Clear();
            Debug.Log($"CAVEMEN TARGETS: {sideSamples} of {fightSamples} fighting samples had the goblin more than 65 degrees off his front.");
            Debug.Log($"Cavemen on Level 2: cave at {spot}, damage dealt {tower.DamageDealt:F0}, spawned {enemies.TotalSpawned}.");
            Debug.Log($"CAVEMEN blows struck {AlliedUnitActor.TestStrikes}, swings shown {CavemanVisual.TestSwings} over {seconds:F0} s.");
        }

        internal static float EnvFloat(string name, float fallback)
        {
            return float.TryParse(System.Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
        }

        // A test-only health line over a caveman: dark back, green fill, "current/max" in small numbers on it.
        private sealed class HealthLabel
        {
            public GameObject Root;
            public Transform Fill;
            public TextMesh Text;
        }

        private const float LabelWidth = 1.2f;
        private const float LabelHeight = 0.2f;

        private static void UpdateHealthLabels(Dictionary<AlliedUnitActor, HealthLabel> labels, Camera camera)
        {
            foreach (var unit in Object.FindObjectsByType<AlliedUnitActor>(FindObjectsSortMode.None))
            {
                if (!labels.TryGetValue(unit, out var label))
                {
                    label = CreateHealthLabel();
                    labels[unit] = label;
                }

                var gameBar = unit.transform.Find("AlliedHealthBar");
                if (gameBar != null)
                {
                    gameBar.gameObject.SetActive(false);
                }
            }

            foreach (var pair in labels)
            {
                var unit = pair.Key;
                var label = pair.Value;
                var alive = unit != null && unit.IsAlive;
                label.Root.SetActive(alive);
                if (!alive)
                {
                    continue;
                }

                var ratio = Mathf.Clamp01(unit.CurrentHealth / Mathf.Max(1f, unit.MaximumHealth));
                label.Root.transform.position = unit.Position + Vector3.up * (unit.IsChief ? 2.25f : 1.85f);
                label.Root.transform.rotation = camera.transform.rotation;
                label.Fill.localScale = new Vector3(LabelWidth * ratio, LabelHeight * 0.8f, 1f);
                label.Fill.localPosition = new Vector3(-LabelWidth * 0.5f * (1f - ratio), 0f, -0.01f);
                label.Text.text = $"{unit.CurrentHealth:F0}/{unit.MaximumHealth:F0}";
            }
        }

        private static HealthLabel CreateHealthLabel(float width = LabelWidth, float height = LabelHeight, Color? fillColor = null)
        {
            var root = new GameObject("TestHealthLabel");
            var back = Quad(root.transform, new Color(0.06f, 0.06f, 0.06f));
            back.localScale = new Vector3(width + 0.04f, height, 1f);
            var fill = Quad(root.transform, fillColor ?? new Color(0.3f, 0.78f, 0.28f));
            var text = CreateText(root.transform, height * 0.8f, Color.white);
            text.transform.localPosition = new Vector3(0f, 0f, -0.03f);
            return new HealthLabel { Root = root, Fill = fill, Text = text };
        }

        // Text about `height` metres tall.
        private static TextMesh CreateText(Transform parent, float height, Color color)
        {
            var textObject = new GameObject("Numbers");
            textObject.transform.SetParent(parent, false);
            var text = textObject.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = 64;
            text.characterSize = height / 6.4f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            return text;
        }

        // Testing only: reads the horde back from the GPU each frame. Where a goblin within reach of
        // a caveman loses health, a small red number rises off it, and a health line ("5/15") stays
        // over it from then on until it dies. Goblins nobody has hurt show nothing.
        internal sealed class GoblinDamageView
        {
            private const float CavemanReach = 2.6f;
            private const float NumberSeconds = 0.9f;
            private const float BarWidth = 0.5f;
            private const float BarHeight = 0.1f;
            private readonly GpuHordeSimulation simulation;
            private readonly HordeEnemyManager horde;
            private GpuHordeSimulation.AgentState[] states = new GpuHordeSimulation.AgentState[0];
            private float[] lastHealth = new float[0];
            private readonly Dictionary<int, HealthLabel> bars = new();
            private readonly List<(TextMesh Text, Vector3 From, float Born)> numbers = new();

            // Where a lost point counts: by a caveman unless the test says otherwise (the mammoth's).
            private readonly System.Func<Vector3, bool> near;

            public GoblinDamageView(HordeEnemyManager horde, System.Func<Vector3, bool> near = null)
            {
                this.horde = horde;
                this.near = near;
                simulation = horde != null ? (GpuHordeSimulation)typeof(HordeEnemyManager).GetField("gpuSimulation", Private).GetValue(horde) : null;
            }

            public void Update(Camera camera)
            {
                if (simulation == null)
                {
                    return;
                }

                var count = horde.TotalSpawned;
                if (states.Length < count)
                {
                    System.Array.Resize(ref states, Mathf.NextPowerOfTwo(count));
                    var grown = new float[states.Length];
                    System.Array.Copy(lastHealth, grown, lastHealth.Length);
                    for (var i = lastHealth.Length; i < grown.Length; i++)
                    {
                        grown[i] = -1f;
                    }

                    lastHealth = grown;
                }

                simulation.ReadStatesSynchronous(states, count);
                var cavemen = Object.FindObjectsByType<AlliedUnitActor>(FindObjectsSortMode.None);
                for (var i = 0; i < count; i++)
                {
                    var state = states[i];
                    var alive = state.Status == 1u && state.Health > 0f;
                    var before = lastHealth[i];
                    lastHealth[i] = alive ? state.Health : 0f;
                    var position = new Vector3(state.Position.x, 0f, state.Position.y);
                    if (!alive)
                    {
                        if (bars.TryGetValue(i, out var dead))
                        {
                            Object.Destroy(dead.Root);
                            bars.Remove(i);
                        }

                        // The killing blow still shows its number.
                        if (before >= 0.9f && state.Status != 1u && Counts(position, cavemen))
                        {
                            SpawnNumber(before, position, state.Scale);
                        }

                        continue;
                    }

                    var lost = before - state.Health;
                    // Torch burns tick away half a point at a time; only blows count.
                    if (before > 0f && lost >= 0.9f && Counts(position, cavemen))
                    {
                        SpawnNumber(lost, position, state.Scale);
                        if (!bars.ContainsKey(i))
                        {
                            bars[i] = CreateHealthLabel(BarWidth, BarHeight, new Color(0.85f, 0.25f, 0.2f));
                        }
                    }

                    if (bars.TryGetValue(i, out var bar))
                    {
                        var max = Mathf.Max(1f, state.MaxHealth);
                        var ratio = Mathf.Clamp01(state.Health / max);
                        bar.Root.transform.position = position + Vector3.up * (0.75f + state.Scale * 0.5f);
                        bar.Root.transform.rotation = camera.transform.rotation;
                        bar.Fill.localScale = new Vector3(BarWidth * ratio, BarHeight * 0.8f, 1f);
                        bar.Fill.localPosition = new Vector3(-BarWidth * 0.5f * (1f - ratio), 0f, -0.01f);
                        bar.Text.text = $"{Mathf.CeilToInt(state.Health)}/{Mathf.CeilToInt(max)}";
                    }
                }

                for (var n = numbers.Count - 1; n >= 0; n--)
                {
                    var (text, from, born) = numbers[n];
                    var age = (Time.time - born) / NumberSeconds;
                    if (age >= 1f)
                    {
                        Object.Destroy(text.gameObject);
                        numbers.RemoveAt(n);
                        continue;
                    }

                    text.transform.position = from + Vector3.up * (0.5f * age);
                    text.transform.rotation = camera.transform.rotation;
                    var color = text.color;
                    color.a = 1f - age * age;
                    text.color = color;
                }
            }

            private void SpawnNumber(float amount, Vector3 position, float scale)
            {
                var holder = new GameObject("TestDamageNumber");
                var text = CreateText(holder.transform, 0.2f, new Color(1f, 0.12f, 0.08f));
                text.text = "-" + Mathf.RoundToInt(amount).ToString(System.Globalization.CultureInfo.InvariantCulture);
                text.transform.SetParent(null, true);
                Object.Destroy(holder);
                var from = position + Vector3.up * (1.0f + scale * 0.5f) + new Vector3(Random.Range(-0.15f, 0.15f), 0f, Random.Range(-0.1f, 0.1f));
                text.transform.position = from;
                numbers.Add((text, from, Time.time));
            }

            private bool Counts(Vector3 position, AlliedUnitActor[] cavemen) => near != null ? near(position) : NearCaveman(position, cavemen);

            private static bool NearCaveman(Vector3 position, AlliedUnitActor[] cavemen)
            {
                foreach (var unit in cavemen)
                {
                    if (unit == null || !unit.IsAlive)
                    {
                        continue;
                    }

                    var offset = unit.Position - position;
                    offset.y = 0f;
                    if (offset.sqrMagnitude <= CavemanReach * CavemanReach)
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Clear()
            {
                foreach (var bar in bars.Values)
                {
                    Object.Destroy(bar.Root);
                }

                foreach (var number in numbers)
                {
                    Object.Destroy(number.Text.gameObject);
                }

                bars.Clear();
                numbers.Clear();
            }
        }

        private static Transform Quad(Transform parent, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(parent, false);
            quad.GetComponent<Renderer>().material = new Material(Shader.Find("Unlit/Color")) { color = color };
            return quad.transform;
        }

        // The cavemen's centre on the ground; `fallback` while none are out.
        private static Vector3 Middle(TowerActor tower, Vector3 fallback)
        {
            var units = tower.GetComponentsInChildren<AlliedUnitActor>();
            if (units.Length == 0)
            {
                return fallback;
            }

            var middle = Vector3.zero;
            foreach (var unit in units)
            {
                middle += unit.Position / units.Length;
            }

            middle.y = 0f;
            return middle;
        }

        internal static void DrawHorde(HordeEnemyManager horde)
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
