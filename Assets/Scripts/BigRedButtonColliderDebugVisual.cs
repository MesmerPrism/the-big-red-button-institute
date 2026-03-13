using UnityEngine;
using UnityEngine.Rendering;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonColliderDebugVisual : MonoBehaviour
    {
        public enum VisualRole
        {
            Interactor = 0,
            PressZone = 1
        }

        const float HighlightHoldSeconds = 0.12f;
        const float BoxOversize = 1.08f;
        const float CapsuleOversize = 1.02f;
        const float SphereOversize = 1.03f;
        const float PressZoneOversize = 1.01f;

        [SerializeField] VisualRole role = VisualRole.Interactor;

        Collider _targetCollider;
        BigRedButtonPressColliderProxy _proxy;
        Transform _visualTransform;
        MeshRenderer _visualRenderer;
        Material _runtimeMaterial;
        PrimitiveType _currentPrimitiveType = (PrimitiveType)(-1);
        float _lastHighlightTime = float.NegativeInfinity;

        public void Configure(VisualRole targetRole)
        {
            role = targetRole;
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

            var primitiveType = GetPrimitiveType();
            if (_visualTransform == null || primitiveType != _currentPrimitiveType)
            {
                RebuildVisual(primitiveType);
            }

            if (_runtimeMaterial == null)
            {
                _runtimeMaterial = CreateRuntimeMaterial();
                if (_visualRenderer != null)
                {
                    _visualRenderer.sharedMaterial = _runtimeMaterial;
                }
            }
        }

        void RebuildVisual(PrimitiveType primitiveType)
        {
            DestroyVisual();

            var primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = "Collider Debug Visual";
            primitive.layer = gameObject.layer;
            primitive.transform.SetParent(transform, false);

            var primitiveCollider = primitive.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                primitiveCollider.enabled = false;
            }

            _visualTransform = primitive.transform;
            _visualRenderer = primitive.GetComponent<MeshRenderer>();
            _currentPrimitiveType = primitiveType;

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
            _currentPrimitiveType = (PrimitiveType)(-1);
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
            _visualRenderer.enabled = _targetCollider.enabled && gameObject.activeInHierarchy;
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
                    _visualTransform.localScale = role == VisualRole.PressZone
                        ? new Vector3(boxCollider.size.x * oversize, boxCollider.size.y * 0.5f * oversize, boxCollider.size.z * oversize)
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
            }
        }

        void ApplyVisualColor(bool highlighted)
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            var color = GetBaseColor();
            if (highlighted)
            {
                color = role == VisualRole.PressZone
                    ? new Color(1f, 0.95f, 0.15f, 0.8f)
                    : new Color(0.2f, 1f, 0.25f, 0.8f);
            }

            SetMaterialColor(_runtimeMaterial, color);
            SetMaterialEmission(_runtimeMaterial, color, highlighted ? 1.8f : 0.8f);
        }

        Color GetBaseColor()
        {
            if (role == VisualRole.PressZone)
            {
                return new Color(1f, 0.1f, 0.1f, 0.35f);
            }

            return _targetCollider switch
            {
                CapsuleCollider => new Color(1f, 0.15f, 0.85f, 0.45f),
                SphereCollider => new Color(1f, 0.45f, 0.1f, 0.45f),
                _ => new Color(0.1f, 0.95f, 1f, 0.45f)
            };
        }

        PrimitiveType GetPrimitiveType()
        {
            if (role == VisualRole.PressZone)
            {
                return PrimitiveType.Cylinder;
            }

            return _targetCollider switch
            {
                CapsuleCollider => PrimitiveType.Capsule,
                SphereCollider => PrimitiveType.Sphere,
                _ => PrimitiveType.Cube
            };
        }

        float GetBaseOversize()
        {
            if (role == VisualRole.PressZone)
            {
                return PressZoneOversize;
            }

            return _targetCollider switch
            {
                CapsuleCollider => CapsuleOversize,
                SphereCollider => SphereOversize,
                _ => BoxOversize
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
