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

// How high the snow's surface stands over the ground at a point, metres, for the tier that lifts the ground under
// snow: the blanket the cover makes (`blanket` metres when the ground is white), the deep snow past it
// (FishSnowDepthAt), trodden down where feet have pressed it (FishTrailAt) to a packed floor, and none on a slope
// that sheds it. The terrain shaders and the ground-contact band all ask this, so they agree where the surface is.
float FishSnowLiftMetres(float3 worldPos, float3 worldNormal, float blanket)
{
    float lift = saturate(FishCoverAt(worldPos).x) * blanket + FishSnowDepthAt(worldPos);
    float trodden = saturate(FishTrailAt(worldPos.xz).x);
    lift *= 1.0 - 0.8 * trodden;
    return lift * FishCoverFacing(worldNormal, worldPos, 0.5);
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
        // Each drop's pace snapped to whole drops in the clock's wrap, so the wrap is seamless.
        float phase = frac(time * FishWrapCycles(0.9 + seed * 0.6) + seed);
        float distance = length(local);
        // Rings a little different in size and pitch from drop to drop.
        float pitch = 22.0 + seed * 14.0;
        float ring = sin((distance - phase) * pitch) * exp(-distance * (5.0 + seed * 3.0)) * (1.0 - phase);
        slope += normalize(local + 1e-4) * ring / scale;
    }
    return slope * strength * 0.35;
}

// ── Shared pieces of a weathered surface ───────────────────────────────

// Damp: water darkens what it soaks into and makes it shine, without changing its shape. The ground
// and the foliage both use this, so a wet leaf and the wet ground under it darken alike.
void FishDampen(float wet, inout half3 albedo, inout half smoothness)
{
    albedo *= lerp(1.0, 0.62, wet * 0.85);
    smoothness = lerp(smoothness, 0.75, wet * 0.6);
}

// Which cover lies deepest here and what it looks like: its depth (0..1), colour and sheen. Snow,
// ash and sand settle the same way and differ only in colour and in how they take light; a world
// does not have two of them at once, so the deepest one speaks for all three.
void FishCoverLook(float4 here, out float depth, out half3 colour, out half coverSmoothness)
{
    float snow = saturate(here.x);
    float ash = saturate(here.z);
    float sand = saturate(here.w);
    depth = max(snow, max(ash, sand));
    colour = half3(0.92, 0.94, 0.98);      // snow
    coverSmoothness = 0.35;
    if (ash >= snow && ash >= sand)
    {
        colour = half3(0.22, 0.21, 0.20);  // ash
        coverSmoothness = 0.12;
    }
    else if (sand >= snow && sand >= ash)
    {
        colour = half3(0.76, 0.66, 0.45);  // sand
        coverSmoothness = 0.2;
    }
}

// How much of the cover settles on a surface with this normal: deeper cover reaches further up a
// slope, and its edge wanders instead of ringing the world at one height.
float FishCoverAmount(float depth, float3 worldPos, float3 normalWS)
{
    float drift = FishSurfaceNoise(worldPos.xz * 0.6) * 0.25;
    float facing = FishCoverFacing(normalWS, worldPos, lerp(0.75, 0.15, saturate(depth + drift)));
    return saturate(facing * (depth * 1.4 - 0.1));
}

// ── Spray round a waterfall ────────────────────────────────────────────

// A fall throws spray over the rock beside its curtain and the ground round its pool, and keeps it
// wet whatever the sky is doing: the wet zone here, set by WaterfallPresenter from the falls the water
// plugin published (FishMMO.Shared.Weather.Waterfalls), the nearest sixteen to the camera.
//
// Each zone is a tapered capsule: from the middle of the lip (A, radius A.w, the thin spray beside the
// sheet) down to where the water lands (B, radius B.w, the cloud the impact throws out). Clamped to its
// ends, the capsule closes in a sphere round the landing, which is the zone round the pool.
//
// Spray drives sideways and up under an overhang, so unlike the rain this is never asked whether the
// sky is open above a point. Count 0 when there is no fall near, which is almost everywhere: then the
// cost is one uniform branch, and the box round every zone (Min/Max) turns away the rest of the world
// with six compares before anything is looped over.
float _FishFallWetCount;        // how many of the zones below are in use, 0..16
float4 _FishFallWetA[16];       // xyz the lip's middle (world metres), w the zone's radius there (m)
float4 _FishFallWetB[16];       // xyz where the water lands (world metres), w the zone's radius there (m)
float4 _FishFallWetMin;         // xyz the low corner of a box round every zone, w unused
float4 _FishFallWetMax;         // xyz the high corner, w unused

