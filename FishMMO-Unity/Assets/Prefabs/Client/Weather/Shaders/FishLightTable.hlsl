#ifndef FISHMMO_LIGHT_TABLE_INCLUDED
#define FISHMMO_LIGHT_TABLE_INCLUDED
// WebGPU: URP's per-light arrays read from one storage buffer, the light table, instead of from the program's $Globals.
// FishShadowVariants.hlsl makes URP's lighting cbuffers plain globals on WebGPU (to stay within the device's 10 dynamic
// uniform buffers), so every lit program's $Globals carried ~14 KB of light, shadow-slice, cookie and reflection-probe
// arrays, and Unity copies a program's whole $Globals on every pass application: the largest engine cost left in the
// browser (Docs/Rendering/2026-11-03/webgpu-light-buffer). The table holds the same values, copied from URP's globals once
// per camera (AdditionalLightTable.cs, FishLightTableGather.hlsl), and is bound once, so nothing per light is copied per draw.
//
// This file: the buffer, its rows, and the additional-light cookie read from it. FishLightTableLighting.hlsl routes URP's
// light, shadow and reflection-probe functions here. Both are included by FishLightCookieVariants.hlsl on WebGPU only.
#include "FishLightTableLayout.hlsl"

StructuredBuffer<uint4> _FishLightTable;

float4 FishLightTableRow(uint row)
{
    return asfloat(_FishLightTable[row]);
}

float4 FishLightTableLight(int lightIndex, uint field)
{
    return FishLightTableRow((uint)lightIndex * FISH_LIGHT_TABLE_LIGHT_ROWS + field);
}

float4x4 FishLightTableMatrix(uint row)
{
    return float4x4(FishLightTableRow(row), FishLightTableRow(row + 1u), FishLightTableRow(row + 2u), FishLightTableRow(row + 3u));
}

float4 FishLightTableProbe(uint probeIndex, uint field)
{
    return FishLightTableRow(FISH_LIGHT_TABLE_PROBES_BEGIN + probeIndex * FISH_LIGHT_TABLE_PROBE_ROWS + field);
}

// LightCookie.hlsl SampleAdditionalLightCookie, with LightCookieInput.hlsl's getters reading the table.
real3 FishTableSampleAdditionalLightCookie(int perObjectLightIndex, float3 samplePositionWS)
{
    // IsLightCookieEnabled: the table keeps the one enable word 32 lights need.
    uint bitOffset = (uint)perObjectLightIndex & ((1 << 5) - 1);
    uint elem = _FishLightTable[FISH_LIGHT_TABLE_HEADER].x;
    if((elem & (1u << bitOffset)) == 0u)
        return real3(1,1,1);

    int lightType     = FishLightTableLight(perObjectLightIndex, FISH_LIGHT_ROW_MISC).y;
    int isSpot        = lightType == URP_LIGHT_TYPE_SPOT;
    int isDirectional = lightType == URP_LIGHT_TYPE_DIRECTIONAL;

    float4x4 worldToLight = FishLightTableMatrix((uint)perObjectLightIndex * FISH_LIGHT_TABLE_LIGHT_ROWS + FISH_LIGHT_ROW_WORLD_TO_LIGHT);
    float4 uvRect = FishLightTableLight(perObjectLightIndex, FISH_LIGHT_ROW_COOKIE_RECT);

    float2 uv;
    if(isSpot)
    {
        uv = ComputeLightCookieUVSpot(worldToLight, samplePositionWS, uvRect);
    }
    else if(isDirectional)
    {
        uv = ComputeLightCookieUVDirectional(worldToLight, samplePositionWS, uvRect, URP_TEXTURE_WRAP_MODE_REPEAT);
    }
    else
    {
        uv = ComputeLightCookieUVPoint(worldToLight, samplePositionWS, uvRect);
    }

    real4 color = SampleAdditionalLightsCookieAtlasTexture(uv);

    return IsAdditionalLightsCookieAtlasTextureRGBFormat() ? color.rgb
            : IsAdditionalLightsCookieAtlasTextureAlphaFormat() ? color.aaa
            : color.rrr;
}
#endif
