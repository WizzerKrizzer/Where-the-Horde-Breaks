using TowerDefense.Runtime;
using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        public string id = "enemy";
        public string displayName = "Enemy";
        [TextArea(2, 4)]
        public string shortDescription = "A hostile creature marching toward the gate.";
        [TextArea(2, 4)]
        public string weaknessDescription = "Weaknesses have not been defined yet.";
        public EnemyRole role = EnemyRole.Runner;
        // The era it belongs to (EraId); each era has its own enemies.
        public int eraIndex;
        public float maxHealth = 10f;
        public float speed = 3f;
        public float mass = 1f;
        public float armor;
        [Range(0f, 0.95f)] public float physicalResistance;
        [Range(0f, 0.95f)] public float fireResistance;
        [Range(0f, 0.95f)] public float slowResistance;
        public float attackDamage = 2f;
        public float attackInterval = 0.9f;
        public float wallDamageMultiplier = 1f;
        public float alliedDamageMultiplier = 1f;
        public bool isFlying;
        // Nothing pushes or throws it: no mammoth, rock or knockback (the boss).
        public bool cannotBePushed;
        // Blockers (cavemen, the fighting mammoth) only slow it down; it strikes them as it walks
        // on (the boss).
        public bool passesBlockers;
        public bool healsEnemies;
        public float healAmount;
        public float healInterval = 2.5f;
        public float healRadius = 3f;
        public bool drainsAllies;
        public float drainHealMultiplier = 1f;
        public bool infectsAllies;
        public bool revivesOnce;
        public float reviveDelay = 3f;
        public int lifeDamage = 1;
        public int killReward = 1;
        public Color color = PlaceholderPalette.Grey(.299f);
        public float visualScale = 0.45f;
    }
}
