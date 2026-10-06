#ifndef FISHMMO_SEA_LIFE_INCLUDED
#define FISHMMO_SEA_LIFE_INCLUDED

// FishMMO/Sea Life: the background creatures of the sea (SeaLifeSystem, FishMMO.Client/World/SeaLife).
// Drawn only with Graphics.DrawMeshInstanced, one material per kind; where each animal is and which way
// it heads is worked out on the CPU from the shared world clock, so every player sees the same school in
// the same place. What the shader adds is the swimming itself — a body wave, a wing beat, a bell's pulse —
// from the phase the CPU hands each instance.
//
// The meshes (SeaCreatureMeshes) are one unit long, nose toward +z, back up +y; the instance matrix
// scales and places them. Vertex data:
//   COLOR       rgb the animal's own colour (countershaded: dark back, pale belly), a its opacity (jellies)
//   TEXCOORD0   x how far along the body (0 nose .. 1 tail tip; a jelly: 0 crown .. 1 rim, tentacles
//               on to their tips), y the share out from the midline (rays' wings, crabs' legs)
//   TEXCOORD1   x which appendage (0 body; turtle flippers 1-4; jelly tentacles 1; crab legs 1-8, claws 9),
//               y how far out along it (0 where it joins .. 1 its tip)
//
// Per instance (instanced properties):
//   _Anim       x the stroke's phase in cycles (0..1, from the clock on the CPU), y the stroke's strength
//               (1 cruising, more when hurrying), z the turn (-1 .. 1: the body bent into the turn), w spare
//   _Tint       rgb times the mesh colour (a school's members differ a little; reef fish take their colour
//               from it), a how much of the animal is drawn (the distance dither; a jelly's opacity)

#include "FishSurface.hlsl"
#include "FishAmbient.hlsl"

CBUFFER_START(UnityPerMaterial)
    half _Mode;          // 0 fish (side to side), 1 whale (up and down), 2 ray (wings), 3 turtle (flippers), 4 jelly (pulse), 5 crab (legs)
    half _Amplitude;     // the stroke's swing, as a share of the body's length
    half _WaveLength;    // the body wave's length, in body lengths
    half _Smoothness;
    half _Translucent;   // 1 for a jelly: lit through, edges glowing, drawn blended
    half _SrcBlend;
    half _DstBlend;
    half _ZWrite;
CBUFFER_END

UNITY_INSTANCING_BUFFER_START(SeaLifeProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Anim)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
UNITY_INSTANCING_BUFFER_END(SeaLifeProps)

struct SeaLifeAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 color : COLOR;
    float2 body : TEXCOORD0;
    float2 part : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// The animal's stroke, in its own (unit-length) space: where the vertex is now, and its normal.
