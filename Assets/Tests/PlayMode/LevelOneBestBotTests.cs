using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using TowerDefense.Data;
using TowerDefense.Input;
using TowerDefense.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TowerDefense.Tests
{
    /// <summary>
    /// Level 1's theoretical shortest time (user, 2026-11-18): the "Best" bot plays it from an empty
    /// profile. It throws the spear whenever it is ready, buys the upgrades it rates best after
    /// every run and builds its towers, until it wins or gives up after MaxAttempts runs. It plays
    /// on a fixed clock as fast as the machine allows (Time.captureFramerate), so the times are
    /// game seconds. Every run is logged as a BOT line; the bot's own report goes to
    /// Logs/Reports/LevelOneBot/report.txt. The bot keeps its own profile and puts the player's back.
    /// CAPTURE_RUN=n records every run from the n-th until one is won, at 30 frames a game second, from
    /// the level's own view, to Logs/Captures/LevelOneBotWin (only the won run is kept).
    /// </summary>
    public sealed class LevelOneBestBotTests
    {
        private const string Output = "Logs/Reports/LevelOneBot";
        private const int MaxAttempts = 80;
        private const int FramesPerGameSecond = 20;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        }

        [UnityTest, Timeout(3600000), Explicit("Plays the whole of level 1 with the bot; takes minutes.")]
        public IEnumerator BestBot_BeatsLevelOne()
        {
            PrehistoricTowerTests.RequireComputeShaders();
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var session = Object.FindFirstObjectByType<GameSession>();
            var router = Object.FindFirstObjectByType<PlayerInputRouter>();
            var towers = Object.FindFirstObjectByType<TowerManager>();
            var horde = Object.FindFirstObjectByType<HordeEnemyManager>();
            Assert.That(session != null && router != null && towers != null && horde != null, Is.True, "The Main scene is missing a part.");
            session.SelectLevel("level_01");
            yield return null;
            router.enabled = false;
            typeof(PlayerInputRouter).GetProperty("Current").SetValue(router, default(GameInputState));

            // BOT_PROFILE picks the bot (Best by default; Smart plays as a sensible player would).
            var profileName = System.Environment.GetEnvironmentVariable("BOT_PROFILE");
            for (var guard = 0; !string.IsNullOrEmpty(profileName) && session.DevBestBotSelectedProfileName != profileName && guard < 10; guard++)
            {
                session.SelectNextDevBestBotProfile();
            }

            Debug.Log($"BOT profile {session.DevBestBotSelectedProfileName}");
            session.ToggleDevBestBot();
            Assert.That(session.DevBestBotRunning, Is.True, session.DevBestBotStatus);
            Time.timeScale = 1f;
            Time.captureFramerate = FramesPerGameSecond;

            // Recording the winning run (user, 2026-11-18).
            var captureRun = int.TryParse(System.Environment.GetEnvironmentVariable("CAPTURE_RUN"), out var parsedRun) ? parsedRun : 0;
            // CAPTURE_ALL=1 keeps every run from CAPTURE_RUN on, each in its own run_NN folder, and
            // CAPTURE_EVERY=n writes every n-th frame only (user, 2026-11-18: all 15 runs, sped up).
            var captureAll = System.Environment.GetEnvironmentVariable("CAPTURE_ALL") == "1";
            var captureEvery = int.TryParse(System.Environment.GetEnvironmentVariable("CAPTURE_EVERY"), out var parsedEvery) ? Mathf.Max(1, parsedEvery) : 1;
            const string captureOutput = "Logs/Captures/LevelOneBotWin";
            var runFolder = captureOutput;
            var captureTick = 0;
            if (captureAll && Directory.Exists(captureOutput))
            {
                Directory.Delete(captureOutput, true);
            }
            Camera captureCamera = null;
            RenderTexture captureTarget = null;
            Texture2D captureTexture = null;
            if (captureRun > 0)
            {
                var main = Camera.main;
                captureCamera = new GameObject("Bot Win Camera").AddComponent<Camera>();
                captureCamera.CopyFrom(main);
                captureCamera.enabled = false;
                captureTarget = new RenderTexture(StyleLowPolyTests.Width, StyleLowPolyTests.Height, 24) { antiAliasing = 4 };
                captureTexture = new Texture2D(StyleLowPolyTests.Width, StyleLowPolyTests.Height, TextureFormat.RGB24, false);
                captureCamera.targetTexture = captureTarget;
            }

            var recording = false;
            var recordedWin = false;
            var captureFrame = 0;
            var log = new StringBuilder();
            var realStart = Time.realtimeSinceStartup;
            var gameSeconds = 0f;
            var wasRunning = false;
            var runStart = 0f;
            var lastAttempt = 0;
            while (session.DevBestBotRunning && session.DevBestBotAttemptCount <= MaxAttempts)
            {
                if (recording && captureTick++ % captureEvery == 0)
                {
                    CavemenLevelTwoCaptureTests.DrawHorde(horde);
                    yield return StyleLowPolyTests.Capture(captureCamera, captureTarget, captureTexture, $"{runFolder}/frame_{captureFrame++:D4}.png");
                }
                else
                {
                    yield return null;
                }

                gameSeconds += Time.deltaTime;
                var runningNow = session.IsRunning;
                if (runningNow && !wasRunning)
                {
                    runStart = gameSeconds;
                    if (captureRun > 0 && !recordedWin && session.DevBestBotAttemptCount >= captureRun)
                    {
                        runFolder = captureAll ? $"{captureOutput}/run_{session.DevBestBotAttemptCount:D2}" : captureOutput;
                        if (!captureAll && Directory.Exists(captureOutput))
                        {
                            Directory.Delete(captureOutput, true);
                        }

                        Directory.CreateDirectory(runFolder);
                        captureFrame = 0;
                        captureTick = 0;
                        recording = true;
                        Time.captureFramerate = 30;
                    }
                }

                if (!runningNow && wasRunning && session.Finished)
                {
                    var line = $"BOT run {session.DevBestBotAttemptCount}: {(session.Won ? "WON" : "lost")} after {gameSeconds - runStart:F1} s, " +
                               $"kills {session.EnemiesKilled}, towers {towers.TowerCount}, " +
                               $"Kill Essence {session.Profile.GetCurrency(CurrencyType.KillEssence)}, wall {(LevelWall.Active != null ? LevelWall.Active.CurrentHealth : -1f):F0}";
                    Debug.Log(line);
                    log.AppendLine(line);
                    lastAttempt = session.DevBestBotAttemptCount;
                    if (recording)
                    {
                        // A lost run's frames go; the won one's stay.
                        recording = false;
                        recordedWin = session.Won;
                        Time.captureFramerate = FramesPerGameSecond;
                        Debug.Log($"BOT recorded run {lastAttempt}: {(session.Won ? "won, kept" : "lost")} ({captureFrame} frames)");
                    }
                }

                wasRunning = runningNow;
            }

            var gaveUp = session.DevBestBotRunning;
            if (gaveUp)
            {
                session.StopDevBestBot();
                yield return null;
            }

            var summary = $"BOT level 1: {(gaveUp ? "gave up" : "finished")} after {lastAttempt} runs, " +
                          $"{gameSeconds:F0} game seconds ({gameSeconds / 60f:F1} min) in {Time.realtimeSinceStartup - realStart:F0} real seconds.";
            Debug.Log(summary);
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/report.txt",
                summary + "\n\n" + log + "\n" + session.DevBestBotReport + "\n\n" + session.DevBestBotPurchaseHistory);
            Debug.Log(session.DevBestBotReport);
            Assert.That(gaveUp, Is.False, $"The bot did not beat level 1 in {MaxAttempts} runs.");
        }
    }
}
