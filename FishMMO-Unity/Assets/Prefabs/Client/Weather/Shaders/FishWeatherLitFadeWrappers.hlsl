#ifndef FISHMMO_WEATHER_LIT_FADE_WRAPPERS_INCLUDED
#define FISHMMO_WEATHER_LIT_FADE_WRAPPERS_INCLUDED

// FishMMO edit: the per-instance LOD fade (FishLodFade.hlsl) ahead of URP's own fragments, for the
// FishMMO/Weather Lit passes that are not forked — ShadowCaster, GBuffer, DepthOnly, DepthNormals,
// MotionVectors and XRMotionVectors run URP's stock pass files, and each gets a small wrapper here as
// its fragment instead.
//
// In a file rather than inline in the shader so that FishMMO/Weather Lit and FishMMO/Weather Lit
// Indirect (the GPU-driven copy, FishIndirectInstancing.hlsl) compile the very same wrappers: each
// pass defines which one it wants, then includes this file after URP's pass file —
//   FISH_LIT_WRAP_SHADOW         FishShadowPassFragment       (ShadowCasterPass.hlsl)
//   FISH_LIT_WRAP_GBUFFER        FishLitGBufferPassFragment   (LitGBufferPass.hlsl)
//   FISH_LIT_WRAP_DEPTH          FishDepthOnlyFragment        (DepthOnlyPass.hlsl)
//   FISH_LIT_WRAP_DEPTHNORMALS   FishDepthNormalsFragment     (LitDepthNormalsPass.hlsl)
//   FISH_LIT_WRAP_MOTIONVECTORS  FishMotionVectorsFragment    (ObjectMotionVectors.hlsl)
// The entry names are what the shaders' `#pragma fragment` lines (and SurfaceShaderTests) expect.

#include "FishLodFade.hlsl"

#if defined(FISH_LIT_WRAP_SHADOW)
half4 FishShadowPassFragment(Varyings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    FishLodFadeClip(input.positionCS);
    return ShadowPassFragment(input);
}
#endif

#if defined(FISH_LIT_WRAP_GBUFFER)
GBufferFragOutput FishLitGBufferPassFragment(Varyings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    FishLodFadeClip(input.positionCS);
    return LitGBufferPassFragment(input);
}
#endif

#if defined(FISH_LIT_WRAP_DEPTH)
half FishDepthOnlyFragment(Varyings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    FishLodFadeClip(input.positionCS);
    return DepthOnlyFragment(input);
}
#endif

#if defined(FISH_LIT_WRAP_DEPTHNORMALS)
void FishDepthNormalsFragment(
    Varyings input
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    FishLodFadeClip(input.positionCS);
    DepthNormalsFragment(input, outNormalWS
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}
#endif

#if defined(FISH_LIT_WRAP_MOTIONVECTORS)
float4 FishMotionVectorsFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    FishLodFadeClip(input.positionCS);
    return frag(input);
}
#endif

#endif
