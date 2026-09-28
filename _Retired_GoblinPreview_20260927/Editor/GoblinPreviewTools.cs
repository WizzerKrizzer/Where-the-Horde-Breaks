using TowerDefense.Runtime;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TowerDefense.Editor
{
    public static class GoblinPreviewTools
    {
        public static void ValidateShaders()
        {
            var compute = Resources.Load<ComputeShader>("HordeSimulation");
            var shader = Resources.Load<Shader>("HordeIndirect");
            Debug.Log($"HordeSimulation valid={compute != null && compute.HasKernel("ClearGrid")}; HordeIndirect supported={shader != null && shader.isSupported}");
            foreach (var message in ShaderUtil.GetComputeShaderMessages(compute))
                Debug.LogError($"HordeSimulation {message.severity}: {message.message} line {message.line}");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Debug.LogError($"HordeIndirect {message.severity}: {message.message} line {message.line}");
        }

        [MenuItem("Tools/Goblin Preview/Build separate playable test")]
        public static void BuildPreview()
        {
            var originalName = PlayerSettings.productName;
            var previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            try
            {
                // Separate product name also isolates the test's save and preferences.
                PlayerSettings.productName = "WhereTheHordeBreaks Goblin Preview";
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                Directory.CreateDirectory("Builds/GoblinPreview");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Main.unity" },
                    locationPathName = "Builds/GoblinPreview/GoblinPreview.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                    extraScriptingDefines = new[] { "GOBLIN_VISUAL_PREVIEW" }
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception($"Goblin preview build failed: {report.summary.result}");
                Debug.Log("Goblin preview built successfully: Builds/GoblinPreview/GoblinPreview.exe");
            }
            finally
            {
                PlayerSettings.productName = originalName;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previousBackend);
            }
        }

        [MenuItem("Tools/Goblin Preview/Enable for next wave")]
        public static void Enable()
        {
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 1);
            PlayerPrefs.Save();
            Debug.Log("Goblin Running preview enabled. Restart the wave or Play Mode. This is a full-detail visual test, not a 10,000-enemy performance build.");
        }

        [MenuItem("Tools/Goblin Preview/Use original visuals for next wave")]
        public static void Disable()
        {
            PlayerPrefs.SetInt(GoblinPreviewAssets.PreferenceKey, 0);
            PlayerPrefs.Save();
            Debug.Log("Original horde visuals restored for the next wave/Play Mode.");
        }
    }
}
