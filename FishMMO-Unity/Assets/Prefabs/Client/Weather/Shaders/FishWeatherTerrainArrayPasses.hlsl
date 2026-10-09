// Passes for FishMMO/Weather Terrain Array. The lighting, GI, shadow, depth and selection code is URP's
// TerrainLitPasses.hlsl as FishWeatherTerrainPasses.hlsl carries it; what differs is where the surface comes
// from — FishSampleArraySurface (FishWeatherTerrainArrayInput.hlsl) instead of four splat textures — and that
// the splat coordinates are world-space, so there are no per-layer interpolators.
//
// FishMMO/Backdrop Ground compiles these same passes with FISH_TERRAIN_BACKDROP defined: a plain mesh rather
// than a terrain, and FishBackdropSurface for its ground (FISH_GROUND_SURFACE).
//
// On a URP upgrade, diff the lighting half of this file against
// Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl.
#ifndef FISHMMO_WEATHER_TERRAIN_ARRAY_PASSES_INCLUDED
#define FISHMMO_WEATHER_TERRAIN_ARRAY_PASSES_INCLUDED

#include "FishSurface.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
// FishMMO edit: the ground reflects the sky on the true curve, not URP's (FishGroundLighting.hlsl).
#include "FishGroundLighting.hlsl"

// Every layer may carry a normal map, so the tangent frame is always interpolated (TerrainLit only does when
// the material's _NORMALMAP keyword says some layer has one). Per-pixel normals replace it when instanced.
#if defined(ENABLE_TERRAIN_PERPIXEL_NORMAL)
#define FISH_TERRAIN_TANGENT_FRAME 0
#else
#define FISH_TERRAIN_TANGENT_FRAME 1
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 uvMainAndLM              : TEXCOORD0; // xy: control, zw: lightmap

    #if FISH_TERRAIN_TANGENT_FRAME
        half4 normal                    : TEXCOORD3;    // xyz: normal, w: viewDir.x
        half4 tangent                   : TEXCOORD4;    // xyz: tangent, w: viewDir.y
        half4 bitangent                 : TEXCOORD5;    // xyz: bitangent, w: viewDir.z
    #else
        half3 normal                    : TEXCOORD3;
        half3 vertexSH                  : TEXCOORD4; // SH
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        half4 fogFactorAndVertexLight   : TEXCOORD6; // x: fogFactor, yzw: vertex light
    #else
        half  fogFactor                 : TEXCOORD6;
    #endif

    float3 positionWS               : TEXCOORD7;

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        float4 shadowCoord              : TEXCOORD8;
    #endif

#if defined(DYNAMICLIGHTMAP_ON)
    float2 dynamicLightmapUV        : TEXCOORD9;
#endif

#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion           : TEXCOORD10;
#endif

    float4 clipPos                  : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

/// The world normal at a pixel from the interpolated frame (or the instanced normal map) and a tangent-space one.
half3 FishTerrainNormalWS(Varyings IN, half3 normalTS)
{
    #if FISH_TERRAIN_TANGENT_FRAME
        return TransformTangentToWorld(normalTS, half3x3(-IN.tangent.xyz, IN.bitangent.xyz, IN.normal.xyz));
    #else
        float2 sampleCoords = (IN.uvMainAndLM.xy / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
        half3 normalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
        half3 tangentWS = cross(GetObjectToWorldMatrix()._13_23_33, normalWS);
        return TransformTangentToWorld(normalTS, half3x3(-tangentWS, cross(normalWS, tangentWS), normalWS));
    #endif
}

void InitializeInputData(Varyings IN, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = IN.positionWS;
    inputData.positionCS = IN.clipPos;

    #if FISH_TERRAIN_TANGENT_FRAME
        half3 viewDirWS = half3(IN.normal.w, IN.tangent.w, IN.bitangent.w);
        inputData.tangentToWorld = half3x3(-IN.tangent.xyz, IN.bitangent.xyz, IN.normal.xyz);
        half3 SH = 0;
    #else
        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
        half3 SH = IN.vertexSH;
    #endif
    inputData.normalWS = FishTerrainNormalWS(IN, normalTS);

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        inputData.shadowCoord = IN.shadowCoord;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
    #else
        inputData.shadowCoord = float4(0, 0, 0, 0);
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactorAndVertexLight.x);
        inputData.vertexLighting = IN.fogFactorAndVertexLight.yzw;
    #else
        inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactor);
    #endif

    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.clipPos);

    #if defined(DEBUG_DISPLAY)
    #if defined(DYNAMICLIGHTMAP_ON)
    inputData.dynamicLightmapUV = IN.dynamicLightmapUV;
    #endif
    #if defined(LIGHTMAP_ON)
    inputData.staticLightmapUV = IN.uvMainAndLM.zw;
    #else
    inputData.vertexSH = SH;
    #endif
    #if defined(USE_APV_PROBE_OCCLUSION)
    inputData.probeOcclusion = IN.probeOcclusion;
    #endif
    // No mip-streaming data for the arrays; flag the surface as terrain as TerrainLit does.
    inputData.streamInfo = TERRAIN_STREAM_INFO;
    #endif
}

