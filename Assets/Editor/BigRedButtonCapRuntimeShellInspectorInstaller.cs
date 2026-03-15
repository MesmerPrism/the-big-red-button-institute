using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheBigRedButtonInstitute.Editor
{
    public static class BigRedButtonCapRuntimeShellInspectorInstaller
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ButtonName = "Big Red Button";
        const string CapChildName = "button";

        [MenuItem("Tools/Big Red Button/Install Cap Runtime Shell Inspector")]
        public static void InstallFromMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var button = scene.GetRootGameObjects().FirstOrDefault(root => root.name == ButtonName);
            if (button == null)
            {
                Debug.LogError($"Could not find '{ButtonName}' in {ScenePath}.");
                return;
            }

            var capRenderer = button.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.name == CapChildName);
            if (capRenderer == null)
            {
                Debug.LogError($"Could not find the cap renderer '{CapChildName}' under '{ButtonName}'.");
                return;
            }

            var inspector = capRenderer.GetComponent<BigRedButtonCapRuntimeShellInspector>();
            if (inspector == null)
            {
                inspector = capRenderer.gameObject.AddComponent<BigRedButtonCapRuntimeShellInspector>();
            }

            inspector.Configure(
                capRenderer,
                targetShellOversize: 1.08f,
                targetPulseAmplitude: 0.05f,
                targetPulseFrequency: 2.4f,
                targetAlwaysOnTop: true);

            var serializedInspector = new SerializedObject(inspector);
            serializedInspector.FindProperty("sourceRenderer").objectReferenceValue = capRenderer;
            serializedInspector.FindProperty("autoResolveSource").boolValue = true;
            serializedInspector.FindProperty("shellOversize").floatValue = 1.08f;
            serializedInspector.FindProperty("pulseAmplitude").floatValue = 0.05f;
            serializedInspector.FindProperty("pulseFrequency").floatValue = 2.4f;
            serializedInspector.FindProperty("alwaysOnTop").boolValue = true;
            serializedInspector.FindProperty("shellColor").colorValue = new Color(0.34f, 1f, 0.18f, 0.24f);
            serializedInspector.FindProperty("pulseColor").colorValue = new Color(1f, 0.98f, 0.28f, 0.58f);
            serializedInspector.FindProperty("emissionIntensity").floatValue = 1.5f;
            serializedInspector.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(inspector);
            EditorUtility.SetDirty(capRenderer.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Installed cap runtime shell inspector on Big Red Button/RootNode/button.");
        }
    }
}
