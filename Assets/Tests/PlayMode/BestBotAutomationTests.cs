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

            session.ToggleDevBestBot();

            Assert.That(session.DevBestBotRunning, Is.True);
            Assert.That(session.Level.id, Is.EqualTo("level_01"));
            Assert.That(session.Profile, Is.Not.SameAs(originalProfile));
            Assert.That(Time.timeScale, Is.EqualTo(20f).Within(0.01f));

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
            Assert.That(session.DevBestBotReport, Does.Contain("Combat upgrade spend: towers"));
            Assert.That(session.Profile, Is.SameAs(originalProfile));
            Assert.That(session.Level, Is.SameAs(originalLevel));
            Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale).Within(0.01f));

            var cleanupScene = SceneManager.CreateScene("BestBotTestCleanup");
            SceneManager.SetActiveScene(cleanupScene);
            yield return SceneManager.UnloadSceneAsync("Main");
        }
    }
}
