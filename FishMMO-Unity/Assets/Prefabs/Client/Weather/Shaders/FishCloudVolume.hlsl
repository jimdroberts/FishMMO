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
// x unused, y 1 when the band follows the forecast (0 for the fog band, which follows the fog),
// z 1 when the band is a cloud column, w the thinnest a column's cloud gets (its deck), for the
// stride guard.
float4 _FishCloudLayerF[FISH_CLOUD_MAX_LAYERS];
// How a band turns the sky's cover into its own: x the cover it waits for, y how much of the cover
// reaches it (negative for cirrus, which thins as the sky below fills), z what it has regardless.
// CloudLayer.CoverageFor, exactly, so it can be asked about any place and not only the camera.
float4 _FishCloudLayerG[FISH_CLOUD_MAX_LAYERS];
// The cloud map's tall part. x how much of a tower the air allows, y metres one tile of the tower
// lattice covers, z tiles per period, w the type every column starts from (0.1 deck .. 0.5 heaps).
float4 _FishCloudColumn;
// xy the drift the tower lattice is read through, wrapped to its period. z how hard the mid-scale
// carving is. w unused.
float4 _FishCloudTowerDrift;
int _FishCloudTowerSeed;
// What hangs below a column's base: x ground fog 0..1, y rain 0..1. zw unused.
float4 _FishCloudSub;
// The ground under the sky, twice over. r the mountain as the AIR feels it — the terrain smoothed
// over some 700 m — and gb how fast that rises toward +x and +z (metres a metre): everything that
// bends the cloud field reads these. a the ground itself, barely smoothed, for the fog that lies
// on it. Rect: xy corner, z size (m), w 1 when there is one.
TEXTURE2D(_FishCloudTerrain);
SAMPLER(sampler_FishCloudTerrain);
float4 _FishCloudTerrainRect;
// Where the viewer is. Not _WorldSpaceCameraPos: the shadow cookie is drawn outside any camera, where
// that holds whichever camera happened to render last — the Scene view's, in the editor — so the
// front's slope was laid out from one place for the sky and from another for its shadow.
float4 _FishCloudViewer;
// 1 where the ray ends in open sky, 0 where it ends on the world. Set by the march pass before it
// marches. The ground fog in the volume is for the sky — the band of mist at the horizon, the fog
// that rises into a cloud's base — because the world is already fogged by the pipeline's own fog,
// in its own shaders; drawn over the terrain as well it fogged everything twice, and the second
// coat was a lit one.
static float FishCloudGroundFogScale = 1.0;
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
// the shape volume may be read at. z: one over how much detail the far sky is asked to keep. w: how
// far the clouds are drawn, in metres: they dissolve over the last quarter of it.
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

/// A twin of WeatherDriver.Tower: where, within a sky, the air goes all the way up. Sparse — only the
/// peaks of the lattice stand as towers. The server reads the C# one to put the rain under them.
float FishCloudTower(float2 driftedMetres)
{
    float2 p = driftedMetres / max(1.0, _FishCloudColumn.y);
    float n = FishCloudPeriodicNoise(p, max(1, (int)_FishCloudColumn.z), (uint)_FishCloudTowerSeed);
    return smoothstep(0.0, 1.0, saturate((n - 0.6) / 0.25));
}

/// How far up its band a column of a given type gets, as a share of the band: a flat deck stops
/// within the first tenth, a heaped cumulus at about a third, a tower takes the whole depth. One
/// curve, so the three are the same cloud at different heights and share the base they grow from.
float FishCloudColumnTop(float type)
{
    float low = lerp(0.07, 0.30, saturate(type * 2.0));
    return lerp(low, 1.0, saturate(type * 2.0 - 1.0));
}

// ── The field ──────────────────────────────────────────────────────────

// Height above the ground, in metres, on a curved world. The bands are shells around the planet, so
// they meet the horizon by curving away rather than by running out.
float FishCloudAltitude(float3 position)
{
    float radius = max(1000.0, _FishCloudLayer.z);
    return length(position - float3(0.0, -radius, 0.0)) - radius;
}

