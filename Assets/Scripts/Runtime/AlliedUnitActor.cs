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
        public float CurrentHealth => health;
        public float MaximumHealth => maxHealth;
        public float Armor => definition != null ? Mathf.Max(0f, definition.alliedUnitDefense + definition.alliedUnitBonusArmor) : 0f;
        // The cave's shields (a special): this share of every blow is stopped.
        public float PhysicalResistance => definition != null ? Mathf.Clamp(definition.alliedUnitDamageReduction, 0f, 0.9f) : 0f;
        // The cave's chief (a special): tougher, hits harder, and the cavemen near him hit harder.
        public bool IsChief { get; private set; }
        private const float ChiefDamageMultiplier = 1.5f;
        private const float ChiefAuraRadius = 4f;
        private const float ChiefScale = 1.3f;
        public float FireResistance => 0f;
        public float SlowResistance => 0f;
        public float ThornsDamage => 0f;
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
            if (IsRangedArcher && enemies.TryGetHordeTargetPosition(transform.position, Mathf.Max(definition.alliedUnitAggroRange, definition.alliedUnitRange), definition.alliedUnitCanHitFlying, TowerTargetingMode.Closest, out var hordeTarget))
            {
                UpdateArcherHordeCombat(hordeTarget);
                return;
            }

            if (!IsRangedArcher && enemies.TryGetHordeTargetSnapshot(transform.position, definition.alliedUnitAggroRange, definition.alliedUnitCanHitFlying, TowerTargetingMode.Closest, out var hordeIndex, out hordeTarget, out _))
            {
                UpdateMeleeHordeCombat(hordeIndex, hordeTarget);
                return;
            }

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

            var appliedDamage = target.ApplyDamage(definition.alliedUnitDamage * GetDamageMultiplier());
            owner?.RecordDamage(appliedDamage);
            attackCooldown = Mathf.Max(0.1f, definition.alliedUnitAttackInterval);
        }

        public void MakeChief()
        {
            IsChief = true;
            maxHealth *= 1f + Mathf.Max(0f, definition.chiefHealthBonus);
            health = maxHealth;
            transform.localScale *= ChiefScale;
            UpdateHealthBar();
        }

        // The chief hits harder himself, and lends the cavemen of his cave near him his strength.
        private float GetDamageMultiplier()
        {
            if (IsChief)
            {
                return ChiefDamageMultiplier;
            }

            if (definition.chiefAuraBonus <= 0f)
            {
                return 1f;
            }

            foreach (var unit in AllUnits)
            {
                if (unit != this && unit != null && unit.IsChief && unit.owner == owner && unit.IsAlive &&
                    (unit.transform.position - transform.position).sqrMagnitude <= ChiefAuraRadius * ChiefAuraRadius)
                {
                    return 1f + definition.chiefAuraBonus;
                }
            }

            return 1f;
        }

        // Torches (a special): the goblin a caveman strikes catches fire.
        private void LightTorchBurn(Vector3 targetPosition)
        {
            if (definition.torchBurnDamagePerSecond <= 0f || definition.torchBurnSeconds <= 0f)
            {
                return;
            }

            const float ticksPerSecond = 2f;
            enemies.DamageAndKnockbackInRadius(targetPosition, 0.6f, 0f, 0f, out _, null,
                definition.torchBurnDamagePerSecond / ticksPerSecond, ticksPerSecond, definition.torchBurnSeconds, 2);
        }

        private void UpdateMeleeHordeCombat(int targetIndex, Vector3 targetPosition)
        {
            var attackRange = Mathf.Max(definition.alliedUnitRange, CombatRadius + 0.48f);
            if (!IsWithinXzRange(targetPosition, attackRange))
            {
                MoveToward(targetPosition + formationOffset * 0.45f);
                return;
            }

            if (attackCooldown > 0f)
            {
                return;
            }

            // Strike the goblin it is already tracking. A fresh target query at the moment of the
            // blow (as before 2026-11-08) was never answered in time: GPU target answers go stale
            // after a few frames, and blows are 0.8 s apart, so the cavemen never hurt anyone.
            var appliedDamage = enemies.DamageHordeTarget(targetIndex, definition.alliedUnitDamage * GetDamageMultiplier());
            owner?.RecordDamage(appliedDamage);
            if (appliedDamage > 0f)
            {
                LightTorchBurn(targetPosition);
            }
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

        private void UpdateArcherHordeCombat(Vector3 targetPosition)
        {
            MoveToward(rallyPoint);
            if (!IsWithinXzRange(rallyPoint, 0.15f) || !IsWithinXzRange(targetPosition, definition.alliedUnitRange))
            {
                return;
            }

            if (attackCooldown > 0f)
            {
                return;
            }

            var queued = enemies.QueueHordeProjectile(
                transform.position + Vector3.up * 0.65f,
                targetPosition,
                0.22f,
                definition.alliedUnitDamage,
                0f,
                1,
                definition.alliedUnitCanHitFlying,
                splash: false);
            if (!queued)
            {
                return;
            }
            owner?.RecordDamage(definition.alliedUnitDamage);
            FireArrowVisual(targetPosition);
            attackCooldown = Mathf.Max(0.1f, definition.alliedUnitAttackInterval);
        }

        private void FireArrow(EnemyActor target)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "AlliedArrow";
            go.transform.position = transform.position + Vector3.up * 0.65f;
            go.transform.localScale = new Vector3(0.08f, 0.08f, 0.34f);
            go.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Grey(.852f, 1f));
            var projectile = go.AddComponent<AlliedArrowProjectile>();
            projectile.Fire(owner, target, definition.alliedUnitDamage, 16f);
        }

        private void FireArrowVisual(Vector3 targetPosition)
        {
            var start = transform.position + Vector3.up * 0.65f;
            var end = targetPosition + Vector3.up * 0.45f;
            var duration = Mathf.Clamp(Vector3.Distance(start, end) / 16f, 0.06f, 0.24f);
            HordeProjectileVisual.Spawn(start, end, PlaceholderPalette.Grey(.852f, 1f), arcProjectile: false, travelDuration: duration, markerRadius: 0.12f);
        }

        public void TakeDamage(float damage, EnemyActor source)
        {
            if (!IsAlive)
            {
                return;
            }

            var appliedDamage = Mathf.Max(0f, damage - Armor) * (1f - PhysicalResistance);
            health -= appliedDamage;
            UpdateHealthBar();
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

        public void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition)
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            health = Mathf.Clamp(authoritativeHealth, 0f, maxHealth);
            UpdateHealthBar();
            if (!destroyed || health > 0f)
            {
                return;
            }

            ReleaseAllBlockers();
            if (sourceDefinition != null && sourceDefinition.infectsAllies)
            {
                enemies?.SpawnConvertedEnemy(sourceDefinition, transform.position);
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
                case AlliedUnitType.Caveman:
                    // Stocky, a little wider than a knight (greybox until its model exists).
                    return new Vector3(0.4f, 0.62f, 0.4f);
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
                    return PlaceholderPalette.Grey(.833f);
                case AlliedUnitType.Paladin:
                    return PlaceholderPalette.Grey(.887f);
                default:
                    return PlaceholderPalette.Grey(.716f);
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
            background.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Grey(.03f, 1f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "AlliedHealthBarFill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.525f, 0.011f, 0f);
            fill.transform.localScale = new Vector3(1.05f, 0.085f, 0.14f);
            fill.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Grey(.735f, 1f));
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
