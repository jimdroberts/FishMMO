#ifndef FISH_WATER_FOAM_FIELD_INCLUDED
#define FISH_WATER_FOAM_FIELD_INCLUDED

// ─────────────────────────────────────────────────────────────────────────────
// The simulated foam (WaterFoamField.cs, FishWaterFoamField.shader): how much foam lies on the water here, carried
// by the water from where it was made.
//
// WHY. Every foam before this was worked out afresh each frame from where the water is breaking NOW: a breaking
// crest's white, an envelope laid downstream of a fall, a memory that faded in place. Real foam is made where water
// breaks and then CARRIED: it streams off a rapid in lines, gathers where currents meet and in the eddies behind
// rocks, is drawn into streaks along the wind on the sea, rides in on the bores and is left on the sand. That needs
// the foam itself to move, so it is a quantity kept over the water and moved by it each step.
//
// TWO WINDOWS round the camera, the near one fine and the far one coarse, both world-aligned (a texel stays put as
// the camera moves). R is the foam: an amount, 0 none, about 1 a surface mostly white, more for thick foam that has
// piled up. G is how fresh it is, 1 just made (thick, bright, opaque) fading to 0 as it ages into lace. A surface
// turns the amount into the share of it that is white (FishFoamFieldCoverage) and cuts the lace to that, as every
// foam source does (FishWaterFoam.hlsl).
//
// Where neither window reaches, or nothing is simulating (no shader, a scene with no water), `known` is 0 and a
// surface falls back to its own stateless foam.
// ─────────────────────────────────────────────────────────────────────────────

TEXTURE2D(_FishFoamField0);
SAMPLER(sampler_FishFoamField0);
TEXTURE2D(_FishFoamField1);
/// The near window's current map (what the inland water drew into it), for debug views only.
TEXTURE2D(_FishFoamFieldCurrent0);
/// The windows: xy the world corner (min x, min z), z the side in metres, w 1 while the field is live.
float4 _FishFoamFieldRect0;
float4 _FishFoamFieldRect1;

/// <summary>What the field holds at a point.</summary>
struct FishFoamFieldSample
{
	/// <summary>How much foam (R): 0 none, about 1 mostly white, more where it has piled up.</summary>
	half amount;
	/// <summary>How fresh it is (G): 1 just made, 0 old.</summary>
	half fresh;
	/// <summary>How far the field speaks for this point, 0 … 1: 0 outside both windows or with no field running.</summary>
	half known;
};

/// <summary>How much of a window covers a point: 1 inside, fading to 0 over its outer twelfth.</summary>
half FishFoamFieldInside(float2 uv)
{
	float2 edge = min(uv, 1.0 - uv);
	return (half)saturate(min(edge.x, edge.y) * 12.0);
}

/// <summary>The foam at world xz, the near window where it reaches, the far one beyond it.</summary>
FishFoamFieldSample FishFoamFieldAt(float2 xz)
{
	FishFoamFieldSample s = (FishFoamFieldSample)0;
	if (_FishFoamFieldRect1.w < 0.5 || _FishFoamFieldRect1.z < 1.0)
	{
		return s;
	}
	float2 uvNear = (xz - _FishFoamFieldRect0.xy) / max(1.0, _FishFoamFieldRect0.z);
	float2 uvFar = (xz - _FishFoamFieldRect1.xy) / max(1.0, _FishFoamFieldRect1.z);
	half nearShare = FishFoamFieldInside(uvNear);
	half farShare = FishFoamFieldInside(uvFar);
	half2 near = 0.0;
	half2 far = 0.0;
	if (nearShare > 0.0)
	{
		near = SAMPLE_TEXTURE2D_LOD(_FishFoamField0, sampler_FishFoamField0, uvNear, 0).rg;
	}
	if (nearShare < 1.0 && farShare > 0.0)
	{
		far = SAMPLE_TEXTURE2D_LOD(_FishFoamField1, sampler_FishFoamField0, uvFar, 0).rg;
	}
	half2 value = lerp(far, near, nearShare);
	s.known = max(nearShare, farShare);
	s.amount = max(0.0h, value.x);
	s.fresh = saturate(value.y);
	return s;
}

/// <summary>
/// The share of the surface an amount of foam whitens: one minus the open water left between patches as they pile
/// up at random, so twice the foam is not twice the white, and it never quite reaches all of it.
/// </summary>
/// <remarks>
/// Most carried foam is thin: at a third of the surface or less the lace cuts it into clumps and strands, which is
/// what foam that has drifted for a few seconds looks like. Only where it is being made, or has piled up, does it
/// close into froth; fed at the rates the first try used, every crest and stone left an opaque flat flake that
/// lingered (2026-10-13 renders).
/// </remarks>
half FishFoamFieldCoverage(half amount)
{
	return 1.0h - exp(-max(0.0h, amount));
}

