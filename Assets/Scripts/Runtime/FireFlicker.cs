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
            var pulse = 1f + Mathf.Sin(Time.time * 9.5f + phase) * 0.12f + Mathf.Sin(Time.time * 17.3f + phase) * 0.06f;
            transform.localScale = new Vector3(baseScale.x * pulse, baseScale.y * (1.08f - (pulse - 1f) * 0.45f), baseScale.z * pulse);
        }
    }
}
