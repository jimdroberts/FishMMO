#ifndef FISHMMO_SURFACE_INCLUDED
#define FISHMMO_SURFACE_INCLUDED

// What the weather does to a surface: wet it, puddle it, ripple it, and cover it in snow, ash or
// sand. One function does the whole job — `FishWeatherSurface` — so a forked shader has a single
// line to add and every surface in the world agrees about what a storm looks like.
//
// Everything here reads the globals in FishWeather.hlsl, which WeatherShaderGlobals sets once a
// frame. Nothing here writes a global, samples a texture the caller does not own, or needs a
// per-material property: a material opts out by setting _FishWeatherAmount to 0, and that is all.

#include "FishWeather.hlsl"

// ── Where the weather reaches ──────────────────────────────────────────

// Rain does not fall through a roof, and snow does not settle under one: both ask the sky
// occlusion map whether this point can see the sky at all.
//
// A surface asks more gently than a raindrop does. The map is one height per texel, so a hard
// answer prints the map's own squares onto every rounded thing it covers — a tree's crown ends up
// with a blocky cap. Falling off over about a metre and a half keeps a roof sheltering what is
// under it while letting a curved surface shade its own cover smoothly.
//
// Nothing settles under the sea either: the bed holds no snow, ash or sand, and is not "wet" the way
// ground in the rain is. That holds past the map's edge, so it is applied outside the map's test.
float FishSurfaceExposure(float3 worldPos)
{
    float exposure = 1.0;
    if (_FishOcclusionRange.z > 0.5)
    {
        float2 uv = (worldPos.xz - _FishOcclusionRect.xy) / max(_FishOcclusionRect.zw, 1e-3);
        if (all(uv >= 0.0) && all(uv <= 1.0))
        {
            float top = FishSkyOcclusionGround(worldPos);
            exposure = saturate((worldPos.y - top) * 0.65 + 1.0);
        }
    }
    return min(exposure, FishWaterOpen(worldPos));
}

// How much of a flat-lying cover (snow, ash, sand) a surface holds: all of it on the level, none
// on a wall, and nothing underneath anything.
float FishCoverFacing(float3 worldNormal, float3 worldPos, float slopeBias)
{
    float facing = saturate((worldNormal.y - slopeBias) / max(0.05, 1.0 - slopeBias));
    // Steep ground sheds it; a smooth edge keeps the line from looking cut.
    return facing * facing * FishSurfaceExposure(worldPos);
}

// ── Noise ──────────────────────────────────────────────────────────────

// A number in [0, 1) from a point on the ground plane, by integer arithmetic. It was a sin() hash,
// and those lose their precision at the coordinates of a real scene — a few kilometres from the
// origin the argument is hundreds of thousands of radians — and start repeating in stripes and
// blocks, differently on every GPU. Resolved to a sixteenth, so the fractional salts the callers add
// still pick different values.
float FishSurfaceHash(float2 p)
{
    int2 q = int2(floor(p * 16.0));
    uint h = asuint(q.x) * 73856093u ^ asuint(q.y) * 19349663u;
    h ^= h >> 16;
    h *= 0x7feb352du;
    h ^= h >> 15;
    h *= 0x846ca68bu;
    h ^= h >> 16;
    return float(h & 0xFFFFFFu) / 16777216.0;
}

// Value noise on the ground plane, for puddle edges and the drift in a snow line.
float FishSurfaceNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = FishSurfaceHash(i);
    float b = FishSurfaceHash(i + float2(1, 0));
    float c = FishSurfaceHash(i + float2(0, 1));
    float d = FishSurfaceHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// ── Rain on water ──────────────────────────────────────────────────────

// Rings spreading from raindrops, as a normal in tangent-free world terms: the xz slope of the
// water film. Cheap on purpose — three cells of the same ring animation at different phases.
float2 FishRippleSlope(float3 worldPos, float time, float strength)
{
    if (strength <= 0.001)
    {
        return 0.0;
    }
    float2 slope = 0.0;
    UNITY_UNROLL
    for (int i = 0; i < 3; i++)
    {
        float scale = 1.7 + i * 1.3;
        // Each layer's grid turned its own way, so the three lattices never line up with one
        // another or with the world's axes.
        float turn = 0.6 + i * 1.1;
        float2 rotated = float2(worldPos.x * cos(turn) - worldPos.z * sin(turn), worldPos.x * sin(turn) + worldPos.z * cos(turn));
        float2 p = rotated * scale;
        float2 cell = floor(p);
        // One drop per cell, landing where IT lands and at its own moment. The drop used to land
        // dead centre in every cell, which put the rings on a perfect lattice — a pattern on the
        // ground wherever it rained, plain as a tiled floor.
        float seed = FishSurfaceHash(cell + i * 7.3);
        float2 landing = (float2(FishSurfaceHash(cell + 3.1 + i), FishSurfaceHash(cell + 9.7 + i)) - 0.5) * 0.8;
        float2 local = frac(p) - 0.5 - landing;
        float phase = frac(time * (0.9 + seed * 0.6) + seed);
        float distance = length(local);
        // Rings a little different in size and pitch from drop to drop.
        float pitch = 22.0 + seed * 14.0;
        float ring = sin((distance - phase) * pitch) * exp(-distance * (5.0 + seed * 3.0)) * (1.0 - phase);
        slope += normalize(local + 1e-4) * ring / scale;
    }
    return slope * strength * 0.35;
}

