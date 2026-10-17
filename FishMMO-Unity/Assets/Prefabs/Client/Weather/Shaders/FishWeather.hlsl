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
// The fog channels as they come: x how much fog (0..1), y how deep it lies (0..1 of
// FogLayer.ChannelMetres), z how far it has lifted off the ground (0..1), w unused. The fog passes read
// it in metres and per metre from FishFogLayer.hlsl's _FishFogLayer instead.
float4 _FishWeatherFog;
// Surface cover: x snow, y wet, z ash, w sand
float4 _FishWeatherCover;
// x aurora, y local temperature (-1..1), z camera shelter (0..1), w weather time in seconds: the
// world's shared motion clock (WorldMotion.Seconds), wrapped at FISH_MOTION_WRAP — the same moment on
// every player's screen
float4 _FishWeatherMisc;
// The wind held still for each window of the shared motion clock (WeatherShaderGlobals.Apply): x seconds
// into the window, y the window's length, z the wind speed (as _FishWeatherWind.z) when it began, w unused.
// What a phase that runs at the wind's pace reads (FishWindTravel), so it never scrubs as the wind eases.
float4 _FishWindHold;
// The scene's air, the same on every player's screen (WeatherShaderGlobals.Apply, AirDrift): xy the way it blows
// (world x, z; its prevailing wind, which never turns with the weather), z how far its ten-metre wind has carried
// it, m (summed exactly from the world clock, with what the scene adds), wrapped at w. What a pattern laid out across
// the world on the wind reads: laid on the eased wind at the camera, it was each player's own, and turning it about
// the world's origin moved it metres a degree far from there (2026-10-15).
float4 _FishWindWay;

// The way the scene's air blows, unit (_FishWindWay.xy); a diagonal before any weather is set.
float2 FishWindWay()
{
    float2 way = _FishWindWay.xy;
    return dot(way, way) > 1e-6 ? normalize(way) : float2(0.7071, 0.7071);
}

// What _FishWeatherMisc.w wraps at, s (WorldMotion.ShaderWrapSeconds).
#define FISH_MOTION_WRAP 10000.0

// A steady rate, in cycles per second, snapped so the weather clock's wrap holds a whole number of
// cycles: a phase it drives meets itself across the wrap instead of jumping. Constants fold.
float FishWrapCycles(float cyclesPerSecond)
{
    return round(cyclesPerSecond * FISH_MOTION_WRAP) / FISH_MOTION_WRAP;
}

// The same for an angular rate, radians per second.
float FishWrapAngular(float radiansPerSecond)
{
    return round(radiansPerSecond * (FISH_MOTION_WRAP / 6.2831853)) * (6.2831853 / FISH_MOTION_WRAP);
}

// How far a pattern of period `period` has travelled at `rate` (period units per second, held for the
// window), from the window's start: the rate snapped so the window holds whole periods. At the window's
// end it has come a whole number of periods, which is where the next window starts it — so a speed that
// changes with the wind moves the pattern faster or slower and never jumps it, and every player's
// pattern stands in the same place. speed × clock, which it replaces, jumped by the change in speed
// times the whole clock every time the wind eased.
float FishWindTravel(float rate, float period)
{
    float window = max(1.0, _FishWindHold.y);
    float whole = max(0.0, round(rate * window / period));
    return whole * period / window * _FishWindHold.x;
}
// x 1 when the quality tier lifts terrain under deep snow, 0 otherwise. y-w unused.
float4 _FishWeatherTier;
// The season where the camera is, from the world clock (WeatherShaderGlobals.ApplySeason, called by
// ClientWeather with the scene's climate): x the local year phase (0 midwinter, 0.25 spring,
// 0.5 midsummer, 0.75 autumn: CelestialMath.Season01, turned half a year in the south), y local
// summer (-1 dormant .. 1 growing: SceneClimate.Foliage), z the climate's humidity offset from its
// mean (negative = drier than usual: drought), w how hard its winter is (0 none .. 1 one that strips
// a deciduous tree, never quite 0 once set). All zero means no season is known.
float4 _FishSeason;
// The season's air for plants (WeatherShaderGlobals.SeasonAir, SceneClimate.FoliageState): x °C at sea level under the
// scene now, y the year's coldest there, z °C colder per metre up, w 1 when set. Unset, every plant takes _FishSeason's.
float4 _FishSeasonAir;

// SceneClimate.GrowthC / DormantC / StripC / LeafyC.
#define FISH_GROWTH_C 5.0
#define FISH_DORMANT_C 1.0
#define FISH_STRIP_C 5.0
#define FISH_LEAFY_C 12.0

/* How warm it is for growing at a plant's height (world y, metres above the sea): −1 dormant .. 1 growing
 * (SceneClimate.GrowingAt of the season's air cooled up to it). One figure for every plant once: the scene's at sea level,
 * so an alpine meadow flowered and cured on the valley's dates. */
