// The daylight falling through openings in caves' roofs (CaveSkylights draws them, 2026-10-20, caves livened): a pre-built
// mesh of closed prisms and mote quads that the vertex shader hands out among the nearest openings. Each prism is the
// shaft of sunlit air under an opening — the opening's disc carried down along the sun's direction to the floor — drawn by
// its far faces only (told from the near ones by the ray's own crossing, whatever the winding), with the light it scatters worked out along the eye's ray through it exactly (the ray against the slanted
// cylinder, cut at the floor, the opening and whatever stands in the way), lit where the main light's shadow map says the
// sun reaches (the terrain's hole and the rock round the opening draw its edge), with slow dust drifting through it. Each
// quad is a mote of dust turning in the beam, glinting where the sun catches it. Added, never covering. Nothing is
// simulated and nothing is stored: the motes drift on the shared motion clock (_FishWeatherMisc.w).
Shader "FishMMO/World/Cave Skylight Shafts"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+30"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "SkylightShafts"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _LIGHT_COOKIES

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define SLOTS 4
            // Taps of the shadow map and the dust along the eye's ray through a shaft.
            #define TAPS 5

            // The shared motion clock, wrapped (WorldMotion.ShaderWrapSeconds; FishWeather.hlsl's, set by the weather).
            float4 _FishWeatherMisc;

            // Per draw (CaveSkylights): the openings, nearest first. xyz the opening's middle on the ground (world), w its radius.
            float4 _SkylightSlots[SLOTS];
            // Per opening: x the floor's height under it (world y), y how strongly its shaft shows (0 none: faded with distance),
            // z how strongly its motes show, w a phase of its own.
            float4 _SkylightFloors[SLOTS];
            // x the shaft's air: how much light a metre of it scatters toward the eye; y the shaft's brightness; z the motes';
            // w how far the motes are drawn from the eye (m).
            float4 _SkylightAir;

            struct Attributes
            {
                float3 shape : POSITION;    // a prism's unit circle (xy) and 0 at the opening / 1 at the floor (z); a quad's corner (xy)
                float3 tag : TEXCOORD0;     // x the opening, y 0 for a prism / 1 for a mote, z the mote's number
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 data : TEXCOORD1;    // x the opening, y 0 a prism / 1 a mote, z the mote's glint, w its strength
                float3 centre : TEXCOORD2;  // a mote's middle (world)
                float2 corner : TEXCOORD3;
            };

            float Hash(float n)
            {
                return frac(sin(n * 12.9898 + 4.1414) * 43758.5453);
            }

            // Toward the sun (the main light), and how high it stands: no shaft under a sun at or below the horizon.
            float3 TowardSun()
            {
                return normalize(_MainLightPosition.xyz);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                int s = min(SLOTS - 1, (int)round(input.tag.x));
                float4 slot = _SkylightSlots[s];
                float4 floors = _SkylightFloors[s];
                float3 L = TowardSun();
                float shown = step(0.02, L.y) * step(0.001, slot.w);
                float drop = slot.y - floors.x + 1.0;
                float time = _FishWeatherMisc.w;
                float3 worldPos;
                if (input.tag.y < 0.5)
                {
                    // The prism: the opening's disc, a little wider than it (the edge fades inside), carried down along the sun
                    // to under the floor. Folds to a point where there is no shaft.
                    float radius = slot.w * 1.08;
                    float along = input.shape.z * drop / max(L.y, 0.02);
                    worldPos = float3(slot.x, slot.y, slot.z) + float3(input.shape.x, 0.0, input.shape.y) * radius - L * along;
                    worldPos = lerp(float3(slot.x, slot.y, slot.z), worldPos, shown * step(0.001, floors.y));
                    output.data = float4(s, 0.0, 0.0, floors.y);
                }
                else
                {
                    // A mote: somewhere in the shaft by its number, sinking slowly down it and turning in the air, round again
                    // when it reaches the floor (a few minutes a pass), a quad turned to the eye a few pixels across.
                    float n = input.tag.z + 17.0 * s + floors.w * 101.0;
                    float h1 = Hash(n), h2 = Hash(n + 1.7), h3 = Hash(n + 3.1), h4 = Hash(n + 5.3);
                    float depthShare = frac(h1 + time * (0.002 + 0.004 * h4));
                    float r = 0.92 * slot.w * sqrt(h2);
                    float a = 6.2831853 * h3 + 0.4 * sin(time * 0.07 + h1 * 6.2831853);
                    float3 centre = float3(slot.x + cos(a) * r, slot.y, slot.z + sin(a) * r) - L * (depthShare * drop / max(L.y, 0.02));
                    centre += float3(sin(time * 0.37 + h1 * 40.0), sin(time * 0.23 + h2 * 40.0), sin(time * 0.31 + h3 * 40.0)) * 0.3;
                    float distance = length(centre - _WorldSpaceCameraPos.xyz);
                    float near = 1.0 - smoothstep(0.7 * _SkylightAir.w, _SkylightAir.w, distance);
                    float ends = smoothstep(0.0, 0.06, depthShare) * (1.0 - smoothstep(0.85, 1.0, depthShare));
                    float size = max(0.025, 0.0025 * distance) * shown * step(0.001, near);
                    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                    worldPos = centre + (right * input.shape.x + up * input.shape.y) * size;
                    // A slow glint, each mote on its own beat.
                    float glint = 0.35 + 0.65 * pow(0.5 + 0.5 * sin(time * (0.6 + 1.4 * h4) + h2 * 6.2831853), 6.0);
                    output.data = float4(s, 1.0, glint, floors.z * near * ends);
                    output.centre = centre;
                    output.corner = input.shape.xy;
                }
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            // The main light's shadow at a point in the air, from its cascade (screen-space shadows have no answer off a surface).
            float SunShadow(float3 positionWS)
            {
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            #if defined(_MAIN_LIGHT_SHADOWS)
                half cascade = 0;
            #else
                half cascade = ComputeCascadeIndex(positionWS);
            #endif
                float4 coord = float4(mul(_MainLightWorldToShadow[cascade], float4(positionWS, 1.0)).xyz, 0.0);
                return SampleShadowmap(TEXTURE2D_SHADOW_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare), coord,
                    GetMainLightShadowSamplingData(), GetMainLightShadowParams(), false);
            #else
                return 1.0;
            #endif
            }

            // The sun's light at a point, with the cloud's shadow on it where the cookie carries one (never the bare intensity:
            // under cloud it is divided by the cloud's average and the cookie puts it back).
            float3 SunLight(float3 positionWS)
            {
                Light sun = GetMainLight(float4(0.0, 0.0, 0.0, 1.0), positionWS, half4(1.0, 1.0, 1.0, 1.0));
                return sun.color;
            }

            // Where a ray (from `eye`, unit `ray`) is inside the shaft under an opening (its middle's x/z `centre`, `radius`) along
            // the sun (toward it: L) between the heights `bottom` and `top`: false where it never is.
            bool Crossing(float3 eye, float3 ray, float3 L, float2 centre, float radius, float bottom, float top, out float t0, out float t1)
            {
                float2 start = eye.xz + L.xz * ((top - eye.y) / L.y) - centre;
                float2 drift = ray.xz - L.xz * (ray.y / L.y);
                float a = dot(drift, drift), b = dot(start, drift), c = dot(start, start) - radius * radius;
                t0 = -1e6;
                t1 = 1e6;
                if (a < 1e-6)
                {
                    // Looking straight along the sun: in the shaft all the way, or not at all.
                    if (c > 0.0)
                    {
                        return false;
                    }
                }
                else
                {
                    float disc = b * b - a * c;
                    if (disc <= 0.0)
                    {
                        return false;
                    }
                    float root = sqrt(disc);
                    t0 = (-b - root) / a;
                    t1 = (-b + root) / a;
                }
                if (abs(ray.y) > 1e-5)
                {
                    float ta = (bottom - eye.y) / ray.y, tb = (top - eye.y) / ray.y;
                    t0 = max(t0, min(ta, tb));
                    t1 = min(t1, max(ta, tb));
                }
                else if (eye.y < bottom || eye.y > top)
                {
                    return false;
                }
                return t1 > t0;
            }

            // Dust in the air: slow, soft lumps drifting on the clock, 0.4 .. 1.
            float Dust(float3 p, float time)
            {
                float3 q = p * 0.45 + float3(0.0, -time * 0.05, time * 0.02);
                float v = sin(q.x * 1.7 + sin(q.z * 1.3)) * sin(q.z * 1.9 + sin(q.y * 1.1)) * sin(q.y * 1.5 + sin(q.x * 0.9));
                return 0.7 + 0.3 * v;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                int s = min(SLOTS - 1, (int)round(input.data.x));
                float strength = input.data.w;
                if (strength <= 0.0)
                {
                    discard;
                }
                float4 slot = _SkylightSlots[s];
                float4 floors = _SkylightFloors[s];
                float3 L = TowardSun();
                float rise = smoothstep(0.02, 0.25, L.y);
                if (input.data.y > 0.5)
                {
                    // A mote: a soft dot, lit only where the sun reaches it.
                    float r2 = dot(input.corner, input.corner);
                    float dot_ = exp(-r2 * 5.0) * step(r2, 1.0);
                    float3 mote = SunLight(input.centre) * (SunShadow(input.centre) * dot_ * input.data.z * strength * rise * _SkylightAir.z);
                    return half4((half3)mote, 0.0);
                }

                // The eye's ray through the shaft: the cylinder of the opening's disc carried along the sun. Seen along the sun,
                // every point maps to where its line toward the sun crosses the opening's plane; inside the shaft where that
                // lies within the opening.
                float3 eye = _WorldSpaceCameraPos.xyz;
                float3 ray = normalize(input.positionWS - eye);
                float top = slot.y, bottom = floors.x - 1.0, radius = slot.w;
                float t0, t1;
                if (!Crossing(eye, ray, L, slot.xz, radius, bottom, top, t0, t1))
                {
                    discard;
                }
                // Once a pixel: by the prism's far face. Its near face (the drawn prism is the opening's disc a little wider)
                // is where the ray comes in, nearer than where it goes out.
                float enter, leave;
                if (Crossing(eye, ray, L, slot.xz, radius * 1.08, bottom, top, enter, leave) && enter > 0.0)
                {
                    float here = length(input.positionWS - eye);
                    if (abs(here - enter) < abs(here - leave))
                    {
                        discard;
                    }
                }
                // Not through what stands in the way.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float tScene = sceneEye / max(dot(ray, forward), 1e-4);
                t0 = max(t0, 0.0);
                t1 = min(t1, tScene);
                float chord = t1 - t0;
                if (chord <= 0.0)
                {
                    discard;
                }

                // Along it: where the sun reaches (the shadow map), the dust, and soft toward the shaft's edge.
                float time = _FishWeatherMisc.w;
                float jitter = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
                float lit = 0.0;
                [unroll]
                for (int i = 0; i < TAPS; i++)
                {
                    float3 p = eye + ray * (t0 + chord * ((i + jitter) / TAPS));
                    float2 q = p.xz + L.xz * ((top - p.y) / L.y) - slot.xz;
                    float edge = 1.0 - smoothstep(0.75 * radius, radius, length(q));
                    lit += SunShadow(p) * Dust(p, time) * edge;
                }
                lit /= TAPS;
                // Forward scattering: brightest looking up the shaft toward the sun, a floor of it from the side.
                float toward = saturate(dot(ray, L) * 0.5 + 0.5);
                float phase = 0.35 + 1.4 * pow(toward, 6.0);
                float inscatter = 1.0 - exp(-_SkylightAir.x * chord);
                float3 shaft = SunLight(float3(slot.x, slot.y, slot.z)) * (inscatter * lit * phase * strength * rise * _SkylightAir.y);
                return half4((half3)shaft, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
