#ifndef FISHMMO_CASTER_CULLING_INCLUDED
#define FISHMMO_CASTER_CULLING_INCLUDED

// Per-cascade shadow caster culling for the GPU-driven draws (TerrainGpuRenderer's trees, details and rocks, the GPU
// detail scatter's bushes and pebbles): an indirect draw has one set of bounds round the camera, so URP puts it into every
// cascade's caster list, and each instance is culled here instead, before any of its own vertex work. Shared by
// FishMMO/Vegetation's caster (FishVegetationPasses.hlsl VegCasterCulled) and FishMMO/Weather Lit Indirect's
// (FishWeatherLitFadeWrappers.hlsl FishShadowPassVertex), so both follow the one rule.
//
// The includer declares `float3 _LightDirection` first (URP's ShadowCasterPass.hlsl does; the vegetation's pass file
// declares its own) and calls FishCasterCulled() after UNITY_SETUP_INSTANCE_ID, so UNITY_MATRIX_M is the instance's.

// This camera's main-light cascades (ShadowCascadeCulling, set just before the shadow maps): xyz each culling sphere's
// centre, w its radius; how many there are (under 2: nothing is culled).
float4 _FishCasterSpheres[4];
float _FishCasterCascades;
// Per draw (TerrainGpuRenderer's slot blocks, DetailScatterRenderer's command blocks): the drawn level's bounding sphere
// in object space, grown for what the shader does to a plant (VegVary's crown, height and lean, the wind); w 0 where
// nobody set it, and then nothing is culled.
float4 _FishCasterSphere;
// Probe diagnostic (ScenePerfProbe `castercullall`): 1 culls every caster in any cascade FishCasterCulled recognises, to
// prove the recognition works (the casters' shadows go). 0 as shipped.
float _FishCasterCullAll;

// Margin round a nearer cascade's sphere before a caster counts as wholly inside it, as a share of that sphere's radius
// (and metres on top): room for URP's cascade blend band, where receivers still read the next cascade out.
#define FISH_CASTER_INSIDE_MARGIN 0.15
#define FISH_CASTER_INSIDE_METRES 1.0

// Whether this instance draws nothing into the cascade being rendered, decided before any of its own work: its sphere is
// off this cascade's map, or it and the far end of its shadow both lie well inside a nearer cascade's sphere, whose
// receivers (URP takes the first sphere that contains a point) never read this cascade there. Which cascade is being
// drawn is told from the projection: a cascade's orthographic half size is its sphere's radius. Where that does not
// match one of this camera's spheres (a camera that published none, or some other shadow) nothing is culled.
bool FishCasterCulledAt(float3 centreWS, float radius);

bool FishCasterCulled()
{
#if defined(FISH_INDIRECT_DOTS)
    // A BatchRendererGroup draw has no per-draw block, so no sphere: nothing culled (FishIndirectInstancing.hlsl).
    return false;
#else
    if (_FishCasterSphere.w <= 0.0)
    {
        return false;
    }
    float3 centreWS = TransformObjectToWorld(_FishCasterSphere.xyz);
    float scale = max(length(UNITY_MATRIX_M._m00_m10_m20), max(length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22)));
    return FishCasterCulledAt(centreWS, _FishCasterSphere.w * scale);
#endif
}

/// FishCasterCulled for a caster known by a world-space sphere (2026-10-21: the grass blades, drawn from a buffer of roots
/// with no instance matrix): the same rule.
bool FishCasterCulledAt(float3 centreWS, float radius)
{
    int count = (int)_FishCasterCascades;
    if (count < 2)
    {
        return false;
    }
    // 1 over the cascade's half size, and which cascade has that half size and is centred where this projection is.
    float perMetre = length(UNITY_MATRIX_VP[0].xyz);
    int cascade = -1;
    UNITY_LOOP
    for (int i = 0; i < count; i++)
    {
        float4 sphere = _FishCasterSpheres[i];
        float2 centre = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1.0)).xy;
        if (abs(perMetre * sphere.w - 1.0) < 0.1 && all(abs(centre) < 0.05))
        {
            cascade = i;
        }
    }
    if (cascade < 0)
    {
        return false;
    }
    if (_FishCasterCullAll > 0.5)
    {
        return true;
    }
    // Off this cascade's map: the projection looks down the light, so the shadow it throws lies on the same spot of it.
    float2 onMap = mul(UNITY_MATRIX_VP, float4(centreWS, 1.0)).xy;
    if (any(abs(onMap) > 1.0 + radius * perMetre))
    {
        return true;
    }
    // Where its shadow ends: as far down the light as it takes to fall the caster's height (its sphere's diameter).
    float down = max(_LightDirection.y, 0.1);
    float3 shadowEnd = centreWS - _LightDirection * (2.0 * radius / down);
    UNITY_LOOP
    for (int j = 0; j < cascade; j++)
    {
        float4 nearer = _FishCasterSpheres[j];
        float room = nearer.w - radius - nearer.w * FISH_CASTER_INSIDE_MARGIN - FISH_CASTER_INSIDE_METRES;
        if (room > 0.0 && distance(centreWS, nearer.xyz) <= room && distance(shadowEnd, nearer.xyz) <= room)
        {
            return true;
        }
    }
    return false;
}

#endif
