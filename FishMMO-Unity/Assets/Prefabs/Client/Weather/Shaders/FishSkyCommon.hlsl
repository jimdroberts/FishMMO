#ifndef FISHMMO_SKY_COMMON_INCLUDED
#define FISHMMO_SKY_COMMON_INCLUDED

// Sky state, set every frame by FishMMO.Client.SkySystem (the only writer).
#include "FishWeather.hlsl"

float4 _FishSkyZenith;      // rgb
float4 _FishSkyHorizon;     // rgb
float4 _FishSkyGround;      // rgb
float4 _FishSkyFogColor;    // rgb, a = how much the horizon melts into fog
float4 _FishSkyParams;      // x star visibility, y milky way, z twinkle, w exposure
float4 _FishSunShape;       // x unused (the halo is SunHalo, in the sun colour's alpha), y disc, z horizon glow
float4 _FishSkyEclipseBody;  // xyz direction to the body covering the sun, w its angular radius (rad)
float4 _FishSkyEclipse;     // x how dark the solar eclipse looks 0..1 (the adapted eye), y lunar eclipse: share of the moon in the umbra, z airless (1 = black sky), w totality 0..1
float4 _FishSkyEclipseCover; // x share of the sun's area covered, y magnitude (of its diameter), z phase (0 none, 1 partial, 2 annular, 3 total), w how much air the covering body has (0 none .. ~3 thick)
float4 _FishLunarShadow;    // xyz direction to the centre of the planet's shadow at the moon, w the umbra's angular radius (rad)
float4 _FishLunarShadowEdge; // x the penumbra's angular radius (rad), y 1 while a moon is in the shadow at all
float4 _FishSunDir[4];      // xyz direction, w drawn angular radius (radians)
float4 _FishSunColor[4];    // rgb colour × intensity, a halo strength
float  _FishSunCount;
float4x4 _FishStarMatrix;   // scene direction → star space
// The ring of the world stood on. It lies in that world's equator, so it is worked out in the
// world's own equatorial frame (z the pole), in units of the world's radius.
float4x4 _FishRingMatrix;   // scene direction → the observer's equatorial frame
float4 _FishOwnRing;        // x inner rim, y outer rim (body radii), z bands, w how solid — 0 when there is no ring
float4 _FishOwnRingTint;    // rgb the ring material
float4 _FishOwnRingZenith;  // xyz where the observer stands (unit: straight up, in that frame), w 1 with a texture
float4 _FishOwnRingSun;     // xyz the direction to the sun, in that frame, w the world's radius in thousands of km
// The discs in the sky that hide what is behind them, nearest last.
float4 _FishOccluders[32];      // xyz direction, w drawn angular radius (rad)
float4 _FishOccluderRanks[32];  // x rank in the far-to-near order
float4 _FishOccluderRings[32];  // xyz the rings' pole (scene), w outer rim (rad); zero without rings
float4 _FishOccluderRingShapes[32]; // x inner rim over outer, y bands, z opacity, w 1 with rings
float _FishOccluderCount;
TEXTURE2D(_FishOwnRingTex);
SAMPLER(sampler_FishOwnRingTex);
float4 _FishCloudLit;       // rgb
float4 _FishCloudShadow;    // rgb
float4 _FishAuroraParams;   // x strength, y time
float4 _FishAuroraA;
float4 _FishAuroraB;
float4 _FishRainbow;        // x strength
// The storms, from WeatherMap: the cloud each one makes, laid out by its anatomy, on two cascades — a
// fine one of 12 km round the viewer and a coarse one of 112 km. See FishStormAt below for what each
// channel holds.
float4 _FishWeatherMapRect;    // the fine cascade: xy corner (world x, z), zw size in metres
float4 _FishWeatherMapFarRect; // the coarse cascade: the same
// x 1 when the fine cascade is there, y 1 when the coarse one is, z 1 when either holds any storm at
// all (neither is read otherwise), w what an anvil's ice takes out of the light (1/m).
float4 _FishWeatherMapParams;

