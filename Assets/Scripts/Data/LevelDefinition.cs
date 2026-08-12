using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Level Definition")]
    public sealed class LevelDefinition : ScriptableObject
    {
        public string id = "level_01";
        public string displayName = "First Pass";
        public int startingLives = 20;
        public WaveDefinition wave;
        public Vector3[] pathWaypoints;
        public Vector3[] secondaryPathWaypoints;
        public Vector3 groundCenter = new(0f, -0.08f, 1.5f);
        public Vector3 groundSize = new(82f, 0.1f, 50f);
        public int decorVariant;
        public Vector3 cameraPosition = new(0f, 24f, -20f);
        public float cameraFieldOfView = 45f;
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
