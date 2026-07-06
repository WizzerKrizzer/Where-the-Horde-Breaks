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
