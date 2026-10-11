using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>What floats in the air round the camera, as opposed to what falls through it (<see cref="PrecipitationField"/>).</summary>
	public enum AirMoteKind
	{
		/// <summary>Dust, lint and fibre: in every air, seen only in the light.</summary>
		Dust = 0,
		/// <summary>Pollen and the occasional thistledown seed, over growing land in the warm half of the year.</summary>
		Pollen = 1,
		/// <summary>Diamond dust: ice crystals that form in clear, calm, very cold air and glint toward the sun.</summary>
		Ice = 2,
		/// <summary>Fireflies on a warm humid summer night, low over vegetation and water.</summary>
		Firefly = 3,
		/// <summary>Will-o'-the-wisps: the faint cold lights of marsh gas over wetland at night.</summary>
		Wisp = 4,
	}

	/// <summary>The air at the camera, as the air motes need it. Built from the weather by <see cref="From"/>; a plain value so the choice is testable.</summary>
	public struct AirMotesClimate
	{
		/// <summary>Whether there is any air at all: an airless world holds nothing up.</summary>
		public bool HasAir;
		/// <summary>The air at the ground, °C.</summary>
		public float Celsius;
		/// <summary>Where the world's condensate freezes, °C (0 for water).</summary>
		public float FreezingCelsius;
		/// <summary>Relative humidity at the ground, over liquid, 0..1.</summary>
		public float RelativeHumidity;
		/// <summary>The ten-metre wind, m/s.</summary>
		public float WindMetres;
		/// <summary>The sun's altitude, degrees (negative below the horizon).</summary>
		public float SunAltitude;
		/// <summary>Cloud cover, 0..1.</summary>
		public float CloudCover;
		/// <summary>The precipitation channel, 0..1.</summary>
		public float Precipitation;
		/// <summary>The local year phase (0 midwinter, 0.25 spring, 0.5 midsummer, 0.75 autumn; <c>_FishSeason.x</c>).</summary>
		public float YearPhase;
		/// <summary>How strongly the year swings here (<c>_FishSeason.w</c>): 0 when unknown or on the equator.</summary>
		public float SeasonStrength;
		/// <summary>How much growing plant cover the ground carries, 0..1 (the biome's <see cref="TerrainProcess.VegetationCohesion"/>).</summary>
		public float Vegetation;
		/// <summary>How much the ground is a wetland — standing water through living cover — 0..1 (<see cref="AirMotesField.WetlandOf"/>).</summary>
		public float Wetland;
		/// <summary>How far under cover the camera is, 0..1.</summary>
		public float Shelter;

		/// <summary>The air at the camera from the weather last presented.</summary>
		/// <param name="sunAltitude">The sun's altitude, degrees.</param>
		/// <param name="season">The published season (<see cref="WeatherShaderGlobals.Season"/>): x year phase, w the swing's strength.</param>
		public static AirMotesClimate From(in WeatherContext context, in WeatherFrame frame, float sunAltitude, Vector4 season)
		{
			WeatherSample sample = context.Sample;
			float freezingKelvin = AirPhysics.FreezingKelvin(sample.Planet.Condensate);
			float celsius = sample.Column.SurfaceKelvin > 0f
				? sample.Column.SurfaceKelvin - 273.15f
				: context.Temperature * (float)ClimateModel.KelvinPerUnit;
			BiomeTemplate biome = sample.Biome;
			TerrainProcess ground = biome != null ? biome.ResolvedTerrainProcess : default;
			return new AirMotesClimate
			{
				HasAir = sample.Planet.HasAir,
				Celsius = celsius,
				FreezingCelsius = freezingKelvin - 273.15f,
				RelativeHumidity = sample.Column.SurfaceKelvin > 0f ? sample.Column.RelativeHumidity : sample.Air.Humidity,
				WindMetres = Mathf.Clamp01(frame[WeatherChannel.WindSpeed]) * 30f,
				SunAltitude = sunAltitude,
				CloudCover = frame[WeatherChannel.CloudCover],
				Precipitation = frame[WeatherChannel.Precipitation],
				YearPhase = season.x,
				SeasonStrength = season.w,
				// No biome (a scene without one): a little cover, as most land has; no wetland.
				Vegetation = biome != null ? ground.VegetationCohesion : 0.3f,
				Wetland = biome != null ? AirMotesField.WetlandOf(ground) : 0f,
				Shelter = context.Shelter,
			};
		}
	}

	/// <summary>How much of each kind's full count is in the air, 0..1.</summary>
	public struct AirMotesMix
	{
		public float Dust, Pollen, Ice, Fireflies, Wisps;

		public float this[AirMoteKind kind]
		{
			get
			{
				switch (kind)
				{
					case AirMoteKind.Dust: return Dust;
					case AirMoteKind.Pollen: return Pollen;
					case AirMoteKind.Ice: return Ice;
					case AirMoteKind.Firefly: return Fireflies;
					default: return Wisps;
				}
			}
		}
	}

	/// <summary>
	/// The small things that float in the air round the camera — dust in the light, pollen and seed
	/// fluff, diamond dust, fireflies, marsh lights — chosen from the real climate where the camera
	/// stands, and drawn as stateless particles: one pre-built field of quads per kind, placed and lit
	/// entirely in the vertex shader from each particle's seed and the shared motion clock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The water had its light shafts and drifting matter; the air had nothing, and a still sunny
	/// glade or a summer night over a marsh read as empty. Nothing here is networked or random per
	/// player: where a particle stands is its seed, the air's drift (<see cref="SurfaceAirDrift"/>,
	/// the same integral of the reported wind the fog banks ride) and <see cref="WorldMotion.Seconds"/>,
	/// so every player sees the same firefly flash at the same moment.
	/// </para>
	/// <para>
	/// Brightness is worked from what the particle is, in the units URP lights a surface in (a white
	/// card facing the sun shows the main light's colour): a mote scatters the sun by its single-scatter
	/// albedo times its extinction efficiency (about 2 for anything many wavelengths across) times its
	/// phase function, so it is bright toward the sun and nearly nothing with the sun behind the eye.
	/// The particles are far smaller than a pixel, so they are drawn as soft dots that keep their light
	/// in sum (the precipitation's sub-pixel rule, <see cref="PrecipitationField.SubPixelSigma"/>).
	/// </para>
	/// </remarks>
	public sealed class AirMotesField
	{
		/// <summary>The shader the field is drawn with, found by name when the profile holds none.</summary>
		public const string ShaderName = "FishMMO/Weather/Air Motes";

		/// <summary>The kinds, in draw order.</summary>
		public static readonly AirMoteKind[] Kinds = { AirMoteKind.Dust, AirMoteKind.Pollen, AirMoteKind.Ice, AirMoteKind.Firefly, AirMoteKind.Wisp };

		/// <summary>The least amount of a kind worth a draw.</summary>
		public const float DrawThreshold = 0.005f;

		/// <summary>The quads all the kinds share at a tier, as a share of its precipitation budget, and the bounds on that.</summary>
		/// <remarks>
		/// A quarter of the precipitation's: 750, 2000 and 4000 on the shipped tiers. At most two or three
		/// kinds are ever in the air together (dust with pollen by day, or with fireflies by night), and a
		/// hidden particle collapses to a point in the vertex stage, so the cost is a few thousand
		/// vertices and the few pixels each visible mote covers.
		/// </remarks>
		public const float BudgetShare = 0.25f;
		public const int MinBudget = 400, MaxBudget = 4000;

		/// <summary>The quads all the kinds share at a precipitation budget.</summary>
		public static int Budget(int precipitationParticles) => Mathf.Clamp(Mathf.RoundToInt(precipitationParticles * BudgetShare), MinBudget, MaxBudget);

		/// <summary>The settling speeds are stepped in 64ths of the kind's, so the settling can be wrapped (as <see cref="PrecipitationField.SpeedSteps"/>).</summary>
		public const int SpeedSteps = PrecipitationField.SpeedSteps;

		// ── Which kinds are in the air ─────────────────────────────────

		/// <summary>
		/// How much of the air's floating matter survives the precipitation: rain and snow sweep the air
		/// clean (below-cloud scavenging), a drizzle hardly, a steady rain all of it.
		/// </summary>
		public static float Washout(float precipitation) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.2f, precipitation));

		/// <summary>
		/// The relative humidity (over liquid) at which air at <paramref name="celsiusBelowFreezing"/>
		/// is saturated over ice: e_i/e_w, about 1 + 0.0096·T near the freezing point down to −40 °C
		/// (from the Magnus forms over water and ice). Diamond dust grows only in air at or past it.
		/// </summary>
		public static float IceSaturationHumidity(float celsiusBelowFreezing) => Mathf.Clamp(1f + 0.0096f * Mathf.Min(0f, celsiusBelowFreezing), 0.55f, 1f);

		/// <summary>
		/// How much a ground is a wetland, 0..1: it keeps its lakes (<see cref="TerrainProcess.LakeRetention"/>)
		/// AND carries living cover (<see cref="TerrainProcess.VegetationCohesion"/>). Peat bog, swamp,
		/// wetlands, mangrove and estuary share the Wetland process (0.95, 0.8); periglacial ground keeps
		/// its ponds too (0.9) but over bare frost-churned soil (0.35), which makes no marsh gas.
		/// </summary>
		public static float WetlandOf(in TerrainProcess ground)
		{
			return Smooth(0.8f, 0.93f, ground.LakeRetention) * Smooth(0.5f, 0.75f, ground.VegetationCohesion);
		}

		/// <summary>
		/// How far into the pollen season the year is, 0..1. Trees shed in spring and grasses into high
		/// summer: from a little before the spring point (0.25) to a little after midsummer (0.5), gone
		/// by late summer. Where the year hardly swings (the tropics) something is always flowering,
		/// at the season's middle strength.
		/// </summary>
		public static float PollenSeason(float yearPhase, float seasonStrength)
		{
			float window = Smooth(0.15f, 0.25f, yearPhase) * (1f - Smooth(0.58f, 0.68f, yearPhase));
			return Mathf.Lerp(0.6f, window, Mathf.Clamp01(seasonStrength));
		}

		/// <summary>
		/// How far into the fireflies' season the year is, 0..1: the adults fly from late spring to late
		/// summer, peaking just after midsummer. In the tropics they fly all year.
		/// </summary>
		public static float FireflySeason(float yearPhase, float seasonStrength)
		{
			float window = Smooth(0.36f, 0.43f, yearPhase) * (1f - Smooth(0.62f, 0.7f, yearPhase));
			return Mathf.Lerp(0.85f, window, Mathf.Clamp01(seasonStrength));
		}

		/// <summary>Which kinds are in the air at the camera, and how much of each.</summary>
		/// <remarks>
		/// <para>
		/// <b>Dust</b> is in all air. What decides whether it is SEEN is the light, which the shader
		/// works out (the forward lobe, the sun's shadow), so it is only thinned here: by the rain, which
		/// washes it out, and by humid air near saturation, where it grows into haze droplets too small to
		/// show one by one. Under cover there is more of it: no wind clears a room or a cave.
		/// </para>
		/// <para>
		/// <b>Pollen</b>: spring to midsummer, by day (anthers open as the morning dries the air and
		/// close above about 80 % humidity), over growing land, above about 5 °C, in light to moderate
		/// wind — a breeze shakes it loose and a gale scatters it too thin and too fast to see.
		/// </para>
		/// <para>
		/// <b>Diamond dust</b>: below about −10 °C (the condensate's own freezing point for another
		/// world's), in clear sky (no cloud seeds the air, and the clear night chills it), calm air, and
		/// air saturated over ice (<see cref="IceSaturationHumidity"/>).
		/// </para>
		/// <para>
		/// <b>Fireflies</b>: warmer than about 15 °C, humid (they dry out in dry air), in summer, from
		/// sunset into the dark, over vegetation or wetland, in calm air, not in rain.
		/// </para>
		/// <para>
		/// <b>Wisps</b>: wetland at full night, not frozen (the bog's methane and phosphine come from
		/// decay, which stops in the cold), calm. Rare by their count, not their gate (see the traits).
		/// </para>
		/// </remarks>
		public static AirMotesMix Choose(in AirMotesClimate c)
		{
			if (!c.HasAir)
			{
				return default;
			}
			float washout = Washout(c.Precipitation);
			float open = 1f - Mathf.Clamp01(c.Shelter);
			float rh = Mathf.Clamp01(c.RelativeHumidity);
			float wind = Mathf.Max(0f, c.WindMetres);
			float clear = 1f - Smooth(0.3f, 0.75f, c.CloudCover);

			var mix = new AirMotesMix();

			mix.Dust = washout * Mathf.Lerp(0.6f, 1f, 1f - Smooth(0.85f, 1f, rh)) * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(c.Shelter));

			float pollenDay = Smooth(0f, 10f, c.SunAltitude);
			float pollenWarm = Smooth(3f, 10f, c.Celsius);
			float pollenDry = 1f - Smooth(0.75f, 0.95f, rh);
			float pollenWind = Mathf.Lerp(0.4f, 1f, Smooth(0.5f, 3f, wind)) * (1f - Smooth(8f, 14f, wind));
			float plants = Smooth(0.15f, 0.5f, c.Vegetation);
			mix.Pollen = washout * PollenSeason(c.YearPhase, c.SeasonStrength) * pollenDay * pollenWarm * pollenDry * pollenWind * plants * Mathf.Lerp(0.3f, 1f, open);

			float belowFreezing = c.Celsius - c.FreezingCelsius;
			float iceCold = 1f - Smooth(-14f, -8f, belowFreezing);
			float iceCalm = 1f - Smooth(2f, 6f, wind);
			float iceSat = IceSaturationHumidity(belowFreezing);
			float iceSaturated = Smooth(iceSat - 0.15f, iceSat, rh);
			mix.Ice = washout * iceCold * clear * iceCalm * iceSaturated * open;

			float dusk = 1f - Smooth(-6f, 0f, c.SunAltitude);
			float fireflyWarm = Smooth(13f, 18f, c.Celsius);
			float fireflyHumid = Smooth(0.5f, 0.72f, rh);
			float fireflyCalm = 1f - Smooth(3f, 7f, wind);
			float fireflyCover = Smooth(0.25f, 0.55f, Mathf.Max(c.Vegetation, c.Wetland));
			float fireflyDry = 1f - Smooth(0.01f, 0.08f, c.Precipitation);
			mix.Fireflies = dusk * fireflyWarm * fireflyHumid * fireflyCalm * fireflyCover * fireflyDry * FireflySeason(c.YearPhase, c.SeasonStrength) * open;

			float dark = 1f - Smooth(-12f, -6f, c.SunAltitude);
			float thawed = Smooth(1f, 6f, c.Celsius - c.FreezingCelsius);
			float wispCalm = 1f - Smooth(2f, 5f, wind);
			mix.Wisps = Mathf.Clamp01(c.Wetland) * dark * thawed * wispCalm * washout * open;
			return mix;
		}

		private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

		// ── What each kind is ──────────────────────────────────────────

		/// <summary>How a kind floats and looks. Physics rather than looks, so not on the profile.</summary>
		public struct Traits
		{
			/// <summary>Its share of the field's quads.</summary>
			public float BudgetShare;
			/// <summary>The box it wraps in round the camera, m across and high. For a ground band, y is only the volume's height.</summary>
			public Vector3 Box;
			/// <summary>Heights above the ground it flies at (m), or zero for a box round the camera.</summary>
			public Vector2 Band;
			/// <summary>How many to a cubic metre at full amount.</summary>
			public float PerCubicMetre;
			/// <summary>Smallest, typical and largest across, m (an exponential spread about the typical, as a snowfall's).</summary>
			public Vector3 Size;
			/// <summary>Its settling speed, m/s, and the slowest and fastest against it.</summary>
			public float Settle;
			public Vector2 SettleSpread;
			/// <summary>How much of the surface air's drift carries it: 1 for anything too small to fly, little for what flies.</summary>
			public float Carry;
			/// <summary>Its own wander, m, at the slowest and fastest pace (rad/s), and how much of it is vertical.</summary>
			public float Wander;
			public Vector2 WanderPace;
			public float WanderVertical;
			/// <summary>rgb its colour (albedo, or the light it gives), a its single-scatter albedo times its extinction efficiency.</summary>
			public Color Color;
			/// <summary>x the share in a sharp forward lobe, y that lobe's g, z the broad lobe's g, w the share lit as an opaque grain.</summary>
			public Vector4 Phase;
			/// <summary>The light it gives (linear, at its own disc), and the shortest and longest cycle of it (s), and the lit share of a cycle.</summary>
			public float Radiance;
			public Vector2 Cycle;
			public float LitShare;
			/// <summary>Its glow's halo: radius against the core, and its share of the core's light.</summary>
			public Vector2 Halo;
		}

		/// <summary>
		/// The cosine half-angle of the band of plate tilts that throws a glint to the eye, and the glint's
		/// brightness against a white card in the sun.
		/// </summary>
		/// <remarks>
		/// A crystal face is a mirror: it shows the sun's own disc at its Fresnel reflectance, about 5 %
		/// for ice over both faces of a plate, and the sun's disc is π / 6.8e-5 ≈ 46 000 times as bright
		/// as a white card it lights. A plate swings through a few degrees as it falls, and a glint drawn
		/// only while it holds the sun's quarter-degree would blink for a frame and be gone; so the glint
		/// is spread over a cone of <see cref="GlintDegrees"/> half-angle with its light divided by that
		/// cone's solid angle over the sun's, which holds what the crystal sends the eye over its swing.
		/// </remarks>
		public const float GlintDegrees = 1.5f, IceReflectance = 0.05f;

		/// <summary>The glint's brightness at the heart of its cone, against a white card in the sun: R·π / Ω.</summary>
		public static float GlintPeak()
		{
			float half = GlintDegrees * Mathf.Deg2Rad;
			return IceReflectance * Mathf.PI / (Mathf.PI * half * half);
		}

		/// <summary>The traits of a kind.</summary>
		public static Traits TraitsOf(AirMoteKind kind)
		{
			switch (kind)
			{
				// Dust motes: the ones big enough to be seen one by one in a beam are lint, fibre and
				// soil aggregates, 20 µm to a millimetre, a few to a litre in still air near the
				// ground. They scatter mostly forward — diffraction puts a sharp lobe within ten degrees
				// of the sun, refraction a broad one — with an albedo of about 0.8: a sunbeam's motes
				// blaze against the shade and vanish with the sun behind the eye. They settle at a few
				// centimetres a second and go wherever the air goes.
				case AirMoteKind.Dust: return new Traits
				{
					BudgetShare = 0.35f, Box = new Vector3(8f, 6f, 8f), PerCubicMetre = 3f,
					Size = new Vector3(20e-6f, 150e-6f, 1e-3f), Settle = 0.02f, SettleSpread = new Vector2(0.5f, 1.5f), Carry = 1f,
					Wander = 0.12f, WanderPace = new Vector2(0.15f, 0.6f), WanderVertical = 0.6f,
					Color = new Color(0.92f, 0.88f, 0.8f, 0.8f * 2f), Phase = new Vector4(0.45f, 0.92f, 0.55f, 0f),
				};
				// Pollen: single grains (20–90 µm) are too small to see; what is seen in the air of a
				// meadow are clumps of them and, now and then, a seed on its pappus — thistledown or
				// willow fluff a centimetre or two across, falling at a few tenths of a metre a second
				// (eight in a hundred here). Yellowish and opaque, so lit partly as a grain and partly by
				// its forward scatter.
				case AirMoteKind.Pollen: return new Traits
				{
					BudgetShare = 0.3f, Box = new Vector3(16f, 10f, 16f), PerCubicMetre = 0.2f,
					Size = new Vector3(60e-6f, 0.25e-3f, 1.5e-3f), Settle = 0.04f, SettleSpread = new Vector2(0.5f, 1.5f), Carry = 1f,
					Wander = 0.35f, WanderPace = new Vector2(0.1f, 0.45f), WanderVertical = 0.7f,
					Color = new Color(1f, 0.86f, 0.48f, 0.9f * 2f), Phase = new Vector4(0.3f, 0.85f, 0.5f, 0.5f),
				};
				// Diamond dust: hexagonal plates and columns 0.1–1 mm, settling at a few tenths of a
				// metre a second. The plates fall face-down, rocking a few degrees (what draws the sun
				// pillar); the columns tumble. Clear ice, so almost all of their light is the glint.
				case AirMoteKind.Ice: return new Traits
				{
					BudgetShare = 0.35f, Box = new Vector3(12f, 10f, 12f), PerCubicMetre = 0.5f,
					Size = new Vector3(0.1e-3f, 0.3e-3f, 1.2e-3f), Settle = 0.2f, SettleSpread = new Vector2(0.6f, 1.4f), Carry = 1f,
					Wander = 0.06f, WanderPace = new Vector2(0.3f, 1.2f), WanderVertical = 0.3f,
					Color = new Color(0.95f, 0.97f, 1f, 0.1f), Phase = new Vector4(0f, 0f, 0.8f, 0f),
				};
				// Fireflies: a lantern some 6 mm across under the abdomen, yellow-green (560 nm), one
				// flash of about half a second every three to eight seconds (Photinus), climbing as it
				// flashes. They fly from knee height to a little over head height, mostly low, at about
				// one to twenty-five cubic metres over a good meadow at the season's height. A lantern
				// is some 20 cd/m², a few hundred times moonlit ground: the radiance is that against the
				// game's night, with a faint halo for the light the dark-adapted eye spreads round it.
				case AirMoteKind.Firefly: return new Traits
				{
					BudgetShare = 0.12f, Box = new Vector3(40f, 2.5f, 40f), Band = new Vector2(0.2f, 2.6f), PerCubicMetre = 0.04f,
					Size = new Vector3(5e-3f, 6e-3f, 7e-3f), Carry = 0.1f,
					Wander = 1.4f, WanderPace = new Vector2(0.15f, 0.5f), WanderVertical = 0.25f,
					Color = new Color(0.55f, 1f, 0.1f, 0f), Radiance = 10f, Cycle = new Vector2(3f, 8f), LitShare = 0.09f,
					Halo = new Vector2(5f, 0.06f),
				};
				// Will-o'-the-wisps: a pale bluish cold flame of marsh gas a hand or two across, at knee
				// to head height, drifting slowly, alight for half a minute and gone. A few in a whole
				// marsh (one to two thousand square metres each) — the rarity is the count.
				case AirMoteKind.Wisp: return new Traits
				{
					BudgetShare = 0f, Box = new Vector3(90f, 1.4f, 90f), Band = new Vector2(0.4f, 1.8f), PerCubicMetre = 0.0005f / 1.4f,
					Size = new Vector3(0.12f, 0.18f, 0.24f), Carry = 0.25f,
					Wander = 3f, WanderPace = new Vector2(0.04f, 0.12f), WanderVertical = 0.15f,
					Color = new Color(0.5f, 0.8f, 1f, 0f), Radiance = 1.2f, Cycle = new Vector2(40f, 90f), LitShare = 0.5f,
					Halo = new Vector2(3f, 0.35f),
				};
				default: return default;
			}
		}

		/// <summary>The quads a kind is given out of a budget. Wisps are so few that they take a fixed handful.</summary>
		public static int ParticlesOf(AirMoteKind kind, int budget)
		{
			return kind == AirMoteKind.Wisp ? 16 : Mathf.Max(16, Mathf.RoundToInt(budget * TraitsOf(kind).BudgetShare));
		}

		/// <summary>
		/// The share of a kind's particles shown: its amount, but never more particles to a cubic metre
		/// than the air holds — a budget bigger than the kind's real count would otherwise thicken it.
		/// </summary>
		public static float ShownShare(float amount, in Traits traits, int particles)
		{
			float volume = traits.Box.x * traits.Box.y * traits.Box.z;
			float full = traits.PerCubicMetre * volume / Mathf.Max(1, particles);
			return Mathf.Clamp01(amount) * Mathf.Clamp01(full);
		}

		// ── Drawing ────────────────────────────────────────────────────

		// The shader's property IDs, apart so the pure model above can be exercised without the engine
		// (a static field initialised by the engine would run on the first touch of any static here).
		private static class Ids
		{
			public static readonly int OriginId = Shader.PropertyToID("_MoteOrigin");
			public static readonly int BoxId = Shader.PropertyToID("_MoteBox");
			public static readonly int BandId = Shader.PropertyToID("_MoteBand");
			public static readonly int TravelId = Shader.PropertyToID("_MoteTravel");
			public static readonly int ShapeId = Shader.PropertyToID("_MoteShape");
			public static readonly int FallId = Shader.PropertyToID("_MoteFall");
			public static readonly int WanderId = Shader.PropertyToID("_MoteWander");
			public static readonly int ColorId = Shader.PropertyToID("_MoteColor");
			public static readonly int PhaseId = Shader.PropertyToID("_MotePhase");
			public static readonly int GlowId = Shader.PropertyToID("_MoteGlow");
			public static readonly int HaloId = Shader.PropertyToID("_MoteHalo");
			public static readonly int ExtraId = Shader.PropertyToID("_MoteExtra");
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("Weather.AirMotes");
		}

		/// <summary>Pollen's seed fluff: its share of the particles, its size (m) and how much faster it settles than a clump.</summary>
		public const float FluffShare = 0.08f, FluffMetres = 0.015f, FluffSettle = 6f;

		private readonly Mesh[] meshes = new Mesh[Kinds.Length];
		private readonly int[] meshParticles = new int[Kinds.Length];
		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private Material material;
		private Shader materialShader;
		private AirMotesMix drawn;

		/// <summary>The mix drawn last frame.</summary>
		public AirMotesMix Drawn => drawn;

		private Material ResolveMaterial(WeatherRenderProfile profile)
		{
			Shader shader = profile != null && profile.AirMotesShader != null ? profile.AirMotesShader : null;
			if (shader == null)
			{
				shader = materialShader != null ? materialShader : Shader.Find(ShaderName);
			}
			if (shader == null)
			{
				return null;
			}
			if (material == null || materialShader != shader)
			{
				DestroyMaterial();
				material = new Material(shader) { name = "Air Motes", hideFlags = HideFlags.HideAndDontSave };
				materialShader = shader;
			}
			return material;
		}

		/// <summary>How far the eye is taken to stand over the ground when nothing else says, m.</summary>
		public const float EyeHeight = 1.7f;

		/// <summary>
		/// Draws the motes for the weather being shown: the climate from the context and the frame, the sun
		/// from the sky, the season from its globals, and the ground under the camera from the sky-occlusion
		/// map (a roof or a canopy over the camera is not the ground, so a height above the eye is not taken).
		/// </summary>
		public void Render(Camera camera, WeatherTierSettings tier, WeatherRenderProfile profile, in WeatherFrame frame, bool hasContext, in WeatherContext context,
			SkyOcclusionMap occlusion)
		{
			if (!hasContext || camera == null)
			{
				drawn = default;
				return;
			}
			Ids.Marker.Begin();
			CelestialState state = SkySystem.Instance != null ? SkySystem.Instance.State : null;
			float sunAltitude = state != null ? state.SunAltitude : context.IsDaylight ? 30f : -30f;
			AirMotesClimate climate = AirMotesClimate.From(context, frame, sunAltitude, Shader.GetGlobalVector(WeatherShaderGlobals.Season));
			Vector3 eye = camera.transform.position;
			float ground = occlusion != null && occlusion.TryGetHeight(eye.x, eye.z, out float height) && height <= eye.y ? height : eye.y - EyeHeight;
			Render(camera, tier, profile, climate, ground);
			Ids.Marker.End();
		}

		/// <param name="climate">The air at the camera (<see cref="AirMotesClimate.From"/>).</param>
		/// <param name="groundUnderCamera">The ground under the camera, m: where a ground band stands where no finer map of the ground reaches.</param>
		public void Render(Camera camera, WeatherTierSettings tier, WeatherRenderProfile profile, in AirMotesClimate climate, float groundUnderCamera)
		{
			drawn = Choose(climate);
			if (camera == null || tier == null)
			{
				return;
			}
			bool any = false;
			foreach (AirMoteKind kind in Kinds)
			{
				any |= drawn[kind] > DrawThreshold;
			}
			if (!any)
			{
				return;
			}
			Material mat = ResolveMaterial(profile);
			if (mat == null)
			{
				return;
			}

			Vector3 origin = camera.transform.position;
			// The surface air's travel since the epoch, at the share the air a couple of metres up
			// moves at: the fog banks' own query (same share, so the same cached sums), so the motes
			// and the mist drift together and every player's stand in the same place.
			SkySystem.AirAt(out float latitude, out WindBelts belts);
			double seconds = WorldMotion.Seconds;
			SurfaceAirDrift.At(WeatherDriver.WorldSeed, latitude, belts, FogLayerView.SurfaceShare, seconds, out double airX, out double airZ, out _);
			// The air is turbulent near the ground: what it carries wanders further in a wind.
			float turbulence = 1f + Mathf.Clamp01(climate.WindMetres / 8f) * 1.5f;
			int budget = Budget(tier.Particles);
			float glintCos = Mathf.Cos(GlintDegrees * Mathf.Deg2Rad);

			var rp = new RenderParams(mat)
			{
				camera = camera,
				matProps = block,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
			};

			for (int k = 0; k < Kinds.Length; k++)
			{
				AirMoteKind kind = Kinds[k];
				float amount = drawn[kind];
				if (amount <= DrawThreshold)
				{
					continue;
				}
				Traits traits = TraitsOf(kind);
				int particles = ParticlesOf(kind, budget);
				if (meshes[k] == null || meshParticles[k] != particles)
				{
					DestroyMesh(k);
					// The precipitation's field layout (seed in the unit box, corner, show threshold and
					// jitters), each kind on its own seed so no two kinds share a pattern.
					meshes[k] = PrecipitationField.BuildMesh(particles, 7919 + 104729 * k);
					meshes[k].name = $"Air Motes {kind} ({particles})";
					meshParticles[k] = particles;
				}
				bool band = traits.Band.y > 0f;
				Vector3 box = traits.Box;
				// Wrapped in double before a float sees it: the drift is kilometres after a day.
				float travelX = (float)WorldMotion.Repeat(airX * traits.Carry, box.x);
				float travelZ = (float)WorldMotion.Repeat(airZ * traits.Carry, box.z);
				float settled = band ? 0f : (float)WorldMotion.Repeat(traits.Settle * seconds, box.y * SpeedSteps);
				float far = band ? 0.5f * box.x : 0.5f * Mathf.Min(box.x, box.y);
				bool passive = traits.Radiance <= 0f;

				block.Clear();
				block.SetVector(Ids.OriginId, new Vector4(origin.x, origin.y, origin.z, groundUnderCamera));
				block.SetVector(Ids.BoxId, new Vector4(box.x, box.y, box.z, band ? 1f : 0f));
				// Faded out over the outer two fifths of the box, so nothing is seen to wrap.
				block.SetVector(Ids.BandId, new Vector4(traits.Band.x, traits.Band.y, 0.6f * far, far));
				block.SetVector(Ids.TravelId, new Vector4(travelX, settled, travelZ, 0f));
				block.SetVector(Ids.ShapeId, new Vector4(ShownShare(amount, traits, particles), traits.Size.y, traits.Size.z, (float)kind));
				block.SetVector(Ids.FallId, new Vector4(traits.SettleSpread.x, traits.SettleSpread.y, traits.Size.x,
					kind == AirMoteKind.Firefly ? 1f : kind == AirMoteKind.Wisp ? 2f : 0f));
				block.SetVector(Ids.WanderId, new Vector4(traits.Wander * (passive ? turbulence : 1f), traits.WanderPace.x, traits.WanderPace.y, traits.WanderVertical));
				block.SetColor(Ids.ColorId, traits.Color);
				block.SetVector(Ids.PhaseId, traits.Phase);
				block.SetVector(Ids.GlowId, new Vector4(traits.Radiance, traits.Cycle.x, traits.Cycle.y, traits.LitShare));
				block.SetVector(Ids.HaloId, new Vector4(traits.Halo.x, traits.Halo.y, glintCos, kind == AirMoteKind.Ice ? GlintPeak() : 0f));
				block.SetVector(Ids.ExtraId, new Vector4(kind == AirMoteKind.Pollen ? FluffShare : 0f, FluffMetres, FluffSettle, PrecipitationField.SubPixelSigma));
				rp.worldBounds = new Bounds(new Vector3(origin.x, band ? groundUnderCamera : origin.y, origin.z), new Vector3(box.x, band ? 200f : box.y, box.z) * 1.5f);
				Graphics.RenderMesh(rp, meshes[k], 0, Matrix4x4.identity);
			}
		}

		private void DestroyMesh(int k)
		{
			if (meshes[k] != null)
			{
				Destroy(meshes[k]);
				meshes[k] = null;
			}
		}

		private void DestroyMaterial()
		{
			if (material != null)
			{
				Destroy(material);
				material = null;
			}
		}

		private static void Destroy(Object target)
		{
			if (Application.isPlaying)
			{
				Object.Destroy(target);
			}
			else
			{
				Object.DestroyImmediate(target);
			}
		}

		public void Dispose()
		{
			for (int k = 0; k < meshes.Length; k++)
			{
				DestroyMesh(k);
			}
			DestroyMaterial();
		}
	}
}
