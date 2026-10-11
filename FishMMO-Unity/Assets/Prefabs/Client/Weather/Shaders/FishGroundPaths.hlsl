// FishMMO: the scene's paths, as the terrain, the grass and the details read them.
//
// ScenePathField (C#) rasterises every routed road, track, footpath and trail into a half-metre distance field kept only
// in the 16 m pages a path passes through; ScenePathSurfaceBinder uploads it as _FishPathTable (a texel a page: its
// atlas column and row, a = 1 where stored) and _FishPathAtlas (34 x 34 texel pages: 32 and a border). A texel holds
//   r  the signed distance to the nearest path's edge, -8 m (inside) .. +3 m (outside)
//   g  that path's half width / 8 m
//   b  its wear, 0 grown over .. 1 bare
//   a  its surface: earth 0, cart track 1/3, gravel 2/3, stone 1.
//
// Plain HLSL (no SRP library), so the compute shaders can include it as they are.

#ifndef FISHMMO_GROUND_PATHS_INCLUDED
#define FISHMMO_GROUND_PATHS_INCLUDED

Texture2D<float4> _FishPathTable;
Texture2D<float4> _FishPathAtlas;
// A bilinear clamp sampler. The terrain lends its control maps' (samplers are scarce in its fragment stage); a compute
// shader takes this inline one.
#ifndef FISH_PATH_SAMPLER
SamplerState fishpath_linear_clamp_sampler;
#define FISH_PATH_SAMPLER fishpath_linear_clamp_sampler
#endif
// xy: world x/z of page (0, 0), z: 1 / page metres, w: 1 when any path is bound.
float4 _FishPathParams;
// xy: the page table's size (pages), z: 1 / the atlas's side (texels), w: stored texels a page side.
float4 _FishPathSize;
// 1: the terrain draws every path flat magenta (cyan where its surface slices are missing). Biome Tools → Diagnostics.
float _FishPathDebug;

#define FISH_PATH_PAGE_TEXELS 32.0
#define FISH_PATH_INSIDE 8.0
#define FISH_PATH_OUTSIDE 3.0
#define FISH_PATH_HALF_RANGE 8.0

struct FishPathSample
{
    // Metres to the nearest path's edge, negative inside it.
    float edge;
    float halfWidth;
    float wear;
    float surface;
    bool found;
};

/// The path field at a world point; found is false where no page is stored (no path within ~3 m).
FishPathSample FishPathAt(float2 xz)
{
    FishPathSample s;
    s.edge = FISH_PATH_OUTSIDE;
    s.halfWidth = 0.0;
    s.wear = 0.0;
    s.surface = 0.0;
    s.found = false;
    float2 pc = (xz - _FishPathParams.xy) * _FishPathParams.z;
    float2 ip = floor(pc);
    // One exit: early returns of a struct read as "potentially uninitialized" to Unity's Vulkan compile.
    if (_FishPathParams.w > 0.5 && ip.x >= 0.0 && ip.y >= 0.0 && ip.x < _FishPathSize.x && ip.y < _FishPathSize.y)
    {
        float4 entry = _FishPathTable.Load(int3((int2)ip, 0));
        if (entry.a >= 0.5)
        {
            // The page's corner in the atlas, past its border, then texels: texel t's centre is at t + 0.5.
            float2 base = round(entry.rg * 255.0) * _FishPathSize.w;
            float2 coordinate = base + 1.0 + (pc - ip) * FISH_PATH_PAGE_TEXELS;
            float4 t = _FishPathAtlas.SampleLevel(FISH_PATH_SAMPLER, coordinate * _FishPathSize.z, 0.0);
            s.edge = t.r * (FISH_PATH_INSIDE + FISH_PATH_OUTSIDE) - FISH_PATH_INSIDE;
            s.halfWidth = t.g * FISH_PATH_HALF_RANGE;
            s.wear = t.b;
            s.surface = t.a;
            s.found = true;
        }
    }
    return s;
}

float FishPathHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

/// Smooth value noise, 0 .. 1, a unit cell.
float FishPathNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = FishPathHash(i);
    float b = FishPathHash(i + float2(1.0, 0.0));
    float c = FishPathHash(i + float2(0.0, 1.0));
    float d = FishPathHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

/// How much a cart track (surface 1/3) the path is, 0 .. 1.
float FishPathTrackness(float surface) { return saturate(1.0 - abs(surface * 3.0 - 1.0)); }

/// The path's edge moved by its fraying: a kept way has a firm edge, a lost one wanders and breaks.
float FishPathFrayedEdge(FishPathSample p, float2 xz)
{
    float fray = (FishPathNoise(xz * 1.7) - 0.5) * lerp(0.55, 0.12, p.wear)
               + (FishPathNoise(xz * 0.35 + 17.3) - 0.5) * lerp(0.8, 0.2, p.wear);
    return p.edge + fray;
}

/// Where a path that is grown over has grass across it after all: patches, more of them the less it is walked.
float FishPathOvergrown(FishPathSample p, float2 xz)
{
    float patches = smoothstep(0.42, 0.72, FishPathNoise(xz * 0.55 + 41.0) * 0.7 + FishPathNoise(xz * 2.3 + 5.1) * 0.3);
    return patches * (1.0 - smoothstep(0.45, 0.95, p.wear));
}

/// The grass down a cart track's middle, between its ruts, 0 .. 1.
float FishPathMedian(FishPathSample p)
{
    float fromCentre = max(0.0, p.edge + p.halfWidth);
    return FishPathTrackness(p.surface) * (1.0 - smoothstep(0.3, 0.48, fromCentre));
}

/// <summary>
/// What the path does to what grows at a point. trodden: how bare it wears the ground, 0 untouched .. 1 bare: what the
/// grass and the details are thinned by. A kept road is bare across its width; a trail barely walked keeps most of its
/// grass; a cart track keeps the grass between its ruts. flattened: how much shorter what still grows there stands
/// (trampled), 0 .. 1 — a lost trail reads by its low grass. One field sample serves both.
/// </summary>
void FishPathWearAt(float2 xz, out float trodden, out float flattened)
{
    trodden = 0.0;
    flattened = 0.0;
    FishPathSample p = FishPathAt(xz);
    if (!p.found || p.edge > 1.0)
    {
        return;
    }
    float edge = FishPathFrayedEdge(p, xz);
    float median = FishPathMedian(p);
    float inside = 1.0 - smoothstep(-0.12, 0.12, edge);
    trodden = inside * saturate(p.wear * 1.15) * (1.0 - FishPathOvergrown(p, xz)) * (1.0 - median);
    float within = 1.0 - smoothstep(-0.25, 0.35, edge);
    flattened = within * lerp(0.45, 0.8, saturate(p.wear)) * (1.0 - 0.6 * median);
}

/// The trodden share alone (FishPathWearAt).
float FishPathTrodden(float2 xz)
{
    float trodden, flattened;
    FishPathWearAt(xz, trodden, flattened);
    return trodden;
}

#endif
