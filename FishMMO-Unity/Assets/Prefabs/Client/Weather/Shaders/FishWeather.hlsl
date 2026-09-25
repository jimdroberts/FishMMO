#ifndef FISHMMO_WEATHER_INCLUDED
#define FISHMMO_WEATHER_INCLUDED

// Weather state, set once per frame by WeatherShaderGlobals on the client. Every weather effect
// reads these; nothing else writes them. See FishMMO.Client.WeatherShaderGlobals.

// x cloud cover, y cloud density, z cloud base (0 low .. 1 high), w lightning flash (0..1)
float4 _FishWeatherCloud;
// x precipitation amount, y drop size, z snow share of what falls, w storm severity
float4 _FishWeatherPrecip;
// What is falling, as shares of the whole: x rain, y snow, z hail, w dust (ash and sand).
float4 _FishWeatherMix;
// xy wind direction on the ground plane (world x, z), z speed (0..1 = 0..30 m/s), w gust
float4 _FishWeatherWind;
// x fog density, y fog height, z volumetric fog, w unused
float4 _FishWeatherFog;
// Surface cover: x snow, y wet, z ash, w sand
float4 _FishWeatherCover;
// x aurora, y local temperature (-1..1), z camera shelter (0..1), w weather time in seconds
float4 _FishWeatherMisc;
// x 1 when the quality tier lifts terrain under deep snow, 0 otherwise. y-w unused.
float4 _FishWeatherTier;

// Where the cover lies: a top-down map around the camera, r snow, g wet, b ash, a sand. The
// scene-wide _FishWeatherCover is the average of it, and stands in wherever the map does not reach.
float4 _FishCoverRect;      // xy world x/z of the map's corner, zw size in metres
float4 _FishCoverParams;    // x 1 when the map is valid, y metres per texel
TEXTURE2D(_FishCoverTex);
SAMPLER(sampler_FishCoverTex);

// The cover at a world position: from the map where it reaches, the scene's own figure elsewhere.
float4 FishCoverAt(float3 worldPos)
{
    if (_FishCoverParams.x < 0.5)
    {
        return _FishWeatherCover;
    }
    float2 uv = (worldPos.xz - _FishCoverRect.xy) / max(_FishCoverRect.zw, 1e-3);
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return _FishWeatherCover;
    }
    return SAMPLE_TEXTURE2D_LOD(_FishCoverTex, sampler_FishCoverTex, uv, 0);
}

// Sky occlusion: a top-down height map of the highest surface around the camera.
// Rect: xy = world x/z of the map's corner, zw = size in metres.
float4 _FishOcclusionRect;
// x lowest height, y height range, z 1 when the map is valid, w metres per texel
float4 _FishOcclusionRange;
TEXTURE2D(_FishOcclusionTex);
SAMPLER(sampler_FishOcclusionTex);
// The open water: x its still level (world metres, tide included), y 1 when there is any, z the
// significant height of its waves (m). The map is cast at solid ground and a ray goes straight
// through water, so the sea is laid over it here: wherever the ground is lower, the water is the
// highest surface, and rain, snow and splashes stop on it instead of on the bed underneath.
float4 _FishOcclusionWater;

// One texel of the map, decoded. Heights are 16 bits split across R and G, which the sampler cannot
// filter: blending the two bytes separately garbles every value that carries from one to the other.
float FishSkyOcclusionTexel(float2 texel, float resolution)
{
    float4 t = SAMPLE_TEXTURE2D_LOD(_FishOcclusionTex, sampler_FishOcclusionTex, (texel + 0.5) / resolution, 0);
    float encoded = t.r * (255.0 / 256.0) + t.g * (1.0 / 256.0);
    return _FishOcclusionRange.x + encoded * _FishOcclusionRange.y;
}

