// Test port for Unity 6 / URP 17, derived from the supplied material math.
Shader "Raygeas/Highlands URP/Background Objects"
{
    Properties
    {
		[Header(Maps)][Space(5)]_Albedo("Albedo", 2D) = "white" {}
		[Normal]_Normal("Normal", 2D) = "bump" {}
		[Header(Settings)][Space(5)]_CustomCutoff("Alpha Cutoff", Range( 0 , 1)) = 0
		[HDR]_Color("Color", Color) = (0.8,0.8,0.8,0)
		_NormalScale("Normal Scale", Float) = 1
		[Header(Fog Settings)][Space(5)][Toggle(_FOGON_ON)] _FogOn("Fog On", Float) = 1
		_FogColor("Fog Color", Color) = (1,1,1,0)
		_Fade("Fade", Float) = 1
		_Height("Height", Float) = 1
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="AlphaTest" }
        Cull Back
        HLSLINCLUDE
#define HG_BACKGROUND 1
#include "HighlandsURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _Albedo_ST;
float4 _Normal_ST;
float _CustomCutoff;
float4 _Color;
float _NormalScale;
float _FogOn;
float4 _FogColor;
float _Fade;
float _Height;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);

float3 HGDeform(float3 p, float2 uv, half4 color) { return p; }

void surf(HGInput i, inout HGSurface o)
{
			float2 uv_Normal = i.uv_texcoord * _Normal_ST.xy + _Normal_ST.zw;
			o.Normal = HGUnpackNormal( SAMPLE_TEXTURE2D(_Normal, sampler_Normal, uv_Normal ), _NormalScale );
			float2 uv_Albedo = i.uv_texcoord * _Albedo_ST.xy + _Albedo_ST.zw;
			float4 tex2DNode2 = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv_Albedo );
			o.Albedo = ( tex2DNode2 * _Color ).rgb;
			float3 ase_vertex3Pos = mul( unity_WorldToObject, float4( i.worldPos , 1 ) );
			#ifdef _FOGON_ON
				float4 staticSwitch35 = ( _FogColor * saturate( ( ( ( ase_vertex3Pos.y * 0.1 ) + _Height ) * _Fade ) ) );
			#else
				float4 staticSwitch35 = float4( 0,0,0,0 );
			#endif
			o.Emission = staticSwitch35.rgb;
			o.Alpha = 1;
			clip( tex2DNode2.a - _CustomCutoff );
		
}
void HGClip(float2 uv)
{
    clip(SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv * _Albedo_ST.xy + _Albedo_ST.zw).a - _CustomCutoff);
}
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HGVertex
            #pragma fragment HGForward
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _FOGON_ON

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
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HGVertex
            #pragma fragment HGDepth
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _FOGON_ON

            
            #include "HighlandsURPPasses.hlsl"
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
            #pragma vertex HGVertex
            #pragma fragment HGDepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _FOGON_ON
#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            
            #include "HighlandsURPPasses.hlsl"
            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
