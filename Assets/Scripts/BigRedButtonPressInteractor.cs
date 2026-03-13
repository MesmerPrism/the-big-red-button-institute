using System.Collections.Generic;
using System.Reflection;
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
        [SerializeField] bool trackingValid = true;

        const float BodySourceRefreshIntervalSeconds = 0.25f;

        static readonly FieldInfo EnablePhysicsCapsulesField = typeof(OVRSkeleton).GetField(
            "_enablePhysicsCapsules",
            BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo InitializeCapsulesMethod = typeof(OVRSkeleton).GetMethod(
            "InitializeCapsules",
            BindingFlags.Instance | BindingFlags.NonPublic);

        float _nextBodySourceRefreshTime;

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
        }

        public void ConfigureBody(Renderer[] renderers, float padding = 0f)
        {
            interactionMode = InteractionMode.RendererBody;
            bodyPadding = Mathf.Max(0f, padding);
            bodyRenderers = SanitizeRenderers(renderers);
            bodyColliders = System.Array.Empty<Collider>();
            _nextBodySourceRefreshTime = 0f;
        }

        public void SetTrackingValid(bool isValid)
        {
            trackingValid = isValid;
        }

        public bool OverlapsSphere(Vector3 worldCenter, float worldRadius)
        {
            if (!TrackingValid)
            {
                return false;
            }

            if (interactionMode == InteractionMode.RendererBody)
            {
                var combinedRadius = Mathf.Max(0f, worldRadius + bodyPadding);
                var combinedRadiusSquared = combinedRadius * combinedRadius;
                var colliders = GetBodyColliders();
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

                var renderers = GetBodyRenderers();
                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (!IsRendererUsable(renderer))
                    {
                        continue;
                    }

                    var closestPoint = renderer.bounds.ClosestPoint(worldCenter);
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
                var colliders = GetBodyColliders();
                for (var i = 0; i < colliders.Length; i++)
                {
                    if (IsColliderUsable(colliders[i]))
                    {
                        return true;
                    }
                }

                var renderers = GetBodyRenderers();
                for (var i = 0; i < renderers.Length; i++)
                {
                    if (IsRendererUsable(renderers[i]))
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

            if (Application.isPlaying &&
                (bodyColliders == null ||
                 bodyColliders.Length == 0 ||
                 Time.unscaledTime >= _nextBodySourceRefreshTime))
            {
                RefreshBodyCollisionSources();
                _nextBodySourceRefreshTime = Time.unscaledTime + BodySourceRefreshIntervalSeconds;
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
                bodyRenderers = GetComponentsInChildren<Renderer>(true);
            }

            return bodyRenderers;
        }

        void RefreshBodyCollisionSources()
        {
            EnsureGeneratedRendererColliders();

            var colliders = new List<Collider>();
            var renderers = GetBodyRenderers();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var collider = renderer.GetComponent<Collider>();
                if (collider != null && !colliders.Contains(collider))
                {
                    EnsureColliderProxy(collider);
                    colliders.Add(collider);
                }
            }

            AppendHandCapsules(colliders);
            bodyColliders = colliders.ToArray();
        }

        void EnsureGeneratedRendererColliders()
        {
            var renderers = GetBodyRenderers();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || IsHandRenderer(renderer))
                {
                    continue;
                }

                var marker = renderer.GetComponent<BigRedButtonGeneratedBodyCollider>();
                var existingCollider = renderer.GetComponent<Collider>();
                if (existingCollider != null && marker == null)
                {
                    continue;
                }

                if (!TryGetRendererLocalBounds(renderer, out var localBounds))
                {
                    continue;
                }

                var boxCollider = renderer.GetComponent<BoxCollider>();
                if (boxCollider == null)
                {
                    boxCollider = renderer.gameObject.AddComponent<BoxCollider>();
                }

                if (marker == null)
                {
                    renderer.gameObject.AddComponent<BigRedButtonGeneratedBodyCollider>();
                }

                boxCollider.center = localBounds.center;
                boxCollider.size = localBounds.size;
                boxCollider.isTrigger = false;
                EnsureColliderProxy(boxCollider);
            }
        }

        void AppendHandCapsules(List<Collider> colliders)
        {
            if (colliders == null)
            {
                return;
            }

            var handSkeletons = new List<OVRSkeleton>();
            var renderers = GetBodyRenderers();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var skeleton = renderer.GetComponentInParent<OVRSkeleton>();
                if (skeleton == null || handSkeletons.Contains(skeleton))
                {
                    continue;
                }

                handSkeletons.Add(skeleton);
            }

            for (var i = 0; i < handSkeletons.Count; i++)
            {
                var skeleton = handSkeletons[i];
                if (!IsHandSkeleton(skeleton))
                {
                    continue;
                }

                EnsureHandPhysicsCapsules(skeleton);
                var capsules = skeleton.Capsules;
                if (capsules == null)
                {
                    continue;
                }

                for (var capsuleIndex = 0; capsuleIndex < capsules.Count; capsuleIndex++)
                {
                    var capsule = capsules[capsuleIndex];
                    var collider = capsule?.CapsuleCollider;
                    if (collider == null || colliders.Contains(collider))
                    {
                        continue;
                    }

                    EnsureColliderProxy(collider);
                    colliders.Add(collider);
                }
            }
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

            var debugVisual = collider.GetComponent<BigRedButtonColliderDebugVisual>();
            if (debugVisual == null)
            {
                debugVisual = collider.gameObject.AddComponent<BigRedButtonColliderDebugVisual>();
            }

            debugVisual.Configure(BigRedButtonColliderDebugVisual.VisualRole.Interactor);
        }

        static void EnsureHandPhysicsCapsules(OVRSkeleton skeleton)
        {
            if (skeleton == null)
            {
                return;
            }

            var physicsCapsulesEnabled = EnablePhysicsCapsulesField != null &&
                EnablePhysicsCapsulesField.GetValue(skeleton) is bool isEnabled &&
                isEnabled;

            if (!physicsCapsulesEnabled && EnablePhysicsCapsulesField != null)
            {
                EnablePhysicsCapsulesField.SetValue(skeleton, true);
                if (skeleton.IsInitialized)
                {
                    InitializeCapsulesMethod?.Invoke(skeleton, null);
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

        static Renderer[] SanitizeRenderers(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return System.Array.Empty<Renderer>();
            }

            var validCount = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return System.Array.Empty<Renderer>();
            }

            var sanitized = new Renderer[validCount];
            var writeIndex = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                sanitized[writeIndex++] = renderers[i];
            }

            return sanitized;
        }

        static bool IsRendererUsable(Renderer renderer)
        {
            return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
        }

        static bool IsColliderUsable(Collider collider)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
        }

        static bool TryGetRendererLocalBounds(Renderer renderer, out Bounds localBounds)
        {
            localBounds = default;
            if (renderer == null)
            {
                return false;
            }

            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                localBounds = skinnedRenderer.localBounds;
                if (localBounds.size.sqrMagnitude > 0.000001f)
                {
                    return true;
                }

                if (skinnedRenderer.sharedMesh != null)
                {
                    localBounds = skinnedRenderer.sharedMesh.bounds;
                    return localBounds.size.sqrMagnitude > 0.000001f;
                }

                return false;
            }

            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return false;
            }

            localBounds = meshFilter.sharedMesh.bounds;
            return localBounds.size.sqrMagnitude > 0.000001f;
        }

        static bool IsHandRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            var hand = renderer.GetComponentInParent<OVRHand>();
            if (hand != null)
            {
                return true;
            }

            var skeleton = renderer.GetComponentInParent<OVRSkeleton>();
            return IsHandSkeleton(skeleton);
        }
    }
}
