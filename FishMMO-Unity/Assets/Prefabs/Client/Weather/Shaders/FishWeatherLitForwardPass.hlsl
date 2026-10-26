// A copy of URP's LitForwardPass.hlsl, with the FishMMO edits marked below. URP offers no hook for this, so
// the file is copied. On a URP upgrade, diff it against
// Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl and re-apply the marked edits.
#ifndef FISHMMO_WEATHER_LIT_FORWARD_PASS_INCLUDED
#define FISHMMO_WEATHER_LIT_FORWARD_PASS_INCLUDED

#include "FishSurface.hlsl"
// ── FishMMO edit: the instanced tree channels' per-instance LOD cross-fade (contract in the file). ──
#include "FishLodFade.hlsl"
#include "FishGroundColour.hlsl"
// ── FishMMO edit: rock-against-rock crevices (the indirect twin's rocks only). ──
#include "FishRockContact.hlsl"
#if defined(FISH_INDIRECT_INSTANCED)
// ── FishMMO edit: the biomes' looks on this world (FishFlora.hlsl, FloraPaletteMap), for the stones the terrain scatters
// (the indirect twin's detail draws only: texel loads, no sampler). ──
#include "FishFlora.hlsl"
#ifndef FISH_FLORA_ON_DECLARED
#define FISH_FLORA_ON_DECLARED
/* Per draw, not per material: 1 on the terrain's detail draws only (DetailScatterRenderer's and TerrainDetailInstancing's
 * clones and per-draw blocks), as _FishContactOn is set. A pebble wears the same Rock_* material as the boulders, the
 * formations and the cliff rocks, which never set it and read 0: they draw as before. */
float _FishFloraOn;
#endif
// The stones' patch size, metres, and their salt (apart from the plants' patches, FishVegetationPasses.hlsl).
#define FISH_LIT_FLORA_PATCH 8.0
#define FISH_LIT_FLORA_SALT 0x9b05688cu
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
// ── FishMMO edit: the terrain's own lighting, for the ground a rock's base and crevices render (FishGroundContactLit.hlsl,
// included below, after the moss it grows round a waterfall). ──
#include "FishGroundLighting.hlsl"
// ── FishMMO edit: what years do to a building (the structure kit's materials, _FishStructure). ──
#include "FishStructureWeathering.hlsl"
// ── FishMMO edit: the fine ground map, for how far underground a surface is (a carved cave's walls). Read with the
// core's inline bilinear clamp sampler (GlobalSamplers.hlsl, through FishGroundColour.hlsl): this pass is near D3D11's
// sixteen, and a texture's own sampler (sampler_FishGroundColour) is refused in a program that does not read that texture. ──
#define FISH_GROUND_MAP_SAMPLER sampler_LinearClamp
#include "FishGroundMap.hlsl"
#if defined(FISH_INDIRECT_INSTANCED)
// ── FishMMO edit: the coat a biome's weather lays over its buildings (2026-10-25, FishStructureCoat.hlsl, FloraPaletteMap's table; after the ground map,
// which gives it the slope a piece stands on). ──
#include "FishStructureCoat.hlsl"
// ── FishMMO edit: how a biome's cold glows move (2026-10-25, I6i; FishGlowModes.hlsl, GlowModeTable's table): off unless a biome here names a mode. ──
#include "FishGlowModes.hlsl"
#endif
// ── FishMMO edit: moss on the surfaces whose material asks for it (_FishMoss; FishMoss.hlsl). Read with the base map's
// own sampler, bound in every program of this pass: no sampler state of its own. ──
#include "FishMoss.hlsl"
#include "FishGroundContactLit.hlsl"
// ── FishMMO edit: the rooms round the camera, darker than the day outside them (FishIndoor). ──
#include "FishIndoor.hlsl"
// ── FishMMO edit: a cave's rock dressed by its habitat (FishCaveRock.hlsl): the plain shader's cave rock materials only (the
// indirect twin never draws cave rock, and its contact and rock-origin interpolators take the same slots). ──
#if defined(_FISH_CAVE_ROCK) && !defined(FISH_INDIRECT_INSTANCED)
#define FISH_CAVE_ROCK_ON
#include "FishCaveRock.hlsl"
#endif
// How much of the sky's ambient a room's inside loses (SkySystem dims the camera's own ambient by a share of it).
#define FISH_INDOOR_DIM 0.65h
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#if defined(_PARALLAXMAP)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) || (defined(_PARALLAXMAP) && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

