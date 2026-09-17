// The FishMMO sky: atmosphere colours from the sky profile, suns with halos, the star field
// and Milky Way turning with the body, three cloud layers fed by the weather, aurora, rainbow
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
                    color += sc * glow * 0.35 * (1.0 - airless);
                    color += sc * HenyeyGreenstein(cosSun, 0.78) * _FishSunColor[i].a * 0.08 * sunUp * (1.0 - airless * 0.8);
                    float angle = acos(clamp(cosSun, -1.0, 1.0));
                    float radius = max(_FishSunDir[i].w, 0.002);
                    float disc = 1.0 - smoothstep(radius * 0.92, radius, angle);
                    float limb = sqrt(saturate(1.0 - pow(angle / radius, 2.0)));
                    float cover = (i == 0) ? eclipse : 0.0;
                    sunsLight += sc * disc * (0.6 + 0.4 * limb) * 18.0 * (1.0 - cover) * saturate(up * 40.0 + 1.0);
                }
                color *= lerp(1.0, 0.2, eclipse);

                // Stars and the Milky Way, dimmed by the sky and the clouds.
                float starVisibility = _FishSkyParams.x * saturate(up * 8.0 + 0.2);
                if (starVisibility > 0.001)
                {
                    float3 sd = mul((float3x3)_FishStarMatrix, dir);
                    float3 stars = SAMPLE_TEXTURECUBE_LOD(_FishStarCube, sampler_FishStarCube, sd, 0).rgb;
                    float twinkle = 1.0 + _FishSkyParams.z * (Hash31(floor(sd * 400.0) + floor(_FishWeatherMisc.w * 6.0)) - 0.5) * (1.0 - h);
                    float3 galacticPole = normalize(float3(0.46, -0.88, 0.12));
                    float band = exp(-pow(dot(sd, galacticPole), 2.0) * 30.0);
                    float2 mwUv = float2(atan2(sd.z, sd.x) * 0.32, sd.y * 0.9);
                    float dust = FishNoise(mwUv, 1) * FishNoise(mwUv * 2.7 + 0.3, 0);
                    float3 milkyWay = float3(0.55, 0.58, 0.72) * band * saturate(dust * 2.2 - 0.2) * _FishSkyParams.y * 0.35;
                    color += (stars * twinkle * 2.0 + milkyWay) * starVisibility;
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

                // Clouds cover everything above.
                float4 clouds = FishSkyClouds(dir, _WorldSpaceCameraPos.xyz);
                color = color * (1.0 - clouds.a) + clouds.rgb;
                sunsLight *= (1.0 - saturate(clouds.a * 1.6));

                // Rainbow: 42° from the point opposite the sun, while it rains and the sun is low.
                if (_FishRainbow.x > 0.001)
                {
                    float angle = degrees(acos(clamp(dot(dir, -_FishSunDir[0].xyz), -1.0, 1.0)));
                    float band = saturate(1.0 - abs(angle - 41.5) / 1.8);
                    color += Hue((angle - 39.7) / 3.6 * 0.8) * band * band * _FishRainbow.x * 0.25 * saturate(up * 10.0);
                }

                // Lightning brightens the whole sky a little.
                color += _FishWeatherCloud.w * float3(0.35, 0.37, 0.42) * (1.0 - airless);

                color += sunsLight;

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
