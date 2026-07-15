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
        public IReadOnlyDictionary<CurrencyType, int> LastRunCurrencyDeltas => lastRunCurrencyDeltas;
        public IReadOnlyList<SkillNodeDefinition> UpgradeNodes => progression.GetNodes();
        public IReadOnlyList<TowerDefinition> AllTowerDefinitions => allTowerDefinitions;
        public IReadOnlyList<TowerDefinition> UnlockedTowerDefinitions => towers?.AvailableTowers ?? System.Array.Empty<TowerDefinition>();
        public float PathLength => path?.TotalLength ?? 0f;
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

        public LevelProgressRecord GetLevelProgress(string levelId = null)
        {
            return profile.GetOrCreateLevelProgress(string.IsNullOrEmpty(levelId) ? level.id : levelId);
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
            profile.GetOrCreateLevelProgress(level.id).attempts++;
            profileStore.Save(profile);
            enemiesKilled = 0;
            killRewardMassProgress = 0f;
            CaptureRunStartCurrencies();
            lastRunCurrencyDeltas.Clear();
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
            rewards.ApplyLevelRewards(profile, level, won, won && lives == maxLivesForRun);
            var progress = profile.GetOrCreateLevelProgress(level.id);
            if (won)
            {
                progress.bestLivesRemaining = Mathf.Max(progress.bestLivesRemaining, lives);
            }

            var levelEndEssenceBonus = Mathf.RoundToInt(progression.GetEffectTotal(UpgradeEffectType.LevelEndKillEssenceFlat));
            if (levelEndEssenceBonus > 0)
            {
                profile.AddCurrency(CurrencyType.KillEssence, levelEndEssenceBonus);
            }

            SaveLayout();
            CaptureLastRunCurrencyDeltas();
            profileStore.Save(profile);
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
                essenceReward++;
                killRewardMassProgress -= 10f;
            }

            if (essenceReward > 0)
            {
                profile.AddCurrency(CurrencyType.KillEssence, essenceReward);
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
            var combatDuration = Mathf.Max(12f, spawnDuration + pathTravelDuration * 0.55f);
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
                        var pierceMultiplier = 1f + Mathf.Min(3f, Mathf.Max(0, definition.pierce) * 0.45f);
                        var splashMultiplier = definition.projectilePattern == ProjectilePattern.ArcSplash ? 2.2f : 1f;
                        var reliability = definition.canHitFlying ? 0.74f : 0.68f;
                        reliability += definition.aimAssistStrength * 0.22f;
                        reliability += Mathf.Clamp((definition.projectileSpeed - 12f) / 40f, 0f, 0.18f);
                        dps += definition.damage * shotsPerSecond * doubleShotMultiplier * pierceMultiplier * splashMultiplier * reliability;
                        break;
                    }
                    case TowerBehavior.Barracks:
                    {
                        var troops = Mathf.Max(1, definition.barracksCapacity);
                        var attackRate = 1f / Mathf.Max(0.15f, definition.alliedUnitAttackInterval);
                        dps += troops * definition.alliedUnitDamage * attackRate * 0.58f;
                        break;
                    }
                    case TowerBehavior.Barrier:
                        dps += Mathf.Max(0f, definition.thornsDamage) * 0.35f;
                        break;
                    case TowerBehavior.SlowAura:
                        dps += Mathf.Max(0f, definition.slowPercent) * 0.05f;
                        break;
                }
            }

            return dps;
        }

        private float EstimateActiveWeaponDps()
        {
            if (activeWeapon == null)
            {
                return 0f;
            }

            var hitsPerShot = Mathf.Max(1, activeWeapon.MaxTargets) * Mathf.Clamp01(activeWeapon.Radius / 4.5f);
            return activeWeapon.Damage * hitsPerShot / Mathf.Max(0.1f, activeWeapon.CooldownSeconds) * 0.48f;
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
            var towerAimAssist = Mathf.Clamp01(progression.GetEffectTotal(UpgradeEffectType.TowerAimAssistPercent) / 100f);
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
                    towerDefinition.aimAssistStrength = towerDefinition.behavior == TowerBehavior.Projectile ? towerAimAssist : 0f;

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
    }
}
