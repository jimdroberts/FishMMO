#ifndef FISHMMO_WILDLIFE_INCLUDED
#define FISHMMO_WILDLIFE_INCLUDED

// Vertex contract (WildlifeBodyMesh): colour rgb the albedo as authored (sRGB); UV1 xyz the pivot of the vertex's part in
// object space, w the part: 0 body, 1 front left, 2 front right, 3 hind left, 4 hind right legs, 5 neck and head, 6 tail.
// The animal faces +Z and stands on y = 0.

#if defined(FISH_WILDLIFE_PASS_FORWARD)
#include "FishAmbient.hlsl"
#endif

CBUFFER_START(UnityPerMaterial)
    half _Smoothness;
    float4 _WildGait;   // x step phase (radians), y leg swing (radians), z gallop 0..1, w body bob (metres)
    float4 _WildPose;   // x attack lunge 0..1, y fallen 0..1, z tail wag (radians), w half the body's width (the side it falls on)
CBUFFER_END

struct WildlifeAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 color : COLOR;
    float4 part : TEXCOORD1;
};

float3 WildRotX(float3 p, float a) { float s, c; sincos(a, s, c); return float3(p.x, p.y * c - p.z * s, p.y * s + p.z * c); }
float3 WildRotY(float3 p, float a) { float s, c; sincos(a, s, c); return float3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c); }
float3 WildRotZ(float3 p, float a) { float s, c; sincos(a, s, c); return float3(p.x * c - p.y * s, p.x * s + p.y * c, p.z); }

// Poses one vertex in object space: legs swing about their hips (a trot's diagonal pairs blending to a gallop's front and
// hind pairs), the head nods with the stride and dips forward to bite, the tail wags, the body bobs, and the whole animal
// rolls onto its side as it dies.
void WildlifePose(inout float3 position, inout float3 normal, float4 part)
{
    int id = (int)round(part.w);
    float3 pivot = part.xyz;
    float phase = _WildGait.x;
    float swing = _WildGait.y;
    float gallop = _WildGait.z;

    if (id >= 1 && id <= 4)
    {
        float trot = (id == 2 || id == 3) ? PI : 0.0;
        float run = id == 1 ? 0.0 : (id == 2 ? 0.6 : (id == 3 ? PI : PI + 0.6));
        float a = swing * sin(phase + lerp(trot, run, gallop));
        position = pivot + WildRotX(position - pivot, a);
        normal = WildRotX(normal, a);
    }
    else if (id == 5)
    {
        float attack = _WildPose.x;
        float a = 0.06 * sin(2.0 * phase) * swing + attack * 0.55;
        position = pivot + WildRotX(position - pivot, a);
        normal = WildRotX(normal, a);
        position.z += attack * 0.18;
    }
    else if (id == 6)
    {
        float a = _WildPose.z;
        position = pivot + WildRotY(position - pivot, a);
        normal = WildRotY(normal, a);
    }

    position.y += _WildGait.w * abs(sin(phase));

    float fall = -_WildPose.y * 0.5 * PI;
    float3 side = float3(_WildPose.w, 0.0, 0.0);
    position = side + WildRotZ(position - side, fall);
    normal = WildRotZ(normal, fall);
}

#if defined(FISH_WILDLIFE_PASS_FORWARD) || defined(FISH_WILDLIFE_PASS_DEPTHNORMALS)

struct WildlifeVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 color : TEXCOORD2;
    float fog : TEXCOORD3;
};

WildlifeVaryings WildlifeVertex(WildlifeAttributes input)
{
    WildlifeVaryings o = (WildlifeVaryings)0;
    float3 position = input.positionOS.xyz;
    float3 normal = input.normalOS;
    WildlifePose(position, normal, input.part);
    o.positionWS = TransformObjectToWorld(position);
    o.normalWS = TransformObjectToWorldNormal(normal);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.color = input.color.rgb;
#if defined(FISH_WILDLIFE_PASS_FORWARD)
    o.fog = ComputeFogFactor(o.positionCS.z);
#endif
    return o;
}

#endif

#if defined(FISH_WILDLIFE_PASS_FORWARD)

