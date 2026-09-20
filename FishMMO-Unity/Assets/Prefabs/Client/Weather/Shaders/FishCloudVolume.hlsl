#ifndef FISH_CLOUD_VOLUME_INCLUDED
#define FISH_CLOUD_VOLUME_INCLUDED

// The volumetric clouds: a 3D noise field over the whole sky, cut into bands of altitude.
//
// There are no cloud objects. Each layer is a slice of atmosphere — sea level to the cloud base,
// the cloud deck above it, the sheet above that — and within a slice the cloud is wherever the
// noise survives the coverage the weather asks for. What looks like one cloud is a connected piece
// of that field, so clouds merge, split and grow the way they do in the sky, and none of them is
// the same shape as another because none of them was a shape to begin with.
//
// A pixel's ray is marched once through the whole stack: long strides through the empty air between
// bands, short steps inside cloud. Light is integrated the same way toward the sun, so a band below
// another is shadowed by it without anyone having to arrange that.

#include "FishSkyCommon.hlsl"

#define FISH_CLOUD_MAX_LAYERS 6

TEXTURE3D(_FishCloudShape);
SAMPLER(sampler_FishCloudShape);
TEXTURE3D(_FishCloudDetail);
SAMPLER(sampler_FishCloudDetail);

float4 _FishCloudLayerA[FISH_CLOUD_MAX_LAYERS];  // x bottom (m), y top (m), z coverage 0..1, w optical density
float4 _FishCloudLayerB[FISH_CLOUD_MAX_LAYERS];  // x noise scale (m), y detail scale (m), z detail strength, w stretch along wind
float4 _FishCloudLayerC[FISH_CLOUD_MAX_LAYERS];  // x wind scale, y base softness, z top softness, w rain 0..1
float4 _FishCloudLayerD[FISH_CLOUD_MAX_LAYERS];  // x convection, yz cover change per metre across the ground, w vertical noise scale (x band thickness)
// How far this band's noise has been carried by the wind, already in the frame the noise is read in
// (x along the wind, y across it) and already wrapped to the band's own period, so a float holds it
// without losing metres. zw the same for the detail volume, on the world axes.
float4 _FishCloudLayerE[FISH_CLOUD_MAX_LAYERS];
// x how much of a change in the forecast reaches this band (the slope of its response), y 1 when
// the band follows the forecast at all (0 for the fog band, which follows the fog).
float4 _FishCloudLayerF[FISH_CLOUD_MAX_LAYERS];
float4 _FishCloudLayerTint[FISH_CLOUD_MAX_LAYERS]; // rgb what a shaded part of this band tends toward
int _FishCloudLayerCount;
// The formations: xy the drift the formation field is read through, wrapped to its period; z the
// formation's own contribution at the camera, in cover units, already in the bands' figures and so
// to be taken back out; w its contrast here (rises with unstable air).
float4 _FishCloudMeso;
// x how much cover a formation adds or takes at its peak, y metres one tile covers, z tiles per
// period of the lattice, w unused.
float4 _FishCloudMesoParams;
int _FishCloudMesoSeed;

float4 _FishCloudLayer;      // x lowest bottom (m), y highest top (m), z planet radius (m), w background coverage
float4 _FishCloudShapeParams;// x detail fade start (m), y detail fade range (m), z shape warp, w overall density multiplier
// x: how wide one marched pixel's cone opens, in metres per metre of distance. y: the coarsest mip
// the shape volume may be read at. z: one over how much detail the far sky is asked to keep. w: spare.
float4 _FishCloudLodParams;
float4 _FishCloudWind;       // xy accumulated drift (m), z detail wind gain, w drift multiplier
float4 _FishCloudWindDir;    // xy the axis the noise is drawn out along: the steady prevailing wind, never the gusting one; z speed (m/s), w unused
float4 _FishCloudLight;      // x light steps, y powder, z forward scatter (g), w unused
float4 _FishCloudTypeParams; // x stratus..cumulus mix, y storm 0..1, z unused, w rain 0..1
float4 _FishCloudSunDir;     // xyz direction to whatever lights the clouds now, w its strength
float4 _FishCloudSunColor;   // rgb its colour
float4 _FishCloudAmbient;    // rgb the skylight a cloud sits in, already scaled
float4 _FishCloudTint;       // rgb what a shaded underside takes its colour from, a how much of it
float4 _FishCloudCoverage;   // x cut at no cover, y edge softness, z cut at full cover, w how hard it bends at the end
float4 _FishCloudHaze;       // rgb the colour distance turns a cloud, a metres over which it does
float4 _FishCloudShadowArea;   // x the cookie's texel (m), y how dark the ground goes under cloud, z window size (m), w steps
float4 _FishCloudShadowOrigin; // xyz the world point at the middle of the window
float4 _FishCloudShadowRight;  // xyz the light's right, across the window
float4 _FishCloudShadowUp;     // xyz the light's up, up the window

// ── The formations ─────────────────────────────────────────────────────
// A twin of WeatherDriver.Mesoscale, kept in step by hand: the same hash, the same lattice, the
// same octaves. The server reads the C# one to decide where it rains; this one decides where the
// cloud is drawn. They agree because they are the same function.

