// Test port of the user-supplied Dreamscape Built-in shader. Unity 6 / URP 17.
Shader "Polyart/Dreamscape URP/Waterfall Foam Volumetric"
{
    Properties
    {
		_Cutoff( "Mask Clip Value", Float ) = 0.5
		_ScaleWidth("Scale Width", Float) = 1
		_Acceleration("Acceleration", Float) = 0
		_VerticalSpeed("Vertical Speed", Float) = 1
		_ScaleX("Scale X", Float) = 1
		_ScaleY("Scale Y", Float) = 1
		_TextureSample0("Texture Sample 0", 2D) = "white" {}
		_Threshold("Threshold", Float) = 0.1
		_Height("Height", Float) = 100
		_Color0("Color 0", Color) = (0,0.9495223,1,0)
		_Color1("Color 1", Color) = (1,1,1,0)
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Back
        HLSLINCLUDE

#include "DreamscapeURPTypes.hlsl"
CBUFFER_START(UnityPerMaterial)
float _Cutoff;
float _ScaleWidth;
float _Acceleration;
float _VerticalSpeed;
float _ScaleX;
float _ScaleY;
float4 _TextureSample0_ST;
float _Threshold;
float _Height;
float4 _Color0;
float4 _Color1;
float4 _texcoord_ST;
float __dirty;
CBUFFER_END
TEXTURE2D(_TextureSample0); SAMPLER(sampler_TextureSample0);
TEXTURE2D(_texcoord); SAMPLER(sampler_texcoord);


float3 DGDeform(float3 p, float3 n, float4 tangent, float2 uv, float4 color)
{
    DGVertexData v;
    v.vertex = float4(p, 1);
    v.normal = n;
    v.tangent = tangent;
    v.texcoord = float4(uv, 0, 0);
    v.color = color;

			
			float2 appendResult6 = (float2(( _ScaleWidth * v.texcoord.xy.x ) , ( v.texcoord.xy.y * _Acceleration )));
			float2 break7 = appendResult6;
			float2 appendResult20 = (float2(( _ScaleX * break7.x ) , ( ( break7.y - ( _Time.y * _VerticalSpeed ) ) * _ScaleY )));
			float4 temp_cast_0 = (_Threshold).xxxx;
			float4 temp_output_26_0 = ( ( v.texcoord.xy.y * SAMPLE_TEXTURE2D_LOD(_TextureSample0, sampler_TextureSample0, (float4( appendResult20, 0, 0.0)).xy, (float4( appendResult20, 0, 0.0)).w) ) - temp_cast_0 );
			float4 temp_cast_1 = (( _Height / 4.0 )).xxxx;
			float grayscale34 = DGLuminance(( ( saturate( temp_output_26_0 ) * _Height ) - temp_cast_1 ).rgb);
			float3 appendResult33 = (float3(0.0 , grayscale34 , 0.0));
			v.vertex.xyz += appendResult33;
			v.vertex.w = 1;
		
    return v.vertex.xyz;
}
void surf(DGInput i, inout DGSurface o)
{
			float2 appendResult6 = (float2(( _ScaleWidth * i.uv_texcoord.x ) , ( i.uv_texcoord.y * _Acceleration )));
			float2 break7 = appendResult6;
			float2 appendResult20 = (float2(( _ScaleX * break7.x ) , ( ( break7.y - ( _Time.y * _VerticalSpeed ) ) * _ScaleY )));
			float4 temp_cast_0 = (_Threshold).xxxx;
			float4 temp_output_26_0 = ( ( i.uv_texcoord.y * SAMPLE_TEXTURE2D(_TextureSample0, sampler_TextureSample0, appendResult20 ) ) - temp_cast_0 );
			float4 lerpResult37 = lerp( _Color0 , _Color1 , temp_output_26_0);
			o.Emission = lerpResult37.rgb;
			o.Metallic = temp_output_26_0.r;
			o.Alpha = 1;
			clip( temp_output_26_0.r - _Cutoff );
		
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
