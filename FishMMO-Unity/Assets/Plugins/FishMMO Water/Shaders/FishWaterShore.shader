// The shoreline: the sheet of water that runs up a beach and drains back, the foam it carries,
// and the wet sand it leaves behind.
//
// Drawn as a PROJECTED pass, not as geometry. It reads the depth buffer, reconstructs the world
// position of whatever is there, and shades it if it falls inside the swash band — so it conforms
// exactly to the terrain, and to rocks, piers and anything else standing in the surf, with no mesh
// to tessellate and nothing to keep in sync with the ground.
Shader "FishMMO/Water/Shore"
{
    Properties
    {
        _FoamColor ("Foam", Color) = (0.97, 0.99, 1.0, 1)
        _SwashColor ("Swash water", Color) = (0.42, 0.58, 0.56, 1)
        _WetColor ("Wet sand", Color) = (0.26, 0.20, 0.13, 1)
        _WetDistance ("Wet sand reaches (m)", Range(0, 40)) = 12
        _FoamScale ("Foam size (m)", Range(0.5, 40)) = 2.2
        _EdgeWobble ("Foam edge raggedness (m)", Range(0, 8)) = 3.0
        _SeawardReach ("Reaches into the water (m)", Range(0, 60)) = 18
        _FoamSharpness ("Foam sharpness", Range(0.01, 1)) = 0.22
        _EdgeFoam ("Foam at the edge", Range(0, 4)) = 2.6
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

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // Set by WaterSurface when the pipeline makes a depth texture; without one there is
            // no ground to project onto.
            #pragma multi_compile_fragment _ _WATER_DEPTH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishWaterShoreCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                half4 _SwashColor;
                half4 _WetColor;
                half _WetDistance;
                half _FoamScale;
                half _FoamSharpness;
                half _EdgeFoam;
                half _SwashOpacity;
                half _EdgeWobble;
                half _SeawardReach;
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

            float _FishWaterLevel;       // the sea's surface now, tide included
            float _FishWaterMeanLevel;   // the level the shore field was built against
            float _FishWaterShoreTexel;  // metres per texel of the shore field
            float4 _FishWaterWind;

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
                        return half4(0, 0, 0, 0);
                    }
                    float ndcDepth = rawDepth;
                #else
                    if (rawDepth >= 1.0 - 1e-7)
                    {
                        return half4(0, 0, 0, 0);
                    }
                    float ndcDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float3 positionWS = ComputeWorldSpacePosition(input.screenUV, ndcDepth, UNITY_MATRIX_I_VP);

                /* Only near the water's height. The band is laid out by horizontal distance, and
                 * horizontal distance alone would paint a cliff or a hill standing at the shore all
                 * the way up its face. Swash climbs a beach to about one and a half times the
                 * height of the waves — Hs is the reach over thirteen, as WaterShore sets it — and
                 * the wet band a little above that; under water, only the first metre or so of the
                 * inner surf belongs to the shore. Measured from the sea as it stands now, tide in. */
                float rise = positionWS.y - _FishWaterLevel;
                float runUp = max(0.75, _FishWaterSwashReach * 0.1);
                float ceiling = runUp * 1.6;
                float floorDepth = max(1.0, runUp);
                if (rise > ceiling || rise < -floorDepth)
                {
                    return half4(0, 0, 0, 0);
                }

                float2 shore = FishWaterShoreSample(positionWS.xz);
                if (shore.y > 990.0)
                {
                    return half4(0, 0, 0, 0);
                }

                /* The field's waterline is the MEAN one; the sea stands higher or lower with the
                 * tide, and the waterline moves by the rise over the slope of the beach. Left
                 * where it was, a metre of tide on an ordinary beach put the swash thirty metres
                 * from the water: drowned at high tide, stranded up the sand at low. The slope is
                 * taken over a few metres of the field's depth rather than at the texel, so the
                 * line moves as a line and not as every bump in the sand. */
                float slopeStep = max(4.0, _FishWaterShoreTexel * 4.0);
                float depthEast = FishWaterShoreSample(positionWS.xz + float2(slopeStep, 0.0)).x;
                float depthNorth = FishWaterShoreSample(positionWS.xz + float2(0.0, slopeStep)).x;
                float slope = max(0.01, length(float2(depthEast - shore.x, depthNorth - shore.x)) / slopeStep);
                float tide = _FishWaterLevel - _FishWaterMeanLevel;
                float edgeDistance = shore.y + clamp(tide / slope, -60.0, 60.0);

                /* The band STRADDLES the waterline, and it has to.
                 *
                 * Stopping at the water's edge left a dead strip: seaward of the line this pass
                 * refused to draw, and the ocean there is in a few centimetres of water where its
                 * own alpha has faded almost to nothing — so neither system owned the shallows and
                 * the beach and the sea did not appear to touch. The inner surf, where broken
                 * waves wash back and forth over the sand, belongs to the shore: it is the same
                 * sheet of water, and it is what joins the two.
                 */
                float landward = -edgeDistance;
                float band = max(_FishWaterSwashReach, _WetDistance);
                if (landward > band || edgeDistance > _SeawardReach)
                {
                    return half4(0, 0, 0, 0);
                }

                /* The foam mask is sampled FIRST, because the waterline itself is perturbed by it.
                 *
                 * A swash edge taken straight from the distance field is a perfect contour of that
                 * field — a smooth curve carrying the grid's own stair-steps, which is exactly what
                 * a real foam line never looks like. Wobbling the edge by a couple of metres of
                 * noise turns the contour into a ragged, fingered front, and costs one sample. */
                float2 foamUV = positionWS.xz / max(0.5, _FoamScale) - float2(0.0, _FishWaterShoreTime * 0.12);
                half mask = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV).r * 0.6
                    + SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV * 2.3 + 0.41).r * 0.4;
                half wobble = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture,
                    positionWS.xz / max(2.0, _FoamScale * 4.0)).r;

                float sheet, lip, highWater;
                FishWaterSwash(edgeDistance + (wobble - 0.5) * _EdgeWobble, sheet, lip, highWater);

                /* NO shadow attenuation here.
                 *
                 * This is a full-screen pass: its vertices are three clip-space corners, so the
                 * shadow coordinate derived for it is meaningless and comes back fully occluded.
                 * Measured, that took `lit` down to ambient alone and drew the entire beach band
                 * as a flat dark grey slab — which is what I kept mistaking for a missing foam
                 * term. The ground under this overlay is already shadowed correctly by its own
                 * shader; the band only has to tint what is there. */
                Light mainLight = GetMainLight();
                half3 lit = mainLight.color * saturate(mainLight.direction.y) * 0.7
                    + _GlossyEnvironmentColor.rgb * 0.6 + 0.12;

                /* Foam mask drifting SHOREWARD, because foam on a beach is carried by the water
                 * under it; a mask sliding any other way reads as a texture projected onto the
                 * sand rather than as something floating on the water. */

                /* Wet sand is left by the LAST SEVERAL waves, not by this one.
                 *
                 * Sand takes minutes to dry and waves arrive every few seconds, so at any instant
                 * the dark band reaches as far as the biggest recent run-up — which is why a real
                 * beach has a wide wet zone with a clear line at the top of it, and the swash
                 * moving about well inside that zone. Tying the wet band to the CURRENT wave
                 * instead made it a thin line that vanished whenever the water happened to be
                 * drained, which is most of the cycle. */
                half wetReach = max(_WetDistance, _FishWaterSwashReach * 0.8);
                /* Wobbled like the swash edge, and for the same reason: an unperturbed band is a
                 * contour of the distance field, with the grid's stair-steps along its top. */
                half wetLandward = landward + (wobble - 0.5) * _EdgeWobble * 0.8;
                half wet = saturate(1.0 - wetLandward / max(0.5, wetReach));
                // Darkest right at the water and fading up the beach, with a defined top edge.
                wet = wet * wet * smoothstep(1.0, 0.85, wetLandward / max(0.5, wetReach));
                // Sand under water is not "wet sand"; the sheet covers it instead.
                wet *= saturate(landward * 0.5 + 0.5);

                /* The lip carries the foam. The sheet behind it keeps a TRACE — cubed, and at a
                 * seventh of the weight it had.
                 *
                 * At 0.45 the sheet term alone cleared the contrast curve everywhere the water
                 * reached, so the whole band came out a uniform pale wash and the lip, which was
                 * computing perfectly all along, had nothing to stand out against. Every earlier
                 * attempt to find the missing white line was looking in the wrong place: the line
                 * was there, drowned by the surface it was supposed to be drawn on. */
                /* The mask is applied to the FINISHED foam, not to its input.
                 *
                 * Fed in before the contrast curve it does nothing at all where the lip term
                 * saturates — which is most of the lip — so the white came out perfectly uniform
                 * however the weighting was set. Applied after, it breaks the line into the
                 * patches and holes that foam actually is. */
                half foam = smoothstep(_FoamSharpness * 0.35, _FoamSharpness * 0.35 + _FoamSharpness,
                    saturate(lip * _EdgeFoam + sheet * sheet * sheet * 0.05));
                foam *= 0.42 + 0.58 * mask;

                /* The foam the swash LEFT here, from the memory WaterShore keeps. Thresholded against
                 * the mottle rather than scaled by it, so as a line fades it breaks into lace and
                 * then into scattered bubbles, the way stranded foam actually goes — scaled, it would
                 * only dim. Thinned where the next sheet is washing over it. */
                float2 fieldUV = (positionWS.xz - _FishWaterShoreRect.xy) / max(1.0, _FishWaterShoreRect.zw);
                half left = SAMPLE_TEXTURE2D_LOD(_FishWaterFoamMemory, sampler_FishWaterFoamMemory, fieldUV, 0).r;
                half stranded = smoothstep(0.12, 0.5, left * _Residual * (0.35 + 0.65 * mask)) * (1.0 - sheet * 0.6);
                foam = max(foam, stranded);

                half3 color = _WetColor.rgb * lit;
                half alpha = wet * 0.72;

                color = lerp(color, _SwashColor.rgb * lit, sheet);
                alpha = lerp(alpha, _SwashOpacity, sheet * sheet);

                color = lerp(color, _FoamColor.rgb * lit, foam);
                alpha = max(alpha, foam);

                /* Feathered at the landward edge of the band. A hard cut-off draws the reach as a
                 * visible arc across the sand — the contour of the field rather than anything that
                 * happens on a beach. Edges in ascending order: GLSL leaves smoothstep undefined
                 * when they are not, and this runs as GLSL on the Linux editor. */
                half fade = 1.0 - smoothstep(band * 0.55, band, landward);

                /* Handed over to the ocean as the water deepens. The two overlap through the
                 * shallows so there is no line where one stops and the other starts. */
                fade *= saturate(1.0 - edgeDistance / max(0.5, _SeawardReach));

                // Feathered at the height limits too, so neither shows as a level line on a slope.
                fade *= 1.0 - smoothstep(runUp, ceiling, rise);
                fade *= smoothstep(-floorDepth, -floorDepth * 0.5, rise);
                alpha = saturate(alpha * fade);

                /* THE SHEEN. A sheet of water a centimetre thick is nearly clear — what makes it read
                 * as water and not as a darker patch of sand is what it reflects: the sky, far more
                 * at a glancing angle than looking straight down, and a hard glint of the sun. More
                 * opacity only ever made it muddier sand.
                 *
                 * The surface is the beach's own slope, from the shore field, not the depth buffer's:
                 * water fills the small bumps in sand and lies smooth over them. The ocean's ripple map
                 * roughens it a little where the sheet is moving. The sun's glint is shadowed per
                 * pixel — from the reconstructed point, which is sound in a full-screen pass where a
                 * shadow coordinate from the vertices was not. */
                half3 sheen = 0.0;
                half gloss = saturate(sheet + wet * 0.35 * (1.0 - sheet)) * (1.0 - foam) * _Sheen;
                if (gloss > 0.001)
                {
                    float3 normalWS = normalize(float3((depthEast - shore.x) / slopeStep, 1.0, (depthNorth - shore.x) / slopeStep));
                    float2 rippleUV = positionWS.xz / 3.5 + _FishWaterWind.xy * (_FishWaterShoreTime * 0.05);
                    half3 ripple = UnpackNormalScale(SAMPLE_TEXTURE2D(_FishWaterNormalTexture, sampler_FishWaterNormalTexture, rippleUV), 0.35 * sheet);
                    normalWS = normalize(normalWS + float3(ripple.x, 0.0, ripple.y));

                    float3 viewWS = normalize(_WorldSpaceCameraPos - positionWS);
                    half fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normalWS, viewWS)), 5.0);
                    half3 sky = GlossyEnvironmentReflection(reflect(-viewWS, normalWS), 0.06, 1.0);

                    half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
                    float3 halfway = normalize(mainLight.direction + viewWS);
                    half glint = pow(saturate(dot(normalWS, halfway)), 600.0) * 24.0;
                    half sunFresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(halfway, viewWS)), 5.0);

                    sheen = (sky * fresnel + mainLight.color * glint * sunFresnel * shadow) * gloss * fade;
                }

                return half4(color * alpha + sheen, alpha);
                #endif
            }
            ENDHLSL
        }
    }
}