/// <summary>
/// How thick the foam's body is for its age: fresh froth near opaque, old foam a translucent web of bubble walls with
/// the water showing through (multiplies FishFoam.body).
/// </summary>
half FishFoamFieldBody(FishFoamFieldSample s)
{
	return lerp(1.0h, lerp(0.3h, 1.0h, s.fresh), s.known);
}

// ── The lace, carried with the water ──────────────────────────────────────────────────────────────────────────
//
// WHY. The foam's fine look is its lace (the foam map's alpha), and every surface read it FLOW-MAPPED: two copies slid
// along the current for a short cycle each and cross-faded, so the lace changed under the eye every cycle, and every
// patch of foam on a river or the sea throbbed in step (2026-10-13: "the foam just pulses"). The near window keeps
// instead, for each texel, how far its water has moved since it was last reset (_FishFoamLace0: rg one layer, ba the
// other), carried with the foam by the same current; the lace is read back through that, so it moves exactly as the
// water does, eddies and convergence included. Each layer is reset every FISH_FOAM_LACE_PERIOD seconds, the two half a
// period apart, cross-faded so the one being reset is unseen; and each place resets at its own time
// (FishFoamLaceStagger), so no two patches change together and nothing throbs.

TEXTURE2D(_FishFoamLace0);
/// The step's clock when the lace was last carried (s, wrapped at 10 000), for the layers' phases.
float _FishFoamFieldTime;

/// Seconds between a layer's resets: long, so the lace is seen to drift a good way before it is renewed; a whole number
/// of them in the clock's 10 000 s wrap, so the wrap does not show.
#define FISH_FOAM_LACE_PERIOD 8.0

/// <summary>When, in its cycle, a place's lace is renewed (0 … 1): smooth value noise on a 23 m grid, so neighbours
/// renew nearly together (no seams) while places tens of metres apart never do.</summary>
float FishFoamLaceStagger(float2 xz)
{
	float2 g = xz / 23.0;
	float2 i = floor(g), f = g - i;
	f = f * f * (3.0 - 2.0 * f);
	float4 corner;
	corner.x = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453);
	corner.y = frac(sin(dot(i + float2(1.0, 0.0), float2(127.1, 311.7))) * 43758.5453);
	corner.z = frac(sin(dot(i + float2(0.0, 1.0), float2(127.1, 311.7))) * 43758.5453);
	corner.w = frac(sin(dot(i + float2(1.0, 1.0), float2(127.1, 311.7))) * 43758.5453);
	return lerp(lerp(corner.x, corner.y, f.x), lerp(corner.z, corner.w, f.x), f.y);
}

/// <summary>Where a place is in a layer's cycle, 0 … 1 (0 just renewed); <paramref name="layer"/> 0 or 1, half a period apart.</summary>
float FishFoamLacePhase(float2 xz, float time, float layer)
{
	return frac(time / FISH_FOAM_LACE_PERIOD + FishFoamLaceStagger(xz) + 0.5 * layer);
}

/// <summary>
/// The lace (the foam map's alpha) at world xz, read through the carried offsets: two layers cross-faded so the one being
/// renewed is unseen, blended keeping the lace's contrast (FishFoamBlend). <paramref name="scale"/> is a tile of the lace
/// in metres, <paramref name="dX"/>/<paramref name="dY"/> the pixel's derivatives of xz (taken in uniform control flow),
/// <paramref name="minLod"/> the coarsest-detail floor (a river's FISH_RIVER_LACE_MIP; 0 for the sea). Returns through
/// <paramref name="weight"/> how far it speaks here (0 outside the near window, where a surface keeps its own lace) and
/// <paramref name="lod"/> the mip read.
/// </summary>
half FishFoamFieldLace(TEXTURE2D_PARAM(laceMap, laceSampler), float2 xz, float2 dX, float2 dY, float scale, float minLod,
	out half weight, out float lod)
{
	weight = 0.0;
	lod = minLod;
	if (_FishFoamFieldRect0.w < 0.5 || _FishFoamFieldRect0.z < 1.0)
	{
		return 0.5;
	}
	float2 uv = (xz - _FishFoamFieldRect0.xy) / _FishFoamFieldRect0.z;
	weight = FishFoamFieldInside(uv);
	if (weight <= 0.0)
	{
		return 0.5;
	}
	float4 moved = SAMPLE_TEXTURE2D_LOD(_FishFoamLace0, sampler_FishFoamField0, uv, 0);
	float phase = FishFoamLacePhase(xz, _FishFoamFieldTime, 0.0);
	half blend = (half)abs(2.0 * phase - 1.0);
	float texels = max(length(dX), length(dY)) * (1024.0 / max(1e-3, scale));
	lod = clamp(log2(max(1e-6, texels)), minLod, 10.0);
	half a = SAMPLE_TEXTURE2D_LOD(laceMap, laceSampler, (xz - moved.xy) / scale, lod).a;
	half b = SAMPLE_TEXTURE2D_LOD(laceMap, laceSampler, (xz - moved.zw) / scale + float2(0.37, 0.61), lod).a;
	return FishFoamBlend(a, b, blend);
}

#endif
