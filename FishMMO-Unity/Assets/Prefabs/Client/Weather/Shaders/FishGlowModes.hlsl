// FishMMO: how a biome's cold glows move (2026-10-25, I6i; SceneGlowModes.cs, constant for constant): its way glows' parts (a glowjar's
// mote, a rune stone's runes: _FishStructure 3) and its glowing crystals (_FishStructure -1 flickering, -2 steady), each by the mode its
// biome names (BiomeTemplate.GlowModes), read from the biome under the piece's origin. Every mode a pure function of the shared motion
// clock (_FishWeatherMisc.w, wrapped at 10 000 s; every rate a whole number of cycles in it) and of the sky's and the weather's state
// GlowSky publishes (themselves pure functions of the world clock at the scene), so the part, its light (SiteLightPool) and every
// other player's view move as one.
//
// The table (GlowModeTable), global arrays, no texture (this pass is near the texture units WebGPU and GL allow): _FishGlowRows[64],
//   one per biome on the scene's flora map (its palette index): x the way glows' entry, y the crystals', each mode + 32·warm + 64·every,
//   0 none; z, w their episode schedule's index into _FishGlowEpisodes[16] (slot A, slot B, slot C, on share), -1 none; the schedules'
//   ramps in _FishGlowRamps[4] (four a vector).
// _FishGlowInfo = (on, rows, grid w, grid h): x 0 when no biome on the scene names a mode, and every glow draws exactly as before.
// The sky: _FishGlowSkyA (sun height, local time 0..1, night share, day mod 720720) · B (moon lit, parent lit, field turn, orbit turn) ·
//   C (wet, wind, rain to come, fog) · D (sea, turbidity, prevailing way xz); _FishPlumeTide; _FishTidalStress (cos 2φ, sin 2φ, 0, 0);
//   _FishVentBurst (the scene's own vent's Strombolian burst, decision 95).
// The stored places (2026-10-25, I6i-b; SceneGlowPlaces.cs, number for number): _FishGlowLines[64] (a stored landform line's stretch:
//   A.xz, B.xz) + _FishGlowLineData[64] (along at A + the line's phase, along span to B, cos 2b, sin 2b), which a TRAIL creeps along
//   and a KNEADING reads the bearing of; _FishGlowVents[16] (a fountain vent: x, z, its seed's high half + 65536·its schedule, its
//   seed's low half), whose episodes an EPISODE plays; _FishGlowAnchors[32] (x, z, radius, row: a site or a light by a biome edge,
//   everything inside reads that row, so a light and its stone agree); _FishGlowPlaceInfo = (lines, vents, anchors, 0). With none
//   stored: a fixed bearing, a smooth field, a 500 m cell and the grid's own row, as before.
//
// Only the indirect twin's pass includes it (FishWeatherLitIndirect.shader, FXC on purpose); needs FishFlora.hlsl (the flora grid),
// FishWeather.hlsl (the clock) and FishStructureWeathering.hlsl (_FishGlowLights, the pulse and the flicker) first. Loads only, no sampler.

#ifndef FISHMMO_GLOW_MODES_INCLUDED
#define FISHMMO_GLOW_MODES_INCLUDED

float4 _FishGlowRows[64];
float4 _FishGlowEpisodes[16];
float4 _FishGlowRamps[4];
float4 _FishGlowInfo;
float4 _FishGlowLines[64];
float4 _FishGlowLineData[64];
float4 _FishGlowVents[16];
float4 _FishGlowAnchors[32];
float4 _FishGlowPlaceInfo;
float4 _FishGlowSkyA;
float4 _FishGlowSkyB;
float4 _FishGlowSkyC;
float4 _FishGlowSkyD;
float _FishPlumeTide;
// The scene's own Strombolian vent's burst, 0..1 (decision 95; VentBursts.At on the scene's seed, GlowSky).
float _FishVentBurst;
float4 _FishTidalStress;

#define FISH_GLOW_TURN 6.28318530718
#define FISH_GLOW_DAY_TURN 0.01

/// <summary>The glow table's row (the biome's palette index) under a world xz: the flora grid's texel, unjittered.</summary>
uint FishGlowRowAt(float2 xz)
{
    if (_FishFloraRect.z <= 0.0 || _FishGlowInfo.z < 1.0 || _FishGlowInfo.w < 1.0)
    {
        return 0u;
    }
    float2 uv = (xz - _FishFloraRect.xy) * _FishFloraRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return 0u;
    }
    int2 size = (int2)_FishGlowInfo.zw;
    int2 texel = clamp((int2)floor(uv * _FishGlowInfo.zw), int2(0, 0), size - 1);
    uint index = (uint)round(_FishFloraBiomes.Load(int3(texel, 0)) * 255.0);
    return min(index, (uint)max(_FishGlowInfo.y - 1.0, 0.0));
}

