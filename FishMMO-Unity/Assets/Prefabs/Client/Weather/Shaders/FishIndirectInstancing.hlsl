#ifndef FISHMMO_INDIRECT_INSTANCING_INCLUDED
#define FISHMMO_INDIRECT_INSTANCING_INCLUDED

// GPU-driven instancing for the terrain's trees and details: the compute-culled draws that
// TerrainTreeInstancing (FishMMO.Client/World/Terrain) issues with Graphics.RenderMeshIndirect.
// Nothing about the instance comes from the CPU per frame — its transform and its LOD fade are read
// here, in the procedural setup Unity calls from UNITY_SETUP_INSTANCE_ID, from two buffers the
// renderer's compute pass keeps. Only the indirect shaders include this file (FishMMO/Vegetation
// Indirect, FishMMO/Weather Lit Indirect); the ordinary shaders, which Unity's own instanced paths
// (detail painting, LODGroups, DrawMeshInstanced, the SRP Batcher) use, never see it.
//
// ── The contract (names are the renderer's; change them here and in TerrainTreeInstancing) ─────
//
//   struct FishInstance { float4 row0; float4 row1; float4 row2; }     48 bytes, stride 48
//       byte 0  row0   objectToWorld row 0: xyz the 3×3 row, w translation x
//       byte 16 row1   objectToWorld row 1: xyz the 3×3 row, w translation y
//       byte 32 row2   objectToWorld row 2: xyz the 3×3 row, w translation z
//       (C#: Matrix4x4.GetRow(0..2); the fourth row is always 0 0 0 1.)
//   struct FishVisible  { uint index; float lodFade; }                 8 bytes, stride 8
//       byte 0  index    which _FishInstances entry this drawn instance is
//       byte 4  lodFade  its per-instance LOD cross-fade, FishLodFade.hlsl's sign convention:
//                        0 fully drawn, +f fading in (share f drawn), -f fading out (exactly the
//                        complement of +f). The two levels of a transition get +f and -f.
//   StructuredBuffer<FishInstance> _FishInstances;          every instance (the culling input)
//   StructuredBuffer<FishVisible>  _FishVisibleInstances;   the culling output, one run per draw
//   StructuredBuffer<uint> _FishCommandBases;               each command's run start in it, by command index
//
//   The instance drawn as unity_InstanceID is
//   _FishInstances[_FishVisibleInstances[_FishCommandBases[GetCommandID(0)] + (unity_InstanceID - unity_BaseInstanceID)].index],
//   GetCommandID being Unity's per-call unity_BaseCommandID (UnityIndirect.cginc) = the call's startCommand.
//   NOT "_FishVisible": FishMMO/Weather Lit already has a material property of that name (the
//   day/night dissolve, a half in UnityPerMaterial), and a buffer bound under the same property ID
//   would collide with it in the shader and in every MaterialPropertyBlock.
//   Why a per-command buffer and not a per-draw uniform: the first version passed each draw's run
//   start as a loose uniform (_FishVisibleBase) in the call's MaterialPropertyBlock. It never reached
//   the shader reliably — as a uint through SetInteger the GLCore backend read 0, and as a float the
//   grass still came out in bands — so every draw read slot 0's run. unity_BaseCommandID is set by the
//   engine itself for each RenderMeshIndirect call, which is the mechanism Unity documents for it;
//   _FishCommandBases is bound once and indexed by it. _FishVisibleBase stays declared, unused.
//
// ── What the renderer must do ────────────────────────────────────────────────────────────────
//
//   * startInstance = 0 in every indirect args entry, and one command per RenderMeshIndirect call
//     (commandCount 1). SV_InstanceID does not mean the same thing on every API: on Vulkan and
//     WebGPU it INCLUDES the args' startInstance, on D3D11/12 it does not (Unity's own
//     UnityIndirect.cginc corrects for this per API). With startInstance 0 both agree, and the
//     per-draw offset lives in _FishVisibleBase instead. unity_InstanceID is SV_InstanceID plus
//     unity_BaseInstanceID, which Unity leaves 0 for indirect draws.
//   * Transforms are T·R·S with a rotation and a positive per-axis scale, and NO SHEAR — which
//     every terrain tree, detail, boulder and ice piece is (rotation about Y or ground-aligned,
//     scale (w, h, w)). The 3×3's columns are then orthogonal, and the inverse below relies on it.
//     A sheared or mirrored transform would light and bend wrongly, silently.
//   * Only where SystemInfo.supportsComputeShaders: the procedural variants read StructuredBuffers,
//     which WebGL2 (the fallback target) has none of. There the renderer must draw with the
//     ordinary shaders (FishMMO/Vegetation, FishMMO/Weather Lit) through RenderMeshInstanced.
//   * The ShadowCaster pass runs this same setup, so a ShadowsOnly call with its own visible list
//     and base casts from exactly the transforms it was given.
//
// ── WebGPU and the other primary targets ─────────────────────────────────────────────────────
//
//   Read-only StructuredBuffers in the vertex and fragment stages only: no RW buffers outside
//   compute, no geometry stage, no wave intrinsics. That is WebGPU's floor and holds on D3D11/12
//   and Vulkan too. The fragment stage runs the setup again (UNITY_SETUP_INSTANCE_ID in a fragment
//   calls it); the compiler keeps only the one 8-byte _FishVisibleInstances load the LOD fade needs there.
//
// ── Why writing unity_ObjectToWorld works ────────────────────────────────────────────────────
//
//   Under procedural instancing URP 17 (Unity 6) maps UNITY_MATRIX_M straight to
//   unity_ObjectToWorld and UNITY_MATRIX_I_M to unity_WorldToObject (Input.hlsl; the instanced
//   arrays exist only for INSTANCING_ON and DOTS), and UNITY_PREV_MATRIX_M / _I_M to
//   unity_MatrixPreviousM / unity_MatrixPreviousMI, all members of UnityPerDraw. A procedural
//   setup overwrites them — URP's own particles do exactly this (ParticlesInstancing.hlsl). So
//   everything downstream that reads the object's origin or scale through those macros —
//   TransformObjectToWorld, TransformObjectToWorldNormal (the inverse, for non-uniform scale),
//   VegFaceCamera's billboard scale, the distance fade's origin, the ground sink and tint hashes,
//   URP's shadow bias and motion vectors — sees the instance and needs no change.
//
// Include after Core.hlsl (unity_InstanceID, UnityPerDraw) and BEFORE the pass code — in
// particular before FishLodFade.hlsl, which only reads the indirect fade when it finds this file
// already included.

