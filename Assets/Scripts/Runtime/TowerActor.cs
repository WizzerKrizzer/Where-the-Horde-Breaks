using TowerDefense.Data;
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
                renderer.material = BootstrapMaterials.Get(towerDefinition.color);
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
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Projectile_{definition.id}";
            go.transform.position = transform.position + Vector3.up * 0.45f;
            go.transform.localScale = Vector3.one * (definition.projectilePattern == ProjectilePattern.ArcSplash ? 0.34f : 0.16f);
            var projectileColor = definition.appliesFire
                ? new Color(1f, 0.32f, 0.05f)
                : definition.projectilePattern == ProjectilePattern.ArcSplash ? new Color(0.42f, 0.36f, 0.28f) : Color.yellow;
            go.GetComponent<Renderer>().material = BootstrapMaterials.Get(projectileColor);
            var projectile = go.AddComponent<ProjectileActor>();
            projectile.Fire(this, definition, target, enemies, definition.damage * damageMultiplier);
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
            DamagePopup.Show(transform.position, damage, new Color(1f, 0.25f, 0.18f, 1f));
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
                selectionDisc.transform.position = transform.position + Vector3.up * 0.055f;
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
            auraDisc.transform.position = transform.position + Vector3.up * 0.035f;
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
