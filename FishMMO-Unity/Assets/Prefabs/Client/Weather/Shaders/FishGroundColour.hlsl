#ifndef FISHMMO_GROUND_COLOUR_INCLUDED
#define FISHMMO_GROUND_COLOUR_INCLUDED

// The ground colour map and the distance blending built on it (GroundColourMap, FishMMO.Client/World/Terrain):
// one texture over every active terrain of what the ground looks like (its splat weights times the colours
// the terrain array shader draws), published as globals in play mode. Params.x is 0 when there is no map
// (edit mode, no terrain): everything here then does nothing.
//
// Shared by FishVegetationPasses.hlsl (grass, flowers, trees), FishGrassBlades.hlsl (procedural blades) and
// FishWeatherLitForwardPass.hlsl (its GPU-instanced terrain rocks only). The contact blend at the end is the
// GPU-drawn terrain objects' only: the detail scatter's pebbles, rocks, shells and litter, and TerrainGpuRenderer's
// trees and rocks.

TEXTURE2D(_FishGroundColour);
SAMPLER(sampler_FishGroundColour);
float4 _FishGroundColourRect;    // xy the map's world xz origin, zw one over its xz size
float4 _FishGroundColourParams;  // x 1 when the map is set; y debug view; z blend at the root; w blend over the whole plant
float4 _FishGroundColourShape;   // x metres over which the root blend eases to the plant blend; y, z crown fade start/end (m); w blade detail
float4 _FishDistanceBlend;       // x start (m), y end (m), z normal flatten, w ground colour pull — distant terrain objects
TEXTURE2D(_FishGroundNormal);    // the terrain's surface normal over the same rect: linear, rgb = n·0.5 + 0.5, a coverage
float4 _FishContactBlend;        // small details' bases: x band height (m), y colour at the ground, z normal at the ground, w 1 when on
float4 _FishContactLarge;        // the same for trees and terrain rocks (a deeper band)
// Per draw, not global: 1 on the detail scatter's draws (DetailScatterRenderer), 2 on TerrainGpuRenderer's (trees,
// rocks), set on their own material clones and per-draw blocks. Nothing else sets it, so it reads 0 and every
// ordinary material drawn with these shaders (buildings, props, the shared assets themselves) is untouched.
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
    float4 packed = SAMPLE_TEXTURE2D_LOD(_FishGroundNormal, sampler_FishGroundColour, uv, 0.0);
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
// A trunk, rock or pebble drawn in its own colour right down to the ground line reads as set ON the terrain,
// not in it. Over a band at its base it draws the TERRAIN's own surface instead — the same layers, weights,
// world tiling, tints and normal maps the ground beside it is drawn with — so at the join the two are the same
// surface and the line disappears.
//
// "Vertex paint on placement", done on the GPU because these objects are instanced (one mesh, thousands of
// placements, no per-placement vertex colours): every vertex traces straight down to the terrain (its exact
// height above it, GroundMaterialMap's copy of the heightmaps), and the instance's root picks the four strongest
// terrain layers there with their weights (the control maps). The root's pick is the same for every vertex of the
// instance, so it interpolates unchanged. Where no terrain was copied (another scene's array set, edit mode) the
// blend falls back to the ground colour map's average colour, measured from the plane through the root.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

#define FISH_GROUND_MAX_TILES 32      // GroundMaterialMap.MaxTiles
#define FISH_GROUND_MAX_LAYERS 32     // GroundMaterialMap.MaxLayers
#define FISH_GROUND_MAX_CONTROLS 8    // GroundMaterialMap.MaxControls

float4 _FishGroundInfo;                              // x terrains copied (0 = none), y layers in the arrays, z 1 when the normal array holds anything
float4 _FishGroundArrayRes;                          // x heightmap slice size, y control slice size (texels)
float4 _FishGroundTile[FISH_GROUND_MAX_TILES];       // xy world xz origin, zw 1 / size xz
float4 _FishGroundTileData[FISH_GROUND_MAX_TILES];   // x world y origin, y metres per heightmap unit, z control maps, w first control slice
float4 _FishGroundLayerST[FISH_GROUND_MAX_LAYERS];   // the terrain's own per-layer values (TerrainArraySet)
float4 _FishGroundLayerTint[FISH_GROUND_MAX_LAYERS];
float4 _FishGroundLayerSurface[FISH_GROUND_MAX_LAYERS];
TEXTURE2D_ARRAY(_FishGroundHeights);                 // one slice per terrain, Unity's heightmap units (0..0.5)
TEXTURE2D_ARRAY(_FishGroundControl);                 // the terrains' control maps, consecutive slices per terrain
TEXTURE2D_ARRAY(_FishGroundAlbedo);  SAMPLER(sampler_FishGroundAlbedo);   // the scene's layer arrays, with their own sampler
TEXTURE2D_ARRAY(_FishGroundNormals);

