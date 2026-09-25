// Everything the camera sees while it is under the sea, dimmed and tinted by the water in front of
// it, with the things that make water look like water rather than like fog: daylight that fades as
// the view turns down into the deep, shafts of sun the waves focus through it, and the silt and
// plankton hanging in it. A full-screen pass, so the sea bed, the rocks, anything swimming and the
// surface overhead all fade into the deep at the same rate.
Shader "FishMMO/Water/Underwater"
{
    Properties
    {
        _UnderwaterTint ("Water colour", Color) = (0.05, 0.28, 0.36, 1)
        _UnderwaterDepth ("Visibility (m)", Range(1, 200)) = 22
        _UnderwaterDensity ("Murk", Range(0, 3)) = 1
        _Shafts ("Light shafts", Range(0, 2)) = 1
        _Motes ("Drifting matter", Range(0, 2)) = 1
    }

    SubShader
    {
        /* AFTER the surface (Transparent-100), so the surface seen from below is fogged by the water
         * between it and the eye like anything else down here. Drawn before it, the surface covered
         * this pass and stayed crisp at any distance — and took the shafts and the motes in front of
         * it with it, in exactly the direction a diver looks to see them. Snell's window through five
         * metres of clear water is still nearly clear; through twenty it is the hazy bright disc it
         * is in the sea. */
        Tags { "Queue" = "Transparent-90" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Underwater"
            Tags { "LightMode" = "UniversalForward" }

            // The project's own fog convention: rgb adds the light scattered toward the eye, alpha
            // carries how much of what is behind survives.
            Blend One SrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            /* No screen-space variant: URP turns it off before the transparent queue, and a march
             * needs the shadow at points in the water, which only the shadow map has. No soft
             * variant either — sixteen steps of one hardware-filtered tap is the softening. */
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            /* The name of this block is not cosmetic: Unity fills a constant buffer from the
             * material ONLY when it is called UnityPerMaterial. Called anything else — it was
             * UnityPerMaterial_Underwater — every property in it silently reads as ZERO, so the
             * density was 0, the transmittance was 1 and this whole pass did nothing at all while
             * looking for all the world like it was running.
             *
             * FishWater.hlsl is deliberately NOT included here for the same reason: it declares
             * the OCEAN's UnityPerMaterial, and a shader cannot have two. What is needed from the
             * sea is all globals, so they are simply redeclared. */
            TEXTURE2D(_FishWaterDerivatives1);
            TEXTURE2D(_FishWaterDerivatives2);
            float4 _FishWaterPatch;         // xyz the cascades' tile sizes in metres
            float _FishWaterLevel;          // the sea's surface now, tide included
            float _FishWaterCloudShadow;    // 1 under open sky, 0 under cloud
            float _FishWaterTime;           // the sea's clock: stops when the world's does
            float4 _FishWaterWind;          // xy toward, z speed (m/s)
            float _FishWaterGravity;

            CBUFFER_START(UnityPerMaterial)
                half4 _UnderwaterTint;
                half _UnderwaterDepth;
                half _UnderwaterDensity;
                half _Shafts;
                half _Motes;
            CBUFFER_END

            #include "FishWaterCausticsCommon.hlsl"

            // How far out the shafts are marched. Past it the sun's light is taken as its average,
            // and at the default visibility less than a sixth of anything is still seen that far.
            static const float ShaftReach = 40.0;
            static const int ShaftSteps = 16;
            // Metres of surface curvature each shaft sample is blurred over: about the march's
            // step a few metres out, where the shafts are looked at.
            static const float ShaftBlur = 0.4;

            // The mote grid: one mote at most to a cell, seen out to MoteReach.
            static const float MoteCell = 0.6;
            static const float MoteReach = 7.0;
            static const int MoteCells = 24;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Straight to clip space: the mesh is a full-screen triangle and must not be moved
                // by wherever its transform happens to be sitting.
                output.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                output.screenUV = input.positionOS.xy * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    output.screenUV.y = 1.0 - output.screenUV.y;
                #endif
                return output;
            }

            /* Interleaved gradient noise (Jimenez 2014): an offset per pixel that differs as much as
             * it can from its neighbours', so the march's slices break up into an even fine grain
             * instead of stepping across the view in bands. Not animated: with no temporal filter
             * to average it, a pattern that changes every frame is a shimmer, and one that does
             * not is invisible. */
            float PixelNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            // Four numbers from a cell. Dave Hoskins' hash without sine: sin() differs between
            // GPUs, and a sin() hash would put the motes somewhere else on every card.
            float4 CellHash(float3 cell)
            {
                float4 p = frac(cell.xyzx * float4(0.1031, 0.1030, 0.0973, 0.1099));
                p += dot(p, p.wzxy + 33.33);
                return frac((p.xxyz + p.yzzw) * p.zywx);
            }

            /* ∫ from a to b of σ·exp(-k·t) dt: the light a stretch of ray scatters toward the eye
             * when both the water in front of it and the depth the light came down through change
             * exponentially along it. The callers hold k at a tenth of the extinction at least, so
             * there is no 0/0 to guard. It could otherwise reach nothing looking up at a low sun,
             * whose beam brightens toward the surface as fast as the water in front hides it. */
            float3 Scattered(float extinction, float3 k, float a, float b)
            {
                return extinction * (exp(-k * a) - exp(-k * b)) / k;
            }

            /* Suspended matter: the silt, plankton and specks of everything that hang in every real
             * sea. They are the first thing a diver notices, because they are what the eye uses to
             * tell that it is moving at all — water without them reads as empty, or as fog.
             *
             * One mote at most to each cell of a world grid, found by walking the cells the view ray
             * crosses (Amanatides & Woo). Anchored in the WATER, not the screen: they slide past with
             * parallax as the camera moves, and the whole grid drifts with the current and sinks,
             * slowly, as marine snow does. A mote is drawn no smaller than about a pixel, and dimmed
             * by the same ratio, so a distant one fades out instead of sparkling on and off.
             *
             * Near the surface the water also SWAYS. Under a swell every parcel of water runs round a
             * circle as each wave passes, more than a metre across under a two-metre sea, shrinking with
             * depth as exp(-k·d) — the surge a diver is rocked by, and the motion that most says
             * "underwater" in any footage. The wave is the one the wind raises: Pierson-Moskowitz's
             * peak, ω = 0.877·g/U, with a significant height of 0.21·U²/g, the same sea the surface
             * above is built from. Two frequencies, so it is irregular like a real sea's.
             */
            half MoteCover(float3 origin, float3 direction, float limit, float pixelAngle, float extinction)
            {
                float2 downwind = _FishWaterWind.xy;
                float3 current = float3(downwind.x, 0.0, downwind.y) * (0.008 * _FishWaterWind.z)
                    + float3(0.0, -0.012, 0.0);

                float gravity = max(0.05, _FishWaterGravity);
                float wind = max(0.5, _FishWaterWind.z);
                float omega = 0.877 * gravity / wind;
                float orbit = 0.0735 * wind * wind / gravity                          // 0.35 of the significant height
                    * exp(-(omega * omega / gravity) * max(0.0, _FishWaterLevel - origin.y));
                float2 turn = float2(cos(omega * _FishWaterTime) + 0.5 * cos(1.23 * omega * _FishWaterTime + 1.7),
                    sin(omega * _FishWaterTime) + 0.5 * sin(1.23 * omega * _FishWaterTime + 1.7)) * (orbit / 1.5);
                float3 sway = float3(downwind.x * turn.x, turn.y, downwind.y * turn.x);

                float3 start = (origin - current * _FishWaterTime - sway) / MoteCell;
                float end = limit / MoteCell;

                float3 stride = float3(direction.x >= 0.0 ? 1.0 : -1.0, direction.y >= 0.0 ? 1.0 : -1.0,
                    direction.z >= 0.0 ? 1.0 : -1.0);
                float3 perCell = 1.0 / max(abs(direction), 1e-5);
                float3 cell = floor(start);
                // How far along the ray, in cells, the next boundary on each axis lies.
                float3 boundary = (cell + max(stride, 0.0) - start) * stride * perCell;

                half cover = 0.0;
                float entered = 0.0;
                for (int i = 0; i < MoteCells && entered < end; i++)
                {
                    float4 hash = CellHash(cell);
                    // Kept off the walls of its cell, so it is only ever seen from the cell it is in.
                    float3 mote = cell + 0.15 + 0.7 * hash.xyz;
                    float along = dot(mote - start, direction);
                    float miss = length(mote - (start + direction * along)) * MoteCell;
                    float distanceToMote = along * MoteCell;

                    float radius = 0.002 + 0.004 * hash.w * hash.w;      // two to six millimetres
                    float spread = max(radius, distanceToMote * pixelAngle);
                    /* About half the cells hold one. Faded in over the first half-metre: one at the
                     * lens would be a blot across a tenth of the screen, where a real one is too
                     * close to focus on and is not seen at all. */
                    float here = step(0.45, frac(hash.w * 11.3))
                        * smoothstep(0.15, 0.6, distanceToMote) * step(distanceToMote, limit)
                        * (1.0 - smoothstep(MoteReach * 0.6, MoteReach, distanceToMote));
                    cover += here * (radius * radius) / (spread * spread) * exp(-miss * miss / (spread * spread))
                        * exp(-extinction * distanceToMote);

                    // Into the next cell, across whichever boundary is nearest.
                    float3 nearest = step(boundary.xyz, boundary.yzx) * step(boundary.xyz, boundary.zxy);
                    entered = min(boundary.x, min(boundary.y, boundary.z));
                    cell += nearest * stride;
                    boundary += nearest * perCell;
                }
                return cover;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                /* FishWaterSceneWorldPosition converts OpenGL's depth first. Passed straight
                 * through, every point came back about twice as far away as it is and the water
                 * took twice the fog it should: on the Linux editor, which is OpenGL Core, the
                 * visibility was half what the material says. */
                float rawDepth = SampleSceneDepth(input.screenUV);
                float3 positionWS = FishWaterSceneWorldPosition(input.screenUV, rawDepth);
                float3 origin = _WorldSpaceCameraPos;
                float3 toPoint = positionWS - origin;
                float sceneDistance = length(toPoint);
                float3 direction = sceneDistance > 1e-4 ? toPoint / sceneDistance : float3(0, 0, 1);

                /* How far this ray travels THROUGH water, which is not how far it travels.
                 *
                 * Looking up, it leaves the water at the surface and everything past that — the
                 * sky, the sun — is in air and must not be fogged, or the sky goes green. Looking
                 * down or level it runs to whatever it hit. */
                float exitDistance = direction.y > 1e-4
                    ? (_FishWaterLevel - origin.y) / direction.y
                    : 1e9;
                float path = min(sceneDistance, max(0.0, exitDistance));

                // Beer-Lambert. Visibility is the distance at which about a third of the light is
                // left, which is what a diver would call the visibility.
                float extinction = max(1e-4, _UnderwaterDensity / max(1.0, _UnderwaterDepth));
                half transmittance = saturate(exp(-path * extinction));

                /* How the daylight dims on its way down, per colour: red first, the water's own
                 * colour last, which is why everything turns blue-green with depth. Its spread is
                 * read off the water's colour and scaled so the eye's average is the old single
                 * rate — 0.45 of the extinction, the daylight being scattered forward and kept in
                 * the beam rather than lost from it. */
                half3 tint = _UnderwaterTint.rgb;
                half3 spread = 1.0 / (tint / max(1e-3, max(tint.r, max(tint.g, tint.b))) + 0.35);
                spread /= max(1e-3, dot(spread, half3(0.2126, 0.7152, 0.0722)));
                float3 dimming = 0.45 * extinction * min(spread, 1.9);

                Light sun = GetMainLight();
                float3 toSun = sun.direction;
                half3 sunLight = sun.color * saturate(toSun.y) * 0.9 * saturate(_FishWaterCloudShadow);
                half3 skyLight = _GlossyEnvironmentColor.rgb * 0.8;
                float depthHere = max(0.0, _FishWaterLevel - origin.y);

                /* The sun's beam in the water: bent toward the vertical, so it comes down steeper
                 * than the sun stands and never lower than the critical angle, 48.6 degrees off it.
                 * Its light has come the long way down, so it dims faster with depth than the sky's. */
                float sinAir = length(toSun.xz);
                float sinWater = sinAir / FishWaterRefractiveIndex;
                float cosWater = sqrt(saturate(1.0 - sinWater * sinWater));
                float2 sunward = sinAir > 1e-4 ? toSun.xz / sinAir : float2(0.0, 0.0);
                float3 beam = float3(sunward.x * sinWater, cosWater, sunward.y * sinWater);
                float3 beamDimming = dimming / max(0.6, cosWater);

                /* Water scatters forward: looking toward the sun the water glows and the shafts
                 * stand out, looking away it is dimmer. Henyey-Greenstein, half and half with
                 * even scattering so the shafts still show looking across them, averaging to one
                 * over every direction so the water is no brighter or darker than it was. */
                const float g = 0.55;
                float cosScatter = dot(direction, beam);
                float phase = 0.45 + 0.55 * (1.0 - g * g) / pow(max(1e-4, 1.0 + g * g - 2.0 * g * cosScatter), 1.5);

                /* The sky's light comes down too — all of it through Snell's window overhead — and
                 * scatters forward like the sun's, so the same law holds for it about the vertical,
                 * broader. Without this, water scattered the sky's light back up at a diver looking
                 * down as brightly as it scattered it across to one looking level, and the deep
                 * below was no darker than the water ahead. Scaled so looking level is as bright
                 * as it was: down is about half that, up two to three times it. */
                const float gSky = 0.4;
                float skyPhase = (0.45 + 0.55 * (1.0 - gSky * gSky)
                    / pow(max(1e-4, 1.0 + gSky * gSky - 2.0 * gSky * direction.y), 1.5)) / 0.82;

                /* The light the water scatters toward the eye, integrated exactly along the ray: the
                 * water in front of each point hides it, and the depth the daylight had to come
                 * down through dims it. Looking down, each metre is deeper and darker, so the view
                 * falls away into the dark; looking up, it brightens toward the surface. The old
                 * pass took the depth at the camera for the whole view, which made every direction
                 * the same flat colour. */
                float3 skyK = max(extinction - dimming * direction.y, 0.1 * extinction);
                float3 skyIn = exp(-dimming * depthHere) * Scattered(extinction, skyK, 0.0, path);

                float marched = _Shafts > 0.0 ? min(path, ShaftReach) : 0.0;
                float3 sunK = max(extinction - beamDimming * direction.y, 0.1 * extinction);
                float3 sunIn = exp(-beamDimming * depthHere) * Scattered(extinction, sunK, marched, path);

                /* The shafts. Near the camera the sun's light is marched instead of averaged: at each
                 * step, how hard the waves overhead focus it there — the same lens the caustics on
                 * the sea bed are, along the same refracted ray, so a shaft IS the caustic, seen in
                 * the water it passes through on the way down — and whether anything stands between
                 * that point and the sun. A cliff, a hull or the player puts a shadow through the
                 * water, not just on the floor. The steps crowd toward the camera, where a shaft is
                 * sharp; they thin out where the water has already hidden most of it. */
                if (marched > 0.0)
                {
                    float jitter = PixelNoise(input.positionCS.xy);
                    float shaftDepth = 2.0 * max(1.0, _FishWaterCaustics.y);
                    float3 near = 0.0;
                    for (int i = 0; i < ShaftSteps; i++)
                    {
                        float a = (float)i / ShaftSteps;
                        float b = (float)(i + 1) / ShaftSteps;
                        float from = marched * a * a;
                        float to = marched * b * b;
                        float t = lerp(from, to, jitter);
                        float3 p = origin + direction * t;
                        float depth = max(0.0, _FishWaterLevel - p.y);

                        half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                        float focus = FishWaterShaftFocus(p, depth, toSun, ShaftBlur);
                        // None at the surface, where the lens has converged nothing yet, and fading
                        // with depth as the scattering spreads every beam.
                        float contrast = _Shafts * smoothstep(0.0, 1.0, depth) * exp(-depth / shaftDepth);
                        float light = max(0.0, 1.0 + (focus - 1.0) * contrast) * shadow;
                        near += exp(-extinction * t - beamDimming * depth) * (light * (to - from));
                    }
                    sunIn += extinction * near;
                }

                half3 inscatter = tint * (skyLight * skyPhase * skyIn + sunLight * phase * sunIn);

                // The motes, lit by the light where the camera is: backlit ones glint, as specks do.
                half3 motes = 0.0;
                if (_Motes > 0.0)
                {
                    float pixelAngle = 2.0 / max(1e-3, abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                    half cover = MoteCover(origin, direction, min(path, MoteReach), pixelAngle, extinction);
                    half shade = MainLightRealtimeShadow(TransformWorldToShadowCoord(origin + direction * min(path, 2.0)));
                    half3 lightHere = skyLight * skyPhase * exp(-dimming * depthHere)
                        + sunLight * phase * shade * exp(-beamDimming * depthHere);
                    motes = lightHere * (cover * 0.45 * _Motes);
                }

                return half4(inscatter + motes, transmittance);
            }
            ENDHLSL
        }
    }
}
