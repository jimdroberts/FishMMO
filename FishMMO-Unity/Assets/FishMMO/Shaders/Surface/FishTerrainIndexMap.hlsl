// FishMMO: the terrain's index map and layer table, as the terrain and the contact blend read them.
//
// INDEX MAP (TerrainIndexMap.cs; baked per tile from the alphamaps by FishTerrainIndexBake.shader, TerrainIndexMapBaker):
// one R32G32_UInt texel per alphamap texel,
//   x  the four strongest layer ids, a byte each, strongest first
//   y  their weights in bytes (0..255), each LESS the texel's fifth-strongest weight (the seam-free rule, at the texel)
// so a scene may name up to 256 layers and a pixel still reads four texels, however many it has.
//
// LAYER TABLE (TerrainLayerTable.cs): an RGBA float texture, column = layer, row = which number (FISH_LAYER_ROW_*).
//
// Plain HLSL (no SRP library) and loads only (no sampler): an index cannot be filtered, and a load costs no sampler slot.

#ifndef FISHMMO_TERRAIN_INDEX_MAP_INCLUDED
#define FISHMMO_TERRAIN_INDEX_MAP_INCLUDED

#define FISH_LAYER_ROW_ST 0           // 1 / tile size, offset / tile size: applied to world x/z
#define FISH_LAYER_ROW_TINT 1         // rgb the diffuse remap maximum (a tint), a the normal scale
#define FISH_LAYER_ROW_MASK_OFFSET 2  // the mask remap: value = sample * scale + offset
#define FISH_LAYER_ROW_MASK_SCALE 3
#define FISH_LAYER_ROW_SURFACE 4      // x metallic, y smoothness (1 when the albedo's alpha holds it), z 1 when it has a mask map
#define FISH_LAYER_ROW_FLORA 5        // x living, y lichen, z mineral (TerrainArraySet.LayerFlora)
#define FISH_LAYER_ROW_PATHS 6        // x its biome's path ground slice, y road ground, z cobbles (-1 none)
#define FISH_LAYER_ROW_BACKDROP 7     // column c: x the scene layer backdrop channel c holds (SceneBackdrop.ControlLayers; c itself)
#define FISH_LAYER_ROW_GLOW 8         // the layer's glow (BiomeGroundGlow.Pack; FishGroundGlow.hlsl): x mode + 8 * period, y K, z strength, w plate m
#define FISH_LAYER_ROW_CLOCK 9        // the layer's shared-clock window (BiomeGroundClock.Pack; FishGroundClock.hlsl): x kind, y from, z to, w falloff
#define FISH_LAYER_ROW_CLOCK_SHARE 10 // x the share a clocked layer keeps outside its window, y the layer taking the rest (-1 none), w 1 when any layer is clocked

// One index texel's ids and weights (0..1), scaled by the texel's bilinear share.
void FishIndexDecode(uint2 packed, float share, out int4 ids, out float4 weights)
{
    ids = int4(packed.x & 255u, (packed.x >> 8) & 255u, (packed.x >> 16) & 255u, packed.x >> 24);
    weights = float4(packed.y & 255u, (packed.y >> 8) & 255u, (packed.y >> 16) & 255u, packed.y >> 24) * (share / 255.0);
}

// True when (w, id) ranks above (otherW, otherId): heavier first, the lower id between equals — the order the per-pixel
// control-map loop produced, which met the layers by index and moved one only for a strictly greater weight.
bool FishIndexBeats(float w, int id, float otherW, int otherId)
{
    return w > otherW || (w == otherW && id < otherId);
}

