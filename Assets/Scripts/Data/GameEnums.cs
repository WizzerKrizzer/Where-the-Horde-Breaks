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
        Barracks,
        // A beast that leaves its pen and charges along the road, knocking enemies aside.
        Charge
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

    public enum TowerTargetingMode
    {
        First,
        Last,
        Closest,
        HighestHealth
    }

    public enum UpgradeEffectType
    {
        ActiveWeaponDamagePercent,
        ActiveWeaponCooldownPercent,
        ActiveWeaponRadiusFlat,
        ActiveWeaponPierceFlat,
        ActiveWeaponAutoFireUnlock,
        PerTypeTowerLimitFlat,
        TowerDamageFlat,
        TowerDamagePercent,
        TowerFireRateFlat,
        TowerFireRatePercent,
        TowerProjectileSpeedPercent,
        TowerAimAssistPercent,
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
        LevelEndKillEssenceFlat,
        UnlockTower,
        UnlockEra
    }

    /// <summary>The six eras of the campaign. TowerDefinition.eraIndex stores the value.</summary>
    public enum EraId
    {
        Prehistoric = 0,
        Medieval = 1,
        Gunpowder = 2,
        Industrial = 3,
        Electric = 4,
        Future = 5
    }
}
