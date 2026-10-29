#ifndef FISHMMO_AMBIENT_LIFE_INCLUDED
#define FISHMMO_AMBIENT_LIFE_INCLUDED

// FishMMO/Ambient Life: the land's background creatures (AmbientLifeSystem, FishMMO.Client/World/AmbientLife) —
// birds, bats, rodents, rabbits, lizards, crabs, frogs, and the caves' creatures, some with a light of their own. Drawn only by
// AmbientLifeSystem's Graphics.RenderMeshPrimitives, one draw and one material per kind, every animal read from the instance buffer
// below; where each animal is and which way it faces is worked out on the CPU from the shared world clock. What
// the shader adds is the movement of the body itself — wing beats and folding, strides and bounds, a lizard's
// wriggle, a bird's head turning — from the phase the CPU hands each instance.
//
// The meshes (AmbientCreatureMeshes) are one unit long, nose toward +z, back up +y, standing on the origin; the
// instance matrix scales and places them. Vertex data:
//   COLOR       rgb the animal's own colour (countershaded), a how much of it gives off its own light (the emission mask;
//               0 all over a creature without one)
//   TEXCOORD0   x how far along the body (0 nose .. 1 tail; a jelly's from its crown), y across a wing's chord (0 leading ..
//               1 trailing edge), or on a glowing lamp that lamp's own twinkle offset (0..1)
//   TEXCOORD1   x which part (birds 0 body, 1 left wing, 2 right wing, 3 legs, 4 head, 5 tail; walkers 0 body,
//               1-4 legs FL FR HL HR, 5 tail, 6 head; crabs 1-8 legs, 9 claws; jellies 0 bell, 1-8 tentacles), y how far out
//               along it (0 root .. 1 tip; a jelly's bell 0 crown .. 1 rim)
//   TEXCOORD2   the colour's roles: how much of it came from the kind's back (x), belly (y) and accent (z) (AmbientCreatureMeshes)
//
// Per instance (_LifeInstances[_LifeInstanceBase + SV_InstanceID], LifeInstanceRecord on the C# side): the matrix's rows 0..2,
// then
//   _Anim       x the stroke's phase in cycles (wing beat, stride), y its strength (birds: 0 gliding .. 1 beating;
//               walkers: 0 still .. 1 striding), z birds: 0 wings open, legs tucked .. 1 wings folded, standing;
//               walkers: 1 for a bounding gait (rabbits, squirrels, frogs), w birds: the head's turn (radians);
//               walkers: a pose (a rabbit sitting up, a lizard's push-up, sniffing)
//   _Tint       rgb times the mesh colour, a how much of the animal is drawn (the distance and fright dither)
//   _Glow       its own light (AmbientGlow, worked out on the CPU in double precision on the world clock): x its pattern's
//               phase (cycles, 0..1), y its brightness now (0..1); the material's _EmitColour and _EmitPattern say what colour
//               and how it runs over the body
//   _Look       the biome look its group wears (AmbientLooks): x coat, y belly, z markings as packed 8-bit sRGB
//               (r + 256 g + 65536 b), w 1 + the markings' pattern (plain, stripes, spots, bands, mottled) + its phase
//               (the fraction / 0.95); w 0 for none: the kind's own colours, exactly as the mesh has them
//
// The look: each colour role's own colour (_OwnBack, _OwnBelly, _OwnAccent) is swapped for the look's by the kind's
// _LookMix (x coat, y accents toward the markings, z the markings' strength, w how much of its own brightness the
// coat keeps), so a rabbit wears its ground's colours, a crow only a hint of its tone, and the countershading, the
// eyes and the shading the mesh was built with all stay. The markings are drawn over the coat in the fragment from
// the body's rest pose (object space, one unit long), so they ride the body as it moves, and fade to their average
// where they would be finer than a pixel.
//
// Swarms (mode 4) are the exception: one instance is a whole cloud of insects, its matrix at the cloud's middle and
// scaled to its radius, every insect a quad flown here about it and turned to the camera (AmbientSwarm):
//   TEXCOORD0   the quad's corner (-1..1)
//   TEXCOORD1   x the insect's index, y where it falls in the swarm (0..1)
//   _Anim       x the world clock wound back every 512 s (AmbientLifeMotion.SwarmClockWrap), y the share of the insects
//               drawn, w the swarm's own seed

#include "FishSurface.hlsl"
#include "FishAmbient.hlsl"

