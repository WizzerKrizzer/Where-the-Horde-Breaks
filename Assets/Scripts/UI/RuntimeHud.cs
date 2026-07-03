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
        private GameObject resultPanel;
        private Text resultTitle;
        private Text resultBody;
        private GameObject upgradePanel;
        private RectTransform upgradeTreeContent;
        private RectTransform upgradeTreeViewport;
        private Text upgradeCurrencyText;
        private Text upgradeDetailTitle;
        private Text upgradeDetailBody;
        private Button upgradeBuyButton;
        private SkillNodeDefinition selectedUpgradeNode;
        private Vector2 upgradeTreePan;
        private float upgradeTreeZoom = 1f;
        private GameObject devPanel;

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
            CreateDevPanel(parent);
        }

        private void Update()
        {
            if (session == null || statusText == null)
            {
                return;
            }

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
                for (var i = 0; i < towers.AvailableTowers.Count; i++)
                {
                    var marker = input.Current.SelectedTowerIndex == i ? ">" : " ";
                    var tower = towers.AvailableTowers[i];
                    text.AppendLine($"{marker} {i + 1}. {tower.displayName}  {towers.CountOf(tower)}/{tower.perTypeLimit}");
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
            hint.text = "Drag to pan. Mouse wheel zooms the tree.";

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
            var button = CreateAnchoredButton($"Node_{node.id}", parent, node.isMajorUnlock ? "A" : "+", node.radialPosition, size, new Vector2(0.5f, 0.5f), node.isMajorUnlock ? 18 : 16);
            button.onClick.AddListener(() => SelectUpgradeNode(node));

            var events = button.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => SelectUpgradeNode(node));
            events.triggers.Add(enter);

            var label = CreateText($"NodeLabel_{node.id}", parent, node.radialPosition + new Vector2(0f, -34f), TextAnchor.MiddleCenter, 11);
            ConfigureCenteredRect(label.GetComponent<RectTransform>(), node.radialPosition + new Vector2(0f, -34f), new Vector2(128f, 28f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.text = node.displayName;
            label.color = new Color(0.86f, 0.93f, 1f, 1f);
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
        }

        private void SetUpgradePanelVisible(bool visible)
        {
            if (upgradePanel != null)
            {
                upgradePanel.SetActive(visible);
            }

            if (devPanel != null)
            {
                devPanel.SetActive(!visible);
            }
        }

        private void CreateDevPanel(Transform parent)
        {
            devPanel = CreatePanel("DevWalletPanel", parent, new Vector2(-14f, -14f), new Vector2(230f, 262f), new Vector2(1f, 1f), new Vector2(1f, 1f));
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
        }

        private static void HighlightSpeedButton(Button button, bool active)
        {
            if (button?.targetGraphic is Image image)
            {
                image.color = active ? new Color(0.25f, 0.7f, 1f, 1f) : new Color(0.15f, 0.45f, 0.82f, 1f);
            }
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
                var label = button.GetComponentInChildren<Text>();
                var image = button.targetGraphic as Image;
                if (session.IsUpgradePurchased(nodeId))
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

            upgradeDetailTitle.text = selectedUpgradeNode.displayName;
            upgradeDetailBody.text = $"{selectedUpgradeNode.description}\nEffect: {FormatEffects(selectedUpgradeNode.effects)}\nCost: {FormatCosts(selectedUpgradeNode.costs)}";
            var buttonLabel = upgradeBuyButton.GetComponentInChildren<Text>();
            if (session.IsUpgradePurchased(selectedUpgradeNode.id))
            {
                upgradeBuyButton.interactable = false;
                buttonLabel.text = "OWNED";
            }
            else if (session.CanPurchaseUpgrade(selectedUpgradeNode.id))
            {
                upgradeBuyButton.interactable = true;
                buttonLabel.text = "BUY";
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
