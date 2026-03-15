using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using TheBigRedButtonInstitute.VR;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BigRedButtonManualPressController : MonoBehaviour
    {
        const string LegacyPressZoneObjectName = "Button Press Surface";
        const string TriggerSurfaceObjectName = "Button Trigger Surface";

        [Header("References")]
        [SerializeField] Renderer targetRenderer;
        [SerializeField] Renderer pressTriggerRenderer;
        [SerializeField] QuestVrInputManager inputManager;
        [SerializeField] bool autoResolveReferences = true;

        [Header("Timing")]
        [SerializeField, Min(0.05f)] float pressCooldownSeconds = 0.25f;
        [SerializeField, Min(0f)] float startupContactSuppressionSeconds = 0.35f;
        [SerializeField, Min(0.1f)] float interactorRefreshIntervalSeconds = 0.5f;

        [Header("Press Mesh")]
        [SerializeField] bool usePressMeshCollider = true;
        [SerializeField] bool preferConvexPressMeshCollider = true;
        [SerializeField, Min(0f)] float pressTriggerMeshInflation;
        [SerializeField, Min(0f)] float minimumPressPenetration = 0.0015f;
        [SerializeField, Min(0f)] float pressMeshContactTolerance = 0.001f;
        [SerializeField, Min(0f)] float pressTriggerSurfaceContactTolerance = 0.0004f;

        [Header("Trigger Surface Alignment")]
        [SerializeField] bool triggerSurfaceAlignmentMode = false;
        [SerializeField] bool useTriggerSurfacePoseOverride = false;
        [SerializeField] bool triggerSurfacePoseOverrideIsOffset = false;
        [SerializeField] Vector3 triggerSurfaceLocalPositionOverride;
        [SerializeField] Vector3 triggerSurfaceLocalEulerOverride = new(0f, 180f, 0f);
        [SerializeField] bool useTriggerSurfaceSizeOverride = false;
        [SerializeField] Vector3 triggerSurfaceLocalSizeOverride;
        [SerializeField, Range(0.8f, 1.1f)] float triggerSurfaceDiameterScale = 1f;
        [SerializeField] bool enableTriggerSurfacePressFallback = false;

        [Header("Trigger Collider Alignment")]
        [SerializeField] bool alignTriggerColliderToSurface = true;
        [SerializeField] Vector3 triggerColliderDerivedLocalOffset;
        [SerializeField] Vector3 triggerColliderManualLocalOffset;

        [Header("Debug")]
        [SerializeField] bool logPressCollisionDiagnostics = true;
        [SerializeField, Min(0.05f)] float pressCollisionDiagnosticCooldownSeconds = 0.2f;

        readonly HashSet<int> _activeInteractorIds = new();
        readonly HashSet<int> _frameInteractorIds = new();
        BigRedButtonGeneratedBodyCollider _baseColliderGenerator;
        BigRedButtonGeneratedBodyCollider _triggerColliderGenerator;
        MeshCollider _baseCollider;
        MeshCollider _pressTriggerCollider;
        BoxCollider _pressTriggerSurfaceCollider;
        BigRedButtonColliderDebugVisual _baseDebugVisual;
        BigRedButtonColliderDebugVisual _pressTriggerDebugVisual;
        BigRedButtonColliderDebugVisual _pressTriggerSurfaceDebugVisual;
        BigRedButtonRendererMeshDebug _pressTriggerRendererShell;
        BigRedButtonTriggerSurfaceAlignmentTool _pressTriggerSurfaceAlignmentTool;
        BigRedButtonPressInteractor[] _interactors = Array.Empty<BigRedButtonPressInteractor>();
        Rigidbody _body;
        Mesh _triggerSurfaceFitMesh;
        float _nextAllowedPressTime;
        float _nextInteractorRefreshTime;
        float _entrySuppressionEndTime;
        float _nextPressCollisionDiagnosticTime;
        Vector3 _runtimeTriggerSurfaceLocalOffset;
        Quaternion _runtimeTriggerSurfaceRotationOffset = Quaternion.identity;
        bool _hasRuntimeTriggerSurfaceOffset;
        bool _hasLoggedRuntimeSetup;
        bool _pressArmed;

        public Renderer TargetRenderer => targetRenderer;
        public Renderer PressTriggerRenderer => pressTriggerRenderer != null ? pressTriggerRenderer : targetRenderer;
        public bool TriggerSurfaceAlignmentModeEnabled => triggerSurfaceAlignmentMode;
        public bool ConsumesRightIndexTriggerInput => triggerSurfaceAlignmentMode;

        public void SetTriggerSurfacePoseOverride(Vector3 localPosition, Vector3 localEulerAngles)
        {
            useTriggerSurfacePoseOverride = true;
            triggerSurfacePoseOverrideIsOffset = false;
            triggerSurfaceLocalPositionOverride = localPosition;
            triggerSurfaceLocalEulerOverride = localEulerAngles;
            ResetTriggerSurfacePoseOffsetCache();
        }

        public void SetTriggerSurfaceSizeOverride(Vector3 localSize)
        {
            useTriggerSurfaceSizeOverride = true;
            triggerSurfaceLocalSizeOverride = SanitizeTriggerSurfaceLocalSize(localSize);
        }

        public void SetTriggerColliderManualLocalOffset(Vector3 localOffset)
        {
            triggerColliderManualLocalOffset = localOffset;
        }

        void Reset()
        {
            ResolveReferences(forceRefresh: true);
            ConfigureBody();
            RebuildConfiguredPressGeometryInEditor();
        }

        void Awake()
        {
            ResolveReferences(forceRefresh: true);
            ConfigureBody();
            ResolveConfiguredColliders();
        }

        void OnEnable()
        {
            _activeInteractorIds.Clear();
            _frameInteractorIds.Clear();
            ResetTriggerSurfacePoseOffsetCache();
            _hasLoggedRuntimeSetup = false;
            ResolveReferences(forceRefresh: false);
            ConfigureBody();
            ResolveConfiguredColliders();
            _entrySuppressionEndTime = Time.unscaledTime + startupContactSuppressionSeconds;
            _pressArmed = false;
        }

        void OnDisable()
        {
            _activeInteractorIds.Clear();
            _frameInteractorIds.Clear();
            ResetTriggerSurfacePoseOffsetCache();
            _hasLoggedRuntimeSetup = false;
            _pressArmed = false;
        }

        void OnDestroy()
        {
            if (_triggerSurfaceFitMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_triggerSurfaceFitMesh);
            }
            else
            {
                DestroyImmediate(_triggerSurfaceFitMesh);
            }
        }

        public void ConfigureReferences(Renderer renderer, QuestVrInputManager manager = null)
        {
            ConfigureReferences(renderer, null, manager);
        }

        public void ConfigureReferences(Renderer renderer, Renderer triggerRenderer, QuestVrInputManager manager = null)
        {
            targetRenderer = renderer;
            pressTriggerRenderer = triggerRenderer;
            inputManager = manager;
            ConfigureBody();
            if (Application.isPlaying)
            {
                ResolveConfiguredColliders();
                return;
            }

            RebuildConfiguredPressGeometryInEditor();
        }

        public void RebuildConfiguredPressGeometryInEditor()
        {
            if (Application.isPlaying)
            {
                ResolveConfiguredColliders();
                return;
            }

            ResolveReferences(forceRefresh: false);
            RemoveLegacyPressGeometry();
            RefreshColliders();
        }

        void LateUpdate()
        {
            ResolveReferences(forceRefresh: false);
            ConfigureBody();
            ResolveConfiguredColliders();
            SyncAnimatedPressGeometry();
            TryLogRuntimeSetup();
            if (!triggerSurfaceAlignmentMode)
            {
                EvaluatePressInteractors();
            }
        }

        void EvaluatePressInteractors()
        {
            if (Time.unscaledTime >= _nextInteractorRefreshTime || _interactors == null || _interactors.Length == 0)
            {
                _interactors = FindObjectsByType<BigRedButtonPressInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                _nextInteractorRefreshTime = Time.unscaledTime + interactorRefreshIntervalSeconds;
            }

            var hasPressTriggerMesh = HasUsablePressTriggerMesh();
            var hasTriggerSurface = HasUsableTriggerSurfaceFallback();
            if (_interactors == null ||
                _interactors.Length == 0 ||
                (!hasPressTriggerMesh && !hasTriggerSurface))
            {
                return;
            }

            _frameInteractorIds.Clear();
            if (!_pressArmed && Time.unscaledTime >= _entrySuppressionEndTime)
            {
                _pressArmed = true;
                _activeInteractorIds.Clear();
            }

            for (var i = 0; i < _interactors.Length; i++)
            {
                var interactor = _interactors[i];
                if (interactor == null || !interactor.UsesBodyInteraction)
                {
                    continue;
                }

                interactor.PrepareForQueries();
            }

            Physics.SyncTransforms();

            for (var i = 0; i < _interactors.Length; i++)
            {
                var interactor = _interactors[i];
                if (interactor == null || !interactor.UsesBodyInteraction || !interactor.TrackingValid)
                {
                    continue;
                }

                var interactorColliders = interactor.GetInteractionColliders();
                var hasUsableCollider = false;
                var interactorHit = false;
                var diagnosticHitCollider = default(Collider);
                var diagnosticMeshHit = false;
                var diagnosticSurfaceHit = false;
                for (var colliderIndex = 0; colliderIndex < interactorColliders.Length; colliderIndex++)
                {
                    var interactorCollider = interactorColliders[colliderIndex];
                    if (!IsColliderUsable(interactorCollider))
                    {
                        continue;
                    }

                    hasUsableCollider = true;
                    var meshHit = IsMeshTriggerHit(interactorCollider);
                    var surfaceHit = IsTriggerSurfaceHit(interactorCollider);
                    if (!meshHit && !surfaceHit)
                    {
                        continue;
                    }

                    MarkColliderOverlap(interactorCollider);
                    interactorHit = true;
                    diagnosticHitCollider ??= interactorCollider;
                    diagnosticMeshHit |= meshHit;
                    diagnosticSurfaceHit |= surfaceHit;
                }

                if (!hasUsableCollider)
                {
                    continue;
                }

                var interactorId = interactor.GetInstanceID();
                if (!interactorHit)
                {
                    continue;
                }

                _pressTriggerDebugVisual?.MarkHighlighted();
                if (enableTriggerSurfacePressFallback)
                {
                    _pressTriggerSurfaceDebugVisual?.MarkHighlighted();
                }
                _pressTriggerRendererShell?.MarkHighlighted();

                _frameInteractorIds.Add(interactorId);
                if (!_activeInteractorIds.Contains(interactorId))
                {
                    TryLogPressCollisionDiagnostics(interactor, diagnosticHitCollider, diagnosticMeshHit, diagnosticSurfaceHit);
                    if (!_pressArmed)
                    {
                        _activeInteractorIds.Add(interactorId);
                        continue;
                    }

                    TryHandleInteractor(interactor, diagnosticHitCollider, diagnosticMeshHit, diagnosticSurfaceHit);
                }
            }

            _activeInteractorIds.RemoveWhere(id => !_frameInteractorIds.Contains(id));
        }

        void TryHandleInteractor(
            BigRedButtonPressInteractor interactor,
            Collider interactorCollider,
            bool meshHit,
            bool surfaceHit)
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
            if (Time.unscaledTime < _entrySuppressionEndTime || Time.unscaledTime < _nextAllowedPressTime)
            {
                return;
            }

            ResolveReferences(forceRefresh: false);
            if (inputManager == null || !inputManager.TriggerButtonPressFromRuntime())
            {
                TryLogPressDispatchFailure(interactor);
                return;
            }

            Debug.LogWarning(
                $"[BigRedButtonPressDispatch] interactor={BuildTransformPath(interactor.transform)} " +
                $"collider={DescribeCollider(interactorCollider)} meshHit={(meshHit ? 1 : 0)} surfaceHit={(surfaceHit ? 1 : 0)} " +
                $"triggerMesh={DescribeCollider(_pressTriggerCollider)} triggerSurface={DescribeCollider(_pressTriggerSurfaceCollider)}");

            _nextAllowedPressTime = Time.unscaledTime + pressCooldownSeconds;
        }

        void ResolveReferences(bool forceRefresh)
        {
            if ((pressTriggerRenderer == null || forceRefresh) && autoResolveReferences)
            {
                pressTriggerRenderer = FindTriggerRenderer();
            }

            if ((targetRenderer == null || forceRefresh || ReferenceEquals(targetRenderer, pressTriggerRenderer)) && autoResolveReferences)
            {
                targetRenderer = FindPassiveRenderer(pressTriggerRenderer);
            }

            targetRenderer ??= pressTriggerRenderer;
            pressTriggerRenderer ??= targetRenderer;

            if ((inputManager == null || forceRefresh) && autoResolveReferences)
            {
                inputManager = FindAnyObjectByType<QuestVrInputManager>();
            }
        }

        void RefreshColliders()
        {
            if (!usePressMeshCollider)
            {
                DisableColliderSurface(ref _baseColliderGenerator, ref _baseDebugVisual);
                DisableColliderSurface(ref _triggerColliderGenerator, ref _pressTriggerDebugVisual);
                DisableTriggerSurface();
                DisableRendererShell(PressTriggerRenderer);
                _pressTriggerRendererShell = null;
                _pressTriggerSurfaceAlignmentTool = null;
                _baseCollider = null;
                _pressTriggerCollider = null;
                _pressTriggerSurfaceCollider = null;
                return;
            }

            var resolvedTriggerRenderer = PressTriggerRenderer;
            if (ReferenceEquals(targetRenderer, resolvedTriggerRenderer))
            {
                _baseCollider = RefreshColliderSurface(
                    resolvedTriggerRenderer,
                    ref _baseColliderGenerator,
                    ref _baseDebugVisual,
                    BigRedButtonColliderDebugVisual.VisualRole.PressTrigger);
                _pressTriggerCollider = _baseCollider;
                _triggerColliderGenerator = _baseColliderGenerator;
                _pressTriggerDebugVisual = _baseDebugVisual;
                DisableTriggerSurface();
                _pressTriggerRendererShell = RefreshRendererShell(resolvedTriggerRenderer, _pressTriggerCollider);
                return;
            }

            _baseCollider = RefreshColliderSurface(
                targetRenderer,
                ref _baseColliderGenerator,
                ref _baseDebugVisual,
                BigRedButtonColliderDebugVisual.VisualRole.PressZone);
            _pressTriggerCollider = RefreshColliderSurface(
                resolvedTriggerRenderer,
                ref _triggerColliderGenerator,
                ref _pressTriggerDebugVisual,
                BigRedButtonColliderDebugVisual.VisualRole.PressTrigger);
            _pressTriggerSurfaceCollider = RefreshTriggerSurface(resolvedTriggerRenderer, ref _pressTriggerSurfaceDebugVisual);
            ApplyTriggerSurfaceActivationState();
            _pressTriggerRendererShell = RefreshRendererShell(resolvedTriggerRenderer, _pressTriggerCollider);
        }

        void ResolveConfiguredColliders()
        {
            if (!usePressMeshCollider)
            {
                _baseColliderGenerator = null;
                _triggerColliderGenerator = null;
                _baseCollider = null;
                _pressTriggerCollider = null;
                _pressTriggerSurfaceCollider = null;
                _baseDebugVisual = null;
                _pressTriggerDebugVisual = null;
                _pressTriggerSurfaceDebugVisual = null;
                _pressTriggerRendererShell = null;
                _pressTriggerSurfaceAlignmentTool = null;
                return;
            }

            var resolvedTriggerRenderer = PressTriggerRenderer;
            if (ReferenceEquals(targetRenderer, resolvedTriggerRenderer))
            {
                _baseCollider = ResolveConfiguredColliderSurface(
                    resolvedTriggerRenderer,
                    ref _baseColliderGenerator,
                    ref _baseDebugVisual);
                _pressTriggerCollider = _baseCollider;
                _triggerColliderGenerator = _baseColliderGenerator;
                _pressTriggerDebugVisual = _baseDebugVisual;
                _pressTriggerSurfaceCollider = null;
                _pressTriggerSurfaceDebugVisual = null;
                _pressTriggerSurfaceAlignmentTool = null;
                _pressTriggerRendererShell = ResolveConfiguredRendererShell(resolvedTriggerRenderer, _pressTriggerCollider);
                return;
            }

            _baseCollider = ResolveConfiguredColliderSurface(
                targetRenderer,
                ref _baseColliderGenerator,
                ref _baseDebugVisual);
            _pressTriggerCollider = ResolveConfiguredColliderSurface(
                resolvedTriggerRenderer,
                ref _triggerColliderGenerator,
                ref _pressTriggerDebugVisual);
            _pressTriggerSurfaceCollider = ResolveConfiguredTriggerSurface(ref _pressTriggerSurfaceDebugVisual);
            ApplyTriggerSurfaceActivationState();
            _pressTriggerRendererShell = ResolveConfiguredRendererShell(resolvedTriggerRenderer, _pressTriggerCollider);
        }

        void SyncAnimatedPressGeometry()
        {
            if (!Application.isPlaying || !usePressMeshCollider)
            {
                return;
            }

            var resolvedTriggerRenderer = PressTriggerRenderer;
            if (ReferenceEquals(targetRenderer, resolvedTriggerRenderer))
            {
                SyncAnimatedCollider(
                    resolvedTriggerRenderer,
                    _baseColliderGenerator,
                    ref _baseCollider,
                    ref _baseDebugVisual);
                _pressTriggerCollider = _baseCollider;
                _triggerColliderGenerator = _baseColliderGenerator;
                _pressTriggerDebugVisual = _baseDebugVisual;
                return;
            }

            SyncAnimatedCollider(
                targetRenderer,
                _baseColliderGenerator,
                ref _baseCollider,
                ref _baseDebugVisual);
            SyncAnimatedCollider(
                resolvedTriggerRenderer,
                _triggerColliderGenerator,
                ref _pressTriggerCollider,
                ref _pressTriggerDebugVisual);

            if (IsRendererUsable(resolvedTriggerRenderer) && _pressTriggerSurfaceCollider != null)
            {
                ConfigureTriggerSurfaceAlignmentTool(_pressTriggerSurfaceCollider.transform);
                if (!triggerSurfaceAlignmentMode)
                {
                    UpdateTriggerSurfaceBounds(
                        resolvedTriggerRenderer,
                        _pressTriggerSurfaceCollider.transform,
                        _pressTriggerSurfaceCollider);
                }

                SyncTriggerColliderAlignmentFromSurface(
                    resolvedTriggerRenderer,
                    _triggerColliderGenerator,
                    _pressTriggerSurfaceCollider.transform);
                ApplyTriggerSurfaceActivationState();
            }
            else
            {
                _triggerColliderGenerator?.SetColliderHostLocalPositionOffset(triggerColliderManualLocalOffset);
            }
        }

        static void SyncAnimatedCollider(
            Renderer renderer,
            BigRedButtonGeneratedBodyCollider colliderGenerator,
            ref MeshCollider collider,
            ref BigRedButtonColliderDebugVisual debugVisual)
        {
            if (renderer is not SkinnedMeshRenderer || colliderGenerator == null)
            {
                return;
            }

            collider = colliderGenerator.RefreshCollider();
            if (collider == null)
            {
                if (debugVisual != null)
                {
                    debugVisual.enabled = false;
                }

                return;
            }

            if (debugVisual == null || debugVisual.gameObject != collider.gameObject)
            {
                if (debugVisual != null)
                {
                    debugVisual.enabled = false;
                }

                debugVisual = collider.GetComponent<BigRedButtonColliderDebugVisual>();
                if (debugVisual == null)
                {
                    debugVisual = collider.gameObject.AddComponent<BigRedButtonColliderDebugVisual>();
                    debugVisual.Configure(BigRedButtonColliderDebugVisual.VisualRole.PressTrigger);
                }
            }

            if (debugVisual != null)
            {
                debugVisual.enabled = true;
            }
        }

        void SyncTriggerColliderAlignmentFromSurface(
            Renderer renderer,
            BigRedButtonGeneratedBodyCollider colliderGenerator,
            Transform surfaceTransform)
        {
            if (colliderGenerator == null)
            {
                return;
            }

            // Keep normal play locked to the authored cap offset. Only calibration mode
            // recomputes the derived correction from the movable trigger surface.
            if (triggerSurfaceAlignmentMode &&
                renderer != null &&
                surfaceTransform != null &&
                TryGetDefaultTriggerSurfacePose(
                    renderer,
                    out var baseCenterWorld,
                    out _,
                    out _,
                    out _))
            {
                var correctionWorld = surfaceTransform.position - baseCenterWorld;
                triggerColliderDerivedLocalOffset = renderer.transform.InverseTransformVector(correctionWorld);
            }

            var finalLocalOffset = (alignTriggerColliderToSurface ? triggerColliderDerivedLocalOffset : Vector3.zero) +
                triggerColliderManualLocalOffset;
            colliderGenerator.SetColliderHostLocalPositionOffset(finalLocalOffset);
        }

        MeshCollider RefreshColliderSurface(
            Renderer renderer,
            ref BigRedButtonGeneratedBodyCollider colliderGenerator,
            ref BigRedButtonColliderDebugVisual debugVisual,
            BigRedButtonColliderDebugVisual.VisualRole visualRole)
        {
            if (!IsRendererUsable(renderer))
            {
                DisableColliderSurface(ref colliderGenerator, ref debugVisual);
                return null;
            }

            if (colliderGenerator == null || colliderGenerator.SourceRenderer != renderer)
            {
                colliderGenerator = renderer.GetComponent<BigRedButtonGeneratedBodyCollider>();
                if (colliderGenerator == null)
                {
                    colliderGenerator = renderer.gameObject.AddComponent<BigRedButtonGeneratedBodyCollider>();
                }
            }

            colliderGenerator.Configure(
                renderer,
                targetPreferConvex: preferConvexPressMeshCollider,
                targetIsTrigger: false,
                targetSurfaceInflation: visualRole == BigRedButtonColliderDebugVisual.VisualRole.PressTrigger ? pressTriggerMeshInflation : 0f);

            if (!Application.isPlaying && renderer is SkinnedMeshRenderer)
            {
                colliderGenerator.CaptureEditorSnapshot();
            }

            var collider = colliderGenerator.RefreshCollider();
            if (collider == null)
            {
                DisableColliderSurface(ref colliderGenerator, ref debugVisual);
                return null;
            }

            if (debugVisual == null || debugVisual.gameObject != collider.gameObject)
            {
                if (debugVisual != null)
                {
                    debugVisual.enabled = false;
                }

                debugVisual = collider.GetComponent<BigRedButtonColliderDebugVisual>();
                if (debugVisual == null)
                {
                    debugVisual = collider.gameObject.AddComponent<BigRedButtonColliderDebugVisual>();
                }
            }

            debugVisual.enabled = true;
            debugVisual.Configure(visualRole);
            return collider;
        }

        static MeshCollider ResolveConfiguredColliderSurface(
            Renderer renderer,
            ref BigRedButtonGeneratedBodyCollider colliderGenerator,
            ref BigRedButtonColliderDebugVisual debugVisual)
        {
            if (renderer == null)
            {
                colliderGenerator = null;
                debugVisual = null;
                return null;
            }

            colliderGenerator = renderer.GetComponent<BigRedButtonGeneratedBodyCollider>();
            var collider = renderer.GetComponentInChildren<MeshCollider>(true);
            debugVisual = collider != null ? collider.GetComponent<BigRedButtonColliderDebugVisual>() : null;
            return IsColliderUsable(collider) && collider.sharedMesh != null ? collider : null;
        }

        bool IsPressTriggerHit(Collider interactorCollider)
        {
            if (!IsColliderUsable(interactorCollider))
            {
                return false;
            }

            return IsMeshTriggerHit(interactorCollider) || IsTriggerSurfaceHit(interactorCollider);
        }

        bool HasUsablePressTriggerMesh()
        {
            return IsColliderUsable(_pressTriggerCollider) && _pressTriggerCollider.sharedMesh != null;
        }

        bool IsMeshTriggerHit(Collider interactorCollider)
        {
            if (!IsColliderUsable(interactorCollider) || !HasUsablePressTriggerMesh())
            {
                return false;
            }

            if (_pressTriggerCollider.convex)
            {
                return Physics.ComputePenetration(
                        _pressTriggerCollider,
                        _pressTriggerCollider.transform.position,
                        _pressTriggerCollider.transform.rotation,
                        interactorCollider,
                        interactorCollider.transform.position,
                        interactorCollider.transform.rotation,
                        out _,
                        out var penetrationDistance) &&
                    penetrationDistance >= minimumPressPenetration;
            }

            return AreCollidersWithinTolerance(_pressTriggerCollider, interactorCollider, pressMeshContactTolerance);
        }

        bool IsTriggerSurfaceHit(Collider interactorCollider)
        {
            if (!HasUsableTriggerSurfaceFallback() || !IsColliderUsable(interactorCollider))
            {
                return false;
            }

            return AreCollidersWithinTolerance(
                _pressTriggerSurfaceCollider,
                interactorCollider,
                pressTriggerSurfaceContactTolerance);
        }

        void ConfigureBody()
        {
            _body ??= GetComponent<Rigidbody>();
            if (_body == null)
            {
                return;
            }

            _body.isKinematic = true;
            _body.useGravity = false;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        void MarkColliderOverlap(Collider interactorCollider)
        {
            var proxy = interactorCollider != null ? interactorCollider.GetComponent<BigRedButtonPressColliderProxy>() : null;
            proxy?.MarkOverlap();

            var debugVisual = interactorCollider != null ? interactorCollider.GetComponent<BigRedButtonColliderDebugVisual>() : null;
            debugVisual?.MarkHighlighted();
        }

        void RemoveLegacyPressGeometry()
        {
            RemoveLegacyRootPressZone();
            RemoveLegacyPressZoneObject();
        }

        void RemoveLegacyRootPressZone()
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

        void RemoveLegacyPressZoneObject()
        {
            var legacyPressZone = transform.Find(LegacyPressZoneObjectName);
            if (legacyPressZone == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(legacyPressZone.gameObject);
            }
            else
            {
                DestroyImmediate(legacyPressZone.gameObject);
            }
        }

        Renderer FindTriggerRenderer()
        {
            var blinkController = GetComponent<BigRedButtonBlinkController>();
            if (blinkController != null && IsRendererCandidate(blinkController.TargetRenderer, null))
            {
                return blinkController.TargetRenderer;
            }

            var namedTriggerRenderer = FindRendererOnNamedChild("button");
            if (IsRendererCandidate(namedTriggerRenderer, null))
            {
                return namedTriggerRenderer;
            }

            return FindNamedRenderer("button", preferSkinnedRenderer: true);
        }

        Renderer FindPassiveRenderer(Renderer excludedRenderer)
        {
            var namedPassiveRenderer = FindRendererOnNamedChild("stand1") ??
                FindRendererOnNamedChild("stand") ??
                FindRendererOnNamedChild("base");
            if (IsRendererCandidate(namedPassiveRenderer, excludedRenderer))
            {
                return namedPassiveRenderer;
            }

            return FindNamedRenderer("stand", preferSkinnedRenderer: false, excludedRenderer) ??
                FindNamedRenderer("base", preferSkinnedRenderer: false, excludedRenderer) ??
                FindFirstRenderer(preferSkinnedRenderer: false, excludedRenderer) ??
                FindFirstRenderer(preferSkinnedRenderer: true, excludedRenderer);
        }

        Renderer FindNamedRenderer(string token, bool preferSkinnedRenderer, Renderer excludedRenderer = null)
        {
            Renderer fallback = null;
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!IsRendererCandidate(renderer, excludedRenderer))
                {
                    continue;
                }

                if (fallback == null && IsRendererTypeMatch(renderer, preferSkinnedRenderer))
                {
                    fallback = renderer;
                }

                var objectName = renderer.gameObject.name;
                if (string.Equals(objectName, token, StringComparison.OrdinalIgnoreCase) ||
                    objectName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (IsRendererTypeMatch(renderer, preferSkinnedRenderer))
                    {
                        return renderer;
                    }

                    fallback ??= renderer;
                }
            }

            return fallback;
        }

        Renderer FindFirstRenderer(bool preferSkinnedRenderer, Renderer excludedRenderer)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (IsRendererCandidate(renderer, excludedRenderer) && IsRendererTypeMatch(renderer, preferSkinnedRenderer))
                {
                    return renderer;
                }
            }

            return null;
        }

        Renderer FindRendererOnNamedChild(string childName)
        {
            var childTransform = FindDescendantByName(transform, childName);
            return childTransform != null ? childTransform.GetComponent<Renderer>() : null;
        }

        static bool AreCollidersWithinTolerance(Collider a, Collider b, float tolerance)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (TryComputeColliderPenetration(a, b, out var penetrationDistance))
            {
                return penetrationDistance > 0f || penetrationDistance >= tolerance;
            }

            return AreBoundsWithinTolerance(a.bounds, b.bounds, tolerance);
        }

        static bool TryComputeColliderPenetration(Collider a, Collider b, out float penetrationDistance)
        {
            penetrationDistance = 0f;
            if (!CanUseComputePenetration(a) && !CanUseComputePenetration(b))
            {
                return false;
            }

            return Physics.ComputePenetration(
                a,
                a.transform.position,
                a.transform.rotation,
                b,
                b.transform.position,
                b.transform.rotation,
                out _,
                out penetrationDistance);
        }

        static bool CanUseComputePenetration(Collider collider)
        {
            return collider switch
            {
                BoxCollider => true,
                SphereCollider => true,
                CapsuleCollider => true,
                MeshCollider meshCollider => meshCollider.convex,
                _ => false
            };
        }

        static bool AreBoundsWithinTolerance(Bounds aBounds, Bounds bBounds, float tolerance)
        {
            if (aBounds.Intersects(bBounds))
            {
                return true;
            }

            var expandedBounds = aBounds;
            expandedBounds.Expand(Mathf.Max(0f, tolerance) * 2f);
            return expandedBounds.Intersects(bBounds);
        }

        static void DisableColliderSurface(
            ref BigRedButtonGeneratedBodyCollider colliderGenerator,
            ref BigRedButtonColliderDebugVisual debugVisual)
        {
            colliderGenerator?.DisableCollider();
            if (debugVisual != null)
            {
                debugVisual.enabled = false;
            }
        }

        BoxCollider ResolveConfiguredTriggerSurface(ref BigRedButtonColliderDebugVisual debugVisual)
        {
            var surfaceTransform = transform.Find(TriggerSurfaceObjectName);
            if (surfaceTransform == null)
            {
                _pressTriggerSurfaceAlignmentTool = null;
                debugVisual = null;
                return null;
            }

            debugVisual = surfaceTransform.GetComponent<BigRedButtonColliderDebugVisual>();
            var boxCollider = surfaceTransform.GetComponent<BoxCollider>();
            _pressTriggerSurfaceAlignmentTool = ConfigureTriggerSurfaceAlignmentTool(surfaceTransform);
            if (debugVisual != null)
            {
                debugVisual.Configure(BigRedButtonColliderDebugVisual.VisualRole.PressTrigger);
            }

            return boxCollider;
        }

        BoxCollider RefreshTriggerSurface(
            Renderer renderer,
            ref BigRedButtonColliderDebugVisual debugVisual)
        {
            if (!IsRendererUsable(renderer))
            {
                DisableTriggerSurface();
                return null;
            }

            var surfaceTransform = transform.Find(TriggerSurfaceObjectName);
            if (surfaceTransform == null)
            {
                var surfaceObject = new GameObject(TriggerSurfaceObjectName);
                surfaceObject.layer = gameObject.layer;
                surfaceTransform = surfaceObject.transform;
                surfaceTransform.SetParent(transform, false);
            }

            _pressTriggerSurfaceCollider = surfaceTransform.GetComponent<BoxCollider>();
            if (_pressTriggerSurfaceCollider == null)
            {
                _pressTriggerSurfaceCollider = surfaceTransform.gameObject.AddComponent<BoxCollider>();
            }

            _pressTriggerSurfaceCollider.isTrigger = true;
            UpdateTriggerSurfaceBounds(renderer, surfaceTransform, _pressTriggerSurfaceCollider);
            _pressTriggerSurfaceAlignmentTool = ConfigureTriggerSurfaceAlignmentTool(surfaceTransform);

            debugVisual ??= _pressTriggerSurfaceCollider.GetComponent<BigRedButtonColliderDebugVisual>();
            if (debugVisual == null)
            {
                debugVisual = _pressTriggerSurfaceCollider.gameObject.AddComponent<BigRedButtonColliderDebugVisual>();
            }

            debugVisual.Configure(BigRedButtonColliderDebugVisual.VisualRole.PressTrigger);
            ApplyTriggerSurfaceActivationState();
            return _pressTriggerSurfaceCollider;
        }

        bool HasUsableTriggerSurfaceFallback()
        {
            return enableTriggerSurfacePressFallback && IsColliderUsable(_pressTriggerSurfaceCollider);
        }

        bool ShouldEnableTriggerSurfacePresentation()
        {
            return triggerSurfaceAlignmentMode || enableTriggerSurfacePressFallback;
        }

        void ApplyTriggerSurfaceActivationState()
        {
            var shouldEnablePresentation = ShouldEnableTriggerSurfacePresentation();
            if (_pressTriggerSurfaceCollider != null)
            {
                _pressTriggerSurfaceCollider.isTrigger = true;
                _pressTriggerSurfaceCollider.enabled = shouldEnablePresentation;
            }

            if (_pressTriggerSurfaceDebugVisual != null)
            {
                _pressTriggerSurfaceDebugVisual.enabled = shouldEnablePresentation;
            }

            if (_pressTriggerSurfaceAlignmentTool != null)
            {
                _pressTriggerSurfaceAlignmentTool.enabled = triggerSurfaceAlignmentMode;
            }
        }

        BigRedButtonTriggerSurfaceAlignmentTool ConfigureTriggerSurfaceAlignmentTool(Transform surfaceTransform)
        {
            if (surfaceTransform == null)
            {
                return null;
            }

            var alignmentTool = surfaceTransform.GetComponent<BigRedButtonTriggerSurfaceAlignmentTool>();
            if (alignmentTool == null && triggerSurfaceAlignmentMode)
            {
                alignmentTool = surfaceTransform.gameObject.AddComponent<BigRedButtonTriggerSurfaceAlignmentTool>();
            }

            if (alignmentTool == null)
            {
                return null;
            }

            alignmentTool.Configure(transform);
            alignmentTool.enabled = triggerSurfaceAlignmentMode;
            return alignmentTool;
        }

        void UpdateTriggerSurfaceBounds(Renderer renderer, Transform surfaceTransform, BoxCollider boxCollider)
        {
            if (renderer == null || surfaceTransform == null || boxCollider == null)
            {
                return;
            }

            if (!TryGetDefaultTriggerSurfacePose(
                    renderer,
                    out var centerWorld,
                    out var surfaceRotation,
                    out var thicknessWorld,
                    out var circularDiameter))
            {
                return;
            }

            if (!TryApplyTriggerSurfacePoseOverride(surfaceTransform, centerWorld, surfaceRotation))
            {
                surfaceTransform.SetPositionAndRotation(centerWorld, surfaceRotation);
            }

            surfaceTransform.localScale = Vector3.one;
            boxCollider.center = Vector3.zero;
            var defaultLocalSize = WorldSizeToLocal(
                new Vector3(
                    Mathf.Max(0.01f, circularDiameter),
                    thicknessWorld,
                    Mathf.Max(0.01f, circularDiameter)),
                surfaceTransform);
            boxCollider.size = useTriggerSurfaceSizeOverride
                ? SanitizeTriggerSurfaceLocalSize(triggerSurfaceLocalSizeOverride)
                : SanitizeTriggerSurfaceLocalSize(defaultLocalSize);
        }

        bool TryApplyTriggerSurfacePoseOverride(Transform surfaceTransform, Vector3 baseCenterWorld, Quaternion baseRotation)
        {
            if (!useTriggerSurfacePoseOverride || surfaceTransform == null)
            {
                return false;
            }

            if (!Application.isPlaying)
            {
                surfaceTransform.SetLocalPositionAndRotation(
                    triggerSurfaceLocalPositionOverride,
                    Quaternion.Euler(triggerSurfaceLocalEulerOverride));
                return true;
            }

            EnsureRuntimeTriggerSurfacePoseOffset(baseCenterWorld, baseRotation);
            surfaceTransform.SetPositionAndRotation(
                baseCenterWorld + (baseRotation * _runtimeTriggerSurfaceLocalOffset),
                baseRotation * _runtimeTriggerSurfaceRotationOffset);
            return true;
        }

        void EnsureRuntimeTriggerSurfacePoseOffset(Vector3 baseCenterWorld, Quaternion baseRotation)
        {
            if (_hasRuntimeTriggerSurfaceOffset)
            {
                return;
            }

            var desiredWorldPosition = transform.TransformPoint(triggerSurfaceLocalPositionOverride);
            var desiredWorldRotation = transform.rotation * Quaternion.Euler(triggerSurfaceLocalEulerOverride);
            _runtimeTriggerSurfaceLocalOffset =
                Quaternion.Inverse(baseRotation) * (desiredWorldPosition - baseCenterWorld);
            _runtimeTriggerSurfaceRotationOffset =
                Quaternion.Inverse(baseRotation) * desiredWorldRotation;
            _hasRuntimeTriggerSurfaceOffset = true;
        }

        void ResetTriggerSurfacePoseOffsetCache()
        {
            _runtimeTriggerSurfaceLocalOffset = Vector3.zero;
            _runtimeTriggerSurfaceRotationOffset = Quaternion.identity;
            _hasRuntimeTriggerSurfaceOffset = false;
        }

        void TryLogPressCollisionDiagnostics(
            BigRedButtonPressInteractor interactor,
            Collider interactorCollider,
            bool meshHit,
            bool surfaceHit)
        {
            if (!logPressCollisionDiagnostics ||
                interactorCollider == null ||
                Time.unscaledTime < _nextPressCollisionDiagnosticTime)
            {
                return;
            }

            _nextPressCollisionDiagnosticTime = Time.unscaledTime + pressCollisionDiagnosticCooldownSeconds;

            var builder = new StringBuilder(768);
            builder.Append("[BigRedButtonPressDiagnostics] ");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "time={0:0.###} armed={1} cooldownRemaining={2:0.###} meshHit={3} surfaceHit={4} meshInflation={5:0.####} surfaceTolerance={6:0.####} ",
                Time.unscaledTime,
                _pressArmed ? 1 : 0,
                Mathf.Max(0f, _nextAllowedPressTime - Time.unscaledTime),
                meshHit ? 1 : 0,
                surfaceHit ? 1 : 0,
                pressTriggerMeshInflation,
                pressTriggerSurfaceContactTolerance);
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "interactor={0} collider={1} ",
                BuildTransformPath(interactor != null ? interactor.transform : null),
                DescribeCollider(interactorCollider));
            AppendColliderContactDiagnostics(builder, "triggerMesh", _pressTriggerCollider, interactorCollider);
            AppendColliderContactDiagnostics(builder, "triggerSurface", _pressTriggerSurfaceCollider, interactorCollider);
            AppendColliderContactDiagnostics(builder, "base", _baseCollider, interactorCollider);
            if (IsColliderUsable(_pressTriggerSurfaceCollider))
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "triggerSurface.localSize={0} ",
                    FormatVector(_pressTriggerSurfaceCollider.size));
            }

            Debug.Log(builder.ToString(), this);
        }

        void TryLogPressDispatchFailure(BigRedButtonPressInteractor interactor)
        {
            if (!logPressCollisionDiagnostics || Time.unscaledTime < _nextPressCollisionDiagnosticTime)
            {
                return;
            }

            _nextPressCollisionDiagnosticTime = Time.unscaledTime + pressCollisionDiagnosticCooldownSeconds;
            Debug.Log(
                $"[BigRedButtonPressDiagnostics] dispatchFailed interactor={BuildTransformPath(interactor != null ? interactor.transform : null)} " +
                $"inputManagerPresent={(inputManager != null ? 1 : 0)}",
                this);
        }

        void TryLogRuntimeSetup()
        {
            if (_hasLoggedRuntimeSetup || !Application.isPlaying)
            {
                return;
            }

            var triggerRendererScale = PressTriggerRenderer != null ? FormatVector(PressTriggerRenderer.transform.lossyScale) : "<none>";
            var triggerMeshLocalBounds = "<none>";
            var triggerMeshWorldBounds = "<none>";
            var triggerMeshLocalPosition = "<none>";
            var triggerColliderDerivedOffset = FormatVector(triggerColliderDerivedLocalOffset);
            var triggerColliderManualOffset = FormatVector(triggerColliderManualLocalOffset);
            if (IsColliderUsable(_pressTriggerCollider) && _pressTriggerCollider.sharedMesh != null)
            {
                triggerMeshLocalBounds = FormatBounds(_pressTriggerCollider.sharedMesh.bounds);
                triggerMeshWorldBounds = FormatBounds(_pressTriggerCollider.bounds);
                triggerMeshLocalPosition = FormatVector(_pressTriggerCollider.transform.localPosition);
            }

            Debug.LogWarning(
                $"[BigRedButtonPressSetup] button={BuildTransformPath(transform)} " +
                $"triggerRenderer={BuildTransformPath(PressTriggerRenderer != null ? PressTriggerRenderer.transform : null)} " +
                $"triggerMesh={DescribeCollider(_pressTriggerCollider)} triggerSurface={DescribeCollider(_pressTriggerSurfaceCollider)} " +
                $"triggerSurfaceFallbackEnabled={(enableTriggerSurfacePressFallback ? 1 : 0)} " +
                $"rendererShellEnabled={(_pressTriggerRendererShell != null && _pressTriggerRendererShell.enabled ? 1 : 0)} " +
                $"meshInflation={pressTriggerMeshInflation:0.####} surfaceTolerance={pressTriggerSurfaceContactTolerance:0.####} " +
                $"surfaceSize={(IsColliderUsable(_pressTriggerSurfaceCollider) ? FormatVector(_pressTriggerSurfaceCollider.size) : "<none>")} " +
                $"triggerRendererScale={triggerRendererScale} " +
                $"triggerColliderDerivedOffset={triggerColliderDerivedOffset} " +
                $"triggerColliderManualOffset={triggerColliderManualOffset} " +
                $"triggerMeshLocalPosition={triggerMeshLocalPosition} " +
                $"triggerMeshLocalBounds={triggerMeshLocalBounds} " +
                $"triggerMeshWorldBounds={triggerMeshWorldBounds}");
            _hasLoggedRuntimeSetup = true;
        }

        void AppendColliderContactDiagnostics(StringBuilder builder, string label, Collider targetCollider, Collider interactorCollider)
        {
            if (builder == null)
            {
                return;
            }

            builder.Append(label);
            builder.Append('=');
            if (!IsColliderUsable(targetCollider))
            {
                builder.Append("<none> ");
                return;
            }

            builder.Append(DescribeCollider(targetCollider));
            builder.Append(' ');

            if (TryComputeColliderPenetration(targetCollider, interactorCollider, out var penetrationDistance))
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "penetration={0:0.####} ", penetrationDistance);
            }
            else
            {
                builder.Append("penetration=<unsupported> ");
            }

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "boundsGap={0:0.####} ",
                ComputeBoundsGap(targetCollider.bounds, interactorCollider.bounds));
        }

        static float ComputeBoundsGap(Bounds aBounds, Bounds bBounds)
        {
            var gapX = Mathf.Max(0f, Mathf.Max(aBounds.min.x - bBounds.max.x, bBounds.min.x - aBounds.max.x));
            var gapY = Mathf.Max(0f, Mathf.Max(aBounds.min.y - bBounds.max.y, bBounds.min.y - aBounds.max.y));
            var gapZ = Mathf.Max(0f, Mathf.Max(aBounds.min.z - bBounds.max.z, bBounds.min.z - aBounds.max.z));
            return Mathf.Sqrt((gapX * gapX) + (gapY * gapY) + (gapZ * gapZ));
        }

        static Vector3 SanitizeTriggerSurfaceLocalSize(Vector3 localSize)
        {
            return new Vector3(
                Mathf.Max(0.0005f, Mathf.Abs(localSize.x)),
                Mathf.Max(0.0005f, Mathf.Abs(localSize.y)),
                Mathf.Max(0.0005f, Mathf.Abs(localSize.z)));
        }

        static string DescribeCollider(Collider collider)
        {
            if (collider == null)
            {
                return "<null>";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}('{1}') enabled={2}",
                collider.GetType().Name,
                BuildTransformPath(collider.transform),
                collider.enabled ? 1 : 0);
        }

        static string BuildTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            var path = transform.name;
            var current = transform.parent;
            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }

        static string FormatVector(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.####},{1:0.####},{2:0.####})",
                value.x,
                value.y,
                value.z);
        }

        static string FormatBounds(Bounds bounds)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[center={0} size={1}]",
                FormatVector(bounds.center),
                FormatVector(bounds.size));
        }

        bool TryGetDefaultTriggerSurfacePose(
            Renderer renderer,
            out Vector3 centerWorld,
            out Quaternion surfaceRotation,
            out float thicknessWorld,
            out float circularDiameter)
        {
            centerWorld = default;
            surfaceRotation = Quaternion.identity;
            thicknessWorld = 0f;
            circularDiameter = 0f;

            if (renderer == null)
            {
                return false;
            }

            var worldBounds = renderer.bounds;
            thicknessWorld = Mathf.Clamp(worldBounds.size.y * 0.14f, 0.012f, 0.028f);
            var centerFallback = new Vector3(
                worldBounds.center.x,
                worldBounds.max.y - (thicknessWorld * 0.25f),
                worldBounds.center.z);
            var sizeFallback = new Vector3(
                Mathf.Max(0.012f, worldBounds.size.x * 0.68f),
                thicknessWorld,
                Mathf.Max(0.012f, worldBounds.size.z * 0.68f));

            var surfaceNormal = Vector3.up;
            if (TryGetTriggerSurfacePose(renderer, thicknessWorld, out _, out var fittedRotation, out _))
            {
                surfaceNormal = ReflectTriggerSurfaceNormal(renderer, fittedRotation * Vector3.up);
            }

            surfaceRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
            circularDiameter = Mathf.Min(sizeFallback.x, sizeFallback.z) * triggerSurfaceDiameterScale;
            centerWorld = centerFallback - (surfaceNormal * (thicknessWorld * 0.08f));
            return true;
        }

        static Vector3 ReflectTriggerSurfaceNormal(Renderer renderer, Vector3 fittedNormal)
        {
            var normalizedNormal = fittedNormal.sqrMagnitude > 0.000001f ? fittedNormal.normalized : Vector3.up;
            if (renderer == null)
            {
                return normalizedNormal;
            }

            var mirrorAxis = renderer.transform.forward;
            if (mirrorAxis.sqrMagnitude <= 0.000001f)
            {
                return normalizedNormal;
            }

            return Vector3.Reflect(normalizedNormal, mirrorAxis.normalized).normalized;
        }

        bool TryGetTriggerSurfacePose(
            Renderer renderer,
            float thicknessWorld,
            out Vector3 centerWorld,
            out Quaternion surfaceRotation,
            out Vector3 sizeWorld)
        {
            centerWorld = default;
            surfaceRotation = Quaternion.identity;
            sizeWorld = Vector3.zero;

            if (renderer == null)
            {
                return false;
            }

            if (!TryResolveTriggerSurfaceMesh(renderer, out var mesh, out var localToWorld))
            {
                return false;
            }

            var triangleCount = mesh.triangles.Length / 3;
            if (triangleCount == 0)
            {
                return false;
            }

            var expectedNormal = GetExpectedPressNormal(renderer);
            var indices = mesh.triangles;
            var vertices = mesh.vertices;
            var maxProjectedHeight = float.NegativeInfinity;
            const float TopFaceAlignmentThreshold = 0.75f;

            for (var triangleIndex = 0; triangleIndex < indices.Length; triangleIndex += 3)
            {
                if (!TryGetWorldTriangle(vertices, indices, triangleIndex, localToWorld, out var a, out var b, out var c, out var normal, out _, out var centroid))
                {
                    continue;
                }

                if (Vector3.Dot(normal, expectedNormal) <= TopFaceAlignmentThreshold)
                {
                    continue;
                }

                var projectedHeight = Vector3.Dot(centroid, expectedNormal);
                if (projectedHeight > maxProjectedHeight)
                {
                    maxProjectedHeight = projectedHeight;
                }
            }

            if (!float.IsFinite(maxProjectedHeight))
            {
                return false;
            }

            var topBandThickness = Mathf.Max(thicknessWorld * 0.5f, renderer.bounds.size.magnitude * 0.01f);
            var weightedNormal = Vector3.zero;
            var weightedCentroid = Vector3.zero;
            var totalWeight = 0f;

            for (var triangleIndex = 0; triangleIndex < indices.Length; triangleIndex += 3)
            {
                if (!TryGetWorldTriangle(vertices, indices, triangleIndex, localToWorld, out var a, out var b, out var c, out var normal, out var area, out var centroid))
                {
                    continue;
                }

                var alignment = Vector3.Dot(normal, expectedNormal);
                if (alignment <= TopFaceAlignmentThreshold)
                {
                    continue;
                }

                var projectedHeight = Vector3.Dot(centroid, expectedNormal);
                if (projectedHeight < maxProjectedHeight - topBandThickness)
                {
                    continue;
                }

                var weight = area * alignment;
                weightedNormal += normal * weight;
                weightedCentroid += centroid * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0.000001f)
            {
                return false;
            }

            var surfaceNormal = weightedNormal.normalized;
            if (Vector3.Dot(surfaceNormal, expectedNormal) < 0f)
            {
                surfaceNormal = -surfaceNormal;
            }

            var tangentRight = Vector3.ProjectOnPlane(renderer.transform.right, surfaceNormal);
            if (tangentRight.sqrMagnitude <= 0.000001f)
            {
                tangentRight = Vector3.ProjectOnPlane(renderer.transform.forward, surfaceNormal);
            }

            if (tangentRight.sqrMagnitude <= 0.000001f)
            {
                tangentRight = Vector3.Cross(surfaceNormal, Vector3.up);
            }

            if (tangentRight.sqrMagnitude <= 0.000001f)
            {
                tangentRight = Vector3.right;
            }

            tangentRight.Normalize();
            var tangentForward = Vector3.Cross(surfaceNormal, tangentRight).normalized;
            var surfaceCenter = weightedCentroid / totalWeight;
            var minRight = float.PositiveInfinity;
            var maxRight = float.NegativeInfinity;
            var minForward = float.PositiveInfinity;
            var maxForward = float.NegativeInfinity;
            AccumulateTopBandExtents(
                vertices,
                indices,
                localToWorld,
                expectedNormal,
                maxProjectedHeight,
                topBandThickness,
                surfaceCenter,
                tangentRight,
                tangentForward,
                ref minRight,
                ref maxRight,
                ref minForward,
                ref maxForward);

            var hasTopBandExtents = float.IsFinite(minRight) &&
                float.IsFinite(maxRight) &&
                float.IsFinite(minForward) &&
                float.IsFinite(maxForward) &&
                maxRight > minRight &&
                maxForward > minForward;
            var sizeRight = hasTopBandExtents ? (maxRight - minRight) * 0.94f : 0f;
            var sizeForward = hasTopBandExtents ? (maxForward - minForward) * 0.94f : 0f;
            if (!hasTopBandExtents)
            {
                var bounds = renderer.bounds;
                sizeRight = ProjectBoundsExtent(bounds.extents, tangentRight) * 2f * 0.72f;
                sizeForward = ProjectBoundsExtent(bounds.extents, tangentForward) * 2f * 0.72f;
            }

            centerWorld = surfaceCenter - (surfaceNormal * (thicknessWorld * 0.18f));
            surfaceRotation = Quaternion.LookRotation(tangentForward, surfaceNormal);
            sizeWorld = new Vector3(
                Mathf.Max(0.01f, sizeRight),
                thicknessWorld,
                Mathf.Max(0.01f, sizeForward));
            return true;
        }

        Vector3 GetExpectedPressNormal(Renderer renderer)
        {
            if (renderer != null && targetRenderer != null)
            {
                var centerDelta = renderer.bounds.center - targetRenderer.bounds.center;
                if (centerDelta.sqrMagnitude > 0.000001f)
                {
                    return centerDelta.normalized;
                }
            }

            return renderer != null ? renderer.transform.up : Vector3.up;
        }

        bool TryResolveTriggerSurfaceMesh(Renderer renderer, out Mesh mesh, out Matrix4x4 localToWorld)
        {
            mesh = null;
            localToWorld = Matrix4x4.identity;
            if (renderer == null)
            {
                return false;
            }

            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                _triggerSurfaceFitMesh ??= CreateTriggerSurfaceFitMesh();
                skinnedRenderer.BakeMesh(_triggerSurfaceFitMesh, false);
                if (IsUsableSurfaceMesh(_triggerSurfaceFitMesh))
                {
                    mesh = _triggerSurfaceFitMesh;
                    localToWorld = renderer.localToWorldMatrix;
                    return true;
                }
            }

            var meshCollider = renderer.GetComponent<MeshCollider>();
            if (meshCollider != null && IsUsableSurfaceMesh(meshCollider.sharedMesh))
            {
                mesh = meshCollider.sharedMesh;
                localToWorld = renderer.localToWorldMatrix;
                return true;
            }

            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && IsUsableSurfaceMesh(meshFilter.sharedMesh))
            {
                mesh = meshFilter.sharedMesh;
                localToWorld = renderer.localToWorldMatrix;
                return true;
            }

            return false;
        }

        static void AccumulateTopBandExtents(
            Vector3[] vertices,
            int[] indices,
            Matrix4x4 localToWorld,
            Vector3 expectedNormal,
            float maxProjectedHeight,
            float topBandThickness,
            Vector3 surfaceCenter,
            Vector3 tangentRight,
            Vector3 tangentForward,
            ref float minRight,
            ref float maxRight,
            ref float minForward,
            ref float maxForward)
        {
            for (var triangleIndex = 0; triangleIndex < indices.Length; triangleIndex += 3)
            {
                if (!TryGetWorldTriangle(vertices, indices, triangleIndex, localToWorld, out var a, out var b, out var c, out var normal, out _, out var centroid))
                {
                    continue;
                }

                if (Vector3.Dot(normal, expectedNormal) <= 0.75f)
                {
                    continue;
                }

                var projectedHeight = Vector3.Dot(centroid, expectedNormal);
                if (projectedHeight < maxProjectedHeight - topBandThickness)
                {
                    continue;
                }

                AccumulateProjectedPoint(a, surfaceCenter, tangentRight, tangentForward, ref minRight, ref maxRight, ref minForward, ref maxForward);
                AccumulateProjectedPoint(b, surfaceCenter, tangentRight, tangentForward, ref minRight, ref maxRight, ref minForward, ref maxForward);
                AccumulateProjectedPoint(c, surfaceCenter, tangentRight, tangentForward, ref minRight, ref maxRight, ref minForward, ref maxForward);
            }
        }

        static void AccumulateProjectedPoint(
            Vector3 point,
            Vector3 origin,
            Vector3 tangentRight,
            Vector3 tangentForward,
            ref float minRight,
            ref float maxRight,
            ref float minForward,
            ref float maxForward)
        {
            var delta = point - origin;
            var right = Vector3.Dot(delta, tangentRight);
            var forward = Vector3.Dot(delta, tangentForward);
            minRight = Mathf.Min(minRight, right);
            maxRight = Mathf.Max(maxRight, right);
            minForward = Mathf.Min(minForward, forward);
            maxForward = Mathf.Max(maxForward, forward);
        }

        static bool TryGetWorldTriangle(
            Vector3[] vertices,
            int[] indices,
            int triangleIndex,
            Matrix4x4 localToWorld,
            out Vector3 a,
            out Vector3 b,
            out Vector3 c,
            out Vector3 normal,
            out float area,
            out Vector3 centroid)
        {
            a = default;
            b = default;
            c = default;
            normal = default;
            area = 0f;
            centroid = default;

            if (vertices == null ||
                indices == null ||
                triangleIndex < 0 ||
                triangleIndex + 2 >= indices.Length)
            {
                return false;
            }

            a = localToWorld.MultiplyPoint3x4(vertices[indices[triangleIndex]]);
            b = localToWorld.MultiplyPoint3x4(vertices[indices[triangleIndex + 1]]);
            c = localToWorld.MultiplyPoint3x4(vertices[indices[triangleIndex + 2]]);

            var cross = Vector3.Cross(b - a, c - a);
            var magnitude = cross.magnitude;
            if (magnitude <= 0.000001f)
            {
                return false;
            }

            normal = cross / magnitude;
            area = magnitude * 0.5f;
            centroid = (a + b + c) / 3f;
            return true;
        }

        Mesh CreateTriggerSurfaceFitMesh()
        {
            var mesh = new Mesh
            {
                name = $"{name} Trigger Surface Fit Mesh"
            };
            mesh.MarkDynamic();
            return mesh;
        }

        static bool IsUsableSurfaceMesh(Mesh mesh)
        {
            return mesh != null && mesh.vertexCount > 0 && mesh.triangles != null && mesh.triangles.Length >= 3;
        }

        static float ProjectBoundsExtent(Vector3 extents, Vector3 worldAxis)
        {
            var normalizedAxis = worldAxis.sqrMagnitude > 0.000001f ? worldAxis.normalized : Vector3.up;
            return Mathf.Abs(normalizedAxis.x) * extents.x +
                Mathf.Abs(normalizedAxis.y) * extents.y +
                Mathf.Abs(normalizedAxis.z) * extents.z;
        }

        void DisableTriggerSurface()
        {
            if (_pressTriggerSurfaceCollider != null)
            {
                _pressTriggerSurfaceCollider.enabled = false;
            }

            if (_pressTriggerSurfaceDebugVisual != null)
            {
                _pressTriggerSurfaceDebugVisual.enabled = false;
            }

            if (_pressTriggerSurfaceAlignmentTool != null)
            {
                _pressTriggerSurfaceAlignmentTool.enabled = false;
            }
        }

        static BigRedButtonRendererMeshDebug RefreshRendererShell(Renderer renderer, MeshCollider meshCollider)
        {
            return ConfigureRendererShell(renderer, meshCollider);
        }

        static BigRedButtonRendererMeshDebug ResolveConfiguredRendererShell(Renderer renderer, MeshCollider meshCollider)
        {
            return ConfigureRendererShell(renderer, meshCollider);
        }

        static BigRedButtonRendererMeshDebug ConfigureRendererShell(Renderer renderer, MeshCollider meshCollider)
        {
            DisableRendererShell(renderer);
            return null;
        }

        static void DisableRendererShell(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var rendererShell = renderer.GetComponent<BigRedButtonRendererMeshDebug>();
            if (rendererShell != null)
            {
                rendererShell.enabled = false;
            }

            var capShellInspector = renderer.GetComponent<BigRedButtonCapRuntimeShellInspector>();
            if (capShellInspector != null)
            {
                capShellInspector.enabled = false;
            }
        }

        static Vector3 WorldSizeToLocal(Vector3 worldSize, Transform targetTransform)
        {
            if (targetTransform == null)
            {
                return worldSize;
            }

            var scale = targetTransform.lossyScale;
            return new Vector3(
                worldSize.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                worldSize.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                worldSize.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
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

        static bool IsRendererUsable(Renderer renderer)
        {
            return renderer != null &&
                renderer.enabled &&
                renderer.gameObject.activeInHierarchy &&
                !IsDebugVisualRenderer(renderer);
        }

        static bool IsColliderUsable(Collider collider)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
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
    }
}
