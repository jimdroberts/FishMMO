#ifndef FISH_WATERFALL_INCLUDED
#define FISH_WATERFALL_INCLUDED

// The curtain's shared half (FishWaterfall.shader): what each vertex carries, how the curtain sways and how its ropes
// turn to face the eye, and how solid and how white the water is at a pixel. The lit pass shades it; the shadow pass
// dithers by it, so the shadow a curtain casts on its rock is the curtain's own shape.

CBUFFER_START(UnityPerMaterial)
    half4 _WaterColor;
    half4 _FoamColor;
    float _StreakScale;
    float _AerationMetres;
    half _Smoothness;
    half _Opacity;
    half _Breakup;
CBUFFER_END

TEXTURE2D(_FoamTexture);
SAMPLER(sampler_FoamTexture);
TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);

/// The inland water's clock, seconds (InlandWaterRenderer): the shared world-motion clock, wrapped here.
float _FishInlandTime;
#define FISH_INLAND_CLOCK_WRAP 10000.0
/// The streaks' and ripples' cycle: 1.1 s, snapped to a whole fraction of the clock's wrap so the wrap
/// lands on a cycle's start (1.1 s is not one: every 10 000 s the whole fall would jump).
#define FISH_FALL_CYCLE (FISH_INLAND_CLOCK_WRAP / round(FISH_INLAND_CLOCK_WRAP / 1.1))
float _FishFallDebug;           // debug: alpha, fallen and the brink as colour
float _FishInlandCameraInSea;   // 1 under the sea (InlandWaterRenderer.MarkCamera): no falls are seen from there

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;       // out of the sheet; a rope's own way down its path
    float4 color : COLOR;           // r the water's share over the lip ÷ 2, g how open the sheet's edge is, b 1 on a side face
    float2 uv0 : TEXCOORD0;         // x 0…1 across, y metres fallen from the lip
    float2 uv1 : TEXCOORD1;         // x width (m), y how free of the rock it hangs (0 riding it, 1 airborne)
    float2 uv2 : TEXCOORD2;         // x the water's speed leaving the brink (m/s), y the fall's whole drop (m)
    float2 uv3 : TEXCOORD3;         // x the sheet's thickness here (m; a rope's half width), y metres above the lip
    float4 tangentOS : TANGENT;     // across the curtain: the way it sways
    float2 uv4 : TEXCOORD4;         // x how churned it is from riding rock, y held still by rock
    float2 uv5 : TEXCOORD5;         // x metres along the water's path from the lip, y a rope's side (−1/+1; 0 on the sheet)
    float2 uv6 : TEXCOORD6;         // x fallen over this strip's break-up length, y a rope's seed
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 fall : TEXCOORD2;        // x across 0…1, y fallen m, z width m, w airborne
    float3 extra : TEXCOORD3;       // x brink speed, y drop, z side solidity
    float4 screenPos : TEXCOORD4;
    float fogFactor : TEXCOORD5;
    float3 flowShare : TEXCOORD6;   // x water over the lip over its mean, y how open the edge is, z 1 on a side face
    float4 sheetDepth : TEXCOORD7;  // x thickness (m), y metres above the lip, z churned by rock, w metres along the path
    float4 rope : TEXCOORD8;        // x across a rope −1…1 (0 on the sheet), y 1 on a rope, z fallen over break-up, w the rope's seed
    float3 sideWS : TEXCOORD9;      // a rope's own side, for its rounded normal
};

/// An angular speed snapped so a whole number of turns fits the clock's 10 000 s wrap: no jump at the wrap.
float FallOmega(float hertz) { return 6.2831853 * round(hertz * FISH_INLAND_CLOCK_WRAP) / FISH_INLAND_CLOCK_WRAP; }

