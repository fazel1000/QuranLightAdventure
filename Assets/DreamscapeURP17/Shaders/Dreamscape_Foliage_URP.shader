// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Foliage"
{
    Properties
    {
		_AlphaCutoff("Alpha Cutoff", Range( 0 , 1)) = 0.35
		_FoliageTexture("Foliage Texture", 2D) = "white" {}
		_ColorTop("Color Top", Color) = (0,0,0,0)
		_ColorBottom("Color Bottom", Color) = (0,0,0,0)
		_ColorBottomLevel("Color Bottom Level", Float) = 0
		_ColorBottomMaskFade("Color Bottom Mask Fade", Range( -1 , 0)) = 0
		_Smoothness("Smoothness", Range( 0 , 1)) = 0
		[Header(WIND)]_WindSpeed("Wind Speed", Range( 0 , 1)) = 0.5
		_WindScale("Wind Scale", Range( 0 , 2)) = 0.2588236
		_WindIntensity("Wind Intensity", Range( 0 , 50)) = 5
		_InteractionAlpha("Interaction Alpha", Float) = 1
		[Header(Translucency)]
		_Translucency("Strength", Range( 0 , 50)) = 1
		_TransNormalDistortion("Normal Distortion", Range( 0 , 1)) = 0.1
		_TransScattering("Scaterring Falloff", Range( 1 , 50)) = 2
		_TransDirect("Direct", Range( 0 , 1)) = 1
		_TransAmbient("Ambient", Range( 0 , 1)) = 0.2
		_TransShadow("Shadow", Range( 0 , 1)) = 0.9
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
#define DG_FOLIAGE 1
#define DG_TRANSLUCENCY 1
#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float _AlphaCutoff;
float4 _FoliageTexture_ST;
float4 _ColorTop;
float4 _ColorBottom;
float _ColorBottomLevel;
float _ColorBottomMaskFade;
float _Smoothness;
float _WindSpeed;
float _WindScale;
float _WindIntensity;
float _InteractionAlpha;
float _Translucency;
float _TransNormalDistortion;
float _TransScattering;
float _TransDirect;
float _TransAmbient;
float _TransShadow;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_FoliageTexture); SAMPLER(sampler_FoliageTexture);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);
float4 _ActorPosition;
float _InteractionStrength;
float _InteractionRadius;
struct Gradient
{
			int type;
			int colorsLength;
			int alphasLength;
			float4 colors[8];
			float2 alphas[8];
		
};
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
Gradient NewGradient(int type, int colorsLength, int alphasLength, 
		float4 colors0, float4 colors1, float4 colors2, float4 colors3, float4 colors4, float4 colors5, float4 colors6, float4 colors7,
		float2 alphas0, float2 alphas1, float2 alphas2, float2 alphas3, float2 alphas4, float2 alphas5, float2 alphas6, float2 alphas7)
		{
			Gradient g;
			g.type = type;
			g.colorsLength = colorsLength;
			g.alphasLength = alphasLength;
			g.colors[ 0 ] = colors0;
			g.colors[ 1 ] = colors1;
			g.colors[ 2 ] = colors2;
			g.colors[ 3 ] = colors3;
			g.colors[ 4 ] = colors4;
			g.colors[ 5 ] = colors5;
			g.colors[ 6 ] = colors6;
			g.colors[ 7 ] = colors7;
			g.alphas[ 0 ] = alphas0;
			g.alphas[ 1 ] = alphas1;
			g.alphas[ 2 ] = alphas2;
			g.alphas[ 3 ] = alphas3;
			g.alphas[ 4 ] = alphas4;
			g.alphas[ 5 ] = alphas5;
			g.alphas[ 6 ] = alphas6;
			g.alphas[ 7 ] = alphas7;
			return g;
		}
float4 SampleGradient( Gradient gradient, float time )
		{
			float3 color = gradient.colors[0].rgb;
			UNITY_UNROLL
			for (int c = 1; c < 8; c++)
			{
			float colorPos = saturate((time - gradient.colors[c-1].w) / ( 0.00001 + (gradient.colors[c].w - gradient.colors[c-1].w)) * step(c, (float)gradient.colorsLength-1));
			color = lerp(color, gradient.colors[c].rgb, lerp(colorPos, step(0.01, colorPos), gradient.type));
			}
			#ifndef UNITY_COLORSPACE_GAMMA
			color = half3(DGGammaToLinearSpaceExact(color.r), DGGammaToLinearSpaceExact(color.g), DGGammaToLinearSpaceExact(color.b));
			#endif
			float alpha = gradient.alphas[0].x;
			UNITY_UNROLL
			for (int a = 1; a < 8; a++)
			{
			float alphaPos = saturate((time - gradient.alphas[a-1].y) / ( 0.00001 + (gradient.alphas[a].y - gradient.alphas[a-1].y)) * step(a, (float)gradient.alphasLength-1));
			alpha = lerp(alpha, gradient.alphas[a].x, lerp(alphaPos, step(0.01, alphaPos), gradient.type));
			}
			return float4(color, alpha);
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
			float vWind54 = ( ( simplePerlin3D47 * 0.015 ) * _WindIntensity );
			Gradient gradient91 = NewGradient( 0, 2, 2, float4( 0, 0, 0, 0 ), float4( 1, 1, 1, 1 ), 0, 0, 0, 0, 0, 0, float2( 1, 0 ), float2( 1, 1 ), 0, 0, 0, 0, 0, 0 );
			float4 normalizeResult76 = DGSafeNormalize( ( _ActorPosition - float4( ase_worldPos , 0.0 ) ) );
			float temp_output_79_0 = ( _InteractionStrength * 0.1 );
			float3 appendResult80 = (float3(temp_output_79_0 , 0.0 , temp_output_79_0));
			float clampResult84 = clamp( ( distance( _ActorPosition , float4( ase_worldPos , 0.0 ) ) / DGSafeDivisor(_InteractionRadius) ) , 0.0 , 1.0 );
			float4 vInteraction97 = ( SampleGradient( gradient91, v.texcoord.xy.y ) * -( ( ( normalizeResult76 * float4( appendResult80 , 0.0 ) ) * ( 1.0 - clampResult84 ) ) * _InteractionAlpha ) );
			v.vertex.xyz += ( vWind54 + vInteraction97 ).rgb;
			v.vertex.w = 1;
		
    return v.vertex.xyz;
}
void surf(DGInput i, inout DGSurface o)
{
			float3 ase_vertex3Pos = mul( unity_WorldToObject, float4( i.worldPos , 1 ) );
			float4 lerpResult7 = lerp( _ColorTop , _ColorBottom , saturate( ( ( ase_vertex3Pos.y + _ColorBottomLevel ) * ( _ColorBottomMaskFade * 2 ) ) ));
			float2 uv_FoliageTexture = i.uv_texcoord * _FoliageTexture_ST.xy + _FoliageTexture_ST.zw;
			float4 tex2DNode10 = SAMPLE_TEXTURE2D(_FoliageTexture, sampler_FoliageTexture, uv_FoliageTexture );
			o.Albedo = ( lerpResult7 * tex2DNode10 ).rgb;
			o.Smoothness = _Smoothness;
			float3 temp_cast_1 = (1.0).xxx;
			o.Translucency = temp_cast_1;
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
