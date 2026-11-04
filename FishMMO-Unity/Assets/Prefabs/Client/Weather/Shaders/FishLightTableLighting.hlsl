#ifndef FISHMMO_LIGHT_TABLE_LIGHTING_INCLUDED
#define FISHMMO_LIGHT_TABLE_LIGHTING_INCLUDED
// WebGPU: every URP function that reads a per-light array, as a copy reading the light table (FishLightTable.hlsl), and a
// macro that sends every later call there. URP's own versions stay compiled but are never called, so the compiler drops
// their arrays from $Globals (WebGpuLightTableTests checks the compiled reflection of every pass including this).
//
// How the calls are moved without editing URP: each of these is defined in the same URP file as its callers (a macro there
// would rename URP's definition too), so URP's files are included here first, exactly as Lighting.hlsl begins (BRDF, the
// debug helpers, GlobalIllumination, which pulls in RealtimeLights, Shadows, LightCookie and Clustering); then the copies;
// then the macros. Everything after this point — Lighting.hlsl's UniversalFragmentPBR and light loops, and our own passes —
// calls the copies. FishLightCookieVariants.hlsl includes this after Core.hlsl and before any lit pass includes
// Lighting.hlsl. DebuggingCommon, not Debugging3D: Debugging3D's debug-view code is left to compile where Lighting.hlsl
// puts it, after the pass's own material defines (FishWeatherLitInput.hlsl defines _NORMALMAP, which it tests).
//
// Each copy is URP 17.4's body with only the array reads changed (WebGpuLightTableTests pins this against the package
// source, so a URP upgrade that changes one fails the test rather than drifting).
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "FishLightTable.hlsl"

// ── Shadows.hlsl ─────────────────────────────────────────────────────────

ShadowSamplingData FishTableGetAdditionalLightShadowSamplingData(int index)
{
    ShadowSamplingData shadowSamplingData = (ShadowSamplingData)0;

    #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
        // shadowOffsets are used in SampleShadowmapFiltered for low quality soft shadows.
        shadowSamplingData.shadowOffset0 = _AdditionalShadowOffset0;
        shadowSamplingData.shadowOffset1 = _AdditionalShadowOffset1;

        // shadowmapSize is used in SampleShadowmapFiltered otherwise.
        shadowSamplingData.shadowmapSize = _AdditionalShadowmapSize;
        shadowSamplingData.softShadowQuality = FishLightTableLight(index, FISH_LIGHT_ROW_SHADOW).y;
    #endif

    return shadowSamplingData;
}

half4 FishTableGetAdditionalLightShadowParams(int lightIndex)
{
    half4 results;
    #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
        results = FishLightTableLight(lightIndex, FISH_LIGHT_ROW_SHADOW);
        // workaround: Avoid failing the graphics test using Terrain Shader on Android Vulkan when using dynamic branching for fog keywords.
        #if !SKIP_SHADOWS_LIGHT_INDEX_CHECK
            results.w = lightIndex < 0 ? -1 : results.w;
        #endif
    #else
        // Same defaults as set in AdditionalLightsShadowCasterPass.cs
        return half4(0, 0, 0, -1);
    #endif

    return results;
}

half FishTableAdditionalLightRealtimeShadow(int lightIndex, float3 positionWS, half3 lightDirection, half4 shadowParams, ShadowSamplingData shadowSamplingData)
{
    #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
        int shadowSliceIndex = shadowParams.w;
        if (shadowSliceIndex < 0)
            return 1.0;

        half isPointLight = shadowParams.z;

        UNITY_BRANCH
        if (isPointLight)
        {
            // This is a point light, we have to find out which shadow slice to sample from
            const int cubeFaceOffset = CubeMapFaceID(-lightDirection);
            shadowSliceIndex += cubeFaceOffset;
        }

        float4 shadowCoord = mul(FishLightTableMatrix(FISH_LIGHT_TABLE_SLICES_BEGIN + (uint)shadowSliceIndex * FISH_LIGHT_TABLE_SLICE_ROWS), float4(positionWS, 1.0));

        return SampleShadowmap(TEXTURE2D_ARGS(_AdditionalLightsShadowmapTexture, sampler_LinearClampCompare), shadowCoord, shadowSamplingData, shadowParams, true);
    #else
        return half(1.0);
    #endif
}

