using System;
using UnityEngine;

namespace TowerDefense.Data
{
    public enum EnvironmentTilePackageId
    {
        Prehistoric,
        WeatheredRoad,
        Arena
    }

    [Flags]
    public enum EnvironmentTileKind
    {
        None = 0,
        Trees = 1 << 0,
        Huts = 1 << 1,
        Fires = 1 << 2,
        Rivers = 1 << 3,
        StandingStones = 1 << 4
    }

    /// <summary>
    /// Shared visual rules and reusable tile types for a family of levels.
    /// Level layouts only place tiles; materials and road language live here.
    /// </summary>
    public sealed class EnvironmentTilePackage
    {
        public EnvironmentTilePackageId Id { get; }
        public string Name { get; }
        public bool UsesRoadMarkings { get; }
        public EnvironmentTileKind Tiles { get; }
        public Color GroundColour { get; }

        internal EnvironmentTilePackage(
            EnvironmentTilePackageId id,
            string name,
            bool usesRoadMarkings,
            EnvironmentTileKind tiles,
            Color groundColour)
        {
            Id = id;
            Name = name;
            UsesRoadMarkings = usesRoadMarkings;
            Tiles = tiles;
            GroundColour = groundColour;
        }

        public bool Contains(EnvironmentTileKind kind) => (Tiles & kind) == kind;
    }

    public static class EnvironmentTilePackages
    {
        private static readonly EnvironmentTilePackage Prehistoric = new(
            EnvironmentTilePackageId.Prehistoric,
            "Prehistoric Wilds",
            false,
            EnvironmentTileKind.Trees | EnvironmentTileKind.Huts |
            EnvironmentTileKind.Fires | EnvironmentTileKind.Rivers |
            EnvironmentTileKind.StandingStones,
            new Color(0.27f, 0.32f, 0.19f));

        private static readonly EnvironmentTilePackage WeatheredRoad = new(
            EnvironmentTilePackageId.WeatheredRoad,
            "Weathered Road",
            true,
            EnvironmentTileKind.None,
            new Color(0.29f, 0.36f, 0.25f));

        private static readonly EnvironmentTilePackage Arena = new(
            EnvironmentTilePackageId.Arena,
            "Arena",
            false,
            EnvironmentTileKind.None,
            new Color(0.29f, 0.36f, 0.25f));

        public static EnvironmentTilePackage Resolve(EnvironmentTilePackageId id) => id switch
        {
            EnvironmentTilePackageId.WeatheredRoad => WeatheredRoad,
            EnvironmentTilePackageId.Arena => Arena,
            _ => Prehistoric
        };
    }
}