uint FishCloudWxHash(int x, int y, uint seed)
{
    uint h = seed;
    h ^= (uint)x * 0x9E3779B1u;
    h ^= (uint)y * 0x85EBCA77u;
    h ^= h >> 15;
    h *= 0x2545F491u;
    h ^= h >> 13;
    h *= 0xC2B2AE35u;
    h ^= h >> 16;
    return h;
}

float FishCloudWxValue(int x, int y, uint seed)
{
    return (FishCloudWxHash(x, y, seed) & 0xFFFFFFu) / 16777215.0;
}

int FishCloudWrapCell(int cell, int period)
{
    return ((cell % period) + period) % period;
}

float FishCloudPeriodicNoise(float2 p, int period, uint seed)
{
    float2 f = floor(p);
    int2 i = (int2)f;
    float2 t = p - f;
    t = t * t * (3.0 - 2.0 * t);
    int x0 = FishCloudWrapCell(i.x, period), x1 = FishCloudWrapCell(i.x + 1, period);
    int y0 = FishCloudWrapCell(i.y, period), y1 = FishCloudWrapCell(i.y + 1, period);
    float a = FishCloudWxValue(x0, y0, seed), b = FishCloudWxValue(x1, y0, seed);
    float c = FishCloudWxValue(x0, y1, seed), d = FishCloudWxValue(x1, y1, seed);
    return lerp(lerp(a, b, t.x), lerp(c, d, t.x), t.y);
}

/// The formation term at a drifted ground position, -1..1: where in this sky the banks and the gaps
/// are. Two octaves at the scale of cloud masses, not clouds — a system is a hundred kilometres and
/// a cloud a few, and this is the organisation in between that a sky has and a noise field does not.
float FishCloudMesoscale(float2 driftedMetres)
{
    float2 p = driftedMetres / max(1.0, _FishCloudMesoParams.y);
    int period = max(1, (int)_FishCloudMesoParams.z);
    uint seed = (uint)_FishCloudMesoSeed;
    float n = FishCloudPeriodicNoise(p, period, seed) * 0.65
        + FishCloudPeriodicNoise(p * 2.3 + float2(11.7, -4.1), period * 23 / 10, seed ^ 0x5BD1E995u) * 0.35;
    return clamp((n - 0.5) * _FishCloudMeso.w, -1.0, 1.0);
}

// ── The field ──────────────────────────────────────────────────────────

// Height above the ground, in metres, on a curved world. The bands are shells around the planet, so
// they meet the horizon by curving away rather than by running out.
float FishCloudAltitude(float3 position)
{
    float radius = max(1000.0, _FishCloudLayer.z);
    return length(position - float3(0.0, -radius, 0.0)) - radius;
}

/// The mip to read a volume at, given how much world a sample stands for and how much world one of
/// that volume's texels covers. Each mip doubles the texel, so the level that matches a footprint is
/// its log base two. Never below 0 — there is nothing finer than mip 0 to ask for — and never above
/// `coarsest`, past which the volume has averaged itself down to a single grey and the sky with it.
float FishCloudLod(float footprint, float texelMetres, float coarsest)
{
    return clamp(log2(max(1.0, footprint / max(0.5, texelMetres))), 0.0, min(coarsest, _FishCloudLodParams.y));
}

// Where in its band a height sits, and how much cloud that height can hold. A band is solid a
// little way above its floor and thins toward its ceiling, which is what gives a deck a flat base
// and a ragged top.
float FishCloudBandProfile(float height01, float baseSoftness, float topSoftness)
{
    float rise = saturate(height01 / max(0.02, baseSoftness));
    float fall = saturate((1.0 - height01) / max(0.05, topSoftness));
    return rise * fall;
}

/// How much cloud there is at a point, and which band it belongs to.
///
/// `detailAmount` is how much of the fine erosion to do: none on the cheapest tier, and none when a
/// rough answer will do, as in the light and shadow marches.
///
/// `footprint` is how much world the sample stands for, in metres — the wider of the step along the
/// ray and the cone one pixel has opened to by the time it gets here. It picks the mip: a noise
/// feature smaller than the footprint cannot be resolved, and asking for it anyway is what returns
/// a different answer on every neighbouring pixel, which is the fuzz. Reading it from a mip whose
/// texels are about a footprint wide gives the average of what is in there instead, which is the
/// right answer and, being a quarter of the memory traffic per level, a cheaper one.
/// The formation term for a column, in cover units with the camera's share taken out. Read once per
/// view sample and handed down: the light march's samples are within a few kilometres of it and the
/// formations are tens of kilometres across, and it is eight hashes a call.
float FishCloudMesoAt(float2 xz)
{
    return FishCloudMesoscale(xz - _FishCloudMeso.xy) * _FishCloudMesoParams.x - _FishCloudMeso.z;
}

