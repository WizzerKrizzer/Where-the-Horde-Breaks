using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public interface ICombatTarget
    {
        Vector3 Position { get; }
        bool IsAlive { get; }
        CombatTargetKind TargetKind { get; }
        float CombatRadius { get; }
        float BlockCapacity { get; }
        float CurrentBlockedMass { get; }
        float CurrentHealth { get; }
        float MaximumHealth { get; }
        float Armor { get; }
        float PhysicalResistance { get; }
        float FireResistance { get; }
        float SlowResistance { get; }
        float ThornsDamage { get; }
        bool TryAddBlocker(EnemyActor enemy);
        void RemoveBlocker(EnemyActor enemy);
        void TakeDamage(float damage, EnemyActor source);
        void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition);
    }

    public interface IOrientedCombatTarget
    {
        Vector3 CombatAxis { get; }
        float CombatHalfLength { get; }
        float CombatHalfDepth { get; }
    }
}
