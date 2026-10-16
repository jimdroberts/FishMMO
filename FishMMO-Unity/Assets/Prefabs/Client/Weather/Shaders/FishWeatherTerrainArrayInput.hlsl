// Inputs for FishMMO/Weather Terrain Array: the terrain engine's own inputs (heightmap instancing, holes,
// per-pixel normals — the same declarations FishWeatherTerrainInput.hlsl carries, itself a copy of URP's
// TerrainLitInput.hlsl), plus what the array renderer reads instead of four splat textures.
//
// Unlike the two weather forks this is not a copy with marked edits: the splat model is FishMMO's own, so
// a URP upgrade does not overwrite anything here. What does need re-checking on an upgrade is the terrain
// engine's interface — TerrainInstancing, the holes texture, the heightmap scale — which this file keeps
// exactly as URP's TerrainLitInput.hlsl declares it.
//
// FishMMO edit: weather. The FishMMO edit markers are kept so SurfaceShaderTests-style searches find every
// surface that runs FishWeatherSurface.
#ifndef FISHMMO_WEATHER_TERRAIN_ARRAY_INPUT_INCLUDED
#define FISHMMO_WEATHER_TERRAIN_ARRAY_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

// The most layers one scene carries: what a byte of the index map can name. Matches SceneTerrainPalette.MaximumLayers,
// TerrainArrayLayerParams.MaximumLayers and TerrainLayerTable.Width on the C# side (32, eight control maps, until 2026-10-16).
#define FISH_TERRAIN_ARRAY_MAX_LAYERS 256

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    half4 _BaseColor;
    half _Cutoff;
    // FishMMO edit: how much weather this terrain takes, and how deep the snow lies on it.
    half _FishWeatherAmount;
    half _FishSnowDepth;
    half _HeightTransition;
#ifdef FISH_TERRAIN_BACKDROP
    // FishMMO/Backdrop Ground only: the ground past the scene's edge (FishBackdropSurface below).
    float4 _FishBackdropColour_ST;
    // x: the camera distance (m) the arrays start fading into the colour bake at, y: where they are gone.
    float4 _FishBackdropDetail;
#endif
CBUFFER_END

#define _Surface 0.0 // Terrain is always opaque

// What the terrain engine sets per tile (URP's TerrainLitInput.hlsl names, unchanged).
CBUFFER_START(_Terrain)
#ifdef UNITY_INSTANCING_ENABLED
    float4 _TerrainHeightmapRecipSize;   // float4(1.0f/width, 1.0f/height, 1.0f/(width-1), 1.0f/(height-1))
#endif
    float4 _TerrainHeightmapScale;       // float4(hmScale.x, hmScale.y / (float)(kMaxHeight), hmScale.z, 0.0f)
    #ifdef SCENESELECTIONPASS
    int _ObjectId;
    int _PassValue;
    #endif
CBUFFER_END

// What TerrainArrayBinder pushes per tile through Terrain.SetSplatMaterialPropertyBlock.
//
// The per-layer numbers are not here: they live in the layer table (_FishLayerTable below, TerrainLayerTable.cs), one
// texture loaded by layer. As uniform arrays they were 27 KB at 256 layers, pushed per tile — past the 16 KB uniform block
// OpenGL ES guarantees, and large constant arrays varied per draw are what broke the grass on OpenGL. Their values live
// in the baked TerrainArraySet asset, because they depend on which art (committed or LOCAL) a layer resolved to.
CBUFFER_START(_FishTerrainArray)
    // x: layers in the arrays (0 = nothing baked yet), y: 1 when the tile's index map is bound (the backdrop: its control
    // maps bound), z: 1 when the normal array holds anything, w: 1 when the mask array does.
    float4 _FishArrayInfo;
    // The backdrop's control maps (FishBackdropSurface): 1 / size, size.
    float4 _FishControl0_TexelSize;
    // x: bit k set when control map k is zero over the whole tile: it can neither add a layer nor move one, so it is not
    // sampled. 0 (nothing pushed) reads every map. The backdrop's only, now; it never pushes it.
    float4 _FishControlEmpty;
    // The tile's index map (_FishIndexMap): 1 / size, size, as Unity's _TexelSize.
    float4 _FishIndexMap_TexelSize;
    // FishMMO edit: the slices the scene's paths are surfaced from (x trodden earth, y gravel, z stone; -1 none), set
    // per tile by TerrainArrayBinder from the scene's path surface (each scene's palette puts them elsewhere). Each layer's
    // own biome's are in the layer table (FISH_LAYER_ROW_PATHS); these are what a layer without them paves with.
    float4 _FishPathLayers;
CBUFFER_END

// FishMMO edit: the ground on its world (FishFlora.hlsl, published by FloraPaletteMap): the living ground takes its biome's
// ground shift (what the planet's light and air change; white on an Earth-like world), bare ground on an airless or
// thin-aired world the world's own colour. Never a patch's look: those are the grass's and the plants', and on the ground
// they blotched the layers' own textures (2026-10-15). Texel loads only, no sampler: a pass that never reads the albedo
// (depth, shadow, depth-normals) strips it whole, with nothing left to orphan.
#include "FishFlora.hlsl"

// The index map and the layer table (FishTerrainIndexMap.hlsl): loads only, no sampler.
#include "FishTerrainIndexMap.hlsl"

#ifdef FISH_TERRAIN_BACKDROP
// The backdrop's own control maps (SceneBackdropBuilder): channel c of map k is layer 4k + c. One sampler serves all
// eight. A terrain tile reads its index map instead.
TEXTURE2D(_FishControl0);   SAMPLER(sampler_FishControl0);
TEXTURE2D(_FishControl1);
TEXTURE2D(_FishControl2);
TEXTURE2D(_FishControl3);
TEXTURE2D(_FishControl4);
TEXTURE2D(_FishControl5);
TEXTURE2D(_FishControl6);
TEXTURE2D(_FishControl7);
#else
// The tile's index map: per alphamap texel its four strongest layers (TerrainIndexMap.cs), baked on the GPU from Unity's
// own alphamaps by TerrainArrayBinder (TerrainIndexMapBaker) whenever they change — a brush stroke included, so the paint
// tools keep working with no other glue, and the alphamaps stay the weights everything edits.
Texture2D<uint2> _FishIndexMap;
#endif

