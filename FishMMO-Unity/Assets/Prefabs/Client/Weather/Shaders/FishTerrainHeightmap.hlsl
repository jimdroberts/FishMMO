// FishMMO: the client's own draw of the terrain heightmaps (TerrainHeightmapSystem, 2026-10-28), shared by the patch selection
// (FishTerrainHeightmap.compute) and the patches' vertex stage (FishMMO/Weather Terrain Array GPU, FISH_TERRAIN_GPU).
//
// Every tile of a scene is drawn as one quadtree of 16 x 16-quad patches (TerrainHeightmapMath.PatchQuads), all tiles in one
// indirect draw: a level-L patch takes every 2^L-th heightmap texel. Which patches are drawn is decided per camera on the GPU, a
// thread per quadtree node, from continuous LEVEL FIELDS, one per step between levels:
//
//     f_l(p) = log2(distance from the camera to p) - g_l(p) - c
//
// g_l is log2 of how far level l+1's surface misses the heightmap near p (its error, metres) less l+1: cells of one level-0
// patch, the most of the cell's texels, dilated so it falls at most TerrainHeightmapMath.FieldSlope a cell, raised so that
// g_l >= g_(l-1) - 1, and read bilinearly. c turns the camera's pixels into levels (TerrainHeightmapMath.LodBias). f_l >= l+1
// is then exactly "level l+1's error near p is within the pixel error at p's distance" — the engine's own rule, per point
// instead of per patch — and the raise makes f_(l-1) >= f_l - 1 everywhere, which the two proofs below lean on.
//
// SELECTION (exactly one patch over every point): a node of level L SPLITS where the smallest f_(L-1) over its box (camera
// distance to the box, the largest g over the cells its points read) is below L. Its children's boxes lie inside it and
// f_(L-2) >= f_(L-1) - 1, so a node that does not split has no child that does. A node is drawn when its parent splits and it
// does not (the roots: when it does not split; level 0: whenever its parent splits) — one per point, each thread alone.
//
// GEOMORPH (no cracks at any level difference): a vertex at texel g of a level-L patch slides toward the lower even texel of
// each coarser grid in turn, step l by t = saturate((f_l(p) - l - a) / (1 - a)) at the place it has reached, and takes the
// heightmap's height where it stops. Its place depends only on g and the fields, not on which patch draws it, so neighbours
// agree on every shared vertex; and a vertex on the edge of a coarser neighbour of level M has f_(M-1) >= M (that node did not
// split), so f_l >= l+1 for every l < M and it slides all the way onto the M grid, which is the neighbour's own edge.

#ifndef FISHMMO_TERRAIN_HEIGHTMAP_INCLUDED
#define FISHMMO_TERRAIN_HEIGHTMAP_INCLUDED

// Plain HLSL: the compute includes no SRP library.
#ifndef UNITY_BRANCH
#define UNITY_BRANCH [branch]
#endif
#ifndef UNITY_LOOP
#define UNITY_LOOP [loop]
#endif
#ifndef UNITY_UNROLL
#define UNITY_UNROLL [unroll]
#endif

// TerrainHeightmapMath.PatchQuads: quads along a patch's side at every level.
#define FISH_TERRAIN_PATCH_QUADS 16u
// The four lists the selection fills, an indirect command each (TerrainHeightmapMath.List*).
#define FISH_TERRAIN_LIST_SOLID 0u
#define FISH_TERRAIN_LIST_HOLED 1u
#define FISH_TERRAIN_LIST_SHADOW_SOLID 2u
#define FISH_TERRAIN_LIST_SHADOW_HOLED 3u

// One tile (TerrainHeightmapField.TileRecord, 96 bytes).
struct FishTerrainTile
{
    float4 origin;      // xyz the tile's world corner, w the metres a height read of 1 stands for
    float4 grid;        // x metres per heightmap texel along x, y along z, z quads along a side (resolution - 1), w quadtree levels
    float4 indexTexel;  // the tile's index map as the binder pushed it (_FishIndexMap_TexelSize): 1 / size, size
    float4 fadeTexel;   // its snow hole fade (_FishSnowHoleFade_TexelSize): 1 / size, size; size 1 is full lift everywhere
    int4 neighbours;    // the tiles across its -x, +x, -z and +z edges (-1: none)
    int4 slices;        // x its holes slice (-1: no holes), y its fade slice (-1: none), z 1 when the heights are R16, w 1 when an index map is bound
};

