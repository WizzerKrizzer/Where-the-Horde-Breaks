using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>
    /// Shared, inspectable crowd-motion tuning. The reference preset models a
    /// flowing horde: bodies brake behind a contact instead of oscillating
    /// through it, while explosions retain full physical freedom.
    /// </summary>
    [CreateAssetMenu(menuName = "Tower Defense/Horde Physics Profile")]
    public sealed class HordePhysicsProfile : ScriptableObject
    {
        [Min(30)] public int fixedSimulationHz = 60;
        [Range(0.55f, 1.5f)] public float contactDiameterMultiplier = 1f;
        [Range(0.9f, 1.35f)] public float spritePackingScale = 1f;
        [Min(0.1f)] public float flowAcceleration = 2.2f;
        [Min(0.1f)] public float collisionAcceleration = 14f;
        [Min(0.01f)] public float velocityDamping = 0.3f;
        [Range(0f, 1f)] public float lateralContactResponse = 0.62f;
        [Min(0.01f)] public float externalForceRecovery = 0.42f;
        [Range(1, 32)] public int contactIterations = 8;
        public bool useReferenceCrowdRules = true;

        public static HordePhysicsProfile CreateOrcReference()
        {
            var profile = CreateInstance<HordePhysicsProfile>();
            profile.name = "Orc Reference Horde Physics";
            profile.hideFlags = HideFlags.DontSave;
            profile.fixedSimulationHz = 60;
            // The sprite card is wider than its old 0.70x collision envelope.
            // This produces a 0.38-unit physical diameter for the Level 2
            // runner: clear across its painted width and only slightly shared
            // along its longer direction of travel.
            profile.contactDiameterMultiplier = 1.34f;
            profile.spritePackingScale = 1f;
            // A body needs roughly one second to build back to cruising speed.
            // It must not regain full movement on the first frame after a wall,
            // contact, or explosion has taken that momentum away.
            profile.flowAcceleration = 2.2f;
            profile.collisionAcceleration = 14f;
            profile.velocityDamping = 0.3f;
            profile.lateralContactResponse = 0.62f;
            profile.externalForceRecovery = 0.42f;
            // Contact velocity is the primary queue mechanism. Eight bounded
            // projections only remove residual penetration; more passes turn a
            // Jacobi crowd solve into visible position hopping at close zoom.
            profile.contactIterations = 8;
            profile.useReferenceCrowdRules = true;
            return profile;
        }
    }
}
