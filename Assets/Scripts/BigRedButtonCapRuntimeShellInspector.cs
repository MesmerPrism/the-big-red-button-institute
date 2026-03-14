using UnityEngine;
using UnityEngine.Rendering;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonCapRuntimeShellInspector : MonoBehaviour
    {
        [SerializeField] SkinnedMeshRenderer sourceRenderer;
        [SerializeField] bool autoResolveSource = true;
        [SerializeField, Min(1f)] float shellOversize = 1.08f;
        [SerializeField, Min(0f)] float pulseAmplitude = 0.035f;
        [SerializeField, Min(0f)] float pulseFrequency = 2.8f;
        [SerializeField] bool alwaysOnTop = true;
        [SerializeField] Color shellColor = new(0.34f, 1f, 0.18f, 0.24f);
        [SerializeField] Color pulseColor = new(1f, 0.98f, 0.28f, 0.58f);
        [SerializeField, Min(0f)] float emissionIntensity = 1.4f;

        Transform _visualTransform;
        MeshFilter _visualMeshFilter;
        MeshRenderer _visualRenderer;
        Mesh _runtimeMesh;
        Material _runtimeMaterial;

        public SkinnedMeshRenderer SourceRenderer => sourceRenderer;

        void Reset()
        {
            ResolveSourceRenderer(forceRefresh: true);
        }

        public void Configure(
            SkinnedMeshRenderer renderer,
            float targetShellOversize = 1.08f,
            float targetPulseAmplitude = 0.035f,
            float targetPulseFrequency = 2.8f,
            bool targetAlwaysOnTop = true)
        {
            sourceRenderer = renderer;
            shellOversize = Mathf.Max(1f, targetShellOversize);
            pulseAmplitude = Mathf.Max(0f, targetPulseAmplitude);
            pulseFrequency = Mathf.Max(0f, targetPulseFrequency);
            alwaysOnTop = targetAlwaysOnTop;

            if (!Application.isPlaying)
            {
                return;
            }

            EnsureVisual();
            SyncVisualMesh();
            UpdateVisualPresentation();
        }

        void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveSourceRenderer(forceRefresh: false);
            EnsureVisual();
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveSourceRenderer(forceRefresh: false);
            EnsureVisual();
            SyncVisualMesh();
            UpdateVisualPresentation();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveSourceRenderer(forceRefresh: false);
            EnsureVisual();
            SyncVisualMesh();
            UpdateVisualPresentation();
        }

        void OnDisable()
        {
            DestroyVisual();
        }

        void OnDestroy()
        {
            DestroyVisual();
            DestroyRuntimeMesh();
            DestroyRuntimeMaterial();
        }

        void ResolveSourceRenderer(bool forceRefresh)
        {
            if ((sourceRenderer == null || forceRefresh) && autoResolveSource)
            {
                sourceRenderer = GetComponent<SkinnedMeshRenderer>();
            }
        }

        void EnsureVisual()
        {
            if (sourceRenderer == null)
            {
                DestroyVisual();
                return;
            }

            if (_visualTransform == null)
            {
                var visualObject = new GameObject("Cap Runtime Shell Inspector");
                visualObject.layer = gameObject.layer;
                visualObject.transform.SetParent(transform, false);
                _visualTransform = visualObject.transform;
                _visualMeshFilter = visualObject.AddComponent<MeshFilter>();
                _visualRenderer = visualObject.AddComponent<MeshRenderer>();
                _visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _visualRenderer.receiveShadows = false;
                _visualRenderer.lightProbeUsage = LightProbeUsage.Off;
                _visualRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _visualRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                _visualRenderer.allowOcclusionWhenDynamic = false;
            }

            _runtimeMaterial ??= CreateRuntimeMaterial(alwaysOnTop);
            if (_visualRenderer != null)
            {
                _visualRenderer.sharedMaterial = _runtimeMaterial;
            }
        }

        void SyncVisualMesh()
        {
            if (sourceRenderer == null || _visualMeshFilter == null || _visualRenderer == null)
            {
                return;
            }

            _runtimeMesh ??= CreateRuntimeMesh();
            sourceRenderer.BakeMesh(_runtimeMesh, false);
            if (_runtimeMesh.vertexCount <= 0)
            {
                _visualMeshFilter.sharedMesh = null;
                _visualRenderer.enabled = false;
                return;
            }

            _visualMeshFilter.sharedMesh = _runtimeMesh;
            _visualRenderer.enabled = sourceRenderer.enabled && sourceRenderer.gameObject.activeInHierarchy;
        }

        void UpdateVisualPresentation()
        {
            if (_visualTransform == null || _visualRenderer == null)
            {
                return;
            }

            var pulse = pulseAmplitude <= 0f || pulseFrequency <= 0f
                ? 1f
                : 1f + Mathf.Abs(Mathf.Sin((Time.unscaledTime * pulseFrequency) + (GetInstanceID() * 0.01f))) * pulseAmplitude;
            _visualTransform.localPosition = Vector3.zero;
            _visualTransform.localRotation = Quaternion.identity;
            _visualTransform.localScale = Vector3.one * shellOversize * pulse;

            var lerp = pulseAmplitude <= 0f || pulseFrequency <= 0f
                ? 0f
                : Mathf.Abs(Mathf.Sin((Time.unscaledTime * pulseFrequency * 0.5f) + (GetInstanceID() * 0.013f)));
            ApplyMaterialColor(Color.Lerp(shellColor, pulseColor, lerp), Mathf.Lerp(emissionIntensity, emissionIntensity * 1.9f, lerp));
        }

        void DestroyVisual()
        {
            if (_visualTransform == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_visualTransform.gameObject);
            }
            else
            {
                DestroyImmediate(_visualTransform.gameObject);
            }

            _visualTransform = null;
            _visualMeshFilter = null;
            _visualRenderer = null;
        }

        void DestroyRuntimeMesh()
        {
            if (_runtimeMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_runtimeMesh);
            }
            else
            {
                DestroyImmediate(_runtimeMesh);
            }

            _runtimeMesh = null;
        }

        void DestroyRuntimeMaterial()
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

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

        Mesh CreateRuntimeMesh()
        {
            var mesh = new Mesh
            {
                name = $"{name} Runtime Shell Mesh"
            };
            mesh.MarkDynamic();
            return mesh;
        }

        static Material CreateRuntimeMaterial(bool targetAlwaysOnTop)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");

            var material = new Material(shader)
            {
                name = "BigRedButton Cap Runtime Shell",
                renderQueue = (int)RenderQueue.Overlay
            };

            material.enableInstancing = true;
            material.SetOverrideTag("RenderType", "Transparent");

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)CullMode.Off);
            }

            if (material.HasProperty("_QueueOffset"))
            {
                material.SetFloat("_QueueOffset", 100f);
            }

            if (targetAlwaysOnTop && material.HasProperty("_ZTest"))
            {
                material.SetFloat("_ZTest", (float)CompareFunction.Always);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_EMISSION");
            return material;
        }

        void ApplyMaterialColor(Color color, float targetEmissionIntensity)
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            if (_runtimeMaterial.HasProperty("_BaseColor"))
            {
                _runtimeMaterial.SetColor("_BaseColor", color);
            }

            if (_runtimeMaterial.HasProperty("_Color"))
            {
                _runtimeMaterial.SetColor("_Color", color);
            }

            var emission = color * Mathf.Max(0f, targetEmissionIntensity);
            if (_runtimeMaterial.HasProperty("_EmissionColor"))
            {
                _runtimeMaterial.SetColor("_EmissionColor", emission);
            }

            if (_runtimeMaterial.HasProperty("_EmissiveColor"))
            {
                _runtimeMaterial.SetColor("_EmissiveColor", emission);
            }
        }
    }
}
