#ifndef FISH_CAVE_COVER_INCLUDED
#define FISH_CAVE_COVER_INCLUDED

// The run-time cave cover's draw (FishMMO/Cave Cover, CaveCoverRenderer, 2026-10-20): every instance is a 32-byte record
// FishCaveCover.compute wrote this frame into its (type, level) slot; the mesh is the cover art's one mesh (CaveCover_Set),
// a submesh a type and level. No textures: the vertex colour is the albedo (sRGB) and, below 255 in alpha, the glowing part;
// UV0.x the height up the item (its foot shaded).
//
// ── The instance (FishCaveCover.compute CoverEmit) ──────────────────────────────────────────
//   positionScale   xyz world position (on the rock, sunk by the type's sink), w uniform scale
//   data.x          its up, octahedral, two 16-bit halves
//   data.y          bits 0..15 its turn about its up (of the canonical frame, CoverFrame), 16..23 the share faded out,
//                   24..31 the rock's wet
//   data.z          bits 0..23 a hash (colour jitter, shimmer phase), 24..31 its type
//   data.w          bits 0..7 the rock's glow (times the cave's), 8..15 the cave's palette slot
//
// Which slot a draw reads: on Vulkan the args' startInstance is the slot's start and SV_InstanceID includes it; elsewhere
// startInstance is 0 and the start is _CoverCommandBases[GetCommandID(0)] (one command a call), as FishIndirectInstancing.hlsl.
//
// Lit as the cave's own rock is: the sun and every other light (the cave's lanterns, torches and crystal lights), the sky's
// trilight (never SampleSH: nothing binds probes to these draws), all darkened by how deep underground it is (FishUnderground,
// as FishWeatherLitForwardPass.hlsl darkens the rock), the fog only in the open. The glow adds after the lighting, so the
// cave's darkness never dims it: it shimmers slowly on the shared motion clock (_FishWeatherMisc.w), the same on every client.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "UnityIndirect.cginc"

struct CoverInstance { float4 positionScale; uint4 data; };

StructuredBuffer<CoverInstance> _CoverInstances;
StructuredBuffer<uint> _CoverCommandBases;
// Per cave palette slot three glow colours (HDR): the leading fungus hue, the accent, the crystal hue.
StructuredBuffer<float4> _CoverPalettes;
// Per type two rows: x smoothness, y how much the wet raises it, z colour jitter, w shimmer; x glow slot (0 none, 1 fungus,
// 2 accent, 3 crystal, 4 flame, 5 ember).
StructuredBuffer<float4> _CoverLooks;

CBUFFER_START(UnityPerMaterial)
    float4 _CoverGlow;     // x strength, y shimmer amount, z shimmer speed (cycles a second), w foot shade (share of light at the foot)
    float4 _CoverDarkness; // x how much of the sun and sky underground takes away (0..1), y highlight (debug, 1 magenta)
CBUFFER_END

// The shared motion clock (FishWeather.hlsl's, set by WeatherShaderGlobals); w seconds.
float4 _FishWeatherMisc;

struct CoverUnpacked
{
    float3 position;
    float scale;
    float3 up;
    float3 x;
    float3 z;
    float fadeOut;
    float wet;
    uint seed;
    uint type;
    float glow;
    uint cave;
};

float3 CoverUnpackOct(uint packed)
{
    float2 e = float2(packed & 0xFFFFu, packed >> 16) / 65535.0 * 2.0 - 1.0;
    float3 n = float3(e.x, 1.0 - abs(e.x) - abs(e.y), e.y);
    if (n.y < 0.0)
    {
        float2 flipped = (1.0 - abs(n.zx)) * float2(n.x >= 0.0 ? 1.0 : -1.0, n.z >= 0.0 ? 1.0 : -1.0);
        n.x = flipped.x;
        n.z = flipped.y;
    }
    return normalize(n);
}

// FishCaveCover.compute CoverFrame: the canonical frame about an up.
void CoverFrame(float3 up, out float3 x0, out float3 z0)
{
    float3 helper = abs(up.y) < 0.99 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    x0 = normalize(cross(helper, up));
    z0 = cross(x0, up);
}