/// <summary>The row a glow reads: the first anchor disc holding it (SceneGlowPlaces.RowAt), else the grid's.</summary>
uint FishGlowAnchoredRow(float2 xz, uint row)
{
    int count = (int)_FishGlowPlaceInfo.z;
    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 a = _FishGlowAnchors[i];
        float2 d = xz - a.xy;
        if (dot(d, d) <= a.z * a.z)
        {
            return (uint)round(a.w);
        }
    }
    return row;
}

/// <summary>
/// The nearest stored line's stretch (SceneGlowPlaces.NearestLine, the first of equals): how far along its line the glow stands and
/// its bearing doubled. False with none stored.
/// </summary>
bool FishGlowNearestLine(float2 xz, out float along, out float2 doubled)
{
    along = 0.0;
    doubled = float2(1.0, 0.0);
    int count = (int)_FishGlowPlaceInfo.x;
    if (count <= 0)
    {
        return false;
    }
    float best = 3.402823e38;
    float bestT = 0.0;
    int found = 0;
    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 s = _FishGlowLines[i];
        float2 d = s.zw - s.xy;
        float length2 = dot(d, d);
        float t = length2 > 0.0 ? saturate(dot(xz - s.xy, d) / length2) : 0.0;
        float2 q = s.xy + t * d - xz;
        float dist = dot(q, q);
        if (dist < best)
        {
            best = dist;
            found = i;
            bestT = t;
        }
    }
    float4 data = _FishGlowLineData[found];
    along = data.x + bestT * data.y;
    doubled = data.zw;
    return true;
}

/// <summary>A schedule's slots and ramp (_FishGlowEpisodes, _FishGlowRamps).</summary>
void FishGlowSchedule(uint index, out float4 slots, out float ramp)
{
    uint s = min(index, 15u);
    slots = _FishGlowEpisodes[s];
    float4 r = _FishGlowRamps[s / 4u];
    uint k = s % 4u;
    ramp = k == 0u ? r.x : k == 1u ? r.y : k == 2u ? r.z : r.w;
}

/// <summary>The nearest stored fountain vent (SceneGlowPlaces.NearestVent): its seed and its schedule. False with none stored.</summary>
bool FishGlowNearestVent(float2 xz, out uint seed, out float4 slots, out float ramp)
{
    seed = 0u;
    slots = float4(0.0, 0.0, 0.0, 0.0);
    ramp = 0.0;
    int count = (int)_FishGlowPlaceInfo.y;
    if (count <= 0)
    {
        return false;
    }
    float best = 3.402823e38;
    int found = 0;
    [loop]
    for (int i = 0; i < count; i++)
    {
        float2 d = xz - _FishGlowVents[i].xy;
        float dist = dot(d, d);
        if (dist < best)
        {
            best = dist;
            found = i;
        }
    }
    float4 v = _FishGlowVents[found];
    float schedule = floor(v.z / 65536.0);
    uint high = (uint)round(v.z - 65536.0 * schedule);
    seed = (high << 16) | (uint)round(v.w);
    FishGlowSchedule((uint)schedule, slots, ramp);
    return true;
}

float FishGlowSmooth(float t)
{
    t = saturate(t);
    return t * t * (3.0 - 2.0 * t);
}

float FishGlowPositiveMod(float v, float n)
{
    float m = v - n * floor(v / n);
    return m >= n ? m - n : m;
}

/// <summary>A 32-bit mix (lowbias32) of a value and a seed: TurbidityEpisodes.Mix.</summary>
uint FishGlowMix(uint v, uint seed)
{
    uint h = (v * 0x9E3779B1u) ^ seed;
    h ^= h >> 16;
    h *= 0x7FEB352Du;
    h ^= h >> 15;
    h *= 0x846CA68Bu;
    h ^= h >> 16;
    return h;
}

float FishGlowUnit(uint h)
{
    return (float)(h >> 8) * (1.0 / 16777216.0);
}

/// <summary>
/// THE slot function (EpisodeSlots.At) with no skip and full strength, as the fountains' episodes read it: 0 between episodes, the
/// ramps inside one.
/// </summary>
float FishGlowSlot(float seconds, float length, float onSeconds, float ramp, uint seed)
{
    if (!(length > 0.0) || !(onSeconds > 0.0))
    {
        return 0.0;
    }
    float on = min(onSeconds, length);
    float slot = floor(seconds / length);
    uint h = FishGlowMix((uint)(int)slot, seed);
    float start = slot * length + FishGlowUnit(h) * (length - on);
    float into = seconds - start;
    if (into <= 0.0 || into >= on)
    {
        return 0.0;
    }
    float r = max(1e-3, min(ramp, 0.5 * on));
    return FishGlowSmooth(into / r) * FishGlowSmooth((on - into) / r);
}

