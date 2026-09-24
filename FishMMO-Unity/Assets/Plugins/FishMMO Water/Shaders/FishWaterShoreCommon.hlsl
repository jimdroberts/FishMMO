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

// ── The wave train ────────────────────────────────────────────────────

float _FishWaterSwashPeriod;     // seconds between arriving waves
float _FishWaterSwashReach;      // metres the swash runs up the beach at full strength
float _FishWaterSwashSkew;       // 0 symmetric, 1 a fast rush and a long drain
float _FishWaterShoreTime;

/// <summary>
/// Where the water's edge is right now, as a distance from the still waterline in metres.
/// </summary>
/// <remarks>
/// <para>
/// <b>Waves arrive as fronts parallel to the shore</b>, because refraction has already turned
/// them; so the phase is a function of distance to the water's edge and nothing else. That is
/// what makes the lines ROLL in rather than slide sideways past the beach.
/// </para>
/// <para>
/// <b>The cycle is deliberately asymmetric.</b> A real swash rushes up in a second or two and
/// drains for several — a sine looks like breathing, not like water. The skew is what makes the
/// difference between a sheet of water being thrown up a beach and a tide going in and out.
/// </para>
/// <para>
/// <b>No two waves run up the same distance.</b> Swell arrives in groups, so the run-up swings
/// between waves — some barely wet the sand the last one left, some go well past it. Every wave
/// reaching exactly the same line drew the swash as a machine, and left its foam in one ruled
/// stripe; varied, each wave strands its foam at its own height and the beach carries several.
/// </para>
/// </remarks>
float FishWaterSwashEdge(float alongShorePhase)
{
	float period = max(0.5, _FishWaterSwashPeriod);
	float cycles = _FishWaterShoreTime / period + alongShorePhase;
	float t = frac(cycles);
	// This wave's share of the full reach: two thirds to four thirds, fixed for the whole wave.
	float wave = floor(cycles);
	float reach = 0.65 + 0.7 * frac(sin(wave * 12.9898 + 78.233) * 43758.5453);

	// Rush up over the first part of the cycle, drain over the rest.
	float rush = saturate(_FishWaterSwashSkew) * 0.5 + 0.12;
	float climb = smoothstep(0.0, rush, t);
	float drain = 1.0 - smoothstep(rush, 1.0, t);
	float height = min(climb, drain);
	// Squared on the way out: water drains fastest when the sheet is thickest.
	return height * height * _FishWaterSwashReach * reach;
}

/// <summary>
/// The state of the swash at a point on the beach.
/// </summary>
/// <param name="edgeDistance">Signed metres to the still waterline; negative on dry land.</param>
/// <param name="sheet">How much water is over this point, 0 dry to 1 the thick part of the rush.</param>
/// <param name="lip">1 right at the leading edge of the rush, falling away behind it.</param>
/// <param name="highWater">How far up the beach this wave reached, 0 … 1 of the full reach.</param>
/// <remarks>
/// <para>
/// <b>Returned as three separate things on purpose.</b> A swash is not one value: there is the
/// sheet of water, the white lip at its leading edge, and the wet sand it leaves behind when it
/// drains. Collapsing them into a single "wetness" is what produced a flat wash of colour over
/// the whole band instead of a wave running up a beach.
/// </para>
/// <para>
/// The lip is the important one. It is the brightest thing on any beach and the feature whose
/// absence made every earlier attempt read as a plane meeting sand at a hard line.
/// </para>
/// </remarks>
void FishWaterSwash(float edgeDistance, out float sheet, out float lip, out float highWater)
{
	float reach = max(0.5, _FishWaterSwashReach);
	float landward = -edgeDistance;

	/* The phase a point sees depends on how far up the beach it is: the far end of a run-up is
	 * reached later than the near end, which is what a swash looks like from the side. Small,
	 * because the whole sheet must still move as one body of water. */
	float alongShore = saturate(landward / reach) * 0.22;
	highWater = FishWaterSwashEdge(alongShore) / reach;

	float front = highWater * reach;
	// Behind the front, in metres. Negative means the water has not arrived here yet.
	float behind = front - landward;

	// Not reached, or only just: nothing here.
	sheet = saturate(behind / max(0.35, reach * 0.35));
	/* The lip is the band of white at the leading edge, and its width scales with the reach so a
	 * gentle lap and a storm surge both get a lip of a sensible size on screen.
	 *
	 * 0.18, not 0.06. At a fourteen-metre reach the narrower figure is a strip 1.7 m wide on the
	 * ground — a handful of pixels from anywhere a player stands, which is why the beach kept
	 * rendering as a flat wash with no white line on it at all. */
	/* 0.09, not 0.18. At 0.18 the lip is a band nearly six metres across, and since the foam
	 * term saturates through most of it the result is a flat white slab rather than a line —
	 * which reads as a bleached beach, not as surf. A real swash lip is a metre or two. */
	float lipWidth = max(0.5, reach * 0.09);
	lip = saturate(1.0 - abs(behind) / lipWidth);
	lip *= step(0.0, front - 0.05);
}

/// <summary>
/// How much foam the swash lays at a point right now, for the foam memory to keep.
/// </summary>
/// <remarks>
/// Foam is made by the breaking bore and carried up at its lip, and what stays behind is what the
/// lip leaves where it stalls — the lacy line at the top of each run-up — so the lip lays foam in
/// the upper part of its run and hardly at all low down, where the next wave washes it off anyway.
/// </remarks>
float FishWaterSwashFoamDeposit(float edgeDistance)
{
	float sheet, lip, highWater;
	FishWaterSwash(edgeDistance, sheet, lip, highWater);
	return lip * smoothstep(0.35, 0.9, highWater);
}

#endif
