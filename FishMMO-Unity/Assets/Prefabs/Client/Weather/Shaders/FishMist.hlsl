#ifndef FISH_MIST_INCLUDED
#define FISH_MIST_INCLUDED

// Mist in patches near the ground (FishMMO.Shared.Weather.GroundMist): wisps a couple of metres across up
// to banks of tens of metres, lying a few metres to a few tens deep, wherever the air just over the
// ground is brought to saturation. The fog layer (FishFogLayer.hlsl) is the weather's fog, one layer over
// the scene with banks hundreds of metres across; this is what a humid place does on its own — a rain
// forest's floor, a river at dawn, wet ground after a shower — scattered, local and small.
//
// HOW READY THE AIR IS: the CPU says how far short of saturation the air over open ground stays
// (GroundMist.Deficit: the dew-point spread at the ground, less the night's radiative chill and what wet
// ground and falling rain breathe into the air) and how much a wind leaves (GroundMist.Stirred). Each spot
// then closes that gap on its own (FishMistReadiness, the twin of GroundMist.LocalClosing): a hollow pools
// the night's chilled air, open water breathes into the air over and beside it, a tree canopy transpires
// and keeps the floor under it shaded and still. So a lake shore or a forest holds mist on an evening the
// open fields beside it stay clear. WHERE within that the mist stands is the noise:
//   - the patches, a field of every scale from tens of metres down to two: one read of the shape volume
//     on a 200 m tile (its Perlin–Worley channel gives patches some thirty metres across, its Worley
//     octaves blobs of twenty-five, twelve and six) and one of the detail volume on a 25 m tile (blobs of
//     six, three and one and a half metres) — each channel taken in standard units, from its measured
//     mean and spread, so the cut is a share of the ground: about one part in fifty at the onset, and
//     most of it at saturation;
//   - and free wisps, a field of the finest octaves alone, so a few shreds two or three metres across
//     stand out on their own beside the patches.
// All of it carried on the fog's wind and steered round the terrain the fog's air cannot climb
// (FishFlowAround), turning over as it goes: the wisps several times faster than the patches.
//
// DRAWN BY ITS OWN SHORT MARCH, beside the clouds' (FishClouds.shader pass 0): FISH_MIST_STEPS steps
// spaced quadratically to `range` — a metre or so apart at the camera, where a two-metre wisp has to be
// seen, tens of metres by the end, where only patches are left (the finer octaves fade with the step
// that samples them). Composited in front of the clouds' march: what stands nearer than a few hundred
// metres along a ray at ground level is the mist; the fog layer it shares that stretch with is thin
// against it over such a distance, and the error of ordering the two is a fraction of a per cent.
// Lit as the fog is (FishFogLight), under whatever fog lies over it — and out of the light wherever the
// terrain shades it (FishTerrainSunlit, FishFogLayer.hlsl: a mountain's evening shadow kilometres long).

TEXTURE2D(_FishMistGround);
SAMPLER(sampler_FishMistGround);
// xy the fine ground map's corner, z its size (m), w 1 once it is read (MistGroundMap). r the ground above
// _FishMistGroundBase (m), g the hollow (m, + in a dip), b how near open water is (0..1), a the canopy (0..1).
float4 _FishMistGroundRect;
float _FishMistGroundBase;
// x how far short of saturation the air over open ground stays (K, GroundMist.Deficit), y how much of the
// mist the wind leaves (0..1), z how much the night chills the ground (0..1, GroundMist.NightCalm), w how
// far the mist is drawn (m).
float4 _FishMist;

// Twins of GroundMist's: change one, change the other.
#define FISH_MIST_ONSET 2.5
#define FISH_MIST_HOLLOW_COOLING 1.5
#define FISH_MIST_FULL_HOLLOW 4.0
#define FISH_MIST_WATER_MOISTURE 2.0
#define FISH_MIST_CANOPY_MOISTURE 1.5
#define FISH_MIST_THIN_VISIBILITY 900.0
#define FISH_MIST_THICK_VISIBILITY 150.0
#define FISH_MIST_DEEP 40.0

#define FISH_MIST_STEPS 16
// The tiles, m: both divide the fog's drift period (1600 m, FogLayerView.BankTile), so the wrap of the
// drift is a whole number of tiles.
#define FISH_MIST_PATCH_TILE 200.0
#define FISH_MIST_WISP_TILE 25.0
// Stratified: a tile stands this share of its width.
#define FISH_MIST_RISE 0.3
// The channels as baked, measured off the volumes (mip 0, every texel): the shape volume's Perlin–Worley,
// and every Worley octave in either volume, which all came out alike.
#define FISH_MIST_PW_MEAN 0.667
#define FISH_MIST_PW_SPREAD 0.046
#define FISH_MIST_WORLEY_MEAN 0.476
#define FISH_MIST_WORLEY_SPREAD 0.119
// The standard spread of the patch field's terms added together (√ of the squared weights below).
#define FISH_MIST_PATCH_NORM 0.84
#define FISH_MIST_WISP_NORM 0.95
// The shallowest a patch lies, m (GroundMist.ShallowDepth).
#define FISH_MIST_SHALLOW 3.0

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
    float4 g = SAMPLE_TEXTURE2D_LOD(_FishMistGround, sampler_FishMistGround, uv, 0);
    return float4(g.x + _FishMistGroundBase, g.yzw);
}

