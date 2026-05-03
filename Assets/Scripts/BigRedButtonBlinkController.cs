using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonBlinkController : MonoBehaviour
    {
        const string DefaultBlinkChildName = "button";
        const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");

        [Header("References")]
        [SerializeField] Renderer targetRenderer;
        [SerializeField] Transform blinkAnchor;
        [SerializeField] Light pulseLight;
        [SerializeField] bool autoResolveReferences = true;

        [Header("Blink")]
        [SerializeField] string targetChildName = DefaultBlinkChildName;
        [SerializeField] Color idleTint = new(0.82f, 0.22f, 0.22f, 1f);
        [SerializeField] Color blinkTint = new(1f, 0.72f, 0.72f, 1f);
        [SerializeField, ColorUsage(false, true)] Color idleEmission = Color.black;
        [SerializeField, ColorUsage(false, true)] Color blinkEmission = new(7f, 1.1f, 0.85f, 1f);
        [SerializeField, Min(0.05f)] float pulseDuration = 0.32f;
        [SerializeField, Min(0.1f)] float blinkRateHz = 1f;
        [SerializeField, Range(0f, 1f)] float continuousBlinkFloor = 0.18f;
        [SerializeField] AnimationCurve pulseCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Light")]
        [SerializeField] bool drivePulseLight = true;
        [SerializeField] Color pulseLightColor = new(1f, 0.18f, 0.12f, 1f);
        [SerializeField, Min(0f)] float idleLightIntensity = 0f;
        [SerializeField, Min(0f)] float blinkLightIntensity = 2.2f;
        [SerializeField, Min(0.05f)] float pulseLightRange = 0.6f;
        [SerializeField] bool usePulseLightSurfaceRig = true;
        [SerializeField, Range(1, 4)] int pulseLightSurfaceCount = 4;
        [SerializeField, Range(0f, 1f)] float pulseLightSurfaceRadiusScale = 0.42f;
        [SerializeField] bool placePulseLightOnRendererSurface = true;
        [SerializeField, Min(0f)] float pulseLightSurfaceInset = 0.016f;
        [SerializeField] Vector3 pulseLightSurfaceWorldOffset = Vector3.zero;
        [SerializeField] Vector3 pulseLightLocalOffset = new(0f, 0.0195f, 0.0029f);

        Material _runtimeMaterial;
        bool _ownsPulseLight;
        Transform _surfaceLightRigRoot;
        readonly List<Light> _surfacePulseLights = new();
        bool _isInitialized;
        bool _continuousBlink;
        float _pulseStartTime = float.NegativeInfinity;
        int _colorPropertyId;
        int _emissionPropertyId;

        public Renderer TargetRenderer => targetRenderer;
        public bool IsBlinkingContinuously => _continuousBlink;

        void Reset()
        {
            ConfigureReferences(FindPreferredRenderer(), null, null);
        }

        void Awake()
        {
            Initialize(forceRefresh: true);
            ApplyCurrentState();
        }

        void OnEnable()
        {
            Initialize(forceRefresh: false);
            ApplyCurrentState();
        }

        void Update()
        {
            if (!_isInitialized && autoResolveReferences)
            {
                Initialize(forceRefresh: true);
            }

            if (!_isInitialized)
            {
                return;
            }

            ApplyCurrentState();
        }

        void OnDisable()
        {
            if (_isInitialized)
            {
                ApplyIntensity(0f);
            }
        }

        void OnDestroy()
        {
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(_runtimeMaterial);
                }

                _runtimeMaterial = null;
            }

            if (_ownsPulseLight && pulseLight != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(pulseLight.gameObject);
                }
                else
                {
                    DestroyImmediate(pulseLight.gameObject);
                }
            }

            if (_surfaceLightRigRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_surfaceLightRigRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(_surfaceLightRigRoot.gameObject);
                }
            }
        }

        public void ConfigureReferences(Renderer renderer, Transform anchor = null, Light light = null)
        {
            targetRenderer = renderer;
            blinkAnchor = anchor != null ? anchor : renderer != null ? renderer.transform : blinkAnchor;
            pulseLight = light;

            if (_isInitialized && Application.isPlaying)
            {
                Initialize(forceRefresh: true);
                ApplyCurrentState();
            }
        }

        public void PulseOnce()
        {
            Initialize(forceRefresh: false);
            if (!_isInitialized)
            {
                return;
            }

            _pulseStartTime = Time.unscaledTime;
            ApplyCurrentState();
        }

        public void SetBlinking(bool shouldBlink)
        {
            Initialize(forceRefresh: false);
            _continuousBlink = shouldBlink;
            ApplyCurrentState();
        }

        public void StopAndReset()
        {
            _continuousBlink = false;
            _pulseStartTime = float.NegativeInfinity;
            ApplyCurrentState();
        }

        void Initialize(bool forceRefresh)
        {
            if (!forceRefresh && _isInitialized)
            {
                return;
            }

            if (targetRenderer == null || forceRefresh)
            {
                targetRenderer = FindPreferredRenderer();
            }

            if (targetRenderer == null)
            {
                _isInitialized = false;
                return;
            }

            blinkAnchor ??= targetRenderer.transform;

            if (_runtimeMaterial == null || forceRefresh)
            {
                AssignRuntimeMaterial(targetRenderer);
            }

            EnsurePulseLight();
            _isInitialized = _runtimeMaterial != null;
        }

        void AssignRuntimeMaterial(Renderer renderer)
        {
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(_runtimeMaterial);
                }
            }

            var sourceMaterial = renderer.sharedMaterial;
            _runtimeMaterial = CreateBlinkMaterial(sourceMaterial);
            if (_runtimeMaterial == null)
            {
                _isInitialized = false;
                return;
            }

            _runtimeMaterial.name = $"{renderer.name}_BlinkRuntime";
            _runtimeMaterial.enableInstancing = true;

            _colorPropertyId = ResolveColorPropertyId(_runtimeMaterial);
            _emissionPropertyId = ResolveEmissionPropertyId(_runtimeMaterial);
            if (_emissionPropertyId != 0)
            {
                _runtimeMaterial.EnableKeyword("_EMISSION");
            }

            renderer.sharedMaterial = _runtimeMaterial;
        }

        Material CreateBlinkMaterial(Material sourceMaterial)
        {
            if (sourceMaterial != null && SupportsBlinkProperties(sourceMaterial))
            {
                return new Material(sourceMaterial);
            }

            var fallbackShader = Shader.Find(UrpLitShaderName);
            if (fallbackShader != null)
            {
                var material = new Material(fallbackShader);
                CopySurfaceProperties(sourceMaterial, material);
                material.SetFloat(SmoothnessId, sourceMaterial != null && sourceMaterial.HasProperty(SmoothnessId)
                    ? sourceMaterial.GetFloat(SmoothnessId)
                    : sourceMaterial != null && sourceMaterial.HasProperty(GlossinessId)
                        ? sourceMaterial.GetFloat(GlossinessId)
                        : 0.55f);
                return material;
            }

            return sourceMaterial != null ? new Material(sourceMaterial) : null;
        }

        void CopySurfaceProperties(Material sourceMaterial, Material targetMaterial)
        {
            if (sourceMaterial == null || targetMaterial == null)
            {
                return;
            }

            var texture = GetFirstTexture(sourceMaterial, BaseMapId, MainTexId);
            if (texture != null && targetMaterial.HasProperty(BaseMapId))
            {
                targetMaterial.SetTexture(BaseMapId, texture);
                var sourceProperty = sourceMaterial.HasProperty(BaseMapId) ? BaseMapId : MainTexId;
                if (sourceMaterial.HasProperty(sourceProperty))
                {
                    targetMaterial.SetTextureScale(BaseMapId, sourceMaterial.GetTextureScale(sourceProperty));
                    targetMaterial.SetTextureOffset(BaseMapId, sourceMaterial.GetTextureOffset(sourceProperty));
                }
            }

            var sourceColorProperty = ResolveColorPropertyId(sourceMaterial);
            if (sourceColorProperty != 0 && targetMaterial.HasProperty(BaseColorId))
            {
                targetMaterial.SetColor(BaseColorId, sourceMaterial.GetColor(sourceColorProperty));
            }
        }

        void EnsurePulseLight()
        {
            if (!drivePulseLight)
            {
                DisablePulseLights();

                return;
            }

            if (usePulseLightSurfaceRig)
            {
                EnsureSurfacePulseLights();
                ApplyPulseLightPose();
                return;
            }

            if (pulseLight == null && Application.isPlaying)
            {
                var anchor = blinkAnchor != null ? blinkAnchor : targetRenderer != null ? targetRenderer.transform : transform;
                var lightObject = new GameObject("Button Blink Light");
                lightObject.transform.SetParent(anchor, false);
                lightObject.transform.localPosition = pulseLightLocalOffset;
                lightObject.transform.localRotation = Quaternion.identity;
                pulseLight = lightObject.AddComponent<Light>();
                pulseLight.type = LightType.Point;
                pulseLight.shadows = LightShadows.None;
                pulseLight.renderMode = LightRenderMode.ForcePixel;
                pulseLight.enabled = false;
                _ownsPulseLight = true;
            }

            if (pulseLight == null)
            {
                return;
            }

            pulseLight.type = LightType.Point;
            pulseLight.shadows = LightShadows.None;
            pulseLight.renderMode = LightRenderMode.ForcePixel;
            pulseLight.color = pulseLightColor;
            pulseLight.range = pulseLightRange;
            ApplyPulseLightPose();
        }

        void ApplyCurrentState()
        {
            if (!_isInitialized)
            {
                return;
            }

            var pulseIntensity = EvaluatePulse();
            var continuousIntensity = EvaluateContinuousBlink();
            ApplyIntensity(Mathf.Clamp01(Mathf.Max(pulseIntensity, continuousIntensity)));
        }

        float EvaluatePulse()
        {
            if (float.IsNegativeInfinity(_pulseStartTime) || pulseDuration <= 0f)
            {
                return 0f;
            }

            var elapsed = Time.unscaledTime - _pulseStartTime;
            if (elapsed < 0f || elapsed >= pulseDuration)
            {
                return 0f;
            }

            var normalizedTime = Mathf.Clamp01(elapsed / pulseDuration);
            return pulseCurve == null ? 1f - normalizedTime : Mathf.Clamp01(pulseCurve.Evaluate(normalizedTime));
        }

        float EvaluateContinuousBlink()
        {
            if (!_continuousBlink)
            {
                return 0f;
            }

            var wave = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * blinkRateHz * Mathf.PI * 2f));
            return Mathf.Lerp(continuousBlinkFloor, 1f, wave);
        }

        void ApplyIntensity(float intensity01)
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            if (_colorPropertyId != 0)
            {
                _runtimeMaterial.SetColor(_colorPropertyId, Color.Lerp(idleTint, blinkTint, intensity01));
            }

            if (_emissionPropertyId != 0)
            {
                _runtimeMaterial.SetColor(_emissionPropertyId, Color.Lerp(idleEmission, blinkEmission, intensity01));
                _runtimeMaterial.EnableKeyword("_EMISSION");
            }

            ApplyPulseLights(intensity01);
        }

        void ApplyPulseLights(float intensity01)
        {
            if (!drivePulseLight)
            {
                DisablePulseLights();
                return;
            }

            EnsurePulseLight();
            var intensity = Mathf.Lerp(idleLightIntensity, blinkLightIntensity, intensity01);
            var shouldEnable = intensity > 0.001f;

            if (usePulseLightSurfaceRig)
            {
                for (var i = 0; i < _surfacePulseLights.Count; i++)
                {
                    ApplyLightSettings(_surfacePulseLights[i], intensity, shouldEnable);
                }

                return;
            }

            ApplyLightSettings(pulseLight, intensity, shouldEnable);
            ApplyPulseLightPose();
        }

        void ApplyLightSettings(Light light, float intensity, bool shouldEnable)
        {
            if (light == null)
            {
                return;
            }

            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.color = pulseLightColor;
            light.range = pulseLightRange;
            light.intensity = intensity;
            light.enabled = shouldEnable;
        }

        void EnsureSurfacePulseLights()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var anchor = blinkAnchor != null ? blinkAnchor : targetRenderer != null ? targetRenderer.transform : transform;
            if (_surfaceLightRigRoot == null)
            {
                var rigObject = new GameObject("Button Surface Glow Lights");
                rigObject.transform.SetParent(anchor, false);
                rigObject.transform.localPosition = Vector3.zero;
                rigObject.transform.localRotation = Quaternion.identity;
                _surfaceLightRigRoot = rigObject.transform;
            }

            var requiredCount = Mathf.Clamp(pulseLightSurfaceCount, 1, 4);
            while (_surfacePulseLights.Count < requiredCount)
            {
                var lightObject = new GameObject($"Surface Glow Light {_surfacePulseLights.Count + 1}");
                lightObject.transform.SetParent(_surfaceLightRigRoot, false);
                var light = lightObject.AddComponent<Light>();
                light.enabled = false;
                _surfacePulseLights.Add(light);
            }

            for (var i = 0; i < _surfacePulseLights.Count; i++)
            {
                _surfacePulseLights[i].gameObject.SetActive(i < requiredCount);
            }

            pulseLight = _surfacePulseLights.Count > 0 ? _surfacePulseLights[0] : null;
        }

        void ApplyPulseLightPose()
        {
            if (usePulseLightSurfaceRig)
            {
                ApplySurfacePulseLightPoses();
                return;
            }

            if (pulseLight == null || !CanDrivePulseLightPose())
            {
                return;
            }

            if (placePulseLightOnRendererSurface && targetRenderer != null)
            {
                var bounds = targetRenderer.bounds;
                pulseLight.transform.position = new Vector3(
                    bounds.center.x,
                    bounds.max.y - pulseLightSurfaceInset,
                    bounds.center.z) + pulseLightSurfaceWorldOffset;
                pulseLight.transform.rotation = Quaternion.identity;
                return;
            }

            pulseLight.transform.localPosition = pulseLightLocalOffset;
            pulseLight.transform.localRotation = Quaternion.identity;
        }

        void ApplySurfacePulseLightPoses()
        {
            if (_surfacePulseLights.Count <= 0 || targetRenderer == null)
            {
                return;
            }

            var positions = ResolveSurfaceLightPositions();
            var count = Mathf.Min(_surfacePulseLights.Count, positions.Length);
            for (var i = 0; i < count; i++)
            {
                var light = _surfacePulseLights[i];
                if (light == null)
                {
                    continue;
                }

                light.transform.position = positions[i];
                light.transform.rotation = Quaternion.identity;
            }
        }

        Vector3[] ResolveSurfaceLightPositions()
        {
            var bounds = targetRenderer.bounds;
            var y = bounds.max.y - pulseLightSurfaceInset;
            var center = new Vector3(bounds.center.x, y, bounds.center.z) + pulseLightSurfaceWorldOffset;
            var radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * pulseLightSurfaceRadiusScale;
            var right = ResolveHorizontalAxis(blinkAnchor != null ? blinkAnchor.right : transform.right, Vector3.right);
            var forward = ResolveHorizontalAxis(blinkAnchor != null ? blinkAnchor.forward : transform.forward, Vector3.forward);
            var count = Mathf.Clamp(pulseLightSurfaceCount, 1, 4);

            return count switch
            {
                1 => new[] { center },
                2 => new[] { center - (right * radius), center + (right * radius) },
                3 => new[] { center + (forward * radius), center - (right * radius * 0.86f) - (forward * radius * 0.5f), center + (right * radius * 0.86f) - (forward * radius * 0.5f) },
                _ => new[] { center - (right * radius), center + (right * radius), center - (forward * radius), center + (forward * radius) }
            };
        }

        bool CanDrivePulseLightPose()
        {
            return _ownsPulseLight || pulseLight.transform.parent == blinkAnchor;
        }

        void DisablePulseLights()
        {
            if (pulseLight != null)
            {
                pulseLight.enabled = false;
            }

            for (var i = 0; i < _surfacePulseLights.Count; i++)
            {
                if (_surfacePulseLights[i] != null)
                {
                    _surfacePulseLights[i].enabled = false;
                }
            }
        }

        Renderer FindPreferredRenderer()
        {
            Renderer fallback = null;
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = renderer;
                }

                var objectName = renderer.gameObject.name;
                if (string.Equals(objectName, targetChildName, StringComparison.OrdinalIgnoreCase))
                {
                    return renderer;
                }

                if (objectName.IndexOf(targetChildName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fallback = renderer;
                }
            }

            return fallback;
        }

        static Texture GetFirstTexture(Material material, params int[] propertyIds)
        {
            if (material == null)
            {
                return null;
            }

            for (var i = 0; i < propertyIds.Length; i++)
            {
                if (material.HasProperty(propertyIds[i]))
                {
                    var texture = material.GetTexture(propertyIds[i]);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
            }

            return null;
        }

        static bool SupportsBlinkProperties(Material material)
        {
            if (material == null)
            {
                return false;
            }

            return ResolveColorPropertyId(material) != 0 && ResolveEmissionPropertyId(material) != 0;
        }

        static int ResolveColorPropertyId(Material material)
        {
            if (material == null)
            {
                return 0;
            }

            if (material.HasProperty(BaseColorId))
            {
                return BaseColorId;
            }

            return material.HasProperty(ColorId) ? ColorId : 0;
        }

        static int ResolveEmissionPropertyId(Material material)
        {
            if (material == null)
            {
                return 0;
            }

            if (material.HasProperty(EmissionColorId))
            {
                return EmissionColorId;
            }

            return material.HasProperty(EmissiveColorId) ? EmissiveColorId : 0;
        }

        static Vector3 ResolveHorizontalAxis(Vector3 axis, Vector3 fallback)
        {
            var projected = Vector3.ProjectOnPlane(axis, Vector3.up);
            return projected.sqrMagnitude > 0.0001f ? projected.normalized : fallback;
        }
    }
}
