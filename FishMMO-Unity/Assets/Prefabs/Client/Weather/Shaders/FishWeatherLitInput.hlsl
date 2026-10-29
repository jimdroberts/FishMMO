// A copy of URP's LitInput.hlsl, with the FishMMO edits marked below. URP offers no hook for this, so
// the file is copied. On a URP upgrade, diff it against
// Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl and re-apply the marked edits.
#ifndef FISHMMO_WEATHER_LIT_INPUT_INCLUDED
#define FISHMMO_WEATHER_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

#if defined(_DETAIL_MULX2) || defined(_DETAIL_SCALED)
#define _DETAIL
#endif

// NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseMap_TexelSize;
float4 _DetailAlbedoMap_ST;
half4 _BaseColor;
half4 _SpecColor;
half4 _EmissionColor;
half _Cutoff;
half _Smoothness;
half _Metallic;
half _BumpScale;
half _Parallax;
half _OcclusionStrength;
half _ClearCoatMask;
half _ClearCoatSmoothness;
half _DetailAlbedoMapScale;
half _DetailNormalMapScale;
half _Surface;
// ── FishMMO edit: how much weather this surface takes, and how much of it is drawn while the
// day/night cycle fades it in or out. Both live in the material's own buffer, so a material keeps
// working with the SRP batcher; the fade writes _FishVisible per renderer during a fade only.
half _FishWeatherAmount;
half _FishVisible;
// FishMMO edit: 1 on the structure kit's materials: weathered (FishStructureWeathering.hlsl), no ground unfold up the walls.
half _FishStructure;
// FishMMO edit: how much moss this surface grows, 0..1 (FishMoss.hlsl; 0, the default, is none and costs one branch).
// Times the scene's dampness, a global. Set by the art generator on rock, cliff, cave rock and the structure kit.
half _FishMoss;
// FishMMO edit: cave rock (keyword _FISH_CAVE_ROCK, FishCaveRock.hlsl; set by the cave shaper's CaveRockMaterials): the floor
// grounds' tiling (1/m: silt, gravel, litter, worn way), the walls' stain (a its strength) and crust, the glow crust's colour
// (HDR), the guano's, and x moss in the twilight, y wet gloss, z 1 when the grounds' textures are bound. In every variant so the
// buffer's layout never changes (the SRP batcher); read only with the keyword.
float4 _FishCaveTiles;
half4 _FishCaveStain;
half4 _FishCaveCrust;
half4 _FishCaveGlow;
half4 _FishCaveGuano;
half4 _FishCaveParams;
// FishMMO edit (2026-10-25, I2n-c): the tea ring at a cave's water line: x the water's level (world y), y the highest flood over it
// (m), z the top edge's fade (m), w 1 on; and its colour (a strength). w 0 (the default) on every cave rock without one.
float4 _FishCaveFlood;
half4 _FishCaveFloodStain;
// FishMMO edit (2026-10-28): the look arrays (_FISH_LOOK_ARRAY, the block below): this material's slices of its arrays — albedo,
// normal, gloss, occlusion, and emission; −1 an empty slot — and its row in a look family's table (_FISH_LOOK_TABLE: the family
// material's default, overridden per DOTS instance; −1 none). In every variant so the buffer's layout never changes.
float4 _FishLookSlices;
float _FishLookEmissionSlice;
float _FishLook;
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// NOTE: Do not ifdef the properties for dots instancing, but ifdef the actual usage.
// Otherwise you might break CPU-side as property constant-buffer offsets change per variant.
// NOTE: Dots instancing is orthogonal to the constant buffer above.
#ifdef UNITY_DOTS_INSTANCING_ENABLED

