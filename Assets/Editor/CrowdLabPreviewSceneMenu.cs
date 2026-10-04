using System.IO;
using TowerDefense.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TowerDefense.Editor
{
    public static class CrowdLabPreviewSceneMenu
    {
        private const string ScenePath = "Assets/Scenes/CrowdLab.unity";

        [MenuItem("Tools/Crowd Lab/Open Interactive Preview")]
        public static void OpenInteractivePreview()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) CreateSceneAsset();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/Crowd Lab/Rebuild Preview Scene")]
        public static void RebuildPreviewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!EditorUtility.DisplayDialog(
                    "Rebuild Crowd Lab preview?",
                    "This replaces Assets/Scenes/CrowdLab.unity with a clean preview scene.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }
            CreateSceneAsset();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        public static void CreateSceneAssetForAutomation()
        {
            CreateSceneAsset();
            Debug.Log($"Created interactive Crowd Lab preview at {ScenePath}");
        }

        private static void CreateSceneAsset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var previewObject = new GameObject("CrowdLab Preview");
            previewObject.tag = "MainCamera";
            var camera = previewObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.depth = 0f;
            previewObject.AddComponent<CrowdLabRunner>();
            previewObject.AddComponent<CrowdLabPreview>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            Selection.activeGameObject = previewObject;
        }
    }
}
