Shader "SandFlowPuzzle/FlyingGrains"
{
    Properties
    {
        _Color ("Grain Color", Color) = (1, 1, 1, 1)
        _LightDirection ("Light Direction (Camera XYZ)", Vector) = (-0.45, 0.6, 0.8, 0)
        _LightColor ("Light Color", Color) = (1, 0.97, 0.92, 1)
        _AmbientStrength ("Ambient Light", Range(0, 1)) = 0.32
        _DiffuseStrength ("Diffuse Light", Range(0, 1.5)) = 0.8
        _SpecularStrength ("Soft Highlight", Range(0, 1)) = 0.28
        _Shininess ("Highlight Sharpness", Range(4, 64)) = 20
        _RimStrength ("Edge Reflection", Range(0, 0.5)) = 0.08
        _ContactShade ("Grain Edge Occlusion", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "FlyingGrains"
            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                half3 normal : TEXCOORD0;
                half3 lightDirection : TEXCOORD1;
                half3 halfDirection : TEXCOORD2;
                float3 viewDirection : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _Color;
            float4 _LightDirection;
            half4 _LightColor;
            half _AmbientStrength;
            half _DiffuseStrength;
            half _SpecularStrength;
            half _Shininess;
            half _RimStrength;
            half _ContactShade;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.normal = mul((float3x3)UNITY_MATRIX_V, UnityObjectToWorldNormal(input.normal));
                // View space has +Z toward the camera, like the grid's fake hemisphere.
                float3 lightDirection = _LightDirection.xyz;
                lightDirection.z = max(lightDirection.z, 0.001);
                output.lightDirection = normalize(lightDirection);
                float3 viewPosition = UnityObjectToViewPos(input.vertex);
                output.viewDirection = lerp(-viewPosition, float3(0, 0, 1), unity_OrthoParams.w);
                output.halfDirection = normalize(output.lightDirection + float3(0, 0, 1));
                return output;
            }

            half4 frag(v2f input) : SV_Target
            {
                half3 normal = normalize(input.normal);
                half facing = saturate(dot(normal, normalize(input.viewDirection)));
                half diffuse = saturate(dot(normal, input.lightDirection));
                half edge = 1.0h - facing;
                half edgeSquared = edge * edge;
                half occlusion = 1.0h - _ContactShade * edgeSquared;
                half specular = pow(saturate(dot(normal, input.halfDirection)), _Shininess);
                // Match CircularGrains without scene lighting, textures, or shadow passes.
                half3 color = _Color.rgb *
                    (_AmbientStrength + _DiffuseStrength * diffuse * _LightColor.rgb) * occlusion;
                color += _LightColor.rgb * (_SpecularStrength * specular * diffuse
                    + _RimStrength * edgeSquared * facing);
                return half4(color, 1);
            }
            ENDCG
        }
    }
}
