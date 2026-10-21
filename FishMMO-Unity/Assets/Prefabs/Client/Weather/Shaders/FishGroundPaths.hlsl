// FishMMO: the scene's paths, as the terrain, the grass and the details read them.
//
// ScenePathField (C#) rasterises every routed road, track, footpath and trail into a half-metre distance field kept only
// in the 16 m pages a path passes through; ScenePathSurfaceBinder uploads it as _FishPathTable (a texel a page: its
// atlas column and row, a = 1 where stored) and _FishPathAtlas (34 x 34 texel pages: 32 and a border). A texel holds
//   r  the signed distance to the nearest path's edge, -8 m (inside) .. +3 m (outside)
//   g  that path's half width / 8 m
//   b  its wear, 0 grown over .. 1 bare
//   a  its surface: cart track 0, earth 1/3, gravel 2/3, stone 1 (ScenePathStyle.SurfaceCode; the first bake differed,
//      FishPathSurfaceCode). Where ways meet, r is the nearer edge and g, b, a a blend of the ways near it.
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
// xy: world x/z of page (0, 0), z: 1 / page metres, w: the field's encoding when any path is bound (ScenePathField.Encoding), else 0.
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

/// <summary>
/// A baked surface code in this encoding (track 0, earth 1/3, gravel 2/3, stone 1). The first bake (_FishPathParams.w 1)
/// coded earth 0 and the track 1/3, which put the track between earth and gravel: a blend across a junction passed through
/// it. Read on until the scene is cut again.
/// </summary>
float FishPathSurfaceCode(float baked)
{
    float code = baked;
    if (_FishPathParams.w < 1.5)
    {
        code = baked < 1.0 / 3.0 ? 1.0 / 3.0 - baked : (baked < 2.0 / 3.0 ? 2.0 * (baked - 1.0 / 3.0) : baked);
    }
    return code;
}

/// <summary>
/// Breaks up a blend between two surfaces into patches: where two ways meet the field mixes their codes smoothly
/// (ScenePathField.BlendMetres), and an even fade of gravel into earth read as a smear laid along a line. Here each
/// surface holds where a noise field stands over its share, so the trail's earth runs on into the road in tongues and
/// the road's grit scatters out along the trail. A clean code (a way on its own) is left exactly as it is; the blend
/// stays between the two surfaces it is of.
/// </summary>
float FishPathSeam(float surface, float2 xz)
{
    float code = surface * 3.0;
    float lower = floor(min(code, 2.999));
    float share = code - lower;
    float n = 0.3 + 0.4 * (FishPathNoise(xz * 0.9 + 71.0) * 0.65 + FishPathNoise(xz * 3.3 + 13.0) * 0.35);
    return (lower + smoothstep(n - 0.3, n + 0.3, share)) / 3.0;
}

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
            s.surface = FishPathSeam(FishPathSurfaceCode(t.a), xz);
            s.found = true;
        }
    }
    return s;
}

/// How much a cart track (surface 0) the path is, 0 .. 1.
float FishPathTrackness(float surface) { return saturate(1.0 - surface * 3.0); }

/// <summary>
/// The path's edge moved by its fraying: a lost way wanders and breaks; a kept one wanders less, but never runs ruler
/// straight. Floored at wear 1 to about a third of a metre either way (and more on a wide street), where it had been
/// a sixth: every town street was a band cut to its plan with a knife (2026-10-13: "they are 100% opacity").
/// </summary>
float FishPathFrayedEdge(FishPathSample p, float2 xz)
{
    float wide = saturate(p.halfWidth / 4.0);
    float fray = (FishPathNoise(xz * 1.7) - 0.5) * lerp(0.55, 0.3, p.wear)
               + (FishPathNoise(xz * 0.35 + 17.3) - 0.5) * lerp(0.8, 0.45 + 0.35 * wide, p.wear)
               + (FishPathNoise(xz * 0.11 + 3.7) - 0.5) * (0.4 + 0.6 * wide);
    return p.edge + fray;
}

/// <summary>
/// The width over which a path's edge breaks up into the ground beside it, metres: the ground creeping in over its
/// margin in patches, and its stone or grit scattering out into the verge. Wider on a wide street.
/// </summary>
float FishPathEdgeBand(FishPathSample p) { return 0.45 + 0.2 * clamp(p.halfWidth, 0.0, 4.5); }

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
/// How much of the ground at a point the path wears bare, 0 .. 1: the share the terrain draws with the path's own ground
/// (FishApplyPaths), and so what the grass and the details are cleared by (FishPathWearAt). One function for both, so
/// nothing grows where the ground is drawn bare: the grass used to thin by its own curve of the wear, which left a
/// quarter of the blades standing on a camp's yard (wear 0.64) that the terrain drew as plain earth.
/// </summary>
/// <param name="soft">Half the width over which the frayed edge fades, metres.</param>
float FishPathBare(FishPathSample p, float2 xz, float soft)
{
    float wear = saturate(p.wear);
    float edge = FishPathFrayedEdge(p, xz);
    /* Not a band of even fade but a broken one: across the edge band the path's surface holds where a noise field stands
     * over how far in from the edge the point is, so the ground runs in over its margin in patches and bits of it lie
     * out past its line, as gravel and stone thin into the grass at a real road's side. */
    float band = FishPathEdgeBand(p);
    float within = saturate((-edge + 0.35 * band) / band);
    float breakup = FishPathNoise(xz * 2.3 + 11.0) * 0.6 + FishPathNoise(xz * 6.1 + 2.0) * 0.4;
    float spread = 0.18 + soft / band;
    float cover = smoothstep(breakup - spread, breakup + spread, within);
    cover *= 1.0 - FishPathOvergrown(p, xz) * 0.92;
    cover *= 1.0 - FishPathMedian(p) * 0.85;
    // Barely walked: the earth shows through in a broken scatter rather than a band.
    cover *= lerp(0.55 + 0.45 * FishPathNoise(xz * 3.1 + 9.0), 1.0, wear);
    // And never a slab even where it is kept: the ground shows through its middle in broad, faint patches.
    cover *= 1.0 - 0.22 * smoothstep(0.5, 0.85, FishPathNoise(xz * 0.45 + 23.0));
    return cover;
}

/// <summary>
/// What the path does to what grows at a point. trodden: the chance a blade or a detail is not there, 0 .. 1, from the
/// bare share the terrain draws (FishPathBare). A kept road or a building's yard is bare across its width; a lost trail
/// is broken by the patches it has grown over, and grass stands in them; a cart track keeps the grass between its ruts.
/// flattened: how much shorter what still grows there stands (trampled), 0 .. 1 — a lost trail reads by its low grass.
/// One field sample serves both.
/// </summary>
/// <remarks>
/// The bare share is not taken as the chance itself: earth drawn at 85 % over the ground reads as earth, but 15 % of a
/// thick sward left standing on it reads as grass. So ground drawn mostly bare grows nothing, and the grass comes back
/// only as the drawing fades — over a soft edge wider than the paint's, so the sward thins in instead of stopping at a
/// cut line.
/// </remarks>
void FishPathWearAt(float2 xz, out float trodden, out float flattened)
{
    trodden = 0.0;
    flattened = 0.0;
    FishPathSample p = FishPathAt(xz);
    // Past the furthest the frayed, broken edge reaches (FishPathFrayedEdge, FishPathEdgeBand).
    if (!p.found || p.edge > 2.0)
    {
        return;
    }
    float bare = FishPathBare(p, xz, 0.4);
    trodden = smoothstep(0.15, 0.8, bare);
    float edge = FishPathFrayedEdge(p, xz);
    float median = FishPathMedian(p);
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
