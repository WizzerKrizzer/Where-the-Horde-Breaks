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
        Paladin,
        // Prehistoric cavemen with clubs, from the Cave.
        Caveman
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
        HighestHealth,
        // The heaviest goblin in range first (the boss, then brutes); among equals, the one furthest along the road.
        Heaviest
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
        UnlockEra,
        // The tower's beast also pushes and throws heavy enemies (the mammoth and Orc Brutes).
        TowerPushesHeavy,
        // The rock rolls this many metres further.
        ActiveWeaponRollDistanceFlat,
        // The fire rock: it leaves a burning trail where it rolls.
        ActiveWeaponFireTrailUnlock,
        // Slinger specials (user, 2026-11-08). The special itself grants the first amount and
        // Kill Essence upgrades add to it. Stun: chance per stone (%) and how long (s).
        TowerStunChancePercent,
        TowerStunSecondsFlat,
        // Ricochet: how many more goblins the stone bounces on to, and the share of the hit each
        // bounce deals (%).
        TowerRicochetBouncesFlat,
        TowerRicochetDamagePercent,
        // Shrapnel: the stone bursts into pebbles that hit this many goblins in a cone ahead, each
        // for this share of the hit (%).
        TowerShrapnelTargetsFlat,
        TowerShrapnelDamagePercent,
        // Rock specials. Fire trail: more burn damage per tick (%) and more ticks per second.
        ActiveWeaponFireDamagePercent,
        ActiveWeaponFireTicksPerSecondFlat,
        // Shatter: at the end of its roll the rock bursts into this many shards, each hitting for
        // this share of the rock's damage (%).
        ActiveWeaponShatterShardsFlat,
        ActiveWeaponShatterDamagePercent,
        // Quake: where the rock lands, goblins in this radius are stunned for this long.
        ActiveWeaponQuakeRadiusFlat,
        ActiveWeaponQuakeSecondsFlat,
        // Spear Thrower specials. Pierce falloff: each goblin a spear passes through takes this
        // much less than the one before (%); Kill Essence upgrades lower it with negative values.
        TowerPierceFalloffPercent,
        // Big-game hunters: extra damage (%) against heavy enemies (Orc Brutes, the boss).
        TowerHeavyDamagePercent,
        // Poisoned tips: poison damage per second and how long it lasts.
        TowerPoisonDamagePerSecondFlat,
        TowerPoisonSecondsFlat,
        // Mammoth specials. Armour: extra health lost before its own; crowd: how much less a
        // crowd slows the charge (%).
        TowerBeastArmorFlat,
        TowerBeastCrowdPercent,
        // Slingers on its back: how many ride, and the damage of each stone.
        TowerBeastRidersFlat,
        TowerBeastRiderDamageFlat,
        // Rage: how much faster and harder it stomps (%), once its health falls below the
        // threshold (% of its health).
        TowerBeastRagePercent,
        TowerBeastRageThresholdPercent,
        // Cave specials. Shields: less damage taken (%); hide armour: flat damage off each blow.
        TowerUnitDamageReductionPercent,
        TowerUnitArmorFlat,
        // The chief: extra damage (%) for the cavemen near him, and his extra health (%).
        TowerChiefAuraPercent,
        TowerChiefHealthPercent,
        // Torches: the cavemen's blows set goblins burning, this much per second for this long.
        TowerTorchBurnDamagePerSecondFlat,
        TowerTorchBurnSecondsFlat
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
