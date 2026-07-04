namespace TowerDefense.Data
{
    public enum CurrencyType
    {
        KillEssence,
        VictorySigil,
        PerfectSigil,
        ChallengeToken,
        BossCore
    }

    public enum EnemyRole
    {
        Runner,
        Heavy,
        Support,
        Saboteur,
        Ranged,
        Boss
    }

    public enum TowerRole
    {
        ArcherLine,
        ArtilleryLine,
        ControlLine
    }

    public enum ProjectilePattern
    {
        Direct,
        ArcSplash
    }

    public enum UpgradeEffectType
    {
        ActiveWeaponDamagePercent,
        ActiveWeaponCooldownPercent,
        ActiveWeaponRadiusFlat,
        ActiveWeaponPierceFlat,
        PerTypeTowerLimitFlat,
        TowerDamagePercent,
        EnableTowerFire,
        TowerFireDamagePerTickFlat,
        TowerFireTicksPerSecondFlat,
        TowerFireMaxStacksFlat,
        TowerFireDurationFlat,
        BaseLivesFlat,
        UnlockTower,
        UnlockEra
    }
}
