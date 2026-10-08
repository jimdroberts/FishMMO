#ifndef FISH_CLOUD_RESOLVE_INCLUDED
#define FISH_CLOUD_RESOLVE_INCLUDED

// The steadying of the clouds: a temporal upsampler of the standard design (Karis, "High Quality
// Temporal Supersampling", SIGGRAPH 2014; Salvi, "An Excursion in Temporal Supersampling", GDC 2016;
// Schneider, "The Real-time Volumetric Cloudscapes of Horizon Zero Dawn", SIGGRAPH 2015, and "Nubis",
// SIGGRAPH 2017; Hillaire's volumetric cloud reconstruction in Unreal Engine 5). The march is a fraction
// of the screen and looks through a different one of sixteen places inside its texels each frame
// (FishCloudsFeature.SubPixelPlace); per rebuilt pixel, every frame:
//
//   1. THE CURRENT ESTIMATE. The pixel is read off the marched texels round it with a smooth kernel that
//      is never negative and sums to one — the tent (bilinear) over the texels' TRUE places this frame,
//      each weighed by whether its ray looked at the same surface the pixel does — so every pixel takes
//      a share of two to four rays, and a texel's edge is a ramp and never a step. Scattered light and
//      transmittance with the same weights. Averaged over the sixteen places, what a still converges on
//      is the sky filtered by that tent (the jitter makes the reconstruction an exact convolution), with
//      no grid left in it: that is what the sixteen places are for.
//   2. REPROJECTION with the clouds' own motion. The march hands back, per texel, the mean distance of
//      what it saw and the mean wind gain of the bands it saw (FishCloudMarch's `cloudMotion`), each
//      weighted by what the step added. The pixel's history is fetched from where THAT cloud was a frame
//      ago: the previous camera's view of the cloud point, less its own band's drift this frame. Sky with
//      no cloud in front of it is the far plane. It used to be one plane at the low deck's middle, moved
//      back by the low deck's drift alone, so the middle and high bands — carried two to three times as
//      fast since the wind was given a height (CloudClimate's band WindScale) — were fetched from the
//      wrong place every frame: the long smeared streaks along the wind in the night sky.
//   3. RECTIFICATION. The history is clipped toward the mean of this frame's three-by-three marched
//      texels, in YCoCg and transmittance, to within γ standard deviations (variance clipping, Salvi
//      2016). The texels, not the pixels' tent estimates: neighbouring pixels' tents share the same two to
//      four rays, so their spread hardly sees the rays' noise at all, and a box that narrow would clip
//      every history to this frame's noise — no averaging, the very grain this replaces. The texels are
//      this frame's estimates at their own places, nine independent rays: their spread IS the noise plus
//      the cloud's own change across them.
//   4. BLEND. An exponential moving average: α = 1 / (the frames behind the pixel + 1), so a pixel with
//      nothing behind it (a reset, off-screen last frame, just uncovered) takes this frame whole, and one
//      that has settled takes 1 / _FishCloudTemporal.x — one sixteenth, the jitter's whole cycle, so its
//      sixteen places and the rays' phases (golden-ratio from cycle to cycle, FishClouds.shader pass 0)
//      average out. A history the clip had to pull far is trusted less, the further the less.
//
// What it replaced (issue #238, 2026-09-28, Jim's editor on a real GPU, camera moving): each sample was
// handed only to the pixel it fell in, histories dropped wherever an own sample disagreed with them —
// sharp on a still, but every pixel showed one ray a visit and every reset exposed that ray alone: the
// crosshatch and the dark blocky specks under an overcast, and the woven rings of frozen ray-phase noise.
//
// ONE arithmetic, two ways in. FishClouds.shader pass 1 fetches each pixel's three-by-three block of
// marched texels itself; FishCloudResolve.compute loads every texel an 8 × 8 group of pixels needs once
// into groupshared memory and hands each pixel the same block from there. Both call FishCloudResolve, so
// the two cannot drift apart. Every variable here is set by FishCloudsFeature: on the material for the
// pass, on the kernel for the compute (a compute kernel is handed its own, never a material's).

