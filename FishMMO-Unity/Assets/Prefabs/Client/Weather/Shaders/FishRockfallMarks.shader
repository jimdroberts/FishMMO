// FALLING STONE's marks and breath (2026-10-25, phase I2u; decisions 79, 95, 97): drawn by RockfallField with two materials of this
// shader (_Mode 0 and 1), both from the per-frame globals FishRockfall.hlsl declares.
//
// _Mode 0, the MARKS: boxes projected onto the ground from the depth texture. A scree chute's fresh SCAR (pale unlichened chips down
// its runout, at the freshness of its last fall: bright after each fall, fading over days) and a breathing talus's summer RIME (hoar
// on the blocks round its toe, where the cold air drains out). Only the ground inside a box and near its line takes the mark.
//
// _Mode 1, the PUFFS: soft camera-facing puffs. A fall's DUST (with air only: each stone kicks up a few as it bounds down, at moments
// hashed along its run on the shared clock) and the talus's BREATH (in summer a cold mist sinking out of its toe and down the slope,
// in winter faint steam rising off its head: TalusBreath on the clock's season), each puff on a cycle that divides the 10 000 s wrap.
Shader "FishMMO/World/Rockfall Marks"
{
    Properties
    {
        [HideInInspector] _Mode ("Mode (0 marks, 1 puffs)", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _ZTest ("ZTest", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+30"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "RockfallMarks"
            Tags { "LightMode" = "UniversalForward" }
            // Premultiplied: a mark or a puff covers by its alpha and adds its lit colour.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex MarkVertex
            #pragma fragment MarkFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "FishShadowVariants.hlsl"
            #include_with_pragmas "FishLightCookieVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishAmbient.hlsl"
            #include "FishRockfall.hlsl"
            #include "FishGroundMap.hlsl"
            #include "FishGroundColour.hlsl"

            #define ROCK_MAX_DECALS 160
            #define ROCK_DUST_PUFFS (ROCK_MAX_FALLS * ROCK_STONES_PER_FALL)
            #define ROCK_BREATH_PER_CONE 16
            // A dust puff's life (s), a mark's reach above and below its line (m).
            #define ROCK_DUST_SECONDS 6.0
            #define ROCK_MARK_REACH 3.0

            CBUFFER_START(UnityPerMaterial)
            float _Mode;
            float _Cull;
            float _ZTest;
            CBUFFER_END

            // Per mark: xyz its line's start (a rime's toe), w its half width (m).
            float4 _RockDecals[ROCK_MAX_DECALS];
            // Per mark: xyz its line's end (a rime: its toe again), w its kind (0 scar, 1 rime).
            float4 _RockDecalsB[ROCK_MAX_DECALS];
            // Per mark: x its strength (0..1), y its seed, z/w how far down its chute its start and end lie (share, 0 head … 1 toe).
            float4 _RockDecalsC[ROCK_MAX_DECALS];
            // x the marks.
            float4 _RockDecalInfo;

            struct Attributes
            {
                float3 positionOS : POSITION;   // marks: a unit box corner (−1..1); puffs: the quad's corner (xy −1..1)
                float2 slot : TEXCOORD0;        // x the mark or the puff
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                // marks: the mark; puffs: xy the corner, z its alpha, w its kind (0 dust, 1 mist, 2 steam)
                nointerpolation float4 data : TEXCOORD1;
                // puffs: rgb its colour, a its depth (m, from the camera)
                nointerpolation float4 colour : TEXCOORD2;
                float2 corner : TEXCOORD3;
                float fogCoord : TEXCOORD4;
            };

            float MarkHash(float2 cell, float seed)
            {
                uint2 c = (uint2)(int2)cell;
                return RockUnitOf(RockMix(c.x * 0x9E3779B1u ^ c.y, (uint)seed));
            }

            float MarkGround(float3 p)
            {
                bool covered;
                float4 g = FishMistGroundAt(p.xz, covered);
                return covered ? g.x + _FishMistGroundBase : p.y;
            }

            Varyings Collapse(Varyings o)
            {
                o.positionCS = float4(0.0, 0.0, -2.0, 1.0);
                o.data = 0.0;
                return o;
            }

            Varyings MarkBox(Attributes input, uint d, Varyings o)
            {
                if ((float)d >= _RockDecalInfo.x)
                {
                    return Collapse(o);
                }
                float4 a = _RockDecals[d];
                float4 b = _RockDecalsB[d];
                float2 ab = b.xz - a.xz;
                float len = length(ab);
                float2 x = len > 1e-3 ? ab / len : float2(1.0, 0.0);
                float2 z = float2(-x.y, x.x);
                float halfW = a.w + 1.0;
                float3 mid = 0.5 * (a.xyz + b.xyz);
                float ry = 0.5 * abs(b.y - a.y) + ROCK_MARK_REACH + 1.0;
                float2 xz = mid.xz + x * input.positionOS.x * (0.5 * len + halfW) + z * input.positionOS.z * halfW;
                float3 positionWS = float3(xz.x, mid.y + input.positionOS.y * ry, xz.y);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.data = float4((float)d, 0.0, 0.0, 0.0);
                return o;
            }

            Varyings Puff(Attributes input, uint slot, Varyings o)
            {
                float gravity = max(0.5, _RockInfo.w);
                float air = saturate(_RockInfo.z);
                float3 centre;
                float size;
                float alpha;
                float kind;
                float3 colour;
                if (slot < (uint)ROCK_DUST_PUFFS)
                {
                    // A fall's dust: puff j of stone j mod n, born at a hashed share of that stone's run.
                    uint f = slot / (uint)ROCK_STONES_PER_FALL;
                    uint j = slot - f * (uint)ROCK_STONES_PER_FALL;
                    float4 fall = _RockFalls[f];
                    if ((float)f >= _RockInfo.x || fall.w < 0.5 || air <= 0.0)
                    {
                        return Collapse(o);
                    }
                    uint n = max(1u, (uint)fall.w);
                    uint i = j % n;
                    uint turn = j / n;
                    uint rounds = ((uint)ROCK_STONES_PER_FALL + n - 1u) / n;
                    uint c = (uint)fall.x;
                    float4 chute = _RockChutes[c];
                    float4 chuteB = _RockChutesB[c];
                    bool sciara = chuteB.x > 0.5;
                    uint seed = (uint)fall.z;
                    RockStone s = RockStoneOf(seed, i, sciara, chute.z, chute.w, gravity);
                    RockRun run = RockRunOf(s.stop, s.peak, s.accel, s.decel);
                    float share = ((float)turn + 0.15 + 0.7 * RockU(seed, j, 0xD057u)) / (float)rounds;
                    float born = s.delay + share * RockRunTotal(run);
                    float age = fall.y - born;
                    if (age <= 0.0 || age >= ROCK_DUST_SECONDS)
                    {
                        return Collapse(o);
                    }
                    float a01 = age / ROCK_DUST_SECONDS;
                    float along = RockAlongAt(run, born - s.delay);
                    float2 dir;
                    float3 p = RockChutePoint(c, along, dir);
                    p += float3(dir.y, 0.0, -dir.x) * RockAcrossAt(s, along);
                    p.y = MarkGround(p);
                    size = (0.5 + 2.2 * sqrt(a01)) * (1.0 + 3.0 * s.size);
                    centre = p + float3(0.0, 0.3 * size + 1.2 * a01, 0.0);
                    alpha = (1.0 - a01) * (1.0 - a01) * 0.55 * air * chuteB.z * saturate(RockSpeedAt(run, born - s.delay) / max(1.0, run.peak) + 0.2);
                    half4 ground = FishGroundColourAt(p);
                    float3 dust = sciara ? float3(0.05, 0.05, 0.055) : float3(0.42, 0.40, 0.37);
                    colour = ground.a > 0.01 ? lerp(dust, saturate((float3)ground.rgb * 1.15 + 0.05), ground.a * 0.7) : dust;
                    kind = 0.0;
                }
                else
                {
                    // A breathing talus: summer mist out of the toe, winter steam off the head.
                    uint k = slot - (uint)ROCK_DUST_PUFFS;
                    uint cone = k / (uint)ROCK_BREATH_PER_CONE;
                    uint m = k - cone * (uint)ROCK_BREATH_PER_CONE;
                    if (cone >= (uint)ROCK_MAX_CONES || (float)cone >= _RockConeInfo.x || air <= 0.0)
                    {
                        return Collapse(o);
                    }
                    float4 apex = _RockCones[cone];
                    float4 toe = _RockConesB[cone];
                    float4 info = _RockConesC[cone];
                    float ice = apex.w;
                    float steam = toe.w;
                    bool mist = ice >= steam;
                    float strength = max(ice, steam) * info.z * air;
                    if (strength <= 0.0)
                    {
                        return Collapse(o);
                    }
                    uint seed = (uint)info.y;
                    // Whole cycles in the 10 000 s wrap: 20, 25, 40 or 50 s.
                    float period = m % 4u == 0u ? 20.0 : m % 4u == 1u ? 25.0 : m % 4u == 2u ? 40.0 : 50.0;
                    float phase = RockU(seed, m, 0xB4EAu);
                    float a01 = frac(_RockConeInfo.y / period + phase);
                    float bell = sin(3.14159265 * a01);
                    float2 down = toe.xz - apex.xz;
                    float length2 = max(1.0, length(down));
                    down /= length2;
                    float2 across = float2(-down.y, down.x);
                    float side = (RockU(seed, m, 0x51DEu) * 2.0 - 1.0);
                    if (mist)
                    {
                        float2 at = toe.xz + across * side * 0.5 * info.x + down * (RockU(seed, m, 0xA10Au) - 0.3) * 0.3 * length2 + down * a01 * 9.0;
                        centre = float3(at.x, 0.0, at.y);
                        centre.y = MarkGround(float3(at.x, toe.y, at.y)) + 0.4 + 0.3 * a01;
                        size = 2.2 + 3.0 * a01;
                        alpha = bell * 0.3 * strength;
                        colour = float3(0.82, 0.86, 0.9);
                        kind = 1.0;
                    }
                    else
                    {
                        float2 at = apex.xz + across * side * 0.2 * length2 + down * RockU(seed, m, 0xA10Au) * 0.25 * length2
                            + _RockConeInfo.zw * a01 * period * 0.3;
                        centre = float3(at.x, 0.0, at.y);
                        centre.y = MarkGround(float3(at.x, apex.y, at.y)) + 0.5 + 9.0 * a01;
                        size = 1.2 + 2.8 * a01;
                        alpha = bell * 0.18 * strength;
                        colour = float3(0.9, 0.9, 0.9);
                        kind = 2.0;
                    }
                }
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = centre + (right * input.positionOS.x + up * input.positionOS.y) * size;
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.corner = input.positionOS.xy;
                o.data = float4(0.0, 0.0, alpha, kind);
                o.colour = float4(colour, -TransformWorldToView(centre).z);
                return o;
            }

            Varyings MarkVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                uint slot = (uint)(input.slot.x + 0.5);
                if (_Mode < 0.5)
                {
                    o = MarkBox(input, slot, o);
                }
                else
                {
                    o = Puff(input, slot, o);
                }
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 MarkLight(float3 positionWS, float3 n)
            {
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord, positionWS, half4(1.0, 1.0, 1.0, 1.0));
                return mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * saturate(dot(n, mainLight.direction))
                    + FishTrilight((half3)n);
            }

            half4 MarkFragment(Varyings input) : SV_Target
            {
                if (_Mode > 0.5)
                {
                    float r2 = dot(input.corner, input.corner);
                    float alpha = input.data.z * exp(-3.0 * r2) * saturate(1.0 - r2);
                    // Soft against what stands behind it.
                    float2 uvp = input.positionCS.xy / _ScaledScreenParams.xy;
                    float scene = LinearEyeDepth(SampleSceneDepth(uvp), _ZBufferParams);
                    alpha *= saturate((scene - input.colour.a) / 1.5);
                    if (alpha <= 1e-3)
                    {
                        discard;
                    }
                    float3 lit = input.colour.rgb * (MarkLight(input.positionWS, float3(0.0, 1.0, 0.0)) * 0.6 + FishTrilight(half3(0.0, 1.0, 0.0)) * 0.4);
                    lit = MixFog(lit, input.fogCoord);
                    return half4(lit * alpha, alpha);
                }

                uint d = (uint)(input.data.x + 0.5);
                float4 a = _RockDecals[d];
                float4 b = _RockDecalsB[d];
                float4 c = _RockDecalsC[d];
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float depth = SampleSceneDepth(uv);
                float3 p = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float2 ab = b.xz - a.xz;
                float len2 = dot(ab, ab);
                float t = len2 > 1e-6 ? dot(p.xz - a.xz, ab) / len2 : 0.0;
                bool rime = b.w > 0.5;
                // A scar's boxes meet end to end: each owns [0, 1) of its line.
                if (!rime && (t < 0.0 || t >= 1.0))
                {
                    discard;
                }
                t = saturate(t);
                float3 onLine = lerp(a.xyz, b.xyz, t);
                float dist = length(p.xz - onLine.xz);
                float across = 1.0 - dist / max(0.1, a.w);
                if (across <= 0.0 || abs(p.y - onLine.y) > ROCK_MARK_REACH)
                {
                    discard;
                }
                float3 n = normalize(cross(ddy(p), ddx(p)));
                n = n.y < 0.0 ? -n : n;
                float alpha;
                float3 albedo;
                if (rime)
                {
                    // Hoar on the blocks: fine white specks thickest near the toe, a faint frosting over all of it.
                    float speck = MarkHash(floor(p.xz / 0.12), c.y) < 0.45 * across ? 1.0 : 0.0;
                    alpha = c.x * (0.22 * across + 0.6 * speck * across);
                    albedo = float3(0.855, 0.906, 0.94);
                }
                else
                {
                    // Fresh chips, thickest down the runout and along the middle of the chute.
                    float down = lerp(c.z, c.w, t);
                    float density = smoothstep(0.0, 0.6, across) * lerp(0.25, 1.0, smoothstep(0.3, 1.0, down));
                    float chip = MarkHash(floor(p.xz / 0.22), c.y) < 0.55 * density ? 1.0 : 0.0;
                    alpha = c.x * (0.12 * density + 0.75 * chip);
                    albedo = float3(0.776, 0.745, 0.686);
                }
                // Not on steep rock faces or on anything standing (a wall, a body): the ground only.
                alpha *= smoothstep(0.35, 0.6, n.y);
                if (alpha <= 1e-3)
                {
                    discard;
                }
                float3 lit = albedo * MarkLight(p, n);
                lit = MixFog(lit, ComputeFogFactor(TransformWorldToHClip(p).z));
                return half4(lit * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
