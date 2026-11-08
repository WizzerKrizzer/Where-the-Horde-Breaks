using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.UI
{
    /// <summary>
    /// The skill tree's pixel icons (user, 2026-11-08): each node shows a small picture of what it
    /// does instead of its name. An icon key is a base picture ("stun") with an optional corner
    /// badge for the Kill Essence upgrades of a special ("stun|up": stronger, "|time": longer,
    /// "|plus": more, "|fast": faster or sooner, "|heart": more health). The pictures are 16x16
    /// palette strings in SkillTreePixelIcons.Data.cs, made by a script, and become point-filtered
    /// textures here so they stay crisp at any zoom.
    /// </summary>
    internal static partial class SkillTreePixelIcons
    {
        private const int Size = 16;
        private static readonly Dictionary<string, Texture2D> Cache = new();

        public static bool TryGet(string key, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (Cache.TryGetValue(key, out texture) && texture != null)
            {
                return true;
            }

            if (!Data.TryGetValue(key, out var icon))
            {
                return false;
            }

            texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "SkillIcon_" + key,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[Size * Size];
            for (var row = 0; row < Size; row++)
            {
                var line = icon.rows[row];
                for (var column = 0; column < Size; column++)
                {
                    var symbol = line[column];
                    // Textures fill from the bottom row up; the strings are written top first.
                    pixels[(Size - 1 - row) * Size + column] = symbol == '.'
                        ? new Color32(0, 0, 0, 0)
                        : icon.palette[PaletteIndex(symbol)];
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return true;
        }

        private static int PaletteIndex(char symbol)
        {
            if (symbol >= 'a' && symbol <= 'z') return symbol - 'a';
            if (symbol >= 'A' && symbol <= 'Z') return 26 + symbol - 'A';
            return 52 + symbol - '0';
        }
    }
}
