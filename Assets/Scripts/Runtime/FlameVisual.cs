using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// A low-poly flame (StyleLowPolyFlame.shader on LowPolyShapes.Flame, user 2026-11-12): faceted,
    /// yellow at the root to red at the tip, its corners always moving. The flame stands on this
    /// object's position and rises along the world's up whatever the parent does. Used for the
    /// cavemen's torches and the burning rock.
    /// </summary>
    public sealed class FlameVisual : MonoBehaviour
    {
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static Material material;

        /// <summary>The flame's material, shared by every low-poly flame (null without the shader).</summary>
        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = Shader.Find("TowerDefense/StyleLowPolyFlame");
                    if (shader != null)
                    {
                        material = new Material(shader) { name = "LowPolyFlame" };
                    }
                }

                return material;
            }
        }

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

        /// <summary>A flame mesh object for an effect part: sized by its own scale (x width, y height).</summary>
        public static Renderer CreateMesh(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = LowPolyShapes.Flame;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetFloat(SeedId, Random.Range(0f, 100f));
            renderer.SetPropertyBlock(block);
            return renderer;
        }

        private void Build(float height, float width)
        {
            if (Material == null)
            {
                return;
            }

            var layer = CreateMesh(transform, "FlameLayer");
            // The shader reads the flame's size from the object's scale and ignores rotation; the
            // parent's scale is taken out so `height` and `width` are in metres.
            var parentScale = transform.lossyScale;
            layer.transform.localScale = new Vector3(width / Mathf.Max(1e-4f, parentScale.x),
                height / Mathf.Max(1e-4f, parentScale.y), width / Mathf.Max(1e-4f, parentScale.z));
        }
    }
}