// The highest SOLID surface above a point, in world metres: the map alone, BILINEAR.
//
// Read as one texel, the answer was a flat step per texel — and on a slope the ground under a texel
// falls away from its one recorded height toward the downhill edge and jumps back at the next. Every
// test against it (is this surface under something? how much rain reaches it?) came out as a
// sawtooth ramp in squares, and the puddle threshold magnified it into the checkerboard of wet and
// dry squares on sloping terrain. Blended between the four texel centres the rays were cast at, the
// map follows the slope, and ground reads as level with itself.
float FishSkyOcclusionGround(float3 worldPos)
{
    float texelMetres = max(1e-3, _FishOcclusionRange.w);
    float resolution = max(1.0, _FishOcclusionRect.z / texelMetres);
    float2 st = (worldPos.xz - _FishOcclusionRect.xy) / texelMetres - 0.5;
    float2 i = floor(st);
    float2 f = st - i;
    float a = FishSkyOcclusionTexel(i, resolution);
    float b = FishSkyOcclusionTexel(i + float2(1.0, 0.0), resolution);
    float c = FishSkyOcclusionTexel(i + float2(0.0, 1.0), resolution);
    float d = FishSkyOcclusionTexel(i + float2(1.0, 1.0), resolution);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// The highest surface above a point, water included: where anything falling from the sky ends up.
float FishSkyOcclusionHeight(float3 worldPos)
{
    float top = FishSkyOcclusionGround(worldPos);
    return _FishOcclusionWater.y > 0.5 ? max(top, _FishOcclusionWater.x) : top;
}

// 1 above the open water, 0 a quarter of a metre under it; 1 everywhere when there is no water.
// Independent of the map, so it holds past the map's edge too: the sea bed a hundred metres off
// gathers no snow either.
float FishWaterOpen(float3 worldPos)
{
    return _FishOcclusionWater.y > 0.5 ? saturate((worldPos.y - _FishOcclusionWater.x) * 4.0 + 1.0) : 1.0;
}

// 1 where the sky is open above a point, 0 under a roof or under the water. Outside the map, or
// without one, open to the sky — but never under the sea.
float FishSkyOpen(float3 worldPos)
{
    float open = 1.0;
    if (_FishOcclusionRange.z > 0.5)
    {
        float2 uv = (worldPos.xz - _FishOcclusionRect.xy) / max(_FishOcclusionRect.zw, 1e-3);
        if (all(uv >= 0.0) && all(uv <= 1.0))
        {
            float top = FishSkyOcclusionGround(worldPos);
            open = saturate((worldPos.y - top) * 2.0 + 1.0);
        }
    }
    return min(open, FishWaterOpen(worldPos));
}

// How much cloud stands over a point, 0 clear sky to 1 solid: the sky's own overhead map, a window
// round the camera. Outside it, or without one, everything is under cloud — the old behaviour, so a
// scene with no cloud map at all still gets its rain.
float4 _FishCloudOverheadRect;  // xy the window's centre (world xz), z its size (m), w 1 when there is a map
TEXTURE2D(_FishCloudOverhead);
SAMPLER(sampler_FishCloudOverhead);

float FishCloudOver(float3 worldPos)
{
    if (_FishCloudOverheadRect.w < 0.5)
    {
        return 1.0;
    }
    float2 uv = (worldPos.xz - _FishCloudOverheadRect.xy) / max(_FishCloudOverheadRect.z, 1e-3) + 0.5;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return 1.0;
    }
    return SAMPLE_TEXTURE2D_LOD(_FishCloudOverhead, sampler_FishCloudOverhead, uv, 0).r;
}

// A cheap moving gust, 0..1, for vertex animation.
float FishWindGust(float2 xz, float time)
{
    float2 dir = _FishWeatherWind.xy;
    float phase = dot(xz, dir) * 0.08 - time * (0.6 + _FishWeatherWind.z * 2.0);
    return (sin(phase) * 0.5 + 0.5) * (sin(phase * 0.37 + 1.7) * 0.5 + 0.5) * _FishWeatherWind.w;
}

#endif
