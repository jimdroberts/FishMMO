#ifndef FISHMMO_VORTEX_INCLUDED
#define FISHMMO_VORTEX_INCLUDED

// A tornado or a dust devil: its axis, the water its low pressure condenses and the dust its wind
// lifts, and how both are lit. VortexPhysics.cs is the C# twin of every physical function here, and the
// tests pin it: the two have to stay in step. VortexPresenter fills the property block once per vortex;
// FishVortex.shader marches a box through what this describes, and FishVortexDebris.shader throws the
// flecks round it on the same axis.

// ── The vortex ─────────────────────────────────────────────────────────────
float4 _VortexCentre;     // x, z: its foot, where it stands on the ground; y the ground's height; w how tall it stands: the wall cloud's base above the ground for a tornado, the whirl's height for a dust devil (m)
float4 _VortexWind;       // x core radius (m), y peak wind (m/s), z gravity (m/s²), w spin (+1 anticlockwise seen from above)
float4 _VortexLean;       // xy where the top stands from the foot (m, world x/z); z how far the axis wanders (m); w how far above the top the funnel reaches into the cloud (m)
float4 _VortexFlow;       // x how fast the air climbs the axis (m/s); y sub-vortices (0, or 2..6); z how hollow the core is (a two-celled vortex), 0..1; w 1 while the sky has published the clouds' light
float4 _VortexTime;       // x seconds, wrapped; y a number of its own, 0..1; z how far it has roped out, 0..1; w unused
float4 _VortexPace;       // what its motions are TIMED by, the cell's at maturity and constant for its life: x the climb (m/s), y the core radius (m), z the peak wind (m/s); w the clock's wrap (s)

// ── What it puts in the air (the volume only) ─────────────────────────────
float4 _VortexDust;       // x the wind that starts the ground moving (m/s); y how high the dust stands (m); z its extinction where the wind carries all it can (1/m); w how fast it thins upward (a power of the height left)
float4 _VortexDustShape;  // x its reach at the ground, y at its top, in core radii; z the tube the march is kept to (m); w unused
float4 _VortexLook;       // rgb the colour of what it lifts; a the funnel's extinction per (metre of lift past condensation)^⅔ — 0 for a dust devil, which condenses nothing
float4 _VortexSky;        // x how much of the sun the storm lets through to it; y how much of the sky it sees under the storm's base; z the drops' forward scatter; w the widest the funnel flares into the cloud (m)
float4 _VortexMarch;      // x strides; y the nearest the march starts (m); z 1 with a noise volume bound; w how far under the top the flare is wide (m)
float4 _VortexStriae;     // x how far the inflow's moisture swings its condensation level either way (m, VortexPhysics.CondensationSwing) — 0 draws no bands; y the wavelength of the rolls it comes through (m); z how high the sky it sees reaches, the storm base's edge (radians, π/2 in the open); w the ground's share of the light an upright face gets
TEXTURE3D(_VortexNoise);
SAMPLER(sampler_VortexNoise);

// The light the sky gives its clouds (SkySystem publishes these for FishCloudVolume.hlsl): the funnel
// is cloud, and lit as the storm's own cloud is, it is the same white where the two meet.
float4 _FishCloudSunDir;   // xyz toward whatever lights the clouds now, w its strength
float4 _FishCloudSunColor; // rgb its colour
float4 _FishCloudAmbient;  // rgb the skylight a cloud sits in, already scaled
float4 _FishCloudHaze;     // rgb the colour distance turns a cloud, a the metres over which it does

#define VORTEX_TAU 6.2831853
// VortexPhysics.SubVortexOrbit, SubVortexCore, SubVortexWind, SubVortexTurn.
#define VORTEX_SUB_ORBIT 0.8
#define VORTEX_SUB_CORE 0.25
#define VORTEX_SUB_WIND 0.5
#define VORTEX_SUB_TURN 0.5
// The clouds' own gain on the sun (FishCloudColour): the funnel's white is the cloud's white.
#define VORTEX_SUN_GAIN 3.5
// VortexPhysics.BandCliff, and asin of it: the peak of a band's ramp-and-cliff.
#define VORTEX_BAND_CLIFF 0.9
#define VORTEX_BAND_PEAK 1.1197695
// VortexPhysics.StriationStarts for its four octaves, and each to the power −⅓ (BandSwing's cube root).
#define VORTEX_BAND_STARTS float4(3.0, 7.0, 15.0, 31.0)
#define VORTEX_BAND_THIRDS float4(0.6933613, 0.5227580, 0.4054801, 0.3183314)
// VortexPhysics.StriationsWholeWithin and StriationsGoneBy, in core radii: where the spin stops holding
// the inflow's streamlines apart.
#define VORTEX_STRIAE_WHOLE 1.5
#define VORTEX_STRIAE_GONE 2.0
// acos(BandCliff)/π: the share of a band its cliff takes (VortexPhysics.CliffWeight).
#define VORTEX_CLIFF_SHARE 0.1435663
// How far out on the noise's plane the ring the inflow's grain is read round sits, in tiles: about
// five of its Perlin billows round the funnel, and ten to forty Worley cells.
#define VORTEX_GRAIN_RING 0.2

