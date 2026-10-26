#ifndef FISH_ROCKFALL_INCLUDED
#define FISH_ROCKFALL_INCLUDED

// FALLING STONE (2026-10-25, phase I2u; decisions 79, 95): the stones of the falls down the scene's stored chutes and the
// Strombolian bombs rolling down its sciara, placed per vertex from a few numbers a fall (RockfallField, FishMMO.Client), every
// stone's path hashed from its fall's seed. The C# twin is FishMMO.Shared.Weather.Rockfall (StoneOf, RunOf, AlongAt, SpeedAt, HopAt,
// AcrossAt, StateAt) and StromboliBombs.GlowAt: constant for constant, salt for salt (RockfallTests checks the mirror). Nothing here
// is simulated or stored: a fall's age on the shared clock is all a frame is handed.

// ── Rockfall's constants (Rockfall.cs) ──
#define ROCK_STAGGER_SECONDS 4.0
#define ROCK_REST_SECONDS 600.0
#define ROCK_SINK_SECONDS 60.0
#define ROCK_SCREE_LEAST 0.12
#define ROCK_SCREE_MOST 0.6
#define ROCK_SCIARA_LEAST 0.2
#define ROCK_SCIARA_MOST 0.8
#define ROCK_LEAST_PEAK 6.0
#define ROCK_MOST_PEAK 15.0
#define ROCK_LEAST_ACCEL 0.25
#define ROCK_MOST_ACCEL 0.45
#define ROCK_LEAST_DECEL 0.6
#define ROCK_MOST_DECEL 1.0
#define ROCK_LEAST_STOP 0.62
#define ROCK_MOST_STOP 1.0
#define ROCK_LEAST_HOP 3.0
#define ROCK_MOST_HOP 9.0
#define ROCK_LEAST_HOP_HEIGHT 0.15
#define ROCK_MOST_HOP_HEIGHT 1.2
#define ROCK_SCREE_ACROSS 0.5
#define ROCK_SCIARA_ACROSS 0.7
#define ROCK_LEAST_WOBBLE 12.0
#define ROCK_MOST_WOBBLE 40.0
#define ROCK_SALT_SIZE 0x51E0u
#define ROCK_SALT_DELAY 0xDE1Au
#define ROCK_SALT_ACROSS 0xAC05u
#define ROCK_SALT_PEAK 0x9EA6u
#define ROCK_SALT_ACCEL 0xACCEu
#define ROCK_SALT_DECEL 0xDECEu
#define ROCK_SALT_STOP 0x5709u
#define ROCK_SALT_HOP 0x4099u
#define ROCK_SALT_HOP_HEIGHT 0x40E1u
#define ROCK_SALT_WOBBLE 0x30B1u
#define ROCK_SALT_PHASE 0x9A5Eu
// StromboliBombs' cooling (s).
#define BOMB_COOL_SECONDS 60.0
#define BOMB_REST_SECONDS 240.0
#define BOMB_SINK_SECONDS 30.0

// ── Per draw (RockfallField) ──
#define ROCK_MAX_POINTS 512
#define ROCK_MAX_CHUTES 16
#define ROCK_MAX_FALLS 64
#define ROCK_MAX_BOMBS 256
#define ROCK_MAX_BURSTS 8
#define ROCK_STONES_PER_FALL 12
#define ROCK_MAX_CONES 16

// The near chutes' points: xyz on the ground, w metres along its chute.
float4 _RockPoints[ROCK_MAX_POINTS];
// Per chute: x its first point, y its point count, z its length (m), w its half width (m).
float4 _RockChutes[ROCK_MAX_CHUTES];
// Per chute: x its kind (0 scree, 1 sciara), y its scar's freshness (0..1), z its fade with distance (0..1), w unused.
float4 _RockChutesB[ROCK_MAX_CHUTES];
// Per fall: x its chute, y its age (s), z its seed (24 bits), w its stones (0: an empty slot).
float4 _RockFalls[ROCK_MAX_FALLS];
// Per bomb: xyz its launch velocity (m/s), w seconds after its burst's start it leaves the vent.
float4 _RockBombs[ROCK_MAX_BOMBS];
// Per bomb: x its burst + its radius (the fraction, m), y its chute (−1 none), z where it lands along it (m), w its offset across (m).
float4 _RockBombsB[ROCK_MAX_BOMBS];
// Per bomb: x its flight (s), y its roll's seed (24 bits: Rockfall.StoneOf stone 0 down a sciara gives its speeds), z the metres it
// rolls on from where it lands (0: it lies where it lands), w unused.
float4 _RockBombsC[ROCK_MAX_BOMBS];
// Per burst: xyz its vent, w seconds since the burst started.
float4 _RockBursts[ROCK_MAX_BURSTS];
// Per burst: x its strength, yzw unused.
float4 _RockBurstsB[ROCK_MAX_BURSTS];
// x falls, y bombs, z the air (0 none … 1 full: dust and breath), w gravity (m/s²).
float4 _RockInfo;
// Per talus cone: xyz its apex, w its summer ice (0..1).
float4 _RockCones[ROCK_MAX_CONES];
// Per talus cone: xyz its toe, w its winter steam (0..1).
float4 _RockConesB[ROCK_MAX_CONES];
// Per talus cone: x its spread at the toe (m), y its seed (24 bits), z its fade with distance, w unused.
float4 _RockConesC[ROCK_MAX_CONES];
// x the cones, y the motion clock wrapped at 10 000 s, z the wind's x (m/s), w the wind's z (m/s).
float4 _RockConeInfo;

