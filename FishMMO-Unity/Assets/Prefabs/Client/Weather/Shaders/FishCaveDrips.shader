// Water dripping in the caves (CaveDripField, 2026-10-20): the nearest drips' falling drops, the spatter where they land and the
// puddles they fall into, with the rings each drop sends over its puddle. A pre-built set of quads per drip that the vertex
// shader places from a few numbers a drip (CaveDripField: where its drops land, how far they fall, and the seconds to each of its
// next landings on the shared motion clock, SceneDrip), so every player sees the same drop land at the same moment and hears it
// (CaveDripPresenter). Nothing is simulated and nothing is stored.
//
// A puddle is its water's level: a flat disc a few centimetres over the floor where the drops land, and the rock that rises
// through that level covers it, so its edge is the floor's own shape; softened where the water thins to nothing (the scene's
// depth). What shows of it in a cave is what lights it: the torches and lanterns and the light carried in glint in it and
// shiver with its rings; near a mouth it holds a little of the sky. The drops are specks of the same light, streaked by their
// fall.
Shader "FishMMO/World/Cave Drips"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "CaveDrips"
            Tags { "LightMode" = "UniversalForward" }
            // Premultiplied: a puddle darkens the rock under it (alpha) and adds its glints; a drop only adds.
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishAmbient.hlsl"
            #include "FishGroundMap.hlsl"

            #define DRIP_SOURCES 24
            // SceneDrip's fall: gravity and a drop's terminal speed (m/s², m/s).
            #define DRIP_GRAVITY 9.81
            #define DRIP_TERMINAL 8.0
            // SceneDrip.RingSpeed (m/s) and SceneDrip.SpatterSeconds.
            #define DRIP_RING_SPEED 0.24
            #define DRIP_SPATTER_SECONDS 0.32

            // How many of the slots below hold a drip (CaveDripField); the rest are empty. The material's own, so the draw
            // stays in the SRP Batcher: the arrays are globals the system sets before each camera renders.
            CBUFFER_START(UnityPerMaterial)
            float _DripCount;
            CBUFFER_END

            // Per draw (CaveDripField), nearest first. xyz where the drops land (a puddle's surface, the floor, a stalagmite's tip),
            // w how far they fall (m).
            float4 _DripSources[DRIP_SOURCES];
            // x the puddle's radius (m, 0 none), y a drop's fall (s), z the drip's fade (0..1, with distance; 0 an empty slot),
            // w how long a ring shows (s).
            float4 _DripShape[DRIP_SOURCES];
            // Seconds to the landings of the drip's drops −1, 0, +1, +2 about its beat (negative: landed that long ago).
            float4 _DripTimes[DRIP_SOURCES];
            // x the number of drop 0 (mod 4096), y its landing (0 puddle, 1 rock, 2 a stalagmite's tip), zw unused.
            float4 _DripExtra[DRIP_SOURCES];

            struct Attributes
            {
                float3 slot : POSITION;     // x the drip, y the part (0 puddle, 1 drop, 2 spatter), z which of them
                float2 corner : TEXCOORD0;  // 0/1 corner of the quad
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                // xy the corner, -1..1; z the part; w a drop's share of its quad (sub-pixel drops spread their light thin).
                float4 quad : TEXCOORD1;
                // A puddle: xyz its centre, w its radius.
                nointerpolation float4 puddle : TEXCOORD2;
                // A puddle's rings: the ages of the last three landings (s; negative, none) and how long one shows.
                nointerpolation float4 rings : TEXCOORD3;
                float fogCoord : TEXCOORD4;
                // The fade with distance.
                nointerpolation float fade : TEXCOORD5;
            };

            // ── The fall (SceneDrip.FallenMetres, SpeedAt) ──

            float DripFallen(float seconds)
            {
                float x = DRIP_GRAVITY * max(0.0, seconds) / DRIP_TERMINAL;
                return DRIP_TERMINAL * DRIP_TERMINAL / DRIP_GRAVITY * (x + log(0.5 * (1.0 + exp(-2.0 * x))));
            }

            float DripSpeed(float seconds)
            {
                return DRIP_TERMINAL * tanh(DRIP_GRAVITY * max(0.0, seconds) / DRIP_TERMINAL);
            }

            float DripPixelMetres(float3 positionWS)
            {
                float focal = max(abs(UNITY_MATRIX_P._m11), 1e-4) * max(_ScreenParams.y, 1.0) * 0.5;
                float depth = max(-TransformWorldToView(positionWS).z, 1e-3);
                return unity_OrthoParams.w > 0.5 ? 1.0 / focal : depth / focal;
            }

            float DripHash(float a, float b)
            {
                return frac(sin(a * 12.9898 + b * 78.233) * 43758.5453);
            }

            // A thin streak along world up from `bottom` for `streak` metres, `width` across, turned to the camera.
            float3 DripStreak(float3 bottom, float streak, float width, float2 corner)
            {
                float3 toCamera = _WorldSpaceCameraPos.xyz - bottom;
                float3 side = cross(float3(0.0, 1.0, 0.0), toCamera);
                side = dot(side, side) > 1e-8 ? normalize(side) : float3(1.0, 0.0, 0.0);
                return bottom + side * ((corner.x - 0.5) * width) + float3(0.0, corner.y * streak, 0.0);
            }

            Varyings Hidden()
            {
                Varyings output = (Varyings)0;
                output.positionCS = float4(0.0, 0.0, -2.0, 1.0);
                return output;
            }

            Varyings Vert(Attributes input)
            {
                int index = min(DRIP_SOURCES - 1, (int)round(input.slot.x));
                int part = (int)round(input.slot.y);
                int which = (int)round(input.slot.z);
                float4 source = _DripSources[index];
                float4 shape = _DripShape[index];
                float4 times = _DripTimes[index];
                float4 extra = _DripExtra[index];
                if (index >= (int)_DripCount || shape.z <= 0.001)
                {
                    return Hidden();
                }

                Varyings output = (Varyings)0;
                output.fade = shape.z;
                output.puddle = float4(source.xyz, shape.x);
                output.quad.z = part;
                float3 worldPos;
                if (part == 0)
                {
                    // The puddle: a level disc at its water's surface.
                    if (shape.x <= 0.0)
                    {
                        return Hidden();
                    }
                    float2 xy = input.corner * 2.0 - 1.0;
                    worldPos = source.xyz + float3(xy.x * shape.x, 0.0, xy.y * shape.x);
                    output.quad.xy = xy;
                    output.rings = float4(-times.x, -times.y, -times.z, shape.w);
                }
                else if (part == 1)
                {
                    // A falling drop: drop `which` of the three due next (−1 has landed), between letting go and landing.
                    float due = which == 0 ? times.y : which == 1 ? times.z : times.w;
                    if (due <= 0.0 || due > shape.y)
                    {
                        return Hidden();
                    }
                    float fell = shape.y - due;
                    float height = max(0.0, source.w - DripFallen(fell));
                    float speed = DripSpeed(fell);
                    float3 at = source.xyz + float3(0.0, height, 0.0);
                    float pixel = DripPixelMetres(at);
                    const float diameter = 0.0045;
                    float width = max(diameter, 1.5 * pixel);
                    // Streaked over a sixtieth of a second of its fall, never past where it lands.
                    float streak = min(speed * 0.016 + diameter, height + diameter);
                    streak = max(streak, 1.5 * pixel);
                    worldPos = DripStreak(at, streak, width, input.corner);
                    output.quad.xy = float2(input.corner.x * 2.0 - 1.0, input.corner.y);
                    output.quad.w = diameter / width * saturate(diameter * 4.0 / streak + 0.25);
                }
                else
                {
                    // The spatter of the last drop to land: a few droplets thrown up and out, falling back within a third of a
                    // second; into a puddle the first is the jet that stands up where the drop went in.
                    float age = -1.0;
                    float number = extra.x;
                    if (-times.z >= 0.0 && -times.z < DRIP_SPATTER_SECONDS) { age = -times.z; number = extra.x + 1.0; }
                    else if (-times.y >= 0.0 && -times.y < DRIP_SPATTER_SECONDS) { age = -times.y; }
                    else if (-times.x >= 0.0 && -times.x < DRIP_SPATTER_SECONDS) { age = -times.x; number = extra.x - 1.0; }
                    if (age < 0.0)
                    {
                        return Hidden();
                    }
                    float h = DripHash(number + 17.0 * which, index * 0.37 + source.x * 0.11 + source.z * 0.07);
                    bool jet = which == 0 && extra.y < 0.5;
                    float up = jet ? lerp(0.5, 0.8, h) : lerp(0.6, 1.3, h);
                    float out_ = jet ? 0.0 : lerp(0.25, 0.7, frac(h * 7.13));
                    float turn = 6.2831853 * frac(h * 3.71 + which * 0.25);
                    float rise = up * age - 0.5 * DRIP_GRAVITY * age * age;
                    if (rise < 0.0)
                    {
                        return Hidden();
                    }
                    float3 at = source.xyz + float3(cos(turn) * out_ * age, rise, sin(turn) * out_ * age);
                    float pixel = DripPixelMetres(at);
                    const float diameter = 0.0025;
                    float width = max(diameter, 1.5 * pixel);
                    float streak = max(abs(up - DRIP_GRAVITY * age) * 0.016 + diameter, 1.5 * pixel);
                    worldPos = DripStreak(at - float3(0.0, 0.5 * streak, 0.0), streak, width, input.corner);
                    output.quad.xy = float2(input.corner.x * 2.0 - 1.0, input.corner.y);
                    output.quad.w = diameter / width * saturate(diameter * 4.0 / streak + 0.25) * (1.0 - age / DRIP_SPATTER_SECONDS);
                }
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // The light at a point other than the sky's: the sun where it reaches (out of the deep), and the additional lights
            // (torches, lanterns, crystals, the light carried in) through the cluster loop under Forward+ or the per-object list.
            // `specular` their glint off a surface of normal n seen from v (the puddle's), `lit` their light arriving.
            void DripLights(float3 positionWS, float3 n, float3 v, float2 screenUV, float power, out float3 lit, out float3 specular)
            {
                float under = (float)FishUnderground(positionWS);
                Light mainLight = GetMainLight();
                float3 sun = mainLight.color * mainLight.distanceAttenuation * (1.0 - under);
                lit = sun;
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
                    specular += arriving * pow(saturate(dot(n, normalize(light.direction + v))), power);
                }
#endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, positionWS);
                    float3 arriving = light.color * light.distanceAttenuation;
                    lit += arriving;
                    specular += arriving * pow(saturate(dot(n, normalize(light.direction + v))), power);
                LIGHT_LOOP_END
#endif
            }

            // The slope a puddle's rings give its surface at `dist` metres from where the drops land: each ring a short train
            // of capillary ripples running out at DRIP_RING_SPEED, fading as it spreads and as it ages.
            float DripRingSlope(float dist, float age, float life)
            {
                if (age < 0.0 || age >= life)
                {
                    return 0.0;
                }
                float radius = DRIP_RING_SPEED * age + 0.004;
                float off = dist - radius;
                const float wavelength = 0.018;
                float train = exp(-off * off / (2.0 * 0.011 * 0.011));
                float amplitude = 0.0035 * (1.0 - age / life) * exp(-age / 0.6) / sqrt(1.0 + dist / 0.03);
                return amplitude * (6.2831853 / wavelength) * cos(6.2831853 * off / wavelength) * train;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                if (input.quad.z < 0.5)
                {
                    // The puddle.
                    float2 off = input.positionWS.xz - input.puddle.xz;
                    float dist = length(off);
                    float radius = input.puddle.w;
                    if (dist > radius)
                    {
                        discard;
                    }
                    float2 out_ = dist > 1e-5 ? off / dist : float2(1.0, 0.0);
                    float slope = DripRingSlope(dist, input.rings.x, input.rings.w) + DripRingSlope(dist, input.rings.y, input.rings.w)
                        + DripRingSlope(dist, input.rings.z, input.rings.w);
                    float3 n = normalize(float3(-out_.x * slope, 1.0, -out_.y * slope));
                    // Where the water thins to nothing over the floor (the scene's depth under it), and towards its rim, it fades.
                    float scene = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float own = -TransformWorldToView(input.positionWS).z;
                    float deep = (scene - own) * max(abs(v.y), 0.05);
                    float edge = saturate(deep / 0.012) * (1.0 - smoothstep(0.7, 1.0, dist / radius));
                    if (edge <= 1e-3)
                    {
                        discard;
                    }
                    float facing = saturate(dot(n, v));
                    float fresnel = 0.02 + 0.98 * pow(1.0 - facing, 5.0);
                    float3 lit, specular;
                    // A near mirror: a light's glint is small and bright (normalised Blinn-Phong).
                    const float power = 900.0;
                    DripLights(input.positionWS, n, v, screenUV, power, lit, specular);
                    specular *= (power + 8.0) / 25.13 * max(fresnel, 0.04);
                    float under = (float)FishUnderground(input.positionWS);
                    float3 sky = (float3)FishTrilight((half3)reflect(-v, n)) * (1.0 - 0.96 * under) * fresnel;
                    // The lit rock round it, mirrored: about what a rock lit by the same lights sends back (albedo ~0.35 over pi).
                    float3 round_ = lit * 0.11 * fresnel;
                    float3 rgb = (specular + sky + round_) * edge * input.fade;
                    rgb = MixFogColor(rgb, float3(0.0, 0.0, 0.0), input.fogCoord);
                    // The water darkens the rock under it (it is already wet: a little more).
                    return half4((half3)rgb, (half)(0.32 * edge * input.fade));
                }
                // A drop or a droplet: a speck of the light round it, as a lens holds it (a bright glint of each light, a little of the
                // light coming off everything round it), soft across its streak.
                float across = input.quad.x;
                float cover = exp(-2.5 * across * across) * input.quad.w * input.fade;
                if (cover <= 1e-4)
                {
                    discard;
                }
                float3 lit, specular;
                DripLights(input.positionWS, v, v, screenUV, 1.0, lit, specular);
                float under = (float)FishUnderground(input.positionWS);
                float3 ambient = (float3)FishTrilight((half3)float3(0.0, 1.0, 0.0)) * (1.0 - 0.96 * under);
                float3 rgb = (2.0 * lit + 0.6 * ambient) * cover;
                rgb = MixFogColor(rgb, float3(0.0, 0.0, 0.0), input.fogCoord);
                return half4((half3)rgb, 0.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