// ── Physics: VortexPhysics, line for line ─────────────────────────────────

/// <summary>VortexPhysics.CondensationRadius: the funnel's radius this far below the cloud base.</summary>
float VortexCondensationRadius(float belowBase, float coreRadius, float peakWind, float gravity)
{
	if (belowBase <= 0.0)
	{
		return 1e6;
	}
	float depth = peakWind * peakWind / max(0.05, gravity);
	if (depth <= 1e-4)
	{
		return 0.0;
	}
	float share = belowBase / depth;
	if (share > 1.0)
	{
		return 0.0;
	}
	return share <= 0.5
		? coreRadius / sqrt(2.0 * share)
		: coreRadius * sqrt(2.0 * (1.0 - share));
}

/// <summary>VortexPhysics.DeficitLift: the lift the vortex's low pressure is worth at a radius, m.</summary>
float VortexDeficitLift(float radius, float coreRadius, float peakWind, float gravity)
{
	float depth = peakWind * peakWind / max(0.05, gravity);
	float x = abs(radius) / max(0.01, coreRadius);
	return x < 1.0 ? depth * (1.0 - 0.5 * x * x) : depth * 0.5 / (x * x);
}

/// <summary>VortexPhysics.WindAt: the wind round the axis at a radius, m/s.</summary>
float VortexWindAt(float radius, float coreRadius, float peakWind)
{
	float core = max(0.01, coreRadius);
	return radius < core ? peakWind * radius / core : peakWind * core / max(core, radius);
}

/// <summary>VortexPhysics.AngularSpeed: radians a second at a radius.</summary>
float VortexAngularSpeed(float radius, float coreRadius, float peakWind)
{
	float core = max(0.01, coreRadius);
	return radius < core ? peakWind / core : peakWind * core / max(1e-4, radius * radius);
}

/// <summary>VortexPhysics.HelixRate: how fast the wall's streamlines turn as they climb, radians per metre.</summary>
float VortexHelixRate(float belowBase, float coreRadius, float peakWind, float gravity, float climb)
{
	float depth = peakWind * peakWind / max(0.05, gravity);
	if (depth <= 1e-4 || belowBase <= 0.0)
	{
		return 0.0;
	}
	float share = belowBase / depth;
	float core = peakWind / (max(0.01, coreRadius) * max(0.5, climb));
	return share <= 0.5 ? core * 2.0 * share : core;
}

/// <summary>VortexPhysics.HelixTurn: how far round the wall's streamlines have turned since the cloud base, radians.</summary>
float VortexHelixTurn(float belowBase, float coreRadius, float peakWind, float gravity, float climb)
{
	float depth = peakWind * peakWind / max(0.05, gravity);
	if (depth <= 1e-4 || belowBase <= 0.0)
	{
		return 0.0;
	}
	float share = belowBase / depth;
	float wound = share <= 0.5 ? share * share : share - 0.25;
	return depth * peakWind / (max(0.01, coreRadius) * max(0.5, climb)) * wound;
}

/// <summary>VortexPhysics.BandWeight, for the four octaves at once: all of a band four pixels across, none at two.</summary>
float4 VortexBandWeight(float4 spacing, float footprint)
{
	return saturate(0.5 * spacing / max(1e-4, footprint) - 1.0);
}

/// <summary>
/// VortexPhysics.RampCliff, for the four octaves at once: a long ramp and a sharp cliff, −1 to 1 — and
/// how steeply it climbs there, per radian (VortexPhysics.RampCliffSlope).
/// </summary>
float4 VortexRampCliff(float4 phase, out float4 slope)
{
	float4 s, c;
	sincos(phase, s, c);
	slope = VORTEX_BAND_CLIFF * (c - VORTEX_BAND_CLIFF) / ((1.0 + VORTEX_BAND_CLIFF * VORTEX_BAND_CLIFF - 2.0 * VORTEX_BAND_CLIFF * c) * VORTEX_BAND_PEAK);
	return atan2(VORTEX_BAND_CLIFF * s, 1.0 - VORTEX_BAND_CLIFF * c) / VORTEX_BAND_PEAK;
}

/// <summary>VortexPhysics.CliffWeight, for the four octaves at once: a cliff's shading drawn once it is two pixels across, none at one.</summary>
float4 VortexCliffWeight(float4 spacing, float footprint)
{
	return saturate(spacing * VORTEX_CLIFF_SHARE / max(1e-4, footprint) - 1.0);
}

/// <summary>VortexPhysics.StriationsKept: how much of the bands survive at a radius, all to 1.5 R, none past 2 R.</summary>
float VortexStriaeKept(float radius, float coreRadius)
{
	return 1.0 - smoothstep(VORTEX_STRIAE_WHOLE, VORTEX_STRIAE_GONE, radius / max(0.01, coreRadius));
}

/// <summary>VortexPhysics.DeficitSlope: how fast the vortex's lift falls outward, m per m (negative).</summary>
float VortexDeficitSlope(float radius, float coreRadius, float peakWind, float gravity)
{
	float depth = peakWind * peakWind / max(0.05, gravity);
	float core = max(0.01, coreRadius);
	float x = max(1e-3, abs(radius) / core);
	return x < 1.0 ? -depth * x / core : -depth / (core * x * x * x);
}