/// A band's cover for a given sky cover: CloudLayer.CoverageFor, to the letter.
///
/// The shader used to be handed this function's *slope* at the camera's cover and told to
/// extrapolate: cover here = cover at the camera + slope x (how different this place is). That is
/// a fair likeness where the function is nearly straight and a wild one where it is not — and at
/// the bottom of the range it is a steep gate, nothing to a band's whole share across the first
/// eighth of the cover. With the camera in a formation's gap and the cover just coming up through
/// that gate, the slope was at its limit and everywhere else in the sky was "more formation than
/// here", so the whole sky was extrapolated to more than full cover — and then snapped back the
/// moment the cover left the gate and the slope fell to nothing. Measured on the bed: 5% of the
/// screen solid cloud, then 67%, then 24%, inside a second and a half, with every input rising
/// smoothly throughout. Evaluated outright there is nothing to extrapolate.
float FishCloudCoverageFor(float skyCover, float3 response)
{
    float above = response.x >= 1.0 ? 0.0 : saturate((skyCover - response.x) / (1.0 - response.x));
    float asked = saturate(above * response.y + response.z);
    // A forecast of no cloud means no cloud, whatever a band would keep on its own account.
    return asked * smoothstep(0.0, 0.12, skyCover);
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

/// Whether any cloud can be at this height at all, from the bands' figures alone — no texture, no
/// hash. Most of a long ray is the empty air between a deck and the cirrus, and every sample of it
/// used to pay for the formation lattice, the tower lattice and the weather map before finding out
/// there was no band there to use them.
bool FishCloudPossibleAt(float altitude)
{
    // The tallest a column can stand anywhere this frame: the day's type, a full tower, and a storm
    // if there is one to be had.
    float tallest = FishCloudColumnTop(saturate(_FishCloudColumn.w + _FishCloudColumn.x + 0.6));
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        float4 f = _FishCloudLayerF[i];
        if (a.w <= 0.001 || (a.z <= 0.002 && f.y < 0.5))
        {
            continue;
        }
        bool column = f.z > 0.5;
        // With ground under the sky a band can stand higher than its own top: a kilometre of
        // headroom covers the most any mountain lifts it.
        float top = (column ? a.x + (a.y - a.x) * tallest : a.y) + (_FishCloudTerrainRect.w > 0.5 ? 1000.0 : 0.0);
        float bottom = column && (_FishCloudSub.x >= 0.01 || _FishCloudSub.y >= 0.01) ? 0.0 : a.x;
        if (altitude > bottom && altitude < top)
        {
            return true;
        }
    }
    return false;
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
    float radius = max(1000.0, _FishCloudLayer.z);
    float3 fromCentre = origin - float3(0.0, -radius, 0.0);
    float along = dot(fromCentre, direction);
    float offset = dot(fromCentre, fromCentre);
    float tallest = FishCloudColumnTop(saturate(_FishCloudColumn.w + _FishCloudColumn.x + 0.6));
    float headroom = _FishCloudTerrainRect.w > 0.5 ? 1000.0 : 0.0;
    float next = 1e9;
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        float4 f = _FishCloudLayerF[i];
        if (a.w <= 0.001 || (a.z <= 0.002 && f.y < 0.5))
        {
            continue;
        }
        bool column = f.z > 0.5;
        float top = (column ? a.x + (a.y - a.x) * tallest : a.y) + headroom;
        float bottom = column && (_FishCloudSub.x >= 0.01 || _FishCloudSub.y >= 0.01) ? 0.0 : a.x;
        // Below the band: where the ray climbs through its floor. Above it: where it comes down
        // through its ceiling, if it ever does — a ray that is already curving away never will.
        float shell = radius + (altitude <= bottom ? bottom : top);
        float disc = along * along - (offset - shell * shell);
        if (disc < 0.0)
        {
            continue;
        }
        float root = sqrt(disc);
        float hit = altitude <= bottom ? -along + root : -along - root;
        if (hit > travelled)
        {
            next = min(next, hit);
        }
    }
    return next;
}

