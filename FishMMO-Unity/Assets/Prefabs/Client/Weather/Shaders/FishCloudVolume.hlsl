#ifndef FISH_CLOUD_VOLUME_INCLUDED
#define FISH_CLOUD_VOLUME_INCLUDED

// The volumetric clouds: a 3D noise field over the whole sky, cut into bands of altitude — and the fog,
// which is cloud whose base is the ground.
//
// There are no cloud objects. Each layer is a slice of atmosphere — the column from the cloud base up,
// the middle sheet, the high ice — and within a slice the cloud is wherever the noise survives the
// coverage the weather asks for. What looks like one cloud is a connected piece of that field, so
// clouds merge, split and grow the way they do in the sky, and none of them is the same shape as
// another because none of them was a shape to begin with. Under them all lies the fog
// (FishFogLayer.hlsl): the air the night has chilled to its dew point, cloud whose base is the ground,
// walked by the same march through the same noise volumes.
//
// A pixel's ray is marched once through the whole stack: one leap across the empty air between bands,
// and within a band steps laid out from the band's own edge and from each cloud's, short inside cloud. Light is integrated the same way toward the sun, so a band below
// another is shadowed by it without anyone having to arrange that.

#include "FishSkyCommon.hlsl"
// The fog's layer, and the noise volumes and the terrain map the clouds share with it.
#include "FishFogLayer.hlsl"

#define FISH_CLOUD_MAX_LAYERS 6

// The regimes of cloud the air makes (CloudClimate.Bands): the column from the cloud base up, the
// middle sheet, the high ice. Every figure is worked out from the air; nothing here is authored.
float4 _FishCloudLayerA[FISH_CLOUD_MAX_LAYERS];  // x bottom (m), y top (m) — for the column, the lowest base and highest tower anywhere in view — z most cover anywhere in view, w extinction: the column's C in β = C·h^⅔, a layer's β (1/m)
float4 _FishCloudLayerB[FISH_CLOUD_MAX_LAYERS];  // x noise scale (m), y detail scale (m), z the edge's critical mixing fraction (CloudClimate.CriticalMixingFraction), w stretch along wind
float4 _FishCloudLayerC[FISH_CLOUD_MAX_LAYERS];  // x wind scale, y base softness, z top softness, w rain 0..1
// x convection; y and z the shell this band's cloud can reach anywhere in view (m) — its own floor
// and top (A.xy, which its noise and profile are laid out in) widened by the storms' lowered bases and
// towers for the column and by their anvils for the high ice: what the march skips empty air by;
// w vertical noise scale (x band thickness).
float4 _FishCloudLayerD[FISH_CLOUD_MAX_LAYERS];
// How far this band's noise has been carried by the wind, already in the frame the noise is read in
// (x along the wind, y across it) and already wrapped to the band's own period, so a float holds it
// without losing metres. zw the same for the detail volume, in the same frame: one detail tile across,
// one tile times the stretch along (CloudClimate.DetailDriftPeriod).
float4 _FishCloudLayerE[FISH_CLOUD_MAX_LAYERS];
// x which regime (0 the column, 1 the middle, 2 the high ice: which of the air map's covers is its),
// y the mean diameter of its drops (µm; 0 for ice), which decides how they scatter, z 1 when the band
// is the cloud column, w the thinnest its cloud gets anywhere in view, for how far a step may climb.
float4 _FishCloudLayerF[FISH_CLOUD_MAX_LAYERS];
// rgb how much more of the sun reaches this band than reaches the ground (CloudClimate.SunGainAloft):
// the air under it, which at dusk has taken the blue out of the ground's sun and not yet out of a
// cloud's eight kilometres up.
float4 _FishCloudLayerSun[FISH_CLOUD_MAX_LAYERS];
// The air over the whole visible sky, place by place (CloudAirMap), and the map before it that the
// sky is still fading from. A: x low cover, y cloud base (m), z an ordinary cloud's top (m), w how
// high a tower can climb (m). B: x how vigorous the convection is, y middle cover, z high cover, w
// freezing level (m). Rect: xy corner (m), z size (m), w (current) 1 when there is a map; w
// (previous) how far the sky has faded to the current one.
TEXTURE2D(_FishCloudAirA);
SAMPLER(sampler_FishCloudAirA);
TEXTURE2D(_FishCloudAirB);
TEXTURE2D(_FishCloudAirPrevA);
TEXTURE2D(_FishCloudAirPrevB);
float4 _FishCloudAirRect;
float4 _FishCloudAirPrevRect;
// The wind with height, for how far a cloud leans (WindProfile.Of, the weather of the moment — a lean
// does not accumulate, so it may follow the weather): x the thermal wind's shear, (m/s) per m; y how
// fast an ordinary heap's air rises, m/s; z the cloud base, where the lean starts (m); w the ordinary
// cloud's top (m): anything above it is a tower, which only a storm's updraught carries there.
float4 _FishCloudShear;
// The ice: x the level where every drop has frozen (m); y what glaciated cloud takes out against
// liquid cloud of the same water (CloudClimate.GlaciatedRatio); z how fast cirrus ice falls (m/s);
// w the tropopause (m), where the shear turns.
float4 _FishCloudIce;
// rgb what the open ground sends back up, as radiance: its albedo times the sun and sky on it. What
// lights a cloud's base from below; w unused.
float4 _FishCloudGround;
// The air between the camera and a cloud: xyz its molecules' extinction at sea level (1/m, red, green,
// blue — this world's air, not ours), w how fast they thin with height (m). _FishCloudAerosol: x the
// haze's extinction at the ground (1/m), y its scale height (m).
float4 _FishCloudAirExtinction;
float4 _FishCloudAerosol;
// The tower lattice: x 1 when it is used, y metres one tile covers, z tiles per period, w unused.
// How high a tower there climbs is the air map's to say.
float4 _FishCloudColumn;
// xy the drift the tower lattice is read through, wrapped to its period. z how hard the mid-scale
// carving is. w unused.
float4 _FishCloudTowerDrift;
int _FishCloudTowerSeed;
// What hangs below a column's base: y rain 0..1, w what is falling takes out (1/m) — the same the
// world's own distance fog is given. x and z are unused: they were a second ground fog, the column's
// own, with a depth of its own that had nothing to do with the fog's physics; the fog is the fog
// layer's now (FishFogLayer.hlsl), walked by this march.
float4 _FishCloudSub;
// The ground under the sky (_FishCloudTerrain, declared in FishFogLayer.hlsl), twice over. r the
// mountain as the AIR feels it — the terrain smoothed over some 700 m — and gb how fast that rises
// toward +x and +z (metres a metre): everything that bends the cloud field reads these. a the ground
// itself, barely smoothed, for the fog that lies on it.
// Where the viewer is. Not _WorldSpaceCameraPos: the shadow cookie is drawn outside any camera, where
// that holds whichever camera happened to render last — the Scene view's, in the editor — so the
// front's slope was laid out from one place for the sky and from another for its shadow.
float4 _FishCloudViewer;
int _FishCloudLayerCount;
// The formations: xy the drift the formation field is read through, wrapped to its period; z the
// formation's own contribution at the camera, in cover units, already in the bands' figures and so
// to be taken back out; w its contrast here (rises with unstable air).
float4 _FishCloudMeso;
// x how much cover a formation adds or takes at its peak, y metres one tile covers, z tiles per
// period of the lattice, w unused.
float4 _FishCloudMesoParams;
int _FishCloudMesoSeed;

float4 _FishCloudLayer;      // x lowest bottom (m), y highest top (m), z planet radius (m), w low cover at the viewer
float4 _FishCloudShapeParams;// x how much of a marched texel's cone the finest detail is drawn down to, y unused, z shape warp, w unused
// x: how wide one marched pixel's cone opens, in metres per metre of distance. y: the coarsest mip
// the shape volume may be read at. z: one over how much detail the far sky is asked to keep. w: how
// far the clouds are drawn, in metres: they dissolve over the last quarter of it.
float4 _FishCloudLodParams;
float4 _FishCloudWind;       // xy accumulated drift (m), z detail wind gain, w drift multiplier
float4 _FishCloudWindDir;    // xy the axis the noise is drawn out along: the steady prevailing wind, never the gusting one; z speed (m/s), w unused
float4 _FishCloudLight;      // x light steps, y the sky's share of the ambient (SkySystem; 0 unset: all of it), z the column's drops' asymmetry (g), w unused
float4 _FishCloudTypeParams; // x unused, y storm 0..1, z unused, w rain 0..1
float4 _FishCloudSunDir;     // xyz direction to whatever lights the clouds now, w its strength
float4 _FishCloudSunColor;   // rgb its colour
float4 _FishCloudAmbient;    // rgb the sky's own light over a cloud: the upper hemisphere's mean radiance
float4 _FishCloudCoverage;   // x cut at no cover, y the noise's gradient at the cut (per tile), z cut at full cover, w how hard it bends at the end
float4 _FishCloudHaze;       // rgb the colour the air between turns a cloud, a unused by the march
float4 _FishCloudShadowArea;   // x the cookie's texel (m), y how dark the ground goes under cloud, z window size (m), w steps
float4 _FishCloudShadowOrigin; // xyz the world point at the middle of the window
float4 _FishCloudShadowRight;  // xyz the light's right, across the window
float4 _FishCloudShadowUp;     // xyz the light's up, up the window

// ── Diagnostics (VolumetricCloudDiagnostics, the render profile's inspector; SkySystem) ──
// Switches for taking the march apart to find where a fault comes from. Every one reads all zeros as
// the clouds as they ship, so a shader or a test that never sets them draws the default: the flags are
// "off" flags, and the scales arrive as their difference from the default.
// x 1: every sample at the middle of its step (no ray jitter; pass 0). y 1: the step no longer grows with
// distance. z the light march: 0 stratified, 1 fixed phase, 2 off. w the debug view (CloudDebugView):
// 0 the clouds, 1 transmittance, 2 step count, 3 light depth, 4 detail LOD, 5 step length.
float4 _FishCloudDiag;
// Each 1 off: x the droplets' diffraction spike, y multiple scattering, z the sky's light, w the ground's.
float4 _FishCloudDiagLight;
// x detail strength − 1, y the mixing shell's width − 1, z the step's length − 1, w the early exit − 0.05.
float4 _FishCloudDiagScale;
// The 2026-09-29 audit's fixes, each 1 to go back to the old behaviour (the profile's diagnostics, "Fixes
// under trial"; all zeros is as shipped): x each sample's extinction held over its whole step instead of
// the trapezoid between samples, y one light-march phase for the whole screen instead of each pixel's,
// z the carve eased by the step instead of by what the screen resolves, w the light march's shorter
// reach far off and deep in, and its switches at hard lines.
float4 _FishCloudFix;
// More of them: x 1 takes the diffused sun's far side as the cloud's base whatever the sun is doing,
// instead of measuring it along the light's own way through the cloud (FishCloudFarDepth); y 1 walks
// dense cloud and the haze under it as it used to (see `economy` in FishCloudMarch).
float4 _FishCloudFixB;
// A cloud's smooth base (the profile's Base Detail and Base Smooth Height): x 1 − how much of the
// eddies' and the cauliflower's pattern the base keeps, y how far up the cloud that eases off, as a
// share of its height. All zeros: none, the pattern the same all the way up.
float4 _FishCloudBase;
// What the march hands pass 0 in place of the clouds when _FishCloudDiag.w asks for a debug view.
static float4 FishCloudMarchDebug = float4(0.0, 0.0, 0.0, 1.0);

/// A heat map for the debug views, 0 blue through green and yellow to 1 red.
float3 FishCloudDebugHeat(float t)
{
    t = saturate(t);
    return saturate(float3(1.5 - abs(4.0 * t - 3.0), 1.5 - abs(4.0 * t - 2.0), 1.5 - abs(4.0 * t - 1.0)));
}

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

/// Gradient noise on a lattice that repeats every `period` cells, centred on 0.5 at the spread of
/// the value noise it replaced. Twin of WeatherDriver.PeriodicGradient: value noise can only peak
/// at its lattice points and its edges line up with the lattice's axes (measured: three times
/// likelier along north and east than any other way), so a sky cut from it had banks ruled
/// north-south and east-west. Gradient noise has no preferred direction.
float FishCloudGradientDot(int cx, int cy, float2 d, int period, uint seed)
{
    float angle = FishCloudWxValue(FishCloudWrapCell(cx, period), FishCloudWrapCell(cy, period), seed) * (PI * 2.0);
    return cos(angle) * d.x + sin(angle) * d.y;
}

float FishCloudPeriodicGradient(float2 p, int period, uint seed)
{
    float2 f = floor(p);
    int2 i = (int2)f;
    float2 t = p - f;
    float2 s = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
    float n00 = FishCloudGradientDot(i.x, i.y, t, period, seed);
    float n10 = FishCloudGradientDot(i.x + 1, i.y, t - float2(1.0, 0.0), period, seed);
    float n01 = FishCloudGradientDot(i.x, i.y + 1, t - float2(0.0, 1.0), period, seed);
    float n11 = FishCloudGradientDot(i.x + 1, i.y + 1, t - float2(1.0, 1.0), period, seed);
    float a = n00 + (n10 - n00) * s.x;
    float b = n01 + (n11 - n01) * s.x;
    return 0.5 + (a + (b - a) * s.y) * 0.868;
}

/// The formation term at a drifted ground position, -1..1: where in this sky the banks and the gaps
/// are. Two octaves at the scale of cloud masses, not clouds — a system is a hundred kilometres and
/// a cloud a few, and this is the organisation in between that a sky has and a noise field does not.
float FishCloudMesoscale(float2 driftedMetres)
{
    float2 p = driftedMetres / max(1.0, _FishCloudMesoParams.y);
    int period = max(1, (int)_FishCloudMesoParams.z);
    uint seed = (uint)_FishCloudMesoSeed;
    float n = FishCloudPeriodicGradient(p, period, seed) * 0.65
        + FishCloudPeriodicGradient(p * 2.3 + float2(11.7, -4.1), period * 23 / 10, seed ^ 0x5BD1E995u) * 0.35;
    return clamp((n - 0.5) * _FishCloudMeso.w, -1.0, 1.0);
}

/// A twin of WeatherDriver.Tower: where, within a sky, the air goes all the way up. One candidate
/// tower per lattice cell at a hashed place, size and strength, round, with a flat core — not the
/// peaks of a value-noise lattice, which put every tower on a nine-kilometre square grid. The
/// server reads the C# one to put the rain under them.
float FishCloudTower(float2 driftedMetres)
{
    float2 p = driftedMetres / max(1.0, _FishCloudColumn.y);
    int period = max(1, (int)_FishCloudColumn.z);
    uint seed = (uint)_FishCloudTowerSeed;
    int2 i = (int2)floor(p);
    float best = 0.0;
    [unroll] for (int dy = -1; dy <= 1; dy++)
    {
        [unroll] for (int dx = -1; dx <= 1; dx++)
        {
            int cx = i.x + dx, cy = i.y + dy;
            int wx = FishCloudWrapCell(cx, period), wy = FishCloudWrapCell(cy, period);
            float strength = smoothstep(0.0, 1.0, saturate((FishCloudWxValue(wx, wy, seed) - 0.55) / 0.30));
            float2 jitter = 0.2 + 0.6 * float2(FishCloudWxValue(wx, wy, seed ^ 0x68E31DA4u), FishCloudWxValue(wx, wy, seed ^ 0xB5297A4Du));
            float radius = 0.65 * (0.75 + 0.5 * FishCloudWxValue(wx, wy, seed ^ 0x1B56C4E9u));
            float d = length(p - (float2(cx, cy) + jitter)) / radius;
            float t = saturate((d - 0.35) / 0.65);
            best = max(best, strength * (1.0 - t * t * (3.0 - 2.0 * t)));
        }
    }
    return best;
}

// ── The field ──────────────────────────────────────────────────────────

// Height above the ground, in metres, on a curved world. The bands are shells around the planet, so
// they meet the horizon by curving away rather than by running out.
// ── The planet, in numbers a float can hold ───────────────────────────
//
// The bands are shells about the planet's centre, which is 6,371 km under the origin. Height above
// the ground was length(position − centre) − radius: a number of a few hundred, got by taking one
// number of six million from another. A float has about seven figures. At six million it counts
// in HALF METRES — so every sample's altitude was rounded to the nearest 0.5 m, and the camera's
// own height above the centre was too, which is why a camera at y = 0.74 and one at y = 0.75 drew
// different skies: 0.75 is exactly half way between the two heights a float can tell apart there,
// and a centimetre's move tipped it from one to the other. The ray–shell intersections were
// worse: b² − (c − r²) takes numbers of 4×10¹³ from each other to leave one of a thousand, and
// came out a metre or more wrong. The band logic then compared the one imprecise figure with the
// other across a margin of a metre. The error is a fine sawtooth in the angle of view, and a fine
// sawtooth read off a grid of pixels is a bullseye round the point overhead.
//
// Nothing here forms the large numbers at all. For a point p near the origin and a centre
// (0, −R, 0):   |p − centre|² − R²  =  p·p + 2R·p.y   — every term of which is small. Call it
// `over`; then for a height h,  over = h(2R + h),  so  h = over / (R + √(R² + over)),  which adds
// where the old form subtracted.

/// |position − centre|² − R², without forming either.
float FishCloudOver(float3 position, float radius)
{
    return dot(position, position) + 2.0 * radius * position.y;
}

float FishCloudAltitude(float3 position)
{
    float radius = max(1000.0, _FishCloudLayer.z);
    float over = FishCloudOver(position, radius);
    return over / (radius + sqrt(max(0.0, radius * radius + over)));
}

/// Where a ray meets the shell at a given height, nearest first (either may be behind the ray).
/// False when it misses.
///
/// |o + d·t − centre|² = (R + h)²  is  t² + 2B·t + C = 0  with  B = o·d + R·d.y  and
/// C = over(o) − h(2R + h). Of its two roots −B ± √(B² − C), one adds and one subtracts; the one
/// that adds is taken, and the other is had from it as C over it, since the two multiply to C. So
/// neither is ever the small difference of two large numbers, whichever way the ray points.
bool FishCloudShell(float3 origin, float3 direction, float height, out float first, out float second)
{
    float radius = max(1000.0, _FishCloudLayer.z);
    float B = dot(origin, direction) + radius * direction.y;
    float C = FishCloudOver(origin, radius) - height * (2.0 * radius + height);
    float disc = B * B - C;
    first = 0.0;
    second = 0.0;
    if (disc < 0.0)
    {
        return false;
    }
    float root = sqrt(disc);
    float sure = B >= 0.0 ? -(B + root) : root - B;
    float other = abs(sure) > 1e-12 ? C / sure : 0.0;
    first = min(sure, other);
    second = max(sure, other);
    return true;
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

// The same, with how it fills in above its floor given in metres: a storm's base is sharp however
// deep the storm, and a softness in shares of its depth cannot say so.
float FishCloudProfile(float aboveFloorM, float height01, float riseM, float topSoftness)
{
    float rise = saturate(aboveFloorM / max(1.0, riseM));
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
/// The formation term, in cover units. Read once per view sample and handed down: the light march's
/// samples are within a few kilometres of it and the formations are tens of kilometres across.
float FishCloudMesoAt(float2 xz)
{
    return FishCloudMesoscale(xz - _FishCloudMeso.xy) * _FishCloudMesoParams.x - _FishCloudMeso.z;
}

/// Whether any cloud can be at this height at all, from the bands' figures alone — no texture, no
/// hash. Most of a long ray is the empty air between a deck and the cirrus, and every sample of it
/// used to pay for the formation lattice, the tower lattice and the weather map before finding out
/// there was no band there to use them.
bool FishCloudPossibleAt(float altitude)
{
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        float4 f = _FishCloudLayerF[i];
        float4 d = _FishCloudLayerD[i];
        // A regime with no cloud anywhere in view, or none of any opacity, cannot be here.
        if (a.w <= 0.0 || a.z <= 0.002)
        {
            continue;
        }
        bool column = f.z > 0.5;
        // With ground under the sky a band can stand higher than its own top: a kilometre of
        // headroom covers the most any mountain lifts it. A column's top is already the highest a
        // tower climbs anywhere in view, and its shell the highest storm and lowest storm base.
        float top = d.z + (_FishCloudTerrainRect.w > 0.5 ? 1000.0 : 0.0);
        // Only the rain haze under a wet base reaches the ground now; the fog has its own shell.
        float bottom = column && _FishCloudSub.y >= 0.01 ? 0.0 : d.y;
        if (altitude > bottom && altitude < top)
        {
            return true;
        }
    }
    return false;
}

/// The thinnest cloud that can stand at this height, in metres, for how far a step of the march may
/// climb; 0 where no cloud can be at all (the same test as FishCloudPossibleAt).
///
/// The column's figure is its thinnest deck anywhere in view (`_FishCloudLayerF.w`), which is the
/// cloud itself. A layer's is a quarter of its band. Its `w` is the band's whole depth, and the cloud
/// in it is a sheet, wherever the band's profile times the noise clears the cover's cut: a hundred
/// metres of a two-kilometre band. A stride held to a share of the band's depth climbed three hundred
/// metres at a time through the middle band and landed in such a sheet on a third of the rays, which
/// drew it in rings with the jitter off and in grain with it on (FishCloudMarch, the step).
float FishCloudFinestAt(float altitude)
{
    float finest = 1e9;
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        float4 f = _FishCloudLayerF[i];
        float4 d = _FishCloudLayerD[i];
        if (a.w <= 0.0 || a.z <= 0.002)
        {
            continue;
        }
        bool column = f.z > 0.5;
        float top = d.z + (_FishCloudTerrainRect.w > 0.5 ? 1000.0 : 0.0);
        float bottom = column && _FishCloudSub.y >= 0.01 ? 0.0 : d.y;
        if (altitude > bottom && altitude < top)
        {
            float depth = max(50.0, f.w > 0.0 ? f.w : a.y - a.x);
            finest = min(finest, column ? depth : 0.25 * depth);
        }
    }
    return finest < 1e8 ? finest : 0.0;
}

