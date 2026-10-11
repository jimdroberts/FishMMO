// FishMMO: what years do to a building (the structure kit's materials, _FishStructure = 1).
//
// The kit's surfaces are clean tiles: every house the same white, every wall foot meeting the grass in a ruled line,
// nothing ever rained on. This lays on what weather and use leave, from the surface's own world position and facing:
//   * each building its own a little: a limewash warmer or cooler, a timber darker or paler (from its origin);
//   * the foot of a wall damp and splashed with soil, darkest at the ground;
//   * grime rising from the ground and fading by head height;
//   * rain streaks down the walls;
//   * moss on what faces the sky (roofs, sills, wall tops) and on north faces, in patches.
// Pure functions of position and normal: no textures, no per-material inputs but the flag.

#ifndef FISHMMO_STRUCTURE_WEATHERING_INCLUDED
#define FISHMMO_STRUCTURE_WEATHERING_INCLUDED

float FishWearHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

float FishWearNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(FishWearHash(i), FishWearHash(i + float2(1.0, 0.0)), u.x),
                lerp(FishWearHash(i + float2(0.0, 1.0)), FishWearHash(i + float2(1.0, 1.0)), u.x), u.y);
}

/// <summary>
/// Weathers one pixel of a structure. heightAboveGround is the pixel's height over the terrain (FishContactHeight, metres;
/// very large where no terrain is known), originWS the instance's root (each building's own variation), normalWS the
/// surface's geometric normal.
/// </summary>
void FishStructureWeather(float3 positionWS, half3 normalWS, float heightAboveGround, float3 originWS,
    inout half3 albedo, inout half smoothness, inout half occlusion)
{
    half3 n = normalize(normalWS);
    half up = n.y;
    // How much the surface is a wall (vertical) rather than a roof or a floor.
    half wall = (half)(1.0 - saturate(abs(up) * 2.5));

    // Each building its own: warmer or cooler, lighter or darker, by a few percent.
    float2 seed = originWS.xz * 0.173 + originWS.y * 0.01;
    half warmth = (half)FishWearHash(seed);
    half value = (half)FishWearHash(seed + 17.31);
    albedo *= lerp(half3(0.95h, 0.97h, 1.02h), half3(1.05h, 1.0h, 0.93h), warmth) * lerp(0.86h, 1.08h, value);

    float2 along = positionWS.xz + positionWS.y * 0.37;
    half grain = (half)FishWearNoise(along * 1.3);
    half fine = (half)FishWearNoise(along * 5.1 + 3.7);

    // Grime from the ground: feet, splash and smoke-stained hands, gone by head height.
    half grime = (half)(1.0 - smoothstep(0.0, 1.8, heightAboveGround)) * wall;
    albedo *= lerp(1.0h, 0.74h + 0.14h * grain, grime * 0.55h);

    // The foot: soil splashed up the wall and the damp that stays in it.
    half splash = (half)(1.0 - smoothstep(0.0, 0.55, heightAboveGround)) * (0.65h + 0.35h * fine);
    albedo = lerp(albedo, albedo * half3(0.6h, 0.5h, 0.4h), splash * 0.75h * max(wall, 0.4h));
    smoothness = lerp(smoothness, max(smoothness, 0.3h), splash * 0.45h);
    occlusion *= 1.0h - splash * 0.2h;

    // Rain streaks: dark runs down a wall, narrow across it and long down it.
    float2 across = normalize(float2(-n.z, n.x) + 1e-4);
    float run = dot(positionWS.xz, across);
    half streak = (half)smoothstep(0.62, 0.92, FishWearNoise(float2(run * 6.0, positionWS.y * 0.45)))
                * (half)(0.5 + 0.5 * FishWearNoise(float2(run * 1.1, positionWS.y * 0.2 + 9.0)));
    albedo *= 1.0h - streak * wall * 0.22h;

    // Moss where the sky falls (roofs, sills, wall tops) and on north faces, in patches.
    half skyward = (half)saturate(up * 1.6 - 0.25);
    half north = (half)saturate(n.z) * wall;
    half patches = (half)smoothstep(0.42, 0.8, FishWearNoise(positionWS.xz * 0.55 + positionWS.y * 0.3) * 0.6
                                              + FishWearNoise(positionWS.xz * 2.9 + 11.0) * 0.4);
    half moss = saturate(skyward * 0.85h + north * 0.35h) * patches;
    half3 mossColour = half3(0.2h, 0.27h, 0.1h) * (0.8h + 0.4h * fine);
    albedo = lerp(albedo, mossColour, moss * 0.7h);
    smoothness = lerp(smoothness, 0.08h, moss * 0.6h);
}

// 0 by day, 1 from dusk (SkySystem, from the main sun's height): when lamps are lit and windows glow.
float _FishNightLights;

/// <summary>
/// A pane's light at night (a material marked _FishStructure = 2): warm, in most of a building's windows and not all of
/// them (its origin decides which), a little uneven pane to pane as a fire or a candle is. Zero by day.
/// </summary>
half3 FishStructureGlow(float3 positionWS, float3 originWS)
{
    half night = (half)saturate(_FishNightLights);
    if (night <= 0.0h)
    {
        return half3(0.0h, 0.0h, 0.0h);
    }
    // Whether this building is lived in tonight, and this pane's own warmth.
    half home = (half)step(FishWearHash(originWS.xz * 0.137 + 5.3), 0.75);
    half pane = (half)FishWearHash(floor(positionWS.xz * 1.7) + floor(positionWS.y * 1.3));
    half3 warm = lerp(half3(1.0h, 0.55h, 0.22h), half3(1.0h, 0.72h, 0.4h), pane);
    return warm * (1.6h + 1.2h * pane) * night * home;
}

#endif