/// <summary>VortexPhysics.SkyRing: the sky a face whose normal stands this far above level gets from a ring of it at one elevation.</summary>
float VortexSkyRing(float normalElevation, float skyElevation)
{
	float a = cos(normalElevation) * cos(skyElevation);
	float b = sin(normalElevation) * sin(skyElevation);
	if (b >= a)
	{
		return VORTEX_TAU * b;
	}
	if (b <= -a)
	{
		return 0.0;
	}
	return 2.0 * (sqrt(max(0.0, a * a - b * b)) + b * acos(clamp(-b / max(1e-6, a), -1.0, 1.0)));
}

/// <summary>VortexPhysics.SkyOnFace: the sky and ground a face gets against an upright face's, under a sky reaching up to the storm base's edge.</summary>
float VortexSkyOnFace(float normalElevation, float skyEdge, float ground)
{
	float low = 0.25 * skyEdge;
	float high = 0.75 * skyEdge;
	float cl = cos(low);
	float ch = cos(high);
	float sky = cl * VortexSkyRing(normalElevation, low) + ch * VortexSkyRing(normalElevation, high);
	float upright = 2.0 * (cl * cl + ch * ch);
	float g = saturate(ground);
	return (1.0 - g) * sky / max(1e-6, upright) + g * (1.0 - sin(normalElevation));
}

// ── The axis ──────────────────────────────────────────────────────────────

/// <summary>
/// How far round a motion of this many cycles a second has come, 0..1, on the shared clock — its rate
/// snapped to a whole number of cycles in the clock's wrap, so it comes round to where it started as the
/// clock wraps. A rate times the clock jumps by the change times the whole clock whenever the rate
/// changes, so every rate given here is a constant: the cell's pace (_VortexPace), never its live form,
/// which grows and decays every tick and scrubbed every motion with it.
/// </summary>
float VortexCycles(float cyclesPerSecond)
{
	float wrap = max(1.0, _VortexPace.w);
	return frac(round(cyclesPerSecond * wrap) / wrap * _VortexTime.x);
}

/// <summary>
/// Where the vortex's axis is at a height above the ground, world x/z. The foot is where the storm
/// cell is; the top stands ahead of it (VortexPhysics.TopOffset), upright where it leaves the cloud and
/// bent over toward the ground, since the difference between the storm and the air held to the ground
/// is all near the ground. And it wanders: waves travel up a vortex like a plucked rope, most in the
/// middle of the column and not at all at either end, and shorter as it ropes out — which is what makes
/// a dying tornado sinuous. The funnel, its dust and its flecks all stand on this.
/// </summary>
float2 VortexAxis(float height)
{
	float top = max(1.0, _VortexCentre.w);
	float z = saturate(height / top);
	float2 lean = _VortexLean.xy * (z * (2.0 - z));
	float bow = 4.0 * z * (1.0 - z);
	float wave = top * lerp(0.9, 0.4, saturate(_VortexTime.z));
	float seed = _VortexTime.y * VORTEX_TAU;
	// 0.21 and 0.17 rad/s, whole in the clock's wrap.
	float2 sway = _VortexLean.z * bow * float2(
		sin(VORTEX_TAU * (height / wave - VortexCycles(0.21 / VORTEX_TAU)) + seed),
		cos(VORTEX_TAU * (height / (1.4 * wave) - VortexCycles(0.17 / VORTEX_TAU)) + seed * 1.7));
	return _VortexCentre.xz + lean + sway;
}

// ── Noise ─────────────────────────────────────────────────────────────────
// The clouds' own shape volume (r Perlin pulled toward Worley, g/b/a Worley at 8, 16 and 32 cells a
// tile), read at the mip a sample's footprint asks for so detail finer than a pixel averages away.

float VortexLod(float footprint, float tileMetres)
{
	return max(0.0, log2(max(1e-4, footprint * 128.0 / tileMetres)));
}

float4 VortexNoise(float3 uvw, float lod)
{
	if (_VortexMarch.z < 0.5)
	{
		return float4(0.5, 0.5, 0.5, 0.5);
	}
	return SAMPLE_TEXTURE3D_LOD(_VortexNoise, sampler_VortexNoise, uvw, lod);
}