TEXTURECUBE(_FishStarCube);
SAMPLER(sampler_FishStarCube);
// The sky's own galaxy, when one is supplied. It lives in the same frame as the stars, so it
// turns with them. _FishGalaxyParams.x says whether it is real: with none supplied the sky
// draws its own Milky Way band instead, and this must not be sampled — an unbound cubemap
// reads as black and would lay a dark patch across the night sky where the galaxy should be.
TEXTURECUBE(_FishGalaxyCube);
SAMPLER(sampler_FishGalaxyCube);
float4 _FishGalaxyParams;
TEXTURE2D(_FishWeatherNoise);
SAMPLER(sampler_FishWeatherNoise);
TEXTURE2D(_FishWeatherMap);
SAMPLER(sampler_FishWeatherMap);
TEXTURE2D(_FishWeatherMapB);
TEXTURE2D(_FishWeatherMapFar);
TEXTURE2D(_FishWeatherMapFarB);

float FishNoise(float2 uv, int channel)
{
    float4 n = SAMPLE_TEXTURE2D_LOD(_FishWeatherNoise, sampler_FishWeatherNoise, uv, 0);
    return channel == 0 ? n.r : channel == 1 ? n.g : n.b;
}

// ── The storms ────────────────────────────────────────────────────────
// Both cascades carry the same two textures (WeatherMap, which must agree with this):
//   A  r the storm's own cloud, 0..1 — its cumulonimbus, and the lowerings under and ahead of it
//        (a supercell's wall cloud, a squall line's shelf)
//      g its anvil, 0..1 of the anvil's full depth: thick over the storm, nothing at its edge
//      b clearing, 0..1: sinking air (a hurricane's eye) dissolves whatever cloud the air makes
//      a how much of a storm it is, 0..1: more water in its cloud
//   B  r the storm's base above the ground (m) × A.r: flat under the tower, lowered under the wall
//        cloud and the shelf
//      g how high its cloud reaches (m) × A.r: its tower, domed over the updraught
//      b the anvil's flat top (m) × A.g
//      a how thick the anvil is here (m)
// The heights are multiplied by the share they belong to so that the texture filter, and the blend
// between the cascades, weigh them honestly across a storm's edge: a texel of storm next to a texel of
// clear air filters to half a storm AT THE STORM'S BASE, not to a whole storm at half its base.

// What a storm is at one place, unpacked.
struct FishStorm
{
    float cover;        // how solid the storm's own cloud is here, 0..1
    float anvil;        // how much of its anvil is here, 0..1
    float clearing;     // how much of any cloud sinking air dissolves here, 0..1
    float strength;     // how much of a storm it is here, 0..1
    float baseM;        // its cloud base here (m): where cover is above 0
    float topM;         // how high its cloud reaches here (m)
    float anvilTopM;    // the anvil's flat top (m): where anvil is above 0
    float anvilDepthM;  // how thick the anvil is here (m)
};

// How much of each cascade is read at a place: x the fine one, handed over to the coarse one across a
// ring before its edge; y the coarse one, faded out across a ring before its own. Circles round each
// map's middle, not its square: a square's edge is a straight line, and a straight line across the
// sky is the one thing an eye finds at once. Twins: WeatherMap.NearWeight and FarWeight.
float2 FishWeatherMapWeights(float2 xz)
{
    float2 w = float2(0.0, 0.0);
    if (_FishWeatherMapParams.x > 0.5)
    {
        float r = length(xz - (_FishWeatherMapRect.xy + 0.5 * _FishWeatherMapRect.zw)) / max(1.0, 0.5 * _FishWeatherMapRect.z);
        w.x = 1.0 - smoothstep(0.6, 0.9, r);
    }
    if (_FishWeatherMapParams.y > 0.5)
    {
        float r = length(xz - (_FishWeatherMapFarRect.xy + 0.5 * _FishWeatherMapFarRect.zw)) / max(1.0, 0.5 * _FishWeatherMapFarRect.z);
        w.y = 1.0 - smoothstep(0.8, 0.97, r);
    }
    return w;
}

