#ifndef FISHMMO_LIGHT_TABLE_GATHER_INCLUDED
#define FISHMMO_LIGHT_TABLE_GATHER_INCLUDED
// FishLightTableGather: URP's per-light arrays into the light table (FishLightTableLayout.hlsl), one thread per row, once per
// camera after URP has set them (AdditionalLightTable.cs: after the shadow passes, which set the last of them). The arrays
// are declared here by URP's own names and sizes (Input.hlsl, Shadows.hlsl, LightCookieInput.hlsl), so the kernel's
// $Globals is filled from the very global properties URP sets for its lit passes: a copy, bit for bit, whatever URP put
// there (stale entries past the visible count included), with no second account of URP's light setup to keep in step.
#include "FishLightTableLayout.hlsl"

float4 _AdditionalLightsPosition[FISH_LIGHT_TABLE_LIGHTS];
float4 _AdditionalLightsColor[FISH_LIGHT_TABLE_LIGHTS];
float4 _AdditionalLightsAttenuation[FISH_LIGHT_TABLE_LIGHTS];
float4 _AdditionalLightsSpotDir[FISH_LIGHT_TABLE_LIGHTS];
float4 _AdditionalLightsOcclusionProbes[FISH_LIGHT_TABLE_LIGHTS];
float _AdditionalLightsLayerMasks[FISH_LIGHT_TABLE_LIGHTS];
float4 _AdditionalShadowParams[FISH_LIGHT_TABLE_LIGHTS];
float4x4 _AdditionalLightsWorldToShadow[FISH_LIGHT_TABLE_LIGHTS];
float _AdditionalLightsCookieEnableBits[(FISH_LIGHT_TABLE_LIGHTS + 31) / 32];
float4x4 _AdditionalLightsWorldToLights[FISH_LIGHT_TABLE_LIGHTS];
float4 _AdditionalLightsCookieAtlasUVRects[FISH_LIGHT_TABLE_LIGHTS];
float _AdditionalLightsLightTypes[FISH_LIGHT_TABLE_LIGHTS];
float4 urp_ReflProbes_BoxMax[FISH_LIGHT_TABLE_PROBES];
float4 urp_ReflProbes_BoxMin[FISH_LIGHT_TABLE_PROBES];
float4 urp_ReflProbes_ProbePosition[FISH_LIGHT_TABLE_PROBES];
float4 urp_ReflProbes_MipScaleOffset[FISH_LIGHT_TABLE_PROBES * 7];
float4 urp_ReflProbes_Rotation[FISH_LIGHT_TABLE_PROBES];

RWStructuredBuffer<uint4> _FishLightTableOut;

uint4 FishLightTableMatrixRow(float4x4 m, uint row)
{
    return asuint(row == 0u ? m[0] : row == 1u ? m[1] : row == 2u ? m[2] : m[3]);
}

[numthreads(64, 1, 1)]
void FishLightTableGather(uint3 id : SV_DispatchThreadID)
{
    uint row = id.x;
    if (row >= FISH_LIGHT_TABLE_ROWS)
    {
        return;
    }
    uint4 bits = uint4(0u, 0u, 0u, 0u);
    if (row < FISH_LIGHT_TABLE_SLICES_BEGIN)
    {
        uint light = row / FISH_LIGHT_TABLE_LIGHT_ROWS;
        uint field = row - light * FISH_LIGHT_TABLE_LIGHT_ROWS;
        switch (field)
        {
            case FISH_LIGHT_ROW_POSITION: bits = asuint(_AdditionalLightsPosition[light]); break;
            case FISH_LIGHT_ROW_COLOR: bits = asuint(_AdditionalLightsColor[light]); break;
            case FISH_LIGHT_ROW_ATTENUATION: bits = asuint(_AdditionalLightsAttenuation[light]); break;
            case FISH_LIGHT_ROW_SPOT_DIR: bits = asuint(_AdditionalLightsSpotDir[light]); break;
            case FISH_LIGHT_ROW_OCCLUSION: bits = asuint(_AdditionalLightsOcclusionProbes[light]); break;
            case FISH_LIGHT_ROW_SHADOW: bits = asuint(_AdditionalShadowParams[light]); break;
            case FISH_LIGHT_ROW_COOKIE_RECT: bits = asuint(_AdditionalLightsCookieAtlasUVRects[light]); break;
            case FISH_LIGHT_ROW_MISC: bits = uint4(asuint(_AdditionalLightsLayerMasks[light]), asuint(_AdditionalLightsLightTypes[light]), 0u, 0u); break;
            default: bits = FishLightTableMatrixRow(_AdditionalLightsWorldToLights[light], field - FISH_LIGHT_ROW_WORLD_TO_LIGHT); break;
        }
    }
    else if (row < FISH_LIGHT_TABLE_PROBES_BEGIN)
    {
        uint local = row - FISH_LIGHT_TABLE_SLICES_BEGIN;
        uint slice = local / FISH_LIGHT_TABLE_SLICE_ROWS;
        bits = FishLightTableMatrixRow(_AdditionalLightsWorldToShadow[slice], local - slice * FISH_LIGHT_TABLE_SLICE_ROWS);
    }
    else if (row < FISH_LIGHT_TABLE_HEADER)
    {
        uint local = row - FISH_LIGHT_TABLE_PROBES_BEGIN;
        uint probe = local / FISH_LIGHT_TABLE_PROBE_ROWS;
        uint field = local - probe * FISH_LIGHT_TABLE_PROBE_ROWS;
        switch (field)
        {
            case FISH_PROBE_ROW_BOX_MAX: bits = asuint(urp_ReflProbes_BoxMax[probe]); break;
            case FISH_PROBE_ROW_BOX_MIN: bits = asuint(urp_ReflProbes_BoxMin[probe]); break;
            case FISH_PROBE_ROW_POSITION: bits = asuint(urp_ReflProbes_ProbePosition[probe]); break;
            case FISH_PROBE_ROW_ROTATION: bits = asuint(urp_ReflProbes_Rotation[probe]); break;
            default: bits = asuint(urp_ReflProbes_MipScaleOffset[probe * 7u + (field - FISH_PROBE_ROW_MIP_SCALE_OFFSET)]); break;
        }
    }
    else
    {
        bits = uint4(asuint(_AdditionalLightsCookieEnableBits[0]), 0u, 0u, 0u);
    }
    _FishLightTableOut[row] = bits;
}
#endif
