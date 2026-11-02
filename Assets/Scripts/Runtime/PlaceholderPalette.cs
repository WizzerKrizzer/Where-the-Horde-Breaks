using UnityEngine;

namespace TowerDefense.Runtime
{
    // Neutral greybox colours used until the new art style exists. Objects are
    // told apart by shape and size; state (slow, hit, selection) only by value.
    public static class PlaceholderPalette
    {
        public static readonly Color Background = Grey(0.16f);
        public static readonly Color Ground = Grey(0.30f);
        public static readonly Color Road = Grey(0.22f);
        public static readonly Color Enemy = Grey(0.55f);
        public static readonly Color Tower = Grey(0.72f);
        public static readonly Color Accent = Grey(0.92f);
        public static readonly Color Dark = Grey(0.08f);

        public static Color Grey(float value, float alpha = 1f) => new Color(value, value, value, alpha);

        // Collapses any authored colour to its luminance so data tables can keep
        // their per-entry values without carrying a palette.
        public static Color Neutral(Color color)
        {
            var value = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
            return new Color(value, value, value, color.a);
        }
    }
}