// The two texels at a place, the cascades blended: the coarse one where the fine one does not reach,
// the fine one where it does, and between them both. Each cascade is only read where it is used, and
// its heights only where it has a storm's cloud or anvil to give them to: most of a stormy sky is
// still clear of any storm, and this is read for every sample the cloud march takes.
void FishWeatherMapTexels(float2 xz, bool heights, out float4 a, out float4 b)
{
    a = float4(0.0, 0.0, 0.0, 0.0);
    b = float4(0.0, 0.0, 0.0, 0.0);
    if (_FishWeatherMapParams.z < 0.5)
    {
        return;
    }
    float2 w = FishWeatherMapWeights(xz);
    if (w.x < 1.0 && w.y > 0.0)
    {
        float2 uv = saturate((xz - _FishWeatherMapFarRect.xy) / max(_FishWeatherMapFarRect.zw, 1.0));
        float4 farA = SAMPLE_TEXTURE2D_LOD(_FishWeatherMapFar, sampler_FishWeatherMap, uv, 0);
        a = farA * w.y;
        if (heights && farA.r + farA.g > 0.0)
        {
            b = SAMPLE_TEXTURE2D_LOD(_FishWeatherMapFarB, sampler_FishWeatherMap, uv, 0) * w.y;
        }
    }
    if (w.x > 0.0)
    {
        float2 uv = saturate((xz - _FishWeatherMapRect.xy) / max(_FishWeatherMapRect.zw, 1.0));
        float4 nearA = SAMPLE_TEXTURE2D_LOD(_FishWeatherMap, sampler_FishWeatherMap, uv, 0);
        a = lerp(a, nearA, w.x);
        float4 nearB = float4(0.0, 0.0, 0.0, 0.0);
        if (heights && nearA.r + nearA.g > 0.0)
        {
            nearB = SAMPLE_TEXTURE2D_LOD(_FishWeatherMapB, sampler_FishWeatherMap, uv, 0);
        }
        b = lerp(b, nearB, w.x);
    }
}

// The storms at a world position.
FishStorm FishStormAt(float2 xz)
{
    float4 a, b;
    FishWeatherMapTexels(xz, true, a, b);
    FishStorm storm;
    storm.cover = saturate(a.r);
    storm.anvil = saturate(a.g);
    storm.clearing = saturate(a.b);
    storm.strength = saturate(a.a);
    storm.baseM = a.r > 1e-4 ? b.r / a.r : 0.0;
    storm.topM = a.r > 1e-4 ? b.g / a.r : 0.0;
    storm.anvilTopM = a.g > 1e-4 ? b.b / a.g : 0.0;
    storm.anvilDepthM = max(0.0, b.a);
    return storm;
}

// Only the first texture: r the storm's own cloud, g its anvil, b clearing, a strength. Half the reads,
// for what needs to know where a storm's cloud is and not how high.
float4 FishWeatherMapAt(float2 xz)
{
    float4 a, b;
    FishWeatherMapTexels(xz, false, a, b);
    return a;
}

// ── Rings ─────────────────────────────────────────────────────────────
// How solid a ring is across its width: t is 0 at the inner rim and 1 at the outer. Shared by the
// rings of a body in the sky and by the ring of the world stood on, so they are the same rings.
// Each band has a weight of its own and a gap between it and the next; one band is one unbroken
// ring. Nothing here depends on the angle round the ring: a ring is the same all the way round.
float FishRingBands(float t, float bands)
{
    if (t <= 0.0 || t >= 1.0)
    {
        return 0.0;
    }
    float rim = smoothstep(0.0, 0.04, t) * (1.0 - smoothstep(0.96, 1.0, t));
    float count = max(1.0, floor(bands + 0.5));
    float cell = t * count;
    float index = floor(cell);
    float within = frac(cell);
    float weight = 0.45 + 0.55 * frac(sin(index * 12.9898 + count * 78.233) * 43758.5453);
    float gap = count > 1.5 ? smoothstep(0.0, 0.12, within) * (1.0 - smoothstep(0.88, 1.0, within)) : 1.0;
    return rim * weight * gap;
}

