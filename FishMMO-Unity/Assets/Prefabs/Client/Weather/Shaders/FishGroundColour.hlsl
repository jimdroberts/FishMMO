#ifndef FISHMMO_GROUND_COLOUR_INCLUDED
#define FISHMMO_GROUND_COLOUR_INCLUDED

// The ground colour map and the distance blending built on it (GroundColourMap, FishMMO.Client/World/Terrain):
// one texture over every active terrain of what the ground looks like (its splat weights times the colours
// the terrain array shader draws), published as globals in play mode. Params.x is 0 when there is no map
// (edit mode, no terrain): everything here then does nothing.
//
// Shared by FishVegetationPasses.hlsl (grass, flowers, trees), FishGrassBlades.hlsl (procedural blades) and
// FishWeatherLitForwardPass.hlsl (its GPU-instanced terrain rocks only).

TEXTURE2D(_FishGroundColour);
SAMPLER(sampler_FishGroundColour);
float4 _FishGroundColourRect;    // xy the map's world xz origin, zw one over its xz size
float4 _FishGroundColourParams;  // x 1 when the map is set; y debug view; z blend at the root; w blend over the whole plant
float4 _FishGroundColourShape;   // x metres over which the root blend eases to the plant blend; y, z crown fade start/end (m); w blade detail
float4 _FishDistanceBlend;       // x start (m), y end (m), z normal flatten, w ground colour pull — distant terrain objects
TEXTURE2D(_FishGroundNormal);    // the terrain's surface normal over the same rect: linear, rgb = n·0.5 + 0.5, a coverage

// The ground's colour under a world position (rgb) and the map's coverage there (a; 0 outside it or where no
// terrain was read).
half4 FishGroundColourAt(float3 positionWS)
{
    if (_FishGroundColourParams.x <= 0.0)
    {
        return half4(0.0, 0.0, 0.0, 0.0);
    }
    float2 uv = (positionWS.xz - _FishGroundColourRect.xy) * _FishGroundColourRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return half4(0.0, 0.0, 0.0, 0.0);
    }
    return SAMPLE_TEXTURE2D_LOD(_FishGroundColour, sampler_FishGroundColour, uv, 0.0);
}

// The terrain's surface normal under a world position; straight up where the map knows nothing.
float3 FishGroundNormalAt(float3 positionWS)
{
    if (_FishGroundColourParams.x <= 0.0)
    {
        return float3(0.0, 1.0, 0.0);
    }
    float2 uv = (positionWS.xz - _FishGroundColourRect.xy) * _FishGroundColourRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return float3(0.0, 1.0, 0.0);
    }
    float4 packed = SAMPLE_TEXTURE2D_LOD(_FishGroundNormal, sampler_FishGroundColour, uv, 0.0);
    float3 n = packed.rgb * 2.0 - 1.0;
    return packed.a > 0.0 && dot(n, n) > 1e-4 ? normalize(n) : float3(0.0, 1.0, 0.0);
}

// Distant terrain objects (trees, rocks, plants) settle into the landscape: with distance their lighting
// normal turns toward the TERRAIN's normal under them (not straight up: a tree on a slope facing away from
// the sun lit as if on flat ground out-shone its own hillside), so they shade like the slope they stand
// on, and their colour drifts partly toward the local ground's. Close up nothing changes.
// Returned as factors, not applied, so each caller lerps in its own types (URP's InputData.normalWS is a
// float3, a vegetation pass's a half3, and an inout parameter must match exactly): far is the 0..1
// distance factor (fade gloss and glow by it), flatten how far the normal goes to target, pull.rgb the
// ground's colour and pull.a how much of it the albedo takes.
void FishDistanceBlend(float3 positionWS, out float far, out float flatten, out float3 target, out half4 pull)
{
    far = 0.0;
    flatten = 0.0;
    target = float3(0.0, 1.0, 0.0);
    pull = half4(0.0, 0.0, 0.0, 0.0);
    if (_FishGroundColourParams.x <= 0.0 || _FishDistanceBlend.y <= _FishDistanceBlend.x)
    {
        return;
    }
    far = smoothstep(_FishDistanceBlend.x, _FishDistanceBlend.y, distance(_WorldSpaceCameraPos, positionWS));
    if (far <= 0.0)
    {
        return;
    }
    flatten = far * _FishDistanceBlend.z;
    target = FishGroundNormalAt(positionWS);
    half4 ground = FishGroundColourAt(positionWS);
    pull = half4(ground.rgb, (half)(far * _FishDistanceBlend.w) * ground.a);
}

#endif
