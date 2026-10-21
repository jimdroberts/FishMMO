#ifndef FISHMMO_ROCK_CONTACT_INCLUDED
#define FISHMMO_ROCK_CONTACT_INCLUDED

// Rock crevices: where one rock meets another, the seam darkens toward its bottom and fills with the surrounding
// ground's own material — what real rock piles do, soil and grit settling between the stones (2026-10-08).
// Rocks only: FishMMO/Weather Lit Indirect's forward pass, on the draws that take the contact blend.
//
// RockContactField (FishMMO.Client/World/Terrain) keeps, for the camera:
//   _FishRockSdf    one signed distance field per rock SHAPE, 32³ bricks side by side in a 16-bit float 3D atlas, each
//                   the distance (the shape's local units, negative inside) from a voxel of a box around the mesh to its
//                   nearest triangle;
//   _FishRockList   the rocks near the camera: world-to-local transform, origin and scale, and their shape's brick;
//   _FishRockSlots  a grid of cells around the camera, FISH_ROCK_SLOTS list indices each (~0 empty): the rocks whose
//                   box, grown by the crevice width, reaches the cell.
// A pixel looks up its cell and measures, through each OTHER listed rock's own field, how far it is from that rock.
// Measured in the world, not on screen: a seam looks the same from every angle and distance, and rocks hidden from
// view still count.

#include "FishGroundColour.hlsl"

#define FISH_ROCK_SLOTS 8
#define FISH_ROCK_BRICK 32

struct FishRock
{
    float4 row0;        // world to local: xyz the 3×3 row, w the translation
    float4 row1;
    float4 row2;
    float4 origin;      // xyz the rock's world position, w world metres per local unit
    float4 brickMin;    // xyz its shape's brick, local min corner; w the field's unit (1: local units)
    float4 brickSize;   // xyz the brick's local size; w its index in the atlas
    float4 flags;       // x 1 a rock (crevices), y its look + 1 (the fillets' material; 0 none)
};

StructuredBuffer<FishRock> _FishRockList;
StructuredBuffer<uint> _FishRockSlots;
TEXTURE3D(_FishRockSdf);
float4 _FishRockGrid;       // xyz the grid's world min corner, w the cell size (m)
float4 _FishRockGridDims;   // xyz cells, w 1 while the grid is live
float4 _FishRockAtlas;      // x bricks along x, y 1 / atlas width, z 1 / atlas depth (texels)
float4 _FishRockCrevice;    // x crevice width (m), y how dark its bottom, z how much soil fills it, w 1 when on

// Metres from a point to one listed rock's surface through that rock's own field (negative inside); false when the
// point is outside the rock's brick (farther than the brick's padding: no crevice from it).
bool FishRockSampleDistance(FishRock rock, float3 positionWS, out float distanceWS)
{
    distanceWS = 1e4;
    float3 local = float3(dot(rock.row0.xyz, positionWS) + rock.row0.w,
                          dot(rock.row1.xyz, positionWS) + rock.row1.w,
                          dot(rock.row2.xyz, positionWS) + rock.row2.w);
    float3 uvw = (local - rock.brickMin.xyz) / rock.brickSize.xyz;
    if (any(uvw < 0.0) || any(uvw > 1.0))
    {
        return false;
    }
    // Voxel centres only, so a brick never filters into its neighbour in the atlas.
    float3 voxel = clamp(uvw * FISH_ROCK_BRICK, 0.5, FISH_ROCK_BRICK - 0.5);
    uint brick = (uint)rock.brickSize.w;
    uint bricksX = (uint)_FishRockAtlas.x;
    float3 atlas = float3((float)((brick % bricksX) * FISH_ROCK_BRICK) + voxel.x,
                          voxel.y,
                          (float)((brick / bricksX) * FISH_ROCK_BRICK) + voxel.z);
    float3 uv = float3(atlas.x * _FishRockAtlas.y, atlas.y / FISH_ROCK_BRICK, atlas.z * _FishRockAtlas.z);
    distanceWS = SAMPLE_TEXTURE3D_LOD(_FishRockSdf, sampler_LinearClamp, uv, 0.0).r * rock.brickMin.w * rock.origin.w;
    return true;
}

