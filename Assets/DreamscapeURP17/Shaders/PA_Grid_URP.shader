// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Utilities/Grid"
{
    Properties
    {
		_GridColor("Grid Color", Color) = (1,1,1,0)
		_GridTexture("Grid Texture", 2D) = "white" {}
		_GridScale("Grid Scale", Vector) = (1,1,0,0)
		_Smoothness("Smoothness", Range( 0 , 1)) = 0
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        HLSLINCLUDE

#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _GridColor;
float4 _GridTexture_ST;
float4 _GridScale;
float _Smoothness;
float __dirty;
CBUFFER_END
TEXTURE2D(_GridTexture); SAMPLER(sampler_GridTexture);

inline float4 TriplanarSampling1( TEXTURE2D_PARAM(topTexMap, samplertopTexMap), TEXTURE2D_PARAM(midTexMap, samplermidTexMap), TEXTURE2D_PARAM(botTexMap, samplerbotTexMap), float3 worldPos, float3 worldNormal, float falloff, float2 tiling, float3 normalScale, float3 index )
		{
			float3 projNormal = ( pow( abs( worldNormal ), falloff ) );
			projNormal /= ( projNormal.x + projNormal.y + projNormal.z ) + 0.00001;
			float3 nsign = sign( worldNormal );
			float negProjNormalY = max( 0, projNormal.y * -nsign.y );
			projNormal.y = max( 0, projNormal.y * nsign.y );
			half4 xNorm; half4 yNorm; half4 yNormN; half4 zNorm;
			xNorm  = SAMPLE_TEXTURE2D(midTexMap, samplermidTexMap, tiling * worldPos.zy * float2(  nsign.x, 1.0 ) );
			yNorm  = SAMPLE_TEXTURE2D(topTexMap, samplertopTexMap, tiling * worldPos.xz * float2(  nsign.y, 1.0 ) );
			yNormN = SAMPLE_TEXTURE2D(botTexMap, samplerbotTexMap, tiling * worldPos.xz * float2(  nsign.y, 1.0 ) );
			zNorm  = SAMPLE_TEXTURE2D(midTexMap, samplermidTexMap, tiling * worldPos.xy * float2( -nsign.z, 1.0 ) );
			return xNorm * projNormal.x + yNorm * projNormal.y + yNormN * negProjNormalY + zNorm * projNormal.z;
		}
float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color) { return p; }
void surf(DGInput i, inout DGSurface o)
{
			o.Normal = float3(0,0,1);
			float3 ase_worldPos = i.worldPos;
			float3 ase_worldNormal = DGWorldNormal( i, float3( 0, 0, 1 ) );
			float4 triplanar1 = TriplanarSampling1( TEXTURE2D_ARGS(_GridTexture, sampler_GridTexture), TEXTURE2D_ARGS(_GridTexture, sampler_GridTexture), TEXTURE2D_ARGS(_GridTexture, sampler_GridTexture), ase_worldPos, ase_worldNormal, 1.0, _GridScale.xy, float3( 1,1,1 ), float3(0,0,0) );
			o.Albedo = ( triplanar1 * _GridColor ).xyz;
			o.Smoothness = _Smoothness;
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
