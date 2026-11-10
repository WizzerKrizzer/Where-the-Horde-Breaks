using System;
using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// How much of the decorative effects the player wants (user, 2026-11-09: in a chaotic fight
    /// all the dust and stars could get confusing). One setting with three steps, kept on this
    /// machine, not in the save. Effects that carry information (where a fire burns, who is
    /// stunned, a big-game hit) keep a trace even at Minimal.
    /// </summary>
    public static class EffectsQuality
    {
        public enum Level
        {
            Full = 0,
            Reduced = 1,
            Minimal = 2
        }

        private const string PreferenceKey = "effects_quality";
        private static Level? current;

        public static event Action Changed;

        public static Level Current
        {
            get
            {
                if (current == null)
                {
                    var stored = 0;
                    try
                    {
                        stored = PlayerPrefs.GetInt(PreferenceKey, 0);
                    }
                    catch (Exception)
                    {
                        // Preferences unavailable (some test runners): full effects.
                    }

                    current = (Level)Mathf.Clamp(stored, 0, 2);
                }

                return current.Value;
            }
        }

        public static void Set(Level level)
        {
            current = level;
            try
            {
                PlayerPrefs.SetInt(PreferenceKey, (int)level);
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // Kept for this run only.
            }

            Changed?.Invoke();
        }

        public static Level Next(Level level)
        {
            return (Level)(((int)level + 1) % 3);
        }

        public static string Label(Level level)
        {
            return level switch
            {
                Level.Reduced => "REDUCED",
                Level.Minimal => "MINIMAL",
                _ => "FULL"
            };
        }

        /// <summary>How many pieces of a decorative effect to make: all, about half, or none.</summary>
        public static int Pieces(int full, bool keepOneAtMinimal = false)
        {
            return Current switch
            {
                Level.Reduced => Mathf.Max(1, Mathf.RoundToInt(full * 0.5f)),
                Level.Minimal => keepOneAtMinimal ? Mathf.Min(full, 1) : 0,
                _ => full
            };
        }

        /// <summary>At most this many stunned goblins show stars at once; the rest are tinted only.</summary>
        public static int StunStarCap => Current switch
        {
            Level.Reduced => 12,
            Level.Minimal => 0,
            _ => 40
        };
    }
}
