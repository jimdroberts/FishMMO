// The ground's lighting: URP's UniversalFragmentPBR and GlobalIllumination with one change, the curve that says
// how much of the sky a surface reflects (used by both terrain shaders, FishWeatherTerrainPasses.hlsl and
// FishWeatherTerrainArrayPasses.hlsl, after URP's Lighting.hlsl).
//
// URP's EnvironmentBRDFSpecular (BRDF.hlsl) lerps F0 to a "grazing term" of smoothness + reflectivity by
// (1 − N·V)⁴ and divides by roughness² + 1. For a smooth surface that is close to the true split-sum curve; for a
// rough one it is far too much at a glancing view. Ground of smoothness 0.2 seen at 84° reflects 0.12 of the sky
// that way against 0.03 on the real curve, which, for dark grass (albedo ~0.12) under an overcast where the sky
// outweighs the sun, is about as much as its own diffuse: the whole field takes on the sky's colour, and
// RenderSettings.reflectionIntensity → 0 takes it away again. Karis's fit to the pre-integrated curve
// ("Physically Based Shading on Mobile", 2014) replaces it. Dry ground (smoothness 0.1–0.3) now reflects 2.5–4×
// less at a glancing view; wet ground and puddles (0.75–0.95, from FishWeatherSurface) keep about 80% of what URP
// gave them, so they still mirror the sky.
//
// On a URP upgrade, diff FishFragmentPBR against UniversalFragmentPBR (Lighting.hlsl) and FishGlobalIllumination
// against GlobalIllumination (GlobalIllumination.hlsl); nothing else here is URP's.
#ifndef FISHMMO_GROUND_LIGHTING_INCLUDED
#define FISHMMO_GROUND_LIGHTING_INCLUDED

// How much of the environment a surface reflects, by its F0, roughness and view angle (Karis's EnvBRDFApprox).
half3 FishEnvironmentSpecular(BRDFData brdfData, half NoV)
{
    const half4 c0 = half4(-1.0h, -0.0275h, -0.572h, 0.022h);
    const half4 c1 = half4(1.0h, 0.0425h, 1.04h, -0.04h);
    half4 r = brdfData.perceptualRoughness * c0 + c1;
    half a004 = min(r.x * r.x, exp2(-9.28h * NoV)) * r.x + r.y;
    half2 ab = half2(-1.04h, 1.04h) * a004 + r.zw;
    return max(brdfData.specular * ab.x + ab.y, 0.0h);
}

// URP's GlobalIllumination (no clear coat), with FishEnvironmentSpecular for EnvironmentBRDFSpecular.
half3 FishGlobalIllumination(BRDFData brdfData, half3 bakedGI, half occlusion, float3 positionWS,
    half3 normalWS, half3 viewDirectionWS, float2 normalizedScreenSpaceUV)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half3 indirectSpecular = GlossyEnvironmentReflection(reflectVector, positionWS, brdfData.perceptualRoughness, 1.0h, normalizedScreenSpaceUV);
    half3 color = bakedGI * brdfData.diffuse + indirectSpecular * FishEnvironmentSpecular(brdfData, NoV);

    if (IsOnlyAOLightingFeatureEnabled())
    {
        color = half3(1, 1, 1); // "Base white" for AO debug lighting mode
    }
    return color * occlusion;
}

// URP's UniversalFragmentPBR (the overload taking loose values, no clear coat), with FishGlobalIllumination.
half4 FishFragmentPBR(InputData inputData, half3 albedo, half metallic, half smoothness, half occlusion, half alpha)
{
    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = albedo;
    surfaceData.metallic = metallic;
    surfaceData.smoothness = smoothness;
    surfaceData.normalTS = half3(0, 0, 1);
    surfaceData.occlusion = occlusion;
    surfaceData.alpha = alpha;
    surfaceData.clearCoatSmoothness = 1;

    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
    #else
    bool specularHighlightsOff = false;
    #endif
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

    #if defined(DEBUG_DISPLAY)
    half4 debugColor;
    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
    #endif

    BRDFData noClearCoat = (BRDFData)0;
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);

    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);

    lightingData.giColor = FishGlobalIllumination(brdfData, inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                                  inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = LightingPhysicallyBased(brdfData, noClearCoat, mainLight,
                                                              inputData.normalWS, inputData.viewDirectionWS,
                                                              0.0h, specularHighlightsOff);
    }

    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, noClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          0.0h, specularHighlightsOff);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, noClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          0.0h, specularHighlightsOff);
        }
    LIGHT_LOOP_END
    #endif

    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
    #endif

#if REAL_IS_HALF
    // Clamp any half.inf+ to HALF_MAX
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}

#endif
