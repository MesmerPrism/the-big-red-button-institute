using TMPro;
using TheBigRedButtonInstitute.Biofeedback;
using TheBigRedButtonInstitute.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheBigRedButtonInstitute.Editor
{
    public static class BigRedButtonWorldPressCounterAuthoring
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string CounterRootName = "Button Press Counter";
        const string CanvasName = "CounterCanvas";
        const string TextName = "CountText";
        const string CounterFontMaterialPath = "Assets/Settings/BigRedButtonCounterText.mat";
        const string TmpOverlayShaderName = "TextMeshPro/Distance Field Overlay";
        static readonly Vector3 CounterLocalPosition = new(0f, 0.0274f, -0.009f);
        static readonly int FaceDilateId = Shader.PropertyToID("_FaceDilate");
        static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int OutlineSoftnessId = Shader.PropertyToID("_OutlineSoftness");
        static readonly int WeightBoldId = Shader.PropertyToID("_WeightBold");
        static readonly int ZTestModeId = Shader.PropertyToID("_ZTestMode");

        [MenuItem("Tools/Big Red Button/Author World Press Counter")]
        public static void AuthorFromMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            AuthorIntoOpenScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static BigRedButtonWorldPressCounter AuthorIntoOpenScene(
            Scene scene,
            QuestVrInputManager inputManager = null,
            Transform buttonTransform = null,
            Camera targetCamera = null,
            PolarH10RuntimeManager polarRuntimeManager = null)
        {
            if (!scene.IsValid())
            {
                Debug.LogError("SampleScene is not valid for world press counter authoring.");
                return null;
            }

            inputManager ??= Object.FindFirstObjectByType<QuestVrInputManager>(FindObjectsInactive.Include);
            buttonTransform ??= ResolveButtonTransform();
            targetCamera ??= Camera.main ?? Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);

            if (inputManager == null || buttonTransform == null)
            {
                Debug.LogError("Could not find QuestVrInputManager or button transform while authoring the world press counter.");
                return null;
            }

            var counterRoot = FindOrCreateChild(buttonTransform, CounterRootName);
            counterRoot.localPosition = CounterLocalPosition;
            counterRoot.localRotation = Quaternion.identity;
            counterRoot.localScale = Vector3.one;

            var canvasRect = EnsureCanvas(counterRoot);
            var displayText = EnsureDisplayText(canvasRect);

            ConfigureCanvas(canvasRect);
            ConfigureDisplayText(displayText);
            return ConfigureCounter(counterRoot.gameObject, inputManager, displayText, targetCamera, polarRuntimeManager);
        }

        static Transform ResolveButtonTransform()
        {
            var buttonAnimationTester = Object.FindFirstObjectByType<BigRedButtonAnimationTester>(FindObjectsInactive.Include);
            if (buttonAnimationTester != null)
            {
                return buttonAnimationTester.transform;
            }

            var button = GameObject.Find("Big Red Button");
            return button != null ? button.transform : null;
        }

        static Transform FindOrCreateChild(Transform parent, string childName)
        {
            var existingChild = parent.Find(childName);
            if (existingChild != null)
            {
                return existingChild;
            }

            var childObject = new GameObject(childName);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        static RectTransform EnsureCanvas(Transform counterRoot)
        {
            var existingCanvas = counterRoot.Find(CanvasName) as RectTransform;
            if (existingCanvas != null)
            {
                if (existingCanvas.gameObject.GetComponent<Canvas>() == null)
                {
                    existingCanvas.gameObject.AddComponent<Canvas>();
                }

                if (existingCanvas.gameObject.GetComponent<CanvasScaler>() == null)
                {
                    existingCanvas.gameObject.AddComponent<CanvasScaler>();
                }

                return existingCanvas;
            }

            var canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(counterRoot, false);
            return canvasObject.GetComponent<RectTransform>();
        }

        static TextMeshProUGUI EnsureDisplayText(RectTransform canvasRect)
        {
            var existingText = canvasRect.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existingText != null)
            {
                if (existingText.gameObject.GetComponent<CanvasRenderer>() == null)
                {
                    existingText.gameObject.AddComponent<CanvasRenderer>();
                }

                existingText.gameObject.name = TextName;
                return existingText;
            }

            var textObject = new GameObject(TextName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasRect, false);
            return textObject.GetComponent<TextMeshProUGUI>();
        }

        static void ConfigureCanvas(RectTransform canvasRect)
        {
            canvasRect.gameObject.name = CanvasName;
            canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.anchoredPosition = Vector2.zero;
            canvasRect.sizeDelta = new Vector2(720f, 280f);
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.0001f;

            var canvas = canvasRect.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null;
            canvas.pixelPerfect = false;
            canvas.planeDistance = 1f;
            canvas.overrideSorting = false;
            canvas.sortingOrder = 0;
            canvas.additionalShaderChannels =
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.Normal |
                AdditionalCanvasShaderChannels.Tangent;

            var scaler = canvasRect.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 8f;
        }

        static void ConfigureDisplayText(TextMeshProUGUI displayText)
        {
            var textRect = displayText.rectTransform;
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(680f, 240f);
            textRect.localPosition = Vector3.zero;
            textRect.localRotation = Quaternion.identity;
            textRect.localScale = Vector3.one;

            displayText.raycastTarget = false;
            displayText.maskable = false;
            displayText.richText = false;
            displayText.textWrappingMode = TextWrappingModes.NoWrap;
            displayText.overflowMode = TextOverflowModes.Overflow;
            displayText.alignment = TextAlignmentOptions.Center;
            displayText.fontSize = 210f;
            displayText.enableAutoSizing = false;
            displayText.fontStyle = FontStyles.Bold;
            displayText.extraPadding = true;
            displayText.color = new Color(0.82f, 0.22f, 0.22f, 1f);
            displayText.text = "0";
            displayText.margin = Vector4.zero;

            var fontAsset = ResolveDefaultFont();
            if (fontAsset != null)
            {
                displayText.font = fontAsset;
                displayText.fontSharedMaterial = EnsureCounterFontMaterial(fontAsset);
            }

            displayText.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
        }

        static BigRedButtonWorldPressCounter ConfigureCounter(
            GameObject counterObject,
            QuestVrInputManager inputManager,
            TMP_Text displayText,
            Camera targetCamera,
            PolarH10RuntimeManager polarRuntimeManager)
        {
            var counter = counterObject.GetComponent<BigRedButtonWorldPressCounter>() ?? counterObject.AddComponent<BigRedButtonWorldPressCounter>();
            counter.Configure(inputManager, displayText, targetCamera, polarRuntimeManager);

            var serializedObject = new SerializedObject(counter);
            serializedObject.FindProperty("inputManager").objectReferenceValue = inputManager;
            serializedObject.FindProperty("displayText").objectReferenceValue = displayText;
            serializedObject.FindProperty("targetCamera").objectReferenceValue = targetCamera;
            serializedObject.FindProperty("polarRuntimeManager").objectReferenceValue = polarRuntimeManager;
            serializedObject.FindProperty("faceCamera").boolValue = true;
            serializedObject.FindProperty("yawOnly").boolValue = true;
            serializedObject.FindProperty("blinkWhilePolarConnected").boolValue = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(counterObject);
            EditorUtility.SetDirty(counter);
            EditorUtility.SetDirty(displayText);
            return counter;
        }

        static TMP_FontAsset ResolveDefaultFont()
        {
            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        static Material EnsureCounterFontMaterial(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(CounterFontMaterialPath);
            if (material == null)
            {
                var sourceMaterial = fontAsset.material;
                var shader = Shader.Find(TmpOverlayShaderName);
                material = shader != null ? new Material(shader) : sourceMaterial != null ? new Material(sourceMaterial) : null;
                if (material == null)
                {
                    return sourceMaterial;
                }

                material.name = "BigRedButtonCounterText";
                AssetDatabase.CreateAsset(material, CounterFontMaterialPath);
            }

            ConfigureCounterFontMaterial(material, fontAsset);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void ConfigureCounterFontMaterial(Material material, TMP_FontAsset fontAsset)
        {
            if (material == null || fontAsset == null)
            {
                return;
            }

            var sourceMaterial = fontAsset.material;
            if (sourceMaterial != null && sourceMaterial.mainTexture != null)
            {
                material.mainTexture = sourceMaterial.mainTexture;
            }

            if (material.HasProperty(FaceDilateId))
            {
                material.SetFloat(FaceDilateId, 0.035f);
            }

            if (material.HasProperty(OutlineWidthId))
            {
                material.SetFloat(OutlineWidthId, 0f);
            }

            if (material.HasProperty(OutlineSoftnessId))
            {
                material.SetFloat(OutlineSoftnessId, 0f);
            }

            if (material.HasProperty(WeightBoldId))
            {
                material.SetFloat(WeightBoldId, 0.65f);
            }

            if (material.HasProperty(ZTestModeId))
            {
                material.SetFloat(ZTestModeId, (float)CompareFunction.Always);
            }
        }
    }
}
