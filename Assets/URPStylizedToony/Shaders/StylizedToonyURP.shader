Shader "Custom/URP Stylized Toony"
{
    Properties
    {
        _MainTex ("Texture Image", 2D) = "white" {}
        _Color ("Texture Color", Color) = (1,1,1,1)
        _DiffuseColor ("Diffuse Color", Color) = (1,1,1,1)
        _SpecColor ("Specular Material Color", Color) = (1,1,1,1)
        _Shininess ("Specular Shininess", Range(0,1)) = 0.5
        _SpecIntensity ("Specular Intensity", Range(0,1)) = 1
        _Mask ("Mask", 2D) = "white" {}
        _AO ("Ambient Occlusion", 2D) = "white" {}
        _ShadowTint ("Shadow Tint Color", Color) = (0,0,0,0)
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0,10)) = 3
        [Toggle] _IsUseEmission ("Is Use Emission", Float) = 0
        _Emission ("Emission (RGB)", 2D) = "black" {}
        _IlluminPower ("Illumination Power", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Mask);
            SAMPLER(sampler_Mask);
            TEXTURE2D(_AO);
            SAMPLER(sampler_AO);
            TEXTURE2D(_Emission);
            SAMPLER(sampler_Emission);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _DiffuseColor;
                float4 _SpecColor;
                float _Shininess;
                float _SpecIntensity;
                float4 _ShadowTint;
                float4 _RimColor;
                float _RimPower;
                float _IsUseEmission;
                float _IlluminPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uvMain : TEXCOORD0;
                float2 uvAO : TEXCOORD1;
                float2 uvEmission : TEXCOORD2;
                float2 lightmapUV : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uvMain : TEXCOORD2;
                float2 uvAO : TEXCOORD3;
                float2 uvEmission : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                half fogFactor : TEXCOORD6;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uvMain = TRANSFORM_TEX(input.uvMain, _MainTex);
                output.uvAO = input.uvAO;
                output.uvEmission = input.uvEmission;
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 baseSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uvMain) * _Color;
                half3 ao = SAMPLE_TEXTURE2D(_AO, sampler_AO, input.uvAO).rgb;
                half mask = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, input.uvAO).r;

                half3 normalWS = normalize(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                half3 lightDir = normalize(mainLight.direction);
                half ndotl = saturate(dot(normalWS, lightDir));

                // Toon-like diffuse tint based on the original shader's controls.
                half3 diffuseTint = lerp((half3)_DiffuseColor.rgb, half3(1,1,1), ndotl);
                half3 direct = baseSample.rgb * ao * diffuseTint * mainLight.color;

                half3 bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS);
                half3 indirect = baseSample.rgb * ao * bakedGI * 0.15h;
                half shadowAttenuation = mainLight.shadowAttenuation;

                half specExponent = max(1.0h, _Shininess * _Shininess * 100.0h + 1.0h);
                half specular = pow(saturate(dot(reflect(-lightDir, normalWS), viewDir)), specExponent);
                half rim = pow(saturate(1.0h - dot(normalWS, viewDir)), max(0.001h, _RimPower));

                half3 highlights =
                    (specular * _SpecColor.rgb * _SpecIntensity +
                     rim * _RimColor.rgb) * mainLight.color * mask;

                half3 color = lerp(_ShadowTint.rgb, direct + indirect + highlights, shadowAttenuation);

                if (_IsUseEmission > 0.5h)
                {
                    half3 emissionTex = SAMPLE_TEXTURE2D(_Emission, sampler_Emission, input.uvEmission).rgb;
                    color += emissionTex * _IlluminPower;
                }

                color = MixFog(color, input.fogFactor);
                return half4(color, baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 _LightDirection;
            float3 _LightPosition;

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                positionWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
                output.positionCS = TransformWorldToHClip(positionWS);

                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