/// <summary>
/// A point in the frame the vortex's flow stands still in, as noise coordinates: which streamline it
/// is on, and how far up that streamline, stretched along it.
/// </summary>
/// <remarks>
/// <para>
/// Every parcel of the air goes round at the vortex's angular speed at its radius, ω, and up at the
/// axis's climb w — so it keeps θ − (ω/w)·h, which labels the helix it is on. A pattern held on
/// those labels never winds up however long the game runs, because nothing in it grows with time;
/// the only thing that moves is how far up each helix the pattern has slid, at w. Yet everything in
/// it goes round at its own radius's rate as it climbs: the core fast and together, the flare slow.
/// Winding the pattern round at each radius's own rate in TIME, as the first funnel did, turned it
/// into stripes finer than a pixel within minutes.
/// </para>
/// <para>
/// Stretched along the helices, the noise lies in streaks wound up at the pitch the flow has — w over
/// the wind. The debris cloud is read on it. The funnel's striations are not: this turns every radius
/// at its own rate from the ground up, which winds the pattern round the axis a little more at every
/// height, and on a funnel hanging a kilometre up it had wound its streaks finer than a pixel. The
/// bands follow the wall's own turn instead (VortexHelixTurn, VortexGrain).
/// </para>
/// </remarks>
/// <param name="paceTile">The tile at the cell's pace (_VortexPace): how fast the pattern slides is timed by it, its size by <paramref name="tile"/>.</param>
float3 VortexStream(float2 offset, float radius, float height, float stretch, float tile, float paceTile)
{
	float climb = max(0.5, _VortexFlow.x);
	float omega = _VortexWind.w * VortexAngularSpeed(radius, _VortexWind.x, _VortexWind.y);
	float s, c;
	sincos(-omega / climb * height, s, c);
	float2 label = float2(offset.x * c - offset.y * s, offset.x * s + offset.y * c);
	// Slid up the streamline with the air, a whole tile at a time so a long-running clock keeps its
	// precision, at the pace's climb over the pace's tile: the live ones change as it grows.
	float along = tile * stretch;
	float slid = height - along * VortexCycles(max(0.5, _VortexPace.x) / max(1.0, paceTile * stretch));
	return float3(label.x / tile, slid / along, label.y / tile);
}

/// <summary>
/// The inflow's grain where a streamline on the wall came from: the noise read on a ring round the
/// band's own phase and slid up it with the air — so it is the same all the way along a streamline
/// but for the air arriving later, and never winds up.
/// </summary>
/// <param name="phase">The angle round the axis plus the wall's turn (VortexHelixTurn): which streamline.</param>
/// <remarks>
/// It says how the air arriving along each band differed from the bands' mean — how damp (how bold the
/// band), and how far the inflow had carried it aside (a band's wander). The funnel's light is read a
/// few tens of metres away with the grain of the point it lights, which the grain barely changes over.
/// </remarks>
float4 VortexGrain(float phase, float height, float radius, float footprint)
{
	if (_VortexMarch.z < 0.5)
	{
		return float4(0.5, 0.5, 0.5, 0.5);
	}
	float core = max(1.0, _VortexWind.x);
	// Billows a core radius or so long up the streamlines, slid up them at the air's climb, a whole
	// tile at a time so a long-running clock keeps its precision.
	float along = 4.0 * core;
	float slid = height - along * VortexCycles(max(0.5, _VortexPace.x) / (4.0 * max(1.0, _VortexPace.y)));
	float s, c;
	sincos(phase, s, c);
	float3 uvw = float3(VORTEX_GRAIN_RING * c, slid / along, VORTEX_GRAIN_RING * s) + _VortexTime.y * float3(3.7, 1.3, 5.9);
	// A tile is 1/ring of the radius round the funnel and `along` up it: the finer sets the mip.
	float tile = min(max(1.0, radius) / VORTEX_GRAIN_RING, along);
	return VortexNoise(uvw, VortexLod(footprint, tile));
}

// ── What it puts in the air ───────────────────────────────────────────────

/// <summary>
/// The lift the sub-vortices add at a point, m: each a small Rankine vortex going round the parent's
/// core near its wind's peak, carried at half the parent's speed, its tube wound up the column along
/// the flow. Zero with none.
/// </summary>
float VortexSubLift(float2 offset, float radius, float height)
{
	int count = (int)(_VortexFlow.y + 0.5);
	if (count < 2)
	{
		return 0.0;
	}
	float core = max(1.0, _VortexWind.x);
	float orbit = VORTEX_SUB_ORBIT * core;
	float subCore = VORTEX_SUB_CORE * core * rsqrt((float)count);
	// Far enough from the ring that what they add is a sixtieth of the least of them.
	if (abs(radius - orbit) > 6.0 * subCore)
	{
		return 0.0;
	}
	float subWind = VORTEX_SUB_WIND * _VortexWind.y;
	float turn = VORTEX_SUB_TURN * _VortexWind.y / core;
	float climb = max(0.5, _VortexFlow.x);
	// Round in time at the pace's turn; wound up the column at the live one, which is its shape.
	float paceTurn = VORTEX_SUB_TURN * _VortexPace.z / max(1.0, _VortexPace.y);
	float phase = _VortexWind.w * (VORTEX_TAU * VortexCycles(paceTurn / VORTEX_TAU) - turn / climb * height) + _VortexTime.y * VORTEX_TAU;
	float s, c;
	sincos(phase, s, c);
	float2 at = float2(c, s) * orbit;
	float stepS, stepC;
	sincos(VORTEX_TAU / count, stepS, stepC);
	float lift = 0.0;
	UNITY_LOOP
	for (int i = 0; i < 6; i++)
	{
		if (i >= count)
		{
			break;
		}
		lift += VortexDeficitLift(length(offset - at), subCore, subWind, _VortexWind.z);
		at = float2(at.x * stepC - at.y * stepS, at.x * stepS + at.y * stepC);
	}
	return lift;
}

