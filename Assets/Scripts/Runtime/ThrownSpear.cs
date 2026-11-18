using System.Collections.Generic;
using TowerDefense.Data;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The first active weapon (user, 2026-11-12, replacing the rock, which comes back later): click a
    /// goblin and a spear flies in from the camera's side on a short arc, follows it, hits it alone,
    /// and sticks in the ground there for a few seconds before it sinks away. Simple and fast, no
    /// aiming and no crowd damage. Its upgrades (user, 2026-11-12): the heavy spear's blow also hurts
    /// the goblins around its target, the volley throws more spears at once, and a precise strike
    /// deals several times the damage with a red burst. Numbers are TEMPORARY until balancing.
    /// </summary>
    public sealed class ThrownSpear : MonoBehaviour
    {
        // How far from the click a goblin is picked.
        public const float PickRadius = 1.6f;
        // How far from the click the volley's other spears aim.
        public const float VolleySpread = 1.4f;
        private const float FlightSeconds = 0.32f;
        // Where it comes from: from a side of the screen, a little towards the camera, and up.
        private const float StartSide = 6.5f;
        private const float StartBack = 2.5f;
        private const float StartUp = 5.5f;
        private const float Arc = 0.9f;
        // It hits the goblin's body, not its feet.
        private const float BodyHeight = 0.35f;
        // How deep the tip goes into the ground, and how steep it stands.
        private const float BuryDepth = 0.28f;
        private const float StuckTiltDegrees = 55f;
        private const float SinkSeconds = 0.45f;
        private const int MaximumSpears = 24;
        // A goblin that dies before the spear lands: the nearest one within this takes the hit.
        private const float FallbackRadius = 0.8f;

        private static readonly List<ThrownSpear> Spears = new();
        // Goblins a spear in the air is already flying at, so the volley's spears pick others.
        private static readonly HashSet<int> Claimed = new();

        /// <summary>What one spear does.</summary>
        public struct Settings
        {
            public float Damage;
            public float StuckSeconds;
            // The heavy spear: goblins within this of its target take this share of its damage.
            public float SplashRadius;
            public float SplashFraction;
            // The precise strike: the chance (0-1) and how many times the damage.
            public float CritChance;
            public float CritMultiplier;
            // Seconds before it is thrown (the volley's spears come a moment apart).
            public float Delay;
        }

        private EnemyManager enemies;
        private System.Action<float> onDamage;
        private Settings settings;
        private float wait;
        private int hordeIndex = -1;
        private EnemyActor actor;
        private Vector3 click;
        private Vector3 start;
        private Vector3 target;
        private Vector3 previous;
        private Vector3 flight;
        private float elapsed;
        private bool stuck;
        private float stuckElapsed;
        private Vector3 stuckPosition;
        private Transform model;

        // The camera the spear comes from (the game's main camera when not set; a capture sets its own).
        public static Camera ViewCamera { get; set; }
        public static int Thrown { get; private set; }
        public static int Hits { get; private set; }
        public static int Stuck { get; private set; }
        public static int Shocks { get; private set; }
        public static int Crits { get; private set; }
        public bool WasCrit { get; private set; }
        public bool IsStuck => stuck;
        public bool HasTarget => hordeIndex >= 0 || actor != null;

        public static void ResetCounters()
        {
            Thrown = 0;
            Hits = 0;
            Stuck = 0;
            Shocks = 0;
            Crits = 0;
        }

        /// <summary>
        /// Throws a spear at the goblin nearest `point` (within PickRadius), or at the ground there when
        /// no goblin is near. `onDamage` gets the damage dealt when it lands.
        /// </summary>
        public static ThrownSpear Throw(EnemyManager enemies, Vector3 point, float damage, float stuckSeconds, System.Action<float> onDamage)
        {
            return Throw(enemies, point, new Settings { Damage = damage, StuckSeconds = stuckSeconds, CritMultiplier = 3f }, onDamage);
        }

        public static ThrownSpear Throw(EnemyManager enemies, Vector3 point, Settings settings, System.Action<float> onDamage)
        {
            var go = new GameObject("ThrownSpear");
            var spear = go.AddComponent<ThrownSpear>();
            spear.enemies = enemies;
            settings.StuckSeconds = Mathf.Max(0.1f, settings.StuckSeconds);
            spear.settings = settings;
            spear.wait = Mathf.Max(0f, settings.Delay);
            spear.onDamage = onDamage;
            point.y = 0f;
            spear.target = point;
            spear.click = point;
            spear.Pick();

            spear.target.y = 0f;
            // From the left or the right of the screen and a little from below it, so the flight
            // crosses the view and the spear in the ground leans sideways, seen at its full length
            // (from the camera's side it was foreshortened to a stub).
            var back = Vector3.back;
            var camera = ViewCamera != null ? ViewCamera : Camera.main;
            if (camera != null)
            {
                back = camera.transform.position - spear.target;
                back.y = 0f;
                back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
            }

            var side = Vector3.Cross(Vector3.up, back) * (Random.value < 0.5f ? -1f : 1f);
            spear.start = spear.target + side * StartSide + back * StartBack + Vector3.up * StartUp;
            spear.previous = spear.start;
            spear.model = SpearModel.Create(go.transform, "Spear");
            spear.model.gameObject.SetActive(spear.wait <= 0f);
            go.transform.position = spear.start;
            go.transform.rotation = Quaternion.LookRotation(spear.target - spear.start, Vector3.up);
            Thrown++;
            Spears.RemoveAll(item => item == null);
            Spears.Add(spear);
            while (Spears.Count > MaximumSpears)
            {
                var oldest = Spears[0];
                Spears.RemoveAt(0);
                if (oldest != null)
                {
                    Destroy(oldest.gameObject);
                }
            }

            return spear;
        }

        private void Update()
        {
            if (stuck)
            {
                UpdateStuck();
                return;
            }

            if (wait > 0f)
            {
                wait -= Time.deltaTime;
                Pick();
                if (wait > 0f)
                {
                    return;
                }

                model.gameObject.SetActive(true);
            }

            elapsed += Time.deltaTime;
            Follow();
            var t = Mathf.Clamp01(elapsed / FlightSeconds);
            var aim = target + Vector3.up * BodyHeight;
            var position = Vector3.Lerp(start, aim, t) + Vector3.up * (Arc * 4f * t * (1f - t));
            var step = position - previous;
            if (step.sqrMagnitude > 1e-6f)
            {
                flight = step.normalized;
                transform.rotation = Quaternion.LookRotation(flight, Vector3.up);
            }

            transform.position = position;
            previous = position;
            if (t >= 1f)
            {
                Land();
            }
        }

        // The goblin nearest the click. The horde answers such questions a frame or two later (its
        // targeting runs on the GPU), so the spear keeps asking while it flies.
        private void Pick()
        {
            if (enemies == null || HasTarget)
            {
                return;
            }

            if (enemies.TryGetHordeTargetSnapshot(click, PickRadius, true, TowerTargetingMode.Closest, out var index, out var position, out _))
            {
                // Another spear is already on its way to that one: the next nearest, if there is one.
                if (Claimed.Contains(index) &&
                    enemies.TryGetNearestHordeTargetExcept(click, PickRadius, true, index, out var other, out var otherPosition, out _))
                {
                    index = other;
                    position = otherPosition;
                }

                hordeIndex = index;
                Claimed.Add(index);
                target = new Vector3(position.x, 0f, position.z);
                return;
            }

            var enemy = enemies.GetNearestEnemy(click, PickRadius);
            if (enemy != null)
            {
                actor = enemy;
                var position2 = enemy.transform.position;
                target = new Vector3(position2.x, 0f, position2.z);
            }
        }

        // Keeps aiming at the goblin while it walks.
        private void Follow()
        {
            if (enemies == null)
            {
                return;
            }

            Pick();

            if (hordeIndex >= 0 && enemies.TryGetHordeAgentPosition(hordeIndex, out var position))
            {
                target = new Vector3(position.x, 0f, position.z);
            }
            else if (actor != null && actor.IsAlive)
            {
                var position2 = actor.transform.position;
                target = new Vector3(position2.x, 0f, position2.z);
            }
        }

        // A hit that did damage: counted, and a precise strike shows its burst.
        private void Hit(float dealt, bool crit, Vector3 at)
        {
            Hits++;
            onDamage?.Invoke(dealt);
            if (crit)
            {
                Crits++;
                SpecialEffectVisuals.SpawnCritBurst(at);
            }
        }

        private void Land()
        {
            Release();
            var dealt = 0f;
            var damage = settings.Damage;
            WasCrit = settings.CritChance > 0f && Random.value < settings.CritChance;
            if (WasCrit)
            {
                damage *= Mathf.Max(1f, settings.CritMultiplier);
            }

            if (enemies != null)
            {
                if (hordeIndex >= 0 && enemies.TryGetHordeAgentPosition(hordeIndex, out _))
                {
                    dealt = enemies.DamageHordeTarget(hordeIndex, damage);
                }
                else if (actor != null && actor.IsAlive)
                {
                    dealt = actor.ApplyDamage(damage);
                }

                // Its goblin died on the way, or the horde never named one: the nearest one there.
                // Counted only when it really hits one (user, 2026-11-18: a spear thrown into
                // empty ground counted its full damage); the horde says so a few frames later.
                if (dealt <= 0f)
                {
                    var crit = WasCrit;
                    var at = target;
                    enemies.DamageInRadius(target, FallbackRadius, damage, 1, out _, onDealt: amount => Hit(amount, crit, at));
                }
            }

            if (dealt > 0f)
            {
                Hit(dealt, WasCrit, target);
            }

            // The heavy spear: the blow shakes the ground and hurts the goblins around (not the one
            // it hit again).
            if (settings.SplashRadius > 0f)
            {
                Shocks++;
                SpecialEffectVisuals.SpawnSpearShock(target, settings.SplashRadius);
                if (enemies != null)
                {
                    enemies.DamageInRadius(target, settings.SplashRadius, settings.Damage * settings.SplashFraction, 24, out _, hordeIndex,
                        amount => onDamage?.Invoke(amount));
                }
            }

            // Into the ground where the goblin stood, leaning back the way it came.
            var along = flight;
            along.y = 0f;
            along = along.sqrMagnitude > 0.0001f ? along.normalized : Vector3.forward;
            var pointing = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(StuckTiltDegrees, 0f, 0f);
            var down = pointing * Vector3.forward;
            stuckPosition = target + down * (BuryDepth / Mathf.Max(0.1f, -down.y));
            transform.SetPositionAndRotation(stuckPosition, pointing);
            SpecialEffectVisuals.SpawnPuff(target, 0.55f);
            stuck = true;
            Stuck++;
        }

        private void Release()
        {
            if (hordeIndex >= 0)
            {
                Claimed.Remove(hordeIndex);
            }
        }

        private void OnDestroy()
        {
            if (!stuck)
            {
                Release();
            }
        }

        // Stands, then slides down into the ground along its shaft and is gone.
        private void UpdateStuck()
        {
            stuckElapsed += Time.deltaTime;
            // A short shudder as it bites in.
            var shake = stuckElapsed < 0.25f ? Mathf.Sin(stuckElapsed * 70f) * (0.25f - stuckElapsed) * 14f : 0f;
            model.localRotation = Quaternion.Euler(shake, 0f, 0f);
            var sinkStart = settings.StuckSeconds;
            if (stuckElapsed > sinkStart)
            {
                var sink = Mathf.Clamp01((stuckElapsed - sinkStart) / SinkSeconds);
                transform.position = stuckPosition + transform.forward * (SpearModel.Length * sink * sink);
                if (sink >= 1f)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
