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
    /// horde attacks it like an allied unit and it scoops at the goblins in front with its tusks, tiring as it fights, until
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
            // Out on the road, ready, waiting for goblins to come (user, 2026-11-11).
            Waiting,
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
        // The model's rump reaches further back than the capsule: it is lengthened backwards by this
        // much, so goblins do not stand inside its hindquarters (user, 2026-11-11). TEMPORARY.
        private const float BodyBackExtra = 0.6f;
        // The charge slows down over its last metres instead of stopping dead.
        private const float SlowDownDistance = 4f;
        private const float SlowestChargeFraction = 0.3f;
        // Slowed below this, the charge is over and the beast fights where it stands.
        private const float StopSpeed = 1.5f;
        // Fighting (user, 2026-11-09): every StompSeconds it dips its head and scoops up with its
        // tusks, hitting the goblins in front of it and tossing them up and away ahead and to the
        // sides. It used to stomp in a circle around itself, which showed no movement at all.
        // The blow lands ScoopHitSeconds into the swing. Sizes TEMPORARY until balancing.
        // Half as often as before, and the scoop itself played a little slower, heavier (user,
        // 2026-11-11). TEMPORARY.
        // Then 30% slower again, swing and all (user, 2026-11-11).
        private const float StompSeconds = 2.1f;
        private const float ScoopPlaySpeed = 0.58f;
        // Rage of its own (user, 2026-11-11): wounded to 30% it turns red, steams and scoops 20%
        // faster. The Rage special adds to it: a higher threshold, faster and harder scoops. TEMPORARY.
        private const float InnateRageThreshold = 0.3f;
        private const float InnateRageTempo = 0.2f;
        // As the rage comes over it, it stops and roars before it scoops on (user, 2026-11-11): the
        // model's 1.2 s Rage clip played 50% longer. It takes no damage while it roars, but still
        // blocks the horde.
        public const float RoarStretch = 1.5f;
        private const float RoarSeconds = 1.2f * RoarStretch;
        private const float RoarArmor = 1e6f;
        // Its body crushes the goblins it falls on (user, 2026-11-11): this many times the tower's
        // damage to each. TEMPORARY.
        private const float CrushDamageFactor = 2f;
        private const float StompDamageFraction = 0.6f;
        private const int MaxStompedPerStep = 30;
        private const float ScoopReach = 1.8f;
        private const float ScoopRadius = 1.7f;
        private const float ScoopKnockback = 8f;
        private const float ScoopLaunch = 1.1f;
        // How much of the toss goes ahead rather than sideways (1 would be the rock's mostly
        // sideways throw).
        private const float ScoopForward = 3f;
        private const float ScoopHitSeconds = 0.2f;
        private const float ScoopSwingSeconds = 0.55f;
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
        public bool IsWaiting => state == State.Waiting;
        public bool IsFighting => state == State.Fighting;
        public bool IsFalling => state == State.Falling;
        public bool IsRoaring => state == State.Fighting && roarTimer > 0f;
        private float roarTimer;
        private bool roared;
        private bool crushed;
        // How fast it scoops: faster while raging.
        public float FightTempo => IsRaging ? 1f + InnateRageTempo + definition.rageBonus : 1f;
        // How fast the scoop plays (the model's clip keeps to it).
        public float ScoopClipSpeed => FightTempo * ScoopPlaySpeed;
        // Out of the pen: running to the road, charging, fighting or falling.
        public bool IsOut => state != State.Resting;
        public int ChargeCount { get; private set; }
        public int FallCount { get; private set; }
        public int RiderVolleys { get; private set; }
        // Rage: wounded below the threshold, it scoops faster (and harder with the special).
        public bool IsRaging => state == State.Fighting && definition != null &&
                                RemainingHealth <= MaximumHealth * Mathf.Max(InnateRageThreshold, definition.rageThreshold);
        // How far along the road the last charge ran before it stopped.
        public float LastRunDistance { get; private set; }
        public Vector3 BeastPosition => beast != null ? beast.position : transform.position;
        public PathRoute Route => enemies != null ? enemies.Route : null;
        // Testing only: the recordings give the mammoth this many times its health.
        internal static float TestHealthMultiplier = 1f;

        // The middle of the blocking capsule: behind the beast's middle by half the rump's extra length.
        public Vector3 Position => BeastPosition - CombatAxis * (BodyBackExtra * 0.5f);
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
            ? Mathf.Max(1f, (definition.beastHealth + definition.beastExtraHealth) * Mathf.Max(0.01f, TestHealthMultiplier) + definition.beastArmor)
            : 1f;
        private float OwnHealth => Mathf.Max(1f, MaximumHealth - definition.beastArmor);
        public float Armor => IsRoaring ? RoarArmor : 0f;
        public float PhysicalResistance => 0f;
        public float FireResistance => 0f;
        public float SlowResistance => 0f;
        public float ThornsDamage => 0f;
        public Vector3 CombatAxis => beast != null ? beast.forward : Vector3.forward;
        public float CombatHalfLength => BodyHalfLength + BodyBackExtra * 0.5f;
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
                // The mammoth's and the pen's models, when installed, replace the greybox. The pen
                // stands back from the tower's spot with its gate on it, facing the road.
                visual = MammothVisual.TryCreate(this, beast);
                pen = MammothVisual.TryCreatePen(transform.parent, restPosition + PenOutward(enemies.Route) * PenGateAhead, PenOutward(enemies.Route));
                if (pen != null && GetComponent<Renderer>() != null)
                {
                    GetComponent<Renderer>().enabled = false;
                }
            }

            reportId = enemies.CreateAreaReport();
            beast.position = restPosition;
            beast.gameObject.SetActive(true);
            state = State.Resting;
            cooldown = 0.5f;
        }

        private void OnDestroy()
        {
            ReleaseCorpse();
            HordeMounds.Clear(this);
            enemies?.UnregisterCombatTarget(this);
            if (beast != null)
            {
                Destroy(beast.gameObject);
            }

            if (healthBar != null)
            {
                Destroy(healthBar.gameObject);
            }

            if (pen != null)
            {
                Destroy(pen);
            }
        }

        private MammothVisual visual;
        private GameObject pen;
        // With the pen's model: its gate this far out from the tower's spot towards the road, and the
        // waiting mammoth's middle this far behind the tower's spot: at the back, and at the gate when ready
        // (its head just out of the gate). TEMPORARY.
        private const float PenGateAhead = 0.3f;
        private const float ModelPenBack = MammothVisual.PenWidth * 0.5f - PenGateAhead + 0.3f;
        private const float ModelPenFront = MammothVisual.PenWidth * 0.5f - PenGateAhead - 0.6f;
        // With the model it lies dead this long (its fall) before it sinks; the next mammoth's wait
        // is shortened to match, so the cycle stays as long as before.
        private float FallLieSeconds => visual != null ? visual.LieSeconds : 0f;
        // With the model the horde climbs over the body as it sinks, so it sinks slowly and evenly. TEMPORARY.
        private float SinkSeconds => visual != null ? 6f : FallSeconds;
        // Lying across the whole road, it is pushed on down the road by the crowd behind it. TEMPORARY.
        private const float CorpsePushSpeed = 0.4f;

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
                    UpdateInPen(route);
                    if (cooldown > 0f)
                    {
                        break;
                    }

                    // Ready, it goes out onto the road whether goblins are coming or not, and waits
                    // there for them (user, 2026-11-11).
                    if (route != null && route.TotalLength > 0f)
                    {
                        BeginCharge(route);
                    }

                    break;
                case State.ToRoad:
                    if (MoveTowards(roadStart, definition.chargeSpeed * ToRoadSpeedFraction))
                    {
                        if (route != null && EnemiesOnStretch(route))
                        {
                            BeginRun();
                        }
                        else
                        {
                            state = State.Waiting;
                        }
                    }

                    break;
                case State.Waiting:
                    // Facing up the road, the way the goblins come; it runs at them once they are near.
                    if (route != null)
                    {
                        Face(route.Sample(Mathf.Max(0f, chargeStart - 1f)) - beast.position, 90f);
                        if (EnemiesOnStretch(route))
                        {
                            BeginRun();
                        }
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

            UpdateRageTint();
            UpdateHeadPose();
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
            roared = false;
            roarTimer = 0f;
            state = State.ToRoad;
        }

        // It runs at the goblins: a charge (going out to wait on the road is not one).
        private void BeginRun()
        {
            ChargeCount++;
            state = State.Charging;
            runSpeed = definition.chargeSpeed;
            hitTimer = 0f;
            enemies.TakeAreaReport(reportId);
            enemies.TakeBlockedReport(reportId);
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
            scoopAge = -1f;
            blockers.Clear();
            enemies.RegisterCombatTarget(this);
        }

        private void UpdateFight()
        {
            // It does not tire while it roars either: it loses no health at all.
            if (!IsRoaring)
            {
                tiredness += MaximumHealth * TiringPerSecond * Time.deltaTime;
            }

            if (RemainingHealth <= 0f)
            {
                BeginFall();
                return;
            }

            // It scoops at what is in front of it, so it turns to face up the road, into the
            // oncoming horde; bogged down as it came onto the road it would face the roadside.
            var route = enemies.Route;
            if (route != null && route.TotalLength > 0f)
            {
                var upRoad = route.Sample(Mathf.Max(0f, chargeDistance - 2f)) - beast.position;
                upRoad.y = 0f;
                if (upRoad.sqrMagnitude > 0.04f)
                {
                    Face(upRoad, 180f);
                }
            }

            // The rage comes over it: it roars first (once a scoop in progress is done).
            if (IsRaging && !roared && scoopAge < 0f)
            {
                roared = true;
                roarTimer = RoarSeconds;
            }

            if (roarTimer > 0f)
            {
                roarTimer -= Time.deltaTime;
                stompTimer = 0f;
                return;
            }

            stompTimer -= Time.deltaTime;
            if (stompTimer <= 0f && scoopAge < 0f)
            {
                stompTimer += StompSeconds / FightTempo;
                scoopAge = 0f;
                scoopLanded = false;
            }

            if (scoopAge < 0f)
            {
                return;
            }

            scoopAge += Time.deltaTime * ScoopClipSpeed;
            if (!scoopLanded && scoopAge >= ScoopHitSeconds)
            {
                scoopLanded = true;
                Scoop();
            }

            if (scoopAge >= ScoopSwingSeconds)
            {
                scoopAge = -1f;
            }
        }

        // The blow of the scoop: hurts the goblins in front and tosses them ahead and aside.
        private void Scoop()
        {
            var rage = IsRaging ? 1f + definition.rageBonus : 1f;
            var damage = definition.damage * tower.DamageMultiplier * StompDamageFraction * rage;
            var forward = beast.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            var center = beast.position + forward * ScoopReach;
            center.y = 0f;
            var dealt = enemies.DamageAndKnockbackInRadius(center, ScoopRadius, damage, 0f, MaxStompedPerStep, out _,
                maxPushMass: PushLimit);
            tower.RecordDamage(dealt);
            enemies.ThrowOffLine(center, ScoopRadius, ScoopKnockback * rage, ScoopLaunch, forward, ScoopForward, PushLimit);
            ScoopCount++;
            SpecialEffectVisuals.SpawnPuff(center, IsRaging ? 2f : 1.4f);
        }

        /// <summary>Scoops so far (tests).</summary>
        public int ScoopCount { get; private set; }

        /// <summary>
        /// Where the head is in its scoop, 0 to 1 (-1 between scoops), for the mammoth's animation.
        /// </summary>
        public float ScoopProgress => scoopAge < 0f ? -1f : Mathf.Clamp01(scoopAge / ScoopSwingSeconds);

        private float scoopAge = -1f;
        private bool scoopLanded;

        // Greybox head: dips, sweeps up through the blow, and settles. TEMPORARY until the
        // mammoth's own animation.
        private void UpdateHeadPose()
        {
            headPivot ??= beast.Find("Head");
            if (headPivot == null)
            {
                return;
            }

            var pitch = 0f;
            if (state == State.Fighting && scoopAge >= 0f)
            {
                const float dipEnd = ScoopHitSeconds * 0.7f;
                const float sweepEnd = ScoopHitSeconds + 0.1f;
                if (scoopAge < dipEnd)
                {
                    pitch = Mathf.SmoothStep(0f, 28f, scoopAge / dipEnd);
                }
                else if (scoopAge < sweepEnd)
                {
                    pitch = Mathf.Lerp(28f, -40f, (scoopAge - dipEnd) / (sweepEnd - dipEnd));
                }
                else
                {
                    pitch = Mathf.SmoothStep(-40f, 0f, (scoopAge - sweepEnd) / (ScoopSwingSeconds - sweepEnd));
                }
            }

            // Grazing while it waits in the pen; it lifts its head as it gets ready.
            if (state == State.Resting && PenReadiness < 1f)
            {
                var graze = Mathf.Clamp01((0.85f - PenReadiness) / 0.15f);
                pitch = graze * (22f + 8f * Mathf.Sin(Time.time * 2.6f));
            }

            headPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private Transform headPivot;

        // Rage (a special) shows as a red pulse over the whole beast while it lasts. Works on any
        // model under the beast, so it carries over to the real mammoth.
        private static readonly Color RageColor = new(0.85f, 0.18f, 0.12f, 1f);
        private Renderer[] beastRenderers;
        private MaterialPropertyBlock rageBlock;
        private bool rageShown;

        private void UpdateRageTint()
        {
            var raging = IsRaging;
            if (!raging && !rageShown)
            {
                return;
            }

            beastRenderers ??= beast.GetComponentsInChildren<Renderer>(true);
            rageBlock ??= new MaterialPropertyBlock();
            foreach (var renderer in beastRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!raging)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }

                var baseColor = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color")
                    ? renderer.sharedMaterial.color
                    : PlaceholderPalette.Grey(0.62f);
                var pulse = 0.35f + 0.25f * Mathf.Sin(Time.time * 9f);
                var tinted = Color.Lerp(baseColor, RageColor, pulse);
                renderer.GetPropertyBlock(rageBlock);
                rageBlock.SetColor("_Color", tinted);
                rageBlock.SetColor("_BaseColor", tinted);
                renderer.SetPropertyBlock(rageBlock);
            }

            rageShown = raging;
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
            crushed = false;
            roarTimer = 0f;
            fallStart = beast.position;
            blockers.Clear();
            enemies.UnregisterCombatTarget(this);
            FallCount++;
        }

        // It sinks into the crowd and the ground, then the next mammoth waits for its turn.
        private void UpdateFall()
        {
            fallTimer += Time.deltaTime;
            KeepHordeOffBody();
            var t = Mathf.Clamp01((fallTimer - FallLieSeconds) / SinkSeconds);
            // The model sinks evenly while the horde climbs over it; the greybox drops away.
            var depth = visual != null ? (visual.BackHeight + 0.4f) * t : FallDepth * t * t;
            PushCorpseDownRoad(depth);
            beast.position = fallStart + Vector3.down * depth;
            UpdateMound(depth);
            if (t < 1f)
            {
                return;
            }

            ReleaseCorpse();
            HordeMounds.Clear(this);
            state = State.Resting;
            cooldown = Mathf.Max(0.5f, definition.fireInterval / Mathf.Max(0.05f, tower.FireRateMultiplier) - FallLieSeconds - (SinkSeconds - FallSeconds));
            penWaitSeconds = cooldown;
            // The next mammoth is already in the pen, at the back, and walks to the gate while it
            // gets ready (user, 2026-11-09): the player sees when it comes without a timer.
            beast.position = PenBack(enemies.Route);
            beast.rotation = Quaternion.LookRotation(PenOutward(enemies.Route), Vector3.up);
        }

        // In the pen, between mammoths. TEMPORARY sizes until the pen's model.
        private const float PenHalfDepth = 0.9f;
        private float penWaitSeconds;

        /// <summary>How far the waiting mammoth is from ready, 0 to 1 (1: at the gate).</summary>
        public float PenReadiness => state != State.Resting ? 1f
            : penWaitSeconds <= 0f ? 1f : Mathf.Clamp01(1f - cooldown / penWaitSeconds);

        // From the pen towards the road: the way out through the gate.
        private Vector3 PenOutward(PathRoute route)
        {
            var outward = Vector3.zero;
            if (route != null && route.TotalLength > 0f)
            {
                outward = route.GetNearestRoadPoint(restPosition, out _) - restPosition;
                outward.y = 0f;
            }

            return outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.forward;
        }

        private Vector3 PenBack(PathRoute route) => restPosition - PenOutward(route) * (pen != null ? ModelPenBack : PenHalfDepth);

        private Vector3 PenGate(PathRoute route) => restPosition + PenOutward(route) * (pen != null ? -ModelPenFront : PenHalfDepth);

        // The waiting mammoth grazes at the back of the pen and walks slowly to the gate as it
        // gets ready; ready, it stands at the gate until goblins come.
        private void UpdateInPen(PathRoute route)
        {
            var readiness = PenReadiness;
            var target = Vector3.Lerp(PenBack(route), PenGate(route), Mathf.SmoothStep(0f, 1f, readiness));
            beast.position = target;
            Face(PenOutward(route), 90f);
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
            if (!IsAlive || IsRoaring)
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

        // Its body comes down on the road, not on the crowd: the goblins under it are pushed out as it
        // lands, and the horde goes round it for a while; then they walk over it as it sinks (user,
        // 2026-11-11). It shoves, it does not hurt.
        private void KeepHordeOffBody()
        {
            if (visual == null || !visual.TryGetFootprint(out var a, out var b, out var radius, out var landing) ||
                visual.SinceImpact > MammothVisual.HoldSeconds)
            {
                ReleaseCorpse();
                return;
            }

            // The body is a solid obstacle for the horde while it is held off: the crowd presses
            // round it instead of walking through it, and does not stop to fight it.
            corpse ??= new Corpse();
            corpse.Set(a, b, radius - 0.25f);
            if (!corpseRegistered)
            {
                enemies.RegisterCombatTarget(corpse);
                corpseRegistered = true;
            }

            shoveTimer -= Time.deltaTime;
            if (shoveTimer > 0f)
            {
                return;
            }

            shoveTimer += ShoveStepSeconds;
            var circles = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / Mathf.Max(0.3f, radius * 0.9f))) + 1;
            // As it hits the ground it crushes the goblins under it, once.
            if (!crushed && visual.SinceImpact >= 0f)
            {
                crushed = true;
                var crush = definition.damage * tower.DamageMultiplier * CrushDamageFactor;
                for (var i = 0; i < circles; i++)
                {
                    var at = circles == 1 ? a : Vector3.Lerp(a, b, i / (circles - 1f));
                    tower.RecordDamage(enemies.DamageAndKnockbackInRadius(at, radius - 0.35f, crush, 0f, MaxStompedPerStep, out _));
                }
            }

            for (var i = 0; i < circles; i++)
            {
                var at = circles == 1 ? a : Vector3.Lerp(a, b, i / (circles - 1f));
                enemies.DamageAndKnockbackInRadius(at, radius, 0f, BodyKnockback * (landing ? 2f : 1f), 0, out _,
                    maxPushMass: PushLimit);
            }
        }

        private Corpse corpse;
        private bool corpseRegistered;

        // After the hold the goblins climb onto the body, drawn lifted over it, as it sinks.
        private void UpdateMound(float depth)
        {
            if (visual == null || visual.SinceImpact < MammothVisual.HoldSeconds ||
                !visual.TryGetFootprint(out var a, out var b, out var radius, out _))
            {
                HordeMounds.Clear(this);
                return;
            }

            var height = visual.BackHeight - depth;
            if (height <= 0.02f)
            {
                HordeMounds.Clear(this);
                return;
            }

            HordeMounds.Set(this, a, b, radius, height);
        }

        // Lying across the whole road, the body is shoved slowly on down the road while goblins press
        // against it from behind (until it has sunk halfway). With room to get round it, it stays put.
        private void PushCorpseDownRoad(float depth)
        {
            if (visual == null || !visual.FillsRoad || visual.SinceImpact < 0f || depth > visual.BackHeight * 0.5f)
            {
                return;
            }

            var route = enemies.Route;
            if (route == null || route.TotalLength <= 0f || !visual.TryGetFootprint(out var a, out var b, out var radius, out _))
            {
                return;
            }

            var here = enemies.EstimatePathDistance(fallStart);
            var down = route.Sample(Mathf.Min(route.TotalLength, here + 1f)) - route.Sample(here);
            down.y = 0f;
            if (down.sqrMagnitude < 1e-6f)
            {
                return;
            }

            down.Normalize();
            // The end of the body facing up the road, and just beyond it: are goblins pressing there?
            var middle = (a + b) * 0.5f;
            var reach = Mathf.Abs(Vector3.Dot(b - a, down)) * 0.5f + radius;
            var behind = middle - down * (reach + 0.3f);
            if (!enemies.TryGetHordeTargetSnapshot(behind, 0.8f, false, TowerTargetingMode.Closest, out _, out _, out _))
            {
                return;
            }

            fallStart += down * CorpsePushSpeed * Time.deltaTime;
        }

        private void ReleaseCorpse()
        {
            if (corpseRegistered)
            {
                enemies?.UnregisterCombatTarget(corpse);
                corpseRegistered = false;
            }
        }

        // The dead mammoth's body as an obstacle of the horde: a capsule on the ground that bodies
        // cannot enter. Its blocking room is next to nothing, so no goblin stops to attack it.
        private sealed class Corpse : ICombatTarget, IOrientedCombatTarget
        {
            private Vector3 middle;
            private Vector3 axis = Vector3.forward;
            private float halfLength;
            private float halfDepth;

            public void Set(Vector3 a, Vector3 b, float radius)
            {
                middle = (a + b) * 0.5f;
                var along = b - a;
                along.y = 0f;
                axis = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
                halfDepth = Mathf.Max(0.3f, radius);
                halfLength = along.magnitude * 0.5f + halfDepth;
            }

            public Vector3 Position => middle;
            public bool IsAlive => true;
            public CombatTargetKind TargetKind => CombatTargetKind.Barrier;
            public float CombatRadius => halfDepth;
            public float BlockCapacity => 0.001f;
            public float CurrentBlockedMass => 0f;
            public float CurrentHealth => 1e6f;
            public float MaximumHealth => 1e6f;
            public float Armor => 0f;
            public float PhysicalResistance => 0f;
            public float FireResistance => 0f;
            public float SlowResistance => 0f;
            public float ThornsDamage => 0f;
            public Vector3 CombatAxis => axis;
            public float CombatHalfLength => halfLength;
            public float CombatHalfDepth => halfDepth;
            public bool TryAddBlocker(EnemyActor enemy) => false;
            public void RemoveBlocker(EnemyActor enemy) { }
            public void TakeDamage(float damage, EnemyActor source) { }
            public void ApplyGpuCombatState(float authoritativeHealth, bool destroyed, EnemyDefinition sourceDefinition) { }
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

            healthBar.position = beast.position + new Vector3(0f, visual != null ? MammothVisual.Height + 0.5f : 2.9f, 0f);
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
            // Head, trunk and tusks hang from a neck pivot, so the scoop can swing them together.
            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            var neck = new Vector3(0f, 1.6f, 0.9f);
            head.localPosition = neck;
            AddPart(head, PrimitiveType.Sphere, new Vector3(0f, 1.6f, 1.3f) - neck, Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), material);
            AddPart(head, PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 1.85f) - neck, new Vector3(25f, 0f, 0f), new Vector3(0.28f, 0.55f, 0.28f), material);
            foreach (var side in new[] { -1f, 1f })
            {
                AddPart(head, PrimitiveType.Cylinder, new Vector3(0.32f * side, 1.15f, 1.85f) - neck, new Vector3(70f, 0f, 0f), new Vector3(0.12f, 0.45f, 0.12f), BootstrapMaterials.Get(PlaceholderPalette.Accent));
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
