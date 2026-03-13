using UnityEngine;
using UnityEngine.Rendering;

namespace TheBigRedButtonInstitute.Environment
{
    [ExecuteAlways]
    public sealed class QuestLinkEnvironmentSetup : MonoBehaviour
    {
        const string FloorObjectName = "Quest Link Floor";
        const string FloorShaderName = "TheBigRedButton/QuestLinkFloorGrid";
        const string ProceduralSkyboxShaderName = "Skybox/Procedural";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int LineColorId = Shader.PropertyToID("_LineColor");
        static readonly int MinorCellSizeId = Shader.PropertyToID("_MinorCellSize");
        static readonly int MajorCellSizeId = Shader.PropertyToID("_MajorCellSize");
        static readonly int MinorLineWidthId = Shader.PropertyToID("_MinorLineWidth");
        static readonly int MajorLineWidthId = Shader.PropertyToID("_MajorLineWidth");
        static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");
        static readonly int SunDiskId = Shader.PropertyToID("_SunDisk");
        static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");
        static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        [SerializeField] Shader floorShader;
        [SerializeField] Material skyboxMaterialTemplate;
        [SerializeField] Vector3 floorScale = new(20f, 1f, 20f);
        [SerializeField] Color floorBaseColor = new(0.17f, 0.18f, 0.19f, 1f);
        [SerializeField] Color floorLineColor = new(0.30f, 0.31f, 0.33f, 1f);
        [SerializeField] float minorCellSize = 1f;
        [SerializeField] float majorCellSize = 5f;
        [SerializeField] float minorLineWidth = 0.01f;
        [SerializeField] float majorLineWidth = 0.016f;
        [SerializeField] float fadeStart = 42f;
        [SerializeField] float fadeEnd = 78f;
        [SerializeField] Color skyTint = new(0.86f, 0.87f, 0.89f, 1f);
        [SerializeField] Color skyGroundColor = new(0.58f, 0.60f, 0.63f, 1f);
        [SerializeField] float skyExposure = 0.88f;
        [SerializeField] float skyAtmosphereThickness = 0.12f;
        [SerializeField] Color ambientSkyColor = new(0.72f, 0.74f, 0.76f, 1f);
        [SerializeField] Color ambientEquatorColor = new(0.55f, 0.57f, 0.60f, 1f);
        [SerializeField] Color ambientGroundColor = new(0.34f, 0.36f, 0.39f, 1f);
        [SerializeField] float ambientIntensity = 0.8f;
        [SerializeField] Color directionalLightColor = new(0.95f, 0.97f, 1f, 1f);
        [SerializeField] float directionalLightIntensity = 0.85f;
        [SerializeField] float shadowStrength = 0.22f;

        Material floorMaterialInstance;
        Material skyboxMaterialInstance;

        void Awake()
        {
            Apply();
        }

        void OnEnable()
        {
            Apply();
        }

        void OnValidate()
        {
            Apply();
        }

        void OnDisable()
        {
            DestroyMaterial(ref floorMaterialInstance);
            DestroyMaterial(ref skyboxMaterialInstance);
        }

        void Apply()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            floorShader ??= Shader.Find(FloorShaderName);

            ConfigureFloor();
            ConfigureSkybox();
            ConfigureAmbient();
            ConfigureDirectionalLight();
        }

        void ConfigureFloor()
        {
            var floorObject = EnsureFloorObject();
            if (!floorObject.TryGetComponent(out MeshRenderer renderer) || floorShader == null)
            {
                return;
            }

            if (floorMaterialInstance == null || floorMaterialInstance.shader != floorShader)
            {
                DestroyMaterial(ref floorMaterialInstance);
                floorMaterialInstance = new Material(floorShader)
                {
                    name = "Quest Link Floor (Runtime)"
                };
                floorMaterialInstance.hideFlags = HideFlags.HideAndDontSave;
                floorMaterialInstance.enableInstancing = true;
            }

            floorMaterialInstance.SetColor(BaseColorId, floorBaseColor);
            floorMaterialInstance.SetColor(LineColorId, floorLineColor);
            floorMaterialInstance.SetFloat(MinorCellSizeId, Mathf.Max(0.1f, minorCellSize));
            floorMaterialInstance.SetFloat(MajorCellSizeId, Mathf.Max(minorCellSize, majorCellSize));
            floorMaterialInstance.SetFloat(MinorLineWidthId, Mathf.Clamp(minorLineWidth, 0.001f, 0.1f));
            floorMaterialInstance.SetFloat(MajorLineWidthId, Mathf.Clamp(majorLineWidth, 0.001f, 0.1f));
            floorMaterialInstance.SetFloat(FadeStartId, Mathf.Max(1f, fadeStart));
            floorMaterialInstance.SetFloat(FadeEndId, Mathf.Max(fadeStart + 1f, fadeEnd));

            renderer.sharedMaterial = floorMaterialInstance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
        }