float FishCloudDensityAt(float3 position, float detailAmount, float footprint, float meso, out float high01, out int layerIndex)
{
    high01 = 0.0;
    layerIndex = 0;
    float altitude = FishCloudAltitude(position);
    if (altitude <= _FishCloudLayer.x || altitude >= _FishCloudLayer.y)
    {
        return 0.0;
    }

    // The axis the noise is drawn out along, and the frame every band's drift arrives in. It has
    // to be the *steady* prevailing wind — never the gusting one the weather reports, and never the
    // drift normalised. Rotating the lookup about the world origin is harmless only while the axis
    // holds still: the moment it turns, the whole field turns with it, and a camera kilometres out
    // watches the entire sky sweep past. The reported wind turns whenever a cell drifts by or a
    // layer fades in, so a sky read on that axis circled the scene every time the weather changed.
    // A zero here would collapse the sampling frame and with it the whole field, so an unset
    // global falls back to due north rather than to a sky of one flat colour.
    float2 wind = dot(_FishCloudWindDir.xy, _FishCloudWindDir.xy) > 1e-6 ? _FishCloudWindDir.xy : float2(0.0, 1.0);
    float2 across = float2(-wind.y, wind.x);
    float2 frame = float2(dot(position.xz, wind), dot(position.xz, across));
    // The storm cells the server sends.
    float4 weather = FishWeatherMapAt(position.xz);
    float storm = saturate(max(_FishCloudTypeParams.y, weather.a));
    // `meso` is where this column stands in the formations: in the bank, or in the gap, as a
    // change in cover with the camera's own share taken back out — the bands' figures were
    // measured at the camera and already carry it. Read through the same drift the weather field
    // is read through, so the bank overhead is the bank the rain falls out of.

    float best = 0.0;
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        if (altitude <= a.x || altitude >= a.y)
        {
            continue;
        }
        float4 b = _FishCloudLayerB[i];
        float4 c = _FishCloudLayerC[i];
        float4 d = _FishCloudLayerD[i];
        float4 e = _FishCloudLayerE[i];
        float4 f = _FishCloudLayerF[i];
        // Packed: the units are the rain, and a band that grows storms carries a 2 on top, so that
        // rain alone can never trip the storm test and a storm band's rain is still legible.
        bool growsStorms = c.w >= 2.0;
        float bandRain = c.w - (growsStorms ? 2.0 : 0.0);

        // Coverage, as a slope across the ground rather than one number for the whole sky. A front
        // is a gradient in cloud: with a single figure the cut drops everywhere at once and cloud
        // appears in place across the entire sky, which is what made the weather look like it was
        // materialising instead of arriving. The shape always drifted — it was the amount that never
        // travelled. Measured at the camera and carried outward, so the sky thickens on the side the
        // weather is coming from and thins on the side it has left.
        float2 fromViewer = position.xz - _WorldSpaceCameraPos.xz;
        // Clamped, because this is a straight line standing in for a curve. It is a good likeness
        // within a few kilometres of the camera and a poor one at the horizon, where the ray runs
        // tens of kilometres out — half a weather system away — and the line has long since left the
        // field behind. Unclamped it drives the far sky to nothing on one side and to solid on the
        // other, which reads as the sky simply ending in one direction.
        float tilt = clamp(dot(d.yz, fromViewer), -0.35, 0.35);
        // The forecast at the camera, the slope of the front, and the formation here — the last two
        // put through this band's own response to a change in the forecast.
        float coverage = saturate(a.z + tilt + meso * f.x);
        // A storm makes its own cloud wherever the cell stands, whatever the day is doing.
        if (storm > 0.01 && growsStorms)
        {
            coverage = max(coverage, storm);
        }
        // Tested here and not on the band's figure at the camera. Tested there, a band that happens
        // to be clear overhead is skipped outright — and then the cloud rolling in from upwind
        // cannot be drawn at all until it has already arrived, which is the popping this is meant
        // to cure.
        if (coverage <= 0.002)
        {
            continue;
        }

        // This band's own slice of the noise, blown along by its own share of the wind, in the
        // wind's frame. *Subtracted*: the value here is the value the air brought with it from
        // upwind, so the pattern at p is the field at p minus the drift. It was added, which slid
        // every cloud in the sky against the wind while the weather field underneath moved with
        // it — the front thickened the sky on the upwind side and the clouds crossed it going the
        // other way, which is what "clouds disappear in the direction they are moving" was.
        // Wrapped on the CPU to this band's own period, in double, so a float holds it exactly.
        float2 along = frame - e.xy;

        // A cloud's base is flat because air rising off the ground cools as it expands and reaches
        // its dew point at one height across the whole region — the band's floor is that level, and
        // it is the same everywhere. Its top is lumpy because each parcel of air keeps rising until
        // it runs out of buoyancy or moisture, and no two stop in the same place. So the floor is
        // fixed and the ceiling is a broad noise on the ground plane: level underneath, cauliflower
        // on top, which is the shape convection actually makes.
        float convection = d.x;
        float ceiling = 1.0;
        if (convection > 0.001)
        {
            // Twice the band's tile, in the wind's frame, so its period divides the drift's wrap.
            float3 lifted = float3(along / max(1.0, b.x * 2.0), 0.31 + i * 0.11);
            float liftLod = FishCloudLod(footprint, b.x * 2.0 * (1.0 / 128.0), 6.0);
            float lift = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, lifted, liftLod).r;
            // Spread over the field's own range. The red channel runs about 0.33 to 0.76, so read
            // raw it never reached 1 and the top of every band went unused: with convection at 1 the
            // tallest column stopped at 83% of the band. Remapped, the tops run from 30% to the
            // band's real top. Kept a spread and never a clamp: clamping the reach gave every high
            // column the very same ceiling and the deck grew a flat table with a hard rim.
            float reach = (lift - 0.33) / 0.43;
            ceiling = lerp(1.0, 0.3 + reach * 0.7, convection);
        }

        float h = saturate((altitude - a.x) / max(1.0, a.y - a.x));
        // Dividing by the ceiling raises the top of the profile to where this column reached while
        // leaving h = 0 — the condensation level — exactly where it is.
        float profile = FishCloudBandProfile(h / max(0.15, ceiling), c.y, c.z);
        if (profile <= 0.0)
        {
            continue;
        }
        along.x /= max(1.0, b.w);
        // The vertical scale is the band's own thickness, not the horizontal tile. Sampled on the
        // same scale going up as going along, the noise moved less than a tenth of a tile over a
        // band's whole height — so a column held one value from floor to ceiling and every cloud
        // came out a flat slab, with all its vertical shape coming from the profile curve, which is
        // the same curve everywhere. Tying it to the thickness puts real lumps and hollows in.
        float verticalTile = max(50.0, (a.y - a.x) * d.w);
        // Height from the band's own floor, not from the ground: a band that follows the
        // condensation level moves, and read from the ground the noise stayed put while the band
        // slid through it, so every cloud changed shape as the deck rose or fell.
        float3 uv = float3(along.x / max(1.0, b.x), (altitude - a.x) / verticalTile, along.y / max(1.0, b.x)) + i * 0.37;
        // The shape volume is 128 cubed and wraps, so a band whose tile is 4 km repeats that same
        // 4 km for ever — a grid of identical clouds, which is what "the clouds are tiled" means.
        // Warping the lookup by a much coarser noise breaks the lattice: the warp's period is
        // deliberately not a multiple of the tile's, so the two never line up again within any
        // distance that can be seen, and because the warp is low frequency it slides and stretches
        // whole cloud masses rather than shredding them.
        // The warp is read from the shape volume, not the detail one. The detail volume is 32 texels
        // a side, so stretched over this distance one texel spans hundreds of metres, and trilinear
        // interpolation between them is straight-sided: displacing by kilometres along a field made
        // of flat facets folds those facets into the cloud, and the tops come out as hard-edged
        // terraces. The shape volume is 128 a side — four times finer over the same ground — and is
        // already in cache from the sample below.
        // The warp field has to be far coarser than the shape it bends, and bend it gently. What
        // matters is not the size of the displacement but how fast it changes: at a tile of 4.3x the
        // shape's and a strength of 0.35, the lookup moved about five times faster from the warp
        // than from actually going anywhere — so the cloud was not being warped, it was being
        // replaced by the warp, and that field's own lattice came through as combed, hairy edges.
        // At 42x and the default strength the warp contributes about a quarter of the motion, which
        // bends the tiling without printing itself on the sky. 42 rather than 43: the drift is
        // wrapped to 42 tiles, and the lift field above at 2 tiles divides that, so all three read
        // the same field either side of the wrap. (The lattice does not repeat within any visible
        // distance either way — 42 tiles is 336 km.)
        float3 warpUV = float3(along.x, altitude * 0.25, along.y) / max(1.0, b.x * 42.0) + i * 0.19;
        // The warp stays at mip 0 whatever the distance. Its tile is 43x the shape's, so one of its
        // texels is already hundreds of metres across and no footprint this march produces comes
        // near it — and blurring it would not remove an alias, it would straighten out the very
        // bend that stops the shape's lattice repeating.
        float3 warp = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, warpUV, 0).rgb;
        uv += (warp - 0.5) * _FishCloudShapeParams.z;
        // The mip the shape is read at. The texel to compare against is the *finest* of the three
        // axes, not the horizontal one: the vertical tile is the band's own thickness, which at a
        // 1.6 km band is a good deal finer than an 8 km horizontal tile, so it is the axis that
        // aliases first and the one the mip has to answer to.
        float shapeLod = FishCloudLod(footprint, min(b.x, verticalTile) * (1.0 / 128.0), 6.0);
        float4 shape = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, uv, shapeLod);
        // Perlin billowed by the Worley channels: solid cores, cauliflower edges.
        float billow = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
        float floorValue = (1.0 - billow) * 0.45;
        float body = saturate((shape.r - floorValue) / max(0.05, 1.0 - floorValue));
        // Put back what the mip took, so that filtering the far sky does not also thin it.
        //
        // Measured over the baked volume, level by level: this figure's mean falls 0.00445 a level,
        // dead straight, and its spread narrows by about 1.8% of a level squared (1.011x at mip 1,
        // 1.048x at mip 2, 1.163x at mip 3). Both are small, and both matter, because what is cut
        // against the threshold is the *tail* of this distribution and not its middle: at mip 2 the
        // top of the field had come down 0.0135, which against an edge softness of 0.06 is a fifth
        // of a cloud's density gone — enough, through the exponential, to take the broken sky from
        // 38% cover to 19%. Mapping the mip's mean and spread back onto mip 0's restores the
        // quantiles to within 0.002 across the whole range.
        float restore = 1.0 + 0.018 * shapeLod * shapeLod;
        body = saturate(0.5642 + (body - (0.5642 - 0.00445 * shapeLod)) * restore);

        // What survives. The cut is on the noise *times the band's profile*, so a cloud narrows as
        // it nears the top of its band and the deck keeps its flat floor.
        //
        // Where the cut sits, from the noise's own quantiles.
        //
        // This field is not spread over 0..1: measured, it runs 0.33 to 0.76 and sits around 0.57,
        // so a cut anywhere outside that window gives a sky that is either empty or solid, with
        // nothing in between. These three numbers follow the measured curve — the cut falls
        // steadily as the forecast rises and then drops away at the very top, which is what turns
        // the last of the gaps into an overcast.
        float coverCurve = coverage * coverage;
        float threshold = lerp(_FishCloudCoverage.x, _FishCloudCoverage.z, coverage) - _FishCloudCoverage.w * coverCurve * coverCurve;
        float density = saturate((body * profile - threshold) / max(0.05, _FishCloudCoverage.y));
        if (density <= 0.0)
        {
            continue;
        }

        // Detail eats the edges into wisps, more so where the cloud is already thin.
        if (detailAmount > 0.0 && b.z > 0.0)
        {
            // Carried by the same wind as its band, wrapped to its own tile. It used to move at a
            // fixed two and a half times the drift whatever the band's own share was, so the wisps
            // slid through the cloud they were eating — four times faster than the fog band, slower
            // than the cirrus.
            float3 detailUV = float3(position.x - e.z, position.y, position.z - e.w) / max(20.0, b.y) + i;
            // Mip 0, always — and this one is measured, not assumed. The detail does not add to the
            // cloud, it *eats* it: `density - wisp * strength * (1 - density)`. What a mip returns is
            // the mean of the texels it covers, so a blurred wisp stops being high in some places and
            // low in others and becomes the same middling figure everywhere — which does not soften
            // the erosion, it applies it uniformly to cloud that used to keep its edges. Reading the
            // 32-cubed volume at mip 3 took the broken sky from 38% cover to 8%. Distance is already
            // handled for this volume the right way, by fading the term out entirely.
            float3 detail = SAMPLE_TEXTURE3D_LOD(_FishCloudDetail, sampler_FishCloudDetail, detailUV, 0).rgb;
            float wisp = detail.r * 0.625 + detail.g * 0.25 + detail.b * 0.125;
            // Wispy underneath, billowy on top.
            wisp = lerp(1.0 - wisp, wisp, saturate(h * 2.0));
            density = saturate(density - wisp * b.z * detailAmount * (1.0 - density));
        }

        density *= a.w * (1.0 + (bandRain + (growsStorms ? 1.0 : 0.0)) * storm * 0.8);
        if (density > best)
        {
            best = density;
            // Measured against the height this column actually reached, not against the band's
            // ceiling: a low heap's crown is still its crown, and should take the skylight a crown
            // takes rather than be shaded as though it were half way up a tall one.
            high01 = saturate(h / max(0.15, ceiling));
            layerIndex = i;
        }
    }
    // In per-metre extinction, which the field was not: read raw, a full point was 1.1 a metre,
    // so one 90 m step had an optical depth of ninety-nine and any sample with a twentieth of
    // that was already opaque. Every edge was decided by a single sample — solid or nothing —
    // which is what made the sky read as a noise field cut by a threshold, whatever the softness
    // was set to, and it starved the light march the same way (a few hundred metres to the sun
    // was black, so nothing but the rim ever caught the light). Real cloud extinguishes at a few
    // hundredths a metre: a 300 m heap is opaque, a 60 m wisp lets a fifth through, and an edge
    // fades over the dozens of metres it actually has.
    return best * _FishCloudShapeParams.w * 0.03;
}

