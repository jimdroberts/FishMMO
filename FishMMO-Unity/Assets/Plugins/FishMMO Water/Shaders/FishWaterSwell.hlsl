#ifndef FISHMMO_WATER_SWELL_INCLUDED
#define FISHMMO_WATER_SWELL_INCLUDED

// The sea's wave sum on its own (2026-10-25, phase I2p): the FFT cascades, the depth they fade with over the shore and
// where the open sea reaches, for anything that has to ride the SAME sea as the ocean surface — the floats on it
// (FishMMO/Sea Floats: weed, drift logs, ice). FishWaterWaves.hlsl includes this after FishWaterInput.hlsl and builds the
// ocean surface from exactly these functions, so the sea is unchanged and the two cannot drift apart.
//
// A shader other than the sea's must NOT include FishWaterInput.hlsl (it carries the sea's UnityPerMaterial): it includes
// this file alone, which then declares the globals WaterSurface sets by the same names. Included after FishWaterInput.hlsl,
// the declarations are skipped (they are that file's). Everything here is a global: no material constant, so no fade with
// distance (_WaveFadeStart is the sea material's, 900 m by default): a rider keeps within that of the camera.

#ifndef FISHMMO_WATER_INPUT_INCLUDED
TEXTURE2D(_FishWaterDisplacement0);
TEXTURE2D(_FishWaterDisplacement1);
TEXTURE2D(_FishWaterDisplacement2);
TEXTURE2D(_FishWaterDerivatives0);
TEXTURE2D(_FishWaterDerivatives1);
TEXTURE2D(_FishWaterDerivatives2);
SAMPLER(sampler_FishWaterDisplacement0);
float4 _FishWaterPatch;          // the three patch sizes in metres, w the vertical scale
float4 _FishWaterMeshSpacing;    // x the ocean mesh's vertex gap as a share of a ring's radius, y the inner radius
float _FishWaterLevel;           // the sea's surface now, tide included
float _FishWaterMeanLevel;       // the level with no tide on it
float4 _FishWaterWind;           // xy the wind's way, z its speed (m/s)
TEXTURE2D(_FishWaterShore);
SAMPLER(sampler_FishWaterShore);
float4 _FishWaterShoreRect;      // xy world minimum, zw size; zero width: no shore
float _FishWaterShoreTexel;      // metres per texel of the shore field
#endif

// The shore's clock, the world's gravity and where the sea hands over to the breakers.
#include "FishWaterSurf.hlsl"

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

/// <summary>
/// Samples one FFT cascade.
/// </summary>
/// <param name="xz">World position in metres.</param>
/// <param name="patch">The cascade's tile size in metres.</param>
/// <param name="weight">How much of this cascade to take, for resolution fading.</param>
void FishWaterCascade(Texture2D displacementMap, Texture2D derivativeMap, float2 xz, float patch,
	float weight, inout float3 displacement, inout float2 slope, inout float folding, inout float kept)
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
	kept += n.w * weight;
}

/// <summary>
/// The three cascades summed at a point, each faded out where <paramref name="footprintMetres"/> is too coarse to resolve
/// it (0: all three kept): the ocean's own sum (FishWaterDisplace), before the open-sea, distance and depth fades.
/// </summary>
void FishWaterSwellCascades(float2 xz, float footprintMetres, inout float3 displacement, inout float2 slope, inout float folding, inout float kept)
{
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

	FishWaterCascade(_FishWaterDisplacement0, _FishWaterDerivatives0, xz,
		_FishWaterPatch.x, resolved.x, displacement, slope, folding, kept);
	FishWaterCascade(_FishWaterDisplacement1, _FishWaterDerivatives1, xz,
		_FishWaterPatch.y, resolved.y, displacement, slope, folding, kept);
	FishWaterCascade(_FishWaterDisplacement2, _FishWaterDerivatives2, xz,
		_FishWaterPatch.z, resolved.z, displacement, slope, folding, kept);
}

/// <summary>
/// Where the sea's surface is over a still-water point, as the ocean draws it near the camera (no distance fade): the
/// displacement from the flat point (xyz, metres) and the surface's normal. Exactly FishWaterDisplace's sum with an
/// amplitude scale of one: the cascades, then the open-sea reach, then the shallows' calm, in that order.
/// </summary>
float3 FishWaterSwellAt(float2 xz, float footprintMetres, out float3 normalWS)
{
	float3 displacement = 0.0;
	float2 slope = 0.0;
	float folding = 0.0;
	float kept = 0.0;
	FishWaterSwellCascades(xz, footprintMetres, displacement, slope, folding, kept);
	float openSea = FishWaterOpenSea(xz);
	displacement *= 1.0 * openSea;
	slope *= 1.0 * openSea;
	float calm = FishWaterCalm(FishWaterSeabedDepth(xz));
	displacement *= calm;
	slope *= calm;
	normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
	return displacement;
}

#endif