/// <summary>A fountain's episode (BiomeFountainEpisodes.At): the slot function by a seed and the slot length it picks.</summary>
float FishGlowEpisodeOf(float4 slots, float ramp, float seconds, uint seed)
{
    float count = (slots.x > 0.0 ? 1.0 : 0.0) + (slots.y > 0.0 ? 1.0 : 0.0) + (slots.z > 0.0 ? 1.0 : 0.0);
    if (count < 0.5)
    {
        return 0.0;
    }
    uint pick = seed % (uint)count;
    // The pick-th named slot length, in order A, B, C.
    float length = 0.0;
    uint i = pick;
    if (slots.x > 0.0) { if (i == 0u) { length = slots.x; } i = i == 0u ? 99u : i - 1u; }
    if (slots.y > 0.0 && length <= 0.0) { if (i == 0u) { length = slots.y; } i = i == 0u ? 99u : i - 1u; }
    if (slots.z > 0.0 && length <= 0.0 && i == 0u) { length = slots.z; }
    return FishGlowSlot(seconds, length, saturate(slots.w) * length, ramp, seed);
}

/// <summary>
/// The fountains' episode a glow plays: its nearest stored fountain vent's (I6i-b: that vent's seed and schedule), else its 500 m
/// cell's seed on its own schedule (BiomeFountainEpisodes.GlowAt).
/// </summary>
float FishGlowEpisode(float4 slots, float ramp, float seconds, float2 xz)
{
    uint ventSeed;
    float4 ventSlots;
    float ventRamp;
    if (FishGlowNearestVent(xz, ventSeed, ventSlots, ventRamp))
    {
        return FishGlowEpisodeOf(ventSlots, ventRamp, seconds, ventSeed);
    }
    int2 cell = (int2)floor(xz / 500.0);
    uint seed = FishGlowMix(((uint)cell.x * 0x8DA6B343u) ^ ((uint)cell.y * 0xD8163841u), 0xE9150DEu);
    return FishGlowEpisodeOf(slots, ramp, seconds, seed);
}

/// <summary>Whether a mode carries its own daylight (sun 6, after sunset 11, terminator 14, night 19): SceneGlowModes.OwnsDaylight.</summary>
bool FishGlowOwnsDaylight(int mode)
{
    return mode == 6 || mode == 11 || mode == 14 || mode == 19;
}

