using UnityEngine;

namespace TowerDefense.Data
{
    /// <summary>
    /// The shared body envelope and fixed-step contract used by every GPU crowd.
    /// Individual CPU actors also use the body envelope. Gameplay may vary
    /// desired speed and mass per enemy, but it must not change contact quality by
    /// selecting a level-specific solver path.
    /// </summary>
    [CreateAssetMenu(menuName = "Tower Defense/Crowd Solver Profile")]
    public sealed class CrowdSolverProfile : ScriptableObject
    {
        public const int FixedSimulationHz = 60;
        public const int PositionIterations = 8;
        public const float DefaultBodyRadiusPerVisualScale = 0.56f;

        // These values are retained only to deserialize the first migration
        // assets. A solver profile may tune material response, never timestep
        // or iteration count: those are the deterministic global contract.
        [HideInInspector] public int fixedSimulationHz = FixedSimulationHz;
        [HideInInspector] public int positionIterations = PositionIterations;
        [Min(0f)] public float contactCompliance = 0f;
        [Min(0f)] public float wallCompliance = 0f;
        [Min(0.1f)] public float propulsionAcceleration = 0.5f;
        [Min(0.01f)] public float velocityDamping = 0.3f;
        [Min(0.1f)] public float maximumSpeed = 7f;
        [Min(0.005f)] public float maximumTravelPerTick = 0.12f;
        [Min(0.05f)] public float bodyDiameterPerEnemyScale = 0.56f;
        // Scale sizes the entire render card. Only its painted torso occupies
        // the contact disk; transparent padding, ears and weapons are not walls.
        [Range(0.1f, 1f)] public float bodyRadiusPerVisualScale = DefaultBodyRadiusPerVisualScale;

        private static CrowdSolverProfile defaultProfile;

        public float FixedStep => 1f / FixedSimulationHz;

        public float GetBodyRadius(float visualScale)
        {
            return Mathf.Max(0f, visualScale) * Mathf.Clamp(bodyRadiusPerVisualScale, 0.1f, 1f);
        }

        public static CrowdSolverProfile Default
        {
            get
            {
                if (defaultProfile == null)
                {
                    defaultProfile = CreateDefault();
                }
                return defaultProfile;
            }
        }

        public static CrowdSolverProfile CreateDefault()
        {
            var profile = CreateInstance<CrowdSolverProfile>();
            profile.name = "Default GPU Crowd Solver";
            profile.hideFlags = HideFlags.DontSave;
            return profile;
        }
    }
}
