#ifndef FISHMMO_MOSS_INCLUDED
#define FISHMMO_MOSS_INCLUDED

// FishMMO: moss on what it really grows on (2026-10-12: "a moss layer that can be applied to objects", built into
// the world lit shader). FishMMO/Weather Lit's forward pass and its indirect twin's, on a material whose _FishMoss
// (UnityPerMaterial, 0..1, default 0) is above zero: the art generator sets it on rock types, cliff and cave rock, and
// the structure kit's stone and timber (BiomeArtGenerator). A material at 0 pays one uniform branch and nothing else.
// FishMMO/Vegetation's forward pass (and its indirect twin's) grows it too, for the deadwood's bark and wood
// (BiomeArtGenerator.Deadwood): logs and stumps are held still, so world-projected moss stays put on them. Every
// living plant is at 0.
// Round a waterfall the spray grows it too (FishFallMossGrowth): on those same materials, more than the climate alone
// would, and on the ground that has no moss material of its own — the terrain shaders, and the terrain's surface at a
// rock's or a trunk's foot (FishFallMoss). It is this one moss, textured the same, never a tint of its own.
//
// How much grows on a pixel, all from what the pass already knows:
//   * the scene's dampness × the profile's strength (_FishMossParams.x, MossShaderGlobals: the biome under the camera,
//     eased): a swamp or a forest grows a lot, a grassland some, a desert, a volcano or an ice sheet next to none;
//   * the ground round it (the fine ground map, FishGroundMap.hlsl, already read for FishUnderground): near open water,
//     under a closed canopy and down in a hollow it is damper, so rocks on a lake shore or a coast and in a wood's shade
//     carry more than the same rock on an open ridge;
//   * the surface itself: what faces the sky or is gently sloped (water and spores settle there), the poleward side of a
//     wall (out of the sun, it stays damp), the crevices (the surface's own occlusion, rock-on-rock crevices included,
//     since the pass has multiplied their shade in by then), and the first metre or so over the ground (splash and damp);
//   * none deep underground, where there is no light to grow by (a cave's mouth keeps it);
//   * a waterfall's spray (FishFallMossGrowth), which keeps its band of rock and ground damp in any climate;
//   * broken into patches by world-space noise, so it reads as clumps and colonies, never as a gradient up the rock,
//     and the patches' edges follow the moss's own height (clumps first, then the gaps between them) so it sits in the
//     low places and spreads from there.
//
// Looks: the generated Ground_Moss surface (SurfaceCatalogue Ground.Moss, written by every "Generate biome art" run),
// projected triplanar in world space so it needs no UVs and is continuous across every rock and wall, as albedo, normal
// and smoothness. Its height is not a third texture: the generator stores smoothness = mean + (0.5 - height) × variation
// in the albedo's alpha (SurfaceSynth), so the height is read back from it (_FishMossSurface). The weather comes after
// this in the pass (FishWeatherSurface), so wet moss darkens and shines and snow lies over it like everything else.
//
// Cost: no keywords. The textures are read with sampler_BaseMap — the material's own base map sampler, bound in every
// forward program because the base map always is — so the pass needs no new sampler state: it is at D3D11's sixteen
// in its heaviest variants already (memory: unity-borrowed-sampler-trap; an inline sampler would be a seventeenth).
// Every generated texture is imported Repeat, trilinear, aniso 8, which is what the moss wants. The vegetation pass,
// far below the limit, defines FISH_MOSS_SAMPLER as the core's inline sampler_TrilinearRepeat instead: the deadwood's
// wood wears the foliage atlas, which is imported Clamp. Only the planes that face the pixel are read (a rock's top
// reads one, its shoulder two), and nothing is read outside the moss. The terrain shaders, which grow only the spray's
// (FISH_MOSS_FALL_ONLY: no material amount, no ground map), borrow their own layers' sampler the same way.

float4 _FishMossParams;     // x strength × the scene's dampness (0 = no moss anywhere), y 1 / tile size (1/m),
                            // z 1 when the moss textures are bound, w poleward: +1 northern hemisphere, -1 southern, 0 unknown
float4 _FishMossSurface;    // x the moss recipe's mean smoothness, y 1 / its smoothness variation (height from the albedo's alpha),
                            // z the profile's strength alone, without the scene's dampness (the spray's moss), w unused
TEXTURE2D(_FishMossAlbedo); // Ground_Moss_Albedo: sRGB colour, a smoothness
TEXTURE2D(_FishMossNormal); // Ground_Moss_Normal (imported as a normal map)

