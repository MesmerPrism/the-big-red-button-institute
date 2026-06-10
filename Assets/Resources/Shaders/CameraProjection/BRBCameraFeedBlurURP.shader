Shader "Hidden/TheBigRedButton/CameraProjection/CameraFeedBlurURP"
{
    Properties
    {
        _MainTex ("Input", 2D) = "black" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Blur"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _BlurRadiusTexels;
                float _BlurSigma;
                float4 _BlurDirection;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float blurRadiusTexels = max(_BlurRadiusTexels, 0.0f);
                if (blurRadiusTexels <= 0.0001f)
                {
                    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                }

                const int KernelRadius = 4;
                float sigmaTexels = max(_BlurSigma, 0.1f);
                float stepTexels = max(blurRadiusTexels / (float)KernelRadius, 1.0f);
                float2 direction = normalize(max(abs(_BlurDirection.xy), 0.0001.xx) * sign(_BlurDirection.xy));
                float2 uvStep = direction * _MainTex_TexelSize.xy * stepTexels;

                float4 weightedSum = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float weightSum = 1.0f;

                [unroll]
                for (int i = 1; i <= KernelRadius; i++)
                {
                    float distanceTexels = (float)i * stepTexels;
                    float weight = exp(-(distanceTexels * distanceTexels) / (2.0f * sigmaTexels * sigmaTexels));
                    float2 offset = uvStep * (float)i;
                    float4 positive = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(input.uv + offset));
                    float4 negative = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(input.uv - offset));
                    weightedSum += (positive + negative) * weight;
                    weightSum += 2.0f * weight;
                }

                float4 blurred = weightSum > 0.0001f ? weightedSum / weightSum : 0.0f.xxxx;
                return half4(saturate(blurred.rgb), 1.0h);
            }
            ENDHLSL
        }
    }
}
