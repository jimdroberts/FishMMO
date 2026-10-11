#ifndef FISHMMO_AMBIENT_LIFE_INCLUDED
#define FISHMMO_AMBIENT_LIFE_INCLUDED

// FishMMO/Ambient Life: the land's background creatures (AmbientLifeSystem, FishMMO.Client/World/AmbientLife) —
// birds, bats, rodents, rabbits, lizards, crabs, frogs. Drawn only with Graphics.DrawMeshInstanced, one material
// per kind; where each animal is and which way it faces is worked out on the CPU from the shared world clock. What
// the shader adds is the movement of the body itself — wing beats and folding, strides and bounds, a lizard's
// wriggle, a bird's head turning — from the phase the CPU hands each instance.
//
// The meshes (AmbientCreatureMeshes) are one unit long, nose toward +z, back up +y, standing on the origin; the
// instance matrix scales and places them. Vertex data:
//   COLOR       rgb the animal's own colour (countershaded)
//   TEXCOORD0   x how far along the body (0 nose .. 1 tail), y across a wing's chord (0 leading .. 1 trailing edge)
//   TEXCOORD1   x which part (birds 0 body, 1 left wing, 2 right wing, 3 legs, 4 head, 5 tail; walkers 0 body,
//               1-4 legs FL FR HL HR, 5 tail, 6 head; crabs 1-8 legs, 9 claws), y how far out along it (0 root .. 1 tip)
//
// Per instance (instanced properties):
//   _Anim       x the stroke's phase in cycles (wing beat, stride), y its strength (birds: 0 gliding .. 1 beating;
//               walkers: 0 still .. 1 striding), z birds: 0 wings open, legs tucked .. 1 wings folded, standing;
//               walkers: 1 for a bounding gait (rabbits, squirrels, frogs), w birds: the head's turn (radians);
//               walkers: a pose (a rabbit sitting up, a lizard's push-up, sniffing)
//   _Tint       rgb times the mesh colour, a how much of the animal is drawn (the distance and fright dither)

#include "FishSurface.hlsl"
#include "FishAmbient.hlsl"

CBUFFER_START(UnityPerMaterial)
    half _Mode;          // 0 bird or bat, 1 four-legged walker, 2 lizard, 3 crab
    half _Amplitude;     // birds: the beat's half-angle (radians); walkers: the stride, as a share of the body's length
    half _Dihedral;      // birds: the wings' angle above level while gliding (radians)
    half _Smoothness;
    float4 _Shoulder;    // birds: the wings' shoulder (x out, y up, z along), w the neck's z (the head turns about it)
CBUFFER_END

UNITY_INSTANCING_BUFFER_START(AmbientLifeProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Anim)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
UNITY_INSTANCING_BUFFER_END(AmbientLifeProps)

