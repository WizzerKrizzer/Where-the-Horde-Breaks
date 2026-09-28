using System.Collections;
using NUnit.Framework;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class MeteorImpactVisualTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            foreach (var visual in Object.FindObjectsByType<MeteorImpactVisual>(FindObjectsSortMode.None))
            {
                Object.Destroy(visual.gameObject);
            }

            foreach (var visual in Object.FindObjectsByType<ShockwaveRingVisual>(FindObjectsSortMode.None))
            {
                Object.Destroy(visual.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator MeteorImpact_CreatesPresentationAndRestoresAcceleratedTimeScale()
        {
            Time.timeScale = 7f;
            MeteorImpactVisual.Spawn(Vector3.zero, 10f);

            Assert.That(Time.timeScale, Is.Zero, "Meteor impact did not create its hit-stop.");
            Assert.That(GameObject.Find("MeteorImpactFlash"), Is.Not.Null);
            Assert.That(GameObject.Find("MeteorScorchedCrater"), Is.Not.Null);
            Assert.That(GameObject.Find("MeteorGroundDust"), Is.Not.Null);
            Assert.That(GameObject.Find("MeteorDebris"), Is.Not.Null);

            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(Time.timeScale, Is.EqualTo(7f).Within(0.01f),
                "Meteor hit-stop did not restore the previous dev speed.");
        }

        [UnityTest]
        public IEnumerator ShockwaveVisualSpam_KeepsBoundedRingAndDustCount()
        {
            for (var i = 0; i < 16; i++)
            {
                ShockwaveRingVisual.Spawn(
                    Vector3.zero,
                    10f,
                    50f,
                    20f,
                    Vector3.zero,
                    50f);
            }

            yield return null;
            var rings = Object.FindObjectsByType<ShockwaveRingVisual>(FindObjectsSortMode.None);
            Assert.That(rings.Length, Is.LessThanOrEqualTo(12));
            foreach (var ring in rings)
            {
                Assert.That(ring.GetComponentInChildren<ParticleSystem>(), Is.Not.Null,
                    "Shockwave ring was missing its travelling dust.");
            }
        }
    }
}
