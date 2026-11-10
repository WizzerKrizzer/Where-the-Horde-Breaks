using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    // Greybox effects of the Spear Throwers (user, 2026-11-08): the dust a throw raises around the
    // hunter, the spear left standing in the ground, and the red flash of a big-game hit. Plain
    // primitives that grow and shrink (no fades), until the real art comes. Sizes are TEMPORARY.
    internal sealed class SpearVisuals : MonoBehaviour
    {
        private const int DustPuffs = 10;
        private const float DustSeconds = 0.7f;
        private const float StuckSpearSeconds = 4f;
        private const float StuckSpearSinkSeconds = 0.5f;
        private const float StuckSpearLength = 1.1f;
        private const float StuckSpearTiltDegrees = 35f;
        private const int MaximumStuckSpears = 40;
        private const float HeavyFlashSeconds = 0.22f;

        private static readonly List<GameObject> StuckSpears = new();
        private static readonly Color DustColor = PlaceholderPalette.Grey(.72f);
        private static readonly Color HeavyFlashColor = new(0.92f, 0.16f, 0.12f, 1f);

        private enum Kind
        {
            Dust,
            StuckSpear,
            HeavyFlash
        }

        private Kind kind;
        private float elapsed;
        private float lifetime;
        private Transform[] parts;
        private Vector3[] partStart;
        private Vector3[] partEnd;
        private Vector3[] partScale;

        public static int DustCloudsSpawned { get; private set; }
        public static int SpearsStuck { get; private set; }
        public static int HeavyFlashesSpawned { get; private set; }

        // A ring of dust puffs around the hunter's feet that rolls outward and up, then shrinks away.
        public static void SpawnThrowDust(Vector3 feet)
        {
            DustCloudsSpawned++;
            var puffs = EffectsQuality.Pieces(DustPuffs);
            if (puffs <= 0)
            {
                return;
            }

            var go = new GameObject("SpearThrowDust");
            go.transform.position = feet;
            var visual = go.AddComponent<SpearVisuals>();
            visual.kind = Kind.Dust;
            visual.lifetime = DustSeconds;
            visual.parts = new Transform[puffs];
            visual.partStart = new Vector3[puffs];
            visual.partEnd = new Vector3[puffs];
            visual.partScale = new Vector3[puffs];
            var turn = Random.Range(0f, 360f);
            for (var i = 0; i < puffs; i++)
            {
                var angle = (turn + i * 360f / puffs + Random.Range(-12f, 12f)) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var puff = CreatePart(go.transform, PrimitiveType.Cube, DustColor);
                visual.parts[i] = puff;
                visual.partStart[i] = outward * 0.22f + Vector3.up * 0.06f;
                visual.partEnd[i] = outward * Random.Range(0.9f, 1.35f) + Vector3.up * Random.Range(0.25f, 0.6f);
                visual.partScale[i] = Vector3.one * Random.Range(0.18f, 0.3f);
                puff.localPosition = visual.partStart[i];
                puff.localRotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
                puff.localScale = visual.partScale[i];
            }
        }

        // The spear left in the ground where it stopped, leaning back along its flight; it stands a
        // few seconds, then sinks away.
        public static void SpawnStuckSpear(Vector3 groundPoint, Vector3 flightDirection, Color color)
        {
            flightDirection.y = 0f;
            if (flightDirection.sqrMagnitude < 0.0001f)
            {
                flightDirection = Vector3.forward;
            }
            flightDirection.Normalize();

            SpearsStuck++;
            groundPoint.y = 0f;
            var go = new GameObject("StuckSpear");
            go.transform.position = groundPoint;
            var visual = go.AddComponent<SpearVisuals>();
            visual.kind = Kind.StuckSpear;
            visual.lifetime = StuckSpearSeconds;
            var shaft = CreatePart(go.transform, PrimitiveType.Cube, color);
            // The tip points down along the flight, buried a little; the shaft leans back toward the thrower.
            var pointing = Quaternion.LookRotation(flightDirection, Vector3.up) * Quaternion.Euler(90f - StuckSpearTiltDegrees, 0f, 0f);
            shaft.localRotation = pointing;
            shaft.localScale = new Vector3(0.06f, 0.06f, StuckSpearLength);
            var along = pointing * Vector3.forward;
            shaft.localPosition = -along * (StuckSpearLength * 0.5f - 0.12f);
            visual.parts = new[] { shaft };
            visual.partStart = new[] { shaft.localPosition };
            visual.partEnd = new[] { shaft.localPosition + along * StuckSpearLength * 0.9f };

            StuckSpears.RemoveAll(item => item == null);
            StuckSpears.Add(go);
            while (StuckSpears.Count > MaximumStuckSpears)
            {
                var oldest = StuckSpears[0];
                StuckSpears.RemoveAt(0);
                if (oldest != null)
                {
                    Destroy(oldest);
                }
            }
        }

        // A short red burst on a heavy goblin hit by the big-game hunters.
        public static void SpawnHeavyHitFlash(Vector3 position)
        {
            HeavyFlashesSpawned++;
            var go = new GameObject("SpearHeavyHitFlash");
            go.transform.position = position + Vector3.up * 0.55f;
            var visual = go.AddComponent<SpearVisuals>();
            visual.kind = Kind.HeavyFlash;
            visual.lifetime = HeavyFlashSeconds;
            var flash = CreatePart(go.transform, PrimitiveType.Sphere, HeavyFlashColor);
            flash.localScale = Vector3.one * 0.9f;
            visual.parts = new[] { flash };
        }

        // The physics module is not in the project, so colliders are found by their type name.
        internal static void RemoveColliders(GameObject go)
        {
            var components = go.GetComponents<Component>();
            for (var i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }
        }

        public static void ResetCounters()
        {
            DustCloudsSpawned = 0;
            SpearsStuck = 0;
            HeavyFlashesSpawned = 0;
        }

        private static Transform CreatePart(Transform parent, PrimitiveType type, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            RemoveColliders(part);

            part.transform.SetParent(parent, false);
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = BootstrapMaterials.GetUnlit(color);
            renderer.shadowCastingMode = type == PrimitiveType.Cube && color != DustColor ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part.transform;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, lifetime));
            switch (kind)
            {
                case Kind.Dust:
                {
                    var travel = 1f - (1f - t) * (1f - t) * (1f - t);
                    // Grows a little as it billows, then shrinks away over the last 45%.
                    var size = t < 0.55f ? Mathf.Lerp(1f, 1.6f, t / 0.55f) : Mathf.Lerp(1.6f, 0f, (t - 0.55f) / 0.45f);
                    for (var i = 0; i < parts.Length; i++)
                    {
                        parts[i].localPosition = Vector3.LerpUnclamped(partStart[i], partEnd[i], travel);
                        parts[i].localScale = partScale[i] * size;
                    }
                    break;
                }
                case Kind.StuckSpear:
                {
                    var sinkStart = 1f - StuckSpearSinkSeconds / lifetime;
                    if (t > sinkStart)
                    {
                        var sink = (t - sinkStart) / (1f - sinkStart);
                        parts[0].localPosition = Vector3.Lerp(partStart[0], partEnd[0], sink * sink);
                    }
                    break;
                }
                case Kind.HeavyFlash:
                    parts[0].localScale = Vector3.one * Mathf.Lerp(0.9f, 1.5f, t) * (1f - t * t);
                    break;
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
