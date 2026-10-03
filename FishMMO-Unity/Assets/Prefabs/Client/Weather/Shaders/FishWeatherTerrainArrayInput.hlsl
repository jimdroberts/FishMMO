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

// The most layers one terrain carries: eight control maps of four channels. Matches
// SceneTerrainPalette.MaximumLayers and TerrainArrayLayerParams.MaximumLayers on the C# side.
#define FISH_TERRAIN_ARRAY_MAX_LAYERS 32

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    half4 _BaseColor;
    half _Cutoff;
    // FishMMO edit: how much weather this terrain takes, and how deep the snow lies on it.
    half _FishWeatherAmount;
    half _FishSnowDepth;
    half _HeightTransition;
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
// The per-layer parameters are uniform arrays rather than a texture because they are a handful of numbers
// the shader reads by layer index: an array costs no texture binding (the fragment stage is already near
// the 16-texture floor some APIs guarantee) and no precision. They are re-pushed by the binder whenever it
// enables, so a domain reload or a scene load cannot leave them stale; their values live in the baked
// TerrainArraySet asset, because they depend on which art (committed or LOCAL) a layer resolved to.
CBUFFER_START(_FishTerrainArray)
    // x: layers in the arrays (0 = nothing baked yet), y: control maps bound,
    // z: 1 when the normal array holds anything, w: 1 when the mask array does.
    float4 _FishArrayInfo;
    float4 _FishControl0_TexelSize;
    // xy: 1 / tile size in metres, zw: tile offset / tile size. Applied to WORLD x/z.
    float4 _FishLayerST[FISH_TERRAIN_ARRAY_MAX_LAYERS];
    // rgb: diffuse remap maximum (a tint, as URP's terrain uses it), a: normal scale.
    float4 _FishLayerTint[FISH_TERRAIN_ARRAY_MAX_LAYERS];
    // The mask remap: value = sample * scale + offset (offset = remap min, scale = max - min).
    float4 _FishLayerMaskOffset[FISH_TERRAIN_ARRAY_MAX_LAYERS];
    float4 _FishLayerMaskScale[FISH_TERRAIN_ARRAY_MAX_LAYERS];
    // x: metallic, y: smoothness (1 when the albedo's alpha holds it), z: 1 when the layer has a mask map.
    float4 _FishLayerSurface[FISH_TERRAIN_ARRAY_MAX_LAYERS];
CBUFFER_END

// Unity's own alphamaps, bound as they are: terrainData.alphamapTextures[k] is control map k, channel c of
// it is layer 4k + c. Binding them directly (rather than copying them into an array) is what keeps the
// terrain paint tools working with no glue — a stroke writes these very textures — and it costs no second
// copy of what can be the largest textures in the scene. One sampler serves all eight.
TEXTURE2D(_FishControl0);   SAMPLER(sampler_FishControl0);
TEXTURE2D(_FishControl1);
TEXTURE2D(_FishControl2);
TEXTURE2D(_FishControl3);
TEXTURE2D(_FishControl4);
TEXTURE2D(_FishControl5);
TEXTURE2D(_FishControl6);
TEXTURE2D(_FishControl7);

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

