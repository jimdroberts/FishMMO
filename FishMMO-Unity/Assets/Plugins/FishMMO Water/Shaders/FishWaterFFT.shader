Shader "Hidden/FishMMO/Water/FFT"
{
    // The ocean's FFT as render passes, for machines with no compute shaders: WebGL2 and GLES3.
    //
    // The same arithmetic as FishWaterFFT.compute — both include FishWaterSpectrum.hlsl — so the two
    // draw the same sea from the same seed. What differs is only how the butterflies are scheduled.
    // The compute version runs all eight stages of a row inside one thread group, in shared memory;
    // a fragment shader has no shared memory, so here each stage is its own pass over the whole
    // tile, ping-ponging between two targets: eight passes a row, eight a column, per cascade.
    //
    // WHY NOT A BAKE, which is what this fallback was first meant to be. A baked loop has to hold
    // frames close enough together that the shortest waves do not smear when they are blended —
    // about thirty a second for the fine cascade — which even at reduced resolution is tens to
    // hundreds of megabytes to download, and it freezes the sea in the one state it was baked in.
    // These passes are 54 draws of a 256² tile a frame, with nothing to download, and they follow
    // the wind like the compute version does.
    SubShader
    {
        ZTest Always ZWrite Off Cull Off Blend Off

        HLSLINCLUDE
        #pragma target 3.5
        #include "FishWaterSpectrum.hlsl"

        float _SeedValue;       // the cascade's seed — a float, since integer uniforms are not everywhere
        float _Stage;           // butterfly stage, 1 … LOG_SIZE
        float _Horizontal;      // 1 transforms rows, 0 columns
        Texture2D<float4> _H0Source;
        Texture2D<float4> _SourceA;
        Texture2D<float4> _SourceB;

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        struct Pair
        {
            float4 a : SV_Target0;
            float4 b : SV_Target1;
        };

        // One triangle over the whole target; no vertex buffer at all.
        Varyings Vert(uint vertexID : SV_VertexID)
        {
            float2 corner = float2((vertexID << 1) & 2, vertexID & 2);
            Varyings output;
            output.positionCS = float4(corner * 2.0 - 1.0, 0.0, 1.0);
            return output;
        }

        /* The texel this fragment writes. Read back with Load at the same index, never with a
         * sampler: every pass reads and writes by index, which is also what makes the result the
         * same on APIs whose pixel rows run opposite ways — both the write and the read use the
         * API's own row order. */
        uint2 Texel(Varyings input)
        {
            return uint2(input.positionCS.xy);
        }

        float4 Read(Texture2D<float4> source, uint2 texel)
        {
            return source.Load(int3(texel, 0));
        }
        ENDHLSL

        Pass
        {
            Name "InitialSpectrum"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                return FishInitialSpectrum(Texel(input), (uint)_SeedValue);
            }
            ENDHLSL
        }

        Pass
        {
            Name "TimeSpectrum"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            Pair Frag(Varyings input)
            {
                uint2 id = Texel(input);
                Pair output;
                FishTimeSpectrum(id, Read(_H0Source, id), output.a, output.b);
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Butterfly"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            Pair Frag(Varyings input)
            {
                uint2 id = Texel(input);
                uint stage = (uint)_Stage;
                bool horizontal = _Horizontal > 0.5;
                uint x = horizontal ? id.x : id.y;

                uint m = 1u << stage;
                uint halfSpan = m >> 1;
                uint j = x & (halfSpan - 1u);
                uint lower = x & ~halfSpan;
                uint upper = lower + halfSpan;
                bool isUpper = (x & halfSpan) != 0u;
                float2 w = Twiddle(j, m);

                // The first stage reads its input in bit-reversed order, exactly as the compute
                // version loads a row into shared memory; after that the order is natural.
                if (stage == 1u)
                {
                    lower = BitReverse(lower);
                    upper = BitReverse(upper);
                }
                uint2 here = horizontal ? uint2(lower, id.y) : uint2(id.x, lower);
                uint2 partner = horizontal ? uint2(upper, id.y) : uint2(id.x, upper);

                float4 a = Read(_SourceA, here);
                float4 b = ComplexMulPair(Read(_SourceA, partner), w);
                float4 c = Read(_SourceB, here);
                float4 d = ComplexMulPair(Read(_SourceB, partner), w);

                Pair output;
                output.a = isUpper ? (a - b) : (a + b);
                output.b = isUpper ? (c - d) : (c + d);
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Assemble"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            Pair Frag(Varyings input)
            {
                uint2 id = Texel(input);
                uint2 east = uint2((id.x + 1) & (SIZE - 1), id.y);
                uint2 west = uint2((id.x + SIZE - 1) & (SIZE - 1), id.y);
                uint2 north = uint2(id.x, (id.y + 1) & (SIZE - 1));
                uint2 south = uint2(id.x, (id.y + SIZE - 1) & (SIZE - 1));

                Pair output;
                FishAssemble(id,
                    Read(_SourceA, id), Read(_SourceA, east), Read(_SourceA, west), Read(_SourceA, north), Read(_SourceA, south),
                    Read(_SourceB, id), Read(_SourceB, east), Read(_SourceB, west), Read(_SourceB, north), Read(_SourceB, south),
                    output.a, output.b);
                return output;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
