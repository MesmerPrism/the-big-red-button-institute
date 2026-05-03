using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonPressInteractor : MonoBehaviour
    {
        enum InteractionMode
        {
            PointSphere = 0,
            RendererBody = 1
        }

        [SerializeField] InteractionMode interactionMode = InteractionMode.PointSphere;
        [SerializeField, Min(0.005f)] float interactionRadius = 0.05f;
        [SerializeField, Min(0f)] float bodyPadding;
        [SerializeField] Renderer[] bodyRenderers = System.Array.Empty<Renderer>();
        [SerializeField] Collider[] bodyColliders = System.Array.Empty<Collider>();
        [SerializeField] bool autoResolveBodyRenderersFromParent;
        [SerializeField] string parentRendererRootName = string.Empty;
        [SerializeField] bool trackingValid = true;
        [SerializeField] bool generatedMeshCollidersOnly = true;
        [SerializeField] bool disableLegacyHandPhysicsCapsules = true;
        [SerializeField] bool enableRuntimeDebugVisuals = false;

        const float BodySourceRefreshIntervalSeconds = 0.25f;

        static readonly FieldInfo EnablePhysicsCapsulesField = typeof(OVRSkeleton).GetField(
            "_enablePhysicsCapsules",
            BindingFlags.Instance | BindingFlags.NonPublic);

        float _nextBodySourceRefreshTime;
        bool _hasLoggedSetup;

        public float InteractionRadius => interactionRadius;
        public bool UsesBodyInteraction => interactionMode == InteractionMode.RendererBody;
        public bool TrackingValid => trackingValid && HasActiveInteractionShape();

        public Vector3 WorldPosition => transform.position;

        public float WorldRadius
        {
            get
            {
                var maxScale = Mathf.Max(
                    0.0001f,
                    Mathf.Abs(transform.lossyScale.x),
                    Mathf.Abs(transform.lossyScale.y),
                    Mathf.Abs(transform.lossyScale.z));
                return interactionRadius * maxScale;
            }
        }

        public void Configure(float radius)
        {
            interactionMode = InteractionMode.PointSphere;
            interactionRadius = Mathf.Max(0.005f, radius);
            _hasLoggedSetup = false;
        }

        public void ConfigureBody(Renderer[] renderers, float padding = 0f)
        {
            interactionMode = InteractionMode.RendererBody;
            bodyPadding = Mathf.Max(0f, padding);
            bodyRenderers = SanitizeRenderers(renderers);
            bodyColliders = System.Array.Empty<Collider>();
            autoResolveBodyRenderersFromParent = false;
            parentRendererRootName = string.Empty;
            trackingValid = true;
            _nextBodySourceRefreshTime = 0f;
            _hasLoggedSetup = false;
        }

        public void ConfigureBodyFromParent(string rendererRootName, float padding = 0f)
        {
            interactionMode = InteractionMode.RendererBody;
            bodyPadding = Mathf.Max(0f, padding);
            bodyRenderers = System.Array.Empty<Renderer>();
            bodyColliders = System.Array.Empty<Collider>();
            autoResolveBodyRenderersFromParent = true;
            parentRendererRootName = rendererRootName ?? string.Empty;
            trackingValid = true;
            _nextBodySourceRefreshTime = 0f;
            _hasLoggedSetup = false;
        }

        public void SetTrackingValid(bool isValid)
        {
            trackingValid = isValid;
        }

        public Collider[] GetInteractionColliders()
        {
            if (interactionMode != InteractionMode.RendererBody)
            {
                return System.Array.Empty<Collider>();
            }

            PrepareForQueries();
            return bodyColliders ?? System.Array.Empty<Collider>();
        }

        public void PrepareForQueries()
        {
            if (interactionMode != InteractionMode.RendererBody)
            {
                return;
            }

            var forceRendererRefresh = bodyRenderers == null ||
                bodyRenderers.Length == 0 ||
                Time.unscaledTime >= _nextBodySourceRefreshTime;
            RefreshBodyCollisionSources(forceRendererRefresh);
            if (forceRendererRefresh)
            {
                _nextBodySourceRefreshTime = Time.unscaledTime + BodySourceRefreshIntervalSeconds;
            }

            TryLogBodySetup();
        }

        void OnEnable()
        {
            _hasLoggedSetup = false;
            _nextBodySourceRefreshTime = 0f;
        }

        public bool OverlapsSphere(Vector3 worldCenter, float worldRadius)
        {
            if (!TrackingValid)
            {
                return false;
            }

            if (interactionMode == InteractionMode.RendererBody)
            {
                var colliders = GetInteractionColliders();
                var combinedRadius = Mathf.Max(0f, worldRadius + bodyPadding);
                var combinedRadiusSquared = combinedRadius * combinedRadius;
                for (var i = 0; i < colliders.Length; i++)
                {
                    var collider = colliders[i];
                    if (!IsColliderUsable(collider))
                    {
                        continue;
                    }

                    var closestPoint = collider.ClosestPoint(worldCenter);
                    if ((closestPoint - worldCenter).sqrMagnitude <= combinedRadiusSquared)
                    {
                        return true;
                    }
                }

                return false;
            }

            var pointRadius = Mathf.Max(0f, worldRadius + WorldRadius);
            return (WorldPosition - worldCenter).sqrMagnitude <= pointRadius * pointRadius;
        }

        bool HasActiveInteractionShape()
        {
            if (interactionMode == InteractionMode.RendererBody)
            {
                var colliders = GetInteractionColliders();
                for (var i = 0; i < colliders.Length; i++)
                {
                    if (IsColliderUsable(colliders[i]))
                    {
                        return true;
                    }
                }

                return false;
            }

            return true;
        }

        Collider[] GetBodyColliders()
        {
            if (interactionMode != InteractionMode.RendererBody)
            {
                return System.Array.Empty<Collider>();
            }

            if (bodyColliders == null || bodyColliders.Length == 0)
            {
                PrepareForQueries();
            }

            return bodyColliders ?? System.Array.Empty<Collider>();
        }

        Renderer[] GetBodyRenderers()
        {
            if (interactionMode != InteractionMode.RendererBody)
            {
                return System.Array.Empty<Renderer>();
            }

            if (bodyRenderers == null || bodyRenderers.Length == 0)
            {
                bodyRenderers = ResolveConfiguredBodyRenderers();
            }

            return bodyRenderers;
        }

        void RefreshBodyCollisionSources(bool forceRendererRefresh)
        {
            if (forceRendererRefresh)
            {
                bodyRenderers = ResolveConfiguredBodyRenderers();
            }

            var colliders = new List<Collider>();
            var handSkeletons = new HashSet<OVRSkeleton>();
            var renderers = GetBodyRenderers();

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var handSkeleton = GetHandSkeleton(renderer);
                if (handSkeleton != null)
                {
                    handSkeletons.Add(handSkeleton);
                }

                if (TryResolveRendererCollider(renderer, out var generatedCollider))
                {
                    AppendCollider(colliders, generatedCollider);
                    continue;
                }

                if (!generatedMeshCollidersOnly)
                {
                    AppendExistingRendererColliders(renderer, colliders);
                }
            }

            if (disableLegacyHandPhysicsCapsules)
            {
                foreach (var handSkeleton in handSkeletons)
                {
                    DisableHandPhysicsCapsules(handSkeleton);
                }
            }

            bodyColliders = colliders.ToArray();
        }

        bool TryResolveRendererCollider(Renderer renderer, out Collider collider)
        {
            collider = null;
            if (renderer == null)
            {
                return false;
            }

            var generatedCollider = renderer.GetComponent<BigRedButtonGeneratedBodyCollider>();
            if (generatedCollider == null)
            {
                generatedCollider = renderer.gameObject.AddComponent<BigRedButtonGeneratedBodyCollider>();
            }

            generatedCollider.Configure(renderer, targetPreferConvex: false, targetIsTrigger: false);
            if (!IsRendererUsable(renderer))
            {
                generatedCollider.DisableCollider();
                return false;
            }

            collider = generatedCollider.RefreshCollider();
            return IsColliderUsable(collider);
        }

        void AppendExistingRendererColliders(Renderer renderer, List<Collider> colliders)
        {
            if (renderer == null || colliders == null)
            {
                return;
            }

            var existingColliders = renderer.GetComponents<Collider>();
            for (var i = 0; i < existingColliders.Length; i++)
            {
                AppendCollider(colliders, existingColliders[i]);
            }
        }

        void AppendCollider(List<Collider> colliders, Collider collider)
        {
            if (colliders == null || !IsColliderUsable(collider) || colliders.Contains(collider))
            {
                return;
            }

            EnsureColliderProxy(collider);
            colliders.Add(collider);
        }

        void EnsureColliderProxy(Collider collider)
        {
            if (collider == null)
            {
                return;
            }

            var proxy = collider.GetComponent<BigRedButtonPressColliderProxy>();
            if (proxy == null)
            {
                proxy = collider.gameObject.AddComponent<BigRedButtonPressColliderProxy>();
            }

            proxy.Configure(this);

            if (!enableRuntimeDebugVisuals)
            {
                var existingDebugVisual = collider.GetComponent<BigRedButtonColliderDebugVisual>();
                if (existingDebugVisual != null)
                {
                    existingDebugVisual.enabled = false;
                }

                return;
            }

            var debugVisual = collider.GetComponent<BigRedButtonColliderDebugVisual>();
            if (debugVisual == null)
            {
                debugVisual = collider.gameObject.AddComponent<BigRedButtonColliderDebugVisual>();
            }

            debugVisual.Configure(BigRedButtonColliderDebugVisual.VisualRole.Interactor);
        }

        void TryLogBodySetup()
        {
            if (_hasLoggedSetup || !Application.isPlaying || interactionMode != InteractionMode.RendererBody)
            {
                return;
            }

            var renderers = bodyRenderers ?? System.Array.Empty<Renderer>();
            var colliders = bodyColliders ?? System.Array.Empty<Collider>();
            Debug.LogWarning(
                $"[BigRedButtonInteractorSetup] interactor={BuildTransformPath(transform)} " +
                $"renderers={renderers.Length} rendererNames={BuildRendererList(renderers)} " +
                $"colliders={colliders.Length} colliderNames={BuildColliderList(colliders)} " +
                $"generatedOnly={(generatedMeshCollidersOnly ? 1 : 0)}");
            _hasLoggedSetup = true;
        }

        static string BuildRendererList(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return "<none>";
            }

            var builder = new StringBuilder(96);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('|');
                }

                builder.Append(renderers[i].name);
            }

            return builder.Length > 0 ? builder.ToString() : "<none>";
        }

        static string BuildColliderList(Collider[] colliders)
        {
            if (colliders == null || colliders.Length == 0)
            {
                return "<none>";
            }

            var builder = new StringBuilder(96);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('|');
                }

                builder.Append(colliders[i].GetType().Name);
                builder.Append(':');
                builder.Append(colliders[i].name);
            }

            return builder.Length > 0 ? builder.ToString() : "<none>";
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

        Renderer[] ResolveConfiguredBodyRenderers()
        {
            if (autoResolveBodyRenderersFromParent)
            {
                var parentTransform = transform.parent;
                if (parentTransform == null)
                {
                    return System.Array.Empty<Renderer>();
                }

                if (!string.IsNullOrWhiteSpace(parentRendererRootName))
                {
                    var rendererRoot = parentTransform.Find(parentRendererRootName);
                    if (rendererRoot != null)
                    {
                        return SanitizeRenderers(rendererRoot.GetComponentsInChildren<Renderer>(true));
                    }
                }

                return SanitizeRenderers(parentTransform.GetComponentsInChildren<Renderer>(true));
            }

            return bodyRenderers != null && bodyRenderers.Length > 0
                ? SanitizeRenderers(bodyRenderers)
                : SanitizeRenderers(GetComponentsInChildren<Renderer>(true));
        }

        static void DisableHandPhysicsCapsules(OVRSkeleton skeleton)
        {
            if (skeleton == null)
            {
                return;
            }

            if (EnablePhysicsCapsulesField != null)
            {
                EnablePhysicsCapsulesField.SetValue(skeleton, false);
            }

            var capsules = skeleton.Capsules;
            if (capsules == null)
            {
                return;
            }

            for (var capsuleIndex = 0; capsuleIndex < capsules.Count; capsuleIndex++)
            {
                var capsuleCollider = capsules[capsuleIndex]?.CapsuleCollider;
                if (capsuleCollider == null)
                {
                    continue;
                }

                capsuleCollider.enabled = false;

                var proxy = capsuleCollider.GetComponent<BigRedButtonPressColliderProxy>();
                if (proxy != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(proxy);
                    }
                    else
                    {
                        DestroyImmediate(proxy);
                    }
                }

                var debugVisual = capsuleCollider.GetComponent<BigRedButtonColliderDebugVisual>();
                if (debugVisual != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(debugVisual);
                    }
                    else
                    {
                        DestroyImmediate(debugVisual);
                    }
                }
            }
        }

        static bool IsHandSkeleton(OVRSkeleton skeleton)
        {
            if (skeleton == null)
            {
                return false;
            }

            return skeleton.GetSkeletonType() switch
            {
                OVRSkeleton.SkeletonType.HandLeft => true,
                OVRSkeleton.SkeletonType.HandRight => true,
                OVRSkeleton.SkeletonType.XRHandLeft => true,
                OVRSkeleton.SkeletonType.XRHandRight => true,
                _ => false
            };
        }

        static OVRSkeleton GetHandSkeleton(Renderer renderer)
        {
            if (renderer == null)
            {
                return null;
            }

            var skeleton = renderer.GetComponentInParent<OVRSkeleton>();
            return IsHandSkeleton(skeleton) ? skeleton : null;
        }

        static Renderer[] SanitizeRenderers(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return System.Array.Empty<Renderer>();
            }

            var sanitized = new List<Renderer>(renderers.Length);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || IsDebugVisualRenderer(renderer))
                {
                    continue;
                }

                if (!sanitized.Contains(renderer))
                {
                    sanitized.Add(renderer);
                }
            }

            return sanitized.ToArray();
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
    }
}