CBUFFER_START(UnityPerMaterial)
    half _Mode;          // 0 bird or bat, 1 four-legged walker, 2 lizard (fish, worm), 3 crab (beetle, millipede, snail), 4 swarm, 5 jelly
    half _Amplitude;     // birds: the beat's half-angle (radians); walkers: the stride, as a share of the body's length
    half _Dihedral;      // birds: the wings' angle above level while gliding (radians)
    half _Smoothness;
    float4 _Shoulder;    // birds: the wings' shoulder (x out, y up, z along), w the neck's z (the head turns about it)
    half _SwarmStyle;    // swarms: 0 column, 1 cloud, 2 buzz, 3 drift (fireflies), 4 hover (bees), 5 plague, 6 spores (AmbientSwarmStyle)
    float _BugSize;      // swarms: one insect's length, metres
    float4 _GlowColour;  // swarms: rgb its own light at a flash (linear, HDR), a 1 when it has one
    float4 _LookMix;     // how far it takes a look: x coat, y accents, z markings, w its own brightness kept (AmbientLooks.MixOf)
    float4 _OwnBack;     // the kind's own back, belly and accent (linear): what the colour roles swap out
    float4 _OwnBelly;
    float4 _OwnAccent;
    float4 _EmitColour;  // its own light at its brightest (rgb linear, HDR), a 1 when it has one (AmbientGlow.EmitColour)
    float4 _EmitPattern; // x 0 even, 1 a wave along the body's 'along', 2 each lamp twinkling; y crests at once; z a crest's
                         // sharpness; w the share lit between crests (AmbientGlow.Pattern)
    float _LifeInstanceBase; // where this kind's run starts in _LifeInstances (set per camera by AmbientLifeSystem)
CBUFFER_END

// Every drawn animal of every kind, each kind a run (AmbientLifeSystem, LifeInstanceRecord: 112 bytes). The draw is a plain
// procedural one (RenderMeshPrimitives, no indirect args), so SV_InstanceID counts from 0 within it on every API.
struct AmbientLifeInstance
{
    float4 row0;   // objectToWorld row 0: xyz the 3x3 row, w translation x
    float4 row1;
    float4 row2;
    float4 anim;   // _Anim
    float4 tint;   // _Tint
    float4 look;   // _Look
    float4 glow;   // _Glow
};
StructuredBuffer<AmbientLifeInstance> _LifeInstances;

AmbientLifeInstance AmbientLifeLoad(uint instanceID)
{
    return _LifeInstances[(uint)_LifeInstanceBase + instanceID];
}

// The instance's object to world, for a point: the matrix's three top rows (its fourth is 0 0 0 1).
float3 AmbientObjectToWorld(AmbientLifeInstance instance, float3 p)
{
    return float3(dot(instance.row0.xyz, p) + instance.row0.w, dot(instance.row1.xyz, p) + instance.row1.w,
        dot(instance.row2.xyz, p) + instance.row2.w);
}

// ... and for a normal: as TransformObjectToWorldNormal, the inverse transpose. The matrices are T.R.S with positive scale and no
// shear (AmbientLifeMotion), so the inverse's row i is column i over its squared length (FishIndirectInverseAffine).
float3 AmbientObjectToWorldNormal(AmbientLifeInstance instance, float3 n)
{
    float3 c0 = float3(instance.row0.x, instance.row1.x, instance.row2.x);
    float3 c1 = float3(instance.row0.y, instance.row1.y, instance.row2.y);
    float3 c2 = float3(instance.row0.z, instance.row1.z, instance.row2.z);
    return SafeNormalize(n.x * c0 / dot(c0, c0) + n.y * c1 / dot(c1, c1) + n.z * c2 / dot(c2, c2));
}

// One instance is a whole swarm (mode 4).
bool AmbientIsSwarm()
{
    return abs(_Mode - 4.0) < 0.5;
}

struct AmbientLifeAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 color : COLOR;
    float2 body : TEXCOORD0;
    float2 part : TEXCOORD1;
    float3 role : TEXCOORD2;
    uint instanceID : SV_InstanceID;
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

// A jelly's bell beat: a quick squeeze (a fifth of the beat), then a slow relaxing (AmbientGlow.Squeeze, the same curve).
float AmbientSqueeze(float phase)
{
    float f = frac(phase);
    return smoothstep(0.0, 0.2, f) * (1.0 - smoothstep(0.2, 0.9, f));
}

