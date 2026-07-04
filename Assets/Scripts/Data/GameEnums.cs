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

    public enum UpgradeEffectType
    {
        ActiveWeaponDamagePercent,
        ActiveWeaponCooldownPercent,
        PerTypeTowerLimitFlat,
        TowerDamagePercent,
        BaseLivesFlat,
        UnlockTower,
        UnlockEra
    }
}
