// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Surface Coverage"
{
    Properties
    {
		[Header(MAIN)]_AlbedoTint("Albedo Tint", Color) = (1,1,1,0)
		_AlbedoTexture("Albedo Texture", 2D) = "white" {}
		_NormalTexture("Normal Texture", 2D) = "bump" {}
		_SmoothnessMultiplier("Smoothness Multiplier", Range( -2 , 2)) = 1
		_SmoothnessTexture("Smoothness Texture", 2D) = "white" {}
		_MetallicMultiplier("Metallic Multiplier", Range( 0 , 2)) = 0
		_MetallicTexture("Metallic Texture", 2D) = "black" {}
		_EmissiveMultiplier("Emissive Multiplier", Range( 0 , 20)) = 0
		_EmissiveTexture("Emissive Texture", 2D) = "black" {}
		[Header(GROUND COVERAGE)][Toggle(_GROUNDCOVERON_ON)] _GroundCoverON("Ground Cover ON", Float) = 0
		[Toggle(_BLENDNORMALSON_ON)] _BlendnormalsON("Blend normals ON", Float) = 1
		_CoverageMask("Coverage Mask", 2D) = "white" {}
		_CoverageContrast("Coverage Contrast", Range( 0.1 , 1)) = 0.25
		_CoverageLevel("Coverage Level", Float) = 0
		_CoverageFade("Coverage Fade", Range( -1 , 1)) = 0.5058824
		_CoverageTint("Coverage Tint", Color) = (1,1,1,0)
		_CoverageTexture("Coverage Texture", 2D) = "white" {}
		_CoverageMetallic("Coverage Metallic", Float) = 0
		_CoverageSmoothnessTexture("Coverage Smoothness Texture", 2D) = "white" {}
		_CoverageSmoothnessMultiplier("Coverage Smoothness Multiplier", Range( -2 , 2)) = 1
		_CoverageNormalTexture("Coverage Normal Texture", 2D) = "bump" {}
		_CoverageThickness("Coverage Thickness", Range( 0 , 1)) = 1
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
float4 _AlbedoTint;
float4 _AlbedoTexture_ST;
float4 _NormalTexture_ST;
float _SmoothnessMultiplier;
float4 _SmoothnessTexture_ST;
float _MetallicMultiplier;
float4 _MetallicTexture_ST;
float _EmissiveMultiplier;
float4 _EmissiveTexture_ST;
float _GroundCoverON;
float _BlendnormalsON;
float4 _CoverageMask_ST;
float _CoverageContrast;
float _CoverageLevel;
float _CoverageFade;
float4 _CoverageTint;
float4 _CoverageTexture_ST;
float _CoverageMetallic;
float4 _CoverageSmoothnessTexture_ST;
float _CoverageSmoothnessMultiplier;
float4 _CoverageNormalTexture_ST;
float _CoverageThickness;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_AlbedoTexture); SAMPLER(sampler_AlbedoTexture);
TEXTURE2D(_NormalTexture); SAMPLER(sampler_NormalTexture);
TEXTURE2D(_SmoothnessTexture); SAMPLER(sampler_SmoothnessTexture);
TEXTURE2D(_MetallicTexture); SAMPLER(sampler_MetallicTexture);
TEXTURE2D(_EmissiveTexture); SAMPLER(sampler_EmissiveTexture);
TEXTURE2D(_CoverageMask); SAMPLER(sampler_CoverageMask);
TEXTURE2D(_CoverageTexture); SAMPLER(sampler_CoverageTexture);
TEXTURE2D(_CoverageSmoothnessTexture); SAMPLER(sampler_CoverageSmoothnessTexture);
TEXTURE2D(_CoverageNormalTexture); SAMPLER(sampler_CoverageNormalTexture);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);

