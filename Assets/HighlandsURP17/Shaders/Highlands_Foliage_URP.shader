// Test port for Unity 6 / URP 17, derived from the supplied material math.
Shader "Raygeas/Highlands URP/Foliage"
{
    Properties
    {
		[Header(Maps)][Space(5)]_Albedo("Albedo", 2D) = "white" {}
		[Normal]_Normal("Normal", 2D) = "bump" {}
		_SmoothnessTexture("Smoothness", 2D) = "white" {}
		[Enum(Back,2,Front,1,Double Sided,0)][Header(Settings)][Space(5)]_CullMode("Cull Mode", Float) = 0
		_AlphaCutoff("Alpha Cutoff", Range( 0 , 1)) = 0.35
		[Space(12)]_MainColor("Main Color", Color) = (1,1,1,0)
		_NormalScale("Normal Scale", Float) = 1
		_Smoothness("Smoothness", Range( 0 , 1)) = 0
		[KeywordEnum(Smoothness_R,Smoothness_Alpha,Albedo_Alpha)] _SmoothnessSource("Smoothness Source", Float) = 0
		[Header(Second Color)][Space(5)][Toggle(_COLOR2ENABLE_ON)] _Color2Enable("Enable", Float) = 0
		_SecondColor("Second Color", Color) = (0,0,0,0)
		[KeywordEnum(World_Position,Vertex_Position,UV)] _SecondColorOverlayType("Overlay Method", Float) = 1
		_SecondColorOffset("Offset", Float) = 0.1
		_SecondColorFade("Fade", Range( -1 , 1)) = 0.5
		_WorldScale("World Scale", Float) = 1
		[Header(Wind)][Space(5)][Toggle(_ENABLEWIND_ON)] _EnableWind("Enable", Float) = 1
		_WindForce("Force", Range( 0 , 1)) = 0.3
		_WindWavesScale("Waves Scale", Range( 0 , 1)) = 0.25
		_WindSpeed("Speed", Range( 0 , 1)) = 0.5
		[Toggle(_LOCKBYVCOORDINATES_ON)] _LockbyVcoordinates("Lock by V coordinates", Float) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull [_CullMode]
        HLSLINCLUDE
#define HG_FOLIAGE 1
#include "HighlandsURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _Albedo_ST;
float4 _Normal_ST;
float4 _SmoothnessTexture_ST;
float _CullMode;
float _AlphaCutoff;
float4 _MainColor;
float _NormalScale;
float _Smoothness;
float _SmoothnessSource;
float _Color2Enable;
float4 _SecondColor;
float _SecondColorOverlayType;
float _SecondColorOffset;
float _SecondColorFade;
float _WorldScale;
float _EnableWind;
float _WindForce;
float _WindWavesScale;
float _WindSpeed;
float _LockbyVcoordinates;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
TEXTURE2D(_SmoothnessTexture); SAMPLER(sampler_SmoothnessTexture);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);
float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }

		float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }

		float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }

		float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }

		float snoise( float3 v )
		{
			const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
			float3 i = floor( v + dot( v, C.yyy ) );
			float3 x0 = v - i + dot( i, C.xxx );
			float3 g = step( x0.yzx, x0.xyz );
			float3 l = 1.0 - g;
			float3 i1 = min( g.xyz, l.zxy );
			float3 i2 = max( g.xyz, l.zxy );
			float3 x1 = x0 - i1 + C.xxx;
			float3 x2 = x0 - i2 + C.yyy;
			float3 x3 = x0 - 0.5;
			i = mod3D289( i);
			float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
			float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
			float4 x_ = floor( j / 7.0 );
			float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
			float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
			float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
			float4 h = 1.0 - abs( x ) - abs( y );
			float4 b0 = float4( x.xy, y.xy );
			float4 b1 = float4( x.zw, y.zw );
			float4 s0 = floor( b0 ) * 2.0 + 1.0;
			float4 s1 = floor( b1 ) * 2.0 + 1.0;
			float4 sh = -step( h, 0.0 );
			float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
			float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
			float3 g0 = float3( a0.xy, h.x );
			float3 g1 = float3( a0.zw, h.y );
			float3 g2 = float3( a1.xy, h.z );
			float3 g3 = float3( a1.zw, h.w );
			float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
			g0 *= norm.x;
			g1 *= norm.y;
			g2 *= norm.z;
			g3 *= norm.w;
			float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
			m = m* m;
			m = m* m;
			float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
			return 42.0 * dot( m, px);
		}


		
float RAYGlobalWindSpeed;
float RAYGlobalWavesScale;
float RAYGlobalWindForce;


float3 HGDeform(float3 p, float2 uv, half4 color)
{
    #if defined(_ENABLEWIND_ON)
        float t = _Time.y * ((_WindSpeed * 5) * RAYGlobalWindSpeed);
        float noise = snoise((TransformObjectToWorld(p) + t) * ((1.0 - _WindWavesScale) * RAYGlobalWavesScale));
        float weight = noise * 0.01;
        #if defined(_LOCKBYVCOORDINATES_ON)
            weight *= uv.y * uv.y;
        #endif
        p += (weight * color.r * ((_WindForce * 30) * RAYGlobalWindForce)).xxx;
    #endif
    return p;
}

