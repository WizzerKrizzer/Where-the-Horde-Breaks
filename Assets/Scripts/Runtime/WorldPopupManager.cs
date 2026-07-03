using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class WorldPopupManager : MonoBehaviour
    {
        private Camera targetCamera;

        public void Initialize(Camera camera)
        {
            targetCamera = camera;
        }

        public void Show(string message, Vector3 worldPosition)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var go = new GameObject("WorldPopup");
            go.transform.position = worldPosition + Vector3.up * 1.4f;
            var popup = go.AddComponent<WorldPopup>();
            popup.Initialize(message, targetCamera);
        }
    }

    internal sealed class WorldPopup : MonoBehaviour
    {
        private TextMesh textMesh;
        private Camera targetCamera;
        private float age;
        private const float Lifetime = 1.15f;

        public void Initialize(string message, Camera camera)
        {
            targetCamera = camera;
            textMesh = gameObject.AddComponent<TextMesh>();
            textMesh.text = message;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 48;
            textMesh.characterSize = 0.1f;
            textMesh.color = new Color(1f, 0.78f, 0.28f, 1f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            transform.position += Vector3.up * (0.75f * Time.deltaTime);

            if (targetCamera != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - targetCamera.transform.position);
                var distance = Vector3.Distance(transform.position, targetCamera.transform.position);
                transform.localScale = Vector3.one * Mathf.Clamp(distance * 0.09f, 1.4f, 7.5f);
            }

            var remaining = Mathf.Clamp01(1f - age / Lifetime);
            textMesh.color = new Color(1f, 0.78f, 0.28f, remaining);

            if (age >= Lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