/// How ready the air at a spot is, 0..1: the open ground's deficit, closed further by the spot's hollow,
/// water and canopy (GroundMist.LocalClosing), then the onset curve and the wind (GroundMist.Local).
float FishMistReadiness(float4 ground)
{
    float night = _FishMist.z;
    float closing = FISH_MIST_HOLLOW_COOLING * saturate(ground.y / FISH_MIST_FULL_HOLLOW) * night
        + FISH_MIST_WATER_MOISTURE * saturate(ground.z) * lerp(0.5, 1.0, night)
        + FISH_MIST_CANOPY_MOISTURE * saturate(ground.w);
    float deficit = _FishMist.x - closing;
    return smoothstep(0.0, 1.0, saturate((FISH_MIST_ONSET - deficit) / FISH_MIST_ONSET)) * _FishMist.y;
}



/// The mist's extinction at a point (1/m), `above` metres over the ground, where the air is `potential`
/// ready (FishMistReadiness), for a sample that stands for `footprint` metres along the ray.
float FishMistDensity(float3 at, float above, float potential, float footprint)
{
    // How deep and how thick the mist this ready stands (GroundMist.Depth, GroundMist.Extinction).
    float deepest = lerp(2.0 * FISH_MIST_SHALLOW, FISH_MIST_DEEP, potential);
    if (above > deepest || potential <= 0.001)
    {
        return 0.0;
    }
    float thick = 3.912 / lerp(FISH_MIST_THIN_VISIBILITY, FISH_MIST_THICK_VISIBILITY, potential);
    // Carried on the fog's wind, round the hills its air cannot climb.
    float2 wind = dot(_FishFogFlow.xy, _FishFogFlow.xy) > 1e-6 ? _FishFogFlow.xy : float2(0.0, 1.0);
    float2 carried = at.xz + FishFlowAround(at.xz, at.y, _FishFogFlow.z, wind) - _FishFogLayerDrift.xy;
    float turn = _FishFogLayerShape.z;

    // The patches: one read of the shape volume, heights measured from the ground so the mist follows it.
    float3 patchUV = float3(carried.x / FISH_MIST_PATCH_TILE, above / (FISH_MIST_PATCH_TILE * FISH_MIST_RISE) + turn * 2.0, carried.y / FISH_MIST_PATCH_TILE);
    float4 shape = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, patchUV, 0);
    float3 zWorley = (shape.gba - FISH_MIST_WORLEY_MEAN) / FISH_MIST_WORLEY_SPREAD;
    float zPatch = (shape.r - FISH_MIST_PW_MEAN) / FISH_MIST_PW_SPREAD;

    // The wisps: one read of the detail volume, faded out (to its mean, so the cut does not move) where a
    // step is too long to see a three-metre shred.
    float wispSeen = saturate(1.0 - (footprint - 1.5) / 4.5);
    float3 zWisp = float3(0.0, 0.0, 0.0);
    if (wispSeen > 0.0)
    {
        float3 wispUV = float3(carried.x / FISH_MIST_WISP_TILE, above / (FISH_MIST_WISP_TILE * FISH_MIST_RISE) + turn * 8.0, carried.y / FISH_MIST_WISP_TILE);
        float3 detail = SAMPLE_TEXTURE3D_LOD(_FishCloudDetail, sampler_FishCloudDetail, wispUV, 0).rgb;
        zWisp = (detail - FISH_MIST_WORLEY_MEAN) / FISH_MIST_WORLEY_SPREAD * wispSeen;
    }

    // Every scale at once, in standard units. (The hollows, the water and the trees are already in how
    // ready the air is here, which sets the cut.)
    float field = (0.55 * zPatch + 0.35 * zWorley.x + 0.25 * zWorley.y + 0.2 * zWorley.z
        + 0.3 * zWisp.x + 0.25 * zWisp.y + 0.15 * zWisp.z) / FISH_MIST_PATCH_NORM;
    // The share of the ground covered: the cut in standard units, from about one part in fifty at the
    // onset to three in five at saturation.
    float cut = lerp(2.05, -0.25, potential);
    float patchMask = smoothstep(cut, cut + 0.5, field);
    // Free wisps: the finest octaves alone, likelier near a patch.
    float wispField = (0.6 * zWisp.x + 0.6 * zWisp.y + 0.4 * zWisp.z) / FISH_MIST_WISP_NORM;
    float wispCut = lerp(2.4, 1.0, potential);
    float wispMask = smoothstep(wispCut, wispCut + 0.6, wispField) * wispSeen * lerp(0.35, 1.0, smoothstep(cut - 1.2, cut, field));

    // How deep each lies: a patch the deeper the stronger it is (a shallow film for a small one, the full
    // depth for a bank), a free wisp a few metres. Each fades out over the upper part of its depth.
    float patchDepth = lerp(FISH_MIST_SHALLOW, deepest, saturate(patchMask * (0.5 + 0.25 * saturate(zPatch * 0.5 + 0.5) + 0.25 * potential)));
    float wispDepth = lerp(1.5, 5.0, wispMask);
    float patchProfile = 1.0 - smoothstep(0.35 * patchDepth, patchDepth, above);
    float wispProfile = 1.0 - smoothstep(0.3 * wispDepth, wispDepth, above);
    return thick * max(patchMask * patchProfile, 0.6 * wispMask * wispProfile);
}