// FishMMO edit: URP's UNITY_DOTS_INSTANCED_PROP declares each property's override mode as a static const INSIDE the
// metadata cbuffer. FXC drops it; DXC keeps it as a cbuffer member, which Unity's reflection then reports ("DOTS instancing
// metadata constant "_Smoothness_DOTSInstancingOverrideMode" does not match expected format, ignoring"). So the cbuffer
// holds only the metadata uints, and the modes (the same value URP's macro gives: overrides supported) sit after it.
#define FISH_DOTS_METADATA(type, name) uint UNITY_DOTS_INSTANCED_METADATA_NAME(type, name);
#define FISH_DOTS_OVERRIDE_MODE(name) static const int UNITY_DOTS_INSTANCED_PROP_OVERRIDE_MODE_NAME(name) = kDotsInstancedPropOverrideSupported;
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    FISH_DOTS_METADATA(float4, _BaseColor)
    FISH_DOTS_METADATA(float4, _SpecColor)
    FISH_DOTS_METADATA(float4, _EmissionColor)
    FISH_DOTS_METADATA(float , _Cutoff)
    FISH_DOTS_METADATA(float , _Smoothness)
    FISH_DOTS_METADATA(float , _Metallic)
    FISH_DOTS_METADATA(float , _BumpScale)
    FISH_DOTS_METADATA(float , _Parallax)
    FISH_DOTS_METADATA(float , _OcclusionStrength)
    FISH_DOTS_METADATA(float , _ClearCoatMask)
    FISH_DOTS_METADATA(float , _ClearCoatSmoothness)
    FISH_DOTS_METADATA(float , _DetailAlbedoMapScale)
    FISH_DOTS_METADATA(float , _DetailNormalMapScale)
    FISH_DOTS_METADATA(float , _Surface)
    // FishMMO edit (2026-10-28): a look family's row per instance (_FISH_LOOK_TABLE; FishLookSetup below).
    FISH_DOTS_METADATA(float , _FishLook)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
FISH_DOTS_OVERRIDE_MODE(_BaseColor)
FISH_DOTS_OVERRIDE_MODE(_SpecColor)
FISH_DOTS_OVERRIDE_MODE(_EmissionColor)
FISH_DOTS_OVERRIDE_MODE(_Cutoff)
FISH_DOTS_OVERRIDE_MODE(_Smoothness)
FISH_DOTS_OVERRIDE_MODE(_Metallic)
FISH_DOTS_OVERRIDE_MODE(_BumpScale)
FISH_DOTS_OVERRIDE_MODE(_Parallax)
FISH_DOTS_OVERRIDE_MODE(_OcclusionStrength)
FISH_DOTS_OVERRIDE_MODE(_ClearCoatMask)
FISH_DOTS_OVERRIDE_MODE(_ClearCoatSmoothness)
FISH_DOTS_OVERRIDE_MODE(_DetailAlbedoMapScale)
FISH_DOTS_OVERRIDE_MODE(_DetailNormalMapScale)
FISH_DOTS_OVERRIDE_MODE(_Surface)
FISH_DOTS_OVERRIDE_MODE(_FishLook)

// Here, we want to avoid overriding a property like e.g. _BaseColor with something like this:
// #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor0)
//
// It would be simpler, but it can cause the compiler to regenerate the property loading code for each use of _BaseColor.
//
// To avoid this, the property loads are cached in some static values at the beginning of the shader.
// The properties such as _BaseColor are then overridden so that it expand directly to the static value like this:
// #define _BaseColor unity_DOTS_Sampled_BaseColor
//
// This simple fix happened to improve GPU performances by ~10% on Meta Quest 2 with URP on some scenes.
static float4 unity_DOTS_Sampled_BaseColor;
static float4 unity_DOTS_Sampled_SpecColor;
static float4 unity_DOTS_Sampled_EmissionColor;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_BumpScale;
static float  unity_DOTS_Sampled_Parallax;
static float  unity_DOTS_Sampled_OcclusionStrength;
static float  unity_DOTS_Sampled_ClearCoatMask;
static float  unity_DOTS_Sampled_ClearCoatSmoothness;
static float  unity_DOTS_Sampled_DetailAlbedoMapScale;
static float  unity_DOTS_Sampled_DetailNormalMapScale;
static float  unity_DOTS_Sampled_Surface;

void SetupDOTSLitMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_SpecColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpecColor);
    unity_DOTS_Sampled_EmissionColor        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EmissionColor);
    unity_DOTS_Sampled_Cutoff               = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
    unity_DOTS_Sampled_Smoothness           = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
    unity_DOTS_Sampled_Metallic             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Metallic);
    unity_DOTS_Sampled_BumpScale            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BumpScale);
    unity_DOTS_Sampled_Parallax             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Parallax);
    unity_DOTS_Sampled_OcclusionStrength    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _OcclusionStrength);
    unity_DOTS_Sampled_ClearCoatMask        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ClearCoatMask);
    unity_DOTS_Sampled_ClearCoatSmoothness  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ClearCoatSmoothness);
    unity_DOTS_Sampled_DetailAlbedoMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailAlbedoMapScale);
    unity_DOTS_Sampled_DetailNormalMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailNormalMapScale);
    unity_DOTS_Sampled_Surface              = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Surface);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSLitMaterialPropertyCaches()

#define _BaseColor              unity_DOTS_Sampled_BaseColor
#define _SpecColor              unity_DOTS_Sampled_SpecColor
#define _EmissionColor          unity_DOTS_Sampled_EmissionColor
#define _Cutoff                 unity_DOTS_Sampled_Cutoff
#define _Smoothness             unity_DOTS_Sampled_Smoothness
#define _Metallic               unity_DOTS_Sampled_Metallic
#define _BumpScale              unity_DOTS_Sampled_BumpScale
#define _Parallax               unity_DOTS_Sampled_Parallax
#define _OcclusionStrength      unity_DOTS_Sampled_OcclusionStrength
#define _ClearCoatMask          unity_DOTS_Sampled_ClearCoatMask
#define _ClearCoatSmoothness    unity_DOTS_Sampled_ClearCoatSmoothness
#define _DetailAlbedoMapScale   unity_DOTS_Sampled_DetailAlbedoMapScale
#define _DetailNormalMapScale   unity_DOTS_Sampled_DetailNormalMapScale
#define _Surface                unity_DOTS_Sampled_Surface

#endif

// ── FishMMO edit (2026-10-28): look arrays. The art generator writes each surface family's maps (the structure kit's, the rock
// and ice surfaces', the cave glow's) as three Texture2DArray assets — albedo + emission (sRGB), normals, gloss + occlusion
// (linear) — and its materials name their slices instead of 2D maps (ProceduralArtLookArrays; keyword _FISH_LOOK_ARRAY):
// _FishLookSlices (albedo, normal, gloss, occlusion) and _FishLookEmissionSlice, −1 an empty slot, which reads what URP's
// default texture gives there. Everything else is the material's own, as before. So the props' many materials all reference
// the same few textures, and a GPU-driven family of them draws through ONE material with nothing copied:
// _FISH_LOOK_TABLE (TerrainGpuRenderer.Looks.cs, TerrainLookMath.cs; ForwardLit) reads the member's slices AND scalars from
// the family's table at the row its vertex names (uv3.x, FishWeatherLitForwardPass.hlsl) or, DOTS-instanced, its instance
// (_FishLook, when ≥ 0). FishLookSetup reads the row into statics and the material's own names below then read them, as URP's
// DOTS path does for its per-instance values; _BaseMap's tiling is baked into the family's merged uv0, so it is the identity
// there. Cave rock (its own TEXCOORD3) never takes the table; its empty albedo and normal slots read the material's own 2D maps
// (a cave's inner wall laid over its shell, CaveRockLayers). ──
#if defined(_FISH_LOOK_ARRAY) || defined(_FISH_LOOK_TABLE)
#define FISH_LOOK_ARRAYS 1
TEXTURE2D_ARRAY(_FishLookColour);   SAMPLER(sampler_FishLookColour);
TEXTURE2D_ARRAY(_FishLookNormal);   SAMPLER(sampler_FishLookNormal);
TEXTURE2D_ARRAY(_FishLookMask);     SAMPLER(sampler_FishLookMask);
// The moss and a cave's grounds borrow a sampler bound in every program of the pass: the base map's is not here (no program of
// this variant reads _BaseMap, and a borrowed sampler_<Texture> binds only where its texture is read). The colour array is
// imported as every generated texture is — Repeat, trilinear, aniso 8 — which is what both want.
#define FISH_MOSS_SAMPLER sampler_FishLookColour
#define FISH_CAVE_SAMPLER sampler_FishLookColour

#if defined(_FISH_LOOK_TABLE) && !defined(_FISH_CAVE_ROCK)
#define FISH_LOOK_TABLE 1
TEXTURE2D(_FishLookTable);