// keep this file in sync with LitGBufferPass.hlsl

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float2 staticLightmapUV   : TEXCOORD1;
    float2 dynamicLightmapUV  : TEXCOORD2;
#if defined(FISH_CAVE_ROCK_ON)
    // ── FishMMO edit: the cave habitat the mesher wrote (CaveHabitat): wet, daylight, sediment, trodden; organic, glow. ──
    half4 fishHabitat               : COLOR;
    float2 fishHabitat2             : TEXCOORD3;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    float3 positionWS               : TEXCOORD1;
#endif

    float3 normalWS                 : TEXCOORD2;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    half4 tangentWS                : TEXCOORD3;    // xyz: tangent, w: sign
#endif

#if defined(FISH_INDIRECT_INSTANCED)
    // ── FishMMO edit: metres above the terrain straight below, for the contact blend (FishGroundColour.hlsl). ──
    float fishContact               : TEXCOORD4;
    float3 fishRockOrigin           : TEXCOORD11;   // this rock's world position, to tell it from its neighbours
#elif defined(FISH_CAVE_ROCK_ON)
    // ── FishMMO edit: the cave habitat, per vertex (FishCaveRock.hlsl). ──
    half4 fishHabitat               : TEXCOORD4;
    half2 fishHabitat2              : TEXCOORD11;
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight   : TEXCOORD5; // x: fogFactor, yzw: vertex light
#else
    half  fogFactor                 : TEXCOORD5;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord              : TEXCOORD6;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS                : TEXCOORD7;
#endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef DYNAMICLIGHTMAP_ON
    float2  dynamicLightmapUV : TEXCOORD9; // Dynamic lightmap UVs
#endif

#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif

    float4 positionCS               : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    inputData.positionWS = input.positionWS;
#endif

#if defined(DEBUG_DISPLAY)
    inputData.positionCS = input.positionCS;
#endif

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(_NORMALMAP) || defined(_DETAIL)
    float sgn = input.tangentWS.w;      // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

    #if defined(_NORMALMAP)
    inputData.tangentToWorld = tangentToWorld;
    #endif
    inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
#else
    inputData.normalWS = input.normalWS;
#endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif

#if defined(UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION)
    float2 preRotatedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    switch (UNITY_DISPLAY_ORIENTATION_PRETRANSFORM)
    {
    default:
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_0: inputData.normalizedScreenSpaceUV = preRotatedScreenSpaceUV; break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_90: inputData.normalizedScreenSpaceUV = float2(1 - preRotatedScreenSpaceUV.y, preRotatedScreenSpaceUV.x); break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_180: inputData.normalizedScreenSpaceUV = float2(1 - preRotatedScreenSpaceUV.x, 1 - preRotatedScreenSpaceUV.y); break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_270: inputData.normalizedScreenSpaceUV = float2(preRotatedScreenSpaceUV.y, 1 - preRotatedScreenSpaceUV.x); break;
    }
#else
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
#endif

    #if defined(DEBUG_DISPLAY)
    #if defined(DYNAMICLIGHTMAP_ON)
    inputData.dynamicLightmapUV = input.dynamicLightmapUV;
    #endif
    #if defined(LIGHTMAP_ON)
    inputData.staticLightmapUV = input.staticLightmapUV;
    #else
    inputData.vertexSH = input.vertexSH;
    #endif
    #if defined(USE_APV_PROBE_OCCLUSION)
    inputData.probeOcclusion = input.probeOcclusion;
    #endif
    #endif
}

void InitializeBakedGIData(Varyings input, inout InputData inputData)
{
    #if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
    #elif defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        input.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
    #else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #endif
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings LitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    // normalWS and tangentWS already normalize.
    // this is required to avoid skewing the direction during interpolation
    // also required for per-vertex lighting and SH evaluation
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
    #endif

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    // already normalized from normal transform to WS.
    output.normalWS = normalInput.normalWS;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    real sign = input.tangentOS.w * GetOddNegativeScale();
    half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
#endif
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    output.tangentWS = tangentWS;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
    output.viewDirTS = viewDirTS;
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
    output.fogFactor = fogFactor;
#endif

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    output.positionWS = vertexInput.positionWS;
#endif
#if defined(FISH_INDIRECT_INSTANCED)
    output.fishContact = FishContactHeight(vertexInput.positionWS, TransformObjectToWorld(float3(0.0, 0.0, 0.0)));
    output.fishRockOrigin = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
#elif defined(FISH_CAVE_ROCK_ON)
    output.fishHabitat = input.fishHabitat;
    output.fishHabitat2 = (half2)input.fishHabitat2;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif

    output.positionCS = vertexInput.positionCS;

    return output;
}

