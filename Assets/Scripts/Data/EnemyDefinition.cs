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
        public float maxHealth = 10f;
        public float speed = 3f;
        public float mass = 1f;
        public float attackDamage = 2f;
        public float attackInterval = 0.9f;
        public float wallDamageMultiplier = 1f;
        public float alliedDamageMultiplier = 1f;
        public bool isFlying;
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
        public Color color = Color.red;
        public float visualScale = 0.45f;
    }
}
