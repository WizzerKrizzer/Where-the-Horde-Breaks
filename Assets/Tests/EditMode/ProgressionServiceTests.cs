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
        public void StartsUnlockedNode_CountsAsPurchasedPrerequisite()
        {
            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            tree.nodes = new[]
            {
                new SkillNodeDefinition
                {
                    id = "volley_core",
                    maxRanks = 1,
                    startsUnlocked = true
                },
                new SkillNodeDefinition
                {
                    id = "volley_damage",
                    prerequisiteNodeIds = new[] { "volley_core" },
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 10) }
                }
            };

            var profile = new PlayerProfile();
            profile.AddCurrency(CurrencyType.KillEssence, 10);
            var progression = new ProgressionService(tree, profile);

            Assert.True(progression.IsPurchased("volley_core"));
            Assert.That(progression.GetPurchasedRank("volley_core"), Is.EqualTo(1));
            Assert.False(progression.CanPurchase("volley_core"));
            Assert.True(progression.CanPurchase("volley_damage"));
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
            profile.AddCurrency(CurrencyType.KillEssence, 60);
            var progression = new ProgressionService(tree, profile);

            Assert.That(progression.GetCurrentCosts("damage")[0].amount, Is.EqualTo(10));
            Assert.True(progression.TryPurchase("damage"));
            Assert.That(progression.GetCurrentCosts("damage")[0].amount, Is.EqualTo(15));
            Assert.True(progression.TryPurchase("damage"));
            Assert.That(progression.GetCurrentCosts("damage")[0].amount, Is.EqualTo(23));
            Assert.True(progression.TryPurchase("damage"));
            Assert.False(progression.CanPurchase("damage"));
            Assert.That(progression.GetPurchasedRank("damage"), Is.EqualTo(3));
            Assert.That(profile.GetCurrency(CurrencyType.KillEssence), Is.EqualTo(12));
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

        [Test]
        public void RefundAndResetPurchasedUpgrades_ReturnsScaledRankCosts()
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
            profile.AddCurrency(CurrencyType.KillEssence, 60);
            var progression = new ProgressionService(tree, profile);

            progression.TryPurchase("damage");
            progression.TryPurchase("damage");
            progression.TryPurchase("damage");
            progression.RefundAndResetPurchasedUpgrades();

            Assert.That(profile.GetCurrency(CurrencyType.KillEssence), Is.EqualTo(60));
            Assert.That(progression.GetPurchasedRank("damage"), Is.EqualTo(0));
        }

        [Test]
        public void GetEffectTotal_FiltersPerTypeTowerLimitByTarget()
        {
            var tree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
            tree.nodes = new[]
            {
                new SkillNodeDefinition
                {
                    id = "archer_limit",
                    maxRanks = 2,
                    costs = new[] { new CurrencyAmount(CurrencyType.KillEssence, 1) },
                    effects = new[] { new UpgradeEffect { type = UpgradeEffectType.PerTypeTowerLimitFlat, targetId = "archer", value = 1f } }
                }
            };

            var profile = new PlayerProfile();
            profile.AddCurrency(CurrencyType.KillEssence, 5);
            var progression = new ProgressionService(tree, profile);

            progression.TryPurchase("archer_limit");
            progression.TryPurchase("archer_limit");

            Assert.That(progression.GetEffectTotal(UpgradeEffectType.PerTypeTowerLimitFlat, "archer"), Is.EqualTo(2f));
            Assert.That(progression.GetEffectTotal(UpgradeEffectType.PerTypeTowerLimitFlat, "ballista"), Is.EqualTo(0f));
        }
    }
}