// How much of a direction the ring of the world underfoot covers, 0..1, and the light coming off
// the ring there.
//
// The ring lies in that world's equator, so from the ground it is an arc along the celestial
// equator: high and thin from near the equator, low and broad toward the poles, below the horizon
// from the pole itself. A ray of the sky is followed out to the ring plane and asked whether it
// lands between the rims.
//
// One function, because two things need the answer. The sky draws the ring with it. And the ring
// is the NEAREST thing in the sky — a few of the world's own radii off, where every moon and
// planet is thousands — so whatever else is drawn has to ask it too, and step behind. Drawn by the
// sky alone it was the furthest-back thing there is, with every body in the sky painted over it.
float FishOwnRingAt(float3 dir, out float3 ringLight, out float reachRadii)
{
    ringLight = float3(0.0, 0.0, 0.0);
    reachRadii = 1e9;
    if (_FishOwnRing.w <= 0.001 || dir.y <= 0.0)
    {
        return 0.0;
    }
    float3 ray = mul((float3x3)_FishRingMatrix, dir);
    float3 stand = _FishOwnRingZenith.xyz;
    if (abs(ray.z) <= 1e-5)
    {
        return 0.0;
    }
    float reach = -stand.z / ray.z;
    if (reach <= 0.0)
    {
        return 0.0;
    }
    float3 hit = stand + ray * reach;
    reachRadii = reach;
    float t = (length(hit.xy) - _FishOwnRing.x) / max(1e-4, _FishOwnRing.y - _FishOwnRing.x);
    float solid;
    float3 material = _FishOwnRingTint.rgb;
    if (_FishOwnRingZenith.w > 0.5)
    {
        // A strip read across the rings: inner rim on the left, outer on the right.
        float4 strip = SAMPLE_TEXTURE2D_LOD(_FishOwnRingTex, sampler_FishOwnRingTex, float2(saturate(t), 0.5), 0);
        material *= strip.rgb;
        solid = strip.a * step(0.0, t) * step(t, 1.0);
    }
    else
    {
        solid = FishRingBands(t, _FishOwnRing.z);
    }
    // The world's own shadow lies across its rings on the side away from the sun: at night the arc
    // is lit to either side and dark through the middle — and still hides what is behind it there.
    float3 toSun = _FishOwnRingSun.xyz;
    float sunward = dot(hit, toSun);
    float3 aside = hit - toSun * sunward;
    float lit = (sunward < 0.0 && dot(aside, aside) < 1.0) ? 0.03 : 1.0;
    ringLight = material * lit * 0.85;
    return solid * _FishOwnRing.w * saturate(dir.y * 25.0);
}

float FishOwnRing(float3 dir, out float3 ringLight)
{
    float unused;
    return FishOwnRingAt(dir, ringLight, unused);
}

// How much a body's RINGS cover a direction, 0..1: the annulus in the plane of the body's equator,
// seen from here as an ellipse, banded the way the ring quad draws it.
float FishRingCover(float3 dir, float3 bodyDir, float4 ring, float4 shape)
{
    if (shape.w < 0.5)
    {
        return 0.0;
    }
    float3 pole = normalize(ring.xyz);
    float outer = max(1e-5, ring.w);
    float3 across = pole - bodyDir * dot(pole, bodyDir);
    float3 ringUp = dot(across, across) > 1e-6 ? normalize(across) : normalize(cross(bodyDir, abs(bodyDir.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0)));
    float3 ringRight = normalize(cross(ringUp, bodyDir));
    float2 c = float2(dot(dir, ringRight), dot(dir, ringUp)) / outer;
    float squash = max(abs(dot(pole, -bodyDir)), 0.015);
    float onPlane = length(float2(c.x, c.y / squash));
    float t = (onPlane - shape.x) / max(1e-4, 1.0 - shape.x);
    return FishRingBands(t, shape.y) * shape.z;
}


#endif
