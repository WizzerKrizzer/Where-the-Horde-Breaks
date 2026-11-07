using TowerDefense.Data;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The beast of a charge tower (the Tamed Mammoth). It rests in its pen until enemies are on the
    /// stretch of road it charges along, then walks to the road and charges along it against the
    /// enemies' flow, trampling and knocking aside everything across the road. It slows down at the
    /// end of the charge, walks back along the road and returns to the pen. The next charge starts
    /// once it is back and the tower's fire interval has passed.
    ///
    /// The beast is solid whenever it is out of the pen: it keeps shoving away every enemy that
    /// touches its body, so the horde parts around it instead of walking through it.
    /// </summary>
    public sealed class BeastCharge : MonoBehaviour
    {
        private enum State
        {
            Resting,
            ToRoad,
            Charging,
            BackAlongRoad,
            ToPen
        }

        // Trample damage is applied in steps; each enemy under the beast is hit a few times per pass.
        private const float HitStepSeconds = 0.1f;
        private const float ShoveStepSeconds = 0.05f;
        // Walking to and from the road is slower than the charge.
        private const float WalkSpeedFraction = 0.55f;
        private const float TurnDegreesPerSecond = 360f;
        private const int MaxTrampledPerStep = 40;
        // The body: enemies closer than this to the beast's centre are shoved out hard.
        private const float BodyRadius = 1.6f;
        private const float BodyKnockback = 2.4f;
        // The charge slows down over its last metres instead of stopping dead.
        private const float SlowDownDistance = 4f;
        private const float SlowestChargeFraction = 0.3f;
        // Enemies count as on the stretch when one is this close to a point along it.
        private const float StretchProbeRadius = 3f;
        private static readonly float[] StretchProbeDistances = { 1.5f, 5f, 8.5f };

        private TowerActor tower;
        private TowerDefinition definition;
        private EnemyManager enemies;
        private Transform beast;
        private State state;
        private float cooldown;
        private float hitTimer;
        private float shoveTimer;
        private float roadDistance;
        private float chargeDistance;
        private float chargeEnd;
        private Vector3 roadStart;
        private Vector3 restPosition;
        private PathRoute stretchRoute;
        private Vector3[] stretchProbes;

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

            var route = enemies.Route;
            var walkSpeed = definition.chargeSpeed * WalkSpeedFraction;
            switch (state)
            {
                case State.Resting:
                    cooldown -= Time.deltaTime;
                    if (cooldown <= 0f && route != null && route.TotalLength > 0f && EnemiesOnStretch(route))
                    {
                        BeginCharge(route);
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
                    UpdateCharge(route);
                    break;
                case State.BackAlongRoad:
                    // Back with the enemies' flow to where it came onto the road.
                    if (route == null || FollowRoad(route, roadDistance, walkSpeed))
                    {
                        state = State.ToPen;
                    }

                    break;
                case State.ToPen:
                    if (MoveTowards(restPosition, walkSpeed))
                    {
                        state = State.Resting;
                        cooldown = definition.fireInterval / Mathf.Max(0.05f, tower.FireRateMultiplier);
                    }

                    break;
            }

            if (state != State.Resting)
            {
                ShoveAwayFromBody();
            }
        }

        // True when enemies are on the first part of the road the beast would charge along, so the
        // charge meets them instead of running along an empty road.
        private bool EnemiesOnStretch(PathRoute route)
        {
            if (stretchRoute != route || stretchProbes == null)
            {
                // The probes stay at fixed points so the horde's cached target queries can answer.
                stretchRoute = route;
                var start = enemies.EstimatePathDistance(transform.position);
                stretchProbes = new Vector3[StretchProbeDistances.Length];
                for (var i = 0; i < stretchProbes.Length; i++)
                {
                    stretchProbes[i] = route.Sample(Mathf.Max(0f, start - StretchProbeDistances[i]));
                }
            }

            foreach (var probe in stretchProbes)
            {
                if (enemies.TryGetHordeTargetSnapshot(probe, StretchProbeRadius, false, TowerTargetingMode.Closest, out _, out _, out _) ||
                    enemies.GetEnemyByTargetingMode(probe, StretchProbeRadius, false, TowerTargetingMode.Closest) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void BeginCharge(PathRoute route)
        {
            // Enemies walk from the start of the road (distance 0) towards its end, so the beast
            // runs towards the start, head-on into them.
            roadDistance = enemies.EstimatePathDistance(transform.position);
            chargeDistance = roadDistance;
            chargeEnd = Mathf.Max(0f, roadDistance - definition.chargeDistance);
            roadStart = route.Sample(roadDistance);
            state = State.ToRoad;
            ChargeCount++;
        }

        private void UpdateCharge(PathRoute route)
        {
            if (route == null)
            {
                state = State.ToPen;
                return;
            }

            var remaining = chargeDistance - chargeEnd;
            var speed = definition.chargeSpeed * Mathf.Lerp(SlowestChargeFraction, 1f, Mathf.Clamp01(remaining / SlowDownDistance));
            chargeDistance = Mathf.Max(chargeEnd, chargeDistance - speed * Time.deltaTime);
            var position = route.Sample(chargeDistance);
            Face(position - beast.position, 1080f);
            beast.position = position;

            hitTimer -= Time.deltaTime;
            if (hitTimer <= 0f)
            {
                hitTimer += HitStepSeconds;
                Trample(position);
            }

            if (chargeDistance <= chargeEnd + 0.001f)
            {
                state = State.BackAlongRoad;
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
            var dealt = enemies.DamageAndKnockbackInRadius(position, definition.chargeRadius, damage, 0f, MaxTrampledPerStep, out _);
            tower.RecordDamage(dealt);
            // Across the whole road: the horde hugs the inside of bends, and the beast runs down
            // the middle.
            enemies.DamageAndKnockbackInRadius(position, definition.chargeRadius, 0f, definition.knockbackDistance, 0, out _);
        }

        private void ShoveAwayFromBody()
        {
            shoveTimer -= Time.deltaTime;
            if (shoveTimer > 0f)
            {
                return;
            }

            shoveTimer += ShoveStepSeconds;
            enemies.DamageAndKnockbackInRadius(beast.position, BodyRadius, 0f, BodyKnockback, 0, out _);
        }

        // Walks along the road towards a distance along it; true once there.
        private bool FollowRoad(PathRoute route, float targetDistance, float speed)
        {
            var step = speed * Time.deltaTime;
            var arrived = Mathf.Abs(targetDistance - chargeDistance) <= step;
            chargeDistance = arrived ? targetDistance : chargeDistance + Mathf.Sign(targetDistance - chargeDistance) * step;
            var position = route.Sample(chargeDistance);
            Face(position - beast.position, TurnDegreesPerSecond);
            beast.position = position;
            return arrived;
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
            Face(direction, TurnDegreesPerSecond);
            return false;
        }

        private void Face(Vector3 direction, float degreesPerSecond)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return;
            }

            beast.rotation = Quaternion.RotateTowards(beast.rotation, Quaternion.LookRotation(direction.normalized, Vector3.up), degreesPerSecond * Time.deltaTime);
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
