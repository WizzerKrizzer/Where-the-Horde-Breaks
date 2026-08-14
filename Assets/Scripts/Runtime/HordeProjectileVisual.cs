using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class HordeProjectileVisual : MonoBehaviour
    {
        private Vector3 start;
        private Vector3 end;
        private float duration;
        private float elapsed;
        private bool arc;

        public static void Spawn(Vector3 from, Vector3 to, Color color, bool arcProjectile)
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
            visual.Initialize(from, to, arcProjectile);
        }

        private void Initialize(Vector3 from, Vector3 to, bool arcProjectile)
        {
            start = from;
            end = to;
            arc = arcProjectile;
            duration = arc ? 0.42f : 0.16f;
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
                Destroy(gameObject);
            }
        }
    }
}
