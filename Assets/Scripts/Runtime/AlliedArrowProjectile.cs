using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class AlliedArrowProjectile : MonoBehaviour
    {
        private TowerActor owner;
        private EnemyActor target;
        private float damage;
        private float speed;

        public void Fire(TowerActor sourceOwner, EnemyActor targetEnemy, float projectileDamage, float projectileSpeed)
        {
            owner = sourceOwner;
            target = targetEnemy;
            damage = projectileDamage;
            speed = Mathf.Max(1f, projectileSpeed);
        }

        private void Update()
        {
            if (target == null || !target.IsAlive)
            {
                Destroy(gameObject);
                return;
            }

            var targetPosition = target.transform.position + Vector3.up * 0.35f;
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);
            if ((targetPosition - transform.position).sqrMagnitude > 0.06f)
            {
                return;
            }

            var appliedDamage = target.ApplyDamage(damage);
            owner?.RecordDamage(appliedDamage);
            Destroy(gameObject);
        }
    }
}
