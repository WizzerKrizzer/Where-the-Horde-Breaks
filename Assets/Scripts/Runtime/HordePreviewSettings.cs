namespace TowerDefense.Runtime
{
    // Diagnostics switches kept for crowd tests. No horde preview model exists;
    // contact sizing is purely physical.
    public static class HordePreviewSettings
    {
        public const string PreferenceKey = "TowerDefense.HordePreview";
        public const int DefaultEnabled = 0;
        public const float ContactRadiusPerScale = 1f;

        public static float BodyDiameter(float selfScale, float otherScale) => selfScale + otherScale;
    }
}
