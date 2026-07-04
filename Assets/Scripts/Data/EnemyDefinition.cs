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
        public EnemyRole role = EnemyRole.Runner;
        public float maxHealth = 10f;
        public float speed = 3f;
        public float mass = 1f;
        public int lifeDamage = 1;
        public int killReward = 1;
        public Color color = Color.red;
        public float visualScale = 0.45f;
    }
}