/// <summary>
/// The condensed water's extinction at a point, 1/m: the funnel.
/// </summary>
/// <remarks>
/// <para>
/// Where the vortex's low pressure has taken the air past its condensation, it holds the water that
/// much lift condenses, and takes light out as the two-thirds power of it — exactly as the cloud above
/// does with its height over its base (AirColumn.ExtinctionCoefficient). So nothing here is made
/// opaque or soft by hand: the core, lifted hundreds of metres past its base, is dense; the edge,
/// lifted a metre past it, is a veil; the tip fades out as the excess does.
/// </para>
/// <para>
/// <b>The striations</b> are the inflow's moisture (VortexPhysics.CondensationSwing): each streamline
/// keeps the condensation level of the air it brought, so a damp one has that much more lift past
/// its condensation all the way up the wall and a dry one that much less. That is added to the excess
/// as bands wound round the wall at its own pitch (VortexHelixTurn) — octaves of 3 to 31 round the
/// funnel, each swinging the lift as the cube root of its size, each a ramp and a cliff, each kept only
/// while a band is wider than a few pixels (VortexBandWeight) so the far funnel keeps its broad bands
/// and never turns to grey noise. A metre of lift is a third of a metre of radius at the core's edge
/// of a middling tornado, so the bands are ledges a few per cent of its width deep — deeper on a rope
/// (V²/g short against the swing) than a wedge — and the same excess that moves the wall thins the
/// water in a dry band's skin, which is what the light (VortexSource) and the silhouette show.
/// </para>
/// <para>
/// <b>Only where the spin holds them</b> (VortexStriaeKept): whole to 1.5 core radii, gone by 2. Added
/// everywhere, as they first were, the swing moved the funnel's edge by itself over the deficit's slope
/// — metres at the core's edge, but hundreds where the funnel flares and the deficit is nearly flat —
/// and the funnel's top burst into radial spokes: every damp sector condensed out to the flare's limit.
/// Past 2 R nothing is read, which also spares the whole flare the four octaves' cost.
/// </para>
/// <para>
/// It is frayed where it is thin, most at the tip and as it ropes out. A breakdown hollows the core
/// round its sinking centre, and its sub-vortices add their own lows, so each hangs its own tube below
/// the parent's. Above the base it adds only what the vortex lowers the cloud by, fading out into the
/// wall cloud the sky draws there.
/// </para>
/// </remarks>
/// <param name="fine">Reads the inflow's grain here into <paramref name="grain"/>; otherwise uses the grain given.</param>
/// <param name="banded">Draws the bands and the fraying: the march, and the light's taps near enough to see a band's ledge.</param>
/// <param name="slope">
/// What tilts the funnel's skin here, for its light (VortexSource): x how fast the vortex's lift falls
/// outward (m per m); y how fast the bands' lift climbs with their phase (m per radian), only from the
/// octaves whose cliffs a pixel resolves; z how fast the phase turns per metre of height (the spin times
/// the helix's rate); w how deep the bands stand off the wall (m) — 0 where there are none.
/// </param>
float VortexCondensate(float2 offset, float radius, float height, float subLift, float footprint, bool fine, bool banded, inout float4 grain, out float4 slope)
{
	slope = float4(0.0, 0.0, 0.0, 0.0);
	float extinction = _VortexLook.a;
	float below = _VortexCentre.w - height;
	float over = max(1.0, _VortexLean.w);
	if (extinction <= 0.0 || below < -over || radius > _VortexSky.w)
	{
		return 0.0;
	}
	float core = max(1.0, _VortexWind.x);
	float wind = _VortexWind.y;
	float gravity = _VortexWind.z;
	float lift = VortexDeficitLift(radius, core, wind, gravity) + subLift;

	// Which streamline of the wall this is, how far apart its bands lie across them, and how far each
	// octave swings the lift here — faded as its bands narrow toward two pixels, and as the spin stops
	// holding them apart.
	float4 amplitude = float4(0.0, 0.0, 0.0, 0.0);
	float4 resolved = float4(0.0, 0.0, 0.0, 0.0);
	float phase = 0.0;
	float rate = 0.0;
	float kept = VortexStriaeKept(radius, core);
	bool striated = false;
	if (banded && _VortexStriae.x > 0.0)
	{
		// The streamline is wanted even where its bands are gone: the fraying's grain is read along it.
		float climb = max(0.5, _VortexFlow.x);
		// The turn in whole turns dropped: every octave's count is whole, so its bands are unchanged,
		// and a rope's hundreds of radians times thirty-one would lose a sine's precision.
		float turn = VortexHelixTurn(below, core, wind, gravity, climb);
		phase = atan2(offset.y, offset.x) + _VortexWind.w * VORTEX_TAU * frac(turn / VORTEX_TAU);
		rate = VortexHelixRate(below, core, wind, gravity, climb);
		striated = kept > 0.0;
	}
	if (striated)
	{
		float r = max(0.5, radius);
		// VortexPhysics.BandSpacing and BandSwing for the four octaves.
		float4 spacing = VORTEX_TAU / (VORTEX_BAND_STARTS * sqrt(1.0 / (r * r) + rate * rate));
		float size = pow(VORTEX_TAU * r / max(1.0, _VortexStriae.y), 1.0 / 3.0);
		amplitude = _VortexStriae.x * kept * min(1.0, size * VORTEX_BAND_THIRDS) * VortexBandWeight(spacing, footprint);
		resolved = VortexCliffWeight(spacing, footprint);
	}
	// Outside the funnel even where its dampest streamlines, at the grain's boldest, stand furthest out:
	// no noise to read.
	float most = 1.5 * (amplitude.x + amplitude.y + amplitude.z + amplitude.w);
	if (lift + most <= below)
	{
		return 0.0;
	}
	if (fine)
	{
		grain = VortexGrain(phase, height, radius, footprint);
	}
	float erosion = 0.0;
	float bands = 0.0;
	if (banded)
	{
		// Frayed where it is thin: a few percent of the funnel's depth, more as it ropes out.
		float depth = wind * wind / max(0.05, gravity);
		erosion = depth * (0.03 + 0.08 * _VortexTime.z) * (1.0 - grain.a);
	}
	if (striated)
	{
		// Each octave's bands, bolder or fainter as the air along them came damper or drier than the
		// rolls' mean, wandering a little where the inflow carried them aside, set where the storm's own
		// number puts them.
		float4 wander = 1.2 * (grain.gbar - 0.5) + _VortexTime.y * VORTEX_TAU * float4(1.0, 2.3, 3.7, 5.1);
		float4 swing = amplitude * lerp(0.5, 1.5, grain);
		float4 climbs;
		bands = dot(swing, VortexRampCliff(VORTEX_BAND_STARTS * phase + wander, climbs));
		float falls = VortexDeficitSlope(radius, core, wind, gravity);
		slope = float4(falls, dot(swing * resolved * VORTEX_BAND_STARTS, climbs), _VortexWind.w * rate,
			(swing.x + swing.y + swing.z + swing.w) / max(1.0, -falls));
	}
	float excess = lift - below - erosion + bands;
	if (excess <= 0.0)
	{
		return 0.0;
	}
	float beta = extinction * pow(excess, 2.0 / 3.0);
	// A breakdown's sinking centre, warmed by its own compression on the way down, evaporates the
	// middle of the core and leaves its wall: a tube, a ring seen from underneath. Its low pressure is
	// still there, so the wall and the sub-vortices in it are not touched.
	beta *= 1.0 - _VortexFlow.z * (1.0 - smoothstep(0.25 * core, 0.55 * core, radius));
	if (below < 0.0)
	{
		// Inside the cloud: only what the vortex adds to what the cloud has already condensed there, and
		// only round the funnel itself, which it carries on up into the cloud. Past that the lowering is
		// the wall cloud's, which the sky draws.
		beta = max(0.0, beta - extinction * pow(-below, 2.0 / 3.0)) * (1.0 - smoothstep(0.0, over, -below))
			* (1.0 - smoothstep(1.5 * core, 2.5 * core, radius));
	}
	return beta * (1.0 - smoothstep(0.75 * _VortexSky.w, _VortexSky.w, radius));
}

