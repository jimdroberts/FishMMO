#ifndef FISHMMO_WATER_WAVES_INCLUDED
#define FISHMMO_WATER_WAVES_INCLUDED

#include "FishWaterInput.hlsl"
// The shore's clock, the world's gravity and where the sea hands over to the breakers.
#include "FishWaterSurf.hlsl"

struct FishWaterSurface
{
	float3 positionWS;
	float3 normalWS;
	/// Height above still water, in metres. Drives scattering on the crests.
	float height;
	/// Determinant of the horizontal displacement. At or below zero the surface would fold, which
	/// is where a real wave breaks.
	float jacobian;
	/// Whitewater from the breakers here, 0 to 1. Zero from the wave model; the fragment adds it.
	float surf;
	/// How much of the open sea's waves reach this depth: 1 offshore, 0 at the break line and inshore.
	float calm;
	/// Metres of water over the ground here. 1000 in open ocean.
	float depth;
};

/// <summary>Metres of water over the ground now, or open ocean where the scene has no shore field.</summary>
/// <remarks>
/// The field holds the depth at MEAN sea level; the tide is added here. Without it the waves died
/// at the mean waterline whatever the tide was doing, which at high tide left a strip of calm,
/// waveless water between the surf and the swash.
/// </remarks>
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
	return SAMPLE_TEXTURE2D_LOD(_FishWaterShore, sampler_FishWaterShore, uv, 0).r + (_FishWaterLevel - _FishWaterMeanLevel);
}

/// <summary>Signed metres to the water's edge: positive at sea, negative on dry land.</summary>
float FishWaterShoreDistance(float2 xz)
{
	if (_FishWaterShoreRect.z < 1.0)
	{
		return 1000.0;
	}
	float2 uv = (xz - _FishWaterShoreRect.xy) / _FishWaterShoreRect.zw;
	if (any(uv < 0.0) || any(uv > 1.0))
	{
		return 1000.0;
	}
	return SAMPLE_TEXTURE2D_LOD(_FishWaterShore, sampler_FishWaterShore, uv, 0).g;
}

/// <summary>
/// The direction to the nearest shore, as a unit vector, and how sure it is: 1 almost everywhere,
/// 0 on a ridge midway between two shores, where it flips from one to the other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Down the distance field, not the depth.</b> The surf's crests are contours of the distance to
/// the water's edge, so the direction across them — the way a crest travels and is thrown — is that
/// field's gradient, and nothing else is square to them. It was the slope of the seabed over
/// fourteen metres either side, which across an island narrower than twenty-eight metres straddles
/// it: measured around the small islands of Cov Viaduct it was 33 degrees off the way to the shore
/// at the median and worse than 45 over two-fifths of the water, so every crest was thrown partly
/// along itself.
/// </para>
/// <para>
/// The confidence is the gradient's own length. An exact distance field slopes at one metre per
/// metre everywhere except on those ridges, where the difference taken across them cancels.
/// </para>
/// </remarks>
float FishWaterShoreFacing(float2 xz, out float2 towardShore)
{
	towardShore = float2(0.0, 0.0);
	float confidence = 0.0;
	if (_FishWaterShoreRect.z >= 1.0)
	{
		float step = max(1.0, _FishWaterShoreTexel);
		float east = FishWaterShoreDistance(xz + float2(step, 0.0)) - FishWaterShoreDistance(xz - float2(step, 0.0));
		float north = FishWaterShoreDistance(xz + float2(0.0, step)) - FishWaterShoreDistance(xz - float2(0.0, step));
		float2 gradient = -float2(east, north) / (2.0 * step);   // toward the shore: where the distance falls
		float length2 = dot(gradient, gradient);
		if (length2 > 1e-8)
		{
			float slope = sqrt(length2);
			towardShore = gradient / slope;
			confidence = saturate((slope - 0.35) / 0.4);
		}
	}
	return confidence;
}

/// <summary>The direction to the nearest shore, as a unit vector; zero where there is none.</summary>
float2 FishWaterShoreGradient(float2 xz)
{
	float2 towardShore;
	FishWaterShoreFacing(xz, towardShore);
	return towardShore;
}

/// <summary>
/// How much of the open sea's waves reach water this deep: 1 from the full-sea depth down, falling
/// smoothly to 0 at the break depth, and 0 everywhere shallower.
/// </summary>
/// <remarks>
/// <b>One published pair of depths, read by everything.</b> WaterSurface works them out once from
/// the sea state and publishes them; this, its CPU copy in WaterSurface.Displace and the break line
/// WaterBreakers lays its breakers along all read the same numbers. That is what makes the base of
/// a breaker sit exactly where the sea has gone flat — by construction, not by tuning two things
/// to agree. HLSL smoothstep; the C# copy is WaterSurface.Smoothstep, not Mathf.SmoothStep.
/// </remarks>
float FishWaterCalm(float depth)
{
	// Nothing publishing a break depth: the whole sea is open water.
	if (_FishWaterBreakDepth.y <= _FishWaterBreakDepth.x)
	{
		return 1.0;
	}
	return smoothstep(_FishWaterBreakDepth.x, _FishWaterBreakDepth.y, depth);
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
	return surface;
}

#endif
