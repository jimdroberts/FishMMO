#ifndef FISHMMO_WATER_WAVES_INCLUDED
#define FISHMMO_WATER_WAVES_INCLUDED

#include "FishWaterInput.hlsl"
// The wave sum shared with whatever rides the sea (2026-10-25): the cascades, the seabed's depth and calm, the shore's
// distance; it includes FishWaterSurf.hlsl (the shore's clock, the world's gravity, where the sea hands over to the breakers).
#include "FishWaterSwell.hlsl"

struct FishWaterSurface
{
	float3 positionWS;
	float3 normalWS;
	/// Height above still water, in metres. Drives scattering on the crests.
	float height;
	/// Determinant of the horizontal displacement. At or below zero the surface would fold, which
	/// is where a real wave breaks.
	float jacobian;
	/// How hard the surface here broke lately: the folding past where white caps start, kept and decaying
	/// (WaterFFT's foam memory), so a crest's foam stays on the water after it has run on. 0 with no memory.
	float breakingKept;
	/// Whitewater from the breakers here, 0 to 1. Zero from the wave model; the fragment adds it.
	float surf;
	/// How much of the open sea's waves reach this depth: 1 offshore, 0 at the break line and inshore.
	float calm;
	/// Metres of water over the ground here. 1000 in open ocean.
	float depth;
};


/// <summary>How much of its height a wave keeps at this distance from the camera.</summary>
/// <remarks>
/// A correctness fix, not a saving. A wave whose crests are closer together on screen than a pixel
/// cannot be sampled; it aliases into a crawling moiré no anti-aliasing touches, because the
/// geometry itself is under-sampled. Flattening the far sea and letting the reflection carry it is
/// why the horizon of a good ocean is calm.
/// </remarks>
float FishWaterAmplitudeFade(float distanceToCamera)
{
	return 1.0 - smoothstep(_WaveFadeStart, max(_WaveFadeEnd, _WaveFadeStart + 1.0), distanceToCamera);
}


/// <summary>
/// The sea surface at a point: displacement, normal, and where it is breaking.
/// </summary>
/// <param name="flatPositionWS">The undisplaced point; Y is the still-water level.</param>
/// <param name="amplitudeScale">Distance fade, from <see cref="FishWaterAmplitudeFade"/>.</param>
/// <param name="footprintMetres">
/// How much ground one pixel covers — or, in the vertex stage, how far apart the vertices are. A
/// cascade whose tile is finer than the footprint cannot be resolved and is faded out rather than
/// point-sampled into aliasing: by the pixel for the shading, by the vertex spacing for the geometry.
/// </param>
/// <remarks>
/// <b>This replaces a sum of six Gerstner waves.</b> Six components cannot look like an ocean —
/// the periodicity is always visible and no tuning removes it. Three FFT cascades carry about
/// 200,000 components between them, and cost LESS per pixel than the sum did, because sampling
/// three textures is cheaper than re-evaluating six sines and their derivatives.
/// </remarks>
FishWaterSurface FishWaterDisplace(float3 flatPositionWS, float amplitudeScale, float footprintMetres)
{
	FishWaterSurface surface;
	surface.positionWS = flatPositionWS;
	surface.depth = FishWaterSeabedDepth(flatPositionWS.xz);

	float3 displacement = 0.0;
	float2 slope = 0.0;
	float folding = 0.0;
	float kept = 0.0;

	FishWaterSwellCascades(flatPositionWS.xz, footprintMetres, displacement, slope, folding, kept);

	/* Only where the open sea's waves can reach, at this tide: a pool or a lagoon cut off from it lies
	 * still, its folding (and so its white caps) with it (FishWaterOpenSea). */
	float openSea = FishWaterOpenSea(flatPositionWS.xz);
	displacement *= amplitudeScale * openSea;
	slope *= amplitudeScale * openSea;
	folding *= openSea;
	kept *= openSea;

	/* ── The shallows: the sea fades out, it does not break ──
	 *
	 * The transform is a deep-water model and a single height field, and a height field cannot
	 * curl. Everything once bolted onto its output to make it break — a shoaling gain of up to
	 * twice its height, a depth limit, a sine surf train and a forward shear thrown into a barrel
	 * — made no breaker, and made the open-sea waves near every coast look wrong. So the sea hands
	 * over instead: from the depth where it is whole (_FishWaterBreakDepth.y) to the depth where
	 * waves break (.x) its amplitude falls smoothly to nothing, and from the break line in the
	 * water is still. The breakers (WaterBreakers) rise out of it there, their base laid along that
	 * same contour from the same published depth, so the seam is at the one place where both are
	 * exactly flat water.
	 *
	 * The FOLDING is not faded. It is the sea's own steepness, and the band where the waves are
	 * being taken out is exactly where a real sea would be breaking — so the white caps there are
	 * kept, and grow (the fragment lowers their threshold with 1 - calm), which is what hides the
	 * waves going. */
	surface.calm = FishWaterCalm(surface.depth);
	displacement *= surface.calm;
	slope *= surface.calm;

	// The breakers' whitewater is laid by the fragment stage (FishWaterBreakerCommon.hlsl), not here.
	surface.surf = 0.0;
	surface.positionWS += displacement;
	surface.height = displacement.y;
	surface.normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
	surface.jacobian = 1.0 - saturate(folding);
	surface.breakingKept = kept;
	return surface;
}

#endif
