// A rock shape's unsigned distance field, drawn into one brick of the shared 3D atlas (RockContactField,
// FishMMO.Client/World/Terrain): one full-viewport triangle per slice of the brick, each pixel the distance from
// its voxel's centre to the nearest triangle of the mesh, as 0..1 of the brick's range (R8).
//
// Drawn rather than computed: an 8-bit 3D texture is a render target on every API, while WebGPU's compute can only
// store 32-bit floats (four times the memory, per rock shape in a scene). Bricks lie side by side in x and z and each
// fills the atlas's whole height, so no viewport depends on an API's vertical flip: the voxel is read back from the
// very pixel position it is written at.
Shader "Hidden/FishMMO/RockSdf"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            Name "RockSdf"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define FISH_ROCK_BRICK 32

            StructuredBuffer<float4> _SdfPositions;   // the mesh's vertices, local space (w unused)
            StructuredBuffer<uint> _SdfIndices;       // its triangles
            uint _SdfTriangles;
            float4 _SdfMin;                           // xyz the brick's local min corner, w its range (local units) = 1.0
            float4 _SdfSize;                          // xyz its local size
            float4 _SdfSlice;                         // x the brick's first atlas column (texels), y this slice's voxel z

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }

            // Distance from p to triangle abc (Ericson, closest point on triangle).
            float PointTriangleDistance(float3 p, float3 a, float3 b, float3 c)
            {
                float3 ab = b - a, ac = c - a, ap = p - a;
                float d1 = dot(ab, ap), d2 = dot(ac, ap);
                if (d1 <= 0.0 && d2 <= 0.0) return length(ap);
                float3 bp = p - b;
                float d3 = dot(ab, bp), d4 = dot(ac, bp);
                if (d3 >= 0.0 && d4 <= d3) return length(bp);
                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0) return length(p - (a + ab * (d1 / (d1 - d3))));
                float3 cp = p - c;
                float d5 = dot(ab, cp), d6 = dot(ac, cp);
                if (d6 >= 0.0 && d5 <= d6) return length(cp);
                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0) return length(p - (a + ac * (d2 / (d2 - d6))));
                float va = d3 * d6 - d5 * d4;
                if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0) return length(p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)))));
                float denom = 1.0 / (va + vb + vc);
                return length(p - (a + ab * (vb * denom) + ac * (vc * denom)));
            }

            float Frag(Varyings input) : SV_Target
            {
                float3 voxel = float3(floor(input.positionCS.x - _SdfSlice.x), floor(input.positionCS.y), _SdfSlice.y);
                float3 p = _SdfMin.xyz + (voxel + 0.5) / FISH_ROCK_BRICK * _SdfSize.xyz;
                float best = 1e30;
                UNITY_LOOP
                for (uint t = 0; t < _SdfTriangles; t++)
                {
                    float3 a = _SdfPositions[_SdfIndices[t * 3u]].xyz;
                    float3 b = _SdfPositions[_SdfIndices[t * 3u + 1u]].xyz;
                    float3 c = _SdfPositions[_SdfIndices[t * 3u + 2u]].xyz;
                    best = min(best, PointTriangleDistance(p, a, b, c));
                }
                return saturate(best / max(_SdfMin.w, 1e-5));
            }
            ENDHLSL
        }
    }
}
