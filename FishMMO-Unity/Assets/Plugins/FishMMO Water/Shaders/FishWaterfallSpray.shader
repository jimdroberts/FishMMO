Shader "FishMMO/Water/Waterfall Spray"
{
    // The spray and mist where a fall lands (InlandWaterRenderer.Falls). Stateless: every quad is one particle
    // whose whole life (when it is thrown up, where it flies, how it grows and fades) comes from its seed and the
    // inland water's clock. Spray: droplets flung up and out of the pool, falling back. Mist: soft billows rising
    // from the foot and drifting downstream, lit bright when the sun is behind them.
    Properties
    {
        _SprayColor("Spray colour", Color) = (0.95, 0.97, 1.0, 1)
        _MistColor("Mist colour", Color) = (0.90, 0.93, 0.96, 1)
        _SprayOpacity("Spray opacity", Range(0, 1)) = 0.22
        _MistOpacity("Mist opacity", Range(0, 1)) = 0.05
        _SpraySize("Droplet size (m)", Float) = 0.035
        _MistSize("Mist size (share of the pool's radius)", Float) = 0.45
        _SoftDistance("Soft against geometry (m)", Float) = 0.8
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
            CBUFFER_END

            float _FishInlandTime;
            float _FishInlandCameraInSea;   // 1 under the sea (InlandWaterRenderer.MarkCamera): no falls are seen from there

            struct Attributes
            {
                float4 positionOS : POSITION;   // the particle's home at the fall's foot
                float3 normalOS : NORMAL;       // the river's way downstream
                float4 color : COLOR;           // r: the fall's drop over 30 m
                float2 corner : TEXCOORD0;      // −1…1
                float2 seed : TEXCOORD1;        // two seeds, 0…1
                float2 kind : TEXCOORD2;        // x 0 spray / 1 mist (y the impact zone's radius, m); 2 strand / 3 veil (y the fall's drop, m; color g its run over 20 m)
                float2 impact : TEXCOORD3;      // spray and mist: where it starts from the impact, a share of the zone's radius (xz)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float4 look : TEXCOORD1;        // xyz the particle's world position, w its opacity
                float2 kind : TEXCOORD2;        // x kind, y eye depth
                float4 screenPos : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            float Hash(float x) { return frac(sin(x * 78.233) * 43758.5453); }

            Varyings SprayVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 home = TransformObjectToWorld(input.positionOS.xyz);
                float3 downstream = normalize(TransformObjectToWorldDir(input.normalOS) + float3(1e-4, 0.0, 0.0));
                float3 sideways = float3(-downstream.z, 0.0, downstream.x);
                int kindId = (int)round(input.kind.x);
                bool mist = kindId == 1 || kindId == 3;
                float radius = max(1.0, input.kind.y);
                float drop = input.color.r * 30.0;
                float s1 = input.seed.x, s2 = input.seed.y;

                // Its life, and where in it the particle is now.
                float life = mist ? lerp(4.0, 7.5, s2) : lerp(1.0, 2.0, s2);
                /* Snapped so a whole number of lives fits the clock's 10 000 s wrap (InlandWaterRenderer), and the
                 * life's number counted round that many: at the wrap every droplet is where it was, mid-flight,
                 * rather than all of them re-dealt at once. */
                const float wrap = 10000.0;
                float lives = max(1.0, round(wrap / life));
                life = wrap / lives;
                float cycle = _FishInlandTime / life + s1;
                float age = frac(cycle);
                // Each life throws it a new way: the life's own number reseeds it.
                float lifeNumber = floor(cycle);
                lifeNumber -= lives * floor(lifeNumber / lives);
                float n = lifeNumber * 7.13 + s1 * 91.7;
                float r1 = Hash(n), r2 = Hash(n + 1.3), r3 = Hash(n + 2.7);
                float t = age * life;
                const float g = 9.81;
                float3 position;
                float size;
                float opacity;
                float3 velocity = float3(0.0, 0.0, 0.0);
                if (kindId == 2)
                {
                    /* A strand peeling off the curtain: it leaves the lip with the water and falls the whole height with it,
                     * along the curtain's path out to where it lands, drawn out along its speed as falling water is. */
                    float fallDrop = max(0.5, input.kind.y);
                    float run = input.color.g * 20.0;
                    float fallTime = sqrt(2.0 * fallDrop / g);
                    float strandLife = fallTime * lerp(0.95, 1.15, s2);
                    float strandAge = frac(_FishInlandTime / strandLife + s1);
                    float tf = strandAge * strandLife;
                    float fallen = min(fallDrop, 0.5 * g * tf * tf);
                    float share = fallen / fallDrop;
                    float peel = (0.15 + 0.6 * r1) * share;
                    position = home + downstream * (run * share + peel) + sideways * ((r2 - 0.5) * 0.8 * share) + float3(0.0, -fallen, 0.0);
                    velocity = downstream * (run / max(0.2, fallTime)) + float3(0.0, -g * tf, 0.0);
                    // Clumps of water, not droplets: big enough to read against the curtain from a distance.
                    size = lerp(0.12, 0.35, r2) * (0.8 + 0.4 * share);
                    opacity = saturate(_SprayOpacity * 2.5) * smoothstep(0.0, 0.08, strandAge) * (1.0 - smoothstep(0.85, 1.0, strandAge));
                }
                else if (kindId == 3)
                {
                    /* The veil: mist hanging over the whole fall, thickest low down where the water shatters, swelling and
                     * drifting out from the curtain, rising a little. */
                    float fallDrop = max(0.5, input.kind.y);
                    float run = input.color.g * 20.0;
                    float h = pow(r1, 0.6);
                    float veilLife = lerp(4.0, 7.5, s2);
                    float veilAge = frac(_FishInlandTime / veilLife + s1);
                    float tv = veilAge * veilLife;
                    position = home + downstream * (run * h + 0.4 + lerp(0.2, 0.7, r2) * tv) + sideways * ((r3 - 0.5) * 2.0)
                        + float3(0.0, -fallDrop * h + 0.15 * tv, 0.0);
                    size = lerp(1.2, 4.0, h) * lerp(0.5, 1.2, veilAge) * lerp(0.7, 1.3, r3) * (0.6 + 0.15 * sqrt(fallDrop));
                    opacity = sin(3.14159 * veilAge) * _MistOpacity * lerp(1.0, 3.0, h);
                }
                else if (!mist)
                {
                    /* Thrown up and out from where the curtain lands: hardest and highest at the impact, softer toward the
                     * zone's edge (out = how far out it starts, 0 at the impact, 1 at the edge), outward from the impact
                     * with a scatter, and falling back under gravity and drag. Clumps of spray at the impact, droplets out
                     * at the edge, and fewer of them there (InlandWaterRenderer.SprayMesh puts them out by R·u²). */
                    float out_ = saturate(length(input.impact));
                    float2 away2 = dot(input.impact, input.impact) > 1e-6 ? normalize(input.impact) : float2(downstream.x, downstream.z);
                    float3 away = float3(away2.x, 0.0, away2.y);
                    float angle = (r3 - 0.5) * 1.6;
                    float3 outward = away * cos(angle) + float3(-away.z, 0.0, away.x) * sin(angle);
                    float hard = lerp(1.0, 0.45, out_);
                    /* Low and out, not up: the curtain drives the water down into the pool, and what bursts back out is thrown
                     * a metre or two high and well out across it. At up to 16 m/s (13 m high) and stretched along its flight,
                     * the spray read as water shooting back up the fall (Jim, 2026-10-07). */
                    float up = lerp(1.2, 4.0, r1 * r1) * (0.8 + 0.08 * sqrt(drop)) * hard;
                    float3 horizontal = outward * lerp(1.0, 4.0, r2) * (0.6 + 0.8 * out_) + downstream * 0.6;
                    float drag = 1.0 / (1.0 + 0.6 * t);
                    position = home + horizontal * t * drag + float3(0.0, up * t - 0.5 * g * t * t, 0.0);
                    velocity = horizontal * drag * drag + float3(0.0, up - g * t, 0.0);
                    // A droplet that has fallen back into the pool is gone.
                    opacity = position.y > home.y - 0.1 ? 1.0 : 0.0;
                    size = lerp(0.05, 0.18, r2) * lerp(1.3, 0.6, out_);
                    opacity *= smoothstep(0.0, 0.06, age) * (1.0 - smoothstep(0.7, 1.0, age)) * saturate(_SprayOpacity * 2.2) * lerp(1.0, 0.45, out_);
                }
                else
                {
                    /* The mist boiling off the impact: dense, big and low over it, rising and spreading out from it and drifting
                     * downstream, thinner the further out it began. */
                    float out_ = saturate(length(input.impact));
                    float2 away2 = dot(input.impact, input.impact) > 1e-6 ? normalize(input.impact) : float2(downstream.x, downstream.z);
                    float3 away = float3(away2.x, 0.0, away2.y);
                    float rise = lerp(0.3, 1.0, r1) * (0.6 + 0.15 * sqrt(drop)) * lerp(1.2, 0.7, out_);
                    float spread = lerp(0.3, 1.2, r2) * (0.5 + out_);
                    float drift = lerp(0.3, 1.0, r3);
                    position = home + away * (spread * t) + downstream * (drift * t) + float3(0.0, rise * t, 0.0);
                    size = radius * _MistSize * lerp(0.4, 1.1, age) * lerp(0.7, 1.3, r3) * lerp(1.2, 0.6, out_);
                    opacity = sin(3.14159 * age) * saturate(_MistOpacity * 3.0) * saturate(0.4 + drop / 10.0) * lerp(1.0, 0.3, out_);
                }

                // Facing the camera; a droplet drawn out along its flight, as a fast drop is in any exposure.
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 upAxis = UNITY_MATRIX_V[1].xyz;
                float stretch = 1.0;
                float speed = length(velocity);
                if (!mist && speed > 0.1)   // spray and strands: drawn out along their flight
                {
                    float3 toEye = normalize(_WorldSpaceCameraPos - position);
                    upAxis = velocity / speed;
                    right = normalize(cross(upAxis, toEye) + float3(1e-5, 0.0, 0.0));
                    // Drawn out along its flight, less for the spray's droplets than for the strands falling with the curtain.
                    stretch = 1.0 + speed * (kindId == 2 ? 0.9 : 0.35);
                }
                float3 world = position + (right * input.corner.x + upAxis * (input.corner.y * stretch)) * size;
                output.positionCS = TransformWorldToHClip(world);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.corner = input.corner;
                output.look = float4(position, opacity);
                output.kind = float2(input.kind.x, -TransformWorldToView(world).z);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
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
                // Mist in wisps, not balls: the soft disc broken by a swirl that turns with the particle's own seed.
                float angle = atan2(input.corner.y, input.corner.x);
                half wisp = 0.55 + 0.45 * sin(angle * 3.0 + input.look.x * 0.7 + input.look.z * 0.5 + d2 * 4.0);
                half shape = mist ? exp(-d2 * 2.6) * saturate(1.0 - d2) * wisp : smoothstep(1.0, 0.15, sqrt(d2));
                half alpha = shape * input.look.w;

                // Soft where it meets the pool and the rock, so no quad edge cuts the water.
                #if defined(_WATER_DEPTH)
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    alpha *= saturate((sceneEye - input.kind.y) / max(0.05, _SoftDistance));
                #endif
                clip(alpha - 0.004);

                // Light: the sun and the sky, and much brighter looking toward the sun through it (droplets scatter forward).
                Light mainLight = GetMainLight();
                float3 toEye = normalize(_WorldSpaceCameraPos - input.look.xyz);
                half forward = pow(saturate(dot(-toEye, mainLight.direction)), 8.0);
                half3 light = mainLight.color * (0.45 + 1.6 * forward) + _GlossyEnvironmentColor.rgb * 0.9;
                half3 color = (mist ? _MistColor.rgb : _SprayColor.rgb) * light;
                color = MixFog(color, input.fogFactor);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