// How air meets high ground: x the wind speed over the air's stability (U/N, metres) — the height of
// ground the air has the energy to climb; y how far a blocked flow is turned aside (m).
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
    float2 deflect;     // how far the air here has been turned aside (m, on the ground plane)
};

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
    field.deflect = float2(0.0, 0.0);
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
            float2 across = float2(-wind.y, wind.x);

            // Over, or round: the height this air can climb against the height that is here.
            float froude = max(1.0, _FishCloudFlow.x) / max(50.0, field.ground);
            // Over a wide band of heights, on purpose: where the regime flips is where the turning
            // aside switches on, and switched on across a few hundred metres of slope it folded
            // the cloud lookup into rings. From four tenths to nearly twice the height the air can
            // climb, the change is spread across more than a kilometre of any real slope.
            field.over = smoothstep(0.4, 1.8, froude);

            // Over: rising along the wind is the windward slope, where the cloud gathers; falling
            // along it is the lee, where it clears. A blocked flow still banks a little cloud
            // against the foot of what blocks it.
            float upslope = dot(terrain.gb, wind);
            field.orographic = clamp(upslope * 0.9, -0.12, 0.3) * inside * lerp(0.4, 1.0, field.over);

            // Round: the air is turned aside, away from the high ground, across the wind. The
            // field is read where the air *came from*, so the lookup moves the other way — up the
            // slope, across the wind — and the pattern on screen is carried outward round the
            // flanks. Only the slope across the wind turns the flow; the slope along it is what
            // the air either climbs or does not.
            float sideways = dot(terrain.gb, across);
            field.deflect = across * clamp(sideways, -1.0, 1.0) * _FishCloudFlow.y * (1.0 - field.over) * inside;
        }
    }
    return field;
}

