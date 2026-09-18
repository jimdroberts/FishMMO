#ifndef FISHMMO_SKY_COMMON_INCLUDED
#define FISHMMO_SKY_COMMON_INCLUDED

// Sky state, set every frame by FishMMO.Client.SkySystem (the only writer).
#include "FishWeather.hlsl"

float4 _FishSkyZenith;      // rgb
float4 _FishSkyHorizon;     // rgb
float4 _FishSkyGround;      // rgb
float4 _FishSkyFogColor;    // rgb, a = how much the horizon melts into fog
float4 _FishSkyParams;      // x star visibility, y milky way, z twinkle, w exposure
float4 _FishSkyEclipseBody;  // xyz direction to the body covering the sun, w its angular radius (rad)
float4 _FishSkyEclipse;     // x solar eclipse 0..1, y lunar eclipse 0..1, z airless (1 = black sky), w the covering body's angular radius (rad)
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

#endif
