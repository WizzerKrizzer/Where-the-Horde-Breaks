using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TowerDefense.Editor
{
    public static class BuildPlayerTools
    {
        private const string MainScene = "Assets/Scenes/Main.unity";
        private const string BuildRoot = "Builds";
        private const string ProductName = "WhereTheHordeBreaks";

        [MenuItem("Tools/Build/Windows Development Build")]
        public static void BuildWindowsDevelopment()
        {
            BuildWindows(development: true);
        }

        [MenuItem("Tools/Build/Windows Release Build")]
        public static void BuildWindowsRelease()
        {
            BuildWindows(development: false);
        }

        public static void BuildWindowsDevelopmentBatch()
        {
            BuildWindows(development: true);
        }

        public static void BuildWindowsReleaseBatch()
        {
            BuildWindows(development: false);
        }

        private static void BuildWindows(bool development)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException("Could not resolve project root.");
            }

            var folderName = development ? "Development" : "Release";
            var outputDirectory = Path.Combine(projectRoot, BuildRoot, folderName);
            Directory.CreateDirectory(outputDirectory);

            var outputPath = Path.Combine(outputDirectory, $"{ProductName}.exe");
            var options = development
                ? BuildOptions.Development | BuildOptions.AllowDebugging
                : BuildOptions.None;

            var report = BuildPipeline.BuildPlayer(
                new[] { MainScene },
                outputPath,
                BuildTarget.StandaloneWindows64,
                options);

            var summary = report.summary;
            Debug.Log($"Build {summary.result}: {outputPath} ({summary.totalSize} bytes)");
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Build failed with result: {summary.result}");
            }
        }
    }
}
