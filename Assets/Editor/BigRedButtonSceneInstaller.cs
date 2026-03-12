using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheBigRedButtonInstitute.Editor
{
    public static class BigRedButtonSceneInstaller
    {
        const string ModelPath = "Assets/Models/BigRedButton.glb";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ButtonName = "Big Red Button";
        const string ClipName = "pressed";
        const string SessionKey = "TheBigRedButtonInstitute.ImportedButtonInstalled.v6";
        static readonly Vector3 ButtonPosition = new(0f, 0.9f, 1.4f);

        [MenuItem("Tools/Big Red Button/Install Imported Button")]
        public static void InstallFromMenu()
        {
            SessionState.EraseBool(SessionKey);
            TryInstall();
        }

        static void TryInstall()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefabAsset == null)
            {
                EditorApplication.delayCall += TryInstall;
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemoveRuntimeLoader(scene);

            var instance = PrefabUtility.InstantiatePrefab(prefabAsset, scene) as GameObject;
            if (instance == null)
            {
                Debug.LogError($"Failed to instantiate imported button prefab from {ModelPath}");
                return;
            }

            instance.name = ButtonName;
            var transform = instance.transform;
            transform.position = ButtonPosition;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            ConfigureAnimationTest(instance);
            NormalizeButtonScale(instance);
            PositionCamera(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(SessionKey, true);
            Debug.Log("Installed imported Big Red Button into SampleScene.");
        }

        public static void ConfigureAnimationTest(GameObject button)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(asset => asset.name == ClipName);

            clip ??= AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault();

            var tester = button.GetComponent<BigRedButtonAnimationTester>();
            if (tester == null)
            {
                tester = button.AddComponent<BigRedButtonAnimationTester>();
            }

            var legacyAnimation = button.GetComponentInChildren<Animation>(true);
            if (legacyAnimation == null)
            {
                legacyAnimation = button.GetComponent<Animation>();
            }

            if (legacyAnimation != null)
            {
                tester.Configure(legacyAnimation, clip);
                tester.ConfigurePlayback(false, false, 0f, 0f, KeyCode.Space);
                EditorUtility.SetDirty(tester);
                return;
            }

            var animator = button.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                animator = button.GetComponent<Animator>();
            }

            if (animator == null)
            {
                animator = button.AddComponent<Animator>();
            }

            tester.Configure(animator, clip);
            tester.ConfigurePlayback(false, false, 0f, 0f, KeyCode.Space);
            EditorUtility.SetDirty(tester);
        }

        public static void NormalizeButtonScale(GameObject button, float targetHeight = 0.36f)
        {
            if (button == null)
            {
                return;
            }

            var renderers = button.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            if (bounds.size.y <= 0.0001f)
            {
                return;
            }

            var scaleFactor = targetHeight / bounds.size.y;
            button.transform.localScale *= scaleFactor;
        }

        static void RemoveRuntimeLoader(Scene scene)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name != ButtonName)
                {
                    continue;
                }

                Object.DestroyImmediate(rootObject);
                break;
            }
        }

        static void PositionCamera(Scene scene)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.GetComponent<OVRCameraRig>() != null)
                {
                    return;
                }
            }

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name != "Main Camera")
                {
                    continue;
                }

                var transform = rootObject.transform;
                transform.position = new Vector3(0f, 1.6f, -2.5f);
                transform.rotation = Quaternion.identity;
                break;
            }
        }
    }
}
