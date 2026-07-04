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
        public TowerBehavior behavior = TowerBehavior.Projectile;
        public int eraIndex;
        public int perTypeLimit = 6;
        public float range = 5f;
        public float damage = 3f;
        public float fireInterval = 0.65f;
        public float projectileSpeed = 18f;
        public bool canHitFlying;
        public int pierce;
        [Range(0f, 1f)]
        public float doubleShotChance;
        public ProjectilePattern projectilePattern = ProjectilePattern.Direct;
        public float splashRadius;
        public float knockbackDistance;
        public float arcFlightTimeMultiplier = 1f;
        [Range(0f, 0.95f)]
        public float slowPercent;
        public float slowCapacity;
        public float health;
        public float thornsDamage;
        public AlliedUnitType barracksUnitType = AlliedUnitType.Knight;
        public int barracksCapacity = 1;
        public float barracksRespawnSeconds = 8f;
        public float alliedUnitHealth = 18f;
        public float alliedUnitDamage = 3f;
        public float alliedUnitDefense;
        public float alliedUnitAttackInterval = 0.8f;
        public float alliedUnitRange = 1.4f;
        public float alliedUnitMoveSpeed = 3.2f;
        public float alliedUnitAggroRange = 5.5f;
        public float alliedUnitBlockCapacity = 3f;
        public bool alliedUnitCanHitFlying;
        public int alliedUnitSlots = 1;
        public bool appliesFire;
        public float fireDamagePerTick;
        public float fireTicksPerSecond = 1f;
        public int fireMaxStacks = 1;
        public float fireDuration = 3f;
        public Color color = Color.white;
        public TowerDefinition nextEvolution;
    }
}
