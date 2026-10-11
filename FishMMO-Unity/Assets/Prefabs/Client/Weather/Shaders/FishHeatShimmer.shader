Shader "Hidden/FishMMO/Weather/HeatShimmer"
{
    // Heat shimmer: the frame displaced by the hot air over sun-baked ground, and the sky's sliver of
    // "water" (the inferior mirage) at the horizon. FishHeatShimmerFeature works out the air (HeatShimmer.cs);
    // this does the geometry, pixel by pixel.
    //
    // PATH-INTEGRATED. A ray is thrown by every eddy it crosses, so the variance of the angle it arrives at is
    // the integral of the refractive turbulence Cn² along it, and Cn² lives in the bottom metre or two over the
    // ground (it falls as height^(-4/3)). A ray that hits the ground at incidence α crosses that layer in
    // depth / sin α metres, or runs inside it all the way when the eye is in it too — so the ground at your
    // feet hardly moves, the flats three hundred metres off boil, and from a hilltop looking down at the same
    // plain nothing much happens, which is all true of the real thing. Jitter is the square root of the path.
    //
    // NEVER PULLS NEAR COLOUR ONTO FAR. With MSAA the depth texture holds one depth a pixel against four colour
    // samples, so an edge pixel's colour is part foreground. The strength is taken at the NEAREST depth round
    // the pixel (a foreground edge is not displaced at all), and the displaced position is accepted only if
    // nothing nearer than the pixel lies there (nearest of a cross of five). Far pixels — past FarAccept, all
    // inside the boiling air — swap freely, so the horizon still wavers into the sky above it.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FishHeatShimmer"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "FishWeather.hlsl"

            float4x4 _HeatInverseVP;   // GPU clip -> world, for the point behind each pixel
            float4x4 _HeatDirectionVP; // world direction -> clip (no translation), for the mirage's mirrored ray
            float4 _HeatParams;  // x jitter rms after the reference path on a still desert noon, scaled by today's air (rad); y the hot layer's depth (m); z the reference path (m); w the eye depth past which everything is inside the boiling air (m)
            float4 _HeatMirage;  // x the critical grazing angle of today's air (rad, widened); y the mirage's strength; z index change per kelvin, widened; w a lava skin's excess (K)
            float4 _HeatNoise;   // x shimmer cells per radian across, y up; z how fast the cells rise (cells/s); w how fast they boil
            float4 _HeatLava;    // x 1 when the scene has lava; y its level (m); z jitter rms over it after the reference path (rad); w how far up its hot air reaches (m)
            float4 _HeatScreen;  // x uv per radian across, y up; z the most displacement allowed (uv, up); w unused
            float4 _HeatEye;     // xyz the camera; w the ground under it (m)

            // The molten mask (WaterSurface / LavaMask), bound only while a lava surface is live.
            TEXTURE2D(_FishLavaMask);
            SAMPLER(sampler_FishLavaMask);
            float4 _FishLavaMaskRect;   // xy world minimum, zw size
            float4 _FishLavaMaskInfo;   // x metres per texel, y 1 when the mask is live

            // World position behind a pixel (FishHeightFog.shader's own, OpenGL's depth range included).
            float3 HeatWorldAt(float2 uv, float rawDepth)
            {
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float4 clip = float4(uv * 2.0 - 1.0, rawDepth, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                float4 world = mul(_HeatInverseVP, clip);
                return world.xyz / world.w;
            }

            bool HeatIsSky(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 1e-7;
                #else
                    return rawDepth >= 1.0 - 1e-7;
                #endif
            }

            // Eye depth, m; the sky is a million metres.
            float HeatEyeDepth(float rawDepth)
            {
                return HeatIsSky(rawDepth) ? 1e6 : LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            // The nearest eye depth on a cross of five round a uv.
            float HeatNearest(float2 uv)
            {
                float2 t = _CameraDepthTexture_TexelSize.xy;
                float d = HeatEyeDepth(SampleSceneDepth(uv));
                d = min(d, HeatEyeDepth(SampleSceneDepth(uv + float2(t.x, 0.0))));
                d = min(d, HeatEyeDepth(SampleSceneDepth(uv - float2(t.x, 0.0))));
                d = min(d, HeatEyeDepth(SampleSceneDepth(uv + float2(0.0, t.y))));
                d = min(d, HeatEyeDepth(SampleSceneDepth(uv - float2(0.0, t.y))));
                return d;
            }

            // ── Noise ──────────────────────────────────────────────────────

            float HeatHash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // Value noise, -1..1 with an rms near 1 (the lattice's uniform values have an rms of 0.58 about the
            // middle; the smooth blend between eight of them about 0.45 of that range).
            float HeatNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(HeatHash(i), HeatHash(i + float3(1, 0, 0)), f.x);
                float b = lerp(HeatHash(i + float3(0, 1, 0)), HeatHash(i + float3(1, 1, 0)), f.x);
                float c = lerp(HeatHash(i + float3(0, 0, 1)), HeatHash(i + float3(1, 0, 1)), f.x);
                float d = lerp(HeatHash(i + float3(0, 1, 1)), HeatHash(i + float3(1, 1, 1)), f.x);
                return (lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z) * 2.0 - 1.0) * 2.2;
            }

            /* The jitter's direction and size along a view direction, unit rms in each axis: two octaves, each
             * rising as the plumes rise (up the screen) and carried across by the wind, the finer one faster and
             * the other way — which, summed, reads as air boiling rather than a pattern sliding. In angle, so it
             * stays put as the camera turns; the noise is of the direction, on the unit sphere, so it has no seam.
             * The clock wraps every FISH_MOTION_WRAP seconds (2.8 h) and the pattern jumps once then: inside
             * boiling air nobody sees it. */
            float2 HeatJitter(float3 d)
            {
                float t = _FishWeatherMisc.w;
                float across = _HeatNoise.x, up = _HeatNoise.y;
                // The wind's drift seen across a few hundred metres, in cells a second.
                float2 drift = _FishWeatherWind.xy * (_FishWeatherWind.z * 30.0 / 200.0) * across;
                float3 q = float3(d.x * across, d.y * up, d.z * across);
                float3 q1 = q - float3(drift.x, -_HeatNoise.z, drift.y) * t + float3(0.0, 0.0, _HeatNoise.w * t * 0.37);
                float3 q2 = q * 2.3 - float3(-0.6 * drift.x, -1.7 * _HeatNoise.z, -0.6 * drift.y) * t + float3(_HeatNoise.w * t * 0.53, 0.0, 0.0) + 17.1;
                float2 n1 = float2(HeatNoise(q1), HeatNoise(q1 + float3(31.7, 11.3, 5.9)));
                float2 n2 = float2(HeatNoise(q2), HeatNoise(q2 + float3(7.3, 23.1, 13.7)));
                return n1 * 0.8 + n2 * 0.6;
            }

            // ── The path through the hot layer ─────────────────────────────

            // How much of a straight ray, from height a above the ground to height b over a length `len`, lies in a
            // layer `depth` deep (heights linear along it).
            float HeatLayerPath(float a, float b, float len, float depth)
            {
                float lo = min(a, b), hi = max(a, b);
                if (lo >= depth)
                {
                    return 0.0;
                }
                if (hi <= depth)
                {
                    return len;
                }
                return len * (depth - lo) / max(1e-4, hi - lo);
            }

            /* The ray's path through a layer `depth` deep, m. On ground-like surfaces from the incidence: the ray
             * runs depth / sin α through the layer over it — or all of its length, if the eye is that low too. On
             * steep ones (a trunk, a wall) only their foot is in the layer, and where the foot is is unknown: the
             * camera's ground stands in for it, never above the surface itself. */
            float HeatPath(float3 p, float distanceToP, float sinIncidence, float groundness, float depth, float ground)
            {
                float onGround = min(distanceToP, depth / max(sinIncidence, 1e-3));
                float base = min(ground, p.y);
                float onSteep = HeatLayerPath(_HeatEye.y - base, p.y - base, distanceToP, depth);
                return lerp(onSteep, onGround, groundness);
            }

            // The molten share of the ground round a point; the whole of it where the mask is not live but lava is.
            float HeatMolten(float3 p)
            {
                if (_HeatLava.x < 0.5)
                {
                    return 0.0;
                }
                float near = step(_HeatLava.y - 2.0, p.y) * saturate(1.0 - (p.y - _HeatLava.y - 1.0) / max(1.0, _HeatLava.w));
                if (near <= 0.0)
                {
                    return 0.0;
                }
                if (_FishLavaMaskInfo.y < 0.5)
                {
                    return near;
                }
                float2 uv = (p.xz - _FishLavaMaskRect.xy) / max(1e-3, _FishLavaMaskRect.zw);
                if (any(uv < 0.0) || any(uv > 1.0))
                {
                    return near;
                }
                float lod = log2(max(1.0, 12.0 / max(1e-3, _FishLavaMaskInfo.x)));
                return near * SAMPLE_TEXTURE2D_LOD(_FishLavaMask, sampler_FishLavaMask, uv, lod).r;
            }

            // The sun's share of the heat a patch of ground keeps against the scene's figure: wet and snow-covered
            // patches spend it on evaporating and melting (HeatShimmer.SensibleHeat's own terms).
            float HeatCoverShare(float3 p)
            {
                float4 here = FishCoverAt(p);
                float local = (1.0 - 0.85 * saturate(here.g)) * (1.0 - saturate(here.r));
                float scene = (1.0 - 0.85 * saturate(_FishWeatherCover.g)) * (1.0 - saturate(_FishWeatherCover.r));
                return clamp(local / max(0.15, scene), 0.0, 1.5);
            }

            float2 HeatScreenOf(float3 direction, out float inFront)
            {
                float4 clip = mul(_HeatDirectionVP, float4(direction, 0.0));
                inFront = step(1e-5, clip.w);
                float2 ndc = clip.xy / max(1e-5, clip.w);
                #if UNITY_UV_STARTS_AT_TOP
                    ndc.y = -ndc.y;
                #endif
                return ndc * 0.5 + 0.5;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 here = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                float2 t = _CameraDepthTexture_TexelSize.xy;
                float raw = SampleSceneDepth(uv);
                bool sky = HeatIsSky(raw);
                float3 eye = _HeatEye.xyz;
                float3 p = HeatWorldAt(uv, raw);
                float3 toP = p - eye;
                float distanceToP = max(1e-3, length(toP));
                float3 d = toP / distanceToP;
                float depthHere = HeatEyeDepth(raw);

                float layer = _HeatParams.y;
                float variance = 0.0;
                float groundness = 0.0;
                float sinIncidence = 1.0;
                float molten = 0.0;
                if (sky)
                {
                    /* The sky just over the horizon is seen past the far ground, through the same air: its rays
                     * pass low over the distant flats. Fades out over a quarter of a degree of elevation. */
                    float elevation = max(0.0, d.y);
                    variance = _HeatParams.x * _HeatParams.x * exp(-elevation / 0.004);
                }
                else
                {
                    /* The surface's normal from the neighbours' points, each axis from the side whose depth is
                     * nearer this pixel's (the other may be across an edge). */
                    float rawR = SampleSceneDepth(uv + float2(t.x, 0.0)), rawL = SampleSceneDepth(uv - float2(t.x, 0.0));
                    float rawU = SampleSceneDepth(uv + float2(0.0, t.y)), rawD = SampleSceneDepth(uv - float2(0.0, t.y));
                    float3 pr = HeatWorldAt(uv + float2(t.x, 0.0), rawR), pl = HeatWorldAt(uv - float2(t.x, 0.0), rawL);
                    float3 pu = HeatWorldAt(uv + float2(0.0, t.y), rawU), pd = HeatWorldAt(uv - float2(0.0, t.y), rawD);
                    float3 dx = abs(rawR - raw) < abs(rawL - raw) ? pr - p : p - pl;
                    float3 dy = abs(rawU - raw) < abs(rawD - raw) ? pu - p : p - pd;
                    float3 n = cross(dy, dx);
                    n = dot(n, n) > 1e-12 ? normalize(n) : float3(0.0, 1.0, 0.0);
                    groundness = smoothstep(0.35, 0.65, abs(n.y));
                    sinIncidence = abs(dot(n, d));

                    float ground = _HeatEye.w;
                    float path = HeatPath(p, distanceToP, sinIncidence, groundness, layer, ground);
                    float cover = HeatCoverShare(p);
                    // Cn² goes as the heat flux to the 4/3.
                    variance = _HeatParams.x * _HeatParams.x * pow(cover, 4.0 / 3.0) * path / _HeatParams.z;

                    molten = HeatMolten(p);
                    if (molten > 0.0)
                    {
                        // Over lava the hot air stands far deeper than the sun's skin layer.
                        float lavaLayer = 0.25 * _HeatLava.w;
                        float lavaPath = HeatPath(p, distanceToP, sinIncidence, groundness, lavaLayer, _HeatLava.y);
                        variance += _HeatLava.z * _HeatLava.z * molten * lavaPath / _HeatParams.z;
                    }

                    // Nothing nearer than this pixel stands round it, or its strength is the nearer thing's:
                    // a foreground edge (half its colour foreground under MSAA) is never moved.
                    float nearest = min(min(min(HeatEyeDepth(rawR), HeatEyeDepth(rawL)), min(HeatEyeDepth(rawU), HeatEyeDepth(rawD))), depthHere);
                    float guard = saturate(nearest / max(1e-3, depthHere));
                    variance *= guard * guard * guard * guard;
                }

                // The inferior mirage: incidence under the critical angle, the hot layer turns the ray back up.
                float critical = max(_HeatMirage.x, sqrt(2.0 * _HeatMirage.z * molten * _HeatMirage.w));
                float mirage = sky ? 0.0 : _HeatMirage.y * groundness * pow(saturate(1.0 - sinIncidence / max(1e-5, critical)), 2.0) * saturate(distanceToP / 30.0);

                if (variance < 1e-12 && mirage <= 0.0)
                {
                    return here;
                }

                // The jitter, in radians: the vertical gradient bends rays up and down more than across.
                float sigma = sqrt(variance);
                float2 jitter = HeatJitter(d) * sigma * float2(0.6, 1.0);
                float limit = _HeatScreen.z / max(1e-5, _HeatScreen.y);
                jitter *= min(1.0, limit / max(1e-9, length(jitter)));
                float2 offset = jitter * _HeatScreen.xy;

                half4 color = here;
                float2 there = uv + offset;
                if (all(there > 0.0) && all(there < 1.0))
                {
                    // Accepted only where nothing nearer than this pixel lies (far pixels trade freely).
                    float reference = min(depthHere, _HeatParams.w);
                    float accept = saturate((HeatNearest(there) / max(1e-3, reference) - 0.7) / 0.15);
                    color = lerp(here, SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, there, 0), (half)accept);
                }

                if (mirage > 0.0)
                {
                    // The ray turned back up: the sky (or the far land) mirrored about the horizontal, boiling too.
                    float inFront;
                    float2 mirrored = HeatScreenOf(float3(d.x, abs(d.y), d.z), inFront) + offset;
                    if (inFront > 0.5 && all(mirrored > 0.0) && all(mirrored < 1.0))
                    {
                        float far = HeatEyeDepth(SampleSceneDepth(mirrored));
                        float open = saturate((far - _HeatParams.w) / _HeatParams.w);
                        half3 reflected = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, mirrored, 0).rgb;
                        color.rgb = lerp(color.rgb, reflected, (half)(saturate(mirage) * open));
                    }
                }
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