// The same, for callers that do not care which band answered.
float FishCloudDensity(float3 position, float detailAmount, float footprint, float meso, out float high01)
{
    int layer;
    return FishCloudDensityAt(position, detailAmount, footprint, meso, high01, layer);
}

// ── Lighting ───────────────────────────────────────────────────────────

// Henyey-Greenstein, two lobes: a strong forward glow and a soft back scatter.
float FishCloudPhase(float cosAngle, float g)
{
    float g2 = g * g;
    float forward = (1.0 - g2) / (4.0 * PI * pow(abs(1.0 + g2 - 2.0 * g * cosAngle), 1.5));
    float back = (1.0 - g2 * 0.25) / (4.0 * PI * pow(abs(1.0 + g2 * 0.25 + 0.5 * g * cosAngle), 1.5));
    return lerp(back, forward, 0.6);
}

// How much cloud stands between a point and the sun: the optical depth that way, in a few steps
// that grow as they go. Because it asks the whole field, a band below another is shadowed by it.
float FishCloudLightDepth(float3 position, float3 toSun, float detailAmount, float footprint, float meso)
{
    int steps = (int)max(1.0, _FishCloudLight.x);
    float step = 120.0;
    float density = 0.0;
    float high01;
    // A cone around the sun's own direction. The samples used to be pushed along one fixed world
    // vector, whatever the sun was doing, so every cloud in the world was shaded as if the light
    // leaned toward the north-west — the same side lit however the sun stood.
    float3 side = normalize(cross(toSun, abs(toSun.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0)));
    float3 up = cross(toSun, side);
    UNITY_LOOP
    for (int k = 0; k < steps; k++)
    {
        float grow = 1.0 + k * 0.6;
        float reach = step * (k + 1) * grow;
        // Each step lands on a different spoke of the cone, so the shading is soft instead of banded.
        float spoke = k * 2.399;
        float3 offset = toSun * reach + (side * cos(spoke) + up * sin(spoke)) * reach * 0.2;
        density += FishCloudDensity(position + offset, detailAmount * 0.5, footprint, meso, high01) * step * grow;
        // Past this nothing gets through whatever the rest of the way holds; the remaining steps
        // would only be spent confirming it.
        if (density > 10.0)
        {
            break;
        }
    }
    return density;
}

