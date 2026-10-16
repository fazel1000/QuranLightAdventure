// URP compatibility types for the user-supplied HIGHLANDS shaders.
#ifndef HIGHLANDS_URP_TYPES_INCLUDED
#define HIGHLANDS_URP_TYPES_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

struct HGInput
{
    float2 uv_texcoord;
    float3 worldPos;
    half3 worldNormal;
    half3 tangentWS;
    half3 bitangentWS;
    float2 screenUV;
    float eyeDepth;
};
struct HGSurface
{
    half3 Albedo;
    half3 Normal;
    half3 Emission;
    half Metallic;
    half Smoothness;
    half Occlusion;
    half Alpha;
};
half3 HGWorldNormal(HGInput i, half3 normalTS)
{
    return normalize(normalTS.x * i.tangentWS + normalTS.y * i.bitangentWS + normalTS.z * i.worldNormal);
}
half3 HGBlendNormals(half3 a, half3 b)
{
    return normalize(half3(a.xy + b.xy, a.z * b.z));
}
half3 HGUnpackNormal(half4 packed, half scale)
{
    return UnpackNormalScale(packed, scale);
}
#endif
