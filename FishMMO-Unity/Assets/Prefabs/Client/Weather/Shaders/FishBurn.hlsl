#ifndef FISHMMO_BURN_INCLUDED
#define FISHMMO_BURN_INCLUDED

// The burn mosaic (2026-10-25, phase I2t, decisions 75, 82, 84): a biome's veld fires and brimstone flames, a pure function
// of the shared clock, the world's season and the scene (FishMMO.Shared.Weather.BurnMosaic is the C# twin, constant for
// constant; BurnMosaicTable publishes the globals, client only). Presentation only: no damage, nothing on the wire.
//
// Veld (Annual, Occasional): the scene is tiled into FISH_BURN_CELL cells, each the Voronoi region of a jittered ignition
// point hashed per thermal year; a cell that burns that year is lit at a hashed moment in its regime's window and its front
// reaches a point at lit + (r - e·along) / v (an ellipse with the ignition at a focus, the head running downwind). Behind it:
// a thin flame line, embers, black stubble, the green flush, the fire lilies, until the scar fades.
// Brimstone: blue sulphur flames from dusk to dawn over seeps and the vents' lips, breathable worlds only (the table publishes
// no Brimstone row elsewhere).
//
// Needs FishFlora.hlsl first (its hashes, noise and the biome grid). Plain HLSL, no Core RP macros: the grass compute includes
// it. Off (_FishBurnInfo.x = 0) unless a biome on the scene burns: every caller then draws exactly as before.

#define FISH_BURN_NONE 0u
#define FISH_BURN_OCCASIONAL 1u
#define FISH_BURN_ANNUAL 2u
#define FISH_BURN_BRIMSTONE 3u
#define FISH_BURN_ROWS 64u
#define FISH_BURN_VENTS 16

// BurnMosaic's constants.
#define FISH_BURN_CELL 400.0
#define FISH_BURN_JITTER 0.7
#define FISH_BURN_ECCENTRICITY 0.6
#define FISH_BURN_MIN_SPEED (0.5 / 60.0)
#define FISH_BURN_MAX_SPEED (2.0 / 60.0)
#define FISH_BURN_RAGGED 0.3
#define FISH_BURN_RAGGED_METRES 35.0
#define FISH_BURN_FLAME_SECONDS 240.0
#define FISH_BURN_EMBER_SECONDS 1800.0
#define FISH_BURN_CHAR_DAYS 3.0
#define FISH_BURN_FLUSH_DAYS 21.0
#define FISH_BURN_SCAR_DAYS 45.0
#define FISH_BURN_GREEN_DAYS 75.0
#define FISH_BURN_LILY_FROM_DAYS 7.0
#define FISH_BURN_LILY_TO_DAYS 28.0
#define FISH_BURN_STUBBLE 0.1
#define FISH_BURN_YEAR_DAYS 365.0
#define FISH_BURN_EMERALD_TONE (3.0 / 7.0)
#define FISH_BURN_ANNUAL_CHANCE 0.6
#define FISH_BURN_ANNUAL_FROM 0.10
#define FISH_BURN_ANNUAL_TO 0.30
#define FISH_BURN_OCCASIONAL_CHANCE 0.2
#define FISH_BURN_OCCASIONAL_FROM 0.18
#define FISH_BURN_OCCASIONAL_TO 0.30
#define FISH_BURN_SEEP_METRES 20.0
#define FISH_BURN_SEEP_CHANCE 0.18
#define FISH_BURN_SEEP_MIN_RADIUS 1.5
#define FISH_BURN_SEEP_MAX_RADIUS 4.0
#define FISH_BURN_FLICKER_WRAP 10000.0
#define FISH_BURN_FLICKER_CYCLES_A 37000.0
#define FISH_BURN_FLICKER_CYCLES_B 13000.0
#define FISH_BURN_SALT_CELL 0xB5E1A7C3u
#define FISH_BURN_SALT_RAGGED 0x2F9D3E61u
#define FISH_BURN_SALT_SEEP 0x7C1D9E35u

