// Sparks off the sites' fires and forges (SiteEmbers): a pre-built field of quads that the vertex shader
// hands out among the nearest fires and forges and moves on the shared motion clock. Each ember is born
// at the fire's heart, rises on the hot air (a campfire's a few metres, a forge's less), drifts with the
// wind, cools from yellow to a dull red and goes out. Nothing is simulated and nothing is stored.
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
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishWeather.hlsl"

            #define EMBER_SOURCES 16

            // Per draw (SiteEmbers): the fires, nearest first. xyz the fire's heart, w its kind (0 none, 1 fire, 2 forge).
            float4 _EmberSources[EMBER_SOURCES];
            // Per source: x how strong it burns (0..1, faded with distance), y the share of its embers alive, zw unused.
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
                float4 spot : TEXCOORD1;    // x the core's sigma (px), y its area (px²), zw unused
                float3 light : TEXCOORD2;   // its light, already weighted
                float fogCoord : TEXCOORD3;
            };

            float EmberPixelMetres(float3 positionWS)
            {
                float focal = max(abs(UNITY_MATRIX_P._m11), 1e-4) * max(_ScreenParams.y, 1.0) * 0.5;
                float depth = max(-TransformWorldToView(positionWS).z, 1e-3);
                return unity_OrthoParams.w > 0.5 ? 1.0 / focal : depth / focal;
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

                /* Its life: born, rising and cooling, gone, then born again. Each ember its own length (a
                 * campfire's spark lives one to three seconds; a forge's is a quicker spit), snapped so the
                 * motion clock's wrap holds whole lives. */
                float life = forge ? lerp(0.5, 1.4, input.random.z) : lerp(1.0, 3.0, input.random.z);
                float rate = FishWrapCycles(1.0 / life);
                float cycles = time * rate + input.random.w;
                float u = frac(cycles);
                float age = u * life;

                /* Where: born in the fire's heart, a little off its centre; carried up by the plume, which
                 * slows as it mixes (the climb's √ of age), downwind as the wind takes it, and swirling. */
                float2 off = (float2(input.seed.y, input.seed.z) * 2.0 - 1.0) * (forge ? 0.12 : 0.3);
                float climb = (forge ? 1.3 : 2.4) * sqrt(age) * lerp(0.7, 1.3, input.random.y) + age * 0.25;
                float swirlRate = FishWrapAngular(lerp(5.0, 9.0, input.random.x));
                float swirl = 0.12 * age;
                float2 drift = _EmberWind.xz * age * 0.8
                    + swirl * float2(sin(time * swirlRate + 6.2831853 * input.random.w), cos(time * swirlRate * 0.83 + 6.2831853 * input.random.y));
                float3 centre = fire.xyz + float3(off.x + drift.x, 0.15 + climb, off.y + drift.y);

                /* Its glow: hot yellow at birth to a dull red as it cools, and faint at the end; it flickers
                 * as it tumbles. By day only the brightest are seen at all. */
                float cool = u;
                float3 colour = lerp(float3(1.0, 0.62, 0.22), float3(0.85, 0.18, 0.04), saturate(cool * 1.3));
                float flicker = 0.75 + 0.25 * sin(time * FishWrapAngular(lerp(14.0, 26.0, input.random.x)) + 6.2831853 * input.random.y);
                float fade = pow(1.0 - u, 1.6) * saturate(u * 20.0);
                float radiance = lerp(4.0, 14.0, _EmberWind.y) * strength.x * fade * flicker;
                float shown = step(input.random.x, strength.y) * step(0.5, (float)kind);

                // A dot that keeps its light (AirMotes): its own size, never under the sub-pixel floor.
                float own = lerp(0.012, 0.03, input.random.y) * (forge ? 0.8 : 1.0);
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
