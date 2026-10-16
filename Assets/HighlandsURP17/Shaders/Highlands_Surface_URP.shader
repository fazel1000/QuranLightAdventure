// Test port for Unity 6 / URP 17, derived from the supplied material math.
Shader "Raygeas/Highlands URP/Surface"
{
    Properties
    {
		[Header(Maps)][Space(10)]_Albedo("Albedo", 2D) = "white" {}
		[Normal]_Normal("Normal", 2D) = "bump" {}
		_MetallicSmoothness("Metallic/Smoothness", 2D) = "white" {}
		[HDR]_Emission("Emission", 2D) = "white" {}
		[Header(Coverage Maps)][Space(10)]_CoverageAlbedo("Coverage Albedo", 2D) = "white" {}
		[Normal]_CoverageNormal("Coverage Normal", 2D) = "bump" {}
		_CoverageSmoothnessTexture("Coverage Smoothness", 2D) = "white" {}
		_CoverageMask("Coverage Mask", 2D) = "white" {}
		[Enum(Back,2,Front,1,Double Sided,0)][Header(Settings)][Space(5)]_CullMode("Cull Mode", Float) = 2
		_AlphaCutoff("Alpha Cutoff", Range( 0 , 1)) = 0
		[Space(12)]_Color("Main Color", Color) = (1,1,1,0)
		[HDR]_EmissionColor("Emission", Color) = (0,0,0,1)
		_NormalScale("Normal Scale", Float) = 1
		_Metallic("Metallic", Range( 0 , 1)) = 0
		_SurfaceSmoothness("Smoothness", Range( 0 , 1)) = 0
		[KeywordEnum(Metallic_Alpha,Albedo_Alpha,Metallic_R)] _SmoothnessSource("Smoothness Source", Float) = 0
		[Header(Coverage)][Space(5)][Toggle(_COVERAGEON_ON)] _Coverageon("Enable", Float) = 0
		_CoverageColor("Coverage Color", Color) = (1,1,1,0)
		_CoverageNormalScale("Normal Scale", Float) = 1
		_CoverageSmoothness("Smoothness", Range( 0 , 1)) = 0
		[KeywordEnum(Smoothness_R,Smoothness_Alpha,Albedo_Alpha)] _CoverageSmoothnessSource("Smoothness Source", Float) = 0
		[Header(Coverage Overlay)][Space(5)][KeywordEnum(World_Normal,Vertex_Position)] _OverlayMethod("Overlay Method", Float) = 0
		_CoverageLevel("Level", Float) = 0
		_CoverageFade("Fade", Float) = 1
		_MaskContrast("Mask Contrast", Float) = 0
		[Toggle(_BLENDNORMALS_ON)] _BlendNormals("Blend Normals", Float) = 1
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="AlphaTest" }
        Cull [_CullMode]
        HLSLINCLUDE
#include "HighlandsURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _Albedo_ST;
float4 _Normal_ST;
float4 _MetallicSmoothness_ST;
float4 _Emission_ST;
float4 _CoverageAlbedo_ST;
float4 _CoverageNormal_ST;
float4 _CoverageSmoothnessTexture_ST;
float4 _CoverageMask_ST;
float _CullMode;
float _AlphaCutoff;
float4 _Color;
float4 _EmissionColor;
float _NormalScale;
float _Metallic;
float _SurfaceSmoothness;
float _SmoothnessSource;
float _Coverageon;
float4 _CoverageColor;
float _CoverageNormalScale;
float _CoverageSmoothness;
float _CoverageSmoothnessSource;
float _OverlayMethod;
float _CoverageLevel;
float _CoverageFade;
float _MaskContrast;
float _BlendNormals;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
TEXTURE2D(_MetallicSmoothness); SAMPLER(sampler_MetallicSmoothness);
TEXTURE2D(_Emission); SAMPLER(sampler_Emission);
TEXTURE2D(_CoverageAlbedo); SAMPLER(sampler_CoverageAlbedo);
TEXTURE2D(_CoverageNormal); SAMPLER(sampler_CoverageNormal);
TEXTURE2D(_CoverageSmoothnessTexture); SAMPLER(sampler_CoverageSmoothnessTexture);
TEXTURE2D(_CoverageMask); SAMPLER(sampler_CoverageMask);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);
float4 CalculateContrast(float contrastValue, float4 colorTarget)
{
			float t = 0.5 * ( 1.0 - contrastValue );
			return mul( float4x4( contrastValue,0,0,t, 0,contrastValue,0,t, 0,0,contrastValue,t, 0,0,0,1 ), colorTarget );
		
}

float3 HGDeform(float3 p, float2 uv, half4 color) { return p; }

