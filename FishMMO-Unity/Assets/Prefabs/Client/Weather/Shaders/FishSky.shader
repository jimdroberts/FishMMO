// The FishMMO sky: atmosphere colours from the sky profile, suns with halos, the star field
// and Milky Way turning with the body, aurora, rainbow
// and lightning flash, melting into the fog at the horizon. Moons, planets, comets and meteors
// are drawn by FishMMO/Sky Body on top. No compute; runs on WebGPU, WebGL2 and desktop.
Shader "FishMMO/Sky"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishSkyCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = TransformObjectToWorldDir(input.positionOS.xyz, false);
                return output;
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float3 Hue(float h)
            {
                return saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float up = dir.y;
                float3 zenith = _FishSkyZenith.rgb;
                float3 horizon = _FishSkyHorizon.rgb;
                float eclipse = _FishSkyEclipse.x;
                float airless = _FishSkyEclipse.z;

                // Overcast greys the sky.
                float overcast = saturate(_FishWeatherCloud.x * _FishWeatherCloud.y);
                float3 grey = dot(horizon, float3(0.3, 0.5, 0.2)) * float3(0.85, 0.88, 0.92);
                zenith = lerp(zenith, grey * 0.9, overcast * 0.7);
                horizon = lerp(horizon, grey, overcast * 0.7);

                // Atmosphere.
                float h = saturate(up);
                float3 color = lerp(horizon, zenith, pow(h, 0.5));
                color = lerp(color, _FishSkyGround.rgb, saturate(-up * 5.0));

                // The ring of the world underfoot, asked about once: how much of this direction it covers
                // and what light comes off it. What is BEHIND the ring — the stars, the galaxy, the
                // sun's disc — is dimmed by it below; what is in FRONT of it, the air's own glow and
                // the aurora, is not.
                float3 ownRingLight;
                float ownRing = FishOwnRing(dir, ownRingLight);

                // Suns: glow near the horizon toward them, a halo, and the disc.
                float3 sunsLight = 0;
                int count = (int)_FishSunCount;
                for (int i = 0; i < min(count, 4); i++)
                {
                    float3 sd = _FishSunDir[i].xyz;
                    float3 sc = _FishSunColor[i].rgb;
                    float cosSun = dot(dir, sd);
                    float sunUp = saturate(sd.y * 3.0 + 0.4);
                    float glow = pow(saturate(cosSun * 0.5 + 0.5), 5.0) * saturate(1.0 - abs(up) * 2.5) * saturate(1.0 - abs(sd.y) * 4.0);
                    color += sc * glow * _FishSunShape.z * (1.0 - airless);
                    // The glare: the air scattering the sun forward at you. During an eclipse it has
                    // to come from the part of the sun that is still showing and no other, or the sky
                    // glows round a disc that is nine tenths hidden and the crescent has no glare of
                    // its own. Cheaply: the point of the sun's disc nearest this pixel is what mostly
                    // lights the air here, so ask whether THAT point is behind the covering body,
                    // and give the rest a share in proportion to how much of the sun is uncovered.
                    // It came out as a glow hugging the crescent, a thin ring at an annular eclipse,
                    // and nothing at totality, where the corona takes over. Mostly the sun's own
                    // glare, then, and only shaped by the eclipse, which is what a glare is.
                    float3 sunRight = normalize(cross(sd, abs(sd.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0)));
                    float3 sunUpAxis = cross(sd, sunRight);
                    float glareShape = 1.0;
                    if (i == 0 && _FishSkyEclipseCover.x > 0.001 && _FishSkyEclipseBody.w > 0.0)
                    {
                        float2 offset = float2(dot(dir, sunRight), dot(dir, sunUpAxis));
                        float away = length(offset);
                        float sunR = max(_FishSunDir[0].w, 0.002);
                        float2 nearestOnSun = offset / max(away, 1e-6) * min(away, sunR);
                        float2 bodyAt = float2(dot(_FishSkyEclipseBody.xyz, sunRight), dot(_FishSkyEclipseBody.xyz, sunUpAxis));
                        // Soft-edged: a hard test on the body's limb cut the glare into a wedge with
                        // straight sides. The transition is a third of the SUN's radius wide, which is
                        // about the width of the crescent it is standing in for.
                        float hidden = 1.0 - smoothstep(_FishSkyEclipseBody.w - sunR * 0.35, _FishSkyEclipseBody.w + sunR * 0.35, length(nearestOnSun - bodyAt));
                        glareShape = 0.3 * (1.0 - _FishSkyEclipseCover.x) + 0.7 * (1.0 - hidden);

                        // The crescent's own glare. The everyday halo above peaks at about a seventh
                        // and is dimmed with the rest of the sky, which beside a crescent drawn at
                        // eighteen is nothing to see. This is the glare a thin, hard-bright edge of
                        // sun throws in a darkened sky: tight round the uncovered part, brightest at
                        // its edge, and standing out MORE as the day gets darker — it is added to the
                        // sun's own light below, which the darkening does not touch.
                        float fromSun = max(0.0, away - sunR);
                        // And rounder: the fan fades across as well as out, so it is a glow and not a beam.
                        float crescentGlare = exp(-fromSun / max(0.0005, sunR * 0.5)) * 0.6 + exp(-fromSun / max(0.0005, sunR * 1.6)) * 0.4;
                        crescentGlare *= 0.55 + 0.45 * saturate(dot(normalize(offset), normalize(bodyAt - nearestOnSun + 1e-6)) * -1.0);
                        float showing = 1.0 - hidden;
                        float darkened = 0.35 + 0.65 * _FishSkyEclipse.x;
                        float onlyInEclipse = smoothstep(0.15, 0.5, _FishSkyEclipseCover.x) * (1.0 - _FishSkyEclipse.w);
                        sunsLight += sc * crescentGlare * showing * darkened * onlyInEclipse * 2.2 * saturate(up * 40.0 + 1.0);
                    }
                    color += sc * HenyeyGreenstein(cosSun, 0.78) * _FishSunColor[i].a * 0.08 * sunUp * glareShape * (1.0 - airless * 0.8);
                    float angle = acos(clamp(cosSun, -1.0, 1.0));
                    float radius = max(_FishSunDir[i].w, 0.002);
                    float disc = 1.0 - smoothstep(radius * 0.92, radius, angle);
                    float limb = sqrt(saturate(1.0 - pow(angle / radius, 2.0)));
                    // The disc is not dimmed by the eclipse: the body in front of it is drawn as a
                    // silhouette, and what is left showing is the sun at its own brightness — the
                    // bite. Dimmed as well, the crescent faded to nothing before it was covered. Only
                    // in the last of totality, when the silhouette's edge and the disc's edge are one
                    // pixel apart, is it taken down, so the corona is not fighting a rim of disc.
                    float totality = (i == 0) ? _FishSkyEclipse.w : 0.0;
                    sunsLight += sc * disc * (0.6 + 0.4 * limb) * _FishSunShape.y * (1.0 - totality) * saturate(up * 40.0 + 1.0);

                    if (i == 0 && _FishSkyEclipseBody.w > 0.0 && _FishSkyEclipseCover.x > 0.001)
                    {
                        float ring = _FishSkyEclipseBody.w;
                        float3 bodyDir = _FishSkyEclipseBody.xyz;
                        float bodyAngle = acos(clamp(dot(dir, bodyDir), -1.0, 1.0));
                        float outsideBody = smoothstep(ring * 0.995, ring * 1.005, bodyAngle);
                        float dark = smoothstep(0.55, 0.95, _FishSkyEclipse.x);

                        // The corona: the SUN's own outer atmosphere, centred on the sun and reaching
                        // beyond its disc. Seen only against a dark sky and never through the body in
                        // front, so a same-sized moon shows it on one side going in, all round at
                        // totality, and on the other side coming out. A body much bigger than the sun
                        // hides the whole of it, which is right: there is no corona to be seen from a
                        // moon when its planet crosses the sun.
                        float beyond = max(0.0, angle - radius);
                        // Streamers. A corona is not a smooth glow: it is combed out into rays and
                        // plumes by the sun's field, brighter and longer at its equator. Two octaves
                        // of noise round the sun, stretched along the radius so each streamer runs
                        // outward, over a smooth inner glow that the streamers stand out of.
                        // Round the sun as 0..1, then whole numbers of the noise's tiles, so there is
                        // no seam where the angle wraps.
                        float theta = atan2(dot(dir, sunUpAxis), dot(dir, sunRight)) / (2.0 * PI) + 0.5;
                        float radial = beyond / max(0.0005, radius);
                        float streamers = FishNoise(float2(theta * 3.0, radial * 0.4), 0) * 0.6 + FishNoise(float2(theta * 8.0 + 0.37, radial * 0.7 + 0.5), 1) * 0.4;
                        streamers = 0.45 + 1.1 * saturate(streamers * 1.6 - 0.4);
                        float corona = exp(-beyond / max(0.0005, radius * 0.25)) * 0.5
                            + exp(-beyond / max(0.0005, radius * 0.9)) * 0.5 * streamers;
                        sunsLight += sc * corona * 2.5 * dark * outsideBody * saturate(up * 40.0 + 1.0);

                        // Baily's beads. The last of the sun going in, and the first coming out, is
                        // not a smooth sliver: it shows through the valleys of the covering body's
                        // limb as a string of points, and a single last bead with the corona already
                        // out round it is the diamond ring. The limb's roughness is noise round the
                        // body, fine enough to be beads and not a bite.
                        float behindSun = 1.0 - smoothstep(radius * 0.96, radius * 1.02, angle);
                        float atLimb = exp(-max(0.0, bodyAngle - ring) / max(0.0005, ring * 0.03));
                        float3 bodyRight = normalize(cross(bodyDir, abs(bodyDir.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0)));
                        float limbTheta = atan2(dot(dir, cross(bodyDir, bodyRight)), dot(dir, bodyRight)) / (2.0 * PI) + 0.5;
                        float valleys = saturate(FishNoise(float2(limbTheta * 24.0, 0.5), 2) * 2.2 - 0.9);
                        float beads = behindSun * atLimb * valleys * smoothstep(0.35, 0.9, _FishSkyEclipse.x);
                        sunsLight += sc * beads * 8.0 * outsideBody * saturate(up * 40.0 + 1.0);

                        // The covering body's own lit rim is the body's, and is drawn on its quad by
                        // the sky-body shader, where it can sit on top of the silhouette.
                    }
                }
                // As dark as it looks, not as dark as it is: an eye adapts, and a half-covered sun is
                // an ordinary day. The plunge is in the last tenth, and totality has already turned
                // the sky's own colours to twilight before this is applied.
                color *= lerp(1.0, 0.45, eclipse);

                // Stars and the Milky Way, dimmed by the sky.
                float starVisibility = _FishSkyParams.x * saturate(up * 8.0 + 0.2);
                if (starVisibility > 0.001)
                {
                    float3 sd = mul((float3x3)_FishStarMatrix, dir);
                    float3 stars = SAMPLE_TEXTURECUBE_LOD(_FishStarCube, sampler_FishStarCube, sd, 0).rgb;
                    float twinkle = 1.0 + _FishSkyParams.z * (Hash31(floor(sd * 400.0) + floor(_FishWeatherMisc.w * 6.0)) - 0.5) * (1.0 - h);
                    float3 milkyWay;
                    if (_FishGalaxyParams.x > 0.5)
                    {
                        // The sky's own galaxy, along the same direction the stars use so the two
                        // turn together. The brightness slider scales it, as it does the band below.
                        milkyWay = SAMPLE_TEXTURECUBE_LOD(_FishGalaxyCube, sampler_FishGalaxyCube, sd, 0).rgb * _FishSkyParams.y;
                    }
                    else
                    {
                        float3 galacticPole = normalize(float3(0.46, -0.88, 0.12));
                        float band = exp(-pow(dot(sd, galacticPole), 2.0) * 30.0);
                        // The dust is taken from the direction itself, in the galaxy's own frame. It
                        // used to come from atan2(sd.z, sd.x), which wraps at ±π and drew a hard seam
                        // straight across the sky, while the cylindrical mapping pinched at the poles
                        // into a fan of straight streaks — together they read as a torn grey sheet
                        // hanging over the stars rather than as a galaxy.
                        float3 galacticX = normalize(cross(galacticPole, float3(0.0, 0.0, 1.0)));
                        float3 galacticY = cross(galacticPole, galacticX);
                        float across = dot(sd, galacticPole);
                        float2 mwUv = float2(dot(sd, galacticX), dot(sd, galacticY));
                        float dust = FishNoise(mwUv * 5.0 + across * 2.0, 1)
                                   * FishNoise(mwUv * 11.0 - across * 3.0 + 0.3, 0);
                        milkyWay = float3(0.55, 0.58, 0.72) * band * saturate(dust * 2.2 - 0.2) * _FishSkyParams.y * 0.35;
                    }
                    color += (stars * twinkle * 2.0 + milkyWay) * starVisibility * (1.0 - ownRing);
                }

                // Aurora curtains.
                if (_FishAuroraParams.x > 0.001 && up > 0.02)
                {
                    float3 p = dir / max(up, 0.05);
                    float t = _FishAuroraParams.y;
                    float3 aurora = 0;
                    for (int layer = 0; layer < 4; layer++)
                    {
                        float height = 1.0 + layer * 0.18;
                        float2 q = p.xz * (0.25 / height);
                        float wave = FishNoise(q * 0.35 + float2(t * 0.004, layer * 0.13), 0);
                        float curtain = exp(-pow((frac(q.x * 0.6 + wave * 2.5 + t * 0.01) - 0.5) * 6.0, 2.0));
                        float shimmer = FishNoise(float2(q.x * 3.0 - t * 0.05, layer * 0.3), 2);
                        float3 tint = lerp(_FishAuroraA.rgb, _FishAuroraB.rgb, layer / 3.0);
                        aurora += tint * curtain * shimmer * (1.0 - layer * 0.2);
                    }
                    color += aurora * _FishAuroraParams.x * saturate(up * 3.0) * 0.6;
                }

                // No clouds here: they are a volume, marched after the sky and composited over it.

                // Rainbow: 42° from the point opposite the sun, while it rains and the sun is low.
                if (_FishRainbow.x > 0.001)
                {
                    float angle = degrees(acos(clamp(dot(dir, -_FishSunDir[0].xyz), -1.0, 1.0)));
                    float band = saturate(1.0 - abs(angle - 41.5) / 1.8);
                    color += Hue((angle - 39.7) / 3.6 * 0.8) * band * band * _FishRainbow.x * 0.25 * saturate(up * 10.0);
                }

                // Lightning brightens the whole sky a little.
                color += _FishWeatherCloud.w * float3(0.35, 0.37, 0.42) * (1.0 - airless);

                // The ring of the world underfoot, over everything that is behind it. By night the lit
                // ring is as bright as a moon; by day the air in front of it is brighter than it is,
                // and it shows as a moon does at noon — pale, and only where it outshines the sky.
                color = lerp(color, max(color, ownRingLight), ownRing);

                // The sun's disc is a great deal further off than the ring, and goes behind it.
                color += sunsLight * (1.0 - ownRing);

                // Melt into the fog at the horizon, so terrain and sky meet without a seam.
                float fogBand = saturate(1.0 - up * 7.0) * _FishSkyFogColor.a;
                color = lerp(color, _FishSkyFogColor.rgb, fogBand);

                return half4(color * _FishSkyParams.w, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
