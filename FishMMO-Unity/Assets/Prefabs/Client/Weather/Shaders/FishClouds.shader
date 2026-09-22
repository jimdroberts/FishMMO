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

            float4 _FishCloudMarchParams;   // x steps, y detail amount, z frame index, w max distance
            float4x4 _FishCloudInverseVP;

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

            float4 Frag(Varyings input) : SV_Target
            {
                float3 direction = RayFor(input.uv);
                float rawDepth = SampleSceneDepth(input.uv);
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

                float jitter = Jitter(input.uv * _ScreenParams.xy, _FishCloudMarchParams.z);
                // The world is fogged by the pipeline already; the volume's ground fog is for the sky.
                FishCloudGroundFogScale = isSky ? 1.0 : 0.0;
                float cloudDistance;
                float4 result = FishCloudMarch(_WorldSpaceCameraPos.xyz, direction, maxDistance, jitter,
                    (int)_FishCloudMarchParams.x, _FishCloudMarchParams.y, cloudDistance);
                // rgb scattered light, a transmittance. The distance rides along for reprojection.
                return float4(result.rgb, result.a);
            }
            ENDHLSL
        }

        // ── 1: steady it against the last frame ──
        Pass
        {
            Name "CloudTemporal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_FishCloudCurrent);
            SAMPLER(sampler_FishCloudCurrent);
            TEXTURE2D(_FishCloudHistory);
            SAMPLER(sampler_FishCloudHistory);
            float4 _FishCloudCurrent_TexelSize;
            float4x4 _FishCloudPreviousVP;
            float4x4 _FishCloudInverseVP;
            float4 _FishCloudTemporal;      // x blend, y valid history, z far distance

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
                float4 current = SAMPLE_TEXTURE2D(_FishCloudCurrent, sampler_FishCloudCurrent, input.uv);
                if (_FishCloudTemporal.y < 0.5)
                {
                    return current;
                }
                // Clouds are far away, so reprojecting a point on the shell is close enough: take a
                // direction, push it out to the layer, and ask where it was on the last frame.
                float4 clip = float4(input.uv * 2.0 - 1.0, 1.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                float4 world = mul(_FishCloudInverseVP, clip);
                float3 direction = normalize(world.xyz / world.w - _WorldSpaceCameraPos.xyz);
                // "point" is a reserved word in HLSL.
                float3 onLayer = _WorldSpaceCameraPos.xyz + direction * _FishCloudTemporal.z;
                float4 previous = mul(_FishCloudPreviousVP, float4(onLayer, 1.0));
                float2 previousUV = previous.xy / max(1e-5, previous.w) * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    previousUV.y = 1.0 - previousUV.y;
                #endif
                if (any(previousUV < 0.0) || any(previousUV > 1.0))
                {
                    return current;
                }

                float4 history = SAMPLE_TEXTURE2D(_FishCloudHistory, sampler_FishCloudHistory, previousUV);
                // Clamp the history to what this frame's neighbourhood allows, or a turning camera
                // smears the clouds.
                float4 low = current, high = current;
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    [unroll] for (int y = -1; y <= 1; y++)
                    {
                        float4 tap = SAMPLE_TEXTURE2D(_FishCloudCurrent, sampler_FishCloudCurrent,
                            input.uv + float2(x, y) * _FishCloudCurrent_TexelSize.xy);
                        low = min(low, tap);
                        high = max(high, tap);
                    }
                }
                history = clamp(history, low, high);
                return lerp(current, history, _FishCloudTemporal.x);
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
            float4 _FishCloudBuffer_TexelSize;

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
                // Four taps, and each weighed by whether it is looking at the same thing this pixel
                // is. The clouds are marched at two fifths of the screen and every ray stops at the
                // world, so a low-resolution texel that straddles the edge of a post holds the sky
                // ray's answer — a long march full of scattered light — while the pixels of the post
                // itself hold almost none. Averaged blindly, that light was smeared back over the
                // post: a white rim around everything, brightest against a dark scene. Comparing
                // depths keeps each pixel to the taps that belong to it.
                float2 texel = _FishCloudBuffer_TexelSize.xy * 0.5;
                float here = LinearEyeDepth(SampleSceneDepth(input.uv), _ZBufferParams);
                float4 sum = 0.0;
                float total = 0.0;
                [unroll] for (int t = 0; t < 4; t++)
                {
                    float2 at = input.uv + float2(t == 0 || t == 2 ? -texel.x : texel.x, t < 2 ? -texel.y : texel.y);
                    float there = LinearEyeDepth(SampleSceneDepth(at), _ZBufferParams);
                    // Both far away is both sky, whatever the numbers say.
                    float weight = saturate(1.0 - abs(there - here) / max(12.0, here * 0.2));
                    weight = max(weight, saturate(min(there, here) / 4000.0));
                    sum += SAMPLE_TEXTURE2D(_FishCloudBuffer, sampler_FishCloudBuffer, at) * weight;
                    total += weight;
                }
                return total > 1e-3
                    ? sum / total
                    : SAMPLE_TEXTURE2D(_FishCloudBuffer, sampler_FishCloudBuffer, input.uv);
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
                // A real optical depth, so the shadow of a heap is hard-edged and dark and the
                // shadow of a wisp is faint — nothing softened, nothing floored. The strength is
                // how dark the ground may go: at 1 a solid cloud's shadow is black and the ambient
                // term is all that lights it, which is what a cloud shadow on a sunny day is.
                float through = exp(-density);
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
                return lerp(1.0, shadow, inside);
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
                float3 origin = float3(xz.x, 0.0, xz.y);
                float texel = _FishCloudOverheadDraw.z / 256.0;
                float density = FishCloudShadowDepth(origin, float3(0.0, 1.0, 0.0), (int)max(2.0, _FishCloudOverheadDraw.w), 0.5, texel);
                return 1.0 - exp(-density);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
