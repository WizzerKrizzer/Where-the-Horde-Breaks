using TowerDefense.Data;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class AlliedUnitActor : MonoBehaviour, ICombatTarget
    {
        private EnemyManager enemies;
        private TowerActor owner;
        private TowerDefinition definition;
        private readonly List<EnemyActor> blockers = new();
        private Vector3 rallyPoint;
        private float health;
        private float attackCooldown;

        public Vector3 Position => transform.position;
        public bool IsAlive => health > 0f && gameObject.activeSelf;
        public CombatTargetKind TargetKind => CombatTargetKind.AlliedUnit;
        public float CombatRadius => definition != null && definition.barracksUnitType == AlliedUnitType.Paladin ? 0.72f : 0.55f;
        public float BlockCapacity => definition != null ? Mathf.Max(0f, definition.alliedUnitBlockCapacity) : 0f;
        public float CurrentBlockedMass => GetBlockedMass();

        public void Initialize(TowerActor ownerTower, TowerDefinition towerDefinition, EnemyManager enemyManager, Vector3 position)
        {
            owner = ownerTower;
            definition = towerDefinition;
            enemies = enemyManager;
            health = Mathf.Max(1f, towerDefinition.alliedUnitHealth);
            attackCooldown = Random.Range(0f, Mathf.Max(0.1f, towerDefinition.alliedUnitAttackInterval));
            transform.position = position;
            rallyPoint = enemies != null ? enemies.GetNearestPathPosition(position) : position;
            transform.localScale = GetScale(towerDefinition);
            GetComponent<Renderer>().material = BootstrapMaterials.Get(GetColor(towerDefinition));
            enemies.RegisterCombatTarget(this);
            gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            ReleaseAllBlockers();
            enemies?.UnregisterCombatTarget(this);
        }

        private void Update()
        {
            if (!IsAlive || enemies == null || definition == null)
            {
                return;
            }

            attackCooldown -= Time.deltaTime;
            var target = enemies.GetNearestEnemy(transform.position, Mathf.Max(definition.alliedUnitAggroRange, definition.alliedUnitRange), definition.alliedUnitCanHitFlying);
            if (target == null)
            {
                MoveToward(rallyPoint);
                return;
            }

            var attackRange = Mathf.Max(definition.alliedUnitRange, CombatRadius + target.Definition.visualScale * 0.5f + 0.2f);
            if (!IsWithinXzRange(target.transform.position, attackRange))
            {
                MoveToward(target.transform.position);
                return;
            }

            if (attackCooldown > 0f)
            {
                return;
            }

            var appliedDamage = target.ApplyDamage(definition.alliedUnitDamage);
            owner?.RecordDamage(appliedDamage);
            attackCooldown = Mathf.Max(0.1f, definition.alliedUnitAttackInterval);
        }

        public void TakeDamage(float damage, EnemyActor source)
        {
            if (!IsAlive)
            {
                return;
            }

            health -= Mathf.Max(0f, damage - definition.alliedUnitDefense);
            if (health > 0f)
            {
                return;
            }

            health = 0f;
            ReleaseAllBlockers();
            if (source != null && source.Definition != null && source.Definition.infectsAllies)
            {
                enemies?.SpawnConvertedEnemy(source.Definition, transform.position);
            }

            enemies?.UnregisterCombatTarget(this);
            owner?.NotifyAlliedUnitLost(this);
            Destroy(gameObject);
        }

        public bool TryAddBlocker(EnemyActor enemy)
        {
            if (enemy == null)
            {
                return false;
            }

            if (blockers.Contains(enemy))
            {
                return true;
            }

            var enemyMass = enemy.Definition != null ? enemy.Definition.mass : 1f;
            if (CurrentBlockedMass + enemyMass > BlockCapacity)
            {
                return false;
            }

            blockers.Add(enemy);
            return true;
        }

        public void RemoveBlocker(EnemyActor enemy)
        {
            blockers.Remove(enemy);
        }

        private void MoveToward(Vector3 destination)
        {
            var current = transform.position;
            destination.y = current.y;
            if (IsWithinXzRange(destination, 0.08f))
            {
                return;
            }

            transform.position = Vector3.MoveTowards(current, destination, definition.alliedUnitMoveSpeed * Time.deltaTime);
        }

        private bool IsWithinXzRange(Vector3 position, float range)
        {
            var offset = position - transform.position;
            var distanceSq = offset.x * offset.x + offset.z * offset.z;
            return distanceSq <= range * range;
        }

        private float GetBlockedMass()
        {
            for (var i = blockers.Count - 1; i >= 0; i--)
            {
                if (blockers[i] == null || !blockers[i].IsAlive)
                {
                    blockers.RemoveAt(i);
                }
            }

            var mass = 0f;
            foreach (var enemy in blockers)
            {
                mass += enemy.Definition != null ? enemy.Definition.mass : 1f;
            }

            return mass;
        }

        private void ReleaseAllBlockers()
        {
            blockers.Clear();
        }

        private static Vector3 GetScale(TowerDefinition towerDefinition)
        {
            switch (towerDefinition.barracksUnitType)
            {
                case AlliedUnitType.Archer:
                    return new Vector3(0.32f, 0.7f, 0.32f);
                case AlliedUnitType.Paladin:
                    return new Vector3(0.58f, 0.9f, 0.58f);
                default:
                    return new Vector3(0.45f, 0.75f, 0.45f);
            }
        }

        private static Color GetColor(TowerDefinition towerDefinition)
        {
            switch (towerDefinition.barracksUnitType)
            {
                case AlliedUnitType.Archer:
                    return new Color(0.75f, 0.95f, 0.45f);
                case AlliedUnitType.Paladin:
                    return new Color(0.95f, 0.92f, 0.55f);
                default:
                    return new Color(0.62f, 0.72f, 0.95f);
            }
        }
    }
}
