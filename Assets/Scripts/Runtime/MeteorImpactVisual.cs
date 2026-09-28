using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Runtime
{
    internal sealed class MeteorImpactVisual : MonoBehaviour
    {
        private const float HitStopDuration = 0.05f;
        private const float VisualLifetime = 7f;
        private const int MaximumPersistentImpacts = 20;
        private static readonly List<GameObject> PersistentImpacts = new();
        private static int hitStopGeneration;
        private static float nextAllowedHitStopTime;
        private static Mesh cubeMesh;

        private Renderer flashRenderer;
        private Renderer craterRenderer;
        private MaterialPropertyBlock propertyBlock;
        private float radius;
        private float elapsed;
        private float hitStopRemaining;
        private float previousTimeScale;
        private int hitStopToken;

        public static void Spawn(Vector3 position, float impactRadius)
        {
            var go = new GameObject("MeteorImpactVisual");
            go.transform.position = position;
            var visual = go.AddComponent<MeteorImpactVisual>();
            visual.Initialize(Mathf.Max(0.5f, impactRadius));

            PersistentImpacts.RemoveAll(item => item == null);
            PersistentImpacts.Add(go);
            while (PersistentImpacts.Count > MaximumPersistentImpacts)
            {
                var oldest = PersistentImpacts[0];
                PersistentImpacts.RemoveAt(0);
                if (oldest != null)
                {
                    Destroy(oldest);
                }
            }
        }

        private void Initialize(float impactRadius)
        {
            radius = impactRadius;
            propertyBlock = new MaterialPropertyBlock();
            flashRenderer = CreateDisc(
                "MeteorImpactFlash",
                0.08f,
                new Color(1f, 0.76f, 0.24f, 0.82f));
            craterRenderer = CreateDisc(
                "MeteorScorchedCrater",
                0.025f,
                new Color(0.12f, 0.09f, 0.07f, 0.62f));
            craterRenderer.transform.localScale = new Vector3(radius * 1.25f, 0.012f, radius * 1.25f);
            CreateGroundDust();
            CreateDebris();
            BeginHitStop();
        }

        private Renderer CreateDisc(string objectName, float height, Color color)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = objectName;
            disc.transform.SetParent(transform, false);
            disc.transform.localPosition = Vector3.up * height;
            disc.transform.localScale = new Vector3(0.1f, 0.012f, 0.1f);
            RemoveCollider(disc);
            var renderer = disc.GetComponent<Renderer>();
            renderer.sharedMaterial = BootstrapMaterials.GetUnlit(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private void CreateGroundDust()
        {
            var dustObject = new GameObject("MeteorGroundDust");
            dustObject.transform.SetParent(transform, false);
            dustObject.transform.localPosition = Vector3.up * 0.16f;
            var particles = dustObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 1.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.58f, 0.49f, 0.37f, 0.34f),
                new Color(0.82f, 0.73f, 0.58f, 0.16f));
            main.gravityModifier = -0.08f;
            main.maxParticles = 96;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = BootstrapMaterials.GetUnlit(new Color(0.7f, 0.62f, 0.48f, 0.3f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            for (var i = 0; i < 72; i++)
            {
                var angle = i * Mathf.PI * 2f / 72f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var noise = Mathf.Repeat(i * 0.6180339f, 1f);
                var emit = new ParticleSystem.EmitParams
                {
                    position = dustObject.transform.position + direction * radius * Mathf.Lerp(0.04f, 0.2f, noise),
                    velocity = direction * radius * Mathf.Lerp(0.8f, 1.8f, noise) +
                        Vector3.up * Mathf.Lerp(0.35f, 1.2f, 1f - noise),
                    startSize = Mathf.Lerp(0.45f, 1.35f, noise),
                    startLifetime = Mathf.Lerp(0.45f, 0.9f, 1f - noise)
                };
                particles.Emit(emit, 1);
            }
            particles.Play();
        }

        private void CreateDebris()
        {
            var debrisObject = new GameObject("MeteorDebris");
            debrisObject.transform.SetParent(transform, false);
            debrisObject.transform.localPosition = Vector3.up * 0.18f;
            var particles = debrisObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.55f, radius * 1.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.38f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.2f, 0.16f, 0.12f, 1f),
                new Color(0.5f, 0.31f, 0.14f, 1f));
            main.gravityModifier = 2.6f;
            main.maxParticles = 32;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 68f;
            shape.radius = radius * 0.12f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = GetCubeMesh();
            renderer.material = BootstrapMaterials.Get(new Color(0.28f, 0.2f, 0.13f, 1f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Emit(24);
            particles.Play();
        }

        private void BeginHitStop()
        {
            if (Time.timeScale <= 0f || Time.unscaledTime < nextAllowedHitStopTime)
            {
                return;
            }

            nextAllowedHitStopTime = Time.unscaledTime + 0.18f;
            previousTimeScale = Time.timeScale;
            hitStopRemaining = HitStopDuration;
            hitStopToken = ++hitStopGeneration;
            Time.timeScale = 0f;
        }

        private void Update()
        {
            var delta = Time.unscaledDeltaTime;
            elapsed += delta;
            UpdateHitStop(delta);

            var flashProgress = Mathf.Clamp01(elapsed / 0.2f);
            var flashDiameter = Mathf.Lerp(
                radius * 0.15f,
                radius * 2.15f,
                1f - Mathf.Pow(1f - flashProgress, 3f));
            flashRenderer.transform.localScale = new Vector3(flashDiameter, 0.012f, flashDiameter);
            SetRendererColor(
                flashRenderer,
                new Color(1f, 0.72f, 0.2f, (1f - flashProgress) * 0.8f));

            var craterFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4.5f, VisualLifetime, elapsed));
            SetRendererColor(craterRenderer, new Color(0.12f, 0.09f, 0.07f, craterFade * 0.62f));
            if (elapsed >= VisualLifetime)
            {
                Destroy(gameObject);
            }
        }

        private void UpdateHitStop(float delta)
        {
            if (hitStopRemaining <= 0f)
            {
                return;
            }

            hitStopRemaining = Mathf.Max(0f, hitStopRemaining - delta);
            if (hitStopRemaining <= 0f && hitStopToken == hitStopGeneration && Mathf.Approximately(Time.timeScale, 0f))
            {
                Time.timeScale = Mathf.Max(0.01f, previousTimeScale);
            }
        }

        private void OnDestroy()
        {
            if (hitStopRemaining > 0f && hitStopToken == hitStopGeneration && Mathf.Approximately(Time.timeScale, 0f))
            {
                Time.timeScale = Mathf.Max(0.01f, previousTimeScale);
            }
        }

        private void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(propertyBlock);
        }

        private static Mesh GetCubeMesh()
        {
            if (cubeMesh != null)
            {
                return cubeMesh;
            }

            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            primitive.SetActive(false);
            cubeMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Destroy(primitive);
            return cubeMesh;
        }

        private static void RemoveCollider(GameObject target)
        {
            var components = target.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }
        }
    }
}
