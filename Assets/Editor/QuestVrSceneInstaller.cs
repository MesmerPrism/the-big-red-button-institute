using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheBigRedButtonInstitute.VR;

namespace TheBigRedButtonInstitute.Editor
{
    public static class QuestVrSceneInstaller
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ModelPath = "Assets/Models/BigRedButton.glb";
        const string OvrCameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string ButtonName = "Big Red Button";
        const string RuntimeRootName = "VR Runtime";
        const string HudName = "VR Overlay HUD";
        const string SessionKey = "TheBigRedButtonInstitute.QuestVrInstalled.v2";
        static readonly Vector3 RigPosition = new(0f, 1.65f, -1.7f);

        [MenuItem("Tools/Big Red Button/Install Quest VR Runtime")]
        public static void InstallFromMenu()
        {
            SessionState.EraseBool(SessionKey);
            TryInstall();
        }

        public static void InstallIntoSampleScene()
        {
            SessionState.EraseBool(SessionKey);
            TryInstall();
        }

        static void TryInstall()
        {
            if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cameraRig = EnsureCameraRig(scene);
            var button = EnsureButton(scene);
            var tester = button != null ? button.GetComponent<BigRedButtonAnimationTester>() : null;

            if (tester != null)
            {
                tester.ConfigurePlayback(false, false, 0f, 0f, KeyCode.Space);
                EditorUtility.SetDirty(tester);
            }

            var runtimeRoot = EnsureRuntimeRoot(scene);
            var hud = EnsureHud(runtimeRoot.transform);
            var inputManager = EnsureInputManager(runtimeRoot);
            var headTransform = cameraRig != null ? cameraRig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : null;

            if (headTransform == null)
            {
                Debug.LogError("Quest VR installer could not find a head/camera transform.");
                return;
            }

            hud.ApplyAstralPresentationPreset();
            hud.ConfigureReferences(inputManager, headTransform);
            hud.EnsureSetupInEditor();
            inputManager.ConfigureReferences(hud, headTransform, button != null ? button.transform : null, tester);
            inputManager.CenterButtonInFrontOfHead();

            EditorUtility.SetDirty(runtimeRoot);
            EditorUtility.SetDirty(hud.gameObject);
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(inputManager);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(SessionKey, true);
            Debug.Log("Installed minimal Quest VR runtime into SampleScene.");
        }

        static OVRCameraRig EnsureCameraRig(Scene scene)
        {
            OVRCameraRig cameraRig = null;

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                cameraRig = rootObject.GetComponent<OVRCameraRig>();
                if (cameraRig != null)
                {
                    break;
                }
            }

            if (cameraRig == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OvrCameraRigPrefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"Could not load OVRCameraRig prefab from {OvrCameraRigPrefabPath}");
                    return null;
                }

                cameraRig = PrefabUtility.InstantiatePrefab(prefab, scene) as OVRCameraRig;
                if (cameraRig == null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                    cameraRig = instance != null ? instance.GetComponent<OVRCameraRig>() : null;
                }
            }

            if (cameraRig == null)
            {
                return null;
            }

            cameraRig.transform.position = RigPosition;
            cameraRig.transform.rotation = Quaternion.identity;
            RemovePlainMainCamera(scene, cameraRig.gameObject);
            EditorUtility.SetDirty(cameraRig.gameObject);
            return cameraRig;
        }

        static void RemovePlainMainCamera(Scene scene, GameObject keepObject)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject == keepObject || rootObject.name != "Main Camera")
                {
                    continue;
                }

                Object.DestroyImmediate(rootObject);
                break;
            }
        }

        static GameObject EnsureButton(Scene scene)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == ButtonName)
                {
                    BigRedButtonSceneInstaller.ConfigureAnimationTest(rootObject);
                    BigRedButtonSceneInstaller.NormalizeButtonScale(rootObject);
                    return rootObject;
                }
            }

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefabAsset == null)
            {
                Debug.LogError($"Could not load the imported button prefab from {ModelPath}");
                return null;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefabAsset, scene) as GameObject;
            if (instance == null)
            {
                Debug.LogError("Failed to instantiate the imported button prefab.");
                return null;
            }

            instance.name = ButtonName;
            instance.transform.position = new Vector3(0f, 1.2f, 0.4f);
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            BigRedButtonSceneInstaller.ConfigureAnimationTest(instance);
            BigRedButtonSceneInstaller.NormalizeButtonScale(instance);
            EditorUtility.SetDirty(instance);
            return instance;
        }

        static GameObject EnsureRuntimeRoot(Scene scene)
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

        static QuestVrOverlayHud EnsureHud(Transform runtimeRoot)
        {
            var hudTransform = runtimeRoot.Find(HudName);
            GameObject hudObject;
            if (hudTransform == null)
            {
                hudObject = new GameObject(HudName);
                hudObject.transform.SetParent(runtimeRoot, false);
            }
            else
            {
                hudObject = hudTransform.gameObject;
            }

            return hudObject.GetComponent<QuestVrOverlayHud>() ?? hudObject.AddComponent<QuestVrOverlayHud>();
        }

        static QuestVrInputManager EnsureInputManager(GameObject runtimeRoot)
        {
            return runtimeRoot.GetComponent<QuestVrInputManager>() ?? runtimeRoot.AddComponent<QuestVrInputManager>();
        }
    }
}