// Used in Standard (Physically Based) shader
void LitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
#endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif
    // ── FishMMO edit: per-instance LOD fade (_FishLodFade), apart from unity_LODFade above. ──
    FishLodFadeClip(input.positionCS);

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

    // ── FishMMO edit: the weather on this surface, and the day/night dissolve. Here, because this
    // is the first point where the world position and the shaded normal are both known. ──
    FishDitherClip(input.positionCS.xy, _FishVisible);
    // ── FishMMO edit: how deep underground this surface is (a cave's walls, its formations): the sky reaches none of it. ──
    half fishUnderground = FishUnderground(inputData.positionWS);
#if defined(FISH_INDIRECT_INSTANCED)
    // ── FishMMO edit: a terrain rock sits IN the ground. Over a band at its base, and in the crevices where it meets
    // another rock, it renders the TERRAIN — the terrain's own surface there (FishGroundColour.hlsl FishGroundContact)
    // lit by the terrain's own lighting and weather (FishGroundContactLit.hlsl), blended with the rock's lit colour
    // after its lighting below. The crevice also darkens the rock itself toward its bottom (FishRockContact.hlsl). ──
    float3 fishWorldDx = ddx(inputData.positionWS);
    float3 fishWorldDy = ddy(inputData.positionWS);
    half fishGroundWeight;
    FishGroundSite fishSite;
    bool fishHasGround = FishGroundContact(inputData.positionWS, input.fishContact, (half3)input.normalWS, fishGroundWeight, fishSite);
    // ── FishMMO edit: a scattered pebble or stone takes the mineral tint of the patch it lies in (FishFlora.hlsl: a tint
    // about white, the soil's warmth in it, a fantastic look's vein), by its own origin so a stone is one colour. Ahead of
    // the crevices, the moss and the weather, which then lie on the tinted stone as on any other. ──
    UNITY_BRANCH
    if (_FishFloraOn > 0.5 && _FishFloraInfo.x > 0.5)
    {
        FishFloraLook fishFlora = FishFloraAt(input.fishRockOrigin.xz, FISH_LIT_FLORA_PATCH, FISH_LIT_FLORA_SALT);
        surfaceData.albedo *= lerp(half3(1.0h, 1.0h, 1.0h), (half3)fishFlora.mineral, (half)_FishFloraStrength.w);
    }
    half fishSoil, fishShade;
    FishRockCrevice(inputData.positionWS, input.fishRockOrigin, (half3)input.normalWS, fishSoil, fishShade);
    surfaceData.albedo *= fishShade;
    surfaceData.occlusion *= fishShade;
    bool fishStructure = _FishStructure > 0.5h;
    if (fishStructure)
    {
        /* A wall is not a rock: the terrain unfolded half a metre up it read as grass climbing the wall. Only its very
         * foot takes the ground (the plinth set into the earth); above, the weathering's damp and splash. */
        half fishFoot = 1.0h - (half)smoothstep(0.0, 0.18, input.fishContact);
        fishGroundWeight = min(fishGroundWeight, fishFoot * 0.9h);
        fishSoil = 0.0h;
        half3 fishUnweathered = surfaceData.albedo;
        half fishUnweatheredSmooth = surfaceData.smoothness;
        FishStructureWeather(inputData.positionWS, (half3)input.normalWS, input.fishContact, input.fishRockOrigin,
            surfaceData.albedo, surfaceData.smoothness, surfaceData.occlusion);
        // The biome's coat over the weathering (walls, roofs, timber: not panes or glowing parts). Off unless a biome here has one.
        if (_FishStructure < 1.5h)
        {
            FishStructureCoat(inputData.positionWS, (half3)input.normalWS, input.fishContact, input.fishRockOrigin, fishUnweathered,
                fishUnweatheredSmooth, surfaceData.albedo, surfaceData.smoothness, surfaceData.occlusion);
        }
        if (_FishStructure > 2.5h)
        {
            // A glowing part (faerie, rune or crystal light): its emission pulses with its piece's light, from before dusk; or moves by
            // the mode its biome names (FishGlowModes.hlsl), as the light does.
            half3 fishGlowMade = surfaceData.emission;
            surfaceData.emission *= FishStructureGlowLight(input.fishRockOrigin, fishUnderground);
            FishGlowModeWay(fishGlowMade, input.fishRockOrigin, fishUnderground, surfaceData.emission);
        }
        else if (_FishStructure > 1.5h)
        {
            // A pane: lit at night.
            surfaceData.emission += FishStructureGlow(inputData.positionWS, input.fishRockOrigin);
        }
    }
    else if (_FishStructure < -0.5h)
    {
        // A glowing crystal (2026-10-25: -1 flickering, -2 steady). A flickering one (stormglass, 2026-10-18) crackles with the light
        // beside it (FishGlowFlicker); a steady one shines as made; either moves by the mode its biome names (FishGlowModes.hlsl).
        half3 fishGlowMade = surfaceData.emission;
        bool fishFlickers = _FishStructure > -1.5h;
        if (fishFlickers)
        {
            surfaceData.emission *= FishGlowFlicker(input.fishRockOrigin);
        }
        FishGlowModeCrystal(fishGlowMade, input.fishRockOrigin, fishFlickers, surfaceData.emission);
    }
    if (fishSoil > fishGroundWeight)
    {
        if (!fishHasGround)
        {
            half fishSteep = 1.0h - saturate((normalize((half3)input.normalWS).y - 0.45h) / 0.4h);
            fishHasGround = FishGroundSiteAt(inputData.positionWS, fishSteep, fishSite);
        }
        fishGroundWeight = fishSoil;
    }
    // Underground there is no terrain surface at a rock's foot to blend in (a cave's stalagmite stands on the cave's floor,
    // and the terrain's own sky-lit colour there read as daylight at every formation's base).
    fishGroundWeight *= 1.0h - fishUnderground;
    // One sample of the terrain's surface for both.
    FishGroundPixel fishGround = (FishGroundPixel)0;
    if (fishHasGround && fishGroundWeight > 0.0h)
    {
        fishGround = FishGroundSurfaceAtSite(fishSite, inputData.positionWS, fishWorldDx, fishWorldDy);
    }
