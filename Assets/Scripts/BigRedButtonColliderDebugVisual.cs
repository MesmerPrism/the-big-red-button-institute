using UnityEngine;
using UnityEngine.Rendering;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonColliderDebugVisual : MonoBehaviour
    {
        enum VisualGeometryType
        {
            None = 0,
            Box = 1,
            Sphere = 2,
            Capsule = 3,
            PressZone = 4,
            Mesh = 5
        }

        public enum VisualRole
        {
            Interactor = 0,
            PressZone = 1,
            PressTrigger = 2
        }

        const float HighlightHoldSeconds = 0.12f;
        const float BoxOversize = 1.08f;
        const float CapsuleOversize = 1.02f;
        const float SphereOversize = 1.03f;
        const float MeshOversize = 1.01f;
        const float PressZoneOversize = 1.01f;
        const float PressTriggerOversize = 1.22f;
        const float PressTriggerMeshOversize = 1.16f;

        [SerializeField] VisualRole role = VisualRole.Interactor;

        Collider _targetCollider;
        BigRedButtonPressColliderProxy _proxy;
        Transform _visualTransform;
        MeshRenderer _visualRenderer;
        MeshFilter _visualMeshFilter;
        Material _runtimeMaterial;
        VisualGeometryType _currentGeometry = VisualGeometryType.None;
        float _lastHighlightTime = float.NegativeInfinity;

        public void Configure(VisualRole targetRole)
        {
            role = targetRole;
            if (!Application.isPlaying)
            {
                return;
            }

            EnsureVisual();
            ApplyMaterialPresentation();
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

            CacheComponents();
            EnsureVisual();
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CacheComponents();
            EnsureVisual();
            UpdateVisual(forceHighlight: false);
        }

        void OnDisable()
        {
            DestroyVisual();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CacheComponents();
            EnsureVisual();
            UpdateVisual(forceHighlight: false);
        }

        void OnDestroy()
        {
            DestroyVisual();

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
        }

        void CacheComponents()
        {
            _targetCollider ??= GetComponent<Collider>();
            _proxy ??= GetComponent<BigRedButtonPressColliderProxy>();
        }

        void EnsureVisual()
        {
            if (_targetCollider == null)
            {
                return;
            }

            var geometryType = GetGeometryType();
            if (_visualTransform == null || geometryType != _currentGeometry)
            {
                RebuildVisual(geometryType);
            }

            if (_runtimeMaterial == null)
            {
                _runtimeMaterial = CreateRuntimeMaterial();
                if (_visualRenderer != null)
                {
                    _visualRenderer.sharedMaterial = _runtimeMaterial;
                }
            }

            ApplyMaterialPresentation();
        }

        void RebuildVisual(VisualGeometryType geometryType)
        {
            DestroyVisual();

            GameObject visualObject;
            if (geometryType == VisualGeometryType.Mesh)
            {
                visualObject = new GameObject("Collider Debug Visual");
                visualObject.layer = gameObject.layer;
                visualObject.transform.SetParent(transform, false);
                _visualMeshFilter = visualObject.AddComponent<MeshFilter>();
                _visualRenderer = visualObject.AddComponent<MeshRenderer>();
            }
            else
            {
                var primitiveType = GetPrimitiveType(geometryType);
                visualObject = GameObject.CreatePrimitive(primitiveType);
                visualObject.name = "Collider Debug Visual";
                visualObject.layer = gameObject.layer;
                visualObject.transform.SetParent(transform, false);

                var primitiveCollider = visualObject.GetComponent<Collider>();
                if (primitiveCollider != null)
                {
                    primitiveCollider.enabled = false;
                }

                _visualRenderer = visualObject.GetComponent<MeshRenderer>();
                _visualMeshFilter = visualObject.GetComponent<MeshFilter>();
            }

            _visualTransform = visualObject.transform;
            _currentGeometry = geometryType;

            if (_visualRenderer == null)
            {
                return;
            }

            _visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _visualRenderer.receiveShadows = false;
            _visualRenderer.lightProbeUsage = LightProbeUsage.Off;
            _visualRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _visualRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _visualRenderer.allowOcclusionWhenDynamic = false;

            if (_runtimeMaterial != null)
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
            _visualRenderer = null;
            _visualMeshFilter = null;
            _currentGeometry = VisualGeometryType.None;
        }

        void UpdateVisual(bool forceHighlight)
        {
            if (_targetCollider == null || _visualTransform == null || _visualRenderer == null)
            {
                return;
            }

            if (_proxy != null && _proxy.WasHighlightedRecently)
            {
                _lastHighlightTime = Time.unscaledTime;
            }

            var highlighted = forceHighlight || Time.unscaledTime - _lastHighlightTime <= HighlightHoldSeconds;
            ApplyVisualTransform(highlighted);
            ApplyVisualColor(highlighted);
            _visualRenderer.enabled = _targetCollider.enabled &&
                gameObject.activeInHierarchy &&
                (_currentGeometry != VisualGeometryType.Mesh || (_visualMeshFilter != null && _visualMeshFilter.sharedMesh != null));
        }

        void ApplyVisualTransform(bool highlighted)
        {
            var pulse = 1f + Mathf.Abs(Mathf.Sin((Time.unscaledTime * 4.5f) + (GetInstanceID() * 0.01f))) * 0.04f;
            var highlightScale = highlighted ? 1.04f : 1f;
            var oversize = GetBaseOversize() * pulse * highlightScale;

            switch (_targetCollider)
            {
                case BoxCollider boxCollider:
                    _visualTransform.localPosition = boxCollider.center;
                    _visualTransform.localRotation = Quaternion.identity;
                    _visualTransform.localScale = UsesDiscVisual(role)
                        ? new Vector3(
                            boxCollider.size.x * oversize,
                            boxCollider.size.y * GetDiscHeightFactor(role) * oversize,
                            boxCollider.size.z * oversize)
                        : boxCollider.size * oversize;
                    break;
                case SphereCollider sphereCollider:
                    var sphereDiameter = sphereCollider.radius * 2f * oversize;
                    _visualTransform.localPosition = sphereCollider.center;
                    _visualTransform.localRotation = Quaternion.identity;
                    _visualTransform.localScale = Vector3.one * sphereDiameter;
                    break;
                case CapsuleCollider capsuleCollider:
                    var diameter = capsuleCollider.radius * 2f * oversize;
                    var totalHeight = Mathf.Max(capsuleCollider.height, capsuleCollider.radius * 2f) * oversize;
                    _visualTransform.localPosition = capsuleCollider.center;
                    _visualTransform.localRotation = GetCapsuleRotation(capsuleCollider.direction);
                    _visualTransform.localScale = new Vector3(diameter, totalHeight * 0.5f, diameter);
                    break;
                case MeshCollider meshCollider:
                    if (_visualMeshFilter != null && _visualMeshFilter.sharedMesh != meshCollider.sharedMesh)
                    {
                        _visualMeshFilter.sharedMesh = meshCollider.sharedMesh;
                    }

                    _visualTransform.localPosition = Vector3.zero;
                    _visualTransform.localRotation = Quaternion.identity;
                    _visualTransform.localScale = Vector3.one * oversize;
                    break;
            }
        }

        void ApplyVisualColor(bool highlighted)
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            var color = highlighted ? GetHighlightColor() : GetBaseColor();

            SetMaterialColor(_runtimeMaterial, color);
            SetMaterialEmission(_runtimeMaterial, color, highlighted ? 1.8f : 0.8f);
        }

        Color GetBaseColor()
        {
            if (role == VisualRole.PressZone)
            {
                return new Color(1f, 0.1f, 0.1f, 0.35f);
            }

            if (role == VisualRole.PressTrigger)
            {
                return _targetCollider is MeshCollider
                    ? new Color(0.18f, 0.86f, 1f, 0.5f)
                    : new Color(1f, 0.18f, 0.84f, 0.72f);
            }

            return _targetCollider switch
            {
                CapsuleCollider => new Color(1f, 0.15f, 0.85f, 0.45f),
                SphereCollider => new Color(1f, 0.45f, 0.1f, 0.45f),
                MeshCollider => new Color(0.1f, 0.95f, 1f, 0.35f),
                _ => new Color(0.1f, 0.95f, 1f, 0.45f)
            };
        }

        Color GetHighlightColor()
        {
            return role switch
            {
                VisualRole.PressZone => new Color(1f, 0.4f, 0.18f, 0.78f),
                VisualRole.PressTrigger when _targetCollider is MeshCollider => new Color(0.72f, 0.96f, 1f, 0.94f),
                VisualRole.PressTrigger => new Color(1f, 0.78f, 0.2f, 0.92f),
                _ => new Color(0.2f, 1f, 0.25f, 0.8f)
            };
        }

        VisualGeometryType GetGeometryType()
        {
            if (UsesDiscVisual(role) && _targetCollider is BoxCollider)
            {
                return VisualGeometryType.PressZone;
            }

            return _targetCollider switch
            {
                CapsuleCollider => VisualGeometryType.Capsule,
                SphereCollider => VisualGeometryType.Sphere,
                MeshCollider => VisualGeometryType.Mesh,
                _ => VisualGeometryType.Box
            };
        }

        float GetBaseOversize()
        {
            if (role == VisualRole.PressZone)
            {
                return PressZoneOversize;
            }

            if (role == VisualRole.PressTrigger)
            {
                return _targetCollider is MeshCollider ? PressTriggerMeshOversize : PressTriggerOversize;
            }

            return _targetCollider switch
            {
                CapsuleCollider => CapsuleOversize,
                SphereCollider => SphereOversize,
                MeshCollider => MeshOversize,
                _ => BoxOversize
            };
        }

        static PrimitiveType GetPrimitiveType(VisualGeometryType geometryType)
        {
            return geometryType switch
            {
                VisualGeometryType.Capsule => PrimitiveType.Capsule,
                VisualGeometryType.Sphere => PrimitiveType.Sphere,
                VisualGeometryType.PressZone => PrimitiveType.Cylinder,
                _ => PrimitiveType.Cube
            };
        }

        static Quaternion GetCapsuleRotation(int direction)
        {
            return direction switch
            {
                0 => Quaternion.Euler(0f, 0f, 90f),
                2 => Quaternion.Euler(90f, 0f, 0f),
                _ => Quaternion.identity
            };
        }

        static Material CreateRuntimeMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Legacy Shaders/Transparent/Diffuse") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");

            var material = new Material(shader)
            {
                name = "BigRedButton Collider Debug"
            };

            ConfigureMaterialTransparency(material);
            return material;
        }

        static void ConfigureMaterialTransparency(Material material)
        {
            if (material == null)
            {
                return;
            }

            material.enableInstancing = true;
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
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

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_EMISSION");
        }

        void ApplyMaterialPresentation()
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            var isPressTrigger = role == VisualRole.PressTrigger;
            var isPressTriggerMesh = isPressTrigger && _currentGeometry == VisualGeometryType.Mesh;
            _runtimeMaterial.renderQueue = isPressTrigger ? (int)RenderQueue.Overlay : (int)RenderQueue.Transparent;

            if (_runtimeMaterial.HasProperty("_Cull"))
            {
                _runtimeMaterial.SetFloat("_Cull", (float)CullMode.Off);
            }

            if (_runtimeMaterial.HasProperty("_QueueOffset"))
            {
                _runtimeMaterial.SetFloat("_QueueOffset", isPressTriggerMesh ? 80f : isPressTrigger ? 48f : 0f);
            }

            if (_runtimeMaterial.HasProperty("_ZTest"))
            {
                _runtimeMaterial.SetFloat("_ZTest", isPressTrigger ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
            }
        }

        static bool UsesDiscVisual(VisualRole visualRole)
        {
            return visualRole == VisualRole.PressZone || visualRole == VisualRole.PressTrigger;
        }

        static float GetDiscHeightFactor(VisualRole visualRole)
        {
            return visualRole == VisualRole.PressTrigger ? 0.35f : 0.5f;
        }

        static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        static void SetMaterialEmission(Material material, Color color, float intensity)
        {
            var emission = color * intensity;
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emission);
            }

            if (material.HasProperty("_EmissiveColor"))
            {
                material.SetColor("_EmissiveColor", emission);
            }
        }
    }
}
