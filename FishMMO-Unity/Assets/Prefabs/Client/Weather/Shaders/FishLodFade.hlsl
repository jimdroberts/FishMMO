#ifndef FISHMMO_LOD_FADE_INCLUDED
#define FISHMMO_LOD_FADE_INCLUDED

// A per-instance LOD cross-fade for draws that Unity's LODGroup never sees — the terrain's instanced
// tree channels (TerrainTreeInstancing, FishMMO.Client/World/Terrain) draw trees, boulders,
// formations and ice with Graphics.RenderMeshInstanced, so unity_LODFade is never set for them.
//
// The contract:
//   _FishLodFade   float, one per instance, in its own instancing buffer (FishLodFadeProps), set by
//                  the renderer as a MaterialPropertyBlock float array named "_FishLodFade" with one
//                  entry per instance of the RenderMeshInstanced call (material.enableInstancing on).
//      0           fully drawn — the default, and what every draw that does not set it gets.
//      +f (0..1]   fading IN:  the share f of the pattern is drawn (+1 draws everything).
//      -f          fading OUT: exactly the complement of +f is drawn (-1 draws nothing).
//   The sign convention is unity_LODFade.x's. In a transition band the incoming level gets +f and the
//   outgoing level -f, the same magnitude negated (exact in floating point): every pixel is then
//   drawn by exactly one of the two levels — no overlap, no hole — in every pass, because both
//   evaluate the same predicate (threshold < |f|) on the same screen-space pattern. Sending +f to
//   one level and -(1 - f) to the other does NOT partition; keep the magnitudes equal. Never send 0
//   for a level that should be invisible: 0 means fully drawn; skip the instance instead.
//
// The pattern is FishSurface's 4×4 Bayer matrix (FishDitherThreshold — the day/night dissolve's),
// NOT URP's LODCrossFade: that one samples _DitheringTexture, which URP binds only when its asset's
// LOD cross-fade is on and may be blue noise, while the Bayer matrix needs nothing bound and is the
// same in every shader that includes this file. It is not shifted per instance (unlike
// FishVegetation's distance fade): two different meshes, possibly on two different shaders, have to
// meet pixel for pixel. Shadow maps get the same partition, in shadow-map pixels.
//
// Kept apart from LOD_FADE_CROSSFADE on purpose: ordinary LODGroup renderers keep unity_LODFade and
// URP's path untouched. No keyword: the value only exists in the INSTANCING_ON variants
// (multi_compile_instancing, already declared by every pass). In non-instanced variants — the ones
// the SRP Batcher uses — nothing is declared at all, so UnityPerMaterial and the cbuffer layout are
// byte-for-byte what they were, and the clip compiles away. DOTS instancing reads 0.
//
// GPU-driven draws (the indirect shaders' PROCEDURAL_INSTANCING_ON variants, RenderMeshIndirect):
// the same value with the same sign convention, but read from the culling output instead —
// FishVisible.lodFade, which FishIndirectSetup (FishIndirectInstancing.hlsl) copies into
// FishIndirectLodFade. Only the SOURCE differs; the predicate and the pattern below are shared, so
// an indirect level and an instanced level would still partition each other exactly. Procedural
// variants of a shader that does not include FishIndirectInstancing.hlsl (first) read 0.
//
// Include after Core.hlsl (and after FishIndirectInstancing.hlsl when a shader has both); call
// FishLodFadeClip in a fragment, after UNITY_SETUP_INSTANCE_ID.

#include "FishSurface.hlsl"

#if defined(FISH_INDIRECT_INSTANCED)
    // A procedural (indirect) variant: FishIndirectSetup has the fade from the culling output.
    #define FISH_LOD_FADE_INSTANCED 1
#elif defined(UNITY_INSTANCING_ENABLED)
    #define FISH_LOD_FADE_INSTANCED 1
    UNITY_INSTANCING_BUFFER_START(FishLodFadeProps)
        UNITY_DEFINE_INSTANCED_PROP(float, _FishLodFade)
    UNITY_INSTANCING_BUFFER_END(FishLodFadeProps)
#endif

// This instance's fade, 0 when there is none (always 0 outside the INSTANCING_ON variants and the
// indirect shaders' procedural ones).
float FishLodFadeValue()
{
#if defined(FISH_INDIRECT_INSTANCED)
    return FishIndirectLodFade;
#elif defined(FISH_LOD_FADE_INSTANCED)
    return UNITY_ACCESS_INSTANCED_PROP(FishLodFadeProps, _FishLodFade);
#else
    return 0.0;
#endif
}

// Whether a pixel at threshold t (0..15/16) survives fade f. A single predicate shared by both signs,
// so +f and -f partition the pattern exactly whatever f is.
bool FishLodFadeKeeps(float fade, float threshold)
{
    bool inShare = threshold < abs(fade);
    return fade == 0.0 || (fade > 0.0) == inShare;
}

void FishLodFadeClip(float4 positionCS)
{
#if defined(FISH_LOD_FADE_INSTANCED)
    float fade = FishLodFadeValue();
    if (fade != 0.0)
    {
        clip(FishLodFadeKeeps(fade, FishDitherThreshold(positionCS.xy)) ? 1.0 : -1.0);
    }
#endif
}

#endif
