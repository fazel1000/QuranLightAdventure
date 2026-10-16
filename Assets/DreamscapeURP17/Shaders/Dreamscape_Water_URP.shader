// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Water"
{
    Properties
    {
		[Header(COLOR)]_ColorShallow("Color Shallow", Color) = (0.5990566,0.9091429,1,0)
		_ColorDeep("Color Deep", Color) = (0.1213065,0.347919,0.5471698,0)
		_ColorDepthFade("Color Depth Fade", Float) = 0
		_Smoothness("Smoothness", Range( 0 , 1)) = 0
		_Opacity("Opacity", Range( 0 , 1)) = 1
		[Header(FOAM)]_FoamColor("Foam Color", Color) = (1,1,1,1)
		_FoamDepthFade("Foam Depth Fade", Float) = 0.19
		_FoamCutoff("Foam Cutoff", Float) = 0
		_FoamScale("Foam Scale", Float) = 2
		_FoamSpeed("Foam Speed", Float) = 1
		[Header(REFRACTION)]_RefractionStrength("Refraction Strength", Float) = 0
		_RefractionDepth("Refraction Depth", Float) = 0
		[Header(WAVES)]_WaveNormal("Wave Normal", 2D) = "bump" {}
		_WaveNormalIntensity("Wave Normal Intensity", Range( 0 , 1)) = 1
		[Toggle(_WORLDTOOBJECTPOSITION_ON)] _WorldtoObjectPosition("World to Object Position?", Float) = 0
		_Displacement("Displacement", 2D) = "gray" {}
		_DisplaceStrength("Displace Strength", Range( -1 , 1)) = 0
		[IntRange]_WaveTiling01("Wave Tiling 01", Range( 0 , 50)) = 1
		[IntRange]_WaveTiling02("Wave Tiling 02", Range( 0 , 50)) = 1
		[Header(REFLECTION)]_ReflectionTex("ReflectionTex", 2D) = "black" {}
		_ReflectionNormal("Reflection Normal", 2D) = "bump" {}
		_ReflectionsIntensity("ReflectionsIntensity", Range( 0 , 3)) = 1
		_TurbulenceScale("TurbulenceScale", Float) = 1
		_TurbulenceTiling("Turbulence Tiling", Float) = 0
		_TurbulenceDistortionIntensity("Turbulence Distortion Intensity", Range( 0 , 6)) = 0.8
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Off
        HLSLINCLUDE
#define _SURFACE_TYPE_TRANSPARENT 1
#define DG_NEEDS_DEPTH 1
#define DG_NEEDS_SCENE_COLOR 1
#define _RECEIVE_SHADOWS_OFF 1
#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _ColorShallow;
float4 _ColorDeep;
float _ColorDepthFade;
float _Smoothness;
float _Opacity;
float4 _FoamColor;
float _FoamDepthFade;
float _FoamCutoff;
float _FoamScale;
float _FoamSpeed;
float _RefractionStrength;
float _RefractionDepth;
float4 _WaveNormal_ST;
float _WaveNormalIntensity;
float _WorldtoObjectPosition;
float4 _Displacement_ST;
float _DisplaceStrength;
float _WaveTiling01;
float _WaveTiling02;
float4 _ReflectionTex_ST;
float4 _ReflectionNormal_ST;
float _ReflectionsIntensity;
float _TurbulenceScale;
float _TurbulenceTiling;
float _TurbulenceDistortionIntensity;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_WaveNormal); SAMPLER(sampler_WaveNormal);
TEXTURE2D(_Displacement); SAMPLER(sampler_Displacement);
TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
TEXTURE2D(_ReflectionNormal); SAMPLER(sampler_ReflectionNormal);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);

inline float noise_randomValue (float2 uv) { return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453); }
inline float noise_interpolate (float a, float b, float t) { return (1.0-t)*a + (t*b); }
inline float valueNoise (float2 uv)
		{
			float2 i = floor(uv);
			float2 f = frac( uv );
			f = f* f * (3.0 - 2.0 * f);
			uv = abs( frac(uv) - 0.5);
			float2 c0 = i + float2( 0.0, 0.0 );
			float2 c1 = i + float2( 1.0, 0.0 );
			float2 c2 = i + float2( 0.0, 1.0 );
			float2 c3 = i + float2( 1.0, 1.0 );
			float r0 = noise_randomValue( c0 );
			float r1 = noise_randomValue( c1 );
			float r2 = noise_randomValue( c2 );
			float r3 = noise_randomValue( c3 );
			float bottomOfGrid = noise_interpolate( r0, r1, f.x );
			float topOfGrid = noise_interpolate( r2, r3, f.x );
			float t = noise_interpolate( bottomOfGrid, topOfGrid, f.y );
			return t;
		}
