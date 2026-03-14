using UnityEngine;
using UnityEngine.Rendering;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonRendererMeshDebug : MonoBehaviour
    {
        const float HighlightHoldSeconds = 0.12f;

        [SerializeField] Renderer sourceRenderer;
        [SerializeField] bool autoResolveSource = true;
        [SerializeField, Min(1f)] float oversize = 1.08f;
        [SerializeField, Min(0f)] float verticalLiftFactor;
        [SerializeField] Color baseColor = new(0.08f, 1f, 0.82f, 0.72f);
        [SerializeField] Color highlightColor = new(1f, 0.95f, 0.18f, 0.92f);

        Transform _visualTransform;
        MeshFilter _visualMeshFilter;
        MeshRenderer _visualRenderer;
        Mesh _runtimeMesh;
        Material _runtimeMaterial;
        float _lastHighlightTime = float.NegativeInfinity;

        public Renderer SourceRenderer => sourceRenderer;

        public void Configure(
            Renderer renderer,
            float targetOversize = 1.08f,
            float targetVerticalLiftFactor = 0f)
        {
            sourceRenderer = renderer;
            oversize = Mathf.Max(1f, targetOversize);
            verticalLiftFactor = Mathf.Max(0f, targetVerticalLiftFactor);
            if (!Application.isPlaying)
            {
                return;
            }

            EnsureVisual();
            UpdateVisual(forceHighlight: false);
        }

        public void MarkHighlighted()
        {
            _lastHighlightTime = Time.unscaledTime;
            UpdateVisual(forceHighlight: true);
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
            UpdateVisual(forceHighlight: false);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveSourceRenderer(forceRefresh: false);
            EnsureVisual();
            UpdateVisual(forceHighlight: false);
        }

        void OnDisable()
        {
            DestroyVisual();
        }

        void OnDestroy()
        {
            DestroyVisual();

            if (_runtimeMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMesh);
                }
                else
                {
                    DestroyImmediate(_runtimeMesh);
                }
            }

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
        }

        void ResolveSourceRenderer(bool forceRefresh)
        {
            if ((sourceRenderer == null || forceRefresh) && autoResolveSource)
            {
                sourceRenderer = GetComponent<Renderer>();
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
                var visualObject = new GameObject("Renderer Mesh Debug");
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

            if (_runtimeMaterial == null)
            {
                _runtimeMaterial = CreateRuntimeMaterial();
            }

            if (_visualRenderer != null)
            {
                _visualRenderer.sharedMaterial = _runtimeMaterial;
            }
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

        void UpdateVisual(bool forceHighlight)
        {
            if (sourceRenderer == null || _visualTransform == null || _visualMeshFilter == null || _visualRenderer == null)
            {
                return;
            }

            if (!TryResolveVisualMesh(sourceRenderer, out var visualMesh))
            {
                _visualMeshFilter.sharedMesh = null;
                _visualRenderer.enabled = false;
                return;
            }

            _visualMeshFilter.sharedMesh = visualMesh;

            var bounds = visualMesh.bounds;
            var highlighted = forceHighlight || Time.unscaledTime - _lastHighlightTime <= HighlightHoldSeconds;
            var lift = Mathf.Max(0.0025f, bounds.extents.y * verticalLiftFactor);
            var pulse = 1f + Mathf.Abs(Mathf.Sin((Time.unscaledTime * 4.5f) + (GetInstanceID() * 0.01f))) * 0.05f;
            var highlightScale = highlighted ? 1.05f : 1f;

            _visualTransform.localPosition = Vector3.up * lift;
            _visualTransform.localRotation = Quaternion.identity;
            _visualTransform.localScale = Vector3.one * oversize * pulse * highlightScale;

            ApplyMaterialColor(highlighted ? highlightColor : baseColor, highlighted ? 1.8f : 0.95f);
            _visualRenderer.enabled = sourceRenderer.enabled && sourceRenderer.gameObject.activeInHierarchy;
        }

        bool TryResolveVisualMesh(Renderer renderer, out Mesh mesh)
        {
            mesh = null;
            if (renderer == null)
            {
                return false;
            }

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                _runtimeMesh ??= CreateRuntimeMesh();
                skinnedMeshRenderer.BakeMesh(_runtimeMesh);
                mesh = _runtimeMesh;
                return mesh.vertexCount > 0;
            }

            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return false;
            }

            mesh = meshFilter.sharedMesh;
            return mesh.vertexCount > 0;
        }

        Mesh CreateRuntimeMesh()
        {
            var mesh = new Mesh
            {
                name = $"{name} Renderer Debug Mesh"
            };
            mesh.MarkDynamic();
            return mesh;
        }

        static Material CreateRuntimeMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");

            var material = new Material(shader)
            {
                name = "BigRedButton Renderer Mesh Debug",
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
                material.SetFloat("_QueueOffset", 80f);
            }

            if (material.HasProperty("_ZTest"))
            {
                material.SetFloat("_ZTest", (float)CompareFunction.Always);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_EMISSION");
            return material;
        }

        void ApplyMaterialColor(Color color, float emissionIntensity)
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

            var emission = color * emissionIntensity;
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
