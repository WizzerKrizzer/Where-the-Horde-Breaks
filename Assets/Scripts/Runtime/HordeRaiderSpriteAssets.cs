using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// GPU horde art is a flat, top-down card rather than a tiny animated humanoid.
    /// Keeping the silhouette on one quad makes ten thousand bodies readable and keeps
    /// the visual tied to the authoritative GPU motion instead of a separate Animator.
    /// </summary>
    internal static class HordeRaiderSpriteAssets
    {
        internal const string TextureResourcePath = "Npcs/HordeRaiderSpriteSheet";
        // The painted body fills roughly two thirds of its card. Collision must
        // describe that painted silhouette, not the transparent atlas padding.
        internal const float ContactRadiusPerStateScale = 0.62f;
        internal const float MinimumContactDiameterMultiplier = 0.70f;
        // Radius from the card centre to a painted card corner. Navigation
        // boundaries need this value, not the smaller circular body contact.
        internal const float GroundCardCornerRadius = 1.35f;

        internal static Mesh CreateGroundCard()
        {
            var mesh = new Mesh
            {
                name = "HordeRaiderGroundCard",
                hideFlags = HideFlags.DontSave,
            };

            // The art is painted for the game camera, so its plane lies on the road.
            // It intentionally has no upright limbs or view-dependent billboard logic.
            mesh.vertices = new[]
            {
                new Vector3(-0.86f, 0f, -1.04f),
                new Vector3( 0.86f, 0f, -1.04f),
                new Vector3( 0.86f, 0f,  1.04f),
                new Vector3(-0.86f, 0f,  1.04f),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            // X × Z points down in Unity's left-handed world. Keep the winding
            // upward so the normal and the visible face agree under backface culling.
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f, 0.1f, 2.4f));
            mesh.UploadMeshData(false);
            return mesh;
        }
    }
}
