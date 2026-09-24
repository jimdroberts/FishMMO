Shader "Hidden/FishMMO/Weather/Overlay"
{
    // What the weather does to the view itself (P5). Six treatments, because six kinds of weather
    // do six different things to a surface held in front of your eyes:
    //
    //   rain   beads that refract and run down
    //   snow   flecks that land, whiten and melt
    //   hail   hard brief impacts, no beading
    //   ash    greasy dark smears that build up and clear slowly
    //   sand   dry grain scouring across with the wind
    //   frost  crystals creeping in from the edges
    //
    // SHELTER GATES ALL OF IT. Every one fades as the viewer goes under cover, because the effect
    // is about being OUT in it. Rain running down the screen indoors is the single thing that makes
    // an overlay like this feel broken.
    //
    // WHAT IS FALLING HAS A MIX, AND A TRACE OF ONE TYPE IS NOT THAT TYPE. Rain and snow are
    // re-typed against the local temperature, so through the sleet band both weights are non-zero
    // at once — at freezing point the split is about three-quarters rain. Gating on "any rain at
    // all" put water beading on the lens through visibly falling snow. Each treatment asks for its
    // share to DOMINATE, and they hand over across the middle rather than both running.
    //
    // Everything is procedural: no texture to author or keep resident, and no tiling seam.
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
            float4 _FishWeatherMix2;        // x ash, y sand
            float4 _FishWeatherMisc;        // x aurora, y temperature, z shelter, w time
            float4 _FishWeatherWind;        // xy direction, z speed, w gust
            float4 _FishWeatherSubstance;   // rgb what is falling, a how harsh
            float4 _FishOverlayParams;      // x drops, y frost/snow, z dust, w aspect

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

            /* Screen space with square pixels. Every cell grid below works in THIS, not in raw uv:
             * a grid laid out in 0..1 is stretched by the aspect, so drops came out as ovals on a
             * wide screen and flecks as lozenges. Frost was already corrected; nothing else was. */
            float2 Square(float2 uv)
            {
                return float2(uv.x * _FishOverlayParams.w, uv.y);
            }

            /* One cell's drop, measured against a point given in THAT cell's local space.
             * Split out so a neighbouring cell can evaluate it too — see Drops(). */
            float3 DropInCell(float2 id, float2 local, float time, float amount, float size)
            {
                float2 random = Hash22(id);
                if (random.x > amount * 1.15)
                {
                    return float3(0.0, 0.0, 0.0);
                }
                float life = frac(time * (0.25 + random.x * 0.35) + random.y);

                float2 at = (random - 0.5) * 0.6;
                // Slides down as it ages. This is what takes it out of its own cell.
                at.y -= life * 0.5;
                float radius = (0.12 + random.y * 0.16) * (0.6 + size * 0.9);
                float age = smoothstep(0.0, 0.12, life) * (1.0 - smoothstep(0.65, 1.0, life));

                float d = length((local - at) * float2(1.0, 1.3));
                float mask = smoothstep(radius, radius * 0.45, d) * age;
                float2 bend = normalize(local - at + 1e-5) * mask;
                return float3(mask, bend);
            }

            /* Rain: beads that refract what is behind them and slide down as they age.
             *
             * EVALUATED ACROSS THE NEIGHBOURING CELLS, not just this pixel's own. A drop is placed
             * by the cell it was born in, but it is offset within that cell and then slides
             * downward out of it — its centre reaches -0.8 against a half-cell of 0.5, and its
             * radius can be 0.42 on top of that. A pixel that only asked its own cell therefore saw
             * nothing where a neighbour's drop had spilled over, and the drop was sliced along the
             * cell boundary: the "cut off" drops.
             *
             * Nine cells rather than the three the vertical slide alone would need, because the
             * horizontal offset overflows too on the larger drops. It is nine evaluations of cheap
             * scalar maths in a pass that only runs while it is actually raining.
             */
            float3 Drops(float2 uv, float time, float amount, float size)
            {
                float2 grid = float2(5.0, 5.0) * (0.7 + size * 0.8);
                float2 cell = Square(uv) * grid;
                float2 id = floor(cell);
                float2 within = frac(cell) - 0.5;

                float3 best = float3(0.0, 0.0, 0.0);
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbour = float2(x, y);
                        // This pixel, in the neighbour's local space.
                        float3 drop = DropInCell(id + neighbour, within - neighbour, time, amount, size);
                        // The nearest one wins outright rather than adding: two overlapping drops
                        // are two drops, and summing their masks makes a bright blob where they
                        // cross and doubles the refraction there.
                        best = drop.x > best.x ? drop : best;
                    }
                }
                return best;
            }

            // Snow: flecks that land, whiten and melt. No refraction — snow does not bead.
            float Flecks(float2 uv, float time, float amount)
            {
                float2 cell = Square(uv) * float2(13.0, 13.0);
                float2 id = floor(cell);
                float2 within = frac(cell) - 0.5;
                float2 random = Hash22(id);
                if (random.x > amount * 1.1)
                {
                    return 0.0;
                }
                float life = frac(time * (0.5 + random.x * 0.5) + random.y);
                float melt = smoothstep(0.0, 0.1, life) * (1.0 - smoothstep(0.4, 1.0, life));
                float d = length(within - (random - 0.5) * 0.5);
                return smoothstep(0.17, 0.04, d) * melt;
            }

            // Hail: hard, brief, and gone. A short white star rather than a lingering bead.
            float Impacts(float2 uv, float time, float amount)
            {
                float2 cell = Square(uv) * float2(7.0, 7.0);
                float2 id = floor(cell);
                float2 within = frac(cell) - 0.5;
                float2 random = Hash22(id + 31.7);
                if (random.x > amount * 0.9)
                {
                    return 0.0;
                }
                // Much faster than rain: a strike, not a drop.
                float life = frac(time * (2.2 + random.x) + random.y);
                float flash = 1.0 - smoothstep(0.0, 0.22, life);
                float2 d = abs(within - (random - 0.5) * 0.6);
                // A cross, which reads as a sharp hit where a disc reads as a drop.
                float star = max(smoothstep(0.10, 0.0, d.x) * smoothstep(0.02, 0.0, d.y),
                                 smoothstep(0.10, 0.0, d.y) * smoothstep(0.02, 0.0, d.x));
                return star * flash;
            }

            // Ash: greasy smears that build where they land and clear slowly.
            float Smears(float2 uv, float time, float amount)
            {
                float2 p = Square(uv) * 6.0;
                float n = 0.0, scale = 1.0, weight = 0.6;
                for (int i = 0; i < 3; i++)
                {
                    n += Hash21(floor(p * scale + float2(0.0, time * 0.02))) * weight;
                    scale *= 2.3;
                    weight *= 0.5;
                }
                return saturate(smoothstep(0.35, 0.85, n) * amount);
            }

            // Sand: dry grain driven across the view by the wind, not settling on it.
            float Scour(float2 uv, float time, float amount)
            {
                // Along the wind, so a sandstorm reads as coming FROM somewhere.
                float2 dir = normalize(_FishWeatherWind.xy + 1e-4);
                float along = dot(Square(uv), dir) * 40.0 - time * (6.0 + _FishWeatherWind.z * 2.0);
                float streak = frac(along * 0.5);
                float grain = Hash21(floor(Square(uv) * 180.0) + floor(time * 20.0));
                return saturate((smoothstep(0.85, 1.0, streak) * 0.6 + grain * 0.5) * amount);
            }

            // Frost: crystals thickening in from the edges when it is cold enough to hurt.
            float Frost(float2 uv, float time, float amount)
            {
                float2 centred = (uv - 0.5) * float2(_FishOverlayParams.w, 1.0);
                float edge = saturate(length(centred) * 1.35 - 0.25);
                float n = 0.0, scale = 6.0, weight = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    n += Hash21(floor(uv * scale)) * weight;
                    scale *= 2.1;
                    weight *= 0.5;
                }
                float grown = saturate(amount * 1.4 - 0.15 + sin(time * 0.05) * 0.02);
                return saturate(smoothstep(0.35, 0.9, n * edge + grown - 0.5)) * grown;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float exposure = saturate(1.0 - _FishWeatherMisc.z);
                float time = _FishWeatherMisc.w;
                float precip = _FishWeatherPrecip.x;

                float rainWeight = _FishWeatherMix.x;
                float snowWeight = _FishWeatherMix.y;
                float hailWeight = _FishWeatherMix.z;
                float frozenWeight = snowWeight + hailWeight;
                float total = max(1e-4, rainWeight + frozenWeight);
                float liquidShare = rainWeight / total;

                // Each asks for its own share to dominate; they hand over across the middle.
                float wet    = rainWeight * smoothstep(0.35, 0.70, liquidShare) * precip * exposure * _FishOverlayParams.x;
                float snowy  = snowWeight * smoothstep(0.35, 0.70, 1.0 - liquidShare) * precip * exposure * _FishOverlayParams.y;
                float hailly = hailWeight * smoothstep(0.35, 0.70, 1.0 - liquidShare) * precip * exposure * _FishOverlayParams.x;
                float ashy   = _FishWeatherMix2.x * precip * exposure * _FishOverlayParams.z;
                float sandy  = _FishWeatherMix2.y * precip * exposure * _FishOverlayParams.z;
                // Cold enough to frost is a temperature thing, not a precipitation thing: a clear
                // still night on an ice moon frosts a visor with nothing falling at all.
                float cold   = saturate(-_FishWeatherMisc.y - 0.25) * exposure * _FishOverlayParams.y;

                float2 offset = float2(0.0, 0.0);
                float3 tint = float3(0.0, 0.0, 0.0);
                float haze = 0.0;
                float3 substance = _FishWeatherSubstance.rgb;

                if (wet > 0.001)
                {
                    float3 drop = Drops(uv, time, saturate(wet), _FishWeatherPrecip.y);
                    offset += drop.yz * 0.035 * saturate(wet);
                    tint += substance * drop.x * 0.10;
                }
                if (snowy > 0.001)
                {
                    float fleck = Flecks(uv, time, saturate(snowy));
                    tint += substance * fleck * 0.35;
                    haze = max(haze, fleck * 0.30);
                }
                if (hailly > 0.001)
                {
                    // White, not the substance's colour: a hailstone hits hard enough to show as
                    // impact rather than as whatever it is made of.
                    tint += float3(0.9, 0.95, 1.0) * Impacts(uv, time, saturate(hailly)) * 0.5;
                }
                if (ashy > 0.001)
                {
                    float smear = Smears(uv, time, saturate(ashy));
                    tint += substance * smear * 0.10;
                    haze = max(haze, smear * 0.45);
                }
                if (sandy > 0.001)
                {
                    float scour = Scour(uv, time, saturate(sandy));
                    tint += substance * scour * 0.18;
                    haze = max(haze, scour * 0.38);
                }
                if (cold > 0.001)
                {
                    float frost = Frost(uv, time, saturate(cold));
                    offset += (Hash22(floor(uv * 90.0)) - 0.5) * frost * 0.012;
                    tint += float3(0.72, 0.82, 0.95) * frost * 0.45;
                    haze = max(haze, frost * 0.55);
                }

                /* The refraction is pulled back toward the middle at the screen's edge.
                 *
                 * Sampling at uv + offset walks off the frame there, and a clamped sampler answers
                 * with the border pixel smeared along the whole edge — the same class of mistake as
                 * a splash quad sinking through the ground: the effect runs past where its source
                 * data exists. Fading the offset out over the last few percent costs nothing and
                 * the drops simply stop bending right at the rim, where nobody is looking anyway. */
                float2 toEdge = min(uv, 1.0 - uv);
                float inside = smoothstep(0.0, 0.04, min(toEdge.x, toEdge.y));
                offset *= inside;

                float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv + offset));
                float3 color = scene.rgb * (1.0 - saturate(haze)) + tint;
                return float4(color, scene.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
