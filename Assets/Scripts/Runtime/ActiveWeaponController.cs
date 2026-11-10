using TowerDefense.Input;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class ActiveWeaponController : MonoBehaviour
    {
        private EnemyManager enemies;
        private PlayerInputRouter input;
        private TowerManager towers;
        private TopDownCameraController cameraController;
        private float cooldown;
        private bool canFire;
        private bool autoFireUnlocked;

        public float Damage { get; set; } = 9.2f;
        public float Radius { get; set; } = 2.5f;
        public float CooldownSeconds { get; set; } = 1.7f;
        public int MaxTargets { get; set; } = 8;
        public float KnockbackDistance { get; set; }
        public float KnockbackPropagationRadius { get; set; }
        public Vector3 ShockwaveArenaCenter { get; set; }
        public float ShockwaveArenaRadius { get; set; }
        public string DisplayName { get; set; } = "Volley of Arrows";
        // The prehistoric rock (RollingRock) instead of an instant blast: press on a point, drag
        // for the direction along the road, release to drop it.
        public bool Rock { get; set; }
        public float RockRollDistance { get; set; } = 8f;
        public bool RockFireTrail { get; set; }
        // Rock specials (user, 2026-11-08), set from the skill tree; 0 = not bought.
        public float RockFireDamageMultiplier { get; set; } = 1f;
        public float RockFireTicksPerSecond { get; set; } = 2f;
        public int RockShatterShards { get; set; }
        public float RockShatterDamageFraction { get; set; }
        public float RockQuakeRadius { get; set; }
        public float RockQuakeSeconds { get; set; }
        public bool IsAimingRock => aimingRock;
        private bool aimingRock;
        // After a click without a drag: the rock's point is set and the direction follows the
        // pointer until the next click.
        private bool pickingRockDirection;
        // A drag shorter than this on release is a click: the direction is picked next.
        private const float MinimumRockDrag = 0.6f;
        private Vector3 rockAimPoint;
        private GameObject rockAimMarker;
        private Transform rockAimShaft;
        private Transform rockAimHeadLeft;
        private Transform rockAimHeadRight;
        private Transform[] rockAimRingDashes;
        private const int RockAimRingDashCount = 20;
        // The aim is drawn above the crowd (user, 2026-11-09: in a big horde the old flat arrow on
        // the ground was hidden under the goblins).
        private const float RockAimHeight = 1.7f;
        // Kept quiet (user, 2026-11-09: the bright yellow arrow, disc and text were too much): a thin
        // see-through white line with a chevron tip, and a dashed outline where the rock falls.
        private static readonly Color RockAimColor = new(1f, 1f, 1f, 0.55f);
        private static readonly Color RockAimRingColor = new(1f, 1f, 1f, 0.4f);
        // The aiming hint shows until the player's first throw.
        public bool HasThrownRock { get; private set; }
        public int TotalDamageEvents { get; private set; }
        public float TotalDamageDealt { get; private set; }
        public bool CanFire
        {
            get => canFire;
            set
            {
                canFire = value;
                if (!canFire)
                {
                    AutoFireEnabled = false;
                }
            }
        }
        public bool AutoFireUnlocked
        {
            get => autoFireUnlocked;
            set
            {
                autoFireUnlocked = value;
                if (!autoFireUnlocked)
                {
                    AutoFireEnabled = false;
                }
            }
        }
        public bool AutoFireEnabled { get; private set; }
        public bool DevAutoActiveEnabled { get; set; }
        public float DevAutoEfficiency { get; set; } = 1f;
        public float CooldownRemaining => Mathf.Max(0f, cooldown);
        public float CooldownProgress => CooldownSeconds <= 0f ? 1f : 1f - Mathf.Clamp01(CooldownRemaining / CooldownSeconds);
        public bool IsReady => CanFire && CooldownRemaining <= 0f;

        public void Initialize(
            EnemyManager enemyManager,
            PlayerInputRouter router,
            TowerManager towerManager,
            TopDownCameraController topDownCamera = null)
        {
            enemies = enemyManager;
            input = router;
            towers = towerManager;
            cameraController = topDownCamera;
        }

        public void ResetRunStats()
        {
            TotalDamageEvents = 0;
            TotalDamageDealt = 0f;
            AutoFireEnabled = false;
            cooldown = 0f;
        }

        private void Update()
        {
            cooldown = Mathf.Max(0f, cooldown - Time.deltaTime);
            if (!CanFire || input == null || enemies == null)
            {
                return;
            }

            if (DevAutoActiveEnabled)
            {
                TryDevAutoFire();
                return;
            }

            if (Rock)
            {
                UpdateRockAim();
                return;
            }

            if (towers != null && towers.GetNearestTower(input.Current.PointerWorld) != null)
            {
                return;
            }

            if (AutoFireUnlocked && input.Current.FireActive)
            {
                AutoFireEnabled = !AutoFireEnabled;
            }

            var shouldFire = AutoFireUnlocked ? AutoFireEnabled : input.Current.FireActive;
            if (!shouldFire || cooldown > 0f)
            {
                return;
            }

            var appliedDamage = ApplyWeaponEffect(input.Current.PointerWorld, out var hitCount);
            TotalDamageEvents += hitCount;
            TotalDamageDealt += appliedDamage;
            cooldown = CooldownSeconds;
            SpawnImpactMarker(input.Current.PointerWorld);
        }

        private void TryDevAutoFire()
        {
            if (cooldown > 0f || enemies == null)
            {
                return;
            }

            if (!enemies.TryGetLeadEnemyAimPoint(Radius, out var aimPoint))
            {
                return;
            }

            if (Rock)
            {
                ThrowRock(aimPoint, Vector3.zero);
                cooldown = CooldownSeconds / Mathf.Clamp(DevAutoEfficiency, 0.1f, 1f);
                return;
            }

            var appliedDamage = ApplyWeaponEffect(aimPoint, out var hitCount);
            TotalDamageEvents += hitCount;
            TotalDamageDealt += appliedDamage;
            cooldown = CooldownSeconds / Mathf.Clamp(DevAutoEfficiency, 0.1f, 1f);
            SpawnImpactMarker(aimPoint);
        }

        // Aiming the rock (user, 2026-11-08): click where it should fall, then move the pointer the
        // way it should roll and click again. Pressing, dragging and releasing works too. A right
        // click or Escape puts the rock down without throwing.
        private void UpdateRockAim()
        {
            var current = input.Current;
            if (!aimingRock)
            {
                if (!current.FireActive || cooldown > 0f ||
                    (towers != null && towers.GetNearestTower(current.PointerWorld) != null))
                {
                    return;
                }

                aimingRock = true;
                pickingRockDirection = false;
                rockAimPoint = current.PointerWorld;
            }

            if (UnityEngine.Input.GetMouseButtonDown(1) || UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                aimingRock = false;
                pickingRockDirection = false;
                HideRockAim();
                return;
            }

            var drag = current.PointerWorld - rockAimPoint;
            drag.y = 0f;
            ShowRockAim(drag);
            if (pickingRockDirection)
            {
                if (current.FireActive)
                {
                    Release(drag);
                }

                return;
            }

            if (current.FireReleased || !current.FireHeld)
            {
                if (drag.magnitude < MinimumRockDrag)
                {
                    pickingRockDirection = true;
                    return;
                }

                Release(drag);
            }
        }

        private void Release(Vector3 drag)
        {
            aimingRock = false;
            pickingRockDirection = false;
            HideRockAim();
            // As the arrow showed: a drag this short means the default roll along the road.
            ThrowRock(rockAimPoint, drag.magnitude >= MinimumRockDrag ? drag : Vector3.zero);
            HasThrownRock = true;
            cooldown = CooldownSeconds;
        }

        private void ThrowRock(Vector3 point, Vector3 drag)
        {
            // TEMPORARY numbers until balancing: the roll hurts each goblin half the impact
            // damage, and the fire trail burns a tenth of it per tick.
            var settings = new RollingRock.Settings
            {
                ImpactRadius = Radius,
                ImpactDamage = Damage,
                ImpactKnockback = KnockbackDistance,
                ImpactLaunch = 0.9f,
                ImpactMaxTargets = MaxTargets,
                RollDistance = RockRollDistance,
                RollSpeed = 6f,
                RollRadius = 1.3f,
                RollDamage = Damage * 0.5f,
                RollKnockback = 1.2f,
                FireTrail = RockFireTrail,
                FireDamagePerTick = Damage * 0.1f * RockFireDamageMultiplier,
                FireTicksPerSecond = RockFireTicksPerSecond,
                ShatterShards = RockShatterShards,
                ShatterDamage = Damage * RockShatterDamageFraction,
                QuakeRadius = RockQuakeRadius,
                QuakeSeconds = RockQuakeSeconds
            };
            RollingRock.Throw(enemies, point, drag, settings, dealt =>
            {
                TotalDamageEvents++;
                TotalDamageDealt += dealt;
            });
        }

        // A ring where the rock will fall and an arrow as long as its roll, the way it will roll:
        // the drag's way, or with no drag yet, back along the road against the horde.
        private void ShowRockAim(Vector3 drag)
        {
            if (rockAimMarker == null)
            {
                rockAimMarker = new GameObject("RockAim");
                rockAimRingDashes = new Transform[RockAimRingDashCount];
                for (var i = 0; i < rockAimRingDashes.Length; i++)
                {
                    rockAimRingDashes[i] = CreateAimPart("RingDash", PrimitiveType.Cube, RockAimRingColor);
                }

                rockAimShaft = CreateAimPart("Shaft", PrimitiveType.Cube, RockAimColor);
                rockAimHeadLeft = CreateAimPart("HeadLeft", PrimitiveType.Cube, RockAimColor);
                rockAimHeadRight = CreateAimPart("HeadRight", PrimitiveType.Cube, RockAimColor);
            }

            drag.y = 0f;
            var forward = drag.magnitude >= MinimumRockDrag ? drag.normalized : DefaultRollDirection(rockAimPoint);
            var length = Mathf.Max(1.5f, RockRollDistance);
            var top = new Vector3(rockAimPoint.x, RockAimHeight, rockAimPoint.z);
            rockAimMarker.SetActive(true);

            // Dashed outline of where it falls: short segments around the circle with gaps.
            var circumference = 2f * Mathf.PI * Radius;
            var dashLength = circumference / RockAimRingDashCount * 0.55f;
            for (var i = 0; i < rockAimRingDashes.Length; i++)
            {
                var angle = i * 360f / RockAimRingDashCount;
                var radians = angle * Mathf.Deg2Rad;
                var dash = rockAimRingDashes[i];
                dash.position = top + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * Radius;
                dash.rotation = Quaternion.Euler(0f, -angle, 0f);
                dash.localScale = new Vector3(0.08f, 0.02f, dashLength);
            }

            // A thin line the length of the roll, ending in a chevron.
            const float lineWidth = 0.1f;
            const float headLength = 0.7f;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            var tip = top + forward * length;
            rockAimShaft.position = top + forward * (length * 0.5f);
            rockAimShaft.rotation = rotation;
            rockAimShaft.localScale = new Vector3(lineWidth, 0.02f, length);
            PlaceChevronStroke(rockAimHeadLeft, tip, rotation, 35f, headLength, lineWidth);
            PlaceChevronStroke(rockAimHeadRight, tip, rotation, -35f, headLength, lineWidth);
        }

        private static void PlaceChevronStroke(Transform stroke, Vector3 tip, Quaternion rotation, float angle, float length, float width)
        {
            var strokeRotation = rotation * Quaternion.Euler(0f, angle, 0f);
            stroke.rotation = strokeRotation;
            stroke.position = tip - strokeRotation * Vector3.forward * (length * 0.5f);
            stroke.localScale = new Vector3(width, 0.02f, length);
        }

        private Transform CreateAimPart(string partName, PrimitiveType type, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = partName;
            RemovePrimitiveCollider(part);
            part.transform.SetParent(rockAimMarker.transform, false);
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = BootstrapMaterials.GetUnlit(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part.transform;
        }

        // The way a rock thrown without a drag rolls: back along the road, against the horde.
        private Vector3 DefaultRollDirection(Vector3 point)
        {
            var route = enemies != null ? enemies.Route : null;
            if (route == null || !route.HasUsableRoute)
            {
                return Vector3.forward;
            }

            var roadPoint = route.GetNearestRoadPoint(point, out _);
            var distance = enemies.EstimatePathDistance(roadPoint);
            var back = route.Sample(Mathf.Max(0f, distance - 1f)) - route.Sample(Mathf.Min(route.TotalLength, distance + 1f));
            back.y = 0f;
            return back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.forward;
        }

        private void HideRockAim()
        {
            if (rockAimMarker != null)
            {
                rockAimMarker.SetActive(false);
            }
        }

        private float ApplyWeaponEffect(Vector3 position, out int hitCount)
        {
            var propagationRadius = ResolveKnockbackPropagationRadius(position);
            return KnockbackDistance > 0f
                ? enemies.DamageAndKnockbackInRadius(
                    position,
                    Radius,
                    Damage,
                    KnockbackDistance,
                    MaxTargets,
                    out hitCount,
                    knockbackPropagationRadius: propagationRadius)
                : enemies.DamageInRadius(position, Radius, Damage, MaxTargets, out hitCount);
        }

        private void SpawnImpactMarker(Vector3 position)
        {
            if (KnockbackDistance > 0f && KnockbackPropagationRadius > 0f)
            {
                var propagationRadius = ResolveKnockbackPropagationRadius(position);
                MeteorImpactVisual.Spawn(position, Radius);
                cameraController?.PlayMeteorImpact(position, Radius);
                ShockwaveRingVisual.Spawn(
                    position,
                    Radius,
                    Radius + propagationRadius,
                    GpuHordeSimulation.ShockwavePropagationSpeed,
                    ShockwaveArenaCenter,
                    ShockwaveArenaRadius);
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "ActiveWeaponImpact";
            go.transform.position = position + Vector3.up * 0.2f;
            go.transform.localScale = new Vector3(Radius * 2f, 0.05f, Radius * 2f);
            var color = KnockbackDistance > 0f
                ? PlaceholderPalette.Grey(.672f, 0.55f)
                : PlaceholderPalette.Grey(.518f, 0.45f);
            go.GetComponent<Renderer>().material = BootstrapMaterials.Get(color);
            RemovePrimitiveCollider(go);
            Destroy(go, 0.18f);

        }

        private float ResolveKnockbackPropagationRadius(Vector3 position)
        {
            if (ShockwaveArenaRadius <= 0f)
            {
                return KnockbackPropagationRadius;
            }

            var offset = new Vector2(
                position.x - ShockwaveArenaCenter.x,
                position.z - ShockwaveArenaCenter.z);
            var distanceFromCenter = Mathf.Min(ShockwaveArenaRadius, offset.magnitude);
            var farthestBoundaryDistance = ShockwaveArenaRadius + distanceFromCenter;
            return Mathf.Min(
                KnockbackPropagationRadius,
                Mathf.Max(0f, farthestBoundaryDistance - Radius));
        }

        private static void RemovePrimitiveCollider(GameObject target)
        {
            var components = target.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }
        }
    }
}
