using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The beast of a charge tower (the Tamed Mammoth). It rests in its pen until enemies come within
    /// range, then walks to the road and charges along it against the enemies' flow, damaging and
    /// knocking aside everything it runs over, and walks back to the pen. The next charge starts
    /// once it is back and the tower's fire interval has passed.
    /// </summary>
    public sealed class BeastCharge : MonoBehaviour
    {
        private enum State
        {
            Resting,
            ToRoad,
            Charging,
            Returning
        }

        // Hits are applied in steps; each enemy under the beast is hit a few times per pass.
        private const float HitStepSeconds = 0.1f;
        // Walking to and from the road is slower than the charge.
        private const float WalkSpeedFraction = 0.55f;
        private const float TurnDegreesPerSecond = 540f;
        private const int MaxTrampledPerStep = 40;

        private TowerActor tower;
        private TowerDefinition definition;
        private EnemyManager enemies;
        private Transform beast;
        private State state;
        private float cooldown;
        private float hitTimer;
        private float chargeDistance;
        private float chargeEnd;
        private Vector3 roadStart;
        private Vector3 restPosition;

        public bool IsCharging => state == State.Charging;
        // Out of the pen: walking to the road, charging or walking back.
        public bool IsOut => state != State.Resting;
        public int ChargeCount { get; private set; }
        public Vector3 BeastPosition => beast != null ? beast.position : transform.position;

        public void Initialize(TowerActor owner, TowerDefinition towerDefinition, EnemyManager enemyManager)
        {
            tower = owner;
            definition = towerDefinition;
            enemies = enemyManager;
            restPosition = transform.position;
            if (beast == null)
            {
                beast = CreateBeast(transform.parent).transform;
            }

            beast.position = restPosition;
            state = State.Resting;
            cooldown = 0.5f;
        }

        private void OnDestroy()
        {
            if (beast != null)
            {
                Destroy(beast.gameObject);
            }
        }

        private void Update()
        {
            if (definition == null || enemies == null || beast == null)
            {
                return;
            }

            var walkSpeed = definition.chargeSpeed * WalkSpeedFraction;
            switch (state)
            {
                case State.Resting:
                    cooldown -= Time.deltaTime;
                    if (cooldown <= 0f && HasEnemyInRange())
                    {
                        BeginCharge();
                    }

                    break;
                case State.ToRoad:
                    if (MoveTowards(roadStart, walkSpeed))
                    {
                        state = State.Charging;
                        hitTimer = 0f;
                    }

                    break;
                case State.Charging:
                    UpdateCharge();
                    break;
                case State.Returning:
                    if (MoveTowards(restPosition, walkSpeed))
                    {
                        state = State.Resting;
                        cooldown = definition.fireInterval / Mathf.Max(0.05f, tower.FireRateMultiplier);
                    }

                    break;
            }
        }

        private bool HasEnemyInRange()
        {
            if (enemies.TryGetHordeTargetSnapshot(transform.position, definition.range, false, TowerTargetingMode.First, out _, out _, out _))
            {
                return true;
            }

            return enemies.GetEnemyByTargetingMode(transform.position, definition.range, false, TowerTargetingMode.First) != null;
        }

        private void BeginCharge()
        {
            var route = enemies.Route;
            if (route == null || route.TotalLength <= 0f)
            {
                cooldown = 1f;
                return;
            }

            // Enemies walk from the start of the road (distance 0) towards its end, so the beast
            // runs towards the start, head-on into them.
            chargeDistance = enemies.EstimatePathDistance(transform.position);
            chargeEnd = Mathf.Max(0f, chargeDistance - definition.chargeDistance);
            roadStart = route.Sample(chargeDistance);
            state = State.ToRoad;
            ChargeCount++;
        }

        private void UpdateCharge()
        {
            var route = enemies.Route;
            if (route == null)
            {
                state = State.Returning;
                return;
            }

            chargeDistance = Mathf.Max(chargeEnd, chargeDistance - definition.chargeSpeed * Time.deltaTime);
            var position = route.Sample(chargeDistance);
            var heading = position - beast.position;
            heading.y = 0f;
            beast.position = position;
            if (heading.sqrMagnitude > 0.000001f)
            {
                beast.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
            }

            hitTimer -= Time.deltaTime;
            if (hitTimer <= 0f)
            {
                hitTimer += HitStepSeconds;
                Trample(position);
            }

            if (chargeDistance <= chargeEnd + 0.001f)
            {
                state = State.Returning;
            }
        }

        // Each enemy is under the beast for about 2 * radius / speed seconds, so the damage per step
        // is spread to add up to the tower's damage per enemy over one pass.
        private void Trample(Vector3 position)
        {
            var stepsUnderBeast = Mathf.Max(1f, 2f * definition.chargeRadius / Mathf.Max(0.1f, definition.chargeSpeed) / HitStepSeconds);
            var damage = definition.damage * tower.DamageMultiplier / stepsUnderBeast;
            // At most about as many goblins as fit under the beast; the GPU horde reports damage as
            // damage times this cap, so it also keeps the tower's damage figure from running away.
            var dealt = enemies.DamageAndKnockbackInRadius(position, definition.chargeRadius, damage, definition.knockbackDistance, MaxTrampledPerStep, out _);
            tower.RecordDamage(dealt);
        }

        private bool MoveTowards(Vector3 destination, float speed)
        {
            var offset = destination - beast.position;
            offset.y = 0f;
            var step = speed * Time.deltaTime;
            if (offset.sqrMagnitude <= step * step)
            {
                beast.position = destination;
                return true;
            }

            var direction = offset.normalized;
            beast.position += direction * step;
            beast.rotation = Quaternion.RotateTowards(beast.rotation, Quaternion.LookRotation(direction, Vector3.up), TurnDegreesPerSecond * Time.deltaTime);
            return false;
        }

        // Greybox mammoth, about 2.6 m long and 2.2 m tall, facing +Z.
        private static GameObject CreateBeast(Transform parent)
        {
            var root = new GameObject("Beast");
            root.transform.SetParent(parent, false);
            var material = BootstrapMaterials.Get(PlaceholderPalette.Grey(0.62f));
            AddPart(root.transform, PrimitiveType.Capsule, new Vector3(0f, 1.15f, 0f), new Vector3(90f, 0f, 0f), new Vector3(1.4f, 1.3f, 1.3f), material);
            AddPart(root.transform, PrimitiveType.Sphere, new Vector3(0f, 1.6f, 1.3f), Vector3.zero, new Vector3(1.0f, 1.0f, 1.0f), material);
            AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 1.85f), new Vector3(25f, 0f, 0f), new Vector3(0.28f, 0.55f, 0.28f), material);
            foreach (var side in new[] { -1f, 1f })
            {
                AddPart(root.transform, PrimitiveType.Cylinder, new Vector3(0.32f * side, 1.15f, 1.85f), new Vector3(70f, 0f, 0f), new Vector3(0.12f, 0.45f, 0.12f), BootstrapMaterials.Get(PlaceholderPalette.Accent));
            }

            return root;
        }

        private static void AddPart(Transform parent, PrimitiveType type, Vector3 position, Vector3 euler, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
