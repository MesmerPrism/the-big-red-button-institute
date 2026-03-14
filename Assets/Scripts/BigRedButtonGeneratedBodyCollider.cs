using UnityEngine;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonGeneratedBodyCollider : MonoBehaviour
    {
        static readonly MeshColliderCookingOptions DefaultCookingOptions =
            MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.UseFastMidphase;
        const string ColliderHostName = "Generated Collider Host";
        const float MinScaleComponent = 0.000001f;

        [SerializeField] Renderer sourceRenderer;
        [SerializeField] bool autoResolveSource = true;
        [SerializeField] bool preferConvex;
        [SerializeField] bool isTrigger;
        [SerializeField, Min(0f)] float surfaceInflation;
        [SerializeField] MeshColliderCookingOptions cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase;
        [SerializeField] Mesh serializedSnapshotMesh;

        MeshCollider _meshCollider;
        Mesh _runtimeMesh;
        Mesh _generatedColliderMesh;
        Transform _colliderHost;
        Vector3 _colliderHostLocalPositionOffset;

        public Renderer SourceRenderer => sourceRenderer;
        public MeshCollider Collider => _meshCollider;
        public bool IsConvex => _meshCollider != null && _meshCollider.convex;
        public Mesh SerializedSnapshotMesh => serializedSnapshotMesh;

        void Reset()
        {
            cookingOptions = DefaultCookingOptions;
            ResolveSourceRenderer(forceRefresh: true);
        }

        public void Configure(Renderer renderer, bool targetPreferConvex = false, bool targetIsTrigger = false, float targetSurfaceInflation = 0f)
        {
            sourceRenderer = renderer;
            preferConvex = targetPreferConvex;
            isTrigger = targetIsTrigger;
            surfaceInflation = Mathf.Max(0f, targetSurfaceInflation);
        }

        public MeshCollider RefreshCollider()
        {
            ResolveSourceRenderer(forceRefresh: false);
            var meshCollider = EnsureMeshCollider();
            if (meshCollider == null || sourceRenderer == null)
            {
                DisableCollider();
                return null;
            }

            if (!TryResolveSourceMesh(sourceRenderer, out var sourceMesh))
            {
                DisableCollider();
                return null;
            }

            if (!TryBuildColliderMesh(sourceMesh, out var colliderMesh))
            {
                DisableCollider();
                return null;
            }

            var useConvex = preferConvex && GetTriangleCount(colliderMesh) <= 255;
            meshCollider.enabled = false;
            meshCollider.sharedMesh = null;
            meshCollider.convex = false;
            meshCollider.isTrigger = isTrigger;
            meshCollider.cookingOptions = cookingOptions;
            meshCollider.convex = useConvex;
            meshCollider.sharedMesh = colliderMesh;
            meshCollider.enabled = meshCollider.sharedMesh != null;
            return meshCollider.enabled ? meshCollider : null;
        }

        public void CaptureEditorSnapshot()
        {
            ResolveSourceRenderer(forceRefresh: false);
            if (!TryBuildSnapshotMesh(sourceRenderer, out var snapshotMesh))
            {
                ReplaceSerializedSnapshotMesh(null);
                return;
            }

            if (!TryBuildColliderMesh(snapshotMesh, out var colliderSnapshot))
            {
                ReplaceSerializedSnapshotMesh(null);
                return;
            }

            var snapshotCopy = Object.Instantiate(colliderSnapshot);
            snapshotCopy.name = $"{name} Collider Snapshot";
            ReplaceSerializedSnapshotMesh(snapshotCopy);
        }

        public void DisableCollider()
        {
            if (_meshCollider == null)
            {
                return;
            }

            _meshCollider.sharedMesh = null;
            _meshCollider.enabled = false;
        }

        public void SetColliderHostLocalPositionOffset(Vector3 localOffset)
        {
            _colliderHostLocalPositionOffset = localOffset;
            SyncColliderHostTransform();
        }

        void OnDestroy()
        {
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

            if (_generatedColliderMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_generatedColliderMesh);
            }
            else
            {
                DestroyImmediate(_generatedColliderMesh);
            }
        }

        void ResolveSourceRenderer(bool forceRefresh)
        {
            if ((sourceRenderer == null || forceRefresh) && autoResolveSource)
            {
                sourceRenderer = GetComponent<Renderer>();
            }
        }

        MeshCollider EnsureMeshCollider()
        {
            if (_meshCollider != null)
            {
                SyncColliderHostTransform();
                return _meshCollider;
            }

            if (sourceRenderer is SkinnedMeshRenderer)
            {
                EnsureColliderHost();
                RemoveLegacyGeneratedColliders(_colliderHost != null ? _colliderHost.gameObject : null);
                DisableLegacySourceColliderArtifacts();
                if (_colliderHost != null)
                {
                    _meshCollider = _colliderHost.GetComponent<MeshCollider>();
                    if (_meshCollider == null)
                    {
                        _meshCollider = _colliderHost.gameObject.AddComponent<MeshCollider>();
                    }
                }
            }
            else
            {
                RemoveLegacyGeneratedColliders(gameObject);
                _meshCollider = GetComponent<MeshCollider>();
                if (_meshCollider == null)
                {
                    _meshCollider = gameObject.AddComponent<MeshCollider>();
                }
            }

            SyncColliderHostTransform();
            return _meshCollider;
        }

        void RemoveLegacyGeneratedColliders(GameObject targetObject)
        {
            if (targetObject == null)
            {
                return;
            }

            var colliders = targetObject.GetComponents<Collider>();
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider is MeshCollider)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }
        }

        void EnsureColliderHost()
        {
            if (_colliderHost == null)
            {
                var existingHost = transform.Find(ColliderHostName);
                if (existingHost != null)
                {
                    _colliderHost = existingHost;
                }
            }

            if (_colliderHost == null)
            {
                var hostObject = new GameObject(ColliderHostName);
                hostObject.layer = gameObject.layer;
                _colliderHost = hostObject.transform;
                _colliderHost.SetParent(transform, false);
            }

            SyncColliderHostTransform();
        }

        void SyncColliderHostTransform()
        {
            if (_colliderHost == null)
            {
                return;
            }

            _colliderHost.localPosition = _colliderHostLocalPositionOffset;
            _colliderHost.localRotation = Quaternion.identity;
            _colliderHost.localScale = ComputeInverseLossyScale(transform.lossyScale);
        }

        void DisableLegacySourceColliderArtifacts()
        {
            var sourceMeshCollider = GetComponent<MeshCollider>();
            if (sourceMeshCollider != null)
            {
                sourceMeshCollider.enabled = false;
                sourceMeshCollider.sharedMesh = null;
            }

            var sourceDebugVisual = GetComponent<BigRedButtonColliderDebugVisual>();
            if (sourceDebugVisual != null)
            {
                sourceDebugVisual.enabled = false;
            }
        }

        static Vector3 ComputeInverseLossyScale(Vector3 lossyScale)
        {
            return new Vector3(
                Mathf.Abs(lossyScale.x) <= MinScaleComponent ? 1f : 1f / lossyScale.x,
                Mathf.Abs(lossyScale.y) <= MinScaleComponent ? 1f : 1f / lossyScale.y,
                Mathf.Abs(lossyScale.z) <= MinScaleComponent ? 1f : 1f / lossyScale.z);
        }

        bool TryResolveSourceMesh(Renderer renderer, out Mesh sourceMesh)
        {
            sourceMesh = null;
            if (renderer == null)
            {
                return false;
            }

            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                if (Application.isPlaying)
                {
                    _runtimeMesh ??= CreateRuntimeMesh();
                    // Keep baked vertices in renderer-local space so transform scale is applied only once.
                    skinnedRenderer.BakeMesh(_runtimeMesh, false);
                    if (IsUsableMesh(_runtimeMesh))
                    {
                        sourceMesh = _runtimeMesh;
                        return true;
                    }
                }

                if (IsUsableMesh(serializedSnapshotMesh))
                {
                    sourceMesh = serializedSnapshotMesh;
                    return true;
                }

                if (!Application.isPlaying)
                {
                    _runtimeMesh ??= CreateRuntimeMesh();
                    skinnedRenderer.BakeMesh(_runtimeMesh, false);
                    if (IsUsableMesh(_runtimeMesh))
                    {
                        sourceMesh = _runtimeMesh;
                        return true;
                    }
                }

                return false;
            }

            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return false;
            }

            sourceMesh = meshFilter.sharedMesh;
            return IsUsableMesh(sourceMesh);
        }

        bool TryBuildSnapshotMesh(Renderer renderer, out Mesh snapshotMesh)
        {
            snapshotMesh = null;
            if (renderer == null)
            {
                return false;
            }

            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                _runtimeMesh ??= CreateRuntimeMesh();
                skinnedRenderer.BakeMesh(_runtimeMesh, false);
                if (!IsUsableMesh(_runtimeMesh))
                {
                    return false;
                }

                snapshotMesh = _runtimeMesh;
                return true;
            }

            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || !IsUsableMesh(meshFilter.sharedMesh))
            {
                return false;
            }

            snapshotMesh = meshFilter.sharedMesh;
            return true;
        }

        void ReplaceSerializedSnapshotMesh(Mesh snapshotMesh)
        {
            if (serializedSnapshotMesh == snapshotMesh)
            {
                return;
            }

            if (serializedSnapshotMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(serializedSnapshotMesh);
                }
                else
                {
                    DestroyImmediate(serializedSnapshotMesh);
                }
            }

            serializedSnapshotMesh = snapshotMesh;
        }

        Mesh CreateRuntimeMesh()
        {
            var mesh = new Mesh
            {
                name = $"{name} Generated Collider Mesh"
            };
            mesh.MarkDynamic();
            return mesh;
        }

        bool TryBuildColliderMesh(Mesh sourceMesh, out Mesh colliderMesh)
        {
            colliderMesh = null;
            if (!IsUsableMesh(sourceMesh))
            {
                return false;
            }

            if (surfaceInflation <= 0f)
            {
                colliderMesh = sourceMesh;
                return true;
            }

            _generatedColliderMesh ??= CreateGeneratedColliderMesh();
            if (!TryInflateMesh(sourceMesh, _generatedColliderMesh, surfaceInflation))
            {
                return false;
            }

            colliderMesh = _generatedColliderMesh;
            return true;
        }

        Mesh CreateGeneratedColliderMesh()
        {
            var mesh = new Mesh
            {
                name = $"{name} Inflated Collider Mesh"
            };
            mesh.MarkDynamic();
            return mesh;
        }

        static bool TryInflateMesh(Mesh sourceMesh, Mesh targetMesh, float inflation)
        {
            if (sourceMesh == null || targetMesh == null)
            {
                return false;
            }

            var sourceVertices = sourceMesh.vertices;
            if (sourceVertices == null || sourceVertices.Length == 0)
            {
                return false;
            }

            var sourceNormals = sourceMesh.normals;
            var sourceBounds = sourceMesh.bounds;
            var boundsCenter = sourceBounds.center;
            var inflatedVertices = new Vector3[sourceVertices.Length];
            var inflatedNormals = new Vector3[sourceVertices.Length];
            for (var vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                var normal = vertexIndex < sourceNormals.Length ? sourceNormals[vertexIndex] : Vector3.zero;
                if (normal.sqrMagnitude <= 0.000001f)
                {
                    normal = sourceVertices[vertexIndex] - boundsCenter;
                }

                if (normal.sqrMagnitude <= 0.000001f)
                {
                    normal = Vector3.up;
                }

                normal.Normalize();
                inflatedNormals[vertexIndex] = normal;
                inflatedVertices[vertexIndex] = sourceVertices[vertexIndex] + (normal * inflation);
            }

            targetMesh.Clear();
            targetMesh.indexFormat = sourceMesh.indexFormat;
            targetMesh.vertices = inflatedVertices;
            targetMesh.normals = inflatedNormals;
            targetMesh.subMeshCount = sourceMesh.subMeshCount;
            for (var subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                targetMesh.SetIndices(
                    sourceMesh.GetIndices(subMeshIndex),
                    sourceMesh.GetTopology(subMeshIndex),
                    subMeshIndex,
                    calculateBounds: false);
            }

            var inflatedBounds = sourceBounds;
            inflatedBounds.Expand(inflation * 2f);
            targetMesh.bounds = inflatedBounds;
            return true;
        }

        static int GetTriangleCount(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            var triangleCount = 0;
            for (var subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                triangleCount += (int)(mesh.GetIndexCount(subMeshIndex) / 3u);
            }

            return triangleCount;
        }

        static bool IsUsableMesh(Mesh mesh)
        {
            return mesh != null && mesh.vertexCount > 0 && GetTriangleCount(mesh) > 0;
        }
    }
}
