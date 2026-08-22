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
            activeWeapon.Initialize(enemyManager, input, towerManager);
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
            Application.targetFrameRate = 240;
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
            CreateMapDecor(mapRoot.transform, level);
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

        private static void CreateGround(Transform parent, LevelDefinition level)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "BuildableGround";
            ground.transform.SetParent(parent, false);
            ground.transform.position = level != null ? level.groundCenter : new Vector3(0f, -0.08f, 1.5f);
            ground.transform.localScale = level != null ? level.groundSize : new Vector3(82f, 0.1f, 50f);
            ground.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.29f, 0.36f, 0.25f));
            CreateGroundTexture(parent, level);
        }

        private static void CreateGroundTexture(Transform parent, LevelDefinition level)
        {
            var root = new GameObject("GroundTexture");
            root.transform.SetParent(parent, false);
            if (level != null && level.decorVariant == 2)
            {
                CreateLargeMapGroundTexture(root.transform, level.groundCenter, level.groundSize);
                return;
            }

            var patches = new[]
            {
                new Vector3(-34f, 0f, -14f), new Vector3(-29f, 0f, 16f), new Vector3(-24f, 0f, -18f),
                new Vector3(-17f, 0f, 1.5f), new Vector3(-8f, 0f, 16.5f), new Vector3(-5f, 0f, -16.5f),
                new Vector3(9f, 0f, 15.5f), new Vector3(11f, 0f, -17f), new Vector3(19f, 0f, -12f),
                new Vector3(26f, 0f, 17f), new Vector3(31f, 0f, -2f), new Vector3(35f, 0f, -17f),
                new Vector3(-38f, 0f, 5f), new Vector3(38f, 0f, 9f), new Vector3(0f, 0f, 20f)
            };

            for (var i = 0; i < patches.Length; i++)
            {
                var size = 0.55f + Mathf.Abs(Mathf.Sin(i * 1.71f)) * 0.75f;
                var color = i % 3 == 0
                    ? new Color(0.23f, 0.29f, 0.19f)
                    : new Color(0.34f, 0.41f, 0.27f);
                CreateGroundPatch(root.transform, patches[i], size, color, i);
            }

            for (var i = 0; i < 42; i++)
            {
                var x = -38f + i * 1.85f;
                var z = 20.5f + Mathf.Sin(i * 0.83f) * 1.1f;
                CreateGroundPatch(root.transform, new Vector3(x, 0f, z), 0.22f + 0.08f * (i % 4), new Color(0.23f, 0.29f, 0.18f), i + 50);
            }

            for (var i = 0; i < 36; i++)
            {
                var x = -37f + i * 2.1f;
                var z = -21.2f + Mathf.Sin(i * 1.12f) * 0.9f;
                CreateGroundPatch(root.transform, new Vector3(x, 0f, z), 0.18f + 0.07f * (i % 5), new Color(0.22f, 0.28f, 0.17f), i + 100);
            }
        }

        private static void CreateLargeMapGroundTexture(Transform parent, Vector3 center, Vector3 size)
        {
            var left = center.x - size.x * 0.5f + 4f;
            var right = center.x + size.x * 0.5f - 4f;
            var bottom = center.z - size.z * 0.5f + 4f;
            var top = center.z + size.z * 0.5f - 4f;
            for (var i = 0; i < 70; i++)
            {
                var x = Mathf.Lerp(left, right, Mathf.Abs(Mathf.Sin(i * 2.173f)));
                var z = Mathf.Lerp(bottom, top, Mathf.Abs(Mathf.Sin(i * 1.417f + 0.31f)));
                var avoidCenter = Mathf.Abs(z) < 19f && x > -48f && x < 48f;
                if (avoidCenter)
                {
                    z += z >= 0f ? 10f : -10f;
                }

                var color = i % 4 == 0 ? new Color(0.22f, 0.28f, 0.17f) : new Color(0.33f, 0.4f, 0.26f);
                CreateGroundPatch(parent, new Vector3(x, 0f, z), 0.25f + 0.12f * (i % 5), color, i + 200);
            }
        }

        private static void CreateGroundPatch(Transform parent, Vector3 position, float size, Color color, int index)
        {
            var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            patch.name = "GroundTexturePatch";
            patch.transform.SetParent(parent, false);
            patch.transform.position = position + Vector3.up * -0.015f;
            patch.transform.rotation = Quaternion.Euler(0f, index * 23f, 0f);
            patch.transform.localScale = new Vector3(size * (1.4f + 0.25f * (index % 3)), 0.018f, size * (0.55f + 0.18f * (index % 4)));
            patch.GetComponent<Renderer>().material = BootstrapMaterials.Get(color);
            RemovePrimitiveCollider(patch);
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

            route.SetWaypoints(points);
            var roadWidth = Mathf.Max(1f, level != null ? level.roadWidth : 5.4f);
            CreatePathVisuals(parent, points, roadWidth);
            if (level?.secondaryPathWaypoints != null && level.secondaryPathWaypoints.Length > 1)
            {
                route.SetSecondaryWaypoints(level.secondaryPathWaypoints);
                CreatePathVisuals(parent, level.secondaryPathWaypoints, roadWidth);
            }

            return route;
        }

        private static void CreatePathVisuals(Transform parent, Vector3[] points, float roadWidth)
        {
            for (var i = 1; i < points.Length; i++)
            {
                CreatePathSegment(parent, points[i - 1], points[i], roadWidth);
            }

            for (var i = 1; i < points.Length - 1; i++)
            {
                CreatePathCorner(parent, points[i], roadWidth);
            }

            CreatePathBoundary(parent, "PathBoundary_Left", points, 1f, roadWidth);
            CreatePathBoundary(parent, "PathBoundary_Right", points, -1f, roadWidth);
        }

        private static void CreatePathSegment(Transform parent, Vector3 from, Vector3 to, float roadWidth)
        {
            var midpoint = (from + to) * 0.5f + Vector3.up * 0.09f;
            var direction = to - from;
            var forward = direction.normalized;

            var shadow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shadow.name = "PathContactShadow";
            shadow.transform.SetParent(parent, false);
            shadow.transform.position = (from + to) * 0.5f + Vector3.up * 0.002f;
            shadow.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            shadow.transform.localScale = new Vector3(roadWidth + 0.7f, 0.018f, direction.magnitude + 0.18f);
            shadow.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.2f, 0.24f, 0.16f));
            RemovePrimitiveCollider(shadow);

            var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = "PathVisual";
            segment.transform.SetParent(parent, false);
            segment.transform.position = midpoint;
            segment.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            segment.transform.localScale = new Vector3(roadWidth, 0.08f, direction.magnitude);
            segment.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.55f, 0.44f, 0.31f));

            var rut = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rut.name = "PathWornCenter";
            rut.transform.SetParent(parent, false);
            rut.transform.position = midpoint + Vector3.up * 0.046f;
            rut.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            rut.transform.localScale = new Vector3(roadWidth * 0.34f, 0.012f, direction.magnitude * 0.96f);
            rut.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.42f, 0.31f, 0.2f));
            RemovePrimitiveCollider(rut);

            var side = Vector3.Cross(Vector3.up, forward);
            CreatePathEdgeAO(parent, midpoint, forward, side, direction.magnitude, roadWidth, 1f);
            CreatePathEdgeAO(parent, midpoint, forward, side, direction.magnitude, roadWidth, -1f);
        }

        private static void CreatePathEdgeAO(Transform parent, Vector3 midpoint, Vector3 forward, Vector3 side, float length, float roadWidth, float sideSign)
        {
            var ao = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ao.name = "PathEdgeAO";
            ao.transform.SetParent(parent, false);
            ao.transform.position = midpoint + side * sideSign * (roadWidth * 0.5f + 0.12f) + Vector3.up * -0.078f;
            ao.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            ao.transform.localScale = new Vector3(0.42f, 0.012f, length * 0.98f);
            ao.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.22f, 0.28f, 0.18f));
            RemovePrimitiveCollider(ao);
        }

        private static void CreatePathCorner(Transform parent, Vector3 position, float roadWidth)
        {
            var corner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            corner.name = "PathCornerFill";
            corner.transform.SetParent(parent, false);
            corner.transform.position = position + Vector3.up * 0.092f;
            corner.transform.localScale = new Vector3(roadWidth + 0.25f, 0.082f, roadWidth + 0.25f);
            corner.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.55f, 0.44f, 0.31f));
        }

        private static void CreatePathBoundary(Transform parent, string name, Vector3[] points, float sideSign, float roadWidth)
        {
            var bankOffset = roadWidth * 0.5f + 0.28f;
            var boundary = new GameObject(name);
            boundary.transform.SetParent(parent, false);
            var line = boundary.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            line.widthMultiplier = 0.22f;
            line.numCornerVertices = 4;
            line.numCapVertices = 2;
            line.material = BootstrapMaterials.Get(new Color(0.29f, 0.36f, 0.25f));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            for (var i = 0; i < points.Length; i++)
            {
                line.SetPosition(i, GetBoundaryPoint(points, i, sideSign, bankOffset) + Vector3.up * 0.165f);
            }
        }

        private static Vector3 GetBoundaryPoint(Vector3[] points, int index, float sideSign, float bankOffset)
        {
            if (points.Length < 2)
            {
                return points[index];
            }

            if (index == 0)
            {
                return points[index] + GetSegmentSide(points[0], points[1]) * sideSign * bankOffset;
            }

            if (index == points.Length - 1)
            {
                return points[index] + GetSegmentSide(points[index - 1], points[index]) * sideSign * bankOffset;
            }

            var incomingSide = GetSegmentSide(points[index - 1], points[index]) * sideSign;
            var outgoingSide = GetSegmentSide(points[index], points[index + 1]) * sideSign;
            var miter = incomingSide + outgoingSide;
            if (miter.sqrMagnitude < 0.001f)
            {
                return points[index] + incomingSide * bankOffset;
            }

            miter.Normalize();
            var scale = bankOffset / Mathf.Max(0.25f, Mathf.Abs(Vector3.Dot(miter, incomingSide)));
            return points[index] + miter * scale;
        }

        private static Vector3 GetSegmentSide(Vector3 from, Vector3 to)
        {
            var forward = (to - from).normalized;
            return Vector3.Cross(Vector3.up, forward);
        }

        private static void CreateMapDecor(Transform parent, LevelDefinition level)
        {
            var root = new GameObject("MapDecor");
            root.transform.SetParent(parent, false);
            if (level != null && level.decorVariant == 2)
            {
                CreateLevelTwoDecor(root.transform);
                return;
            }

            if (level != null && level.decorVariant == 3)
            {
                CreateLevelThreeDecor(root.transform);
                return;
            }

            CreateTrees(root.transform);
            CreateRuinedHouses(root.transform);
            CreatePondAndVillageProps(root.transform);
            CreateVillageFires(root.transform);
        }

        private static void CreateLevelTwoDecor(Transform parent)
        {
            CreateTreeCluster(parent, new Vector3(-58f, 0f, 23f), 5, 0);
            CreateTreeCluster(parent, new Vector3(-52f, 0f, -23f), 4, 10);
            CreateTreeCluster(parent, new Vector3(54f, 0f, 23f), 5, 20);
            CreateTreeCluster(parent, new Vector3(57f, 0f, -22f), 4, 30);
            CreateTreeCluster(parent, new Vector3(0f, 0f, 28f), 3, 40);
            CreateTreeCluster(parent, new Vector3(0f, 0f, -28f), 3, 50);
            CreateRuinedHouse(parent, new Vector3(-44f, 0f, 18f), 0.9f, -8f);
            CreateRuinedHouse(parent, new Vector3(44f, 0f, -18f), 0.9f, 172f);
            CreateWell(parent, new Vector3(0f, 0f, 25f), 0.7f);
            CreateDecorCube(parent, "DecorLevel2MarkerStoneA", new Vector3(-34f, 0.12f, 0f), new Vector3(0.8f, 0.24f, 0.55f), new Color(0.24f, 0.23f, 0.2f));
            CreateDecorCube(parent, "DecorLevel2MarkerStoneB", new Vector3(34f, 0.12f, 0f), new Vector3(0.8f, 0.24f, 0.55f), new Color(0.24f, 0.23f, 0.2f));
        }

        private static void CreateLevelThreeDecor(Transform parent)
        {
            CreateTreeCluster(parent, new Vector3(-58f, 0f, -28f), 5, 70);
            CreateTreeCluster(parent, new Vector3(-48f, 0f, 29f), 4, 80);
            CreateTreeCluster(parent, new Vector3(54f, 0f, 28f), 5, 90);
            CreateTreeCluster(parent, new Vector3(24f, 0f, -34f), 4, 100);
            CreateRuinedHouse(parent, new Vector3(-18f, 0f, 25f), 1.05f, 22f);
            CreateRuinedHouse(parent, new Vector3(31f, 0f, -32f), 0.95f, -18f);
            CreateRuinedHouse(parent, new Vector3(57f, 0f, 8f), 0.75f, 82f);
            CreateWell(parent, new Vector3(-34f, 0f, -24f), 0.68f);
            CreateDecorCube(parent, "DecorLevel3BrokenCartA", new Vector3(-42f, 0.18f, 0f), new Vector3(2.3f, 0.34f, 1.0f), new Color(0.23f, 0.14f, 0.08f));
            CreateDecorCube(parent, "DecorLevel3BrokenCartB", new Vector3(45f, 0.18f, -8f), new Vector3(2.1f, 0.34f, 0.9f), new Color(0.23f, 0.14f, 0.08f));
            CreateDecorCube(parent, "DecorLevel3GraveMarkerA", new Vector3(-4f, 0.18f, 31f), new Vector3(0.5f, 0.55f, 0.18f), new Color(0.38f, 0.38f, 0.36f));
            CreateDecorCube(parent, "DecorLevel3GraveMarkerB", new Vector3(1f, 0.18f, 33f), new Vector3(0.5f, 0.55f, 0.18f), new Color(0.38f, 0.38f, 0.36f));
            CreateDecorCube(parent, "DecorLevel3GraveMarkerC", new Vector3(6f, 0.18f, 31.5f), new Vector3(0.5f, 0.55f, 0.18f), new Color(0.38f, 0.38f, 0.36f));
        }

        private static void CreateTreeCluster(Transform parent, Vector3 center, int count, int seed)
        {
            for (var i = 0; i < count; i++)
            {
                var offset = new Vector3(Mathf.Sin((seed + i) * 1.7f) * 2.4f, 0f, Mathf.Cos((seed + i) * 1.31f) * 2.1f);
                CreateTree(parent, center + offset, 0.75f + 0.12f * (i % 4), seed + i);
            }
        }

        private static void CreateTrees(Transform parent)
        {
            var trees = new[]
            {
                new Vector3(-35f, 0f, -17f), new Vector3(-31f, 0f, -18.5f), new Vector3(-28f, 0f, 18f),
                new Vector3(-6.5f, 0f, 18.5f), new Vector3(12f, 0f, -18.8f), new Vector3(23f, 0f, -16.5f),
                new Vector3(33.5f, 0f, -10f), new Vector3(35f, 0f, 15.5f), new Vector3(0f, 0f, 19.2f)
            };

            for (var i = 0; i < trees.Length; i++)
            {
                CreateTree(parent, trees[i], 0.85f + 0.15f * (i % 3), i);
            }
        }

        private static void CreateTree(Transform parent, Vector3 position, float scale, int index)
        {
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "DecorTree_Trunk";
            trunk.transform.SetParent(parent, false);
            trunk.transform.position = position + Vector3.up * 0.45f;
            trunk.transform.localScale = new Vector3((0.18f + 0.04f * (index % 3)) * scale, 0.55f * scale, (0.2f + 0.03f * ((index + 1) % 3)) * scale);
            trunk.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.24f, 0.16f, 0.09f));
            RemovePrimitiveCollider(trunk);

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "DecorTree_Crown";
            crown.transform.SetParent(parent, false);
            crown.transform.position = position + Vector3.up * (1.06f * scale);
            crown.transform.localScale = new Vector3((0.9f + 0.13f * (index % 3)) * scale, (0.55f + 0.07f * ((index + 2) % 3)) * scale, (0.98f + 0.16f * ((index + 1) % 3)) * scale);
            var green = index % 4 == 0
                ? new Color(0.35f, 0.42f, 0.23f)
                : index % 3 == 0
                    ? new Color(0.18f, 0.35f, 0.15f)
                    : new Color(0.13f, 0.31f, 0.12f);
            crown.GetComponent<Renderer>().material = BootstrapMaterials.Get(green);
            RemovePrimitiveCollider(crown);

            if (index % 5 == 2)
            {
                var sideBlob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sideBlob.name = "DecorTree_CrownLobe";
                sideBlob.transform.SetParent(parent, false);
                sideBlob.transform.position = position + new Vector3(0.28f * scale, 0.94f * scale, -0.2f * scale);
                sideBlob.transform.localScale = new Vector3(0.55f, 0.36f, 0.52f) * scale;
                sideBlob.GetComponent<Renderer>().material = BootstrapMaterials.Get(green * 0.92f);
                RemovePrimitiveCollider(sideBlob);
            }
        }

        private static void CreateRuinedHouses(Transform parent)
        {
            CreateRuinedHouse(parent, new Vector3(-25.5f, 0f, -15f), 1.1f, 8f);
            CreateRuinedHouse(parent, new Vector3(20.5f, 0f, 17f), 0.95f, -11f);
            CreateRuinedHouse(parent, new Vector3(31f, 0f, -17.5f), 0.82f, 18f);
        }

        private static void CreateRuinedHouse(Transform parent, Vector3 position, float scale, float yaw)
        {
            var root = new GameObject("DecorRuinedHouse");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var stone = new Color(0.36f, 0.33f, 0.27f);
            var darkStone = new Color(0.24f, 0.23f, 0.2f);
            var timber = new Color(0.23f, 0.13f, 0.065f);
            var thatch = new Color(0.38f, 0.29f, 0.15f);

            CreateDecorCube(root.transform, "Ruin_Floor", new Vector3(0f, 0.015f, 0f), new Vector3(2.8f, 0.03f, 2.05f) * scale, new Color(0.2f, 0.18f, 0.14f));
            CreateDecorCube(root.transform, "Ruin_BackWall", new Vector3(0f, 0.44f, 0.92f), new Vector3(2.65f, 0.88f, 0.2f) * scale, stone);
            CreateDecorCube(root.transform, "Ruin_LeftWall", new Vector3(-1.22f, 0.34f, -0.08f), new Vector3(0.22f, 0.68f, 1.85f) * scale, stone);
            CreateDecorCube(root.transform, "Ruin_RightStub", new Vector3(1.18f, 0.24f, 0.48f), new Vector3(0.2f, 0.48f, 0.82f) * scale, darkStone);
            CreateDecorCube(root.transform, "Ruin_DoorGapLintel", new Vector3(0.15f, 0.82f, -0.98f), new Vector3(1.35f, 0.16f, 0.18f) * scale, timber);

            var roofA = CreateDecorCube(root.transform, "Ruin_CollapsedRoofA", new Vector3(-0.45f, 0.82f, 0.1f), new Vector3(1.65f, 0.16f, 1.05f) * scale, thatch);
            roofA.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
            var roofB = CreateDecorCube(root.transform, "Ruin_CollapsedRoofB", new Vector3(0.6f, 0.34f, -0.45f), new Vector3(1.2f, 0.13f, 0.85f) * scale, thatch * 0.82f);
            roofB.transform.localRotation = Quaternion.Euler(0f, 0f, 24f);

            CreateDecorCube(root.transform, "Ruin_BeamA", new Vector3(-0.1f, 0.67f, 0.2f), new Vector3(2.45f, 0.12f, 0.14f) * scale, timber);
            CreateDecorCube(root.transform, "Ruin_BeamB", new Vector3(0.35f, 0.18f, -0.85f), new Vector3(1.65f, 0.12f, 0.13f) * scale, timber);
            CreateDecorCube(root.transform, "Ruin_RubbleA", new Vector3(0.95f, 0.08f, -0.1f), new Vector3(0.5f, 0.16f, 0.34f) * scale, darkStone);
            CreateDecorCube(root.transform, "Ruin_RubbleB", new Vector3(-0.2f, 0.07f, -0.78f), new Vector3(0.48f, 0.14f, 0.28f) * scale, darkStone);
            CreateDecorCube(root.transform, "Ruin_RubbleC", new Vector3(0.35f, 0.06f, 1.22f), new Vector3(0.62f, 0.12f, 0.26f) * scale, darkStone);
        }

        private static void CreatePondAndVillageProps(Transform parent)
        {
            CreatePond(parent, new Vector3(-34f, 0f, 2.5f), 1.05f);
            CreateWell(parent, new Vector3(31.5f, 0f, 3.8f), 0.85f);
            CreateDecorCube(parent, "DecorCartBroken_Axle", new Vector3(29.4f, 0.13f, -12.5f), new Vector3(1.35f, 0.12f, 0.16f), new Color(0.24f, 0.14f, 0.075f));
            var cartBed = CreateDecorCube(parent, "DecorCartBroken_Bed", new Vector3(29.1f, 0.18f, -12.15f), new Vector3(1.15f, 0.16f, 0.65f), new Color(0.3f, 0.19f, 0.09f));
            cartBed.transform.rotation = Quaternion.Euler(0f, -18f, 0f);
            CreateDecorCube(parent, "DecorCrates", new Vector3(-30.5f, 0.16f, 15.4f), new Vector3(0.62f, 0.32f, 0.62f), new Color(0.31f, 0.2f, 0.1f));
            CreateDecorCube(parent, "DecorCrates", new Vector3(-29.8f, 0.12f, 16.1f), new Vector3(0.48f, 0.24f, 0.48f), new Color(0.27f, 0.17f, 0.085f));
        }

        private static void CreateVillageFires(Transform parent)
        {
            CreateFire(parent, new Vector3(-24.6f, 0f, -13.9f), 0.75f);
            CreateFire(parent, new Vector3(21.3f, 0f, 15.9f), 0.65f);
            CreateFire(parent, new Vector3(30.2f, 0f, -16.4f), 0.55f);
        }

        private static void CreateFire(Transform parent, Vector3 position, float scale)
        {
            var baseAsh = CreateDecorCube(parent, "DecorFire_Ash", position + Vector3.up * 0.02f, new Vector3(0.9f, 0.035f, 0.65f) * scale, new Color(0.08f, 0.075f, 0.07f));
            baseAsh.transform.rotation = Quaternion.Euler(0f, 24f, 0f);
            for (var i = 0; i < 6; i++)
            {
                var angle = i * Mathf.PI * 2f / 6f;
                var rockPosition = position + new Vector3(Mathf.Cos(angle) * 0.48f * scale, 0.07f, Mathf.Sin(angle) * 0.36f * scale);
                CreateDecorCube(parent, "DecorFire_RingStone", rockPosition, new Vector3(0.18f, 0.12f, 0.16f) * scale, new Color(0.18f, 0.17f, 0.15f));
            }

            var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flame.name = "DecorFire_Flame";
            flame.transform.SetParent(parent, false);
            flame.transform.position = position + Vector3.up * (0.34f * scale);
            flame.transform.localScale = new Vector3(0.32f, 0.58f, 0.32f) * scale;
            flame.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(1f, 0.44f, 0.08f));
            flame.AddComponent<FireFlicker>();
            RemovePrimitiveCollider(flame);

            var flameCore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flameCore.name = "DecorFire_Core";
            flameCore.transform.SetParent(parent, false);
            flameCore.transform.position = position + Vector3.up * (0.42f * scale);
            flameCore.transform.localScale = new Vector3(0.18f, 0.38f, 0.18f) * scale;
            flameCore.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(1f, 0.82f, 0.24f));
            flameCore.AddComponent<FireFlicker>();
            RemovePrimitiveCollider(flameCore);

            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "DecorFire_Glow";
            glow.transform.SetParent(parent, false);
            glow.transform.position = position + Vector3.up * (0.2f * scale);
            glow.transform.localScale = new Vector3(0.95f, 0.04f, 0.95f) * scale;
            glow.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.75f, 0.22f, 0.04f, 0.55f));
            RemovePrimitiveCollider(glow);
        }

        private static void CreatePond(Transform parent, Vector3 position, float scale)
        {
            var bank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bank.name = "DecorPond_Bank";
            bank.transform.SetParent(parent, false);
            bank.transform.position = position + Vector3.up * 0.005f;
            bank.transform.localScale = new Vector3(3.4f * scale, 0.015f, 2.05f * scale);
            bank.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.09f, 0.18f, 0.11f));
            RemovePrimitiveCollider(bank);

            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = "DecorPond_Water";
            water.transform.SetParent(parent, false);
            water.transform.position = position + Vector3.up * 0.025f;
            water.transform.localScale = new Vector3(2.75f * scale, 0.012f, 1.55f * scale);
            water.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.06f, 0.26f, 0.36f));
            RemovePrimitiveCollider(water);

            CreateDecorCube(parent, "DecorPond_ReedA", position + new Vector3(-2.1f, 0.18f, 0.78f) * scale, new Vector3(0.12f, 0.36f, 0.08f) * scale, new Color(0.16f, 0.31f, 0.12f));
            CreateDecorCube(parent, "DecorPond_ReedB", position + new Vector3(1.9f, 0.14f, -0.62f) * scale, new Vector3(0.1f, 0.28f, 0.08f) * scale, new Color(0.14f, 0.29f, 0.1f));
        }

        private static void CreateWell(Transform parent, Vector3 position, float scale)
        {
            var baseStone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseStone.name = "DecorWell_StoneRing";
            baseStone.transform.SetParent(parent, false);
            baseStone.transform.position = position + Vector3.up * 0.24f * scale;
            baseStone.transform.localScale = new Vector3(0.9f * scale, 0.26f * scale, 0.9f * scale);
            baseStone.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.28f, 0.27f, 0.24f));
            RemovePrimitiveCollider(baseStone);

            CreateDecorCube(parent, "DecorWell_PostA", position + new Vector3(-0.62f, 0.76f, 0f) * scale, new Vector3(0.13f, 0.92f, 0.13f) * scale, new Color(0.22f, 0.13f, 0.065f));
            CreateDecorCube(parent, "DecorWell_PostB", position + new Vector3(0.62f, 0.76f, 0f) * scale, new Vector3(0.13f, 0.92f, 0.13f) * scale, new Color(0.22f, 0.13f, 0.065f));
            var roof = CreateDecorCube(parent, "DecorWell_Roof", position + new Vector3(0f, 1.28f, 0f) * scale, new Vector3(1.65f, 0.16f, 1f) * scale, new Color(0.38f, 0.25f, 0.12f));
            roof.transform.rotation = Quaternion.Euler(0f, 0f, 6f);
        }

        private static GameObject CreateDecorCube(Transform parent, string name, Vector3 localPosition, Vector3 scale, Color color)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().material = BootstrapMaterials.Get(color);
            RemovePrimitiveCollider(cube);
            return cube;
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
            wave.totalEnemyCount = 215;
            wave.spawnInterval = 0.5f;
            wave.randomSpawnBurstMin = 3;
            wave.randomSpawnBurstMax = 8;
            wave.entries = BuildLevelOneWaveEntries(runner, brute);

            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.id = "level_01";
            level.displayName = "Broken Green Pass";
            level.startingLives = 10;
            level.wave = wave;
            level.pathWaypoints = CreateLevelOnePath();
            level.groundCenter = new Vector3(0f, -0.08f, 1.5f);
            level.groundSize = new Vector3(82f, 0.1f, 50f);
            level.decorVariant = 1;
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
            levelTwoWave.totalEnemyCount = 500;
            levelTwoWave.spawnInterval = 0.45f;
            levelTwoWave.randomSpawnBurstMin = 5;
            levelTwoWave.randomSpawnBurstMax = 12;
            levelTwoWave.useEndpointSeeking = true;
            levelTwoWave.entries = new[]
            {
                new WaveEntry { enemy = runner, count = 500 }
            };

            var levelTwo = ScriptableObject.CreateInstance<LevelDefinition>();
            levelTwo.id = "level_02";
            levelTwo.displayName = "Twin Serpent Road";
            levelTwo.startingLives = 12;
            levelTwo.wave = levelTwoWave;
            levelTwo.pathWaypoints = CreateLevelTwoPath();
            levelTwo.secondaryPathWaypoints = CreateLevelTwoSecondaryPath();
            levelTwo.groundCenter = new Vector3(0f, -0.08f, 0f);
            levelTwo.groundSize = new Vector3(126f, 0.1f, 70f);
            levelTwo.decorVariant = 2;
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
            levelTwo.recommendedTactics = "A symmetrical split road. Defenses near the fork and the rejoin should cover both lanes, while long-range towers can exploit the broad middle stretch.";

            var levelThreeWave = ScriptableObject.CreateInstance<WaveDefinition>();
            levelThreeWave.id = "wave_03_foundation";
            levelThreeWave.totalEnemyCount = 2040;
            levelThreeWave.spawnInterval = 0.34f;
            levelThreeWave.randomSpawnBurstMin = 9;
            levelThreeWave.randomSpawnBurstMax = 20;
            levelThreeWave.entries = BuildLevelThreeWaveEntries(runner, brute, shaman, vampire, harpy, zombie);

            var levelThree = ScriptableObject.CreateInstance<LevelDefinition>();
            levelThree.id = "level_03";
            levelThree.displayName = "Haunted Causeway";
            levelThree.startingLives = 14;
            levelThree.wave = levelThreeWave;
            levelThree.pathWaypoints = CreateLevelThreePath();
            levelThree.groundCenter = new Vector3(0f, -0.08f, 1f);
            levelThree.groundSize = new Vector3(150f, 0.1f, 84f);
            levelThree.decorVariant = 3;
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
            levelFourWave.entries = BuildLevelFourStressWaveEntries(runner, brute, shaman, vampire, harpy, zombie);

            var levelFour = ScriptableObject.CreateInstance<LevelDefinition>();
            levelFour.id = "level_04";
            levelFour.displayName = "Stress Field: Ten Thousand";
            levelFour.startingLives = 30;
            levelFour.wave = levelFourWave;
            levelFour.pathWaypoints = CreateLevelFourPath();
            levelFour.groundCenter = new Vector3(0f, -0.08f, 0f);
            levelFour.groundSize = new Vector3(230f, 0.1f, 132f);
            levelFour.decorVariant = 4;
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
            // Match Level 4's entrance throughput instead of forcing 100K bodies
            // through one road ten times faster. Fifty agents every ~0.1s gives
            // a smooth ~500 agents/second and a total spawn duration near 200s.
            levelFiveWave.spawnInterval = 0.1725f;
            levelFiveWave.randomSpawnBurstMin = 40;
            levelFiveWave.randomSpawnBurstMax = 60;
            levelFiveWave.useEndpointSeeking = true;
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
            levelFive.decorVariant = 4;
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
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.TowerHealthFlat, targetId = "barrier", value = 20f } }
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
                }
            };

            return new SampleContent
            {
                Level = level,
                Levels = new[] { level, levelTwo, levelThree, levelFour, levelFive },
                SkillTree = tree,
                Towers = new[] { archer, ballista, bell, catapult, barrier, knightBarracks, archerBarracks, paladinBarracks }
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
                new Vector3(-48f, 0f, 0f),
                new Vector3(-36f, 0f, 0f),
                new Vector3(-25f, 0f, 5.2f),
                new Vector3(-18f, 0f, 12.8f),
                new Vector3(-6f, 0f, 16.6f),
                new Vector3(6f, 0f, 16.6f),
                new Vector3(15.8f, 0f, 12.8f),
                new Vector3(13.6f, 0f, 3.6f),
                new Vector3(25f, 0f, 5.2f),
                new Vector3(36f, 0f, 0f),
                new Vector3(48f, 0f, 0f)
            };
        }

        private static Vector3[] CreateLevelTwoSecondaryPath()
        {
            return new[]
            {
                new Vector3(-48f, 0f, 0f),
                new Vector3(-36f, 0f, 0f),
                new Vector3(-25f, 0f, -5.2f),
                new Vector3(-18f, 0f, -12.8f),
                new Vector3(-6f, 0f, -16.6f),
                new Vector3(6f, 0f, -16.6f),
                new Vector3(15.8f, 0f, -12.8f),
                new Vector3(13.6f, 0f, -3.6f),
                new Vector3(25f, 0f, -5.2f),
                new Vector3(36f, 0f, 0f),
                new Vector3(48f, 0f, 0f)
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
