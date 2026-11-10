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
        // The former Level 1 wave (170 Goblin Runners and 45 Orc Brutes), for tests on the Level 1
        // road now that the level itself has no enemies.
        public WaveDefinition LevelOneTestWave { get; private set; }
        // Not in any level yet: the fourth prehistoric map is still being designed.
        public EnemyDefinition PrehistoricBoss { get; private set; }
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
            var shaman = CreateEnemy("shaman", "Shaman", EnemyRole.Support,
                "Walks with the horde and heals the goblins around it.",
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
            // The prehistoric boss, on the era's last map (docs/Handoff.md): big, heavy, slow, with a
            // lot of health and no special skills. Nothing pushes it; cavemen and the mammoth only
            // slow it. Its look and race are not decided yet. TEMPORARY numbers until balancing.
            var prehistoricBoss = CreateEnemy("prehistoric_boss", "Prehistoric Boss", EnemyRole.Boss,
                "A huge, slow brute leading the horde. Nothing can push it, and the cavemen only slow it down.",
                "Weak to heavy single hits: Spear Throwers are the main damage against it.",
                1500f, 1.5f, 10, 50, PlaceholderPalette.Enemy, 1.1f);
            prehistoricBoss.mass = 20f;
            prehistoricBoss.cannotBePushed = true;
            prehistoricBoss.passesBlockers = true;
            prehistoricBoss.attackDamage = 8f;
            prehistoricBoss.attackInterval = 1.2f;
            // The prehistoric era has only the Runner, the Brute and the Shaman; these three come in
            // the medieval era (docs/Handoff.md, 2026-11-08).
            vampire.eraIndex = (int)EraId.Medieval;
            harpy.eraIndex = (int)EraId.Medieval;
            zombie.eraIndex = (int)EraId.Medieval;
            runner.mass = 1f;
            // The Brute hits a little harder than the Runner; its strength is that it lasts
            // (docs/Handoff.md). TEMPORARY value until balancing.
            brute.alliedDamageMultiplier = 1.3f;
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
            // User, 2026-11-08: up to 3 hunters on a rock outcrop; they aim at the heaviest goblin
            // first (the boss, brutes). Knockback size is TEMPORARY.
            spearThrower.throwsSpears = true;
            spearThrower.crewSize = 3;
            spearThrower.defaultTargetingMode = TowerTargetingMode.Heaviest;
            spearThrower.knockbackDistance = 0.35f;
            spearThrower.nextEvolution = ballista;
            // Fast, regular throws with small damage: they finish off the goblins that slipped past
            // the mammoth (docs/Handoff.md). TEMPORARY numbers until balancing.
            var slingers = CreateTower("slingers", "Slingers", TowerRole.ArcherLine,
                "Hunters whirl slings and pelt the road with stones. Fast, steady throws with small hits; they finish off goblins that slip past.",
                "Weak against tough enemies: each stone hits lightly.",
                (int)EraId.Prehistoric, 1, 6.5f, 3f, 0.4f, 16f, PlaceholderPalette.Tower);
            slingers.nextEvolution = archer;
            // Cavemen with clubs come out of a cave beside the road and hold the goblins back; they
            // hit lightly, holding is their job. One at first, up to five with upgrades; one shared
            // timer brings the next one only while fewer than the maximum are out (docs/Handoff.md,
            // user 2026-11-08). TEMPORARY numbers until balancing.
            var cave = CreateTower("cave", "Cave", TowerRole.BarracksLine,
                "A cave beside the road. Cavemen with clubs come out and hold the goblins back on the road.",
                "Weak against thick hordes and heavy enemies: the cavemen hold, but hit lightly.",
                (int)EraId.Prehistoric, 1, 3.2f, 0f, 1f, 0f, PlaceholderPalette.Tower);
            cave.behavior = TowerBehavior.Barracks;
            cave.barracksUnitType = AlliedUnitType.Caveman;
            cave.barracksCapacity = 1;
            cave.barracksRespawnSeconds = 8f;
            cave.alliedUnitHealth = 26f;
            cave.alliedUnitDamage = 2f;
            cave.alliedUnitBlockCapacity = 3f;
            cave.alliedUnitMoveSpeed = 3f;
            cave.alliedUnitAggroRange = 6f;
            var mammoth = CreateTower("mammoth", "Tamed Mammoth", TowerRole.ControlLine,
                "A mammoth waits in its pen beside the road. When goblins come near, it charges along the road into them, trampling and throwing the horde aside until the crowd slows it to a halt. Then it fights where it stands until it falls, and a new mammoth takes its place.",
                "Weak against thick hordes: they stop the charge early, and the road is open until the next mammoth is ready.",
                (int)EraId.Prehistoric, 1, 8f, 9f, 7f, 0f, PlaceholderPalette.Tower);
            mammoth.behavior = TowerBehavior.Charge;
            // The trample throws goblins into the air: in a jammed road they fly over the crowd
            // instead of having nowhere to go (user, 2026-11-07).
            mammoth.knockbackDistance = 1.0f;
            mammoth.knockbackLaunch = 0.8f;
            // The longest charge, on an empty road; a crowd stops it much sooner (user, 2026-11-07).
            mammoth.chargeDistance = 24f;
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

            // Level 1 has no enemies until it is redesigned (user, 2026-11-08): starting it wins at
            // once. The old wave above stays for the tests on the Level 1 road (LevelOneTestWave).
            var levelOneWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelOneWave.id = "wave_01_empty";
            levelOneWave.totalEnemyCount = 0;
            levelOneWave.spawnInterval = wave.spawnInterval;
            levelOneWave.entries = System.Array.Empty<WaveEntry>();

            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.id = "level_01";
            level.displayName = "Broken Green Pass";
            level.startingLives = 10;
            level.wave = levelOneWave;
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
            level.recommendedTactics = "No enemies yet: this level is being redesigned.";

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

            // The prehistoric tree (user, 2026-11-08). The rock sits at the centre with its own
            // stats and specials just above and below it; the four towers branch out two to a
            // side (Slingers and Cave left, Spear Throwers and Mammoth right). Every tower has three
            // Kill Essence stats and three specials bought with Challenge Sigils (2, 3 and 4: about
            // 3 on average), each with two Kill Essence upgrades of its own. Each node shows a pixel
            // icon (SkillTreePixelIcons) instead of a name. The positions come from a layout script
            // (seed 948) that grows each branch outward with some randomness and rejects any square
            // sitting on a line or lines crossing. ALL PRICES AND VALUES ARE TEMPORARY.
            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            tree.id = "core_tree";
            tree.nodes = new[]
            {
                new SkillNodeDefinition
                {
                    // The rock replaced the Volley of Arrows (user, 2026-11-08); the volley ids stay so
                    // saved progress keeps its ranks.
                    id = "volley_core",
                    displayName = "Rolling Rock",
                    description = "Your starting active weapon: drop a rock on the road, then let it roll the way you drag. This is the center of the first tree.",
                    iconKey = "rock",
                    radialPosition = Vector2.zero,
                    maxRanks = 1,
                    startsUnlocked = true,
                    isMajorUnlock = true
                },
                new SkillNodeDefinition
                {
                    id = "slingers_unlock",
                    displayName = "Slingers",
                    description = "Hunters with slings: fast, light hits that finish off goblins. Yours from the start.",
                    iconKey = "slingers",
                    radialPosition = new Vector2(-499f, 126f),
                    maxRanks = 1,
                    startsUnlocked = true,
                    isMajorUnlock = true,
                    prerequisiteNodeIds = new[] { "volley_core" },
                    effects = new[] { TreeEffect(UpgradeEffectType.UnlockTower, 1f, "slingers") }
                },
                TreeNode("slingers_damage", "Heavier Stones", "Slinger stones hit harder.", "damage", new Vector2(-481f, -18f), 10, CurrencyType.KillEssence, 2, 1.2f, "slingers_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerDamagePercent, 10f, "slingers")),
                TreeNode("slingers_speed", "Quick Slings", "Slingers throw faster.", "speed", new Vector2(-667f, 48f), 8, CurrencyType.KillEssence, 3, 1.2f, "slingers_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerFireRatePercent, 6f, "slingers")),
                TreeNode("slingers_range", "Long Cords", "Slingers reach further.", "range", new Vector2(-571f, 13f), 5, CurrencyType.KillEssence, 4, 1.25f, "slingers_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerRangeFlat, 0.4f, "slingers")),
                TreeNode("slingers_stun", "Stunning Stones", "Special: a stone sometimes stuns the goblin it hits (15%, 0.6 s).", "stun", new Vector2(-674f, 153f), 1, CurrencyType.ChallengeToken, 2, 1.2f, "slingers_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerStunChancePercent, 15f, "slingers"), TreeEffect(UpgradeEffectType.TowerStunSecondsFlat, 0.6f, "slingers")),
                TreeNode("slingers_stun_chance", "Sure Stun", "Stunning Stones: +5% chance to stun.", "stun|up", new Vector2(-777f, 120f), 5, CurrencyType.KillEssence, 6, 1.25f, "slingers_stun", false,
                    TreeEffect(UpgradeEffectType.TowerStunChancePercent, 5f, "slingers")),
                TreeNode("slingers_stun_time", "Long Daze", "Stunning Stones: the stun lasts 0.15 s longer.", "stun|time", new Vector2(-762f, 29f), 5, CurrencyType.KillEssence, 6, 1.25f, "slingers_stun", false,
                    TreeEffect(UpgradeEffectType.TowerStunSecondsFlat, 0.15f, "slingers")),
                TreeNode("slingers_ricochet", "Ricochet", "Special: the stone bounces on to one more goblin for 60% of the hit.", "ricochet", new Vector2(-514f, 296f), 1, CurrencyType.ChallengeToken, 3, 1.2f, "slingers_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerRicochetBouncesFlat, 1f, "slingers"), TreeEffect(UpgradeEffectType.TowerRicochetDamagePercent, 60f, "slingers")),
                TreeNode("slingers_ricochet_bounces", "Skipping Stones", "Ricochet: one more bounce.", "ricochet|plus", new Vector2(-418f, 358f), 3, CurrencyType.KillEssence, 10, 1.5f, "slingers_ricochet", false,
                    TreeEffect(UpgradeEffectType.TowerRicochetBouncesFlat, 1f, "slingers")),
                TreeNode("slingers_ricochet_damage", "Hard Bounce", "Ricochet: each bounce deals 10% more of the hit.", "ricochet|up", new Vector2(-605f, 357f), 5, CurrencyType.KillEssence, 6, 1.25f, "slingers_ricochet", false,
                    TreeEffect(UpgradeEffectType.TowerRicochetDamagePercent, 10f, "slingers")),
                TreeNode("slingers_shrapnel", "Shrapnel", "Special: the stone bursts into pebbles that hit 3 goblins in a cone behind the target for 40% of the hit.", "shrapnel", new Vector2(-583f, 215f), 1, CurrencyType.ChallengeToken, 4, 1.2f, "slingers_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerShrapnelTargetsFlat, 3f, "slingers"), TreeEffect(UpgradeEffectType.TowerShrapnelDamagePercent, 40f, "slingers")),
                TreeNode("slingers_shrapnel_count", "More Pebbles", "Shrapnel: hits one more goblin.", "shrapnel|plus", new Vector2(-680f, 276f), 5, CurrencyType.KillEssence, 6, 1.25f, "slingers_shrapnel", false,
                    TreeEffect(UpgradeEffectType.TowerShrapnelTargetsFlat, 1f, "slingers")),
                TreeNode("slingers_shrapnel_damage", "Sharp Pebbles", "Shrapnel: the pebbles deal 10% more of the hit.", "shrapnel|up", new Vector2(-756f, 229f), 5, CurrencyType.KillEssence, 6, 1.25f, "slingers_shrapnel", false,
                    TreeEffect(UpgradeEffectType.TowerShrapnelDamagePercent, 10f, "slingers")),
                TreeNode("spear_unlock", "Spear Throwers", "Hunters hurl long spears: slow, heavy hits, the main damage against the boss. Costs a Victory Sigil.", "spear", new Vector2(469f, -107f), 1, CurrencyType.VictorySigil, 1, 1.2f, "volley_core", true,
                    TreeEffect(UpgradeEffectType.UnlockTower, 1f, "spear_thrower")),
                TreeNode("spear_damage", "Flint Heads", "Spears hit harder.", "damage", new Vector2(290f, -153f), 10, CurrencyType.KillEssence, 2, 1.2f, "spear_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerDamagePercent, 10f, "spear_thrower")),
                TreeNode("spear_speed", "Practised Throw", "Spear Throwers throw faster.", "speed", new Vector2(562f, 0f), 8, CurrencyType.KillEssence, 3, 1.2f, "spear_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerFireRatePercent, 6f, "spear_thrower")),
                TreeNode("spear_range", "Long Atlatls", "Spears reach further.", "range", new Vector2(406f, 34f), 5, CurrencyType.KillEssence, 4, 1.25f, "spear_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerRangeFlat, 0.5f, "spear_thrower")),
                TreeNode("spear_poison", "Poisoned Tips", "Special: every goblin a spear hits is poisoned, 2 damage a second for 3 s.", "poison", new Vector2(596f, -187f), 1, CurrencyType.ChallengeToken, 2, 1.2f, "spear_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerPoisonDamagePerSecondFlat, 2f, "spear_thrower"), TreeEffect(UpgradeEffectType.TowerPoisonSecondsFlat, 3f, "spear_thrower")),
                TreeNode("spear_poison_damage", "Strong Poison", "Poisoned Tips: +1 poison damage a second.", "poison|up", new Vector2(655f, -304f), 5, CurrencyType.KillEssence, 6, 1.25f, "spear_poison", false,
                    TreeEffect(UpgradeEffectType.TowerPoisonDamagePerSecondFlat, 1f, "spear_thrower")),
                TreeNode("spear_poison_time", "Slow Poison", "Poisoned Tips: the poison lasts 1 s longer.", "poison|time", new Vector2(719f, -191f), 5, CurrencyType.KillEssence, 6, 1.25f, "spear_poison", false,
                    TreeEffect(UpgradeEffectType.TowerPoisonSecondsFlat, 1f, "spear_thrower")),
                TreeNode("spear_pierce", "Piercing Spears", "Special: a spear passes through 2 more goblins; each one after the first takes 30% less than the one before.", "pierce", new Vector2(634f, -94f), 1, CurrencyType.ChallengeToken, 3, 1.2f, "spear_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerPierceFlat, 2f, "spear_thrower"), TreeEffect(UpgradeEffectType.TowerPierceFalloffPercent, 30f, "spear_thrower")),
                TreeNode("spear_pierce_count", "Longer Shafts", "Piercing Spears: passes through one more goblin.", "pierce|plus", new Vector2(756f, -110f), 5, CurrencyType.KillEssence, 6, 1.25f, "spear_pierce", false,
                    TreeEffect(UpgradeEffectType.TowerPierceFlat, 1f, "spear_thrower")),
                TreeNode("spear_pierce_keep", "Balanced Spears", "Piercing Spears: each next goblin takes 5% less off.", "pierce|up", new Vector2(725f, 1f), 5, CurrencyType.KillEssence, 6, 1.25f, "spear_pierce", false,
                    TreeEffect(UpgradeEffectType.TowerPierceFalloffPercent, -5f, "spear_thrower")),
                TreeNode("spear_big_game", "Big-Game Hunters", "Special: double damage against heavy enemies: Orc Brutes and the boss.", "biggame", new Vector2(472f, -223f), 1, CurrencyType.ChallengeToken, 4, 1.2f, "spear_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerHeavyDamagePercent, 100f, "spear_thrower")),
                TreeNode("spear_big_game_damage", "Hunter's Eye", "Big-Game Hunters: +25% more against heavy enemies.", "biggame|up", new Vector2(361f, -273f), 5, CurrencyType.KillEssence, 6, 1.25f, "spear_big_game", false,
                    TreeEffect(UpgradeEffectType.TowerHeavyDamagePercent, 25f, "spear_thrower")),
                TreeNode("spear_big_game_weight", "Heavier Spears", "Big-Game Hunters: spears hit 5% harder against everything.", "spear|up", new Vector2(531f, -306f), 5, CurrencyType.KillEssence, 6, 1.25f, "spear_big_game", false,
                    TreeEffect(UpgradeEffectType.TowerDamagePercent, 5f, "spear_thrower")),
                TreeNode("mammoth_unlock", "Tamed Mammoth", "A mammoth that charges into the horde, trampling and throwing goblins aside. Costs a Victory Sigil.", "mammoth", new Vector2(484f, 129f), 1, CurrencyType.VictorySigil, 1, 1.2f, "volley_core", true,
                    TreeEffect(UpgradeEffectType.UnlockTower, 1f, "mammoth")),
                TreeNode("mammoth_damage", "Heavy Tread", "The mammoth tramples and scoops harder.", "damage", new Vector2(662f, 193f), 10, CurrencyType.KillEssence, 3, 1.2f, "mammoth_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerDamagePercent, 10f, "mammoth")),
                TreeNode("mammoth_health", "Thick Fur", "The mammoth has 20 more health.", "health", new Vector2(269f, 183f), 8, CurrencyType.KillEssence, 3, 1.2f, "mammoth_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerHealthFlat, 20f, "mammoth")),
                TreeNode("mammoth_herd", "Bigger Herd", "The next mammoth is ready sooner.", "cooldown", new Vector2(490f, 277f), 5, CurrencyType.KillEssence, 4, 1.25f, "mammoth_unlock", false,
                    TreeEffect(UpgradeEffectType.TowerFireRatePercent, 8f, "mammoth")),
                TreeNode("mammoth_rage", "Rage", "Special: below 40% health the mammoth scoops 30% faster and harder.", "rage", new Vector2(640f, 78f), 1, CurrencyType.ChallengeToken, 2, 1.2f, "mammoth_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerBeastRagePercent, 30f, "mammoth"), TreeEffect(UpgradeEffectType.TowerBeastRageThresholdPercent, 40f, "mammoth")),
                TreeNode("mammoth_rage_power", "Blind Fury", "Rage: +10% faster and harder.", "rage|up", new Vector2(766f, 91f), 5, CurrencyType.KillEssence, 6, 1.25f, "mammoth_rage", false,
                    TreeEffect(UpgradeEffectType.TowerBeastRagePercent, 10f, "mammoth")),
                TreeNode("mammoth_rage_early", "Short Temper", "Rage: starts 10% of its health sooner.", "rage|fast", new Vector2(762f, 181f), 4, CurrencyType.KillEssence, 6, 1.25f, "mammoth_rage", false,
                    TreeEffect(UpgradeEffectType.TowerBeastRageThresholdPercent, 10f, "mammoth")),
                TreeNode("mammoth_riders", "Slingers on its Back", "Special: 2 slingers ride the mammoth and pelt the goblins ahead while it charges and fights (3 damage a stone).", "riders", new Vector2(373f, 218f), 1, CurrencyType.ChallengeToken, 3, 1.2f, "mammoth_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerBeastRidersFlat, 2f, "mammoth"), TreeEffect(UpgradeEffectType.TowerBeastRiderDamageFlat, 3f, "mammoth")),
                TreeNode("mammoth_riders_more", "More Riders", "Slingers on its Back: one more slinger.", "riders|plus", new Vector2(325f, 341f), 3, CurrencyType.KillEssence, 10, 1.5f, "mammoth_riders", false,
                    TreeEffect(UpgradeEffectType.TowerBeastRidersFlat, 1f, "mammoth")),
                TreeNode("mammoth_riders_damage", "Riders' Stones", "Slingers on its Back: stones deal 1.5 more damage.", "riders|up", new Vector2(252f, 271f), 5, CurrencyType.KillEssence, 6, 1.25f, "mammoth_riders", false,
                    TreeEffect(UpgradeEffectType.TowerBeastRiderDamageFlat, 1.5f, "mammoth")),
                TreeNode("mammoth_armor", "Armoured Mammoth", "Special: 100 armour that is lost before its health, 20% harder hits, and crowds slow its charge 30% less.", "armor", new Vector2(592f, 285f), 1, CurrencyType.ChallengeToken, 4, 1.2f, "mammoth_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerBeastArmorFlat, 100f, "mammoth"), TreeEffect(UpgradeEffectType.TowerDamagePercent, 20f, "mammoth"), TreeEffect(UpgradeEffectType.TowerBeastCrowdPercent, 30f, "mammoth")),
                TreeNode("mammoth_armor_thick", "Thicker Hide", "Armoured Mammoth: 40 more armour.", "armor|up", new Vector2(705f, 343f), 5, CurrencyType.KillEssence, 6, 1.25f, "mammoth_armor", false,
                    TreeEffect(UpgradeEffectType.TowerBeastArmorFlat, 40f, "mammoth")),
                TreeNode("mammoth_armor_ram", "Battering Ram", "Armoured Mammoth: crowds slow its charge 10% less.", "armor|fast", new Vector2(440f, 356f), 5, CurrencyType.KillEssence, 6, 1.25f, "mammoth_armor", false,
                    TreeEffect(UpgradeEffectType.TowerBeastCrowdPercent, 10f, "mammoth")),
                TreeNode("cave_unlock", "Cave", "Cavemen with clubs come out and hold the goblins back on the road. Costs a Victory Sigil.", "cave", new Vector2(-473f, -136f), 1, CurrencyType.VictorySigil, 1, 1.2f, "volley_core", true,
                    TreeEffect(UpgradeEffectType.UnlockTower, 1f, "cave")),
                TreeNode("cave_damage", "Bigger Clubs", "Cavemen hit harder.", "damage", new Vector2(-295f, -227f), 10, CurrencyType.KillEssence, 2, 1.2f, "cave_unlock", false,
                    TreeEffect(UpgradeEffectType.BarracksUnitDamagePercent, 10f, "cave")),
                TreeNode("cave_health", "Tough Hides", "Cavemen have more health.", "health", new Vector2(-617f, -190f), 10, CurrencyType.KillEssence, 2, 1.2f, "cave_unlock", false,
                    TreeEffect(UpgradeEffectType.BarracksUnitHealthPercent, 10f, "cave")),
                TreeNode("cave_capacity", "Bigger Tribe", "One more caveman can be out at once (up to 5).", "capacity", new Vector2(-401f, 25f), 4, CurrencyType.KillEssence, 8, 1.5f, "cave_unlock", false,
                    TreeEffect(UpgradeEffectType.BarracksUnitCapacityFlat, 1f, "cave")),
                TreeNode("cave_shields", "Shields", "Special: the cavemen carry shields and take 25% less damage.", "shield", new Vector2(-627f, -87f), 1, CurrencyType.ChallengeToken, 2, 1.2f, "cave_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerUnitDamageReductionPercent, 25f, "cave")),
                TreeNode("cave_shields_strong", "Stronger Shields", "Shields: 5% less damage taken.", "shield|up", new Vector2(-767f, -75f), 5, CurrencyType.KillEssence, 6, 1.25f, "cave_shields", false,
                    TreeEffect(UpgradeEffectType.TowerUnitDamageReductionPercent, 5f, "cave")),
                TreeNode("cave_shields_hide", "Hide Armour", "Shields: each blow does 0.5 less damage.", "shield|plus", new Vector2(-725f, -165f), 5, CurrencyType.KillEssence, 6, 1.25f, "cave_shields", false,
                    TreeEffect(UpgradeEffectType.TowerUnitArmorFlat, 0.5f, "cave")),
                TreeNode("cave_chief", "Chief", "Special: one caveman is a chief: twice the health, 50% harder hits, and cavemen near him hit 25% harder.", "chief", new Vector2(-403f, -296f), 1, CurrencyType.ChallengeToken, 3, 1.2f, "cave_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerChiefAuraPercent, 25f, "cave"), TreeEffect(UpgradeEffectType.TowerChiefHealthPercent, 100f, "cave")),
                TreeNode("cave_chief_aura", "War Cry", "Chief: the cavemen near him hit 10% harder.", "chief|up", new Vector2(-275f, -345f), 5, CurrencyType.KillEssence, 6, 1.25f, "cave_chief", false,
                    TreeEffect(UpgradeEffectType.TowerChiefAuraPercent, 10f, "cave")),
                TreeNode("cave_chief_health", "Old Warrior", "Chief: 25% more health.", "chief|heart", new Vector2(-199f, -283f), 5, CurrencyType.KillEssence, 6, 1.25f, "cave_chief", false,
                    TreeEffect(UpgradeEffectType.TowerChiefHealthPercent, 25f, "cave")),
                TreeNode("cave_torches", "Torches", "Special: the cavemen's blows set goblins burning, 2 damage a second for 3 s.", "torch", new Vector2(-533f, -259f), 1, CurrencyType.ChallengeToken, 4, 1.2f, "cave_unlock", true,
                    TreeEffect(UpgradeEffectType.TowerTorchBurnDamagePerSecondFlat, 2f, "cave"), TreeEffect(UpgradeEffectType.TowerTorchBurnSecondsFlat, 3f, "cave")),
                TreeNode("cave_torches_hot", "Hot Embers", "Torches: +1 burn damage a second.", "torch|up", new Vector2(-632f, -325f), 5, CurrencyType.KillEssence, 6, 1.25f, "cave_torches", false,
                    TreeEffect(UpgradeEffectType.TowerTorchBurnDamagePerSecondFlat, 1f, "cave")),
                TreeNode("cave_torches_long", "Pitch", "Torches: the burn lasts 1 s longer.", "torch|time", new Vector2(-499f, -356f), 5, CurrencyType.KillEssence, 6, 1.25f, "cave_torches", false,
                    TreeEffect(UpgradeEffectType.TowerTorchBurnSecondsFlat, 1f, "cave")),
                TreeNode("rock_damage", "Heavier Rock", "The rock hits harder (impact, roll, fire and shards).", "damage", new Vector2(-46f, 146f), 10, CurrencyType.KillEssence, 2, 1.2f, "volley_core", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponDamagePercent, 10f)),
                TreeNode("rock_cooldown", "Quick Haul", "The next rock is ready sooner.", "cooldown", new Vector2(-109f, -98f), 8, CurrencyType.KillEssence, 3, 1.2f, "volley_core", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponCooldownPercent, 5f)),
                TreeNode("rock_roll", "Longer Roll", "The rock rolls 2 m further.", "roll", new Vector2(115f, -118f), 5, CurrencyType.KillEssence, 4, 1.25f, "volley_core", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponRollDistanceFlat, 2f)),
                TreeNode("rock_quake", "Quake", "Special: where the rock lands, goblins within 3.5 m are stunned for 0.8 s.", "quake", new Vector2(-142f, 117f), 1, CurrencyType.ChallengeToken, 2, 1.2f, "volley_core", true,
                    TreeEffect(UpgradeEffectType.ActiveWeaponQuakeRadiusFlat, 3.5f), TreeEffect(UpgradeEffectType.ActiveWeaponQuakeSecondsFlat, 0.8f)),
                TreeNode("rock_quake_radius", "Wide Quake", "Quake: reaches 0.5 m further.", "quake|plus", new Vector2(-195f, 233f), 5, CurrencyType.KillEssence, 6, 1.25f, "rock_quake", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponQuakeRadiusFlat, 0.5f)),
                TreeNode("rock_quake_time", "Deep Quake", "Quake: the stun lasts 0.2 s longer.", "quake|time", new Vector2(-243f, 112f), 5, CurrencyType.KillEssence, 6, 1.25f, "rock_quake", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponQuakeSecondsFlat, 0.2f)),
                TreeNode("rock_fire", "Fire Rock", "Special: the rock burns and leaves a burning trail where it rolls.", "fire", new Vector2(75f, 108f), 1, CurrencyType.ChallengeToken, 3, 1.2f, "volley_core", true,
                    TreeEffect(UpgradeEffectType.ActiveWeaponFireTrailUnlock, 1f)),
                TreeNode("rock_fire_damage", "Hotter Fire", "Fire Rock: each burn tick deals 20% more.", "fire|up", new Vector2(181f, 158f), 5, CurrencyType.KillEssence, 6, 1.25f, "rock_fire", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponFireDamagePercent, 20f)),
                TreeNode("rock_fire_rate", "Fierce Fire", "Fire Rock: burns 0.5 more times per second.", "fire|fast", new Vector2(103f, 238f), 5, CurrencyType.KillEssence, 6, 1.25f, "rock_fire", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponFireTicksPerSecondFlat, 0.5f)),
                TreeNode("rock_shatter", "Shatter", "Special: at the end of its roll the rock bursts into 5 shards, each hitting where it lands for 40% of the rock's damage.", "shatter", new Vector2(25f, -171f), 1, CurrencyType.ChallengeToken, 4, 1.2f, "volley_core", true,
                    TreeEffect(UpgradeEffectType.ActiveWeaponShatterShardsFlat, 5f), TreeEffect(UpgradeEffectType.ActiveWeaponShatterDamagePercent, 40f)),
                TreeNode("rock_shatter_shards", "More Shards", "Shatter: 2 more shards.", "shatter|plus", new Vector2(-41f, -263f), 5, CurrencyType.KillEssence, 6, 1.25f, "rock_shatter", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponShatterShardsFlat, 2f)),
                TreeNode("rock_shatter_damage", "Sharp Shards", "Shatter: each shard deals 10% more of the rock's damage.", "shatter|up", new Vector2(104f, -261f), 5, CurrencyType.KillEssence, 6, 1.25f, "rock_shatter", false,
                    TreeEffect(UpgradeEffectType.ActiveWeaponShatterDamagePercent, 10f)),
            };

            return new SampleContent
            {
                Level = level,
                LevelOneTestWave = wave,
                PrehistoricBoss = prehistoricBoss,
                Levels = new[] { level, levelTwo, levelTwoPointOne, levelThree, levelFour, levelFive, levelSix, levelSeven },
                SkillTree = tree,
                Towers = new[] { archer, ballista, bell, catapult, barrier, knightBarracks, archerBarracks, paladinBarracks, slingers, spearThrower, mammoth, cave }
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

        private static SkillNodeDefinition TreeNode(string id, string displayName, string description, string iconKey, Vector2 position,
            int maxRanks, CurrencyType currency, int cost, float costGrowth, string prerequisite, bool isMajor,
            params UpgradeEffect[] effects)
        {
            return new SkillNodeDefinition
            {
                id = id,
                displayName = displayName,
                description = description,
                iconKey = iconKey,
                radialPosition = position,
                maxRanks = maxRanks,
                costGrowthMultiplier = costGrowth,
                costs = new[] { new CurrencyAmount(currency, cost) },
                prerequisiteNodeIds = new[] { prerequisite },
                isMajorUnlock = isMajor,
                effects = effects
            };
        }

        private static UpgradeEffect TreeEffect(UpgradeEffectType type, float value, string targetId = null)
        {
            return new UpgradeEffect { type = type, value = value, targetId = targetId };
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
