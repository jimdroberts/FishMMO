// What floats in the air round the camera (AirMotesField): dust in the light, pollen and seed fluff,
// diamond dust, fireflies and marsh lights. A pre-built field of quads per kind that the vertex shader
// places, moves and lights from each particle's seed and the shared motion clock; nothing is simulated
// and nothing is stored, so it runs the same on WebGPU, WebGL2 and desktop, and every player sees the
// same mote in the same place.
Shader "FishMMO/Weather/Air Motes"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            // After the water's surfaces (Transparent-100 .. -90) and the plumes, before what falls: a
            // mote over a pond is drawn over the pond, and the rain in front of it over the mote.
            "Queue" = "Transparent+45"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "AirMotes"
            Tags { "LightMode" = "UniversalForward" }
            // Premultiplied: a scattering mote covers what is behind it by its opacity (rgb·a, a), a
            // glowing one only adds its light (rgb, 0) — one pass for both.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            // The cloud shadow on the sun (CloudShadowPresenter), read in the vertex stage as the rain does.
            #pragma multi_compile _ _LIGHT_COOKIES
            // The sun's shadow, so the motes in a forest are seen in its sun patches and not in its shade.
            // Not the screen-space variant: a mote has no depth of its own in that texture.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWeather.hlsl"
            #include "FishAmbient.hlsl"
            #include "FishFogLayer.hlsl"

            // The fine ground round the camera (MistGroundMap; FishMist.hlsl reads the same): r the ground
            // above _FishMistGroundBase (m; over open water, the water's surface), g the hollow (m), b how
            // near open water is (0..1), a the tree canopy (0..1). Declared here rather than by including
            // FishMist.hlsl, which brings the whole mist march with it.
            TEXTURE2D(_FishMistGround);
            SAMPLER(sampler_FishMistGround);
            float4 _FishMistGroundRect;
            float _FishMistGroundBase;

            // Per draw, from a MaterialPropertyBlock (AirMotesField.Render).
            float4 _MoteOrigin;  // xyz camera position, w the ground under the camera (m): a ground band's last resort
            float4 _MoteBox;     // xyz the box it wraps in round the camera (m), w 1 for a band over the ground, 0 a box round the camera
            float4 _MoteBand;    // x, y the heights over the ground a band flies at (m), z, w the distances it fades out over (m)
            float4 _MoteTravel;  // x, z metres the air has carried it (wrapped by the box), y metres the typical particle has settled (wrapped at box.y · 64)
            float4 _MoteShape;   // x share of particles shown, y typical size (m), z largest (m), w the kind (AirMoteKind)
            float4 _MoteFall;    // x slowest, y fastest settling against the typical, z smallest size (m), w the ground's say in the count: 1 fireflies, 2 wisps, 0 none
            float4 _MoteWander;  // x its wander (m), y slowest, z fastest pace (rad/s), w the vertical share
            float4 _MoteColor;   // rgb its colour (albedo, or the colour of its light), a single-scatter albedo × extinction efficiency
            float4 _MotePhase;   // x the share in the sharp forward lobe, y its g, z the broad lobe's g, w the share lit as an opaque grain
            float4 _MoteGlow;    // x the light it gives (linear, at its own disc; 0: it only scatters), y shortest, z longest cycle (s), w lit share of a cycle
            float4 _MoteHalo;    // x a glow's halo radius against its core, y the halo's share of its light, z the glint cone's cosine, w the glint's peak (0: no glint)
            float4 _MoteExtra;   // x pollen's seed-fluff share, y its size (m), z how much faster it settles, w the sub-pixel dot's sigma (px)

            #define MOTE_DUST 0
            #define MOTE_POLLEN 1
            #define MOTE_ICE 2
            #define MOTE_FIREFLY 3
            #define MOTE_WISP 4

            struct Attributes
            {
                float3 seed : POSITION;     // particle position in the unit box, shared by its 4 corners
                float2 corner : TEXCOORD0;  // 0/1 corner of the quad
                float4 random : TEXCOORD1;  // x show threshold, y size, z pace, w phase
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 quad : TEXCOORD0;        // xy the corner, -1..1; z pixels per unit of xy; w 1 for light it gives, 0 for light it scatters
                float4 spot : TEXCOORD1;        // x the core's sigma (px), y the halo's (px), z the core's own area (px²), w the halo's share of its light
                float4 sunLight : TEXCOORD2;    // rgb the sun it scatters, before the sun's shadow; w the shadow's distance fade
                float4 light : TEXCOORD3;       // rgb everything else: the sky it scatters and the light it gives, through the fog; w its weight
                float4 shadowCoord : TEXCOORD4; // where its centre is in the sun's shadow map
                float2 fluff : TEXCOORD5;       // x how much of a seed's filaments are drawn (0 a dot), y their turn
                float fogCoord : TEXCOORD6;
            };

            // A 32-bit integer hash (Wellons' lowbias32): what a wisp's new place is drawn from each time
            // it comes alight. Integer, so it is the same on every GPU, which sin-based hashes are not.
            uint MoteHash(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }

            float MoteRandom(uint x)
            {
                return (float)(MoteHash(x) >> 8) * (1.0 / 16777216.0);
            }

            // The rain's twins (FishPrecipitation.shader): the cloud shadow on the sun at a point, and how
            // many metres a pixel spans there.
            float3 MoteSunCookie(float3 positionWS)
            {
                #if defined(_LIGHT_COOKIES)
                    if (IsMainLightCookieEnabled())
                    {
                        float2 uv = ComputeLightCookieUVDirectional(_MainLightWorldToLight, positionWS, float4(1.0, 1.0, 0.0, 0.0), URP_TEXTURE_WRAP_MODE_NONE);
                        float4 c = SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture, sampler_MainLightCookieTexture, uv, 0);
                        return IsMainLightCookieTextureRGBFormat() ? c.rgb : (IsMainLightCookieTextureAlphaFormat() ? c.aaa : c.rrr);
                    }
                #endif
                return float3(1.0, 1.0, 1.0);
            }

            float MotePixelMetres(float3 positionWS)
            {
                float focal = max(abs(UNITY_MATRIX_P._m11), 1e-4) * max(_ScreenParams.y, 1.0) * 0.5;
                float depth = max(-TransformWorldToView(positionWS).z, 1e-3);
                return unity_OrthoParams.w > 0.5 ? 1.0 / focal : depth / focal;
            }

            // Henyey–Greenstein: the share of scattered light sent at an angle, per steradian.
            float MoteHG(float cosTheta, float g)
            {
                float d = max(1e-4, 1.0 + g * g - 2.0 * g * cosTheta);
                return (1.0 - g * g) / (4.0 * PI * d * sqrt(d));
            }

            // PrecipitationField.GrainSun: a Lambertian sphere's light over its disc, against a white card.
            float MoteGrainSun(float cosPhase)
            {
                float alpha = acos(clamp(cosPhase, -1.0, 1.0));
                return (2.0 / 3.0) * (sin(alpha) + (PI - alpha) * cos(alpha)) / PI;
            }

            /* The ground under a point, m, and what it is: the fine mist map where it reaches (the true
             * ground, the water's surface over water, and how near water and how closed the canopy are),
             * else the sky-occlusion map's highest surface (canopies and roofs as well), else the ground
             * under the camera. `what` is (hollow, water, canopy, known). */
            float MoteGround(float2 xz, out float4 what)
            {
                what = float4(0.0, 0.0, 0.0, 0.0);
                if (_FishMistGroundRect.w > 0.5)
                {
                    float2 uv = (xz - _FishMistGroundRect.xy) / max(1.0, _FishMistGroundRect.z);
                    if (all(uv > 0.0) && all(uv < 1.0))
                    {
                        float4 g = SAMPLE_TEXTURE2D_LOD(_FishMistGround, sampler_FishMistGround, uv, 0);
                        what = float4(g.y, g.z, g.w, 1.0);
                        return g.x + _FishMistGroundBase;
                    }
                }
                if (_FishOcclusionRange.z > 0.5)
                {
                    float2 uv = (xz - _FishOcclusionRect.xy) / max(_FishOcclusionRect.zw, 1e-3);
                    if (all(uv > 0.0) && all(uv < 1.0))
                    {
                        return FishSkyOcclusionHeight(float3(xz.x, 0.0, xz.y));
                    }
                }
                return _MoteOrigin.w;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float time = _FishWeatherMisc.w;
                int kind = (int)round(_MoteShape.w);
                float3 box = max(_MoteBox.xyz, 0.5);
                bool band = _MoteBox.w > 0.5;
                bool glows = _MoteGlow.x > 0.0;
                uint id = MoteHash(asuint(input.seed.x) ^ MoteHash(asuint(input.seed.z) ^ MoteHash(asuint(input.seed.y))));
                float3 seed = input.seed;

                /* Its light's cycle: a firefly's flash, a wisp's life. Each its own length, snapped so the
                 * motion clock's wrap holds whole cycles (FishWrapCycles), so it never jumps at the wrap. */
                float lit = 1.0;
                float flashing = 0.0;
                if (glows)
                {
                    float rate = FishWrapCycles(1.0 / lerp(_MoteGlow.y, _MoteGlow.z, input.random.z));
                    float cycles = time * rate + input.random.w;
                    float index = floor(cycles);
                    float u = cycles - index;
                    float share = max(_MoteGlow.w, 1e-3);
                    float on = saturate(u / share);
                    float pulse = sin(PI * on);
                    lit = u < share ? pulse * pulse : 0.0;
                    flashing = on;
                    if (kind == MOTE_WISP)
                    {
                        /* A wisp comes alight somewhere new each time: its place is drawn afresh for each
                         * cycle, at the cycle's dark end, where it cannot be seen to move. And it flickers,
                         * as a cold flame does, by a fifth. */
                        uint life = id ^ MoteHash((uint)index + 0x9e3779b9u);
                        seed = float3(MoteRandom(life), MoteRandom(life ^ 0x68e31da4u), MoteRandom(life ^ 0xb5297a4du));
                        lit *= 0.8 + 0.2 * sin(time * FishWrapAngular(2.3 + 1.7 * input.random.y) + 6.2831853 * input.random.x);
                    }
                }

                // Seed fluff among the pollen: bigger, whiter, settling faster.
                bool fluff = kind == MOTE_POLLEN && MoteRandom(id ^ 0x1b873593u) < _MoteExtra.x;

                /* How big this one is: an exponential spread about the typical, between the smallest and
                 * the largest, as dust and crystals come — many small, a few large. A lantern or a flame
                 * is within a fifth of its kind's. */
                float own;
                if (glows)
                {
                    own = _MoteShape.y * lerp(0.8, 1.2, input.random.y);
                }
                else if (fluff)
                {
                    own = _MoteExtra.y * lerp(0.6, 1.3, input.random.y);
                }
                else
                {
                    float e = -log(max(1.0 - input.random.y, 1e-4));
                    own = min(_MoteShape.z, _MoteFall.z + max(0.0, _MoteShape.y - _MoteFall.z) * e);
                }

                /* Where it is. Carried by the surface air, wrapped in a box that follows the camera but
                 * stands in the world, so walking does not drag the motes along. Each settles at its own
                 * speed, a whole number of 64ths of its kind's, which is what lets the settling be
                 * wrapped (PrecipitationField.Advance says why). A band's height is from the ground under
                 * it instead, mostly low. */
                float speed = round(lerp(_MoteFall.x, _MoteFall.y, input.random.z) * (fluff ? _MoteExtra.z : 1.0) * 64.0) / 64.0;
                float3 travelled = float3(_MoteTravel.x, band ? 0.0 : -_MoteTravel.y * speed, _MoteTravel.z);
                float3 local = frac(seed + (travelled - _MoteOrigin.xyz) / box) * box - box * 0.5;
                float3 centre = _MoteOrigin.xyz + local;

                /* Its own wander: a slow figure of eights, its own pace on each axis, each snapped to whole
                 * turns of the motion clock's wrap. For what the air carries it is the turbulence near
                 * the ground; for what flies, its flight. */
                float paceX = FishWrapAngular(lerp(_MoteWander.y, _MoteWander.z, input.random.z));
                float paceY = FishWrapAngular(lerp(_MoteWander.y, _MoteWander.z, frac(input.random.z + input.random.w)));
                float paceZ = FishWrapAngular(lerp(_MoteWander.y, _MoteWander.z, input.random.w));
                float3 wander = float3(sin(time * paceX + 6.2831853 * input.random.w),
                    _MoteWander.w * sin(time * paceY + 6.2831853 * input.random.y),
                    cos(time * paceZ + 6.2831853 * input.random.x)) * _MoteWander.x;
                centre += wander;

                float local01 = 1.0;
                if (band)
                {
                    float4 what;
                    float ground = MoteGround(centre.xz, what);
                    float height = lerp(_MoteBand.x, _MoteBand.y, pow(seed.y, 1.6));
                    // A firefly climbs as it flashes: the J of Photinus's courtship flight.
                    if (kind == MOTE_FIREFLY)
                    {
                        height += 0.35 * flashing * flashing;
                    }
                    centre.y = ground + height + wander.y;
                    /* Where the fine ground map says what the ground is, it has a say in the count:
                     * fireflies keep to the edges of woods and to water, wisps to water and hollows. */
                    if (what.w > 0.5)
                    {
                        local01 = _MoteFall.w < 1.5
                            ? saturate(0.35 + 0.65 * saturate(1.5 * max(what.y, what.z)))
                            : saturate(1.5 * what.y + what.x / 3.0);
                    }
                }

                float visible = saturate((_MoteShape.x * local01 - input.random.x) * 25.0);
                float3 toCamera = _WorldSpaceCameraPos.xyz - centre;
                float distance = length(toCamera);
                float3 view = toCamera / max(distance, 1e-3);

                /* Drawn as a soft dot that keeps its light (PrecipitationField.SubPixelAlpha): a Gaussian
                 * whose spread is its own size's convolved with the sub-pixel floor, carrying its own
                 * disc's area. A mote of a tenth of a millimetre a metre off covers a hundredth of a
                 * pixel and is drawn over three of them a hundredth as strong in sum; a lantern near the
                 * eye is a soft blob its own size. A glow adds a wide faint halo. */
                float pixel = MotePixelMetres(centre);
                float corePx = own / pixel;
                float floorSigma = max(_MoteExtra.w, 0.25);
                float coreSigma = sqrt(corePx * corePx / (2.355 * 2.355) + floorSigma * floorSigma);
                float haloPx = glows ? corePx * _MoteHalo.x : 0.0;
                float haloSigma = glows ? sqrt(haloPx * haloPx / (2.355 * 2.355) + floorSigma * floorSigma) : 0.0;
                float quadPx = 6.0 * max(coreSigma, haloSigma) * step(0.001, visible);
                float size = quadPx * pixel;

                float3 side = normalize(cross(float3(0.0, 1.0, 0.0), view) + float3(1e-4, 0.0, 0.0));
                float3 up = cross(view, side);
                float3 worldPos = centre + (side * (input.corner.x - 0.5) + up * (input.corner.y - 0.5)) * size;

                // Nearest a quarter of a metre (the near plane), out over the outer share of its box; and
                // never under the sea's surface, which this would show through.
                float fadeIn = saturate((distance - 0.25) / 0.25);
                float fadeOut = saturate((distance - _MoteBand.z) / max(1e-3, _MoteBand.w - _MoteBand.z));
                float weight = sin(0.5 * PI * fadeIn) * cos(0.5 * PI * fadeOut) * visible * FishWaterOpen(centre);

                /* Its light, in the units URP lights a surface in: a white card facing the sun shows the
                 * main light's colour, so the sun's irradiance is π times it. A mote sends the eye
                 * E·ωQ·p(θ) over its disc — its single-scatter albedo times its extinction efficiency
                 * (_MoteColor.a) times its phase function at the scattering angle — which toward the sun
                 * is tens of times a white card and with the sun behind the eye a few hundredths of it:
                 * the beam's motes blaze and the rest of the air shows none. The sky round it, which
                 * comes from all over, it scatters by ωQ alone. Lit by the sun through the cloud over it
                 * (its cookie), and its shadow is read per pixel below. */
                Light mainLight = GetMainLight();
                float3 sun = mainLight.color * MoteSunCookie(centre);
                // The scattering angle: between the sunlight's way (−L) and the way on to the eye; 1 with the sun straight behind the mote.
                float cosScatter = dot(-mainLight.direction, view);
                float3 sunTerm = float3(0.0, 0.0, 0.0);
                float3 skyTerm = float3(0.0, 0.0, 0.0);
                float3 tint = fluff ? float3(0.96, 0.96, 0.93) : _MoteColor.rgb;
                float wq = _MoteColor.a;
                if (_MoteHalo.w > 0.0)
                {
                    /* Diamond dust: a mirror. Two thirds are plates falling face-down, rocking a few
                     * degrees either way as they fall (the sun pillar's crystals); the rest tumble
                     * freely. A face throws a glint to the eye while its normal is within the glint's
                     * cone of the half-way vector between the sun and the eye; the glint is the sun's
                     * disc at ice's reflectance, spread over that cone (AirMotesField.GlintPeak). */
                    float3 h = normalize(mainLight.direction + view + float3(0.0, 1e-4, 0.0));
                    float rock = 0.12 + 0.2 * input.random.y;
                    float3 normal;
                    if (frac(input.random.w * 3.7) < 0.67)
                    {
                        normal = normalize(float3(rock * sin(time * paceX * 6.0 + 6.2831853 * input.random.x), 1.0,
                            rock * sin(time * paceZ * 6.0 + 6.2831853 * input.random.y)));
                    }
                    else
                    {
                        float a = time * paceX * 4.0 + 6.2831853 * input.random.x;
                        float b = time * paceZ * 3.0 + 6.2831853 * input.random.y;
                        normal = float3(cos(a) * sin(b), cos(b), sin(a) * sin(b));
                    }
                    float facing = saturate((abs(dot(normal, h)) - _MoteHalo.z) / max(1e-5, 1.0 - _MoteHalo.z));
                    // Squared over the cone, three times its mean: the cone holds the peak's light on average.
                    sunTerm = _MoteHalo.w * 3.0 * facing * facing * step(0.0, mainLight.direction.y + 0.02);
                    skyTerm = wq * max(0.0, FishTrilight(-view));
                }
                else if (!glows)
                {
                    float sharp = _MotePhase.x;
                    float phase = sharp * MoteHG(cosScatter, _MotePhase.y) + (1.0 - sharp) * MoteHG(cosScatter, _MotePhase.z);
                    if (fluff)
                    {
                        // A pappus is a tuft of fine fibres: all forward scatter, the seed lit against the sun.
                        phase = MoteHG(cosScatter, 0.85);
                    }
                    float grain = fluff ? 0.0 : _MotePhase.w;
                    float scattered = PI * wq * phase;
                    float faced = MoteGrainSun(dot(view, mainLight.direction));
                    sunTerm = tint * lerp(scattered, faced, grain);
                    skyTerm = tint * max(0.0, lerp(wq * FishTrilight(-view), FishTrilight(view), grain));
                }
                float3 emitted = glows ? _MoteColor.rgb * _MoteGlow.x * lit : float3(0.0, 0.0, 0.0);

                /* The fog between it and the eye, as the rain has it: the fog layer's own transmittance
                 * over the path and, for what scatters, the fog's light in place of what it hides. Only
                 * the fireflies and the wisps are far enough for it to matter. */
                float through = 1.0;
                float3 inFog = float3(0.0, 0.0, 0.0);
                if (_FishFogLayerShape.w > 0.5 && _FishFogLayer.x > 0.0)
                {
                    FishFogColumn column = FishFogColumnAt(centre.xz);
                    float3 eye = _WorldSpaceCameraPos.xyz;
                    through = exp(-_FishFogLayer.x * FishFogPath(eye.y, -view.y, 0.0, distance, column));
                    if (!glows)
                    {
                        inFog = FishFogLight(0.5 * (eye.y + centre.y), -view, column, _FishFogLayer.x, FishTerrainSunlit(centre), FishFogSunShare(centre)) * (1.0 - through);
                    }
                }

                output.positionCS = TransformWorldToHClip(worldPos);
                output.quad = float4(input.corner * 2.0 - 1.0, 0.5 * quadPx, glows ? 1.0 : 0.0);
                float area = 0.25 * PI * corePx * corePx;
                output.spot = float4(coreSigma, haloSigma, area, glows ? _MoteHalo.y : 0.0);
                output.sunLight = float4(sun * sunTerm * through, GetMainLightShadowFade(centre));
                output.light = float4((skyTerm + emitted) * through + inFog, weight);
                output.shadowCoord = TransformWorldToShadowCoord(centre);
                output.fluff = float2(fluff ? saturate((corePx - 4.0) / 8.0) : 0.0, 6.2831853 * input.random.x);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Its coverage at this pixel: its disc's area gathered into the Gaussian dot, opaque at most.
                float d = length(input.quad.xy) * input.quad.z;
                float core = input.spot.z * exp(-d * d / (2.0 * input.spot.x * input.spot.x)) / (2.0 * PI * input.spot.x * input.spot.x);
                float cover = min(1.0, core);
                if (input.fluff.x > 0.0)
                {
                    // Near enough to see a seed's fibres: nine of them, their light no more in sum (the mean of the lobes is 0.82).
                    float angle = atan2(input.quad.y, input.quad.x);
                    float lobes = pow(0.5 + 0.5 * cos(9.0 * angle + input.fluff.y), 4.0);
                    cover *= lerp(1.0, (0.6 + 0.8 * lobes) / 0.82, input.fluff.x);
                }
                if (input.spot.w > 0.0)
                {
                    float halo = input.spot.w * input.spot.z * exp(-d * d / (2.0 * input.spot.y * input.spot.y)) / (2.0 * PI * input.spot.y * input.spot.y);
                    cover += halo;
                }
                cover *= input.light.w;
                if (cover < 1e-4)
                {
                    discard;
                }

                float3 sunLight = input.sunLight.rgb;
                if (max(sunLight.r, max(sunLight.g, sunLight.b)) > 0.0)
                {
                    half shadow = MainLightRealtimeShadow(input.shadowCoord);
                    sunLight *= lerp((float)shadow, 1.0, input.sunLight.w);
                }
                float3 rgb = input.light.rgb + sunLight;
                if (input.quad.w > 0.5)
                {
                    // Light it gives: added, and dimmed by the distance fog, which it does not take the colour of.
                    rgb = MixFogColor(rgb, float3(0.0, 0.0, 0.0), input.fogCoord);
                    return half4((half3)(rgb * cover), 0.0);
                }
                rgb = MixFog(rgb, input.fogCoord);
                float alpha = min(1.0, cover);
                return half4((half3)(rgb * cover), (half)alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