// The scene's per-layer numbers, a column per layer (TerrainLayerTable.cs; rows FISH_LAYER_ROW_*).
Texture2D<float4> _FishLayerTable;

float4 FishLayerRow(int id, int row)
{
    return _FishLayerTable.Load(int3(clamp(id, 0, FISH_TERRAIN_ARRAY_MAX_LAYERS - 1), row, 0));
}

// Slice i of every array is terrainData.terrainLayers[i]. One sampler (repeat, trilinear, anisotropic, from
// the albedo array's import settings) serves all three: they share a layout, and samplers are scarcer than
// textures on D3D11 (16 per stage, shadows and probes included).
TEXTURE2D_ARRAY(_FishAlbedoArray);  SAMPLER(sampler_FishAlbedoArray);   // rgb albedo (sRGB), a smoothness
TEXTURE2D_ARRAY(_FishNormalArray);                                      // rgb tangent-space normal, linear
TEXTURE2D_ARRAY(_FishMaskArray);                                        // r metallic, g AO, b height, a smoothness
// The sampler the normal and mask arrays are read with. A pass that defines FISH_TERRAIN_NORMALS_ONLY
// (DepthNormals) reads no albedo, so the compiler strips the albedo array, and its sampler would be left
// matching no texture: Vulkan and D3D reject that ("Unrecognized sampler"); GL, which pairs samplers with
// textures, never complained. That pass takes the normal array's own sampler, still one sampler.
#if defined(FISH_TERRAIN_NORMALS_ONLY)
    SAMPLER(sampler_FishNormalArray);
    #define FISH_ARRAY_SURFACE_SAMPLER sampler_FishNormalArray
#else
    #define FISH_ARRAY_SURFACE_SAMPLER sampler_FishAlbedoArray
#endif

// Used by the meta pass's fallback only.
TEXTURE2D(_MainTex);       SAMPLER(sampler_MainTex);

#if defined(UNITY_INSTANCING_ENABLED) && defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL)
#define ENABLE_TERRAIN_PERPIXEL_NORMAL
#endif

#ifdef UNITY_INSTANCING_ENABLED
TEXTURE2D(_TerrainHeightmapTexture);
TEXTURE2D(_TerrainNormalmapTexture);
SAMPLER(sampler_TerrainNormalmapTexture);
#endif

UNITY_INSTANCING_BUFFER_START(Terrain)
UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)  // float4(xBase, yBase, skipScale, ~)
UNITY_INSTANCING_BUFFER_END(Terrain)

#ifdef _ALPHATEST_ON
TEXTURE2D(_TerrainHolesTexture);
SAMPLER(sampler_TerrainHolesTexture);

float SampleTerrainHolesTexture(float2 uv)
{
    return SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, uv).r;
}

void ClipHoles(float2 uv)
{
    float hole = SampleTerrainHolesTexture(uv);
    // Fixes bug where compression is enabled and 0 isn't actually 0 but low like 1/2047. (UUM-61913)
    float epsilon = 0.0005f;
    clip(hole < epsilon ? -1 : 1);
}
#endif

// URP's terrain instancing, unchanged: the patch's vertices come from the heightmap texture. The backdrop is
// a mesh whose vertices are already where they belong, so it never reads a heightmap, instanced or not.
void TerrainInstancing(inout float4 positionOS, inout float3 normal, inout float2 uv)
{
#if defined(UNITY_INSTANCING_ENABLED) && !defined(FISH_TERRAIN_BACKDROP)
    float2 patchVertex = positionOS.xy;
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);

    float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z; // (xy + float2(xBase,yBase)) * skipScale
    float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

    positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    positionOS.y = height * _TerrainHeightmapScale.y;

#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
    normal = float3(0, 1, 0);
#else
    normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2 - 1;
#endif
    uv = sampleCoords * _TerrainHeightmapRecipSize.zw;
#endif
}

void TerrainInstancing(inout float4 positionOS, inout float3 normal)
{
    float2 uv = { 0, 0 };
    TerrainInstancing(positionOS, normal, uv);
}

void TerrainInstancing(inout float4 positionOS)
{
    float3 normal = { 0, 0, 0 };
    TerrainInstancing(positionOS, normal);
}

// ── The array splat ─────────────────────────────────────────────────────

/// What the array splat makes of one pixel: the blend of its strongest layers.
struct FishArraySurface
{
    half3 albedo;
    half3 normalTS;
    half metallic;
    half smoothness;
    half occlusion;
    // The share of the ground the control maps account for: 1 on a terrain tile, whose weights always sum to
    // one; less on the backdrop wherever a biome past the edge paints with art the scene's arrays lack.
    half covered;
};

// Keeps the four strongest weights seen so far, sorted, and the fifth strongest.
//
// Branch-free on purpose: every candidate takes the same path, so a pixel at a five-biome junction costs
// what a pixel in the middle of a meadow does, and the compiler turns the conditionals into selects.
void FishConsiderLayer(half weight, int index, inout half4 top, inout int4 ids, inout half fifth)
{
    bool b0 = weight > top.x;
    bool b1 = weight > top.y;
    bool b2 = weight > top.z;
    bool b3 = weight > top.w;
    // A weight that makes the four pushes the old fourth down to fifth; one that does not may still be fifth.
    fifth = b3 ? top.w : max(fifth, weight);
    half4 t = top;
    int4 i = ids;
    top.w = b2 ? t.z : (b3 ? weight : t.w);   ids.w = b2 ? i.z : (b3 ? index : i.w);
    top.z = b1 ? t.y : (b2 ? weight : t.z);   ids.z = b1 ? i.y : (b2 ? index : i.z);
    top.y = b0 ? t.x : (b1 ? weight : t.y);   ids.y = b0 ? i.x : (b1 ? index : i.y);
    top.x = b0 ? weight : t.x;                ids.x = b0 ? index : i.x;
}

