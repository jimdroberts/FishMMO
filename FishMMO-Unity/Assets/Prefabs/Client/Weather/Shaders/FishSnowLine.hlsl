#ifndef FISH_SNOW_LINE_INCLUDED
#define FISH_SNOW_LINE_INCLUDED

// The seasonal snow line's shape (SnowLineShape.cs is its C# twin, and pins these): where the pack starts, how it wanders
// and how deep it lies, at every point by its own height. Pure functions of their arguments, so the surfaces (through
// FishWeather.hlsl's globals) and the grass's compute pass (through its own bound copies) draw one line.
//
// snowLine: x where the middle's pack starts, y where it is whole (world y), z the anywhere gate, w the share it covers.
// deep: x where it lies deepest (world y), y the metres it then lies.

// GroundCover.PackWarm - PackCold, K: what snowLine.y - snowLine.x stands for.
#define FISH_PACK_SPAN_K 0.7
// SnowLineShape.WanderKelvin: how far the line wanders either way, K of the climate.
#define FISH_SNOW_WANDER_K 0.8

// SnowLineShape.Cell: a cell's value 0..1, the hash's 24 low bits.
float FishSnowCell(int2 c)
{
    uint h = (uint)c.x * 0x8DA6B343u + (uint)c.y * 0xD8163841u;
    h ^= h >> 15;
    h *= 0x2C1B3C6Du;
    h ^= h >> 12;
    h *= 0x297A2D39u;
    h ^= h >> 15;
    return (float)(h & 0xFFFFFFu) / 16777215.0;
}

float FishSnowValueNoise(float2 p)
{
    float2 fl = floor(p);
    int2 i = (int2)fl;
    float2 f = p - fl;
    f = f * f * (3.0 - 2.0 * f);
    float a = FishSnowCell(i), b = FishSnowCell(i + int2(1, 0)), c = FishSnowCell(i + int2(0, 1)), d = FishSnowCell(i + int2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// SnowLineShape.Wander: about -1..1, over 240 m and 80 m.
float FishSnowLineWander(float2 xz)
{
    float coarse = FishSnowValueNoise(xz / 240.0);
    float fine = FishSnowValueNoise(xz / 80.0 + 31.7);
    return (coarse * 0.65 + fine * 0.35) * 2.0 - 1.0;
}

// SnowLineShape.LiftAt: how far the line is lifted at a place, world metres, from its sea-level climate against the
// middle's (fieldKelvin) and the wander, at the metres per kelvin the published line stands for.
float FishSnowLineLift(float2 xz, float fieldKelvin, float4 snowLine)
{
    float perKelvin = max(1e-3, snowLine.y - snowLine.x) / FISH_PACK_SPAN_K;
    return (fieldKelvin + FishSnowLineWander(xz) * FISH_SNOW_WANDER_K) * perKelvin;
}

// SnowLineShape.CoverAt: the pack's cover at a height, 0..1.
float FishPackCover(float worldY, float lift, float4 snowLine)
{
    float t = saturate((worldY - lift - snowLine.x) / max(1e-3, snowLine.y - snowLine.x));
    return snowLine.w * t * t * (3.0 - 2.0 * t);
}

// SnowLineShape.DepthAt: the pack's depth past the blanket at a height, metres.
float FishPackDepth(float worldY, float lift, float4 snowLine, float4 deep)
{
    return snowLine.w * saturate((worldY - lift - snowLine.y) / max(1e-3, deep.x - snowLine.y)) * deep.y;
}

#endif
