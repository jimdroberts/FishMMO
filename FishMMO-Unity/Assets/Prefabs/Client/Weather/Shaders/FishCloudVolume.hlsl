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
float4 _FishCloudLayerD[FISH_CLOUD_MAX_LAYERS];  // x convection: how much the height a column reaches varies, y/z/w spare
float4 _FishCloudLayerTint[FISH_CLOUD_MAX_LAYERS]; // rgb what a shaded part of this band tends toward
int _FishCloudLayerCount;

float4 _FishCloudLayer;      // x lowest bottom (m), y highest top (m), z planet radius (m), w background coverage
float4 _FishCloudShapeParams;// x detail fade start (m), y detail fade range (m), z unused, w overall density multiplier
float4 _FishCloudWind;       // xy accumulated drift (m), z detail wind gain, w drift multiplier
float4 _FishCloudWindDir;    // xy unit wind direction (the axis noise is stretched along), z speed (m/s), w unused
float4 _FishCloudLight;      // x light steps, y powder, z forward scatter (g), w unused
float4 _FishCloudTypeParams; // x stratus..cumulus mix, y storm 0..1, z unused, w rain 0..1
float4 _FishCloudSunDir;     // xyz direction to whatever lights the clouds now, w its strength
float4 _FishCloudSunColor;   // rgb its colour
float4 _FishCloudAmbient;    // rgb the skylight a cloud sits in, already scaled
float4 _FishCloudTint;       // rgb what a shaded underside takes its colour from, a how much of it
float4 _FishCloudCoverage;   // x cut at no cover, y edge softness, z cut at full cover, w how hard it bends at the end
float4 _FishCloudHaze;       // rgb the colour distance turns a cloud, a metres over which it does
float4 _FishCloudShadowArea;   // xy unused, z window size (m), w steps
float4 _FishCloudShadowOrigin; // xyz the world point at the middle of the window
float4 _FishCloudShadowRight;  // xyz the light's right, across the window
float4 _FishCloudShadowUp;     // xyz the light's up, up the window

// ── The field ──────────────────────────────────────────────────────────

// Height above the ground, in metres, on a curved world. The bands are shells around the planet, so
// they meet the horizon by curving away rather than by running out.
float FishCloudAltitude(float3 position)
{
    float radius = max(1000.0, _FishCloudLayer.z);
    return length(position - float3(0.0, -radius, 0.0)) - radius;
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
float FishCloudDensityAt(float3 position, float detailAmount, out float high01, out int layerIndex)
{
    high01 = 0.0;
    layerIndex = 0;
    float altitude = FishCloudAltitude(position);
    if (altitude <= _FishCloudLayer.x || altitude >= _FishCloudLayer.y)
    {
        return 0.0;
    }

    float2 drift = _FishCloudWind.xy * _FishCloudWind.w;
    // The axis the noise is drawn out along. This must come from the wind's *direction* and never
    // from the drift, which is the wind integrated over time: normalising the drift gave an axis
    // that swung as the drift built up and swung faster the harder the wind blew, and because the
    // axis rotates the whole field about the world origin, a camera kilometres out saw the entire
    // sky sweep past — clouds circling the scene, and a different slice of the noise on screen
    // every time the wind changed, which read as the wind changing how much cloud there was.
    // A zero here would collapse the sampling frame and with it the whole field, so an unset
    // global falls back to due north rather than to a sky of one flat colour.
    float2 wind = dot(_FishCloudWindDir.xy, _FishCloudWindDir.xy) > 1e-6 ? _FishCloudWindDir.xy : float2(0.0, 1.0);
    // The storm cells the server sends, which are the only thing that varies from place to place
    // other than the noise itself.
    float4 weather = FishWeatherMapAt(position.xz);
    float storm = saturate(max(_FishCloudTypeParams.y, weather.a));

    float best = 0.0;
    UNITY_LOOP
    for (int i = 0; i < _FishCloudLayerCount; i++)
    {
        float4 a = _FishCloudLayerA[i];
        if (altitude <= a.x || altitude >= a.y || a.z <= 0.002)
        {
            continue;
        }
        float4 b = _FishCloudLayerB[i];
        float4 c = _FishCloudLayerC[i];

        // This band's own slice of the noise, blown along by its own share of the wind. Drawing the
        // along-wind axis out is what turns the same noise into the streaks of cirrus.
        float2 flat = position.xz + drift * c.x;

        // A cloud's base is flat because air rising off the ground cools as it expands and reaches
        // its dew point at one height across the whole region — the band's floor is that level, and
        // it is the same everywhere. Its top is lumpy because each parcel of air keeps rising until
        // it runs out of buoyancy or moisture, and no two stop in the same place. So the floor is
        // fixed and the ceiling is a broad noise on the ground plane: level underneath, cauliflower
        // on top, which is the shape convection actually makes.
        float convection = _FishCloudLayerD[i].x;
        float ceiling = 1.0;
        if (convection > 0.001)
        {
            float3 lifted = float3(flat / max(1.0, b.x * 1.7), 0.31 + i * 0.11);
            float lift = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, lifted, 0).r;
            // No saturate here. Clamping the reach at 1 gave every column whose lift ran high the
            // very same ceiling — the band's own top — so they all levelled off at one altitude and
            // the deck grew a flat table with a hard rim, which is what it looked like from above.
            // The reach has to stay a spread, never a clamp.
            ceiling = lerp(1.0, 0.3 + lift * 0.7, convection);
        }

        float h = saturate((altitude - a.x) / max(1.0, a.y - a.x));
        // Dividing by the ceiling raises the top of the profile to where this column reached while
        // leaving h = 0 — the condensation level — exactly where it is.
        float profile = FishCloudBandProfile(h / max(0.15, ceiling), c.y, c.z);
        if (profile <= 0.0)
        {
            continue;
        }
        float2 along = float2(dot(flat, wind), dot(flat, float2(-wind.y, wind.x)));
        along.x /= max(1.0, b.w);
        float3 uv = float3(along.x, altitude * 0.6, along.y) / max(1.0, b.x) + i * 0.37;
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
        float3 warpUV = float3(along.x, altitude * 0.25, along.y) / max(1.0, b.x * 4.3) + i * 0.19;
        float3 warp = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, warpUV, 0).rgb;
        uv += (warp - 0.5) * 0.35;
        float4 shape = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, uv, 0);
        // Perlin billowed by the Worley channels: solid cores, cauliflower edges.
        float billow = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
        float floorValue = (1.0 - billow) * 0.45;
        float body = saturate((shape.r - floorValue) / max(0.05, 1.0 - floorValue));

        // Coverage: a storm makes its own cloud wherever the cell stands, whatever the day is doing.
        float coverage = saturate(a.z);
        if (storm > 0.01 && c.w > 0.5)
        {
            coverage = max(coverage, storm);
        }
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
            float3 detailUV = (position + float3(drift.x, 0.0, drift.y) * _FishCloudWind.z) / max(20.0, b.y) + i;
            float3 detail = SAMPLE_TEXTURE3D_LOD(_FishCloudDetail, sampler_FishCloudDetail, detailUV, 0).rgb;
            float wisp = detail.r * 0.625 + detail.g * 0.25 + detail.b * 0.125;
            // Wispy underneath, billowy on top.
            wisp = lerp(1.0 - wisp, wisp, saturate(h * 2.0));
            density = saturate(density - wisp * b.z * detailAmount * (1.0 - density));
        }

        density *= a.w * (1.0 + c.w * storm * 0.8);
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
    return best * _FishCloudShapeParams.w;
}

