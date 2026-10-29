#ifndef FISHMMO_ROCK_FILLET_PASS_INCLUDED
#define FISHMMO_ROCK_FILLET_PASS_INCLUDED

// The rock seam fillets' passes (FishMMO/Rock Fillet; RockFillets, FishMMO.Client/World/Terrain): the triangles
// FishRockFillets.compute built in the seams between rocks, drawn with Graphics.RenderPrimitivesIndirect, one call per
// rock look (material). No mesh and no vertex input: a vertex is read from the drawn set by SV_VertexID, its draw's run
// found by its command (GetCommandID, the call's startCommand = the look).
//
// The forward pass is FishMMO/Weather Lit's indirect forward pass (FishWeatherLitForwardPass.hlsl) for a surface with no
// UVs: the look's own textures projected in world space (triplanar, at the material's tiling: rock UVs are in metres),
// then the same steps in the same order — the base band and the crevices' soil (the terrain itself, lit as the
// terrain), the moss, the weather, the distant blend, how far underground, URP's PBR, the sun taken back off
// underground, the fog — so a fillet is lit exactly as the rock face it runs into. Its crevice is measured to the
// SECOND nearest rock: where the fillet meets a face that rock is the nearest, and the second is what the face's own
// pixel measures, so soil and shade carry across the join unbroken.
//
// Every pass clips the same pixels: past the fade distance (dithered over its last quarter) and where the fillet is
// under the rocks by the distance fields (dithered over FISH_FILLET_EDGE): what a field's error would otherwise show of
// the fillet in front of a rock's face is the face itself.
//
// Read-only StructuredBuffers in the vertex and fragment stages only (WebGPU's floor).

#include "UnityIndirect.cginc"

// FishRockFillets.compute's FishFilletTri.
struct FishFilletTri
{
    float4 a;
    float4 b;
    float4 c;
    uint4 normals;
};

StructuredBuffer<FishFilletTri> _FishFilletTris;   // the drawn set, grouped by look
StructuredBuffer<uint> _FishFilletStarts;          // each look's first triangle in it, by command (= look)
float4 _FishFilletFade;                            // xyz the camera the fillets were drawn round, w the distance they are gone at (m)
// Per look (its material, set by RockFillets from the rock material's keywords): x its metallic-gloss map, y its
// occlusion map, z its normal map are in use, w its maps are look array slices (_FISH_LOOK_ARRAY). Outside UnityPerMaterial,
// which is FishWeatherLitInput.hlsl's layout.
float4 _FishFilletMaps;

// ── FishMMO edit (2026-10-28): the rock's maps as look array slices (ProceduralArtLookArrays: the generator writes the rock
// surfaces as Texture2DArrays and the rock materials name slices, _FishLookSlices in UnityPerMaterial). All three arrays are
// read with the colour array's sampler — every generated map is imported Repeat, trilinear, aniso 8 — so the pass needs one
// sampler more, not three. The fillet shader never enables the look keyword, so it declares them itself. ──
#if !defined(FISH_LOOK_ARRAYS)
TEXTURE2D_ARRAY(_FishLookColour);   SAMPLER(sampler_FishLookColour);
TEXTURE2D_ARRAY(_FishLookNormal);
TEXTURE2D_ARRAY(_FishLookMask);
#endif

// One map of the look at a projected uv: its array slice when the rock's maps are slices, else its 2D map.
#define FISH_FILLET_MAP(map, mapSampler, array, slice, uv) (_FishFilletMaps.w > 0.5 \
    ? (half4)SAMPLE_TEXTURE2D_ARRAY(array, sampler_FishLookColour, uv, max(slice, 0.0)) : (half4)SAMPLE_TEXTURE2D(map, mapSampler, uv))

// Metres over which a fillet's edge dissolves where it reaches the rocks' faces.
#define FISH_FILLET_EDGE 0.04

struct FishFilletVertex
{
    float3 positionWS;
    float3 normalWS;
    float h;
};

float3 FishFilletUnpackNormal(uint packed)
{
    float2 o = float2(packed & 0xFFFFu, packed >> 16) / 65535.0 * 2.0 - 1.0;
    float3 n = float3(o, 1.0 - abs(o.x) - abs(o.y));
    float t = saturate(-n.z);
    n.xy += float2(n.x >= 0.0 ? -t : t, n.y >= 0.0 ? -t : t);
    return normalize(n);
}

