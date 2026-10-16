// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Waterfall"
{
    Properties
    {
		_LineScaleWidth("Line Scale Width", Float) = 0
		[Header(COLOR)]_ColorShallow("Color Shallow", Color) = (0.5990566,0.9091429,1,0)
		_ColorDeep("Color Deep", Color) = (0.1213065,0.347919,0.5471698,0)
		_Smoothness("Smoothness", Float) = 0
		_LineAcceleration("Line Acceleration", Float) = 1
		_NormalWave("Normal Wave", 2D) = "bump" {}
		_ColorDepthFade("Color Depth Fade", Float) = 0
		_Line01ScaleX("Line 01 Scale X", Float) = 0
		_Line02ScaleX("Line 02 Scale X", Float) = 0
		_Line01Speed("Line 01 Speed", Float) = 0
		_Line02Speed("Line 02 Speed", Float) = 0
		_Line01ScaleY("Line 01 Scale Y", Float) = 0
		_Line02ScaleY("Line 02 Scale Y", Float) = 0
		_LineUVFade("Line UV Fade", Float) = 0
		_LineTreshold("Line Treshold", Range( 0 , 1)) = 0.1
		_Displace1("Displace 1", 2D) = "white" {}
		_WaveNormalIntensity("Wave Normal Intensity", Range( 0 , 1)) = 1
		[IntRange]_WaveTiling01("Wave Tiling 01", Range( 0 , 50)) = 1
		[IntRange]_WaveTiling02("Wave Tiling 02", Range( 0 , 50)) = 1
		_DisplaceStrength("Displace Strength", Range( -1 , 1)) = 0
		_WaterLines("Water Lines", 2D) = "white" {}
		_LineColor("Line Color", Color) = (1,1,1,0)
		_DisplacementMovement("Displacement Movement", Vector) = (0,3,0,0)
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Back
        HLSLINCLUDE
#define _SURFACE_TYPE_TRANSPARENT 1
#define DG_NEEDS_DEPTH 1
#define _RECEIVE_SHADOWS_OFF 1
#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float _LineScaleWidth;
float4 _ColorShallow;
float4 _ColorDeep;
float _Smoothness;
float _LineAcceleration;
float4 _NormalWave_ST;
float _ColorDepthFade;
float _Line01ScaleX;
float _Line02ScaleX;
float _Line01Speed;
float _Line02Speed;
float _Line01ScaleY;
float _Line02ScaleY;
float _LineUVFade;
float _LineTreshold;
float4 _Displace1_ST;
float _WaveNormalIntensity;
float _WaveTiling01;
float _WaveTiling02;
float _DisplaceStrength;
float4 _WaterLines_ST;
float4 _LineColor;
float4 _DisplacementMovement;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_NormalWave); SAMPLER(sampler_NormalWave);
TEXTURE2D(_Displace1); SAMPLER(sampler_Displace1);
TEXTURE2D(_WaterLines); SAMPLER(sampler_WaterLines);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);


float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color)
{
    DGVertexData v;
    v.vertex = float4(p, 1);
    v.normal = n;
    v.tangent = tangent;
    v.texcoord = float4(uv, 0, 0);
    v.color = color;

			
			float2 temp_cast_0 = (_WaveTiling02).xx;
			float2 uv_TexCoord142 = v.texcoord.xy * temp_cast_0;
			float2 panner143 = ( 1.0 * _Time.y * _DisplacementMovement.xy + uv_TexCoord142);
			float4 vDisplacement136 = ( saturate( SAMPLE_TEXTURE2D_LOD(_Displace1, sampler_Displace1, (float4( panner143, 0, 0.0)).xy, (float4( panner143, 0, 0.0)).w) ) * _DisplaceStrength );
			v.vertex.xyz += vDisplacement136.rgb;
			v.vertex.w = 1;
		
    return v.vertex.xyz;
}
void surf(DGInput i, inout DGSurface o)
{
			float2 temp_cast_0 = (_WaveTiling01).xx;
			float2 uv_TexCoord72 = i.uv_texcoord * temp_cast_0;
			float2 panner76 = ( 1.0 * _Time.y * float2( 0,0.75 ) + uv_TexCoord72);
			float3 vNormal91 = DGUnpackNormal( SAMPLE_TEXTURE2D(_NormalWave, sampler_NormalWave, panner76 ), _WaveNormalIntensity );
			o.Normal = vNormal91;
			float4 ase_screenPos = float4( i.screenPos.xyz , i.screenPos.w + 0.00000000001 );
			float4 ase_screenPosNorm = ase_screenPos / ase_screenPos.w;
			ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
			float screenDepth105 = DGSceneEyeDepth(ase_screenPosNorm.xy);
			float distanceDepth105 = abs( ( screenDepth105 - i.eyeDepth ) / DGSafeDivisor(_ColorDepthFade) );
			float4 lerpResult109 = lerp( _ColorShallow , _ColorDeep , saturate( distanceDepth105 ));
			float2 temp_cast_2 = (_WaveTiling02).xx;
			float2 uv_TexCoord142 = i.uv_texcoord * temp_cast_2;
			float2 panner143 = ( 1.0 * _Time.y * _DisplacementMovement.xy + uv_TexCoord142);
			float4 vDisplacement136 = ( saturate( SAMPLE_TEXTURE2D(_Displace1, sampler_Displace1, panner143 ) ) * _DisplaceStrength );
			float4 lerpResult140 = lerp( lerpResult109 , ( lerpResult109 + float4( 0.4414383,0.5903998,0.6037736,1 ) ) , vDisplacement136);
			float4 vDepthColor110 = lerpResult140;
			float2 appendResult26 = (float2(( _LineScaleWidth * i.uv_texcoord.x ) , pow( i.uv_texcoord.y , _LineAcceleration )));
			float2 vMovementLines112 = appendResult26;
			float2 break27 = vMovementLines112;
			float2 appendResult30 = (float2(( _Line01ScaleX * break27.x ) , ( ( break27.y - ( _Time.y * _Line01Speed ) ) * _Line01ScaleY )));
			float4 vLine01113 = SAMPLE_TEXTURE2D(_WaterLines, sampler_WaterLines, appendResult30 );
			float2 break55 = vMovementLines112;
			float2 appendResult61 = (float2(( _Line02ScaleX * break55.x ) , ( ( break55.y - ( _Time.y * _Line02Speed ) ) * _Line02ScaleY )));
			float4 vLine02120 = SAMPLE_TEXTURE2D(_WaterLines, sampler_WaterLines, appendResult61 );
			float4 vLinesCombined122 = floor( saturate( ( saturate( ( ( i.uv_texcoord.y * _LineUVFade ) + ( ( vLine01113 * vLine02120 ) * 2.0 ) ) ) + ( 1.0 - _LineTreshold ) ) ) );
			float4 lerpResult124 = lerp( _LineColor , vDepthColor110 , vLinesCombined122);
			o.Albedo = lerpResult124.rgb;
			o.Smoothness = _Smoothness;
			o.Alpha = 1;
		
}
void DGClip(DGInput i) {}
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
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

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
