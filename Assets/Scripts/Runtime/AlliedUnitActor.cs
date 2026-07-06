using TowerDefense.Data;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class AlliedUnitActor : MonoBehaviour, ICombatTarget
    {
        private static readonly List<AlliedUnitActor> AllUnits = new();
        private EnemyManager enemies;
        private TowerActor owner;
        private TowerDefinition definition;
        private readonly List<EnemyActor> blockers = new();
        private Vector3 rallyPoint;
        private Vector3 formationOffset;
        private float health;
        private float maxHealth;
        private float attackCooldown;
        private Transform healthFill;

        public Vector3 Position => transform.position;
        public bool IsAlive => health > 0f && gameObject.activeSelf;
        public CombatTargetKind TargetKind => CombatTargetKind.AlliedUnit;
        public float CombatRadius => definition != null && definition.barracksUnitType == AlliedUnitType.Paladin ? 0.72f : 0.55f;
        public float BlockCapacity => IsRangedArcher ? 0f : definition != null ? Mathf.Max(0f, definition.alliedUnitBlockCapacity) : 0f;
        public float CurrentBlockedMass => GetBlockedMass();
        private bool IsRangedArcher => definition != null && definition.barracksUnitType == AlliedUnitType.Archer;

        public void Initialize(TowerActor ownerTower, TowerDefinition towerDefinition, EnemyManager enemyManager, Vector3 position, int formationIndex)
        {
            owner = ownerTower;
            definition = towerDefinition;
            enemies = enemyManager;
            maxHealth = Mathf.Max(1f, towerDefinition.alliedUnitHealth);
            health = maxHealth;
            attackCooldown = Random.Range(0f, Mathf.Max(0.1f, towerDefinition.alliedUnitAttackInterval));
            transform.position = position;
            formationOffset = GetFormationOffset(formationIndex, towerDefinition);
            rallyPoint = enemies != null
                ? towerDefinition.barracksUnitType == AlliedUnitType.Archer ? enemies.GetPathSidePosition(position, 1.85f) + formationOffset : enemies.GetNearestPathPosition(position) + formationOffset
                : position;
            transform.localScale = GetScale(towerDefinition);
            GetComponent<Renderer>().material = BootstrapMaterials.Get(GetColor(towerDefinition));
            EnsureHealthBar();
            UpdateHealthBar();
            if (!IsRangedArcher)
            {
                enemies.RegisterCombatTarget(this);
            }
            gameObject.SetActive(true);
            if (!AllUnits.Contains(this))
            {
                AllUnits.Add(this);
            }
        }

        private void OnDestroy()
        {
            AllUnits.Remove(this);
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

            if (IsRangedArcher)
            {
                UpdateArcherCombat(target);
                return;
            }

            var attackRange = Mathf.Max(definition.alliedUnitRange, CombatRadius + target.Definition.visualScale * 0.5f + 0.2f);
            if (!IsWithinXzRange(target.transform.position, attackRange))
            {
                MoveToward(target.transform.position + formationOffset * 0.45f);
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

        private void UpdateArcherCombat(EnemyActor target)
        {
            MoveToward(rallyPoint);
            if (!IsWithinXzRange(rallyPoint, 0.15f) || !IsWithinXzRange(target.transform.position, definition.alliedUnitRange))
            {
                return;
            }

            if (attackCooldown > 0f)
            {
                return;
            }

            FireArrow(target);
            attackCooldown = Mathf.Max(0.1f, definition.alliedUnitAttackInterval);
        }

        private void FireArrow(EnemyActor target)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "AlliedArrow";
            go.transform.position = transform.position + Vector3.up * 0.65f;
            go.transform.localScale = new Vector3(0.08f, 0.08f, 0.34f);
            go.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(1f, 0.88f, 0.32f, 1f));
            var projectile = go.AddComponent<AlliedArrowProjectile>();
            projectile.Fire(owner, target, definition.alliedUnitDamage, 16f);
        }

        public void TakeDamage(float damage, EnemyActor source)
        {
            if (!IsAlive)
            {
                return;
            }

            var appliedDamage = Mathf.Max(0f, damage - definition.alliedUnitDefense);
            health -= appliedDamage;
            UpdateHealthBar();
            DamagePopup.Show(transform.position, appliedDamage, new Color(1f, 0.25f, 0.18f, 1f));
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
            destination += GetSeparationOffset();
            if (IsWithinXzRange(destination, 0.08f))
            {
                return;
            }

            transform.position = Vector3.MoveTowards(current, destination, definition.alliedUnitMoveSpeed * Time.deltaTime);
        }

        private Vector3 GetSeparationOffset()
        {
            var offset = Vector3.zero;
            for (var i = AllUnits.Count - 1; i >= 0; i--)
            {
                var other = AllUnits[i];
                if (other == null || other == this || !other.IsAlive)
                {
                    if (other == null)
                    {
                        AllUnits.RemoveAt(i);
                    }
                    continue;
                }

                var away = transform.position - other.transform.position;
                away.y = 0f;
                var distance = away.magnitude;
                if (distance <= 0.001f || distance > 0.72f)
                {
                    continue;
                }

                offset += away.normalized * (0.72f - distance);
            }

            return Vector3.ClampMagnitude(offset, 0.85f);
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
                    return new Vector3(0.24f, 0.52f, 0.24f);
                case AlliedUnitType.Paladin:
                    return new Vector3(0.42f, 0.72f, 0.42f);
                default:
                    return new Vector3(0.34f, 0.6f, 0.34f);
            }
        }

        private static Vector3 GetFormationOffset(int index, TowerDefinition towerDefinition)
        {
            var row = index / 3;
            var column = index % 3 - 1;
            var side = towerDefinition.barracksUnitType == AlliedUnitType.Archer ? 0.55f : 0.42f;
            var forward = towerDefinition.barracksUnitType == AlliedUnitType.Archer ? 0.42f : 0.34f;
            return new Vector3(column * side, 0f, row * forward);
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

        private void EnsureHealthBar()
        {
            if (healthFill != null)
            {
                return;
            }

            var root = new GameObject("AlliedHealthBar");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            background.name = "AlliedHealthBarBackground";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = Vector3.zero;
            background.transform.localScale = new Vector3(1.05f, 0.075f, 0.12f);
            background.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.025f, 0.03f, 0.04f, 1f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "AlliedHealthBarFill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.525f, 0.011f, 0f);
            fill.transform.localScale = new Vector3(1.05f, 0.085f, 0.14f);
            fill.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.35f, 0.88f, 1f, 1f));
            healthFill = fill.transform;
        }

        private void UpdateHealthBar()
        {
            if (healthFill == null)
            {
                return;
            }

            var normalizedHealth = Mathf.Clamp01(health / Mathf.Max(1f, maxHealth));
            healthFill.localScale = new Vector3(1.05f * normalizedHealth, 0.085f, 0.14f);
            healthFill.localPosition = new Vector3(-0.525f + 0.525f * normalizedHealth, 0.011f, 0f);
        }
    }
}