#ifdef FISH_TERRAIN_BACKDROP
// The backdrop reads its control maps inside a branch (FishBackdropSurface), where an implicit-gradient
// sample is not allowed; level 0 is also the right level there, since a 26 m control texel is magnified
// everywhere the arrays are drawn.
#define FISH_SAMPLE_CONTROL(k) SAMPLE_TEXTURE2D_LOD(_FishControl##k, sampler_FishControl0, controlUV, 0)

// A map that is zero everywhere on the tile is skipped, exactly: its four zeros add nothing to covered, and a weight of
// zero never displaces one of the four (FishConsiderLayer's tests are strict) nor raises the fifth (which is never below 0).
#define FISH_CONSIDER_CONTROL(k) \
    if ((k) < controlCount && ((emptyControls >> (k)) & 1u) == 0u) \
    { \
        half4 c = FISH_SAMPLE_CONTROL(k); \
        covered += dot(c, 1.0h); \
        FishConsiderLayer(c.r, (k) * 4 + 0, top, ids, fifth); \
        FishConsiderLayer(c.g, (k) * 4 + 1, top, ids, fifth); \
        FishConsiderLayer(c.b, (k) * 4 + 2, top, ids, fifth); \
        FishConsiderLayer(c.a, (k) * 4 + 3, top, ids, fifth); \
    }

/// <summary>
/// Picks this pixel's four strongest layers from every control map and returns their weights, normalised.
/// </summary>
/// <remarks>
/// Four because that is what TerrainLit blends per pass and because painted or generated ground almost
/// never has a fifth layer worth seeing: a junction of two biomes is two mains, a cliff and perhaps a detail.
/// Twelve array samples (four layers × albedo, normal, mask) is also what TerrainLit's own first pass costs.
///
/// The fifth-strongest weight is subtracted from the four before they are normalised. Without that, the
/// moment a fifth layer overtakes the fourth the blend jumps by the whole of the fourth's weight — a visible
/// seam wherever five layers meet. With it, a layer enters and leaves the four at zero contribution, so the
/// blend is continuous everywhere. Ground that only ever has four layers is untouched (the fifth is zero).
///
/// An unpainted texel (every weight zero) draws layer 0 rather than black.
///
/// <paramref name="covered"/> is the sum of every weight before the normalising: what share of the ground
/// the control maps account for at all.
/// </remarks>
half4 FishTopLayers(float2 uv, out int4 ids, out half covered)
{
    // The backdrop's control maps are baked at texel centres over its own 0..1 coordinate (SceneBackdropBuilder).
    float2 controlUV = uv;
    int controlCount = (int)_FishArrayInfo.y;
    uint emptyControls = (uint)_FishControlEmpty.x;

    half4 top = 0;
    half fifth = 0;
    ids = 0;
    covered = 0;
    FISH_CONSIDER_CONTROL(0)
    FISH_CONSIDER_CONTROL(1)
    FISH_CONSIDER_CONTROL(2)
    FISH_CONSIDER_CONTROL(3)
    FISH_CONSIDER_CONTROL(4)
    FISH_CONSIDER_CONTROL(5)
    FISH_CONSIDER_CONTROL(6)
    FISH_CONSIDER_CONTROL(7)

    // Channels are the scene's layers through the table: itself for 32 layers or fewer, else the 32 the backdrop's bake gave
    // its channels (SceneBackdrop.ControlLayers).
    ids = int4(FishLayerRow(ids.x, FISH_LAYER_ROW_BACKDROP).x, FishLayerRow(ids.y, FISH_LAYER_ROW_BACKDROP).x,
        FishLayerRow(ids.z, FISH_LAYER_ROW_BACKDROP).x, FishLayerRow(ids.w, FISH_LAYER_ROW_BACKDROP).x);
    half4 weights = max(top - fifth, 0);
    half total = dot(weights, 1.0h);
    if (total <= 1e-4h)
    {
        ids = 0;
        return half4(1, 0, 0, 0);
    }
    return weights / total;
}
#else
/// <summary>
/// A tile pixel's four strongest layers, from its index map: the four texels a bilinear read of the control maps took,
/// merged by layer (FishIndexFilter) — the control-map rule above, at four texel reads a pixel however many layers the
/// scene has. The index map is cut where TerrainLit addresses _Control (texel 0 on the tile's edge).
/// </summary>
/// <remarks>
/// <paramref name="covered"/> is 1: a tile's alphamaps always account for all of its ground (Unity keeps them summing to
/// one), and only the backdrop reads it. No index map bound (_FishArrayInfo.y 0) is unpainted ground: layer 0, as a
/// tile with no control maps drew.
/// </remarks>
half4 FishTopLayers(float2 uv, out int4 ids, out half covered)
{
    covered = 1.0h;
    ids = 0;
    if (_FishArrayInfo.y < 0.5)
    {
        return half4(1, 0, 0, 0);
    }
    int2 i0, i1;
    float2 f;
    FishIndexFootprint(uv, _FishIndexMap_TexelSize.zw, i0, i1, f);
    uint2 t00 = _FishIndexMap.Load(int3(i0.x, i0.y, 0));
    uint2 t10 = _FishIndexMap.Load(int3(i1.x, i0.y, 0));
    uint2 t01 = _FishIndexMap.Load(int3(i0.x, i1.y, 0));
    uint2 t11 = _FishIndexMap.Load(int3(i1.x, i1.y, 0));
    return (half4)FishIndexFilter(t00, t10, t01, t11, f, ids);
}
#endif

