using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class ProjectileActor : MonoBehaviour
    {
        private TowerActor source;
        private TowerDefinition sourceTower;
        private EnemyActor target;
        private EnemyManager enemies;
        private float damage;
        private float speed;
        private Vector3 startPosition;
        private Vector3 impactPosition;
        private float flightTime;
        private float flightElapsed;
        private int remainingPierce;
        private bool active;

        public void Fire(TowerActor sourceTowerActor, TowerDefinition towerDefinition, EnemyActor targetEnemy, EnemyManager enemyManager, float projectileDamage)
        {
            source = sourceTowerActor;
            sourceTower = towerDefinition;
            target = targetEnemy;
            enemies = enemyManager;
            damage = projectileDamage;
            speed = towerDefinition.projectileSpeed;
            startPosition = transform.position;
            impactPosition = targetEnemy != null ? targetEnemy.transform.position : transform.position;
            flightElapsed = 0f;
            remainingPierce = Mathf.Max(0, towerDefinition.pierce);
            var flightMultiplier = towerDefinition.projectilePattern == ProjectilePattern.ArcSplash
                ? Mathf.Max(1f, towerDefinition.arcFlightTimeMultiplier)
                : 1f;
            flightTime = Mathf.Max(0.25f, Vector3.Distance(startPosition, impactPosition) / Mathf.Max(0.01f, speed) * flightMultiplier);
            active = true;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!active)
            {
                Deactivate();
                return;
            }

            if (sourceTower != null && sourceTower.projectilePattern == ProjectilePattern.ArcSplash)
            {
                UpdateArcSplash();
                return;
            }

            UpdateDirect();
        }

        private void UpdateDirect()
        {
            if (target == null || !target.IsAlive)
            {
                Deactivate();
                return;
            }

            transform.position = Vector3.MoveTowards(transform.position, target.transform.position, speed * Time.deltaTime);
            if ((target.transform.position - transform.position).sqrMagnitude > 0.08f)
            {
                return;
            }

            var appliedDamage = target.ApplyDamage(damage);
            source?.RecordDamage(appliedDamage);
            if (remainingPierce > 0)
            {
                remainingPierce--;
                var nextTarget = enemies.GetNearestEnemyExcept(transform.position, 2.6f, sourceTower.canHitFlying, target);
                if (nextTarget != null)
                {
                    target = nextTarget;
                    return;
                }
            }
            Deactivate();
        }

        private void UpdateArcSplash()
        {
            flightElapsed += Time.deltaTime;
            var t = Mathf.Clamp01(flightElapsed / flightTime);
            var position = Vector3.Lerp(startPosition, impactPosition, t);
            position.y += Mathf.Sin(t * Mathf.PI) * 3.4f;
            transform.position = position;

            if (t < 1f)
            {
                return;
            }

            var radius = sourceTower != null ? sourceTower.splashRadius : 0f;
            var knockback = sourceTower != null ? sourceTower.knockbackDistance : 0f;
            var burnDamage = sourceTower != null && sourceTower.appliesFire ? sourceTower.fireDamagePerTick : 0f;
            var burnRate = sourceTower != null && sourceTower.appliesFire ? sourceTower.fireTicksPerSecond : 0f;
            var burnDuration = sourceTower != null && sourceTower.appliesFire ? sourceTower.fireDuration : 0f;
            var burnStacks = sourceTower != null && sourceTower.appliesFire ? sourceTower.fireMaxStacks : 0;
            var appliedDamage = enemies != null
                ? enemies.DamageAndKnockbackInRadius(impactPosition, radius, damage, knockback, out _, source, burnDamage, burnRate, burnDuration, burnStacks)
                : 0f;
            source?.RecordDamage(appliedDamage);
            SpawnImpactMarker(radius);
            Deactivate();
        }

        private void SpawnImpactMarker(float radius)
        {
            if (radius <= 0f)
            {
                return;
            }

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "CatapultImpact";
            marker.transform.position = impactPosition + Vector3.up * 0.03f;
            marker.transform.localScale = new Vector3(radius * 2f, 0.04f, radius * 2f);
            marker.GetComponent<Renderer>().material = BootstrapMaterials.Get(sourceTower != null && sourceTower.appliesFire
                ? new Color(1f, 0.32f, 0.05f, 0.42f)
                : new Color(0.58f, 0.44f, 0.27f, 0.35f));
            Destroy(marker, 0.25f);
        }

        private void Deactivate()
        {
            active = false;
            Destroy(gameObject);
        }
    }
}
