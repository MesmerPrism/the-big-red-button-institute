Shader "TheBigRedButton/CameraProjection/ProjectedCameraFeedQuadURP"
{
    Properties
    {
        _LeftRawTex ("Left Raw Texture", 2D) = "black" {}
        _RightRawTex ("Right Raw Texture", 2D) = "black" {}
        _LeftBlurTex ("Left Blur Texture", 2D) = "black" {}
        _RightBlurTex ("Right Blur Texture", 2D) = "black" {}
        _LayerMode ("Layer Mode", Range(0, 3)) = 2
        _Opacity ("Opacity", Range(0, 1)) = 1
        _PreviewEye ("Preview Eye", Range(0, 1)) = 0
        _ProjectionEdgeFade ("Projection Edge Fade", Range(0, 0.25)) = 0.015
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_LeftRawTex);
            SAMPLER(sampler_LeftRawTex);
            TEXTURE2D(_RightRawTex);
            SAMPLER(sampler_RightRawTex);
            TEXTURE2D(_LeftBlurTex);
            SAMPLER(sampler_LeftBlurTex);
            TEXTURE2D(_RightBlurTex);
            SAMPLER(sampler_RightBlurTex);

            CBUFFER_START(UnityPerMaterial)
                float _LayerMode;
                float _Opacity;
                float _PreviewEye;
                float _ProjectionEdgeFade;
                float3 _LeftCameraPos;
                float3 _RightCameraPos;
                float4x4 _LeftCameraRotationMatrix;
                float4x4 _RightCameraRotationMatrix;
                float2 _LeftFocalLength;
                float2 _RightFocalLength;
                float2 _LeftPrincipalPoint;
                float2 _RightPrincipalPoint;
                float2 _LeftSensorResolution;
                float2 _RightSensorResolution;
                float2 _LeftCurrentResolution;
                float2 _RightCurrentResolution;
                float2 _LeftUvOffset;
                float2 _RightUvOffset;
                float3 _QuadCenterWS;
                float3 _QuadRightWS;
                float3 _QuadUpWS;
                float2 _QuadSize;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 ProjectToViewport(
                float3 worldPos,
                float3 cameraPos,
                float4x4 inverseCameraRotation,
                float2 focalLength,
                float2 principalPoint,
                float2 sensorResolution,
                float2 currentResolution)
            {
                float3 localPos = mul(inverseCameraRotation, float4(worldPos - cameraPos, 1.0f)).xyz;
                if (localPos.z <= 0.0001f)
                    return float4(0.0f, 0.0f, 0.0f, 0.0f);

                float2 sensorPoint = float2(
                    (localPos.x / localPos.z) * focalLength.x + principalPoint.x,
                    (localPos.y / localPos.z) * focalLength.y + principalPoint.y);

                float2 scaleFactor = currentResolution / sensorResolution;
                scaleFactor /= max(scaleFactor.x, scaleFactor.y);

                float2 cropMin = sensorResolution * (1.0f - scaleFactor) * 0.5f;
                float2 cropSize = sensorResolution * scaleFactor;
                float2 uv = (sensorPoint - cropMin) / cropSize;
                return float4(uv, localPos.z, 1.0f);
            }

            bool TryProjectSampleUv(int eyeIndex, float3 worldPos, out float2 uv)
            {
                uv = 0.5f.xx;

                float4 projected = eyeIndex == 0
                    ? ProjectToViewport(
                        worldPos,
                        _LeftCameraPos,
                        _LeftCameraRotationMatrix,
                        _LeftFocalLength,
                        _LeftPrincipalPoint,
                        _LeftSensorResolution,
                        _LeftCurrentResolution)
                    : ProjectToViewport(
                        worldPos,
                        _RightCameraPos,
                        _RightCameraRotationMatrix,
                        _RightFocalLength,
                        _RightPrincipalPoint,
                        _RightSensorResolution,
                        _RightCurrentResolution);

                if (projected.w < 0.5f)
                    return false;

                uv = projected.xy + (eyeIndex == 0 ? _LeftUvOffset : _RightUvOffset);
                return all(uv >= 0.0f.xx) && all(uv <= 1.0f.xx);
            }

            float3 BuildQuadWorldPos(float2 contentUv)
            {
                float2 centeredUv = (contentUv * 2.0f) - 1.0f;
                return
                    _QuadCenterWS +
                    (_QuadRightWS * (centeredUv.x * _QuadSize.x * 0.5f)) +
                    (_QuadUpWS * (centeredUv.y * _QuadSize.y * 0.5f));
            }

            bool TryResolveVirtualProjection(
                float2 contentUv,
                int preferredEyeIndex,
                out float2 sampleUv,
                out int sampleEyeIndex)
            {
                float3 virtualWorldPos = BuildQuadWorldPos(contentUv);
                sampleEyeIndex = preferredEyeIndex;
                if (TryProjectSampleUv(preferredEyeIndex, virtualWorldPos, sampleUv))
                    return true;

                sampleEyeIndex = 1 - preferredEyeIndex;
                return TryProjectSampleUv(sampleEyeIndex, virtualWorldPos, sampleUv);
            }

            float3 SampleRaw(float2 uv, int eyeIndex)
            {
                return eyeIndex == 0
                    ? SAMPLE_TEXTURE2D(_LeftRawTex, sampler_LeftRawTex, uv).rgb
                    : SAMPLE_TEXTURE2D(_RightRawTex, sampler_RightRawTex, uv).rgb;
            }

            float3 SampleBlur(float2 uv, int eyeIndex)
            {
                return eyeIndex == 0
                    ? SAMPLE_TEXTURE2D(_LeftBlurTex, sampler_LeftBlurTex, uv).rgb
                    : SAMPLE_TEXTURE2D(_RightBlurTex, sampler_RightBlurTex, uv).rgb;
            }

            float3 ResolveLayerColor(float2 surfaceUv, float2 sampleUv, int sampleEyeIndex)
            {
                float3 raw = SampleRaw(sampleUv, sampleEyeIndex);
                float3 blurred = SampleBlur(sampleUv, sampleEyeIndex);
                int mode = (int)round(_LayerMode);

                if (mode == 0)
                    return raw;

                if (mode == 1)
                    return blurred;

                if (mode == 2)
                {
                    float3 splitColor = surfaceUv.x < 0.5f ? raw : blurred;
                    float separator = 1.0f - smoothstep(0.0f, 0.006f, abs(surfaceUv.x - 0.5f));
                    return lerp(splitColor, 1.0f.xxx, separator * 0.8f);
                }

                return saturate(abs(raw - blurred) * 2.0f);
            }

            float ResolveEdgeFade(float2 sampleUv)
            {
                if (_ProjectionEdgeFade <= 0.0001f)
                    return 1.0f;

                float borderDistance = min(min(sampleUv.x, sampleUv.y), min(1.0f - sampleUv.x, 1.0f - sampleUv.y));
                return smoothstep(0.0f, _ProjectionEdgeFade, borderDistance);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int preferredEyeIndex = 0;
                #if defined(UNITY_SINGLE_PASS_STEREO) || defined(STEREO_INSTANCING_ON) || defined(STEREO_MULTIVIEW_ON)
                    preferredEyeIndex = unity_StereoEyeIndex;
                #else
                    preferredEyeIndex = _PreviewEye < 0.5f ? 0 : 1;
                #endif

                float2 sampleUv;
                int sampleEyeIndex;
                if (!TryResolveVirtualProjection(saturate(input.uv), preferredEyeIndex, sampleUv, sampleEyeIndex))
                    discard;

                float3 color = ResolveLayerColor(input.uv, sampleUv, sampleEyeIndex);
                float alpha = _Opacity * ResolveEdgeFade(sampleUv);
                return half4(saturate(color), saturate(alpha));
            }
            ENDHLSL
        }
    }
}