float FishSeasonGrowing(float worldY)
{
    float t = _FishSeasonAir.x - max(0.0, worldY) * _FishSeasonAir.z;
    float growing = clamp(t >= FISH_GROWTH_C ? (t - FISH_GROWTH_C) / 10.0 : (t - FISH_GROWTH_C) / (FISH_GROWTH_C - FISH_DORMANT_C), -1.0, 1.0);
    return _FishSeasonAir.w > 0.5 ? growing : _FishSeason.y;
}

// How hard the winter is at a plant's height (SceneClimate.WinterAt of the year's coldest cooled up to it): 0 none .. 1
// strips a deciduous tree; never quite 0 once a season is known, as _FishSeason.w.
float FishSeasonWinter(float worldY)
{
    float coldest = _FishSeasonAir.y - max(0.0, worldY) * _FishSeasonAir.z;
    float winter = max(0.001, saturate((FISH_LEAFY_C - coldest) / (FISH_LEAFY_C - FISH_STRIP_C)));
    return _FishSeasonAir.w > 0.5 ? winter : _FishSeason.w;
}

/* How far open a flower is now: 0 closed (gone over, nothing showing) .. 1 in full bloom. Flowers open once the place is
 * warm enough to grow (FishSeasonGrowing past 0: 5 °C at the flower's height) and close again as it cools, each at its own
 * point (`own`, 0..1, its hash) between FISH_BLOOM_FIRST and FISH_BLOOM_LAST, so a meadow comes into flower over weeks and
 * goes over the same way. A tundra whose summer barely passes 8 °C flowers only around its warmest weeks; no season known,
 * every flower is open. */
#define FISH_BLOOM_FIRST 0.0
#define FISH_BLOOM_LAST 0.25
#define FISH_BLOOM_RAMP 0.05
float FishBloom(float own, float worldY)
{
    float opensAt = lerp(FISH_BLOOM_FIRST, FISH_BLOOM_LAST, saturate(own));
    float open = saturate((FishSeasonGrowing(worldY) - opensAt) / FISH_BLOOM_RAMP);
    return _FishSeason.w < 1e-4 ? 1.0 : open;
}

// Where the cover lies: a top-down map around the camera, r snow, g wet, b ash, a sand. The
// scene-wide _FishWeatherCover is the average of it, and stands in wherever the map does not reach.
float4 _FishCoverRect;      // xy world x/z of the map's corner, zw size in metres
float4 _FishCoverParams;    // x 1 when the map is valid, y metres per texel
TEXTURE2D(_FishCoverTex);
SAMPLER(sampler_FishCoverTex);

// The seasonal snow line (WeatherCoverMap.SnowLineId, GroundCover.SnowLine): x the height the snowpack starts at, y where
// it is whole (world y), z 1 while snow may lie anywhere in view, w how much of a cold enough place it covers (0: none).
float4 _FishSnowLine;

// The season's snowpack at a height, 0..1: what lies past the cover map, where the map's own bands (each its own climate,
// altitude and all) do not reach. A distant range stands white above its snow line.
float FishSnowpackAt(float worldY)
{
    return _FishSnowLine.w * saturate((worldY - _FishSnowLine.x) / max(1e-3, _FishSnowLine.y - _FishSnowLine.x));
}

// Whether snow may lie anywhere in view: the scene's own figure, the map's, or ground reaching the snow line. What the snow
// lift and the ground's snow blend are gated on; the scene's own figure alone shut them off under snow-capped peaks.
bool FishSnowAnywhere()
{
    return _FishWeatherCover.x > 0.0 || _FishSnowLine.z > 0.5;
}

// The cover at a world position: from the map where it reaches, the scene's own figure elsewhere, with the season's
// snowpack by height over it.
float4 FishCoverAt(float3 worldPos)
{
    float4 scene = _FishWeatherCover;
    scene.x = max(scene.x, FishSnowpackAt(worldPos.y));
    if (_FishCoverParams.x < 0.5)
    {
        return scene;
    }
    float2 uv = (worldPos.xz - _FishCoverRect.xy) / max(_FishCoverRect.zw, 1e-3);
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return scene;
    }
    return SAMPLE_TEXTURE2D_LOD(_FishCoverTex, sampler_FishCoverTex, uv, 0);
}

// The blade grass standing on the ground round the camera (GrassSwardMap): how closed the sward is, 0..1, read with
// the cover's sampler (the ground's passes are near the limit of samplers).
TEXTURE2D(_FishSwardTex);
float4 _FishSwardRect;      // xy the map's world x/z corner, zw one over its size in metres
float4 _FishSwardParams;    // x light the ground keeps under a closed sward, y/z the blades' far blend (metres), w 1 when on

