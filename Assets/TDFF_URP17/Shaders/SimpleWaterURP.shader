Shader "TDFF URP17/Simple Water" {
    Properties {

		[Normal]_NormalMap("Normal Map", 2D) = "bump" {}
		_NormalMapScale("Normal Map Scale", Range( 0 , 10)) = 1
		_NormalIntensity("Normal Intensity", Range( 0 , 5)) = 0
		_WaterColorBlend("Water Color Blend", Float) = 0
		_WaterDeepColor("Water Deep Color", Color) = (0.1320755,0.1320755,0.1320755,0)
		_WaterShallowColor("Water Shallow Color", Color) = (0.1320755,0.1320755,0.1320755,0)
		_MainOpacity("Main Opacity", Range( 0 , 1)) = 0.2
		_WaterOpacityBlend("Water Opacity Blend", Float) = 0
		_WaterSmoothness("Water Smoothness", Range( 0 , 1)) = 0.95
		_WaterSpecularity("Water Specularity", Range( 0 , 1)) = 1
		_FoamColor("Foam Color", Color) = (0.9150943,0.9150943,0.9150943,0)
		_FoamAmount("Foam Amount", Range( 0 , 2)) = 0
		_FoamContrast("Foam Contrast", Range( 0 , 5)) = 1.5
		_FoamTexture("Foam Texture", 2D) = "white" {}
		_FoamScale("Foam Scale", Float) = 3
		_RefractionIntensity("Refraction Intensity", Range( 0 , 5)) = 1
		[HideInInspector] __dirty( "", Int ) = 1
		[Header(Forward Rendering Options)]
		[ToggleOff(_SPECULARHIGHLIGHTS_OFF)] _SpecularHighlights("Specular Highlights", Float) = 1.0
		[ToggleOff(_ENVIRONMENTREFLECTIONS_OFF)] _GlossyReflections("Reflections", Float) = 1.0
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Back
        HLSLINCLUDE
        #define _SPECULAR_SETUP 1
        #define _SURFACE_TYPE_TRANSPARENT 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)

        float4 _NormalMap_ST, _FoamTexture_ST;
        half4 _WaterShallowColor, _WaterDeepColor, _FoamColor;
        float _NormalMapScale, _NormalIntensity, _WaterColorBlend, _RefractionIntensity;
        float _MainOpacity, _WaterOpacityBlend, _FoamAmount, _FoamContrast, _FoamScale;
        float _WaterSpecularity, _WaterSmoothness, _SpecularHighlights, _GlossyReflections;

        CBUFFER_END

        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_FoamTexture); SAMPLER(sampler_FoamTexture);

        #include "TDFFCommon.hlsl"

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
        float3 TDDeform(float3 p, float2 uv2) { return p; }
        half TDAlpha(TDVaryings i) { return 1; }
        half3 TDWaterNormal(float2 uv) {
            return UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uv),_NormalIntensity);
        }
        half3 TDFourNormals(float2 uv,float t) {
            return TDWaterNormal(uv+t*float2(.1,.1))
                 + TDWaterNormal(uv+float2(.418,.355)+t*float2(-.1,-.1))
                 + TDWaterNormal(uv+float2(.865,.148)+t*float2(-.1,.1))
                 + TDWaterNormal(uv+float2(.651,.752)+t*float2(.1,-.1));
        }
        float TDSceneEyeDepth(float raw) {
            if (unity_OrthoParams.w > 0.5) {
                #if UNITY_REVERSED_Z
                    raw = 1.0-raw;
                #endif
                return lerp(_ProjectionParams.y,_ProjectionParams.z,raw);
            }
            return LinearEyeDepth(raw,_ZBufferParams);
        }
        void TDEvaluate(TDVaryings i, inout SurfaceData s) {
            float t = _Time.y;
            float scale = max(abs(_NormalMapScale),0.0001);
            half3 a = TDFourNormals(i.positionWS.xz/(3.0*scale)+t*float2(.02,.1),t);
            half3 b = TDFourNormals(i.positionWS.xz/(8.0*scale)+t*float2(.1,.06),t*.6);
            // Same normal blending formula as UnityStandardUtils.BlendNormals.
            s.normalTS = normalize(half3(a.xy+b.xy,a.z*b.z));
            float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
            float surfaceDepth = -TransformWorldToView(i.positionWS).z;
            float gap = abs(TDSceneEyeDepth(SampleSceneDepth(uv))-surfaceDepth);
            half depthColor = saturate(gap/max(abs(_WaterColorBlend),0.0001));
            half opacityFade = saturate(gap/max(abs(_WaterOpacityBlend),0.0001));
            half3 waterColor = lerp(_WaterShallowColor.rgb,_WaterDeepColor.rgb,depthColor);
            half foam = 0;
            if (_FoamAmount > 0.0001) {
                float2 fuv = i.positionWS.xz/max(abs(_FoamScale),0.0001);
                half r = SAMPLE_TEXTURE2D(_FoamTexture,sampler_FoamTexture,fuv+t*float2(.2,0)).r;
                half g = SAMPLE_TEXTURE2D(_FoamTexture,sampler_FoamTexture,fuv+t*float2(.3,0)).g;
                half edge = saturate(1.0-gap/_FoamAmount);
                half p = pow(max(edge,0.00001h),max(_FoamContrast,0.00001));
                foam = saturate(saturate(p)-r-saturate(pow(max(g*.88h,0.0h),.65h)));
            }
            half originalOpacity = lerp(_MainOpacity*opacityFade,1.0h,foam);
            // OpaqueTexture replaces the old Built-in GrabPass. Other transparent objects are not included.
            float clipW = max(abs(TransformWorldToHClip(i.positionWS).w),0.0001);
            half3 refractColor = SampleSceneColor(saturate(uv+s.normalTS.xy*_RefractionIntensity/clipW));
            s.albedo = lerp(waterColor+refractColor*.5h*(1.0h-originalOpacity),_FoamColor.rgb,foam);
            s.specular = (_WaterSpecularity*lerp(1.0h,.45h,foam)).xxx;
            s.smoothness = _WaterSmoothness;
            // Preserve the source shader: MainOpacity affects refraction, not this shoreline alpha.
            s.alpha = saturate(pow(saturate(gap/.11),1.5));
        }

        ENDHLSL
        Pass {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
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
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            ENDHLSL
        }
    }
    Fallback Off
}
