using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Input;
using TowerDefense.Progression;
using TowerDefense.Rewards;
using TowerDefense.Save;
using TowerDefense.Simulation;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class GameSession : MonoBehaviour
    {
        private readonly RewardService rewards = new();
        private ProfileStore profileStore;
        private PlayerProfile profile;
        private LevelDefinition level;
        private SkillTreeDefinition skillTree;
        private ProgressionService progression;
        private EnemyManager enemies;
        private TowerManager towers;
        private ActiveWeaponController activeWeapon;
        private WorldPopupManager popups;
        private PlayerInputRouter input;
        private PathRoute path;
        private IReadOnlyList<TowerDefinition> allTowerDefinitions;
        private readonly Dictionary<string, TowerBaseStats> baseTowerStats = new();
        private int lives;
        private int maxLivesForRun;
        private int enemiesKilled;
        private float baseActiveWeaponDamage;
        private float baseActiveWeaponCooldown;
        private float baseActiveWeaponRadius;
        private int baseActiveWeaponMaxTargets;
        private bool running;
        private bool finished;
        private bool won;

        public PlayerProfile Profile => profile;
        public LevelDefinition Level => level;
        public int Lives => lives;
        public int EnemiesKilled => enemiesKilled;
        public bool IsPlanning => !running && !finished;
        public bool IsRunning => running;
        public bool Finished => finished;
        public bool Won => finished && won;
        public IReadOnlyList<SkillNodeDefinition> UpgradeNodes => progression.GetNodes();
        public IReadOnlyList<TowerDefinition> AllTowerDefinitions => allTowerDefinitions;
        public IReadOnlyList<TowerDefinition> UnlockedTowerDefinitions => towers?.AvailableTowers ?? System.Array.Empty<TowerDefinition>();
        public float BaseActiveWeaponDamage => baseActiveWeaponDamage;
        public float BaseActiveWeaponCooldown => baseActiveWeaponCooldown;
        public float BaseActiveWeaponRadius => baseActiveWeaponRadius;
        public int BaseActiveWeaponMaxTargets => baseActiveWeaponMaxTargets;

        public IReadOnlyList<EnemyDefinition> GetDebugSpawnableEnemies()
        {
            var result = new List<EnemyDefinition>();
            var entries = level?.wave?.entries;
            if (entries == null)
            {
                return result;
            }

            foreach (var entry in entries)
            {
                if (entry.enemy != null && !result.Contains(entry.enemy))
                {
                    result.Add(entry.enemy);
                }
            }

            return result;
        }

        public void SpawnDebugEnemy(EnemyDefinition enemyDefinition)
        {
            enemies?.SpawnDebug(enemyDefinition, path);
        }

        public bool HasEncounteredEnemy(EnemyDefinition enemyDefinition)
        {
            EnsureEncounteredEnemyList();
            return enemyDefinition != null && profile.encounteredEnemyIds.Contains(enemyDefinition.id);
        }

        public void AddCurrency(CurrencyType currency, int amount)
        {
            profile.AddCurrency(currency, amount);
            profileStore.Save(profile);
        }

        public void ClearCurrencies()
        {
            profile.ClearCurrencies();
            profileStore.Save(profile);
        }

        public void ClearLevelRewardProgress()
        {
            profile.ClearLevelRewardProgress();
            profileStore.Save(profile);
        }

        public void RefundAndResetUpgrades()
        {
            progression.RefundAndResetPurchasedUpgrades();
            profileStore.Save(profile);
            ResetToPlanning();
        }

        public void SaveDevSnapshot(int slot)
        {
            SaveLayout();
            profileStore.SaveDevSnapshot(profile, slot);
        }

        public bool HasDevSnapshot(int slot)
        {
            return profileStore.HasDevSnapshot(slot);
        }

        public bool TryLoadDevSnapshot(int slot)
        {
            if (!profileStore.TryLoadDevSnapshot(slot, out var loadedProfile))
            {
                return false;
            }

            profile = loadedProfile;
            progression = new ProgressionService(skillTree, profile);
            profileStore.Save(profile);
            ResetToPlanning();
            return true;
        }

        public bool IsUpgradePurchased(string nodeId)
        {
            return progression.IsPurchased(nodeId);
        }

        public int GetUpgradeRank(string nodeId)
        {
            return progression.GetPurchasedRank(nodeId);
        }

        public int GetUpgradeMaxRank(string nodeId)
        {
            return progression.GetMaxRank(nodeId);
        }

        public float GetUpgradeEffectTotal(UpgradeEffectType type, string targetId = null)
        {
            return progression.GetEffectTotal(type, targetId);
        }

        public TowerDefinition GetTowerDefinition(string towerId)
        {
            if (allTowerDefinitions == null || string.IsNullOrEmpty(towerId))
            {
                return null;
            }

            for (var i = 0; i < allTowerDefinitions.Count; i++)
            {
                if (allTowerDefinitions[i] != null && allTowerDefinitions[i].id == towerId)
                {
                    return allTowerDefinitions[i];
                }
            }

            return null;
        }

        public CurrencyAmount[] GetUpgradeNextCosts(string nodeId)
        {
            return progression.GetCurrentCosts(nodeId);
        }

        public bool CanPurchaseUpgrade(string nodeId)
        {
            return progression.CanPurchase(nodeId);
        }

        public bool TryPurchaseUpgrade(string nodeId)
        {
            var purchased = progression.TryPurchase(nodeId);
            if (purchased)
            {
                ApplyProgressionStats();
                profileStore.Save(profile);
                ResetToPlanning();
            }

            return purchased;
        }

        public void Initialize(
            LevelDefinition levelDefinition,
            SkillTreeDefinition skillTree,
            PathRoute path,
            IReadOnlyList<TowerDefinition> availableTowers,
            EnemyManager enemyManager,
            TowerManager towerManager,
            ActiveWeaponController activeWeaponController,
            WorldPopupManager popupManager,
            PlayerInputRouter inputRouter)
        {
            level = levelDefinition;
            this.skillTree = skillTree;
            profileStore = new ProfileStore();
            profile = profileStore.LoadOrCreate();
            progression = new ProgressionService(skillTree, profile);
            enemies = enemyManager;
            towers = towerManager;
            activeWeapon = activeWeaponController;
            popups = popupManager;
            input = inputRouter;
            this.path = path;
            allTowerDefinitions = availableTowers;
            CaptureBaseTowerStats();
            baseActiveWeaponDamage = activeWeapon.Damage;
            baseActiveWeaponCooldown = activeWeapon.CooldownSeconds;
            baseActiveWeaponRadius = activeWeapon.Radius;
            baseActiveWeaponMaxTargets = activeWeapon.MaxTargets;

            enemiesKilled = 0;
            running = false;
            finished = false;
            won = false;
            activeWeapon.CanFire = false;

            enemies.EnemyKilled += OnEnemyKilled;
            enemies.EnemyEscaped += OnEnemyEscaped;
            enemies.EnemySpawned += OnEnemySpawned;
            towers.Initialize(enemies, path, GetUnlockedTowers());
            ApplyProgressionStats();
            lives = maxLivesForRun;
            towers.LoadLayout(profile.GetOrCreateLayout(level.id).placements);
        }

        private void Update()
        {
            if (input == null)
            {
                return;
            }

            var state = input.Current;
            if (state.RestartLevel)
            {
                ResetToPlanning();
                return;
            }

            if (finished)
            {
                return;
            }

            if (IsPlanning)
            {
                if (state.StartLevel)
                {
                    StartLevel();
                    return;
                }

                if (state.PlaceTower && towers.AvailableTowers.Count > state.SelectedTowerIndex)
                {
                    var selectedTower = towers.AvailableTowers[state.SelectedTowerIndex];
                    var blockReason = towers.GetPlacementBlockReason(selectedTower, state.PointerWorld);
                    if (string.IsNullOrEmpty(blockReason) && towers.TryPlace(selectedTower, state.PointerWorld))
                    {
                        SaveLayout();
                    }
                    else
                    {
                        popups?.Show(blockReason, state.PointerWorld);
                    }
                }

                if (state.RemoveTower && towers.RemoveNearest(state.PointerWorld))
                {
                    SaveLayout();
                }

                if (state.RemoveAllTowers)
                {
                    towers.RemoveAll();
                    SaveLayout();
                }

                return;
            }

            if (state.PlaceTower)
            {
                towers.TrySelectNearest(state.PointerWorld);
                return;
            }

            if (lives <= 0)
            {
                Finish(false);
            }
            else if (enemies.IsWaveComplete)
            {
                Finish(true);
            }
        }

        public void StartLevel()
        {
            if (!IsPlanning)
            {
                return;
            }

            SaveLayout();
            enemiesKilled = 0;
            activeWeapon.ResetRunStats();
            running = true;
            activeWeapon.CanFire = true;
            enemies.BeginWave(level.wave, path);
        }

        public void ResetToPlanning()
        {
            enemies.EnemyKilled -= OnEnemyKilled;
            enemies.EnemyEscaped -= OnEnemyEscaped;
            enemies.StopWave();
            ApplyProgressionStats();
            towers.LoadLayout(profile.GetOrCreateLayout(level.id).placements);
            lives = maxLivesForRun;
            enemiesKilled = 0;
            running = false;
            finished = false;
            won = false;
            activeWeapon.CanFire = false;
            enemies.EnemyKilled += OnEnemyKilled;
            enemies.EnemyEscaped += OnEnemyEscaped;
        }

        private void Finish(bool won)
        {
            finished = true;
            running = false;
            this.won = won;
            activeWeapon.CanFire = false;
            enemies.StopWave();
            rewards.ApplyLevelRewards(profile, level, won, won && lives == maxLivesForRun);
            var levelEndEssenceBonus = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.LevelEndKillEssenceFlat));
            if (levelEndEssenceBonus > 0)
            {
                profile.AddCurrency(CurrencyType.KillEssence, levelEndEssenceBonus);
            }

            SaveLayout();
            profileStore.Save(profile);
        }

        private void OnEnemyKilled(EnemyActor enemy)
        {
            enemiesKilled++;
            if (enemiesKilled % 10 == 0)
            {
                profile.AddCurrency(CurrencyType.KillEssence, 1);
                profileStore.Save(profile);
            }
        }

        private void OnEnemyEscaped(EnemyActor enemy)
        {
            lives -= enemy.Definition.lifeDamage;
        }

        private void OnEnemySpawned(EnemyDefinition enemy)
        {
            EnsureEncounteredEnemyList();
            if (enemy == null || profile.encounteredEnemyIds.Contains(enemy.id))
            {
                return;
            }

            profile.encounteredEnemyIds.Add(enemy.id);
            profileStore.Save(profile);
        }

        private void EnsureEncounteredEnemyList()
        {
            if (profile.encounteredEnemyIds == null)
            {
                profile.encounteredEnemyIds = new List<string>();
            }
        }

        private void SaveLayout()
        {
            var layout = profile.GetOrCreateLayout(level.id);
            layout.placements = towers.CaptureLayout();
            profileStore.Save(profile);
        }

        private void ApplyProgressionStats()
        {
            var bonusLives = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.BaseLivesFlat));
            var towerDamageMultiplier = 1f + progression.GetEffectTotal(UpgradeEffectType.TowerDamagePercent) / 100f;
            var towerFireRateMultiplier = 1f + progression.GetEffectTotal(UpgradeEffectType.TowerFireRatePercent) / 100f;
            var activeDamageMultiplier = 1f + progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponDamagePercent) / 100f;
            var activeCooldownMultiplier = Mathf.Max(0.1f, 1f - progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponCooldownPercent) / 100f);
            var activeRadiusBonus = progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponRadiusFlat);
            var activePierceBonus = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponPierceFlat));
            var activeAutoFireUnlocked = progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponAutoFireUnlock) > 0f;

            maxLivesForRun = level.startingLives + bonusLives;
            towers.SetAvailableTowers(GetUnlockedTowers());
            towers.ClearPerTypeLimitBonuses();
            towers.ClearPerTypeDamageMultipliers();
            towers.ClearPerTypeFireRateMultipliers();
            if (allTowerDefinitions != null)
            {
                foreach (var towerDefinition in allTowerDefinitions)
                {
                    RestoreBaseTowerStats(towerDefinition);
                    var perTypeBonus = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.PerTypeTowerLimitFlat, towerDefinition.id));
                    var perTypeDamageMultiplier = 1f + progression.GetEffectTotal(UpgradeEffectType.TowerDamagePercent, towerDefinition.id) / 100f;
                    var perTypeFireRateMultiplier = 1f + progression.GetEffectTotal(UpgradeEffectType.TowerFireRatePercent, towerDefinition.id) / 100f;
                    towers.SetPerTypeLimitBonus(towerDefinition.id, perTypeBonus);
                    towers.SetPerTypeDamageMultiplier(towerDefinition.id, perTypeDamageMultiplier);
                    towers.SetPerTypeFireRateMultiplier(towerDefinition.id, perTypeFireRateMultiplier);
                    towerDefinition.pierce = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.TowerPierceFlat, towerDefinition.id));
                    towerDefinition.doubleShotChance = progression.GetEffectTotal(UpgradeEffectType.TowerDoubleShotChancePercent, towerDefinition.id) / 100f;
                    towerDefinition.slowPercent = progression.GetEffectTotal(UpgradeEffectType.TowerSlowPercentFlat, towerDefinition.id) / 100f;
                    towerDefinition.slowCapacity = progression.GetEffectTotal(UpgradeEffectType.TowerSlowCapacityFlat, towerDefinition.id);
                    towerDefinition.range += progression.GetEffectTotal(UpgradeEffectType.TowerRangeFlat, towerDefinition.id);
                    towerDefinition.health += progression.GetEffectTotal(UpgradeEffectType.TowerHealthFlat, towerDefinition.id);
                    towerDefinition.thornsDamage = progression.GetEffectTotal(UpgradeEffectType.TowerThornsDamageFlat, towerDefinition.id);
                    towerDefinition.barracksCapacity += Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.BarracksUnitCapacityFlat, towerDefinition.id));
                    towerDefinition.alliedUnitDamage *= 1f + progression.GetEffectTotal(UpgradeEffectType.BarracksUnitDamagePercent, towerDefinition.id) / 100f;
                    towerDefinition.alliedUnitHealth *= 1f + progression.GetEffectTotal(UpgradeEffectType.BarracksUnitHealthPercent, towerDefinition.id) / 100f;
                    towerDefinition.barracksRespawnSeconds *= Mathf.Max(0.1f, 1f - progression.GetEffectTotal(UpgradeEffectType.BarracksRespawnCooldownPercent, towerDefinition.id) / 100f);
                    towerDefinition.appliesFire = progression.GetEffectTotal(UpgradeEffectType.EnableTowerFire, towerDefinition.id) > 0f;
                    towerDefinition.fireDamagePerTick = progression.GetEffectTotal(UpgradeEffectType.TowerFireDamagePerTickFlat, towerDefinition.id);
                    towerDefinition.fireTicksPerSecond = progression.GetEffectTotal(UpgradeEffectType.TowerFireTicksPerSecondFlat, towerDefinition.id);
                    towerDefinition.fireMaxStacks = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.TowerFireMaxStacksFlat, towerDefinition.id));
                    towerDefinition.fireDuration = progression.GetEffectTotal(UpgradeEffectType.TowerFireDurationFlat, towerDefinition.id);
                }
            }
            towers.SetTowerDamageMultiplier(towerDamageMultiplier);
            towers.SetTowerFireRateMultiplier(towerFireRateMultiplier);
            activeWeapon.Damage = baseActiveWeaponDamage * activeDamageMultiplier;
            activeWeapon.CooldownSeconds = baseActiveWeaponCooldown * activeCooldownMultiplier;
            activeWeapon.Radius = baseActiveWeaponRadius + activeRadiusBonus;
            activeWeapon.MaxTargets = baseActiveWeaponMaxTargets + activePierceBonus;
            activeWeapon.AutoFireUnlocked = activeAutoFireUnlocked;
        }

        private IReadOnlyList<TowerDefinition> GetUnlockedTowers()
        {
            var unlocked = new List<TowerDefinition>();
            if (allTowerDefinitions == null)
            {
                return unlocked;
            }

            foreach (var tower in allTowerDefinitions)
            {
                if (progression.GetEffectTotal(UpgradeEffectType.UnlockTower, tower.id) > 0f)
                {
                    unlocked.Add(tower);
                }
            }

            return unlocked;
        }

        private void CaptureBaseTowerStats()
        {
            baseTowerStats.Clear();
            if (allTowerDefinitions == null)
            {
                return;
            }

            foreach (var tower in allTowerDefinitions)
            {
                baseTowerStats[tower.id] = new TowerBaseStats(tower);
            }
        }

        private void RestoreBaseTowerStats(TowerDefinition tower)
        {
            if (tower == null || !baseTowerStats.TryGetValue(tower.id, out var stats))
            {
                return;
            }

            stats.Apply(tower);
        }

        private readonly struct TowerBaseStats
        {
            private readonly float range;
            private readonly float health;
            private readonly float alliedUnitHealth;
            private readonly float alliedUnitDamage;
            private readonly float barracksRespawnSeconds;
            private readonly int barracksCapacity;

            public TowerBaseStats(TowerDefinition tower)
            {
                range = tower.range;
                health = tower.health;
                alliedUnitHealth = tower.alliedUnitHealth;
                alliedUnitDamage = tower.alliedUnitDamage;
                barracksRespawnSeconds = tower.barracksRespawnSeconds;
                barracksCapacity = tower.barracksCapacity;
            }

            public void Apply(TowerDefinition tower)
            {
                tower.range = range;
                tower.health = health;
                tower.alliedUnitHealth = alliedUnitHealth;
                tower.alliedUnitDamage = alliedUnitDamage;
                tower.barracksRespawnSeconds = barracksRespawnSeconds;
                tower.barracksCapacity = barracksCapacity;
                tower.pierce = 0;
                tower.doubleShotChance = 0f;
                tower.slowPercent = 0f;
                tower.slowCapacity = 0f;
                tower.thornsDamage = 0f;
                tower.appliesFire = false;
                tower.fireDamagePerTick = 0f;
                tower.fireTicksPerSecond = 0f;
                tower.fireMaxStacks = 0;
                tower.fireDuration = 0f;
            }
        }
    }
}
