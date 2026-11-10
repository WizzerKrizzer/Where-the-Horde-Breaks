using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    // Greybox effects of the special upgrades (user, 2026-11-09: "improve the effects"): the rock's
    // quake, burning trail and shattering, and the slingers' bouncing and bursting stones. Plain
    // primitives that move, grow and shrink, like the spear's dust, until the real art comes.
    // Gameplay stays where it was (RollingRock, TowerActor); this only draws. Sizes are TEMPORARY.
    internal sealed class SpecialEffectVisuals : MonoBehaviour
    {
        private static readonly Color DustColor = PlaceholderPalette.Grey(.72f);
        private static readonly Color CrackColor = PlaceholderPalette.Grey(.1f);
        private static readonly Color ShockColor = PlaceholderPalette.Grey(.85f);
        private static readonly Color ScorchColor = new(0.1f, 0.08f, 0.07f, 1f);
        private static readonly Color FlameOuterColor = new(0.95f, 0.45f, 0.12f, 1f);
        private static readonly Color FlameInnerColor = new(1f, 0.82f, 0.32f, 1f);
        private static readonly Color RockColor = PlaceholderPalette.Grey(.42f);

        private struct Part
        {
            public Transform Transform;
            public Vector3 From;
            public Vector3 To;
            public Vector3 ScaleFrom;
            public Vector3 ScaleTo;
            public float Delay;
            public float Duration;
            // Height of a hop along the way (0: a straight line).
            public float Hop;
            public Vector3 Spin;
            // Flames: the height flickers.
            public float Flicker;
            public float Phase;
            // Fades (shrinks) away over the last this many seconds of the effect.
            public float FadeOut;
            // Gone as soon as its own move is done (the quake's running ring).
            public bool Vanish;
        }

        private readonly List<Part> parts = new();
        private float elapsed;
        private float lifetime;

        public static int QuakesShown { get; private set; }
        public static int FirePatchesShown { get; private set; }
        public static int BurstsShown { get; private set; }

        /// <summary>
        /// The rock's quake: a shock ring runs out over the ground, cracks open from the middle and
        /// dust jumps up around it; the cracks stay a moment, then close.
        /// </summary>
        public static void SpawnQuake(Vector3 center, float radius)
        {
            QuakesShown++;
            center.y = 0f;
            var effect = Create("RockQuake", center, 1.6f);
            const int ringPieces = 28;
            for (var i = 0; i < ringPieces; i++)
            {
                var angle = i * Mathf.PI * 2f / ringPieces;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var piece = effect.AddPart(PrimitiveType.Cube, ShockColor);
                piece.localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, outward), Vector3.up);
                effect.parts.Add(new Part
                {
                    Transform = piece,
                    From = outward * radius * 0.25f + Vector3.up * 0.04f,
                    To = outward * radius + Vector3.up * 0.04f,
                    ScaleFrom = new Vector3(0.14f, 0.05f, radius * 0.12f),
                    ScaleTo = new Vector3(0.04f, 0.02f, radius * 0.24f),
                    Duration = 0.45f,
                    Vanish = true
                });
            }

            // Fewer cracks and no branches at lower effect settings; the running ring always shows.
            var cracks = EffectsQuality.Pieces(Random.Range(6, 9));
            var turn = Random.Range(0f, 360f);
            for (var i = 0; i < cracks; i++)
            {
                var angle = (turn + i * 360f / cracks + Random.Range(-14f, 14f)) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var length = radius * Random.Range(0.45f, 0.85f);
                effect.AddCrack(Vector3.zero, outward, length, 0.09f, 0f);
                // A branch off the outer half of most cracks.
                if (EffectsQuality.Current == EffectsQuality.Level.Full && Random.value < 0.7f)
                {
                    var branchStart = outward * length * Random.Range(0.45f, 0.7f);
                    var branch = Quaternion.Euler(0f, Random.Range(25f, 45f) * (Random.value < 0.5f ? -1f : 1f), 0f) * outward;
                    effect.AddCrack(branchStart, branch, length * Random.Range(0.3f, 0.45f), 0.06f, 0.08f);
                }
            }

            effect.AddDust(Vector3.zero, radius * 0.55f, radius * 1.05f, EffectsQuality.Pieces(14), 0.32f, 0.75f);
        }

        /// <summary>
        /// One patch of the fire rock's trail: scorched ground with flames licking up from it,
        /// lasting seconds; the flames die down over the last of it.
        /// </summary>
        public static GameObject SpawnFirePatch(Vector3 position, float radius, float seconds)
        {
            FirePatchesShown++;
            position.y = 0f;
            var effect = Create("FireTrail", position, seconds);
            var scorch = effect.AddPart(PrimitiveType.Cylinder, ScorchColor, true);
            effect.parts.Add(new Part
            {
                Transform = scorch,
                From = Vector3.up * 0.02f,
                To = Vector3.up * 0.02f,
                ScaleFrom = new Vector3(radius * 1.1f, 0.01f, radius * 1.1f),
                ScaleTo = new Vector3(radius * 1.5f, 0.01f, radius * 1.5f),
                Duration = 0.25f,
                FadeOut = 0.6f
            });

            // At Minimal one flame stays, so a burning patch still shows where it burns.
            var flames = EffectsQuality.Pieces(Random.Range(5, 8), true);
            for (var i = 0; i < flames; i++)
            {
                var spot = Random.insideUnitCircle * radius * 0.6f;
                var inner = i % 3 == 0;
                var flame = effect.AddPart(PrimitiveType.Cube, inner ? FlameInnerColor : FlameOuterColor, true);
                var height = Random.Range(0.35f, 0.7f) * (inner ? 0.7f : 1f);
                var width = Random.Range(0.14f, 0.22f) * (inner ? 0.75f : 1f);
                flame.localRotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
                var at = new Vector3(spot.x, height * 0.45f, spot.y);
                effect.parts.Add(new Part
                {
                    Transform = flame,
                    From = new Vector3(spot.x, 0.05f, spot.y),
                    To = at,
                    ScaleFrom = Vector3.zero,
                    ScaleTo = new Vector3(width, height, width),
                    Delay = Random.Range(0f, 0.15f),
                    Duration = 0.2f,
                    Spin = new Vector3(0f, Random.Range(-90f, 90f), 0f),
                    Flicker = Random.Range(7f, 12f),
                    Phase = Random.Range(0f, 6.28f),
                    FadeOut = 0.8f
                });
            }

            return effect.gameObject;
        }

        /// <summary>The rock bursting: a cloud of dust where it stood.</summary>
        public static void SpawnShatterBurst(Vector3 center)
        {
            BurstsShown++;
            var puffs = EffectsQuality.Pieces(12);
            if (puffs <= 0)
            {
                return;
            }

            center.y = 0f;
            var effect = Create("RockShatterBurst", center, 0.8f);
            effect.AddDust(Vector3.up * 0.3f, 0.2f, 1.3f, puffs, 0.35f, 0.7f);
        }

        /// <summary>A small puff where a stone or a rock chip hits the ground.</summary>
        public static void SpawnPuff(Vector3 position, float size = 1f)
        {
            BurstsShown++;
            var puffs = EffectsQuality.Pieces(7);
            if (puffs <= 0)
            {
                return;
            }

            position.y = 0f;
            var effect = Create("HitPuff", position, 0.45f);
            effect.AddDust(Vector3.up * 0.05f, 0.1f * size, 0.65f * size, puffs, 0.2f * size, 0.45f);
        }

        /// <summary>A chunk of rock that tumbles as it flies (the shattering rock's shards).</summary>
        public static Transform CreateRockChip(Transform parent, float size)
        {
            var chip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chip.name = "RockShard";
            SpearVisuals.RemoveColliders(chip);
            chip.transform.SetParent(parent, false);
            chip.transform.localScale = new Vector3(Random.Range(0.8f, 1.3f), Random.Range(0.6f, 1f), Random.Range(0.8f, 1.4f)) * size;
            chip.transform.rotation = Random.rotation;
            chip.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(RockColor);
            return chip.transform;
        }

        // Props on a caveman for the cave's specials. They hang off the unit's own primitive, which
        // is scaled unevenly, so each prop root undoes that scale and is laid out in metres.
        private static readonly Color WoodColor = new(0.36f, 0.24f, 0.14f, 1f);
        private static readonly Color HideColor = new(0.62f, 0.5f, 0.34f, 1f);
        private static readonly Color HornColor = PlaceholderPalette.Grey(.9f);

        /// <summary>A burning torch in the caveman's hand (the Torches special).</summary>
        public static void AttachTorch(Transform holder)
        {
            if (holder.Find("Torch") != null)
            {
                return;
            }

            var size = holder.lossyScale;
            var effect = AttachRoot(holder, "Torch");
            var stick = effect.AddPart(PrimitiveType.Cube, WoodColor);
            var hand = new Vector3(size.x * 0.62f, 0f, size.z * 0.2f);
            stick.localRotation = Quaternion.Euler(18f, 0f, -12f);
            var top = hand + stick.localRotation * Vector3.up * 0.25f;
            effect.parts.Add(new Part
            {
                Transform = stick,
                From = hand,
                To = hand,
                ScaleFrom = new Vector3(0.06f, 0.5f, 0.06f),
                ScaleTo = new Vector3(0.06f, 0.5f, 0.06f),
                Duration = 0.01f
            });
            for (var i = 0; i < 2; i++)
            {
                var inner = i == 1;
                var flame = effect.AddPart(PrimitiveType.Cube, inner ? FlameInnerColor : FlameOuterColor, true);
                var width = inner ? 0.08f : 0.13f;
                var height = inner ? 0.18f : 0.28f;
                effect.parts.Add(new Part
                {
                    Transform = flame,
                    From = top + Vector3.up * height * 0.45f,
                    To = top + Vector3.up * height * 0.45f,
                    ScaleFrom = new Vector3(width, height, width),
                    ScaleTo = new Vector3(width, height, width),
                    Duration = 0.01f,
                    Spin = new Vector3(0f, inner ? -160f : 120f, 0f),
                    Flicker = inner ? 13f : 9f,
                    Phase = Random.Range(0f, 6.28f)
                });
            }
        }

        /// <summary>A round hide shield on the caveman's other arm (the Shields special).</summary>
        public static void AttachShield(Transform holder)
        {
            if (holder.Find("Shield") != null)
            {
                return;
            }

            var size = holder.lossyScale;
            var effect = AttachRoot(holder, "Shield");
            var shield = effect.AddPart(PrimitiveType.Cylinder, HideColor);
            // Upright on the left side, facing out and a little forward.
            shield.localRotation = Quaternion.Euler(0f, -25f, 90f);
            var at = new Vector3(-size.x * 0.62f, size.y * 0.1f, size.z * 0.12f);
            effect.parts.Add(new Part
            {
                Transform = shield,
                From = at,
                To = at,
                ScaleFrom = new Vector3(0.5f, 0.03f, 0.5f),
                ScaleTo = new Vector3(0.5f, 0.03f, 0.5f),
                Duration = 0.01f
            });
        }

        /// <summary>Two horns on the chief's head, so he stands out from his cavemen.</summary>
        public static void AttachChiefHorns(Transform holder)
        {
            if (holder.Find("ChiefHorns") != null)
            {
                return;
            }

            var size = holder.lossyScale;
            var effect = AttachRoot(holder, "ChiefHorns");
            // On top of the unit's own shape, whatever primitive it is.
            var filter = holder.GetComponent<MeshFilter>();
            var top = (filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.max.y : 0.5f) * size.y;
            foreach (var side in new[] { -1f, 1f })
            {
                var horn = effect.AddPart(PrimitiveType.Cylinder, HornColor);
                // Leaning outwards from the sides of the head.
                horn.localRotation = Quaternion.Euler(0f, 0f, -35f * side);
                var at = new Vector3(side * size.x * 0.42f, top + 0.12f, 0f) + horn.localRotation * Vector3.up * 0.1f;
                effect.parts.Add(new Part
                {
                    Transform = horn,
                    From = at,
                    To = at,
                    ScaleFrom = new Vector3(0.08f, 0.2f, 0.08f),
                    ScaleTo = new Vector3(0.08f, 0.2f, 0.08f),
                    Duration = 0.01f
                });
            }
        }

        private static SpecialEffectVisuals AttachRoot(Transform holder, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(holder, false);
            var scale = holder.lossyScale;
            go.transform.localScale = new Vector3(1f / Mathf.Max(0.001f, scale.x), 1f / Mathf.Max(0.001f, scale.y),
                1f / Mathf.Max(0.001f, scale.z));
            var effect = go.AddComponent<SpecialEffectVisuals>();
            effect.lifetime = float.PositiveInfinity;
            return effect;
        }

        private static SpecialEffectVisuals Create(string name, Vector3 position, float seconds)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var effect = go.AddComponent<SpecialEffectVisuals>();
            effect.lifetime = seconds;
            return effect;
        }

        private Transform AddPart(PrimitiveType shape, Color color, bool unlit = false)
        {
            var go = GameObject.CreatePrimitive(shape);
            SpearVisuals.RemoveColliders(go);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.zero;
            go.GetComponent<Renderer>().sharedMaterial = unlit ? BootstrapMaterials.GetUnlit(color) : BootstrapMaterials.Get(color);
            return go.transform;
        }

        // A crack: a thin dark slab that opens from start outwards along direction.
        private void AddCrack(Vector3 start, Vector3 direction, float length, float width, float delay)
        {
            var crack = AddPart(PrimitiveType.Cube, CrackColor, true);
            crack.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            parts.Add(new Part
            {
                Transform = crack,
                From = start + Vector3.up * 0.025f,
                To = start + direction * length * 0.5f + Vector3.up * 0.025f,
                ScaleFrom = new Vector3(width, 0.02f, 0.01f),
                ScaleTo = new Vector3(width * 0.6f, 0.02f, length),
                Delay = delay,
                Duration = 0.16f,
                FadeOut = 0.45f
            });
        }

        // Dust puffs thrown out from the middle and up, shrinking away.
        private void AddDust(Vector3 origin, float fromRadius, float toRadius, int puffs, float size, float seconds)
        {
            var turn = Random.Range(0f, 360f);
            for (var i = 0; i < puffs; i++)
            {
                var angle = (turn + i * 360f / puffs + Random.Range(-12f, 12f)) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var puff = AddPart(PrimitiveType.Cube, DustColor);
                puff.localRotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
                var puffSize = size * Random.Range(0.75f, 1.25f);
                parts.Add(new Part
                {
                    Transform = puff,
                    From = origin + outward * fromRadius,
                    To = origin + outward * toRadius * Random.Range(0.8f, 1.1f) + Vector3.up * size * Random.Range(1f, 2.5f),
                    ScaleFrom = Vector3.one * puffSize,
                    ScaleTo = Vector3.zero,
                    Duration = seconds * Random.Range(0.8f, 1f),
                    Spin = new Vector3(0f, Random.Range(-120f, 120f), 0f)
                });
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            var remaining = lifetime - elapsed;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Transform == null)
                {
                    continue;
                }

                var u = Mathf.Clamp01((elapsed - part.Delay) / Mathf.Max(0.01f, part.Duration));
                if (elapsed < part.Delay || (part.Vanish && u >= 1f))
                {
                    part.Transform.localScale = Vector3.zero;
                    continue;
                }

                var eased = 1f - (1f - u) * (1f - u);
                var position = Vector3.Lerp(part.From, part.To, eased);
                position.y += part.Hop * 4f * u * (1f - u);
                part.Transform.localPosition = position;
                var scale = Vector3.Lerp(part.ScaleFrom, part.ScaleTo, eased);
                if (part.Flicker > 0f)
                {
                    var flicker = 0.75f + 0.25f * Mathf.Sin(elapsed * part.Flicker + part.Phase) +
                                  0.1f * Mathf.Sin(elapsed * part.Flicker * 2.3f + part.Phase * 1.7f);
                    scale.y *= flicker;
                }

                if (part.FadeOut > 0f && remaining < part.FadeOut)
                {
                    scale *= Mathf.Clamp01(remaining / part.FadeOut);
                }

                part.Transform.localScale = scale;
                if (part.Spin != Vector3.zero)
                {
                    part.Transform.Rotate(part.Spin * Time.deltaTime, Space.World);
                }
            }

            if (elapsed >= lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