/// <summary>A glow's brightness under a mode (SceneGlowModes.Value), at the piece's origin. Mode 0 reads as the pulse.</summary>
float FishGlowModeValue(int mode, float every, float4 slots, float ramp, float3 originWS)
{
    float t = _FishWeatherMisc.w;
    float x = originWS.x, z = originWS.z;
    float4 a = _FishGlowSkyA;
    float4 b = _FishGlowSkyB;
    float4 c = _FishGlowSkyC;
    float4 d = _FishGlowSkyD;
    // The nearest stored line, for a trail or a kneading only (I6i-b): one search, read by either branch.
    float along = 0.0;
    float2 doubled = float2(1.0, 0.0);
    bool onLine = false;
    UNITY_BRANCH
    if (mode == 17 || mode == 22)
    {
        onLine = FishGlowNearestLine(originWS.xz, along, doubled);
    }
    UNITY_BRANCH
    if (mode == 2)
    {
        return FishGlowFlicker(originWS);
    }
    else if (mode == 3)
    {
        return 1.0;
    }
    else if (mode == 4)
    {
        float ca = sin(FISH_GLOW_TURN * frac(0.31 * t + frac(x * 0.61 + z * 0.23)));
        float cb = sin(FISH_GLOW_TURN * frac(0.47 * t + frac(x * -0.29 + z * 0.53)));
        float cc = sin(FISH_GLOW_TURN * frac(0.13 * t + frac(x * 0.17 + z * -0.41)));
        float m = 1.0 - abs(ca + cb + cc) / 3.0;
        return 0.5 + 0.5 * m * m * m * m;
    }
    else if (mode == 5 || mode == 21)
    {
        float n = clamp(every, 1.0, 16.0);
        float from, to;
        if (mode == 5)
        {
            from = (FishGlowPositiveMod(a.w - 1.0, n) + 1.0) / n;
            to = (FishGlowPositiveMod(a.w, n) + 1.0) / n;
        }
        else
        {
            from = FishGlowPositiveMod(a.w - 1.0, n) < 0.5 ? 1.0 : 0.0;
            to = FishGlowPositiveMod(a.w, n) < 0.5 ? 1.0 : 0.0;
        }
        float v = lerp(from, to, FishGlowSmooth(a.y / FISH_GLOW_DAY_TURN));
        return mode == 5 ? 0.2 + 0.8 * v : 0.04 + 0.96 * v;
    }
    else if (mode == 6)
    {
        return 0.15 + 0.85 * FishGlowSmooth((a.x + 0.05) / 0.55);
    }
    else if (mode == 19)
    {
        return 0.15 + 0.85 * (1.0 - FishGlowSmooth((a.x + 0.05) / 0.55));
    }
    else if (mode == 7)
    {
        return 0.3 + 0.7 * FishGlowSmooth(c.x);
    }
    else if (mode == 8)
    {
        return 0.25 + 0.75 * FishGlowSmooth(c.y);
    }
    else if (mode == 9)
    {
        float breath = 0.5 - 0.5 * cos(FISH_GLOW_TURN * frac(0.125 * t + frac(x * 0.013 + z * 0.019)));
        float night = saturate(a.z), day = 1.0 - night;
        float sunrise = 0.5 - 0.5 * day + 0.02;
        float off = frac(a.y - sunrise + 0.5) - 0.5;
        float dawn = saturate(1.0 - abs(off) / 0.06) * FishGlowSmooth(min(night, day) / 0.02);
        return (0.35 + 0.65 * FishGlowSmooth(breath)) * (1.0 + 0.4 * dawn);
    }
    else if (mode == 10)
    {
        float p = frac(0.0025 * t + frac(x * 0.0071 + z * 0.0053));
        return 0.15 + 0.85 * max(exp(-6.0 * p), FishGlowSmooth((p - 0.97) / 0.03));
    }
    else if (mode == 11)
    {
        float night = saturate(a.z);
        if (night <= 1e-4)
        {
            return 0.0;
        }
        float sunset = 0.5 + 0.5 * (1.0 - night);
        float progress = frac(a.y - sunset) / night;
        if (progress >= 1.0)
        {
            return 0.0;
        }
        return (1.0 - 0.85 * progress) * FishGlowSmooth(progress / 0.03) * FishGlowSmooth((1.0 - progress) / 0.03);
    }
    else if (mode == 12)
    {
        float wave = 0.5 + 0.5 * sin(FISH_GLOW_TURN * frac(0.08 * t - frac((x * d.z + z * d.w) / 244.0)));
        return 1.0 - 0.6 * lerp(0.25, 1.0, saturate(d.x)) * (1.0 - wave);
    }
    else if (mode == 13)
    {
        float f = 0.5 + 0.5 * cos(FISH_GLOW_TURN * frac(2.0 * b.z));
        return 0.25 + 0.75 * f * f * f;
    }
    else if (mode == 14)
    {
        float h = a.x / 0.09;
        return 0.08 + 0.92 * exp(-h * h);
    }
    else if (mode == 15)
    {
        return 0.15 + 0.85 * saturate(b.y);
    }
    else if (mode == 16)
    {
        return 0.25 + 0.75 * FishGlowSmooth(c.z * 2.0);
    }
    else if (mode == 17)
    {
        // Along the nearest stored line (I6i-b), else along a fixed bearing.
        if (!onLine)
        {
            along = x * 0.6 + z * 0.8;
        }
        float p = frac(0.001 * t - frac(along / 500.0)) - 0.5;
        return 0.15 + 0.85 * exp(-(p * 10.0) * (p * 10.0));
    }
    else if (mode == 18)
    {
        return 0.2 + 0.8 * saturate(a.z);
    }
    else if (mode == 20)
    {
        return 0.04 + 0.96 * (1.0 - FishGlowSmooth((b.x - 0.04) / 0.16));
    }
    else if (mode == 22)
    {
        // The nearest stored crack's bearing (I6i-b), else the smooth field's.
        if (!onLine)
        {
            float bearing = 3.14159265359 * (0.5 + 0.5 * sin(FISH_GLOW_TURN * frac(x * 0.0011 + z * 0.0007)));
            doubled = float2(cos(2.0 * bearing), sin(2.0 * bearing));
        }
        float stress = 0.5 + 0.5 * (_FishTidalStress.x * doubled.x + _FishTidalStress.y * doubled.y);
        return 0.2 + 0.8 * stress * stress;
    }
    else if (mode == 23)
    {
        return 0.3 + 0.7 * FishGlowSmooth(c.w * 1.5);
    }
    else if (mode == 24)
    {
        return saturate(_FishPlumeTide);
    }
    else if (mode == 25)
    {
        float tremble = 0.75 + 0.25 * sin(FISH_GLOW_TURN * frac(6.1 * t + frac(x * 0.21 + z * 0.15)));
        return 0.06 + 0.94 * saturate(d.y) * tremble;
    }
    else if (mode == 26)
    {
        return 0.12 + 0.88 * FishGlowEpisode(slots, ramp, t, originWS.xz);
    }
    else if (mode == 27)
    {
        return 0.1 + 0.9 * saturate(_FishVentBurst);
    }
    // 1 (and anything unknown): the pulse, without its dusk share.
    float slow = frac(0.11 * t + frac(x * 0.037 + z * 0.029));
    float slower = frac(0.043 * t + frac(x * -0.021 + z * 0.033));
    return 0.78 + 0.14 * sin(FISH_GLOW_TURN * slow) + 0.08 * sin(FISH_GLOW_TURN * slower);
}

