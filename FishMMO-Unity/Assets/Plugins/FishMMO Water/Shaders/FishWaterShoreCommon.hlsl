#ifndef FISHMMO_WATER_SHORE_COMMON_INCLUDED
#define FISHMMO_WATER_SHORE_COMMON_INCLUDED

// The shoreline, as a model of its own.
//
// WHY THIS IS NOT IN THE OCEAN SHADER
// The ocean is a deep-water spectrum drawn on a camera-centred disc. A shoreline is a different
// phenomenon on different geometry: a thin sheet of water running up a slope and draining back,
// whose timing is set by when each wave ARRIVES rather than by any spectrum, and whose foam has
// memory — it is made where the wave breaks, carried up the beach, and LEFT BEHIND as the water
// drains. None of that fits a stateless surface shader on a mesh whose tessellation near the
// waterline depends on where the camera happens to be standing.

TEXTURE2D(_FishWaterShore);
SAMPLER(sampler_FishWaterShore);
float4 _FishWaterShoreRect;   // xy world minimum, zw size

/// R metres of water over the ground (negative on land), G signed metres to the water's edge.
float2 FishWaterShoreSample(float2 xz)
{
	if (_FishWaterShoreRect.z < 1.0)
	{
		return float2(1000.0, 1000.0);
	}
	float2 uv = (xz - _FishWaterShoreRect.xy) / _FishWaterShoreRect.zw;
	if (any(uv < 0.0) || any(uv > 1.0))
	{
		return float2(1000.0, 1000.0);
	}
	return SAMPLE_TEXTURE2D_LOD(_FishWaterShore, sampler_FishWaterShore, uv, 0).rg;
}

// ── The swash ─────────────────────────────────────────────────────────
//
// IN HEIGHT, NOT DISTANCE. How far up a beach the water runs is a HEIGHT: the run-up, which on an
// ordinary sandy beach is about half the wave height and on a steep bank a wave height or two. The
// distance it covers is that height over the slope — sixteen metres of a 1:30 beach, one metre of a
// steep bank. The swash was laid out by horizontal distance first, a fixed dozen metres from the
// waterline whatever the ground did, which is right on a gentle beach and wrong everywhere else:
// on a steep bank it stood several metres up the face as a pale sheet. Measured in height it takes
// the slope from the ground itself, follows the terrain's contours as standing water does, and
// follows the tide for nothing, because it is measured from the sea as it stands now.

float _FishWaterSwashSkew;       // 0 symmetric, 1 a fast rush and a long drain
float4 _FishWaterWind;           // xy the direction the wind and the sea run toward

// The clock, the period, the sea and the phase along the shore, shared with the surf train so the
// swash and the waves that make it are one motion.
#include "FishWaterSurf.hlsl"

/// <summary>
/// The direction to the nearest shore from the field's distance channel, and how sure it is — worked
/// out exactly as the sea does it (FishWaterShoreFacing), so the swash and the surf agree about which
/// shores are sheltered.
/// </summary>
float FishWaterShoreFacingField(float2 xz, float texelMetres, out float2 towardShore)
{
	float step = max(1.0, texelMetres);
	float east = FishWaterShoreSample(xz + float2(step, 0.0)).y - FishWaterShoreSample(xz - float2(step, 0.0)).y;
	float north = FishWaterShoreSample(xz + float2(0.0, step)).y - FishWaterShoreSample(xz - float2(0.0, step)).y;
	float2 gradient = -float2(east, north) / (2.0 * step);
	float length2 = dot(gradient, gradient);
	float slope = sqrt(max(length2, 1e-16));
	towardShore = length2 > 1e-8 ? gradient / slope : float2(0.0, 0.0);
	return length2 > 1e-8 ? saturate((slope - 0.35) / 0.4) : 0.0;
}

/// <summary>
/// How high the swash runs above the still water on a beach of this slope (rise over run), in metres.
/// </summary>
/// <remarks>
/// Stockdon et al. (2006), the run-up exceeded by 2% of waves: 1.1 × (setup + swash/2), setup
/// 0.35·β·√(H·L) and swash √(H·L·(0.563·β² + 0.004)) for deep-water height H and wavelength L. On a
/// 1:30 beach under a metre of sea at eight seconds that is half a metre; on 1:10, nearly one. It is
/// fitted on beaches, so the slope is capped where a beach stops and a wall starts, and the run-up
/// at twice the wave height: a surge up a wall reaches about that and no further.
/// </remarks>
float FishWaterRunUp(float slope)
{
	float height = max(0.05, _FishWaterSwashSea.x);
	float wavelength = max(1.0, _FishWaterSwashSea.y);
	float beta = min(slope, 0.5);
	float scale = sqrt(height * wavelength);
	float setup = 0.35 * beta * scale;
	float swash = sqrt(height * wavelength * (0.563 * beta * beta + 0.004));
	return min(1.1 * (setup + 0.5 * swash), 2.0 * height);
}

