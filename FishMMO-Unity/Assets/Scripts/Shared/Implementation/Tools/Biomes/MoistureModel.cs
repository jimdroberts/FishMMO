using UnityEngine;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The ground under a point as moisture needs to see it: how high, and whether it is open water.
	/// </summary>
	/// <remarks>
	/// An interface so the moisture walk can be run over ground built by hand — a test's single
	/// ridge on an otherwise flat continent — as well as over a planet's noise field. Implemented by
	/// structs and taken as a generic constraint, so the planet's version is called directly with no
	/// boxing and no allocation.
	/// </remarks>
	public interface IMoistureTerrain
	{
		/// <summary>The ground in a direction from the body's centre.</summary>
		/// <param name="direction">A unit vector.</param>
		/// <param name="altitudeMetres">Metres above sea level; the sea surface (0) over open water.</param>
		/// <param name="ocean">True over open water that evaporates.</param>
		void Sample(Vector3 direction, out float altitudeMetres, out bool ocean);
	}

	/// <summary>
	/// A planet's own ground, seen from a few hundred kilometres up: <see cref="PlanetSurface.CoarseHeight"/>
	/// through the body's hypsometry.
	/// </summary>
	public readonly struct PlanetMoistureTerrain : IMoistureTerrain
	{
		private readonly uint seed;
		private readonly PlanetSurface.PlanetProfile profile;
		private readonly float reliefMetres;
		private readonly bool hasSea;

		public PlanetMoistureTerrain(uint seed, in PlanetSurface.PlanetProfile profile, float reliefMetres)
		{
			this.seed = seed;
			this.profile = profile;
			this.reliefMetres = reliefMetres;
			// A dry world's sub-datum ground is lowland, not sea floor (PlanetSurface.AltitudeFromHeight).
			hasSea = profile.OceanFloor != null;
		}

		public void Sample(Vector3 direction, out float altitudeMetres, out bool ocean)
		{
			float height = PlanetSurface.CoarseHeight(seed, direction);
			ocean = hasSea && height <= profile.SeaLevel;
			altitudeMetres = ocean ? 0f : PlanetSurface.AltitudeFromHeight(height, profile, reliefMetres);
		}
	}

	/// <summary>
	/// One row of an equirectangular grid of the ground, sampled once and looked up by longitude:
	/// for bakes that ask about every pixel of a latitude row.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The moisture walk follows the circle of latitude upwind, so every sample it takes for a pixel
	/// is another pixel of the same row. A globe bake that asks <see cref="MoistureModel.Anomaly(double, Vector3)"/>
	/// per pixel samples the ground eight times a pixel; filling the row once with
	/// <see cref="Fill"/> and walking over this instead samples it once, and the walk itself is a
	/// handful of lookups. Measured in the moisture harness at 2048 pixels a row (.NET, RyuJIT):
	/// 2.8 µs a pixel through the planet's terrain, 0.95 µs through a filled row (0.37 filling it,
	/// 0.58 walking) — a 2048 × 1024 bake pays about 2 s for its moisture instead of 6.
	/// </para>
	/// <para>
	/// Looked up at the nearest pixel, so a sample can be half a pixel from where the per-point walk
	/// would take it — 10 km at a 2048-wide Earth-sized globe's equator, against 100 km between the
	/// nearest two samples. Measured against the per-point walk on the Earth-like reference: mean
	/// difference 0.002 in humidity, 99th percentile 0.02; the largest (0.8) are single pixels where
	/// a sample lands exactly on a coast and the nearest pixel is on the other side of it.
	/// </para>
	/// </remarks>
	public readonly struct MoistureRowTerrain : IMoistureTerrain
	{
		private readonly float[] altitudes;
		private readonly bool[] oceans;

		/// <param name="altitudes">One altitude per pixel, metres, filled by <see cref="Fill"/>.</param>
		/// <param name="oceans">One open-water flag per pixel, filled by <see cref="Fill"/>.</param>
		public MoistureRowTerrain(float[] altitudes, bool[] oceans)
		{
			this.altitudes = altitudes;
			this.oceans = oceans;
		}

		/// <summary>
		/// Samples a row of the world's ground, at pixel centres: longitude (x + 0.5) × 360 / width − 180,
		/// the convention <c>PlanetSurfaceBaker</c> uses.
		/// </summary>
		public static void Fill(in PlanetMoistureTerrain terrain, double latitudeDegrees, float[] altitudes, bool[] oceans)
		{
			int width = altitudes.Length;
			for (int x = 0; x < width; x++)
			{
				double longitude = (x + 0.5) * 360.0 / width - 180.0;
				terrain.Sample(PlanetSurface.Direction(latitudeDegrees, longitude), out altitudes[x], out oceans[x]);
			}
		}

		public void Sample(Vector3 direction, out float altitudeMetres, out bool ocean)
		{
			int width = altitudes.Length;
			// Longitude in the atlas frame (x = cos·sin(lon), z = cos·cos(lon)), then the pixel it falls in.
			float longitude = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			int x = Mathf.FloorToInt((longitude + 180f) / 360f * width);
			x = ((x % width) + width) % width;
			altitudeMetres = altitudes[x];
			ocean = oceans[x];
		}
	}

	/// <summary>
	/// Where the rain falls on a world: humidity as a second climate axis, carried by the prevailing
	/// wind from the sea and wrung out by the ground it crosses.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it exists.</b> Humidity used to be a pure function of temperature and height
	/// (<see cref="ClimateParameters.HumidityAt"/>), so every point at one temperature and height
	/// had the same humidity, and the biome envelopes — authored as boxes on a temperature/humidity
	/// plane — collapsed onto a single curve through it. Jungle, which asks for humidity above 0.8,
	/// could not be reached anywhere on any world; a desert could only ever be a hot place, never a
	/// rain shadow. This is the second axis: two places at the same temperature and height differ
	/// in humidity because the air reaching them has been somewhere different.
	/// </para>
	/// <para>
	/// <b>The model, in the order the air meets it.</b>
	/// </para>
	/// <list type="number">
	/// <item><b>The wind.</b> The prevailing surface wind by latitude, read from
	/// <see cref="WeatherDriver.ZonalWind(float, in WindBelts)"/> — the very wind the weather drifts
	/// on, from the body's own belts (<see cref="WindBelts"/>, so a world's spin, size and sense of
	/// rotation decide it). The climate's wind and the weather's wind cannot disagree.</item>
	/// <item><b>Fetch.</b> The air is followed upwind for a couple of thousand kilometres. Over open
	/// water it fills toward saturation; over land it rains out slowly; climbing over higher ground
	/// than it has crossed before wrings it out by the vapour's own scale height. What arrives is
	/// the air's moisture: an ocean coast is wet, a deep interior dry, the far side of a range drier
	/// still — the rain shadow.</item>
	/// <item><b>Windward slopes.</b> What the air lost climbing the last stretch fell HERE, so the
	/// windward face of a range is the wettest ground on it.</item>
	/// <item><b>Circulation.</b> The overturning cells lift air where they meet at the surface and
	/// sink it where they part above: the equatorial trough (the ITCZ) is wet, the edge of the Hadley
	/// cell (the subtropical highs, ±30° on our world) is dry whatever is upwind, the storm track
	/// in the westerlies is wet, and the pole — always sinking, cold and dense — is dry.</item>
	/// </list>
	/// <para>
	/// The result, <see cref="Precipitation"/>, is the moisture the air brings times the lift that
	/// rains it out. The climate's temperature curves are NOT replaced: heat still dries the ground
	/// (evaporation outruns rain) and cold still holds less vapour. This adds the part those curves
	/// cannot know — where the water comes from — as an anomaly on top (<see cref="Anomaly"/>).
	/// </para>
	/// <para>
	/// <b>A world with nothing to evaporate stays dry.</b> No air: nothing at all is added (the
	/// curves already pin an airless world at −1). Air but no surface liquid — a dry world, or one
	/// frozen solid — the air carries nothing, so every point takes the full dry anomaly. "Liquid"
	/// is what the world's own air condenses (<see cref="PlanetAir.Condensate"/>): water on a
	/// temperate world, methane on a Titan, whose lakes have a weather cycle of their own.
	/// </para>
	/// <para>
	/// <b>Stateless and cheap.</b> A pure function of the point, deterministic on every machine, no
	/// allocation: seven upwind samples and one at the point, each through
	/// <see cref="PlanetSurface.CoarseHeight"/> (eight noise evaluations rather than the full
	/// field's fourteen). A tidally locked world, whose real circulation runs from its day side to
	/// its night side, is treated as the single slow cell its slow spin gives it — the belt model's
	/// own answer, not a special case.
	/// </para>
	/// </remarks>
	public struct MoistureModel
	{
		// ── Calibration ───────────────────────────────────────────────

		/// <summary>
		/// How far upwind each sample is, in kilometres on an Earth-sized world, farthest last.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Taken as arcs of the sphere (km ÷ <see cref="PlanetSurface.EarthRadiusKm"/>), because the
		/// ground is a field on the sphere: a mountain range at the noise's own scale spans the same
		/// arc on every body. The distances the air travels between them are then the body's real
		/// kilometres, so a small world's small continents are crossed quickly and rain out less.
		/// </para>
		/// <para>
		/// Spaced more finely near the point, where the ground decides the most (a coastal range
		/// a hundred kilometres away matters more than one two thousand away), and reaching 2300 km,
		/// about the distance continental air keeps a maritime origin (<see cref="LandDecayKm"/>).
		/// </para>
		/// </remarks>
		private static readonly float[] UpwindKm = { 100f, 250f, 450f, 700f, 1050f, 1550f, 2300f };

		/// <summary>Kilometres of open water over which dry air recovers most of the way to saturated.</summary>
		/// <remarks>The e-folding fetch for evaporation into a passing air mass: a few hundred kilometres of sea makes maritime air.</remarks>
		public const float OceanRechargeKm = 600f;

		/// <summary>Kilometres of land over which an air mass loses most of its moisture to rain, with no ground in the way.</summary>
		/// <remarks>
		/// Not the ~1000 km of a single rain-out: land gives much of what falls back to the air
		/// (evapotranspiration), which is how Amazonian rain stays heavy 2000 km from the Atlantic.
		/// </remarks>
		public const float LandDecayKm = 2500f;

		/// <summary>Moisture of air of unknown history found over land at the far end of the walk, 0 … 1, on a fully wet world.</summary>
		public const float LandStart = 0.5f;

		/// <summary>
		/// How steady the zonal wind must be before the walk upwind is believed in full.
		/// </summary>
		/// <remarks>
		/// <para>
		/// At a belt edge the surface wind has no steady direction — on our world, the subtropical
		/// highs at ±30° and the polar front at ±60°: the air on one side goes one way and on the
		/// other the other way. Walking "upwind" from there would put the samples on opposite sides
		/// of a line of latitude and draw that line on the map. So the walk is weighted by the
		/// zonal wind's strength (which passes through zero exactly where its direction flips) and
		/// blended with the point's own surface, which makes the field continuous across every edge.
		/// </para>
		/// <para>
		/// 0.35 of full strength: on our world that is fully steady to within about four degrees of
		/// either edge.
		/// </para>
		/// </remarks>
		public const float SteadyZonal = 0.35f;

		/// <summary>
		/// Lift from the circulation, from <see cref="Circulation"/>: the share of the air's moisture
		/// that rains out here. <c>LiftFloor + LiftRange × circulation</c>.
		/// </summary>
		/// <remarks>
		/// 0.3 under a subtropical high (circulation −0.75), 1.0 in the equatorial trough (+1):
		/// a sinking air mass still rains on a windward coast now and then, which is why Florida is
		/// not the Sahara, but much less than rising air.
		/// </remarks>
		public const float LiftFloor = 0.6f;
		public const float LiftRange = 0.4f;

		/// <summary>The circulation at the Hadley cell's rising edge, the equatorial trough.</summary>
		public const float TroughCirculation = 1f;

		/// <summary>The circulation where two cells sink: the subtropical highs, and the pole.</summary>
		public const float SubsidenceCirculation = -0.75f;

		/// <summary>The circulation in the storm track, where the westerlies' fronts lift the air.</summary>
		public const float StormTrackCirculation = 0.55f;

		/// <summary>Where across the westerly belt the storm track is fully established, 0 … 1.</summary>
		/// <remarks>Our own storm track peaks around 45–55°, two thirds of the way across the 30–60° belt rather than at its poleward edge.</remarks>
		public const float StormTrackOnset = 0.6f;

		/// <summary>
		/// Humidity units per e-fold of <see cref="Precipitation"/>: how far the wettest and driest
		/// places stand from the middle.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Logarithmic, because rain is.</b> Rainfall spans orders of magnitude — a desert gets a
		/// twentieth of a rainforest's — and what decides the ground is the ratio: halving the rain
		/// of a steppe makes a desert, halving a rainforest's leaves a forest. Read linearly, the
		/// measured precipitation (Earth-like reference land: 5th percentile 0.05, median 0.16, 95th
		/// 0.64) gave a long wet tail running off the top of the scale and no dry end. Read as a log
		/// ratio the two tails come out nearly symmetric about the median.
		/// </para>
		/// <para>
		/// Measured on the Earth-like reference (seed 1, 100,000 Fibonacci points, the moisture
		/// harness, 2026-10-02), land humidity after the temperature curves: 5th percentile −0.51,
		/// quartiles −0.17 / +0.11 / +0.50, 95th +0.91 — where before it was −0.06 … +0.15, a curve.
		/// 0.7 clipped the wettest 7% of land at +1; 0.6 leaves the top of the scale to the wettest
		/// windward tropical coasts. Re-measure if the walk changes.
		/// </para>
		/// </remarks>
		public const float Span = 0.6f;

		/// <summary>
		/// The <see cref="Precipitation"/> that adds nothing: the median of the Earth-like
		/// reference's land.
		/// </summary>
		/// <remarks>
		/// Centring on the median keeps the middle of the land where the biome envelopes were
		/// authored against — the temperate grassland stays grassland — and spreads the rest
		/// either side of it, rather than shifting the whole world wetter or drier. Measured.
		/// </remarks>
		public const float Midpoint = 0.16f;

		/// <summary>
		/// Precipitation added before the log is taken, so nothing reads as infinitely dry.
		/// </summary>
		/// <remarks>
		/// It sets the floor: a world with nothing to evaporate (precipitation 0) reads
		/// <c>Span × ln(Floor / (Midpoint + Floor))</c> ≈ −1.5, past the scale's −1, so it is fully
		/// dry whatever its temperature curves say, and the driest real desert is still distinguished
		/// from the merely dry.
		/// </remarks>
		public const float Floor = 0.02f;

		/// <summary>The vapour scale height is clamped to this range, metres.</summary>
		private const float MinVapourScaleHeight = 500f;
		private const float MaxVapourScaleHeight = 10000f;

		/// <summary>No sample walks further round a latitude circle than half of it, which only matters near a pole.</summary>
		private const float MaxLongitudeSweep = Mathf.PI;

		// ── Per world ─────────────────────────────────────────────────

		/// <summary>True when the world has air to carry anything.</summary>
		public bool HasAir;

		/// <summary>True when the world has open liquid for its air to take up: water, or whatever its air condenses.</summary>
		public bool SurfaceLiquid;

		/// <summary>Moisture of air of unknown history over far land, for this world's share of water.</summary>
		public float FarLandMoisture;

		/// <summary>The body's radius in km: the arcs between samples become real distances through it.</summary>
		public float RadiusKm;

		/// <summary>
		/// How high air must climb to lose most of its vapour, metres: <c>Rv·T² / (L·Γ)</c>.
		/// </summary>
		/// <remarks>
		/// Vapour thins with height because the air cools as it rises and cold air holds less, so
		/// its scale height is the Clausius–Clapeyron rate set against the lapse rate. About 2.4 km
		/// on our world, which is why a two-kilometre range throws a deep rain shadow; under the home
		/// world's weaker pull the air cools more slowly and the same range casts a shallower one.
		/// </remarks>
		public float VapourScaleHeight;

		/// <summary>The body's wind belts, the same ones its weather uses.</summary>
		public WindBelts Belts;

		/// <summary>The ground the walk samples.</summary>
		public PlanetMoistureTerrain Terrain;

		/// <summary>The upwind arcs, radians, shared by every world.</summary>
		private static readonly float[] UpwindArcs = BuildArcs();

		private static float[] BuildArcs()
		{
			var arcs = new float[UpwindKm.Length];
			for (int i = 0; i < arcs.Length; i++)
			{
				arcs[i] = UpwindKm[i] / PlanetSurface.EarthRadiusKm;
			}
			return arcs;
		}

		/// <summary>
		/// A body's moisture model, from what it is.
		/// </summary>
		/// <param name="system">The solar system. Null gives an Earth-like world's air.</param>
		/// <param name="body">The body. Null gives an Earth-like world.</param>
		/// <param name="seed">The body's terrain seed, as the climate field resolved it.</param>
		/// <param name="profile">The body's surface profile, as the climate field resolved it.</param>
		/// <param name="reliefMetres">The body's relief, as the climate field resolved it.</param>
		/// <param name="conditions">What the world offers, for whether its water is liquid.</param>
		public static MoistureModel For(SolarSystemProfile system, WorldBody body, uint seed,
			in PlanetSurface.PlanetProfile profile, float reliefMetres, in BiomeWorldConditions conditions)
		{
			PlanetAir air = PlanetAir.For(system, body);
			bool hasAir = conditions.Atmosphere != AtmosphereKind.None && air.HasAir;

			/* Liquid is whatever this world's air condenses. On a temperate world that is water,
			 * and the world's conditions already decide whether it is liquid anywhere. On a cold
			 * world with air the condensate is methane or nitrogen, and the body's water fraction
			 * stands for the lakes of it — Titan's hydrological cycle runs on methane. A world frozen
			 * solid under air that condenses water has nothing open to evaporate and comes out dry. */
			bool cryogenic = air.Condensate == Condensate.Methane
				|| air.Condensate == Condensate.Nitrogen
				|| air.Condensate == Condensate.Ammonia;
			bool liquid = hasAir && (conditions.HasLiquidWater || (cryogenic && conditions.Water > 0.02f));

			float lapse = (float)ClimateModel.LapseRatePerMetre(conditions.Atmosphere, air.Gravity, air.SpecificHeat);
			float kelvin = Mathf.Max(20f, air.MeanSurfaceKelvin);
			float scaleHeight = AirPhysics.VapourGasConstant(air.Condensate) * kelvin * kelvin
				/ Mathf.Max(1f, AirPhysics.LatentHeat(air.Condensate) * Mathf.Max(1e-5f, lapse));

			float radiusKm = body != null && body.SkyRadiusKm > 0.01f ? body.SkyRadiusKm : PlanetSurface.EarthRadiusKm;
			return Of(seed, profile, reliefMetres, radiusKm, WindBelts.For(air), hasAir, liquid, conditions.Water, scaleHeight);
		}

		/// <summary>
		/// A moisture model from its parts, for tests and probes that describe a world directly
		/// rather than through its assets.
		/// </summary>
		/// <param name="water">The world's water fraction, 0 … 1: a drier world's far land starts drier.</param>
		/// <param name="vapourScaleHeight">Metres; see <see cref="VapourScaleHeight"/>.</param>
		public static MoistureModel Of(uint seed, in PlanetSurface.PlanetProfile profile, float reliefMetres, float radiusKm,
			in WindBelts belts, bool hasAir, bool surfaceLiquid, float water, float vapourScaleHeight)
		{
			return new MoistureModel
			{
				HasAir = hasAir,
				SurfaceLiquid = hasAir && surfaceLiquid,
				// A wet world's far land is half saturated; a world of puddles' much less.
				FarLandMoisture = LandStart * Mathf.Clamp01(water / 0.5f),
				RadiusKm = Mathf.Max(1f, radiusKm),
				VapourScaleHeight = Mathf.Clamp(vapourScaleHeight, MinVapourScaleHeight, MaxVapourScaleHeight),
				Belts = belts,
				Terrain = new PlanetMoistureTerrain(seed, profile, reliefMetres),
			};
		}

		// ── The field ─────────────────────────────────────────────────

		/// <summary>
		/// What the water cycle adds to a point's humidity, on the climate's −1 … 1 scale: positive
		/// where the air brings more rain than the world's middle, negative where less.
		/// </summary>
		/// <remarks>
		/// Zero on a world without air (its humidity is already −1). On a world with air and nothing
		/// to evaporate it is the full dry anomaly everywhere (see <see cref="Floor"/>).
		/// </remarks>
		public float Anomaly(double latitudeDegrees, Vector3 direction) => Anomaly(latitudeDegrees, direction, Terrain);

		/// <summary>The same, over ground the caller supplies.</summary>
		public float Anomaly<T>(double latitudeDegrees, Vector3 direction, in T terrain) where T : struct, IMoistureTerrain
		{
			if (!HasAir)
			{
				return 0f;
			}
			return Span * Mathf.Log((Precipitation(latitudeDegrees, direction, terrain) + Floor) / (Midpoint + Floor));
		}

		/// <summary>
		/// How much rain the point gets, 0 … 1: the moisture the wind brings times the lift that
		/// wrings it out, plus whatever fell climbing the last stretch to it.
		/// </summary>
		public float Precipitation<T>(double latitudeDegrees, Vector3 direction, in T terrain) where T : struct, IMoistureTerrain
		{
			if (!HasAir || !SurfaceLiquid)
			{
				return 0f;
			}
			float latitude = (float)latitudeDegrees;
			float moisture = Supply(latitude, direction, terrain, out float windward);
			float lift = LiftFloor + LiftRange * Circulation(latitude, Belts);
			return Mathf.Clamp01(moisture * lift + windward);
		}

		/// <summary>
		/// The air's moisture arriving at a point, 0 dry … 1 saturated maritime air, and the rain
		/// it lost climbing to it.
		/// </summary>
		/// <param name="windward">What the last stretch of rising ground wrung out, which fell here.</param>
		public float Supply<T>(float latitudeDegrees, Vector3 direction, in T terrain, out float windward) where T : struct, IMoistureTerrain
		{
			windward = 0f;
			if (!HasAir || !SurfaceLiquid)
			{
				return 0f;
			}
			Vector3 p = direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.up;
			terrain.Sample(p, out float altitude, out bool ocean);
			// The point's own surface: what it would have with no steady wind to bring anything.
			float local = ocean ? 1f : FarLandMoisture;

			float zonal = WeatherDriver.ZonalWind(latitudeDegrees, Belts);
			float steadiness = Mathf.Clamp01(Mathf.Abs(zonal) / SteadyZonal);
			if (steadiness <= 0f)
			{
				return local;
			}

			/* Upwind along the circle of latitude. A westerly (zonal > 0) blows toward the east, so
			 * its air comes from the west: a negative turn in longitude. Following the latitude
			 * circle rather than a great circle keeps the walk in the belt the wind belongs to. */
			float turn = zonal > 0f ? -1f : 1f;
			float cosLatitude = Mathf.Max(1e-3f, Mathf.Cos(latitudeDegrees * Mathf.Deg2Rad));

			int last = UpwindArcs.Length - 1;
			float q = 0f, ceiling = 0f, previousArc = 0f;
			for (int i = last; i >= 0; i--)
			{
				float arc = UpwindArcs[i];
				float angle = Mathf.Min(arc / cosLatitude, MaxLongitudeSweep) * turn;
				terrain.Sample(AboutPole(p, angle), out float sampleAltitude, out bool sampleOcean);
				if (i == last)
				{
					// Air of unknown history: saturated off the sea, part-spent over land, already lifted to the ground it is on.
					q = sampleOcean ? 1f : FarLandMoisture;
					ceiling = sampleOcean ? 0f : sampleAltitude;
				}
				else
				{
					Step(ref q, ref ceiling, (previousArc - arc) * RadiusKm, sampleAltitude, sampleOcean);
				}
				previousArc = arc;
			}
			// The last stretch, onto the point itself. What it wrings out climbing falls here.
			float lost = Step(ref q, ref ceiling, previousArc * RadiusKm, altitude, ocean);

			windward = lost * steadiness;
			return Mathf.Lerp(local, q, steadiness);
		}

		/// <summary>
		/// One stretch of the walk: the air crosses <paramref name="km"/> and arrives over ground at
		/// <paramref name="altitude"/>. Returns what it lost to lifting on the way.
		/// </summary>
		/// <param name="q">The air's moisture, 0 … 1.</param>
		/// <param name="ceiling">
		/// The highest ground the air has been lifted over since it last saw the sea, metres. Only
		/// climbing ABOVE it wrings more out: air that has crossed a three-kilometre plateau is already
		/// as dry as a two-kilometre ridge would make it.
		/// </param>
		private float Step(ref float q, ref float ceiling, float km, float altitude, bool ocean)
		{
			if (ocean)
			{
				// Evaporation fills the air toward saturation, and it settles back to the sea surface.
				float keep = Mathf.Exp(-km / OceanRechargeKm);
				q = 1f - (1f - q) * keep;
				ceiling *= keep;
				return 0f;
			}

			float decay = Mathf.Exp(-km / LandDecayKm);
			q *= decay;
			if (altitude <= ceiling)
			{
				// Over a long stretch of lower land the air forgets how high it was lifted, as it does how wet it was.
				ceiling = altitude + (ceiling - altitude) * decay;
				return 0f;
			}
			// Forced up: the air cools, its vapour condenses and falls as it climbs.
			float before = q;
			q *= Mathf.Exp(-(altitude - ceiling) / VapourScaleHeight);
			ceiling = altitude;
			return before - q;
		}

		/// <summary>A direction turned about the body's axis (+Y) by an angle in radians: east for a positive angle.</summary>
		/// <remarks>
		/// The atlas frame (<see cref="FishMMO.Shared.Atlas.AtlasGeometry.ToUnit"/>): x = cos(lat)·sin(lon),
		/// z = cos(lat)·cos(lon), so adding to the longitude is this rotation.
		/// </remarks>
		private static Vector3 AboutPole(Vector3 p, float angle)
		{
			float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
			return new Vector3(p.x * c + p.z * s, p.y, p.z * c - p.x * s);
		}

		/// <summary>
		/// The overturning circulation's vertical motion at a latitude, −1 … 1: +1 where the cells
		/// lift the air (the equatorial trough), negative where they sink it (the subtropical highs,
		/// the pole).
		/// </summary>
		/// <remarks>
		/// <para>
		/// The cells are the body's own (<see cref="WindBelts.Locate"/>), the same ones its winds
		/// blow in. Rising at the Hadley cell's equatorward edge, sinking at its poleward edge; in
		/// each westerly belt the fronts of the storm track lift the air through most of the belt;
		/// each easterly belt beyond sinks again toward its poleward edge. A world with more, narrower
		/// belts — faster spin, smaller size — gets more, narrower wet and dry bands, weaker the
		/// further they are from the equator; a slow world has one cell, wet equator, dry pole.
		/// </para>
		/// <para>
		/// The pole always sinks: cold, dense air pours off it whatever the belt count says, so the
		/// last half-cell is drawn to subsidence.
		/// </para>
		/// </remarks>
		public static float Circulation(float latitudeDegrees, in WindBelts belts)
		{
			float absolute = Mathf.Min(90f, Mathf.Abs(latitudeDegrees));
			int index = belts.Locate(absolute, out float t, out bool _);
			float from = Boundary(index), to = Boundary(index + 1);
			float c;
			if (index == 0)
			{
				// Trough to high, smoothly.
				c = Mathf.Lerp(from, to, 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI));
			}
			else if ((index & 1) == 1)
			{
				// High to storm track: the fronts are established well before the belt's poleward edge.
				float s = Mathf.Clamp01(t / StormTrackOnset);
				c = Mathf.Lerp(from, to, s * s * (3f - 2f * s));
			}
			else
			{
				// Storm track to the next high, or to the pole.
				c = Mathf.Lerp(from, to, 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI));
			}

			float cell = Mathf.Clamp(belts.CellDegrees, 1f, 90f);
			float half = Mathf.Min(45f, cell * 0.5f);
			float polar = Mathf.Clamp01((absolute - (90f - half)) / half);
			return Mathf.Lerp(c, SubsidenceCirculation, polar * polar * (3f - 2f * polar));
		}

		/// <summary>The circulation at belt edge <paramref name="k"/>: the trough at 0, highs at odd edges, storm tracks at even ones, fading poleward.</summary>
		private static float Boundary(int k)
		{
			if (k <= 0)
			{
				return TroughCirculation;
			}
			// Our own world has three belts; past the second edge, further belts are weaker copies.
			float fade = k <= 2 ? 1f : 2f / k;
			return (k & 1) == 1 ? SubsidenceCirculation * fade : StormTrackCirculation * fade;
		}
	}
}
