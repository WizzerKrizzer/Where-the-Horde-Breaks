using TowerDefense.Input;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class ActiveWeaponController : MonoBehaviour
    {
        private EnemyManager enemies;
        private PlayerInputRouter input;
        private float cooldown;

        public float Damage { get; set; } = 9.166667f;
        public float Radius { get; set; } = 2.8f;
        public float CooldownSeconds { get; set; } = 1.6f;
        public int MaxTargets { get; set; } = 10;
        public int TotalDamageEvents { get; private set; }
        public float TotalDamageDealt { get; private set; }
        public bool CanFire { get; set; }
        public float CooldownRemaining => Mathf.Max(0f, cooldown);
        public float CooldownProgress => CooldownSeconds <= 0f ? 1f : 1f - Mathf.Clamp01(CooldownRemaining / CooldownSeconds);
        public bool IsReady => CanFire && CooldownRemaining <= 0f;

        public void Initialize(EnemyManager enemyManager, PlayerInputRouter router)
        {
            enemies = enemyManager;
            input = router;
        }

        public void ResetRunStats()
        {
            TotalDamageEvents = 0;
            TotalDamageDealt = 0f;
        }

        private void Update()
        {
            cooldown = Mathf.Max(0f, cooldown - Time.deltaTime);
            if (!CanFire || input == null || enemies == null || !input.Current.FireActive || cooldown > 0f)
            {
                return;
            }

            var appliedDamage = enemies.DamageInRadius(input.Current.PointerWorld, Radius, Damage, MaxTargets, out var hitCount);
            TotalDamageEvents += hitCount;
            TotalDamageDealt += appliedDamage;
            cooldown = CooldownSeconds;
            SpawnImpactMarker(input.Current.PointerWorld);
        }

        private void SpawnImpactMarker(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "ActiveWeaponImpact";
            go.transform.position = position + Vector3.up * 0.03f;
            go.transform.localScale = new Vector3(Radius * 2f, 0.05f, Radius * 2f);
            go.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(1f, 0.35f, 0.12f, 0.45f));
            Destroy(go, 0.18f);
        }
    }
}
