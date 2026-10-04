using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>
    /// Legacy serialized schema retained so existing wave assets still load.
    /// Runtime crowd motion is configured exclusively by CrowdSolverProfile.
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
    }
}