#endif
#if defined(FISH_CAVE_ROCK_ON)
    // ── FishMMO edit: a cave's rock dressed by its habitat (FishCaveRock.hlsl): its floors' grounds, its walls' wet and stains,
    // the twilight's moss and the glow crust. Ahead of the material's moss and the weather, as a surface of its own. ──
    FishCaveRockSurface(inputData.positionWS, (half3)input.normalWS, input.fishHabitat, input.fishHabitat2,
        surfaceData.albedo, inputData.normalWS, surfaceData.smoothness, surfaceData.metallic, surfaceData.occlusion, surfaceData.emission);
#endif
    // ── FishMMO edit: moss (FishMoss.hlsl), ahead of the weather so rain darkens it and snow covers it like the rest.
    // After the crevices and the structure's weathering, whose shade it reads as where damp collects. A GPU-drawn rock
    // knows its height over the terrain exactly; anything else reads the fine ground map's. ──
#if defined(FISH_INDIRECT_INSTANCED)
    float fishMossAbove = input.fishContact;
#else
    float fishMossAbove = 1e4;
#endif
    FishMossSurface(inputData.positionWS, (half3)input.normalWS, fishMossAbove, fishUnderground,
        surfaceData.albedo, inputData.normalWS, surfaceData.smoothness, surfaceData.metallic, surfaceData.occlusion);
    if (_FishWeatherAmount > 0.0)
    {
        half3 weathered = inputData.normalWS;
        FishWeatherSurface(inputData.positionWS, surfaceData.albedo, weathered, surfaceData.smoothness, surfaceData.metallic, surfaceData.occlusion);
        inputData.normalWS = normalize(lerp(inputData.normalWS, weathered, _FishWeatherAmount));
    }
