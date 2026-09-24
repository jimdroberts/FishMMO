#ifndef FISHMMO_WATER_WAVES_INCLUDED
#define FISHMMO_WATER_WAVES_INCLUDED

#include "FishWaterInput.hlsl"

/// <summary>
/// Surface gravity of the world this sea is on, in m/s². Driven from the celestial body.
/// </summary>
/// <remarks>
/// Not a constant, and that is the point. Deep-water waves travel at sqrt(g/k), so on a moon at a
/// sixth of a gravity the same swell moves at 40% of the speed and takes six times the wavelength
/// to reach the same height. A sea that ran at 9.81 everywhere would look identical on every world
/// in the system, which is the opposite of what this project is for.
/// </remarks>
float _FishWaterGravity;

struct FishWaterSurface
{
	float3 positionWS;
	float3 normalWS;
	/// Height above still water, in metres. Drives scattering on the crests.
	float height;
	/// Determinant of the horizontal displacement. At or below zero the surface would fold, which
	/// is where a real wave breaks.
	float jacobian;
	/// How hard the shore is breaking the waves here: 0 open water, 1 full surf.
	float surf;
	/// Metres of water over the ground here. 1000 in open ocean.
	float depth;
};

/// <summary>Metres of water over the ground, or open ocean where the scene has no shore field.</summary>
float FishWaterSeabedDepth(float2 xz)
{
	if (_FishWaterShoreRect.z < 1.0)
	{
		return 1000.0;
	}
	float2 uv = (xz - _FishWaterShoreRect.xy) / _FishWaterShoreRect.zw;
	// Outside the scene's ground is open water, not the clamped edge repeated to the horizon.
	if (any(uv < 0.0) || any(uv > 1.0))
	{
		return 1000.0;
	}
	return SAMPLE_TEXTURE2D_LOD(_FishWaterShore, sampler_FishWaterShore, uv, 0).r;
}

/// <summary>
/// Which way the ground falls away toward the shore, as a unit vector; zero where there is none.
/// </summary>
/// <remarks>
/// Waves slow in shallow water, so a crest arriving at an angle is held back at its inshore end
/// and swings round until it is nearly parallel with the beach. It is why surf arrives square onto
/// a shore whatever the wind is doing, and its absence is why a sea with the wind blowing along a
/// coast looked like corrugated iron sliding sideways past it.
/// </remarks>
float2 FishWaterShoreGradient(float2 xz)
{
	if (_FishWaterShoreRect.z < 1.0)
	{
		return float2(0.0, 0.0);
	}
	// Over a span rather than a texel: the wanted gradient is the shape of the beach, not the
	// noise of one heightmap sample against its neighbour.
	const float Step = 14.0;
	float east = FishWaterSeabedDepth(xz + float2(Step, 0.0)) - FishWaterSeabedDepth(xz - float2(Step, 0.0));
	float north = FishWaterSeabedDepth(xz + float2(0.0, Step)) - FishWaterSeabedDepth(xz - float2(0.0, Step));
	float2 gradient = -float2(east, north);   // toward shore is where depth DECREASES
	float length2 = dot(gradient, gradient);
	return length2 > 1e-8 ? gradient * rsqrt(length2) : float2(0.0, 0.0);
}

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
/// Samples one FFT cascade.
/// </summary>
/// <param name="xz">World position in metres.</param>
/// <param name="patch">The cascade's tile size in metres.</param>
/// <param name="weight">How much of this cascade to take, for resolution fading.</param>
void FishWaterCascade(Texture2D displacementMap, Texture2D derivativeMap, float2 xz, float patch,
	float weight, inout float3 displacement, inout float2 slope, inout float folding)
{
	if (weight <= 0.001)
	{
		return;
	}
	float2 uv = xz / max(1.0, patch);
	float4 d = SAMPLE_TEXTURE2D_LOD(displacementMap, sampler_FishWaterDisplacement0, uv, 0);
	float4 n = SAMPLE_TEXTURE2D_LOD(derivativeMap, sampler_FishWaterDisplacement0, uv, 0);
	displacement += d.xyz * weight;
	slope += n.xy * weight;
	// Folding below one is a compressing surface; at or under zero it has turned inside out, which
	// is physically where a wave breaks. Accumulated as a deficit so cascades can agree.
	folding += max(0.0, 1.0 - n.z) * weight;
}