struct AmbientLifeAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 color : COLOR;
    float2 body : TEXCOORD0;
    float2 part : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float2 AmbientRotate(float2 v, float angle)
{
    float c = cos(angle), s = sin(angle);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// A bird's or a bat's wings, legs and head.
void AmbientFly(inout float3 p, inout float3 n, float2 body, float2 part, float4 anim)
{
    int id = (int)(part.x + 0.5);
    float fold = saturate(anim.z);
    if (id == 1 || id == 2)
    {
        float side = id == 1 ? -1.0 : 1.0;
        float span = saturate(part.y);
        // The beat runs out along the wing, the tips a little behind the arm; centred a little above level
        // (birds lift their wings higher than they drop them). Gliding, they hold their dihedral.
        float beat = sin(anim.x * 6.2831853 - span * 0.9);
        float angle = lerp(_Dihedral, _Amplitude * (beat + 0.25), saturate(anim.y)) * side;
        float2 fromShoulder = float2(p.x - side * _Shoulder.x, p.y - _Shoulder.y);
        float2 turned = AmbientRotate(fromShoulder, angle);
        float3 open = float3(side * _Shoulder.x + turned.x, _Shoulder.y + turned.y, p.z);
        // Folded: the hand laid back along the flank, the arm tucked under the shoulder.
        float3 folded = float3(side * (_Shoulder.x * 0.95 + 0.012 * span), _Shoulder.y + 0.01 - 0.03 * span,
            _Shoulder.z - span * 0.42 + (p.z - _Shoulder.z) * 0.25);
        p = lerp(open, folded, fold);
        float3 openNormal = float3(AmbientRotate(n.xy, angle), n.z);
        n = normalize(lerp(openNormal, float3(side, 0.4, 0.0), fold));
    }
    else if (id == 3)
    {
        // Legs: down when standing, tucked up under the body in flight.
        p.y = lerp(_Shoulder.y - 0.1, p.y, fold);
        p.z = lerp(p.z - 0.06, p.z, fold);
    }
    else if (id == 4)
    {
        // The head turns about the neck to look round.
        float2 xz = AmbientRotate(float2(p.x, p.z - _Shoulder.w), -anim.w);
        p.x = xz.x;
        p.z = xz.y + _Shoulder.w;
        n.xz = AmbientRotate(n.xz, -anim.w);
    }
    else if (id == 5)
    {
        // The tail spreads a little as the wings beat, and dips on the upstroke.
        p.y += 0.02 * part.y * sin(anim.x * 6.2831853) * saturate(anim.y) * (1.0 - fold);
    }
}

// A four-legged walker: trotting or bounding legs, a swaying tail, a bobbing head.
void AmbientWalk(inout float3 p, inout float3 n, float2 body, float2 part, float4 anim)
{
    int id = (int)(part.x + 0.5);
    float phase = anim.x * 6.2831853;
    float stride = saturate(anim.y);
    bool bound = anim.z > 0.5;
    if (id >= 1 && id <= 4)
    {
        // A trot moves diagonal pairs together; a bound the front pair, then the hind pair.
        float offset = bound ? (id <= 2 ? 0.0 : 2.2) : ((id == 1 || id == 4) ? 0.0 : 3.14159);
        float reach = part.y;
        p.z += sin(phase + offset) * _Amplitude * stride * reach;
        p.y += max(0.0, cos(phase + offset)) * _Amplitude * 0.5 * stride * reach;
    }
    else if (id == 5)
    {
        p.x += sin(phase * 0.5 + part.y * 2.0) * 0.06 * part.y * (0.3 + stride);
        p.y += 0.04 * part.y * anim.w;
    }
    else if (id == 6)
    {
        // Sniffing at rest; the head up when alert.
        p.y += 0.008 * sin(phase * 3.0) * (1.0 - stride) + 0.02 * anim.w;
    }
    // The whole body: up off the ground at each bound, a small bob at each trotting step.
    float lift = bound ? 0.22 * max(0.0, sin(phase)) * stride : 0.012 * abs(sin(phase * 2.0)) * stride;
    p.y += lift;
}

// A lizard: the body's sideways wave, sprawling legs in diagonal pairs, push-ups at rest.
void AmbientWriggle(inout float3 p, inout float3 n, float2 body, float2 part, float4 anim)
{
    int id = (int)(part.x + 0.5);
    float phase = anim.x * 6.2831853;
    float stride = saturate(anim.y);
    float along = 0.5 - p.z; // 0 at the snout, more toward the tail
    float wave = sin(phase - along * 2.4) * (0.25 + 0.3 * along * along);
    if (id == 0 || id == 5 || id == 6)
    {
        p.x += 0.07 * stride * wave + 0.02 * (1.0 - stride) * sin(phase * 0.3 - along * 1.5) * saturate(along - 0.6);
    }
    if (id >= 1 && id <= 4)
    {
        float offset = (id == 1 || id == 4) ? 0.0 : 3.14159;
        p.z += sin(phase + offset) * _Amplitude * stride * part.y;
        p.y += max(0.0, cos(phase + offset)) * _Amplitude * 0.4 * stride * part.y;
        p.x += 0.07 * stride * wave;
    }
    // Push-ups: the front of the body pressed up off the ground.
    p.y += 0.04 * anim.w * saturate(p.z + 0.25) * (id >= 3 && id <= 4 ? 0.0 : 1.0);
}

// A crab's legs stepping in alternating sets, its claws lifting.
void AmbientScuttle(inout float3 p, inout float3 n, float2 body, float2 part, float4 anim)
{
    int id = (int)(part.x + 0.5);
    float phase = anim.x * 6.2831853;
    float stride = saturate(anim.y);
    if (id >= 1 && id <= 8)
    {
        float set = (id % 2 == 0) ? 0.0 : 3.14159;
        float step = sin(phase + set + id * 0.6);
        p.y += _Amplitude * (0.2 + stride) * part.y * max(0.0, step);
        p.x += _Amplitude * 0.6 * stride * part.y * cos(phase + set + id * 0.6);
    }
    else if (id == 9)
    {
        p.y += 0.05 * part.y * (0.5 + 0.5 * sin(phase * 0.5));
    }
}

void AmbientMove(inout float3 p, inout float3 n, float2 body, float2 part, float4 anim)
{
    int mode = (int)(_Mode + 0.5);
    if (mode == 0)
    {
        AmbientFly(p, n, body, part, anim);
    }
    else if (mode == 1)
    {
        AmbientWalk(p, n, body, part, anim);
    }
    else if (mode == 2)
    {
        AmbientWriggle(p, n, body, part, anim);
    }
    else
    {
        AmbientScuttle(p, n, body, part, anim);
    }
}

struct AmbientLifeVertex
{
    float3 positionWS;
    float3 normalWS;
    float3 color;
    float visible;
};

AmbientLifeVertex AmbientLifeBuild(AmbientLifeAttributes input)
{
    float4 anim = UNITY_ACCESS_INSTANCED_PROP(AmbientLifeProps, _Anim);
    float4 tint = UNITY_ACCESS_INSTANCED_PROP(AmbientLifeProps, _Tint);
    float3 p = input.positionOS.xyz;
    float3 n = input.normalOS;
    AmbientMove(p, n, input.body, input.part, anim);
    AmbientLifeVertex o;
    o.positionWS = TransformObjectToWorld(p);
    o.normalWS = TransformObjectToWorldNormal(n);
    o.color = input.color.rgb * tint.rgb;
    o.visible = tint.a;
    return o;
}

#if defined(FISH_AMBIENT_LIFE_PASS_FORWARD) || defined(FISH_AMBIENT_LIFE_PASS_DEPTHNORMALS)

struct AmbientLifeVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 color : TEXCOORD2;
    float2 extra : TEXCOORD3;   // x visible share, y fog
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

AmbientLifeVaryings AmbientLifeVertexMain(AmbientLifeAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    AmbientLifeVaryings o = (AmbientLifeVaryings)0;
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    AmbientLifeVertex v = AmbientLifeBuild(input);
    o.positionWS = v.positionWS;
    o.normalWS = v.normalWS;
    o.color = v.color;
    o.positionCS = TransformWorldToHClip(v.positionWS);
    o.extra = float2(v.visible, ComputeFogFactor(o.positionCS.z));
    return o;
}

#endif

#if defined(FISH_AMBIENT_LIFE_PASS_FORWARD)

half4 AmbientLifeForwardFragment(AmbientLifeVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    FishDitherClip(input.positionCS.xy, input.extra.x);
    float facing = IS_FRONT_VFACE(face, 1.0, -1.0);
    float3 n = normalize(input.normalWS) * facing;
    float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
    float3 albedo = input.color;

#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    Light mainLight = GetMainLight(shadowCoord);
    float3 light = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation);
    float nl = dot(n, mainLight.direction);
    // Wrapped: fur and feathers scatter light round the body's edge.
    float diffuse = saturate((nl + 0.35) / 1.35);
    float3 colour = albedo * light * diffuse;
    // Ambient from the sky's trilight (not SampleSH, which reads zero for a DrawMeshInstanced draw here).
    colour += albedo * FishTrilight((half3)n);
    // A thin wing or ear lit from behind glows a little through.
    colour += albedo * light * 0.25 * saturate(-nl) * saturate(1.0 - abs(dot(n, v)));
    float3 h = normalize(mainLight.direction + v);
    float spec = pow(saturate(dot(n, h)), exp2(9.0 * _Smoothness + 1.0)) * _Smoothness * 0.5;
    colour += light * spec;
    colour = MixFog(colour, input.extra.y);
    return half4(colour, 1.0);
}

