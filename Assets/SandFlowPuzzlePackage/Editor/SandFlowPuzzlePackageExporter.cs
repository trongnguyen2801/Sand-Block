#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SandFlowPuzzle.Editor
{
    public static class SandFlowPuzzlePackageExporter
    {
        private const string PackageFolder = "Assets/SandFlowPuzzlePackage";
        private const string PrefabPath = PackageFolder + "/Prefabs/SandFlowPuzzleGame.prefab";
        private const string SourceScene = "Assets/Hypercasual Game Engine/Core/Scenes/HC-Games.unity";

        [MenuItem("Tools/Sand Flow Puzzle/Export Unity Package")]
        public static void ExportUnityPackage()
        {
            AssetDatabase.Refresh();
            GameObject gameRoot = FindLoadedGameRoot();
            if (gameRoot != null)
            {
                PrefabUtility.SaveAsPrefabAsset(gameRoot, PrefabPath, out bool prefabSaved);
                if (!prefabSaved)
                    Debug.LogWarning("[SandFlowPuzzle] Could not capture the loaded game root as a prefab.");
            }
            else
            {
                Debug.LogWarning(
                    "[SandFlowPuzzle] #16_SandFlowPuzzle is not loaded. " +
                    "The package will be exported without refreshing SandFlowPuzzleGame.prefab.");
            }

            AssetDatabase.SaveAssets();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputPath = Path.Combine(projectRoot, "SandFlowPuzzlePackage.unitypackage");
            AssetDatabase.ExportPackage(
                PackageFolder,
                outputPath,
                ExportPackageOptions.Recurse);
            Debug.Log($"[SandFlowPuzzle] Package exported to: {outputPath}");
        }

        public static void ExportUnityPackageBatch()
        {
            EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            ExportUnityPackage();
        }

        private static GameObject FindLoadedGameRoot()
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
                if (candidate.name == "#16_SandFlowPuzzle") return candidate.gameObject;
            }
            return null;
        }
    }
}
#endif
