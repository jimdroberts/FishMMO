#ifndef FISHMMO_SEA_INCLUDED
#define FISHMMO_SEA_INCLUDED

// The sea as what lives in it sees it: where the surface is, and the surge that rocks everything under it.
// Published every frame by SeaLifeSystem (FishMMO.Client/World/SeaLife) for the camera's scene:
//
//   _FishSea       x the sea's surface now (tide included), y the lowest it falls (low tide, less the
//                  swell's troughs), z 1 while there is a sea (0: none — every global reads zero until
//                  set, and a sea at y = 0 is a real sea), w the shared clock: world seconds folded to
//                  0 .. 256 on the CPU (SeaLifeClock), so it never loses float precision and every
//                  player's plants rock in step.
//   _FishSeaSurge  xy the direction the swell runs (unit), z how hard it surges (0 .. 1, by the wave
//                  height), w unused.
//
// Read by the sea-floor plants (FishGrassBlades.hlsl for seagrass and seaweed, FishVegetationPasses.hlsl
// for kelp and the rest with _Aquatic set) and by the swimming creatures (FishSeaLife.shader).

float4 _FishSea;
float4 _FishSeaSurge;

/// <summary>The clock's fold, seconds: every frequency below is a whole number of cycles in it, so the fold is seamless.</summary>
#define FISH_SEA_PERIOD 256.0

bool FishSeaPresent()
{
    return _FishSea.z > 0.5;
}

/// <summary>The highest a plant may reach and stay under water at every tide, metres (world y).</summary>
float FishSeaCeiling()
{
    return _FishSea.y;
}

/// <summary>
/// The horizontal sway the sea gives a point at a world position: the swell's surge rocking back and
/// forth along its run in slow strokes (about 11 s, under a weaker 7 s chop) that travel across the sea
/// floor, a slow wobble across it, and a little drift one way, so the stroke is not a metronome. Strongest
/// in the shallows and dying with depth (a wave's orbit shrinks as exp(-k·depth)), never quite still.
/// About 0.6 at full surge in the shallows; zero with no sea.
/// <para>
/// Slow and short on purpose (Jim, 2026-10-06: "animated too fast"). How fast a frond seems to move is its stroke
/// times its rate: at an 8 s swell, a 5 s chop and a unit stroke a tall kelp's tip swept metres every few seconds.
/// Real kelp in a surge leans a metre or two over ten seconds and more.
/// </para>
/// </summary>
/// <param name="lag">A per-plant phase delay in cycles, so neighbours do not move in lockstep.</param>
float2 FishSeaSurge(float3 positionWS, float lag)
{
    if (!FishSeaPresent())
    {
        return float2(0.0, 0.0);
    }
    float2 dir = _FishSeaSurge.xy;
    dir = dot(dir, dir) > 1e-6 ? normalize(dir) : float2(0.8, 0.6);
    float2 across = float2(-dir.y, dir.x);
    float depth = max(0.0, _FishSea.x - positionWS.y);
    float reach = lerp(0.25, 1.0, exp(-depth / 12.0));
    float t = _FishSea.w / FISH_SEA_PERIOD;
    float along = dot(positionWS.xz, dir);
    float swell = sin(6.2831853 * (t * 23.0 - along / 80.0 - lag));
    float chop = sin(6.2831853 * (t * 37.0 - along / 50.0 - lag * 1.7) + 1.3);
    float wobble = sin(6.2831853 * (t * 15.0 + dot(positionWS.xz, across) / 60.0 + lag));
    float strength = saturate(_FishSeaSurge.z) * reach * 0.6;
    return (dir * (0.8 * swell + 0.2 * chop + 0.25) + across * (0.2 * wobble)) * strength;
}

/// <summary>
/// A flutter at a point, -1 .. 1: a frond's edge rippling in moving water (about 4 s; water is thick, and the 1.6 s it was read as a shiver in air).
/// </summary>
float FishSeaFlutter(float3 positionWS, float lag)
{
    float t = _FishSea.w / FISH_SEA_PERIOD;
    return sin(6.2831853 * (t * 64.0 + lag) + dot(positionWS, float3(0.9, 1.2, 0.6)));
}

/// <summary>
/// Keeps a point under the surface: below the ceiling less <paramref name="soft"/> metres it is left alone;
/// above that it is eased toward the ceiling and never reaches it. Returns how far it was lowered, so the
/// caller can lay that length along the surface instead (kelp's canopy spreading under the water).
/// </summary>
float FishSeaKeepUnder(inout float3 positionWS, float soft)
{
    if (!FishSeaPresent())
    {
        return 0.0;
    }
    float s = max(0.05, soft);
    float start = FishSeaCeiling() - s;
    if (positionWS.y <= start)
    {
        return 0.0;
    }
    float y = start + s * (1.0 - exp(-(positionWS.y - start) / s));
    float lowered = positionWS.y - y;
    positionWS.y = y;
    return lowered;
}

#endif