float4 _FishBurnInfo;                    // x 1 when on, y 1 when a veld regime is live this frame, z 1 when a Brimstone row is, w vents
float4 _FishBurnRows[16];                // each flora row's regime, four rows a vector (row r at [r >> 2][r & 3])
float4 _FishBurnClock;                   // x the thermal year mod 65536, y seconds into it, z its length, w 1 this year may show + 2 last year may
float4 _FishBurnWind;                    // xy the scene's prevailing downwind way (unit), z the scene's seed (24 bits), w night (0..1)
float4 _FishBurnMotion;                  // x the motion clock wrapped at FISH_BURN_FLICKER_WRAP
float4 _FishBurnVents[FISH_BURN_VENTS];  // the nearest brimstone vents: xy world xz, z lip radius (m), w strength

struct FishBurnStage
{
    float flame;    // the flame line
    float ember;    // the embers behind it
    float scar;     // the burnt-veld tint
    float regrow;   // the blades' height against their own
    float tone;     // blade colour: charcoal 1 → emerald FISH_BURN_EMERALD_TONE → own 0
    float lily;     // the share of after-burn flowers standing
};

FishBurnStage FishBurnUntouched()
{
    FishBurnStage s;
    s.flame = 0.0;
    s.ember = 0.0;
    s.scar = 0.0;
    s.regrow = 1.0;
    s.tone = 0.0;
    s.lily = 0.0;
    return s;
}

// The regime of the biome under xz (the flora grid, jittered at its borders as the grass's looks are).
uint FishBurnRegimeAt(float2 xz)
{
    if (_FishBurnInfo.x < 0.5 || _FishFloraInfo.x < 0.5)
    {
        return FISH_BURN_NONE;
    }
    uint row = min(FishFloraBiomeAt(xz), FISH_BURN_ROWS - 1u);
    return (uint)round(_FishBurnRows[row >> 2][row & 3u]);
}

uint FishBurnYearSalt(uint year, uint seed)
{
    return (year & 0xFFFFu) * 0x9E3779B9u ^ seed * 0x85EBCA6Bu ^ FISH_BURN_SALT_CELL;
}

// Seconds since the front of one year's fire reached xz, at `now` seconds into that year; -1e30 when its cell does not burn.
float FishBurnArrivalAge(float2 xz, uint year, float now, uint seed, uint regime)
{
    int2 g = (int2)floor(xz / FISH_BURN_CELL);
    uint salt = FishBurnYearSalt(year, seed);
    float best = 1e30;
    uint owner = 0u;
    float2 ignition = float2(0.0, 0.0);
    [unroll] for (int j = -1; j <= 1; j++)
    {
        [unroll] for (int i = -1; i <= 1; i++)
        {
            int2 c = g + int2(i, j);
            uint h = FishFloraCellHash(c, salt);
            float2 p = (float2(c) + 0.5 * (1.0 - FISH_BURN_JITTER) + FISH_BURN_JITTER * float2(FishFloraUnit(h ^ 4u), FishFloraUnit(h ^ 5u))) * FISH_BURN_CELL;
            float2 d = xz - p;
            float d2 = dot(d, d);
            if (d2 < best)
            {
                best = d2;
                owner = h;
                ignition = p;
            }
        }
    }
    bool occasional = regime == FISH_BURN_OCCASIONAL;
    float chance = occasional ? FISH_BURN_OCCASIONAL_CHANCE : FISH_BURN_ANNUAL_CHANCE;
    float age = -1e30;
    if (FishFloraUnit(owner ^ 1u) < chance)
    {
        float from = occasional ? FISH_BURN_OCCASIONAL_FROM : FISH_BURN_ANNUAL_FROM;
        float to = occasional ? FISH_BURN_OCCASIONAL_TO : FISH_BURN_ANNUAL_TO;
        float2 o = xz - ignition;
        float r = length(o) * (1.0 + FISH_BURN_RAGGED * (FishFloraNoise(xz / FISH_BURN_RAGGED_METRES, FISH_BURN_SALT_RAGGED ^ seed) - 0.5));
        float along = dot(o, _FishBurnWind.xy);
        float lit = (from + (to - from) * FishFloraUnit(owner ^ 2u)) * _FishBurnClock.z;
        float speed = FISH_BURN_MIN_SPEED + (FISH_BURN_MAX_SPEED - FISH_BURN_MIN_SPEED) * FishFloraUnit(owner ^ 3u);
        age = now - (lit + (r - FISH_BURN_ECCENTRICITY * along) / speed);
    }
    return age;
}