CoverUnpacked CoverFetch(uint instanceID)
{
#if defined(SHADER_API_VULKAN)
    CoverInstance instance = _CoverInstances[instanceID];
#else
    CoverInstance instance = _CoverInstances[_CoverCommandBases[GetCommandID(0)] + instanceID];
#endif
    CoverUnpacked u;
    u.position = instance.positionScale.xyz;
    u.scale = instance.positionScale.w;
    u.up = CoverUnpackOct(instance.data.x);
    float yaw = (float)(instance.data.y & 0xFFFFu) / 65535.0 * 6.2831853;
    float3 x0, z0;
    CoverFrame(u.up, x0, z0);
    float c = cos(yaw), s = sin(yaw);
    u.x = x0 * c + z0 * s;
    u.z = -x0 * s + z0 * c;
    u.fadeOut = (float)((instance.data.y >> 16) & 0xFFu) / 255.0;
    u.wet = (float)(instance.data.y >> 24) / 255.0;
    u.seed = instance.data.z & 0xFFFFFFu;
    u.type = instance.data.z >> 24;
    u.glow = (float)(instance.data.w & 0xFFu) / 255.0;
    u.cave = (instance.data.w >> 8) & 0xFFu;
    return u;
}

float3 CoverToWorld(CoverUnpacked u, float3 v)
{
    return u.position + (u.x * v.x + u.up * v.y + u.z * v.z) * u.scale;
}

float3 CoverDirectionToWorld(CoverUnpacked u, float3 v)
{
    return u.x * v.x + u.up * v.y + u.z * v.z;
}

float CoverUnitOf(uint seed, uint stream)
{
    uint x = seed ^ (stream + 1u) * 0x165667B1u;
    x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
    return (float)(x >> 8) * (1.0 / 16777216.0);
}

// The dithered fade (a thinned instance dissolving at its ring's keep): the same pattern in every pass.
void CoverFadeClip(float4 positionCS, float fadeOut)
{
    if (fadeOut > 0.0)
    {
        float2 p = floor(positionCS.xy);
        float dither = frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
        clip(dither - fadeOut);
    }
}

struct CoverAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 color : COLOR;
    float2 uv : TEXCOORD0;
    uint instanceID : SV_InstanceID;
};

#if defined(FISH_COVER_PASS_FORWARD) || defined(FISH_COVER_PASS_DEPTHNORMALS)

struct CoverVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 albedo : TEXCOORD2;      // rgb linear albedo, a the glow mask (1 − vertex alpha)
    float4 extra : TEXCOORD3;       // x faded out, y height up the item, z wet, w fog factor
    nointerpolation uint4 look : TEXCOORD4;   // x type, y cave slot, z seed, w glow (0..255)
};

CoverVaryings CoverVertexMain(CoverAttributes input)
{
    CoverUnpacked u = CoverFetch(input.instanceID);
    CoverVaryings o = (CoverVaryings)0;
    float3 positionWS = CoverToWorld(u, input.positionOS.xyz);
    o.positionWS = positionWS;
    o.positionCS = TransformWorldToHClip(positionWS);
    o.normalWS = normalize(CoverDirectionToWorld(u, input.normalOS));
    float4 look = _CoverLooks[u.type * 2u];
    // Each instance a little lighter or darker and warmer or cooler than its art, by its hash.
    float value = 1.0 + (CoverUnitOf(u.seed, 0u) - 0.5) * 2.0 * look.z;
    float warm = (CoverUnitOf(u.seed, 1u) - 0.5) * look.z;
    float3 albedo = SRGBToLinear(saturate(input.color.rgb)) * value * float3(1.0 + warm, 1.0, 1.0 - warm);
    o.albedo = float4(saturate(albedo), 1.0 - input.color.a);
    o.extra = float4(u.fadeOut, input.uv.x, u.wet, ComputeFogFactor(o.positionCS.z));
    o.look = uint4(u.type, u.cave, u.seed, (uint)round(u.glow * 255.0));
    return o;
}

#endif

#if defined(FISH_COVER_PASS_FORWARD)

#include "FishAmbient.hlsl"
#include "FishGroundMap.hlsl"

float3 CoverLit(float3 L, float3 radiance, float3 albedo, float3 n, float3 V, float smoothness)
{
    float wrap = saturate((dot(n, L) + 0.25) / 1.25);
    float3 lit = albedo * radiance * wrap;
    float3 H = normalize(L + V);
    float power = exp2(10.0 * smoothness + 1.0);
    lit += radiance * pow(saturate(dot(n, H)), power) * (power + 2.0) / 8.0 * 0.04 * saturate(dot(n, L));
    return lit;
}

float3 CoverGlowColour(uint slot, uint cave)
{
    if (slot == 4u)
    {
        return float3(1.6, 0.85, 0.35);
    }
    if (slot == 5u)
    {
        return float3(1.7, 0.5, 0.14);
    }
    if (slot >= 1u && slot <= 3u)
    {
        return _CoverPalettes[cave * 3u + (slot - 1u)].rgb;
    }
    return float3(0.0, 0.0, 0.0);
}

