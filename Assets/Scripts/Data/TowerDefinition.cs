using UnityEngine;

namespace TowerDefense.Data
{
    [CreateAssetMenu(menuName = "Tower Defense/Tower Definition")]
    public sealed class TowerDefinition : ScriptableObject
    {
        public string id = "tower";
        public string displayName = "Tower";
        [TextArea(2, 4)]
        public string shortDescription = "A defensive tower.";
        [TextArea(2, 4)]
        public string weaknessDescription = "Weaknesses have not been defined yet.";
        public TowerRole role = TowerRole.ArcherLine;
        public int eraIndex;
        public int perTypeLimit = 6;
        public float range = 5f;
        public float damage = 3f;
        public float fireInterval = 0.65f;
        public float projectileSpeed = 18f;
        public ProjectilePattern projectilePattern = ProjectilePattern.Direct;
        public float splashRadius;
        public float knockbackDistance;
        public float arcFlightTimeMultiplier = 1f;
        public bool appliesFire;
        public float fireDamagePerTick;
        public float fireTicksPerSecond = 1f;
        public int fireMaxStacks = 1;
        public float fireDuration = 3f;
        public Color color = Color.white;
        public TowerDefinition nextEvolution;
    }
}
