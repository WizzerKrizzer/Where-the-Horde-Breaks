using System.Collections.Generic;
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
        // The towers the one being placed would overlap, shown all red, with the preview (user, 2026-11-12).
        private static readonly Color OverlapRed = new(1f, 0.12f, 0.1f, 0.78f);
        private readonly List<TowerActor> overlapped = new();
        private readonly List<TowerActor> tinted = new();

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
            towers.GetOverlappingTowers(definition, position, overlapped);
            Tint(overlapped);

            EnsurePreviewTower(definition);
            SetVisible(true);
            previewTower.transform.position = position + Vector3.up * 0.35f;
            TowerVisualFactory.ApplyScale(previewTower.transform, definition);
            // The tower's own model, see-through, where it has one (user, 2026-11-12): red over another
            // tower, darker where it cannot stand.
            if (ghost != null)
            {
                previewTower.SetActive(false);
                ghost.SetActive(true);
                ghost.transform.SetPositionAndRotation(position, Quaternion.LookRotation(towers.FacingRoad(position), Vector3.up));
                SetGhostColor(overlapped.Count > 0 ? GhostRed : canPlace ? GhostClear : GhostBlocked);
            }
            rangeDisc.transform.position = position + Vector3.up * 0.24f;
            rangeDisc.transform.localScale = new Vector3(definition.range * 2f, 0.025f, definition.range * 2f);

            previewRenderer.material = BootstrapMaterials.Get(overlapped.Count > 0 ? OverlapRed
                : canPlace ? PlaceholderPalette.Grey(.917f, 0.78f) : PlaceholderPalette.Grey(.758f, 0.78f));
            rangeRenderer.material = BootstrapMaterials.Get(PlaceholderPalette.Grey(.718f, canPlace ? 0.07f : 0.04f));
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
            BuildGhost(definition);
        }

        private static readonly Color GhostClear = new(1f, 1f, 1f, 0.55f);
        private static readonly Color GhostBlocked = new(0.45f, 0.45f, 0.45f, 0.55f);
        private static readonly Color GhostRed = new(1f, 0.12f, 0.1f, 0.6f);
        private GameObject ghost;
        private readonly List<Material> ghostMaterials = new();
        private Color ghostColor;

        // The model of the tower being placed, built once facing +Z at the origin and then moved with
        // the pointer: the tipi, the rock, the cave or the pen, with their crews.
        private void BuildGhost(TowerDefense.Data.TowerDefinition definition)
        {
            if (ghost != null)
            {
                Destroy(ghost);
            }

            foreach (var material in ghostMaterials)
            {
                Destroy(material);
            }

            ghostMaterials.Clear();
            ghost = new GameObject("TowerPlacementGhost");
            GameObject built = null;
            if (definition.throwsSpears || definition.id == "slingers")
            {
                // The crew is built for a tower standing at the origin.
                var stand = new GameObject("GhostTower");
                stand.transform.SetParent(ghost.transform, false);
                var actor = stand.AddComponent<TowerActor>();
                built = (definition.throwsSpears
                    ? (TowerCrewVisual)SpearRockVisual.TryCreate(actor, Vector3.forward)
                    : SlingerTowerVisual.TryCreate(actor, Vector3.forward))?.gameObject;
                Destroy(stand);
            }
            else if (definition.barracksUnitType == TowerDefense.Data.AlliedUnitType.Caveman && definition.behavior == TowerDefense.Data.TowerBehavior.Barracks)
            {
                built = CaveVisual.TryCreate(ghost.transform, Vector3.zero, Vector3.forward, out _, out _);
            }
            else if (definition.behavior == TowerDefense.Data.TowerBehavior.Charge)
            {
                // The pen's gate stands 0.3 m out from the spot (BeastCharge.PenGateAhead).
                built = MammothVisual.TryCreatePen(ghost.transform, Vector3.forward * 0.3f, Vector3.forward);
            }

            if (built == null)
            {
                Destroy(ghost);
                ghost = null;
                return;
            }

            foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.materials;
                foreach (var material in materials)
                {
                    if (material.HasProperty("_SrcBlend"))
                    {
                        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.SetFloat("_ZWrite", 0f);
                        material.SetFloat("_ShadowStrength", 0f);
                        material.renderQueue = 3000;
                    }

                    ghostMaterials.Add(material);
                }

                renderer.materials = materials;
            }

            ghostColor = Color.clear;
            ghost.SetActive(false);
        }

        private void SetGhostColor(Color color)
        {
            if (color == ghostColor)
            {
                return;
            }

            ghostColor = color;
            foreach (var material in ghostMaterials)
            {
                if (material != null && material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", color);
                }
            }
        }

        // Red on the towers in `now`, back to normal on those no longer overlapped.
        private void Tint(List<TowerActor> now)
        {
            for (var i = tinted.Count - 1; i >= 0; i--)
            {
                if (tinted[i] == null)
                {
                    tinted.RemoveAt(i);
                }
                else if (!now.Contains(tinted[i]))
                {
                    tinted[i].SetOverlapTint(false);
                    tinted.RemoveAt(i);
                }
            }

            foreach (var tower in now)
            {
                tower.SetOverlapTint(true);
                if (!tinted.Contains(tower))
                {
                    tinted.Add(tower);
                }
            }
        }

        private void SetVisible(bool visible)
        {
            if (!visible && tinted.Count > 0)
            {
                overlapped.Clear();
                Tint(overlapped);
            }

            if (ghost != null)
            {
                ghost.SetActive(visible);
            }

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