#ifndef FISH_MOSS_SAMPLER
#define FISH_MOSS_SAMPLER sampler_BaseMap
#endif

// One plane of the projection: colour + smoothness, and the tangent-space normal as the texture has it.
void FishMossPlane(float2 uv, float2 dx, float2 dy, out half4 albedo, out half3 normalTS)
{
    albedo = (half4)SAMPLE_TEXTURE2D_GRAD(_FishMossAlbedo, FISH_MOSS_SAMPLER, uv, dx, dy);
    normalTS = (half3)UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_FishMossNormal, FISH_MOSS_SAMPLER, uv, dx, dy));
}

// ── Spray round a waterfall ──
// A fall's spray keeps the rock beside its curtain and the ground round its pool damp whatever the climate, so moss grows
// there in a desert as in a forest: the zone is the weather's (FishFallSpray, FishSurface.hlsl: a tapered capsule from
// the lip to the landing, `reach` 0 on its axis .. 1 at its edge). A band, not a disc: none in the inner zone where the
// water sheets over the rock and scours it, most from about half the radius out, thinning to nothing before the spray's
// edge, all on smooth ramps so there is no line where it stops. Thickest on what faces up (water settles there) and on
// what faces the fall (where the spray lands); only above the pool, whose rock is the water's to draw. Scaled by the
// profile's strength (_FishMossSurface.z) and not by the scene's dampness: spray is always damp.
// Returns the growth the patches are cut from, as FishMossGrow takes it; 0 almost everywhere, after one uniform branch.
half FishFallMossGrowth(float3 positionWS, half3 n)
{
    float reach;
    float3 anchor;
    float spray = FishFallSpray(positionWS, reach, anchor);
    half growth = 0.0h;
    UNITY_BRANCH
    if (spray > 0.001)
    {
        half band = (half)(smoothstep(0.3, 0.55, reach) * (1.0 - smoothstep(0.75, 1.0, reach)));
        half above = (half)saturate((positionWS.y - anchor.y) * 2.0 + 1.0);
        half3 toFall = (half3)normalize(anchor - positionWS + float3(0.0, 1e-3, 0.0));
        half facing = saturate(0.35h + 0.65h * max(n.y, dot(n, toFall)));
        growth = band * above * facing * 0.8h * (half)_FishMossSurface.z;
    }
    return growth;
}

