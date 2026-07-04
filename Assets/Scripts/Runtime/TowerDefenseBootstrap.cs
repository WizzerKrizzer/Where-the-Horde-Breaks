using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Input;
using TowerDefense.Simulation;
using TowerDefense.UI;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class TowerDefenseBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            var content = SampleContent.Create();
            var camera = CreateCamera();
            CreateLight();
            CreateGround();
            var route = CreatePath();

            var input = gameObject.AddComponent<PlayerInputRouter>();
            input.Initialize(camera);

            var cameraController = gameObject.AddComponent<TopDownCameraController>();
            cameraController.Initialize(camera, input);

            var enemyManager = new GameObject("EnemyManager").AddComponent<EnemyManager>();
            var towerManager = new GameObject("TowerManager").AddComponent<TowerManager>();
            var activeWeapon = new GameObject("ActiveWeapon").AddComponent<ActiveWeaponController>();
            activeWeapon.Initialize(enemyManager, input);
            var popups = new GameObject("WorldPopups").AddComponent<WorldPopupManager>();
            popups.Initialize(camera);

            var session = gameObject.AddComponent<GameSession>();
            session.Initialize(
                content.Level,
                content.SkillTree,
                route,
                content.Towers,
                enemyManager,
                towerManager,
                activeWeapon,
                popups,
                input);

            var placementFeedback = new GameObject("TowerPlacementFeedback").AddComponent<TowerPlacementFeedback>();
            placementFeedback.Initialize(session, input, towerManager);

            RuntimeHud.Create(session, input, towerManager, enemyManager, activeWeapon);
        }

        private static Camera CreateCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 24f, -20f);
            camera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.11f);
            camera.fieldOfView = 45f;
            return camera;
        }

        private static void CreateLight()
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            go.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
        }

        private static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "BuildableGround";
            ground.transform.position = new Vector3(0f, -0.08f, 0f);
            ground.transform.localScale = new Vector3(58f, 0.1f, 34f);
            ground.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.15f, 0.23f, 0.18f));
        }

        private static PathRoute CreatePath()
        {
            var routeObject = new GameObject("PathRoute");
            var route = routeObject.AddComponent<PathRoute>();
            var points = new[]
            {
                new Vector3(-26f, 0f, -11f),
                new Vector3(-16f, 0f, -8f),
                new Vector3(-8f, 0f, 4f),
                new Vector3(3f, 0f, 7f),
                new Vector3(13f, 0f, -4f),
                new Vector3(26f, 0f, -2f)
            };

            route.SetWaypoints(points);
            for (var i = 1; i < points.Length; i++)
            {
                CreatePathSegment(points[i - 1], points[i]);
            }

            return route;
        }

        private static void CreatePathSegment(Vector3 from, Vector3 to)
        {
            var midpoint = (from + to) * 0.5f + Vector3.up * 0.01f;
            var direction = to - from;
            var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = "PathVisual";
            segment.transform.position = midpoint;
            segment.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            segment.transform.localScale = new Vector3(2.2f, 0.05f, direction.magnitude);
            segment.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.36f, 0.29f, 0.2f));
        }
    }

    internal sealed class SampleContent
    {
        public LevelDefinition Level { get; private set; }
        public SkillTreeDefinition SkillTree { get; private set; }
        public IReadOnlyList<TowerDefinition> Towers { get; private set; }

        public static SampleContent Create()
        {
            var runner = CreateEnemy("runner", "Goblin Runner", EnemyRole.Runner, 14f, 5.2f, 1, 1, new Color(0.2f, 0.9f, 0.25f), 0.38f);
            var brute = CreateEnemy("brute", "Orc Brute", EnemyRole.Heavy, 58f, 2.35f, 2, 3, new Color(0.05f, 0.45f, 0.08f), 0.62f);
            var shaman = CreateEnemy("shaman", "Witch Shaman", EnemyRole.Support, 32f, 3.35f, 1, 2, new Color(0.55f, 0.18f, 0.75f), 0.5f);

            var archer = CreateTower("archer", "Archer Tower", TowerRole.ArcherLine, 0, 1, 7f, 4.2f, 0.5f, 18f, new Color(0.9f, 0.85f, 0.4f));
            var ballista = CreateTower("ballista", "Ballista", TowerRole.ArtilleryLine, 0, 1, 11f, 16f, 1f / 0.7f, 14f, new Color(0.7f, 0.35f, 0.16f));
            var watch = CreateTower("watch", "Watch Tower", TowerRole.ControlLine, 0, 1, 6.5f, 2.2f, 0.22f, 22f, new Color(0.45f, 0.72f, 1f));

            var wave = ScriptableObject.CreateInstance<WaveDefinition>();
            wave.id = "wave_01";
            wave.totalEnemyCount = 390;
            wave.entries = new[]
            {
                new WaveEntry { enemy = runner, count = 150, startTime = 0f, spawnInterval = 0.12f },
                new WaveEntry { enemy = brute, count = 70, startTime = 10f, spawnInterval = 0.36f },
                new WaveEntry { enemy = shaman, count = 45, startTime = 18f, spawnInterval = 0.44f },
                new WaveEntry { enemy = runner, count = 80, startTime = 28f, spawnInterval = 0.075f },
                new WaveEntry { enemy = brute, count = 45, startTime = 34f, spawnInterval = 0.26f }
            };

            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.id = "level_01";
            level.displayName = "Green Pass";
            level.startingLives = 12;
            level.wave = wave;
            level.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            level.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            level.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 25);

            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            tree.id = "core_tree";
            tree.nodes = new[]
            {
                new SkillNodeDefinition
                {
                    id = "volley_core",
                    displayName = "Volley of Arrows",
                    description = "Your starting active weapon. This is the center of the first tree.",
                    radialPosition = Vector2.zero,
                    maxRanks = 1,
                    startsUnlocked = true,
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "volley_damage_01",
                    displayName = "Sharper Arrows",
                    description = "Each rank increases active weapon damage by 2%.",
                    radialPosition = new Vector2(150f, 54f),
                    maxRanks = 10,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 18) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponDamagePercent, value = 2f } }
                },
                new SkillNodeDefinition
                {
                    id = "volley_pierce_01",
                    displayName = "Arrow Rain",
                    description = "Each rank lets Volley of Arrows hit 2 additional enemies.",
                    radialPosition = new Vector2(156f, -84f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 30) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponPierceFlat, value = 2f } }
                },
                new SkillNodeDefinition
                {
                    id = "volley_cooldown_01",
                    displayName = "Quick Draw",
                    description = "Each rank reduces active weapon cooldown by 2%.",
                    radialPosition = new Vector2(285f, 116f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "volley_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 32) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponCooldownPercent, value = 2f } }
                },
                new SkillNodeDefinition
                {
                    id = "volley_radius_01",
                    displayName = "Wider Volley",
                    description = "Each rank increases active weapon radius by 0.15.",
                    radialPosition = new Vector2(292f, -28f),
                    maxRanks = 5,
                    prerequisiteNodeIds = new[] { "volley_pierce_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 38) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponRadiusFlat, value = 0.15f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_unlock",
                    displayName = "Archer Tower",
                    description = "Unlock the Archer Tower for future runs.",
                    radialPosition = new Vector2(-150f, 52f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 25) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "archer", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "archer_limit_01",
                    displayName = "Archer Barracks",
                    description = "Each rank increases the Archer Tower limit by 1.",
                    radialPosition = new Vector2(-298f, 36f),
                    maxRanks = 4,
                    prerequisiteNodeIds = new[] { "archer_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 35) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "archer", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_damage_01",
                    displayName = "Fletching",
                    description = "Each rank increases Archer Tower damage by 3%.",
                    radialPosition = new Vector2(-280f, 130f),
                    maxRanks = 10,
                    prerequisiteNodeIds = new[] { "archer_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 18) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamagePercent, targetId = "archer", value = 3f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_unlock",
                    displayName = "Ballista",
                    description = "Unlock a slow tower with heavy single-target damage.",
                    radialPosition = new Vector2(-150f, -98f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 95), new CurrencyAmount(CurrencyType.VictorySigil, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "ballista", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "ballista_limit_01",
                    displayName = "Siege Crew",
                    description = "Each rank increases the Ballista limit by 1.",
                    radialPosition = new Vector2(-306f, -112f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "ballista_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 70) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "ballista", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_damage_01",
                    displayName = "Heavy Bolts",
                    description = "Each rank increases Ballista damage by 4%.",
                    radialPosition = new Vector2(-232f, -208f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "ballista_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 55) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamagePercent, targetId = "ballista", value = 4f } }
                }
            };

            return new SampleContent
            {
                Level = level,
                SkillTree = tree,
                Towers = new[] { archer, ballista, watch }
            };
        }

        private static EnemyDefinition CreateEnemy(string id, string name, EnemyRole role, float hp, float speed, int lifeDamage, int killReward, Color color, float scale)
        {
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            enemy.id = id;
            enemy.displayName = name;
            enemy.role = role;
            enemy.maxHealth = hp;
            enemy.speed = speed;
            enemy.lifeDamage = lifeDamage;
            enemy.killReward = killReward;
            enemy.color = color;
            enemy.visualScale = scale;
            return enemy;
        }

        private static TowerDefinition CreateTower(string id, string name, TowerRole role, int era, int limit, float range, float damage, float fireInterval, float projectileSpeed, Color color)
        {
            var tower = ScriptableObject.CreateInstance<TowerDefinition>();
            tower.id = id;
            tower.displayName = name;
            tower.role = role;
            tower.eraIndex = era;
            tower.perTypeLimit = limit;
            tower.range = range;
            tower.damage = damage;
            tower.fireInterval = fireInterval;
            tower.projectileSpeed = projectileSpeed;
            tower.color = color;
            return tower;
        }
    }
}
