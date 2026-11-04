#ifndef FISH_TERRAIN_INDEX_FAST_INCLUDED
#define FISH_TERRAIN_INDEX_FAST_INCLUDED
// Preserve the general 16-candidate merge as the reference and boundary path.
#define FishIndexFilter FishIndexFilterFull
#include "FishTerrainIndexMap.hlsl"
#undef FishIndexFilter
// Diagnostic control only: zero (the default) uses the common-layer path.
float _FishIndexFullFilter;
float4 FishIndexFilter(uint2 t00, uint2 t10, uint2 t01, uint2 t11, float2 f, out int4 ids)
{
    bool same = t00.x == t10.x && t00.x == t01.x && t00.x == t11.x;
    int4 layer = int4(t00.x & 255u, (t00.x >> 8) & 255u, (t00.x >> 16) & 255u, t00.x >> 24);
    bool distinct = layer.x != layer.y && layer.x != layer.z && layer.x != layer.w && layer.y != layer.z && layer.y != layer.w && layer.z != layer.w;
    [branch]
    if (_FishIndexFullFilter < 0.5 && same && distinct)
    {
        // The same four layers in each footprint texel: merge each layer once, then apply the
        // original ranking/tie rule. There is no fifth distinct layer to subtract.
        int4 unused;
        float4 w00, w10, w01, w11;
        FishIndexDecode(t00, (1.0-f.x)*(1.0-f.y), unused, w00);
        FishIndexDecode(t10, f.x*(1.0-f.y), unused, w10);
        FishIndexDecode(t01, (1.0-f.x)*f.y, unused, w01);
        FishIndexDecode(t11, f.x*f.y, unused, w11);
        float4 top = 0.0; float fifth = 0.0; ids = 0;
        [unroll] for (int s = 0; s < 4; s++)
        {
            float4 match = (float4)(layer == layer[s]);
            float total = w00[s]; total += dot(match,w10); total += dot(match,w01); total += dot(match,w11);
            FishIndexConsider(total,layer[s],top,ids,fifth);
        }
        float sum = dot(top,1.0);
        if (sum <= 1e-4) { ids = 0; return float4(1,0,0,0); }
        return top/sum;
    }
    return FishIndexFilterFull(t00,t10,t01,t11,f,ids);
}
#endif
