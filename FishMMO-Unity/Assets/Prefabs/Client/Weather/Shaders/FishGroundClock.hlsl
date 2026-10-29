#ifndef FISHMMO_GROUND_CLOCK_INCLUDED
#define FISHMMO_GROUND_CLOCK_INCLUDED

// FishMMO (2026-10-26, phase GROUND-BANDS): ground layers that come and go on the SHARED CLOCK — a seasonal phytodetritus
// on an abyssal plain, a dawn SO2 frost gone by noon. The cut paints such a layer like any other; here, after a pixel's
// four strongest layers are found (FishTopLayers), a clocked layer's weight is scaled by a window of the local year or day
// and what it gives up goes to its biome's main ground (the layer table's clock rows, TerrainLayerTable.RowClock /
// RowClockShare, baked from TerrainTextureLayer.clock). The clocks are _FishGroundClock (GlowSky, from the celestial model
// at the world clock): every viewer sees the same ground at the same moment. The C# twin is
// FishMMO.Shared.Biomes.BiomeGroundClock (Window, FactorOf, Shift), pinned constant for constant by GroundBandsTests.
//
// Needs FishLayerRow (FishWeatherTerrainArrayInput.hlsl). A scene with no clocked layer reads one table texel a pixel
// (the share row's w, 0 in every column) and returns.

#ifndef FISH_LAYER_ROW_CLOCK
#define FISH_LAYER_ROW_CLOCK 9
#endif
#ifndef FISH_LAYER_ROW_CLOCK_SHARE
#define FISH_LAYER_ROW_CLOCK_SHARE 10
#endif

#define FISH_GROUND_CLOCK_YEAR 1
#define FISH_GROUND_CLOCK_DAY 2

// x the local year phase (0 midwinter, 0.5 midsummer; turned in the south), y 1 when known, z the local solar time of day
// (0 midnight, 0.5 noon), w 1 when known. All zero: no clock known, every layer is drawn as painted.
float4 _FishGroundClock;

// The distance round the clock between two phases, 0 .. 0.5.
float FishGroundClockRound(float d)
{
    d = frac(d);
    return min(d, 1.0 - d);
}

// 1 inside [from, to] (wrapping past 0), easing to 0 over falloff outside it by the nearer end; from == to is the whole clock.
float FishGroundClockWindow(float phase, float from, float to, float falloff)
{
    phase = frac(phase);
    from = frac(from);
    to = frac(to);
    float span = frac(to - from);
    if (span <= 0.0)
    {
        return 1.0;
    }
    if (frac(phase - from) <= span)
    {
        return 1.0;
    }
    float d = min(FishGroundClockRound(phase - from), FishGroundClockRound(phase - to));
    if (falloff <= 0.0)
    {
        return 0.0;
    }
    float t = saturate(d / falloff);
    return 1.0 - t * t * (3.0 - 2.0 * t);
}

// How much of a clocked layer is drawn now (packed: x kind, y from, z to, w falloff): its floor outside the window, 1 inside;
// 1 for no clock or one not known.
float FishGroundClockFactor(float4 packed, float floorShare)
{
    int kind = (int)round(packed.x);
    bool year = kind == FISH_GROUND_CLOCK_YEAR;
    if (!year && kind != FISH_GROUND_CLOCK_DAY)
    {
        return 1.0;
    }
    float phase = year ? _FishGroundClock.x : _FishGroundClock.z;
    float known = year ? _FishGroundClock.y : _FishGroundClock.w;
    if (known < 0.5)
    {
        return 1.0;
    }
    float w = FishGroundClockWindow(phase, packed.y, packed.z, packed.w);
    return floorShare + (1.0 - floorShare) * w;
}

// Component k (0..3, a literal once the loops unroll) of a vector, read and written without indexing it: FXC (D3D, and the
// WebGPU path, which starts from FXC's bytecode) refuses a vector indexed as an l-value inside a loop it must unroll
// ("array reference cannot be used as an l-value; not natively addressable"), and a select is what the index becomes anyway.
float FishGroundClockPick(float4 v, int k)
{
    return k == 0 ? v.x : (k == 1 ? v.y : (k == 2 ? v.z : v.w));
}

int FishGroundClockPickId(int4 v, int k)
{
    return k == 0 ? v.x : (k == 1 ? v.y : (k == 2 ? v.z : v.w));
}

float4 FishGroundClockPut(float4 v, int k, float x)
{
    return float4(k == 0 ? x : v.x, k == 1 ? x : v.y, k == 2 ? x : v.z, k == 3 ? x : v.w);
}

int4 FishGroundClockPutId(int4 v, int k, int x)
{
    return int4(k == 0 ? x : v.x, k == 1 ? x : v.y, k == 2 ? x : v.z, k == 3 ? x : v.w);
}

// Moves what each clocked layer of the four gives up to its biome's main ground: to that layer's slot when it is one of the
// four, else into the weakest other slot (which gives way to it); a layer with no main to give to (-1) leaves its share to
// the others by the renormalising. The weights stay normalised; an unclocked scene is untouched. No vector is indexed and no
// loop is left early (both stop FXC unrolling): the steps nest instead.
void FishGroundClockShift(inout half4 weights, inout int4 ids)
{
    float4 first = FishLayerRow(ids.x, FISH_LAYER_ROW_CLOCK_SHARE);
    [branch] if (first.w < 0.5)
    {
        return;
    }
    float4 w = (float4)weights;
    int4 id = ids;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float wk = FishGroundClockPick(w, k);
        int idk = FishGroundClockPickId(id, k);
        float4 packed = FishLayerRow(idk, FISH_LAYER_ROW_CLOCK);
        if (wk > 0.0 && packed.x >= 0.5)
        {
            float4 share = FishLayerRow(idk, FISH_LAYER_ROW_CLOCK_SHARE);
            float moved = wk * (1.0 - FishGroundClockFactor(packed, share.x));
            int to = (int)round(share.y);
            if (moved > 0.0)
            {
                w = FishGroundClockPut(w, k, wk - moved);
                if (to >= 0)
                {
                    int at = -1;
                    int weak = -1;
                    float weakest = 2.0;
                    [unroll] for (int j = 0; j < 4; j++)
                    {
                        float wj = FishGroundClockPick(w, j);
                        bool other = j != k;
                        at = (other && at < 0 && FishGroundClockPickId(id, j) == to) ? j : at;
                        bool weaker = other && wj < weakest;
                        weakest = weaker ? wj : weakest;
                        weak = weaker ? j : weak;
                    }
                    at = at < 0 ? weak : at;
                    float kept = FishGroundClockPickId(id, at) == to ? FishGroundClockPick(w, at) : 0.0;
                    w = FishGroundClockPut(w, at, kept + moved);
                    id = FishGroundClockPutId(id, at, to);
                }
            }
        }
    }
    float total = w.x + w.y + w.z + w.w;
    if (total > 1e-4)
    {
        weights = (half4)(w / total);
        ids = id;
    }
}

#endif
