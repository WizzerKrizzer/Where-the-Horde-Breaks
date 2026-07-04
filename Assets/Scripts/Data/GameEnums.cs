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
        Flying,
        Undead,
        Boss
    }

    public enum TowerRole
    {
        ArcherLine,
        ArtilleryLine,
        ControlLine,
        BarrierLine,
        BarracksLine
    }

    public enum TowerBehavior
    {
        Projectile,
        SlowAura,
        Barrier,
        Barracks
    }

    public enum AlliedUnitType
    {
        Knight,
        Archer,
        Paladin
    }

    public enum CombatTargetKind
    {
        Barrier,
        AlliedUnit
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
        TowerFireRatePercent,
        TowerPierceFlat,
        TowerDoubleShotChancePercent,
        TowerSlowPercentFlat,
        TowerSlowCapacityFlat,
        TowerRangeFlat,
        TowerHealthFlat,
        TowerThornsDamageFlat,
        BarracksUnitCapacityFlat,
        BarracksUnitDamagePercent,
        BarracksUnitHealthPercent,
        BarracksRespawnCooldownPercent,
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
