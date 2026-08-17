using TowerDefense.Data;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class TowerActor : MonoBehaviour, ICombatTarget
    {
        private EnemyManager enemies;
        private TowerDefinition definition;
        private float damageMultiplier = 1f;
        private float fireRateMultiplier = 1f;
        private float cooldown;
        private float health;
        private float maxHealth;
        private float respawnTimer;
        private readonly List<AlliedUnitActor> alliedUnits = new();
        private readonly List<EnemyActor> blockers = new();
        private GameObject auraDisc;
        private GameObject selectionDisc;
        private Transform barrierHealthFill;
        private TowerTargetingMode targetingMode = TowerTargetingMode.First;

        public TowerDefinition Definition => definition;
        public float DamageDealt { get; private set; }
        public Vector3 Position => transform.position;
        public bool IsAlive => gameObject.activeSelf && (definition == null || definition.behavior != TowerBehavior.Barrier || health > 0f);
        public CombatTargetKind TargetKind => CombatTargetKind.Barrier;
        public float CombatRadius => definition != null && definition.behavior == TowerBehavior.Barrier ? 1.25f : 0.7f;
        public float BlockCapacity => definition != null && definition.behavior == TowerBehavior.Barrier ? 9999f : 0f;
        public float CurrentBlockedMass => GetBlockedMass();
        public TowerTargetingMode TargetingMode => targetingMode;
        public bool CanChangeTargeting => definition != null && definition.behavior == TowerBehavior.Projectile;

        public void SetDamageMultiplier(float multiplier)
        {
            damageMultiplier = Mathf.Max(0.05f, multiplier);
        }

        public void SetFireRateMultiplier(float multiplier)
        {
            fireRateMultiplier = Mathf.Max(0.05f, multiplier);
        }

        public void SetTargetingMode(TowerTargetingMode mode)
        {
            targetingMode = mode;
        }

        public void Initialize(TowerDefinition towerDefinition, EnemyManager enemyManager, float towerDamageMultiplier = 1f)
        {
            definition = towerDefinition;
            enemies = enemyManager;
            SetDamageMultiplier(towerDamageMultiplier);
            maxHealth = Mathf.Max(1f, towerDefinition.health);
            health = maxHealth;
            cooldown = Random.Range(0f, towerDefinition.fireInterval);
            respawnTimer = 0f;
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = BootstrapMaterials.Get(towerDefinition.color);
            }

            if (towerDefinition.behavior == TowerBehavior.Barrier)
            {
                enemies.RegisterCombatTarget(this);
                EnsureBarrierHealthBar();
                UpdateBarrierHealthBar();
            }
            else if (towerDefinition.behavior == TowerBehavior.Barracks)
            {
                FillBarracksImmediately();
            }

            UpdateAuraVisual();
        }

        private void OnDestroy()
        {
            if (definition != null && definition.behavior == TowerBehavior.Barrier)
            {
                enemies?.UnregisterCombatTarget(this);
            }

            if (auraDisc != null)
            {
                Destroy(auraDisc);
            }

            if (selectionDisc != null)
            {
                Destroy(selectionDisc);
            }
        }

        private void Update()
        {
            if (definition == null || enemies == null)
            {
                return;
            }

            switch (definition.behavior)
            {
                case TowerBehavior.Barrier:
                    return;
                case TowerBehavior.Barracks:
                    UpdateBarracks();
                    return;
                case TowerBehavior.SlowAura:
                    UpdateAuraVisual();
                    enemies.ApplySlowAura(transform.position, definition.range, definition.slowPercent, definition.slowCapacity);
                    return;
            }

            cooldown -= Time.deltaTime;
            if (cooldown > 0f)
            {
                return;
            }

            if (TryFireAtHordeTarget())
            {
                if (definition.doubleShotChance > 0f && Random.value < definition.doubleShotChance)
                {
                    TryFireAtHordeTarget();
                }

                cooldown = definition.fireInterval / fireRateMultiplier;
                return;
            }

            var target = enemies.GetEnemyByTargetingMode(transform.position, definition.range, definition.canHitFlying, targetingMode);
            if (target == null)
            {
                return;
            }

            Fire(target);
            if (definition.doubleShotChance > 0f && Random.value < definition.doubleShotChance)
            {
                var secondTarget = enemies.GetNearestEnemyExcept(transform.position, definition.range, definition.canHitFlying, target) ?? target;
                Fire(secondTarget);
            }
            cooldown = definition.fireInterval / fireRateMultiplier;
        }

        private void Fire(EnemyActor target)
        {
            var projectileColor = definition.appliesFire
                ? new Color(1f, 0.32f, 0.05f)
                : definition.projectilePattern == ProjectilePattern.ArcSplash ? new Color(0.42f, 0.36f, 0.28f) : Color.yellow;
            ProjectileActor.Spawn(this, definition, target, enemies, definition.damage * damageMultiplier, projectileColor);
        }

        private bool TryFireAtHordeTarget()
        {
            if (enemies == null || definition == null)
            {
                return false;
            }

            if (!enemies.TryGetHordeTargetPosition(transform.position, definition.range, definition.canHitFlying, targetingMode, out var targetPosition))
            {
                return false;
            }

            var damage = definition.damage * damageMultiplier;
            if (definition.projectilePattern == ProjectilePattern.ArcSplash)
            {
                var duration = GetHordeShotDuration(targetPosition);
                StartCoroutine(ApplyDelayedHordeSplash(targetPosition, damage, duration));
                SpawnHordeShotVisual(targetPosition, duration);
                return true;
            }

            var appliedDamage = enemies.DamageHordeTarget(transform.position, definition.range, definition.canHitFlying, targetingMode, damage, out targetPosition);
            RecordDamage(appliedDamage);
            SpawnHordeShotVisual(targetPosition, GetHordeShotDuration(targetPosition));
            return appliedDamage > 0f;
        }

        private IEnumerator ApplyDelayedHordeSplash(Vector3 targetPosition, float damage, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (enemies == null || definition == null)
            {
                yield break;
            }

            var radius = Mathf.Max(0.8f, definition.splashRadius);
            var burnDamage = definition.appliesFire ? definition.fireDamagePerTick : 0f;
            var burnRate = definition.appliesFire ? definition.fireTicksPerSecond : 0f;
            var burnDuration = definition.appliesFire ? definition.fireDuration : 0f;
            var burnStacks = definition.appliesFire ? definition.fireMaxStacks : 0;
            var appliedDamage = enemies.DamageAndKnockbackInRadius(
                targetPosition,
                radius,
                damage,
                definition.knockbackDistance,
                out _,
                this,
                burnDamage,
                burnRate,
                burnDuration,
                burnStacks);
            RecordDamage(appliedDamage);
        }

        private float GetHordeShotDuration(Vector3 targetPosition)
        {
            var start = transform.position + Vector3.up * 0.82f;
            var end = targetPosition + Vector3.up * (definition.projectilePattern == ProjectilePattern.ArcSplash ? 0.28f : 0.45f);
            var distance = Vector3.Distance(start, end);
            return definition.projectilePattern == ProjectilePattern.ArcSplash
                ? Mathf.Max(0.32f, distance / Mathf.Max(0.01f, definition.projectileSpeed) * Mathf.Max(1f, definition.arcFlightTimeMultiplier))
                : Mathf.Clamp(distance / Mathf.Max(0.01f, definition.projectileSpeed), 0.06f, 0.28f);
        }

        private void SpawnHordeShotVisual(Vector3 targetPosition, float duration)
        {
            var projectileColor = definition.appliesFire
                ? new Color(1f, 0.32f, 0.05f)
                : definition.projectilePattern == ProjectilePattern.ArcSplash ? new Color(0.42f, 0.36f, 0.28f) : Color.yellow;
            var start = transform.position + Vector3.up * 0.82f;
            var end = targetPosition + Vector3.up * (definition.projectilePattern == ProjectilePattern.ArcSplash ? 0.28f : 0.45f);
            var markerRadius = definition.projectilePattern == ProjectilePattern.ArcSplash
                ? Mathf.Max(0.55f, definition.splashRadius)
                : 0.14f;
            HordeProjectileVisual.Spawn(start, end, projectileColor, definition.projectilePattern == ProjectilePattern.ArcSplash, duration, markerRadius);
        }

        private void UpdateBarracks()
        {
            for (var i = alliedUnits.Count - 1; i >= 0; i--)
            {
                if (alliedUnits[i] == null || !alliedUnits[i].IsAlive)
                {
                    alliedUnits.RemoveAt(i);
                }
            }

            if (GetUsedBarracksSlots() >= definition.barracksCapacity)
            {
                return;
            }

            respawnTimer -= Time.deltaTime;
            if (respawnTimer > 0f)
            {
                return;
            }

            SpawnAlliedUnit(alliedUnits.Count);
            respawnTimer = Mathf.Max(0.5f, definition.barracksRespawnSeconds);
        }

        private int GetUsedBarracksSlots()
        {
            var slotUse = 0;
            foreach (var unit in alliedUnits)
            {
                slotUse += unit != null ? Mathf.Max(1, definition.alliedUnitSlots) : 0;
            }

            return slotUse;
        }

        private void SpawnAlliedUnit(int index)
        {
            var go = GameObject.CreatePrimitive(definition.barracksUnitType == AlliedUnitType.Paladin ? PrimitiveType.Capsule : PrimitiveType.Cube);
            go.name = $"Allied_{definition.barracksUnitType}";
            go.transform.SetParent(transform, worldPositionStays: true);
            var angle = index * 75f * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.9f;
            var unit = go.AddComponent<AlliedUnitActor>();
            unit.Initialize(this, definition, enemies, transform.position + offset, index);
            alliedUnits.Add(unit);
        }

        private void FillBarracksImmediately()
        {
            while (GetUsedBarracksSlots() < definition.barracksCapacity)
            {
                SpawnAlliedUnit(alliedUnits.Count);
            }

            respawnTimer = Mathf.Max(0.5f, definition.barracksRespawnSeconds);
        }

        public void TakeDamage(float damage, EnemyActor source)
        {
            if (definition == null || definition.behavior != TowerBehavior.Barrier || health <= 0f)
            {
                return;
            }

            health -= damage;
            UpdateBarrierDamageVisual();
            UpdateBarrierHealthBar();
            if (definition.thornsDamage > 0f && source != null && source.IsAlive)
            {
                var thornDamage = source.ApplyDamage(definition.thornsDamage);
                RecordDamage(thornDamage);
            }

            if (health > 0f)
            {
                return;
            }

            health = 0f;
            enemies?.UnregisterCombatTarget(this);
            gameObject.SetActive(false);
        }

        public bool TryAddBlocker(EnemyActor enemy)
        {
            if (enemy == null || blockers.Contains(enemy))
            {
                return enemy != null;
            }

            blockers.Add(enemy);
            return true;
        }

        public void RemoveBlocker(EnemyActor enemy)
        {
            blockers.Remove(enemy);
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

        private void UpdateBarrierDamageVisual()
        {
            if (definition == null || definition.behavior != TowerBehavior.Barrier)
            {
                return;
            }

            var renderer = GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var healthPercent = Mathf.Clamp01(health / Mathf.Max(1f, maxHealth));
            renderer.material = BootstrapMaterials.Get(Color.Lerp(new Color(0.75f, 0.12f, 0.08f), definition.color, healthPercent));
        }

        private void EnsureBarrierHealthBar()
        {
            if (barrierHealthFill != null)
            {
                return;
            }

            var root = new GameObject("BarrierHealthBar");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, 0.78f, 0f);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            background.name = "BarrierHealthBarBackground";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = Vector3.zero;
            background.transform.localScale = new Vector3(1.28f, 0.08f, 0.12f);
            background.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.035f, 0.025f, 0.02f, 1f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "BarrierHealthBarFill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.64f, 0.012f, 0f);
            fill.transform.localScale = new Vector3(1.28f, 0.09f, 0.14f);
            fill.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.95f, 0.55f, 0.2f, 1f));
            barrierHealthFill = fill.transform;
        }

        private void UpdateBarrierHealthBar()
        {
            if (barrierHealthFill == null)
            {
                return;
            }

            var normalizedHealth = Mathf.Clamp01(health / Mathf.Max(1f, maxHealth));
            barrierHealthFill.localScale = new Vector3(1.28f * normalizedHealth, 0.09f, 0.14f);
            barrierHealthFill.localPosition = new Vector3(-0.64f + 0.64f * normalizedHealth, 0.012f, 0f);
        }

        public void NotifyAlliedUnitLost(AlliedUnitActor unit)
        {
            alliedUnits.Remove(unit);
        }

        public void SetSelected(bool selected)
        {
            EnsureSelectionVisual();
            selectionDisc.SetActive(selected);
            if (selected)
            {
                selectionDisc.transform.position = transform.position + Vector3.up * 0.24f;
                selectionDisc.transform.localScale = new Vector3(definition.range * 2f, 0.035f, definition.range * 2f);
            }
        }

        public void RecordDamage(float damage)
        {
            DamageDealt += damage;
        }

        private void UpdateAuraVisual()
        {
            if (definition == null || definition.behavior != TowerBehavior.SlowAura)
            {
                if (auraDisc != null)
                {
                    auraDisc.SetActive(false);
                }
                return;
            }

            EnsureAuraVisual();
            auraDisc.SetActive(definition.slowPercent > 0f && definition.slowCapacity > 0f);
            auraDisc.transform.position = transform.position + Vector3.up * 0.22f;
            auraDisc.transform.localScale = new Vector3(definition.range * 2f, 0.025f, definition.range * 2f);
        }

        private void EnsureAuraVisual()
        {
            if (auraDisc != null)
            {
                return;
            }

            auraDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            auraDisc.name = "BellSlowAura";
            auraDisc.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.3f, 0.75f, 1f, 0.12f));
        }

        private void EnsureSelectionVisual()
        {
            if (selectionDisc != null)
            {
                return;
            }

            selectionDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            selectionDisc.name = "TowerSelectionRange";
            selectionDisc.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(1f, 0.9f, 0.25f, 0.16f));
            selectionDisc.SetActive(false);
        }
    }
}