// What that depth lets through. One pass of Beer's law gives a bright rim and a black body, which
// is what a cloud would look like if light bounced once. It bounces many times; the usual stand-in
// is a few octaves that each attenuate less, contribute less and scatter more evenly than the last.
float FishCloudScatter(float depth, float cosAngle, float g, float intoCloud)
{
    float attenuation = 1.0;
    float contribution = 1.0;
    float eccentricity = g;
    float energy = 0.0;
    UNITY_UNROLL
    for (int o = 0; o < 3; o++)
    {
        float beer = exp(-depth * 0.9 * attenuation);
        // The powder effect stands for the light that has not yet worked its way in from a cloud's
        // surface, so what it must be keyed to is how far into the cloud *this view ray* has come.
        // It used to be keyed to the depth toward the sun, which is the opposite measure: the least
        // shadowed point of all — the sunlit crown — came out darkest, and a point with more cloud
        // between it and the sun came out brighter. On a deep storm the error hid in the middle of
        // the curve, but a thin deck like fair weather or mist sits entirely on the rising part of
        // it, so the whole sky was lit from underneath and shaded on top.
        float powder = 1.0 - exp(-intoCloud * 3.0 * attenuation);
        float phase = FishCloudPhase(cosAngle, eccentricity);
        energy += contribution * beer * lerp(1.0, 0.5 + 0.5 * powder, _FishCloudLight.y) * phase;
        attenuation *= 0.5;
        contribution *= 0.55;
        eccentricity *= 0.6;
    }
    return energy;
}

