Shader "SandFlowPuzzle/CircularGrains"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sand Grid", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _BackgroundColor ("Background", Color) = (0.333, 0.333, 0.333, 1)
        _GridSize ("Grid Size", Float) = 70
        _Radius ("Grain Radius", Range(0.1, 0.7)) = 0.46
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.15)) = 0.02

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CircularGrains"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _BackgroundColor;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _GridSize;
            float _Radius;
            float _EdgeSoftness;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 gridUV = saturate(input.texcoord) * _GridSize;
                float2 cellIndex = min(floor(gridUV), _GridSize - 1.0);
                float2 sampleUV = (cellIndex + 0.5) / _GridSize;
                fixed4 grain = tex2D(_MainTex, sampleUV) + _TextureSampleAdd;

                float2 localUV = frac(gridUV) - 0.5;
                float distanceToCenter = length(localUV);
                float antialias = max(_EdgeSoftness, fwidth(distanceToCenter));
                float circleMask = 1.0 - smoothstep(
                    _Radius - antialias,
                    _Radius + antialias,
                    distanceToCenter);
                float occupied = step(0.5, grain.a);

                fixed4 color = lerp(
                    _BackgroundColor,
                    fixed4(grain.rgb, 1.0),
                    circleMask * occupied);
                color *= input.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
