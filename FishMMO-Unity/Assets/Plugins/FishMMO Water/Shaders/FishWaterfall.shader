Shader "FishMMO/Water/Waterfall"
{
    // A fall's water (InlandWaterRenderer.FallSheet): a glassy sheet bending over the lip, whitening as it takes in air,
    // breaking below its break-up length into ropes of white water with the rock seen between them. The shared half
    // (vertex motion, how white and solid the water is) is FishWaterfall.hlsl, so the shadow it casts is its own shape.
    Properties
    {
        _WaterColor("Water colour (glassy, at the lip)", Color) = (0.10, 0.27, 0.26, 1)
        _FoamColor("Aerated water colour", Color) = (0.93, 0.95, 0.96, 1)
        [NoScaleOffset] _FoamTexture("Streaks (set per fall: FallTextures)", 2D) = "black" {}
        [NoScaleOffset] _NormalMap("Ripples (set per fall: FallTextures)", 2D) = "bump" {}
        _StreakScale("Streak size (m)", Float) = 0.7
        _AerationMetres("Metres fallen to start whitening", Float) = 1.0
        _Smoothness("Smoothness", Range(0, 1)) = 0.86
        _Opacity("Opacity", Range(0, 1)) = 0.96
        _Breakup("How much a free curtain frays", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-98"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // After the lakes and rivers (Transparent-99), over the pool it falls into.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // A closed slab (its strips' front, back and open sides) and ropes wound to face the eye: outsides only.
            Cull Back

            HLSLPROGRAM
            #pragma vertex FallVertex
            #pragma fragment FallFragment
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _WATER_REFRACTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(_WATER_REFRACTION)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #endif
            #include "FishWaterFog.hlsl"
            #include "FishWaterfall.hlsl"

            Varyings FallVertex(Attributes input)
            {
                Varyings output = FallVertexCommon(input, false, float3(0.0, 1.0, 0.0));
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            /// Henyey-Greenstein, scaled to 1 for light scattered evenly: how much more light white water sends on
            /// toward the eye than to the side when the eye looks toward the sun through it.
            half ForwardScatter(half cosine, half g)
            {
                half g2 = g * g;
                return (1.0 - g2) / pow(max(1e-3, 1.0 + g2 - 2.0 * g * cosine), 1.5);
            }

            half4 FallFragment(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                if (_FishInlandCameraInSea > 0.5)
                {
                    discard;
                }
                float3 positionWS = input.positionWS;
                float3 view = _WorldSpaceCameraPos - positionWS;
                float distanceToCamera = max(1e-4, length(view));
                view /= distanceToCamera;
                bool rope = input.rope.y > 0.5;
                float3 normalWS = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                if (rope)
                {
                    // Round: its normal turns from the eye toward its sides across it.
                    half x = clamp(input.rope.x, -0.98, 0.98);
                    normalWS = normalize(view * sqrt(1.0 - x * x) + normalize(input.sideWS) * x);
                }
                FallSurface s = EvaluateFall(input, view, normalWS, distanceToCamera);

                // ── Ripples racing down it catch the light ────────────────────
                float rippleTime = _FishInlandTime / FISH_FALL_CYCLE;
                float ripplePhaseA = frac(rippleTime);
                float ripplePhaseB = frac(rippleTime + 0.5);
                half rippleBlend = abs(2.0 * ripplePhaseA - 1.0);
                float2 rippleScale = float2(_StreakScale * 0.8, _StreakScale * s.stretch * 0.6);
                float rippleTravel = s.speed * FISH_FALL_CYCLE / rippleScale.y;
                float2 rippleUVA = s.sheet / rippleScale + float2(0.0, rippleTravel * ripplePhaseA);
                float2 rippleUVB = s.sheet / rippleScale + float2(0.29, 0.71 + rippleTravel * ripplePhaseB);
                half3 ripple = UnpackNormalScale(lerp(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, rippleUVA),
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, rippleUVB), rippleBlend), 0.8 * (1.0 - s.white * 0.6));
                float3 tangentX = normalize(cross(float3(0.0, 1.0, 0.0), normalWS) + float3(1e-4, 0.0, 0.0));
                float3 tangentY = cross(normalWS, tangentX);
                float3 bumped = rope ? normalWS : normalize(normalWS + tangentX * ripple.x + tangentY * ripple.y);

                // ── Light ─────────────────────────────────────────────────────
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half3 lightColor = mainLight.color * mainLight.shadowAttenuation;
                half NdotL = dot(bumped, mainLight.direction);
                // The sky and the ground round it, from the ambient probe: a fall in a gorge lit by its walls, not one flat colour.
                half3 ambient = SampleSH(bumped);

                // Glassy water near the lip: the sky in it, and what is behind it through it.
                half NdotV = saturate(dot(bumped, view));
                half fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                float3 reflectVector = reflect(-view, bumped);
                // Falling water is rough: the sky in it is blurred and weak, never a mirror.
                half3 reflection = GlossyEnvironmentReflection(reflectVector, positionWS, max(0.25, 1.0 - _Smoothness), 1.0h) * 0.45;
                half3 body = _WaterColor.rgb * (lightColor * saturate(NdotL * 0.5 + 0.5) + ambient * 0.8);
                half glassAlpha = 0.4;
                #if defined(_WATER_REFRACTION)
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    half3 behind = SampleSceneColor(screenUV + bumped.xz * 0.02 / max(1.0, distanceToCamera * 0.1));
                    /* Water lets through what is behind it by how much of it there is: the whole river's depth rolling over
                     * the brink is green and deep, a thin sheet lower down nearly clear. */
                    half absorb = 1.0 - exp(-max(0.02, input.sheetDepth.x) * 2.2);
                    body = lerp(behind * lerp(half3(0.95, 0.99, 0.98), half3(0.72, 0.86, 0.85), absorb), body, lerp(0.12, 0.7, absorb));
                    glassAlpha = 1.0;
                #endif
                half3 halfVector = SafeNormalize(mainLight.direction + view);
                half glint = pow(saturate(dot(bumped, halfVector)), 180.0) * 3.0 * (1.0 - s.white);
                half3 glassy = lerp(body, reflection, fresnel) + lightColor * glint;

                /* Aerated water: white, lit on its face and through it. Looking toward the sun through a curtain the light
                 * scattered on through its bubbles makes it glow (forward scattering, g 0.6), more the thinner it is. */
                half wrap = saturate(NdotL * 0.5 + 0.5);
                half thickness = rope ? 2.0 * input.sheetDepth.x : max(0.02, input.sheetDepth.x);
                half through = ForwardScatter(dot(-view, mainLight.direction), 0.6) * 0.09 * exp(-thickness * 1.5);
                half3 foamLit = _FoamColor.rgb * (lightColor * (wrap * 0.8 + through) + ambient * 0.9 + 0.03);
                // Darker in the hollows between strands, so the sheet has depth; brightest at a jet's head.
                foamLit *= lerp(0.55, 1.1, s.strand) * lerp(1.0, lerp(0.85, 1.12, s.jet), s.broken);
                // Where it thins between jets the white takes the water's own colour: bubbles in green water, not chalk.
                foamLit *= lerp(saturate(_WaterColor.rgb * 3.2 + 0.25), half3(1.0, 1.0, 1.0), saturate(s.strand * 1.2));
                // Seen face on, light has come through more water than at a thin edge: a thick curtain's body is blue-grey.
                half faceOn = saturate(abs(dot(normalize(input.normalWS), view)));
                foamLit *= rope ? 1.0 : lerp(1.0, lerp(1.05, 0.82, faceOn), saturate(input.fall.y / 6.0) * (1.0 - saturate(input.flowShare.y) * 0.6));

                /* White water is still water: its white covers only part of what is behind it (most in a dense jet), the
                 * refracted rock and glassy body show through the rest, and it is wet, so the sky and the sun shine in it.
                 * Painted over the water as one opaque white it read as a texture, not water (Jim, 2026-10-08). */
                half cover = s.white * (rope ? 0.85 : lerp(0.5, 0.88, s.strand));
                half3 color = lerp(glassy, foamLit, cover);
                half sheen = pow(saturate(dot(bumped, halfVector)), 48.0) * 0.35;
                color += (reflection * fresnel * 0.6 + lightColor * sheen) * s.white;
                half alpha = s.alpha * lerp(glassAlpha, 1.0, cover);

                if (_FishFallDebug > 0.5)
                {
                    return half4(saturate(alpha * 2.0), saturate(input.fall.y / 5.0), rope ? 1.0 : saturate(input.sheetDepth.y), 1.0);
                }
                color = MixFog(color, input.fogFactor);
                float2 fogUV = input.screenPos.xy / input.screenPos.w;
                color = FishWaterBehindClouds(color, fogUV);
                if (_FishAirFogRange.z < 0.5)
                {
                    half fogKeep;
                    color = FishWaterAirFog(color, positionWS, fogUV, fogKeep);
                }
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }

        Pass
        {
            /* The shadow it casts: dithered by how much light the water stops. White water stops most of it; clear water at
             * the brink lets nearly all of it through, so it casts little. Its ropes face the light here, not the eye. */
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex FallShadowVertex
            #pragma fragment FallShadowFragment
            #pragma target 3.5
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWaterfall.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings FallShadowVertex(Attributes input)
            {
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightGuess = float3(0.0, 1.0, 0.0);
                #else
                    float3 lightGuess = _LightDirection;
                #endif
                Varyings output = FallVertexCommon(input, true, lightGuess);
                float3 positionWS = output.positionWS;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalize(output.normalWS), lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 FallShadowFragment(Varyings input) : SV_Target
            {
                float3 view = _LightDirection;
                float3 normalWS = normalize(input.normalWS);
                FallSurface s = EvaluateFall(input, view, normalWS, length(_WorldSpaceCameraPos - input.positionWS));
                half stops = s.alpha * lerp(0.15, 0.75, s.white);
                // Dithered against a fixed screen pattern; the shadow map's own filtering softens it.
                half noise = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
                clip(stops - noise);
                return 0;
            }
            ENDHLSL
        }
    }
}
