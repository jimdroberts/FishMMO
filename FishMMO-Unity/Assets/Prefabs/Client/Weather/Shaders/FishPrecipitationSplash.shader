Shader "FishMMO/Weather/Precipitation Splash"
{
    // Where the rain lands (P5). A RING spreading wherever there is standing water — the puddles the
    // ground's own shader draws, and calm open water — and, everywhere else, a GLINT: a faint soft
    // spurt flicked up from the impact and gone in a tenth of a second, which is what rain on a road
    // looks like from a few metres off. Subtle on purpose: every bolder version of this read as
    // something laid on the ground rather than as rain hitting it.
    //
    // GROUND COLLISION WITHOUT A SIMULATION. The sky occlusion map is already a top-down heightfield
    // of the highest surface around the camera — built from downward raycasts so rain stops at
    // roofs — and "the highest surface" is precisely where a falling drop ends up. So a splash does
    // not need a physics step or a particle to trace: it reads the landing height straight out of a
    // texture the vertex shader can already sample, on every target including WebGL2.
    //
    // That also gets roofs right for free. A drop over a barn lands on the barn, because that is
    // what the heightfield says is up there — no special case, and no splashes appearing on the
    // floor underneath it. And the sea: where the ground is under water the water is the highest
    // surface (FishSkyOcclusionHeight lays it over the map), so a drop lands on the sea, not on
    // the sand three metres below it.
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "FishPrecipitationSplash"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // Integer hashing wants shader model 4-class hardware: WebGL2 is, and so is everything else.
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // The ground's own weather (and FishWeather.hlsl through it), so a ring goes exactly where
            // the ground draws a puddle.
            #include "FishSurface.hlsl"

            float4 _FishSplashOrigin;   // xyz camera, w time
            float4 _FishSplashParams;   // x radius, y density 0..1, z widest ring across (m), w lifetime seconds
            float4 _FishSplashColor;    // rgb tint, a alpha
            float4 _FishSplashHeat;     // x dryness 0..1 (hot ground cuts a glint short), yzw unused
            float4 _FishSplashGrid;     // x cells along a side of the square round the camera, y metres a cell
            float4 _FishSplashGlint;    // x seconds a glint lasts, y its height (m), z its opacity, w a ring's opacity

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;   // x splash index, y unused
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 tint : TEXCOORD1;
                float4 splash : TEXCOORD2;  // x 1 for a glint, 0 for a ripple; y its time 0..1; z a glint's seed, a ripple's bearing to the camera; w a glint's height in pixels, a ripple's half-width in metres
            };

            /* A number in [0, 1) from a cell of the world and a salt, by integer arithmetic: a sin()
             * hash of world coordinates loses its precision a few kilometres out and repeats. */
            float SplashHash(int2 cell, int salt)
            {
                uint h = asuint(cell.x) * 73856093u ^ asuint(cell.y) * 19349663u ^ asuint(salt) * 83492791u;
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return float(h & 0xFFFFFFu) / 16777216.0;
            }

            /* Somewhere in a cell, new for each splash it makes and kept for the whole of that splash:
             * re-rolled every frame it would teleport across the ground instead of bursting and fading
             * where it landed. */
            float3 SplashPlace(int2 cell, int2 key, int salt, float cellMetres)
            {
                float2 random = float2(SplashHash(key, salt), SplashHash(key, salt + 1));
                return float3((cell.x + 0.1 + 0.8 * random.x) * cellMetres, 0.0,
                    (cell.y + 0.1 + 0.8 * random.y) * cellMetres);
            }

            /* Where a drop falling at this point ends up: x the height of the surface it hits (roof,
             * ledge, ground or the sea), y 1 when that surface is standing water, z 1 when it is the
             * open sea.
             *
             * Standing water is decided exactly as the ground's own shader decides a puddle
             * (FishWeatherSurface): wet enough, nearly level, and where the noise that stands in for
             * the ground's hollows lets water gather. So a drop rings the puddles you can see and
             * nowhere else. The slope is the height map's, over half a metre. */
            float3 SplashLanding(float3 at)
            {
                float ground = FishSkyOcclusionGround(at);
                float top = FishSkyOcclusionHeight(at);
                float onWater = step(ground + 0.01, top);
                const float SlopeStep = 0.5;
                float hx = FishSkyOcclusionGround(at + float3(SlopeStep, 0.0, 0.0)) - ground;
                float hz = FishSkyOcclusionGround(at + float3(0.0, 0.0, SlopeStep)) - ground;
                float level = smoothstep(0.93, 0.99, normalize(float3(-hx, SlopeStep, -hz)).y);
                float wet = saturate(FishCoverAt(float3(at.x, top, at.z)).y);
                float gather = FishSurfaceNoise(at.xz * 0.35);
                float puddle = saturate((wet * 1.6 - 0.45 - gather * 0.8) * 3.0) * level;
                return float3(top, max(onWater, step(0.35, puddle)), onWater);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float time = _FishSplashOrigin.w;
                float lifetime = max(0.05, _FishSplashParams.w);

                /* ANCHORED TO THE WORLD. Each splash is a cell of a grid laid on the ground, and the
                 * grid stays where it is: this quad is the cell at its index's place in the square of
                 * cells round the camera's own cell. It used to be put at the CAMERA plus a random
                 * offset it kept for its whole life, so every splash rode along with the camera and
                 * the whole field of rings slid over the ground whenever it moved. Now, as the camera
                 * crosses a cell, the column of cells behind it is handed to the front — and those are
                 * at the edge of the radius, faded to nothing, so none of them pops.
                 *
                 * TWO GRIDS, the same number of cells each. The first square of quads is laid a
                 * quarter the size over the few metres round the camera, the second over the whole
                 * radius. One grid across the radius put a splash to every two square metres, so a
                 * frame near the camera seldom caught one at all, and spent most of its quads far off
                 * where a splash is a pixel. */
                int index = (int)(input.data.x + 0.5);
                int side = max(1, (int)(_FishSplashGrid.x + 0.5));
                bool near = index < side * side;
                int local = near ? index : index - side * side;
                float scale = near ? 0.25 : 1.0;
                float cellMetres = max(0.05, _FishSplashGrid.y) * scale;
                float reach = max(1.0, _FishSplashParams.x) * scale;
                int2 home = int2(floor(_FishSplashOrigin.xz / cellMetres));
                int2 cell = home + int2(local % side, local / side) - side / 2;
                // What the hashes are keyed on. The near grid's cells are numbered like the far one's,
                // and without this a near cell would splash in step with the far cell of its number.
                int2 key = near ? cell + int2(104729, -7919) : cell;
                float stagger = SplashHash(key, 1) * 7.0;
                /* Thinned by how hard it is raining, HERE: the cloud above sheds the drops the
                 * particle field draws (FishPrecipitation), and a splash lands only where one of them
                 * could. Thinned by the scene's rain alone, the ground splashed on under a gap in the
                 * clouds where nothing was falling. */
                float density = _FishSplashParams.y * smoothstep(0.08, 0.35, FishCloudOver(float3(cell.x * cellMetres, 0.0, cell.y * cellMetres)));

                /* A RIPPLE if the drop lands in standing water. Decided at the ripple's own place,
                 * which holds for its whole life: decided anywhere that moved sooner, a ripple would
                 * blink out part-way whenever its cell straddled a puddle's edge. */
                float rippleTime = time / lifetime + stagger;
                int wave = (int)floor(rippleTime);
                float age = frac(rippleTime);
                float3 at = SplashPlace(cell, key, 3 + wave * 5, cellMetres);
                float3 landing = SplashLanding(at);
                bool ripple = landing.y > 0.5;
                float alive = step(SplashHash(key, 5 + wave * 5), density);
                if (!ripple)
                {
                    /* Otherwise a GLINT, on the glint's own shorter clock. A spurt is over in a tenth
                     * of a second, and on the ripple's clock each cell stood idle three quarters of the
                     * time. One that would land in water is not drawn: the water there is rippling. */
                    float glintTime = time / max(0.02, _FishSplashGlint.x) + stagger;
                    wave = (int)floor(glintTime);
                    age = frac(glintTime);
                    at = SplashPlace(cell, key, 1000003 + wave * 5, cellMetres);
                    landing = SplashLanding(at);
                    alive = step(SplashHash(key, 1000005 + wave * 5), density) * (1.0 - landing.y);
                }
                float top = landing.x;

                // The square of cells reaches past the radius at its corners; out there, nothing.
                float fromCamera = distance(at.xz, _FishSplashOrigin.xz);
                float edge = 1.0 - smoothstep(reach * 0.8, reach, fromCamera);
                alive *= step(fromCamera, reach);

                /* On the open sea a ring sits at the STILL level, and the waves carry the real
                 * surface away from it: above a trough it would hang in the air, and under a crest
                 * it would show through the water, which writes no depth to hide it. So rings on the
                 * sea only where it is calm enough for the still level to be where the surface is —
                 * a millpond, a sheltered cove. Rain on a rough sea is a hiss across the whole
                 * surface, not rings, anyway. */
                float calm = 1.0 - smoothstep(0.08, 0.3, _FishOcclusionWater.z);
                alive *= lerp(1.0, calm, landing.z);

                /* Lit like the drops that made it: the sky's light from above and a little of the
                 * main light, as FishPrecipitation lights them. It was the tint alone, unlit, so at
                 * night every splash glowed a moonlit blue at full daytime brightness on black
                 * ground. */
                Light mainLight = GetMainLight();
                half3 light = SampleSH(half3(0.0, 1.0, 0.0)) + mainLight.color * 0.35;

                /* Splashes that exist but are not wanted still cost a quad each, so they are
                 * collapsed to nothing and the rasteriser throws them away. */
                float3 world;
                float alpha;
                if (ripple)
                {
                    /* The quad is the whole reach of the ripple, the SIZE the profile gives, a
                     * little more or less from drop to drop, and stays that size: the crests travel
                     * out across it (Frag). It used to be the quad that grew, with one fat ring at its
                     * edge, which kept the ring the same thickness at every size and read as a
                     * sticker. Flat on the water and a hair above it. */
                    float size = 0.5 * _FishSplashParams.z * (0.7 + 0.6 * SplashHash(key, 6 + wave * 5)) * alive;
                    world = at + float3(input.positionOS.x, 0.0, input.positionOS.z) * size;
                    world.y = top + 0.012;
                    alpha = _FishSplashGlint.w * alive;
                    // z the way to the camera across the water, as an angle from world x toward z.
                    float2 toEye = _WorldSpaceCameraPos.xz - at.xz;
                    output.splash = float4(0.0, age, atan2(toEye.y, toEye.x + 1e-5), max(1e-3, size));
                }
                else
                {
                    /* A glint stands upright, turned to face the camera round the vertical, on the
                     * surface the drop hit.
                     *
                     * Hot ground dries a drop the moment it lands: the glint is cut short there rather
                     * than its timing changed, since every glint's phase comes from time over its
                     * lifetime and a changed lifetime would jump them all at once. */
                    float dry = saturate(_FishSplashHeat.x);
                    float moment = age / lerp(1.0, 0.5, dry);
                    float shown = step(moment, 1.0) * alive;
                    float height = max(0.01, _FishSplashGlint.y);
                    float3 toCamera = _WorldSpaceCameraPos - at;
                    float2 across = normalize(float2(toCamera.z, -toCamera.x) + float2(1e-5, 0.0));
                    float3 corner = at + float3(across.x, 0.0, across.y) * (input.positionOS.x * 0.5 * height);
                    corner.y = top + 0.004 + (input.positionOS.z * 0.5 + 0.5) * height;
                    world = lerp(at + float3(0.0, top, 0.0), corner, shown);
                    alpha = _FishSplashGlint.z * shown;
                    float pixel = 2.0 * max(0.1, length(toCamera)) / max(1e-3, abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                    output.splash = float4(1.0, saturate(moment), SplashHash(key, 6 + wave * 5), height / max(1e-4, pixel));
                }

                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.tint = float4(_FishSplashColor.rgb * light, alpha * edge);
                return output;
            }

            /* A glint, in its square quad's own frame: x 0..1 across, y 0..1 from the surface up. A
             * soft spurt thrown up from the impact, stretched as it rises and fading as it falls back,
             * and two faint droplets flicked out either side. Nothing in it is hard-edged: at a few
             * metres the whole thing is a handful of pixels, and a crisp shape there reads as a
             * sprite rather than as water. Never drawn thinner than a pixel and a half, and dimmer by
             * as much, so a far one is a faint flicker. */
            float GlintCover(float2 uv, float moment, float seed, float heightPixels)
            {
                float least = 1.5 / max(1.0, heightPixels);
                float rise = 4.0 * moment * (1.0 - moment);
                float2 centre = float2(0.5, 0.1 + 0.45 * rise);
                float2 sigma = float2(0.09, 0.08 + 0.16 * rise);
                float2 drawn = max(sigma, least);
                float2 d = (uv - centre) / drawn;
                float cover = exp(-dot(d, d)) * (sigma.x * sigma.y) / (drawn.x * drawn.y);

                float droplet = max(0.035, least);
                float dropletEnergy = (0.035 * 0.035) / (droplet * droplet);
                float spread = 0.25 + 0.15 * frac(seed * 7.13);
                UNITY_UNROLL
                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1.0 : 1.0;
                    float height = 0.1 + 0.55 * rise * (0.6 + 0.4 * frac(seed * (3.7 + i * 2.9)));
                    float2 q = (uv - float2(0.5 + side * spread * moment, height)) / droplet;
                    cover = max(cover, exp(-dot(q, q)) * dropletEnergy * 0.7);
                }
                return cover * (1.0 - smoothstep(0.5, 1.0, moment));
            }

            /* A ripple, from how far out a pixel is (0 the impact, 1 the quad's edge) and how much of
             * that one quad-width a pixel spans. Three crests a couple of centimetres apart run out
             * from the impact at about the speed real ones do, the ones behind the first weaker, all
             * of them fading as they spread. Each crest is a few millimetres across: never drawn
             * thinner than a pixel, and dimmer by as much, so a far ripple is a faint line rather
             * than a bright oval.
             *
             * Nor is a ripple seen from the side a ring of light. Its crests catch the sky where their
             * slopes tilt toward or away from the eye, on the near and far arcs, and hardly at all
             * where they tilt sideways: an overcast sky is the same all the way round, so a sideways
             * tilt reflects the same grey. A whole bright circle is what made them read as drawn on. */
            float RippleCover(float2 centred, float r, float pixel, float age, float halfWidth, float bearing)
            {
                float crest = 0.004 / halfWidth;
                float drawn = max(crest, pixel * 0.8);
                float energy = crest / drawn;
                float gap = 0.022 / halfWidth;
                float front = 0.12 + 0.83 * age;
                float cover = 0.0;
                UNITY_UNROLL
                for (int k = 0; k < 3; k++)
                {
                    float radius = front - k * gap;
                    float d = (r - radius) / drawn;
                    cover += exp(-d * d) * pow(0.55, k) * step(0.0, radius);
                }
                float fade = (1.0 - age) * (1.0 - age);
                float along = dot(centred, float2(cos(bearing), sin(bearing))) / max(r, 1e-4);
                float arcs = 0.25 + 0.75 * along * along;
                return saturate(cover * energy) * fade * arcs;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Out here, not in the branch: a derivative needs every pixel round it running the same code.
                float2 centred = input.uv * 2.0 - 1.0;
                float r = length(centred);
                float pixel = fwidth(r);
                float cover = input.splash.x > 0.5
                    ? GlintCover(input.uv, input.splash.y, input.splash.z, input.splash.w)
                    : RippleCover(centred, r, pixel, input.splash.y, input.splash.w, input.splash.z);
                return half4(input.tint.rgb, input.tint.a * cover);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