/// <summary>An emission warmed (SceneGlowModes.Warmed): its luminance in firelight's colour.</summary>
half3 FishGlowWarmed(half3 emission, float warm)
{
    if (warm < 0.5)
    {
        return emission;
    }
    half l = dot(emission, half3(0.2126h, 0.7152h, 0.0722h));
    return l * half3(1.4756h, 0.9149h, 0.4427h);
}

/// <summary>
/// A source's entry under a piece's origin: true, with (mode, warm, every) and its episode schedule, when its biome names one.
/// source 0 way, 1 crystal.
/// </summary>
bool FishGlowEntryAt(float3 originWS, int source, out float4 entry, out float4 slots, out float ramp)
{
    entry = float4(0.0, 0.0, 0.0, 0.0);
    slots = float4(0.0, 0.0, 0.0, 0.0);
    ramp = 0.0;
    UNITY_BRANCH
    if (_FishGlowInfo.x < 0.5)
    {
        return false;
    }
    uint row = min(FishGlowAnchoredRow(originWS.xz, FishGlowRowAt(originWS.xz)), 63u);
    float4 rowEntry = _FishGlowRows[row];
    float packed = source == 0 ? rowEntry.x : rowEntry.y;
    if (packed < 0.5)
    {
        return false;
    }
    float every = floor(packed / 64.0);
    float rest = packed - 64.0 * every;
    float warm = rest >= 32.0 ? 1.0 : 0.0;
    entry = float4(rest - 32.0 * warm, warm, every, 0.0);
    float schedule = source == 0 ? rowEntry.z : rowEntry.w;
    if (schedule >= 0.0)
    {
        FishGlowSchedule((uint)schedule, slots, ramp);
    }
    return true;
}

/// <summary>
/// A way glow's part (_FishStructure 3) under its biome's mode: <paramref name="emission"/> is the part's unmoved emission, and
/// <paramref name="moved"/> (already the old pulse × dusk, FishStructureGlowLight) is replaced when the biome names a mode, else kept.
/// </summary>
void FishGlowModeWay(half3 emission, float3 originWS, half underground, inout half3 moved)
{
    float4 entry, slots;
    float ramp;
    if (!FishGlowEntryAt(originWS, 0, entry, slots, ramp))
    {
        return;
    }
    int mode = (int)round(entry.x);
    half shows = FishGlowOwnsDaylight(mode) ? 1.0h : max((half)saturate(_FishGlowLights), underground);
    // Default with the warm tint only: today's pulse, warmed.
    float value = mode == 0 ? FishStructureGlowLight(originWS, underground) : FishGlowModeValue(mode, entry.z, slots, ramp, originWS) * shows;
    moved = FishGlowWarmed(emission, entry.y) * (half)value;
}

/// <summary>
/// A glowing crystal (_FishStructure -1 flickering, -2 steady) under its biome's mode: as FishGlowModeWay, with no dusk share (a
/// crystal glows by day as by night, as it always has). Default keeps its own (flicker or steady), warmed when named.
/// </summary>
void FishGlowModeCrystal(half3 emission, float3 originWS, bool flickers, inout half3 moved)
{
    float4 entry, slots;
    float ramp;
    if (!FishGlowEntryAt(originWS, 1, entry, slots, ramp))
    {
        return;
    }
    int mode = (int)round(entry.x);
    float value = mode == 0 ? (flickers ? FishGlowFlicker(originWS) : 1.0) : FishGlowModeValue(mode, entry.z, slots, ramp, originWS);
    moved = FishGlowWarmed(emission, entry.y) * (half)value;
}

#endif