#if defined(_ADDITIONAL_LIGHTS)
// Torches, lanterns and carried lights: wrapped Lambert as the sun's, no shadows (as FishAmbientLife).
float3 WildlifeAdditionalLight(float3 positionWS, float3 n, float2 screenUV)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = screenUV;
    float3 sum = 0.0;
    uint pixelLightCount = GetAdditionalLightsCount();
#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        Light light = GetAdditionalLight(lightIndex, positionWS);
        sum += light.color * light.distanceAttenuation * saturate((dot(n, light.direction) + 0.35) / 1.35);
    }
#endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS);
        sum += light.color * light.distanceAttenuation * saturate((dot(n, light.direction) + 0.35) / 1.35);
    LIGHT_LOOP_END
    return sum;
}
#endif

half4 WildlifeForwardFragment(WildlifeVaryings input) : SV_Target
{
    float3 n = normalize(input.normalWS);
    float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
    // Authored as sRGB colours.
    float3 albedo = pow(saturate(input.color), 2.2);

#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
    float3 light = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation);
    float nl = dot(n, mainLight.direction);
    // Wrapped: fur scatters light round the body's edge.
    float3 colour = albedo * light * saturate((nl + 0.35) / 1.35);
    colour += albedo * FishTrilight((half3)n);
    float3 h = normalize(mainLight.direction + v);
    colour += light * pow(saturate(dot(n, h)), exp2(9.0 * _Smoothness + 1.0)) * _Smoothness * 0.5;
#if defined(_ADDITIONAL_LIGHTS)
    colour += albedo * WildlifeAdditionalLight(input.positionWS, n, GetNormalizedScreenSpaceUV(input.positionCS));
#endif
    colour = MixFog(colour, input.fog);
    return half4(colour, 1.0);
}

#endif

#if defined(FISH_WILDLIFE_PASS_DEPTHNORMALS)

half4 WildlifeDepthNormalsFragment(WildlifeVaryings input) : SV_Target
{
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

#if defined(FISH_WILDLIFE_PASS_SHADOW) || defined(FISH_WILDLIFE_PASS_DEPTH)

struct WildlifeDepthVaryings
{
    float4 positionCS : SV_POSITION;
};

#if defined(FISH_WILDLIFE_PASS_SHADOW)
float3 _LightDirection;
float3 _LightPosition;
#endif

WildlifeDepthVaryings WildlifeDepthVertex(WildlifeAttributes input)
{
    WildlifeDepthVaryings o = (WildlifeDepthVaryings)0;
    float3 position = input.positionOS.xyz;
    float3 normal = input.normalOS;
    WildlifePose(position, normal, input.part);
    float3 positionWS = TransformObjectToWorld(position);
#if defined(FISH_WILDLIFE_PASS_SHADOW)
    float3 normalWS = TransformObjectToWorldNormal(normal);
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
#else
    o.positionCS = TransformWorldToHClip(positionWS);
#endif
    return o;
}

half4 WildlifeDepthFragment(WildlifeDepthVaryings input) : SV_Target
{
    return half4(input.positionCS.z, 0.0, 0.0, 0.0);
}

#endif

#if defined(FISH_WILDLIFE_PASS_OUTLINE)

#include "Assets/Prefabs/Client/Materials/Outline/FishTargetOutlineMask.hlsl"

struct WildlifeOutlineVaryings
{
    float4 positionCS : SV_POSITION;
};

// The target outline's mask in the pose: the same WildlifePose as the lit pass, so the outline follows the gait, the bite
// and the fall.
WildlifeOutlineVaryings WildlifeOutlineVertex(WildlifeAttributes input)
{
    WildlifeOutlineVaryings o = (WildlifeOutlineVaryings)0;
    float3 position = input.positionOS.xyz;
    float3 normal = input.normalOS;
    WildlifePose(position, normal, input.part);
    o.positionCS = TransformWorldToHClip(TransformObjectToWorld(position));
    return o;
}

half4 WildlifeOutlineFragment(WildlifeOutlineVaryings input) : SV_Target
{
    return FishOutlineMaskFragment(input.positionCS);
}

#endif

#endif
