Shader "TheBigRedButtonInstitute/Indirect Particles/URP Coordinate Billboard"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _RadialClip ("Radial Clip", Range(0, 1)) = 1
        _ParticleTex ("Particle Texture", 2D) = "white" {}
        _UseParticleTexture ("Use Particle Texture", Float) = 0
        _ParticleTextureAlphaMode ("Particle Texture Alpha Mode", Float) = 0
        [HideInInspector] _SrcBlend ("_SrcBlend", Float) = 5
        [HideInInspector] _DstBlend ("_DstBlend", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma only_renderers vulkan
            #pragma multi_compile _ STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            float4 _Tint;
            float _RadialClip;
            float _UseParticleTexture;
            float _ParticleTextureAlphaMode;
            TEXTURE2D(_ParticleTex);
            SAMPLER(sampler_ParticleTex);

            struct ParticleGPU
            {
                float3 positionWS;
                float size;
                float4 color;
                float rotation;
                float frame;
                float aux0;
                float aux1;
            };

            StructuredBuffer<ParticleGPU> _Particles;
            StructuredBuffer<uint> _IndexRemap;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half2 uv : TEXCOORD0;
                half4 color : COLOR0;
                float frame : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                InitIndirectDrawArgs(0);
                uint id = GetIndirectInstanceID(input.instanceID);
                uint src = _IndexRemap[id];
                ParticleGPU p = _Particles[src];

                float s, c;
                sincos(p.rotation, s, c);
                float2 local = input.positionOS.xy * p.size;
                float2 rotated = float2(local.x * c - local.y * s, local.x * s + local.y * c);

                float3 cameraRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 cameraUp = UNITY_MATRIX_I_V._m01_m11_m21;
                float3 world = p.positionWS + cameraRight * rotated.x + cameraUp * rotated.y;

                output.positionCS = TransformWorldToHClip(world);
                output.uv = (half2)input.uv;
                output.color = (half4)(p.color * _Tint);
                output.frame = p.frame;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half2 centered = input.uv * 2.0h - 1.0h;
                half radiusSq = dot(centered, centered);
                half limit = lerp(2.0h, 1.0h, saturate((half)_RadialClip));
                clip(limit - radiusSq);

                half pulse = (half)(0.75 + 0.25 * sin(input.frame * 6.2831853));
                half alpha = saturate(input.color.a);
                if (_UseParticleTexture > 0.5)
                {
                    half4 particleTex = SAMPLE_TEXTURE2D(_ParticleTex, sampler_ParticleTex, input.uv);
                    half luminanceAlpha = dot(particleTex.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                    half textureAlpha = lerp(particleTex.a, luminanceAlpha, step(0.5h, (half)_ParticleTextureAlphaMode));
                    alpha *= saturate(textureAlpha);
                }

                clip(alpha - 0.001h);
                return half4(input.color.rgb * pulse * alpha, alpha);
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma only_renderers d3d11

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            float4 _Tint;
            float _RadialClip;
            float _UseParticleTexture;
            float _ParticleTextureAlphaMode;
            TEXTURE2D(_ParticleTex);
            SAMPLER(sampler_ParticleTex);

            struct ParticleGPU
            {
                float3 positionWS;
                float size;
                float4 color;
                float rotation;
                float frame;
                float aux0;
                float aux1;
            };

            StructuredBuffer<ParticleGPU> _Particles;
            StructuredBuffer<uint> _IndexRemap;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half2 uv : TEXCOORD0;
                half4 color : COLOR0;
                float frame : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                InitIndirectDrawArgs(0);
                uint id = GetIndirectInstanceID(input.instanceID);
                uint src = _IndexRemap[id];
                ParticleGPU p = _Particles[src];

                float s, c;
                sincos(p.rotation, s, c);
                float2 local = input.positionOS.xy * p.size;
                float2 rotated = float2(local.x * c - local.y * s, local.x * s + local.y * c);

                float3 cameraRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 cameraUp = UNITY_MATRIX_I_V._m01_m11_m21;
                float3 world = p.positionWS + cameraRight * rotated.x + cameraUp * rotated.y;

                output.positionCS = TransformWorldToHClip(world);
                output.uv = (half2)input.uv;
                output.color = (half4)(p.color * _Tint);
                output.frame = p.frame;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half2 centered = input.uv * 2.0h - 1.0h;
                half radiusSq = dot(centered, centered);
                half limit = lerp(2.0h, 1.0h, saturate((half)_RadialClip));
                clip(limit - radiusSq);

                half pulse = (half)(0.75 + 0.25 * sin(input.frame * 6.2831853));
                half alpha = saturate(input.color.a);
                if (_UseParticleTexture > 0.5)
                {
                    half4 particleTex = SAMPLE_TEXTURE2D(_ParticleTex, sampler_ParticleTex, input.uv);
                    half luminanceAlpha = dot(particleTex.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                    half textureAlpha = lerp(particleTex.a, luminanceAlpha, step(0.5h, (half)_ParticleTextureAlphaMode));
                    alpha *= saturate(textureAlpha);
                }

                clip(alpha - 0.001h);
                return half4(input.color.rgb * pulse * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
