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
        private Vector3 movementVelocity;
        private readonly List<BurnStack> burnStacks = new();
        private readonly List<EnemyActor> nearbyEnemies = new(32);
        private float attackCooldown;
        private float healCooldown;
        private float slowTimer;
        private float slowMultiplier = 1f;
        private bool reviveUsed;
        private bool waitingToRevive;
        private bool endpointSeeking;
        private float reviveTimer;
        private float accumulatedSimulationTime;
        private float stepDeltaTime;
        private float currentMaxHealth;
        private bool active;
        private ICombatTarget currentCombatTarget;
        private Renderer bodyRenderer;
        private MeshFilter bodyMeshFilter;
        private GameObject healthRoot;
        private Transform healthFill;
        private float healthBarTimer;
        private float visualBudgetTimer;
        private int visualBudgetBucket;
        private bool usingLowDetailMesh;
        private bool isOffscreenForBudget;
        private bool isFarFromCameraForBudget;
        private bool isZoomedOutForBudget;
        private static int cachedMainCameraFrame = -1;
        private static Camera cachedMainCamera;
        private const float RoadHalfWidth = 2.45f;
        private const float PathLookAhead = 3.35f;
        private const float SteeringAcceleration = 6.8f;
        private const float WallBounce = 0.32f;
        private const float RoadFriction = 0.85f;
        private const float SeparationPathWindow = 3.4f;
        private const float SeparationRadiusScale = 0.94f;
        private const float SeparationVelocityScale = 5.6f;
        private const float MaxGroundSeparationOffset = 2.05f;
        private const int MaxEnemiesWithHealthBars = 180;
        private const float MaxHealthBarCameraHeight = 42f;
        private const float MaxHealthBarCameraDistance = 30f;

        public EnemyDefinition Definition => definition;
        public float Health => health;
        public float PathDistance => pathDistance;
        public bool IsAlive => active && health > 0f;
        public event Action<EnemyActor> Died;

        public void Initialize(EnemyDefinition enemyDefinition, PathRoute route, EnemyManager enemyOwner, float initialOffset, bool useEndpointSeeking = false)
        {
            definition = enemyDefinition;
            path = route;
            owner = enemyOwner;
            endpointSeeking = useEndpointSeeking;
            currentMaxHealth = enemyDefinition.maxHealth;
            health = currentMaxHealth;
            pathDistance = initialOffset;
            laneOffset = UnityEngine.Random.Range(-1.35f, 1.35f) * Mathf.Max(0.8f, enemyDefinition.visualScale / 0.45f);
            crowdOffset = Vector3.zero;
            knockbackOffset = Vector3.zero;
            movementVelocity = (endpointSeeking ? GetEndpointDirection() : GetPathTangent(pathDistance)) * enemyDefinition.speed;
            burnStacks.Clear();
            attackCooldown = 0f;
            healCooldown = enemyDefinition.healInterval;
            slowTimer = 0f;
            slowMultiplier = 1f;
            reviveUsed = false;
            waitingToRevive = false;
            reviveTimer = 0f;
            accumulatedSimulationTime = 0f;
            stepDeltaTime = 0f;
            active = true;
            currentCombatTarget = null;
            transform.localScale = Vector3.one * enemyDefinition.visualScale;
            bodyRenderer = GetComponent<Renderer>();
            bodyMeshFilter = GetComponent<MeshFilter>();
            if (bodyRenderer != null)
            {
                bodyRenderer.sharedMaterial = BootstrapMaterials.Get(enemyDefinition.color);
                bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bodyRenderer.receiveShadows = false;
                bodyRenderer.enabled = true;
            }

            visualBudgetBucket = Mathf.Abs(GetInstanceID()) % 12;
            visualBudgetTimer = UnityEngine.Random.Range(0f, 0.25f);
            isOffscreenForBudget = false;
            isFarFromCameraForBudget = false;
            isZoomedOutForBudget = false;
            SetLowDetailMesh(false);
            healthBarTimer = 0f;
            if (healthRoot != null)
            {
                healthRoot.SetActive(false);
            }

            UpdateHealthBar();
            gameObject.SetActive(true);
            SnapToPathPosition();
        }

        private void Update()
        {
            var frameDeltaTime = Time.deltaTime;
            if (waitingToRevive)
            {
                reviveTimer -= frameDeltaTime;
                if (reviveTimer <= 0f)
                {
                    waitingToRevive = false;
                    active = true;
                    health = currentMaxHealth * 0.5f;
                    gameObject.SetActive(true);
                    SnapToPathPosition();
                    ShowHealthBarBriefly();
                    UpdateHealthBar();
                }
                return;
            }

            if (!active || path == null)
            {
                return;
            }

            UpdateHealthBarVisibility();
            UpdateVisualBudget(frameDeltaTime);
            accumulatedSimulationTime += frameDeltaTime;
            if (!ShouldRunSimulationThisFrame())
            {
                return;
            }

            stepDeltaTime = accumulatedSimulationTime;
            accumulatedSimulationTime = 0f;

            if (slowTimer > 0f)
            {
                slowTimer -= stepDeltaTime;
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
                ApplyCombatJostle();
                return;
            }

            if (definition.healsEnemies)
            {
                healCooldown -= stepDeltaTime;
                if (healCooldown <= 0f)
                {
                    owner.HealEnemiesInRadius(transform.position, definition.healRadius, definition.healAmount, this);
                    healCooldown = Mathf.Max(0.1f, definition.healInterval);
                }
            }

            if (endpointSeeking)
            {
                UpdateEndpointPhysics();
            }
            else
            {
                UpdatePathPhysics();
            }
            UpdateBurns();
            UpdateHealthBar();
            if ((!endpointSeeking && pathDistance >= path.TotalLength) || (endpointSeeking && HasReachedEndpoint()))
            {
                active = false;
                ReleaseCombatTarget();
                owner.NotifyEnemyEscaped(this);
                gameObject.SetActive(false);
                return;
            }
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

            movementVelocity += direction.normalized * distance * 2.4f;
            knockbackOffset += direction.normalized * distance * 0.22f;
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
            ShowHealthBarBriefly();
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
            ShowHealthBarBriefly();
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

        private bool ShouldRunSimulationThisFrame()
        {
            var activeCount = owner != null ? owner.ActiveEnemyCount : 0;
            var frameStride = activeCount >= 8000 ? 32 : activeCount >= 5000 ? 24 : activeCount >= 3500 ? 16 : activeCount >= 2000 ? 12 : activeCount >= 1000 ? 8 : activeCount >= 650 ? 6 : activeCount >= 300 ? 4 : activeCount >= 180 ? 2 : 1;
            if (activeCount >= 1000)
            {
                if (isOffscreenForBudget)
                {
                    frameStride *= activeCount >= 4000 ? 4 : 3;
                }
                else if (isZoomedOutForBudget && isFarFromCameraForBudget)
                {
                    frameStride *= 2;
                }
            }

            frameStride = Mathf.Clamp(frameStride, 1, 64);
            if (frameStride <= 1)
            {
                return true;
            }

            return Mathf.Abs(GetInstanceID()) % frameStride == Time.frameCount % frameStride;
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

            attackCooldown -= stepDeltaTime;
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

            bodyRenderer.sharedMaterial = BootstrapMaterials.Get(slowed
                ? Color.Lerp(definition.color, new Color(0.28f, 0.72f, 1f), 0.58f)
                : definition.color);
        }

        private void SnapToPathPosition()
        {
            var pathPosition = path.Sample(pathDistance);
            var side = GetPathSide(pathDistance);
            var offset = side * laneOffset + crowdOffset + knockbackOffset;
            transform.position = pathPosition + offset;
        }

        private void UpdateEndpointPhysics()
        {
            var deltaTime = stepDeltaTime;
            var currentSpeed = definition.speed * slowMultiplier;
            var nearestRoad = path.GetNearestRoadPointToward(transform.position, path.EndPoint, out var roadTangent);
            nearestRoad.y = transform.position.y;

            var toEnd = path.EndPoint - transform.position;
            toEnd.y = 0f;
            var endpointDirection = toEnd.sqrMagnitude > 0.001f ? toEnd.normalized : roadTangent;

            var forwardBias = Vector3.Dot(roadTangent, endpointDirection) >= 0f ? roadTangent : -roadTangent;
            var centerPull = nearestRoad - transform.position;
            centerPull.y = 0f;
            var desiredDirection = (endpointDirection * 0.22f + forwardBias * 1.15f + centerPull.normalized * 0.55f).normalized;
            var separationVelocity = GetSeparationOffset(transform.position) * (SeparationVelocityScale * 1.18f);
            var desiredVelocity = desiredDirection * currentSpeed + separationVelocity;

            movementVelocity = Vector3.MoveTowards(movementVelocity, desiredVelocity, SteeringAcceleration * 1.18f * deltaTime);
            movementVelocity = Vector3.MoveTowards(movementVelocity, Vector3.zero, RoadFriction * 0.06f * deltaTime);
            knockbackOffset = Vector3.MoveTowards(knockbackOffset, Vector3.zero, deltaTime * 3.2f);

            var nextPosition = transform.position + (movementVelocity * deltaTime) + (knockbackOffset * deltaTime * 0.45f);
            nextPosition.y = nearestRoad.y;
            nextPosition = ConstrainToNearestRoad(nextPosition);
            nextPosition = ApplyHardOverlapCorrection(nextPosition, GetRoadSide(nextPosition));
            nextPosition = ConstrainToNearestRoad(nextPosition);
            transform.position = nextPosition;

            pathDistance = Mathf.Max(0f, path.TotalLength - Vector3.Distance(transform.position, path.EndPoint));
        }

        private void UpdatePathPhysics()
        {
            var deltaTime = stepDeltaTime;
            var currentSpeed = definition.speed * slowMultiplier;
            pathDistance += currentSpeed * deltaTime;
            knockbackOffset = Vector3.MoveTowards(knockbackOffset, Vector3.zero, deltaTime * 3.2f);
            UpdateCrowdOffset();

            var side = GetPathSide(pathDistance);
            var tangent = GetPathTangent(pathDistance);
            var pathPosition = path.Sample(pathDistance);
            var lookAheadPosition = path.Sample(Mathf.Min(path.TotalLength, pathDistance + PathLookAhead));
            var desiredPosition = lookAheadPosition + side * laneOffset + crowdOffset + knockbackOffset;
            var toDesired = desiredPosition - transform.position;
            toDesired.y = 0f;

            var desiredVelocity = toDesired.sqrMagnitude > 0.001f
                ? toDesired.normalized * currentSpeed
                : tangent * currentSpeed;

            var separationVelocity = GetSeparationOffset(transform.position) * SeparationVelocityScale;
            desiredVelocity += separationVelocity;

            movementVelocity = Vector3.MoveTowards(movementVelocity, desiredVelocity, SteeringAcceleration * deltaTime);
            movementVelocity = Vector3.MoveTowards(movementVelocity, Vector3.zero, RoadFriction * 0.08f * deltaTime);

            var nextPosition = transform.position + movementVelocity * deltaTime;
            nextPosition.y = pathPosition.y;
            nextPosition = ConstrainToRoad(nextPosition, pathPosition, side, tangent);
            nextPosition = ApplyHardOverlapCorrection(nextPosition, side);
            nextPosition = ConstrainToRoad(nextPosition, pathPosition, side, tangent);
            transform.position = nextPosition;
        }

        private void ApplyCombatJostle()
        {
            UpdateCrowdOffset();
            var desiredOffset = crowdOffset + knockbackOffset;
            if (desiredOffset.sqrMagnitude > 0.001f)
            {
                var nextPosition = transform.position + desiredOffset * stepDeltaTime * 0.85f;
                var pathPosition = path.Sample(pathDistance);
                nextPosition = ConstrainToRoad(nextPosition, pathPosition, GetPathSide(pathDistance), GetPathTangent(pathDistance));
                transform.position = nextPosition;
            }

            movementVelocity = Vector3.MoveTowards(movementVelocity, Vector3.zero, RoadFriction * stepDeltaTime);
            knockbackOffset = Vector3.MoveTowards(knockbackOffset, Vector3.zero, stepDeltaTime * 3.2f);
        }

        private Vector3 ConstrainToRoad(Vector3 position, Vector3 pathPosition, Vector3 side, Vector3 tangent)
        {
            var fromCenter = position - pathPosition;
            fromCenter.y = 0f;
            var lateral = Vector3.Dot(fromCenter, side);
            var clampedLateral = Mathf.Clamp(lateral, -RoadHalfWidth, RoadHalfWidth);
            if (!Mathf.Approximately(lateral, clampedLateral))
            {
                var excess = lateral - clampedLateral;
                position -= side * excess;
                var lateralVelocity = Vector3.Dot(movementVelocity, side);
                movementVelocity -= side * lateralVelocity * (1f + WallBounce);
                movementVelocity += tangent * Mathf.Sign(Vector3.Dot(movementVelocity, tangent) + 0.01f) * Mathf.Abs(excess) * 0.65f;
            }

            return position;
        }

        private Vector3 ConstrainToNearestRoad(Vector3 position)
        {
            Vector3 tangent;
            var roadPoint = endpointSeeking
                ? path.GetNearestRoadPointToward(position, path.EndPoint, out tangent)
                : path.GetNearestRoadPoint(position, out tangent);
            var side = Vector3.Cross(Vector3.up, tangent.normalized);
            var fromCenter = position - roadPoint;
            fromCenter.y = 0f;
            var lateral = Vector3.Dot(fromCenter, side);
            var clampedLateral = Mathf.Clamp(lateral, -RoadHalfWidth, RoadHalfWidth);
            if (!Mathf.Approximately(lateral, clampedLateral))
            {
                var excess = lateral - clampedLateral;
                position -= side * excess;
                var lateralVelocity = Vector3.Dot(movementVelocity, side);
                movementVelocity -= side * lateralVelocity * (1f + WallBounce * 1.65f);
                movementVelocity += tangent.normalized * Mathf.Abs(excess) * 0.72f;
            }

            return position;
        }

        private void UpdateCrowdOffset()
        {
            var desiredOffset = GetSeparationOffset(transform.position);
            crowdOffset = Vector3.MoveTowards(crowdOffset, desiredOffset, stepDeltaTime * 4.2f);
            crowdOffset = Vector3.MoveTowards(crowdOffset, Vector3.zero, stepDeltaTime * 0.18f);
        }

        private Vector3 GetSeparationOffset(Vector3 origin)
        {
            if (owner?.ActiveEnemies == null)
            {
                return Vector3.zero;
            }

            var offset = Vector3.zero;
            var candidates = GetSeparationCandidates(origin, 3.3f);
            foreach (var other in candidates)
            {
                if (other == null || other == this || !other.IsAlive)
                {
                    continue;
                }

                if (!IsRelevantSeparationNeighbor(other, origin))
                {
                    continue;
                }

                var away = origin - other.transform.position;
                away.y = 0f;
                var distance = away.magnitude;
                var desiredDistance = GetDesiredSeparationDistance(other);
                if (distance <= 0.001f)
                {
                    away = (endpointSeeking ? GetRoadSide(origin) : GetPathSide(pathDistance)) * (GetInstanceID() < other.GetInstanceID() ? -1f : 1f);
                    distance = 0.001f;
                }

                if (distance >= desiredDistance)
                {
                    continue;
                }

                var overlap = desiredDistance - distance;
                var pressure = overlap / desiredDistance;
                offset += away.normalized * overlap * (1.15f + pressure * 1.85f);
            }

            return Vector3.ClampMagnitude(offset, definition.isFlying ? 0.58f : MaxGroundSeparationOffset);
        }

        private Vector3 ApplyHardOverlapCorrection(Vector3 position, Vector3 fallbackSide)
        {
            if (definition.isFlying || owner?.ActiveEnemies == null)
            {
                return position;
            }

            if (owner.ActiveEnemyCount >= 900)
            {
                return position;
            }

            var correction = Vector3.zero;
            var candidates = GetSeparationCandidates(position, 2.6f);
            foreach (var other in candidates)
            {
                if (other == null || other == this || !other.IsAlive || other.Definition == null)
                {
                    continue;
                }

                if (!IsRelevantSeparationNeighbor(other, position))
                {
                    continue;
                }

                var away = position - other.transform.position;
                away.y = 0f;
                var distance = away.magnitude;
                var desiredDistance = GetDesiredSeparationDistance(other) * 0.82f;
                if (distance <= 0.001f)
                {
                    away = fallbackSide * (GetInstanceID() < other.GetInstanceID() ? -1f : 1f);
                    distance = 0.001f;
                }

                if (distance >= desiredDistance)
                {
                    continue;
                }

                correction += away.normalized * ((desiredDistance - distance) * 0.55f);
            }

            return position + Vector3.ClampMagnitude(correction, 0.55f);
        }

        private IEnumerable<EnemyActor> GetSeparationCandidates(Vector3 origin, float endpointRadius)
        {
            owner.CollectNearbyEnemies(origin, endpointSeeking ? endpointRadius : 3.8f, nearbyEnemies, GetSeparationNeighborLimit());
            return nearbyEnemies;
        }

        private int GetSeparationNeighborLimit()
        {
            var activeCount = owner != null ? owner.ActiveEnemyCount : 0;
            if (activeCount >= 2500)
            {
                return 10;
            }

            if (activeCount >= 1200)
            {
                return 14;
            }

            if (activeCount >= 650)
            {
                return 20;
            }

            return 30;
        }

        private float GetDesiredSeparationDistance(EnemyActor other)
        {
            var combinedScale = definition.visualScale + (other.Definition?.visualScale ?? definition.visualScale);
            return Mathf.Max(0.62f, combinedScale * SeparationRadiusScale);
        }

        private bool IsRelevantSeparationNeighbor(EnemyActor other, Vector3 origin)
        {
            if (!endpointSeeking)
            {
                return Mathf.Abs(other.PathDistance - pathDistance) <= SeparationPathWindow;
            }

            var dx = other.transform.position.x - origin.x;
            var dz = other.transform.position.z - origin.z;
            return dx * dx + dz * dz <= 10.5f;
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

        private Vector3 GetRoadSide(Vector3 position)
        {
            if (path == null)
            {
                return Vector3.right;
            }

            if (endpointSeeking)
            {
                path.GetNearestRoadPointToward(position, path.EndPoint, out var endpointTangent);
                return Vector3.Cross(Vector3.up, endpointTangent.normalized);
            }

            path.GetNearestRoadPoint(position, out var roadTangent);
            return Vector3.Cross(Vector3.up, roadTangent.normalized);
        }

        private Vector3 GetEndpointDirection()
        {
            if (path == null)
            {
                return Vector3.forward;
            }

            var direction = path.EndPoint - path.Sample(pathDistance);
            direction.y = 0f;
            return direction.sqrMagnitude < 0.001f ? Vector3.forward : direction.normalized;
        }

        private bool HasReachedEndpoint()
        {
            if (path == null)
            {
                return false;
            }

            var toEnd = path.EndPoint - transform.position;
            toEnd.y = 0f;
            return toEnd.sqrMagnitude <= 4.5f;
        }

        private void UpdateBurns()
        {
            for (var i = burnStacks.Count - 1; i >= 0; i--)
            {
                var burn = burnStacks[i];
                burn.remainingDuration -= stepDeltaTime;
                burn.tickTimer -= stepDeltaTime;

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
                healthRoot?.SetActive(true);
                return;
            }

            var root = new GameObject("HealthBar");
            healthRoot = root;
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, 1.78f, 0f);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            background.name = "HealthBarBackground";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = Vector3.zero;
            background.transform.localScale = new Vector3(1.05f, 0.06f, 0.1f);
            background.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(new Color(0.03f, 0.03f, 0.035f, 1f));
            RemovePrimitiveColliders(background);

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "HealthBarFill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.525f, 0.01f, 0f);
            fill.transform.localScale = new Vector3(1.05f, 0.07f, 0.12f);
            fill.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(new Color(0.22f, 1f, 0.25f, 1f));
            RemovePrimitiveColliders(fill);
            healthFill = fill.transform;
        }

        private static void RemovePrimitiveColliders(GameObject gameObject)
        {
            var components = gameObject.GetComponents<Component>();
            for (var i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }
        }

        private void UpdateHealthBar()
        {
            if (healthFill == null || definition == null)
            {
                return;
            }

            if (healthRoot != null && !healthRoot.activeSelf)
            {
                return;
            }

            var normalizedHealth = Mathf.Clamp01(health / currentMaxHealth);
            healthFill.localScale = new Vector3(1.05f * normalizedHealth, 0.07f, 0.12f);
            healthFill.localPosition = new Vector3(-0.525f + 0.525f * normalizedHealth, 0.01f, 0f);
        }

        private void ShowHealthBarBriefly()
        {
            if (!IsAlive || !ShouldShowHealthBar())
            {
                if (healthRoot != null)
                {
                    healthRoot.SetActive(false);
                }

                return;
            }

            EnsureHealthBar();
            healthBarTimer = 1f;
            healthRoot?.SetActive(true);
        }

        private void UpdateHealthBarVisibility()
        {
            if (healthRoot == null || !healthRoot.activeSelf)
            {
                return;
            }

            if (!ShouldShowHealthBar())
            {
                healthRoot.SetActive(false);
                return;
            }

            healthBarTimer -= Time.deltaTime;
            if (healthBarTimer <= 0f)
            {
                healthRoot.SetActive(false);
            }
        }

        private bool ShouldShowHealthBar()
        {
            if (owner != null && owner.ActiveEnemyCount > MaxEnemiesWithHealthBars)
            {
                return false;
            }

            var camera = GetMainCameraCached();
            if (camera == null)
            {
                return true;
            }

            if (camera.transform.position.y > MaxHealthBarCameraHeight)
            {
                return false;
            }

            var toEnemy = transform.position - camera.transform.position;
            toEnemy.y = 0f;
            return toEnemy.sqrMagnitude <= MaxHealthBarCameraDistance * MaxHealthBarCameraDistance;
        }

        private void UpdateVisualBudget(float deltaTime)
        {
            if (bodyRenderer == null || owner == null)
            {
                return;
            }

            visualBudgetTimer -= deltaTime;
            if (visualBudgetTimer > 0f)
            {
                return;
            }

            var activeCount = owner.ActiveEnemyCount;
            visualBudgetTimer = activeCount >= 5000 ? 0.42f : activeCount >= 2500 ? 0.3f : 0.18f;
            if (activeCount < 650)
            {
                isOffscreenForBudget = false;
                isFarFromCameraForBudget = false;
                isZoomedOutForBudget = false;
                bodyRenderer.enabled = true;
                SetLowDetailMesh(false);
                return;
            }

            var camera = GetMainCameraCached();
            if (camera == null)
            {
                isOffscreenForBudget = false;
                isFarFromCameraForBudget = false;
                isZoomedOutForBudget = false;
                bodyRenderer.enabled = true;
                return;
            }

            var viewport = camera.WorldToViewportPoint(transform.position);
            isOffscreenForBudget = viewport.z < 0f || viewport.x < -0.12f || viewport.x > 1.12f || viewport.y < -0.12f || viewport.y > 1.12f;
            isZoomedOutForBudget = camera.transform.position.y >= 72f;
            if (isOffscreenForBudget)
            {
                isFarFromCameraForBudget = true;
                bodyRenderer.enabled = false;
                return;
            }

            var toEnemy = transform.position - camera.transform.position;
            toEnemy.y = 0f;
            isFarFromCameraForBudget = toEnemy.sqrMagnitude > 28f * 28f;
            var closeToCamera = !isFarFromCameraForBudget;
            if (closeToCamera || camera.transform.position.y < 58f)
            {
                bodyRenderer.enabled = true;
                SetLowDetailMesh(false);
                return;
            }

            var visualStride = activeCount >= 2500 ? 4 : activeCount >= 1400 ? 3 : 2;
            bodyRenderer.enabled = visualBudgetBucket % visualStride == 0;
            SetLowDetailMesh(activeCount >= 1000 || camera.transform.position.y >= 72f);
        }

        private void SetLowDetailMesh(bool lowDetail)
        {
            if (bodyMeshFilter == null)
            {
                return;
            }

            var targetMesh = lowDetail
                ? EnemyManager.GetLowEnemyMesh()
                : EnemyManager.GetDetailedEnemyMesh();
            if (usingLowDetailMesh == lowDetail && bodyMeshFilter.sharedMesh == targetMesh)
            {
                return;
            }

            bodyMeshFilter.sharedMesh = targetMesh;
            usingLowDetailMesh = lowDetail;
        }

        private static Camera GetMainCameraCached()
        {
            if (cachedMainCameraFrame == Time.frameCount)
            {
                return cachedMainCamera;
            }

            cachedMainCameraFrame = Time.frameCount;
            cachedMainCamera = Camera.main;
            return cachedMainCamera;
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