static half4 FishLookBaseColor;
static half4 FishLookSpecColor;
static half4 FishLookEmissionColor;
static half FishLookCutoff;
static half FishLookSmoothness;
static half FishLookMetallic;
static half FishLookBumpScale;
static half FishLookOcclusionStrength;
static half FishLookMoss;
static half FishLookStructure;
static half FishLookWeatherAmount;
// Slices: albedo, normal, gloss, occlusion; and emission.
static float4 FishLookSlices;
static float FishLookEmissionSlice;

// The look's row of the table (TerrainLookMath.WriteRow's columns). Called at the top of the vertex and fragment stages, after
// the instance is set up (UNITY_SETUP_INSTANCE_ID: a DOTS instance's own row is read here).
void FishLookSetup(float vertexLook)
{
    float look = vertexLook;
#if defined(UNITY_DOTS_INSTANCING_ENABLED)
    float instanceLook = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _FishLook);
    look = instanceLook >= 0.0 ? instanceLook : vertexLook;
#endif
    int row = (int)(look + 0.5);
    half4 c0 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(0, row));
    half4 c1 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(1, row));
    half4 c2 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(2, row));
    half4 c3 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(3, row));
    half4 c4 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(4, row));
    FishLookBaseColor = c0;
    FishLookSpecColor = c1;
    FishLookEmissionColor = half4(c2.rgb, 1.0h);
    FishLookCutoff = c2.a;
    FishLookSmoothness = c3.x;
    FishLookMetallic = c3.y;
    FishLookBumpScale = c3.z;
    FishLookOcclusionStrength = c3.w;
    FishLookMoss = c4.x;
    FishLookStructure = c4.y;
    FishLookWeatherAmount = c4.z;
    FishLookSlices = LOAD_TEXTURE2D(_FishLookTable, int2(5, row));
    FishLookEmissionSlice = LOAD_TEXTURE2D(_FishLookTable, int2(6, row)).x;
}

// The DOTS path #defines some of these names to its own per-instance caches: the table's take their place.
#undef _BaseMap_ST
#undef _BaseColor
#undef _SpecColor
#undef _EmissionColor
#undef _Cutoff
#undef _Smoothness
#undef _Metallic
#undef _BumpScale
#undef _OcclusionStrength
#define _BaseMap_ST             float4(1.0, 1.0, 0.0, 0.0)
#define _BaseColor              FishLookBaseColor
#define _SpecColor              FishLookSpecColor
#define _EmissionColor          FishLookEmissionColor
#define _Cutoff                 FishLookCutoff
#define _Smoothness             FishLookSmoothness
#define _Metallic               FishLookMetallic
#define _BumpScale              FishLookBumpScale
#define _OcclusionStrength      FishLookOcclusionStrength
#define _FishMoss               FishLookMoss
#define _FishStructure          FishLookStructure
#define _FishWeatherAmount      FishLookWeatherAmount
#define FISH_LOOK_SLICES        FishLookSlices
#define FISH_LOOK_EMISSION_SLICE FishLookEmissionSlice
#else
// The material's own slices (uniform across a draw).
#define FISH_LOOK_SLICES        _FishLookSlices
#define FISH_LOOK_EMISSION_SLICE _FishLookEmissionSlice
#endif

// One slot's texel: its slice of the array, or the default where the slot is empty. Always sampled (slice 0 at worst) and then
// chosen, never branched round: in a family a quad can span two looks, and a sample's derivatives must not diverge.
#define FISH_LOOK_SAMPLE(tex, uv, slice, fallback) ((slice) < 0.0 ? (half4)(fallback) : (half4)SAMPLE_TEXTURE2D_ARRAY(tex, sampler##tex, uv, max(slice, 0.0)))
#endif

TEXTURE2D(_ParallaxMap);        SAMPLER(sampler_ParallaxMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DetailMask);         SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailAlbedoMap);    SAMPLER(sampler_DetailAlbedoMap);
TEXTURE2D(_DetailNormalMap);    SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);
TEXTURE2D(_ClearCoatMap);       SAMPLER(sampler_ClearCoatMap);

#if defined(FISH_LOOK_ARRAYS)
    #define SAMPLE_METALLICSPECULAR(uv) FISH_LOOK_SAMPLE(_FishLookMask, uv, FISH_LOOK_SLICES.z, half4(1.0, 1.0, 1.0, 1.0))
#elif defined(_SPECULAR_SETUP)
    #define SAMPLE_METALLICSPECULAR(uv) SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv)
