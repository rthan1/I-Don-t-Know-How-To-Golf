// Bright, banded gradient sky for the retro course. Assign it as the scene's skybox material.
// Works with URP and single-pass / multiview VR rendering on Quest.
Shader "Golf/RetroSky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.13, 0.42, 0.95, 1)
        _HorizonColor ("Horizon", Color) = (0.72, 0.90, 1.0, 1)
        _BottomColor ("Below Horizon", Color) = (0.55, 0.78, 0.95, 1)
        _Falloff ("Gradient Falloff", Range(0.1, 3)) = 0.55
        _ColorSteps ("Color Steps (0 = smooth)", Float) = 14
        _Bands ("Hard Bands (0 = off)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half _Falloff;
                half _ColorSteps;
                half _Bands;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float y = normalize(input.direction).y;
                float t = pow(saturate(y), _Falloff);
                // Hard bands: a few flat stripes of color, like an 8-bit sky
                if (_Bands > 0.5) t = saturate(floor(t * _Bands) / max(_Bands - 1.0, 1.0));
                half3 color = y >= 0.0
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, t)
                    : lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-y * 4.0));

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
    }

    FallBack Off
}
