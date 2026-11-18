using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Level Definition")]
    public sealed class LevelDefinition : ScriptableObject
    {
        public string id = "level_01";
        public string displayName = "First Pass";
        public int startingLives = 20;
        public bool preventLifeLoss;
        public WaveDefinition wave;
        public Vector3[] pathWaypoints;
        public float[] pathWaypointWidths;
        public Vector3[] secondaryPathWaypoints;
        public float[] secondaryPathWaypointWidths;
        public Vector3[] tertiaryPathWaypoints;
        public float[] tertiaryPathWaypointWidths;
        public bool useSquarePathCorners;
        public bool useCircularArena;
        public float arenaRadius = 30f;
        public float roadWidth = 5.4f;
        public Vector3 groundCenter = new(0f, -0.08f, 1.5f);
        public Vector3 groundSize = new(82f, 0.1f, 50f);
        public EnvironmentTilePackageId environmentTilePackage = EnvironmentTilePackageId.Prehistoric;
        public bool useDataHordePrototype;
        // A plain wall across the road this many metres before its end; 0 health means no wall
        // (user, 2026-11-17). TEMPORARY numbers.
        public float wallHealth;
        public float wallDistanceFromEnd = 3f;
        // Which greybox scenery the map gets: 0 none, 1 level 1 (river, rocks, trees, bushes).
        public int sceneryLayout;
        public Vector3 cameraPosition = new(0f, 24f, -20f);
        public float cameraFieldOfView = 45f;
        public float cameraMinHeight = 8f;
        public float cameraMaxHeight = 60f;
        public float cameraPanSpeed = 26f;
        public float cameraMouseDragSensitivity = 3.35f;
        public Vector2 cameraMinBounds = new(-36f, -22f);
        public Vector2 cameraMaxBounds = new(36f, 22f);
        public CurrencyAmount firstClearReward = new(CurrencyType.VictorySigil, 1);
        public CurrencyAmount perfectClearReward = new(CurrencyType.PerfectSigil, 1);
        public CurrencyAmount replayReward = new(CurrencyType.KillEssence, 3);
        public CurrencyAmount bossClearReward = new(CurrencyType.BossCore, 1);
        public CurrencyAmount challengeReward = new(CurrencyType.ChallengeToken, 1);
        [TextArea(2, 4)]
        public string recommendedTactics = "No tactics have been recorded yet.";
        public ChallengeRule[] challenges;
    }
}