void surf(HGInput i, inout HGSurface o)
{
			float2 uv_Normal = i.uv_texcoord * _Normal_ST.xy + _Normal_ST.zw;
			float3 Normal888 = HGUnpackNormal( SAMPLE_TEXTURE2D(_Normal, sampler_Normal, uv_Normal ), _NormalScale );
			o.Normal = Normal888;
			float2 uv_Albedo = i.uv_texcoord * _Albedo_ST.xy + _Albedo_ST.zw;
			float4 tex2DNode1 = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv_Albedo );
			float4 temp_output_10_0 = ( _MainColor * tex2DNode1 );
			float3 ase_vertex3Pos = mul( unity_WorldToObject, float4( i.worldPos , 1 ) );
			float3 ase_worldPos = i.worldPos;
			float simplePerlin3D742 = snoise( ase_worldPos*_WorldScale );
			simplePerlin3D742 = simplePerlin3D742*0.5 + 0.5;
			#if defined(_SECONDCOLOROVERLAYTYPE_WORLD_POSITION)
				float staticSwitch360 = simplePerlin3D742;
			#elif defined(_SECONDCOLOROVERLAYTYPE_VERTEX_POSITION)
				float staticSwitch360 = ase_vertex3Pos.y;
			#elif defined(_SECONDCOLOROVERLAYTYPE_UV)
				float staticSwitch360 = i.uv_texcoord.y;
			#else
				float staticSwitch360 = ase_vertex3Pos.y;
			#endif
			float SecondColorMask335 = saturate( ( ( staticSwitch360 + _SecondColorOffset ) * ( _SecondColorFade * 3 ) ) );
			float4 lerpResult332 = lerp( temp_output_10_0 , ( _SecondColor * SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv_Albedo ) ) , SecondColorMask335);
			#ifdef _COLOR2ENABLE_ON
				float4 staticSwitch340 = lerpResult332;
			#else
				float4 staticSwitch340 = temp_output_10_0;
			#endif
			float4 Albedo259 = staticSwitch340;
			o.Albedo = Albedo259.rgb;
			float2 uv_SmoothnessTexture = i.uv_texcoord * _SmoothnessTexture_ST.xy + _SmoothnessTexture_ST.zw;
			float4 tex2DNode708 = SAMPLE_TEXTURE2D(_SmoothnessTexture, sampler_SmoothnessTexture, uv_SmoothnessTexture );
			float AlbedoAlpha263 = tex2DNode1.a;
			#if defined(_SMOOTHNESSSOURCE_SMOOTHNESS_R)
				float staticSwitch894 = tex2DNode708.r;
			#elif defined(_SMOOTHNESSSOURCE_SMOOTHNESS_ALPHA)
				float staticSwitch894 = tex2DNode708.a;
			#elif defined(_SMOOTHNESSSOURCE_ALBEDO_ALPHA)
				float staticSwitch894 = AlbedoAlpha263;
			#else
				float staticSwitch894 = tex2DNode708.r;
			#endif
			float Smoothness734 = saturate( ( staticSwitch894 * _Smoothness ) );
			o.Smoothness = Smoothness734;
			o.Alpha = 1;
			clip( AlbedoAlpha263 - _AlphaCutoff );
		
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
            #pragma shader_feature_local _ENABLEWIND_ON
            #pragma shader_feature_local _LOCKBYVCOORDINATES_ON
            #pragma shader_feature_local _COLOR2ENABLE_ON
            #pragma shader_feature_local _SECONDCOLOROVERLAYTYPE_WORLD_POSITION _SECONDCOLOROVERLAYTYPE_VERTEX_POSITION _SECONDCOLOROVERLAYTYPE_UV
            #pragma shader_feature_local _SMOOTHNESSSOURCE_SMOOTHNESS_R _SMOOTHNESSSOURCE_SMOOTHNESS_ALPHA _SMOOTHNESSSOURCE_ALBEDO_ALPHA

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
            #pragma shader_feature_local _ENABLEWIND_ON
            #pragma shader_feature_local _LOCKBYVCOORDINATES_ON
            #pragma shader_feature_local _COLOR2ENABLE_ON
            #pragma shader_feature_local _SECONDCOLOROVERLAYTYPE_WORLD_POSITION _SECONDCOLOROVERLAYTYPE_VERTEX_POSITION _SECONDCOLOROVERLAYTYPE_UV
            #pragma shader_feature_local _SMOOTHNESSSOURCE_SMOOTHNESS_R _SMOOTHNESSSOURCE_SMOOTHNESS_ALPHA _SMOOTHNESSSOURCE_ALBEDO_ALPHA
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
            #pragma shader_feature_local _ENABLEWIND_ON
            #pragma shader_feature_local _LOCKBYVCOORDINATES_ON
            #pragma shader_feature_local _COLOR2ENABLE_ON
            #pragma shader_feature_local _SECONDCOLOROVERLAYTYPE_WORLD_POSITION _SECONDCOLOROVERLAYTYPE_VERTEX_POSITION _SECONDCOLOROVERLAYTYPE_UV
            #pragma shader_feature_local _SMOOTHNESSSOURCE_SMOOTHNESS_R _SMOOTHNESSSOURCE_SMOOTHNESS_ALPHA _SMOOTHNESSSOURCE_ALBEDO_ALPHA

            
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
            #pragma shader_feature_local _ENABLEWIND_ON
            #pragma shader_feature_local _LOCKBYVCOORDINATES_ON
            #pragma shader_feature_local _COLOR2ENABLE_ON
            #pragma shader_feature_local _SECONDCOLOROVERLAYTYPE_WORLD_POSITION _SECONDCOLOROVERLAYTYPE_VERTEX_POSITION _SECONDCOLOROVERLAYTYPE_UV
            #pragma shader_feature_local _SMOOTHNESSSOURCE_SMOOTHNESS_R _SMOOTHNESSSOURCE_SMOOTHNESS_ALPHA _SMOOTHNESSSOURCE_ALBEDO_ALPHA
#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            
            #include "HighlandsURPPasses.hlsl"
            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
