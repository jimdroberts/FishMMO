#ifndef FISHMMO_GROUND_COLOUR_INCLUDED
#define FISHMMO_GROUND_COLOUR_INCLUDED

// The ground colour map and the distance blending built on it (GroundColourMap, FishMMO.Client/World/Terrain):
// one texture over every active terrain of what the ground looks like (its splat weights times the colours
// the terrain array shader draws), published as globals in play mode. Params.x is 0 when there is no map
// (edit mode, no terrain): everything here then does nothing.
//
// Shared by FishVegetationPasses.hlsl (grass, flowers, trees), FishGrassBlades.hlsl (procedural blades) and
// FishWeatherLitForwardPass.hlsl (its GPU-instanced terrain rocks only). The contact blend at the end is the
// GPU-drawn trees and rocks' only (TerrainGpuRenderer); the scattered pebbles and litter take none.

// URP's inline samplers (sampler_LinearClamp): every texture here other than _FishGroundColour itself is read through
// one, never through sampler_FishGroundColour. A pass that reads only the normal map or the heights (a shadow or depth
// pass running the skirt) has _FishGroundColour stripped, and its sampler then matches no texture: Vulkan and D3D
// reject the shader ("Unrecognized sampler 'sampler_fishgroundcolour'", 2026-10-08).
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

TEXTURE2D(_FishGroundColour);
SAMPLER(sampler_FishGroundColour);
float4 _FishGroundColourRect;    // xy the map's world xz origin, zw one over its xz size
float4 _FishGroundColourParams;  // x 1 when the map is set; y debug view; z blend at the root; w blend over the whole plant
float4 _FishGroundColourShape;   // x metres over which the root blend eases to the plant blend; y, z crown fade start/end (m); w blade detail
float4 _FishDistanceBlend;       // x start (m), y end (m), z normal flatten, w ground colour pull — distant terrain objects
TEXTURE2D(_FishGroundNormal);    // the terrain's surface normal over the same rect: linear, rgb = n·0.5 + 0.5, a coverage
float4 _FishContactLarge;        // trees' and terrain rocks' bases: x band height (m), y how much terrain the band shows above its foot, w 1 when on
// Per draw, not global: 1 on TerrainGpuRenderer's tree and rock draws (TerrainTreeInstancing, CliffRockInstancing), set
// on their own material clones and per-draw blocks. Nothing else sets it, so it reads 0 and every ordinary material
// drawn with these shaders (buildings, props, the scattered pebbles and litter, the shared assets) is untouched.
float _FishContactOn;

// The ground's colour under a world position (rgb) and the map's coverage there (a; 0 outside it or where no
// terrain was read).
half4 FishGroundColourAt(float3 positionWS)
{
    if (_FishGroundColourParams.x <= 0.0)
    {
        return half4(0.0, 0.0, 0.0, 0.0);
    }
    float2 uv = (positionWS.xz - _FishGroundColourRect.xy) * _FishGroundColourRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return half4(0.0, 0.0, 0.0, 0.0);
    }
    return SAMPLE_TEXTURE2D_LOD(_FishGroundColour, sampler_FishGroundColour, uv, 0.0);
}

// The terrain's surface normal under a world position; straight up where the map knows nothing.
float3 FishGroundNormalAt(float3 positionWS)
{
    if (_FishGroundColourParams.x <= 0.0)
    {
        return float3(0.0, 1.0, 0.0);
    }
    float2 uv = (positionWS.xz - _FishGroundColourRect.xy) * _FishGroundColourRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return float3(0.0, 1.0, 0.0);
    }
    float4 packed = SAMPLE_TEXTURE2D_LOD(_FishGroundNormal, sampler_LinearClamp, uv, 0.0);
    float3 n = packed.rgb * 2.0 - 1.0;
    return packed.a > 0.0 && dot(n, n) > 1e-4 ? normalize(n) : float3(0.0, 1.0, 0.0);
}

