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
            return profile.purchasedUpgradeIds.Contains(nodeId);
        }

        public bool CanPurchase(string nodeId)
        {
            var node = FindNode(nodeId);
            if (node == null || IsPurchased(nodeId))
            {
                return false;
            }

            if (node.prerequisiteNodeIds != null && node.prerequisiteNodeIds.Any(id => !IsPurchased(id)))
            {
                return false;
            }

            return node.costs == null || node.costs.All(cost => profile.GetCurrency(cost.currency) >= cost.amount);
        }

        public bool TryPurchase(string nodeId)
        {
            if (!CanPurchase(nodeId))
            {
                return false;
            }

            var node = FindNode(nodeId);
            if (node.costs != null)
            {
                foreach (var cost in node.costs)
                {
                    profile.TrySpend(cost);
                }
            }

            profile.purchasedUpgradeIds.Add(nodeId);
            return true;
        }

        public SkillNodeDefinition[] GetNodes()
        {
            return tree.nodes ?? System.Array.Empty<SkillNodeDefinition>();
        }

        public void RefundAndResetPurchasedUpgrades()
        {
            foreach (var nodeId in profile.purchasedUpgradeIds)
            {
                var node = FindNode(nodeId);
                if (node?.costs == null)
                {
                    continue;
                }

                foreach (var cost in node.costs)
                {
                    profile.AddCurrency(cost.currency, cost.amount);
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
                if (node?.effects == null)
                {
                    continue;
                }

                foreach (var effect in node.effects)
                {
                    if (effect.type == type && (string.IsNullOrEmpty(effect.targetId) || effect.targetId == targetId))
                    {
                        total += effect.value;
                    }
                }
            }

            return total;
        }

        private SkillNodeDefinition FindNode(string nodeId)
        {
            return tree.nodes?.FirstOrDefault(node => node.id == nodeId);
        }
    }
}
