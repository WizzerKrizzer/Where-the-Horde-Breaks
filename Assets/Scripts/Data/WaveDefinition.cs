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
        // A steady stream instead of a burst every spawnInterval (user, 2026-11-17): on average this
        // many a second, each after a random gap, so it never pulses. 0 keeps the bursts.
        [Min(0f)] public float continuousSpawnRate;
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
        // The authored speed is the speed they really walk (user, 2026-11-17). The crowd's slow
        // propulsion and its damping otherwise leave a lone walker at about 62% of it.
        public bool walkAtFullSpeed;
        // Each goblin runner gets its own health in this range (user, 2026-11-18: 8 to 14 on level 1;
        // later per era). 0 keeps the definition's health.
        [Min(0f)] public float runnerHealthMin;
        [Min(0f)] public float runnerHealthMax;
        // Along a road edge (user, 2026-11-17): a body pressed to the edge, after a turn say, keeps
        // to it for this many metres and then little by little walks back towards the middle of the
        // road. 0 leaves it on the edge.
        [Min(0f)] public float edgeWalkBeforeCentre;
        public float roadHalfWidth = 2.65f;
        [Tooltip("Corridor keeps the authored road as a hard boundary. Open Field makes the complete configured board walkable.")]
        public HordeNavigationMode hordeNavigationMode = HordeNavigationMode.Corridor;
        [Tooltip("World-space centre and dimensions of the walkable board used by Open Field mode.")]
        public Vector3 openFieldCenter;
        public Vector2 openFieldSize;
        [Min(0.25f), Tooltip("Half-width of the entrance and exit gates in Open Field mode.")]
        public float openFieldGateHalfWidth = 2.65f;
        [HideInInspector, Tooltip("Deprecated serialized data; the runtime always uses CrowdSolverProfile.Default.")]
        public HordePhysicsProfile hordePhysicsProfile;
        public WaveEntry[] entries;
    }
}
