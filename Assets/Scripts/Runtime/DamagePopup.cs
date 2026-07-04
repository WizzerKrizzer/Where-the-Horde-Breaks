using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class DamagePopup : MonoBehaviour
    {
        private TextMesh textMesh;
        private Camera targetCamera;
        private Color baseColor;
        private float age;
        private const float Lifetime = 0.75f;

        public static void Show(Vector3 position, float amount, Color color)
        {
            if (amount <= 0f)
            {
                return;
            }

            var go = new GameObject("DamagePopup");
            go.transform.position = position + Vector3.up * 1.25f;
            var popup = go.AddComponent<DamagePopup>();
            popup.Initialize(amount, color);
        }

        private void Initialize(float amount, Color color)
        {
            targetCamera = Camera.main;
            baseColor = color;
            textMesh = gameObject.AddComponent<TextMesh>();
            textMesh.text = Mathf.CeilToInt(amount).ToString();
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 72;
            textMesh.characterSize = 0.11f;
            textMesh.color = color;
        }

        private void Update()
        {
            age += Time.deltaTime;
            transform.position += Vector3.up * (1.25f * Time.deltaTime);

            if (targetCamera != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - targetCamera.transform.position);
                var distance = Vector3.Distance(transform.position, targetCamera.transform.position);
                transform.localScale = Vector3.one * Mathf.Clamp(distance * 0.085f, 1.2f, 6.5f);
            }

            var remaining = Mathf.Clamp01(1f - age / Lifetime);
            textMesh.color = new Color(baseColor.r, baseColor.g, baseColor.b, remaining);

            if (age >= Lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