// The grid cell under a point, or false when the grid is off or the point is outside it.
bool FishRockCellAt(float3 positionWS, out uint cell)
{
    cell = 0u;
    if (_FishRockGridDims.w <= 0.0)
    {
        return false;
    }
    int3 c = int3(floor((positionWS - _FishRockGrid.xyz) / _FishRockGrid.w));
    if (any(c < 0) || any(c >= int3(_FishRockGridDims.xyz)))
    {
        return false;
    }
    cell = (uint)c.x + (uint)_FishRockGridDims.x * ((uint)c.y + (uint)_FishRockGridDims.y * (uint)c.z);
    return true;
}

// Metres from a point to the nearest rock listed in its cell other than its own (ownOrigin: this rock's world
// position); far when there is none, the grid is off or the point is outside it.
float FishRockDistance(float3 positionWS, float3 ownOrigin)
{
    uint cell;
    if (_FishRockCrevice.w <= 0.0 || !FishRockCellAt(positionWS, cell))
    {
        return 1e4;
    }
    float best = 1e4;
    // Slots fill in order, so the first empty one ends the cell's list.
    UNITY_LOOP
    for (uint s = 0; s < FISH_ROCK_SLOTS; s++)
    {
        uint index = _FishRockSlots[cell * FISH_ROCK_SLOTS + s];
        if (index == 0xFFFFFFFFu)
        {
            break;
        }
        FishRock rock = _FishRockList[index];
        float d;
        if (distance(rock.origin.xyz, ownOrigin) > 0.01 && FishRockSampleDistance(rock, positionWS, d))
        {
            // Signed: inside the other rock is as deep in the crevice as its surface.
            best = min(best, max(d, 0.0));
        }
    }
    return best;
}

// The distances from a point to the nearest rock listed in its cell (d1) and to the next nearest (d2), metres, far
// when there are none: a fillet's pixel (RockFillets), which belongs to no one rock. Where a fillet meets a rock's face,
// d1 is that rock's (about 0) and d2 is what the rock's own pixel there measures (FishRockDistance), so its crevice
// carries on across the join unbroken.
void FishRockNearestTwo(float3 positionWS, out float d1, out float d2)
{
    d1 = 1e4;
    d2 = 1e4;
    uint cell;
    if (!FishRockCellAt(positionWS, cell))
    {
        return;
    }
    UNITY_LOOP
    for (uint s = 0; s < FISH_ROCK_SLOTS; s++)
    {
        uint index = _FishRockSlots[cell * FISH_ROCK_SLOTS + s];
        if (index == 0xFFFFFFFFu)
        {
            break;
        }
        float d;
        if (FishRockSampleDistance(_FishRockList[index], positionWS, d))
        {
            d = max(d, 0.0);
            d2 = d < d1 ? d1 : min(d2, d);
            d1 = min(d1, d);
        }
    }
}

// The crevice's darkening (shade, 1 none) and soil (0..1) at a distance d from the other rock, for a surface whose
// normal before any normal map is surfaceNormalWS: the rock pixels' and the fillets' one rule.
void FishRockCreviceAt(float d, half3 surfaceNormalWS, out half soil, out half shade)
{
    soil = 0.0h;
    shade = 1.0h;
    half t = (half)saturate(1.0 - d / max(0.01, _FishRockCrevice.x));
    if (t <= 0.0h || _FishRockCrevice.w <= 0.0)
    {
        return;
    }
    half crevice = t * t;
    shade = 1.0h - crevice * (half)_FishRockCrevice.y;
    half facing = saturate((normalize(surfaceNormalWS).y + 0.2h) / 0.6h);
    soil = crevice * (half)_FishRockCrevice.z * lerp(0.35h, 1.0h, facing);
}

// The crevice at a rock pixel: shade (1 none) darkens it toward the crevice's bottom; soil (0..1) is how much of the
// terrain's surface fills it there, on the parts that face up (a vertical seam takes the shadow and a little soil).
// The caller renders that soil through the same ground path as the base band (FishGroundContact, FishGroundLit).
// surfaceNormalWS is the mesh's normal before any normal map; ownOrigin this rock's world position.
void FishRockCrevice(float3 positionWS, float3 ownOrigin, half3 surfaceNormalWS, out half soil, out half shade)
{
    soil = 0.0h;
    shade = 1.0h;
    if (FishContactParams().w <= 0.0)
    {
        return;
    }
    FishRockCreviceAt(FishRockDistance(positionWS, ownOrigin), surfaceNormalWS, soil, shade);
}

#endif