TEXTURE2D(_FishCloudCurrent);
SAMPLER(sampler_FishCloudCurrent);
// The march's second target: per texel, x the mean distance of what its ray saw (km), y the mean wind
// gain of the bands it saw (FishCloudMarch's `cloudMotion`; km so a half float holds 150 km).
TEXTURE2D(_FishCloudCurrentMotion);
TEXTURE2D(_FishCloudHistory);
SAMPLER(sampler_FishCloudHistory);
TEXTURE2D(_FishCloudHistoryWeight);
SAMPLER(sampler_FishCloudHistoryWeight);
float4x4 _FishCloudPreviousVP;
float4x4 _FishCloudInverseVP;
// x the frames a settled pixel averages over (1/α), y 1 with a usable history; the profile's diagnostics
// (VolumetricCloudDiagnostics, FishCloudsFeature): z the variance clip's width as its difference from
// FISH_RESOLVE_GAMMA, w 1 to take the history unclipped. Both 0 as shipped.
float4 _FishCloudTemporal;
float4 _FishCloudMotion;        // xy how far the air carried the LOW clouds since the last frame (m, world x and z); a band moves its wind gain times this
float4 _FishCloudJitter;        // xy where inside its texel the march looked this frame (texels, -0.5..0.5), zw the march's size
float4 _FishCloudUpsample;      // xy the rebuilt buffer's size, z how big a change a hard clip must be before it lets go (a share), w 1 when the frames are steadied, 0 when the rebuild is only this frame's upscale
// x the lowest any cloud can be (m above sea level: the floor of the shell the march runs through),
// y the world's radius (m), z 1 when this camera stands below that floor and a pixel whose own ray
// ends below it may be taken as clear without asking the samples, w unused.
float4 _FishCloudResolveShell;
// The reconstruction options under trial (CloudOptions, FishCloudsFeature.cs), each 1 on and 0 off: x A,
// the quadratic B-spline over the three-by-three in place of the tent; y B, no longer read (its
// confidence halo belonged to the scheme this replaced); z D, informational (its phase is the march's
// own now); w the rays' phase this frame (read by the march, pass 0, which declares its own copy).
float4 _FishCloudOptions;

// How many standard deviations of this frame's texels a history may stand from their mean before it is
// clipped (Salvi 2016 recommends 1 for anti-aliasing, 1.25 where noise must average). MEASURED on a CPU
// copy (sixteen places, four pixels a texel, noise 0.03 on every ray, α 1/16, 128 frames): the noise left
// 0.0043 / 0.0038 / 0.0035 at γ 1 / 1.25 / 1.5 of the 0.03 put in, and a sub-texel pattern's error
// against its converged picture 0.020 / 0.015 / 0.012. 1.25 is the spec's and the middle of that trade.
#define FISH_RESOLVE_GAMMA 1.25

// The least a standard deviation is taken to be, of full scale: nine texels exactly alike (clear air, a
// flat overcast) would otherwise clip any history onto one point, rounding and all.
#define FISH_RESOLVE_SIGMA_FLOOR 0.002

// Below this much opacity the texels round a pixel saw no cloud worth reprojecting by, and the pixel is
// reprojected as what it shows: the far plane for sky, its own surface for the world.
// A twentieth of a percent: next to nothing. It was 1 %, and a faint wisp — whose light and opacity are
// all the pixel holds — under 1 % was carried as the far plane, not with its band's drift, so its history
// trailed behind it, was clipped, and fizzed at every thin edge; and a wisp crossing 1 % switched which
// place its history came from in one frame.
#define FISH_RESOLVE_CLOUD_SEEN 0.0005

// The least brightness (luma) a change is measured against, so that a change in near-black cloud is not
// every change there is.
#define FISH_RESOLVE_CHANGE_FLOOR 0.005

// How hard parallax shortens the moving average: settled frames over (1 + this × the pixels the fetch
// moved for the cloud's distance this frame). 3: a quarter-pixel shift keeps nine of sixteen frames, a
// pixel's keeps four.
#define FISH_RESOLVE_PARALLAX_FRAMES 3.0

struct FishCloudResolved
{
    float4 clouds;      // rgb scattered light, a transmittance
    float weight;       // the frames behind the pixel, capped at _FishCloudTemporal.x
};

