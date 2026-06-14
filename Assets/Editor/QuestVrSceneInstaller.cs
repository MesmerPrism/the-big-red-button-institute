using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheBigRedButtonInstitute.Biofeedback;
using TheBigRedButtonInstitute.Diagnostics;
using TheBigRedButtonInstitute.Questionnaire;
using TheBigRedButtonInstitute.VR;

namespace TheBigRedButtonInstitute.Editor
{
    public static class QuestVrSceneInstaller
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ModelPath = "Assets/Models/BigRedButton.glb";
        const string OvrCameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string OvrControllerPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab";
        const string OvrHandPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";
        const string ButtonName = "Big Red Button";
        const string RuntimeRootName = "VR Runtime";
        const string HudName = "VR Overlay HUD";
        const string LegacyGeneratedCounterCanvasName = "Button Press Counter Canvas";
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
            EnsureOculusHandsAndControllersProjectConfig();
            var cameraRig = EnsureCameraRig(scene);
            ConfigureCameraRigTracking(cameraRig);
            EnsureTrackedVisuals(cameraRig);
            var button = EnsureButton(scene);
            var tester = button != null ? button.GetComponent<BigRedButtonAnimationTester>() : null;
            var blinkController = button != null ? button.GetComponent<BigRedButtonBlinkController>() : null;

            if (tester != null)
            {
                tester.ConfigurePlayback(false, false, 0f, 0f, KeyCode.Space);
                EditorUtility.SetDirty(tester);
            }

            var runtimeRoot = EnsureRuntimeRoot(scene);
            QuestLinkEnvironmentInstaller.InstallIntoScene(scene, runtimeRoot);
            RemoveLegacyGeneratedCounter(runtimeRoot);
            var hud = EnsureHud(runtimeRoot.transform);
            var inputManager = EnsureInputManager(runtimeRoot);
            ConfigureInputManagerPlacement(inputManager);
            BigRedButtonSceneInstaller.ConfigureManualPressController(button, inputManager);
            EnsurePressInteractors(cameraRig);
            var headTransform = cameraRig != null ? cameraRig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : null;

            if (headTransform == null)
            {
                Debug.LogError("Quest VR installer could not find a head/camera transform.");
                return;
            }

            var polarRuntimeManager = PolarH10SceneInstaller.InstallIntoScene(scene, runtimeRoot, headTransform);
            var polarHeartbeatButtonDriver = EnsurePolarHeartbeatButtonDriver(runtimeRoot);
            var diagnosticRuntime = EnsureDiagnosticComparisonRuntime(
                runtimeRoot,
                inputManager,
                polarRuntimeManager,
                polarHeartbeatButtonDriver);
            var questionnaireLauncher = EnsureQuestionnaireLauncher(runtimeRoot);

            hud.ApplyOverlayPresentationPreset();
            ConfigureHud(hud);
            hud.ConfigureReferences(inputManager, headTransform);
            hud.EnsureSetupInEditor();
            inputManager.ConfigureReferences(hud, headTransform, button != null ? button.transform : null, tester);
            inputManager.ConfigurePolarReferences(polarRuntimeManager, polarHeartbeatButtonDriver);
            inputManager.ConfigureDiagnosticReferences(diagnosticRuntime);
            inputManager.ConfigureQuestionnaireReferences(questionnaireLauncher);
            polarHeartbeatButtonDriver.ConfigureReferences(polarRuntimeManager, inputManager, blinkController);
            inputManager.CenterButtonInFrontOfHead();
            var targetCamera = headTransform.GetComponent<Camera>() ?? Camera.main;
            var pressCounter = BigRedButtonWorldPressCounterAuthoring.AuthorIntoOpenScene(
                scene,
                inputManager,
                button != null ? button.transform : null,
                targetCamera,
                polarRuntimeManager);
            RemoveRuntimeDebugVisuals(scene);

            EditorUtility.SetDirty(runtimeRoot);
            EditorUtility.SetDirty(hud.gameObject);
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(inputManager);
            EditorUtility.SetDirty(questionnaireLauncher);
            if (pressCounter != null)
            {
                EditorUtility.SetDirty(pressCounter);
            }
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

