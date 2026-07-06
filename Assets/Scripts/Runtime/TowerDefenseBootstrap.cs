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
            var corpseManager = new GameObject("EnemyCorpses").AddComponent<EnemyCorpseManager>();
            enemyManager.SetCorpseManager(corpseManager);
            var towerManager = new GameObject("TowerManager").AddComponent<TowerManager>();
            var activeWeapon = new GameObject("ActiveWeapon").AddComponent<ActiveWeaponController>();
            activeWeapon.Initialize(enemyManager, input, towerManager);
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
            ground.transform.localScale = new Vector3(72f, 0.1f, 44f);
            ground.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.15f, 0.23f, 0.18f));
        }

        private static PathRoute CreatePath()
        {
            var routeObject = new GameObject("PathRoute");
            var route = routeObject.AddComponent<PathRoute>();
            var points = new[]
            {
                new Vector3(-27.2f, 0f, -11.2f),
                new Vector3(-20.4f, 0f, -8.8f),
                new Vector3(-15f, 0f, -0.2f),
                new Vector3(-6.8f, 0f, 3.6f),
                new Vector3(1.4f, 0f, 0.8f),
                new Vector3(6.6f, 0f, -6.8f),
                new Vector3(15.4f, 0f, -4.4f),
                new Vector3(24f, 0f, 4.6f)
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
            const float roadWidth = 5.4f;
            const float bankOffset = roadWidth * 0.5f + 0.28f;
            var midpoint = (from + to) * 0.5f + Vector3.up * 0.01f;
            var direction = to - from;
            var forward = direction.normalized;
            var side = Vector3.Cross(Vector3.up, forward);

            var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = "PathVisual";
            segment.transform.position = midpoint;
            segment.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            segment.transform.localScale = new Vector3(roadWidth, 0.05f, direction.magnitude);
            segment.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.42f, 0.25f, 0.08f));

            CreateRoadBank("RoadBank_Left", midpoint + side * bankOffset, forward, direction.magnitude);
            CreateRoadBank("RoadBank_Right", midpoint - side * bankOffset, forward, direction.magnitude);
        }

        private static void CreateRoadBank(string name, Vector3 position, Vector3 forward, float length)
        {
            var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bank.name = name;
            bank.transform.position = position + Vector3.up * 0.08f;
            bank.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            bank.transform.localScale = new Vector3(0.55f, 0.22f, length + 0.25f);
            bank.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.11f, 0.17f, 0.08f));
        }
    }

    internal sealed class SampleContent
    {
        public LevelDefinition Level { get; private set; }
        public SkillTreeDefinition SkillTree { get; private set; }
        public IReadOnlyList<TowerDefinition> Towers { get; private set; }

        public static SampleContent Create()
        {
            var runner = CreateEnemy("runner", "Goblin Runner", EnemyRole.Runner,
                "Fast and fragile. Dangerous in large groups because it slips past slow towers.",
                "Weak to rapid-fire towers, bends with overlapping coverage, and well-timed arrow volleys.",
                12f, 5.35f, 1, 1, new Color(0.2f, 0.9f, 0.25f), 0.36f);
            var brute = CreateEnemy("brute", "Orc Brute", EnemyRole.Heavy,
                "Slow but durable. It soaks repeated hits and punishes weak single-target damage.",
                "Weak to heavy single-target damage and long-range focus fire before it reaches the gate.",
                72f, 2.28f, 2, 4, new Color(0.05f, 0.45f, 0.08f), 0.6f);
            var shaman = CreateEnemy("shaman", "Witch Shaman", EnemyRole.Support,
                "Mid-speed support caster. For now it is a tougher priority target; later it will empower nearby hordes.",
                "Weak to burst damage and priority targeting before it can travel with the main pack.",
                32f, 3.35f, 1, 2, new Color(0.55f, 0.18f, 0.75f), 0.5f);
            var vampire = CreateEnemy("vampire", "Vampire", EnemyRole.Saboteur,
                "A duelist that hunts allied troops. Damaging allied units heals it and can raise its maximum health.",
                "Weak to ranged focus fire before it reaches your frontline.",
                48f, 3.55f, 2, 5, new Color(0.45f, 0.02f, 0.08f), 0.52f);
            var harpy = CreateEnemy("harpy", "Harpy", EnemyRole.Flying,
                "Flying enemy. It ignores ground pressure and can only be hit by anti-air towers or archers.",
                "Weak to Archer Towers, Ballistae, and Archer units from barracks.",
                24f, 4.2f, 1, 3, new Color(0.62f, 0.62f, 0.86f), 0.44f);
            var zombie = CreateEnemy("zombie", "Gravebound Zombie", EnemyRole.Undead,
                "Slow undead. The first time it falls, it rises once more at half health.",
                "Weak to sustained damage after its revival has been spent.",
                34f, 2.05f, 1, 2, new Color(0.38f, 0.5f, 0.34f), 0.5f);
            runner.alliedDamageMultiplier = 1.7f;
            runner.mass = 1f;
            brute.wallDamageMultiplier = 1.8f;
            brute.mass = 3f;
            shaman.healsEnemies = true;
            shaman.healAmount = 4f;
            shaman.mass = 1.5f;
            vampire.alliedDamageMultiplier = 2.1f;
            vampire.drainsAllies = true;
            vampire.drainHealMultiplier = 1.4f;
            vampire.mass = 2f;
            harpy.isFlying = true;
            harpy.mass = 1f;
            zombie.revivesOnce = true;
            zombie.infectsAllies = true;
            zombie.mass = 1.4f;

            var archer = CreateTower("archer", "Archer Tower", TowerRole.ArcherLine,
                "Reliable rapid-fire turret. Good against steady streams and weak enemies, but struggles with heavy targets.",
                "Weak against high-health enemies and dense waves once too many targets pass through at once.",
                0, 1, 7f, 4.2f, 1f / 2.3f, 18f, new Color(0.9f, 0.85f, 0.4f));
            archer.canHitFlying = true;
            var ballista = CreateTower("ballista", "Ballista", TowerRole.ArtilleryLine,
                "Long-range heavy hitter. Excellent against brutes and priority targets, but its slow rate can waste shots on swarms.",
                "Weak against fast swarms, overkill on tiny enemies, and enemies that slip past between shots.",
                0, 1, 11f, 16f, 1f / 0.7f, 14f, new Color(0.7f, 0.35f, 0.16f));
            ballista.canHitFlying = true;
            var bell = CreateTower("bell", "Bell Tower", TowerRole.ControlLine,
                "A lookout and alarm tower. Once tuned, its ringing slows a capped amount of enemies in its radius.",
                "Weak against very dense hordes until its slow capacity is upgraded.",
                0, 1, 6.5f, 2.4f, 0.24f, 22f, new Color(0.45f, 0.72f, 1f));
            bell.behavior = TowerBehavior.SlowAura;
            var catapult = CreateTower("catapult", "Catapult", TowerRole.ArtilleryLine,
                "Throws boulders in a high arc. When a boulder lands, it damages enemies in an area and knocks survivors outward.",
                "Weak against single fast enemies because the shot lands where the target was when fired.",
                0, 1, 9.5f, 7.5f, 2.8f, 8.5f, new Color(0.46f, 0.32f, 0.18f), ProjectilePattern.ArcSplash, 1.75f, 1.15f, 1.65f);
            var barrier = CreateTower("barrier", "Timber Barrier", TowerRole.BarrierLine,
                "A physical barricade that can be placed on the path. It absorbs enemy attacks until destroyed.",
                "Weak to enemies that specialize in breaking walls, especially orcs.",
                0, 1, 1.4f, 0f, 1f, 0f, new Color(0.46f, 0.28f, 0.13f));
            barrier.behavior = TowerBehavior.Barrier;
            barrier.health = 65f;
            var knightBarracks = CreateTower("knight_barracks", "Knight Barracks", TowerRole.BarracksLine,
                "Spawns knights that hold the line and fight enemies in melee.",
                "Weak to enemies that specialize in killing allied troops.",
                0, 1, 3.2f, 0f, 1f, 0f, new Color(0.36f, 0.36f, 0.52f));
            knightBarracks.behavior = TowerBehavior.Barracks;
            knightBarracks.barracksUnitType = AlliedUnitType.Knight;
            knightBarracks.alliedUnitHealth = 26f;
            knightBarracks.alliedUnitDamage = 4.5f;
            knightBarracks.alliedUnitBlockCapacity = 3f;
            knightBarracks.alliedUnitMoveSpeed = 3.2f;
            knightBarracks.alliedUnitAggroRange = 6f;
            var archerBarracks = CreateTower("archer_barracks", "Archer Post", TowerRole.BarracksLine,
                "Spawns archers that stand beside the road and fire arrows into the path.",
                "Weak to future ranged enemies and enemies that bypass the melee line.",
                0, 1, 3.8f, 0f, 1f, 0f, new Color(0.42f, 0.54f, 0.28f));
            archerBarracks.behavior = TowerBehavior.Barracks;
            archerBarracks.barracksUnitType = AlliedUnitType.Archer;
            archerBarracks.alliedUnitCanHitFlying = true;
            archerBarracks.alliedUnitRange = 3.4f;
            archerBarracks.alliedUnitHealth = 16f;
            archerBarracks.alliedUnitDamage = 3.2f;
            archerBarracks.alliedUnitBlockCapacity = 0f;
            archerBarracks.alliedUnitMoveSpeed = 3f;
            archerBarracks.alliedUnitAggroRange = 7f;
            var paladinBarracks = CreateTower("paladin_barracks", "Paladin Chapter", TowerRole.BarracksLine,
                "Spawns a durable paladin. Paladins take more space but bring higher defense.",
                "Weak because each paladin takes extra capacity and respawns slowly.",
                0, 1, 3.2f, 0f, 1f, 0f, new Color(0.72f, 0.66f, 0.35f));
            paladinBarracks.behavior = TowerBehavior.Barracks;
            paladinBarracks.barracksUnitType = AlliedUnitType.Paladin;
            paladinBarracks.barracksCapacity = 2;
            paladinBarracks.alliedUnitSlots = 2;
            paladinBarracks.alliedUnitHealth = 44f;
            paladinBarracks.alliedUnitDamage = 5.8f;
            paladinBarracks.alliedUnitDefense = 1.4f;
            paladinBarracks.alliedUnitBlockCapacity = 10f;
            paladinBarracks.alliedUnitMoveSpeed = 2.65f;
            paladinBarracks.alliedUnitAggroRange = 6f;
            paladinBarracks.barracksRespawnSeconds = 12f;

            var wave = ScriptableObject.CreateInstance<WaveDefinition>();
            wave.id = "wave_01";
            wave.totalEnemyCount = 300;
            wave.spawnInterval = 0.5f;
            wave.randomSpawnBurstMin = 3;
            wave.randomSpawnBurstMax = 8;
            wave.entries = BuildLevelOneWaveEntries(runner, brute);

            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.id = "level_01";
            level.displayName = "Broken Green Pass";
            level.startingLives = 10;
            level.wave = wave;
            level.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            level.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            level.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 8);

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
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
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
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
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
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponCooldownPercent, value = 2f } }
                },
                new SkillNodeDefinition
                {
                    id = "volley_auto_fire_unlock",
                    displayName = "Loose Command",
                    description = "Unlock active weapon auto-fire toggle.",
                    radialPosition = new Vector2(424f, 170f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "volley_cooldown_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 15) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponAutoFireUnlock, value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "volley_radius_01",
                    displayName = "Wider Volley",
                    description = "Each rank increases active weapon radius by 0.15.",
                    radialPosition = new Vector2(292f, -28f),
                    maxRanks = 5,
                    prerequisiteNodeIds = new[] { "volley_pierce_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponRadiusFlat, value = 0.15f } }
                },
                new SkillNodeDefinition
                {
                    id = "base_health_01",
                    displayName = "Reinforced Gate",
                    description = "Each rank gives the base 1 extra life.",
                    radialPosition = new Vector2(118f, 314f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "steady_tithe_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 9) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BaseLivesFlat, value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "steady_tithe_01",
                    displayName = "Steady Tithe",
                    description = "Each rank grants 3 bonus essence when a level ends.",
                    radialPosition = new Vector2(20f, 170f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costGrowthMultiplier = 2f,
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.LevelEndKillEssenceFlat, value = 3f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_unlock",
                    displayName = "Archer Tower",
                    description = "Unlock the Archer Tower for future runs.",
                    radialPosition = new Vector2(-150f, 52f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "archer", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "archer_limit_01",
                    displayName = "Archer Barracks",
                    description = "Each rank increases the Archer Tower limit by 1.",
                    radialPosition = new Vector2(-330f, 8f),
                    maxRanks = 4,
                    prerequisiteNodeIds = new[] { "archer_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "archer", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_damage_01",
                    displayName = "Fletching",
                    description = "Each rank increases Archer Tower damage by 4%.",
                    radialPosition = new Vector2(-280f, 130f),
                    maxRanks = 10,
                    prerequisiteNodeIds = new[] { "archer_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamagePercent, targetId = "archer", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_double_01",
                    displayName = "Twin Loose",
                    description = "Each rank gives Archer Tower 3% chance to fire a second shot.",
                    radialPosition = new Vector2(-420f, 154f),
                    maxRanks = 10,
                    prerequisiteNodeIds = new[] { "archer_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 8) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDoubleShotChancePercent, targetId = "archer", value = 3f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_flat_damage_01",
                    displayName = "Bodkin Heads",
                    description = "Each rank adds 0.5 Archer Tower damage.",
                    radialPosition = new Vector2(-560f, 190f),
                    maxRanks = 10,
                    prerequisiteNodeIds = new[] { "archer_double_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamageFlat, targetId = "archer", value = 0.5f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_speed_01",
                    displayName = "Quick Nocks",
                    description = "Each rank makes Archer Towers shoot 3% faster.",
                    radialPosition = new Vector2(-420f, 74f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "archer_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireRatePercent, targetId = "archer", value = 3f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_flat_speed_01",
                    displayName = "Draw Drills",
                    description = "Each rank adds 0.2 Archer Tower shots per second.",
                    radialPosition = new Vector2(-560f, 34f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "archer_speed_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 10) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireRateFlat, targetId = "archer", value = 0.2f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_unlock",
                    displayName = "Ballista",
                    description = "Unlock a slow tower with heavy single-target damage.",
                    radialPosition = new Vector2(-150f, -98f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "archer_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.VictorySigil, 1) },
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
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "ballista", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_damage_01",
                    displayName = "Heavy Bolts",
                    description = "Each rank increases Ballista damage by 4%.",
                    radialPosition = new Vector2(-298f, -190f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "ballista_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamagePercent, targetId = "ballista", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_pierce_01",
                    displayName = "Skewering Bolts",
                    description = "Each rank lets Ballista bolts pierce 1 additional nearby enemy.",
                    radialPosition = new Vector2(-452f, -190f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "ballista_damage_01" },
                    costGrowthMultiplier = 2.4f,
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 12) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerPierceFlat, targetId = "ballista", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_speed_01",
                    displayName = "Winch Drills",
                    description = "Each rank makes Ballista shoot 4% faster.",
                    radialPosition = new Vector2(-326f, -292f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "ballista_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireRatePercent, targetId = "ballista", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "bell_unlock",
                    displayName = "Bell Tower",
                    description = "Unlock the Bell Tower, a fast medieval lookout turret for cleaning up leaks.",
                    radialPosition = new Vector2(438f, -70f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "volley_radius_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.VictorySigil, 1) },
                    effects = new[]
                    {
                        new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "bell", value = 1f },
                        new UpgradeEffect { type = UpgradeEffectType.TowerSlowPercentFlat, targetId = "bell", value = 12f },
                        new UpgradeEffect { type = UpgradeEffectType.TowerSlowCapacityFlat, targetId = "bell", value = 10f }
                    },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "bell_limit_01",
                    displayName = "Signal Crews",
                    description = "Each rank increases the Bell Tower limit by 1.",
                    radialPosition = new Vector2(586f, -122f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "bell_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "bell", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "bell_slow_01",
                    displayName = "Heavy Clapper",
                    description = "Each rank increases Bell Tower slow by 3%.",
                    radialPosition = new Vector2(586f, 8f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "bell_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerSlowPercentFlat, targetId = "bell", value = 3f } }
                },
                new SkillNodeDefinition
                {
                    id = "bell_capacity_01",
                    displayName = "Wider Toll",
                    description = "Each rank increases how much enemy mass the Bell Tower can slow.",
                    radialPosition = new Vector2(724f, -42f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "bell_slow_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerSlowCapacityFlat, targetId = "bell", value = 3f } }
                },
                new SkillNodeDefinition
                {
                    id = "bell_range_01",
                    displayName = "High Belfry",
                    description = "Each rank increases Bell Tower radius by 0.35.",
                    radialPosition = new Vector2(724f, -172f),
                    maxRanks = 5,
                    prerequisiteNodeIds = new[] { "bell_limit_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerRangeFlat, targetId = "bell", value = 0.35f } }
                },
                new SkillNodeDefinition
                {
                    id = "catapult_unlock",
                    displayName = "Catapult",
                    description = "Unlock the Catapult, an arcing splash tower that knocks enemies away from the impact.",
                    radialPosition = new Vector2(-150f, -306f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "ballista_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.VictorySigil, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "catapult", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "catapult_limit_01",
                    displayName = "Siege Yard",
                    description = "Each rank increases the Catapult limit by 1.",
                    radialPosition = new Vector2(-306f, -338f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "catapult_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "catapult", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "catapult_damage_01",
                    displayName = "Heavier Stones",
                    description = "Each rank increases Catapult damage by 4%.",
                    radialPosition = new Vector2(-150f, -430f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "catapult_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamagePercent, targetId = "catapult", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "catapult_speed_01",
                    displayName = "Trained Winches",
                    description = "Each rank makes Catapult shoot 4% faster.",
                    radialPosition = new Vector2(-306f, -446f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "catapult_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireRatePercent, targetId = "catapult", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "catapult_fire_unlock",
                    displayName = "Pitch-Soaked Stones",
                    description = "Catapult boulders ignite enemies hit by the splash.",
                    radialPosition = new Vector2(-150f, -562f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "catapult_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[]
                    {
                        new UpgradeEffect { type = UpgradeEffectType.EnableTowerFire, targetId = "catapult", value = 1f },
                        new UpgradeEffect { type = UpgradeEffectType.TowerFireDamagePerTickFlat, targetId = "catapult", value = 0.7f },
                        new UpgradeEffect { type = UpgradeEffectType.TowerFireTicksPerSecondFlat, targetId = "catapult", value = 1f },
                        new UpgradeEffect { type = UpgradeEffectType.TowerFireMaxStacksFlat, targetId = "catapult", value = 1f },
                        new UpgradeEffect { type = UpgradeEffectType.TowerFireDurationFlat, targetId = "catapult", value = 3f }
                    },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "catapult_fire_damage_01",
                    displayName = "Hotter Pitch",
                    description = "Each rank increases Catapult burn damage per tick by 0.25.",
                    radialPosition = new Vector2(-306f, -610f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "catapult_fire_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireDamagePerTickFlat, targetId = "catapult", value = 0.25f } }
                },
                new SkillNodeDefinition
                {
                    id = "catapult_fire_rate_01",
                    displayName = "Hungry Flames",
                    description = "Each rank increases Catapult burn tick rate by 0.15 per second.",
                    radialPosition = new Vector2(6f, -610f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "catapult_fire_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireTicksPerSecondFlat, targetId = "catapult", value = 0.15f } }
                },
                new SkillNodeDefinition
                {
                    id = "catapult_fire_stacks_01",
                    displayName = "Layered Pitch",
                    description = "Each rank lets Catapult fire stack one additional time.",
                    radialPosition = new Vector2(-458f, -684f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "catapult_fire_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 4) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireMaxStacksFlat, targetId = "catapult", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "catapult_fire_duration_01",
                    displayName = "Clinging Tar",
                    description = "Each rank makes Catapult fire last 0.5 seconds longer.",
                    radialPosition = new Vector2(158f, -684f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "catapult_fire_rate_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireDurationFlat, targetId = "catapult", value = 0.5f } }
                },
                new SkillNodeDefinition
                {
                    id = "barrier_unlock",
                    displayName = "Timber Barrier",
                    description = "Unlock a destructible physical barrier that can be placed on the enemy path.",
                    radialPosition = new Vector2(20f, -220f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.VictorySigil, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "barrier", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "barrier_health_01",
                    displayName = "Layered Timbers",
                    description = "Each rank increases Timber Barrier health by 20.",
                    radialPosition = new Vector2(18f, -356f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "barrier_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerHealthFlat, targetId = "barrier", value = 20f } }
                },
                new SkillNodeDefinition
                {
                    id = "barrier_thorns_01",
                    displayName = "Iron Spikes",
                    description = "Each rank makes the barrier damage enemies that hit it.",
                    radialPosition = new Vector2(154f, -344f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "barrier_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerThornsDamageFlat, targetId = "barrier", value = 1.5f } }
                },
                new SkillNodeDefinition
                {
                    id = "barrier_limit_01",
                    displayName = "Reserve Timbers",
                    description = "Each rank lets you place one additional Timber Barrier.",
                    radialPosition = new Vector2(-114f, -344f),
                    maxRanks = 5,
                    prerequisiteNodeIds = new[] { "barrier_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "barrier", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "knight_barracks_unlock",
                    displayName = "Knight Barracks",
                    description = "Unlock barracks that respawn one knight defender.",
                    radialPosition = new Vector2(310f, 292f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "base_health_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.PerfectSigil, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "knight_barracks", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "barracks_capacity_01",
                    displayName = "Knight Bunks",
                    description = "Each rank lets Knight Barracks hold one more troop slot.",
                    radialPosition = new Vector2(466f, 326f),
                    maxRanks = 4,
                    prerequisiteNodeIds = new[] { "knight_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitCapacityFlat, targetId = "knight_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "barracks_damage_01",
                    displayName = "Knight Steel",
                    description = "Each rank increases knight damage by 5%.",
                    radialPosition = new Vector2(466f, 228f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "knight_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitDamagePercent, targetId = "knight_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "barracks_health_01",
                    displayName = "Knight Mail",
                    description = "Each rank increases knight health by 5%.",
                    radialPosition = new Vector2(618f, 300f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "barracks_capacity_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitHealthPercent, targetId = "knight_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "barracks_respawn_01",
                    displayName = "Knight Muster",
                    description = "Each rank reduces Knight Barracks respawn time by 4%.",
                    radialPosition = new Vector2(618f, 202f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "barracks_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksRespawnCooldownPercent, targetId = "knight_barracks", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_barracks_unlock",
                    displayName = "Archer Post",
                    description = "Unlock barracks that respawn anti-air archers.",
                    radialPosition = new Vector2(772f, 304f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "barracks_health_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.PerfectSigil, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "archer_barracks", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "archer_post_capacity_01",
                    displayName = "Arrow Racks",
                    description = "Each rank lets Archer Post hold one more troop slot.",
                    radialPosition = new Vector2(908f, 386f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "archer_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitCapacityFlat, targetId = "archer_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_post_damage_01",
                    displayName = "War Arrows",
                    description = "Each rank increases archer damage by 5%.",
                    radialPosition = new Vector2(950f, 498f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "archer_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitDamagePercent, targetId = "archer_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_post_health_01",
                    displayName = "Leather Jacks",
                    description = "Each rank increases archer health by 5%.",
                    radialPosition = new Vector2(1076f, 432f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "archer_post_capacity_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitHealthPercent, targetId = "archer_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_post_respawn_01",
                    displayName = "Ready Quivers",
                    description = "Each rank reduces Archer Post respawn time by 4%.",
                    radialPosition = new Vector2(1098f, 544f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "archer_post_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksRespawnCooldownPercent, targetId = "archer_barracks", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "paladin_barracks_unlock",
                    displayName = "Paladin Chapter",
                    description = "Unlock barracks that respawn durable paladins.",
                    radialPosition = new Vector2(772f, 202f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "barracks_respawn_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.PerfectSigil, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "paladin_barracks", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "paladin_chapter_capacity_01",
                    displayName = "Chapter Cells",
                    description = "Each rank lets Paladin Chapter hold one more troop slot.",
                    radialPosition = new Vector2(930f, 156f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "paladin_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitCapacityFlat, targetId = "paladin_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "paladin_chapter_damage_01",
                    displayName = "Blessed Maces",
                    description = "Each rank increases paladin damage by 5%.",
                    radialPosition = new Vector2(930f, 64f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "paladin_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 4) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitDamagePercent, targetId = "paladin_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "paladin_chapter_health_01",
                    displayName = "Plate Vows",
                    description = "Each rank increases paladin health by 5%.",
                    radialPosition = new Vector2(1084f, 156f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "paladin_chapter_capacity_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 4) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitHealthPercent, targetId = "paladin_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "paladin_chapter_respawn_01",
                    displayName = "Chapter Bells",
                    description = "Each rank reduces Paladin Chapter respawn time by 4%.",
                    radialPosition = new Vector2(1084f, 64f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "paladin_chapter_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksRespawnCooldownPercent, targetId = "paladin_barracks", value = 4f } }
                }
            };

            return new SampleContent
            {
                Level = level,
                SkillTree = tree,
                Towers = new[] { archer, ballista, bell, catapult, barrier, knightBarracks, archerBarracks, paladinBarracks }
            };
        }

        private static EnemyDefinition CreateEnemy(string id, string name, EnemyRole role, string shortDescription, string weaknessDescription, float hp, float speed, int lifeDamage, int killReward, Color color, float scale)
        {
            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            enemy.id = id;
            enemy.displayName = name;
            enemy.shortDescription = shortDescription;
            enemy.weaknessDescription = weaknessDescription;
            enemy.role = role;
            enemy.maxHealth = hp;
            enemy.speed = speed;
            enemy.lifeDamage = lifeDamage;
            enemy.killReward = killReward;
            enemy.color = color;
            enemy.visualScale = scale;
            return enemy;
        }

        private static WaveEntry[] BuildLevelOneWaveEntries(EnemyDefinition runner, EnemyDefinition brute)
        {
            var entries = new List<WaveEntry>();
            for (var i = 0; i < 33; i++)
            {
                entries.Add(new WaveEntry { enemy = runner, count = 7 });
                entries.Add(new WaveEntry { enemy = brute, count = 2 });
            }

            entries.Add(new WaveEntry { enemy = runner, count = 3 });
            return entries.ToArray();
        }

        private static TowerDefinition CreateTower(
            string id,
            string name,
            TowerRole role,
            string shortDescription,
            string weaknessDescription,
            int era,
            int limit,
            float range,
            float damage,
            float fireInterval,
            float projectileSpeed,
            Color color,
            ProjectilePattern projectilePattern = ProjectilePattern.Direct,
            float splashRadius = 0f,
            float knockbackDistance = 0f,
            float arcFlightTimeMultiplier = 1f)
        {
            var tower = ScriptableObject.CreateInstance<TowerDefinition>();
            tower.id = id;
            tower.displayName = name;
            tower.shortDescription = shortDescription;
            tower.weaknessDescription = weaknessDescription;
            tower.role = role;
            tower.eraIndex = era;
            tower.perTypeLimit = limit;
            tower.range = range;
            tower.damage = damage;
            tower.fireInterval = fireInterval;
            tower.projectileSpeed = projectileSpeed;
            tower.projectilePattern = projectilePattern;
            tower.splashRadius = splashRadius;
            tower.knockbackDistance = knockbackDistance;
            tower.arcFlightTimeMultiplier = arcFlightTimeMultiplier;
            tower.color = color;
            return tower;
        }
    }
}
