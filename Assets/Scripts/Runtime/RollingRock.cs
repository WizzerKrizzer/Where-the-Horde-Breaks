using System;
using System.Collections.Generic;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The prehistoric active weapon (docs/Handoff.md): a rock falls on a point of the road with a
    /// heavy hit and a strong knockback, then rolls along the road in the chosen direction, hurting
    /// and pushing aside the goblins in its way. A fire rock leaves a burning trail behind it.
    /// Specials (user, 2026-11-08): a quake stuns the goblins around where it lands, and a
    /// shattering rock bursts into shards at the end of its roll.
    /// The rock is the Meshy boulder, and the fire rock the burning one with its flames (RockFireVisual),
    /// when installed (Tools/Animation/install_rocks.sh into the git-ignored Resources/Meshy); otherwise
    /// a grey ball. The trail, quake and shards are drawn by SpecialEffectVisuals.
    /// </summary>
    public sealed class RollingRock : MonoBehaviour
    {
        [Serializable]
        public struct Settings
        {
            public float ImpactRadius;
            public float ImpactDamage;
            public float ImpactKnockback;
            public float ImpactLaunch;
            public int ImpactMaxTargets;
            public float RollDistance;
            public float RollSpeed;
            public float RollRadius;
            // Damage to each goblin the rock rolls over once.
            public float RollDamage;
            public float RollKnockback;
            public bool FireTrail;
            public float FireDamagePerTick;
            public float FireTicksPerSecond;
            // Shatter: shards that fly out at the end of the roll, each hitting where it lands.
            public int ShatterShards;
            public float ShatterDamage;
            // Quake: goblins within this radius of the landing point are stunned this long.
            public float QuakeRadius;
            public float QuakeSeconds;
        }

        private const float FallSeconds = 0.35f;
        private const float FallHeight = 7f;
        private const float BallRadius = 0.65f;
        private const float HitStepSeconds = 0.1f;
        private const int MaxRolledPerStep = 30;
        // The rock starts where it fell and joins the middle of the road over this distance.
        private const float JoinRoadDistance = 1.5f;
        private const float FadeSeconds = 0.4f;
        // Fire trail: a patch every TrailSpacing metres, burning for TrailSeconds.
        private const float TrailSpacing = 1f;
        private const float TrailRadius = 1.1f;
        private const float TrailSeconds = 4f;
        private const float TrailBurnStep = 0.5f;
        private const float DefaultBurnTicksPerSecond = 2f;
        private const float BurnSeconds = 3f;
        private const int BurnStacks = 3;
        // Shatter and quake. TEMPORARY sizes until balancing.
        private const float ShardRadius = 0.25f;
        private const float ShardHitRadius = 1f;
        private const float ShardMinDistance = 1.5f;
        private const float ShardMaxDistance = 3.5f;
        private const float ShardFlightSeconds = 0.3f;
        private const float ShardKnockback = 0.6f;
        private const int ShardMaxTargets = 8;
        // Pushing a packed crowd (user, 2026-11-09): the damage stays capped, but the push reaches
        // every goblin under the rock and throws them into the air, where they fly over the others,
        // so a jam has somewhere to go. The GPU's push on every body is gentler per unit than the
        // capped one, hence the scale. TEMPORARY until balancing.
        private const float CrowdPushScale = 7f;
        private const float RollLaunch = 0.6f;

        private enum State
        {
            Falling,
            Rolling,
            Fading
        }

        private struct TrailPatch
        {
            public Vector3 Position;
            public float Age;
            public GameObject Visual;
        }

        private struct Shard
        {
            public Vector3 From;
            public Vector3 To;
            public float Age;
            public Transform Visual;
            public Vector3 Spin;
        }

        private EnemyManager enemies;
        private PathRoute route;
        private Settings settings;
        private Action<float> reportDamage;
        private Transform ball;
        private State state;
        private float timer;
        private Vector3 dropPoint;
        private Vector3 lateralOffset;
        private float distance;
        private float direction;
        // Thrown with a drag (user, 2026-11-08): it rolls straight the way the drag points, on any
        // map; without one (the dev auto-throw) it follows the road back against the horde.
        private bool rollsStraight;
        private Vector3 straightDirection;
        private Vector3 straightStart;
        private float rolled;
        private float hitTimer;
        private float nextTrailAt;
        private float trailTimer;
        private readonly List<TrailPatch> trail = new();
        private readonly List<Shard> shards = new();
        private bool shattered;
        private bool burst;
        private RockFireVisual fire;

        public bool IsRolling => state == State.Rolling;
        public float Rolled => rolled;
        public Vector3 BallPosition => ball != null ? ball.position : transform.position;

        /// <summary>
        /// Drops a rock on point and rolls it straight the way drag points (a direction on the
        /// ground); a short or no drag rolls it along the road against the horde, towards its start.
        /// </summary>
        public static RollingRock Throw(EnemyManager enemies, Vector3 point, Vector3 drag, Settings settings, Action<float> reportDamage = null)
        {
            var go = new GameObject("RollingRock");
            var rock = go.AddComponent<RollingRock>();
            rock.Begin(enemies, point, drag, settings, reportDamage);
            return rock;
        }

        private void Begin(EnemyManager enemyManager, Vector3 point, Vector3 drag, Settings rockSettings, Action<float> damageReport)
        {
            enemies = enemyManager;
            route = enemyManager != null ? enemyManager.Route : null;
            settings = rockSettings;
            reportDamage = damageReport;
            point.y = 0f;
            dropPoint = point;
            drag.y = 0f;
            rollsStraight = drag.sqrMagnitude > 0.25f;
            straightDirection = rollsStraight ? drag.normalized : Vector3.zero;
            if (route != null && route.HasUsableRoute)
            {
                var roadPoint = route.GetNearestRoadPoint(point, out _);
                distance = enemies.EstimatePathDistance(roadPoint);
                var along = route.Sample(Mathf.Min(route.TotalLength, distance + 0.5f)) - route.Sample(Mathf.Max(0f, distance - 0.5f));
                along.y = 0f;
                drag.y = 0f;
                direction = drag.sqrMagnitude > 0.25f && Vector3.Dot(drag, along) > 0f ? 1f : -1f;
                // Inside the road it falls where aimed; outside, on the nearest edge of the road.
                if (!route.IsInsideCorridor(point))
                {
                    dropPoint = roadPoint;
                }

                lateralOffset = dropPoint - route.Sample(distance);
                lateralOffset.y = 0f;
            }

            ball = CreateBall(transform, settings.FireTrail);
            ball.localScale = Vector3.one * BallRadius * 2f;
            ball.position = dropPoint + Vector3.up * FallHeight;
            if (settings.FireTrail)
            {
                fire = RockFireVisual.Attach(transform, ball, BallRadius);
            }

            state = State.Falling;
        }

        private static readonly Dictionary<string, Material> ModelMaterials = new();

        /// <summary>True when the Meshy rocks are installed.</summary>
        public static bool ModelsInstalled => Resources.Load<GameObject>("Meshy/rock") != null &&
                                              Resources.Load<GameObject>("Meshy/fire-rock") != null;

        // The rolling ball, one metre across (scaled by the caller): the model centred on it, or a sphere.
        private static Transform CreateBall(Transform parent, bool fire)
        {
            var name = fire ? "fire-rock" : "rock";
            var prefab = Resources.Load<GameObject>("Meshy/" + name);
            var shader = Shader.Find("TowerDefense/StyleSkinned");
            if (prefab == null || shader == null)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Rock";
                RemoveCollider(sphere);
                sphere.transform.SetParent(parent, false);
                sphere.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(PlaceholderPalette.Grey(.42f));
                return sphere.transform;
            }

            if (!ModelMaterials.TryGetValue(name, out var material) || material == null)
            {
                material = new Material(shader) { name = name };
                material.SetTexture("_MainTex", Resources.Load<Texture2D>("Meshy/" + name + "-tex"));
                material.SetFloat("_Glow", fire ? 1f : 0f);
                ModelMaterials[name] = material;
            }

            var pivot = new GameObject("Rock").transform;
            pivot.SetParent(parent, false);
            // The model keeps its own import scale and turn; a holder between sizes and centres it.
            var holder = new GameObject("Model").transform;
            holder.SetParent(pivot, false);
            var model = Instantiate(prefab, holder, false);
            var bounds = new Bounds();
            var first = true;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (first)
                {
                    bounds = renderer.bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            // Its widest side one metre, its middle on the pivot so it turns about itself.
            var size = Mathf.Max(0.01f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
            holder.localScale = Vector3.one / size;
            holder.localPosition = -(bounds.center - pivot.position) / size;
            return pivot;
        }

        private void Update()
        {
            UpdateTrail();
            UpdateShards();
            switch (state)
            {
                case State.Falling:
                    timer += Time.deltaTime;
                    var t = Mathf.Clamp01(timer / FallSeconds);
                    ball.position = dropPoint + Vector3.up * (BallRadius + (FallHeight - BallRadius) * (1f - t * t));
                    if (t >= 1f)
                    {
                        Impact();
                    }

                    break;
                case State.Rolling:
                    Roll();
                    break;
                case State.Fading:
                    timer += Time.deltaTime;
                    ball.localScale = Vector3.one * BallRadius * 2f * Mathf.Clamp01(1f - timer / FadeSeconds);
                    if (timer >= FadeSeconds && trail.Count == 0 && shards.Count == 0)
                    {
                        Destroy(gameObject);
                    }

                    break;
            }
        }

        private void Impact()
        {
            var dealt = enemies != null
                ? enemies.DamageAndKnockbackInRadius(dropPoint, settings.ImpactRadius, settings.ImpactDamage, 0f,
                    settings.ImpactMaxTargets, out _)
                : 0f;
            reportDamage?.Invoke(dealt);
            // Thrown off the way it will roll: sideways and forward, none back (user, 2026-11-09).
            enemies?.ThrowOffLine(dropPoint, settings.ImpactRadius, settings.ImpactKnockback * CrowdPushScale,
                settings.ImpactLaunch, RollAxis());
            if (settings.QuakeRadius > 0f && settings.QuakeSeconds > 0f)
            {
                enemies?.StunInRadius(dropPoint, settings.QuakeRadius, settings.QuakeSeconds);
                SpecialEffectVisuals.SpawnQuake(dropPoint, settings.QuakeRadius);
            }

            state = settings.RollDistance > 0f && (rollsStraight || (route != null && route.HasUsableRoute))
                ? State.Rolling
                : State.Fading;
            straightStart = dropPoint;
            timer = 0f;
            hitTimer = 0f;
            nextTrailAt = 0f;
            if (state == State.Fading)
            {
                End(dropPoint);
            }
        }

        private void Roll()
        {
            var step = settings.RollSpeed * Time.deltaTime;
            var previous = ball.position;
            distance += direction * step;
            rolled += step;
            Vector3 position;
            if (rollsStraight)
            {
                position = straightStart + straightDirection * Mathf.Min(rolled, settings.RollDistance);
            }
            else
            {
                var joined = Mathf.Clamp01(1f - rolled / JoinRoadDistance);
                position = route.Sample(Mathf.Clamp(distance, 0f, route.TotalLength)) + lateralOffset * joined;
            }
            position.y = BallRadius;
            ball.position = position;
            var moved = position - previous;
            moved.y = 0f;
            if (moved.sqrMagnitude > 0.000001f)
            {
                var axis = Vector3.Cross(Vector3.up, moved.normalized);
                ball.Rotate(axis, moved.magnitude / BallRadius * Mathf.Rad2Deg, Space.World);
            }

            hitTimer -= Time.deltaTime;
            if (hitTimer <= 0f)
            {
                hitTimer += HitStepSeconds;
                // Spread over the steps a goblin spends under the rock, so one pass deals RollDamage.
                var stepsUnder = Mathf.Max(1f, 2f * settings.RollRadius / Mathf.Max(0.1f, settings.RollSpeed) / HitStepSeconds);
                var dealt = enemies.DamageAndKnockbackInRadius(position, settings.RollRadius, settings.RollDamage / stepsUnder,
                    0f, MaxRolledPerStep, out _);
                reportDamage?.Invoke(dealt);
                enemies.ThrowOffLine(position, settings.RollRadius, settings.RollKnockback * CrowdPushScale, RollLaunch,
                    RollAxis());
            }

            if (settings.FireTrail && rolled >= nextTrailAt)
            {
                nextTrailAt += TrailSpacing;
                AddTrailPatch(new Vector3(position.x, 0f, position.z));
            }

            if (rolled >= settings.RollDistance ||
                (!rollsStraight && (distance <= 0f || distance >= route.TotalLength)))
            {
                state = State.Fading;
                timer = 0f;
                End(new Vector3(position.x, 0f, position.z));
            }
        }

        // The way the rock rolls here: the drag's way, or along the road in its rolling direction.
        private Vector3 RollAxis()
        {
            if (rollsStraight)
            {
                return straightDirection;
            }

            if (route == null || !route.HasUsableRoute)
            {
                return Vector3.zero;
            }

            var here = Mathf.Clamp(distance, 0f, route.TotalLength);
            var ahead = Mathf.Clamp(distance + direction, 0f, route.TotalLength);
            var axis = route.Sample(ahead) - route.Sample(here);
            axis.y = 0f;
            return axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.zero;
        }

        // The end of the rock (user, 2026-11-12): it bursts, into the Shattering special's shards when
        // bought, otherwise into harmless chips of itself; its fire bursts with it.
        private void End(Vector3 center)
        {
            if (fire != null)
            {
                fire.Burst();
            }

            Shatter(center);
            if (shattered || burst)
            {
                return;
            }

            burst = true;
            var renderer = ball.GetComponentInChildren<Renderer>();
            var material = renderer != null && ModelMaterials.ContainsValue(renderer.sharedMaterial) ? renderer.sharedMaterial : null;
            RockDebris.Burst(ball.position, BallRadius, material);
            ball.localScale = Vector3.zero;
            timer = FadeSeconds;
        }

        private void Shatter(Vector3 center)
        {
            if (shattered || settings.ShatterShards <= 0 || settings.ShatterDamage <= 0f)
            {
                return;
            }

            shattered = true;
            // The rock is gone at once; the shards carry on.
            ball.localScale = Vector3.zero;
            timer = FadeSeconds;
            var turn = UnityEngine.Random.Range(0f, 360f);
            for (var i = 0; i < settings.ShatterShards; i++)
            {
                var angle = (turn + 360f * i / settings.ShatterShards) * Mathf.Deg2Rad;
                var reach = UnityEngine.Random.Range(ShardMinDistance, ShardMaxDistance);
                shards.Add(new Shard
                {
                    From = center + Vector3.up * BallRadius,
                    To = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * reach,
                    Visual = SpecialEffectVisuals.CreateRockChip(transform, ShardRadius * 1.4f),
                    Spin = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(360f, 900f)
                });
            }

            SpecialEffectVisuals.SpawnShatterBurst(center);
        }

        private void UpdateShards()
        {
            for (var i = shards.Count - 1; i >= 0; i--)
            {
                var shard = shards[i];
                shard.Age += Time.deltaTime;
                var t = Mathf.Clamp01(shard.Age / ShardFlightSeconds);
                var position = Vector3.Lerp(shard.From, shard.To, t);
                position.y = Mathf.Lerp(shard.From.y, ShardRadius, t) + 4f * 0.8f * t * (1f - t);
                shard.Visual.position = position;
                shard.Visual.Rotate(shard.Spin * Time.deltaTime, Space.World);
                if (t >= 1f)
                {
                    SpecialEffectVisuals.SpawnPuff(shard.To, 1.3f);
                    var dealt = enemies != null
                        ? enemies.DamageAndKnockbackInRadius(shard.To, ShardHitRadius, settings.ShatterDamage, ShardKnockback,
                            ShardMaxTargets, out _)
                        : 0f;
                    reportDamage?.Invoke(dealt);
                    Destroy(shard.Visual.gameObject);
                    shards.RemoveAt(i);
                    continue;
                }

                shards[i] = shard;
            }
        }

        private void AddTrailPatch(Vector3 position)
        {
            // Scorched ground with flames on it; the effect ends itself when the patch stops burning.
            var visual = SpecialEffectVisuals.SpawnFirePatch(position, TrailRadius, TrailSeconds);
            trail.Add(new TrailPatch { Position = position, Visual = visual });
        }

        private void UpdateTrail()
        {
            if (trail.Count == 0)
            {
                return;
            }

            trailTimer -= Time.deltaTime;
            var burn = trailTimer <= 0f;
            if (burn)
            {
                trailTimer += TrailBurnStep;
            }

            for (var i = trail.Count - 1; i >= 0; i--)
            {
                var patch = trail[i];
                patch.Age += Time.deltaTime;
                if (patch.Age >= TrailSeconds)
                {
                    if (patch.Visual != null)
                    {
                        Destroy(patch.Visual);
                    }

                    trail.RemoveAt(i);
                    continue;
                }

                trail[i] = patch;
                if (burn && enemies != null)
                {
                    enemies.DamageAndKnockbackInRadius(patch.Position, TrailRadius, 0f, 0f, out _, null,
                        settings.FireDamagePerTick, settings.FireTicksPerSecond > 0f ? settings.FireTicksPerSecond : DefaultBurnTicksPerSecond,
                        BurnSeconds, BurnStacks);
                }
            }
        }

        private static void RemoveCollider(GameObject target)
        {
            // The physics module is not in the project, so colliders are found by name.
            foreach (var component in target.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }
        }
    }
}
