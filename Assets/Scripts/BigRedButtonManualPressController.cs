using System.Collections.Generic;
using UnityEngine;
using TheBigRedButtonInstitute.VR;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BigRedButtonManualPressController : MonoBehaviour
    {
        const string PressZoneObjectName = "Button Press Surface";

        [Header("References")]
        [SerializeField] Renderer targetRenderer;
        [SerializeField] QuestVrInputManager inputManager;
        [SerializeField] bool autoResolveReferences = true;

        [Header("Press Zone")]
        [SerializeField, Range(0.3f, 1.2f)] float pressZoneRadiusScale = 0.6f;
        [SerializeField, Min(0.015f)] float minimumPressZoneRadius = 0.03f;
        [SerializeField, Min(0.004f)] float minimumPressZoneThickness = 0.012f;
        [SerializeField, Min(0.05f)] float pressCooldownSeconds = 0.25f;
        [SerializeField, Min(0f)] float exitHysteresis = 0.015f;
        [SerializeField, Min(0.1f)] float interactorRefreshIntervalSeconds = 0.5f;

        readonly HashSet<int> _activeInteractorIds = new();
        readonly HashSet<int> _frameInteractorIds = new();
        readonly Collider[] _overlapResults = new Collider[128];
        BoxCollider _pressZone;
        Transform _pressZoneTransform;
        Rigidbody _pressZoneBody;
        BigRedButtonColliderDebugVisual _pressZoneDebugVisual;
        BigRedButtonPressInteractor[] _interactors = System.Array.Empty<BigRedButtonPressInteractor>();
        float _nextAllowedPressTime;
        float _nextInteractorRefreshTime;

        public Renderer TargetRenderer => targetRenderer;

        void Reset()
        {
            targetRenderer = FindPreferredRenderer();
            ConfigurePressZoneBody();
            ConfigurePressZone();
        }

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
            ConfigurePressZoneBody();
            ConfigurePressZone();
        }

        void OnEnable()
        {
            ResolveReferences(forceRefresh: false);
            ConfigurePressZoneBody();
            ConfigurePressZone();
        }

        void OnDisable()
        {
            _activeInteractorIds.Clear();
            _frameInteractorIds.Clear();
        }

        public void ConfigureReferences(Renderer renderer, QuestVrInputManager manager = null)
        {
            targetRenderer = renderer;
            inputManager = manager;
            ConfigurePressZoneBody();
            ConfigurePressZone();
        }

        void LateUpdate()
        {
            ResolveReferences(forceRefresh: false);
            ConfigurePressZoneBody();
            ConfigurePressZone();
            EvaluatePressInteractors();
        }

        void EvaluatePressInteractors()
        {
            if (Time.unscaledTime >= _nextInteractorRefreshTime || _interactors == null || _interactors.Length == 0)
            {
                _interactors = FindObjectsByType<BigRedButtonPressInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                _nextInteractorRefreshTime = Time.unscaledTime + interactorRefreshIntervalSeconds;
            }

            if (_interactors == null || _interactors.Length == 0 || _pressZone == null || !_pressZone.enabled)
            {
                return;
            }

            _frameInteractorIds.Clear();

            for (var i = 0; i < _interactors.Length; i++)
            {
                var interactor = _interactors[i];
                if (interactor == null || !interactor.UsesBodyInteraction)
                {
                    continue;
                }

                _ = interactor.TrackingValid;
            }

            Physics.SyncTransforms();

            var zoneWorldCenter = _pressZone.transform.TransformPoint(_pressZone.center);
            var zoneRotation = _pressZone.transform.rotation;
            var zoneHalfExtents = GetWorldHalfExtents(_pressZone, exitHysteresis);
            var overlapCount = Physics.OverlapBoxNonAlloc(
                zoneWorldCenter,
                zoneHalfExtents,
                _overlapResults,
                zoneRotation,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var overlapIndex = 0; overlapIndex < overlapCount; overlapIndex++)
            {
                var overlapCollider = _overlapResults[overlapIndex];
                if (overlapCollider == null || overlapCollider == _pressZone)
                {
                    continue;
                }

                var proxy = overlapCollider.GetComponent<BigRedButtonPressColliderProxy>();
                var interactor = proxy != null ? proxy.Owner : null;
                if (interactor == null || !interactor.TrackingValid)
                {
                    continue;
                }

                proxy.MarkOverlap();
                _pressZoneDebugVisual?.MarkHighlighted();

                var interactorId = interactor.GetInstanceID();
                _frameInteractorIds.Add(interactorId);
                if (!_activeInteractorIds.Contains(interactorId))
                {
                    TryHandleInteractor(interactor);
                }
            }

            _activeInteractorIds.RemoveWhere(id => !_frameInteractorIds.Contains(id));
        }

        void TryHandleInteractor(BigRedButtonPressInteractor interactor)
        {
            if (interactor == null)
            {
                return;
            }

            var interactorId = interactor.GetInstanceID();
            if (_activeInteractorIds.Contains(interactorId))
            {
                return;
            }

            _activeInteractorIds.Add(interactorId);
            if (Time.unscaledTime < _nextAllowedPressTime)
            {
                return;
            }

            ResolveReferences(forceRefresh: false);
            if (inputManager == null)
            {
                return;
            }

            if (!inputManager.TriggerButtonPressFromRuntime())
            {
                return;
            }

            _nextAllowedPressTime = Time.unscaledTime + pressCooldownSeconds;
        }

        void ResolveReferences(bool forceRefresh)
        {
            if (targetRenderer == null || forceRefresh)
            {
                targetRenderer = FindPreferredRenderer();
            }

            if ((inputManager == null || forceRefresh) && autoResolveReferences)
            {
                inputManager = FindAnyObjectByType<QuestVrInputManager>();
            }
        }

        void ConfigurePressZone()
        {
            DisableLegacyRootPressZone();

            if (targetRenderer == null)
            {
                targetRenderer = FindPreferredRenderer();
            }

            if (targetRenderer == null)
            {
                DisablePressZone();
                return;
            }

            EnsurePressZoneObject();
            if (_pressZone == null)
            {
                return;
            }

            var rootLossyScale = transform.lossyScale;
            var scaleX = Mathf.Max(0.0001f, Mathf.Abs(rootLossyScale.x));
            var scaleY = Mathf.Max(0.0001f, Mathf.Abs(rootLossyScale.y));
            var scaleZ = Mathf.Max(0.0001f, Mathf.Abs(rootLossyScale.z));
            var targetBounds = targetRenderer.bounds;
            var worldRadius = Mathf.Max(
                minimumPressZoneRadius,
                Mathf.Max(targetBounds.extents.x, targetBounds.extents.z) * pressZoneRadiusScale);
            var worldThickness = Mathf.Max(
                minimumPressZoneThickness,
                targetBounds.size.y * 0.14f);

            var worldCenter = new Vector3(
                targetBounds.center.x,
                targetBounds.max.y - (worldThickness * 0.5f),
                targetBounds.center.z);

            _pressZoneTransform.localPosition = transform.InverseTransformPoint(worldCenter);
            // TODO: Rotate the press surface to match the button cap's visible tilt instead of assuming the cap is upright.
            _pressZoneTransform.localRotation = Quaternion.identity;
            _pressZone.center = Vector3.zero;
            _pressZone.size = new Vector3(
                (worldRadius * 2f) / scaleX,
                worldThickness / scaleY,
                (worldRadius * 2f) / scaleZ);
            _pressZone.enabled = true;
            _pressZone.isTrigger = true;

            EnsurePressZoneDebugVisual();
        }

        void ConfigurePressZoneBody()
        {
            _pressZoneBody ??= GetComponent<Rigidbody>();
            if (_pressZoneBody == null)
            {
                return;
            }

            _pressZoneBody.isKinematic = true;
            _pressZoneBody.useGravity = false;
            _pressZoneBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        void EnsurePressZoneObject()
        {
            _pressZoneTransform ??= transform.Find(PressZoneObjectName);
            if (_pressZoneTransform == null)
            {
                var zoneObject = new GameObject(PressZoneObjectName);
                _pressZoneTransform = zoneObject.transform;
                _pressZoneTransform.SetParent(transform, false);
            }
            else if (_pressZoneTransform.parent != transform)
            {
                _pressZoneTransform.SetParent(transform, false);
            }

            _pressZoneTransform.localPosition = Vector3.zero;
            _pressZoneTransform.localRotation = Quaternion.identity;
            _pressZoneTransform.localScale = Vector3.one;

            _pressZone = _pressZoneTransform.GetComponent<BoxCollider>();
            if (_pressZone == null)
            {
                _pressZone = _pressZoneTransform.gameObject.AddComponent<BoxCollider>();
            }
        }

        void EnsurePressZoneDebugVisual()
        {
            if (_pressZone == null)
            {
                return;
            }

            _pressZoneDebugVisual ??= _pressZone.GetComponent<BigRedButtonColliderDebugVisual>();
            if (_pressZoneDebugVisual == null)
            {
                _pressZoneDebugVisual = _pressZone.gameObject.AddComponent<BigRedButtonColliderDebugVisual>();
            }

            _pressZoneDebugVisual.enabled = true;
            _pressZoneDebugVisual.Configure(BigRedButtonColliderDebugVisual.VisualRole.PressZone);
        }

        void DisableLegacyRootPressZone()
        {
            var legacySphere = GetComponent<SphereCollider>();
            if (legacySphere != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(legacySphere);
                }
                else
                {
                    DestroyImmediate(legacySphere);
                }
            }

            var legacyDebugVisual = GetComponent<BigRedButtonColliderDebugVisual>();
            if (legacyDebugVisual != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(legacyDebugVisual);
                }
                else
                {
                    DestroyImmediate(legacyDebugVisual);
                }
            }
        }

        void DisablePressZone()
        {
            if (_pressZone != null)
            {
                _pressZone.enabled = false;
            }
        }

        Renderer FindPreferredRenderer()
        {
            Renderer fallback = null;
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.GetComponentInParent<BigRedButtonColliderDebugVisual>() != null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = renderer;
                }

                if (renderer.gameObject.name == "button")
                {
                    return renderer;
                }
            }

            return fallback;
        }

        static Vector3 GetWorldHalfExtents(BoxCollider collider, float padding)
        {
            var lossyScale = collider.transform.lossyScale;
            var halfSize = collider.size * 0.5f;
            return new Vector3(
                Mathf.Abs(lossyScale.x) * halfSize.x + padding,
                Mathf.Abs(lossyScale.y) * halfSize.y + padding,
                Mathf.Abs(lossyScale.z) * halfSize.z + padding);
        }
    }
}