#else
    #define SAMPLE_METALLICSPECULAR(uv) SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv)
#endif

half4 SampleMetallicSpecGloss(float2 uv, half albedoAlpha)
{
    half4 specGloss;

#ifdef _METALLICSPECGLOSSMAP
    specGloss = half4(SAMPLE_METALLICSPECULAR(uv));
    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a *= _Smoothness;
    #endif
#else // _METALLICSPECGLOSSMAP
    #if _SPECULAR_SETUP
        specGloss.rgb = _SpecColor.rgb;
    #else
        specGloss.rgb = _Metallic.rrr;
    #endif

    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a = _Smoothness;
    #endif
#endif

    return specGloss;
}

half SampleOcclusion(float2 uv)
{
    #if defined(_OCCLUSIONMAP) && defined(FISH_LOOK_ARRAYS)
        half occ = FISH_LOOK_SAMPLE(_FishLookMask, uv, FISH_LOOK_SLICES.w, half4(1.0, 1.0, 1.0, 1.0)).g;
        return LerpWhiteTo(occ, _OcclusionStrength);
    #elif defined(_OCCLUSIONMAP)
        half occ = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
        return LerpWhiteTo(occ, _OcclusionStrength);
    #else
        return half(1.0);
    #endif
}


// Returns clear coat parameters
// .x/.r == mask
// .y/.g == smoothness
half2 SampleClearCoat(float2 uv)
{
#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half2 clearCoatMaskSmoothness = half2(_ClearCoatMask, _ClearCoatSmoothness);

#if defined(_CLEARCOATMAP)
    clearCoatMaskSmoothness *= SAMPLE_TEXTURE2D(_ClearCoatMap, sampler_ClearCoatMap, uv).rg;
#endif

    return clearCoatMaskSmoothness;
#else
    return half2(0.0, 1.0);
#endif  // _CLEARCOAT
}

void ApplyPerPixelDisplacement(half3 viewDirTS, inout float2 uv)
{
#if defined(_PARALLAXMAP)
    uv += ParallaxMapping(TEXTURE2D_ARGS(_ParallaxMap, sampler_ParallaxMap), viewDirTS, _Parallax, uv);
#endif
}

// Used for scaling detail albedo. Main features:
// - Depending if detailAlbedo brightens or darkens, scale magnifies effect.
// - No effect is applied if detailAlbedo is 0.5.
half3 ScaleDetailAlbedo(half3 detailAlbedo, half scale)
{
    // detailAlbedo = detailAlbedo * 2.0h - 1.0h;
    // detailAlbedo *= _DetailAlbedoMapScale;
    // detailAlbedo = detailAlbedo * 0.5h + 0.5h;
    // return detailAlbedo * 2.0f;

    // A bit more optimized
    return half(2.0) * detailAlbedo * scale - scale + half(1.0);
}

half3 ApplyDetailAlbedo(float2 detailUv, half3 albedo, half detailMask)
{
#if defined(_DETAIL)
    half3 detailAlbedo = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, detailUv).rgb;

    // In order to have same performance as builtin, we do scaling only if scale is not 1.0 (Scaled version has 6 additional instructions)
#if defined(_DETAIL_SCALED)
    detailAlbedo = ScaleDetailAlbedo(detailAlbedo, _DetailAlbedoMapScale);
#else
    detailAlbedo = half(2.0) * detailAlbedo;
#endif

    return albedo * LerpWhiteTo(detailAlbedo, detailMask);
#else
    return albedo;
#endif
}

half3 ApplyDetailNormal(float2 detailUv, half3 normalTS, half detailMask)
{
#if defined(_DETAIL)
#if BUMP_SCALE_NOT_SUPPORTED
    half3 detailNormalTS = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUv));
#else
    half3 detailNormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUv), _DetailNormalMapScale);
#endif

    // With UNITY_NO_DXT5nm unpacked vector is not normalized for BlendNormalRNM
    // For visual consistancy we going to do in all cases
    detailNormalTS = normalize(detailNormalTS);

    return lerp(normalTS, BlendNormalRNM(normalTS, detailNormalTS), detailMask); // todo: detailMask should lerp the angle of the quaternion rotation, not the normals
#else
    return normalTS;
#endif
}

