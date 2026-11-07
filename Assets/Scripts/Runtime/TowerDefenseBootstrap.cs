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
        private Camera mainCamera;
        private TopDownCameraController cameraController;
        private GameObject mapRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeBootstrap()
        {
            if (FindAnyObjectByType<TowerDefenseBootstrap>() != null)
            {
                return;
            }

            new GameObject("TowerDefenseBootstrap").AddComponent<TowerDefenseBootstrap>();
        }

        private void Awake()
        {
            ConfigureRuntimePerformanceDefaults();
            var content = SampleContent.Create();
            var camera = CreateCamera();
            mainCamera = camera;
            CreateLight();
            var route = LoadLevelMap(content.Level);

            var input = gameObject.AddComponent<PlayerInputRouter>();
            input.Initialize(camera);

            cameraController = gameObject.AddComponent<TopDownCameraController>();
            cameraController.Initialize(camera, input);
            ApplyLevelCamera(content.Level);

            var enemyManager = new GameObject("EnemyManager").AddComponent<EnemyManager>();
            var hordeManager = new GameObject("HordeEnemyManager").AddComponent<HordeEnemyManager>();
            var corpseManager = new GameObject("EnemyCorpses").AddComponent<EnemyCorpseManager>();
            enemyManager.SetCorpseManager(corpseManager);
            enemyManager.SetHordePrototype(hordeManager);
            var towerManager = new GameObject("TowerManager").AddComponent<TowerManager>();
            var activeWeapon = new GameObject("ActiveWeapon").AddComponent<ActiveWeaponController>();
            activeWeapon.Initialize(enemyManager, input, towerManager, cameraController);
            var popups = new GameObject("WorldPopups").AddComponent<WorldPopupManager>();
            popups.Initialize(camera);

            var session = gameObject.AddComponent<GameSession>();
            session.Initialize(
                content.Levels,
                content.Level,
                content.SkillTree,
                route,
                LoadLevelMap,
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

        private static void ConfigureRuntimePerformanceDefaults()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        private PathRoute LoadLevelMap(LevelDefinition level)
        {
            if (mapRoot != null)
            {
                Destroy(mapRoot);
            }

            mapRoot = new GameObject("LevelMap");
            CreateGround(mapRoot.transform, level);
            var route = CreatePath(level, mapRoot.transform);
            ApplyLevelCamera(level);
            return route;
        }

        private void ApplyLevelCamera(LevelDefinition level)
        {
            if (level == null || mainCamera == null)
            {
                return;
            }

            mainCamera.transform.position = level.cameraPosition;
            mainCamera.fieldOfView = level.cameraFieldOfView;
            cameraController?.ApplyView(level.cameraPosition, level.cameraFieldOfView, level.cameraMinHeight, level.cameraMaxHeight, level.cameraPanSpeed, level.cameraMouseDragSensitivity, level.cameraMinBounds, level.cameraMaxBounds);
        }

        private static Camera CreateCamera()
        {
            var camera = Camera.main;
            var go = camera != null ? camera.gameObject : new GameObject("Main Camera");
            go.tag = "MainCamera";
            camera ??= go.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 24f, -20f);
            camera.transform.rotation = Quaternion.Euler(75f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PlaceholderPalette.Background;
            camera.fieldOfView = 45f;
            camera.orthographic = true;
            camera.orthographicSize = 24f / Mathf.Sin(75f * Mathf.Deg2Rad) * Mathf.Tan(22.5f * Mathf.Deg2Rad);
            return camera;
        }

        private static void CreateLight()
        {
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Light light = null;
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    light = lights[i];
                    break;
                }
            }

            var go = light != null ? light.gameObject : new GameObject("Sun");
            light ??= go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = .7f;
            light.shadowBias = .035f;
            light.shadowNormalBias = .18f;
            go.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = PlaceholderPalette.Grey(.35f);
            RenderSettings.sun = light;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 110f;
        }

        private static void CreateGround(Transform parent, LevelDefinition level)
        {
            if (level != null && level.useCircularArena)
            {
                var arenaGround = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                arenaGround.name = "BuildableGround";
                arenaGround.transform.SetParent(parent, false);
                arenaGround.transform.position = level.groundCenter;
                var groundRadius = Mathf.Max(2f, level.arenaRadius + 5f);
                arenaGround.transform.localScale = new Vector3(groundRadius * 2f, 0.05f, groundRadius * 2f);
                arenaGround.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Dark);
                RemovePrimitiveCollider(arenaGround);
                return;
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "BuildableGround";
            ground.transform.SetParent(parent, false);
            ground.transform.position = level != null ? level.groundCenter : new Vector3(0f, -0.08f, 1.5f);
            ground.transform.localScale = level != null ? level.groundSize : new Vector3(82f, 0.1f, 50f);
            ground.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Dark);
        }


        private static void RemovePrimitiveCollider(GameObject target)
        {
            var components = target.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }
        }

        private static PathRoute CreatePath(LevelDefinition level, Transform parent)
        {
            var routeObject = new GameObject("PathRoute");
            routeObject.transform.SetParent(parent, false);
            var route = routeObject.AddComponent<PathRoute>();
            var points = level?.pathWaypoints != null && level.pathWaypoints.Length > 1
                ? level.pathWaypoints
                : new[]
            {
                new Vector3(-32f, 0f, 9.5f),
                new Vector3(-20.5f, 0f, 9.5f),
                new Vector3(-13.2f, 0f, 9.2f),
                new Vector3(-13.2f, 0f, -7.8f),
                new Vector3(3.8f, 0f, -7.8f),
                new Vector3(3.8f, 0f, 10.2f),
                new Vector3(15.6f, 0f, 10.2f),
                new Vector3(26.5f, 0f, 10.2f)
            };

            var roadWidth = Mathf.Max(1f, level != null ? level.roadWidth : 5.4f);
            var waypointWidths = ResolveWaypointWidths(points, level?.pathWaypointWidths, roadWidth);
            var useSquareCorners = level != null && level.useSquarePathCorners;
            route.SetWaypoints(points, waypointWidths);
            route.SetSquareCorners(useSquareCorners);
            if (level != null && level.useCircularArena)
            {
                CreateCircularArenaVisual(parent, points[0], Mathf.Max(1f, level.arenaRadius));
                return route;
            }

            if (level?.secondaryPathWaypoints != null && level.secondaryPathWaypoints.Length > 1)
            {
                var secondaryWidths = ResolveWaypointWidths(
                    level.secondaryPathWaypoints,
                    level.secondaryPathWaypointWidths,
                    roadWidth);
                route.SetSecondaryWaypoints(level.secondaryPathWaypoints, secondaryWidths);
            }
            if (level?.tertiaryPathWaypoints != null && level.tertiaryPathWaypoints.Length > 1)
            {
                var tertiaryWidths = ResolveWaypointWidths(
                    level.tertiaryPathWaypoints,
                    level.tertiaryPathWaypointWidths,
                    roadWidth);
                route.SetTertiaryWaypoints(level.tertiaryPathWaypoints, tertiaryWidths);
            }

            RoadSurface.Create(parent, route, level);
            return route;
        }

        private static void CreateCircularArenaVisual(Transform parent, Vector3 center, float radius)
        {
            CreateArenaDisc(parent, "ArenaContactShadow", center, radius + 0.45f, 0.002f,
                BootstrapMaterials.Get(PlaceholderPalette.Dark));
            CreateArenaDisc(parent, "ArenaFloor", center, radius, 0.092f,
                BootstrapMaterials.Get(PlaceholderPalette.Road));

            var boundary = new GameObject("ArenaBoundary");
            boundary.transform.SetParent(parent, false);
            var line = boundary.AddComponent<LineRenderer>();
            const int segmentCount = 96;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = segmentCount;
            line.widthMultiplier = 0.22f;
            line.numCornerVertices = 2;
            line.material = BootstrapMaterials.Get(PlaceholderPalette.Ground);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (var i = 0; i < segmentCount; i++)
            {
                var angle = i * Mathf.PI * 2f / segmentCount;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0.165f, Mathf.Sin(angle) * radius));
            }
        }

        private static void CreateArenaDisc(
            Transform parent,
            string name,
            Vector3 center,
            float radius,
            float height,
            Material material)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            disc.transform.SetParent(parent, false);
            disc.transform.position = center + Vector3.up * height;
            disc.transform.localScale = new Vector3(radius * 2f, 0.025f, radius * 2f);
            disc.GetComponent<Renderer>().material = material;
            RemovePrimitiveCollider(disc);
        }

        private static float[] ResolveWaypointWidths(Vector3[] points, float[] authoredWidths, float fallbackWidth)
        {
            var widths = new float[points.Length];
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = authoredWidths != null && authoredWidths.Length == points.Length
                    ? Mathf.Max(1f, authoredWidths[i])
                    : fallbackWidth;
            }

            return widths;
        }
    }

    internal sealed class SampleContent
    {
        public LevelDefinition Level { get; private set; }
        public IReadOnlyList<LevelDefinition> Levels { get; private set; }
        public SkillTreeDefinition SkillTree { get; private set; }
        public IReadOnlyList<TowerDefinition> Towers { get; private set; }

        public static SampleContent Create()
        {
            var runner = CreateEnemy("runner", "Goblin Runner", EnemyRole.Runner,
                "Fast and fragile. Dangerous in large groups because it slips past slow towers.",
                "Weak to rapid-fire towers, bends with overlapping coverage, and well-timed arrow volleys.",
                12f, 5.35f, 1, 1, PlaceholderPalette.Enemy, 0.36f);
            var brute = CreateEnemy("brute", "Orc Brute", EnemyRole.Heavy,
                "Slow but durable. It soaks repeated hits and punishes weak single-target damage.",
                "Weak to heavy single-target damage and long-range focus fire before it reaches the gate.",
                72f, 2.28f, 2, 4, PlaceholderPalette.Enemy, 0.39f);
            var shaman = CreateEnemy("shaman", "Witch Shaman", EnemyRole.Support,
                "Mid-speed support caster. For now it is a tougher priority target; later it will empower nearby hordes.",
                "Weak to burst damage and priority targeting before it can travel with the main pack.",
                32f, 3.35f, 1, 2, PlaceholderPalette.Enemy, 0.5f);
            var vampire = CreateEnemy("vampire", "Vampire", EnemyRole.Saboteur,
                "A duelist that hunts allied troops. Damaging allied units heals it and can raise its maximum health.",
                "Weak to ranged focus fire before it reaches your frontline.",
                48f, 3.55f, 2, 5, PlaceholderPalette.Enemy, 0.52f);
            var harpy = CreateEnemy("harpy", "Harpy", EnemyRole.Flying,
                "Flying enemy. It ignores ground pressure and can only be hit by anti-air towers or archers.",
                "Weak to Archer Towers, Ballistae, and Archer units from barracks.",
                24f, 4.2f, 1, 3, PlaceholderPalette.Enemy, 0.44f);
            var zombie = CreateEnemy("zombie", "Gravebound Zombie", EnemyRole.Undead,
                "Slow undead. The first time it falls, it rises once more at half health.",
                "Weak to sustained damage after its revival has been spent.",
                34f, 2.05f, 1, 2, PlaceholderPalette.Enemy, 0.5f);
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
                (int)EraId.Medieval, 1, 7f, 4.2f, 1f / 2.3f, 18f, PlaceholderPalette.Tower);
            archer.canHitFlying = true;
            var ballista = CreateTower("ballista", "Ballista", TowerRole.ArtilleryLine,
                "Long-range heavy hitter. Excellent against brutes and priority targets, but its slow rate can waste shots on swarms.",
                "Weak against fast swarms, overkill on tiny enemies, and enemies that slip past between shots.",
                (int)EraId.Medieval, 1, 11f, 16f, 1f / 0.7f, 14f, PlaceholderPalette.Tower);
            ballista.canHitFlying = true;
            ballista.flyThroughShot = true;
            // Prehistoric era (user, 2026-11-07): Slingers (to come) evolve into the Archer Tower,
            // Spear Throwers into the Ballista and the Tamed Mammoth into a war elephant.
            var spearThrower = CreateTower("spear_thrower", "Spear Throwers", TowerRole.ArtilleryLine,
                "Hunters hurl long spears with atlatls. Long range and heavy hits; each spear flies on along its line past the target.",
                "Weak against fast swarms: the throws are slow, and goblins slip past between spears.",
                (int)EraId.Prehistoric, 1, 9.5f, 12f, 1.6f, 12f, PlaceholderPalette.Tower);
            spearThrower.canHitFlying = true;
            spearThrower.flyThroughShot = true;
            spearThrower.nextEvolution = ballista;
            var mammoth = CreateTower("mammoth", "Tamed Mammoth", TowerRole.ControlLine,
                "A mammoth waits in its pen beside the road. When goblins come near, it charges along the road into them, trampling and scattering the horde, then walks back to rest.",
                "Weak against steady streams: between charges the road is open, and it never chases enemies off the road.",
                (int)EraId.Prehistoric, 1, 8f, 9f, 7f, 0f, PlaceholderPalette.Tower);
            mammoth.behavior = TowerBehavior.Charge;
            mammoth.knockbackDistance = 1.6f;
            mammoth.chargeDistance = 14f;
            mammoth.chargeSpeed = 9f;
            // Wide enough to reach the whole road: the horde hugs the inside of bends, and the beast
            // runs down the middle.
            mammoth.chargeRadius = 2.6f;
            var bell = CreateTower("bell", "Bell Tower", TowerRole.ControlLine,
                "A lookout and alarm tower. Once tuned, its ringing slows a capped amount of enemies in its radius.",
                "Weak against very dense hordes until its slow capacity is upgraded.",
                (int)EraId.Medieval, 1, 6.5f, 2.4f, 0.24f, 22f, PlaceholderPalette.Tower);
            bell.behavior = TowerBehavior.SlowAura;
            var catapult = CreateTower("catapult", "Catapult", TowerRole.ArtilleryLine,
                "Throws boulders in a high arc. When a boulder lands, it damages enemies in an area and knocks survivors outward.",
                "Weak against single fast enemies because the shot lands where the target was when fired.",
                (int)EraId.Medieval, 1, 9.5f, 7.5f, 2.8f, 8.5f, PlaceholderPalette.Tower, ProjectilePattern.ArcSplash, 1.75f, 1.15f, 1.65f);
            var barrier = CreateTower("barrier", "Timber Barrier", TowerRole.BarrierLine,
                "A physical barricade that can be placed on the path. It absorbs enemy attacks until destroyed.",
                "Weak to enemies that specialize in breaking walls, especially orcs.",
                (int)EraId.Medieval, 1, 1.4f, 0f, 1f, 0f, PlaceholderPalette.Tower);
            barrier.behavior = TowerBehavior.Barrier;
            barrier.health = 65f;
            var knightBarracks = CreateTower("knight_barracks", "Knight Barracks", TowerRole.BarracksLine,
                "Spawns knights that hold the line and fight enemies in melee.",
                "Weak to enemies that specialize in killing allied troops.",
                (int)EraId.Medieval, 1, 3.2f, 0f, 1f, 0f, PlaceholderPalette.Tower);
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
                (int)EraId.Medieval, 1, 3.8f, 0f, 1f, 0f, PlaceholderPalette.Tower);
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
                (int)EraId.Medieval, 1, 3.2f, 0f, 1f, 0f, PlaceholderPalette.Tower);
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
            wave.totalEnemyCount = 215;
            wave.spawnInterval = 0.5f;
            wave.randomSpawnBurstMin = 3;
            wave.randomSpawnBurstMax = 8;
            wave.enforceSpawnClearance = true;
            wave.entries = BuildLevelOneWaveEntries(runner, brute);

            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.id = "level_01";
            level.displayName = "Broken Green Pass";
            level.startingLives = 10;
            level.wave = wave;
            level.pathWaypoints = CreateLevelOnePath();
            level.groundCenter = new Vector3(0f, -0.08f, 1.5f);
            level.groundSize = new Vector3(82f, 0.1f, 50f);
            level.environmentTilePackage = EnvironmentTilePackageId.Prehistoric;
            level.cameraPosition = new Vector3(0f, 24f, -20f);
            level.cameraFieldOfView = 45f;
            level.cameraMinHeight = 8f;
            level.cameraMaxHeight = 60f;
            level.cameraPanSpeed = 26f;
            level.cameraMouseDragSensitivity = 3.35f;
            level.cameraMinBounds = new Vector2(-36f, -22f);
            level.cameraMaxBounds = new Vector2(36f, 22f);
            level.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            level.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            level.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 8);
            level.bossClearReward = new CurrencyAmount(CurrencyType.BossCore, 1);
            level.challengeReward = new CurrencyAmount(CurrencyType.ChallengeToken, 1);
            level.recommendedTactics = "Use early Archer Towers to thin Goblin Runners, then place them around bends so their range covers the road for longer. Save Volley of Arrows for dense mixed packs or Brutes that are about to leak through.";

            var levelTwoWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelTwoWave.id = "wave_02_placeholder";
            levelTwoWave.totalEnemyCount = 10000;
            levelTwoWave.spawnInterval = 0.009f;
            levelTwoWave.randomSpawnBurstMin = 5;
            levelTwoWave.randomSpawnBurstMax = 12;
            levelTwoWave.useEndpointSeeking = true;
            levelTwoWave.enforceSpawnClearance = true;
            levelTwoWave.enemyScaleMultiplier = 0.7f;
            levelTwoWave.enemyInertiaMultiplier = 1.35f;
            levelTwoWave.enemyMassMultiplier = 1.3f;
            levelTwoWave.collisionPressureMultiplier = 1.2f;
            levelTwoWave.roadHalfWidth = 15f;
            // Level 2 keeps the reference crowd physics, but navigation is constrained to
            // the authored roads. Open-field flow remains available for a separate level.
            levelTwoWave.hordeNavigationMode = HordeNavigationMode.Corridor;
            levelTwoWave.entries = new[]
            {
                new WaveEntry { enemy = runner, count = 10000 }
            };

            var levelTwo = ScriptableObject.CreateInstance<LevelDefinition>();
            levelTwo.id = "level_02";
            levelTwo.displayName = "Three-Lane Horde Test";
            levelTwo.startingLives = 12;
            levelTwo.preventLifeLoss = true;
            levelTwo.wave = levelTwoWave;
            levelTwo.useDataHordePrototype = true;
            levelTwo.pathWaypoints = CreateLevelTwoPath();
            levelTwo.pathWaypointWidths = CreateLevelTwoPathWidths();
            levelTwo.secondaryPathWaypoints = CreateLevelTwoUpperPath();
            levelTwo.secondaryPathWaypointWidths = CreateLevelTwoPathWidths();
            levelTwo.tertiaryPathWaypoints = CreateLevelTwoLowerPath();
            levelTwo.tertiaryPathWaypointWidths = CreateLevelTwoPathWidths();
            levelTwo.groundCenter = new Vector3(0f, -0.08f, 0f);
            levelTwo.groundSize = new Vector3(126f, 0.1f, 70f);
            levelTwo.environmentTilePackage = EnvironmentTilePackageId.Prehistoric;
            levelTwo.cameraPosition = new Vector3(0f, 42f, -32f);
            levelTwo.cameraFieldOfView = 50f;
            levelTwo.cameraMinHeight = 10f;
            levelTwo.cameraMaxHeight = 88f;
            levelTwo.cameraPanSpeed = 38f;
            levelTwo.cameraMouseDragSensitivity = 4.75f;
            levelTwo.cameraMinBounds = new Vector2(-62f, -34f);
            levelTwo.cameraMaxBounds = new Vector2(62f, 34f);
            levelTwo.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            levelTwo.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            levelTwo.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 12);
            levelTwo.bossClearReward = new CurrencyAmount(CurrencyType.BossCore, 1);
            levelTwo.challengeReward = new CurrencyAmount(CurrencyType.ChallengeToken, 1);
            levelTwo.recommendedTactics = "Horde stress test: 10,000 heavier, high-inertia enemies split between three three-metre lanes before merging into a three-metre finish.";

            var levelTwoPointOneWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelTwoPointOneWave.id = "wave_02_1_three_exit";
            levelTwoPointOneWave.totalEnemyCount = 10000;
            levelTwoPointOneWave.spawnInterval = 0.009f;
            levelTwoPointOneWave.randomSpawnBurstMin = 5;
            levelTwoPointOneWave.randomSpawnBurstMax = 12;
            levelTwoPointOneWave.useEndpointSeeking = true;
            levelTwoPointOneWave.enforceSpawnClearance = true;
            levelTwoPointOneWave.enemyScaleMultiplier = 0.7f;
            levelTwoPointOneWave.enemyInertiaMultiplier = 1.35f;
            levelTwoPointOneWave.enemyMassMultiplier = 1.3f;
            levelTwoPointOneWave.collisionPressureMultiplier = 1.2f;
            levelTwoPointOneWave.roadHalfWidth = 15f;
            levelTwoPointOneWave.hordeNavigationMode = HordeNavigationMode.Corridor;
            levelTwoPointOneWave.entries = new[]
            {
                new WaveEntry { enemy = runner, count = 10000 }
            };

            var levelTwoPointOne = ScriptableObject.CreateInstance<LevelDefinition>();
            levelTwoPointOne.id = "level_02_1";
            levelTwoPointOne.displayName = "Three-Exit Horde Flow Test";
            levelTwoPointOne.startingLives = 12;
            levelTwoPointOne.preventLifeLoss = true;
            levelTwoPointOne.wave = levelTwoPointOneWave;
            levelTwoPointOne.useDataHordePrototype = true;
            levelTwoPointOne.pathWaypoints = CreateLevelTwoPath();
            levelTwoPointOne.pathWaypointWidths = CreateLevelTwoPathWidths();
            levelTwoPointOne.secondaryPathWaypoints = CreateLevelTwoThreeExitUpperPath();
            levelTwoPointOne.secondaryPathWaypointWidths = CreateLevelTwoPathWidths();
            levelTwoPointOne.tertiaryPathWaypoints = CreateLevelTwoThreeExitLowerPath();
            levelTwoPointOne.tertiaryPathWaypointWidths = CreateLevelTwoPathWidths();
            levelTwoPointOne.groundCenter = new Vector3(0f, -0.08f, 0f);
            levelTwoPointOne.groundSize = new Vector3(126f, 0.1f, 70f);
            levelTwoPointOne.environmentTilePackage = EnvironmentTilePackageId.Prehistoric;
            levelTwoPointOne.cameraPosition = new Vector3(0f, 42f, -32f);
            levelTwoPointOne.cameraFieldOfView = 50f;
            levelTwoPointOne.cameraMinHeight = 10f;
            levelTwoPointOne.cameraMaxHeight = 88f;
            levelTwoPointOne.cameraPanSpeed = 38f;
            levelTwoPointOne.cameraMouseDragSensitivity = 4.75f;
            levelTwoPointOne.cameraMinBounds = new Vector2(-62f, -34f);
            levelTwoPointOne.cameraMaxBounds = new Vector2(62f, 34f);
            levelTwoPointOne.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            levelTwoPointOne.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            levelTwoPointOne.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 12);
            levelTwoPointOne.bossClearReward = new CurrencyAmount(CurrencyType.BossCore, 1);
            levelTwoPointOne.challengeReward = new CurrencyAmount(CurrencyType.ChallengeToken, 1);
            levelTwoPointOne.recommendedTactics = "Control test: the same 10,000-enemy entrance and middle as Level 2, but each lane keeps an equal three-metre exit instead of merging.";

            var levelThreeWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelThreeWave.id = "wave_03_foundation";
            levelThreeWave.totalEnemyCount = 2040;
            levelThreeWave.spawnInterval = 0.34f;
            levelThreeWave.randomSpawnBurstMin = 9;
            levelThreeWave.randomSpawnBurstMax = 20;
            levelThreeWave.enforceSpawnClearance = true;
            levelThreeWave.entries = BuildLevelThreeWaveEntries(runner, brute, shaman, vampire, harpy, zombie);

            var levelThree = ScriptableObject.CreateInstance<LevelDefinition>();
            levelThree.id = "level_03";
            levelThree.displayName = "Haunted Causeway";
            levelThree.startingLives = 14;
            levelThree.wave = levelThreeWave;
            levelThree.pathWaypoints = CreateLevelThreePath();
            levelThree.groundCenter = new Vector3(0f, -0.08f, 1f);
            levelThree.groundSize = new Vector3(150f, 0.1f, 84f);
            levelThree.environmentTilePackage = EnvironmentTilePackageId.Prehistoric;
            levelThree.cameraPosition = new Vector3(0f, 50f, -38f);
            levelThree.cameraFieldOfView = 52f;
            levelThree.cameraMinHeight = 12f;
            levelThree.cameraMaxHeight = 120f;
            levelThree.cameraPanSpeed = 48f;
            levelThree.cameraMouseDragSensitivity = 5.9f;
            levelThree.cameraMinBounds = new Vector2(-74f, -41f);
            levelThree.cameraMaxBounds = new Vector2(74f, 41f);
            levelThree.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            levelThree.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            levelThree.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 16);
            levelThree.bossClearReward = new CurrencyAmount(CurrencyType.BossCore, 1);
            levelThree.challengeReward = new CurrencyAmount(CurrencyType.ChallengeToken, 1);
            levelThree.recommendedTactics = "A long experimental route for mixed enemy roles. Use anti-air coverage before Harpies appear, preserve burst damage for Witch Shamans, and avoid relying only on frontline units once Vampires and Zombies enter the stream.";

            var levelFourWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelFourWave.id = "wave_04_stress_test";
            levelFourWave.totalEnemyCount = 10000;
            levelFourWave.spawnInterval = 0.24f;
            levelFourWave.randomSpawnBurstMin = 48;
            levelFourWave.randomSpawnBurstMax = 92;
            levelFourWave.useEndpointSeeking = true;
            levelFourWave.enforceSpawnClearance = true;
            levelFourWave.entries = BuildLevelFourStressWaveEntries(runner, brute, shaman, vampire, harpy, zombie);

            var levelFour = ScriptableObject.CreateInstance<LevelDefinition>();
            levelFour.id = "level_04";
            levelFour.displayName = "Stress Field: Ten Thousand";
            levelFour.startingLives = 30;
            levelFour.wave = levelFourWave;
            levelFour.pathWaypoints = CreateLevelFourPath();
            levelFour.groundCenter = new Vector3(0f, -0.08f, 0f);
            levelFour.groundSize = new Vector3(230f, 0.1f, 132f);
            levelFour.environmentTilePackage = EnvironmentTilePackageId.Prehistoric;
            levelFour.useDataHordePrototype = true;
            levelFour.cameraPosition = new Vector3(0f, 78f, -62f);
            levelFour.cameraFieldOfView = 54f;
            levelFour.cameraMinHeight = 16f;
            levelFour.cameraMaxHeight = 180f;
            levelFour.cameraPanSpeed = 72f;
            levelFour.cameraMouseDragSensitivity = 8.5f;
            levelFour.cameraMinBounds = new Vector2(-112f, -63f);
            levelFour.cameraMaxBounds = new Vector2(112f, 63f);
            levelFour.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            levelFour.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            levelFour.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 28);
            levelFour.bossClearReward = new CurrencyAmount(CurrencyType.BossCore, 1);
            levelFour.challengeReward = new CurrencyAmount(CurrencyType.ChallengeToken, 1);
            levelFour.recommendedTactics = "Prototype stress map. The route is oversized and the wave is intentionally excessive so horde rendering, simulation ticking, and endpoint-seeking crowd movement can be profiled under pressure.";

            var levelFiveWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelFiveWave.id = "wave_05_100k_stress_test";
            levelFiveWave.totalEnemyCount = 100000;
            // Keep the 54 metre entrance physically full without returning to the
            // near-instant spawn that produced jelly and frame spikes. About 62
            // agents every 0.09s yields ~700 agents/second and ~145s for all 100K.
            levelFiveWave.spawnInterval = 0.155f;
            levelFiveWave.randomSpawnBurstMin = 55;
            levelFiveWave.randomSpawnBurstMax = 70;
            levelFiveWave.useEndpointSeeking = true;
            levelFiveWave.enforceSpawnClearance = true;
            levelFiveWave.roadHalfWidth = 26.5f;
            levelFiveWave.entries = new[]
            {
                new WaveEntry { enemy = runner, count = 100000 }
            };

            var levelFive = ScriptableObject.CreateInstance<LevelDefinition>();
            levelFive.id = "level_05";
            levelFive.displayName = "Stress Field: Hundred Thousand";
            levelFive.startingLives = 100000;
            levelFive.wave = levelFiveWave;
            levelFive.pathWaypoints = CreateLevelFivePath();
            levelFive.roadWidth = 54f;
            levelFive.groundCenter = new Vector3(0f, -0.08f, 0f);
            levelFive.groundSize = new Vector3(400f, 0.1f, 360f);
            levelFive.environmentTilePackage = EnvironmentTilePackageId.WeatheredRoad;
            levelFive.useDataHordePrototype = true;
            levelFive.cameraPosition = new Vector3(0f, 150f, -155f);
            levelFive.cameraFieldOfView = 55f;
            levelFive.cameraMinHeight = 20f;
            levelFive.cameraMaxHeight = 360f;
            levelFive.cameraPanSpeed = 96f;
            levelFive.cameraMouseDragSensitivity = 10f;
            levelFive.cameraMinBounds = new Vector2(-190f, -170f);
            levelFive.cameraMaxBounds = new Vector2(190f, 170f);
            levelFive.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            levelFive.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            levelFive.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 40);
            levelFive.bossClearReward = new CurrencyAmount(CurrencyType.BossCore, 1);
            levelFive.challengeReward = new CurrencyAmount(CurrencyType.ChallengeToken, 1);
            levelFive.recommendedTactics = "Dedicated 100,000-enemy GPU stress test. The very long map and rapid batched spawn expose simulation, grid, rendering, combat-command, and frame-stability limits.";

            var levelSixWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelSixWave.id = "wave_06_right_angle_collision_test";
            levelSixWave.totalEnemyCount = 10000;
            levelSixWave.spawnInterval = 0.009f;
            levelSixWave.randomSpawnBurstMin = 5;
            levelSixWave.randomSpawnBurstMax = 12;
            levelSixWave.useEndpointSeeking = true;
            levelSixWave.enforceSpawnClearance = true;
            levelSixWave.enemyScaleMultiplier = 0.567f;
            levelSixWave.enemyInertiaMultiplier = 1.7f;
            levelSixWave.enemyMassMultiplier = 1.3f;
            levelSixWave.collisionPressureMultiplier = 1.2f;
            levelSixWave.spawnPackingMultiplier = 1.5f;
            levelSixWave.randomEnemySpeedMin = 5f;
            levelSixWave.randomEnemySpeedMax = 7f;
            levelSixWave.colorEnemiesBySpeed = true;
            levelSixWave.roadHalfWidth = 3f;
            levelSixWave.entries = new[]
            {
                new WaveEntry { enemy = runner, count = 10000 }
            };

            var levelSix = ScriptableObject.CreateInstance<LevelDefinition>();
            levelSix.id = "level_06";
            levelSix.displayName = "Right-Angle Collision Lab";
            levelSix.startingLives = 12;
            levelSix.preventLifeLoss = true;
            levelSix.wave = levelSixWave;
            levelSix.pathWaypoints = CreateLevelSixPath();
            levelSix.pathWaypointWidths = CreateLevelSixPathWidths();
            levelSix.roadWidth = 3f;
            levelSix.useSquarePathCorners = true;
            levelSix.groundCenter = new Vector3(0f, -0.08f, 3f);
            levelSix.groundSize = new Vector3(160f, 0.1f, 72f);
            levelSix.environmentTilePackage = EnvironmentTilePackageId.WeatheredRoad;
            levelSix.useDataHordePrototype = true;
            levelSix.cameraPosition = new Vector3(0f, 54f, -43f);
            levelSix.cameraFieldOfView = 52f;
            levelSix.cameraMinHeight = 12f;
            levelSix.cameraMaxHeight = 130f;
            levelSix.cameraPanSpeed = 52f;
            levelSix.cameraMouseDragSensitivity = 6f;
            levelSix.cameraMinBounds = new Vector2(-78f, -34f);
            levelSix.cameraMaxBounds = new Vector2(78f, 38f);
            levelSix.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            levelSix.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            levelSix.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 20);
            levelSix.bossClearReward = new CurrencyAmount(CurrencyType.BossCore, 1);
            levelSix.challengeReward = new CurrencyAmount(CurrencyType.ChallengeToken, 1);
            levelSix.recommendedTactics = "Collision lab: 10,000 high-inertia enemies enter through a six-metre opening before traversing the traced three-metre orthogonal route.";

            var levelSevenWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelSevenWave.id = "wave_07_explosion_physics_test";
            levelSevenWave.totalEnemyCount = 10000;
            // Keep this laboratory's bodies identical to the Level 2 queue.
            // The arena shape and Shockwave Bomb are the only test variables, so
            // its blast response can be compared directly with the choke point.
            levelSevenWave.spawnInterval = 0.009f;
            levelSevenWave.randomSpawnBurstMin = 5;
            levelSevenWave.randomSpawnBurstMax = 12;
            levelSevenWave.enforceSpawnClearance = true;
            levelSevenWave.stationaryArena = true;
            levelSevenWave.randomArenaMovement = false;
            levelSevenWave.centerSeekingArena = true;
            levelSevenWave.enemyScaleMultiplier = 0.7f;
            levelSevenWave.enemyInertiaMultiplier = 1.35f;
            levelSevenWave.enemyMassMultiplier = 1.3f;
            levelSevenWave.collisionPressureMultiplier = 1.2f;
            levelSevenWave.spawnPackingMultiplier = 1f;
            levelSevenWave.enemySpeedMultiplier = 1f;
            levelSevenWave.openingSpeedDuration = 0f;
            levelSevenWave.openingSpeedMultiplier = 1f;
            levelSevenWave.roadHalfWidth = 60f;
            levelSevenWave.entries = new[]
            {
                new WaveEntry { enemy = runner, count = 10000 }
            };

            var levelSeven = ScriptableObject.CreateInstance<LevelDefinition>();
            levelSeven.id = "level_07";
            levelSeven.displayName = "Explosion Physics Lab";
            levelSeven.startingLives = 10000;
            levelSeven.preventLifeLoss = true;
            levelSeven.wave = levelSevenWave;
            levelSeven.pathWaypoints = CreateLevelSevenPath();
            levelSeven.pathWaypointWidths = new[] { 120f, 120f };
            levelSeven.roadWidth = 120f;
            levelSeven.useCircularArena = true;
            levelSeven.arenaRadius = 60f;
            levelSeven.groundCenter = new Vector3(0f, -0.08f, 0f);
            levelSeven.groundSize = new Vector3(140f, 0.1f, 140f);
            levelSeven.environmentTilePackage = EnvironmentTilePackageId.Arena;
            levelSeven.useDataHordePrototype = true;
            levelSeven.cameraPosition = new Vector3(0f, 104f, -80f);
            levelSeven.cameraFieldOfView = 48f;
            levelSeven.cameraMinHeight = 24f;
            levelSeven.cameraMaxHeight = 270f;
            levelSeven.cameraPanSpeed = 108f;
            levelSeven.cameraMouseDragSensitivity = 6f;
            levelSeven.cameraMinBounds = new Vector2(-70f, -70f);
            levelSeven.cameraMaxBounds = new Vector2(70f, 70f);
            levelSeven.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            levelSeven.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            levelSeven.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 20);
            levelSeven.bossClearReward = new CurrencyAmount(CurrencyType.BossCore, 1);
            levelSeven.challengeReward = new CurrencyAmount(CurrencyType.ChallengeToken, 1);
            levelSeven.recommendedTactics = "Explosion lab: the same 10,000 light runners and crowd envelope as Level 2 converge from the edge of a sealed circular arena. Use the Shockwave Bomb to inspect radial knockback, impulse transfer, wall pressure, and crowd recovery.";

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
                    description = "Active weapon damage.",
                    radialPosition = new Vector2(150f, 18f),
                    maxRanks = 10,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponDamagePercent, value = 2f } }
                },
                new SkillNodeDefinition
                {
                    id = "volley_pierce_01",
                    displayName = "Arrow Rain",
                    description = "Active weapon targets.",
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
                    description = "Active weapon cooldown.",
                    radialPosition = new Vector2(285f, 68f),
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
                    radialPosition = new Vector2(424f, 104f),
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
                    description = "Active weapon radius.",
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
                    description = "Base lives.",
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
                    description = "Level completion essence.",
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
                    id = "archer_projectile_speed_01",
                    displayName = "Swift Shafts",
                    description = "Archer Tower projectile speed.",
                    radialPosition = new Vector2(-150f, 188f),
                    maxRanks = 5,
                    prerequisiteNodeIds = new[] { "archer_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerProjectileSpeedPercent, targetId = "archer", value = 12f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_range_01",
                    displayName = "High Perches",
                    description = "Archer Tower range.",
                    radialPosition = new Vector2(-150f, 318f),
                    maxRanks = 5,
                    prerequisiteNodeIds = new[] { "archer_projectile_speed_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerRangeFlat, targetId = "archer", value = 0.35f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_limit_01",
                    displayName = "Archer Barracks",
                    description = "Archer Tower placement limit.",
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
                    description = "Archer Tower damage.",
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
                    description = "Archer Tower double-shot chance.",
                    radialPosition = new Vector2(-420f, 154f),
                    maxRanks = 5,
                    prerequisiteNodeIds = new[] { "archer_damage_01" },
                    costGrowthMultiplier = 2f,
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDoubleShotChancePercent, targetId = "archer", value = 6f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_flat_damage_01",
                    displayName = "Bodkin Heads",
                    description = "Archer Tower damage.",
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
                    description = "Archer Tower fire rate.",
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
                    description = "Archer Tower fire rate.",
                    radialPosition = new Vector2(-560f, 34f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "archer_speed_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 8) },
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
                    description = "Ballista placement limit.",
                    radialPosition = new Vector2(-306f, -112f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "ballista_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 4) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "ballista", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_damage_01",
                    displayName = "Heavy Bolts",
                    description = "Ballista damage.",
                    radialPosition = new Vector2(-298f, -190f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "ballista_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamagePercent, targetId = "ballista", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_pierce_01",
                    displayName = "Skewering Bolts",
                    description = "Ballista pierce.",
                    radialPosition = new Vector2(-452f, -190f),
                    maxRanks = 4,
                    prerequisiteNodeIds = new[] { "ballista_damage_01" },
                    costGrowthMultiplier = 2.4f,
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 12) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerPierceFlat, targetId = "ballista", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "ballista_speed_01",
                    displayName = "Winch Drills",
                    description = "Ballista fire rate.",
                    radialPosition = new Vector2(-326f, -292f),
                    maxRanks = 10,
                    prerequisiteNodeIds = new[] { "ballista_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireRatePercent, targetId = "ballista", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "projectile_aim_assist_01",
                    displayName = "Guiding Fletches",
                    description = "Archer Tower aim assist.",
                    radialPosition = new Vector2(-700f, 112f),
                    maxRanks = 5,
                    prerequisiteNodeIds = new[] { "archer_flat_damage_01", "archer_flat_speed_01" },
                    costGrowthMultiplier = 2.5f,
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 8) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerAimAssistPercent, targetId = "archer", value = 20f } }
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
                    description = "Bell Tower placement limit.",
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
                    description = "Bell Tower slow.",
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
                    description = "Bell Tower slow capacity.",
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
                    description = "Bell Tower range.",
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
                    description = "Catapult placement limit.",
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
                    description = "Catapult damage.",
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
                    description = "Catapult fire rate.",
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
                    description = "Catapult burn damage.",
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
                    description = "Catapult burn rate.",
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
                    description = "Catapult burn stacks.",
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
                    description = "Catapult burn duration.",
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
                    description = "Timber Barrier health.",
                    radialPosition = new Vector2(18f, -356f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "barrier_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerHealthFlat, targetId = "barrier", value = 15f } }
                },
                new SkillNodeDefinition
                {
                    id = "barrier_thorns_01",
                    displayName = "Iron Spikes",
                    description = "Timber Barrier thorns damage.",
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
                    description = "Timber Barrier placement limit.",
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
                    description = "Knight Barracks troop slots.",
                    radialPosition = new Vector2(466f, 326f),
                    maxRanks = 4,
                    prerequisiteNodeIds = new[] { "knight_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitCapacityFlat, targetId = "knight_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "knight_barracks_limit_01",
                    displayName = "Additional Barracks",
                    description = "Knight Barracks placement limit.",
                    radialPosition = new Vector2(310f, 178f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "knight_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.ChallengeToken, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "knight_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "barracks_damage_01",
                    displayName = "Knight Steel",
                    description = "Knight damage.",
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
                    description = "Knight health.",
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
                    description = "Knight respawn time.",
                    radialPosition = new Vector2(618f, 202f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "barracks_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksRespawnCooldownPercent, targetId = "knight_barracks", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "knight_quarters_02",
                    displayName = "Veteran Quarters",
                    description = "Knight troop slots and health.",
                    radialPosition = new Vector2(770f, 326f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "barracks_health_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 8) },
                    effects = new[]
                    {
                        new UpgradeEffect { type = UpgradeEffectType.BarracksUnitCapacityFlat, targetId = "knight_barracks", value = 1f },
                        new UpgradeEffect { type = UpgradeEffectType.BarracksUnitHealthPercent, targetId = "knight_barracks", value = 10f }
                    }
                },
                new SkillNodeDefinition
                {
                    id = "knight_drills_02",
                    displayName = "Veteran Muster",
                    description = "Knight damage and respawn time.",
                    radialPosition = new Vector2(770f, 202f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "barracks_respawn_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 8) },
                    effects = new[]
                    {
                        new UpgradeEffect { type = UpgradeEffectType.BarracksUnitDamagePercent, targetId = "knight_barracks", value = 10f },
                        new UpgradeEffect { type = UpgradeEffectType.BarracksRespawnCooldownPercent, targetId = "knight_barracks", value = 6f }
                    }
                },
                new SkillNodeDefinition
                {
                    id = "archer_barracks_unlock",
                    displayName = "Archer Post",
                    description = "Unlock barracks that respawn anti-air archers.",
                    radialPosition = new Vector2(330f, 438f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "knight_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.PerfectSigil, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "archer_barracks", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "archer_post_capacity_01",
                    displayName = "Arrow Racks",
                    description = "Archer Post troop slots.",
                    radialPosition = new Vector2(510f, 520f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "archer_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitCapacityFlat, targetId = "archer_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_barracks_limit_01",
                    displayName = "Additional Archer Posts",
                    description = "Archer Post placement limit.",
                    radialPosition = new Vector2(330f, 552f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "archer_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.ChallengeToken, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "archer_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_post_damage_01",
                    displayName = "War Arrows",
                    description = "Archer troop damage.",
                    radialPosition = new Vector2(510f, 630f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "archer_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitDamagePercent, targetId = "archer_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_post_health_01",
                    displayName = "Leather Jacks",
                    description = "Archer troop health.",
                    radialPosition = new Vector2(670f, 550f),
                    maxRanks = 6,
                    prerequisiteNodeIds = new[] { "archer_post_capacity_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 2) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitHealthPercent, targetId = "archer_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "archer_post_respawn_01",
                    displayName = "Ready Quivers",
                    description = "Archer Post respawn time.",
                    radialPosition = new Vector2(670f, 660f),
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
                    radialPosition = new Vector2(430f, 438f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "knight_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.PerfectSigil, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "paladin_barracks", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "paladin_chapter_capacity_01",
                    displayName = "Chapter Cells",
                    description = "Paladin Chapter troop slots.",
                    radialPosition = new Vector2(610f, 420f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "paladin_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitCapacityFlat, targetId = "paladin_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "paladin_barracks_limit_01",
                    displayName = "Additional Chapters",
                    description = "Paladin Chapter placement limit.",
                    radialPosition = new Vector2(430f, 326f),
                    maxRanks = 3,
                    prerequisiteNodeIds = new[] { "paladin_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.ChallengeToken, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "paladin_barracks", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "paladin_chapter_damage_01",
                    displayName = "Blessed Maces",
                    description = "Paladin damage.",
                    radialPosition = new Vector2(610f, 330f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "paladin_barracks_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 4) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitDamagePercent, targetId = "paladin_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "paladin_chapter_health_01",
                    displayName = "Plate Vows",
                    description = "Paladin health.",
                    radialPosition = new Vector2(770f, 420f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "paladin_chapter_capacity_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 4) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksUnitHealthPercent, targetId = "paladin_barracks", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "paladin_chapter_respawn_01",
                    displayName = "Chapter Bells",
                    description = "Paladin Chapter respawn time.",
                    radialPosition = new Vector2(770f, 330f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "paladin_chapter_damage_01" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 5) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BarracksRespawnCooldownPercent, targetId = "paladin_barracks", value = 4f } }
                },
                // Prehistoric towers. Their place in the tree is provisional until the era trees exist.
                new SkillNodeDefinition
                {
                    id = "spear_thrower_unlock",
                    displayName = "Spear Throwers",
                    description = "Unlock the Spear Throwers, a slow long-range tower whose spears fly on through the goblins behind.",
                    radialPosition = new Vector2(-440f, 380f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "spear_thrower", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "spear_thrower_damage_01",
                    displayName = "Flint Points",
                    description = "Spear Throwers damage.",
                    radialPosition = new Vector2(-600f, 460f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "spear_thrower_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamagePercent, targetId = "spear_thrower", value = 4f } }
                },
                new SkillNodeDefinition
                {
                    id = "spear_thrower_pierce_01",
                    displayName = "Long Shafts",
                    description = "Spear Throwers pierce.",
                    radialPosition = new Vector2(-480f, 580f),
                    maxRanks = 4,
                    prerequisiteNodeIds = new[] { "spear_thrower_damage_01" },
                    costGrowthMultiplier = 2.4f,
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 12) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerPierceFlat, targetId = "spear_thrower", value = 1f } }
                },
                new SkillNodeDefinition
                {
                    id = "mammoth_unlock",
                    displayName = "Tamed Mammoth",
                    description = "Unlock the Tamed Mammoth, which charges along the road and scatters the horde.",
                    radialPosition = new Vector2(-600f, -300f),
                    maxRanks = 1,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.UnlockTower, targetId = "mammoth", value = 1f } },
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "mammoth_speed_01",
                    displayName = "Restless Herd",
                    description = "Tamed Mammoth charges more often.",
                    radialPosition = new Vector2(-720f, -180f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "mammoth_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 4) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerFireRatePercent, targetId = "mammoth", value = 5f } }
                },
                new SkillNodeDefinition
                {
                    id = "mammoth_damage_01",
                    displayName = "Heavy Tread",
                    description = "Tamed Mammoth damage.",
                    radialPosition = new Vector2(-560f, -460f),
                    maxRanks = 8,
                    prerequisiteNodeIds = new[] { "mammoth_unlock" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 3) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerDamagePercent, targetId = "mammoth", value = 5f } }
                }
            };

            return new SampleContent
            {
                Level = level,
                Levels = new[] { level, levelTwo, levelTwoPointOne, levelThree, levelFour, levelFive, levelSix, levelSeven },
                SkillTree = tree,
                Towers = new[] { archer, ballista, bell, catapult, barrier, knightBarracks, archerBarracks, paladinBarracks, spearThrower, mammoth }
            };
        }

        private static Vector3[] CreateLevelOnePath()
        {
            return new[]
            {
                new Vector3(-32f, 0f, 9.5f),
                new Vector3(-20.5f, 0f, 9.5f),
                new Vector3(-13.2f, 0f, 9.2f),
                new Vector3(-13.2f, 0f, -7.8f),
                new Vector3(3.8f, 0f, -7.8f),
                new Vector3(3.8f, 0f, 10.2f),
                new Vector3(15.6f, 0f, 10.2f),
                new Vector3(26.5f, 0f, 10.2f)
            };
        }

        private static Vector3[] CreateLevelTwoPath()
        {
            return new[]
            {
                new Vector3(-56f, 0f, 0f),
                new Vector3(-44f, 0f, 0f),
                new Vector3(-34f, 0f, 0f),
                new Vector3(-22f, 0f, 0f),
                new Vector3(-8f, 0f, 0f),
                new Vector3(8f, 0f, 0f),
                new Vector3(22f, 0f, 0f),
                new Vector3(34f, 0f, 0f),
                new Vector3(44f, 0f, 0f),
                new Vector3(56f, 0f, 0f)
            };
        }

        private static Vector3[] CreateLevelTwoUpperPath()
        {
            return new[]
            {
                new Vector3(-56f, 0f, 0f),
                new Vector3(-44f, 0f, 0f),
                new Vector3(-34f, 0f, 12f),
                new Vector3(-22f, 0f, 19f),
                new Vector3(-8f, 0f, 22f),
                new Vector3(8f, 0f, 22f),
                new Vector3(22f, 0f, 19f),
                new Vector3(34f, 0f, 12f),
                new Vector3(44f, 0f, 0f),
                new Vector3(56f, 0f, 0f)
            };
        }

        private static Vector3[] CreateLevelTwoLowerPath()
        {
            return new[]
            {
                new Vector3(-56f, 0f, 0f),
                new Vector3(-44f, 0f, 0f),
                new Vector3(-34f, 0f, -12f),
                new Vector3(-22f, 0f, -19f),
                new Vector3(-8f, 0f, -22f),
                new Vector3(8f, 0f, -22f),
                new Vector3(22f, 0f, -19f),
                new Vector3(34f, 0f, -12f),
                new Vector3(44f, 0f, 0f),
                new Vector3(56f, 0f, 0f)
            };
        }

        private static float[] CreateLevelTwoPathWidths()
        {
            return new[]
            {
                30f, 18f, 3f, 3f, 3f, 3f, 3f, 3f, 3f, 3f
            };
        }

        private static Vector3[] CreateLevelThreePath()
        {
            return new[]
            {
                new Vector3(-62f, 0f, 17f),
                new Vector3(-48f, 0f, 17f),
                new Vector3(-36f, 0f, 6f),
                new Vector3(-24f, 0f, -14f),
                new Vector3(-7f, 0f, -18f),
                new Vector3(8f, 0f, -7f),
                new Vector3(-2f, 0f, 12f),
                new Vector3(17f, 0f, 23f),
                new Vector3(38f, 0f, 19f),
                new Vector3(48f, 0f, 2f),
                new Vector3(36f, 0f, -20f),
                new Vector3(55f, 0f, -24f),
                new Vector3(66f, 0f, -10f)
            };
        }

        private static Vector3[] CreateLevelFourPath()
        {
            return new[]
            {
                new Vector3(-104f, 0f, 30f),
                new Vector3(-82f, 0f, 30f),
                new Vector3(-64f, 0f, 20f),
                new Vector3(-52f, 0f, 2f),
                new Vector3(-66f, 0f, -22f),
                new Vector3(-42f, 0f, -42f),
                new Vector3(-10f, 0f, -48f),
                new Vector3(18f, 0f, -34f),
                new Vector3(1f, 0f, -10f),
                new Vector3(-18f, 0f, 8f),
                new Vector3(2f, 0f, 31f),
                new Vector3(36f, 0f, 44f),
                new Vector3(70f, 0f, 36f),
                new Vector3(88f, 0f, 14f),
                new Vector3(70f, 0f, -8f),
                new Vector3(42f, 0f, -12f),
                new Vector3(54f, 0f, -38f),
                new Vector3(86f, 0f, -42f),
                new Vector3(106f, 0f, -22f)
            };
        }

        private static Vector3[] CreateLevelFivePath()
        {
            return new[]
            {
                new Vector3(-180f, 0f, 150f),
                new Vector3(-110f, 0f, 150f),
                new Vector3(0f, 0f, 145f),
                new Vector3(110f, 0f, 150f),
                new Vector3(180f, 0f, 125f),
                new Vector3(180f, 0f, 78f),
                new Vector3(110f, 0f, 50f),
                new Vector3(0f, 0f, 55f),
                new Vector3(-110f, 0f, 50f),
                new Vector3(-180f, 0f, 25f),
                new Vector3(-180f, 0f, -28f),
                new Vector3(-110f, 0f, -50f),
                new Vector3(0f, 0f, -45f),
                new Vector3(110f, 0f, -50f),
                new Vector3(180f, 0f, -78f),
                new Vector3(180f, 0f, -128f),
                new Vector3(110f, 0f, -150f),
                new Vector3(0f, 0f, -145f),
                new Vector3(-110f, 0f, -150f),
                new Vector3(-180f, 0f, -150f)
            };
        }

        private static Vector3[] CreateLevelSixPath()
        {
            return new[]
            {
                new Vector3(-72f, 0f, 30f),
                new Vector3(-36.5f, 0f, 30f),
                new Vector3(-36f, 0f, 30f),
                new Vector3(-36f, 0f, 5f),
                new Vector3(12f, 0f, 5f),
                new Vector3(12f, 0f, -11f),
                new Vector3(-18f, 0f, -11f),
                new Vector3(-18f, 0f, -30f),
                new Vector3(72f, 0f, -30f)
            };
        }

        private static float[] CreateLevelSixPathWidths()
        {
            var widths = new float[9];
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = i < 2 ? 6f : 3f;
            }

            return widths;
        }

        private static Vector3[] CreateLevelSevenPath()
        {
            return new[]
            {
                Vector3.zero,
                Vector3.zero
            };
        }

        private static Vector3[] CreateLevelTwoThreeExitUpperPath()
        {
            return new[]
            {
                new Vector3(-56f, 0f, 0f),
                new Vector3(-44f, 0f, 0f),
                new Vector3(-34f, 0f, 12f),
                new Vector3(-22f, 0f, 19f),
                new Vector3(-8f, 0f, 22f),
                new Vector3(8f, 0f, 22f),
                new Vector3(22f, 0f, 19f),
                new Vector3(34f, 0f, 12f),
                new Vector3(44f, 0f, 12f),
                new Vector3(56f, 0f, 12f)
            };
        }

        private static Vector3[] CreateLevelTwoThreeExitLowerPath()
        {
            return new[]
            {
                new Vector3(-56f, 0f, 0f),
                new Vector3(-44f, 0f, 0f),
                new Vector3(-34f, 0f, -12f),
                new Vector3(-22f, 0f, -19f),
                new Vector3(-8f, 0f, -22f),
                new Vector3(8f, 0f, -22f),
                new Vector3(22f, 0f, -19f),
                new Vector3(34f, 0f, -12f),
                new Vector3(44f, 0f, -12f),
                new Vector3(56f, 0f, -12f)
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
            var remainingRunners = 170;
            var remainingBrutes = 45;
            while (remainingRunners > 0 || remainingBrutes > 0)
            {
                if (remainingRunners > 0)
                {
                    var runnerCount = Mathf.Min(7, remainingRunners);
                    entries.Add(new WaveEntry { enemy = runner, count = runnerCount });
                    remainingRunners -= runnerCount;
                }

                if (remainingBrutes > 0)
                {
                    var bruteCount = Mathf.Min(2, remainingBrutes);
                    entries.Add(new WaveEntry { enemy = brute, count = bruteCount });
                    remainingBrutes -= bruteCount;
                }
            }

            return entries.ToArray();
        }

        private static WaveEntry[] BuildLevelThreeWaveEntries(
            EnemyDefinition runner,
            EnemyDefinition brute,
            EnemyDefinition shaman,
            EnemyDefinition vampire,
            EnemyDefinition harpy,
            EnemyDefinition zombie)
        {
            var entries = new List<WaveEntry>();
            AddWaveEntries(entries, runner, 360, 18);
            AddWaveEntries(entries, zombie, 230, 11);
            AddWaveEntries(entries, runner, 270, 17);
            AddWaveEntries(entries, brute, 120, 5);
            AddWaveEntries(entries, shaman, 45, 2);
            AddWaveEntries(entries, runner, 330, 20);
            AddWaveEntries(entries, harpy, 135, 7);
            AddWaveEntries(entries, zombie, 210, 10);
            AddWaveEntries(entries, vampire, 55, 2);
            AddWaveEntries(entries, brute, 135, 6);
            AddWaveEntries(entries, runner, 150, 18);
            return entries.ToArray();
        }

        private static WaveEntry[] BuildLevelFourStressWaveEntries(
            EnemyDefinition runner,
            EnemyDefinition brute,
            EnemyDefinition shaman,
            EnemyDefinition vampire,
            EnemyDefinition harpy,
            EnemyDefinition zombie)
        {
            var entries = new List<WaveEntry>();
            AddWaveEntries(entries, runner, 10000, 50);
            return entries.ToArray();
        }

        private static void AddWaveEntries(List<WaveEntry> entries, EnemyDefinition enemy, int totalCount, int chunkSize)
        {
            var remaining = totalCount;
            while (remaining > 0)
            {
                var count = Mathf.Min(Mathf.Max(1, chunkSize), remaining);
                entries.Add(new WaveEntry { enemy = enemy, count = count });
                remaining -= count;
            }
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