/// How far the air at a height has been pushed up by the ground beneath it, in metres.
///
/// Air does not pass through a mountain, it goes over it, and every layer of the sky above rides up
/// with it — most just above the slope, less and less with height, until a few kilometres up the
/// mountain is not felt at all. The cloud field used to be read at plain height above sea level,
/// so a deck with its base at 800 m drifted straight into a 1600 m peak and the depth buffer hid
/// it inside the rock: the mountain swallowed every cloud that reached it. Read at a height with
/// this taken off, the deck drapes the mountain instead — a few hundred metres over the slopes, a
/// cap over the summit — and comes down again in the lee.
///
/// Six tenths of the ground's height, not all of it, so the highest ground still stands through a
/// low deck rather than every layer clearing every peak by the same margin. The fall-off with
/// height is the scale a mountain disturbs the air over; the mapping stays one-to-one for any
/// slope gentler than that, so no height is read twice.
float FishCloudLift(float altitude, float ground)
{
    return ground * 0.6 * exp(-max(0.0, altitude - ground) / 2500.0);
}


// The most cover a hill adds where it lifts moist air past its condensation level (FishCloudDensityAt):
// most of a full sky's, in a skin on the windward face.
#define FISH_CLOUD_CAP_COVER 0.7

// How far above the rock cloud is thinned where its air cannot climb what it is against (m). The visible
// edge settles at most of this, where the thinned profile falls under the cut.
#define FISH_CLOUD_ROCK_GAP 250.0

/// How much of its cloud the air at this point may hold for the rock beside it, 0..1.
///
/// Where the air goes round a mountain (FishFlowAround) its streamlines run round the rock and the
/// cloud with them — but a streamline grazing a face still carries cloud right up against it, and
/// the grid is tens of metres to the cell. So where the mountain beside a point stands higher than its
/// air can climb (the massif, against the point's height plus `climb`), the cloud is thinned over the
/// last FISH_CLOUD_ROCK_GAP above the rock, before the noise is cut (FishCloudDensityAt), and parts
/// round the face along the noise with its usual billowed edge. Where the air CAN climb, it goes over
/// and its cloud is left on the ground: that is a hill's cap cloud and its hill fog (the orographic
/// cover), not a cloud running into rock. Read at the point itself, not at the leaned field position.
float FishCloudClearance(float2 xz, float trueAltitude, float climb)
{
    if (_FishCloudRockTop <= 0.0 || _FishCloudFlowRect.w < 0.5 || trueAltitude >= _FishCloudRockTop + FISH_CLOUD_ROCK_GAP)
    {
        return 1.0;
    }
    float2 uv = (xz - _FishCloudFlowRect.xy) / max(1.0, _FishCloudFlowRect.z);
    if (any(uv <= 0.0) || any(uv >= 1.0))
    {
        return 1.0;
    }
    float2 rock = SAMPLE_TEXTURE2D_LOD(_FishCloudRock, sampler_FishCloudRock, uv, 0).rg;
    float blocked = smoothstep(-100.0, 50.0, rock.y - (trueAltitude + max(0.0, climb)));
    float clear = smoothstep(0.0, FISH_CLOUD_ROCK_GAP, trueAltitude - rock.x);
    return lerp(1.0, clear, blocked);
}

/// How far along a ray the next place is where any cloud can be, from `travelled`; a very large
/// number when there is none.
///
/// Empty air used to be walked: a stride, a test, another stride, each capped at half the thinnest
/// cloud in the sky — 245 m with a deck up — so that none could be stepped over. A level ray has
/// tens of kilometres of nothing to cross before it climbs into the cirrus, and on the middle
/// quality tier the whole march is 192 iterations: it ran out 47 km from the camera having met
/// nothing, and whatever lay past that was never drawn. Cirrus ten degrees above the horizon is 45
/// km away. So every high layer ended along a cone, with no fade at all, which is what clouds
/// "trimmed round a sphere at the horizon" was. The bands are shells about the planet's centre,
/// so where a ray next meets one is a sphere intersection — one square root, exact, curvature
/// included — and the empty air between costs one iteration however much of it there is.
float FishCloudNextPossible(float3 origin, float3 direction, float travelled, float altitude)
{
    float headroom = _FishCloudTerrainRect.w > 0.5 ? 1000.0 : 0.0;
    float next = 1e9;
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        float4 f = _FishCloudLayerF[i];
        float4 d = _FishCloudLayerD[i];
        if (a.w <= 0.0 || a.z <= 0.002)
        {
            continue;
        }
        bool column = f.z > 0.5;
        float top = d.z + headroom;
        float bottom = column && _FishCloudSub.y >= 0.01 ? 0.0 : d.y;
        // Below the band: where the ray climbs through its floor — the far side of that shell,
        // since the ray starts inside it. Above it: where it comes down through its ceiling, the
        // near side, if it ever does — a ray that is already curving away never will.
        bool under = altitude <= bottom;
        float first, second;
        if (!FishCloudShell(origin, direction, under ? bottom : top, first, second))
        {
            continue;
        }
        float hit = under ? second : first;
        if (hit > travelled)
        {
            next = min(next, hit);
        }
    }
    return next;
}

// How air meets high ground: x the wind speed over the air's stability (U/N, metres) — the height of
// ground the air has the energy to climb; y the share of that the cloud's steering round the terrain and
// its clearance from the rock honour, m (SkySystem.CloudSteeringClimbShare × x: 0 as shipped, so every peak
// taller than a cloud parts it — the physical U/N, about a kilometre on an ordinary day, cleared every
// peak in a scene whose mountains stand a kilometre high, and nothing went round anything); z the ground
// the air arrives from (m): the scene's lowest ground (CloudFlowField.LowestGround) — never a window's
// round the viewer, whose lowest moved with the window and lifted the whole sky up and down as it did.
float4 _FishCloudFlow;

/// What the sky is doing over one spot of ground. Read once per view sample and handed down to the
/// light march, whose samples are a few kilometres off at most.
struct FishCloudField
{
    float meso;         // the formation here, as a change in cover
    float tower;        // 0..1: how strongly a tower stands here
    float orographic;   // what the slope does to the cover here
    float ground;       // how high the ground is as the air feels it: the mountain, smoothed (m)
    float surface;      // how high the ground itself is, for what lies on it (m)
    float over;         // 1 the air goes over this ground, 0 it goes round it
    // The air over this place, from the air map.
    float lowCover;     // low cloud the air asks for here, before the formations
    float cloudBase;    // where rising air here reaches its dew point (m)
    float cloudTop;     // where an ordinary cloud here stops (m): a deck's top, or a heap's
    float towerTop;     // how high a tower here can climb (m); the ordinary top where none can
    float vigour;       // how vigorous the convection is: 0 a flat deck, 1 boiling towers
    float midCover;     // middle cloud the air asks for here
    float highCover;    // high ice the air asks for here
    float freezing;     // the freezing level here (m): above it a storm's drops begin to turn to ice
    // The storms over this place, from the weather map: their own cloud, base, towers and anvils.
    // Read here, once per view sample, and not in the density — the light march takes a handful of
    // density samples for every view sample, a few kilometres off at most, and a storm is several
    // kilometres across; the shadow walk, whose samples are many kilometres apart, reads it afresh.
    FishStorm storm;
};

// Over how many metres a storm's cloud goes from nothing to solid above its base (twin of
// WeatherMap.BaseRiseMetres). A heap's base is where each of its bubbles happened to reach its dew
// point, and its cloud fills in over a tenth of its depth: across a storm ten kilometres deep that
// was a kilometre of fuzz where its base should be. A storm feeds on one broad updraught of one air,
// which condenses at one height across the whole storm: the flat, hard underside a storm is known by.
#define FISH_STORM_BASE_RISE 60.0

/// The air map at a place: the current map, faded in over the previous one. Outside it, its edge —
/// the far sky beyond sixty kilometres is haze by then, and the systems it follows are far wider.
void FishCloudAirAt(float2 xz, out float4 airA, out float4 airB)
{
    if (_FishCloudAirRect.w < 0.5)
    {
        airA = float4(0.0, 1000.0, 1500.0, 1500.0);
        airB = float4(0.0, 0.0, 0.0, 2000.0);
        return;
    }
    float2 uv = saturate((xz - _FishCloudAirRect.xy) / max(1.0, _FishCloudAirRect.z));
    float2 before = saturate((xz - _FishCloudAirPrevRect.xy) / max(1.0, _FishCloudAirPrevRect.z));
    float fade = saturate(_FishCloudAirPrevRect.w);
    airA = lerp(SAMPLE_TEXTURE2D_LOD(_FishCloudAirPrevA, sampler_FishCloudAirA, before, 0),
        SAMPLE_TEXTURE2D_LOD(_FishCloudAirA, sampler_FishCloudAirA, uv, 0), fade);
    airB = lerp(SAMPLE_TEXTURE2D_LOD(_FishCloudAirPrevB, sampler_FishCloudAirA, before, 0),
        SAMPLE_TEXTURE2D_LOD(_FishCloudAirB, sampler_FishCloudAirA, uv, 0), fade);
}

/// The terrain decides two things, and which of them happens is a matter of energy.
///
/// OVER. Wind driven against a slope is forced up it, cools and condenses: cloud gathers on the
/// windward side, caps the summit, rolls across the ridge and thins in the lee where the air sinks
/// and warms again. The whole layer rides up with the air (FishCloudLift).
///
/// ROUND. If the air is very stable, or the barrier is very high, it has not the energy to climb
/// and does not try: the flow splits, the cloud is turned aside through the valleys and round the
/// flanks, and the summit stands clear above a sea of it.
///
/// Which, is the Froude number — the wind's speed against the air's stability times the height
/// to be climbed, U / (N h). Above about one the air goes over; below it, round. The wind and the
/// stability are the sky's and arrive as one figure (U/N, in metres: the height this air can
/// climb); the height is the ground's, read here. So it is decided place by place — the same wind
/// crosses a low ridge and is split by the peak behind it — and changes with the weather: a calm
/// stable morning flows round a hill that an unsettled windy afternoon pours over.
///
/// The cloud is not cut away where the rock is. The depth buffer already stops the march at the
/// mountain, so cloud that overlaps a slope is hidden by it, and carving it out would cost a lookup
/// per sample to remove what cannot be seen.
FishCloudField FishCloudFieldAt(float2 xz)
{
    FishCloudField field;
    field.meso = FishCloudMesoAt(xz);
    field.tower = _FishCloudColumn.x > 0.001 ? FishCloudTower(xz - _FishCloudTowerDrift.xy) : 0.0;
    field.orographic = 0.0;
    field.ground = 0.0;
    field.surface = 0.0;
    field.over = 1.0;
    float4 airA, airB;
    FishCloudAirAt(xz, airA, airB);
    field.lowCover = airA.x;
    field.cloudBase = airA.y;
    field.cloudTop = max(airA.y, airA.z);
    field.towerTop = max(field.cloudTop, airA.w);
    field.vigour = saturate(airB.x);
    field.midCover = airB.y;
    field.highCover = airB.z;
    field.freezing = airB.w;
    field.storm = FishStormAt(xz);
    if (_FishCloudTerrainRect.w > 0.5)
    {
        float2 uv = (xz - _FishCloudTerrainRect.xy) / max(1.0, _FishCloudTerrainRect.z);
        float2 toEdge = min(uv, 1.0 - uv);
        float inside = saturate(min(toEdge.x, toEdge.y) / 0.08);
        if (inside > 0.0)
        {
            float4 terrain = SAMPLE_TEXTURE2D_LOD(_FishCloudTerrain, sampler_FishCloudTerrain, saturate(uv), 0);
            field.ground = terrain.r * inside;
            field.surface = terrain.a * inside;
            float2 wind = dot(_FishCloudWindDir.xy, _FishCloudWindDir.xy) > 1e-6 ? _FishCloudWindDir.xy : float2(0.0, 1.0);

            // Over, or round: the height this air can climb against the height that is here — how
            // far the ground rises above the land the air came over, not above the sea. Measured
            // from sea level, a plateau was a wall its own height high and the air over the whole
            // of it was "blocked", which switched the lift off everywhere on it.
            float relief = field.ground - _FishCloudFlow.z * inside;
            float froude = max(1.0, _FishCloudFlow.x) / max(50.0, relief);
            // Over a wide band of heights, on purpose: from four tenths to nearly twice the height the
            // air can climb, the lift fades across more than a kilometre of any real slope. The going
            // round is not this figure's: it is the flow field's, read at the point's own height
            // (FishFlowAround).
            field.over = smoothstep(0.4, 1.8, froude);

            // Over: rising along the wind is the windward slope, where the cloud gathers; falling
            // along it is the lee, where it clears. A blocked flow still banks a little cloud
            // against the foot of what blocks it.
            float upslope = dot(terrain.gb, wind);
            field.orographic = clamp(upslope * 0.9, -0.12, 0.3) * inside * lerp(0.4, 1.0, field.over);
        }
    }
    return field;
}

// ── What the cloud is made of ──────────────────────────────────────────
// Twins of CloudClimate's constants: change one, change the other.

// The most extinction a convective cloud's drops reach, 1/m: about a gram a cubic metre in drops of
// ten or fifteen microns (3·LWC/(2ρ·r_e)), the wettest a cumulonimbus core holds as liquid.
#define FISH_CLOUD_BETA_CEILING 0.15
// A storm's undiluted core keeps three quarters of the water its ascent condenses, where an ordinary
// heap keeps a third (CloudClimate.StormAdiabaticFraction / AdiabaticFraction): (0.75/0.35)^⅔ the
// extinction, with the same drops.
#define FISH_STORM_WATER 1.66
// A thunderstorm's updraught, m/s (CloudClimate.StormUpdraught): what a tower above the ordinary
// heaps' tops rose on, for how far it leans.
#define FISH_STORM_UPDRAUGHT 20.0
// The mean cosine of scattering by ice crystals (CloudClimate.IceAsymmetry).
#define FISH_ICE_ASYMMETRY 0.76

// ── The edge of a cloud ────────────────────────────────────────────────
//
// Why the clouds were soft blobs, measured on a CPU port of this file against the baked volumes:
//  - The edge was a softness in the noise's own units, 0.06 + 0.13 × humidity. Through the noise's
//    gradient where the cut meets it (1.67e-4 a metre on the 9.6 km tile) that was a ramp 830 m wide
//    at ordinary humidity, and a broken sky's clouds sat 100 % in it, 96 % at under half their water:
//    all fringe, no core. Seen from below, the edge from 10 % to 90 % opaque was 120–140 m and the
//    outline's fractal dimension 1.05 — a smooth blob. Real cloud outlines are fractal, D ≈ 1.35
//    (Lovejoy 1982), and a cumulus's mixing zone is about a hundred metres (Katzwinkel et al. 2014).
//  - The detail noise barely varies: its three channels combined have a spread of 0.074 about 0.48.
//    Subtracted as `wisp × k × (1 − density)` — which is the remap (d − e)/(1 − e) with e = kw/(1 + kw),
//    so the core was always kept — it took 0.118 ± 0.025 on Balanced: the same everywhere, a uniform
//    thinning that moved the edge ±20 m against an 830 m ramp. Nothing was frayed.
//  - "Wispy underneath, billowy on top" was the wrong way round (CloudClimate.EddyPolarity).
//  - The detail faded out whole between 2.5 and 11.5 km, nearer than most of the clouds in view, and
//    took the erosion's thinning with it, so a far cloud was 20 % thicker than the same cloud near to.
// Now: everything inside the cut holds the cloud's whole water, however small the cloud; outside it
// lies a mixing shell a hundred metres wide in which the mixtures keep water only as far out as the
// dry air round them lets them (the critical mixing fraction); the eddies carry that edge in and out
// by the detail noise, normalised to its own spread; their average is kept at every distance, so the
// cloud is the same size however far off, however fine the tier; and how much cloud stands over a
// point — its lighting — follows the cloud's body, not the eddy it sits in, so the light is continuous
// across the edge. The cut rose 0.020 (CPU port against the baked volumes and the old renders'
// cover: the port reproduces the old model's cover at the probe's own camera and effective cover).

// The mixing shell's width, as a share of the detail tile (CloudClimate.MixingShellShare).
#define FISH_CLOUD_SHELL_SHARE 0.5
// How far the eddies carry a cloud's edge in and out, in shells (CloudClimate.EddyReach).
#define FISH_CLOUD_EDDY_REACH 0.5
// How far inside the cut a cloud's body fills out, in the noise's units, for how much cloud stands
// over and under a point (its lighting): 0.14, the old edge softness — wrong as an edge, eight hundred
// metres of fog, but the right scale for how a cloud's depth grows from its rim to its core.
#define FISH_CLOUD_BODY_FILL 0.14
// The most the critical fraction may be (CloudClimate.MaxCritical).
#define FISH_CLOUD_MAX_CRITICAL 0.98
// How deep the mid-scale carve cuts into the noise at its strongest, in the noise's units: the
// softness it was tuned against (0.14, the asset's calibrated EdgeSoftness), so the cauliflower keeps
// the depth it had when it carved a soft ramp. It raises the cut, where it used to remap the ramp:
// against a hundred-metre shell a remap could only have carved a hundred metres.
#define FISH_CLOUD_CARVE_DEPTH 0.14
// The mean of the carve's hollows, 1 − billows, measured over the volume (0.524): what it is eased to
// with distance, so easing it moves no cloud's edge on average.
#define FISH_CLOUD_CARVE_MEAN 0.524
// The detail volume's channels (inverted Worley at 4, 8 and 16 cells a tile): their weights, means and
// spreads, measured off the baked asset at mip 0 as the trilinear read returns them. The channels are
// independent (correlations under 0.01); their weighted sum's measured spread is 0.924 of the
// independent estimate, which the normalisation keeps.
static const float3 FishCloudDetailWeights = float3(0.625, 0.25, 0.125);
static const float3 FishCloudDetailMean = float3(0.4779, 0.4760, 0.4705);
static const float3 FishCloudDetailSpread = float3(0.1102, 0.0986, 0.0775);
#define FISH_CLOUD_DETAIL_SPREAD_FIX 0.924

// How wide the current view sample's pixel cone is, m: what decides which octaves of the eddies can be
// drawn. Set by the march before each sample; the light march and the cookie leave it where it is,
// and ask for no pattern.
static float FishCloudDetailCone = 0.0;
// Whether a density read is one the camera sees, and so worth the terrain: the flow round the mountains
// and the clearance from their rock (FishFlowAround, FishCloudClearance) — two texture reads. The light
// march and the shadow walk read the density several times for every view sample, a few kilometres off
// and for a depth, not a shape, and leave them out: off while they run.
static bool FishCloudReadsTerrain = true;
// How far the last density read stood outside the outermost place any cloud's water can reach, m —
// its shell, the eddies' reach and the edge's ramp (FishCloudDensityAt); 0 inside it, very large where no
// band could hold cloud near. The march's empty air is walked no faster than this lets it (FishCloudMarch).
static float FishCloudEdgeAhead = 1e9;
// And how far apart its samples are along the ray, m (the step, times _FishCloudStepTau.z): no eddy
// finer than the samples can resolve, and no edge sharper than them, is drawn. 0 outside the march.
static float FishCloudDetailAlong = 0.0;

/// How far out of the cut the shell's mixtures keep water, in shells: 1 − χ*, but never under the
/// pixel's cone or a fiftieth. Twin of CloudClimate.EdgeRampWidth.
float FishCloudEdgeRampWidth(float critical, float coneShells)
{
    return max(1.0 - clamp(critical, 0.0, FISH_CLOUD_MAX_CRITICAL), max(coneShells, 0.02));
}