/// <summary>
/// Where a vertex stands this frame: swayed across the curtain, and a rope's sides set either side of its path
/// across the line to <paramref name="toward"/> (the eye in the lit pass, the light in the shadow pass).
/// </summary>
Varyings FallVertexCommon(Attributes input, bool towardLight, float3 lightDirection)
{
    Varyings output = (Varyings)0;
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    /* The curtain sways: slow waves travelling down it, larger the further it has fallen free (the air pushes a long
     * sheet about), and its open edges flutter faster on top of that. Across the curtain only, so it never swings into
     * the rock it hangs in front of; never where rock holds it (uv4.y). */
    float fallenHere = max(0.0, input.uv0.y);
    float freeHere = saturate(input.uv1.y);
    float acrossMetres = input.uv0.x * input.uv1.x;
    float t = _FishInlandTime;
    float swayAmp = freeHere * saturate(fallenHere / 8.0) * min(0.9, 0.03 + 0.012 * fallenHere) * (1.0 - saturate(input.uv4.y));
    float sway = 0.65 * sin(FallOmega(0.11) * t - fallenHere * 0.16 + acrossMetres * 0.05)
               + 0.35 * sin(FallOmega(0.23) * t - fallenHere * 0.37 + acrossMetres * 0.21 + 1.7);
    float flutter = input.color.g * (1.0 - 0.7 * saturate(input.uv4.y)) * freeHere * saturate(fallenHere / 3.0) * min(0.35, 0.06 + 0.006 * fallenHere)
        * sin(FallOmega(0.9) * t - fallenHere * 0.95 + acrossMetres * 2.3);
    float3 acrossWS = TransformObjectToWorldDir(input.tangentOS.xyz);
    positionWS += acrossWS * (swayAmp * sway + flutter);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    bool rope = abs(input.uv5.y) > 0.5;
    if (!rope)
    {
        /* Rollers: the sheet's own surface heaves as disturbances carried down it grow toward its break-up length, moved
         * in the geometry, not only painted into the normals. The whole slab moves together (out of the curtain, the one
         * way for all its faces), riding down at about the water's pace, never where rock holds the water. */
        float grow = (0.02 + 0.1 * saturate(input.uv6.x)) * saturate(fallenHere / 1.5) * (1.0 - saturate(input.uv4.x));
        float3 outward = cross(float3(0.0, 1.0, 0.0), TransformObjectToWorldDir(input.tangentOS.xyz));
        outward = dot(outward, outward) > 1e-8 ? normalize(outward) : float3(0.0, 0.0, 1.0);
        float wave = 0.6 * sin(input.uv5.x * 2.4 - FallOmega(3.06) * t + acrossMetres * 0.9)
                   + 0.4 * sin(input.uv5.x * 4.1 - FallOmega(5.2) * t - acrossMetres * 1.7);
        positionWS += outward * (grow * wave);
    }
    if (rope)
    {
        /* A rope: its two sides either side of its path, across the line to the eye, so it shows its full width from
         * any side. Each wanders a little on its own as it falls (its seed sets the pace), more the longer it has
         * fallen broken. */
        float seed = input.uv6.y;
        float beyond = max(0.0, input.uv6.x - 1.0);
        float wander = (0.04 + 0.05 * saturate(beyond)) * freeHere * (1.0 - saturate(input.uv4.y))
            * sin(FallOmega(0.45 + 0.6 * seed) * t - input.uv5.x * (0.5 + 0.7 * seed) + seed * 6.2831853);
        positionWS += acrossWS * wander;
        float3 along = normalize(normalWS + float3(0.0, -1e-4, 0.0));
        float3 toward = towardLight ? lightDirection : normalize(_WorldSpaceCameraPos - positionWS);
        float3 side = cross(along, toward);
        side = dot(side, side) > 1e-8 ? normalize(side) : acrossWS;
        positionWS += side * (input.uv5.y * input.uv3.x);
        output.sideWS = side;
        normalWS = toward;
    }
    output.positionWS = positionWS;
    output.normalWS = normalWS;
    output.fall = float4(input.uv0.x, input.uv0.y, input.uv1.x, input.uv1.y);
    output.extra = float3(input.uv2.x, input.uv2.y, input.color.a);
    output.flowShare = float3(input.color.r * 2.0, input.color.g, input.color.b);
    output.sheetDepth = float4(input.uv3, input.uv4.x, input.uv5.x);
    output.rope = float4(rope ? input.uv5.y : 0.0, rope ? 1.0 : 0.0, input.uv6.x, input.uv6.y);
    return output;
}