// A jelly: the bell's rim drawn in and its crown lifted at each squeeze, the tentacles drawn in with it and trailing in slow waves.
void AmbientPulse(inout float3 p, inout float3 n, float2 body, float2 part, float4 anim)
{
    int id = (int)(part.x + 0.5);
    float squeeze = AmbientSqueeze(anim.x);
    if (id == 0)
    {
        float rim = saturate(part.y);
        p.xz *= 1.0 - 0.24 * squeeze * rim * rim;
        p.y += 0.05 * squeeze * (1.0 - rim);
        n.xz *= 1.0 + 0.3 * squeeze * rim;
    }
    else
    {
        float s = saturate(part.y);
        float phase = anim.x * 6.2831853 - s * 2.6 + id * 0.8;
        p.xz *= 1.0 - 0.22 * squeeze * (1.0 - 0.5 * s);
        p.x += 0.06 * s * sin(phase);
        p.z += 0.06 * s * cos(phase * 0.8);
        p.y += 0.1 * s * squeeze;
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
    else if (mode == 5)
    {
        AmbientPulse(p, n, body, part, anim);
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
    float glow;          // how much of its light it gives off: _GlowColour's (swarms), _EmitColour's (the rest)
    float3 role;         // the colour's roles (TEXCOORD2)
    float3 rest;         // where on the body it is, at rest (object space): the markings' coordinates
};

// ── Swarms ────────────────────────────────────────────────────────

#define AMBIENT_SWARM_WRAP 512.0

// 0..1 from an insect and a salt: an integer hash (a sin hash stripes).
float AmbientSwarmHash(uint insect, uint salt)
{
    uint h = insect * 747796405u + salt * 2891336453u + 0x9E3779B9u;
    h ^= h >> 16;
    h *= 0x7FEB352Du;
    h ^= h >> 15;
    h *= 0x846CA68Bu;
    h ^= h >> 16;
    return (h >> 8) * (1.0 / 16777216.0);
}

// A sine of the wound-back clock at about <rate> radians a second, snapped to whole turns in the wrap so it runs on
// without a jump when the clock winds back.
float AmbientSwarmWave(float t, float rate, float phase)
{
    float turns = max(1.0, floor(rate * AMBIENT_SWARM_WRAP / 6.2831853 + 0.5));
    return sin(t * turns * (6.2831853 / AMBIENT_SWARM_WRAP) + phase);
}

// How far through a cycle of about <seconds> it is, 0..1, snapped the same way.
float AmbientSwarmCycle(float t, float seconds, float phase)
{
    float cycles = max(1.0, floor(AMBIENT_SWARM_WRAP / max(0.05, seconds) + 0.5));
    return frac(t * cycles / AMBIENT_SWARM_WRAP + phase);
}

// Where one insect is about its swarm's middle, in the swarm's own space (its radius one), and how brightly it glows.
float3 AmbientSwarmPlace(uint i, uint swarm, float t, int style, out float glow, out float size)
{
    float r0 = AmbientSwarmHash(i, swarm + 1u), r1 = AmbientSwarmHash(i, swarm + 2u), r2 = AmbientSwarmHash(i, swarm + 3u);
    float r3 = AmbientSwarmHash(i, swarm + 4u), r4 = AmbientSwarmHash(i, swarm + 5u), r5 = AmbientSwarmHash(i, swarm + 6u);
    float swarmPhase = AmbientSwarmHash(0u, swarm + 7u) * 6.2831853;
    glow = 0.0;
    size = 1.0;
    float3 p;
    if (style == 0)
    {
        // A midge column: each dancing up and down a hand's breadth over its place in the column, round it slowly; the
        // column swaying as one, its top the most.
        float a = r0 * 6.2831853 + AmbientSwarmWave(t, 0.3 + 0.4 * r3, r4 * 6.2831853) * 0.6;
        float rad = sqrt(r1) * 0.35;
        p = float3(cos(a) * rad, -0.4 + r2 * 1.6, sin(a) * rad);
        p.y += 0.12 * AmbientSwarmWave(t, 6.2831853 * (2.0 + 3.0 * r3), r5 * 6.2831853);
        p.x += 0.15 * AmbientSwarmWave(t, 0.6, swarmPhase) * (p.y + 0.4);
    }
    else if (style == 1)
    {
        // A mosquito cloud: a loose ellipsoid, each jinking on its own and drifting through it.
        float3 home = float3(r0 * 2.0 - 1.0, (r1 * 2.0 - 1.0) * 0.6, r2 * 2.0 - 1.0) * 0.8;
        float3 jink = float3(AmbientSwarmWave(t, 1.5 + 2.5 * r3, r4 * 6.2831853), AmbientSwarmWave(t, 2.0 + 2.0 * r4, r5 * 6.2831853),
            AmbientSwarmWave(t, 1.7 + 2.3 * r5, r3 * 6.2831853)) * 0.22;
        float3 drift = float3(AmbientSwarmWave(t, 0.25, r3 * 6.2831853), 0.5 * AmbientSwarmWave(t, 0.3, r4 * 6.2831853), AmbientSwarmWave(t, 0.22, r5 * 6.2831853)) * 0.3;
        p = home + jink + drift;
        // A swarm with a light of its own (another world's gnats) glimmers, each on its own slow pulse.
        glow = 0.55 + 0.45 * AmbientSwarmWave(t, 0.8 + 1.2 * r3, r4 * 6.2831853);
    }
    else if (style == 2)
    {
        // Flies: tight fast loops round a spot near the middle, each on its own tilted plane, now and then a dart out.
        float3 centre = float3(r0 - 0.5, (r1 - 0.5) * 0.5, r2 - 0.5) * 0.4;
        float loop = 0.35 + 0.55 * r3;
        float spin = AmbientSwarmCycle(t, 0.5 + 0.7 * r4, r5) * 6.2831853;
        float3 ring = float3(cos(spin), 0.35 * sin(spin * 2.0), sin(spin)) * loop;
        float tilt = (r4 - 0.5) * 1.6;
        ring.xy = float2(ring.x * cos(tilt) - ring.y * sin(tilt), ring.x * sin(tilt) + ring.y * cos(tilt));
        float dartAt = AmbientSwarmCycle(t, 5.0 + 6.0 * r0, r1);
        float dart = smoothstep(0.0, 0.03, dartAt) * (1.0 - smoothstep(0.06, 0.1, dartAt));
        p = centre + ring * (1.0 + 1.2 * dart);
    }
    else if (style == 3)
    {
        // Fireflies: spread wide and low, drifting slowly; each flashes on its own rhythm, climbing a little as it does.
        float a = r0 * 6.2831853;
        float rad = sqrt(r1);
        p = float3(cos(a) * rad, (r2 - 0.5) * 0.35, sin(a) * rad);
        p += float3(AmbientSwarmWave(t, 0.2 + 0.3 * r3, r4 * 6.2831853), 0.5 * AmbientSwarmWave(t, 0.25 + 0.2 * r4, r5 * 6.2831853),
            AmbientSwarmWave(t, 0.18 + 0.3 * r5, r3 * 6.2831853)) * 0.15;
        float phase = AmbientSwarmCycle(t, 2.5 + 4.0 * r3, r4);
        glow = smoothstep(0.0, 0.03, phase) * (1.0 - smoothstep(0.07, 0.16, phase));
        p.y += 0.06 * saturate(phase / 0.16) * glow;
        size = 1.0 + 1.5 * glow;
    }
    else if (style == 4)
    {
        // Bees over flowers: hanging at a bloom, then darting to the next.
        float u = t / AMBIENT_SWARM_WRAP * max(1.0, floor(AMBIENT_SWARM_WRAP / (2.0 + 3.0 * r0) + 0.5)) + r1;
        uint k = (uint)floor(u);
        float f = frac(u);
        uint before = k == 0u ? 0xFFFFu : k - 1u;
        float a0 = AmbientSwarmHash(i * 131u + before, swarm + 11u) * 6.2831853, d0 = sqrt(AmbientSwarmHash(i * 131u + before, swarm + 12u));
        float a1 = AmbientSwarmHash(i * 131u + k, swarm + 11u) * 6.2831853, d1 = sqrt(AmbientSwarmHash(i * 131u + k, swarm + 12u));
        float3 from = float3(cos(a0) * d0, AmbientSwarmHash(i * 131u + before, swarm + 13u) * 0.3, sin(a0) * d0);
        float3 to = float3(cos(a1) * d1, AmbientSwarmHash(i * 131u + k, swarm + 13u) * 0.3, sin(a1) * d1);
        p = lerp(from, to, smoothstep(0.0, 0.15, f));
        p += float3(AmbientSwarmWave(t, 9.0 + 5.0 * r2, r3 * 6.2831853), AmbientSwarmWave(t, 7.0 + 4.0 * r3, r4 * 6.2831853), AmbientSwarmWave(t, 8.0 + 6.0 * r4, r5 * 6.2831853)) * 0.015;
    }
    else if (style == 6)
    {
        // Spore motes: a wide slow haze, deeper than a firefly's, each mote sinking and lifting on the faintest draught about its
        // place in it and kindling and fading on a slow breath of its own (three to eight seconds), never quite out.
        float a = r0 * 6.2831853;
        float rad = pow(r1, 0.4);
        p = float3(cos(a) * rad, (r2 - 0.5) * 0.9, sin(a) * rad);
        p += float3(0.25 * AmbientSwarmWave(t, 0.05 + 0.08 * r3, r4 * 6.2831853), 0.18 * AmbientSwarmWave(t, 0.04 + 0.06 * r4, r5 * 6.2831853),
            0.25 * AmbientSwarmWave(t, 0.06 + 0.07 * r5, r3 * 6.2831853));
        float breath = 0.5 + 0.5 * AmbientSwarmWave(t, 6.2831853 / (3.0 + 5.0 * r3), r4 * 6.2831853);
        glow = 0.2 + 0.8 * breath * breath;
        size = 0.8 + 0.5 * glow;
    }
    else
    {
        // Locusts: a wide low cloud streaming one way (+z, the swarm's heading) and round again, churning as it goes; each
        // shrinks to nothing at the cloud's front and back, so none pops in or out.
        float along = frac(r0 + t * max(1.0, floor(AMBIENT_SWARM_WRAP * (0.05 + 0.04 * r1) + 0.5)) / AMBIENT_SWARM_WRAP) * 2.0 - 1.0;
        float across = (r2 * 2.0 - 1.0) * sqrt(saturate(1.0 - along * along));
        p = float3(across, (r3 - 0.5) * 0.35, along);
        p += float3(0.08 * AmbientSwarmWave(t, 3.0 + 3.0 * r4, r5 * 6.2831853), 0.1 * AmbientSwarmWave(t, 4.0 + 3.0 * r5, r3 * 6.2831853), 0.0);
        size = 1.0 - smoothstep(0.8, 1.0, abs(along));
    }
    return p;
}

// One insect of a swarm: flown to its place, turned to the camera, never less than about a pixel and a half across. A
// speck smaller than that is drawn at that size and thinned a little with distance, never below two in three of the
// swarm: thinned in proportion to its true size, a swarm was gone a few metres off. A firefly's light is never thinned.
AmbientLifeVertex AmbientSwarm(AmbientLifeAttributes input, AmbientLifeInstance instance, float4 anim, float4 tint)
{
    uint i = (uint)(input.part.x + 0.5);
    uint swarm = (uint)(anim.w * 65535.0);
    int style = (int)(_SwarmStyle + 0.5);
    float glow, size;
    float3 offset = AmbientSwarmPlace(i, swarm, anim.x, style, glow, size);
    // Only the first share of the swarm's insects: a smaller swarm, or one coming out or going in.
    bool drawn = input.part.y <= anim.y;
    float3 centreWS = AmbientObjectToWorld(instance, offset);
    float3 toCamera = _WorldSpaceCameraPos - centreWS;
    float distance = max(1e-3, length(toCamera));
    float pixel = distance * 2.0 / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
    float own = 0.5 * _BugSize * size;
    float extent = max(own, 0.75 * pixel);
    float coverage = max(0.65, saturate(4.0 * (own * own) / (extent * extent)));
    float lit = _GlowColour.a > 0.5 ? glow : 0.0;
    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
    AmbientLifeVertex o;
    o.positionWS = centreWS + (right * input.body.x + up * input.body.y) * (drawn ? extent : 0.0);
    o.normalWS = toCamera / distance;
    o.color = input.color.rgb * tint.rgb;
    o.visible = drawn ? tint.a * max(coverage, lit) : 0.0;
    o.glow = lit;
    o.role = input.role;
    o.rest = float3(0.0, 0.0, 0.0);
    return o;
}

// ── Its own light ─────────────────────────────────────────────────

// How much of its light a point of the body gives at the pattern's phase (_Glow.x), before the mask and the brightness now: even
// all over; a wave of crests running along the body's 'along' (a millipede's nose to tail, a lateral line's, a jelly's out from its
// crown); or each lamp twinkling on its own offset (a newt's spots: a quick kindling, a slow fading). Per vertex: the patterns are
// a few crests across a creature a few centimetres long.
float AmbientEmitShape(float along, float offset, float phase)
{
    int pattern = (int)(_EmitPattern.x + 0.5);
    float rest = saturate(_EmitPattern.w);
    if (pattern == 1)
    {
        float crest = frac(phase - along * _EmitPattern.y);
        float s = pow(saturate(0.5 + 0.5 * cos(crest * 6.2831853)), max(1.0, _EmitPattern.z));
        return lerp(rest, 1.0, s);
    }
    if (pattern == 2)
    {
        float c = frac(phase + offset);
        float s = smoothstep(0.0, 0.1, c) * (1.0 - smoothstep(0.15, 0.55, c));
        return lerp(rest, 1.0, s);
    }
    return 1.0;
}

AmbientLifeVertex AmbientLifeBuild(AmbientLifeAttributes input)
{
    AmbientLifeInstance instance = AmbientLifeLoad(input.instanceID);
    float4 anim = instance.anim;
    float4 tint = instance.tint;
    if (AmbientIsSwarm())
    {
        return AmbientSwarm(input, instance, anim, tint);
    }
    float3 p = input.positionOS.xyz;
    float3 n = input.normalOS;
    AmbientMove(p, n, input.body, input.part, anim);
    AmbientLifeVertex o;
    o.positionWS = AmbientObjectToWorld(instance, p);
    o.normalWS = AmbientObjectToWorldNormal(instance, n);
    o.color = input.color.rgb * tint.rgb;
    o.visible = tint.a;
    o.glow = 0.0;
    if (_EmitColour.a > 0.5)
    {
        float4 glow = instance.glow;
        o.glow = input.color.a * AmbientEmitShape(input.body.x, input.body.y, glow.x) * glow.y;
    }
    o.role = input.role;
    o.rest = input.positionOS.xyz;
    return o;
}

// ── Biome looks ───────────────────────────────────────────────────

// An sRGB colour packed as r + 256 g + 65536 b (AmbientLooks.PackSrgb), in linear light (a close polynomial fit). The
// number is whole and exact; no rounding (+0.5 above 2^23, where floats step by one, rounds an odd number up: white
// would wrap to black).
float3 AmbientUnpackLook(float packed)
{
    uint u = (uint)packed;
    float3 c = float3(u & 255u, (u >> 8) & 255u, (u >> 16) & 255u) * (1.0 / 255.0);
    return c * (c * (c * 0.305306011 + 0.682171111) + 0.012522878);
}

float AmbientLookLuma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

// 0..1 from a cell of the body's rest space and a salt (the swarm's integer hash).
float AmbientMarkHash(float3 cell, uint salt)
{
    int3 i = (int3)cell + 512;
    return AmbientSwarmHash((uint)i.x * 73856093u ^ (uint)i.y * 19349663u ^ (uint)i.z * 83492791u, salt);
}

// Smooth value noise over the rest space, 0..1.
float AmbientMarkNoise(float3 p, uint salt)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(AmbientMarkHash(i, salt), AmbientMarkHash(i + float3(1, 0, 0), salt), f.x);
    float b = lerp(AmbientMarkHash(i + float3(0, 1, 0), salt), AmbientMarkHash(i + float3(1, 1, 0), salt), f.x);
    float c = lerp(AmbientMarkHash(i + float3(0, 0, 1), salt), AmbientMarkHash(i + float3(1, 0, 1), salt), f.x);
    float d = lerp(AmbientMarkHash(i + float3(0, 1, 1), salt), AmbientMarkHash(i + float3(1, 1, 1), salt), f.x);
    return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
}

// How much of a point of the body is marking (0..1), for a pattern (1 stripes, 2 spots, 3 bands, 4 mottled) and the
// group's phase. Each faded toward its average where its features would be finer than about two pixels: 'pixel' is
// how much of the rest space one pixel spans (taken before any branch, so its derivatives are sound).
float AmbientMarking(float3 rest, float pixel, int pattern, float phase)
{
    float m = 0.0, mean = 0.0, frequency = 1.0;
    if (pattern == 1)
    {
        // Stripes: narrow and many, across the back, a little swept back toward the flanks (a tabby, a striped lizard).
        frequency = 7.0;
        float t = rest.z * frequency + abs(rest.x) * 2.0 + phase;
        m = smoothstep(0.45, 0.62, abs(frac(t) * 2.0 - 1.0));
        mean = 0.3;
    }
    else if (pattern == 2)
    {
        // Spots: one in most cells of a jittered grid (a fawn, a spotted toad, a leopard gecko).
        frequency = 9.0;
        float3 p = rest * frequency + phase * 17.0;
        float3 cell = floor(p);
        float3 centre = 0.25 + 0.5 * float3(AmbientMarkHash(cell, 31u), AmbientMarkHash(cell, 32u), AmbientMarkHash(cell, 33u));
        float present = step(0.3, AmbientMarkHash(cell, 34u));
        m = present * (1.0 - smoothstep(0.17, 0.25, length(frac(p) - centre)));
        mean = 0.12;
    }
    else if (pattern == 3)
    {
        // Bands: few and broad, round the whole body (a banded snake-lizard, a ringed tail, a wasp's livery).
        frequency = 3.0;
        float t = rest.z * frequency + phase;
        m = smoothstep(0.5, 0.62, abs(frac(t) * 2.0 - 1.0));
        mean = 0.4;
    }
    else if (pattern == 4)
    {
        // Mottled: soft blotches at two scales (a toad's back, a hen's speckle, lichen on a shell).
        frequency = 13.0;
        float n = AmbientMarkNoise(rest * 6.0 + phase * 31.0, 41u) * 0.65 + AmbientMarkNoise(rest * 13.0 + phase * 7.0, 42u) * 0.35;
        m = smoothstep(0.48, 0.66, n) * 0.8;
        mean = 0.25;
    }
    else
    {
        return 0.0;
    }
    return lerp(m, mean, smoothstep(0.25, 0.5, pixel * frequency));
}

// The albedo with the group's look: each colour role's own colour swapped for the look's by the kind's mix, the
// markings over the coat. 'own' is the mesh colour times the tint, as without a look; 'instanceID' the animal's place in its draw.
float3 AmbientLookAlbedo(float3 own, float3 role, float3 rest, uint instanceID)
{
    float3 spans = fwidth(rest);
    float pixel = max(spans.x, max(spans.y, spans.z));
    AmbientLifeInstance instance = AmbientLifeLoad(instanceID);
    float4 look = instance.look;
    if (look.w < 0.5)
    {
        return own;
    }
    float3 tint = instance.tint.rgb;
    float3 coat = AmbientUnpackLook(look.x);
    float3 belly = AmbientUnpackLook(look.y);
    float3 mark = AmbientUnpackLook(look.z);
    // Birds keep their own brightness: the look's tone at the kind's own lightness, so a crow stays dark, a gull pale.
    float keep = saturate(_LookMix.w);
    coat = lerp(coat, saturate(coat * clamp(AmbientLookLuma(_OwnBack.rgb) / max(1e-3, AmbientLookLuma(coat)), 0.05, 4.0)), keep);
    belly = lerp(belly, saturate(belly * clamp(AmbientLookLuma(_OwnBelly.rgb) / max(1e-3, AmbientLookLuma(belly)), 0.05, 4.0)), keep);
    // The markings, on the coat; a belly is mostly plain.
    if (_Mode < 3.5 && _LookMix.z > 0.0)
    {
        int pattern = (int)floor(look.w) - 1;
        float phase = saturate(frac(look.w) / 0.95);
        float m = AmbientMarking(rest, pixel, pattern, phase) * saturate(_LookMix.z);
        coat = lerp(coat, mark, m);
        belly = lerp(belly, mark, m * 0.25);
    }
    float3 shift = role.x * (coat - _OwnBack.rgb) * _LookMix.x + role.y * (belly - _OwnBelly.rgb) * _LookMix.x
        + role.z * (mark - _OwnAccent.rgb) * _LookMix.y;
    return max(0.0, own + shift * tint);
}

#if defined(FISH_AMBIENT_LIFE_PASS_FORWARD)
// The fine ground map, for how far underground a creature is (a cave's bats, crickets and olms): the sky's light, the sun's
// and the weather's fog reach none of it there, as none reaches the cave's walls (FishWeatherLitForwardPass.hlsl). Its own
// sampler: this pass is far below D3D11's sixteen.
#include "FishGroundMap.hlsl"
#endif

#if defined(FISH_AMBIENT_LIFE_PASS_FORWARD) || defined(FISH_AMBIENT_LIFE_PASS_DEPTHNORMALS)

struct AmbientLifeVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 color : TEXCOORD2;
    float4 extra : TEXCOORD3;   // x visible share, y fog, z how much of its own light it gives off, w how far underground (FishUnderground)
    float3 role : TEXCOORD4;    // the colour's roles, for the biome look
    float3 rest : TEXCOORD5;    // the body at rest (object space), for the look's markings
    nointerpolation uint instanceID : TEXCOORD6;   // the animal's place in its draw, for the look's per-instance numbers
};

