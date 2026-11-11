using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// The cavemen's cave (the Meshy "Primal Cave Dwelling", user 2026-11-11), when installed
    /// (Tools/Animation/install_cave.sh into the git-ignored Resources/Meshy): it stands back from the
    /// tower's spot with its mouth facing the road, and the cavemen come out of its mouth.
    /// </summary>
    public static class CaveVisual
    {
        private const string Folder = "Meshy/";
        // TEMPORARY: the cave's depth on the map (front of its floor to the back of its rocks).
        public const float Depth = 6.5f;
        // The cave's mouth: this share of its depth back from the front of its floor (the fire pit
        // lies before it); and deep inside, under its rocks, where the cavemen come from.
        private const float MouthBack = 0.36f;
        private const float InsideBack = 0.62f;
        // Its front this far out from the tower's spot towards the road, as the mammoth's pen.
        private const float FrontAhead = 0.3f;

        /// <summary>The cave on the map beside the road, or null when the model is not installed.</summary>
        public static GameObject TryCreate(Transform parent, Vector3 spot, Vector3 outward, out Vector3 mouth, out Vector3 inside)
        {
            mouth = spot;
            inside = spot;
            var prefab = Resources.Load<GameObject>(Folder + "cave");
            var shader = Shader.Find("TowerDefense/StyleSkinned");
            if (prefab == null || shader == null)
            {
                return null;
            }

            var material = new Material(shader) { name = "Cave" };
            material.SetTexture("_MainTex", Resources.Load<Texture2D>(Folder + "cave-tex"));
            var pivot = new GameObject("Cave");
            pivot.transform.SetParent(parent, false);
            var cave = Object.Instantiate(prefab, pivot.transform, false);
            var bounds = new Bounds();
            var first = true;
            foreach (var renderer in cave.GetComponentsInChildren<Renderer>())
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

            // The model's middle over the pivot and its floor on the ground. Its mouth faces +Z.
            cave.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            var scale = Depth / Mathf.Max(0.01f, bounds.size.z);
            pivot.transform.localScale = Vector3.one * scale;
            outward.y = 0f;
            var facing = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
            var front = spot + facing * FrontAhead;
            pivot.transform.SetPositionAndRotation(front - facing * Depth * 0.5f, Quaternion.LookRotation(facing, Vector3.up));
            mouth = front - facing * Depth * MouthBack;
            mouth.y = spot.y;
            inside = front - facing * Depth * InsideBack;
            inside.y = spot.y;
            return pivot;
        }
    }
}
