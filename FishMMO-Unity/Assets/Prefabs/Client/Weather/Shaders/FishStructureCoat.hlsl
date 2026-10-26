// FishMMO: the coat a biome's weather lays over its buildings (2026-10-25; BiomeStructureCoat / BiomeStructureCoats, published by
// FloraPaletteMap): lichen, rime, salt bloom, soot, a tide-mark… drawn where a piece stands, from the biome under its origin, on top of
// the weathering every building already wears (FishStructureWeathering.hlsl). No textures but the small table; no keywords.
//
// The table: _FishCoatTable, 10 RGBA texels a row, one row per biome on the scene's flora map (the row is its palette index, the same
// grid _FishFloraBiomes FishFlora.hlsl reads), linear:
//   0 A.rgb + strength · 1 B.rgb + cover · 2 C.rgb + speckle · 3 foot.rgb + foot height m · 4 skyward, shade, windward (signed), sun ·
//   5 film, edge, streaks, cracks · 6 patchiness, smoothness (< 0 keep), bleach (signed), keep · 7 mark m, foot from (0 the ground,
//   1 the sea's low water, 2 the local water: any water within 6 m of the piece on the fine ground map), scale m, foot wind ·
//   8 uphill (the main colour on the faces toward the slope above), uphill foot bias, foot spread (per piece), year fade ·
//   9 scour (windward polish), scour top m (0 the whole wall), faults (+ vertical strike-slip, − dipping along the contour), growth
//   below m (under the sea; above it only a film of the second colour). Zero in 8.zw and 9 draws exactly as before 2026-10-25 (I2g-e).
// _FishCoatInfo = (on, rows, grid w, grid h): x 0 when no biome on the scene has a coat, and every building draws as before.
//
// Only the indirect twin's pass includes it (FishWeatherLitIndirect.shader, FXC on purpose: its procedural setup writes the per-draw
// matrices); needs FishFlora.hlsl, FishWeather.hlsl, FishStructureWeathering.hlsl and FishGroundMap.hlsl (the slope: the fine ground
// map round the camera, so a piece beyond its 800 m has no uphill side) first. Every input is a position, a normal, the
// prevailing wind (_FishWindWay, which never turns with the weather), the scene sea's low water, the fine ground map's water or the
// local year phase (_FishSeason.x, from the shared world clock: Rewashed's limewash renewed each spring): nothing else moves with time.

#ifndef FISHMMO_STRUCTURE_COAT_INCLUDED
#define FISHMMO_STRUCTURE_COAT_INCLUDED

#include "FishSea.hlsl"

Texture2D<float4> _FishCoatTable;
float4 _FishCoatInfo;

#define FISH_COAT_TEXELS 10

/// <summary>The coat table's row (the biome's palette index) under a world xz: the flora grid's texel, unjittered.</summary>
uint FishCoatRowAt(float2 xz)
{
    if (_FishFloraRect.z <= 0.0 || _FishCoatInfo.z < 1.0 || _FishCoatInfo.w < 1.0)
    {
        return 0u;
    }
    float2 uv = (xz - _FishFloraRect.xy) * _FishFloraRect.zw;
    if (any(uv < 0.0) || any(uv > 1.0))
    {
        return 0u;
    }
    int2 size = (int2)_FishCoatInfo.zw;
    int2 texel = clamp((int2)floor(uv * _FishCoatInfo.zw), int2(0, 0), size - 1);
    uint index = (uint)round(_FishFloraBiomes.Load(int3(texel, 0)) * 255.0);
    return min(index, (uint)max(_FishCoatInfo.y - 1.0, 0.0));
}

float4 FishCoatTexel(uint row, int column)
{
    return _FishCoatTable.Load(int3(column, (int)row, 0));
}

