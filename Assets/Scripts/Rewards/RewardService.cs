using TowerDefense.Data;
using TowerDefense.Save;

namespace TowerDefense.Rewards
{
    public sealed class RewardService
    {
        public void ApplyLevelRewards(PlayerProfile profile, LevelDefinition level, bool won, bool perfect, int killEssenceMultiplier = 1)
        {
            if (!won)
            {
                return;
            }

            var firstClear = !profile.clearedLevelIds.Contains(level.id);
            var progress = profile.GetOrCreateLevelProgress(level.id);
            if (firstClear)
            {
                profile.clearedLevelIds.Add(level.id);
                profile.AddCurrency(level.firstClearReward.currency, level.firstClearReward.amount);
                progress.firstClearClaimed = true;
                if (progress.firstVictoryAttempt <= 0)
                {
                    progress.firstVictoryAttempt = progress.attempts;
                }
            }
            else
            {
                var replayAmount = level.replayReward.currency == CurrencyType.KillEssence
                    ? level.replayReward.amount * killEssenceMultiplier
                    : level.replayReward.amount;
                profile.AddCurrency(level.replayReward.currency, replayAmount);
            }

            progress.victories++;
            if (perfect && !profile.perfectClearedLevelIds.Contains(level.id))
            {
                profile.perfectClearedLevelIds.Add(level.id);
                profile.AddCurrency(level.perfectClearReward.currency, level.perfectClearReward.amount);
                progress.perfectClearClaimed = true;
            }
        }

        public void ApplyKillReward(PlayerProfile profile, EnemyDefinition enemy)
        {
            profile.AddCurrency(CurrencyType.KillEssence, enemy.killReward);
        }
    }
}
