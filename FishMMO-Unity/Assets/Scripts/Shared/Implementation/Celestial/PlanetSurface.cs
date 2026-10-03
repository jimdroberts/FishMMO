using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;

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

		/// <summary>How wide the continent field's rounding is where it saturates, in its own 0..1 units.</summary>
		/// <remarks>
		/// <para>
		/// The field used to be hard-clamped to 0..1, which leaves a kink where it reaches the
		/// clamp: full slope on one side, dead flat on the other. Seen from orbit that kink is a
		/// contour line thousands of kilometres long. A scene samples less than a hundredth of a
		/// field that smooth, so across a scene the contour is a straight line, and the kink
		/// became a ruler-straight cliff running the whole width of the terrain.
		/// </para>
		/// <para>
		/// A C1 knee removes the kink without moving anything that matters: the field only changes
		/// within this distance of 0 and 1, far from the 0.5 coastline. Measured on Arthis, 99.8%
		/// of the globe kept its land or sea.
		/// </para>
		/// </remarks>
		private const float ContinentKnee = 0.12f;

		/// <summary>
		/// The share of full mountain uplift that deep continental interiors keep.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This used to be an accident.</b> Uplift was written
		/// <c>Mathf.SmoothStep(0.15f, 0.95f, edge)</c>, meant as a smoothstep between those edges.
		/// Unity's <c>SmoothStep</c> takes (from, to, t) and interpolates between the first two, so
		/// the call returned 0.15 in the deepest interior and 0.95 at the coast, not 0 and 1.
		/// </para>
		/// <para>
		/// Fixing only the call gave interiors no mountains at all and dropped an Earth-like
		/// world's 99th-percentile land from 3.2 km to 1.4 km: flatter worlds, where the goal was
		/// taller mountains. So the floor stays, stated on purpose, at a share where interior
		/// ranges still rise well above the shield around them.
		/// </para>
		/// </remarks>
		private const float InteriorUplift = 0.2f;

		/// <summary>
		/// A clamp to 0..1 with its corners rounded over <paramref name="knee"/> either side, so
		/// the result's slope never jumps.
		/// </summary>
		public static float SoftClamp01(float value, float knee)
		{
			if (knee <= 0f)
			{
				return Mathf.Clamp01(value);
			}
			if (value <= -knee)
			{
				return 0f;
			}
			if (value < knee)
			{
				return (value + knee) * (value + knee) / (4f * knee);
			}
			if (value <= 1f - knee)
			{
				return value;
			}
			if (value < 1f + knee)
			{
				float rest = 1f + knee - value;
				return 1f - rest * rest / (4f * knee);
			}
			return 1f;
		}

		/// <summary>
		/// A smoothstep between two edges, 0..1.
		/// </summary>
		/// <remarks>
		/// <b>Not <c>Mathf.SmoothStep</c>.</b> Unity's interpolates between its first two arguments
		/// by the third, which is how mountain uplift came to be written wrongly here.
		/// </remarks>
		private static float Smooth(float edge0, float edge1, float x)
		{
			return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));
		}

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
		/// <para>
		/// <b>A standard atmosphere keeps none.</b> It used to keep 5%, meant as Earth's surviving
		/// two hundred. But the field is uniform, so 5% was not a few scattered craters, it was a
		/// faint copy of the whole bombardment everywhere at once. From orbit that did not show.
		/// Cut into a scene, every mountain came out ringed with circular rims hundreds of metres
		/// high.
		/// </para>
		/// </remarks>
		public static float CrateringOf(AtmosphereKind atmosphere)
		{
			switch (atmosphere)
			{
				case AtmosphereKind.None: return 1f;
				case AtmosphereKind.Thin: return 0.45f;
				default: return 0f;
			}
		}

		/// <summary>
		/// How much of a body's cratering survives: its air (<see cref="CrateringOf(AtmosphereKind)"/>)
		/// and its volcanism together.
		/// </summary>
		/// <remarks>
		/// An airless world only keeps its craters if nothing paves them over. Io is airless and has
		/// no impact craters at all: its volcanoes resurface it faster than anything hits it, and its
		/// lowlands are lava lakes, not basins. Europa's young ice is nearly as clean. The Moon and
		/// Mercury, cold inside, keep everything. So the record fades out as internal heat rises from
		/// <see cref="ResurfacingStartHeat"/> to <see cref="SurfaceLiquids.LavaLakeHeat"/>, the heat at
		/// which lava stands open: a world with lava lakes has none, so a lake can never sit in a crater
		/// that a scene, which draws craters at their own proportions, would draw shallower than the globe.
		/// </remarks>
		public static float CrateringOf(WorldBody body)
		{
			if (body == null)
			{
				return 0f;
			}
			float fromAir = CrateringOf(body.Atmosphere);
			if (fromAir <= 0f)
			{
				return 0f;
			}
			float heat = ClimateModel.InternalHeat(null, body);
			return fromAir * (1f - Smooth(ResurfacingStartHeat, SurfaceLiquids.LavaLakeHeat, heat));
		}

		/// <summary>Internal heat below which a world keeps every crater; the Moon is about 0.16.</summary>
		/// <remarks>
		/// Above the 0.55 a magnetic field alone gives a world (ClimateModel.InternalHeat), so a cold moon
		/// with a field authored on it still keeps its craters; only real volcanism erases them.
		/// </remarks>
		public const float ResurfacingStartHeat = 0.6f;

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
			float continent = Continent(seed, p, out Vector3 warped, out float uplift);
			float mountains = Ridged(warped * MountainFrequency, MountainOctaves, seed ^ 0xD00DFEEDu);

			float detail = Fbm(p * DetailFrequency, DetailOctaves, seed ^ 0xFACEB00Cu) - 0.5f;

			float height = continent * 0.62f + mountains * uplift * 0.33f + detail * 0.05f;
			return Mathf.Clamp01(height);
		}

		/// <summary>
		/// The continent term and the mountain uplift it allows, shared by <see cref="Height"/> and
		/// <see cref="CoarseHeight"/> so the two cannot disagree about where a coast is.
		/// </summary>
		private static float Continent(uint seed, Vector3 p, out Vector3 warped, out float uplift)
		{
			/* Dragged sideways before the continents are drawn. Undragged fBm gives blobs with
			 * smooth, rounded coasts; warping the input by another noise field is what produces
			 * inlets, peninsulas and the ragged edges a real coast has. */
			Vector3 warp = new Vector3(
				Noise(p * WarpFrequency, seed ^ 0x1A2B3C4Du) - 0.5f,
				Noise(p * WarpFrequency, seed ^ 0x5E6F7A8Bu) - 0.5f,
				Noise(p * WarpFrequency, seed ^ 0x9CADBEEFu) - 0.5f);
			warped = p + warp * WarpStrength;

			// Continents: the slow shape of the world, and the only term that decides land from sea.
			float continent = Fbm(warped * ContinentFrequency, ContinentOctaves, seed);
			continent = SoftClamp01(0.5f + (continent - 0.5f) * ContinentContrast, ContinentKnee);

			/* Mountains ride the continents and are tallest near their edges. That is where they
			 * are on a real world too: ranges stand along convergent margins — the Andes, the
			 * Rockies, the Cascades — because that is where one plate is being driven under
			 * another. Interiors keep a floor of uplift, because they are not all shield: the
			 * Urals, the Altai and the Tian Shan stand thousands of kilometres from any coast. */
			float edge = 1f - Mathf.Abs(continent - 0.5f) * 2f;
			uplift = Mathf.SmoothStep(0f, 1f, continent) * Mathf.Lerp(InteriorUplift, 1f, Smooth(0.15f, 0.95f, edge));
			return continent;
		}

		/// <summary>How many of the mountain octaves <see cref="CoarseHeight"/> keeps.</summary>
		public const int CoarseMountainOctaves = 2;

		/// <summary>
		/// What the mountain octaves <see cref="CoarseHeight"/> leaves out add on average, in the
		/// ridged field's own 0..1 units.
		/// </summary>
		/// <remarks>
		/// Measured over 50,000 Fibonacci points at the mountain frequency (the moisture harness,
		/// 2026-10-02): the full five-octave ridged field minus its first two octaves averages
		/// 0.1212. Adding it back keeps the coarse ground at the real ground's mean height, so the
		/// coarse coastline lies on the real one rather than seaward of every mountainous coast —
		/// without it 6.9% of an Earth-like globe changed between land and sea.
		/// </remarks>
		public const float CoarseRidgeResidual = 0.121f;

		/// <summary>
		/// The surface field with only its continent-scale and mountain-range-scale terms: the
		/// ground as seen from a few hundred kilometres away.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>For anything that asks about the ground many times per point.</b> Moisture walks
		/// thousands of kilometres upwind of every point it is asked about, sampling the ground as
		/// it goes, and it only needs to know whether that ground is sea or land and how high the
		/// ranges in the way stand — features hundreds of kilometres across. <see cref="Height"/>
		/// spends fourteen noise evaluations a point, six of them on the fine mountain octaves and
		/// the hill-scale detail, none of which an air mass crossing a continent can feel. This
		/// spends eight.
		/// </para>
		/// <para>
		/// The continent term is the SAME function <see cref="Height"/> uses, so the coasts agree;
		/// the mountain octaves kept are exactly <see cref="Height"/>'s first two, normalised as
		/// the full field normalises them, with the mean of the rest added back. Detail is left
		/// out entirely: it is centred on zero, so leaving it out moves nothing on average.
		/// Craters are left out too — nothing that needs this has air over a cratered surface.
		/// </para>
		/// </remarks>
		public static float CoarseHeight(uint seed, Vector3 direction)
		{
			Vector3 p = direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.up;
			float continent = Continent(seed, p, out Vector3 warped, out float uplift);
			float mountains = Ridged(warped * MountainFrequency, CoarseMountainOctaves, MountainOctaves, seed ^ 0xD00DFEEDu)
				+ CoarseRidgeResidual;
			return Mathf.Clamp01(continent * 0.62f + mountains * uplift * 0.33f);
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
			/// <summary>
			/// The normalised height at each of <see cref="OceanAreaShallower"/>'s fractions of
			/// this world's ocean, shallowest first: where its shelf ends, where its abyssal plain
			/// begins. Null on a dry world, which has no sea floor.
			/// </summary>
			public float[] OceanFloor;

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
			int seaIndex = water >= 1f ? SeaLevelSamples - 1
				: Mathf.Clamp(Mathf.RoundToInt(water * (SeaLevelSamples - 1)), 0, SeaLevelSamples - 1);
			profile.SeaLevel = water <= 0f ? heights[SeaLevelSamples / 2] : heights[seaIndex];

			/* The same sorted sweep says how this world's own ocean floor is distributed, which is
			 * what lets it be given Earth's: the height below which each fraction of the ocean
			 * lies. Everything from the sea's index down is ocean, shallowest first. */
			if (water > 0f && seaIndex > 0)
			{
				profile.OceanFloor = new float[OceanAreaShallower.Length];
				for (int i = 0; i < OceanAreaShallower.Length; i++)
				{
					float at = seaIndex * (1f - OceanAreaShallower[i]);
					int below = Mathf.Clamp(Mathf.FloorToInt(at), 0, seaIndex);
					int above = Mathf.Min(below + 1, seaIndex);
					profile.OceanFloor[i] = Mathf.Lerp(heights[below], heights[above], at - below);
				}
			}

			profiles[key] = profile;
			return profile;
		}

		/// <summary>The normalised height the ocean surface sits at.</summary>
		public static float SeaLevel(uint seed, float waterFraction, float cratering = 0f) => Profile(seed, waterFraction, cratering).SeaLevel;

		/// <summary>A body's profile, with its own cratering taken into account.</summary>
		public static PlanetProfile ProfileOf(uint seed, WorldBody body)
		{
			return Profile(seed, body != null ? body.Water : 0.7f, CrateringOf(body));
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
		/// <remarks>
		/// Earth runs about 11 km below sea level to 8.8 km above it. A body's sea floor is Earth's
		/// scaled by its own relief against this (<see cref="OceanDepthMetres"/>), so a smaller
		/// world has a proportionally shallower shelf and abyss.
		/// </remarks>
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
			return ReliefMetresForRadius(body != null && body.SkyRadiusKm > 0.01f ? body.SkyRadiusKm : EarthRadiusKm);
		}

		/// <summary>The relief a body of this radius has, in metres. See <see cref="ReliefMetres"/>.</summary>
		public static float ReliefMetresForRadius(float radiusKm)
		{
			radiusKm = Mathf.Max(0.01f, radiusKm);
			float reliefKm = EarthReliefMetres / 1000f * Mathf.Pow(radiusKm / EarthRadiusKm, ReliefRadiusExponent);
			return Mathf.Min(reliefKm, radiusKm * MaximumReliefFraction) * 1000f;
		}

		// ── The world a scene is cut from ─────────────────────────────

		/// <summary>
		/// The radius a scene's kilometres are laid over the globe at.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The atlas radius, not the sky radius.</b> A body has two: <c>SkyRadiusKm</c> is the
		/// physical planet, which decides how it looks in somebody else's sky, how hard its tides
		/// pull and how much relief it has. <c>AtlasRadiusKm</c> is the world the game is played
		/// on, the globe the atlas draws scenes on, sized so that scenes cover a real share of it.
		/// </para>
		/// <para>
		/// Scenes were cut on the sky radius while the atlas drew them on the atlas radius. On
		/// Arthis that is 3583 km against 30: a rectangle drawn over 12° of coast and mountains
		/// produced a scene of the single point at its centre, magnified 119 times. The ground
		/// was a shallow shelf with 10 m islands under a rectangle the globe showed as 61% land
		/// reaching 1.8 km. Cut at the atlas radius, a scene is exactly the ground the rectangle
		/// covers.
		/// </para>
		/// </remarks>
		public static double SceneRadiusKm(WorldBody body)
		{
			return body != null ? Math.Max(1.0, body.AtlasRadiusKm) : EarthRadiusKm;
		}

		/// <summary>
		/// How much taller the game world's relief is than a body of its radius would have.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A scene lays hundreds of real kilometres into a few, so real heights cannot come with
		/// them: at full height a mountain range that rises over 50 km rises over 400 m, and
		/// measured on Arthis 30% of the ground came out steeper than 45°. Scaled exactly with the
		/// kilometres, the same range is a 60 m hill and everything is flat.
		/// </para>
		/// <para>
		/// The middle is the relief a body of the atlas radius would have (relief ∝ radius^0.36,
		/// the same law as everything else), times this. At 2, measured on Arthis (a 30 km atlas,
		/// so a scale of 0.36): its highest mountain scenes stand 0.9–2.6 km with a median slope
		/// of 36° and 29% of the ground steeper than 45° — alpine — while foothills run at 19°
		/// and a coast at 10°. At 1.5 mountains read as hills; at 2.5 they were a third cliff
		/// before the interior uplift raised them further.
		/// </para>
		/// </remarks>
		public const float SceneReliefExaggeration = 2f;

		/// <summary>
		/// Metres in a scene per metre of the planet's altitude. Never more than one: a scene is
		/// the planet's own ground, exaggerated against its kilometres, never taller than it.
		/// </summary>
		public static float SceneVerticalScale(WorldBody body, double sceneRadiusKm)
		{
			float planet = ReliefMetres(body);
			float world = ReliefMetresForRadius((float)sceneRadiusKm) * SceneReliefExaggeration;
			return planet > 0f ? Mathf.Clamp(world / planet, 0f, 1f) : 1f;
		}

		/// <summary>
		/// Metres in a scene per metre of crater depth: the craters keep their own proportions.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A crater is a shape, not relief. Its depth is a fixed share of its width: about a fifth for a
		/// small bowl, a twentieth for a 100 km crater, a fiftieth for a basin (Pike 1977, from lunar
		/// craters). A scene shrinks the planet's kilometres by the atlas radius over the sky radius
		/// (about 1/60 for a 1,700 km moon on a 30 km atlas) but its heights only by
		/// <see cref="SceneVerticalScale"/> (about 0.36), so a crater cut with the ground came out about as
		/// deep as it was wide: every crater a pit. Scaled with the kilometres instead, a crater is the
		/// shape it is on the globe; times <see cref="SceneCraterExaggeration"/>, the same lift the
		/// mountains get, so the scale reads, kept within the range real craters span.
		/// </para>
		/// <para>Never more than the vertical scale: a crater is never deeper than the ground it is cut in.</para>
		/// </remarks>
		public static float SceneCraterScale(WorldBody body, double sceneRadiusKm)
		{
			float sky = body != null && body.SkyRadiusKm > 0.01f ? body.SkyRadiusKm : EarthRadiusKm;
			float horizontal = (float)(sceneRadiusKm / sky) * SceneCraterExaggeration;
			return Mathf.Min(horizontal, SceneVerticalScale(body, sceneRadiusKm));
		}

		/// <summary>How much deeper than their true proportion a scene draws craters (as <see cref="SceneReliefExaggeration"/> does mountains).</summary>
		public const float SceneCraterExaggeration = 2f;

		/// <summary>
		/// How rugged the ground is at an altitude, 0 on the shore to 1 in high mountains.
		/// </summary>
		/// <param name="altitudeMetres">The planet's altitude, in its own metres.</param>
		/// <param name="relief">The body's relief, from <see cref="ReliefMetres"/>.</param>
		/// <remarks>
		/// High ground is broken ground. Coastal plains are old sediment and wear flat; uplands are
		/// being lifted faster than they wear, so hills there are taller and ridges sharper. This
		/// is what lets local detail make a lowland gentle and the foothills below a range steep,
		/// using nothing but what the planet already knows about where the range is.
		/// </remarks>
		public static float Ruggedness(float altitudeMetres, float relief)
		{
			return altitudeMetres > 0f ? Smooth(0.03f, 0.4f, altitudeMetres / Mathf.Max(1f, SummitMetres(relief))) : 0f;
		}

		/// <summary>
		/// The altitude of a point above the body's sea level, in metres.
		/// </summary>
		/// <param name="seed">The world's seed.</param>
		/// <param name="body">The body, for its size and its ocean fraction. Null is Earth-like.</param>
		/// <param name="direction">Where on the globe.</param>
		public static float AltitudeMetres(uint seed, WorldBody body, Vector3 direction)
		{
			return AltitudeFromHeight(Height(seed, direction, CrateringOf(body)), ProfileOf(seed, body), ReliefMetres(body));
		}

		/// <summary>
		/// <see cref="AltitudeMetres"/> with and without the body's craters, in the planet's metres, from
		/// one sample of the field. Their difference is what the craters add at this point (negative in a
		/// bowl, positive on a rim), which a scene scales on its own (<see cref="SceneCraterScale"/>).
		/// </summary>
		public static void AltitudeParts(uint seed, WorldBody body, Vector3 direction, out float withCraters, out float withoutCraters)
		{
			float cratering = CrateringOf(body);
			PlanetProfile profile = ProfileOf(seed, body);
			float relief = ReliefMetres(body);
			float height = Height(seed, direction);
			withoutCraters = AltitudeFromHeight(height, profile, relief);
			withCraters = cratering > 0.001f
				? AltitudeFromHeight(Mathf.Clamp01(height + Craters(direction.normalized, seed ^ 0x0C0FFEE1u) * cratering * CraterDepth), profile, relief)
				: withoutCraters;
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
				if (profile.OceanFloor == null)
				{
					// A dry world: below its datum is low ground, not sea floor, and has no shelf.
					return above / profile.Range * relief;
				}
				return -OceanDepthMetres(height, profile.OceanFloor) * (relief / EarthReliefMetres);
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
			/* Not clamped at the top. The profile's Highest is the highest of 8192 samples, and the
			 * real field goes higher between them. Clamped, every summit above that sample came out
			 * as a flat table at exactly the same altitude, which nobody sees on a globe and which is
			 * the first thing anybody sees standing on the mountain. */
			float top = Mathf.Max(1e-4f, profile.Highest - profile.SeaLevel);
			float x = Mathf.Max(0f, above / top);
			float shaped = LandShoreSlope * x + (1f - LandShoreSlope) * Mathf.Pow(x, LandHypsometry);
			return shaped * SummitMetres(relief);
		}

		/// <summary>Earth's highest summit as a share of its relief: Everest, 8848 m, of 20 km.</summary>
		/// <remarks>
		/// <para>
		/// <b>A world's summit is Earth's share of its relief, not whatever its seed happened to reach.</b>
		/// Land used to be scaled by how much of the field's range stood above the sea, which
		/// varies from seed to seed. On an Earth-like body it came to a fifth of the relief, so the
		/// highest mountain on the planet was 4.2 km. The ocean floor was already given Earth's
		/// shape by area, and this is the same move for the land: a fixed share, so every seed
		/// has Everest-proportioned peaks.
		/// </para>
		/// </remarks>
		public const float EarthSummitFraction = 8848f / EarthReliefMetres;

		/// <summary>How high a body's highest ground stands above its sea, in metres.</summary>
		public static float SummitMetres(float relief) => relief * EarthSummitFraction;


		/// <summary>
		/// How hard land is pushed down toward sea level, standing in for a bimodal hypsometry.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Measured over 200,000 points of the real field rather than reasoned about, because the
		/// arithmetic that used to stand here was wrong twice over. A bare <c>x^6</c> was chosen on
		/// the theory that the mean of x^p is 1/(p+1) of the summit and would land near 1.3 km;
		/// that identity holds for x spread evenly, and the field's land is not — measured, x^6
		/// gave a mean land elevation of <b>198 m</b> against Earth's 840, and put the MEDIAN land
		/// point <b>8 m</b> above the water line.
		/// </para>
		/// <para>
		/// <b>The second error was worse, and it is why the ground never matched the globe.</b> A
		/// pure power curve has zero derivative at the shore, so it does not merely lower the land,
		/// it flattens it: across a two-kilometre scene the planet's own shape contributed
		/// <b>0.30 m</b> of height. Meanwhile the baked globe shades the raw field, which varies
		/// normally over the same ground — so the picture showed a continent with relief on it and
		/// the scene cut from that spot came out a table.
		/// </para>
		/// </remarks>
		public const float LandHypsometry = 4f;

		/// <summary>
		/// Earth's ocean floor: the fraction of the ocean shallower than each of
		/// <see cref="OceanKnotDepthMetres"/>, measured.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The sea floor was a straight line, and it put a third of every ocean in a trench.</b>
		/// Below sea level the field used to map linearly onto depth, on the theory that trenches
		/// really are spread across their range. They are not. Earth's ocean floor is its own
		/// second hypsometric mode: a continental shelf a couple of hundred metres deep, a slope
		/// that falls kilometres in a narrow band, and an abyssal plain that holds three quarters
		/// of all the ocean between three and six kilometres down, with trenches a sliver below.
		/// Measured over 200,000 points of a 75%-ocean world, the straight line put 2.2% of its
		/// ocean on shelves (Earth: 7.1%), 26% on the abyssal plain (74%) and <b>33% deeper than
		/// seven kilometres (0.1%)</b>. Its mean depth was 5351 m against Earth's 3686.
		/// </para>
		/// <para>
		/// <b>So a world's ocean is given Earth's, by area.</b> Each point's depth comes from how
		/// much of its own ocean is shallower than it, read off Earth's curve. That fixes the
		/// shares exactly for any seed and any ocean fraction, which a curve fitted to one seed's
		/// field cannot; and because depth still rises with the field's height, every coastline,
		/// basin and ridge stays exactly where it was — only how deep each lies changes. The shelf
		/// comes out where it belongs, as the band of shallows along every coast.
		/// </para>
		/// <para>
		/// Digitised from NOAA NCEI's hypsographic curve of ETOPO1 (Eakins &amp; Sharman, 2012):
		/// the coastal curve for the first 100 m and the global one below it, ocean taken as the
		/// 70.95% of the surface below sea level. The table's own mean depth is 3680 m, against
		/// 3686 m published with the curve. The hadal tail beyond 6.4 km is too thin a sliver for
		/// the figure to resolve and follows Jamieson (2010): the hadal zone is 1–2% of the ocean
		/// floor, nearly all of it above seven kilometres.
		/// </para>
		/// </remarks>
		public static readonly float[] OceanAreaShallower =
		{
			0f, 0.0376f, 0.0555f, 0.0709f, 0.0980f, 0.1179f, 0.1389f, 0.1629f, 0.1961f, 0.2503f, 0.3433f,
			0.4767f, 0.6366f, 0.7928f, 0.9299f, 0.9896f, 0.9970f, 0.9993f, 0.99985f, 0.99997f, 1f,
		};

		/// <summary>The depths, in metres on Earth, that <see cref="OceanAreaShallower"/> pairs with.</summary>
		public static readonly float[] OceanKnotDepthMetres =
		{
			0f, 50f, 100f, 200f, 500f, 1000f, 1500f, 2000f, 2500f, 3000f, 3500f,
			4000f, 4500f, 5000f, 5500f, 6000f, 7000f, 8000f, 9000f, 10000f, 10911f,
		};

		/// <summary>Where the hadal zone starts in the table: six kilometres, the floor of the abyssal plain.</summary>
		private const int HadalKnot = 15;

		/// <summary>
		/// How deep a sea-floor height lies on Earth's curve, in metres, before the body's own
		/// size scales it.
		/// </summary>
		/// <param name="height">A normalised height at or below the world's sea level.</param>
		/// <param name="floor">The world's <see cref="PlanetProfile.OceanFloor"/>.</param>
		/// <remarks>
		/// <para>
		/// <b>Ground that is flat takes the shallowest depth it reaches.</b> The field is clamped
		/// at 0, and on some worlds a percent of the ocean lies exactly on that clamp — measured,
		/// 1.0% of Naraeon's — so the deepest few knots all land on the same height. Taking the
		/// deepest of them laid that whole percent flat at trench depth, a plate the size of a
		/// sea at the bottom of the world; taking the shallowest makes it what flat ground down
		/// there is, an abyssal plain.
		/// </para>
		/// <para>
		/// Below the deepest point the sweep found, the hadal slope is carried on rather than the
		/// depth held: about one part in eight thousand of the surface is deeper than any of the
		/// sweep's samples, and a floor that stopped there would be a flat plate at the bottom of
		/// every deepest trench.
		/// </para>
		/// </remarks>
		public static float OceanDepthMetres(float height, float[] floor)
		{
			int last = floor.Length - 1;
			if (height >= floor[0])
			{
				return 0f;
			}
			if (height < floor[last])
			{
				float span = floor[HadalKnot] - floor[last];
				float perHeight = span > 1e-7f ? (OceanKnotDepthMetres[last] - OceanKnotDepthMetres[HadalKnot]) / span : 0f;
				return OceanKnotDepthMetres[last] + (floor[last] - height) * perHeight;
			}

			// The knots fall with depth. Find the first at or below the height — the shallowest,
			// when several share it — and come down to it from the one before.
			int lo = 0;
			int hi = last;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) >> 1;
				if (floor[mid] > height)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			float gap = floor[lo] - floor[hi];
			float t = gap > 1e-9f ? (floor[lo] - height) / gap : 1f;
			return Mathf.Lerp(OceanKnotDepthMetres[lo], OceanKnotDepthMetres[hi], t);
		}

		/// <summary>
		/// How much of the land curve stays linear, which is what keeps a slope at the shore.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The linear part sets the derivative near sea level and the power part sets the mean.
		/// </para>
		/// <para>
		/// 0.20 since summits were fixed at Earth's share of the relief
		/// (<see cref="EarthSummitFraction"/>). That roughly doubled the height of the top of the
		/// curve, and with the old 0.30 the whole of the land rose with it. Measured over 60,000
		/// points on five seeds: mean land 1.0–1.3 km (Earth: 840 m) and summits of 7–11 km
		/// (Earth: 8.8), where the old curve's summits were 4–5 km. A little more mountainous than
		/// Earth, on purpose: mountains are what a scene cut from a world is for.
		/// </para>
		/// </remarks>
		public const float LandShoreSlope = 0.20f;

		/// <summary>The altitude above sea level at a latitude and longitude, in metres.</summary>
		public static float AltitudeMetresAt(uint seed, WorldBody body, double latitudeDegrees, double longitudeDegrees)
		{
			return AltitudeMetres(seed, body, Direction(latitudeDegrees, longitudeDegrees));
		}

		// ── Craters ───────────────────────────────────────────────────

		/// <summary>The crater sizes drawn, largest first, as frequencies on the unit sphere.</summary>
		/// <remarks>
		/// <para>
		/// Three sizes, because an impact record is scale-free: a few basins, many craters, and
		/// countless small ones. On a Moon-sized body they are basins of 230–480 km, craters of
		/// 95–195 km and 40–85 km.
		/// </para>
		/// <para>
		/// <b>Depth follows the measured law, not the size.</b> A complex crater's depth grows only as
		/// its diameter to the 0.3 (lunar d ≈ 1.04·D^0.301 km, Pike 1977), so a 60 km crater is 3.6 km
		/// deep, a 150 km one 4.8 km and a basin 5–6 km: nearly the same. The weights below give
		/// those on a Moon (relief 12.5 km, <see cref="CraterDepth"/> 0.5). They used to fall by a
		/// third per size, which made the smaller craters a third of their real depth while the
		/// basins were right. Crater widths grow with the radius and depths with D^0.3, close to the
		/// relief law's radius^0.36, so the same shares hold on any body.
		/// </para>
		/// </remarks>
		private static readonly float[] CraterFrequencies = { 4.5f, 11f, 26f };
		private static readonly float[] CraterWeights = { 1f, 0.77f, 0.58f };

		/// <summary>
		/// Each size's shape: the flat floor's share of the radius and the central peak's height as a
		/// share of the depth.
		/// </summary>
		/// <remarks>
		/// Craters this big are all complex: the floor rebounds flat after the impact and a central
		/// peak rises where the rock sprang back. Basins are wide and flat-floored with the peak
		/// collapsed into a low inner ring; the smaller complex craters have narrower floors and the
		/// tallest peaks (about a third of the depth, as Tycho's and Copernicus's are).
		/// </remarks>
		private static readonly float[] CraterFloors = { 0.48f, 0.36f, 0.24f };
		private static readonly float[] CraterPeaks = { 0f, 0.28f, 0.34f };

		/// <summary>The rim crest's position (share of the radius) and height (share of the depth).</summary>
		/// <remarks>Rim height grows as D^0.4 against depth's D^0.3, so it is about 0.35–0.4 of the depth over these sizes (Pike 1977).</remarks>
		private const float RimCrest = 0.86f, RimHeight = 0.38f;

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
				total += Crater(p * CraterFrequencies[octave], seed + (uint)octave * 0x7F4A7C15u, CraterFloors[octave], CraterPeaks[octave]) * CraterWeights[octave];
			}
			return total;
		}

		/// <summary>One scale of craters: the deepest cut from any nearby impact. <paramref name="floor"/> and <paramref name="peak"/> as <see cref="CraterFloors"/> and <see cref="CraterPeaks"/>.</summary>
		private static float Crater(Vector3 p, uint seed, float floor, float peak)
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
						/* The rim is faded to nothing at the crater's edge. The Gaussian is still
						 * about 4% of its peak at t = 1, where the crater stops being counted, so
						 * every crater ended in a small step: invisible on a globe, but a ring-shaped
						 * cliff once a scene magnifies it. */
						/* A complex crater: a flat floor out to `floor`, walls rising to the rim, and a
						 * central peak. The walls are the old parabola, run from the floor's edge. */
						float u = t <= floor ? 0f : (t - floor) / (1f - floor);
						float bowl = -(1f - u * u);
						float rebound = peak > 0f ? peak * Mathf.Exp(-(t * t) / 0.012f) : 0f;
						float rim = Mathf.Exp(-((t - RimCrest) * (t - RimCrest)) / 0.006f) * RimHeight * (1f - Smooth(0.92f, 1f, t));
						float shape = bowl + rebound + rim;

						// The deepest wins, so a newer crater cuts through an older one instead of
						// the two averaging into a shallow dish.
						deepest = Mathf.Min(deepest, shape) + Mathf.Max(0f, rim) * 0.35f;
					}
				}
			}
			return Mathf.Clamp(deepest, -1f, 0.35f);
		}

		// ── Local detail ──────────────────────────────────────────────

		/// <summary>How far apart the coarsest rolling hills are, in metres.</summary>
		/// <remarks>
		/// The planet itself now reaches down to features about 1.7 km across in a scene cut at
		/// the atlas radius, so local detail only has to supply what is smaller than that: the
		/// hills and gullies somebody walks over.
		/// </remarks>
		public const float DetailFeatureMetres = 500f;

		/// <summary>How far apart local ridges are, in metres.</summary>
		public const float RidgeFeatureMetres = 1100f;

		/// <summary>Rolling hills on the flattest ground, as a share of the scene's relief.</summary>
		/// <remarks>
		/// About 8 m on Arthis: a lowland is not a table, but its undulations are gentle.
		/// </remarks>
		public const float LowlandHillFraction = 0.0014f;

		/// <summary>Rolling hills in the mountains, as a share of the scene's relief. About 60 m on Arthis.</summary>
		public const float UplandHillFraction = 0.0103f;

		/// <summary>Sharp-crested ridges in the mountains, as a share of the scene's relief. About 220 m on Arthis.</summary>
		/// <remarks>
		/// Ridged noise rather than more of the same hills, because mountains are not rounded:
		/// they are crests and the gullies between them. Only high ground gets it (see
		/// <see cref="Ruggedness"/>), which is what makes the foothills of a range steeper than a
		/// coastal plain.
		/// </remarks>
		public const float RidgeFraction = 0.038f;

		/// <summary>The mean of five octaves of <see cref="Ridged"/>, measured over 40,000 samples: what is subtracted to centre it.</summary>
		private const float RidgedMean = 0.583f;

		/// <summary>
		/// The most local detail can move the ground either way, in metres.
		/// </summary>
		/// <param name="sceneReliefMetres">The scene's relief: the body's relief times <see cref="SceneVerticalScale"/>.</param>
		/// <remarks>
		/// Exact, not an estimate: the hills are <c>(Fbm − 0.5) × 2</c> of at most the upland
		/// amplitude, and <c>Fbm</c> is bounded to 0 … 1; the ridges are <c>Ridged − 0.583</c>,
		/// bounded to −0.583 … 0.417. So the result cannot leave this at any point on any world,
		/// which is what lets a scene's height range be bounded without sampling every point of
		/// it — a range that is merely sampled is a range some peak between the samples falls
		/// outside, and the terrain then flattens that peak.
		/// </remarks>
		public static float LocalDetailAmplitudeMetres(float sceneReliefMetres)
		{
			return sceneReliefMetres * (UplandHillFraction + RidgeFraction * RidgedMean);
		}

		/// <summary>
		/// Metre-scale ground shape for a point inside a scene, in scene metres.
		/// </summary>
		/// <param name="seed">The body's terrain seed, mixed with whatever identifies the scene.</param>
		/// <param name="eastMetres">Metres east of the scene's centre.</param>
		/// <param name="northMetres">Metres north of the scene's centre.</param>
		/// <param name="sceneReliefMetres">The scene's relief, which the amplitudes are shares of.</param>
		/// <param name="ruggedness">0 on flat low ground to 1 in mountains, from <see cref="Ruggedness"/>.</param>
		/// <remarks>
		/// <para>
		/// In scene-local metres, not on the unit sphere, and that is a numerical necessity rather
		/// than a convenience. A feature 500 m across on the globe is a frequency far past what a
		/// float can interpolate on the unit sphere: the fractional part the noise needs would be
		/// quantised into visible steps. In local metres the coordinates stay small and every bit
		/// of the mantissa does useful work.
		/// </para>
		/// <para>
		/// It is centred on zero, so it roughens the ground without moving it: the scene still sits
		/// at the altitude the globe says, and the coastline is still where the map shows it.
		/// </para>
		/// </remarks>
		public static float LocalDetailMetres(uint seed, float eastMetres, float northMetres, float sceneReliefMetres, float ruggedness)
		{
			ruggedness = Mathf.Clamp01(ruggedness);
			/* Six octaves, so the finest features are about fifteen metres across: a scene needs
			 * shape at the scale somebody walks over, not only at the scale they see from a ridge. */
			var p = new Vector3(eastMetres / DetailFeatureMetres, 0.37f, northMetres / DetailFeatureMetres);
			float hills = (Fbm(p, 6, seed ^ 0x5CE4E7A1u) - 0.5f) * 2f
				* Mathf.Lerp(LowlandHillFraction, UplandHillFraction, ruggedness);

			float ridges = 0f;
			if (ruggedness > 0f)
			{
				var q = new Vector3(eastMetres / RidgeFeatureMetres, 0.61f, northMetres / RidgeFeatureMetres);
				ridges = (Ridged(q, 5, seed ^ 0x2F1D6E3Bu) - RidgedMean) * RidgeFraction * ruggedness;
			}
			return (hills + ridges) * sceneReliefMetres;
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

		/// <summary>
		/// The first <paramref name="octaves"/> of a <paramref name="normaliseOctaves"/>-octave
		/// ridged field, divided by the FULL field's total so each kept octave weighs exactly what
		/// it weighs in the full field.
		/// </summary>
		private static float Ridged(Vector3 p, int octaves, int normaliseOctaves, uint seed)
		{
			float total = 0f, a = 0.5f;
			for (int i = 0; i < normaliseOctaves; i++)
			{
				total += a;
				a *= 0.5f;
			}
			float sum = 0f, amplitude = 0.5f, weight = 1f;
			Vector3 q = p;
			for (int i = 0; i < octaves; i++)
			{
				float n = 1f - Mathf.Abs(Noise(q, seed + (uint)i * 0x9E3779B1u) * 2f - 1f);
				n *= n;
				n *= weight;
				weight = Mathf.Clamp01(n * 2f);
				sum += n * amplitude;
				q *= 2.07f;
				amplitude *= 0.5f;
			}
			return total > 0f ? Mathf.Clamp01(sum / total) : 0f;
		}
	}
}
