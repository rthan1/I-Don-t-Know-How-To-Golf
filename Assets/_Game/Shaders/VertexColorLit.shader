// Simple, cheap lit shader that colors a mesh using its vertex colors.
// Used for the generated golf course terrain, trees, and props (colors are baked into the meshes).
// Works with URP, fog, and single-pass / multiview VR rendering on Quest.
//
// Retro extras (all optional, per material):
//  - Checkerboard mowing pattern: meshes store (checker size in meters, strength 0..1) in UV0.
//  - Color Steps: posterizes the final color for a retro, limited-color look (0 = off).
//  - Fog Strength: lets far-away things like clouds ignore most of the fog.
Shader "Golf/VertexColorLit"
{
    Properties
    {
        _SkyColor ("Ambient Sky", Color) = (0.62, 0.70, 0.82, 1)
        _GroundColor ("Ambient Ground", Color) = (0.30, 0.29, 0.24, 1)
        _LightStrength ("Sun Strength", Range(0, 2)) = 0.85
        _CheckerContrast ("Checker Contrast", Range(0, 0.5)) = 0.08
        _ColorSteps ("Color Steps (0 = off)", Float) = 0
        _FogStrength ("Fog Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _SkyColor;
            half4 _GroundColor;
            half _LightStrength;
            half _CheckerContrast;
            half _ColorSteps;
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
                half4 color : COLOR;
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

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 albedo = input.color.rgb;

                // Anti-aliased world-space checkerboard (mowing pattern)
                if (input.uv.y > 0.001)
                {
                    float2 q = input.positionWS.xz * (PI / max(input.uv.x, 0.05));
                    float s = sin(q.x) * sin(q.y);
                    float w = fwidth(s) + 1e-4;
                    float checker = smoothstep(-w, w, s) * 2.0 - 1.0;
                    albedo *= 1.0 + checker * _CheckerContrast * saturate(input.uv.y);
                }

                float3 n = normalize(input.normalWS);
                Light sun = GetMainLight();
                half diffuse = saturate(dot(n, sun.direction)) * _LightStrength;
                half3 ambient = lerp(_GroundColor.rgb, _SkyColor.rgb, n.y * 0.5 + 0.5);
                half3 color = albedo * (sun.color * diffuse + ambient);

                color = lerp(color, MixFog(color, input.fogFactor), _FogStrength);

                // Retro posterize (done in display/sRGB space so the steps look even)
                if (_ColorSteps > 0.5)
                {
                    half3 g = LinearToSRGB(saturate(color));
                    g = floor(g * _ColorSteps + 0.5) / _ColorSteps;
                    color = SRGBToLinear(g);
                }

                return half4(color, 1);
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