// Seconds since a fire last passed xz (this year's once its front has gone by, else last year's), -1 never. The years the
// clock says cannot show anything (before the window opens, after every scar has faded) are skipped: BurnMosaic.LiveYears.
float FishBurnAge(float2 xz, uint regime)
{
    uint live = (uint)round(_FishBurnClock.w);
    uint year = (uint)round(_FishBurnClock.x);
    uint seed = (uint)round(_FishBurnWind.z);
    float age = -1.0;
    [branch] if ((live & 1u) != 0u)
    {
        float now = FishBurnArrivalAge(xz, year, _FishBurnClock.y, seed, regime);
        age = now >= 0.0 ? now : -1.0;
    }
    [branch] if (age < 0.0 && (live & 2u) != 0u)
    {
        float last = FishBurnArrivalAge(xz, (year + 65535u) & 0xFFFFu, _FishBurnClock.y + _FishBurnClock.z, seed, regime);
        age = last >= 0.0 ? last : -1.0;
    }
    return age;
}

// BurnMosaic.StageOf.
FishBurnStage FishBurnStageOf(float ageSeconds)
{
    FishBurnStage s = FishBurnUntouched();
    float yearSeconds = _FishBurnClock.z;
    if (ageSeconds >= 0.0 && yearSeconds > 0.0)
    {
        float days = ageSeconds / (yearSeconds / FISH_BURN_YEAR_DAYS);
        s.flame = ageSeconds < FISH_BURN_FLAME_SECONDS ? 1.0 - ageSeconds / FISH_BURN_FLAME_SECONDS : 0.0;
        s.ember = ageSeconds < FISH_BURN_EMBER_SECONDS ? 1.0 - ageSeconds / FISH_BURN_EMBER_SECONDS : 0.0;
        float flush = smoothstep(FISH_BURN_CHAR_DAYS, FISH_BURN_CHAR_DAYS + FISH_BURN_FLUSH_DAYS, days);
        s.scar = 1.0 - smoothstep(FISH_BURN_CHAR_DAYS, FISH_BURN_SCAR_DAYS, days);
        s.regrow = FISH_BURN_STUBBLE + (1.0 - FISH_BURN_STUBBLE) * flush;
        s.tone = days < FISH_BURN_CHAR_DAYS + FISH_BURN_FLUSH_DAYS ? 1.0 + (FISH_BURN_EMERALD_TONE - 1.0) * flush
            : FISH_BURN_EMERALD_TONE * (1.0 - smoothstep(FISH_BURN_CHAR_DAYS + FISH_BURN_FLUSH_DAYS, FISH_BURN_GREEN_DAYS, days));
        s.lily = smoothstep(FISH_BURN_LILY_FROM_DAYS, FISH_BURN_LILY_FROM_DAYS + 2.0, days)
            * (1.0 - smoothstep(FISH_BURN_LILY_TO_DAYS - 4.0, FISH_BURN_LILY_TO_DAYS, days));
    }
    return s;
}

// The veld's stage at xz: untouched unless a veld regime is live and the biome there burns.
FishBurnStage FishBurnVeldAt(float2 xz)
{
    FishBurnStage s = FishBurnUntouched();
    [branch] if (_FishBurnInfo.y > 0.5)
    {
        uint regime = FishBurnRegimeAt(xz);
        [branch] if (regime == FISH_BURN_ANNUAL || regime == FISH_BURN_OCCASIONAL)
        {
            s = FishBurnStageOf(FishBurnAge(xz, regime));
        }
    }
    return s;
}

// BurnMosaic.Flicker.
float FishBurnFlicker(float t, float phase)
{
    float u = t / FISH_BURN_FLICKER_WRAP;
    float a = sin(6.2831853 * (FISH_BURN_FLICKER_CYCLES_A * u + phase));
    float b = sin(6.2831853 * (FISH_BURN_FLICKER_CYCLES_B * u + 0.37 + 1.7 * phase));
    return 0.65 + 0.35 * (0.5 * a + 0.5 * b);
}