// ── Patches and looks ──
// Everything after "how much grows here": the patches, the moss's own colour, normal and height, and the soft edge.
// FishMossSurface (a material's moss: climate, site and spray) and FishFallMoss (the spray's alone, for the ground)
// both end here, so the moss by a fall and the moss on a forest boulder are the same moss.
/// positionWS, n   where, and the geometric normal (normalized) the projection and the planes follow
/// dpx, dpy        the position's screen derivatives, taken by the caller in uniform control flow
/// growth          0..1+: how much grows here before the patches (site × climate, or the spray's)
/// drought         0..0.6: how far a dry spell has browned it
/// Safe in divergent control flow: no derivative is taken here.
half FishMossGrow(float3 positionWS, half3 n, float3 dpx, float3 dpy, half growth, half drought,
    inout half3 albedo, inout half3 normalWS, inout half smoothness, inout half metallic, inout half occlusion)
{
    // ── In patches ──
    // Two scales of world-space noise, sheared by height so a wall's patches are not columns of the ground's.
    float2 p = positionWS.xz + positionWS.y * float2(0.61, -0.43);
    half coarse = (half)FishSurfaceNoise(p * 0.45);
    half fine = (half)FishSurfaceNoise(p * 1.9 + 7.3);
    half patch = coarse * 0.65h + fine * 0.35h;
    // Little growth: a few spots where the noise is highest. Plenty: the patches run together and only the noise's
    // lowest gaps stay bare.
    half spread = saturate((growth * 1.3h - (1.0h - patch)) * 2.5h + 0.5h);
    UNITY_BRANCH
    if (spread <= 0.001h)
    {
        return 0.0h;
    }

    // ── What it looks like ──
    half3 mossAlbedo;
    half mossSmoothness;
    half mossHeight;
    half3 mossNormal = normalWS;
    UNITY_BRANCH
    if (_FishMossParams.z > 0.5)
    {
        // Triplanar on the mesh's normal, sharpened so each face reads one plane and only the shoulders blend; the
        // planes mirrored by the facing's sign so the normal map's slopes point the right way on every side
        // (the "whiteout" blend: each plane's normal reoriented onto the surface before the three are mixed).
        half3 w = n * n;
        w *= w;
        w /= max(w.x + w.y + w.z, 1e-3h);
        half3 axisSign = half3(n.x < 0.0h ? -1.0h : 1.0h, n.y < 0.0h ? -1.0h : 1.0h, n.z < 0.0h ? -1.0h : 1.0h);
        float s = _FishMossParams.y;
        half4 colour = half4(0.0h, 0.0h, 0.0h, 0.0h);
        half3 blended = half3(0.0h, 0.0h, 0.0h);
        half used = 0.0h;
        half4 a;
        half3 t;
        UNITY_BRANCH
        if (w.x > 0.02h)
        {
            float2 flip = float2(axisSign.x, 1.0);
            FishMossPlane(positionWS.zy * flip * s, dpx.zy * flip * s, dpy.zy * flip * s, a, t);
            t.x *= axisSign.x;
            t = half3(t.xy + n.zy, abs(t.z) * n.x);
            colour += a * w.x;
            blended += t.zyx * w.x;
            used += w.x;
        }
        UNITY_BRANCH
        if (w.y > 0.02h)
        {
            float2 flip = float2(axisSign.y, 1.0);
            FishMossPlane(positionWS.xz * flip * s, dpx.xz * flip * s, dpy.xz * flip * s, a, t);
            t.x *= axisSign.y;
            t = half3(t.xy + n.xz, abs(t.z) * n.y);
            colour += a * w.y;
            blended += t.xzy * w.y;
            used += w.y;
        }
        UNITY_BRANCH
        if (w.z > 0.02h)
        {
            float2 flip = float2(-axisSign.z, 1.0);
            FishMossPlane(positionWS.xy * flip * s, dpx.xy * flip * s, dpy.xy * flip * s, a, t);
            t.x *= -axisSign.z;
            t = half3(t.xy + n.xy, abs(t.z) * n.z);
            colour += a * w.z;
            blended += t * w.z;
            used += w.z;
        }
        colour /= max(used, 1e-3h);
        mossAlbedo = colour.rgb;
        mossSmoothness = colour.a;
        mossHeight = saturate(0.5h - (colour.a - (half)_FishMossSurface.x) * (half)_FishMossSurface.y);
        mossNormal = normalize(blended);
    }
    else
    {
        // The textures not bound yet (no biome art generated, or the profile has none): a flat dark green from the
        // recipe's ramp, its clumps from the fine noise. Still moss, never a grey smear.
        mossAlbedo = lerp(half3(0.016h, 0.034h, 0.006h), half3(0.105h, 0.195h, 0.04h), fine);
        mossSmoothness = 0.25h;
        mossHeight = fine;
    }
    // Colonies differ: a little yellower in one patch, bluer in the next. In a drought it dries toward olive and brown,
    // as moss does, and greens again when the rain comes back.
    mossAlbedo *= lerp(half3(0.9h, 0.96h, 1.08h), half3(1.12h, 1.05h, 0.82h), coarse);
    half grey = dot(mossAlbedo, half3(0.3h, 0.5h, 0.2h));
    mossAlbedo = lerp(mossAlbedo, grey * half3(1.3h, 1.05h, 0.6h), drought);

    // Soft edges that follow the moss's clumps: at half spread the clumps' tops are in and their gaps out, so a patch's
    // border is ragged at the scale of the moss itself; the last factor makes the very edge fade to nothing.
    half cover = saturate((spread - 0.5h) * 2.2h + (mossHeight - 0.5h) * 1.2h + 0.5h) * (half)smoothstep(0.0, 0.15, spread);

    albedo = lerp(albedo, mossAlbedo, cover);
    smoothness = lerp(smoothness, mossSmoothness, cover);
    metallic = lerp(metallic, 0.0h, cover);
    // A cushion of moss hides the shape under it and has its own little hollows.
    normalWS = normalize(lerp(normalWS, mossNormal, cover));
    occlusion *= lerp(1.0h, 0.72h + 0.28h * mossHeight, cover);
    return cover;
}

/// <summary>The spray's moss alone, applied in place; returns how much of it covers the pixel (0..1).</summary>
/// For the surfaces with no moss material of their own that the spray still greens: the terrain (FishWeatherTerrain,
/// FishWeatherTerrainArray) and the terrain's own surface where it renders at a rock's or a trunk's foot
/// (FishGroundLit), so the band at a base matches the ground round it. Called just before FishWeatherSurface, as the
/// material moss is, so the spray's wet darkens it and snow covers it. dpx and dpy are the position's screen
/// derivatives, taken by the caller in uniform control flow; nothing here takes one.
half FishFallMoss(float3 positionWS, half3 geomNormalWS, float3 dpx, float3 dpy,
    inout half3 albedo, inout half3 normalWS, inout half smoothness, inout half metallic, inout half occlusion)
{
    half3 n = normalize(geomNormalWS);
    half growth = FishFallMossGrowth(positionWS, n);
    UNITY_BRANCH
    if (growth <= 0.0h)
    {
        return 0.0h;
    }
    return FishMossGrow(positionWS, n, dpx, dpy, growth, 0.0h, albedo, normalWS, smoothness, metallic, occlusion);
}

