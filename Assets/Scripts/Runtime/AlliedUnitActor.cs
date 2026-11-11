using TowerDefense.Data;
using TowerDefense.Simulation;
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
        // Testing only: the recordings give the cavemen this many times their health.
        internal static float TestHealthMultiplier = 1f;
        // Testing only: how many blows the cavemen struck.
        internal static int TestStrikes;

        public Vector3 Position => transform.position;
        public bool IsAlive => health > 0f && gameObject.activeSelf;
        public CombatTargetKind TargetKind => CombatTargetKind.AlliedUnit;
        public float CombatRadius => definition != null && definition.barracksUnitType == AlliedUnitType.Paladin ? 0.72f : 0.55f;
        public float BlockCapacity => IsRangedArcher ? 0f : definition != null ? Mathf.Max(0f, definition.alliedUnitBlockCapacity) * (IsChief ? ChiefBlockFactor : 1f) : 0f;
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
        // The chief holds twice as many goblins as a caveman (user, 2026-11-11). TEMPORARY.
        private const float ChiefBlockFactor = 2f;
        public float FireResistance => 0f;
        public float SlowResistance => 0f;
        public float ThornsDamage => 0f;
        private bool IsRangedArcher => definition != null && definition.barracksUnitType == AlliedUnitType.Archer;

        // Cavemen each hold their own post across the road and fight only what comes into their own
        // sector around it, so they spread out instead of piling onto one goblin (user, 2026-11-11).
        // TEMPORARY distances until balancing.
        private const float CavemanLaneSpacing = 1.5f;
        private const float CavemanSectorRadius = 1.9f;
        private const float CavemanRowSpacing = 1.5f;
        private const float CavemanRoadMargin = 0.45f;
        // On a road too narrow for three across, posts zig-zag down it, left and right in turn.
        private const float CavemanZigZagStep = 1.1f;
        // Posts closer than this to another caveman's post (from any cave) move on down the road.
        private const float CavemanPostClearance = 1.2f;
        // A caveman never strays further than this from his post, and never off the road, even when
        // chasing a goblin at the road's edge (one stood outside it, unreachable, 2026-11-11).
        private const float CavemanLeash = 1.3f;
        // With nothing to fight he strolls back and forth along the road by his post, pausing now and then.
        private const float CavemanPatrolReach = 1.0f;
        // He looks for goblins from a point this far up the road from his post, so he fights the
        // ones coming at him, not those beside or behind him (user, 2026-11-11). TEMPORARY.
        private const float CavemanFrontBias = 0.8f;
        // His health bar over his head, in metres.
        private const float CavemanBarWidth = 0.9f;
        private const float CavemanBarDepth = 0.22f;
        // Goblins straight ahead come first: those within this of a point this far up the road from
        // his post. Only with none there does he turn to one beside him; and a goblin he holds is
        // let go once it is more than ~65 degrees off his front (they still went for those beside
        // them too often, user 2026-11-11). TEMPORARY.
        private const float CavemanFrontReach = 1.1f;
        private const float CavemanFrontRadius = 0.9f;
        private const float CavemanFrontCos = 0.42f;
        // He keeps hitting the goblin he picked for this long before he looks for a better one.
        private const float CavemanRetargetSeconds = 1.2f;
        // He steps slowly while fighting, and speeds up and slows down gently instead of darting.
        private const float CavemanCombatStepSpeed = 1.1f;
        private const float CavemanAcceleration = 3.5f;
        // A chief's blow also hurts goblins this close to the one struck (alliedUnitSplashTargets of them in all).
        private const float CavemanSplashRadius = 0.85f;
        private int trackedHordeIndex = -1;
        private float retargetTimer;
        private Vector3 smoothVelocity;
        private bool movedThisFrame;
        private Vector3 caveExit;
        private bool hasCaveExit;

        /// <summary>Born deep inside the cave: he walks out through its mouth before he goes to his post.</summary>
        public void ComeOutThrough(Vector3 mouth)
        {
            caveExit = mouth;
            hasCaveExit = true;
            var outward = mouth - transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude > 1e-4f)
            {
                transform.rotation = Quaternion.LookRotation(outward.normalized, Vector3.up);
            }
        }
        private const float CavemanPatrolSpeed = 0.7f;
        private Vector3 postAlong = Vector3.forward;
        private float postHalfWidth = 12f;
        private Vector3 patrolTarget;
        private float patrolPause;
        private int patrolSide = 1;
        private bool IsCaveman => definition != null && definition.barracksUnitType == AlliedUnitType.Caveman;
        public int Slot { get; private set; }
        public Vector3 Post => rallyPoint;
        // Up the road, where the horde comes from: where a caveman looks while he waits.
        public Vector3 WatchDirection => -postAlong;

        // What the caveman's model (CavemanVisual) needs to show what it is doing.
        private CavemanVisual visual;
        public bool HasFacingTarget { get; private set; }
        public Vector3 FacingTarget { get; private set; }
        public bool InStrikeRange { get; private set; }
        public float SecondsToNextStrike => attackCooldown;
        public float AttackInterval => definition != null ? Mathf.Max(0.1f, definition.alliedUnitAttackInterval) : 1f;
        public bool HasShield => definition != null && definition.alliedUnitDamageReduction > 0f;
        public bool HasTorch => definition != null && definition.torchBurnDamagePerSecond > 0f && definition.torchBurnSeconds > 0f;

        public void Initialize(TowerActor ownerTower, TowerDefinition towerDefinition, EnemyManager enemyManager, Vector3 position, int formationIndex)
        {
            owner = ownerTower;
            definition = towerDefinition;
            enemies = enemyManager;
            maxHealth = Mathf.Max(1f, towerDefinition.alliedUnitHealth);
            if (towerDefinition.barracksUnitType == AlliedUnitType.Caveman)
            {
                maxHealth *= Mathf.Max(0.01f, TestHealthMultiplier);
            }

            health = maxHealth;
            trackedHordeIndex = -1;
            reachedPost = false;
            hasCaveExit = false;
            smoothVelocity = Vector3.zero;
            attackCooldown = Random.Range(0f, Mathf.Max(0.1f, towerDefinition.alliedUnitAttackInterval));
            transform.position = position;
            Slot = formationIndex;
            formationOffset = GetFormationOffset(formationIndex, towerDefinition);
            rallyPoint = enemies != null
                ? towerDefinition.barracksUnitType == AlliedUnitType.Archer ? enemies.GetPathSidePosition(position, 1.85f) + formationOffset : enemies.GetNearestPathPosition(position) + formationOffset
                : position;
            if (enemies != null && towerDefinition.barracksUnitType == AlliedUnitType.Caveman)
            {
                formationOffset = Vector3.zero;
                rallyPoint = RoadPost(position, formationIndex);
                postAlong = RoadAlong(rallyPoint);
                if (enemies.Route != null && enemies.Route.TotalLength > 0f)
                {
                    var postCenter = enemies.Route.Sample(enemies.EstimatePathDistance(rallyPoint));
                    postHalfWidth = HalfWidthAt(enemies.Route, postCenter, Vector3.Cross(Vector3.up, postAlong));
                }
                patrolTarget = rallyPoint;
                patrolPause = Random.Range(0.5f, 2.5f);
                patrolSide = formationIndex % 2 == 0 ? 1 : -1;
            }
            transform.localScale = GetScale(towerDefinition);
            GetComponent<Renderer>().material = BootstrapMaterials.Get(GetColor(towerDefinition));
            EnsureHealthBar();
            UpdateHealthBar();
            // The cave's specials show on the caveman (greybox props until the cavemen's model).
            // A reused unit starts as an ordinary caveman again until it is made chief.
            var horns = transform.Find("ChiefHorns");
            if (horns != null)
            {
                horns.SetParent(null, false);
                Destroy(horns.gameObject);
            }

            // The cavemen's model, when it is installed, replaces the block and its greybox props.
            if (towerDefinition.barracksUnitType == AlliedUnitType.Caveman && visual == null)
            {
                visual = CavemanVisual.TryCreate(this, formationIndex);
            }

            if (visual != null)
            {
                GetComponent<Renderer>().enabled = false;
                PlaceHealthBarAbove(visual.Height);
            }

            if (visual == null && !IsRangedArcher && towerDefinition.torchBurnDamagePerSecond > 0f && towerDefinition.torchBurnSeconds > 0f)
            {
                SpecialEffectVisuals.AttachTorch(transform);
            }

            if (visual == null && !IsRangedArcher && towerDefinition.alliedUnitDamageReduction > 0f)
            {
                SpecialEffectVisuals.AttachShield(transform);
            }

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

            movedThisFrame = false;
            UpdateBehaviour();
            if (IsCaveman && !movedThisFrame)
            {
                Coast();
            }
        }

        private void UpdateBehaviour()
        {
            attackCooldown -= Time.deltaTime;
            retargetTimer -= Time.deltaTime;
            HasFacingTarget = false;
            InStrikeRange = false;
            if (IsRangedArcher && enemies.TryGetHordeTargetPosition(transform.position, Mathf.Max(definition.alliedUnitAggroRange, definition.alliedUnitRange), definition.alliedUnitCanHitFlying, TowerTargetingMode.Closest, out var hordeTarget))
            {
                UpdateArcherHordeCombat(hordeTarget);
                return;
            }

            // Fresh from deep inside the cave, he first walks out through its mouth.
            if (hasCaveExit)
            {
                if (IsWithinXzRange(caveExit, 0.3f))
                {
                    hasCaveExit = false;
                }
                else
                {
                    MoveToward(caveExit);
                    return;
                }
            }

            if (IsCaveman && !reachedPost)
            {
                if (IsWithinXzRange(KeepNearPost(transform.position), 0.15f))
                {
                    reachedPost = true;
                }
                else
                {
                    MoveToward(rallyPoint);
                    return;
                }
            }

            // A caveman walks out onto the road by his post before he fights: one fresh out of the
            // cave would otherwise stop at its mouth to hit the goblins at the road's edge, and stay.
            if (IsCaveman && !IsWithinXzRange(KeepNearPost(transform.position), 0.15f))
            {
                MoveToward(rallyPoint);
                return;
            }

            // A caveman keeps the goblin he picked while it lives and stays in his sector, and only
            // now and then looks for one further up the road, so he does not swing round every frame.
            if (IsCaveman && trackedHordeIndex >= 0 && retargetTimer > 0f &&
                enemies.TryGetHordeAgentPosition(trackedHordeIndex, out var tracked) &&
                IsWithin(rallyPoint, tracked, CavemanSectorRadius + 0.3f) && IsInFront(tracked))
            {
                UpdateMeleeHordeCombat(trackedHordeIndex, tracked);
                return;
            }

            if (IsCaveman && enemies.TryGetHordeTargetSnapshot(rallyPoint + WatchDirection * CavemanFrontReach, CavemanFrontRadius,
                    definition.alliedUnitCanHitFlying, TowerTargetingMode.Closest, out var frontIndex, out var frontTarget, out _))
            {
                trackedHordeIndex = frontIndex;
                retargetTimer = CavemanRetargetSeconds;
                UpdateMeleeHordeCombat(frontIndex, frontTarget);
                return;
            }

            var searchFrom = IsCaveman ? rallyPoint + WatchDirection * CavemanFrontBias : transform.position;
            var searchRange = IsCaveman ? CavemanSectorRadius : definition.alliedUnitAggroRange;
            if (!IsRangedArcher && enemies.TryGetHordeTargetSnapshot(searchFrom, searchRange, definition.alliedUnitCanHitFlying, TowerTargetingMode.Closest, out var hordeIndex, out hordeTarget, out _))
            {
                if (IsCaveman)
                {
                    trackedHordeIndex = hordeIndex;
                    retargetTimer = CavemanRetargetSeconds;
                }

                UpdateMeleeHordeCombat(hordeIndex, hordeTarget);
                return;
            }

            trackedHordeIndex = -1;

            var target = IsCaveman
                ? enemies.GetNearestEnemy(rallyPoint, CavemanSectorRadius, definition.alliedUnitCanHitFlying)
                : enemies.GetNearestEnemy(transform.position, Mathf.Max(definition.alliedUnitAggroRange, definition.alliedUnitRange), definition.alliedUnitCanHitFlying);
            if (target == null)
            {
                if (IsCaveman)
                {
                    Patrol();
                }
                else
                {
                    MoveToward(rallyPoint);
                }

                return;
            }

            if (IsRangedArcher)
            {
                UpdateArcherCombat(target);
                return;
            }

            var attackRange = Mathf.Max(definition.alliedUnitRange, CombatRadius + target.Definition.visualScale * 0.5f + 0.2f);
            HasFacingTarget = true;
            FacingTarget = target.transform.position;
            InStrikeRange = IsWithinXzRange(target.transform.position, attackRange);
            if (!InStrikeRange)
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
            if (visual != null)
            {
                visual.MakeChief();
                PlaceHealthBarAbove(visual.Height);
            }
            else
            {
                SpecialEffectVisuals.AttachChiefHorns(transform);
            }

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
            HasFacingTarget = true;
            FacingTarget = targetPosition;
            InStrikeRange = IsWithinXzRange(targetPosition, attackRange);
            if (!InStrikeRange)
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
            var blow = definition.alliedUnitDamage * GetDamageMultiplier();
            if (IsCaveman)
            {
                TestStrikes++;
            }

            var appliedDamage = enemies.DamageHordeTarget(targetIndex, blow);
            // Only the chief's blows hurt the goblins next to the one struck (user, 2026-11-11).
            if (appliedDamage > 0f && IsChief && definition.alliedUnitSplashTargets > 1)
            {
                appliedDamage += enemies.DamageInRadius(targetPosition, CavemanSplashRadius, blow, definition.alliedUnitSplashTargets - 1, out _, targetIndex);
            }

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

        private void MoveToward(Vector3 destination, float speed = -1f)
        {
            var current = transform.position;
            destination.y = current.y;
            destination += GetSeparationOffset();
            if (IsCaveman)
            {
                destination = HoldOnRoad(destination);
            }

            var maxSpeed = speed > 0f ? speed : definition.alliedUnitMoveSpeed;
            if (IsCaveman)
            {
                // Gentle: speeds up and slows down instead of darting, and steps slowly in a fight.
                if (HasFacingTarget)
                {
                    maxSpeed = Mathf.Min(maxSpeed, CavemanCombatStepSpeed);
                }

                var to = destination - current;
                to.y = 0f;
                var distance = to.magnitude;
                var desired = distance > 0.08f ? to / distance * Mathf.Min(maxSpeed, distance * 2f) : Vector3.zero;
                smoothVelocity = Vector3.MoveTowards(smoothVelocity, desired, CavemanAcceleration * Time.deltaTime);
                transform.position = HoldOnRoad(current + smoothVelocity * Time.deltaTime);
                movedThisFrame = true;
                return;
            }

            if (IsWithinXzRange(destination, 0.08f))
            {
                return;
            }

            transform.position = Vector3.MoveTowards(current, destination, maxSpeed * Time.deltaTime);
        }

        // A caveman who did not walk this frame slows to a stop instead of halting dead.
        private void Coast()
        {
            if (smoothVelocity.sqrMagnitude < 1e-6f)
            {
                smoothVelocity = Vector3.zero;
                return;
            }

            smoothVelocity = Vector3.MoveTowards(smoothVelocity, Vector3.zero, CavemanAcceleration * 1.5f * Time.deltaTime);
            transform.position = HoldOnRoad(transform.position + smoothVelocity * Time.deltaTime);
        }

        // Kept on the road by his post once he has got there; on his way out of the cave he walks free
        // (held from the start, he was put on the road at once, out of nowhere: user, 2026-11-11).
        private Vector3 HoldOnRoad(Vector3 position) => reachedPost ? KeepNearPost(position) : position;
        private bool reachedPost;

        private static bool IsWithin(Vector3 a, Vector3 b, float range)
        {
            var offset = a - b;
            offset.y = 0f;
            return offset.sqrMagnitude <= range * range;
        }

        // Within the leash of his post and on the road.
        private Vector3 KeepNearPost(Vector3 destination)
        {
            var offset = destination - rallyPoint;
            offset.y = 0f;
            if (offset.sqrMagnitude > CavemanLeash * CavemanLeash)
            {
                offset = offset.normalized * CavemanLeash;
            }

            // His whole body on the road: no further from the road's middle line than the post's own
            // room allows, measured from the nearest point of that line (so it holds round bends,
            // and a branch road joining nearby does not count as room).
            var point = rallyPoint + offset;
            var route = enemies != null ? enemies.Route : null;
            if (route != null && route.TotalLength > 0f)
            {
                var middle = route.GetNearestRoadPoint(point, out _);
                middle.y = point.y;
                var out_ = point - middle;
                out_.y = 0f;
                // The road the horde fills is narrower than its corridor in the sums, so the room is
                // also kept to just past the post's own distance from the middle.
                var postMiddle = route.GetNearestRoadPoint(rallyPoint, out _);
                postMiddle.y = rallyPoint.y;
                var room = Mathf.Max(0.1f, Mathf.Min(postHalfWidth - CavemanRoadMargin, Vector3.Distance(rallyPoint, postMiddle) + 0.45f));
                if (out_.sqrMagnitude > room * room)
                {
                    point = middle + out_.normalized * room;
                }
            }

            return new Vector3(point.x, destination.y, point.z);
        }

        // Stroll to a spot a little up or down the road from the post, wait there a moment, then go
        // the other way.
        private void Patrol()
        {
            if (!IsWithinXzRange(patrolTarget, 0.15f))
            {
                // Back from a fight, or on the way: walk at a stroll unless far off.
                var far = !IsWithinXzRange(rallyPoint, CavemanLeash + 0.5f);
                MoveToward(far ? rallyPoint : patrolTarget, far ? -1f : CavemanPatrolSpeed);
                return;
            }

            patrolPause -= Time.deltaTime;
            if (patrolPause > 0f)
            {
                return;
            }

            patrolSide = -patrolSide;
            var reach = CavemanPatrolReach * Random.Range(0.5f, 1f);
            patrolTarget = KeepNearPost(rallyPoint + postAlong * patrolSide * reach);
            patrolPause = Random.Range(1.2f, 3.5f);
        }

        private Vector3 RoadAlong(Vector3 point)
        {
            var route = enemies != null ? enemies.Route : null;
            if (route == null || route.TotalLength <= 0f)
            {
                return Vector3.forward;
            }

            var distance = enemies.EstimatePathDistance(point);
            var along = route.Sample(Mathf.Min(route.TotalLength, distance + 0.5f)) - route.Sample(Mathf.Max(0f, distance - 0.5f));
            along.y = 0f;
            return along.sqrMagnitude < 0.001f ? Vector3.forward : along.normalized;
        }

        // The post for `slot` on the stretch of road nearest `near`. On a road wide enough for three
        // across, posts fan out from its middle (centre, right, left, further right, further left)
        // and the next row stands behind the first. On a narrower road they zig-zag down it, left and
        // right in turn, so each meets its own edge of the stream instead of queueing behind the one
        // in front. A post too close to another caveman's (a second cave nearby) moves on down the road.
        private Vector3 RoadPost(Vector3 near, int slot)
        {
            var route = enemies.Route;
            if (route == null || route.TotalLength <= 0f)
            {
                return enemies.GetNearestPathPosition(near);
            }

            var start = enemies.EstimatePathDistance(near);
            for (var shift = 0; shift < 12; shift++)
            {
                var distance = Mathf.Min(route.TotalLength, start + shift * CavemanRowSpacing);
                var post = PostAt(route, distance, slot, near.y);
                if (!IsPostTaken(post))
                {
                    return post;
                }
            }

            return PostAt(route, start, slot, near.y);
        }

        private static Vector3 PostAt(PathRoute route, float distance, int slot, float height)
        {
            var along = route.Sample(Mathf.Min(route.TotalLength, distance + 0.5f)) - route.Sample(Mathf.Max(0f, distance - 0.5f));
            along.y = 0f;
            along = along.sqrMagnitude < 0.001f ? Vector3.forward : along.normalized;
            var across = Vector3.Cross(Vector3.up, along);
            var halfWidth = HalfWidthAt(route, route.Sample(distance), across);
            var perSide = Mathf.FloorToInt(Mathf.Max(0f, halfWidth - CavemanRoadMargin) / CavemanLaneSpacing);
            float lane, down;
            if (perSide >= 1)
            {
                var perRow = 1 + 2 * perSide;
                var place = slot % perRow;
                lane = (place + 1) / 2 * CavemanLaneSpacing * (place % 2 == 1 ? 1f : -1f);
                down = slot / perRow * CavemanRowSpacing;
            }
            else
            {
                lane = (slot % 2 == 0 ? -1f : 1f) * Mathf.Max(0f, Mathf.Min(halfWidth * 0.55f, halfWidth - CavemanRoadMargin * 0.6f));
                down = slot * CavemanZigZagStep;
            }

            var center = route.Sample(Mathf.Min(route.TotalLength, distance + down));
            center.y = height;
            return center + across * lane;
        }

        // How wide the road is at `center`: step out until leaving it, on the narrower side.
        private static float HalfWidthAt(PathRoute route, Vector3 center, Vector3 across)
        {
            var halfWidth = 0f;
            while (halfWidth < 12f && route.IsInsideCorridor(center + across * (halfWidth + 0.1f)) &&
                   route.IsInsideCorridor(center - across * (halfWidth + 0.1f)))
            {
                halfWidth += 0.1f;
            }

            return halfWidth;
        }

        private bool IsPostTaken(Vector3 post)
        {
            foreach (var other in AllUnits)
            {
                if (other == null || other == this || !other.IsAlive || !other.IsCaveman)
                {
                    continue;
                }

                var gap = other.rallyPoint - post;
                gap.y = 0f;
                if (gap.sqrMagnitude < CavemanPostClearance * CavemanPostClearance)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 GetSeparationOffset()
        {
            var offset = Vector3.zero;
            // Cavemen with their model are broad, hides and all: they keep further apart than blocks.
            var spacing = visual != null ? 1.05f : 0.72f;
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
                if (distance <= 0.001f || distance > spacing)
                {
                    continue;
                }

                offset += away.normalized * (spacing - distance);
            }

            return Vector3.ClampMagnitude(offset, 0.85f);
        }

        // Ahead of him, up the road where the goblins come from (or right at him).
        private bool IsInFront(Vector3 position)
        {
            var offset = position - transform.position;
            offset.y = 0f;
            var distance = offset.magnitude;
            return distance < 0.4f || Vector3.Dot(offset / distance, WatchDirection) >= CavemanFrontCos;
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
            if (IsCaveman)
            {
                // Scaled with the small block it was 0.4 m wide and 5 cm deep: from above it was lost
                // against the body (user, 2026-11-11). Now its own size in metres: 0.9 m by 0.22 m.
                var unitScale = transform.localScale;
                root.transform.localScale = new Vector3(
                    CavemanBarWidth / 1.05f / Mathf.Max(0.01f, unitScale.x),
                    1.6f / Mathf.Max(0.01f, unitScale.y),
                    CavemanBarDepth / 0.14f / Mathf.Max(0.01f, unitScale.z));
            }

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

        // Over the model's head instead of over the block.
        private void PlaceHealthBarAbove(float height)
        {
            var bar = transform.Find("AlliedHealthBar");
            if (bar != null)
            {
                bar.localPosition = new Vector3(0f, (height + 0.25f) / Mathf.Max(0.01f, transform.localScale.y), 0f);
            }
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