// Distant terrain objects (trees, rocks, plants) settle into the landscape: with distance their lighting
// normal turns toward the TERRAIN's normal under them (not straight up: a tree on a slope facing away from
// the sun lit as if on flat ground out-shone its own hillside), so they shade like the slope they stand
// on, and their colour drifts partly toward the local ground's. Close up nothing changes.
// Returned as factors, not applied, so each caller lerps in its own types (URP's InputData.normalWS is a
// float3, a vegetation pass's a half3, and an inout parameter must match exactly): far is the 0..1
// distance factor (fade gloss and glow by it), flatten how far the normal goes to target, pull.rgb the
// ground's colour and pull.a how much of it the albedo takes.
void FishDistanceBlend(float3 positionWS, out float far, out float flatten, out float3 target, out half4 pull)
{
    far = 0.0;
    flatten = 0.0;
    target = float3(0.0, 1.0, 0.0);
    pull = half4(0.0, 0.0, 0.0, 0.0);
    if (_FishGroundColourParams.x <= 0.0 || _FishDistanceBlend.y <= _FishDistanceBlend.x)
    {
        return;
    }
    far = smoothstep(_FishDistanceBlend.x, _FishDistanceBlend.y, distance(_WorldSpaceCameraPos, positionWS));
    if (far <= 0.0)
    {
        return;
    }
    flatten = far * _FishDistanceBlend.z;
    target = FishGroundNormalAt(positionWS);
    half4 ground = FishGroundColourAt(positionWS);
    pull = half4(ground.rgb, (half)(far * _FishDistanceBlend.w) * ground.a);
}

// ── Contact with the ground ───────────────────────────────────────────────────────────────────
// A rock or trunk drawn in its own colour right down to the ground line reads as set ON the terrain, not in it.
// Over a band at its base it renders the TERRAIN instead — exactly as the terrain shader renders that point: the same
// layers chosen the same way under that pixel (FishWeatherTerrainArrayInput.hlsl's FishSampleArraySurface, ported here
// line for line: height blend, mask maps, tints, normal maps), the same surface normal (from the heightmap, as the
// terrain's own normal map is), the same weather and the same lighting (FishFragmentPBR, FishGroundContactLit.hlsl),
// and then blends its own LIT colour into that over the band. At the ground line the two are one surface; no geometry
// is added or moved, so collision stays exact.
//
// Height above the ground is measured per pixel against the terrain's own heightmap (GroundMaterialMap's copy) plus
// the terrain's snow lift, so the band follows the ground line the terrain is drawn at.

#define FISH_GROUND_MAX_TILES 32      // GroundMaterialMap.MaxTiles
#define FISH_GROUND_MAX_LAYERS 32     // GroundMaterialMap.MaxLayers
#define FISH_GROUND_MAX_CONTROLS 8    // GroundMaterialMap.MaxControls

float4 _FishGroundInfo;                              // x terrains copied (0 = none), y layers in the arrays, z 1 when the normal array holds anything
float4 _FishGroundArrayRes;                          // x heightmap slice size, y control slice size (texels)
float4 _FishGroundTile[FISH_GROUND_MAX_TILES];       // xy world xz origin, zw 1 / size xz
float4 _FishGroundTileData[FISH_GROUND_MAX_TILES];   // x world y origin, y terrain height (m: a heights texel is 0..1 of it), z control maps, w first control slice
float4 _FishGroundLayerST[FISH_GROUND_MAX_LAYERS];   // the terrain's own per-layer values (TerrainArraySet)
float4 _FishGroundLayerTint[FISH_GROUND_MAX_LAYERS];
float4 _FishGroundLayerSurface[FISH_GROUND_MAX_LAYERS];
float4 _FishGroundLayerMaskOffset[FISH_GROUND_MAX_LAYERS];
float4 _FishGroundLayerMaskScale[FISH_GROUND_MAX_LAYERS];
float4 _FishGroundBlend;                             // x the terrain's height transition, y 1 when it height-blends, z 1 when the mask array holds anything, w its snow lift (m)
TEXTURE2D_ARRAY(_FishGroundHeights);                 // one slice per terrain, 0..1 of its height (uploaded from TerrainData.GetHeights)
TEXTURE2D_ARRAY(_FishGroundControl);                 // the terrains' control maps, consecutive slices per terrain
TEXTURE2D_ARRAY(_FishGroundAlbedo);  SAMPLER(sampler_FishGroundAlbedo);   // the scene's layer arrays, with their own sampler
TEXTURE2D_ARRAY(_FishGroundNormals);
TEXTURE2D_ARRAY(_FishGroundMasks);

