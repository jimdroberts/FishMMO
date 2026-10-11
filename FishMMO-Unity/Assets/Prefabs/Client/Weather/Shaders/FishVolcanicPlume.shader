// Volcanic plumes: a pre-built field of quads the vertex shader places, as the precipitation field's
// are. Four populations, one per draw (VolcanicPlumePresenter):
//   0 the eruption column: buoyant puffs climbing a bent-over axis, widening as they entrain air
//   1 the umbrella: puffs spreading at neutral buoyancy and carried downwind
//   2 the fall beneath it: a thin veil of ash settling out of the umbrella over the fallout footprint
//   3 a fountain on an airless world: grains on ballistic arcs with no drag, landing in a ring
//   4 a steam plume off hot ground (GeothermalSteamPresenter): puffs climbing a leaning axis, widening as
//     they entrain air, evaporating as they reach the length the air lets them be seen (SteamPhysics)
//   5 a geyser's water column: every drop on its own parabola, launched at the speed the eruption had
//     when it left the vent (GeothermalVents.ColumnEnvelope), so the column grows and sinks without a scrub
// The motion is VolcanicPlume's (Shared/Weather), the same functions the weather's ash falls by; the
// steam's and the geyser's are SteamPhysics' and GeothermalVents'.
// No geometry or compute shaders, so it runs the same on WebGPU, WebGL2 and desktop.
Shader "FishMMO/Weather/Volcanic Plume"
{
    Properties
    {
        _MainTex ("Atlas (4 x 8 tiles)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "VolcanicPlume"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishAmbient.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            // Per draw, from a MaterialPropertyBlock.
            float4 _PlumeVent;      // xyz the vent on the ground, w the shared clock, wrapped (s): the fountain's, whose pace is constant
            float4 _PlumeForm;      // x population (0 column, 1 umbrella, 2 fall, 3 fountain), y share of the quads drawn, z opacity, w atlas row
            float4 _PlumeRise;      // x column top m (fountain: puff size m), y umbrella height m, z umbrella half-width at the column m, w seconds to climb
            float4 _PlumeLean;      // xy the column top's offset from the vent XZ, zw the downwind unit XZ (zero in calm)
            float4 _PlumeDrift;     // x distance drawn downwind m, y seconds to drift it, z spread per metre downwind, w fallout length m
            float4 _PlumeBallistic; // fountain: x launch speed m/s, y gravity m/s2, z widest launch angle rad, w narrowest share; fall: x seconds to fall
            float4 _PlumeColor;     // rgb what it is made of, a its own glow near the vent
            // The column, umbrella and fall's pace (VolcanicPlumePresenter.PaceWindowSeconds): two layers of puffs, on windows of the
            // shared clock half a window apart, each running on the time into its own window at the period it held from that window's
            // start, and fading in and out over it. The rise.w, drift.y and ballistic.x periods are the live ones, and time nothing.
            float4 _PlumePace;      // x seconds into layer A's window, y into layer B's, z the window (s)
            float4 _PlumeHeld;      // x the period layer A holds, y layer B (s): the drawn population's climb, drift or fall
            // Steam (population 4): rise x the visible length along the plume (m), y the source's radius (m), z how fast it widens
            // per metre along; lean xy where the visible end stands from the vent, XZ m, z how high (m), w how strong the steam is (0..1).
            // A geyser (population 5): ballistic x the full launch speed (m/s), y gravity, z the jet's half-angle (rad), w the launch
            // period (s, longer than any drop's flight); drift x seconds since the eruption began, y its length (s), z the wind on
            // the spray (m/s, along lean.zw); rise x a drop puff's size (m).

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 corner : TEXCOORD0;  // 0/1 corner of the quad
                float4 random : TEXCOORD1;  // four uniform numbers per particle; w also decides whether it is drawn
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            static const float TAU = 6.2831853;

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float4 r = input.random;
                if (r.w > _PlumeForm.y)
                {
                    // Not drawn this time: every corner at one point, so nothing is rasterised.
                    output.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                    return output;
                }

                float t = _PlumeVent.w;
                float3 vent = _PlumeVent.xyz;
                int population = (int)round(_PlumeForm.x);
                float2 down = _PlumeLean.zw;
                bool calm = dot(down, down) < 0.25;
                float2 across = float2(-down.y, down.x);
                float radius0 = max(1.0, _PlumeRise.z);

                float3 centre = vent;
                float size = 10.0;
                float stretch = 1.0;
                float alpha = 1.0;
                float shade = 1.0;
                float glow = 0.0;
                float2 sideways = float2(0.0, 0.0);

                // The layer this puff is in, how far into its window, and the period it holds: the weights
                // sin² and cos² of the same angle add to one, so the two layers together never thin.
                bool second = frac(r.w * 37.13) >= 0.5;
                float into = second ? _PlumePace.y : _PlumePace.x;
                float period = max(1.0, second ? _PlumeHeld.y : _PlumeHeld.x);
                float layer = sin(PI * saturate(into / max(1.0, _PlumePace.z)));
                // Twice the weight, as each layer is half the puffs.
                float paced = 2.0 * layer * layer;

                if (population == 0)
                {
                    // Climbing the column: from the vent to the top, leaning further downwind the higher
                    // it is (the wind has had longer to carry it), widening by entrainment.
                    float s = frac(into / period + r.x);
                    float z = s * _PlumeRise.x;
                    float2 axis = _PlumeLean.xy * (s * s);
                    float width = 25.0 + 0.12 * z;
                    float2 offset = float2(cos(r.y * TAU), sin(r.y * TAU)) * sqrt(r.z) * width;
                    sideways = offset / width;
                    centre = vent + float3(axis.x + offset.x, z, axis.y + offset.y);
                    size = 0.9 * width + 15.0;
                    alpha = smoothstep(0.0, 0.04, s) * (1.0 - smoothstep(0.8, 1.0, s)) * paced;
                    glow = pow(saturate(1.0 - s * 6.0), 3.0) * paced;
                }
                else if (population == 4)
                {
                    // Steam: climbing from the vent along an axis that leans downwind (straight up at the mouth,
                    // where the jet's own momentum carries it, bending over as the wind takes it), widening by
                    // entrainment, and evaporating over the last part of the length the air lets it be seen.
                    float s = frac(into / period + r.x);
                    float along = s * _PlumeRise.x;
                    float width = _PlumeRise.y + _PlumeRise.z * along;
                    float2 axis = _PlumeLean.xy * (s * sqrt(s));
                    float2 offset = float2(cos(r.y * TAU), sin(r.y * TAU)) * sqrt(r.z) * width;
                    sideways = offset / max(0.1, width);
                    centre = vent + float3(axis.x + offset.x, s * _PlumeLean.z + 0.3 * _PlumeRise.y * r.z, axis.y + offset.y);
                    size = 1.6 * width + 0.6;
                    alpha = smoothstep(0.0, 0.06, s) * (1.0 - smoothstep(0.35, 1.0, s)) * paced * _PlumeLean.w;
                }
                else if (population == 5)
                {
                    // A geyser's water: the drop launched most recently from this quad's slot in the launch
                    // period, at the speed the eruption had at that moment, on its own parabola since.
                    float since = _PlumeDrift.x;
                    float launchPeriod = max(0.5, _PlumeBallistic.w);
                    float sinceSlot = since - r.x * launchPeriod;
                    float age = sinceSlot - floor(sinceSlot / launchPeriod) * launchPeriod;
                    float launched = since - age;
                    float duration = _PlumeDrift.y;
                    // GeothermalVents.ColumnEnvelope's twin: up in a few seconds, sinking over the last three tenths.
                    float ramp = min(4.0, 0.1 * duration);
                    float envelope = (sinceSlot < 0.0 || launched >= duration) ? 0.0
                        : smoothstep(0.0, 1.0, saturate(launched / ramp)) * (1.0 - smoothstep(0.7 * duration, duration, launched));
                    // The column surges as the plumbing boils in pulses: the whole column together, by launch time.
                    envelope *= 0.88 + 0.12 * sin(launched * 2.7);
                    float g = max(0.01, _PlumeBallistic.y);
                    float v = _PlumeBallistic.x * sqrt(saturate(envelope)) * (0.72 + 0.28 * r.z);
                    float angle = _PlumeBallistic.z * sqrt(r.w);
                    float flight = 2.0 * v * cos(angle) / g;
                    if (envelope <= 0.01 || age >= flight)
                    {
                        output.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                        return output;
                    }
                    float phi = r.y * TAU;
                    float outward = v * sin(angle) * age;
                    float up = v * cos(angle) * age - 0.5 * g * age * age;
                    float2 carried = down * (_PlumeDrift.z * 0.25 * age);
                    centre = vent + float3(cos(phi) * outward + carried.x, up, sin(phi) * outward + carried.y);
                    float u = age / flight;
                    size = _PlumeRise.x * (1.0 + 0.2 * age);
                    alpha = smoothstep(0.0, 0.03, u) * (1.0 - smoothstep(0.85, 1.0, u)) * lerp(1.0, 0.45, u);
                    shade = 0.9;
                }
                else if (population == 1)
                {
                    // The umbrella: spreading from the column's top at neutral buoyancy, carried downwind,
                    // and thinning as it spreads — the same ash over more sky.
                    float s = frac(into / period + r.x);
                    float thick = max(60.0, 0.8 * (_PlumeRise.x - _PlumeRise.y));
                    float2 p;
                    float width;
                    if (calm)
                    {
                        float reach = sqrt(s) * radius0 * 1.5;
                        p = float2(cos(r.y * TAU), sin(r.y * TAU)) * reach;
                        width = max(radius0, reach);
                    }
                    else
                    {
                        float x = s * _PlumeDrift.x - 0.5 * radius0 * (1.0 - s);
                        width = radius0 + _PlumeDrift.z * max(0.0, x);
                        p = down * x + across * ((r.y * 2.0 - 1.0) * width);
                    }
                    float h = _PlumeRise.y + (r.z - 0.4) * thick;
                    centre = vent + float3(_PlumeLean.x + p.x, h, _PlumeLean.y + p.y);
                    size = 0.3 * width + 0.6 * thick;
                    alpha = smoothstep(0.0, 0.06, s) * (1.0 - s) * saturate(radius0 / width) * paced;
                    // Its underside is in its own shadow.
                    shade = lerp(0.4, 1.0, saturate(r.z * 1.25));
                }
                else if (population == 2)
                {
                    // The fall: ash settling out of the umbrella over the footprint its fallout lands in,
                    // thickest near the column and thinning downwind as the deposit does.
                    float2 p;
                    float width;
                    float x = 0.0;
                    if (calm)
                    {
                        float reach = sqrt(r.x) * radius0;
                        p = float2(cos(r.y * TAU), sin(r.y * TAU)) * reach;
                        width = radius0;
                    }
                    else
                    {
                        x = r.x * r.x * _PlumeDrift.x;
                        width = radius0 + _PlumeDrift.z * x;
                        p = down * x + across * ((r.y * 2.0 - 1.0) * width);
                    }
                    float fallen = frac(into / period + r.z);
                    float h = _PlumeRise.y * (1.0 - fallen);
                    centre = vent + float3(_PlumeLean.x + p.x, h, _PlumeLean.y + p.y);
                    size = 0.2 * width + 30.0;
                    stretch = 3.0;
                    alpha = exp(-x / max(1.0, _PlumeDrift.w)) * saturate(radius0 / width) * smoothstep(0.0, 0.1, 1.0 - fallen) * smoothstep(0.0, 0.1, fallen) * paced;
                    shade = 0.75;
                }
                else if (population == 3)
                {
                    // A fountain with no air: every grain on its own parabola, v·t up and out, g·t²/2
                    // down, landing as far out as its angle throws it — most in a ring near the widest.
                    float cone = _PlumeBallistic.z;
                    float angle = cone * lerp(_PlumeBallistic.w, 1.0, sqrt(r.x));
                    if (frac(r.w * 13.7) < 0.15)
                    {
                        angle = cone * _PlumeBallistic.w * r.x;
                    }
                    float phi = r.y * TAU;
                    float v = _PlumeBallistic.x * (0.88 + 0.12 * r.z);
                    float g = max(0.01, _PlumeBallistic.y);
                    float flight = max(0.1, 2.0 * v * cos(angle) / g);
                    float since = frac(t / flight + frac(r.w * 7.31)) * flight;
                    float outward = v * sin(angle) * since;
                    float up = v * cos(angle) * since - 0.5 * g * since * since;
                    centre = vent + float3(cos(phi) * outward, up, sin(phi) * outward);
                    size = _PlumeRise.x;
                    float u = since / flight;
                    alpha = smoothstep(0.0, 0.04, u) * (1.0 - smoothstep(0.96, 1.0, u));
                    glow = pow(saturate(1.0 - u * 8.0), 2.0);
                }
                else
                {
                    output.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                    return output;
                }

                float3 eye = _WorldSpaceCameraPos.xyz;
                float3 toCentre = centre - eye;
                float distance = length(toCentre);
                float3 view = toCentre / max(1e-3, distance);
                // A puff the camera is inside is a veil over the whole view, not a puff: faded out.
                alpha *= saturate((distance - 0.5 * size) / max(1.0, size));

                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 upward = UNITY_MATRIX_V[1].xyz;
                float3 worldPos = centre + right * (input.corner.x - 0.5) * size + upward * (input.corner.y - 0.5) * size * stretch;

                /* Lit as a cloud of grains: the sky round it, and the sun — brighter looking toward it,
                 * which is how ash and dust scatter — on the column's sunward side and the umbrella's top,
                 * the far side and the underside in their own shadow. */
                Light mainLight = GetMainLight();
                float2 sunFlat = mainLight.direction.xz;
                float sunSide = dot(sunFlat, sunFlat) > 1e-6 ? dot(sideways, normalize(sunFlat)) : 0.0;
                float selfShadow = population == 0 || population == 4 ? lerp(0.35, 1.0, saturate(0.5 + 0.5 * sunSide)) : shade;
                float forward = 0.55 + 0.9 * pow(saturate(dot(view, mainLight.direction)), 6.0);
                float3 light = max(0.0, FishTrilight(-view)) + mainLight.color * selfShadow * forward * saturate(mainLight.direction.y * 4.0 + 0.2);

                output.positionCS = TransformWorldToHClip(worldPos);
                float tile = floor(frac(r.w * 5.17 + r.y) * 4.0);
                float row = _PlumeForm.w;
                output.uv = float2((input.corner.x + tile) * 0.25, 1.0 - (row + 1.0 - input.corner.y) * 0.125);
                float3 lit = _PlumeColor.rgb * light + float3(1.0, 0.45, 0.15) * (_PlumeColor.a * glow);
                output.color = float4(lit, saturate(alpha * _PlumeForm.z));
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = half4((half3)input.color.rgb, (half)input.color.a * texel.a);
                clip(color.a - 0.002);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
