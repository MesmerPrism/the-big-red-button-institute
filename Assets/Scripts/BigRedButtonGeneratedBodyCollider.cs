using UnityEngine;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonGeneratedBodyCollider : MonoBehaviour
    {
        static readonly MeshColliderCookingOptions DefaultCookingOptions =
            MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.UseFastMidphase;

        [SerializeField] Renderer sourceRenderer;
        [SerializeField] bool autoResolveSource = true;
        [SerializeField] bool preferConvex;
        [SerializeField] bool isTrigger;
        [SerializeField] MeshColliderCookingOptions cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase;
        [SerializeField] Mesh serializedSnapshotMesh;

        MeshCollider _meshCollider;
        Mesh _runtimeMesh;

        public Renderer SourceRenderer => sourceRenderer;
        public MeshCollider Collider => _meshCollider;
        public bool IsConvex => _meshCollider != null && _meshCollider.convex;
        public Mesh SerializedSnapshotMesh => serializedSnapshotMesh;

        void Reset()
        {
            cookingOptions = DefaultCookingOptions;
            ResolveSourceRenderer(forceRefresh: true);
        }

        public void Configure(Renderer renderer, bool targetPreferConvex = false, bool targetIsTrigger = false)
        {
            sourceRenderer = renderer;
            preferConvex = targetPreferConvex;
            isTrigger = targetIsTrigger;
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

            var useConvex = preferConvex && GetTriangleCount(sourceMesh) <= 255;
            meshCollider.enabled = false;
            meshCollider.sharedMesh = null;
            meshCollider.convex = false;
            meshCollider.isTrigger = isTrigger;
            meshCollider.cookingOptions = cookingOptions;
            meshCollider.convex = useConvex;
            meshCollider.sharedMesh = sourceMesh;
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

            var snapshotCopy = Object.Instantiate(snapshotMesh);
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

        void OnDestroy()
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
                return _meshCollider;
            }

            RemoveLegacyGeneratedColliders();
            _meshCollider = GetComponent<MeshCollider>();
            if (_meshCollider == null)
            {
                _meshCollider = gameObject.AddComponent<MeshCollider>();
            }

            return _meshCollider;
        }

        void RemoveLegacyGeneratedColliders()
        {
            var colliders = GetComponents<Collider>();
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
                    skinnedRenderer.BakeMesh(_runtimeMesh);
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
                    skinnedRenderer.BakeMesh(_runtimeMesh);
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
                skinnedRenderer.BakeMesh(_runtimeMesh);
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