/// The water a point keeps, as a share of the cloud's, `inside` shells inside the cut: all of it
/// inside, falling to none a ramp's width outside. Twin of CloudClimate.EdgeWater.
float FishCloudEdgeWater(float inside, float ramp)
{
    return saturate(1.0 + inside / max(1e-3, ramp));
}

/// saturate's antiderivative, for the average below.
float FishCloudEdgeRamp(float y)
{
    if (y <= 0.0)
    {
        return 0.0;
    }
    return y <= 1.0 ? 0.5 * y * y : y - 0.5;
}

/// What the edge keeps on average when the eddies carry it evenly over ± reach: the edge seen from too
/// far off to see the eddies. Twin of CloudClimate.ExpectedEdgeWater.
float FishCloudExpectedEdgeWater(float inside, float reach, float ramp)
{
    float r = max(1e-3, ramp);
    if (reach < 1e-4)
    {
        return FishCloudEdgeWater(inside, r);
    }
    return (FishCloudEdgeRamp(1.0 + (inside + reach) / r) - FishCloudEdgeRamp(1.0 + (inside - reach) / r)) * r / (2.0 * reach);
}

/// How much of the eddies' and the cauliflower's PATTERN a point keeps by how far up its cloud it is
/// (0 at the base, 1 at the top): a cumulus's base is flat and smooth — it is where rising air reaches
/// its condensation level, one height across the cloud — and the turrets and the ragged edges are its
/// upper parts, where the bubbles overshoot and mix. Only the pattern: what it averages to is kept
/// everywhere (FishCloudExpectedEdgeWater, the carve's mean), so a smoothed base is not a smaller one.
float FishCloudBaseKeep(float height01)
{
    if (_FishCloudBase.y <= 0.0)
    {
        return 1.0;
    }
    return lerp(1.0 - saturate(_FishCloudBase.x), 1.0, smoothstep(0.0, _FishCloudBase.y, saturate(height01)));
}

/// Which way the eddies carve, −1 (wisps: the outside air engulfed, the cloud left as filaments) low in
/// the cloud to +1 (billows: bubbles pushed out) from a third of the way up. Twin of CloudClimate.EddyPolarity.
float FishCloudEddyPolarity(float height01)
{
    return lerp(-1.0, 1.0, smoothstep(0.1, 0.4, height01));
}

/// How much of each octave of the detail the screen resolves: in full while the cone is under half an
/// octave's cell, gone once it is the cell. Twin of CloudClimate.DetailResolved.
float3 FishCloudDetailResolved(float cone, float detailTile)
{
    float3 cells = max(1.0, detailTile) * float3(0.25, 0.125, 0.0625);
    return 1.0 - smoothstep(0.5, 1.0, max(0.0, cone) / cells);
}

/// The axis the noise is drawn out along and everything leans along: the steady prevailing wind.
float2 FishCloudAxis()
{
    return dot(_FishCloudWindDir.xy, _FishCloudWindDir.xy) > 1e-6 ? _FishCloudWindDir.xy : float2(0.0, 1.0);
}

/// How far downwind of its foot the air in a convective cloud at this height has been carried, m.
///
/// A parcel rising at w through a wind that grows S metres a second every metre up drifts
/// S(z − base)²/(2w) ahead of where it left the base: this is why a heap leans and a tower's top
/// stands kilometres downwind of its foot. Heaps rise at their own few metres a second; anything
/// above the ordinary heaps' tops is a tower, which only a storm's updraught carried there, so that
/// part of its climb is taken at a storm's speed — continuous, and never turning back. It stops
/// growing at the tropopause, where the thermal wind turns. CloudClimate.ShearedLean is the twin.
///
/// Applied as a shear of the whole column — the field it is read from and the noise alike — so a
/// tower leans as one cloud and its anvil stays on top of it. A shear keeps the height, so the lookup
/// can never fold however steep the lean (the rings of 2026-09-20 were a lookup that did); and it is
/// metres added, not a multiplier on the drifted lookup, so the 42-tile wrap is untouched.
float FishCloudLean(float altitude)
{
    float shear = _FishCloudShear.x;
    if (shear <= 0.0)
    {
        return 0.0;
    }
    float baseM = _FishCloudShear.z;
    float trop = max(baseM + 100.0, _FishCloudIce.w);
    float top = clamp(_FishCloudShear.w, baseM, trop);
    float z = min(altitude, trop);
    float low = clamp(z - baseM, 0.0, top - baseM);
    float high = max(0.0, z - top);
    float heap = max(0.5, _FishCloudShear.y);
    float tower = max(heap, FISH_STORM_UPDRAUGHT);
    return shear * (low * low / (2.0 * heap) + (high * high * 0.5 + high * (top - baseM)) / tower);
}

/// How far behind its head a cirrus fall streak has trailed at a depth below the band's top, m:
/// ice falling at its terminal speed through air that slows with depth is left upwind of the head it
/// fell from by S·d²/(2v) (CloudClimate.FallStreakLength). The hooks of cirrus uncinus.
float FishCloudStreak(float depthBelowTop)
{
    float d = max(0.0, depthBelowTop);
    return max(0.0, _FishCloudShear.x) * d * d / (2.0 * max(0.02, _FishCloudIce.z));
}

/// A convective cloud's extinction at a height above its base, 1/m: the adiabatic β = C·h^⅔, to the
/// most its drops can hold.
float FishCloudColumnBeta(float c, float aboveBase)
{
    return min(FISH_CLOUD_BETA_CEILING, c * pow(max(20.0, aboveBase), 2.0 / 3.0));
}

/// The optical depth straight up through that cloud from its base to a height (twin of
/// CloudClimate.ColumnOpticalDepth): the integral of C·h^⅔, which is 0.6·C·h^(5/3), and straight
/// on past the height where it meets the ceiling.
float FishCloudColumnDepth(float c, float aboveBase)
{
    float h = max(0.0, aboveBase);
    if (c <= 0.0)
    {
        return 0.0;
    }
    float capHeight = pow(FISH_CLOUD_BETA_CEILING / c, 1.5);
    float below = min(h, capHeight);
    return 0.6 * c * pow(below, 5.0 / 3.0) + FISH_CLOUD_BETA_CEILING * max(0.0, h - capHeight);
}

/// How much of a cloud's water is ice at a height, 0..1: none at the freezing level, all of it at the
/// level where the last supercooled drop freezes (about −38 °C: Rosenfeld and Woodley 2000).
float FishCloudGlaciated(float altitude, float freezing)
{
    float iceLevel = _FishCloudIce.x;
    if (iceLevel <= freezing + 1.0)
    {
        return 0.0;
    }
    return smoothstep(freezing, iceLevel, altitude);
}

/// What a density sample found, besides how dense it is: what the lighting needs to know about the
/// cloud around it.
struct FishCloudPoint
{
    float high01;       // where in its cloud: 0 at the base, 1 at the top; −1 under a base
    int layer;          // which band
    float cover;        // that band's cover here
    // Straight down to the cloud's base and straight up to its top, as optical depth, from the
    // profile it was built with: what the light from the sky above and the ground below has to get
    // through, without a march to find out.
    float tauBelow;
    float tauAbove;
    // Under a base: how much of a cloud stands overhead (0..1), how thick it is (optical depth), and
    // how much of the open sky round its edge this air sees, sideways under it (0..0.5).
    float under;
    float tauOverhead;
    float sideSky;
    // In a cloud: how much of what its base looks down on is sunlit ground out past the edge of its
    // own shadow, 0..1 — sin² of the angle below the horizontal at which that edge is seen.
    float groundOpen;
    // Under a base: how far up the base is, m (0 elsewhere), so the march can walk the haze to it.
    float baseAbove;
};

