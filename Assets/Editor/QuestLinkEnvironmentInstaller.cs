using TheBigRedButtonInstitute.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheBigRedButtonInstitute.Editor
{
    public static class QuestLinkEnvironmentInstaller
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string RuntimeRootName = "VR Runtime";
        const string SkyboxMaterialPath = "Assets/Settings/QuestLinkSkybox.mat";

        static readonly Color SkyTint = new(0.86f, 0.87f, 0.89f, 1f);
        static readonly Color SkyGroundColor = new(0.58f, 0.60f, 0.63f, 1f);
        static readonly Color AmbientSkyColor = new(0.72f, 0.74f, 0.76f, 1f);
        static readonly Color AmbientEquatorColor = new(0.55f, 0.57f, 0.60f, 1f);
        static readonly Color AmbientGroundColor = new(0.34f, 0.36f, 0.39f, 1f);
        static readonly Color CameraFallbackColor = new(0.86f, 0.87f, 0.89f, 0f);

        [MenuItem("Tools/Big Red Button/Install Quest Link Environment")]
        public static void InstallFromMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var runtimeRoot = FindOrCreateRuntimeRoot(scene);
            InstallIntoScene(scene, runtimeRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void InstallIntoScene(Scene scene, GameObject runtimeRoot)
        {
            if (!scene.IsValid())
            {
                return;
            }

            runtimeRoot ??= FindOrCreateRuntimeRoot(scene);

            var skyboxMaterial = EnsureSkyboxMaterialAsset();
            if (skyboxMaterial == null)
            {
                return;
            }

            var setup = runtimeRoot.GetComponent<QuestLinkEnvironmentSetup>() ?? runtimeRoot.AddComponent<QuestLinkEnvironmentSetup>();
            var serializedObject = new SerializedObject(setup);
            serializedObject.FindProperty("skyboxMaterialTemplate").objectReferenceValue = skyboxMaterial;
            serializedObject.FindProperty("skyTint").colorValue = SkyTint;
            serializedObject.FindProperty("skyGroundColor").colorValue = SkyGroundColor;
            serializedObject.FindProperty("skyExposure").floatValue = 0.88f;
            serializedObject.FindProperty("skyAtmosphereThickness").floatValue = 0.12f;
            serializedObject.FindProperty("ambientSkyColor").colorValue = AmbientSkyColor;
            serializedObject.FindProperty("ambientEquatorColor").colorValue = AmbientEquatorColor;
            serializedObject.FindProperty("ambientGroundColor").colorValue = AmbientGroundColor;
            serializedObject.FindProperty("ambientIntensity").floatValue = 0.8f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            RenderSettings.skybox = skyboxMaterial;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSkyColor;
            RenderSettings.ambientEquatorColor = AmbientEquatorColor;
            RenderSettings.ambientGroundColor = AmbientGroundColor;
            RenderSettings.ambientIntensity = 0.8f;

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                foreach (var camera in rootObject.GetComponentsInChildren<Camera>(true))
                {
                    camera.clearFlags = CameraClearFlags.Skybox;
                    camera.backgroundColor = CameraFallbackColor;
                    EditorUtility.SetDirty(camera);
                }
            }

            EditorUtility.SetDirty(setup);
            EditorUtility.SetDirty(runtimeRoot);
            EditorUtility.SetDirty(skyboxMaterial);
        }

        static Material EnsureSkyboxMaterialAsset()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                Debug.LogError("Could not find Skybox/Procedural shader while installing Quest Link environment.");
                return null;
            }

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "QuestLinkSkybox"
                };
                AssetDatabase.CreateAsset(material, SkyboxMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetFloat("_SunDisk", 0f);
            material.SetFloat("_AtmosphereThickness", 0.12f);
            material.SetColor("_SkyTint", SkyTint);
            material.SetColor("_GroundColor", SkyGroundColor);
            material.SetFloat("_Exposure", 0.88f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return material;
        }

        static GameObject FindOrCreateRuntimeRoot(Scene scene)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == RuntimeRootName)
                {
                    return rootObject;
                }
            }

            var runtimeRoot = new GameObject(RuntimeRootName);
            SceneManager.MoveGameObjectToScene(runtimeRoot, scene);
            runtimeRoot.transform.position = Vector3.zero;
            runtimeRoot.transform.rotation = Quaternion.identity;
            return runtimeRoot;
        }
    }
}
