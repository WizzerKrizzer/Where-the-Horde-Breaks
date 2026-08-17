using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class HordeProjectileVisual : MonoBehaviour
    {
        private Vector3 start;
        private Vector3 end;
        private float duration;
        private float impactRadius;
        private float elapsed;
        private bool arc;
        private Color impactColor;

        public static void Spawn(Vector3 from, Vector3 to, Color color, bool arcProjectile)
        {
            Spawn(from, to, color, arcProjectile, arcProjectile ? 0.42f : 0.16f, arcProjectile ? 0.95f : 0.16f);
        }

        public static void Spawn(Vector3 from, Vector3 to, Color color, bool arcProjectile, float travelDuration, float markerRadius)
        {
            var go = GameObject.CreatePrimitive(arcProjectile ? PrimitiveType.Sphere : PrimitiveType.Capsule);
            go.name = arcProjectile ? "HordeBoulderVisual" : "HordeProjectileVisual";
            go.transform.position = from;
            go.transform.localScale = arcProjectile ? Vector3.one * 0.34f : new Vector3(0.08f, 0.08f, 0.34f);
            go.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(color);
            var components = go.GetComponents<Component>();
            for (var i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }

            var visual = go.AddComponent<HordeProjectileVisual>();
            visual.Initialize(from, to, color, arcProjectile, travelDuration, markerRadius);
        }

        private void Initialize(Vector3 from, Vector3 to, Color color, bool arcProjectile, float travelDuration, float markerRadius)
        {
            start = from;
            end = to;
            arc = arcProjectile;
            impactColor = color;
            duration = Mathf.Max(0.04f, travelDuration);
            impactRadius = Mathf.Max(0.05f, markerRadius);
            elapsed = 0f;
            transform.position = start;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            var position = Vector3.Lerp(start, end, t);
            if (arc)
            {
                position.y += Mathf.Sin(t * Mathf.PI) * 2.1f;
            }

            transform.position = position;
            if (!arc)
            {
                var direction = end - start;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }

            if (t >= 1f)
            {
                SpawnImpactMarker(end, impactColor, arc, impactRadius);
                Destroy(gameObject);
            }
        }

        private static void SpawnImpactMarker(Vector3 position, Color color, bool arcProjectile, float radius)
        {
            var marker = GameObject.CreatePrimitive(arcProjectile ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
            marker.name = arcProjectile ? "HordeBoulderImpactMarker" : "HordeProjectileImpactMarker";
            marker.transform.position = position + Vector3.up * 0.035f;
            marker.transform.localScale = arcProjectile ? new Vector3(radius * 2f, 0.025f, radius * 2f) : Vector3.one * radius;
            var impactColor = arcProjectile
                ? new Color(1f, 0.22f, 0.08f, 0.86f)
                : new Color(color.r, color.g, color.b, 0.75f);
            marker.GetComponent<Renderer>().sharedMaterial = BootstrapMaterials.Get(impactColor);
            var components = marker.GetComponents<Component>();
            for (var i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                }
            }

            Destroy(marker, arcProjectile ? 0.55f : 0.1f);
        }
    }
}
