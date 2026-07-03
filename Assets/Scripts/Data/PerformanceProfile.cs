using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Performance Profile")]
    public sealed class PerformanceProfile : ScriptableObject
    {
        public string id = "pc_default";
        public int maxDetailedEnemies = 1200;
        public int maxVisibleProjectiles = 600;
        public float distantEnemyUpdateInterval = 0.08f;
        [Range(0, 1)] public float effectDensity = 0.75f;
        public bool useSimplifiedFarSimulation = true;
    }
}