void SeaLifeSwim(inout float3 p, inout float3 n, float2 body, float2 part, float4 anim)
{
    float phase = anim.x * 6.2831853;
    float strength = anim.y;
    float turn = anim.z;
    float a = body.x;
    int mode = (int)(_Mode + 0.5);
    if (mode == 0 || mode == 1)
    {
        // A wave running nose to tail, growing toward the tail (the head barely moves), the body
        // bent into the turn. A fish sweeps side to side; a whale's flukes beat up and down.
        float envelope = 0.03 + a * a;
        float wave = sin(phase - a * 6.2831853 / max(0.2, _WaveLength));
        float swing = _Amplitude * strength * envelope * wave;
        float bend = turn * 0.25 * a * a;
        // The slope of the swing along the body turns the normal with it.
        float slope = _Amplitude * strength * envelope * cos(phase - a * 6.2831853 / max(0.2, _WaveLength)) * 6.2831853 / max(0.2, _WaveLength);
        if (mode == 0)
        {
            p.x += swing + bend;
            n = normalize(n + float3(0.0, 0.0, -slope * n.x));
        }
        else
        {
            p.y += swing;
            p.x += bend;
            n = normalize(n + float3(0.0, 0.0, -slope * n.y));
        }
    }
    else if (mode == 2)
    {
        // Wings beat in a wave from the front of the wing to its trailing edge, the tips furthest.
        float span = saturate(body.y);
        float beat = sin(phase - a * 2.2);
        p.y += _Amplitude * strength * pow(span, 1.6) * beat;
        // The tail trails the beat.
        p.x += turn * 0.2 * a * a + (part.x > 0.5 ? 0.03 * sin(phase * 0.5 - a * 4.0) : 0.0);
        n = normalize(n + float3(-sign(p.x) * 1.6 * _Amplitude * strength * pow(span, 0.6) * beat, 0.0, 0.0));
    }
    else if (mode == 3)
    {
        // Front flippers beat down and back together (a turtle flies through the water); the rear
        // ones paddle small and steer.
        int flipper = (int)(part.x + 0.5);
        float reach = part.y;
        if (flipper == 1 || flipper == 2)
        {
            float stroke = sin(phase);
            p.y += _Amplitude * strength * reach * stroke;
            p.z -= _Amplitude * strength * 0.5 * reach * (0.5 + 0.5 * cos(phase));
        }
        else if (flipper == 3 || flipper == 4)
        {
            p.y += _Amplitude * strength * 0.35 * reach * sin(phase + (flipper == 3 ? 0.0 : 3.14159));
            p.x += turn * 0.1 * reach * (flipper == 3 ? -1.0 : 1.0);
        }
        // The head bobs a little with the stroke.
        if (flipper == 0 && a < 0.15)
        {
            p.y += 0.01 * sin(phase + 1.0);
        }
    }
    else if (mode == 4)
    {
        // The bell squeezes and relaxes: quick contraction, slow recovery. Its rim squeezes most; the
        // tentacles trail behind the stroke in a wave that runs down them.
        float cycle = frac(anim.x);
        float squeeze = cycle < 0.3 ? sin(cycle / 0.3 * 1.5707963) : cos((cycle - 0.3) / 0.7 * 1.5707963);
        squeeze *= squeeze;
        if (part.x < 0.5)
        {
            float rim = saturate(a);
            p.xz *= 1.0 - _Amplitude * strength * squeeze * rim;
            p.y += _Amplitude * strength * 0.4 * squeeze * rim * 0.5;
        }
        else
        {
            float down = part.y;
            float wave = sin(phase - down * 5.0);
            float2 sideways = float2(cos(body.y * 6.2831853), sin(body.y * 6.2831853));
            p.xz *= 1.0 - _Amplitude * strength * squeeze * (1.0 - down) * 0.6;
            p.xz += sideways * (0.06 * down * wave) + float2(0.05, 0.03) * down * down * sin(phase * 0.5 + body.y * 3.0);
            p.y += 0.08 * down * squeeze;
        }
    }
    else
    {
        // A crab's legs step in alternating sets; its claws lift and open a little.
        int leg = (int)(part.x + 0.5);
        float reach = part.y;
        if (leg >= 1 && leg <= 8)
        {
            float set = (leg % 2 == 0) ? 0.0 : 3.14159;
            float step = sin(phase + set + leg * 0.6);
            p.y += _Amplitude * strength * reach * max(0.0, step);
            p.z += _Amplitude * strength * 0.6 * reach * cos(phase + set + leg * 0.6);
        }
        else if (leg == 9)
        {
            p.y += 0.04 * reach * (0.5 + 0.5 * sin(phase * 0.5));
        }
    }
}

struct SeaLifeVertex
{
    float3 positionWS;
    float3 normalWS;
    float4 color;
    float visible;
};

SeaLifeVertex SeaLifeBuild(SeaLifeAttributes input)
{
    float4 anim = UNITY_ACCESS_INSTANCED_PROP(SeaLifeProps, _Anim);
    float4 tint = UNITY_ACCESS_INSTANCED_PROP(SeaLifeProps, _Tint);
    float3 p = input.positionOS.xyz;
    float3 n = input.normalOS;
    SeaLifeSwim(p, n, input.body, input.part, anim);
    SeaLifeVertex o;
    o.positionWS = TransformObjectToWorld(p);
    o.normalWS = TransformObjectToWorldNormal(n);
    o.color = float4(input.color.rgb * tint.rgb, input.color.a);
    o.visible = tint.a;
    return o;
}

