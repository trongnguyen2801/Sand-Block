Shader "Custom/HoleShader"
{
    Properties
    {
        _MainTex ("SDF Texture", 2D) = "black" {}
        _Color ("Main Color", Color) = (0, 0.5, 1, 1)
        _BottomColor ("Bottom Color", Color) = (0, 0, 0, 1)
        _BottomDarkness ("Bottom Darkness", Range(0, 1)) = 0.5
        _OutlineWidth ("Outline Width", Range(0, 0.5)) = 0.06
        _OutlineColor ("Outline Color", Color) = (1, 0.85, 0.85, 1)
        _OutlineBrightness ("Outline Brightness", Range(0, 2)) = 1.2
        _EdgeSmoothness ("Edge Smoothness", Range(0.001, 0.05)) = 0.01
        _Height ("Object Height", Float) = 0.2
        _VisualScale ("Visual Scale", Float) = 1.0
        _DarkGradientPower ("Dark Gradient Power", Range(1, 4)) = 2.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionOS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _BottomColor;
                float _BottomDarkness;
                float _OutlineWidth;
                float4 _OutlineColor;
                float _OutlineBrightness;
                float _EdgeSmoothness;
                float _Height;
                float _VisualScale;
                float _DarkGradientPower;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Vertical Gradient for sides with more dark area
                float heightFactor = saturate(input.positionOS.y / _Height);
                // Apply power function to keep more area dark
                heightFactor = pow(heightFactor, _DarkGradientPower);
                half4 sideGradient = lerp(_BottomColor * _BottomDarkness, _Color, heightFactor);

                // Check if this is the top face
                bool isTop = input.color.r > 0.5;

                if (isTop)
                {
                    float sdf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                    
                    // Main Body: sdf > 0.5
                    // Outline Area: 0.5 - width < sdf < 0.5
                    // Visible if sdf > 0.5 - width
                    
                    // Calculate total mask (body + outline)
                    float visibleEdge = 0.5 - _OutlineWidth;
                    float totalMask = smoothstep(visibleEdge - _EdgeSmoothness, visibleEdge + _EdgeSmoothness, sdf);
                    
                    // Clip outside
                    clip(totalMask - 0.01);
                    
                    // Body Mask (Inner part)
                    float bodyEdge = 0.5;
                    float bodyMask = smoothstep(bodyEdge - _EdgeSmoothness, bodyEdge + _EdgeSmoothness, sdf);
                    
                    // Outline Only Mask
                    float outlineMask = totalMask - bodyMask;
                    outlineMask = saturate(outlineMask);
                    
                    // Fill gradient with more dark area
                    // Apply power function to UV.y to compress the bright area
                    float gradientFactor = pow(input.uv.y, _DarkGradientPower);
                    half4 darkBottom = _BottomColor * _BottomDarkness;
                    half4 fillColor = lerp(darkBottom, _Color, gradientFactor);
                    
                    // Outline Color - Lighter than main
                    half4 outlineColor = lerp(_Color, half4(1,1,1,1), 0.3);
                    outlineColor.a = 1;
                    
                    // Combine
                    half4 finalColor = lerp(outlineColor, fillColor, bodyMask);
                    
                    return finalColor;
                }
                else
                {
                    return sideGradient;
                }
            }
            ENDHLSL
        }
    }
}