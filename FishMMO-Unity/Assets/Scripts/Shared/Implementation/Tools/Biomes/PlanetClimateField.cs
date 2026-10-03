using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// One point on a world's surface: how high it stands, how warm and wet it is, and which biome
	/// that makes it.
	/// </summary>
	public struct PlanetSurfacePoint
	{
		/// <summary>The raw surface field at this point, 0 … 1. Only meaningful against a profile.</summary>
		public float Height;

		/// <summary>Metres above the body's sea level. Negative is under water.</summary>
		public float AltitudeMetres;

		/// <summary>
		/// Height as the biome system reads it, 0 … 1, with the water line at
		/// <see cref="ClimateModel.DefaultWaterSurfaceHeight"/>.
		/// </summary>
		public float NormalizedHeight;

		public ClimateSample Climate;

		/// <summary>True when this point is below the body's sea level.</summary>
		public bool UnderWater => AltitudeMetres < 0f;
	}

	/// <summary>
	/// A world's climate as a field over its whole surface: the terms that do not change from
	/// point to point, worked out once, so asking about a point costs a few noise samples.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Shared so the picture and the ground cannot disagree.</b> The globe bake, the scene
	/// generator and anything that names a place all have to answer the same question — what is it
	/// like at this latitude and longitude — and each doing its own arithmetic is how a scene comes
	/// to be called a frost hollow on ground the map draws as desert. <c>AltitudeFromHeight</c> was
	/// pulled out for exactly this reason after the bake and the generator disagreed about how high
	/// a continent stood; this is the same move for temperature and humidity.
	/// </para>
	/// <para>
	/// <b>A struct built once, then asked many times.</b> The bake asks it two million times in a
	/// loop, so the mean surface temperature, the lapse rate and the hypsometric profile are
	/// resolved in <see cref="For"/> and never again. Nothing here allocates.
	/// </para>
	/// </remarks>
	public struct PlanetClimateField
	{
		/// <summary>
		/// How far regional climate pushes a temperature about, in scale units.
		/// </summary>
		/// <remarks>
		/// 0.18 is about six kelvin. Measured on Naron, the latitude term moves roughly 0.02 units
		/// per degree, so this swings the snowline about nine degrees either way — enough for real
		/// lobes and bays, small enough that it never turns a temperate world into a frozen one.
		/// </remarks>
		public const float RegionalVariation = 0.18f;

		/// <summary>The body's terrain seed.</summary>
		public uint Seed;

		/// <summary>How heavily cratered this body's surface is, from its atmosphere.</summary>
		public float Cratering;

		/// <summary>Sea level, floor and summit of the surface field on this body.</summary>
		public PlanetSurface.PlanetProfile Profile;

		/// <summary>Floor-to-summit relief in metres.</summary>
		public float ReliefMetres;

		/// <summary>Metres above sea level of the highest ground, and below it of the deepest.</summary>
		public float HighestMetres;
		public float LowestMetres;

		/// <summary>Mean surface temperature in scale units, UNCLAMPED: the world's average over its whole sphere.</summary>
		public float MeanTemperature;

		/// <summary>
		/// Temperature of the ground directly under the star at noon, in scale units, UNCLAMPED —
		/// what latitude and altitude are taken down from.
		/// </summary>
		/// <remarks>
		/// The latitude term is zero under the sun and negative everywhere else, so it has to start
		/// from the hottest point, not the average: starting from the mean cooled every point twice,
		/// about 0.36 units too cold on an Earth-like world — the equator read +0.50 where the
		/// runtime model (<see cref="ClimateModel.For"/>, which starts from the same sub-solar
		/// figure) reads +0.80, and Jungle, Savanna and Scrubland could never be chosen. Measured
		/// with this, the globe's area-weighted average lands within about 0.02 of the mean.
		/// </remarks>
		public float SubSolarTemperature;

		/// <summary>Temperature lost per metre of altitude, in scale units.</summary>
		public float LapsePerMetre;

		/// <summary>What the world makes for itself: 0 dead, 1 molten.</summary>
		public float InternalHeat;

		/// <summary>What the world offers a biome, atmosphere and water included.</summary>
		public BiomeWorldConditions Conditions;

		/// <summary>The humidity curves, from the same derivation the runtime climate uses.</summary>
		public ClimateParameters Parameters;

		/// <summary>Where the water cycle puts the rain: the wind-driven half of humidity.</summary>
		public MoistureModel Moisture;

		private SolarSystemProfile system;
		private WorldBody body;

		/// <summary>True when there is a body to answer about.</summary>
		public bool Valid => body != null;

		/// <summary>The body this field describes.</summary>
		public WorldBody Body => body;

		/// <summary>
		/// Resolves everything that is the same everywhere on a body.
		/// </summary>
		/// <param name="system">The solar system, for the starlight. Null gives an Earth-like world.</param>
		/// <param name="body">The body. Null gives a field that answers Earth-like everywhere.</param>
		public static PlanetClimateField For(SolarSystemProfile system, WorldBody body)
		{
			var field = new PlanetClimateField
			{
				system = system,
				body = body,
				Seed = body != null ? body.ResolvedTerrainSeed : 1u,
				Cratering = PlanetSurface.CrateringOf(body),
				ReliefMetres = PlanetSurface.ReliefMetres(body),
				InternalHeat = ClimateModel.InternalHeat(system, body),
				Conditions = BiomeWorldConditions.For(system, body),
			};

			field.Profile = PlanetSurface.ProfileOf(field.Seed, body);

			/* Unclamped, because latitude and altitude are still to be subtracted from it. Clamped
			 * first, a 460 K greenhouse world reads +1 like any warm planet, and the pole's -1.6
			 * then drags it below freezing — which is how a world hot enough to melt lead came out
			 * with ice caps.
			 *
			 * The orbit's mean, and the same function the world's conditions, its air and its
			 * surface liquids read, so the field and the conditions it is built with cannot disagree
			 * about how warm the world is (they once differed by a whole world: see
			 * BiomeWorldConditions). */
			field.MeanTemperature = (float)ClimateModel.ToScaleUnclamped(ClimateModel.MeanSurfaceKelvin(system, body));
			field.SubSolarTemperature = field.MeanTemperature + (float)(ClimateModel.SubSolarExcess / ClimateModel.KelvinPerUnit);
			field.LapsePerMetre = (float)ClimateModel.LapseRatePerMetre(system, body) / (float)ClimateModel.KelvinPerUnit;

			// The ends of the hypsometric curve, so a height can be placed against the world's own
			// range rather than against a number chosen for an Earth-sized planet.
			field.HighestMetres = PlanetSurface.AltitudeFromHeight(field.Profile.Highest, field.Profile, field.ReliefMetres);
			field.LowestMetres = PlanetSurface.AltitudeFromHeight(field.Profile.Lowest, field.Profile, field.ReliefMetres);

			// The humidity curves only; its lapse rate is per-normalised-height and this works in
			// metres, so TemperatureAt does that part itself.
			field.Parameters = ClimateModel.For(system, body, field.ReliefMetres);
			field.Moisture = MoistureModel.For(system, body, field.Seed, field.Profile, field.ReliefMetres, field.Conditions);
			return field;
		}

		/// <summary>
		/// The regional wobble at a point, in scale units.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A regional term on top of the physics, so the snowline is a coastline and not a ruled
		/// line. Latitude temperature is a pure function of latitude and altitude barely moves on
		/// gentle ground, so without this the freezing contour is exactly a circle of latitude, and
		/// the ice cap comes out as a band drawn straight across the map with a razor edge. Real
		/// caps are lobed because currents, land and weather push them about; this stands in for
		/// all three at a fraction of a kelvin's worth of variation.
		/// </para>
		/// <para>
		/// Two scales, and stretched, because fBm does not fill its own range. Measured:
		/// (Fbm − 0.5) × 2 swings only about ±0.22, since averaging octaves pulls the result to the
		/// middle. Unstretched that was ±0.02 of temperature — under a degree of latitude, and
		/// invisible.
		/// </para>
		/// </remarks>
		public float RegionalOffset(Vector3 direction)
		{
			float coarse = Mathf.Clamp((PlanetSurface.FieldNoise(Seed ^ 0x51CEEDA7u, direction, 2.4f, 3) - 0.5f) * 2f * 3.4f, -1f, 1f);
			float fine = Mathf.Clamp((PlanetSurface.FieldNoise(Seed ^ 0x2A66EDu, direction, 11f, 3) - 0.5f) * 2f * 3.4f, -1f, 1f);
			return (coarse * 0.78f + fine * 0.22f) * RegionalVariation;
		}

		// ── Biome selection: fuzzy, interlocking boundaries ──────────

		/// <summary>
		/// How many features of the selection noise fit across the unit sphere at its coarsest octave.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The noise lattice is one cell per <c>1 / frequency</c> of the body's radius, so 24 puts the
		/// coarsest cells about 265 km across on an Earth-sized world and the three octaves (×2.02 each)
		/// add meanders at about 130 km and 65 km. That is the scale real ecotones wander at on a map —
		/// a forest edge or a treeline traced across a continent bends every few tens to hundreds of
		/// kilometres — and it is several globe pixels (a 2048-wide bake is about 20 km a pixel at the
		/// equator), so the bends show on the globe instead of averaging away inside one pixel.
		/// </para>
		/// <para>
		/// <b>Coherent within a scene.</b> Scenes are cut on the atlas radius (10–30 km), not the
		/// body's own, so a 2–5 km scene spans several degrees of the sphere and sees a few cells of the
		/// finest octave: on a 30 km atlas those cells are about 300 m across and the coarsest about
		/// 1.2 km. A scene's biome edges therefore bend smoothly every few hundred metres around the
		/// contours they otherwise follow, which is how a treeline or a forest edge looks from a ridge,
		/// and never flicker cell to cell (a scene samples every 16 m). It is a fraction of the sphere,
		/// like <see cref="RegionalOffset"/>, so the globe looks the same on any size of body.
		/// </para>
		/// </remarks>
		public const float SelectionNoiseFrequency = 24f;

		/// <summary>Octaves of the selection noise: 265, 130 and 65 km cells on an Earth-sized body.</summary>
		public const int SelectionNoiseOctaves = 3;

		/// <summary>
		/// How far fBm's swing is stretched before it is clamped to −1…1, for the selection noise.
		/// </summary>
		/// <remarks>
		/// fBm averages its octaves toward the middle, so <c>(Fbm − 0.5) × 2</c> almost never leaves
		/// ±0.5 and an amplitude applied to it would be mostly unused. Measured over 200,000 Fibonacci
		/// points at this frequency and octave count: unstretched, its standard deviation is 0.176;
		/// stretched by 2.8 it is 0.48, reaches the clamp on 3.9% of the sphere, and its mean is
		/// −0.0005. So each amplitude below is the most a boundary moves, about half of it the
		/// typical move, and the shift averages out. (<see cref="RegionalOffset"/>'s 3.4 clips 9.6% at
		/// this frequency — too much of the sphere sitting at the extreme.)
		/// </remarks>
		public const float SelectionNoiseStretch = 2.8f;

		/// <summary>
		/// The most the biome-selection altitude moves, as a fraction of the altitude itself.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Proportional, so the water line is never crossed.</b> Under 1, a point above sea level is
		/// still above it once moved and a point below is still below, so a biome is never chosen from
		/// the wrong side of the shore. Proportional also matches how the landform edges blur: the
		/// shore's 30 m edge moves a few metres, which is what tidal range and storm reach do to a real
		/// coast; the 1.5 km lowland/highland edge moves a few hundred metres, which is how far the
		/// UNEP-WCMC classes (Kapos 2000) themselves let it move — between 300 m and 2.5 km "mountain"
		/// depends on local relief and slope, not altitude alone. The shelf break at 200 m moves about
		/// 40 m; Earth's real one lies anywhere from about 100 to 500 m.
		/// </para>
		/// </remarks>
		public const float SelectionHeightFraction = 0.2f;

		/// <summary>
		/// The cap on how far the biome-selection altitude moves, in Earth metres (scaled by the body's
		/// relief, like the tier edges).
		/// </summary>
		/// <remarks>
		/// 500 m: the mass-elevation effect alone raises vegetation belts by several hundred metres in
		/// the interior of a large massif against an isolated peak at the same latitude (the Alps'
		/// treeline sits near 1.8 km on the outer ranges and above 2.3 km in the central ones), and the
		/// abyssal plains Earth's 4 km edge stands for lie anywhere from 3 to 6 km down. So the mountain,
		/// alpine and nival edges wander by a few hundred metres, and by up to half a kilometre, around
		/// a massif — enough to break the concentric rings, too little to put a glacier on a plain.
		/// </remarks>
		public const float SelectionHeightMetres = 500f;

		/// <summary>The most the biome-selection temperature moves, in scale units (0.06 ≈ 2 K).</summary>
		/// <remarks>
		/// What the field's climate does not resolve: slope aspect (a sun-facing slope runs 1–3 K warmer
		/// than a shaded one), cold air pooling in basins, the local effect of a lake or a forest. A
		/// tenth to a quarter of the narrower temperature envelopes (Tundra spans 0.35, Taiga 0.5), so
		/// a boundary wanders across a band of the envelope rather than swapping one biome for another
		/// wholesale. <see cref="RegionalOffset"/> already moves the snowline at continental scale; this
		/// is the finer scale under it.
		/// </remarks>
		public const float SelectionTemperature = 0.06f;

		/// <summary>The most the biome-selection humidity moves, in scale units.</summary>
		/// <remarks>
		/// Wider than temperature's in proportion, because humidity boundaries are genuinely wider in
		/// nature: forest and savanna both occur across a wide band of rainfall, decided locally by
		/// soil, fire and drainage, so the boundary between them is a mosaic tens of kilometres deep.
		/// About a quarter of the narrowest humidity envelopes (Grassland 0.25, Plains 0.3).
		/// </remarks>
		public const float SelectionHumidity = 0.08f;

		/// <summary>
		/// The three selection-noise values at a point, each −1…1 and zero-mean over the sphere:
		/// one for the altitude, one for the temperature, one for the humidity.
		/// </summary>
		/// <remarks>
		/// Independent fields (one seed each, from the body's terrain seed), so the three boundaries
		/// do not all bend the same way at the same place: a treeline and a forest/grassland edge
		/// interlock instead of moving in lock-step. Nine gradient-noise samples, no allocation.
		/// </remarks>
		public void SelectionNoise(Vector3 direction, out float height, out float temperature, out float humidity)
		{
			height = SignedSelectionNoise(Seed ^ 0x7E1C0A3Du, direction);
			temperature = SignedSelectionNoise(Seed ^ 0x3B05E6F1u, direction);
			humidity = SignedSelectionNoise(Seed ^ 0x5D17A92Bu, direction);
		}

		private static float SignedSelectionNoise(uint seed, Vector3 direction)
		{
			float n = PlanetSurface.FieldNoise(seed, direction, SelectionNoiseFrequency, SelectionNoiseOctaves);
			return Mathf.Clamp((n - 0.5f) * 2f * SelectionNoiseStretch, -1f, 1f);
		}

		/// <summary>
		/// The point as the biome resolver reads it: its altitude, temperature and humidity moved by
		/// the selection noise, its normalised height and elevation tier recomputed from the moved
		/// altitude. Everything else is copied.
		/// </summary>
		/// <param name="direction">The point as a unit vector from the body's centre.</param>
		/// <param name="point">The honest point, from <see cref="At(Vector3, float)"/> or its siblings.</param>
		/// <remarks>
		/// <para>
		/// <b>Why.</b> The resolver chooses exactly one biome from a height tier and a climate envelope,
		/// so on its own every biome border is a contour line or an isotherm: round a massif the
		/// glacier, scree and rock came out as concentric rings at the exact tier altitudes, and
		/// elsewhere biomes met along hard, smooth curves. Real borders are ecotones — the treeline
		/// rises on a sun-facing slope and in the heart of a range, the forest gives way to grassland
		/// in a mosaic decided by soil and fire — so the choice is made from values moved by a
		/// coherent, deterministic field, and the borders meander and interlock.
		/// </para>
		/// <para>
		/// <b>Only the choice.</b> The point's reported climate (<see cref="PlanetSurfacePoint.Climate"/>,
		/// which the weather, exposure and naming read) is never moved, and neither is anything that
		/// decides where a liquid stands: sea, lava and ice shelves are placed from the real altitude.
		/// The moved altitude keeps its side of the water line by construction
		/// (<see cref="SelectionHeightFraction"/> is under 1).
		/// </para>
		/// <para>
		/// <b>A reading at the end of the scale stays there.</b> Temperature and humidity are moved by
		/// at most their distance from the nearest end (the offset is tapered over the last
		/// amplitude's worth of the scale), so the result needs no clamp and is continuous, and a
		/// world whose readings sit pinned at a corner — every airless body reads humidity −1, every
		/// moon past the frost line temperature −1 — keeps choosing exactly as before, by weight.
		/// </para>
		/// <para>
		/// <b>Measured</b> (2026-10-02, 200,000 points a world, the <c>BiomeSpecTable</c> envelopes): the
		/// choice changes on 9.6% of an Earth-like world and 12.4% of Arthis, about 3% of a dead moon;
		/// no biome's share of any of the twenty worlds moves by more than 0.66 points (the largest is
		/// the 4 km abyssal edge, Deep Ocean ↔ Ocean); no biome chosen anywhere before stops being
		/// chosen; the alien biomes stay at 0% of the Earth-like world and of Arthis; no point
		/// changed side of the water line. The mean shifts are −0.0005 of noise, under 2 m of altitude
		/// and under 0.0003 of temperature or humidity. Cost: about 0.4 µs a point, nine gradient-noise
		/// samples — a quarter to half a second on a 2048 × 1024 globe bake.
		/// </para>
		/// </remarks>
		public PlanetSurfacePoint SelectionPoint(Vector3 direction, in PlanetSurfacePoint point)
		{
			SelectionNoise(direction, out float heightNoise, out float temperatureNoise, out float humidityNoise);

			float earthScale = Mathf.Max(1e-3f, ReliefMetres / PlanetSurface.EarthReliefMetres);
			float altitude = point.AltitudeMetres;
			float reach = Mathf.Min(SelectionHeightFraction * Mathf.Abs(altitude), SelectionHeightMetres * earthScale);
			float movedAltitude = altitude + reach * heightNoise;
			float normalized = HeightOfAltitude(movedAltitude);

			ClimateSample climate = point.Climate;
			climate.Temperature = Nudge(climate.Temperature, temperatureNoise, SelectionTemperature);
			climate.Humidity = Nudge(climate.Humidity, humidityNoise, SelectionHumidity);
			climate.ElevationTier = ClimateSettings.TierForHeight(normalized, Parameters.ElevationBoundaries);

			return new PlanetSurfacePoint
			{
				Height = point.Height,
				AltitudeMetres = movedAltitude,
				NormalizedHeight = normalized,
				Climate = climate,
			};
		}

		/// <summary>
		/// A −1…1 reading moved by <paramref name="noise"/> × <paramref name="amplitude"/>, tapered over
		/// the last <paramref name="amplitude"/> of the scale so it never passes either end.
		/// </summary>
		private static float Nudge(float value, float noise, float amplitude)
		{
			float room = Mathf.Clamp01((1f - Mathf.Abs(value)) / amplitude);
			return value + noise * amplitude * room;
		}

		/// <summary>
		/// The biome for an honest point, chosen from its <see cref="SelectionPoint"/>: the one place every
		/// <c>BiomeAt</c> overload decides, so the globe, the scenes and the names cannot disagree.
		/// </summary>
		public BiomeTemplate SelectBiome(Vector3 direction, in PlanetSurfacePoint point)
		{
			PlanetSurfacePoint chosen = SelectionPoint(direction, point);
			return BiomeResolver.Select(chosen.NormalizedHeight, chosen.Climate, Conditions);
		}

		/// <summary>
		/// Temperature at a point: the ground under the star at noon, cooled toward the pole by the sun's noon
		/// altitude, cooled again by how far above sea level the ground stands, and nudged by the
		/// regional wobble.
		/// </summary>
		/// <param name="latitudeDegrees">Latitude of the point.</param>
		/// <param name="direction">The same point as a unit vector, for the regional term.</param>
		/// <param name="altitudeMetres">Metres above sea level. Below it costs nothing: the sea is the sea.</param>
		public float TemperatureAt(double latitudeDegrees, Vector3 direction, float altitudeMetres)
		{
			float latitude = system != null && body != null
				? (float)CelestialMath.LatitudeTemperature(system, body, 0.0, latitudeDegrees)
				: 0f;
			// Clamped here, once, with every term in.
			return Mathf.Clamp(SubSolarTemperature
				+ latitude
				- Mathf.Max(0f, altitudeMetres) * LapsePerMetre
				+ RegionalOffset(direction), -1f, 1f);
		}

		/// <summary>
		/// An altitude in metres as the biome system's normalised height, water line at
		/// <see cref="ClimateModel.DefaultWaterSurfaceHeight"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>From the metres, not from the raw field.</b> The surface field is squeezed toward sea
		/// level by <see cref="PlanetSurface.LandHypsometry"/>, so its median land point sits a long
		/// way up the raw range while standing barely a kilometre above the water. Normalising the
		/// raw field instead would put ordinary coastal plain in the alpine elevation tiers, and
		/// every scene cut from a continent would come out named for a mountain it is not on.
		/// </para>
		/// <para>
		/// <b>Above water, the tiers are landform bands at physical altitudes (2026-10-02).</b> Land
		/// used to be spread evenly from the water line to the summit, so on an Earth-sized world the
		/// coast tier ran from 0 to 460 m and a third of all land could only ever be beach, estuary
		/// or rocky coast. Now each tier edge stands at a real altitude
		/// (<see cref="ClimateSettings.TierEdgeMetres"/>): the shore to 30 m, lowland and plateau to
		/// 1.5 km, highland to 2.5 km, mountain to 3.5 km, alpine to 4.5 km, nival above. The tiers
		/// are where the ground stands, not what grows on it — the treeline and the snowline are the
		/// temperature's job, which already falls with altitude at the body's own lapse rate — so the
		/// edges are landform edges (the reasons are on <see cref="ClimateSettings.TierEdgeMetres"/>).
		/// </para>
		/// <para>
		/// <b>Under water, the tiers are the ocean's own zones.</b> The sea floor used to be spread
		/// evenly from the water line to the deepest trench, so the coastal tier — "tidal pools,
		/// kelp beds" — reached 2.6 km down on a world with a 16 km trench, and a scene cut from a
		/// kilometre of open water 141 km offshore was named "Shallow". Now the coastal tier is
		/// the continental shelf, to 200 m; the next is the slope and rise, to 4 km; the lowest is
		/// the abyssal floor and the trenches. The metres are Earth's, scaled by the body's size
		/// exactly as <see cref="PlanetSurface.OceanDepthMetres"/> scales its floor, so the tiers
		/// follow the body's own shelf wherever it is.
		/// </para>
		/// </remarks>
		public float HeightOfAltitude(float altitudeMetres)
		{
			const float Water = ClimateModel.DefaultWaterSurfaceHeight;

			// The climate's own tier edges, so a climate that moves them keeps its zones.
			float[] boundaries = Parameters.ElevationBoundaries != null && Parameters.ElevationBoundaries.Length == 10
				? Parameters.ElevationBoundaries
				: ClimateSettings.DefaultElevationBoundaries;
			// Metres are Earth's, scaled by the body's relief exactly as its sea floor and summit are.
			float earthScale = Mathf.Max(1e-3f, ReliefMetres / PlanetSurface.EarthReliefMetres);

			if (altitudeMetres >= 0f)
			{
				return LandHeight(altitudeMetres / earthScale, HighestMetres / earthScale, boundaries);
			}

			float shelfTier = Mathf.Min(boundaries[2], Water);
			float abyssalTier = Mathf.Min(boundaries[1], shelfTier);

			float depth = -altitudeMetres / earthScale;
			if (depth <= ShelfEdgeMetres)
			{
				return Mathf.Lerp(Water, shelfTier, depth / ShelfEdgeMetres);
			}
			if (depth <= AbyssalMetres)
			{
				return Mathf.Lerp(shelfTier, abyssalTier, (depth - ShelfEdgeMetres) / (AbyssalMetres - ShelfEdgeMetres));
			}
			float deepest = PlanetSurface.OceanKnotDepthMetres[PlanetSurface.OceanKnotDepthMetres.Length - 1];
			return Mathf.Lerp(abyssalTier, 0f, (depth - AbyssalMetres) / (deepest - AbyssalMetres));
		}

		/// <summary>
		/// Earth-equivalent metres above sea level as the biome system's normalised height: piecewise
		/// linear through <see cref="ClimateSettings.TierEdgeMetres"/>, so each tier's ground fills
		/// that tier's own band of normalised height.
		/// </summary>
		/// <param name="earthMetres">Altitude above sea level in Earth metres (the body's metres over its relief scale).</param>
		/// <param name="summitEarthMetres">The body's highest ground in the same metres: the top of the nival band.</param>
		/// <param name="boundaries">The climate's ten tier boundaries.</param>
		/// <remarks>
		/// Anchored at the water line like the sea floor, and kept monotone, so a climate that has
		/// moved its boundaries about still gets a height that only ever rises with the ground.
		/// Ground above the summit — local detail on top of the planet's highest peak — stays at 1.
		/// </remarks>
		public static float LandHeight(float earthMetres, float summitEarthMetres, float[] boundaries)
		{
			const float Water = ClimateModel.DefaultWaterSurfaceHeight;
			float[] metres = ClimateSettings.TierEdgeMetres;
			// The top of the nival band is the body's own summit; never at or below the alpine edge.
			float summit = Mathf.Max(metres[8] + 1f, summitEarthMetres);
			float below = Water;
			float belowMetres = 0f;
			for (int edge = 4; edge <= 9; edge++)
			{
				float top = Mathf.Clamp(Mathf.Max(boundaries[edge], below), 0f, 1f);
				float topMetres = edge == 9 ? summit : metres[edge];
				if (earthMetres < topMetres)
				{
					return Mathf.Lerp(below, top, (earthMetres - belowMetres) / (topMetres - belowMetres));
				}
				below = top;
				belowMetres = topMetres;
			}
			return below;
		}

		/// <summary>Where the continental shelf ends on Earth, in metres: the bottom of the coastal tier.</summary>
		public const float ShelfEdgeMetres = 200f;

		/// <summary>Where the bathyal zone gives way to the abyssal on Earth, in metres: the top of the lowest tier.</summary>
		public const float AbyssalMetres = 4000f;

		/// <summary>Everything about one point on the globe.</summary>
		public PlanetSurfacePoint At(double latitudeDegrees, double longitudeDegrees)
		{
			Vector3 direction = PlanetSurface.Direction(latitudeDegrees, longitudeDegrees);
			float height = PlanetSurface.Height(Seed, direction, Cratering);
			float altitude = PlanetSurface.AltitudeFromHeight(height, Profile, ReliefMetres);
			return Point(latitudeDegrees, direction, height, altitude);
		}

		/// <summary>
		/// Everything about one point, at an altitude the caller already knows.
		/// </summary>
		/// <param name="direction">The point as a unit vector from the body's centre.</param>
		/// <param name="altitudeMetres">Metres above sea level, in the planet's own metres.</param>
		/// <remarks>
		/// For the scene generator, whose ground is the planet's plus local detail: a ridge the
		/// globe is too coarse to show is still colder than the valley beside it, and the biome
		/// painted on it has to say so. <see cref="PlanetSurfacePoint.Height"/> is left at the raw
		/// field under the point, since local detail has no raw-field equivalent.
		/// </remarks>
		public PlanetSurfacePoint At(Vector3 direction, float altitudeMetres)
		{
			direction = direction.normalized;
			double latitude = LatitudeOf(direction);
			float height = PlanetSurface.Height(Seed, direction, Cratering);
			return Point(latitude, direction, height, altitudeMetres);
		}

		/// <summary>The latitude of a unit direction, in degrees, exactly as <see cref="At(Vector3, float)"/> takes it.</summary>
		public static double LatitudeOf(Vector3 unitDirection)
		{
			return Mathf.Asin(Mathf.Clamp(unitDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
		}

		/// <summary>
		/// The climate at a point and altitude, with the moisture term supplied by the caller.
		/// </summary>
		/// <param name="direction">The point as a vector from the body's centre; normalised here, as <see cref="At(Vector3, float)"/> does.</param>
		/// <param name="altitudeMetres">Metres above sea level, in the planet's own metres.</param>
		/// <param name="moistureAnomaly">
		/// <see cref="MoistureModel.Anomaly(double, Vector3)"/> at this direction, or a value
		/// interpolated from it. It depends on the direction alone, so a caller that samples a small
		/// area often can grid it once and pass it here.
		/// </param>
		/// <param name="normalizedHeight">The height as the biome system reads it (<see cref="HeightOfAltitude"/>).</param>
		/// <remarks>
		/// <para>
		/// For a running scene that stands on this body (<see cref="ScenePlacementClimate"/>). The
		/// generator painted the scene's biomes from <see cref="At(Vector3, float)"/> at each point's
		/// direction and altitude; this is the same arithmetic through the same private path, minus
		/// the surface-field lookup the climate never reads, so with the exact moisture anomaly it
		/// returns exactly the climate <see cref="At(Vector3, float)"/> does.
		/// </para>
		/// <para>Allocation-free: a few noise samples for the regional term and one latitude-temperature call.</para>
		/// </remarks>
		public ClimateSample ClimateAt(Vector3 direction, float altitudeMetres, float moistureAnomaly, out float normalizedHeight)
		{
			direction = direction.normalized;
			return Climate(LatitudeOf(direction), direction, altitudeMetres, moistureAnomaly, out normalizedHeight);
		}

		private PlanetSurfacePoint Point(double latitudeDegrees, Vector3 direction, float height, float altitude)
		{
			return Point(latitudeDegrees, direction, height, altitude, Moisture.Terrain);
		}

		private PlanetSurfacePoint Point<T>(double latitudeDegrees, Vector3 direction, float height, float altitude, in T terrain)
			where T : struct, IMoistureTerrain
		{
			ClimateSample climate = Climate(latitudeDegrees, direction, altitude,
				Moisture.Anomaly(latitudeDegrees, direction, terrain), out float normalized);
			return new PlanetSurfacePoint
			{
				Height = height,
				AltitudeMetres = altitude,
				NormalizedHeight = normalized,
				Climate = climate,
			};
		}

		/// <summary>
		/// The one place a point's climate is assembled: every public path — the globe, the scene
		/// generator, a running scene — comes through here, so none can drift from the others.
		/// </summary>
		private ClimateSample Climate(double latitudeDegrees, Vector3 direction, float altitude, float moistureAnomaly, out float normalized)
		{
			normalized = HeightOfAltitude(altitude);
			float temperature = TemperatureAt(latitudeDegrees, direction, altitude);
			return new ClimateSample
			{
				Temperature = temperature,
				Humidity = Humidity(temperature, normalized, moistureAnomaly),
				ElevationTier = ClimateSettings.TierForHeight(normalized, Parameters.ElevationBoundaries),
			};
		}

		/// <summary>The curves' humidity for a temperature and height, plus the wind's share, on the scale.</summary>
		private float Humidity(float temperature, float normalizedHeight, float moistureAnomaly)
		{
			return Mathf.Clamp(Parameters.HumidityAt(temperature, normalizedHeight) + moistureAnomaly, -1f, 1f);
		}

		/// <summary>
		/// Humidity at a point: the climate's curves for its temperature and height, plus where the
		/// prevailing wind brings the rain.
		/// </summary>
		/// <param name="latitudeDegrees">Latitude of the point.</param>
		/// <param name="direction">The same point as a unit vector.</param>
		/// <param name="temperature">Its temperature, from <see cref="TemperatureAt"/>.</param>
		/// <param name="normalizedHeight">Its height as the biome system reads it.</param>
		/// <remarks>
		/// <para>
		/// <b>Two halves, added.</b> The curves (<see cref="ClimateParameters.HumidityAt"/>) say what
		/// heat and cold do — hot ground loses its water faster than rain brings it, cold air holds
		/// little — and what the world's share of ocean makes of its air overall. They cannot say
		/// where the water comes from, so on their own every point at one temperature and height had
		/// one humidity and the biome envelopes collapsed onto a curve. <see cref="MoistureModel"/>
		/// is that half: fetch from the sea along the wind, rain shadows behind ranges, the wet
		/// equatorial trough and the dry subtropical highs.
		/// </para>
		/// <para>
		/// The moisture term depends on the direction alone, never on the caller's height, so a
		/// scene that samples it on a coarse grid of its own footprint (see
		/// <c>ScenePlacementClimate</c>) gets exactly what this gives at those points.
		/// </para>
		/// </remarks>
		public float HumidityAt(double latitudeDegrees, Vector3 direction, float temperature, float normalizedHeight)
		{
			return Humidity(temperature, normalizedHeight, Moisture.Anomaly(latitudeDegrees, direction));
		}

		/// <summary>
		/// The biome a point would carry, from the world's own conditions. Null when no biome
		/// template is registered or none fits.
		/// </summary>
		/// <remarks>
		/// Every overload hands back the honest <paramref name="point"/> and chooses from its
		/// <see cref="SelectionPoint"/> (<see cref="SelectBiome"/>), so biome borders meander while the
		/// climate a caller reads stays the climate.
		/// </remarks>
		public BiomeTemplate BiomeAt(double latitudeDegrees, double longitudeDegrees, out PlanetSurfacePoint point)
		{
			point = At(latitudeDegrees, longitudeDegrees);
			return SelectBiome(PlanetSurface.Direction(latitudeDegrees, longitudeDegrees), point);
		}

		/// <summary>The biome at a point whose altitude the caller already knows. See <see cref="At(Vector3, float)"/>.</summary>
		public BiomeTemplate BiomeAt(Vector3 direction, float altitudeMetres, out PlanetSurfacePoint point)
		{
			point = At(direction, altitudeMetres);
			return SelectBiome(direction.normalized, point);
		}

		/// <summary>
		/// The biome at a point whose raw height and altitude the caller has already worked out.
		/// </summary>
		/// <remarks>
		/// For the globe bake, which asks two million points and has already sampled the surface
		/// field at each for its own shading: asking it again here would double the costliest part
		/// of the bake for an answer it already holds.
		/// </remarks>
		public BiomeTemplate BiomeAt(double latitudeDegrees, Vector3 direction, float height, float altitudeMetres, out PlanetSurfacePoint point)
		{
			point = Point(latitudeDegrees, direction, height, altitudeMetres);
			return SelectBiome(direction, point);
		}

		/// <summary>
		/// The same, with the moisture walk reading the ground from <paramref name="terrain"/> — a
		/// <see cref="MoistureRowTerrain"/> the globe bake fills once per row, so the upwind samples
		/// cost a lookup instead of the noise.
		/// </summary>
		public BiomeTemplate BiomeAt<T>(double latitudeDegrees, Vector3 direction, float height, float altitudeMetres, in T terrain, out PlanetSurfacePoint point)
			where T : struct, IMoistureTerrain
		{
			point = Point(latitudeDegrees, direction, height, altitudeMetres, terrain);
			return SelectBiome(direction, point);
		}
	}
}
