#ifndef SOFT_ENVIRONMENT_LIGHTING_INCLUDED
#define SOFT_ENVIRONMENT_LIGHTING_INCLUDED

// Bind the upstream lighting equation to URP's surface data. These are environment surfaces.
// Source: UnityURPToonLitShaderExample @ fbe276e0df1d1f9379e13581c20b6a080a1a6977 (MIT).
#define ToonSurfaceData SurfaceData
#define ToonLightingData InputData
#define _IndirectLightMinColor _ShadeFloor.rgb
#define _CelShadeMidPoint _BandCenter
#define _CelShadeSoftness _BandFeather
#define _ShadowMapColor _BounceTint.rgb
#define _ReceiveShadowMappingAmount 1
#define _IsFace false
#define _EmissionMulByBaseColor 0
#include "NiloLightingEquation.hlsl"
#undef ToonSurfaceData
#undef ToonLightingData
#undef _IndirectLightMinColor
#undef _CelShadeMidPoint
#undef _CelShadeSoftness
#undef _ShadowMapColor
#undef _ReceiveShadowMappingAmount
#undef _IsFace
#undef _EmissionMulByBaseColor

half3 SoftDirectLight(Light light, InputData inputData, SurfaceData surface, bool additional)
{
    half facing = dot(inputData.normalWS, light.direction);
    half attenuation = min(light.distanceAttenuation, 4.0) * light.shadowAttenuation;
    half3 lambert = saturate(facing) * attenuation * light.color;
    // Preserve HDR sunlight hue when the upstream function limits its color channels to one.
    half energy = max(1.0h, Max3(light.color.r, light.color.g, light.color.b));
    Light normalizedLight = light;
    normalizedLight.color /= energy;
    half3 band = ShadeSingleLight(surface, inputData, normalizedLight, additional) * energy;
    half3 halfway = SafeNormalize(light.direction + inputData.viewDirectionWS);
    half broadHighlight = smoothstep(0.78h, 0.98h, saturate(dot(inputData.normalWS, halfway)));
    half sheen = broadHighlight * surface.smoothness * lerp(0.025h, 0.18h, surface.metallic);
    return surface.albedo * lerp(lambert, band, _BandStrength) + sheen * lambert;
}

half3 SoftEnvironmentLighting(InputData inputData, SurfaceData surface)
{
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, surface);
    Light key = GetMainLight(inputData, shadowMask, ao);
    MixRealtimeAndBakedGI(key, inputData.normalWS, inputData.bakedGI, ao);
    // Large outdoor meshes can share a single dark probe. A tinted floor protects the shade family.
    half3 fill = max(inputData.bakedGI * _ShadeTint.rgb, _ShadeFloor.rgb);
    half3 color = surface.albedo * fill * lerp(1.0h, surface.occlusion, 0.5h) + surface.emission;
    #ifdef _LIGHT_LAYERS
    uint layers = GetMeshRenderingLayer();
    if (IsMatchingLightLayer(key.layerMask, layers))
    #endif
        color += SoftDirectLight(key, inputData, surface, false);
#if defined(_ADDITIONAL_LIGHTS)
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, layers))
        #endif
            color += SoftDirectLight(light, inputData, surface, true);
    }
    #endif
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, layers))
        #endif
            color += SoftDirectLight(light, inputData, surface, true);
    LIGHT_LOOP_END
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    color += inputData.vertexLighting * surface.albedo;
#endif
    return color;
}
#endif