// The colour a cloud is here. Not one grey: a sunlit crown takes the sun's own colour — red at dawn,
// long before that light reaches the ground — while what is shaded takes the sky's, tinted by the
// band it belongs to and darker toward the base.
float3 FishCloudColour(float3 sunColour, float scatter, float high01, int layerIndex, float rain, float3 ambient)
{
    // 3.5, not 9. There is no tonemapping in the project, so whatever this produces above 1 clips
    // to white: at 9 half of every cloud pixel measured exactly 1.0 and the sunlit top, the lit
    // rim and the thin edge were all the same white. At 3.5 a top facing away from the sun sits
    // just under white, the rim toward it still burns out — which is what a silver lining is —
    // and the underside, lit by the sky alone, comes out darker than the sky beside it.
    float3 lit = sunColour * scatter * 3.5 * (1.0 - rain * 0.45);
    float3 bandTint = _FishCloudLayerTint[clamp(layerIndex, 0, FISH_CLOUD_MAX_LAYERS - 1)].rgb;
    float3 shaded = lerp(ambient, ambient * bandTint, saturate(_FishCloudTint.a + rain * 0.35));
    return lit + shaded * (0.5 + 0.5 * high01) * (1.0 - rain * 0.35);
}

// ── March ──────────────────────────────────────────────────────────────

// Where a ray meets the whole stack of bands: the shell from the lowest floor to the highest ceiling.
bool FishCloudRange(float3 origin, float3 direction, out float near, out float far)
{
    float radius = max(1000.0, _FishCloudLayer.z);
    float3 centre = float3(0.0, -radius, 0.0);
    float3 toCentre = origin - centre;
    float b = dot(toCentre, direction);
    float c = dot(toCentre, toCentre);
    float inner = radius + _FishCloudLayer.x;
    float outer = radius + _FishCloudLayer.y;

    float innerDisc = b * b - (c - inner * inner);
    float outerDisc = b * b - (c - outer * outer);
    near = 0.0;
    far = 0.0;
    if (outerDisc < 0.0)
    {
        return false;
    }
    float outerNear = -b - sqrt(outerDisc);
    float outerFar = -b + sqrt(outerDisc);
    if (outerFar < 0.0)
    {
        return false;
    }
    float height = length(toCentre) - radius;
    if (height < _FishCloudLayer.x)
    {
        near = innerDisc < 0.0 ? 0.0 : -b + sqrt(innerDisc);
        far = outerFar;
    }
    else if (height > _FishCloudLayer.y)
    {
        near = max(0.0, outerNear);
        far = innerDisc < 0.0 ? outerFar : -b - sqrt(innerDisc);
    }
    else
    {
        // Inside the stack: flying between the bands, or standing in the weather band itself.
        near = 0.0;
        far = innerDisc < 0.0 || -b + sqrt(innerDisc) < 0.0 ? outerFar : -b - sqrt(innerDisc);
        far = max(far, 0.0);
    }
    return far > near;
}

// The world's curvature is already in the field twice over, and correctly: FishCloudRange
// intersects the ray with real spheres around the planet's centre, and FishCloudAltitude measures
// height from that same centre. A third correction used to be applied here, lifting each sample by
// travelled² / 2R — but `travelled` is the distance from the *camera*, so that warped the noise
// into a bowl centred on the viewer, and the bowl went with them: the field appeared to swim and
// circle as the camera moved, because it was anchored to the camera and not to the world.
// The sample position is the world position. Nothing to correct.