bool FishResolveIsSky(float rawDepth)
{
    #if UNITY_REVERSED_Z
        return rawDepth <= 1e-6;
    #else
        return rawDepth >= 1.0 - 1e-6;
    #endif
}

// A world-space ray for a screen position, as the march casts it.
float3 FishResolveRay(float2 uv, float3 camera)
{
    float4 clip = float4(uv * 2.0 - 1.0, 1.0, 1.0);
    #if UNITY_UV_STARTS_AT_TOP
        clip.y = -clip.y;
    #endif
    float4 world = mul(_FishCloudInverseVP, clip);
    return normalize(world.xyz / world.w - camera);
}

// What of a texel's answer lies nearer than `depth` metres along its ray: every ray stops at the world, so
// one that went past the pixel's surface (the sky beside a post, beside a leaf, beside a far ridge) also
// carries everything behind that surface. Only the march's mean distance of what it saw is known
// (`cloudDistance`), so what it saw is taken as spread evenly round that mean, as widely as the ray's own
// length allows (fog from the camera on: the whole ray; a cloud deck far out: a narrow band), and the share
// in front of `depth` is kept: its transmittance as that share of the optical depth, its light in proportion
// to the opacity kept. A leaf a few metres out keeps nothing of the sky ray's kilometres (it stands clear in
// front of the clouds); a ridge six kilometres out keeps the haze in front of it and none of a deck behind.
//
// This replaced "both far away is both sky": a texel and a pixel both past 4 km matched whatever their
// depths, so on the horizon a far ridge's pixels took the sky rays' whole march (twenty kilometres of fog
// and cloud) and the sky's took the ridge's. With the march at a sixteenth of the screen, which texels hit
// the ridge and which the sky changed with the grid's jitter every frame, and the horizon shimmered; a ridge
// thinner than a texel flipped between that and the leaf rule's "clear" outright.
float4 FishResolveInFront(float4 value, float rayDepth, float cloudDistance, float depth)
{
    float4 kept = value;
    if (cloudDistance > 0.0 && depth < rayDepth)
    {
        float spread = rayDepth > cloudDistance ? min(cloudDistance, rayDepth - cloudDistance) : 0.25 * cloudDistance;
        spread = max(1.0, spread);
        float share = saturate((depth - (cloudDistance - spread)) / (2.0 * spread));
        float transmittance = max(value.a, 1e-4);
        float keptTransmittance = pow(transmittance, share);
        float opacity = 1.0 - value.a;
        float light = opacity > 1e-4 ? (1.0 - keptTransmittance) / opacity : share;
        kept = float4(value.rgb * light, keptTransmittance);
    }
    return kept;
}

// Catmull-Rom in five bilinear reads, the corners dropped. Carrying the history from where it was to
// where it is now is a resample every frame, and a bilinear one blurs a little each time: over the
// dozens of frames a pixel's history lives, it would take back the detail the accumulation is for. Five
// reads and not the sixteen of the full filter: the four corner taps together carry (f(1 − f) / 2)² per
// axis pair, at most 1/64 of the whole at f = ½, and dropping them is the standard trade (Jimenez,
// "Filmic SMAA", SIGGRAPH 2016 Advances course). Its small overshoot is the clip's to catch.
float4 FishResolveCatmullRom(TEXTURE2D_PARAM(source, sourceSampler), float2 uv, float2 size)
{
    float2 position = uv * size;
    float2 centre = floor(position - 0.5) + 0.5;
    float2 f = position - centre;
    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    float2 w3 = f * f * (-0.5 + 0.5 * f);
    float2 w12 = w1 + w2;
    float2 at12 = (centre + w2 / w12) / size;
    float2 at0 = (centre - 1.0) / size;
    float2 at3 = (centre + 2.0) / size;
    float4 sum = SAMPLE_TEXTURE2D_LOD(source, sourceSampler, float2(at12.x, at0.y), 0) * (w12.x * w0.y)
        + SAMPLE_TEXTURE2D_LOD(source, sourceSampler, float2(at0.x, at12.y), 0) * (w0.x * w12.y)
        + SAMPLE_TEXTURE2D_LOD(source, sourceSampler, at12, 0) * (w12.x * w12.y)
        + SAMPLE_TEXTURE2D_LOD(source, sourceSampler, float2(at3.x, at12.y), 0) * (w3.x * w12.y)
        + SAMPLE_TEXTURE2D_LOD(source, sourceSampler, float2(at12.x, at3.y), 0) * (w12.x * w3.y);
    float total = w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y;
    return sum / total;
}

