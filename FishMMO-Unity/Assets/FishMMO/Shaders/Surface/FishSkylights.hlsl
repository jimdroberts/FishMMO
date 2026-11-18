#ifndef FISH_SKYLIGHTS_INCLUDED
#define FISH_SKYLIGHTS_INCLUDED

// Openings in caves' roofs up to the sky round the camera (2026-10-20, caves livened): the nearest few skylights
// (SceneEmitterKind.Skylight) that CaveSkylights hands the shaders each frame. Deep in a cave the lit surfaces take no sky
// and no sun (FishUnderground, FishGroundMap.hlsl); under an opening they see some of the sky, and where a line toward
// the sun leaves through the opening the sun's own light is kept (the shadow map — the terrain's, its hole clipped, and the
// rock round the opening, which casts — then lights exactly the patch it reaches). These two functions mirror
// SceneSkylight.SkyShare and SceneSkylight.InBeam line for line; change both together.

#define FISH_SKYLIGHTS 4
// The sky's share of a disc's view factor is lifted by this (the light the throat's sunlit walls throw down), and falls this
// much faster off the opening's axis (the rock round it hides it).
#define FISH_SKYLIGHT_SKY_BOOST 3.0
#define FISH_SKYLIGHT_SKY_OFF_AXIS 4.0
// Where a beam's edge fades, as shares of the opening's radius; the least height of the sun (its direction's y) for any beam.
#define FISH_SKYLIGHT_BEAM_INNER 0.8
#define FISH_SKYLIGHT_BEAM_OUTER 1.15
#define FISH_SKYLIGHT_LOWEST_SUN 0.02

// xyz the opening's middle on the ground (world), w its radius (0 for an empty slot).
float4 _FishSkylights[FISH_SKYLIGHTS];
// How many slots are set (0: none near, and nothing below costs more than this test).
float _FishSkylightCount;

/// How much of the sky a point sees through the nearest openings, 0..1 (SceneSkylight.SkyShare): all of it in an opening,
/// the boosted share of a disc of its radius under it, falling off away from under it; nothing over one.
float FishSkylightSky(float3 positionWS)
{
    float share = 0.0;
    [unroll]
    for (int i = 0; i < FISH_SKYLIGHTS; i++)
    {
        float4 s = _FishSkylights[i];
        float below = s.y - positionWS.y;
        if ((float)i < _FishSkylightCount && s.w > 0.0 && below >= 0.0)
        {
            float off = max(0.0, length(positionWS.xz - s.xz) - s.w);
            float r2 = s.w * s.w;
            float f = r2 / (r2 + below * below + FISH_SKYLIGHT_SKY_OFF_AXIS * off * off);
            share = max(share, min(1.0, FISH_SKYLIGHT_SKY_BOOST * f));
        }
    }
    return share;
}

/// Whether the sun (toward it: `toSun`, unit) falls on a point through one of the nearest openings, 0..1
/// (SceneSkylight.InBeam): a line from the point toward it leaves through the opening, soft at the edge.
float FishSkylightSun(float3 positionWS, float3 toSun)
{
    float lit = 0.0;
    if (toSun.y > FISH_SKYLIGHT_LOWEST_SUN)
    {
        [unroll]
        for (int i = 0; i < FISH_SKYLIGHTS; i++)
        {
            float4 s = _FishSkylights[i];
            float below = s.y - positionWS.y;
            if ((float)i < _FishSkylightCount && s.w > 0.0 && below >= 0.0)
            {
                float2 q = positionWS.xz + toSun.xz * (below / toSun.y) - s.xz;
                lit = max(lit, 1.0 - smoothstep(FISH_SKYLIGHT_BEAM_INNER * s.w, FISH_SKYLIGHT_BEAM_OUTER * s.w, length(q)));
            }
        }
    }
    return lit;
}

#endif
