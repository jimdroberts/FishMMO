#ifndef FISHMMO_DITHER_INCLUDED
#define FISHMMO_DITHER_INCLUDED

// Core (Phase 4 of the core and modules plan, 2026-11-17): the screen-door pattern every dissolve in the world uses — the
// day/night fade (FishSurface.hlsl) and the LOD crossfade (FishLodFade.hlsl) — so the LOD fade needs no world module.

// ── Dithered dissolve ──────────────────────────────────────────────────

// The day/night fade: objects appear and disappear through a screen-door pattern instead of
// popping. A 4×4 Bayer matrix, which is stable under temporal antialiasing and costs nothing.
float FishDitherThreshold(float2 positionSS)
{
    float2 p = fmod(positionSS, 4.0);
    const float bayer[16] =
    {
         0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
        12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
         3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
        15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0,
    };
    int index = (int)(p.y) * 4 + (int)(p.x);
    return bayer[index];
}

// Clips the pixel when the object is more dissolved than this pixel's place in the pattern.
void FishDitherClip(float2 positionSS, float visible)
{
    if (visible < 0.999)
    {
        clip(visible - FishDitherThreshold(positionSS) - 0.0001);
    }
}

#endif
