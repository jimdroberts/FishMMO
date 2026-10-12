// Flames on the sites' fires (SiteEmbers draws them with the sparks): a pre-built field of quads that the vertex shader
// hands out among the nearest fires, hearths, forges and torches as tongues of flame — each a quad standing on the fire's
// bed and turned to the camera about the vertical, its height breathing on its own beat and its tip leaning downwind —
// and the fragment shades as a flame: a teardrop torn at its top by noise scrolling up it, white-yellow at its heart,
// orange, then red at its edges and tip. Added, never covering. Nothing is simulated and nothing is stored.
// (2026-10-12: "Fire in houses looks terrible, just a bunch of little orange balls" — there were only the sparks.)
Shader "FishMMO/World/Site Flames"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+45"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "SiteFlames"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishWeather.hlsl"

            #define FLAME_SOURCES 16

            // Per draw (SiteEmbers): the fires, nearest first. xyz the fire's bed, w its kind (0 none, 1 a fire, 2 a forge,
            // 3 a hearth, 4 a torch).
            float4 _FlameSources[FLAME_SOURCES];
            // Per source: x how strong it shows (0..1, faded with distance), y how high it burns now (0..1), zw unused.
            float4 _FlameStrength[FLAME_SOURCES];
            float4 _FlameWind;   // xz the wind near the ground (m/s), y the night (0..1), w unused

            struct Attributes
            {
                float3 seed : POSITION;     // the tongue's own position in the unit box, shared by its 4 corners
                float2 corner : TEXCOORD0;  // 0/1 corner of the quad
                float4 random : TEXCOORD1;  // x width, y height, z beat, w phase
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;      // x across 0..1, y up 0..1
                float4 flame : TEXCOORD1;   // x radiance, y phase, z time's scroll, w shown
                float fogCoord : TEXCOORD2;
            };

            // A tongue's size by kind, metres: x the bed's radius the tongues stand on, y their height, z their width share.
            float3 FlameShape(int kind)
            {
                return kind == 2 ? float3(0.16, 0.32, 0.55)   // a forge: low, hard-blown
                     : kind == 3 ? float3(0.28, 0.5, 0.5)     // a hearth: a domestic fire under a hood
                     : kind == 4 ? float3(0.03, 0.26, 0.6)    // a torch's head
                     : float3(0.3, 0.85, 0.45);               // a campfire
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float time = _FishWeatherMisc.w;
                int source = min(FLAME_SOURCES - 1, (int)floor(input.seed.x * FLAME_SOURCES));
                float4 fire = _FlameSources[source];
                float4 strength = _FlameStrength[source];
                int kind = (int)round(fire.w);
                float3 shape = FlameShape(kind);

                // Where it stands on the bed: a disc, the tallest tongues nearest the middle.
                float angle = 6.2831853 * input.seed.z;
                float reach = shape.x * sqrt(input.seed.y);
                float3 foot = fire.xyz + float3(cos(angle) * reach, 0.0, sin(angle) * reach);

                // Its height breathes on its own beat, under the fire's slower burn; its width follows.
                float beat = FishWrapAngular(lerp(5.0, 11.0, input.random.z));
                float breath = 0.75 + 0.25 * sin(time * beat + 6.2831853 * input.random.w);
                float centrality = 1.0 - 0.45 * saturate(reach / max(shape.x, 1e-3));
                float height = shape.y * lerp(0.45, 1.0, input.random.y) * breath * centrality * lerp(0.7, 1.15, strength.y);
                float width = height * shape.z * lerp(0.7, 1.2, input.random.x);

                // Turned to the camera about the vertical; its upper part leans downwind and sways.
                float3 toCamera = _WorldSpaceCameraPos.xyz - foot;
                toCamera.y = 0.0;
                float3 view = toCamera / max(length(toCamera), 1e-3);
                float3 side = float3(view.z, 0.0, -view.x);
                float swayRate = FishWrapAngular(lerp(2.0, 4.0, input.random.w));
                float lean = input.corner.y * input.corner.y;
                float3 sway = float3(_FlameWind.x, 0.0, _FlameWind.z) * 0.06 * height
                    + side * (0.08 * height * sin(time * swayRate + 6.2831853 * input.random.z));
                float shown = step(0.5, (float)kind) * step(0.001, strength.x);
                float3 worldPos = foot + side * ((input.corner.x - 0.5) * width * shown) + float3(0.0, input.corner.y * height * shown, 0.0) + sway * lean * shown;

                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = input.corner;
                // Bright enough to read by day, brighter by night; dimmer far off.
                output.flame = float4(lerp(1.6, 2.6, _FlameWind.y) * strength.x, 6.2831853 * input.random.w, time * FishWrapCycles(lerp(1.2, 1.8, input.random.z)), shown);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Value noise, smooth, in 0..1.
            float FlameHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float FlameNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = FlameHash(i), b = FlameHash(i + float2(1.0, 0.0)), c = FlameHash(i + float2(0.0, 1.0)), d = FlameHash(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.flame.w < 0.5)
                {
                    discard;
                }
                float2 uv = input.uv;
                // Noise scrolling up the tongue: it tears the tip and wobbles the sides.
                float2 q = float2(uv.x * 2.5 + input.flame.y, uv.y * 2.0 - input.flame.z);
                float n = 0.65 * FlameNoise(q * 1.7) + 0.35 * FlameNoise(q * 4.1 + 7.3);
                float x = (uv.x - 0.5) + (n - 0.5) * 0.35 * uv.y;
                // The teardrop: round at its foot, drawing to a point at its tip.
                float halfWidth = 0.5 * smoothstep(0.0, 0.18, uv.y) * pow(saturate(1.0 - uv.y), 0.7) + 0.02;
                float across = abs(x) / halfWidth;
                float body = saturate(1.0 - across);
                body *= saturate((1.0 - uv.y) * 1.6 + (n - 0.55) * 1.2);
                if (body < 1e-3)
                {
                    discard;
                }
                // Heat: hottest low and in the middle.
                float heat = saturate(body * (1.15 - uv.y * 0.75));
                float3 colour = heat > 0.66 ? lerp(float3(1.0, 0.62, 0.18), float3(1.0, 0.92, 0.62), (heat - 0.66) / 0.34)
                              : heat > 0.33 ? lerp(float3(0.95, 0.28, 0.04), float3(1.0, 0.62, 0.18), (heat - 0.33) / 0.33)
                              : lerp(float3(0.35, 0.04, 0.0), float3(0.95, 0.28, 0.04), heat / 0.33);
                float3 rgb = colour * body * input.flame.x;
                rgb = MixFogColor(rgb, float3(0.0, 0.0, 0.0), input.fogCoord);
                return half4((half3)rgb, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