// This draw's settings (x band, y strength, w on), or all zero when it takes no contact blend.
float4 FishContactParams()
{
    return _FishContactOn > 0.5 ? _FishContactLarge : float4(0.0, 0.0, 0.0, 0.0);
}

// The copied terrain under a world xz and the 0..1 position on it, or −1.
//
// Every loop over tiles or control maps here is UNITY_LOOP with a runtime bound and no return inside: FXC (Unity's
// HLSL compiler) otherwise tries to unroll a constant-bound loop with early exits and texture reads in full, at every
// call site, in every variant — the vegetation and rock shaders' compile stalled at 84% (2026-10-08).
int FishGroundTileAt(float2 xz, out float2 t)
{
    t = float2(0.0, 0.0);
    int found = -1;
    int count = min((int)_FishGroundInfo.x, FISH_GROUND_MAX_TILES);
    UNITY_LOOP
    for (int i = 0; i < count; i++)
    {
        float2 local = (xz - _FishGroundTile[i].xy) * _FishGroundTile[i].zw;
        if (all(local >= 0.0) && all(local <= 1.0))
        {
            t = local;
            found = i;
            break;
        }
    }
    return found;
}

// No check of the traced ground against the instance's root: cliff rocks are pushed INTO the slope at placement (up to
// 85 % of their thickness, CliffRockPlacement), so a root metres from the surface is normal, and such a check switched
// the blend off for them and painted a flat-plane band halfway up instead (2026-10-08). The trace is safe without it:
// what stands above the ground measures far above it, and what is buried is hidden by the terrain.

// A slice's texel-centre coordinate for a 0..1 tile position: texel 0 on the tile's edge, as the terrain addresses
// its heightmap and control maps.
float2 FishGroundTexelUV(float2 t, float res)
{
    return (t * (res - 1.0) + 0.5) / max(res, 1.0);
}

// The terrain's surface height (world y) at a position on a tile.
float FishGroundHeightAt(int tile, float2 t)
{
    float h = SAMPLE_TEXTURE2D_ARRAY_LOD(_FishGroundHeights, sampler_LinearClamp, FishGroundTexelUV(t, _FishGroundArrayRes.x), tile, 0.0).r;
    return _FishGroundTileData[tile].x + h * _FishGroundTileData[tile].y;
}

// FishWeatherTerrainArrayInput.hlsl's FishConsiderLayer: keeps the four strongest weights, sorted, and the fifth.
void FishGroundConsider(half weight, int index, inout half4 top, inout int4 ids, inout half fifth)
{
    bool b0 = weight > top.x;
    bool b1 = weight > top.y;
    bool b2 = weight > top.z;
    bool b3 = weight > top.w;
    fifth = b3 ? top.w : max(fifth, weight);
    half4 t = top;
    int4 i = ids;
    top.w = b2 ? t.z : (b3 ? weight : t.w);   ids.w = b2 ? i.z : (b3 ? index : i.w);
    top.z = b1 ? t.y : (b2 ? weight : t.z);   ids.z = b1 ? i.y : (b2 ? index : i.z);
    top.y = b0 ? t.x : (b1 ? weight : t.y);   ids.y = b0 ? i.x : (b1 ? index : i.y);
    top.x = b0 ? weight : t.x;                ids.x = b0 ? index : i.x;
}

