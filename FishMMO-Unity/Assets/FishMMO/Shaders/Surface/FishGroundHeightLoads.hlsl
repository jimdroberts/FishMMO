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

// The same for the ground heights, which may be 16 bits packed into two bytes (packed: red the low byte, green the high, 0..65535
// for 0..1; GroundMaterialMap.HeightsFormat, Firefox's WebGPU, which cannot sample r16unorm). Each texel is decoded before
// the blend: a blend of packed bytes is no height. Unpacked, a texel's red is the height as it is.
float FishGroundHeightTexel(Texture2DArray<float4> heights, int4 at, bool packed)
{
    float4 c = heights.Load(at);
    return packed ? (round(c.r * 255.0) + round(c.g * 255.0) * 256.0) * (1.0 / 65535.0) : c.r;
}

float FishGroundHeightLoadsPacked(Texture2DArray<float4> heights, int size, int tile, float2 t, bool packed)
{
    size = max(1,size);
    float2 p = saturate(t) * (float)(size - 1);
    int2 a = (int2)floor(p), b = min(a + 1, size - 1);
    float2 f = p - (float2)a;
    float h00 = FishGroundHeightTexel(heights, int4(a,tile,0), packed);
    float h10 = FishGroundHeightTexel(heights, int4(b.x,a.y,tile,0), packed);
    float h01 = FishGroundHeightTexel(heights, int4(a.x,b.y,tile,0), packed);
    float h11 = FishGroundHeightTexel(heights, int4(b,tile,0), packed);
    return lerp(lerp(h00,h10,f.x),lerp(h01,h11,f.x),f.y);
}
#endif
