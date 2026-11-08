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
        private Vector3 rockAimPoint;
        private GameObject rockAimMarker;
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

        // Press on a point, drag the way it should roll along the road, release to drop it.
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
                rockAimPoint = current.PointerWorld;
            }

            var drag = current.PointerWorld - rockAimPoint;
            drag.y = 0f;
            ShowRockAim(drag);
            if (current.FireReleased || !current.FireHeld)
            {
                aimingRock = false;
                HideRockAim();
                ThrowRock(rockAimPoint, drag);
                cooldown = CooldownSeconds;
            }
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

        private void ShowRockAim(Vector3 drag)
        {
            if (rockAimMarker == null)
            {
                rockAimMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rockAimMarker.name = "RockAim";
                RemovePrimitiveCollider(rockAimMarker);
                rockAimMarker.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Accent);
            }

            var length = Mathf.Max(0.6f, drag.magnitude);
            var forward = drag.sqrMagnitude > 0.01f ? drag.normalized : Vector3.forward;
            rockAimMarker.SetActive(true);
            rockAimMarker.transform.position = rockAimPoint + forward * length * 0.5f + Vector3.up * 0.1f;
            rockAimMarker.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            rockAimMarker.transform.localScale = new Vector3(0.25f, 0.05f, length);
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
