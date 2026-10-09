#ifndef FISHMMO_ROCK_CONTACT_INCLUDED
#define FISHMMO_ROCK_CONTACT_INCLUDED

// Rock crevices: where one rock meets another, the seam darkens toward its bottom and fills with the surrounding
// ground's own material — what real rock piles do, soil and grit settling between the stones (Jim, 2026-10-08).
// Rocks only: FishMMO/Weather Lit Indirect's forward pass, on the draws that take the contact blend.
//
// RockContactField (FishMMO.Client/World/Terrain) keeps, for the camera:
//   _FishRockSdf    one unsigned distance field per rock SHAPE, 32³ bricks side by side in an R8 3D atlas, each the
//                   distance (0..1 of its brick's range) from a voxel of a box around the mesh to its nearest triangle;
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
    float4 brickMin;    // xyz its shape's brick, local min corner; w the brick's range (local units at 1.0)
    float4 brickSize;   // xyz the brick's local size; w its index in the atlas
    float4 flags;       // x 1 a rock (crevices), y 1 a skirt
};

StructuredBuffer<FishRock> _FishRockList;
StructuredBuffer<uint> _FishRockSlots;
TEXTURE3D(_FishRockSdf);
float4 _FishRockGrid;       // xyz the grid's world min corner, w the cell size (m)
float4 _FishRockGridDims;   // xyz cells, w 1 while the grid is live
float4 _FishRockAtlas;      // x bricks along x, y 1 / atlas width, z 1 / atlas depth (texels)
float4 _FishRockCrevice;    // x crevice width (m), y how dark its bottom, z how much soil fills it, w 1 when on

// Metres from a point to the nearest rock listed in its cell other than its own (ownOrigin: this rock's world
// position); far when there is none, the grid is off or the point is outside it.
float FishRockDistance(float3 positionWS, float3 ownOrigin)
{
    if (_FishRockCrevice.w <= 0.0 || _FishRockGridDims.w <= 0.0)
    {
        return 1e4;
    }
    int3 c = int3(floor((positionWS - _FishRockGrid.xyz) / _FishRockGrid.w));
    if (any(c < 0) || any(c >= int3(_FishRockGridDims.xyz)))
    {
        return 1e4;
    }
    uint cell = (uint)c.x + (uint)_FishRockGridDims.x * ((uint)c.y + (uint)_FishRockGridDims.y * (uint)c.z);
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
        {
            FishRock rock = _FishRockList[index];
            if (distance(rock.origin.xyz, ownOrigin) > 0.01)
            {
                float3 local = float3(dot(rock.row0.xyz, positionWS) + rock.row0.w,
                                      dot(rock.row1.xyz, positionWS) + rock.row1.w,
                                      dot(rock.row2.xyz, positionWS) + rock.row2.w);
                float3 uvw = (local - rock.brickMin.xyz) / rock.brickSize.xyz;
                // Outside the brick is farther than its padding: no crevice from this rock.
                if (all(uvw >= 0.0) && all(uvw <= 1.0))
                {
                    // Voxel centres only, so a brick never filters into its neighbour in the atlas.
                    float3 voxel = clamp(uvw * FISH_ROCK_BRICK, 0.5, FISH_ROCK_BRICK - 0.5);
                    uint brick = (uint)rock.brickSize.w;
                    uint bricksX = (uint)_FishRockAtlas.x;
                    float3 atlas = float3((float)((brick % bricksX) * FISH_ROCK_BRICK) + voxel.x,
                                          voxel.y,
                                          (float)((brick / bricksX) * FISH_ROCK_BRICK) + voxel.z);
                    float3 uv = float3(atlas.x * _FishRockAtlas.y, atlas.y / FISH_ROCK_BRICK, atlas.z * _FishRockAtlas.z);
                    float d = SAMPLE_TEXTURE3D_LOD(_FishRockSdf, sampler_LinearClamp, uv, 0.0).r;
                    best = min(best, d * rock.brickMin.w * rock.origin.w);
                }
            }
        }
    }
    return best;
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
    float d = FishRockDistance(positionWS, ownOrigin);
    half t = (half)saturate(1.0 - d / max(0.01, _FishRockCrevice.x));
    if (t <= 0.0h)
    {
        return;
    }
    half crevice = t * t;
    shade = 1.0h - crevice * (half)_FishRockCrevice.y;
    half facing = saturate((normalize(surfaceNormalWS).y + 0.2h) / 0.6h);
    soil = crevice * (half)_FishRockCrevice.z * lerp(0.35h, 1.0h, facing);
}

#endif
