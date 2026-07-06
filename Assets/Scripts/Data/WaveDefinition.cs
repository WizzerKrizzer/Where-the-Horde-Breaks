using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Wave Definition")]
    public sealed class WaveDefinition : ScriptableObject
    {
        public string id = "wave";
        public int totalEnemyCount = 200;
        public float spawnInterval = 0.5f;
        public int randomSpawnBurstMin;
        public int randomSpawnBurstMax;
        public int[] spawnBurstPattern;
        public WaveEntry[] entries;
    }
}
