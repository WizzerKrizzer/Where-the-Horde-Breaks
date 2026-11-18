using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Where the slinger's throw is moving and where it stands still (user, 2026-11-18: after a throw
    /// he stood doing nothing for about half a second). Logs the right hand's speed through the clip
    /// as THROW lines. Needs the local Mixamo models.
    /// </summary>
    public sealed class SlingerThrowTimingTests
    {
        [UnityTest, Explicit("Measures the slinger's throw clip.")]
        public IEnumerator Throw_HandSpeedThroughTheClip()
        {
            var prefab = Resources.Load<GameObject>("Mixamo/stonehide");
            AnimationClip clip = null;
            foreach (var candidate in Resources.LoadAll<AnimationClip>("Mixamo/stonehide"))
            {
                if (candidate.name.Contains("Throw"))
                {
                    clip = candidate;
                }
            }

            if (prefab == null || clip == null)
            {
                Assert.Ignore("The slinger model is not installed.");
            }

            var rig = Object.Instantiate(prefab);
            Transform hand = null;
            foreach (var bone in rig.GetComponentsInChildren<Transform>())
            {
                if (bone.name.EndsWith("RightHand"))
                {
                    hand = bone;
                }
            }

            Assert.That(hand, Is.Not.Null);
            var log = new StringBuilder($"THROW clip {clip.length:F2} s: ");
            var steps = 52;
            clip.SampleAnimation(rig, 0f);
            var last = hand.position;
            for (var i = 1; i <= steps; i++)
            {
                var t = clip.length * i / steps;
                clip.SampleAnimation(rig, t);
                var speed = (hand.position - last).magnitude / (clip.length / steps);
                last = hand.position;
                log.Append($"{t / clip.length * 2.6f:F2}:{speed:F1} ");
            }

            Debug.Log(log.ToString());
            Object.Destroy(rig);
            yield return null;
        }
    }
}
