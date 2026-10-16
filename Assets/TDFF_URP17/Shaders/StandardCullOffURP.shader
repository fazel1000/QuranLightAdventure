Shader "TDFF URP17/Standard Cull Off" {
    Properties {

		_Cutoff( "Mask Clip Value", Float ) = 0.5
		_BaseColor("Albedo", 2D) = "white" {}
		_Color("Color", Color) = (1,1,1,0)
		_Normal("Normal Map", 2D) = "bump" {}
		_MetallicRSmoothnessA("Metallic (R), Smoothness (A)", 2D) = "black" {}
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        
        
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)

        float4 _BaseColor_ST, _Normal_ST, _MetallicRSmoothnessA_ST;
        half4 _Color;
        float _Cutoff;

        CBUFFER_END

        TEXTURE2D(_BaseColor); SAMPLER(sampler_BaseColor);
        TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
        TEXTURE2D(_MetallicRSmoothnessA); SAMPLER(sampler_MetallicRSmoothnessA);

        #include "TDFFCommon.hlsl"

        float3 TDDeform(float3 p, float2 uv2) { return p; }
        half TDAlpha(TDVaryings i) {
            half a = SAMPLE_TEXTURE2D(_BaseColor,sampler_BaseColor,i.uv.xy*_BaseColor_ST.xy+_BaseColor_ST.zw).a;
            clip(a-_Cutoff); return a;
        }
        void TDEvaluate(TDVaryings i, inout SurfaceData s) {
            half4 tex = SAMPLE_TEXTURE2D(_BaseColor,sampler_BaseColor,i.uv.xy*_BaseColor_ST.xy+_BaseColor_ST.zw);
            clip(tex.a-_Cutoff);
            s.albedo = tex.rgb * _Color.rgb;
            s.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal,sampler_Normal,i.uv.xy*_Normal_ST.xy+_Normal_ST.zw),1);
            half4 mask = SAMPLE_TEXTURE2D(_MetallicRSmoothnessA,sampler_MetallicRSmoothnessA,i.uv.xy*_MetallicRSmoothnessA_ST.xy+_MetallicRSmoothnessA_ST.zw);
            s.metallic = mask.r; s.smoothness = mask.a; s.alpha = 1;
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