#ifdef _TERRAIN_BLEND_HEIGHT
// URP's HeightBasedSplatModify, on the four chosen layers: heights from the mask's blue channel, the highest
// layer wins within _HeightTransition of the rest.
void FishHeightBlend(inout half4 weights, half4 heights)
{
    half4 splatHeight = heights * weights;
    half maxHeight = max(splatHeight.r, max(splatHeight.g, max(splatHeight.b, splatHeight.a)));
    half transition = max(_HeightTransition, 1e-5);
    half4 weightedHeights = max(0, splatHeight + transition - maxHeight.xxxx);
    weightedHeights = (weightedHeights + 1e-6) * weights;
    half sumHeight = max(dot(weightedHeights, half4(1, 1, 1, 1)), 1e-6);
    weights = weightedHeights / sumHeight.xxxx;
}
#endif

#if !defined(FISH_TERRAIN_BACKDROP)
// FishMMO edit: the scene's paths over the ground (FishGroundPaths.hlsl; baked by the cut, ScenePathField). Read with its
// own inline bilinear clamp sampler: the control maps' sampler it borrowed went with them (the index map is loaded).
#include "FishGroundPaths.hlsl"

/// <summary>
/// One path surface's art from the arrays: albedo (tinted) and smoothness, tangent normal, occlusion. blur widens the
/// sampling footprint (4 = two mips down): trodden ground has lost its fine detail, the litter and crumbs trampled flat.
/// groundShift is the ground's (FishTerrainGroundShift): a path layer is on the world the ground it crosses is (a trodden
/// way on a rust-red airless world is rust-red too), or its edge would show the old colour.
/// </summary>
void FishPathLayer(int id, float2 world, float2 worldDx, float2 worldDy, float blur, float3 groundShift, out half3 albedo, out half3 normal, out half smoothness, out half occlusion)
{
    float4 st = FishLayerRow(id, FISH_LAYER_ROW_ST);
    float4 tint = FishLayerRow(id, FISH_LAYER_ROW_TINT);
    float4 surface = FishLayerRow(id, FISH_LAYER_ROW_SURFACE);
    float2 layerUV = world * st.xy + st.zw;
    float2 dx = worldDx * st.xy * blur;
    float2 dy = worldDy * st.xy * blur;
    albedo = half3(0.45h, 0.38h, 0.3h);
    smoothness = 0.1h;
#ifndef FISH_TERRAIN_NORMALS_ONLY
    half4 a = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishAlbedoArray, sampler_FishAlbedoArray, layerUV, id, dx, dy);
    albedo = (half3)FishFloraGround(a.rgb * (half3)tint.rgb, FishLayerRow(id, FISH_LAYER_ROW_FLORA).xyz, groundShift);
    smoothness = a.a * (half)surface.y;
#endif
    normal = half3(0.0h, 0.0h, 1.0h);
    occlusion = 1.0h;
    if (_FishArrayInfo.z > 0.5)
    {
        half4 packed = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishNormalArray, FISH_ARRAY_SURFACE_SAMPLER, layerUV, id, dx, dy);
        half3 n;
        n.xy = (packed.rg * 2.0h - 1.0h) * (half)tint.a;
        n.z = sqrt(max(1.0e-3h, 1.0h - saturate(dot(n.xy, n.xy))));
        normal = n;
    }
    if (_FishArrayInfo.w > 0.5 && surface.z > 0.5)
    {
        half4 m = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishMaskArray, FISH_ARRAY_SURFACE_SAMPLER, layerUV, id, dx, dy);
        m = m * (half4)FishLayerRow(id, FISH_LAYER_ROW_MASK_SCALE) + (half4)FishLayerRow(id, FISH_LAYER_ROW_MASK_OFFSET);
        smoothness = m.a;
        occlusion = m.g;
    }
}