#if defined(FISH_SEA_LIFE_PASS_FORWARD) || defined(FISH_SEA_LIFE_PASS_DEPTHNORMALS)

struct SeaLifeVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 color : TEXCOORD2;
    float2 extra : TEXCOORD3;   // x visible share, y fog
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

SeaLifeVaryings SeaLifeVertexMain(SeaLifeAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    SeaLifeVaryings o = (SeaLifeVaryings)0;
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    SeaLifeVertex v = SeaLifeBuild(input);
    o.positionWS = v.positionWS;
    o.normalWS = v.normalWS;
    o.color = v.color;
    o.positionCS = TransformWorldToHClip(v.positionWS);
    o.extra = float2(v.visible, ComputeFogFactor(o.positionCS.z));
    return o;
}

#endif

#if defined(FISH_SEA_LIFE_PASS_FORWARD)

half4 SeaLifeForwardFragment(SeaLifeVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    bool translucent = _Translucent > 0.5;
    if (!translucent)
    {
        FishDitherClip(input.positionCS.xy, input.extra.x);
    }
    float facing = IS_FRONT_VFACE(face, 1.0, -1.0);
    float3 n = normalize(input.normalWS) * facing;
    float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
    float3 albedo = input.color.rgb;

#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    Light mainLight = GetMainLight(shadowCoord);
    float3 light = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation);
    float nl = dot(n, mainLight.direction);
    // Wrapped a little: a fish's flank is round, and light in water comes from everywhere.
    float diffuse = saturate((nl + 0.3) / 1.3);
    float3 colour = albedo * light * diffuse;
    colour += albedo * FishTrilight((half3)n);
    // Wet skin: a narrow, bright highlight.
    float3 h = normalize(mainLight.direction + v);
    float spec = pow(saturate(dot(n, h)), exp2(10.0 * _Smoothness + 1.0)) * _Smoothness;
    colour += light * spec * 0.6;
    half alpha = 1.0;
    if (translucent)
    {
        // A jelly: lit through its body, its edges (seen through more of it) brighter, mostly clear.
        float rim = 1.0 - saturate(abs(dot(n, v)));
        colour += albedo * light * 0.6 * (0.4 + 0.6 * saturate(-nl + 0.5));
        colour += albedo * (0.25 + 0.75 * rim * rim) * 0.6;
        alpha = saturate(input.color.a * (0.35 + 0.65 * rim)) * input.extra.x;
        colour *= alpha; // premultiplied
    }
    colour = MixFog(colour, input.extra.y);
    return half4(colour, alpha);
}

#endif

#if defined(FISH_SEA_LIFE_PASS_DEPTHNORMALS)

half4 SeaLifeDepthNormalsFragment(SeaLifeVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
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

#if defined(FISH_SEA_LIFE_PASS_SHADOW) || defined(FISH_SEA_LIFE_PASS_DEPTH)

struct SeaLifeDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float visible : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

#if defined(FISH_SEA_LIFE_PASS_SHADOW)
float3 _LightDirection;
float3 _LightPosition;
#endif

SeaLifeDepthVaryings SeaLifeDepthVertex(SeaLifeAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    SeaLifeDepthVaryings o = (SeaLifeDepthVaryings)0;
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    SeaLifeVertex v = SeaLifeBuild(input);
#if defined(FISH_SEA_LIFE_PASS_SHADOW)
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 lightDirectionWS = normalize(_LightPosition - v.positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    // Fins are thin and double-sided: bias along the normal turned to the light.
    float3 n = dot(v.normalWS, lightDirectionWS) < 0.0 ? -v.normalWS : v.normalWS;
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(v.positionWS, n, lightDirectionWS)));
#else
    o.positionCS = TransformWorldToHClip(v.positionWS);
#endif
    o.visible = v.visible;
    return o;
}

half4 SeaLifeDepthFragment(SeaLifeDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    FishDitherClip(input.positionCS.xy, input.visible);
    return half4(input.positionCS.z, 0.0, 0.0, 0.0);
}

#endif

#endif
