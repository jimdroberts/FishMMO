using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Atlas;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// A world's terrain, as a function of its seed and a direction from its centre.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The function is the truth; every texture is a bake of it.</b> The server samples it to
	/// find how high a scene stands, the scene generator samples it across a rectangle at whatever
	/// precision it likes, and the globe's surface texture is the same function written into an
	/// image. Nothing can disagree about where a coastline is, because there is only one answer and
	/// no stored copy to drift from it. The same shape as the weather: one pure function, several
	/// presenters.
	/// </para>
	/// <para>
	/// <b>Sampled in three dimensions on the unit sphere</b>, never on a lat/long grid. A 2D grid
	/// wrapped round a ball has a seam down one meridian and a singularity at each pole, and
	/// terrain generated on one shows both. A direction has neither: the noise simply does not know
	/// the sphere is there.
	/// </para>
	/// <para>
	/// Determinism is the point, so the lattice hash is written out here in integer arithmetic
	/// rather than taken from <c>Mathf.PerlinNoise</c>, whose output Unity has never promised to
	/// keep identical between platforms or versions. A server and a browser client must agree about
	/// a mountain for as long as the world exists.
	/// </para>
	/// </remarks>
	public static class PlanetSurface
	{
		// ── The shape of a world ──────────────────────────────────────

		/// <summary>
		/// How many continent-sized features fit across the world.
		/// </summary>
		/// <remarks>
		/// Low, and only three octaves, because the continent term decides land from sea and
		/// nothing else. At 2.2 with five octaves the same generator produced a scatter of islands
		/// rather than continents — the finer octaves were deciding coastlines that the base octave
		/// should own. Mountains and detail ride on top; they must not vote on where the ocean is.
		/// </remarks>
		private const float ContinentFrequency = 1.0f;
		private const int ContinentOctaves = 3;

		/// <summary>
		/// How decisively the continent field separates land from sea.
		/// </summary>
		/// <remarks>
		/// fBm piles up around its midpoint, so without this a world is mostly coastline: endless
		/// shallow shelf and marsh with no interior and no deep ocean. Pushing the field away from
		/// its middle gives continents an inside and oceans a floor.
		/// </remarks>
		private const float ContinentContrast = 2.2f;

		/// <summary>How far the continent field is dragged sideways, which is what stops coastlines looking like contours.</summary>
		/// <remarks>
		/// 0.28. At 0.55 the warp displaced by more than a quarter of the sphere's radius and tore
		/// the continents into a lacy web of filaments — marbling, not geography. A warp is meant
		/// to roughen a coast, not to shred the landmass behind it.
		/// </remarks>
		private const float WarpStrength = 0.28f;
		private const float WarpFrequency = 1.5f;

		private const float MountainFrequency = 6.0f;
		private const int MountainOctaves = 5;

		private const float DetailFrequency = 17f;
		private const int DetailOctaves = 3;

		/// <summary>
		/// How much of a world's cratering survives, by how much air it has to erode it.
		/// </summary>
		/// <remarks>
		/// Craters are the default state of a solid surface in a solar system full of debris. What
		/// removes them is weather, plate tectonics and volcanism — so an airless body keeps every
		/// impact it has ever taken, which is why the Moon is covered in them and Earth has about
		/// two hundred left. A thin atmosphere erodes slowly; anything thicker erases them.
		/// </remarks>
		public static float CrateringOf(AtmosphereKind atmosphere)
		{
			switch (atmosphere)
			{
				case AtmosphereKind.None: return 1f;
				case AtmosphereKind.Thin: return 0.45f;
				case AtmosphereKind.Standard: return 0.05f;
				default: return 0f;
			}
		}

		/// <summary>
		/// Normalised height in a direction from the body's centre: 0 the deepest trench, 1 the
		/// highest summit.
		/// </summary>
		/// <param name="seed">The world's seed. The same seed always gives the same world.</param>
		/// <param name="direction">Any non-zero direction; normalised here.</param>
		/// <param name="cratering">
		/// How much impact cratering survives, from <see cref="CrateringOf"/>. Zero is a world
		/// whose weather has erased it.
		/// </param>
		public static float Height(uint seed, Vector3 direction, float cratering)
		{
			float height = Height(seed, direction);
			return cratering > 0.001f
				? Mathf.Clamp01(height + Craters(direction.normalized, seed ^ 0x0C0FFEE1u) * cratering * CraterDepth)
				: height;
		}

		/// <summary>How deep the crater field cuts, as a fraction of the whole height range.</summary>
		private const float CraterDepth = 0.5f;

		public static float Height(uint seed, Vector3 direction)
		{
			Vector3 p = direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.up;

			/* Dragged sideways before the continents are drawn. Undragged fBm gives blobs with
			 * smooth, rounded coasts; warping the input by another noise field is what produces
			 * inlets, peninsulas and the ragged edges a real coast has. */
			Vector3 warp = new Vector3(
				Noise(p * WarpFrequency, seed ^ 0x1A2B3C4Du) - 0.5f,
				Noise(p * WarpFrequency, seed ^ 0x5E6F7A8Bu) - 0.5f,
				Noise(p * WarpFrequency, seed ^ 0x9CADBEEFu) - 0.5f);
			Vector3 warped = p + warp * WarpStrength;

			// Continents: the slow shape of the world, and the only term that decides land from sea.
			float continent = Fbm(warped * ContinentFrequency, ContinentOctaves, seed);
			continent = Mathf.Clamp01(0.5f + (continent - 0.5f) * ContinentContrast);

			/* Mountains ride the continents and are tallest near their edges. That is where they
			 * are on a real world too: ranges stand along convergent margins — the Andes, the
			 * Rockies, the Cascades — because that is where one plate is being driven under
			 * another, while continental interiors are old, eroded and flat. */
			float edge = 1f - Mathf.Abs(continent - 0.5f) * 2f;
			float uplift = Mathf.SmoothStep(0f, 1f, continent) * Mathf.SmoothStep(0.15f, 0.95f, edge);
			float mountains = Ridged(warped * MountainFrequency, MountainOctaves, seed ^ 0xD00DFEEDu);

			float detail = Fbm(p * DetailFrequency, DetailOctaves, seed ^ 0xFACEB00Cu) - 0.5f;

			float height = continent * 0.62f + mountains * uplift * 0.33f + detail * 0.05f;
			return Mathf.Clamp01(height);
		}

		/// <summary>Normalised height at a latitude and longitude in degrees.</summary>
		public static float HeightAt(uint seed, double latitudeDegrees, double longitudeDegrees, float cratering = 0f)
		{
			return Height(seed, Direction(latitudeDegrees, longitudeDegrees), cratering);
		}

		// ── Lat/long and directions ───────────────────────────────────

		/// <summary>
		/// A direction from the body's centre for a latitude and longitude in degrees.
		/// </summary>
		/// <remarks>
		/// <b>Delegated to <see cref="AtlasGeometry.ToUnit"/>, and it must stay that way.</b> The
		/// atlas decides where everything on a globe is — a scene's latitude, the rectangle it was
		/// cut from, the routes between them — so a second convention here would not be a
		/// disagreement about maths but about geography. Written out independently these two used
		/// opposite axes for longitude, exactly a quarter turn apart, which would have baked a
		/// surface texture showing one hemisphere while scenes were cut from another, with nothing
		/// anywhere reporting a fault.
		/// </remarks>
		public static Vector3 Direction(double latitudeDegrees, double longitudeDegrees)
		{
			return AtlasGeometry.ToUnit(latitudeDegrees, longitudeDegrees).ToVector3();
		}

		/// <summary>The latitude and longitude a direction points at, in degrees. The exact inverse of <see cref="Direction"/>.</summary>
		public static void LatLong(Vector3 direction, out double latitudeDegrees, out double longitudeDegrees)
		{
			Vector3 p = direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.up;
			AtlasGeometry.FromUnit(new Vector3d(p.x, p.y, p.z), out latitudeDegrees, out longitudeDegrees);
		}

		// ── Sea level, derived from the ocean fraction ────────────────

		/// <summary>How many directions the sea-level search samples. Fibonacci-spaced, so they cover the sphere evenly.</summary>
		public const int SeaLevelSamples = 8192;

		/// <summary>What one sweep of a world's surface found: where the sea sits, and how far the ground ranges.</summary>
		public struct PlanetProfile
		{
			/// <summary>Normalised height of the ocean surface.</summary>
			public float SeaLevel;
			/// <summary>The lowest normalised height found anywhere.</summary>
			public float Lowest;
			/// <summary>The highest normalised height found anywhere.</summary>
			public float Highest;

			/// <summary>The part of the 0..1 range this world's ground actually occupies.</summary>
			public float Range => Mathf.Max(1e-4f, Highest - Lowest);
		}

		private static readonly Dictionary<long, PlanetProfile> profiles = new Dictionary<long, PlanetProfile>();

		/// <summary>
		/// A world's sea level and the range its ground occupies, from one sweep of the sphere.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Sea level is derived, not authored. <see cref="WorldBody.Water"/> already says how much
		/// of a world is ocean; the sea level that produces it is simply that quantile of the
		/// heightmap. Move the ocean fraction and every coastline moves with it, and there is no
		/// second number to keep in step by hand.
		/// </para>
		/// <para>
		/// The range comes from the same sweep because it costs nothing more and is needed for the
		/// same reason. Octaves of noise pile up around their midpoint rather than filling 0..1 —
		/// measured, a world uses about 40% of the range — so reading the raw value as a fraction
		/// of the world's relief would put its highest summit at 2.8 km and no scene would ever be
		/// in real mountains. Stretching by the range that was actually found gives Earth-like
		/// numbers without anything being tuned to make it so.
		/// </para>
		/// </remarks>
		public static PlanetProfile Profile(uint seed, float waterFraction, float cratering = 0f)
		{
			float water = Mathf.Clamp01(waterFraction);
			/* Cratering is part of the key: it changes the surface, so it changes where the sea
			 * sits and how far the ground ranges. A cached profile measured without craters would
			 * put an airless moon's datum in the wrong place. */
			long key = ((long)seed << 24) | ((long)Mathf.RoundToInt(water * 1000f) << 8) | (long)Mathf.RoundToInt(Mathf.Clamp01(cratering) * 100f);
			if (profiles.TryGetValue(key, out PlanetProfile cached))
			{
				return cached;
			}

			var heights = new float[SeaLevelSamples];
			for (int i = 0; i < SeaLevelSamples; i++)
			{
				heights[i] = Height(seed, FibonacciDirection(i, SeaLevelSamples), cratering);
			}
			Array.Sort(heights);

			var profile = new PlanetProfile
			{
				Lowest = heights[0],
				Highest = heights[SeaLevelSamples - 1],
			};
			/* A dry world's datum is its median ground, not its floor.
			 *
			 * With no ocean there is no sea level to find, but altitudes still have to be measured
			 * from somewhere — and taking the lowest point put every square metre of a dry world
			 * above its own datum, so a Mars-like body read as 38 km of unbroken highland. Mars
			 * itself quotes elevations against its mean radius for exactly this reason, which is
			 * what the median is. */
			profile.SeaLevel = water <= 0f ? heights[SeaLevelSamples / 2]
				: water >= 1f ? profile.Highest
				: heights[Mathf.Clamp(Mathf.RoundToInt(water * (SeaLevelSamples - 1)), 0, SeaLevelSamples - 1)];

			profiles[key] = profile;
			return profile;
		}

		/// <summary>The normalised height the ocean surface sits at.</summary>
		public static float SeaLevel(uint seed, float waterFraction, float cratering = 0f) => Profile(seed, waterFraction, cratering).SeaLevel;

		/// <summary>A body's profile, with its own cratering taken into account.</summary>
		public static PlanetProfile ProfileOf(uint seed, WorldBody body)
		{
			return Profile(seed, body != null ? body.Water : 0.7f,
				body != null ? CrateringOf(body.Atmosphere) : 0f);
		}

		/// <summary>Forgets every cached profile. For tests and for tools that change a seed.</summary>
		public static void ClearCache() => profiles.Clear();

		/// <summary>
		/// Evenly spaced directions over the whole sphere.
		/// </summary>
		/// <remarks>
		/// The Fibonacci spiral, not a lat/long grid: a grid crowds its samples at the poles, so a
		/// quantile taken from one would weigh the ice caps many times more heavily than the
		/// tropics and put sea level in the wrong place.
		/// </remarks>
		public static Vector3 FibonacciDirection(int index, int count)
		{
			double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
			double y = count > 1 ? 1.0 - 2.0 * index / (count - 1.0) : 0.0;
			double radius = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
			double theta = golden * index;
			return new Vector3((float)(Math.Cos(theta) * radius), (float)y, (float)(Math.Sin(theta) * radius));
		}

		// ── Relief in metres ──────────────────────────────────────────

		/// <summary>Total relief of an Earth-sized world, trench floor to summit, in metres.</summary>
		/// <remarks>Earth runs about 11 km below sea level to 8.8 km above it.</remarks>
		public const float EarthReliefMetres = 20000f;

		/// <summary>The radius that relief is quoted for, in kilometres.</summary>
		public const float EarthRadiusKm = 6371f;

		/// <summary>
		/// How relief grows with radius: relief ∝ radius^0.36.
		/// </summary>
		/// <remarks>
		/// Fitted to real bodies from Phobos to Earth, and an independent least-squares fit over
		/// them lands on 0.367 — so this is measurement, not a shape chosen for convenience.
		/// </remarks>
		public const float ReliefRadiusExponent = 0.36f;

		/// <summary>The most relief a body gets as a fraction of its own radius.</summary>
		/// <remarks>
		/// A safety rail rather than a model. Past roughly a third of its radius a body stops being
		/// a sphere with mountains on it and becomes a lump, and the equirectangular projection —
		/// and everything downstream that assumes a globe — stops meaning anything.
		/// </remarks>
		public const float MaximumReliefFraction = 0.35f;

		/// <summary>
		/// How much height a world has between its lowest and highest ground, in metres.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Bodies here run from about 5 km to 1000 km of radius, so this has to hold over three
		/// orders of magnitude. The absolute relief <em>grows</em> with size while the relief
		/// relative to the radius <em>falls</em> steeply, and both parts matter: a 5 km rock is a
		/// lumpy potato whose hills are a third of its own radius but only 1.5 km tall, while Earth
		/// has 20 km of relief that is a third of one percent of it.
		/// </para>
		/// <para>
		/// Two earlier attempts were wrong in opposite directions. Scaling by 1/radius — from the
		/// strength limit, since weaker gravity lets a mountain stand taller — gave the Moon 73 km
		/// of mountains; it has 20, because small bodies run out of interior heat to build with
		/// long before they run out of structural headroom. Capping that at a flat 30 km then gave
		/// a <b>5 km body 30 km of relief</b>, six times its own radius.
		/// </para>
		/// <para>
		/// Measured against real bodies this lands close across the whole range: Phobos 2.02 km
		/// against a real 2.0, the Moon 12.5 against 20, Earth 20 by construction, and a 5 km rock
		/// 1.5 km — lumpy, as it should be, without being turned inside out.
		/// </para>
		/// </remarks>
		public static float ReliefMetres(WorldBody body)
		{
			float radiusKm = body != null && body.SkyRadiusKm > 0.01f ? body.SkyRadiusKm : EarthRadiusKm;
			float reliefKm = EarthReliefMetres / 1000f * Mathf.Pow(radiusKm / EarthRadiusKm, ReliefRadiusExponent);
			return Mathf.Min(reliefKm, radiusKm * MaximumReliefFraction) * 1000f;
		}

		/// <summary>
		/// The altitude of a point above the body's sea level, in metres.
		/// </summary>
		/// <param name="seed">The world's seed.</param>
		/// <param name="body">The body, for its size and its ocean fraction. Null is Earth-like.</param>
		/// <param name="direction">Where on the globe.</param>
		public static float AltitudeMetres(uint seed, WorldBody body, Vector3 direction)
		{
			float cratering = body != null ? CrateringOf(body.Atmosphere) : 0f;
			return AltitudeFromHeight(Height(seed, direction, cratering), ProfileOf(seed, body), ReliefMetres(body));
		}

		/// <summary>
		/// Metres above sea level for a height already sampled.
		/// </summary>
		/// <remarks>
		/// Shared so the surface bake and the terrain generator cannot disagree about how high the
		/// ground is. They each did this arithmetic themselves once, and when the land curve below
		/// was added to one of them the other went on drawing continents four kilometres up.
		/// </remarks>
		public static float AltitudeFromHeight(float height, in PlanetProfile profile, float relief)
		{
			float above = height - profile.SeaLevel;

			if (above <= 0f)
			{
				// Ocean floor, straight through: trenches really are spread across their range.
				return above / profile.Range * relief;
			}

			/* Land is squeezed down toward sea level, and that is not a fudge.
			 *
			 * A single fBm has a roughly bell-shaped height distribution, so putting sea level at
			 * the median spreads land evenly from the shore to the summit — which measured a MEAN
			 * land elevation of 4520 m on this world. Earth's is 840 m against a summit of 8848.
			 * Earth's hypsometric curve is bimodal: continental crust floats on a shelf near sea
			 * level and ocean basins sit kilometres below, with very little in between, and a
			 * fractal cannot produce two modes at any settings.
			 *
			 * Left linear, every continent read as a plateau four kilometres up, the lapse rate
			 * took nearly a whole unit of temperature off it, and a temperate world came out a
			 * snowball with ice to the equator.
			 */
			float top = Mathf.Max(1e-4f, profile.Highest - profile.SeaLevel);
			float shaped = Mathf.Pow(Mathf.Clamp01(above / top), LandHypsometry);
			return shaped * top / profile.Range * relief;
		}


		/// <summary>
		/// How hard land is pushed down toward sea level, standing in for a bimodal hypsometry.
		/// </summary>
		/// <remarks>
		/// The mean of x^p over the land is 1/(p+1) of the summit, so 6 puts the average continent
		/// at about a seventh of the highest peak — roughly 1.3 km against a 9 km summit here,
		/// against Earth's 0.84 km against 8.85. Most of a world is coastal plain and a little of
		/// it is mountain, which is what the curve has to say.
		/// </remarks>
		public const float LandHypsometry = 6f;

		/// <summary>The altitude above sea level at a latitude and longitude, in metres.</summary>
		public static float AltitudeMetresAt(uint seed, WorldBody body, double latitudeDegrees, double longitudeDegrees)
		{
			return AltitudeMetres(seed, body, Direction(latitudeDegrees, longitudeDegrees));
		}

		// ── Craters ───────────────────────────────────────────────────

		/// <summary>The crater sizes drawn, largest first, as frequencies on the unit sphere.</summary>
		/// <remarks>
		/// Three sizes, because an impact record is scale-free: a few basins, many craters, and
		/// countless small ones. Each is a third the depth of the one above so the big features
		/// still read from orbit while the small ones give the surface its texture.
		/// </remarks>
		private static readonly float[] CraterFrequencies = { 4.5f, 11f, 26f };
		private static readonly float[] CraterWeights = { 1f, 0.45f, 0.18f };

		/// <summary>
		/// A crater field on the sphere, roughly -1..0.3: bowls with raised rims.
		/// </summary>
		/// <remarks>
		/// Cellular rather than noise-based. Craters are not a fractal surface — they are discrete
		/// round holes with rims, they overlap, and the newer ones cut the older. Summing octaves
		/// of smooth noise cannot make that shape at any settings, which is why an airless moon
		/// built from fBm alone reads as dunes.
		/// </remarks>
		private static float Craters(Vector3 p, uint seed)
		{
			float total = 0f;
			for (int octave = 0; octave < CraterFrequencies.Length; octave++)
			{
				total += Crater(p * CraterFrequencies[octave], seed + (uint)octave * 0x7F4A7C15u) * CraterWeights[octave];
			}
			return total;
		}

		/// <summary>One scale of craters: the deepest cut from any nearby impact.</summary>
		private static float Crater(Vector3 p, uint seed)
		{
			int bx = Mathf.FloorToInt(p.x), by = Mathf.FloorToInt(p.y), bz = Mathf.FloorToInt(p.z);
			float deepest = 0f;

			for (int oz = -1; oz <= 1; oz++)
			{
				for (int oy = -1; oy <= 1; oy++)
				{
					for (int ox = -1; ox <= 1; ox++)
					{
						int cx = bx + ox, cy = by + oy, cz = bz + oz;
						uint h = HashBits(cx, cy, cz, seed);

						// Not every cell has one, or the surface would be a regular lattice of
						// identical holes rather than a bombardment.
						if ((h & 0xFFu) < 96u)
						{
							continue;
						}

						// The impact point, jittered inside its cell, and its size.
						var centre = new Vector3(
							cx + ((h >> 8) & 0xFFu) / 255f,
							cy + ((h >> 16) & 0xFFu) / 255f,
							cz + ((h >> 24) & 0xFFu) / 255f);
						float radius = Mathf.Lerp(0.30f, 0.62f, ((h >> 4) & 0x0Fu) / 15f);

						float t = (p - centre).magnitude / radius;
						if (t >= 1f)
						{
							continue;
						}

						/* A bowl with a rim: parabolic floor, and a ring of ejecta thrown up just
						 * outside it. Real craters look like this because the material removed from
						 * the middle has to land somewhere. */
						float bowl = -(1f - t * t);
						float rim = Mathf.Exp(-((t - 0.86f) * (t - 0.86f)) / 0.006f) * 0.55f;
						float shape = bowl + rim;

						// The deepest wins, so a newer crater cuts through an older one instead of
						// the two averaging into a shallow dish.
						deepest = Mathf.Min(deepest, shape) + Mathf.Max(0f, rim) * 0.35f;
					}
				}
			}
			return Mathf.Clamp(deepest, -1f, 0.35f);
		}

		// ── Local detail ──────────────────────────────────────────────

		/// <summary>How far apart the coarsest local detail features are, in metres.</summary>
		public const float DetailFeatureMetres = 900f;

		/// <summary>Local detail height as a fraction of the body's total relief.</summary>
		/// <remarks>
		/// Proportional rather than absolute so a small moon is not given Earth-sized hills: at
		/// 0.5% of a 20 km relief that is about 100 m of local shape on an Earth-like world, and a
		/// few metres on a 5 km rock.
		/// </remarks>
		public const float DetailReliefFraction = 0.005f;

		/// <summary>
		/// Metre-scale ground shape for a point inside a scene, averaging to zero.
		/// </summary>
		/// <param name="seed">The body's terrain seed, mixed with whatever identifies the scene.</param>
		/// <param name="eastMetres">Metres east of the scene's centre.</param>
		/// <param name="northMetres">Metres north of the scene's centre.</param>
		/// <param name="body">The body, for how much relief it has to spend. Null is Earth-like.</param>
		/// <remarks>
		/// <para>
		/// In scene-local metres, not on the unit sphere, and that is a numerical necessity rather
		/// than a convenience. A feature 500 m across on an Earth-sized globe is a frequency of
		/// about 80,000 on the unit sphere; a float at that magnitude has a spacing of 0.008, so
		/// the fractional part the interpolation needs would be quantised into visible steps. In
		/// local metres the coordinates stay small and every bit of the mantissa does useful work.
		/// </para>
		/// <para>
		/// It averages to zero, so it roughens the ground without moving it: the scene still sits
		/// at the altitude the globe says it does, and the coastline is still where the map shows.
		/// </para>
		/// </remarks>
		public static float LocalDetailMetres(uint seed, float eastMetres, float northMetres, WorldBody body)
		{
			float amplitude = ReliefMetres(body) * DetailReliefFraction;
			var p = new Vector3(eastMetres / DetailFeatureMetres, 0.37f, northMetres / DetailFeatureMetres);
			// Centred on zero: Fbm runs 0..1 and this must not raise the ground.
			return (Fbm(p, 5, seed ^ 0x5CE4E7A1u) - 0.5f) * 2f * amplitude;
		}

		/// <summary>
		/// A smooth field over the sphere, 0..1, for anything that wants natural variation.
		/// </summary>
		/// <param name="frequency">How many features fit across the world. Low is continent-sized.</param>
		/// <remarks>
		/// Exposed because a boundary decided by latitude alone is a perfect circle, and nothing in
		/// nature is. The snowline is the obvious case: ocean currents, land and weather push a real
		/// ice cap into lobes and bays, and without a field like this the cap comes out as a band
		/// ruled across the map.
		/// </remarks>
		public static float FieldNoise(uint seed, Vector3 direction, float frequency, int octaves = 3)
		{
			Vector3 p = direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.up;
			return Fbm(p * Mathf.Max(0.001f, frequency), Mathf.Clamp(octaves, 1, 8), seed);
		}

		/// <summary>
		/// The same field, sampled at a point exactly as given: <b>not normalised</b>.
		/// </summary>
		/// <remarks>
		/// <see cref="FieldNoise"/> normalises first, which is right for anything asking a question
		/// about a direction and fatal for anything asking an anisotropic one. Scaling a unit
		/// vector to (x·0.45, y·7, z·0.45) to stretch features along the bands of a gas giant does
		/// nothing at all once it is normalised again — the stretch is exactly what normalising
		/// removes — and the result comes back a featureless wash.
		/// </remarks>
		public static float FieldNoiseAt(uint seed, Vector3 point, int octaves = 3)
		{
			return Fbm(point, Mathf.Clamp(octaves, 1, 8), seed);
		}

		// ── Noise ─────────────────────────────────────────────────────

		/// <summary>
		/// A lattice hash, written out so it cannot change under us.
		/// </summary>
		/// <remarks>
		/// The same construction the weather driver uses on its own 2D lattice, extended to three
		/// axes. Both exist rather than one being shared because the weather's is on a plane and
		/// this is on a sphere; keeping the arithmetic identical means a reader who has understood
		/// one has understood the other.
		/// </remarks>
		private static uint HashBits(int x, int y, int z, uint seed)
		{
			uint h = seed;
			h ^= (uint)x * 0x9E3779B1u;
			h ^= (uint)y * 0x85EBCA77u;
			h ^= (uint)z * 0xC2B2AE3Du;
			h ^= h >> 15;
			h *= 0x2545F491u;
			h ^= h >> 13;
			h *= 0xC2B2AE35u;
			h ^= h >> 16;
			return h;
		}

		/// <summary>
		/// The twelve edge-midpoint gradients of a cube: the classic Perlin set.
		/// </summary>
		/// <remarks>
		/// Deliberately not the six axis directions or a random sphere point. These twelve are
		/// evenly spread and none of them points along an axis, which is what keeps the result from
		/// showing the lattice it was built on.
		/// </remarks>
		private static readonly Vector3[] Gradients =
		{
			new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, -1f, 0f),
			new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, -1f),
			new Vector3(0f, 1f, 1f), new Vector3(0f, -1f, 1f), new Vector3(0f, 1f, -1f), new Vector3(0f, -1f, -1f),
		};

		/// <summary>The gradient at a lattice corner, dotted with the offset to the sample point.</summary>
		private static float GradientDot(int x, int y, int z, float dx, float dy, float dz, uint seed)
		{
			Vector3 g = Gradients[HashBits(x, y, z, seed) % 12u];
			return g.x * dx + g.y * dy + g.z * dz;
		}

		/// <summary>Quintic fade: zero first and second derivatives at the ends, so octaves do not crease.</summary>
		private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

		/// <summary>
		/// Gradient noise in three dimensions, 0..1.
		/// </summary>
		/// <remarks>
		/// Gradient noise, not value noise, and the difference is visible from orbit. Value noise
		/// interpolates a number stored at each lattice corner, so its features sit on the lattice
		/// and line up with the axes; at continent scale a sphere spans only a couple of cells, and
		/// what came out was a smooth axis-aligned band of ocean round the equator with land at
		/// both poles. Gradient noise stores a direction instead and is zero at every corner, so it
		/// has no preferred axis and no feature to give the lattice away.
		/// </remarks>
		private static float Noise(Vector3 p, uint seed)
		{
			float fx = Mathf.Floor(p.x), fy = Mathf.Floor(p.y), fz = Mathf.Floor(p.z);
			int ix = (int)fx, iy = (int)fy, iz = (int)fz;
			float dx = p.x - fx, dy = p.y - fy, dz = p.z - fz;
			float u = Fade(dx), v = Fade(dy), w = Fade(dz);

			float n000 = GradientDot(ix, iy, iz, dx, dy, dz, seed);
			float n100 = GradientDot(ix + 1, iy, iz, dx - 1f, dy, dz, seed);
			float n010 = GradientDot(ix, iy + 1, iz, dx, dy - 1f, dz, seed);
			float n110 = GradientDot(ix + 1, iy + 1, iz, dx - 1f, dy - 1f, dz, seed);
			float n001 = GradientDot(ix, iy, iz + 1, dx, dy, dz - 1f, seed);
			float n101 = GradientDot(ix + 1, iy, iz + 1, dx - 1f, dy, dz - 1f, seed);
			float n011 = GradientDot(ix, iy + 1, iz + 1, dx, dy - 1f, dz - 1f, seed);
			float n111 = GradientDot(ix + 1, iy + 1, iz + 1, dx - 1f, dy - 1f, dz - 1f, seed);

			float x00 = Mathf.Lerp(n000, n100, u), x10 = Mathf.Lerp(n010, n110, u);
			float x01 = Mathf.Lerp(n001, n101, u), x11 = Mathf.Lerp(n011, n111, u);
			// Gradient noise runs about -1..1; the rest of this file works in 0..1.
			return (Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w) + 1f) * 0.5f;
		}

		/// <summary>Fractional Brownian motion: octaves of noise, each finer and quieter, 0..1.</summary>
		private static float Fbm(Vector3 p, int octaves, uint seed)
		{
			float sum = 0f, amplitude = 0.5f, total = 0f;
			Vector3 q = p;
			for (int i = 0; i < octaves; i++)
			{
				sum += Noise(q, seed + (uint)i * 0x68BC21EBu) * amplitude;
				total += amplitude;
				q *= 2.02f;              // not exactly 2, so octaves do not line up on the lattice
				amplitude *= 0.5f;
			}
			return total > 0f ? sum / total : 0f;
		}

		/// <summary>
		/// Ridged multifractal, 0..1: mountain chains rather than scattered bumps.
		/// </summary>
		/// <remarks>
		/// Folding the noise about its midpoint turns smooth hills into creases, and creases join up
		/// into ranges. Each octave is weighted by the one above it, so detail appears on the ridges
		/// and not in the valleys — which is what makes the result read as eroded rock.
		/// </remarks>
		private static float Ridged(Vector3 p, int octaves, uint seed)
		{
			float sum = 0f, amplitude = 0.5f, total = 0f, weight = 1f;
			Vector3 q = p;
			for (int i = 0; i < octaves; i++)
			{
				float n = 1f - Mathf.Abs(Noise(q, seed + (uint)i * 0x9E3779B1u) * 2f - 1f);
				n *= n;
				n *= weight;
				weight = Mathf.Clamp01(n * 2f);
				sum += n * amplitude;
				total += amplitude;
				q *= 2.07f;
				amplitude *= 0.5f;
			}
			return total > 0f ? Mathf.Clamp01(sum / total) : 0f;
		}
	}
}
