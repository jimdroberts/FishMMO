Shader "Hidden/FishMMO/Water/ShoreFoamMemory"
{
    // Foam that stays behind.
    //
    // The shore pass is stateless: each frame it works out where the swash is and draws the foam at
    // its lip, and the moment the water drains the foam goes with it. Real foam does not. It is
    // made by the breaking bore, carried up at the lip, and STRANDED where the lip stalls — a lacy
    // line at the top of every run-up that sits on the wet sand for seconds, bubbles bursting,
    // before it is gone. That needs memory, and this is it: a texture over the shore field's own
    // rectangle, where each step keeps what was there, faded, and adds whatever the swash is
    // laying now. The shore pass reads it back as the foam left on the sand.
    //
    // It runs on the swash's clock, so it fades only while the world's time runs.
    SubShader
    {
        ZTest Always ZWrite Off Cull Off Blend Off

        Pass
        {
            Name "FoamMemory"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishWaterShoreCommon.hlsl"

            Texture2D<float4> _FoamMemoryPrevious;
            float4 _FoamMemorySize;      // xy texels, zw one over them
            float4 _FoamMemoryStep;      // x what is kept of the last step, y how strongly foam is laid

            float _FishWaterLevel;       // the sea's surface now, tide included
            float _FishWaterMeanLevel;   // the level the shore field was built against
            float _FishWaterShoreTexel;  // metres per texel of the shore field

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            // One triangle over the whole target.
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                float2 corner = float2((vertexID << 1) & 2, vertexID & 2);
                Varyings output;
                output.positionCS = float4(corner * 2.0 - 1.0, 0.0, 1.0);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Read and written by index, so the rows line up on every API.
                uint2 texel = uint2(input.positionCS.xy);
                float kept = _FoamMemoryPrevious.Load(int3(texel, 0)).r * _FoamMemoryStep.x;

                // The world point this texel covers: the memory spans the shore field exactly.
                float2 uv = (float2(texel) + 0.5) * _FoamMemorySize.zw;
                float2 xz = _FishWaterShoreRect.xy + uv * _FishWaterShoreRect.zw;
                float2 shore = FishWaterShoreSample(xz);
                if (shore.y > 990.0)
                {
                    return float4(kept, 0.0, 0.0, 0.0);
                }
                // Too far from the water for any swash to reach: only fade what is there.
                if (abs(shore.y) > max(_FishWaterSwashReach * 1.6, 8.0) + 60.0)
                {
                    return float4(kept, 0.0, 0.0, 0.0);
                }

                /* The same edge the shore pass draws the swash against: the mean waterline moved by
                 * the tide over the slope of the beach, or the foam would be laid thirty metres from
                 * where the water is. */
                float slopeStep = max(4.0, _FishWaterShoreTexel * 4.0);
                float depthEast = FishWaterShoreSample(xz + float2(slopeStep, 0.0)).x;
                float depthNorth = FishWaterShoreSample(xz + float2(0.0, slopeStep)).x;
                float slope = max(0.01, length(float2(depthEast - shore.x, depthNorth - shore.x)) / slopeStep);
                float tide = _FishWaterLevel - _FishWaterMeanLevel;
                float edgeDistance = shore.y + clamp(tide / slope, -60.0, 60.0);

                float laid = FishWaterSwashFoamDeposit(edgeDistance) * _FoamMemoryStep.y;
                return float4(max(kept, laid), 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
