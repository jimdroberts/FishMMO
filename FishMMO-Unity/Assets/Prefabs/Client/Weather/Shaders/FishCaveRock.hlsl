#ifndef FISHMMO_CAVE_ROCK_INCLUDED
#define FISHMMO_CAVE_ROCK_INCLUDED

// FishMMO: the cave's own "terrain paint" (2026-10-20: "add something to caves and mines that adds more realism and detail
// similar to the way we have grass on the surface"). FishMMO/Weather Lit's forward pass on a material with the keyword
// _FISH_CAVE_ROCK (CaveRockMaterials: the shell rock's material cloned per look, a sub-asset of the scene's caves asset).
// The cave mesher writes each vertex's habitat (CaveHabitat): COLOR = (wet, daylight, sediment, trodden) and TEXCOORD3 =
// (organic, glow). Here the shell rock is dressed by it:
//   * walls and roofs keep the shell's rock, darkened and glossed where wet, streaked down where the seepage runs (mineral
//     stain and flowstone crust in their own colours: a limestone cave's ochre and calcite, a mine's ore rust and verdigris);
//   * floors blend the rock into the cave's grounds by the sediment (coarse grit and gravel at its low values, fine silt and
//     mud in the hollows at its high), litter (humus where the daylight reaches, pale guano in the dark under the roosts) and
//     worn ways (packed and polished along the lanes); the wet makes mud of silt and lays puddles in the deepest hollows;
//   * the twilight zone grows the world's own moss (FishMoss.hlsl FishMossGrow) where it is damp, the deep dark none;
//   * the glow field shines as a speckled glowing crust (emission, so the cave's darkness — FishUnderground — keeps it).
//
// Cost: one keyword (shader_feature_local: only cave rock materials compile it), two more interpolators (the slots the
// indirect twin's contact and rock origin use; cave rock is never drawn by the twin), and on a floor pixel up to four albedo
// and two normal reads, planar in world xz (sediment is only on what faces up). Every ground is read with sampler_BaseMap,
// the material's own base-map sampler, bound in every program of the pass because the base map always is: the pass is at
// D3D11's sixteen samplers in its heaviest variants (memory: unity-borrowed-sampler-trap). The grounds are the biome's own
// layers or the generated ones (CaveRockLayers.For), all imported Repeat.

TEXTURE2D(_FishCaveSiltMap);
TEXTURE2D(_FishCaveSiltNormal);
TEXTURE2D(_FishCaveGravelMap);
TEXTURE2D(_FishCaveGravelNormal);
TEXTURE2D(_FishCaveOrganicMap);
TEXTURE2D(_FishCaveTroddenMap);

#ifndef FISH_CAVE_SAMPLER
#define FISH_CAVE_SAMPLER sampler_BaseMap
#endif

// How much of a ground covers a pixel at a weight: its high grains first (the albedo's alpha is the generator's smoothness,
// which falls as the surface's height rises), so a layer's edge is ragged at the scale of its own stones and clods.
half FishCaveCover(half weight, half alpha)
{
    half height = saturate(1.15h - alpha * 1.3h);
    return saturate((weight - 0.5h) * 2.4h + (height - 0.5h) * 0.9h + 0.5h) * (half)smoothstep(0.0, 0.12, weight);
}

// A ground read planar on the floor: colour + alpha; and its normal turned from the plane's tangent space into world space.
half4 FishCaveGround(TEXTURE2D_PARAM(map, samplerMap), float2 uv, float2 dx, float2 dy)
{
    return (half4)SAMPLE_TEXTURE2D_GRAD(map, samplerMap, uv, dx, dy);
}

half3 FishCaveGroundNormal(TEXTURE2D_PARAM(map, samplerMap), float2 uv, float2 dx, float2 dy)
{
    half3 t = (half3)UnpackNormal(SAMPLE_TEXTURE2D_GRAD(map, samplerMap, uv, dx, dy));
    // The xz plane: the texture's x is world x, its y world z, its z up.
    return normalize(half3(t.x, t.z, t.y));
}

