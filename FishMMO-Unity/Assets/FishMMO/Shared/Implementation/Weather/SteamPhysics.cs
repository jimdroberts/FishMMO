using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Steam: water vapour off something warmer than the air it rises into, seen where the two mix — the
	/// sea smoking on an arctic morning, a lake steaming in October, a fumarole's plume, a geyser's cloud.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Mixing fog.</b> The air right at a water surface is saturated at the water's temperature. Mixed
	/// with the colder air above it, the blend's temperature and vapour pressure both fall on the straight
	/// line between the two (by mass, to within a fraction of a per cent at these pressures), while the
	/// saturation curve under that line is convex (Clausius–Clapeyron). Where the line rises above the
	/// curve, the blend holds more vapour than it can and the excess condenses: steam fog. Where it never
	/// does, the water only evaporates unseen. That is the whole criterion (Saunders 1964, "The
	/// observation and forecasting of steam fog"; Bohren and Albrecht, Atmospheric Thermodynamics §5) and
	/// it gives the forecasters' rule of itself: at 0–10 °C and 80 % relative humidity the line first
	/// crosses the curve when the water is 9–9.5 K warmer than the air, at 60 % only past 12 K, at 90 %
	/// from 7 K; in air of −30 °C, where the curve bends hardest, from 7 K at 80 %, and the blend past the
	/// line condenses more for every kelvin beyond — which is why arctic sea smoke is the commonest fog
	/// over a winter sea. Dry air raises the onset; it does not lower it (what dry air does do is
	/// evaporate a plume sooner, below).
	/// </para>
	/// <para>
	/// <b>Plumes.</b> A vent's steam is the same blend, entered from the other end: saturated air at
	/// boiling, diluted with ambient air as the plume entrains it. It is visible while the source's share
	/// of the blend is above the lower crossing of the line with the curve (<see cref="VisibleDilution"/>)
	/// and evaporates once dilution takes it below. A buoyant plume from a small source dilutes as the
	/// five-thirds power of height (its volume flux grows as z^5/3: Morton, Taylor and Turner 1956), so
	/// the visible length is a virtual source length times that share to the −3/5 — about nine source
	/// lengths in a dry summer afternoon, a hundred and forty in air of −20 °C. Yellowstone's fumaroles
	/// are wisps in July and columns hundreds of metres tall in January for exactly this reason.
	/// </para>
	/// <para>
	/// The saturation curve is the weather's own (<see cref="AirPhysics.SaturationPressure"/>), read back
	/// by its exact inverse, so a dew point the weather hands in comes back as the vapour pressure it
	/// meant. Plain arithmetic on plain inputs: the server and every client agree on it, and none of it
	/// goes on the wire.
	/// </para>
	/// </remarks>
	public static class SteamPhysics
	{
		private const float Kelvin = 273.15f;

		/// <summary>The vapour pressure over sea water against fresh water: Raoult's law at a salinity of 35 g/kg.</summary>
		public const float SeaVapourShare = 0.98f;
		/// <summary>The dew-point excess (K) of the most supersaturated blend at which steam fog is at its thickest.</summary>
		public const float FullExcess = 2f;
		/// <summary>The least excess counted as fog at all, K: the line merely touching the curve is not steam.</summary>
		public const float OnsetExcess = 0.02f;
		/// <summary>The longest a plume is drawn visible, m: past this the air above a few hundred metres is another air.</summary>
		public const float MaxVisibleMetres = 400f;
		/// <summary>The shallowest and deepest a steam fog stands, m (FishMist.hlsl draws to 40).</summary>
		public const float ShallowFog = 2f, DeepFog = 30f;
		/// <summary>The excess (K) at which steam fog stands at its deepest: water 25–30 K warmer than the air.</summary>
		public const float DeepExcess = 6f;

		// ── The saturation curve ────────────────────────────────────────

		/// <summary>Water's saturation vapour pressure over liquid at a temperature, Pa (liquid even below 0 °C: fog drops supercool).</summary>
		public static float SaturationPressure(float celsius)
		{
			return AirPhysics.SaturationPressure(celsius + Kelvin, Condensate.Water);
		}

		/// <summary>The dew point of a vapour pressure, °C: <see cref="SaturationPressure"/> read the other way, over the whole range.</summary>
		public static float DewPointOf(float pascals)
		{
			float t0 = AirPhysics.FreezingKelvin(Condensate.Water);
			float e0 = AirPhysics.SaturationPressure(t0, Condensate.Water);
			float inverse = 1f / t0 - AirPhysics.VapourGasConstant(Condensate.Water) / AirPhysics.LatentHeat(Condensate.Water)
				* Mathf.Log(Mathf.Max(1e-3f, pascals) / e0);
			return (inverse > 1e-6f ? 1f / inverse : float.MaxValue) - Kelvin;
		}

		// ── Mixing ──────────────────────────────────────────────────────

		/// <summary>
		/// How far a blend is past saturation, K: its dew point less its temperature, for a share
		/// <paramref name="sourceShare"/> of air saturated off the source and the rest ambient. Positive
		/// means drops.
		/// </summary>
		public static float MixExcess(float sourceC, float sourcePa, float airC, float airPa, float sourceShare)
		{
			float f = Mathf.Clamp01(sourceShare);
			float t = f * sourceC + (1f - f) * airC;
			float e = f * sourcePa + (1f - f) * airPa;
			return DewPointOf(e) - t;
		}

		/// <summary>
		/// The most supersaturated any blend of the source's air and the ambient gets, K, and the source's
		/// share in it. At or under zero, mixing never makes a drop.
		/// </summary>
		/// <remarks>
		/// Read along a grid: forty points spaced evenly in the logarithm of the share from a millionth up
		/// (where a hot plume crosses), and nineteen evenly across the middle (where steam fog peaks). The
		/// excess is smooth and flat at its top, so the grid misses it by hundredths of a kelvin. The pure
		/// source itself (share 1) is left out: it is saturated by construction, and its zero is not fog.
		/// </remarks>
		public static float MostExcess(float sourceC, float sourcePa, float airC, float airPa, out float atShare)
		{
			float best = float.NegativeInfinity;
			atShare = 0f;
			for (int i = 0; i < 40; i++)
			{
				float f = Mathf.Pow(10f, -6f + 6f * i / 40f);
				float x = MixExcess(sourceC, sourcePa, airC, airPa, f);
				if (x > best)
				{
					best = x;
					atShare = f;
				}
			}
			for (int i = 1; i < 20; i++)
			{
				float f = 0.05f * i;
				float x = MixExcess(sourceC, sourcePa, airC, airPa, f);
				if (x > best)
				{
					best = x;
					atShare = f;
				}
			}
			return best;
		}

		/// <summary>
		/// The share of source air below which a plume's blend is no longer saturated: it is visible while
		/// diluted less than this, and has evaporated once diluted more. 1 when no blend ever saturates
		/// (nothing to see); a millionth when the ambient air is itself saturated (it never clears).
		/// </summary>
		public static float VisibleDilution(float sourceC, float sourcePa, float airC, float airPa)
		{
			float most = MostExcess(sourceC, sourcePa, airC, airPa, out float peak);
			if (most <= OnsetExcess)
			{
				return 1f;
			}
			const float floor = 1e-6f;
			if (MixExcess(sourceC, sourcePa, airC, airPa, floor) > 0f)
			{
				return floor;
			}
			// The lower crossing, by bisection in the logarithm of the share.
			float lo = floor, hi = peak;
			for (int i = 0; i < 32; i++)
			{
				float mid = Mathf.Sqrt(lo * hi);
				if (MixExcess(sourceC, sourcePa, airC, airPa, mid) > 0f)
				{
					hi = mid;
				}
				else
				{
					lo = mid;
				}
			}
			return hi;
		}

		/// <summary>
		/// How long a plume stays visible, m along it: its virtual source length times the dilution it
		/// can take to the −3/5 (a point source's volume flux grows as height to the 5/3), at most
		/// <see cref="MaxVisibleMetres"/>. Zero when it is never visible.
		/// </summary>
		public static float VisibleLength(float virtualSourceMetres, float visibleDilution)
		{
			if (visibleDilution >= 1f || virtualSourceMetres <= 0f)
			{
				return 0f;
			}
			return Mathf.Min(MaxVisibleMetres, virtualSourceMetres * Mathf.Pow(Mathf.Max(1e-6f, visibleDilution), -0.6f));
		}

		/// <summary>
		/// The same from temperatures: a source saturated at <paramref name="sourceC"/> (times
		/// <paramref name="vapourShare"/>, for salt) rising into air at <paramref name="airC"/> with dew
		/// point <paramref name="dewPointC"/>.
		/// </summary>
		public static float VisibleLength(float virtualSourceMetres, float sourceC, float airC, float dewPointC, float vapourShare = 1f)
		{
			float dilution = VisibleDilution(sourceC, vapourShare * SaturationPressure(sourceC), airC, SaturationPressure(Mathf.Min(dewPointC, airC)));
			return VisibleLength(virtualSourceMetres, dilution);
		}

		/// <summary>Where water boils under a pressure, °C, on the same curve: about 95 °C at 1.5 km, at sea level a little under 100.</summary>
		public static float BoilingC(float pascals)
		{
			return DewPointOf(Mathf.Max(1000f, pascals));
		}

		// ── Steam fog over open water ───────────────────────────────────

		/// <summary>
		/// The dew-point excess of the most supersaturated blend of the air over a water surface with the
		/// ambient, K (<see cref="MostExcess"/>): positive is steam fog.
		/// </summary>
		/// <param name="waterC">The water surface, °C.</param>
		/// <param name="airC">The air a few metres up, °C.</param>
		/// <param name="dewPointC">That air's dew point, °C.</param>
		/// <param name="salty">Sea water: its vapour pressure is <see cref="SeaVapourShare"/> of fresh water's.</param>
		public static float SteamFogExcess(float waterC, float airC, float dewPointC, bool salty)
		{
			if (waterC <= airC)
			{
				// Water no warmer than the air above it cools that air from below instead: advection fog's
				// business, not steam's.
				return float.NegativeInfinity;
			}
			float source = (salty ? SeaVapourShare : 1f) * SaturationPressure(waterC);
			return MostExcess(waterC, source, airC, SaturationPressure(Mathf.Min(dewPointC, airC)), out _);
		}

		/// <summary>
		/// How much of the steam a wind leaves, 0..1. A steam fog is fed as fast as it is torn up, so it
		/// outlasts a radiation mist in a breeze; a gale shreds it into streaks of half the strength.
		/// </summary>
		public static float WindLeaves(float windMetresPerSecond)
		{
			return 1f - 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 15f, Mathf.Max(0f, windMetresPerSecond)));
		}

		/// <summary>How ready the air over a water surface is to smoke, 0..1, from <see cref="SteamFogExcess"/> and the wind.</summary>
		public static float SteamFogReadiness(float excessKelvin, float windMetresPerSecond)
		{
			if (!(excessKelvin > OnsetExcess))
			{
				return 0f;
			}
			return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(OnsetExcess, FullExcess, excessKelvin)) * WindLeaves(windMetresPerSecond);
		}

		/// <summary>
		/// How deep the steam stands, m: a metre or two of wisps on a lake at the onset, tens of metres of
		/// sea smoke when the water is 25–30 K warmer than the air; flattened by a wind, which bends the
		/// columns over and mixes their tops away.
		/// </summary>
		public static float SteamFogDepth(float excessKelvin, float windMetresPerSecond)
		{
			if (!(excessKelvin > OnsetExcess))
			{
				return 0f;
			}
			float depth = Mathf.Lerp(ShallowFog, DeepFog, Mathf.Clamp01(excessKelvin / DeepExcess));
			return depth * Mathf.Lerp(1f, 0.35f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 12f, Mathf.Max(0f, windMetresPerSecond))));
		}

		/// <summary>
		/// How fast the steam climbs off the water, m/s: the convection the warm water drives in the cold air
		/// over it — a metre or so a second at the onset, the "steam devils" of a big difference several.
		/// </summary>
		public static float SteamRise(float waterMinusAirKelvin)
		{
			return 0.5f + 0.1f * Mathf.Clamp(waterMinusAirKelvin, 0f, 40f);
		}

		/// <summary>How far downwind the steam is carried for every metre it climbs, m/m: the wind over its rise, at most six.</summary>
		public static float SteamLean(float windMetresPerSecond, float waterMinusAirKelvin)
		{
			return Mathf.Min(6f, Mathf.Max(0f, windMetresPerSecond) / SteamRise(waterMinusAirKelvin));
		}
	}
}
