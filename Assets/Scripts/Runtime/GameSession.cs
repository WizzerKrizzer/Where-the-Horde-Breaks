using System;
using System.Collections.Generic;
using System.Text;
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
        private IReadOnlyList<LevelDefinition> allLevels;
        private LevelDefinition level;
        private SkillTreeDefinition skillTree;
        private ProgressionService progression;
        private EnemyManager enemies;
        private TowerManager towers;
        private ActiveWeaponController activeWeapon;
        private WorldPopupManager popups;
        private PlayerInputRouter input;
        private PathRoute path;
        private Func<LevelDefinition, PathRoute> loadLevelMap;
        private IReadOnlyList<TowerDefinition> allTowerDefinitions;
        private readonly Dictionary<string, TowerBaseStats> baseTowerStats = new();
        private readonly Dictionary<CurrencyType, int> runStartCurrencies = new();
        private readonly Dictionary<CurrencyType, int> lastRunCurrencyDeltas = new();
        private int lives;
        private int maxLivesForRun;
        private int enemiesKilled;
        private float killRewardMassProgress;
        private float baseActiveWeaponDamage;
        private float baseActiveWeaponCooldown;
        private float baseActiveWeaponRadius;
        private int baseActiveWeaponMaxTargets;
        private int rewardTestMultiplier = 1;
        private bool running;
        private bool finished;
        private bool won;
        private bool devAutoTestLoopEnabled;
        private bool devAutoTestLoopWaitingToStart;
        private float devAutoTestLoopTimer;
        private string devLastAutoPurchase = "None";
        private string devLastAutoPurchaseDetails = "None";
        private bool devBestBotRunning;
        private bool devBestBotWaitingToStart;
        private float devBestBotStartTimer;
        private float devBestBotGameSeconds;
        private float devBestBotRealSeconds;
        private float devBestBotCurrentAttemptSeconds;
        private int devBestBotAttemptCount;
        private int devBestBotCurrentSeed;
        private int devBestBotLastBaseDamage;
        private string devBestBotStatus = "Ready";
        private string devBestBotReport = string.Empty;
        private string devBestBotPurchaseHistory = string.Empty;
        private float devBestBotTowerUpgradeSpend;
        private float devBestBotActiveUpgradeSpend;
        private float devBestBotOtherUpgradeSpend;
        private int devBestBotSelectedProfileIndex;
        private int devBestBotRunningProfileIndex;
        private float devBestBotSelectedTimeScale = 30f;
        private bool devBestBotRunAll;
        private bool devBestBotPendingNextProfile;
        private float devBestBotNextProfileTimer;
        private readonly List<BestBotComparisonRecord> devBestBotComparisons = new();
        private readonly List<BestBotPurchaseRecord> devBestBotPurchases = new();
        private readonly List<BestBotAttemptRecord> devBestBotAttempts = new();
        private ProfileStore devBestBotOriginalProfileStore;
        private PlayerProfile devBestBotOriginalProfile;
        private LevelDefinition devBestBotOriginalLevel;
        private UnityEngine.Random.State devBestBotOriginalRandomState;
        private float devBestBotOriginalTimeScale = 1f;
        private bool devBestBotOriginalAutoActive;
        private float devBestBotOriginalAutoEfficiency = 1f;
        private int devBestBotOriginalRewardMultiplier = 1;
        private const float DevAutoTestLoopDelay = 0.65f;
        private const float DevAutoPurchaseWindowSeconds = 3f;
        private const float DevBestBotPlanningDelay = 0.2f;
        private const int DevBestBotSeedBase = 73001;
        private static readonly BestBotSkillProfile[] DevBestBotSkillProfiles =
        {
            new("Best", 0.60f, 0.00f, 1.00f, 1.00f, 1.00f, 2, false),
            new("Skilled", 0.58f, 0.03f, 0.98f, 0.96f, 0.96f, 2, true),
            new("Average", 0.56f, 0.07f, 0.94f, 0.88f, 0.90f, 1, true),
            new("Casual", 0.53f, 0.12f, 0.85f, 0.76f, 0.80f, 1, true),
            new("Novice", 0.51f, 0.15f, 0.65f, 0.72f, 0.76f, 1, true)
        };
        private static readonly DevAutoUpgradeGoal[] DevAutoUpgradePriority =
        {
            new("steady_tithe_01", 3),
            new("archer_unlock", 1),
            new("archer_limit_01", 4),
            new("volley_damage_01", 4),
            new("volley_pierce_01", 3),
            new("archer_damage_01", 6),
            new("archer_speed_01", 5),
            new("archer_double_01", 5),
            new("archer_flat_damage_01", 3),
            new("archer_flat_speed_01", 3),
            new("projectile_aim_assist_01", 3),
            new("volley_cooldown_01", 4),
            new("volley_radius_01", 3),
            new("archer_projectile_speed_01", 5),
            new("archer_range_01", 5),
            new("archer_damage_01", 10),
            new("archer_speed_01", 8),
            new("archer_flat_damage_01", 10),
            new("archer_flat_speed_01", 8),
            new("projectile_aim_assist_01", 5),
            new("volley_damage_01", 10),
            new("volley_pierce_01", 6),
            new("volley_cooldown_01", 8),
            new("volley_radius_01", 5),
            new("base_health_01", 8)
        };

        public PlayerProfile Profile => profile;
        public LevelDefinition Level => level;
        public IReadOnlyList<LevelDefinition> AllLevels => allLevels ?? Array.Empty<LevelDefinition>();
        public int Lives => lives;
        public int EnemiesKilled => enemiesKilled;
        public bool IsPlanning => !running && !finished;
        public bool IsRunning => running;
        public bool Finished => finished;
        public bool Won => finished && won;
        public IReadOnlyDictionary<CurrencyType, int> LastRunCurrencyDeltas => lastRunCurrencyDeltas;
        public IReadOnlyList<SkillNodeDefinition> UpgradeNodes => progression.GetNodes();
        public IReadOnlyList<TowerDefinition> AllTowerDefinitions => allTowerDefinitions;
        public IReadOnlyList<TowerDefinition> UnlockedTowerDefinitions => towers?.AvailableTowers ?? System.Array.Empty<TowerDefinition>();
        public float PathLength => path?.TotalLength ?? 0f;
        public float BaseActiveWeaponDamage => baseActiveWeaponDamage;
        public float BaseActiveWeaponCooldown => baseActiveWeaponCooldown;
        public float BaseActiveWeaponRadius => baseActiveWeaponRadius;
        public int BaseActiveWeaponMaxTargets => baseActiveWeaponMaxTargets;
        public int RewardTestMultiplier => rewardTestMultiplier;
        public bool RewardTestingEnabled => rewardTestMultiplier > 1;
        public bool DevAutoActiveEnabled => activeWeapon != null && activeWeapon.DevAutoActiveEnabled;
        public bool DevAutoTestLoopEnabled => devAutoTestLoopEnabled;
        public bool DevAutoPurchaseWindowVisible => devAutoTestLoopEnabled && devAutoTestLoopWaitingToStart;
        public string DevLastAutoPurchase => devLastAutoPurchase;
        public string DevLastAutoPurchaseDetails => devLastAutoPurchaseDetails;
        public bool DevBestBotRunning => devBestBotRunning || devBestBotPendingNextProfile;
        public bool DevBestBotWaitingToStart => devBestBotRunning && devBestBotWaitingToStart;
        public int DevBestBotAttemptCount => devBestBotAttemptCount;
        public string DevBestBotStatus => devBestBotStatus;
        public bool DevBestBotReportAvailable => !string.IsNullOrEmpty(devBestBotReport);
        public string DevBestBotReport => devBestBotReport;
        public string DevBestBotPurchaseHistory => devBestBotPurchaseHistory;
        public string DevBestBotSelectedProfileName => DevBestBotSkillProfiles[devBestBotSelectedProfileIndex].Name;
        public float DevBestBotSelectedTimeScale => devBestBotSelectedTimeScale;
        public bool DevBestBotRunAll => devBestBotRunAll;

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

        public LevelProgressRecord GetLevelProgress(string levelId = null)
        {
            return profile.GetOrCreateLevelProgress(string.IsNullOrEmpty(levelId) ? level.id : levelId);
        }

        public bool IsLevelUnlocked(LevelDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            if (definition.id == "level_01")
            {
                return true;
            }

            if (profile.unlockedLevelIds.Contains(definition.id) || profile.clearedLevelIds.Contains(definition.id))
            {
                return true;
            }

            if (definition.id == "level_02")
            {
                return profile.clearedLevelIds.Contains("level_01");
            }

            if (definition.id == "level_03")
            {
                return profile.clearedLevelIds.Contains("level_02");
            }

            return definition.id == "level_04" && profile.clearedLevelIds.Contains("level_03");
        }

        public bool SelectLevel(string levelId)
        {
            if (running || string.IsNullOrEmpty(levelId) || allLevels == null)
            {
                return false;
            }

            var nextLevel = FindLevel(levelId);
            if (nextLevel == null || nextLevel == level)
            {
                return false;
            }

            SaveLayout();
            enemies?.StopWave();
            towers?.RemoveAll();
            level = nextLevel;
            profile.selectedLevelId = level.id;
            path = loadLevelMap != null ? loadLevelMap(level) : path;
            enemies?.SetLevelRoute(path);
            towers.Initialize(enemies, path, GetUnlockedTowers());
            ApplyProgressionStats();
            enemiesKilled = 0;
            killRewardMassProgress = 0f;
            CaptureRunStartCurrencies();
            lastRunCurrencyDeltas.Clear();
            running = false;
            finished = false;
            won = false;
            activeWeapon.CanFire = false;
            lives = maxLivesForRun;
            towers.LoadLayout(profile.GetOrCreateLayout(level.id).placements);
            profileStore.Save(profile);
            return true;
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

        public void ToggleRewardTesting()
        {
            rewardTestMultiplier = rewardTestMultiplier switch
            {
                1 => 2,
                2 => 3,
                3 => 5,
                5 => 10,
                _ => 1
            };
        }

        public void ToggleDevAutoActive()
        {
            if (activeWeapon != null)
            {
                activeWeapon.DevAutoActiveEnabled = !activeWeapon.DevAutoActiveEnabled;
            }
        }

        public void ToggleDevAutoTestLoop()
        {
            devAutoTestLoopEnabled = !devAutoTestLoopEnabled;
            devAutoTestLoopWaitingToStart = false;
            devAutoTestLoopTimer = devAutoTestLoopEnabled ? 0f : DevAutoTestLoopDelay;
            if (devAutoTestLoopEnabled && activeWeapon != null)
            {
                activeWeapon.DevAutoActiveEnabled = true;
            }
        }

        public void ToggleDevBestBot()
        {
            if (devBestBotRunning)
            {
                StopDevBestBot();
                return;
            }

            StartDevBestBot();
        }

        public void SelectPreviousDevBestBotProfile()
        {
            if (!devBestBotRunning && !devBestBotPendingNextProfile)
            {
                devBestBotSelectedProfileIndex = (devBestBotSelectedProfileIndex + DevBestBotSkillProfiles.Length - 1) % DevBestBotSkillProfiles.Length;
            }
        }

        public void SelectNextDevBestBotProfile()
        {
            if (!devBestBotRunning && !devBestBotPendingNextProfile)
            {
                devBestBotSelectedProfileIndex = (devBestBotSelectedProfileIndex + 1) % DevBestBotSkillProfiles.Length;
            }
        }

        public void SetDevBestBotTimeScale(float speed)
        {
            if (!devBestBotRunning && !devBestBotPendingNextProfile)
            {
                devBestBotSelectedTimeScale = Mathf.Clamp(speed, 20f, 50f);
            }
        }

        public void StopDevBestBot()
        {
            if (devBestBotPendingNextProfile)
            {
                devBestBotPendingNextProfile = false;
                devBestBotRunAll = false;
                devBestBotReport = BuildDevBestBotComparisonReport();
                devBestBotPurchaseHistory = BuildDevBestBotComparisonPurchaseHistory();
                devBestBotStatus = "Run All stopped between profiles";
                return;
            }

            if (!devBestBotRunning)
            {
                return;
            }

            devBestBotReport = BuildDevBestBotReport(false, "Stopped manually");
            devBestBotRunAll = false;
            devBestBotPendingNextProfile = false;
            RestoreProfileAfterDevBestBot();
        }

        public void StartAllDevBestBots()
        {
            if (devBestBotRunning || devBestBotPendingNextProfile)
            {
                return;
            }

            devBestBotRunAll = true;
            devBestBotComparisons.Clear();
            devBestBotSelectedProfileIndex = 0;
            StartDevBestBot();
        }

        public void DismissDevBestBotReport()
        {
            devBestBotReport = string.Empty;
            devBestBotPurchaseHistory = string.Empty;
        }

        public void ClearLevelRewardProgress()
        {
            profile.ClearLevelRewardProgress();
            profile.ResetBalanceTestProgress();
            profileStore.Save(profile);
        }

        public void RefundAndResetUpgrades()
        {
            progression.RefundAndResetPurchasedUpgrades();
            profile.ResetBalanceTestProgress();
            profileStore.Save(profile);
            ResetToPlanning();
        }

        public void ResetBalanceTestProgress()
        {
            profile.ResetBalanceTestProgress();
            profileStore.Save(profile);
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
            var savedLevel = FindLevel(profile.selectedLevelId);
            if (savedLevel != null && savedLevel != level)
            {
                level = savedLevel;
                path = loadLevelMap != null ? loadLevelMap(level) : path;
                enemies?.SetLevelRoute(path);
                towers.Initialize(enemies, path, GetUnlockedTowers());
            }

            profileStore.Save(profile);
            ResetToPlanning();
            return true;
        }

        public void SetSelectedTowerTargeting(TowerTargetingMode mode)
        {
            if (towers?.SelectedTower == null || !towers.SelectedTower.CanChangeTargeting)
            {
                return;
            }

            towers.SelectedTower.SetTargetingMode(mode);
            SaveLayout();
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

        public float GetTowerBaseDamage(string towerId)
        {
            if (!string.IsNullOrEmpty(towerId) && baseTowerStats.TryGetValue(towerId, out var stats))
            {
                return stats.Damage;
            }

            return GetTowerDefinition(towerId)?.damage ?? 0f;
        }

        public float GetTowerBaseFireRate(string towerId)
        {
            if (!string.IsNullOrEmpty(towerId) && baseTowerStats.TryGetValue(towerId, out var stats))
            {
                return stats.FireRate;
            }

            var tower = GetTowerDefinition(towerId);
            return tower == null ? 0f : 1f / Mathf.Max(0.01f, tower.fireInterval);
        }

        public float GetTowerBaseProjectileSpeed(string towerId)
        {
            if (!string.IsNullOrEmpty(towerId) && baseTowerStats.TryGetValue(towerId, out var stats))
            {
                return stats.ProjectileSpeed;
            }

            return GetTowerDefinition(towerId)?.projectileSpeed ?? 0f;
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
            IReadOnlyList<LevelDefinition> levelDefinitions,
            LevelDefinition levelDefinition,
            SkillTreeDefinition skillTree,
            PathRoute path,
            Func<LevelDefinition, PathRoute> levelMapLoader,
            IReadOnlyList<TowerDefinition> availableTowers,
            EnemyManager enemyManager,
            TowerManager towerManager,
            ActiveWeaponController activeWeaponController,
            WorldPopupManager popupManager,
            PlayerInputRouter inputRouter)
        {
            allLevels = levelDefinitions;
            this.skillTree = skillTree;
            profileStore = new ProfileStore();
            profile = profileStore.LoadOrCreate();
            loadLevelMap = levelMapLoader;
            level = FindLevel(profile.selectedLevelId) ?? levelDefinition;
            if (level != null)
            {
                profile.selectedLevelId = level.id;
            }

            progression = new ProgressionService(skillTree, profile);
            enemies = enemyManager;
            towers = towerManager;
            activeWeapon = activeWeaponController;
            popups = popupManager;
            input = inputRouter;
            this.path = level != levelDefinition && loadLevelMap != null ? loadLevelMap(level) : path;
            enemies?.SetLevelRoute(this.path);
            allTowerDefinitions = availableTowers;
            CaptureBaseTowerStats();
            baseActiveWeaponDamage = activeWeapon.Damage;
            baseActiveWeaponCooldown = activeWeapon.CooldownSeconds;
            baseActiveWeaponRadius = activeWeapon.Radius;
            baseActiveWeaponMaxTargets = activeWeapon.MaxTargets;

            enemiesKilled = 0;
            killRewardMassProgress = 0f;
            CaptureRunStartCurrencies();
            lastRunCurrencyDeltas.Clear();
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

        private LevelDefinition FindLevel(string levelId)
        {
            if (allLevels == null)
            {
                return null;
            }

            for (var i = 0; i < allLevels.Count; i++)
            {
                if (allLevels[i] != null && allLevels[i].id == levelId)
                {
                    return allLevels[i];
                }
            }

            return null;
        }

        private void Update()
        {
            if (input == null)
            {
                return;
            }

            if (devBestBotPendingNextProfile)
            {
                devBestBotNextProfileTimer -= Time.unscaledDeltaTime;
                if (devBestBotNextProfileTimer <= 0f)
                {
                    devBestBotPendingNextProfile = false;
                    StartDevBestBot();
                }

                return;
            }

            if (devBestBotRunning)
            {
                UpdateDevBestBot();
                if (devBestBotRunning && running)
                {
                    if (lives <= 0)
                    {
                        Finish(false);
                    }
                    else if (enemies.IsWaveComplete)
                    {
                        Finish(true);
                    }
                }

                return;
            }

            UpdateDevAutoTestLoop();

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

        private void StartDevBestBot()
        {
            var levelOne = FindLevel("level_01");
            if (levelOne == null || profileStore == null || enemies == null || towers == null || activeWeapon == null)
            {
                devBestBotStatus = "Cannot start: Level 1 is unavailable";
                return;
            }

            SaveLayout();
            enemies.StopWave();
            towers.RemoveAll();
            if (!devBestBotRunAll)
            {
                devBestBotComparisons.Clear();
            }

            devBestBotOriginalProfileStore = profileStore;
            devBestBotOriginalProfile = profile;
            devBestBotOriginalLevel = level;
            devBestBotOriginalRandomState = UnityEngine.Random.state;
            devBestBotOriginalTimeScale = Mathf.Approximately(Time.timeScale, 0f) ? 1f : Time.timeScale;
            devBestBotOriginalAutoActive = activeWeapon.DevAutoActiveEnabled;
            devBestBotOriginalAutoEfficiency = activeWeapon.DevAutoEfficiency;
            devBestBotOriginalRewardMultiplier = rewardTestMultiplier;
            devBestBotRunningProfileIndex = devBestBotSelectedProfileIndex;

            profileStore = new ProfileStore("best_bot_test_profile.json");
            profile = new PlayerProfile { selectedLevelId = levelOne.id };
            profile.unlockedLevelIds.Add(levelOne.id);
            progression = new ProgressionService(skillTree, profile);
            level = levelOne;
            path = loadLevelMap != null ? loadLevelMap(level) : path;
            enemies.SetLevelRoute(path);
            towers.Initialize(enemies, path, GetUnlockedTowers());
            ApplyProgressionStats();
            lives = maxLivesForRun;
            enemiesKilled = 0;
            killRewardMassProgress = 0f;
            running = false;
            finished = false;
            won = false;
            activeWeapon.CanFire = false;
            activeWeapon.DevAutoActiveEnabled = true;
            activeWeapon.DevAutoEfficiency = RunningDevBestBotProfile.ActiveWeaponEfficiency;
            rewardTestMultiplier = 1;
            devAutoTestLoopEnabled = false;
            lastRunCurrencyDeltas.Clear();
            profileStore.Save(profile);

            devBestBotAttemptCount = 0;
            devBestBotGameSeconds = 0f;
            devBestBotRealSeconds = 0f;
            devBestBotCurrentAttemptSeconds = 0f;
            devBestBotCurrentSeed = 0;
            devBestBotLastBaseDamage = 0;
            devBestBotPurchases.Clear();
            devBestBotAttempts.Clear();
            devBestBotTowerUpgradeSpend = 0f;
            devBestBotActiveUpgradeSpend = 0f;
            devBestBotOtherUpgradeSpend = 0f;
            devBestBotReport = string.Empty;
            devBestBotPurchaseHistory = string.Empty;
            devBestBotRunning = true;
            devBestBotWaitingToStart = true;
            devBestBotStartTimer = DevBestBotPlanningDelay;
            devBestBotStatus = $"Preparing {RunningDevBestBotProfile.Name} on a fresh Level 1 profile";
            Time.timeScale = devBestBotSelectedTimeScale;
        }

        private void UpdateDevBestBot()
        {
            devBestBotRealSeconds += Time.unscaledDeltaTime;
            if (running)
            {
                devBestBotGameSeconds += Time.deltaTime;
                devBestBotCurrentAttemptSeconds += Time.deltaTime;
                devBestBotStatus = $"Attempt {devBestBotAttemptCount} running | {FormatBotDuration(devBestBotCurrentAttemptSeconds)} | seed {devBestBotCurrentSeed}";
                return;
            }

            if (finished)
            {
                CaptureDevBestBotAttempt();
                if (won)
                {
                    CompleteDevBestBotVictory();
                    return;
                }

                ResetToPlanning();
                var purchaseCount = TryBuyBestBotUpgrades();
                RebuildBestBotTowerLayout();
                devBestBotWaitingToStart = true;
                devBestBotStartTimer = DevBestBotPlanningDelay;
                devBestBotStatus = purchaseCount > 0
                    ? $"Planning attempt {devBestBotAttemptCount + 1} | bought {purchaseCount} rank(s)"
                    : $"Planning attempt {devBestBotAttemptCount + 1} | no affordable upgrades";
                return;
            }

            if (!IsPlanning)
            {
                return;
            }

            if (!devBestBotWaitingToStart)
            {
                devBestBotWaitingToStart = true;
                devBestBotStartTimer = DevBestBotPlanningDelay;
            }

            devBestBotStartTimer -= Time.unscaledDeltaTime;
            if (devBestBotStartTimer > 0f)
            {
                return;
            }

            devBestBotWaitingToStart = false;
            devBestBotAttemptCount++;
            devBestBotCurrentAttemptSeconds = 0f;
            devBestBotCurrentSeed = DevBestBotSeedBase + devBestBotAttemptCount * 7919;
            UnityEngine.Random.InitState(devBestBotCurrentSeed);
            StartLevel();
        }

        private void CompleteDevBestBotVictory()
        {
            if (!devBestBotRunAll)
            {
                devBestBotReport = BuildDevBestBotReport(true, "Level 1 cleared");
                RestoreProfileAfterDevBestBot();
                return;
            }

            devBestBotComparisons.Add(CreateDevBestBotComparisonRecord());
            var nextProfileIndex = devBestBotRunningProfileIndex + 1;
            RestoreProfileAfterDevBestBot();
            if (nextProfileIndex < DevBestBotSkillProfiles.Length)
            {
                devBestBotSelectedProfileIndex = nextProfileIndex;
                devBestBotReport = string.Empty;
                devBestBotPurchaseHistory = string.Empty;
                devBestBotPendingNextProfile = true;
                devBestBotNextProfileTimer = 0.35f;
                devBestBotStatus = $"Next: {DevBestBotSkillProfiles[nextProfileIndex].Name}";
                return;
            }

            devBestBotRunAll = false;
            devBestBotReport = BuildDevBestBotComparisonReport();
            devBestBotPurchaseHistory = BuildDevBestBotComparisonPurchaseHistory();
            devBestBotStatus = "All five bot profiles finished";
        }

        private BestBotComparisonRecord CreateDevBestBotComparisonRecord()
        {
            var towerDamage = 0f;
            var activeDamage = 0f;
            for (var i = 0; i < devBestBotAttempts.Count; i++)
            {
                towerDamage += devBestBotAttempts[i].TowerDamage;
                activeDamage += devBestBotAttempts[i].ActiveWeaponDamage;
            }

            var finalAttempt = devBestBotAttempts.Count > 0 ? devBestBotAttempts[devBestBotAttempts.Count - 1] : null;
            return new BestBotComparisonRecord
            {
                Name = RunningDevBestBotProfile.Name,
                Attempts = devBestBotAttemptCount,
                GameSeconds = devBestBotGameSeconds,
                RealSeconds = devBestBotRealSeconds,
                WinningRunSeconds = finalAttempt?.GameSeconds ?? 0f,
                LivesRemaining = finalAttempt?.LivesRemaining ?? 0,
                TowerDamage = towerDamage,
                ActiveDamage = activeDamage,
                PurchaseHistory = BuildDevBestBotPurchaseHistory()
            };
        }

        private string BuildDevBestBotComparisonReport()
        {
            var text = new StringBuilder();
            text.AppendLine("ALL 5 BOTS — LEVEL 1 COMPARISON");
            text.AppendLine($"Speed: {devBestBotSelectedTimeScale:0}x   Same deterministic seed sequence");
            text.AppendLine("Profile      Runs   Game    Real    Win     HP    Tower dmg   Active dmg");
            for (var i = 0; i < devBestBotComparisons.Count; i++)
            {
                var result = devBestBotComparisons[i];
                text.AppendLine($"{result.Name,-10} {result.Attempts,4}   {FormatBotDuration(result.GameSeconds),5}   {FormatBotDuration(result.RealSeconds),5}   {FormatBotDuration(result.WinningRunSeconds),5}   {result.LivesRemaining,3}   {result.TowerDamage,9:0}   {result.ActiveDamage,10:0}");
            }

            return text.ToString().TrimEnd();
        }

        private string BuildDevBestBotComparisonPurchaseHistory()
        {
            var text = new StringBuilder();
            for (var i = 0; i < devBestBotComparisons.Count; i++)
            {
                if (i > 0)
                {
                    text.AppendLine();
                    text.AppendLine("────────────────────────");
                    text.AppendLine();
                }

                text.AppendLine($"{devBestBotComparisons[i].Name.ToUpperInvariant()} BOT");
                text.AppendLine(devBestBotComparisons[i].PurchaseHistory);
            }

            return text.ToString().TrimEnd();
        }

        private void UpdateDevAutoTestLoop()
        {
            if (!devAutoTestLoopEnabled)
            {
                return;
            }

            if (devAutoTestLoopWaitingToStart)
            {
                devAutoTestLoopTimer -= Time.unscaledDeltaTime;
                if (devAutoTestLoopTimer > 0f)
                {
                    return;
                }

                devAutoTestLoopWaitingToStart = false;
                StartLevel();
                return;
            }

            if (finished)
            {
                if (won)
                {
                    devAutoTestLoopEnabled = false;
                    return;
                }

                devAutoTestLoopTimer -= Time.unscaledDeltaTime;
                if (devAutoTestLoopTimer > 0f)
                {
                    return;
                }

                ResetToPlanning();
                var boughtUpgrades = TryBuyDevAutoUpgrades();
                TryDevAutoPlaceTowers();
                if (boughtUpgrades)
                {
                    devAutoTestLoopWaitingToStart = true;
                    devAutoTestLoopTimer = DevAutoPurchaseWindowSeconds;
                }
                else
                {
                    StartLevel();
                    devAutoTestLoopTimer = DevAutoTestLoopDelay;
                }
                return;
            }

            if (IsPlanning)
            {
                var boughtUpgrades = TryBuyDevAutoUpgrades();
                TryDevAutoPlaceTowers();
                if (!boughtUpgrades)
                {
                    StartLevel();
                }
                else
                {
                    devAutoTestLoopWaitingToStart = true;
                    devAutoTestLoopTimer = DevAutoPurchaseWindowSeconds;
                }
            }
        }

        private bool TryBuyDevAutoUpgrades()
        {
            var purchases = new List<string>();
            var guard = 0;
            while (guard++ < 80)
            {
                var boughtThisPass = false;
                for (var i = 0; i < DevAutoUpgradePriority.Length; i++)
                {
                    var goal = DevAutoUpgradePriority[i];
                    if (progression.GetPurchasedRank(goal.NodeId) >= goal.TargetRank || !progression.CanPurchase(goal.NodeId))
                    {
                        continue;
                    }

                    if (!progression.TryPurchase(goal.NodeId))
                    {
                        continue;
                    }

                    ApplyProgressionStats();
                    purchases.Add(FormatAutoUpgradePurchase(goal.NodeId));
                    boughtThisPass = true;
                    break;
                }

                if (!boughtThisPass)
                {
                    break;
                }
            }

            if (purchases.Count == 0)
            {
                devLastAutoPurchase = "None affordable";
                devLastAutoPurchaseDetails = "None affordable";
                return false;
            }

            profileStore.Save(profile);
            devLastAutoPurchaseDetails = purchases.Count <= 8
                ? string.Join("\n", purchases)
                : $"{string.Join("\n", purchases.GetRange(0, 8))}\n+{purchases.Count - 8} more";
            devLastAutoPurchase = purchases.Count <= 3
                ? string.Join(", ", purchases)
                : $"{string.Join(", ", purchases.GetRange(0, 3))} +{purchases.Count - 3} more";
            return true;
        }

        private string FormatAutoUpgradePurchase(string nodeId)
        {
            if (skillTree?.nodes == null)
            {
                return nodeId;
            }

            for (var i = 0; i < skillTree.nodes.Length; i++)
            {
                var node = skillTree.nodes[i];
                if (node != null && node.id == nodeId)
                {
                    return $"{node.displayName} {progression.GetPurchasedRank(nodeId)}/{progression.GetMaxRank(nodeId)}";
                }
            }

            return nodeId;
        }

        private void TryDevAutoPlaceTowers()
        {
            TryDevAutoPlaceTowerType("archer");
            SaveLayout();
        }

        private void TryDevAutoPlaceTowerType(string towerId)
        {
            var definition = GetTowerDefinition(towerId);
            if (definition == null || !IsTowerAvailable(definition))
            {
                return;
            }

            var limit = towers.GetPerTypeLimit(definition);
            var guard = 0;
            while (towers.CountOf(definition) < limit && guard++ < 24)
            {
                if (!TryPlaceDevTowerAtBestCandidate(definition))
                {
                    return;
                }
            }
        }

        private bool IsTowerAvailable(TowerDefinition definition)
        {
            if (definition == null || towers.AvailableTowers == null)
            {
                return false;
            }

            for (var i = 0; i < towers.AvailableTowers.Count; i++)
            {
                if (towers.AvailableTowers[i] == definition)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryPlaceDevTowerAtBestCandidate(TowerDefinition definition)
        {
            if (path == null || path.TotalLength <= 0f)
            {
                return false;
            }

            var fractions = new[] { 0.14f, 0.26f, 0.38f, 0.5f, 0.62f, 0.74f, 0.86f, 0.2f, 0.44f, 0.68f, 0.92f };
            var sideDistances = new[] { 4.05f, -4.05f, 5.2f, -5.2f, 6.25f, -6.25f };
            var startIndex = Mathf.Max(0, towers.CountOf(definition)) % fractions.Length;
            for (var i = 0; i < fractions.Length; i++)
            {
                var fraction = fractions[(startIndex + i) % fractions.Length];
                var distance = path.TotalLength * fraction;
                var center = path.Sample(distance);
                var tangent = GetPathTangent(distance);
                var side = new Vector3(-tangent.z, 0f, tangent.x).normalized;
                if (side.sqrMagnitude < 0.01f)
                {
                    side = Vector3.right;
                }

                for (var j = 0; j < sideDistances.Length; j++)
                {
                    var candidate = center + side * sideDistances[j];
                    candidate.y = 0f;
                    if (IsTooCloseToSameDevTower(definition, candidate))
                    {
                        continue;
                    }

                    if (towers.TryPlace(definition, candidate))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsTooCloseToSameDevTower(TowerDefinition definition, Vector3 candidate)
        {
            const float minimumSpacing = 7.25f;
            var minimumSpacingSq = minimumSpacing * minimumSpacing;
            foreach (var tower in towers.Towers)
            {
                if (tower == null || tower.Definition != definition)
                {
                    continue;
                }

                var delta = tower.transform.position - candidate;
                delta.y = 0f;
                if (delta.sqrMagnitude < minimumSpacingSq)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 GetPathTangent(float distance)
        {
            var before = path.Sample(Mathf.Max(0f, distance - 0.5f));
            var after = path.Sample(Mathf.Min(path.TotalLength, distance + 0.5f));
            var tangent = after - before;
            tangent.y = 0f;
            return tangent.sqrMagnitude > 0.001f ? tangent.normalized : Vector3.forward;
        }

        private int TryBuyBestBotUpgrades()
        {
            var bought = 0;
            var guard = 0;
            while (guard++ < 200)
            {
                SkillNodeDefinition bestNode = null;
                SkillNodeDefinition bestTowerNode = null;
                SkillNodeDefinition bestActiveNode = null;
                SkillNodeDefinition bestOtherNode = null;
                var bestTowerScore = float.MinValue;
                var bestActiveScore = float.MinValue;
                var bestOtherScore = float.MinValue;
                var nodes = progression.GetNodes();
                var incomeNode = RunningDevBestBotProfile.IncomePolicy > 0 ? FindBestBotIncomeNode(nodes) : null;
                if (incomeNode != null && progression.CanPurchase(incomeNode.id))
                {
                    bestNode = incomeNode;
                }

                for (var i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (bestNode != null || node == null || !progression.CanPurchase(node.id))
                    {
                        continue;
                    }

                    if (!RunningDevBestBotProfile.AllowBaseLives && HasUpgradeEffect(node, UpgradeEffectType.BaseLivesFlat))
                    {
                        continue;
                    }

                    if (incomeNode != null && RunningDevBestBotProfile.IncomePolicy >= 2 && !CanBuyWithoutSpendingReservedIncomeCurrency(node, incomeNode))
                    {
                        continue;
                    }

                    var score = ScoreBestBotUpgrade(node);
                    switch (GetBestBotUpgradeCategory(node))
                    {
                        case BestBotUpgradeCategory.Tower when score > bestTowerScore:
                            bestTowerScore = score;
                            bestTowerNode = node;
                            break;
                        case BestBotUpgradeCategory.ActiveWeapon when score > bestActiveScore:
                            bestActiveScore = score;
                            bestActiveNode = node;
                            break;
                        case BestBotUpgradeCategory.Other when score > bestOtherScore:
                            bestOtherScore = score;
                            bestOtherNode = node;
                            break;
                    }
                }

                bestNode ??= SelectBalancedBestBotCombatUpgrade(bestTowerNode, bestActiveNode, bestOtherNode);

                if (bestNode == null)
                {
                    break;
                }

                var category = GetBestBotUpgradeCategory(bestNode);
                var purchaseSpend = GetBestBotWeightedUpgradeCost(bestNode);
                if (!progression.TryPurchase(bestNode.id))
                {
                    break;
                }

                if (category == BestBotUpgradeCategory.Tower)
                {
                    devBestBotTowerUpgradeSpend += purchaseSpend;
                }
                else if (category == BestBotUpgradeCategory.ActiveWeapon)
                {
                    devBestBotActiveUpgradeSpend += purchaseSpend;
                }
                else
                {
                    devBestBotOtherUpgradeSpend += purchaseSpend;
                }

                bought++;
                devBestBotPurchases.Add(new BestBotPurchaseRecord
                {
                    Attempt = devBestBotAttemptCount + 1,
                    DisplayName = bestNode.displayName,
                    Rank = progression.GetPurchasedRank(bestNode.id),
                    MaxRank = progression.GetMaxRank(bestNode.id),
                    EffectSummary = FormatBestBotUpgradeEffects(bestNode)
                });
            }

            if (bought > 0)
            {
                ApplyProgressionStats();
                profileStore.Save(profile);
            }

            return bought;
        }

        private SkillNodeDefinition SelectBalancedBestBotCombatUpgrade(
            SkillNodeDefinition towerNode,
            SkillNodeDefinition activeNode,
            SkillNodeDefinition otherNode)
        {
            var totalSpend = devBestBotTowerUpgradeSpend + devBestBotActiveUpgradeSpend + devBestBotOtherUpgradeSpend;
            var profile = RunningDevBestBotProfile;
            var activeTarget = Mathf.Max(0f, 1f - profile.TowerSpendTarget - profile.OtherSpendTarget);
            var towerDeficit = towerNode == null ? float.MinValue : profile.TowerSpendTarget * (totalSpend + 1f) - devBestBotTowerUpgradeSpend;
            var activeDeficit = activeNode == null ? float.MinValue : activeTarget * (totalSpend + 1f) - devBestBotActiveUpgradeSpend;
            var otherDeficit = otherNode == null ? float.MinValue : profile.OtherSpendTarget * (totalSpend + 1f) - devBestBotOtherUpgradeSpend;

            if (towerDeficit >= activeDeficit && towerDeficit >= otherDeficit)
            {
                return towerNode;
            }

            return activeDeficit >= otherDeficit ? activeNode : otherNode;
        }

        private BestBotSkillProfile RunningDevBestBotProfile => DevBestBotSkillProfiles[Mathf.Clamp(devBestBotRunningProfileIndex, 0, DevBestBotSkillProfiles.Length - 1)];

        private float DeterministicBestBotNoise(string key, int salt)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (!string.IsNullOrEmpty(key))
                {
                    for (var i = 0; i < key.Length; i++)
                    {
                        hash = (hash ^ key[i]) * 16777619;
                    }
                }

                hash = (hash ^ (uint)(salt * 397 + devBestBotAttemptCount * 7919 + devBestBotRunningProfileIndex * 104729)) * 16777619;
                return (hash & 0x00ffffff) / 16777215f;
            }
        }

        private float GetBestBotWeightedUpgradeCost(SkillNodeDefinition node)
        {
            var weightedCost = 0f;
            var costs = progression.GetCurrentCosts(node.id);
            for (var i = 0; i < costs.Length; i++)
            {
                weightedCost += costs[i].amount * GetBestBotCurrencyWeight(costs[i].currency);
            }

            return Mathf.Max(1f, weightedCost);
        }

        private static BestBotUpgradeCategory GetBestBotUpgradeCategory(SkillNodeDefinition node)
        {
            if (node?.effects == null)
            {
                return BestBotUpgradeCategory.Other;
            }

            for (var i = 0; i < node.effects.Length; i++)
            {
                if (IsTowerUpgradeEffect(node.effects[i].type))
                {
                    return BestBotUpgradeCategory.Tower;
                }

                if (IsActiveWeaponUpgradeEffect(node.effects[i].type))
                {
                    return BestBotUpgradeCategory.ActiveWeapon;
                }
            }

            return BestBotUpgradeCategory.Other;
        }

        private static bool IsTowerUpgradeEffect(UpgradeEffectType type)
        {
            return type == UpgradeEffectType.UnlockTower ||
                   type == UpgradeEffectType.PerTypeTowerLimitFlat ||
                   type == UpgradeEffectType.TowerDamageFlat ||
                   type == UpgradeEffectType.TowerDamagePercent ||
                   type == UpgradeEffectType.TowerFireRateFlat ||
                   type == UpgradeEffectType.TowerFireRatePercent ||
                   type == UpgradeEffectType.TowerProjectileSpeedPercent ||
                   type == UpgradeEffectType.TowerAimAssistPercent ||
                   type == UpgradeEffectType.TowerPierceFlat ||
                   type == UpgradeEffectType.TowerDoubleShotChancePercent ||
                   type == UpgradeEffectType.TowerSlowPercentFlat ||
                   type == UpgradeEffectType.TowerSlowCapacityFlat ||
                   type == UpgradeEffectType.TowerRangeFlat ||
                   type == UpgradeEffectType.TowerHealthFlat ||
                   type == UpgradeEffectType.TowerThornsDamageFlat ||
                   type == UpgradeEffectType.BarracksUnitCapacityFlat ||
                   type == UpgradeEffectType.BarracksUnitDamagePercent ||
                   type == UpgradeEffectType.BarracksUnitHealthPercent ||
                   type == UpgradeEffectType.BarracksRespawnCooldownPercent ||
                   type == UpgradeEffectType.EnableTowerFire ||
                   type == UpgradeEffectType.TowerFireDamagePerTickFlat ||
                   type == UpgradeEffectType.TowerFireTicksPerSecondFlat ||
                   type == UpgradeEffectType.TowerFireMaxStacksFlat ||
                   type == UpgradeEffectType.TowerFireDurationFlat;
        }

        private static bool IsActiveWeaponUpgradeEffect(UpgradeEffectType type)
        {
            return type == UpgradeEffectType.ActiveWeaponDamagePercent ||
                   type == UpgradeEffectType.ActiveWeaponCooldownPercent ||
                   type == UpgradeEffectType.ActiveWeaponRadiusFlat ||
                   type == UpgradeEffectType.ActiveWeaponPierceFlat ||
                   type == UpgradeEffectType.ActiveWeaponAutoFireUnlock;
        }

        private string FormatBestBotUpgradeEffects(SkillNodeDefinition node)
        {
            if (node?.effects == null || node.effects.Length == 0)
            {
                return string.IsNullOrWhiteSpace(node?.description) ? "Milestone unlock" : node.description;
            }

            var parts = new List<string>();
            for (var i = 0; i < node.effects.Length; i++)
            {
                parts.Add(FormatBestBotUpgradeEffect(node.effects[i]));
            }

            return string.Join("; ", parts);
        }

        private string FormatBestBotUpgradeEffect(UpgradeEffect effect)
        {
            var target = FormatBestBotTarget(effect.targetId);
            switch (effect.type)
            {
                case UpgradeEffectType.UnlockTower:
                    return $"Unlocks the {target}";
                case UpgradeEffectType.PerTypeTowerLimitFlat:
                    return $"+{effect.value:0} {target} placement limit";
                case UpgradeEffectType.TowerDamageFlat:
                    return $"+{effect.value:0.#} {target} damage per hit";
                case UpgradeEffectType.TowerDamagePercent:
                    return string.IsNullOrEmpty(effect.targetId) ? $"+{effect.value:0.#}% tower damage" : $"+{effect.value:0.#}% {target} damage";
                case UpgradeEffectType.TowerFireRateFlat:
                    return $"+{effect.value:0.#} {target} shots per second";
                case UpgradeEffectType.TowerFireRatePercent:
                    return string.IsNullOrEmpty(effect.targetId) ? $"+{effect.value:0.#}% tower fire rate" : $"+{effect.value:0.#}% {target} fire rate";
                case UpgradeEffectType.TowerProjectileSpeedPercent:
                    return $"+{effect.value:0.#}% {target} projectile speed";
                case UpgradeEffectType.TowerAimAssistPercent:
                    return $"+{effect.value:0.#}% {target} aim assist";
                case UpgradeEffectType.TowerPierceFlat:
                    return $"+{effect.value:0} {target} pierce";
                case UpgradeEffectType.TowerDoubleShotChancePercent:
                    return $"+{effect.value:0.#}% {target} double-shot chance";
                case UpgradeEffectType.TowerSlowPercentFlat:
                    return $"+{effect.value:0.#}% {target} slow";
                case UpgradeEffectType.TowerSlowCapacityFlat:
                    return $"+{effect.value:0.#} {target} slow capacity";
                case UpgradeEffectType.TowerRangeFlat:
                    return $"+{effect.value:0.#} {target} range";
                case UpgradeEffectType.TowerHealthFlat:
                    return $"+{effect.value:0.#} {target} health";
                case UpgradeEffectType.TowerThornsDamageFlat:
                    return $"+{effect.value:0.#} {target} thorns damage";
                case UpgradeEffectType.BarracksUnitCapacityFlat:
                    return $"+{effect.value:0} {target} troop slot";
                case UpgradeEffectType.BarracksUnitDamagePercent:
                    return $"+{effect.value:0.#}% {target} troop damage";
                case UpgradeEffectType.BarracksUnitHealthPercent:
                    return $"+{effect.value:0.#}% {target} troop health";
                case UpgradeEffectType.BarracksRespawnCooldownPercent:
                    return $"-{effect.value:0.#}% {target} respawn time";
                case UpgradeEffectType.EnableTowerFire:
                    return $"Enables {target} fire damage";
                case UpgradeEffectType.TowerFireDamagePerTickFlat:
                    return $"+{effect.value:0.#} {target} fire damage per tick";
                case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                    return $"+{effect.value:0.#} {target} fire ticks per second";
                case UpgradeEffectType.TowerFireMaxStacksFlat:
                    return $"+{effect.value:0} {target} fire stacks";
                case UpgradeEffectType.TowerFireDurationFlat:
                    return $"+{effect.value:0.#}s {target} fire duration";
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                    return $"+{effect.value:0.#}% active weapon damage";
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return $"-{effect.value:0.#}% active weapon cooldown";
                case UpgradeEffectType.ActiveWeaponRadiusFlat:
                    return $"+{effect.value:0.#} active weapon radius";
                case UpgradeEffectType.ActiveWeaponPierceFlat:
                    return $"+{effect.value:0} active weapon targets";
                case UpgradeEffectType.ActiveWeaponAutoFireUnlock:
                    return "Unlocks active weapon auto-fire";
                case UpgradeEffectType.BaseLivesFlat:
                    return $"+{effect.value:0} base lives";
                case UpgradeEffectType.LevelEndKillEssenceFlat:
                    return $"+{effect.value:0} Kill Essence after each run";
                case UpgradeEffectType.UnlockEra:
                    return $"Unlocks the {target} era";
                default:
                    return $"{effect.type} +{effect.value:0.#}";
            }
        }

        private string FormatBestBotTarget(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return "all towers";
            }

            var tower = GetTowerDefinition(targetId);
            return tower != null ? tower.displayName : targetId.Replace('_', ' ');
        }

        private SkillNodeDefinition FindBestBotIncomeNode(SkillNodeDefinition[] nodes)
        {
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null || !HasUpgradeEffect(node, UpgradeEffectType.LevelEndKillEssenceFlat))
                {
                    continue;
                }

                if (progression.GetPurchasedRank(node.id) < progression.GetMaxRank(node.id) && AreUpgradePrerequisitesMet(node))
                {
                    return node;
                }
            }

            return null;
        }

        private bool CanBuyWithoutSpendingReservedIncomeCurrency(SkillNodeDefinition candidate, SkillNodeDefinition incomeNode)
        {
            var candidateCosts = progression.GetCurrentCosts(candidate.id);
            var reservedCosts = progression.GetCurrentCosts(incomeNode.id);
            for (var i = 0; i < reservedCosts.Length; i++)
            {
                var candidateAmount = 0;
                for (var j = 0; j < candidateCosts.Length; j++)
                {
                    if (candidateCosts[j].currency == reservedCosts[i].currency)
                    {
                        candidateAmount += candidateCosts[j].amount;
                    }
                }

                if (profile.GetCurrency(reservedCosts[i].currency) - candidateAmount < reservedCosts[i].amount)
                {
                    return false;
                }
            }

            return true;
        }

        private bool AreUpgradePrerequisitesMet(SkillNodeDefinition node)
        {
            if (node?.prerequisiteNodeIds == null)
            {
                return true;
            }

            for (var i = 0; i < node.prerequisiteNodeIds.Length; i++)
            {
                if (!progression.IsPurchased(node.prerequisiteNodeIds[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasUpgradeEffect(SkillNodeDefinition node, UpgradeEffectType effectType)
        {
            if (node?.effects == null)
            {
                return false;
            }

            for (var i = 0; i < node.effects.Length; i++)
            {
                if (node.effects[i].type == effectType)
                {
                    return true;
                }
            }

            return false;
        }

        private float ScoreBestBotUpgrade(SkillNodeDefinition node)
        {
            var score = 0f;
            if (node.effects != null)
            {
                for (var i = 0; i < node.effects.Length; i++)
                {
                    score += ScoreBestBotEffect(node.effects[i]);
                }
            }

            if (skillTree?.nodes != null)
            {
                for (var i = 0; i < skillTree.nodes.Length; i++)
                {
                    var candidate = skillTree.nodes[i];
                    if (candidate?.prerequisiteNodeIds == null)
                    {
                        continue;
                    }

                    for (var j = 0; j < candidate.prerequisiteNodeIds.Length; j++)
                    {
                        if (candidate.prerequisiteNodeIds[j] == node.id)
                        {
                            score += candidate.isMajorUnlock ? 18f : 4f;
                        }
                    }
                }
            }

            var weightedCost = 0f;
            var costs = progression.GetCurrentCosts(node.id);
            for (var i = 0; i < costs.Length; i++)
            {
                weightedCost += costs[i].amount * GetBestBotCurrencyWeight(costs[i].currency);
            }

            var valuePerCost = score / Mathf.Max(1f, weightedCost);
            var decisionQuality = RunningDevBestBotProfile.DecisionQuality;
            var noise = 0.15f + DeterministicBestBotNoise(node.id, progression.GetPurchasedRank(node.id)) * 1.7f;
            return valuePerCost * Mathf.Lerp(noise, 1f, decisionQuality);
        }

        private float ScoreBestBotEffect(UpgradeEffect effect)
        {
            var towerUnlocked = string.IsNullOrEmpty(effect.targetId) || progression.GetEffectTotal(UpgradeEffectType.UnlockTower, effect.targetId) > 0f;
            var towerFactor = towerUnlocked ? 1f : 0.3f;
            switch (effect.type)
            {
                case UpgradeEffectType.UnlockTower:
                    return effect.targetId == "archer" ? 180f : 120f;
                case UpgradeEffectType.PerTypeTowerLimitFlat:
                    return 55f * effect.value * towerFactor;
                case UpgradeEffectType.TowerDamageFlat:
                    return 18f * effect.value * towerFactor;
                case UpgradeEffectType.TowerDamagePercent:
                case UpgradeEffectType.TowerFireRatePercent:
                    return 2.4f * effect.value * towerFactor;
                case UpgradeEffectType.TowerFireRateFlat:
                    return 24f * effect.value * towerFactor;
                case UpgradeEffectType.TowerPierceFlat:
                    return 28f * effect.value * towerFactor;
                case UpgradeEffectType.TowerDoubleShotChancePercent:
                    return 2.2f * effect.value * towerFactor;
                case UpgradeEffectType.TowerRangeFlat:
                    return 16f * effect.value * towerFactor;
                case UpgradeEffectType.TowerProjectileSpeedPercent:
                case UpgradeEffectType.TowerAimAssistPercent:
                    return 0.7f * effect.value * towerFactor;
                case UpgradeEffectType.TowerSlowPercentFlat:
                    return 1.8f * effect.value * towerFactor;
                case UpgradeEffectType.TowerSlowCapacityFlat:
                    return 2.5f * effect.value * towerFactor;
                case UpgradeEffectType.TowerHealthFlat:
                    return 0.45f * effect.value * towerFactor;
                case UpgradeEffectType.TowerThornsDamageFlat:
                    return 8f * effect.value * towerFactor;
                case UpgradeEffectType.BarracksUnitCapacityFlat:
                    return 38f * effect.value * towerFactor;
                case UpgradeEffectType.BarracksUnitDamagePercent:
                case UpgradeEffectType.BarracksUnitHealthPercent:
                case UpgradeEffectType.BarracksRespawnCooldownPercent:
                    return 1.5f * effect.value * towerFactor;
                case UpgradeEffectType.EnableTowerFire:
                    return 70f * towerFactor;
                case UpgradeEffectType.TowerFireDamagePerTickFlat:
                    return 16f * effect.value * towerFactor;
                case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                    return 20f * effect.value * towerFactor;
                case UpgradeEffectType.TowerFireMaxStacksFlat:
                    return 15f * effect.value * towerFactor;
                case UpgradeEffectType.TowerFireDurationFlat:
                    return 7f * effect.value * towerFactor;
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                    return 3.2f * effect.value;
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return 4f * effect.value;
                case UpgradeEffectType.ActiveWeaponRadiusFlat:
                    return 42f * effect.value;
                case UpgradeEffectType.ActiveWeaponPierceFlat:
                    return 20f * effect.value;
                case UpgradeEffectType.ActiveWeaponAutoFireUnlock:
                    return 8f;
                case UpgradeEffectType.BaseLivesFlat:
                    return float.MinValue;
                case UpgradeEffectType.LevelEndKillEssenceFlat:
                    return 12f * effect.value;
                case UpgradeEffectType.UnlockEra:
                    return 35f;
                default:
                    return Mathf.Max(1f, effect.value);
            }
        }

        private static float GetBestBotCurrencyWeight(CurrencyType currency)
        {
            return currency switch
            {
                CurrencyType.KillEssence => 1f,
                CurrencyType.VictorySigil => 20f,
                CurrencyType.PerfectSigil => 24f,
                CurrencyType.ChallengeToken => 24f,
                CurrencyType.BossCore => 40f,
                _ => 1f
            };
        }

        private void RebuildBestBotTowerLayout()
        {
            towers.RemoveAll();
            var available = new List<TowerDefinition>(towers.AvailableTowers ?? Array.Empty<TowerDefinition>());
            available.Sort((left, right) => ScoreBestBotTowerDefinition(right).CompareTo(ScoreBestBotTowerDefinition(left)));
            for (var i = 0; i < available.Count; i++)
            {
                var definition = available[i];
                var limit = towers.GetPerTypeLimit(definition);
                for (var count = 0; count < limit; count++)
                {
                    if (!TryPlaceBestBotTower(definition))
                    {
                        break;
                    }
                }
            }

            SaveLayout();
        }

        private static float ScoreBestBotTowerDefinition(TowerDefinition definition)
        {
            if (definition == null)
            {
                return 0f;
            }

            return definition.behavior switch
            {
                TowerBehavior.Projectile => definition.damage / Mathf.Max(0.05f, definition.fireInterval) * (1f + definition.pierce * 0.6f) + definition.splashRadius * 6f,
                TowerBehavior.SlowAura => definition.slowPercent * definition.slowCapacity * 10f,
                TowerBehavior.Barracks => definition.barracksCapacity * definition.alliedUnitDamage / Mathf.Max(0.05f, definition.alliedUnitAttackInterval),
                TowerBehavior.Barrier => definition.health * 0.05f + definition.thornsDamage,
                _ => 1f
            };
        }

        private bool TryPlaceBestBotTower(TowerDefinition definition)
        {
            if (definition == null || path == null || path.TotalLength <= 0f)
            {
                return false;
            }

            var isBarrier = definition.behavior == TowerBehavior.Barrier;
            var sideDistances = isBarrier
                ? new[] { 0f, 0.65f, -0.65f }
                : new[] { 3.35f, -3.35f, 4.15f, -4.15f, 5.1f, -5.1f, 6.1f, -6.1f };
            var bestScore = float.MinValue;
            var bestPosition = Vector3.zero;
            var found = false;
            for (var step = 2; step <= 31; step++)
            {
                var fraction = step / 33f;
                var distance = path.TotalLength * fraction;
                var center = path.Sample(distance);
                var tangent = GetPathTangent(distance);
                var side = new Vector3(-tangent.z, 0f, tangent.x).normalized;
                for (var sideIndex = 0; sideIndex < sideDistances.Length; sideIndex++)
                {
                    var candidate = center + side * sideDistances[sideIndex];
                    candidate.y = 0f;
                    if (!towers.CanPlace(definition, candidate))
                    {
                        continue;
                    }

                    var score = ScoreBestBotTowerPosition(definition, candidate, fraction);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPosition = candidate;
                        found = true;
                    }
                }
            }

            return found && towers.TryPlace(definition, bestPosition);
        }

        private float ScoreBestBotTowerPosition(TowerDefinition definition, Vector3 candidate, float pathFraction)
        {
            if (definition.behavior == TowerBehavior.Barrier)
            {
                var barrierScore = 80f + pathFraction * 20f;
                for (var i = 0; i < towers.Towers.Count; i++)
                {
                    var distance = Vector3.Distance(towers.Towers[i].transform.position, candidate);
                    barrierScore -= Mathf.Max(0f, 9f - distance) * 9f;
                }

                return ApplyDevBestBotPlacementQuality(definition, candidate, barrierScore);
            }

            var effectiveRange = definition.behavior == TowerBehavior.Barracks
                ? Mathf.Max(definition.alliedUnitAggroRange, definition.range)
                : Mathf.Max(1f, definition.range);
            var coverageScore = 0f;
            const int samples = 72;
            for (var i = 0; i <= samples; i++)
            {
                var sampleFraction = i / (float)samples;
                var sample = path.Sample(path.TotalLength * sampleFraction);
                var distance = Vector3.Distance(candidate, sample);
                if (distance <= effectiveRange)
                {
                    coverageScore += 1f + sampleFraction * 0.18f;
                }
            }

            for (var i = 0; i < towers.Towers.Count; i++)
            {
                var distance = Vector3.Distance(towers.Towers[i].transform.position, candidate);
                coverageScore -= Mathf.Max(0f, 5.5f - distance) * 3.5f;
            }

            return ApplyDevBestBotPlacementQuality(definition, candidate, coverageScore + pathFraction * 2f);
        }

        private float ApplyDevBestBotPlacementQuality(TowerDefinition definition, Vector3 candidate, float idealScore)
        {
            var quality = RunningDevBestBotProfile.PlacementQuality;
            var positionSalt = Mathf.RoundToInt(candidate.x * 17f + candidate.z * 31f);
            var randomScore = DeterministicBestBotNoise(definition?.id, positionSalt) * 100f;
            return idealScore * quality + randomScore * (1f - quality);
        }

        public void StartLevel()
        {
            if (!IsPlanning)
            {
                return;
            }

            SaveLayout();
            var progress = profile.GetOrCreateLevelProgress(level.id);
            progress.attempts++;
            progress.testSessionAttempts++;
            progress.testSessionEquivalentAttempts += rewardTestMultiplier;
            profileStore.Save(profile);
            enemiesKilled = 0;
            killRewardMassProgress = 0f;
            CaptureRunStartCurrencies();
            lastRunCurrencyDeltas.Clear();
            activeWeapon.ResetRunStats();
            running = true;
            activeWeapon.CanFire = true;
            enemies.BeginWave(level.wave, path, level.useDataHordePrototype);
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
            killRewardMassProgress = 0f;
            running = false;
            finished = false;
            won = false;
            activeWeapon.CanFire = false;
            lastRunCurrencyDeltas.Clear();
            enemies.EnemyKilled += OnEnemyKilled;
            enemies.EnemyEscaped += OnEnemyEscaped;
        }

        public void SurrenderRun()
        {
            if (!running || finished)
            {
                return;
            }

            lives = 0;
            Finish(false);
        }

        public void AutoResolveRun()
        {
            if (finished)
            {
                return;
            }

            if (IsPlanning)
            {
                StartLevel();
            }

            if (!running)
            {
                return;
            }

            var remainingEnemies = BuildRemainingEnemySequence();
            if (remainingEnemies.Count == 0)
            {
                Finish(lives > 0);
                return;
            }

            var damageBudget = EstimateAutoResolveDamageBudget(remainingEnemies.Count);
            var simulatedKills = 0;
            var simulatedKillMass = 0f;
            for (var i = 0; i < remainingEnemies.Count; i++)
            {
                var enemy = remainingEnemies[i];
                var health = Mathf.Max(1f, enemy.maxHealth);
                if (damageBudget < health)
                {
                    break;
                }

                damageBudget -= health;
                simulatedKills++;
                simulatedKillMass += Mathf.Max(0f, enemy.mass);
            }

            enemiesKilled += simulatedKills;
            AwardKillEssenceForMass(simulatedKillMass);

            var remainingLives = lives;
            for (var i = simulatedKills; i < remainingEnemies.Count && remainingLives > 0; i++)
            {
                remainingLives -= Mathf.Max(1, remainingEnemies[i].lifeDamage);
            }

            lives = Mathf.Max(0, remainingLives);
            Finish(simulatedKills >= remainingEnemies.Count && lives > 0);
        }

        private void Finish(bool won)
        {
            finished = true;
            running = false;
            this.won = won;
            activeWeapon.CanFire = false;
            enemies.StopWave();
            rewards.ApplyLevelRewards(profile, level, won, won && lives == maxLivesForRun, rewardTestMultiplier);
            var progress = profile.GetOrCreateLevelProgress(level.id);
            if (won)
            {
                progress.bestLivesRemaining = Mathf.Max(progress.bestLivesRemaining, lives);
                progress.testSessionVictories++;
                if (progress.testSessionFirstVictoryAttempt <= 0)
                {
                    progress.testSessionFirstVictoryAttempt = progress.testSessionAttempts;
                    progress.testSessionFirstVictoryEquivalentAttempt = progress.testSessionEquivalentAttempts;
                }
            }

            var levelEndEssenceBonus = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.LevelEndKillEssenceFlat));
            if (levelEndEssenceBonus > 0)
            {
                profile.AddCurrency(CurrencyType.KillEssence, levelEndEssenceBonus * rewardTestMultiplier);
            }

            SaveLayout();
            CaptureLastRunCurrencyDeltas();
            profileStore.Save(profile);
        }

        private void CaptureDevBestBotAttempt()
        {
            var towerDamage = new Dictionary<string, float>();
            var totalTowerDamage = 0f;
            for (var i = 0; i < towers.Towers.Count; i++)
            {
                var tower = towers.Towers[i];
                if (tower?.Definition == null)
                {
                    continue;
                }

                var id = tower.Definition.id;
                towerDamage.TryGetValue(id, out var accumulated);
                towerDamage[id] = accumulated + tower.DamageDealt;
                totalTowerDamage += tower.DamageDealt;
            }

            devBestBotLastBaseDamage = Mathf.Max(0, maxLivesForRun - lives);
            devBestBotAttempts.Add(new BestBotAttemptRecord
            {
                Attempt = devBestBotAttemptCount,
                Seed = devBestBotCurrentSeed,
                Won = won,
                GameSeconds = devBestBotCurrentAttemptSeconds,
                Kills = enemiesKilled,
                LivesRemaining = lives,
                BaseDamage = devBestBotLastBaseDamage,
                TowerCount = towers.TowerCount,
                TowerDamage = totalTowerDamage,
                ActiveWeaponDamage = activeWeapon.TotalDamageDealt,
                TowerDamageById = towerDamage
            });
        }

        private string BuildDevBestBotReport(bool victory, string reason)
        {
            devBestBotPurchaseHistory = BuildDevBestBotPurchaseHistory();
            var text = new StringBuilder();
            text.AppendLine(victory ? $"{RunningDevBestBotProfile.Name.ToUpperInvariant()} BOT — LEVEL 1 CLEARED" : $"{RunningDevBestBotProfile.Name.ToUpperInvariant()} BOT — TEST STOPPED");
            text.AppendLine(reason);
            text.AppendLine($"Skill: {RunningDevBestBotProfile.Name}   Speed: {devBestBotSelectedTimeScale:0}x");
            text.AppendLine($"Attempts: {devBestBotAttemptCount}   Game time: {FormatBotDuration(devBestBotGameSeconds)}   Real time: {FormatBotDuration(devBestBotRealSeconds)}");

            BestBotAttemptRecord finalAttempt = null;
            if (devBestBotAttempts.Count > 0)
            {
                finalAttempt = devBestBotAttempts[devBestBotAttempts.Count - 1];
                text.AppendLine($"Final run: {FormatBotDuration(finalAttempt.GameSeconds)}   Seed: {finalAttempt.Seed}   Lives: {finalAttempt.LivesRemaining}");
                text.AppendLine($"Final damage: towers {finalAttempt.TowerDamage:0}   active {finalAttempt.ActiveWeaponDamage:0}   kills {finalAttempt.Kills}");
            }

            var totalTowerDamage = 0f;
            var totalActiveDamage = 0f;
            var totalKills = 0;
            var damageByTower = new Dictionary<string, float>();
            for (var i = 0; i < devBestBotAttempts.Count; i++)
            {
                var attempt = devBestBotAttempts[i];
                totalTowerDamage += attempt.TowerDamage;
                totalActiveDamage += attempt.ActiveWeaponDamage;
                totalKills += attempt.Kills;
                foreach (var entry in attempt.TowerDamageById)
                {
                    damageByTower.TryGetValue(entry.Key, out var accumulated);
                    damageByTower[entry.Key] = accumulated + entry.Value;
                }
            }

            text.AppendLine($"All attempts: {totalKills} kills   tower damage {totalTowerDamage:0}   active damage {totalActiveDamage:0}");
            if (damageByTower.Count > 0)
            {
                var towerParts = new List<string>();
                foreach (var entry in damageByTower)
                {
                    var definition = GetTowerDefinition(entry.Key);
                    towerParts.Add($"{(definition != null ? definition.displayName : entry.Key)} {entry.Value:0}");
                }

                text.AppendLine($"Tower split: {string.Join(" | ", towerParts)}");
            }

            text.AppendLine($"Purchased ranks: {devBestBotPurchases.Count}   Final currencies: {FormatBestBotCurrencies()}");
            var upgradeSpend = devBestBotTowerUpgradeSpend + devBestBotActiveUpgradeSpend + devBestBotOtherUpgradeSpend;
            var towerSpendPercent = upgradeSpend <= 0f ? 0f : devBestBotTowerUpgradeSpend / upgradeSpend * 100f;
            var activeSpendPercent = upgradeSpend <= 0f ? 0f : devBestBotActiveUpgradeSpend / upgradeSpend * 100f;
            var otherSpendPercent = upgradeSpend <= 0f ? 0f : devBestBotOtherUpgradeSpend / upgradeSpend * 100f;
            text.AppendLine($"Upgrade spend: towers {devBestBotTowerUpgradeSpend:0} ({towerSpendPercent:0}%)   active {devBestBotActiveUpgradeSpend:0} ({activeSpendPercent:0}%)   utility {devBestBotOtherUpgradeSpend:0} ({otherSpendPercent:0}%)");
            var recentStart = Mathf.Max(0, devBestBotAttempts.Count - 6);
            if (devBestBotAttempts.Count > 0)
            {
                text.AppendLine("Recent attempts:");
                for (var i = recentStart; i < devBestBotAttempts.Count; i++)
                {
                    var attempt = devBestBotAttempts[i];
                    text.AppendLine($"#{attempt.Attempt} {(attempt.Won ? "WIN" : "loss")}  {FormatBotDuration(attempt.GameSeconds)}  kills {attempt.Kills}  base damage {attempt.BaseDamage}  towers {attempt.TowerCount}");
                }
            }

            return text.ToString().TrimEnd();
        }

        private string BuildDevBestBotPurchaseHistory()
        {
            var text = new StringBuilder();
            var lastAttempt = Mathf.Max(1, devBestBotAttemptCount);
            for (var attempt = 1; attempt <= lastAttempt; attempt++)
            {
                text.AppendLine($"BEFORE ATTEMPT #{attempt}");
                var purchaseCount = 0;
                for (var i = 0; i < devBestBotPurchases.Count; i++)
                {
                    var purchase = devBestBotPurchases[i];
                    if (purchase.Attempt != attempt)
                    {
                        continue;
                    }

                    text.AppendLine($"  • {purchase.DisplayName}  {purchase.Rank}/{purchase.MaxRank}");
                    text.AppendLine($"    {purchase.EffectSummary}");
                    purchaseCount++;
                }

                if (purchaseCount == 0)
                {
                    text.AppendLine(attempt == 1 ? "  Fresh profile — no purchases" : "  No affordable purchases");
                }

                text.AppendLine();
            }

            return text.ToString().TrimEnd();
        }

        private string FormatBestBotCurrencies()
        {
            var parts = new List<string>();
            foreach (CurrencyType currency in Enum.GetValues(typeof(CurrencyType)))
            {
                var amount = profile.GetCurrency(currency);
                if (amount > 0)
                {
                    parts.Add($"{currency} {amount}");
                }
            }

            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        private void RestoreProfileAfterDevBestBot()
        {
            enemies.StopWave();
            towers.RemoveAll();
            profileStore = devBestBotOriginalProfileStore;
            profile = devBestBotOriginalProfile;
            level = devBestBotOriginalLevel;
            progression = new ProgressionService(skillTree, profile);
            path = loadLevelMap != null ? loadLevelMap(level) : path;
            enemies.SetLevelRoute(path);
            towers.Initialize(enemies, path, GetUnlockedTowers());
            rewardTestMultiplier = devBestBotOriginalRewardMultiplier;
            ApplyProgressionStats();
            towers.LoadLayout(profile.GetOrCreateLayout(level.id).placements);
            lives = maxLivesForRun;
            enemiesKilled = 0;
            killRewardMassProgress = 0f;
            running = false;
            finished = false;
            won = false;
            activeWeapon.CanFire = false;
            activeWeapon.DevAutoActiveEnabled = devBestBotOriginalAutoActive;
            activeWeapon.DevAutoEfficiency = devBestBotOriginalAutoEfficiency;
            lastRunCurrencyDeltas.Clear();
            CaptureRunStartCurrencies();
            UnityEngine.Random.state = devBestBotOriginalRandomState;
            Time.timeScale = devBestBotOriginalTimeScale;
            devBestBotRunning = false;
            devBestBotWaitingToStart = false;
            devBestBotStatus = "Finished — report ready";
            devBestBotOriginalProfileStore = null;
            devBestBotOriginalProfile = null;
            devBestBotOriginalLevel = null;
        }

        private static string FormatBotDuration(float seconds)
        {
            var totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        private void OnEnemyKilled(EnemyActor enemy)
        {
            enemiesKilled++;
            AwardKillEssenceForMass(Mathf.Max(0f, enemy?.Definition?.mass ?? 1f));
        }

        private void AwardKillEssenceForMass(float mass)
        {
            killRewardMassProgress += mass;
            var essenceReward = 0;
            while (killRewardMassProgress >= 10f)
            {
                essenceReward += 2;
                killRewardMassProgress -= 10f;
            }

            if (essenceReward > 0)
            {
                profile.AddCurrency(CurrencyType.KillEssence, essenceReward * rewardTestMultiplier);
                profileStore.Save(profile);
            }
        }

        private List<EnemyDefinition> BuildRemainingEnemySequence()
        {
            var sequence = new List<EnemyDefinition>();
            var wave = level != null ? level.wave : null;
            if (wave?.entries == null)
            {
                return sequence;
            }

            for (var i = 0; i < wave.entries.Length && sequence.Count < wave.totalEnemyCount; i++)
            {
                var entry = wave.entries[i];
                if (entry.enemy == null || entry.count <= 0)
                {
                    continue;
                }

                var count = Mathf.Min(entry.count, wave.totalEnemyCount - sequence.Count);
                for (var j = 0; j < count; j++)
                {
                    sequence.Add(entry.enemy);
                }
            }

            var resolved = Mathf.Clamp(enemies != null ? enemies.TotalResolved : 0, 0, sequence.Count);
            if (resolved > 0)
            {
                sequence.RemoveRange(0, resolved);
            }

            return sequence;
        }

        private float EstimateAutoResolveDamageBudget(int remainingEnemyCount)
        {
            var wave = level != null ? level.wave : null;
            var averageBurst = 1f;
            if (wave != null)
            {
                if (wave.randomSpawnBurstMax >= wave.randomSpawnBurstMin && wave.randomSpawnBurstMin > 0)
                {
                    averageBurst = (wave.randomSpawnBurstMin + wave.randomSpawnBurstMax) * 0.5f;
                }
                else if (wave.spawnBurstPattern != null && wave.spawnBurstPattern.Length > 0)
                {
                    var sum = 0f;
                    for (var i = 0; i < wave.spawnBurstPattern.Length; i++)
                    {
                        sum += Mathf.Max(1, wave.spawnBurstPattern[i]);
                    }

                    averageBurst = sum / wave.spawnBurstPattern.Length;
                }
            }

            var spawnDuration = wave == null ? remainingEnemyCount * 0.5f : remainingEnemyCount / Mathf.Max(1f, averageBurst) * Mathf.Max(0.05f, wave.spawnInterval);
            var pathTravelDuration = path != null ? path.TotalLength / 4.2f : 18f;
            var combatDuration = Mathf.Max(5f, spawnDuration * 0.35f + pathTravelDuration * 0.25f);
            return EstimateTowerDps() * combatDuration + EstimateActiveWeaponDps() * combatDuration;
        }

        private float EstimateTowerDps()
        {
            if (towers?.Towers == null)
            {
                return 0f;
            }

            var dps = 0f;
            foreach (var tower in towers.Towers)
            {
                var definition = tower != null ? tower.Definition : null;
                if (definition == null)
                {
                    continue;
                }

                switch (definition.behavior)
                {
                    case TowerBehavior.Projectile:
                    {
                        var shotsPerSecond = 1f / Mathf.Max(0.05f, definition.fireInterval);
                        var doubleShotMultiplier = 1f + Mathf.Clamp01(definition.doubleShotChance);
                        var pierceMultiplier = 1f + Mathf.Min(2f, Mathf.Max(0, definition.pierce) * 0.32f);
                        var splashMultiplier = definition.projectilePattern == ProjectilePattern.ArcSplash ? 1.65f : 1f;
                        var reliability = definition.canHitFlying ? 0.24f : 0.22f;
                        reliability += definition.aimAssistStrength * 0.12f;
                        reliability += Mathf.Clamp((definition.projectileSpeed - 12f) / 55f, 0f, 0.08f);
                        dps += definition.damage * shotsPerSecond * doubleShotMultiplier * pierceMultiplier * splashMultiplier * reliability;
                        break;
                    }
                    case TowerBehavior.Barracks:
                    {
                        var troops = Mathf.Max(1, definition.barracksCapacity);
                        var attackRate = 1f / Mathf.Max(0.15f, definition.alliedUnitAttackInterval);
                        dps += troops * definition.alliedUnitDamage * attackRate * 0.3f;
                        break;
                    }
                    case TowerBehavior.Barrier:
                        dps += Mathf.Max(0f, definition.thornsDamage) * 0.18f;
                        break;
                    case TowerBehavior.SlowAura:
                        dps += Mathf.Max(0f, definition.slowPercent) * 0.025f;
                        break;
                }
            }

            return dps;
        }

        private float EstimateActiveWeaponDps()
        {
            if (activeWeapon == null || !activeWeapon.AutoFireUnlocked)
            {
                return 0f;
            }

            var hitsPerShot = Mathf.Max(1, activeWeapon.MaxTargets) * Mathf.Clamp01(activeWeapon.Radius / 4.5f);
            return activeWeapon.Damage * hitsPerShot / Mathf.Max(0.1f, activeWeapon.CooldownSeconds) * 0.12f;
        }

        private void CaptureRunStartCurrencies()
        {
            runStartCurrencies.Clear();
            foreach (CurrencyType currency in System.Enum.GetValues(typeof(CurrencyType)))
            {
                runStartCurrencies[currency] = profile.GetCurrency(currency);
            }
        }

        private void CaptureLastRunCurrencyDeltas()
        {
            lastRunCurrencyDeltas.Clear();
            foreach (CurrencyType currency in System.Enum.GetValues(typeof(CurrencyType)))
            {
                runStartCurrencies.TryGetValue(currency, out var startAmount);
                var delta = profile.GetCurrency(currency) - startAmount;
                if (delta > 0)
                {
                    lastRunCurrencyDeltas[currency] = delta;
                }
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
                    var perTypeDamageFlat = progression.GetEffectTotal(UpgradeEffectType.TowerDamageFlat, towerDefinition.id);
                    var perTypeDamagePercent = progression.GetEffectTotal(UpgradeEffectType.TowerDamagePercent, towerDefinition.id);
                    var perTypeFireRateFlat = progression.GetEffectTotal(UpgradeEffectType.TowerFireRateFlat, towerDefinition.id);
                    var perTypeFireRatePercent = progression.GetEffectTotal(UpgradeEffectType.TowerFireRatePercent, towerDefinition.id);
                    var perTypeProjectileSpeedPercent = progression.GetEffectTotal(UpgradeEffectType.TowerProjectileSpeedPercent, towerDefinition.id);
                    var baseDamage = towerDefinition.damage;
                    var baseFireRate = 1f / Mathf.Max(0.01f, towerDefinition.fireInterval);
                    towerDefinition.damage = baseDamage * (1f + perTypeDamagePercent / 100f) + perTypeDamageFlat;
                    towerDefinition.fireInterval = 1f / Mathf.Max(0.01f, baseFireRate * (1f + perTypeFireRatePercent / 100f) + perTypeFireRateFlat);
                    towerDefinition.projectileSpeed *= 1f + perTypeProjectileSpeedPercent / 100f;
                    var perTypeAimAssist = Mathf.Clamp01(progression.GetEffectTotal(UpgradeEffectType.TowerAimAssistPercent, towerDefinition.id) / 100f);
                    towerDefinition.aimAssistStrength = towerDefinition.behavior == TowerBehavior.Projectile ? perTypeAimAssist : 0f;

                    towers.SetPerTypeLimitBonus(towerDefinition.id, perTypeBonus);
                    towers.SetPerTypeDamageMultiplier(towerDefinition.id, 1f);
                    towers.SetPerTypeFireRateMultiplier(towerDefinition.id, 1f);
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
            private readonly float damage;
            private readonly float fireInterval;
            private readonly float projectileSpeed;
            private readonly float health;
            private readonly float alliedUnitHealth;
            private readonly float alliedUnitDamage;
            private readonly float barracksRespawnSeconds;
            private readonly int barracksCapacity;

            public TowerBaseStats(TowerDefinition tower)
            {
                range = tower.range;
                damage = tower.damage;
                fireInterval = tower.fireInterval;
                projectileSpeed = tower.projectileSpeed;
                health = tower.health;
                alliedUnitHealth = tower.alliedUnitHealth;
                alliedUnitDamage = tower.alliedUnitDamage;
                barracksRespawnSeconds = tower.barracksRespawnSeconds;
                barracksCapacity = tower.barracksCapacity;
            }

            public void Apply(TowerDefinition tower)
            {
                tower.range = range;
                tower.damage = damage;
                tower.fireInterval = fireInterval;
                tower.projectileSpeed = projectileSpeed;
                tower.aimAssistStrength = 0f;
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

            public float Damage => damage;
            public float FireRate => 1f / Mathf.Max(0.01f, fireInterval);
            public float ProjectileSpeed => projectileSpeed;
        }

        private readonly struct DevAutoUpgradeGoal
        {
            public readonly string NodeId;
            public readonly int TargetRank;

            public DevAutoUpgradeGoal(string nodeId, int targetRank)
            {
                NodeId = nodeId;
                TargetRank = Mathf.Max(1, targetRank);
            }
        }

        private sealed class BestBotAttemptRecord
        {
            public int Attempt;
            public int Seed;
            public bool Won;
            public float GameSeconds;
            public int Kills;
            public int LivesRemaining;
            public int BaseDamage;
            public int TowerCount;
            public float TowerDamage;
            public float ActiveWeaponDamage;
            public Dictionary<string, float> TowerDamageById;
        }

        private sealed class BestBotPurchaseRecord
        {
            public int Attempt;
            public string DisplayName;
            public int Rank;
            public int MaxRank;
            public string EffectSummary;
        }

        private sealed class BestBotComparisonRecord
        {
            public string Name;
            public int Attempts;
            public float GameSeconds;
            public float RealSeconds;
            public float WinningRunSeconds;
            public int LivesRemaining;
            public float TowerDamage;
            public float ActiveDamage;
            public string PurchaseHistory;
        }

        private enum BestBotUpgradeCategory
        {
            Tower,
            ActiveWeapon,
            Other
        }

        private readonly struct BestBotSkillProfile
        {
            public readonly string Name;
            public readonly float TowerSpendTarget;
            public readonly float OtherSpendTarget;
            public readonly float ActiveWeaponEfficiency;
            public readonly float DecisionQuality;
            public readonly float PlacementQuality;
            public readonly int IncomePolicy;
            public readonly bool AllowBaseLives;

            public BestBotSkillProfile(
                string name,
                float towerSpendTarget,
                float otherSpendTarget,
                float activeWeaponEfficiency,
                float decisionQuality,
                float placementQuality,
                int incomePolicy,
                bool allowBaseLives)
            {
                Name = name;
                TowerSpendTarget = Mathf.Clamp01(towerSpendTarget);
                OtherSpendTarget = Mathf.Clamp(otherSpendTarget, 0f, 1f - TowerSpendTarget);
                ActiveWeaponEfficiency = Mathf.Clamp(activeWeaponEfficiency, 0.1f, 1f);
                DecisionQuality = Mathf.Clamp01(decisionQuality);
                PlacementQuality = Mathf.Clamp01(placementQuality);
                IncomePolicy = Mathf.Clamp(incomePolicy, 0, 2);
                AllowBaseLives = allowBaseLives;
            }
        }
    }
}
