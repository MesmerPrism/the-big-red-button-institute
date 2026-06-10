using System;
using System.Collections.Generic;
using Meta.XR;
using UnityEngine;

namespace TheBigRedButtonInstitute.CameraProjection
{
    /// <summary>
    /// Owns Quest passthrough camera access and a small blur layer for BRB camera-projection examples.
    /// It deliberately excludes the downstream colorama/distortion stack so the scene can compare the
    /// camera-feed projection contract against Rusty XR implementations.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10)]
    public sealed class BrbCameraFeedProjectionDriver : MonoBehaviour
    {
        const string LeftCameraAccessName = "BRB Passthrough Camera Left";
        const string RightCameraAccessName = "BRB Passthrough Camera Right";
        const string CenterEyeAnchorName = "CenterEyeAnchor";
        const string BlurShaderResourcePath = "Shaders/CameraProjection/BRBCameraFeedBlurURP";
        const string BlurShaderName = "Hidden/TheBigRedButton/CameraProjection/CameraFeedBlurURP";

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int BlurRadiusTexelsId = Shader.PropertyToID("_BlurRadiusTexels");
        static readonly int BlurSigmaId = Shader.PropertyToID("_BlurSigma");
        static readonly int BlurDirectionId = Shader.PropertyToID("_BlurDirection");

        [Header("References")]
        [SerializeField] Transform lookOrigin;
        [SerializeField] bool autoFindLookOrigin = true;

        [Header("Quest Camera Access")]
        [SerializeField] bool enableInAndroidPlayers = true;
        [SerializeField] bool requestPermissionOnEnable = true;
        [SerializeField] Vector2Int requestedResolution = new(640, 480);
        [SerializeField] [Range(1, 120)] int maxFramerate = 30;

        [Header("Blur Layer")]
        [SerializeField] bool enableBlurLayer = true;
        [SerializeField] [Min(128)] int blurTextureWidth = 640;
        [SerializeField] [Min(128)] int blurTextureHeight = 480;
        [SerializeField] [Range(0f, 32f)] float blurRadiusTexels = 8f;
        [SerializeField] [Range(0.1f, 16f)] float blurSigma = 3.5f;

        [Header("Logging")]
        [SerializeField] bool logDebug;

        PassthroughCameraAccess leftCameraAccess;
        PassthroughCameraAccess rightCameraAccess;
        Material blurMaterial;
        RenderTexture leftBlurScratch;
        RenderTexture rightBlurScratch;
        RenderTexture leftBlurredTexture;
        RenderTexture rightBlurredTexture;
        bool loggedMissingBlurShader;

        public bool LeftCameraReady => IsAccessReady(leftCameraAccess);
        public bool RightCameraReady => IsAccessReady(rightCameraAccess);
        public bool HasAnySourceTexture => TryGetSourceEyeTexture(true, out _) || TryGetSourceEyeTexture(false, out _);
        public bool HasAnyBlurredTexture => TryGetBlurredEyeTexture(true, out _) || TryGetBlurredEyeTexture(false, out _);

        public void ConfigureLookOrigin(Transform newLookOrigin)
        {
            lookOrigin = newLookOrigin;
        }

        public void ConfigureBlurLayer(float radiusTexels, float sigma, int textureWidth, int textureHeight)
        {
            blurRadiusTexels = Mathf.Clamp(radiusTexels, 0f, 32f);
            blurSigma = Mathf.Clamp(sigma, 0.1f, 16f);
            blurTextureWidth = Mathf.Max(128, textureWidth);
            blurTextureHeight = Mathf.Max(128, textureHeight);
        }

        void Reset()
        {
            AutoFindLookOrigin();
        }

        void Awake()
        {
            if (autoFindLookOrigin)
            {
                AutoFindLookOrigin();
            }
        }

        void OnEnable()
        {
            if (!ShouldRunInCurrentPlayer())
            {
                return;
            }

            if (autoFindLookOrigin)
            {
                AutoFindLookOrigin();
            }

            RequestPassthroughCameraPermissionIfNeeded();
            EnsureCameraAccesses();
        }

        void OnDisable()
        {
            SetCameraAccessEnabled(leftCameraAccess, false);
            SetCameraAccessEnabled(rightCameraAccess, false);
            ReleaseRuntimeResources();
        }

        void OnDestroy()
        {
            ReleaseRuntimeResources();
            ReleaseMaterial(ref blurMaterial);
        }

        void OnValidate()
        {
            requestedResolution.x = Mathf.Max(16, requestedResolution.x);
            requestedResolution.y = Mathf.Max(16, requestedResolution.y);
            maxFramerate = Mathf.Clamp(maxFramerate, 1, 120);
            blurTextureWidth = Mathf.Max(128, blurTextureWidth);
            blurTextureHeight = Mathf.Max(128, blurTextureHeight);
            blurRadiusTexels = Mathf.Clamp(blurRadiusTexels, 0f, 32f);
            blurSigma = Mathf.Clamp(blurSigma, 0.1f, 16f);
        }

