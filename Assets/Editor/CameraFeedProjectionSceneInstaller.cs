using TheBigRedButtonInstitute.CameraProjection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheBigRedButtonInstitute.Editor
{
    public static class CameraFeedProjectionSceneInstaller
    {
        const string ScenePath = "Assets/Scenes/CameraFeedProjectionComparison.unity";
        const string OvrCameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string RuntimeRootName = "Camera Feed Projection Example";
        static readonly Vector3 RigPosition = new(0f, 1.65f, -1.6f);

        [MenuItem("Tools/Big Red Button/Install Camera Feed Projection Comparison Scene")]
        public static void InstallSceneFromMenu()
        {
            InstallScene();
        }

        public static void InstallSceneFromBatch()
        {
            InstallScene();
        }

        public static void InstallScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraRig = EnsureCameraRig(scene);
            Transform headTransform = ResolveHeadTransform(cameraRig);

            var runtimeRoot = new GameObject(RuntimeRootName);
            SceneManager.MoveGameObjectToScene(runtimeRoot, scene);

            QuestLinkEnvironmentInstaller.InstallIntoScene(scene, runtimeRoot);

            var driverObject = new GameObject("BRB Camera Feed Projection Driver");
            driverObject.transform.SetParent(runtimeRoot.transform, false);
            var driver = driverObject.AddComponent<BrbCameraFeedProjectionDriver>();
            driver.ConfigureLookOrigin(headTransform);
            driver.ConfigureBlurLayer(radiusTexels: 8f, sigma: 3.5f, textureWidth: 640, textureHeight: 480);
            EditorUtility.SetDirty(driver);

            var panelsRoot = new GameObject("Projection Layer Panels");
            panelsRoot.transform.SetParent(runtimeRoot.transform, false);

            CreatePanel(
                panelsRoot.transform,
                "Raw Camera Reprojection",
                headTransform,
                driver,
                BrbCameraFeedProjectionQuadRenderer.ProjectionLayerMode.RawFeed,
                new Vector3(-0.68f, 0.12f, 1.35f));
            CreatePanel(
                panelsRoot.transform,
                "Blurred Camera Layer",
                headTransform,
                driver,
                BrbCameraFeedProjectionQuadRenderer.ProjectionLayerMode.BlurredFeed,
                new Vector3(0f, 0.12f, 1.35f));
            CreatePanel(
                panelsRoot.transform,
                "Raw Blur Split Comparison",
                headTransform,
                driver,
                BrbCameraFeedProjectionQuadRenderer.ProjectionLayerMode.RawBlurSplit,
                new Vector3(0.68f, 0.12f, 1.35f));
            CreatePanel(
                panelsRoot.transform,
                "Blur Difference Check",
                headTransform,
                driver,
                BrbCameraFeedProjectionQuadRenderer.ProjectionLayerMode.BlurDifference,
                new Vector3(0f, -0.36f, 1.38f));

            ConfigureOvrManager(cameraRig);
            EnsurePassthroughCameraProjectConfig();
            EnsureSceneListedInBuildSettings();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Installed BRB camera-feed projection comparison scene at {ScenePath}.");
        }

        static OVRCameraRig EnsureCameraRig(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OvrCameraRigPrefabPath);
            OVRCameraRig cameraRig = null;
            if (prefab != null)
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                cameraRig = instance != null ? instance.GetComponent<OVRCameraRig>() : null;
            }

            if (cameraRig == null)
            {
                var fallback = new GameObject("Camera Rig Fallback");
                SceneManager.MoveGameObjectToScene(fallback, scene);
                fallback.transform.position = RigPosition;
                fallback.transform.rotation = Quaternion.identity;
                var cameraObject = new GameObject("CenterEyeAnchor");
                cameraObject.transform.SetParent(fallback.transform, false);
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<Camera>();
                return null;
            }

            cameraRig.transform.position = RigPosition;
            cameraRig.transform.rotation = Quaternion.identity;
            EditorUtility.SetDirty(cameraRig.gameObject);
            return cameraRig;
        }

        static Transform ResolveHeadTransform(OVRCameraRig cameraRig)
        {
            if (cameraRig != null && cameraRig.centerEyeAnchor != null)
            {
                return cameraRig.centerEyeAnchor;
            }

            var centerEye = GameObject.Find("CenterEyeAnchor");
            if (centerEye != null)
            {
                return centerEye.transform;
            }

            return Camera.main != null ? Camera.main.transform : null;
        }

        static void CreatePanel(
            Transform parent,
            string name,
            Transform headTransform,
            BrbCameraFeedProjectionDriver driver,
            BrbCameraFeedProjectionQuadRenderer.ProjectionLayerMode layerMode,
            Vector3 anchoredLocalPosition)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            var filter = panel.AddComponent<MeshFilter>();
            var renderer = panel.AddComponent<MeshRenderer>();
            var anchor = panel.AddComponent<BrbCameraAnchoredTransform>();
            var projectionRenderer = panel.AddComponent<BrbCameraFeedProjectionQuadRenderer>();
            anchor.Configure(headTransform, anchoredLocalPosition, Vector3.zero, enableSmoothing: false);
            projectionRenderer.Configure(
                driver,
                layerMode,
                BrbCameraFeedProjectionQuadRenderer.DisplaySurfaceMode.DiagnosticQuad,
                newQuadWidthMeters: 0.58f,
                newQuadHeightMeters: 0.42f,
                newPreviewEye: 0);

            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(anchor);
            EditorUtility.SetDirty(projectionRenderer);
            EditorUtility.SetDirty(panel);
        }

        static void ConfigureOvrManager(OVRCameraRig cameraRig)
        {
            if (cameraRig == null)
            {
                return;
            }

            var manager = cameraRig.GetComponent<OVRManager>() ?? cameraRig.gameObject.AddComponent<OVRManager>();
            var serializedObject = new SerializedObject(manager);
            TrySetBool(serializedObject, "requestPassthroughCameraAccessPermissionOnStartup", true);
            TrySetBool(serializedObject, "requestScenePermissionOnStartup", false);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        static void EnsurePassthroughCameraProjectConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<Object>("Assets/Oculus/OculusProjectConfig.asset");
            if (config == null)
            {
                return;
            }

            var serializedObject = new SerializedObject(config);
            TrySetBool(serializedObject, "isPassthroughCameraAccessEnabled", true);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        static void EnsureSceneListedInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    return;
                }
            }

            var nextScenes = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(nextScenes, 0);
            nextScenes[^1] = new EditorBuildSettingsScene(ScenePath, enabled: false);
            EditorBuildSettings.scenes = nextScenes;
        }

        static void TrySetBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Boolean)
            {
                property.boolValue = value;
            }
        }
    }
}
