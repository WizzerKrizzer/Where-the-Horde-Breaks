using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Rewards;
using TowerDefense.Save;
using UnityEngine;

namespace TowerDefense.Tests
{
    public sealed class RewardServiceTests
    {
        [Test]
        public void ApplyLevelRewards_FirstPerfectClear_AwardsBothOneTimeRewards()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.id = "level_01";
            level.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            level.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            level.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 25);

            var profile = new PlayerProfile();
            var rewards = new RewardService();

            rewards.ApplyLevelRewards(profile, level, won: true, perfect: true);
            rewards.ApplyLevelRewards(profile, level, won: true, perfect: true);

            Assert.That(profile.GetCurrency(CurrencyType.VictorySigil), Is.EqualTo(1));
            Assert.That(profile.GetCurrency(CurrencyType.PerfectSigil), Is.EqualTo(1));
            Assert.That(profile.GetCurrency(CurrencyType.KillEssence), Is.EqualTo(25));
        }

        [Test]
        public void ClearLevelRewardProgress_AllowsFirstClearRewardsAgain()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.id = "level_01";
            level.firstClearReward = new CurrencyAmount(CurrencyType.VictorySigil, 1);
            level.perfectClearReward = new CurrencyAmount(CurrencyType.PerfectSigil, 1);
            level.replayReward = new CurrencyAmount(CurrencyType.KillEssence, 25);

            var profile = new PlayerProfile();
            var rewards = new RewardService();

            rewards.ApplyLevelRewards(profile, level, won: true, perfect: true);
            profile.ClearCurrencies();
            profile.ClearLevelRewardProgress();
            rewards.ApplyLevelRewards(profile, level, won: true, perfect: false);

            Assert.That(profile.GetCurrency(CurrencyType.VictorySigil), Is.EqualTo(1));
            Assert.That(profile.GetCurrency(CurrencyType.KillEssence), Is.EqualTo(0));
        }
    }
}