        void Update()
        {
            if (!ShouldRunInCurrentPlayer())
            {
                return;
            }

            if (autoFindLookOrigin && lookOrigin == null)
            {
                AutoFindLookOrigin();
            }

            EnsureCameraAccesses();
            UpdateBlurLayer();
        }

        public Camera ResolveHeadCamera()
        {
            if (lookOrigin != null && lookOrigin.TryGetComponent(out Camera camera))
            {
                return camera;
            }

            return Camera.main;
        }

        public bool TryGetSourceEyeTexture(bool isLeftEye, out Texture sourceTexture)
        {
            sourceTexture = ResolveSourceTexture(isLeftEye ? leftCameraAccess : rightCameraAccess);
            if (sourceTexture != null)
            {
                return true;
            }

            sourceTexture = ResolveSourceTexture(isLeftEye ? rightCameraAccess : leftCameraAccess);
            return sourceTexture != null;
        }

        public bool TryGetBlurredEyeTexture(bool isLeftEye, out Texture blurredTexture)
        {
            blurredTexture = isLeftEye ? leftBlurredTexture : rightBlurredTexture;
            if (blurredTexture != null)
            {
                return true;
            }

            blurredTexture = isLeftEye ? rightBlurredTexture : leftBlurredTexture;
            return blurredTexture != null;
        }

        public bool TryGetEyeCameraMappingData(
            bool isLeftEye,
            out Pose cameraPose,
            out PassthroughCameraAccess.CameraIntrinsics intrinsics,
            out Vector2Int captureResolution)
        {
            cameraPose = default;
            intrinsics = default;
            captureResolution = Vector2Int.zero;

            var access = isLeftEye ? leftCameraAccess : rightCameraAccess;
            if (!IsAccessReady(access))
            {
                return false;
            }

            cameraPose = access.GetCameraPose();
            intrinsics = access.Intrinsics;
            captureResolution = access.CurrentResolution;

            return captureResolution.x > 0 &&
                   captureResolution.y > 0 &&
                   intrinsics.SensorResolution.x > 0f &&
                   intrinsics.SensorResolution.y > 0f &&
                   intrinsics.FocalLength.x > 0f &&
                   intrinsics.FocalLength.y > 0f;
        }

        bool ShouldRunInCurrentPlayer()
        {
            return Application.isPlaying &&
                   enableInAndroidPlayers &&
                   !Application.isEditor &&
                   Application.platform == RuntimePlatform.Android;
        }

        void RequestPassthroughCameraPermissionIfNeeded()
        {
            if (!requestPermissionOnEnable ||
                OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess))
            {
                return;
            }

            OVRPermissionsRequester.Request(new List<OVRPermissionsRequester.Permission>
            {
                OVRPermissionsRequester.Permission.PassthroughCameraAccess
            });
        }

        void EnsureCameraAccesses()
        {
            if (leftCameraAccess == null)
            {
                leftCameraAccess = EnsureCameraAccess(LeftCameraAccessName, PassthroughCameraAccess.CameraPositionType.Left);
            }
            else if (!leftCameraAccess.enabled)
            {
                ConfigureCameraAccess(leftCameraAccess, PassthroughCameraAccess.CameraPositionType.Left);
            }

            if (rightCameraAccess == null)
            {
                rightCameraAccess = EnsureCameraAccess(RightCameraAccessName, PassthroughCameraAccess.CameraPositionType.Right);
            }
            else if (!rightCameraAccess.enabled)
            {
                ConfigureCameraAccess(rightCameraAccess, PassthroughCameraAccess.CameraPositionType.Right);
            }
        }

        PassthroughCameraAccess EnsureCameraAccess(
            string objectName,
            PassthroughCameraAccess.CameraPositionType cameraPosition)
        {
            var existing = FindCameraAccess(cameraPosition);
            if (existing == null)
            {
                var host = new GameObject(objectName);
                host.SetActive(false);
                host.transform.SetParent(transform, false);
                existing = host.AddComponent<PassthroughCameraAccess>();
                existing.enabled = false;
                ConfigureCameraAccess(existing, cameraPosition);
                host.SetActive(true);
                return existing;
            }

            ConfigureCameraAccess(existing, cameraPosition);
            return existing;
        }

        void ConfigureCameraAccess(
            PassthroughCameraAccess access,
            PassthroughCameraAccess.CameraPositionType cameraPosition)
        {
            if (access == null)
            {
                return;
            }

            access.enabled = false;
            access.CameraPosition = cameraPosition;
            access.RequestedResolution = requestedResolution;
            access.MaxFramerate = maxFramerate;
            access.enabled = true;
        }

