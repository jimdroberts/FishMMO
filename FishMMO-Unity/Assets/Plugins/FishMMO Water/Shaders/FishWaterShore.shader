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
        _FoamSharpness ("Foam sharpness", Range(0.01, 1)) = 0.22
        _EdgeFoam ("Foam at the edge", Range(0, 4)) = 2.6
        _SwashOpacity ("Swash opacity", Range(0, 1)) = 0.20
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

            Blend SrcAlpha OneMinusSrcAlpha
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
                half _Debug;
            CBUFFER_END

            TEXTURE2D(_FoamTexture);
            SAMPLER(sampler_FoamTexture);

            float _FishWaterLevel;
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
                float rawDepth = SampleSceneDepth(input.screenUV);
                // Nothing there but sky.
                if (rawDepth <= 1e-7)
                {
                    return half4(0, 0, 0, 0);
                }
                float3 positionWS = ComputeWorldSpacePosition(input.screenUV, rawDepth, UNITY_MATRIX_I_VP);

                float2 shore = FishWaterShoreSample(positionWS.xz);
                float edgeDistance = shore.y;
                // Far from any shore, or off the field entirely.
                if (edgeDistance > 990.0 || edgeDistance > 0.5)
                {
                    // Positive distance is water; the ocean surface draws that, not this pass.
                    return half4(0, 0, 0, 0);
                }

                /* Only the band this pass owns: from the waterline up to as far as the swash can
                 * reach plus the wet sand it leaves. Everything further inland is dry beach and
                 * must be left exactly as the terrain drew it. */
                float landward = -edgeDistance;
                float band = max(_FishWaterSwashReach, _WetDistance);
                if (landward > band)
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
                half wet = saturate(1.0 - landward / max(0.5, wetReach));
                // Darkest right at the water and fading up the beach, with a defined top edge.
                wet = wet * wet * smoothstep(1.0, 0.85, landward / max(0.5, wetReach));

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

                half3 color = _WetColor.rgb * lit;
                half alpha = wet * 0.72;

                color = lerp(color, _SwashColor.rgb * lit, sheet);
                alpha = lerp(alpha, _SwashOpacity, sheet * sheet);

                color = lerp(color, _FoamColor.rgb * lit, foam);
                alpha = max(alpha, foam);

                /* Feathered at the landward edge of the band. A hard cut-off draws the reach as a
                 * visible arc across the sand — the contour of the field rather than anything that
                 * happens on a beach. */
                alpha *= smoothstep(band, band * 0.55, landward);

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
