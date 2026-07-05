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
        private float laneOffset;
        private Vector3 crowdOffset;
        private Vector3 knockbackOffset;
        private readonly List<BurnStack> burnStacks = new();
        private float attackCooldown;
        private float healCooldown;
        private float slowTimer;
        private float slowMultiplier = 1f;
        private bool reviveUsed;
        private bool waitingToRevive;
        private float reviveTimer;
        private float currentMaxHealth;
        private bool active;
        private ICombatTarget currentCombatTarget;
        private Renderer bodyRenderer;
        private Transform healthFill;
        private const float RoadHalfWidth = 2.45f;

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
            currentMaxHealth = enemyDefinition.maxHealth;
            health = currentMaxHealth;
            pathDistance = initialOffset;
            laneOffset = UnityEngine.Random.Range(-0.52f, 0.52f) * Mathf.Max(0.8f, enemyDefinition.visualScale / 0.45f);
            crowdOffset = Vector3.zero;
            knockbackOffset = Vector3.zero;
            burnStacks.Clear();
            attackCooldown = 0f;
            healCooldown = enemyDefinition.healInterval;
            slowTimer = 0f;
            slowMultiplier = 1f;
            reviveUsed = false;
            waitingToRevive = false;
            reviveTimer = 0f;
            active = true;
            currentCombatTarget = null;
            transform.localScale = Vector3.one * enemyDefinition.visualScale;
            bodyRenderer = GetComponent<Renderer>();
            if (bodyRenderer != null)
            {
                bodyRenderer.material = BootstrapMaterials.Get(enemyDefinition.color);
            }

            EnsureHealthBar();
            UpdateHealthBar();
            gameObject.SetActive(true);
            MoveToPathPosition();
        }

        private void Update()
        {
            if (waitingToRevive)
            {
                reviveTimer -= Time.deltaTime;
                if (reviveTimer <= 0f)
                {
                    waitingToRevive = false;
                    active = true;
                    health = currentMaxHealth * 0.5f;
                    gameObject.SetActive(true);
                    MoveToPathPosition();
                    UpdateHealthBar();
                }
                return;
            }

            if (!active || path == null)
            {
                return;
            }

            if (slowTimer > 0f)
            {
                slowTimer -= Time.deltaTime;
            }
            else
            {
                slowMultiplier = 1f;
                UpdateSlowVisual(false);
            }

            if (TryAttackCombatTarget())
            {
                UpdateCrowdOffset();
                UpdateBurns();
                UpdateHealthBar();
                MoveToPathPosition();
                return;
            }

            if (definition.healsEnemies)
            {
                healCooldown -= Time.deltaTime;
                if (healCooldown <= 0f)
                {
                    owner.HealEnemiesInRadius(transform.position, definition.healRadius, definition.healAmount, this);
                    healCooldown = Mathf.Max(0.1f, definition.healInterval);
                }
            }

            pathDistance += definition.speed * slowMultiplier * Time.deltaTime;
            knockbackOffset = Vector3.MoveTowards(knockbackOffset, Vector3.zero, Time.deltaTime * 4f);
            UpdateCrowdOffset();
            UpdateBurns();
            UpdateHealthBar();
            if (pathDistance >= path.TotalLength)
            {
                active = false;
                ReleaseCombatTarget();
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

        public void ApplySlow(float slowPercent, float duration)
        {
            if (!IsAlive || slowPercent <= 0f || duration <= 0f)
            {
                return;
            }

            slowMultiplier = Mathf.Min(slowMultiplier, 1f - Mathf.Clamp(slowPercent, 0f, 0.95f));
            slowTimer = Mathf.Max(slowTimer, duration);
            UpdateSlowVisual(true);
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return;
            }

            health = Mathf.Min(currentMaxHealth, health + amount);
            UpdateHealthBar();
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

            if (definition.revivesOnce && !reviveUsed)
            {
                reviveUsed = true;
                active = false;
                ReleaseCombatTarget();
                waitingToRevive = true;
                reviveTimer = Mathf.Max(0.1f, definition.reviveDelay);
            }
            else
            {
                active = false;
                ReleaseCombatTarget();
                Died?.Invoke(this);
                owner.NotifyEnemyKilled(this);
                gameObject.SetActive(false);
            }
            return appliedDamage;
        }

        private void OnDisable()
        {
            ReleaseCombatTarget();
        }

        private bool TryAttackCombatTarget()
        {
            if (definition.isFlying)
            {
                ReleaseCombatTarget();
                return false;
            }

            var target = currentCombatTarget;
            if (target == null || !target.IsAlive || !IsInCombatRange(target))
            {
                ReleaseCombatTarget();
                target = owner.GetNearestCombatTarget(transform.position, 0.85f, definition.mass);
                if (target != null && !target.TryAddBlocker(this))
                {
                    target = null;
                }

                currentCombatTarget = target;
            }

            if (target == null)
            {
                return false;
            }

            attackCooldown -= Time.deltaTime;
            if (attackCooldown > 0f)
            {
                return true;
            }

            var multiplier = target.TargetKind == CombatTargetKind.Barrier ? definition.wallDamageMultiplier : definition.alliedDamageMultiplier;
            var damage = definition.attackDamage * Mathf.Max(0f, multiplier);
            target.TakeDamage(damage, this);
            if (definition.drainsAllies && target.TargetKind == CombatTargetKind.AlliedUnit)
            {
                currentMaxHealth += damage * 0.35f;
                health = Mathf.Min(currentMaxHealth, health + damage * definition.drainHealMultiplier);
            }

            attackCooldown = Mathf.Max(0.1f, definition.attackInterval);
            return true;
        }

        private bool IsInCombatRange(ICombatTarget target)
        {
            var allowedRange = 0.85f + Mathf.Max(0f, target.CombatRadius);
            var offset = target.Position - transform.position;
            var distanceSq = offset.x * offset.x + offset.z * offset.z;
            return distanceSq <= allowedRange * allowedRange;
        }

        private void ReleaseCombatTarget()
        {
            currentCombatTarget?.RemoveBlocker(this);
            currentCombatTarget = null;
        }

        private void UpdateSlowVisual(bool slowed)
        {
            if (bodyRenderer == null || definition == null)
            {
                return;
            }

            bodyRenderer.material = BootstrapMaterials.Get(slowed
                ? Color.Lerp(definition.color, new Color(0.28f, 0.72f, 1f), 0.58f)
                : definition.color);
        }

        private void MoveToPathPosition()
        {
            var pathPosition = path.Sample(pathDistance);
            var side = GetPathSide(pathDistance);
            var tangent = GetPathTangent(pathDistance);
            var offset = side * laneOffset + crowdOffset + knockbackOffset;
            var lateral = Vector3.Dot(offset, side);
            var clampedLateral = Mathf.Clamp(lateral, -RoadHalfWidth, RoadHalfWidth);
            if (!Mathf.Approximately(lateral, clampedLateral))
            {
                var excess = lateral - clampedLateral;
                knockbackOffset -= side * (excess * 1.25f);
                knockbackOffset += tangent * Random.Range(-0.18f, 0.18f);
                offset -= side * excess;
            }

            transform.position = pathPosition + offset;
        }

        private void UpdateCrowdOffset()
        {
            var desiredOffset = GetSeparationOffset(transform.position);
            crowdOffset = Vector3.MoveTowards(crowdOffset, desiredOffset, Time.deltaTime * 1.8f);
            crowdOffset = Vector3.MoveTowards(crowdOffset, Vector3.zero, Time.deltaTime * 0.35f);
        }

        private Vector3 GetSeparationOffset(Vector3 origin)
        {
            if (owner?.ActiveEnemies == null)
            {
                return Vector3.zero;
            }

            var offset = Vector3.zero;
            foreach (var other in owner.ActiveEnemies)
            {
                if (other == null || other == this || !other.IsAlive)
                {
                    continue;
                }

                if (Mathf.Abs(other.PathDistance - pathDistance) > 1.8f)
                {
                    continue;
                }

                var away = origin - other.transform.position;
                away.y = 0f;
                var distance = away.magnitude;
                var desiredDistance = Mathf.Max(0.38f, (definition.visualScale + other.Definition.visualScale) * 0.54f);
                if (distance <= 0.001f)
                {
                    away = GetPathSide(pathDistance) * (GetInstanceID() < other.GetInstanceID() ? -1f : 1f);
                    distance = 0.001f;
                }

                if (distance >= desiredDistance)
                {
                    continue;
                }

                offset += away.normalized * (desiredDistance - distance);
            }

            return Vector3.ClampMagnitude(offset, definition.isFlying ? 0.42f : 0.72f);
        }

        private Vector3 GetPathTangent(float distance)
        {
            if (path == null || path.TotalLength <= 0f)
            {
                return Vector3.forward;
            }

            var before = path.Sample(Mathf.Max(0f, distance - 0.35f));
            var after = path.Sample(Mathf.Min(path.TotalLength, distance + 0.35f));
            var tangent = after - before;
            tangent.y = 0f;
            return tangent.sqrMagnitude < 0.001f ? Vector3.forward : tangent.normalized;
        }

        private Vector3 GetPathSide(float distance)
        {
            if (path == null || path.TotalLength <= 0f)
            {
                return Vector3.right;
            }

            var before = path.Sample(Mathf.Max(0f, distance - 0.45f));
            var after = path.Sample(Mathf.Min(path.TotalLength, distance + 0.45f));
            var tangent = after - before;
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.001f)
            {
                tangent = Vector3.forward;
            }

            return Vector3.Cross(Vector3.up, tangent.normalized);
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

            var normalizedHealth = Mathf.Clamp01(health / currentMaxHealth);
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