float4 CoverShadowCoord(float3 positionWS)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    return ComputeScreenPos(TransformWorldToHClip(positionWS));
#else
    return TransformWorldToShadowCoord(positionWS);
#endif
}

half4 CoverForwardFragment(CoverVaryings input) : SV_Target
{
    CoverFadeClip(input.positionCS, input.extra.x);
    float3 n = normalize(input.normalWS);
    float3 V = normalize(_WorldSpaceCameraPos - input.positionWS);
    float4 look = _CoverLooks[input.look.x * 2u];
    float4 look2 = _CoverLooks[input.look.x * 2u + 1u];
    float wet = input.extra.z;
    // Wet rock's cover darkens and shines (FishSurface.hlsl's damp, in short).
    float3 albedo = input.albedo.rgb * (1.0 - 0.3 * wet * look.y);
    float smoothness = saturate(look.x + wet * look.y);
    float foot = lerp(_CoverGlow.w, 1.0, saturate(input.extra.y * 1.5));

    // Underground the sky reaches nothing and the sun little (FishUnderground, as the rock's own forward pass).
    float under = (float)FishUnderground(input.positionWS);
    float daylight = 1.0 - _CoverDarkness.x * under;

    Light mainLight = GetMainLight(CoverShadowCoord(input.positionWS), input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
    // In the sun's beam through an opening in the roof (2026-10-20, FishSkylights.hlsl) its shadowed light is kept.
    float sunDaylight = 1.0 - _CoverDarkness.x * under * (1.0 - FishSkylightSun(input.positionWS, mainLight.direction));
    float3 colour = CoverLit(mainLight.direction, mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * sunDaylight,
        albedo, n, V, smoothness) * foot;

#if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    half4 noMask = half4(1.0, 1.0, 1.0, 1.0);
#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, input.positionWS, noMask);
        colour += CoverLit(light.direction, light.color * light.distanceAttenuation * daylight, albedo, n, V, smoothness) * foot;
    }
#endif
    uint additionalCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(additionalCount)
        Light light = GetAdditionalLight(lightIndex, input.positionWS, noMask);
        colour += CoverLit(light.direction, light.color * light.distanceAttenuation, albedo, n, V, smoothness) * foot;
    LIGHT_LOOP_END
#endif

    // The sky's trilight, darkened underground (× (1 − 0.96 u); the rock's own occlusion now takes all of it, for its reflections).
    colour += albedo * FishTrilight((half3)n) * foot * (1.0 - 0.96 * under);

    // The glow, after the lighting: the cave's colour for the type's slot, brighter where the rock's glow gathers, shimmering slowly.
    uint slot = (uint)(look2.x + 0.5);
    if (slot != 0u && input.albedo.a > 0.0)
    {
        float t = _FishWeatherMisc.w;
        float phase = CoverUnitOf(input.look.z, 2u);
        float wave = sin(6.2831853 * frac(_CoverGlow.z * t + phase)) * sin(6.2831853 * frac(_CoverGlow.z * 0.37 * t + phase * 1.7));
        float shimmer = 1.0 + _CoverGlow.y * look.w * wave;
        float habitat = 0.6 + 0.8 * ((float)input.look.w / 255.0);
        colour += CoverGlowColour(slot, input.look.y) * input.albedo.a * _CoverGlow.x * shimmer * habitat;
    }

    // The fog only in the open: a cave's air is its own (FishOpenAir's rule for the rock).
    float3 fogged = MixFog(colour, input.extra.w);
    colour = lerp(fogged, colour, under);
    if (_CoverDarkness.y > 0.5)
    {
        colour = float3(1.0, 0.0, 1.0);
    }
    return half4(colour, 1.0);
}

#endif

#if defined(FISH_COVER_PASS_DEPTHNORMALS)

half4 CoverDepthNormalsFragment(CoverVaryings input) : SV_Target
{
    CoverFadeClip(input.positionCS, input.extra.x);
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

#if defined(FISH_COVER_PASS_DEPTH)

struct CoverDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float fadeOut : TEXCOORD0;
};

CoverDepthVaryings CoverDepthVertex(CoverAttributes input)
{
    CoverUnpacked u = CoverFetch(input.instanceID);
    CoverDepthVaryings o = (CoverDepthVaryings)0;
    o.positionCS = TransformWorldToHClip(CoverToWorld(u, input.positionOS.xyz));
    o.fadeOut = u.fadeOut;
    return o;
}

half4 CoverDepthFragment(CoverDepthVaryings input) : SV_Target
{
    CoverFadeClip(input.positionCS, input.fadeOut);
    return half4(input.positionCS.z, 0.0, 0.0, 0.0);
}

#endif

#endif
