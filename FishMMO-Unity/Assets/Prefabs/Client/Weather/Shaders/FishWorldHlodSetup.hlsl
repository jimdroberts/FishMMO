#ifndef FISH_WORLD_HLOD_SETUP_INCLUDED
#define FISH_WORLD_HLOD_SETUP_INCLUDED
// Proxy vertices and their original prop origins are baked in world coordinates.
void FishWorldHlodSetup()
{
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    unity_ObjectToWorld = float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
    unity_WorldToObject = unity_ObjectToWorld;
#endif
}
#endif