// Keeps the four strongest seen so far, sorted, and the fifth strongest (FishConsiderLayer, with the tie rule above:
// the merged candidates do not arrive in layer order). Branch-free: the compiler turns the conditionals into selects.
void FishIndexConsider(float weight, int index, inout float4 top, inout int4 ids, inout float fifth)
{
    bool b0 = FishIndexBeats(weight, index, top.x, ids.x);
    bool b1 = FishIndexBeats(weight, index, top.y, ids.y);
    bool b2 = FishIndexBeats(weight, index, top.z, ids.z);
    bool b3 = FishIndexBeats(weight, index, top.w, ids.w);
    fifth = b3 ? top.w : max(fifth, weight);
    float4 t = top;
    int4 i = ids;
    top.w = b2 ? t.z : (b3 ? weight : t.w);   ids.w = b2 ? i.z : (b3 ? index : i.w);
    top.z = b1 ? t.y : (b2 ? weight : t.z);   ids.z = b1 ? i.y : (b2 ? index : i.z);
    top.y = b0 ? t.x : (b1 ? weight : t.y);   ids.y = b0 ? i.x : (b1 ? index : i.y);
    top.x = b0 ? weight : t.x;                ids.x = b0 ? index : i.x;
}

/// <summary>
/// The blend between four index texels — (x0, y0), (x1, y0), (x0, y1), (x1, y1) — at the bilinear position f: the four
/// strongest layers and their normalised weights, the fifth subtracted; an unpainted point is layer 0 alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why by hand.</b> The hardware would blend the ids themselves (layer 3 and layer 9 make layer 6). Instead each
/// texel's weights take its bilinear share, the entries naming the same layer are added (a layer is weighed once, at the
/// first texel naming it), and the old rule runs on the result: keep the four strongest, subtract the fifth, normalise.
/// </para>
/// <para>
/// <b>The same picture.</b> Where the four texels hold four layers or fewer each, the merged weights are the bilinear
/// weights of every layer — what the control-map loop computed — so the four chosen, their weights and the seams are
/// the same. Where a texel had a fifth layer, its stored weights are already less that fifth, so at its centre the blend
/// is the old one, and between centres it stays continuous (a texel's share falls to zero before it is left).
/// </para>
/// <para>
/// Mirrored line for line by TerrainIndexMap.Merge (C#), which the tests hold against the old rule.
/// </para>
/// </remarks>
float4 FishIndexFilter(uint2 texel00, uint2 texel10, uint2 texel01, uint2 texel11, float2 f, out int4 ids)
{
    int4 tid[4];
    float4 tw[4];
    FishIndexDecode(texel00, (1.0 - f.x) * (1.0 - f.y), tid[0], tw[0]);
    FishIndexDecode(texel10, f.x * (1.0 - f.y), tid[1], tw[1]);
    FishIndexDecode(texel01, (1.0 - f.x) * f.y, tid[2], tw[2]);
    FishIndexDecode(texel11, f.x * f.y, tid[3], tw[3]);

    float4 top = 0.0;
    float fifth = 0.0;
    ids = 0;
    [unroll]
    for (int t = 0; t < 4; t++)
    {
        [unroll]
        for (int s = 0; s < 4; s++)
        {
            int layer = tid[t][s];
            bool seen = false;
            [unroll]
            for (int e = 0; e < t; e++)
            {
                seen = seen || any(tid[e] == layer);
            }
            float total = tw[t][s];
            [unroll]
            for (int g = t + 1; g < 4; g++)
            {
                total += dot((float4)(tid[g] == layer), tw[g]);
            }
            FishIndexConsider(seen ? 0.0 : total, layer, top, ids, fifth);
        }
    }

    float4 weights = max(top - fifth, 0.0);
    float sum = dot(weights, 1.0);
    if (sum <= 1e-4)
    {
        ids = 0;
        return float4(1.0, 0.0, 0.0, 0.0);
    }
    return weights / sum;
}

// The four texels around a tile's 0..1 point (t) and the position between them, for an index map of res texels a side:
// texel centres as the terrain addresses its control maps (texel 0 on the tile's edge), clamped at the far edge.
void FishIndexFootprint(float2 t, float2 res, out int2 i0, out int2 i1, out float2 f)
{
    float2 p = saturate(t) * max(res - 1.0, 0.0);
    int2 last = max((int2)res - 1, 0);
    i0 = min((int2)floor(p), max(last - 1, 0));
    i1 = min(i0 + 1, last);
    f = saturate(p - (float2)i0);
}

#endif
