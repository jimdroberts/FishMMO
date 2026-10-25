// The shoreline: the sheet of water that runs up a beach and drains back, the foam it carries,
// and the wet sand it leaves behind.
//
// Drawn as a PROJECTED pass, not as geometry. It reads the depth buffer, reconstructs the world
// position of whatever is there, and shades it if it falls inside the swash band — so it conforms
// exactly to the terrain, and to rocks, piers and anything else standing in the surf, with no mesh
// to tessellate and nothing to keep in sync with the ground. Floating hulls are the exception: they
// mark their pixels (stencil, below) and the pass leaves them to the sea.
Shader "FishMMO/Water/Shore"
{
    Properties
    {
        _FoamColor ("Foam", Color) = (0.97, 0.99, 1.0, 1)
        _SwashColor ("Swash water", Color) = (0.42, 0.58, 0.56, 1)
        _WetDarkening ("Wet sand: how much darker than dry", Range(0, 1)) = 0.5
        _WetAbove ("Wet above the highest run-up (share)", Range(0, 1)) = 0.15
        _FoamScale ("Foam size (m)", Range(0.5, 40)) = 2.2
        _EdgeWobble ("Edge raggedness (share of the run-up)", Range(0, 0.6)) = 0.25
        _Underwater ("Reaches under the water (share of the wave height)", Range(0, 2)) = 0.6
        _FoamSharpness ("Foam softness at its edges", Range(0.01, 1)) = 0.3
        _EdgeFoam ("Foam at the edge", Range(0, 4)) = 2.6
        _FoamOpacity ("Foam opacity at its thickest", Range(0, 1)) = 0.8
        _FoamVeil ("Thin foam between the froth", Range(0, 1)) = 0.22
        _SwashOpacity ("Swash opacity", Range(0, 1)) = 0.20
        _Sheen ("Wet sheen", Range(0, 2)) = 1
        _Residual ("Foam left on the sand", Range(0, 2)) = 1
        [NoScaleOffset] _FoamTexture ("Foam mask", 2D) = "white" {}
        [Header(Development)]
        _Debug ("Show terms (R sheet G lip B wet)", Range(0, 1)) = 0
    }

    SubShader
    {
        // Before the ocean at Transparent-100, so where the two overlap near the waterline the sea
        // is drawn over the beach rather than the other way round.
        Tags { "Queue" = "Transparent-150" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Shore"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied: the sheen is light ADDED over the sand, which straight alpha cannot say.
            Blend One OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off
            /* Not on a floating hull, inside or out: only where neither bit is set. Bit 128 is a hull's water lid
             * (FishHullLid.shader, across its open top): its planking is under the waterline, but no sea is in it. Bit 32
             * is the hull's own planking (FishHullShoreMark.shader, laid on by FloatingBoats): a boat at its waterline is
             * not a beach, and the swash, its foam and the wet sand drew a shoreline up every moored hull and passing ship.
             * Rocks, cliffs and pier piles carry no mark and keep their surf. */
            Stencil
            {
                Ref 0
                ReadMask 160
                Comp Equal
            }

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "Assets/Prefabs/Client/Weather/Shaders/FishShadowVariants.hlsl"
            #include_with_pragmas "Assets/Prefabs/Client/Weather/Shaders/FishLightCookieVariants.hlsl"
            // Set by WaterSurface when the pipeline makes a depth texture; without one there is
            // no ground to project onto.
            #pragma multi_compile_fragment _ _WATER_DEPTH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishWaterShoreCommon.hlsl"
            #include "FishWaterSun.hlsl"
            #include "FishWaterFog.hlsl"
            #include "FishWaterFoam.hlsl"
            #include "FishWaterFoamField.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                half4 _SwashColor;
                half _WetDarkening;
                half _WetAbove;
                half _FoamScale;
                half _FoamSharpness;
                half _EdgeFoam;
                half _FoamOpacity;
                half _FoamVeil;
                half _SwashOpacity;
                half _EdgeWobble;
                half _Underwater;
                half _Sheen;
                half _Residual;
                half _Debug;
            CBUFFER_END

            TEXTURE2D(_FoamTexture);
            SAMPLER(sampler_FoamTexture);
            // The ocean's own ripple map, published by WaterSurface, so the sheet ripples like the sea.
            TEXTURE2D(_FishWaterNormalTexture);
            SAMPLER(sampler_FishWaterNormalTexture);
            // The foam the swash has left on the sand, kept by WaterShore over the shore field.
            TEXTURE2D(_FishWaterFoamMemory);
            SAMPLER(sampler_FishWaterFoamMemory);

            /* How wet the rain has left the ground (the weather's cover map, g; WeatherCoverMap), as the ground's own
             * shaders read it: declared here rather than included, so the water keeps no dependency on the weather's
             * shaders. Never published (no weather), every figure is zero, and the ground counts as dry. */
            float4 _FishCoverRect;      // xy world x/z of the map's corner, zw size in metres
            float4 _FishCoverParams;    // x 1 when the map is valid
            float4 _FishWeatherCover;   // the scene-wide cover, where the map does not reach
            TEXTURE2D(_FishCoverTex);

            half RainWetness(float3 positionWS)
            {
                float2 uv = (positionWS.xz - _FishCoverRect.xy) / max(_FishCoverRect.zw, 1e-3);
                bool mapped = _FishCoverParams.x > 0.5 && all(uv >= 0.0) && all(uv <= 1.0);
                // Through a shared sampler: this pass is near the sampler limit.
                float4 cover = mapped ? SAMPLE_TEXTURE2D_LOD(_FishCoverTex, sampler_LinearClamp, uv, 0) : _FishWeatherCover;
                return (half)saturate(cover.y);
            }

            float _FishWaterLevel;       // the sea's surface now, tide included
            float _FishWaterMeanLevel;   // the level the shore field was built against
            float _FishWaterShoreTexel;  // metres per texel of the shore field

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Straight to clip space: a full-screen triangle that must not be moved by
                // whatever transform its object happens to have.
                output.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                output.screenUV = input.positionOS.xy * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    output.screenUV.y = 1.0 - output.screenUV.y;
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #if !defined(_WATER_DEPTH)
                    // Nothing here: not written at all (with this blend, (0, 0, 0, 0) leaves the frame as it was), and no further work.
                    discard;
                    return half4(0, 0, 0, 0);
                #else
                float rawDepth = SampleSceneDepth(input.screenUV);

                /* Where the far plane is, and what depth means, depend on the graphics API.
                 *
                 * A reversed depth buffer (Direct3D, Vulkan, Metal) puts the sky at 0; OpenGL puts
                 * it at 1, and its clip space runs -1 to 1 in depth where the texture holds 0 to 1.
                 * This pass was written for the first and run on the second — the editor on Linux
                 * is OpenGL Core — so the sky was never rejected and was shaded wherever the far
                 * plane happened to lie over the beach, and every other point was reconstructed
                 * at about TWICE its real distance along its ray. The band came out magnified about
                 * the camera, and a hill took its shading from whatever ground lay behind it: the
                 * swash climbing slopes and hanging in the air. FishWaterVolume has the same fix. */
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 1e-7)
                    {
                        discard;
                        return half4(0, 0, 0, 0);
                    }
                    float ndcDepth = rawDepth;
                #else
                    if (rawDepth >= 1.0 - 1e-7)
                    {
                        discard;
                        return half4(0, 0, 0, 0);
                    }
                    float ndcDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float3 positionWS = ComputeWorldSpacePosition(input.screenUV, ndcDepth, UNITY_MATRIX_I_VP);

                /* EVERYTHING HERE IS A HEIGHT above the sea as it stands now, tide included: the
                 * swash, its lip and the wet sand. How far up a beach water runs is a height — the
                 * run-up — and the ground it covers is that height over the slope: sixteen metres of
                 * a gentle beach, one metre of a steep bank. Laid out by horizontal distance, as the
                 * band first was, it stood metres up a steep face as a pale sheet; in height it
                 * follows the ground's own contours, as standing water does, and it follows the tide
                 * without being told, because it is measured from the water as it is. */
                float rise = positionWS.y - _FishWaterLevel;
                float waveHeight = max(0.05, _FishWaterSwashSea.x);
                /* The most anything here can reach: the largest run-up (twice the wave height), the
                 * biggest wave of a group, the wettest stretch of shore, and the wet sand above it.
                 * Under the water, the inner surf a fraction of the wave height down. */
                float highest = 2.0 * waveHeight * 1.35 * 1.3 * (1.0 + _WetAbove) + 0.05;
                float under = max(0.2, waveHeight * _Underwater);
                if (rise > highest || rise < -under)
                {
                    discard;
                    return half4(0, 0, 0, 0);
                }

                float2 shore = FishWaterShoreSample(positionWS.xz);
                if (shore.y > 990.0)
                {
                    discard;
                    return half4(0, 0, 0, 0);
                }

                /* The beach's own slope, over a few metres of the shore field rather than at a
                 * texel: the run-up is set by the shape of the beach, not by each bump in it. */
                float slopeStep = max(4.0, _FishWaterShoreTexel * 4.0);
                float depthEast = FishWaterShoreSample(positionWS.xz + float2(slopeStep, 0.0)).x;
                float depthNorth = FishWaterShoreSample(positionWS.xz + float2(0.0, slopeStep)).x;
                float2 gradient = float2(depthEast - shore.x, depthNorth - shore.x) / slopeStep;
                float slope = length(gradient);
                float runUp = FishWaterRunUpHere(slope, positionWS.xz, _FishWaterShoreTexel);
                float2 alongShore = FishWaterAlongShore(positionWS.xz);

                /* The foam mask is sampled FIRST, because the water's edge itself is perturbed by it.
                 * A front taken straight from the ground is a perfect contour of it — a level line
                 * drawn along the sand, which a foam line never is. Wobbling it by a share of the
                 * run-up turns the contour into a ragged, fingered front. */
                float2 foamUV = positionWS.xz / max(0.5, _FoamScale) - float2(0.0, _FishWaterShoreTime * 0.12);
                half mask = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV).r * 0.6
                    + SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV * 2.3 + 0.41).r * 0.4;
                half wobble = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture,
                    positionWS.xz / max(2.0, _FoamScale * 4.0)).r;
                float ragged = (wobble - 0.5) * _EdgeWobble * runUp;

                float sheet, lip, highWater;
                FishWaterSwash(rise + ragged, runUp, positionWS.xz, sheet, lip, highWater);

                /* A thin sheet is what a GENTLE beach carries: the bore spends itself running up the
                 * sand as a film. A steep face reflects — the water surges up it and falls back as a
                 * body, with no film — so there the sheet goes and its lip stays: the foam line riding
                 * up and down at the water's edge, over rock left wet below it. */
                sheet *= 1.0 - smoothstep(0.12, 0.35, slope);

                /* NO shadow attenuation on this diffuse tint: the ground under it is already shadowed
                 * by its own shader, and the band only has to tint what is there.
                 *
                 * And no floor of light of its own, as the sea has: the ground it tints has none, and goes as dark
                 * as the sky lets it. With one, a storm or a night left the swash and the wet sand a pale teal
                 * band along every shore, brighter than the beach it lay on. */
                Light mainLight = GetMainLight();
                // The sun through the cloud as the ground under it has it (FishWaterSun.hlsl): read bare, a rain deck lit the band by ten suns.
                half3 sunColor = mainLight.color * FishWaterCloudLight(positionWS, 1.0h);
                half3 lit = sunColor * saturate(mainLight.direction.y) * 0.7
                    + _GlossyEnvironmentColor.rgb * 0.6;

                /* Wet sand is left by the LAST SEVERAL waves, not by this one: sand takes minutes to
                 * dry and waves arrive every few seconds, so the dark band reaches as high as the
                 * biggest recent run-up on this stretch, with a clear line at its top and the swash
                 * moving about well inside it. */
                float wetLine = max(0.01, runUp * alongShore.y * 1.35 * (1.0 + _WetAbove) + ragged * 0.8);
                half wet = 1.0 - smoothstep(wetLine * 0.85, wetLine, rise);
                // Darkest right at the water, and lighter toward its top.
                wet *= lerp(1.0, 0.55, saturate(rise / wetLine));
                // Sand under the water is not wet sand: the sheet and the sea cover it.
                wet *= smoothstep(-0.05 - 0.1 * runUp, 0.0, rise);

                /* The lip carries the foam; the sheet behind it keeps only a trace, or the whole band
                 * clears the contrast curve and the lip has nothing to stand out against.
                 *
                 * As COVERAGE (FishWaterFoam.hlsl), cut from the lace in the foam map's alpha: the stronger
                 * the lip, the larger the share of it that is white, and the lace says what that much froth
                 * looks like — dense with round holes at the lip's crest, a web behind it, strands where it
                 * thins. Thresholding the blotch mask drew every lip as cotton wool with a milky veil laid
                 * between the clumps. */
                half strength = saturate(lip * _EdgeFoam + sheet * sheet * sheet * 0.05);
                half lipCover = smoothstep(0.02, 1.0, strength) * 0.9;

                /* The foam the swash LEFT here, from the memory WaterShore keeps: as a line fades it breaks
                 * into lace and then into strands, by itself. Thinned where the next sheet is washing over it. */
                float2 fieldUV = (positionWS.xz - _FishWaterShoreRect.xy) / max(1.0, _FishWaterShoreRect.zw);
                half left = SAMPLE_TEXTURE2D_LOD(_FishWaterFoamMemory, sampler_FishWaterFoamMemory, fieldUV, 0).r;
                half strandedCover = saturate(left * _Residual) * 0.6 * (1.0 - sheet * 0.6);
                /* Where the simulated foam runs (FishWaterFoamField.hlsl) it is the foam the swash left: the same lip
                 * lays it, and it lies as the sea's own foam does, ageing into lace. */
                FishFoamFieldSample carried = FishFoamFieldAt(positionWS.xz);
                strandedCover = lerp(strandedCover, FishFoamFieldCoverage(carried.amount) * 0.6 * (1.0 - sheet * 0.6), carried.known);
                half foamLace = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV).a;
                // The stranded foam's lace where the simulated foam keeps it (FishFoamFieldLace): it lies still with the foam.
                half carriedLaceShare;
                float carriedLod;
                half carriedLace = FishFoamFieldLace(TEXTURE2D_ARGS(_FoamTexture, sampler_FoamTexture), positionWS.xz, ddx(positionWS.xz), ddy(positionWS.xz),
                    max(0.5, _FoamScale), 0.0, carriedLaceShare, carriedLod);
                foamLace = FishFoamBlend(foamLace, FishFoamLaceRestore(carriedLace, carriedLod), carriedLaceShare * carried.known);
                FishFoam foamCut = FishFoamCut(foamLace, max(lipCover, strandedCover), length(fwidth(positionWS.xz)), _FoamScale);
                foamCut.body *= FishFoamFieldBody(carried);

                /* The FILM: where the sheet actually covers the sand. Thick down by the sea it does,
                 * everywhere; toward the top of its run it thins to a few millimetres and the sand's
                 * own ripples break through it, so it lies in patches and rivulets with bare wet sand
                 * between — the mottle decides which. Drawn as one unbroken sheet, its reflection was
                 * a solid silver band along every beach, which read as a line of foam. */
                half bubbles = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV * 5.1 + float2(0.73, 0.19)).r;
                half mottle = mask * 0.7 + bubbles * 0.3;
                half film = sheet * smoothstep(0.25, 0.6, sheet + (mottle - 0.32) * 0.9);

                /* Wet sand is the sand darker, not a colour laid over it: the band adds no light and takes a share of
                 * what the ground has, as the ground's own wetness does (FishDampen). It was a dark brown times this
                 * pass's guess at the light, at 72%: by day a wet band, but under a storm's deck or at night the guess
                 * fell to near nothing while the ground kept its own ambient, and the band drew a black line along
                 * every shore. And rain has already wetted the sand the swash would: on a soaked beach there is no
                 * line where the waves stop. */
                half3 color = 0.0h;
                half alpha = wet * _WetDarkening * (1.0h - RainWetness(positionWS));

                color = lerp(color, _SwashColor.rgb * lit, film);
                alpha = lerp(alpha, _SwashOpacity, film * film);

                /* The foam: lit as foam is (the sun wrapped round it, the sky as ambient, no light of its own),
                 * opaque where it is thick and lace that lets the sand through where it is thin. */
                half3 foamLit = FishFoamLight(_FoamColor.rgb, sunColor, saturate(mainLight.direction.y), 0.0h, _GlossyEnvironmentColor.rgb * 0.6);
                half foam;
                color = FishFoamOver(color, foamCut, foamLit, foam);
                foam *= _FoamOpacity;
                alpha = max(alpha, foam);

                // Feathered above the wet line, so it has no hard edge; handed over to the sea below.
                half fade = 1.0 - smoothstep(wetLine, wetLine * 1.15 + 0.03, rise);
                fade *= smoothstep(-under, -under * 0.5, rise);
                alpha = saturate(alpha * fade);

                if (_Debug > 0.5)
                {
                    return half4(sheet, lip, wet, 1.0);
                }

                /* THE SHEEN. A sheet of water a centimetre thick is nearly clear — what makes it read
                 * as water and not as a darker patch of sand is what it reflects: the sky, far more
                 * at a glancing angle than looking straight down, and a hard glint of the sun. More
                 * opacity only ever made it muddier sand.
                 *
                 * The surface is the beach's own slope, from the shore field, not the depth buffer's:
                 * water fills the small bumps in sand and lies smooth over them. The ocean's ripple map
                 * roughens it where the sheet is moving — enough that, at a glancing angle, some of it
                 * tilts to catch the bright sky and some the ground, and the reflection breaks into
                 * streaks rather than lying there as one mirror. The sun's glint is shadowed per
                 * pixel — from the reconstructed point, which is sound in a full-screen pass where a
                 * shadow coordinate from the vertices was not. */
                half3 sheen = 0.0;
                half gloss = saturate(film + wet * 0.35 * (1.0 - film)) * (1.0 - foam) * _Sheen;
                if (gloss > 0.001)
                {
                    float3 normalWS = normalize(float3(gradient.x, 1.0, gradient.y));
                    /* Scrolled with the sea's held wind, a whole number of tiles a window (FishWaterHeldScroll in
                     * FishWaterInput.hlsl): the eased wind times the clock scrubbed whenever the wind turned. */
                    float rippleWindow = max(1.0, _FishWaterRippleWindow.w);
                    float2 rippleUV = positionWS.xz / 3.5
                        + round(_FishWaterRippleWindow.xy * (0.05 * rippleWindow)) * (_FishWaterRippleWindow.z / rippleWindow);
                    half3 ripple = UnpackNormalScale(SAMPLE_TEXTURE2D(_FishWaterNormalTexture, sampler_FishWaterNormalTexture, rippleUV), 0.8 * film);
                    normalWS = normalize(normalWS + float3(ripple.x, 0.0, ripple.y));

                    /* The swash is not glass. The uprush is churned and the backwash rippled, and a rough
                     * surface reflects a BLURRED sky and less of it at a glancing angle — its grazing
                     * reflectance is capped at its smoothness (Schlick with roughness, the form URP's own
                     * lit shaders use). Drawn at 0.06, a mirror, the sheet threw back 60% of the bright
                     * horizon from eye height: a silver band down every beach that read as foam. Wet sand
                     * with no sheet over it is rougher still. */
                    float3 viewWS = normalize(_WorldSpaceCameraPos - positionWS);
                    half roughness = lerp(0.45, 0.28, film);
                    half grazing = max(1.0 - roughness, 0.02);
                    half fresnel = 0.02 + (grazing - 0.02) * pow(1.0 - saturate(dot(normalWS, viewWS)), 5.0);
                    half3 sky = GlossyEnvironmentReflection(reflect(-viewWS, normalWS), roughness, 1.0);

                    half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
                    float3 halfway = normalize(mainLight.direction + viewWS);
                    // Only where the sheet faces the light: a light at or under the horizon has no glint to give.
                    half glint = pow(saturate(dot(normalWS, halfway)), 600.0) * 24.0 * saturate(dot(normalWS, mainLight.direction) * 4.0);
                    half sunFresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(halfway, viewWS)), 5.0);

                    sheen = (sky * fresnel + sunColor * glint * sunFresnel * shadow) * gloss * fade;
                }

                /* Nothing to lay down — no band (alpha 0) and no sheen — writes (0, 0, 0, 0), which this blend turns
                 * into the frame as it was whatever the clouds and the fog in front say: so not their nine taps, and not
                 * the blend's read and write. Every gradient sample is above, so no neighbour loses one. */
                if (alpha <= 0.0h && all(sheen == 0.0h))
                {
                    discard;
                    return half4(0, 0, 0, 0);
                }

                /* Behind the weather's clouds and through its fog, both laid over the frame before
                 * this pass, in that order: the band's own colour is hidden as the ground under it is,
                 * and its sheen — light it adds — reaches the eye only as far as they let it. Without
                 * the clouds, the swash drew its lines on the beach straight through them from above. */
                float4 clouds = FishWaterCloudsInFront(input.screenUV);
                color = color * clouds.a + clouds.rgb;
                sheen *= clouds.a;
                half fogKeep;
                color = FishWaterAirFogOver(color, positionWS, input.screenUV, fogKeep);
                sheen *= fogKeep;

                return half4(color * alpha + sheen, alpha);
                #endif
            }
            ENDHLSL
        }
    }
}
