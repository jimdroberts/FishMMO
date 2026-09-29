#ifndef FISHMMO_WATER_SURF_INCLUDED
#define FISHMMO_WATER_SURF_INCLUDED

// The rhythm of the shore, shared by the breakers, their whitewater and the swash so they are one wave.
//
// ONE CLOCK. The surf once ran on the sea's clock at a period of its own and the swash on the
// shore's, with a phase along the beach the surf did not have — so a wave rolled in, died at the
// waterline, and the run-up came at some unrelated moment. Nothing ever broke ONTO the shore.
// Everything now reads this: a breaker's whitewater reaches the waterline at the instant the swash
// there begins to rush up (FishWaterBreakerCommon.hlsl).
//
// Everything here is arithmetic but the open-sea map (FishWaterOpenSea).
//
// The includer declares _FishWaterWind first — the sea and the shore both already do.

// The tide each place joins the open sea at, m over mean sea level (WaterShoreField.JoinTides), and the
// tide now: water the sea's waves cannot reach at this tide — a pool, a lagoon behind a bar — lies still.
TEXTURE2D(_FishWaterOpenSea);
SAMPLER(sampler_FishWaterOpenSea);
float4 _FishWaterOpenSeaRect;    // xy world minimum, zw size; z under 1: no map, all of it open sea
float _FishWaterTide;            // metres over mean sea level now
float _FishWaterOpenSeaTides;    // the highest tide the map's gaps were measured up to, m

/// <summary>
/// How open to the sea's waves the water at a point is, 0..1, at the tide of the moment: 1 where a way in
/// from the open sea is deep enough now and wide enough, 0 in a pool or a lagoon cut off from it — opening
/// as the tide rises over its sill and closing as it falls — and less behind a narrow gap.
/// WaterShoreField.Snapshot.OpenSeaAt, line for line.
/// </summary>
float FishWaterOpenSea(float2 xz)
{
	if (_FishWaterOpenSeaRect.z < 1.0)
	{
		return 1.0;
	}
	float2 uv = (xz - _FishWaterOpenSeaRect.xy) / _FishWaterOpenSeaRect.zw;
	if (any(uv < 0.0) || any(uv > 1.0))
	{
		return 1.0;
	}
	// r the tide the way in needs; gba how much of the swell its narrowest gap lets through at the mean
	// tide, half the highest and the highest (WaterShoreField.ShelterTides), blended by the tide now.
	float4 open = SAMPLE_TEXTURE2D_LOD(_FishWaterOpenSea, sampler_FishWaterOpenSea, uv, 0);
	float x = _FishWaterOpenSeaTides > 0.01 ? saturate(_FishWaterTide / _FishWaterOpenSeaTides) * 2.0 : 0.0;
	float shelter = x <= 1.0 ? lerp(open.g, open.b, x) : lerp(open.b, open.a, x - 1.0);
	return saturate((_FishWaterTide - open.r) / 0.3 + 0.5) * saturate(shelter);
}

float _FishWaterSwashPeriod;     // seconds between arriving waves; 0 when the scene has no shore
float _FishWaterShoreTime;       // the shore's clock, seconds
float _FishWaterSwashCycles;     // which wave the shore is on: the swash's phase added up, wrapped at 1000
float4 _FishWaterSwashSea;       // x significant wave height (m), y deep-water wavelength at the peak period (m)

/// Where the open sea hands over to the breakers, from WaterSurface: x the break depth (m), where the
/// sea's waves have faded to nothing and the breakers' base is laid; y the depth (m) from which the
/// sea is whole; z the breakers' height (m); w unused. All zero with no shore: open water throughout.
float4 _FishWaterBreakDepth;

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

/// <summary>True when a shore is keeping time: the surf follows its clock rather than the sea's.</summary>
bool FishWaterSurfKeepsTime()
{
	return _FishWaterSwashPeriod > 0.25;
}

/// <summary>
/// A number in [0, 1) from a cell, from integer arithmetic alone — sin() hashes differ between GPUs,
/// and every pass that reads the shore's rhythm has to see the same one.
/// </summary>
float FishWaterSurfHash(int2 cell)
{
	uint h = asuint(cell.x) * 73856093u ^ asuint(cell.y) * 19349663u;
	h ^= h >> 16;
	h *= 0x7feb352du;
	h ^= h >> 15;
	h *= 0x846ca68bu;
	h ^= h >> 16;
	return float(h & 0xFFFFFFu) / 16777216.0;
}