void surf(HGInput i, inout HGSurface o)
{
			float4 _Normal_ST_Instance = _Normal_ST;
			float2 uv_Normal = i.uv_texcoord * _Normal_ST_Instance.xy + _Normal_ST_Instance.zw;
			float _NormalScale_Instance = _NormalScale;
			float3 tex2DNode6 = HGUnpackNormal( SAMPLE_TEXTURE2D(_Normal, sampler_Normal, uv_Normal ), _NormalScale_Instance );
			float4 _CoverageNormal_ST_Instance = _CoverageNormal_ST;
			float2 uv_CoverageNormal = i.uv_texcoord * _CoverageNormal_ST_Instance.xy + _CoverageNormal_ST_Instance.zw;
			float _CoverageNormalScale_Instance = _CoverageNormalScale;
			float3 tex2DNode305 = HGUnpackNormal( SAMPLE_TEXTURE2D(_CoverageNormal, sampler_CoverageNormal, uv_CoverageNormal ), _CoverageNormalScale_Instance );
			#ifdef _BLENDNORMALS_ON
				float3 staticSwitch358 = HGBlendNormals( tex2DNode6 , tex2DNode305 );
			#else
				float3 staticSwitch358 = tex2DNode305;
			#endif
			float3 ase_vertex3Pos = mul( unity_WorldToObject, float4( i.worldPos , 1 ) );
			#if defined(_OVERLAYMETHOD_WORLD_NORMAL)
				float staticSwitch355 = (HGWorldNormal( i , tex2DNode6 )).y;
			#elif defined(_OVERLAYMETHOD_VERTEX_POSITION)
				float staticSwitch355 = ase_vertex3Pos.y;
			#else
				float staticSwitch355 = (HGWorldNormal( i , tex2DNode6 )).y;
			#endif
			float4 _CoverageMask_ST_Instance = _CoverageMask_ST;
			float2 uv_CoverageMask = i.uv_texcoord * _CoverageMask_ST_Instance.xy + _CoverageMask_ST_Instance.zw;
			float4 CoverageMask297 = saturate( ( ( staticSwitch355 + _CoverageLevel ) * saturate( CalculateContrast(_MaskContrast,SAMPLE_TEXTURE2D(_CoverageMask, sampler_CoverageMask, uv_CoverageMask )) ) * ( _CoverageFade * 5 ) ) );
			float3 lerpResult309 = lerp( tex2DNode6 , staticSwitch358 , CoverageMask297.rgb);
			#ifdef _COVERAGEON_ON
				float3 staticSwitch308 = lerpResult309;
			#else
				float3 staticSwitch308 = tex2DNode6;
			#endif
			float3 Normal75 = staticSwitch308;
			o.Normal = Normal75;
			float4 _Albedo_ST_Instance = _Albedo_ST;
			float2 uv_Albedo = i.uv_texcoord * _Albedo_ST_Instance.xy + _Albedo_ST_Instance.zw;
			float4 tex2DNode2 = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv_Albedo );
			float4 temp_output_3_0 = ( _Color * tex2DNode2 );
			float4 _CoverageAlbedo_ST_Instance = _CoverageAlbedo_ST;
			float2 uv_CoverageAlbedo = i.uv_texcoord * _CoverageAlbedo_ST_Instance.xy + _CoverageAlbedo_ST_Instance.zw;
			float4 tex2DNode299 = SAMPLE_TEXTURE2D(_CoverageAlbedo, sampler_CoverageAlbedo, uv_CoverageAlbedo );
			float4 lerpResult302 = lerp( temp_output_3_0 , ( _CoverageColor * tex2DNode299 ) , CoverageMask297);
			#ifdef _COVERAGEON_ON
				float4 staticSwitch304 = lerpResult302;
			#else
				float4 staticSwitch304 = temp_output_3_0;
			#endif
			float4 Albedo19 = staticSwitch304;
			o.Albedo = Albedo19.rgb;
			float4 _Emission_ST_Instance = _Emission_ST;
			float2 uv_Emission = i.uv_texcoord * _Emission_ST_Instance.xy + _Emission_ST_Instance.zw;
			float4 Emission259 = ( SAMPLE_TEXTURE2D(_Emission, sampler_Emission, uv_Emission ) * _EmissionColor );
			o.Emission = Emission259.rgb;
			float4 _MetallicSmoothness_ST_Instance = _MetallicSmoothness_ST;
			float2 uv_MetallicSmoothness = i.uv_texcoord * _MetallicSmoothness_ST_Instance.xy + _MetallicSmoothness_ST_Instance.zw;
			float4 tex2DNode239 = SAMPLE_TEXTURE2D(_MetallicSmoothness, sampler_MetallicSmoothness, uv_MetallicSmoothness );
			float temp_output_241_0 = ( _Metallic * tex2DNode239.r );
			float lerpResult335 = lerp( temp_output_241_0 , 0.0 , CoverageMask297.r);
			#ifdef _COVERAGEON_ON
				float staticSwitch340 = lerpResult335;
			#else
				float staticSwitch340 = temp_output_241_0;
			#endif
			float Metallic262 = staticSwitch340;
			o.Metallic = Metallic262;
			float AlbedoAlpha391 = tex2DNode2.a;
			#if defined(_SMOOTHNESSSOURCE_METALLIC_ALPHA)
				float staticSwitch266 = tex2DNode239.a;
			#elif defined(_SMOOTHNESSSOURCE_ALBEDO_ALPHA)
				float staticSwitch266 = AlbedoAlpha391;
			#elif defined(_SMOOTHNESSSOURCE_METALLIC_R)
				float staticSwitch266 = tex2DNode239.r;
			#else
				float staticSwitch266 = tex2DNode239.a;
			#endif
			float temp_output_240_0 = ( staticSwitch266 * _SurfaceSmoothness );
			float4 _CoverageSmoothnessTexture_ST_Instance = _CoverageSmoothnessTexture_ST;
			float2 uv_CoverageSmoothnessTexture = i.uv_texcoord * _CoverageSmoothnessTexture_ST_Instance.xy + _CoverageSmoothnessTexture_ST_Instance.zw;
			float4 tex2DNode367 = SAMPLE_TEXTURE2D(_CoverageSmoothnessTexture, sampler_CoverageSmoothnessTexture, uv_CoverageSmoothnessTexture );
			#if defined(_COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_R)
				float staticSwitch371 = tex2DNode367.r;
			#elif defined(_COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_ALPHA)
				float staticSwitch371 = tex2DNode367.a;
			#elif defined(_COVERAGESMOOTHNESSSOURCE_ALBEDO_ALPHA)
				float staticSwitch371 = AlbedoAlpha391;
			#else
				float staticSwitch371 = tex2DNode367.r;
			#endif
			float lerpResult333 = lerp( temp_output_240_0 , ( staticSwitch371 * _CoverageSmoothness ) , CoverageMask297.r);
			#ifdef _COVERAGEON_ON
				float staticSwitch339 = lerpResult333;
			#else
				float staticSwitch339 = temp_output_240_0;
			#endif
			float Smoothness263 = staticSwitch339;
			o.Smoothness = Smoothness263;
			o.Alpha = 1;
			clip( AlbedoAlpha391 - _AlphaCutoff );
		
}
void HGClip(float2 uv)
{
    clip(SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv * _Albedo_ST.xy + _Albedo_ST.zw).a - _AlphaCutoff);
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
            #pragma shader_feature_local _COVERAGEON_ON
            #pragma shader_feature_local _BLENDNORMALS_ON
            #pragma shader_feature_local _OVERLAYMETHOD_WORLD_NORMAL _OVERLAYMETHOD_VERTEX_POSITION
            #pragma shader_feature_local _SMOOTHNESSSOURCE_METALLIC_ALPHA _SMOOTHNESSSOURCE_ALBEDO_ALPHA _SMOOTHNESSSOURCE_METALLIC_R
            #pragma shader_feature_local _COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_R _COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_ALPHA _COVERAGESMOOTHNESSSOURCE_ALBEDO_ALPHA

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
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HGVertex
            #pragma fragment HGDepth
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local _COVERAGEON_ON
            #pragma shader_feature_local _BLENDNORMALS_ON
            #pragma shader_feature_local _OVERLAYMETHOD_WORLD_NORMAL _OVERLAYMETHOD_VERTEX_POSITION
            #pragma shader_feature_local _SMOOTHNESSSOURCE_METALLIC_ALPHA _SMOOTHNESSSOURCE_ALBEDO_ALPHA _SMOOTHNESSSOURCE_METALLIC_R
            #pragma shader_feature_local _COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_R _COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_ALPHA _COVERAGESMOOTHNESSSOURCE_ALBEDO_ALPHA