// The blue fire at xz, 0..1: BurnMosaic.SeepAt plus the nearest vents' lips. Only where the biome's regime is Brimstone.
float FishBurnBrimstoneAt(float2 xz)
{
    float fire = 0.0;
    float night = _FishBurnWind.w;
    [branch] if (_FishBurnInfo.z > 0.5 && night > 0.0 && FishBurnRegimeAt(xz) == FISH_BURN_BRIMSTONE)
    {
        uint seed = (uint)round(_FishBurnWind.z);
        float t = _FishBurnMotion.x;
        int2 g = (int2)floor(xz / FISH_BURN_SEEP_METRES);
        uint h = FishFloraCellHash(g, FISH_BURN_SALT_SEEP ^ seed);
        if (FishFloraUnit(h ^ 1u) < FISH_BURN_SEEP_CHANCE)
        {
            float2 c = (float2(g) + 0.25 + 0.5 * float2(FishFloraUnit(h ^ 2u), FishFloraUnit(h ^ 3u))) * FISH_BURN_SEEP_METRES;
            float radius = FISH_BURN_SEEP_MIN_RADIUS + (FISH_BURN_SEEP_MAX_RADIUS - FISH_BURN_SEEP_MIN_RADIUS) * FishFloraUnit(h ^ 4u);
            float2 d = xz - c;
            float q = 1.0 - dot(d, d) / (radius * radius);
            if (q > 0.0)
            {
                fire = night * q * q * FishBurnFlicker(t, FishFloraUnit(h ^ 5u));
            }
        }
        int vents = (int)min(_FishBurnInfo.w, (float)FISH_BURN_VENTS);
        [loop] for (int k = 0; k < vents; k++)
        {
            float4 v = _FishBurnVents[k];
            float2 d = xz - v.xy;
            float q = 1.0 - dot(d, d) / max(v.z * v.z, 1e-4);
            if (q > 0.0)
            {
                fire = max(fire, night * q * q * v.w * FishBurnFlicker(t, frac(0.6180339 * (float)(k + 1))));
            }
        }
    }
    return fire;
}

// The burn on the terrain (FishWeatherTerrainArrayPasses): the burnt-veld tint with white ash rings where the tussocks
// stood, the flame line and its embers, the brimstone's blue flames. `firebreak` 0..1 keeps it off (a way). Returns the
// emission to add after lighting.
float3 FishBurnGround(float3 positionWS, float3 normalWS, float firebreak, inout half3 albedo, inout half smoothness)
{
    float3 emission = float3(0.0, 0.0, 0.0);
    [branch] if (_FishBurnInfo.x > 0.5)
    {
        float2 xz = positionWS.xz;
        // Fire runs through the grass: not up cliffs, not across a way.
        float gate = saturate((normalWS.y - 0.6) / 0.2) * (1.0 - saturate(firebreak));
        FishBurnStage s = FishBurnVeldAt(xz);
        [branch] if (gate > 0.0 && (s.scar > 0.0 || s.ember > 0.0))
        {
            // Ash rings: each burnt tussock stool leaves a pale ring (1.6 m cells, 0.25–0.45 m across).
            int2 cell = (int2)floor(xz / 1.6);
            uint h = FishFloraCellHash(cell, 0x41C64E6Du);
            float2 c = (float2(cell) + 0.2 + 0.6 * float2(FishFloraUnit(h ^ 1u), FishFloraUnit(h ^ 2u))) * 1.6;
            float ringR = 0.25 + 0.2 * FishFloraUnit(h ^ 3u);
            float ring = FishFloraUnit(h ^ 4u) < 0.55 ? saturate(1.0 - abs(length(xz - c) - ringR) / 0.06) : 0.0;
            float soot = FishFloraNoise(xz * 0.7, 0x5851F42Du);
            float3 burnt = lerp(float3(0.020, 0.018, 0.016), float3(0.045, 0.040, 0.034), soot);
            burnt = lerp(burnt, float3(0.40, 0.38, 0.35), ring * 0.8);
            float amount = s.scar * gate;
            albedo = (half3)lerp((float3)albedo, burnt, amount * 0.92);
            smoothness *= (half)(1.0 - 0.7 * amount);
            float flicker = FishBurnFlicker(_FishBurnMotion.x, FishFloraUnit(h ^ 5u));
            float spark = FishFloraNoise(xz * 2.3 + _FishBurnMotion.x * 0.05, 0x7A1C4E9Bu);
            emission += float3(3.2, 1.0, 0.18) * (s.flame * s.flame * flicker * gate);
            emission += float3(0.55, 0.07, 0.01) * (s.ember * s.ember * smoothstep(0.55, 0.85, spark) * gate);
        }
        float blue = FishBurnBrimstoneAt(xz);
        emission += float3(0.30, 0.55, 2.2) * blue;
    }
    return emission;
}

#endif