#if defined(FISH_LOOK_ARRAYS)
// URP's SampleAlbedoAlpha, SampleNormal and SampleEmission (SurfaceInput.hlsl), from the look's slices. An empty albedo or normal
// slot reads URP's default there — white, flat — or, on cave rock, the material's own 2D map (its inner wall, CaveRockLayers).
half4 FishLookAlbedo(float2 uv)
{
#if defined(_FISH_CAVE_ROCK)
    half4 own = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
#else
    half4 own = half4(1.0, 1.0, 1.0, 1.0);
#endif
    return FISH_LOOK_SAMPLE(_FishLookColour, uv, FISH_LOOK_SLICES.x, own);
}

half3 FishLookNormal(float2 uv, half scale)
{
#ifdef _NORMALMAP
    half4 n = (half4)SAMPLE_TEXTURE2D_ARRAY(_FishLookNormal, sampler_FishLookNormal, uv, max(FISH_LOOK_SLICES.y, 0.0));
    #if defined(_FISH_CAVE_ROCK)
        n = FISH_LOOK_SLICES.y < 0.0 ? (half4)SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv) : n;
    #endif
    #if BUMP_SCALE_NOT_SUPPORTED
        half3 unpacked = UnpackNormal(n);
    #else
        half3 unpacked = UnpackNormalScale(n, scale);
    #endif
    #if defined(_FISH_CAVE_ROCK)
        return unpacked;
    #else
        // No normal map in this slot: the flat normal URP's default "bump" texture unpacks to.
        return FISH_LOOK_SLICES.y < 0.0 ? half3(0.0h, 0.0h, 1.0h) : unpacked;
    #endif
#else
    return half3(0.0h, 0.0h, 1.0h);
#endif
}

half3 FishLookEmission(float2 uv, half3 emissionColor)
{
#ifndef _EMISSION
    return 0;
#else
    return FISH_LOOK_SAMPLE(_FishLookColour, uv, FISH_LOOK_EMISSION_SLICE, half4(1.0, 1.0, 1.0, 1.0)).rgb * emissionColor;
#endif
}

// URP's stock pass files that sample the normal map themselves (LitDepthNormalsPass.hlsl) read it from the look too. Defined
// after SurfaceInput.hlsl's own SampleNormal, which it would otherwise rewrite.
#define SampleNormal(uv, bumpMap, scale) FishLookNormal(uv, scale)
#endif

inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
#if defined(FISH_LOOK_ARRAYS)
    half4 albedoAlpha = FishLookAlbedo(uv);
#else
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
#endif
    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half4 specGloss = SampleMetallicSpecGloss(uv, albedoAlpha.a);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;
    outSurfaceData.albedo = AlphaModulate(outSurfaceData.albedo, outSurfaceData.alpha);

#if _SPECULAR_SETUP
    outSurfaceData.metallic = half(1.0);
    outSurfaceData.specular = specGloss.rgb;
#else
    outSurfaceData.metallic = specGloss.r;
    outSurfaceData.specular = half3(0.0, 0.0, 0.0);
#endif

    outSurfaceData.smoothness = specGloss.a;
#if defined(FISH_LOOK_ARRAYS)
    outSurfaceData.normalTS = FishLookNormal(uv, _BumpScale);
    outSurfaceData.occlusion = SampleOcclusion(uv);
    outSurfaceData.emission = FishLookEmission(uv, _EmissionColor.rgb);
#else
    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    outSurfaceData.occlusion = SampleOcclusion(uv);
    outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));
#endif

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half2 clearCoat = SampleClearCoat(uv);
    outSurfaceData.clearCoatMask       = clearCoat.r;
    outSurfaceData.clearCoatSmoothness = clearCoat.g;
#else
    outSurfaceData.clearCoatMask       = half(0.0);
    outSurfaceData.clearCoatSmoothness = half(0.0);
#endif

#if defined(_DETAIL)
    half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv).a;
    float2 detailUv = uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;
    outSurfaceData.albedo = ApplyDetailAlbedo(detailUv, outSurfaceData.albedo, detailMask);
    outSurfaceData.normalTS = ApplyDetailNormal(detailUv, outSurfaceData.normalTS, detailMask);
#endif
}

#endif // UNIVERSAL_INPUT_SURFACE_PBR_INCLUDED