// What the vertex stage hands the pixel: x metres above the ground, y the root's four layers packed 5 bits
// each (−1 = none: use the average colour), and their weights.
struct FishContactData
{
    float2 trace;
    half4 weights;
};

// This draw's settings (x band, y colour, z normal, w on), or all zero when it takes no contact blend.
float4 FishContactParams()
{
    return _FishContactOn > 1.5 ? _FishContactLarge : _FishContactOn > 0.5 ? _FishContactBlend : float4(0.0, 0.0, 0.0, 0.0);
}

// The copied terrain under a world xz and the 0..1 position on it, or −1.
int FishGroundTileAt(float2 xz, out float2 t)
{
    t = float2(0.0, 0.0);
    int count = (int)_FishGroundInfo.x;
    for (int i = 0; i < FISH_GROUND_MAX_TILES; i++)
    {
        if (i >= count)
        {
            break;
        }
        float2 local = (xz - _FishGroundTile[i].xy) * _FishGroundTile[i].zw;
        if (all(local >= 0.0) && all(local <= 1.0))
        {
            t = local;
            return i;
        }
    }
    return -1;
}

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

// The four strongest layers at a position on a tile (FishTopLayers' rule: the fifth subtracted, normalised; an
// unpainted texel is layer 0), packed 5 bits each into a float (exact: 20 bits).
float FishGroundLayersAt(int tile, float2 t, out half4 weights)
{
    float2 uv = FishGroundTexelUV(t, _FishGroundArrayRes.y);
    int count = (int)_FishGroundTileData[tile].z;
    int first = (int)_FishGroundTileData[tile].w;
    half4 top = 0;
    int4 ids = 0;
    half fifth = 0;
    for (int k = 0; k < FISH_GROUND_MAX_CONTROLS; k++)
    {
        if (k >= count)
        {
            break;
        }
        half4 c = SAMPLE_TEXTURE2D_ARRAY_LOD(_FishGroundControl, sampler_LinearClamp, uv, first + k, 0.0);
        FishGroundConsider(c.r, k * 4 + 0, top, ids, fifth);
        FishGroundConsider(c.g, k * 4 + 1, top, ids, fifth);
        FishGroundConsider(c.b, k * 4 + 2, top, ids, fifth);
        FishGroundConsider(c.a, k * 4 + 3, top, ids, fifth);
    }
    weights = max(top - fifth, 0.0h);
    half total = dot(weights, 1.0h);
    if (total <= 1e-4h)
    {
        weights = half4(1.0h, 0.0h, 0.0h, 0.0h);
        ids = 0;
    }
    else
    {
        weights /= total;
    }
    return (float)(ids.x + ids.y * 32 + ids.z * 1024 + ids.w * 32768);
}

int4 FishGroundUnpackLayers(float packed)
{
    uint p = (uint)(packed + 0.5);
    return int4(p & 31u, (p >> 5u) & 31u, (p >> 10u) & 31u, (p >> 15u) & 31u);
}

// The vertex stage's part: the trace down to the terrain and the root's layers. positionWS is the vertex as drawn,
// originWS the instance's root.
FishContactData FishContactVertex(float3 positionWS, float3 originWS)
{
    FishContactData c;
    c.trace = float2(1e4, -1.0);
    c.weights = half4(1.0h, 0.0h, 0.0h, 0.0h);
    if (FishContactParams().w <= 0.0)
    {
        return c;
    }
    float2 rootT;
    int root = FishGroundTileAt(originWS.xz, rootT);
    if (root >= 0)
    {
        // Straight down from this vertex, on whichever terrain is under it (a big rock can straddle a seam).
        float2 ownT;
        int own = FishGroundTileAt(positionWS.xz, ownT);
        if (own < 0)
        {
            own = root;
            ownT = saturate((positionWS.xz - _FishGroundTile[root].xy) * _FishGroundTile[root].zw);
        }
        c.trace.x = positionWS.y - FishGroundHeightAt(own, ownT);
        c.trace.y = FishGroundLayersAt(root, rootT, c.weights);
    }
    else if (_FishGroundColourParams.x > 0.0)
    {
        c.trace.x = dot(positionWS - originWS, FishGroundNormalAt(originWS));
    }
    return c;
}

