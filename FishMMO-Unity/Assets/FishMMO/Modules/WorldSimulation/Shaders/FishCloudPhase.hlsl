#ifndef FISH_CLOUD_PHASE_INCLUDED
#define FISH_CLOUD_PHASE_INCLUDED

// Where each marched ray starts along its first step (FishClouds.shader passes 0 and 10). Its own file so the
// cloud library (FishCloudVolume.hlsl, which the bake compute inlines) is not touched by it.

// x the frame, wrapped at 4096; y the frame's phase (CloudOptions.JitterPhase: the sub-texel place's Bayer value
// over sixteen plus the golden ratio per cycle); z 1 when _FishCloudBlueNoise is bound (FishCloudsFeature).
float4 _FishCloudPhaseState;
// URP's 64 × 64 blue noise (UniversalRenderPipelineRuntimeTextures.blueNoise64LTex). Imported as Alpha8: the value
// is in .a, and .r reads 0.
TEXTURE2D(_FishCloudBlueNoise);

/// Interleaved gradient noise (Jimenez 2014): spreads its values evenly over every three-by-three block of whole
/// pixels. Only the fallback for when the blue noise is not bound: see FishCloudRayPhase.
float FishCloudIgn(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

/// <summary>
/// Where along its first step a march texel's ray starts this frame, 0..1: a blue-noise value fixed to the texel,
/// turned on by the frame's phase.
/// </summary>
/// <remarks>
/// A rebuilt pixel keeps whatever error its rays' phases fail to average out, and that error is drawn in the
/// pattern the per-texel part of the phase makes. With interleaved gradient noise that pattern is a lattice, and
/// it does not matter how the noise is "moved": the noise is linear in the pixel inside its frac, so the pixel
/// shifted by any offset is the same pattern plus one constant (IGN(p + o) − IGN(p) varies by 0.017 over the
/// screen). Moving it by 5.588238 a frame (2026-10-09, morning) only re-rolled a phase for the whole frame; the
/// lattice came back as soon as the camera stood still long enough for the steadying to settle.
///
/// Blue noise has no lattice to leave, and is nearly as evenly spread over a neighbourhood, which is what the
/// steadying's clip box is sized from. Kept FIXED to the texel, with the frame's phase on top, every texel's own
/// phases stay the place-ordered golden sequence: evenly spread in time, so they average away. Moving the tile
/// instead (by a random offset a frame) makes each texel's phases random in time: measured that way this morning
/// as a blotchy grain 30 % stronger and twice the flicker. On a CPU copy of the march, rebuild, clip and moving
/// average (an overcast base, a ray error of ±0.1, 384 frames still), against the shipped phase: the weave's
/// strongest frequency 220 000 → 2 300 times the band's median (white noise: 400), grain 1.20 → 1.35 ×10⁻³,
/// flicker 0.57 → 0.69 ×10⁻³. The same tile turned by the golden ratio alone, not by the place: grain 1.68.
/// </remarks>
float FishCloudRayPhase(float2 texel)
{
    if (_FishCloudPhaseState.z < 0.5)
    {
        return frac(FishCloudIgn(texel) + _FishCloudPhaseState.y);
    }
    float tile = LOAD_TEXTURE2D(_FishCloudBlueNoise, uint2(texel) & 63u).a;
    return frac(tile + _FishCloudPhaseState.y);
}

#endif
