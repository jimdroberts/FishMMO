#ifndef FISH_GROUND_HEIGHT_LOADS_INCLUDED
#define FISH_GROUND_HEIGHT_LOADS_INCLUDED
// Bilinear mip-zero sampling without a filtering sampler; preserves R32 height precision on WebGPU.
float FishGroundHeightLoads(Texture2DArray<float4> heights, int size, int tile, float2 t)
{
    size = max(1,size);
    float2 p = saturate(t) * (float)(size - 1);
    int2 a = (int2)floor(p), b = min(a + 1, size - 1);
    float2 f = p - (float2)a;
    float h00 = heights.Load(int4(a,tile,0)).r;
    float h10 = heights.Load(int4(b.x,a.y,tile,0)).r;
    float h01 = heights.Load(int4(a.x,b.y,tile,0)).r;
    float h11 = heights.Load(int4(b,tile,0)).r;
    return lerp(lerp(h00,h10,f.x),lerp(h01,h11,f.x),f.y);
}
#endif
