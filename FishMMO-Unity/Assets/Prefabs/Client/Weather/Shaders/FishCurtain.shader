// What a distant storm drops, and the dust a storm's outflow lifts: a volume under a storm cell,
// marched through a box round the cell's footprint, for the nearest cells beyond the particle field.
//
// It was an open cylinder 1300 m tall with streaks scrolled round its wall, the same for every storm
// whatever its shape, standing up from the camera's own height into nothing. Every storm read as a
// straight-sided column — a squall line and a haboob as much as a shower — because that is what it was.
// Now it is the cell's own footprint (a faithful twin of StormCell.Coverage, all four shapes), filled
// with what falls there as thickly as AirPhysics.PrecipitationExtinction says it takes light out, and
// seen through by Beer and Lambert. Rain and snow hang from the cloud base the storm's air gives it
// and are carried off downwind of the storm on their way down; a haboob is a wall of dust as tall as
// the storm's outflow is deep, densest on the ground, with its top boiling up in billows; a dust devil
// is a whirl that leans and wanders and twists as it turns.
//
// The box is drawn from both sides and every pixel keeps the side the ray leaves by, so it works from
// outside and inside alike, and every fragment is pinned to the far plane: a storm is often further
// off than the camera's far clip, and a box cut off there would vanish from the sky. The world in
// front of it stops the march at the scene's depth.
//
// What falls from cloud is lit as the white, lossless scatterer water is, by the day round the storm
// (the light the sky gives the storm's own cloud), and seen through the same air as that cloud. Lit by
// the viewer's ambient straight up and coloured by an albedo of six tenths, with no air in front of
// it, a distant storm's rain drew as a black box under a pale cloud base.
Shader "FishMMO/Weather/Curtain"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // The scene's depth is read in the march, not tested: the box's far side is usually behind
        // the world, and that is exactly where the march has to stop, not whether it runs at all.
        ZTest Always
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            // The cloud shadow cookie on the sun (CloudShadowPresenter): the sun a shaft is lit by is
            // the sun through the cloud in view, which that cookie carries and the light does not.
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // The storms' own cloud (the weather map), which what falls from a storm hangs from.
            #include "FishSkyCommon.hlsl"
            #include "FishAmbient.hlsl"

            // Per cell, from CurtainPresenter.
            float4 _CurtainCentre;  // xyz the cell's centre on the ground (world x, ground height, world z), w how high it stands above the ground (m): the cloud base it falls from, the wall's top, the whirl's
            float4 _CurtainShape;   // xy the way it faces (unit, world x/z; a front's travel), z RadiusMeters, w ExtentMeters
            float4 _CurtainForm;    // x shape (0 disc, 1 front, 2 eyewall, 3 funnel), y style (0 falling, 1 dust wall, 2 dust whirl), z flakes (0 streaks … 1 a mottled veil), w seed 0..1
            float4 _CurtainFall;    // x fall speed (m/s), y time (s), zw how far the shaft's foot is carried from under its top (m, world x/z)
            float4 _CurtainAmount;  // x what falls at its heart now (precipitation × envelope × peak), y water's share of the extinction (rain + 5 snow + ½ hail), z dust's (0.03 ash + 0.04 sand), w forward scatter g
            float4 _CurtainColor;   // rgb the colour of what falls, in this air
            float4 _CurtainBox;     // xy the box's middle (world x/z), zw its half-sizes along the facing and across it (m)
            float4 _CurtainMarch;   // x strides, y the nearest the march starts (m from the camera), z 1 with a noise volume bound, w the shape's own size (m)
            float4 _CurtainStorm;   // x 1 when it falls from a storm's own cloud, which the weather map lays out: it falls only under that cloud, and is lit by it; y 1 when the sky has published the open day's light (SkySystem.CloudsReady)
            TEXTURE3D(_CurtainNoise);
            SAMPLER(sampler_CurtainNoise);

            /* The open day round a storm, as the sky publishes it for its clouds (SkySystem's cloud
             * globals, the ones FishCloudVolume.hlsl lights the storm's own cloud by). A distant storm
             * stands in that day and not in the viewer's: the scene's ambient is the viewer's own sky,
             * dimmed by whatever cloud is over the viewer, and lit by it the rain of a storm twenty
             * kilometres off was as dark as the viewer's shade. */
            float4 _FishCloudAmbient;       // rgb the upper sky's mean radiance: what a white surface facing up shows under it
            float4 _FishCloudHaze;          // rgb the air seen edge-on — the horizon's own light, and the colour distance turns a cloud
            float4 _FishCloudGround;        // w the open ground's albedo (CloudClimate.GroundAlbedo)
            float4 _FishCloudAirExtinction; // xyz the air's molecules' extinction at sea level (1/m, red, green, blue), w their scale height (m)
            float4 _FishCloudAerosol;       // x the haze's extinction at the ground (1/m), y its scale height (m)

            // The share of the light a sunlit storm's rain gets from the sun beside the sky's. Only the
            // brightness: the colour is what falls, and the direction the phase function's.
            // A quarter is single scattering (π times a phase function normalised over the sphere,
            // which _CurtainAmount.w's Henyey–Greenstein carries at 4π); a thick scatterer that loses
            // nothing sends back about as much again by multiple scattering (Chandrasekhar's H
            // functions put a conservative medium's reflection at two to three times its single
            // scattering at middling angles). So a half.
            #define CURTAIN_SUN 0.5
            // How much further than its rain the cloud of a storm with no anatomy of its own reaches
            // (WeatherMap.CloudShieldScale). A storm with one is lit by its cloud as the map lays it out.
            #define CURTAIN_SHIELD 0.55
            // What a storm's cloud lets down of the light on its top, as diffuse light: 1/(1 + ¾(1−g)τ)
            // for τ 200 and g 0.85 (CurtainPresenter.StormCloudThrough, which says where they come from).
            #define CURTAIN_STORM_THROUGH 0.0426
            // The open ground's albedo when the sky has not said (CloudClimate.GroundAlbedo's own
            // grassland figure): only a fallback.
            #define CURTAIN_GROUND_ALBEDO 0.2

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                float4 positionCS = TransformWorldToHClip(output.positionWS);
                // On the far plane, always. A storm eight kilometres off under a five-kilometre far clip
                // would otherwise lose the far side of its box and with it every pixel of the march;
                // only what is behind the camera is still cut away, at the eye.
                #if UNITY_REVERSED_Z
                    positionCS.z = positionCS.w * 1e-6;
                #else
                    positionCS.z = positionCS.w * (1.0 - 1e-6);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            // ── The footprint: StormCell.Coverage, line for line ─────────────────────────────
            // Mathf.InverseLerp: 0 when the ends meet.
            float CurtainInverseLerp(float a, float b, float v)
            {
                return b != a ? saturate((v - a) / (b - a)) : 0.0;
            }

            // How strongly the cell's shape covers a point, given as an offset from its centre. The
            // C# is the truth, and this has to agree with it: what the sky shows falling has to be
            // where the ground gets wet and the player is rained on. Mathf.SmoothStep(0, 1, t) clamps
            // t and is smoothstep(0, 1, t) exactly.
            float CurtainCoverage(float2 offset)
            {
                float radius = _CurtainShape.z;
                float extent = _CurtainShape.w;
                float shape = _CurtainForm.x;
                if (shape > 0.5 && shape < 1.5)
                {
                    // Front: sharp ahead, long tail behind, soft ends.
                    float2 forward = _CurtainShape.xy;
                    float2 along = float2(-forward.y, forward.x);
                    float across = dot(offset, forward);
                    float sideways = abs(dot(offset, along));
                    float reach = across >= 0.0 ? radius * 0.45 : radius * 1.6;
                    float depth = 1.0 - smoothstep(0.0, 1.0, abs(across) / max(1e-3, reach));
                    float halfLength = max(radius, extent);
                    float lengthwise = 1.0 - smoothstep(0.0, 1.0, CurtainInverseLerp(halfLength * 0.75, halfLength, sideways));
                    return depth * lengthwise;
                }
                float fromCentre = length(offset);
                if (shape > 1.5 && shape < 2.5)
                {
                    // Eyewall: quiet but not empty in the eye, worst in the ring, decaying outward.
                    float eye = clamp(extent, 0.0, radius * 0.6);
                    if (fromCentre <= eye)
                    {
                        float t = eye > 1e-3 ? fromCentre / eye : 1.0;
                        return lerp(0.05, 1.0, smoothstep(0.0, 1.0, t));
                    }
                    return 1.0 - smoothstep(0.0, 1.0, CurtainInverseLerp(eye, radius, fromCentre));
                }
                if (shape > 2.5)
                {
                    // Funnel: all of it inside the core, falling away steeply, squared.
                    if (fromCentre <= radius)
                    {
                        return 1.0;
                    }
                    float outerReach = max(extent, radius * 1.5);
                    float falloff = 1.0 - smoothstep(0.0, 1.0, CurtainInverseLerp(radius, outerReach, fromCentre));
                    return falloff * falloff;
                }
                // Disc.
                return 1.0 - smoothstep(0.0, 1.0, CurtainInverseLerp(radius * 0.55, radius, fromCentre));
            }

            // What falling stuff takes out of the view, 1/m, at a precipitation: the twin of
            // AirPhysics.PrecipitationExtinction, with what is falling already folded into y and z.
            // Worked out from the precipitation HERE — the heart's times the coverage — and not
            // scaled down from the heart's extinction: rain thins the view as the 1.26 power of the
            // channel, so a shaft's fringe is thinner than a straight share of its core.
            float CurtainExtinction(float precipitation)
            {
                float p = saturate(precipitation);
                if (p <= 0.0)
                {
                    return 0.0;
                }
                float rate = 50.0 * p * p;
                float rainPerMetre = 0.29e-3 * pow(rate, 0.63);
                return rainPerMetre * _CurtainAmount.y + p * _CurtainAmount.z;
            }

            // ── Noise ─────────────────────────────────────────────────────────────────────
            // The clouds' own shape volume: r Perlin pulled toward Worley (billows), g/b/a Worley at
            // one, two and four times. Tiling, mipmapped, read at the mip a pixel's footprint asks for,
            // so detail finer than the pixel averages away instead of sparkling.
            float CurtainLod(float footprint, float tileMetres)
            {
                return max(0.0, log2(max(1e-4, footprint * 128.0 / tileMetres)));
            }

            float4 CurtainNoise(float3 uvw, float lod)
            {
                if (_CurtainMarch.z < 0.5)
                {
                    return float4(0.5, 0.5, 0.5, 0.5);
                }
                return SAMPLE_TEXTURE3D_LOD(_CurtainNoise, sampler_CurtainNoise, uvw, lod);
            }

            // ── What fills it ─────────────────────────────────────────────────────────────
            // Rain, snow, hail and ash, falling from the cloud base to the ground. `cloudOver` is how
            // much of the storm's own cloud stands over where these drops left the base (the weather
            // map's cover there, 0 where it is not read), which the light has a use for as well.
            float CurtainFalling(float3 position, float footprint, bool fine, out float cloudOver)
            {
                cloudOver = 0.0;
                float3 local = position - _CurtainCentre.xyz;
                float top = _CurtainCentre.w;
                float below = top - local.y;
                if (below <= 0.0)
                {
                    return 0.0;
                }
                /* Carried downwind of the storm on the way down. The drops leave the base moving with
                 * the air that carries the storm, and every metre below it they meet more of the wind
                 * at the ground, which the storm's own gusts drive past it; so the shaft leaves the
                 * cloud upright and bends over toward its foot — a parabola through the depth of the
                 * dry layer, and on past the reference ground into any valley lower than the centre. */
                float descent = min(below / top, 1.5);
                float2 q = local.xz - _CurtainFall.zw * (descent * descent);
                // What falls, falls: the pattern is fixed to the world and carried down at the fall
                // speed, so a shaft's heavier stretches come down it rather than sliding with the camera.
                float fallen = local.y + _CurtainFall.y * _CurtainFall.x;
                float size = max(30.0, _CurtainMarch.w);
                float3 seed = _CurtainForm.w * float3(37.1, 17.3, 23.9);

                // The body: stretches of the shaft heavier and lighter than the rest, and its ragged
                // sides, both a shape's own size across and coming down with the rain.
                float bodyTile = clamp(1.6 * size, 500.0, 5000.0);
                float4 body = CurtainNoise(float3(q.x / bodyTile, fallen / (2.5 * bodyTile), q.y / bodyTile) + seed, CurtainLod(footprint, bodyTile));
                float cover = CurtainCoverage(q + (body.gb - 0.5) * (0.5 * size));
                if (cover <= 0.0)
                {
                    return 0.0;
                }
                float heavy = lerp(0.3, 1.7, body.r);
                /* Only under its cloud. A storm's rain leaves its base where the storm's own cloud
                 * stands, and where the sky draws none — past the far edge of the weather map, off the
                 * fringe of the tower — none falls: rain hanging out of a clear sky is what gave a
                 * distant storm away as a curtain with nothing over it. Asked where the drops left the
                 * base, before the wind carried them. */
                if (_CurtainStorm.x > 0.5)
                {
                    float2 fromCloud = _CurtainCentre.xz + q;
                    cloudOver = saturate(FishWeatherMapAt(fromCloud).r);
                    cover *= smoothstep(0.02, 0.35, cloudOver);
                    if (cover <= 0.0)
                    {
                        return 0.0;
                    }
                }

                // The streaks: columns tens of metres across and hundreds tall, falling with it. Rods
                // for rain and hail; for snow and ash, which drift and tumble, a mottled veil.
                float streak = 1.0;
                if (fine)
                {
                    float flakes = _CurtainForm.z;
                    float across = lerp(900.0, 1400.0, flakes);
                    float tall = lerp(12000.0, 2500.0, flakes);
                    float4 rods = CurtainNoise(float3(q.x / across, fallen / tall, q.y / across) + seed.zxy, CurtainLod(footprint, across));
                    streak = lerp(0.35, 1.65, lerp(rods.b, rods.g, flakes));
                }
                // Thin for the first stretch under the base, where it has only begun to leave the cloud.
                float fade = smoothstep(0.0, 0.12, below / top);
                return CurtainExtinction(_CurtainAmount.x * cover) * heavy * streak * fade;
            }

            // The wall of dust a storm's outflow drives before it: a haboob.
            float CurtainDustWall(float3 position, float footprint, bool fine, out float topHere)
            {
                topHere = 0.0;
                float3 local = position - _CurtainCentre.xyz;
                float wall = _CurtainCentre.w;
                // Below the ground under the centre is still ground-level air to a valley.
                float h = max(0.0, local.y);
                float time = _CurtainFall.y;
                float radius = _CurtainShape.z;
                float3 seed = _CurtainForm.w * float3(37.1, 17.3, 23.9);
                // Which way the dust is being driven: a front's travel; outward, for anything else,
                // since a storm's outflow spreads out all round it.
                bool front = _CurtainForm.x > 0.5 && _CurtainForm.x < 1.5;
                float2 outward = front ? _CurtainShape.xy : (dot(local.xz, local.xz) > 1.0 ? normalize(local.xz) : float2(1.0, 0.0));

                // The head's billows: rising up its face and rolling back over its top, carried with it.
                float tile = clamp(0.6 * wall, 300.0, 1500.0);
                float2 rolled = local.xz + outward * (time * 2.5);
                float3 uvw = float3(rolled.x, h - time * 1.5, rolled.y) / tile + seed;
                float4 billow = CurtainNoise(uvw, CurtainLod(footprint, tile));
                float lumps = billow.r * 0.65 + billow.g * 0.35;
                if (fine)
                {
                    // The cauliflower on the cauliflower.
                    float4 finer = CurtainNoise(uvw * 3.1 + 0.37, CurtainLod(footprint, tile / 3.1));
                    lumps = lumps * 0.75 + finer.r * 0.25;
                }

                /* The face. A density current's head is steep, with its nose at the ground and its top
                 * rounding back over it, so the edge is set back a little the higher it is; and ragged,
                 * pushed about by the same billows. At the ground the edge is exactly the coverage's,
                 * where a player standing in front of it is first in the dust. */
                float rise = saturate(h / max(1.0, wall));
                float2 q = local.xz + outward * (0.12 * radius * rise * rise) + (billow.gb - 0.5) * (0.3 * radius) * rise;
                float cover = CurtainCoverage(q);
                if (cover <= 0.0)
                {
                    return 0.0;
                }
                /* Tallest in the head, right behind the leading edge, and lower in the wake: a density
                 * current's head stands about twice as deep as the current following it. For a front
                 * that is measured back from its face, and the head runs about half the band's depth
                 * before the wake; anything else spreads out all round, and its head is its rim. And
                 * its top is wherever the billows have carried it, which is what makes it a wall and
                 * not a slab. */
                float head = front
                    ? smoothstep(-radius, -0.1 * radius, dot(q, _CurtainShape.xy))
                    : smoothstep(0.0, 0.3, cover);
                topHere = wall * lerp(0.6, 1.0, head) * (0.72 + 0.56 * lumps);
                if (h >= topHere)
                {
                    return 0.0;
                }
                // Densest on the ground and thinning upward, ending in the billows' tops.
                float profile = exp(-h / (0.45 * topHere)) * (1.0 - smoothstep(0.8 * topHere, topHere, h));
                return CurtainExtinction(_CurtainAmount.x * cover) * profile * lerp(0.6, 1.4, lumps);
            }

            // A whirl of dust: a dust devil, or the dust a tornado has lifted.
            float CurtainDustWhirl(float3 position, float footprint, out float topHere)
            {
                float3 local = position - _CurtainCentre.xyz;
                topHere = _CurtainCentre.w;
                float rise = local.y / max(1.0, topHere);
                if (rise < -0.05 || rise >= 1.0)
                {
                    return 0.0;
                }
                rise = saturate(rise);
                float time = _CurtainFall.y;
                float size = max(4.0, _CurtainMarch.w);
                float spin = _CurtainForm.w * 6.2831853;
                // Never upright and never still: its top swings round over its foot.
                float2 axis = (0.35 * size * rise) * float2(sin(time * 0.21 + rise * 2.7 + spin), cos(time * 0.17 + rise * 3.1 + spin * 1.3));
                float2 q = local.xz - axis;
                // Wider aloft, where the whirl weakens and lets its dust spread.
                float cover = CurtainCoverage(q / (1.0 + 2.0 * rise));
                if (cover <= 0.0)
                {
                    return 0.0;
                }
                // Read in a frame that turns with the air and winds tighter up the column, so its dust
                // spirals rather than standing in a tube.
                float angle = time * 0.9 + rise * 5.0 + spin;
                float s = sin(angle), c = cos(angle);
                float2 turned = float2(q.x * c - q.y * s, q.x * s + q.y * c);
                float tile = max(40.0, 1.5 * size);
                float4 n = CurtainNoise(float3(turned.x / tile, (local.y - time * 3.0) / (2.0 * tile), turned.y / tile) + spin, CurtainLod(footprint, tile));
                // Its dust is in the whirl's wall, not spread evenly over what it disturbs: squared.
                float profile = pow(1.0 - rise, 1.5) * smoothstep(-0.05, 0.02, rise);
                return CurtainExtinction(_CurtainAmount.x * cover * cover) * profile * lerp(0.3, 1.7, n.r);
            }

            /* What the box leaves of the curtain: nothing at its sides, all of it well inside. The rain's
             * ragged sides are carried out by the noise and its core may stand off the cell's centre,
             * so without this a shaft reaching a face of the box was cut off square, and the box showed
             * as a translucent block with straight edges against the sky. The box is sized so real rain
             * rarely reaches the fade. */
            float CurtainBoxWindow(float3 position)
            {
                float2 d = position.xz - _CurtainBox.xy;
                float2 forward = _CurtainShape.xy;
                float2 along = float2(-forward.y, forward.x);
                float2 reach = abs(float2(dot(d, forward), dot(d, along))) / max(_CurtainBox.zw, 1.0);
                float2 keep = 1.0 - smoothstep(0.7, 0.98, reach);
                return keep.x * keep.y;
            }

            float CurtainDensityInBox(float3 position, float footprint, bool fine, out float topHere, out float cloudOver)
            {
                topHere = _CurtainCentre.w;
                cloudOver = 0.0;
                if (_CurtainForm.y < 0.5)
                {
                    return CurtainFalling(position, footprint, fine, cloudOver);
                }
                if (_CurtainForm.y < 1.5)
                {
                    return CurtainDustWall(position, footprint, fine, topHere);
                }
                return CurtainDustWhirl(position, footprint, topHere);
            }

            // ── Light ─────────────────────────────────────────────────────────────────────
            // Light that has come through an optical depth of something that scatters most of what it
            // takes: the first pass straight through, and the light scattered on through it again and
            // again, which a dense dust keeps far more of than a single pass would say.
            float CurtainScattered(float tau)
            {
                return (exp(-tau) + 0.5 * exp(-0.25 * tau) + 0.25 * exp(-0.0625 * tau)) / 1.75;
            }

            // Henyey–Greenstein, times 4π: 1 for light scattered evenly every way.
            float CurtainPhase(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / pow(max(1e-4, 1.0 + g2 - 2.0 * g * cosTheta), 1.5);
            }

            // What lights a curtain, worked out once a pixel.
            struct CurtainLights
            {
                float3 toSun;     // toward the main light
                float3 sun;       // its light where the storm stands: through the cloud in view (the cookie), never the light's bare figure
                float3 sky;       // the open sky's mean radiance, overhead of a point in the open
                float3 horizon;   // the open sky low down, which is all of it that shows under a cloud base's edge
                float3 ground;    // what the open ground sends back: its albedo times the sun and sky on it
                float2 toCamera;  // the way back to the camera across the ground, unit
            };

            /* The main light's cookie at a point: the cloud shadow CloudShadowPresenter lays on the sun.
             * URP's own SampleMainLightCookie, read at the top mip so it may be read inside the march.
             *
             * The sun's light is handed over with the cloud in view divided back OUT of it — the cookie
             * carries that, patch by patch, and past its window fades to the average of it. Read bare,
             * the light is the open sun even under a storm. Past the window (every curtain but the
             * nearest) this is that average: the sun through the cloud in view, as the ground there is
             * lit. With no cookie the light already carries the average, and this is 1. */
            float3 CurtainCookie(float3 positionWS)
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

            // The share of the upper sky a point `gap` metres under a cloud base sees open past the
            // base's edge `edge` metres off: the sine of the edge's elevation. Twin of
            // CurtainPresenter.OpenSkyShare.
            float CurtainOpenShare(float gap, float edge)
            {
                return gap / max(1e-3, sqrt(gap * gap + edge * edge));
            }

            // How far off the storm's cloud ends, from its cover here and `reach` metres on: where the
            // cover, running on as it runs between the two, falls through a half. Twin of
            // CurtainPresenter.EdgeDistance.
            float CurtainEdge(float here, float there, float reach)
            {
                if (here <= 0.5)
                {
                    return 0.0;
                }
                float fall = here - there;
                return fall > 1e-3 ? min(4.0 * reach, reach * (here - 0.5) / fall) : 4.0 * reach;
            }

            /* The diffuse light at a point in a shaft, per unit of what it takes out: the mean of the
             * light arriving from every way, which is what a medium that loses nothing scatters on
             * whatever its phase function. Twin of CurtainPresenter.DiffuseLight, which says why. */
            float3 CurtainDiffuse(CurtainLights lights, float gap, float height, float edge, float overhead)
            {
                float open = CurtainOpenShare(gap, edge);
                float openGround = CurtainOpenShare(height, edge);
                float through = lerp(1.0, CURTAIN_STORM_THROUGH, saturate(overhead));
                float3 onOpen = lights.sky + lights.sun * max(0.0, lights.toSun.y);
                float3 upper = lights.horizon * open + onOpen * (through * (1.0 - open));
                float3 lower = lights.ground * (openGround + (1.0 - openGround) * through);
                return 0.5 * (upper + lower);
            }

            // What a point scatters toward the eye, per unit of what it takes out.

            float CurtainDensity(float3 position, float footprint, bool fine, out float topHere, out float cloudOver)
            {
                float window = CurtainBoxWindow(position);
                if (window <= 0.0)
                {
                    topHere = _CurtainCentre.w;
                    cloudOver = 0.0;
                    return 0.0;
                }
                return CurtainDensityInBox(position, footprint, fine, topHere, cloudOver) * window;
            }

            float3 CurtainSource(float3 position, float extinction, float topHere, float cloudOver, float phase, CurtainLights lights, float footprint)
            {
                float3 local = position - _CurtainCentre.xyz;
                float3 toSun = lights.toSun;
                float3 lit;
                if (_CurtainForm.y < 0.5)
                {
                    /* Rain is water, and water in the visible takes out next to nothing of what it
                     * scatters (its absorption index is ~1e-9: across a 2 mm drop it keeps all but a
                     * few parts in a hundred thousand). So a shaft is not a dark thing hanging from a
                     * dark cloud: it is a white scatterer, and it shows the light it stands in.
                     *
                     * That light is the day round the storm, not the viewer's. Overhead is the storm's
                     * underside, which lets down a few per cent of the light on its top; to the side,
                     * under the base's edge, is the open sky low down — the horizon — which a point
                     * sees more of the further below the base it hangs, so a shaft pales toward the
                     * ground and toward its edges; below is the ground, in the storm's shade under it
                     * and sunlit past it, which the upper part of a shaft sees more of. And the sun
                     * itself only where its ray gets in under the base clear of the cloud: grey under a
                     * high sun, gold from the side at the end of the day.
                     *
                     * It was lit by the viewer's own ambient straight up, 0.7 of it under the storm,
                     * and coloured by the rain's fog colour, 0.6 — an albedo of six tenths for water;
                     * nothing from the side, and no air between: a shaft twenty kilometres off was a
                     * twentieth of the brightness of the cloud base right over it. */
                    float gap = max(0.0, _CurtainCentre.w - local.y);
                    float height = max(0.0, local.y);
                    float2 toSunUnder = toSun.y > 0.02 ? toSun.xz / toSun.y * gap : float2(0.0, 0.0);
                    // How far off the cloud ends on the camera's side, from its cover a shape's size
                    // that way: that side is the face the camera sees.
                    float reach = max(30.0, _CurtainMarch.w);
                    float2 aside = local.xz + lights.toCamera * reach;
                    float overhead;
                    float there;
                    float cloudOnSun = 0.0;
                    if (_CurtainStorm.x > 0.5)
                    {
                        // The storm's own cloud, where the sky draws it: over where these drops left the
                        // base (read already, for whether they fall), a shape's size toward the camera,
                        // and where the sun's ray to the point comes in at the height of the base.
                        overhead = cloudOver;
                        there = FishWeatherMapAt(_CurtainCentre.xz + aside).r;
                        if (toSun.y > 0.02)
                        {
                            cloudOnSun = FishWeatherMapAt(_CurtainCentre.xz + local.xz + toSunUnder).r;
                        }
                    }
                    else
                    {
                        overhead = CurtainCoverage(local.xz * CURTAIN_SHIELD);
                        there = CurtainCoverage(aside * CURTAIN_SHIELD);
                        if (toSun.y > 0.02)
                        {
                            cloudOnSun = CurtainCoverage((local.xz + toSunUnder) * CURTAIN_SHIELD);
                        }
                    }
                    float edge = CurtainEdge(saturate(overhead), saturate(there), reach);
                    float sunSeen = exp(-6.0 * cloudOnSun);
                    lit = CurtainDiffuse(lights, gap, height, edge, overhead) + lights.sun * (sunSeen * phase * CURTAIN_SUN);
                }
                else
                {
                    /* Dust shades itself. The sun is read through two taps of the dust toward it; the
                     * sky, through the dust above, which thins upward as the wall does; and a wall is
                     * open to the sky along its side as well, so its foot is dark but not black. */
                    float span = max(1.0, _CurtainCentre.w);
                    float nearTap = 0.03 * span;
                    float farTap = 0.15 * span;
                    float unused;
                    float unusedCover;
                    float tau = CurtainDensity(position + toSun * nearTap, footprint, false, unused, unusedCover) * nearTap
                        + CurtainDensity(position + toSun * farTap, footprint, false, unused, unusedCover) * (farTap - nearTap);
                    float sunSeen = CurtainScattered(tau);
                    float scale = 0.45 * max(1.0, topHere);
                    float above = max(0.0, topHere - max(0.0, local.y));
                    float skySeen = 0.3 + 0.7 * CurtainScattered(extinction * scale * (1.0 - exp(-above / scale)));
                    lit = lights.sky * skySeen + lights.sun * (sunSeen * phase * CURTAIN_SUN);
                }
                return _CurtainColor.rgb * lit;
            }

            // ── The air between ───────────────────────────────────────────────────────────
            // Twins of FishCloudThinShare and FishCloudAirDepth (FishCloudVolume.hlsl): the storm's
            // cloud is seen through this air, and the rain hanging from it has to be seen through the
            // same, or the base goes pale with distance and the shaft under it stays as dark as it is
            // near. A shaft twenty kilometres off is behind about half an optical depth of haze.
            float CurtainThinShare(float x)
            {
                return x < 1e-3 ? 1.0 - 0.5 * x : (1.0 - exp(-x)) / x;
            }

            float3 CurtainAirDepth(float distance, float fromAltitude, float toAltitude)
            {
                float low = max(0.0, min(fromAltitude, toAltitude));
                float rise = abs(toAltitude - fromAltitude);
                float airHeight = max(100.0, _FishCloudAirExtinction.w);
                float hazeHeight = max(10.0, _FishCloudAerosol.y);
                float air = exp(-low / airHeight) * CurtainThinShare(rise / airHeight);
                float haze = exp(-low / hazeHeight) * CurtainThinShare(rise / hazeHeight);
                return max(0.0, distance) * (_FishCloudAirExtinction.xyz * air + _FishCloudAerosol.x * haze);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 origin = _WorldSpaceCameraPos.xyz;
                float3 toFragment = input.positionWS - origin;
                float fragmentDistance = length(toFragment);
                float3 direction = toFragment / max(fragmentDistance, 1e-4);

                // Where the ray is inside the box: in the box's own space it is the unit cube, and the
                // ray's parameter there is its distance in the world.
                float4x4 worldToObject = GetWorldToObjectMatrix();
                float3 ro = mul(worldToObject, float4(origin, 1.0)).xyz;
                float3 rd = mul((float3x3)worldToObject, direction);
                float3 reciprocal = 1.0 / (rd + 1e-9 * (step(0.0, rd) * 2.0 - 1.0));
                float3 a = (-0.5 - ro) * reciprocal;
                float3 b = (0.5 - ro) * reciprocal;
                float3 lo = min(a, b);
                float3 hi = max(a, b);
                float enter = max(max(max(lo.x, lo.y), lo.z), 0.0);
                float leave = min(min(hi.x, hi.y), hi.z);
                // The box is drawn from both sides; the side the ray leaves by does the work, whether the
                // camera is outside or in.
                if (leave <= enter || fragmentDistance < 0.5 * (enter + leave))
                {
                    discard;
                }

                // And it stops at the world. Nothing drawn is the sky, which does not stop it.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1e-6;
                #else
                    bool isSky = rawDepth >= 1.0 - 1e-6;
                #endif
                float3 cameraForward = -UNITY_MATRIX_V[2].xyz;
                float sceneDistance = isSky ? 1e9 : LinearEyeDepth(rawDepth, _ZBufferParams) / max(1e-4, dot(direction, cameraForward));
                // Not the first stretch round the camera: the particle field and the weather's own fog show that.
                float start = max(enter, _CurtainMarch.y);
                float end = min(leave, sceneDistance);
                if (end <= start)
                {
                    discard;
                }

                // How wide a pixel is per metre away, for the noise's mip.
                float perMetre = 2.0 / max(1e-4, abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                // The same offset down each column of pixels, so a shaft's streaks stay whole up the
                // screen; neighbouring columns differ, which dithers the strides away sideways.
                float jitter = InterleavedGradientNoise(float2(input.positionCS.x, 0.5), 0);

                Light light = GetMainLight();
                CurtainLights lights;
                lights.toSun = light.direction;
                // Read once, where the march comes in: past the cookie's window that is the average
                // the cookie fades to, the same for the whole curtain.
                lights.sun = light.color * CurtainCookie(origin + direction * start);
                float2 back = -direction.xz;
                lights.toCamera = dot(back, back) > 1e-8 ? normalize(back) : float2(1.0, 0.0);
                if (_CurtainStorm.y > 0.5)
                {
                    // The open day, as the storm's own cloud is lit by it.
                    lights.sky = _FishCloudAmbient.rgb;
                    lights.horizon = _FishCloudHaze.rgb;
                    lights.ground = _FishCloudGround.w * (lights.sky + lights.sun * max(0.0, lights.toSun.y));
                }
                else
                {
                    // No sky to ask: the scene's own ambient, up, level toward the camera, and down.
                    lights.sky = max(0.0, FishTrilight(half3(0.0, 1.0, 0.0)));
                    lights.horizon = max(0.0, FishTrilight(half3(lights.toCamera.x, 0.0, lights.toCamera.y)));
                    lights.ground = max(0.0, FishTrilight(half3(0.0, -1.0, 0.0)));
                }
                float phase = lerp(1.0, CurtainPhase(dot(direction, light.direction), _CurtainAmount.w), 0.6);

                float strides = max(4.0, _CurtainMarch.x);
                float stride = (end - start) / strides;
                float fineStride = stride * 0.12;
                float t = start + stride * jitter;
                float transmittance = 1.0;
                float3 scattered = 0.0;
                float weighted = 0.0;
                // As if the march had just come from the start: something dense right there is backed
                // up to and entered finely like anything else, not jumped by the jitter's first stride.
                float lastStep = stride * jitter;
                float lastExtinction = 0.0;
                float refineUntil = -1.0;
                int backs = 0;
                UNITY_LOOP
                for (int i = 0; i < 80; i++)
                {
                    if (t >= end || transmittance < 0.004)
                    {
                        break;
                    }
                    float3 position = origin + direction * t;
                    float footprint = t * perMetre;
                    float topHere;
                    float cloudOver;
                    float extinction = CurtainDensity(position, footprint, true, topHere, cloudOver);
                    /* Out of clear air into something on a long stride: go back and come through its
                     * edge in short ones. A dust wall is opaque within a few tens of metres, and a
                     * stride of a few hundred put its face wherever the stride happened to land — a
                     * different depth in every pixel, which is noise, not dust. */
                    if (extinction > 0.0 && lastExtinction <= 0.0 && lastStep > fineStride * 1.5 && backs < 2)
                    {
                        refineUntil = t;
                        t = max(start, t - lastStep) + fineStride * jitter;
                        lastStep = 0.0;
                        backs++;
                        continue;
                    }
                    // Short strides through the edge just found, and wherever the medium is thick
                    // enough that a long one would jump a whole optical depth; long ones elsewhere.
                    float advance = t < refineUntil ? fineStride
                        : (extinction > 0.0 ? clamp(0.35 / extinction, fineStride, stride) : stride);
                    advance = min(advance, end - t);
                    if (extinction > 0.0)
                    {
                        float through = exp(-extinction * advance);
                        float3 source = CurtainSource(position, extinction, topHere, cloudOver, phase, lights, footprint);
                        // The light scattered in over the stride, of which what gets out is the share
                        // the stride itself did not take: exact for a stride of one medium.
                        float taken = transmittance * (1.0 - through);
                        scattered += source * taken;
                        weighted += t * taken;
                        transmittance *= through;
                    }
                    lastStep = advance;
                    lastExtinction = extinction;
                    t += advance;
                }

                float alpha = 1.0 - transmittance;
                if (alpha <= 1e-4)
                {
                    discard;
                }
                float3 color = scattered / alpha;
                // Seen through the air as far off as what was seen of it is, on average: the air's own
                // haze as the clouds are seen through it (world height is altitude), and then the
                // weather's fog, which the ground behind it is fogged by too.
                float seenAt = weighted / alpha;
                float3 seen = origin + direction * seenAt;
                if (_CurtainStorm.y > 0.5)
                {
                    float3 airThrough = exp(-CurtainAirDepth(seenAt, origin.y, seen.y));
                    color = color * airThrough + _FishCloudHaze.rgb * (1.0 - airThrough);
                }
                color = MixFog(color, ComputeFogFactor(TransformWorldToHClip(seen).z));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
