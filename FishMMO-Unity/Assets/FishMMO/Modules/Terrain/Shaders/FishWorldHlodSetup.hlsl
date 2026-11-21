#ifndef FISH_WORLD_HLOD_SETUP_INCLUDED
#define FISH_WORLD_HLOD_SETUP_INCLUDED
// Proxy vertices and their original prop origins are baked in world coordinates.
void FishWorldHlodSetup()
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    unity_ObjectToWorld = float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
    unity_WorldToObject = unity_ObjectToWorld;
#if defined(FISH_INDIRECT_STATIC_PER_DRAW)
    // DXC: these names are FishIndirectPerDraw.hlsl's statics, which hold nothing until written. The proxies never move, so
    // last frame's matrix is this frame's; never mirrored, so w is 1 (GetOddNegativeScale).
    unity_MatrixPreviousM = unity_ObjectToWorld;
    unity_MatrixPreviousMI = unity_WorldToObject;
    unity_WorldTransformParams = FishPerDrawWorldTransformParams();
    unity_WorldTransformParams.w = 1.0;
#endif
#endif
}
#endif
