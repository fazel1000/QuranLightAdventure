// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/World Aligned Blend"
{
    Properties
    {
		_StoneTexture("Stone Texture", 2D) = "white" {}
		_DirtTexture("Dirt Texture", 2D) = "white" {}
		_GrassTexture("Grass Texture", 2D) = "white" {}
		_StoneDirtSharpness("Stone Dirt Sharpness", Float) = 0
		_StoneDirtBias("Stone Dirt Bias", Float) = 0
		_GrassSharpness("Grass Sharpness", Float) = 0
		_GrassBias("Grass Bias", Float) = 0
		_NormalTexture("Normal Texture", 2D) = "bump" {}
		_Normal("Normal", Range( 0 , 1)) = 1
		_SmoothnessTexture("Smoothness Texture", 2D) = "white" {}
		_SmoothnessMultiplier("Smoothness Multiplier", Range( -2 , 2)) = 1
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        HLSLINCLUDE

#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _StoneTexture_ST;
float4 _DirtTexture_ST;
float4 _GrassTexture_ST;
float _StoneDirtSharpness;
float _StoneDirtBias;
float _GrassSharpness;
float _GrassBias;
float4 _NormalTexture_ST;
float _Normal;
float4 _SmoothnessTexture_ST;
float _SmoothnessMultiplier;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_StoneTexture); SAMPLER(sampler_StoneTexture);
TEXTURE2D(_DirtTexture); SAMPLER(sampler_DirtTexture);
TEXTURE2D(_GrassTexture); SAMPLER(sampler_GrassTexture);
TEXTURE2D(_NormalTexture); SAMPLER(sampler_NormalTexture);
TEXTURE2D(_SmoothnessTexture); SAMPLER(sampler_SmoothnessTexture);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);


float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color) { return p; }
void surf(DGInput i, inout DGSurface o)
{
			float2 uv_NormalTexture = i.uv_texcoord * _NormalTexture_ST.xy + _NormalTexture_ST.zw;
			o.Normal = DGUnpackNormal( SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, uv_NormalTexture ), _Normal );
			float2 uv_StoneTexture = i.uv_texcoord * _StoneTexture_ST.xy + _StoneTexture_ST.zw;
			float2 uv_DirtTexture = i.uv_texcoord * _DirtTexture_ST.xy + _DirtTexture_ST.zw;
			float3 ase_worldNormal = DGWorldNormal( i, float3( 0, 0, 1 ) );
			float3 ase_normWorldNormal = DGSafeNormalize( ase_worldNormal );
			float3 normalizeResult44_g3 = DGSafeNormalize( float3(0,1,0) );
			float dotResult45_g3 = dot( ase_normWorldNormal , normalizeResult44_g3 );
			float temp_output_67_0_g3 = _StoneDirtSharpness;
			float temp_output_68_0_g3 = _StoneDirtBias;
			float clampResult41_g3 = clamp( ( ( ( ( dotResult45_g3 * 0.5 ) + 0.5 ) * temp_output_67_0_g3 ) + ( temp_output_68_0_g3 - ( temp_output_67_0_g3 * 0.5 ) ) ) , 0.0 , 1.0 );
			float4 lerpResult13 = lerp( SAMPLE_TEXTURE2D(_StoneTexture, sampler_StoneTexture, uv_StoneTexture ) , SAMPLE_TEXTURE2D(_DirtTexture, sampler_DirtTexture, uv_DirtTexture ) , clampResult41_g3);
			float2 uv_GrassTexture = i.uv_texcoord * _GrassTexture_ST.xy + _GrassTexture_ST.zw;
			float3 normalizeResult44_g4 = DGSafeNormalize( float3(0,1,0) );
			float dotResult45_g4 = dot( ase_normWorldNormal , normalizeResult44_g4 );
			float temp_output_67_0_g4 = _GrassSharpness;
			float temp_output_68_0_g4 = _GrassBias;
			float clampResult41_g4 = clamp( ( ( ( ( dotResult45_g4 * 0.5 ) + 0.5 ) * temp_output_67_0_g4 ) + ( temp_output_68_0_g4 - ( temp_output_67_0_g4 * 0.5 ) ) ) , 0.0 , 1.0 );
			float4 lerpResult34 = lerp( lerpResult13 , SAMPLE_TEXTURE2D(_GrassTexture, sampler_GrassTexture, uv_GrassTexture ) , clampResult41_g4);
			o.Albedo = lerpResult34.rgb;
			float2 uv_SmoothnessTexture = i.uv_texcoord * _SmoothnessTexture_ST.xy + _SmoothnessTexture_ST.zw;
			o.Smoothness = ( ( 1.0 - SAMPLE_TEXTURE2D(_SmoothnessTexture, sampler_SmoothnessTexture, uv_SmoothnessTexture ) ) * _SmoothnessMultiplier ).r;
			o.Alpha = 1;
		
}
void DGClip(DGInput i) {}
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DGVertex
            #pragma fragment DGForward
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE


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

            
            #include "DreamscapeURPPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DGVertex
            #pragma fragment DGDepth
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE

#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define DG_SHADOW_PASS 1
            #include "DreamscapeURPPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DGVertex
            #pragma fragment DGDepth
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE


            
            #include "DreamscapeURPPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DGVertex
            #pragma fragment DGDepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE

#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            
            #include "DreamscapeURPPasses.hlsl"
            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