/// The mist along a ray to `depth` (m): rgb the light it scatters toward the camera, a its transmittance.
/// `mistDistance`: the mean distance of what it added, weighted by what each step added (m), for the
/// steadying's reprojection; 0 when it met nothing.
float4 FishMistMarch(float3 origin, float3 direction, float depth, float jitter, out float mistDistance)
{
    mistDistance = 0.0;
    if (_FishMist.w <= 0.0 || _FishMist.y <= 0.0 || _FishMistGroundRect.w < 0.5)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    float range = min(depth, _FishMist.w);
    float deepest = FISH_MIST_DEEP;
    // A camera above the deepest mist looking level or up never comes down into it.
    bool covered;
    float4 under = FishMistGroundAt(origin.xz, covered);
    if (covered && origin.y - under.x > deepest && direction.y >= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    float3 scattered = float3(0.0, 0.0, 0.0);
    float transmittance = 1.0;
    float weighted = 0.0;
    float weight = 0.0;
    UNITY_LOOP
    for (int i = 0; i < FISH_MIST_STEPS; i++)
    {
        float t0 = _FishMist.w * (float(i) / FISH_MIST_STEPS) * (float(i) / FISH_MIST_STEPS);
        if (t0 >= range)
        {
            break;
        }
        float t1 = _FishMist.w * (float(i + 1) / FISH_MIST_STEPS) * (float(i + 1) / FISH_MIST_STEPS);
        float stepEnd = min(t1, range);
        float dt = stepEnd - t0;
        float t = t0 + jitter * dt;
        float3 at = origin + direction * t;
        float4 ground = FishMistGroundAt(at.xz, covered);
        if (!covered)
        {
            continue;
        }
        /* Below the ground is no air. Over land the depth buffer ends the ray at the terrain, so this only drops what
         * lies well under the map's 5 m surface (its bilinear ground can sit a little above the real one on a convex
         * slope: the tolerance keeps the mist hugging it). Over water the map's ground IS the surface, and water
         * writes no depth the march sees: the ray ran on under the surface, where `above` clamped to 0 is the
         * mist's densest, lit only by the dim ambient of a basin the terrain shades. That was the black mist
         * sinking into lakes and the sea. */
        float underTolerance = ground.z > 0.95 ? 0.0 : 1.5;
        if (at.y < ground.x - underTolerance)
        {
            continue;
        }
        float above = max(0.0, at.y - ground.x);
        if (above > deepest)
        {
            continue;
        }
        float ready = FishMistReadiness(ground);
        // Thinned over the last two fifths of the range, so the mist ends in the distance and not on a ring.
        float fade = 1.0 - smoothstep(_FishMist.w * 0.6, _FishMist.w, t);
        float beta = FishMistDensity(at, above, ready, dt) * fade;
        if (beta <= 0.0)
        {
            continue;
        }
        float stepT = exp(-beta * dt);
        FishFogColumn column = FishFogColumnAt(at.xz);
        float3 light = FishFogLight(at.y, direction, column, _FishFogLayer.x, FishTerrainSunlit(at), FishFogSunShare(at));
        float added = transmittance * (1.0 - stepT);
        scattered += light * added;
        weighted += t * added;
        weight += added;
        transmittance *= stepT;
        if (transmittance < 0.02)
        {
            break;
        }
    }
    mistDistance = weight > 1e-5 ? weighted / weight : 0.0;
    return float4(scattered, transmittance);
}

#endif