half FishTableAdditionalLightRealtimeShadow(int lightIndex, float3 positionWS, half3 lightDirection)
{
    #if !defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
        return half(1.0);
    #endif

    return FishTableAdditionalLightRealtimeShadow(lightIndex, positionWS, lightDirection, FishTableGetAdditionalLightShadowParams(lightIndex), FishTableGetAdditionalLightShadowSamplingData(lightIndex));
}

// Deprecated in URP: AdditionalLightRealtimeShadow(int lightIndex, float3 positionWS).
half FishTableAdditionalLightRealtimeShadow(int lightIndex, float3 positionWS)
{
    return FishTableAdditionalLightRealtimeShadow(lightIndex, positionWS, half3(1, 0, 0));
}

half FishTableAdditionalLightShadow(int lightIndex, float3 positionWS, half3 lightDirection, half4 shadowMask, half4 occlusionProbeChannels)
{
    half4 shadowParams = FishTableGetAdditionalLightShadowParams(lightIndex);
    ShadowSamplingData samplingData = FishTableGetAdditionalLightShadowSamplingData(lightIndex);
    half realtimeShadow = FishTableAdditionalLightRealtimeShadow(lightIndex, positionWS, lightDirection, shadowParams, samplingData);

    #ifdef CALCULATE_BAKED_SHADOWS
        // This fading of the baked shadow using the light's shadow strength parameter needs
        // to be guarded against the Real-Time Shadow keyword as _AdditionalShadowParams is
        // only included in URP Shaders and updated when real time additional shadows are enabled.
        #ifndef ADDITIONAL_LIGHT_CALCULATE_SHADOWS
            shadowParams.x = half(1.0);
        #endif
        half bakedShadow = BakedShadow(shadowMask, occlusionProbeChannels, shadowParams);
    #else
        half bakedShadow = half(1.0);
    #endif

    #ifdef ADDITIONAL_LIGHT_CALCULATE_SHADOWS
        half shadowFade = GetAdditionalLightShadowFade(positionWS);
    #else
        half shadowFade = half(1.0);
    #endif

    return MixRealtimeAndBakedShadows(realtimeShadow, bakedShadow, shadowFade);
}

// Deprecated in URP: GetAdditionalLightShadowStrenth.
half FishTableGetAdditionalLightShadowStrenth(int lightIndex)
{
    #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
        return half(FishLightTableLight(lightIndex, FISH_LIGHT_ROW_SHADOW).x);
    #else
        return half(1.0);
    #endif
}

// ── RealtimeLights.hlsl ─────────────────────────────────────────────────

// Fills a light struct given a perObjectLightIndex
Light FishTableGetAdditionalPerObjectLight(int perObjectLightIndex, float3 positionWS)
{
    // Abstraction over Light input constants
    float4 lightPositionWS = FishLightTableLight(perObjectLightIndex, FISH_LIGHT_ROW_POSITION);
    half3 color = FishLightTableLight(perObjectLightIndex, FISH_LIGHT_ROW_COLOR).rgb;
    half4 distanceAndSpotAttenuation = FishLightTableLight(perObjectLightIndex, FISH_LIGHT_ROW_ATTENUATION);
    half4 spotDirection = FishLightTableLight(perObjectLightIndex, FISH_LIGHT_ROW_SPOT_DIR);
    uint lightLayerMask = _FishLightTable[(uint)perObjectLightIndex * FISH_LIGHT_TABLE_LIGHT_ROWS + FISH_LIGHT_ROW_MISC].x;

    // Directional lights store direction in lightPosition.xyz and have .w set to 0.0.
    // This way the following code will work for both directional and punctual lights.
    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
    // full-float precision required on some platforms
    float attenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy) * AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);

    Light light;
    light.direction = lightDirection;
    light.distanceAttenuation = attenuation;
    light.shadowAttenuation = 1.0; // This value can later be overridden in GetAdditionalLight(uint i, float3 positionWS, half4 shadowMask)
    light.color = color;
    light.layerMask = lightLayerMask;

    return light;
}

