// A sea hall's vents and ice near the camera (SeaHallVents, 2026-10-26, phase I2n-b, decision 83), drawn in one call from a few numbers
// a smoker and a hall (SceneSeaHallVents.cs, number for number). Every part is the shared motion clock's; nothing scrubs (each phase
// runs on a period that is a whole number of cycles in its wrap, at a speed of its own):
//   part 0  PLUMES: puffs climbing from each smoker's orifice to its ceiling, widening as they entrain the water, black (white for a
//           white smoker, a faint shimmer for a beehive), their strength the plume tide's times the smoker's flicker (computed on the
//           CPU per smoker from the same clock); an ember glow at the orifice by night (fyr on flode, the Glow NIGHT mode's curve).
//   part 1  the SMOKE CEILING: puffs spreading under the water's surface from where each plume meets it, drifting round the hall on a
//           pure-clock current; the Great Smoker's also under the ice roof over the air pocket.
//   part 2  FLANGE MIRRORS: small shimmering inverted pools under the ledges of the tall chimneys, pulsing with the plume.
//   part 3  CO2 BEADS: liquid carbon-dioxide droplets rising 4–10 m off each white smoker.
//   part 4  BRINICLES: tapering tubes of ice hung from the dome's roof over the deepest water in two groves, each growing over its own
//           6–18 h and snapping; part 5 the puff of ice where one snapped.
//   part 6  BRINE MIRRORS: the dense hot brine's surface standing in each brine pool on the floor.
// Premultiplied over what is behind (Blend One OneMinusSrcAlpha), no depth written. No compute, no geometry: the same on WebGPU, Vulkan,
// D3D and Metal. #pragma use_dxc vulkan (the pass includes nothing of FishIndirectInstancing).
Shader "FishMMO/World/Sea Hall Vents"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+44"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "SeaHallVents"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
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

            // SceneSeaHallVents' sizes and rates.
            #define HALL_SMOKERS 24
            #define HALL_BRINE 4
            #define HALL_WRAP 10000.0
            #define HALL_BRINICLE_WRAP 129600.0
            #define HALL_RISE_SLOWEST 0.3
            #define HALL_RISE_FASTEST 1.0
            #define HALL_BRINICLE_SHORTEST 2.0
            #define HALL_BRINICLE_LONGEST 12.0
            #define HALL_SNAP_SECONDS 4.0
            #define HALL_CEILING_TURNS 1.0
            #define HALL_PER_GROVE 40
            #define HALL_TURN 6.2831853
            // Kinds (SceneSeaHallVents.Black … Great).
            #define HALL_BLACK 0
            #define HALL_WHITE 1
            #define HALL_BEEHIVE 2
            #define HALL_GREAT 3

            // Per smoker, nearest first (SeaHallVents): xyz its orifice, w the orifice's radius (m).
            float4 _HallSmokers[HALL_SMOKERS];
            // x the chimney's height under the orifice, y the plume's rise to its ceiling, z its strength now (0..1), w its kind.
            float4 _HallSmokerShape[HALL_SMOKERS];
            // x its seed's high 16 bits, y its low 16, z its own turn (degrees), w its fade with distance (0: an empty slot).
            float4 _HallSmokerSeed[HALL_SMOKERS];
            // The hall's own numbers are the material's (SeaHallVents), so the draw stays in the SRP Batcher; the arrays are
            // globals the system sets before each camera renders.
            CBUFFER_START(UnityPerMaterial)
            // The dome: xyz its vault's centre, w its heading (radians); radii along, up, across, w the water's surface y.
            float4 _HallDome;
            float4 _HallRadii;
            // x the motion clock at 10 000 s, y at 36 h (the brinicles'), z the smokers drawn, w the night (the NIGHT mode's curve, 0.15..1).
            float4 _HallInfo;
            // x the waterline's radius, y the hall's seed's high 16 bits, z its low, w the hall's fade with distance.
            float4 _HallWater;
            CBUFFER_END
            // Per brine pool: xyz its mirror's middle, w its radius (0: none).
            float4 _HallBrine[HALL_BRINE];

            struct Attributes
            {
                float3 slot : POSITION;     // x the part, y the smoker / grove / pool, z which (puff, flange, bead, member·2 + plane)
                float2 corner : TEXCOORD0;  // 0/1 corner
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                // xy the corner (-1..1), z the part, w a share along (a brinicle's root 0 .. tip 1).
                float4 quad : TEXCOORD1;
                // rgb the albedo (premultiplied by nothing), a its cover.
                nointerpolation float4 tint : TEXCOORD2;
                // rgb the glow it gives off (HDR), a a spare.
                nointerpolation float4 glow : TEXCOORD3;
                float fogCoord : TEXCOORD4;
            };

            // ── SceneCaveWater's hashes (SceneSeaHallVents takes them) ──

            uint HallMix(uint x)
            {
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }

            uint HallHash(uint seed, uint a, uint b)
            {
                return HallMix(seed ^ HallMix((a * 0x9E3779B1u) ^ (b + 0x632BE5ABu)));
            }

            float HallUnit(uint h)
            {
                return (float)(h >> 8) * (1.0 / 16777216.0);
            }

            // SceneSeaHallVents.WholePeriod.
            float HallWholePeriod(float seconds, float wrap)
            {
                return wrap / max(1.0, round(wrap / max(0.01, seconds)));
            }

            // SceneSeaHallVents.RiseSpeed / PuffShare.
            float HallRiseSpeed(uint seed)
            {
                return HALL_RISE_SLOWEST + (HALL_RISE_FASTEST - HALL_RISE_SLOWEST) * HallUnit(HallHash(seed, 3u, 1u));
            }

            float HallPuffShare(float t, uint seed, uint puff, float rise)
            {
                float period = HallWholePeriod(max(0.5, rise) / HallRiseSpeed(seed), HALL_WRAP);
                return frac(t / period + HallUnit(HallHash(seed, puff, 4u)));
            }

            // SceneSeaHallVents.CeilingShare / CeilingTurn.
            float HallCeilingShare(float t, uint seed, uint puff)
            {
                float period = HallWholePeriod(120.0 + 120.0 * HallUnit(HallHash(seed, puff, 9u)), HALL_WRAP);
                return frac(t / period + HallUnit(HallHash(seed, puff, 10u)));
            }

            // SceneSeaHallVents.BriniclePeriod (a select chain, not an array: the same on every back end).
            float HallBriniclePeriod(uint seed)
            {
                uint k = HallHash(seed, 31u, 0u) % 4u;
                return 3600.0 * (k == 0u ? 6.0 : k == 1u ? 9.0 : k == 2u ? 12.0 : 18.0);
            }

            // The dome's roof over a point (PointOfInterestCaveDressing.RoofOver): its ellipsoid's top over the point's plan.
            float HallRoofOver(float2 xz)
            {
                float2 along = float2(sin(_HallDome.w), cos(_HallDome.w));
                float2 side = float2(along.y, -along.x);
                float2 o = xz - _HallDome.xz;
                float u = dot(o, along) / max(1.0, _HallRadii.x), v = dot(o, side) / max(1.0, _HallRadii.z);
                float q = 1.0 - u * u - v * v;
                return _HallDome.y + _HallRadii.y * sqrt(max(0.0, q));
            }

            float HallPixelMetres(float3 positionWS)
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

            // A camera-facing quad of half-width h at a point.
            float3 HallBillboard(float3 at, float2 xy, float h)
            {
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                return at + (right * xy.x + up * xy.y) * h;
            }

            Varyings Vert(Attributes input)
            {
                int part = (int)round(input.slot.x);
                int index = (int)round(input.slot.y);
                uint which = (uint)round(input.slot.z);
                float2 xy = input.corner * 2.0 - 1.0;
                float t = _HallInfo.x;
                float night = _HallInfo.w;
                float water = _HallRadii.w;
                if (_HallWater.w <= 0.001)
                {
                    return Hidden();
                }
                Varyings output = (Varyings)0;
                output.quad = float4(xy, (float)part, 0.0);
                float3 worldPos;

                if (part <= 3)
                {
                    // ── A smoker's parts ──
                    int s = min(HALL_SMOKERS - 1, index);
                    float4 vent = _HallSmokers[s];
                    float4 shape = _HallSmokerShape[s];
                    float4 seeds = _HallSmokerSeed[s];
                    if ((float)index >= _HallInfo.z || seeds.w <= 0.001)
                    {
                        return Hidden();
                    }
                    uint seed = ((uint)round(seeds.x) << 16) | (uint)round(seeds.y);
                    int kind = (int)round(shape.w);
                    float strength = shape.z;
                    float fade = seeds.w * _HallWater.w;
                    float3 orifice = vent.xyz;
                    bool great = kind == HALL_GREAT;
                    float3 smoke = kind == HALL_WHITE ? float3(0.78, 0.79, 0.76) : float3(0.03, 0.028, 0.025);
                    if (part == 0)
                    {
                        // A plume puff on its own climb; a beehive's only a faint low shimmer.
                        float rise = kind == HALL_BEEHIVE ? 1.5 : shape.y;
                        uint puffs = kind == HALL_BEEHIVE ? 8u : 48u;
                        if (which >= puffs || strength <= 0.01)
                        {
                            return Hidden();
                        }
                        float sh = HallPuffShare(t, seed, which, rise);
                        float above = sh * rise;
                        float width = vent.w + 0.12 * above;
                        float a = HALL_TURN * HallUnit(HallHash(seed, which, 5u));
                        float r = sqrt(HallUnit(HallHash(seed, which, 6u))) * width * (0.35 + 0.65 * strength);
                        // A slow lean on the hall's own drift, more the higher it climbs.
                        float drift = 0.6 * sh * sh * rise * 0.05;
                        float3 at = orifice + float3(cos(a) * r + drift, above, sin(a) * r);
                        float size = 0.5 * vent.w + 0.9 * width;
                        worldPos = HallBillboard(at, xy, size);
                        float cover = smoothstep(0.0, 0.05, sh) * (1.0 - smoothstep(0.75, 1.0, sh)) * strength * fade;
                        cover *= kind == HALL_BEEHIVE ? 0.12 : kind == HALL_WHITE ? 0.45 : 0.6;
                        output.tint = float4(smoke, cover);
                        float ember = pow(saturate(1.0 - sh * 5.0), 3.0) * strength * night;
                        output.glow = float4(float3(1.0, 0.36, 0.09) * (ember * (great ? 5.0 : 3.0) * (kind == HALL_WHITE ? 0.3 : 1.0)), 0.0);
                    }
                    else if (part == 1)
                    {
                        // The ceiling: spreading from where the plume meets the water's underside (eight puffs a smoker), turning round the
                        // hall on the clock's current; the Great Smoker's crown stands in the air pocket, so its sixteen spread under the ice roof.
                        if (kind == HALL_BEEHIVE || strength <= 0.01 || which >= (great ? 16u : 8u))
                        {
                            return Hidden();
                        }
                        bool roof = great;
                        float sh = HallCeilingShare(t, seed, which);
                        float reach = (great ? 30.0 : 8.0 + 1.2 * shape.y * 0.25) * sqrt(sh);
                        float a = HALL_TURN * HallUnit(HallHash(seed, which, 11u)) + HALL_TURN * HALL_CEILING_TURNS * frac(t / HALL_WRAP);
                        float2 p = orifice.xz + float2(cos(a), sin(a)) * reach;
                        float y = roof ? HallRoofOver(p) - 1.2 - 1.5 * HallUnit(HallHash(seed, which, 12u))
                            : min(orifice.y + shape.y, water - 0.5) - 1.2 * HallUnit(HallHash(seed, which, 12u));
                        float3 at = float3(p.x, y, p.y);
                        float size = 2.5 + 3.5 * sqrt(sh);
                        worldPos = HallBillboard(at, xy, size);
                        float cover = smoothstep(0.0, 0.1, sh) * (1.0 - sh) * strength * fade * (roof ? 0.22 : 0.16);
                        output.tint = float4(roof ? float3(0.12, 0.11, 0.1) : smoke, cover);
                        output.glow = float4(0.0, 0.0, 0.0, 0.0);
                    }
                    else if (part == 2)
                    {
                        // A flange mirror: a disc facing down under a ledge of a tall black chimney.
                        if (which >= 4u || !(kind == HALL_BLACK && shape.x >= 4.0 || great))
                        {
                            return Hidden();
                        }
                        float share = 0.35 + 0.15 * (float)which + 0.05 * HallUnit(HallHash(seed, which, 12u));
                        float turn = radians(seeds.z + 90.0 * (float)which + 40.0 * HallUnit(HallHash(seed, which, 13u)));
                        float baseRadius = vent.w / 0.35;
                        float radiusAt = baseRadius * (1.0 - 0.6 * share) + 0.3;
                        float y = orifice.y - (1.0 - share) * shape.x;
                        float2 dir = float2(sin(turn), cos(turn));
                        float discRadius = 0.3 + 0.6 * HallUnit(HallHash(seed, which, 15u));
                        float2 c = orifice.xz + dir * (radiusAt + discRadius * 0.6);
                        worldPos = float3(c.x + xy.x * discRadius, y, c.y + xy.y * discRadius);
                        float phase = HallUnit(HallHash(seed, which, 14u));
                        float shimmer = (0.6 + 0.4 * (0.5 + 0.5 * sin(HALL_TURN * frac(t / 2.5 + phase)))) * saturate(strength);
                        output.tint = float4(float3(0.55, 0.56, 0.6), 0.75 * fade);
                        output.glow = float4(float3(1.0, 0.42, 0.12) * (shimmer * (0.4 + 1.6 * night)), shimmer);
                    }
                    else
                    {
                        // A CO2 bead off a white smoker: a bright droplet rising, wobbling.
                        if (kind != HALL_WHITE || which >= 24u)
                        {
                            return Hidden();
                        }
                        float height = 4.0 + 6.0 * HallUnit(HallHash(seed, 21u, 0u));
                        float speed = 0.22 + 0.08 * HallUnit(HallHash(seed, which, 22u));
                        float period = HallWholePeriod(height / speed, HALL_WRAP);
                        float up = height * frac(t / period + HallUnit(HallHash(seed, which, 23u)));
                        uint trail = which % 3u;
                        float a = HALL_TURN * HallUnit(HallHash(seed, trail, 24u));
                        float2 off = float2(cos(a), sin(a)) * (vent.w * 0.8);
                        float3 at = orifice + float3(off.x + 0.03 * sin(5.0 * up + (float)which), up, off.y + 0.03 * cos(4.0 * up));
                        float size = 0.025 + 0.02 * HallUnit(HallHash(seed, which, 25u));
                        float halfWidth = max(size, 0.75 * HallPixelMetres(at));
                        worldPos = HallBillboard(at, xy, halfWidth);
                        output.quad.w = size / halfWidth;
                        output.tint = float4(float3(0.9, 0.95, 1.0), (1.0 - smoothstep(0.85, 1.0, up / height)) * fade);
                        output.glow = float4(0.0, 0.0, 0.0, 0.0);
                    }
                }
                else if (part <= 5)
                {
                    // ── A brinicle and its snap ──
                    uint hallSeed = ((uint)round(_HallWater.y) << 16) | (uint)round(_HallWater.z);
                    uint member = part == 4 ? which / 2u : which;
                    if (member >= (uint)HALL_PER_GROVE || index > 1)
                    {
                        return Hidden();
                    }
                    uint grove = (uint)index;
                    float gShare = 0.55 * sqrt(HallUnit(HallHash(hallSeed, grove, 41u)));
                    float gTurn = HALL_TURN * HallUnit(HallHash(hallSeed, grove, 42u));
                    float2 centre = _HallDome.xz + float2(cos(gTurn), sin(gTurn)) * (gShare * _HallWater.x);
                    uint seed = HallHash(hallSeed, grove * 64u + member, 43u);
                    float ga = HALL_TURN * HallUnit(HallHash(seed, 1u, 44u));
                    float gr = (8.0 + 7.0 * HallUnit(HallHash(hallSeed, grove, 45u))) * sqrt(HallUnit(HallHash(seed, 2u, 44u)));
                    float2 p = centre + float2(cos(ga), sin(ga)) * gr;
                    float roof = HallRoofOver(p);
                    float longest = min(HALL_BRINICLE_SHORTEST + (HALL_BRINICLE_LONGEST - HALL_BRINICLE_SHORTEST) * HallUnit(HallHash(seed, 33u, 0u)),
                        max(0.0, roof - water - 0.5));
                    float period = HallBriniclePeriod(seed);
                    float phase = frac(_HallInfo.y / period + HallUnit(HallHash(seed, 32u, 0u)));
                    float since = phase * period;
                    float length_ = longest * phase;
                    if (longest < 0.5)
                    {
                        return Hidden();
                    }
                    if (part == 4)
                    {
                        // Two crossed quads tapering to the tip, from a little up in the ice.
                        if (length_ < 0.05)
                        {
                            return Hidden();
                        }
                        float plane = (float)(which % 2u);
                        float yaw = HALL_TURN * HallUnit(HallHash(seed, 3u, 44u)) + plane * 1.5707963;
                        float girth = (0.08 + 0.12 * HallUnit(HallHash(seed, 4u, 44u))) * (0.5 + 0.5 * sqrt(length_ / max(0.5, longest)));
                        float v = input.corner.y;
                        float halfWidth = girth * (1.0 - 0.85 * v);
                        float2 across = float2(cos(yaw), sin(yaw)) * (xy.x * halfWidth);
                        worldPos = float3(p.x + across.x, roof + 0.3 - v * (length_ + 0.3), p.y + across.y);
                        output.quad.w = v;
                        output.tint = float4(float3(0.72, 0.84, 0.92), 0.6 * _HallWater.w);
                        output.glow = float4(0.0, 0.0, 0.0, 0.0);
                    }
                    else
                    {
                        // Its snap: a puff of ice dust where the old one broke, for a few seconds.
                        if (since >= HALL_SNAP_SECONDS)
                        {
                            return Hidden();
                        }
                        float age = since / HALL_SNAP_SECONDS;
                        float3 at = float3(p.x, roof - 0.5 - 1.5 * age, p.y);
                        worldPos = HallBillboard(at, xy, 0.4 + 1.6 * age);
                        output.tint = float4(float3(0.85, 0.9, 0.95), 0.35 * (1.0 - age) * _HallWater.w);
                        output.glow = float4(0.0, 0.0, 0.0, 0.0);
                    }
                }
                else
                {
                    // ── A brine pool's mirror ──
                    int b = min(HALL_BRINE - 1, index);
                    float4 pool = _HallBrine[b];
                    if (pool.w <= 0.01 || which > 0u)
                    {
                        return Hidden();
                    }
                    worldPos = float3(pool.x + xy.x * pool.w, pool.y, pool.z + xy.y * pool.w);
                    output.tint = float4(float3(0.23, 0.1, 0.06), 0.92 * _HallWater.w);
                    output.glow = float4(pool.xyz, pool.w);
                }

                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // The light at a point (as FishCaveWaterLife's WaterLights): the sun where it reaches (never under the ground), the
            // additional lights through the cluster loop under Forward+ or the per-object list.
            void HallLights(float3 positionWS, float3 n, float3 v, float2 screenUV, float power, out float3 lit, out float3 specular, out float3 diffuse)
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
                float3 ambient = (float3)FishTrilight((half3)float3(0.0, 1.0, 0.0)) * (1.0 - 0.96 * under) + 0.004;
                int part = (int)round(input.quad.z);
                float3 lit, specular, diffuse;
                float r2 = dot(input.quad.xy, input.quad.xy);
                float3 rgb;
                float cover;
                if (part == 4)
                {
                    // A brinicle: a hollow tube of ice, bright at its rims, brine channels down it, its tip clear.
                    float across = abs(input.quad.x);
                    float rim = 0.35 + 0.65 * pow(across, 3.0);
                    float channels = 0.85 + 0.15 * sin(HALL_TURN * (input.quad.w * 7.0 + input.quad.x * 2.0));
                    float3 n = normalize(float3(input.quad.x, 0.0, 1.0 - across));
                    HallLights(input.positionWS, n, v, screenUV, 60.0, lit, specular, diffuse);
                    cover = input.tint.a * rim * (1.0 - 0.6 * input.quad.w);
                    rgb = input.tint.rgb * channels * (diffuse + ambient) + 0.4 * specular;
                }
                else if (part == 6)
                {
                    // A brine mirror: the dense brine's dark surface, a slow shimmer, the lights glinting off it.
                    if (r2 > 1.0)
                    {
                        discard;
                    }
                    float2 o = input.positionWS.xz - input.glow.xz;
                    float ripple = sin(HALL_TURN * (length(o) * 0.6 - _HallInfo.x / 20.0)) * 0.04;
                    float3 n = normalize(float3(o.x * ripple, 1.0, o.y * ripple));
                    HallLights(input.positionWS, n, v, screenUV, 200.0, lit, specular, diffuse);
                    float fresnel = pow(1.0 - saturate(abs(v.y)), 4.0);
                    cover = input.tint.a * (1.0 - smoothstep(0.85, 1.0, sqrt(r2)));
                    rgb = input.tint.rgb * (diffuse + ambient) * 0.6 + specular * 1.5 + fresnel * 0.08 * input.tint.rgb;
                }
                else if (part == 2)
                {
                    // A flange mirror: an inverted pool of the vent's own fluid, a silver shimmer and the plume's ember in it.
                    if (r2 > 1.0)
                    {
                        discard;
                    }
                    HallLights(input.positionWS, float3(0.0, -1.0, 0.0), v, screenUV, 120.0, lit, specular, diffuse);
                    cover = input.tint.a * (1.0 - smoothstep(0.7, 1.0, sqrt(r2))) * (0.5 + 0.5 * input.glow.a);
                    rgb = input.tint.rgb * (diffuse + ambient) * input.glow.a + specular + input.glow.rgb;
                }
                else if (part == 3)
                {
                    // A bead: a speck of the light round it, soft to its edge.
                    if (r2 > 1.0)
                    {
                        discard;
                    }
                    HallLights(input.positionWS, v, v, screenUV, 1.0, lit, specular, diffuse);
                    cover = (1.0 - r2) * input.quad.w * input.quad.w * input.tint.a;
                    rgb = input.tint.rgb * (1.2 * lit + ambient + 0.02);
                }
                else
                {
                    // A puff (a plume's, the ceiling's, a snap's): a soft round cloud lit by the lights in the hall, its ember its own.
                    if (r2 > 1.0)
                    {
                        discard;
                    }
                    float soft = (1.0 - r2) * (1.0 - r2);
                    HallLights(input.positionWS, v, v, screenUV, 1.0, lit, specular, diffuse);
                    cover = input.tint.a * soft;
                    rgb = input.tint.rgb * (0.6 * lit + ambient) + input.glow.rgb * soft;
                }
                if (cover <= 1e-4)
                {
                    discard;
                }
                rgb = MixFogColor(rgb, float3(0.0, 0.0, 0.0), input.fogCoord);
                // Premultiplied: what it covers by its cover, what it gives off added.
                return half4((half3)(rgb * cover), (half)saturate(cover));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