StructuredBuffer<FishTerrainTile> _FishTerrainTiles;
// The bound tile's existing terrainData.heightmapTexture, in its own format.
Texture2D<float4> _FishTerrainHeights;
uint _FishTerrainBuildTile;
// The fields g_l (see the top), a cell each (TerrainHeightmapMath.CellQuads texels square), field l from l * cells.
StructuredBuffer<float> _FishTerrainLodField;
// Per node (TerrainHeightmapMath.NodeIndex): x lowest height, y highest (world y), z the largest g_(level-1) its points can
// read (level 0: unused), w 1 when a hole texel touches it.
StructuredBuffer<float4> _FishTerrainNodes;
// The patches drawn, a list per command (FISH_TERRAIN_LIST_*), _FishTerrainLayout.x apart: x tile | level << 16, y node x | node z << 16.
StructuredBuffer<uint2> _FishTerrainPatches;

// Per scene (TerrainHeightmapField, on its materials and its compute):
float4 _FishTerrainLodGrid;   // xy world x/z of the field's corner, z metres a cell, w cells along x
float4 _FishTerrainLodScale;  // x cells along z, y a (the morph's start), z metres added over a node's highest point (lifted snow), w fields (levels - 1)
float4 _FishTerrainLayout;    // x patches a list holds, y nodes a tile holds, z tiles, w unused
// Per camera (TerrainHeightmapSystem, globals set before the camera renders):
float4 _FishTerrainLodView;   // xyz the eye the levels are chosen for, w c (TerrainHeightmapMath.LodBias)
float _FishTerrainLodOrtho;   // 1 when the camera is orthographic: the field then measures no distance

/// A tile's heightmap at texel <paramref name="texel"/>, metres above its origin: the engine's own unpacking (Common.hlsl
/// UnpackHeightmap: R16 is the height, two bytes are its low and high halves) times the height scale.
float FishTerrainHeightTexel(FishTerrainTile tile, uint tileIndex, int2 texel)
{
    int last = (int)tile.grid.z;
    texel = clamp(texel, int2(0, 0), int2(last, last));
    float4 raw = _FishTerrainHeights.Load(int3(texel, 0));
    float read01 = tile.slices.z != 0 ? raw.r : (raw.r + raw.g * 256.0) / 257.0;
    return read01 * tile.origin.w;
}

/// The heightmap between texels (a geomorph in flight): bilinear over the four texels round <paramref name="g"/>; exact at a texel.
float FishTerrainHeightAt(FishTerrainTile tile, uint tileIndex, float2 g)
{
    float2 base = floor(g);
    float2 f = g - base;
    int2 i = (int2)base;
    float h00 = FishTerrainHeightTexel(tile, tileIndex, i);
    UNITY_BRANCH
    if (f.x == 0.0 && f.y == 0.0)
    {
        return h00;
    }
    float h10 = FishTerrainHeightTexel(tile, tileIndex, i + int2(1, 0));
    float h01 = FishTerrainHeightTexel(tile, tileIndex, i + int2(0, 1));
    float h11 = FishTerrainHeightTexel(tile, tileIndex, i + int2(1, 1));
    return lerp(lerp(h00, h10, f.x), lerp(h01, h11, f.x), f.y);
}

