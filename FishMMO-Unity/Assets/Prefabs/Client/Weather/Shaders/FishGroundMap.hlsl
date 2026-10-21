#ifndef FISH_GROUND_MAP_INCLUDED
#define FISH_GROUND_MAP_INCLUDED

// The openings in caves' roofs round the camera: under one, some of the sky comes back (FishUnderground below).
#include "FishSkylights.hlsl"

// The fine ground round the camera (MistGroundMap): 160 cells over 800 m, read by the mist (FishMist.hlsl), the
// air motes (FishAirMotes.shader) and the lit surfaces' underground darkness (FishUnderground). One declaration,
// so any of them can be included together.

TEXTURE2D(_FishMistGround);
// A shader near D3D11's sixteen samplers (the lit pass) defines FISH_GROUND_MAP_SAMPLER as an inline bilinear sampler
// it already declares (sampler_LinearClamp) before including this. Not another texture's own sampler_<Texture>: the
// compiler refuses one in a program that does not read that texture.
#ifndef FISH_GROUND_MAP_SAMPLER
SAMPLER(sampler_FishMistGround);
#define FISH_GROUND_MAP_SAMPLER sampler_FishMistGround
#endif
// xy the fine ground map's corner, z its size (m), w 1 once it is read (2 over a cloud world, where nothing is
// underground: MistGroundMap, FishUnderground). r the ground above
// _FishMistGroundBase (m; over open water, the water's surface), g the hollow (m, + in a dip), b how near open
// water is (0..1), a the canopy (0..1).
float4 _FishMistGroundRect;
float _FishMistGroundBase;

/// The ground under a point: x its height (m), y its hollow (m), z how near open water (0..1), w the canopy
/// over it (0..1). `covered` is false outside the map.
float4 FishMistGroundAt(float2 xz, out bool covered)
{
    covered = false;
    if (_FishMistGroundRect.w < 0.5)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float2 uv = (xz - _FishMistGroundRect.xy) / max(1.0, _FishMistGroundRect.z);
    if (any(uv <= 0.0) || any(uv >= 1.0))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    covered = true;
    float4 g = SAMPLE_TEXTURE2D_LOD(_FishMistGround, FISH_GROUND_MAP_SAMPLER, uv, 0);
    return float4(g.x + _FishMistGroundBase, g.yzw);
}

// The live caves' columns round the camera (CaveEnclosureMap, 2026-10-21): per column of the world's FISH_CAVE_CELL grid,
// r its floor and g its roof (world y: the rock's inner faces' lowest and highest), b how shut in from the daylight it is,
// a 1 where it holds cave rock. Read texel by texel, no sampler. xy the window's first column, z its columns across, w 1
// when any cave's columns are in it.
TEXTURE2D_FLOAT(_FishCaveEnclosure);
float4 _FishCaveEnclosureRect;

// CaveEnclosure.Cell, .Margin and .FloorMargin.
#define FISH_CAVE_CELL 2.5
#define FISH_CAVE_MARGIN 0.5
#define FISH_CAVE_FLOOR_MARGIN 2.0

/// How much a height is within a column's span, 0..1 (CaveEnclosure.Gate).
float FishCaveGate(float y, float floorY, float roofY)
{
    return smoothstep(floorY - 2.0 * FISH_CAVE_FLOOR_MARGIN, floorY - FISH_CAVE_FLOOR_MARGIN, y)
        * (1.0 - smoothstep(roofY + FISH_CAVE_MARGIN, roofY + 2.0 * FISH_CAVE_MARGIN, y));
}

/// How far in a cave a point is, 0..1 (CaveEnclosure.At, line for line): the four columns round it filtered by their
/// closure, only those holding cave rock, gated by the most open of their floor-to-roof spans. In a cave's air, however
/// thin the rock over it, a surface is underground; on the hillside over it, metres above the roof, it is not.
half FishCaveEnclosed(float3 positionWS)
{
    if (_FishCaveEnclosureRect.w < 0.5)
    {
        return 0.0h;
    }
    float2 u = positionWS.xz / FISH_CAVE_CELL - 0.5;
    float2 i0 = floor(u);
    float2 f = u - i0;
    int2 first = (int2)(i0 - _FishCaveEnclosureRect.xy);
    int n = (int)_FishCaveEnclosureRect.z;
    float sumB = 0.0;
    float sumW = 0.0;
    float gate = 0.0;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        int2 o = int2(k & 1, k >> 1);
        int2 c = first + o;
        if (all(c >= 0) && all(c < n))
        {
            float4 t = LOAD_TEXTURE2D_LOD(_FishCaveEnclosure, c, 0);
            if (t.a > 0.5)
            {
                float b = (o.x == 1 ? f.x : 1.0 - f.x) * (o.y == 1 ? f.y : 1.0 - f.y);
                sumB += b;
                sumW += b * t.b;
                gate = max(gate, FishCaveGate(positionWS.y, t.r, t.g));
            }
        }
    }
    return sumB > 1e-5 ? (half)(sumW / sumB * gate) : 0.0h;
}

/// How far underground a point is, 0..1: 0 at the surface and within a few metres under it (the map is 5 m cells,
/// so a cliff's face or a cave's mouth may read a few metres under its own ground), 1 from some twenty metres in.
/// A carved cave's walls deep in take no sky: no ambient, no reflections (the terrain over them shadows the sun).
half FishUnderground(float3 positionWS)
{
    // Over a cloud world (w 2) nothing is: under a floating island's ground is its underside, open to the air.
    bool covered;
    float4 ground = FishMistGroundAt(positionWS.xz, covered);
    half under = covered && _FishMistGroundRect.w < 1.5 ? (half)smoothstep(6.0, 20.0, ground.x - positionWS.y) : 0.0h;
    // In a cave's air however thin the rock over it (2026-10-21): a hall under a hillside that falls away took the sky and,
    // past the shadow distance, the sun on its far walls.
    under = max(under, FishCaveEnclosed(positionWS));
    // Under an opening in a cave's roof (2026-10-20, FishSkylights.hlsl): the share of the sky it lets in is not underground.
    if (under > 0.0h && _FishSkylightCount > 0.5)
    {
        under *= (half)(1.0 - FishSkylightSky(positionWS));
    }
    return under;
}

/// How much of a full-screen effect over the air (fog, clouds, mist, god rays) a pixel keeps, 0..1: none where the eye
/// and the surface it looks at are both deep underground (in a cave the air is the cave's, not the weather's), all of
/// it wherever either is in the open — the sky seen out of a cave's mouth, or a mouth seen from outside.
float FishOpenAir(float3 camera, float3 surface)
{
    return 1.0 - min((float)FishUnderground(camera), (float)FishUnderground(surface));
}

/// A premultiplied over-the-scene value (rgb added, a the transmittance kept) faded by FishOpenAir's share.
float4 FishOpenAirFade(float4 value, float open)
{
    return float4(value.rgb * open, lerp(1.0, value.a, open));
}

#endif