/// <summary>
/// Lays the scene's paths over the ground: a crisp, frayed edge from the half-metre distance field; trodden earth taking
/// some of the ground's own colour, so a desert trail is sandy and a forest one dark; a cart track's two ruts with grass
/// between; packed gravel; cobbles. A lost trail is broken by patches of the ground it crosses. Far off, a way narrower
/// than a pixel fades by the share of the pixel it covers rather than shimmering.
/// </summary>
void FishApplyPaths(inout FishArraySurface s, float3 positionWS, float2 worldDx, float2 worldDy, int groundId, float3 groundShift)
{
    FishPathSample p = FishPathAt(positionWS.xz);
    // Past the furthest the frayed, broken edge reaches (FishPathFrayedEdge, FishPathEdgeBand).
    UNITY_BRANCH
    if (!p.found || p.edge > 2.0)
    {
        return;
    }
    // The path, road and cobble grounds of the biome this pixel's ground belongs to (its layer's column of the table,
    // SceneTerrainPalette.PathLayerMap), else the scene's. A scene cut before the cobbles had a map holds -1 there and
    // paves with _FishPathLayers.z, as it always did.
    float4 pathSlices = FishLayerRow(groundId, FISH_LAYER_ROW_PATHS);
    float earthSlice = pathSlices.x;
    float roadSlice = pathSlices.y;
    float stoneSlice = pathSlices.z;
    earthSlice = earthSlice > -0.5 ? earthSlice : _FishPathLayers.x;
    roadSlice = roadSlice > -0.5 ? roadSlice : (_FishPathLayers.y > -0.5 ? _FishPathLayers.y : earthSlice);
    stoneSlice = stoneSlice > -0.5 ? stoneSlice : (_FishPathLayers.z > -0.5 ? _FishPathLayers.z : roadSlice);
    // No path slices bound, or slices past what the scene's arrays hold (arrays older than the palette): leave the ground.
    bool surfaced = earthSlice > -0.5 && max(earthSlice, max(roadSlice, stoneSlice)) < _FishArrayInfo.x;
    if (_FishPathDebug > 0.5)
    {
        s.albedo = lerp(s.albedo, surfaced ? half3(1.0h, 0.0h, 1.0h) : half3(0.0h, 1.0h, 1.0h), (half)(1.0 - smoothstep(-0.05, 0.05, p.edge)));
        return;
    }
    if (!surfaced)
    {
        return;
    }
    float2 xz = positionWS.xz;
    float pixel = max(1e-3, max(length(worldDx), length(worldDy)));
    float wear = saturate(p.wear);
    float fromCentre = max(0.0, p.edge + p.halfWidth);

    // The bare share, as the grass and the details are cleared by (FishPathBare), so neither grows on what is drawn here.
    float cover = FishPathBare(p, xz, max(pixel * 1.5, lerp(0.3, 0.06, wear)));
    // A way under a pixel wide covers that share of it.
    cover *= saturate(2.0 * max(p.halfWidth, 0.25) / pixel);
    if (cover <= 0.004)
    {
        return;
    }

    float code = p.surface * 3.0;
    float track = FishPathTrackness(p.surface);
    float earthShare = saturate(1.0 - code) + track;
    float gravelShare = saturate(1.0 - abs(code - 2.0));
    float stoneShare = saturate(code - 2.0);
    int earthId = (int)(earthSlice + 0.5);
    int gravelId = (int)(roadSlice + 0.5);
    int stoneId = (int)(stoneSlice + 0.5);

    half3 albedo = 0;
    half3 normal = 0;
    half smoothness = 0;
    half occlusion = 0;
    half3 a; half3 n; half sm; half oc;
    /* The path grounds are made for this (SurfaceCatalogue: PathLoam, PathSand, PathSnow …): drawn as authored, with a
     * touch of the ground's own colour so the edge sits in it. Only where a biome's override makes its path the very
     * ground it crosses would the way vanish; there feet work grit up through it. */
    float grit = groundId == earthId ? 0.4 : 0.0;
    gravelShare += earthShare * grit * (1.0 - track);
    earthShare *= 1.0 - grit * (1.0 - track);
    UNITY_BRANCH
    if (earthShare > 0.0)
    {
        FishPathLayer(earthId, xz, worldDx, worldDy, 1.0, groundShift, a, n, sm, oc);
        a = lerp(a, s.albedo, 0.12h);
        albedo += a * earthShare; normal += n * earthShare; smoothness += sm * earthShare; occlusion += oc * earthShare;
    }
    UNITY_BRANCH
    if (gravelShare > 0.0)
    {
        FishPathLayer(gravelId, xz, worldDx, worldDy, 1.0, groundShift, a, n, sm, oc);
        albedo += a * gravelShare; normal += n * gravelShare; smoothness += sm * gravelShare; occlusion += oc * gravelShare;
    }
    UNITY_BRANCH
    if (stoneShare > 0.0)
    {
        FishPathLayer(stoneId, xz, worldDx, worldDy, 1.0, groundShift, a, n, sm, oc);
        albedo += a * stoneShare; normal += n * stoneShare; smoothness += sm * stoneShare; occlusion += oc * stoneShare;
    }
    half total = max(1e-3h, (half)(earthShare + gravelShare + stoneShare));
    albedo /= total; normal /= total; smoothness /= total; occlusion /= total;

    // A cart track's ruts: two lanes a gauge apart, darker, packed glossy, shadowed in their bottoms.
    // Fainter where the track is less used, so they melt away with it where it peters out (ScenePathField.EndFade).
    float rut = track * (1.0 - smoothstep(0.1, 0.23, abs(fromCentre - 0.72))) * saturate(wear * 1.6);
    albedo *= (half)(1.0 - 0.22 * rut);
    smoothness = lerp(smoothness, max(smoothness, 0.35h), (half)rut);
    occlusion *= (half)(1.0 - 0.25 * rut);
    // A worn verge: the last of the path, darker where feet crowd the edge.
    float verge = smoothstep(-0.6, 0.0, p.edge) * (1.0 - smoothstep(0.0, 0.4, p.edge));
    albedo *= (half)(1.0 - 0.08 * verge * wear);
    /* Toward its edge the way takes on the ground it crosses, in patches: dust and leaves blown over it, soil trodden up
     * from the verge. Its own colour edge to edge read as a band laid on the ground at full strength (2026-10-13). */
    float band = FishPathEdgeBand(p);
    float nearEdge = smoothstep(-2.5 * band, 0.0, p.edge);
    float blown = 0.35 + 0.65 * FishPathNoise(xz * 0.8 + 57.0);
    albedo = lerp(albedo, s.albedo, (half)(0.35 * nearEdge * blown));

    half c = (half)saturate(cover);
    s.albedo = lerp(s.albedo, albedo, c);
    s.normalTS = normalize(lerp(s.normalTS, normalize(normal + half3(0.0h, 0.0h, 1e-4h)), c));
    s.smoothness = lerp(s.smoothness, saturate(smoothness), c);
    s.occlusion = lerp(s.occlusion, saturate(occlusion), c);
    s.metallic = lerp(s.metallic, 0.0h, c);
}
#endif

/// <summary>
/// One layer slice sampled on one plane: its albedo, mask and tangent-space normal (decoded from plain RGB, as the
/// baker writes it on every platform, rather than through UnpackNormalScale, whose layout follows the platform's
/// normal-map encoding).
/// </summary>
void FishSampleLayerPlane(int id, float2 layerUV, float2 dx, float2 dy, bool hasMasks, bool hasNormals, half hasMask, half normalScale,
    out half4 albedo, out half4 mask, out half3 normal)
{
    albedo = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishAlbedoArray, sampler_FishAlbedoArray, layerUV, id, dx, dy);
    mask = 0.5h;
    normal = half3(0.0h, 0.0h, 1.0h);
    if (hasMasks)
    {
        mask = lerp(0.5h, SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishMaskArray, FISH_ARRAY_SURFACE_SAMPLER, layerUV, id, dx, dy), hasMask);
    }
    if (hasNormals)
    {
        half4 packed = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishNormalArray, FISH_ARRAY_SURFACE_SAMPLER, layerUV, id, dx, dy);
        half3 n;
        n.xy = (packed.rg * 2.0h - 1.0h) * normalScale;
        n.z = sqrt(max(1.0e-3h, 1.0h - saturate(dot(n.xy, n.xy))));
        normal = n;
    }
}