float3 PerturbNormal107_g4( float3 surf_pos, float3 surf_norm, float height, float scale )
		{
			// "Bump Mapping Unparametrized Surfaces on the GPU" by Morten S. Mikkelsen
			float3 vSigmaS = ddx( surf_pos );
			float3 vSigmaT = ddy( surf_pos );
			float3 vN = surf_norm;
			float3 vR1 = cross( vSigmaT , vN );
			float3 vR2 = cross( vN , vSigmaS );
			float fDet = dot( vSigmaS , vR1 );
			float dBs = ddx( height );
			float dBt = ddy( height );
			float3 vSurfGrad = scale * 0.05 * sign( fDet ) * ( dBs * vR1 + dBt * vR2 );
			return DGSafeNormalize ( abs( fDet ) * vN - vSurfGrad );
		}
float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color) { return p; }
void surf(DGInput i, inout DGSurface o)
{
			float2 uv_NormalTexture = i.uv_texcoord * _NormalTexture_ST.xy + _NormalTexture_ST.zw;
			float3 tex2DNode76 = DGUnpackNormalDefault( SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, uv_NormalTexture ) );
			float2 uv_CoverageNormalTexture = i.uv_texcoord * _CoverageNormalTexture_ST.xy + _CoverageNormalTexture_ST.zw;
			float3 tex2DNode79 = DGUnpackNormalDefault( SAMPLE_TEXTURE2D(_CoverageNormalTexture, sampler_CoverageNormalTexture, uv_CoverageNormalTexture ) );
			#ifdef _BLENDNORMALSON_ON
				float3 staticSwitch80 = DGBlendNormals( tex2DNode76 , tex2DNode79 );
			#else
				float3 staticSwitch80 = tex2DNode79;
			#endif
			float3 ase_worldPos = i.worldPos;
			float3 surf_pos107_g4 = ase_worldPos;
			float3 ase_worldNormal = DGWorldNormal( i, float3( 0, 0, 1 ) );
			float3 surf_norm107_g4 = ase_worldNormal;
			float2 uv_CoverageMask = i.uv_texcoord * _CoverageMask_ST.xy + _CoverageMask_ST.zw;
			float saferPower22 = max( ( ( ( ase_worldNormal.y + _CoverageLevel ) * ( _CoverageFade * 5 ) ) + SAMPLE_TEXTURE2D(_CoverageMask, sampler_CoverageMask, uv_CoverageMask ).r ) , 0.0001 );
			float vCoverage27 = saturate( pow( saferPower22 , ( _CoverageContrast * 15 ) ) );
			float height107_g4 = ( vCoverage27 * ( _CoverageThickness * 8 ) );
			float scale107_g4 = 1.0;
			float3 localPerturbNormal107_g4 = PerturbNormal107_g4( surf_pos107_g4 , surf_norm107_g4 , height107_g4 , scale107_g4 );
			float3 ase_worldTangent = DGWorldNormal( i, float3( 1, 0, 0 ) );
			float3 ase_worldBitangent = DGWorldNormal( i, float3( 0, 1, 0 ) );
			float3x3 ase_worldToTangent = float3x3( ase_worldTangent, ase_worldBitangent, ase_worldNormal );
			float3 worldToTangentDir42_g4 = mul( ase_worldToTangent, localPerturbNormal107_g4);
			float3 lerpResult83 = lerp( tex2DNode76 , DGBlendNormals( staticSwitch80 , worldToTangentDir42_g4 ) , vCoverage27);
			#ifdef _GROUNDCOVERON_ON
				float3 staticSwitch81 = lerpResult83;
			#else
				float3 staticSwitch81 = tex2DNode76;
			#endif
			float3 vNormal82 = staticSwitch81;
			o.Normal = vNormal82;
			float2 uv_AlbedoTexture = i.uv_texcoord * _AlbedoTexture_ST.xy + _AlbedoTexture_ST.zw;
			float4 temp_output_5_0 = ( _AlbedoTint * SAMPLE_TEXTURE2D(_AlbedoTexture, sampler_AlbedoTexture, uv_AlbedoTexture ) );
			float2 uv_CoverageTexture = i.uv_texcoord * _CoverageTexture_ST.xy + _CoverageTexture_ST.zw;
			float4 lerpResult26 = lerp( temp_output_5_0 , ( _CoverageTint * SAMPLE_TEXTURE2D(_CoverageTexture, sampler_CoverageTexture, uv_CoverageTexture ) ) , vCoverage27);
			#ifdef _GROUNDCOVERON_ON
				float4 staticSwitch1 = lerpResult26;
			#else
				float4 staticSwitch1 = temp_output_5_0;
			#endif
			o.Albedo = staticSwitch1.rgb;
			float2 uv_EmissiveTexture = i.uv_texcoord * _EmissiveTexture_ST.xy + _EmissiveTexture_ST.zw;
			float4 temp_output_91_0 = ( SAMPLE_TEXTURE2D(_EmissiveTexture, sampler_EmissiveTexture, uv_EmissiveTexture ) * _EmissiveMultiplier );
			float4 temp_cast_1 = (0.0).xxxx;
			float4 lerpResult93 = lerp( temp_output_91_0 , temp_cast_1 , vCoverage27);
			#ifdef _GROUNDCOVERON_ON
				float4 staticSwitch94 = lerpResult93;
			#else
				float4 staticSwitch94 = temp_output_91_0;
			#endif
			float4 vEmissive95 = staticSwitch94;
			o.Emission = vEmissive95.rgb;
			float2 uv_MetallicTexture = i.uv_texcoord * _MetallicTexture_ST.xy + _MetallicTexture_ST.zw;
			float4 temp_output_68_0 = ( SAMPLE_TEXTURE2D(_MetallicTexture, sampler_MetallicTexture, uv_MetallicTexture ) * _MetallicMultiplier );
			float4 temp_cast_3 = (_CoverageMetallic).xxxx;
			float4 lerpResult69 = lerp( temp_output_68_0 , temp_cast_3 , vCoverage27);
			#ifdef _GROUNDCOVERON_ON
				float4 staticSwitch70 = lerpResult69;
			#else
				float4 staticSwitch70 = temp_output_68_0;
			#endif
			float4 vMetallic72 = staticSwitch70;
			o.Metallic = vMetallic72.r;
			float2 uv_SmoothnessTexture = i.uv_texcoord * _SmoothnessTexture_ST.xy + _SmoothnessTexture_ST.zw;
			float4 temp_output_48_0 = ( ( 1.0 - SAMPLE_TEXTURE2D(_SmoothnessTexture, sampler_SmoothnessTexture, uv_SmoothnessTexture ) ) * _SmoothnessMultiplier );
			float2 uv_CoverageSmoothnessTexture = i.uv_texcoord * _CoverageSmoothnessTexture_ST.xy + _CoverageSmoothnessTexture_ST.zw;
			float4 lerpResult52 = lerp( temp_output_48_0 , ( ( 1.0 - SAMPLE_TEXTURE2D(_CoverageSmoothnessTexture, sampler_CoverageSmoothnessTexture, uv_CoverageSmoothnessTexture ) ) * _CoverageSmoothnessMultiplier ) , vCoverage27);
			#ifdef _GROUNDCOVERON_ON
				float4 staticSwitch49 = lerpResult52;
			#else
				float4 staticSwitch49 = temp_output_48_0;
			#endif
			float4 vSmoothness51 = staticSwitch49;
			o.Smoothness = vSmoothness51.r;
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
            #pragma shader_feature_local _GROUNDCOVERON_ON
            #pragma shader_feature_local _BLENDNORMALSON_ON

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
            #pragma shader_feature_local _GROUNDCOVERON_ON
            #pragma shader_feature_local _BLENDNORMALSON_ON
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
            #pragma shader_feature_local _GROUNDCOVERON_ON
            #pragma shader_feature_local _BLENDNORMALSON_ON

            
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
            #pragma shader_feature_local _GROUNDCOVERON_ON
            #pragma shader_feature_local _BLENDNORMALSON_ON
#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            
            #include "DreamscapeURPPasses.hlsl"
            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