// ── Hashes (TurbidityEpisodes.Mix / Unit) ──

uint RockMix(uint v, uint seed)
{
    uint h = (v * 0x9E3779B1u) ^ seed;
    h ^= h >> 16;
    h *= 0x7FEB352Du;
    h ^= h >> 15;
    h *= 0x846CA68Bu;
    h ^= h >> 16;
    return h;
}

float RockUnitOf(uint h)
{
    return (float)(h >> 8) * (1.0 / 16777216.0);
}

// Rockfall.U: one stone's (index i) draw of one property (its salt).
float RockU(uint seed, uint i, uint salt)
{
    return RockUnitOf(RockMix((i * 0x9E3779B1u) ^ salt, seed));
}

// ── A stone (Rockfall.StoneOf) ──

struct RockStone
{
    float size;
    float rank;
    float delay;
    float across;
    float peak;
    float accel;
    float decel;
    float stop;
    float hopLength;
    float hopHeight;
    float wobble;
    float wobbleLength;
    float phase;
};

RockStone RockStoneOf(uint seed, uint i, bool sciara, float length, float half, float gravity)
{
    RockStone s;
    float g = max(0.5, gravity);
    float rank = RockU(seed, i, ROCK_SALT_SIZE);
    rank *= rank;
    s.rank = rank;
    s.size = sciara ? lerp(ROCK_SCIARA_LEAST, ROCK_SCIARA_MOST, rank) : lerp(ROCK_SCREE_LEAST, ROCK_SCREE_MOST, rank);
    s.delay = i == 0u ? 0.0 : RockU(seed, i, ROCK_SALT_DELAY) * ROCK_STAGGER_SECONDS;
    float acrossShare = sciara ? ROCK_SCIARA_ACROSS : ROCK_SCREE_ACROSS;
    s.across = (RockU(seed, i, ROCK_SALT_ACROSS) * 2.0 - 1.0) * acrossShare * half;
    s.peak = lerp(ROCK_LEAST_PEAK, ROCK_MOST_PEAK, RockU(seed, i, ROCK_SALT_PEAK));
    s.accel = g * lerp(ROCK_LEAST_ACCEL, ROCK_MOST_ACCEL, RockU(seed, i, ROCK_SALT_ACCEL));
    s.decel = g * lerp(ROCK_LEAST_DECEL, ROCK_MOST_DECEL, RockU(seed, i, ROCK_SALT_DECEL));
    s.stop = max(0.0, length) * lerp(ROCK_LEAST_STOP, ROCK_MOST_STOP, 0.6 * rank + 0.4 * RockU(seed, i, ROCK_SALT_STOP));
    s.hopLength = lerp(ROCK_LEAST_HOP, ROCK_MOST_HOP, RockU(seed, i, ROCK_SALT_HOP));
    s.hopHeight = lerp(ROCK_LEAST_HOP_HEIGHT, ROCK_MOST_HOP_HEIGHT, RockU(seed, i, ROCK_SALT_HOP_HEIGHT)) * (0.5 + rank);
    s.wobbleLength = lerp(ROCK_LEAST_WOBBLE, ROCK_MOST_WOBBLE, RockU(seed, i, ROCK_SALT_WOBBLE));
    s.phase = RockU(seed, i, ROCK_SALT_PHASE) * 6.2831853;
    s.wobble = 0.25 * acrossShare * half * RockU(seed, i, ROCK_SALT_WOBBLE ^ 0x77u);
    return s;
}

// ── A run (Rockfall.RunOf / AlongAt / SpeedAt) ──

struct RockRun
{
    float peak;
    float accel;
    float decel;
    float stop;
    float t1;
    float t2;
    float t3;
    float s1;
    float s2;
};