void InitializeBakedGIData(Varyings IN, inout InputData inputData)
{
    #if FISH_TERRAIN_TANGENT_FRAME
    half3 SH = 0;
    #else
    half3 SH = IN.vertexSH;
    #endif

#if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, inputData.positionCS.xy);
#elif defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(IN.uvMainAndLM.zw, IN.dynamicLightmapUV, SH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(IN.uvMainAndLM.zw);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(SH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        inputData.positionCS.xy,
        IN.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(IN.uvMainAndLM.zw, SH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(IN.uvMainAndLM.zw);
#endif
}

void SplatmapFinalColor(inout half4 color, half fogCoord)
{
    color.rgb *= color.a;
    #ifndef TERRAIN_GBUFFER
        color.rgb = MixFog(color.rgb, fogCoord);
    #endif
}

/// Deep snow lifts the ground it lies on — the same lift FishWeatherTerrain applies, so the two terrain
/// shaders agree about where the snow's surface is.
void FishSnowLift(inout float4 positionOS, float3 normalOS)
{
    // FishMMO edit: off unless the quality tier asks for it (_FishSnowDepth is 0 below High), and only
    // where the sky can reach.
    if (_FishSnowDepth > 0.0 && _FishWeatherTier.x > 0.0 && _FishWeatherCover.x > 0.0)
    {
        float3 snowWorld = TransformObjectToWorld(positionOS.xyz);
        float3 snowNormal = TransformObjectToWorldNormal(normalOS);
        float lift = FishCoverAt(snowWorld).x * _FishSnowDepth * FishCoverFacing(snowNormal, snowWorld, 0.5);
        positionOS.xyz += TransformWorldToObjectDir(float3(0, 1, 0)) * lift;
    }
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

Varyings SplatmapVert(Attributes v)
{
    Varyings o = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(v.positionOS, v.normalOS, v.texcoord);
    FishSnowLift(v.positionOS, v.normalOS);

    VertexPositionInputs Attributes = GetVertexPositionInputs(v.positionOS.xyz);

    o.uvMainAndLM.xy = v.texcoord;
    o.uvMainAndLM.zw = v.texcoord * unity_LightmapST.xy + unity_LightmapST.zw;

#if defined(DYNAMICLIGHTMAP_ON)
    o.dynamicLightmapUV = v.texcoord * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif

    #if FISH_TERRAIN_TANGENT_FRAME
        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(Attributes.positionWS);
        float4 vertexTangent = float4(cross(float3(0, 0, 1), v.normalOS), 1.0);
        VertexNormalInputs normalInput = GetVertexNormalInputs(v.normalOS, vertexTangent);

        o.normal = half4(normalInput.normalWS, viewDirWS.x);
        o.tangent = half4(normalInput.tangentWS, viewDirWS.y);
        o.bitangent = half4(normalInput.bitangentWS, viewDirWS.z);
    #else
        o.normal = TransformObjectToWorldNormal(v.normalOS);
        OUTPUT_SH4(Attributes.positionWS, o.normal.xyz, GetWorldSpaceNormalizeViewDir(Attributes.positionWS), o.vertexSH, o.probeOcclusion);
    #endif

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(Attributes.positionCS.z);
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        o.fogFactorAndVertexLight.x = fogFactor;
        o.fogFactorAndVertexLight.yzw = VertexLighting(Attributes.positionWS, o.normal.xyz);
    #else
        o.fogFactor = fogFactor;
    #endif

    o.positionWS = Attributes.positionWS;
    o.clipPos = Attributes.positionCS;

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        o.shadowCoord = GetShadowCoord(Attributes);
    #endif

    return o;
}

#ifdef TERRAIN_GBUFFER
GBufferFragOutput SplatmapFragment(Varyings IN)
#else
void SplatmapFragment(
    Varyings IN
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
    )
#endif
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
#ifdef _ALPHATEST_ON
    ClipHoles(IN.uvMainAndLM.xy);
#endif

    FishArraySurface ground = FISH_GROUND_SURFACE(IN.uvMainAndLM.xy, IN.positionWS);
    half3 albedo = ground.albedo;
    half metallic = ground.metallic;
    half smoothness = ground.smoothness;
    half occlusion = ground.occlusion;
    half alpha = 1.0h;

    InputData inputData;
    InitializeInputData(IN, ground.normalTS, inputData);

    // FishMMO edit: the weather on this ground.
    if (_FishWeatherAmount > 0.0)
    {
        half3 weathered = inputData.normalWS;
        FishWeatherSurface(inputData.positionWS, albedo, weathered, smoothness, metallic, occlusion);
        inputData.normalWS = normalize(lerp(inputData.normalWS, weathered, _FishWeatherAmount));
    }

#if defined(_DBUFFER)
    half3 specular = half3(0.0h, 0.0h, 0.0h);
    ApplyDecal(IN.clipPos,
        albedo,
        specular,
        inputData.normalWS,
        metallic,
        occlusion,
        smoothness);
#endif

    InitializeBakedGIData(IN, inputData);

#ifdef TERRAIN_GBUFFER

    BRDFData brdfData;
    InitializeBRDFData(albedo, metallic, /* specular */ half3(0.0h, 0.0h, 0.0h), smoothness, alpha, brdfData);

    // Baked lighting.
    half4 color;
    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);
    color.rgb = FishGlobalIllumination(brdfData, inputData.bakedGI, occlusion, inputData.positionWS,
                                   inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
    color.a = alpha;
    SplatmapFinalColor(color, inputData.fogCoord);

    return PackGBuffersBRDFData(brdfData, inputData, smoothness, color.rgb, occlusion);
#else

    half4 color = FishFragmentPBR(inputData, albedo, metallic, smoothness, occlusion, alpha);

    SplatmapFinalColor(color, inputData.fogCoord);

    outColor = half4(color.rgb, 1.0h);

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
#endif
}

// ── Shadow and depth ────────────────────────────────────────────────────

// Shadow Casting Light geometric parameters, set by URP's ShadowUtils.SetupShadowCasterConstantBuffer.
float3 _LightDirection;
float3 _LightPosition;

struct AttributesLean
{
    float4 position     : POSITION;
    float3 normalOS       : NORMAL;
    float2 texcoord     : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VaryingsLean
{
    float4 clipPos      : SV_POSITION;
    float2 texcoord     : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

VaryingsLean ShadowPassVertex(AttributesLean v)
{
    VaryingsLean o = (VaryingsLean)0;
    UNITY_SETUP_INSTANCE_ID(v);
    TerrainInstancing(v.position, v.normalOS, v.texcoord);
    // Lifted like the lit pass, so the snow's surface casts the shadow it receives.
    FishSnowLift(v.position, v.normalOS);

    float3 positionWS = TransformObjectToWorld(v.position.xyz);
    float3 normalWS = TransformObjectToWorldNormal(v.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 clipPos = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

#if UNITY_REVERSED_Z
    clipPos.z = min(clipPos.z, UNITY_NEAR_CLIP_VALUE);
#else
    clipPos.z = max(clipPos.z, UNITY_NEAR_CLIP_VALUE);
#endif

    o.clipPos = clipPos;
    o.texcoord = v.texcoord;
    return o;
}

half4 ShadowPassFragment(VaryingsLean IN) : SV_TARGET
{
#ifdef _ALPHATEST_ON
    ClipHoles(IN.texcoord);
#endif
    return 0;
}

VaryingsLean DepthOnlyVertex(AttributesLean v)
{
    VaryingsLean o = (VaryingsLean)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(v.position, v.normalOS, v.texcoord);
    // The depth prepass must agree with the lit pass to the bit, or the lifted snow fails the depth test
    // against its own prepass and the ground flickers.
    FishSnowLift(v.position, v.normalOS);
    o.clipPos = TransformObjectToHClip(v.position.xyz);
    o.texcoord = v.texcoord;
    return o;
}

half4 DepthOnlyFragment(VaryingsLean IN) : SV_TARGET
{
#ifdef _ALPHATEST_ON
    ClipHoles(IN.texcoord);
#endif
#ifdef SCENESELECTIONPASS
    // We use depth prepass for scene selection in the editor, this code allow to output the outline correctly
    return half4(_ObjectId, _PassValue, 1.0, 1.0);
#endif
    return IN.clipPos.z;
}

// ── Depth normals ───────────────────────────────────────────────────────

struct VaryingsDepthNormal
{
    float4 uvMainAndLM              : TEXCOORD0; // xy: control
    #if FISH_TERRAIN_TANGENT_FRAME
        half4 normal                   : TEXCOORD3;
        half4 tangent                  : TEXCOORD4;
        half4 bitangent                : TEXCOORD5;
    #else
        half3 normal                   : TEXCOORD3;
    #endif
    float3 positionWS               : TEXCOORD7;
    float4 clipPos                  : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

VaryingsDepthNormal DepthNormalOnlyVertex(Attributes v)
{
    VaryingsDepthNormal o = (VaryingsDepthNormal)0;

    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(v.positionOS, v.normalOS, v.texcoord);
    FishSnowLift(v.positionOS, v.normalOS);

    const VertexPositionInputs attributes = GetVertexPositionInputs(v.positionOS.xyz);

    o.uvMainAndLM.xy = v.texcoord;
    o.uvMainAndLM.zw = 0;

    #if FISH_TERRAIN_TANGENT_FRAME
        float4 vertexTangent = float4(cross(float3(0, 0, 1), v.normalOS), 1.0);
        VertexNormalInputs normalInput = GetVertexNormalInputs(v.normalOS, vertexTangent);
        o.normal = half4(normalInput.normalWS, 0);
        o.tangent = half4(normalInput.tangentWS, 0);
        o.bitangent = half4(normalInput.bitangentWS, 0);
    #else
        o.normal = TransformObjectToWorldNormal(v.normalOS);
    #endif

    o.positionWS = attributes.positionWS;
    o.clipPos = attributes.positionCS;
    return o;
}

void DepthNormalOnlyFragment(
    VaryingsDepthNormal IN
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
    )
{
    #ifdef _ALPHATEST_ON
        ClipHoles(IN.uvMainAndLM.xy);
    #endif

    // The same blend the lit pass uses; only the normal survives, so the compiler drops the albedo reads.
    FishArraySurface ground = FISH_GROUND_SURFACE(IN.uvMainAndLM.xy, IN.positionWS);

    #if FISH_TERRAIN_TANGENT_FRAME
        half3 normalWS = TransformTangentToWorld(ground.normalTS, half3x3(-IN.tangent.xyz, IN.bitangent.xyz, IN.normal.xyz));
    #else
        float2 sampleCoords = (IN.uvMainAndLM.xy / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
        half3 baseNormalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
        half3 tangentWS = cross(GetObjectToWorldMatrix()._13_23_33, baseNormalWS);
        half3 normalWS = TransformTangentToWorld(ground.normalTS, half3x3(-tangentWS, cross(baseNormalWS, tangentWS), baseNormalWS));
    #endif

    outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);

    #ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#endif
