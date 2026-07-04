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
        void TakeDamage(float damage, EnemyActor source);
    }
}
