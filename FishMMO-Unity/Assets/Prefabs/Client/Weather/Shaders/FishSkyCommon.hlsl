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
float4 _FishSkyEclipse;     // x solar eclipse 0..1, y lunar eclipse 0..1, z airless (1 = black sky), w the covering body's angular radius (rad)
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
float4 _FishOwnRingSun;     // xyz the direction to the sun, in that frame
// The discs in the sky that hide what is behind them, nearest last.
float4 _FishOccluders[32];      // xyz direction, w drawn angular radius (rad)
float4 _FishOccluderRanks[32];  // x rank in the far-to-near order
float _FishOccluderCount;
TEXTURE2D(_FishOwnRingTex);
SAMPLER(sampler_FishOwnRingTex);
float4 _FishCloudLit;       // rgb
float4 _FishCloudShadow;    // rgb
float4 _FishCloudParams;    // x pattern scale, y cirrus, z time (s), w wind drift scale
float4 _FishAuroraParams;   // x strength, y time
float4 _FishAuroraA;
float4 _FishAuroraB;
float4 _FishRainbow;        // x strength
float4 _FishWeatherMapRect; // xy corner (world x, z), zw size in metres
float4 _FishWeatherMapParams; // x 1 when valid

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

float FishNoise(float2 uv, int channel)
{
    float4 n = SAMPLE_TEXTURE2D_LOD(_FishWeatherNoise, sampler_FishWeatherNoise, uv, 0);
    return channel == 0 ? n.r : channel == 1 ? n.g : n.b;
}

// The weather map at a world position: r cloud cover, g precipitation, b snow share, a storm.
float4 FishWeatherMapAt(float2 xz)
{
    float2 uv = (xz - _FishWeatherMapRect.xy) / max(_FishWeatherMapRect.zw, 1.0);
    if (_FishWeatherMapParams.x < 0.5)
    {
        return float4(0, 0, 0, 0);
    }
    // The map only covers a square around the viewer. Fade it out over the outer tenth, or its
    // edge draws a straight line across the sky where what it carries stops.
    float2 toEdge = min(uv, 1.0 - uv);
    float fade = saturate(min(toEdge.x, toEdge.y) / 0.1);
    if (fade <= 0.0)
    {
        return float4(0, 0, 0, 0);
    }
    return SAMPLE_TEXTURE2D_LOD(_FishWeatherMap, sampler_FishWeatherMap, saturate(uv), 0) * fade;
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
float FishOwnRing(float3 dir, out float3 ringLight)
{
    ringLight = float3(0.0, 0.0, 0.0);
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


#endif
