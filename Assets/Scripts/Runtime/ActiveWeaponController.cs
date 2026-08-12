using TowerDefense.Input;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class ActiveWeaponController : MonoBehaviour
    {
        private EnemyManager enemies;
        private PlayerInputRouter input;
        private TowerManager towers;
        private float cooldown;
        private bool canFire;
        private bool autoFireUnlocked;

        public float Damage { get; set; } = 9.2f;
        public float Radius { get; set; } = 2.5f;
        public float CooldownSeconds { get; set; } = 1.7f;
        public int MaxTargets { get; set; } = 8;
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
        public float CooldownRemaining => Mathf.Max(0f, cooldown);
        public float CooldownProgress => CooldownSeconds <= 0f ? 1f : 1f - Mathf.Clamp01(CooldownRemaining / CooldownSeconds);
        public bool IsReady => CanFire && CooldownRemaining <= 0f;

        public void Initialize(EnemyManager enemyManager, PlayerInputRouter router, TowerManager towerManager)
        {
            enemies = enemyManager;
            input = router;
            towers = towerManager;
        }

        public void ResetRunStats()
        {
            TotalDamageEvents = 0;
            TotalDamageDealt = 0f;
            AutoFireEnabled = false;
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

            var appliedDamage = enemies.DamageInRadius(input.Current.PointerWorld, Radius, Damage, MaxTargets, out var hitCount);
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

            var appliedDamage = enemies.DamageInRadius(aimPoint, Radius, Damage, MaxTargets, out var hitCount);
            TotalDamageEvents += hitCount;
            TotalDamageDealt += appliedDamage;
            cooldown = CooldownSeconds;
            SpawnImpactMarker(aimPoint);
        }

        private void SpawnImpactMarker(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "ActiveWeaponImpact";
            go.transform.position = position + Vector3.up * 0.2f;
            go.transform.localScale = new Vector3(Radius * 2f, 0.05f, Radius * 2f);
            go.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(1f, 0.35f, 0.12f, 0.45f));
            RemovePrimitiveCollider(go);
            Destroy(go, 0.18f);
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
