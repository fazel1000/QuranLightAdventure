// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Surface World Aligned"
{
    Properties
    {
		_AlbedoTexture("Albedo Texture", 2D) = "white" {}
		[Normal]_NormalTexture("Normal Texture", 2D) = "bump" {}
		_RoughnessTexture("Roughness Texture", 2D) = "white" {}
		_Tiling("Tiling", Float) = 1
		_NormalIntensity("Normal Intensity", Range( 0 , 1)) = 1
		_SmoothnessIntensity("Smoothness Intensity", Range( -1 , 1)) = 1
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        HLSLINCLUDE

#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _AlbedoTexture_ST;
float4 _NormalTexture_ST;
float4 _RoughnessTexture_ST;
float _Tiling;
float _NormalIntensity;
float _SmoothnessIntensity;
float __dirty;
CBUFFER_END
TEXTURE2D(_AlbedoTexture); SAMPLER(sampler_AlbedoTexture);
TEXTURE2D(_NormalTexture); SAMPLER(sampler_NormalTexture);
TEXTURE2D(_RoughnessTexture); SAMPLER(sampler_RoughnessTexture);


float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color) { return p; }
void surf(DGInput i, inout DGSurface o)
{
			float3 ase_worldPos = i.worldPos;
			float3 break9_g16 = ( ase_worldPos * ( abs( _Tiling ) * -1.0 ) );
			float2 appendResult12_g16 = (float2(break9_g16.x , break9_g16.z));
			float2 XZ_Coord13_g16 = appendResult12_g16;
			float4 XZ_Texture21_g16 = SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, XZ_Coord13_g16 );
			float2 appendResult14_g16 = (float2(break9_g16.y , break9_g16.z));
			float2 YZ_Coord15_g16 = appendResult14_g16;
			float4 YZ_Texture23_g16 = SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, YZ_Coord15_g16 );
			float4 lerpResult33_g16 = lerp( XZ_Texture21_g16 , YZ_Texture23_g16 , abs( i.worldNormal.x ));
			float2 appendResult10_g16 = (float2(break9_g16.x , break9_g16.y));
			float2 XY_Coord11_g16 = appendResult10_g16;
			float4 XY_Texture19_g16 = SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, XY_Coord11_g16 );
			float4 lerpResult36_g16 = lerp( lerpResult33_g16 , XY_Texture19_g16 , abs( i.worldNormal.z ));
			o.Normal = DGUnpackNormal( lerpResult36_g16, _NormalIntensity );
			float3 break9_g17 = ( ase_worldPos * ( abs( _Tiling ) * -1.0 ) );
			float2 appendResult12_g17 = (float2(break9_g17.x , break9_g17.z));
			float2 XZ_Coord13_g17 = appendResult12_g17;
			float4 XZ_Texture21_g17 = SAMPLE_TEXTURE2D(_AlbedoTexture, sampler_AlbedoTexture, XZ_Coord13_g17 );
			float2 appendResult14_g17 = (float2(break9_g17.y , break9_g17.z));
			float2 YZ_Coord15_g17 = appendResult14_g17;
			float4 YZ_Texture23_g17 = SAMPLE_TEXTURE2D(_AlbedoTexture, sampler_AlbedoTexture, YZ_Coord15_g17 );
			float4 lerpResult33_g17 = lerp( XZ_Texture21_g17 , YZ_Texture23_g17 , abs( i.worldNormal.x ));
			float2 appendResult10_g17 = (float2(break9_g17.x , break9_g17.y));
			float2 XY_Coord11_g17 = appendResult10_g17;
			float4 XY_Texture19_g17 = SAMPLE_TEXTURE2D(_AlbedoTexture, sampler_AlbedoTexture, XY_Coord11_g17 );
			float4 lerpResult36_g17 = lerp( lerpResult33_g17 , XY_Texture19_g17 , abs( i.worldNormal.z ));
			o.Albedo = lerpResult36_g17.rgb;
			float3 break9_g15 = ( ase_worldPos * ( abs( _Tiling ) * -1.0 ) );
			float2 appendResult12_g15 = (float2(break9_g15.x , break9_g15.z));
			float2 XZ_Coord13_g15 = appendResult12_g15;
			float4 XZ_Texture21_g15 = SAMPLE_TEXTURE2D(_RoughnessTexture, sampler_RoughnessTexture, XZ_Coord13_g15 );
			float2 appendResult14_g15 = (float2(break9_g15.y , break9_g15.z));
			float2 YZ_Coord15_g15 = appendResult14_g15;
			float4 YZ_Texture23_g15 = SAMPLE_TEXTURE2D(_RoughnessTexture, sampler_RoughnessTexture, YZ_Coord15_g15 );
			float4 lerpResult33_g15 = lerp( XZ_Texture21_g15 , YZ_Texture23_g15 , abs( i.worldNormal.x ));
			float2 appendResult10_g15 = (float2(break9_g15.x , break9_g15.y));
			float2 XY_Coord11_g15 = appendResult10_g15;
			float4 XY_Texture19_g15 = SAMPLE_TEXTURE2D(_RoughnessTexture, sampler_RoughnessTexture, XY_Coord11_g15 );
			float4 lerpResult36_g15 = lerp( lerpResult33_g15 , XY_Texture19_g15 , abs( i.worldNormal.z ));
			o.Smoothness = ( ( 1.0 - lerpResult36_g15 ) * _SmoothnessIntensity ).r;
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
