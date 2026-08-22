using TowerDefense.Input;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class TopDownCameraController : MonoBehaviour
    {
        // The original 55 degree view lets front rows hide several rows behind
        // them in a dense horde. A steeper view keeps touching ground-plane
        // silhouettes individually readable without changing their simulation.
        private const float AuthoredCameraPitch = 55f;
        private const float HordeReadableCameraPitch = 75f;

        [SerializeField] private float panSpeed = 26f;
        [SerializeField] private float mouseDragSensitivity = 3.35f;
        [SerializeField] private float zoomSpeed = 10f;
        [SerializeField] private float minHeight = 8f;
        [SerializeField] private float maxHeight = 60f;
        [SerializeField] private Vector2 minBounds = new(-36f, -22f);
        [SerializeField] private Vector2 maxBounds = new(36f, 22f);

        private Camera controlledCamera;
        private PlayerInputRouter input;

        public void Initialize(Camera camera, PlayerInputRouter router)
        {
            controlledCamera = camera;
            input = router;
        }

        public void ApplyView(Vector3 position, float fieldOfView, float minZoomHeight, float maxZoomHeight, float levelPanSpeed, float levelMouseDragSensitivity, Vector2 min, Vector2 max)
        {
            if (controlledCamera == null)
            {
                return;
            }

            controlledCamera.transform.position = ConvertAuthoredPositionToReadablePitch(position);
            controlledCamera.transform.rotation = Quaternion.Euler(HordeReadableCameraPitch, 0f, 0f);
            controlledCamera.fieldOfView = fieldOfView;
            minHeight = Mathf.Max(1f, minZoomHeight);
            maxHeight = Mathf.Max(minHeight + 1f, maxZoomHeight);
            panSpeed = Mathf.Max(1f, levelPanSpeed);
            mouseDragSensitivity = Mathf.Max(0.1f, levelMouseDragSensitivity);
            minBounds = min;
            maxBounds = max;
            ClampCameraPosition();
        }

        private static Vector3 ConvertAuthoredPositionToReadablePitch(Vector3 authoredPosition)
        {
            // Level camera positions were composed at 55 degrees. Preserve the
            // same point on the ground while changing only the viewing pitch.
            var authoredGroundOffset = authoredPosition.y / Mathf.Tan(AuthoredCameraPitch * Mathf.Deg2Rad);
            var readableGroundOffset = authoredPosition.y / Mathf.Tan(HordeReadableCameraPitch * Mathf.Deg2Rad);
            authoredPosition.z += authoredGroundOffset - readableGroundOffset;
            return authoredPosition;
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
            controlledCamera.transform.position += (right * state.Pan.x + forward * state.Pan.y) * (panSpeed * Time.unscaledDeltaTime);
            if (state.MousePanDelta.sqrMagnitude > 0.001f)
            {
                var verticalWorldSpan = 2f * controlledCamera.transform.position.y * Mathf.Tan(controlledCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var worldPerPixel = verticalWorldSpan / Mathf.Max(1f, Screen.height);
                controlledCamera.transform.position += (-right * state.MousePanDelta.x - forward * state.MousePanDelta.y) * (worldPerPixel * mouseDragSensitivity);
            }

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
