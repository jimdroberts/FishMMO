Shader "Hidden/FishMMO/Weather/Overlay"
{
    // What the weather does to the view itself (P5). Six treatments, because six kinds of weather
    // do six different things to a surface held in front of your eyes:
    //
    //   rain   streaks that refract and run down
    //   snow   flakes that land, whiten and melt
    //   hail   hard brief impacts, no beading
    //   ash    dark flakes that stick, build up and clear slowly
    //   sand   grit scouring across with the wind
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
    // THE SHAPES ARE THE PRECIPITATION ATLAS'S OWN SPRITES — the sheet the falling particles are
    // drawn from — so what lands on the view is what is falling past it. Snow on the lens used to be
    // a procedural disc, which read as a white circle whatever was falling. Frost alone is still
    // procedural: nothing falls to make it.
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

            TEXTURE2D(_FishWeatherAtlas);
            SAMPLER(sampler_FishWeatherAtlas);

            // Four variants to a row, eight rows: 0 streak, 1 flake, 2 hail, 3 ash, 4 grit.
            static const float2 AtlasCell = float2(0.25, 0.125);
            // The streak is a line down the middle of its square, a tenth of the square's width at
            // half its peak (12 px of 128), so a streak N wide is drawn from a square ten N across.
            static const float StreakFill = 0.094;

            /* One sprite's alpha from the atlas, at `local` 0..1 across the sprite, y up.
             *
             * The gradients are passed in rather than taken from the uv, because the uv jumps at
             * every cell edge — the sprite is chosen per cell — and a mip picked across that jump is
             * the smallest one: a hairline of mush round every sprite. `blur` adds whole mips on top,
             * which is how a melting flake softens into water. */
            float AtlasSprite(float row, float variant, float2 local, float2 dx, float2 dy, float blur)
            {
                if (any(local < 0.0) || any(local > 1.0))
                {
                    return 0.0;
                }
                float2 uv = float2((variant + local.x) * AtlasCell.x, 1.0 - (row + 1.0 - local.y) * AtlasCell.y);
                float widen = exp2(blur);
                return SAMPLE_TEXTURE2D_GRAD(_FishWeatherAtlas, sampler_FishWeatherAtlas, uv,
                    dx * AtlasCell * widen, dy * AtlasCell * widen).a;
            }

            /* The sprite a cell holds, at this pixel. `within` is 0..1 across the cell and dpdx/dpdy
             * its change per pixel.
             *
             * KEPT WHOLLY INSIDE ITS OWN CELL. The sprite's centre never lies closer to the cell's
             * edge than half its size, and every sprite's content sits within the circle inscribed
             * in its square, so it survives any rotation — one cell answers for every pixel it
             * covers, and nothing is sliced off along a cell boundary. Rain is the exception, since
             * its drops run out of their cells, and searches its neighbours instead. */
            float CellSprite(float2 id, float2 within, float2 dpdx, float2 dpdy, float row, float size, float spin, float blur)
            {
                float2 centre = 0.5 + (Hash22(id + 3.3) - 0.5) * (1.0 - size);
                float s, c;
                sincos(spin, s, c);
                float2x2 turn = float2x2(c, -s, s, c);
                float2 local = mul(turn, within - centre) / size + 0.5;
                float variant = floor(Hash21(id + 5.7) * 4.0);
                return AtlasSprite(row, variant, local, mul(turn, dpdx) / size, mul(turn, dpdy) / size, blur);
            }

            /* One cell's drop, measured against a point given in THAT cell's local space (-0.5..0.5).
             * Split out so a neighbouring cell can evaluate it too — see Drops().
             *
             * The drop is the atlas's streak: short when it lands, drawing out into a trail as it
             * runs down, its top held where it struck. Bent like a thin cylinder of water — outward
             * from its own centre line — which is what makes a trail on glass read as water. */
            float3 DropInCell(float2 id, float2 local, float2 dpdx, float2 dpdy, float time, float amount, float size)
            {
                float2 random = Hash22(id);
                if (random.x > amount * 1.15)
                {
                    return float3(0.0, 0.0, 0.0);
                }
                float life = frac(time * (0.25 + random.x * 0.35) + random.y);
                float age = smoothstep(0.0, 0.12, life) * (1.0 - smoothstep(0.65, 1.0, life));

                float2 born = (random - 0.5) * 0.6;
                // Runs down as it ages. This is what takes it out of its own cell.
                float slide = life * 0.5;
                float width = (0.05 + random.y * 0.05) * (0.6 + size * 0.9);
                float trail = width * 3.0 + slide;
                float2 head = born - float2(0.0, slide);

                float square = width / StreakFill;
                float2 sprite = float2((local.x - head.x) / square + 0.5, (local.y - head.y) / trail);
                float2 dx = float2(dpdx.x / square, dpdx.y / trail);
                float2 dy = float2(dpdy.x / square, dpdy.y / trail);
                float mask = AtlasSprite(0.0, floor(random.y * 4.0), sprite, dx, dy, 0.0) * age;

                float across = clamp((local.x - head.x) / (width * 0.5), -1.0, 1.0);
                return float3(mask, across * mask, 0.0);
            }

            /* Rain: streaks that refract what is behind them and run down as they age.
             *
             * EVALUATED ACROSS THE NEIGHBOURING CELLS, not just this pixel's own. A drop is placed
             * by the cell it was born in, but it slides downward out of it and its trail reaches
             * above it — so a pixel that only asked its own cell saw nothing where a neighbour's
             * drop had spilled over, and the drop was sliced along the cell boundary.
             */
            float3 Drops(float2 uv, float time, float amount, float size)
            {
                float2 grid = float2(5.0, 5.0) * (0.7 + size * 0.8);
                float2 cell = Square(uv) * grid;
                // Taken here, before any branch: a derivative inside divergent flow is undefined.
                float2 dpdx = ddx(cell);
                float2 dpdy = ddy(cell);
                float2 id = floor(cell);
                float2 within = frac(cell) - 0.5;

                float3 best = float3(0.0, 0.0, 0.0);
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbour = float2(x, y);
                        float3 drop = DropInCell(id + neighbour, within - neighbour, dpdx, dpdy, time, amount, size);
                        // The nearest one wins outright rather than adding: two overlapping drops
                        // are two drops, and summing their masks makes a bright blob where they
                        // cross and doubles the refraction there.
                        best = drop.x > best.x ? drop : best;
                    }
                }
                return best;
            }

            // Snow: flakes that land, sit a moment, and melt — shrinking and softening into water
            // before they go. No refraction: snow does not bead.
            float Flecks(float2 uv, float time, float amount)
            {
                float2 cell = Square(uv) * 10.0;
                float2 dpdx = ddx(cell);
                float2 dpdy = ddy(cell);
                float2 id = floor(cell);
                float2 random = Hash22(id);
                if (random.x > amount * 1.1)
                {
                    return 0.0;
                }
                float life = frac(time * (0.16 + random.x * 0.18) + random.y);
                float landed = smoothstep(0.0, 0.04, life);
                float melt = smoothstep(0.3, 1.0, life);
                float size = lerp(0.35, 0.7, Hash21(id + 7.1)) * (1.0 - 0.4 * melt);
                float flake = CellSprite(id, frac(cell), dpdx, dpdy, 1.0, size, Hash21(id + 11.9) * 6.2831853, melt * 1.5);
                return flake * landed * (1.0 - melt * melt);
            }

            // Hail: hard, brief, and gone — a stone that strikes and bounces off, shrinking as it
            // leaves, not a lingering bead.
            float Impacts(float2 uv, float time, float amount)
            {
                float2 cell = Square(uv) * 7.0;
                float2 dpdx = ddx(cell);
                float2 dpdy = ddy(cell);
                float2 id = floor(cell);
                float2 random = Hash22(id + 31.7);
                if (random.x > amount * 0.9)
                {
                    return 0.0;
                }
                // Much faster than rain: a strike, not a drop.
                float life = frac(time * (2.2 + random.x) + random.y);
                float gone = smoothstep(0.0, 0.22, life);
                float size = lerp(0.25, 0.45, Hash21(id + 2.9)) * (1.0 - 0.5 * gone);
                return CellSprite(id + 31.7, frac(cell), dpdx, dpdy, 2.0, size, 0.0, 0.0) * (1.0 - gone);
            }

            // Ash: flakes that stick where they land and clear slowly — tens of seconds, where snow
            // melts in a few — softened a little, since ash smears rather than sitting crisp.
            float Smears(float2 uv, float time, float amount)
            {
                float2 cell = Square(uv) * 8.0;
                float2 dpdx = ddx(cell);
                float2 dpdy = ddy(cell);
                float2 id = floor(cell);
                float2 random = Hash22(id + 17.3);
                if (random.x > amount)
                {
                    return 0.0;
                }
                float life = frac(time * (0.03 + random.x * 0.04) + random.y);
                float stuck = smoothstep(0.0, 0.03, life) * (1.0 - smoothstep(0.55, 1.0, life));
                float size = lerp(0.45, 0.85, Hash21(id + 4.4));
                return CellSprite(id + 17.3, frac(cell), dpdx, dpdy, 3.0, size, Hash21(id + 8.8) * 6.2831853, 0.4) * stuck;
            }

            // Sand: grit driven across the view by the wind, not settling on it.
            float Scour(float2 uv, float time, float amount)
            {
                // Along the wind, so a sandstorm reads as coming FROM somewhere.
                float2 dir = normalize(_FishWeatherWind.xy + 1e-4);
                float2 cell = Square(uv) * 12.0 - dir * time * (4.0 + _FishWeatherWind.z * 6.0);
                float2 dpdx = ddx(cell);
                float2 dpdy = ddy(cell);
                float2 id = floor(cell);
                float2 random = Hash22(id + 57.1);
                float grit = 0.0;
                if (random.x <= amount)
                {
                    float size = lerp(0.55, 0.95, Hash21(id + 6.6));
                    grit = CellSprite(id + 57.1, frac(cell), dpdx, dpdy, 4.0, size, Hash21(id + 9.2) * 6.2831853, 0.0);
                }
                // The streaks stay, faint: they are the air moving rather than anything in it, and
                // they are what says the grit is being blown.
                float along = dot(Square(uv), dir) * 40.0 - time * (6.0 + _FishWeatherWind.z * 2.0);
                float streak = smoothstep(0.85, 1.0, frac(along * 0.5));
                return saturate((grit * 0.8 + streak * 0.35) * amount);
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