                UnityEngine.Object.DestroyImmediate(rootObject);
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
                    BigRedButtonSceneInstaller.ConfigureBlinkController(rootObject);
                    BigRedButtonSceneInstaller.ConfigureManualPressController(rootObject);
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
            BigRedButtonSceneInstaller.ConfigureBlinkController(instance);
            BigRedButtonSceneInstaller.ConfigureManualPressController(instance);
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

        static void RemoveLegacyGeneratedCounter(GameObject runtimeRoot)
        {
            if (runtimeRoot == null)
            {
                return;
            }

            var legacyCanvas = runtimeRoot.transform.Find(LegacyGeneratedCounterCanvasName);
            if (legacyCanvas != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyCanvas.gameObject);
            }

            var behaviours = runtimeRoot.GetComponents<MonoBehaviour>();
            for (var index = behaviours.Length - 1; index >= 0; index--)
            {
                var behaviour = behaviours[index];
                if (behaviour == null)
                {
                    continue;
                }

                if (behaviour.GetType().Name == "QuestVrButtonPressCounterCanvas")
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(runtimeRoot);
        }

        static void RemoveRuntimeDebugVisuals(Scene scene)
        {
            if (!scene.IsValid())
            {
                return;
            }

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                RemoveRuntimeDebugVisuals(rootObject);
            }
        }

        static void RemoveRuntimeDebugVisuals(GameObject rootObject)
        {
            if (rootObject == null)
            {
                return;
            }

            var colliderDebugVisuals = rootObject.GetComponentsInChildren<BigRedButtonColliderDebugVisual>(true);
            for (var index = colliderDebugVisuals.Length - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(colliderDebugVisuals[index]);
            }

            var rendererDebugVisuals = rootObject.GetComponentsInChildren<BigRedButtonRendererMeshDebug>(true);
            for (var index = rendererDebugVisuals.Length - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(rendererDebugVisuals[index]);
            }

            var capShellInspectors = rootObject.GetComponentsInChildren<BigRedButtonCapRuntimeShellInspector>(true);
            for (var index = capShellInspectors.Length - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(capShellInspectors[index]);
            }

            RemoveRuntimeDebugVisualChildren(rootObject.transform);
        }

        static void RemoveRuntimeDebugVisualChildren(Transform root)
        {
            if (root == null)
            {
                return;
            }

            for (var childIndex = root.childCount - 1; childIndex >= 0; childIndex--)
            {
                var child = root.GetChild(childIndex);
                if (child == null)
                {
                    continue;
                }

                RemoveRuntimeDebugVisualChildren(child);
                if (string.Equals(child.name, "Collider Debug Visual", StringComparison.Ordinal) ||
                    string.Equals(child.name, "Renderer Mesh Debug", StringComparison.Ordinal) ||
                    string.Equals(child.name, "Cap Runtime Shell Inspector", StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        static void ConfigureInputManagerPlacement(QuestVrInputManager inputManager)
        {
            if (inputManager == null)
            {
                return;
            }

            var serializedInputManager = new SerializedObject(inputManager);
            serializedInputManager.FindProperty("placeButtonOnStartup").boolValue = true;
            serializedInputManager.FindProperty("keepButtonInFrontOfHead").boolValue = true;
            serializedInputManager.FindProperty("enableSimultaneousHandsAndControllers").boolValue = true;
            serializedInputManager.FindProperty("startupPlacementDelay").floatValue = 0.2f;
            serializedInputManager.FindProperty("buttonDistanceFromHead").floatValue = 0.48f;
            serializedInputManager.FindProperty("buttonVerticalOffset").floatValue = -0.32f;
            serializedInputManager.FindProperty("minimumButtonHeight").floatValue = 0.54f;
            serializedInputManager.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(inputManager);
        }

        static void ConfigureHud(QuestVrOverlayHud hud)
        {
            if (hud == null)
            {
                return;
            }

            var serializedHud = new SerializedObject(hud);
            serializedHud.FindProperty("visible").boolValue = false;
            serializedHud.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);
        }

        static void ConfigureCameraRigTracking(OVRCameraRig cameraRig)
        {
            if (cameraRig == null)
            {
                return;
            }

            var manager = cameraRig.GetComponent<OVRManager>();
            if (manager == null)
            {
                return;
            }

            var serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("launchSimultaneousHandsControllersOnStartup").boolValue = true;
            serializedManager.FindProperty("controllerDrivenHandPosesType").enumValueIndex = (int)OVRManager.ControllerDrivenHandPosesType.ConformingToController;
            serializedManager.FindProperty("SimultaneousHandsAndControllersEnabled").boolValue = true;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        static void EnsureOculusHandsAndControllersProjectConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Oculus/OculusProjectConfig.asset");
            if (config == null)
            {
                Debug.LogWarning("OculusProjectConfig.asset is missing; cannot enforce Controllers And Hands profile.");
                return;
            }

            var serializedConfig = new SerializedObject(config);
            var handTrackingSupport = serializedConfig.FindProperty("handTrackingSupport");
            if (handTrackingSupport != null)
            {
                handTrackingSupport.enumValueIndex = 1; // OVRProjectConfig.HandTrackingSupport.ControllersAndHands
            }

            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        static void EnsureTrackedVisuals(OVRCameraRig cameraRig)
        {
            if (cameraRig == null)
            {
                return;
            }

            var leftControllerAnchor = ResolveAnchor(cameraRig.leftControllerAnchor, cameraRig.transform, "LeftControllerAnchor");
            var rightControllerAnchor = ResolveAnchor(cameraRig.rightControllerAnchor, cameraRig.transform, "RightControllerAnchor");
            var leftHandAnchor = ResolveAnchor(cameraRig.leftHandAnchor, cameraRig.transform, "LeftHandAnchor");
            var rightHandAnchor = ResolveAnchor(cameraRig.rightHandAnchor, cameraRig.transform, "RightHandAnchor");
            var leftControllerInHandAnchor = ResolveAnchor(cameraRig.leftControllerInHandAnchor, cameraRig.transform, "LeftControllerInHandAnchor");
            var rightControllerInHandAnchor = ResolveAnchor(cameraRig.rightControllerInHandAnchor, cameraRig.transform, "RightControllerInHandAnchor");
            var leftHandOnControllerAnchor = ResolveAnchor(cameraRig.leftHandOnControllerAnchor, cameraRig.transform, "LeftHandOnControllerAnchor");
            var rightHandOnControllerAnchor = ResolveAnchor(cameraRig.rightHandOnControllerAnchor, cameraRig.transform, "RightHandOnControllerAnchor");

            RemoveDeprecatedTrackedChildren(leftControllerAnchor, typeof(OVRControllerHelper), "Left Controller Visual");
            RemoveDeprecatedTrackedChildren(rightControllerAnchor, typeof(OVRControllerHelper), "Right Controller Visual");
            RemoveDeprecatedTrackedChildren(leftHandAnchor, typeof(OVRHand), "Left Hand Visual");
            RemoveDeprecatedTrackedChildren(rightHandAnchor, typeof(OVRHand), "Right Hand Visual");

            var leftController = EnsureControllerVisual(
                leftControllerInHandAnchor,
                OVRInput.Controller.LTouch,
                "Left Controller Visual");
            var rightController = EnsureControllerVisual(
                rightControllerInHandAnchor,
                OVRInput.Controller.RTouch,
                "Right Controller Visual");
            var leftHand = EnsureHandVisual(
                leftHandOnControllerAnchor,
                OVRHand.Hand.HandLeft,
                "Left Hand Visual");
            var rightHand = EnsureHandVisual(
                rightHandOnControllerAnchor,
                OVRHand.Hand.HandRight,
                "Right Hand Visual");

            SetDirtyIfNotNull(leftController);
            SetDirtyIfNotNull(rightController);
            SetDirtyIfNotNull(leftHand);
            SetDirtyIfNotNull(rightHand);
        }

        static OVRControllerHelper EnsureControllerVisual(Transform anchor, OVRInput.Controller controllerType, string objectName)
        {
            if (anchor == null)
            {
                return null;
            }

            var helper = FindDirectChildComponent<OVRControllerHelper>(anchor);
            if (helper == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OvrControllerPrefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"Could not load controller prefab from {OvrControllerPrefabPath}");
                    return null;
                }

                var instance = PrefabUtility.InstantiatePrefab(prefab, anchor.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    Debug.LogError($"Failed to instantiate controller prefab from {OvrControllerPrefabPath}");
                    return null;
                }

                instance.transform.SetParent(anchor, false);
                helper = instance.GetComponent<OVRControllerHelper>();
            }

            if (helper == null)
            {
                return null;
            }

            var controllerObject = helper.gameObject;
            controllerObject.name = objectName;
            controllerObject.transform.SetParent(anchor, false);
            controllerObject.transform.localPosition = Vector3.zero;
            controllerObject.transform.localRotation = Quaternion.identity;
            controllerObject.transform.localScale = Vector3.one;
            helper.m_controller = controllerType;
            helper.m_showState = OVRInput.InputDeviceShowState.ControllerInHand;
            helper.showWhenHandsArePoweredByNaturalControllerPoses = false;
            EditorUtility.SetDirty(helper);
            EditorUtility.SetDirty(controllerObject);
            return helper;
        }

        static OVRHand EnsureHandVisual(Transform anchor, OVRHand.Hand handType, string objectName)
        {
            if (anchor == null)
            {
                return null;
            }

            var hand = FindDirectChildComponent<OVRHand>(anchor);
            if (hand == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OvrHandPrefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"Could not load hand prefab from {OvrHandPrefabPath}");
                    return null;
                }

                var instance = PrefabUtility.InstantiatePrefab(prefab, anchor.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    Debug.LogError($"Failed to instantiate hand prefab from {OvrHandPrefabPath}");
                    return null;
                }

                instance.transform.SetParent(anchor, false);
                hand = instance.GetComponent<OVRHand>();
            }

            if (hand == null)
            {
                return null;
            }

            var handObject = hand.gameObject;
            handObject.name = objectName;
            handObject.transform.SetParent(anchor, false);
            handObject.transform.localPosition = Vector3.zero;
            handObject.transform.localRotation = Quaternion.identity;
            handObject.transform.localScale = Vector3.one;

            var skeleton = handObject.GetComponent<OVRSkeleton>();
            var mesh = handObject.GetComponent<OVRMesh>();
            var skeletonVersion = OVRRuntimeSettings.Instance != null
                ? OVRRuntimeSettings.Instance.HandSkeletonVersion
                : OVRHandSkeletonVersion.OpenXR;
            var handSerializedObject = new SerializedObject(hand);
            handSerializedObject.FindProperty("HandType").intValue = (int)handType;
            handSerializedObject.FindProperty("m_showState").intValue = (int)OVRInput.InputDeviceShowState.ControllerInHand;
            handSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            if (skeleton != null)
            {
                var skeletonSerializedObject = new SerializedObject(skeleton);
                skeletonSerializedObject.FindProperty("_skeletonType").intValue = (int)handType.AsSkeletonType(skeletonVersion);
                skeletonSerializedObject.FindProperty("_enablePhysicsCapsules").boolValue = false;
                skeletonSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            if (mesh != null)
            {
                var meshSerializedObject = new SerializedObject(mesh);
                meshSerializedObject.FindProperty("_meshType").intValue = (int)handType.AsMeshType(skeletonVersion);
                meshSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(hand);
            if (skeleton != null)
            {
                EditorUtility.SetDirty(skeleton);
            }

            if (mesh != null)
            {
                EditorUtility.SetDirty(mesh);
            }

            EditorUtility.SetDirty(handObject);
            return hand;
        }

        static void EnsurePressInteractors(OVRCameraRig cameraRig)
        {
            if (cameraRig == null)
            {
                return;
            }

            var leftHandAnchor = ResolveAnchor(cameraRig.leftHandOnControllerAnchor, cameraRig.transform, "LeftHandOnControllerAnchor");
            var rightHandAnchor = ResolveAnchor(cameraRig.rightHandOnControllerAnchor, cameraRig.transform, "RightHandOnControllerAnchor");
            var leftControllerAnchor = ResolveAnchor(cameraRig.leftControllerInHandAnchor, cameraRig.transform, "LeftControllerInHandAnchor");
            var rightControllerAnchor = ResolveAnchor(cameraRig.rightControllerInHandAnchor, cameraRig.transform, "RightControllerInHandAnchor");
            var deprecatedLeftHandAnchor = ResolveAnchor(cameraRig.leftHandAnchor, cameraRig.transform, "LeftHandAnchor");
            var deprecatedRightHandAnchor = ResolveAnchor(cameraRig.rightHandAnchor, cameraRig.transform, "RightHandAnchor");
            var deprecatedLeftControllerAnchor = ResolveAnchor(cameraRig.leftControllerAnchor, cameraRig.transform, "LeftControllerAnchor");
            var deprecatedRightControllerAnchor = ResolveAnchor(cameraRig.rightControllerAnchor, cameraRig.transform, "RightControllerAnchor");

            RemovePressInteractor(leftHandAnchor, "Left Hand Press Interactor");
            RemovePressInteractor(rightHandAnchor, "Right Hand Press Interactor");
            RemovePressInteractor(leftControllerAnchor, "Left Controller Press Interactor");
            RemovePressInteractor(rightControllerAnchor, "Right Controller Press Interactor");
            RemovePressInteractor(leftHandAnchor, "Left Input Body Interactor");
            RemovePressInteractor(rightHandAnchor, "Right Input Body Interactor");
            RemovePressInteractor(leftControllerAnchor, "Left Input Body Interactor");
            RemovePressInteractor(rightControllerAnchor, "Right Input Body Interactor");
            RemovePressInteractor(leftHandAnchor, "Left Controller Body Interactor");
            RemovePressInteractor(rightHandAnchor, "Right Controller Body Interactor");
            RemovePressInteractor(leftControllerAnchor, "Left Controller Body Interactor");
            RemovePressInteractor(rightControllerAnchor, "Right Controller Body Interactor");
            RemovePressInteractor(leftControllerAnchor, "Left Controller Shell Interactor");
            RemovePressInteractor(rightControllerAnchor, "Right Controller Shell Interactor");
            RemovePressInteractor(deprecatedLeftHandAnchor, "Left Hand Press Interactor");
            RemovePressInteractor(deprecatedRightHandAnchor, "Right Hand Press Interactor");
            RemovePressInteractor(deprecatedLeftControllerAnchor, "Left Controller Press Interactor");
            RemovePressInteractor(deprecatedRightControllerAnchor, "Right Controller Press Interactor");
            RemovePressInteractor(deprecatedLeftHandAnchor, "Left Input Body Interactor");
            RemovePressInteractor(deprecatedRightHandAnchor, "Right Input Body Interactor");
            RemovePressInteractor(deprecatedLeftControllerAnchor, "Left Input Body Interactor");
            RemovePressInteractor(deprecatedRightControllerAnchor, "Right Input Body Interactor");
            RemovePressInteractor(deprecatedLeftControllerAnchor, "Left Controller Shell Interactor");
            RemovePressInteractor(deprecatedRightControllerAnchor, "Right Controller Shell Interactor");

            var leftHand = FindDirectChildComponent<OVRHand>(leftHandAnchor);
            var rightHand = FindDirectChildComponent<OVRHand>(rightHandAnchor);
            EnsureBodyPressInteractor(
                leftHandAnchor,
                "Left Hand Body Interactor",
                0f,
                leftHand != null ? leftHand.gameObject : null);
            EnsureBodyPressInteractor(
                rightHandAnchor,
                "Right Hand Body Interactor",
                0f,
                rightHand != null ? rightHand.gameObject : null);
            EnsureParentResolvedBodyPressInteractor(
                leftControllerAnchor,
                "Left Controller Shell Interactor",
                0f,
                "Left Controller Visual/MetaQuestTouchPlus_Left",
                "Left Controller Visual",
                "MetaQuestTouchPlus_Left",
                participatesInPresses: true);
            EnsureParentResolvedBodyPressInteractor(
                rightControllerAnchor,
                "Right Controller Shell Interactor",
                0f,
                "Right Controller Visual/MetaQuestTouchPlus_Right",
                "Right Controller Visual",
                "MetaQuestTouchPlus_Right",
                participatesInPresses: true);
        }

        static GameObject EnsureBodyPressInteractor(Transform anchor, string interactorName, float padding, params GameObject[] bodyRoots)
        {
            if (anchor == null)
            {
                return null;
            }

            var interactorTransform = anchor.Find(interactorName);
            GameObject interactorObject;
            if (interactorTransform == null)
            {
                interactorObject = new GameObject(interactorName);
                interactorObject.transform.SetParent(anchor, false);
            }
            else
            {
                interactorObject = interactorTransform.gameObject;
            }

            interactorObject.transform.localPosition = Vector3.zero;
            interactorObject.transform.localRotation = Quaternion.identity;
            interactorObject.transform.localScale = Vector3.one;

            var interactor = interactorObject.GetComponent<BigRedButtonPressInteractor>();
            if (interactor == null)
            {
                interactor = interactorObject.AddComponent<BigRedButtonPressInteractor>();
            }

            var renderers = CollectRenderers(bodyRoots);
            interactor.ConfigureBody(renderers, padding);
            interactor.SetTrackingValid(renderers.Length > 0);

            var serializedInteractor = new SerializedObject(interactor);
            serializedInteractor.FindProperty("generatedMeshCollidersOnly").boolValue = true;
            serializedInteractor.FindProperty("disableLegacyHandPhysicsCapsules").boolValue = true;
            serializedInteractor.FindProperty("enableRuntimeDebugVisuals").boolValue = false;
            serializedInteractor.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(interactor);
            EditorUtility.SetDirty(interactorObject);
            return interactorObject;
        }

        static GameObject EnsureParentResolvedBodyPressInteractor(
            Transform anchor,
            string interactorName,
            float padding,
            string rendererRootName,
            string controllerVisualName,
            string touchPlusModelRootName,
            bool participatesInPresses)
        {
            if (anchor == null)
            {
                return null;
            }

            var interactorTransform = anchor.Find(interactorName);
            GameObject interactorObject;
            if (interactorTransform == null)
            {
                interactorObject = new GameObject(interactorName);
                interactorObject.transform.SetParent(anchor, false);
            }
            else
            {
                interactorObject = interactorTransform.gameObject;
            }

            interactorObject.transform.localPosition = Vector3.zero;
            interactorObject.transform.localRotation = Quaternion.identity;
            interactorObject.transform.localScale = Vector3.one;

            var interactor = interactorObject.GetComponent<BigRedButtonPressInteractor>();
            if (interactor == null)
            {
                interactor = interactorObject.AddComponent<BigRedButtonPressInteractor>();
            }

            interactor.ConfigureBodyFromParent(rendererRootName, padding);
            interactor.SetTrackingValid(participatesInPresses);

            var touchPlusOverride = interactorObject.GetComponent<QuestVrTouchPlusControllerOnly>();
            if (touchPlusOverride == null)
            {
                touchPlusOverride = interactorObject.AddComponent<QuestVrTouchPlusControllerOnly>();
            }

            touchPlusOverride.Configure(controllerVisualName, touchPlusModelRootName);

            var serializedInteractor = new SerializedObject(interactor);
            serializedInteractor.FindProperty("generatedMeshCollidersOnly").boolValue = true;
            serializedInteractor.FindProperty("disableLegacyHandPhysicsCapsules").boolValue = true;
            serializedInteractor.FindProperty("enableRuntimeDebugVisuals").boolValue = false;
            serializedInteractor.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(interactor);
            EditorUtility.SetDirty(interactorObject);
            return interactorObject;
        }

        static Renderer[] CollectRenderers(params GameObject[] bodyRoots)
        {
            if (bodyRoots == null || bodyRoots.Length == 0)
            {
                return Array.Empty<Renderer>();
            }

            var renderers = new List<Renderer>();
            for (var i = 0; i < bodyRoots.Length; i++)
            {
                var bodyRoot = bodyRoots[i];
                if (bodyRoot == null)
                {
                    continue;
                }

                var bodyRenderers = bodyRoot.GetComponentsInChildren<Renderer>(true);
                for (var j = 0; j < bodyRenderers.Length; j++)
                {
                    var renderer = bodyRenderers[j];
                    if (renderer == null || renderers.Contains(renderer))
                    {
                        continue;
                    }

                    renderers.Add(renderer);
                }
            }

            return renderers.ToArray();
        }

        static void RemoveDeprecatedTrackedChildren(Transform anchor, Type componentType, string expectedName)
        {
            if (anchor == null)
            {
                return;
            }

            for (var index = anchor.childCount - 1; index >= 0; index--)
            {
                var child = anchor.GetChild(index);
                if (child == null)
                {
                    continue;
                }

                if (!string.Equals(child.name, expectedName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (child.GetComponent(componentType) == null)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        static void RemovePressInteractor(Transform anchor, string interactorName)
        {
            if (anchor == null)
            {
                return;
            }

            var interactor = anchor.Find(interactorName);
            if (interactor != null)
            {
                UnityEngine.Object.DestroyImmediate(interactor.gameObject);
            }
        }

        static void SetDirtyIfNotNull(UnityEngine.Object target)
        {
            if (target != null)
            {
                EditorUtility.SetDirty(target);
            }
        }

        static Transform ResolveAnchor(Transform directAnchor, Transform rigRoot, string anchorName)
        {
            if (directAnchor != null)
            {
                return directAnchor;
            }

            if (rigRoot == null)
            {
                return null;
            }

            var transforms = rigRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == anchorName)
                {
                    return transforms[i];
                }
            }

            return null;
        }

        static T FindDirectChildComponent<T>(Transform parent) where T : Component
        {
            if (parent == null)
            {
                return null;
            }

            var components = parent.GetComponentsInChildren<T>(true);
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i].transform.parent == parent)
                {
                    return components[i];
                }
            }

            return null;
        }

        static PolarHeartbeatButtonDriver EnsurePolarHeartbeatButtonDriver(GameObject runtimeRoot)
        {
            return runtimeRoot.GetComponent<PolarHeartbeatButtonDriver>() ?? runtimeRoot.AddComponent<PolarHeartbeatButtonDriver>();
        }

        static QuestQuestionnairePanelLauncher EnsureQuestionnaireLauncher(GameObject runtimeRoot)
        {
            return runtimeRoot.GetComponent<QuestQuestionnairePanelLauncher>() ??
                   runtimeRoot.AddComponent<QuestQuestionnairePanelLauncher>();
        }

        static BigRedButtonDiagnosticComparisonController EnsureDiagnosticComparisonRuntime(
            GameObject runtimeRoot,
            QuestVrInputManager inputManager,
            PolarH10RuntimeManager polarRuntimeManager,
            PolarHeartbeatButtonDriver polarHeartbeatButtonDriver)
        {
            var comparison = runtimeRoot.GetComponent<BigRedButtonDiagnosticComparisonController>() ??
                             runtimeRoot.AddComponent<BigRedButtonDiagnosticComparisonController>();
            var directOsc = runtimeRoot.GetComponent<BigRedButtonDirectOscDriveReceiver>() ??
                            runtimeRoot.AddComponent<BigRedButtonDirectOscDriveReceiver>();
            var directPolar = runtimeRoot.GetComponent<BigRedButtonDirectPolarDiagnosticReceiver>() ??
                              runtimeRoot.AddComponent<BigRedButtonDirectPolarDiagnosticReceiver>();
            var directLsl = runtimeRoot.GetComponent<BigRedButtonDirectLslDriveReceiver>() ??
                            runtimeRoot.AddComponent<BigRedButtonDirectLslDriveReceiver>();

            directPolar.ConfigureReferences(polarRuntimeManager, comparison);
            directOsc.ConfigureReferences(inputManager, comparison);
            directLsl.ConfigureReferences(inputManager, comparison);
            comparison.ConfigureReferences(
                inputManager,
                polarHeartbeatButtonDriver,
                directPolar,
                directOsc,
                directLsl);

            EditorUtility.SetDirty(directPolar);
            EditorUtility.SetDirty(directOsc);
            EditorUtility.SetDirty(directLsl);
            EditorUtility.SetDirty(comparison);
            return comparison;
        }
    }
}