/// <summary>
/// The shift the pixel's living ground takes on its world (FishFlora.hlsl FishFloraGroundShiftAt): white, and never looked
/// up, while the looks are not published or where none of the pixel's layers is living ground. One lookup a pixel, however
/// many of its layers take it. <paramref name="living"/>: each of the four layers' living share (FISH_LAYER_ROW_FLORA x).
/// </summary>
float3 FishTerrainGroundShift(float2 xz, float4 living, half4 weights)
{
    float3 shift = 1.0;
#if !defined(FISH_TERRAIN_NORMALS_ONLY)
    UNITY_BRANCH
    if (_FishFloraInfo.x > 0.5)
    {
        float anyLiving = 0.0;
        UNITY_UNROLL
        for (int k = 0; k < 4; k++)
        {
            anyLiving += weights[k] > 0.0h ? living[k] : 0.0;
        }
        UNITY_BRANCH
        if (anyLiving > 0.0)
        {
            shift = FishFloraGroundShiftAt(xz);
        }
    }
#endif
    return shift;
}

/// <summary>Ground steeper than this (the normal's height under it) starts to take its layers from the side; fully by the second.</summary>
#define FISH_SIDE_FROM 0.766h
#define FISH_SIDE_FULL 0.5h

/// <summary>
/// The ground at one pixel: its four strongest layers sampled from the arrays and blended.
/// </summary>
/// <param name="uv">The tile's 0..1 coordinate, for the control maps.</param>
/// <param name="positionWS">World position, for the layers' tiling.</param>
/// <param name="dPdx">The world position's screen derivatives, for the layers' gradients.</param>
/// <param name="groundNormalWS">The ground's own normal (the terrain's, without the layers' detail): how steep it is, and which way it faces.</param>
/// <remarks>
/// <para>
/// <b>World-space tiling.</b> Each layer tiles by world x/z rather than by the tile's own 0..1 coordinate,
/// so a texture runs straight across the seam between two tiles whatever its tile size; TerrainLit's per-tile
/// coordinate restarts at every seam and shows one unless the tile size divides the tile exactly.
/// </para>
/// <para>
/// <b>Explicit gradients.</b> A pixel's four layers are chosen per pixel, so two neighbours can sample slot
/// k from layers with different tile sizes; implicit derivatives would then see a jump in the coordinate and
/// fall to the smallest mip — sparkling seams along every transition. The gradient is taken from the world
/// position once and scaled by each layer's own tiling. It also makes it legal to skip a layer whose weight
/// is zero, which is most layers in most places.
/// </para>
/// <para>
/// <b>Steep ground from the side.</b> Every layer is laid on the world's xz, so on a fall's wall or a gorge, 60–85°, one
/// texel ran metres down the face: rock smeared into vertical streaks (2026-10-13: "the texturing of the waterfall walls,
/// it's stretched"). From about 40° the layers are also laid on the planes facing across the slope (zy and xy), blended
/// by the ground's normal, so a face reads at the texture's own scale. Their normal maps are turned into the terrain's own
/// tangent frame (tangent = (0, 0, 1) × normal, the frame the passes build), so lighting on the face matches. Under 40°
/// nothing changes: the one top-down sample, as before.
/// </para>
/// </remarks>
FishArraySurface FishSampleArraySurfaceSloped(float2 uv, float3 positionWS, float3 dPdx, float3 dPdy, half3 groundNormalWS)
{
    float2 worldDx = dPdx.xz;
    float2 worldDy = dPdy.xz;

    FishArraySurface s;
    s.albedo = half3(0.5h, 0.5h, 0.5h);
    s.normalTS = half3(0.0h, 0.0h, 1.0h);
    s.metallic = 0.0h;
    s.smoothness = 0.0h;
    s.occlusion = 1.0h;
    s.covered = 0.0h;

    // Nothing baked yet (a fresh clone before its first bake, or a build without arrays): plain grey ground
    // that still takes light and weather, rather than garbage from unbound slices.
    if (_FishArrayInfo.x < 0.5)
    {
        return s;
    }

    int4 ids;
    half covered;
    half4 weights = FishTopLayers(uv, ids, covered);

    float2 world = positionWS.xz;
    bool hasNormals = _FishArrayInfo.z > 0.5;
    bool hasMasks = _FishArrayInfo.w > 0.5;

    /* How much the ground is taken from the side, and from which: the planes facing x and z, by the share of each in the
     * triangle's own facing. A cliff across the heightmap's grid is a staircase of triangles facing x and z by turns;
     * chosen by the terrain's smoothed normal (their average), a plane ran edge-on across every other step, banded. The
     * facet's normal from the position's derivatives is the triangle's, so each step takes the plane it faces. */
    half3 n = normalize(groundNormalWS);
    float3 facet = cross(dPdy, dPdx);
    float facetLength = length(facet);
    half3 facing = facetLength > 1e-8 ? (half3)(facet / facetLength) : n;
    facing = dot(facing, n) < 0.0h ? -facing : facing;
    half side = 1.0h - smoothstep(FISH_SIDE_FULL, FISH_SIDE_FROM, facing.y);
    /* Sharp between the two: blended evenly, a face running diagonally took both planes, each stretched 1.4 times across
     * it, and the two laid over each other read as banding. Now one plane holds a face until it turns past 45°, and the
     * change comes over a few degrees either side. */
    half2 across = abs(facing.xz) + 1e-4h;
    across = saturate((across / (across.x + across.y) - 0.5h) * 6.0h + 0.5h);
    half facingX = side * across.x, facingZ = side * across.y;
    half top = 1.0h - facingX - facingZ;
    // The terrain's tangent frame at this normal, as the passes build it: TS x along -tangent, y along the bitangent.
    float3 tangentWS = normalize(cross(float3(0.0, 0.0, 1.0), (float3)n) + float3(1e-5, 0.0, 0.0));
    float3 bitangentWS = cross((float3)n, tangentWS);
    // Each side plane's texture axes in the world, laid so a face is never mirrored seen from outside it.
    float signX = facing.x >= 0.0h ? 1.0 : -1.0;
    float signZ = facing.z >= 0.0h ? 1.0 : -1.0;
    float2 planeX = float2(-signX * positionWS.z, positionWS.y), planeXDx = float2(-signX * dPdx.z, dPdx.y), planeXDy = float2(-signX * dPdy.z, dPdy.y);
    float2 planeZ = float2(signZ * positionWS.x, positionWS.y), planeZDx = float2(signZ * dPdx.x, dPdx.y), planeZDy = float2(signZ * dPdy.x, dPdy.y);
    float3 axisXU = float3(0.0, 0.0, -signX), axisZU = float3(signZ, 0.0, 0.0), axisV = float3(0.0, 1.0, 0.0);

    half4 albedo[4];
    half4 masks[4];
    half3 normals[4];
    half4 hasMask = 0;
    /* Each layer's numbers from the table (TerrainLayerTable), loaded once and only for a layer that is drawn: one of
     * weight zero adds nothing to the blend whatever its numbers (the height blend weighs it by zero too), so it keeps
     * neutral ones and costs no loads. */
    float4 layerTint[4];
    float4 layerSurface[4];
    float4 layerMaskOffset[4];
    float4 layerMaskScale[4];
    float4 layerFlora[4];

    UNITY_UNROLL
    for (int k = 0; k < 4; k++)
    {
        int id = ids[k];
        albedo[k] = half4(0.5h, 0.5h, 0.5h, 1.0h);
        masks[k] = 0.5h;
        normals[k] = half3(0.0h, 0.0h, 1.0h);
        layerTint[k] = 1.0;
        layerSurface[k] = 0.0;
        layerMaskOffset[k] = 0.0;
        layerMaskScale[k] = 1.0;
        layerFlora[k] = 0.0;

        UNITY_BRANCH
        if (weights[k] > 0.0h)
        {
            float4 st = FishLayerRow(id, FISH_LAYER_ROW_ST);
            layerTint[k] = FishLayerRow(id, FISH_LAYER_ROW_TINT);
            layerSurface[k] = FishLayerRow(id, FISH_LAYER_ROW_SURFACE);
            layerMaskOffset[k] = FishLayerRow(id, FISH_LAYER_ROW_MASK_OFFSET);
            layerMaskScale[k] = FishLayerRow(id, FISH_LAYER_ROW_MASK_SCALE);
            layerFlora[k] = FishLayerRow(id, FISH_LAYER_ROW_FLORA);
            hasMask[k] = (half)layerSurface[k].z;
            half normalScale = (half)layerTint[k].a;
            float2 layerUV = world * st.xy + st.zw;
            float2 dx = worldDx * st.xy;
            float2 dy = worldDy * st.xy;
            half4 a = 0, m = 0;
            half3 detail = 0;
            half4 planeAlbedo, planeMask;
            half3 planeNormal;
            UNITY_BRANCH
            if (top > 0.0h)
            {
                FishSampleLayerPlane(id, layerUV, dx, dy, hasMasks, hasNormals, hasMask[k], normalScale, planeAlbedo, planeMask, planeNormal);
                a += planeAlbedo * top;
                m += planeMask * top;
                detail += planeNormal * top;
            }
            // A side plane's normal: its detail along the plane's axes in the world, then into the terrain's frame.
            UNITY_BRANCH
            if (facingX > 0.0h)
            {
                FishSampleLayerPlane(id, planeX * st.xy + st.zw, planeXDx * st.xy, planeXDy * st.xy, hasMasks, hasNormals, hasMask[k], normalScale, planeAlbedo, planeMask, planeNormal);
                float3 perturb = axisXU * planeNormal.x + axisV * planeNormal.y;
                a += planeAlbedo * facingX;
                m += planeMask * facingX;
                detail += half3(dot(perturb, -tangentWS), dot(perturb, bitangentWS), planeNormal.z) * facingX;
            }
            UNITY_BRANCH
            if (facingZ > 0.0h)
            {
                FishSampleLayerPlane(id, planeZ * st.xy + st.zw, planeZDx * st.xy, planeZDy * st.xy, hasMasks, hasNormals, hasMask[k], normalScale, planeAlbedo, planeMask, planeNormal);
                float3 perturb = axisZU * planeNormal.x + axisV * planeNormal.y;
                a += planeAlbedo * facingZ;
                m += planeMask * facingZ;
                detail += half3(dot(perturb, -tangentWS), dot(perturb, bitangentWS), planeNormal.z) * facingZ;
            }
            albedo[k] = a;
            masks[k] = m;
            normals[k] = normalize(detail + half3(0.0h, 0.0h, 1e-4h));
        }
        masks[k] = masks[k] * (half4)layerMaskScale[k] + (half4)layerMaskOffset[k];
    }

#ifdef _TERRAIN_BLEND_HEIGHT
    FishHeightBlend(weights, half4(masks[0].b, masks[1].b, masks[2].b, masks[3].b));
#endif

    half3 mixedAlbedo = 0;
    half3 mixedNormal = 0;
    half metallic = 0;
    half smoothness = 0;
    half occlusion = 0;

    /* FishMMO edit: each layer on its planet (FishFloraGround), before the blend, so a pixel half turf and half rock takes
     * the shift on its turf only. Found once for the pixel's four layers. Albedo here is linear (the sRGB array decodes on
     * sampling), as the palette is. */
    float3 groundShift = FishTerrainGroundShift(positionWS.xz, float4(layerFlora[0].x, layerFlora[1].x, layerFlora[2].x, layerFlora[3].x), weights);

    UNITY_UNROLL
    for (int j = 0; j < 4; j++)
    {
        half w = weights[j];
        float4 surface = layerSurface[j];
        half3 layerAlbedo = albedo[j].rgb * (half3)layerTint[j].rgb;
#if !defined(FISH_TERRAIN_NORMALS_ONLY)
        layerAlbedo = (half3)FishFloraGround(layerAlbedo, layerFlora[j].xyz, groundShift);
#endif
        mixedAlbedo += layerAlbedo * w;
        mixedNormal += normals[j] * w;
        // TerrainLit's rule: a layer with a mask map takes metallic, AO and smoothness from it; one without
        // takes its sliders, with smoothness from the albedo's alpha when the albedo has one.
        half defaultSmoothness = albedo[j].a * (half)surface.y;
        half defaultOcclusion = (half)(layerMaskScale[j].g + layerMaskOffset[j].g);
        metallic += lerp((half)surface.x, masks[j].r, hasMask[j]) * w;
        smoothness += lerp(defaultSmoothness, masks[j].a, hasMask[j]) * w;
        occlusion += lerp(defaultOcclusion, masks[j].g, hasMask[j]) * w;
    }

    // Avoid a NaN when normalising a blend that cancels out.
    mixedNormal.z += 1e-4h;

    s.albedo = mixedAlbedo;
    s.normalTS = normalize(mixedNormal);
    s.metallic = saturate(metallic);
    s.smoothness = saturate(smoothness);
    s.occlusion = saturate(occlusion);
    s.covered = saturate(covered);
#if !defined(FISH_TERRAIN_BACKDROP)
    FishApplyPaths(s, positionWS, worldDx, worldDy, ids.x, groundShift);
#endif
    return s;
}

