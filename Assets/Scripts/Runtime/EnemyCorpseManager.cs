using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense.Runtime
{
    public sealed class EnemyCorpseManager : MonoBehaviour
    {
        [SerializeField] private int maxCorpses = 260;
        [SerializeField] private int maxBloodDecals = 360;

        private readonly Queue<GameObject> corpses = new();
        private readonly Queue<GameObject> bloodDecals = new();

        public void SpawnCorpse(EnemyActor enemy)
        {
            if (enemy == null || enemy.Definition == null)
            {
                return;
            }

            var position = enemy.transform.position;
            position.y = 0.018f;
            var corpse = GameObject.CreatePrimitive(PrimitiveType.Cube);
            corpse.name = $"Corpse_{enemy.Definition.id}";
            corpse.transform.SetParent(transform);
            corpse.transform.position = position + new Vector3(Random.Range(-0.08f, 0.08f), 0f, Random.Range(-0.08f, 0.08f));
            corpse.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var size = enemy.Definition.visualScale * Random.Range(0.75f, 1.05f);
            corpse.transform.localScale = new Vector3(size * 0.9f, 0.025f, size * 1.25f);
            corpse.GetComponent<Renderer>().material = BootstrapMaterials.Get(Color.Lerp(enemy.Definition.color, new Color(0.08f, 0.06f, 0.045f), 0.62f));
            RemovePrimitiveCollider(corpse);
            corpses.Enqueue(corpse);
            Trim(corpses, maxCorpses);

            SpawnBlood(position, enemy.Definition.visualScale);
        }

        public void ClearAllVisuals()
        {
            ClearQueue(corpses);
            ClearQueue(bloodDecals);
        }

        private void SpawnBlood(Vector3 position, float scale)
        {
            var decal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            decal.name = "BloodDecal";
            decal.transform.SetParent(transform);
            decal.transform.position = position + new Vector3(Random.Range(-0.16f, 0.16f), 0.012f, Random.Range(-0.16f, 0.16f));
            decal.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var radius = scale * Random.Range(0.65f, 1.25f);
            decal.transform.localScale = new Vector3(radius, 0.01f, radius * Random.Range(0.65f, 1.1f));
            decal.GetComponent<Renderer>().material = BootstrapMaterials.Get(new Color(0.32f, 0.015f, 0.01f));
            RemovePrimitiveCollider(decal);
            bloodDecals.Enqueue(decal);
            Trim(bloodDecals, maxBloodDecals);
        }

        private static void RemovePrimitiveCollider(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            var components = target.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component != null && component.GetType().Name.Contains("Collider"))
                {
                    Destroy(component);
                    return;
                }
            }
        }

        private static void Trim(Queue<GameObject> queue, int maxCount)
        {
            while (queue.Count > maxCount)
            {
                var old = queue.Dequeue();
                if (old != null)
                {
                    Destroy(old);
                }
            }
        }

        private static void ClearQueue(Queue<GameObject> queue)
        {
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (item != null)
                {
                    Destroy(item);
                }
            }
        }
    }
}
