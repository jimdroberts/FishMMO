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
                float maxDistance = isSky ? _FishCloudMarchParams.w : depth / max(1e-4, dot(direction, -UNITY_MATRIX_V[2].xyz));

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
                // A small upscale filter: four taps, so the low-resolution buffer does not show its
                // own pixels along a cloud edge.
                float2 texel = _FishCloudBuffer_TexelSize.xy * 0.5;
                float4 a = SAMPLE_TEXTURE2D(_FishCloudBuffer, sampler_FishCloudBuffer, input.uv + float2(-texel.x, -texel.y));
                float4 b = SAMPLE_TEXTURE2D(_FishCloudBuffer, sampler_FishCloudBuffer, input.uv + float2(texel.x, -texel.y));
                float4 c = SAMPLE_TEXTURE2D(_FishCloudBuffer, sampler_FishCloudBuffer, input.uv + float2(-texel.x, texel.y));
                float4 d = SAMPLE_TEXTURE2D(_FishCloudBuffer, sampler_FishCloudBuffer, input.uv + float2(texel.x, texel.y));
                float4 cloud = (a + b + c + d) * 0.25;
                return cloud;
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
            float4 _FishGodRayParams;   // x decay, y how sharply a shaft narrows, z reach (uv), w taps
            float4 _FishGodRayMask;     // x dark enough to block, y how soft that edge is
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

            // How much of the light gets past this pixel: nothing through the world, which has
            // depth, and only what the clouds let through where they stand. This is the shaft
            // itself — it does not depend on how bright the frame happens to be.
            float Visible(float2 uv)
            {
                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1e-6;
                #else
                    bool isSky = rawDepth >= 1.0 - 1e-6;
                #endif
                if (!isSky)
                {
                    return 0.0;
                }
                // The cloud buffer's alpha is transmittance: 1 clear sky, 0 solid cloud.
                float clear = SAMPLE_TEXTURE2D_LOD(_FishGodRayClouds, sampler_FishGodRayClouds, uv, 0).a;
                // A body in front of the light blocks it too, and a body has no depth to be found
                // by: it is simply dark against a bright sky. This is what puts the rays around an
                // eclipsing body instead of through it.
                float3 colour = SAMPLE_TEXTURE2D_LOD(_FishGodRaySource, sampler_FishGodRaySource, uv, 0).rgb;
                float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
                float lit = smoothstep(_FishGodRayMask.x, _FishGodRayMask.x + max(0.01, _FishGodRayMask.y), luminance);
                return clear * lit;
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

                // March from this pixel toward the light, gathering what is still shining through.
                int taps = (int)max(4.0, _FishGodRayParams.w);
                float2 toLight = light - input.uv;
                // Aspect-correct, so the reach is a circle on screen and not an ellipse.
                float aspect = _ScreenParams.x / max(1.0, _ScreenParams.y);
                float spread = length(float2(toLight.x * aspect, toLight.y)) / max(1e-4, _FishGodRayParams.z);
                if (spread > 1.0)
                {
                    return 0.0;      // too far from the light for a shaft to reach
                }
                float2 step = toLight / taps;
                float sum = 0.0;
                float total = 0.0;
                float weight = 1.0;
                float2 uv = input.uv;
                UNITY_LOOP
                for (int i = 0; i < taps; i++)
                {
                    uv += step;
                    sum += Visible(uv) * weight;
                    total += weight;
                    weight *= _FishGodRayParams.x;
                }
                // Fades out with distance from the light, so the shafts have an end, and it takes
                // a good run of clear line of sight to make one.
                float shaft = sum / max(1e-4, total);
                shaft = pow(saturate(shaft), max(1.0, _FishGodRayParams.y));
                return float4(shaft.xxx * (1.0 - spread * spread), 1.0);
            }
            ENDHLSL
        }

        // ── 5: add the shafts to the frame ──
        Pass
        {
            Name "CloudGodRayComposite"
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_FishGodRayBuffer);
            SAMPLER(sampler_FishGodRayBuffer);
            float4 _FishGodRayBuffer_TexelSize;
            float4 _FishGodRayColor;    // rgb tint, a intensity

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
                float2 texel = _FishGodRayBuffer_TexelSize.xy * 0.5;
                float3 a = SAMPLE_TEXTURE2D(_FishGodRayBuffer, sampler_FishGodRayBuffer, input.uv + float2(-texel.x, -texel.y)).rgb;
                float3 b = SAMPLE_TEXTURE2D(_FishGodRayBuffer, sampler_FishGodRayBuffer, input.uv + float2(texel.x, -texel.y)).rgb;
                float3 c = SAMPLE_TEXTURE2D(_FishGodRayBuffer, sampler_FishGodRayBuffer, input.uv + float2(-texel.x, texel.y)).rgb;
                float3 d = SAMPLE_TEXTURE2D(_FishGodRayBuffer, sampler_FishGodRayBuffer, input.uv + float2(texel.x, texel.y)).rgb;
                float3 rays = (a + b + c + d) * 0.25;
                return float4(rays * _FishGodRayColor.rgb * _FishGodRayColor.a, 1.0);
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

    }
    Fallback Off
}
