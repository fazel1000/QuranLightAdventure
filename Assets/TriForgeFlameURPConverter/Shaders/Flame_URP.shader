Shader "TriForge/Flame URP"
{
    Properties
    {
        [MainTexture] _MainTex ("Particle Texture", 2D) = "white" {}
        [HDR] _TintColor ("Tint Color", Color) = (1, 0.35, 0.05, 0.5)
        _DistortionStrength ("Distortion Strength", Range(0, 1)) = 0.191
        _DissolveStrength ("Dissolve Strength", Range(0, 10)) = 1
        _DissolveScale ("Dissolve Scale", Range(0, 10)) = 2
        _DistortionScale ("Distortion Scale", Range(0, 10)) = 2
        _EffectSpeed ("Effect Speed", Range(0.1, 10)) = 1
        _Brightness ("Brightness", Range(0, 4)) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FlameForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TintColor;
                float _DistortionStrength;
                float _DissolveStrength;
                float _DissolveScale;
                float _DistortionScale;
                float _EffectSpeed;
                float _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color : COLOR;
                half fogFactor : TEXCOORD2;
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
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            float3 Mod289(float3 x)
            {
                return x - floor(x * (1.0 / 289.0)) * 289.0;
            }

            float2 Mod289(float2 x)
            {
                return x - floor(x * (1.0 / 289.0)) * 289.0;
            }

            float3 Permute(float3 x)
            {
                return Mod289(((x * 34.0) + 1.0) * x);
            }

            float SimplexNoise2D(float2 v)
            {
                const float4 C = float4(0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439);

                float2 i = floor(v + dot(v, C.yy));
                float2 x0 = v - i + dot(i, C.xx);
                float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);

                float4 x12 = x0.xyxy + C.xxzz;
                x12.xy -= i1;
                i = Mod289(i);

                float3 p = Permute(Permute(i.y + float3(0.0, i1.y, 1.0)) + i.x + float3(0.0, i1.x, 1.0));
                float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
                m = m * m;
                m = m * m;

                float3 x = 2.0 * frac(p * C.www) - 1.0;
                float3 h = abs(x) - 0.5;
                float3 ox = floor(x + 0.5);
                float3 a0 = x - ox;

                m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);

                float3 g;
                g.x = a0.x * x0.x + h.x * x0.y;
                g.yz = a0.yz * x12.xz + h.yz * x12.yw;

                return 130.0 * dot(m, g);
            }

            float2 VoronoiHash(float2 p)
            {
                p = float2(
                    dot(p, float2(127.1, 311.7)),
                    dot(p, float2(269.5, 183.3))
                );
                return frac(sin(p) * 43758.5453);
            }

            float Voronoi23(float2 v, float time, inout float2 cellId)
            {
                float2 n = floor(v);
                float2 f = frac(v);
                float closestDistance = 8.0;

                [unroll]
                for (int j = -1; j <= 1; j++)
                {
                    [unroll]
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 g = float2(i, j);
                        float2 o = VoronoiHash(n + g);
                        o = sin(time + o * 6.2831) * 0.5 + 0.5;

                        float2 r = g - f + o;
                        float d = 0.5 * dot(r, r);

                        if (d < closestDistance)
                        {
                            closestDistance = d;
                            cellId = o;
                        }
                    }
                }

                return closestDistance;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Keep the source shader's world-position/time-driven distortion.
                float3 animatedWorldPosition = (_Time.y * _EffectSpeed) + (input.positionWS * 2.0);
                float2 noiseUV = input.uv + (float2(0.0, -0.3) * animatedWorldPosition).xy;

                float noise = SimplexNoise2D(noiseUV * _DistortionScale);
                noise = noise * 0.5 + 0.5;

                // This mirrors the original Flame shader's UV distortion calculation.
                float2 distortedUV = lerp(input.uv, noise.xx, _DistortionStrength);
                float4 particleTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distortedUV);

                // Match the original shader's Voronoi dissolve pattern.
                float2 cellId = 0.0;
                float voronoi = Voronoi23(input.uv * _DissolveScale, 0.0, cellId);
                float effect = noise * pow(max(voronoi, 0.0), _DissolveStrength);

                float3 rgb = particleTex.rgb * effect * _TintColor.rgb * input.color.rgb;
                rgb *= _Brightness;

                float depthFade = 1.0;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneRawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                float fragmentEyeDepth = -TransformWorldToView(input.positionWS).z;
                depthFade = saturate(abs(sceneEyeDepth - fragmentEyeDepth) / 3.0);

                float alpha = saturate(particleTex.a * effect * input.color.a * depthFade);

                rgb = MixFog(rgb, input.fogFactor);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
