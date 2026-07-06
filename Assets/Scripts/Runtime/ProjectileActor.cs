using TowerDefense.Data;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class ProjectileActor : MonoBehaviour
    {
        private TowerActor source;
        private TowerDefinition sourceTower;
        private EnemyManager enemies;
        private float damage;
        private float speed;
        private Vector3 startPosition;
        private Vector3 impactPosition;
        private Vector3 directDirection;
        private float flightTime;
        private float flightElapsed;
        private float directTravelDistance;
        private float directMaxDistance;
        private int remainingPierce;
        private bool active;
        private readonly List<EnemyActor> hitEnemies = new();

        public void Fire(TowerActor sourceTowerActor, TowerDefinition towerDefinition, EnemyActor targetEnemy, EnemyManager enemyManager, float projectileDamage)
        {
            source = sourceTowerActor;
            sourceTower = towerDefinition;
            enemies = enemyManager;
            damage = projectileDamage;
            speed = towerDefinition.projectileSpeed;
            startPosition = transform.position;
            impactPosition = targetEnemy != null ? targetEnemy.transform.position : transform.position;
            directDirection = impactPosition - startPosition;
            directDirection.y = 0f;
            directDirection = directDirection.sqrMagnitude <= 0.001f ? transform.forward : directDirection.normalized;
            flightElapsed = 0f;
            directTravelDistance = 0f;
            directMaxDistance = towerDefinition.projectilePattern == ProjectilePattern.ArcSplash
                ? Vector3.Distance(startPosition, impactPosition)
                : Mathf.Max(Vector3.Distance(startPosition, impactPosition) + 1f, towerDefinition.range + 1f);
            remainingPierce = Mathf.Max(0, towerDefinition.pierce);
            hitEnemies.Clear();
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
            if (enemies == null || sourceTower == null)
            {
                Deactivate();
                return;
            }

            var previousPosition = transform.position;
            var step = speed * Time.deltaTime;
            transform.position += directDirection * step;
            directTravelDistance += step;

            var hit = FindDirectHit(previousPosition, transform.position);
            if (hit != null)
            {
                ApplyDirectHit(hit);
                if (!active)
                {
                    return;
                }
            }

            if (directTravelDistance < directMaxDistance)
            {
                return;
            }

            Deactivate();
        }

        private EnemyActor FindDirectHit(Vector3 from, Vector3 to)
        {
            foreach (var enemy in enemies.ActiveEnemies)
            {
                if (enemy == null || !enemy.IsAlive || hitEnemies.Contains(enemy) || (enemy.Definition.isFlying && !sourceTower.canHitFlying))
                {
                    continue;
                }

                var hitRadius = Mathf.Max(0.26f, enemy.Definition.visualScale * 0.55f);
                if (DistancePointToSegmentXz(enemy.transform.position, from, to) <= hitRadius)
                {
                    return enemy;
                }
            }

            return null;
        }

        private void ApplyDirectHit(EnemyActor hit)
        {
            hitEnemies.Add(hit);
            var appliedDamage = hit.ApplyDamage(damage);
            source?.RecordDamage(appliedDamage);
            DamagePopup.Show(hit.transform.position, appliedDamage, new Color(1f, 0.92f, 0.22f, 1f));
            if (remainingPierce > 0)
            {
                remainingPierce--;
                return;
            }

            Deactivate();
        }

        private static float DistancePointToSegmentXz(Vector3 point, Vector3 a, Vector3 b)
        {
            var point2 = new Vector2(point.x, point.z);
            var a2 = new Vector2(a.x, a.z);
            var b2 = new Vector2(b.x, b.z);
            var segment = b2 - a2;
            var lengthSq = segment.sqrMagnitude;
            if (lengthSq <= 0.0001f)
            {
                return Vector2.Distance(point2, a2);
            }

            var t = Mathf.Clamp01(Vector2.Dot(point2 - a2, segment) / lengthSq);
            return Vector2.Distance(point2, a2 + segment * t);
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