#endif

#if defined(FISH_AMBIENT_LIFE_PASS_DEPTHNORMALS)

half4 AmbientLifeDepthNormalsFragment(AmbientLifeVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
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

#if defined(FISH_AMBIENT_LIFE_PASS_SHADOW) || defined(FISH_AMBIENT_LIFE_PASS_DEPTH)

struct AmbientLifeDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float visible : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

#if defined(FISH_AMBIENT_LIFE_PASS_SHADOW)
float3 _LightDirection;
float3 _LightPosition;
#endif

AmbientLifeDepthVaryings AmbientLifeDepthVertex(AmbientLifeAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    AmbientLifeDepthVaryings o = (AmbientLifeDepthVaryings)0;
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    AmbientLifeVertex v = AmbientLifeBuild(input);
#if defined(FISH_AMBIENT_LIFE_PASS_SHADOW)
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 lightDirectionWS = normalize(_LightPosition - v.positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    // Wings and ears are thin and double-sided: bias along the normal turned to the light.
    float3 n = dot(v.normalWS, lightDirectionWS) < 0.0 ? -v.normalWS : v.normalWS;
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(v.positionWS, n, lightDirectionWS)));
#else
    o.positionCS = TransformWorldToHClip(v.positionWS);
#endif
    o.visible = v.visible;
    return o;
}

half4 AmbientLifeDepthFragment(AmbientLifeDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    FishDitherClip(input.positionCS.xy, input.visible);
    return half4(input.positionCS.z, 0.0, 0.0, 0.0);
}

#endif

#endif