// The four strongest layers at a position on a tile and their weights (FishTopLayers' rule: the fifth subtracted,
// normalised; an unpainted texel is layer 0).
half4 FishGroundLayersAt(int tile, float2 t, out int4 ids)
{
    float2 uv = FishGroundTexelUV(t, _FishGroundArrayRes.y);
    int count = min((int)_FishGroundTileData[tile].z, FISH_GROUND_MAX_CONTROLS);
    int first = (int)_FishGroundTileData[tile].w;
    half4 top = 0;
    ids = 0;
    half fifth = 0;
    UNITY_LOOP
    for (int k = 0; k < count; k++)
    {
        half4 c = SAMPLE_TEXTURE2D_ARRAY_LOD(_FishGroundControl, sampler_LinearClamp, uv, first + k, 0.0);
        FishGroundConsider(c.r, k * 4 + 0, top, ids, fifth);
        FishGroundConsider(c.g, k * 4 + 1, top, ids, fifth);
        FishGroundConsider(c.b, k * 4 + 2, top, ids, fifth);
        FishGroundConsider(c.a, k * 4 + 3, top, ids, fifth);
    }
    half4 weights = max(top - fifth, 0.0h);
    half total = dot(weights, 1.0h);
    if (total <= 1e-4h)
    {
        ids = 0;
        return half4(1.0h, 0.0h, 0.0h, 0.0h);
    }
    return weights / total;
}

// Metres above the terrain straight below a vertex as drawn (positionWS); originWS is the instance's root. Linear in
// the position across a triangle as far as the terrain is, so it interpolates. Far above (no contact) when this draw
// takes none; the plane through the root where no terrain was copied or the copy disagrees with the root.
float FishContactHeight(float3 positionWS, float3 originWS)
{
    if (FishContactParams().w <= 0.0)
    {
        return 1e4;
    }
    float2 rootT;
    int root = FishGroundTileAt(originWS.xz, rootT);
    if (root >= 0)
    {
        // On whichever terrain is under the vertex (a big rock can straddle a seam).
        float2 ownT;
        int own = FishGroundTileAt(positionWS.xz, ownT);
        if (own < 0)
        {
            own = root;
            ownT = saturate((positionWS.xz - _FishGroundTile[root].xy) * _FishGroundTile[root].zw);
        }
        // Across the slope, not straight down: on a cliff face a vertical metre is a few centimetres from the surface,
        // so the band would run thin there and thick on the flat. Linear in the position as long as the slope is.
        return (positionWS.y - FishGroundHeightAt(own, ownT)) * FishGroundNormalAt(positionWS).y;
    }
    if (_FishGroundColourParams.x > 0.0)
    {
        return dot(positionWS - originWS, FishGroundNormalAt(originWS));
    }
    return 1e4;
}

// The terrain height under a world xz, on whichever copied terrain is there (fallbackTile when none is).
float FishGroundHeightUnder(float2 xz, int fallbackTile)
{
    float2 t;
    int tile = FishGroundTileAt(xz, t);
    if (tile < 0)
    {
        tile = fallbackTile;
        t = saturate((xz - _FishGroundTile[tile].xy) * _FishGroundTile[tile].zw);
    }
    return FishGroundHeightAt(tile, t);
}

// The terrain's own surface at a point, as FishSampleArraySurface makes it.
struct FishGroundPixel
{
    half3 albedo;
    half3 normalWS;
    half metallic;
    half smoothness;
    half occlusion;
};

// The terrain's surface normal at a position on a tile, from its heights a texel either side — what its own normal
// map (Unity's, from the same heights) holds, at the same resolution; not the 2 m ground colour map's.
float3 FishGroundHeightNormal(int tile, float2 t)
{
    float res = max(_FishGroundArrayRes.x, 2.0);
    float step = 1.0 / (res - 1.0);
    float hL = FishGroundHeightAt(tile, saturate(t - float2(step, 0.0)));
    float hR = FishGroundHeightAt(tile, saturate(t + float2(step, 0.0)));
    float hD = FishGroundHeightAt(tile, saturate(t - float2(0.0, step)));
    float hU = FishGroundHeightAt(tile, saturate(t + float2(0.0, step)));
    float2 size = 1.0 / max(_FishGroundTile[tile].zw, 1e-6);
    return normalize(float3((hL - hR) / (2.0 * step * size.x), 1.0, (hD - hU) / (2.0 * step * size.y)));
}

