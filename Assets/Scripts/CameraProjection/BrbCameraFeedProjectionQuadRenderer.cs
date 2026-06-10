using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheBigRedButtonInstitute.CameraProjection
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(55)]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    public sealed class BrbCameraFeedProjectionQuadRenderer : MonoBehaviour
    {
        public enum ProjectionLayerMode
        {
            RawFeed = 0,
            BlurredFeed = 1,
            RawBlurSplit = 2,
            BlurDifference = 3,
        }

        public enum DisplaySurfaceMode
        {
            DiagnosticQuad = 0,
            FullViewOverlay = 1,
        }

        const string ShaderResourcePath = "Shaders/CameraProjection/BRBProjectedCameraFeedQuadURP";
        const string ShaderName = "TheBigRedButton/CameraProjection/ProjectedCameraFeedQuadURP";

        static readonly int LeftRawTexId = Shader.PropertyToID("_LeftRawTex");
        static readonly int RightRawTexId = Shader.PropertyToID("_RightRawTex");
        static readonly int LeftBlurTexId = Shader.PropertyToID("_LeftBlurTex");
        static readonly int RightBlurTexId = Shader.PropertyToID("_RightBlurTex");
        static readonly int LayerModeId = Shader.PropertyToID("_LayerMode");
        static readonly int PreviewEyeId = Shader.PropertyToID("_PreviewEye");
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int ProjectionEdgeFadeId = Shader.PropertyToID("_ProjectionEdgeFade");
        static readonly int LeftCameraPosId = Shader.PropertyToID("_LeftCameraPos");
        static readonly int RightCameraPosId = Shader.PropertyToID("_RightCameraPos");
        static readonly int LeftCameraRotationMatrixId = Shader.PropertyToID("_LeftCameraRotationMatrix");
        static readonly int RightCameraRotationMatrixId = Shader.PropertyToID("_RightCameraRotationMatrix");
        static readonly int LeftFocalLengthId = Shader.PropertyToID("_LeftFocalLength");
        static readonly int RightFocalLengthId = Shader.PropertyToID("_RightFocalLength");
        static readonly int LeftPrincipalPointId = Shader.PropertyToID("_LeftPrincipalPoint");
        static readonly int RightPrincipalPointId = Shader.PropertyToID("_RightPrincipalPoint");
        static readonly int LeftSensorResolutionId = Shader.PropertyToID("_LeftSensorResolution");
        static readonly int RightSensorResolutionId = Shader.PropertyToID("_RightSensorResolution");
        static readonly int LeftCurrentResolutionId = Shader.PropertyToID("_LeftCurrentResolution");
        static readonly int RightCurrentResolutionId = Shader.PropertyToID("_RightCurrentResolution");
        static readonly int LeftUvOffsetId = Shader.PropertyToID("_LeftUvOffset");
        static readonly int RightUvOffsetId = Shader.PropertyToID("_RightUvOffset");
        static readonly int QuadCenterId = Shader.PropertyToID("_QuadCenterWS");
        static readonly int QuadRightId = Shader.PropertyToID("_QuadRightWS");
        static readonly int QuadUpId = Shader.PropertyToID("_QuadUpWS");
        static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");

        [SerializeField] BrbCameraFeedProjectionDriver sourceDriver;
        [SerializeField] ProjectionLayerMode layerMode = ProjectionLayerMode.RawBlurSplit;
        [SerializeField] DisplaySurfaceMode displaySurfaceMode = DisplaySurfaceMode.DiagnosticQuad;
        [SerializeField] [Min(0.05f)] float quadWidthMeters = 0.58f;
        [SerializeField] [Min(0.05f)] float quadHeightMeters = 0.42f;
        [SerializeField] [Range(1f, 1.35f)] float fullViewOverlayOverscan = 1.06f;
        [SerializeField] bool billboardToCamera = true;
        [SerializeField] bool preserveCameraRoll = true;
        [SerializeField] Vector2 leftUvOffset = Vector2.zero;
        [SerializeField] Vector2 rightUvOffset = Vector2.zero;
        [SerializeField] [Range(0f, 1f)] float opacity = 1f;
        [SerializeField] [Range(0, 1)] int previewEye = 0;
        [SerializeField] [Range(0f, 0.25f)] float projectionEdgeFade = 0.015f;
        [SerializeField] bool logDebug;

        MeshRenderer meshRenderer;
        MeshFilter meshFilter;
        Material material;
        MaterialPropertyBlock propertyBlock;
        bool loggedMissingShader;

        public void Configure(
            BrbCameraFeedProjectionDriver newSourceDriver,
            ProjectionLayerMode newLayerMode,
            DisplaySurfaceMode newDisplaySurfaceMode,
            float newQuadWidthMeters,
            float newQuadHeightMeters,
            int newPreviewEye)
        {
            sourceDriver = newSourceDriver;
            layerMode = newLayerMode;
            displaySurfaceMode = newDisplaySurfaceMode;
            quadWidthMeters = Mathf.Max(0.05f, newQuadWidthMeters);
            quadHeightMeters = Mathf.Max(0.05f, newQuadHeightMeters);
            previewEye = Mathf.Clamp(newPreviewEye, 0, 1);
        }

        void Reset()
        {
            sourceDriver = GetComponentInParent<BrbCameraFeedProjectionDriver>();
        }

        void Awake()
        {
            CacheComponents();
            EnsureQuadMesh();
            EnsureMaterial();
        }

        void OnEnable()
        {
            CacheComponents();
            EnsureQuadMesh();
            EnsureMaterial();
        }

        void OnDisable()
        {
            SetRendererEnabled(false);
        }

        void OnDestroy()
        {
            if (material != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(material);
                }
                else
                {
                    DestroyImmediate(material);
                }

                material = null;
            }
        }

        void OnValidate()
        {
            quadWidthMeters = Mathf.Max(0.05f, quadWidthMeters);
            quadHeightMeters = Mathf.Max(0.05f, quadHeightMeters);
            fullViewOverlayOverscan = Mathf.Clamp(fullViewOverlayOverscan, 1f, 1.35f);
            opacity = Mathf.Clamp01(opacity);
            previewEye = Mathf.Clamp(previewEye, 0, 1);
            projectionEdgeFade = Mathf.Clamp(projectionEdgeFade, 0f, 0.25f);
        }

        void LateUpdate()
        {
            if (!ShouldRunInCurrentPlayer())
            {
                SetRendererEnabled(false);
                return;
            }

            if (sourceDriver == null)
            {
                sourceDriver = GetComponentInParent<BrbCameraFeedProjectionDriver>();
            }

            if (sourceDriver == null)
            {
                if (logDebug)
                {
                    Debug.LogWarning("[BrbCameraFeedProjectionQuadRenderer] Source driver is not assigned.", this);
                }

                SetRendererEnabled(false);
                return;
            }

            CacheComponents();
            EnsureQuadMesh();
            if (meshRenderer == null || !EnsureMaterial())
            {
                SetRendererEnabled(false);
                return;
            }

            if (!sourceDriver.TryGetSourceEyeTexture(true, out Texture leftRawTexture) &&
                !sourceDriver.TryGetSourceEyeTexture(false, out leftRawTexture))
            {
                SetRendererEnabled(false);
                return;
            }

            if (!sourceDriver.TryGetSourceEyeTexture(false, out Texture rightRawTexture))
            {
                rightRawTexture = leftRawTexture;
            }

            if (!sourceDriver.TryGetBlurredEyeTexture(true, out Texture leftBlurTexture))
            {
                leftBlurTexture = leftRawTexture;
            }

            if (!sourceDriver.TryGetBlurredEyeTexture(false, out Texture rightBlurTexture))
            {
                rightBlurTexture = rightRawTexture;
            }

            if (!sourceDriver.TryGetEyeCameraMappingData(
                    true,
                    out Pose leftPose,
                    out PassthroughCameraAccess.CameraIntrinsics leftIntrinsics,
                    out Vector2Int leftResolution) ||
                !sourceDriver.TryGetEyeCameraMappingData(
                    false,
                    out Pose rightPose,
                    out PassthroughCameraAccess.CameraIntrinsics rightIntrinsics,
                    out Vector2Int rightResolution))
            {
                SetRendererEnabled(false);
                return;
            }

            Camera headCamera = sourceDriver.ResolveHeadCamera();
            if (headCamera == null)
            {
                SetRendererEnabled(false);
                return;
            }

            Pose referenceHeadPose = ResolveHeadPose(leftPose, leftIntrinsics, rightPose, rightIntrinsics);
            if (billboardToCamera)
            {
                Vector3 towardCamera = referenceHeadPose.position - transform.position;
                if (towardCamera.sqrMagnitude > 0.000001f)
                {
                    Vector3 up = preserveCameraRoll ? referenceHeadPose.rotation * Vector3.up : Vector3.up;
                    transform.rotation = Quaternion.LookRotation(-towardCamera.normalized, up);
                }
            }

            ResolveSurfaceSize(headCamera, referenceHeadPose, out float resolvedQuadWidth, out float resolvedQuadHeight);
            transform.localScale = new Vector3(resolvedQuadWidth, resolvedQuadHeight, 1f);

            Vector3 quadRight = transform.right.normalized;
            Vector3 quadUp = transform.up.normalized;

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetTexture(LeftRawTexId, leftRawTexture);
            propertyBlock.SetTexture(RightRawTexId, rightRawTexture);
            propertyBlock.SetTexture(LeftBlurTexId, leftBlurTexture);
            propertyBlock.SetTexture(RightBlurTexId, rightBlurTexture);
            propertyBlock.SetFloat(LayerModeId, (float)layerMode);
            propertyBlock.SetFloat(PreviewEyeId, previewEye);
            propertyBlock.SetFloat(OpacityId, opacity);
            propertyBlock.SetFloat(ProjectionEdgeFadeId, projectionEdgeFade);
            ApplyEyeData(propertyBlock, true, leftPose, leftIntrinsics, leftResolution, leftUvOffset);
            ApplyEyeData(propertyBlock, false, rightPose, rightIntrinsics, rightResolution, rightUvOffset);
            propertyBlock.SetVector(QuadCenterId, transform.position);
            propertyBlock.SetVector(QuadRightId, quadRight);
            propertyBlock.SetVector(QuadUpId, quadUp);
            propertyBlock.SetVector(QuadSizeId, new Vector4(resolvedQuadWidth, resolvedQuadHeight, 0f, 0f));
            meshRenderer.SetPropertyBlock(propertyBlock);
            SetRendererEnabled(true);
        }

        void CacheComponents()
        {
            meshRenderer ??= GetComponent<MeshRenderer>();
            meshFilter ??= GetComponent<MeshFilter>();
            propertyBlock ??= new MaterialPropertyBlock();
        }

        bool EnsureMaterial()
        {
            if (meshRenderer == null)
            {
                return false;
            }

            if (material == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderResourcePath);
                if (shader == null)
                {
                    shader = Shader.Find(ShaderName);
                }

                if (shader == null)
                {
                    if (logDebug && !loggedMissingShader)
                    {
                        loggedMissingShader = true;
                        Debug.LogWarning("[BrbCameraFeedProjectionQuadRenderer] Missing projected camera-feed shader.", this);
                    }

                    return false;
                }

                material = new Material(shader)
                {
                    name = "BRB Projected Camera Feed Quad"
                };
            }

            if (meshRenderer.sharedMaterial != material)
            {
                meshRenderer.sharedMaterial = material;
            }

            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.allowOcclusionWhenDynamic = false;
            return true;
        }

        void EnsureQuadMesh()
        {
            if (meshFilter == null || meshFilter.sharedMesh != null)
            {
                return;
            }

            var mesh = new Mesh
            {
                name = "BRB Camera Feed Projection Quad"
            };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
        }

        void ResolveSurfaceSize(
            Camera headCamera,
            Pose referenceHeadPose,
            out float resolvedQuadWidth,
            out float resolvedQuadHeight)
        {
            resolvedQuadWidth = quadWidthMeters;
            resolvedQuadHeight = quadHeightMeters;

            if (displaySurfaceMode != DisplaySurfaceMode.FullViewOverlay || headCamera == null)
            {
                return;
            }

            float verticalFovRadians = headCamera.fieldOfView * Mathf.Deg2Rad;
            if (verticalFovRadians <= 0.0001f)
            {
                return;
            }

            float planeDistance = Mathf.Abs(Vector3.Dot(transform.position - referenceHeadPose.position, transform.forward));
            planeDistance = Mathf.Max(0.05f, planeDistance);
            float aspect = headCamera.aspect > 0.001f
                ? headCamera.aspect
                : Mathf.Max(1f, (float)headCamera.pixelWidth / Mathf.Max(1, headCamera.pixelHeight));
            float overlayHeight = 2f * planeDistance * Mathf.Tan(verticalFovRadians * 0.5f);
            float overlayWidth = overlayHeight * Mathf.Max(aspect, 0.001f);
            float overscan = Mathf.Max(1f, fullViewOverlayOverscan);
            resolvedQuadWidth = Mathf.Max(0.05f, overlayWidth * overscan);
            resolvedQuadHeight = Mathf.Max(0.05f, overlayHeight * overscan);
        }

        static void ApplyEyeData(
            MaterialPropertyBlock block,
            bool isLeftEye,
            Pose pose,
            PassthroughCameraAccess.CameraIntrinsics intrinsics,
            Vector2Int captureResolution,
            Vector2 uvOffset)
        {
            Matrix4x4 rotationInverse = Matrix4x4.Rotate(Quaternion.Inverse(pose.rotation));
            block.SetVector(isLeftEye ? LeftCameraPosId : RightCameraPosId, pose.position);
            block.SetMatrix(isLeftEye ? LeftCameraRotationMatrixId : RightCameraRotationMatrixId, rotationInverse);
            block.SetVector(isLeftEye ? LeftFocalLengthId : RightFocalLengthId, new Vector4(intrinsics.FocalLength.x, intrinsics.FocalLength.y, 0f, 0f));
            block.SetVector(isLeftEye ? LeftPrincipalPointId : RightPrincipalPointId, new Vector4(intrinsics.PrincipalPoint.x, intrinsics.PrincipalPoint.y, 0f, 0f));
            block.SetVector(
                isLeftEye ? LeftSensorResolutionId : RightSensorResolutionId,
                new Vector4(intrinsics.SensorResolution.x, intrinsics.SensorResolution.y, 0f, 0f));
            block.SetVector(
                isLeftEye ? LeftCurrentResolutionId : RightCurrentResolutionId,
                new Vector4(captureResolution.x, captureResolution.y, 0f, 0f));
            block.SetVector(isLeftEye ? LeftUvOffsetId : RightUvOffsetId, new Vector4(uvOffset.x, uvOffset.y, 0f, 0f));
        }

        static Pose ResolveHeadPose(
            Pose leftEyePose,
            PassthroughCameraAccess.CameraIntrinsics leftIntrinsics,
            Pose rightEyePose,
            PassthroughCameraAccess.CameraIntrinsics rightIntrinsics)
        {
            Pose leftHeadPose = ResolveHeadPose(leftEyePose, leftIntrinsics);
            Pose rightHeadPose = ResolveHeadPose(rightEyePose, rightIntrinsics);
            return new Pose(
                Vector3.Lerp(leftHeadPose.position, rightHeadPose.position, 0.5f),
                Quaternion.Slerp(leftHeadPose.rotation, rightHeadPose.rotation, 0.5f));
        }

        static Pose ResolveHeadPose(Pose eyePose, PassthroughCameraAccess.CameraIntrinsics intrinsics)
        {
            Quaternion headRotation = eyePose.rotation * Quaternion.Inverse(intrinsics.LensOffset.rotation);
            Vector3 headPosition = eyePose.position - (headRotation * intrinsics.LensOffset.position);
            return new Pose(headPosition, headRotation);
        }

        static bool ShouldRunInCurrentPlayer()
        {
            return Application.isPlaying &&
                   !Application.isEditor &&
                   Application.platform == RuntimePlatform.Android;
        }

        void SetRendererEnabled(bool enabled)
        {
            if (meshRenderer != null)
            {
                meshRenderer.enabled = enabled;
            }
        }
    }
}
