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

            var appliedDamage = ApplyWeaponEffect(aimPoint, out var hitCount);
            TotalDamageEvents += hitCount;
            TotalDamageDealt += appliedDamage;
            cooldown = CooldownSeconds / Mathf.Clamp(DevAutoEfficiency, 0.1f, 1f);
            SpawnImpactMarker(aimPoint);
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
