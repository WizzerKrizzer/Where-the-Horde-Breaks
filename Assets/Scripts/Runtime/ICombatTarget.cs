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
        bool TryAddBlocker(EnemyActor enemy);
        void RemoveBlocker(EnemyActor enemy);
        void TakeDamage(float damage, EnemyActor source);
    }
}
