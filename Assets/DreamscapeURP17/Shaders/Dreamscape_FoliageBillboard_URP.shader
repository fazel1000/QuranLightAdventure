// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Foliage Billboard"
{
    Properties
    {
		_Cutoff( "Mask Clip Value", Float ) = 0.5
		_FoliageTexture("Foliage Texture", 2D) = "white" {}
		_NormalTexture("Normal Texture", 2D) = "bump" {}
		_ColorTop("Color Top", Color) = (1,1,1,0)
		_ColorBottom("Color Bottom", Color) = (1,1,1,0)
		_ColorBottomLevel("Color Bottom Level", Float) = 0
		_ColorBottomMaskFade("Color Bottom Mask Fade", Range( -1 , 0)) = 0
		_Smoothness("Smoothness", Range( 0 , 1)) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
#define DG_FOLIAGE 1
#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float _Cutoff;
float4 _FoliageTexture_ST;
float4 _NormalTexture_ST;
float4 _ColorTop;
float4 _ColorBottom;
float _ColorBottomLevel;
float _ColorBottomMaskFade;
float _Smoothness;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_FoliageTexture); SAMPLER(sampler_FoliageTexture);
TEXTURE2D(_NormalTexture); SAMPLER(sampler_NormalTexture);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);


float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color) { return p; }
void surf(DGInput i, inout DGSurface o)
{
			float2 uv_NormalTexture = i.uv_texcoord * _NormalTexture_ST.xy + _NormalTexture_ST.zw;
			o.Normal = DGUnpackNormalDefault( SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, uv_NormalTexture ) );
			float3 ase_vertex3Pos = mul( unity_WorldToObject, float4( i.worldPos , 1 ) );
			float4 lerpResult11 = lerp( _ColorTop , _ColorBottom , saturate( ( ( ase_vertex3Pos.y + _ColorBottomLevel ) * ( _ColorBottomMaskFade * 2 ) ) ));
			float2 uv_FoliageTexture = i.uv_texcoord * _FoliageTexture_ST.xy + _FoliageTexture_ST.zw;
			float4 tex2DNode12 = SAMPLE_TEXTURE2D(_FoliageTexture, sampler_FoliageTexture, uv_FoliageTexture );
			o.Albedo = ( lerpResult11 * tex2DNode12 ).rgb;
			o.Smoothness = _Smoothness;
			o.Alpha = 1;
			clip( tex2DNode12.a - _Cutoff );
		
}
void DGClip(DGInput i)
{
    DGSurface surface = (DGSurface)0;
    surface.Normal = half3(0,0,1);
    surface.Alpha = 1;
    surface.Occlusion = 1;
    surf(i, surface);
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