#if !defined(FISH_MOSS_FALL_ONLY)
/// <summary>Moss on a lit surface, applied in place; returns how much of it covers the pixel (0..1).</summary>
/// geomNormalWS      the mesh's normal before any normal map: where the moss grows and how it is projected
/// heightAboveGround metres over the terrain (FishContactHeight on the GPU-drawn rocks and on deadwood); above 1000 =
///                   not known, and the fine ground map's height is used (coarser, 5 m cells, enough for "near the ground")
/// underground       FishUnderground: 0 in the open .. 1 deep in a cave
/// albedo .. occlusion the surface, before the weather
/// Must be called in uniform control flow (it takes screen derivatives of the position before anything branches).
half FishMossSurface(float3 positionWS, half3 geomNormalWS, float heightAboveGround, half underground,
    inout half3 albedo, inout half3 normalWS, inout half smoothness, inout half metallic, inout half occlusion)
{
    UNITY_BRANCH
    if (_FishMoss <= 0.0h)
    {
        return 0.0h;
    }
    // Derivatives first, while every pixel of the quad is still here: the planes are read with explicit gradients
    // inside branches that only some pixels take.
    float3 dpx = ddx(positionWS);
    float3 dpy = ddy(positionWS);

    half3 n = normalize(geomNormalWS);
    half amount = (half)_FishMoss * (half)_FishMossParams.x;
    // A waterfall's spray (FishFallMossGrowth) on the materials that grow moss at all, the rockiest the most: always
    // damp, so it greens a desert's rock beside a fall as fully as a forest's. No light to grow by deep in a cave.
    half spray = FishFallMossGrowth(positionWS, n) * saturate((half)_FishMoss * 2.0h) * (1.0h - underground);
    UNITY_BRANCH
    if (amount <= 0.0h && spray <= 0.0h)
    {
        return 0.0h;
    }
    // A little of the normal-mapped facing, so a rock's own bumps hold moss on their tops and not their sides.
    half up = lerp(n.y, normalWS.y, 0.35h);

    // ── Where it can grow ──
    // Level and gently sloped faces take it all, a wall some, the underside of an overhang none.
    half facing = (half)smoothstep(-0.2, 0.7, up);
    // The poleward side of a wall stays out of the sun and damp (northern faces north of the equator).
    half wall = 1.0h - saturate(abs(n.y) * 1.6h);
    half shaded = saturate(n.z * (half)_FishMossParams.w) * wall;
    // The surface's own hollows: its occlusion map and, on the GPU-drawn rocks, the rock-on-rock crevice shade.
    half crevice = saturate((1.0h - occlusion) * 2.0h);
    // The ground round it: water near, a canopy over it, a hollow under it — damper than the open.
    bool covered;
    float4 ground = FishMistGroundAt(positionWS.xz, covered);
    half local = covered ? saturate((half)ground.z * 0.7h + (half)ground.w * 0.5h + (half)saturate(ground.y * 0.5) * 0.3h) : 0.25h;
    float above = heightAboveGround < 1000.0 ? heightAboveGround : (covered ? positionWS.y - ground.x : 1000.0);
    half nearGround = 1.0h - (half)smoothstep(0.0, 1.6, max(above, 0.0));
    half site = saturate(facing * 0.75h + shaded * 0.4h + crevice * 0.6h + nearGround * 0.35h);
    // No light to grow by deep in a cave.
    half grow = amount * (0.55h + 0.9h * local) * (1.0h - underground);

    // The climate's moss or the spray's, whichever grows more. In a drought (the climate's humidity below its mean,
    // _FishSeason.z) the climate's browns; the spray keeps its own green.
    half growth = max(site * grow, spray);
    half drought = saturate(-(half)_FishSeason.z) * 0.6h * (1.0h - saturate(spray * 2.0h));
    return FishMossGrow(positionWS, n, dpx, dpy, growth, drought, albedo, normalWS, smoothness, metallic, occlusion);
}
#endif

#endif