// Where the terrain is DRAWN at a point: its height, lifted by deep snow as the terrain shader lifts it (FishSnowLift).
float FishGroundSurfaceY(int tile, float2 t, float3 positionWS, float3 groundNormal)
{
    float y = FishGroundHeightAt(tile, t);
#if defined(FISHMMO_SURFACE_INCLUDED)
    if (_FishGroundBlend.w > 0.0 && _FishWeatherTier.x > 0.0 && _FishWeatherCover.x > 0.0)
    {
        float3 at = float3(positionWS.x, y, positionWS.z);
        y += FishSnowLiftMetres(at, groundNormal, _FishGroundBlend.w);
    }
#endif
    return y;
}

// URP's HeightBasedSplatModify on the four chosen layers (FishWeatherTerrainArrayInput.hlsl's FishHeightBlend).
void FishGroundHeightBlend(inout half4 weights, half4 heights, half transitionIn)
{
    half4 splatHeight = heights * weights;
    half maxHeight = max(splatHeight.r, max(splatHeight.g, max(splatHeight.b, splatHeight.a)));
    half transition = max(transitionIn, 1e-5h);
    half4 weightedHeights = max(0, splatHeight + transition - maxHeight.xxxx);
    weightedHeights = (weightedHeights + 1e-6h) * weights;
    half sumHeight = max(dot(weightedHeights, half4(1, 1, 1, 1)), 1e-6h);
    weights = weightedHeights / sumHeight.xxxx;
}