/// <summary>
/// What the vortex has lifted off the ground at a point, 1/m: its debris cloud, or the whole of a dust
/// devil — dust, soil, spray over the sea.
/// </summary>
/// <remarks>
/// As thick as the wind at the core's edge lifts this ground (VortexPhysics.Lifted, folded into its
/// extinction), a skirt at the ground as wide as the wind lifts it and narrowing up round the core as
/// the inflow gathers it into the updraught — or, for a dust devil, spreading as its plume does —
/// densest at the ground, thinning upward and ending in billows. It turns with the air and climbs
/// with it, on the same streamlines as the funnel.
/// </remarks>
float VortexDustAt(float2 offset, float radius, float height, float footprint, bool fine)
{
	float top = max(1.0, _VortexDust.y);
	if (_VortexDust.z <= 0.0 || height < -5.0 || height > 1.35 * top)
	{
		return 0.0;
	}
	float core = max(0.5, _VortexWind.x);
	// Beyond its widest billow: no noise to read.
	if (radius >= 1.2 * core * max(_VortexDustShape.x, _VortexDustShape.y))
	{
		return 0.0;
	}
	float lumps = 0.5;
	float ragged = 0.5;
	if (fine && _VortexMarch.z > 0.5)
	{
		float tile = clamp(3.0 * core, 20.0, 800.0);
		float paceTile = clamp(3.0 * max(0.5, _VortexPace.y), 20.0, 800.0);
		float4 n = VortexNoise(VortexStream(offset, radius, height, 2.0, tile, paceTile) + _VortexTime.y * float3(7.1, 2.9, 4.3), VortexLod(footprint, tile));
		lumps = n.r * 0.7 + n.b * 0.3;
		ragged = n.g;
	}
	// Its top is wherever the billows have carried it.
	float topHere = top * (0.75 + 0.6 * ragged);
	if (height >= topHere)
	{
		return 0.0;
	}
	float rise = saturate(height / topHere);
	float spread = core * lerp(_VortexDustShape.x, _VortexDustShape.y, sqrt(rise)) * (0.8 + 0.4 * lumps);
	if (radius >= spread)
	{
		return 0.0;
	}
	float edge = 1.0 - smoothstep(0.45, 1.0, radius / spread);
	return _VortexDust.z * edge * pow(1.0 - rise, _VortexDust.w) * lerp(0.3, 1.7, lumps);
}

