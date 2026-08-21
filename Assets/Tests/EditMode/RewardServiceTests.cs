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

        [Test]
        public void BalanceBotSnapshot_RoundTripsIndependentProgressionState()
        {
            var store = new ProfileStore("balance_bot_snapshot_test_host.json");
            var source = new PlayerProfile { selectedLevelId = "level_02" };
            source.unlockedLevelIds.Add("level_01");
            source.unlockedLevelIds.Add("level_02");
            source.purchasedUpgradeIds.Add("archer_unlock");
            source.AddCurrency(CurrencyType.KillEssence, 37);
            source.AddCurrency(CurrencyType.PerfectSigil, 1);

            store.SaveBalanceBotSnapshot(source, "Test Bot", "level_01");

            Assert.That(store.HasBalanceBotSnapshot("Test Bot", "level_01"), Is.True);
            Assert.That(store.TryLoadBalanceBotSnapshot("Test Bot", "level_01", out var loaded), Is.True);
            Assert.That(loaded, Is.Not.SameAs(source));
            Assert.That(loaded.selectedLevelId, Is.EqualTo("level_02"));
            Assert.That(loaded.purchasedUpgradeIds, Does.Contain("archer_unlock"));
            Assert.That(loaded.GetCurrency(CurrencyType.KillEssence), Is.EqualTo(37));
            Assert.That(loaded.GetCurrency(CurrencyType.PerfectSigil), Is.EqualTo(1));
        }
    }
}