// Height above the sea over a curved world, the twin of FishCloudAltitude (FishCloudVolume.hlsl) with
// the radius handed in. Never |p − centre| − R: with the centre 6371 km down that quantises every
// height to half a metre. For a point near the origin, |p − centre|² − R² = p·p + 2R·p.y, every term
// of it small, and h = over / (R + √(R² + over)) adds where the old form subtracted.
float FishResolveAltitude(float3 position, float radius)
{
    float r = max(1000.0, radius);
    float over = dot(position, position) + 2.0 * r * position.y;
    return over / (r + sqrt(max(0.0, r * r + over)));
}

// Where this pixel's own ray ends: the world it stops at.
float3 FishResolveSeen(float here, float3 camera, float3 direction, float3 forward)
{
    return camera + direction * (here / max(1e-4, dot(direction, forward)));
}

// True when no cloud can stand between the camera and what this pixel shows, so its clouds are
// exactly clear and nothing needs averaging.
//
// Everything that can hold cloud — every band, a storm's lowered base, the mist and the hang under a
// wet tower — lies above the shell's floor, which the march itself runs from: SkySystem widens it
// down to the lowest storm floor, and to the ground whenever there is fog or rain haze to draw. The
// air below that floor is a ball about the world's centre, and a ball is convex: with the camera and
// the point it looks at both inside it, the whole segment between them is too, so the march of this
// very ray finds nothing. A metre of margin keeps the one comparison from ever being decided by rounding.
// One exit, as every helper here: FXC flags an early return inlined into a kernel as a potentially
// uninitialised return value.
bool FishResolveBelowClouds(float rawHere, float3 seen)
{
    bool below = false;
    if (_FishCloudResolveShell.z > 0.5 && !FishResolveIsSky(rawHere))
    {
        below = FishResolveAltitude(seen, _FishCloudResolveShell.y) < _FishCloudResolveShell.x - 1.0;
    }
    return below;
}

// Where what this pixel shows stood last frame, and whether the history there can be used.
//   cloudSeen — how opaque the clouds round the pixel are (the kernel's mean of 1 − T)
//   motion — their mean distance along the ray (m) and mean wind gain, weighted by what each added
//
// With cloud in front of it the pixel is the cloud: the point at that distance down its ray (never
// past the surface the ray stops at), carried back by its own bands' drift — the low clouds' step times
// their wind gain — and seen through last frame's camera. With none, sky is the far plane, a direction
// that only the camera's turn moves, and the world is its own surface, which holds still.
bool FishResolvePrevious(float rawHere, float here, float3 camera, float3 direction, float3 forward,
    float cloudSeen, float2 motion, out float2 previousUV, out float parallax)
{
    parallax = 0.0;
    bool sky = FishResolveIsSky(rawHere);
    float toSurface = here / max(1e-4, dot(direction, forward));
    float4 seenAt;
    if (cloudSeen > FISH_RESOLVE_CLOUD_SEEN && motion.x > 0.0)
    {
        float along = sky ? motion.x : min(motion.x, toSurface);
        float3 cloud = camera + direction * along - motion.y * float3(_FishCloudMotion.x, 0.0, _FishCloudMotion.y);
        seenAt = float4(cloud, 1.0);
    }
    else if (sky)
    {
        seenAt = float4(direction, 0.0);
    }
    else
    {
        seenAt = float4(camera + direction * toSurface, 1.0);
    }
    float4 previous = mul(_FishCloudPreviousVP, seenAt);
    previousUV = previous.xy / max(1e-5, previous.w) * 0.5 + 0.5;
    #if UNITY_UV_STARTS_AT_TOP
        previousUV.y = 1.0 - previousUV.y;
    #endif
    // How far, in this buffer's pixels, the place it was fetched from depends on how far away the cloud
    // was taken to be: the same direction at infinity, against it. Turning the camera moves both alike
    // (0); only moving it does — and how far it moves the fetch is how far an error in that distance
    // (one figure a pixel, the mean depth of all it saw, deeper than the face the eye follows) moves it.
    if (seenAt.w > 0.5)
    {
        float4 atInfinity = mul(_FishCloudPreviousVP, float4(direction, 0.0));
        float2 infinityUV = atInfinity.xy / max(1e-5, atInfinity.w) * 0.5 + 0.5;
        #if UNITY_UV_STARTS_AT_TOP
            infinityUV.y = 1.0 - infinityUV.y;
        #endif
        parallax = atInfinity.w > 1e-5 ? length((previousUV - infinityUV) * _FishCloudUpsample.xy) : 0.0;
    }
    return _FishCloudTemporal.y > 0.5 && previous.w > 1e-5 && all(previousUV >= 0.0) && all(previousUV <= 1.0);
}

