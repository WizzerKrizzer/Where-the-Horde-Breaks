using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class TowerActor : MonoBehaviour
    {
        private EnemyManager enemies;
        private TowerDefinition definition;
        private float damageMultiplier = 1f;
        private float cooldown;

        public TowerDefinition Definition => definition;
        public float DamageDealt { get; private set; }

        public void SetDamageMultiplier(float multiplier)
        {
            damageMultiplier = Mathf.Max(0.05f, multiplier);
        }

        public void Initialize(TowerDefinition towerDefinition, EnemyManager enemyManager, float towerDamageMultiplier = 1f)
        {
            definition = towerDefinition;
            enemies = enemyManager;
            SetDamageMultiplier(towerDamageMultiplier);
            cooldown = Random.Range(0f, towerDefinition.fireInterval);
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = BootstrapMaterials.Get(towerDefinition.color);
            }
        }

        private void Update()
        {
            if (definition == null || enemies == null)
            {
                return;
            }

            cooldown -= Time.deltaTime;
            if (cooldown > 0f)
            {
                return;
            }

            var target = enemies.GetNearestEnemy(transform.position, definition.range);
            if (target == null)
            {
                return;
            }

            Fire(target);
            cooldown = definition.fireInterval;
        }

        private void Fire(EnemyActor target)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Projectile_{definition.id}";
            go.transform.position = transform.position + Vector3.up * 0.45f;
            go.transform.localScale = Vector3.one * 0.16f;
            go.GetComponent<Renderer>().material = BootstrapMaterials.Get(Color.yellow);
            var projectile = go.AddComponent<ProjectileActor>();
            projectile.Fire(this, definition, target, definition.damage * damageMultiplier);
        }

        public void RecordDamage(float damage)
        {
            DamageDealt += damage;
        }
    }
}
