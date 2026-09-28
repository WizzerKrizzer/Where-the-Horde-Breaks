using System;
using UnityEngine;

namespace TowerDefense.Runtime
{
    // Compatibility surface for old diagnostics. The FBX/VAT preview was removed:
    // it was not a viable visual for the 10,000-unit production horde.
    public sealed class GoblinPreviewAssets : IDisposable
    {
        public const string PreferenceKey = "TowerDefense.GoblinPreview";
        public const int DefaultEnabled = 0;
        public const int VariantCount = 0;
        public const float ContactRadiusPerScale = 1f;
        public const float ContactSkin = 1f;
        public const float RunningStride = 0f;
        public const float BracePhase = 0f;
        public const float MovementSpeedMultiplier = 1f;

        public static bool IsEnabled => false;
        public Mesh Mesh => null;
        public Mesh LodMesh => null;

        public static float BodyDiameter(float selfScale, float otherScale) => selfScale + otherScale;
        public Mesh GetMesh(int index) => null;
        public static GoblinPreviewAssets TryLoad() => null;
        public void Dispose() { }
    }
}
