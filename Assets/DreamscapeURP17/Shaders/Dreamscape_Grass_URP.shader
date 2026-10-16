// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Grass"
{
    Properties
    {
		_Cutoff( "Mask Clip Value", Float ) = 0.59
		_ColorTop("Color Top", Color) = (0,0,0,0)
		_ColorTopVariation("Color Top Variation", Color) = (0,0,0,0)
		_ColorBottom("Color Bottom", Color) = (0,0,0,0)
		_ColorBottomLevel("Color Bottom Level", Float) = 0
		_ColorBottomMaskFade("Color Bottom Mask Fade", Range( -1 , 1)) = 0
		_FoliageTexture("Foliage Texture", 2D) = "white" {}
		_Smoothness("Smoothness", Range( -2 , 2)) = 1
		_ColorWave("Color Wave", Color) = (0,0,0,0)
		_WaveScale("Wave Scale", Float) = 33
		_VariationMapScale("Variation Map Scale", Float) = 15
		_WaveSpeed("Wave Speed", Color) = (0.1320755,0.1320755,0.1320755,0)
		_PanningWaveTexture("Panning Wave Texture", 2D) = "white" {}
		_VariationMask("Variation Mask", 2D) = "white" {}
		_InteractionAlpha("Interaction Alpha", Float) = 1
		_WindNoiseTexture("Wind Noise Texture", 2D) = "white" {}
		_WindNoiseSmallMultiply("Wind Noise Small Multiply", Range( -10 , 10)) = 0
		_WindNoiseLargeMultiply("Wind Noise Large Multiply", Range( -10 , 10)) = 1
		_PivotLockPower("Pivot Lock Power", Range( 0 , 10)) = 2
		_WindNoiseLarge("Wind Noise Large", Float) = 20
		_WindNoiseSmall("Wind Noise Small", Float) = 20
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
float4 _ColorTop;
float4 _ColorTopVariation;
float4 _ColorBottom;
float _ColorBottomLevel;
float _ColorBottomMaskFade;
float4 _FoliageTexture_ST;
float _Smoothness;
float4 _ColorWave;
float _WaveScale;
float _VariationMapScale;
float4 _WaveSpeed;
float4 _PanningWaveTexture_ST;
float4 _VariationMask_ST;
float _InteractionAlpha;
float4 _WindNoiseTexture_ST;
float _WindNoiseSmallMultiply;
float _WindNoiseLargeMultiply;
float _PivotLockPower;
float _WindNoiseLarge;
float _WindNoiseSmall;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_FoliageTexture); SAMPLER(sampler_FoliageTexture);
TEXTURE2D(_PanningWaveTexture); SAMPLER(sampler_PanningWaveTexture);
TEXTURE2D(_VariationMask); SAMPLER(sampler_VariationMask);
TEXTURE2D(_WindNoiseTexture); SAMPLER(sampler_WindNoiseTexture);
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

			
			float4 color128 = DGIsGammaSpace() ? float4(0,0,0,0) : float4(0,0,0,0);
			float3 ase_worldPos = mul( unity_ObjectToWorld, v.vertex );
			float4 lerpResult107 = lerp( ( SAMPLE_TEXTURE2D_LOD(_WindNoiseTexture, sampler_WindNoiseTexture, (float4( ( ( float2( 0,0.2 ) * _Time.y ) + ( (ase_worldPos).xz / DGSafeDivisor(_WindNoiseSmall) ) ), 0, 0.0)).xy, (float4( ( ( float2( 0,0.2 ) * _Time.y ) + ( (ase_worldPos).xz / DGSafeDivisor(_WindNoiseSmall) ) ), 0, 0.0)).w) * _WindNoiseSmallMultiply ) , ( SAMPLE_TEXTURE2D_LOD(_WindNoiseTexture, sampler_WindNoiseTexture, (float4( ( ( float2( 0,0.1 ) * _Time.y ) + ( (ase_worldPos).xz / DGSafeDivisor(_WindNoiseLarge) ) ), 0, 0.0)).xy, (float4( ( ( float2( 0,0.1 ) * _Time.y ) + ( (ase_worldPos).xz / DGSafeDivisor(_WindNoiseLarge) ) ), 0, 0.0)).w) * _WindNoiseLargeMultiply ) , 0.5);
			Gradient gradient111 = NewGradient( 0, 2, 2, float4( 0, 0, 0, 0 ), float4( 1, 1, 1, 1 ), 0, 0, 0, 0, 0, 0, float2( 1, 0 ), float2( 1, 1 ), 0, 0, 0, 0, 0, 0 );
			float4 temp_cast_0 = (_PivotLockPower).xxxx;
			float4 lerpResult108 = lerp( color128 , lerpResult107 , pow( SampleGradient( gradient111, v.texcoord.xy.y ) , temp_cast_0 ));
			float4 vWind116 = lerpResult108;
			Gradient gradient74 = NewGradient( 0, 2, 2, float4( 0, 0, 0, 0 ), float4( 1, 1, 1, 1 ), 0, 0, 0, 0, 0, 0, float2( 1, 0 ), float2( 1, 1 ), 0, 0, 0, 0, 0, 0 );
			float4 normalizeResult69 = DGSafeNormalize( ( _ActorPosition - float4( ase_worldPos , 0.0 ) ) );
			float temp_output_64_0 = ( _InteractionStrength * 0.1 );
			float3 appendResult67 = (float3(temp_output_64_0 , 0.0 , temp_output_64_0));
			float clampResult68 = clamp( ( distance( _ActorPosition , float4( ase_worldPos , 0.0 ) ) / DGSafeDivisor(_InteractionRadius) ) , 0.0 , 1.0 );
			float4 vInteraction81 = ( SampleGradient( gradient74, v.texcoord.xy.y ) * -( ( ( normalizeResult69 * float4( appendResult67 , 0.0 ) ) * ( 1.0 - clampResult68 ) ) * _InteractionAlpha ) );
			v.vertex.xyz += ( vWind116 + vInteraction81 ).rgb;
			v.vertex.w = 1;
		
    return v.vertex.xyz;
}
void surf(DGInput i, inout DGSurface o)
{
			float2 uv_FoliageTexture = i.uv_texcoord * _FoliageTexture_ST.xy + _FoliageTexture_ST.zw;
			float4 tex2DNode22 = SAMPLE_TEXTURE2D(_FoliageTexture, sampler_FoliageTexture, uv_FoliageTexture );
			float4 vWaveColor28 = ( tex2DNode22 * _ColorWave );
			float3 ase_worldPos = i.worldPos;
			float4 lerpResult132 = lerp( _ColorTop , _ColorTopVariation , SAMPLE_TEXTURE2D(_VariationMask, sampler_VariationMask, (( ase_worldPos / DGSafeDivisor(_VariationMapScale) )).xz ));
			float3 ase_vertex3Pos = mul( unity_WorldToObject, float4( i.worldPos , 1 ) );
			float4 lerpResult23 = lerp( lerpResult132 , _ColorBottom , saturate( ( ( ase_vertex3Pos.y + _ColorBottomLevel ) * ( _ColorBottomMaskFade * 2 ) ) ));
			float4 vColor27 = ( lerpResult23 * tex2DNode22 );
			float2 panner5 = ( _Time.y * (_WaveSpeed).rg + (( ase_worldPos / DGSafeDivisor(_WaveScale) )).xz);
			float4 lerpResult9 = lerp( vWaveColor28 , vColor27 , SAMPLE_TEXTURE2D(_PanningWaveTexture, sampler_PanningWaveTexture, panner5 ));
			o.Albedo = lerpResult9.rgb;
			o.Smoothness = _Smoothness;
			o.Alpha = 1;
			float vAlpha125 = tex2DNode22.a;
			clip( vAlpha125 - _Cutoff );
		
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