// Premultiplied scattered light into luma and two chroma axes, transmittance carried as it is: the
// space the clip's box is drawn in (Karis 2014: a box in YCoCg hugs a neighbourhood's colours far
// tighter than one in RGB, since most of their spread is along luma). Exact, and exactly inverted below.
float4 FishResolveToYCoCg(float4 value)
{
    float3 c = value.rgb;
    return float4(dot(c, float3(0.25, 0.5, 0.25)), dot(c, float3(0.5, 0.0, -0.5)), dot(c, float3(-0.25, 0.5, -0.25)), value.a);
}

float4 FishResolveFromYCoCg(float4 value)
{
    float y = value.x, co = value.y, cg = value.z;
    return float4(y + co - cg, y + cg, y - co - cg, value.w);
}

// Clips `history` toward `mean` onto the box mean ± extent, along the line between them — not a
// per-channel clamp, which bends a colour off that line into one the neighbourhood never held (Karis
// 2014, clip_aabb). `units` is how far outside the box it stood, in box half-widths (≤ 1 inside).
float4 FishResolveClip(float4 history, float4 mean, float4 extent, out float units)
{
    float4 offset = history - mean;
    float4 scaled = abs(offset) / max(extent, 1e-6);
    units = max(max(scaled.x, scaled.y), max(scaled.z, scaled.w));
    return units > 1.0 ? mean + offset / units : history;
}

// The current-estimate kernel for a texel `apart` texels from the pixel on this frame's grid: the tent
// (bilinear), max(0, 1 − |x|) per axis — never negative, one over the two nearest texels per axis
// wherever the pixel sits, continuous across the switch from one pair to the next. Option A swaps in the
// quadratic B-spline over the three nearest (de Boor, "A Practical Guide to Splines", 1978, ch. IX:
// ¾ − x² to |x| = ½, ½(3/2 − |x|)² to 3/2), smoother and a little wider. Twins: CloudResolveTwin.Tent,
// CloudOptions.BSpline (FishCloudsFeature.cs).
float2 FishResolveKernel(float2 apart)
{
    float2 a = abs(apart);
    float2 weight = saturate(1.0 - a);
    if (_FishCloudOptions.x > 0.5)
    {
        float2 inner = 0.75 - a * a;
        float2 tail = saturate(1.5 - a);
        weight = lerp(0.5 * tail * tail, inner, step(a, 0.5));
    }
    return weight;
}

// A pixel with nothing in front of it (FishResolveBelowClouds), or whose every texel came back clear air
// (FishCloudResolve.compute's clear tile, and FishCloudResolve below for a clear three-by-three). Every
// estimate it could be steadied toward is (0, 0, 0, 1), and the clip would pull any history onto that one
// point, so that is what it is. Settled: when a cloud does arrive, the clip is what lets go of the clear
// history (FishCloudResolve's step 3), not how recently the pixel was last cloudy.
FishCloudResolved FishResolveClear()
{
    FishCloudResolved result;
    result.clouds = float4(0.0, 0.0, 0.0, 1.0);
    result.weight = _FishCloudTemporal.x;
    return result;
}

