using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Wave Definition")]
    public sealed class WaveDefinition : ScriptableObject
    {
        public string id = "wave";
        public int totalEnemyCount = 200;
        public WaveEntry[] entries;
    }
}
