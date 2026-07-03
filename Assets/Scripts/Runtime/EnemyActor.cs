using System;
using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class EnemyActor : MonoBehaviour
    {
        private EnemyDefinition definition;
        private PathRoute path;
        private EnemyManager owner;
        private float health;
        private float pathDistance;
        private bool active;
        private Transform healthFill;

        public EnemyDefinition Definition => definition;
        public float Health => health;
        public float PathDistance => pathDistance;
        public bool IsAlive => active && health > 0f;
        public event Action<EnemyActor> Died;

        public void Initialize(EnemyDefinition enemyDefinition, PathRoute route, EnemyManager enemyOwner, float initialOffset)
        {
            definition = enemyDefinition;
            path = route;
            owner = enemyOwner;
            health = enemyDefinition.maxHealth;
            pathDistance = initialOffset;
            active = true;
            transform.localScale = Vector3.one * enemyDefinition.visualScale;
            EnsureHealthBar();
            UpdateHealthBar();
            gameObject.SetActive(true);
            MoveToPathPosition();
        }

        private void Update()
        {
            if (!active || path == null)
            {
                return;
            }

            pathDistance += definition.speed * Time.deltaTime;
            UpdateHealthBar();
            if (pathDistance >= path.TotalLength)
            {
                active = false;
                owner.NotifyEnemyEscaped(this);
                gameObject.SetActive(false);
                return;
            }

            MoveToPathPosition();
        }

        public void ApplyDamage(float damage)
        {
            if (!IsAlive)
            {
                return;
            }

            health -= damage;
            UpdateHealthBar();
            if (health > 0f)
            {
                return;
            }

            active = false;
            Died?.Invoke(this);
            owner.NotifyEnemyKilled(this);
            gameObject.SetActive(false);
        }

        private void MoveToPathPosition()
        {
            transform.position = path.Sample(pathDistance);
        }

        private void EnsureHealthBar()
        {
            if (healthFill != null)
            {
                return;
            }

            var root = new GameObject("HealthBar");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            background.name = "HealthBarBackground";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = Vector3.zero;
            background.transform.localScale = new Vector3(1.15f, 0.08f, 0.12f);
            background.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.03f, 0.03f, 0.035f, 1f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "HealthBarFill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = new Vector3(-0.575f, 0.012f, 0f);
            fill.transform.localScale = new Vector3(1.15f, 0.09f, 0.14f);
            fill.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.22f, 1f, 0.25f, 1f));
            healthFill = fill.transform;
        }

        private void UpdateHealthBar()
        {
            if (healthFill == null || definition == null)
            {
                return;
            }

            var normalizedHealth = Mathf.Clamp01(health / definition.maxHealth);
            healthFill.localScale = new Vector3(1.15f * normalizedHealth, 0.09f, 0.14f);
            healthFill.localPosition = new Vector3(-0.575f + 0.575f * normalizedHealth, 0.012f, 0f);
        }
    }
}
