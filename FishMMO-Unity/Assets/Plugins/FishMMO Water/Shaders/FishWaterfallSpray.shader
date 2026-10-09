Shader "FishMMO/Water/Waterfall Spray"
{
    // The spray and mist of a fall (InlandWaterRenderer.FallSpray). Stateless: every quad is one particle whose whole
    // life (when it is thrown, where it flies, how it grows and fades) comes from its seed and the inland water's clock.
    // Spray: drops flung up and out where each part of the fall lands, and off the rock where a stream strikes it.
    // Strands and shed drops: water falling with the curtain on its traced paths (_FallPathTex). Mist: billows rising
    // from the landing and a veil over the fall, drifting downwind, lit bright when the sun is behind them, a rainbow in
    // the spray when it is behind the eye.
    Properties
    {
        _SprayColor("Spray colour", Color) = (0.95, 0.97, 1.0, 1)
        _MistColor("Mist colour", Color) = (0.90, 0.93, 0.96, 1)
        _SprayOpacity("Spray opacity", Range(0, 1)) = 0.22
        _MistOpacity("Mist opacity", Range(0, 1)) = 0.05
        _SpraySize("Droplet size (m)", Float) = 0.035
        _MistSize("Mist size (share of the pool's radius)", Float) = 0.45
        _SoftDistance("Soft against geometry (m)", Float) = 0.8
        _Rainbow("Rainbow strength", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-97"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex SprayVertex
            #pragma fragment SprayFragment
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _WATER_DEPTH
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(_WATER_DEPTH)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            CBUFFER_START(UnityPerMaterial)
                half4 _SprayColor;
                half4 _MistColor;
                half _SprayOpacity;
                half _MistOpacity;
                float _SpraySize;
                float _MistSize;
                float _SoftDistance;
                half _Rainbow;
            CBUFFER_END

            float _FishInlandTime;
            float _FishInlandCameraInSea;   // 1 under the sea (InlandWaterRenderer.MarkCamera): no falls are seen from there
            float _FishFallMistVolumetric;  // 1 when the volumetric fog carries the falls' mist this frame (FishVolumetricFogFeature)
            float4 _FishWeatherWind;        // xy the wind's way, z its speed over 30 m/s, w gusts

            // Per fall (InlandWaterRenderer.BuildFalls): the traced paths, and what they need.
            TEXTURE2D(_FallPathTex);
            SAMPLER(sampler_FallPathTex);
            float4 _FallPath;               // x strips, y seconds from the lip to the last landing, z the pool's level, w break-up length (m)
            float4 _FallOrigin;             // xyz the lip, w the speed the water lands at (m/s)

            #define FISH_INLAND_CLOCK_WRAP 10000.0

            struct Attributes
            {
                float4 positionOS : POSITION;   // the particle's home
                float3 normalOS : NORMAL;       // the river's way downstream (a strike: out of the rock)
                float4 color : COLOR;           // r the fall's drop over 120 m, g its run from lip to landing over 20 m
                float2 corner : TEXCOORD0;      // −1…1
                float2 seed : TEXCOORD1;        // two seeds, 0…1
                float2 kind : TEXCOORD2;        // x kind (0 spray, 1 mist, 2 strand, 3 veil, 4 drop, 5 strike); y its size (zone radius, drop or strike speed)
                float2 impact : TEXCOORD3;      // spray and mist: where it starts from the impact, a share of the zone's radius (xz)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float4 look : TEXCOORD1;        // xyz the particle's world position, w its opacity
                float3 kind : TEXCOORD2;        // x kind, y eye depth, z a seed for its shape
                float4 screenPos : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float3 pixelWS : TEXCOORD5;     // where on its quad this pixel is, in the world: the mist's shared noise is read there
            };

            float Hash(float x) { return frac(sin(x * 78.233) * 43758.5453); }

            /// A life snapped so a whole number of them fits the clock's wrap: at the wrap every particle is where it was.
            float SnapLife(float life) { return FISH_INLAND_CLOCK_WRAP / max(1.0, round(FISH_INLAND_CLOCK_WRAP / life)); }

            /// The wind, m/s, world xz.
            float3 Wind()
            {
                float2 dir = _FishWeatherWind.xy;
                float speed = dot(dir, dir) > 1e-6 ? 30.0 * saturate(_FishWeatherWind.z) : 0.0;   // WeatherDriver: WindSpeed = |wind| / 30 m/s
                return float3(dir.x, 0.0, dir.y) * speed;
            }

            /// Where a strip's water is <paramref name="time"/> seconds after leaving the lip: xyz world, w how broken (−1 landed).
            float4 OnPath(float strip, float time)
            {
                float strips = max(1.0, _FallPath.x);
                float2 uv = float2((strip + 0.5) / strips, saturate(time / max(0.05, _FallPath.y)));
                float4 p = SAMPLE_TEXTURE2D_LOD(_FallPathTex, sampler_FallPathTex, uv, 0);
                return float4(_FallOrigin.xyz + p.xyz, p.w);
            }

            /// <summary>
            /// A drop's flight under gravity and air drag linear in its speed, closed form: it settles to <paramref name="terminal"/>
            /// m/s. x0 + (g/k)t + (v0 − g/k)(1 − e^(−kt))/k, with k = g / terminal.
            /// </summary>
            float3 DragFlight(float3 x0, float3 v0, float t, float terminal, out float3 velocity)
            {
                float3 gravity = float3(0.0, -9.81, 0.0);
                float k = 9.81 / max(0.5, terminal);
                float e = exp(-k * t);
                float3 drift = gravity / k;
                velocity = drift + (v0 - drift) * e;
                return x0 + drift * t + (v0 - drift) * (1.0 - e) / k;
            }

            Varyings SprayVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 home = TransformObjectToWorld(input.positionOS.xyz);
                float3 downstream = normalize(TransformObjectToWorldDir(input.normalOS) + float3(1e-4, 0.0, 0.0));
                float3 sideways = float3(-downstream.z, 0.0, downstream.x);
                int kindId = (int)round(input.kind.x);
                bool mist = kindId == 1 || kindId == 3;
                float radius = max(1.0, input.kind.y);
                float drop = input.color.r * 120.0;   // the fall's drop over 120 m (FallSpray: 30 m capped every tall fall alike)
                float s1 = input.seed.x, s2 = input.seed.y;
                bool hasPaths = _FallPath.x > 0.5;
                float poolLevel = hasPaths ? _FallPath.z : home.y - 60.0;
                float3 wind = Wind();

                // Its life, and where in it the particle is now; each life throws it a new way (the life's own number reseeds it).
                float life = SnapLife(mist ? lerp(4.0, 7.5, s2) : lerp(1.0, 2.0, s2));
                float lives = round(FISH_INLAND_CLOCK_WRAP / life);
                float cycle = _FishInlandTime / life + s1;
                float age = frac(cycle);
                float lifeNumber = floor(cycle);
                lifeNumber -= lives * floor(lifeNumber / lives);
                float n = lifeNumber * 7.13 + s1 * 91.7;
                float r1 = Hash(n), r2 = Hash(n + 1.3), r3 = Hash(n + 2.7);
                float t = age * life;
                const float g = 9.81;
                float3 position = home;
                float size = 0.05;
                float opacity = 0.0;
                float3 velocity = float3(0.0, 0.0, 0.0);
                if (kindId == 2)
                {
                    /* A strand: water peeling off the curtain and falling with it, on one strip's traced path at the path's
                     * own pace, so it goes where the water goes (round every rock the curtain parts round). Thicker and
                     * more of them where the water has broken. */
                    float seconds = max(0.2, _FallPath.y);
                    float strandLife = SnapLife(seconds * lerp(1.0, 1.15, s2));
                    float strandCycle = _FishInlandTime / strandLife + s1;
                    float tf = frac(strandCycle) * strandLife;
                    float strip = floor(Hash(floor(strandCycle) * 3.7 + s1 * 51.3) * max(1.0, _FallPath.x));
                    if (hasPaths)
                    {
                        float4 here = OnPath(strip, tf);
                        float4 ahead = OnPath(strip, tf + 0.05);
                        velocity = (ahead.xyz - here.xyz) / 0.05;
                        position = here.xyz + sideways * ((r2 - 0.5) * 0.5) + wind * (0.05 * tf);
                        float broken = saturate(here.w);
                        opacity = here.w < 0.0 ? 0.0 : saturate(_SprayOpacity * 2.5) * lerp(0.25, 1.0, broken);
                        size = lerp(0.08, 0.25, r2) * (0.8 + 0.6 * broken);
                    }
                    else
                    {
                        // No paths (a fall with no curtain material): straight down from the lip toward the landing.
                        float fallDrop = max(0.5, input.kind.y);
                        float run = input.color.g * 20.0;
                        float fallen = min(fallDrop, 0.5 * g * tf * tf);
                        position = home + downstream * (run * sqrt(fallen / fallDrop)) + float3(0.0, -fallen, 0.0);
                        velocity = float3(0.0, -g * tf, 0.0);
                        opacity = saturate(_SprayOpacity * 2.5) * step(fallen, fallDrop - 0.1);
                        size = lerp(0.12, 0.3, r2);
                    }
                    opacity *= smoothstep(0.0, 0.06, frac(strandCycle)) * (1.0 - smoothstep(0.92, 1.0, frac(strandCycle)));
                }
                else if (kindId == 4)
                {
                    /* A drop shed from a rope: leaves its strip's path somewhere below the break-up length, with the water's
                     * speed and a little sideways, then flies free and the air slows it to the speed a drop falls at. */
                    float seconds = max(0.2, _FallPath.y);
                    float dropLife = SnapLife(lerp(0.8, 2.2, s2));
                    float dropCycle = _FishInlandTime / dropLife + s1;
                    float shedNumber = floor(dropCycle);
                    float h1 = Hash(shedNumber * 1.31 + s1 * 17.0), h2 = Hash(shedNumber * 2.17 + s2 * 23.0), h3 = Hash(shedNumber * 3.7 + s1 * 5.0);
                    float strip = floor(h1 * max(1.0, _FallPath.x));
                    float shedAt = h2 * seconds;
                    float4 shed = OnPath(strip, shedAt);
                    float4 next = OnPath(strip, shedAt + 0.05);
                    float3 v0 = (next.xyz - shed.xyz) / 0.05 + sideways * ((h3 - 0.5) * 3.0) + downstream * (0.8 * h3);
                    float tf = frac(dropCycle) * dropLife;
                    position = DragFlight(shed.xyz, v0, tf, 9.0, velocity) + wind * (0.25 * tf);
                    opacity = (shed.w >= 0.9 && hasPaths) ? saturate(_SprayOpacity * 2.0) : 0.0;
                    opacity *= step(poolLevel, position.y) * smoothstep(0.0, 0.08, frac(dropCycle)) * (1.0 - smoothstep(0.8, 1.0, frac(dropCycle)));
                    size = lerp(0.03, 0.08, r3);
                }
                else if (kindId == 5)
                {
                    /* Spray off the rock where a stream strikes it: thrown out along the rock's normal and down, scattered across
                     * it, then falling on to the pool, slowed by the air. Never up the fall, never into the rock. */
                    float3 normal = normalize(TransformObjectToWorldDir(input.normalOS) + float3(0.0, 1e-4, 0.0));
                    float3 tangentA = normalize(cross(normal, float3(0.0, 1.0, 0.0)) + float3(1e-4, 0.0, 0.0));
                    float3 tangentB = cross(normal, tangentA);
                    /* Most of a strike's water runs on down the rock; what splashes off leaves slowly, close to the face
                     * and downward. Thrown out at up to 8 m/s for over a second it sprayed the terrain (Jim, 2026-10-08). */
                    float speed = min(4.5, max(1.0, input.kind.y) * lerp(0.06, 0.18, r1));
                    float3 way = normalize(normal * 0.45 + float3(0.0, -0.45, 0.0) + (tangentA * (r2 - 0.5) + tangentB * (r3 - 0.5)) * 0.7);
                    float strikeLife = SnapLife(lerp(0.45, 0.9, s2));
                    float strikeCycle = _FishInlandTime / strikeLife + s1;
                    float tf = frac(strikeCycle) * strikeLife;
                    position = DragFlight(home, way * speed, tf, 9.0, velocity) + wind * (0.2 * tf);
                    opacity = saturate(_SprayOpacity * 2.2) * step(poolLevel, position.y) * smoothstep(0.0, 0.06, frac(strikeCycle)) * (1.0 - smoothstep(0.7, 1.0, frac(strikeCycle)));
                    size = lerp(0.03, 0.12, r2);
                }
                else if (kindId == 3)
                {
                    /* The veil: mist hanging over the whole fall, thickest low down where the water shatters, swelling and
                     * drifting out from the curtain and downwind, rising a little. */
                    float fallDrop = max(0.5, input.kind.y);
                    float run = input.color.g * 20.0;
                    float h = pow(r1, 0.6);
                    float tv = t;
                    position = home + downstream * (run * h + 0.4 + lerp(0.2, 0.7, r2) * tv) + sideways * ((r3 - 0.5) * 2.0)
                        + float3(0.0, -fallDrop * h + 0.15 * tv, 0.0) + wind * (0.6 * tv);
                    // Fine-grained like the landing's cloud: small puffs, four times as many (FallSpray.cs).
                    size = 0.4 * lerp(1.2, 4.0, h) * lerp(0.5, 1.2, age) * lerp(0.7, 1.3, r3) * (0.6 + 0.15 * sqrt(fallDrop));
                    opacity = sin(3.14159 * age) * _MistOpacity * lerp(1.5, 4.5, h);
                }
                else if (!mist)
                {
                    /* Thrown up and out where the curtain lands: a crown of drops, hardest at the impact and softer toward
                     * the zone's edge, outward with a scatter, falling back under gravity and drag. Low and out, not up: at
                     * up to 16 m/s the spray read as water shooting back up the fall (Jim, 2026-10-07). */
                    float out_ = saturate(length(input.impact));
                    float2 away2 = dot(input.impact, input.impact) > 1e-6 ? normalize(input.impact) : float2(downstream.x, downstream.z);
                    float3 away = float3(away2.x, 0.0, away2.y);
                    float angle = (r3 - 0.5) * 1.6;
                    float3 outward = away * cos(angle) + float3(-away.z, 0.0, away.x) * sin(angle);
                    float hard = lerp(1.0, 0.45, out_);
                    float up = lerp(1.2, 4.0, r1 * r1) * (0.8 + 0.08 * sqrt(drop)) * hard;
                    float3 v0 = outward * (lerp(1.0, 4.0, r2) * (0.6 + 0.8 * out_)) + downstream * 0.6 + float3(0.0, up, 0.0);
                    position = DragFlight(home, v0, t, 9.0, velocity) + wind * (0.3 * t);
                    opacity = position.y > home.y - 0.1 ? 1.0 : 0.0;
                    size = lerp(0.05, 0.18, r2) * lerp(1.3, 0.6, out_);
                    opacity *= smoothstep(0.0, 0.06, age) * (1.0 - smoothstep(0.7, 1.0, age)) * saturate(_SprayOpacity * 2.2) * lerp(1.0, 0.45, out_);
                }
                else
                {
                    /* The mist boiling off the impact: dense, big and low over it, rising and spreading out from it and drifting
                     * downstream and downwind, thinner the further out it began. Thinned where the volumetric fog carries it. */
                    float out_ = saturate(length(input.impact));
                    float2 away2 = dot(input.impact, input.impact) > 1e-6 ? normalize(input.impact) : float2(downstream.x, downstream.z);
                    float3 away = float3(away2.x, 0.0, away2.y);
                    /* A cloud that hugs the landing and climbs up the curtain, higher for a taller fall (Jim, 2026-10-08):
                     * it rises to about four tenths of the drop and spreads only a little past the impact. Spread by the
                     * drop as well, a 40 m fall's cloud fanned out across the whole cliff as scattered puffs. */
                    float rise = lerp(0.3, 1.0, r1) * (0.4 + 0.05 * drop) * lerp(1.1, 0.7, out_);
                    float spread = lerp(0.2, 0.7, r2) * (0.4 + 0.02 * drop) * (0.6 + out_);
                    float drift = lerp(0.2, 0.7, r3);
                    position = home + away * (spread * t) + downstream * (drift * t) + float3(0.0, rise * t, 0.0) + wind * (0.7 * t);
                    /* Soft puffs that overlap many times over, each faint: the cloud is their sum, and the shared noise in
                     * the fragment (MistField) carves the same wisps through every one of them, so no puff shows its own
                     * edge. Small, denser puffs each with its own pattern drew the cloud as a field of separate grey balls. */
                    size = min(2.5, (0.7 + 0.02 * drop) * lerp(0.7, 1.5, age) * lerp(0.8, 1.2, r3));
                    opacity = sin(3.14159 * age) * saturate(_MistOpacity * 4.0) * saturate(0.5 + drop / 8.0) * lerp(1.0, 0.5, out_);
                }
                if (mist)
                {
                    opacity *= lerp(1.0, 0.7, saturate(_FishFallMistVolumetric));
                }

                // Facing the camera; a drop drawn out along its flight, as a fast drop is in any exposure.
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 upAxis = UNITY_MATRIX_V[1].xyz;
                float stretch = 1.0;
                float speed = length(velocity);
                if (!mist && speed > 0.1)
                {
                    float3 toEye = normalize(_WorldSpaceCameraPos - position);
                    upAxis = velocity / speed;
                    right = normalize(cross(upAxis, toEye) + float3(1e-5, 0.0, 0.0));
                    stretch = 1.0 + speed * (kindId == 2 ? 0.6 : 0.3);
                }
                float3 world = position + (right * input.corner.x + upAxis * (input.corner.y * stretch)) * size;
                output.positionCS = TransformWorldToHClip(world);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.corner = input.corner;
                output.look = float4(position, opacity);
                output.pixelWS = world;
                output.kind = float3(input.kind.x, -TransformWorldToView(world).z, s1 * 37.0 + s2 * 11.0);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            /// Value noise over a quad's corner space, for its shape.
            half ShapeNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i.x + i.y * 57.0), b = Hash(i.x + 1.0 + i.y * 57.0), c = Hash(i.x + (i.y + 1.0) * 57.0), d = Hash(i.x + 1.0 + (i.y + 1.0) * 57.0);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Hash3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }

            /// Value noise in three dimensions.
            half Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                half a = lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y);
                half b = lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            /// <summary>
            /// The mist's own field, in the world and shared by every puff: billows of about two metres and wisps of about
            /// half a metre, rising as the cloud does. Read where each pixel of a puff lies, so overlapping puffs carve the
            /// same shapes and add up to one cloud with wisps through it rather than a heap of separate balls.
            /// </summary>
            half MistFieldAt(float3 p)
            {
                half n = 0.65 * Noise3(p * 0.45) + 0.35 * Noise3(p * 1.7 + 13.1);
                return saturate(n * 1.9 - 0.45);
            }

            half MistField(float3 world)
            {
                /* Rising at 0.6 m/s, carried as the curtain's streaks are: two copies half a cycle apart, cross-faded, so
                 * the pattern never jumps (a cycle snapped to a whole fraction of the clock's wrap). */
                const float cycle = FISH_INLAND_CLOCK_WRAP / round(FISH_INLAND_CLOCK_WRAP / 40.0);
                float phaseA = frac(_FishInlandTime / cycle), phaseB = frac(_FishInlandTime / cycle + 0.5);
                half blend = abs(2.0 * phaseA - 1.0);
                half a = MistFieldAt(world - float3(0.0, phaseA * cycle * 0.6, 0.0));
                half b = MistFieldAt(world - float3(0.0, phaseB * cycle * 0.6, 0.0) + float3(31.7, 0.0, 17.3));
                return lerp(a, b, blend);
            }

            /// Henyey-Greenstein, scaled to 1 for light scattered evenly.
            half ForwardScatter(half cosine, half g)
            {
                half g2 = g * g;
                return (1.0 - g2) / pow(max(1e-3, 1.0 + g2 - 2.0 * g * cosine), 1.5);
            }

            /// <summary>
            /// A rainbow's colour at <paramref name="angle"/> degrees from the point opposite the sun: the primary bow 40.6–42.3°,
            /// violet inside and red outside, and the fainter secondary 50–53° with its colours the other way round.
            /// </summary>
            half3 Rainbow(half angle)
            {
                half t1 = (angle - 40.6) / 1.7;
                half t2 = 1.0 - (angle - 50.0) / 3.0;
                half3 bow = half3(0.0, 0.0, 0.0);
                half in1 = smoothstep(-0.15, 0.05, t1) * (1.0 - smoothstep(0.95, 1.15, t1));
                half in2 = smoothstep(-0.15, 0.05, t2) * (1.0 - smoothstep(0.95, 1.15, t2));
                half3 c1 = half3(smoothstep(0.45, 0.95, t1), saturate(1.0 - abs(t1 - 0.5) * 2.2), 1.0 - smoothstep(0.05, 0.55, t1));
                half3 c2 = half3(smoothstep(0.45, 0.95, t2), saturate(1.0 - abs(t2 - 0.5) * 2.2), 1.0 - smoothstep(0.05, 0.55, t2));
                return c1 * in1 + c2 * in2 * 0.43;
            }

            half4 SprayFragment(Varyings input) : SV_Target
            {
                if (_FishInlandCameraInSea > 0.5)
                {
                    discard;
                }
                int kindId = (int)round(input.kind.x);
                bool mist = kindId == 1 || kindId == 3;
                float d2 = dot(input.corner, input.corner);
                // Mist in wisps, not balls: the soft disc broken by noise that turns with the particle's own seed.
                // Soft all the way to its edge: a billow, never a lens with a rim.
                half shape = mist ? exp(-d2 * 2.2) * smoothstep(1.0, 0.45, d2) * MistField(input.pixelWS) : smoothstep(1.0, 0.15, sqrt(d2));
                half alpha = shape * input.look.w;
                // Not in the eye's face: a puff the camera is in drew a flat grey sheet over the view.
                alpha *= mist ? smoothstep(1.0, 4.0, input.kind.y) : 1.0;

                // Soft where it meets the pool and the rock, so no quad edge cuts the water.
                #if defined(_WATER_DEPTH)
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    alpha *= saturate((sceneEye - input.kind.y) / max(0.05, _SoftDistance));
                #endif
                clip(alpha - 0.004);

                /* Light: the sun where it reaches (spray in a shaded gorge was lit as if in full sun), the sky and the ground
                 * round it, and much brighter looking toward the sun through it (droplets scatter forward). */
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.look.xyz));
                half sunlit = mainLight.shadowAttenuation;
                float3 toEye = normalize(_WorldSpaceCameraPos - input.look.xyz);
                half forward = ForwardScatter(dot(-toEye, mainLight.direction), mist ? 0.55 : 0.7);
                half3 ambient = 0.5 * (SampleSH(float3(0.0, 1.0, 0.0)) + SampleSH(toEye));
                /* Spray mist is dense and white: most of the light in it has been turned more than once, so it is nearly as
                 * bright as what lights it from every side, the sky's light most of all, and glows looking toward the sun.
                 * Lit like a sparse drop (the beam and a little sky) it was a dull grey-brown in a shaded gorge. */
                half3 light = mist
                    ? mainLight.color * sunlit * (0.55 + 0.3 * forward) + ambient * 1.6
                    : mainLight.color * sunlit * (0.45 + 0.12 * forward) + ambient * 0.9;
                half3 color = (mist ? _MistColor.rgb : _SprayColor.rgb) * light;

                // A rainbow in the sunlit spray, round the point opposite the sun; mist's fine drops give only a faint one.
                half bowAngle = degrees(acos(clamp(dot(-toEye, -mainLight.direction), -1.0, 1.0)));
                color += Rainbow(bowAngle) * mainLight.color * sunlit * _Rainbow * (mist ? 0.25 : 1.0) * saturate(mainLight.direction.y * 4.0 + 0.2);

                color = MixFog(color, input.fogFactor);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
