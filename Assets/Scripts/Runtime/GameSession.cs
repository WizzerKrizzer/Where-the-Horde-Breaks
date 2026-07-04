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
        private ProgressionService progression;
        private EnemyManager enemies;
        private TowerManager towers;
        private ActiveWeaponController activeWeapon;
        private WorldPopupManager popups;
        private PlayerInputRouter input;
        private PathRoute path;
        private IReadOnlyList<TowerDefinition> allTowerDefinitions;
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
            SaveLayout();
            profileStore.Save(profile);
        }

        private void OnEnemyKilled(EnemyActor enemy)
        {
            enemiesKilled++;
            rewards.ApplyKillReward(profile, enemy.Definition);
        }

        private void OnEnemyEscaped(EnemyActor enemy)
        {
            lives -= enemy.Definition.lifeDamage;
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
            var activeDamageMultiplier = 1f + progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponDamagePercent) / 100f;
            var activeCooldownMultiplier = Mathf.Max(0.1f, 1f - progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponCooldownPercent) / 100f);
            var activeRadiusBonus = progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponRadiusFlat);
            var activePierceBonus = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.ActiveWeaponPierceFlat));

            maxLivesForRun = level.startingLives + bonusLives;
            towers.SetAvailableTowers(GetUnlockedTowers());
            towers.ClearPerTypeLimitBonuses();
            towers.ClearPerTypeDamageMultipliers();
            if (allTowerDefinitions != null)
            {
                foreach (var towerDefinition in allTowerDefinitions)
                {
                    var perTypeBonus = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.PerTypeTowerLimitFlat, towerDefinition.id));
                    var perTypeDamageMultiplier = 1f + progression.GetEffectTotal(UpgradeEffectType.TowerDamagePercent, towerDefinition.id) / 100f;
                    towers.SetPerTypeLimitBonus(towerDefinition.id, perTypeBonus);
                    towers.SetPerTypeDamageMultiplier(towerDefinition.id, perTypeDamageMultiplier);
                }
            }
            towers.SetTowerDamageMultiplier(towerDamageMultiplier);
            activeWeapon.Damage = baseActiveWeaponDamage * activeDamageMultiplier;
            activeWeapon.CooldownSeconds = baseActiveWeaponCooldown * activeCooldownMultiplier;
            activeWeapon.Radius = baseActiveWeaponRadius + activeRadiusBonus;
            activeWeapon.MaxTargets = baseActiveWeaponMaxTargets + activePierceBonus;
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
    }
}
