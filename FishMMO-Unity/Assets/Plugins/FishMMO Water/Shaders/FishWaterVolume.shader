// Everything the camera sees while it is under the sea, dimmed and tinted by the water in front of
// it. A full-screen pass drawn before the surface, so the sea bed, the rocks and anything swimming
// all fade into the deep at the same rate.
Shader "FishMMO/Water/Underwater"
{
    Properties
    {
        _UnderwaterTint ("Water colour", Color) = (0.05, 0.28, 0.36, 1)
        _UnderwaterDepth ("Visibility (m)", Range(1, 200)) = 22
        _UnderwaterDensity ("Murk", Range(0, 3)) = 1
    }

    SubShader
    {
        // Before the surface at Transparent-100, so the surface is drawn on top of its own volume
        // and a player looking up sees Snell's window through clear water rather than through fog.
        Tags { "Queue" = "Transparent-200" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

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
             * the OCEAN's UnityPerMaterial, and a shader cannot have two. The sea level is the one
             * thing needed from it, and that is a global rather than a material property, so it is
             * simply redeclared. */
            float _FishWaterLevel;

            CBUFFER_START(UnityPerMaterial)
                half4 _UnderwaterTint;
                half _UnderwaterDepth;
                half _UnderwaterDensity;
            CBUFFER_END

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

            half4 Frag(Varyings input) : SV_Target
            {
                float rawDepth = SampleSceneDepth(input.screenUV);
                /* OpenGL's clip space runs -1 to 1 in depth where the texture holds 0 to 1. Passed
                 * straight through, every point came back about twice as far away as it is, and
                 * the water took twice the fog it should: on the Linux editor, which is OpenGL
                 * Core, the visibility was half what the material says. FishWaterShore has the
                 * same fix. */
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float3 positionWS = ComputeWorldSpacePosition(input.screenUV, rawDepth, UNITY_MATRIX_I_VP);
                float3 toPoint = positionWS - _WorldSpaceCameraPos;
                float sceneDistance = length(toPoint);
                float3 direction = sceneDistance > 1e-4 ? toPoint / sceneDistance : float3(0, 0, 1);

                /* How far this ray travels THROUGH water, which is not how far it travels.
                 *
                 * Looking up, it leaves the water at the surface and everything past that — the
                 * sky, the sun — is in air and must not be fogged, or the sky goes green. Looking
                 * down or level it runs to whatever it hit. */
                float exitDistance = direction.y > 1e-4
                    ? (_FishWaterLevel - _WorldSpaceCameraPos.y) / direction.y
                    : 1e9;
                float path = min(sceneDistance, max(0.0, exitDistance));

                // Beer-Lambert. Visibility is the distance at which about a third of the light is
                // left, which is what a diver would call the visibility.
                float extinction = _UnderwaterDensity / max(1.0, _UnderwaterDepth);
                half transmittance = saturate(exp(-path * extinction));

                Light mainLight = GetMainLight();
                half sunHeight = saturate(mainLight.direction.y);
                half3 waterLight = mainLight.color * sunHeight * 0.9 + _GlossyEnvironmentColor.rgb * 0.8;

                /* Dimmer the deeper you are, because the light had to get down here too. Keyed to
                 * the camera rather than the pixel so the whole view darkens together as a diver
                 * descends, which is what it looks like from inside a head. */
                float depthBelow = max(0.0, _FishWaterLevel - _WorldSpaceCameraPos.y);
                half daylight = exp(-depthBelow * 0.45 / max(1.0, _UnderwaterDepth));

                half3 inscatter = _UnderwaterTint.rgb * waterLight * daylight * (1.0 - transmittance);

                return half4(inscatter, transmittance);
            }
            ENDHLSL
        }
    }
}