RockRun RockRunOf(float stop, float peak, float accel, float decel)
{
    RockRun r;
    r.stop = max(0.0, stop);
    r.accel = max(1e-3, accel);
    r.decel = max(1e-3, decel);
    r.peak = max(1e-3, peak);
    float s1 = r.peak * r.peak / (2.0 * r.accel);
    float sd = r.peak * r.peak / (2.0 * r.decel);
    if (s1 + sd > r.stop)
    {
        r.peak = sqrt(max(1e-6, 2.0 * r.stop * r.accel * r.decel / (r.accel + r.decel)));
        s1 = r.peak * r.peak / (2.0 * r.accel);
        sd = r.peak * r.peak / (2.0 * r.decel);
    }
    r.t1 = r.peak / r.accel;
    r.s1 = s1;
    r.s2 = max(s1, r.stop - sd);
    r.t2 = (r.s2 - s1) / r.peak;
    r.t3 = r.peak / r.decel;
    return r;
}

float RockRunTotal(RockRun r)
{
    return r.t1 + r.t2 + r.t3;
}

float RockAlongAt(RockRun r, float t)
{
    if (t <= 0.0)
    {
        return 0.0;
    }
    if (t < r.t1)
    {
        return 0.5 * r.accel * t * t;
    }
    if (t < r.t1 + r.t2)
    {
        return r.s1 + r.peak * (t - r.t1);
    }
    float u = min(t - r.t1 - r.t2, r.t3);
    return min(r.stop, r.s2 + r.peak * u - 0.5 * r.decel * u * u);
}

float RockSpeedAt(RockRun r, float t)
{
    if (t <= 0.0 || t >= RockRunTotal(r))
    {
        return 0.0;
    }
    if (t < r.t1)
    {
        return r.accel * t;
    }
    if (t < r.t1 + r.t2)
    {
        return r.peak;
    }
    return max(0.0, r.peak - r.decel * (t - r.t1 - r.t2));
}

float RockHopAt(RockStone s, float along, float speed, float peak)
{
    return s.hopHeight * saturate(speed / max(1e-3, peak)) * abs(sin(3.14159265 * along / max(0.1, s.hopLength)));
}

float RockAcrossAt(RockStone s, float along)
{
    return s.across + s.wobble * sin(along / max(1.0, s.wobbleLength) * 6.2831853 + s.phase);
}

// Rockfall.StateAt: x metres down the chute, y off its line (m), z above it (m), w how much of it shows; t = its own seconds since
// the fall started (its delay not yet taken off); `rolled` how far it has turned (m of travel, for its roll).
float4 RockStoneState(RockStone s, float age)
{
    RockRun run = RockRunOf(s.stop, s.peak, s.accel, s.decel);
    float t = age - s.delay;
    float along = RockAlongAt(run, t);
    float speed = RockSpeedAt(run, t);
    float rest = t - RockRunTotal(run);
    float shown = t <= 0.0 ? 0.0 : 1.0 - saturate((rest - ROCK_REST_SECONDS) / ROCK_SINK_SECONDS);
    return float4(along, RockAcrossAt(s, along), RockHopAt(s, along, speed, run.peak), shown);
}

// StromboliBombs.GlowAt.
float RockBombGlow(float sinceLaunch)
{
    if (sinceLaunch < 0.0)
    {
        return 0.0;
    }
    float c = saturate(1.0 - sinceLaunch / BOMB_COOL_SECONDS);
    return c * c;
}

// ── A chute's line ──

// The point `along` metres down chute `c` (clamped to its ends): xyz on the ground; `direction` its downhill heading there (xz, unit).
float3 RockChutePoint(uint c, float along, out float2 direction)
{
    float4 info = _RockChutes[c];
    int first = (int)info.x;
    int count = max(2, (int)info.y);
    int lo = 0;
    int hi = count - 1;
    // The last point at or before `along` (the points' w climbs from 0 to the length).
    [loop]
    for (int it = 0; it < 9 && hi - lo > 1; it++)
    {
        int mid = (lo + hi) >> 1;
        if (_RockPoints[first + mid].w <= along)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }
    float4 a = _RockPoints[first + lo];
    float4 b = _RockPoints[first + min(lo + 1, count - 1)];
    float span = max(1e-3, b.w - a.w);
    float f = saturate((along - a.w) / span);
    float2 d = b.xz - a.xz;
    direction = dot(d, d) > 1e-8 ? normalize(d) : float2(1.0, 0.0);
    return lerp(a.xyz, b.xyz, f);
}

#endif
