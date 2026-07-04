using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class AlliedUnitActor : MonoBehaviour, ICombatTarget
    {
        private EnemyManager enemies;
        private TowerActor owner;
        private TowerDefinition definition;
        private float health;
        private float attackCooldown;

        public Vector3 Position => transform.position;
        public bool IsAlive => health > 0f && gameObject.activeSelf;
        public CombatTargetKind TargetKind => CombatTargetKind.AlliedUnit;

        public void Initialize(TowerActor ownerTower, TowerDefinition towerDefinition, EnemyManager enemyManager, Vector3 position)
        {
            owner = ownerTower;
            definition = towerDefinition;
            enemies = enemyManager;
            health = Mathf.Max(1f, towerDefinition.alliedUnitHealth);
            attackCooldown = Random.Range(0f, Mathf.Max(0.1f, towerDefinition.alliedUnitAttackInterval));
            transform.position = position;
            transform.localScale = GetScale(towerDefinition);
            GetComponent<Renderer>().material = BootstrapMaterials.Get(GetColor(towerDefinition));
            enemies.RegisterCombatTarget(this);
            gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            enemies?.UnregisterCombatTarget(this);
        }

        private void Update()
        {
            if (!IsAlive || enemies == null || definition == null)
            {
                return;
            }

            attackCooldown -= Time.deltaTime;
            if (attackCooldown > 0f)
            {
                return;
            }

            var target = enemies.GetNearestEnemy(transform.position, definition.alliedUnitRange, definition.alliedUnitCanHitFlying);
            if (target == null)
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
            if (source != null && source.Definition != null && source.Definition.infectsAllies)
            {
                enemies?.SpawnConvertedEnemy(source.Definition, transform.position);
            }

            enemies?.UnregisterCombatTarget(this);
            owner?.NotifyAlliedUnitLost(this);
            Destroy(gameObject);
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
