using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class ProjectileActor : MonoBehaviour
    {
        private TowerActor source;
        private EnemyActor target;
        private float damage;
        private float speed;
        private bool active;

        public void Fire(TowerActor sourceTowerActor, TowerDefinition sourceTower, EnemyActor targetEnemy, float projectileDamage)
        {
            source = sourceTowerActor;
            target = targetEnemy;
            damage = projectileDamage;
            speed = sourceTower.projectileSpeed;
            active = true;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!active || target == null || !target.IsAlive)
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
            Deactivate();
        }

        private void Deactivate()
        {
            active = false;
            Destroy(gameObject);
        }
    }
}
