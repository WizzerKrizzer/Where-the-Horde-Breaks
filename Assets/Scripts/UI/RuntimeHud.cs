using System;
using System.Text;
using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Input;
using TowerDefense.Runtime;
using TowerDefense.Save;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TowerDefense.UI
{
    public sealed class RuntimeHud : MonoBehaviour
    {
        private GameSession session;
        private PlayerInputRouter input;
        private TowerManager towers;
        private EnemyManager enemies;
        private ActiveWeaponController activeWeapon;
        private Text statusText;
        private Text fpsText;
        private Text perfText;
        private GameObject performancePanel;
        private Button performanceToggleButton;
        private bool performancePanelVisible = true;
        private readonly FrameTiming[] frameTimings = new FrameTiming[1];
        private float smoothedCpuFrameMs;
        private float smoothedGpuFrameMs;
        private float latestFps;
        private System.Diagnostics.Process currentProcess;
        private TimeSpan previousProcessCpuTime;
        private double previousProcessSampleTime;
        private float processCpuPercent;
        private float nextFpsRefreshTime;
        private float nextPerfRefreshTime;
        private float fpsAccumulatedTime;
        private int fpsAccumulatedFrames;
        private Text towerText;
        private GameObject activeWeaponSlot;
        private Image activeWeaponIcon;
        private Image activeWeaponCooldownFill;
        private Text activeWeaponCooldownText;
        private GameObject selectedTowerPanel;
        private Text selectedTowerTitle;
        private Text selectedTowerBody;
        private readonly Button[] selectedTowerTargetButtons = new Button[4];
        private Button startBattleButton;
        private Button devSpeed1Button;
        private Button devSpeed2Button;
        private Button devSpeed5Button;
        private Button devSpeed10Button;
        private Button devRewardTestingButton;
        private Button devAutoActiveButton;
        private Button devBestBotButton;
        private Button devBestBotRunAllButton;
        private Text devBestBotProfileText;
        private Button devBestBotSpeed20Button;
        private Button devBestBotSpeed30Button;
        private Button devBestBotSpeed40Button;
        private Button devBestBotSpeed50Button;
        private readonly Button[] devLoadSlotButtons = new Button[4];
        private readonly Text[] devSaveSlotStatusTexts = new Text[4];
        private Button devToggleButton;
        private Button upgradeToggleButton;
        private GameObject resultPanel;
        private Text resultTitle;
        private Text resultBody;
        private GameObject devAutoPurchasePanel;
        private Text devAutoPurchaseTitle;
        private Text devAutoPurchaseBody;
        private GameObject devBestBotReportPanel;
        private Text devBestBotReportBody;
        private Button devBestBotPurchasesButton;
        private GameObject devBestBotPurchasesDropdown;
        private RectTransform devBestBotPurchasesContent;
        private Text devBestBotPurchasesText;
        private bool devBestBotPurchasesExpanded;
        private GameObject pausePanel;
        private bool pausePanelVisible;
        private float timeScaleBeforePause = 1f;
        private GameObject upgradePanel;
        private RectTransform upgradeTreeContent;
        private RectTransform upgradeTreeViewport;
        private readonly Dictionary<string, Vector2> upgradeTreeNodePositions = new();
        private GameObject upgradeDetailPanel;
        private SkillTreeIconGraphic upgradeDetailIcon;
        private Text upgradeCurrencyText;
        private Text upgradeDetailTitle;
        private Text upgradeDetailRank;
        private Text upgradeDetailBody;
        private Text upgradeDetailCost;
        private SkillNodeDefinition selectedUpgradeNode;
        private SkillNodeDefinition hoveredUpgradeNode;
        private SkillNodeDefinition pendingHoveredUpgradeNode;
        private float pendingUpgradeHoverStartedAt;
        private readonly List<RectTransform> upgradeTreeRankBadges = new();
        private Vector2 upgradeTreePan;
        private float upgradeTreeZoom = 1f;
        private const float UpgradeHoverDelay = 0.3f;
        private const float MaximumUpgradeTreeZoom = 1.35f;
        private const float UpgradeTreeHorizontalPanFreedom = 160f;
        private const float UpgradeTreeVerticalPanFreedom = 90f;
        private GameObject devPanel;
        private bool devPanelVisible;
        private Button statsToggleButton;
        private Button codexToggleButton;
        private Button debugSpawnToggleButton;
        private GameObject debugSpawnPanel;
        private bool debugSpawnPanelVisible;
        private GameObject statsPanel;
        private readonly Dictionary<TowerDefinition, Text> statsRows = new();
        private readonly Dictionary<TowerDefinition, Button> statsRowButtons = new();
        private Text statsEmptyTowerText;
        private Button activeWeaponStatsButton;
        private bool statsPanelVisible;
        private GameObject codexPanel;
        private RectTransform codexListContent;
        private RectTransform codexDetailContent;
        private Text codexDetailText;
        private CodexSector codexSector = CodexSector.Turrets;
        private string selectedCodexId;
        private float codexScroll;
        private float codexDetailScroll;
        private bool codexListDirty = true;
        private bool codexPanelVisible;

        private enum CodexSector
        {
            Turrets,
            ActiveWeapons,
            Enemies,
            Bosses,
            Levels
        }

        public static RuntimeHud Create(GameSession gameSession, PlayerInputRouter inputRouter, TowerManager towerManager, EnemyManager enemyManager, ActiveWeaponController activeWeaponController)
        {
            var canvasObject = new GameObject("RuntimeHud");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            var hud = canvasObject.AddComponent<RuntimeHud>();
            hud.session = gameSession;
            hud.input = inputRouter;
            hud.towers = towerManager;
            hud.enemies = enemyManager;
            hud.activeWeapon = activeWeaponController;
            hud.Build(canvasObject.transform);
            return hud;
        }

        private void Build(Transform parent)
        {
            statusText = CreateText("Status", parent, new Vector2(12f, -12f), TextAnchor.UpperLeft, 13);
            statusText.GetComponent<RectTransform>().sizeDelta = new Vector2(420f, 96f);
            fpsText = CreateText("FpsCounter", parent, new Vector2(12f, -106f), TextAnchor.UpperLeft, 11);
            fpsText.GetComponent<RectTransform>().sizeDelta = new Vector2(120f, 22f);
            fpsText.text = "FPS: --";
            CreatePerformancePanel(parent);
            towerText = CreateText("TowerSelection", parent, new Vector2(12f, -332f), TextAnchor.UpperLeft, 13);
            towerText.GetComponent<RectTransform>().sizeDelta = new Vector2(340f, 178f);
            CreateActiveWeaponSlot(parent);
            CreateSelectedTowerPanel(parent);
            CreateStartBattleButton(parent);
            CreateResultPanel(parent);
            CreateDevAutoPurchasePanel(parent);
            CreateDevBestBotReportPanel(parent);
            CreatePausePanel(parent);
            CreateUpgradePanel(parent);
            CreateStatsPanel(parent);
            CreateDebugSpawnPanel(parent);
            CreateCodexPanel(parent);
            CreateDevPanel(parent);
            CreateTopRightToggles(parent);
            currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            previousProcessCpuTime = currentProcess.TotalProcessorTime;
            previousProcessSampleTime = Time.realtimeSinceStartupAsDouble;
        }

        private void Update()
        {
            if (session == null || statusText == null)
            {
                return;
            }

            HandleHudShortcuts();
            UpdateUpgradeHoverDelay();
            FrameTimingManager.CaptureFrameTimings();

            var profile = session.Profile;
            var text = new StringBuilder();
            text.AppendLine($"{session.Level.displayName}   Lives: {session.Lives}");
            if (session.Level.wave == null || session.Level.wave.totalEnemyCount <= 0)
            {
                text.AppendLine("Wave: placeholder / no enemies");
            }
            else if (session.Level.wave.useEndpointSeeking)
            {
                text.AppendLine("Wave: endpoint flow test");
            }
            text.AppendLine($"Spawned: {enemies.TotalSpawned}   Alive: {enemies.ActiveEnemyCount}");
            text.AppendLine($"{FormatCurrencyBalance(profile, CurrencyType.KillEssence)}   {FormatCurrencyBalance(profile, CurrencyType.VictorySigil)}   {FormatCurrencyBalance(profile, CurrencyType.PerfectSigil)}");
            text.AppendLine($"{FormatCurrencyBalance(profile, CurrencyType.ChallengeToken)}   {FormatCurrencyBalance(profile, CurrencyType.BossCore)}");

            if (session.Finished)
            {
                text.AppendLine(session.Won ? "VICTORY - press R to rebuild/replay" : "DEFEAT - press R to adjust towers");
            }

            statusText.text = text.ToString();
            UpdateFpsCounter();
            UpdatePerformanceCounter();
            UpdateTowerText();
            UpdateSelectedTowerPanel();
            UpdateActiveWeaponSlot();
            UpdateDevSpeedButtons();
            UpdateResultPanel();
            UpdateDevAutoPurchasePanel();
            UpdateDevBestBotReportPanel();
            UpdateStartBattleButton();
            UpdateUpgradeShortcutButton();
            UpdateUpgradePanel();
            UpdateStatsPanel();
            UpdateCodexPanel();
        }

        private void UpdateFpsCounter()
        {
            if (fpsText == null)
            {
                return;
            }

            var delta = Time.unscaledDeltaTime;
            fpsAccumulatedTime += delta;
            fpsAccumulatedFrames++;
            if (Time.realtimeSinceStartup < nextFpsRefreshTime)
            {
                return;
            }

            var fps = fpsAccumulatedTime > 0.0001f ? fpsAccumulatedFrames / fpsAccumulatedTime : 0f;
            latestFps = fps;
            fpsText.text = $"FPS: {fps:0}";
            fpsText.color = fps >= 55f
                ? new Color(0.55f, 1f, 0.6f, 0.95f)
                : fps >= 30f
                    ? new Color(1f, 0.85f, 0.35f, 0.95f)
                    : new Color(1f, 0.35f, 0.3f, 0.95f);
            nextFpsRefreshTime = Time.realtimeSinceStartup + 1f;
            fpsAccumulatedTime = 0f;
            fpsAccumulatedFrames = 0;
        }

        private void UpdatePerformanceCounter()
        {
            if (perfText == null || enemies == null)
            {
                return;
            }

            if (Time.realtimeSinceStartup < nextPerfRefreshTime)
            {
                return;
            }

            var perf = enemies.HordePerformance;
            var timingCount = FrameTimingManager.GetLatestTimings((uint)frameTimings.Length, frameTimings);
            if (timingCount > 0)
            {
                var timing = frameTimings[0];
                if (timing.cpuFrameTime > 0d)
                {
                    smoothedCpuFrameMs = Mathf.Lerp(smoothedCpuFrameMs, (float)timing.cpuFrameTime, 0.35f);
                }

                if (timing.gpuFrameTime > 0d)
                {
                    smoothedGpuFrameMs = Mathf.Lerp(smoothedGpuFrameMs, (float)timing.gpuFrameTime, 0.35f);
                }
            }

            UpdateProcessCpuUsage();
            var frameBudgetMs = Application.targetFrameRate > 0 ? 1000f / Application.targetFrameRate : 1000f / 60f;
            var cpuFrameLoad = smoothedCpuFrameMs > 0f ? smoothedCpuFrameMs / frameBudgetMs * 100f : 0f;
            var gpuFrameLoad = smoothedGpuFrameMs > 0f ? smoothedGpuFrameMs / frameBudgetMs * 100f : 0f;
            var backend = string.IsNullOrEmpty(perf.ShaderName)
                ? "Waiting for horde"
                : perf.ShaderName.Contains("GPU Compute") ? "GPU COMPUTE" : "CPU FALLBACK";
            var bottleneck = smoothedGpuFrameMs <= 0f
                ? "GPU timing unavailable"
                : smoothedCpuFrameMs > smoothedGpuFrameMs * 1.15f
                    ? "CPU limited"
                    : smoothedGpuFrameMs > smoothedCpuFrameMs * 1.15f ? "GPU limited" : "Balanced";
            var gpuTiming = smoothedGpuFrameMs > 0f
                ? $"{smoothedGpuFrameMs:0.00} ms   {gpuFrameLoad:0}% budget"
                : "not reported by graphics API";
            var overflowWarning = perf.OverflowCells > 0 || perf.DroppedAgents > 0
                ? "<color=#ff9a62>WARNING: GRID OVERFLOW — EMERGENCY PRESSURE ACTIVE</color>\n"
                : string.Empty;
            perfText.text =
                $"PERF  {latestFps:0} FPS   {backend}   {bottleneck}\n" +
                $"CPU  {smoothedCpuFrameMs:0.00} ms  {cpuFrameLoad:0}%   process {processCpuPercent:0.0}%\n" +
                $"GPU  {gpuTiming}\n" +
                $"HORDE  {enemies.ActiveEnemyCount} active   {perf.VisibleDrawn} visible\n" +
                $"GRID  {perf.MaxCellOccupancy}/48 max   {perf.OverflowCells} overflow   {perf.DroppedAgents} dropped\n" +
                overflowWarning +
                $"WORK  sim {perf.SimMs:0.00}   move {perf.MovementMs:0.00}   draw {perf.DrawMs:0.00} ms\n" +
                $"{SystemInfo.graphicsDeviceName}";
            nextPerfRefreshTime = Time.realtimeSinceStartup + 0.5f;
        }

        private void UpdateProcessCpuUsage()
        {
            if (currentProcess == null)
            {
                return;
            }

            currentProcess.Refresh();
            var now = Time.realtimeSinceStartupAsDouble;
            var elapsed = now - previousProcessSampleTime;
            if (elapsed <= 0.05d)
            {
                return;
            }

            var cpuTime = currentProcess.TotalProcessorTime;
            var cpuSeconds = (cpuTime - previousProcessCpuTime).TotalSeconds;
            processCpuPercent = Mathf.Clamp((float)(cpuSeconds / elapsed / Mathf.Max(1, Environment.ProcessorCount) * 100d), 0f, 100f);
            previousProcessCpuTime = cpuTime;
            previousProcessSampleTime = now;
        }

        private void CreatePerformancePanel(Transform parent)
        {
            performancePanel = CreatePanel("PerformancePanel", parent, new Vector2(-12f, -104f), new Vector2(390f, 142f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            if (performancePanel.TryGetComponent<Image>(out var image))
            {
                image.color = new Color(0.018f, 0.028f, 0.035f, 0.46f);
                image.raycastTarget = false;
            }

            perfText = CreateText("HordePerformance", performancePanel.transform, Vector2.zero, TextAnchor.UpperLeft, 9);
            ConfigureCenteredRect(perfText.GetComponent<RectTransform>(), new Vector2(10f, -8f), new Vector2(370f, 128f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            perfText.text = "PERFORMANCE MONITOR\nCollecting frame timings...";
            perfText.color = new Color(0.82f, 0.93f, 1f, 0.9f);
            perfText.lineSpacing = 0.9f;
        }

        private void HandleHudShortcuts()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && CloseCurrentOverlay())
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && session.IsRunning)
            {
                SetPausePanelVisible(true);
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.F3))
            {
                TogglePerformancePanel();
            }

            if (IsUpgradePanelOpen())
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab))
            {
                ToggleStatsPanel();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.U))
            {
                ShowUpgradePanel();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.G))
            {
                ToggleCodexPanel();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.BackQuote))
            {
                ToggleDevPanel();
            }
        }

        private bool CloseCurrentOverlay()
        {
            if (pausePanelVisible)
            {
                SetPausePanelVisible(false);
                return true;
            }

            if (IsUpgradePanelOpen())
            {
                SetUpgradePanelVisible(false);
                return true;
            }

            if (debugSpawnPanelVisible)
            {
                debugSpawnPanelVisible = false;
                if (debugSpawnPanel != null)
                {
                    debugSpawnPanel.SetActive(false);
                }
                return true;
            }

            if (statsPanelVisible)
            {
                statsPanelVisible = false;
                if (statsPanel != null)
                {
                    statsPanel.SetActive(false);
                }
                return true;
            }

            if (codexPanelVisible)
            {
                codexPanelVisible = false;
                if (codexPanel != null)
                {
                    codexPanel.SetActive(false);
                }
                return true;
            }

            if (devPanelVisible)
            {
                devPanelVisible = false;
                if (devPanel != null)
                {
                    devPanel.SetActive(false);
                }
                return true;
            }

            return false;
        }

        private void UpdateTowerText()
        {
            if (towerText == null || towers.AvailableTowers == null)
            {
                return;
            }

            var text = new StringBuilder();
            if (session.IsPlanning)
            {
                if (towers.AvailableTowers.Count == 0)
                {
                    text.AppendLine("No towers unlocked");
                }

                for (var i = 0; i < towers.AvailableTowers.Count; i++)
                {
                    var marker = input.Current.SelectedTowerIndex == i ? ">" : " ";
                    var tower = towers.AvailableTowers[i];
                    text.AppendLine($"{marker} {i + 1}. {tower.displayName}  {towers.CountOf(tower)}/{towers.GetPerTypeLimit(tower)}");
                }
            }
            else
            {
                text.AppendLine("Active weapon");
            }
            towerText.text = text.ToString();
        }

        private void CreateResultPanel(Transform parent)
        {
            resultPanel = CreatePanel("ResultPanel", parent, new Vector2(0f, 92f), new Vector2(470f, 194f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            resultTitle = CreateText("ResultTitle", resultPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 25);
            ConfigureCenteredRect(resultTitle.GetComponent<RectTransform>(), new Vector2(0f, 156f), new Vector2(410f, 34f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            resultBody = CreateText("ResultBody", resultPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 14);
            ConfigureCenteredRect(resultBody.GetComponent<RectTransform>(), new Vector2(0f, 96f), new Vector2(420f, 94f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            CreateButton("RetryButton", resultPanel.transform, "RETRY", new Vector2(-84f, 30f), new Vector2(124f, 28f), 13)
                .onClick.AddListener(() => session.ResetToPlanning());
            CreateButton("OpenUpgradesButton", resultPanel.transform, "UPGRADES", new Vector2(84f, 30f), new Vector2(124f, 28f), 13)
                .onClick.AddListener(ShowUpgradePanel);
            resultPanel.SetActive(false);
        }

        private void CreateDevAutoPurchasePanel(Transform parent)
        {
            devAutoPurchasePanel = CreatePanel("DevAutoPurchasePanel", parent, new Vector2(0f, 82f), new Vector2(470f, 170f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            devAutoPurchaseTitle = CreateText("DevAutoPurchaseTitle", devAutoPurchasePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 16);
            ConfigureCenteredRect(devAutoPurchaseTitle.GetComponent<RectTransform>(), new Vector2(0f, 140f), new Vector2(410f, 24f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            devAutoPurchaseTitle.text = "AUTO LOOP PURCHASES";
            devAutoPurchaseTitle.color = new Color(0.74f, 0.95f, 1f, 1f);
            devAutoPurchaseBody = CreateText("DevAutoPurchaseBody", devAutoPurchasePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(devAutoPurchaseBody.GetComponent<RectTransform>(), new Vector2(0f, 70f), new Vector2(420f, 110f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            devAutoPurchasePanel.SetActive(false);
        }

        private void CreateDevBestBotReportPanel(Transform parent)
        {
            devBestBotReportPanel = CreatePanel("DevBestBotReportPanel", parent, Vector2.zero, new Vector2(640f, 400f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            input.RegisterBlockingUiRect(devBestBotReportPanel.GetComponent<RectTransform>());
            var title = CreateText("DevBestBotReportTitle", devBestBotReportPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 20);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, 368f), new Vector2(590f, 30f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            title.text = "BOT REPORT";
            title.color = new Color(0.72f, 1f, 0.66f, 1f);
            devBestBotReportBody = CreateText("DevBestBotReportBody", devBestBotReportPanel.transform, Vector2.zero, TextAnchor.UpperLeft, 12);
            ConfigureCenteredRect(devBestBotReportBody.GetComponent<RectTransform>(), new Vector2(0f, 198f), new Vector2(590f, 310f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            devBestBotReportBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            devBestBotReportBody.verticalOverflow = VerticalWrapMode.Truncate;
            devBestBotPurchasesButton = CreateAnchoredButton("ToggleDevBestBotPurchases", devBestBotReportPanel.transform, "PURCHASES ▼", new Vector2(-92f, 22f), new Vector2(158f, 28f), new Vector2(0.5f, 0f), 12);
            devBestBotPurchasesButton.onClick.AddListener(ToggleDevBestBotPurchaseHistory);
            CreateAnchoredButton("CloseDevBestBotReport", devBestBotReportPanel.transform, "CLOSE", new Vector2(92f, 22f), new Vector2(158f, 28f), new Vector2(0.5f, 0f), 12)
                .onClick.AddListener(() =>
                {
                    devBestBotPurchasesExpanded = false;
                    session.DismissDevBestBotReport();
                });

            devBestBotPurchasesDropdown = CreatePanel("DevBestBotPurchasesDropdown", devBestBotReportPanel.transform, new Vector2(0f, 48f), new Vector2(600f, 300f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            var purchaseTitle = CreateText("DevBestBotPurchasesTitle", devBestBotPurchasesDropdown.transform, Vector2.zero, TextAnchor.MiddleCenter, 14);
            ConfigureCenteredRect(purchaseTitle.GetComponent<RectTransform>(), new Vector2(0f, 276f), new Vector2(560f, 24f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            purchaseTitle.text = "PURCHASE HISTORY BY ATTEMPT";
            purchaseTitle.color = new Color(1f, 0.76f, 0.28f, 1f);

            var viewport = new GameObject("DevBestBotPurchasesViewport");
            viewport.transform.SetParent(devBestBotPurchasesDropdown.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            ConfigureCenteredRect(viewportRect, new Vector2(0f, 139f), new Vector2(560f, 242f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            viewport.AddComponent<RectMask2D>();
            var viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0.015f, 0.022f, 0.028f, 0.88f);

            var contentObject = new GameObject("DevBestBotPurchasesContent");
            contentObject.transform.SetParent(viewport.transform, false);
            devBestBotPurchasesContent = contentObject.AddComponent<RectTransform>();
            devBestBotPurchasesContent.anchorMin = new Vector2(0f, 1f);
            devBestBotPurchasesContent.anchorMax = new Vector2(1f, 1f);
            devBestBotPurchasesContent.pivot = new Vector2(0.5f, 1f);
            devBestBotPurchasesContent.anchoredPosition = Vector2.zero;
            devBestBotPurchasesContent.sizeDelta = new Vector2(0f, 242f);

            devBestBotPurchasesText = CreateText("DevBestBotPurchasesText", devBestBotPurchasesContent, Vector2.zero, TextAnchor.UpperLeft, 11);
            var purchaseTextRect = devBestBotPurchasesText.GetComponent<RectTransform>();
            purchaseTextRect.anchorMin = new Vector2(0f, 1f);
            purchaseTextRect.anchorMax = new Vector2(1f, 1f);
            purchaseTextRect.pivot = new Vector2(0.5f, 1f);
            purchaseTextRect.anchoredPosition = new Vector2(0f, -8f);
            purchaseTextRect.sizeDelta = new Vector2(-20f, 226f);
            devBestBotPurchasesText.horizontalOverflow = HorizontalWrapMode.Wrap;
            devBestBotPurchasesText.verticalOverflow = VerticalWrapMode.Overflow;

            var purchaseScroll = devBestBotPurchasesDropdown.AddComponent<ScrollRect>();
            purchaseScroll.viewport = viewportRect;
            purchaseScroll.content = devBestBotPurchasesContent;
            purchaseScroll.horizontal = false;
            purchaseScroll.vertical = true;
            purchaseScroll.movementType = ScrollRect.MovementType.Clamped;
            purchaseScroll.scrollSensitivity = 28f;
            devBestBotPurchasesDropdown.SetActive(false);
            devBestBotReportPanel.SetActive(false);
        }

        private void CreatePausePanel(Transform parent)
        {
            pausePanel = CreatePanel("PausePanel", parent, new Vector2(0f, 124f), new Vector2(300f, 150f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            var title = CreateText("PauseTitle", pausePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 22);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, 118f), new Vector2(240f, 28f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            title.text = "PAUSED";
            var surrenderButton = CreateButton("SurrenderButton", pausePanel.transform, "SURRENDER", new Vector2(0f, 78f), new Vector2(132f, 28f), 13);
            surrenderButton.onClick.AddListener(SurrenderFromPause);
            var resumeButton = CreateButton("ResumeButton", pausePanel.transform, "RESUME", new Vector2(0f, 44f), new Vector2(132f, 28f), 13);
            resumeButton.onClick.AddListener(() => SetPausePanelVisible(false));
            var hint = CreateText("PauseHint", pausePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(hint.GetComponent<RectTransform>(), new Vector2(0f, 16f), new Vector2(250f, 22f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            hint.text = "Escape resumes the battle.";
            input.RegisterBlockingUiRect(pausePanel.GetComponent<RectTransform>());
            pausePanel.SetActive(false);
        }

        private void CreateUpgradePanel(Transform parent)
        {
            upgradePanel = CreatePanel("UpgradePanel", parent, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var panelRect = upgradePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            upgradePanel.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.024f, 1f);

            var title = CreateText("UpgradeTitle", upgradePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 22);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, -22f), new Vector2(460f, 32f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            title.text = "SKILL TREE";

            upgradeCurrencyText = CreateText("UpgradeCurrencies", upgradePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 13);
            ConfigureCenteredRect(upgradeCurrencyText.GetComponent<RectTransform>(), new Vector2(0f, -50f), new Vector2(680f, 24f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));

            var hint = CreateText("UpgradeHint", upgradePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(hint.GetComponent<RectTransform>(), new Vector2(0f, -72f), new Vector2(680f, 20f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            hint.color = new Color(0.68f, 0.78f, 0.86f, 1f);
            hint.text = "Hover for 0.3 seconds to inspect. Click a node to buy its next rank. Drag to pan. Mouse wheel zooms.";

            var viewport = CreatePanel("UpgradeTreeViewport", upgradePanel.transform, new Vector2(0f, -94f), Vector2.zero, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            upgradeTreeViewport = viewport.GetComponent<RectTransform>();
            upgradeTreeViewport.anchorMin = new Vector2(0f, 0f);
            upgradeTreeViewport.anchorMax = new Vector2(1f, 1f);
            upgradeTreeViewport.offsetMin = new Vector2(36f, 54f);
            upgradeTreeViewport.offsetMax = new Vector2(-36f, -92f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var treeInput = viewport.AddComponent<SkillTreeViewportInput>();
            treeInput.Initialize(OnUpgradeTreeDragged, OnUpgradeTreeScrolled);

            var contentObject = new GameObject("UpgradeTreeContent");
            contentObject.transform.SetParent(viewport.transform, false);
            upgradeTreeContent = contentObject.AddComponent<RectTransform>();
            upgradeTreeContent.anchorMin = new Vector2(0.5f, 0.5f);
            upgradeTreeContent.anchorMax = new Vector2(0.5f, 0.5f);
            upgradeTreeContent.pivot = new Vector2(0.5f, 0.5f);
            var nodes = session.UpgradeNodes;
            BuildUpgradeTreeLayout(nodes);
            upgradeTreeContent.sizeDelta = CalculateUpgradeTreeContentSize(nodes, upgradeTreeNodePositions);
            upgradeTreePan = Vector2.zero;
            upgradeTreeZoom = 1f;

            CreateUpgradeLinks(upgradeTreeContent, nodes);
            for (var i = 0; i < nodes.Count; i++)
            {
                CreateUpgradeNode(upgradeTreeContent, nodes[i]);
            }
            ApplyUpgradeTreeTransform();

            upgradeDetailPanel = CreatePanel("UpgradeDetails", upgradePanel.transform, new Vector2(-14f, 44f), new Vector2(300f, 124f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            var upgradeDetailImage = upgradeDetailPanel.GetComponent<Image>();
            upgradeDetailImage.color = new Color(0.018f, 0.035f, 0.052f, 0.96f);
            upgradeDetailImage.raycastTarget = false;
            var detailOutline = upgradeDetailPanel.AddComponent<Outline>();
            detailOutline.effectColor = new Color(0.2f, 0.65f, 0.88f, 0.78f);
            detailOutline.effectDistance = new Vector2(2f, -2f);
            var detailIconObject = new GameObject("DetailIcon");
            detailIconObject.transform.SetParent(upgradeDetailPanel.transform, false);
            var detailIconRect = detailIconObject.AddComponent<RectTransform>();
            ConfigureCenteredRect(detailIconRect, new Vector2(-131f, 47f), new Vector2(24f, 24f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            upgradeDetailIcon = detailIconObject.AddComponent<SkillTreeIconGraphic>();
            upgradeDetailIcon.color = new Color(0.72f, 0.93f, 1f, 1f);
            upgradeDetailIcon.raycastTarget = false;
            upgradeDetailTitle = CreateText("DetailTitle", upgradeDetailPanel.transform, Vector2.zero, TextAnchor.MiddleLeft, 11);
            upgradeDetailTitle.raycastTarget = false;
            upgradeDetailTitle.fontStyle = FontStyle.Bold;
            ConfigureCenteredRect(upgradeDetailTitle.GetComponent<RectTransform>(), new Vector2(0f, 47f), new Vector2(218f, 22f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            upgradeDetailRank = CreateText("DetailRank", upgradeDetailPanel.transform, Vector2.zero, TextAnchor.MiddleRight, 10);
            upgradeDetailRank.raycastTarget = false;
            upgradeDetailRank.fontStyle = FontStyle.Bold;
            upgradeDetailRank.color = new Color(0.72f, 0.93f, 1f, 1f);
            ConfigureCenteredRect(upgradeDetailRank.GetComponent<RectTransform>(), new Vector2(128f, 47f), new Vector2(40f, 22f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

            var detailDivider = CreateImage("DetailDivider", upgradeDetailPanel.transform, new Vector2(0f, 31f), new Vector2(276f, 1f), new Color(0.78f, 0.88f, 0.94f, 0.9f));
            detailDivider.raycastTarget = false;
            upgradeDetailBody = CreateText("DetailBody", upgradeDetailPanel.transform, Vector2.zero, TextAnchor.MiddleLeft, 10);
            upgradeDetailBody.raycastTarget = false;
            upgradeDetailBody.color = new Color(0.82f, 0.89f, 0.94f, 1f);
            upgradeDetailBody.verticalOverflow = VerticalWrapMode.Truncate;
            ConfigureCenteredRect(upgradeDetailBody.GetComponent<RectTransform>(), new Vector2(0f, 3f), new Vector2(272f, 44f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            upgradeDetailCost = CreateText("DetailCost", upgradeDetailPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            upgradeDetailCost.raycastTarget = false;
            upgradeDetailCost.fontStyle = FontStyle.Bold;
            ConfigureCenteredRect(upgradeDetailCost.GetComponent<RectTransform>(), new Vector2(0f, -48f), new Vector2(210f, 22f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            upgradeDetailPanel.SetActive(false);

            CreateAnchoredButton("ResetUpgradeButton", upgradePanel.transform, "RESET", new Vector2(-140f, 18f), new Vector2(120f, 28f), new Vector2(0.5f, 0f), 13)
                .onClick.AddListener(() => session.RefundAndResetUpgrades());
            CreateAnchoredButton("BuyAllUpgradeButton", upgradePanel.transform, "BUY ALL", new Vector2(0f, 18f), new Vector2(120f, 28f), new Vector2(0.5f, 0f), 13)
                .onClick.AddListener(BuyAllAffordableUpgrades);
            CreateAnchoredButton("CloseUpgradeButton", upgradePanel.transform, "BACK", new Vector2(140f, 18f), new Vector2(120f, 28f), new Vector2(0.5f, 0f), 13)
                .onClick.AddListener(() => SetUpgradePanelVisible(false));

            input.RegisterBlockingUiRect(upgradePanel.GetComponent<RectTransform>());
            upgradePanel.SetActive(false);
        }

        private void CreateUpgradeLinks(Transform parent, IReadOnlyList<SkillNodeDefinition> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.prerequisiteNodeIds == null)
                {
                    continue;
                }

                foreach (var prerequisiteId in node.prerequisiteNodeIds)
                {
                    var prerequisite = FindNode(nodes, prerequisiteId);
                    if (prerequisite != null)
                    {
                        CreateUpgradeLink(parent, prerequisite, node);
                    }
                }
            }
        }

        private static Vector2 CalculateUpgradeTreeContentSize(IReadOnlyList<SkillNodeDefinition> nodes, IReadOnlyDictionary<string, Vector2> positions)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return new Vector2(900f, 580f);
            }

            var maxAbsX = 0f;
            var maxAbsY = 0f;
            for (var i = 0; i < nodes.Count; i++)
            {
                var position = positions != null && positions.TryGetValue(nodes[i].id, out var resolved)
                    ? resolved
                    : nodes[i].radialPosition;
                maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(position.x));
                maxAbsY = Mathf.Max(maxAbsY, Mathf.Abs(position.y));
            }

            return new Vector2(
                Mathf.Max(900f, maxAbsX * 2f + 360f),
                Mathf.Max(580f, maxAbsY * 2f + 300f));
        }

        private void BuildUpgradeTreeLayout(IReadOnlyList<SkillNodeDefinition> nodes)
        {
            upgradeTreeNodePositions.Clear();
            upgradeTreeRankBadges.Clear();
            if (nodes == null)
            {
                return;
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                upgradeTreeNodePositions[nodes[i].id] = nodes[i].radialPosition * 1.22f;
            }

            // Authored positions define the overall branches. This small deterministic
            // relaxation only resolves local collisions, including a few nearly
            // identical positions in the original data, without turning the tree into
            // an unpredictable force-directed graph.
            for (var iteration = 0; iteration < 80; iteration++)
            {
                var changed = false;
                for (var a = 0; a < nodes.Count; a++)
                {
                    for (var b = a + 1; b < nodes.Count; b++)
                    {
                        var first = nodes[a];
                        var second = nodes[b];
                        var firstPosition = upgradeTreeNodePositions[first.id];
                        var secondPosition = upgradeTreeNodePositions[second.id];
                        var delta = secondPosition - firstPosition;
                        var distance = delta.magnitude;
                        var minimumDistance = first.isMajorUnlock || second.isMajorUnlock ? 112f : 94f;
                        if (distance >= minimumDistance)
                        {
                            continue;
                        }

                        if (distance < 0.001f)
                        {
                            var angle = StableTreeAngle(first.id, second.id);
                            delta = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                            distance = 1f;
                        }

                        var correction = delta / distance * ((minimumDistance - distance) * 0.52f);
                        var firstPinned = first.startsUnlocked || first.radialPosition.sqrMagnitude < 0.01f;
                        var secondPinned = second.startsUnlocked || second.radialPosition.sqrMagnitude < 0.01f;
                        if (!firstPinned && !secondPinned)
                        {
                            upgradeTreeNodePositions[first.id] = firstPosition - correction * 0.5f;
                            upgradeTreeNodePositions[second.id] = secondPosition + correction * 0.5f;
                        }
                        else if (firstPinned && !secondPinned)
                        {
                            upgradeTreeNodePositions[second.id] = secondPosition + correction;
                        }
                        else if (!firstPinned)
                        {
                            upgradeTreeNodePositions[first.id] = firstPosition - correction;
                        }
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        private Vector2 GetUpgradeTreeNodePosition(SkillNodeDefinition node)
        {
            return node != null && upgradeTreeNodePositions.TryGetValue(node.id, out var position)
                ? position
                : node != null ? node.radialPosition : Vector2.zero;
        }

        private static float StableTreeAngle(string first, string second)
        {
            unchecked
            {
                var hash = 17;
                var text = (first ?? string.Empty) + ":" + (second ?? string.Empty);
                for (var i = 0; i < text.Length; i++)
                {
                    hash = hash * 31 + text[i];
                }
                return Mathf.Abs(hash % 6283) * 0.001f;
            }
        }

        private void CreateUpgradeLink(Transform parent, SkillNodeDefinition prerequisite, SkillNodeDefinition target)
        {
            var from = GetUpgradeTreeNodePosition(prerequisite);
            var to = GetUpgradeTreeNodePosition(target);
            var delta = to - from;
            if (delta.sqrMagnitude < 0.001f)
            {
                return;
            }

            var direction = delta.normalized;
            from += direction * (prerequisite.isMajorUnlock ? 42f : 34f);
            to -= direction * (target.isMajorUnlock ? 42f : 34f);
            delta = to - from;
            var image = CreateImage($"Link_{target.id}", parent, (from + to) * 0.5f, new Vector2(Mathf.Max(1f, delta.magnitude), 3f), new Color(0.15f, 0.75f, 1f, 0.65f));
            image.raycastTarget = false;
            var go = image.gameObject;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private void CreateUpgradeNode(Transform parent, SkillNodeDefinition node)
        {
            var position = GetUpgradeTreeNodePosition(node);
            var size = node.isMajorUnlock ? new Vector2(76f, 76f) : new Vector2(60f, 60f);
            var button = CreateAnchoredButton($"Node_{node.id}", parent, string.Empty, position, size, new Vector2(0.5f, 0.5f), 10);
            button.onClick.AddListener(() => PurchaseUpgradeNode(node));
            var nodeOutline = button.gameObject.AddComponent<Outline>();
            nodeOutline.effectColor = new Color(0.18f, 0.67f, 0.9f, 0.72f);
            nodeOutline.effectDistance = node.isMajorUnlock ? new Vector2(3f, -3f) : new Vector2(2f, -2f);

            var label = button.GetComponentInChildren<Text>();
            label.gameObject.name = "Rank";
            label.fontStyle = FontStyle.Bold;
            label.fontSize = node.isMajorUnlock ? 13 : 12;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            var rankTextOutline = label.gameObject.AddComponent<Outline>();
            rankTextOutline.effectColor = new Color(0.015f, 0.055f, 0.085f, 0.98f);
            rankTextOutline.effectDistance = new Vector2(1.25f, -1.25f);

            var rankBadgeObject = new GameObject("RankBadge");
            rankBadgeObject.transform.SetParent(button.transform, false);
            var rankBadgeRect = rankBadgeObject.AddComponent<RectTransform>();
            ConfigureCenteredRect(
                rankBadgeRect,
                new Vector2(0f, node.isMajorUnlock ? -42f : -34f),
                new Vector2(node.isMajorUnlock ? 38f : 34f, 16f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f));
            label.transform.SetParent(rankBadgeObject.transform, false);
            var rankLabelRect = label.GetComponent<RectTransform>();
            rankLabelRect.anchorMin = Vector2.zero;
            rankLabelRect.anchorMax = Vector2.one;
            rankLabelRect.offsetMin = Vector2.zero;
            rankLabelRect.offsetMax = Vector2.zero;
            upgradeTreeRankBadges.Add(rankBadgeRect);

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(button.transform, false);
            var iconRect = iconObject.AddComponent<RectTransform>();
            ConfigureCenteredRect(
                iconRect,
                new Vector2(0f, node.isMajorUnlock ? 5f : 4f),
                node.isMajorUnlock ? new Vector2(46f, 46f) : new Vector2(36f, 36f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f));
            var icon = iconObject.AddComponent<SkillTreeIconGraphic>();
            icon.Kind = ResolveSkillTreeIcon(node);
            icon.color = new Color(0.82f, 0.95f, 1f, 1f);
            icon.raycastTarget = false;

            var events = button.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => HoverUpgradeNode(node));
            events.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => StopHoveringUpgradeNode(node));
            events.triggers.Add(exit);

            // Buttons normally consume pointer input before it reaches the viewport.
            // Forward drag and wheel input from every node so inspecting a node never
            // locks tree navigation.
            var nodeTreeInput = button.gameObject.AddComponent<SkillTreeViewportInput>();
            nodeTreeInput.Initialize(OnUpgradeTreeDragged, OnUpgradeTreeScrolled);
        }

        private void ShowUpgradePanel()
        {
            if (session.IsRunning)
            {
                return;
            }

            SetUpgradePanelVisible(true);
            ApplyUpgradeTreeTransform();
            ClearUpgradeDetails();
        }

        private void HoverUpgradeNode(SkillNodeDefinition node)
        {
            pendingHoveredUpgradeNode = node;
            pendingUpgradeHoverStartedAt = Time.unscaledTime;
        }

        private void StopHoveringUpgradeNode(SkillNodeDefinition node)
        {
            if (pendingHoveredUpgradeNode == node)
            {
                pendingHoveredUpgradeNode = null;
            }

            if (hoveredUpgradeNode == node)
            {
                hoveredUpgradeNode = null;
                UpdateSelectedUpgradeDetails();
            }
        }

        private void UpdateUpgradeHoverDelay()
        {
            if (pendingHoveredUpgradeNode == null || upgradePanel == null || !upgradePanel.activeSelf)
            {
                return;
            }

            if (Time.unscaledTime - pendingUpgradeHoverStartedAt < UpgradeHoverDelay)
            {
                return;
            }

            hoveredUpgradeNode = pendingHoveredUpgradeNode;
            pendingHoveredUpgradeNode = null;
            UpdateSelectedUpgradeDetails();
        }

        private void ClearUpgradeDetails(SkillNodeDefinition node = null)
        {
            if (node != null && selectedUpgradeNode != node && hoveredUpgradeNode != node)
            {
                return;
            }

            selectedUpgradeNode = null;
            hoveredUpgradeNode = null;
            pendingHoveredUpgradeNode = null;
            if (upgradeDetailPanel != null)
            {
                upgradeDetailPanel.SetActive(false);
            }

            if (upgradeDetailTitle != null)
            {
                upgradeDetailTitle.text = string.Empty;
            }

            if (upgradeDetailBody != null)
            {
                upgradeDetailBody.text = string.Empty;
            }

            if (upgradeDetailRank != null)
            {
                upgradeDetailRank.text = string.Empty;
            }

            if (upgradeDetailCost != null)
            {
                upgradeDetailCost.text = string.Empty;
            }
        }

        private void PurchaseUpgradeNode(SkillNodeDefinition node)
        {
            selectedUpgradeNode = null;
            pendingHoveredUpgradeNode = null;
            hoveredUpgradeNode = node;
            if (session.TryPurchaseUpgrade(node.id))
            {
                UpdateUpgradePanel();
            }

            UpdateSelectedUpgradeDetails();
        }

        private void BuyAllAffordableUpgrades()
        {
            var nodes = session.UpgradeNodes;
            var remainingRanks = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                remainingRanks += Mathf.Max(0, session.GetUpgradeMaxRank(nodes[i].id) - session.GetUpgradeRank(nodes[i].id));
            }

            var purchasedRanks = 0;
            var purchasedDuringPass = true;
            while (purchasedDuringPass && purchasedRanks < remainingRanks)
            {
                purchasedDuringPass = false;
                for (var i = 0; i < nodes.Count && purchasedRanks < remainingRanks; i++)
                {
                    var node = nodes[i];
                    if (!session.CanPurchaseUpgrade(node.id) || !session.TryPurchaseUpgrade(node.id))
                    {
                        continue;
                    }

                    purchasedRanks++;
                    purchasedDuringPass = true;
                }
            }

            UpdateUpgradePanel();
            UpdateSelectedUpgradeDetails();
        }

        private void OnUpgradeTreeDragged(Vector2 delta)
        {
            upgradeTreePan += delta;
            ApplyUpgradeTreeTransform();
        }

        private void OnUpgradeTreeScrolled(float scrollDelta)
        {
            var previousZoom = upgradeTreeZoom;
            upgradeTreeZoom = Mathf.Clamp(upgradeTreeZoom + scrollDelta * 0.12f, GetMinimumUpgradeTreeZoom(), MaximumUpgradeTreeZoom);
            if (!Mathf.Approximately(previousZoom, upgradeTreeZoom))
            {
                ApplyUpgradeTreeTransform();
            }
        }

        private void ApplyUpgradeTreeTransform()
        {
            if (upgradeTreeContent == null)
            {
                return;
            }

            upgradeTreeZoom = Mathf.Clamp(upgradeTreeZoom, GetMinimumUpgradeTreeZoom(), MaximumUpgradeTreeZoom);
            upgradeTreePan = ClampUpgradeTreePan(upgradeTreePan);
            upgradeTreeContent.anchoredPosition = upgradeTreePan;
            upgradeTreeContent.localScale = Vector3.one * upgradeTreeZoom;

            var badgeScale = Mathf.Clamp(1f / upgradeTreeZoom, 1f, 2.4f);
            for (var i = 0; i < upgradeTreeRankBadges.Count; i++)
            {
                if (upgradeTreeRankBadges[i] != null)
                {
                    upgradeTreeRankBadges[i].localScale = Vector3.one * badgeScale;
                }
            }
        }

        private Vector2 ClampUpgradeTreePan(Vector2 pan)
        {
            if (upgradeTreeViewport == null || upgradeTreeContent == null)
            {
                return pan;
            }

            var viewportSize = upgradeTreeViewport.rect.size;
            var contentSize = upgradeTreeContent.rect.size * upgradeTreeZoom;
            var maxPanX = Mathf.Max(UpgradeTreeHorizontalPanFreedom, (contentSize.x - viewportSize.x) * 0.5f + UpgradeTreeHorizontalPanFreedom);
            var maxPanY = Mathf.Max(UpgradeTreeVerticalPanFreedom, (contentSize.y - viewportSize.y) * 0.5f + UpgradeTreeVerticalPanFreedom);
            return new Vector2(
                Mathf.Clamp(pan.x, -maxPanX, maxPanX),
                Mathf.Clamp(pan.y, -maxPanY, maxPanY));
        }

        private float GetMinimumUpgradeTreeZoom()
        {
            if (upgradeTreeViewport == null || upgradeTreeContent == null)
            {
                return 0.35f;
            }

            var viewportSize = upgradeTreeViewport.rect.size;
            var contentSize = upgradeTreeContent.rect.size;
            if (viewportSize.x <= 0f || viewportSize.y <= 0f || contentSize.x <= 0f || contentSize.y <= 0f)
            {
                return 0.35f;
            }

            var fitX = viewportSize.x / contentSize.x;
            var fitY = viewportSize.y / contentSize.y;
            return Mathf.Clamp(Mathf.Min(fitX, fitY) * 0.92f, 0.25f, 0.55f);
        }

        private void SetUpgradePanelVisible(bool visible)
        {
            if (upgradePanel != null)
            {
                upgradePanel.SetActive(visible);
            }

            if (input != null)
            {
                input.GameplayInputBlocked = visible;
            }

            SetMainHudVisible(!visible);

            if (devPanel != null)
            {
                devPanel.SetActive(!visible && devPanelVisible);
            }

            if (statsPanel != null)
            {
                statsPanel.SetActive(!visible && statsPanelVisible);
            }

            if (codexPanel != null)
            {
                codexPanel.SetActive(!visible && codexPanelVisible);
            }

            if (debugSpawnPanel != null)
            {
                debugSpawnPanel.SetActive(!visible && debugSpawnPanelVisible);
            }
        }

        private void SetPausePanelVisible(bool visible)
        {
            if (pausePanel == null)
            {
                return;
            }

            if (visible && !pausePanelVisible)
            {
                timeScaleBeforePause = Mathf.Approximately(Time.timeScale, 0f) ? 1f : Time.timeScale;
                Time.timeScale = 0f;
            }
            else if (!visible && pausePanelVisible)
            {
                Time.timeScale = timeScaleBeforePause <= 0f ? 1f : timeScaleBeforePause;
            }

            pausePanelVisible = visible;
            pausePanel.SetActive(visible);
            if (input != null)
            {
                input.GameplayInputBlocked = visible || IsUpgradePanelOpen();
            }
        }

        private void SurrenderFromPause()
        {
            var restoreScale = timeScaleBeforePause <= 0f ? 1f : timeScaleBeforePause;
            pausePanelVisible = false;
            if (pausePanel != null)
            {
                pausePanel.SetActive(false);
            }

            Time.timeScale = restoreScale;
            if (input != null)
            {
                input.GameplayInputBlocked = IsUpgradePanelOpen();
            }

            session.SurrenderRun();
        }

        private void SetMainHudVisible(bool visible)
        {
            if (statusText != null)
            {
                statusText.gameObject.SetActive(visible);
            }

            if (towerText != null)
            {
                towerText.gameObject.SetActive(visible);
            }

            if (activeWeaponSlot != null)
            {
                activeWeaponSlot.SetActive(visible);
            }

            if (selectedTowerPanel != null)
            {
                selectedTowerPanel.SetActive(visible && towers.SelectedTower != null);
            }

            if (resultPanel != null)
            {
                resultPanel.SetActive(visible && session.Finished);
            }

            if (startBattleButton != null)
            {
                startBattleButton.gameObject.SetActive(visible && session.IsPlanning);
            }

            if (statsToggleButton != null)
            {
                statsToggleButton.gameObject.SetActive(visible);
            }

            if (debugSpawnToggleButton != null)
            {
                debugSpawnToggleButton.gameObject.SetActive(visible);
            }

            if (codexToggleButton != null)
            {
                codexToggleButton.gameObject.SetActive(visible);
            }

            if (upgradeToggleButton != null)
            {
                upgradeToggleButton.gameObject.SetActive(visible && !session.IsRunning);
            }

            if (devToggleButton != null)
            {
                devToggleButton.gameObject.SetActive(visible);
            }
        }

        private void UpdateUpgradeShortcutButton()
        {
            if (upgradeToggleButton == null || IsUpgradePanelOpen())
            {
                return;
            }

            upgradeToggleButton.gameObject.SetActive(!session.IsRunning);
        }

        private void CreateSelectedTowerPanel(Transform parent)
        {
            selectedTowerPanel = CreatePanel("SelectedTowerPanel", parent, new Vector2(12f, 18f), new Vector2(286f, 158f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            var image = selectedTowerPanel.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.02f, 0.025f, 0.03f, 0.74f);
            }

            selectedTowerTitle = CreateText("SelectedTowerTitle", selectedTowerPanel.transform, Vector2.zero, TextAnchor.MiddleLeft, 13);
            ConfigureCenteredRect(selectedTowerTitle.GetComponent<RectTransform>(), new Vector2(12f, -12f), new Vector2(260f, 22f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            selectedTowerBody = CreateText("SelectedTowerBody", selectedTowerPanel.transform, Vector2.zero, TextAnchor.UpperLeft, 11);
            ConfigureCenteredRect(selectedTowerBody.GetComponent<RectTransform>(), new Vector2(12f, -38f), new Vector2(260f, 78f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            CreateTargetingButton(0, TowerTargetingMode.First, "FIRST", new Vector2(46f, -130f));
            CreateTargetingButton(1, TowerTargetingMode.Last, "LAST", new Vector2(111f, -130f));
            CreateTargetingButton(2, TowerTargetingMode.Closest, "CLOSE", new Vector2(176f, -130f));
            CreateTargetingButton(3, TowerTargetingMode.HighestHealth, "STRONG", new Vector2(241f, -130f));
            selectedTowerPanel.SetActive(false);
        }

        private void CreateTargetingButton(int index, TowerTargetingMode mode, string label, Vector2 position)
        {
            var button = CreateAnchoredButton($"Targeting_{mode}", selectedTowerPanel.transform, label, position, new Vector2(58f, 22f), new Vector2(0f, 1f), 9);
            button.onClick.AddListener(() => session.SetSelectedTowerTargeting(mode));
            selectedTowerTargetButtons[index] = button;
        }

        private void UpdateSelectedTowerPanel()
        {
            if (selectedTowerPanel == null || selectedTowerTitle == null || selectedTowerBody == null || IsUpgradePanelOpen())
            {
                return;
            }

            var tower = towers.SelectedTower;
            if (tower != null && !tower.IsAlive)
            {
                towers.ClearSelectedTower();
                tower = null;
            }

            selectedTowerPanel.SetActive(tower != null);
            if (tower == null || tower.Definition == null)
            {
                return;
            }

            var definition = tower.Definition;
            selectedTowerTitle.text = definition.displayName;
            selectedTowerBody.text =
                $"{FormatShortTowerStats(definition)}\n" +
                $"Targeting: {FormatTargetingMode(tower.TargetingMode)}\n" +
                $"This tower damage: {tower.DamageDealt:0}\n" +
                $"{definition.displayName} type damage: {towers.GetDamageDealt(definition):0}";
            UpdateSelectedTowerTargetButtons(tower);
        }

        private void UpdateSelectedTowerTargetButtons(TowerActor tower)
        {
            for (var i = 0; i < selectedTowerTargetButtons.Length; i++)
            {
                var button = selectedTowerTargetButtons[i];
                if (button == null)
                {
                    continue;
                }

                button.gameObject.SetActive(tower != null && tower.CanChangeTargeting);
                if (button.targetGraphic is Image image)
                {
                    var mode = (TowerTargetingMode)i;
                    image.color = tower != null && tower.TargetingMode == mode
                        ? new Color(0.25f, 0.72f, 1f, 1f)
                        : new Color(0.15f, 0.45f, 0.82f, 1f);
                }
            }
        }

        private static string FormatTargetingMode(TowerTargetingMode mode)
        {
            switch (mode)
            {
                case TowerTargetingMode.First:
                    return "First";
                case TowerTargetingMode.Last:
                    return "Last";
                case TowerTargetingMode.HighestHealth:
                    return "Strong";
                default:
                    return "Closest";
            }
        }

        private static string FormatShortTowerStats(TowerDefinition tower)
        {
            switch (tower.behavior)
            {
                case TowerBehavior.SlowAura:
                    return $"Range: {tower.range:0.0}   Slow: {tower.slowPercent * 100f:0}%\nCapacity: {tower.slowCapacity:0.0} mass";
                case TowerBehavior.Barrier:
                    return $"Health: {tower.health:0}   Thorns: {tower.thornsDamage:0.0}\nPhysical blocker";
                case TowerBehavior.Barracks:
                    return $"Unit: {tower.barracksUnitType}   Capacity: {tower.barracksCapacity}\nUnit dmg: {tower.alliedUnitDamage:0.0}   Block: {tower.alliedUnitBlockCapacity:0.0}";
                default:
                    return $"Damage: {tower.damage:0.0}   Range: {tower.range:0.0}\nFire rate: {1f / Mathf.Max(0.01f, tower.fireInterval):0.0}/sec";
            }
        }

        private void CreateStartBattleButton(Transform parent)
        {
            startBattleButton = CreateAnchoredButton("StartBattleButton", parent, "START BATTLE", new Vector2(-92f, 52f), new Vector2(154f, 34f), new Vector2(1f, 0f), 13);
            RegisterBlockingButton(startBattleButton);
            startBattleButton.onClick.AddListener(() => session.StartLevel());
            startBattleButton.gameObject.SetActive(false);
        }

        private void UpdateStartBattleButton()
        {
            if (startBattleButton == null || IsUpgradePanelOpen())
            {
                return;
            }

            startBattleButton.gameObject.SetActive(session.IsPlanning && !session.DevBestBotRunning);
        }

        private void CreateDevPanel(Transform parent)
        {
            devPanel = CreatePanel("DevWalletPanel", parent, new Vector2(-326f, -48f), new Vector2(230f, 386f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            input.RegisterBlockingUiRect(devPanel.GetComponent<RectTransform>());
            var title = CreateText("DevTitle", devPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 13);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, -14f), new Vector2(210f, 20f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            title.text = "DEV WALLET";

            var viewport = new GameObject("DevScrollViewport");
            viewport.transform.SetParent(devPanel.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            ConfigureCenteredRect(viewportRect, new Vector2(0f, -210f), new Vector2(214f, 336f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("DevScrollContent");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 1f);
            contentRect.anchorMax = new Vector2(0.5f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(214f, 820f);

            var scrollRect = devPanel.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 28f;

            CreateButton("AddKillEssence", content.transform, $"+10000 {FormatCurrencySymbol(CurrencyType.KillEssence)}", new Vector2(0f, -10f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.KillEssence, 10000));
            CreateButton("AddVictorySigil", content.transform, $"+10000 {FormatCurrencySymbol(CurrencyType.VictorySigil)}", new Vector2(0f, -38f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.VictorySigil, 10000));
            CreateButton("AddPerfectSigil", content.transform, $"+10000 {FormatCurrencySymbol(CurrencyType.PerfectSigil)}", new Vector2(0f, -66f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.PerfectSigil, 10000));
            CreateButton("AddChallengeToken", content.transform, $"+10000 {FormatCurrencySymbol(CurrencyType.ChallengeToken)}", new Vector2(0f, -94f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.ChallengeToken, 10000));
            CreateButton("AddBossCore", content.transform, $"+10000 {FormatCurrencySymbol(CurrencyType.BossCore)}", new Vector2(0f, -122f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.BossCore, 10000));

            var speedLabel = CreateText("DevSpeedTitle", content.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(speedLabel.GetComponent<RectTransform>(), new Vector2(0f, -152f), new Vector2(178f, 18f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            speedLabel.text = "TEST SPEED";
            devSpeed1Button = CreateButton("DevSpeed1x", content.transform, "1x", new Vector2(-66f, -178f), new Vector2(38f, 22f), 11);
            devSpeed2Button = CreateButton("DevSpeed2x", content.transform, "2x", new Vector2(-22f, -178f), new Vector2(38f, 22f), 11);
            devSpeed5Button = CreateButton("DevSpeed5x", content.transform, "5x", new Vector2(22f, -178f), new Vector2(38f, 22f), 11);
            devSpeed10Button = CreateButton("DevSpeed10x", content.transform, "10x", new Vector2(66f, -178f), new Vector2(38f, 22f), 11);
            devSpeed1Button.onClick.AddListener(() => SetTestSpeed(1f));
            devSpeed2Button.onClick.AddListener(() => SetTestSpeed(2f));
            devSpeed5Button.onClick.AddListener(() => SetTestSpeed(5f));
            devSpeed10Button.onClick.AddListener(() => SetTestSpeed(10f));

            var testingLabel = CreateText("DevTestingTitle", content.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(testingLabel.GetComponent<RectTransform>(), new Vector2(0f, -208f), new Vector2(178f, 18f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            testingLabel.text = "TESTING";
            devRewardTestingButton = CreateButton("DevRewardTesting", content.transform, "REWARDS: OFF", new Vector2(0f, -234f), new Vector2(178f, 24f), 11);
            devRewardTestingButton.onClick.AddListener(() =>
            {
                session.ToggleRewardTesting();
                UpdateDevSpeedButtons();
            });
            devAutoActiveButton = CreateButton("DevAutoActive", content.transform, "AUTO ACTIVE: OFF", new Vector2(0f, -262f), new Vector2(178f, 24f), 11);
            devAutoActiveButton.onClick.AddListener(() =>
            {
                session.ToggleDevAutoActive();
                UpdateDevSpeedButtons();
            });
            CreateButton("DevPreviousBot", content.transform, "<", new Vector2(-76f, -290f), new Vector2(30f, 24f), 12)
                .onClick.AddListener(() => session.SelectPreviousDevBestBotProfile());
            devBestBotProfileText = CreateText("DevBestBotProfile", content.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(devBestBotProfileText.GetComponent<RectTransform>(), new Vector2(0f, -290f), new Vector2(112f, 24f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            CreateButton("DevNextBot", content.transform, ">", new Vector2(76f, -290f), new Vector2(30f, 24f), 12)
                .onClick.AddListener(() => session.SelectNextDevBestBotProfile());

            devBestBotSpeed20Button = CreateButton("DevBotSpeed20x", content.transform, "20x", new Vector2(-69f, -318f), new Vector2(42f, 22f), 10);
            devBestBotSpeed30Button = CreateButton("DevBotSpeed30x", content.transform, "30x", new Vector2(-23f, -318f), new Vector2(42f, 22f), 10);
            devBestBotSpeed40Button = CreateButton("DevBotSpeed40x", content.transform, "40x", new Vector2(23f, -318f), new Vector2(42f, 22f), 10);
            devBestBotSpeed50Button = CreateButton("DevBotSpeed50x", content.transform, "50x", new Vector2(69f, -318f), new Vector2(42f, 22f), 10);
            devBestBotSpeed20Button.onClick.AddListener(() => session.SetDevBestBotTimeScale(20f));
            devBestBotSpeed30Button.onClick.AddListener(() => session.SetDevBestBotTimeScale(30f));
            devBestBotSpeed40Button.onClick.AddListener(() => session.SetDevBestBotTimeScale(40f));
            devBestBotSpeed50Button.onClick.AddListener(() => session.SetDevBestBotTimeScale(50f));

            devBestBotButton = CreateButton("DevBestBot", content.transform, "START", new Vector2(-46f, -346f), new Vector2(86f, 24f), 11);
            devBestBotButton.onClick.AddListener(() =>
            {
                session.ToggleDevBestBot();
                UpdateDevSpeedButtons();
            });
            devBestBotRunAllButton = CreateButton("DevBestBotRunAll", content.transform, "RUN ALL 5", new Vector2(46f, -346f), new Vector2(86f, 24f), 10);
            devBestBotRunAllButton.onClick.AddListener(() =>
            {
                session.StartAllDevBestBots();
                UpdateDevSpeedButtons();
            });
            CreateButton("DevStopRun", content.transform, "STOP RUN", new Vector2(0f, -374f), new Vector2(178f, 24f), 11)
                .onClick.AddListener(() =>
                {
                    if (session.DevBestBotRunning)
                    {
                        session.StopDevBestBot();
                    }
                    else
                    {
                        session.ResetToPlanning();
                    }
                });

            var levelsLabel = CreateText("DevLevelsTitle", content.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(levelsLabel.GetComponent<RectTransform>(), new Vector2(0f, -402f), new Vector2(178f, 18f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            levelsLabel.text = "LEVEL MAPS";
            CreateButton("DevLoadLevel1", content.transform, "LEVEL 1", new Vector2(-46f, -428f), new Vector2(86f, 24f), 11)
                .onClick.AddListener(() => session.SelectLevel("level_01"));
            CreateButton("DevLoadLevel2", content.transform, "LEVEL 2", new Vector2(46f, -428f), new Vector2(86f, 24f), 11)
                .onClick.AddListener(() => session.SelectLevel("level_02"));
            CreateButton("DevLoadLevel3", content.transform, "LEVEL 3", new Vector2(-46f, -456f), new Vector2(86f, 24f), 11)
                .onClick.AddListener(() => session.SelectLevel("level_03"));
            CreateButton("DevLoadLevel4", content.transform, "10K TEST", new Vector2(46f, -456f), new Vector2(86f, 24f), 11)
                .onClick.AddListener(() => session.SelectLevel("level_04"));

            var saveLabel = CreateText("DevSaveTitle", content.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(saveLabel.GetComponent<RectTransform>(), new Vector2(0f, -494f), new Vector2(178f, 18f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            saveLabel.text = "DEV SAVES";

            for (var slot = 1; slot <= 3; slot++)
            {
                var capturedSlot = slot;
                var rowY = -494f - slot * 26f;
                var status = CreateText($"DevSaveSlotStatus{slot}", content.transform, Vector2.zero, TextAnchor.MiddleCenter, 9);
                ConfigureCenteredRect(status.GetComponent<RectTransform>(), new Vector2(0f, rowY), new Vector2(46f, 20f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
                devSaveSlotStatusTexts[slot] = status;

                CreateButton($"SaveDevSlot{slot}", content.transform, $"SAVE {slot}", new Vector2(-66f, rowY), new Vector2(64f, 22f), 10)
                    .onClick.AddListener(() =>
                    {
                        session.SaveDevSnapshot(capturedSlot);
                        UpdateDevSaveSlotIndicators();
                    });
                var loadButton = CreateButton($"LoadDevSlot{slot}", content.transform, $"LOAD {slot}", new Vector2(66f, rowY), new Vector2(64f, 22f), 10);
                devLoadSlotButtons[slot] = loadButton;
                loadButton.onClick.AddListener(() =>
                {
                    session.TryLoadDevSnapshot(capturedSlot);
                    UpdateDevSaveSlotIndicators();
                });
            }

            CreateButton("RefundUpgrades", content.transform, "RESET UPGRADES", new Vector2(0f, -604f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.RefundAndResetUpgrades());
            CreateButton("ClearCurrencies", content.transform, "CLEAR CURRENCIES", new Vector2(0f, -632f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.ClearCurrencies());
            CreateButton("ResetRewardProgress", content.transform, "RESET CLEAR REWARDS", new Vector2(0f, -660f), new Vector2(178f, 24f), 11)
                .onClick.AddListener(() => session.ClearLevelRewardProgress());
            CreateButton("ResetBalanceTestProgress", content.transform, "RESET TEST STATS", new Vector2(0f, -688f), new Vector2(178f, 24f), 11)
                .onClick.AddListener(() => session.ResetBalanceTestProgress());
            CreateButton("AutoResolveRun", content.transform, "AUTO RESOLVE RUN", new Vector2(0f, -716f), new Vector2(178f, 24f), 11)
                .onClick.AddListener(() => session.AutoResolveRun());
            var autoResolveNote = CreateText("AutoResolveNote", content.transform, Vector2.zero, TextAnchor.MiddleCenter, 8);
            ConfigureCenteredRect(autoResolveNote.GetComponent<RectTransform>(), new Vector2(0f, -742f), new Vector2(178f, 22f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            autoResolveNote.text = "AFK estimate, not perfect play";
            autoResolveNote.color = new Color(0.78f, 0.86f, 0.95f, 0.82f);

            devPanelVisible = false;
            devPanel.SetActive(false);
            UpdateDevSaveSlotIndicators();
        }

        private void UpdateDevSaveSlotIndicators()
        {
            if (session == null)
            {
                return;
            }

            for (var slot = 1; slot <= 3; slot++)
            {
                var hasSave = session.HasDevSnapshot(slot);
                if (devSaveSlotStatusTexts[slot] != null)
                {
                    devSaveSlotStatusTexts[slot].text = hasSave ? "SAVED" : "EMPTY";
                    devSaveSlotStatusTexts[slot].color = hasSave ? new Color(0.55f, 1f, 0.6f, 1f) : new Color(0.85f, 0.85f, 0.85f, 0.72f);
                }

                if (devLoadSlotButtons[slot] != null)
                {
                    devLoadSlotButtons[slot].interactable = hasSave;
                }
            }
        }

        private void CreateTopRightToggles(Transform parent)
        {
            statsToggleButton = CreateAnchoredButton("StatsToggle", parent, "STATS [TAB]", new Vector2(-70f, -18f), new Vector2(102f, 28f), new Vector2(1f, 1f), 11);
            RegisterBlockingButton(statsToggleButton);
            statsToggleButton.onClick.AddListener(ToggleStatsPanel);

            debugSpawnToggleButton = CreateAnchoredButton("DebugSpawnToggle", parent, "SPAWN", new Vector2(-70f, -50f), new Vector2(102f, 24f), new Vector2(1f, 1f), 10);
            RegisterBlockingButton(debugSpawnToggleButton);
            debugSpawnToggleButton.onClick.AddListener(ToggleDebugSpawnPanel);

            performanceToggleButton = CreateAnchoredButton("PerformanceToggle", parent, "PERF [F3]", new Vector2(-70f, -80f), new Vector2(102f, 24f), new Vector2(1f, 1f), 10);
            RegisterBlockingButton(performanceToggleButton);
            performanceToggleButton.onClick.AddListener(TogglePerformancePanel);

            codexToggleButton = CreateAnchoredButton("CodexToggle", parent, "GRIMOIRE [G]", new Vector2(-186f, -18f), new Vector2(122f, 28f), new Vector2(1f, 1f), 10);
            RegisterBlockingButton(codexToggleButton);
            codexToggleButton.onClick.AddListener(ToggleCodexPanel);

            upgradeToggleButton = CreateAnchoredButton("UpgradeToggle", parent, "UPGRADES [U]", new Vector2(-312f, -18f), new Vector2(126f, 28f), new Vector2(1f, 1f), 10);
            RegisterBlockingButton(upgradeToggleButton);
            upgradeToggleButton.onClick.AddListener(ShowUpgradePanel);

            devToggleButton = CreateAnchoredButton("DevToggle", parent, "DEV [`]", new Vector2(-428f, -18f), new Vector2(82f, 28f), new Vector2(1f, 1f), 11);
            RegisterBlockingButton(devToggleButton);
            devToggleButton.onClick.AddListener(ToggleDevPanel);
        }

        private void TogglePerformancePanel()
        {
            performancePanelVisible = !performancePanelVisible;
            if (performancePanel != null)
            {
                performancePanel.SetActive(performancePanelVisible);
            }

            if (performanceToggleButton != null)
            {
                HighlightSpeedButton(performanceToggleButton, performancePanelVisible);
            }
        }

        private void RegisterBlockingButton(Button button)
        {
            if (button != null)
            {
                input.RegisterBlockingUiRect(button.GetComponent<RectTransform>());
            }
        }

        private void ToggleDevPanel()
        {
            devPanelVisible = !devPanelVisible;
            if (devPanelVisible)
            {
                UpdateDevSaveSlotIndicators();
            }

            if (devPanel != null)
            {
                devPanel.SetActive(devPanelVisible && !IsUpgradePanelOpen());
            }
        }

        private void ToggleStatsPanel()
        {
            statsPanelVisible = !statsPanelVisible;
            if (statsPanelVisible)
            {
                codexPanelVisible = false;
                debugSpawnPanelVisible = false;
            }

            if (statsPanel != null)
            {
                statsPanel.SetActive(statsPanelVisible && !IsUpgradePanelOpen());
            }

            if (codexPanel != null)
            {
                codexPanel.SetActive(false);
            }

            if (debugSpawnPanel != null)
            {
                debugSpawnPanel.SetActive(false);
            }
        }

        private void ToggleCodexPanel()
        {
            codexPanelVisible = !codexPanelVisible;
            if (codexPanelVisible)
            {
                statsPanelVisible = false;
                debugSpawnPanelVisible = false;
                codexListDirty = true;
            }

            if (codexPanel != null)
            {
                codexPanel.SetActive(codexPanelVisible && !IsUpgradePanelOpen());
            }

            if (statsPanel != null)
            {
                statsPanel.SetActive(false);
            }

            if (debugSpawnPanel != null)
            {
                debugSpawnPanel.SetActive(false);
            }
        }

        private void ToggleDebugSpawnPanel()
        {
            debugSpawnPanelVisible = !debugSpawnPanelVisible;
            if (debugSpawnPanelVisible)
            {
                statsPanelVisible = false;
                codexPanelVisible = false;
                RebuildDebugSpawnPanel();
            }

            if (debugSpawnPanel != null)
            {
                debugSpawnPanel.SetActive(debugSpawnPanelVisible && !IsUpgradePanelOpen());
            }

            if (statsPanel != null)
            {
                statsPanel.SetActive(false);
            }

            if (codexPanel != null)
            {
                codexPanel.SetActive(false);
            }
        }

        private bool IsUpgradePanelOpen()
        {
            return upgradePanel != null && upgradePanel.activeSelf;
        }

        private void CreateStatsPanel(Transform parent)
        {
            statsPanel = CreatePanel("StatsPanel", parent, new Vector2(-14f, -48f), new Vector2(380f, 264f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            input.RegisterBlockingUiRect(statsPanel.GetComponent<RectTransform>());
            var title = CreateText("StatsTitle", statsPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 13);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, -14f), new Vector2(350f, 20f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            title.text = "DAMAGE STATS";

            statsRows.Clear();
            var towersForStats = session.AllTowerDefinitions ?? towers.AvailableTowers;
            for (var i = 0; i < towersForStats.Count; i++)
            {
                var tower = towersForStats[i];
                var row = CreateButton($"Stats_{tower.id}", statsPanel.transform, tower.displayName, new Vector2(-96f, -60f - i * 28f), new Vector2(176f, 24f), 10);
                statsRows[tower] = row.GetComponentInChildren<Text>();
                statsRowButtons[tower] = row;
            }

            var towerHeader = CreateText("StatsTowerHeader", statsPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(towerHeader.GetComponent<RectTransform>(), new Vector2(-96f, -40f), new Vector2(176f, 18f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            towerHeader.text = "TOWERS";

            var activeHeader = CreateText("StatsActiveHeader", statsPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(activeHeader.GetComponent<RectTransform>(), new Vector2(98f, -40f), new Vector2(150f, 18f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            activeHeader.text = "ACTIVE";
            activeWeaponStatsButton = CreateButton("Stats_ActiveWeapon", statsPanel.transform, "Volley of Arrows", new Vector2(98f, -60f), new Vector2(150f, 24f), 10);

            statsEmptyTowerText = CreateText("StatsNoTowers", statsPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(statsEmptyTowerText.GetComponent<RectTransform>(), new Vector2(-96f, -62f), new Vector2(176f, 42f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            statsEmptyTowerText.text = "No towers unlocked";
            statsEmptyTowerText.color = new Color(0.7f, 0.78f, 0.86f, 1f);

            statsPanelVisible = false;
            statsPanel.SetActive(false);
        }

        private void CreateDebugSpawnPanel(Transform parent)
        {
            debugSpawnPanel = CreatePanel("DebugSpawnPanel", parent, new Vector2(-14f, -78f), new Vector2(220f, 220f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            input.RegisterBlockingUiRect(debugSpawnPanel.GetComponent<RectTransform>());

            var title = CreateText("DebugSpawnTitle", debugSpawnPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 13);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, -16f), new Vector2(190f, 20f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            title.text = "SPAWN ENEMY";

            debugSpawnPanelVisible = false;
            debugSpawnPanel.SetActive(false);
        }

        private void RebuildDebugSpawnPanel()
        {
            if (debugSpawnPanel == null)
            {
                return;
            }

            for (var i = debugSpawnPanel.transform.childCount - 1; i >= 0; i--)
            {
                var child = debugSpawnPanel.transform.GetChild(i);
                if (child.name != "DebugSpawnTitle")
                {
                    Destroy(child.gameObject);
                }
            }

            var spawnableEnemies = session.GetDebugSpawnableEnemies();
            if (spawnableEnemies.Count == 0)
            {
                var empty = CreateText("DebugSpawnEmpty", debugSpawnPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
                ConfigureCenteredRect(empty.GetComponent<RectTransform>(), new Vector2(0f, -70f), new Vector2(180f, 38f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
                empty.text = "No enemies in this level";
                empty.color = new Color(0.72f, 0.8f, 0.88f, 1f);
                return;
            }

            for (var i = 0; i < spawnableEnemies.Count; i++)
            {
                var enemy = spawnableEnemies[i];
                var button = CreateButton($"DebugSpawn_{enemy.id}", debugSpawnPanel.transform, enemy.displayName, new Vector2(0f, -52f - i * 30f), new Vector2(174f, 24f), 10);
                button.onClick.AddListener(() => session.SpawnDebugEnemy(enemy));
            }
        }

        private void CreateCodexPanel(Transform parent)
        {
            codexPanel = CreatePanel("BreakerGrimoirePanel", parent, new Vector2(-14f, -48f), new Vector2(600f, 520f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            input.RegisterBlockingUiRect(codexPanel.GetComponent<RectTransform>());
            var title = CreateText("CodexTitle", codexPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 15);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, -18f), new Vector2(480f, 24f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            title.text = "THE BREAKER'S GRIMOIRE";

            CreateButton("CodexTurrets", codexPanel.transform, "TURRETS", new Vector2(-240f, -50f), new Vector2(92f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.Turrets));
            CreateButton("CodexActive", codexPanel.transform, "ACTIVE", new Vector2(-120f, -50f), new Vector2(92f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.ActiveWeapons));
            CreateButton("CodexEnemies", codexPanel.transform, "ENEMIES", new Vector2(0f, -50f), new Vector2(92f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.Enemies));
            CreateButton("CodexBosses", codexPanel.transform, "BOSSES", new Vector2(120f, -50f), new Vector2(92f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.Bosses));
            CreateButton("CodexLevels", codexPanel.transform, "LEVELS", new Vector2(240f, -50f), new Vector2(92f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.Levels));

            var listViewport = CreatePanel("CodexListViewport", codexPanel.transform, new Vector2(-185f, -82f), new Vector2(190f, 400f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            listViewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);
            listViewport.AddComponent<Mask>().showMaskGraphic = false;
            var scrollInput = listViewport.AddComponent<CodexListScrollInput>();
            scrollInput.Initialize(OnCodexListScrolled);

            var listContentObject = new GameObject("CodexListContent");
            listContentObject.transform.SetParent(listViewport.transform, false);
            codexListContent = listContentObject.AddComponent<RectTransform>();
            codexListContent.anchorMin = new Vector2(0.5f, 1f);
            codexListContent.anchorMax = new Vector2(0.5f, 1f);
            codexListContent.pivot = new Vector2(0.5f, 1f);
            codexListContent.sizeDelta = new Vector2(178f, 400f);

            var detailPanel = CreatePanel("CodexDetails", codexPanel.transform, new Vector2(118f, -82f), new Vector2(366f, 400f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            detailPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.36f);
            detailPanel.AddComponent<Mask>().showMaskGraphic = false;
            var detailScrollInput = detailPanel.AddComponent<CodexListScrollInput>();
            detailScrollInput.Initialize(OnCodexDetailScrolled);

            var detailContentObject = new GameObject("CodexDetailContent");
            detailContentObject.transform.SetParent(detailPanel.transform, false);
            codexDetailContent = detailContentObject.AddComponent<RectTransform>();
            codexDetailContent.anchorMin = new Vector2(0.5f, 1f);
            codexDetailContent.anchorMax = new Vector2(0.5f, 1f);
            codexDetailContent.pivot = new Vector2(0.5f, 1f);
            codexDetailContent.anchoredPosition = Vector2.zero;
            codexDetailContent.sizeDelta = new Vector2(330f, 360f);

            codexDetailText = CreateText("CodexDetailText", codexDetailContent, Vector2.zero, TextAnchor.UpperLeft, 11);
            ConfigureCenteredRect(codexDetailText.GetComponent<RectTransform>(), new Vector2(0f, -14f), new Vector2(330f, 360f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            codexDetailText.color = new Color(0.86f, 0.93f, 1f, 1f);

            codexPanelVisible = false;
            codexPanel.SetActive(false);
        }

        private void UpdateResultPanel()
        {
            if (resultPanel == null)
            {
                return;
            }

            if (IsUpgradePanelOpen())
            {
                resultPanel.SetActive(false);
                return;
            }

            resultPanel.SetActive(session.Finished && !session.DevBestBotRunning);
            if (!session.Finished || session.DevBestBotRunning)
            {
                return;
            }

            resultTitle.text = session.Won ? "VICTORY" : "DEFEAT";
            resultTitle.color = session.Won ? new Color(0.7f, 1f, 0.55f, 1f) : new Color(1f, 0.35f, 0.25f, 1f);
            var earnedText = FormatRunCurrencyDeltas();
            var testingText = session.RewardTestingEnabled ? $"\nTesting rewards: x{session.RewardTestMultiplier}" : string.Empty;
            var progress = session.GetLevelProgress();
            var testProgressText = $"\nTest session: {progress.testSessionAttempts} runs";
            if (progress.testSessionEquivalentAttempts != progress.testSessionAttempts)
            {
                testProgressText += $" (~{progress.testSessionEquivalentAttempts} normal)";
            }

            resultBody.text = session.Won
                ? $"Wave cleared. Lives: {session.Lives}\nKilled: {session.EnemiesKilled}\nEarned: {earnedText}{testingText}{testProgressText}"
                : $"The horde broke through. Killed: {session.EnemiesKilled}\nEarned: {earnedText}{testingText}{testProgressText}";
        }

        private void UpdateDevAutoPurchasePanel()
        {
            if (devAutoPurchasePanel == null || devAutoPurchaseTitle == null || devAutoPurchaseBody == null)
            {
                return;
            }

            var showBestBotPlanning = session.DevBestBotWaitingToStart;
            devAutoPurchasePanel.SetActive((session.DevAutoPurchaseWindowVisible || showBestBotPlanning) && !IsUpgradePanelOpen());
            if (!devAutoPurchasePanel.activeSelf)
            {
                return;
            }

            if (showBestBotPlanning)
            {
                devAutoPurchaseTitle.text = "BEST BOT PLANNING";
                devAutoPurchaseBody.text = $"{session.DevBestBotStatus}\nIsolated profile at {session.DevBestBotSelectedTimeScale:0}x speed.";
            }
            else
            {
                devAutoPurchaseTitle.text = "AUTO LOOP PURCHASES";
                devAutoPurchaseBody.text = $"Bought this run:\n{session.DevLastAutoPurchaseDetails}\nNext run starts in a moment.";
            }
        }

        private void UpdateDevBestBotReportPanel()
        {
            if (devBestBotReportPanel == null || devBestBotReportBody == null)
            {
                return;
            }

            devBestBotReportPanel.SetActive(session.DevBestBotReportAvailable && !IsUpgradePanelOpen());
            if (!devBestBotReportPanel.activeSelf)
            {
                devBestBotPurchasesExpanded = false;
                devBestBotPurchasesDropdown?.SetActive(false);
                return;
            }

            devBestBotReportBody.text = session.DevBestBotReport;
            if (devBestBotPurchasesButton != null)
            {
                devBestBotPurchasesButton.GetComponentInChildren<Text>().text = devBestBotPurchasesExpanded ? "PURCHASES ▲" : "PURCHASES ▼";
            }

            if (devBestBotPurchasesDropdown != null)
            {
                devBestBotPurchasesDropdown.SetActive(devBestBotPurchasesExpanded);
            }
        }

        private void ToggleDevBestBotPurchaseHistory()
        {
            devBestBotPurchasesExpanded = !devBestBotPurchasesExpanded;
            if (!devBestBotPurchasesExpanded || devBestBotPurchasesDropdown == null || devBestBotPurchasesText == null || devBestBotPurchasesContent == null)
            {
                devBestBotPurchasesDropdown?.SetActive(false);
                return;
            }

            devBestBotPurchasesDropdown.SetActive(true);
            devBestBotPurchasesText.text = session.DevBestBotPurchaseHistory;
            var preferredHeight = Mathf.Max(242f, devBestBotPurchasesText.preferredHeight + 20f);
            devBestBotPurchasesContent.sizeDelta = new Vector2(0f, preferredHeight);
            var textRect = devBestBotPurchasesText.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(-20f, preferredHeight - 16f);
            devBestBotPurchasesContent.anchoredPosition = Vector2.zero;
        }

        private string FormatRunCurrencyDeltas()
        {
            if (session.LastRunCurrencyDeltas == null || session.LastRunCurrencyDeltas.Count == 0)
            {
                return "None";
            }

            var text = new StringBuilder();
            foreach (var delta in session.LastRunCurrencyDeltas)
            {
                if (delta.Value <= 0)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append("   ");
                }

                text.Append('+');
                text.Append(delta.Value);
                text.Append(' ');
                text.Append(FormatCurrencySymbol(delta.Key));
            }

            return text.Length == 0 ? "None" : text.ToString();
        }

        private void SetTestSpeed(float speed)
        {
            Time.timeScale = speed;
            UpdateDevSpeedButtons();
        }

        private void UpdateDevSpeedButtons()
        {
            HighlightSpeedButton(devSpeed1Button, Mathf.Approximately(Time.timeScale, 1f));
            HighlightSpeedButton(devSpeed2Button, Mathf.Approximately(Time.timeScale, 2f));
            HighlightSpeedButton(devSpeed5Button, Mathf.Approximately(Time.timeScale, 5f));
            HighlightSpeedButton(devSpeed10Button, Mathf.Approximately(Time.timeScale, 10f));
            HighlightToggleButton(devToggleButton, devPanelVisible);
            HighlightToggleButton(statsToggleButton, statsPanelVisible);
            HighlightToggleButton(codexToggleButton, codexPanelVisible);
            HighlightToggleButton(debugSpawnToggleButton, debugSpawnPanelVisible);
            if (devRewardTestingButton != null)
            {
                devRewardTestingButton.GetComponentInChildren<Text>().text = session.RewardTestingEnabled ? $"REWARDS x{session.RewardTestMultiplier}: ON" : "REWARDS: OFF";
                HighlightSpeedButton(devRewardTestingButton, session.RewardTestingEnabled);
            }
            if (devAutoActiveButton != null)
            {
                devAutoActiveButton.GetComponentInChildren<Text>().text = session.DevAutoActiveEnabled ? "AUTO ACTIVE: ON" : "AUTO ACTIVE: OFF";
                HighlightSpeedButton(devAutoActiveButton, session.DevAutoActiveEnabled);
            }
            if (devBestBotButton != null)
            {
                devBestBotButton.GetComponentInChildren<Text>().text = session.DevBestBotRunning
                    ? $"STOP ({session.DevBestBotAttemptCount})"
                    : "START";
                HighlightSpeedButton(devBestBotButton, session.DevBestBotRunning);
            }
            if (devBestBotRunAllButton != null)
            {
                devBestBotRunAllButton.GetComponentInChildren<Text>().text = session.DevBestBotRunAll ? "RUNNING ALL" : "RUN ALL 5";
                HighlightSpeedButton(devBestBotRunAllButton, session.DevBestBotRunAll);
                devBestBotRunAllButton.interactable = !session.DevBestBotRunning;
            }
            if (devBestBotProfileText != null)
            {
                devBestBotProfileText.text = $"BOT: {session.DevBestBotSelectedProfileName.ToUpperInvariant()}";
                devBestBotProfileText.color = session.DevBestBotRunning ? new Color(0.55f, 0.86f, 1f, 1f) : Color.white;
            }
            HighlightSpeedButton(devBestBotSpeed20Button, Mathf.Approximately(session.DevBestBotSelectedTimeScale, 20f));
            HighlightSpeedButton(devBestBotSpeed30Button, Mathf.Approximately(session.DevBestBotSelectedTimeScale, 30f));
            HighlightSpeedButton(devBestBotSpeed40Button, Mathf.Approximately(session.DevBestBotSelectedTimeScale, 40f));
            HighlightSpeedButton(devBestBotSpeed50Button, Mathf.Approximately(session.DevBestBotSelectedTimeScale, 50f));
        }

        private static void HighlightSpeedButton(Button button, bool active)
        {
            if (button?.targetGraphic is Image image)
            {
                image.color = active ? new Color(0.25f, 0.7f, 1f, 1f) : new Color(0.15f, 0.45f, 0.82f, 1f);
            }
        }

        private static void HighlightToggleButton(Button button, bool active)
        {
            if (button?.targetGraphic is Image image)
            {
                image.color = active ? new Color(0.25f, 0.7f, 1f, 1f) : new Color(0.12f, 0.32f, 0.58f, 1f);
            }
        }

        private void UpdateStatsPanel()
        {
            if (statsPanel == null || !statsPanel.activeSelf)
            {
                return;
            }

            var unlockedTowers = towers.AvailableTowers;
            var totalDamage = 0f;
            foreach (var entry in statsRows)
            {
                if (ContainsTower(unlockedTowers, entry.Key))
                {
                    totalDamage += towers.GetDamageDealt(entry.Key);
                }
            }
            totalDamage += activeWeapon.TotalDamageDealt;

            foreach (var entry in statsRows)
            {
                var tower = entry.Key;
                var text = entry.Value;
                var unlocked = ContainsTower(unlockedTowers, tower);
                if (statsRowButtons.TryGetValue(tower, out var button))
                {
                    button.gameObject.SetActive(unlocked);
                }

                if (!unlocked)
                {
                    continue;
                }

                var damage = towers.GetDamageDealt(tower);
                var percent = totalDamage <= 0f ? 0f : damage / totalDamage * 100f;
                text.text = $"{tower.displayName}  {damage:0}  {percent:0}%";
                text.color = Color.white;
            }

            var visibleTowerIndex = 0;
            var towersForOrdering = session.AllTowerDefinitions ?? unlockedTowers;
            foreach (var tower in towersForOrdering)
            {
                if (!ContainsTower(unlockedTowers, tower) || !statsRowButtons.TryGetValue(tower, out var button))
                {
                    continue;
                }

                button.GetComponent<RectTransform>().anchoredPosition = new Vector2(-96f, -60f - visibleTowerIndex * 28f);
                visibleTowerIndex++;
            }

            if (statsEmptyTowerText != null)
            {
                statsEmptyTowerText.gameObject.SetActive(unlockedTowers.Count == 0);
            }

            if (activeWeaponStatsButton != null)
            {
                var label = activeWeaponStatsButton.GetComponentInChildren<Text>();
                var activePercent = totalDamage <= 0f ? 0f : activeWeapon.TotalDamageDealt / totalDamage * 100f;
                label.text = $"Volley of Arrows  {activeWeapon.TotalDamageDealt:0}  {activePercent:0}%";
                label.color = Color.white;
            }
        }

        private void UpdateCodexPanel()
        {
            if (codexPanel == null || !codexPanel.activeSelf || codexDetailText == null || codexListContent == null)
            {
                return;
            }

            if (codexListDirty)
            {
                RebuildCodexList();
                codexListDirty = false;
            }

            UpdateCodexDetails();
        }

        private void SetCodexSector(CodexSector sector)
        {
            codexSector = sector;
            selectedCodexId = null;
            codexScroll = 0f;
            codexDetailScroll = 0f;
            codexListDirty = true;
            UpdateCodexDetails();
        }

        private void RebuildCodexList()
        {
            for (var i = codexListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(codexListContent.GetChild(i).gameObject);
            }

            var entries = GetCodexEntries();
            if (entries.Count == 0)
            {
                var empty = CreateText("EmptyCodexList", codexListContent, Vector2.zero, TextAnchor.MiddleCenter, 11);
                ConfigureCenteredRect(empty.GetComponent<RectTransform>(), new Vector2(0f, -18f), new Vector2(168f, 32f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
                empty.text = "Nothing catalogued";
                empty.color = new Color(0.7f, 0.78f, 0.86f, 1f);
                selectedCodexId = null;
                ApplyCodexScroll(entries.Count);
                return;
            }

            if (string.IsNullOrEmpty(selectedCodexId))
            {
                selectedCodexId = entries[0].id;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var button = CreateButton($"CodexEntry_{entry.id}", codexListContent, entry.displayName, new Vector2(0f, -18f - i * 30f), new Vector2(168f, 24f), 10);
                button.onClick.AddListener(() =>
                {
                    selectedCodexId = entry.id;
                    codexDetailScroll = 0f;
                    codexListDirty = true;
                    UpdateCodexDetails();
                });
                if (button.targetGraphic is Image image && entry.id == selectedCodexId)
                {
                    image.color = new Color(0.25f, 0.7f, 1f, 1f);
                }
            }

            ApplyCodexScroll(entries.Count);
        }

        private void UpdateCodexDetails()
        {
            var entries = GetCodexEntries();
            CodexEntry selected = null;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].id == selectedCodexId)
                {
                    selected = entries[i];
                    break;
                }
            }

            codexDetailText.text = selected?.details ?? "Select an entry.";
            ApplyCodexDetailScroll();
        }

        private List<CodexEntry> GetCodexEntries()
        {
            var entries = new List<CodexEntry>();
            switch (codexSector)
            {
                case CodexSector.Turrets:
                    AddTurretCodexEntries(entries);
                    break;
                case CodexSector.ActiveWeapons:
                    entries.Add(new CodexEntry("volley_of_arrows", "Volley of Arrows",
                        $"Volley of Arrows\n\nManual burst weapon. Fires into a target area and damages only a capped number of enemies inside it, so timing and target choice matter.\n\nWeakness: Limited by cooldown and pierce cap; wasted shots hurt when enemies are spread out or only a few targets are inside the radius.\n\nDamage: {activeWeapon.Damage:0.0} per target\nRadius: {activeWeapon.Radius:0.0}\nPierce cap: {activeWeapon.MaxTargets}\nCooldown: {activeWeapon.CooldownSeconds:0.0}s\nProjectile: area volley\nRole: manual burst damage"));
                    break;
                case CodexSector.Enemies:
                    AddEnemyCodexEntries(entries, includeBosses: false);
                    break;
                case CodexSector.Bosses:
                    AddEnemyCodexEntries(entries, includeBosses: true);
                    break;
                case CodexSector.Levels:
                    AddLevelCodexEntries(entries);
                    break;
            }

            return entries;
        }

        private void AddLevelCodexEntries(List<CodexEntry> entries)
        {
            var levels = session.AllLevels;
            if (levels == null || levels.Count == 0)
            {
                return;
            }

            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                if (level != null)
                {
                    entries.Add(new CodexEntry(level.id, level.displayName, FormatLevelCodexDetails(level)));
                }
            }
        }

        private void AddTurretCodexEntries(List<CodexEntry> entries)
        {
            var towerDefinitions = session.UnlockedTowerDefinitions;
            if (towerDefinitions == null)
            {
                return;
            }

            for (var i = 0; i < towerDefinitions.Count; i++)
            {
                var tower = towerDefinitions[i];
                entries.Add(new CodexEntry(tower.id, tower.displayName,
                    $"{tower.displayName}\n\n{tower.shortDescription}\n\nWeakness: {tower.weaknessDescription}\n\n{FormatTowerCodexStats(tower)}"));
            }
        }

        private void AddEnemyCodexEntries(List<CodexEntry> entries, bool includeBosses)
        {
            var waveEntries = session.Level?.wave?.entries;
            if (waveEntries == null || waveEntries.Length == 0)
            {
                return;
            }

            var seen = new HashSet<string>();
            for (var i = 0; i < waveEntries.Length; i++)
            {
                var enemy = waveEntries[i].enemy;
                if (enemy == null || !seen.Add(enemy.id) || (enemy.role == EnemyRole.Boss) != includeBosses)
                {
                    continue;
                }

                if (!session.HasEncounteredEnemy(enemy))
                {
                    continue;
                }

                entries.Add(new CodexEntry(enemy.id, enemy.displayName,
                    $"{enemy.displayName}\n\n{enemy.shortDescription}\n\nWeakness: {enemy.weaknessDescription}\n\n{FormatEnemyCodexStats(enemy)}"));
            }
        }

        private string FormatLevelCodexDetails(LevelDefinition level)
        {
            var progress = session.GetLevelProgress(level.id);
            var firstClearClaimed = progress.firstClearClaimed || session.Profile.clearedLevelIds.Contains(level.id);
            var perfectClearClaimed = progress.perfectClearClaimed || session.Profile.perfectClearedLevelIds.Contains(level.id);
            var text = new StringBuilder();
            text.AppendLine(level.displayName);
            text.AppendLine();
            text.AppendLine("Wave");
            text.AppendLine($"Total enemies: {level.wave?.totalEnemyCount ?? 0}");
            text.AppendLine(FormatWaveComposition(level.wave));
            text.AppendLine($"Path length: {GetLevelPathLength(level):0.0}m");
            if (level.wave != null && level.wave.useEndpointSeeking)
            {
                text.AppendLine("Pathing: endpoint-seeking crowd flow");
            }
            text.AppendLine($"Estimated duration: {FormatWaveDuration(level.wave)}");
            text.AppendLine();
            text.AppendLine("Recommended tactics");
            text.AppendLine(string.IsNullOrWhiteSpace(level.recommendedTactics) ? "No tactical notes yet." : level.recommendedTactics);
            text.AppendLine();
            text.AppendLine("Progress");
            text.AppendLine($"Attempts: {progress.attempts}");
            text.AppendLine($"Victories: {progress.victories}");
            text.AppendLine(progress.firstVictoryAttempt > 0 ? $"First victory: attempt {progress.firstVictoryAttempt}" : "First victory: not yet");
            text.AppendLine(progress.bestLivesRemaining > 0 ? $"Best lives remaining: {progress.bestLivesRemaining}" : "Best lives remaining: none");
            text.AppendLine();
            text.AppendLine("Current Test Session");
            text.AppendLine($"Runs: {progress.testSessionAttempts}");
            text.AppendLine($"Victories: {progress.testSessionVictories}");
            text.AppendLine(progress.testSessionFirstVictoryAttempt > 0
                ? $"First victory: run {progress.testSessionFirstVictoryAttempt}"
                : "First victory: not yet");
            text.AppendLine(progress.testSessionEquivalentAttempts != progress.testSessionAttempts
                ? $"Equivalent normal runs: ~{progress.testSessionEquivalentAttempts}"
                : "Equivalent normal runs: same as runs");
            if (progress.testSessionFirstVictoryEquivalentAttempt > 0 && progress.testSessionFirstVictoryEquivalentAttempt != progress.testSessionFirstVictoryAttempt)
            {
                text.AppendLine($"First victory equivalent: ~{progress.testSessionFirstVictoryEquivalentAttempt} normal runs");
            }
            text.AppendLine();
            text.AppendLine("Rewards");
            text.AppendLine($"Normal: {FormatRewardStatus(progress.attempts > 0, "Started")}");
            text.AppendLine($"Essence: 2 {FormatCurrencySymbol(CurrencyType.KillEssence)} per 10 enemy mass killed");
            text.AppendLine($"Victory Sigil: {FormatRewardStatus(firstClearClaimed, FormatCurrencyAmount(level.firstClearReward))}");
            text.AppendLine($"Perfect Sigil: {FormatRewardStatus(perfectClearClaimed, FormatCurrencyAmount(level.perfectClearReward))}");
            text.AppendLine($"Boss Sigil: {FormatRewardStatus(progress.bossClearClaimed, FormatCurrencyAmount(level.bossClearReward), "planned")}");
            text.Append($"Challenge Sigil: {FormatRewardStatus(progress.challengeClaimed, FormatCurrencyAmount(level.challengeReward), "planned")}");
            return text.ToString();
        }

        private static string FormatWaveComposition(WaveDefinition wave)
        {
            if (wave?.entries == null || wave.entries.Length == 0)
            {
                return "Enemy mix: none";
            }

            var counts = new Dictionary<string, int>();
            var names = new Dictionary<string, string>();
            var remaining = wave.totalEnemyCount;
            for (var i = 0; i < wave.entries.Length && remaining > 0; i++)
            {
                var entry = wave.entries[i];
                if (entry.enemy == null || entry.count <= 0)
                {
                    continue;
                }

                var count = Mathf.Min(entry.count, remaining);
                if (counts.ContainsKey(entry.enemy.id))
                {
                    counts[entry.enemy.id] += count;
                }
                else
                {
                    counts.Add(entry.enemy.id, count);
                    names.Add(entry.enemy.id, entry.enemy.displayName);
                }

                remaining -= count;
            }

            var text = new StringBuilder("Enemy mix:");
            foreach (var entry in counts)
            {
                text.AppendLine();
                text.Append($"- {names[entry.Key]}: {entry.Value}");
            }

            return text.ToString();
        }

        private static float GetLevelPathLength(LevelDefinition level)
        {
            if (level?.pathWaypoints == null || level.pathWaypoints.Length < 2)
            {
                return 0f;
            }

            var length = GetPolylineLength(level.pathWaypoints);
            if (level.secondaryPathWaypoints != null && level.secondaryPathWaypoints.Length > 1)
            {
                length = Mathf.Max(length, GetPolylineLength(level.secondaryPathWaypoints));
            }

            return length;
        }

        private static float GetPolylineLength(Vector3[] points)
        {
            var length = 0f;
            for (var i = 1; i < points.Length; i++)
            {
                length += Vector3.Distance(points[i - 1], points[i]);
            }

            return length;
        }

        private static string FormatWaveDuration(WaveDefinition wave)
        {
            if (wave == null || wave.totalEnemyCount <= 0)
            {
                return "unknown";
            }

            var averageBurst = wave.randomSpawnBurstMin > 0 && wave.randomSpawnBurstMax >= wave.randomSpawnBurstMin
                ? (wave.randomSpawnBurstMin + wave.randomSpawnBurstMax) * 0.5f
                : 1f;
            var seconds = Mathf.Max(0.01f, wave.spawnInterval) * Mathf.Ceil(wave.totalEnemyCount / Mathf.Max(1f, averageBurst));
            return $"{seconds:0}s spawn window";
        }

        private static string FormatCurrencyAmount(CurrencyAmount amount)
        {
            return $"{amount.amount} {FormatCurrencySymbol(amount.currency)}";
        }

        private static string FormatRewardStatus(bool claimed, string reward, string lockedLabel = null)
        {
            if (claimed)
            {
                return $"{reward} - claimed";
            }

            return string.IsNullOrWhiteSpace(lockedLabel) ? $"{reward} - unclaimed" : $"{reward} - {lockedLabel}";
        }

        private static string FormatTowerCodexStats(TowerDefinition tower)
        {
            var text = new StringBuilder();
            text.AppendLine($"Role: {tower.role}");
            text.AppendLine($"Base limit: {tower.perTypeLimit}");
            switch (tower.behavior)
            {
                case TowerBehavior.SlowAura:
                    text.AppendLine($"Range: {tower.range:0.0}");
                    text.AppendLine($"Slow: {tower.slowPercent:0}%");
                    text.AppendLine($"Slow capacity: {tower.slowCapacity:0.0} mass");
                    text.Append("Projectile: none");
                    break;
                case TowerBehavior.Barrier:
                    text.AppendLine($"Health: {tower.health:0}");
                    text.AppendLine($"Thorns: {tower.thornsDamage:0.0}");
                    text.Append("Projectile: none");
                    break;
                case TowerBehavior.Barracks:
                    text.AppendLine($"Unit: {tower.barracksUnitType}");
                    text.AppendLine($"Capacity: {tower.barracksCapacity} slots");
                    text.AppendLine($"Respawn: {tower.barracksRespawnSeconds:0.0}s");
                    text.AppendLine($"Unit health: {tower.alliedUnitHealth:0.0}");
                    text.AppendLine($"Unit damage: {tower.alliedUnitDamage:0.0}");
                    text.AppendLine($"Unit defense: {tower.alliedUnitDefense:0.0}");
                    text.AppendLine($"Unit range: {tower.alliedUnitRange:0.0}");
                    text.AppendLine($"Move speed: {tower.alliedUnitMoveSpeed:0.0}");
                    text.Append($"Block capacity: {tower.alliedUnitBlockCapacity:0.0} mass");
                    break;
                default:
                    var projectileLine = tower.projectilePattern == ProjectilePattern.ArcSplash
                        ? $"Projectile: arcing splash\nSplash radius: {tower.splashRadius:0.0}\nKnockback: {tower.knockbackDistance:0.0}"
                        : $"Projectile: single target\nPierce: {tower.pierce}";
                    var fireLine = tower.appliesFire
                        ? $"\nFire: {tower.fireDamagePerTick:0.0} damage/tick, {tower.fireTicksPerSecond:0.0} ticks/sec, {tower.fireMaxStacks} max stacks, {tower.fireDuration:0.0}s"
                        : string.Empty;
                    text.AppendLine($"Damage: {tower.damage:0.0} per hit");
                    text.AppendLine($"Range: {tower.range:0.0}");
                    text.AppendLine($"Fire rate: {1f / Mathf.Max(0.01f, tower.fireInterval):0.0}/sec");
                    text.AppendLine($"Can hit flying: {(tower.canHitFlying ? "yes" : "no")}");
                    text.Append($"{projectileLine}{fireLine}");
                    break;
            }

            return text.ToString();
        }

        private static string FormatEnemyCodexStats(EnemyDefinition enemy)
        {
            var text = new StringBuilder();
            text.AppendLine($"Role: {enemy.role}");
            text.AppendLine($"Health: {enemy.maxHealth:0}");
            text.AppendLine($"Speed: {enemy.speed:0.0}");
            text.AppendLine($"Mass: {enemy.mass:0.0}");
            text.AppendLine($"Attack: {enemy.attackDamage:0.0} every {enemy.attackInterval:0.0}s");
            text.AppendLine($"Vs barriers: x{enemy.wallDamageMultiplier:0.0}");
            text.AppendLine($"Vs allied units: x{enemy.alliedDamageMultiplier:0.0}");
            text.AppendLine($"Life damage: {enemy.lifeDamage}");
            text.AppendLine($"Kill reward: 2 {FormatCurrencySymbol(CurrencyType.KillEssence)} per 10 reward mass");

            var abilities = new List<string>();
            if (enemy.isFlying)
            {
                abilities.Add("Flying");
            }
            if (enemy.healsEnemies)
            {
                abilities.Add($"Heals allies for {enemy.healAmount:0.0}");
            }
            if (enemy.drainsAllies)
            {
                abilities.Add("Drains allied units and can raise max health");
            }
            if (enemy.infectsAllies)
            {
                abilities.Add("Infects killed allied units");
            }
            if (enemy.revivesOnce)
            {
                abilities.Add("Revives once at 50% health");
            }

            if (abilities.Count > 0)
            {
                text.Append("\nAbilities: ");
                text.Append(string.Join(", ", abilities));
            }

            return text.ToString();
        }

        private void OnCodexListScrolled(float scrollDelta)
        {
            codexScroll -= scrollDelta * 28f;
            ApplyCodexScroll(GetCodexEntries().Count);
        }

        private void OnCodexDetailScrolled(float scrollDelta)
        {
            codexDetailScroll -= scrollDelta * 34f;
            ApplyCodexDetailScroll();
        }

        private void ApplyCodexScroll(int entryCount)
        {
            if (codexListContent == null)
            {
                return;
            }

            var contentHeight = Mathf.Max(400f, entryCount * 30f + 18f);
            codexListContent.sizeDelta = new Vector2(codexListContent.sizeDelta.x, contentHeight);
            var maxScroll = Mathf.Max(0f, contentHeight - 400f);
            codexScroll = Mathf.Clamp(codexScroll, 0f, maxScroll);
            codexListContent.anchoredPosition = new Vector2(0f, codexScroll);
        }

        private void ApplyCodexDetailScroll()
        {
            if (codexDetailContent == null || codexDetailText == null)
            {
                return;
            }

            var textRect = codexDetailText.GetComponent<RectTransform>();
            var contentHeight = Mathf.Max(360f, codexDetailText.preferredHeight + 34f);
            codexDetailContent.sizeDelta = new Vector2(codexDetailContent.sizeDelta.x, contentHeight);
            if (textRect != null)
            {
                textRect.sizeDelta = new Vector2(textRect.sizeDelta.x, contentHeight - 28f);
            }

            var maxScroll = Mathf.Max(0f, contentHeight - 360f);
            codexDetailScroll = Mathf.Clamp(codexDetailScroll, 0f, maxScroll);
            codexDetailContent.anchoredPosition = new Vector2(0f, codexDetailScroll);
        }

        private static bool ContainsTower(IReadOnlyList<TowerDefinition> towerList, TowerDefinition tower)
        {
            if (towerList == null || tower == null)
            {
                return false;
            }

            for (var i = 0; i < towerList.Count; i++)
            {
                if (towerList[i] == tower)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateUpgradePanel()
        {
            if (upgradePanel == null || !upgradePanel.activeSelf)
            {
                return;
            }

            var profile = session.Profile;
            upgradeCurrencyText.text = $"{FormatCurrencyBalance(profile, CurrencyType.KillEssence)}   {FormatCurrencyBalance(profile, CurrencyType.VictorySigil)}   {FormatCurrencyBalance(profile, CurrencyType.PerfectSigil)}   {FormatCurrencyBalance(profile, CurrencyType.ChallengeToken)}   {FormatCurrencyBalance(profile, CurrencyType.BossCore)}";

            UpdateUpgradeTreeVisibility();

            foreach (var button in upgradePanel.GetComponentsInChildren<Button>(true))
            {
                if (!button.name.StartsWith("Node_"))
                {
                    continue;
                }

                var nodeId = button.name.Substring(5);
                var node = FindNode(session.UpgradeNodes, nodeId);
                var label = button.GetComponentInChildren<Text>();
                var image = button.targetGraphic as Image;
                if (node == null)
                {
                    continue;
                }

                var rank = session.GetUpgradeRank(nodeId);
                var maxRank = session.GetUpgradeMaxRank(nodeId);
                label.text = $"{rank}/{maxRank}";
                var icon = button.GetComponentInChildren<SkillTreeIconGraphic>();
                var outline = button.GetComponent<Outline>();
                var isInspected = selectedUpgradeNode == node || hoveredUpgradeNode == node;

                if (rank >= maxRank)
                {
                    button.interactable = true;
                    if (image != null)
                    {
                        image.color = new Color(0.08f, 0.58f, 0.54f, 1f);
                    }
                    label.color = new Color(0.6f, 1f, 0.85f, 1f);
                    if (icon != null) icon.color = new Color(0.68f, 1f, 0.86f, 1f);
                }
                else if (session.CanPurchaseUpgrade(nodeId))
                {
                    button.interactable = true;
                    if (image != null)
                    {
                        image.color = new Color(0.95f, 0.5f, 0.12f, 1f);
                    }
                    label.color = new Color(1f, 0.86f, 0.35f, 1f);
                    if (icon != null) icon.color = new Color(1f, 0.9f, 0.5f, 1f);
                }
                else
                {
                    button.interactable = true;
                    if (image != null)
                    {
                        image.color = new Color(0.08f, 0.2f, 0.32f, 1f);
                    }
                    label.color = new Color(0.45f, 0.55f, 0.65f, 1f);
                    if (icon != null) icon.color = new Color(0.4f, 0.54f, 0.66f, 1f);
                }

                if (outline != null)
                {
                    outline.effectColor = isInspected
                        ? new Color(0.96f, 0.9f, 0.42f, 1f)
                        : rank >= maxRank
                            ? new Color(0.18f, 0.9f, 0.72f, 0.85f)
                            : new Color(0.18f, 0.67f, 0.9f, 0.72f);
                }
            }

            UpdateSelectedUpgradeDetails();
        }

        private void UpdateUpgradeTreeVisibility()
        {
            if (upgradeTreeContent == null)
            {
                return;
            }

            var nodes = session.UpgradeNodes;
            var transforms = upgradeTreeContent.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i];
                if (child == null || child == upgradeTreeContent)
                {
                    continue;
                }

                if (child.name.StartsWith("Node_"))
                {
                    var node = FindNode(nodes, child.name.Substring(5));
                    child.gameObject.SetActive(node != null && IsUpgradeNodeRevealed(node));
                }
                else if (child.name.StartsWith("NodeLabel_"))
                {
                    var node = FindNode(nodes, child.name.Substring(10));
                    child.gameObject.SetActive(node != null && IsUpgradeNodeRevealed(node));
                }
                else if (child.name.StartsWith("Link_"))
                {
                    var node = FindNode(nodes, child.name.Substring(5));
                    var visible = node != null && IsUpgradeNodeRevealed(node) && HasAnyPurchasedPrerequisite(node);
                    child.gameObject.SetActive(visible);
                    if (visible && child.TryGetComponent<Image>(out var linkImage))
                    {
                        linkImage.color = session.IsUpgradePurchased(node.id)
                            ? new Color(0.18f, 0.9f, 0.72f, 0.9f)
                            : session.CanPurchaseUpgrade(node.id)
                                ? new Color(0.95f, 0.54f, 0.15f, 0.92f)
                                : new Color(0.15f, 0.48f, 0.68f, 0.52f);
                    }
                }
            }
        }

        private bool IsUpgradeNodeRevealed(SkillNodeDefinition node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.startsUnlocked || session.IsUpgradePurchased(node.id))
            {
                return true;
            }

            return !MissingPrerequisites(node);
        }

        private bool HasAnyPurchasedPrerequisite(SkillNodeDefinition node)
        {
            if (node?.prerequisiteNodeIds == null || node.prerequisiteNodeIds.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < node.prerequisiteNodeIds.Length; i++)
            {
                if (session.IsUpgradePurchased(node.prerequisiteNodeIds[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateSelectedUpgradeDetails()
        {
            var inspectedNode = hoveredUpgradeNode ?? selectedUpgradeNode;
            if (inspectedNode == null || upgradeDetailTitle == null || upgradeDetailBody == null || upgradeDetailCost == null)
            {
                if (upgradeDetailPanel != null)
                {
                    upgradeDetailPanel.SetActive(false);
                }
                return;
            }

            if (upgradeDetailPanel != null)
            {
                upgradeDetailPanel.SetActive(true);
            }

            var rank = session.GetUpgradeRank(inspectedNode.id);
            var maxRank = session.GetUpgradeMaxRank(inspectedNode.id);
            var missingPrerequisites = MissingPrerequisites(inspectedNode);
            upgradeDetailTitle.text = $"{inspectedNode.displayName} — {FormatUpgradeStatNames(inspectedNode)}";
            if (upgradeDetailRank != null)
            {
                upgradeDetailRank.text = $"{rank}/{maxRank}";
            }
            if (upgradeDetailIcon != null)
            {
                upgradeDetailIcon.Kind = ResolveSkillTreeIcon(inspectedNode);
                upgradeDetailIcon.SetVerticesDirty();
            }
            if (missingPrerequisites)
            {
                upgradeDetailBody.text = FormatUpgradeProgression(inspectedNode, rank, maxRank);
                upgradeDetailCost.text = $"LOCKED · {FormatTooltipCosts(session.GetUpgradeNextCosts(inspectedNode.id), false)}";
                return;
            }

            if (rank >= maxRank)
            {
                upgradeDetailBody.text = FormatUpgradeProgression(inspectedNode, rank, maxRank);
                upgradeDetailCost.text = "MAXED";
            }
            else
            {
                upgradeDetailBody.text = FormatUpgradeProgression(inspectedNode, rank, maxRank);
                upgradeDetailCost.text = FormatTooltipCosts(
                    session.GetUpgradeNextCosts(inspectedNode.id),
                    session.CanPurchaseUpgrade(inspectedNode.id));
            }
        }

        private static string FormatUpgradeStatNames(SkillNodeDefinition node)
        {
            if (node?.effects == null || node.effects.Length == 0)
            {
                return "Milestone";
            }

            var names = new List<string>();
            for (var i = 0; i < node.effects.Length; i++)
            {
                var name = FormatEffectStatName(node.effects[i]);
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                {
                    names.Add(name);
                }
            }

            if (names.Count == 0)
            {
                return "Upgrade";
            }

            return names.Count <= 2 ? string.Join(" + ", names) : names[0] + " + More";
        }

        private string FormatUpgradeProgression(SkillNodeDefinition node, int rank, int maxRank)
        {
            if (node?.effects == null || node.effects.Length == 0)
            {
                return rank < maxRank ? "+1 milestone\n(Locked → Unlocked)" : "+1 milestone\n(Unlocked)";
            }

            var remainingRanks = Mathf.Max(0, maxRank - rank);
            var hasNextRank = remainingRanks > 0;
            var text = new StringBuilder();
            for (var i = 0; i < node.effects.Length; i++)
            {
                var increase = FormatEffectRankIncrease(node.effects[i]);
                var transition = FormatEffectProgression(node.effects[i], remainingRanks, hasNextRank);
                var line = string.IsNullOrWhiteSpace(transition)
                    ? increase
                    : node.effects.Length > 1
                        ? $"{increase} {transition}"
                        : $"{increase}\n{transition}";
                if (node.effects.Length > 1)
                {
                    line = line?.Replace("\n", " ");
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.AppendLine();
                }
                text.Append(line);
            }

            return text.Length > 0 ? text.ToString() : "Upgrade";
        }

        private string FormatEffectProgression(UpgradeEffect effect, int remainingRanks, bool hasNextRank)
        {
            var currentBonus = session.GetUpgradeEffectTotal(effect.type, effect.targetId);
            var nextBonus = hasNextRank ? currentBonus + effect.value : currentBonus;
            var maxBonus = currentBonus + effect.value * remainingRanks;
            var tower = session.GetTowerDefinition(effect.targetId);

            switch (effect.type)
            {
                case UpgradeEffectType.UnlockTower:
                    return string.Empty;
                case UpgradeEffectType.PerTypeTowerLimitFlat:
                {
                    var currentValue = tower != null ? tower.perTypeLimit : Mathf.RoundToInt(currentBonus);
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "placement limit");
                }
                case UpgradeEffectType.TowerDamagePercent:
                {
                    var baseDamage = session.GetTowerBaseDamage(effect.targetId);
                    var flatBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerDamageFlat, effect.targetId);
                    return FormatProgressionValues(
                        baseDamage * (1f + currentBonus / 100f) + flatBonus,
                        baseDamage * (1f + nextBonus / 100f) + flatBonus,
                        baseDamage * (1f + maxBonus / 100f) + flatBonus,
                        "damage/hit");
                }
                case UpgradeEffectType.TowerDamageFlat:
                {
                    var baseDamage = session.GetTowerBaseDamage(effect.targetId);
                    var percentBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerDamagePercent, effect.targetId);
                    var percentDamage = baseDamage * (1f + percentBonus / 100f);
                    return FormatProgressionValues(percentDamage + currentBonus, percentDamage + nextBonus, percentDamage + maxBonus, "damage/hit");
                }
                case UpgradeEffectType.TowerFireRatePercent:
                {
                    var baseRate = session.GetTowerBaseFireRate(effect.targetId);
                    var flatBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerFireRateFlat, effect.targetId);
                    return FormatProgressionValues(
                        baseRate * (1f + currentBonus / 100f) + flatBonus,
                        baseRate * (1f + nextBonus / 100f) + flatBonus,
                        baseRate * (1f + maxBonus / 100f) + flatBonus,
                        "shots/sec");
                }
                case UpgradeEffectType.TowerFireRateFlat:
                {
                    var baseRate = session.GetTowerBaseFireRate(effect.targetId);
                    var percentBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerFireRatePercent, effect.targetId);
                    var percentRate = baseRate * (1f + percentBonus / 100f);
                    return FormatProgressionValues(percentRate + currentBonus, percentRate + nextBonus, percentRate + maxBonus, "shots/sec");
                }
                case UpgradeEffectType.TowerProjectileSpeedPercent:
                {
                    var baseSpeed = session.GetTowerBaseProjectileSpeed(effect.targetId);
                    return FormatProgressionValues(
                        baseSpeed * (1f + currentBonus / 100f),
                        baseSpeed * (1f + nextBonus / 100f),
                        baseSpeed * (1f + maxBonus / 100f),
                        "projectile speed");
                }
                case UpgradeEffectType.TowerAimAssistPercent:
                    return FormatProgressionValues(currentBonus, nextBonus, maxBonus, "aim assist", "%");
                case UpgradeEffectType.TowerPierceFlat:
                    return FormatProgressionValues(currentBonus, nextBonus, maxBonus, "pierce");
                case UpgradeEffectType.TowerDoubleShotChancePercent:
                    return FormatProgressionValues(currentBonus, nextBonus, maxBonus, "double-shot chance", "%");
                case UpgradeEffectType.TowerSlowPercentFlat:
                    return FormatProgressionValues(currentBonus, nextBonus, maxBonus, "slow", "%");
                case UpgradeEffectType.TowerSlowCapacityFlat:
                    return FormatProgressionValues(currentBonus, nextBonus, maxBonus, "slow capacity");
                case UpgradeEffectType.TowerRangeFlat:
                {
                    var currentValue = tower != null ? tower.range : currentBonus;
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "range");
                }
                case UpgradeEffectType.TowerHealthFlat:
                {
                    var currentValue = tower != null ? tower.health : currentBonus;
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "health");
                }
                case UpgradeEffectType.TowerThornsDamageFlat:
                {
                    var currentValue = tower != null ? tower.thornsDamage : currentBonus;
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "thorns damage");
                }
                case UpgradeEffectType.BarracksUnitCapacityFlat:
                {
                    var currentValue = tower != null ? tower.barracksCapacity : Mathf.RoundToInt(currentBonus);
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "troop slots");
                }
                case UpgradeEffectType.BarracksUnitDamagePercent:
                {
                    var currentValue = tower != null ? tower.alliedUnitDamage : 0f;
                    return FormatPercentScaledProgression(currentValue, currentBonus, nextBonus, maxBonus, "troop damage");
                }
                case UpgradeEffectType.BarracksUnitHealthPercent:
                {
                    var currentValue = tower != null ? tower.alliedUnitHealth : 0f;
                    return FormatPercentScaledProgression(currentValue, currentBonus, nextBonus, maxBonus, "troop health");
                }
                case UpgradeEffectType.BarracksRespawnCooldownPercent:
                {
                    var currentValue = tower != null ? tower.barracksRespawnSeconds : 0f;
                    var currentFactor = Mathf.Max(0.1f, 1f - currentBonus / 100f);
                    var nextValue = currentValue * Mathf.Max(0.1f, 1f - nextBonus / 100f) / currentFactor;
                    var maxValue = currentValue * Mathf.Max(0.1f, 1f - maxBonus / 100f) / currentFactor;
                    return FormatProgressionValues(currentValue, nextValue, maxValue, "seconds respawn");
                }
                case UpgradeEffectType.EnableTowerFire:
                    return FormatUnlockProgression(currentBonus > 0f, hasNextRank, "Fire locked", "Fire enabled");
                case UpgradeEffectType.TowerFireDamagePerTickFlat:
                {
                    var currentValue = tower != null ? tower.fireDamagePerTick : currentBonus;
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "burn damage/tick");
                }
                case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                {
                    var currentValue = tower != null ? tower.fireTicksPerSecond : currentBonus;
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "burn ticks/sec");
                }
                case UpgradeEffectType.TowerFireMaxStacksFlat:
                {
                    var currentValue = tower != null ? tower.fireMaxStacks : Mathf.RoundToInt(currentBonus);
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "burn stacks");
                }
                case UpgradeEffectType.TowerFireDurationFlat:
                {
                    var currentValue = tower != null ? tower.fireDuration : currentBonus;
                    return FormatProgressionValues(currentValue, currentValue + (hasNextRank ? effect.value : 0f), currentValue + effect.value * remainingRanks, "seconds burn");
                }
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                    return FormatProgressionValues(
                        session.BaseActiveWeaponDamage * (1f + currentBonus / 100f),
                        session.BaseActiveWeaponDamage * (1f + nextBonus / 100f),
                        session.BaseActiveWeaponDamage * (1f + maxBonus / 100f),
                        "damage/hit");
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return FormatProgressionValues(
                        session.BaseActiveWeaponCooldown * Mathf.Max(0.1f, 1f - currentBonus / 100f),
                        session.BaseActiveWeaponCooldown * Mathf.Max(0.1f, 1f - nextBonus / 100f),
                        session.BaseActiveWeaponCooldown * Mathf.Max(0.1f, 1f - maxBonus / 100f),
                        "seconds cooldown");
                case UpgradeEffectType.ActiveWeaponRadiusFlat:
                    return FormatProgressionValues(session.BaseActiveWeaponRadius + currentBonus, session.BaseActiveWeaponRadius + nextBonus, session.BaseActiveWeaponRadius + maxBonus, "radius");
                case UpgradeEffectType.ActiveWeaponPierceFlat:
                    return FormatProgressionValues(session.BaseActiveWeaponMaxTargets + currentBonus, session.BaseActiveWeaponMaxTargets + nextBonus, session.BaseActiveWeaponMaxTargets + maxBonus, "max targets");
                case UpgradeEffectType.ActiveWeaponAutoFireUnlock:
                    return FormatUnlockProgression(currentBonus > 0f, hasNextRank, "Auto-fire locked", "Auto-fire enabled");
                case UpgradeEffectType.BaseLivesFlat:
                    return FormatProgressionValues(session.Level.startingLives + currentBonus, session.Level.startingLives + nextBonus, session.Level.startingLives + maxBonus, "base lives");
                case UpgradeEffectType.LevelEndKillEssenceFlat:
                    return FormatProgressionValues(currentBonus, nextBonus, maxBonus, $"{FormatCurrencySymbol(CurrencyType.KillEssence)} per level");
                case UpgradeEffectType.UnlockEra:
                    return FormatUnlockProgression(currentBonus > 0f, hasNextRank, "Era locked", "Era unlocked");
                default:
                    return FormatProgressionValues(currentBonus, nextBonus, maxBonus, "value");
            }
        }

        private static string FormatProgressionValues(float current, float next, float maximum, string unit, string suffix = "")
        {
            return Mathf.Approximately(current, next)
                ? $"({current:0.#}{suffix})"
                : $"({current:0.#}{suffix} → {next:0.#}{suffix})";
        }

        private static string FormatPercentScaledProgression(float currentValue, float currentBonus, float nextBonus, float maxBonus, string unit)
        {
            var denominator = Mathf.Max(1f, 100f + currentBonus);
            var nextValue = currentValue * (100f + nextBonus) / denominator;
            var maxValue = currentValue * (100f + maxBonus) / denominator;
            return FormatProgressionValues(currentValue, nextValue, maxValue, unit);
        }

        private static string FormatUnlockProgression(bool unlocked, bool hasNextRank, string lockedText = "Locked", string unlockedText = "Unlocked")
        {
            var current = unlocked ? unlockedText : lockedText;
            var next = unlocked || !hasNextRank ? current : unlockedText;
            return current == next ? $"({current})" : $"({current} → {next})";
        }

        private static string FormatEffectStatName(UpgradeEffect effect)
        {
            var target = FormatTargetName(effect.targetId);
            switch (effect.type)
            {
                case UpgradeEffectType.ActiveWeaponDamagePercent: return "Active Weapon Damage";
                case UpgradeEffectType.ActiveWeaponCooldownPercent: return "Active Weapon Cooldown";
                case UpgradeEffectType.ActiveWeaponRadiusFlat: return "Active Weapon Radius";
                case UpgradeEffectType.ActiveWeaponPierceFlat: return "Active Weapon Targets";
                case UpgradeEffectType.ActiveWeaponAutoFireUnlock: return "Active Weapon Auto-Fire";
                case UpgradeEffectType.PerTypeTowerLimitFlat: return $"{target} Limit";
                case UpgradeEffectType.TowerDamageFlat:
                case UpgradeEffectType.TowerDamagePercent: return $"{target} Damage";
                case UpgradeEffectType.TowerFireRateFlat:
                case UpgradeEffectType.TowerFireRatePercent: return $"{target} Fire Rate";
                case UpgradeEffectType.TowerProjectileSpeedPercent: return $"{target} Projectile Speed";
                case UpgradeEffectType.TowerAimAssistPercent: return $"{target} Aim Assist";
                case UpgradeEffectType.TowerPierceFlat: return $"{target} Pierce";
                case UpgradeEffectType.TowerDoubleShotChancePercent: return $"{target} Double Shot";
                case UpgradeEffectType.TowerSlowPercentFlat: return $"{target} Slow";
                case UpgradeEffectType.TowerSlowCapacityFlat: return $"{target} Slow Capacity";
                case UpgradeEffectType.TowerRangeFlat: return $"{target} Range";
                case UpgradeEffectType.TowerHealthFlat: return $"{target} Health";
                case UpgradeEffectType.TowerThornsDamageFlat: return $"{target} Thorns";
                case UpgradeEffectType.BarracksUnitCapacityFlat: return $"{target} Troop Slots";
                case UpgradeEffectType.BarracksUnitDamagePercent: return $"{target} Troop Damage";
                case UpgradeEffectType.BarracksUnitHealthPercent: return $"{target} Troop Health";
                case UpgradeEffectType.BarracksRespawnCooldownPercent: return $"{target} Respawn";
                case UpgradeEffectType.EnableTowerFire: return $"{target} Fire";
                case UpgradeEffectType.TowerFireDamagePerTickFlat: return $"{target} Burn Damage";
                case UpgradeEffectType.TowerFireTicksPerSecondFlat: return $"{target} Burn Rate";
                case UpgradeEffectType.TowerFireMaxStacksFlat: return $"{target} Burn Stacks";
                case UpgradeEffectType.TowerFireDurationFlat: return $"{target} Burn Duration";
                case UpgradeEffectType.BaseLivesFlat: return "Base Lives";
                case UpgradeEffectType.LevelEndKillEssenceFlat: return "Level Essence";
                case UpgradeEffectType.UnlockTower: return $"{target} Unlock";
                case UpgradeEffectType.UnlockEra: return $"{effect.targetId} Era";
                default: return "Upgrade";
            }
        }

        private static string FormatNextRankIncrease(SkillNodeDefinition node)
        {
            if (node?.effects == null || node.effects.Length == 0)
            {
                return "Milestone";
            }

            var text = new StringBuilder();
            for (var i = 0; i < node.effects.Length; i++)
            {
                var increase = FormatEffectRankIncrease(node.effects[i]);
                if (string.IsNullOrWhiteSpace(increase))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append(" / ");
                }
                text.Append(increase);
            }

            return text.Length > 0 ? text.ToString() : "Upgrade";
        }

        private static string FormatEffectRankIncrease(UpgradeEffect effect)
        {
            switch (effect.type)
            {
                case UpgradeEffectType.ActiveWeaponDamagePercent: return $"+{effect.value:0.#}% damage";
                case UpgradeEffectType.ActiveWeaponCooldownPercent: return $"-{effect.value:0.#}% cooldown";
                case UpgradeEffectType.ActiveWeaponRadiusFlat: return $"+{effect.value:0.#} radius";
                case UpgradeEffectType.ActiveWeaponPierceFlat: return $"+{effect.value:0.#} targets";
                case UpgradeEffectType.ActiveWeaponAutoFireUnlock: return "Enable auto-fire";
                case UpgradeEffectType.PerTypeTowerLimitFlat: return $"+{effect.value:0.#} limit";
                case UpgradeEffectType.TowerDamageFlat: return $"+{effect.value:0.#} damage";
                case UpgradeEffectType.TowerDamagePercent: return $"+{effect.value:0.#}% damage";
                case UpgradeEffectType.TowerFireRateFlat: return $"+{effect.value:0.#} shots / second";
                case UpgradeEffectType.TowerFireRatePercent: return $"+{effect.value:0.#}% fire rate";
                case UpgradeEffectType.TowerProjectileSpeedPercent: return $"+{effect.value:0.#}% speed";
                case UpgradeEffectType.TowerAimAssistPercent: return $"+{effect.value:0.#}% aim assist";
                case UpgradeEffectType.TowerPierceFlat: return $"+{effect.value:0.#} pierce";
                case UpgradeEffectType.TowerDoubleShotChancePercent: return $"+{effect.value:0.#}% double shot";
                case UpgradeEffectType.TowerSlowPercentFlat: return $"+{effect.value:0.#}% slow";
                case UpgradeEffectType.TowerSlowCapacityFlat: return $"+{effect.value:0.#} capacity";
                case UpgradeEffectType.TowerRangeFlat: return $"+{effect.value:0.#} range";
                case UpgradeEffectType.TowerHealthFlat: return $"+{effect.value:0.#} health";
                case UpgradeEffectType.TowerThornsDamageFlat: return $"+{effect.value:0.#} thorns";
                case UpgradeEffectType.BarracksUnitCapacityFlat: return $"+{effect.value:0.#} slot";
                case UpgradeEffectType.BarracksUnitDamagePercent: return $"+{effect.value:0.#}% troop damage";
                case UpgradeEffectType.BarracksUnitHealthPercent: return $"+{effect.value:0.#}% troop health";
                case UpgradeEffectType.BarracksRespawnCooldownPercent: return $"-{effect.value:0.#}% respawn";
                case UpgradeEffectType.EnableTowerFire: return "Enable fire";
                case UpgradeEffectType.TowerFireDamagePerTickFlat: return $"+{effect.value:0.#} burn damage";
                case UpgradeEffectType.TowerFireTicksPerSecondFlat: return $"+{effect.value:0.#} ticks/sec";
                case UpgradeEffectType.TowerFireMaxStacksFlat: return $"+{effect.value:0.#} stack";
                case UpgradeEffectType.TowerFireDurationFlat: return $"+{effect.value:0.#}s duration";
                case UpgradeEffectType.BaseLivesFlat: return $"+{effect.value:0.#} life";
                case UpgradeEffectType.LevelEndKillEssenceFlat: return $"+{effect.value:0.#} essence";
                case UpgradeEffectType.UnlockTower: return FormatTowerUnlockText(effect.targetId);
                case UpgradeEffectType.UnlockEra: return "Unlock era";
                default: return string.Empty;
            }
        }

        private static string FormatTowerUnlockText(string targetId)
        {
            var target = FormatTargetName(targetId);
            if (IsBarracksTarget(targetId) || targetId == "barrier")
            {
                return $"Unlocks the \"{target}\"";
            }

            const string towerSuffix = " Tower";
            if (target.EndsWith(towerSuffix, StringComparison.Ordinal))
            {
                target = target.Substring(0, target.Length - towerSuffix.Length);
            }

            return $"Unlocks the \"{target}\" tower";
        }

        private string FormatPrerequisiteNames(SkillNodeDefinition node)
        {
            if (node?.prerequisiteNodeIds == null || node.prerequisiteNodeIds.Length == 0)
            {
                return "None";
            }

            var result = new StringBuilder();
            for (var i = 0; i < node.prerequisiteNodeIds.Length; i++)
            {
                if (i > 0)
                {
                    result.Append(", ");
                }
                var prerequisite = FindNode(session.UpgradeNodes, node.prerequisiteNodeIds[i]);
                result.Append(prerequisite != null ? prerequisite.displayName : node.prerequisiteNodeIds[i]);
            }
            return result.ToString();
        }

        private bool MissingPrerequisites(SkillNodeDefinition node)
        {
            if (node.prerequisiteNodeIds == null)
            {
                return false;
            }

            for (var i = 0; i < node.prerequisiteNodeIds.Length; i++)
            {
                if (!session.IsUpgradePurchased(node.prerequisiteNodeIds[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static SkillNodeDefinition FindNode(IReadOnlyList<SkillNodeDefinition> nodes, string id)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].id == id)
                {
                    return nodes[i];
                }
            }

            return null;
        }

        private static SkillTreeIconKind ResolveSkillTreeIcon(SkillNodeDefinition node)
        {
            if (node == null)
            {
                return SkillTreeIconKind.Generic;
            }

            if (!string.IsNullOrWhiteSpace(node.iconKey) && TryParseSkillTreeIcon(node.iconKey, out var explicitIcon))
            {
                return explicitIcon;
            }

            var id = (node.id ?? string.Empty).ToLowerInvariant();
            if (id.Contains("barracks") || id.Contains("chapter") || id.Contains("quarters") || id.Contains("muster") || id.Contains("bunks"))
            {
                return SkillTreeIconKind.Barracks;
            }
            if (id.Contains("barrier") || id.Contains("timber")) return SkillTreeIconKind.Wall;
            if (id.Contains("fire") || id.Contains("flame") || id.Contains("pitch") || id.Contains("tar")) return SkillTreeIconKind.Fire;
            if (id.Contains("slow") || id.Contains("bell")) return SkillTreeIconKind.Slow;
            if (id.Contains("health") || id.Contains("mail") || id.Contains("jack") || id.Contains("vow")) return SkillTreeIconKind.Health;
            if (id.Contains("range") || id.Contains("perch") || id.Contains("aim")) return SkillTreeIconKind.Range;
            if (id.Contains("cooldown") || id.Contains("respawn") || id.Contains("ready")) return SkillTreeIconKind.Cooldown;
            if (id.Contains("speed") || id.Contains("swift") || id.Contains("quick")) return SkillTreeIconKind.Speed;
            if (id.Contains("pierce") || id.Contains("bolt") || id.Contains("skewer")) return SkillTreeIconKind.Pierce;
            if (id.Contains("damage") || id.Contains("steel") || id.Contains("arrow")) return SkillTreeIconKind.Damage;
            if (id.Contains("capacity") || id.Contains("additional") || id.Contains("limit")) return SkillTreeIconKind.Capacity;
            if (id.Contains("tithe") || id.Contains("essence")) return SkillTreeIconKind.Economy;

            if (node.effects != null)
            {
                for (var i = 0; i < node.effects.Length; i++)
                {
                    switch (node.effects[i].type)
                    {
                        case UpgradeEffectType.UnlockEra: return SkillTreeIconKind.Era;
                        case UpgradeEffectType.BaseLivesFlat:
                        case UpgradeEffectType.TowerHealthFlat:
                        case UpgradeEffectType.BarracksUnitHealthPercent: return SkillTreeIconKind.Health;
                        case UpgradeEffectType.LevelEndKillEssenceFlat: return SkillTreeIconKind.Economy;
                        case UpgradeEffectType.TowerRangeFlat:
                        case UpgradeEffectType.TowerAimAssistPercent:
                        case UpgradeEffectType.ActiveWeaponRadiusFlat: return SkillTreeIconKind.Range;
                        case UpgradeEffectType.ActiveWeaponCooldownPercent:
                        case UpgradeEffectType.BarracksRespawnCooldownPercent: return SkillTreeIconKind.Cooldown;
                        case UpgradeEffectType.TowerProjectileSpeedPercent:
                        case UpgradeEffectType.TowerFireRateFlat:
                        case UpgradeEffectType.TowerFireRatePercent: return SkillTreeIconKind.Speed;
                        case UpgradeEffectType.TowerPierceFlat:
                        case UpgradeEffectType.ActiveWeaponPierceFlat: return SkillTreeIconKind.Pierce;
                        case UpgradeEffectType.TowerSlowPercentFlat:
                        case UpgradeEffectType.TowerSlowCapacityFlat: return SkillTreeIconKind.Slow;
                        case UpgradeEffectType.TowerThornsDamageFlat: return SkillTreeIconKind.Defense;
                        case UpgradeEffectType.EnableTowerFire:
                        case UpgradeEffectType.TowerFireDamagePerTickFlat:
                        case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                        case UpgradeEffectType.TowerFireMaxStacksFlat:
                        case UpgradeEffectType.TowerFireDurationFlat: return SkillTreeIconKind.Fire;
                        case UpgradeEffectType.PerTypeTowerLimitFlat:
                        case UpgradeEffectType.BarracksUnitCapacityFlat: return SkillTreeIconKind.Capacity;
                        case UpgradeEffectType.BarracksUnitDamagePercent:
                        case UpgradeEffectType.TowerDamageFlat:
                        case UpgradeEffectType.TowerDamagePercent:
                        case UpgradeEffectType.ActiveWeaponDamagePercent: return SkillTreeIconKind.Damage;
                        case UpgradeEffectType.UnlockTower:
                        {
                            var target = (node.effects[i].targetId ?? string.Empty).ToLowerInvariant();
                            if (target.Contains("barracks")) return SkillTreeIconKind.Barracks;
                            if (target.Contains("barrier")) return SkillTreeIconKind.Wall;
                            if (target.Contains("archer")) return SkillTreeIconKind.Bow;
                            return SkillTreeIconKind.Tower;
                        }
                    }
                }
            }

            return id.Contains("volley") ? SkillTreeIconKind.Bow : node.isMajorUnlock ? SkillTreeIconKind.Tower : SkillTreeIconKind.Generic;
        }

        private static bool TryParseSkillTreeIcon(string key, out SkillTreeIconKind kind)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "bow": kind = SkillTreeIconKind.Bow; return true;
                case "tower": kind = SkillTreeIconKind.Tower; return true;
                case "damage": kind = SkillTreeIconKind.Damage; return true;
                case "speed": kind = SkillTreeIconKind.Speed; return true;
                case "range": kind = SkillTreeIconKind.Range; return true;
                case "capacity": kind = SkillTreeIconKind.Capacity; return true;
                case "health": kind = SkillTreeIconKind.Health; return true;
                case "cooldown": kind = SkillTreeIconKind.Cooldown; return true;
                case "fire": kind = SkillTreeIconKind.Fire; return true;
                case "slow": kind = SkillTreeIconKind.Slow; return true;
                case "defense": kind = SkillTreeIconKind.Defense; return true;
                case "economy": kind = SkillTreeIconKind.Economy; return true;
                case "barracks": kind = SkillTreeIconKind.Barracks; return true;
                case "wall": kind = SkillTreeIconKind.Wall; return true;
                case "splash": kind = SkillTreeIconKind.Splash; return true;
                case "pierce": kind = SkillTreeIconKind.Pierce; return true;
                case "era": kind = SkillTreeIconKind.Era; return true;
                default: kind = SkillTreeIconKind.Generic; return false;
            }
        }

        private static string FormatCosts(CurrencyAmount[] costs)
        {
            if (costs == null || costs.Length == 0)
            {
                return "Free";
            }

            var text = new StringBuilder();
            for (var i = 0; i < costs.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(costs[i].amount);
                text.Append(' ');
                text.Append(FormatCurrencySymbol(costs[i].currency));
            }

            return text.ToString();
        }

        private static string FormatTooltipCosts(CurrencyAmount[] costs, bool affordable)
        {
            if (costs == null || costs.Length == 0)
            {
                return "FREE";
            }

            var text = new StringBuilder();
            for (var i = 0; i < costs.Length; i++)
            {
                if (i > 0)
                {
                    text.Append("   ");
                }

                text.Append(affordable ? "<color=#ffffff>" : "<color=#ff3d38>");
                text.Append(costs[i].amount);
                text.Append("</color> <color=#ffb52e>");
                text.Append(FormatCurrencySymbol(costs[i].currency));
                text.Append("</color>");
            }

            return text.ToString();
        }

        private static string FormatCurrencySymbol(CurrencyType currency)
        {
            switch (currency)
            {
                case CurrencyType.KillEssence:
                    return "●";
                case CurrencyType.VictorySigil:
                    return "◆";
                case CurrencyType.PerfectSigil:
                    return "◇";
                case CurrencyType.ChallengeToken:
                    return "▲";
                case CurrencyType.BossCore:
                    return "■";
                default:
                    return "?";
            }
        }

        private static string FormatCurrencyBalance(PlayerProfile profile, CurrencyType currency)
        {
            return $"{profile.GetCurrency(currency)} {FormatCurrencySymbol(currency)}";
        }

        private string FormatUpgradePreview(SkillNodeDefinition node)
        {
            if (node?.effects == null || node.effects.Length == 0)
            {
                return "Unlock or milestone";
            }

            if (TryFormatCatapultFireUnlock(node.effects, out var groupedText))
            {
                return groupedText;
            }

            var text = new StringBuilder();
            for (var i = 0; i < node.effects.Length; i++)
            {
                var line = FormatEffectPreview(node.effects[i]);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.AppendLine();
                }

                text.Append(line);
            }

            return text.Length == 0 ? FormatEffects(node.effects) : text.ToString();
        }

        private string FormatCurrentUpgradeStats(SkillNodeDefinition node)
        {
            if (node?.effects == null || node.effects.Length == 0)
            {
                return "Unlocked";
            }

            var text = new StringBuilder();
            for (var i = 0; i < node.effects.Length; i++)
            {
                var line = FormatEffectCurrentValue(node.effects[i]);
                if (node.effects.Length > 1)
                {
                    line = line?.Replace("\n", " · ");
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.AppendLine();
                }

                text.Append(line);
            }

            return text.Length == 0 ? "Unlocked" : text.ToString();
        }

        private string FormatEffectCurrentValue(UpgradeEffect effect)
        {
            var current = session.GetUpgradeEffectTotal(effect.type, effect.targetId);
            var tower = session.GetTowerDefinition(effect.targetId);

            switch (effect.type)
            {
                case UpgradeEffectType.UnlockTower:
                    return current > 0f ? "Unlocked" : "Not unlocked";
                case UpgradeEffectType.PerTypeTowerLimitFlat:
                {
                    var baseLimit = tower != null ? tower.perTypeLimit : 0;
                    return $"+{Mathf.RoundToInt(current)} total\n{baseLimit + Mathf.RoundToInt(current)} placement limit";
                }
                case UpgradeEffectType.TowerDamagePercent:
                {
                    var baseDamage = session.GetTowerBaseDamage(effect.targetId);
                    var flatBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerDamageFlat, effect.targetId);
                    var currentDamage = baseDamage * (1f + current / 100f) + flatBonus;
                    return $"+{current:0}% total\n{currentDamage:0.#} damage/hit";
                }
                case UpgradeEffectType.TowerDamageFlat:
                {
                    var baseDamage = session.GetTowerBaseDamage(effect.targetId);
                    var percentBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerDamagePercent, effect.targetId);
                    var currentDamage = baseDamage * (1f + percentBonus / 100f) + current;
                    return $"+{current:0.#} damage total\n{currentDamage:0.#} damage/hit";
                }
                case UpgradeEffectType.TowerFireRatePercent:
                {
                    var baseRate = session.GetTowerBaseFireRate(effect.targetId);
                    var flatBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerFireRateFlat, effect.targetId);
                    var currentRate = baseRate * (1f + current / 100f) + flatBonus;
                    return $"+{current:0}% total\n{currentRate:0.#} shots/sec";
                }
                case UpgradeEffectType.TowerFireRateFlat:
                {
                    var baseRate = session.GetTowerBaseFireRate(effect.targetId);
                    var percentBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerFireRatePercent, effect.targetId);
                    var currentRate = baseRate * (1f + percentBonus / 100f) + current;
                    return $"+{current:0.#} shots/sec total\n{currentRate:0.#} shots/sec";
                }
                case UpgradeEffectType.TowerProjectileSpeedPercent:
                {
                    var baseSpeed = session.GetTowerBaseProjectileSpeed(effect.targetId);
                    var currentSpeed = baseSpeed * (1f + current / 100f);
                    return $"+{current:0}% total\n{currentSpeed:0.#} projectile speed";
                }
                case UpgradeEffectType.TowerAimAssistPercent:
                    return $"+{current:0}% total\n{current:0}% attraction";
                case UpgradeEffectType.TowerPierceFlat:
                    return $"+{Mathf.RoundToInt(current)} total\n{Mathf.RoundToInt(current)} extra pierce";
                case UpgradeEffectType.TowerDoubleShotChancePercent:
                    return $"+{current:0}% total\n{current:0}% double-shot chance";
                case UpgradeEffectType.TowerSlowPercentFlat:
                    return $"+{current:0}% total\n{current:0}% slow";
                case UpgradeEffectType.TowerSlowCapacityFlat:
                    return $"+{current:0.#} total\n{current:0.#} slow capacity";
                case UpgradeEffectType.TowerRangeFlat:
                    return $"+{current:0.#} range total\n{(tower != null ? tower.range : current):0.#} range";
                case UpgradeEffectType.TowerHealthFlat:
                    return $"+{current:0.#} health total\n{(tower != null ? tower.health : current):0.#} health";
                case UpgradeEffectType.TowerThornsDamageFlat:
                    return $"+{current:0.#} damage total\n{current:0.#} thorns damage";
                case UpgradeEffectType.BarracksUnitCapacityFlat:
                    return $"+{Mathf.RoundToInt(current)} slots total\n{(tower != null ? tower.barracksCapacity : Mathf.RoundToInt(current))} troop slots";
                case UpgradeEffectType.BarracksUnitDamagePercent:
                    return $"+{current:0}% total\n{(tower != null ? tower.alliedUnitDamage : 0f):0.#} troop damage";
                case UpgradeEffectType.BarracksUnitHealthPercent:
                    return $"+{current:0}% total\n{(tower != null ? tower.alliedUnitHealth : 0f):0.#} troop health";
                case UpgradeEffectType.BarracksRespawnCooldownPercent:
                    return $"-{current:0}% total\n{(tower != null ? tower.barracksRespawnSeconds : 0f):0.#}s respawn";
                case UpgradeEffectType.EnableTowerFire:
                    return current > 0f ? "Fire unlocked" : "Fire locked";
                case UpgradeEffectType.TowerFireDamagePerTickFlat:
                    return $"+{current:0.#} total\n{current:0.#} burn damage/tick";
                case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                    return $"+{current:0.#} total\n{current:0.#} burn ticks/sec";
                case UpgradeEffectType.TowerFireMaxStacksFlat:
                    return $"+{Mathf.RoundToInt(current)} total\n{Mathf.RoundToInt(current)} burn stacks";
                case UpgradeEffectType.TowerFireDurationFlat:
                    return $"+{current:0.#}s total\n{current:0.#}s burn duration";
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                    return $"+{current:0}% total\n{session.BaseActiveWeaponDamage * (1f + current / 100f):0.#} damage/hit";
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return $"-{current:0}% total\n{session.BaseActiveWeaponCooldown * Mathf.Max(0.1f, 1f - current / 100f):0.#}s cooldown";
                case UpgradeEffectType.ActiveWeaponRadiusFlat:
                    return $"+{current:0.#} radius total\n{session.BaseActiveWeaponRadius + current:0.#} radius";
                case UpgradeEffectType.ActiveWeaponPierceFlat:
                    return $"+{Mathf.RoundToInt(current)} targets total\n{session.BaseActiveWeaponMaxTargets + Mathf.RoundToInt(current)} max targets";
                case UpgradeEffectType.ActiveWeaponAutoFireUnlock:
                    return current > 0f ? "Auto-fire unlocked" : "Auto-fire locked";
                case UpgradeEffectType.BaseLivesFlat:
                    return $"+{Mathf.RoundToInt(current)} lives total\n{session.Level.startingLives + Mathf.RoundToInt(current)} base lives";
                case UpgradeEffectType.LevelEndKillEssenceFlat:
                    return $"+{Mathf.RoundToInt(current)} total\n{Mathf.RoundToInt(current)} {FormatCurrencySymbol(CurrencyType.KillEssence)} after each level";
                case UpgradeEffectType.UnlockEra:
                    return current > 0f ? "Era unlocked" : "Era locked";
                default:
                    return FormatEffect(effect);
            }
        }

        private string FormatEffectPreview(UpgradeEffect effect)
        {
            var current = session.GetUpgradeEffectTotal(effect.type, effect.targetId);
            var next = current + effect.value;
            var target = FormatTargetName(effect.targetId);
            var tower = session.GetTowerDefinition(effect.targetId);

            switch (effect.type)
            {
                case UpgradeEffectType.UnlockTower:
                    return $"Unlock {target}";
                case UpgradeEffectType.PerTypeTowerLimitFlat:
                {
                    var baseLimit = tower != null ? tower.perTypeLimit : 0;
                    return $"{target} limit: {baseLimit + Mathf.RoundToInt(current)} -> {baseLimit + Mathf.RoundToInt(next)}";
                }
                case UpgradeEffectType.TowerDamagePercent:
                {
                    var baseDamage = session.GetTowerBaseDamage(effect.targetId);
                    var flatBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerDamageFlat, effect.targetId);
                    var currentDamage = baseDamage * (1f + current / 100f) + flatBonus;
                    var nextDamage = baseDamage * (1f + next / 100f) + flatBonus;
                    return $"{target} bonus damage: {current:0}% -> {next:0}%\nDamage/hit: {currentDamage:0.#} -> {nextDamage:0.#}";
                }
                case UpgradeEffectType.TowerDamageFlat:
                {
                    var baseDamage = session.GetTowerBaseDamage(effect.targetId);
                    var percentBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerDamagePercent, effect.targetId);
                    var currentDamage = baseDamage * (1f + percentBonus / 100f) + current;
                    var nextDamage = baseDamage * (1f + percentBonus / 100f) + next;
                    return $"{target} damage/hit: {currentDamage:0.#} -> {nextDamage:0.#}";
                }
                case UpgradeEffectType.TowerFireRatePercent:
                {
                    var baseRate = session.GetTowerBaseFireRate(effect.targetId);
                    var flatBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerFireRateFlat, effect.targetId);
                    var currentRate = baseRate * (1f + current / 100f) + flatBonus;
                    var nextRate = baseRate * (1f + next / 100f) + flatBonus;
                    return $"{target} fire rate bonus: {current:0}% -> {next:0}%\nShots/sec: {currentRate:0.#} -> {nextRate:0.#}";
                }
                case UpgradeEffectType.TowerFireRateFlat:
                {
                    var baseRate = session.GetTowerBaseFireRate(effect.targetId);
                    var percentBonus = session.GetUpgradeEffectTotal(UpgradeEffectType.TowerFireRatePercent, effect.targetId);
                    var currentRate = baseRate * (1f + percentBonus / 100f) + current;
                    var nextRate = baseRate * (1f + percentBonus / 100f) + next;
                    return $"{target} shots/sec: {currentRate:0.#} -> {nextRate:0.#}";
                }
                case UpgradeEffectType.TowerProjectileSpeedPercent:
                {
                    var baseSpeed = session.GetTowerBaseProjectileSpeed(effect.targetId);
                    var currentSpeed = baseSpeed * (1f + current / 100f);
                    var nextSpeed = baseSpeed * (1f + next / 100f);
                    return $"{target} projectile speed: {current:0}% -> {next:0}%\nSpeed: {currentSpeed:0.#} -> {nextSpeed:0.#}";
                }
                case UpgradeEffectType.TowerAimAssistPercent:
                    return $"Projectile aim assist: {current:0}% -> {next:0}%";
                case UpgradeEffectType.TowerPierceFlat:
                    return $"{target} pierce: {Mathf.RoundToInt(current)} -> {Mathf.RoundToInt(next)}";
                case UpgradeEffectType.TowerDoubleShotChancePercent:
                    return $"{target} double shot chance: {current:0}% -> {next:0}%";
                case UpgradeEffectType.TowerSlowPercentFlat:
                    return $"{target} slow: {current:0}% -> {next:0}%";
                case UpgradeEffectType.TowerSlowCapacityFlat:
                    return $"{target} slow capacity: {current:0.#} -> {next:0.#} mass";
                case UpgradeEffectType.TowerRangeFlat:
                {
                    var currentRange = tower != null ? tower.range : current;
                    return $"{target} range: {currentRange:0.#} -> {currentRange + effect.value:0.#}";
                }
                case UpgradeEffectType.TowerHealthFlat:
                {
                    var currentHealth = tower != null ? tower.health : current;
                    return $"{target} health: {currentHealth:0.#} -> {currentHealth + effect.value:0.#}";
                }
                case UpgradeEffectType.TowerThornsDamageFlat:
                    return $"{target} thorns damage: {current:0.#} -> {next:0.#}";
                case UpgradeEffectType.BarracksUnitCapacityFlat:
                {
                    var currentCapacity = tower != null ? tower.barracksCapacity : Mathf.RoundToInt(current);
                    return $"{target} troop slots: {currentCapacity} -> {currentCapacity + Mathf.RoundToInt(effect.value)}";
                }
                case UpgradeEffectType.BarracksUnitDamagePercent:
                {
                    var currentDamage = tower != null ? tower.alliedUnitDamage : 0f;
                    var nextDamage = currentDamage * (1f + effect.value / Mathf.Max(1f, 100f + current));
                    return $"{target} troop damage bonus: {current:0}% -> {next:0}%\nTroop damage: {currentDamage:0.#} -> {nextDamage:0.#}";
                }
                case UpgradeEffectType.BarracksUnitHealthPercent:
                {
                    var currentHealth = tower != null ? tower.alliedUnitHealth : 0f;
                    var nextHealth = currentHealth * (1f + effect.value / Mathf.Max(1f, 100f + current));
                    return $"{target} troop health bonus: {current:0}% -> {next:0}%\nTroop health: {currentHealth:0.#} -> {nextHealth:0.#}";
                }
                case UpgradeEffectType.BarracksRespawnCooldownPercent:
                {
                    var currentRespawn = tower != null ? tower.barracksRespawnSeconds : 0f;
                    var nextRespawn = currentRespawn * Mathf.Max(0.1f, (100f - next) / Mathf.Max(1f, 100f - current));
                    return $"{target} respawn reduction: {current:0}% -> {next:0}%\nRespawn: {currentRespawn:0.#}s -> {nextRespawn:0.#}s";
                }
                case UpgradeEffectType.EnableTowerFire:
                    return $"Unlock {target} fire";
                case UpgradeEffectType.TowerFireDamagePerTickFlat:
                    return $"{target} burn damage/tick: {current:0.#} -> {next:0.#}";
                case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                    return $"{target} burn ticks/sec: {current:0.#} -> {next:0.#}";
                case UpgradeEffectType.TowerFireMaxStacksFlat:
                    return $"{target} burn stacks: {Mathf.RoundToInt(current)} -> {Mathf.RoundToInt(next)}";
                case UpgradeEffectType.TowerFireDurationFlat:
                    return $"{target} burn duration: {current:0.#}s -> {next:0.#}s";
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                {
                    var baseDamage = session.BaseActiveWeaponDamage;
                    return $"Active weapon bonus damage: {current:0}% -> {next:0}%\nDamage/hit: {baseDamage * (1f + current / 100f):0.#} -> {baseDamage * (1f + next / 100f):0.#}";
                }
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return $"Active weapon cooldown reduction: {current:0}% -> {next:0}%\nCooldown: {session.BaseActiveWeaponCooldown * Mathf.Max(0.1f, 1f - current / 100f):0.#}s -> {session.BaseActiveWeaponCooldown * Mathf.Max(0.1f, 1f - next / 100f):0.#}s";
                case UpgradeEffectType.ActiveWeaponRadiusFlat:
                    return $"Active weapon radius: {session.BaseActiveWeaponRadius + current:0.#} -> {session.BaseActiveWeaponRadius + next:0.#}";
                case UpgradeEffectType.ActiveWeaponPierceFlat:
                    return $"Active weapon targets: {session.BaseActiveWeaponMaxTargets + Mathf.RoundToInt(current)} -> {session.BaseActiveWeaponMaxTargets + Mathf.RoundToInt(next)}";
                case UpgradeEffectType.ActiveWeaponAutoFireUnlock:
                    return "Unlock active weapon auto-fire toggle";
                case UpgradeEffectType.BaseLivesFlat:
                    return $"Base lives: {session.Level.startingLives + Mathf.RoundToInt(current)} -> {session.Level.startingLives + Mathf.RoundToInt(next)}";
                case UpgradeEffectType.LevelEndKillEssenceFlat:
                    return $"Bonus after each level: {Mathf.RoundToInt(current)} -> {Mathf.RoundToInt(next)} {FormatCurrencySymbol(CurrencyType.KillEssence)}";
                case UpgradeEffectType.UnlockEra:
                    return $"Unlock {effect.targetId} era";
                default:
                    return FormatEffect(effect);
            }
        }

        private static string FormatEffects(UpgradeEffect[] effects)
        {
            if (effects == null || effects.Length == 0)
            {
                return "Unlock or milestone";
            }

            if (TryFormatGroupedBarracksEffect(effects, out var groupedText))
            {
                return groupedText;
            }

            if (TryFormatCatapultFireUnlock(effects, out groupedText))
            {
                return groupedText;
            }

            var text = new StringBuilder();
            for (var i = 0; i < effects.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(FormatEffect(effects[i]));
            }

            return text.ToString();
        }

        private static string FormatEffect(UpgradeEffect effect)
        {
            switch (effect.type)
            {
                case UpgradeEffectType.UnlockTower:
                    return $"Unlock {FormatTargetName(effect.targetId)}";
                case UpgradeEffectType.PerTypeTowerLimitFlat:
                    return $"+{effect.value:0} {FormatTargetName(effect.targetId)} limit";
                case UpgradeEffectType.TowerDamageFlat:
                    return $"+{effect.value:0.0} {FormatTargetName(effect.targetId)} damage";
                case UpgradeEffectType.TowerDamagePercent:
                    return $"+{effect.value:0}% {FormatTargetName(effect.targetId)} damage";
                case UpgradeEffectType.TowerFireRateFlat:
                    return $"+{effect.value:0.0} {FormatTargetName(effect.targetId)} shots/sec";
                case UpgradeEffectType.TowerFireRatePercent:
                    return string.IsNullOrWhiteSpace(effect.targetId)
                        ? $"+{effect.value:0}% tower fire rate"
                        : $"+{effect.value:0}% {FormatTargetName(effect.targetId)} fire rate";
                case UpgradeEffectType.TowerProjectileSpeedPercent:
                    return $"+{effect.value:0}% {FormatTargetName(effect.targetId)} projectile speed";
                case UpgradeEffectType.TowerAimAssistPercent:
                    return $"+{effect.value:0}% projectile aim assist";
                case UpgradeEffectType.TowerPierceFlat:
                    return $"+{effect.value:0} {FormatTargetName(effect.targetId)} pierce";
                case UpgradeEffectType.TowerDoubleShotChancePercent:
                    return $"+{effect.value:0}% {FormatTargetName(effect.targetId)} double shot chance";
                case UpgradeEffectType.TowerSlowPercentFlat:
                    return $"+{effect.value:0}% {FormatTargetName(effect.targetId)} slow";
                case UpgradeEffectType.TowerSlowCapacityFlat:
                    return $"+{effect.value:0} {FormatTargetName(effect.targetId)} slow capacity";
                case UpgradeEffectType.TowerRangeFlat:
                    return $"+{effect.value:0.0} {FormatTargetName(effect.targetId)} range";
                case UpgradeEffectType.TowerHealthFlat:
                    return $"+{effect.value:0} {FormatTargetName(effect.targetId)} health";
                case UpgradeEffectType.TowerThornsDamageFlat:
                    return $"+{effect.value:0.0} {FormatTargetName(effect.targetId)} thorns damage";
                case UpgradeEffectType.BarracksUnitCapacityFlat:
                    return $"+{effect.value:0} troop slot for {FormatTargetName(effect.targetId)}";
                case UpgradeEffectType.BarracksUnitDamagePercent:
                    return $"+{effect.value:0}% troop damage for {FormatTargetName(effect.targetId)}";
                case UpgradeEffectType.BarracksUnitHealthPercent:
                    return $"+{effect.value:0}% troop health for {FormatTargetName(effect.targetId)}";
                case UpgradeEffectType.BarracksRespawnCooldownPercent:
                    return $"-{effect.value:0}% respawn time for {FormatTargetName(effect.targetId)}";
                case UpgradeEffectType.EnableTowerFire:
                    return $"Enable {FormatTargetName(effect.targetId)} fire";
                case UpgradeEffectType.TowerFireDamagePerTickFlat:
                    return $"+{effect.value:0.0} {FormatTargetName(effect.targetId)} fire damage/tick";
                case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                    return $"+{effect.value:0.0} {FormatTargetName(effect.targetId)} fire ticks/sec";
                case UpgradeEffectType.TowerFireMaxStacksFlat:
                    return $"+{effect.value:0} {FormatTargetName(effect.targetId)} fire stacks";
                case UpgradeEffectType.TowerFireDurationFlat:
                    return $"+{effect.value:0.0}s {FormatTargetName(effect.targetId)} fire duration";
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                    return $"+{effect.value:0}% active weapon damage";
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return $"-{effect.value:0}% active weapon cooldown";
                case UpgradeEffectType.ActiveWeaponRadiusFlat:
                    return $"+{effect.value:0.0} active weapon radius";
                case UpgradeEffectType.ActiveWeaponPierceFlat:
                    return $"+{effect.value:0} active weapon targets";
                case UpgradeEffectType.ActiveWeaponAutoFireUnlock:
                    return "Unlock active weapon auto-fire";
                case UpgradeEffectType.BaseLivesFlat:
                    return $"+{effect.value:0} base lives";
                case UpgradeEffectType.LevelEndKillEssenceFlat:
                    return $"+{effect.value:0} essence after each level";
                case UpgradeEffectType.UnlockEra:
                    return $"Unlock {effect.targetId} era";
                default:
                    return effect.type.ToString();
            }
        }

        private static bool TryFormatGroupedBarracksEffect(UpgradeEffect[] effects, out string text)
        {
            text = null;
            if (effects.Length < 2)
            {
                return false;
            }

            var type = effects[0].type;
            var value = effects[0].value;
            if (!IsBarracksUnitEffect(type))
            {
                return false;
            }

            for (var i = 0; i < effects.Length; i++)
            {
                if (effects[i].type != type || !Mathf.Approximately(effects[i].value, value) || !IsBarracksTarget(effects[i].targetId))
                {
                    return false;
                }
            }

            switch (type)
            {
                case UpgradeEffectType.BarracksUnitCapacityFlat:
                    text = $"+{value:0} troop slot for every barracks";
                    return true;
                case UpgradeEffectType.BarracksUnitDamagePercent:
                    text = $"+{value:0}% damage for all barracks troops";
                    return true;
                case UpgradeEffectType.BarracksUnitHealthPercent:
                    text = $"+{value:0}% health for all barracks troops";
                    return true;
                case UpgradeEffectType.BarracksRespawnCooldownPercent:
                    text = $"-{value:0}% respawn time for every barracks";
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryFormatCatapultFireUnlock(UpgradeEffect[] effects, out string text)
        {
            text = null;
            if (effects == null || effects.Length == 0)
            {
                return false;
            }

            var enablesCatapultFire = false;
            for (var i = 0; i < effects.Length; i++)
            {
                if (effects[i].type == UpgradeEffectType.EnableTowerFire && effects[i].targetId == "catapult")
                {
                    enablesCatapultFire = true;
                    break;
                }
            }

            if (!enablesCatapultFire)
            {
                return false;
            }

            text = "Catapult boulders ignite enemies on impact";
            return true;
        }

        private static bool IsBarracksUnitEffect(UpgradeEffectType type)
        {
            return type == UpgradeEffectType.BarracksUnitCapacityFlat
                || type == UpgradeEffectType.BarracksUnitDamagePercent
                || type == UpgradeEffectType.BarracksUnitHealthPercent
                || type == UpgradeEffectType.BarracksRespawnCooldownPercent;
        }

        private static bool IsBarracksTarget(string targetId)
        {
            return targetId == "knight_barracks" || targetId == "archer_barracks" || targetId == "paladin_barracks";
        }

        private static string FormatTargetName(string targetId)
        {
            switch (targetId)
            {
                case "archer":
                    return "Archer Tower";
                case "ballista":
                    return "Ballista";
                case "bell":
                    return "Bell Tower";
                case "catapult":
                    return "Catapult";
                case "barrier":
                    return "Timber Barrier";
                case "knight_barracks":
                    return "Knight Barracks";
                case "archer_barracks":
                    return "Archer Post";
                case "paladin_barracks":
                    return "Paladin Chapter";
                default:
                    return string.IsNullOrWhiteSpace(targetId) ? "target" : targetId.Replace('_', ' ');
            }
        }

        private void CreateActiveWeaponSlot(Transform parent)
        {
            var root = new GameObject("ActiveWeaponSlot");
            activeWeaponSlot = root;
            root.transform.SetParent(parent, false);
            var rect = root.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 12f);
            rect.sizeDelta = new Vector2(72f, 86f);

            activeWeaponIcon = CreateImage("Icon", root.transform, new Vector2(0f, 30f), new Vector2(50f, 50f), new Color(0.9f, 0.35f, 0.1f, 1f));
            activeWeaponCooldownFill = CreateImage("CooldownFill", root.transform, new Vector2(0f, 30f), new Vector2(50f, 50f), new Color(0f, 0f, 0f, 0.65f));
            activeWeaponCooldownFill.type = Image.Type.Filled;
            activeWeaponCooldownFill.fillMethod = Image.FillMethod.Vertical;
            activeWeaponCooldownFill.fillOrigin = (int)Image.OriginVertical.Bottom;

            var label = CreateText("Label", root.transform, new Vector2(0f, 11f), TextAnchor.MiddleCenter, 10);
            label.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0f);
            label.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0f);
            label.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
            label.GetComponent<RectTransform>().sizeDelta = new Vector2(110f, 18f);
            label.text = "Active";

            activeWeaponCooldownText = CreateText("CooldownText", root.transform, new Vector2(0f, 51f), TextAnchor.MiddleCenter, 13);
            activeWeaponCooldownText.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0f);
            activeWeaponCooldownText.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0f);
            activeWeaponCooldownText.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
            activeWeaponCooldownText.GetComponent<RectTransform>().sizeDelta = new Vector2(70f, 22f);
        }

        private void UpdateActiveWeaponSlot()
        {
            if (activeWeapon == null || activeWeaponCooldownFill == null || activeWeaponCooldownText == null)
            {
                return;
            }

            activeWeaponIcon.color = activeWeapon.IsReady ? new Color(1f, 0.5f, 0.16f, 1f) : new Color(0.45f, 0.45f, 0.45f, 1f);
            activeWeaponCooldownFill.fillAmount = activeWeapon.CanFire ? 1f - activeWeapon.CooldownProgress : 1f;
            activeWeaponCooldownFill.enabled = !activeWeapon.IsReady;
            activeWeaponCooldownText.text = activeWeapon.AutoFireEnabled
                ? "AUTO"
                : activeWeapon.IsReady
                    ? "OK"
                    : activeWeapon.CanFire ? activeWeapon.CooldownRemaining.ToString("0.0") : "--";
            activeWeaponCooldownText.color = activeWeapon.AutoFireEnabled
                ? new Color(1f, 0.85f, 0.25f, 1f)
                : activeWeapon.IsReady ? new Color(0.6f, 1f, 0.55f, 1f) : Color.white;
        }

        private static Image CreateImage(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, Vector2 anchor, Vector2 pivot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = new Color(0.035f, 0.045f, 0.05f, 0.76f);
            return go;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 anchoredPosition, Vector2 size, int fontSize = 15)
        {
            return CreateAnchoredButton(name, parent, label, anchoredPosition, size, new Vector2(0.5f, 1f), fontSize);
        }

        private static Button CreateAnchoredButton(string name, Transform parent, string label, Vector2 anchoredPosition, Vector2 size, Vector2 anchor, int fontSize = 15)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            ConfigureCenteredRect(rect, anchoredPosition, size, anchor, new Vector2(0.5f, 0.5f));
            var image = go.AddComponent<Image>();
            image.color = new Color(0.15f, 0.45f, 0.82f, 1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateButtonColors();

            var text = CreateText("Label", go.transform, Vector2.zero, TextAnchor.MiddleCenter, fontSize);
            ConfigureCenteredRect(text.GetComponent<RectTransform>(), Vector2.zero, size, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            text.text = label;
            return button;
        }

        private static ColorBlock CreateButtonColors()
        {
            return new ColorBlock
            {
                normalColor = new Color(0.15f, 0.45f, 0.82f, 1f),
                highlightedColor = new Color(0.28f, 0.68f, 1f, 1f),
                pressedColor = new Color(0.08f, 0.28f, 0.62f, 1f),
                selectedColor = new Color(0.22f, 0.58f, 0.94f, 1f),
                disabledColor = new Color(0.08f, 0.16f, 0.26f, 0.72f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
        }

        private static Text CreateText(string name, Transform parent, Vector2 anchoredPosition, TextAnchor anchor, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(760f, 200f);

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void ConfigureCenteredRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size, Vector2 anchor, Vector2 pivot)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        private sealed class SkillTreeViewportInput : MonoBehaviour, IDragHandler, IScrollHandler
        {
            private System.Action<Vector2> onDragged;
            private System.Action<float> onScrolled;

            public void Initialize(System.Action<Vector2> dragged, System.Action<float> scrolled)
            {
                onDragged = dragged;
                onScrolled = scrolled;
            }

            public void OnDrag(PointerEventData eventData)
            {
                onDragged?.Invoke(eventData.delta);
            }

            public void OnScroll(PointerEventData eventData)
            {
                onScrolled?.Invoke(eventData.scrollDelta.y);
            }
        }

        private sealed class CodexListScrollInput : MonoBehaviour, IScrollHandler
        {
            private System.Action<float> onScrolled;

            public void Initialize(System.Action<float> scrolled)
            {
                onScrolled = scrolled;
            }

            public void OnScroll(PointerEventData eventData)
            {
                onScrolled?.Invoke(eventData.scrollDelta.y);
            }
        }

        private sealed class CodexEntry
        {
            public readonly string id;
            public readonly string displayName;
            public readonly string details;

            public CodexEntry(string id, string displayName, string details)
            {
                this.id = id;
                this.displayName = displayName;
                this.details = details;
            }
        }
    }
}