/// <summary>
/// The run-up on this shore: a beach of this slope under the open sea, scaled by how much of that
/// sea reaches it — a lee shore gets a quarter of it, as its surf does.
/// </summary>
float FishWaterRunUpHere(float slope, float2 xz, float texelMetres)
{
	float2 towardShore;
	float confidence = FishWaterShoreFacingField(xz, texelMetres, towardShore);
	return FishWaterRunUp(slope) * FishWaterShoreExposure(towardShore, confidence);
}

/// <summary>
/// Where the swash front stands right now, in metres above the still water.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cycle is deliberately asymmetric.</b> A real swash rushes up in a second or two and
/// drains for several — a sine looks like breathing, not like water.
/// </para>
/// <para>
/// <b>No two waves run up the same height.</b> Swell arrives in groups, so the run-up swings
/// between waves — some barely wet the sand the last one left, some go well past it. Every wave
/// reaching one line drew the swash as a machine and left its foam in one ruled stripe.
/// </para>
/// </remarks>
/// <param name="runUp">This beach's run-up, from <see cref="FishWaterRunUp"/>.</param>
/// <param name="along">A phase offset, in cycles: along the shore, and up the beach.</param>
/// <param name="stretch">Which stretch of shore this is, so the waves of a group differ along it.</param>
float FishWaterSwashFront(float runUp, float along, float stretch)
{
	float cycles = _FishWaterShoreTime / max(0.5, _FishWaterSwashPeriod) + along;
	float t = frac(cycles);
	/* This wave's share of the full run-up: two thirds to four thirds, fixed for the whole wave —
	 * and different on different stretches of shore, so one wave of a group floods this stretch
	 * while the next one along barely wets its sand. */
	float wave = floor(cycles);
	float share = 0.65 + 0.7 * frac(sin(wave * 12.9898 + stretch * 4.1414 + 78.233) * 43758.5453);

	// Rush up over the first part of the cycle, drain over the rest.
	float rush = saturate(_FishWaterSwashSkew) * 0.5 + 0.12;
	float climb = smoothstep(0.0, rush, t);
	float drain = 1.0 - smoothstep(rush, 1.0, t);
	float height = min(climb, drain);
	// Squared on the way out: water drains fastest when the sheet is thickest.
	return height * height * runUp * share;
}

/// <summary>
/// The state of the swash at a point, from how high it stands above the still water.
/// </summary>
/// <param name="rise">Metres above the sea as it stands now; negative under it.</param>
/// <param name="runUp">This beach's run-up, from <see cref="FishWaterRunUp"/>.</param>
/// <param name="xz">Where on the shore, for how this stretch differs from the rest.</param>
/// <param name="sheet">How much water is over this point, 0 dry to 1 the thick part of the rush.</param>
/// <param name="lip">1 right at the leading edge of the rush, falling away behind it.</param>
/// <param name="highWater">How high this wave has reached, 0 … 1 of the beach's run-up.</param>
/// <remarks>
/// <para>
/// <b>Three separate things on purpose.</b> A swash is the sheet of water, the white lip at its
/// leading edge, and the wet sand it leaves behind. Collapsing them into one "wetness" drew a flat
/// wash of colour instead of a wave running up a beach.
/// </para>
/// <para>
/// The lip is a band a sixteenth of the run-up TALL, so it is as wide as the slope makes it: a
/// metre across a gentle beach, a few centimetres of a steep face — where, since a steep face
/// carries no sheet, it is the foam line surging up and down at the water's edge.
/// </para>
/// </remarks>
void FishWaterSwash(float rise, float runUp, float2 xz, out float sheet, out float lip, out float highWater)
{
	float2 alongShore = FishWaterAlongShore(xz);
	float span = max(0.03, runUp * alongShore.y);
	// The phase a point sees depends on where it is along the shore, and on how high up the beach:
	// the top of a run-up is reached later than the bottom, which is what a swash looks like from
	// the side.
	float along = alongShore.x + saturate(rise / span) * 0.22;
	float front = FishWaterSwashFront(span, along, floor(alongShore.x * 0.5));
	highWater = front / span;

	// Metres of water standing above this point.
	float behind = front - rise;
	sheet = saturate(behind / (span * 0.35));
	float lipHeight = max(0.015, span * 0.06);
	lip = saturate(1.0 - abs(behind) / lipHeight) * step(0.0, front - 0.005);
}

/// <summary>
/// How much foam the swash lays at a point right now, for the foam memory to keep.
/// </summary>
/// <remarks>
/// Foam is made by the breaking bore and carried up at its lip, and what stays behind is what the
/// lip leaves where it stalls — the lacy line at the top of each run-up — so the lip lays foam in
/// the upper part of its run and hardly at all low down, where the next wave washes it off anyway.
/// </remarks>
float FishWaterSwashFoamDeposit(float rise, float runUp, float2 xz)
{
	float sheet, lip, highWater;
	FishWaterSwash(rise, runUp, xz, sheet, lip, highWater);
	return lip * smoothstep(0.35, 0.9, highWater);
}

#endif
