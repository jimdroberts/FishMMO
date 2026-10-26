#ifndef FISHMMO_SEA_FLOATS_INCLUDED
#define FISHMMO_SEA_FLOATS_INCLUDED

// FishMMO/Sea Floats (SeaFloatSystem, FishMMO.Client/World/SeaFloats, 2026-10-25, phase I2p): what drifts on the open sea, each
// instance a 32-byte record FishSeaFloats.compute wrote this frame into its kind's slot, the mesh SeaFloatMeshes' one mesh with
// a submesh a kind. Drawn only with Graphics.RenderMeshIndirect, one command per kind.
//
// ── The instance (FishSeaFloats.compute) ───────────────────────────────────────────────────
//   placed   x, z of its middle (world, flat), its turn about the vertical (radians), its scale (the mesh is in metres of one unit)
//   look     x the share drawn (distance fade), y its hash's top 23 bits, z 1 for a rigid body, w the least water it is drawn over
//
// Which slot a draw reads: on Vulkan the args' startInstance is the slot's start and SV_InstanceID includes it; elsewhere
// startInstance is 0 and the start is _FloatCommandBases[GetCommandID(0)] (one command a call), as FishCaveCover.hlsl.
//
// RIDING THE SEA. Each float rides the very surface the ocean draws (FishWaterSwell.hlsl: the same three cascades, open-sea reach
// and shallows' calm, at the ocean mesh's own vertex footprint for the distance): a weed mat bends to it vertex by vertex, a rigid
// log or berg takes the surface at its middle and tilts to its normal. The sea's amplitude fade with distance is the sea material's
// (_WaveFadeStart, 900 m by default) and is not read: floats are drawn within 800 m (BiomeSeaFloats.MaxDrawMetres), where it is 1.
// Below the surface the daylight dims with depth (FishSeaDaylight) as for everything under the sea; the sea's own surface draws
// over the drowned part.
//
// Mesh: COLOR rgb the albedo (sRGB), a 1; positions in metres of a one-metre unit, y up from the waterline (negative: under it).

#include "FishSurface.hlsl"
#include "FishAmbient.hlsl"
#include "Assets/Plugins/FishMMO Water/Shaders/FishWaterSwell.hlsl"
#include "UnityIndirect.cginc"

struct FloatInstance
{
    float4 placed;
    float4 look;
};

StructuredBuffer<FloatInstance> _FloatInstances;
StructuredBuffer<uint> _FloatCommandBases;

CBUFFER_START(UnityPerMaterial)
    half _FloatSmoothness;
CBUFFER_END

struct FloatAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 color : COLOR;
    uint instanceID : SV_InstanceID;
};

FloatInstance FloatFetch(uint instanceID)
{
#if defined(SHADER_API_VULKAN)
    return _FloatInstances[instanceID];
#else
    return _FloatInstances[_FloatCommandBases[GetCommandID(0)] + instanceID];
#endif
}

/// <summary>A vector in a frame whose up is <paramref name="n"/> (a unit normal near the vertical): the float tilted to the sea.</summary>
float3 FloatTilt(float3 v, float3 n)
{
    float3 bitangent = normalize(cross(float3(1.0, 0.0, 0.0), n));
    float3 tangent = cross(n, bitangent);
    return tangent * v.x + n * v.y + bitangent * v.z;
}

struct FloatVertex
{
    float3 positionWS;
    float3 normalWS;
    float3 color;
    float visible;
};

