using TowerDefense.Input;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class TopDownCameraController : MonoBehaviour
    {
        [SerializeField] private float panSpeed = 18f;
        [SerializeField] private float zoomSpeed = 10f;
        [SerializeField] private float minHeight = 8f;
        [SerializeField] private float maxHeight = 60f;
        [SerializeField] private Vector2 minBounds = new(-30f, -18f);
        [SerializeField] private Vector2 maxBounds = new(30f, 18f);

        private Camera controlledCamera;
        private PlayerInputRouter input;

        public void Initialize(Camera camera, PlayerInputRouter router)
        {
            controlledCamera = camera;
            input = router;
        }

        private void LateUpdate()
        {
            if (controlledCamera == null || input == null)
            {
                return;
            }

            var state = input.Current;
            var forward = Vector3.ProjectOnPlane(controlledCamera.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(controlledCamera.transform.right, Vector3.up).normalized;
            controlledCamera.transform.position += (right * state.Pan.x + forward * state.Pan.y) * (panSpeed * Time.deltaTime);
            ClampCameraPosition();

            if (Mathf.Abs(state.Zoom) > 0.01f)
            {
                var pointerBeforeZoom = ScreenPointToGround(UnityEngine.Input.mousePosition);
                var position = controlledCamera.transform.position;
                position.y = Mathf.Clamp(position.y - state.Zoom * zoomSpeed, minHeight, maxHeight);
                controlledCamera.transform.position = position;
                var pointerAfterZoom = ScreenPointToGround(UnityEngine.Input.mousePosition);
                controlledCamera.transform.position += pointerBeforeZoom - pointerAfterZoom;
                ClampCameraPosition();
            }
        }

        private void ClampCameraPosition()
        {
            var position = controlledCamera.transform.position;
            position.y = Mathf.Clamp(position.y, minHeight, maxHeight);
            controlledCamera.transform.position = position;

            var focus = ScreenPointToGround(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            var clampedFocus = new Vector3(
                Mathf.Clamp(focus.x, minBounds.x, maxBounds.x),
                focus.y,
                Mathf.Clamp(focus.z, minBounds.y, maxBounds.y));
            controlledCamera.transform.position += clampedFocus - focus;
        }

        private Vector3 ScreenPointToGround(Vector3 screenPosition)
        {
            var ray = controlledCamera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            return plane.Raycast(ray, out var enter) ? ray.GetPoint(enter) : Vector3.zero;
        }
    }
}
