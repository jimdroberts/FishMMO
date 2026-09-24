// An ocean surface for URP: Gerstner waves in world metres, lit as water rather than as a tinted
// plane. Depth absorption, refraction and foam each degrade to something correct when the render
// pipeline does not provide the texture they need, because two of the project's three URP assets
// do not.
Shader "FishMMO/Water/Ocean"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow", Color) = (0.30, 0.62, 0.58, 1)
        _DeepColor ("Deep", Color) = (0.03, 0.20, 0.30, 1)
        _ScatterColor ("Sub-surface scatter", Color) = (0.12, 0.45, 0.35, 1)
        _FoamColor ("Foam", Color) = (0.95, 0.98, 1.0, 1)
        _ShoreColor ("Shore sediment", Color) = (0.52, 0.66, 0.52, 1)
        _ShoreDepth ("Sediment reaches (m)", Range(0, 30)) = 4.0
        _AbsorptionDepth ("Absorption depth (m)", Range(0.05, 40)) = 6.0

        [Header(Variation)]
        _Clarity ("Clarity varies by", Range(0, 0.9)) = 0.45
        _ClarityScale ("Clarity patch size (m)", Range(20, 4000)) = 420
        _GustStrength ("Wind patches", Range(0, 1)) = 0.55
        _GustScale ("Wind patch size (m)", Range(10, 2000)) = 160
        _ScatterStrength ("Scatter strength", Range(0, 3)) = 1.0

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0.5, 1)) = 0.97
        _SpecularStrength ("Sun glint", Range(0, 8)) = 2.0
        _MaxAlpha ("Maximum opacity", Range(0, 1)) = 1.0

        [Header(Ripples)]
        _DetailStrength ("Ripple strength", Range(0, 2)) = 0.7
        _DetailScale ("Ripple size", Range(0.25, 4)) = 1.0
        _DetailSpeed ("Ripple speed", Range(0, 2)) = 1.0
        _DetailFadeDistance ("Ripples fade by (m)", Range(20, 4000)) = 400

        [Header(Refraction)]
        _RefractionStrength ("Refraction (m)", Range(0, 2)) = 0.35

        [Header(Foam)]
        _FoamScale ("Foam size (m)", Range(0.5, 64)) = 6.0
        _FoamDepth ("Shore foam depth (m)", Range(0, 12)) = 0.5
        _FoamCrest ("White cap threshold", Range(0, 1)) = 0.70
        _FoamSharpness ("Foam sharpness", Range(0.01, 1)) = 0.3

        [Header(Shore)]
        _EdgeFade ("Shore softness (m)", Range(0.01, 10)) = 0.6
        _ShoreBreak ("Breaks at depth fraction", Range(0.3, 1.2)) = 0.62
        _ShoreSurf ("Surf foam", Range(0, 2)) = 1.0
        _SwashMetres ("Swash run-up (m)", Range(0, 3)) = 0.55
        _ShoreRefraction ("Waves turn to face shore", Range(0, 1)) = 0.8

        [Header(Distance)]
        _GroupDepth ("Wave group length", Range(2, 20)) = 7
        _WaveFadeStart ("Waves fade from (m)", Range(10, 8000)) = 600
        _WaveFadeEnd ("Waves flat past (m)", Range(20, 20000)) = 3000
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // Transparent-100: before everything that floats in or on the water, so spray, rain
        // splashes and the weather's own transparent effects sort in front of the surface.
        //
        // No ZWrite, and deliberately NO ShadowCaster pass. Water that casts a shadow darkens the
        // seabed it is supposed to be showing through, which the shader this replaces did.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            // Set from C# against what the running URP asset actually provides, so a pipeline with
            // no depth or no opaque copy never samples a texture that is not there. Sampling one
            // that URP has not bound does not error — it returns whatever was last in that slot,
            // which is how water ends up foaming in mid-air.
            //
            // GLOBAL keywords, not shader_feature_local: the alternative writes the answer into
            // the material asset, so opening the project on a machine whose quality tier differs
            // leaves a modified .mat in the working tree for no reason anybody can explain.
            #pragma multi_compile_fragment _ _WATER_DEPTH
            #pragma multi_compile_fragment _ _WATER_REFRACTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWater.hlsl"

            #if defined(_WATER_DEPTH) || defined(_WATER_REFRACTION)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif
            #if defined(_WATER_REFRACTION)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                // The point on the FLAT sea this vertex came from. The fragment re-evaluates the
                // wave sum here, so the normal is per pixel rather than interpolated.
                float2 flatXZ : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                // x amplitude fade, y distance to camera, z fog
                float3 surface : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 flatWS = TransformObjectToWorld(input.positionOS.xyz);
                // The still-water plane is a global, not the mesh's own height: the mesh is moved
                // around under the camera every frame and must not carry the sea level with it.
                flatWS.y = _FishWaterLevel;

                float distanceToCamera = distance(flatWS, _WorldSpaceCameraPos);
                float fade = FishWaterAmplitudeFade(distanceToCamera);
                FishWaterSurface surface = FishWaterDisplace(flatWS, fade, 0.0);

                output.positionWS = surface.positionWS;
                output.flatXZ = flatWS.xz;
                output.positionCS = TransformWorldToHClip(surface.positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.surface = float3(fade, distanceToCamera, ComputeFogFactor(output.positionCS.z));
                return output;
            }

            /// Eye-space depth of a raw depth sample, correct under an orthographic camera too.
            float FishWaterEyeDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    float ortho = lerp(_ProjectionParams.z, _ProjectionParams.y, rawDepth);
                #else
                    float ortho = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                #endif
                return lerp(LinearEyeDepth(rawDepth, _ZBufferParams), ortho, unity_OrthoParams.w);
            }

            /// <summary>
            /// The water's own colour: shallow to deep by how much light the column has eaten, and
            /// dirtier still in the last few metres before the beach.
            /// </summary>
            /// <remarks>
            /// The sediment term is separate from the absorption on purpose. Absorption alone only
            /// makes shallow water a paler version of deep water, and real shallows are not paler
            /// blue — they are GREENER and browner, because a shore stirs sand and silt into the
            /// water that is not there half a kilometre out. Squared, so it stays in the surf zone
            /// instead of washing the whole bay.
            /// </remarks>
            half3 FishWaterBody(float absorbed, float waterColumn)
            {
                half3 open = lerp(_ShallowColor.rgb, _DeepColor.rgb, absorbed);
                half sediment = 1.0 - saturate(waterColumn / max(0.05, _ShoreDepth));
                return lerp(open, _ShoreColor.rgb, sediment * sediment);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float surfaceEye = input.screenPos.w;
                float distanceToCamera = input.surface.y;

                float3 view = _WorldSpaceCameraPos - input.positionWS;
                float viewLength = max(1e-4, length(view));
                view /= viewLength;

                // ── Normal ────────────────────────────────────────────
                //
                // Detail fades out with distance for the same reason the waves do: past a few
                // hundred metres its ripples are smaller than a pixel, and a normal map sampled
                // below its Nyquist limit turns the specular highlight into a field of crawling
                // sparkles. The lost detail is put back as roughness, which is what those ripples
                // do to the light anyway.
                /* The wave sum again, here, at this pixel.
                 *
                 * Interpolating the vertex normal across the mesh is what made the sea render as a
                 * field of flat blue lozenges when seen from above: the ocean disc's triangles are
                 * tens of metres across at any distance, the Gerstner normal turns right round
                 * inside one of them, and a linear interpolation of that is a facet. Re-evaluating
                 * costs one sine per wave per pixel and is the difference between a surface and a
                 * low-poly model of one. The Jacobian has to come from here too, or white caps are
                 * smeared across whole triangles.
                 */
                // How much ground one pixel covers, which is what decides which waves and ripples
                // can be resolved at all.
                float footprint = max(length(ddx(input.positionWS.xz)), length(ddy(input.positionWS.xz)));

                FishWaterSurface wave = FishWaterDisplace(
                    float3(input.flatXZ.x, _FishWaterLevel, input.flatXZ.y), input.surface.x, footprint);
                float detailFade = saturate(1.0 - distanceToCamera / max(1.0, _DetailFadeDistance));

                /* Cat's paws: the patches of ruffled water a gusting wind drags across a bay,
                 * darker and more matte than the glassy lanes between them. They are one of the
                 * few things that reads instantly as a real sea rather than a shader, and they
                 * cost one noise sample — the ripples are simply stronger inside them.
                 */
                float2 windDrift = _FishWaterWind.xy * _FishWaterTime * 0.6;
                float gust = FishWaterFbm((input.flatXZ - windDrift) / max(10.0, _GustScale));
                detailFade *= lerp(1.0 - _GustStrength, 1.0 + _GustStrength * 0.45, gust);

                // Perturbed by a SLOPE rather than a tangent-space normal, so there is no tangent
                // frame to invent and nothing to seam where a wave face turns past vertical.
                float2 slope = FishWaterRippleSlope(input.flatXZ, detailFade, footprint);
                float3 normalWS = normalize(wave.normalWS + float3(-slope.x, 0.0, -slope.y));

                // Under the surface everything flips: the normal faces the viewer, and none of the
                // shore or foam terms mean anything looking up through the water.
                bool underwater = _WorldSpaceCameraPos.y < _FishWaterLevel;
                if (underwater)
                {
                    normalWS = -normalWS;
                }

                // ── Light ─────────────────────────────────────────────
                float4 shadowCoord;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half shade = mainLight.shadowAttenuation * saturate(_FishWaterCloudShadow);
                half3 lightColor = mainLight.color * shade;

                half NdotL = saturate(dot(normalWS, mainLight.direction));

                /* What lights the water ITSELF.
                 *
                 * The colour of a body of water is not a pigment — it is sunlight and skylight that
                 * went in, scattered, and came back out, so it has to be multiplied by the light
                 * like any other reflectance. Left as a raw constant, which is what this was, deep
                 * water renders as a hole cut in the world: measured at midday on a clear day the
                 * near sea came back at 0.03 luminance, blacker than the sky it sat under.
                 *
                 * The sun's contribution is not NdotL against the surface normal: that is the
                 * mirror direction, and it is already spent on the reflection and the glint. Light
                 * enters where the sun is high, so this is keyed to how far ABOVE the horizon the
                 * sun stands.
                 */
                half sunHeight = saturate(mainLight.direction.y);
                /* The 0.12 floor is the light that reaches the water by every path this does not
                 * model — sky from behind the camera, multiple scattering, bounce off the bed.
                 * Without it the sea goes to literal black wherever the sun is low or shadowed,
                 * and this project runs no tonemapper to lift it back out. */
                half3 waterLight = lightColor * sunHeight * 0.9 + _GlossyEnvironmentColor.rgb * 0.8 + 0.12;

                // ── How much water is in front of what is behind ──────
                float waterColumn = 1000.0;   // no depth texture: assume open sea
                float shoreFade = 1.0;
                #if defined(_WATER_DEPTH)
                    float rawDepth = SampleSceneDepth(screenUV);
                    float sceneEye = FishWaterEyeDepth(rawDepth);
                    // Along the view ray, which is the path the light actually took through it.
                    waterColumn = max(0.0, sceneEye - surfaceEye);
                    shoreFade = saturate(waterColumn / max(0.01, _EdgeFade));
                #endif

                /* How clear the water is HERE. A real sea is never one colour: plankton, sediment
                 * and the currents that carry them leave patches hundreds of metres across, and
                 * without them an ocean renders as one flat tint whose only variation is the
                 * waves. Drifting slowly with the wind, far slower than the gust patches.
                 */
                float clarityField = FishWaterFbm(input.flatXZ / max(20.0, _ClarityScale)
                    - _FishWaterWind.xy * _FishWaterTime * 0.02);
                float absorptionDepth = max(0.05, _AbsorptionDepth * lerp(1.0 - _Clarity, 1.0 + _Clarity, clarityField));

                float absorbed = 1.0 - exp(-waterColumn / absorptionDepth);
                half3 bodyColor = FishWaterBody(absorbed, waterColumn) * waterLight;

                // ── What is behind it ─────────────────────────────────
                half3 behind = bodyColor;
                half alpha = _MaxAlpha;
                #if defined(_WATER_REFRACTION)
                    // Offset by the surface normal, scaled by 1/depth so a ripple bends the sea bed
                    // by the same number of METRES whether it is near or far — an offset in screen
                    // pixels makes distant water look like frosted glass.
                    float2 offset = normalWS.xz * _RefractionStrength / max(1.0, surfaceEye);
                    float2 refractedUV = screenUV + offset;

                    // The edge case every refractive water gets wrong: if what is at the offset UV
                    // is IN FRONT of the water, it is not behind the water at all, and sampling it
                    // smears a character standing at the shoreline across the surface. Reject and
                    // fall back to the straight-through sample.
                    #if defined(_WATER_DEPTH)
                        float refractedEye = FishWaterEyeDepth(SampleSceneDepth(refractedUV));
                        refractedUV = refractedEye < surfaceEye ? screenUV : refractedUV;
                        waterColumn = max(0.0, max(refractedEye, sceneEye) - surfaceEye);
                        absorbed = 1.0 - exp(-waterColumn / absorptionDepth);
                        bodyColor = FishWaterBody(absorbed, waterColumn) * waterLight;
                    #endif

                    half3 refracted = SampleSceneColor(refractedUV);
                    // Beer-Lambert through the column, then the water's own colour on top of what
                    // survived. This is why shallow water shows the sand and deep water does not.
                    behind = lerp(refracted, bodyColor, absorbed);
                    alpha = 1.0;   // the background is already composited into rgb
                #else
                    alpha = _MaxAlpha * lerp(0.35, 1.0, absorbed);
                #endif

                // Fresnel, Schlick, with water's own F0. 0.02 is not a tuning value: it falls out
                // of the refractive index of water, 1.333, and it is the reason water is almost a
                // mirror at a grazing angle and almost clear looking straight down.
                half NdotV = saturate(dot(normalWS, view));
                half fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);

                // Roughness grows with distance to stand in for the detail that was faded out.
                half perceptualRoughness = saturate((1.0 - _Smoothness) + (1.0 - detailFade) * 0.06);
                float3 reflectVector = reflect(-view, normalWS);
                // A reflection vector that has been bent below the horizon by a steep crest picks
                // up the ground half of the probe and puts a brown smear on the wave. Real water
                // at that angle reflects the sky just past the crest.
                reflectVector.y = abs(reflectVector.y) * (underwater ? -1.0 : 1.0);
                half3 reflection = GlossyEnvironmentReflection(reflectVector, input.positionWS,
                    perceptualRoughness, 1.0h, screenUV);

                // Sun glint: GGX, so the highlight is a long streak toward the sun across the
                // chop rather than a round blob.
                half3 halfVector = SafeNormalize(mainLight.direction + view);
                half NdotH = saturate(dot(normalWS, halfVector));
                half roughness = max(1e-3, perceptualRoughness * perceptualRoughness);
                half a2 = roughness * roughness;
                half d = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
                half specular = a2 / max(1e-4, 3.14159 * d * d);
                half3 glint = lightColor * specular * _SpecularStrength * fresnel * NdotL;

                // Sub-surface: light that went into a crest and came out toward the eye, which is
                // what makes a wave glow green when the sun is behind it. Keyed to the wave's own
                // height so it only happens on the crests.
                half back = pow(saturate(dot(view, -mainLight.direction)), 4.0);
                half crestLift = saturate(wave.height * 0.5 + 0.35);
                half3 scatter = _ScatterColor.rgb * lightColor * back * crestLift * _ScatterStrength;

                half3 color = lerp(behind + scatter, reflection, fresnel) + glint;

                /* Seen from below, the surface is Snell's window: a bright disc of the whole sky
                 * squeezed into a cone 48.6 degrees wide, and a mirror of the dark water outside
                 * it. The critical angle is not a tuning value — it is asin(1/1.333) for water
                 * against air, and refract() reports it for free by returning zero when the ray
                 * cannot escape.
                 */
                if (underwater)
                {
                    /* refract() wants the normal on the side the ray is COMING FROM, which under
                     * the water is the downward-facing one — the flip a few lines up already made.
                     * Handing it the upward normal instead does not fail: it quietly returns a
                     * direction pointing back DOWN into the water, so the window sampled the dark
                     * lower half of the environment and Snell's window rendered as a brown hole. */
                    float3 transmitted = refract(-view, normalWS, 1.333);
                    bool escapes = dot(transmitted, transmitted) > 1e-5;
                    /* The mirrored half is NOT the deep colour: outside the window the surface
                     * reflects the sunlit water around the diver, which glows, rather than the
                     * blackness straight down. Taking the deep colour there made the whole sea
                     * outside the window read as a ceiling of tar. */
                    half3 mirrored = _ShallowColor.rgb * waterLight * 0.45;
                    if (escapes)
                    {
                        half3 above = GlossyEnvironmentReflection(normalize(transmitted),
                            input.positionWS, perceptualRoughness, 1.0h, screenUV);
                        color = lerp(mirrored, above, 0.92);
                    }
                    else
                    {
                        // Total internal reflection: the underside of the sea is a mirror, and the
                        // ripples on it are what makes that mirror read as water.
                        color = mirrored;
                    }
                    alpha = _MaxAlpha;
                }

                // ── Foam ──────────────────────────────────────────────
                half foamAlpha = 0.0;
                if (!underwater)
                {
                    // Two scales of value noise drifting with the wind, so foam reads as patches
                    // being carried rather than a texture sliding under the water.
                    float2 drift = _FishWaterWind.xy * _FishWaterTime * 0.35;
                    float2 foamUV = (input.positionWS.xz - drift) / max(0.5, _FoamScale);
                    half mask = saturate(FishWaterNoise(foamUV) * 0.65
                        + FishWaterNoise(foamUV * 2.7 + 13.0) * 0.35);

                    // Where the water is shallow, and where the wave is pinching itself into a
                    // breaking crest. The second is the Jacobian going toward zero, which is the
                    // real criterion for a Gerstner wave breaking rather than a height threshold.
                    /* Shore foam off the VERTICAL depth under this point, not the depth along
                     * the view ray. The view ray's length through water depends on where the
                     * camera is standing, so the surf line crept up and down the beach as the
                     * player turned their head. The seabed field answers the question that was
                     * actually being asked: how much water is over the sand here.
                     */
                    float shoreDepth = FishWaterSeabedDepth(input.flatXZ);
                    half shore = 1.0 - saturate(shoreDepth / max(0.01, _FoamDepth));
                    #if defined(_WATER_DEPTH)
                        // Thin sheets of swash have almost no water in them from any angle.
                        shore = max(shore, 1.0 - saturate(waterColumn / max(0.01, _FoamDepth)));
                    #endif

                    // Where the waves are actually breaking, from the depth limit in the vertex
                    // stage. This is the band of white that rolls in ahead of the swash.
                    half surf = saturate(wave.surf * _ShoreSurf);
                    /* Foam where the JACOBIAN FALLS BELOW the threshold, not where it is merely
                     * less than one — it is below one over the whole upper half of every wave, so
                     * the other reading foamed the entire sea at any choppiness above nothing.
                     *
                     * The threshold is 0.70 and that is measured, not picked. Sampled over six
                     * wavelengths of the swell, the Jacobian drops under 0.70 on 0% of the sea
                     * below 8 m/s of wind, 1.6% at 12 and 7% at 16 — against a real ocean's 0%
                     * below 6, 2-5% at 10-12 and 10-20% past 20. The default used to be 0.35,
                     * which the surface NEVER REACHES: its lowest measured value at any wind is
                     * 0.53, so there were no white caps at all at any setting.
                     *
                     * Past about 15 m/s a fully developed sea stops getting steeper — its height
                     * and its wavelength grow together — so the geometry alone stops adding foam
                     * while a real ocean goes on whitening. _FishWaterWhitecap carries that part.
                     */
                    half threshold = lerp(_FoamCrest, 0.80, saturate(_FishWaterWhitecap));
                    half crest = saturate((threshold - wave.jacobian) / max(0.01, threshold));

                    /* Each term gets its own curve, and the SHARPNESS is applied to the crest term
                     * itself rather than to the product.
                     *
                     * Applied afterwards, as it was, the arithmetic cancelled the calibration: a
                     * genuinely breaking crest scores about 0.18 here, the noise mottling takes it
                     * to 0.14, and a smoothstep starting at 0.15 then returns zero. Measured on a
                     * 20 m/s gale, that left white caps on the single steepest crest in the frame
                     * and nowhere else.
                     */
                    half breaking = smoothstep(0.02, max(0.06, _FoamSharpness), crest);
                    half shoreFoam = smoothstep(0.12, 0.75, max(shore, surf));
                    // Mottled, not masked out: multiplying by the noise outright cut the coverage
                    // back to a fraction of what was just calibrated.
                    half foam = saturate(max(shoreFoam, breaking)) * (0.45 + 0.55 * mask);
                    // Foam is a rough white surface: it takes the sky as diffuse ambient, not as a
                    // reflection. _GlossyEnvironmentColor is URP's own ambient sky colour, which is
                    // right whichever ambient mode the scene is set to.
                    half3 foamLit = _FoamColor.rgb * (lightColor * NdotL * 0.6 + _GlossyEnvironmentColor.rgb * 0.5 + 0.1);
                    color = lerp(color, foamLit, foam);
                    foamAlpha = foam;
                }

                /* The soft edge applies to the WATER, and the foam is then laid over the top of
                 * it. The other order fades the swash out exactly where it matters: a sheet of
                 * water running up wet sand is a centimetre thick and almost entirely foam, so
                 * multiplying that foam by its own thinness erases the thing being drawn. */
                alpha = saturate(alpha * shoreFade);
                alpha = max(alpha, foamAlpha * _MaxAlpha);
                color = MixFog(color, input.surface.z);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    // No fallback. A fallback to Lit would quietly draw a grey plastic plane on any platform where
    // this fails to compile, which is the failure that looks like a content bug for a week.
}
