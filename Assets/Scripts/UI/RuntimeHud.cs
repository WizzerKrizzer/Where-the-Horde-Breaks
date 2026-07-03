using System.Text;
using System.Collections.Generic;
using TowerDefense.Data;
using TowerDefense.Input;
using TowerDefense.Runtime;
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
        private Text towerText;
        private Image activeWeaponIcon;
        private Image activeWeaponCooldownFill;
        private Text activeWeaponCooldownText;
        private Button devSpeed1Button;
        private Button devSpeed2Button;
        private Button devSpeed5Button;
        private Button devSpeed10Button;
        private Button devToggleButton;
        private GameObject resultPanel;
        private Text resultTitle;
        private Text resultBody;
        private GameObject upgradePanel;
        private RectTransform upgradeTreeContent;
        private RectTransform upgradeTreeViewport;
        private readonly List<RectTransform> upgradeTreeLabels = new();
        private Text upgradeCurrencyText;
        private Text upgradeDetailTitle;
        private Text upgradeDetailBody;
        private Button upgradeBuyButton;
        private SkillNodeDefinition selectedUpgradeNode;
        private Vector2 upgradeTreePan;
        private float upgradeTreeZoom = 1f;
        private GameObject devPanel;
        private bool devPanelVisible;
        private Button statsToggleButton;
        private GameObject statsPanel;
        private readonly Dictionary<TowerDefinition, Text> statsRows = new();
        private readonly Dictionary<TowerDefinition, Button> statsRowButtons = new();
        private Text statsDetailText;
        private Text statsEmptyTowerText;
        private Button activeWeaponStatsButton;
        private TowerDefinition selectedStatsTower;
        private bool selectedStatsActiveWeapon;
        private bool statsPanelVisible;

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
            towerText = CreateText("TowerSelection", parent, new Vector2(12f, -84f), TextAnchor.UpperLeft, 13);
            towerText.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 100f);
            CreateActiveWeaponSlot(parent);
            CreateResultPanel(parent);
            CreateUpgradePanel(parent);
            CreateStatsPanel(parent);
            CreateDevPanel(parent);
            CreateTopRightToggles(parent);
        }

        private void Update()
        {
            if (session == null || statusText == null)
            {
                return;
            }

            HandleHudShortcuts();

            var profile = session.Profile;
            var text = new StringBuilder();
            text.AppendLine($"{session.Level.displayName}   Lives: {session.Lives}");
            text.AppendLine($"Spawned: {enemies.TotalSpawned}");
            text.AppendLine($"Essence: {profile.GetCurrency(CurrencyType.KillEssence)}  Victory: {profile.GetCurrency(CurrencyType.VictorySigil)}  Perfect: {profile.GetCurrency(CurrencyType.PerfectSigil)}");
            text.AppendLine($"Challenge: {profile.GetCurrency(CurrencyType.ChallengeToken)}  Boss: {profile.GetCurrency(CurrencyType.BossCore)}");

            if (session.Finished)
            {
                text.AppendLine(session.Won ? "VICTORY - press R to rebuild/replay" : "DEFEAT - press R to adjust towers");
            }

            statusText.text = text.ToString();
            UpdateTowerText();
            UpdateActiveWeaponSlot();
            UpdateDevSpeedButtons();
            UpdateResultPanel();
            UpdateUpgradePanel();
            UpdateStatsPanel();
        }

        private void HandleHudShortcuts()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab))
            {
                ToggleStatsPanel();
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.BackQuote))
            {
                ToggleDevPanel();
            }
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
            resultPanel = CreatePanel("ResultPanel", parent, new Vector2(0f, 112f), new Vector2(380f, 128f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            resultTitle = CreateText("ResultTitle", resultPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 24);
            ConfigureCenteredRect(resultTitle.GetComponent<RectTransform>(), new Vector2(0f, 94f), new Vector2(330f, 30f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            resultBody = CreateText("ResultBody", resultPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 15);
            ConfigureCenteredRect(resultBody.GetComponent<RectTransform>(), new Vector2(0f, 60f), new Vector2(330f, 28f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            CreateButton("RetryButton", resultPanel.transform, "RETRY", new Vector2(-72f, 24f), new Vector2(112f, 26f), 13)
                .onClick.AddListener(() => session.ResetToPlanning());
            CreateButton("OpenUpgradesButton", resultPanel.transform, "UPGRADES", new Vector2(72f, 24f), new Vector2(112f, 26f), 13)
                .onClick.AddListener(ShowUpgradePanel);
            resultPanel.SetActive(false);
        }

        private void CreateUpgradePanel(Transform parent)
        {
            upgradePanel = CreatePanel("UpgradePanel", parent, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var panelRect = upgradePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            upgradePanel.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.024f, 0.94f);

            var title = CreateText("UpgradeTitle", upgradePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 22);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, -22f), new Vector2(460f, 32f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            title.text = "SKILL TREE";

            upgradeCurrencyText = CreateText("UpgradeCurrencies", upgradePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 13);
            ConfigureCenteredRect(upgradeCurrencyText.GetComponent<RectTransform>(), new Vector2(0f, -50f), new Vector2(680f, 24f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));

            var hint = CreateText("UpgradeHint", upgradePanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(hint.GetComponent<RectTransform>(), new Vector2(0f, -72f), new Vector2(680f, 20f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            hint.color = new Color(0.68f, 0.78f, 0.86f, 1f);
            hint.text = "Hover for details. Click a node to buy its next rank. Drag to pan. Mouse wheel zooms.";

            var viewport = CreatePanel("UpgradeTreeViewport", upgradePanel.transform, new Vector2(0f, -94f), Vector2.zero, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            upgradeTreeViewport = viewport.GetComponent<RectTransform>();
            upgradeTreeViewport.anchorMin = new Vector2(0f, 0f);
            upgradeTreeViewport.anchorMax = new Vector2(1f, 1f);
            upgradeTreeViewport.offsetMin = new Vector2(36f, 146f);
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
            upgradeTreeContent.sizeDelta = new Vector2(900f, 580f);
            upgradeTreeLabels.Clear();
            upgradeTreePan = Vector2.zero;
            upgradeTreeZoom = 1f;

            var nodes = session.UpgradeNodes;
            CreateUpgradeLinks(upgradeTreeContent, nodes);
            for (var i = 0; i < nodes.Count; i++)
            {
                CreateUpgradeNode(upgradeTreeContent, nodes[i]);
            }
            ApplyUpgradeTreeTransform();

            var detailPanel = CreatePanel("UpgradeDetails", upgradePanel.transform, new Vector2(0f, 76f), new Vector2(760f, 88f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            detailPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
            upgradeDetailTitle = CreateText("DetailTitle", detailPanel.transform, Vector2.zero, TextAnchor.MiddleLeft, 15);
            ConfigureCenteredRect(upgradeDetailTitle.GetComponent<RectTransform>(), new Vector2(-270f, 24f), new Vector2(190f, 24f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            upgradeDetailBody = CreateText("DetailBody", detailPanel.transform, Vector2.zero, TextAnchor.MiddleLeft, 12);
            ConfigureCenteredRect(upgradeDetailBody.GetComponent<RectTransform>(), new Vector2(12f, 0f), new Vector2(430f, 70f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            upgradeBuyButton = CreateAnchoredButton("BuySelectedUpgrade", detailPanel.transform, "BUY", new Vector2(300f, 0f), new Vector2(112f, 34f), new Vector2(0.5f, 0.5f), 12);
            upgradeBuyButton.onClick.AddListener(BuySelectedUpgrade);
            upgradeBuyButton.gameObject.SetActive(false);

            CreateAnchoredButton("ResetUpgradeButton", upgradePanel.transform, "RESET", new Vector2(-70f, 18f), new Vector2(120f, 28f), new Vector2(0.5f, 0f), 13)
                .onClick.AddListener(() => session.RefundAndResetUpgrades());
            CreateAnchoredButton("CloseUpgradeButton", upgradePanel.transform, "BACK", new Vector2(70f, 18f), new Vector2(120f, 28f), new Vector2(0.5f, 0f), 13)
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
                        CreateUpgradeLink(parent, prerequisite.radialPosition, node.radialPosition, node);
                    }
                }
            }
        }

        private void CreateUpgradeLink(Transform parent, Vector2 from, Vector2 to, SkillNodeDefinition target)
        {
            var delta = to - from;
            var go = CreateImage($"Link_{target.id}", parent, (from + to) * 0.5f, new Vector2(delta.magnitude, 5f), new Color(0.15f, 0.75f, 1f, 0.65f)).gameObject;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private void CreateUpgradeNode(Transform parent, SkillNodeDefinition node)
        {
            var size = node.isMajorUnlock ? new Vector2(46f, 46f) : new Vector2(34f, 34f);
            var button = CreateAnchoredButton($"Node_{node.id}", parent, "0/1", node.radialPosition, size, new Vector2(0.5f, 0.5f), node.isMajorUnlock ? 13 : 11);
            button.onClick.AddListener(() => PurchaseUpgradeNode(node));

            var events = button.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => SelectUpgradeNode(node));
            events.triggers.Add(enter);

            var label = CreateText($"NodeLabel_{node.id}", parent, node.radialPosition + new Vector2(0f, -34f), TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(label.GetComponent<RectTransform>(), node.radialPosition + new Vector2(0f, -34f), new Vector2(128f, 28f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.text = node.displayName;
            label.color = new Color(0.86f, 0.93f, 1f, 1f);
            upgradeTreeLabels.Add(label.GetComponent<RectTransform>());
        }

        private void ShowUpgradePanel()
        {
            SetUpgradePanelVisible(true);
            ApplyUpgradeTreeTransform();
            if (selectedUpgradeNode == null && session.UpgradeNodes.Count > 0)
            {
                SelectUpgradeNode(session.UpgradeNodes[0]);
            }
        }

        private void SelectUpgradeNode(SkillNodeDefinition node)
        {
            selectedUpgradeNode = node;
            UpdateSelectedUpgradeDetails();
        }

        private void PurchaseUpgradeNode(SkillNodeDefinition node)
        {
            selectedUpgradeNode = node;
            if (session.TryPurchaseUpgrade(node.id))
            {
                UpdateUpgradePanel();
            }

            UpdateSelectedUpgradeDetails();
        }

        private void BuySelectedUpgrade()
        {
            if (selectedUpgradeNode != null && session.TryPurchaseUpgrade(selectedUpgradeNode.id))
            {
                UpdateUpgradePanel();
                UpdateSelectedUpgradeDetails();
            }
        }

        private void OnUpgradeTreeDragged(Vector2 delta)
        {
            upgradeTreePan += delta;
            ApplyUpgradeTreeTransform();
        }

        private void OnUpgradeTreeScrolled(float scrollDelta)
        {
            var previousZoom = upgradeTreeZoom;
            upgradeTreeZoom = Mathf.Clamp(upgradeTreeZoom + scrollDelta * 0.12f, 0.55f, 1.85f);
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

            upgradeTreeContent.anchoredPosition = upgradeTreePan;
            upgradeTreeContent.localScale = Vector3.one * upgradeTreeZoom;
            var labelScale = Vector3.one * Mathf.Clamp(1f / upgradeTreeZoom, 0.72f, 1.65f);
            for (var i = 0; i < upgradeTreeLabels.Count; i++)
            {
                if (upgradeTreeLabels[i] != null)
                {
                    upgradeTreeLabels[i].localScale = labelScale;
                }
            }
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

            if (devPanel != null)
            {
                devPanel.SetActive(!visible && devPanelVisible);
            }

            if (statsPanel != null)
            {
                statsPanel.SetActive(!visible && statsPanelVisible);
            }
        }

        private void CreateDevPanel(Transform parent)
        {
            devPanel = CreatePanel("DevWalletPanel", parent, new Vector2(-326f, -48f), new Vector2(230f, 262f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            input.RegisterBlockingUiRect(devPanel.GetComponent<RectTransform>());
            var title = CreateText("DevTitle", devPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 13);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, -14f), new Vector2(210f, 20f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            title.text = "DEV WALLET";

            CreateButton("AddKillEssence", devPanel.transform, "+100 Essence", new Vector2(0f, -42f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.KillEssence, 100));
            CreateButton("AddVictorySigil", devPanel.transform, "+10 Victory", new Vector2(0f, -70f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.VictorySigil, 10));
            CreateButton("AddPerfectSigil", devPanel.transform, "+10 Perfect", new Vector2(0f, -98f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.PerfectSigil, 10));
            CreateButton("AddChallengeToken", devPanel.transform, "+10 Challenge", new Vector2(0f, -126f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.ChallengeToken, 10));
            CreateButton("AddBossCore", devPanel.transform, "+5 Boss Core", new Vector2(0f, -154f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.AddCurrency(CurrencyType.BossCore, 5));

            var speedLabel = CreateText("DevSpeedTitle", devPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(speedLabel.GetComponent<RectTransform>(), new Vector2(0f, -184f), new Vector2(178f, 18f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            speedLabel.text = "TEST SPEED";
            devSpeed1Button = CreateButton("DevSpeed1x", devPanel.transform, "1x", new Vector2(-66f, -210f), new Vector2(38f, 22f), 11);
            devSpeed2Button = CreateButton("DevSpeed2x", devPanel.transform, "2x", new Vector2(-22f, -210f), new Vector2(38f, 22f), 11);
            devSpeed5Button = CreateButton("DevSpeed5x", devPanel.transform, "5x", new Vector2(22f, -210f), new Vector2(38f, 22f), 11);
            devSpeed10Button = CreateButton("DevSpeed10x", devPanel.transform, "10x", new Vector2(66f, -210f), new Vector2(38f, 22f), 11);
            devSpeed1Button.onClick.AddListener(() => SetTestSpeed(1f));
            devSpeed2Button.onClick.AddListener(() => SetTestSpeed(2f));
            devSpeed5Button.onClick.AddListener(() => SetTestSpeed(5f));
            devSpeed10Button.onClick.AddListener(() => SetTestSpeed(10f));

            CreateButton("RefundUpgrades", devPanel.transform, "RESET UPGRADES", new Vector2(0f, -240f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.RefundAndResetUpgrades());

            devPanelVisible = false;
            devPanel.SetActive(false);
        }

        private void CreateTopRightToggles(Transform parent)
        {
            statsToggleButton = CreateAnchoredButton("StatsToggle", parent, "STATS [TAB]", new Vector2(-70f, -18f), new Vector2(102f, 28f), new Vector2(1f, 1f), 11);
            statsToggleButton.onClick.AddListener(ToggleStatsPanel);

            devToggleButton = CreateAnchoredButton("DevToggle", parent, "DEV [`]", new Vector2(-168f, -18f), new Vector2(82f, 28f), new Vector2(1f, 1f), 11);
            devToggleButton.onClick.AddListener(ToggleDevPanel);
        }

        private void ToggleDevPanel()
        {
            devPanelVisible = !devPanelVisible;
            if (devPanel != null)
            {
                devPanel.SetActive(devPanelVisible && !IsUpgradePanelOpen());
            }
        }

        private void ToggleStatsPanel()
        {
            statsPanelVisible = !statsPanelVisible;
            if (statsPanel != null)
            {
                statsPanel.SetActive(statsPanelVisible && !IsUpgradePanelOpen());
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
                row.onClick.AddListener(() => SelectStatsTower(tower));
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
            activeWeaponStatsButton.onClick.AddListener(SelectActiveWeaponStats);

            statsEmptyTowerText = CreateText("StatsNoTowers", statsPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(statsEmptyTowerText.GetComponent<RectTransform>(), new Vector2(-96f, -62f), new Vector2(176f, 42f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            statsEmptyTowerText.text = "No towers unlocked";
            statsEmptyTowerText.color = new Color(0.7f, 0.78f, 0.86f, 1f);

            statsDetailText = CreateText("StatsDetails", statsPanel.transform, Vector2.zero, TextAnchor.UpperLeft, 11);
            ConfigureCenteredRect(statsDetailText.GetComponent<RectTransform>(), new Vector2(0f, -158f), new Vector2(340f, 92f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            statsDetailText.color = new Color(0.86f, 0.93f, 1f, 1f);
            statsPanelVisible = false;
            statsPanel.SetActive(false);
        }

        private void SelectStatsTower(TowerDefinition tower)
        {
            selectedStatsTower = tower;
            selectedStatsActiveWeapon = false;
            UpdateStatsPanel();
        }

        private void SelectActiveWeaponStats()
        {
            selectedStatsTower = null;
            selectedStatsActiveWeapon = true;
            UpdateStatsPanel();
        }

        private void UpdateResultPanel()
        {
            if (resultPanel == null)
            {
                return;
            }

            resultPanel.SetActive(session.Finished);
            if (!session.Finished)
            {
                return;
            }

            resultTitle.text = session.Won ? "VICTORY" : "DEFEAT";
            resultTitle.color = session.Won ? new Color(0.7f, 1f, 0.55f, 1f) : new Color(1f, 0.35f, 0.25f, 1f);
            resultBody.text = session.Won
                ? $"Wave cleared. Lives: {session.Lives}"
                : $"The horde broke through. Killed: {session.EnemiesKilled}";
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
            if (!selectedStatsActiveWeapon && (selectedStatsTower == null || !ContainsTower(unlockedTowers, selectedStatsTower)))
            {
                selectedStatsTower = unlockedTowers.Count > 0 ? unlockedTowers[0] : null;
            }

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
                text.color = tower == selectedStatsTower ? new Color(1f, 0.86f, 0.35f, 1f) : Color.white;
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
                label.color = selectedStatsActiveWeapon ? new Color(1f, 0.86f, 0.35f, 1f) : Color.white;
            }

            if (statsDetailText == null)
            {
                return;
            }

            if (selectedStatsActiveWeapon)
            {
                var activePercent = totalDamage <= 0f ? 0f : activeWeapon.TotalDamageDealt / totalDamage * 100f;
                statsDetailText.text =
                    "Volley of Arrows\n" +
                    $"Damage: {activeWeapon.Damage:0.0} per target\n" +
                    $"Radius: {activeWeapon.Radius:0.0}\n" +
                    $"Cooldown: {activeWeapon.CooldownSeconds:0.0}s\n" +
                    $"Run damage: {activeWeapon.TotalDamageDealt:0} ({activePercent:0}%)";
                return;
            }

            if (selectedStatsTower != null)
            {
                var damage = towers.GetDamageDealt(selectedStatsTower);
                var percent = totalDamage <= 0f ? 0f : damage / totalDamage * 100f;
                statsDetailText.text =
                    $"{selectedStatsTower.displayName}\n" +
                    $"Damage: {selectedStatsTower.damage:0.0} per hit\n" +
                    $"Range: {selectedStatsTower.range:0.0}\n" +
                    $"Fire rate: {1f / Mathf.Max(0.01f, selectedStatsTower.fireInterval):0.0}/sec\n" +
                    $"Projectile: single target\n" +
                    $"Run damage: {damage:0} ({percent:0}%)";
            }
            else
            {
                statsDetailText.text = "Unlock a tower or select Active Weapon.";
            }
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
            upgradeCurrencyText.text = $"Essence {profile.GetCurrency(CurrencyType.KillEssence)}   Victory {profile.GetCurrency(CurrencyType.VictorySigil)}   Perfect {profile.GetCurrency(CurrencyType.PerfectSigil)}   Challenge {profile.GetCurrency(CurrencyType.ChallengeToken)}   Boss {profile.GetCurrency(CurrencyType.BossCore)}";

            foreach (var button in upgradePanel.GetComponentsInChildren<Button>())
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

                if (rank >= maxRank)
                {
                    button.interactable = true;
                    if (image != null)
                    {
                        image.color = new Color(0.08f, 0.58f, 0.54f, 1f);
                    }
                    label.color = new Color(0.6f, 1f, 0.85f, 1f);
                }
                else if (session.CanPurchaseUpgrade(nodeId))
                {
                    button.interactable = true;
                    if (image != null)
                    {
                        image.color = new Color(0.95f, 0.5f, 0.12f, 1f);
                    }
                    label.color = new Color(1f, 0.86f, 0.35f, 1f);
                }
                else
                {
                    button.interactable = true;
                    if (image != null)
                    {
                        image.color = new Color(0.08f, 0.2f, 0.32f, 1f);
                    }
                    label.color = new Color(0.45f, 0.55f, 0.65f, 1f);
                }
            }

            UpdateSelectedUpgradeDetails();
        }

        private void UpdateSelectedUpgradeDetails()
        {
            if (selectedUpgradeNode == null || upgradeDetailTitle == null || upgradeDetailBody == null || upgradeBuyButton == null)
            {
                return;
            }

            var rank = session.GetUpgradeRank(selectedUpgradeNode.id);
            var maxRank = session.GetUpgradeMaxRank(selectedUpgradeNode.id);
            upgradeDetailTitle.text = $"{selectedUpgradeNode.displayName}  {rank}/{maxRank}";
            var costLine = rank >= maxRank ? "Maxed" : FormatCosts(session.GetUpgradeNextCosts(selectedUpgradeNode.id));
            upgradeDetailBody.text = $"{selectedUpgradeNode.description}\nPer rank: {FormatEffects(selectedUpgradeNode.effects)}\nNext cost: {costLine}";
            var buttonLabel = upgradeBuyButton.GetComponentInChildren<Text>();
            if (rank >= maxRank)
            {
                upgradeBuyButton.interactable = false;
                buttonLabel.text = "MAXED";
            }
            else if (session.CanPurchaseUpgrade(selectedUpgradeNode.id))
            {
                upgradeBuyButton.interactable = true;
                buttonLabel.text = "BUY RANK";
            }
            else
            {
                upgradeBuyButton.interactable = false;
                buttonLabel.text = MissingPrerequisites(selectedUpgradeNode) ? "LOCKED" : "NEED COST";
            }
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
                text.Append(costs[i].currency);
            }

            return text.ToString();
        }

        private static string FormatEffects(UpgradeEffect[] effects)
        {
            if (effects == null || effects.Length == 0)
            {
                return "Unlock or milestone";
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
                    return $"Unlock {effect.targetId} tower";
                case UpgradeEffectType.GlobalTowerLimitFlat:
                    return $"+{effect.value:0} total tower limit";
                case UpgradeEffectType.PerTypeTowerLimitFlat:
                    return $"+{effect.value:0} {effect.targetId} tower limit";
                case UpgradeEffectType.TowerDamagePercent:
                    return $"+{effect.value:0}% {effect.targetId} tower damage";
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                    return $"+{effect.value:0}% active weapon damage";
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return $"-{effect.value:0}% active weapon cooldown";
                case UpgradeEffectType.BaseLivesFlat:
                    return $"+{effect.value:0} base lives";
                case UpgradeEffectType.UnlockEra:
                    return $"Unlock {effect.targetId} era";
                default:
                    return effect.type.ToString();
            }
        }

        private void CreateActiveWeaponSlot(Transform parent)
        {
            var root = new GameObject("ActiveWeaponSlot");
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
            activeWeaponCooldownText.text = activeWeapon.IsReady ? "OK" : activeWeapon.CanFire ? activeWeapon.CooldownRemaining.ToString("0.0") : "--";
            activeWeaponCooldownText.color = activeWeapon.IsReady ? new Color(0.6f, 1f, 0.55f, 1f) : Color.white;
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

            var text = CreateText("Label", go.transform, Vector2.zero, TextAnchor.MiddleCenter, fontSize);
            ConfigureCenteredRect(text.GetComponent<RectTransform>(), Vector2.zero, size, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            text.text = label;
            return button;
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
    }
}
