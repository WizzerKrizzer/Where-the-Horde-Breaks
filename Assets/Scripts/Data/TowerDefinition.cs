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
        [Range(0f, 1f)]
        public float aimAssistStrength;
        public bool canHitFlying;
        public int pierce;
        [Range(0f, 1f)]
        public float doubleShotChance;
        // Slinger specials, set from the skill tree each run (0 = not bought). TEMPORARY numbers.
        // Stun: chance per hit (0-1) and seconds.
        public float stunChance;
        public float stunSeconds;
        // Ricochet: goblins the stone bounces on to after the hit, each for this share of it.
        public int ricochetBounces;
        public float ricochetDamageFraction;
        // Shrapnel: goblins the pebbles hit in a cone past the target, each for this share of it.
        public int shrapnelTargets;
        public float shrapnelDamageFraction;
        // Spear Thrower specials, set from the skill tree each run. TEMPORARY numbers.
        // Each goblin a spear passes through takes this share of what the one before took.
        public float pierceDamageKeep = 1f;
        // Damage times this against heavy enemies (Orc Brutes, the boss).
        public float heavyDamageMultiplier = 1f;
        public float poisonDamagePerSecond;
        public float poisonSeconds;
        // Mammoth specials. Armour and bought health add to beastHealth; the armour is lost first.
        public float beastArmor;
        public float beastExtraHealth;
        // Share (0-0.9) of the slowdown a crowd causes that the charge ignores.
        public float beastCrowdResistance;
        public int riderCount;
        public float riderDamage;
        // Rage: stomps this much faster and harder (0.3 = 30%) below this share of its health.
        public float rageBonus;
        public float rageThreshold;
        // Cave specials. Share (0-0.9) of each blow the cavemen's shields stop, and flat armour.
        public float alliedUnitDamageReduction;
        public float alliedUnitBonusArmor;
        // The chief: the cavemen near him deal this much more (0.25 = 25%); he has this much more
        // health. Both 0 = no chief.
        public float chiefAuraBonus;
        public float chiefHealthBonus;
        public float torchBurnDamagePerSecond;
        public float torchBurnSeconds;
        public ProjectilePattern projectilePattern = ProjectilePattern.Direct;
        public float splashRadius;
        public float knockbackDistance;
        // How high knocked enemies are thrown: upward speed per unit of knockback speed. 0 keeps
        // them on the ground; thrown enemies fly over the crowd (HordeSimulation.compute).
        public float knockbackLaunch;
        // Charge towers (Tamed Mammoth): every unit of enemy mass the charge runs into takes this
        // much off its speed (m/s); once it has slowed to a walk it stops and fights where it
        // stands, with this much health and room for this much enemy mass to fight it at once.
        public float chargeSpeedLossPerMass = 0.015f;
        public float beastHealth = 200f;
        public float beastBlockCapacity = 8f;
        // The charge pushes and throws only enemies this heavy or lighter (Goblin Runners, not Orc
        // Brutes) until pushesHeavy is unlocked by an upgrade; 0 pushes every weight. TEMPORARY.
        public float chargeMaxPushMass = 2.5f;
        public bool pushesHeavy;
        // The shot flies on along its line past the target, through everything in its way
        // (Ballista, Spear Throwers); how many it can hit is pierce + 1.
        public bool flyThroughShot;
        // Thrown spears (Spear Throwers, user 2026-11-08): the hunters on the rock take turns, each
        // throw raises a dust cloud at the thrower's feet, the spear knocks the goblins it passes
        // through along its line (knockbackDistance) and sticks in the ground where it stops.
        public bool throwsSpears;
        // How many hunters stand on the tower and take turns throwing (Spear Throwers: 3).
        public int crewSize;
        // The targeting a newly built tower starts with; the player can still change it.
        public TowerTargetingMode defaultTargetingMode = TowerTargetingMode.First;
        // Charge towers (TowerBehavior.Charge): how far along the road the beast runs, how fast,
        // and how wide it hits. damage is what one charge deals to each enemy it runs over.
        public float chargeDistance = 14f;
        public float chargeSpeed = 9f;
        public float chargeRadius = 1.7f;
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
