using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    public sealed class BestBotAutomationTests
    {
        [UnityTest]
        public IEnumerator BestBot_UsesIsolatedProfileAndRestoresPlayerStateWhenStopped()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;

            var session = Object.FindFirstObjectByType<GameSession>();
            Assert.That(session, Is.Not.Null);
            var originalProfile = session.Profile;
            var originalLevel = session.Level;
            var originalTimeScale = Time.timeScale;

            var expectedProfiles = new[] { "Best", "Skilled", "Average", "Casual", "Novice" };
            for (var i = 0; i < expectedProfiles.Length; i++)
            {
                Assert.That(session.DevBestBotSelectedProfileName, Is.EqualTo(expectedProfiles[i]));
                session.SelectNextDevBestBotProfile();
            }
            Assert.That(session.DevBestBotSelectedProfileName, Is.EqualTo("Best"));
            session.SetDevBestBotTimeScale(50f);

            session.ToggleDevBestBot();

            Assert.That(session.DevBestBotRunning, Is.True);
            Assert.That(session.Level.id, Is.EqualTo("level_01"));
            Assert.That(session.Profile, Is.Not.SameAs(originalProfile));
            Assert.That(Time.timeScale, Is.EqualTo(50f).Within(0.01f));

            session.AddCurrency(CurrencyType.KillEssence, 100);
            var purchaseMethod = typeof(GameSession).GetMethod("TryBuyBestBotUpgrades", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(purchaseMethod, Is.Not.Null);
            purchaseMethod.Invoke(session, null);
            Assert.That(session.GetUpgradeRank("steady_tithe_01"), Is.EqualTo(session.GetUpgradeMaxRank("steady_tithe_01")));
            Assert.That(session.GetUpgradeRank("base_health_01"), Is.Zero);
            Assert.That(session.GetUpgradeRank("archer_unlock"), Is.EqualTo(1));
            Assert.That(session.GetUpgradeEffectTotal(UpgradeEffectType.ActiveWeaponDamagePercent), Is.GreaterThan(0f));
            Assert.That(
                session.GetUpgradeEffectTotal(UpgradeEffectType.PerTypeTowerLimitFlat, "archer") +
                session.GetUpgradeEffectTotal(UpgradeEffectType.TowerDamagePercent, "archer") +
                session.GetUpgradeEffectTotal(UpgradeEffectType.TowerFireRatePercent, "archer"),
                Is.GreaterThan(0f));

            yield return new WaitForSecondsRealtime(0.35f);

            Assert.That(session.DevBestBotAttemptCount, Is.GreaterThanOrEqualTo(1));
            session.StopDevBestBot();

            Assert.That(session.DevBestBotRunning, Is.False);
            Assert.That(session.DevBestBotReportAvailable, Is.True);
            Assert.That(session.DevBestBotPurchaseHistory, Does.Contain("Steady Tithe"));
            Assert.That(session.DevBestBotPurchaseHistory, Does.Contain("+3 Kill Essence after each run"));
            Assert.That(session.DevBestBotPurchaseHistory, Does.Not.Contain("Reinforced Gate"));
            Assert.That(session.DevBestBotReport, Does.Contain("Upgrade spend: towers"));
            Assert.That(session.Profile, Is.SameAs(originalProfile));
            Assert.That(session.Level, Is.SameAs(originalLevel));
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale).Within(0.01f));

            session.DismissDevBestBotReport();
            session.SelectNextDevBestBotProfile();
            session.SelectNextDevBestBotProfile();
            session.SelectNextDevBestBotProfile();
            Assert.That(session.DevBestBotSelectedProfileName, Is.EqualTo("Casual"));
            session.ToggleDevBestBot();
            var activeWeapon = Object.FindFirstObjectByType<ActiveWeaponController>();
            Assert.That(activeWeapon, Is.Not.Null);
            Assert.That(activeWeapon.DevAutoEfficiency, Is.EqualTo(0.85f).Within(0.001f));
            session.AddCurrency(CurrencyType.KillEssence, 100);
            purchaseMethod.Invoke(session, null);
            foreach (var node in session.UpgradeNodes)
            {
                Assert.That(session.CanPurchaseUpgrade(node.id), Is.False, $"Casual left affordable upgrade '{node.id}' unpurchased.");
            }
            session.StopDevBestBot();

            session.SelectNextDevBestBotProfile();
            Assert.That(session.DevBestBotSelectedProfileName, Is.EqualTo("Novice"));
            session.ToggleDevBestBot();
            Assert.That(activeWeapon.DevAutoEfficiency, Is.EqualTo(0.65f).Within(0.001f));
            session.StopDevBestBot();

            session.SelectNextDevBestBotProfile();
            Assert.That(session.DevBestBotSelectedProfileName, Is.EqualTo("Best"));
            session.StartAllDevBestBots();
            Assert.That(session.DevBestBotRunAll, Is.True);
            Assert.That(session.DevBestBotRunning, Is.True);
            Assert.That(session.DevBestBotSelectedProfileName, Is.EqualTo("Best"));
            session.StopDevBestBot();
            Assert.That(session.DevBestBotRunAll, Is.False);
            Assert.That(session.Profile, Is.SameAs(originalProfile));

            var cleanupScene = SceneManager.CreateScene("BestBotTestCleanup");
            SceneManager.SetActiveScene(cleanupScene);
            yield return SceneManager.UnloadSceneAsync("Main");
        }
    }
}