// The same, for callers that do not care which band answered.
float FishCloudDensity(float3 position, float detailAmount, out float high01)
{
    int layer;
    return FishCloudDensityAt(position, detailAmount, high01, layer);
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
float FishCloudLightDepth(float3 position, float3 toSun, float detailAmount)
{
    int steps = (int)max(1.0, _FishCloudLight.x);
    float step = 120.0;
    float density = 0.0;
    float high01;
    UNITY_LOOP
    for (int k = 0; k < steps; k++)
    {
        float grow = 1.0 + k * 0.6;
        float reach = step * (k + 1) * grow;
        // Cone sampling: each step wanders a little, so the shading is soft instead of banded.
        float3 offset = toSun * reach + float3(0.7, 0.3, -0.5) * reach * 0.2;
        density += FishCloudDensity(position + offset, detailAmount * 0.5, high01) * step * grow;
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
    float3 lit = sunColour * scatter * 9.0 * (1.0 - rain * 0.45);
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
    float fine = clamp(distance / steps, 18.0, 90.0);
    // A stride must never be able to step over a whole band, or the pixels whose stride happens to
    // straddle a thin deck find nothing while their neighbours find cloud — which is a crosshatch
    // across the sky, not noise, and no amount of temporal averaging removes it.
    float thinnest = 1e6;
    UNITY_LOOP
    for (int b = 0; b < _FishCloudLayerCount; b++)
    {
        float4 band = _FishCloudLayerA[b];
        if (band.z > 0.001 && band.w > 0.001)
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
    int budget = steps * 3;

    UNITY_LOOP
    for (int i = 0; i < budget; i++)
    {
        if (transmittance < 0.01 || travelled >= far)
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
        float density = FishCloudDensityAt(position, inside ? detailHere : 0.0, high01, layerIndex);
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
            if (!found)
            {
                cloudDistance = travelled;
                found = true;
            }
            float depthToSun = FishCloudLightDepth(position, toSun, detailHere);
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
            float clarity = exp(-density * fine);
            // Energy-conserving integration: what this step scatters, dimmed by what is in front.
            scattered += transmittance * colour * (1.0 - clarity);
            transmittance *= clarity;
            travelled += fine;
            emptyDistance = 0.0;
        }
        else if (inside)
        {
            emptyDistance += fine;
            travelled += fine;
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
float FishCloudShadowDepth(float3 origin, float3 toSun, int steps, float jitter)
{
    float near, far;
    if (!FishCloudRange(origin, toSun, near, far))
    {
        return 0.0;
    }
    near = max(near, 0.0);
    float step = (far - near) / max(2, steps);
    float density = 0.0;
    float high01;
    UNITY_LOOP
    for (int k = 0; k < steps; k++)
    {
        density += FishCloudDensity(origin + toSun * (near + step * (k + jitter)), 0.0, high01) * step;
    }
    return density;
}

#endif
