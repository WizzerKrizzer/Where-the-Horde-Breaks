using System;
using System.Collections.Generic;
using TowerDefense.Data;

namespace TowerDefense.Save
{
    [Serializable]
    public sealed class PlayerProfile
    {
        public List<CurrencyBalance> currencies = new();
        public List<string> unlockedLevelIds = new();
        public List<string> clearedLevelIds = new();
        public List<string> perfectClearedLevelIds = new();
        public List<string> purchasedUpgradeIds = new();
        public List<LevelTowerLayout> towerLayouts = new();

        public int GetCurrency(CurrencyType currency)
        {
            return currencies.Find(entry => entry.currency == currency)?.amount ?? 0;
        }

        public void AddCurrency(CurrencyType currency, int amount)
        {
            var entry = currencies.Find(balance => balance.currency == currency);
            if (entry == null)
            {
                currencies.Add(new CurrencyBalance { currency = currency, amount = amount });
            }
            else
            {
                entry.amount += amount;
            }
        }

        public bool TrySpend(CurrencyAmount cost)
        {
            if (GetCurrency(cost.currency) < cost.amount)
            {
                return false;
            }

            AddCurrency(cost.currency, -cost.amount);
            return true;
        }

        public LevelTowerLayout GetOrCreateLayout(string levelId)
        {
            var layout = towerLayouts.Find(record => record.levelId == levelId);
            if (layout != null)
            {
                return layout;
            }

            layout = new LevelTowerLayout { levelId = levelId };
            towerLayouts.Add(layout);
            return layout;
        }
    }

    [Serializable]
    public sealed class CurrencyBalance
    {
        public CurrencyType currency;
        public int amount;
    }

    [Serializable]
    public sealed class LevelTowerLayout
    {
        public string levelId;
        public List<TowerPlacementRecord> placements = new();
    }

    [Serializable]
    public sealed class TowerPlacementRecord
    {
        public string towerId;
        public float x;
        public float y;
        public float z;
    }
}
