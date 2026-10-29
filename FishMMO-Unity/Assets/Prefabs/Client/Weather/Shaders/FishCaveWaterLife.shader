// What lives on and in a cave's still water near the camera (CaveWaterLife, 2026-10-26, phase I2n-c; decision 86 (i) and its
// leeches, decision 97), drawn with two materials of this shader from a few numbers a pool (SceneCaveWater.cs, number for number):
//
// _Mode 0, the BEADS: marsh gas rising in trails from the drowned muck, each bead a speck of the light round it, and the ring it
// sends over the surface as it breaks it. Every bead is the shared motion clock's (a pool's vents and bursts hashed from its
// surface's position), so every player sees the same bead break the same surface at the same moment.
//
// _Mode 1, the LEECHES: olive, rust-striped ribbons undulating through the water on a slow circuit of the pool (the clock's), drawn
// toward a wader's legs as the viewer's own pull grows (CaveWaterLife eases it on each client: nothing on the wire, no damage).
// Opaque and depth-written, before the water's surface is drawn over them.
Shader "FishMMO/World/Cave Water Life"
{
    Properties
    {
        [HideInInspector] _Mode ("Mode (0 beads, 1 leeches)", Float) = 0
        [HideInInspector] _SrcBlend ("Src blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst blend", Float) = 10
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+45"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "CaveWaterLife"
            Tags { "LightMode" = "UniversalForward" }
            // Beads premultiplied (a ring covers by its alpha and adds its glint; a bead only adds); leeches opaque.
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishAmbient.hlsl"
            #include "FishGroundMap.hlsl"

            // CaveWaterLife.MaxPools; SceneCaveWater's sizes and rates.
            #define WATER_POOLS 8
            #define WATER_VENTS 6
            #define WATER_BEADS 4
            #define WATER_LEECHES 6
            #define WATER_WRAP 10000.0
            #define WATER_BEAD_SPACING 0.35
            #define WATER_BEAD_SPEED 0.24
            #define WATER_BEAD_SPEED_SPREAD 0.1
            #define WATER_RING_SECONDS 1.2
            #define WATER_RING_SPEED 0.3
            #define WATER_MOST_DEPTH 2.0
            #define WATER_TURN 6.2831853

            CBUFFER_START(UnityPerMaterial)
            float _Mode;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
            // x the motion clock wrapped at 10 000 s, y the pools. The material's own (CaveWaterLife sets both modes'), so the
            // draws stay in the SRP Batcher; the arrays below are globals the system sets before each camera renders.
            float4 _WaterInfo;
            CBUFFER_END

            // Per pool, nearest first (CaveWaterLife): xyz its surface's middle, w its radius (m).
            float4 _WaterPools[WATER_POOLS];
            // x its depth (m), y its vents, z its leeches, w its fade with distance (0: an empty slot).
            float4 _WaterShape[WATER_POOLS];
            // x its seed's high 16 bits, y its low 16 (a 32-bit seed in one float rounds), z its bead weight.
            float4 _WaterSeeds[WATER_POOLS];
            // xyz the feet of the one wading in it (this viewer's nearest), w this viewer's pull on its leeches (0..1).
            float4 _WaterWaders[WATER_POOLS];

            struct Attributes
            {
                float3 slot : POSITION;     // x the pool, y the part (0 bead, 1 ring, 2 leech), z which (vent·4 + bead, or the leech)
                float2 corner : TEXCOORD0;  // x along (0..1: a leech's segment end), y across (0/1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                // xy the corner (-1..1 across; a leech's x along 0..1), z the part, w a bead's share of its quad.
                float4 quad : TEXCOORD1;
                // A ring: xyz its centre, w its radius; a leech: x its stripes' phase.
                nointerpolation float4 ring : TEXCOORD2;
                // A ring's age share (0 new .. 1 gone), the fade with distance.
                nointerpolation float2 life : TEXCOORD3;
                float fogCoord : TEXCOORD4;
            };

            // ── SceneCaveWater's hashes ──

            uint WaterMix(uint x)
            {
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }

            uint WaterHash(uint seed, uint a, uint b)
            {
                return WaterMix(seed ^ WaterMix((a * 0x9E3779B1u) ^ (b + 0x632BE5ABu)));
            }

            float WaterUnit(uint h)
            {
                return (float)(h >> 8) * (1.0 / 16777216.0);
            }

            // SceneCaveWater.Periods, without an array (a select chain is the same on every back end).
            float WaterPeriod(uint k)
            {
                return k == 0u ? 12.5 : k == 1u ? 16.0 : k == 2u ? 20.0 : k == 3u ? 25.0 : k == 4u ? 40.0 : 50.0;
            }

            float2 WaterVentOffset(uint seed, uint vent, float radius)
            {
                float a = WATER_TURN * WaterUnit(WaterHash(seed, vent, 1u));
                float r = 0.85 * radius * sqrt(WaterUnit(WaterHash(seed, vent, 2u)));
                return float2(cos(a), sin(a)) * r;
            }

            // SceneCaveWater.Bead: false when none is out; else its rise over the bed and its ring's age (-1 while it rises).
            bool WaterBead(float t, uint seed, uint vent, uint bead, float depth, float bubbles, out float rise, out float ringAge, out uint burst)
            {
                rise = 0.0;
                ringAge = -1.0;
                float period = WaterPeriod(WaterHash(seed, vent, 3u) % 6u);
                float cycle = floor(t / period);
                float into = t - cycle * period;
                burst = WaterHash(seed, vent * 64u + 7u, (uint)cycle);
                if (WaterUnit(burst) >= saturate(0.35 + 0.5 * bubbles))
                {
                    return false;
                }
                float d = clamp(depth, 0.05, WATER_MOST_DEPTH);
                float speed = WATER_BEAD_SPEED + WATER_BEAD_SPEED_SPREAD * WaterUnit(WaterHash(burst, bead, 4u));
                float age = into - ((float)bead * WATER_BEAD_SPACING + 0.2 * WaterUnit(WaterHash(burst, bead, 5u)));
                if (age < 0.0)
                {
                    return false;
                }
                float up = d / speed;
                if (age < up)
                {
                    rise = age * speed;
                    return true;
                }
                ringAge = age - up;
                rise = d;
                return ringAge < WATER_RING_SECONDS;
            }

            // SceneCaveWater.LeechHome.
            float3 WaterLeechHome(float t, uint seed, uint leech, float3 surface, float radius, float depth)
            {
                uint h = WaterHash(seed, leech + 101u, 11u);
                float turns = 30.0 + floor(61.0 * WaterUnit(WaterHash(h, 1u, 0u)));
                float sense = WaterUnit(WaterHash(h, 2u, 0u)) < 0.5 ? -1.0 : 1.0;
                float phase = WaterUnit(WaterHash(h, 3u, 0u));
                float a = WATER_TURN * (phase + sense * turns * (t / WATER_WRAP));
                float r = radius * (0.3 + 0.55 * WaterUnit(WaterHash(h, 4u, 0u)));
                float d = clamp(depth, 0.1, WATER_MOST_DEPTH);
                float y = surface.y - d * (0.35 + 0.45 * WaterUnit(WaterHash(h, 5u, 0u))) + 0.04 * sin(WATER_TURN * (t / 40.0 + phase));
                return float3(surface.x + cos(a) * r, y, surface.z + sin(a) * r);
            }

            // SceneCaveWater.LeechAt.
            float3 WaterLeechAt(float3 home, float4 wader, uint seed, uint leech, float3 surface, float depth)
            {
                uint h = WaterHash(seed, leech + 101u, 12u);
                float late = 0.35 * WaterUnit(h);
                float pull = saturate((wader.w - late) / 0.5);
                pull = pull * pull * (3.0 - 2.0 * pull);
                float a = WATER_TURN * WaterUnit(WaterHash(h, 1u, 0u));
                float r = 0.1 + 0.18 * WaterUnit(WaterHash(h, 2u, 0u));
                float bed = surface.y - clamp(depth, 0.1, WATER_MOST_DEPTH);
                float y = clamp(wader.y + 0.08 + 0.3 * WaterUnit(WaterHash(h, 3u, 0u)), bed + 0.03, surface.y - 0.04);
                float3 target = float3(wader.x + cos(a) * r, y, wader.z + sin(a) * r);
                return lerp(home, target, pull);
            }

            float WaterPixelMetres(float3 positionWS)
            {
                float focal = max(abs(UNITY_MATRIX_P._m11), 1e-4) * max(_ScreenParams.y, 1.0) * 0.5;
                float depth = max(-TransformWorldToView(positionWS).z, 1e-3);
                return unity_OrthoParams.w > 0.5 ? 1.0 / focal : depth / focal;
            }

            Varyings Hidden()
            {
                Varyings output = (Varyings)0;
                output.positionCS = float4(0.0, 0.0, -2.0, 1.0);
                return output;
            }

            Varyings Vert(Attributes input)
            {
                int index = min(WATER_POOLS - 1, (int)round(input.slot.x));
                int part = (int)round(input.slot.y);
                uint which = (uint)round(input.slot.z);
                float4 pool = _WaterPools[index];
                float4 shape = _WaterShape[index];
                float4 seeds = _WaterSeeds[index];
                bool leeches = _Mode > 0.5;
                if ((float)index >= _WaterInfo.y || shape.w <= 0.001 || (part == 2) != leeches)
                {
                    return Hidden();
                }
                uint seed = ((uint)round(seeds.x) << 16) | (uint)round(seeds.y);
                float t = _WaterInfo.x;
                float depth = shape.x;
                Varyings output = (Varyings)0;
                output.life = float2(0.0, shape.w);
                output.quad.z = part;
                float3 worldPos;
                if (part == 2)
                {
                    // A leech: a ribbon of segments, its body's wave running back along it as it swims.
                    if ((float)which >= shape.z)
                    {
                        return Hidden();
                    }
                    float3 home = WaterLeechHome(t, seed, which, pool.xyz, pool.w, depth);
                    float3 at = WaterLeechAt(home, _WaterWaders[index], seed, which, pool.xyz, depth);
                    float3 homeAhead = WaterLeechHome(t + 0.25, seed, which, pool.xyz, pool.w, depth);
                    float3 ahead = WaterLeechAt(homeAhead, _WaterWaders[index], seed, which, pool.xyz, depth);
                    float2 heading = ahead.xz - at.xz;
                    heading = dot(heading, heading) > 1e-10 ? normalize(heading) : float2(1.0, 0.0);
                    float2 side = float2(-heading.y, heading.x);
                    uint h = WaterHash(seed, which + 101u, 13u);
                    float length_ = 0.06 + 0.06 * WaterUnit(h);
                    float u = input.corner.x;
                    float girth = 0.0065 * length_ / 0.09 * pow(saturate(sin(3.14159265 * clamp(u, 0.04, 0.96))), 0.6);
                    float wave = 0.006 * sin(WATER_TURN * (1.6 * u - 1.4 * t));
                    float2 xz = at.xz + heading * ((0.5 - u) * length_) + side * ((input.corner.y * 2.0 - 1.0) * girth);
                    worldPos = float3(xz.x, at.y + wave, xz.y);
                    output.quad.xy = float2(u, input.corner.y * 2.0 - 1.0);
                    output.ring = float4(WaterUnit(WaterHash(h, 1u, 0u)), 0.0, 0.0, 0.0);
                }
                else
                {
                    uint vent = which / WATER_BEADS;
                    uint bead = which - vent * WATER_BEADS;
                    if ((float)vent >= shape.y)
                    {
                        return Hidden();
                    }
                    float rise, ringAge;
                    uint burst;
                    if (!WaterBead(t, seed, vent, bead, depth, seeds.z, rise, ringAge, burst))
                    {
                        return Hidden();
                    }
                    float2 off = WaterVentOffset(seed, vent, pool.w);
                    float bed = pool.y - clamp(depth, 0.05, WATER_MOST_DEPTH);
                    if (part == 1)
                    {
                        // Its ring, spreading over the surface from where it broke it.
                        if (ringAge < 0.0)
                        {
                            return Hidden();
                        }
                        float radius = WATER_RING_SPEED * ringAge + 0.01;
                        float2 xy = input.corner * 2.0 - 1.0;
                        float reach = radius + 0.03;
                        worldPos = float3(pool.x + off.x + xy.x * reach, pool.y + 0.004, pool.z + off.y + xy.y * reach);
                        output.quad.xy = xy;
                        output.ring = float4(pool.x + off.x, pool.y, pool.z + off.y, radius);
                        output.life.x = ringAge / WATER_RING_SECONDS;
                    }
                    else
                    {
                        // The bead itself, wobbling a little as it rises: a camera-facing speck, at least a pixel and a half.
                        if (ringAge >= 0.0)
                        {
                            return Hidden();
                        }
                        float wob = WATER_TURN * WaterUnit(WaterHash(burst, bead, 6u));
                        float3 at = float3(pool.x + off.x + 0.012 * sin(7.0 * rise + wob), bed + rise, pool.z + off.y + 0.012 * cos(6.0 * rise + wob));
                        float size = 0.004 + 0.004 * WaterUnit(WaterHash(burst, bead, 7u));
                        float pixel = WaterPixelMetres(at);
                        float halfWidth = max(size, 0.75 * pixel);
                        float3 toCamera = normalize(_WorldSpaceCameraPos.xyz - at);
                        float3 right = cross(float3(0.0, 1.0, 0.0), toCamera);
                        right = dot(right, right) > 1e-8 ? normalize(right) : float3(1.0, 0.0, 0.0);
                        float3 up = cross(toCamera, right);
                        float2 xy = input.corner * 2.0 - 1.0;
                        worldPos = at + (right * xy.x + up * xy.y) * halfWidth;
                        output.quad.xy = xy;
                        output.quad.w = size / halfWidth;
                    }
                }
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // The light at a point other than the sky's (as FishCaveDrips' DripLights): the sun where it reaches, the additional
            // lights through the cluster loop under Forward+ or the per-object list; their glint off a surface of normal n seen from v.
            void WaterLights(float3 positionWS, float3 n, float3 v, float2 screenUV, float power, out float3 lit, out float3 specular, out float3 diffuse)
            {
                float under = (float)FishUnderground(positionWS);
                Light mainLight = GetMainLight();
                float3 sun = mainLight.color * mainLight.distanceAttenuation * (1.0 - under);
                lit = sun;
                diffuse = sun * saturate(dot(n, mainLight.direction));
                specular = sun * pow(saturate(dot(n, normalize(mainLight.direction + v))), power);
#if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalizedScreenSpaceUV = screenUV;
                uint pixelLightCount = GetAdditionalLightsCount();
#if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    Light light = GetAdditionalLight(lightIndex, positionWS);
                    float3 arriving = light.color * light.distanceAttenuation;
                    lit += arriving;
                    diffuse += arriving * saturate(abs(dot(n, light.direction)));
                    specular += arriving * pow(saturate(dot(n, normalize(light.direction + v))), power);
                }
#endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, positionWS);
                    float3 arriving = light.color * light.distanceAttenuation;
                    lit += arriving;
                    diffuse += arriving * saturate(abs(dot(n, light.direction)));
                    specular += arriving * pow(saturate(dot(n, normalize(light.direction + v))), power);
                LIGHT_LOOP_END
#endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float under = (float)FishUnderground(input.positionWS);
                float3 lit, specular, diffuse;
                if (input.quad.z > 1.5)
                {
                    // A leech: olive-black above, two rust stripes down its back, a paler belly; lit as a wet skin.
                    float across = input.quad.y;
                    float stripe = exp(-pow((abs(across) - 0.45) / 0.12, 2.0));
                    float ring = 0.5 + 0.5 * sin(WATER_TURN * (input.quad.x * 9.0 + input.ring.x));
                    float3 albedo = lerp(float3(0.07, 0.075, 0.035), float3(0.32, 0.13, 0.05), saturate(stripe * (0.6 + 0.4 * ring)));
                    WaterLights(input.positionWS, float3(0.0, 1.0, 0.0), v, screenUV, 40.0, lit, specular, diffuse);
                    float3 ambient = (float3)FishTrilight((half3)float3(0.0, 1.0, 0.0)) * (1.0 - 0.96 * under);
                    float3 rgb = albedo * (diffuse + ambient) + 0.25 * specular;
                    rgb = MixFog(rgb, input.fogCoord);
                    return half4((half3)rgb, 1.0h);
                }
                if (input.quad.z > 0.5)
                {
                    // A ring: a thin crest at its radius, fading as it spreads and as it ages; what shows is the lights' glint in it.
                    float dist = length(input.positionWS.xz - input.ring.xz);
                    float crest = exp(-pow((dist - input.ring.w) / 0.01, 2.0)) * (1.0 - input.life.x) * input.life.y;
                    if (crest <= 1e-3)
                    {
                        discard;
                    }
                    float2 out_ = dist > 1e-5 ? (input.positionWS.xz - input.ring.xz) / dist : float2(1.0, 0.0);
                    float3 n = normalize(float3(out_.x * 0.35, 1.0, out_.y * 0.35));
                    const float power = 300.0;
                    WaterLights(input.positionWS, n, v, screenUV, power, lit, specular, diffuse);
                    float3 sky = (float3)FishTrilight((half3)reflect(-v, n)) * (1.0 - 0.96 * under) * 0.05;
                    float3 rgb = (specular * (power + 8.0) / 25.13 * 0.04 + lit * 0.04 + sky) * crest;
                    rgb = MixFogColor(rgb, float3(0.0, 0.0, 0.0), input.fogCoord);
                    return half4((half3)rgb, (half)(0.15 * crest));
                }
                // A bead: a speck of the light round it, as a lens holds it, soft to its edge.
                float r2 = dot(input.quad.xy, input.quad.xy);
                if (r2 > 1.0)
                {
                    discard;
                }
                float cover = (1.0 - r2) * input.quad.w * input.quad.w * input.life.y;
                WaterLights(input.positionWS, v, v, screenUV, 1.0, lit, specular, diffuse);
                float3 ambient = (float3)FishTrilight((half3)float3(0.0, 1.0, 0.0)) * (1.0 - 0.96 * under);
                float3 rgb = (1.5 * lit + 0.5 * ambient) * cover;
                rgb = MixFogColor(rgb, float3(0.0, 0.0, 0.0), input.fogCoord);
                return half4((half3)rgb, 0.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
