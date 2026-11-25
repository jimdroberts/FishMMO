#ifndef FISHMMO_CLOUD_FALLING_INCLUDED
#define FISHMMO_CLOUD_FALLING_INCLUDED

// What is falling — rain, snow, hail, ash or sand — between the camera and the clouds laid over the frame, shared by
// the cloud composite (FishClouds.shader pass 2) and the water, which lays the same clouds over itself after it
// (FishWaterFog.hlsl). One law for both, so far ground, far sea and the sky under a deck meet with no seam.

// The falling rain, snow, hail, ash or sand at the camera, 1/m: what the pipeline's distance fog lays over every surface
// in its colour (WeatherFogPresenter.UniformFallingExtinction; 0 with the fog off).
float _FishFallingFog;
// That fog's colour: RenderSettings.fogColor as SkySystem publishes it for the sky (rgb). Not unity_FogColor, which a
// full-screen pass is not handed (it read black: the sky under falling rain went to black at the horizon above a lit sea).
float4 _FishSkyFogColor;
// The march's globals this reads (FishCloudVolume.hlsl): the shown rain (y) and its extinction (w); the cloud base
// over the viewer (z, m).
float4 _FishCloudSub;
float4 _FishCloudShear;

/// The integral up to u (the height over the ground as a share of the base's, as the hang reads it) of the share
/// of the falling extinction the march's rain hang leaves out: all of it up to 0.3, less and less of it to 0.9 as
/// the hang ramps in, the hang's own fade under the base, none above the base. Uniform minus the hang's profile
/// (FishCloudVolume.hlsl: saturate((under - 0.3) / 0.6) * saturate((1.0 - under) / 0.1)).
float FishSkyFallingShare(float u, bool hangs)
{
    u = max(0.0, u);
    if (!hangs)
    {
        return min(u, 1.0);
    }
    if (u <= 0.3) return u;
    if (u <= 0.9) { float d = u - 0.3; return 0.3 + d - d * d / 1.2; }
    if (u <= 1.0) { float d = u - 0.9; return 0.6 + d * d / 0.2; }
    return 0.65;
}

/// How much of the light along a ray gets through what is falling between the camera and `distance` along it: the
/// same extinction the distance fog lays over a surface at that distance, over the stretch of the ray under the cloud
/// base, less the share the march's rain hang already takes where it drew one (`marchHangs`: on a sky ray, not on one
/// that ends on the world — FishCloudRainHangScale). `ground` is the ground under the camera (m), which the hang
/// measures its heights from. Closed form along a straight ray, the world's curve left out: a level ray stays under
/// the base all the way and fogs to the fog's colour exactly as the far ground under it does; looking up, it is out
/// of the falling air in a few hundred metres and the base shows.
float FishSkyFallingFog(float3 direction, float distance, bool marchHangs, float ground)
{
    float beta = max(0.0, _FishFallingFog);
    if (beta <= 1e-7)
    {
        return 1.0;
    }
    float base = max(1.0, _FishCloudShear.z);
    // The hang is drawn only while rain is shown (the column's floor goes down to the ground with it).
    bool hangs = marchHangs && _FishCloudSub.y >= 0.01;
    // As the hang reads it: the height over the ground against the base's altitude.
    float u0 = (_WorldSpaceCameraPos.y - ground) / base;
    float tau;
    if (abs(direction.y) < 1e-3)
    {
        float share = FishSkyFallingShare(u0 + 1e-3, hangs) - FishSkyFallingShare(u0, hangs);
        tau = beta * distance * share * 1000.0;
    }
    else
    {
        float u1 = u0 + distance * direction.y / base;
        tau = beta * base * (FishSkyFallingShare(u1, hangs) - FishSkyFallingShare(u0, hangs)) / direction.y;
    }
    return exp(-max(0.0, tau));
}

/// What is falling between the camera and a cloud laid over a surface, put in front of that cloud.
///
/// A surface wears it already: the pipeline's distance fog lays it over the surface by the surface's distance (the
/// water's own MixFog likewise), and so the march leaves it out of a ray that ends on the world
/// (FishCloudRainHangScale). But the cloud is laid over the surface after its fog, so a cloud in front of a far
/// surface stood bare of the rain between it and the camera: under a storm, a peak that rose into the deck wore the
/// deck's lit inside, white from the base's level up to the silhouette, while the same deck in the sky beside it was
/// the rain's grey.
///
/// With T the falling air's transmittance in front of the cloud, C the cloud's light, A its transmittance and S' the
/// surface behind it, fogged already, what reaches the eye is T·C + (1 − T)·(1 − A)·F + A·S': the cloud seen through
/// the rain, and the rain's own colour F where the cloud hides the surface (the share of the surface's fog the cloud
/// took away with it). The transmittance stays as it is, so the surface is fogged once.
///
/// `cloudKm` is the march's mean distance for the pixel (km; 0 where it met nothing), `toSurface` the surface from the
/// camera, `ground` as FishSkyFallingFog has it.
float4 FishCloudFallingInFront(float4 clouds, float cloudKm, float3 toSurface, float ground)
{
    if (_FishFallingFog <= 1e-7 || clouds.a >= 0.9999)
    {
        return clouds;
    }
    float surfaceDistance = length(toSurface);
    // No nearer than the march's mean distance, no farther than the surface: a texel the upscale took from over the
    // sky beside a ridge holds the sky's distance, and one marched to a sea bed the sea bed's.
    float cloudDistance = cloudKm > 0.0 ? min(cloudKm * 1000.0, surfaceDistance) : surfaceDistance;
    float through = FishSkyFallingFog(toSurface / max(1e-3, surfaceDistance), cloudDistance, false, ground);
    return float4(through * clouds.rgb + (1.0 - through) * (1.0 - clouds.a) * _FishSkyFogColor.rgb, clouds.a);
}

#endif