FishFilletVertex FishFilletFetch(uint vertexID)
{
    FishFilletTri tri = _FishFilletTris[_FishFilletStarts[GetCommandID(0)] + vertexID / 3u];
    uint corner = vertexID % 3u;
    float4 p = corner == 0u ? tri.a : corner == 1u ? tri.b : tri.c;
    uint n = corner == 0u ? tri.normals.x : corner == 1u ? tri.normals.y : tri.normals.z;
    FishFilletVertex v;
    v.positionWS = p.xyz;
    v.h = p.w;
    v.normalWS = FishFilletUnpackNormal(n);
    return v;
}

// The fade with distance from the camera and the edge where the fillet goes under the rocks, as one dithered clip.
void FishFilletClip(float4 positionCS, float3 positionWS, float h)
{
    float end = max(_FishFilletFade.w, 1.0);
    float far = saturate((end - distance(positionWS, _FishFilletFade.xyz)) / (0.25 * end));
    float edge = saturate(h / FISH_FILLET_EDGE);
    FishDitherClip(positionCS.xy, far * edge);
}

// ── Forward ─────────────────────────────────────────────────────────────────
#if defined(FISH_FILLET_PASS_FORWARD)

struct FishFilletVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float h : TEXCOORD2;
    // Metres above the terrain straight below, for the base band (FishGroundColour.hlsl), as the rocks' TEXCOORD4.
    float contact : TEXCOORD3;
    half fogFactor : TEXCOORD4;
};

FishFilletVaryings FishFilletForwardVertex(uint vertexID : SV_VertexID)
{
    FishFilletVertex v = FishFilletFetch(vertexID);
    FishFilletVaryings output = (FishFilletVaryings)0;
    output.positionWS = v.positionWS;
    output.normalWS = v.normalWS;
    output.h = v.h;
    output.positionCS = TransformWorldToHClip(v.positionWS);
    output.contact = FishContactHeight(v.positionWS, v.positionWS);
#if !defined(_FOG_FRAGMENT)
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
#endif
    return output;
}

// The look's surface projected along the world's three axes, blended by the normal (the whiteout blend for the normal
// maps, each axis's projection mirrored on its negative side so no face reads its texture backwards).
void FishFilletTriplanar(float3 positionWS, float3 n, out SurfaceData surface, out half3 normalWS)
{
    surface = (SurfaceData)0;
    float3 blend = pow(abs(n), 4.0);
    blend /= max(blend.x + blend.y + blend.z, 1e-5);
    float3 axisSign = float3(n.x < 0.0 ? -1.0 : 1.0, n.y < 0.0 ? -1.0 : 1.0, n.z < 0.0 ? -1.0 : 1.0);
    float2 uvX = positionWS.zy * float2(axisSign.x, 1.0);
    float2 uvY = positionWS.xz * float2(axisSign.y, 1.0);
    float2 uvZ = positionWS.xy * float2(-axisSign.z, 1.0);
    uvX = uvX * _BaseMap_ST.xy + _BaseMap_ST.zw;
    uvY = uvY * _BaseMap_ST.xy + _BaseMap_ST.zw;
    uvZ = uvZ * _BaseMap_ST.xy + _BaseMap_ST.zw;

    half4 albedo = FISH_FILLET_MAP(_BaseMap, sampler_BaseMap, _FishLookColour, _FishLookSlices.x, uvX) * blend.x
                 + FISH_FILLET_MAP(_BaseMap, sampler_BaseMap, _FishLookColour, _FishLookSlices.x, uvY) * blend.y
                 + FISH_FILLET_MAP(_BaseMap, sampler_BaseMap, _FishLookColour, _FishLookSlices.x, uvZ) * blend.z;
    surface.albedo = albedo.rgb * _BaseColor.rgb;
    surface.alpha = 1.0h;

    // The material's metallic-gloss and occlusion maps, where its keywords had them on (_FishFilletMaps, set from the
    // rock material by RockFillets: a runtime material has none of the plain shader's variants).
    if (_FishFilletMaps.x > 0.5)
    {
        half4 gloss = FISH_FILLET_MAP(_MetallicGlossMap, sampler_MetallicGlossMap, _FishLookMask, _FishLookSlices.z, uvX) * blend.x
                    + FISH_FILLET_MAP(_MetallicGlossMap, sampler_MetallicGlossMap, _FishLookMask, _FishLookSlices.z, uvY) * blend.y
                    + FISH_FILLET_MAP(_MetallicGlossMap, sampler_MetallicGlossMap, _FishLookMask, _FishLookSlices.z, uvZ) * blend.z;
        surface.metallic = gloss.r;
        surface.smoothness = gloss.a * _Smoothness;
    }
    else
    {
        surface.metallic = _Metallic;
        surface.smoothness = _Smoothness;
    }
    surface.occlusion = 1.0h;
    if (_FishFilletMaps.y > 0.5)
    {
        half occ = FISH_FILLET_MAP(_OcclusionMap, sampler_OcclusionMap, _FishLookMask, _FishLookSlices.w, uvX).g * blend.x
                 + FISH_FILLET_MAP(_OcclusionMap, sampler_OcclusionMap, _FishLookMask, _FishLookSlices.w, uvY).g * blend.y
                 + FISH_FILLET_MAP(_OcclusionMap, sampler_OcclusionMap, _FishLookMask, _FishLookSlices.w, uvZ).g * blend.z;
        surface.occlusion = LerpWhiteTo(occ, _OcclusionStrength);
    }
    surface.specular = half3(0.0, 0.0, 0.0);
    surface.normalTS = half3(0.0, 0.0, 1.0);

    normalWS = (half3)n;
    if (_FishFilletMaps.z > 0.5)
    {
        half3 tx = UnpackNormalScale(FISH_FILLET_MAP(_BumpMap, sampler_BumpMap, _FishLookNormal, _FishLookSlices.y, uvX), _BumpScale);
        half3 ty = UnpackNormalScale(FISH_FILLET_MAP(_BumpMap, sampler_BumpMap, _FishLookNormal, _FishLookSlices.y, uvY), _BumpScale);
        half3 tz = UnpackNormalScale(FISH_FILLET_MAP(_BumpMap, sampler_BumpMap, _FishLookNormal, _FishLookSlices.y, uvZ), _BumpScale);
        tx.x *= (half)axisSign.x;
        ty.x *= (half)axisSign.y;
        tz.x *= (half)-axisSign.z;
        half3 an = (half3)abs(n);
        tx = half3(tx.xy + (half2)n.zy, an.x * tx.z);
        ty = half3(ty.xy + (half2)n.xz, an.y * ty.z);
        tz = half3(tz.xy + (half2)n.xy, an.z * tz.z);
        normalWS = normalize(tx.zyx * (half)blend.x + ty.xzy * (half)blend.y + tz.xyz * (half)blend.z);
    }
}