/// <summary>What the vortex puts in the air at a point, 1/m: x its condensed water, y what it has lifted.</summary>
/// <param name="fine">The march's own sample: the sub-vortices, the dust's billows, and the inflow's grain read here into <paramref name="grain"/>.</param>
/// <param name="banded">With the funnel's bands and fraying, on <paramref name="grain"/>: the march, and the light's near taps.</param>
/// <param name="slope">What tilts the funnel's skin here (VortexCondensate), for its light.</param>
float2 VortexMedia(float3 position, float footprint, bool fine, bool banded, inout float4 grain, out float2 offset, out float4 slope)
{
	float height = position.y - _VortexCentre.y;
	offset = position.xz - VortexAxis(height);
	float radius = length(offset);
	float subLift = fine ? VortexSubLift(offset, radius, height) : 0.0;
	return float2(VortexCondensate(offset, radius, height, subLift, footprint, fine, banded, grain, slope), VortexDustAt(offset, radius, height, footprint, fine));
}

// ── Light ─────────────────────────────────────────────────────────────────

/// <summary>The clouds' phase function (FishCloudPhase): a strong forward lobe and a soft back one, over 4π.</summary>
float VortexPhase(float cosAngle, float g)
{
	float g2 = g * g;
	float forward = (1.0 - g2) / (4.0 * 3.14159265 * pow(abs(1.0 + g2 - 2.0 * g * cosAngle), 1.5));
	float back = (1.0 - g2 * 0.25) / (4.0 * 3.14159265 * pow(abs(1.0 + g2 * 0.25 + 0.5 * g * cosAngle), 1.5));
	return lerp(back, forward, 0.6);
}

/// <summary>
/// What an optical depth toward the sun lets through, scattered toward the eye: the first pass, and
/// the light that has bounced on through again and again, which each keep more and scatter more
/// evenly than the last (as the clouds take it, FishCloudScatter).
/// </summary>
float VortexScatter(float depth, float cosAngle, float g)
{
	float attenuation = 1.0;
	float contribution = 1.0;
	float eccentricity = g;
	float energy = 0.0;
	UNITY_UNROLL
	for (int o = 0; o < 3; o++)
	{
		energy += contribution * exp(-depth * attenuation) * VortexPhase(cosAngle, eccentricity);
		attenuation *= 0.5;
		contribution *= 0.55;
		eccentricity *= 0.6;
	}
	return energy;
}

/// <summary>Diffuse light through an optical depth: the first pass and the light scattered on through it.</summary>
float VortexThrough(float tau)
{
	return (exp(-tau) + 0.5 * exp(-0.25 * tau) + 0.25 * exp(-0.0625 * tau)) / 1.75;
}

/// <summary>A shoulder on brightness, as the clouds take theirs (FishCloudShoulder): the project has no tonemapper.</summary>
float3 VortexShoulder(float3 colour)
{
	float level = dot(colour, float3(0.2126, 0.7152, 0.0722));
	if (level <= 0.8)
	{
		return colour;
	}
	float rolled = 0.8 + 0.2 * (1.0 - exp(-(level - 0.8) / 0.6));
	return colour * (rolled / level);
}