/// `carve` asks for the mid-scale cauliflower: on for what the camera sees and for the first steps
/// toward the sun, where it shapes the self-shadow; off where only a rough depth is wanted.
float FishCloudDensityAt(float3 position, float detailAmount, float footprint, FishCloudField field, bool carve, out FishCloudPoint spot)
{
    FishCloudEdgeAhead = 1e9;
    float meso = field.meso;
    spot.high01 = 0.0;
    spot.layer = 0;
    spot.cover = 0.0;
    spot.tauBelow = 0.0;
    spot.tauAbove = 0.0;
    spot.under = 0.0;
    spot.tauOverhead = 0.0;
    spot.sideSky = 0.0;
    spot.groundOpen = 0.0;
    spot.baseAbove = 0.0;
    // Two heights. The true one, above sea level, is where this point is: the fog lies on the
    // ground by it. The other is where the air here *came from* before the ground pushed it up,
    // and it is the one every band is read at — which is what carries a deck over a mountain.
    float trueAltitude = FishCloudAltitude(position);
    // Only as far as the air here goes over: a blocked flow is not lifted, it is turned aside, and
    // its cloud stays at its own level and banks against the slopes.
    float altitude = trueAltitude - FishCloudLift(trueAltitude, field.ground) * field.over;
    if (altitude <= _FishCloudLayer.x || altitude >= _FishCloudLayer.y)
    {
        return 0.0;
    }
    // In the rock, or against it: no cloud (FishCloudClearance).
    // Against the cloud's own height plus the share of its climb the steering honours (_FishCloudFlow.y,
    // SkySystem.CloudSteeringClimbShare): every peak taller than the cloud parts it.
    float clearance = FishCloudReadsTerrain ? FishCloudClearance(position.xz, trueAltitude, _FishCloudFlow.y) : 1.0;
    if (clearance <= 0.0)
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
    // Where the air here came from, on the ground plane: carried round high ground it could not
    // climb, along the streamlines of the flow past it (FishFlowAround), straight through otherwise.
    float2 source = position.xz + (FishCloudReadsTerrain ? FishFlowAround(position.xz, trueAltitude, _FishCloudFlow.y, wind) : float2(0.0, 0.0));
    float2 frame = float2(dot(source, wind), dot(source, across));
    // The storms the server sends, as the weather map lays out their anatomy: read once for this
    // place, in the field.
    FishStorm stormField = field.storm;
    // `meso` is where this column stands in the formations: in the bank, or in the gap, as a
    // change in cover with the camera's own share taken back out — the bands' figures were
    // measured at the camera and already carry it. Read through the same drift the weather field
    // is read through, so the bank overhead is the bank the rain falls out of.

    float best = 0.0;
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        float4 f = _FishCloudLayerF[i];
        float4 d = _FishCloudLayerD[i];
        // Above anything this band holds anywhere in view, its storms' towers and anvils included.
        if (altitude >= d.z)
        {
            continue;
        }
        bool column = f.z > 0.5;
        int regime = (int)(f.x + 0.5);
        float4 c = _FishCloudLayerC[i];
        // Packed: the units are the rain, and a band that grows storms carries a 2 on top, so that
        // rain alone can never trip the storm test and a storm band's rain is still legible.
        bool growsStorms = c.w >= 2.0;
        // How much of the column here is a storm's own cloud: its cumulonimbus, and the wall cloud
        // or the shelf that hangs under and ahead of it.
        float stormCloud = column && growsStorms ? stormField.cover : 0.0;
        // Where this regime's cloud starts here. A column's base is where rising air over THIS place
        // reaches its dew point — the air map's, so a damp valley's deck hangs lower than the dry
        // plateau's beside it; a layer's is the band's own.
        float floorHere = column ? field.cloudBase : a.x;
        float topHere = a.y;
        if (stormCloud > 0.0)
        {
            // A storm has a base of its own: the condensation level of the moist air it feeds on,
            // lower than the open air's, one flat height across the whole storm — and lower again
            // under a supercell's wall cloud and a squall line's shelf. Set down by most of the
            // base's rise, so that where its cloud turns solid is the base the anatomy gives, and a
            // tornado hung from the wall cloud's base meets cloud.
            floorHere = lerp(floorHere, stormField.baseM - 0.8 * FISH_STORM_BASE_RISE, stormCloud);
            topHere = max(topHere, stormField.topM);
        }
        // The anvil is the high ice's: a slab of it along the tropopause, over and downwind of the
        // storm that feeds it, standing wherever it is here and not where the band's own cirrus is.
        bool inAnvil = false;
        if (regime == 2 && stormField.anvil > 0.002 && stormField.anvilDepthM > 1.0)
        {
            float anvilFloor = stormField.anvilTopM - stormField.anvilDepthM;
            if (altitude > anvilFloor && altitude < stormField.anvilTopM)
            {
                inAnvil = true;
                floorHere = anvilFloor;
                topHere = stormField.anvilTopM;
            }
        }
        // Below a column's base there is still the column: the scud and rain haze that hang from
        // it. Anything else stops at its own floor. (The fog is not the column's: FishFogDensity.)
        bool below = altitude <= floorHere;
        if (altitude >= topHere || (below && (!column || _FishCloudSub.y < 0.01)))
        {
            continue;
        }
        float4 b = _FishCloudLayerB[i];
        float4 e = _FishCloudLayerE[i];

        // The cover here: what the air over this place asks for (the air map), where the formations
        // put it in banks and lanes, and what the ground does to the air — only within a couple of
        // kilometres above the terrain, which is as far up as a hill's lift reaches.
        float airCover = regime == 0 ? field.lowCover : (regime == 1 ? field.midCover : field.highCover);
        float overGround = trueAltitude - field.surface;
        float lifted01 = regime == 0 ? field.orographic * saturate(1.0 - (overGround - 300.0) / 1800.0) : 0.0;
        // Cap cloud and hill fog. Air that climbs a hill cools as it rises, and once the ground lifts
        // it past its condensation level (the air map's base here) it turns to cloud lying on the
        // slope — whatever cover the day's convection is making, since it is the hill doing the
        // lifting. Thickest in a skin a few hundred metres deep on the windward face, where the air
        // is being pushed up; gone in the lee, where it sinks and warms; and only where the air goes
        // over (field.over) — air that goes round is not lifted. So it shows on a humid day (a low
        // base) on hills that reach it, and never on a dry one, when the base stands above them.
        if (column)
        {
            float reaches = smoothstep(field.cloudBase - 50.0, field.cloudBase + 150.0, field.surface);
            float skin = saturate(1.0 - overGround / 350.0);
            float windward = saturate(0.5 + field.orographic / 0.2);
            lifted01 += FISH_CLOUD_CAP_COVER * reaches * skin * windward * lerp(0.3, 1.0, field.over);
        }
        float coverage = saturate(airCover + meso * (regime == 0 ? 1.0 : 0.5) + lifted01);
        // A sky the air asks nothing of has nothing in it: the noise's own top would otherwise show
        // a few wisps under any cover at all.
        coverage *= smoothstep(0.0, 0.12, coverage);

        // How high this column's cloud gets here. An ordinary cloud stops where the air says —
        // a deck's top in settled air, a heap's in unsettled — and where the tower lattice stands a
        // tower, it climbs as far as the air lets a parcel rise on its own: to the tropopause where
        // it breaks through, no further than an ordinary cloud where it cannot. How readily it does
        // is the convection's vigour. Where a storm stands its cloud reaches as high as its anatomy
        // says: its tower, domed over the updraught and overshooting the tropopause, and no higher
        // than its wedge over a squall line's shelf.
        float reachM = inAnvil ? max(30.0, stormField.anvilDepthM) : max(1.0, a.y - a.x);
        if (column)
        {
            float towering = saturate(field.tower * _FishCloudColumn.x * lerp(0.3, 1.0, field.vigour));
            float ordinary = max(30.0, field.cloudTop - floorHere);
            float tall = max(ordinary, field.towerTop - floorHere);
            float stormReach = max(30.0, stormField.topM - floorHere);
            reachM = lerp(lerp(ordinary, tall, towering), stormReach, stormCloud);
            // A tower brings its own cloud: the air that goes all the way up is air that is condensing.
            coverage = saturate(coverage + towering * 0.5);
            // A storm makes its own cloud wherever it stands, whatever the day is doing.
            coverage = max(coverage, stormCloud);
        }
        if (inAnvil)
        {
            // Solid over the storm, broken toward its edge, where it is also thinning.
            coverage = max(coverage, stormField.anvil);
        }
        // Where the air is sinking — a hurricane's eye — it is warmed and dried on the way down, and
        // no cloud stands in it at any height, whatever the air round it makes: the clear sky over
        // the eye, with the eyewall standing round it.
        coverage *= 1.0 - stormField.clearing;
        // Tested here and not on the band's figure at the camera, so cloud rolling in from upwind is
        // drawn before it arrives. Cloud only: the hang under the base shares this loop.
        if (coverage <= 0.002 && !below)
        {
            continue;
        }

        // This band's own slice of the noise, blown along by its own share of the wind, in the
        // wind's frame. *Subtracted*: the value here is the value the air brought with it from
        // upwind. Wrapped on the CPU to this band's own period, in double, so a float holds it.
        // Drawn out along the wind BEFORE anything reads it, the lift field included: read before,
        // the lift's lookup jumped by 21 × stretch tiles at every wrap of the drift — a whole number
        // only when the stretch was, so a stretched band's tops re-rolled every few days.
        float2 along = frame - e.xy;
        // The wind with height. A heap or a tower is read through the lean its rising air has
        // taken, so its top stands downwind of its foot; cirrus ice is read through its fall
        // streaks, so the trails hang upwind of the heads they fell from. Metres along the wind, in
        // the frame, before the stretch: an offset that depends on height alone, never on the drift.
        float leanM = column ? FishCloudLean(altitude) : (regime == 2 ? -FishCloudStreak(topHere - altitude) : 0.0);
        along.x -= leanM;
        along.x /= max(1.0, b.w);

        // A cloud's base is flat because the air reaches its dew point at one height across a
        // region; its top is lumpy because each parcel runs out of buoyancy somewhere different. So
        // the floor is fixed and the ceiling is roughened by a broad noise on the ground plane — more
        // where the convection is vigorous.
        // A storm's top is one updraught's, not a heap's scatter of bubbles, and an anvil's is the
        // tropopause itself: both roughened far less, or not at all.
        float convection = d.x * (column ? lerp(0.35, 1.0, field.vigour) : 1.0) * (1.0 - 0.6 * stormCloud) * (inAnvil ? 0.0 : 1.0);
        float ceilingM = reachM;
        if (convection > 0.001)
        {
            // Twice the band's tile, in the wind's frame, so its period divides the drift's wrap.
            float3 lifted = float3(along / max(1.0, b.x * 2.0), 0.31 + i * 0.11);
            float lift = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, lifted, 0).r;
            // Spread over the field's own range (0.33 to 0.76), never clamped: clamping gave every
            // high column the same ceiling and the deck grew a flat table with a hard rim.
            float reach01 = (lift - 0.33) / 0.43;
            ceilingM = reachM * (column
                ? lerp(1.0, 0.6 + reach01 * 0.4, convection)
                : lerp(1.0, 0.3 + reach01 * 0.7, convection));
        }
        ceilingM = max(column || inAnvil ? 30.0 : 0.15 * (a.y - a.x), ceilingM);
        // Where in this cloud the point is: 0 at its base, 1 at the top it reached here.
        float hIn = (altitude - floorHere) / ceilingM;
        // How it fills in above its floor, in metres, and how soon it thins toward its top. A storm's
        // base goes solid within a few tens of metres, and its tower is solid nearly to its top, which
        // is what carries it up into its anvil — at the band's own softness a storm drew as a heap
        // half its height, with the anvil floating kilometres over it.
        float riseM = max(0.02, c.y) * ceilingM;
        float topSoftness = c.z;
        if (stormCloud > 0.0)
        {
            riseM = lerp(riseM, FISH_STORM_BASE_RISE, stormCloud);
            topSoftness = lerp(topSoftness, 0.1, stormCloud);
        }
        if (inAnvil)
        {
            // Flat on top, where the tropopause stops it like a lid; ragged underneath, where its
            // ice falls out of it.
            riseM = 0.45 * ceilingM;
            topSoftness = 0.08;
        }
        float profile = below ? 1.0 : FishCloudProfile(altitude - floorHere, hIn, riseM, topSoftness);
        // Thinned toward the rock as toward its base, before the cut: the cloud parts round a mountain
        // along the noise, not along a line.
        profile *= clearance;
        if (profile <= 0.0)
        {
            continue;
        }
        // The vertical scale is the cloud's own: on the column, its cells are as tall as they are
        // wide, as convective cells are. Height from this cloud's own base, so the noise rides with
        // the base as the air lifts or lowers it.
        float verticalTile = max(50.0, (a.y - a.x) * d.w);
        float3 uv = float3(along.x / max(1.0, b.x), (altitude - floorHere) / verticalTile, along.y / max(1.0, b.x)) + i * 0.37;
        // The shape volume wraps, so a band tiles; warping the lookup by a much coarser read of the
        // same volume breaks the lattice. The warp changes slowly — about a quarter of the lookup's
        // own motion — so it bends the tiling without printing its own structure on the sky. 42 so
        // the drift's wrap is a whole number of its tiles.
        float3 warpUV = float3(along.x, altitude * 0.25, along.y) / max(1.0, b.x * 42.0) + i * 0.19;
        float3 warp = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, warpUV, 0).rgb;
        uv += (warp - 0.5) * _FishCloudShapeParams.z;
        float4 shape = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, uv, 0);
        // Perlin billowed by the Worley channels: solid cores, cauliflower edges.
        float billow = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
        float floorValue = (1.0 - billow) * 0.45;
        float body = saturate((shape.r - floorValue) / max(0.05, 1.0 - floorValue));
        // The cut, from the noise's own measured quantiles (it runs 0.33 to 0.76 about 0.57), on
        // the noise times the profile so a cloud narrows toward its top and keeps its flat floor.
        float coverCurve = coverage * coverage;
        float threshold = lerp(_FishCloudCoverage.x, _FishCloudCoverage.z, coverage) - _FishCloudCoverage.w * coverCurve * coverCurve;
        // Where this point stands against the cloud's edge, in mixing shells — the zone, about a
        // hundred metres wide (half the detail's tile, FISH_CLOUD_SHELL_SHARE), in which a cloud mixes
        // with the air round it — positive inside. The noise's excess over the cut is turned into
        // metres by how fast the noise changes across the ground (_FishCloudCoverage.y, per tile).
        //
        // The cut is where the undiluted cloud ends; the shell lies OUTSIDE it, as the sinking,
        // evaporating shell round a real cumulus does (Heus and Jonker 2008). Everything inside the cut
        // holds the cloud's whole water, however small the cloud. (The first pass put the shell inside
        // the cut, as a ramp from nothing at the cut to pure cloud a shell in, and took from each
        // mixture what the dry air round it evaporates. A small cumulus is narrower than a shell, so
        // it never reached pure cloud anywhere, and in dry air — which evaporates any mixture under
        // nineteen parts in twenty cloud — it evaporated whole: scattered skies went from 7 % to 1 %.)
        // (The diagnostics' Edge Shell Scale widens or narrows it: _FishCloudDiagScale.y, 0 as shipped.)
        float shellMetres = FISH_CLOUD_SHELL_SHARE * b.y * (1.0 + _FishCloudDiagScale.y);
        float shellNoise = max(1e-4, _FishCloudCoverage.y * shellMetres / max(1.0, b.x));
        float critical = b.z;
        // How far out the shell's mixtures keep any water, in shells: 1 − χ*, all of it in saturated
        // air, a twentieth in dry air (CloudClimate.EdgeRampWidth). Never narrower than the pixel's
        // cone: an edge finer than a pixel cannot be drawn, only aliased.
        // Nor narrower than the samples along the ray are apart (FishCloudDetailAlong): an edge sharper
        // than a step is found by some samples and stepped over by the next, a coin toss per sample.
        float ramp = FishCloudEdgeRampWidth(critical, max(FishCloudDetailCone, FishCloudDetailAlong) / max(1.0, shellMetres));
        // Beyond this nothing the eddies do can bring water: the ramp and their reach.
        float outermost = -(ramp + FISH_CLOUD_EDDY_REACH);
        float inside = (body * profile - threshold) / shellNoise;
        // In shells, so in metres by the shell's width: how far out of reach of this band's water the point
        // stands, as the noise's slope across the ground has it.
        if (!below)
        {
            FishCloudEdgeAhead = min(FishCloudEdgeAhead, max(0.0, (outermost - inside) * shellMetres));
        }
        float density = inside > outermost ? 1.0 : 0.0;
        if (below)
        {
            // Under the base, in the same field read further down — so what hangs from a wet tower
            // hangs from that tower: the rain haze under a deep cloud that is raining, at what the
            // falling rain itself takes out of the light — the world's own figure for it, a few
            // kilometres of view in ordinary rain. It was a quarter of the cloud's own extinction,
            // which is ten times that.
            //
            // There used to be a ground fog here as well, densest at the ground and reaching the base
            // when heavy — a second fog with a depth of its own, drawn only over the sky, and only
            // where the fog passes were not drawing the real one; in practice, never. The fog is the
            // fog layer's (FishFogDensity), walked by the march beside the clouds.
            float under = saturate((trueAltitude - field.surface) / max(1.0, floorHere));
            float mass = saturate((body - threshold + 0.10) / 0.20);
            // Tapered away over the last tenth below the base: densest AT the base, it made a level
            // sheet there, and a level sheet seen edge-on is a line.
            float deep = saturate(reachM / 2500.0);
            float hang = _FishCloudSub.w * deep * mass * saturate((under - 0.3) / 0.6) * saturate((1.0 - under) / 0.1) * clearance;
            float extinction = hang;
            if (extinction > best)
            {
                best = extinction;
                // Negative marks what is under the base: the hang is lit as the air under a cloud
                // is, not as a cloud is.
                spot.high01 = -1.0;
                spot.layer = i;
                spot.cover = coverage;
                // What stands overhead, and how much light it lets down: the column's water from its
                // base to as high as it reaches here, as thick as the noise says it is over this spot.
                spot.under = mass;
                spot.tauOverhead = mass * lerp(1.0, FISH_STORM_WATER, stormCloud) * FishCloudColumnDepth(a.w, reachM);
                // And what it cannot hide: the open sky past the cloud's edge, seen sideways under
                // its base — none just under the base, more the further down, as the angle under the
                // edge opens. A cumulonimbus is about a third as wide as it is deep (StormAnatomy), so
                // its edge is a sixth of its depth away; an ordinary cloud's no nearer than a kilometre.
                // Half the sin of that angle is the share of all directions it covers.
                float gap = max(0.0, floorHere - altitude);
                float edge = max(1000.0, reachM / 6.0);
                spot.sideSky = 0.5 * gap / sqrt(gap * gap + edge * edge);
                spot.baseAbove = gap;
            }
            continue;
        }
        if (inside <= outermost)
        {
            continue;
        }
        // The cauliflower: a second, finer read of the same volume's Worley channels, which carves
        // the body back to its billows — rising bubbles of air, each with its own crown. How hard is
        // the convection's: a vigorous heap is all turrets, a settled deck barely lumpy. Harder toward
        // the top, where a real cloud is most broken up. The turrets lean with the cloud they grow on:
        // the lean is in `uv` already, which this is read from.
        //
        // It raises the cut in the hollows, by up to FISH_CLOUD_CARVE_DEPTH of the noise. It used to
        // remap the density, which carved as deep as the old soft ramp was wide — eight hundred metres
        // — and would carve no deeper than the hundred-metre shell now. A cut can only rise, so a point
        // outside the cloud before the carve is outside it after: the early-out above still holds.
        // Where it is not read (the light march past its first step, the shadow cookie) and as it
        // fades with distance it goes to its own mean, not to nothing, so neither moves an edge on
        // average — faded to nothing, the far sky and the shadows would have had more cloud than the
        // near sky.
        float carveAmount = _FishCloudTowerDrift.z * (column ? lerp(0.35, 1.0, field.vigour) : 0.5);
        if (carveAmount > 0.001)
        {
            float hCarve = saturate(hIn);
            float carveScale = lerp(0.25, 0.6, hCarve) * carveAmount;
            float erode = FISH_CLOUD_CARVE_MEAN * carveScale;
            if (carve)
            {
                // Five times the shape, a whole number on purpose: the drift wraps every 42 tiles.
                float3 midUV = uv * 5.0 + (warp - 0.5) * 0.6;
                float3 mid = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, midUV, 0).gba;
                float billows = mid.r * 0.625 + mid.g * 0.25 + mid.b * 0.125;
                // Eased toward its mean where the march cannot resolve it: the sample's footprint (half
                // the step along the ray, the pixel's cone across it) against the billows' own cells —
                // the shape's tile over five, four cells a tile in the channel that dominates: 300 m to
                // a kilometre. It was eased at a fixed footprint of 25 to 45 m, which is the STEP over
                // most of the sky, and the step grows from 45 to 90 m between 8 and 15 km: every
                // cloud's cauliflower faded to a third on a sphere round the camera, though billows ten
                // times the step were resolved either side of it (_FishCloudFix.z 1 brings that back).
                // Against the cells, the easing starts where the step is half a cell — past 80 km.
                float carveShown = _FishCloudFix.z > 0.5
                    ? lerp(1.0, 0.35, saturate((footprint - 25.0) / 20.0))
                    : lerp(1.0, 0.35, smoothstep(0.5, 1.0, footprint / (max(1.0, b.x) / 20.0)));
                erode = lerp(erode, (1.0 - billows) * carveScale, carveShown * FishCloudBaseKeep(hIn));
            }
            inside -= erode * FISH_CLOUD_CARVE_DEPTH / shellNoise;
            if (inside <= outermost)
            {
                continue;
            }
        }

        // The shell's eddies. They carry the edge in and out by up to half a shell, the size of the
        // largest of them (the detail volume: eddies from a quarter to a sixteenth of its tile), and
        // how far out a mixture keeps water is the ramp — so in dry air the edge is crisp and frayed,
        // in saturated air soft and frayed. Mixing at a cloud's edge is inhomogeneous — the drops
        // evaporate in a second or two, the eddies take tens to stir — so what is left is filaments
        // of cloud beside clear air, which is what a wisp is, and the drops that are left are the
        // cloud's own: the extinction goes with the water, linearly. Past half a shell inside, the
        // core, nothing the eddies do reaches.
        //
        // What the eddies do on average is always applied (FishCloudExpectedEdgeWater, closed form, no
        // texture read) — on every tier, in the light march, in the cookie and at any distance — and the
        // pattern itself is drawn over it as far as the screen can resolve each octave, so the cloud
        // is the same size near and far and on every tier; only how frayed it looks changes.
        float water = FishCloudExpectedEdgeWater(inside, FISH_CLOUD_EDDY_REACH, ramp);
        // What the screen can resolve across the ray and the samples along it: an eddy finer than two
        // steps was drawn anyway, on the view that the jittered step averages it out — it does, but only
        // over many frames, and until then it is a coin toss per sample: the grain a finer Step Scale
        // cured. It is drawn as its average instead, which keeps the cloud its size.
        float3 resolved = FishCloudDetailResolved(max(FishCloudDetailCone, FishCloudDetailAlong), b.y);
        float pattern = detailAmount * resolved.x * FishCloudBaseKeep(hIn);
        if (pattern > 0.001 && inside < FISH_CLOUD_EDDY_REACH)
        {
            // Carried by the same wind as its band, in the band's own frame, drawn out along the wind
            // by the band's stretch (cirrus's eddies are fibres, like its streaks), and leaned with the
            // cloud; its height from the cloud's own base, like the shape's — read at the world's
            // height, the wisps slid up and down through a deck as its base followed the air or rode
            // over a hill. The drift is wrapped to one tile across and a stretch of tiles along, so the
            // lookup moves by whole tiles at a wrap.
            float2 eddyAt = frame - e.zw;
            eddyAt.x = (eddyAt.x - leanM) / max(1.0, b.w);
            float3 detailUV = float3(eddyAt.x, altitude - floorHere, eddyAt.y) / max(20.0, b.y) + i;
            float3 detail = SAMPLE_TEXTURE3D_LOD(_FishCloudDetail, sampler_FishCloudDetail, detailUV, 0).rgb;
            // An octave the screen cannot resolve is its mean; what is left is measured against the
            // spread that is left, and spread evenly over −1..1 by the logistic stand-in for the
            // normal curve (scale 1.702, Haley 1952). Normalised, the eddies carry the edge over their
            // whole reach: raw, the combined channels hold 0.48 ± 0.07 and every edge got the same
            // thinning.
            float3 octaves = lerp(FishCloudDetailMean, detail, resolved);
            float3 spreads = FishCloudDetailWeights * FishCloudDetailSpread * resolved;
            float spread = max(1e-4, sqrt(dot(spreads, spreads)) * FISH_CLOUD_DETAIL_SPREAD_FIX);
            float z = (dot(octaves, FishCloudDetailWeights) - dot(FishCloudDetailMean, FishCloudDetailWeights)) / spread;
            float push = 2.0 / (1.0 + exp(-1.702 * z)) - 1.0;
            // The noise is high at its cells' middles. Engulfing low in the cloud, the middles are the
            // clear air drawn in and the cloud is the filaments between (wisps); higher up the middles
            // are the bubbles pushed out (billows).
            float local = FishCloudEdgeWater(inside + FISH_CLOUD_EDDY_REACH * FishCloudEddyPolarity(hIn) * push, ramp);
            // Saturated for the diagnostics' Detail Strength past 1, which extrapolates; within 0..1 the
            // lerp of two waters is already a water.
            water = saturate(lerp(water, local, pattern));
        }
        density = water;
        if (density <= 0.0)
        {
            continue;
        }

        // How much light it takes out, per metre, and how much stands above and below it.
        //
        // A rising parcel condenses water in proportion to how far it has risen above its base; with
        // a fixed number of drops that makes the extinction grow as the two-thirds power of the
        // height — grey and thin at the flat base, dense at the crown. A storm's core holds more of
        // its water than a heap does (FISH_STORM_WATER); it used to be made "wetter" by its rain
        // and its storm figure together, up to 2.6 times, which is how a storm's body came to be
        // opaque all the way to its anvil. Above the freezing level its drops turn to ice, three
        // times their size, and the same water takes out a third as much: a cumulonimbus's upper
        // half and its anvil are far thinner than the solid liquid base under them. A layer cloud
        // is lifted slowly as a sheet and fills in the same way within its own depth, keeping its
        // mean. An anvil's is its own ice's (WeatherMap.AnvilExtinction); the band's cirrus is even.
        //
        // `density` so far is how much of the cloud's full water is here, 0..1 — the cut, the carve
        // and the wisps. The depths above and below are a share of the profile's own integral: the
        // noise holds together far further up and down than across (VerticalScale), so how far into
        // the cloud's BODY a point is makes a fair guess at how much cloud its column holds.
        //
        // The body, not the water here. How much cloud stands over a point is set by the cloud's
        // shape over hundreds of metres, not by the eddy it happens to sit in: taken from the local
        // water, the hundred-metre shell was lit as a thin veil — the sky carried forward, the sun
        // scattered once — while the solid cloud a few metres inside it was lit by diffusion alone, a
        // step in brightness at every edge: a thin white rim round a flat grey cut-out. The body fills
        // over FISH_CLOUD_BODY_FILL of the noise (about eight hundred metres, a cloud's own half-width),
        // so the light grades from a cloud's rim to its core as smoothly as its thickness does.
        float fill = saturate(inside * shellNoise / FISH_CLOUD_BODY_FILL);
        float aboveBase = max(0.0, altitude - floorHere);
        float betaHere;
        float tauBelow;
        float tauAbove;
        if (column)
        {
            float water = lerp(1.0, FISH_STORM_WATER, stormCloud);
            float iceRatio = _FishCloudIce.y > 0.0 ? _FishCloudIce.y : 1.0;
            float glacHere = FishCloudGlaciated(altitude, field.freezing);
            float glacTop = FishCloudGlaciated(floorHere + ceilingM, field.freezing);
            betaHere = FishCloudColumnBeta(a.w, aboveBase) * water * lerp(1.0, iceRatio, glacHere);
            float depthHere = FishCloudColumnDepth(a.w, aboveBase);
            tauBelow = fill * water * depthHere * lerp(1.0, iceRatio, 0.5 * glacHere);
            tauAbove = fill * water * max(0.0, FishCloudColumnDepth(a.w, ceilingM) - depthHere) * lerp(1.0, iceRatio, 0.5 * (glacHere + glacTop));
        }
        else if (regime == 1)
        {
            float rise = saturate(hIn);
            betaHere = a.w * (5.0 / 3.0) * pow(max(0.02, rise), 2.0 / 3.0);
            float share = pow(rise, 5.0 / 3.0);
            tauBelow = fill * a.w * ceilingM * share;
            tauAbove = fill * a.w * ceilingM * (1.0 - share);
        }
        else
        {
            betaHere = inAnvil ? max(a.w, _FishWeatherMapParams.w) : a.w;
            tauBelow = fill * betaHere * aboveBase;
            tauAbove = fill * betaHere * max(0.0, ceilingM - aboveBase);
        }
        density *= betaHere;
        if (density > best)
        {
            best = density;
            spot.high01 = saturate(hIn);
            spot.layer = i;
            spot.cover = coverage;
            spot.tauBelow = tauBelow;
            spot.tauAbove = tauAbove;
            spot.under = 0.0;
            spot.tauOverhead = 0.0;
            spot.sideSky = 0.0;
            // A cloud's shadow on the ground is only as wide as the cloud — about as wide as it is
            // deep for a heap, a third for a cumulonimbus (StormAnatomy) — and past its edge the
            // ground is in the sun. Seen from up here that sunlit ring lies within an angle below the
            // horizontal whose sin² is its share of the light from below.
            float heightOverGround = max(1.0, overGround);
            float shadowEdge = max(1000.0, ceilingM / 6.0);
            spot.groundOpen = heightOverGround * heightOverGround / (heightOverGround * heightOverGround + shadowEdge * shadowEdge);
        }
    }
    // Already in per-metre extinction: the physics sets it, from how much water the air condenses
    // and how many drops it is shared between.
    return best;
}

// The same, for callers that do not care which band answered, or how.
float FishCloudDensity(float3 position, float detailAmount, float footprint, FishCloudField field, bool carve, out float high01)
{
    FishCloudPoint spot;
    float density = FishCloudDensityAt(position, detailAmount, footprint, field, carve, spot);
    high01 = spot.high01;
    return density;
}

// ── Lighting ───────────────────────────────────────────────────────────
//
// What the light in a cloud is, and why it used to look like smoke.
//
// A cloud absorbs almost nothing: every photon that goes in comes out somewhere, after tens or
// hundreds of scatterings. What was here before lit each point by Beer's law toward the sun, e^−τ,
// with two weaker copies standing in for the rest (e^−τ/2, e^−τ/4 — Wrenninge's octaves), and every
// one of them is still an exponential: a hundred and fifty metres into a cumulus the sun term had
// fallen to a fortieth and at three hundred to a four-hundredth (τ = 10 and 20 at the cumulus's own
// 0.06 /m — measured on a Python port of it). So every large cloud was lit by its ambient term alone:
// one flat grey-beige from edge to edge, which is exactly the dense, dead, unnatural look. Real light
// in a thick cloud does not fall off exponentially at all. It diffuses, and in the Eddington
// solution for a scattering layer its radiance falls in a straight line with the depth still to
// cross — a cumulus is white on the sunlit side because light that went in comes back out, and dark
// grey underneath because only a fraction of it wanders through (FishCloudDiffusion).
//
// And the old phase function was nearly isotropic: two Henyey–Greenstein lobes at g = 0.6 and −0.3,
// which throws 25 times too much light sideways and 4 times too much backwards against the Mie
// scattering of real drops, and has no forward spike at all — so single scattering filled a cloud's
// sides with light real drops never send there. Real drops (FishCloudPhaseMie) send almost nothing
// sideways; the side of a cloud is lit by the diffusion, not by single scattering.

/// Henyey–Greenstein, per steradian.
float FishCloudHG(float cosAngle, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1e-6, 1.0 + g2 - 2.0 * g * cosAngle), 1.5));
}

/// Jendersie and d'Eon's fit of Mie scattering by water drops (SIGGRAPH 2023 Talks, equations 4–7),
/// from their mean diameter in microns, 5 to 50. Twin of CloudClimate.MieParameters.
void FishCloudMieParams(float diameter, out float spikeG, out float bodyG, out float alpha, out float bodyWeight)
{
    float d = clamp(diameter, 5.0, 50.0);
    spikeG = exp(-0.0990567 / (d - 1.67154));
    bodyG = exp(-2.20679 / (d + 3.91029) - 0.428934);
    alpha = exp(3.62489 - 8.29288 / (d + 5.52825));
    bodyWeight = exp(-0.599085 / (d - 0.641583) - 0.665888);
}

/// How cloud drops of a mean diameter (µm) scatter light through an angle, per steradian: a
/// Henyey–Greenstein spike for the diffraction within a degree of the light — the blinding rim of a
/// thin cloud over the sun, the silver lining — and Draine's lobe for the rest, with the flat sides
/// and the rise toward the back that a single HG cannot give. A diameter of 0 is ice, which scatters
/// by its crystals' own broader law. Evaluated once per ray and band: the angle is the ray's.
// Diagnostics for the probe only (SkySimRender option sets); zero in the game. x: 1 draws the droplets
// without their diffraction spike (the body lobe alone), to measure what the spike does to sparkle.
float4 _FishCloudDebug;
// The frame's own phase, 0..1, moved on by the golden ratio each frame (FishCloudsFeature): what the
// light march's sample positions turn with.
float _FishCloudFramePhase;
// x the most optical depth one step may take in cloud (0: the default, 0.35); y the most a step may grow
// inside cloud, as a multiple of its base (0: the default, 2). From the profile's diagnostics.
float4 _FishCloudStepTau;

