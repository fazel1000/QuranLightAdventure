Shader "TDFF URP17/Vegetation Compatibility" {
    Properties {

        [MainTexture] _MainTex("Albedo (RGBA cutout)",2D)="white" {}
        [MainColor] _Color("Tint (RGB only)",Color)=(1,1,1,1)
        [Normal] _Normal("Normal",2D)="bump" {}
        _NormalIntensity("Normal intensity",Range(0,3))=1
        _MaskMap("Optional mask R metallic / A smoothness",2D)="white" {}
        [ToggleUI] _TDUseMask("Use mask R/A (verify packing first)",Float)=0
        _Smoothness("Smoothness",Range(0,1))=0.3
        _Metallic("Metallic",Range(0,1))=0
        _Cutoff("Alpha cutoff",Range(0,1))=0.5
        [ToggleUI] _TDAlphaClip("Alpha clipping",Float)=1
        [Enum(UnityEngine.Rendering.CullMode)] _TDCull("Cull",Float)=0
        _MainTextureBrightness("Base brightness",Range(0,3))=1
        _TopBrightness("Top brightness",Range(0,10))=1
        [ToggleUI] _UseGradient("Approximate top gradient",Float)=0
        _TopColor("Top tint (RGB only)",Color)=(1,1,1,1)
        _GradientPosition("Gradient power",Range(0.1,10))=2.93
        _ColorVariationMask("Color variation mask",2D)="white" {}
        [ToggleUI] _UseColorVariance("Approximate color variance",Float)=0
        _ColorVarianceAmount("Variance amount",Range(0,1))=0
        [ToggleUI] _TDWind("Approximate wind (optional)",Float)=0
        _WindStrength("Wind strength",Range(0,3))=0.5
        _WindMultiplier("Wind multiplier",Range(0,5))=1
        _WindNoiseScale("Wind scale",Float)=1
        _WindAxisXZ("Wind direction X/Z",Vector)=(0,0,1,0)
        _TDWindUV2("Use UV2.y bending mask",Range(0,1))=1

    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull [_TDCull]
        HLSLINCLUDE
        
        
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)

        float4 _MainTex_ST, _Normal_ST, _MaskMap_ST, _ColorVariationMask_ST;
        half4 _Color, _TopColor;
        float4 _WindAxisXZ;
        float _NormalIntensity, _TDUseMask, _Smoothness, _Metallic, _Cutoff, _TDAlphaClip, _TDCull;
        float _MainTextureBrightness, _TopBrightness, _UseGradient, _GradientPosition;
        float _UseColorVariance, _ColorVarianceAmount, _TDWind, _WindStrength, _WindMultiplier, _WindNoiseScale, _TDWindUV2;

        CBUFFER_END

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
        TEXTURE2D(_ColorVariationMask); SAMPLER(sampler_ColorVariationMask);

        #include "TDFFCommon.hlsl"

        float3 TDDeform(float3 p, float2 uv2) {
            if (_TDWind < .5) return p;
            float2 axis = _WindAxisXZ.xz;
            axis = dot(axis,axis) < .00001 ? float2(0,1) : normalize(axis);
            float3 world = TransformObjectToWorld(p);
            float phase = dot(world.xz,float2(.31,.47))*_WindNoiseScale+_Time.y*1.3;
            float mask = lerp(saturate(p.y*.1),saturate(uv2.y),_TDWindUV2);
            world.xz += axis*sin(phase)*_WindStrength*_WindMultiplier*mask*.12;
            return TransformWorldToObject(world);
        }
        half TDAlpha(TDVaryings i) {
            half a = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv.xy*_MainTex_ST.xy+_MainTex_ST.zw).a;
            if (_TDAlphaClip>.5) clip(a-_Cutoff);
            return a;
        }
        void TDEvaluate(TDVaryings i, inout SurfaceData s) {
            half4 tex = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv.xy*_MainTex_ST.xy+_MainTex_ST.zw);
            if (_TDAlphaClip>.5) clip(tex.a-_Cutoff);
            half3 c = tex.rgb*_Color.rgb*_MainTextureBrightness;
            if (_UseGradient>.5) {
                half weight = saturate(pow(max(i.uv.w,0.0),max(_GradientPosition,.1)));
                c = lerp(c,tex.rgb*_TopColor.rgb*_TopBrightness,weight);
            }
            if (_UseColorVariance>.5) {
                half v = SAMPLE_TEXTURE2D(_ColorVariationMask,sampler_ColorVariationMask,i.uv.xy*_ColorVariationMask_ST.xy+_ColorVariationMask_ST.zw).r;
                c *= lerp(1.0h,.7h+v*.6h,saturate(_ColorVarianceAmount));
            }
            s.albedo=c;
            s.normalTS=UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal,sampler_Normal,i.uv.xy*_Normal_ST.xy+_Normal_ST.zw),_NormalIntensity);
            half4 mask = half4(1,1,1,1);
            if (_TDUseMask>.5) mask=SAMPLE_TEXTURE2D(_MaskMap,sampler_MaskMap,i.uv.xy*_MaskMap_ST.xy+_MaskMap_ST.zw);
            s.metallic=lerp(_Metallic,mask.r,saturate(_TDUseMask));
            s.smoothness=_Smoothness*mask.a;
            s.alpha=1;
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
