using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The beast of a charge tower (the Tamed Mammoth). A mammoth waits in its pen until enemies
    /// are on the stretch of road it charges along, then runs onto the road and charges along it
    /// against the enemies' flow, trampling them and throwing them aside. Every enemy it runs into
    /// slows it down, so it ploughs through a thin crowd and bogs down in a thick one. Once it has
    /// slowed to a walk, or has run its full distance, it stops and fights where it stands: the
    /// horde attacks it like an allied unit and it stomps around itself, tiring as it fights, until
    /// it falls and sinks into the ground. A new mammoth appears in the pen once the tower's fire
    /// interval has passed.
    ///
    /// The beast is solid while it runs: it keeps shoving away every enemy that touches its body.
    /// While it fights it is a blocker of the horde, which keeps bodies out of it.
    /// </summary>
    public sealed class BeastCharge : MonoBehaviour, ICombatTarget, IOrientedCombatTarget
    {
        private enum State
        {
            Resting,
            ToRoad,
            Charging,
            Fighting,
            Falling
        }

        // Trample damage is applied in steps; each enemy under the beast is hit a few times per pass.
        private const float HitStepSeconds = 0.1f;
        private const float ShoveStepSeconds = 0.05f;
        // Running from the pen to the road is slower than the charge.
        private const float ToRoadSpeedFraction = 0.6f;
        private const float TurnDegreesPerSecond = 360f;
        private const int MaxTrampledPerStep = 40;
        private const int MaxThrownPerStep = 200;
        // The body: enemies closer than this to the beast's centre are shoved out hard.
        private const float BodyRadius = 1.6f;
        private const float BodyKnockback = 2.4f;
        // The body as a blocker: a capsule along the beast, 2.8 m long and 1.7 m wide.
        private const float BodyHalfLength = 1.4f;
        private const float BodyHalfWidth = 0.85f;
        // The charge slows down over its last metres instead of stopping dead.
        private const float SlowDownDistance = 4f;
        private const float SlowestChargeFraction = 0.3f;
        // Slowed below this, the charge is over and the beast fights where it stands.
        private const float StopSpeed = 1.5f;
        // Fighting: a stomp every so often hurts and pushes back the enemies around it.
        private const float StompSeconds = 0.8f;
        private const float StompRadius = 2.4f;
        private const float StompDamageFraction = 0.6f;
        private const float StompKnockback = 0.5f;
        private const int MaxStompedPerStep = 30;
        // It tires while it fights, losing this share of its health per second even untouched.
        private const float TiringPerSecond = 0.03f;
        private const float FallSeconds = 1.6f;
        private const float FallDepth = 2.6f;
        // Enemies count as on the stretch when one is this close to a point along it.
        private const float StretchProbeRadius = 3f;
        // Slingers on its back (a special): a volley every so often at the goblins ahead.
        private const float RiderSeconds = 0.7f;
        private const float RiderReach = 2.5f;
        private const float RiderRadius = 3f;
        private static readonly float[] StretchProbeDistances = { 1.5f, 5f, 8.5f };

        private TowerActor tower;
        private TowerDefinition definition;
        private EnemyManager enemies;
        private Transform beast;
        private Transform healthBar;
        private Transform healthFill;
        private Transform armorFill;
        private float riderTimer;
        private State state;
        private float cooldown;
        private float hitTimer;
        private float shoveTimer;
        private float stompTimer;
        private float fallTimer;
        private float runSpeed;
        private float chargeStart;
        private float chargeDistance;
        private float chargeEnd;
        private Vector3 roadStart;
        private Vector3 restPosition;
        private Vector3 fallStart;
        private PathRoute stretchRoute;
        private Vector3[] stretchProbes;
        private int reportId;
        // Health as the horde sees it (it is kept on the GPU while the beast fights), and what the
        // beast has lost to tiring on top of it.
        private float health;
        private float tiredness;
        private readonly List<EnemyActor> blockers = new();

        public bool IsCharging => state == State.Charging;
        public bool IsFighting => state == State.Fighting;
        // Out of the pen: running to the road, charging, fighting or falling.
        public bool IsOut => state != State.Resting;
        public int ChargeCount { get; private set; }
        public int FallCount { get; private set; }
        public int RiderVolleys { get; private set; }
        // Rage (a special): wounded below the threshold, it stomps faster and harder.
        public bool IsRaging => state == State.Fighting && definition != null && definition.rageBonus > 0f &&
                                RemainingHealth <= MaximumHealth * definition.rageThreshold;
        // How far along the road the last charge ran before it stopped.
        public float LastRunDistance { get; private set; }
        public Vector3 BeastPosition => beast != null ? beast.position : transform.position;

        public Vector3 Position => BeastPosition;
        public bool IsAlive => state == State.Fighting && RemainingHealth > 0f;
        public CombatTargetKind TargetKind => CombatTargetKind.AlliedUnit;
        public float CombatRadius => BodyHalfWidth;
        public float BlockCapacity => definition != null ? Mathf.Max(0f, definition.beastBlockCapacity) : 0f;
        public float CurrentBlockedMass
        {
            get
            {
                var mass = 0f;
                foreach (var blocker in blockers)
                {
                    mass += blocker != null && blocker.Definition != null ? blocker.Definition.mass : 1f;
                }

                return mass;
            }
        }

        public float CurrentHealth => RemainingHealth;
        // Its own health plus the armour (a special), which is lost first.
        public float MaximumHealth => definition != null
            ? Mathf.Max(1f, definition.beastHealth + definition.beastExtraHealth + definition.beastArmor)
            : 1f;
        private float OwnHealth => Mathf.Max(1f, MaximumHealth - definition.beastArmor);
        public float Armor => 0f;
        public float PhysicalResistance => 0f;
        public float FireResistance => 0f;
        public float SlowResistance => 0f;
        public float ThornsDamage => 0f;
        public Vector3 CombatAxis => beast != null ? beast.forward : Vector3.forward;
        public float CombatHalfLength => BodyHalfLength;
        public float CombatHalfDepth => BodyHalfWidth;
        private float RemainingHealth => Mathf.Max(0f, health - tiredness);

        public void Initialize(TowerActor owner, TowerDefinition towerDefinition, EnemyManager enemyManager)
        {
            tower = owner;
            definition = towerDefinition;
            enemies = enemyManager;
            restPosition = transform.position;
            if (beast == null)
            {
                beast = CreateBeast(transform.parent).transform;
                CreateHealthBar();
            }

            reportId = enemies.CreateAreaReport();
            beast.position = restPosition;
            beast.gameObject.SetActive(true);
            state = State.Resting;
            cooldown = 0.5f;
        }

        private void OnDestroy()
        {
            enemies?.UnregisterCombatTarget(this);
            if (beast != null)
            {
                Destroy(beast.gameObject);
            }

            if (healthBar != null)
            {
                Destroy(healthBar.gameObject);
            }
        }

        private void Update()
        {
            if (definition == null || enemies == null || beast == null)
            {
                return;
            }

            var route = enemies.Route;
            switch (state)
            {
                case State.Resting:
                    cooldown -= Time.deltaTime;
                    if (cooldown > 0f)
                    {
                        break;
                    }

                    // A new mammoth stands in the pen.
                    if (!beast.gameObject.activeSelf)
                    {
                        beast.position = restPosition;
                        beast.gameObject.SetActive(true);
                    }

                    if (route != null && route.TotalLength > 0f && EnemiesOnStretch(route))
                    {
                        BeginCharge(route);
                    }

                    break;
                case State.ToRoad:
                    if (MoveTowards(roadStart, definition.chargeSpeed * ToRoadSpeedFraction))
                    {
                        state = State.Charging;
                        runSpeed = definition.chargeSpeed;
                        hitTimer = 0f;
                        enemies.TakeAreaReport(reportId);
                        enemies.TakeBlockedReport(reportId);
                    }

                    break;
                case State.Charging:
                    UpdateCharge(route);
                    break;
                case State.Fighting:
                    UpdateFight();
                    break;
                case State.Falling:
                    UpdateFall();
                    break;
            }

            if (state == State.ToRoad || state == State.Charging)
            {
                ShoveAwayFromBody();
            }

            if (state == State.Charging || state == State.Fighting)
            {
                UpdateRiders();
            }

            UpdateHealthBar();
        }

        // True when enemies are on the first part of the road the beast would charge along, so the
        // charge meets them instead of running along an empty road.
        private bool EnemiesOnStretch(PathRoute route)
        {
            if (stretchRoute != route || stretchProbes == null)
            {
                // The probes stay at fixed points so the horde's cached target queries can answer.
                stretchRoute = route;
                var start = enemies.EstimatePathDistance(transform.position);
                stretchProbes = new Vector3[StretchProbeDistances.Length];
                for (var i = 0; i < stretchProbes.Length; i++)
                {
                    stretchProbes[i] = route.Sample(Mathf.Max(0f, start - StretchProbeDistances[i]));
                }
            }

            foreach (var probe in stretchProbes)
            {
                if (enemies.TryGetHordeTargetSnapshot(probe, StretchProbeRadius, false, TowerTargetingMode.Closest, out _, out _, out _) ||
                    enemies.GetEnemyByTargetingMode(probe, StretchProbeRadius, false, TowerTargetingMode.Closest) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void BeginCharge(PathRoute route)
        {
            // Enemies walk from the start of the road (distance 0) towards its end, so the beast
            // runs towards the start, head-on into them.
            chargeStart = enemies.EstimatePathDistance(transform.position);
            chargeDistance = chargeStart;
            chargeEnd = Mathf.Max(0f, chargeStart - definition.chargeDistance);
            roadStart = route.Sample(chargeStart);
            health = MaximumHealth;
            tiredness = 0f;
            state = State.ToRoad;
            ChargeCount++;
        }

        private void UpdateCharge(PathRoute route)
        {
            if (route == null)
            {
                BeginFight();
                return;
            }

            // Every enemy it ran into since the last frame slows it down, and one it cannot push
            // (an Orc Brute before the upgrade, the boss) stops it where it stands.
            runSpeed -= enemies.TakeAreaReport(reportId) * definition.chargeSpeedLossPerMass *
                        (1f - Mathf.Clamp(definition.beastCrowdResistance, 0f, 0.9f));
            if (enemies.TakeBlockedReport(reportId) > 0f)
            {
                BeginFight();
                return;
            }

            var remaining = chargeDistance - chargeEnd;
            var speed = Mathf.Min(runSpeed,
                definition.chargeSpeed * Mathf.Lerp(SlowestChargeFraction, 1f, Mathf.Clamp01(remaining / SlowDownDistance)));
            if (runSpeed <= StopSpeed || chargeDistance <= chargeEnd + 0.001f)
            {
                BeginFight();
                return;
            }

            chargeDistance = Mathf.Max(chargeEnd, chargeDistance - speed * Time.deltaTime);
            var position = route.Sample(chargeDistance);
            Face(position - beast.position, 1080f);
            beast.position = position;

            hitTimer -= Time.deltaTime;
            if (hitTimer <= 0f)
            {
                hitTimer += HitStepSeconds;
                Trample(position, speed);
            }
        }

        // Each enemy is under the beast for about 2 * radius / speed seconds, so the damage per step
        // is spread to add up to the tower's damage per enemy over one pass at full speed.
        private void Trample(Vector3 position, float speed)
        {
            var stepsUnderBeast = Mathf.Max(1f, 2f * definition.chargeRadius / Mathf.Max(0.1f, definition.chargeSpeed) / HitStepSeconds);
            var damage = definition.damage * tower.DamageMultiplier / stepsUnderBeast;
            // At most about as many goblins as fit under the beast; the GPU horde reports damage as
            // damage times this cap, so it also keeps the tower's damage figure from running away.
            var dealt = enemies.DamageAndKnockbackInRadius(position, definition.chargeRadius, damage, 0f, MaxTrampledPerStep, out _);
            tower.RecordDamage(dealt);
            // Across the whole road: the horde hugs the inside of bends, and the beast runs down
            // the middle. Goblins are thrown out from the beast, up to MaxThrownPerStep per step;
            // the throw speed falls from 8x to 2.5x the knockback from the beast to the edge, and a
            // slowing beast throws less hard. The goblins it hits on the ground slow it down.
            var force = Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(StopSpeed, definition.chargeSpeed, speed));
            enemies.DamageAndKnockbackInRadius(position, definition.chargeRadius, 0f, definition.knockbackDistance * force, MaxThrownPerStep, out _,
                launch: definition.knockbackLaunch, reportId: reportId, maxPushMass: PushLimit);
        }

        private void BeginFight()
        {
            state = State.Fighting;
            LastRunDistance = chargeStart - chargeDistance;
            stompTimer = 0f;
            blockers.Clear();
            enemies.RegisterCombatTarget(this);
        }

        private void UpdateFight()
        {
            tiredness += MaximumHealth * TiringPerSecond * Time.deltaTime;
            if (RemainingHealth <= 0f)
            {
                BeginFall();
                return;
            }

            stompTimer -= Time.deltaTime;
            if (stompTimer > 0f)
            {
                return;
            }

            var rage = IsRaging ? 1f + definition.rageBonus : 1f;
            stompTimer += StompSeconds / rage;
            // Pushes the goblins around it back along the ground, without throwing them.
            var damage = definition.damage * tower.DamageMultiplier * StompDamageFraction * rage;
            var dealt = enemies.DamageAndKnockbackInRadius(beast.position, StompRadius, damage, StompKnockback, MaxStompedPerStep, out _,
                maxPushMass: PushLimit);
            tower.RecordDamage(dealt);
        }

        // The slingers on its back throw at the goblins ahead of it while it charges and fights.
        private void UpdateRiders()
        {
            if (definition.riderCount <= 0 || definition.riderDamage <= 0f)
            {
                return;
            }

            riderTimer -= Time.deltaTime;
            if (riderTimer > 0f)
            {
                return;
            }

            riderTimer += RiderSeconds;
            RiderVolleys++;
            var center = beast.position + beast.forward * RiderReach;
            center.y = 0f;
            var dealt = enemies.DamageInRadius(center, RiderRadius, definition.riderDamage * tower.DamageMultiplier,
                definition.riderCount, out _);
            tower.RecordDamage(dealt);
            for (var i = 0; i < definition.riderCount; i++)
            {
                var spot = UnityEngine.Random.insideUnitCircle * RiderRadius * 0.7f;
                HordeProjectileVisual.Spawn(beast.position + Vector3.up * 2.6f, center + new Vector3(spot.x, 0.3f, spot.y),
                    PlaceholderPalette.Grey(.841f), false, 0.18f, 0.1f);
            }
        }

        private void BeginFall()
        {
            state = State.Falling;
            health = 0f;
            fallTimer = 0f;
            fallStart = beast.position;
            blockers.Clear();
            enemies.UnregisterCombatTarget(this);
            FallCount++;
        }

        // It sinks into the crowd and the ground, then the next mammoth waits for its turn.
        private void UpdateFall()
        {
            fallTimer += Time.deltaTime;
            var t = Mathf.Clamp01(fallTimer / FallSeconds);
            beast.position = fallStart + Vector3.down * (FallDepth * t * t);
            if (t < 1f)
            {
                return;
            }

            beast.gameObject.SetActive(false);
            beast.position = restPosition;
            beast.rotation = Quaternion.identity;
            state = State.Resting;
            cooldown = definition.fireInterval / Mathf.Max(0.05f, tower.FireRateMultiplier);
        }

        public bool TryAddBlocker(EnemyActor enemy)
        {
            if (enemy == null || !IsAlive)
            {
                return false;
            }

            if (blockers.Contains(enemy))
            {
                return true;
            }

            var mass = enemy.Definition != null ? enemy.Definition.mass : 1f;
            if (CurrentBlockedMass + mass > BlockCapacity)
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

        public void TakeDamage(float damage, EnemyActor source)
        {
            if (!IsAlive)
            {
                return;
            }

            health -= Mathf.Max(0f, damage);
            if (RemainingHealth <= 0f)
            {
                BeginFall();
            }
        }

        public void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition)
        {
            if (state != State.Fighting)
            {
                return;
            }

            health = Mathf.Clamp(authoritativeHealth, 0f, MaximumHealth);
            if (destroyed || RemainingHealth <= 0f)
            {
                BeginFall();
            }
        }

        private void ShoveAwayFromBody()
        {
            shoveTimer -= Time.deltaTime;
            if (shoveTimer > 0f)
            {
                return;
            }

            shoveTimer += ShoveStepSeconds;
            enemies.DamageAndKnockbackInRadius(beast.position, BodyRadius, 0f, BodyKnockback, 0, out _,
                reportId: state == State.Charging ? reportId : 0, maxPushMass: PushLimit);
        }

        // The heaviest enemy it can push: Goblin Runners but not Orc Brutes, until the upgrade.
        private float PushLimit => definition.pushesHeavy ? 0f : definition.chargeMaxPushMass;

        private bool MoveTowards(Vector3 destination, float speed)
        {
            var offset = destination - beast.position;
            offset.y = 0f;
            var step = speed * Time.deltaTime;
            if (offset.sqrMagnitude <= step * step)
            {
                beast.position = destination;
                return true;
            }

            var direction = offset.normalized;
            beast.position += direction * step;
            Face(direction, TurnDegreesPerSecond);
            return false;
        }

        private void Face(Vector3 direction, float degreesPerSecond)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return;
            }

            beast.rotation = Quaternion.RotateTowards(beast.rotation, Quaternion.LookRotation(direction.normalized, Vector3.up), degreesPerSecond * Time.deltaTime);
        }

        // A flat bar above the beast, shown only while it fights.
        private void CreateHealthBar()
        {
            var root = new GameObject("BeastHealthBar");
            root.transform.SetParent(transform.parent, false);
            healthBar = root.transform;
            var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            background.name = "Background";
            background.transform.SetParent(healthBar, false);
            background.transform.localScale = new Vector3(2f, 0.08f, 0.22f);
            background.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Grey(0.03f, 1f));
            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "Fill";
            fill.transform.SetParent(healthBar, false);
            fill.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Grey(0.735f, 1f));
            healthFill = fill.transform;
            // The armour (a special) shows as a lighter stretch after its own health.
            var armor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            armor.name = "Armor";
            armor.transform.SetParent(healthBar, false);
            armor.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Grey(0.95f, 1f));
            armorFill = armor.transform;
            root.SetActive(false);
        }

        private void UpdateHealthBar()
        {
            if (healthBar == null)
            {
                return;
            }

            var show = state == State.Fighting;
            if (healthBar.gameObject.activeSelf != show)
            {
                healthBar.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            healthBar.position = beast.position + new Vector3(0f, 2.9f, 0f);
            var normalized = Mathf.Clamp01(Mathf.Min(RemainingHealth, OwnHealth) / MaximumHealth);
            healthFill.localScale = new Vector3(2f * normalized, 0.09f, 0.24f);
            healthFill.localPosition = new Vector3(-1f + normalized, 0.01f, 0f);
            var armorShare = Mathf.Clamp01(Mathf.Max(0f, RemainingHealth - OwnHealth) / MaximumHealth);
            armorFill.gameObject.SetActive(armorShare > 0f);
            armorFill.localScale = new Vector3(2f * armorShare, 0.095f, 0.25f);
            armorFill.localPosition = new Vector3(-1f + 2f * normalized + armorShare, 0.012f, 0f);
        }

        // Greybox mammoth, about 2.6 m long and 2.2 m tall, facing +Z.
        private static GameObject CreateBeast(Transform parent)
        {
            var root = new GameObject("Beast");
            root.transform.SetParent(parent, false);
            var material = BootstrapMaterials.Get(PlaceholderPalette.Grey(0.62f));
            AddPart(root.transform, PrimitiveType.Capsule, new Vector3(0f, 1.15f, 0f), new Vector3(90f, 0f, 0f), new Vector3(1.4f, 1.3f, 1.3f), material);
            AddPart(root.transform, PrimitiveType.Sphere, new Vector3(0f, 1.6f, 1.3f), Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), material);
            AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 1.85f), new Vector3(25f, 0f, 0f), new Vector3(0.28f, 0.55f, 0.28f), material);
            foreach (var side in new[] { -1f, 1f })
            {
                AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0.32f * side, 1.15f, 1.85f), new Vector3(70f, 0f, 0f), new Vector3(0.12f, 0.45f, 0.12f), BootstrapMaterials.Get(PlaceholderPalette.Accent));
            }

            return root;
        }

        private static void AddPart(Transform parent, PrimitiveType type, Vector3 position, Vector3 euler, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