/// <summary>
/// Coats one pixel of a structure (a material marked _FishStructure = 1), after FishStructureWeather. preAlbedo/preSmoothness are the
/// surface before that weathering (what a coat that keeps a building unaged, Pristine, restores). heightAboveGround as for
/// FishStructureWeather. A piece is coated or not by its origin (the row's cover); nothing at all when the table is off.
/// </summary>
void FishStructureCoat(float3 positionWS, half3 normalWS, float heightAboveGround, float3 originWS, half3 preAlbedo, half preSmoothness,
    inout half3 albedo, inout half smoothness, inout half occlusion)
{
    // Edges and arrises: how fast the normal turns per metre, before any branch (derivatives).
    float bend = length(fwidth((float3)normalWS)) / max(length(fwidth(positionWS)), 1e-4);
    half curvature = (half)saturate(bend * 0.06);

    UNITY_BRANCH
    if (_FishCoatInfo.x < 0.5)
    {
        return;
    }
    uint row = FishCoatRowAt(originWS.xz);
    float4 t0 = FishCoatTexel(row, 0);
    float4 t1 = FishCoatTexel(row, 1);
    UNITY_BRANCH
    if (t0.w <= 0.0 || FishWearHash(originWS.xz * 0.291 + originWS.y * 0.07 + 41.7) >= t1.w)
    {
        return;
    }
    float4 t2 = FishCoatTexel(row, 2);
    float4 t3 = FishCoatTexel(row, 3);
    float4 t4 = FishCoatTexel(row, 4);
    float4 t5 = FishCoatTexel(row, 5);
    float4 t6 = FishCoatTexel(row, 6);
    float4 t7 = FishCoatTexel(row, 7);
    float4 t8 = FishCoatTexel(row, 8);
    float4 t9 = FishCoatTexel(row, 9);
    half strength = (half)t0.w;

    half3 n = normalize(normalWS);
    half up = n.y;
    half wall = (half)(1.0 - saturate(abs(up) * 2.5));
    float scale = max(t7.z, 0.05);

    // A building the years never touched: the weathering undone (Pristine).
    half keep = (half)t6.w * strength;
    albedo = lerp(albedo, preAlbedo, keep);
    smoothness = lerp(smoothness, preSmoothness, keep);

    // The faces: the sky's, the shade's (north walls and undersides), the wind's and its lee, the sun's (the equator side, south).
    float2 way = FishWindWay();
    half skyward = (half)saturate(up * 1.6 - 0.25);
    half shade = max((half)saturate(n.z) * wall, (half)saturate(-up));
    half windward = (half)saturate(dot((float2)n.xz, -way));
    half lee = (half)saturate(dot((float2)n.xz, way));
    half windFace = t4.z >= 0.0 ? (half)t4.z * windward : (half)(-t4.z) * lee;
    half sunFace = (half)saturate(-n.z) * wall * 0.8h + skyward * 0.2h;
    // The ground round the piece's origin, from the fine ground map (800 m round the camera): the slope 6 m either side (Stoneshot's
    // falling stones, Riftstepped's contour) and the local water standing there (a cell over water holds the water's surface).
    half uphillFace = 0.0h;
    float uphillAlong = 0.0;
    float2 upslope = float2(0.0, 0.0);
    bool slopeKnown = false;
    bool waterNear = false;
    float waterLevel = -1e6;
    UNITY_BRANCH
    if (t8.x != 0.0 || t8.y != 0.0 || t9.z < 0.0 || t7.y > 1.5)
    {
        bool c0, c1, c2, c3, c4;
        float4 g0 = FishMistGroundAt(originWS.xz + float2(6.0, 0.0), c0);
        float4 g1 = FishMistGroundAt(originWS.xz - float2(6.0, 0.0), c1);
        float4 g2 = FishMistGroundAt(originWS.xz + float2(0.0, 6.0), c2);
        float4 g3 = FishMistGroundAt(originWS.xz - float2(0.0, 6.0), c3);
        float4 g4 = FishMistGroundAt(originWS.xz, c4);
        float2 rise = float2(g0.x - g1.x, g2.x - g3.x);
        if (c0 && c1 && c2 && c3 && dot(rise, rise) > 0.04)
        {
            upslope = normalize(rise);
            slopeKnown = true;
            half steep = (half)saturate(length(rise) / 12.0 / 0.35);
            uphillAlong = dot((float2)n.xz, upslope) * steep;
            uphillFace = (half)saturate(uphillAlong) * wall;
        }
        // On the water: the cell's nearness is 1 (bilinear, so a little under at its edge).
        if (c4 && g4.z > 0.97) { waterNear = true; waterLevel = max(waterLevel, g4.x); }
        if (c0 && g0.z > 0.97) { waterNear = true; waterLevel = max(waterLevel, g0.x); }
        if (c1 && g1.z > 0.97) { waterNear = true; waterLevel = max(waterLevel, g1.x); }
        if (c2 && g2.z > 0.97) { waterNear = true; waterLevel = max(waterLevel, g2.x); }
        if (c3 && g3.z > 0.97) { waterNear = true; waterLevel = max(waterLevel, g3.x); }
    }

    // Faults through the piece (Sheared, Riftstepped): one plane, a second parallel one on half the pieces, placed by the origin's
    // hash. The coat's own pattern is read across each plane shifted by the slip (2–30 cm), so it steps at the fault: along the
    // strike for a vertical plane, down for a dipping one. The masonry itself does not move (a coat moves no vertex).
    float3 slipped = positionWS;
    float faultGap = 1e6;
    float faultSide = 0.0;
    UNITY_BRANCH
    if (t9.z != 0.0)
    {
        float turn = FishWearHash(originWS.xz * 0.173 + 5.3) * 6.2831853;
        float2 strike = float2(cos(turn), sin(turn));
        if (t9.z < 0.0 && slopeKnown)
        {
            strike = upslope;
        }
        float3 faultNormal = t9.z > 0.0 ? float3(strike.x, 0.0, strike.y) : normalize(float3(strike.x * 0.87, 0.5, strike.y * 0.87));
        float offset = (FishWearHash(originWS.xz * 0.37 + 1.1) - 0.5) * 2.0;
        float d0 = dot(positionWS - originWS, faultNormal) - offset;
        float d1 = FishWearHash(originWS.xz * 0.61 + 9.7) > 0.5 ? d0 - 1.4 : -1e6;
        faultGap = min(abs(d0), abs(d1));
        faultSide = abs(d0) <= abs(d1) ? d0 : d1;
        float steps = step(0.0, d0) + step(0.0, d1);
        float slip = lerp(0.02, 0.3, FishWearHash(originWS.xz * 0.29 + 3.3)) * steps;
        slipped += t9.z > 0.0 ? float3(-faultNormal.z, 0.0, faultNormal.x) * slip : float3(0.0, -slip, 0.0);
    }
    float3 p = slipped / scale;

    // The coat's patches: even at patchiness 0, in blobs at 1.
    half blob = (half)(FishWearNoise(p.xz * 0.9 + p.y * 0.45) * 0.6 + FishWearNoise(p.xz * 3.1 + p.y * 1.7 + 7.0) * 0.4);
    half patches = lerp(1.0h, (half)smoothstep(0.42, 0.72, blob), (half)t6.x);

    // Bleach (silver-grey, most on sun faces), or with a negative weight the sky-facing faces browned dark (radiolysis).
    if (t6.z > 0.0)
    {
        half grey = dot(albedo, half3(0.3h, 0.59h, 0.11h));
        half3 silver = grey * half3(0.9h, 0.9h, 0.88h) + 0.12h;
        albedo = lerp(albedo, silver, (half)t6.z * strength * (0.6h + 0.4h * sunFace));
    }
    else if (t6.z < 0.0)
    {
        albedo = lerp(albedo, albedo * half3(0.45h, 0.32h, 0.22h), (half)(-t6.z) * strength * skyward);
    }

    // The main colour by face, on the edges and as a film everywhere.
    half coverA = saturate((half)t4.x * skyward + (half)t4.y * shade + windFace + (half)t4.w * sunFace + (half)t5.x + (half)t5.y * curvature
        + (half)t8.x * uphillFace)
        * patches * strength;
    half3 colourA = (half3)t0.rgb;
    // The year since the spring renewal (Rewashed): the main colour greys and thins until the next, renewed whole on the day.
    UNITY_BRANCH
    if (t8.w > 0.0)
    {
        float age = any(_FishSeason != 0.0) ? frac(_FishSeason.x - 0.25 + 1.0) : 0.5;
        half fade = (half)(t8.w * age);
        colourA = lerp(colourA, dot(colourA, half3(0.3h, 0.59h, 0.11h)) * 0.55h, fade);
        coverA *= 1.0h - 0.35h * fade;
    }
    // The growth by face only deeper than its depth under the sea; shallower, a film of the second colour (Currentgrown's coralline).
    UNITY_BRANCH
    if (t9.w > 0.0)
    {
        float depth = FishSeaPresent() ? _FishSea.y - positionWS.y : 1e6;
        half deep = (half)smoothstep(t9.w - 2.0, t9.w + 2.0, depth);
        coverA *= deep;
        albedo = lerp(albedo, (half3)t1.rgb, (1.0h - deep) * (half)step(0.0, depth) * 0.6h * patches * strength);
    }
    albedo = lerp(albedo, colourA, coverA);
    if (t6.y >= 0.0)
    {
        smoothness = lerp(smoothness, (half)t6.y, coverA);
    }

    // Streaks of the second colour: runs down a wall, narrow across it and long down it.
    float2 across = normalize(float2(-n.z, n.x) + 1e-4);
    float run = dot(slipped.xz, across) / scale;
    half streak = (half)smoothstep(0.55, 0.85, FishWearNoise(float2(run * 5.0, slipped.y * 0.35 / scale + 3.1)))
                * (half)(0.5 + 0.5 * FishWearNoise(float2(run * 0.9, slipped.y * 0.15 + 19.0)));
    albedo = lerp(albedo, (half3)t1.rgb, saturate(streak * (half)t5.z * wall * strength));

    // Cracks and joints of the second colour: thin ridged lines.
    half lines = (half)(FishWearNoise(p.xz * 1.7 + p.y * 1.3 + 5.0) * 0.65 + FishWearNoise(p.zy * 4.3 + p.x * 0.7) * 0.35);
    half crack = (half)(1.0 - smoothstep(0.0, 0.04, abs(lines - 0.5h))) * (half)t5.w * strength;
    albedo = lerp(albedo, (half3)t1.rgb, crack);
    occlusion *= 1.0h - crack * 0.3h;

    // Speckles and rosettes of the third colour, never on undersides.
    half dots = (half)smoothstep(0.72, 0.8, FishWearNoise(p.xz * 6.3 + p.y * 5.1 + 13.0)) * (half)(0.4 + 0.6 * FishWearNoise(p.xz * 1.3 + 2.0));
    albedo = lerp(albedo, (half3)t2.rgb, dots * (half)t2.w * strength * (1.0h - (half)saturate(-up)));

    // The windward polish (Hazefall, Flowscoured, Burandrift's belt): faces the prevailing way strikes scoured bright and smooth to
    // the belt's top, the weathering and the coat above undone; the drift of the foot band lies over it.
    UNITY_BRANCH
    if (t9.x > 0.0)
    {
        half belt = t9.y > 0.0 ? (half)(1.0 - smoothstep(t9.y * 0.8, t9.y, heightAboveGround)) : 1.0h;
        half scour = (half)t9.x * windward * wall * belt * strength;
        albedo = lerp(albedo, saturate(preAlbedo * 1.15h + 0.03h), scour);
        smoothness = lerp(smoothness, max(preSmoothness, 0.55h), scour);
    }

    // The foot band (over the ground, or over the sea's low water: a tide-mark) and the mark of the second colour above it.
    UNITY_BRANCH
    if (t3.w > 0.0)
    {
        float h = heightAboveGround;
        if (t7.y > 1.5)
        {
            // The local water (Sinkline, Waxed's bathtub ring): none by the piece, no band.
            h = waterNear ? positionWS.y - waterLevel : 1e6;
        }
        else if (t7.y > 0.5)
        {
            h = FishSeaPresent() ? positionWS.y - _FishSea.y : 1e6;
        }
        // The band's height piece to piece (Teastain's flood height): 1 ± spread by the origin's hash.
        float spread = 1.0 + t8.z * (FishWearHash(originWS.xz * 0.53 + 17.3) * 2.0 - 1.0);
        float top = spread * (t3.w * max(0.1, 1.0 + t7.w * dot((float2)n.xz, -way) + t8.y * uphillAlong)
            + FishWearNoise(float2(dot(positionWS.xz, across) * 1.3, 0.5)) * 0.15 * t3.w);
        half foot = (half)(1.0 - smoothstep(top * 0.85 - 0.04, top, h));
        albedo = lerp(albedo, (half3)t3.rgb, foot * strength * lerp(1.0h, patches, 0.3h));
        if (t7.x > 0.0)
        {
            half mark = (half)(step(top, h) * (1.0 - smoothstep(top, top + t7.x, h)));
            albedo = lerp(albedo, (half3)t1.rgb, mark * strength * 0.8h);
        }
    }

    // The fault's trace: a dark gap with a lit lip on its upthrown side, over everything.
    UNITY_BRANCH
    if (t9.z != 0.0)
    {
        half weight = (half)abs(t9.z) * strength;
        half gap = (half)(1.0 - smoothstep(0.012, 0.03, faultGap)) * weight;
        half lip = (half)(smoothstep(0.03, 0.05, faultSide) * (1.0 - smoothstep(0.05, 0.09, faultSide))) * weight;
        albedo = lerp(albedo, (half3)t1.rgb * 0.35h, gap);
        albedo *= 1.0h + lip * 0.25h;
        occlusion *= 1.0h - gap * 0.5h;
    }
}

#endif