/// <summary>The habitat's layers on a cave rock pixel, in place, ahead of the moss and the weather.</summary>
/// geomNormalWS  the mesh's normal before any normal map (the habitat's floor/wall/roof)
/// habitat       COLOR: x wet, y daylight, z sediment, w trodden
/// habitat2      TEXCOORD3: x organic, y glow
/// Must be called in uniform control flow (it takes the position's screen derivatives first).
void FishCaveRockSurface(float3 positionWS, half3 geomNormalWS, half4 habitat, half2 habitat2,
    inout half3 albedo, inout half3 normalWS, inout half smoothness, inout half metallic, inout half occlusion, inout half3 emission)
{
    float3 dpx = ddx(positionWS);
    float3 dpy = ddy(positionWS);
    half3 n = normalize(geomNormalWS);
    half wet = saturate(habitat.x);
    half daylight = saturate(habitat.y);
    half sediment = saturate(habitat.z);
    half trodden = saturate(habitat.w);
    half organic = saturate(habitat2.x);
    half glow = saturate(habitat2.y);
    half up = saturate((n.y - 0.45h) / 0.4h);
    half wall = 1.0h - up;

    // ── Walls and roofs: what the seepage leaves running down them ──
    UNITY_BRANCH
    if (wall * wet > 0.02h && _FishCaveStain.a > 0.0h)
    {
        // Along the wall in plan, and down it: streaks long in y, narrow across.
        float2 side = normalize(float2(-n.z, n.x) + float2(1e-4, 0.0));
        float u = dot(positionWS.xz, side);
        half streak = (half)FishSurfaceNoise(float2(u * 1.7, positionWS.y * 0.12));
        half fine = (half)FishSurfaceNoise(float2(u * 6.1 + 3.7, positionWS.y * 0.35));
        half runs = saturate(((streak * 0.75h + fine * 0.25h) - (1.0h - wet) * 0.7h - 0.2h) * 3.0h) * wall * (half)_FishCaveStain.a;
        // Calcite flowstone in some runs (a crust over the rock, wet-glossy), the mineral stain in the others.
        half crust = step(0.58h, (half)FishSurfaceNoise(float2(u * 0.45 + 11.0, positionWS.y * 0.05)));
        half3 stained = albedo * (half3)_FishCaveStain.rgb * 1.8h;
        half3 crusted = (half3)_FishCaveCrust.rgb * (0.8h + 0.35h * fine);
        albedo = lerp(albedo, lerp(stained, crusted, crust), runs * lerp(1.0h, 0.85h, crust));
        smoothness = lerp(smoothness, max(smoothness, 0.6h), runs * crust);
    }

    // ── The tea ring (2026-10-25, I2n-c "drowned galleries"): every flood's stain up the walls to the highest one's sharp line ──
    // _FishCaveFlood: x the cave water's level (world y), y the highest flood over it, z the top edge's fade, w 1 on (CaveRockLayers.
    // WithStain; BiomeCaveWaters.RingAt is the same curve without the line's wander). Off on every cave rock without one.
    UNITY_BRANCH
    if (_FishCaveFlood.w > 0.5 && wall > 0.02h)
    {
        float above = positionWS.y - _FishCaveFlood.x;
        // The highest flood's line wanders a hand's breadth along the wall (eddies, splash), never in steps.
        float2 along = normalize(float2(-n.z, n.x) + float2(1e-4, 0.0));
        float wander = (FishSurfaceNoise(float2(dot(positionWS.xz, along) * 0.9, 7.3)) - 0.5) * 0.12;
        float t = saturate((_FishCaveFlood.y + wander - above) / max(_FishCaveFlood.z, 0.005));
        half ring = (half)(t * t * (3.0 - 2.0 * t)) * wall * (half)_FishCaveFloodStain.a;
        // Under the water's line the biofilm darkens it further.
        half under = (half)saturate(-above * 4.0);
        half3 tea = albedo * (half3)_FishCaveFloodStain.rgb * 2.2h;
        albedo = lerp(albedo, tea * (1.0h - 0.45h * under), ring);
    }

    // ── Floors: the cave's grounds over the rock ──
    UNITY_BRANCH
    if (up * (sediment + organic + trodden) > 0.01h)
    {
        half gravelWeight = (half)(smoothstep(0.08, 0.3, sediment) * (1.0 - smoothstep(0.5, 0.75, sediment))) * up;
        half siltWeight = (half)smoothstep(0.45, 0.8, sediment) * up;
        half organicWeight = organic * up;
        half troddenWeight = trodden * up * 0.9h;
        half4 gravel, silt, litter, path;
        half3 gravelNormal = n, siltNormal = n;
        if (_FishCaveParams.z > 0.5)
        {
            float2 xz = positionWS.xz;
            float4 tiles = _FishCaveTiles;
            gravel = FishCaveGround(TEXTURE2D_ARGS(_FishCaveGravelMap, FISH_CAVE_SAMPLER), xz * tiles.y, dpx.xz * tiles.y, dpy.xz * tiles.y);
            silt = FishCaveGround(TEXTURE2D_ARGS(_FishCaveSiltMap, FISH_CAVE_SAMPLER), xz * tiles.x, dpx.xz * tiles.x, dpy.xz * tiles.x);
            litter = FishCaveGround(TEXTURE2D_ARGS(_FishCaveOrganicMap, FISH_CAVE_SAMPLER), xz * tiles.z, dpx.xz * tiles.z, dpy.xz * tiles.z);
            path = FishCaveGround(TEXTURE2D_ARGS(_FishCaveTroddenMap, FISH_CAVE_SAMPLER), xz * tiles.w, dpx.xz * tiles.w, dpy.xz * tiles.w);
            UNITY_BRANCH
            if (gravelWeight > 0.01h)
            {
                gravelNormal = FishCaveGroundNormal(TEXTURE2D_ARGS(_FishCaveGravelNormal, FISH_CAVE_SAMPLER), xz * tiles.y, dpx.xz * tiles.y, dpy.xz * tiles.y);
            }
            UNITY_BRANCH
            if (siltWeight > 0.01h)
            {
                siltNormal = FishCaveGroundNormal(TEXTURE2D_ARGS(_FishCaveSiltNormal, FISH_CAVE_SAMPLER), xz * tiles.x, dpx.xz * tiles.x, dpy.xz * tiles.x);
            }
        }
        else
        {
            // The grounds not generated yet: their plain colours, so a floor still reads as silt and grit, never as bare rock.
            half grain = (half)FishSurfaceNoise(positionWS.xz * 3.0);
            gravel = half4(half3(0.36h, 0.34h, 0.31h) * (0.7h + 0.6h * grain), 0.2h);
            silt = half4(0.3h, 0.26h, 0.2h, 0.3h);
            litter = half4(0.17h, 0.12h, 0.07h, 0.15h);
            path = half4(0.28h, 0.25h, 0.2h, 0.35h);
        }
        half cover = FishCaveCover(gravelWeight, gravel.a);
        albedo = lerp(albedo, gravel.rgb, cover);
        normalWS = normalize(lerp(normalWS, gravelNormal, cover));
        smoothness = lerp(smoothness, gravel.a * 0.6h, cover);
        metallic = lerp(metallic, 0.0h, cover);
        occlusion *= lerp(1.0h, 0.85h, cover);

        cover = FishCaveCover(siltWeight, silt.a);
        albedo = lerp(albedo, silt.rgb, cover);
        normalWS = normalize(lerp(normalWS, siltNormal, cover));
        smoothness = lerp(smoothness, silt.a * 0.5h, cover);
        metallic = lerp(metallic, 0.0h, cover);

        // Litter: humus and leaves where the day reaches, pale guano in the dark (the same ground's grain, its colour the roost's).
        half3 guano = (half3)_FishCaveGuano.rgb * (0.55h + 1.6h * dot(litter.rgb, half3(0.3h, 0.5h, 0.2h)));
        half3 rich = lerp(guano, litter.rgb, saturate(daylight * 5.0h));
        cover = FishCaveCover(organicWeight, litter.a);
        albedo = lerp(albedo, rich, cover);
        normalWS = normalize(lerp(normalWS, n, cover * 0.6h));
        smoothness = lerp(smoothness, 0.12h, cover);
        metallic = lerp(metallic, 0.0h, cover);

        // The worn way: packed, its grains pressed flat and a little polished by feet.
        cover = FishCaveCover(troddenWeight, path.a);
        albedo = lerp(albedo, path.rgb, cover * 0.85h);
        normalWS = normalize(lerp(normalWS, n, cover * 0.6h));
        smoothness = lerp(smoothness, max(smoothness, 0.42h), cover * 0.6h);
        metallic = lerp(metallic, 0.0h, cover);
    }

    // ── Wet: darker and glossier, mud for silt, puddles in the deepest hollows ──
    half puddle = (half)(smoothstep(0.75, 0.95, wet) * smoothstep(0.6, 0.85, sediment) * smoothstep(0.9, 0.98, n.y));
    albedo *= 1.0h - 0.38h * wet - 0.12h * puddle;
    half gloss = wet * wet * (half)_FishCaveParams.y;
    smoothness = lerp(smoothness, max(smoothness, 0.8h), gloss);
    smoothness = lerp(smoothness, 0.95h, puddle);
    normalWS = normalize(lerp(normalWS, n, puddle));

    // ── The twilight zone's moss and lichen, where it is damp (the world's own moss, FishMoss.hlsl) ──
    half growth = daylight * daylight * (0.35h + 0.65h * wet) * (half)_FishCaveParams.x;
    UNITY_BRANCH
    if (growth > 0.01h)
    {
        FishMossGrow(positionWS, n, dpx, dpy, growth, 0.0h, albedo, normalWS, smoothness, metallic, occlusion);
    }

    // ── The glow crust: speckled colonies that light themselves ──
    UNITY_BRANCH
    if (glow > 0.01h)
    {
        // Two scales of speckle, sheared by height so the walls' and the floors' do not line up.
        float2 q = positionWS.xz + positionWS.y * float2(0.71, -0.53);
        half speck = (half)(FishSurfaceNoise(q * 2.3) * 0.6 + FishSurfaceNoise(q * 7.9 + 5.1) * 0.4);
        half crust = saturate((glow * 1.35h - (1.0h - speck)) * 2.4h + 0.15h * glow);
        albedo = lerp(albedo, (half3)_FishCaveGlow.rgb * 0.08h + albedo * 0.6h, crust * 0.5h);
        emission += (half3)_FishCaveGlow.rgb * crust * (1.0h - 0.8h * daylight);
    }
}

#endif
