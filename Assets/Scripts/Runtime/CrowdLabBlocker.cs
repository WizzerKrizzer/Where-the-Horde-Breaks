using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// Minimal physical blocker used only by CrowdLab scenarios. It exercises
    /// the exact dynamic-target adapter used by barriers without pulling tower
    /// art, targeting, or level setup into deterministic solver tests.
    /// </summary>
    public sealed class CrowdLabBlocker : MonoBehaviour, ICombatTarget, IOrientedCombatTarget
    {
        private float health = 100000f;
        private bool isAlive = true;
        private float radius = 0.62f;
        private float halfLength;
        private float halfDepth = 0.62f;

        public Vector3 Position => transform.position;
        public bool IsAlive => isAlive;
        public CombatTargetKind TargetKind => CombatTargetKind.Barrier;
        public float CombatRadius => radius;
        public float BlockCapacity => 100000f;
        public float CurrentBlockedMass => 0f;
        public float CurrentHealth => health;
        public float MaximumHealth => 100000f;
        public float Armor => 0f;
        public float PhysicalResistance => 0f;
        public float FireResistance => 0f;
        public float SlowResistance => 0f;
        public float ThornsDamage => 0f;
        public Vector3 CombatAxis => transform.right;
        public float CombatHalfLength => halfLength;
        public float CombatHalfDepth => halfDepth;

        public void Configure(Vector3 position, float contactRadius, float segmentHalfLength = 0f)
        {
            transform.position = position;
            radius = Mathf.Max(0.05f, contactRadius);
            halfDepth = radius;
            halfLength = Mathf.Max(0f, segmentHalfLength);
            isAlive = true;
            health = MaximumHealth;
        }

        public void Release() => isAlive = false;

        public bool TryAddBlocker(EnemyActor enemy) => isAlive;
        public void RemoveBlocker(EnemyActor enemy) { }

        public void TakeDamage(float damage, EnemyActor source)
        {
            if (!isAlive) return;
            health = Mathf.Max(0f, health - Mathf.Max(0f, damage));
            if (health <= 0f) isAlive = false;
        }

        public void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition)
        {
            health = Mathf.Max(0f, authoritativeHealth);
            isAlive = !destroyed && health > 0f;
        }
    }
}
