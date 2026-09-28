using TowerDefense.Runtime;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerDefense.Editor
{
    [InitializeOnLoad]
    internal static class HordeAssemblyReloadGuard
    {
        static HordeAssemblyReloadGuard()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseHordeResources;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseHordeResources;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
        }

        private static void OnCompilationStarted(object context)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            ReleaseHordeResources();
            EditorApplication.isPlaying = false;
        }

        private static void ReleaseHordeResources()
        {
            var managers = Object.FindObjectsByType<HordeEnemyManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < managers.Length; i++)
            {
                managers[i].StopWave();
            }

            AsyncGPUReadback.WaitAllRequests();
        }
    }
}
