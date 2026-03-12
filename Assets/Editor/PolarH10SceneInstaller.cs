using TheBigRedButtonInstitute.Biofeedback;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheBigRedButtonInstitute.Editor
{
    public static class PolarH10SceneInstaller
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string RuntimeRootName = "VR Runtime";

        [MenuItem("Tools/Big Red Button/Install Polar H10 Runtime")]
        public static void InstallFromMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject runtimeRoot = null;

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == RuntimeRootName)
                {
                    runtimeRoot = rootObject;
                    break;
                }
            }

            if (runtimeRoot == null)
            {
                runtimeRoot = new GameObject(RuntimeRootName);
                SceneManager.MoveGameObjectToScene(runtimeRoot, scene);
            }

            if (runtimeRoot.GetComponent<PolarH10RuntimeManager>() == null)
            {
                runtimeRoot.AddComponent<PolarH10RuntimeManager>();
            }

            EditorUtility.SetDirty(runtimeRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Installed Polar H10 runtime manager into SampleScene.");
        }
    }
}