// 0..1, how much of the ground a point that high takes: all of it at (and under) the ground, easing to none
// at the band's top. Per pixel: a trunk's few rings would smear a per-vertex weight over metres.
half FishContactWeight(float height)
{
    half t = (half)saturate(1.0 - height / max(0.01, FishContactParams().x));
    return t * t;
}

// The terrain's own surface at a pixel from the root's four layers, as FishSampleArraySurface draws it: albedo
// array × tint, smoothness from the albedo's alpha, the layers' normal maps on the ground's slope. (No mask maps
// and no height blend: the band is a few centimetres to a metre of a trunk, and they would double the reads.)
void FishGroundSurface(FishContactData c, float3 positionWS, float2 worldDx, float2 worldDy, out half3 albedo, out half smoothness, out half3 normalWS)
{
    int4 ids = FishGroundUnpackLayers(c.trace.y);
    half4 weights = c.weights / max(dot(c.weights, 1.0h), 1e-4h);
    bool hasNormals = _FishGroundInfo.z > 0.5;
    half3 a = 0;
    half3 n = 0;
    half s = 0;
    UNITY_UNROLL
    for (int k = 0; k < 4; k++)
    {
        int id = ids[k];
        half w = weights[k];
        UNITY_BRANCH
        if (w > 0.0h)
        {
            float4 st = _FishGroundLayerST[id];
            float2 uv = positionWS.xz * st.xy + st.zw;
            float2 dx = worldDx * st.xy;
            float2 dy = worldDy * st.xy;
            half4 colour = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishGroundAlbedo, sampler_FishGroundAlbedo, uv, id, dx, dy);
            a += colour.rgb * (half3)_FishGroundLayerTint[id].rgb * w;
            s += colour.a * (half)_FishGroundLayerSurface[id].y * w;
            half3 layerNormal = half3(0.0h, 0.0h, 1.0h);
            if (hasNormals)
            {
                half4 packed = SAMPLE_TEXTURE2D_ARRAY_GRAD(_FishGroundNormals, sampler_FishGroundAlbedo, uv, id, dx, dy);
                layerNormal.xy = (packed.rg * 2.0h - 1.0h) * (half)_FishGroundLayerTint[id].a;
                layerNormal.z = sqrt(max(1.0e-3h, 1.0h - saturate(dot(layerNormal.xy, layerNormal.xy))));
            }
            n += layerNormal * w;
        }
    }
    n.z += 1e-4h;
    // The terrain's tangent frame: tangent = its object z × the surface normal (FishTerrainNormalWS).
    half3 up = (half3)FishGroundNormalAt(positionWS);
    half3 tangent = cross(half3(0.0h, 0.0h, 1.0h), up);
    tangent *= rsqrt(max(dot(tangent, tangent), 1e-4h));
    normalWS = normalize(mul(normalize(n), half3x3(-tangent, cross(up, tangent), up)));
    albedo = a;
    smoothness = saturate(s);
}

// The pixel's part: inside the band, albedo, gloss and lighting normal go to the terrain's. Samples nothing
// outside the band. Half types: the callers convert.
void FishContactApply(FishContactData c, float3 positionWS, inout half3 albedo, inout half smoothness, inout half3 normalWS)
{
    // Before any branch: the layers are sampled with explicit gradients inside one.
    float2 worldDx = ddx(positionWS.xz);
    float2 worldDy = ddy(positionWS.xz);
    half contact = FishContactWeight(c.trace.x);
    if (contact <= 0.0h)
    {
        return;
    }
    float4 p = FishContactParams();
    half3 groundAlbedo;
    half groundSmoothness;
    half3 groundNormal;
    half coverage = 1.0h;
    if (c.trace.y >= 0.0 && _FishGroundInfo.y > 0.5)
    {
        FishGroundSurface(c, positionWS, worldDx, worldDy, groundAlbedo, groundSmoothness, groundNormal);
    }
    else
    {
        half4 ground = FishGroundColourAt(positionWS);
        groundAlbedo = ground.rgb;
        coverage = ground.a;
        groundSmoothness = 0.0h;
        groundNormal = (half3)FishGroundNormalAt(positionWS);
    }
    half colour = contact * (half)p.y * coverage;
    albedo = lerp(albedo, groundAlbedo, colour);
    smoothness = lerp(smoothness, groundSmoothness, colour);
    normalWS = normalize(lerp(normalWS, groundNormal, contact * (half)p.z));
}

#endif