// How wet the spray keeps a point, 0..1, and where in the zone it lies. `reach` is the distance from
// the fall's axis as a share of the zone's radius there (0 on the axis, 1 at the edge; the nearest
// zone's), and `anchor` that fall's landing, which the trickles are laid out from so their pattern is
// pinned to the rock without the precision a few kilometres of world coordinate would cost it.
float FishFallSpray(float3 worldPos, out float reach, out float3 anchor)
{
    reach = 1.0;
    anchor = worldPos;
    int count = (int)_FishFallWetCount;
    if (count <= 0)
    {
        return 0.0;
    }
    if (any(worldPos < _FishFallWetMin.xyz) || any(worldPos > _FishFallWetMax.xyz))
    {
        return 0.0;
    }
    UNITY_LOOP
    for (int i = 0; i < count; i++)
    {
        float4 a = _FishFallWetA[i];
        float4 b = _FishFallWetB[i];
        float3 ab = b.xyz - a.xyz;
        float t = saturate(dot(worldPos - a.xyz, ab) / max(dot(ab, ab), 1e-4));
        float radius = max(lerp(a.w, b.w, t), 0.1);
        float r = length(worldPos - (a.xyz + ab * t)) / radius;
        if (r < reach)
        {
            reach = r;
            anchor = b.xyz;
        }
    }
    // Soaked inside about half the radius, drying smoothly to nothing at its edge: spray thins out
    // with distance rather than stopping at a line.
    return 1.0 - smoothstep(0.5, 1.0, reach);
}

// One set of trickles down a steep face, 0..1: thin rills a quarter of a metre apart across the face,
// each with a brighter head of water sliding down it. `across` runs along the face, `down` is height;
// both relative to the fall, so the pattern is fixed to the rock and only the water moves. Each rill's
// pace is snapped to whole cycles in the weather clock's wrap, so it never jumps when the clock does.
float FishFallTrickle(float across, float down, float time)
{
    float column = across * 4.0;
    float cell = floor(column);
    float seed = FishSurfaceHash(float2(cell, 7.25));
    // Not every column carries a rill, and each lies a little to one side of its column's middle.
    float present = step(0.3, seed);
    float offset = frac(column) - 0.5 - (seed - 0.5) * 0.5;
    float rill = 1.0 - smoothstep(0.04, 0.16, abs(offset));
    float period = 1.2 + seed * 1.8;
    float phase = frac(down / period + time * FishWrapCycles(0.35 + seed * 0.4) + seed);
    float head = phase * phase * phase * phase;
    return present * rill * (0.35 + 0.65 * head);
}