/// <summary>The ground with gradients given, laid top-down only (the far backdrop: no faces it could show).</summary>
FishArraySurface FishSampleArraySurfaceGrad(float2 uv, float3 positionWS, float2 worldDx, float2 worldDy)
{
    return FishSampleArraySurfaceSloped(uv, positionWS, float3(worldDx.x, 0.0, worldDx.y), float3(worldDy.x, 0.0, worldDy.y), half3(0.0h, 1.0h, 0.0h));
}

/// <summary>The ground at one pixel whose ground faces <paramref name="groundNormalWS"/>, with the world gradients taken here.</summary>
FishArraySurface FishSampleArraySurface(float2 uv, float3 positionWS, half3 groundNormalWS)
{
    return FishSampleArraySurfaceSloped(uv, positionWS, ddx(positionWS), ddy(positionWS), groundNormalWS);
}

#ifdef FISH_TERRAIN_BACKDROP
// The backdrop's colour bake (SceneBackdropBuilder): the horizon's biomes as one colour per 26 m texel.
TEXTURE2D(_FishBackdropColour);   SAMPLER(sampler_FishBackdropColour);

/// <summary>
/// The ground past the scene's edge: the scene's own texture arrays near the camera, the colour bake far off.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the arrays.</b> The terrain the backdrop meets is drawn from these arrays, tiled by world position,
/// so with them the same textures run on across the scene's edge instead of stopping at a flat colour.
/// </para>
/// <para>
/// <b>Why the colour bake as well.</b> Far off a layer's texture is below a pixel and its mip is its average
/// colour, which is what the bake already holds: <c>_FishBackdropDetail</c> fades the arrays out with distance
/// and the branch skips them past it, so the far backdrop costs one sample. The bake also stands in wherever
/// the control maps do not cover the ground (<see cref="FishArraySurface.covered"/>): a biome found only past
/// the edge has no slice in the scene's arrays.
/// </para>
/// </remarks>
FishArraySurface FishBackdropSurface(float2 uv, float3 positionWS)
{
    FishArraySurface s;
    s.albedo = SAMPLE_TEXTURE2D(_FishBackdropColour, sampler_FishBackdropColour, uv).rgb;
#ifndef FISH_TERRAIN_NORMALS_ONLY
    /* FishMMO edit: the bake knows no layers, so it takes only the world's own ground (FishFloraGround as for a wholly
     * mineral layer): on an airless or thin-aired world, the one place that colour applies, the horizon is bare ground,
     * and the near arrays above (each layer on its world) fade into a bake of the same cast. Under ordinary air
     * (_FishFloraGround.a = 0) and with the looks off it is the bake as it was. */
    s.albedo = (half3)FishFloraGround(s.albedo, float3(0.0, 0.0, 1.0), float3(1.0, 1.0, 1.0));
#endif
    s.normalTS = half3(0.0h, 0.0h, 1.0h);
    s.metallic = 0.0h;
    s.smoothness = 0.0h;
    s.occlusion = 1.0h;
    s.covered = 0.0h;

    // Outside the branch: gradients are undefined where neighbouring pixels take different sides of it.
    float2 worldDx = ddx(positionWS.xz);
    float2 worldDy = ddy(positionWS.xz);
    float distance = length(positionWS - GetCameraPositionWS());
    half detail = 1.0h - (half)smoothstep(_FishBackdropDetail.x, _FishBackdropDetail.y, distance);

    // The depth-normals pass keeps the mesh's normal: layer normals a kilometre off do nothing SSAO can show,
    // and reading them there cost 0.13 ms of the backdrop's 0.2 ms in that pass.
#ifndef FISH_TERRAIN_NORMALS_ONLY
    UNITY_BRANCH
    if (detail > 0.0h && _FishArrayInfo.x > 0.5)
    {
        FishArraySurface arrays = FishSampleArraySurfaceGrad(uv, positionWS, worldDx, worldDy);
        half w = detail * arrays.covered;
        s.albedo = lerp(s.albedo, arrays.albedo, w);
        s.normalTS = normalize(lerp(s.normalTS, arrays.normalTS, w));
        s.metallic = arrays.metallic * w;
        s.smoothness = arrays.smoothness * w;
        s.occlusion = lerp(1.0h, arrays.occlusion, w);
        s.covered = arrays.covered;
    }
#endif
    return s;
}

#define FISH_GROUND_SURFACE(uv, positionWS, groundNormalWS) FishBackdropSurface(uv, positionWS)
#else
#define FISH_GROUND_SURFACE(uv, positionWS, groundNormalWS) FishSampleArraySurface(uv, positionWS, groundNormalWS)
#endif

#endif