/// <summary>
/// The sea surface at a point: displacement, normal, and where it is breaking.
/// </summary>
/// <param name="flatPositionWS">The undisplaced point; Y is the still-water level.</param>
/// <param name="amplitudeScale">Distance fade, from <see cref="FishWaterAmplitudeFade"/>.</param>
/// <param name="footprintMetres">
/// How much ground one pixel covers. A cascade whose tile is finer than the footprint cannot be
/// resolved and is faded out rather than point-sampled into aliasing; the vertex stage passes zero
/// and keeps them all, because the geometry must not shrink.
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

	/* A cascade is dropped once one pixel covers more than about a quarter of its tile: below
	 * that it is being point-sampled far under its own Nyquist limit and returns noise, which is
	 * the classic FFT-ocean horizon sparkle. */
	float3 resolved = 1.0;
	if (footprintMetres > 0.0)
	{
		resolved.x = saturate(_FishWaterPatch.x / (footprintMetres * 4.0) - 1.0);
		resolved.y = saturate(_FishWaterPatch.y / (footprintMetres * 4.0) - 1.0);
		resolved.z = saturate(_FishWaterPatch.z / (footprintMetres * 4.0) - 1.0);
	}

	FishWaterCascade(_FishWaterDisplacement0, _FishWaterDerivatives0, flatPositionWS.xz,
		_FishWaterPatch.x, resolved.x, displacement, slope, folding);
	FishWaterCascade(_FishWaterDisplacement1, _FishWaterDerivatives1, flatPositionWS.xz,
		_FishWaterPatch.y, resolved.y, displacement, slope, folding);
	FishWaterCascade(_FishWaterDisplacement2, _FishWaterDerivatives2, flatPositionWS.xz,
		_FishWaterPatch.z, resolved.z, displacement, slope, folding);

	displacement *= amplitudeScale;
	slope *= amplitudeScale;

	/* The shore.
	 *
	 * The FFT is a deep-water model and knows nothing about a bottom, so shoaling, breaking and
	 * swash are applied to its output: the height grows as the water shallows (Green's law), and
	 * is then limited to what the depth can carry, which is what makes the wave rear up and
	 * spill. The horizontal displacement is cut at the same time, because a wave that has run out
	 * of water cannot keep throwing itself forward. */
	surface.surf = 0.0;
	if (surface.depth < 900.0)
	{
		float reference = max(1.0, _FishWaterPatch.y * 0.5);
		float shallow = max(0.35, surface.depth);
		float gain = clamp(pow(reference / shallow, 0.25), 1.0, 2.0);

		float grown = abs(displacement.y) * gain;
		float limit = max(0.0, surface.depth) * _ShoreBreak;
		/* NO run-up here any more.
		 *
		 * The swash — the sheet that runs up the sand and drains back — belongs to the shore pass,
		 * which owns the beach and can conform to it. Letting the ocean's surface rise above the
		 * waterline instead made it poke through every dip in the sand independently and strand
		 * disconnected puddles up the beach, because a displaced plane has no way to know that a
		 * swash is ONE body of water. Here the sea simply converges on the waterline, and the
		 * depth buffer hides whatever is under the ground.
		 */
		float allowed = limit;

		surface.surf = saturate((grown - allowed) / max(0.05, grown));
		float scale = grown > 1e-4 ? min(grown, allowed) / grown : 1.0;
		displacement.y *= gain * scale;
		// Shoreward crests keep their slope; the lateral throw dies with the depth.
		// The lateral throw dies with the depth: a wave that has run out of water underneath it
		// cannot keep hurling itself forward.
		displacement.xz *= saturate(surface.depth * 0.5);
		slope *= gain * scale;
	}

	surface.positionWS += displacement;
	surface.height = displacement.y;
	surface.normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
	surface.jacobian = 1.0 - saturate(folding);

	/* Surf only where the wave is up. The break test is true across the whole shallow zone, so
	 * ungated it whitens the entire nearshore into a sheet; a surf zone is BANDS, because
	 * breaking happens on the front of the crest and the troughs between are green water. */
	float crestBand = saturate(surface.height / max(0.15, surface.depth * 0.30));
	surface.surf *= crestBand * crestBand;
	return surface;
}

#endif