// Marches the clouds along a ray. Returns scattered light in rgb and transmittance in a. `depth` is
// how far the ray may travel before the world blocks it; `jitter` breaks up banding.
float4 FishCloudMarch(float3 origin, float3 direction, float depth, float jitter, int steps, float detailAmount, out float cloudDistance)
{
    cloudDistance = depth;
    float near, far;
    if (!FishCloudRange(origin, direction, near, far))
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    far = min(far, depth);
    near = max(near, 0.0);
    if (far <= near)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }

    // The stack is kilometres deep and most of it is empty air between bands. Stepping it evenly
    // would spend the budget on nothing, so the march strides until it finds cloud and takes short
    // steps while it is inside one.
    float distance = far - near;
    // The step inside cloud. It grows with distance from the camera: a cloud overhead is walked in
    // steps a third the size of one at the horizon, which is where the detail is spent and where
    // it can be seen. One step for the whole ray — and it was 90 m on nearly every ray, since any
    // ray through an eight-kilometre shell is long — meant the near sky was sampled as coarsely as
    // the far one, and the mip chosen from that step blurred the cloud overhead exactly as much as
    // the cloud at the horizon. That is what made the level of detail look as if it ran backwards.
    float fine = clamp(distance / steps, 18.0, 90.0);
    float fineNear = max(18.0, fine * 0.5);
    // A stride must never be able to step over a whole band, or the pixels whose stride happens to
    // straddle a thin deck find nothing while their neighbours find cloud — which is a crosshatch
    // across the sky, not noise, and no amount of temporal averaging removes it.
    float thinnest = 1e6;
    UNITY_LOOP
    for (int b = 0; b < _FishCloudLayerCount; b++)
    {
        float4 band = _FishCloudLayerA[b];
        // A band counts if it can have cloud anywhere the ray goes, not only overhead: a deck
        // that is clear at the camera can be solid ten kilometres upwind, and skipping it here
        // let the stride jump it there.
        bool follows = _FishCloudLayerF[b].y > 0.5;
        if ((band.z > 0.001 || follows) && band.w > 0.001)
        {
            thinnest = min(thinnest, max(50.0, band.y - band.x));
        }
    }
    float coarse = max(fine, min(fine * 6.0, thinnest * 0.5));

    float3 toSun = _FishCloudSunDir.xyz;
    float3 sunColour = _FishCloudSunColor.rgb * _FishCloudSunDir.w;
    float cosAngle = dot(direction, toSun);
    float3 ambient = _FishCloudAmbient.rgb;
    float rain = _FishCloudTypeParams.w;

    float3 scattered = float3(0.0, 0.0, 0.0);
    float transmittance = 1.0;
    // Offset by a whole stride, not a fine step: the jitter has to cover the phase of the grid the
    // march actually walks, or five sixths of that phase is the same on every pixel and whatever
    // the stride misses it misses in a fixed pattern.
    float travelled = near + coarse * jitter;
    bool found = false;
    bool inside = false;
    float emptyDistance = 0.0;
    float insideUntil = 0.0;
    // Four times the step count rather than three: the near sky is now walked at half the step,
    // and a deck overhead needs the extra room.
    int budget = steps * 4;
    // The light march is most of the cost of a sample and is only worth it while what the sample
    // adds can still be seen: deep inside, with little light left to reach the camera, the last
    // depth found stands in.
    float depthToSun = 0.0;

    UNITY_LOOP
    for (int i = 0; i < budget; i++)
    {
        // Stop once almost nothing more can reach the camera. At real extinction a ray no longer
        // dies on its first sample — it has to be integrated through the cloud — so where it is
        // allowed to stop is what the march costs. A twentieth is below what the composite shows.
        if (transmittance < 0.05)
        {
            // Report what the ray was about to reach, not where it was stopped: left at a
            // twentieth, that twentieth of the sky — and of the moon — showed through an overcast.
            transmittance = 0.0;
            break;
        }
        if (travelled >= far)
        {
            break;
        }
        float3 position = origin + direction * travelled;
        float high01;
        int layerIndex;
        // The detail noise tiles every few hundred metres over 32 texels — around twelve metres a
        // texel — and the march steps tens of metres, so far out it is sampled several texels at a
        // time and returns speckle rather than wisps. Fade it with distance: what it adds is only
        // legible near to hand anyway, and dropping it is what distance does to a cloud regardless.
        float detailHere = detailAmount * saturate(1.0 - (travelled - _FishCloudShapeParams.x) / _FishCloudShapeParams.y);
        // How much world this sample answers for. Two things widen it, and the wider wins. Along the
        // ray it is the step. Across the ray it is the pixel's cone, which opens with distance: at
        // roughly a thousandth of a radian a pixel, a sample twenty kilometres out is speaking for
        // twenty metres of sky, and its neighbour speaks for the twenty metres beside it. Ask a
        // field with ten-metre features for a point answer at that range and the two come back
        // uncorrelated — that is the fuzz, and it is why raising NoiseScale until the features are
        // bigger than the cone makes it go away. The footprint fixes it at the other end instead,
        // by reading a mip that has already done the averaging, which leaves the near sky free to
        // keep detail the far sky cannot carry.
        // The step here: small near the camera, the tier's full step far out — and half again
        // once the ray is well inside cloud, where what each sample adds is already dimmed by
        // everything in front of it and the edge that needed the fine steps is behind.
        float stepHere = clamp(travelled * 0.006, fineNear, fine) * (transmittance < 0.5 ? 1.5 : 1.0);
        // Scaled by the whole footprint and not just the cone: of the two terms the step is the
        // larger over most of the sky, so a knob that only touched the cone would not be a knob.
        // Half the step, because a sample stands for the half-step either side of it; the cone
        // takes over past about forty kilometres.
        float footprint = max(stepHere * 0.5, travelled * _FishCloudLodParams.x) * _FishCloudLodParams.z;
        float meso = FishCloudMesoAt(position.xz);
        float density = FishCloudDensityAt(position, inside ? detailHere : 0.0, footprint, meso, high01, layerIndex);
        if (density > 0.0)
        {
            if (!inside)
            {
                // A stride found an edge: step back and walk this stretch properly, because an edge
                // is exactly where a cloud is worth looking at closely.
                inside = true;
                emptyDistance = 0.0;
                // Walk back over the stretch the stride jumped, and hold the fine steps at least
                // until we are back here: the way in is where a cloud's edge lives.
                insideUntil = travelled;
                travelled = max(near, travelled - coarse);
                continue;
            }
            bool first = !found;
            if (first)
            {
                cloudDistance = travelled;
                found = true;
            }
            if (transmittance > 0.3 || first)
            {
                depthToSun = FishCloudLightDepth(position, toSun, detailHere, footprint, meso);
            }
            // How far this ray has already come through cloud: 0 at the near surface, toward 1
            // deep inside. That is what the powder term reads.
            float intoCloud = 1.0 - transmittance;
            float scatter = FishCloudScatter(depthToSun, cosAngle, _FishCloudLight.z, intoCloud);
            float3 colour = FishCloudColour(sunColour, scatter, high01, layerIndex, rain, ambient);
            colour += _FishWeatherCloud.w * float3(0.85, 0.9, 1.0) * 2.0;   // lightning lights the volume
            // Distance turns a cloud into the air in front of it: the far side of a sky is haze,
            // not white, and without this every bank reads as though it were a mile away.
            float haze = saturate(travelled / max(1.0, _FishCloudHaze.a));
            colour = lerp(colour, _FishCloudHaze.rgb, haze * haze);
            float clarity = exp(-density * stepHere);
            // Energy-conserving integration: what this step scatters, dimmed by what is in front.
            scattered += transmittance * colour * (1.0 - clarity);
            transmittance *= clarity;
            travelled += stepHere;
            emptyDistance = 0.0;
        }
        else if (inside)
        {
            emptyDistance += stepHere;
            travelled += stepHere;
            // Out the far side and past the point the stride found: stride on.
            if (travelled > insideUntil && emptyDistance > fine * 4.0)
            {
                inside = false;
            }
        }
        else
        {
            travelled += coarse;
        }
    }
    return float4(scattered, saturate(transmittance));
}