AmbientLifeVaryings AmbientLifeVertexMain(AmbientLifeAttributes input)
{
    AmbientLifeVaryings o = (AmbientLifeVaryings)0;
    o.instanceID = input.instanceID;
    AmbientLifeVertex v = AmbientLifeBuild(input);
    o.positionWS = v.positionWS;
    o.normalWS = v.normalWS;
    o.color = v.color;
    o.role = v.role;
    o.rest = v.rest;
    o.positionCS = TransformWorldToHClip(v.positionWS);
    // Per vertex: a creature is a few centimetres to a metre, the ground map's cells five metres.
#if defined(FISH_AMBIENT_LIFE_PASS_FORWARD)
    float under = FishUnderground(v.positionWS);
#else
    float under = 0.0;
#endif
    o.extra = float4(v.visible, ComputeFogFactor(o.positionCS.z), v.glow, under);
    return o;
}

#endif

#if defined(FISH_AMBIENT_LIFE_PASS_FORWARD)

#if defined(_ADDITIONAL_LIGHTS)
// The lights other than the sun at a point (a cave's torches, lanterns and crystals, a carried torch): wrapped Lambert as the
// sun's, no shadows (a creature is small and the lights are near), through the cluster loop under Forward+ (the
// directional ones first, as URP's own loop does) or the per-object list otherwise.
float3 AmbientAdditionalLight(float3 positionWS, float3 n, float2 screenUV, out float3 specular, float3 v)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = screenUV;
    float3 sum = 0.0;
    specular = 0.0;
    float power = exp2(9.0 * _Smoothness + 1.0);
    uint pixelLightCount = GetAdditionalLightsCount();
