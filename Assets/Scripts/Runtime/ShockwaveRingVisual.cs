using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    internal sealed class ShockwaveRingVisual : MonoBehaviour
    {
        private const int SegmentCount = 96;
        // Keep rapid-fire meteor tests from accumulating long-lived renderers.
        private const int MaximumConcurrentRings = 12;
        private static readonly Color RingColor = new(1f, 0.91f, 0.68f, 0.38f);
        private static readonly Color HazeColor = new(1f, 0.94f, 0.78f, 0.14f);
        private static readonly List<GameObject> ActiveRings = new();

        private readonly Vector3[] positions = new Vector3[SegmentCount];
        private readonly Vector3[] hazePositions = new Vector3[SegmentCount];
        private readonly Vector2[] unitCircle = new Vector2[SegmentCount];
        private LineRenderer line;
        private LineRenderer hazeLine;
        private Vector3 center;
        private float startRadius;
        private float endRadius;
        private float duration;
        private float elapsed;
        private Vector2 arenaCenter;
        private float arenaRadius;
        private ParticleSystem travellingDust;

        public static void Spawn(
            Vector3 position,
            float initialRadius,
            float maximumRadius,
            float speed,
            Vector3 boundaryCenter,
            float boundaryRadius)
        {
            if (maximumRadius <= initialRadius || speed <= 0f)
            {
                return;
            }

            var go = new GameObject("ActiveWeaponShockwaveRing");
            var visual = go.AddComponent<ShockwaveRingVisual>();
            visual.Initialize(
                position,
                initialRadius,
                maximumRadius,
                speed,
                boundaryCenter,
                boundaryRadius);
            ActiveRings.RemoveAll(item => item == null);
            ActiveRings.Add(go);
            while (ActiveRings.Count > MaximumConcurrentRings)
            {
                var oldest = ActiveRings[0];
                ActiveRings.RemoveAt(0);
                if (oldest != null)
                {
                    Destroy(oldest);
                }
            }
        }

        private void Initialize(
            Vector3 position,
            float initialRadius,
            float maximumRadius,
            float speed,
            Vector3 boundaryCenter,
            float boundaryRadius)
        {
            center = position + Vector3.up * 0.72f;
            arenaCenter = new Vector2(boundaryCenter.x, boundaryCenter.z);
            arenaRadius = Mathf.Max(0f, boundaryRadius);
            startRadius = Mathf.Max(0.01f, initialRadius);
            endRadius = Mathf.Max(startRadius, maximumRadius);
            duration = (endRadius - startRadius) / speed;

            line = CreateLineRenderer(gameObject, RingColor);
            var haze = new GameObject("ShockwaveCloudHaze");
            haze.transform.SetParent(transform, false);
            hazeLine = CreateLineRenderer(haze, HazeColor);

            for (var i = 0; i < SegmentCount; i++)
            {
                var angle = i * Mathf.PI * 2f / SegmentCount;
                unitCircle[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            CreateTravellingDust();
            UpdateRing(startRadius, 1f);
        }

        private void Update()
        {
            elapsed += Time.timeScale > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
            if (travellingDust != null)
            {
                var main = travellingDust.main;
                main.simulationSpeed = Time.timeScale > 0f ? Mathf.Max(0.01f, Time.timeScale) : 1f;
            }
            var progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            var radius = Mathf.Lerp(startRadius, endRadius, progress);
            var fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f, 1f, progress));
            UpdateRing(radius, fade);

            if (progress >= 1f)
            {
                Destroy(gameObject);
            }
        }

        private void UpdateRing(float radius, float opacity)
        {
            var progress = Mathf.Clamp01(elapsed / duration);
            var cloudAmplitude = Mathf.Lerp(0.4f, 2.1f, progress);
            var phase = elapsed * 0.72f;
            for (var i = 0; i < SegmentCount; i++)
            {
                var angle = i * Mathf.PI * 2f / SegmentCount;
                var cloudNoise =
                    Mathf.Sin(angle * 3f + phase) * 0.5f +
                    Mathf.Sin(angle * 7f - phase * 0.73f + 1.4f) * 0.3f +
                    Mathf.Sin(angle * 13f + phase * 1.17f + 3.1f) * 0.2f;
                var hazeNoise =
                    Mathf.Sin(angle * 4f - phase * 0.61f + 0.8f) * 0.58f +
                    Mathf.Sin(angle * 9f + phase * 0.44f + 2.2f) * 0.42f;
                var cloudRadius = radius + cloudNoise * cloudAmplitude;
                var hazeRadius = radius + hazeNoise * cloudAmplitude * 1.35f;
                var boundaryDistance = GetBoundaryDistance(unitCircle[i]);
                cloudRadius = Mathf.Min(cloudRadius, boundaryDistance);
                hazeRadius = Mathf.Min(hazeRadius, boundaryDistance);
                positions[i] = center + new Vector3(
                    unitCircle[i].x * cloudRadius,
                    0f,
                    unitCircle[i].y * cloudRadius);
                hazePositions[i] = center + new Vector3(
                    unitCircle[i].x * hazeRadius,
                    0.01f,
                    unitCircle[i].y * hazeRadius);
            }

            line.SetPositions(positions);
            hazeLine.SetPositions(hazePositions);
            var color = RingColor;
            color.a *= opacity;
            line.startColor = color;
            line.endColor = color;
            line.widthMultiplier = Mathf.Lerp(0.85f, 0.5f, progress);
            var hazeColor = HazeColor;
            hazeColor.a *= opacity;
            hazeLine.startColor = hazeColor;
            hazeLine.endColor = hazeColor;
            hazeLine.widthMultiplier = Mathf.Lerp(2.2f, 1.15f, progress);
        }

        private float GetBoundaryDistance(Vector2 direction)
        {
            if (arenaRadius <= 0f)
            {
                return float.PositiveInfinity;
            }

            var origin = new Vector2(center.x, center.z) - arenaCenter;
            var projection = Vector2.Dot(origin, direction);
            var discriminant = projection * projection + arenaRadius * arenaRadius - origin.sqrMagnitude;
            return discriminant > 0f
                ? Mathf.Max(0f, -projection + Mathf.Sqrt(discriminant))
                : 0f;
        }

        private void CreateTravellingDust()
        {
            var dustObject = new GameObject("TravellingShockwaveDust");
            dustObject.transform.SetParent(transform, false);
            var particles = dustObject.AddComponent<ParticleSystem>();
            travellingDust = particles;
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.startSpeed = 0f;
            main.startLifetime = duration;
            main.startSize = 1f;
            main.maxParticles = SegmentCount;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = BootstrapMaterials.GetUnlit(new Color(0.68f, 0.61f, 0.48f, 0.16f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            for (var i = 0; i < SegmentCount; i++)
            {
                var direction = unitCircle[i];
                var boundaryDistance = Mathf.Min(endRadius, GetBoundaryDistance(direction));
                var travelDistance = Mathf.Max(0f, boundaryDistance - startRadius);
                if (travelDistance <= 0.01f)
                {
                    continue;
                }

                var emit = new ParticleSystem.EmitParams
                {
                    position = center + new Vector3(direction.x, 0.04f, direction.y) * startRadius,
                    velocity = new Vector3(direction.x, 0f, direction.y) *
                        ((endRadius - startRadius) / Mathf.Max(0.01f, duration)),
                    startLifetime = travelDistance / Mathf.Max(0.01f,
                        (endRadius - startRadius) / Mathf.Max(0.01f, duration)),
                    startSize = Mathf.Lerp(0.55f, 1.35f, Mathf.Repeat(i * 0.6180339f, 1f)),
                    startColor = new Color(0.72f, 0.66f, 0.54f, 0.14f)
                };
                particles.Emit(emit, 1);
            }

            particles.Play();
        }

        private static LineRenderer CreateLineRenderer(GameObject target, Color color)
        {
            var renderer = target.AddComponent<LineRenderer>();
            renderer.useWorldSpace = true;
            renderer.loop = true;
            renderer.positionCount = SegmentCount;
            renderer.numCornerVertices = 3;
            renderer.material = BootstrapMaterials.GetUnlit(new Color(1f, 1f, 1f, 0.5f));
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.textureMode = LineTextureMode.Stretch;
            return renderer;
        }
    }
}
