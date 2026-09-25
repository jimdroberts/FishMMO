#ifndef FISHMMO_WATER_SURF_INCLUDED
#define FISHMMO_WATER_SURF_INCLUDED

// The rhythm of the shore, shared by the sea's surf train and the shore's swash so they are one wave.
//
// ONE CLOCK. The surf ran on the sea's clock at a period of its own and the swash on the shore's,
// with a phase along the beach the surf did not have — so a wave rolled in, died at the waterline,
// and the run-up came at some unrelated moment. Nothing ever broke ONTO the shore. Both now read
// this: a surf crest reaches the waterline at the instant the swash there begins to rush up.
//
// Everything here is arithmetic, no textures, because WaterSurface.cs carries the same functions
// on the CPU for anything floating in the surf, and has to get the same answer.
//
// The includer declares _FishWaterWind first — the sea and the shore both already do.

float _FishWaterSwashPeriod;     // seconds between arriving waves; 0 when the scene has no shore
float _FishWaterShoreTime;       // the shore's clock, seconds
float _FishWaterSwashCycles;     // which wave the shore is on: the swash's phase added up, wrapped at 1000
float4 _FishWaterSwashSea;       // x significant wave height (m), y deep-water wavelength at the peak period (m)

/// <summary>True when a shore is keeping time: the surf follows its clock rather than the sea's.</summary>
bool FishWaterSurfKeepsTime()
{
	return _FishWaterSwashPeriod > 0.25;
}

/// <summary>
/// A number in [0, 1) from a cell, from integer arithmetic alone — sin() hashes differ between GPUs,
/// and between a GPU and the CPU copy in WaterSurface.cs (SurfHash).
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
/// wave — and wanders over a smooth field ninety metres across, and the run-up swells and shrinks
/// over another thirty-five across.
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
	float phase = -dot(xz, direction) / 70.0 + wander * 1.6;
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