FloatVertex FloatBuild(FloatAttributes input)
{
    FloatInstance f = FloatFetch(input.instanceID);
    float yaw = f.placed.z;
    float scale = f.placed.w;
    float c = cos(yaw), s = sin(yaw);
    float3 local = input.positionOS.xyz * scale;
    float3 turned = float3(c * local.x + s * local.z, local.y, -s * local.x + c * local.z);
    float3 normal = float3(c * input.normalOS.x + s * input.normalOS.z, input.normalOS.y, -s * input.normalOS.x + c * input.normalOS.z);
    float2 origin = f.placed.xy;

    FloatVertex o;
    // The sea under it: deep enough and clear of the shore, or it is not drawn (a float never runs into the surf).
    float least = f.look.w;
    bool afloat = FishWaterSeabedDepth(origin) >= least && FishWaterShoreDistance(origin) >= 2.0 * least;
    o.visible = afloat ? f.look.x : 0.0;

    float distance = length(origin - _WorldSpaceCameraPos.xz);
    // The ocean mesh's vertex spacing at this distance: the cascades it can resolve, so the float rides what is drawn.
    float footprint = _FishWaterMeshSpacing.x > 0.0 ? _FishWaterMeshSpacing.x * max(distance, _FishWaterMeshSpacing.y) : 0.0;
    if (f.look.z > 0.5)
    {
        float3 up;
        float3 swell = FishWaterSwellAt(origin, footprint, up);
        o.positionWS = float3(origin.x, _FishWaterLevel, origin.y) + swell + FloatTilt(turned, up);
        o.normalWS = normalize(FloatTilt(normal, up));
    }
    else
    {
        float2 flat = origin + turned.xz;
        float3 up;
        float3 swell = FishWaterSwellAt(flat, footprint, up);
        o.positionWS = float3(flat.x, _FishWaterLevel + turned.y, flat.y) + swell;
        o.normalWS = normalize(FloatTilt(normal, up));
    }
    // A little of its own colour: no two clumps alike.
    float jitter = frac(f.look.y * (1.0 / 8388608.0) * 7.31);
    o.color = input.color.rgb * lerp(0.9, 1.1, jitter);
    return o;
}

#if defined(FISH_SEA_FLOATS_PASS_FORWARD) || defined(FISH_SEA_FLOATS_PASS_DEPTHNORMALS)

struct FloatVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 color : TEXCOORD2;
    float2 extra : TEXCOORD3;   // x the share drawn, y fog
};

FloatVaryings FloatVertexMain(FloatAttributes input)
{
    FloatVaryings o = (FloatVaryings)0;
    FloatVertex v = FloatBuild(input);
    o.positionWS = v.positionWS;
    o.normalWS = v.normalWS;
    o.color = v.color;
    // Not drawn: every vertex at one point, so no triangle is left to raster.
    o.positionCS = v.visible > 0.0 ? TransformWorldToHClip(v.positionWS) : float4(0.0, 0.0, -2.0, 1.0);
    o.extra = float2(v.visible, ComputeFogFactor(o.positionCS.z));
    return o;
}

#endif

#if defined(FISH_SEA_FLOATS_PASS_FORWARD)

half4 FloatForwardFragment(FloatVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    FishDitherClip(input.positionCS.xy, input.extra.x);
    float3 n = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
    float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
    float3 albedo = SRGBToLinear(input.color);

#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
    // Under the water the daylight dims with depth (FishSeaDaylight; exactly 1 above it).
    float seaDaylight = FishSeaDaylight(input.positionWS);
    float3 light = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * seaDaylight;
    float nl = dot(n, mainLight.direction);
    // Wrapped a little: weed and wet wood are lit through and round their edges.
    float diffuse = saturate((nl + 0.25) / 1.25);
    float3 colour = albedo * light * diffuse;
    colour += albedo * FishTrilight((half3)n) * seaDaylight;
    // Wet: a narrow highlight.
    float3 h = normalize(mainLight.direction + v);
    colour += light * pow(saturate(dot(n, h)), exp2(10.0 * _FloatSmoothness + 1.0)) * _FloatSmoothness * 0.5;
    colour = MixFog(colour, input.extra.y);
    return half4(colour, 1.0);
}

#endif

#if defined(FISH_SEA_FLOATS_PASS_DEPTHNORMALS)

half4 FloatDepthNormalsFragment(FloatVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    FishDitherClip(input.positionCS.xy, input.extra.x);
    float3 n = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS = PackNormalOctQuadEncode(n);
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
#else
    return half4(n, 0.0);
#endif
}

#endif

#if defined(FISH_SEA_FLOATS_PASS_DEPTH)

struct FloatDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float visible : TEXCOORD0;
};

FloatDepthVaryings FloatDepthVertex(FloatAttributes input)
{
    FloatDepthVaryings o = (FloatDepthVaryings)0;
    FloatVertex v = FloatBuild(input);
    o.positionCS = v.visible > 0.0 ? TransformWorldToHClip(v.positionWS) : float4(0.0, 0.0, -2.0, 1.0);
    o.visible = v.visible;
    return o;
}

half4 FloatDepthFragment(FloatDepthVaryings input) : SV_Target
{
    FishDitherClip(input.positionCS.xy, input.visible);
    return half4(input.positionCS.z, 0.0, 0.0, 0.0);
}

#endif

#endif