#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        Light light = GetAdditionalLight(lightIndex, positionWS);
        float3 lit = light.color * light.distanceAttenuation;
        sum += lit * saturate((dot(n, light.direction) + 0.35) / 1.35);
        specular += lit * pow(saturate(dot(n, normalize(light.direction + v))), power);
    }
#endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS);
        float3 lit = light.color * light.distanceAttenuation;
        sum += lit * saturate((dot(n, light.direction) + 0.35) / 1.35);
        specular += lit * pow(saturate(dot(n, normalize(light.direction + v))), power);
    LIGHT_LOOP_END
    specular *= _Smoothness * 0.5;
    return sum;
}
#endif

half4 AmbientLifeForwardFragment(AmbientLifeVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    FishDitherClip(input.positionCS.xy, input.extra.x);
    float facing = IS_FRONT_VFACE(face, 1.0, -1.0);
    float3 n = normalize(input.normalWS) * facing;
    float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
    float3 albedo = AmbientLookAlbedo(input.color, input.role, input.rest, input.instanceID);

#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    // With the cloud shadow (the main light's cookie): under cloud the light is boosted for the cookie to dim (FishGrassBlades.hlsl).
    Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
    // Deep underground the sun does not reach (the hill over a cave casts no shadow of its own here), nor the sky: what lights
    // a cave's creatures is its torches and crystals, and the light carried in.
    float under = saturate(input.extra.w);
    float3 light = mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * (1.0 - under);
    float nl = dot(n, mainLight.direction);
    // Wrapped: fur and feathers scatter light round the body's edge.
    float diffuse = saturate((nl + 0.35) / 1.35);
    float3 colour = albedo * light * diffuse;
    // Ambient from the sky's trilight (not SampleSH, which reads zero for a script-issued draw here).
    colour += albedo * FishTrilight((half3)n) * (1.0 - 0.96 * under);
    // A thin wing or ear lit from behind glows a little through.
    colour += albedo * light * 0.25 * saturate(-nl) * saturate(1.0 - abs(dot(n, v)));
    float3 h = normalize(mainLight.direction + v);
    float spec = pow(saturate(dot(n, h)), exp2(9.0 * _Smoothness + 1.0)) * _Smoothness * 0.5;
    colour += light * spec;
#if defined(_ADDITIONAL_LIGHTS)
    float3 extraSpecular;
    float3 extraLight = AmbientAdditionalLight(input.positionWS, n, GetNormalizedScreenSpaceUV(input.positionCS), extraSpecular, v);
    colour += albedo * extraLight + extraSpecular;
#endif
    if (AmbientIsSwarm())
    {
        // A swarm against the low sun: every insect's wings lit through, a cloud of bright specks.
        colour += light * 0.8 * pow(saturate(dot(-v, mainLight.direction)), 6.0);
        // A firefly's flash (and any swarm's own light), unlit by anything else.
        colour += _GlowColour.rgb * input.extra.z;
    }
    else
    {
        // A creature's own light, unlit by anything else: full in a cave and by night, toned down under an open day's sky (where
        // the eye has adapted to the daylight and a cold light reads faint); the bloom catches what is past one.
        float night = saturate(1.0 - 4.0 * AmbientLookLuma(_FishAmbientSky.rgb));
        colour += _EmitColour.rgb * input.extra.z * lerp(0.5, 1.0, max(under, night));
    }
    // The weather's fog is the open air's: none in a cave.
    colour = lerp(MixFog(colour, input.extra.y), colour, under);
    return half4(colour, 1.0);
}

#endif

#if defined(FISH_AMBIENT_LIFE_PASS_DEPTHNORMALS)

half4 AmbientLifeDepthNormalsFragment(AmbientLifeVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
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

#if defined(FISH_AMBIENT_LIFE_PASS_SHADOW) || defined(FISH_AMBIENT_LIFE_PASS_DEPTH)

struct AmbientLifeDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float visible : TEXCOORD0;
};

#if defined(FISH_AMBIENT_LIFE_PASS_SHADOW)
float3 _LightDirection;
float3 _LightPosition;
#endif

AmbientLifeDepthVaryings AmbientLifeDepthVertex(AmbientLifeAttributes input)
{
    AmbientLifeDepthVaryings o = (AmbientLifeDepthVaryings)0;
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
    FishDitherClip(input.positionCS.xy, input.visible);
    return half4(input.positionCS.z, 0.0, 0.0, 0.0);
}

#endif

#endif