/// <summary>
/// What a point of the vortex scatters toward the eye, per unit of what it takes out.
/// </summary>
/// <remarks>
/// <para>
/// The sun, through what lies toward it — two taps of the vortex that way, so the far side of a
/// funnel is in its own shadow and a dust sheath darkens the funnel's foot — and through the storm:
/// under a supercell the sun reaches a tornado only where its ray gets in under the storm's base,
/// which is why one is dark grey under a high sun and lit gold from the side at the end of the day.
/// The drops throw light forward, so a funnel against the sun is a dark core with a burning rim.
/// </para>
/// <para>
/// The sky, through the vortex's own depth up and outward, and only what the storm's base leaves of
/// it: the horizon round the storm, not the dark cloud overhead. Water is white and scatters all it
/// takes; what is lifted is its ground's own colour.
/// </para>
/// <para>
/// <b>The bands are seen by their light.</b> Ledges on a cloud as thick as a funnel show two ways. They
/// shade each other: under a ledge the way up and out to the sky and the way to the sun run through the
/// ledge's damp skin, on top of it they run clear — so the near tap each way, a few tens of metres, the
/// height of a ledge, reads the bands, with the grain of the point it lights. And each face is lit as a
/// face: a funnel's wall is so thick that it gives back the light falling on the skin it enters by, as
/// the cosine of its incidence (VortexPhysics.SkyOnFace), and a ledge tilts that skin — the gradient of
/// the condensation excess, whose terms VortexCondensate already has (<paramref name="slope"/>). Under a
/// storm the sky that lights it is the ring below the base's edge, so a face turned down to the ground
/// is dark and one turned up to the storm's base no brighter than the wall: every ledge's short, steep
/// cliff is a dark line along it, a groove, and in the sun a ledge's top turned toward it is bright.
/// Only the octaves whose cliff a pixel spans tilt it (VortexCliffWeight), so a far funnel's shading is
/// its few broad ledges, not a hatching of the fine ones. It is the face's light only where the cloud is
/// a face — where light goes in no further than the ledge is deep; a veil is lit through, not on.
/// </para>
/// <para>
/// We measured both in a port of this march to Python (a supercell tornado on
/// Arthis, 78 m/s round a 212 m core under a 985 m wall cloud, 3 km off; the light's spread over the
/// face of its neck): under the storm, lit by the sky alone, the taps alone gave 18% rms and the faces as
/// well 19% — the same spread, but gathered from the wall into a dark line at every cliff; in the sun
/// from behind the eye, 37% and 45%, the tops of the ledges bright, and the banding that survives a
/// hundred metres' averaging up from 5% to 7%. The first funnel's taps read a smooth funnel, and its
/// grooves were lit exactly as the wall round them: 3%.
/// </para>
/// <para>
/// When the storm hides the sun entirely (under its base, <c>_VortexSky.x</c> is e^−6) the two sun taps
/// are not read at all: what they would add is a four-hundredth of the sun, and they were half the cost
/// of the light.
/// </para>
/// </remarks>
/// <param name="grain">The inflow's grain at this point (VortexGrain), for the bands the near taps read.</param>
/// <param name="slope">What tilts the funnel's skin here (VortexCondensate): w is 0 where there are no bands.</param>
float3 VortexSource(float3 position, float2 media, float2 offset, float3 toSun, float3 sunColour, float3 ambient, float cosAngle, float footprint, float4 grain, float4 slope)
{
	float extinction = media.x + media.y;
	float dust = media.y / max(1e-6, extinction);
	float reach = clamp(1.5 * _VortexWind.x, 8.0, 400.0);
	float nearTap = 0.15 * reach;
	float farTap = 0.6 * reach;
	float2 unused;
	float4 unusedSlope;
	float4 tapGrain = grain;

	// The face the skin here turns to the light: the excess's gradient with the bands and without them,
	// across the radius, round the axis and up it — outward is where the excess falls. Where the cloud
	// is thick against the ledge's depth it is lit as that face; a veil is not a face.
	float faced = 0.0;
	float3 ridged = float3(0.0, 1.0, 0.0);
	float3 plain = float3(0.0, 1.0, 0.0);
	float2 outward = float2(0.0, 0.0);
	float radius = length(offset);
	if (radius > 1e-2)
	{
		outward = offset / radius;
	}
	if (slope.w > 0.0)
	{
		float2 around = float2(-outward.y, outward.x);
		float3 across = float3(outward.x, 0.0, outward.y) * slope.x;
		plain = -normalize(across + float3(0.0, 1.0, 0.0));
		ridged = -normalize(across + float3(around.x, 0.0, around.y) * (slope.y / max(1.0, radius))
			+ float3(0.0, 1.0 - slope.z * slope.y, 0.0));
		faced = (1.0 - dust) * (1.0 - exp(-media.x * slope.w));
	}

	float sun = 0.0;
	if (_VortexSky.x > 0.01)
	{
		float2 a = VortexMedia(position + toSun * nearTap, footprint, false, true, tapGrain, unused, unusedSlope);
		float2 b = VortexMedia(position + toSun * farTap, footprint, false, false, tapGrain, unused, unusedSlope);
		float tau = (a.x + a.y) * nearTap + (b.x + b.y) * (farTap - nearTap);
		float g = lerp(_VortexSky.z, 0.55, dust);
		// Lambert's cosine on the ledge's face, changed from the smooth wall's by the band's tilt.
		float incidence = 1.0 + faced * (saturate(dot(ridged, toSun)) - saturate(dot(plain, toSun)));
		sun = VortexScatter(tau, cosAngle, g) * VORTEX_SUN_GAIN * _VortexSky.x * incidence;
	}

	float3 skyward = normalize(float3(outward.x, 1.0, outward.y));
	float skyTap = 0.5 * reach;
	float2 n = VortexMedia(position + skyward * nearTap, footprint, false, true, tapGrain, unused, unusedSlope);
	float2 c = VortexMedia(position + skyward * skyTap, footprint, false, false, tapGrain, unused, unusedSlope);
	// Half the sky a cloud's base gets (FishCloudColour at its base), and only what the storm leaves.
	float skyDepth = (n.x + n.y) * nearTap + (c.x + c.y) * (skyTap - nearTap) + 0.5 * extinction * skyTap;
	float sky = 0.5 * VortexThrough(skyDepth) * _VortexSky.y;
	if (faced > 0.0)
	{
		float edge = _VortexStriae.z;
		float ground = _VortexStriae.w;
		float turned = VortexSkyOnFace(asin(clamp(ridged.y, -1.0, 1.0)), edge, ground)
			/ max(1e-3, VortexSkyOnFace(asin(clamp(plain.y, -1.0, 1.0)), edge, ground));
		sky *= lerp(1.0, turned, faced);
	}

	float3 albedo = lerp(float3(1.0, 1.0, 1.0), _VortexLook.rgb, dust);
	return VortexShoulder(albedo * (sunColour * sun + ambient * sky));
}

#endif
