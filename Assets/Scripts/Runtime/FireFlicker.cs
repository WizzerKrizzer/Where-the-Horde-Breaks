using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class FireFlicker : MonoBehaviour
    {
        private Vector3 baseScale;
        private float phase;

        private void Awake()
        {
            baseScale = transform.localScale;
            phase = transform.position.x * 1.7f + transform.position.z * 0.9f;
        }

        private void Update()
        {
            var pulse = 1f + Mathf.Sin(Time.time * 8.5f + phase) * 0.045f + Mathf.Sin(Time.time * 15.3f + phase) * 0.025f;
            transform.localScale = new Vector3(baseScale.x * pulse, baseScale.y * (1.02f - (pulse - 1f) * 0.18f), baseScale.z * pulse);
        }
    }
}