#if defined(FISHMMO_LOD_FADE_INCLUDED)
    #error FishIndirectInstancing.hlsl must be included before FishLodFade.hlsl, or procedural draws ignore the LOD fade
#endif

struct FishInstance
{
    float4 row0;
    float4 row1;
    float4 row2;
};

struct FishVisible
{
    uint index;
    float lodFade;
};

// In procedural variants only, so the ordinary INSTANCING_ON variant of an indirect shader (a stray
// DrawMeshInstanced with one of its materials) binds nothing it does not use.
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_DOTS_INSTANCING_ENABLED)
    #define FISH_INDIRECT_INSTANCED 1
    // Unity's own per-draw command index for RenderMeshIndirect: unity_BaseCommandID, read through
    // GetCommandID. The engine sets it for every indirect call itself, so nothing per draw rides on a
    // MaterialPropertyBlock uniform — which is what failed: a per-call _FishVisibleBase never reached
    // the shader reliably (every draw read slot 0's run, so grass came out in bands).
    #include "UnityIndirect.cginc"
    StructuredBuffer<FishInstance> _FishInstances;
    StructuredBuffer<FishVisible> _FishVisibleInstances;
    // Where each command's run starts in _FishVisibleInstances, one uint per command index (the
    // renderer issues one command per call with startCommand = its slot's command index).
    StructuredBuffer<uint> _FishCommandBases;
    // Superseded by _FishCommandBases; still declared so an older property block binding it is harmless.
    float _FishVisibleBase;
    // This instance's LOD fade, for FishLodFadeValue (FishLodFade.hlsl). Set by FishIndirectSetup.
    static float FishIndirectLodFade = 0.0;
#endif

// The inverse of an affine T·R·S with orthogonal 3×3 columns c0, c1, c2 (no shear). With
// M3 = R·S, inverse(M3) = S⁻¹·Rᵀ, whose row i is column i of M3 divided by its squared length
// (|cᵢ|² = sᵢ²): about fifteen flops, against a general 3×3 inverse's cofactors and determinant.
// The inverse translation is then -(inverse(M3) · t).
float4x4 FishIndirectInverseAffine(float4 row0, float4 row1, float4 row2)
{
    float3 c0 = float3(row0.x, row1.x, row2.x);
    float3 c1 = float3(row0.y, row1.y, row2.y);
    float3 c2 = float3(row0.z, row1.z, row2.z);
    float3 i0 = c0 / dot(c0, c0);
    float3 i1 = c1 / dot(c1, c1);
    float3 i2 = c2 / dot(c2, c2);
    float3 t = float3(row0.w, row1.w, row2.w);
    float3 it = -float3(dot(i0, t), dot(i1, t), dot(i2, t));
    return float4x4(
        float4(i0, it.x),
        float4(i1, it.y),
        float4(i2, it.z),
        float4(0.0, 0.0, 0.0, 1.0));
}

// The procedural setup (#pragma instancing_options procedural:FishIndirectSetup). Unity forward
// declares it and calls it from every UNITY_SETUP_INSTANCE_ID, vertex and fragment, in every pass.
void FishIndirectSetup()
{
#if defined(FISH_INDIRECT_INSTANCED)
    // The instance's position in its draw: unity_InstanceID is SV_InstanceID plus unity_BaseInstanceID,
    // and with startInstance 0 in every args entry SV_InstanceID is the raw 0-based index on every
    // API (Vulkan and WebGPU add startInstance, D3D does not — zero makes them agree).
    uint local = (uint)(unity_InstanceID - unity_BaseInstanceID);
    FishVisible visible = _FishVisibleInstances[_FishCommandBases[GetCommandID(0)] + local];
    FishInstance instance = _FishInstances[visible.index];

    float4x4 objectToWorld = float4x4(instance.row0, instance.row1, instance.row2, float4(0.0, 0.0, 0.0, 1.0));
    float4x4 worldToObject = FishIndirectInverseAffine(instance.row0, instance.row1, instance.row2);
    unity_ObjectToWorld = objectToWorld;
    unity_WorldToObject = worldToObject;
    // Instances do not move: last frame's matrix is this frame's, so object motion is zero and a
    // motion-vector pass (URP's ObjectMotionVectors reads UNITY_PREV_MATRIX_M) leaves only the
    // camera's own motion. Whatever Unity put in UnityPerDraw for the indirect call is not an
    // instance's matrix, current or previous.
    unity_MatrixPreviousM = objectToWorld;
    unity_MatrixPreviousMI = worldToObject;
    // Positive scale only (the precondition above), so never mirrored: GetOddNegativeScale() reads
    // w for the tangent frame's sign and must not depend on what the indirect call left there.
    unity_WorldTransformParams.w = 1.0;

    FishIndirectLodFade = visible.lodFade;
#endif
}

#endif
