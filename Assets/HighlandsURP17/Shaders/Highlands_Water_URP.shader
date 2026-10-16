// Test port for Unity 6 / URP 17, derived from the supplied material math.
Shader "Raygeas/Highlands URP/Water"
{
    Properties
    {
		[NoScaleOffset][Normal]_WavesNormal("Waves Normal", 2D) = "bump" {}
		[Header(Water)][Space(5)]_WaterColor("Water Color", Color) = (0.2705882,0.4823529,0.5372549,0)
		_Smoothness("Smoothness", Range( 0 , 1)) = 0.96
		_Tiling("Tiling", Float) = 0.15
		_WavesSpeed("Waves Speed", Range( 0.1 , 1)) = 0.3
		_NormalIntensity("Normal Intensity", Range( 0 , 2)) = 1
		_Transparency("Transparency", Range( 0 , 10)) = 0
		_TransparencyFade("Transparency Fade", Range( 0 , 2)) = 0
		_CoastalBlending("Coastal Blending", Range( 0 , 1)) = 1
		_RefractionFactor("Refraction Factor", Range( 0 , 1)) = 0.5
		[Header(Foam)][Space(5)][Toggle(_ENABLEFOAM_ON)] _EnableFoam("Enable", Float) = 1
		_FoamTiling("Tiling", Float) = 50
		_FoamOpacity("Opacity", Range( 0 , 1)) = 0
		_FoamDistance("Distance", Range( 0.01 , 1)) = 0.07
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Back
        HLSLINCLUDE
#define _SURFACE_TYPE_TRANSPARENT 1
#include "HighlandsURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _WavesNormal_ST;
float4 _WaterColor;
float _Smoothness;
float _Tiling;
float _WavesSpeed;
float _NormalIntensity;
float _Transparency;
float _TransparencyFade;
float _CoastalBlending;
float _RefractionFactor;
float _EnableFoam;
float _FoamTiling;
float _FoamOpacity;
float _FoamDistance;
float __dirty;
CBUFFER_END
TEXTURE2D(_WavesNormal); SAMPLER(sampler_WavesNormal);
float2 voronoihash61( float2 p )
		{
			
			p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
			return frac( sin( p ) *43758.5453);
		}


		float voronoi61( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
		{
			float2 n = floor( v );
			float2 f = frac( v );
			float F1 = 8.0;
			float F2 = 8.0; float2 mg = 0;
			for ( int j = -1; j <= 1; j++ )
			{
				for ( int i = -1; i <= 1; i++ )
			 	{
			 		float2 g = float2( i, j );
			 		float2 o = voronoihash61( n + g );
					o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
					float d = 0.707 * sqrt(dot( r, r ));
			 		if( d<F1 ) {
			 			F2 = F1;
			 			F1 = d; mg = g; mr = r; id = o;
			 		} else if( d<F2 ) {
			 			F2 = d;
			
			 		}
			 	}
			}
			return F1;
		}


		
float3 HGDeform(float3 p, float2 uv, half4 color) { return p; }


#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

float HGSceneEyeDepth(float2 uv)
{
    float raw = SampleSceneDepth(uv);
    if (unity_OrthoParams.w > 0.5)
    {
        #if UNITY_REVERSED_Z
            raw = 1.0 - raw;
        #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
    }
    return LinearEyeDepth(raw, _ZBufferParams);
}

void surf(HGInput i, inout HGSurface o)
{
    float time = _Time.y * _WavesSpeed;
    float2 tile = i.worldPos.xz * _Tiling;
    float2 pan = tile + time * 0.1;
    float2 pan2 = 1.0 - ((1.0 - time * 0.1) + tile);
    half3 normal = HGBlendNormals(
        HGUnpackNormal(SAMPLE_TEXTURE2D(_WavesNormal, sampler_WavesNormal, pan), _NormalIntensity),
        HGUnpackNormal(SAMPLE_TEXTURE2D(_WavesNormal, sampler_WavesNormal, pan2), _NormalIntensity));
    o.Normal = normal;
    float2 uv = i.screenUV;
    float2 refractedUV = saturate(uv + normal.xy * (_RefractionFactor * 0.1));
    // Reject refracted foreground samples. Opaque Texture contains opaque objects only.
    float useRefracted = step(i.eyeDepth + 0.0001, HGSceneEyeDepth(refractedUV));
    half3 scene = lerp(SampleSceneColor(uv), SampleSceneColor(refractedUV), useRefracted);
    float gap = max(0.0, HGSceneEyeDepth(uv) - i.eyeDepth);
    float deep = saturate(gap / max(_Transparency, 0.0001));
    float tint = (_TransparencyFade <= 0.0) ? 1.0 : pow(max(deep, 0.000001), _TransparencyFade);
    half3 color = lerp(saturate(scene), _WaterColor.rgb, tint);
    float foam = 0.0;
    #if defined(_ENABLEFOAM_ON)
        if (_FoamOpacity > 0.0)
        {
            float2 coords = tile * _FoamTiling;
            float noise = 0.0, total = 0.0, weight = 0.5;
            float2 id = 0, mr = 0, smoothID = 0;
            [unroll] for (int octave = 0; octave < 3; octave++)
            {
                noise += weight * voronoi61(coords, time * 5, id, mr, 0, smoothID);
                total += weight; coords *= 2; weight *= 0.5;
            }
            noise /= max(total, 0.0001);
            foam = saturate(pow(max(abs(1.0 - noise), 0.0001), -1.5) +
                1.0 - gap / max(_FoamDistance * 0.3, 0.0001)) * _FoamOpacity;
        }
    #endif
    o.Albedo = color + foam;
    o.Smoothness = saturate(_Smoothness - foam);
    o.Alpha = saturate(gap / max(_CoastalBlending * 0.5, 0.0001));
}

void HGClip(float2 uv) {}
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HGVertex
            #pragma fragment HGForward
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _ENABLEFOAM_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog

            
            #include "HighlandsURPPasses.hlsl"
            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