// The terrain's surface at a pixel: FishSampleArraySurface line for line, on the copied control maps and the scene's
// layer arrays, then its normal laid on up (the terrain's own normal there). steep (0..1) is for a host surface that is
// not the ground (a rock's wall): the texture, projected straight down as on the terrain, is blurred toward its colour
// and its normal maps flattened there, as it would smear down the wall. 0 where the band meets the ground.
FishGroundPixel FishGroundSurface(int tile, float2 t, float3 positionWS, float2 worldDx, float2 worldDy, half steep, half3 up)
{
    int4 ids;
    half4 weights = FishGroundLayersAt(tile, t, ids);
    bool hasNormals = _FishGroundInfo.z > 0.5;
    bool hasMasks = _FishGroundBlend.z > 0.5;
    // Wider gradients pick a smaller mip: at full steepness, the layer's few-texel average.
    float blur = lerp(1.0, 256.0, (float)steep);
    half detail = 1.0h - steep;

    half4 albedo[4];
    half4 masks[4];
    half3 normals[4];
    half4 hasMask = 0;
    UNITY_UNROLL
    for (int k = 0; k < 4; k++)
    {
        int id = ids[k];
        float4 st = _FishGroundLayerST[id];
        float2 uv = positionWS.xz * st.xy + st.zw;
        float2 dx = worldDx * st.xy * blur;
        float2 dy = worldDy * st.xy * blur;
        albedo[k] = half4(0.5h, 0.5h, 0.5h, 1.0h);
        masks[k] = 0.5h;
        normals[k] = half3(0.0h, 0.0h, 1.0h);
        hasMask[k] = (half)_FishGroundLayerSurface[id].z;
        UNITY_BRANCH
        if (weights[k] > 0.0h)
        {
            albedo[k] = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishGroundAlbedo, sampler_FishGroundAlbedo, uv, id, dx, dy);
            if (hasMasks)
            {
                masks[k] = lerp(0.5h, SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishGroundMasks, sampler_FishGroundAlbedo, uv, id, dx, dy), hasMask[k]);
            }
            if (hasNormals && detail > 0.0h)
            {
                half4 packed = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishGroundNormals, sampler_FishGroundAlbedo, uv, id, dx, dy);
                half3 n;
                n.xy = (packed.rg * 2.0h - 1.0h) * (half)_FishGroundLayerTint[id].a * detail;
                n.z = sqrt(max(1.0e-3h, 1.0h - saturate(dot(n.xy, n.xy))));
                normals[k] = n;
            }
        }
        masks[k] = masks[k] * (half4)_FishGroundLayerMaskScale[id] + (half4)_FishGroundLayerMaskOffset[id];
    }
    if (_FishGroundBlend.y > 0.5)
    {
        FishGroundHeightBlend(weights, half4(masks[0].b, masks[1].b, masks[2].b, masks[3].b), (half)_FishGroundBlend.x);
    }

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
        float4 surface = _FishGroundLayerSurface[id];
        mixedAlbedo += albedo[j].rgb * (half3)_FishGroundLayerTint[id].rgb * w;
        mixedNormal += normals[j] * w;
        // TerrainLit's rule: a layer with a mask map takes metallic, AO and smoothness from it; one without takes its
        // sliders, with smoothness from the albedo's alpha.
        half defaultSmoothness = albedo[j].a * (half)surface.y;
        half defaultOcclusion = (half)(_FishGroundLayerMaskScale[id].g + _FishGroundLayerMaskOffset[id].g);
        metallic += lerp((half)surface.x, masks[j].r, hasMask[j]) * w;
        smoothness += lerp(defaultSmoothness, masks[j].a, hasMask[j]) * w;
        occlusion += lerp(defaultOcclusion, masks[j].g, hasMask[j]) * w;
    }
    mixedNormal.z += 1e-4h;

    FishGroundPixel g;
    // The terrain's tangent frame: tangent = its object z × the surface normal (FishTerrainNormalWS).
    half3 tangent = cross(half3(0.0h, 0.0h, 1.0h), up);
    tangent *= rsqrt(max(dot(tangent, tangent), 1e-4h));
    g.normalWS = normalize(mul(normalize(mixedNormal), half3x3(-tangent, cross(up, tangent), up)));
    g.albedo = mixedAlbedo;
    g.metallic = saturate(metallic);
    g.smoothness = saturate(smoothness);
    g.occlusion = saturate(occlusion);
    return g;
}

// Where on the terrain a pixel's ground comes from: the tile and position on it that its layers are chosen at, the
// world xz its texture is read at (the pixel's own, or the base band's unfolded point), how far that point moves per
// metre the pixel rises (for the texture's gradients), the terrain's normal under the pixel, and how steep the host
// surface is (FishGroundSurface's steep). Found first, sampled once (FishGroundSurfaceAtSite): the base band and a
// rock's crevice soil share one sample, so the layer sampling is inlined once per shader.
struct FishGroundSite
{
    int tile;
    float2 t;
    float2 xz;
    float2 unfold;
    half3 normal;
    half steep;
};

