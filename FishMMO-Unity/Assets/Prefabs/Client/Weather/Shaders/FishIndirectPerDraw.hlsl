#ifndef FISHMMO_INDIRECT_PER_DRAW_INCLUDED
#define FISHMMO_INDIRECT_PER_DRAW_INCLUDED

// The per-draw matrices the procedural setup writes, as statics, so the indirect twins' passes compile with DXC.
//
// FishIndirectSetup (FishIndirectInstancing.hlsl) writes each instance's transform into unity_ObjectToWorld,
// unity_WorldToObject, unity_MatrixPreviousM / MI and unity_WorldTransformParams, the names URP's UNITY_MATRIX_M family and
// SpaceTransforms.hlsl read under procedural instancing. They are UnityPerDraw cbuffer members (UnityInput.hlsl). FXC lets a
// shader write a global (it copies it to a temporary), DXC does not ("cannot assign to … const-qualified"), and the macros
// that read them are expanded inside URP's Core.hlsl, which leaves no hook between the cbuffer and SpaceTransforms.hlsl.
// So this file declares UnityPerDraw itself, first: Common.hlsl, then UnityInput.hlsl (both include-guarded, so Core.hlsl
// skips them later), then statics under the same names as macros. Every later reader — SpaceTransforms.hlsl, URP's passes,
// ours — reads the statics, and the setup writes them; the cbuffer keeps its members and its layout, unread.
//
// Only where it is needed and nothing else changes: DXC programs (UNITY_COMPILER_DXC: Vulkan, `#pragma use_dxc vulkan`) of
// the procedural variants. FXC platforms (WebGPU, D3D) see the same tokens as before, so their binding order — which decides
// WebGPU's dynamic uniform slots (Docs/Rendering/2026-11-02/webgpu-dynamic-uniforms) — is unchanged; DOTS variants have no
// UnityPerDraw and read their matrices from the batch buffer. Measured 2026-11-03 on the heaviest ForwardLit variant: FXC
// 12.4 s for the fragment alone (298 KB of DXBC, then HLSLcc and glslang), DXC 1 s for both stages to SPIR-V
// (Docs/Rendering/2026-11-03/shader-compile-cost).
//
// Include FIRST in every pass that includes FishIndirectInstancing.hlsl, before anything that reaches Core.hlsl
// (TerrainBrgTests.EveryIndirectPass_HasItsDotsVariant).

#if defined(UNITY_COMPILER_DXC) && defined(PROCEDURAL_INSTANCING_ON) && !defined(DOTS_INSTANCING_ON)
    #define FISH_INDIRECT_STATIC_PER_DRAW 1
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"

    // The draw's own value, read before the name below is redirected: the setup keeps its xyz and sets w.
    real4 FishPerDrawWorldTransformParams() { return unity_WorldTransformParams; }

    static float4x4 FishIndirectObjectToWorld;
    static float4x4 FishIndirectWorldToObject;
    static float4x4 FishIndirectMatrixPreviousM;
    static float4x4 FishIndirectMatrixPreviousMI;
    static real4 FishIndirectWorldTransformParams;
    #define unity_ObjectToWorld FishIndirectObjectToWorld
    #define unity_WorldToObject FishIndirectWorldToObject
    #define unity_MatrixPreviousM FishIndirectMatrixPreviousM
    #define unity_MatrixPreviousMI FishIndirectMatrixPreviousMI
    #define unity_WorldTransformParams FishIndirectWorldTransformParams
#endif

#endif
