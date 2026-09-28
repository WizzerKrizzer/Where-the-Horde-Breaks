using UnityEngine;

namespace TowerDefense.Data
{
    public enum HordeNavigationMode
    {
        Corridor,
        OpenField
    }

    [CreateAssetMenu(menuName = "Tower Defense/Wave Definition")]
    public sealed class WaveDefinition : ScriptableObject
    {
        public string id = "wave";
        public int totalEnemyCount = 200;
        public float spawnInterval = 0.5f;
        public int randomSpawnBurstMin;
        public int randomSpawnBurstMax;
        public int[] spawnBurstPattern;
        public bool useEndpointSeeking;
        public bool enforceSpawnClearance;
        public bool stationaryArena;
        public bool randomArenaMovement;
        public bool centerSeekingArena;
        [Range(0.1f, 2f)] public float enemyScaleMultiplier = 1f;
        [Range(0.5f, 2f)] public float enemyInertiaMultiplier = 1f;
        [Range(0.5f, 3f)] public float enemyMassMultiplier = 1f;
        [Range(0.5f, 3f)] public float collisionPressureMultiplier = 1f;
        [Range(1f, 2f)] public float spawnPackingMultiplier = 1f;
        [Range(0.1f, 4f)] public float enemySpeedMultiplier = 1f;
        [Min(0f)] public float openingSpeedDuration;
        [Min(1f)] public float openingSpeedMultiplier = 1f;
        public float randomEnemySpeedMin;
        public float randomEnemySpeedMax;
        public bool colorEnemiesBySpeed;
        public float roadHalfWidth = 2.65f;
        [Tooltip("Corridor keeps the authored road as a hard boundary. Open Field makes the complete configured board walkable.")]
        public HordeNavigationMode hordeNavigationMode = HordeNavigationMode.Corridor;
        [Tooltip("World-space centre and dimensions of the walkable board used by Open Field mode.")]
        public Vector3 openFieldCenter;
        public Vector2 openFieldSize;
        [Min(0.25f), Tooltip("Half-width of the entrance and exit gates in Open Field mode.")]
        public float openFieldGateHalfWidth = 2.65f;
        [Tooltip("Optional locomotion tuning profile. Level 2 uses the Orc Reference profile.")]
        public HordePhysicsProfile hordePhysicsProfile;
        public WaveEntry[] entries;
    }
}
