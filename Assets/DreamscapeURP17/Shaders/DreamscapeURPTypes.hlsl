// URP compatibility types for the user-supplied DREAMSCAPE shaders.
#ifndef DREAMSCAPE_URP_TYPES_INCLUDED
#define DREAMSCAPE_URP_TYPES_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

struct DGInput
{
    float2 uv_texcoord;
    float3 worldPos;
    half3 worldNormal;
    half3 tangentWS;
    half3 bitangentWS;
    float4 screenPos;
    float2 screenUV;
    float eyeDepth;
};
struct DGSurface
{
    half3 Albedo;
    half3 Normal;
    half3 Emission;
    half3 Translucency;
    half Metallic;
    half Smoothness;
    half Occlusion;
    half Alpha;
};
half3 DGWorldNormal(DGInput i, half3 normalTS)
{
    return normalize(normalTS.x * i.tangentWS + normalTS.y * i.bitangentWS + normalTS.z * i.worldNormal);
}
half3 DGBlendNormals(half3 a, half3 b)
{
    return normalize(half3(a.xy + b.xy, a.z * b.z));
}
half3 DGUnpackNormalDefault(half4 packed) { return UnpackNormalScale(packed, 1.0); }
half3 DGUnpackNormal(half4 packed, half scale)
{
    return UnpackNormalScale(packed, scale);
}

struct DGVertexData
{
    float4 vertex;
    float3 normal;
    float4 tangent;
    float4 texcoord;
    float4 color;
};
float2 DGSafeNormalize(float2 x) { return x * rsqrt(max(dot(x, x), 1e-12)); }
float3 DGSafeNormalize(float3 x) { return x * rsqrt(max(dot(x, x), 1e-12)); }
float4 DGSafeNormalize(float4 x) { return x * rsqrt(max(dot(x, x), 1e-12)); }
float DGSafeDivisor(float x) { return x < 0 ? -max(-x, 0.0001) : max(x, 0.0001); }
float DGLuminance(float3 x) { return dot(x, float3(0.2126, 0.7152, 0.0722)); }
bool DGIsGammaSpace()
{
    #if defined(UNITY_COLORSPACE_GAMMA)
        return true;
    #else
        return false;
    #endif
}
float DGGammaToLinearSpaceExact(float x)
{
    if (x <= 0.04045) return x / 12.92;
    if (x < 1.0) return pow((x + 0.055) / 1.055, 2.4);
    return pow(x, 2.2);
}
#if defined(DG_NEEDS_DEPTH)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    float DGSceneEyeDepth(float2 uv)
    {
        float raw = SampleSceneDepth(saturate(uv));
        if (unity_OrthoParams.w > 0.5)
        {
            #if UNITY_REVERSED_Z
                raw = 1.0 - raw;
            #endif
            return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
        }
        return LinearEyeDepth(raw, _ZBufferParams);
    }
    float4 DGScreenTexelSize()
    {
        return float4(rcp(_ScaledScreenParams.xy), _ScaledScreenParams.xy);
    }
#endif
#if defined(DG_NEEDS_SCENE_COLOR)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#endif
#endif