// What the spray does beyond darkening, applied in place on a surface that is `spray` wet: running
// water on steep rock close in, and a film of moss and algae a little further out, where the rock
// stays damp but the water does not run hard enough to scour it. Darkening itself is FishDampen's,
// so the caller combines this with the rain's wetness first.
void FishFallSurface(float3 worldPos, float spray, float reach, float3 anchor, inout half3 albedo, inout half smoothness, half3 normalWS)
{
    if (spray <= 0.001)
    {
        return;
    }
    // Only above the pool: the rock under its surface is the water's to draw.
    float above = saturate((worldPos.y - anchor.y) * 2.0 + 1.0);
    float3 n = normalWS;

    // ── Moss ──
    // A band, not a disc: none in the inner zone where the water sheets over the rock, most from about
    // half the radius out, fading to nothing before the spray's edge. Thickest on what faces up (water
    // settles there) and on what faces the fall (where the spray lands), broken into patches.
    float band = smoothstep(0.3, 0.55, reach) * (1.0 - smoothstep(0.75, 1.0, reach));
    float3 toFall = normalize(anchor - worldPos + float3(0.0, 1e-3, 0.0));
    float facing = saturate(0.35 + 0.65 * max(n.y, dot(n, toFall)));
    float patch = FishSurfaceNoise((worldPos.xz - anchor.xz) * 0.7 + worldPos.y * 0.3);
    float moss = band * facing * saturate(patch * 1.6 - 0.25) * above;
    // A dark wet green, at most half the way: the rock still shows through.
    half3 mossColour = half3(0.06, 0.11, 0.035) + albedo * half3(0.25, 0.35, 0.2);
    albedo = lerp(albedo, mossColour, moss * 0.5);

    // ── Running water ──
    // The inner zone on a steep face: a sheen of water over the rock and the trickles running down it.
    float steep = 1.0 - smoothstep(0.35, 0.75, n.y);
    float run = (1.0 - smoothstep(0.25, 0.6, reach)) * steep * above;
    if (run > 0.001)
    {
        // A face whose normal points along x runs along z, and the other way about: two sets of rills
        // in world axes blended by the normal, as a triplanar map would, rather than turning world
        // coordinates to each pixel's normal (which would make the pattern swim as the normal varies).
        float3 rel = worldPos - anchor;
        float wx = abs(n.x), wz = abs(n.z);
        float time = _FishWeatherMisc.w;
        float trickle = (FishFallTrickle(rel.z, rel.y, time) * wx + FishFallTrickle(rel.x + 13.7, rel.y, time) * wz) / max(wx + wz, 1e-3);
        smoothness = lerp(smoothness, 0.88 + 0.1 * trickle, run * 0.8);
        albedo *= 1.0 - 0.15 * trickle * run;
    }
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
    // A waterfall's spray wets what is round it, rain or none and roof or none: the wetter of the two
    // speaks, so a shower over a fall does not soak its rock twice.
    float fallReach;
    float3 fallAnchor;
    float spray = FishFallSpray(worldPos, fallReach, fallAnchor);
    wet = max(wet, spray);
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
        FishDampen(wet, albedo, smoothness);
        // Round a fall: water running down the steep rock, and moss where the spray keeps it damp.
        // The puddles below take the same level test as the rain's, so spray pools on flat ground
        // by the fall and never on its slopes.
        FishFallSurface(worldPos, spray, fallReach, fallAnchor, albedo, smoothness, normalWS);

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
    float depth;
    half3 colour;
    half coverSmoothness;
    FishCoverLook(here, depth, colour, coverSmoothness);
    if (depth > 0.001)
    {
        float amount = FishCoverAmount(depth, worldPos, normalWS);
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

/// <summary>Where feet have gone (GroundTrailMap), on the ground, applied in place after FishWeatherSurface.</summary>
/// Snow, ash and sand take a packed, shadowed furrow with walls; wet ground takes darker, glossier prints that hold
/// water when it is wet enough to puddle; dry firm ground takes nothing. The ground's shaders call this and nothing
/// else does: a print is the ground's, not the top of a rock someone walked past. `viewDirWS` points from the surface
/// to the eye; on the tier that lifts the ground under snow the print is looked into (a few steps of parallax), so its
/// near wall hides its floor as a real one would.
void FishTrailSurface(float3 worldPos, float3 viewDirWS, inout half3 albedo, inout half3 normalWS, inout half smoothness)
{
    if (_FishTrailRect.w < 0.5)
    {
        return;
    }
    float2 at = worldPos.xz;
    float press = saturate(FishTrailAt(at).x);
    if (press < 0.004)
    {
        return;
    }
    float4 here = FishCoverAt(worldPos);
    float cover = saturate(max(here.x, max(here.z, here.w))) * FishSurfaceExposure(worldPos);
    float wet = saturate(here.y);
    // How readily this ground takes a print at all: any cover does, wet ground does, dry firm ground does not.
    float soft = saturate(cover * 1.5 + wet * 1.2);
    if (soft < 0.01)
    {
        return;
    }
    // How deep a print sinks, metres: a few centimetres into mud, deeper into a blanket, half the deep snow's depth.
    float sink = 0.03 + cover * 0.06 + FishSnowDepthAt(worldPos) * 0.5;

    // Looked into: the point the eye sees on the print's floor lies further along the view than the surface point.
    if (_FishWeatherTier.x > 0.5 && viewDirWS.y > 0.05)
    {
        float2 reach = -viewDirWS.xz / max(viewDirWS.y, 0.25) * sink;
        UNITY_UNROLL
        for (int k = 0; k < 3; k++)
        {
            press = saturate(FishTrailAt(at + reach * press).x);
        }
        at += reach * press;
    }

    // The print's walls, from how fast the press changes across a texel either way.
    float texel = _FishTrailParams.x;
    float px = saturate(FishTrailAt(at + float2(texel, 0.0)).x);
    float pz = saturate(FishTrailAt(at + float2(0.0, texel)).x);
    float2 slope = float2(px - press, pz - press) * (sink / texel) * soft;
    normalWS = normalize(normalWS + float3(slope.x, 0.0, slope.y));

    float trodden = press * soft;
    // Packed snow, ash or sand: greyer and duller than the loose stuff round it, and in its own shade.
    albedo *= lerp(1.0, 0.8, trodden * cover);
    smoothness = lerp(smoothness, smoothness * 0.6, trodden * cover);
    // Wet ground: churned darker and glossier, and in a wet enough spell the prints fill with water.
    albedo *= lerp(1.0, 0.72, trodden * wet);
    smoothness = lerp(smoothness, 0.8, trodden * wet * 0.7);
    float pooled = saturate((wet - 0.5) * 3.0) * smoothstep(0.35, 0.8, press) * (1.0 - cover);
    if (pooled > 0.001)
    {
        albedo = lerp(albedo, albedo * 0.45, pooled);
        smoothness = lerp(smoothness, 0.95, pooled);
        normalWS = normalize(lerp(normalWS, float3(0.0, 1.0, 0.0), pooled));
    }
}

/// <summary>
/// What the weather leaves on foliage — leaves, grass, bark, cards — applied in place: the same
/// cover and the same damp as the ground (FishCoverLook, FishDampen, FishCoverAmount), so a meadow
/// and the grass standing in it whiten and darken together. No standing water and no ripples: a leaf
/// does not hold a puddle, and a card whose normal is bent skyward for soft lighting would otherwise
/// grow one on every blade.
/// </summary>
/// `cover` returns how much cover settled, so the caller can thin or bury what lies under it.
void FishWeatherFoliage(float3 worldPos, inout half3 albedo, inout half3 normalWS, inout half smoothness, out float cover)
{
    cover = 0.0;
    float exposure = FishSurfaceExposure(worldPos);
    float4 here = FishCoverAt(worldPos);

    // Leaves shed water faster than ground soaks it: damp, at most two thirds as dark.
    // A waterfall's spray as well as the rain (FishFallSpray), shed the same way. No moss or running
    // water on a leaf: those are the rock's.
    float fallReach;
    float3 fallAnchor;
    float wet = max(saturate(here.y) * exposure, FishFallSpray(worldPos, fallReach, fallAnchor)) * 0.65;
    if (wet > 0.001)
    {
        FishDampen(wet, albedo, smoothness);
    }

    float depth;
    half3 colour;
    half coverSmoothness;
    FishCoverLook(here, depth, colour, coverSmoothness);
    if (depth > 0.001)
    {
        cover = FishCoverAmount(depth, worldPos, normalWS);
        if (cover > 0.001)
        {
            albedo = lerp(albedo, colour, cover);
            smoothness = lerp(smoothness, coverSmoothness, cover);
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