/// Field g_l at world x/z: bilinear between cell centres, clamped at the field's edge.
float FishTerrainLogRoughAt(float2 xz, uint field)
{
    float2 c = (xz - _FishTerrainLodGrid.xy) / _FishTerrainLodGrid.z - 0.5;
    int2 size = int2((int)_FishTerrainLodGrid.w, (int)_FishTerrainLodScale.x);
    float2 b = floor(c);
    float2 f = c - b;
    int2 i0 = clamp((int2)b, int2(0, 0), size - 1);
    int2 i1 = clamp((int2)b + 1, int2(0, 0), size - 1);
    uint first = field * (uint)(size.x * size.y);
    float v00 = _FishTerrainLodField[first + i0.y * size.x + i0.x];
    float v10 = _FishTerrainLodField[first + i0.y * size.x + i1.x];
    float v01 = _FishTerrainLodField[first + i1.y * size.x + i0.x];
    float v11 = _FishTerrainLodField[first + i1.y * size.x + i1.x];
    return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
}

/// log2 of the distance the level field measures from: the camera's to the point, or none for an orthographic camera.
float FishTerrainLogDistance(float d)
{
    return _FishTerrainLodOrtho > 0.5 ? 0.0 : log2(max(d, 1e-3));
}

/// The level field f_l at a world point (see the top of this file).
float FishTerrainLodAt(float3 positionWS, uint field)
{
    return FishTerrainLogDistance(distance(positionWS, _FishTerrainLodView.xyz)) - FishTerrainLogRoughAt(positionWS.xz, field) - _FishTerrainLodView.w;
}

/// The world point of texel place <paramref name="p"/> at <paramref name="height"/> above the tile's origin.
float3 FishTerrainWorldAt(FishTerrainTile tile, float2 p, float height)
{
    return float3(tile.origin.x + p.x * tile.grid.x, tile.origin.y + height, tile.origin.z + p.y * tile.grid.y);
}

/// <summary>
/// Where a vertex of a level-<paramref name="level"/> patch at texel <paramref name="g"/> is drawn: slid down the coarser grids
/// by the field (see the top of this file), at the heightmap's height there. Returns the texel place it stops at.
/// </summary>
float2 FishTerrainMorph(FishTerrainTile tile, uint tileIndex, float2 g, uint level, out float height)
{
    uint levels = (uint)tile.grid.w;
    float a = _FishTerrainLodScale.y;
    float2 p = g;
    height = FishTerrainHeightTexel(tile, tileIndex, (int2)g);
    UNITY_LOOP
    for (uint l = level; l + 1u < levels; l++)
    {
        float t = saturate((FishTerrainLodAt(FishTerrainWorldAt(tile, p, height), l) - (float)l - a) / (1.0 - a));
        if (t <= 0.0)
        {
            break;
        }
        float stride = (float)(2u << l);
        float2 past = p - floor(p / stride) * stride;
        p -= past * t;
        height = FishTerrainHeightAt(tile, tileIndex, p);
    }
    return p;
}

/// A patch record's parts.
void FishTerrainPatchOf(uint2 patch, out uint tileIndex, out uint level, out uint2 node)
{
    tileIndex = patch.x & 0xFFFFu;
    level = patch.x >> 16;
    node = uint2(patch.y & 0xFFFFu, patch.y >> 16);
}

/// Nodes across a tile's side at <paramref name="level"/> (TerrainHeightmapMath.NodesAcross).
uint FishTerrainNodesAcross(uint quads, uint level)
{
    return (quads / FISH_TERRAIN_PATCH_QUADS) >> level;
}

/// Where a tile's level starts among its nodes (TerrainHeightmapMath.LevelOffset): the finer levels before it.
uint FishTerrainLevelOffset(uint quads, uint level)
{
    uint offset = 0u;
    UNITY_LOOP
    for (uint l = 0u; l < level; l++)
    {
        uint n = FishTerrainNodesAcross(quads, l);
        offset += n * n;
    }
    return offset;
}

/// A node's index in _FishTerrainNodes (TerrainHeightmapMath.NodeIndex).
uint FishTerrainNodeIndex(uint tileIndex, uint quads, uint level, uint2 node)
{
    return tileIndex * (uint)_FishTerrainLayout.y + FishTerrainLevelOffset(quads, level) + node.y * FishTerrainNodesAcross(quads, level) + node.x;
}

#endif