// The steadying itself, for one pixel.
//   uv, rawHere, here — the pixel, and its scene depth raw and as eye depth
//   camera, direction, forward — the camera, this pixel's ray and the camera's forward
//   onGrid, marched, perTexel — the pixel on the grid the rays were cast on this frame (texel (i, j)'s
//     ray went through (i + ½, j + ½) of it), that grid's size, and this buffer's pixels to a texel
//   values, theres, ats, motions — the pixel's three-by-three block of marched texels, k = (dy + 1) · 3 +
//     (dx + 1) for offsets dx, dy of −1..1 from its nearest: each one's value, the eye depth where its
//     ray actually went, its place on the grid, and its motion (_FishCloudCurrentMotion: km, wind gain)
FishCloudResolved FishCloudResolve(float2 uv, float rawHere, float here, float3 camera, float3 direction, float3 forward,
    float2 onGrid, float2 marched, float2 perTexel, float4 values[9], float theres[9], float2 ats[9], float2 motions[9])
{
    float4 current = 0.0;               // 1. the kernel's estimate
    float currentWeight = 0.0;
    float cloudWeight = 0.0;            // the kernel's weight times each texel's opacity
    float2 motion = 0.0;                // distance and wind gain, by that
    float4 sum = 0.0;                   // 3. the neighbourhood, in YCoCg + transmittance
    float4 sumSquares = 0.0;
    float matched = 0.0;
    float4 closest = float4(0.0, 0.0, 0.0, 1.0);
    float closestGap = 1e30;
    bool busy = false;
    // Eye depths to distances along the rays (the march's distances are), for FishResolveInFront.
    float alongRay = rcp(max(0.05, dot(direction, forward)));
    [unroll] for (int k = 0; k < 9; k++)
    {
        float4 value = values[k];
        busy = busy || any(value != float4(0.0, 0.0, 0.0, 1.0));
        // A ray that reached this pixel's depth or beyond saw what is in front of it, and more: it speaks for
        // the pixel once cut back to its depth. One stopped nearer saw a different surface (a post in front,
        // a slope's nearer face), and counts only by how close its depth is.
        float same = 1.0;
        if (theres[k] >= here)
        {
            value = FishResolveInFront(value, theres[k] * alongRay, motions[k].x * 1000.0, here * alongRay);
        }
        else
        {
            same = saturate(1.0 - (here - theres[k]) / max(12.0, here * 0.2));
        }
        float2 across = FishResolveKernel(ats[k] - onGrid);
        float w = across.x * across.y * same;
        current += value * w;
        currentWeight += w;
        float seen = w * saturate(1.0 - value.a);
        cloudWeight += seen;
        motion += seen * float2(motions[k].x * 1000.0, motions[k].y);
        // Only the texels that looked at what this pixel does may say what it can be: at the edge of
        // a post, the sky's texels beside it are another picture.
        if (same > 0.5)
        {
            float4 y = FishResolveToYCoCg(value);
            sum += y;
            sumSquares += y * y;
            matched += 1.0;
        }
        float gap = abs(theres[k] - here);
        if (gap < closestGap)
        {
            closestGap = gap;
            closest = value;
        }
    }
    // Nothing but clear air round it: exactly clear, as the kernel's clear tile says. (One exit for the
    // whole function, the rest under the else: FXC flags early returns inlined into a kernel.)
    FishCloudResolved output = FishResolveClear();
    if (busy)
    {
        // (A leaf or twig thinner than a texel against the sky is nearer than every ray round it: each is cut back
        // to the leaf's depth above, which keeps next to nothing of a sky ray's kilometres, so the canopy takes no
        // pale smudges of sky light, as the old "clear" here gave it.)
        // No texel looked at this pixel's surface (a post thinner than a texel, between hits): the one
        // nearest in depth.
        current = currentWeight > 1e-4 ? current / currentWeight : closest;
        current = float4(max(current.rgb, 0.0), saturate(current.a));

        // With the steadying off, the rebuild is only this frame's upscale.
        if (_FishCloudUpsample.w <= 0.0)
        {
            output.clouds = current;
            output.weight = 0.0;
        }
        else
        {
            // 2. Where what this pixel shows was last frame.
            float cloudSeen = currentWeight > 1e-4 ? cloudWeight / currentWeight : 0.0;
            motion = cloudWeight > 1e-6 ? motion / cloudWeight : float2(0.0, 0.0);
            float2 previousUV;
            float frames = 0.0;
            float4 history = current;
            float parallax;
            if (FishResolvePrevious(rawHere, here, camera, direction, forward, cloudSeen, motion, previousUV, parallax))
            {
                history = FishResolveCatmullRom(TEXTURE2D_ARGS(_FishCloudHistory, sampler_FishCloudHistory), previousUV, _FishCloudUpsample.xy);
                frames = SAMPLE_TEXTURE2D_LOD(_FishCloudHistoryWeight, sampler_FishCloudHistoryWeight, previousUV, 0).r;
                // One bad number kept in a history is kept for good: everything after is blended with it. The
                // bit-pattern test (Common.hlsl): the intrinsic isnan is folded away without /Gic.
                if (AnyIsNaN(history) || IsNaN(frames))
                {
                    history = current;
                    frames = 0.0;
                }
                // 3. Clipped toward this frame's neighbourhood, and trusted less the further it had to be
                // pulled: a history two box-widths out is of a cloud that has gone, three is let go of — when
                // the change is real. A box is narrowest where the cloud is bright and smooth, and there a few
                // per cent of one frame's noise could stand the history three box-widths out: it was let go of
                // whole, and the pixel showed that frame's raw noise. So the letting go is also by how big the
                // change is, as a share of the brightness (luma) or of the opacity: none under half of
                // _FishCloudUpsample.z (the diagnostics' History Reset Change, 0.1 as shipped), all of it over
                // one and a half. A cloud that has gone or come is a change of most of either; noise is a few
                // per cent. The clip itself is unchanged — and no clip can tell a noise every texel shares from
                // a change: that has to be kept out of the march (its light phase is each pixel's own).
                // (The diagnostics may widen or narrow the clip, or turn it off: _FishCloudTemporal.zw.)
                if (matched > 0.5 && _FishCloudTemporal.w < 0.5)
                {
                    float4 mean = sum / matched;
                    float4 sigma = sqrt(max(sumSquares / matched - mean * mean, 0.0)) + FISH_RESOLVE_SIGMA_FLOOR;
                    float units;
                    float gamma = max(0.05, FISH_RESOLVE_GAMMA + _FishCloudTemporal.z);
                    float4 before = FishResolveToYCoCg(history);
                    float4 clipped = FishResolveClip(before, mean, gamma * sigma, units);
                    history = FishResolveFromYCoCg(clipped);
                    float change = max(abs(before.x - mean.x) / max(max(before.x, mean.x), FISH_RESOLVE_CHANGE_FLOOR), abs(before.w - mean.w));
                    float real = _FishCloudUpsample.z > 0.0 ? smoothstep(0.5 * _FishCloudUpsample.z, 1.5 * _FishCloudUpsample.z, change) : 1.0;
                    frames *= 1.0 - smoothstep(1.0, 3.0, units) * real;
                }
            }

            // 4. The moving average: all of this frame for a pixel with nothing behind it, one part in
            // _FishCloudTemporal.x once settled — fewer the more the fetch rode on the cloud's distance. A pixel's
            // history is carried from where its cloud stood a frame ago at ONE distance, the mean depth of all its
            // ray saw; the face the eye follows stands nearer, and while the camera moves the history is fetched a
            // little short every frame. Averaged over sixteen frames that is a lag of a pixel or two behind the
            // cloud, which swings to the other side when the camera turns back: the clouds wobbled as the camera
            // strafed, and only then (turning moves the fetch the same at every distance, so it is exact). The
            // average is shortened by the parallax this frame (FishResolvePrevious), so the lag is held to a
            // fraction of a pixel: none at all for a camera that only turns or stands, a quarter as long for a
            // cloud whose fetch moved a pixel.
            float framesCap = max(1.0, _FishCloudTemporal.x) / (1.0 + FISH_RESOLVE_PARALLAX_FRAMES * parallax);
            float settled = min(min(frames + 1.0, max(1.0, _FishCloudTemporal.x)), max(2.0, framesCap));
            float4 result = lerp(history, current, 1.0 / settled);
            output.clouds = float4(max(result.rgb, 0.0), saturate(result.a));
            output.weight = settled;
        }
    }
    return output;
}

#endif
