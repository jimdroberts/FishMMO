#ifndef FISHMMO_TERRAIN_SNOW_HOLES_INCLUDED
#define FISHMMO_TERRAIN_SNOW_HOLES_INCLUDED

// How much of its snow lift a terrain vertex keeps near a hole in the terrain: none at a hole's edge, all of it
// TerrainSnowHoleFade.FadeMetres away, eased between.
//
// A hole is cut where something else is the ground (a carved cave's mouth, a canyon wall), and that rock meets the
// hole's edge flush. Lifted to the snow's full depth, the terrain's edge stood that far over the rock with nothing under
// it: a gap between the deep snow and the cave. The rock stays where it is; the snow comes down onto it over the last
// metres, and the rock's own cover (FishWeatherSurface) carries the white on from there.
//
// One byte per heightmap vertex (_FishSnowHoleFade, made and pushed per tile by TerrainArrayBinder), loaded at the vertex
// itself: every vertex's own exact distance to the nearest hole, its tile's or a neighbour's, so a vertex two patches or
// two tiles share is lifted alike at whatever level of detail draws it. A tile with no hole near it has a 1×1 map of
// full lift; nothing pushed (size 0) is full lift too. The backdrop is no terrain and has no holes.

#ifdef FISH_TERRAIN_GPU
// The client's heightmap draw: every tile's map that has one, a slice each (fishTerrainFadeSlice, -1 none).
TEXTURE2D(_FishSnowHoleFadeArray);
#else
TEXTURE2D(_FishSnowHoleFade);
#endif

float FishSnowHoleFade(float2 uv)
{
#if !defined(FISH_TERRAIN_BACKDROP)
    int2 size = int2(_FishSnowHoleFade_TexelSize.zw);
#ifdef FISH_TERRAIN_GPU
    UNITY_BRANCH
    if (size.x > 1 && fishTerrainFadeSlice >= 0)
    {
        int2 vertex = int2(round(uv * (float2)(size - 1)));
        return _FishSnowHoleFadeArray.Load(int3(clamp(vertex, int2(0, 0), size - 1), 0)).r;
    }
#else
    UNITY_BRANCH
    if (size.x > 1)
    {
        int2 vertex = int2(round(uv * (float2)(size - 1)));
        return _FishSnowHoleFade.Load(int3(clamp(vertex, int2(0, 0), size - 1), 0)).r;
    }
#endif
#endif
    return 1.0;
}

#endif
