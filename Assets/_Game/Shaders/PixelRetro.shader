// NES-style shader for the retro course.
//  - The world is divided into chunky "pixels" (Pixel Size, in meters) anchored in world space,
//    so the pixels stay put as you move your head (no shimmering screen-space pixelation in VR).
//  - Every pixel is snapped to a limited NES-style color palette, with ordered (Bayer) dithering
//    between neighbouring palette colors, just like 8-bit art.
//  - Vertex colors set the base color; vertex alpha adds pixel "speckles" (grass tufts, sand grains, leaves).
//  - UV0 = (checker square size in meters, checker strength) for the mowing pattern.
//  - Dithering and speckles fade out when pixels get smaller than the headset's own pixels (far away).
// Works with URP and single-pass / multiview VR rendering on Quest.
Shader "Golf/PixelRetro"
{
    Properties
    {
        _PixelSize ("Pixel Size (meters)", Float) = 0.1
        _SkyColor ("Ambient Sky", Color) = (0.75, 0.75, 0.78, 1)
        _GroundColor ("Ambient Ground", Color) = (0.45, 0.45, 0.45, 1)
        _LightStrength ("Sun Strength", Range(0, 2)) = 0.5
        _CheckerContrast ("Checker Contrast", Range(0, 0.5)) = 0.12
        _Dither ("Dithering", Range(0, 1)) = 1
        _Speckle ("Random Dark Pixels", Range(0, 1)) = 1
        _FogStrength ("Fog Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _PixelSize;
            half4 _SkyColor;
            half4 _GroundColor;
            half _LightStrength;
            half _CheckerContrast;
            half _Dither;
            half _Speckle;
            half _FogStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define PALETTE_SIZE 28
            // A subset of the classic NES palette (sRGB).
            static const float3 Palette[PALETTE_SIZE] =
            {
                float3(0.000, 0.000, 0.000), float3(0.486, 0.486, 0.486), float3(0.737, 0.737, 0.737), float3(0.988, 0.988, 0.988),
                float3(0.000, 0.345, 0.000), float3(0.000, 0.408, 0.000), float3(0.000, 0.471, 0.000), float3(0.000, 0.659, 0.000),
                float3(0.000, 0.722, 0.000), float3(0.345, 0.847, 0.329), float3(0.722, 0.973, 0.094), float3(0.847, 0.973, 0.471),
                float3(0.722, 0.973, 0.722), float3(0.000, 0.659, 0.267), float3(0.314, 0.188, 0.000), float3(0.533, 0.078, 0.000),
                float3(0.675, 0.486, 0.000), float3(0.973, 0.722, 0.000), float3(0.973, 0.847, 0.471), float3(0.988, 0.878, 0.659),
                float3(0.988, 0.627, 0.267), float3(0.659, 0.063, 0.000), float3(0.973, 0.220, 0.000), float3(0.000, 0.345, 0.973),
                float3(0.000, 0.471, 0.973), float3(0.235, 0.737, 0.988), float3(0.643, 0.894, 0.988), float3(0.000, 0.533, 0.533)
            };

            static const float BayerMatrix[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float Bayer(float2 texel)
            {
                int2 i = int2(texel - 4.0 * floor(texel / 4.0)); // positive modulo 4
                return (BayerMatrix[i.y * 4 + i.x] + 0.5) / 16.0;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ---- Find this fragment's world-space pixel (depends only on world position, never the view) ----
                float3 n = normalize(input.normalWS);
                float3 an = abs(n);
                float3 p = input.positionWS;
                float2 plane = (an.y >= an.x && an.y >= an.z) ? p.xz : ((an.x >= an.z) ? p.zy : p.xy);

                float2 texel = floor(plane / _PixelSize);
                float2 center = (texel + 0.5) * _PixelSize;
                float4 baseColor = input.color;
                float2 pattern = input.uv;

                // How many screen pixels one world pixel covers; fade detail out when it gets too small to see.
                float screenPixels = _PixelSize / max(max(length(ddx(plane)), length(ddy(plane))), 1e-5);
                float detail = saturate((screenPixels - 2.0) / 3.0);

                // ---- Surface color ----
                float3 albedo = saturate(baseColor.rgb);

                if (pattern.y > 0.01)
                {
                    float size = max(pattern.x, _PixelSize);
                    float2 square = floor(center / size);
                    float checker = abs(square.x + square.y) - 2.0 * floor(abs(square.x + square.y) * 0.5); // 0 or 1
                    albedo *= 1.0 + (checker * 2.0 - 1.0) * _CheckerContrast * saturate(pattern.y);
                }

                float speckle = saturate(baseColor.a) * detail * _Speckle;
                if (Hash(texel) < 0.08 * speckle) albedo *= 0.72;

                // ---- Lighting ----
                Light sun = GetMainLight();
                half diffuse = saturate(dot(n, sun.direction)) * _LightStrength;
                half3 ambient = lerp(_GroundColor.rgb, _SkyColor.rgb, n.y * 0.5 + 0.5);
                float3 color = albedo * (sun.color * diffuse + ambient);
                color = lerp(color, MixFog(color, input.fogFactor), _FogStrength);

                // ---- Snap to the palette with ordered dithering ----
                float3 target = LinearToSRGB(saturate(color));
                float best = 1e9, second = 1e9;
                float3 bestColor = Palette[0], secondColor = Palette[0];
                [loop]
                for (int k = 0; k < PALETTE_SIZE; k++)
                {
                    float3 d = target - Palette[k];
                    float dist = dot(d * d, float3(0.35, 0.45, 0.2));
                    if (dist < best)
                    {
                        second = best; secondColor = bestColor;
                        best = dist; bestColor = Palette[k];
                    }
                    else if (dist < second)
                    {
                        second = dist; secondColor = Palette[k];
                    }
                }

                float mixAmount = sqrt(best) / (sqrt(best) + sqrt(second) + 1e-5); // 0..0.5
                float threshold = lerp(1.0, Bayer(texel), detail * _Dither);
                float3 finalColor = threshold < mixAmount ? secondColor : bestColor;

                return half4(SRGBToLinear(finalColor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            struct AttributesDepth
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsDepth
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VaryingsDepth vertDepth(AttributesDepth input)
            {
                VaryingsDepth output = (VaryingsDepth)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half fragDepth(VaryingsDepth input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
