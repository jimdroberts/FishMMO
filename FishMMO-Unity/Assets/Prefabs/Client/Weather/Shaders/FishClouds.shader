// Volumetric clouds: raymarched at a fraction of the screen, steadied against the last frame, then
// composited over the sky and behind the world. Nothing about clouds lives in the skybox.
Shader "Hidden/FishMMO/Weather/Clouds"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        // ── 0: raymarch, at the pass's own resolution ──
        Pass
        {
            Name "CloudMarch"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishCloudVolume.hlsl"
            #include "FishMist.hlsl"

            float4 _FishCloudMarchParams;   // x steps, y detail amount, z frame index, w max distance
            float4 _FishCloudJitter;        // xy where inside its texel each ray looks this frame (texels, -0.5..0.5), zw this pass's size
            float4x4 _FishCloudInverseVP;
            // The reconstruction options under trial (CloudOptions in FishCloudsFeature.cs; FishCloudResolve.hlsl
            // declares the same for the steadying): w the rays' phase this frame, always set.
            float4 _FishCloudOptions;
            // The far band (pass 10, CloudFarBand): x where it begins (m; 0 none), y frames between a texel's
            // marches, z this frame's turn, w 1 when last frame's band can be carried on.
            float4 _FishCloudFarBand;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // A world-space ray for a screen position.
            float3 RayFor(float2 uv)
            {
                float4 clip = float4(uv * 2.0 - 1.0, 1.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                float4 world = mul(_FishCloudInverseVP, clip);
                return normalize(world.xyz / world.w - _WorldSpaceCameraPos.xyz);
            }

            // Interleaved gradient noise: a different offset per pixel and per frame, which the
            // temporal pass then averages away.
            float Jitter(float2 pixel, float frame)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(pixel + frame * 5.588238, magic.xy)));
            }

            // Two targets: the clouds, and what the steadying needs to carry each texel's history
            // with its own cloud (FishCloudResolve.hlsl, step 2).
            struct MarchOutput
            {
                float4 clouds : SV_Target0;     // rgb scattered light, a transmittance
                float2 motion : SV_Target1;     // x the mean distance of what the ray saw (km), y its bands' mean wind gain
            };

            MarchOutput Frag(Varyings input)
            {
                // Not the middle of the texel: a different place inside it every frame. A texel
                // here is two or three of the screen's pixels across, and a ray through its middle
                // every frame can only ever say what the middle looks like — the steadying could
                // average it for ever and never learn where inside the texel a cloud's edge falls,
                // which is what drew every edge in blocks. Looked through at a new place each frame,
                // the texel is a handful of exact answers for exact places on the screen, and pass 1
                // reads each pixel off them with a tent over their TRUE places, so over the sixteen
                // places the grid averages out of the picture. The ray and the depth it stops at both
                // come from that place, so the answer is true of it.
                float2 uv = input.uv + _FishCloudJitter.xy / max(1.0, _FishCloudJitter.zw);
                float3 direction = RayFor(uv);
                float rawDepth = SampleSceneDepth(uv);
                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                // Nothing drawn means the sky: the clouds may run to the far end of the shell.
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1e-6;
                #else
                    bool isSky = rawDepth >= 1.0 - 1e-6;
                #endif
                // Open sky runs to where the highest layer stops being drawn, which is several
                // times the draw distance: that figure is the deck's, and grows with height.
                float maxDistance = isSky ? _FishCloudMarchParams.w * 4.6 : depth / max(1e-4, dot(direction, -UNITY_MATRIX_V[2].xyz));

                // Hashed on this pass's own pixel, which is a whole number: the noise is built to spread
                // its values evenly over every three-by-three block of whole pixels, and that block is
                // exactly the neighbourhood pass 1 clips the history to — so the spread of those nine
                // rays is the ray-phase noise the clip must let through. Fed the screen position
                // instead, the pixels were two and a half apart and the spread was lost.
                //
                // The spatial pattern moved on in time by one phase for the whole frame, worked out by
                // the feature (CloudOptions.JitterPhase): the place's Bayer value over sixteen, so
                // neighbouring places in a texel are far apart in phase and a cycle covers the step
                // evenly, and golden-ratio from each cycle to the next, the sequence that stays as evenly
                // spread as any however many have been taken. A texel's phase is thus a new one every
                // frame, and the steadying's moving average over sixteen frames and more averages it out;
                // held still, one pixel's phase stood in the picture as the woven rings of interleaved
                // gradient noise. frac(u + c) of a uniform u is uniform, so every ray's phase still covers
                // one full step evenly, which is what keeps the march unbiased.
                float jitter = frac(Jitter(input.positionCS.xy, 0.0) + _FishCloudOptions.w);
                // The render profile's diagnostics (Ray Jitter off, _FishCloudDiag.x): every sample at the
                // middle of its step. The steps are laid out from the bands' edges and each cloud's own
                // (FishCloudMarch), so what is left is smooth; bands that remain are undersampling.
                jitter = _FishCloudDiag.x > 0.5 ? 0.5 : jitter;
                // The light march's own phase for this pixel, independent of the ray's and moved on in
                // time by the same phase (each place's phases the golden-ratio sequence). Not the same
                // noise at another offset: interleaved gradient noise is linear in the pixel inside its
                // frac, so an offset only adds a constant to it — the light's phase would have been the
                // ray's, shifted. x is the R2 dither (a different linear form, as evenly spread over the
                // screen), y the gradient noise with the axes swapped, for the cone's turn.
                float2 texel = input.positionCS.xy;
                float2 lightSeed = frac(float2(frac(dot(texel, float2(0.7548777, 0.5698403))), Jitter(texel.yx, 0.0))
                    + _FishCloudOptions.w * float2(1.0, 1.6180340));
                float cloudDistance;
                float2 cloudMotion;
                // In two bands where the ray runs past the far band's start: this pass walks the near one, at
                // the steps the whole ray would have had; pass 10 lays the far band behind it.
                bool split = _FishCloudFarBand.x > 0.0 && maxDistance > _FishCloudFarBand.x;
                FishCloudMarchWhole = split ? maxDistance : -1.0;
                float4 result = FishCloudMarch(_WorldSpaceCameraPos.xyz, direction, split ? _FishCloudFarBand.x : maxDistance, jitter, lightSeed,
                    (int)_FishCloudMarchParams.x, _FishCloudMarchParams.y, cloudDistance, cloudMotion);
                FishCloudMarchWhole = -1.0;
                // The mist on the ground, by its own short march (FishMist.hlsl), laid in front: it is
                // the nearest thing along any ray it is on. The steadying fetches a pixel's history from
                // where what it shows stood, so the distance it is handed is the two media's, each by
                // what it added; the mist carries no wind gain of its own, as the fog does not.
                float mistDistance;
                float4 mist = FishMistMarch(_WorldSpaceCameraPos.xyz, direction, maxDistance, jitter, mistDistance);
                if (mist.a < 0.9999)
                {
                    float mistAdded = 1.0 - mist.a;
                    float cloudAdded = mist.a * (1.0 - result.a);
                    float added = max(1e-5, mistAdded + cloudAdded);
                    cloudMotion = float2((mistDistance * mistAdded + cloudMotion.x * cloudAdded) / added, cloudMotion.y * cloudAdded / added);
                    result = float4(mist.rgb + mist.a * result.rgb, mist.a * result.a);
                }
                MarchOutput output;
                output.clouds = float4(result.rgb, result.a);
                // A debug view in place of the clouds (_FishCloudDiag.w, CloudDebugView), opaque.
                if (_FishCloudDiag.w > 0.5)
                {
                    output.clouds = FishCloudMarchDebug;
                }
                // In kilometres, so a half float holds the farthest sky ray (4.6 × the draw distance,
                // some 150 km) to a few metres at ten.
                output.motion = float2(cloudMotion.x * 0.001, cloudMotion.y);
                return output;
            }
            ENDHLSL
        }

        // ── 1: rebuild the clouds at the buffer's resolution, from this frame and the last ──
        // The steadying (FishCloudResolve.hlsl): a temporal upsampler — a tent over this frame's texels,
        // the history carried with each cloud's own drift, clipped to this frame's neighbourhood, and
        // blended in as a moving average. This pass is the fallback: where the platform runs
        // compute kernels well, FishCloudResolve.compute does the same arithmetic from groupshared
        // memory and this pass is not drawn. It stays for WebGL, GLES and WebGPU until those are
        // proven, and is exactly the reference the kernel is checked against — both call the same
        // FishCloudResolve, so neither can drift from the other.
        //
        // Two targets: the clouds (rgb scattered light, a transmittance), and how many frames stand
        // behind each pixel, so a pixel that has only just come into view takes this frame whole and one
        // that has settled takes it as one part in sixteen.
        Pass
        {
            Name "CloudTemporal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishCloudResolve.hlsl"

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            struct Output
            {
                float4 clouds : SV_Target0;
                float weight : SV_Target1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            Output Frag(Varyings input)
            {
                float2 marched = max(1.0, _FishCloudJitter.zw);
                float2 perTexel = _FishCloudUpsample.xy / marched;     // this pass's pixels to a marched texel
                float rawHere = SampleSceneDepth(input.uv);
                float here = LinearEyeDepth(rawHere, _ZBufferParams);
                float3 camera = _WorldSpaceCameraPos.xyz;
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float3 direction = FishResolveRay(input.uv, camera);

                Output output;
                // Ground the camera looks down on from under the clouds has none in front of it:
                // one depth read and a height, where the steadying would otherwise read thirty-odd
                // textures to arrive at the same (0, 0, 0, 1).
                if (FishResolveBelowClouds(rawHere, FishResolveSeen(here, camera, direction, forward)))
                {
                    FishCloudResolved clear = FishResolveClear();
                    output.clouds = clear.clouds;
                    output.weight = clear.weight;
                    return output;
                }

                // This pixel on the grid the rays were cast on this frame. With the offset taken
                // away, texel (i, j) looked through (i + 0.5, j + 0.5), so the nearest is the floor.
                float2 onGrid = input.uv * marched - _FishCloudJitter.xy;
                int2 nearest = (int2)floor(onGrid);
                int2 last = (int2)marched - 1;
                float4 values[9];
                float theres[9];
                float2 ats[9];
                float2 motions[9];
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        int k = (y + 1) * 3 + (x + 1);
                        int2 texel = clamp(nearest + int2(x, y), int2(0, 0), last);
                        ats[k] = texel + 0.5;
                        values[k] = LOAD_TEXTURE2D(_FishCloudCurrent, texel);
                        motions[k] = LOAD_TEXTURE2D(_FishCloudCurrentMotion, texel).rg;
                        // The depth where that ray actually went: the same read at the same place
                        // the march made, so the two agree on what it stopped at.
                        float2 sampleUV = (ats[k] + _FishCloudJitter.xy) / marched;
                        theres[k] = LinearEyeDepth(SampleSceneDepth(sampleUV), _ZBufferParams);
                    }
                }
                FishCloudResolved resolved = FishCloudResolve(input.uv, rawHere, here, camera, direction, forward,
                    onGrid, marched, perTexel, values, theres, ats, motions);
                output.clouds = resolved.clouds;
                output.weight = resolved.weight;
                return output;
            }
            ENDHLSL
        }

        // ── 2: composite into the frame ──
        Pass
        {
            Name "CloudComposite"
            Blend One SrcAlpha            // scattered light added, what is behind kept by transmittance
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FishCloudBuffer);
            SAMPLER(sampler_FishCloudBuffer);
            float4 _FishCloudComposite;     // xy one over the buffer's size, z 1 when the buffer is the screen's own size

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Pass 1 already rebuilt the clouds pixel for pixel, each from the samples that looked
                // at what that pixel looks at, so at the screen's own size there is nothing left to
                // do but read it. Filtered again here it would only blur what it rebuilt.
                float blur = _FishCloudComposite.w;
                // Only a buffer at the screen's own size is softened: a smaller one is scaled up below, and
                // its bilinear taps here pulled the sky's texels over every edge the upscale keeps apart.
                if (blur > 0.0 && _FishCloudComposite.z > 0.5)
                {
                    // Softened: a Gaussian of `blur` pixels over a three-by-three of bilinear taps (a
                    // five-pixel support at one pixel), each weighed by whether it looks at the same
                    // thing this pixel does, as the upscale below does — so the clouds lose their grain
                    // and the world's edges keep theirs. Colour and transmittance share the weights: the
                    // buffer is premultiplied, and a blur of it is still a cloud.
                    // In SCREEN pixels, whatever size the buffer is (a large screen's is capped smaller).
                    float2 step = (_ScreenParams.zw - 1.0) * blur;
                    float hereDepth = LinearEyeDepth(SampleSceneDepth(input.uv), _ZBufferParams);
                    float4 blurred = 0.0;
                    float blurWeight = 0.0;
                    [unroll] for (int by = -1; by <= 1; by++)
                    {
                        [unroll] for (int bx = -1; bx <= 1; bx++)
                        {
                            float2 at = input.uv + float2(bx, by) * step;
                            float there = LinearEyeDepth(SampleSceneDepth(at), _ZBufferParams);
                            float same = saturate(1.0 - abs(there - hereDepth) / max(12.0, hereDepth * 0.2));
                            same = max(same, saturate(min(there, hereDepth) / 4000.0));
                            // exp(−d²/2σ²) at a one-step spacing of σ: 1, 0.61, 0.37.
                            float gauss = exp(-0.5 * (bx * bx + by * by));
                            float weight = gauss * same;
                            blurred += SAMPLE_TEXTURE2D_LOD(_FishCloudBuffer, sampler_FishCloudBuffer, at, 0) * weight;
                            blurWeight += weight;
                        }
                    }
                    return blurred / max(1e-4, blurWeight);
                }
                if (_FishCloudComposite.z > 0.5)
                {
                    return SAMPLE_TEXTURE2D_LOD(_FishCloudBuffer, sampler_FishCloudBuffer, input.uv, 0);
                }
                // A buffer smaller than the screen is scaled up here (a march at a sixteenth of the screen
                // rebuilds at a quarter of it). Each pixel takes the four nearest buffer texels, read
                // exactly, by their bilinear share and by whether each was built for what this pixel looks
                // at: the depth at the texel's own centre, which is where the steadying stood when it
                // rebuilt it. Every ray stops at the world, so a texel beside a post holds the sky ray's
                // answer, a long march full of scattered light, while the post's own pixels hold almost
                // none. Bilinear taps at offsets, weighed by the depth at the TAP (not at the texel the
                // tap mostly reads), blended the sky's texel in before any weight could keep it out: pale
                // blocks round every leaf against the sky at a low march (ScenePerfProbe, 2026-10-07).
                float2 size = rcp(_FishCloudComposite.xy);
                float here = LinearEyeDepth(SampleSceneDepth(input.uv), _ZBufferParams);
                float2 pos = input.uv * size - 0.5;
                int2 base = (int2)floor(pos);
                float2 f = pos - base;
                int2 last = (int2)size - 1;
                float4 sum = 0.0;
                float total = 0.0;
                float bestGap = 1e9;
                float4 best = float4(0.0, 0.0, 0.0, 1.0);
                float nearest = 1e9;
                float farthest = 0.0;
                float4 farValue = float4(0.0, 0.0, 0.0, 1.0);
                [unroll] for (int t = 0; t < 4; t++)
                {
                    int2 offset = int2(t & 1, t >> 1);
                    int2 texel = clamp(base + offset, int2(0, 0), last);
                    float there = LinearEyeDepth(SampleSceneDepth((texel + 0.5) * _FishCloudComposite.xy), _ZBufferParams);
                    float4 value = LOAD_TEXTURE2D(_FishCloudBuffer, texel);
                    float share = (offset.x ? f.x : 1.0 - f.x) * (offset.y ? f.y : 1.0 - f.y);
                    float gap = abs(there - here) / max(2.0, here * 0.06);
                    // Both far away is both sky, whatever the numbers say.
                    float same = max(saturate(1.0 - gap), saturate(min(there, here) / 4000.0));
                    sum += value * share * same;
                    total += share * same;
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        best = value;
                    }
                    nearest = min(nearest, there);
                    if (there > farthest)
                    {
                        farthest = there;
                        farValue = value;
                    }
                }
                if (total > 0.02)
                {
                    return sum / total;
                }
                // No texel shares this pixel's depth: a leaf or twig thinner than a texel against the sky,
                // or the sky through a gap in the leaves. The closest depth when it is close enough; a
                // pixel nearer than all of them (a leaf in front of the sky) stands in front of the clouds,
                // which add nothing to it; one farther than all (sky through the canopy) is the farthest's.
                if (bestGap < 3.0)
                {
                    return best;
                }
                return here < nearest ? float4(0.0, 0.0, 0.0, 1.0) : farValue;
            }
            ENDHLSL
        }

        // ── 3: the shadow the clouds throw on the world ──
        Pass
        {
            Name "CloudShadow"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "FishCloudVolume.hlsl"

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // The cookie is a window in the light's own plane, because that is how the light reads
            // it: each texel is one ray through the clouds, and what it carries is what gets through.
            float4 Frag(Varyings input) : SV_Target
            {
                float3 toSun = _FishCloudSunDir.xyz;
                if (toSun.y < 0.02 || _FishCloudSunDir.w < 0.01)
                {
                    return 1.0;     // the sun is down: nothing to shadow
                }
                float2 offset = (input.uv - 0.5) * _FishCloudShadowArea.z;
                float3 origin = _FishCloudShadowOrigin.xyz + _FishCloudShadowRight.xyz * offset.x + _FishCloudShadowUp.xyz * offset.y;
                // Slide down that ray to the ground, so the march always starts under the clouds.
                origin -= toSun * (origin.y / max(0.02, toSun.y));
                // The same offset for every texel, on purpose. Each texel's depth is a handful of
                // samples, and with a random offset per texel the estimate differs texel to texel
                // wherever the cloud is translucent — which, at real extinction, is most of any
                // cloud. That variance is what the "scattered dots" were: not the shadow of anything,
                // just the error of the estimate, redrawn identically every frame. A shared offset
                // makes the error the same next door, so what is left is the shape of the cloud.
                float density = FishCloudShadowDepth(origin, toSun, (int)max(2.0, _FishCloudShadowArea.w), 0.5, _FishCloudShadowArea.x);
                // A cookie: 1 in full sun, darker under a cloud, never black — what the ground still
                // gets from the sky is the ambient term's job, not the cookie's. How dark is the
                // profile's shadow strength, which used to be handed to the presenter and then never
                // read: the slider did nothing and the floor was a constant.
                // A real optical depth, so the shadow of a heap is hard-edged and the shadow of a
                // wisp is faint. What gets through is the light that came straight through AND the
                // light scattered through (FishCloudGroundTransmission, Eddington): e^−τ alone was
                // only the first, and left the ground under any thick cloud black to the sun, when
                // about 15 % of it comes through a cumulus and a few per cent through a storm.
                // The strength is how much of that the cookie applies.
                float through = FishCloudGroundTransmission(density, toSun.y, _FishCloudLight.z);
                return saturate(lerp(1.0, through, saturate(_FishCloudShadowArea.y)));
            }
            ENDHLSL
        }
        // ── 4: light shafts from the sun, or from around a body eclipsing it ──
        Pass
        {
            Name "CloudGodRays"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FishGodRaySource);
            SAMPLER(sampler_FishGodRaySource);
            TEXTURE2D(_FishGodRayClouds);
            SAMPLER(sampler_FishGodRayClouds);
            float4 _FishGodRayDir;      // xyz direction to the light, w unused
            float4 _FishGodRayParams;   // x falloff per screen height, y metres nearer than which the world is ignored, z reach (screen heights), w taps
            float4 _FishGodRayMask;     // x dark enough to block, y how soft that edge is, z eclipse 0..1
            float4 _FishGodRayAir;      // x medium 0..1, y metres of air that fill a shaft in, z/w cloud transmittance that blocks / passes
            float4x4 _FishGodRayVP;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // How much of the light gets past this point of the frame, and whether the point says
            // anything at all. Cloud blocks by how little it lets through; the world blocks only
            // from far enough away to be a ridge and not a fence post, and anything nearer is left
            // out of the count — what stands behind it is unknown, and guessing is what drew rays
            // off every object.
            float Visible(float2 uv, float veil, out float known)
            {
                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1e-6;
                #else
                    bool isSky = rawDepth >= 1.0 - 1e-6;
                #endif
                if (!isSky)
                {
                    float eye = LinearEyeDepth(rawDepth, _ZBufferParams);
                    known = smoothstep(_FishGodRayParams.y * 0.5, _FishGodRayParams.y, eye);
                    return 0.0;
                }
                known = 1.0;
                // The cloud buffer's alpha is transmittance: 1 clear sky, 0 solid cloud. The clouds
                // are translucent, so the raw figure hardly moves between a gap and a cloud; the
                // contrast a shaft needs is drawn out of it here.
                float clear = SAMPLE_TEXTURE2D_LOD(_FishGodRayClouds, sampler_FishGodRayClouds, uv, 0).a;
                // Measured against the clearest sky in the frame and not against 1: fog and haze
                // veil the whole sky evenly, and an even veil interrupts nothing. Read as a blocker
                // it shut every shaft off at dawn and left only the darkening.
                float open = smoothstep(_FishGodRayAir.z, _FishGodRayAir.w, saturate(clear / veil));
                // A body in front of the light blocks it too, and a body has no depth to be found
                // by: it is simply dark against a bright sky. Only in an eclipse — a dusk sky is
                // dark everywhere and would otherwise block every shaft of the day's best hour.
                float3 colour = SAMPLE_TEXTURE2D_LOD(_FishGodRaySource, sampler_FishGodRaySource, uv, 0).rgb;
                float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float lit = smoothstep(_FishGodRayMask.x, _FishGodRayMask.x + max(0.01, _FishGodRayMask.y), luminance);
                return open * lerp(1.0, lit, saturate(_FishGodRayMask.z));
            }

            // The clearest the sky gets anywhere in the frame: what "open" means today. Nine looks
            // across the picture, sky only — the clouds are not marched in front of near ground, so
            // the ground always reads clear and would hide any veil.
            float ClearestSky()
            {
                float clearest = 0.0;
                float found = 0.0;
                for (int y = 0; y < 3; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        float2 uv = float2(0.17 + 0.33 * x, 0.17 + 0.33 * y);
                        float rawDepth = SampleSceneDepth(uv);
                        #if UNITY_REVERSED_Z
                            bool isSky = rawDepth <= 1e-6;
                        #else
                            bool isSky = rawDepth >= 1.0 - 1e-6;
                        #endif
                        if (isSky)
                        {
                            clearest = max(clearest, SAMPLE_TEXTURE2D_LOD(_FishGodRayClouds, sampler_FishGodRayClouds, uv, 0).a);
                            found = 1.0;
                        }
                    }
                }
                // Floored, so a frame that is all cloud does not turn its thinnest cloud into a gap.
                return found > 0.5 ? max(0.35, clearest) : 1.0;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Where the light sits on screen. Behind the camera, there are no shafts.
                float4 clip = mul(_FishGodRayVP, float4(_WorldSpaceCameraPos.xyz + _FishGodRayDir.xyz * 1e6, 1.0));
                if (clip.w <= 1e-5)
                {
                    return 0.0;
                }
                float2 light = clip.xy / clip.w * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    light.y = 1.0 - light.y;
                #endif

                // Aspect-correct, so distances are in screen heights and the reach is a circle.
                float aspect = _ScreenParams.x / max(1.0, _ScreenParams.y);
                float2 scale = float2(aspect, 1.0);

                // The light may stand outside the frame. The shafts it throws into the frame are
                // still there, but less and less is known about what interrupts them, so they
                // fade with how far out it is and are gone by about half a screen.
                float2 outside = max(0.0, max(-light, light - 1.0)) * scale;
                float framed = 1.0 - smoothstep(0.0, 0.6, length(outside));
                if (framed <= 0.0)
                {
                    return 0.0;
                }

                float2 toLight = light - input.uv;
                float span = length(toLight * scale);
                float spread = span / max(1e-4, _FishGodRayParams.z);
                if (spread >= 1.0)
                {
                    return 0.0;      // too far from the light for a shaft to reach
                }

                // March toward the light, but only as far as the frame goes.
                float inFrame = 1.0;
                if (toLight.x > 1e-5) inFrame = min(inFrame, (1.0 - input.uv.x) / toLight.x);
                if (toLight.x < -1e-5) inFrame = min(inFrame, (0.0 - input.uv.x) / toLight.x);
                if (toLight.y > 1e-5) inFrame = min(inFrame, (1.0 - input.uv.y) / toLight.y);
                if (toLight.y < -1e-5) inFrame = min(inFrame, (0.0 - input.uv.y) / toLight.y);
                inFrame = saturate(inFrame);

                int taps = (int)max(4.0, _FishGodRayParams.w);
                float2 hop = toLight * inFrame / taps;
                float stride = span * inFrame / taps;
                float falloff = _FishGodRayParams.x;
                // A different start in every pixel turns the bands of a short march into grain,
                // which the half-resolution upsample then smooths away.
                float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);

                float veil = ClearestSky();
                float sum = 0.0;
                float total = 0.0;
                float possible = 0.0;
                float lastSeen = 1.0;
                float lastKnown = 0.0;
                UNITY_LOOP
                for (int i = 0; i < taps; i++)
                {
                    float along = i + jitter;
                    float known;
                    float seen = Visible(input.uv + hop * along, veil, known);
                    float reach = exp(-falloff * stride * along) * stride;
                    float weight = reach * known;
                    sum += seen * weight;
                    total += weight;
                    possible += reach;
                    lastSeen = seen;
                    lastKnown = known;
                }
                // Past the frame's edge, what was last seen is taken to carry on: a bank of cloud
                // at the edge of the picture usually does.
                float reached = span * inFrame;
                float beyond = (exp(-falloff * reached) - exp(-falloff * span)) / max(1e-4, falloff) * lastKnown;
                sum += lastSeen * beyond;
                total += beyond;
                // With most of the way hidden behind something near, the few taps left decide the
                // answer and the jitter decides which taps they are: beside a box that was a grid of
                // dots in the cloud. The less of the way is known, the more the pixel's own sky
                // stands in — open sky is lit, cloud is not — which is smooth and nearly always right.
                float knownHere;
                float here = Visible(input.uv, veil, knownHere);
                here = lerp(1.0, here, knownHere);
                float trust = saturate(total / max(1e-5, possible + beyond) * 2.0);
                float lit = lerp(here, total > 1e-5 ? sum / total : here, trust);

                // Forward scattering: brightest looking toward the light, gone at the reach.
                float phase = pow(saturate(1.0 - spread * spread), 3.0);
                // No silhouettes in here. How much air stands in front of a pixel is a hard edge at
                // every object, and this buffer is half the screen's resolution: kept here, the edge
                // came back up as stair-steps of light spilt onto the object and notches cut in the
                // sky beside it. The composite applies it, at full resolution.
                float glow = _FishGodRayAir.x * phase * framed;
                // r: light the air scatters toward the eye where the sun reaches it.
                // g: the air that would have glowed and is in shadow — the dark lane beside the shaft.
                // The lanes keep clear of the light itself. Round the sun the frame's own glow is the
                // sunrise, and a lane drawn there does not read as a shadow, only as the glow missing.
                // The lane's depth does not follow the glow's falloff all the way: a shadow a long way
                // from the sun is still a shadow, and it is out there, against plain sky, that a
                // shaft is actually seen.
                float laneReach = _FishGodRayAir.x * sqrt(phase) * framed;
                float lane = laneReach * (1.0 - lit) * smoothstep(0.05, 0.25, spread);
                return float4(glow * lit, lane, 0.0, 1.0);
            }
            ENDHLSL
        }

        // ── 5: light the shafts and darken the lanes between them ──
        // The frame is kept by the alpha and added to by the colour. A shaft is seen as much by
        // the shadow beside it as by its own light, and light alone, added to a bright sky, only
        // ever made an even disc round the sun.
        Pass
        {
            Name "CloudGodRayComposite"
            Blend One SrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FishGodRayBuffer);
            SAMPLER(sampler_FishGodRayBuffer);
            float4 _FishGodRayBuffer_TexelSize;
            float4 _FishGodRayColor;    // rgb tint, a intensity
            float4 _FishGodRayLane;     // x how dark a shadowed lane gets, 0..1
            float4 _FishGodRayAir;      // y metres of air that fill a shaft in

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // A tent over three by three of the buffer's texels. The gather is jittered so that a
                // short march shows as grain and not as bands, and the grain has to be taken out
                // here: four taps half a texel apart is only bilinear filtering, and left it in.
                float2 texel = _FishGodRayBuffer_TexelSize.xy;
                float2 rays = 0.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float weight = (2.0 - abs(x)) * (2.0 - abs(y)) / 16.0;
                        rays += SAMPLE_TEXTURE2D_LOD(_FishGodRayBuffer, sampler_FishGodRayBuffer, input.uv + float2(x, y) * texel, 0).rg * weight;
                    }
                }
                // How much air lies in front of this pixel to be lit or shadowed, read at the
                // screen's own resolution so an object's edge stays the object's edge. The sky has
                // all of it; a wall at arm's length has none.
                float rawDepth = SampleSceneDepth(input.uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1e-6;
                #else
                    bool isSky = rawDepth >= 1.0 - 1e-6;
                #endif
                float air = isSky ? 1.0 : 1.0 - exp(-LinearEyeDepth(rawDepth, _ZBufferParams) / max(1.0, _FishGodRayAir.y));
                rays *= air;
                // Rolled off so that however thick the air, the light arrives below 1: the display
                // clips (a tonemapper was tried and measured — URP's Neutral maps 1.0 to 0.63 and
                // took the brightness out of the whole frame), and added raw this reached white
                // across a quarter of the sky.
                float3 light = 1.0 - exp(-rays.r * _FishGodRayColor.rgb * _FishGodRayColor.a);
                // Screened on, not added: the frame gives way to the light by the light's own
                // brightness, so a bright sky does not push it over and the colour of the sunrise
                // survives into the glow instead of going to white. The lanes ride the same factor.
                float under = 1.0 - dot(light, float3(0.2126, 0.7152, 0.0722));
                float keep = under * (1.0 - saturate(rays.g * _FishGodRayLane.x));
                return float4(light, keep);
            }
            ENDHLSL
        }

        // ── 6: soften the shadow and end it at the window's edge ──
        // Last in the file on purpose: the passes are called by number, and anything put before the
        // light shafts would renumber them.
        // The sun is half a degree across, so the shadow of an edge a kilometre up is ten metres
        // soft on the ground; and the window is a square that follows the camera, whose last texel
        // a clamped lookup would otherwise smear out to the horizon as streaks.
        Pass
        {
            Name "CloudShadowSoften"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_FishCloudShadowRaw);
            SAMPLER(sampler_FishCloudShadowRaw);
            float4 _FishCloudShadowRaw_TexelSize;
            float _FishCloudShadowFarDim;    // 1 − what the light gets through the cloud past the window

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 texel = _FishCloudShadowRaw_TexelSize.xy;
                float sum = 0.0;
                float total = 0.0;
                [unroll] for (int x = -2; x <= 2; x++)
                {
                    [unroll] for (int y = -2; y <= 2; y++)
                    {
                        float weight = (3.0 - abs(x)) * (3.0 - abs(y));
                        sum += SAMPLE_TEXTURE2D_LOD(_FishCloudShadowRaw, sampler_FishCloudShadowRaw, input.uv + float2(x, y) * texel, 0).r * weight;
                        total += weight;
                    }
                }
                float shadow = sum / total;
                float2 toEdge = min(input.uv, 1.0 - input.uv);
                float inside = saturate(min(toEdge.x, toEdge.y) / 0.06);
                // Out to the average past the window, not to full sun: the light carries no other
                // dimming for cloud, and the land beyond the window is under the same sky.
                return lerp(1.0 - _FishCloudShadowFarDim, shadow, inside);
            }
            ENDHLSL
        }


        // ── 7: what cloud stands over each patch of ground ──
        // Appended last, like everything after the shafts: the passes are called by number.
        // A top-down window round the camera, each texel one ray straight up through the same
        // volume the sky draws, carrying how much cloud it met (0 clear sky, 1 solid). Rain and
        // snow read it to fall only under cloud: they used to fall from the whole sky at once,
        // wherever the deck's gaps were, and a storm cell's rain stayed on after the cell had
        // drifted past because the amount was one number for the scene.
        Pass
        {
            Name "CloudOverhead"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "FishCloudVolume.hlsl"

            float4 _FishCloudOverheadDraw;   // xy the window's centre (world xz), z its size (m), w march steps
            float _FishCloudOverheadFrom;    // the height the columns are measured up from: the viewer's (m)

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 xz = _FishCloudOverheadDraw.xy + (input.uv - 0.5) * _FishCloudOverheadDraw.z;
                /* From the viewer's height, not the sea's: what can fall on you is the cloud ABOVE
                 * you. Measured from sea level, a deck below a mountain top still counted as cloud
                 * overhead and it rained up there out of a clear sky. Inside a deck the column starts
                 * where you stand; above its top it is empty. */
                float3 origin = float3(xz.x, _FishCloudOverheadFrom, xz.y);
                float texel = _FishCloudOverheadDraw.z / 256.0;
                float density = FishCloudShadowDepth(origin, float3(0.0, 1.0, 0.0), (int)max(2.0, _FishCloudOverheadDraw.w), 0.5, texel);
                return 1.0 - exp(-density);
            }
            ENDHLSL
        }
        // ── 8: the light volume: the cloud's density on a coarse grid round the camera, for the light march ──
        // Drawn into a band of tiles at a time (CloudLightVolume), each tile one slice of the grid; every pixel is the
        // field's own density at its texel's middle, so the light march's long segments read the same cloud.
        Pass
        {
            Name "CloudLightVolume"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "FishCloudVolume.hlsl"

            float4 _FishCloudLightBuildA;   // xy the grid's corner (world xz), z a texel (m), w texels a side
            float4 _FishCloudLightBuildB;   // x its floor (world y), y a slice (m), z slices, w tiles across

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            float Frag(Varyings input) : SV_Target
            {
                float n = _FishCloudLightBuildA.w;
                float2 pixel = floor(input.positionCS.xy);
                float2 tile = floor(pixel / n);
                float slice = tile.y * _FishCloudLightBuildB.w + tile.x;
                if (slice >= _FishCloudLightBuildB.z)
                {
                    return 0.0;
                }
                float2 inTile = pixel - tile * n + 0.5;
                float3 position = float3(_FishCloudLightBuildA.x + inTile.x * _FishCloudLightBuildA.z,
                    _FishCloudLightBuildB.x + (slice + 0.5) * _FishCloudLightBuildB.y,
                    _FishCloudLightBuildA.y + inTile.y * _FishCloudLightBuildA.z);
                float2 axis = FishCloudAxis();
                FishCloudField field = FishCloudFieldAt(position.xz - axis * FishCloudLean(position.y));
                float high01;
                return FishCloudDensity(position, 0.0, _FishCloudLightBuildA.z, field, false, high01);
            }
            ENDHLSL
        }

        // ── 9: the field's tiles: the tower lattice (x 0) or the formation noise (x 1) over one period of it ──
        // Drawn once a world (CloudFieldTiles), read with a wrapping fetch in place of working either out per sample.
        Pass
        {
            Name "CloudFieldTile"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "FishCloudVolume.hlsl"

            float4 _FishCloudTileBuild;   // x which tile (0 tower, 1 formation noise), y texels a side

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            float Frag(Varyings input) : SV_Target
            {
                float2 uv = (floor(input.positionCS.xy) + 0.5) / max(1.0, _FishCloudTileBuild.y);
                if (_FishCloudTileBuild.x < 0.5)
                {
                    return FishCloudTowerAnalytic(uv * _FishCloudColumn.y * max(1.0, _FishCloudColumn.z));
                }
                return FishCloudMesoNoiseAnalytic(uv * _FishCloudMesoParams.y * max(1.0, _FishCloudMesoParams.z));
            }
            ENDHLSL
        }

        // ── 10: the far band, in four slices, one marched a frame ──
        // Two thirds of a fair sky's march was cloud past ten kilometres (ScenePerfProbe 2026-10-07), and the march is
        // as slow as its longest rays: marching a quarter of the texels in turn saved nothing, since each still walked
        // the whole far band. So the far band is cut into four slices by distance (_FishCloudFarBounds) and every texel
        // marches ONE of them a frame, in turn — every ray a quarter as long, and all the GPU's threads in step. Each
        // slice is kept with the view it was drawn with (_FishCloudFarVP) and fetched back along the texel's direction
        // (at infinity: a camera moving at 50 m/s shifts a cloud five kilometres off by under a pixel in three frames);
        // where it cannot be — off that view's screen, its ray now ending elsewhere — it is marched at once. A slice
        // behind a near band and nearer slices that already let nothing through is not marched at all. Writes near and
        // far together, which the steadying reads in place of pass 0's march. Jim, 2026-10-07: distant clouds may be
        // softer for this.
        Pass
        {
            Name "CloudFarBand"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishCloudVolume.hlsl"

            float4 _FishCloudMarchParams;
            float4 _FishCloudJitter;
            float4x4 _FishCloudInverseVP;
            float4 _FishCloudOptions;
            float4 _FishCloudFarBand;           // x where the far band begins (m), z the slice marched this frame
            float4 _FishCloudFarBounds;         // x, y, z where slices 1, 2, 3 begin (m); slice 3 runs to the ray's end
            float4 _FishCloudFarValid;          // 1 for each slice kept from an earlier frame
            float4x4 _FishCloudFarVP[4];        // the view each slice was drawn with
            TEXTURE2D(_FishCloudFar0); TEXTURE2D(_FishCloudFar1); TEXTURE2D(_FishCloudFar2); TEXTURE2D(_FishCloudFar3);
            TEXTURE2D(_FishCloudFarInfo0); TEXTURE2D(_FishCloudFarInfo1); TEXTURE2D(_FishCloudFarInfo2); TEXTURE2D(_FishCloudFarInfo3);
            TEXTURE2D(_FishCloudNear);          // pass 0's near band: rgb, a transmittance
            TEXTURE2D(_FishCloudNearMotion);    // x its mean distance (km), y its wind gain
            SAMPLER(sampler_linear_clamp);
            SAMPLER(sampler_point_clamp);

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // Pass 0's, exactly: the same rays and phases.
            float3 RayFor(float2 uv)
            {
                float4 clip = float4(uv * 2.0 - 1.0, 1.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                float4 world = mul(_FishCloudInverseVP, clip);
                return normalize(world.xyz / world.w - _WorldSpaceCameraPos.xyz);
            }

            float Jitter(float2 pixel, float frame)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(pixel + frame * 5.588238, magic.xy)));
            }

            struct FarOutput
            {
                float4 clouds : SV_Target0;     // near and far together: rgb, a transmittance
                float2 motion : SV_Target1;     // x their mean distance (km), y their mean wind gain
                float4 slice : SV_Target2;      // this frame's slice: rgb, a transmittance
                float4 info : SV_Target3;       // x its mean distance (km), y its wind gain, z where the ray ends (km), w 1 when real
            };

            float4 FarSlice(int j, float2 uv)
            {
                return j == 0 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFar0, sampler_linear_clamp, uv, 0)
                    : j == 1 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFar1, sampler_linear_clamp, uv, 0)
                    : j == 2 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFar2, sampler_linear_clamp, uv, 0)
                    : SAMPLE_TEXTURE2D_LOD(_FishCloudFar3, sampler_linear_clamp, uv, 0);
            }

            float4 FarSliceInfo(int j, float2 uv)
            {
                return j == 0 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFarInfo0, sampler_point_clamp, uv, 0)
                    : j == 1 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFarInfo1, sampler_point_clamp, uv, 0)
                    : j == 2 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFarInfo2, sampler_point_clamp, uv, 0)
                    : SAMPLE_TEXTURE2D_LOD(_FishCloudFarInfo3, sampler_point_clamp, uv, 0);
            }

            // Whether a kept slice holds for this ray: it was real, and either its ray ended where this one does or
            // both ran past the slice's far end — the depth then had no say in it.
            bool FarSliceHolds(float4 info, float endKm, float toKm)
            {
                return info.w > 0.5 && (abs(info.z - endKm) <= 0.05 * endKm + 0.05 || (info.z >= toKm && endKm >= toKm));
            }

            // A kept slice along this direction: the texel it fell on, or failing that one beside it. The jitter takes
            // the texels at a leaf's edge from sky to leaf and back, and those at a cloud's edge from behind it to clear;
            // marching each such slice again made whole groups of threads wait on the few that did (4.6 % of the sky's
            // pixels cost 2.7 ms in a fair sky). A neighbouring direction stands for a cloud kilometres off.
            bool FarFetch(int j, float2 thenUV, float endKm, float toKm, out float4 slice, out float2 motion)
            {
                slice = float4(0.0, 0.0, 0.0, 1.0);
                motion = float2(0.0, 0.0);
                float4 info = FarSliceInfo(j, thenUV);
                if (FarSliceHolds(info, endKm, toKm))
                {
                    slice = FarSlice(j, thenUV);
                    motion = info.xy;
                    return true;
                }
                // The four beside it, then the four corners and the four two away: a gap between leaves is often a
                // texel or two of sky with leaf all round it.
                float2 step = 1.0 / max(1.0, _FishCloudJitter.zw);
                float2 offsets[12] = { float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                    float2(1, 1), float2(-1, 1), float2(1, -1), float2(-1, -1),
                    float2(2, 0), float2(-2, 0), float2(0, 2), float2(0, -2) };
                UNITY_LOOP
                for (int n = 0; n < 12; n++)
                {
                    float2 at = thenUV + offsets[n] * step;
                    float4 near = FarSliceInfo(j, at);
                    if (all(at >= 0.0) && all(at <= 1.0) && FarSliceHolds(near, endKm, toKm))
                    {
                        // The texel itself, not a blend that may reach into the one that failed.
                        slice = j == 0 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFar0, sampler_point_clamp, at, 0)
                            : j == 1 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFar1, sampler_point_clamp, at, 0)
                            : j == 2 ? SAMPLE_TEXTURE2D_LOD(_FishCloudFar2, sampler_point_clamp, at, 0)
                            : SAMPLE_TEXTURE2D_LOD(_FishCloudFar3, sampler_point_clamp, at, 0);
                        motion = near.xy;
                        return true;
                    }
                }
                return false;
            }

            FarOutput Frag(Varyings input)
            {
                FarOutput output;
                int2 texel = int2(input.positionCS.xy);
                float4 nearBand = LOAD_TEXTURE2D(_FishCloudNear, texel);
                float2 nearMotion = LOAD_TEXTURE2D(_FishCloudNearMotion, texel).xy;
                output.clouds = nearBand;
                output.motion = nearMotion;
                output.slice = float4(0.0, 0.0, 0.0, 1.0);

                float2 uv = input.uv + _FishCloudJitter.xy / max(1.0, _FishCloudJitter.zw);
                float3 direction = RayFor(uv);
                float rawDepth = SampleSceneDepth(uv);
                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1e-6;
                #else
                    bool isSky = rawDepth >= 1.0 - 1e-6;
                #endif
                float maxDistance = isSky ? _FishCloudMarchParams.w * 4.6 : depth / max(1e-4, dot(direction, -UNITY_MATRIX_V[2].xyz));
                float endKm = maxDistance * 0.001;
                output.info = float4(0.0, 0.0, endKm, 1.0);
                float band = _FishCloudFarBand.x;
                if (band <= 0.0 || maxDistance <= band)
                {
                    return output;
                }

                float jitter = frac(Jitter(input.positionCS.xy, 0.0) + _FishCloudOptions.w);
                jitter = _FishCloudDiag.x > 0.5 ? 0.5 : jitter;
                float2 pixel = input.positionCS.xy;
                float2 lightSeed = frac(float2(frac(dot(pixel, float2(0.7548777, 0.5698403))), Jitter(pixel.yx, 0.0))
                    + _FishCloudOptions.w * float2(1.0, 1.6180340));

                float bounds[5] = { band, _FishCloudFarBounds.x, _FishCloudFarBounds.y, _FishCloudFarBounds.z, 1e9 };
                int marching = (int)_FishCloudFarBand.z;
                float3 light = nearBand.rgb;
                float through = nearBand.a;
                float weight = 1.0 - nearBand.a;
                float2 motionSum = nearMotion * weight;
                int marchedSlices = 0;
                UNITY_LOOP
                for (int j = 0; j < 4; j++)
                {
                    float from = bounds[j];
                    float to = min(maxDistance, bounds[j + 1]);
                    float4 slice = float4(0.0, 0.0, 0.0, 1.0);
                    float2 sliceMotion = float2(0.0, 0.0);
                    bool fresh = false;
                    if (to > from)
                    {
                        bool kept = false;
                        if (j != marching && _FishCloudFarValid[j] > 0.5)
                        {
                            float4 then = mul(_FishCloudFarVP[j], float4(direction, 0.0));
                            float2 thenUV = then.xy / max(1e-5, then.w) * 0.5 + 0.5;
                            #if UNITY_UV_STARTS_AT_TOP
                                thenUV.y = 1.0 - thenUV.y;
                            #endif
                            if (then.w > 1e-5 && all(thenUV >= 0.0) && all(thenUV <= 1.0))
                            {
                                kept = FarFetch(j, thenUV, endKm, to * 0.001, slice, sliceMotion);
                            }
                        }
                        // Marched: this frame's slice, or one that could not be fetched — unless nothing of it would
                        // get through what lies in front. (Probe diagnostics, `cloud:fieldskip`: 262144 marches nothing,
                        // what the pass costs besides; 524288 marches only this frame's slice, to time one alone.)
                        bool probeSkip = _FishCloudFieldSkip == 262144.0 || (_FishCloudFieldSkip == 524288.0 && j != marching);
                        if (!kept && through > 0.02 && !probeSkip)
                        {
                            float cloudDistance;
                            float2 cloudMotion;
                            FishCloudMarchFrom = from;
                            FishCloudMarchWhole = maxDistance;
                            slice = FishCloudMarch(_WorldSpaceCameraPos.xyz, direction, to, jitter, lightSeed,
                                (int)_FishCloudMarchParams.x, _FishCloudMarchParams.y, cloudDistance, cloudMotion);
                            FishCloudMarchFrom = 0.0;
                            FishCloudMarchWhole = -1.0;
                            sliceMotion = float2(cloudMotion.x * 0.001, cloudMotion.y);
                            fresh = true;
                            marchedSlices++;
                        }
                    }
                    if (j == marching)
                    {
                        output.slice = slice;
                        // Not marched (behind what is opaque, or past the ray's end): not to be fetched back as real.
                        output.info = float4(sliceMotion, endKm, fresh || to <= from ? 1.0 : 0.0);
                    }
                    float added = through * (1.0 - slice.a);
                    light += through * slice.rgb;
                    motionSum += sliceMotion * added;
                    weight += added;
                    through *= slice.a;
                }
                output.clouds = float4(light, through);
                output.motion = weight > 1e-5 ? motionSum / weight : nearMotion;
                // Probe diagnostic (`cloud:fieldskip=131072`): how many slices this texel marched this frame — green one,
                // yellow two, red three or four, blue none.
                if (_FishCloudFieldSkip == 131072.0)
                {
                    output.clouds = marchedSlices == 0 ? float4(0.0, 0.0, 1.0, 0.0) : marchedSlices == 1 ? float4(0.0, 1.0, 0.0, 0.0)
                        : marchedSlices == 2 ? float4(1.0, 1.0, 0.0, 0.0) : float4(1.0, 0.0, 0.0, 0.0);
                }
                return output;
            }
            ENDHLSL
        }

    }
    Fallback Off
}
