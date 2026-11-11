using UnityEngine;

namespace TowerDefense.Runtime
{
    /// <summary>
    /// A light shake of the ground, felt as a short camera shake (the mammoth crashing down,
    /// user 2026-11-11). Runs on game time, so slow motion slows it too. The game's camera
    /// (TopDownCameraController) and the capture tests' cameras add Offset to their position.
    /// </summary>
    public static class GroundTremor
    {
        private static float start = -100f;
        private static float seconds;
        // How far the camera moves, as a share of its height above the ground.
        private static float strength;

        /// <summary>Starts a tremor `strength` strong (share of the camera's height) for `duration` seconds.</summary>
        public static void Shake(float shakeStrength, float duration)
        {
            start = Time.time;
            seconds = Mathf.Max(0.05f, duration);
            strength = shakeStrength;
        }

        /// <summary>Where the camera should be pushed to now: zero once the tremor has died away.</summary>
        public static Vector3 Offset(Transform camera)
        {
            var age = Time.time - start;
            if (camera == null || age < 0f || age > seconds)
            {
                return Vector3.zero;
            }

            var progress = age / seconds;
            var envelope = (1f - progress) * (1f - progress);
            var size = strength * Mathf.Max(1f, camera.position.y) * envelope;
            var phase = age * 55f;
            var forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
            return camera.right * Mathf.Sin(phase * 1.13f) * size +
                   forward * Mathf.Sin(phase * 1.71f + 1.2f) * size * 0.6f +
                   Vector3.up * Mathf.Sin(phase * 2.3f + 0.4f) * size * 0.4f;
        }
    }
}