void FishFilletForwardFragment(
    FishFilletVaryings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    FishFilletClip(input.positionCS, input.positionWS, input.h);
    float3 geomNormal = normalize(input.normalWS);

    SurfaceData surfaceData;
    half3 mappedNormal;
    FishFilletTriplanar(input.positionWS, geomNormal, surfaceData, mappedNormal);

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = NormalizeNormalPerPixel(mappedNormal);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);

    // ── As the rock's forward pass (FishWeatherLitForwardPass.hlsl), step for step. ──
    half fishUnderground = FishUnderground(inputData.positionWS);
    float3 fishWorldDx = ddx(inputData.positionWS);
    float3 fishWorldDy = ddy(inputData.positionWS);
    half fishGroundWeight;
    FishGroundSite fishSite;
    bool fishHasGround = FishGroundContact(inputData.positionWS, input.contact, (half3)geomNormal, fishGroundWeight, fishSite);
    // The crevice, to the second nearest rock (see the top of the file).
    half fishSoil = 0.0h, fishShade = 1.0h;
    if (FishContactParams().w > 0.0)
    {
        float fishNearest, fishSecond;
        FishRockNearestTwo(inputData.positionWS, fishNearest, fishSecond);
        FishRockCreviceAt(fishSecond, (half3)geomNormal, fishSoil, fishShade);
    }
    surfaceData.albedo *= fishShade;
    surfaceData.occlusion *= fishShade;
    if (fishSoil > fishGroundWeight)
    {
        if (!fishHasGround)
        {
            half fishSteep = 1.0h - saturate(((half)geomNormal.y - 0.45h) / 0.4h);
            fishHasGround = FishGroundSiteAt(inputData.positionWS, fishSteep, fishSite);
        }
        fishGroundWeight = fishSoil;
    }
    fishGroundWeight *= 1.0h - fishUnderground;
    FishGroundPixel fishGround = (FishGroundPixel)0;
    if (fishHasGround && fishGroundWeight > 0.0h)
    {
        fishGround = FishGroundSurfaceAtSite(fishSite, inputData.positionWS, fishWorldDx, fishWorldDy);
    }
    FishMossSurface(inputData.positionWS, (half3)geomNormal, input.contact, fishUnderground,
        surfaceData.albedo, inputData.normalWS, surfaceData.smoothness, surfaceData.metallic, surfaceData.occlusion);
    if (_FishWeatherAmount > 0.0)
    {
        half3 weathered = inputData.normalWS;
        FishWeatherSurface(inputData.positionWS, surfaceData.albedo, weathered, surfaceData.smoothness, surfaceData.metallic, surfaceData.occlusion);
        inputData.normalWS = normalize(lerp(inputData.normalWS, weathered, _FishWeatherAmount));
    }
    {
        float distanceFar, distanceFlatten;
        float3 distanceTarget;
        half4 distancePull;
        FishDistanceBlend(inputData.positionWS, distanceFar, distanceFlatten, distanceTarget, distancePull);
        distanceFlatten *= 1.0h - fishUnderground;
        distancePull.a *= 1.0h - fishUnderground;
        inputData.normalWS = normalize(lerp(inputData.normalWS, distanceTarget, distanceFlatten));
        surfaceData.albedo = lerp(surfaceData.albedo, distancePull.rgb, distancePull.a);
        surfaceData.smoothness *= (half)(1.0 - distanceFar);
    }

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    // The ambient probe, as the rocks (drawn with light probes off, so Unity hands their draws the scene's ambient probe).
    inputData.bakedGI = SampleSH(inputData.normalWS);
    surfaceData.occlusion *= 1.0h - 0.96h * fishUnderground;
    surfaceData.occlusion *= 1.0h - FISH_INDOOR_DIM * FishIndoor(inputData.positionWS);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    if (fishUnderground > 0.001h)
    {
        SurfaceData fishSunSurface = surfaceData;
        BRDFData fishSunBrdf;
        InitializeBRDFData(fishSunSurface, fishSunBrdf);
        Light fishSun = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
        half fishSunTaken = fishUnderground * (half)(1.0 - FishSkylightSun(inputData.positionWS, fishSun.direction));
        color.rgb = max(0.0h, color.rgb - fishSunTaken * LightingPhysicallyBased(fishSunBrdf, fishSun, inputData.normalWS, inputData.viewDirectionWS));
    }
    if (fishHasGround && fishGroundWeight > 0.0h)
    {
        color.rgb = lerp(color.rgb, FishGroundLit(inputData, fishGround, fishShade, fishWorldDx, fishWorldDy), fishGroundWeight);
    }
    color.rgb = lerp(MixFog(color.rgb, inputData.fogCoord), color.rgb, fishUnderground);
    color.a = 1.0h;
    outColor = color;
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif

// ── Shadow caster ───────────────────────────────────────────────────────────
#if defined(FISH_FILLET_PASS_SHADOW)

float3 _LightDirection;
float3 _LightPosition;

struct FishFilletShadowVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float h : TEXCOORD1;
};

FishFilletShadowVaryings FishFilletShadowVertex(uint vertexID : SV_VertexID)
{
    FishFilletVertex v = FishFilletFetch(vertexID);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - v.positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    FishFilletShadowVaryings output;
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(v.positionWS, v.normalWS, lightDirectionWS)));
    output.positionWS = v.positionWS;
    output.h = v.h;
    return output;
}

half4 FishFilletShadowFragment(FishFilletShadowVaryings input) : SV_Target
{
    FishFilletClip(input.positionCS, input.positionWS, input.h);
    return 0;
}

#endif

// ── Depth only, depth and normals ───────────────────────────────────────────
#if defined(FISH_FILLET_PASS_DEPTH) || defined(FISH_FILLET_PASS_DEPTHNORMALS)

struct FishFilletDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float h : TEXCOORD2;
};

FishFilletDepthVaryings FishFilletDepthVertex(uint vertexID : SV_VertexID)
{
    FishFilletVertex v = FishFilletFetch(vertexID);
    FishFilletDepthVaryings output;
    output.positionCS = TransformWorldToHClip(v.positionWS);
    output.positionWS = v.positionWS;
    output.normalWS = v.normalWS;
    output.h = v.h;
    return output;
}

#if defined(FISH_FILLET_PASS_DEPTH)
half FishFilletDepthFragment(FishFilletDepthVaryings input) : SV_Target
{
    FishFilletClip(input.positionCS, input.positionWS, input.h);
    return input.positionCS.z;
}
#else
half4 FishFilletDepthNormalsFragment(FishFilletDepthVaryings input) : SV_Target
{
    FishFilletClip(input.positionCS, input.positionWS, input.h);
    float3 n = normalize(input.normalWS);
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS = PackNormalOctQuadEncode(n);
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
#else
    return half4(n, 0.0);
#endif
}
#endif

#endif

#endif
