using System.Linq;
using TowerDefense.Data;
using TowerDefense.Save;

namespace TowerDefense.Progression
{
    public sealed class ProgressionService
    {
        private readonly SkillTreeDefinition tree;
        private readonly PlayerProfile profile;

        public ProgressionService(SkillTreeDefinition skillTree, PlayerProfile playerProfile)
        {
            tree = skillTree;
            profile = playerProfile;
        }

        public bool IsPurchased(string nodeId)
        {
            return GetPurchasedRank(nodeId) > 0;
        }

        public int GetPurchasedRank(string nodeId)
        {
            var node = FindNode(nodeId);
            if (node != null && node.startsUnlocked)
            {
                return GetMaxRank(node);
            }

            return profile.purchasedUpgradeIds.Count(id => id == nodeId);
        }

        public int GetMaxRank(string nodeId)
        {
            var node = FindNode(nodeId);
            return GetMaxRank(node);
        }

        public bool CanPurchase(string nodeId)
        {
            var node = FindNode(nodeId);
            if (node == null || node.startsUnlocked || GetPurchasedRank(nodeId) >= GetMaxRank(node))
            {
                return false;
            }

            if (node.prerequisiteNodeIds != null && node.prerequisiteNodeIds.Any(id => !IsPurchased(id)))
            {
                return false;
            }

            var currentCosts = GetCurrentCosts(nodeId);
            return currentCosts == null || currentCosts.All(cost => profile.GetCurrency(cost.currency) >= cost.amount);
        }

        public bool TryPurchase(string nodeId)
        {
            if (!CanPurchase(nodeId))
            {
                return false;
            }

            var node = FindNode(nodeId);
            var currentCosts = GetCurrentCosts(nodeId);
            if (currentCosts != null)
            {
                foreach (var cost in currentCosts)
                {
                    profile.TrySpend(cost);
                }
            }

            profile.purchasedUpgradeIds.Add(nodeId);
            return true;
        }

        public bool CanRefundLatestRank(string nodeId)
        {
            var node = FindNode(nodeId);
            var currentRank = GetPurchasedRank(nodeId);
            if (node == null || node.startsUnlocked || currentRank <= 0)
            {
                return false;
            }

            if (currentRank > 1)
            {
                return true;
            }

            return tree.nodes == null || !tree.nodes.Any(other =>
                other?.prerequisiteNodeIds != null &&
                other.prerequisiteNodeIds.Contains(nodeId) &&
                IsPurchased(other.id));
        }

        public bool TryRefundLatestRank(string nodeId)
        {
            if (!CanRefundLatestRank(nodeId))
            {
                return false;
            }

            var node = FindNode(nodeId);
            var refundedRank = GetPurchasedRank(nodeId) - 1;
            var costs = ScaleCosts(node.costs, node.costGrowthMultiplier, refundedRank);
            foreach (var cost in costs)
            {
                profile.AddCurrency(cost.currency, cost.amount);
            }

            var purchaseIndex = profile.purchasedUpgradeIds.LastIndexOf(nodeId);
            if (purchaseIndex < 0)
            {
                return false;
            }

            profile.purchasedUpgradeIds.RemoveAt(purchaseIndex);
            return true;
        }

        public SkillNodeDefinition[] GetNodes()
        {
            return tree.nodes ?? System.Array.Empty<SkillNodeDefinition>();
        }

        public CurrencyAmount[] GetCurrentCosts(string nodeId)
        {
            var node = FindNode(nodeId);
            if (node?.costs == null)
            {
                return System.Array.Empty<CurrencyAmount>();
            }

            return ScaleCosts(node.costs, node.costGrowthMultiplier, GetPurchasedRank(nodeId));
        }

        public void RefundAndResetPurchasedUpgrades()
        {
            var purchaseCounts = profile.purchasedUpgradeIds.GroupBy(id => id);
            foreach (var purchaseCount in purchaseCounts)
            {
                var node = FindNode(purchaseCount.Key);
                if (node?.costs == null)
                {
                    continue;
                }

                for (var rank = 0; rank < purchaseCount.Count(); rank++)
                {
                    var costs = ScaleCosts(node.costs, node.costGrowthMultiplier, rank);
                    foreach (var cost in costs)
                    {
                        profile.AddCurrency(cost.currency, cost.amount);
                    }
                }
            }

            profile.purchasedUpgradeIds.Clear();
        }

        public float GetEffectTotal(UpgradeEffectType type, string targetId = null)
        {
            var total = 0f;
            foreach (var nodeId in profile.purchasedUpgradeIds)
            {
                var node = FindNode(nodeId);
                if (node == null || node.startsUnlocked)
                {
                    continue;
                }

                total += SumEffect(node, type, targetId, 1);
            }

            // A node that starts unlocked is owned at every rank without being bought (the Slingers
            // and the rock at the start of the game).
            if (tree.nodes != null)
            {
                foreach (var node in tree.nodes)
                {
                    if (node != null && node.startsUnlocked)
                    {
                        total += SumEffect(node, type, targetId, GetMaxRank(node));
                    }
                }
            }

            return total;
        }

        private static float SumEffect(SkillNodeDefinition node, UpgradeEffectType type, string targetId, int ranks)
        {
            var total = 0f;
            if (node.effects == null)
            {
                return total;
            }

            foreach (var effect in node.effects)
            {
                if (effect.type == type && (string.IsNullOrEmpty(effect.targetId) || effect.targetId == targetId))
                {
                    total += effect.value * ranks;
                }
            }

            return total;
        }

        private static int GetMaxRank(SkillNodeDefinition node)
        {
            return node == null ? 0 : System.Math.Max(1, node.maxRanks);
        }

        private static CurrencyAmount[] ScaleCosts(CurrencyAmount[] baseCosts, float growthMultiplier, int purchasedRank)
        {
            if (baseCosts == null || baseCosts.Length == 0)
            {
                return System.Array.Empty<CurrencyAmount>();
            }

            var multiplier = (float)System.Math.Pow(System.Math.Max(1f, growthMultiplier), System.Math.Max(0, purchasedRank));
            var scaled = new CurrencyAmount[baseCosts.Length];
            for (var i = 0; i < baseCosts.Length; i++)
            {
                scaled[i] = new CurrencyAmount(baseCosts[i].currency, System.Math.Max(1, (int)System.Math.Ceiling(baseCosts[i].amount * multiplier)));
            }

            return scaled;
        }

        private SkillNodeDefinition FindNode(string nodeId)
        {
            return tree.nodes?.FirstOrDefault(node => node.id == nodeId);
        }
    }
}
