namespace TowerDefense.Runtime
{
    /// <summary>
    /// Contact telemetry deliberately separates normal touching from meaningful
    /// failure. A dense crowd has many shallow contacts; that alone is not an
    /// overlap regression.
    /// </summary>
    public readonly struct CrowdSolverDiagnostics
    {
        public readonly uint ShallowContactPairs;
        public readonly uint DeepOverlapPairs;
        public readonly uint PersistentDeepContactBodyCount;
        public readonly float MaximumPenetrationFraction;
        public readonly float DeepContactDurationSeconds;
        public readonly uint ActiveConstraints;
        public readonly uint MaximumCellOccupancy;
        public readonly uint GridCapacityFailures;
        public readonly uint DeepDiskContactPairs;
        public readonly uint DeepBlockerContacts;
        public readonly uint DeepWallContacts;

        public CrowdSolverDiagnostics(
            uint shallowContactPairs,
            uint deepOverlapPairs,
            uint persistentDeepContactBodyCount,
            float maximumPenetrationFraction,
            float deepContactDurationSeconds,
            uint activeConstraints,
            uint maximumCellOccupancy,
            uint gridCapacityFailures,
            uint deepDiskContactPairs,
            uint deepBlockerContacts,
            uint deepWallContacts)
        {
            ShallowContactPairs = shallowContactPairs;
            DeepOverlapPairs = deepOverlapPairs;
            PersistentDeepContactBodyCount = persistentDeepContactBodyCount;
            MaximumPenetrationFraction = maximumPenetrationFraction;
            DeepContactDurationSeconds = deepContactDurationSeconds;
            ActiveConstraints = activeConstraints;
            MaximumCellOccupancy = maximumCellOccupancy;
            GridCapacityFailures = gridCapacityFailures;
            DeepDiskContactPairs = deepDiskContactPairs;
            DeepBlockerContacts = deepBlockerContacts;
            DeepWallContacts = deepWallContacts;
        }
    }
}