#if defined(FISH_INDIRECT_INSTANCED)
    // ── FishMMO edit: distant terrain rocks settle into the landscape (FishGroundColour.hlsl). Only the
    // GPU-instanced twin (terrain rocks, boulders, formations, cliff rocks): buildings and props drawn with
    // this shader keep their own colour at any distance (the structure kit's are drawn by this twin too: flagged). ──
    if (!fishStructure)
    {
        float distanceFar, distanceFlatten;
        float3 distanceTarget;
        half4 distancePull;
        FishDistanceBlend(inputData.positionWS, distanceFar, distanceFlatten, distanceTarget, distancePull);
        // Not underground: the far pull is toward the landscape's colour, which a cave has none of.
        distanceFlatten *= 1.0h - fishUnderground;
        distancePull.a *= 1.0h - fishUnderground;
        inputData.normalWS = normalize(lerp(inputData.normalWS, distanceTarget, distanceFlatten));
        surfaceData.albedo = lerp(surfaceData.albedo, distancePull.rgb, distancePull.a);
        surfaceData.smoothness *= (half)(1.0 - distanceFar);
    }
#endif

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    // ── FishMMO edit: deep underground (a cave's walls) the sky does not reach: no ambient, no reflections, no sun (taken
    // off below), and the distance fog's sky colour not laid over them either. ──
    // All of it (2026-10-21; it was 0.96): what the occlusion leaves includes the sky's reflection probe, a full-sky cubemap that
    // RenderSettings.reflectionIntensity does not dim, and four hundredths of it at a grazing Fresnel lit a cave's far floors
    // grey, fading as they were walked up to. The sky's ambient underground is the eye's (SkySystem), not the rock's.
    surfaceData.occlusion *= 1.0h - fishUnderground;
    // Indoors the sky comes in only at the windows and the door: most of its ambient and reflection gone.
    surfaceData.occlusion *= 1.0h - FISH_INDOOR_DIM * FishIndoor(inputData.positionWS);
    // ── FishMMO edit (2026-10-25): under the sea the daylight dims with depth (FishSeaDaylight, 1 elsewhere): the sky's ambient and
    // reflections here, the sun's taken back off below; the surface's own emission (a glowing crystal, a lure's bulb) untouched. ──
    half fishSeaDaylight = FishSeaDaylight(inputData.positionWS);
    surfaceData.occlusion *= fishSeaDaylight;

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    // ── FishMMO edit: and the sun's own light taken back off underground. A cave's deep rock casts no shadow (it is drawn
    // without, for cost) and the terrain's shadow reaches only as far as the shadow distance, so the sun lit stalagmites
    // and walls tens of metres under a hill. Its term evaluated again and subtracted, only where there is any to take. ──
    if (fishUnderground > 0.001h || fishSeaDaylight < 0.999h)
    {
        SurfaceData fishSunSurface = surfaceData;
        BRDFData fishSunBrdf;
        InitializeBRDFData(fishSunSurface, fishSunBrdf);
        Light fishSun = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
        // Not where the sun falls through an opening in the roof (2026-10-20, FishSkylights.hlsl): there its shadowed light is
        // the beam, the terrain's holed shadow and the opening's rock drawing its edge.
        half fishSunTaken = fishUnderground * (half)(1.0 - FishSkylightSun(inputData.positionWS, fishSun.direction));
        // What the sea leaves of the rest (only under it: the expression is the cave's alone everywhere else).
        fishSunTaken = fishSeaDaylight < 1.0h ? 1.0h - (1.0h - fishSunTaken) * fishSeaDaylight : fishSunTaken;
        color.rgb = max(0.0h, color.rgb - fishSunTaken * LightingPhysicallyBased(fishSunBrdf, fishSun, inputData.normalWS, inputData.viewDirectionWS));
    }
#if defined(FISH_INDIRECT_INSTANCED)
    // ── FishMMO edit: the ground's lit colour over the band and in the crevices (above). ──
    if (fishHasGround && fishGroundWeight > 0.0h)
    {
        color.rgb = lerp(color.rgb, FishGroundLit(inputData, fishGround, fishShade, fishWorldDx, fishWorldDy), fishGroundWeight);
    }
#endif
    color.rgb = lerp(MixFog(color.rgb, inputData.fogCoord), color.rgb, fishUnderground);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
