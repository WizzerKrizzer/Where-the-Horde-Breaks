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
        [SerializeField] private float minHeight = 2.5f;
        [SerializeField] private float maxHeight = 60f;
        [SerializeField] private Vector2 minBounds = new(-36f, -22f);
        [SerializeField] private Vector2 maxBounds = new(36f, 22f);

        private Camera controlledCamera;
        private PlayerInputRouter input;
        private Vector3 presentationOffset;
        private float authoredFieldOfView;
        private float dragReferenceHeight = 8f;
        private float impactFeedbackRemaining;
        private float impactFeedbackDuration;
        private float impactShakeAmplitude;
        private float impactZoomAmount;

        public void Initialize(Camera camera, PlayerInputRouter router)
        {
            controlledCamera = camera;
            input = router;
            authoredFieldOfView = camera != null ? camera.fieldOfView : 60f;
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
            authoredFieldOfView = fieldOfView;
            dragReferenceHeight = Mathf.Max(1f, minZoomHeight);
            minHeight = Mathf.Min(dragReferenceHeight, 2.5f);
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

            controlledCamera.transform.position -= presentationOffset;
            presentationOffset = Vector3.zero;
            controlledCamera.fieldOfView = authoredFieldOfView;

            var state = input.Current;
            var forward = Vector3.ProjectOnPlane(controlledCamera.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(controlledCamera.transform.right, Vector3.up).normalized;
            controlledCamera.transform.position += (right * state.Pan.x + forward * state.Pan.y) * (panSpeed * Time.unscaledDeltaTime);
            if (state.MousePanDelta.sqrMagnitude > 0.001f)
            {
                var currentPointer = UnityEngine.Input.mousePosition;
                var previousPointer = currentPointer - new Vector3(
                    state.MousePanDelta.x,
                    state.MousePanDelta.y,
                    0f);
                var currentGround = ScreenPointToGround(currentPointer);
                var previousGround = ScreenPointToGround(previousPointer);
                var dragMultiplier = mouseDragSensitivity / 3.35f;
                var zoomCompensation = dragReferenceHeight / Mathf.Max(dragReferenceHeight, controlledCamera.transform.position.y);
                controlledCamera.transform.position +=
                    (previousGround - currentGround) * (dragMultiplier * zoomCompensation);
            }

            ClampCameraPosition();

            if (Mathf.Abs(state.Zoom) > 0.01f)
            {
                var pointerBeforeZoom = ScreenPointToGround(UnityEngine.Input.mousePosition);
                var position = controlledCamera.transform.position;
                var closeZoomScale = Mathf.Clamp(position.y / 12f, 0.12f, 1f);
                position.y = Mathf.Clamp(position.y - state.Zoom * zoomSpeed * closeZoomScale, minHeight, maxHeight);
                controlledCamera.transform.position = position;
                var pointerAfterZoom = ScreenPointToGround(UnityEngine.Input.mousePosition);
                controlledCamera.transform.position += pointerBeforeZoom - pointerAfterZoom;
                ClampCameraPosition();
            }

            ApplyImpactFeedback();
        }

        public void PlayMeteorImpact(Vector3 worldPosition, float impactRadius)
        {
            if (controlledCamera == null)
            {
                return;
            }

            var viewCenter = ScreenPointToGround(new Vector3(
                controlledCamera.pixelWidth * 0.5f,
                controlledCamera.pixelHeight * 0.5f,
                0f));
            var groundDistance = Vector2.Distance(
                new Vector2(viewCenter.x, viewCenter.z),
                new Vector2(worldPosition.x, worldPosition.z));
            var strength = 1f - Mathf.Clamp01(
                Mathf.Max(0f, groundDistance - impactRadius) / Mathf.Max(1f, impactRadius * 4f));
            impactFeedbackDuration = 0.34f;
            impactFeedbackRemaining = impactFeedbackDuration;
            impactShakeAmplitude = Mathf.Max(
                impactShakeAmplitude,
                Mathf.Lerp(0.05f, 0.48f, strength) *
                Mathf.Lerp(0.7f, 1.25f, controlledCamera.transform.position.y / Mathf.Max(1f, maxHeight)));
            impactZoomAmount = Mathf.Max(impactZoomAmount, authoredFieldOfView * 0.027f * strength);
        }

        private void ApplyImpactFeedback()
        {
            if (impactFeedbackRemaining <= 0f)
            {
                impactShakeAmplitude = 0f;
                impactZoomAmount = 0f;
                return;
            }

            impactFeedbackRemaining = Mathf.Max(0f, impactFeedbackRemaining - Time.unscaledDeltaTime);
            var progress = 1f - impactFeedbackRemaining / Mathf.Max(0.001f, impactFeedbackDuration);
            var envelope = (1f - progress) * (1f - Mathf.Exp(-progress * 28f));
            var phase = Time.unscaledTime * 67f;
            var right = controlledCamera.transform.right;
            var forward = Vector3.ProjectOnPlane(controlledCamera.transform.forward, Vector3.up).normalized;
            presentationOffset =
                right * Mathf.Sin(phase * 1.13f) * impactShakeAmplitude * envelope +
                forward * Mathf.Sin(phase * 1.71f + 1.2f) * impactShakeAmplitude * 0.72f * envelope;
            controlledCamera.transform.position += presentationOffset;

            var zoomEnvelope = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI) * (1f - progress * 0.35f);
            controlledCamera.fieldOfView = authoredFieldOfView + impactZoomAmount * zoomEnvelope;
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