/// `carve` asks for the mid-scale cauliflower: on for what the camera sees and for the first steps
/// toward the sun, where it shapes the self-shadow; off where only a rough depth is wanted.
float FishCloudDensityAt(float3 position, float detailAmount, float footprint, FishCloudField field, bool carve, out float high01, out int layerIndex)
{
    float meso = field.meso;
    high01 = 0.0;
    layerIndex = 0;
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
    // Where the air here came from, on the ground plane: turned aside round high ground it could
    // not climb, straight through otherwise.
    float2 source = position.xz + field.deflect;
    float2 frame = float2(dot(source, wind), dot(source, across));
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
        float4 f = _FishCloudLayerF[i];
        bool column = f.z > 0.5;
        // Below a column's base there is still the column: the fog that rises into it and the
        // scud and rain haze that hang from it. Anything else stops at its own floor.
        bool below = altitude <= a.x;
        if (altitude >= a.y || (below && (!column || (_FishCloudSub.x < 0.01 && _FishCloudSub.y < 0.01))))
        {
            continue;
        }
        float4 b = _FishCloudLayerB[i];
        float4 c = _FishCloudLayerC[i];
        float4 d = _FishCloudLayerD[i];
        float4 e = _FishCloudLayerE[i];
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
        float2 fromViewer = position.xz - _FishCloudViewer.xz;
        // Clamped, because this is a straight line standing in for a curve. It is a good likeness
        // within a few kilometres of the camera and a poor one at the horizon, where the ray runs
        // tens of kilometres out — half a weather system away — and the line has long since left the
        // field behind. Unclamped it drives the far sky to nothing on one side and to solid on the
        // other, which reads as the sky simply ending in one direction.
        float tilt = clamp(dot(d.yz, fromViewer), -0.35, 0.35);
        // The forecast at the camera, the slope of the front, and the formation here — the last two
        // put through this band's own response to a change in the forecast.
        // …and what the ground is doing to the air: only within a couple of kilometres above the
        // terrain, which is as far up as a hill's lift reaches.
        float overGround = trueAltitude - field.surface;
        float lifted01 = field.orographic * saturate(1.0 - (overGround - 300.0) / 1800.0);
        // The sky's cover HERE — the forecast at the camera, the slope of the front, the formation
        // and what the ground does to the air — and then this band's own answer to that cover.
        // A band that does not follow the forecast (the fog band) keeps the figure it was given.
        float skyCoverHere = saturate(_FishCloudLayer.w + tilt + meso + lifted01);
        float coverage = f.y > 0.5 ? FishCloudCoverageFor(skyCoverHere, _FishCloudLayerG[i].xyz) : a.z;
        // How tall this column grows: what the day starts every column at, the tower that stands
        // here if one does, and the storm cell overhead if there is one. A tower also brings its
        // own cloud — the air that goes all the way up is air that is condensing.
        float type = 0.0;
        if (column)
        {
            float towering = field.tower * _FishCloudColumn.x;
            type = saturate(_FishCloudColumn.w + towering + (growsStorms ? storm * 0.6 : 0.0));
            coverage = saturate(coverage + towering * 0.5 * f.y);
        }
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
            float lift = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, lifted, 0).r;
            // Spread over the field's own range. The red channel runs about 0.33 to 0.76, so read
            // raw it never reached 1 and the top of every band went unused: with convection at 1 the
            // tallest column stopped at 83% of the band. Remapped, the tops run from 30% to the
            // band's real top. Kept a spread and never a clamp: clamping the reach gave every high
            // column the very same ceiling and the deck grew a flat table with a hard rim.
            float reach = (lift - 0.33) / 0.43;
            ceiling = lerp(1.0, 0.3 + reach * 0.7, convection);
            if (column)
            {
                // In a column the map says how far up the cloud gets and the lift only roughens
                // it, so neighbouring heaps do not all stop at one level.
                ceiling = FishCloudColumnTop(type) * lerp(1.0, 0.6 + reach * 0.4, convection);
            }
        }
        else if (column)
        {
            ceiling = FishCloudColumnTop(type);
        }

        float h = saturate((altitude - a.x) / max(1.0, a.y - a.x));
        // Dividing by the ceiling raises the top of the profile to where this column reached while
        // leaving h = 0 — the condensation level — exactly where it is. A column's deck stops within
        // a few hundredths of the band, so its floor on the divisor is far lower than a band's.
        float reached = max(column ? 0.03 : 0.15, ceiling);
        float profile = below ? 1.0 : FishCloudBandProfile(h / reached, c.y, c.z);
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
        // Mip 0. The shape used to be read at a mip chosen from how much world a sample stood for,
        // with the mean and spread the mip took put back afterwards. It was built for a sky that no
        // longer exists — a 4 km tile cut hard by an extinction thirty times too high — and with the
        // tile at 12 km, real extinction and the haze, the level it chose came to 0.00 near the
        // camera and 0.26 at the far end of the sky: two fetches, a correction and two knobs to do
        // nothing, and it had cost two bugs on the way (the coverage collapse, the breathing).
        // Only the carve below still takes a mip, because only it has texels finer than a step.
        float4 shape = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, uv, 0);
        // Perlin billowed by the Worley channels: solid cores, cauliflower edges.
        float billow = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
        float floorValue = (1.0 - billow) * 0.45;
        float body = saturate((shape.r - floorValue) / max(0.05, 1.0 - floorValue));
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
        if (below)
        {
            // Under the base, in the same field read further down — so the fog is thickest under
            // the masses and rises into them, and what hangs from a wet tower hangs from that
            // tower. Two things live here. Ground fog: densest at the ground, reaching the base
            // only when it is heavy. And the hang: scud and rain haze, densest just under the base
            // of a tall column that is raining. Both far thinner than cloud — a fog you can see a
            // kilometre through is a fifth of a cloud's extinction.
            // From the ground that is actually there, not from sea level: measured from zero, a
            // plateau three hundred metres up stood above its own fog and a valley floor was the
            // only place that ever had any.
            float under = saturate((trueAltitude - field.surface) / max(1.0, a.x));   // 0 at the ground, 1 a base's height above it
            float mass = saturate((body - threshold + 0.10) / 0.20);
            // Squared. The horizon is a level ray, and a level ray stays inside even a thirty-metre
            // fog layer for eighteen kilometres before the curve of the world lifts it out — so read
            // in proportion, a fog of 0.03, which is a clear night, put a wall of mist right round
            // the horizon (41% fogged at 0.02, 89% at 0.05). A trace of fog is clear air and a real
            // fog is thick: squared, 0.05 fogs the horizon by a tenth, 0.3 closes it, and the
            // thickest fog is exactly as thick as it was. The height it reaches stays in proportion.
            float fog = FishCloudGroundFogScale * _FishCloudSub.x * _FishCloudSub.x * saturate(1.0 - under / max(0.05, _FishCloudSub.x * 1.2)) * (0.55 + 0.45 * mass);
            float hang = _FishCloudSub.y * saturate(type * 1.6) * mass * saturate((under - 0.3) / 0.7);
            density = saturate(fog * 0.2 + hang * 0.25);
            if (density <= 0.0)
            {
                continue;
            }
            density *= a.w;
            if (density > best)
            {
                best = density;
                // Negative marks what is under the base: fog and hang are lit as the air is, not
                // as a cloud is.
                high01 = -1.0;
                layerIndex = i;
            }
            continue;
        }
        if (density <= 0.0)
        {
            continue;
        }
        if (column)
        {
            // Wispy and thin at the base, dense toward the top of what this column reached.
            density *= lerp(0.65, 1.0, saturate(h / reached));
        }
        // The cauliflower: a second, finer read of the same volume's Worley channels, which carves
        // the body back to its billows. It fills the gap there was between a shape texel (tens of
        // metres) and the detail volume, which fades with distance — without it a calibrated cloud
        // is a smooth grey mass with no structure on it at all. Carved by remapping, not by
        // subtracting, so a billow keeps its full density and only the hollows between go; harder
        // toward the top, where a real cloud is most broken up; and sheared by the warp more with
        // height, which draws the upper part out into the streaks a tower has. Eased off as the mip
        // rises: a blurred carve is a uniform one, and would thin the far sky instead of shaping it.
        float carveAmount = _FishCloudTowerDrift.z;
        if (carve && carveAmount > 0.001)
        {
            float hIn = saturate(h / reached);
            // Five times the shape, a whole number on purpose: the band's drift is wrapped every 42
            // tiles, and at 5.3 that was a jump of 222.6 in this lookup — not a whole tile, so the
            // cauliflower snapped to a different pattern at every wrap. Mip 0, like everything else:
            // no read in this file takes a mip any more. What cannot be resolved far off is faded
            // out below by distance, which is what distance does to it anyway.
            float3 midUV = uv * 5.0 + (warp - 0.5) * (0.6 + hIn * 1.8);
            float3 mid = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, midUV, 0).gba;
            float billows = mid.r * 0.625 + mid.g * 0.25 + mid.b * 0.125;
            // Eased with distance, by the footprint, and never by the mip: tied to the mip it kept
            // 42% of its strength beside the camera and 7% across the sky, so the cauliflower was
            // all but switched off everywhere. Full close to, a little under half at the far end.
            float erode = (1.0 - billows) * lerp(0.25, 0.6, hIn) * carveAmount * lerp(1.0, 0.35, saturate((footprint - 25.0) / 20.0));
            density = saturate((density - erode) / max(0.05, 1.0 - erode));
            if (density <= 0.0)
            {
                continue;
            }
        }

        // Detail eats the edges into wisps, more so where the cloud is already thin.
        if (detailAmount > 0.0 && b.z > 0.0)
        {
            // Carried by the same wind as its band, wrapped to its own tile. It used to move at a
            // fixed two and a half times the drift whatever the band's own share was, so the wisps
            // slid through the cloud they were eating — four times faster than the fog band, slower
            // than the cirrus.
            float3 detailUV = float3(source.x - e.z, position.y, source.y - e.w) / max(20.0, b.y) + i;
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
float FishCloudDensity(float3 position, float detailAmount, float footprint, FishCloudField field, bool carve, out float high01)
{
    int layer;
    return FishCloudDensityAt(position, detailAmount, footprint, field, carve, high01, layer);
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
float FishCloudLightDepth(float3 position, float3 toSun, float detailAmount, float footprint, FishCloudField field, float travelled, bool deep)
{
    // Half the steps past eight kilometres: the self-shadow of a cloud that far off is a gradient
    // a few pixels wide, and the light march is most of what a sample costs.
    int steps = (int)max(1.0, _FishCloudLight.x);
    // And half again deep inside, where what the sample adds is dimmed by everything in front.
    // Halved, never skipped: the depth used to be frozen once a ray was seven tenths absorbed and
    // the last value reused, so everything past that point in a cloud was lit with one stale
    // number — and the contour where rays crossed it showed as a pale skirt round a darker core
    // with a line between them.
    if (travelled > 8000.0 || deep)
    {
        steps = max(3, steps / 2);
    }
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
        density += FishCloudDensity(position + offset, detailAmount * 0.5, footprint, field, k < 1, high01) * step * grow;
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
            float deck = _FishCloudLayerF[b].w;
            thinnest = min(thinnest, max(50.0, deck > 0.0 ? deck : band.y - band.x));
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
    // How many samples this ray has spent inside cloud. A big soft cloud no longer ends a ray in a
    // sample or two — at real extinction it has to be walked through — and a ray that has been
    // inside for dozens of steps is resolving nothing the first dozen did not: the step grows.
    float insideSamples = 0.0;
    // Where the ray last came into air that can hold cloud.
    float entered = near;

    UNITY_LOOP
    for (int i = 0; i < budget; i++)
    {
        // Stop once almost nothing more can reach the camera. At real extinction a ray no longer
        // dies on its first sample — it has to be integrated through the cloud — so where it is
        // allowed to stop is what the march costs. A twentieth is below what the composite shows.
        if (transmittance < 0.05)
        {
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
        // Small near the camera, the tier's step across the middle distance, and growing again far
        // out: a cloud a hundred kilometres off is a few pixels of haze and does not want ninety-
        // metre steps.
        float stepBase = max(clamp(travelled * 0.006, fineNear, fine), travelled * 0.004);
        float stepHere = stepBase * (transmittance < 0.5 ? 1.5 : 1.0) * min(2.5, 1.0 + insideSamples * (1.0 / 40.0));
        float altitudeHere = FishCloudAltitude(position);
        if (!FishCloudPossibleAt(altitudeHere))
        {
            // Nothing can be here: go straight to the next place something can, or stop.
            float next = FishCloudNextPossible(origin, direction, travelled, altitudeHere);
            if (next > far)
            {
                break;
            }
            inside = false;
            // A step or so short, so the way in is walked and not landed in the middle of — and by
            // a different amount on every pixel and frame. Landed at one fixed offset from the
            // band's floor, every ray would sample it at the same depths, and samples in step with
            // one another across the screen are what draws rings in a cloud.
            travelled = max(travelled + stepHere, next - stepBase * (0.5 + jitter));
            // Where this stretch of possible cloud was entered. The step back on first finding
            // cloud must never go behind it: see there.
            entered = travelled;
            continue;
        }
        // Scaled by the whole footprint and not just the cone: of the two terms the step is the
        // larger over most of the sky, so a knob that only touched the cone would not be a knob.
        // Half the step, because a sample stands for the half-step either side of it; the cone
        // takes over past about forty kilometres.
        // From the step distance alone sets, never from the multipliers on it: those depend on how
        // far this particular ray has got into the cloud, and a mip that follows them makes the
        // inside of a cloud blurrier than its edge and different from one frame's jitter to the
        // next — which the history then shows as the cloud slowly breathing in and out.
        float footprint = max(stepBase * 0.5, travelled * _FishCloudLodParams.x) * _FishCloudLodParams.z;
        FishCloudField field = FishCloudFieldAt(position.xz);
        float density = FishCloudDensityAt(position, inside ? detailHere : 0.0, footprint, field, true, high01, layerIndex);
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
        float leaving = smoothstep(drawn * 0.7, drawn, travelled);
        density *= 1.0 - leaving;
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
                // Back over the stretch a stride may have jumped — but never back out of the band.
                // Cloud found within a stride's length of a band's floor used to be stepped back
                // from to *below* that floor, into air where nothing can be; the skip there put the
                // ray back at the floor with `inside` cleared, where it found the same cloud, took
                // it for a fresh edge and stepped back again. Round and round until the iterations
                // ran out, and the ray came home with nothing: a hole of sky through the cloud.
                // Whether a ray was caught depended on how far past the floor its first hit fell —
                // on the angle it looked up at and on its jitter — so the holes were speckled rings
                // about the point overhead.
                travelled = max(max(near, entered), travelled - coarse);
                continue;
            }
            bool first = !found;
            if (first)
            {
                cloudDistance = travelled;
                found = true;
            }
            depthToSun = FishCloudLightDepth(position, toSun, detailHere, footprint, field, travelled, transmittance < 0.3);
            // How far this ray has already come through cloud: 0 at the near surface, toward 1
            // deep inside. That is what the powder term reads.
            float intoCloud = 1.0 - transmittance;
            float scatter = FishCloudScatter(depthToSun, cosAngle, _FishCloudLight.z, intoCloud);
            bool underBase = high01 < 0.0;
            high01 = max(0.0, high01);
            float3 colour = FishCloudColour(sunColour, scatter, high01, layerIndex, rain, ambient);
            if (underBase)
            {
                // What hangs under a cloud's base is the scene's fog, and takes the scene's fog
                // colour — the one the terrain is already being fogged with, so the mist over the
                // horizon and the mist over the hills are one mist. Shaded as cloud it took the
                // sun's or the moon's full light times the cloud gain: a glowing band round the
                // horizon at midnight, and a bright veil over the ground. A little of the light
                // still gets in, which is what makes a sunlit mist pale.
                // The far sky's own colour, which SkySystem works out every frame from the horizon
                // and the fog together — not unity_FogColor, which means nothing at all unless a
                // region has switched fog on and is otherwise whatever the scene was authored with
                // (Unity's default is a mid grey, which at midnight is a pale band round the whole
                // horizon). Taking the haze colour also means the mist at the horizon and the far
                // sky behind it are the same colour, so there is no line where one ends.
                colour = _FishCloudHaze.rgb + sunColour * scatter * 0.12;
            }
            colour += _FishWeatherCloud.w * float3(0.85, 0.9, 1.0) * 2.0;   // lightning lights the volume
            // Distance turns a cloud into the air in front of it: the far side of a sky is haze,
            // not white, and without this every bank reads as though it were a mile away.
            // Full haze by four fifths of the draw distance whatever the haze distance says, so a
            // cloud is always the horizon's colour before it starts to dissolve.
            float haze = saturate(travelled / max(1.0, min(_FishCloudHaze.a, drawn * 0.8)));
            // Not the whole way — a far cloud loses its contrast to the air, not its shape, and
            // taken all the way every bank past the haze distance became one flat band — until it
            // is actually leaving: through the dissolve it goes the rest of the way to the horizon's
            // colour, so what thins out is already invisible against the sky behind it.
            colour = lerp(colour, _FishCloudHaze.rgb, max(haze * haze * 0.86, leaving));
            colour = FishCloudShoulder(colour);
            float clarity = exp(-density * stepHere);
            // Energy-conserving integration: what this step scatters, dimmed by what is in front.
            scattered += transmittance * colour * (1.0 - clarity);
            transmittance *= clarity;
            travelled += stepHere;
            emptyDistance = 0.0;
            insideSamples += 1.0;
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
    // The march stops at a twentieth, so a twentieth is "nothing gets through". Remapped for every
    // ray rather than snapped for the ones that stopped: snapped, the jump from 5% to 0% drew its
    // own faint contour inside the cloud; left alone, that twentieth of the sky — and of the moon —
    // showed through an overcast.
    return float4(scattered, saturate((transmittance - 0.05) / 0.95));
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
    FishCloudField field = FishCloudFieldAt(origin.xz);
    float stormHere = saturate(max(_FishCloudTypeParams.y, FishWeatherMapAt(origin.xz).a));
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
        // A column is seven kilometres of band and, most places, a few hundred metres of cloud:
        // spread over the whole band the samples miss the deck entirely, which is the coin-toss
        // shadow all over again. Walk it only as far up as the column gets here.
        float reachTop = a.y;
        if (_FishCloudLayerF[i].z > 0.5)
        {
            float type = saturate(_FishCloudColumn.w + field.tower * _FishCloudColumn.x + stormHere * 0.6);
            reachTop = a.x + (a.y - a.x) * min(1.0, FishCloudColumnTop(type) * 1.1);
        }
        // Where the ground has pushed the band to, here. The walk runs from the base's new height
        // to the top's, or over a mountain it looks for the deck where it would have been.
        float liftedBase = a.x + FishCloudLift(a.x + field.ground * 0.6, field.ground) * field.over;
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
            density += FishCloudDensity(origin + toSun * (enter + step * (k + jitter)), 0.0, footprint, field, false, high01) * step;
        }
    }
    return density;
}

#endif
