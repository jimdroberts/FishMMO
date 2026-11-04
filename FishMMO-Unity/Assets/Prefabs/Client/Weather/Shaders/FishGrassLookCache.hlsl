#ifndef FISH_GRASS_LOOK_CACHE_INCLUDED
#define FISH_GRASS_LOOK_CACHE_INCLUDED
#include "FishFlora.hlsl"
// The record's extra word keeps its type bit (0). Bits 1..8 cache the palette biome,
// 9..12 its variant, and 13 marks a valid cache. Bits 14..17 hold the flower variant
// when bit 18 is set. No extra storage or quantized shape data.
#define FISH_GRASS_LOOK_CACHED (1u<<13)
uint FishGrassPackLook(uint more, FishFloraLook look)
{
    more &= ~((255u<<1)|(15u<<9)|FISH_GRASS_LOOK_CACHED);
    if (look.valid) more |= FISH_GRASS_LOOK_CACHED | ((look.biome & 255u)<<1) | ((look.variant & 15u)<<9);
    return more;
}
uint FishGrassPackBloom(uint more,float2 xz,FishFloraLook look)
{
    more &= ~((15u<<14)|(1u<<18));
    if (look.valid) more |= (1u<<18) | ((FishFloraVariantAt(xz,look.biome,11.0,0xBB67AE85u)&15u)<<14);
    return more;
}
FishFloraLook FishGrassReadBloom(uint more,float2 xz)
{
    FishFloraLook look = (FishFloraLook)0;
    [branch] if ((more & (1u<<18)) != 0u && _FishFloraInfo.x >= 0.5)
        look = FishFloraLookOf((more>>1)&255u,(more>>14)&15u);
    else
        look = FishFloraAt(xz,11.0,0xBB67AE85u);
    return look;
}

FishFloraLook FishGrassReadLook(uint more,float2 xz)
{
    FishFloraLook look = (FishFloraLook)0;
    [branch] if ((more & FISH_GRASS_LOOK_CACHED) != 0u && _FishFloraInfo.x >= 0.5)
        look = FishFloraLookOf((more>>1)&255u,(more>>9)&15u);
    else
        look = FishFloraAt(xz,FISH_FLORA_GRASS_PATCH,FISH_FLORA_GRASS_SALT);
    return look;
}
#endif