// ── The surface itself ─────────────────────────────────────────────────

/// <summary>What the weather leaves on a surface, applied in place.</summary>
/// albedo, smoothness, metallic and the world normal are changed; nothing else is touched.
/// `snowColour` is the cover's own colour: snow white, ash grey, sand tan, chosen by whichever
/// cover is deepest, because a world does not have two of them at once.
void FishWeatherSurface(float3 worldPos, inout half3 albedo, inout half3 normalWS, inout half smoothness, inout half metallic, half occlusion)
{
    float exposure = FishSurfaceExposure(worldPos);
    float time = _FishWeatherMisc.w;
    // What lies here, which is not the same as what the scene holds on average.
    float4 here = FishCoverAt(worldPos);

    // ── Wet ──
    // Water darkens what it soaks into and makes it shine. Cavities hold it: without a map of
    // them, the low-frequency noise stands in for where water gathers, and it only gathers where
    // the ground is something like level.
    float wet = saturate(here.y) * exposure * occlusion;
    if (wet > 0.001)
    {
        /* Water only stands on nearly level ground. This let it pool up to 45 degrees (a normal
         * pointing only a quarter up still scored), so wetness turned into puddles all over sloping
         * terrain. Now full on ground flatter than about eight degrees and none past twenty. A ramp
         * rather than a cut, because this is the shaded normal, bumped by the surface's own normal
         * map: a cut would speckle, a ramp only roughens the puddle's edge, as real ones are. */
        float level = smoothstep(0.93, 0.99, normalWS.y);
        float gather = FishSurfaceNoise(worldPos.xz * 0.35);
        float puddle = saturate((wet * 1.6 - 0.45 - gather * 0.8) * 3.0) * level;

        // Damp ground: darker, smoother, the same shape.
        albedo *= lerp(1.0, 0.62, wet * 0.85);
        smoothness = lerp(smoothness, 0.75, wet * 0.6);

        // A puddle is water, not wet ground: flat, mirror-smooth, and it hides what is under it.
        albedo = lerp(albedo, albedo * 0.35, puddle);
        smoothness = lerp(smoothness, 0.95, puddle);
        metallic = lerp(metallic, 0.0, puddle);
        float3 flat = normalize(lerp(normalWS, float3(0, 1, 0), puddle * 0.9));

        // Rain falling into standing water rings it. Only rain: hail bounces, snow settles, and
        // neither of them spreads rings across a puddle.
        float rain = saturate(_FishWeatherPrecip.x * _FishWeatherMix.x) * exposure;
        float2 slope = FishRippleSlope(worldPos, time, rain * puddle);
        flat = normalize(flat + float3(slope.x, 0.0, slope.y));
        normalWS = flat;
    }

    // ── Cover ──
    // Snow, ash and sand settle the same way and differ only in colour and in how they take light,
    // so one blend does all three, using whichever is deepest.
    float snow = saturate(here.x);
    float ash = saturate(here.z);
    float sand = saturate(here.w);
    float depth = max(snow, max(ash, sand));
    if (depth > 0.001)
    {
        half3 colour = half3(0.92, 0.94, 0.98);      // snow
        half coverSmoothness = 0.35;
        if (ash >= snow && ash >= sand)
        {
            colour = half3(0.22, 0.21, 0.20);        // ash
            coverSmoothness = 0.12;
        }
        else if (sand >= snow && sand >= ash)
        {
            colour = half3(0.76, 0.66, 0.45);        // sand
            coverSmoothness = 0.2;
        }
        // Deeper cover reaches further up a slope, and its edge wanders instead of ringing the
        // world at one height.
        float drift = FishSurfaceNoise(worldPos.xz * 0.6) * 0.25;
        float facing = FishCoverFacing(normalWS, worldPos, lerp(0.75, 0.15, saturate(depth + drift)));
        float amount = saturate(facing * (depth * 1.4 - 0.1));
        if (amount > 0.001)
        {
            albedo = lerp(albedo, colour, amount);
            smoothness = lerp(smoothness, coverSmoothness, amount);
            metallic = lerp(metallic, 0.0, amount);
            // Enough of it, and the shape underneath stops showing through.
            normalWS = normalize(lerp(normalWS, float3(0, 1, 0), amount * saturate(depth * 0.8)));
        }
    }
}

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
