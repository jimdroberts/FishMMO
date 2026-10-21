#ifndef FISH_HIZ_OCCLUSION_INCLUDED
#define FISH_HIZ_OCCLUSION_INCLUDED
// Moved out of FishTerrainInstancing.compute (2026-10-21) so the grass's compute culls by the same pyramid and rule.
// Plain HLSL: no Unity headers. The C# side binds it with FishDepthPyramid.Bind for each kernel that reads it.

// ── Occlusion (FishDepthPyramid) ──────────────────────────────────────
// The camera's depth last frame as linear eye depth, the FARTHEST of each block at every level, all levels in one
// atlas: level 0 (half the screen) at the origin, each level after it to its right, stacked downward. A prop whose
// nearest point lies behind the farthest depth over everything its sphere covered last frame was hidden behind
// what was drawn there — a hill, a rock, a stand of trees — and is not drawn in the main view (its shadow still is).
// Taken with last frame's camera, its sphere grown by how far the camera has moved since, so what a step reveals
// is drawn; anything off last frame's screen is drawn. No ground truth was culled: 1 in _FishHiZParams.w only when
// the pyramid is this camera's and fresh.
Texture2D<float> _FishHiZ;
float4 _FishHiZParams;          // x level 0's width, y its height, z levels, w 1 when the pyramid can be used
float4x4 _FishHiZViewProj;      // the GPU view-projection it was drawn with
float4 _FishHiZEye;             // xyz that camera's position, w how far to grow a sphere (m): the camera's move since
float4 _FishHiZForward;         // xyz that camera's forward, w 1 where texture rows start at the top (no Unity headers here)

// Each helper below has one exit: FXC/HLSLcc (Vulkan, GL, Metal) flags an early return out of a function with a
// loop in it as a "potentially uninitialized" result.

// Where a level sits in the atlas: level 0 at the origin, every later level in the column to its right.
int2 FishHiZOrigin(int level)
{
    int y = 0;
    int height = (int)_FishHiZParams.y;
    for (int k = 1; k < level; k++)
    {
        height = (height + 1) >> 1;
        y += height;
    }
    return int2(level > 0 ? (int)_FishHiZParams.x : 0, y);
}

int2 FishHiZBaseSize()
{
    return max(int2((int)_FishHiZParams.x, (int)_FishHiZParams.y), int2(1, 1));
}

int2 FishHiZSize(int level)
{
    int2 size = FishHiZBaseSize();
    for (int k = 0; k < level; k++)
    {
        size = (size + 1) >> 1;
    }
    return max(size, int2(1, 1));
}

bool FishOccluded(float3 centre, float radius)
{
    radius += _FishHiZEye.w;
    float nearest = dot(centre - _FishHiZEye.xyz, _FishHiZForward.xyz) - radius;
    bool occluded = false;
    if (_FishHiZParams.w >= 0.5 && nearest > 1.0)
    {
        bool inFront = true;
        float2 lo = float2(1e9, 1e9);
        float2 hi = float2(-1e9, -1e9);
        [unroll] for (int k = 0; k < 8; k++)
        {
            float3 corner = centre + radius * float3((k & 1) ? 1.0 : -1.0, (k & 2) ? 1.0 : -1.0, (k & 4) ? 1.0 : -1.0);
            float4 clip = mul(_FishHiZViewProj, float4(corner, 1.0));
            inFront = inFront && clip.w > 1e-4;
            float2 uv = clip.xy / max(clip.w, 1e-4) * 0.5 + 0.5;
            uv.y = _FishHiZForward.w > 0.5 ? 1.0 - uv.y : uv.y;
            lo = min(lo, uv);
            hi = max(hi, uv);
        }
        // Behind last frame's camera, or partly off its screen: nothing to say it was hidden.
        if (inFront && all(lo >= 0.0) && all(hi <= 1.0))
        {
            float2 base = _FishHiZParams.xy;
            float2 texels = (hi - lo) * base;
            int levels = (int)_FishHiZParams.z;
            int level = clamp((int)ceil(log2(max(1.0, max(texels.x, texels.y)))), 0, levels - 1);
            // A level-l texel covers 2^l of level 0's, so the rectangle spans two of them at most.
            int2 size = FishHiZSize(level);
            int2 a = min((int2)floor(lo * base) >> level, size - 1);
            int2 b = min((int2)floor(hi * base) >> level, size - 1);
            int2 origin = FishHiZOrigin(level);
            float farthest = max(max(_FishHiZ.Load(int3(origin + a, 0)), _FishHiZ.Load(int3(origin + int2(b.x, a.y), 0))),
                max(_FishHiZ.Load(int3(origin + int2(a.x, b.y), 0)), _FishHiZ.Load(int3(origin + b, 0))));
            occluded = nearest > farthest;
        }
    }
    return occluded;
}

#endif
