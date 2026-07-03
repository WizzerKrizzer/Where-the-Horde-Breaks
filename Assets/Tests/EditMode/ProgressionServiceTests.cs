using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Progression;
using TowerDefense.Save;
using UnityEngine;

namespace TowerDefense.Tests
{
    public sealed class ProgressionServiceTests
    {
        [Test]
        public void TryPurchase_RequiresPrerequisite()
        {
            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            tree.nodes = new[]
            {
                new SkillNodeDefinition
                {
                    id = "core",
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 10) }
                },
                new SkillNodeDefinition
                {
                    id = "tower_limit",
                    prerequisiteNodeIds = new[] { "core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 10) }
                }
            };

            var profile = new PlayerProfile();
            profile.AddCurrency(CurrencyType.KillEssence, 25);
            var progression = new ProgressionService(tree, profile);

            Assert.False(progression.CanPurchase("tower_limit"));
            Assert.True(progression.TryPurchase("core"));
            Assert.True(progression.CanPurchase("tower_limit"));
        }

        [Test]
        public void TryPurchase_SpendsCurrencyAndAddsEffect()
        {
            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            tree.nodes = new[]
            {
                new SkillNodeDefinition
                {
                    id = "lives",
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 20) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.BaseLivesFlat, value = 3f } }
                }
            };

            var profile = new PlayerProfile();
            profile.AddCurrency(CurrencyType.KillEssence, 25);
            var progression = new ProgressionService(tree, profile);

            Assert.True(progression.TryPurchase("lives"));
            Assert.That(profile.GetCurrency(CurrencyType.KillEssence), Is.EqualTo(5));
            Assert.That(progression.GetEffectTotal(UpgradeEffectType.BaseLivesFlat), Is.EqualTo(3f));
        }

        [Test]
        public void TryPurchase_AllowsMultipleRanksUntilMaxRank()
        {
            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            tree.nodes = new[]
            {
                new SkillNodeDefinition
                {
                    id = "damage",
                    maxRanks = 3,
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 10) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.ActiveWeaponDamagePercent, value = 2f } }
                }
            };

            var profile = new PlayerProfile();
            profile.AddCurrency(CurrencyType.KillEssence, 40);
            var progression = new ProgressionService(tree, profile);

            Assert.True(progression.TryPurchase("damage"));
            Assert.True(progression.TryPurchase("damage"));
            Assert.True(progression.TryPurchase("damage"));
            Assert.False(progression.CanPurchase("damage"));
            Assert.That(progression.GetPurchasedRank("damage"), Is.EqualTo(3));
            Assert.That(profile.GetCurrency(CurrencyType.KillEssence), Is.EqualTo(10));
            Assert.That(progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponDamagePercent), Is.EqualTo(6f));
        }

        [Test]
        public void RefundAndResetPurchasedUpgrades_ReturnsCostsAndClearsPurchases()
        {
            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            tree.nodes = new[]
            {
                new SkillNodeDefinition
                {
                    id = "lives",
                    costs = new[]
                    {
                        new CurrencyAmount(CurrencyType.KillEssence, 20),
                        new CurrencyAmount(CurrencyType.VictorySigil, 1)
                    }
                }
            };

            var profile = new PlayerProfile();
            profile.AddCurrency(CurrencyType.KillEssence, 25);
            profile.AddCurrency(CurrencyType.VictorySigil, 2);
            var progression = new ProgressionService(tree, profile);

            Assert.True(progression.TryPurchase("lives"));
            progression.RefundAndResetPurchasedUpgrades();

            Assert.That(profile.GetCurrency(CurrencyType.KillEssence), Is.EqualTo(25));
            Assert.That(profile.GetCurrency(CurrencyType.VictorySigil), Is.EqualTo(2));
            Assert.False(progression.IsPurchased("lives"));
        }
    }
}
