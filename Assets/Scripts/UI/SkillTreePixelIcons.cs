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
        private static readonly Dictionary<string, Texture2D> Cache = new();
        private static readonly Dictionary<string, Texture2D> Silhouettes = new();
        private static readonly Color32 SilhouetteColor = new(24, 20, 18, 255);
        // Three tones (user, 2026-11-12: "the black icons are hard to read, add some detail"): the
        // bright parts of a picture (tusks, flint, flames' cores) cream, its middle tones a shade
        // of the tile, the rest dark. Simple shapes read best as plain silhouettes.
        private static readonly Color32 HighlightColor = new(255, 246, 224, 255);
        private static readonly Color32 ShadeColor = new(0, 0, 0, 140);
        private static readonly HashSet<string> PlainSilhouettes = new() { "health", "fire" };
        private static readonly Dictionary<string, string> Fallbacks = new()
        {
            ["volley"] = "javelin|plus",
            // Stand-ins until they have drawings of their own (2026-11-18).
            ["slingers|plus"] = "slingers",
            ["essence"] = "capacity",
            ["wall"] = "shield",
            ["wall|up"] = "health",
            ["slingers|crew"] = "slingers",
            ["auto"] = "javelin"
        };

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

            // Icons drawn by Gemini (user, 2026-11-12) come first when installed: 128x128 PNGs made
            // by Tools/SkillTree/import_gemini_icons.py, kept local (git-ignored). A "|" in the key
            // is "_" in the file name.
            var drawn = Resources.Load<Texture2D>("Gemini/SkillTreeIcons/" + key.Replace('|', '_'));
            if (drawn != null)
            {
                // Snapped to real pixel art (32x32): kept crisp like the drawn icons.
                if (drawn.width <= 64)
                {
                    drawn.filterMode = FilterMode.Point;
                }

                Cache[key] = drawn;
                texture = drawn;
                return true;
            }


            if (!Data.TryGetValue(key, out var icon))
            {
                // Without them, keys that only Gemini drew show the nearest drawing of our own.
                return Fallbacks.TryGetValue(key, out var fallback) && TryGet(fallback, out texture);
            }

            // 16x16, or 32x32 for the icons drawn bigger (the heavy spear, user 2026-11-12).
            var size = icon.rows.Length;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SkillIcon_" + key,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            for (var row = 0; row < size; row++)
            {
                var line = icon.rows[row];
                for (var column = 0; column < size; column++)
                {
                    var symbol = line[column];
                    // Textures fill from the bottom row up; the strings are written top first.
                    pixels[(size - 1 - row) * size + column] = symbol == '.'
                        ? new Color32(0, 0, 0, 0)
                        : icon.palette[PaletteIndex(symbol)];
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return true;
        }

        /// <summary>
        /// A picture drawn by Gemini for this key, if installed (user, 2026-11-13: the thrown spear,
        /// the volley and the precise strike): shown in its own colours on the tile.
        /// </summary>
        public static bool TryGetGeminiPicture(string key, out Texture2D texture)
        {
            texture = string.IsNullOrWhiteSpace(key)
                ? null
                : Resources.Load<Texture2D>("Gemini/SkillTreeIcons/" + key.Replace('|', '_'));
            if (texture != null)
            {
                // A picture that reaches its edges (the spear's point and butt in the corners) must
                // not wrap round into the opposite corners.
                texture.wrapMode = TextureWrapMode.Clamp;
            }

            return texture != null;
        }

        /// <summary>
        /// The icon as a dark silhouette for a coloured tile (user, 2026-11-12: fewer colours, bolder
        /// shapes, easier to read): every drawn pixel becomes one dark colour, the outline round the
        /// outside included, so the shape is bold; dark pixels inside it (eyes, the cave's mouth, the
        /// lines between parts) become holes the tile shows through. Always from our own drawings,
        /// never Gemini's pictures.
        /// </summary>
        public static bool TryGetSilhouette(string key, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (Silhouettes.TryGetValue(key, out texture) && texture != null)
            {
                return true;
            }

            if (!Data.TryGetValue(key, out var icon))
            {
                return Fallbacks.TryGetValue(key, out var fallback) && TryGetSilhouette(fallback, out texture);
            }

            var size = icon.rows.Length;
            var plain = PlainSilhouettes.Contains(key.Split('|')[0]);
            // Outside: the clear pixels reached from the edges.
            var outside = new bool[size, size];
            var queue = new Queue<(int, int)>();
            for (var i = 0; i < size; i++)
            {
                foreach (var (r, c) in new[] { (0, i), (size - 1, i), (i, 0), (i, size - 1) })
                {
                    if (!outside[r, c] && icon.rows[r][c] == '.')
                    {
                        outside[r, c] = true;
                        queue.Enqueue((r, c));
                    }
                }
            }

            while (queue.Count > 0)
            {
                var (r, c) = queue.Dequeue();
                foreach (var (dr, dc) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var rr = r + dr;
                    var cc = c + dc;
                    if (rr >= 0 && rr < size && cc >= 0 && cc < size && !outside[rr, cc] && icon.rows[rr][cc] == '.')
                    {
                        outside[rr, cc] = true;
                        queue.Enqueue((rr, cc));
                    }
                }
            }

            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SkillSilhouette_" + key,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                {
                    var symbol = icon.rows[row][column];
                    var solid = false;
                    var tone = SilhouetteColor;
                    if (symbol != '.')
                    {
                        var colour = icon.palette[PaletteIndex(symbol)];
                        var luminance = colour.r * 0.3f + colour.g * 0.59f + colour.b * 0.11f;
                        var dark = luminance < 52f;
                        if (!dark)
                        {
                            solid = true;
                            if (!plain)
                            {
                                tone = luminance >= 150f ? HighlightColor : luminance >= 95f ? ShadeColor : SilhouetteColor;
                            }
                        }
                        else
                        {
                            // A dark pixel on the outer edge is the outline: part of the shape.
                            foreach (var (dr, dc) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            {
                                var rr = row + dr;
                                var cc = column + dc;
                                if (rr < 0 || rr >= size || cc < 0 || cc >= size || outside[rr, cc])
                                {
                                    solid = true;
                                    break;
                                }
                            }
                        }
                    }

                    pixels[(size - 1 - row) * size + column] = solid ? tone : new Color32(0, 0, 0, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Silhouettes[key] = texture;
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