/// The streak pattern carried down the sheet: two copies moving at the falling speed, half a cycle apart and
/// cross-faded, so the water accelerating down the fall never tears the pattern apart over time. x streaks, y finer ones.
half2 Streaks(float2 sheet, float speed, float stretch)
{
    const float cycle = FISH_FALL_CYCLE;
    float time = _FishInlandTime / cycle;
    float phaseA = frac(time);
    float phaseB = frac(time + 0.5);
    half blend = abs(2.0 * phaseA - 1.0);
    float2 scale = float2(_StreakScale, _StreakScale * stretch);
    float2 uvA = (sheet - float2(0.0, -speed * phaseA * cycle)) / scale;
    float2 uvB = (sheet - float2(0.0, -speed * phaseB * cycle)) / scale + float2(0.41, 0.23);
    half a = lerp(SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, uvA).r, SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, uvB).r, blend);
    half b = lerp(SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, uvA * float2(2.7, 1.9) + 0.17).r,
        SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, uvB * float2(2.7, 1.9) + 0.53).r, blend);
    return half2(a, b);
}

/// The lace between the jets (the fall's streak map's G), carried down as the streaks are.
half Lace(float2 sheet, float speed)
{
    const float cycle = FISH_FALL_CYCLE;
    float time = _FishInlandTime / cycle;
    float phaseA = frac(time), phaseB = frac(time + 0.5);
    half blend = abs(2.0 * phaseA - 1.0);
    float2 scale = float2(1.3, 2.6);
    half a = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, (sheet + float2(0.0, speed * phaseA * cycle)) / scale).g;
    half b = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, (sheet + float2(0.0, speed * phaseB * cycle)) / scale + float2(0.37, 0.61)).g;
    return lerp(a, b, blend);
}

/// Packets of water racing down a rope (the streak map's B): its own column of the map by its seed, carried at its speed.
half Packets(float seed, float along, float speed)
{
    const float cycle = FISH_FALL_CYCLE;
    float time = _FishInlandTime / cycle;
    float phaseA = frac(time), phaseB = frac(time + 0.5);
    half blend = abs(2.0 * phaseA - 1.0);
    float column = seed * 17.0;
    half a = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, float2(column, (along - speed * phaseA * cycle) / 3.2)).b;
    half b = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, float2(column + 0.5, (along - speed * phaseB * cycle) / 3.2 + 0.37)).b;
    return lerp(a, b, blend);
}

float FallHash(float x) { return frac(sin(x * 127.1) * 43758.5453); }

/// <summary>
/// The jets: water leaving a ledge breaks into columns a couple of hand-spans wide, each a train of packets racing
/// down it, dense at the head and drawn out behind as they speed up. x the packets' density, y how far into one's tail.
/// </summary>
half2 Jets(float acrossMetres, float along, float fallen, float speed)
{
    const float columnMetres = 0.45;
    float column = floor(acrossMetres / columnMetres);
    float inColumn = frac(acrossMetres / columnMetres);
    float h1 = FallHash(column * 1.7 + 3.1), h2 = FallHash(column * 2.3 + 7.9);
    float spacing = lerp(1.8, 4.5, h1) * (1.0 + 0.04 * fallen);
    float tail = 0.5 + 0.06 * fallen;
    const float cycle = FISH_FALL_CYCLE;
    float time = _FishInlandTime / cycle;
    float phaseA = frac(time), phaseB = frac(time + 0.5);
    half blend = abs(2.0 * phaseA - 1.0);
    float pa = frac((along - speed * phaseA * cycle) / spacing + h2);
    float pb = frac((along - speed * phaseB * cycle) / spacing + h2 + 0.5);
    float behindA = (1.0 - pa) * spacing, behindB = (1.0 - pb) * spacing;
    /* Rounded at its front and fading down its tail to nothing before the next: a front that stepped from full to none
     * drew hard horizontal edges across the curtain at every packet. */
    half headA = exp(-behindA / tail) * smoothstep(0.0, 0.7, behindA) * smoothstep(spacing, spacing - 0.8, behindA);
    half headB = exp(-behindB / tail) * smoothstep(0.0, 0.7, behindB) * smoothstep(spacing, spacing - 0.8, behindB);
    half body = smoothstep(0.0, 0.5, inColumn) * smoothstep(1.0, 0.5, inColumn);
    return half2(lerp(headA, headB, blend) * lerp(0.55, 1.0, body), lerp(behindA, behindB, blend) / spacing);
}

