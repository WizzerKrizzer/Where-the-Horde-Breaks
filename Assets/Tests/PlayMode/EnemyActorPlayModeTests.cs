using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using TowerDefense.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class EnemyActorPlayModeTests
    {
        private readonly List<Object> cleanupObjects = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var unityObject in cleanupObjects)
            {
                if (unityObject != null)
                {
                    Object.Destroy(unityObject);
                }
            }

            cleanupObjects.Clear();
            yield return null;
        }

        [Test]
        public void LegacyActors_ReceiveStableIndividualSpeedFactors()
        {
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            for (var i = 0; i < 64; i++)
            {
                var first = EnemyActor.GetPersonalSpeedFactor(i);
                Assert.That(first, Is.InRange(0.90f, 1.10f));
                Assert.That(EnemyActor.GetPersonalSpeedFactor(i), Is.EqualTo(first));
                minimum = Mathf.Min(minimum, first);
                maximum = Mathf.Max(maximum, first);
            }

            Assert.That(minimum, Is.LessThan(0.95f));
            Assert.That(maximum, Is.GreaterThan(1.05f));
        }

        [UnityTest]
        public IEnumerator LegacyActors_ResolveAnExactBodyOverlap()
        {
            var routeObject = new GameObject("Legacy collision route");
            cleanupObjects.Add(routeObject);
            var route = routeObject.AddComponent<PathRoute>();
            route.SetWaypoints(new[] { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) });

            var managerObject = new GameObject("Legacy collision manager");
            cleanupObjects.Add(managerObject);
            var manager = managerObject.AddComponent<EnemyManager>();
            manager.SetLevelRoute(route);

            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            enemy.visualScale = 0.45f;
            enemy.speed = 3f;
            cleanupObjects.Add(enemy);
            manager.SpawnDebug(enemy, route);
            manager.SpawnDebug(enemy, route);
            Assert.That(manager.ActiveEnemies, Has.Count.EqualTo(2));

            var first = manager.ActiveEnemies[0];
            var second = manager.ActiveEnemies[1];
            first.transform.position = Vector3.zero;
            second.transform.position = Vector3.zero;

            for (var frame = 0; frame < 30; frame++)
            {
                yield return null;
            }

            var delta = first.transform.position - second.transform.position;
            delta.y = 0f;
            var physicalContact = (enemy.visualScale + enemy.visualScale) * 1.01f * 0.998f;
            Assert.That(delta.magnitude, Is.GreaterThanOrEqualTo(physicalContact * 0.995f),
                "Regular CPU enemies must resolve to their visible body-contact envelope.");
        }
    }
}