        PassthroughCameraAccess FindCameraAccess(PassthroughCameraAccess.CameraPositionType cameraPosition)
        {
            var accesses = GetComponentsInChildren<PassthroughCameraAccess>(true);
            for (int i = 0; i < accesses.Length; i++)
            {
                if (accesses[i].CameraPosition == cameraPosition)
                {
                    return accesses[i];
                }
            }

            return null;
        }

        void UpdateBlurLayer()
        {
            if (!enableBlurLayer)
            {
                return;
            }

            UpdateEyeBlurLayer(true);
            UpdateEyeBlurLayer(false);
        }

        void UpdateEyeBlurLayer(bool isLeftEye)
        {
            var access = isLeftEye ? leftCameraAccess : rightCameraAccess;
            var sourceTexture = ResolveSourceTexture(access);
            if (sourceTexture == null)
            {
                return;
            }

            int width = Mathf.Max(128, blurTextureWidth);
            int height = Mathf.Max(128, blurTextureHeight);
            ref RenderTexture scratch = ref (isLeftEye ? ref leftBlurScratch : ref rightBlurScratch);
            ref RenderTexture destination = ref (isLeftEye ? ref leftBlurredTexture : ref rightBlurredTexture);
            scratch = EnsureRuntimeTexture(scratch, width, height, isLeftEye ? "BRB Left Camera Blur Scratch" : "BRB Right Camera Blur Scratch");
            destination = EnsureRuntimeTexture(destination, width, height, isLeftEye ? "BRB Left Camera Blurred" : "BRB Right Camera Blurred");

            if (destination == null)
            {
                return;
            }

            if (blurRadiusTexels <= 0.0001f)
            {
                Graphics.Blit(sourceTexture, destination);
                return;
            }

            if (scratch == null || !EnsureBlurMaterial())
            {
                return;
            }

            blurMaterial.SetTexture(MainTexId, sourceTexture);
            blurMaterial.SetFloat(BlurRadiusTexelsId, blurRadiusTexels);
            blurMaterial.SetFloat(BlurSigmaId, blurSigma);
            blurMaterial.SetVector(BlurDirectionId, new Vector4(1f, 0f, 0f, 0f));
            Graphics.Blit(sourceTexture, scratch, blurMaterial);
            blurMaterial.SetTexture(MainTexId, scratch);
            blurMaterial.SetVector(BlurDirectionId, new Vector4(0f, 1f, 0f, 0f));
            Graphics.Blit(scratch, destination, blurMaterial);
        }

        bool EnsureBlurMaterial()
        {
            if (blurMaterial != null)
            {
                return true;
            }

            var shader = Resources.Load<Shader>(BlurShaderResourcePath);
            if (shader == null)
            {
                shader = Shader.Find(BlurShaderName);
            }

            if (shader == null)
            {
                if (logDebug && !loggedMissingBlurShader)
                {
                    loggedMissingBlurShader = true;
                    Debug.LogWarning("[BrbCameraFeedProjectionDriver] Missing camera-feed blur shader.", this);
                }

                return false;
            }

            blurMaterial = new Material(shader)
            {
                name = "BRB Camera Feed Blur"
            };
            return true;
        }

        void AutoFindLookOrigin()
        {
            var cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig != null && cameraRig.centerEyeAnchor != null)
            {
                lookOrigin = cameraRig.centerEyeAnchor;
                return;
            }

            var centerEye = GameObject.Find(CenterEyeAnchorName);
            if (centerEye != null)
            {
                lookOrigin = centerEye.transform;
                return;
            }

            if (Camera.main != null)
            {
                lookOrigin = Camera.main.transform;
            }
        }

        static Texture ResolveSourceTexture(PassthroughCameraAccess access)
        {
            if (!IsAccessReady(access))
            {
                return null;
            }

            try
            {
                var texture = access.GetTexture();
                return texture != null && texture.width > 0 && texture.height > 0 ? texture : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool IsAccessReady(PassthroughCameraAccess access)
        {
            return access != null &&
                   access.isActiveAndEnabled &&
                   access.IsPlaying;
        }

        static RenderTexture EnsureRuntimeTexture(
            RenderTexture existing,
            int width,
            int height,
            string textureName)
        {
            if (existing != null && existing.width == width && existing.height == height)
            {
                return existing;
            }

            ReleaseTexture(ref existing);
            var runtimeTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = textureName,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            runtimeTexture.Create();
            return runtimeTexture;
        }

        static void SetCameraAccessEnabled(PassthroughCameraAccess access, bool enabled)
        {
            if (access != null)
            {
                access.enabled = enabled;
            }
        }

        void ReleaseRuntimeResources()
        {
            ReleaseTexture(ref leftBlurScratch);
            ReleaseTexture(ref rightBlurScratch);
            ReleaseTexture(ref leftBlurredTexture);
            ReleaseTexture(ref rightBlurredTexture);
        }

        static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            DestroyUnityObject(texture);
            texture = null;
        }

        static void ReleaseMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            DestroyUnityObject(material);
            material = null;
        }

        static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
