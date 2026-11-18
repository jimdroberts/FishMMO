#ifndef FISHMMO_TERRAIN_SHADE_INCLUDED
#define FISHMMO_TERRAIN_SHADE_INCLUDED

// ── The terrain's shadow ────────────────────────────────────────────
//
// Where the ground shades the air over it from the light that leads the sky (TerrainShadowSolver): for
// each cell, the height under which a point there is in the shadow of some ground between it and the light
// — a mountain's evening shadow, kilometres long, which no shadow map reaches. Two grids: NEAR, 5 m to the
// cell over 800 m round the camera (MistGroundMap: SizeMeters / Resolution), for the ridge beside the valley
// the camera stands in; FAR, the scene's own grid of 384 cells a side over the whole of it (CloudFlowField),
// for a valley kilometres off. Each a height above its own base (m), with its own rect: xy the corner, z the
// size (m), w 1 once worked out.
//
// Read by the fog (FishFogLayer.hlsl: the cloud march, the mist, the fallbacks), and by the ground's lighting
// (FishGroundLighting.hlsl), where the shadow maps do not reach.
TEXTURE2D(_FishShadeNear);
SAMPLER(sampler_FishShadeNear);
float4 _FishShadeNearRect;
float _FishShadeNearBase;
TEXTURE2D(_FishShadeFar);
SAMPLER(sampler_FishShadeFar);
float4 _FishShadeFarRect;
float _FishShadeFarBase;

// Cells a side in each grid (MistGroundMap.Resolution, CloudFlowField.Resolution).
#define FISH_SHADE_NEAR_CELLS 160.0
#define FISH_SHADE_FAR_CELLS 384.0

/// How much of the light reaches a point, 0..1: the near grid where it covers the point (eased into the far one
/// over its outer tenth), the far one elsewhere, and 1 where neither does. Each grid's edge is given in metres as
/// (lit, shaded): fully lit while the shadow's top is at most at.y + lit, fully shaded once it reaches at.y + shaded.
float FishTerrainShade(float3 at, float2 nearEdge, float2 farEdge)
{
    float far = 1.0;
    float nearShare = 0.0;
    float near = 1.0;
    if (_FishShadeFarRect.w > 0.5)
    {
        float2 uv = (at.xz - _FishShadeFarRect.xy) / max(1.0, _FishShadeFarRect.z);
        if (all(uv > 0.0) && all(uv < 1.0))
        {
            float top = SAMPLE_TEXTURE2D_LOD(_FishShadeFar, sampler_FishShadeFar, uv, 0).r + _FishShadeFarBase;
            far = 1.0 - saturate((top - at.y - farEdge.x) / max(1e-3, farEdge.y - farEdge.x));
        }
    }
    if (_FishShadeNearRect.w > 0.5)
    {
        float2 uv = (at.xz - _FishShadeNearRect.xy) / max(1.0, _FishShadeNearRect.z);
        float2 toEdge = min(uv, 1.0 - uv);
        nearShare = saturate(min(toEdge.x, toEdge.y) / 0.1);
        if (nearShare > 0.0)
        {
            float top = SAMPLE_TEXTURE2D_LOD(_FishShadeNear, sampler_FishShadeNear, saturate(uv), 0).r + _FishShadeNearBase;
            near = 1.0 - saturate((top - at.y - nearEdge.x) / max(1e-3, nearEdge.y - nearEdge.x));
        }
    }
    return lerp(far, near, nearShare);
}

/// How much of the light reaches a point in the air, 0..1, soft over 6 m either side of the shadow's top near the
/// camera and 40 m far from it, so the fog's edge is not a line.
float FishTerrainSunlit(float3 at)
{
    return FishTerrainShade(at, float2(-3.0, 3.0), float2(-20.0, 20.0));
}

/// How much of the light reaches the ground itself, 0..1.
/// <remarks>
/// The field is built for the air, and the ground lies right at it. The walk starts a cell from the point, so flat
/// ground under a light at elevation e sits only cell·tan e above its own top — under a metre in a low moon — and
/// the air's edge, centred on the top, would take a third of the light off open ground. The ground between the
/// grid's points is also not the bilinear surface the field was walked over, by up to a share of a cell on a ridge
/// or in a gully. So the ground counts as lit until the top stands 0.4 of a cell above it (2 m in the near grid,
/// about 14 m in the far grid of a 5 km scene) and as shaded once it stands 1.2 cells above: only the deep shadow
/// of something between it and the light, the shadow the shadow maps cannot reach.
/// </remarks>
float FishGroundSunlit(float3 at)
{
    float nearCell = max(1.0, _FishShadeNearRect.z) / FISH_SHADE_NEAR_CELLS;
    float farCell = max(1.0, _FishShadeFarRect.z) / FISH_SHADE_FAR_CELLS;
    return FishTerrainShade(at, float2(0.4, 1.2) * nearCell, float2(0.4, 1.2) * farCell);
}

#endif