// How much cloud stands between a point on the ground and the sun: what the shadow cookie needs.
//
// `footprint` here is the cookie's own texel, not the march's step. This is the one caller that must
// say so. Everywhere else the footprint stands for how much world a sample speaks for, and the step
// is the honest answer — but the step down this ray is a whole band's thickness over four samples,
// hundreds of metres, and the mip is isotropic: blurring that far to smooth the integration along
// the ray would take the pattern *across* it with it, and the pattern across it is the entire output.
// The cookie is 4 km over 256 texels, so 15 m is what it can resolve and 15 m is what to ask for.
// Integration along the ray is the band walk's job below, and the jitter's.
float FishCloudShadowDepth(float3 origin, float3 toSun, int steps, float jitter, float footprint)
{
    if (toSun.y < 0.02)
    {
        return 0.0;
    }
    // Band by band, so every sample lands where there is cloud to find.
    //
    // This used to spread its steps evenly over the whole shell — ground to cirrus, eleven and a
    // half kilometres of it at a middling sun — which at a quarter of the view ray's step count put
    // one sample every 827 m. The deck that casts the shadow is 600 m thick, about 800 m along that
    // path: *one* sample. A ground texel's shadow was therefore very nearly a coin toss, and all
    // three complaints followed from that single fact. It looked spotty because it was noise. It did
    // not travel with the clouds because a drifting field does not move a coin toss, it re-rolls it.
    // And it seemed to race the clouds because the re-rolling changes far quicker than the cloud
    // that causes it ever moves.
    //
    // Walking each band's own slice of the ray spends the same handful of samples entirely inside
    // cloud. Curvature is ignored here: over the few kilometres between the ground and the deck it
    // is worth nothing, and the shadow is not where curvature shows.
    float density = 0.0;
    float high01;
    float meso = FishCloudMesoAt(origin.xz);
    int perBand = max(4, steps / max(1, _FishCloudLayerCount));
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        // A band with no cloud in it casts no shadow — but "no cloud" has to be judged for the
        // whole window, not the camera: a band that follows the forecast can be clear overhead
        // and solid at the window's edge, where the front or a formation has put it.
        bool follows = _FishCloudLayerF[i].y > 0.5;
        if ((a.z <= 0.002 && !follows) || a.w <= 0.001)
        {
            continue;
        }
        float enter = max(0.0, (a.x - origin.y) / toSun.y);
        float leave = (a.y - origin.y) / toSun.y;
        if (leave <= enter)
        {
            continue;
        }
        float step = (leave - enter) / perBand;
        UNITY_LOOP
        for (int k = 0; k < perBand; k++)
        {
            density += FishCloudDensity(origin + toSun * (enter + step * (k + jitter)), 0.0, footprint, meso, high01) * step;
        }
    }
    return density;
}

#endif
