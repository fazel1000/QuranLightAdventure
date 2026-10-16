// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Waterfall Foam"
{
    Properties
    {
		_ScaleWidth("Scale Width", Float) = 1
		_Cutoff( "Mask Clip Value", Float ) = 0.5
		_Acceleration("Acceleration", Float) = 0
		_VerticalSpeed("Vertical Speed", Float) = 1
		_BSpeed("B Speed", Float) = 1
		_HorizontalSpeed("Horizontal Speed", Float) = 0
		_AScaleX("A Scale X", Float) = 1
		_BScaleX("B Scale X", Float) = 2
		_AScaleY("A Scale Y", Float) = 1
		_BScaleY("B Scale Y", Float) = 1
		_TextureSample0("Texture Sample 0", 2D) = "white" {}
		_TextureSample1("Texture Sample 1", 2D) = "white" {}
		_Color0("Color 0", Color) = (0,0.9495223,1,0)
		_Color1("Color 1", Color) = (1,1,1,0)
		_Bias("Bias", Float) = 0.5
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Back
        HLSLINCLUDE

#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float _ScaleWidth;
float _Cutoff;
float _Acceleration;
float _VerticalSpeed;
float _BSpeed;
float _HorizontalSpeed;
float _AScaleX;
float _BScaleX;
float _AScaleY;
float _BScaleY;
float4 _TextureSample0_ST;
float4 _TextureSample1_ST;
float4 _Color0;
float4 _Color1;
float _Bias;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_TextureSample0); SAMPLER(sampler_TextureSample0);
TEXTURE2D(_TextureSample1); SAMPLER(sampler_TextureSample1);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);


float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color) { return p; }
void surf(DGInput i, inout DGSurface o)
{
			float2 appendResult6 = (float2(( _ScaleWidth * i.uv_texcoord.x ) , ( i.uv_texcoord.y * _Acceleration )));
			float2 break7 = appendResult6;
			float2 appendResult20 = (float2(( _AScaleX * ( ( _Time.y * _HorizontalSpeed ) + break7.x ) ) , ( ( break7.y - ( _Time.y * _VerticalSpeed ) ) * _AScaleY )));
			float2 break55 = appendResult6;
			float2 appendResult45 = (float2(( _BScaleX * break55.x ) , ( ( break55.y - ( _Time.y * _BSpeed ) ) * _BScaleY )));
			float4 temp_output_67_0 = ( ( ( SAMPLE_TEXTURE2D(_TextureSample0, sampler_TextureSample0, appendResult20 ) * SAMPLE_TEXTURE2D(_TextureSample1, sampler_TextureSample1, appendResult45 ) ) * ( 1.0 - ( saturate( abs( ( i.uv_texcoord.y - _Bias ) ) ) * 2.0 ) ) ) * 1.0 );
			float4 lerpResult37 = lerp( _Color0 , _Color1 , temp_output_67_0);
			o.Emission = lerpResult37.rgb;
			o.Alpha = 1;
			clip( temp_output_67_0.r - _Cutoff );
		
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
