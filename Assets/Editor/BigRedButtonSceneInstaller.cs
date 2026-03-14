using System;
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
        const string BlinkMaterialPath = "Assets/Settings/BigRedButtonBlinkCap.mat";
        const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        const string SessionKey = "TheBigRedButtonInstitute.ImportedButtonInstalled.v6";
        static readonly Vector3 ButtonPosition = new(0f, 0.9f, 1.4f);
        static readonly Vector3 TriggerSurfaceStableLocalPosition = new(-0.000021076481f, 0.017285282f, 0.0044704f);
        static readonly Vector3 TriggerSurfaceStableLocalEuler = new(328.31277f, 180.4872f, 359.38284f);
        static readonly Vector3 TriggerSurfaceStableLocalSize = new(0.006696218f, 0.0012468889f, 0.006696218f);
        static readonly Vector3 TriggerColliderManualLocalOffset = new(0f, 0.00158f, -0.0018f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

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
            ConfigureBlinkController(instance);
            ConfigureManualPressController(instance);
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
                legacyAnimation.playAutomatically = false;
                legacyAnimation.wrapMode = WrapMode.Once;
                legacyAnimation.Stop();
                EditorUtility.SetDirty(legacyAnimation);
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

        public static void ConfigureBlinkController(GameObject button)
        {
            if (button == null)
            {
                return;
            }

            var controller = button.GetComponent<BigRedButtonBlinkController>();
            if (controller == null)
            {
                controller = button.AddComponent<BigRedButtonBlinkController>();
            }

            var targetRenderer = FindCapRenderer(button);
            var blinkMaterial = EnsureBlinkMaterial(targetRenderer != null ? targetRenderer.sharedMaterial : null);
            if (targetRenderer != null && blinkMaterial != null)
            {
                targetRenderer.sharedMaterial = blinkMaterial;
                EditorUtility.SetDirty(targetRenderer);
            }

            controller.ConfigureReferences(targetRenderer, targetRenderer != null ? targetRenderer.transform : null, null);
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("idleTint").colorValue = new Color(0.82f, 0.22f, 0.22f, 1f);
            serializedController.FindProperty("blinkTint").colorValue = new Color(1f, 0.72f, 0.72f, 1f);
            serializedController.FindProperty("idleEmission").colorValue = Color.black;
            serializedController.FindProperty("blinkEmission").colorValue = new Color(4f, 0.45f, 0.45f, 1f);
            serializedController.FindProperty("pulseDuration").floatValue = 0.32f;
            serializedController.FindProperty("blinkLightIntensity").floatValue = 1.35f;
            serializedController.FindProperty("pulseLightRange").floatValue = 0.8f;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        public static void ConfigureManualPressController(GameObject button, VR.QuestVrInputManager inputManager = null)
        {
            if (button == null)
            {
                return;
            }

            var controller = button.GetComponent<BigRedButtonManualPressController>();
            if (controller == null)
            {
                controller = button.AddComponent<BigRedButtonManualPressController>();
            }

            var legacyPressZone = button.GetComponent<SphereCollider>();
            if (legacyPressZone != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyPressZone);
            }

            var legacyDebugVisual = button.GetComponent<BigRedButtonColliderDebugVisual>();
            if (legacyDebugVisual != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyDebugVisual);
            }

            var legacyPressSurface = button.transform.Find("Button Press Surface");
            if (legacyPressSurface != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyPressSurface.gameObject);
            }

            var pressTriggerRenderer = FindCapRenderer(button);
            var passiveRenderer = FindBaseRenderer(button, pressTriggerRenderer);

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("targetRenderer").objectReferenceValue = passiveRenderer;
            serializedController.FindProperty("pressTriggerRenderer").objectReferenceValue = pressTriggerRenderer;
            serializedController.FindProperty("inputManager").objectReferenceValue = inputManager;
            serializedController.FindProperty("pressCooldownSeconds").floatValue = 0.18f;
            serializedController.FindProperty("startupContactSuppressionSeconds").floatValue = 0.35f;
            serializedController.FindProperty("interactorRefreshIntervalSeconds").floatValue = 0.2f;
            serializedController.FindProperty("usePressMeshCollider").boolValue = true;
            serializedController.FindProperty("preferConvexPressMeshCollider").boolValue = true;
            serializedController.FindProperty("pressTriggerMeshInflation").floatValue = 0f;
            serializedController.FindProperty("minimumPressPenetration").floatValue = 0.0015f;
            serializedController.FindProperty("pressMeshContactTolerance").floatValue = 0.001f;
            serializedController.FindProperty("pressTriggerSurfaceContactTolerance").floatValue = 0.0004f;
            serializedController.FindProperty("triggerSurfaceAlignmentMode").boolValue = false;
            serializedController.FindProperty("useTriggerSurfacePoseOverride").boolValue = true;
            serializedController.FindProperty("triggerSurfacePoseOverrideIsOffset").boolValue = false;
            serializedController.FindProperty("triggerSurfaceLocalPositionOverride").vector3Value = TriggerSurfaceStableLocalPosition;
            serializedController.FindProperty("triggerSurfaceLocalEulerOverride").vector3Value = TriggerSurfaceStableLocalEuler;
            serializedController.FindProperty("useTriggerSurfaceSizeOverride").boolValue = true;
            serializedController.FindProperty("triggerSurfaceLocalSizeOverride").vector3Value = TriggerSurfaceStableLocalSize;
            serializedController.FindProperty("triggerSurfaceDiameterScale").floatValue = 1f;
            serializedController.FindProperty("enableTriggerSurfacePressFallback").boolValue = false;
            serializedController.FindProperty("alignTriggerColliderToSurface").boolValue = true;
            serializedController.FindProperty("triggerColliderDerivedLocalOffset").vector3Value = Vector3.zero;
            serializedController.FindProperty("triggerColliderManualLocalOffset").vector3Value = TriggerColliderManualLocalOffset;
            serializedController.FindProperty("logPressCollisionDiagnostics").boolValue = true;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            controller.ConfigureReferences(passiveRenderer, pressTriggerRenderer, inputManager);
            EditorUtility.SetDirty(controller);
        }

        static Material EnsureBlinkMaterial(Material sourceMaterial)
        {
            var blinkMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlinkMaterialPath);
            if (blinkMaterial == null)
            {
                var shader = Shader.Find(UrpLitShaderName);
                if (shader == null)
                {
                    Debug.LogError($"Could not find shader '{UrpLitShaderName}' while configuring blink material.");
                    return sourceMaterial;
                }

                blinkMaterial = new Material(shader)
                {
                    name = "BigRedButtonBlinkCap"
                };
                AssetDatabase.CreateAsset(blinkMaterial, BlinkMaterialPath);
            }

            ConfigureBlinkMaterial(blinkMaterial, sourceMaterial);
            EditorUtility.SetDirty(blinkMaterial);
            return blinkMaterial;
        }

        static void ConfigureBlinkMaterial(Material blinkMaterial, Material sourceMaterial)
        {
            if (blinkMaterial == null)
            {
                return;
            }

            Texture sourceTexture = null;
            if (sourceMaterial != null)
            {
                sourceTexture = GetFirstTexture(sourceMaterial, BaseMapId, MainTexId);
                if (sourceTexture != null && blinkMaterial.HasProperty(BaseMapId))
                {
                    var sourceTextureProperty = sourceMaterial.HasProperty(BaseMapId) ? BaseMapId : MainTexId;
                    blinkMaterial.SetTexture(BaseMapId, sourceTexture);
                    if (sourceMaterial.HasProperty(sourceTextureProperty))
                    {
                        blinkMaterial.SetTextureScale(BaseMapId, sourceMaterial.GetTextureScale(sourceTextureProperty));
                        blinkMaterial.SetTextureOffset(BaseMapId, sourceMaterial.GetTextureOffset(sourceTextureProperty));
                    }
                }
            }

            sourceTexture ??= AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Texture2D>()
                .FirstOrDefault(texture => texture != null && !texture.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));

            if (sourceTexture != null && blinkMaterial.HasProperty(BaseMapId))
            {
                blinkMaterial.SetTexture(BaseMapId, sourceTexture);
            }

            if (blinkMaterial.HasProperty(BaseColorId))
            {
                blinkMaterial.SetColor(BaseColorId, Color.white);
            }

            if (blinkMaterial.HasProperty(SmoothnessId))
            {
                blinkMaterial.SetFloat(SmoothnessId, 0.48f);
            }

            if (blinkMaterial.HasProperty(MetallicId))
            {
                blinkMaterial.SetFloat(MetallicId, 0.08f);
            }

            if (blinkMaterial.HasProperty(EmissionColorId))
            {
                blinkMaterial.EnableKeyword("_EMISSION");
                blinkMaterial.SetColor(EmissionColorId, Color.black);
            }
        }

        static Texture GetFirstTexture(Material material, params int[] propertyIds)
        {
            if (material == null)
            {
                return null;
            }

            for (var i = 0; i < propertyIds.Length; i++)
            {
                if (!material.HasProperty(propertyIds[i]))
                {
                    continue;
                }

                var texture = material.GetTexture(propertyIds[i]);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        static Renderer FindCapRenderer(GameObject button)
        {
            if (button == null)
            {
                return null;
            }

            var namedRenderer = FindRendererOnNamedChild(button.transform, "button");
            if (namedRenderer != null)
            {
                return namedRenderer;
            }

            return FindNamedRenderer(button, "button", preferSkinnedRenderer: true) ??
                FindFirstRenderer(button, preferSkinnedRenderer: true);
        }

        static Renderer FindBaseRenderer(GameObject button, Renderer excludedRenderer)
        {
            if (button == null)
            {
                return null;
            }

            var namedRenderer = FindRendererOnNamedChild(button.transform, "stand1") ??
                FindRendererOnNamedChild(button.transform, "stand") ??
                FindRendererOnNamedChild(button.transform, "base");
            if (namedRenderer != null && namedRenderer != excludedRenderer)
            {
                return namedRenderer;
            }

            return FindNamedRenderer(button, "stand", preferSkinnedRenderer: false, excludedRenderer) ??
                FindNamedRenderer(button, "base", preferSkinnedRenderer: false, excludedRenderer) ??
                FindFirstRenderer(button, preferSkinnedRenderer: false, excludedRenderer) ??
                FindFirstRenderer(button, preferSkinnedRenderer: true, excludedRenderer);
        }

        static Renderer FindNamedRenderer(
            GameObject root,
            string token,
            bool preferSkinnedRenderer,
            Renderer excludedRenderer = null)
        {
            Renderer fallback = null;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var candidate = renderers[i];
                if (!IsRendererCandidate(candidate, excludedRenderer))
                {
                    continue;
                }

                if (fallback == null && IsRendererTypeMatch(candidate, preferSkinnedRenderer))
                {
                    fallback = candidate;
                }

                var objectName = candidate.gameObject.name;
                if (string.Equals(objectName, token, StringComparison.OrdinalIgnoreCase) ||
                    objectName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (IsRendererTypeMatch(candidate, preferSkinnedRenderer))
                    {
                        return candidate;
                    }

                    fallback ??= candidate;
                }
            }

            return fallback;
        }

        static Renderer FindFirstRenderer(
            GameObject root,
            bool preferSkinnedRenderer,
            Renderer excludedRenderer = null)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var candidate = renderers[i];
                if (IsRendererCandidate(candidate, excludedRenderer) && IsRendererTypeMatch(candidate, preferSkinnedRenderer))
                {
                    return candidate;
                }
            }

            return null;
        }

        static bool IsRendererCandidate(Renderer renderer, Renderer excludedRenderer)
        {
            return renderer != null &&
                renderer != excludedRenderer &&
                !IsDebugVisualRenderer(renderer);
        }

        static bool IsRendererTypeMatch(Renderer renderer, bool preferSkinnedRenderer)
        {
            var isSkinnedRenderer = renderer is SkinnedMeshRenderer;
            return preferSkinnedRenderer ? isSkinnedRenderer : !isSkinnedRenderer;
        }

        static bool IsDebugVisualRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            var debugVisual = renderer.GetComponentInParent<BigRedButtonColliderDebugVisual>();
            return debugVisual != null && debugVisual.gameObject != renderer.gameObject;
        }

        static Renderer FindRendererOnNamedChild(Transform root, string childName)
        {
            var childTransform = FindDescendantByName(root, childName);
            return childTransform != null ? childTransform.GetComponent<Renderer>() : null;
        }

        static Transform FindDescendantByName(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (var childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                var child = root.GetChild(childIndex);
                if (child == null)
                {
                    continue;
                }

                if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                var descendant = FindDescendantByName(child, childName);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        public static void NormalizeButtonScale(GameObject button, float targetHeight = 0.36f)
        {
            if (button == null)
            {
                return;
            }

            var renderers = button.GetComponentsInChildren<Renderer>(true);
            Renderer rootRenderer = null;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.GetComponentInParent<BigRedButtonColliderDebugVisual>() != null)
                {
                    continue;
                }

                rootRenderer = renderer;
                break;
            }

            if (rootRenderer == null)
            {
                return;
            }

            var bounds = rootRenderer.bounds;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer == rootRenderer || renderer.GetComponentInParent<BigRedButtonColliderDebugVisual>() != null)
                {
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
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

                UnityEngine.Object.DestroyImmediate(rootObject);
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