// URP's terrain instancing, unchanged: the patch's vertices come from the heightmap texture.
void TerrainInstancing(inout float4 positionOS, inout float3 normal, inout float2 uv)
{
#ifdef UNITY_INSTANCING_ENABLED
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

#define FISH_CONSIDER_CONTROL(k) \
    if ((k) < controlCount) \
    { \
        half4 c = SAMPLE_TEXTURE2D(_FishControl##k, sampler_FishControl0, controlUV); \
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
/// </remarks>
half4 FishTopLayers(float2 uv, out int4 ids)
{
    // Texel centres, exactly as TerrainLit addresses _Control.
    float2 controlUV = (uv * (_FishControl0_TexelSize.zw - 1.0f) + 0.5f) * _FishControl0_TexelSize.xy;
    int controlCount = (int)_FishArrayInfo.y;

    half4 top = 0;
    half fifth = 0;
    ids = 0;
    FISH_CONSIDER_CONTROL(0)
    FISH_CONSIDER_CONTROL(1)
    FISH_CONSIDER_CONTROL(2)
    FISH_CONSIDER_CONTROL(3)
    FISH_CONSIDER_CONTROL(4)
    FISH_CONSIDER_CONTROL(5)
    FISH_CONSIDER_CONTROL(6)
    FISH_CONSIDER_CONTROL(7)

    half4 weights = max(top - fifth, 0);
    half total = dot(weights, 1.0h);
    if (total <= 1e-4h)
    {
        ids = 0;
        return half4(1, 0, 0, 0);
    }
    return weights / total;
}

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

/// <summary>
/// The ground at one pixel: its four strongest layers sampled from the arrays and blended.
/// </summary>
/// <param name="uv">The tile's 0..1 coordinate, for the control maps.</param>
/// <param name="positionWS">World position, for the layers' tiling.</param>
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
/// </remarks>
FishArraySurface FishSampleArraySurface(float2 uv, float3 positionWS)
{
    FishArraySurface s;
    s.albedo = half3(0.5h, 0.5h, 0.5h);
    s.normalTS = half3(0.0h, 0.0h, 1.0h);
    s.metallic = 0.0h;
    s.smoothness = 0.0h;
    s.occlusion = 1.0h;

    // Nothing baked yet (a fresh clone before its first bake, or a build without arrays): plain grey ground
    // that still takes light and weather, rather than garbage from unbound slices.
    if (_FishArrayInfo.x < 0.5)
    {
        return s;
    }

    int4 ids;
    half4 weights = FishTopLayers(uv, ids);

    float2 world = positionWS.xz;
    float2 worldDx = ddx(world);
    float2 worldDy = ddy(world);
    bool hasNormals = _FishArrayInfo.z > 0.5;
    bool hasMasks = _FishArrayInfo.w > 0.5;

    half4 albedo[4];
    half4 masks[4];
    half3 normals[4];
    half4 hasMask = 0;

    UNITY_UNROLL
    for (int k = 0; k < 4; k++)
    {
        int id = ids[k];
        float4 st = _FishLayerST[id];
        float2 layerUV = world * st.xy + st.zw;
        float2 dx = worldDx * st.xy;
        float2 dy = worldDy * st.xy;

        albedo[k] = half4(0.5h, 0.5h, 0.5h, 1.0h);
        masks[k] = 0.5h;
        normals[k] = half3(0.0h, 0.0h, 1.0h);
        hasMask[k] = (half)_FishLayerSurface[id].z;

        UNITY_BRANCH
        if (weights[k] > 0.0h)
        {
            albedo[k] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishAlbedoArray, sampler_FishAlbedoArray, layerUV, id, dx, dy);
            if (hasMasks)
            {
                masks[k] = lerp(0.5h, SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishMaskArray, FISH_ARRAY_SURFACE_SAMPLER, layerUV, id, dx, dy), hasMask[k]);
            }
            if (hasNormals)
            {
                // Written as plain RGB by the baker on every platform, so decoded as RGB here rather than
                // through UnpackNormalScale, whose layout follows the platform's normal-map encoding.
                half4 packed = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishNormalArray, FISH_ARRAY_SURFACE_SAMPLER, layerUV, id, dx, dy);
                half3 n;
                n.xy = (packed.rg * 2.0h - 1.0h) * (half)_FishLayerTint[id].a;
                n.z = sqrt(max(1.0e-3h, 1.0h - saturate(dot(n.xy, n.xy))));
                normals[k] = n;
            }
        }
        masks[k] = masks[k] * (half4)_FishLayerMaskScale[id] + (half4)_FishLayerMaskOffset[id];
    }

#ifdef _TERRAIN_BLEND_HEIGHT
    FishHeightBlend(weights, half4(masks[0].b, masks[1].b, masks[2].b, masks[3].b));
#endif

    half3 mixedAlbedo = 0;
    half3 mixedNormal = 0;
    half metallic = 0;
    half smoothness = 0;
    half occlusion = 0;

    UNITY_UNROLL
    for (int j = 0; j < 4; j++)
    {
        int id = ids[j];
        half w = weights[j];
        float4 surface = _FishLayerSurface[id];
        mixedAlbedo += albedo[j].rgb * (half3)_FishLayerTint[id].rgb * w;
        mixedNormal += normals[j] * w;
        // TerrainLit's rule: a layer with a mask map takes metallic, AO and smoothness from it; one without
        // takes its sliders, with smoothness from the albedo's alpha when the albedo has one.
        half defaultSmoothness = albedo[j].a * (half)surface.y;
        half defaultOcclusion = (half)(_FishLayerMaskScale[id].g + _FishLayerMaskOffset[id].g);
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
    return s;
}

#endif
