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
        private float respawnTimer;
        private readonly List<AlliedUnitActor> alliedUnits = new();

        public TowerDefinition Definition => definition;
        public float DamageDealt { get; private set; }
        public Vector3 Position => transform.position;
        public bool IsAlive => gameObject.activeSelf && (definition == null || definition.behavior != TowerBehavior.Barrier || health > 0f);
        public CombatTargetKind TargetKind => CombatTargetKind.Barrier;

        public void SetDamageMultiplier(float multiplier)
        {
            damageMultiplier = Mathf.Max(0.05f, multiplier);
        }

        public void SetFireRateMultiplier(float multiplier)
        {
            fireRateMultiplier = Mathf.Max(0.05f, multiplier);
        }

        public void Initialize(TowerDefinition towerDefinition, EnemyManager enemyManager, float towerDamageMultiplier = 1f)
        {
            definition = towerDefinition;
            enemies = enemyManager;
            SetDamageMultiplier(towerDamageMultiplier);
            health = Mathf.Max(1f, towerDefinition.health);
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
            }
        }

        private void OnDestroy()
        {
            if (definition != null && definition.behavior == TowerBehavior.Barrier)
            {
                enemies?.UnregisterCombatTarget(this);
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
                    enemies.ApplySlowAura(transform.position, definition.range, definition.slowPercent, definition.slowCapacity);
                    return;
            }

            cooldown -= Time.deltaTime;
            if (cooldown > 0f)
            {
                return;
            }

            var target = enemies.GetNearestEnemy(transform.position, definition.range, definition.canHitFlying);
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

            var slotUse = 0;
            foreach (var unit in alliedUnits)
            {
                slotUse += unit != null ? Mathf.Max(1, definition.alliedUnitSlots) : 0;
            }

            if (slotUse >= definition.barracksCapacity)
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

        private void SpawnAlliedUnit(int index)
        {
            var go = GameObject.CreatePrimitive(definition.barracksUnitType == AlliedUnitType.Paladin ? PrimitiveType.Capsule : PrimitiveType.Cube);
            go.name = $"Allied_{definition.barracksUnitType}";
            var angle = index * 75f * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.9f;
            var unit = go.AddComponent<AlliedUnitActor>();
            unit.Initialize(this, definition, enemies, transform.position + offset);
            alliedUnits.Add(unit);
        }

        public void TakeDamage(float damage, EnemyActor source)
        {
            if (definition == null || definition.behavior != TowerBehavior.Barrier || health <= 0f)
            {
                return;
            }

            health -= damage;
            if (definition.thornsDamage > 0f && source != null && source.IsAlive)
            {
                RecordDamage(source.ApplyDamage(definition.thornsDamage));
            }

            if (health > 0f)
            {
                return;
            }

            health = 0f;
            enemies?.UnregisterCombatTarget(this);
            gameObject.SetActive(false);
        }

        public void NotifyAlliedUnitLost(AlliedUnitActor unit)
        {
            alliedUnits.Remove(unit);
        }

        public void RecordDamage(float damage)
        {
            DamageDealt += damage;
        }
    }
}
