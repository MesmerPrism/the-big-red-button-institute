using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheBigRedButtonInstitute.VR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class QuestVrOverlayHud : MonoBehaviour
    {
        const string CanvasName = "HudCanvas";
        const string PanelName = "Panel";
        const string BorderName = "Border";
        const string TextName = "DisplayText";
        const int HiddenCanvasLayer = 2;
        const int OverlayCanvasLayer = 5;

        static readonly FieldInfo DynamicResolutionField =
            typeof(OVROverlayCanvas).GetField("_dynamicResolution", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly Vector2 LegacyCanvasSize = new(1280f, 760f);
        static readonly Vector2 AstralCanvasSize = new(1800f, 1275f);
        static readonly Vector2 AstralTextPadding = new(64f, 56f);
        static readonly Color AstralBackgroundColor = new(0.02f, 0.03f, 0.04f, 0.14f);
        static readonly Color AstralTextColor = new(0.86f, 0.95f, 1f, 1f);
        static readonly Color AstralBorderColor = new(0.38f, 0.74f, 0.94f, 0.92f);

        [Header("References")]
        [SerializeField] QuestVrInputManager inputManager;
        [SerializeField] Transform headTransform;
        [SerializeField] Canvas worldCanvas;
        [SerializeField] RectTransform canvasRect;
        [SerializeField] Image backgroundPanel;
        [SerializeField] TextMeshProUGUI displayText;
        [SerializeField] OVROverlayCanvas overlayCanvas;
        [SerializeField] OVROverlayCanvas_TMPChanged textChangeNotifier;
        [SerializeField] RectTransform borderRoot;
        [SerializeField] Image borderTop;
        [SerializeField] Image borderBottom;
        [SerializeField] Image borderLeft;
        [SerializeField] Image borderRight;

        [Header("Presentation")]
        [SerializeField] bool visible = true;
        [SerializeField] bool useOverlayCanvas = true;
        [SerializeField] Color backgroundColor = new(0.025f, 0.03f, 0.035f, 0.98f);
        [SerializeField] Color textColor = new(0.93f, 0.97f, 1f, 1f);
        [SerializeField] Color borderColor = new(0.35f, 0.74f, 0.92f, 0.86f);
        [SerializeField] Vector2 canvasSize = new(1600f, 920f);
        [SerializeField] Vector2 textPadding = new(72f, 64f);
        [SerializeField, Range(0.0005f, 0.01f)] float worldScale = 0.0012f;
        [SerializeField, Range(20f, 72f)] float fontSize = 36f;
        [SerializeField, Range(0f, 24f)] float lineSpacing = 6f;
        [SerializeField, Range(1f, 16f)] float borderThickness = 4f;
        [SerializeField, Range(0f, 80f)] float borderInset = 18f;

        [Header("Follow")]
        [SerializeField, Min(0.4f)] float distanceFromHead = 1.12f;
        [SerializeField] float horizontalOffset = 0.34f;
        [SerializeField] float verticalOffset = 0.08f;
        [SerializeField] bool smoothFollow = true;
        [SerializeField, Range(0.01f, 0.4f)] float positionSmoothTime = 0.08f;
        [SerializeField, Range(1f, 30f)] float rotationLerpSpeed = 12f;
        [SerializeField, Range(0.05f, 1f)] float refreshIntervalSeconds = 0.1f;

        int _selectedCommandIndex;
        int _activePageIndex;
        float _nextRefreshAt;
        float _transientMessageClearAt;
        string _transientMessage;
        Vector3 _positionVelocity;
        Camera _mainCamera;

        public bool IsVisible => visible;
        public int SelectedCommandIndex => _selectedCommandIndex;
        public int ActivePageIndex => _activePageIndex;

        void Reset()
        {
            ApplyAstralPresentationPresetIfNeeded();
            ResolveReferences();
            EnsureVisualHierarchy();
            ApplyVisibility();
            RefreshImmediately();
        }

        void Awake()
        {
            ApplyAstralPresentationPresetIfNeeded();
            ResolveReferences();
            EnsureVisualHierarchy();
            ApplyVisibility();
            RefreshImmediately();
        }

        void OnEnable()
        {
            ApplyAstralPresentationPresetIfNeeded();
            ResolveReferences();
            EnsureVisualHierarchy();
            ApplyVisibility();
            RefreshImmediately();
        }

        void OnValidate()
        {
            distanceFromHead = Mathf.Max(0.4f, distanceFromHead);
            refreshIntervalSeconds = Mathf.Clamp(refreshIntervalSeconds, 0.05f, 1f);
            positionSmoothTime = Mathf.Clamp(positionSmoothTime, 0.01f, 0.4f);
            rotationLerpSpeed = Mathf.Clamp(rotationLerpSpeed, 1f, 30f);
            worldScale = Mathf.Clamp(worldScale, 0.0005f, 0.01f);
            fontSize = Mathf.Clamp(fontSize, 20f, 72f);
            lineSpacing = Mathf.Clamp(lineSpacing, 0f, 24f);
            borderThickness = Mathf.Clamp(borderThickness, 1f, 16f);
            borderInset = Mathf.Clamp(borderInset, 0f, 80f);
            ApplyAstralPresentationPresetIfNeeded();
        }

        void LateUpdate()
        {
            ResolveReferences();
            UpdatePose();
            ExpireTransientMessageIfNeeded();

            if (!visible || !Application.isPlaying)
            {
                return;
            }

            if (Time.unscaledTime >= _nextRefreshAt)
            {
                RefreshImmediately();
            }
        }

        public void ConfigureReferences(QuestVrInputManager manager, Transform head)
        {
            inputManager = manager;
            headTransform = head;
            ApplyAstralPresentationPresetIfNeeded();
            ResolveReferences();
            EnsureVisualHierarchy();
            ApplyVisibility();
            RefreshImmediately();
        }

        public void ApplyAstralPresentationPreset()
        {
            canvasSize = AstralCanvasSize;
            textPadding = AstralTextPadding;
            worldScale = 0.0012f;
            fontSize = 44f;
            lineSpacing = 2f;
            borderThickness = 6f;
            borderInset = 20f;
            distanceFromHead = 2.8f;
            horizontalOffset = 0f;
            verticalOffset = -0.02f;
            backgroundColor = AstralBackgroundColor;
            textColor = AstralTextColor;
            borderColor = AstralBorderColor;
        }

        public void EnsureSetupInEditor()
        {
            ApplyAstralPresentationPresetIfNeeded();
            ResolveReferences();
            EnsureVisualHierarchy();
            ApplyVisibility();
            RefreshImmediately();
        }

        public void SetVisible(bool isVisible)
        {
            visible = isVisible;
            ApplyVisibility();
            RefreshImmediately();
        }

        public void ToggleVisibility()
        {
            SetVisible(!visible);
        }

        public void SelectNextCommand()
        {
            SelectCommandOffset(1);
        }

        public void SelectPreviousCommand()
        {
            SelectCommandOffset(-1);
        }

        public QuestVrInputManager.TerminalCommand GetSelectedCommand()
        {
            var commands = inputManager != null ? inputManager.Commands : null;
            if (commands == null || commands.Count == 0)
            {
                return default;
            }

            ClampSelectedCommandIndex(commands.Count);
            return commands[_selectedCommandIndex];
        }

        public bool SelectNextPage()
        {
            return SelectPageOffset(1);
        }

        public bool SelectPreviousPage()
        {
            return SelectPageOffset(-1);
        }

        public void SetTransientMessage(string message, float duration = 2.5f)
        {
            _transientMessage = message;
            _transientMessageClearAt = Application.isPlaying ? Time.unscaledTime + Mathf.Max(0.25f, duration) : 0f;
            RefreshImmediately();
        }

        public void RefreshImmediately()
        {
            if (displayText == null)
            {
                return;
            }

            ClampSelectedPageIndex();
            displayText.text = inputManager != null
                ? inputManager.BuildHudText(_activePageIndex, _selectedCommandIndex, GetTransientMessage())
                : BuildFallbackText();
            displayText.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
            Canvas.ForceUpdateCanvases();

            _nextRefreshAt = Application.isPlaying ? Time.unscaledTime + refreshIntervalSeconds : 0f;
            MarkOverlayDirty();
        }

        void SelectCommandOffset(int offset)
        {
            var commands = inputManager != null ? inputManager.Commands : null;
            if (commands == null || commands.Count == 0)
            {
                return;
            }

            ClampSelectedCommandIndex(commands.Count);
            _selectedCommandIndex = (_selectedCommandIndex + offset + commands.Count) % commands.Count;
            RefreshImmediately();
        }

        bool SelectPageOffset(int offset)
        {
            var pageCount = inputManager != null ? inputManager.GetHudPageCount() : 0;
            if (pageCount <= 1)
            {
                _activePageIndex = 0;
                return false;
            }

            ClampSelectedPageIndex();
            _activePageIndex = (_activePageIndex + offset + pageCount) % pageCount;
            RefreshImmediately();
            return true;
        }

        void ClampSelectedCommandIndex(int count)
        {
            _selectedCommandIndex = count <= 0
                ? 0
                : Mathf.Clamp(_selectedCommandIndex, 0, count - 1);
        }

        void ClampSelectedPageIndex()
        {
            var pageCount = inputManager != null ? inputManager.GetHudPageCount() : 0;
            _activePageIndex = pageCount <= 0
                ? 0
                : Mathf.Clamp(_activePageIndex, 0, pageCount - 1);
        }

        string GetTransientMessage()
        {
            return string.IsNullOrWhiteSpace(_transientMessage) ? null : _transientMessage;
        }

        void ApplyAstralPresentationPresetIfNeeded()
        {
            if (!IsLegacyPresentation())
            {
                return;
            }

            ApplyAstralPresentationPreset();
        }

        bool IsLegacyPresentation()
        {
            return Approximately(canvasSize, LegacyCanvasSize)
                && Mathf.Abs(distanceFromHead - 1.1f) <= 0.08f
                && Mathf.Abs(horizontalOffset - 0.34f) <= 0.08f
                && Mathf.Abs(verticalOffset - 0.08f) <= 0.08f;
        }

        void ExpireTransientMessageIfNeeded()
        {
            if (!Application.isPlaying || string.IsNullOrWhiteSpace(_transientMessage))
            {
                return;
            }

            if (Time.unscaledTime < _transientMessageClearAt)
            {
                return;
            }

            _transientMessage = null;
            _transientMessageClearAt = 0f;
            RefreshImmediately();
        }

        void ResolveReferences()
        {
            if (inputManager == null)
            {
                inputManager = GetComponentInParent<QuestVrInputManager>();
            }

            if (headTransform == null)
            {
                var cameraRig = FindAnyObjectByType<OVRCameraRig>();
                if (cameraRig != null)
                {
                    headTransform = cameraRig.centerEyeAnchor;
                }
            }

            if (headTransform == null && Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }

            var resolvedCamera = ResolveMainCamera();
            if (!ReferenceEquals(_mainCamera, resolvedCamera))
            {
                _mainCamera = resolvedCamera;
                ApplyCameraCullingForOverlay();
            }
        }

        void EnsureVisualHierarchy()
        {
            if (worldCanvas == null)
            {
                var existingCanvas = transform.Find(CanvasName);
                if (existingCanvas != null)
                {
                    worldCanvas = existingCanvas.GetComponent<Canvas>();
                }
            }

            if (worldCanvas == null)
            {
                var canvasObject = new GameObject(
                    CanvasName,
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(transform, false);
                worldCanvas = canvasObject.GetComponent<Canvas>();
            }

            canvasRect = worldCanvas.transform as RectTransform;
            SetLayerRecursive(worldCanvas.gameObject, HiddenCanvasLayer);

            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.worldCamera = null;
            worldCanvas.pixelPerfect = false;
            worldCanvas.planeDistance = 1f;
            worldCanvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;

            var scaler = worldCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 1f;

            var raycaster = worldCanvas.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            overlayCanvas ??= worldCanvas.GetComponent<OVROverlayCanvas>();
            if (useOverlayCanvas && overlayCanvas == null)
            {
                overlayCanvas = worldCanvas.gameObject.AddComponent<OVROverlayCanvas>();
            }

            if (overlayCanvas != null)
            {
                overlayCanvas.rectTransform = canvasRect;
                overlayCanvas.maxTextureSize = 2048;
                overlayCanvas.manualRedraw = true;
                overlayCanvas.renderInterval = 1;
                overlayCanvas.renderIntervalFrameOffset = 0;
                overlayCanvas.layer = OverlayCanvasLayer;
                overlayCanvas.opacity = OVROverlayCanvas.DrawMode.OpaqueWithClip;
                overlayCanvas.shape = OVROverlayCanvas.CanvasShape.Flat;
                overlayCanvas.compositionMode = OVROverlayCanvas.CompositionMode.DepthTested;
                overlayCanvas.superSample = false;
                if (DynamicResolutionField != null)
                {
                    DynamicResolutionField.SetValue(overlayCanvas, false);
                }
            }

            if (backgroundPanel == null)
            {
                var panelTransform = canvasRect.Find(PanelName) as RectTransform;
                if (panelTransform == null)
                {
                    var panelObject = new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    panelObject.transform.SetParent(canvasRect, false);
                    panelTransform = panelObject.GetComponent<RectTransform>();
                }

                backgroundPanel = panelTransform.GetComponent<Image>();
            }

            EnsureBorderHierarchy();

            displayText = EnsureDisplayText();

            if (displayText != null)
            {
                SetLayerRecursive(displayText.gameObject, HiddenCanvasLayer);
                if (overlayCanvas != null)
                {
                    textChangeNotifier = displayText.GetComponent<OVROverlayCanvas_TMPChanged>();
                    if (textChangeNotifier == null)
                    {
                        textChangeNotifier = displayText.gameObject.AddComponent<OVROverlayCanvas_TMPChanged>();
                    }

                    textChangeNotifier.TargetCanvas = overlayCanvas;
                }
            }

            ApplyVisualDefaults();
            ApplyCameraCullingForOverlay();
        }

        void EnsureBorderHierarchy()
        {
            borderRoot = EnsureRectTransformChild(borderRoot, canvasRect, BorderName);
            if (borderRoot == null)
            {
                return;
            }

            borderTop = EnsureBorderImage(borderTop, borderRoot, "Top");
            borderBottom = EnsureBorderImage(borderBottom, borderRoot, "Bottom");
            borderLeft = EnsureBorderImage(borderLeft, borderRoot, "Left");
            borderRight = EnsureBorderImage(borderRight, borderRoot, "Right");
        }

        TextMeshProUGUI EnsureDisplayText()
        {
            if (backgroundPanel == null)
            {
                return null;
            }

            if (displayText != null)
            {
                return displayText;
            }

            var existingTmp = backgroundPanel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existingTmp != null)
            {
                return existingTmp;
            }

            var legacyTransform = backgroundPanel.transform.Find(TextName) as RectTransform;
            if (legacyTransform != null)
            {
                var legacyText = legacyTransform.GetComponent<Text>();
                if (legacyText != null)
                {
                    legacyText.enabled = false;
                    legacyText.raycastTarget = false;
                    legacyTransform.name = $"{TextName}Legacy";
                    SetLayerRecursive(legacyTransform.gameObject, HiddenCanvasLayer);
                }
            }

            var textObject = new GameObject(TextName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(backgroundPanel.transform, false);
            SetLayerRecursive(textObject, HiddenCanvasLayer);
            return textObject.GetComponent<TextMeshProUGUI>();
        }

        void ApplyVisualDefaults()
        {
            if (canvasRect == null)
            {
                return;
            }

            canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.sizeDelta = canvasSize;
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * worldScale;

            if (backgroundPanel != null)
            {
                var panelTransform = backgroundPanel.rectTransform;
                panelTransform.anchorMin = Vector2.zero;
                panelTransform.anchorMax = Vector2.one;
                panelTransform.pivot = new Vector2(0.5f, 0.5f);
                panelTransform.offsetMin = Vector2.zero;
                panelTransform.offsetMax = Vector2.zero;
                panelTransform.localScale = Vector3.one;
                backgroundPanel.raycastTarget = false;
                backgroundPanel.maskable = false;
                backgroundPanel.color = backgroundColor;
                SetLayerRecursive(backgroundPanel.gameObject, HiddenCanvasLayer);
            }

            ApplyBorderDefaults();

            if (displayText == null)
            {
                return;
            }

            var textRect = displayText.rectTransform;
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(
                Mathf.Max(64f, canvasSize.x - (textPadding.x * 2f)),
                Mathf.Max(64f, canvasSize.y - (textPadding.y * 2f)));
            textRect.localScale = Vector3.one;
            textRect.localRotation = Quaternion.identity;

            displayText.raycastTarget = false;
            displayText.maskable = false;
            displayText.richText = true;
            displayText.textWrappingMode = TextWrappingModes.NoWrap;
            displayText.overflowMode = TextOverflowModes.Truncate;
            displayText.alignment = TextAlignmentOptions.Center;
            displayText.fontSize = fontSize;
            displayText.lineSpacing = lineSpacing;
            displayText.color = textColor;
            displayText.margin = Vector4.zero;
            var defaultFont = ResolveDefaultFont();
            if (defaultFont != null)
            {
                displayText.font = defaultFont;
                displayText.fontSharedMaterial = defaultFont.material;
            }

            if (string.IsNullOrWhiteSpace(displayText.text))
            {
                displayText.text = BuildFallbackText();
            }
        }

        void ApplyBorderDefaults()
        {
            if (borderRoot == null)
            {
                return;
            }

            borderRoot.anchorMin = Vector2.zero;
            borderRoot.anchorMax = Vector2.one;
            borderRoot.pivot = new Vector2(0.5f, 0.5f);
            borderRoot.offsetMin = Vector2.zero;
            borderRoot.offsetMax = Vector2.zero;
            borderRoot.localScale = Vector3.one;
            borderRoot.localRotation = Quaternion.identity;
            SetLayerRecursive(borderRoot.gameObject, HiddenCanvasLayer);

            ConfigureBorderEdge(borderTop, BorderEdge.Top);
            ConfigureBorderEdge(borderBottom, BorderEdge.Bottom);
            ConfigureBorderEdge(borderLeft, BorderEdge.Left);
            ConfigureBorderEdge(borderRight, BorderEdge.Right);
        }

        void ConfigureBorderEdge(Image edgeImage, BorderEdge edge)
        {
            if (edgeImage == null)
            {
                return;
            }

            edgeImage.raycastTarget = false;
            edgeImage.maskable = false;
            edgeImage.color = borderColor;

            var rect = edgeImage.rectTransform;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            switch (edge)
            {
                case BorderEdge.Top:
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    rect.offsetMin = new Vector2(borderInset, -(borderInset + borderThickness));
                    rect.offsetMax = new Vector2(-borderInset, -borderInset);
                    break;
                case BorderEdge.Bottom:
                    rect.anchorMin = new Vector2(0f, 0f);
                    rect.anchorMax = new Vector2(1f, 0f);
                    rect.pivot = new Vector2(0.5f, 0f);
                    rect.offsetMin = new Vector2(borderInset, borderInset);
                    rect.offsetMax = new Vector2(-borderInset, borderInset + borderThickness);
                    break;
                case BorderEdge.Left:
                    rect.anchorMin = new Vector2(0f, 0f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 0.5f);
                    rect.offsetMin = new Vector2(borderInset, borderInset);
                    rect.offsetMax = new Vector2(borderInset + borderThickness, -borderInset);
                    break;
                case BorderEdge.Right:
                    rect.anchorMin = new Vector2(1f, 0f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(1f, 0.5f);
                    rect.offsetMin = new Vector2(-(borderInset + borderThickness), borderInset);
                    rect.offsetMax = new Vector2(-borderInset, -borderInset);
                    break;
            }
        }

        void ApplyVisibility()
        {
            if (worldCanvas != null)
            {
                worldCanvas.enabled = visible;
            }

            if (backgroundPanel != null)
            {
                backgroundPanel.enabled = visible;
            }

            if (displayText != null)
            {
                displayText.enabled = visible;
            }

            if (borderRoot != null)
            {
                borderRoot.gameObject.SetActive(visible);
            }

            if (overlayCanvas != null)
            {
                if (Application.isPlaying)
                {
                    overlayCanvas.enabled = useOverlayCanvas;
                    overlayCanvas.overlayEnabled = visible && useOverlayCanvas;
                }
                else
                {
                    overlayCanvas.enabled = false;
                    overlayCanvas.overlayEnabled = false;
                }
            }

            ApplyCameraCullingForOverlay();
            MarkOverlayDirty();
        }

        void UpdatePose()
        {
            if (headTransform == null)
            {
                return;
            }

            var targetPosition =
                headTransform.position
                + headTransform.forward * distanceFromHead
                + headTransform.right * horizontalOffset
                + headTransform.up * verticalOffset;

            var targetRotation = headTransform.rotation;

            if (!Application.isPlaying || !smoothFollow)
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
                return;
            }

            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref _positionVelocity, positionSmoothTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.unscaledDeltaTime * rotationLerpSpeed);
        }

        void MarkOverlayDirty()
        {
            if (overlayCanvas != null && Application.isPlaying && overlayCanvas.enabled)
            {
                overlayCanvas.SetFrameDirty();
            }
        }

        void ApplyCameraCullingForOverlay()
        {
            if (!Application.isPlaying || _mainCamera == null || overlayCanvas == null || !useOverlayCanvas)
            {
                return;
            }

            var hiddenLayerMask = 1 << overlayCanvas.gameObject.layer;
            _mainCamera.cullingMask &= ~hiddenLayerMask;
            _mainCamera.cullingMask |= 1 << overlayCanvas.layer;
        }

        Camera ResolveMainCamera()
        {
            if (headTransform != null)
            {
                var headCamera = headTransform.GetComponent<Camera>();
                if (headCamera != null)
                {
                    return headCamera;
                }
            }

            if (Camera.main != null)
            {
                return Camera.main;
            }

            var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (var i = 0; i < cameras.Length; i++)
            {
                var candidate = cameras[i];
                if (candidate == null || !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (candidate.cameraType == CameraType.Game)
                {
                    return candidate;
                }
            }

            return null;
        }

        static RectTransform EnsureRectTransformChild(RectTransform current, RectTransform parent, string childName)
        {
            if (parent == null)
            {
                return current;
            }

            if (current != null && current.parent == parent)
            {
                return current;
            }

            var existing = parent.Find(childName) as RectTransform;
            if (existing != null)
            {
                return existing;
            }

            var created = new GameObject(childName, typeof(RectTransform));
            var rect = created.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static Image EnsureBorderImage(Image current, RectTransform parent, string childName)
        {
            if (parent == null)
            {
                return current;
            }

            if (current != null && current.transform.parent == parent)
            {
                return current;
            }

            var existing = parent.Find(childName);
            GameObject owner;
            if (existing != null)
            {
                owner = existing.gameObject;
            }
            else
            {
                owner = new GameObject(childName, typeof(RectTransform));
                owner.transform.SetParent(parent, false);
            }

            if (owner.GetComponent<CanvasRenderer>() == null)
            {
                owner.AddComponent<CanvasRenderer>();
            }

            var image = owner.GetComponent<Image>();
            if (image == null)
            {
                image = owner.AddComponent<Image>();
            }

            return image;
        }

        static void SetLayerRecursive(GameObject target, int layer)
        {
            if (target == null)
            {
                return;
            }

            target.layer = layer;
            for (var i = 0; i < target.transform.childCount; i++)
            {
                SetLayerRecursive(target.transform.GetChild(i).gameObject, layer);
            }
        }

        static string BuildFallbackText()
        {
            return "<b><size=118%><color=#8FE6FF>=== BIG RED BUTTON ===</color></size></b>\n<size=78%><color=#7FA6B8>[ waiting for input manager ]</color></size>";
        }

        static TMP_FontAsset ResolveDefaultFont()
        {
            try
            {
                var defaultFont = TMP_Settings.defaultFontAsset;
                if (defaultFont != null)
                {
                    return defaultFont;
                }
            }
            catch (NullReferenceException)
            {
                // TMP resources have not initialized yet in this editor state.
            }

            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null && TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        static bool Approximately(Vector2 left, Vector2 right, float tolerance = 0.5f)
        {
            return Mathf.Abs(left.x - right.x) <= tolerance
                && Mathf.Abs(left.y - right.y) <= tolerance;
        }

        enum BorderEdge
        {
            Top = 0,
            Bottom = 1,
            Left = 2,
            Right = 3
        }
    }
}
