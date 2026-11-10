using TowerDefense.Data;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class TowerActor : MonoBehaviour, ICombatTarget, IOrientedCombatTarget
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
        // Spear Throwers: the hunters standing on the rock (greybox markers) and whose throw is next.
        private Transform[] crewMarkers;
        private int nextThrower;

        public TowerDefinition Definition => definition;
        public float DamageMultiplier => damageMultiplier;
        public float FireRateMultiplier => fireRateMultiplier;
        public float DamageDealt { get; private set; }
        public Vector3 Position => transform.position;
        public bool IsAlive => gameObject.activeSelf && (definition == null || definition.behavior != TowerBehavior.Barrier || health > 0f);
        public CombatTargetKind TargetKind => CombatTargetKind.Barrier;
        public float CombatRadius => definition != null && definition.behavior == TowerBehavior.Barrier ? CombatHalfDepth : 0.7f;
        public Vector3 CombatAxis => transform.right;
        public float CombatHalfLength => definition != null && definition.behavior == TowerBehavior.Barrier
            ? Mathf.Max(0.05f, Mathf.Abs(transform.lossyScale.x) * 0.5f)
            : 0f;
        public float CombatHalfDepth => definition != null && definition.behavior == TowerBehavior.Barrier
            ? Mathf.Max(0.05f, Mathf.Abs(transform.lossyScale.z) * 0.5f)
            : CombatRadius;
        public float BlockCapacity => definition != null && definition.behavior == TowerBehavior.Barrier ? 9999f : 0f;
        public float CurrentBlockedMass => GetBlockedMass();
        public float CurrentHealth => health;
        public float MaximumHealth => maxHealth;
        public float Armor => 0f;
        public float PhysicalResistance => 0f;
        public float FireResistance => 0f;
        public float SlowResistance => 0f;
        public float ThornsDamage => definition != null ? Mathf.Max(0f, definition.thornsDamage) : 0f;
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
            targetingMode = towerDefinition.defaultTargetingMode;
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
            else if (towerDefinition.behavior == TowerBehavior.Charge)
            {
                var charge = GetComponent<BeastCharge>() ?? gameObject.AddComponent<BeastCharge>();
                charge.Initialize(this, towerDefinition, enemies);
            }

            EnsureCrewMarkers();

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
                case TowerBehavior.Charge:
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

            if (TryFireAtHordeTarget(out var hordeTargetIndex))
            {
                if (definition.doubleShotChance > 0f && Random.value < definition.doubleShotChance)
                {
                    TryFireAtSecondHordeTarget(hordeTargetIndex);
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
                ? PlaceholderPalette.Grey(.493f)
                : definition.projectilePattern == ProjectilePattern.ArcSplash ? PlaceholderPalette.Grey(.369f) : PlaceholderPalette.Grey(.841f);
            ProjectileActor.Spawn(this, definition, target, enemies, definition.damage * damageMultiplier, projectileColor);
        }

        private bool TryFireAtHordeTarget(out int targetIndex)
        {
            targetIndex = -1;
            if (enemies == null || definition == null)
            {
                return false;
            }

            if (!enemies.TryGetHordeTargetSnapshot(
                    transform.position,
                    definition.range,
                    definition.canHitFlying,
                    targetingMode,
                    out targetIndex,
                    out var targetPosition,
                    out var targetVelocity))
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

            if (!enemies.TryReserveHordeDamage(targetIndex, damage))
            {
                return false;
            }

            if (definition.flyThroughShot)
            {
                SpawnHordePiercingShot(targetIndex, targetPosition, targetVelocity, damage);
                return true;
            }

            if (definition.pierce <= 0)
            {
                var duration = GetHordeShotDuration(targetPosition);
                var impactPosition = PredictHordeImpactPosition(targetPosition, targetVelocity, duration);
                StartCoroutine(ApplyDelayedHordeDirectHit(targetIndex, damage, duration, impactPosition));
                SpawnHordeShotVisual(impactPosition, duration);
                return true;
            }

            var start = transform.position + Vector3.up * 0.82f;
            var queued = enemies.QueueHordeProjectile(
                start,
                targetPosition,
                0.26f,
                damage,
                0f,
                Mathf.Max(1, definition.pierce + 1),
                definition.canHitFlying,
                splash: false);
            if (!queued)
            {
                enemies.ReleaseReservedHordeDamage(targetIndex, damage);
                return false;
            }
            enemies.ReleaseReservedHordeDamage(targetIndex, damage);
            RecordDamage(damage);
            SpawnHordeShotVisual(targetPosition, GetHordeShotDuration(targetPosition));
            return true;
        }

        private bool TryFireAtSecondHordeTarget(int excludedTargetIndex)
        {
            if (enemies == null || definition == null)
            {
                return false;
            }

            if (definition.pierce > 0 || !enemies.TryGetSecondaryHordeTargetSnapshot(
                    transform.position,
                    definition.range,
                    definition.canHitFlying,
                    targetingMode,
                    out var targetIndex,
                    out var targetPosition,
                    out var targetVelocity))
            {
                return false;
            }

            var damage = definition.damage * damageMultiplier;
            if (targetIndex == excludedTargetIndex || !enemies.TryReserveHordeDamage(targetIndex, damage))
            {
                return false;
            }
            var duration = GetHordeShotDuration(targetPosition);
            var impactPosition = PredictHordeImpactPosition(targetPosition, targetVelocity, duration);
            StartCoroutine(ApplyDelayedHordeDirectHit(targetIndex, damage, duration, impactPosition));
            SpawnHordeShotVisual(impactPosition, duration);
            return true;
        }

        private IEnumerator ApplyDelayedHordeDirectHit(int targetIndex, float damage, float delay, Vector3 impactPosition)
        {
            yield return new WaitForSeconds(delay);
            if (enemies == null)
            {
                yield break;
            }

            RecordDamage(enemies.ResolveReservedHordeDamage(targetIndex, damage));
            ApplyStoneSpecials(targetIndex, damage, impactPosition);
        }

        // Slinger specials (user, 2026-11-08). TEMPORARY sizes until balancing.
        // Shrapnel: the pebbles fly on past the target and hit inside a circle this far beyond it.
        private const float ShrapnelReach = 1.5f;
        private const float ShrapnelRadius = 1.2f;
        private const float ShrapnelSpreadDegrees = 30f;
        // Ricochet: each bounce hops this far on along the shot and hits one goblin near there.
        private const float RicochetHop = 1.6f;
        private const float RicochetRadius = 0.9f;
        // Poisoned spear tips are drawn green.
        private static readonly Color PoisonedSpearColor = new(0.42f, 0.78f, 0.24f, 1f);
        private const float RicochetSeconds = 0.12f;

        private void ApplyStoneSpecials(int targetIndex, float damage, Vector3 impactPosition)
        {
            if (enemies == null || definition == null)
            {
                return;
            }

            if (definition.stunChance > 0f && definition.stunSeconds > 0f && Random.value < definition.stunChance)
            {
                enemies.StunHordeTarget(targetIndex, definition.stunSeconds);
            }

            var direction = impactPosition - transform.position;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
            var stoneColor = PlaceholderPalette.Grey(.841f);
            var from = impactPosition + Vector3.up * 0.45f;

            if (definition.shrapnelTargets > 0 && definition.shrapnelDamageFraction > 0f)
            {
                RecordDamage(enemies.DamageInRadius(impactPosition + direction * ShrapnelReach, ShrapnelRadius,
                    damage * definition.shrapnelDamageFraction, definition.shrapnelTargets, out _, targetIndex));
                // The stone bursts: a puff where it struck and pebbles hopping on in a fan.
                SpecialEffectVisuals.SpawnPuff(impactPosition, 0.9f);
                var pebbles = Mathf.Clamp(definition.shrapnelTargets, 2, 6);
                for (var i = 0; i < pebbles; i++)
                {
                    var angle = Mathf.Lerp(-ShrapnelSpreadDegrees, ShrapnelSpreadDegrees, pebbles > 1 ? i / (pebbles - 1f) : 0.5f);
                    angle += Random.Range(-6f, 6f);
                    var reach = (ShrapnelReach + ShrapnelRadius) * Random.Range(0.7f, 1.05f);
                    var to = impactPosition + Quaternion.Euler(0f, angle, 0f) * direction * reach;
                    to.y = 0.05f;
                    HordeProjectileVisual.SpawnPebble(from, to, stoneColor, Random.Range(0.18f, 0.26f), Random.Range(0.3f, 0.55f), 0.09f);
                }
            }

            if (definition.ricochetBounces > 0 && definition.ricochetDamageFraction > 0f)
            {
                StartCoroutine(Ricochet(impactPosition, direction, damage * definition.ricochetDamageFraction,
                    definition.ricochetBounces, targetIndex));
            }
        }

        private IEnumerator Ricochet(Vector3 from, Vector3 direction, float damage, int bounces, int skipIndex)
        {
            var stoneColor = PlaceholderPalette.Grey(.841f);
            for (var bounce = 0; bounce < bounces; bounce++)
            {
                // Each bounce veers a little to one side, so a chain zigzags through the crowd.
                var veer = (bounce % 2 == 0 ? 1f : -1f) * Random.Range(10f, 25f);
                direction = Quaternion.Euler(0f, veer, 0f) * direction;
                var to = from + direction * RicochetHop;
                // It bounces: a puff where it touches down and a low hop to the next goblin.
                SpecialEffectVisuals.SpawnPuff(from, 0.6f);
                HordeProjectileVisual.SpawnPebble(from + Vector3.up * 0.15f, to + Vector3.up * 0.3f, stoneColor, RicochetSeconds,
                    0.45f, 0.12f);
                yield return new WaitForSeconds(RicochetSeconds);
                if (enemies == null)
                {
                    yield break;
                }

                RecordDamage(enemies.DamageInRadius(to, RicochetRadius, damage, 1, out _, skipIndex));
                from = to;
            }
        }

        // Greybox crew of the Spear Throwers: one small capsule per hunter on top of the rock, spread
        // around its centre. TEMPORARY until the hunters' model.
        private const float CrewSpotRadius = 0.28f;
        private const float CrewMarkerHeight = 0.5f;
        private const float ThrowHandHeight = 0.82f;

        private Vector3 GetCrewSpot(int index)
        {
            var count = Mathf.Max(1, definition.crewSize);
            var angle = (90f + index * 360f / count) * Mathf.Deg2Rad;
            var rockTop = transform.position.y + Mathf.Abs(transform.lossyScale.y) * 0.5f;
            var spot = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (count > 1 ? CrewSpotRadius : 0f);
            spot.y = rockTop;
            return spot;
        }

        private void EnsureCrewMarkers()
        {
            var crewSize = definition != null ? Mathf.Max(0, definition.crewSize) : 0;
            if (crewMarkers != null && crewMarkers.Length == crewSize)
            {
                return;
            }

            if (crewMarkers != null)
            {
                foreach (var old in crewMarkers)
                {
                    if (old != null)
                    {
                        Destroy(old.gameObject);
                    }
                }
                crewMarkers = null;
            }

            if (crewSize == 0)
            {
                return;
            }

            crewMarkers = new Transform[definition.crewSize];
            for (var i = 0; i < crewMarkers.Length; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                marker.name = $"Hunter_{i + 1}";
                SpearVisuals.RemoveColliders(marker);

                marker.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Grey(.62f));
                marker.transform.SetParent(transform, true);
                marker.transform.position = GetCrewSpot(i) + Vector3.up * CrewMarkerHeight * 0.5f;
                var parentScale = transform.lossyScale;
                marker.transform.localScale = new Vector3(
                    0.16f / Mathf.Max(0.01f, Mathf.Abs(parentScale.x)),
                    CrewMarkerHeight * 0.5f / Mathf.Max(0.01f, Mathf.Abs(parentScale.y)),
                    0.16f / Mathf.Max(0.01f, Mathf.Abs(parentScale.z)));
                crewMarkers[i] = marker.transform;
            }
        }

        // Where the next spear leaves from: the hand of the hunter whose turn it is. The hunters
        // take turns; each throw raises dust around that hunter's feet.
        private Vector3 TakeNextThrow()
        {
            if (definition.crewSize <= 0)
            {
                return transform.position + Vector3.up * ThrowHandHeight;
            }

            var thrower = nextThrower % definition.crewSize;
            nextThrower = (thrower + 1) % definition.crewSize;
            var feet = GetCrewSpot(thrower);
            if (definition.throwsSpears)
            {
                SpearVisuals.SpawnThrowDust(feet);
            }

            return new Vector3(feet.x, transform.position.y + ThrowHandHeight, feet.z);
        }

        private void SpawnHordePiercingShot(int reservedTargetIndex, Vector3 targetPosition, Vector3 targetVelocity, float damage)
        {
            var start = TakeNextThrow();
            var targetDuration = Vector3.Distance(start, targetPosition + Vector3.up * 0.45f) /
                                 Mathf.Max(0.01f, definition.projectileSpeed);
            var predictedTarget = PredictHordeImpactPosition(targetPosition, targetVelocity, targetDuration) + Vector3.up * 0.45f;
            var flatDirection = predictedTarget - start;
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude < 0.001f)
            {
                flatDirection = transform.forward;
            }
            flatDirection.Normalize();

            var travelDistance = Mathf.Max(definition.range * 1.5f, Vector3.Distance(start, predictedTarget) + 2f);
            var end = start + flatDirection * travelDistance;
            end.y = predictedTarget.y;
            var duration = travelDistance / Mathf.Max(0.01f, definition.projectileSpeed);
            var projectileId = enemies.CreateHordeProjectileId();
            var firstSegment = true;
            var poisoned = definition.poisonDamagePerSecond > 0f;
            var shotColor = poisoned ? PoisonedSpearColor : PlaceholderPalette.Grey(.841f);
            System.Action<Vector3, Vector3> onTravel = (segmentStart, segmentEnd) =>
                {
                    enemies.QueueHordeProjectile(
                        segmentStart,
                        segmentEnd,
                        0.2f,
                        damage,
                        // Spears push the goblins they hit on along their flight.
                        definition.knockbackDistance,
                        Mathf.Max(1, definition.pierce + 1),
                        definition.canHitFlying,
                        splash: false,
                        // Poisoned tips poison like a burn, drawn green on the goblins.
                        burnDamagePerSecond: definition.poisonDamagePerSecond,
                        burnDuration: definition.poisonDamagePerSecond > 0f ? definition.poisonSeconds : 0f,
                        maxBurnStacks: 3,
                        projectileId: projectileId,
                        resetHitCount: firstSegment,
                        hitDamageKeep: definition.pierceDamageKeep,
                        heavyDamageMultiplier: definition.heavyDamageMultiplier,
                        poison: poisoned);
                    firstSegment = false;
                };
            if (definition.throwsSpears)
            {
                // The spear drops to the ground at the end of its flight, or stops in the last
                // goblin it can pierce, and stays standing there for a few seconds.
                HordeProjectileVisual.SpawnSpear(start, end, shotColor, duration, onTravel, projectileId,
                    (ground, direction) => SpearVisuals.SpawnStuckSpear(ground, direction, shotColor));
            }
            else
            {
                HordeProjectileVisual.Spawn(start, end, shotColor, false, duration, 0.12f, onTravel, projectileId);
            }
            StartCoroutine(ReleaseHordeReservationAfterFlight(reservedTargetIndex, damage, targetDuration + 0.12f));
            RecordDamage(damage);
        }

        private IEnumerator ReleaseHordeReservationAfterFlight(int targetIndex, float damage, float delay)
        {
            yield return new WaitForSeconds(delay);
            enemies?.ReleaseReservedHordeDamage(targetIndex, damage);
        }

        private static Vector3 PredictHordeImpactPosition(Vector3 targetPosition, Vector3 targetVelocity, float duration)
        {
            targetVelocity.y = 0f;
            return targetPosition + targetVelocity * Mathf.Max(0f, duration);
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
            var queued = enemies.QueueHordeProjectile(
                transform.position,
                targetPosition,
                radius,
                damage,
                definition.knockbackDistance,
                160,
                definition.canHitFlying,
                splash: true,
                burnDamage * burnRate,
                burnDuration,
                burnStacks);
            if (queued)
            {
                RecordDamage(damage);
            }
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
                ? PlaceholderPalette.Grey(.493f)
                : definition.projectilePattern == ProjectilePattern.ArcSplash ? PlaceholderPalette.Grey(.369f) : PlaceholderPalette.Grey(.841f);
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
            // With the Chief bought, the first caveman out while no chief lives leads the others.
            if (definition.chiefAuraBonus > 0f || definition.chiefHealthBonus > 0f)
            {
                var hasChief = false;
                foreach (var other in alliedUnits)
                {
                    hasChief |= other != null && other.IsAlive && other.IsChief;
                }

                if (!hasChief)
                {
                    unit.MakeChief();
                }
            }

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

        public void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition)
        {
            if (definition == null || definition.behavior != TowerBehavior.Barrier)
            {
                return;
            }

            health = Mathf.Clamp(authoritativeHealth, 0f, maxHealth);
            UpdateBarrierDamageVisual();
            UpdateBarrierHealthBar();
            if (!destroyed || health > 0f)
            {
                return;
            }

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
            renderer.material = BootstrapMaterials.Get(Color.Lerp(PlaceholderPalette.Grey(.304f), definition.color, healthPercent));
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
            background.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Grey(.027f, 1f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "BarrierHealthBarFill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.64f, 0.012f, 0f);
            fill.transform.localScale = new Vector3(1.28f, 0.09f, 0.14f);
            fill.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Grey(.63f, 1f));
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
            auraDisc.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Grey(.644f, 0.12f));
        }

        private void EnsureSelectionVisual()
        {
            if (selectionDisc != null)
            {
                return;
            }

            selectionDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            selectionDisc.name = "TowerSelectionRange";
            selectionDisc.GetComponent<Renderer>().material = BootstrapMaterials.Get(PlaceholderPalette.Grey(.856f, 0.16f));
            selectionDisc.SetActive(false);
        }
    }
}
