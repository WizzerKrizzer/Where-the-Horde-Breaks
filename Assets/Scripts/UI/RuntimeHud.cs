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
        private Text towerText;
        private GameObject activeWeaponSlot;
        private Image activeWeaponIcon;
        private Image activeWeaponCooldownFill;
        private Text activeWeaponCooldownText;
        private GameObject selectedTowerPanel;
        private Text selectedTowerTitle;
        private Text selectedTowerBody;
        private Button startBattleButton;
        private Button devSpeed1Button;
        private Button devSpeed2Button;
        private Button devSpeed5Button;
        private Button devSpeed10Button;
        private readonly Button[] devLoadSlotButtons = new Button[4];
        private readonly Text[] devSaveSlotStatusTexts = new Text[4];
        private Button devToggleButton;
        private Button upgradeToggleButton;
        private GameObject resultPanel;
        private Text resultTitle;
        private Text resultBody;
        private GameObject upgradePanel;
        private RectTransform upgradeTreeContent;
        private RectTransform upgradeTreeViewport;
        private readonly List<RectTransform> upgradeTreeLabels = new();
        private GameObject upgradeDetailPanel;
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
            Bosses
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
            towerText = CreateText("TowerSelection", parent, new Vector2(12f, -84f), TextAnchor.UpperLeft, 13);
            towerText.GetComponent<RectTransform>().sizeDelta = new Vector2(340f, 178f);
            CreateActiveWeaponSlot(parent);
            CreateSelectedTowerPanel(parent);
            CreateStartBattleButton(parent);
            CreateResultPanel(parent);
            CreateUpgradePanel(parent);
            CreateStatsPanel(parent);
            CreateDebugSpawnPanel(parent);
            CreateCodexPanel(parent);
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
            text.AppendLine($"{FormatCurrencyBalance(profile, CurrencyType.KillEssence)}   {FormatCurrencyBalance(profile, CurrencyType.VictorySigil)}   {FormatCurrencyBalance(profile, CurrencyType.PerfectSigil)}");
            text.AppendLine($"{FormatCurrencyBalance(profile, CurrencyType.ChallengeToken)}   {FormatCurrencyBalance(profile, CurrencyType.BossCore)}");

            if (session.Finished)
            {
                text.AppendLine(session.Won ? "VICTORY - press R to rebuild/replay" : "DEFEAT - press R to adjust towers");
            }

            statusText.text = text.ToString();
            UpdateTowerText();
            UpdateSelectedTowerPanel();
            UpdateActiveWeaponSlot();
            UpdateDevSpeedButtons();
            UpdateResultPanel();
            UpdateStartBattleButton();
            UpdateUpgradeShortcutButton();
            UpdateUpgradePanel();
            UpdateStatsPanel();
            UpdateCodexPanel();
        }

        private void HandleHudShortcuts()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && CloseCurrentOverlay())
            {
                return;
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
            upgradePanel.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.024f, 1f);

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
            upgradeTreeContent.sizeDelta = CalculateUpgradeTreeContentSize(nodes);
            upgradeTreeLabels.Clear();
            upgradeTreePan = Vector2.zero;
            upgradeTreeZoom = 1f;

            CreateUpgradeLinks(upgradeTreeContent, nodes);
            for (var i = 0; i < nodes.Count; i++)
            {
                CreateUpgradeNode(upgradeTreeContent, nodes[i]);
            }
            ApplyUpgradeTreeTransform();

            upgradeDetailPanel = CreatePanel("UpgradeDetails", upgradePanel.transform, new Vector2(0f, 76f), new Vector2(760f, 88f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));
            var upgradeDetailImage = upgradeDetailPanel.GetComponent<Image>();
            upgradeDetailImage.color = new Color(0f, 0f, 0f, 0.72f);
            upgradeDetailImage.raycastTarget = false;
            upgradeDetailTitle = CreateText("DetailTitle", upgradeDetailPanel.transform, Vector2.zero, TextAnchor.MiddleLeft, 15);
            upgradeDetailTitle.raycastTarget = false;
            ConfigureCenteredRect(upgradeDetailTitle.GetComponent<RectTransform>(), new Vector2(-270f, 24f), new Vector2(190f, 24f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            upgradeDetailBody = CreateText("DetailBody", upgradeDetailPanel.transform, Vector2.zero, TextAnchor.MiddleLeft, 12);
            upgradeDetailBody.raycastTarget = false;
            ConfigureCenteredRect(upgradeDetailBody.GetComponent<RectTransform>(), new Vector2(24f, -4f), new Vector2(470f, 74f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            upgradeBuyButton = CreateAnchoredButton("BuySelectedUpgrade", upgradeDetailPanel.transform, "BUY", new Vector2(300f, 0f), new Vector2(112f, 34f), new Vector2(0.5f, 0.5f), 12);
            upgradeBuyButton.onClick.AddListener(BuySelectedUpgrade);
            upgradeBuyButton.gameObject.SetActive(false);
            upgradeDetailPanel.SetActive(false);

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

        private static Vector2 CalculateUpgradeTreeContentSize(IReadOnlyList<SkillNodeDefinition> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return new Vector2(900f, 580f);
            }

            var maxAbsX = 0f;
            var maxAbsY = 0f;
            for (var i = 0; i < nodes.Count; i++)
            {
                maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(nodes[i].radialPosition.x));
                maxAbsY = Mathf.Max(maxAbsY, Mathf.Abs(nodes[i].radialPosition.y));
            }

            return new Vector2(
                Mathf.Max(900f, maxAbsX * 2f + 360f),
                Mathf.Max(580f, maxAbsY * 2f + 300f));
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
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => ClearUpgradeDetails(node));
            events.triggers.Add(exit);

            var label = CreateText($"NodeLabel_{node.id}", parent, node.radialPosition + new Vector2(0f, -34f), TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(label.GetComponent<RectTransform>(), node.radialPosition + new Vector2(0f, -34f), new Vector2(128f, 28f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.text = node.displayName;
            label.color = new Color(0.86f, 0.93f, 1f, 1f);
            upgradeTreeLabels.Add(label.GetComponent<RectTransform>());
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

        private void SelectUpgradeNode(SkillNodeDefinition node)
        {
            selectedUpgradeNode = node;
            UpdateSelectedUpgradeDetails();
        }

        private void ClearUpgradeDetails(SkillNodeDefinition node = null)
        {
            if (node != null && selectedUpgradeNode != node)
            {
                return;
            }

            selectedUpgradeNode = null;
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
            upgradeTreeZoom = Mathf.Clamp(upgradeTreeZoom + scrollDelta * 0.12f, GetMinimumUpgradeTreeZoom(), 1.85f);
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

            upgradeTreeZoom = Mathf.Clamp(upgradeTreeZoom, GetMinimumUpgradeTreeZoom(), 1.85f);
            upgradeTreePan = ClampUpgradeTreePan(upgradeTreePan);
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

        private Vector2 ClampUpgradeTreePan(Vector2 pan)
        {
            if (upgradeTreeViewport == null || upgradeTreeContent == null)
            {
                return pan;
            }

            var viewportSize = upgradeTreeViewport.rect.size;
            var contentSize = upgradeTreeContent.rect.size * upgradeTreeZoom;
            var maxPanX = Mathf.Max(0f, (contentSize.x - viewportSize.x) * 0.5f);
            var maxPanY = Mathf.Max(0f, (contentSize.y - viewportSize.y) * 0.5f);
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
            selectedTowerPanel = CreatePanel("SelectedTowerPanel", parent, new Vector2(12f, 18f), new Vector2(286f, 126f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            var image = selectedTowerPanel.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.02f, 0.025f, 0.03f, 0.74f);
            }

            selectedTowerTitle = CreateText("SelectedTowerTitle", selectedTowerPanel.transform, Vector2.zero, TextAnchor.MiddleLeft, 13);
            ConfigureCenteredRect(selectedTowerTitle.GetComponent<RectTransform>(), new Vector2(12f, -12f), new Vector2(260f, 22f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            selectedTowerBody = CreateText("SelectedTowerBody", selectedTowerPanel.transform, Vector2.zero, TextAnchor.UpperLeft, 11);
            ConfigureCenteredRect(selectedTowerBody.GetComponent<RectTransform>(), new Vector2(12f, -38f), new Vector2(260f, 78f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            selectedTowerPanel.SetActive(false);
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
                $"This tower damage: {tower.DamageDealt:0}\n" +
                $"{definition.displayName} type damage: {towers.GetDamageDealt(definition):0}";
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

            startBattleButton.gameObject.SetActive(session.IsPlanning);
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
            contentRect.sizeDelta = new Vector2(214f, 432f);

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

            var saveLabel = CreateText("DevSaveTitle", content.transform, Vector2.zero, TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(saveLabel.GetComponent<RectTransform>(), new Vector2(0f, -208f), new Vector2(178f, 18f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            saveLabel.text = "DEV SAVES";

            for (var slot = 1; slot <= 3; slot++)
            {
                var capturedSlot = slot;
                var rowY = -208f - slot * 26f;
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

            CreateButton("RefundUpgrades", content.transform, "RESET UPGRADES", new Vector2(0f, -310f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.RefundAndResetUpgrades());
            CreateButton("ClearCurrencies", content.transform, "CLEAR CURRENCIES", new Vector2(0f, -338f), new Vector2(178f, 24f), 12)
                .onClick.AddListener(() => session.ClearCurrencies());
            CreateButton("ResetRewardProgress", content.transform, "RESET CLEAR REWARDS", new Vector2(0f, -366f), new Vector2(178f, 24f), 11)
                .onClick.AddListener(() => session.ClearLevelRewardProgress());

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
            codexPanel = CreatePanel("BreakerGrimoirePanel", parent, new Vector2(-14f, -48f), new Vector2(520f, 520f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            input.RegisterBlockingUiRect(codexPanel.GetComponent<RectTransform>());
            var title = CreateText("CodexTitle", codexPanel.transform, Vector2.zero, TextAnchor.MiddleCenter, 15);
            ConfigureCenteredRect(title.GetComponent<RectTransform>(), new Vector2(0f, -18f), new Vector2(480f, 24f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            title.text = "THE BREAKER'S GRIMOIRE";

            CreateButton("CodexTurrets", codexPanel.transform, "TURRETS", new Vector2(-180f, -50f), new Vector2(96f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.Turrets));
            CreateButton("CodexActive", codexPanel.transform, "ACTIVE", new Vector2(-60f, -50f), new Vector2(96f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.ActiveWeapons));
            CreateButton("CodexEnemies", codexPanel.transform, "ENEMIES", new Vector2(60f, -50f), new Vector2(96f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.Enemies));
            CreateButton("CodexBosses", codexPanel.transform, "BOSSES", new Vector2(180f, -50f), new Vector2(96f, 24f), 10)
                .onClick.AddListener(() => SetCodexSector(CodexSector.Bosses));

            var listViewport = CreatePanel("CodexListViewport", codexPanel.transform, new Vector2(-145f, -82f), new Vector2(190f, 400f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
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

            var detailPanel = CreatePanel("CodexDetails", codexPanel.transform, new Vector2(118f, -82f), new Vector2(286f, 400f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
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
            codexDetailContent.sizeDelta = new Vector2(250f, 360f);

            codexDetailText = CreateText("CodexDetailText", codexDetailContent, Vector2.zero, TextAnchor.UpperLeft, 11);
            ConfigureCenteredRect(codexDetailText.GetComponent<RectTransform>(), new Vector2(0f, -14f), new Vector2(250f, 360f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
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
            HighlightToggleButton(codexToggleButton, codexPanelVisible);
            HighlightToggleButton(debugSpawnToggleButton, debugSpawnPanelVisible);
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
            }

            return entries;
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
                        ? $"\nFire: {tower.fireDamagePerTick:0.00} damage/tick, {tower.fireTicksPerSecond:0.00} ticks/sec, {tower.fireMaxStacks} max stacks, {tower.fireDuration:0.0}s"
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
            text.AppendLine($"Kill reward: {enemy.killReward} {FormatCurrencySymbol(CurrencyType.KillEssence)}");

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
                    child.gameObject.SetActive(node != null && IsUpgradeNodeRevealed(node) && HasAnyPurchasedPrerequisite(node));
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
            if (selectedUpgradeNode == null || upgradeDetailTitle == null || upgradeDetailBody == null || upgradeBuyButton == null)
            {
                return;
            }

            if (upgradeDetailPanel != null)
            {
                upgradeDetailPanel.SetActive(true);
            }

            var rank = session.GetUpgradeRank(selectedUpgradeNode.id);
            var maxRank = session.GetUpgradeMaxRank(selectedUpgradeNode.id);
            var missingPrerequisites = MissingPrerequisites(selectedUpgradeNode);
            upgradeDetailTitle.text = $"{selectedUpgradeNode.displayName}  {rank}/{maxRank}";
            if (missingPrerequisites)
            {
                upgradeDetailBody.text = "Locked";
                var lockedLabel = upgradeBuyButton.GetComponentInChildren<Text>();
                upgradeBuyButton.interactable = false;
                lockedLabel.text = "LOCKED";
                return;
            }

            var priceLine = rank >= maxRank ? "Maxed" : FormatCosts(session.GetUpgradeNextCosts(selectedUpgradeNode.id));
            upgradeDetailBody.text = $"{selectedUpgradeNode.description}\n\nStats:\n{FormatUpgradePreview(selectedUpgradeNode)}\n\n{priceLine}";
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
                buttonLabel.text = "NEED COST";
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
                text.Append(FormatCurrencySymbol(costs[i].currency));
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
                    var baseDamage = tower != null ? tower.damage : 0f;
                    var currentDamage = baseDamage * (1f + current / 100f);
                    var nextDamage = baseDamage * (1f + next / 100f);
                    return $"{target} bonus damage: {current:0}% -> {next:0}%\nDamage/hit: {currentDamage:0.##} -> {nextDamage:0.##}";
                }
                case UpgradeEffectType.TowerFireRatePercent:
                {
                    var baseRate = tower != null ? 1f / Mathf.Max(0.01f, tower.fireInterval) : 0f;
                    return $"{target} fire rate bonus: {current:0}% -> {next:0}%\nShots/sec: {baseRate * (1f + current / 100f):0.##} -> {baseRate * (1f + next / 100f):0.##}";
                }
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
                    return $"{target} troop damage bonus: {current:0}% -> {next:0}%\nTroop damage: {currentDamage:0.##} -> {nextDamage:0.##}";
                }
                case UpgradeEffectType.BarracksUnitHealthPercent:
                {
                    var currentHealth = tower != null ? tower.alliedUnitHealth : 0f;
                    var nextHealth = currentHealth * (1f + effect.value / Mathf.Max(1f, 100f + current));
                    return $"{target} troop health bonus: {current:0}% -> {next:0}%\nTroop health: {currentHealth:0.##} -> {nextHealth:0.##}";
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
                    return $"{target} burn damage/tick: {current:0.##} -> {next:0.##}";
                case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                    return $"{target} burn ticks/sec: {current:0.##} -> {next:0.##}";
                case UpgradeEffectType.TowerFireMaxStacksFlat:
                    return $"{target} burn stacks: {Mathf.RoundToInt(current)} -> {Mathf.RoundToInt(next)}";
                case UpgradeEffectType.TowerFireDurationFlat:
                    return $"{target} burn duration: {current:0.#}s -> {next:0.#}s";
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                {
                    var baseDamage = session.BaseActiveWeaponDamage;
                    return $"Active weapon bonus damage: {current:0}% -> {next:0}%\nDamage/hit: {baseDamage * (1f + current / 100f):0.##} -> {baseDamage * (1f + next / 100f):0.##}";
                }
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return $"Active weapon cooldown reduction: {current:0}% -> {next:0}%\nCooldown: {session.BaseActiveWeaponCooldown * Mathf.Max(0.1f, 1f - current / 100f):0.##}s -> {session.BaseActiveWeaponCooldown * Mathf.Max(0.1f, 1f - next / 100f):0.##}s";
                case UpgradeEffectType.ActiveWeaponRadiusFlat:
                    return $"Active weapon radius: {session.BaseActiveWeaponRadius + current:0.##} -> {session.BaseActiveWeaponRadius + next:0.##}";
                case UpgradeEffectType.ActiveWeaponPierceFlat:
                    return $"Active weapon targets: {session.BaseActiveWeaponMaxTargets + Mathf.RoundToInt(current)} -> {session.BaseActiveWeaponMaxTargets + Mathf.RoundToInt(next)}";
                case UpgradeEffectType.BaseLivesFlat:
                    return $"Base lives: {session.Level.startingLives + Mathf.RoundToInt(current)} -> {session.Level.startingLives + Mathf.RoundToInt(next)}";
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
                case UpgradeEffectType.TowerDamagePercent:
                    return $"+{effect.value:0}% {FormatTargetName(effect.targetId)} damage";
                case UpgradeEffectType.TowerFireRatePercent:
                    return string.IsNullOrWhiteSpace(effect.targetId)
                        ? $"+{effect.value:0}% tower fire rate"
                        : $"+{effect.value:0}% {FormatTargetName(effect.targetId)} fire rate";
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
                    return $"+{effect.value:0.00} {FormatTargetName(effect.targetId)} fire damage/tick";
                case UpgradeEffectType.TowerFireTicksPerSecondFlat:
                    return $"+{effect.value:0.00} {FormatTargetName(effect.targetId)} fire ticks/sec";
                case UpgradeEffectType.TowerFireMaxStacksFlat:
                    return $"+{effect.value:0} {FormatTargetName(effect.targetId)} fire stacks";
                case UpgradeEffectType.TowerFireDurationFlat:
                    return $"+{effect.value:0.0}s {FormatTargetName(effect.targetId)} fire duration";
                case UpgradeEffectType.ActiveWeaponDamagePercent:
                    return $"+{effect.value:0}% active weapon damage";
                case UpgradeEffectType.ActiveWeaponCooldownPercent:
                    return $"-{effect.value:0}% active weapon cooldown";
                case UpgradeEffectType.ActiveWeaponRadiusFlat:
                    return $"+{effect.value:0.00} active weapon radius";
                case UpgradeEffectType.ActiveWeaponPierceFlat:
                    return $"+{effect.value:0} active weapon targets";
                case UpgradeEffectType.BaseLivesFlat:
                    return $"+{effect.value:0} base lives";
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
