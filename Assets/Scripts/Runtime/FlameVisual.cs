using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// A flame on a few camera-facing quads (StyleFlame.shader): a broad outer flame and a narrower
    /// one inside it, each with its own flicker. The flame stands on this object's position and rises
    /// along the world's up whatever the parent does. Used for the cavemen's torches; the rock's fire
    /// will use the same shader.
    /// </summary>
    public sealed class FlameVisual : MonoBehaviour
    {
        private static Material material;

        /// <summary>Adds a flame of the given height and width under `parent`, at `localPosition`.</summary>
        public static FlameVisual Attach(Transform parent, Vector3 localPosition, float height, float width)
        {
            var root = new GameObject("Flame");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            var flame = root.AddComponent<FlameVisual>();
            flame.Build(height, width);
            return flame;
        }

        private void Build(float height, float width)
        {
            var shader = Shader.Find("TowerDefense/StyleFlame");
            if (shader == null)
            {
                return;
            }

            material ??= new Material(shader) { name = "Flame" };
            var seed = Random.value;
            foreach (var (scale, strength, offset) in new[] { (1f, 0.95f, 0f), (0.62f, 1f, 0.37f) })
            {
                var layer = new GameObject("FlameLayer");
                layer.transform.SetParent(transform, false);
                layer.AddComponent<MeshFilter>().sharedMesh = Quad(Mathf.Repeat(seed + offset, 1f), strength);
                var renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                // The shader reads the flame's size from the object's scale and ignores rotation;
                // the parent's scale is taken out so `height` and `width` are in metres.
                var parentScale = transform.lossyScale;
                layer.transform.localScale = new Vector3(width * scale / Mathf.Max(1e-4f, parentScale.x),
                    height * scale / Mathf.Max(1e-4f, parentScale.y), 1f);
            }
        }

        // A unit quad standing on its bottom edge, its seed and strength in the vertex colour. Each
        // flame gets its own copy so the seeds differ.
        private static Mesh Quad(float seed, float strength)
        {
            var mesh = new Mesh { name = "FlameQuad" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            var color = new Color(seed, 0f, 0f, strength);
            mesh.colors = new[] { color, color, color, color };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // Bounds generous enough for the camera-facing turn.
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 2f, 2f));
            return mesh;
        }
    }
}