#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HG_SHADOW_PASS 1
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
            #pragma shader_feature_local _COVERAGEON_ON
            #pragma shader_feature_local _BLENDNORMALS_ON
            #pragma shader_feature_local _OVERLAYMETHOD_WORLD_NORMAL _OVERLAYMETHOD_VERTEX_POSITION
            #pragma shader_feature_local _SMOOTHNESSSOURCE_METALLIC_ALPHA _SMOOTHNESSSOURCE_ALBEDO_ALPHA _SMOOTHNESSSOURCE_METALLIC_R
            #pragma shader_feature_local _COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_R _COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_ALPHA _COVERAGESMOOTHNESSSOURCE_ALBEDO_ALPHA

            
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
            #pragma shader_feature_local _COVERAGEON_ON
            #pragma shader_feature_local _BLENDNORMALS_ON
            #pragma shader_feature_local _OVERLAYMETHOD_WORLD_NORMAL _OVERLAYMETHOD_VERTEX_POSITION
            #pragma shader_feature_local _SMOOTHNESSSOURCE_METALLIC_ALPHA _SMOOTHNESSSOURCE_ALBEDO_ALPHA _SMOOTHNESSSOURCE_METALLIC_R
            #pragma shader_feature_local _COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_R _COVERAGESMOOTHNESSSOURCE_SMOOTHNESS_ALPHA _COVERAGESMOOTHNESSSOURCE_ALBEDO_ALPHA
#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            
            #include "HighlandsURPPasses.hlsl"
            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
