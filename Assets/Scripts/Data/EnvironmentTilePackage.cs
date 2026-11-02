namespace TowerDefense.Data
{
    public enum EnvironmentTilePackageId
    {
        Prehistoric,
        WeatheredRoad,
        Arena
    }

    /// <summary>
    /// Shared road rules for a family of levels. Kerb geometry depends on it.
    /// </summary>
    public sealed class EnvironmentTilePackage
    {
        public EnvironmentTilePackageId Id { get; }
        public string Name { get; }
        public bool UsesRoadMarkings { get; }

        internal EnvironmentTilePackage(
            EnvironmentTilePackageId id,
            string name,
            bool usesRoadMarkings)
        {
            Id = id;
            Name = name;
            UsesRoadMarkings = usesRoadMarkings;
        }
    }

    public static class EnvironmentTilePackages
    {
        private static readonly EnvironmentTilePackage Prehistoric = new(
            EnvironmentTilePackageId.Prehistoric,
            "Prehistoric Wilds",
            false);

        private static readonly EnvironmentTilePackage WeatheredRoad = new(
            EnvironmentTilePackageId.WeatheredRoad,
            "Weathered Road",
            true);

        private static readonly EnvironmentTilePackage Arena = new(
            EnvironmentTilePackageId.Arena,
            "Arena",
            false);

        public static EnvironmentTilePackage Resolve(EnvironmentTilePackageId id) => id switch
        {
            EnvironmentTilePackageId.WeatheredRoad => WeatheredRoad,
            EnvironmentTilePackageId.Arena => Arena,
            _ => Prehistoric
        };
    }
}