// How much of the terrain a pixel of an object at its base renders (0..1), and where.
//   vertexHeight    the per-vertex FishContactHeight, interpolated: only a cheap first test (far above → nothing)
//   surfaceNormalWS the object's own normal there, before any normal map: which way the ground unfolds up the object
//
// The band reads as a skirt of ground drawn up over the object's foot: solid terrain over its lower third, fading
// into the object above (the profile's strength scales the fade, never the solid foot). Every orientation takes it —
// walls, trunks and the rounded underside of a rock's bevel just above the ground, which faces DOWN and was skipped as
// an "overhang": that bare, self-shadowed lip was the hard dark line under every rock (Jim, 2026-10-08).
//
// A wall cannot read the terrain's texture where the terrain reads it, straight down: every texel of the ground line
// would streak up the wall. The band instead UNFOLDS the ground up the wall: a pixel h metres up reads the ground h
// metres out from the wall along its normal, so the ground's texture carries on up the face at its own scale, unbroken
// at the ground line, as if the ground had been folded up against the rock.
// False (weight 0) when the pixel is not in the band or no terrain was copied there.
bool FishGroundContact(float3 positionWS, float vertexHeight, half3 surfaceNormalWS, out half weight, out FishGroundSite site)
{
    weight = 0.0h;
    site = (FishGroundSite)0;
    float4 p = FishContactParams();
    float band = max(0.01, p.x);
    // The per-vertex height is interpolated over the triangle while the ground under it is not flat: a generous margin,
    // so a pixel the band reaches is never skipped where the ground bulges up under a large triangle.
    if (p.w <= 0.0 || vertexHeight > band + 2.0 || _FishGroundInfo.y < 0.5)
    {
        return false;
    }
    float2 t;
    int tile = FishGroundTileAt(positionWS.xz, t);
    if (tile < 0)
    {
        return false;
    }
    float3 groundNormal = FishGroundHeightNormal(tile, t);
    // Across the slope, not straight down: on a cliff face a vertical metre is a few centimetres from the surface.
    float height = max(0.0, (positionWS.y - FishGroundSurfaceY(tile, t, positionWS, groundNormal)) * groundNormal.y);
    half u = (half)saturate(1.0 - height / band);
    half contact = u * u * (3.0h - 2.0h * u);
    if (contact <= 0.0h)
    {
        return false;
    }
    half f = (half)saturate(1.0 - height / (0.35 * band));
    half foot = f * f * (3.0h - 2.0h * f);
    weight = lerp(contact * (half)p.y, 1.0h, foot);

    // The unfold: out along the surface's horizontal facing, by the height (scaled by how much it faces sideways, so a
    // near-flat top stays put and a 45° face unfolds by about its slope length).
    float3 n = normalize((float3)surfaceNormalWS);
    float side = length(n.xz);
    float2 unfold = side > 1e-3 ? n.xz / side * saturate(side) : float2(0.0, 0.0);
    site.xz = positionWS.xz + unfold * height;
    site.unfold = unfold;
    // The layers where the texture is read, so the colours and the texture agree; the same tile unless that point is
    // over a seam.
    float2 sampleT = (site.xz - _FishGroundTile[tile].xy) * _FishGroundTile[tile].zw;
    if (any(sampleT < 0.0) || any(sampleT > 1.0))
    {
        int other = FishGroundTileAt(site.xz, sampleT);
        if (other >= 0)
        {
            tile = other;
        }
        else
        {
            sampleT = saturate(sampleT);
        }
    }
    site.tile = tile;
    site.t = sampleT;
    site.normal = (half3)groundNormal;
    site.steep = 0.0h;   // unfolded: no streaks to blur
    return weight > 0.0h;
}

// The terrain under any pixel (for a crevice's soil away from the base band); false where no terrain was copied.
bool FishGroundSiteAt(float3 positionWS, half steep, out FishGroundSite site)
{
    site = (FishGroundSite)0;
    float2 t;
    int tile = _FishGroundInfo.y > 0.5 ? FishGroundTileAt(positionWS.xz, t) : -1;
    if (tile < 0)
    {
        return false;
    }
    site.tile = tile;
    site.t = t;
    site.xz = positionWS.xz;
    site.unfold = float2(0.0, 0.0);
    site.normal = (half3)FishGroundHeightNormal(tile, t);
    site.steep = steep;
    return true;
}

// The terrain's surface at a site (worldDx, Dy: ddx/ddy of positionWS, taken by the caller outside any branch). The
// unfolded point moves by the pixel's own xz step plus the unfold times its rise, so a wall's texture takes the mip its
// height on screen asks for.
FishGroundPixel FishGroundSurfaceAtSite(FishGroundSite site, float3 positionWS, float3 worldDx, float3 worldDy)
{
    float2 dx = worldDx.xz + site.unfold * worldDx.y;
    float2 dy = worldDy.xz + site.unfold * worldDy.y;
    return FishGroundSurface(site.tile, site.t, float3(site.xz.x, positionWS.y, site.xz.y), dx, dy, site.steep, site.normal);
}

#endif
