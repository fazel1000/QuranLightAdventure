// Shared forward, shadow, depth and depth-normal passes. No Built-in surface shaders.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

struct HGAttributes
{
    float4 positionOS : POSITION;
    half3 normalOS : NORMAL;
    half4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct HGVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    float4 screenPos : TEXCOORD4;
    DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
    #if defined(DYNAMICLIGHTMAP_ON)
        float2 dynamicLightmapUV : TEXCOORD6;
    #endif
    half4 fogAndVertexLight : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
#if defined(HG_SHADOW_PASS)
float3 _LightDirection;
float3 _LightPosition;
#endif

HGVaryings HGVertex(HGAttributes v)
{
    HGVaryings o = (HGVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 positionOS = HGDeform(v.positionOS.xyz, v.uv, v.color);
    VertexPositionInputs p = GetVertexPositionInputs(positionOS);
    VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
    o.positionWS = p.positionWS;
    o.positionCS = p.positionCS;
    o.normalWS = n.normalWS;
    o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
    o.uv = v.uv;
    o.screenPos = ComputeScreenPos(p.positionCS);
    o.fogAndVertexLight = half4(ComputeFogFactor(p.positionCS.z), VertexLighting(p.positionWS, n.normalWS));
    OUTPUT_LIGHTMAP_UV(v.lightmapUV, unity_LightmapST, o.lightmapUV);
    #if defined(DYNAMICLIGHTMAP_ON)
        o.dynamicLightmapUV = v.dynamicLightmapUV * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif
    OUTPUT_SH(n.normalWS, o.vertexSH);
    #if defined(HG_SHADOW_PASS)
        #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
            float3 lightDirectionWS = normalize(_LightPosition - p.positionWS);
        #else
            float3 lightDirectionWS = _LightDirection;
        #endif
        o.positionCS = TransformWorldToHClip(ApplyShadowBias(p.positionWS, n.normalWS, lightDirectionWS));
        #if UNITY_REVERSED_Z
            o.positionCS.z = min(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
        #else
            o.positionCS.z = max(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
        #endif
    #endif
    return o;
}

HGInput HGMakeInput(HGVaryings i, bool frontFace)
{
    HGInput data = (HGInput)0;
    data.uv_texcoord = i.uv;
    data.worldPos = i.positionWS;
    data.worldNormal = NormalizeNormalPerPixel(i.normalWS);
    float3 tangent = i.tangentWS.xyz;
    if (dot(tangent, tangent) < 0.00001)
        tangent = cross(abs(data.worldNormal.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0), data.worldNormal);
    data.tangentWS = normalize(tangent);
    data.bitangentWS = cross(data.worldNormal, data.tangentWS) * (i.tangentWS.w < 0.0 ? -1.0 : 1.0);
    #if defined(HG_FOLIAGE)
        if (!frontFace)
        {
            data.worldNormal = -data.worldNormal;
            data.bitangentWS = -data.bitangentWS;
        }
    #endif
    data.screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
    data.eyeDepth = -TransformWorldToView(i.positionWS).z;
    return data;
}

HGSurface HGEvaluate(HGInput data)
{
    HGSurface s = (HGSurface)0;
    s.Normal = half3(0, 0, 1);
    s.Alpha = 1;
    s.Occlusion = 1;
    surf(data, s);
    s.Smoothness = saturate(s.Smoothness);
    s.Metallic = saturate(s.Metallic);
    return s;
}

half4 HGForward(HGVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(i.positionCS);
    #endif
    HGInput data = HGMakeInput(i, IS_FRONT_VFACE(face, true, false));
    HGSurface s = HGEvaluate(data);
    InputData lighting = (InputData)0;
    lighting.positionWS = i.positionWS;
    lighting.normalWS = HGWorldNormal(data, s.Normal);
    lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
        lighting.shadowCoord = i.screenPos;
    #else
        lighting.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
    #endif
    lighting.fogCoord = i.fogAndVertexLight.x;
    lighting.vertexLighting = i.fogAndVertexLight.yzw;
    #if defined(DYNAMICLIGHTMAP_ON)
        lighting.bakedGI = SAMPLE_GI(i.lightmapUV, i.dynamicLightmapUV, i.vertexSH, lighting.normalWS);
    #else
        lighting.bakedGI = SAMPLE_GI(i.lightmapUV, i.vertexSH, lighting.normalWS);
    #endif
    lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    lighting.shadowMask = SAMPLE_SHADOWMASK(i.lightmapUV);
    SurfaceData surface = (SurfaceData)0;
    surface.albedo = s.Albedo;
    surface.alpha = saturate(s.Alpha);
    surface.normalTS = s.Normal;
    surface.emission = s.Emission;
    surface.metallic = s.Metallic;
    surface.smoothness = s.Smoothness;
    surface.occlusion = s.Occlusion;
    half4 color = UniversalFragmentPBR(lighting, surface);
    #if !defined(HG_BACKGROUND)
        color.rgb = MixFog(color.rgb, lighting.fogCoord);
    #endif
    color.a = surface.alpha;
    return color;
}

half4 HGDepth(HGVaryings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(i.positionCS);
    #endif
    HGClip(i.uv);
    return 0;
}

half4 HGDepthNormals(HGVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(i.positionCS);
    #endif
    HGInput data = HGMakeInput(i, IS_FRONT_VFACE(face, true, false));
    HGSurface s = HGEvaluate(data);
    half3 n = HGWorldNormal(data, s.Normal);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 oct = PackNormalOctQuadEncode(n);
        half3 packed = PackFloat2To888(saturate(oct * 0.5 + 0.5));
        return half4(packed, 0);
    #else
        return half4(n, 0);
    #endif
}
