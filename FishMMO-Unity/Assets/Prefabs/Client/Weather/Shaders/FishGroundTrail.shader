// The trail map round the camera (GroundTrailMap): r how hard the ground is trodden, g how flat the grass lies.
// Three passes, all into the one map, once a frame: the strips the camera has moved into are cleared, everything
// fades a little, and this frame's prints and pushed-aside grass are stamped in. Read by FishWeather.hlsl FishTrailAt.
//
// The map is world-aligned and wraps: a texel holds the world texel it equals modulo the map's size. Clear and stamp
// draw in the map's own 0..1 square (GroundTrailMap sets an orthographic view of it), so where a stamp crosses the
// wrap it is drawn again on the far side.
Shader "FishMMO/Ground Trail"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        ENDHLSL

        // ── 0: fade. Multiplies the map by how much of each channel lasts this frame. ──
        Pass
        {
            Name "TrailFade"
            Blend Zero SrcColor
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // x world seconds this frame, y how fast a print fades on its own (per second), z how fast falling snow fills
            // one (per second, at the heaviest fall), w how far pushed-aside grass rises this frame (its motion seconds
            // over its rise time).
            float4 _TrailFade;
            // The weather (WeatherShaderGlobals): x of the precipitation how much falls, y of the mix the share that is snow.
            float4 _FishWeatherPrecip;
            float4 _FishWeatherMix;

            float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(vertexID);
            }

            float4 Frag() : SV_Target
            {
                float snowing = saturate(_FishWeatherPrecip.x) * saturate(_FishWeatherMix.y);
                float press = exp(-_TrailFade.x * (_TrailFade.y + _TrailFade.z * snowing));
                float grass = exp(-_TrailFade.w);
                return float4(press, grass, 1.0, 1.0);
            }
            ENDHLSL
        }

        // ── 1: clear the strips the camera has moved into: ground that left the map on one side and comes in on the other. ──
        Pass
        {
            Name "TrailClear"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Up to eight rectangles of the map, x/y min and max, 0..1.
            float4 _TrailRects[8];

            static const float2 Corners[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION
            {
                float4 rect = _TrailRects[min(vertexID / 6u, 7u)];
                float2 corner = Corners[vertexID % 6u];
                // Its sides lie on texel edges, so every texel whose middle is inside is cleared and no other.
                float2 at = lerp(rect.xy, rect.zw, corner);
                return mul(UNITY_MATRIX_VP, float4(at, 0.0, 1.0));
            }

            float4 Frag() : SV_Target
            {
                return 0.0;
            }
            ENDHLSL
        }

        // ── 2: stamp: prints into the ground, bodies through the grass. The deeper of what is there and the stamp wins. ──
        Pass
        {
            Name "TrailStamp"
            BlendOp Max
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Two to a stamp: xy where its middle is in the map (0..1), zw which way it points (unit, world x/z); then
            // xy half its length and width (0..1 of the map), z how hard it presses the ground, w how flat it lays the grass.
            float4 _TrailStamps[256];

            static const float2 Corners[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;       // -1..1 across the stamp
                float2 strength : TEXCOORD1;    // press, grass
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                // Four copies of each stamp: itself, and shifted by the map's whole size toward the near edge on each axis,
                // so one crossing the wrap is drawn on both sides of it.
                uint copy = (vertexID / 6u) % 4u;
                uint stamp = min(vertexID / 24u, 127u);
                float4 a = _TrailStamps[stamp * 2u];
                float4 b = _TrailStamps[stamp * 2u + 1u];
                float2 corner = Corners[vertexID % 6u];
                float2 along = a.zw;
                float2 side = float2(-along.y, along.x);
                float2 at = a.xy + along * corner.x * b.x + side * corner.y * b.y;
                float2 shift = float2(a.x > 0.5 ? -1.0 : 1.0, a.y > 0.5 ? -1.0 : 1.0);
                at += float2((copy & 1u) != 0u ? shift.x : 0.0, (copy & 2u) != 0u ? shift.y : 0.0);
                Varyings output;
                output.positionCS = mul(UNITY_MATRIX_VP, float4(at, 0.0, 1.0));
                output.local = corner;
                output.strength = b.zw;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // An oval with a soft rim: a print's floor is flat, its walls slope.
                float falloff = 1.0 - smoothstep(0.45, 1.0, length(input.local));
                return float4(input.strength * falloff, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
