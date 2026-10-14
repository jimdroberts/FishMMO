#ifndef FISHMMO_GROUND_CONTACT_LIT_INCLUDED
#define FISHMMO_GROUND_CONTACT_LIT_INCLUDED

// The terrain's own LIT colour at a pixel of an object's base (FishGroundColour.hlsl FishGroundContact): what the
// terrain shader's fragment (FishWeatherTerrainArrayPasses.hlsl SplatmapFragment) makes of the same surface — its
// weather, its lighting function (FishFragmentPBR: URP's PBR with the ground's sky-reflection curve, not URP Lit's), its
// ambient (the scene's ambient probe: no probe groups, no baked lighting; an indirect draw with probes off is handed
// the ambient probe, as the terrain is) — so where the band is wholly the terrain, the pixel IS the terrain's.
//
// Include after URP's Lighting.hlsl, FishGroundLighting.hlsl, FishSurface.hlsl (the weather), FishGroundColour.hlsl
// and FishMoss.hlsl (the spray's moss, which the terrain grows too).

// host is the object's own InputData at the pixel (position, view, shadow coordinate, fog, screen UV); shade darkens
// the ground's albedo and occlusion (a crevice's shadow; 1 none). worldDx and worldDy are the position's screen
// derivatives, taken in uniform control flow (the caller's branch on the band is not).
half3 FishGroundLit(InputData host, FishGroundPixel ground, half shade, float3 worldDx, float3 worldDy)
{
    InputData data = host;
    half3 albedo = ground.albedo * shade;
    half metallic = ground.metallic;
    half smoothness = ground.smoothness;
    half occlusion = ground.occlusion * shade;
    half3 normalWS = ground.normalWS;
    // The terrain's weather, as its fragment applies it (its material's _FishWeatherAmount: _FishGroundInfo.w).
    if (_FishGroundInfo.w > 0.0)
    {
        // The spray's moss round a waterfall, as the terrain grows it (FishFallMoss), so a base's band is as mossy as the
        // ground beside it.
        FishFallMoss(host.positionWS, ground.normalWS, worldDx, worldDy, albedo, normalWS, smoothness, metallic, occlusion);
        half3 weathered = normalWS;
        FishWeatherSurface(host.positionWS, albedo, weathered, smoothness, metallic, occlusion);
        FishTrailSurface(host.positionWS, host.viewDirectionWS, albedo, weathered, smoothness);
        normalWS = normalize(lerp(normalWS, weathered, (half)_FishGroundInfo.w));
    }
    data.normalWS = normalWS;
    data.bakedGI = SampleSH(normalWS);
    return FishFragmentPBR(data, albedo, metallic, smoothness, occlusion, 1.0h).rgb;
}

#endif
