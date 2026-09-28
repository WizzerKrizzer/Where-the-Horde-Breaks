using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TowerDefense.Editor
{
    [InitializeOnLoad]
    public static class CrowdValidationTools
    {
        private static TestRunnerApi runner;
        static CrowdValidationTools()
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
        }

        [MenuItem("Tools/Goblin Preview/Validate crowd fixes %#F9")]
        public static void Run()
        {
            GoblinPreviewTools.ValidateShaders();
            SessionState.SetInt("CrowdValidation.Preference", PlayerPrefs.GetInt(Runtime.GoblinPreviewAssets.PreferenceKey, Runtime.GoblinPreviewAssets.DefaultEnabled));
            PlayerPrefs.SetInt(Runtime.GoblinPreviewAssets.PreferenceKey, 0);
            SessionState.SetBool("CrowdValidation.Running", true);
            runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = new[]
                {
                    "TowerDefense.Tests.GoblinPreviewPlayModeTests",
                    "TowerDefense.Tests.GpuHordeTargetingTests.RedGoblinQueue_TouchesBodiesAndSettlesWithoutRunningInPlace",
                    "TowerDefense.Tests.GpuHordeTargetingTests.ContactProjection_SeparatesOverlappingAgents",
                    "TowerDefense.Tests.GpuHordeTargetingTests.DenseQueue_AtBarricade_PreservesEnemyBodySpacing",
                    "TowerDefense.Tests.GpuHordeTargetingTests.DenseQueue_FlowsThroughNarrowRightAngleWithoutDeadlock",
                    "TowerDefense.Tests.GpuHordeTargetingTests.PackedGoblinQueue_StartsTogetherOnFirstTickAfterBlockerRemoval"
                }
            }));
        }

        public static void BuildAcceptancePlayer()
        {
            var target = UnityEditor.Build.NamedBuildTarget.Standalone;
            var backend = PlayerSettings.GetScriptingBackend(target);
            var frameTiming = PlayerSettings.enableFrameTimingStats;
            try
            {
                PlayerSettings.enableFrameTimingStats = true;
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
                Directory.CreateDirectory("Builds/CrowdAcceptance");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Main.unity" },
                    locationPathName = "Builds/CrowdAcceptance/CrowdAcceptance.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development | BuildOptions.IncludeTestAssemblies,
                    extraScriptingDefines = new[] { "GOBLIN_VISUAL_PREVIEW" }
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new System.Exception("Crowd acceptance player build failed.");
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, backend);
                PlayerSettings.enableFrameTimingStats = frameTiming;
            }
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool("CrowdValidation.Running", false)) return;
                SessionState.SetBool("CrowdValidation.Running", false);
                PlayerPrefs.SetInt(Runtime.GoblinPreviewAssets.PreferenceKey, SessionState.GetInt("CrowdValidation.Preference", 1));
                Directory.CreateDirectory("Logs/CrowdFix");
                TestRunnerApi.SaveResultToFile(result, "Logs/CrowdFix/results.xml");
                Debug.Log($"Crowd validation: {result.ResultState}; passed={result.PassCount}; failed={result.FailCount}");
            }
        }
    }
}
