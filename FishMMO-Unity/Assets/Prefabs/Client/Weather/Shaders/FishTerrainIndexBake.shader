// FishMMO: bakes one terrain tile's index map from its alphamaps (TerrainIndexMapBaker, TerrainIndexMap.cs).
//
// Drawn as one full-screen triangle into an R32G32_UInt target the size of the alphamaps: a pixel per texel. Each pixel
// reads every layer's weight at its texel (the tile's alphamaps, copied into one array: map k in slice k, layer 4k + c in
// channel c) and writes the four strongest — ids in x, a byte each, strongest first; weights in y, each less the fifth.
// Integer arithmetic throughout (weights are bytes), so this is TerrainIndexMap.BakeTexel exactly; the binder checks a
// sample of texels against it in the editor ("[Terrain index] check").
//
// On the GPU because the alphamaps are: a brush stroke writes them there before the CPU copy is synchronised, so a bake
// from the GPU copy keeps painting live, as reading the alphamaps per pixel did.
Shader "Hidden/FishMMO/Terrain Index Bake"
{
    SubShader
    {
        Pass
        {
            Name "Bake"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            // The tile's alphamaps, map k in slice k (UNorm bytes).
            Texture2DArray<float4> _FishIndexSource;
            // x layers (terrainData.alphamapLayers), y maps (slices), z texels across, w texels along.
            float4 _FishIndexBakeParams;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                // One triangle over the whole target; only the pixel positions are read.
                Varyings o;
                float2 corner = float2((vertexID << 1) & 2, vertexID & 2);
                o.positionCS = float4(corner * 2.0 - 1.0, 0.0, 1.0);
                return o;
            }

            // TerrainIndexMap.Consider: the four strongest kept sorted, and the fifth; only a strictly greater weight moves a
            // layer up, so between equals the one met first (the lower index) stays ahead.
            void Consider(float weight, int index, inout float4 top, inout int4 ids, inout float fifth)
            {
                bool b0 = weight > top.x;
                bool b1 = weight > top.y;
                bool b2 = weight > top.z;
                bool b3 = weight > top.w;
                fifth = b3 ? top.w : max(fifth, weight);
                float4 t = top;
                int4 i = ids;
                top.w = b2 ? t.z : (b3 ? weight : t.w);   ids.w = b2 ? i.z : (b3 ? index : i.w);
                top.z = b1 ? t.y : (b2 ? weight : t.z);   ids.z = b1 ? i.y : (b2 ? index : i.z);
                top.y = b0 ? t.x : (b1 ? weight : t.y);   ids.y = b0 ? i.x : (b1 ? index : i.y);
                top.x = b0 ? weight : t.x;                ids.x = b0 ? index : i.x;
            }

            uint2 Frag(Varyings input) : SV_Target
            {
                int2 texel = (int2)input.positionCS.xy;
                int layers = (int)_FishIndexBakeParams.x;
                int maps = (int)_FishIndexBakeParams.y;
                // Empty slots below every weight, so layers of weight zero fill them in index order: the four ids stay distinct.
                float4 top = -1.0;
                float fifth = -1.0;
                int4 ids = 0;
                [loop]
                for (int k = 0; k < maps; k++)
                {
                    // Bytes, exactly: a UNorm8 texel reads back as n / 255.
                    float4 c = round(_FishIndexSource.Load(int4(texel, k, 0)) * 255.0);
                    int first = k * 4;
                    if (first + 0 < layers) Consider(c.r, first + 0, top, ids, fifth);
                    if (first + 1 < layers) Consider(c.g, first + 1, top, ids, fifth);
                    if (first + 2 < layers) Consider(c.b, first + 2, top, ids, fifth);
                    if (first + 3 < layers) Consider(c.a, first + 3, top, ids, fifth);
                }
                // Fewer than four layers at all: layers past the palette, at zero.
                [loop]
                for (int extra = max(layers, 0); extra < 4; extra++)
                {
                    Consider(0.0, extra, top, ids, fifth);
                }
                uint4 w = (uint4)(top - max(fifth, 0.0));
                uint4 n = (uint4)ids;
                return uint2(n.x | (n.y << 8) | (n.z << 16) | (n.w << 24), w.x | (w.y << 8) | (w.z << 16) | (w.w << 24));
            }
            ENDHLSL
        }
    }
}
