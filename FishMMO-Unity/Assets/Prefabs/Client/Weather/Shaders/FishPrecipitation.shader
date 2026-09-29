// Near precipitation: a pre-built field of quads that the vertex shader moves, wraps around the
// camera, stretches along the fall and hides under roofs. No geometry or compute shaders, so it
// runs the same on WebGPU, WebGL2 and desktop.
Shader "FishMMO/Weather/Precipitation"
{
    Properties
    {
        _MainTex ("Atlas (4 x 8 tiles)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Precipitation"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            // The cloud shadow cookie on the sun (CloudShadowPresenter), read in the vertex stage: the
            // sun a drop is lit by is the sun through the cloud over it.
            #pragma multi_compile _ _LIGHT_COOKIES
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWeather.hlsl"
            #include "FishAmbient.hlsl"
            // The fog's layer, for the fog between a drop and the camera: the layer is drawn before the
            // transparent queue, behind everything drawn here.
            #include "FishFogLayer.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
            CBUFFER_END

            // Per draw, from a MaterialPropertyBlock.
            float4 _PrecipOrigin;   // xyz camera position, w time in seconds
            float4 _PrecipBox;      // xyz size of the wrapping box around the camera (this shell's), w unused
            float4 _PrecipShell;    // where this shell is drawn (PrecipitationField.ShellFades): x, y the distances it fades in over, z, w those it fades out over, m
            float4 _PrecipMotion;   // x the eye's moment a grain's streak is gathered over, s (PrecipitationField.StreakSeconds): 0 draws no streak
            float4 _PrecipFall;     // xyz the kind's velocity now at its typical particle, m/s, wind included
            float4 _PrecipTravel;   // x, z metres the air has carried everything, y metres the typical particle has fallen; wrapped
            float4 _PrecipSpread;   // x slowest, y fastest particle against the typical one, z 1 when it falls from cloud, w 1 for a clear drop (seen through), 0 for an opaque grain
            float4 _PrecipShape;    // x visible share of particles, y size in metres, z stretch, w atlas row
            float4 _PrecipFlutter;  // x sway in metres, y sway frequency, z alpha, w brightness
            float4 _PrecipColor;    // tint for this draw
            float4 _PrecipGrain;    // x 1: sizes spread exponentially about _PrecipShape.y (snow), 0: ±30 %; y smallest, z largest, m; w how much of its quad the sprite fills, across
            float4 _PrecipTile;     // x atlas mip at which a texel is a whole tile, y sub-pixel dot sigma px, z fewest pixels across a grain's quad, w grain pixels from which its shape shows
            float4 _PrecipCrowd;    // x how many grains' light one particle carries (PrecipitationField.Crowd): 1 for its own

            struct Attributes
            {
                float3 seed : POSITION;     // particle position in the unit box, shared by its 4 corners
                float2 corner : TEXCOORD0;  // 0/1 corner of the quad
                float4 random : TEXCOORD1;  // x show threshold, y size jitter, z speed jitter, w tile/phase
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;      // xy atlas, zw the centre of the particle's tile
                float4 color : TEXCOORD1;
                float fogCoord : TEXCOORD2;
                float4 grain : TEXCOORD3;   // xy corner of the quad, z pixels across the quad, w 0 a soft dot … 1 the sprite's shape
                float coverage : TEXCOORD4; // the light it carries over what its drawn quad would at full strength: past 1 for a crowd under the pixel floor
                float2 motion : TEXCOORD5;  // x how far it moves over the eye's moment, px (its streak), y its quad's width over its length
            };

            /* The main light's cookie at a point: the cloud shadow CloudShadowPresenter lays on the sun,
             * read as URP's own SampleMainLightCookie reads it but at the top mip, since this is the
             * vertex stage. The sun's light is handed over with the cloud in view divided back OUT of
             * it, for the cookie to carry patch by patch; read bare, it is the open sun under a storm.
             * With no cookie the light already carries the cloud's average, and this is 1. */
            float3 PrecipSunCookie(float3 positionWS)
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

            // PrecipitationField.GrainSun: a Lambertian sphere's light over its disc at a phase angle,
            // against a white card facing the sun — two thirds at full phase, none with the sun behind.
            float PrecipGrainSun(float cosPhase)
            {
                float alpha = acos(clamp(cosPhase, -1.0, 1.0));
                return (2.0 / 3.0) * (sin(alpha) + (PI - alpha) * cos(alpha)) / PI;
            }

            /* How many metres of the world one pixel spans at a point, across: its depth over the
             * projection's focal length in pixels. An orthographic camera's pixel is the same everywhere. */
            float PrecipPixelMetres(float3 positionWS)
            {
                float focal = max(abs(UNITY_MATRIX_P._m11), 1e-4) * max(_ScreenParams.y, 1.0) * 0.5;
                float depth = max(-TransformWorldToView(positionWS).z, 1e-3);
                return unity_OrthoParams.w > 0.5 ? 1.0 / focal : depth / focal;
            }

            // PrecipitationField.SubPixelAlpha: the tile's mean coverage over the whole quad, gathered
            // into a Gaussian dot of _PrecipTile.y pixels — swept along the grain's streak, when it has
            // one, and `offsetPixels` is then the distance from that path — so it sums on the pixel grid
            // to the grain's own light wherever the grain sits against the pixel centres, however long
            // its streak.
            float PrecipSubPixel(float meanCoverage, float quadPixels, float offsetPixels, float streakPixels)
            {
                float s = max(_PrecipTile.y, 1e-2);
                float spread = 2.0 * PI * s * s + sqrt(2.0 * PI) * s * max(0.0, streakPixels);
                return meanCoverage * quadPixels * quadPixels * exp(-offsetPixels * offsetPixels / (2.0 * s * s)) / spread;
            }

            // PrecipitationField.ShellWeight: in over the shell's first fade as a sine, out over its second
            // as a cosine, so where two shells meet the squares of their weights add to one and the
            // snowfall's speckle holds across the seam.
            float PrecipShellWeight(float distance)
            {
                float fadeIn = saturate((distance - _PrecipShell.x) / max(1e-4, _PrecipShell.y - _PrecipShell.x));
                float fadeOut = saturate((distance - _PrecipShell.z) / max(1e-4, _PrecipShell.w - _PrecipShell.z));
                return sin(0.5 * PI * fadeIn) * cos(0.5 * PI * fadeOut);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float time = _PrecipOrigin.w;
                float3 box = max(_PrecipBox.xyz, 1.0);


                // Each particle falls at its own speed; positions wrap inside a box that follows the
                // camera, but in world space, so walking does not drag the particles along.
                // Each drop's own fall. The field used to move as one sheet — every particle carried
                // by the same vector, speeds within a fifth of each other, every streak parallel and
                // the same length — which the eye reads as a pattern sliding past. A big drop falls
                // faster than a small one (mostly the same random that sets its size sets its speed,
                // so the two agree), the air it falls through is never still, and no two streaks are
                // quite alike. Sideways, though, every particle goes with the air.
                //
                // Where it has got to is the travel PrecipitationField.Advance integrates, never the
                // velocity times the time: that is only right while nothing changes, and the wind
                // never stops changing. The speed is a whole number of 64ths of the typical one, which
                // is what lets the fall be wrapped there without moving anything.
                float share = saturate(input.random.y * 0.8 + input.random.z * 0.2);
                float speed = round(lerp(_PrecipSpread.x, _PrecipSpread.y, share) * 64.0) / 64.0;
                float3 travelled = float3(_PrecipTravel.x, -_PrecipTravel.y * speed, _PrecipTravel.z);
                // Turbulence: a slow lateral wander, its own phase and rate to every drop, scaled by
                // the wind — a still day's rain falls straight, a gusty one's is thrown about.
                float gustiness = 0.15 + 0.25 * saturate(length(_PrecipFall.xz) / max(1.0, abs(_PrecipFall.y)));
                float wanderX = lerp(0.7, 1.9, input.random.z), wanderZ = lerp(0.9, 2.3, input.random.w);
                float2 wander = float2(sin(time * wanderX + input.random.w * 6.2831), cos(time * wanderZ + input.random.x * 6.2831)) * gustiness;
                // And how fast each is moving it now, m/s: their derivatives, for the streak below.
                float2 wandering = float2(wanderX * cos(time * wanderX + input.random.w * 6.2831), -wanderZ * sin(time * wanderZ + input.random.x * 6.2831)) * gustiness;
                float3 local = frac(input.seed + (travelled - _PrecipOrigin.xyz) / box) * box - box * 0.5;
                float3 centre = _PrecipOrigin.xyz + local + float3(wander.x, 0.0, wander.y);

                // Sway for snow and ash.
                float phase = input.random.w * 6.2831 + time * _PrecipFlutter.y;
                centre.xz += float2(sin(phase), cos(phase * 1.3)) * _PrecipFlutter.x;
                float2 swaying = float2(cos(phase), -1.3 * sin(phase * 1.3)) * _PrecipFlutter.x * _PrecipFlutter.y;

                /* HOW MANY fall here: the kind's share, times how much of it the cloud above can
                 * shed. The field's weather says how hard it is raining where it rains; the cloud
                 * standing over this spot says where, and a thin patch of it sheds less than the
                 * deck's thick heart — so a gap in the deck is a gap in the rain, a thinning is a
                 * thinning, and a shower moves with the cloud it falls from. It FADED the drops
                 * instead, which under a thin patch left the full number of them hanging half there,
                 * like a ghost of rain. A drop near its turn fades over a narrow band rather than
                 * blinking as the cloud moves past it. Sand is lifted off the ground by the wind and
                 * owes the sky nothing. */
                float shedding = lerp(1.0, smoothstep(0.08, 0.35, FishCloudOver(centre)), saturate(_PrecipSpread.z));
                float visible = saturate((_PrecipShape.x * shedding - input.random.x) * 25.0);

                float3 toCamera = _WorldSpaceCameraPos.xyz - centre;
                float distance = length(toCamera);
                float3 view = toCamera / max(distance, 1e-3);

                /* How big this one is. Snow spreads exponentially about its mean, as a snowfall does
                 * (PrecipitationField's SnowflakeSize.Draw is the twin): many small, a few large, none
                 * below the smallest counted or past the largest the warmth allows. Everything else is
                 * within 30 % of its kind's size. The same random sets the speed share above, so a big
                 * flake falls a little faster, as it does. */
                float own;
                if (_PrecipGrain.x > 0.5)
                {
                    float e = -log(max(1.0 - input.random.y, 1e-4));
                    own = min(_PrecipGrain.z, _PrecipGrain.y + max(0.0, _PrecipShape.y - _PrecipGrain.y) * e);
                }
                else
                {
                    own = _PrecipShape.y * lerp(0.7, 1.3, input.random.y);
                }
                own *= step(0.001, visible);
                float size = own;
                float coverage = 1.0;
                float quadPixels = 1e4;
                float2 motion = float2(0.0, 1.0);
                float resolved = 1.0;
                float3 axis;
                float3 side;
                float length01 = size;
                if (_PrecipShape.z > 1.01)
                {
                    // Streaks: along the fall direction, facing the camera — each leaning a little its
                    // own way, and its own length, since a drop's streak is its speed over the frame.
                    // Along its OWN motion: the same wind and its own fall, so a fast drop streaks
                    // steeper than a slow one in the same gust.
                    float3 own = float3(_PrecipFall.x, _PrecipFall.y * speed, _PrecipFall.z);
                    float3 lean = float3(input.random.z - 0.5, 0.0, input.random.w - 0.5) * 0.12 * length(own);
                    axis = normalize(own + lean + float3(0, -1e-3, 0));
                    side = normalize(cross(axis, view) + float3(1e-4, 0, 0));
                    float quick = saturate((speed - _PrecipSpread.x) / max(1e-3, _PrecipSpread.y - _PrecipSpread.x));
                    length01 = size * _PrecipShape.z * lerp(0.85, 1.15, quick);
                }
                else
                {
                    // Flakes and grit: camera-facing.
                    side = normalize(cross(float3(0, 1, 0), view) + float3(1e-4, 0, 0));
                    axis = cross(view, side);

                    /* Drawn as big as the grain is, not its quad: the sprite fills only part of it. And
                     * never under a few pixels, made fainter by the area it gained so its light in sum
                     * is what it stands for (PrecipitationField.MinQuadPixels and Coverage say why): a
                     * flake of a millimetre or two is under a pixel a couple of metres off, and at its
                     * own size it blinks between pixel centres. One particle is drawn for thousands of
                     * flakes, and carries the light of the square root of them (PrecipitationField.Crowd):
                     * the snowfall's own speckle, where one flake's light was a hundredth of a pixel
                     * and the snow vanished. The crowd only fills in what the floor spread out, so a
                     * flake drawn at its own size stays one flake. */
                    float pixel = PrecipPixelMetres(centre);
                    float ownQuad = own / max(_PrecipGrain.w, 0.05);
                    // A hidden particle (own 0) stays collapsed rather than drawing an empty floor.
                    float drawnQuad = max(ownQuad, _PrecipTile.z * pixel) * step(1e-9, own);
                    float gained = ownQuad / max(drawnQuad, 1e-6);
                    coverage = gained * gained * max(_PrecipCrowd.x, 1.0);
                    size = drawnQuad;
                    length01 = size;
                    quadPixels = drawnQuad / pixel;
                    resolved = saturate((own / pixel - _PrecipTile.w) / 3.0);

                    /* Its streak (PrecipitationField.StreakSeconds): how far it moves across the line of
                     * sight over the eye's moment — its own fall, the air's drift, its wander and sway;
                     * motion along the line of sight draws no streak. A flake a metre off falling at a
                     * metre a second is some fifteen pixels of streak; the quad is lengthened along it
                     * and the flake's light spread along it, never added to (the fragment divides it). */
                    if (_PrecipMotion.x > 0.0)
                    {
                        float3 velocity = float3(_PrecipFall.x + wandering.x + swaying.x, _PrecipFall.y * speed, _PrecipFall.z + wandering.y + swaying.y);
                        float3 across = velocity - dot(velocity, view) * view;
                        float streak = length(across) * _PrecipMotion.x;
                        if (streak > 1e-5)
                        {
                            axis = across / length(across);
                            side = cross(axis, view);
                            length01 = size + streak;
                            motion = float2(streak / pixel, size / length01);
                        }
                    }
                }
                float3 worldPos = centre + side * (input.corner.x - 0.5) * size + axis * (input.corner.y - 0.5) * length01;

                // Drawn over this shell's distances (PrecipitationField.ShellFades: in from the camera's
                // nearest or the next shell in, out toward its box's edge), and not under anything overhead.
                float shellWeight = PrecipShellWeight(distance);
                float open = FishSkyOpen(centre);

                /* Lit as what it is (PrecipitationField.ClearDropSun and GrainSun say why). A raindrop is
                 * a lens: it shows the light BEHIND it, gathered from most of the half of the world on
                 * its far side — the sky's own grey-white, and the sun only against the light. A flake,
                 * a hailstone, a grain of ash or sand is opaque, and shows the light on the face it
                 * turns to the camera. Either way the sun is the sun through the cloud over the drop:
                 * the main light with its cookie. It was the sky straight up plus 0.35 of the light read
                 * bare — the open sun, under a storm — and at a low sun the streaks were orange. */
                Light mainLight = GetMainLight();
                float3 sun = mainLight.color * PrecipSunCookie(centre);
                float3 light;
                if (_PrecipSpread.w > 0.5)
                {
                    light = max(0.0, FishTrilight(-view)) + sun * max(0.0, dot(-view, mainLight.direction));
                }
                else
                {
                    light = max(0.0, FishTrilight(view)) + sun * PrecipGrainSun(dot(view, mainLight.direction));
                }

                output.positionCS = TransformWorldToHClip(worldPos);
                float tile = floor(frac(input.random.w * 7.13) * 4.0);
                float row = _PrecipShape.w;
                output.uv = float4((input.corner.x + tile) * 0.25, 1.0 - (row + 1.0 - input.corner.y) * 0.125, (tile + 0.5) * 0.25, 1.0 - (row + 0.5) * 0.125);
                float3 lit = _PrecipColor.rgb * _Tint.rgb * light * _PrecipFlutter.w;

                /* The fog between it and the camera. The fog's layer is drawn before this queue, over
                 * what lies behind the drop — so it was over the ground behind a flake but never in
                 * front of the flake, and a flake ten metres into a dense fog came through as clear as
                 * one at arm's length: half a fog too sharp, and where the fog was lit brighter than the
                 * flake, a hard dark speck. The layer's own light and depth, as the march has them
                 * (FishFogLayer.hlsl), over the path from the eye, at its mean density (a bank is
                 * hundreds of metres across; this is a few). Only while something has drawn the layer:
                 * where nothing does, its drops are in the pipeline's distance fog, which MixFog adds. */
                if (_FishFogLayerShape.w > 0.5 && _FishFogLayer.x > 0.0)
                {
                    FishFogColumn column = FishFogColumnAt(centre.xz);
                    float3 eye = _WorldSpaceCameraPos.xyz;
                    float through = exp(-_FishFogLayer.x * FishFogPath(eye.y, -view.y, 0.0, distance, column));
                    float3 inFog = FishFogLight(0.5 * (eye.y + centre.y), -view, column, _FishFogLayer.x, 1.0, FishFogSunShare(centre));
                    lit = lit * through + inFog * (1.0 - through);
                }

                output.color = float4(lit, _PrecipColor.a * _Tint.a * _PrecipFlutter.z * shellWeight * open * visible);
                output.grain = float4(input.corner, quadPixels, resolved);
                output.coverage = coverage;
                output.motion = motion;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv.xy);
                // Its shape, as opaque as one grain at most: a crowd never thickens a drawn flake. Drawn
                // along its streak, the shape is smeared over the streak's length and made that much
                // fainter, so the streak holds the flake's light.
                texel.a *= (half)min(1.0, input.coverage);
                texel.a *= (half)input.motion.y;
                if (input.grain.w < 0.999)
                {
                    // Too small on screen for its shape: its tile's mean, gathered into a soft dot swept
                    // along its streak, with all the light it carries, up to opaque at the dot's heart.
                    // The offset is from the streak's path: across the quad, and along it past either end.
                    half4 mean = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, input.uv.zw, _PrecipTile.x);
                    float streak = input.motion.x;
                    float2 offset = float2((input.grain.x - 0.5) * input.grain.z, max(0.0, abs(input.grain.y - 0.5) * (input.grain.z + streak) - 0.5 * streak));
                    half spot = (half)saturate(PrecipSubPixel(mean.a, input.grain.z, length(offset), streak) * input.coverage);
                    texel = lerp(half4(mean.rgb, spot), texel, (half)input.grain.w);
                }
                half4 color = half4(input.color.rgb * texel.rgb, input.color.a * texel.a);
                clip(color.a - 0.002);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