// Fills a light struct given a loop i index. This will convert the i
// index to a perObjectLightIndex
Light FishTableGetAdditionalLight(uint i, float3 positionWS)
{
#if USE_CLUSTER_LIGHT_LOOP
    int lightIndex = i;
#else
    int lightIndex = GetPerObjectLightIndex(i);
#endif
    return FishTableGetAdditionalPerObjectLight(lightIndex, positionWS);
}

Light FishTableGetAdditionalLight(uint i, float3 positionWS, half4 shadowMask)
{
#if USE_CLUSTER_LIGHT_LOOP
    int lightIndex = i;
#else
    int lightIndex = GetPerObjectLightIndex(i);
#endif
    Light light = FishTableGetAdditionalPerObjectLight(lightIndex, positionWS);

    half4 occlusionProbeChannels = FishLightTableLight(lightIndex, FISH_LIGHT_ROW_OCCLUSION);
    light.shadowAttenuation = FishTableAdditionalLightShadow(lightIndex, positionWS, light.direction, shadowMask, occlusionProbeChannels);
#if defined(_LIGHT_COOKIES)
    real3 cookieColor = SampleAdditionalLightCookie(lightIndex, positionWS);
    light.color *= cookieColor;
#endif

    return light;
}

Light FishTableGetAdditionalLight(uint i, InputData inputData, half4 shadowMask, AmbientOcclusionFactor aoFactor)
{
    Light light = FishTableGetAdditionalLight(i, inputData.positionWS, shadowMask);

    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
    {
        light.color *= aoFactor.directAmbientOcclusion;
    }
    #endif

    return light;
}

// ── GlobalIllumination.hlsl (the Forward+ reflection probes: only the clustered path reads arrays) ─────────

#if USE_CLUSTER_LIGHT_LOOP && CLUSTER_HAS_REFLECTION_PROBES
half3 FishTableCalculateIrradianceFromReflectionProbes(half3 reflectVector, float3 positionWS, half perceptualRoughness, float2 normalizedScreenSpaceUV)
{
    half3 irradiance = half3(0.0h, 0.0h, 0.0h);
    half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    float totalWeight = 0.0f;
    uint probeIndex;
    ClusterIterator it = ClusterInit(normalizedScreenSpaceUV, positionWS, 1);
    [loop] while (ClusterNext(it, probeIndex) && totalWeight < 0.99f)
    {
        probeIndex -= URP_FP_PROBES_BEGIN;
        float4 probeBoxMin = FishLightTableProbe(probeIndex, FISH_PROBE_ROW_BOX_MIN);
        float4 probeBoxMax = FishLightTableProbe(probeIndex, FISH_PROBE_ROW_BOX_MAX);
        float4 probePosition = FishLightTableProbe(probeIndex, FISH_PROBE_ROW_POSITION);

#if defined(REFLECTION_PROBE_ROTATION)
        // We need to rotated positionWS such that we can assume the influence volumes to be axis aligned
        // when calculating the weight and box projection.
        float4 probeRotation = FishLightTableProbe(probeIndex, FISH_PROBE_ROW_ROTATION);
        float3 probeCenterPosWS = GetReflectionProbeCenter(probeBoxMin, probeBoxMax);
        float3 rotPosWS = GetRotatedPoint(probeCenterPosWS, probeRotation, positionWS);
#else
        float3 rotPosWS = positionWS;
#endif
        float weight = CalculateProbeWeight(rotPosWS, probeBoxMin, probeBoxMax);
        weight = min(weight, 1.0f - totalWeight);

        half3 sampleVector = reflectVector;
        if (_REFLECTION_PROBE_BOX_PROJECTION)
        {
            #if defined(REFLECTION_PROBE_ROTATION)
            sampleVector = BoxProjectedCubemapDirection(probeRotation, reflectVector, rotPosWS, probePosition, probeBoxMin, probeBoxMax);
            #else
            sampleVector = BoxProjectedCubemapDirection(reflectVector, rotPosWS, probePosition, probeBoxMin, probeBoxMax);
            #endif
        }

        uint maxMip = (uint)abs(probePosition.w) - 1;
        half probeMip = min(mip, maxMip);
        float2 uv = saturate(PackNormalOctQuadEncode(sampleVector) * 0.5 + 0.5);

        float mip0 = floor(probeMip);
        float mip1 = mip0 + 1;
        float mipBlend = probeMip - mip0;
        float4 scaleOffset0 = FishLightTableProbe(probeIndex, FISH_PROBE_ROW_MIP_SCALE_OFFSET + (uint)mip0);
        float4 scaleOffset1 = FishLightTableProbe(probeIndex, FISH_PROBE_ROW_MIP_SCALE_OFFSET + (uint)mip1);

        half3 irradiance0 = half4(SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, sampler_LinearClamp, uv * scaleOffset0.xy + scaleOffset0.zw, 0.0)).rgb;
        half3 irradiance1 = half4(SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, sampler_LinearClamp, uv * scaleOffset1.xy + scaleOffset1.zw, 0.0)).rgb;
        irradiance += weight * lerp(irradiance0, irradiance1, mipBlend);
        totalWeight += weight;
    }

    // Use any remaining weight to blend to environment reflection cube map
    if (totalWeight < 0.99f)
    {
        half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, reflectVector, mip));

        irradiance += (1.0f - totalWeight) * DecodeHDREnvironment(encodedIrradiance, _GlossyEnvironmentCubeMap_HDR);
    }

    return irradiance;
}

