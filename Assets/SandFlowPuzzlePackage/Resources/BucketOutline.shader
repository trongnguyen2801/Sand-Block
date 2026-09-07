Shader "Custom/OutlineOnly"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.08, 0.08, 0.08, 1)
        _OutlineWidth ("Outline Width", Range(0.001, 0.5)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-1" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 dir = normalize(input.positionOS.xyz);
                float3 pos = input.positionOS.xyz + dir * _OutlineWidth;
                output.positionCS = TransformObjectToHClip(pos);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
