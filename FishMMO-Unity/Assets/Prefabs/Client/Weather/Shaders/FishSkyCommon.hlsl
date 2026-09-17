#ifndef FISHMMO_SKY_COMMON_INCLUDED
#define FISHMMO_SKY_COMMON_INCLUDED

// Sky state, set every frame by FishMMO.Client.SkySystem (the only writer).
#include "FishWeather.hlsl"

float4 _FishSkyZenith;      // rgb
float4 _FishSkyHorizon;     // rgb
float4 _FishSkyGround;      // rgb
float4 _FishSkyFogColor;    // rgb, a = how much the horizon melts into fog
float4 _FishSkyParams;      // x star visibility, y milky way, z twinkle, w exposure
float4 _FishSkyEclipse;     // x solar eclipse 0..1, y lunar eclipse 0..1, z airless (1 = black sky)
float4 _FishSunDir[4];      // xyz direction, w drawn angular radius (radians)
float4 _FishSunColor[4];    // rgb colour × intensity, a halo strength
float  _FishSunCount;
float4x4 _FishStarMatrix;   // scene direction → star space
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
    if (_FishWeatherMapParams.x < 0.5 || any(uv < 0.0) || any(uv > 1.0))
    {
        return float4(0, 0, 0, 0);
    }
    return SAMPLE_TEXTURE2D_LOD(_FishWeatherMap, sampler_FishWeatherMap, uv, 0);
}

// Clouds along a view direction: rgb colour (premultiplied), a coverage 0..1.
// Three layers on a curved dome: low (weather-driven cumulus/stratus), mid (altocumulus)
// and high cirrus. Pure 2D, so it costs the same on every tier and platform.
float4 FishSkyClouds(float3 dir, float3 cameraPos)
{
    if (dir.y <= 0.002)
    {
        return float4(0, 0, 0, 0);
    }
    float time = _FishCloudParams.z;
    float scale = max(_FishCloudParams.x, 0.05);
    float2 wind = _FishWeatherWind.xy * (0.3 + _FishWeatherWind.z * 2.0) * _FishCloudParams.w;
    float3 sunDir = _FishSunDir[0].xyz;
    float sunUp = saturate(sunDir.y * 4.0 + 0.3);
    float cosSun = dot(dir, sunDir);
    // Curvature: far layers sit lower toward the horizon.
    float bend = 1.0 / (dir.y + 0.08);

    float4 result = float4(0, 0, 0, 0);

    // Low clouds.
    {
        float height = lerp(900.0, 2400.0, _FishWeatherCloud.z);
        float3 p = cameraPos + dir * (height * bend);
        float4 map = FishWeatherMapAt(p.xz);
        float cover = saturate(max(_FishWeatherCloud.x, map.r));
        float2 uv = (p.xz + wind * time * 12.0) / (3200.0 * scale);
        float n = FishNoise(uv, 0) * 0.65 + FishNoise(uv * 3.1 + 0.37, 1) * 0.35;
        float threshold = 1.0 - cover;
        float density = saturate((n - threshold * 0.85) / 0.22) * saturate(cover * 3.0);
        float storm = saturate(max(_FishWeatherCloud.y * cover, map.a));
        float light = saturate(0.55 + (FishNoise(uv + sunDir.xz * 0.02, 0) - n) * 3.0);
        float3 lit = lerp(_FishCloudShadow.rgb, _FishCloudLit.rgb, light);
        lit = lerp(lit, _FishCloudShadow.rgb * 0.35, storm * 0.85);
        lit += _FishCloudLit.rgb * pow(saturate(cosSun), 12.0) * sunUp * (1.0 - density) * 0.8;   // silver lining
        lit += _FishWeatherCloud.w * float3(0.85, 0.9, 1.0) * (0.6 + storm);                      // lightning
        float fade = saturate(dir.y * 6.0);
        float a = density * fade;
        result.rgb += lit * a * (1.0 - result.a);
        result.a += a * (1.0 - result.a);
    }

    // Mid clouds (altocumulus): smaller cells, thinner.
    {
        float3 p = cameraPos + dir * (4200.0 * bend);
        float4 map = FishWeatherMapAt(p.xz);
        float cover = saturate(max(_FishWeatherCloud.x * 0.7, map.r * 0.8));
        float2 uv = (p.xz + wind * time * 25.0) / (1600.0 * scale);
        float n = FishNoise(uv, 1) * 0.6 + FishNoise(uv * 4.3 + 0.71, 2) * 0.4;
        float density = saturate((n - (1.0 - cover * 0.8)) / 0.18) * 0.7;
        float3 lit = lerp(_FishCloudShadow.rgb, _FishCloudLit.rgb, 0.75) + _FishWeatherCloud.w * 0.5;
        float a = density * saturate(dir.y * 5.0);
        result.rgb += lit * a * (1.0 - result.a);
        result.a += a * (1.0 - result.a);
    }

    // High cirrus: streaks along the high wind.
    {
        float3 p = cameraPos + dir * (9000.0 * bend);
        float2 along = normalize(_FishWeatherWind.xy + float2(0.001, 0.0));
        float2 across = float2(-along.y, along.x);
        float2 q = float2(dot(p.xz, along) / 9000.0, dot(p.xz, across) / 900.0) / scale;
        float n = FishNoise(q + float2(time * 0.002, 0.0), 2);
        float amount = _FishCloudParams.y * (0.4 + _FishWeatherCloud.x * 0.6);
        float density = saturate((n - (1.0 - amount)) / 0.35) * 0.45;
        float3 lit = lerp(_FishCloudLit.rgb, _FishSunColor[0].rgb * 0.2 + _FishCloudLit.rgb, pow(saturate(cosSun), 4.0) * 0.4);
        float a = density * saturate(dir.y * 4.0);
        result.rgb += lit * a * (1.0 - result.a);
        result.a += a * (1.0 - result.a);
    }
    return result;
}

#endif