half3 FishTableGlossyEnvironmentReflection(half3 reflectVector, float3 positionWS, half perceptualRoughness, half occlusion, float2 normalizedScreenSpaceUV)
{
    half3 irradiance;

#if !defined(_ENVIRONMENTREFLECTIONS_OFF)
    if (_REFLECTION_PROBE_BLENDING)
    {
        irradiance = FishTableCalculateIrradianceFromReflectionProbes(reflectVector, positionWS, perceptualRoughness, normalizedScreenSpaceUV);
    }
    else
    {
        if (_REFLECTION_PROBE_BOX_PROJECTION)
        {
            #if defined(REFLECTION_PROBE_ROTATION)
            float3 probeCenterPosWS0 = unity_SpecCube0_BoxMin.xyz + (unity_SpecCube0_BoxMax.xyz - unity_SpecCube0_BoxMin.xyz) / 2;
            float3 rotPosWS0 = RotateVectorByQuat(unity_SpecCube0_Rotation, positionWS - probeCenterPosWS0) + probeCenterPosWS0;
            half3 rotReflectVector0 = RotateVectorByQuat(unity_SpecCube0_Rotation, reflectVector);
            float4 inverseRotation0 = -unity_SpecCube0_Rotation;
            inverseRotation0.w = -inverseRotation0.w;
            reflectVector = BoxProjectedCubemapDirection(rotReflectVector0, rotPosWS0, unity_SpecCube0_ProbePosition, unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax);
            reflectVector = RotateVectorByQuat(inverseRotation0, reflectVector);
            #else
            reflectVector = BoxProjectedCubemapDirection(reflectVector, positionWS, unity_SpecCube0_ProbePosition, unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax);
            #endif
        }
        half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
        half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectVector, mip));

        irradiance = DecodeHDREnvironment(encodedIrradiance, unity_SpecCube0_HDR);
    }
#else // _ENVIRONMENTREFLECTIONS_OFF
    irradiance = _GlossyEnvironmentColor.rgb;
#endif // !_ENVIRONMENTREFLECTIONS_OFF

    return irradiance * occlusion;
}

// URP's overload without a position reads no probe array.
half3 FishTableGlossyEnvironmentReflection(half3 reflectVector, half perceptualRoughness, half occlusion)
{
    return GlossyEnvironmentReflection(reflectVector, perceptualRoughness, occlusion);
}

