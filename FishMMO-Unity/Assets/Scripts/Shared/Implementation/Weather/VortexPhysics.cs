using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The shape and the spin of a tornado or a whirl, from the vortex that makes it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A Rankine vortex</b>: the core turns as a solid body, the wind rising with the radius to its
	/// peak at the core's edge, and outside it the wind falls as one over the radius. It is the model
	/// meteorology uses for a tornado because it is what one measures like, and it gives everything
	/// that is SEEN from two numbers — the core radius and the peak wind.
	/// </para>
	/// <para>
	/// <b>The funnel is where the vortex condenses the air, so it is not drawn — it is solved.</b> In
	/// cyclostrophic balance the pressure falls toward the centre by ρ·V² at the axis; and air whose
	/// pressure drops by Δp cools exactly as air lifted Δp/(ρg) does (<see cref="DeficitLift"/>). So at a
	/// height below the cloud base the air condenses wherever the vortex has dropped the pressure by
	/// enough to make up the missing lift, and it holds as much water as the lift it has been given past
	/// that (<see cref="CondensationExcess"/>) — which is why a funnel's core is dense and its edge soft,
	/// the same way a cloud's crown is dense and its base thin. That surface flares out into the cloud
	/// base at the top, narrows to a point below it, and hangs V²/g under the base — which is why a weak
	/// tornado's funnel stays aloft over its debris cloud and a violent one reaches the ground, and why
	/// the same storm on a world of lower gravity hangs a longer funnel.
	/// </para>
	/// <para>
	/// <b>Its shapes follow.</b> A thin core under a high base is a rope; a funnel whose tip just reaches
	/// the ground is a cone; one whose base is well inside its depth is a straight-sided stovepipe; a wide
	/// core under a low base is a wedge, wider than it is tall. A wide, strong core spinning much faster
	/// than its updraught can feed breaks down into several vortices going round inside it
	/// (<see cref="SwirlRatio"/>). The shaders (FishVortex.hlsl, FishVortex.shader,
	/// FishVortexDebris.shader) carry the same functions and must stay in step with these.
	/// </para>
	/// <para>
	/// <b>Its striations are its streamlines</b>, ridged by the inflow's moisture: each parcel keeps
	/// its own condensation level up its helix, so damp streamlines stand proud of dry ones
	/// (<see cref="CondensationSwing"/>, <see cref="HelixTurn"/>) — but only where the spin keeps them
	/// from mixing (<see cref="StriationsKept"/>), which is not where the funnel flares into the wall
	/// cloud — and lit as the faces of ledges (<see cref="SkyOnFace"/>).
	/// </para>
	/// </remarks>
	public static class VortexPhysics
	{
		/// <summary>The slowest and fastest tornado peak wind drawn, m/s.</summary>
		public const float WeakestTornado = 20f, StrongestTornado = 110f;

		/// <summary>
		/// How much faster a supercell's rotating updraught climbs than buoyancy alone would lift it:
		/// the low pressure in its spinning core pulls it up half again (as <see cref="WeatherPhysics.HailStoneMetres"/> takes it).
		/// </summary>
		public const float MesocycloneUpdraftGain = 1.6f;

		/// <summary>
		/// The swirl ratio past which a vortex's core breaks down into several vortices going round
		/// inside it: about 0.8 in the laboratory vortices that first showed it (Ward; Church et al.
		/// 1979), below which the breakdown hollows the core but leaves it one vortex.
		/// </summary>
		public const float MultipleVortexSwirl = 0.8f;

		/// <summary>The most sub-vortices a breakdown makes: six, in the widest and fastest.</summary>
		public const int MostSubVortices = 6;

		/// <summary>Where the sub-vortices go round, as a share of the parent's core radius: near its wind's peak.</summary>
		public const float SubVortexOrbit = 0.8f;

		/// <summary>A sub-vortex's core radius as a share of the parent's, for two of them; more share the ring as one over √n.</summary>
		public const float SubVortexCore = 0.25f;

		/// <summary>A sub-vortex's own peak wind as a share of the parent's: it adds half again where it passes.</summary>
		public const float SubVortexWind = 0.5f;

		/// <summary>
		/// How fast the sub-vortices go round, as a share of the parent core's own turning: they are
		/// carried at about half the parent's peak wind.
		/// </summary>
		public const float SubVortexTurn = 0.5f;

		/// <summary>
		/// A tornado's peak wind, m/s: the thermodynamic speed limit √(2·CAPE) the storm's air allows,
		/// times how grown the storm is (0 to 1).
		/// </summary>
		/// <remarks>
		/// The energy the air releases to a rising parcel bounds how fast the air spiralling into a
		/// vortex can turn (Rennó and Ingersoll's heat-engine limit): about 75 m/s on a sultry day of
		/// 2800 J/kg, which is a strong tornado; a day that can barely raise a storm raises a weak one.
		/// </remarks>
		public static float PeakWind(float cape, float strength)
		{
			float limit = Mathf.Clamp(Mathf.Sqrt(2f * Mathf.Max(0f, cape)), WeakestTornado, StrongestTornado);
			return limit * Mathf.Clamp01(strength);
		}

		/// <summary>How far below the cloud base the condensation funnel reaches, in metres: V²/g.</summary>
		public static float FunnelDepth(float peakWind, float gravity)
		{
			return peakWind * peakWind / Mathf.Max(0.05f, gravity);
		}

		/// <summary>
		/// The lift the vortex's pressure deficit is worth at a radius, in metres: Δp/(ρg), the height
		/// air would have to be lifted through to cool as much as the vortex's low pressure cools it.
		/// </summary>
		/// <remarks>
		/// In cyclostrophic balance the deficit is ρV²(1 − r²/2R²) inside the core and ρV²R²/(2r²)
		/// outside it, so the lift is the funnel's depth V²/g at the axis, half of it at the core's edge,
		/// and falls as one over the radius squared beyond.
		/// </remarks>
		public static float DeficitLift(float radius, float coreRadius, float peakWind, float gravity)
		{
			float depth = FunnelDepth(peakWind, gravity);
			float x = Mathf.Abs(radius) / Mathf.Max(0.01f, coreRadius);
			return x < 1f ? depth * (1f - 0.5f * x * x) : depth * 0.5f / (x * x);
		}

		/// <summary>
		/// The funnel's extinction per (metre of lift past condensation)^⅔, 1/m: the water a parcel of
		/// this air condenses for every metre it is lifted past its base, in the world's drops.
		/// </summary>
		/// <remarks>
		/// A heaped cloud holds about a third of that water, having mixed the dry air round it into
		/// itself on the way up (<see cref="AirColumn.ExtinctionCoefficient"/> takes 0.35 of it). A
		/// vortex's core does not: the air turning in it is held there by its own spin, which damps the
		/// turbulence that would mix it — so a funnel holds all it condenses, and is denser for its lift
		/// than the cloud it hangs from, by (1/0.35)^⅔, about twice.
		/// </remarks>
		public static float FunnelExtinction(in AirColumn column, in PlanetAir planet)
		{
			float n = Mathf.Max(1e6f, planet.DropletsPerCubicMetre);
			float rho = AirPhysics.LiquidDensity(planet.Condensate);
			// β(e) = 2πN·(3·gradient·e / (4πρN))^⅔  =  C·e^⅔, undiluted.
			float inner = 3f * Mathf.Max(0f, column.CondensedPerMetre) / (4f * Mathf.PI * rho * n);
			return inner > 0f ? 2f * Mathf.PI * n * Mathf.Pow(inner, 2f / 3f) : 0f;
		}

		/// <summary>
		/// How far past its condensation the vortex has taken the air at a point, in metres of lift:
		/// positive inside the funnel, where the air holds the water that much lift condenses, and
		/// negative outside it.
		/// </summary>
		/// <param name="radius">Metres from the axis.</param>
		/// <param name="belowBase">Metres below the cloud base.</param>
		/// <remarks>
		/// A parcel carried up past its condensation level condenses the same water for every metre it
		/// climbs, and so does one whose pressure the vortex drops by the same amount: the funnel holds
		/// water in proportion to this, as a cloud does to its height above its base, and takes light
		/// out as its two-thirds power (<see cref="AirColumn.ExtinctionCoefficient"/>).
		/// </remarks>
		public static float CondensationExcess(float radius, float belowBase, float coreRadius, float peakWind, float gravity)
		{
			return DeficitLift(radius, coreRadius, peakWind, gravity) - belowBase;
		}

		/// <summary>
		/// The radius of the condensation funnel this far below the cloud base, in metres: infinite at
		/// the base itself, where it becomes the cloud, and 0 below the funnel's tip.
		/// </summary>
		/// <param name="belowBase">Metres below the cloud base.</param>
		/// <param name="coreRadius">The vortex core's radius, where the wind peaks, in metres.</param>
		/// <param name="peakWind">The wind at the core's edge, m/s.</param>
		/// <param name="gravity">The world's surface gravity, m/s².</param>
		/// <remarks>
		/// Where <see cref="CondensationExcess"/> is zero. With d the lowering needed as a share of the
		/// most the vortex gives, (base − z)·g/V²: outside the core r = R/√(2d) while d ≤ ½; inside it
		/// r = R·√(2(1 − d)) while d ≤ 1. The two meet at the core's edge when d is ½.
		/// </remarks>
		public static float CondensationRadius(float belowBase, float coreRadius, float peakWind, float gravity)
		{
			if (belowBase <= 0f)
			{
				return float.PositiveInfinity;
			}
			float depth = FunnelDepth(peakWind, gravity);
			if (depth <= 1e-4f)
			{
				return 0f;
			}
			float share = belowBase / depth;
			if (share > 1f)
			{
				return 0f;
			}
			return share <= 0.5f
				? coreRadius / Mathf.Sqrt(2f * share)
				: coreRadius * Mathf.Sqrt(2f * (1f - share));
		}

		/// <summary>The wind turning round the axis at a radius, m/s.</summary>
		public static float WindAt(float radius, float coreRadius, float peakWind)
		{
			float core = Mathf.Max(0.01f, coreRadius);
			return radius < core ? peakWind * radius / core : peakWind * core / Mathf.Max(core, radius);
		}

		/// <summary>
		/// How fast the air at a radius goes round, radians a second: constant across the core, which
		/// turns as a solid body, and falling as one over the radius squared outside it.
		/// </summary>
		public static float AngularSpeed(float radius, float coreRadius, float peakWind)
		{
			float core = Mathf.Max(0.01f, coreRadius);
			return radius < core ? peakWind / core : peakWind * core / (radius * radius);
		}

		/// <summary>
		/// Which way it turns seen from above: +1 anticlockwise, −1 clockwise.
		/// </summary>
		/// <param name="cyclonic">
		/// A tornado spins with its parent storm's mesocyclone, which the turning of the world sets
		/// anticlockwise in the northern hemisphere and clockwise in the southern. A dust devil is too
		/// small and quick for the world's turning to matter and goes either way.
		/// </param>
		public static float Spin(float latitudeDegrees, uint seed, bool cyclonic)
		{
			if (cyclonic)
			{
				return latitudeDegrees >= 0f ? 1f : -1f;
			}
			return (seed & 1u) == 0u ? 1f : -1f;
		}

		/// <summary>
		/// How much of the ground a wind lifts, 0 to 1: nothing below the wind that starts it moving,
		/// all it can carry at twice that — the same measure the weather lifts dust by
		/// (<see cref="WeatherPhysics.Frame"/>).
		/// </summary>
		public static float Lifted(float wind, float threshold)
		{
			return Mathf.Clamp01((wind - threshold) / Mathf.Max(0.5f, threshold));
		}

		/// <summary>
		/// How far from the axis the vortex's wind lifts the ground, m: out to where it falls to the
		/// wind that starts the ground moving, and nowhere if even the core's edge is below it.
		/// </summary>
		public static float LiftingRadius(float coreRadius, float peakWind, float threshold)
		{
			if (peakWind <= threshold || threshold <= 0f)
			{
				return peakWind > threshold ? float.PositiveInfinity : 0f;
			}
			return Mathf.Max(0.01f, coreRadius) * peakWind / threshold;
		}

		/// <summary>
		/// How high a vortex throws what it lifts, m: V²/(2g), as high as the wind at the core's edge
		/// could carry it straight up — half the funnel's depth, so a strong one's debris climbs to meet
		/// its funnel and a weak one's stays in a whirl under it.
		/// </summary>
		public static float DebrisHeight(float peakWind, float gravity)
		{
			return 0.5f * FunnelDepth(peakWind, gravity);
		}

		/// <summary>
		/// How deep the layer is that feeds a tornado from the ground, m: the air under the wall cloud
		/// that its inflow sweeps in, about the lowest third of it.
		/// </summary>
		public static float InflowDepth(float wallCloudBase)
		{
			return Mathf.Clamp(wallCloudBase / 3f, 50f, 400f);
		}

		/// <summary>
		/// The swirl ratio: how fast a vortex spins for the air its updraught takes up through it,
		/// R·V / (2·h·w) for a core of radius R and wind V fed through an inflow h deep into an updraught
		/// climbing at w.
		/// </summary>
		/// <remarks>
		/// The one number that decides a vortex's structure, in the laboratory and in the field: under
		/// about 0.2 a single tight vortex; past it the core breaks down aloft and, by about 0.45, all
		/// the way to the ground, hollowing it round a sinking centre; past about 0.8 it splits into two
		/// vortices going round inside the parent, and more as it rises, up to six. A wide, strong core
		/// under a low base — a wedge — is the one that does it.
		/// </remarks>
		public static float SwirlRatio(float coreRadius, float peakWind, float updraft, float inflowDepth)
		{
			return Mathf.Max(0f, coreRadius) * Mathf.Max(0f, peakWind) / (2f * Mathf.Max(1f, inflowDepth) * Mathf.Max(0.5f, updraft));
		}

		/// <summary>How many vortices go round inside the core at a swirl ratio: none, or two to six.</summary>
		public static int SubVortices(float swirl)
		{
			if (swirl < MultipleVortexSwirl)
			{
				return 0;
			}
			return Mathf.Clamp(Mathf.FloorToInt(2f + (swirl - MultipleVortexSwirl) * 2.5f), 2, MostSubVortices);
		}

		/// <summary>
		/// How far the core's own sinking centre has hollowed the funnel, 0 to 1: nothing in a single
		/// tight vortex, all of it once the breakdown reaches the ground.
		/// </summary>
		public static float Hollow(float swirl)
		{
			return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.45f, swirl));
		}

		/// <summary>
		/// Where a vortex's top stands from its foot, m: the foot held back by the ground while the top
		/// is carried on with the storm.
		/// </summary>
		/// <param name="stormMotion">How the storm moves over the ground, m/s.</param>
		/// <param name="height">How tall the vortex stands, m.</param>
		/// <param name="axialUpdraft">How fast the air climbs its axis, m/s: the stiffer a vortex, the less it bends.</param>
		/// <remarks>
		/// A vortex line is carried by the air it is in. At its foot the air is held to the ground and
		/// the storm passes over it at the storm's own speed; at the top the air moves with the storm.
		/// Taking that difference as easing off evenly from the ground to the top, the line leans over
		/// by it for as long as the air takes to climb it: steep where it leaves the cloud and bent over
		/// near the ground, with the top (motion · height / 2w) ahead of the foot. A strong vortex's
		/// climbing axis stands up; a weak or dying one's lies over, which is a rope.
		/// </remarks>
		public static Vector2 TopOffset(Vector2 stormMotion, float height, float axialUpdraft)
		{
			return stormMotion * (0.5f * Mathf.Max(0f, height) / Mathf.Max(0.5f, axialUpdraft));
		}

		// ── Striations ──────────────────────────────────────────────────────
		//
		// A funnel's striations are its streamlines made visible. The air a tornado draws in is not all
		// one air: across the boundary layer it comes from, the rolls' updraughts are warmer and damper
		// than their downdraughts, so parcels side by side reach their dew point at different lifts.
		// Nothing mixes them once they are in the vortex — its spin damps the turbulence that would (the
		// same reason a funnel holds its water undiluted, FunnelExtinction) — so each keeps its own
		// condensation level all the way up its helix: a damp streamline condenses further out than a dry
		// one beside it, and the wall is ridged along the streamlines. That is what spotters are taught
		// a striation is: grooves in the cloud lying along the flow (NWS spotters' glossary), which is
		// why they wind round a funnel at the pitch its updraught and its wind give the air — the
		// corkscrew — and slide up it with the air. Hoecker (1960), tracking cloud tags on the Dallas
		// tornado's funnel, measured both: up to 76 m/s round and 67 m/s up.
		//
		// The first funnel took its ridges from the clouds' isotropic noise wound round the axis: blobs a
		// third of a core radius across, squashed by the helix's own shallow pitch to a few metres tall,
		// finer than a pixel at three kilometres — and its lighting read the funnel without them, so
		// even where they were drawn nothing shaded them. Now the bands are the inflow's moisture, as big
		// as its swing across the rolls (CondensationSwing), in octaves whose size is the funnel's own
		// (the only length a vortex has is its radius), each kept only while the pixel can hold it.

		/// <summary>
		/// The share of its vapour the air feeding a storm varies by across its boundary layer's rolls:
		/// Weckwerth, Wilson and Wakimoto (1996, CaPE) measured 1.5–2.5 g/kg of mixing ratio between the
		/// rolls' up- and downdraughts in Florida summer air holding about 17 — two in seventeen.
		/// </summary>
		public const float InflowVapourSwing = 0.12f;

		/// <summary>
		/// How much warmer the damper air across the rolls is, per kelvin of its dew point: the 0.5 K of
		/// potential temperature Weckwerth et al. measured alongside the 1.9 K of dew point their
		/// moisture was. The rolls' updraughts carry both off the same warm, damp ground, so the warmth
		/// takes back a quarter of what the moisture lowers the condensation level by.
		/// </summary>
		public const float InflowWarmthPerDew = 0.26f;

		/// <summary>A boundary layer's rolls, wavelength per depth: between two and six (Brown 1980; Stull 1988).</summary>
		public const float RollAspect = 3f;

		/// <summary>The octaves of bands drawn: 3, 7, 15 and 31 going round the funnel (<see cref="StriationStarts"/>).</summary>
		public const int StriationOctaves = 4;

		/// <summary>
		/// How sharp a band's cliff is (<see cref="RampCliff"/>): the front across a band is far thinner
		/// than anything drawn, and 0.9 makes it a seventh of the band — as sharp as the march's fine
		/// strides resolve on the widest bands, blurred on the finest.
		/// </summary>
		public const float BandCliff = 0.9f;

		/// <summary>
		/// How many bands an octave winds round the funnel: 3, 7, 15, 31 — each about twice the last,
		/// odd so no two line up round it. Fewer than three is the funnel lopsided, which is its sway.
		/// </summary>
		public static int StriationStarts(int octave) => (1 << (Mathf.Clamp(octave, 0, 8) + 2)) - 1;

		/// <summary>
		/// How far the inflow's moisture swings its condensation level either way across its rolls, m.
		/// </summary>
		/// <remarks>
		/// The rolls' share of vapour is a dew-point swing through Clausius–Clapeyron — δTd = f·Rv·Td²/L
		/// for a share f — which is 1.9 K at Weckwerth's 17 g/kg and barely changes with how damp the
		/// air is, only with its dew point's square; less the
		/// warmth that comes with it, it is a swing in the dew-point spread, and a kelvin of spread is
		/// 1/(Γd − Γdew) of lift — the old 125 m per degree on our own world, worked out for this one.
		/// About ±90 m on Earth; more on a world of lower gravity, whose air cools more slowly as it
		/// rises. Against a funnel V²/g deep it is most of a weak tornado's (a rope, boldly banded) and a
		/// tenth of a violent one's (a wedge, whose wall is barely grooved).
		/// </remarks>
		public static float CondensationSwing(in AirColumn column, in PlanetAir planet)
		{
			Condensate c = planet.Condensate;
			float td = Mathf.Max(1f, column.DewPointKelvin);
			float dewSwing = InflowVapourSwing * AirPhysics.VapourGasConstant(c) * td * td / Mathf.Max(1f, AirPhysics.LatentHeat(c));
			float spreadSwing = dewSwing * (1f - InflowWarmthPerDew);
			float dewLapse = AirPhysics.DewPointLapse(planet.Gravity, column.SurfaceKelvin, td, planet.GasConstant, c);
			float closing = Mathf.Max(column.DryLapse - dewLapse, 0.1f * column.DryLapse);
			return closing > 0f ? 0.5f * spreadSwing / closing : 0f;
		}

		/// <summary>
		/// The wavelength of the rolls the inflow comes through, m: <see cref="RollAspect"/> times the
		/// mixed layer's depth, whose top on a day that raises storms is the cloud base — and never less
		/// than a couple of hundred metres of layer.
		/// </summary>
		public static float RollWavelength(in AirColumn column)
		{
			return RollAspect * Mathf.Max(200f, column.Base);
		}

		/// <summary>
		/// How far one octave's bands swing the condensation level, m: the rolls' whole swing scaled down
		/// to the band's size as the cube root of it, as a scalar's differences are through the inertial
		/// range (Obukhov 1949; Corrsin 1951). The inflow's sectors keep their angle as the air closes
		/// in, so a band's size in the inflow is taken as its spacing round the wall — the least it was.
		/// </summary>
		public static float BandSwing(float swing, float radius, int starts, float rollWavelength)
		{
			float size = 2f * Mathf.PI * Mathf.Max(0f, radius) / Mathf.Max(1, starts);
			return swing * Mathf.Min(1f, Mathf.Pow(size / Mathf.Max(1f, rollWavelength), 1f / 3f));
		}

		/// <summary>
		/// How fast the streamlines on the funnel's wall turn as they climb, radians per metre of height:
		/// ω/w at the wall's radius this far below the base — the core's own V/R once the wall is inside
		/// the core, and slower above, where the wall stands outside it and the wind has fallen as 1/r.
		/// Nothing above the base, where the funnel has become the cloud.
		/// </summary>
		/// <remarks>
		/// With the wall's radius <see cref="CondensationRadius"/>, ω there is 2V·s/R above the core's
		/// edge (s the share of V²/g below the base) and V/R below it. The band tilts from level by
		/// atan(1/(rate·r)) — atan(w/v), the corkscrew's pitch — and climbs 2π/rate for each turn.
		/// </remarks>
		public static float HelixRate(float belowBase, float coreRadius, float peakWind, float gravity, float axialUpdraft)
		{
			float depth = FunnelDepth(peakWind, gravity);
			if (depth <= 1e-4f || belowBase <= 0f)
			{
				return 0f;
			}
			float share = belowBase / depth;
			float core = peakWind / (Mathf.Max(0.01f, coreRadius) * Mathf.Max(0.5f, axialUpdraft));
			return share <= 0.5f ? core * 2f * share : core;
		}

		/// <summary>
		/// How far round the wall's streamlines have turned between the cloud base and this far below it,
		/// radians: the integral of <see cref="HelixRate"/> up the wall.
		/// </summary>
		/// <remarks>
		/// <para>
		/// With s the share of V²/g below the base, it is (V²/g)·(V/(R·w)) times s² down to the core's
		/// edge and s − ¼ below it, meeting at a quarter. A band is where the angle round the axis plus
		/// this is constant, so it follows the air up the wall at the wall's own pitch.
		/// </para>
		/// <para>
		/// <b>The winding trap.</b> It is a function of the height alone: every radius at one height is
		/// given the wall's turn there. Turning each radius at its own ω — the true flow off the wall —
		/// wound the pattern round the axis a little more at every height, and the bands grew finer
		/// than a pixel toward the top of a tall funnel; the first funnel wound them in time and lost
		/// them within minutes. Only the wall is ever seen, and the wall is what this follows exactly.
		/// </para>
		/// </remarks>
		public static float HelixTurn(float belowBase, float coreRadius, float peakWind, float gravity, float axialUpdraft)
		{
			float depth = FunnelDepth(peakWind, gravity);
			if (depth <= 1e-4f || belowBase <= 0f)
			{
				return 0f;
			}
			float share = belowBase / depth;
			float wound = share <= 0.5f ? share * share : share - 0.25f;
			return depth * peakWind / (Mathf.Max(0.01f, coreRadius) * Mathf.Max(0.5f, axialUpdraft)) * wound;
		}

		/// <summary>How far a band on the wall tilts from level, radians: atan(w/v) at the wall's radius.</summary>
		public static float HelixAngle(float belowBase, float coreRadius, float peakWind, float gravity, float axialUpdraft)
		{
			float radius = CondensationRadius(belowBase, coreRadius, peakWind, gravity);
			float rate = HelixRate(belowBase, coreRadius, peakWind, gravity, axialUpdraft);
			return float.IsInfinity(radius) ? 0.5f * Mathf.PI : Mathf.Atan2(1f, rate * radius);
		}

		/// <summary>
		/// How far one band is from the next across them, m: for n going round at a radius, 2π over n
		/// times the steepness of the band's phase — 1/r round the axis and the helix's rate up it.
		/// </summary>
		public static float BandSpacing(int starts, float radius, float helixRate)
		{
			float r = Mathf.Max(0.01f, radius);
			return 2f * Mathf.PI / (Mathf.Max(1, starts) * Mathf.Sqrt(1f / (r * r) + helixRate * helixRate));
		}

		/// <summary>
		/// How much of an octave of bands is drawn at a pixel's footprint, 0 to 1: all of it while a band
		/// is four pixels across, none by two — where a sampled stripe stops being a stripe and turns to
		/// grey noise. What a far camera loses is the finest octaves; the coarse ones stay.
		/// </summary>
		public static float BandWeight(float spacing, float footprint)
		{
			return Mathf.Clamp01(0.5f * spacing / Mathf.Max(1e-4f, footprint) - 1f);
		}

		/// <summary>
		/// A band's profile across it, −1 to 1: a long ramp and a sharp cliff, as a scalar drawn out by a
		/// shear lies — ramps of the eddies' size ending in fronts far thinner (Sreenivasan 1991; Warhaft
		/// 2000). So a banded funnel is stacked ledges, not a sine's soft corrugation.
		/// </summary>
		/// <remarks>The phase of 1 − k·e^{iφ}, over its peak asin(k).</remarks>
		public static float RampCliff(float phase)
		{
			return Mathf.Atan2(BandCliff * Mathf.Sin(phase), 1f - BandCliff * Mathf.Cos(phase)) / Mathf.Asin(BandCliff);
		}

		/// <summary>
		/// How steeply a band's profile climbs at a phase, per radian: <see cref="RampCliff"/>'s derivative —
		/// a little under ½ down the long ramp, about 8 up the cliff.
		/// </summary>
		/// <remarks>d/dφ of arg(1 − k·e^{iφ}) is k(cos φ − k)/(1 − 2k·cos φ + k²).</remarks>
		public static float RampCliffSlope(float phase)
		{
			float c = Mathf.Cos(phase);
			return BandCliff * (c - BandCliff) / (1f - 2f * BandCliff * c + BandCliff * BandCliff) / Mathf.Asin(BandCliff);
		}

		// ── Where the bands survive ─────────────────────────────────────────
		//
		// The first bands were added to the condensation excess everywhere under the base. Where the
		// funnel meets the wall cloud the excess changes by barely a metre of lift for every metre out —
		// the pressure deficit there falls as 1/r² and is almost flat — so ±90 m of swing (±160 m on
		// Arthis) carried every damp sector's edge out to the flare's limit and every dry one's in: the
		// top of the funnel burst into straight radial spokes, cut off square where the march's flare
		// volume ended. That was not a sampling fault. It was the model claiming the inflow's moisture
		// stays sorted into bands out where nothing holds it so.
		//
		// What keeps a band a band is the same thing that keeps the funnel's water undiluted
		// (FunnelExtinction): the spin damps the turbulence that would mix a streamline with its
		// neighbour. It does so only where the flow's angular momentum rises outward — Rayleigh's
		// criterion — and by how much against the shear that would overturn it: the rotational
		// Richardson number B = 2Ω(2Ω + r·dΩ/dr)/(r·dΩ/dr)², which plays the part stratification's
		// Richardson number does (Bradshaw 1969), with the same critical ¼ below which the shear can mix
		// (Miles 1961; Howard 1961). A Rankine vortex has no answer — infinite inside its core, zero
		// outside — because its corner at R is an idealisation; the viscous core it stands in for (Burgers
		// 1948; Rott 1958), whose peak wind sits at the same R, gives B = 2 at R, ¼ at 1.50 R and 1/28 at
		// 2 R, where its circulation is 99.3% of its whole and the flow outside is the irrotational vortex
		// that cannot hold anything apart. So the bands are whole to 1.5 R and gone by 2 R
		// (<see cref="StriationsKept"/>): the funnel's wall is banded from the tip up to where it has
		// flared to 1.5 R — the top fifth of V²/g — and fades into the smooth underside of the wall
		// cloud above, as photographs show it; and the bands can never carry the funnel's edge past 2 R.

		/// <summary>The Burgers–Rott vortex's α: its wind peaks at r = R when the exponent is α·r²/R².</summary>
		public const float BurgersRottAlpha = 1.25643f;

		/// <summary>Out to here, in core radii, the bands are whole: the rotational Richardson number is past ¼.</summary>
		public const float StriationsWholeWithin = 1.5f;

		/// <summary>From here, in core radii, there are none: the vortex has its whole circulation, and no vorticity is left to hold them.</summary>
		public const float StriationsGoneBy = 2f;

		/// <summary>
		/// The rotational Richardson number of a Burgers–Rott vortex at a radius, in core radii: how far its
		/// spin holds the flow against the shear that would mix it. Past ¼ the shear cannot.
		/// </summary>
		/// <remarks>B = 2Ω(2Ω + r·Ω′)/(r·Ω′)² with Ω = (1 − e^{−αx²})/x² in units of the core's own turning.</remarks>
		public static float RotationalRichardson(float coreRadii)
		{
			float x = Mathf.Max(1e-3f, coreRadii);
			float a = BurgersRottAlpha * x * x;
			float e = Mathf.Exp(-a);
			float omega = (1f - e) / (x * x);
			// r·dΩ/dr, analytically: 2αe − 2(1 − e)/x². And 2Ω + r·dΩ/dr — the vorticity — is then 2αe
			// exactly, taken so rather than as the difference of two nearly equal numbers far out.
			float shear = 2f * BurgersRottAlpha * e - 2f * (1f - e) / (x * x);
			float vorticity = 2f * BurgersRottAlpha * e;
			if (Mathf.Abs(shear) < 1e-9f)
			{
				return float.PositiveInfinity;
			}
			return 2f * omega * vorticity / (shear * shear);
		}

		/// <summary>
		/// How much of the bands survive at a radius, 0 to 1: all while the spin holds the streamlines
		/// apart (<see cref="StriationsWholeWithin"/>), none where the vortex has its whole circulation
		/// (<see cref="StriationsGoneBy"/>).
		/// </summary>
		public static float StriationsKept(float radius, float coreRadius)
		{
			float x = Mathf.Abs(radius) / Mathf.Max(0.01f, coreRadius);
			return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StriationsWholeWithin, StriationsGoneBy, x));
		}

		/// <summary>
		/// How fast the lift the vortex's low pressure is worth changes outward, m of lift per m of radius
		/// (negative): −V²/g·r/R² in the core, −V²/g·R²/r³ outside it. A band's swing moves the funnel's wall
		/// by its lift over this — a few metres at the core's edge, without bound where the funnel flares.
		/// </summary>
		public static float DeficitSlope(float radius, float coreRadius, float peakWind, float gravity)
		{
			float depth = FunnelDepth(peakWind, gravity);
			float core = Mathf.Max(0.01f, coreRadius);
			float x = Mathf.Max(1e-3f, Mathf.Abs(radius) / core);
			return x < 1f ? -depth * x / core : -depth / (core * x * x * x);
		}

		// ── How a band is lit ───────────────────────────────────────────────
		//
		// A funnel's wall is tens of optical depths thick within a few tens of metres, so it returns light
		// the way any thick cloud does: from the skin the light first enters, in proportion to the light
		// falling on that skin (Chandrasekhar 1950: the light a thick, conservatively scattering layer
		// reflects goes as the cosine of its incidence). So a ledge's faces are lit as faces. The first
		// banded funnel read its bands' light only from which way the cloud lay toward the light — a
		// groove under a ledge was darker, but a ledge's top, its underside and the wall between were all
		// lit alike, and under a storm, where a tornado is lit by the sky alone, it came to 20% rms in
		// our port of the march and nothing at all on screen. The faces' tilt is the gradient of the
		// condensation excess, whose every term is already known where the march samples it — no more
		// taps. The sun falls on them as Lambert's cosine; the sky under a storm is not the even dome
		// a cosine law assumes but the ring below the storm base's edge (as VortexPresenter.StormLight
		// works out), so a face turned down to the ground is dark, one turned up to the storm's base
		// barely brighter than an upright one, and a ledge's cliff — steep, short, turned up or down —
		// is a dark line along it: a groove.

		/// <summary>
		/// How much of a band's cliff a pixel must span to shade it: a cliff is acos(k)/π of its band — a
		/// seventh — and its shading is drawn fully once it is two pixels across and not at all at one,
		/// where the line it draws is finer than the pixel and would only sparkle.
		/// </summary>
		public static float CliffWeight(float spacing, float footprint)
		{
			float cliff = spacing * Mathf.Acos(BandCliff) / Mathf.PI;
			return Mathf.Clamp01(cliff / Mathf.Max(1e-4f, footprint) - 1f);
		}

		/// <summary>
		/// The sky a face gets from a ring of it at one elevation, per unit of the ring's radiance: the
		/// integral round the horizon of the cosine between the face's normal and the sky, where it faces
		/// the sky at all.
		/// </summary>
		/// <param name="normalElevation">How far the face's normal stands above level, radians.</param>
		/// <param name="skyElevation">The ring's elevation, radians.</param>
		public static float SkyRing(float normalElevation, float skyElevation)
		{
			float a = Mathf.Cos(normalElevation) * Mathf.Cos(skyElevation);
			float b = Mathf.Sin(normalElevation) * Mathf.Sin(skyElevation);
			if (b >= a)
			{
				return 2f * Mathf.PI * b;
			}
			if (b <= -a)
			{
				return 0f;
			}
			return 2f * (Mathf.Sqrt(a * a - b * b) + b * Mathf.Acos(Mathf.Clamp(-b / Mathf.Max(1e-6f, a), -1f, 1f)));
		}

		/// <summary>
		/// The light a face of a tornado gets from the sky it sees, against an upright face's: the sky
		/// below the storm base's edge — a band from the horizon up to <paramref name="skyEdge"/> — and the
		/// ground's light, which faces turned down see.
		/// </summary>
		/// <param name="normalElevation">How far the face's normal stands above level, radians.</param>
		/// <param name="skyEdge">How high the sky reaches round it, radians: the storm base's edge, π/2 in the open.</param>
		/// <param name="ground">The ground's share of what an upright face gets.</param>
		/// <remarks>
		/// The band is integrated at its quarter and three-quarter heights, each weighted by the cosine of
		/// its elevation (the band's solid angle): under an even open sky that gives a face turned up 2.2
		/// times an upright one's light, against the exact 2. A face turned up under a storm looks at the
		/// dark base and gets little more than an upright one; turned down, only the ground's light.
		/// </remarks>
		public static float SkyOnFace(float normalElevation, float skyEdge, float ground)
		{
			float low = 0.25f * skyEdge, high = 0.75f * skyEdge;
			float sky = Mathf.Cos(low) * SkyRing(normalElevation, low) + Mathf.Cos(high) * SkyRing(normalElevation, high);
			float upright = 2f * (Mathf.Cos(low) * Mathf.Cos(low) + Mathf.Cos(high) * Mathf.Cos(high));
			float g = Mathf.Clamp01(ground);
			return (1f - g) * sky / Mathf.Max(1e-6f, upright) + g * (1f - Mathf.Sin(normalElevation));
		}

		/// <summary>A dust devil's wind at its core's edge, m/s: ten to twenty, however hot the ground.</summary>
		public const float DustDevilWind = 16f;

		/// <summary>
		/// The wind that raises dirt, litter and leaves off ground held by what grows on it, m/s.
		/// </summary>
		/// <remarks>
		/// Bare soil starts moving at about ten metres a second; the plants on it take about half the
		/// wind's stress on themselves (the drag partition), so it takes about twice that — a strong
		/// gale — before a vortex raises its first dirt from grassland or crops. Loose ground lifts at
		/// its own grain's wind (<see cref="WeatherPhysics.LiftingWind"/>), far lower: a weak tornado
		/// over farmland raises a thin whirl of dirt, the same one over a desert a column of dust.
		/// </remarks>
		public const float StripsBoundGround = 20f;

		/// <summary>
		/// The wind that tears the sea's surface into the air, m/s: gale force, where the wave crests
		/// break into spindrift — the spray ring at a waterspout's foot.
		/// </summary>
		public const float TearsTheSea = 17f;

		/// <summary>
		/// Everything seen of a vortex at one moment of its life, from its air and its storm: how big,
		/// how strong, how it leans and wanders, what its core has broken into, and what it lifts.
		/// </summary>
		public struct Form
		{
			/// <summary>False when there is nothing to see.</summary>
			public bool Valid;
			/// <summary>Whether it condenses a funnel: a tornado does, a dust devil's dry air does not.</summary>
			public bool Condenses;
			/// <summary>The core's radius, m.</summary>
			public float CoreRadius;
			/// <summary>The wind at the core's edge, m/s.</summary>
			public float PeakWind;
			/// <summary>How tall it stands, m: from the ground to the wall cloud's base, or a dust devil's height.</summary>
			public float Top;
			/// <summary>How far under the top its condensation funnel reaches, m: V²/g.</summary>
			public float FunnelDepth;
			/// <summary>How fast the air climbs its axis, m/s.</summary>
			public float AxialUpdraft;
			/// <summary>Where its top stands from its foot, m.</summary>
			public Vector2 TopOffset;
			/// <summary>How far its axis wanders, m.</summary>
			public float Sway;
			/// <summary>How far it has roped out, 0 (grown) to 1 (gone).</summary>
			public float Rope;
			/// <summary>Its swirl ratio (<see cref="SwirlRatio"/>).</summary>
			public float Swirl;
			/// <summary>How many sub-vortices go round inside it: 0, or 2 to 6.</summary>
			public int SubVortices;
			/// <summary>How hollow its core is, 0 to 1.</summary>
			public float Hollow;
			/// <summary>How much of its ground its wind lifts, 0 to 1.</summary>
			public float Lifted;
			/// <summary>How high what it lifts stands, m.</summary>
			public float DebrisHeight;
			/// <summary>How far what it lifts reaches from the axis at the ground, and at its top, in core radii.</summary>
			public float DebrisReachGround, DebrisReachTop;
			/// <summary>How fast what it lifts thins upward: a power of the height left.</summary>
			public float DebrisThinning;

			/// <summary>Whether its funnel's tip hangs clear of the ground.</summary>
			public bool Aloft => !Condenses || FunnelDepth < Top;

			/// <summary>How wide its condensation funnel is at a height above the ground, m: 0 where there is none.</summary>
			public float FunnelWidthAt(float height, float gravity)
			{
				return Condenses ? 2f * CondensationRadius(Top - height, CoreRadius, PeakWind, gravity) : 0f;
			}
		}

		/// <summary>
		/// A tornado at one moment of its storm's life.
		/// </summary>
		/// <param name="cape">The CAPE of the air its storm feeds on: its speed limit.</param>
		/// <param name="envelope">Where its storm is in its life, 0 to 1 (<see cref="StormCell.EnvelopeAt"/>).</param>
		/// <param name="peakIntensity">The storm's own strength, 0 to 1.</param>
		/// <param name="decaying">Past its prime: roping out.</param>
		/// <param name="coreRadius">The cell's core radius when grown, m.</param>
		/// <param name="wallCloudBase">The wall cloud's base above the ground, m (<see cref="StormAnatomy.WallCloudBase"/>).</param>
		/// <param name="wallCloudRadius">The wall cloud's radius, m: its top stays under it.</param>
		/// <param name="stormMotion">How the storm moves over the ground, m/s.</param>
		/// <param name="updraft">The storm's rotating updraught, m/s.</param>
		/// <param name="gravity">The world's surface gravity, m/s².</param>
		/// <param name="liftingWind">The wind that starts its ground moving, m/s.</param>
		/// <remarks>
		/// <para>
		/// <b>Growing</b>, its wind rises with the storm, so its funnel lowers from the wall cloud as V²/g
		/// grows — the debris whirl on the ground before the funnel reaches it, since the ground lifts
		/// at a far gentler wind than it takes to condense the air all the way down — and touches down
		/// once V²/g passes the wall cloud's height.
		/// </para>
		/// <para>
		/// <b>Roping out</b>, the storm's outflow wraps round it and cuts off its inflow, and drags its
		/// foot away: its circulation (2πRV) runs down, but what is left of it is stretched, and a
		/// stretched vortex spins up as it narrows, keeping its angular momentum. So its core contracts
		/// with the envelope, to a fifth of its width, while its wind falls only as the envelope's
		/// fourth root: its funnel stays long — V²/g — as it grows thin, and lifts off the ground only at
		/// the very end. With its updraught cut off, the jet up its axis dies with the envelope, so the
		/// ground's drag bends it further over (<see cref="TopOffset"/>), and it wanders more — the thin,
		/// leaning, sinuous rope a tornado's last minutes are.
		/// </para>
		/// </remarks>
		public static Form Tornado(float cape, float envelope, float peakIntensity, bool decaying, float coreRadius,
			float wallCloudBase, float wallCloudRadius, Vector2 stormMotion, float updraft, float gravity, float liftingWind)
		{
			var form = new Form();
			float life = Mathf.Clamp01(envelope);
			float peak = Mathf.Clamp01(peakIntensity);
			float limit = PeakWind(cape, 1f);
			form.Rope = decaying ? 1f - life : 0f;
			// Stretched as it dies, it spins up as it narrows: its wind falls far more slowly than its core.
			form.PeakWind = decaying ? limit * peak * Mathf.Sqrt(Mathf.Sqrt(life)) : limit * peak * life;
			form.Valid = form.PeakWind > 0.5f;
			form.Condenses = true;
			form.CoreRadius = Mathf.Max(5f, coreRadius) * (1f - 0.8f * form.Rope);
			form.Top = Mathf.Max(0f, wallCloudBase);
			form.FunnelDepth = FunnelDepth(form.PeakWind, gravity);
			// The jet up its axis is the storm's updraught feeding it, which grows and dies with it: cut
			// off by the outflow as it ropes out, faster than its spin runs down.
			form.AxialUpdraft = Mathf.Max(1f, updraft * peak * life);
			// Never so far over that its top leaves the wall cloud, nor lying flatter than about fifty degrees.
			float most = Mathf.Min(1.2f * form.Top, 0.8f * Mathf.Max(wallCloudRadius, form.CoreRadius));
			form.TopOffset = Vector2.ClampMagnitude(TopOffset(stormMotion, form.Top, form.AxialUpdraft), most);
			// A grown vortex's axis barely wanders; a rope's wanders across its whole height.
			form.Sway = 0.2f * form.CoreRadius + 0.15f * form.Rope * form.Top;
			form.Swirl = SwirlRatio(form.CoreRadius, form.PeakWind, updraft, InflowDepth(form.Top));
			form.SubVortices = SubVortices(form.Swirl);
			form.Hollow = Hollow(form.Swirl);
			form.Lifted = Lifted(form.PeakWind, liftingWind);
			// As high as it throws it, and no higher than the cloud it is carried into.
			form.DebrisHeight = Mathf.Clamp(DebrisHeight(form.PeakWind, gravity), 10f, Mathf.Max(10f, form.Top));
			// A skirt as wide as the wind lifts the ground, gathered up round the core's edge by the inflow.
			float reach = LiftingRadius(form.CoreRadius, form.PeakWind, liftingWind) / form.CoreRadius;
			form.DebrisReachGround = Mathf.Clamp(reach, 1.5f, 3f);
			form.DebrisReachTop = 1.3f;
			form.DebrisThinning = 2f;
			return form;
		}

		/// <summary>
		/// A dust devil at one moment of its life: a whirl of dry, sun-heated air, which condenses
		/// nothing, as tall as its plume and spreading as it climbs.
		/// </summary>
		/// <param name="height">How tall its plume stands, m.</param>
		public static Form DustDevil(float envelope, float peakIntensity, float coreRadius, float height, Vector2 stormMotion, float liftingWind)
		{
			var form = new Form();
			float grown = Mathf.Clamp01(envelope) * Mathf.Clamp01(peakIntensity);
			form.PeakWind = DustDevilWind * grown;
			form.CoreRadius = Mathf.Max(0.5f, coreRadius);
			form.Top = Mathf.Max(20f, height);
			// Its plume climbs at about half its spin.
			form.AxialUpdraft = Mathf.Max(1f, 0.5f * form.PeakWind);
			form.TopOffset = Vector2.ClampMagnitude(TopOffset(stormMotion, form.Top, form.AxialUpdraft), 0.3f * form.Top);
			form.Sway = 0.1f * form.Top;
			form.Lifted = Lifted(form.PeakWind, liftingWind);
			form.Valid = form.Lifted > 0.001f;
			form.DebrisHeight = form.Top;
			form.DebrisReachGround = 1.2f;
			form.DebrisReachTop = 3.5f;
			form.DebrisThinning = 1.5f;
			return form;
		}
	}
}
