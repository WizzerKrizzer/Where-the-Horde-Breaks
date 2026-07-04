using TowerDefense.Input;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class TowerPlacementFeedback : MonoBehaviour
    {
        private GameSession session;
        private PlayerInputRouter input;
        private TowerManager towers;
        private GameObject previewTower;
        private GameObject rangeDisc;
        private Renderer previewRenderer;
        private Renderer rangeRenderer;
        private string previewTowerId;

        public void Initialize(GameSession gameSession, PlayerInputRouter inputRouter, TowerManager towerManager)
        {
            session = gameSession;
            input = inputRouter;
            towers = towerManager;
            CreatePreviewObjects();
        }

        private void Update()
        {
            if (session == null || input == null || towers?.AvailableTowers == null || towers.AvailableTowers.Count == 0)
            {
                SetVisible(false);
                return;
            }

            if (!session.IsPlanning)
            {
                SetVisible(false);
                return;
            }

            if (input.IsPointerOverBlockingUi())
            {
                SetVisible(false);
                return;
            }

            var selectedIndex = Mathf.Clamp(input.Current.SelectedTowerIndex, 0, towers.AvailableTowers.Count - 1);
            var definition = towers.AvailableTowers[selectedIndex];
            if (towers.CountOf(definition) >= towers.GetPerTypeLimit(definition))
            {
                SetVisible(false);
                return;
            }

            var position = input.Current.PointerWorld;
            var canPlace = string.IsNullOrEmpty(towers.GetPlacementBlockReason(definition, position));

            EnsurePreviewTower(definition);
            SetVisible(true);
            previewTower.transform.position = position + Vector3.up * 0.35f;
            TowerVisualFactory.ApplyScale(previewTower.transform, definition);
            rangeDisc.transform.position = position + Vector3.up * 0.04f;
            rangeDisc.transform.localScale = new Vector3(definition.range * 2f, 0.025f, definition.range * 2f);

            previewRenderer.material = BootstrapMaterials.Get(canPlace ? new Color(0.82f, 0.95f, 1f, 0.78f) : new Color(1f, 0.72f, 0.32f, 0.78f));
            rangeRenderer.material = BootstrapMaterials.Get(new Color(0.45f, 0.8f, 1f, canPlace ? 0.07f : 0.04f));
        }

        private void CreatePreviewObjects()
        {
            rangeDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rangeDisc.name = "TowerRangePreview";
            rangeRenderer = rangeDisc.GetComponent<Renderer>();

            SetVisible(false);
        }

        private void EnsurePreviewTower(TowerDefense.Data.TowerDefinition definition)
        {
            if (previewTower != null && previewTowerId == definition.id)
            {
                return;
            }

            if (previewTower != null)
            {
                Destroy(previewTower);
            }

            previewTower = TowerVisualFactory.CreateTowerVisual(definition, "TowerPlacementPreview");
            previewRenderer = previewTower.GetComponent<Renderer>();
            previewTowerId = definition.id;
        }

        private void SetVisible(bool visible)
        {
            if (previewTower != null)
            {
                previewTower.SetActive(visible);
            }

            if (rangeDisc != null)
            {
                rangeDisc.SetActive(visible);
            }
        }
    }
}
