Shader "Hidden/FishMMO/Weather/Overlay"
{
    // What the weather does to the view itself: drops caught on the lens, frost creeping in from
    // the edges, dust hazing everything over (P5).
    //
    // SHELTER IS THE WHOLE POINT. Every one of these fades out when the viewer is under cover,
    // because the effect is about being OUT in it. Rain streaking the screen while standing inside
    // a building is the single thing that makes an overlay like this feel broken, and it is the
    // easiest thing to get wrong — the weather is still falling outside, so the naive version keeps
    // drawing.
    //
    // Everything is procedural. No texture to author, load or keep in memory, and the drops do not
    // repeat on a tiling seam.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FishWeatherOverlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _FishWeatherPrecip;      // x amount, y drop size, z snow weight, w storm severity
            float4 _FishWeatherMix;         // x rain, y snow, z hail, w ash+sand
            float4 _FishWeatherMisc;        // x aurora, y temperature, z shelter, w time
            float4 _FishWeatherSubstance;   // rgb what is falling, a how harsh
            float4 _FishOverlayParams;      // x drops, y frost, z dust, w aspect

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Hash22(float2 p)
            {
                float n = Hash21(p);
                return float2(n, Hash21(p + n));
            }

            /* Drops caught on the lens. The screen is cut into cells; each holds one drop with its
             * own place, size and birth time, so they appear and fade independently rather than
             * pulsing together. Returns (mask, dx, dy) — the shape and the direction it bends light.
             */
            float3 Drops(float2 uv, float time, float amount, float size)
            {
                float2 grid = float2(9.0, 5.0) * (0.7 + size * 0.8);
                float2 cell = uv * grid;
                float2 id = floor(cell);
                float2 within = frac(cell) - 0.5;

                float2 random = Hash22(id);
                // Its own clock, so a hundred drops are not born on the same frame.
                float life = frac(time * (0.25 + random.x * 0.35) + random.y);
                // Rarer than one per cell when it is barely raining.
                if (random.x > amount * 1.15)
                {
                    return float3(0.0, 0.0, 0.0);
                }

                float2 at = (random - 0.5) * 0.6;
                // A drop slides down as it ages, and the trail is what reads as running water.
                at.y -= life * 0.5;
                float radius = (0.12 + random.y * 0.16) * (0.6 + size * 0.9);
                // Fades in, holds, fades out.
                float age = smoothstep(0.0, 0.12, life) * (1.0 - smoothstep(0.65, 1.0, life));

                float d = length((within - at) * float2(1.0, 1.3));
                float mask = smoothstep(radius, radius * 0.45, d) * age;
                // The surface normal of a little lens, for refraction.
                float2 bend = normalize(within - at + 1e-5) * mask;
                return float3(mask, bend);
            }

            // Frost: crystals thickening in from the edges when it is cold enough to hurt.
            float Frost(float2 uv, float time, float amount)
            {
                float2 centred = (uv - 0.5) * float2(_FishOverlayParams.w, 1.0);
                // Edges first — a visor frosts at its rim, where the warm air does not reach.
                float edge = saturate(length(centred) * 1.35 - 0.25);

                float n = 0.0, scale = 6.0, weight = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    n += Hash21(floor(uv * scale)) * weight;
                    scale *= 2.1;
                    weight *= 0.5;
                }
                // Grows very slowly; frost is not weather that flickers.
                float grown = saturate(amount * 1.4 - 0.15 + sin(time * 0.05) * 0.02);
                return saturate(smoothstep(0.35, 0.9, n * edge + grown - 0.5)) * grown;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                /* Out in it, or under cover. Everything below is scaled by this, so stepping inside
                 * clears the view instead of leaving rain running down a screen indoors. */
                float exposure = saturate(1.0 - _FishWeatherMisc.z);
                float time = _FishWeatherMisc.w;

                float precip = _FishWeatherPrecip.x;
                float wet = _FishWeatherMix.x * precip * exposure * _FishOverlayParams.x;
                float grit = _FishWeatherMix.w * precip * exposure * _FishOverlayParams.z;
                // Cold enough to frost is a temperature thing, not a precipitation thing: a clear
                // still night on an ice moon frosts a visor with nothing falling at all.
                float cold = saturate(-_FishWeatherMisc.y - 0.25) * exposure * _FishOverlayParams.y;

                float2 offset = float2(0.0, 0.0);
                float3 tint = float3(0.0, 0.0, 0.0);
                float haze = 0.0;

                if (wet > 0.001)
                {
                    float3 drop = Drops(uv, time, saturate(wet), _FishWeatherPrecip.y);
                    // Refraction: the drop bends what is behind it.
                    offset += drop.yz * 0.035 * saturate(wet);
                    // And catches a little of the light, in the colour of whatever is falling —
                    // acid rain streaks yellow, water rain does not.
                    tint += _FishWeatherSubstance.rgb * drop.x * 0.10;
                }

                if (cold > 0.001)
                {
                    float frost = Frost(uv, time, saturate(cold));
                    offset += (Hash22(floor(uv * 90.0)) - 0.5) * frost * 0.012;
                    tint += float3(0.72, 0.82, 0.95) * frost * 0.45;
                    haze = max(haze, frost * 0.55);
                }

                if (grit > 0.001)
                {
                    // Dust does not bend light, it dirties it: grain and a tint of the substance.
                    float grain = Hash21(floor(uv * 260.0) + floor(time * 14.0));
                    haze = max(haze, saturate(grit) * (0.28 + grain * 0.16));
                    tint += _FishWeatherSubstance.rgb * saturate(grit) * 0.16;
                }

                float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset);
                float3 color = scene.rgb * (1.0 - saturate(haze)) + tint;
                return float4(color, scene.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
