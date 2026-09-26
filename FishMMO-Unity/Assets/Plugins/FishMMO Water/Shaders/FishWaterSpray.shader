// The breakers' spray and mist, entirely on the GPU and with no state.
//
// Each particle is a quad whose corners carry only which corner, what kind it is and a few random
// numbers (WaterBreakers.FillSprayMesh). Here it picks a point of the break line from the line
// texture, asks the breakers' own model what the breaker there is doing (FishWaterBreakerCommon.hlsl),
// and works out where it is from when in that breaker's life it was born: thrown off the lip as it
// curls, splashed up where the lip lands, or hanging over the impact as mist and drifting downwind.
// Nothing is simulated from frame to frame, so it costs the CPU nothing, stops when the world's time
// does, and is the same on every client — and it needs no compute, so it runs wherever the sea does.
Shader "FishMMO/Water/Spray"
{
    Properties
    {
        _Spray ("Spray", Range(0, 3)) = 1
        _Mist ("Mist", Range(0, 3)) = 1
        // Copied from the ocean's material by WaterBreakers: the spray rides the same breakers.
        [HideInInspector] _BreakerHeight ("Breaker height", Float) = 1
        [HideInInspector] _BreakerCurl ("Barrel", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-95"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // Premultiplied: a drop lit by the sun adds light as well as hiding what is behind it.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex SprayVertex
            #pragma fragment Frag
            #pragma target 3.5

            #pragma multi_compile_fog
            // Soft where it meets the water and the beach; set by WaterSurface against the URP asset.
            #pragma multi_compile_fragment _ _WATER_DEPTH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(_WATER_DEPTH)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            CBUFFER_START(UnityPerMaterial)
                float _Spray;
                float _Mist;
                float _BreakerHeight;
                float _BreakerCurl;
            CBUFFER_END

            float4 _FishWaterWind;           // xy toward, z speed (m/s)
            float _FishWaterLevel;           // the sea's surface now, tide included
            float _FishWaterCloudShadow;     // 1 open sky, 0 under cloud
            /// The break line (WaterBreakers.UploadLineTexture): the first half of the rows xy the point
            /// and zw the way to the shore; the second half x the room, y its confidence, z metres to an
            /// end, w the space it may reach into.
            Texture2D<float4> _FishWaterBreakLine;
            float4 _FishWaterBreakLineInfo;  // x points, y texture width, z rows per half, w metres between points

            #include "FishWaterBreakerCommon.hlsl"
            #include "FishWaterFog.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                /// xy the corner, z the kind (0 spray off the lip, 1 splash where it lands, 2 mist), w a random number.
                float4 corner : TEXCOORD0;
                float4 random : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                /// x eye depth, y metres over which it softens into what is behind it, z the kind, w fog.
                float4 soft : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float4 screenPos : TEXCOORD4;
            };

            /// <summary>Another number in [0, 1) from one the particle carries, for each thing it needs one for.</summary>
            float SprayHash(float seed, float salt)
            {
                return frac(sin(seed * (12.9898 + salt) + salt * 78.233) * 43758.5453);
            }

            Varyings SprayVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                // Off the screen until it is shown to be alive: every corner of a dead quad lands here.
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);

                float count = _FishWaterBreakLineInfo.x;
                int kind = (int)(input.corner.z + 0.5);
                float strength = kind == 2 ? _Mist : _Spray;
                if (count < 1.0 || strength <= 0.0 || !FishWaterSurfKeepsTime())
                {
                    return output;
                }

                // Which point of the line, and where between it and its neighbours.
                float4 r = input.random;
                int index = min((int)(r.x * count), (int)count - 1);
                int width = max(1, (int)_FishWaterBreakLineInfo.y);
                int rows = max(1, (int)_FishWaterBreakLineInfo.z);
                float4 station = _FishWaterBreakLine.Load(int3(index % width, index / width, 0));
                float4 stationShore = _FishWaterBreakLine.Load(int3(index % width, rows + index / width, 0));
                float2 shoreward = normalize(station.zw);
                float2 alongShore = float2(shoreward.y, -shoreward.x);
                float2 breakXZ = station.xy + alongShore * (r.z - 0.5) * _FishWaterBreakLineInfo.w;

                FishWaterBreakerState breaker = FishWaterBreakerAt(breakXZ, shoreward, stationShore.x, stationShore.y,
                    _BreakerHeight, _BreakerCurl, stationShore.w);
                if (breaker.height < 0.05)
                {
                    return output;
                }

                /* When in its breaker's life it is born, how long it lives, and how old it is now — all
                 * in the breaker's own time, so it keeps pace with the wave however fast the world runs. */
                float birth, life;
                if (kind == 0)
                {
                    birth = lerp(FISH_BREAKER_CURL + 0.08, FISH_BREAKER_LAND, r.y);
                    life = lerp(0.7, 1.6, r.w);
                }
                else if (kind == 1)
                {
                    birth = FISH_BREAKER_LAND + r.y * 0.08;
                    life = lerp(0.8, 1.8, r.w);
                }
                else
                {
                    birth = FISH_BREAKER_LAND - 0.02 + r.y * 0.16;
                    life = lerp(3.0, 6.0, r.w);
                }
                life = min(life, 0.9 * breaker.period);
                float age = frac(breaker.t - birth) * breaker.period;
                if (age > life)
                {
                    return output;
                }

                // Where the lip was when it was born, and how fast it was going.
                FishWaterBreakerState then = breaker;
                then.t = birth;
                float2 tip = FishWaterBreakerProfile(then, 1, 1.0, 1.0).position;
                then.t = birth + 0.01;
                float2 lipVelocity = (FishWaterBreakerProfile(then, 1, 1.0, 1.0).position - tip) / (0.01 * breaker.period);
                if (kind > 0)
                {
                    // Splash and mist rise where the lip came down.
                    then.t = FISH_BREAKER_LAND;
                    tip = FishWaterBreakerProfile(then, 1, 1.0, 1.0).position;
                }

                float3 up = float3(0.0, 1.0, 0.0);
                float3 across = float3(shoreward.x, 0.0, shoreward.y);
                float3 alongDirection = float3(alongShore.x, 0.0, alongShore.y);
                float3 origin = float3(breakXZ.x, _FishWaterLevel, breakXZ.y) + across * tip.x + up * tip.y;

                float h1 = SprayHash(r.x, 1.0), h2 = SprayHash(r.y, 2.0), h3 = SprayHash(r.z, 3.0);
                float h4 = SprayHash(r.w, 4.0), h5 = input.corner.w;
                float gravity = max(0.05, _FishWaterGravity);
                float3 velocity0;
                if (kind == 0)
                {
                    // Torn off the lip: its own speed, and flung up and about by the throw.
                    velocity0 = across * (lipVelocity.x * 0.8 + 1.5 * h1)
                        + up * (max(0.0, lipVelocity.y) + 1.0 + 3.0 * h2 * breaker.plunging)
                        + alongDirection * (h3 - 0.5) * 2.5;
                    origin += across * (h4 - 0.5) * 0.3 * breaker.height;
                }
                else if (kind == 1)
                {
                    // The splash-up: as high again as the wave that fell, a little over half of it.
                    velocity0 = up * sqrt(2.0 * gravity * breaker.height) * lerp(0.35, 1.0, h2)
                        + across * (h4 < 0.3 ? -1.0 : 1.0) * (1.0 + 2.0 * h1)
                        + alongDirection * (h3 - 0.5) * 3.0;
                }
                else
                {
                    // Mist: the finest of it, loose over the impact and barely falling.
                    origin += across * (h1 - 0.3) * 2.0 * breaker.height + up * h2 * breaker.height
                        + alongDirection * (h4 - 0.5) * 2.0 * breaker.height;
                    velocity0 = up * lerp(0.3, 1.2, h3) + across * 0.15 * breaker.boreSpeed;
                }

                /* Carried by the air and held up by it. Drag takes a particle to the wind's speed and to
                 * its own terminal fall, exp(-k·age) of the way: a drop falls at ten metres a second, a
                 * speck of mist at a few centimetres. Offshore wind blows the spray back off the crest,
                 * which is what a real one does. */
                float3 wind = float3(_FishWaterWind.x, 0.0, _FishWaterWind.y) * _FishWaterWind.z * (kind == 2 ? 0.55 : 0.35);
                float drag = kind == 2 ? 1.4 : 0.9;
                float fall = kind == 2 ? 0.03 : 1.0;
                float3 terminal = wind - up * (gravity * fall / drag);
                float decay = exp(-drag * age);
                float3 positionWS = origin + terminal * age + (velocity0 - terminal) * (1.0 - decay) / drag;
                float3 velocity = terminal + (velocity0 - terminal) * decay;

                float grown = age / max(1e-3, life);
                float size;
                if (kind == 0)
                {
                    size = lerp(0.06, 0.22, h5) * (1.0 + grown);
                }
                else if (kind == 1)
                {
                    size = lerp(0.1, 0.35, h5) * (1.0 + 1.5 * grown);
                }
                else
                {
                    size = lerp(1.2, 3.0, h5) * (0.6 + 1.2 * grown);
                }
                size *= clamp(breaker.height, 0.4, 2.0);

                float alpha = smoothstep(0.0, 0.08, grown) * (1.0 - smoothstep(0.55, 1.0, grown));
                alpha *= kind == 2 ? 0.16 : 0.85;
                // A plunging wave throws far more than a spilling one; a small one hardly any.
                alpha *= strength * saturate(breaker.height / 0.3)
                    * (kind == 2 ? lerp(0.5, 1.0, breaker.plunging) : lerp(0.25, 1.0, breaker.plunging));
                alpha *= smoothstep(0.0, 6.0, stationShore.z);
                // Fallen back into the sea.
                alpha *= smoothstep(_FishWaterLevel - 0.05, _FishWaterLevel + 0.15, positionWS.y);
                float distanceToCamera = length(positionWS - _WorldSpaceCameraPos);
                alpha *= 1.0 - (kind == 2 ? smoothstep(400.0, 700.0, distanceToCamera) : smoothstep(180.0, 300.0, distanceToCamera));
                if (alpha < 0.002)
                {
                    return output;
                }

                /* Never smaller than a pixel and a half. Far spray drawn at its true size would fall
                 * between the pixels and sparkle; drawn a little bigger with its light shared out over
                 * the extra area, it fades instead. */
                float pixel = 2.0 * distanceToCamera / max(1e-3, UNITY_MATRIX_P[1][1] * _ScreenParams.y);
                float drawnSize = max(size, pixel * 1.5);
                alpha *= (size / drawnSize) * (size / drawnSize);

                float3 view = (positionWS - _WorldSpaceCameraPos) / max(1e-3, distanceToCamera);
                float2 corner = input.corner.xy * 2.0 - 1.0;
                float3 offset;
                if (kind < 2)
                {
                    // Streaked along its flight, as a drop is to the eye over a twenty-fifth of a second.
                    float3 across2D = velocity - view * dot(velocity, view);
                    float speed = length(across2D);
                    float3 axis = speed > 1e-3 ? across2D / speed : UNITY_MATRIX_V[1].xyz;
                    float3 side = normalize(cross(axis, view));
                    float stretch = 1.0 + speed * 0.04 / max(0.02, drawnSize);
                    offset = (side * corner.x + axis * corner.y * stretch) * drawnSize * 0.5;
                    alpha /= stretch;
                }
                else
                {
                    // Turned at random, so no two puffs share an outline.
                    float angle = h4 * 6.2831853;
                    float s, c;
                    sincos(angle, s, c);
                    float2 turned = float2(corner.x * c - corner.y * s, corner.x * s + corner.y * c);
                    offset = (UNITY_MATRIX_V[0].xyz * turned.x + UNITY_MATRIX_V[1].xyz * turned.y) * drawnSize * 0.5;
                }
                positionWS += offset;

                /* Lit by the sun through it — water droplets scatter forward, strongly, so spray against
                 * a low sun blazes and with the sun behind the eye it is dull — and by the sky. */
                Light sun = GetMainLight();
                float g = kind == 2 ? 0.75 : 0.6;
                float cosine = dot(view, sun.direction);
                float phase = (1.0 - g * g) / pow(max(1e-3, 1.0 + g * g - 2.0 * g * cosine), 1.5);
                half3 light = sun.color * saturate(_FishWaterCloudShadow) * saturate(sun.direction.y * 4.0 + 0.2)
                    * (0.3 + 0.12 * phase) + _GlossyEnvironmentColor.rgb * 0.9 + 0.04;
                half3 tint = kind == 2 ? half3(0.9, 0.94, 0.97) : half3(0.95, 0.98, 1.0);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.uv = input.corner.xy;
                output.color = half4(tint * light, saturate(alpha));
                output.soft = float4(-TransformWorldToView(positionWS).z, kind == 2 ? 1.5 : 0.25, kind,
                    ComputeFogFactor(output.positionCS.z));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = input.uv * 2.0 - 1.0;
                float r2 = dot(c, c);
                if (r2 > 1.0)
                {
                    discard;
                }
                // A drop has a body; mist is nothing but falloff.
                half body = input.soft.z < 1.5 ? pow(saturate(1.0 - r2), 1.5) : (1.0 - r2) * (1.0 - r2);
                half alpha = input.color.a * body;

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                #if defined(_WATER_DEPTH)
                    // Softened where it meets the water or the beach behind it, instead of cut off.
                    float scene = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    alpha *= saturate((scene - input.soft.x) / input.soft.y);
                #endif

                half3 color = MixFog(input.color.rgb, input.soft.w);
                half keep;
                color = FishWaterAirFog(color, input.positionWS, screenUV, keep);
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