/// What the water is like at a pixel, before light: how streaked, how white, how solid.
struct FallSurface
{
    half strand;       // the streaks' and clumps' density, 0…1
    half white;        // how aerated, 0 glassy … 1 white
    half alpha;        // how much of what is behind it the water hides, before refraction is counted
    half broken;       // how far it has broken into jets
    half jet;          // a jet's packet here
    float2 sheet;      // where on the sheet (metres across, metres back up the path)
    float speed;       // how fast the water moves here, m/s
    float stretch;     // how drawn out the streaks are
};

/// <summary>
/// The water at a pixel: streaked and racing down, glassy at the brink and white below, a sheet above its break-up
/// length that thins to lace and gives way to ropes below it (InlandWaterRenderer.FallSheet), a rope round and white.
/// </summary>
FallSurface EvaluateFall(Varyings input, float3 view, float3 normalWS, float distanceToEye)
{
    /* Far off the ropes are finer than a pixel and only shimmer: from 120 m to 200 m they fade and the sheet, its streaks
     * and lace, carries the broken water instead. */
    half far = smoothstep(120.0, 200.0, distanceToEye);
    FallSurface s = (FallSurface)0;
    float across = input.fall.x;
    float fallen = max(0.0, input.fall.y);
    float width = input.fall.z;
    half airborne = saturate(input.fall.w);
    float lipSpeed = input.extra.x;
    float drop = max(0.1, input.extra.y);
    half share = saturate(input.flowShare.x);
    half openEdge = saturate(input.flowShare.y);
    half churned = saturate(input.sheetDepth.z);
    float breakup = max(0.0, input.rope.z);
    const float g = 9.81;

    // Fast where the river runs fast over the lip; slowed toward the speed broken clusters fall at once it has broken.
    float freeSpeed = sqrt(lipSpeed * lipSpeed + 2.0 * g * fallen) * lerp(0.75, 1.15, share);
    s.speed = lerp(freeSpeed, min(freeSpeed, 16.0), saturate(breakup - 1.0));
    s.stretch = 3.0 + 0.25 * fallen;
    float along = input.sheetDepth.w;
    s.sheet = float2(across * width, -along);

    if (input.rope.y > 0.5)
    {
        /* A rope of white water: round across (thickest in its middle), packets of water racing down it, frayed at its
         * edges and thinning between packets. It fades in over the band where the sheet fades out. */
        half x = input.rope.x;
        half core = saturate(1.0 - x * x);
        half packet = Packets(input.rope.w, along, s.speed);
        half fray = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, float2(input.rope.w * 9.0 + x * 0.15, (along - s.speed * frac(_FishInlandTime / FISH_FALL_CYCLE) * FISH_FALL_CYCLE) / 1.7)).r;
        half fadeIn = smoothstep(0.55, 1.0, breakup) * (1.0 - 0.6 * churned);
        s.strand = saturate(0.4 + 0.6 * packet);
        // White water, but water: denser in a packet, thinner and clearer between (the shader lets the rock through there).
        s.white = saturate(lerp(0.45, 0.85, packet) + 0.1 * smoothstep(1.0, 2.0, breakup));
        s.broken = 1.0;
        s.jet = packet;
        s.alpha = _Opacity * smoothstep(0.0, 0.45, core - 0.35 * (1.0 - fray)) * lerp(0.35, 1.0, packet) * fadeIn * (1.0 - far);
        return s;
    }

    half2 streak = Streaks(s.sheet, s.speed, s.stretch);
    half clump = Streaks(s.sheet * 0.38 + float2(3.1, 0.0), s.speed * 1.15, 1.3 + 0.12 * fallen).x;
    half strand = saturate(streak.x * 0.5 + streak.y * 0.22 + clump * 0.5);
    /* How far it has broken, by its own break-up length (a stream's discharge sets it, not the fall's height): none at the
     * lip, most of it by the break-up length. Water riding rock breaks sooner. */
    half broken = smoothstep(0.45, 1.1, breakup + 0.5 * churned) * lerp(0.5, 1.0, airborne);
    half2 jet = Jets(across * width + (clump - 0.5) * 0.5, along, fallen, s.speed);
    jet.x *= lerp(0.55, 1.15, streak.x);
    strand = lerp(strand, saturate(strand * 0.6 + jet.x * 0.6), broken);

    /* Air taken in as it falls: glassy where it leaves the lip, its outer water whitening well before its core breaks
     * (from a third of its break-up length), riding rock sooner. */
    half aeration = saturate(smoothstep(0.2, 1.0, breakup) + churned * 0.6 + saturate(fallen / max(0.2, _AerationMetres * 8.0)) * 0.15);
    aeration = 1.0 - (1.0 - aeration) * (1.0 - aeration);
    /* White by degrees, with the streaks in it: aerated water is a mass of bubbles in water, densest in the jets and
     * thinner between them, never one flat sheet of white. The narrower ramp it had went white wherever the water had
     * taken in air at all, and read as painted (Jim, 2026-10-08). */
    half white = smoothstep(0.45 - 0.4 * aeration, 1.05 - 0.25 * aeration, strand + 0.2 * aeration);
    white = max(white, churned * smoothstep(0.1, 0.75, strand + 0.25 * churned));
    white *= lerp(0.6, 0.95, strand);

    // ── How solid it is ───────────────────────────────────────────
    half side = input.extra.z;
    half fray = _Breakup * airborne * broken;
    half rim = 1.0 - saturate(abs(dot(normalWS, view)));
    // Only the curtain's own edges fray: its outer sides and either side of where it parts round rock.
    half edge = max(smoothstep(0.6, 0.97, rim), smoothstep(0.45, 1.0, openEdge) * 0.7) * openEdge;
    // Where little water goes over the lip the sheet thins, once it is falling.
    half thin = (1.0 - smoothstep(0.08, 0.45, share)) * saturate(fallen / 1.5);
    // Open between the jets where it has broken: lace, the rock seen through it.
    half laceNet = Lace(s.sheet, s.speed);
    half lace = broken * saturate(1.0 - jet.x * 1.8) * (0.12 + 0.2 * (1.0 - laceNet)) + churned * saturate(0.5 - strand) * 0.4;
    half threshold = saturate(fray * 0.75 + (1.0 - side) * 0.6 + edge * 0.55 + thin * 0.85 + lace);
    half field = strand + 0.25 * (1.0 - threshold) - threshold;
    half solid = smoothstep(-0.3, 0.3, field);
    half density = lerp(0.55, 0.94, white) * lerp(0.75, 1.0, saturate(fallen / 3.0));
    half alpha = _Opacity * solid * density * smoothstep(0.0, 0.05, side);
    // Above the lip it lies on the river's own water: fading in toward the brink, so the two are one surface.
    alpha *= smoothstep(1.5, 0.4, input.sheetDepth.y);
    // A side face shows only as far as that side has opened: shut against its neighbour it lies inside the sheet.
    alpha *= lerp(1.0, smoothstep(0.05, 0.6, openEdge), saturate(input.flowShare.z));
    /* Below its break-up length the sheet gives way to the ropes drawn there; water riding rock stays a sheet down the
     * face (a horsetail), white and whole. */
    /* Below its break-up length the ropes carry the detail, but the water is still a mass falling there: the sheet stays as
     * its body, thinner and aerated, under the ropes. Faded out entirely it left the fall's lower part as a few edge-on
     * bands with nothing between them (Jim, 2026-10-08). Water riding rock stays whole down the face (a horsetail). */
    half keep = max(max(lerp(1.0, 0.6, smoothstep(0.85, 1.6, breakup)), saturate(churned * 1.2) * (1.0 - airborne * 0.3)), far * 0.85);
    alpha *= keep;

    s.strand = strand;
    s.white = white;
    s.alpha = alpha;
    s.broken = broken;
    s.jet = jet.x;
    return s;
}

#endif
