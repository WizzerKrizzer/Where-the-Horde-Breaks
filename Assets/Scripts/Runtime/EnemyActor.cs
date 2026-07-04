using System;
using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class EnemyActor : MonoBehaviour
    {
        private EnemyDefinition definition;
        private PathRoute path;
        private EnemyManager owner;
        private float health;
        private float pathDistance;
        private Vector3 knockbackOffset;
        private readonly List<BurnStack> burnStacks = new();
        private bool active;
        private Transform healthFill;

        public EnemyDefinition Definition => definition;
        public float Health => health;
        public float PathDistance => pathDistance;
        public bool IsAlive => active && health > 0f;
        public event Action<EnemyActor> Died;

        public void Initialize(EnemyDefinition enemyDefinition, PathRoute route, EnemyManager enemyOwner, float initialOffset)
        {
            definition = enemyDefinition;
            path = route;
            owner = enemyOwner;
            health = enemyDefinition.maxHealth;
            pathDistance = initialOffset;
            knockbackOffset = Vector3.zero;
            burnStacks.Clear();
            active = true;
            transform.localScale = Vector3.one * enemyDefinition.visualScale;
            EnsureHealthBar();
            UpdateHealthBar();
            gameObject.SetActive(true);
            MoveToPathPosition();
        }

        private void Update()
        {
            if (!active || path == null)
            {
                return;
            }

            pathDistance += definition.speed * Time.deltaTime;
            knockbackOffset = Vector3.MoveTowards(knockbackOffset, Vector3.zero, Time.deltaTime * 4f);
            UpdateBurns();
            UpdateHealthBar();
            if (pathDistance >= path.TotalLength)
            {
                active = false;
                owner.NotifyEnemyEscaped(this);
                gameObject.SetActive(false);
                return;
            }

            MoveToPathPosition();
        }

        public void ApplyKnockback(Vector3 origin, float distance)
        {
            if (!IsAlive || distance <= 0f)
            {
                return;
            }

            var direction = transform.position - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.right;
            }

            knockbackOffset += direction.normalized * distance;
        }

        public void ApplyBurn(TowerActor source, float damagePerTick, float ticksPerSecond, float duration, int maxStacks)
        {
            if (!IsAlive || source == null || damagePerTick <= 0f || ticksPerSecond <= 0f || duration <= 0f || maxStacks <= 0)
            {
                return;
            }

            if (burnStacks.Count >= maxStacks)
            {
                burnStacks.Sort((a, b) => a.remainingDuration.CompareTo(b.remainingDuration));
                burnStacks.RemoveAt(0);
            }

            burnStacks.Add(new BurnStack(source, damagePerTick, ticksPerSecond, duration));
        }

        public float ApplyDamage(float damage)
        {
            if (!IsAlive)
            {
                return 0f;
            }

            var appliedDamage = Mathf.Min(health, damage);
            health -= damage;
            UpdateHealthBar();
            if (health > 0f)
            {
                return appliedDamage;
            }

            active = false;
            Died?.Invoke(this);
            owner.NotifyEnemyKilled(this);
            gameObject.SetActive(false);
            return appliedDamage;
        }

        private void MoveToPathPosition()
        {
            transform.position = path.Sample(pathDistance) + knockbackOffset;
        }

        private void UpdateBurns()
        {
            for (var i = burnStacks.Count - 1; i >= 0; i--)
            {
                var burn = burnStacks[i];
                burn.remainingDuration -= Time.deltaTime;
                burn.tickTimer -= Time.deltaTime;

                while (burn.tickTimer <= 0f && burn.remainingDuration > 0f && IsAlive)
                {
                    var appliedDamage = ApplyDamage(burn.damagePerTick);
                    burn.source?.RecordDamage(appliedDamage);
                    burn.tickTimer += burn.tickInterval;
                }

                if (!IsAlive || burn.remainingDuration <= 0f)
                {
                    burnStacks.RemoveAt(i);
                }
                else
                {
                    burnStacks[i] = burn;
                }
            }
        }

        private void EnsureHealthBar()
        {
            if (healthFill != null)
            {
                return;
            }

            var root = new GameObject("HealthBar");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            background.name = "HealthBarBackground";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = Vector3.zero;
            background.transform.localScale = new Vector3(1.15f, 0.08f, 0.12f);
            background.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.03f, 0.03f, 0.035f, 1f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "HealthBarFill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.575f, 0.012f, 0f);
            fill.transform.localScale = new Vector3(1.15f, 0.09f, 0.14f);
            fill.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.22f, 1f, 0.25f, 1f));
            healthFill = fill.transform;
        }

        private void UpdateHealthBar()
        {
            if (healthFill == null || definition == null)
            {
                return;
            }

            var normalizedHealth = Mathf.Clamp01(health / definition.maxHealth);
            healthFill.localScale = new Vector3(1.15f * normalizedHealth, 0.09f, 0.14f);
            healthFill.localPosition = new Vector3(-0.575f + 0.575f * normalizedHealth, 0.012f, 0f);
        }

        private struct BurnStack
        {
            public readonly TowerActor source;
            public readonly float damagePerTick;
            public readonly float tickInterval;
            public float remainingDuration;
            public float tickTimer;

            public BurnStack(TowerActor source, float damagePerTick, float ticksPerSecond, float duration)
            {
                this.source = source;
                this.damagePerTick = damagePerTick;
                tickInterval = 1f / Mathf.Max(0.01f, ticksPerSecond);
                remainingDuration = duration;
                tickTimer = tickInterval;
            }
        }
    }
}
