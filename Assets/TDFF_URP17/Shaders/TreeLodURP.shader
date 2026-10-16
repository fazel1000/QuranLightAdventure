Shader "TDFF URP17/Tree LOD" {
    Properties {

		_Cutoff( "Mask Clip Value", Float ) = 0.5
		_MainTex1("Base Color", 2D) = "white" {}
		_MainTextureBrightness("Main Texture Brightness", Range( 0 , 2)) = 0.5
		_TopBrightness("Top Brightness", Range( 0 , 10)) = 3
		_Color("Color", Color) = (1,1,1,0)
		[HideInInspector] _texcoord2( "", 2D ) = "white" {}
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #define _SPECULAR_SETUP 1
        
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)

        float4 _MainTex1_ST;
        half4 _Color;
        float _MainTextureBrightness, _TopBrightness, _Cutoff;

        CBUFFER_END

        TEXTURE2D(_MainTex1); SAMPLER(sampler_MainTex1);

        #include "TDFFCommon.hlsl"

        float3 TDDeform(float3 p, float2 uv2) { return p; }
        half TDAlpha(TDVaryings i) {
            half a = SAMPLE_TEXTURE2D(_MainTex1,sampler_MainTex1,i.uv.xy*_MainTex1_ST.xy+_MainTex1_ST.zw).a;
            clip(a-_Cutoff); return a;
        }
        void TDEvaluate(TDVaryings i, inout SurfaceData s) {
            half4 tex = SAMPLE_TEXTURE2D(_MainTex1,sampler_MainTex1,i.uv.xy*_MainTex1_ST.xy+_MainTex1_ST.zw);
            clip(tex.a-_Cutoff);
            half3 c = tex.rgb*_Color.rgb*_MainTextureBrightness;
            half grey = dot(c,half3(0.299,0.587,0.114));
            s.albedo = lerp(c,lerp(c,grey.xxx,0.25h)*_TopBrightness,saturate(pow(max(i.uv.w,0.0),2.93)));
            s.specular = 0; s.smoothness = 0; s.alpha = 1;
        }

        ENDHLSL
        Pass {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TDVertex
            #pragma fragment TDFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TDShadowVertex
            #pragma fragment TDDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TDVertex
            #pragma fragment TDDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            ENDHLSL
        }
        Pass {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TDVertex
            #pragma fragment TDDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback Off
}
