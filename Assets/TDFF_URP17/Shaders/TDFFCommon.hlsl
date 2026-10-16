// URP 17. Unity 6. Shared lit, depth, normal and shadow passes.
#ifndef TDFF_COMMON_INCLUDED
#define TDFF_COMMON_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
struct TDAttributes {
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 uv2 : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct TDVaryings {
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half4 tangentWS : TEXCOORD2;
    float4 uv : TEXCOORD3;
    DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
    half4 fogAndVertexLight : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
float3 TDDeform(float3 positionOS, float2 uv2);
half TDAlpha(TDVaryings i);
void TDEvaluate(TDVaryings i, inout SurfaceData s);
float3 _LightDirection;
float3 _LightPosition;
TDVaryings TDVertex(TDAttributes a) {
    TDVaryings o = (TDVaryings)0;
    UNITY_SETUP_INSTANCE_ID(a);
    UNITY_TRANSFER_INSTANCE_ID(a, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    a.positionOS.xyz = TDDeform(a.positionOS.xyz, a.uv2);
    VertexPositionInputs p = GetVertexPositionInputs(a.positionOS.xyz);
    // Some billboard meshes have no tangent stream.
    float3 t = a.tangentOS.xyz;
    if (dot(t,t) < 0.001) t = normalize(cross(abs(a.normalOS.y) < 0.99 ? float3(0,1,0) : float3(0,0,1), a.normalOS));
    VertexNormalInputs n = GetVertexNormalInputs(a.normalOS, float4(t, a.tangentOS.w == 0 ? 1 : a.tangentOS.w));
    o.positionCS = p.positionCS;
    o.positionWS = p.positionWS;
    o.normalWS = n.normalWS;
    o.tangentWS = half4(n.tangentWS, (a.tangentOS.w == 0 ? 1 : a.tangentOS.w) * GetOddNegativeScale());
    o.uv = float4(a.uv, a.uv2);
    OUTPUT_LIGHTMAP_UV(a.uv2, unity_LightmapST, o.lightmapUV);
    OUTPUT_SH(n.normalWS, o.vertexSH);
    o.fogAndVertexLight = half4(ComputeFogFactor(p.positionCS.z), VertexLighting(p.positionWS, n.normalWS));
    return o;
}
TDVaryings TDShadowVertex(TDAttributes a) {
    TDVaryings o = TDVertex(a);
    float3 lightDir = _LightDirection;
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        lightDir = normalize(_LightPosition - o.positionWS);
    #endif
    float3 p = ApplyShadowBias(o.positionWS, o.normalWS, lightDir);
    o.positionCS = TransformWorldToHClip(p);
    #if UNITY_REVERSED_Z
        o.positionCS.z = min(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #else
        o.positionCS.z = max(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #endif
    return o;
}
void TDSetup(TDVaryings i) {
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(i.positionCS);
    #endif
}
half3 TDWorldNormal(TDVaryings i, half3 ts, bool frontFace) {
    half3 n = normalize(i.normalWS);
    half3 t = normalize(i.tangentWS.xyz);
    half3 b = cross(n,t) * i.tangentWS.w;
    half3 result = NormalizeNormalPerPixel(TransformTangentToWorld(ts, half3x3(t,b,n)));
    return frontFace ? result : -result;
}
half4 TDFragment(TDVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    TDSetup(i);
    SurfaceData s = (SurfaceData)0;
    s.normalTS = half3(0,0,1);
    s.occlusion = 1;
    s.alpha = 1;
    TDEvaluate(i,s);
    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.positionCS = i.positionCS;
    d.normalWS = TDWorldNormal(i,s.normalTS,IS_FRONT_VFACE(face,true,false));
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
        // positionCS is SV_POSITION in the fragment, so derive clip position again.
        d.shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
    #else
        d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
    #endif
    d.fogCoord = InitializeInputDataFog(float4(i.positionWS,1.0),i.fogAndVertexLight.x);
    d.vertexLighting = i.fogAndVertexLight.yzw;
    d.bakedGI = SAMPLE_GI(i.lightmapUV, i.vertexSH, d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.shadowMask = SAMPLE_SHADOWMASK(i.lightmapUV);
    half4 c = UniversalFragmentPBR(d,s);
    c.rgb = MixFog(c.rgb,d.fogCoord);
    c.a = s.alpha;
    return c;
}
half4 TDDepthFragment(TDVaryings i) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    TDSetup(i);
    TDAlpha(i);
    return 0;
}
half4 TDDepthNormalsFragment(TDVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    TDSetup(i);
    SurfaceData s = (SurfaceData)0;
    s.normalTS = half3(0,0,1);
    TDEvaluate(i,s);
    float3 n = TDWorldNormal(i,s.normalTS,IS_FRONT_VFACE(face,true,false));
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
        return half4(PackFloat2To888(oct), 0);
    #else
        return half4(n,0);
    #endif
}
#endif
