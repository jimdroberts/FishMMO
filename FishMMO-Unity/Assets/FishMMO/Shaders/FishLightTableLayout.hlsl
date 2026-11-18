#ifndef FISHMMO_LIGHT_TABLE_LAYOUT_INCLUDED
#define FISHMMO_LIGHT_TABLE_LAYOUT_INCLUDED
// The light table's rows (AdditionalLightTable.cs, which sizes the buffer from the same numbers; WebGpuLightTableTests pins
// them). One uint4 per row, the floats as their bits, so a light-layer mask or cookie bit word (a uint URP stores as a float)
// arrives unchanged. Filled each camera by FishLightTableGather (FishLightTableGather.hlsl) from URP's own globals; read by
// the lit passes on WebGPU (FishLightTable.hlsl).
//
// Sizes: URP's limits on WebGPU (UniversalRenderPipeline.maxVisibleAdditionalLights is the mobile count there, and
// maxVisibleReflectionProbes the same), i.e. MAX_VISIBLE_LIGHTS and MAX_REFLECTION_PROBES as Input.hlsl declares them.
#define FISH_LIGHT_TABLE_LIGHTS 32
#define FISH_LIGHT_TABLE_PROBES 32

// Per additional light (URP's visible-light order without the main light, the index its loops use).
#define FISH_LIGHT_TABLE_LIGHT_ROWS 12
#define FISH_LIGHT_ROW_POSITION 0       // _AdditionalLightsPosition
#define FISH_LIGHT_ROW_COLOR 1          // _AdditionalLightsColor (a: subtractive mixed lighting under Forward+)
#define FISH_LIGHT_ROW_ATTENUATION 2    // _AdditionalLightsAttenuation
#define FISH_LIGHT_ROW_SPOT_DIR 3       // _AdditionalLightsSpotDir
#define FISH_LIGHT_ROW_OCCLUSION 4      // _AdditionalLightsOcclusionProbes
#define FISH_LIGHT_ROW_SHADOW 5         // _AdditionalShadowParams
#define FISH_LIGHT_ROW_COOKIE_RECT 6    // _AdditionalLightsCookieAtlasUVRects
#define FISH_LIGHT_ROW_MISC 7           // x _AdditionalLightsLayerMasks, y _AdditionalLightsLightTypes
#define FISH_LIGHT_ROW_WORLD_TO_LIGHT 8 // _AdditionalLightsWorldToLights, four rows

// Per additional-light shadow slice (_AdditionalLightsWorldToShadow; a point light has six), after the lights.
#define FISH_LIGHT_TABLE_SLICE_ROWS 4
#define FISH_LIGHT_TABLE_SLICES_BEGIN (FISH_LIGHT_TABLE_LIGHTS * FISH_LIGHT_TABLE_LIGHT_ROWS)

// Per Forward+ reflection probe (urp_ReflProbes_*), after the slices.
#define FISH_LIGHT_TABLE_PROBE_ROWS 11
#define FISH_PROBE_ROW_BOX_MAX 0
#define FISH_PROBE_ROW_BOX_MIN 1
#define FISH_PROBE_ROW_POSITION 2
#define FISH_PROBE_ROW_ROTATION 3
#define FISH_PROBE_ROW_MIP_SCALE_OFFSET 4 // seven rows, one per mip
#define FISH_LIGHT_TABLE_PROBES_BEGIN (FISH_LIGHT_TABLE_SLICES_BEGIN + FISH_LIGHT_TABLE_LIGHTS * FISH_LIGHT_TABLE_SLICE_ROWS)

// The last row: x _AdditionalLightsCookieEnableBits (one word holds all 32 lights).
#define FISH_LIGHT_TABLE_HEADER (FISH_LIGHT_TABLE_PROBES_BEGIN + FISH_LIGHT_TABLE_PROBES * FISH_LIGHT_TABLE_PROBE_ROWS)
#define FISH_LIGHT_TABLE_ROWS (FISH_LIGHT_TABLE_HEADER + 1)
#endif
