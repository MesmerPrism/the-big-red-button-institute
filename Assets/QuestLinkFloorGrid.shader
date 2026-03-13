Shader "TheBigRedButton/QuestLinkFloorGrid"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.17, 0.18, 0.19, 1)
        _LineColor("Line Color", Color) = (0.30, 0.31, 0.33, 1)
        _MinorCellSize("Minor Cell Size", Float) = 1
        _MajorCellSize("Major Cell Size", Float) = 5
        _MinorLineWidth("Minor Line Width", Range(0.001, 0.1)) = 0.01
        _MajorLineWidth("Major Line Width", Range(0.001, 0.1)) = 0.016
        _FadeStart("Fade Start", Float) = 42
        _FadeEnd("Fade End", Float) = 78
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LineColor;
                float _MinorCellSize;
                float _MajorCellSize;
                float _MinorLineWidth;
                float _MajorLineWidth;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half GridMask(float2 worldXZ, float cellSize, float lineWidth)
            {
                float safeCellSize = max(cellSize, 0.0001);
                float2 gridUv = worldXZ / safeCellSize;
                float2 gridCell = frac(gridUv);
                float2 distanceToLine = min(gridCell, 1.0 - gridCell);
                float2 antiAlias = max(fwidth(gridUv), float2(0.0001, 0.0001));
                float2 gridAxisMask = 1.0 - smoothstep(lineWidth, lineWidth + antiAlias, distanceToLine);
                return saturate(max(gridAxisMask.x, gridAxisMask.y));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 worldXZ = input.positionWS.xz;
                half minorGrid = GridMask(worldXZ, _MinorCellSize, _MinorLineWidth) * 0.45h;
                half majorGrid = GridMask(worldXZ, _MajorCellSize, _MajorLineWidth);

                float cameraDistance = distance(worldXZ, _WorldSpaceCameraPos.xz);
                half fade = 1.0h - smoothstep(_FadeStart, _FadeEnd, cameraDistance);
                half lineMask = saturate(max(minorGrid, majorGrid) * fade);

                half3 color = lerp(_BaseColor.rgb, _LineColor.rgb, lineMask);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
