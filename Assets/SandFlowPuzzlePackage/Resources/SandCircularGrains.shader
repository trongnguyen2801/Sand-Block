Shader "SandFlowPuzzle/CircularGrains"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sand Grid", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _BackgroundColor ("Background", Color) = (0.333, 0.333, 0.333, 1)
        _GrainBackgroundOpacity ("Grain Background Opacity", Range(0, 1)) = 0.4
        _GridSize ("Grid Size", Float) = 70
        _Radius ("Grain Radius", Range(0.1, 0.7)) = 0.58
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.15)) = 0.02
        _StrokeWidth ("Stroke Width (Cell Units)", Range(0, 0.1)) = 0.03
        _StrokeStrength ("Stroke Strength", Range(0, 1)) = 0.7
        _ContactStrokeRemoval ("Contact Stroke Removal", Range(0, 1)) = .8
        _LightDirection ("Light Direction (Canvas XYZ)", Vector) = (0.45, 0.6, 0.8, 0)
        _LightColor ("Light Color", Color) = (1, 0.97, 0.92, 1)
        _AmbientStrength ("Ambient Light", Range(0, 1)) = 0.22
        _DiffuseStrength ("Diffuse Light", Range(0, 1.5)) = 0.7
        _SpecularStrength ("Soft Highlight", Range(0, 1)) = 0.12
        _Shininess ("Highlight Sharpness", Range(4, 64)) = 20
        _RimStrength ("Edge Reflection", Range(0, 0.5)) = 0.08
        _ContactShade ("Grain Edge Occlusion", Range(0, 1)) = 0.22
        _ShadowStrength ("Contact Shadow", Range(0, 1)) = 0.3

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
            #pragma target 3.0
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
                half3 lightDirection : TEXCOORD2;
                half3 halfDirection : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _BackgroundColor;
            half _GrainBackgroundOpacity;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _GridSize;
            float _Radius;
            float _EdgeSoftness;
            float _StrokeWidth;
            half _StrokeStrength;
            half _ContactStrokeRemoval;
            float4 _LightDirection;
            half4 _LightColor;
            half _AmbientStrength;
            half _DiffuseStrength;
            half _SpecularStrength;
            half _Shininess;
            half _RimStrength;
            half _ContactShade;
            half _ShadowStrength;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                // A fixed canvas-space light keeps the UI independent of scene lights.
                // Normalize uniform directions at the vertices, not for every grain pixel.
                float3 lightDirection = _LightDirection.xyz;
                lightDirection.z = max(lightDirection.z, 0.001);
                output.lightDirection = normalize(lightDirection);
                output.halfDirection = normalize(output.lightDirection + float3(0, 0, 1));
                return output;
            }

            // Shared shading keeps a grain continuous when its cap crosses a row.
            fixed4 ShadeGrain(fixed4 grain, float2 localUV, float2 grainIndex, v2f input)
            {
                float distanceToCenter = length(localUV);
                float antialias = max(_EdgeSoftness, fwidth(distanceToCenter));
                float circleMask = 1.0 - smoothstep(_Radius - antialias,
                    _Radius + antialias, distanceToCenter);
                // Analytic sphere impostor: the visible hemisphere supplies a mesh-like
                // normal without geometry, normal maps, or extra texture samples.
                float2 sphereXY = localUV / max(_Radius, 0.001);
                float radiusSquared = dot(sphereXY, sphereXY);
                // Keep the normal unit length in the antialiased fringe as well.
                half3 normal = half3(sphereXY * rsqrt(max(1.0, radiusSquared)),
                    sqrt(saturate(1.0 - radiusSquared)));
                half diffuse = saturate(dot(normal, input.lightDirection));
                half edge = 1.0h - normal.z;
                half edgeSquared = edge * edge;
                half occlusion = 1.0h - _ContactShade * edgeSquared;
                half specular = pow(saturate(dot(normal, input.halfDirection)), _Shininess);
                half3 grainColor = grain.rgb *
                    (_AmbientStrength + _DiffuseStrength * diffuse * _LightColor.rgb) * occlusion;
                grainColor += _LightColor.rgb * (_SpecularStrength * specular * diffuse
                    + _RimStrength * edgeSquared * normal.z);

                // Keep the outline only where no neighboring grain touches it.
                float innerRadius = max(_Radius - _StrokeWidth, 0.0);
                float innerMask = 1.0 - smoothstep(innerRadius - antialias,
                    innerRadius + antialias, distanceToCenter);
                half strokeMask = saturate((circleMask - innerMask) / max(circleMask, 0.001));
                float gridSize = max(_GridSize, 1.0);
                float2 contactStep = step(0.0, localUV) * 2.0 - 1.0;
                float2 contactX = grainIndex + float2(contactStep.x, 0);
                float2 contactY = grainIndex + float2(0, contactStep.y);
                half occupiedX = step(0.5, tex2D(_MainTex,
                    (clamp(contactX, 0.0, gridSize - 1.0) + 0.5) / gridSize).a + _TextureSampleAdd.a);
                half occupiedY = step(0.5, tex2D(_MainTex,
                    (clamp(contactY, 0.0, gridSize - 1.0) + 0.5) / gridSize).a + _TextureSampleAdd.a);
                occupiedX *= step(0.0, contactX.x) * step(contactX.x, gridSize - 1.0);
                occupiedY *= step(0.0, contactY.y) * step(contactY.y, gridSize - 1.0);
                float2 contactDistance = float2(
                    length(localUV - float2(contactStep.x, 0)),
                    length(localUV - float2(0, contactStep.y)));
                // Smaller values narrow the contact region; zero preserves all stroke.
                // One matches the original full contact removal.
                float contactRadius = (_Radius + _StrokeWidth) * _ContactStrokeRemoval;
                float2 contactMask = 1.0 - smoothstep(contactRadius,
                    contactRadius + antialias, contactDistance);
                contactMask *= step(0.001, _ContactStrokeRemoval);
                strokeMask *= 1.0h - max(contactMask.x * occupiedX, contactMask.y * occupiedY);
                grainColor *= 1.0h - strokeMask * _StrokeStrength;

                return fixed4(grainColor, circleMask * step(0.5, grain.a));
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float gridSize = max(_GridSize, 1.0);
                // Extended mesh UVs preserve the round caps beyond the board edge.
                float2 gridUV = input.texcoord * gridSize;
                float2 cellIndex = clamp(floor(gridUV), 0.0, gridSize - 1.0);
                float insideBoard = step(0.0, gridUV.x) * step(gridUV.x, gridSize)
                    * step(0.0, gridUV.y) * step(gridUV.y, gridSize);
                float2 sampleUV = (cellIndex + 0.5) / gridSize;
                fixed4 grain = tex2D(_MainTex, sampleUV) + _TextureSampleAdd;

                float2 localUV = gridUV - cellIndex - 0.5;
                float occupied = step(0.5, grain.a);

                // A cheap soft shadow on the background, shifted away from the light.
                // Fade at cell boundaries so adjacent empty cells never show a hard seam.
                float2 shadowUV = localUV + input.lightDirection.xy * (_Radius * 0.16);
                float shadowDistance = length(shadowUV) / max(_Radius, 0.001);
                half shadow = 1.0 - smoothstep(0.65, 1.3, shadowDistance);
                float cellEdge = 0.5 - max(abs(localUV.x), abs(localUV.y));
                shadow *= smoothstep(0.0, 0.08, cellEdge) * occupied * _ShadowStrength;
                // Interpolate the background between neighboring grain centers even
                // though the grid texture uses point filtering for the circles.
                float2 neighborStep = step(0.0, localUV) * 2.0 - 1.0;
                float2 neighborIndex = clamp(cellIndex + neighborStep, 0.0, gridSize - 1.0);
                fixed4 neighborX = tex2D(_MainTex,
                    (float2(neighborIndex.x, cellIndex.y) + 0.5) / gridSize) + _TextureSampleAdd;
                fixed4 neighborY = tex2D(_MainTex,
                    (float2(cellIndex.x, neighborIndex.y) + 0.5) / gridSize) + _TextureSampleAdd;
                fixed4 neighborXY = tex2D(_MainTex,
                    (neighborIndex + 0.5) / gridSize) + _TextureSampleAdd;
                float2 blend = min(abs(localUV), 0.5);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float4 weights = float4(
                    (1.0 - blend.x) * (1.0 - blend.y),
                    blend.x * (1.0 - blend.y),
                    (1.0 - blend.x) * blend.y,
                    blend.x * blend.y);
                // Exclude empty cells so their stored RGB cannot muddy the gradient.
                weights *= step(0.5, float4(grain.a, neighborX.a, neighborY.a, neighborXY.a));
                half3 backgroundGrainColor = (grain.rgb * weights.x
                    + neighborX.rgb * weights.y + neighborY.rgb * weights.z
                    + neighborXY.rgb * weights.w) / max(dot(weights, float4(1, 1, 1, 1)), 0.001);
                half3 background = lerp(_BackgroundColor.rgb, backgroundGrainColor,
                    occupied * _GrainBackgroundOpacity) * (1.0h - shadow);

                // Texture rows increase upward (the simulator flips its grid on upload).
                // Composite back to front: upper row, current row, then lower row.
                // Only vertical neighbors cross cell boundaries; horizontal contacts stay cropped.
                fixed4 upperGrain = tex2D(_MainTex,
                    (float2(cellIndex.x, min(cellIndex.y + 1.0, gridSize - 1.0)) + 0.5)
                    / gridSize) + _TextureSampleAdd;
                fixed4 lowerGrain = tex2D(_MainTex,
                    (float2(cellIndex.x, max(cellIndex.y - 1.0, 0.0)) + 0.5)
                    / gridSize) + _TextureSampleAdd;
                fixed4 upperLayer = ShadeGrain(upperGrain, localUV - float2(0, 1), cellIndex + float2(0, 1), input);
                fixed4 currentLayer = ShadeGrain(grain, localUV, cellIndex, input);
                fixed4 lowerLayer = ShadeGrain(lowerGrain, localUV + float2(0, 1), cellIndex - float2(0, 1), input);
                upperLayer.a *= step(cellIndex.y + 1.0, gridSize - 1.0);
                lowerLayer.a *= step(1.0, cellIndex.y);

                // Composite in premultiplied form, then return straight alpha for
                // SrcAlpha blending. Outside the board only grain caps cover the frame.
                half backgroundAlpha = _BackgroundColor.a * insideBoard;
                fixed4 color = fixed4(background * backgroundAlpha, backgroundAlpha);
                color = color * (1.0 - upperLayer.a) + fixed4(upperLayer.rgb * upperLayer.a, upperLayer.a);
                color = color * (1.0 - currentLayer.a) + fixed4(currentLayer.rgb * currentLayer.a, currentLayer.a);
                color = color * (1.0 - lowerLayer.a) + fixed4(lowerLayer.rgb * lowerLayer.a, lowerLayer.a);
                color.rgb /= max(color.a, 0.001);
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