float FishCloudPhaseMie(float cosAngle, float diameter)
{
    if (diameter <= 0.0)
    {
        return FishCloudHG(cosAngle, FISH_ICE_ASYMMETRY);
    }
    float spikeG, bodyG, alpha, weight;
    FishCloudMieParams(diameter, spikeG, bodyG, alpha, weight);
    float draine = FishCloudHG(cosAngle, bodyG) * (1.0 + alpha * cosAngle * cosAngle) / (1.0 + alpha * (1.0 + 2.0 * bodyG * bodyG) / 3.0);
    // Or from the render profile's diagnostics (Droplet Spike off: _FishCloudDiagLight.x).
    if (_FishCloudDebug.x > 0.5 || _FishCloudDiagLight.x > 0.5)
    {
        return draine;
    }
    return (1.0 - weight) * FishCloudHG(cosAngle, spikeG) + weight * draine;
}

/// The mean cosine of that scattering, 0.86–0.89 for drops: the g in the diffusion's (1 − g)τ. Twin of
/// CloudClimate.MieAsymmetry.
float FishCloudMieAsymmetry(float diameter)
{
    if (diameter <= 0.0)
    {
        return FISH_ICE_ASYMMETRY;
    }
    float spikeG, bodyG, alpha, weight;
    FishCloudMieParams(diameter, spikeG, bodyG, alpha, weight);
    float g2 = bodyG * bodyG;
    float body = bodyG * (1.0 + alpha * (3.0 + 2.0 * g2) / 5.0) / (1.0 + alpha * (1.0 + 2.0 * g2) / 3.0);
    return (1.0 - weight) * spikeG + weight * body;
}

/// The light diffusing through a thick cloud at a point, as a share of what falls on it: the
/// Eddington solution for a layer that absorbs nothing.
///
/// Light falls on the cloud's lit side and flows toward its far side. At a point `toLight` optical
/// depths in from the lit side and `toFar` from the far one, the radiance is
///     F/4π · (2 + 3τ_far' + 3μ) / (1 + ¾(τ_light' + τ_far'))
/// with τ' = (1 − g)τ the depth as the diffusion counts it and μ the cosine between the way the light
/// flows and the way this radiance travels. A thin share of a vast cloud's light gets through
/// (T = 1/(1 + ¾τ'), about 15 % under a cumulus, 1–3 % under a cumulonimbus); on the lit side of a
/// thick one it comes to exactly the light falling on it — the whole of it reflected, which is why a
/// cumulus is white. The π the display's light units hide cancels the 4π.
float FishCloudDiffusion(float toLight, float toFar, float flowCosine, float g)
{
    float s = 1.0 - g;
    return max(0.0, 2.0 + 3.0 * s * toFar + 3.0 * flowCosine) / (4.0 * (1.0 + 0.75 * s * (toLight + toFar)));
}

/// How much of the sun gets through a cloud to the ground under it, against the open sky, for an
/// optical depth along the sun's ray: what came straight through and what scattered through
/// (Eddington, collimated beam: [(⅔ + μ₀) + (⅔ − μ₀)e^(−τ/μ₀)] / (4/3 + (1 − g)τ), τ vertical). For the
/// cloud shadow cookie: e^−τ alone left the ground under any thick cloud black to the sun, when
/// 15 % of it comes through a cumulus and a few per cent through a storm. Twin of
/// CloudClimate.GroundTransmission.
float FishCloudGroundTransmission(float slantDepth, float sunCosine, float g)
{
    float mu = clamp(sunCosine, 0.05, 1.0);
    float slant = max(0.0, slantDepth);
    float direct = exp(-slant);
    float total = ((2.0 / 3.0 + mu) + (2.0 / 3.0 - mu) * direct) / (4.0 / 3.0 + (1.0 - g) * slant * mu);
    return saturate(max(direct, total));
}

/// (1 − e^−x)/x, the share of a layer's full height a straight path through part of it meets.
float FishCloudThinShare(float x)
{
    return x < 1e-3 ? 1.0 - 0.5 * x : (1.0 - exp(-x)) / x;
}

/// The air between the camera and a point, as optical depth, red/green/blue: this world's molecules
/// (Rayleigh, thinning with its own scale height) and its haze (thinning four times faster). Along a
/// straight path between two heights the density averages to e^(−low/H)·(1 − e^(−rise/H))/(rise/H), so
/// cirrus eighty kilometres off is behind less air than a cumulus forty kilometres off at the
/// horizon — and blue goes first, so a far white cloud turns to the warm grey it is at the horizon
/// while the air in front of it adds its own blue.
float3 FishCloudAirDepth(float distance, float fromAltitude, float toAltitude)
{
    float low = max(0.0, min(fromAltitude, toAltitude));
    float rise = abs(toAltitude - fromAltitude);
    float airHeight = max(100.0, _FishCloudAirExtinction.w);
    float hazeHeight = max(10.0, _FishCloudAerosol.y);
    float air = exp(-low / airHeight) * FishCloudThinShare(rise / airHeight);
    float haze = exp(-low / hazeHeight) * FishCloudThinShare(rise / hazeHeight);
    return max(0.0, distance) * (_FishCloudAirExtinction.xyz * air + _FishCloudAerosol.x * haze);
}

// How much cloud stands between a point and the sun: the optical depth that way, in a few steps
// that grow as they go. Because it asks the whole field, a band below another is shadowed by it.
//
// Each step is a SEGMENT of the path and its sample is drawn from anywhere inside it: `phase.x` says
// where along it, `phase.y` how far the cone is turned about the light. Stratified sampling (Pharr,
// Jakob and Humphreys, PBR 4th ed., §8.5; the same per-pixel offset of the light samples as Schneider
// 2015 and Hillaire 2016): each segment's depth is then right on average, and what is wrong in one
// frame is noise the steadying averages away, since the phase is the ray's own and moves every frame.
//
// It used to sample at fixed points, 120·(k + 1)·(1 + 0.6k) metres out, the same for every pixel in
// every frame, and to count each for 120·(1 + 0.6k) metres of cloud. A fixed point cannot tell a
// segment that is all cloud from one that is all air bar that point, so the shadow it gave was not
// a gradient from the lit face in but a crisp COPY of whatever cloud those points fell in, moved
// that far toward the light: each billow was shaded by the billow above it, copied 120 m down onto
// its crown — dark crowns over pale undersides, repeated down the whole cloud, which is what the
// moon's night (with no sky light to fill the copies in) showed plainest — and at a deck's base the
// wind-drawn streaks copied six times along the fixed spokes, into stripes and a crosshatch. Nor did
// the points tile the path: they reached 2880 m and counted 1800 m of it. The segments now end where
// the old points stood, 120, 384, 792, 1344, 2040 and 2880 m (792 on three steps), so the light is
// looked for as far as it was, and each counts its own length.
float FishCloudLightDepth(float3 position, float3 toSun, float footprint, FishCloudField field, float travelled, float transmittance, float2 phase)
{
    // Probe diagnostic (_FishCloudDebug.y): hold the light march's phase fixed, as it was before it
    // was stratified per ray, to measure what the per-ray phase adds to frame-to-frame flicker.
    // The render profile's diagnostics: the light march off (_FishCloudDiag.z 2), no self-shadowing at
    // all; or its phase fixed as the probe's is (1).
    if (_FishCloudDiag.z > 1.5)
    {
        return 0.0;
    }
    if (_FishCloudDebug.y > 0.5 || _FishCloudDiag.z > 0.5)
    {
        phase = float2(0.5, 0.0);
    }
    // Half the steps past eight kilometres: the self-shadow of a cloud that far off is a gradient
    // a few pixels wide, and the light march is most of what a sample costs.
    int steps = (int)max(1.0, _FishCloudLight.x);
    // And half again deep inside, where what the sample adds is dimmed by everything in front.
    // Halved, never skipped: the depth used to be frozen once a ray was seven tenths absorbed and
    // the last value reused, so everything past that point in a cloud was lit with one stale
    // number — and the contour where rays crossed it showed as a pale skirt round a darker core
    // with a line between them.
    //
    // Halved over the same reach. The segments grow from 120 m, so half as many of them used to stop
    // at 792 m instead of 2880 m, and a cloud thicker than that toward the sun was lit as if it were
    // thin: every cloud past eight kilometres came out brighter, on a sphere round the camera, and so
    // did the inside of every cloud once the ray fell under 30 %, at a line. Now the fewer segments are
    // stretched to end where the full set does — the same depth on average, only noisier — and where
    // the march changes is dithered, from 6 to 10 km and from 40 % down to 20 %, so no line is left for
    // what noise remains to draw. (_FishCloudFix.w 1: the old reach and lines.)
    int fewer = max(3, steps / 2);
    bool halve;
    if (_FishCloudFix.w > 0.5)
    {
        halve = travelled > 8000.0 || transmittance < 0.3;
    }
    else
    {
        float chance = max(smoothstep(6000.0, 10000.0, travelled), 1.0 - smoothstep(0.2, 0.4, transmittance));
        // A dither of its own, uniform whatever the segment's place (x) or the cone's turn (y) is.
        halve = frac(phase.x * 7.0 + phase.y * 13.0) < chance;
    }
    float step = 120.0;
    if (halve && fewer < steps)
    {
        if (_FishCloudFix.w < 0.5)
        {
            // How far k segments reach: 120·k·(1 + 0.6(k − 1)) m.
            step *= (steps * (1.0 + 0.6 * (steps - 1))) / (fewer * (1.0 + 0.6 * (fewer - 1)));
        }
        steps = fewer;
    }
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
        // The segment from 120·k·(1 + 0.6(k − 1)) to 120·(k + 1)·(1 + 0.6k): 120 m long first,
        // growing by 144 m a step.
        float from = step * k * (1.0 + 0.6 * (k - 1));
        float span = step * (1.0 + 1.2 * k);
        float reach = from + phase.x * span;
        // Each step lands on a different spoke of the cone, a golden angle on from the last, and the
        // whole cone is turned by the ray's own phase: fixed, the six spokes leaned the light the same
        // way at every pixel and drew each streak's copies along them.
        float spoke = k * 2.399 + 6.2831853 * phase.y;
        float3 offset = toSun * reach + (side * cos(spoke) + up * sin(spoke)) * reach * 0.2;
        // The edges' eddies on average, never their pattern: the first of these segments is a
        // hundred and twenty metres long, far coarser than the eddies, and their average is what a
        // light path through them meets. It read the detail volume at half strength on every step — up to
        // six more texture reads a sample, for speckle in the self-shadow — and that half-strength
        // thinning made the light's cloud a different size from the one it was lighting.
        density += FishCloudDensity(position + offset, 0.0, footprint, field, k < 1, high01) * span;
        // Past this nothing gets through whatever the rest of the way holds; the remaining steps
        // would only be spent confirming it.
        if (density > 10.0)
        {
            break;
        }
    }
    return density;
}

/// How deep a point is from the cloud's far side along the light's own way through it, as optical depth:
/// a short march AWAY from the light — the light march's first two segments, 0–120 m and 120–384 m,
/// each sampled at its phase of them — and, where that is still in cloud, at least the column below.
///
/// The diffused sun (FishCloudDiffusion) is bright where the light has far to go before it can leave
/// the cloud and dark where it is about to. That far side was always the cloud's base. Right for a deck,
/// where light spreads sideways as fast as it sinks and can only leave through the bottom; wrong for a
/// heap or a tower lit from the side, whose light leaves through the side away from the sun. And the
/// base was reached through the column's depth under the point, which thins to nothing at every rim
/// (FISH_CLOUD_BODY_FILL) — so the rim facing the sun, with the whole cloud behind it, was taken for a
/// thin veil and reflected almost nothing, while the depths inside glowed through it, brightening
/// from the base up rather than toward the sun.
float FishCloudFarDepth(float3 position, float3 toSun, float footprint, FishCloudField field, float2 phase, float tauBelow)
{
    // The light march off (_FishCloudDiag.z 2): nothing to measure against; fixed (1, or the probe's
    // _FishCloudDebug.y): the middle of each segment, as the light march samples.
    if (_FishCloudDiag.z > 1.5)
    {
        return tauBelow;
    }
    if (_FishCloudDebug.y > 0.5 || _FishCloudDiag.z > 0.5)
    {
        phase = float2(0.5, 0.0);
    }
    float high01;
    float near = FishCloudDensity(position - toSun * (phase.x * 120.0), 0.0, footprint, field, true, high01);
    float next = FishCloudDensity(position - toSun * (120.0 + phase.x * 264.0), 0.0, footprint, field, false, high01);
    float depth = near * 120.0 + next * 264.0;
    return next > 0.0 ? max(depth, tauBelow) : depth;
}

/// The light one point of cloud sends toward the camera, per unit of what it takes out of the ray:
/// the sun scattered once and the sun diffused, the sky above and the ground below.
///
/// `phase` and `g` are the band's drops' (FishCloudPhaseMie, per ray). `tauSun` is the light march's
/// optical depth toward the sun; `tauFar` how deep the point is from the cloud's far side, which the
/// light flows toward. `viewUp` is the ray's own upward cosine.
///
/// Where each part comes from, and what it replaces:
/// - Single scattering: the sun's light, π·p(θ)·e^−τ. Only this has the spike, so only thin cloud
///   toward the sun — its edges, a veil over the disc — has a silver lining.
/// - The sun, diffused (FishCloudDiffusion): toward the light, the march's depth — handed over to
///   the column's own depth above the point where the march (a few kilometres) saw no end to it —
///   and weighted in by how optically thick the cloud is, 1 − e^(−(1−g)τ), so a wisp is lit by
///   single scattering alone and a heap by diffusion. This is the white of a cumulus, the grey of
///   its base, and the 1–3 % that gets to the bottom of a cumulonimbus.
/// - The sky: its own light falls on the cloud from above as a diffuse flux, and diffuses down to
///   the point through the depth above it. In the thin limit it is scattered once, and forward: a
///   veil overhead passes the sky's light on, which is why cirrus is whiter than the blue behind it.
/// - The ground: what it reflects falls on the cloud's base and diffuses up. Over sunlit ground a
///   cumulus base is lit from below (over snow it is nearly as bright as its sides); under a solid
///   deck the ground is in the cloud's own shadow and sends back only what the cloud let through.
///
/// None of it is darkened for rain: a raining cloud is darker because it is deeper, and the depths
/// say so. It used to be dimmed by nearly half for rain on top of that, and tinted toward the band,
/// which is how a storm's body came to be a navy block.
float3 FishCloudLight(float3 sunLight, float phase, float g, float tauSun, float tauAway, float tauFacing, FishCloudPoint spot, float cosAngle, float viewUp, float3 sky, float3 ground)
{
    float spread = 1.0 - g;
    float single = PI * phase * exp(-tauSun);
    float tauLight = tauSun + smoothstep(6.0, 12.0, tauSun) * max(0.0, spot.tauAbove - tauSun);
    // How deep the point is from the cloud's far side, which the diffused light flows toward: looking
    // into the light, the side facing the camera, whose depth is how far this ray has come through the
    // cloud (`tauFacing`); otherwise the side away from the light. For a deck that is its base — light
    // in a sheet can only leave through the bottom — and for a heap or a tower the side away from the
    // sun, measured along the light's own way (`tauAway`, FishCloudFarDepth), blended by how much of
    // a deck the column's cloud is here. (_FishCloudFixB.x 1: the base for every cloud, as it was.)
    bool columnBand = _FishCloudLayerF[clamp(spot.layer, 0, FISH_CLOUD_MAX_LAYERS - 1)].z > 0.5;
    float deck = columnBand ? smoothstep(0.7, 0.95, spot.cover) : 1.0;
    float awayFar = _FishCloudFixB.x > 0.5 ? spot.tauBelow : lerp(tauAway, spot.tauBelow, deck);
    float tauFar = lerp(awayFar, tauFacing, saturate(cosAngle));
    float multiple = (1.0 - exp(-spread * (tauLight + tauFar))) * FishCloudDiffusion(tauLight, tauFar, cosAngle, g);
    float depth = spot.tauAbove + spot.tauBelow;
    float thick = 1.0 - exp(-spread * depth);
    // Thin cloud scatters the sky's light once, and mostly forward: looking up at a veil, the light
    // it sends down is the sky above it, carried on (0.5 + g/2 of it); looking down on one, the
    // ground below. An isotropic scatterer would take half of each whichever way it was seen.
    float thinSky = 0.5 + 0.5 * g * clamp(viewUp * 4.0, -1.0, 1.0);
    float fromSky = lerp(thinSky, FishCloudDiffusion(spot.tauAbove, spot.tauBelow, viewUp, g), thick);
    float fromGround = lerp(1.0 - thinSky, FishCloudDiffusion(spot.tauBelow, spot.tauAbove, -viewUp, g), thick);
    // The ground under a cloud lies in its shadow for as much of the sky as it covers there, and gets
    // what the cloud lets through; between the clouds, and past the edge of this one's shadow, it is
    // in the sun. Under the middle of a storm only the far ring of sunlit ground lights its base.
    //
    // Past the edge, in the sun only between the OTHER clouds: the ground out there is covered as
    // much as the sky is. It was counted in full sun whatever the cover, so under an unbroken deck —
    // where there is no sunlit ground anywhere — the base was lit by half the open ground's light
    // (the ring past a notional kilometre-wide shadow), two to three times what the ground under the
    // deck sends up; at night, with the moon on the ground and hardly any sky, that was the brightest
    // light a deck's base had. With the cover in, a deck's base sees ground as lit as its own shade
    // leaves it, and broken cloud as before but for the covered share of the ring.
    float shade = 1.0 / (1.0 + 0.75 * spread * depth);
    float cover = saturate(spot.cover);
    float groundLit = lerp(1.0, shade + (1.0 - shade) * spot.groundOpen * (1.0 - cover), cover);
    // The render profile's strengths for the diffused sun, the sky and the ground: _FishCloudDiagLight.yzw
    // are one minus each (all zeros: each at 1; 1: left out).
    multiple *= 1.0 - _FishCloudDiagLight.y;
    fromSky *= 1.0 - _FishCloudDiagLight.z;
    fromGround *= 1.0 - _FishCloudDiagLight.w;
    return sunLight * (single + multiple) + sky * fromSky + ground * (groundLit * fromGround);
}

/// A shoulder on the cloud's own brightness: untouched up to 0.8, then rolling off so that nothing
/// reaches display white until it is several times over it.
///
/// Light scatters forward through cloud far more than any other way, so a cloud seen toward the
/// sun is many times brighter than the same cloud seen side-on — that is the glare round the sun
/// and it is right. But there is no tonemapping anywhere in this project, so the display simply
/// clips: side-on a lit rim works out at 0.9, at forty-five degrees from the sun 1.2, at twenty
/// 1.9 and straight toward it 2.7, and everything past 1 is the same white. Once the clouds were
/// made as translucent as real ones, "thin enough to glow" described most of every cloud, and the
/// whole sky within a wide cone of the sun went flat white — cloud, veil and rim alike, which read
/// as the clouds dissolving into a sphere round the sun, and left the light shafts nothing to be
/// cut out of. With the shoulder a rim toward the sun is 0.99, a veil 0.90 and a shaded core 0.63:
/// still a glare, but one with the clouds in it. Applied to the brightness and not per channel,
/// so a dawn cloud keeps its colour as it brightens. It stands in for a tonemapper; if the project
/// gains one, this comes out.
float3 FishCloudShoulder(float3 colour)
{
    float level = dot(colour, float3(0.2126, 0.7152, 0.0722));
    if (level <= 0.8)
    {
        return colour;
    }
    float rolled = 0.8 + 0.2 * (1.0 - exp(-(level - 0.8) / 0.6));
    return colour * (rolled / level);
}

// ── March ──────────────────────────────────────────────────────────────