        void ConfigureSkybox()
        {
            var sourceMaterial = skyboxMaterialTemplate;
            var skyboxShader = sourceMaterial != null ? sourceMaterial.shader : Shader.Find(ProceduralSkyboxShaderName);
            if (skyboxShader == null)
            {
                return;
            }

            if (skyboxMaterialInstance == null || skyboxMaterialInstance.shader != skyboxShader)
            {
                DestroyMaterial(ref skyboxMaterialInstance);
                skyboxMaterialInstance = sourceMaterial != null
                    ? new Material(sourceMaterial)
                    : new Material(skyboxShader);
                skyboxMaterialInstance.name = "Quest Link Skybox (Runtime)";
                skyboxMaterialInstance.hideFlags = HideFlags.HideAndDontSave;
            }

            if (sourceMaterial != null)
            {
                // Keep the runtime copy in sync with the serialized template and then
                // apply the current component tuning values below.
                skyboxMaterialInstance.CopyPropertiesFromMaterial(sourceMaterial);
            }

            skyboxMaterialInstance.SetFloat(SunDiskId, 0f);
            skyboxMaterialInstance.SetFloat(AtmosphereThicknessId, Mathf.Clamp(skyAtmosphereThickness, 0f, 5f));
            skyboxMaterialInstance.SetColor(SkyTintId, skyTint);
            skyboxMaterialInstance.SetColor(GroundColorId, skyGroundColor);
            skyboxMaterialInstance.SetFloat(ExposureId, Mathf.Clamp(skyExposure, 0f, 8f));

            if (RenderSettings.skybox != skyboxMaterialInstance)
            {
                RenderSettings.skybox = skyboxMaterialInstance;
                DynamicGI.UpdateEnvironment();
            }
        }

        void ConfigureAmbient()
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSkyColor;
            RenderSettings.ambientEquatorColor = ambientEquatorColor;
            RenderSettings.ambientGroundColor = ambientGroundColor;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 64;
            RenderSettings.reflectionIntensity = 0.15f;
        }

        void ConfigureDirectionalLight()
        {
            var directionalLight = FindDirectionalLight() ?? CreateDirectionalLight();
            if (directionalLight == null)
            {
                return;
            }

            directionalLight.color = directionalLightColor;
            directionalLight.intensity = directionalLightIntensity;
            directionalLight.type = LightType.Directional;
            directionalLight.shadows = shadowStrength > 0f ? LightShadows.Hard : LightShadows.None;
            directionalLight.shadowStrength = Mathf.Clamp01(shadowStrength);
            directionalLight.shadowBias = 0.06f;
            directionalLight.shadowNormalBias = 0.4f;
            directionalLight.shadowNearPlane = 0.2f;
            directionalLight.shadowResolution = LightShadowResolution.Low;
            directionalLight.useColorTemperature = false;
            directionalLight.transform.position = new Vector3(0f, 3f, 0f);
            directionalLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.sun = directionalLight;
        }

        GameObject EnsureFloorObject()
        {
            var floorTransform = transform.Find(FloorObjectName);
            GameObject floorObject;
            if (floorTransform == null)
            {
                floorObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floorObject.name = FloorObjectName;
                floorObject.transform.SetParent(transform, false);
            }
            else
            {
                floorObject = floorTransform.gameObject;
            }

            floorObject.layer = gameObject.layer;
            floorObject.transform.localPosition = Vector3.zero;
            floorObject.transform.localRotation = Quaternion.identity;
            floorObject.transform.localScale = floorScale;
            return floorObject;
        }

        static Light FindDirectionalLight()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
            {
                return RenderSettings.sun;
            }

            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var candidate in lights)
            {
                if (candidate.type == LightType.Directional)
                {
                    return candidate;
                }
            }

            return null;
        }

        static Light CreateDirectionalLight()
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.position = new Vector3(0f, 3f, 0f);
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            return light;
        }

        static void DestroyMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(material);
            }
            else
#endif
            {
                Object.Destroy(material);
            }

            material = null;
        }
    }
}