// How much of the light the ground keeps under the grass standing on it, 1 where none stands. A blade is shaded toward
// its root, and the ground it grows in had nothing of the kind: lit as open ground, it shone between the blades. Taken
// as the roots take it, and let go through the blades' own far blend, where they are lit flat as the ground they cover.
half FishSwardShade(float3 worldPos)
{
    if (_FishSwardParams.w < 0.5)
    {
        return 1.0h;
    }
    float2 uv = (worldPos.xz - _FishSwardRect.xy) * _FishSwardRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return 1.0h;
    }
    float sward = SAMPLE_TEXTURE2D_LOD(_FishSwardTex, sampler_FishCoverTex, uv, 0).r;
    float far = smoothstep(_FishSwardParams.y, _FishSwardParams.z, distance(worldPos, _WorldSpaceCameraPos));
    return (half)lerp(1.0, _FishSwardParams.x, sward * (1.0 - far));
}

// Deep snow (WeatherCoverMap): how deep the snow lies past the blanket the cover's x stands for, on the same map,
// one byte of the deepest it can lie. Only the tier that lifts the ground under snow shows it (FishSnowLiftMetres).
float4 _FishSnowDepthParams;    // x metres the map's 1 stands for (the deepest snow lies), 0 with no map
// Read with the cover's own sampler: the same map, filter and clamp, and the ground's shaders are near the limit of
// samplers a pass may have.
TEXTURE2D(_FishSnowDepthTex);

// Metres of snow past the blanket at a world position; none past the map.
float FishSnowDepthAt(float3 worldPos)
{
    if (_FishCoverParams.x < 0.5 || _FishSnowDepthParams.x <= 0.0)
    {
        return 0.0;
    }
    float2 uv = (worldPos.xz - _FishCoverRect.xy) / max(_FishCoverRect.zw, 1e-3);
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return 0.0;
    }
    return SAMPLE_TEXTURE2D_LOD(_FishSnowDepthTex, sampler_FishCoverTex, uv, 0).r * _FishSnowDepthParams.x;
}

// Trails (GroundTrailMap): r how hard the ground is trodden (0..1), g how flat the grass lies (0..1). A world-aligned
// map round the camera that wraps — a texel holds the world texel it is equal to modulo the map's size — so moving
// the camera only clears the strip it moves into, never copies the map along.
float4 _FishTrailRect;      // xy the window's corner (world x/z), z its size (m), w 1 when the map is valid
float4 _FishTrailParams;    // x metres per texel, y texels a side
TEXTURE2D(_FishTrailTex);

// One texel of the trail map by world texel: wrapped to where it is kept.
float2 FishTrailTexel(float2 worldTexel)
{
    float size = _FishTrailParams.y;
    int2 kept = int2(worldTexel - floor(worldTexel / size) * size);
    return LOAD_TEXTURE2D_LOD(_FishTrailTex, kept, 0).rg;
}

// The trail at a point on the ground plane, filtered; nothing outside the window. Filtered by hand from four loads,
// not by a sampler: a sampler would need wrapping to filter across where the map wraps, and the ground's shaders
// have no sampler left to spare. The window's last texel on each side is left out: there the filter would reach
// round the wrap into ground at the other side of the map.
float2 FishTrailAt(float2 worldXZ)
{
    if (_FishTrailRect.w < 0.5)
    {
        return 0.0;
    }
    float2 local = worldXZ - _FishTrailRect.xy;
    float margin = _FishTrailParams.x;
    if (any(local < margin) || any(local > _FishTrailRect.z - margin))
    {
        return 0.0;
    }
    float2 p = worldXZ / _FishTrailParams.x - 0.5;
    float2 i = floor(p);
    float2 f = p - i;
    float2 a = FishTrailTexel(i);
    float2 b = FishTrailTexel(i + float2(1.0, 0.0));
    float2 c = FishTrailTexel(i + float2(0.0, 1.0));
    float2 d = FishTrailTexel(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
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

// A cheap moving gust, 0..1, for vertex animation. `time` is unused: the gust travels on the held
// wind (FishWindTravel), each factor snapped to its own whole turns, along the scene's own way
// (FishWindWay), so it neither scrubs as the wind eases nor differs between players.
float FishWindGust(float2 xz, float time)
{
    float2 dir = FishWindWay();
    float along = dot(xz, dir) * 0.08;
    float rate = 0.6 + _FishWindHold.z * 2.0;
    float a = along - FishWindTravel(rate, 6.2831853);
    float b = along * 0.37 + 1.7 - FishWindTravel(rate * 0.37, 6.2831853);
    return (sin(a) * 0.5 + 0.5) * (sin(b) * 0.5 + 0.5) * _FishWeatherWind.w;
}

#endif