float SimpleNoise(float2 UV)
		{
			float t = 0.0;
			float freq = pow( 2.0, float( 0 ) );
			float amp = pow( 0.5, float( 3 - 0 ) );
			t += valueNoise( UV/freq )*amp;
			freq = pow(2.0, float(1));
			amp = pow(0.5, float(3-1));
			t += valueNoise( UV/freq )*amp;
			freq = pow(2.0, float(2));
			amp = pow(0.5, float(3-2));
			t += valueNoise( UV/freq )*amp;
			return t;
		}
float3 PerturbNormal107_g2( float3 surf_pos, float3 surf_norm, float height, float scale )
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
float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color)
{
    DGVertexData v;
    v.vertex = float4(p, 1);
    v.normal = n;
    v.tangent = tangent;
    v.texcoord = float4(uv, 0, 0);
    v.color = color;

			
			float2 temp_cast_0 = (_WaveTiling01).xx;
			float2 uv_TexCoord91 = v.texcoord.xy * temp_cast_0;
			float2 panner93 = ( 1.0 * _Time.y * float2( 0.07,0.07 ) + uv_TexCoord91);
			float2 temp_cast_1 = (_WaveTiling02).xx;
			float2 uv_TexCoord105 = v.texcoord.xy * temp_cast_1;
			float2 panner106 = ( 1.0 * _Time.y * float2( -0.07,-0.07 ) + uv_TexCoord105);
			float vDisplacement123 = ( saturate( ( SAMPLE_TEXTURE2D_LOD(_Displacement, sampler_Displacement, (float4( panner93, 0, 0.0)).xy, (float4( panner93, 0, 0.0)).w).r + SAMPLE_TEXTURE2D_LOD(_Displacement, sampler_Displacement, (float4( panner106, 0, 0.0)).xy, (float4( panner106, 0, 0.0)).w).r ) ) * _DisplaceStrength );
			float3 temp_cast_2 = (vDisplacement123).xxx;
			float3 ase_worldPos = mul( unity_ObjectToWorld, v.vertex );
			float3 worldToObj100 = mul( unity_WorldToObject, float4( ase_worldPos, 1 ) ).xyz;
			float3 appendResult101 = (float3(worldToObj100.x , vDisplacement123 , worldToObj100.z));
			#ifdef _WORLDTOOBJECTPOSITION_ON
				float3 staticSwitch138 = appendResult101;
			#else
				float3 staticSwitch138 = temp_cast_2;
			#endif
			v.vertex.xyz += staticSwitch138;
			v.vertex.w = 1;
			
		
    return v.vertex.xyz;
}
void surf(DGInput i, inout DGSurface o)
{
			float2 temp_cast_0 = (_WaveTiling01).xx;
			float2 uv_TexCoord91 = i.uv_texcoord * temp_cast_0;
			float2 panner93 = ( 1.0 * _Time.y * float2( 0.07,0.07 ) + uv_TexCoord91);
			float2 temp_cast_2 = (_WaveTiling02).xx;
			float2 uv_TexCoord105 = i.uv_texcoord * temp_cast_2;
			float2 panner106 = ( 1.0 * _Time.y * float2( -0.07,-0.07 ) + uv_TexCoord105);
			float3 vNormal90 = DGBlendNormals( DGUnpackNormal( SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, panner93 ), _WaveNormalIntensity ) , DGUnpackNormal( SAMPLE_TEXTURE2D(_WaveNormal, sampler_WaveNormal, panner106 ), _WaveNormalIntensity ) );
			o.Normal = vNormal90;
			float4 ase_screenPos = float4( i.screenPos.xyz , i.screenPos.w + 0.00000000001 );
			float4 ase_screenPosNorm = ase_screenPos / ase_screenPos.w;
			ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
			float2 appendResult78 = (float2(( ase_screenPosNorm.x + 0.01 ) , ( ase_screenPosNorm.y + 0.01 )));
			float2 temp_cast_5 = (_TurbulenceTiling).xx;
			float2 uv_TexCoord66 = i.uv_texcoord * temp_cast_5;
			float2 panner68 = ( 1.0 * _Time.y * float2( 0.1,0.1 ) + ( uv_TexCoord66 * _TurbulenceScale ));
			float temp_output_77_0 = ( DGUnpackNormalDefault( SAMPLE_TEXTURE2D(_ReflectionNormal, sampler_ReflectionNormal, panner68 ) ).g * _TurbulenceDistortionIntensity );
			float Turbulence86 = temp_output_77_0;
			float4 lerpResult80 = lerp( ase_screenPosNorm , float4( appendResult78, 0.0 , 0.0 ) , Turbulence86);
			float4 vReflection132 = ( SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, lerpResult80.xy ) * _ReflectionsIntensity );
			float screenDepth18 = DGSceneEyeDepth(ase_screenPosNorm.xy);
			float distanceDepth18 = abs( ( screenDepth18 - i.eyeDepth ) / DGSafeDivisor(_ColorDepthFade) );
			float4 lerpResult22 = lerp( _ColorShallow , _ColorDeep , saturate( distanceDepth18 ));
			float vDisplacement123 = ( saturate( ( SAMPLE_TEXTURE2D(_Displacement, sampler_Displacement, panner93 ).r + SAMPLE_TEXTURE2D(_Displacement, sampler_Displacement, panner106 ).r ) ) * _DisplaceStrength );
			float4 lerpResult102 = lerp( lerpResult22 , ( lerpResult22 + float4( 0.5,0.5,0.5,0 ) ) , vDisplacement123);
			float4 vColorWaves122 = lerpResult102;
			float4 blendOpSrc85 = vReflection132;
			float4 blendOpDest85 = vColorWaves122;
			float4 temp_output_85_0 = ( saturate( ( blendOpSrc85 + blendOpDest85 ) ));
			float3 ase_worldPos = i.worldPos;
			float3 surf_pos107_g2 = ase_worldPos;
			float3 ase_worldNormal = DGWorldNormal( i, float3( 0, 0, 1 ) );
			float3 surf_norm107_g2 = ase_worldNormal;
			float mulTime7 = _Time.y * 0.1;
			float2 temp_cast_7 = (_FoamSpeed).xx;
			float2 temp_cast_8 = (_FoamScale).xx;
			float2 uv_TexCoord5 = i.uv_texcoord * temp_cast_8;
			float2 panner8 = ( mulTime7 * temp_cast_7 + uv_TexCoord5);
			float simpleNoise13 = SimpleNoise( panner8*50.0 );
			float height107_g2 = simpleNoise13;
			float scale107_g2 = 2.0;
			float3 localPerturbNormal107_g2 = PerturbNormal107_g2( surf_pos107_g2 , surf_norm107_g2 , height107_g2 , scale107_g2 );
			float3 ase_worldTangent = DGWorldNormal( i, float3( 1, 0, 0 ) );
			float3 ase_worldBitangent = DGWorldNormal( i, float3( 0, 1, 0 ) );
			float3x3 ase_worldToTangent = float3x3( ase_worldTangent, ase_worldBitangent, ase_worldNormal );
			float3 worldToTangentDir42_g2 = mul( ase_worldToTangent, localPerturbNormal107_g2);
			float3 vRefractionNormal31 = worldToTangentDir42_g2;
			float eyeDepth28_g3 = DGSceneEyeDepth(ase_screenPosNorm.xy);
			float2 temp_output_20_0_g3 = ( (vRefractionNormal31).xy * ( _RefractionStrength / max( i.eyeDepth , 0.1 ) ) * saturate( ( eyeDepth28_g3 - i.eyeDepth ) ) );
			float eyeDepth2_g3 = DGSceneEyeDepth(( float4( temp_output_20_0_g3, 0.0 , 0.0 ) + ase_screenPosNorm ).xy);
			float2 temp_output_32_0_g3 = (( float4( ( temp_output_20_0_g3 * saturate( ( eyeDepth2_g3 - i.eyeDepth ) ) ), 0.0 , 0.0 ) + ase_screenPosNorm )).xy;
			float2 temp_output_1_0_g3 = ( ( floor( ( temp_output_32_0_g3 * (DGScreenTexelSize()).zw ) ) + 0.5 ) * (DGScreenTexelSize()).xy );
			float4 screenColor36 = float4(SampleSceneColor(saturate(temp_output_1_0_g3)), 1.0);
			float4 vRefraction35 = screenColor36;
			float4 blendOpSrc58 = temp_output_85_0;
			float4 blendOpDest58 = vRefraction35;
			float screenDepth50 = DGSceneEyeDepth(ase_screenPosNorm.xy);
			float distanceDepth50 = abs( ( screenDepth50 - i.eyeDepth ) / DGSafeDivisor(_RefractionDepth) );
			float4 lerpResult32 = lerp( ( saturate( lerp(2.0 * blendOpDest58 * blendOpSrc58, 1.0 - 2.0 * (1.0 - blendOpDest58) * (1.0 - blendOpSrc58), step(0.5, blendOpDest58)) )) , temp_output_85_0 , saturate( distanceDepth50 ));
			float screenDepth4 = DGSceneEyeDepth(ase_screenPosNorm.xy);
			float distanceDepth4 = abs( ( screenDepth4 - i.eyeDepth ) / DGSafeDivisor(_FoamDepthFade) );
			float vFoam15 = step( ( saturate( distanceDepth4 ) * _FoamCutoff ) , simpleNoise13 );
			float4 lerpResult24 = lerp( lerpResult32 , _FoamColor , ( _FoamColor.a * vFoam15 ));
			float4 vFinalColor62 = lerpResult24;
			o.Albedo = vFinalColor62.rgb;
			o.Smoothness = _Smoothness;
			o.Alpha = _Opacity;
		
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
            #pragma shader_feature_local _WORLDTOOBJECTPOSITION_ON

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
