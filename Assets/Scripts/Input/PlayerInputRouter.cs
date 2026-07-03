using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Input
{
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        private readonly List<RectTransform> blockingUiRects = new();
        private Camera targetCamera;
        private int selectedTowerIndex;

        public GameInputState Current { get; private set; }
        public bool GameplayInputBlocked { get; set; }

        public void Initialize(Camera camera)
        {
            targetCamera = camera;
        }

        public void RegisterBlockingUiRect(RectTransform rectTransform)
        {
            if (rectTransform != null && !blockingUiRects.Contains(rectTransform))
            {
                blockingUiRects.Add(rectTransform);
            }
        }

        private void Update()
        {
            if (GameplayInputBlocked)
            {
                Current = new GameInputState
                {
                    SelectedTowerIndex = selectedTowerIndex,
                    PointerWorld = PointerToWorld()
                };
                return;
            }

            var pan = new Vector2(UnityEngine.Input.GetAxisRaw("Horizontal"), UnityEngine.Input.GetAxisRaw("Vertical"));
            if (UnityEngine.Input.GetMouseButton(1))
            {
                pan += new Vector2(-UnityEngine.Input.GetAxisRaw("Mouse X"), -UnityEngine.Input.GetAxisRaw("Mouse Y")) * 9f;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1))
            {
                selectedTowerIndex = 0;
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha2))
            {
                selectedTowerIndex = 1;
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha3))
            {
                selectedTowerIndex = 2;
            }

            var leftClick = UnityEngine.Input.GetMouseButtonDown(0) && !IsPointerOverBlockingUi();
            Current = new GameInputState
            {
                Pan = pan,
                Zoom = UnityEngine.Input.mouseScrollDelta.y,
                FireActive = leftClick,
                PlaceTower = leftClick,
                RemoveTower = UnityEngine.Input.GetKeyDown(KeyCode.Delete),
                RemoveAllTowers = UnityEngine.Input.GetKeyDown(KeyCode.Backspace),
                StartLevel = UnityEngine.Input.GetKeyDown(KeyCode.Space) || UnityEngine.Input.GetKeyDown(KeyCode.Return),
                RestartLevel = UnityEngine.Input.GetKeyDown(KeyCode.R),
                SelectedTowerIndex = selectedTowerIndex,
                PointerWorld = PointerToWorld()
            };
        }

        private Vector3 PointerToWorld()
        {
            if (targetCamera == null)
            {
                return Vector3.zero;
            }

            var ray = targetCamera.ScreenPointToRay(UnityEngine.Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            return plane.Raycast(ray, out var enter) ? ray.GetPoint(enter) : Vector3.zero;
        }

        private bool IsPointerOverBlockingUi()
        {
            var mousePosition = UnityEngine.Input.mousePosition;
            for (var i = blockingUiRects.Count - 1; i >= 0; i--)
            {
                var rect = blockingUiRects[i];
                if (rect == null)
                {
                    blockingUiRects.RemoveAt(i);
                    continue;
                }

                if (rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, mousePosition))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
