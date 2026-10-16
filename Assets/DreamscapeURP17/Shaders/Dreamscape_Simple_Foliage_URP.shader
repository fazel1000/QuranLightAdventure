// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Simple Foliage"
{
    Properties
    {
		_AlphaCutoff("Alpha Cutoff", Range( 0 , 1)) = 0.35
		_ColorTint("Color Tint", Color) = (1,1,1,0)
		_FoliageTexture("Foliage Texture", 2D) = "white" {}
		_ColorMask("Color Mask", 2D) = "white" {}
		_Smoothness("Smoothness", Range( -2 , 2)) = 1
		[Header(WIND)]_WindSpeed("Wind Speed", Range( 0 , 1)) = 0.5
		_WindScale("Wind Scale", Range( 0 , 2)) = 0.2588236
		_WindIntensity("Wind Intensity", Range( 0 , 50)) = 5
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
float _AlphaCutoff;
float4 _ColorTint;
float4 _FoliageTexture_ST;
float4 _ColorMask_ST;
float _Smoothness;
float _WindSpeed;
float _WindScale;
float _WindIntensity;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_FoliageTexture); SAMPLER(sampler_FoliageTexture);
TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
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
float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color)
{
    DGVertexData v;
    v.vertex = float4(p, 1);
    v.normal = n;
    v.tangent = tangent;
    v.texcoord = float4(uv, 0, 0);
    v.color = color;

			
			float3 ase_worldPos = mul( unity_ObjectToWorld, v.vertex );
			float mulTime45 = _Time.y * ( _WindSpeed * 3 );
			float simplePerlin3D47 = snoise( ( ase_worldPos + mulTime45 )*_WindScale );
			v.vertex.xyz += ( v.color * ( ( simplePerlin3D47 * 0.015 ) * _WindIntensity ) ).rgb;
			v.vertex.w = 1;
		
    return v.vertex.xyz;
}
void surf(DGInput i, inout DGSurface o)
{
			float2 uv_FoliageTexture = i.uv_texcoord * _FoliageTexture_ST.xy + _FoliageTexture_ST.zw;
			float4 tex2DNode10 = SAMPLE_TEXTURE2D(_FoliageTexture, sampler_FoliageTexture, uv_FoliageTexture );
			float2 uv_ColorMask = i.uv_texcoord * _ColorMask_ST.xy + _ColorMask_ST.zw;
			float4 lerpResult58 = lerp( tex2DNode10 , ( tex2DNode10 * _ColorTint ) , SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv_ColorMask ));
			float4 vColor60 = lerpResult58;
			o.Albedo = vColor60.rgb;
			o.Smoothness = _Smoothness;
			o.Alpha = 1;
			clip( tex2DNode10.a - _AlphaCutoff );
		
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
