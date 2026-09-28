using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace TowerDefense.Tests
{
    // Only included by BuildAcceptancePlayer (IncludeTestAssemblies).
    public sealed class CrowdAcceptancePlayer : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRequestedAudit()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--crowd-acceptance") < 0 && Array.IndexOf(args, "--crowd-timing-only") < 0) return;
            var obj = new GameObject("Crowd acceptance runner");
            DontDestroyOnLoad(obj);
            obj.AddComponent<CrowdAcceptancePlayer>().StartCoroutine(Run(Array.IndexOf(args, "--crowd-timing-only") >= 0));
        }

        private static IEnumerator Run(bool timingOnly)
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            UnityEngine.Profiling.Profiler.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, true);
            var test = new CrowdMergeValidationTests();
            var checks = timingOnly
                ? new[] { test.HardwareFrameTiming_Probe() }
                : new[] { test.HardwareFrameTiming_Probe(), test.ActualTenThousand_ThroughMergeAndTail() };
            Directory.CreateDirectory("Logs/MergeValidation");
            foreach (var check in checks)
            {
                while (true)
                {
                    bool more;
                    try { more = check.MoveNext(); }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        File.WriteAllText("Logs/MergeValidation/player-result.txt", "FAILED\n" + exception);
                        (check as IDisposable)?.Dispose();
                        Application.Quit(1);
                        yield break;
                    }
                    if (!more) break;
                    yield return check.Current;
                }
                (check as IDisposable)?.Dispose();
            }
            File.WriteAllText("Logs/MergeValidation/player-result.txt", "PASSED");
            Application.Quit(0);
        }
    }
}
