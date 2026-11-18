#ifndef FISH_INDOOR_INCLUDED
#define FISH_INDOOR_INCLUDED

// The rooms of the sites' buildings round the camera (IndoorRooms.cs, baked by the cut as SceneRoom boxes): inside one,
// the sky's light comes only through the windows and the door, so the lit surfaces there take less of it.

#define FISH_MAX_ROOMS 16
// World to the room's box as the unit cube (-1..1 on each axis), the nearest first; _FishRoomCount of them are set.
float4x4 _FishRooms[FISH_MAX_ROOMS];
float _FishRoomCount;

// The share of a room's half-size, in from its walls, over which indoors fades in (IndoorRooms.FadeShare): the walls'
// own inner faces and the floor by the door still take some of the day.
#define FISH_INDOOR_FADE 0.2

/// How far inside a room a point is, 0 outside … 1 well in.
half FishIndoor(float3 positionWS)
{
    half best = 0.0h;
    int count = (int)min(_FishRoomCount, (float)FISH_MAX_ROOMS);
    for (int i = 0; i < count; i++)
    {
        float3 q = mul(_FishRooms[i], float4(positionWS, 1.0)).xyz;
        float d = max(abs(q.x), max(abs(q.y), abs(q.z)));
        best = max(best, (half)saturate((1.0 - d) / FISH_INDOOR_FADE));
    }
    return best;
}

#endif