/// <summary>Smooth value noise with one feature to a cell this many metres across, 0 to 1.</summary>
float FishWaterSurfNoise(float2 xz, float cellMetres, int salt)
{
	float2 p = xz / cellMetres;
	float2 i = floor(p);
	float2 f = p - i;
	f = f * f * (3.0 - 2.0 * f);
	int2 c = int2(i) + int2(salt, salt * 7);
	float a = FishWaterSurfHash(c);
	float b = FishWaterSurfHash(c + int2(1, 0));
	float d = FishWaterSurfHash(c + int2(0, 1));
	float e = FishWaterSurfHash(c + int2(1, 1));
	return lerp(lerp(a, b, f.x), lerp(d, e, f.x), f.y);
}

/// <summary>
/// Where the peaks of the surf stand along a shore: 1 on a peak, 0 on the shoulders between them,
/// a few tens of metres apart.
/// </summary>
float FishWaterSurfPeak(float2 xz)
{
	return FishWaterSurfNoise(xz, 38.0, 47);
}

/// <summary>
/// How the waves at a point of the shore differ from the rest of it: x a phase offset in cycles, y a
/// share of the beach's run-up.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every beach ran up in lockstep.</b> With the swash a function of height alone, every point of
/// every shore at the same height did the same thing at the same moment — the whole coastline
/// breathing in and out as one line. A real shore is nothing like that. Swell arrives at an angle,
/// so each wave reaches one end of a beach before the other and zips along it; wave groups hit one
/// stretch while the next is draining; and a beach carves itself into cusps, a few tens of metres
/// apart, where the water runs up the horns and not the bays.
/// </para>
/// <para>
/// So the phase leans along the direction the sea is running — about seventy metres of shore per
/// wave — wanders over a smooth field ninety metres across and peels away from the peaks
/// (FishWaterSurfPeak), and the run-up swells and shrinks over another thirty-five across.
/// </para>
/// <para>
/// <b>The lean runs DOWNWIND.</b> A wave running with the wind reaches the upwind end of a beach
/// first, so the phase falls with distance downwind: later arrival further along. It was written
/// the other way round, which zipped every run-up along the beach against the sea that made it.
/// </para>
/// </remarks>
float2 FishWaterAlongShore(float2 xz)
{
	float2 wind = _FishWaterWind.xy;
	float2 direction = dot(wind, wind) > 1e-4 ? normalize(wind) : float2(0.0, 1.0);
	float wander = FishWaterSurfNoise(xz, 90.0, 11);
	float cusps = FishWaterSurfNoise(xz, 35.0, 29);
	/* And the PEEL: a peak breaks first and its shoulders after it, up to two fifths of a wave later,
	 * so the break runs outward along the crest from every peak at ten or twenty metres a second —
	 * breaker and swash alike, since both read this. A seventh of a wave was tried and the whole line
	 * still broke as one. A larger phase is further on, so the shoulders take it away. */
	float phase = -dot(xz, direction) / 70.0 + wander * 1.6 - (1.0 - FishWaterSurfPeak(xz)) * 0.4;
	return float2(phase, 0.7 + 0.6 * cusps);
}

/// <summary>
/// How much of the open sea's wave energy reaches a shore facing this way, 0.25 to 1.
/// </summary>
/// <param name="towardShore">Unit direction to the nearest shore.</param>
/// <param name="confidence">How sure that direction is: 0 on a ridge midway between two shores.</param>
/// <remarks>
/// <para>
/// <b>A lee shore is sheltered.</b> Surf was drawn on every shore alike, so every island stood in a
/// bullseye of breakers closing on it from all sides at once — the sea arriving from every direction
/// at the same moment. A shore the sea runs straight at takes all of it; one it runs past, a share;
/// a lee shore facing away takes a quarter, the swell that bends round the ends and the old swell
/// from distant weather.
/// </para>
/// <para>
/// On a ridge midway between two shores the direction flips from one to the other, and so would
/// this; there it eases to a middling share from both sides, so the surf has no seam down the
/// middle of a channel.
/// </para>
/// </remarks>
float FishWaterShoreExposure(float2 towardShore, float confidence)
{
	float2 wind = _FishWaterWind.xy;
	float2 direction = dot(wind, wind) > 1e-4 ? normalize(wind) : float2(0.0, 1.0);
	float facing = smoothstep(-0.6, 0.4, dot(towardShore, direction));
	return lerp(0.6, lerp(0.25, 1.0, facing), saturate(confidence));
}

/// <summary>
/// Which wave the shore is on at a point of the waterline, in cycles: the swash begins its rush
/// where this is a whole number, and that is where a surf crest arrives.
/// </summary>
float FishWaterSurfCycles(float2 waterlineXZ)
{
	return _FishWaterSwashCycles + FishWaterAlongShore(waterlineXZ).x;
}

#endif