// Where a ray meets the whole stack of bands: the shell from the lowest floor to the highest ceiling.
bool FishCloudRange(float3 origin, float3 direction, out float near, out float far)
{
    near = 0.0;
    far = 0.0;
    float innerNear, innerFar, outerNear, outerFar;
    bool meetsInner = FishCloudShell(origin, direction, _FishCloudLayer.x, innerNear, innerFar);
    if (!FishCloudShell(origin, direction, _FishCloudLayer.y, outerNear, outerFar) || outerFar < 0.0)
    {
        return false;
    }
    float height = FishCloudAltitude(origin);
    if (height < _FishCloudLayer.x)
    {
        // Under the stack: in through its floor, out through its ceiling.
        near = meetsInner ? innerFar : 0.0;
        far = outerFar;
    }
    else if (height > _FishCloudLayer.y)
    {
        // Over it: in through the ceiling, and out through the floor if the ray gets that far down.
        near = max(0.0, outerNear);
        far = meetsInner ? innerNear : outerFar;
    }
    else
    {
        // Inside the stack: flying between the bands, or standing in the fog.
        near = 0.0;
        far = !meetsInner || innerFar < 0.0 ? outerFar : innerNear;
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
// how far the ray may travel before the world blocks it; `jitter` breaks up banding; `lightSeed` is this
// pixel's own phase for the light march (two numbers, uniform and independent of `jitter`).
//
// `cloudMotion` is what the steadying needs to fetch this ray's history from where its cloud was a
// frame ago (FishCloudResolve.hlsl): x the mean distance along the ray of what it saw (m), y the mean
// wind gain of the bands it saw (CloudBand.WindScale, `_FishCloudLayerC[i].x`: how many times the low
// clouds' drift that band moves), both weighted by what each step added to the picture — its
// transmittance-weighted opacity, T·(1 − e^(−τ)). (0, 0) when the ray met nothing. The fog is counted
// in the distance but carries no gain: it drifts on its own, slower wind (FogLayerView.DriftWind),
// and the steadying's clip takes up what that leaves.
float4 FishCloudMarch(float3 origin, float3 direction, float depth, float jitter, float2 lightSeed, int steps, float detailAmount, out float cloudDistance, out float2 cloudMotion)
{
    cloudDistance = depth;
    cloudMotion = float2(0.0, 0.0);
    // The render profile's debug views (_FishCloudDiag.w): a ray that never reaches the cloud layers is black.
    FishCloudMarchDebug = float4(0.0, 0.0, 0.0, 0.0);
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
    // would spend the budget on nothing, so the march leaps the air where no band can be, lengthens
    // its steps through a band's empty air and shortens them in cloud.
    float distance = far - near;
    // The step inside cloud. It grows with distance from the camera: a cloud overhead is walked in
    // steps a third the size of one at the horizon, which is where the detail is spent and where
    // it can be seen. One step for the whole ray — and it was 90 m on nearly every ray, since any
    // ray through an eight-kilometre shell is long — meant the near sky was sampled as coarsely as
    // the far one, and the mip chosen from that step blurred the cloud overhead exactly as much as
    // the cloud at the horizon. That is what made the level of detail look as if it ran backwards.
    float fine = clamp(distance / steps, 18.0, 90.0);
    float fineNear = max(18.0, fine * 0.5);
    // Up among the clouds the march is at its dearest. From the ground most of a ray is empty air
    // crossed in one leap and the cloud at its end is soon opaque; from the height of the cloud
    // base upward every pixel starts in or beside cloud, looks along the layer rather than through
    // it, and walks its whole budget with a light march on every sample. The steps lengthen as the
    // camera climbs into the layer — by the camera's height alone, so it is one figure for the
    // whole screen and draws no line across it.
    float lowestBase = 1e6;
    UNITY_LOOP
    for (int q = 0; q < _FishCloudLayerCount; q++)
    {
        if (_FishCloudLayerA[q].w > 0.0 && _FishCloudLayerA[q].z > 0.002)
        {
            lowestBase = min(lowestBase, _FishCloudLayerA[q].x);
        }
    }
    float aloft = lowestBase < 1e5 ? smoothstep(0.6, 1.0, FishCloudAltitude(origin) / max(100.0, lowestBase)) : 0.0;
    fine *= 1.0 + 0.8 * aloft;
    fineNear *= 1.0 + 0.8 * aloft;
    // The render profile's diagnostics: Step Scale lengthens or shortens every step through the cloud
    // layers (_FishCloudDiagScale.z, 0 as shipped: × 1), and Distance Step Growth off keeps the near
    // step all the way out (_FishCloudDiag.y). Either may need more steps to reach as far, so the
    // budget grows by as much, to at most 1024 — slow, and meant to be.
    float stepScale = clamp(1.0 + _FishCloudDiagScale.z, 0.25, 4.0);
    bool stepGrows = _FishCloudDiag.y < 0.5;
    fine *= stepScale;
    fineNear *= stepScale;
    // The early exit (_FishCloudDiagScale.w, 0 as shipped: 0.05).
    float exitAt = clamp(0.05 + _FishCloudDiagScale.w, 0.001, 0.5);
    // A step must never be able to step over a whole band, or the pixels whose step happens to
    // straddle a thin deck find nothing while their neighbours find cloud — which is a crosshatch
    // across the sky, not noise, and no amount of temporal averaging removes it.
    float thinnest = 1e6;
    UNITY_LOOP
    for (int b = 0; b < _FishCloudLayerCount; b++)
    {
        float4 band = _FishCloudLayerA[b];
        // A band counts if it can have cloud anywhere the ray goes, not only overhead: a deck
        // that is clear at the camera can be solid ten kilometres upwind, and skipping it here
        // let the step jump it there. Its thinnest is the thinnest anywhere in the air map.
        if (band.z > 0.002 && band.w > 0.0)
        {
            float deck = _FishCloudLayerF[b].w;
            thinnest = min(thinnest, max(50.0, deck > 0.0 ? deck : band.y - band.x));
        }
    }
    // How steeply the ray climbs, for how much height a step gains (see the step, below).
    float climb = max(abs(direction.y), 0.05);

    // ── The fog along this ray ──
    // The fog is cloud whose base is the ground (FishFogLayer.hlsl), and it is walked here beside the
    // clouds — but in a shell of its own, from the ground to the highest its top's fade reaches
    // anywhere in view (_FishFogShell, FogLayerView.Shell). With no fog, none of this runs: the shell is
    // off and a fog-free frame marches exactly as it did. With one, the empty air between its top and
    // the cloud base is still skipped in one step, where the column's old ground fog made every height
    // from the ground up "possible" and walked all of it.
    bool fogOn = _FishFogShell.z > 0.5 && _FishFogLayer.x > 0.0 && _FishFogLayer.y > 0.0;
    // Where this ray is under the fog's ceiling: from `fogIn` to `fogOut`. A ray from inside it starts
    // there; one from above comes down into it; one that looks up from above never does.
    float fogIn = 1e9;
    float fogOut = -1.0;
    // Its steps. A fog has no edge to find, so it does not want the clouds' edge-finding step; it wants
    // its extinction integrated and its structure seen. So:
    //   - no step takes out more than two fifths of the light (τ ≤ 0.4), or the banks and the top it
    //     crosses are smeared into one sample: six metres in a dense fog, thirty in a fog, four hundred
    //     in a mist — and never more than a kilometre, which only a trace of fog, visible as nothing
    //     but a little haze on the horizon, comes near;
    //   - no step climbs or falls more than the top's own softness — half the fade (FogLayerView.
    //     TopSoftness: from 7.5 % of the depth in a mixed fog to all of it in a still mist) — or a
    //     tenth of the depth, so a ray looking down on the fog from a hill sees its top and not a coin
    //     toss of hit and miss. The top is a smooth fade now, not a billowed surface with eddies a few
    //     metres across, so the softness is the finest thing there is to see in it;
    //   - no step is finer than the one over which the fog takes a fiftieth of the light (0.02/β):
    //     finer, what it resolves cannot change a pixel. A trace of a fog, visible only as haze at the
    //     horizon, is walked in a few long strides.
    // Between the two it grows with distance, a fifth of the way travelled: fine at the camera, where
    // the fog you walk into is, and coarse by the far plane.
    float fogCap = 1e9;
    float fogFinest = 0.0;
    if (fogOn)
    {
        float first, second;
        if (FishCloudShell(origin, direction, _FishFogShell.y, first, second))
        {
            fogIn = max(near, first);
            fogOut = min(far, second);
        }
        float beta = _FishFogLayer.x;
        float topScale = max(max(0.25, _FishFogLayer.w), 0.1 * _FishFogLayer.y);
        fogCap = min(min(0.4 / beta, 1000.0), max(1.0, topScale / max(abs(direction.y), 1e-3)));
        fogFinest = min(fogCap, max(0.5, 0.02 / beta));
        fogOn = fogOut > fogIn;
    }

    float3 toSun = _FishCloudSunDir.xyz;
    float3 sunColour = _FishCloudSunColor.rgb * _FishCloudSunDir.w;
    float cosAngle = dot(direction, toSun);
    // The sky's real light on a cloud: the scene's ambient times the share of it the clear sky's own
    // light is (_FishCloudLight.y, SkySystem: about a third by day, all of it as the sun sets). The
    // ambient is set to light a scene that has no bounce light, and taken whole it lit every cloud's
    // shaded side and base two to three times over.
    float3 sky = _FishCloudAmbient.rgb * (_FishCloudLight.y > 0.0 ? _FishCloudLight.y : 1.0);
    float3 ground = _FishCloudGround.rgb;
    float2 axis = FishCloudAxis();
    float originAltitude = FishCloudAltitude(origin);

    // How each regime's drops scatter along this ray. The angle to the light is the ray's, so the
    // phase function — five exponentials and two powers — is worked out once per regime here, not
    // at every sample. Defaults for a regime with no band: ordinary cloud drops and ice.
    float diameterLow = 16.0, diameterMid = 12.0, diameterHigh = 0.0;
    UNITY_LOOP
    for (int r = 0; r < _FishCloudLayerCount; r++)
    {
        int regimeHere = (int)(_FishCloudLayerF[r].x + 0.5);
        float diameter = _FishCloudLayerF[r].y;
        if (regimeHere == 0)
        {
            diameterLow = diameter;
        }
        else if (regimeHere == 1)
        {
            diameterMid = diameter;
        }
        else
        {
            diameterHigh = diameter;
        }
    }
    float phaseLow = FishCloudPhaseMie(cosAngle, diameterLow);
    float phaseMid = FishCloudPhaseMie(cosAngle, diameterMid);
    float phaseHigh = FishCloudPhaseMie(cosAngle, diameterHigh);
    float gLow = FishCloudMieAsymmetry(diameterLow);
    float gMid = FishCloudMieAsymmetry(diameterMid);
    float gHigh = FishCloudMieAsymmetry(diameterHigh);
    // How far this ray has come through the cloud it is in now, as optical depth: the depth to the
    // cloud's near side, which is its far side from the light when the camera looks into the light.
    float tauInCloud = 0.0;

    float3 scattered = float3(0.0, 0.0, 0.0);
    float transmittance = 1.0;
    // `cloudMotion`'s sums: the weight, the distance and the wind gain, each step by what it added.
    float motionWeight = 0.0;
    float motionDistance = 0.0;
    float motionGain = 0.0;
    // How the ray is sampled. `travelled` is where the next step BEGINS, and the step is sampled at
    // `jitter` of its length: stratified, one sample in each step, at a place in it that is this ray's
    // own and moves on every frame (pass 0). With the jitter off every sample is at its step's middle.
    //
    // Where the steps fall is anchored on the air, never on the camera. The first begins where the ray
    // comes into air that can hold cloud — `near`, or a band's floor or ceiling the skip lands on — and
    // each cloud the ray meets re-anchors them on its own near edge (see where cloud is first found).
    // Nothing is strided over: every step in a band is a sample of it. That is what the jitter used to
    // have to cover for. The march strode through a band's empty air at up to three hundred metres of
    // height a step and walked back when a stride landed in cloud; a sheet thinner than a stride was
    // found by the rays whose stride happened to land in it and by no others — a third of them for a
    // hundred-metre sheet in the middle band. So a pixel's answer for such a sheet was all or nothing:
    // with the jitter off, rings of it round the point overhead (which rays hit went by the angle
    // they looked up at); with it on, a coin toss per pixel and per frame, about as strong as the
    // sheet itself, which the steadying's sixteen-frame average leaves at a fifth of its strength
    // — the grain. And inside thick cloud every ray walked back to a place on the same stride grid,
    // so its samples sat at the same distances from the camera as its neighbours': a cloud's surface
    // came out in terraces, one per step. Measured on a Python port of both marches (a 100 m sheet in
    // the 2 km middle band, Balanced): the jitter-on noise went from 17–33 % of the sheet's opacity to
    // 0.4–2.4 %, unbiased where the stride under-counted it by 40 %; jitter-off rings from all or
    // nothing to under 1 %; the terraces on a cumulus's lit face (β 0.05) from 1.7 % rms to 0.3 %.
    float travelled = near;
    // The last place a sample found nothing, where the search for a cloud's near edge starts.
    float lastEmpty = near;
    // The cloud's extinction at the last sample (1/m), for the step's optical-depth limit; 0 after air.
    float lastSigma = 0.0;
    // How far the ray has walked through empty air that can hold cloud since the last cloud or band
    // edge, for how far its steps may lengthen there.
    float emptyRun = 0.0;
    bool found = false;
    bool inside = false;
    float emptyDistance = 0.0;
    float insideUntil = 0.0;
    // Four times the step count rather than three: the near sky is now walked at half the step,
    // and a deck overhead needs the extra room. And a few dozen more with a fog to walk: at the steps
    // above a ray crosses a dense fog in fifteen or twenty and a mist to the far plane in about as many.
    int budget = steps * 4 + (fogOn ? 48 : 0);
    if (stepScale < 1.0 || !stepGrows)
    {
        budget = min(1024, (int)(budget * (stepGrows ? 1.0 : 4.0) / min(1.0, stepScale)));
    }
    // The debug views' tallies (_FishCloudDiag.w): the steps taken, and, weighted by what each cloud
    // sample adds to the picture, its light march's depth, its step's length and its detail octaves.
    bool debugging = _FishCloudDiag.w > 0.5;
    float debugSteps = 0.0;
    float debugWeight = 0.0;
    float debugLight = 0.0;
    float debugStep = 0.0;
    float3 debugDetail = float3(0.0, 0.0, 0.0);
    // The terrain view (6): the most any sample along the ray was kept off rock, and moved round it.
    float debugCleared = 0.0;
    // How far the last sample stood from the nearest cloud's outermost water (FishCloudEdgeAhead).
    float edgeAhead = 1e9;
    float debugTurned = 0.0;
    // The light march is most of the cost of a sample and is only worth it while what the sample
    // adds can still be seen: deep inside, with little light left to reach the camera, the last
    // depth found stands in.
    float depthToSun = 0.0;
    // And from the cloud's far side along the light's way (FishCloudFarDepth), found and reused with it.
    float depthAway = 0.0;
    // How many samples this ray has spent inside cloud. A big soft cloud no longer ends a ray in a
    // sample or two — at real extinction it has to be walked through — and a ray that has been
    // inside for dozens of steps is resolving nothing the first dozen did not: the step grows.
    float insideSamples = 0.0;
    // The camera is inside the cloud: the first cloud this ray met was at its own feet. What it
    // sees then is a fog a few hundred metres deep, and the fine steps that keep an edge crisp
    // from outside are resolving nothing — while every pixel on the screen is paying for them at
    // once, which is the one view where the march cannot afford it.
    // A measure and not a switch: a ray is more or less immersed by how near its first cloud was,
    // so no line is drawn across the sky where rays change from one kind to the other.
    float immersed = 0.0;
    // Whether depthToSun holds a depth found in CLOUD on the sample before this one. Only that may
    // be used again. Anything else — a value left from the mist under the base, from another cloud,
    // from before a gap — lights this cloud with somewhere else's shadow, and because how many
    // samples a ray has taken changes in shells with distance, so does which rays got the wrong
    // one: hard-edged bright stripes stacked toward the horizon.
    bool depthIsFresh = false;
    // Whether the last sample was in a smooth medium — the fog, or the haze under a wet base — rather
    // than in cloud.
    bool wasUnderBase = false;
    // Whether this ray has found cloud yet, for how immersed it is: a fog at the camera's feet is not
    // being inside a cloud (see `immersed`).
    bool metCloud = false;
    // The colour of the last thing sampled: what stands in for the rest of the ray once it stops.
    float3 lastColour = float3(0.0, 0.0, 0.0);
    // ── How the samples are integrated ──
    // Between two samples the extinction and the light are taken to change in a straight line (the
    // trapezoid), and the step after the last sample is closed when the ray leaves the medium or ends.
    // It used to hold each sample's extinction over its whole step: [travelled, travelled + step] took
    // σ(travelled + jitter·step), which is the cloud moved along the ray by (jitter − ½)·step. Where the
    // extinction changes across a step — a cloud's shell, its lit face, a thin sheet — that shifted the
    // whole ray's optical depth by up to (jitter − ½)·step·Δσ: at the 0.35 limit ±0.17, and far more
    // before it. That swing with the ray's phase was the grain, and at a fixed phase the terraces; the
    // trapezoid's error does not depend on the phase to first order. Its light is the exact mean of two
    // straight lines over the segment, the colour weighted by the extinction:
    //   (σa(2Ca + Cb) + σb(Ca + 2Cb)) / 3(σa + σb).
    // MEASURED on the march's C# twin (CloudMarchTests), the spread of one ray's answer over its phase:
    // a lit cumulus face 0.025 → 0.012, a thin cumulus 0.0045 → 0.0013, a 100 m sheet 0.008 → 0.002,
    // with no bias against the exact integral (±1 %) and half a sample more a ray. Sampling each step
    // at a phase of its own as well was tried: it doubled the grain. (_FishCloudFix.x 1: the old way.)
    bool trapezoid = _FishCloudFix.x < 0.5;
    // The last sample: where it was, its extinction (cloud and fog, and the cloud's alone), its colour,
    // and whether it is known — not where a ray from inside the stack began, which may be in cloud.
    float prevAt = near;
    float prevTotal = 0.0;
    float prevDensity = 0.0;
    float3 prevColour = float3(0.0, 0.0, 0.0);
    bool prevKnown = near > 0.0;
    // ── Dense cloud, cheaply (_FishCloudFixB.y 0) ──
    // Where a storm's samples went, measured on the march's twin (a storm and an overcast deck seen
    // from under them, 5 angles, Step Scale 0.25, early exit 0.001): 60 and 73 a ray, of which only 10
    // and 17 were in the cloud. The rest walked the rain haze under the base — smooth air, stepped as
    // finely as a cloud's edge because the step may never climb more than a share of the thinnest deck
    // anywhere in view. And in the cloud, the optical-depth limit kept every step short to the very end
    // of the ray, where a twentieth of the light is left and a step's error is a twentieth as visible.
    // So: the haze is walked by its optical depth, to the base and never past it (the step that reaches
    // the base ends on it, and is closed there, so the straight line to the next sample starts from the
    // base); the in-cloud limit grows as 1/√T (a step's error is second order, and seen through T), and
    // every limit as √(0.2/T) once under a fifth of the light is left (`deepIn`); and deep in, most
    // samples take the light depth of the one before. 19 and 26 samples a ray, three times fewer, with
    // the bias against a fine reference 0.0017 → 0.0026 and 0.0068 → 0.0025.
    bool economy = _FishCloudFixB.y < 0.5;
    // Whether the last sample was the haze under a base, and how high (curved altitude) that base is.
    bool lastWasHaze = false;
    float hazeBase = 0.0;
    // How many samples in a row have taken the light depth of the one before.
    int staleRun = 0;

    UNITY_LOOP
    for (int i = 0; i < budget; i++)
    {
        debugSteps += 1.0;
        // Stop once almost nothing more can reach the camera. At real extinction a ray no longer
        // dies on its first sample — it has to be integrated through the cloud — so where it is
        // allowed to stop is what the march costs. A twentieth is below what the composite shows.
        if (transmittance < exitAt)
        {
            break;
        }
        if (travelled >= far)
        {
            break;
        }
        // Where this step begins: whether cloud can be there (a metre in, so a step that begins exactly
        // on a band's floor, where the skip lands it, counts as inside the band), and whether the fog's
        // shell is. The step is clipped at the fog's floor and ceiling (below), so a step lies wholly in
        // the fog's shell or wholly out of it.
        float boundaryAltitude = FishCloudAltitude(origin + direction * (travelled + 1.0));
        float finest = FishCloudFinestAt(boundaryAltitude);
        bool stepPossible = finest > 0.0;
        bool fogShell = fogOn && travelled >= fogIn && travelled < fogOut;
        if (!stepPossible && !fogShell)
        {
            // Nothing can be here: go straight to the next place something can, or stop — a band
            // the ray climbs into or comes down on, or the top of the fog. The next step begins
            // exactly there, so every ray lays its samples of that band out from the band's own floor
            // or ceiling, a place that moves smoothly from one ray to the next. (It used to land half
            // a step short plus a share of a stride, and walk the band on the stride's grid.)
            float next = FishCloudNextPossible(origin, direction, travelled + 1.0, boundaryAltitude);
            bool intoFog = fogOn && fogIn > travelled && fogIn <= next;
            next = intoFog ? fogIn : next;
            // The last step is closed where cloud stops being possible: its extinction falls to nothing
            // there, in a straight line from the last sample.
            if (trapezoid && prevTotal > 0.0)
            {
                float closing = exp(-0.5 * prevTotal * max(0.0, travelled - prevAt));
                scattered += transmittance * prevColour * (1.0 - closing);
                transmittance *= closing;
            }
            prevAt = next;
            prevTotal = 0.0;
            prevDensity = 0.0;
            prevKnown = true;
            lastWasHaze = false;
            if (next > far)
            {
                break;
            }
            depthIsFresh = false;
            tauInCloud = 0.0;
            inside = false;
            travelled = next;
            lastEmpty = next;
            lastSigma = 0.0;
            emptyRun = 0.0;
            edgeAhead = 1e9;
            continue;
        }
        // The step. Small near the camera, the tier's step across the middle distance, and growing
        // again far out: a cloud a hundred kilometres off is a few pixels of haze and does not want
        // ninety-metre steps. (The step's own scale is already in fine and fineNear; with the growth
        // off the near step stands.)
        float cloudStepBase = stepGrows ? max(clamp(travelled * 0.006 * stepScale, fineNear, fine), travelled * 0.004 * stepScale) : fineNear;
        // In the fog's shell the fog's own step (see where `fogCap` is set) — and no coarser than the
        // clouds' where a cloud can be there too; where none can, the clouds' edge-finding step has
        // nothing to find, and a trace of a fog would be walked at it for kilometres.
        float fogStepHere = clamp(0.2 * travelled, fogFinest, fogCap);
        float stepBase = fogShell ? (stepPossible ? min(cloudStepBase, fogStepHere) : fogStepHere) : cloudStepBase;
        // Immersion is about the fog at the camera's feet and wears off with distance: standing in
        // a ground mist is also "the first thing the ray met was at its feet", and held for the whole
        // ray it doubled the step through every cloud in the sky behind that mist.
        float immersedHere = immersed * saturate(1.0 - (travelled - near) / 2000.0);
        // The mist is smooth — no billows, no detail, no light march — and a smooth thing does not
        // need an edge-finding step. It was walked as finely as cloud, and a ray looking down or
        // along a fog layer spent its whole budget on it.
        // Not in the fog's shell, which has its own step, set by what the fog can show.
        float mistStride = wasUnderBase && inside && !fogShell ? 2.5 : 1.0;
        // Half again once the ray is well inside cloud, where what each sample adds is already dimmed
        // by everything in front of it. A ramp over T = 0.6 to 0.4 and not a switch at 0.5: the samples
        // after a switch all move by half a step at once, and which rays switched where went by where
        // their samples fell — a contour in the cloud.
        float dimmed = 1.0 + 0.5 * (1.0 - smoothstep(0.4, 0.6, transmittance));
        // Longer, too, the longer the ray has been in the same thing. Inside cloud: a ray that has
        // been inside for dozens of steps is resolving nothing the first dozen did not. In the empty
        // air of a band: up to six times, as the strides were, over the first few steps of it — from
        // where the band or the last cloud began, which is where the steps are anchored.
        float growthCap = _FishCloudStepTau.y > 0.0 ? _FishCloudStepTau.y : 2.0;
        float growth = inside ? min(growthCap, 1.0 + insideSamples * (1.0 / 20.0)) : min(6.0, 1.0 + emptyRun / (4.0 * stepBase));
        // And once under a fifth of the light is left, every limit on a step in a medium grows as
        // √(0.2/T) (`economy`): what the rest of the ray adds is seen through that little light. On the
        // march's twin, a 3 km sheet seen along its length took 48 lit samples a ray and now 32, a deck
        // from below 17 and now 11, with the bias against a fine reference unchanged or better; from
        // T = 0.5 it took fewer still but a storm's base came out measurably wrong.
        float deepIn = economy && lastSigma > 0.0 && transmittance < 0.2 ? sqrt(0.2 / max(transmittance, 0.02)) : 1.0;
        float stepHere = stepBase * dimmed * growth * (1.0 + immersedHere) * mistStride * deepIn;
        // Through a band's empty air, never more than half the way to the nearest cloud's outermost water
        // (FishCloudEdgeAhead, from the last sample). The steps grew to six times their base there with
        // nothing to say how near a cloud was — hundreds of metres, against a shell of water and eddies a
        // hundred deep — so whether a step landed in a cloud's edge or over it went by the ray's phase,
        // new every frame: every cloud's edge flickered, and its wisps with it. Far from any cloud the steps
        // keep their length; nearing one they shorten until a sample lands in its edge, where the edge is
        // found (the halvings below).
        if (!inside)
        {
            stepHere = min(stepHere, max(stepBase, 0.5 * edgeAhead));
        }
        // Never longer than half the thinnest cloud in the sky, whatever has multiplied it. The
        // multipliers are earned inside a medium and used to be carried into the next one: a ray
        // that had walked a long mist reached the deck with steps as long as the deck is thick,
        // and whether a sample fell inside it or stepped over it changed in shells with distance —
        // hard-edged stripes, bright where the deck was missed.
        stepHere = min(stepHere, max(stepBase, stepPossible || !fogShell ? thinnest * 0.5 * deepIn : fogCap));
        // Never CLIMBING more than a share of the thinnest cloud that can be at this height
        // (FishCloudFinestAt): at least six or seven steps cross it at any angle. A level ray may take
        // long steps, since it is inside a thin slab for a long way; one looking straight up takes
        // short ones through a band it is only in for a few of them.
        if (stepPossible)
        {
            stepHere = min(stepHere, max(0.25 * fineNear, 0.15 * finest / climb) * deepIn);
        }
        // Never taking out much more than a third of the light that reaches it in one step (τ ≈ 1,
        // more once the ray is dimmed and growing), by the cloud found at the step before. A dense
        // cloud's lit face is where its colour changes fastest with depth, and a first sample that
        // took out nine tenths of the light at one depth or another was the grain on it, and — at
        // a fixed phase — its terraces. It costs two or three more samples a cloud: the early exit
        // comes at the same optical depth either way.
        // No step takes more than a small optical depth of the cloud it is in (0.35 by default: about 30 %
        // of what light is left). It was about one — up to 3.5 once the step had grown — so a single
        // sample could decide two thirds of a pixel, and where it landed (the ray's jitter) swung the
        // pixel so far that the frames could not average it away: the grain. Short steps only where
        // the cloud is dense; thin cloud and clear air keep their long ones.
        // Growing as the ray dims (`economy`): what a step gets wrong is seen through what light is
        // left, and is second order in its optical depth.
        float tauStep = (_FishCloudStepTau.x > 0.0 ? _FishCloudStepTau.x : 0.35) * (economy ? rsqrt(max(transmittance, 0.02)) : 1.0) * deepIn;
        if (lastSigma > 0.0)
        {
            stepHere = min(stepHere, max(0.1 * stepBase, tauStep / lastSigma));
        }
        // The haze under a base (`economy`): smooth, so walked by its optical depth — up to 32 of the
        // steps the distance sets — and never past the base: the step that would cross it ends on it.
        // Nothing thin hides under a base for the climb limit to find (the fog has its own shell).
        bool clippedAtBase = false;
        float baseEnd = 0.0;
        if (economy && lastWasHaze && !fogShell && lastSigma > 0.0)
        {
            float hazeStep = min(max(stepHere, tauStep / lastSigma), 32.0 * stepBase);
            float altitudeNow = FishCloudAltitude(origin + direction * travelled);
            float climbRate = (FishCloudAltitude(origin + direction * (travelled + 10.0)) - altitudeNow) * 0.1;
            if (climbRate > 1e-4)
            {
                float toBase = max(1.0, (hazeBase - altitudeNow) / climbRate);
                if (toBase <= hazeStep)
                {
                    hazeStep = toBase;
                    clippedAtBase = true;
                }
            }
            stepHere = hazeStep;
            baseEnd = travelled + hazeStep;
        }
        // In the last quarter of its budget, never so short that what is left of the ray cannot be walked
        // in the steps that are left: a ray cut off by its budget showed whatever was behind the cloud
        // where it stopped. Only then — most of a ray is empty air that is leapt, so spreading the budget
        // over the whole distance from the start would have made every horizon ray's steps huge.
        int left = budget - i;
        if (left * 4 < budget)
        {
            stepHere = max(stepHere, (far - travelled) / max(1.0, (float)left));
        }
        if (fogShell)
        {
            stepHere = min(stepHere, fogCap);
        }
        // Never across the fog's floor or ceiling, or past the end of the ray: the next step begins on
        // it, so how the ray is sampled on either side of it goes by where it is and not by how many
        // steps happened to fit before it. (The fog's ceiling used to be crossed at whatever phase the
        // fog left the ray at, and it was re-phased by hand.)
        if (fogOn && travelled < fogIn)
        {
            stepHere = min(stepHere, fogIn - travelled);
        }
        if (fogOn && travelled < fogOut)
        {
            stepHere = min(stepHere, fogOut - travelled);
        }
        stepHere = min(stepHere, far - travelled);
        // Still ending on the base only if nothing after it cut the step short.
        clippedAtBase = clippedAtBase && abs(travelled + stepHere - baseEnd) < 0.5;
        // Where in its step this ray samples: `jitter` of it, one number for the whole ray, uniform
        // from pixel to pixel and moved on every frame. One sample in each step at a uniformly random
        // place in it is an unbiased estimate of what lies along the ray, however thin, and its noise
        // averages away; the steps are laid out from the air (see `travelled`), so the phase is all
        // there is to it — the offset used to be carried from step to step by hand, and got wrong
        // twice, when the steps were a grid laid from the camera.
        float at = travelled + jitter * stepHere;
        float3 position = origin + direction * at;
        float high01;
        int layerIndex;
        // How wide this sample's pixel is, m, for which octaves of the edges' eddies can be drawn
        // (FishCloudDetailResolved) and how narrow an edge can be: the cone of the texel the steadying
        // resolves — half a marched one, since it looks through a different place in each texel every
        // frame. Across the ray only: along it the step is jittered through its whole length, which
        // averages to the right optical depth however fine the eddies, so the step is not a reason to
        // drop them. The detail used to be faded out whole with distance, 2.5 to 11.5 km, and the
        // erosion with it: nearer than most of the clouds in view, and a far cloud came out thicker.
        FishCloudDetailCone = at * _FishCloudLodParams.x * _FishCloudShapeParams.x;
        // Along the ray, the step the distance sets — never its multipliers, which follow how far this
        // ray has got into the cloud (see `footprint`) — times the profile's Detail Step Footprint.
        FishCloudDetailAlong = cloudStepBase * max(0.0, _FishCloudStepTau.z);
        // (Times the render profile's Detail Strength: _FishCloudDiagScale.x, 0 as shipped.)
        float detailHere = detailAmount * (1.0 + _FishCloudDiagScale.x);
        // The sample itself may lie past the band's ceiling, where the step ran out of it: no cloud.
        float altitudeHere = FishCloudAltitude(position);
        bool cloudPossible = FishCloudPossibleAt(altitudeHere);
        // How much world this sample answers for, which picks the mip it reads (FishCloudDensityAt).
        // Two things widen it, and the wider wins: along the ray the step, across it the pixel's cone,
        // which opens with distance — a sample twenty kilometres out speaks for twenty metres of sky.
        // Scaled by the whole footprint and not just the cone: of the two terms the step is the
        // larger over most of the sky, so a knob that only touched the cone would not be a knob.
        // Half the step, because a sample stands for the half-step either side of it; the cone
        // takes over past about forty kilometres.
        // From the step distance alone sets, never from the multipliers on it: those depend on how
        // far this particular ray has got into the cloud, and a mip that follows them makes the
        // inside of a cloud blurrier than its edge and different from one frame's jitter to the
        // next — which the history then shows as the cloud slowly breathing in and out.
        // The cloud's step, never the fog's: a cloud sample's footprint is the same on either side of
        // the fog's ceiling, or its carving would change along that line.
        float footprint = max(cloudStepBase * 0.5, at * _FishCloudLodParams.x) * _FishCloudLodParams.z;
        // The air here, read where it rose from: a convective cloud leans downwind with height
        // (FishCloudLean), and its air map, towers and storms lean with it — the field read once
        // per sample at the lean of this sample's height. Only where cloud can be: in the fog's shell
        // under an empty sky, a sample pays for the fog alone.
        FishCloudField field = (FishCloudField)0;
        FishCloudPoint spot = (FishCloudPoint)0;
        float density = 0.0;
        if (cloudPossible)
        {
            field = FishCloudFieldAt(position.xz - axis * FishCloudLean(altitudeHere));
            density = FishCloudDensityAt(position, inside ? detailHere : 0.0, footprint, field, true, spot);
            edgeAhead = FishCloudEdgeAhead;
            if (debugging && _FishCloudDiag.w > 5.5)
            {
                float2 axisHere = dot(_FishCloudWindDir.xy, _FishCloudWindDir.xy) > 1e-6 ? _FishCloudWindDir.xy : float2(0.0, 1.0);
                debugCleared = max(debugCleared, 1.0 - FishCloudClearance(position.xz, altitudeHere, _FishCloudFlow.y));
                debugTurned = max(debugTurned, length(FishFlowAround(position.xz, altitudeHere, _FishCloudFlow.y, axisHere)));
            }
        }
        else
        {
            edgeAhead = 1e9;
        }
        high01 = spot.high01;
        layerIndex = spot.layer;
        // The fog here: its layer over this ground — level over the pooled air, its top a smooth fade —
        // and its banks, one read of the shape volume (FishFogDensity). Nothing finer: the top's heave,
        // its eddies from the detail volume and the wisps are gone, and with them three of the five
        // texture reads a fog sample paid for. Read at the curved altitude, like everything else here:
        // a level ray leaves a layer thirty metres deep eighteen kilometres out, where the world has
        // curved away under it — on a flat world it would never leave, and every horizon would be the fog's.
        float fogBeta = 0.0;
        FishFogColumn fogColumn = (FishFogColumn)0;
        if (fogShell)
        {
            fogColumn = FishFogColumnAt(position.xz);
            fogBeta = FishFogDensity(float3(position.x, altitudeHere, position.z), fogColumn, 0.0);
        }
        // The end of the drawn sky. Haze has already turned a cloud into the colour of the horizon
        // well before this, so what is marched out here is a flat smear that costs as much as the
        // cloud overhead — more, since these are the longest rays in the frame. It thins to nothing
        // over the last quarter of the draw distance, so the sky dissolves into the haze it was
        // already the colour of rather than stopping on an edge.
        // How far cloud is drawn, which depends on how high it is. One distance for the whole
        // sky is a sphere round the camera, and a sphere cuts each layer along its own cone: set for
        // the deck (44 km, where a cloud a kilometre up is already down at the horizon) it cut the
        // cirrus off ten degrees up the sky. High cloud is seen from much further because it IS
        // much further when it is low in the sky — eight kilometres up and five degrees above the
        // horizon is eighty-five kilometres away — so the distance grows with the height.
        float drawn = _FishCloudLodParams.w > 1.0 ? _FishCloudLodParams.w * (1.0 + max(0.0, altitudeHere) / 2500.0) : 1e9;
        float leaving = smoothstep(drawn * 0.7, drawn, at);
        density *= 1.0 - leaving;
        // Two media in one sample, where the fog lies under a low base or the haze of a wet one: their
        // extinctions add, and each lights what it takes out.
        float total = density + fogBeta;
        if (total > 0.0)
        {
            if (!inside)
            {
                // The first cloud since empty air: its near edge lies between the last sample that
                // found nothing and this one. It is found — five halvings, a thirty-second of the gap,
                // by the same density the detection read (no detail, no light) — and the steps begin
                // again ON it, so this cloud is walked from its own surface and not from wherever the
                // grid of steps happened to cross it. That grid is what drew the terraces: every ray
                // sampled a cloud's face at the same distances from the camera as its neighbours, so
                // its depth into the face, and the face's light, stepped with the distance. It is not
                // counted: the walk from the edge takes its place.
                inside = true;
                emptyDistance = 0.0;
                // Hold the fine steps and the detail at least until the ray is back here.
                insideUntil = at;
                // Not in the fog's shell, which has no edge to find: the fog is a smooth layer, walked
                // at its own step from where the ray came into it, and the cloud in it with it.
                if (!fogShell && density > 0.0)
                {
                    float outside = lastEmpty;
                    float within = at;
                    UNITY_LOOP
                    for (int e = 0; e < 5; e++)
                    {
                        float middle = 0.5 * (outside + within);
                        float3 probe = origin + direction * middle;
                        float probeAltitude = FishCloudAltitude(probe);
                        float probeDensity = 0.0;
                        if (FishCloudPossibleAt(probeAltitude))
                        {
                            FishCloudField probeField = FishCloudFieldAt(probe.xz - axis * FishCloudLean(probeAltitude));
                            FishCloudPoint probeSpot;
                            probeDensity = FishCloudDensityAt(probe, 0.0, footprint, probeField, true, probeSpot);
                        }
                        if (probeDensity > 0.0)
                        {
                            within = middle;
                        }
                        else
                        {
                            outside = middle;
                        }
                    }
                    travelled = 0.5 * (outside + within);
                    lastSigma = density;
                    emptyRun = 0.0;
                    // The cloud's edge: its extinction starts from nothing here.
                    lastWasHaze = false;
                    prevAt = travelled;
                    prevTotal = 0.0;
                    prevDensity = 0.0;
                    prevKnown = true;
                    continue;
                }
            }
            bool first = !found;
            if (first)
            {
                cloudDistance = at;
                found = true;
            }
            // Cloud proper, as against the smooth media — the fog, and the haze under a wet base.
            bool cloudHere = density > 0.0 && high01 >= 0.0;
            // How immersed the ray is goes by how near its first CLOUD was. Standing in a fog is not
            // being inside a cloud, and it doubled the step through every cloud in the sky behind it.
            if (cloudHere && !metCloud)
            {
                metCloud = true;
                immersed = 1.0 - smoothstep(60.0, 300.0, at - near);
            }
            // Out of the haze under a base and into the cloud itself: the base is an edge, and it is found
            // as the near face of a cloud seen from clear air is (above). It was not — the haze already
            // counted as being inside, so the ray walked from the haze's long strides (up to 32 of the
            // steps the distance sets, by its own thin optical depth, and only clipped to the base for a
            // ray climbing toward it) straight into the cloud wherever a stride happened to land. Which
            // depth into the base the first sample took then went by each texel's own phase, moved on
            // every frame: the blocks along every cloud's underside and their flicker, plainest on the
            // level rays that see a base from the side. The haze is closed up to the base, the base is
            // found in five halvings between the last haze sample and this one, and the steps begin
            // again on it, with the cloud's own extinction to size them.
            if (cloudHere && wasUnderBase && lastWasHaze && prevKnown && at > prevAt + 1.0)
            {
                float hazeSide = prevAt;
                float cloudSide = at;
                UNITY_LOOP
                for (int b = 0; b < 5; b++)
                {
                    float middle = 0.5 * (hazeSide + cloudSide);
                    float3 probe = origin + direction * middle;
                    float probeAltitude = FishCloudAltitude(probe);
                    bool probeCloud = false;
                    if (FishCloudPossibleAt(probeAltitude))
                    {
                        FishCloudField probeField = FishCloudFieldAt(probe.xz - axis * FishCloudLean(probeAltitude));
                        FishCloudPoint probeSpot;
                        float probeDensity = FishCloudDensityAt(probe, 0.0, footprint, probeField, true, probeSpot);
                        probeCloud = probeDensity > 0.0 && probeSpot.high01 >= 0.0;
                    }
                    if (probeCloud)
                    {
                        cloudSide = middle;
                    }
                    else
                    {
                        hazeSide = middle;
                    }
                }
                float edge = 0.5 * (hazeSide + cloudSide);
                // The haze from its last sample to the base, at that sample's extinction and light.
                float hazeClosing = exp(-prevTotal * max(0.0, edge - prevAt));
                scattered += transmittance * prevColour * (1.0 - hazeClosing);
                transmittance *= hazeClosing;
                travelled = edge;
                lastSigma = density;
                emptyRun = 0.0;
                insideSamples = 0.0;
                tauInCloud = 0.0;
                wasUnderBase = false;
                lastWasHaze = false;
                prevAt = edge;
                prevTotal = 0.0;
                prevDensity = 0.0;
                prevKnown = true;
                continue;
            }
            // Out of the smooth media and into the cloud, or the other way: what was learnt about how
            // coarsely the last medium could be walked does not apply to this one.
            bool underBase = !cloudHere;
            if (underBase != wasUnderBase)
            {
                insideSamples = 0.0;
                tauInCloud = 0.0;
            }
            wasUnderBase = underBase;

            float3 cloudColour = float3(0.0, 0.0, 0.0);
            if (density > 0.0)
            {
                int regimeHere = (int)(_FishCloudLayerF[clamp(layerIndex, 0, FISH_CLOUD_MAX_LAYERS - 1)].x + 0.5);
                float phase = regimeHere == 0 ? phaseLow : (regimeHere == 1 ? phaseMid : phaseHigh);
                float g = regimeHere == 0 ? gLow : (regimeHere == 1 ? gMid : gHigh);
                // The sun this band sees: whiter and brighter than the ground's by the air under it.
                float3 sunLight = sunColour * _FishCloudLayerSun[clamp(layerIndex, 0, FISH_CLOUD_MAX_LAYERS - 1)].rgb;
                if (!cloudHere)
                {
                    // What hangs under a cloud's base is lit as the air there is: the haze of the far
                    // sky — the colour SkySystem works out from the horizon — and a little of the sun
                    // scattered forward through about one optical depth of it. No light march: it
                    // takes a sliver of the light and is lit evenly.
                    //
                    // Under a cloud, only what that cloud lets down: its own diffuse transmission, from
                    // the depth of the column overhead, and the open sky past its edge, seen sideways
                    // under the base. Lit as the open air everywhere, the rain under a cumulonimbus was
                    // drawn as bright as a noon haze — a pale veil hanging from a black cloud; a real
                    // shaft is grey and darkens up toward the base, where less of the open sky is seen.
                    depthIsFresh = false;
                    float3 air = _FishCloudHaze.rgb + sunLight * min(1.0, PI * phaseLow) * exp(-1.0) * 0.12;
                    float through = 1.0 / (1.0 + 0.75 * (1.0 - gLow) * spot.tauOverhead);
                    float lit = saturate(lerp(1.0, min(1.0, 1.25 * through) + spot.sideSky, spot.under));
                    cloudColour = air * lit;
                }
                else
                {
                    // The light march is most of what a sample costs, so it is spent where it shows:
                    // once a ray is well into a cloud, on every other sample. The depth toward the sun
                    // changes slowly along a ray, and one step's worth of staleness is nothing like the
                    // old fault of freezing it for the rest of the cloud. Used again only from the cloud
                    // sample directly before, only once, and only while the step is short beside the
                    // light march's own 120 m: far out a single step is hundreds of metres and a depth
                    // that stale is a different part of the cloud.
                    //
                    // WHICH samples reuse is this ray's own coin, not the count's parity. A reused
                    // depth is one step stale — looking up, from a step lower, so a little darker —
                    // and with the reuse starting on the first sample past T = 0.6 and alternating
                    // from there, how many samples were darkened went up and down with how many steps
                    // fit between T = 0.6 and the end of the ray: a count that steps with the angle
                    // of view, so a uniform deck was darkened in rings round the point overhead. From a
                    // phase the ray draws for itself, half its samples are stale on average whatever
                    // their number, and the darkening no longer changes with the angle.
                    //
                    // And eased in and out rather than switched: from none at T = 0.7 to every other
                    // sample by T = 0.5, and out again as the step grows from 60 to 120 m. The hard
                    // T = 0.6 and 90 m lines were a contour inside every cloud and a sphere round the
                    // camera where the stale share changed at once. (_FishCloudFix.w 1: the lines.)
                    float coin = frac(jitter * 7.31 + insideSamples * 0.5);
                    float reuseChance = _FishCloudFix.w > 0.5
                        ? (transmittance <= 0.6 && stepHere < 90.0 ? 1.0 : 0.0)
                        : (1.0 - smoothstep(0.5, 0.7, transmittance)) * (1.0 - smoothstep(60.0, 120.0, stepHere));
                    bool reuse = depthIsFresh && !first && coin >= 1.0 - 0.5 * reuseChance;
                    if (economy)
                    {
                        // Deeper in, more of them and in runs (`economy`): under a third of the light left
                        // one sample in two, under a tenth seven in eight — what they add is seen through
                        // that little light, and a depth a few steps stale is still this cloud's. Never
                        // more than seven in a row, and never across a gap or from another medium.
                        float deep = 1.0 - smoothstep(0.1, 0.35, transmittance);
                        float chance = max(0.5 * reuseChance, lerp(0.5, 0.875, deep) * step(transmittance, 0.35));
                        float draw = frac(jitter * 7.31 + insideSamples * 0.6180340);
                        reuse = depthIsFresh && !first && staleRun < (transmittance < 0.35 ? 7 : 1) && draw < chance;
                    }
                    staleRun = reuse ? staleRun + 1 : 0;
                    if (!reuse)
                    {
                        // Where in each of its segments the light march samples, and how its cone is
                        // turned: this pixel's own phase (`lightSeed`, pass 0: a noise of its own, not the
                        // ray's, moved on each frame as the ray's is), carried on from sample to sample
                        // down the ray by an R2 low-discrepancy step, so the samples of one ray do not all
                        // look along the same lines.
                        //
                        // It was one phase for the whole screen each frame (_FishCloudFix.y 1). Then the
                        // light march's error moved every pixel's lighting the same way at once, the nine
                        // texels the steadying clips each history to moved with it, and a history that
                        // had averaged many phases fell outside them and was clipped — worst on bright,
                        // smooth cloud, where the box is narrowest: the self-shadow's noise came through
                        // as flicker and sparkle. Before that it was the pixel's own ray jitter, when the
                        // ray's phase was tied to the place in its texel (FishCloudsFeature, JitterPhase)
                        // and drew the scale pattern in the light as well.
                        float2 lightBase = _FishCloudFix.y > 0.5
                            ? float2(_FishCloudFramePhase, _FishCloudFramePhase * 1.6180340) + float2(0.5, 0.0)
                            : lightSeed;
                        float2 lightPhase = frac(lightBase + i * float2(0.7548777, 0.5698403));
                        FishCloudReadsTerrain = false;
                        depthToSun = FishCloudLightDepth(position, toSun, footprint, field, at, transmittance, lightPhase);
                        depthAway = _FishCloudFixB.x > 0.5 ? spot.tauBelow : FishCloudFarDepth(position, toSun, footprint, field, lightPhase, spot.tauBelow);
                        FishCloudReadsTerrain = true;
                    }
                    // Only a depth found in this cloud may be used again: once, or in runs deep in (`economy`).
                    depthIsFresh = economy ? true : !reuse;
                    // How far this ray has come through the cloud: looking into the light, the depth of the
                    // cloud's far side, which faces the camera (FishCloudLight). On the camera's side of a
                    // cloud against the sun it is nothing, and only the silver lining at its thin edge is
                    // bright. (To this sample: through the segment from the last one, in a straight line.)
                    float tauHere = trapezoid
                        ? tauInCloud + 0.5 * (prevKnown ? prevDensity + density : 2.0 * density) * max(0.0, at - prevAt)
                        : tauInCloud + 0.5 * density * stepHere;
                    cloudColour = FishCloudLight(sunLight, phase, g, depthToSun, depthAway, tauHere, spot, cosAngle, direction.y, sky, ground);
                    tauInCloud = trapezoid ? tauHere : tauInCloud + density * stepHere;
                }
            }
            else
            {
                depthIsFresh = false;
            }

            // The fog is lit without a march, since its whole layer is known in closed form: the
            // sun's beam down through the fog above this point along the sun's slant, what that fog
            // has already turned arriving diffuse, and the sky's own light coming down the same way
            // (FishFogLight) — lit by the light on the GROUND, the scene's sun and sky, which the
            // clouds overhead have already dimmed, and not by the light over the clouds. And by the
            // cloud over THIS sample, read off the same cloud shadow the ground here is lit through
            // (FishFogSunShare), not the average over the view: under a storm's core that average
            // lit the fog several times brighter than the ground and the snow standing in it.
            float3 fogColour = float3(0.0, 0.0, 0.0);
            if (fogBeta > 0.0)
            {
                fogColour = FishFogLight(altitudeHere, direction, fogColumn, _FishFogLayer.x, FishTerrainSunlit(position), FishFogSunShare(position));
            }
            // Each medium lights what it takes out of the ray.
            float3 colour = (cloudColour * density + fogColour * fogBeta) / total;
            colour += _FishWeatherCloud.w * float3(0.85, 0.9, 1.0) * 2.0;   // lightning lights the volume
            // The air between: what it takes out of the cloud's light, per colour, and what it adds of
            // its own — the haze, the in-scattered light of the same air (FishCloudAirDepth). Physical
            // per colour and per height, where it was one grey blend with distance capped at 0.86.
            float3 airThrough = exp(-FishCloudAirDepth(at, originAltitude, altitudeHere));
            colour = colour * airThrough + _FishCloudHaze.rgb * (1.0 - airThrough);
            // And through the dissolve at the end of the drawn sky it goes the rest of the way to the
            // horizon's colour, so what thins out is already invisible against the sky behind it.
            colour = lerp(colour, _FishCloudHaze.rgb, leaving);
            colour = FishCloudShoulder(colour);
            // The segment this sample closes: from the last sample (or the cloud's edge) to this one,
            // extinction and light each in a straight line (see `trapezoid`) — or, the old way, this
            // sample's own step at its own extinction. A ray that began in cloud holds its first
            // sample back to where it began.
            float segmentDepth;
            float3 source;
            if (!trapezoid)
            {
                segmentDepth = total * stepHere;
                source = colour;
            }
            else if (prevKnown)
            {
                float span = max(0.0, at - prevAt);
                segmentDepth = 0.5 * (prevTotal + total) * span;
                // At an edge the last extinction is nothing and its colour is never needed; this one's
                // stands in so the weights below need no special case.
                float3 fromColour = prevTotal > 0.0 ? prevColour : colour;
                source = (prevTotal * (2.0 * fromColour + colour) + total * (fromColour + 2.0 * colour)) / (3.0 * (prevTotal + total));
            }
            else
            {
                segmentDepth = total * max(0.0, at - prevAt);
                source = colour;
            }
            float clarity = exp(-segmentDepth);
            // What this step adds to the picture, and so how much its distance and its band's drift
            // count for in `cloudMotion`: the cloud's share of it moves with its band, the fog's not.
            float gained = transmittance * (1.0 - clarity);
            motionWeight += gained;
            motionDistance += gained * at;
            motionGain += gained * (density / total) * _FishCloudLayerC[clamp(layerIndex, 0, FISH_CLOUD_MAX_LAYERS - 1)].x;
            if (debugging && cloudHere)
            {
                float seen = gained * (density / total);
                debugWeight += seen;
                debugLight += seen * depthToSun;
                debugStep += seen * stepHere;
                debugDetail += seen * FishCloudDetailResolved(max(FishCloudDetailCone, FishCloudDetailAlong), _FishCloudLayerB[clamp(layerIndex, 0, FISH_CLOUD_MAX_LAYERS - 1)].y);
            }
            // Energy-conserving integration: what this step scatters, dimmed by what is in front.
            scattered += transmittance * source * (1.0 - clarity);
            transmittance *= clarity;
            lastColour = colour;
            travelled += stepHere;
            prevAt = at;
            prevTotal = total;
            prevDensity = density;
            prevColour = colour;
            prevKnown = true;
            // A step of haze that ended on the base is closed there at the haze's own extinction, so the
            // straight line to the next sample starts from the base — not from deep in the haze, which
            // spread the cloud's extinction back down over the whole long step.
            if (trapezoid && clippedAtBase)
            {
                float closing = exp(-total * max(0.0, travelled - at));
                scattered += transmittance * colour * (1.0 - closing);
                transmittance *= closing;
                prevAt = travelled;
            }
            // The haze under a base, and how high the base is, for the next step (`economy`).
            lastWasHaze = density > 0.0 && high01 < 0.0;
            hazeBase = lastWasHaze ? altitudeHere + spot.baseAbove : hazeBase;
            // The cloud's own extinction, for the next step's limit; the fog has its own (fogCap).
            lastSigma = density;
            emptyDistance = 0.0;
            insideSamples += 1.0;
        }
        else
        {
            // Nothing here. Every branch walks on by the step: nothing is strided over any more.
            // Out of the medium: the segment from the last sample to this one falls to nothing in a
            // straight line, and is closed at half its depth.
            if (trapezoid && prevTotal > 0.0)
            {
                float closing = exp(-0.5 * prevTotal * max(0.0, at - prevAt));
                scattered += transmittance * prevColour * (1.0 - closing);
                transmittance *= closing;
            }
            prevAt = at;
            prevTotal = 0.0;
            prevDensity = 0.0;
            prevKnown = true;
            travelled += stepHere;
            lastEmpty = at;
            lastSigma = 0.0;
            depthIsFresh = false;
            lastWasHaze = false;
            if (inside)
            {
                emptyDistance += stepHere;
                // Out of this cloud: the next one starts from its own near side.
                if (emptyDistance > 2.0 * stepBase)
                {
                    tauInCloud = 0.0;
                }
                // Out the far side and well clear of it: the next cloud is a new edge to find.
                if (at > insideUntil && emptyDistance > fine * 4.0)
                {
                    inside = false;
                    emptyRun = 0.0;
                }
            }
            else if (!fogShell)
            {
                // The empty air of a band, where the steps lengthen (see `growth`). Not the fog's
                // shell, which is walked at the fog's own step, the one that keeps a thin fog seen
                // from above from being stepped clean over.
                emptyRun += stepHere;
            }
        }
    }
    // A ray that ended in the medium — at the world, the far end of the shell, or out of steps — has
    // the rest of its last step still to take, at the last sample's own extinction (as the old way took
    // every step). One stopped by the early exit leaves it to the remainder below.
    if (trapezoid && prevTotal > 0.0 && transmittance >= exitAt)
    {
        float closing = exp(-prevTotal * max(0.0, min(travelled, far) - prevAt));
        scattered += transmittance * prevColour * (1.0 - closing);
        transmittance *= closing;
    }
    // The march stops at a twentieth, so a twentieth is "nothing gets through". Remapped for every
    // ray rather than snapped for the ones that stopped: snapped, the jump from 5% to 0% drew its
    // own faint contour inside the cloud; left alone, that twentieth of the sky — and of the moon —
    // showed through an overcast.
    //
    // And what the remap takes from what lies behind is given to the cloud, as the colour of the
    // last thing sampled. This is the counterpart the remap always lacked, and its lack was the
    // faint rings round the point overhead in a full overcast. Under a uniform deck the samples'
    // phase moves where they fall but not what they add up to: each sample inside counts one full
    // step, so a ray's transmittance after k samples is the same k-fold product whatever its jitter —
    // and the ray stops on the first sample under a twentieth, leaving between e^(−βs)/20 and 1/20
    // of the light behind it uncounted. How much goes by how many steps it took to get there, a whole
    // number that steps with the angle of view: brightness that rose and fell in rings about the
    // zenith, one ring per extra step. No averaging removes it, because the jitter does not change it.
    // Measured on a Python port of this march under a uniform 400 m deck, expected over the
    // jitter at each angle: a ripple of 0.2 % of the deck's brightness at 48 steps, 0.3 % at 72 — near
    // the 8-bit step, and worse since the drops made the deck denser, which puts fewer, heavier steps
    // between the base and a twentieth. With the remainder given back, 0.03 %; and a thick deck is no
    // longer drawn a few per cent too dark, which it was — the twentieth simply went missing.
    // Continuous across the threshold: a ray that ends clear at T = 0.05 gives back 0.05 of its last
    // colour either way, and one that ends at T = 1 gives back nothing.
    // (At the render profile's early exit: 0.05 as shipped.)
    float kept = saturate((transmittance - exitAt) / (1.0 - exitAt));
    scattered += (transmittance - kept) * lastColour;
    cloudMotion = motionWeight > 1e-5 ? float2(motionDistance, motionGain) / motionWeight : float2(0.0, 0.0);
    if (debugging)
    {
        // Drawn opaque (transmittance 0) so the view is not mixed with the world behind it. Where the
        // ray met no cloud the quantity views are a dark blue, told apart from a black shadow.
        int view = (int)(_FishCloudDiag.w + 0.5);
        bool saw = debugWeight > 1e-4;
        float3 none = float3(0.0, 0.0, 0.12);
        float3 shown = float3(0.0, 0.0, 0.0);
        if (view == 1)
        {
            shown = (1.0 - kept).xxx;
        }
        else if (view == 2)
        {
            shown = FishCloudDebugHeat(debugSteps / max(1.0, (float)budget));
        }
        else if (view == 3)
        {
            shown = saw ? exp(-debugLight / debugWeight).xxx : none;
        }
        else if (view == 4)
        {
            shown = saw ? debugDetail / debugWeight : none;
        }
        else if (view == 5)
        {
            // 18 m, the finest step, to a kilometre, on a logarithmic scale.
            shown = saw ? FishCloudDebugHeat(log2(max(1.0, debugStep / debugWeight / 18.0)) / log2(1000.0 / 18.0)) : none;
        }
        else if (view == 6)
        {
            // Red: kept off rock (FishCloudClearance). Green: moved round the terrain (FishFlowAround),
            // full at 500 m. Blue: the flow is built (dim: only the rock is; none: neither).
            float built = _FishCloudFlowRect.w > 1.5 ? 0.35 : (_FishCloudFlowRect.w > 0.5 ? 0.12 : 0.0);
            shown = float3(debugCleared, saturate(debugTurned / 500.0), built);
        }
        FishCloudMarchDebug = float4(shown, 0.0);
    }
    return float4(scattered, kept);
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
    FishCloudReadsTerrain = false;
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
    FishCloudField field = FishCloudFieldAt(origin.xz);
    float2 axis = FishCloudAxis();
    bool storms = _FishWeatherMapParams.z > 0.5;
    int perBand = max(4, steps / max(1, _FishCloudLayerCount));
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        // A regime with no cloud anywhere in view casts no shadow — judged for the whole view, not
        // the camera: it can be clear overhead and solid at the window's edge.
        if (a.z <= 0.002 || a.w <= 0.0)
        {
            continue;
        }
        bool column = _FishCloudLayerF[i].z > 0.5;
        bool ice = (int)(_FishCloudLayerF[i].x + 0.5) == 2;
        // A column is kilometres of band and, most places, a few hundred metres of cloud: spread
        // over the whole band the samples miss the deck entirely, which is the coin-toss shadow all
        // over again. Walk it only from where the base is here to as far up as the cloud gets here.
        float baseHere = a.x;
        float reachTop = a.y;
        if (column)
        {
            baseHere = field.cloudBase;
            float towering = saturate(field.tower * _FishCloudColumn.x * lerp(0.3, 1.0, field.vigour));
            reachTop = lerp(field.cloudTop, field.towerTop, towering);
            reachTop = max(baseHere + 30.0, reachTop);
        }
        /* A storm stands kilometres tall, and unless the light is overhead its cloud is off to the
         * light's side of the ground it shades: a ten-kilometre tower under a sun forty-five degrees
         * up throws its shadow ten kilometres away. So the map is asked twice — over the ground, and
         * where the ray crosses the middle of the storms' heights — and where either finds a storm's
         * cloud (for the column) or its anvil (for the high ice), the walk takes in all of it and asks
         * the map again at every step, where the cloud really is along the ray. Elsewhere the storms
         * are nowhere near this ray and the walk is what it always was. */
        FishCloudField walk = field;
        bool stormy = false;
        if (storms && (column || ice))
        {
            float4 shell = _FishCloudLayerD[i];
            float middle = 0.5 * (shell.y + shell.z);
            FishStorm under = field.storm;
            // Where the storm's cloud is at that height: leaned downwind of its foot like the storm the
            // march draws (FishCloudLean), or its shadow falls where its tower is not.
            FishStorm aloft = FishStormAt(origin.xz + toSun.xz * (max(0.0, middle - origin.y) / toSun.y) - axis * FishCloudLean(middle));
            if (column && max(under.cover, aloft.cover) > 0.002)
            {
                stormy = true;
                bool fromAloft = aloft.cover > under.cover;
                baseHere = min(baseHere, (fromAloft ? aloft.baseM : under.baseM) - FISH_STORM_BASE_RISE);
                reachTop = max(reachTop, fromAloft ? aloft.topM : under.topM);
            }
            else if (ice && max(under.anvil, aloft.anvil) > 0.002)
            {
                stormy = true;
                bool fromAloft = aloft.anvil > under.anvil;
                baseHere = min(baseHere, fromAloft ? aloft.anvilTopM - aloft.anvilDepthM : under.anvilTopM - under.anvilDepthM);
                reachTop = max(reachTop, fromAloft ? aloft.anvilTopM : under.anvilTopM);
            }
        }
        // Where the ground has pushed the band to, here. The walk runs from the base's new height
        // to the top's, or over a mountain it looks for the deck where it would have been.
        float liftedBase = baseHere + FishCloudLift(baseHere + field.ground * 0.6, field.ground) * field.over;
        float liftedTop = reachTop + FishCloudLift(reachTop + field.ground * 0.3, field.ground) * field.over;
        float enter = max(0.0, (liftedBase - origin.y) / toSun.y);
        float leave = (liftedTop - origin.y) / toSun.y;
        if (leave <= enter)
        {
            continue;
        }
        float step = (leave - enter) / perBand;
        UNITY_LOOP
        for (int k = 0; k < perBand; k++)
        {
            float3 at = origin + toSun * (enter + step * (k + jitter));
            if (stormy)
            {
                walk.storm = FishStormAt(at.xz - axis * FishCloudLean(at.y));
            }
            density += FishCloudDensity(at, 0.0, footprint, walk, false, high01) * step;
        }
    }
    return density;
}

#endif