half3 FishTableGlobalIllumination(BRDFData brdfData, BRDFData brdfDataClearCoat, float clearCoatMask,
    half3 bakedGI, half occlusion, float3 positionWS,
    half3 normalWS, half3 viewDirectionWS, float2 normalizedScreenSpaceUV)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV);

    half3 indirectDiffuse = bakedGI;
    half3 indirectSpecular = FishTableGlossyEnvironmentReflection(reflectVector, positionWS, brdfData.perceptualRoughness, 1.0h, normalizedScreenSpaceUV);

    half3 color = EnvironmentBRDF(brdfData, indirectDiffuse, indirectSpecular, fresnelTerm);

    if (IsOnlyAOLightingFeatureEnabled())
    {
        color = half3(1,1,1); // "Base white" for AO debug lighting mode
    }

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half3 coatIndirectSpecular = FishTableGlossyEnvironmentReflection(reflectVector, positionWS, brdfDataClearCoat.perceptualRoughness, 1.0h, normalizedScreenSpaceUV);
    // TODO: "grazing term" causes problems on full roughness
    half3 coatColor = EnvironmentBRDFClearCoat(brdfDataClearCoat, clearCoatMask, coatIndirectSpecular, fresnelTerm);

    // Blend with base layer using khronos glTF recommended way using NoV
    // Smooth surface & "ambiguous" lighting
    // NOTE: fresnelTerm (above) is pow4 instead of pow5, but should be ok as blend weight.
    half coatFresnel = kDielectricSpec.x + kDielectricSpec.a * fresnelTerm;
    return (color * (1.0 - coatFresnel * clearCoatMask) + coatColor) * occlusion;
#else
    return color * occlusion;
#endif
}

// Backwards compatiblity
half3 FishTableGlobalIllumination(BRDFData brdfData, half3 bakedGI, half occlusion, float3 positionWS, half3 normalWS, half3 viewDirectionWS)
{
    const BRDFData noClearCoat = (BRDFData)0;
    return FishTableGlobalIllumination(brdfData, noClearCoat, 0.0, bakedGI, occlusion, positionWS, normalWS, viewDirectionWS, 0);
}

// URP's overloads without a position read no probe array.
half3 FishTableGlobalIllumination(BRDFData brdfData, BRDFData brdfDataClearCoat, float clearCoatMask,
    half3 bakedGI, half occlusion,
    half3 normalWS, half3 viewDirectionWS)
{
    return GlobalIllumination(brdfData, brdfDataClearCoat, clearCoatMask, bakedGI, occlusion, normalWS, viewDirectionWS);
}

half3 FishTableGlobalIllumination(BRDFData brdfData, half3 bakedGI, half occlusion, half3 normalWS, half3 viewDirectionWS)
{
    return GlobalIllumination(brdfData, bakedGI, occlusion, normalWS, viewDirectionWS);
}

#define CalculateIrradianceFromReflectionProbes FishTableCalculateIrradianceFromReflectionProbes
#define GlossyEnvironmentReflection FishTableGlossyEnvironmentReflection
#define GlobalIllumination FishTableGlobalIllumination
#endif

// ── The redirects ─────────────────────────────────────────────────────────

#define GetAdditionalLightShadowSamplingData FishTableGetAdditionalLightShadowSamplingData
#define GetAdditionalLightShadowParams FishTableGetAdditionalLightShadowParams
#define AdditionalLightRealtimeShadow FishTableAdditionalLightRealtimeShadow
#define AdditionalLightShadow FishTableAdditionalLightShadow
#define GetAdditionalLightShadowStrenth FishTableGetAdditionalLightShadowStrenth
#define GetAdditionalPerObjectLight FishTableGetAdditionalPerObjectLight
#define GetAdditionalLight FishTableGetAdditionalLight

#undef CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
#if USE_CLUSTER_LIGHT_LOOP && defined(LIGHTMAP_ON) && defined(LIGHTMAP_SHADOW_MIXING)
#define CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK if (FishLightTableLight(lightIndex, FISH_LIGHT_ROW_COLOR).a > 0.0h) continue;
#else
#define CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
#endif
#endif
