// Sparks off the sites' fires and forges (SiteEmbers): a pre-built field of quads that the vertex shader
// hands out among the nearest fires and forges and moves on the shared motion clock. Each ember is born
// at the fire's heart, rises on the hot air (a campfire's a few metres, a forge's less), drifts with the
// wind, cools from yellow to a dull red and goes out. Nothing is simulated and nothing is stored.
// A lava tube's molten lava (kind 5, 2026-10-20) throws its embers up off the whole width of its body, higher and
// swirling in the convection over it; its fumes (kind 6) are soft puffs of sulphur haze drifting under the roof, lit
// orange from below and faded where they meet the rock.
Shader "FishMMO/World/Site Embers"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+46"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "SiteEmbers"
            Tags { "LightMode" = "UniversalForward" }
            // Light it gives: added, never covering.
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishWeather.hlsl"

            #define EMBER_SOURCES 16

            // Per draw (SiteEmbers): the fires, nearest first. xyz the fire's heart, w its kind (0 none, 1 fire, 2 forge,
            // 5 molten lava, 6 fumes under a lava tube's roof; 3 and 4 are the flames' alone).
            float4 _EmberSources[EMBER_SOURCES];
            // Per source: x how strong it burns (0..1, faded with distance), y the share of its embers alive; for lava z the
            // half-width of its body (m) and w the body (0 river, 1 lake, 2 fall); for fumes z their spread (m).
            float4 _EmberStrength[EMBER_SOURCES];
            float4 _EmberWind;   // xz the wind near the ground (m/s), y the night (0..1, embers show far better in the dark), w the sub-pixel dot's sigma (px)

            struct Attributes
            {
                float3 seed : POSITION;     // particle position in the unit box, shared by its 4 corners
                float2 corner : TEXCOORD0;  // 0/1 corner of the quad
                float4 random : TEXCOORD1;  // x show threshold, y size, z pace, w phase
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 quad : TEXCOORD0;    // xy the corner, -1..1; z pixels per unit of xy
                float4 spot : TEXCOORD1;    // x the core's sigma (px), y its area (px²), z 1 for a puff of fumes, w unused
                float3 light : TEXCOORD2;   // its light, already weighted
                float fogCoord : TEXCOORD3;
                float4 screen : TEXCOORD4;  // a puff's: xy w the screen position, z its eye depth
            };

            float EmberPixelMetres(float3 positionWS)
            {
                float focal = max(abs(UNITY_MATRIX_P._m11), 1e-4) * max(_ScreenParams.y, 1.0) * 0.5;
                float depth = max(-TransformWorldToView(positionWS).z, 1e-3);
                return unity_OrthoParams.w > 0.5 ? 1.0 / focal : depth / focal;
            }

            /* A puff of the sulphur fumes hanging under a lava tube's roof (kind 6): born somewhere over the fumes' spread, a
             * metre or so under their place, it drifts a few centimetres a second one way, swells and thins as it goes, and
             * fades in and out over half a minute; it shows by the lava's orange light under it, faintly. Pure in the clock. */
            Varyings FumesVert(Attributes input, float4 source, float4 strength)
            {
                Varyings output = (Varyings)0;
                float time = _FishWeatherMisc.w;
                float life = lerp(18.0, 34.0, input.random.z);
                float rate = FishWrapCycles(1.0 / life);
                float u = frac(time * rate + input.random.w);
                float age = u * life;
                float heading = 6.2831853 * frac(input.random.w * 7.31 + input.seed.x * 3.7);
                float2 disc = (float2(input.seed.y, input.seed.z) * 2.0 - 1.0) * strength.z;
                float2 way = float2(cos(heading), sin(heading)) * (0.05 + 0.05 * input.random.x) * age;
                float hang = -0.3 - 0.9 * input.random.y + 0.15 * sin(6.2831853 * u + 6.2831853 * input.random.x);
                float3 centre = source.xyz + float3(disc.x + way.x, hang, disc.y + way.y);
                float size = lerp(1.6, 3.4, input.random.y) * (0.75 + 0.5 * u);
                float shown = step(input.random.x, strength.y) * step(0.001, strength.x);
                // A puff the camera is in or right beside fades away first: it would fill the screen (overdraw) and show no edge.
                float3 toCamera = _WorldSpaceCameraPos.xyz - centre;
                float nearFade = saturate((length(toCamera) - 0.5 * size - 0.5) / 2.5);
                size *= shown * step(1e-3, nearFade);

                float3 view = toCamera / max(length(toCamera), 1e-3);
                float3 side = normalize(cross(float3(0.0, 1.0, 0.0), view) + float3(1e-4, 0.0, 0.0));
                float3 up = cross(view, side);
                float3 worldPos = centre + (side * (input.corner.x - 0.5) + up * (input.corner.y - 0.5)) * size;

                output.positionCS = TransformWorldToHClip(worldPos);
                output.quad = float3(input.corner * 2.0 - 1.0, 1.0);
                output.spot = float4(0.0, 0.0, 1.0, 0.0);
                // The haze's sulphur-yellow, lit from below by the lava's orange: a faint glow, in and out over its life.
                float fade = sin(3.14159265 * u);
                output.light = float3(0.55, 0.28, 0.1) * (0.045 * strength.x * fade * nearFade);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                float4 screen = ComputeScreenPos(output.positionCS);
                output.screen = float4(screen.xy, -TransformWorldToView(worldPos).z, screen.w);
                return output;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float time = _FishWeatherMisc.w;
                int source = min(EMBER_SOURCES - 1, (int)floor(input.seed.x * EMBER_SOURCES));
                float4 fire = _EmberSources[source];
                float4 strength = _EmberStrength[source];
                int kind = (int)round(fire.w);
                bool forge = kind == 2;
                bool lava = kind == 5;
                if (kind == 6)
                {
                    return FumesVert(input, fire, strength);
                }

                /* Its life: born, rising and cooling, gone, then born again. Each ember its own length (a
                 * campfire's spark lives one to three seconds; a forge's is a quicker spit), snapped so the
                 * motion clock's wrap holds whole lives. */
                bool fall = lava && strength.w > 1.5;
                float life = lava ? (fall ? lerp(0.8, 2.2, input.random.z) : lerp(1.6, 4.0, input.random.z))
                    : forge ? lerp(0.5, 1.4, input.random.z) : lerp(1.0, 3.0, input.random.z);
                float rate = FishWrapCycles(1.0 / life);
                float cycles = time * rate + input.random.w;
                float u = frac(cycles);
                float age = u * life;

                /* Where: born in the fire's heart, a little off its centre; carried up by the plume, which
                 * slows as it mixes (the climb's √ of age), downwind as the wind takes it, and swirling. */
                /* Over lava: born anywhere on the body's width (its light stands half a metre over the surface), carried
                 * far higher by the convection over 1100 °C rock and swirled wide in it; no wind reaches a tube. */
                float2 off = (float2(input.seed.y, input.seed.z) * 2.0 - 1.0) * (lava ? strength.z : forge ? 0.12 : 0.3);
                float climb = (lava ? (fall ? 2.2 : 3.2) : forge ? 1.3 : 2.4) * sqrt(age) * lerp(0.7, 1.3, input.random.y) + age * (lava ? 0.5 : 0.25);
                float swirlRate = FishWrapAngular(lava ? lerp(2.0, 4.5, input.random.x) : lerp(5.0, 9.0, input.random.x));
                float swirl = (lava ? 0.35 : 0.12) * age;
                float2 drift = (lava ? float2(0.0, 0.0) : _EmberWind.xz * age * 0.8)
                    + swirl * float2(sin(time * swirlRate + 6.2831853 * input.random.w), cos(time * swirlRate * 0.83 + 6.2831853 * input.random.y));
                float3 centre = fire.xyz + float3(off.x + drift.x, (lava ? -0.45 : 0.15) + climb, off.y + drift.y);

                /* Its glow: hot yellow at birth to a dull red as it cools, and faint at the end; it flickers
                 * as it tumbles. By day only the brightest are seen at all. */
                float cool = u;
                float3 colour = lerp(float3(1.0, 0.62, 0.22), float3(0.85, 0.18, 0.04), saturate(cool * 1.3));
                float flicker = 0.75 + 0.25 * sin(time * FishWrapAngular(lerp(14.0, 26.0, input.random.x)) + 6.2831853 * input.random.y);
                float fade = pow(1.0 - u, 1.6) * saturate(u * 20.0);
                // Underground no daylight outshines lava's sparks: its embers burn as at night.
                float radiance = (lava ? 12.0 : lerp(4.0, 14.0, _EmberWind.y)) * strength.x * fade * flicker;
                float shown = step(input.random.x, strength.y) * step(0.5, (float)kind);

                // A dot that keeps its light (AirMotes): its own size, never under the sub-pixel floor.
                float own = lerp(0.012, 0.03, input.random.y) * (forge || lava ? 0.8 : 1.0);
                float pixel = EmberPixelMetres(centre);
                float corePx = own / pixel;
                float floorSigma = max(_EmberWind.w, 0.25);
                float coreSigma = sqrt(corePx * corePx / (2.355 * 2.355) + floorSigma * floorSigma);
                float quadPx = 6.0 * coreSigma * shown * step(0.001, radiance);
                float size = quadPx * pixel;

                float3 toCamera = _WorldSpaceCameraPos.xyz - centre;
                float3 view = toCamera / max(length(toCamera), 1e-3);
                float3 side = normalize(cross(float3(0.0, 1.0, 0.0), view) + float3(1e-4, 0.0, 0.0));
                float3 up = cross(view, side);
                float3 worldPos = centre + (side * (input.corner.x - 0.5) + up * (input.corner.y - 0.5)) * size;

                output.positionCS = TransformWorldToHClip(worldPos);
                output.quad = float3(input.corner * 2.0 - 1.0, 0.5 * quadPx);
                output.spot = float4(coreSigma, 0.25 * PI * corePx * corePx, 0.0, 0.0);
                output.light = colour * radiance * FishWaterOpen(centre);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.spot.z > 0.5)
                {
                    // A puff: soft to its rim, and thinned where it meets the rock (the roof it hangs under).
                    float r2 = dot(input.quad.xy, input.quad.xy);
                    float puff = (1.0 - saturate(r2)) * (1.0 - saturate(r2));
                    float2 uv = input.screen.xy / max(input.screen.w, 1e-5);
                    float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float soft = saturate((scene - input.screen.z) / 1.2);
                    float cover = puff * soft;
                    if (cover < 1e-4)
                    {
                        discard;
                    }
                    float3 haze = MixFogColor(input.light, float3(0.0, 0.0, 0.0), input.fogCoord);
                    return half4((half3)(haze * cover), 0.0);
                }
                float d = length(input.quad.xy) * input.quad.z;
                float core = input.spot.y * exp(-d * d / (2.0 * input.spot.x * input.spot.x)) / (2.0 * PI * input.spot.x * input.spot.x);
                float cover = min(1.0, core);
                if (cover < 1e-4)
                {
                    discard;
                }
                float3 rgb = MixFogColor(input.light, float3(0.0, 0.0, 0.0), input.fogCoord);
                return half4((half3)(rgb * cover), 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
