using System;
using UnityEngine;

namespace TowerDefense.Data
{
    [Serializable]
    public struct CurrencyAmount
    {
        public CurrencyType currency;
        public int amount;

        public CurrencyAmount(CurrencyType currency, int amount)
        {
            this.currency = currency;
            this.amount = amount;
        }
    }

    [Serializable]
    public struct UpgradeEffect
    {
        public UpgradeEffectType type;
        public string targetId;
        public float value;
    }

    [Serializable]
    public struct WaveEntry
    {
        public EnemyDefinition enemy;
        public int count;
        public float startTime;
        public float spawnInterval;
    }

    [Serializable]
    public struct ChallengeRule
    {
        public string id;
        public int maxTowers;
        public bool disallowActiveWeapon;
        [Range(0, 1)] public float minActiveDamageShare;
        public CurrencyAmount reward;
    }
}
