using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Water
{
	/// <summary>
	/// Connects the sea to the world it is in: the gravity that sets its wavelengths, the wind that
	/// raises its waves, the weather overhead, the climate that colours it, and the moons that move
	/// its tide.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Separate from <see cref="WaterSurface"/> on purpose. The surface knows how to be a sea — a
	/// gravity and a wind in, waves out; a level in, a surface at that height — and nothing about
	/// planets. Everything reaching into FishMMO's own weather and celestial systems is here, so a
	/// scene with no atlas entry still gets a working sea from whatever the component is set to.
	/// </para>
	/// <para>
	/// <b>Colour goes through a property block, never the material.</b> Writing the climate into
	/// the shared asset would leave a modified <c>.mat</c> in the tree in whichever scene happened
	/// to be open last. One extra draw call is a cheap price for a clean working copy.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[AddComponentMenu("FishMMO/Water/Water Environment")]
	[RequireComponent(typeof(WaterSurface))]
	public sealed class WaterEnvironment : MonoBehaviour
	{
		private static readonly int ShallowId = Shader.PropertyToID("_ShallowColor");
		private static readonly int DeepId = Shader.PropertyToID("_DeepColor");
		private static readonly int DensityId = Shader.PropertyToID("_WaterDensity");

		[Header("Celestial")]
		[Tooltip("Take surface gravity from the body. It sets every wavelength and wave speed in the sea.")]
		public bool DriveGravity = true;

		[Header("Wind and weather")]
		[Tooltip("Take the wind from the world's weather. Off leaves the surface's own settings alone.")]
		public bool DriveWind = true;
		[Tooltip("Scales the wind the weather reports, for a sheltered bay or an exposed cape.")]
		[Range(0f, 2f)] public float Fetch = 1f;
		[Tooltip("How quickly the sea answers a change in the wind, per world second: its memory of the wind is 1 / this, the same on every client.")]
		[Range(0.01f, 2f)] public float Responsiveness = 0.15f;
		[Tooltip("Let an unstable, stormy sky raise the sea and sharpen its crests.")]
		public bool DriveStorms = true;
		[Tooltip("Darken the water under cloud, the same way the ground is darkened.")]
		public bool DriveCloudShadow = true;

		[Header("Sea state")]
		[Tooltip("Open water upwind when there is no planet to measure it on, in km.")]
		[Range(0.5f, 800f)] public float FallbackFetchKm = 20f;
		[Tooltip("The furthest upwind the fetch is measured, in km. Past this the sea is developed anyway.")]
		[Range(10f, 2000f)] public float MaximumFetchKm = 400f;
		[Tooltip("Swell from distant weather, in metres. A real beach is never dead flat even with an offshore wind.")]
		[Range(0f, 3f)] public float SwellMetres = 0.3f;
		[Tooltip("Force the wind, m/s, for testing. Negative uses the weather.")]
		public float WindOverride = -1f;
		[Tooltip("Force the heading the wind blows TOWARD, degrees, for testing. Negative uses the weather.")]
		public float HeadingOverride = -1f;

		[Header("Climate")]
		[Tooltip("Colour the water from the world's climate: clear cold blue through warm, productive green.")]
		public bool DriveColor = true;

		[Header("Tide")]
		[Tooltip("Move the sea level with the moons and the star.")]
		public bool DriveTide = true;
		/// <remarks>
		/// The open-ocean equilibrium tide is half a metre on an Earth-like world; real coasts run
		/// two to twenty times that because a basin resonates and a funnel concentrates. None of
		/// that is geometry the maths can see, so it is this number.
		/// </remarks>
		[Tooltip("Multiplies the open-ocean tide for this coast. 1 is mid-ocean; a funnelled estuary is 10 or more.")]
		[Range(0f, 20f)] public float CoastalAmplification = 2.5f;
		[Tooltip("The most the tide may move the sea, in metres, whatever the moons say. A tide that would go further is scaled down whole, so it still comes in and goes out smoothly — never cut off.")]
		[Range(0f, 30f)] public float MaximumTideMetres = DefaultMaximumTideMetres;

		/// <summary>
		/// The tide's limit a scene starts with. Generated rivers reaching the sea run on to this far below mean
		/// level (RiverSettings.IntertidalMetres), so they meet the sea at the lowest tide too.
		/// </summary>
		public const float DefaultMaximumTideMetres = 2.5f;

		private WaterSurface surface;
		private MeshRenderer meshRenderer;
		private MaterialPropertyBlock block;
		private WorldSceneSettings settings;
		private float windSpeed;
		private float windHeading;
		// The fetch at whole bins of heading (FetchToward): a pure function of the heading.
		private readonly System.Collections.Generic.Dictionary<int, float> fetchByBin = new System.Collections.Generic.Dictionary<int, float>();
		private WorldBody fetchBinsBody;
		private WorldSceneSettings fetchBinsSettings;
		private const float FetchBinDegrees = 10f;

		/// <summary>The tide this frame, in metres above mean sea level.</summary>
		public float Tide { get; private set; }

		/// <summary>The sustained wind this frame, in metres per second.</summary>
		public float Wind => windSpeed;

		/// <summary>Open water upwind of this scene, in metres, measured across the planet.</summary>
		public float FetchMetres { get; private set; }

		/// <summary>Significant wave height, in metres: the mean of the highest third of waves.</summary>
		public float SignificantHeight { get; private set; }

		/// <summary>Peak period of the sea, in seconds.</summary>
		public float PeakPeriod { get; private set; }

		/// <summary>Surface gravity in use, in m/s².</summary>
		public float Gravity => surface != null ? surface.Gravity : WaterWaves.EarthGravity;

		/// <summary>This sea's level for the simulation (<see cref="WaterQuery"/>): the same tide, kept by the synced clock.</summary>
		public SeaTide SimulatedTide { get; } = new SeaTide();
		private WorldSceneSettings tideSettings;
		private bool tideLava;

		private void OnEnable()
		{
			surface = GetComponent<WaterSurface>();
			meshRenderer = GetComponent<MeshRenderer>();
			windAtKnot.Clear();
			windKnotSeconds = double.NaN;
			settings = null;
			tideSettings = null;
			tideLava = surface != null && surface.IsLava;
			RefreshSimulatedTide();
			WaterQuery.RegisterSea(gameObject.scene, SimulatedTide, tideLava);
		}

		private void OnDisable()
		{
			WaterQuery.UnregisterSea(gameObject.scene, SimulatedTide);
		}

		/// <summary>Copies this sea's level, coast and place into <see cref="SimulatedTide"/>: on enable, and when a setting changes.</summary>
		public void RefreshSimulatedTide()
		{
			WorldSceneSettings.TryGetForScene(gameObject.scene, out WorldSceneSettings here);
			SimulatedTide.MeanLevel = surface != null ? surface.MeanSeaLevel : transform.position.y;
			SimulatedTide.Drive = DriveTide;
			SimulatedTide.Amplification = CoastalAmplification;
			SimulatedTide.MaximumMetres = MaximumTideMetres;
			SimulatedTide.Latitude = here != null ? here.Latitude : 0f;
			SimulatedTide.Longitude = here != null ? here.Longitude : 0f;
			SimulatedTide.Body = SceneTime.BodyOf(here);
			SimulatedTide.Invalidate();
		}

		private void OnValidate()
		{
			if (isActiveAndEnabled)
			{
				RefreshSimulatedTide();
			}
		}

#if UNITY_SERVER
		/* A dedicated server draws no sea: the wind, the sea state, the colour, the drawn tide and the motes are all for a
		 * screen it does not have (the fetch alone is a march of hundreds of planet samples, and the surface rebuilt its
		 * waves every frame). What it keeps is the simulation's sea, SeaTide, which WaterQuery answers from the synced
		 * tick and nothing here moves: only its registration follows the scene's settings. */
		private void LateUpdate() => ApplySimulation();
#else
		private void LateUpdate() => Apply();
#endif

		/// <summary>
		/// Keeps the simulation's sea (<see cref="SimulatedTide"/>) registered as the scene's settings and the liquid say:
		/// the one part of <see cref="Apply"/> a dedicated server runs. False when there is no surface.
		/// </summary>
		private bool ApplySimulation()
		{
			if (surface == null)
			{
				surface = GetComponent<WaterSurface>();
				meshRenderer = GetComponent<MeshRenderer>();
			}
			if (surface == null)
			{
				return false;
			}
			if (settings == null)
			{
				WorldSceneSettings.TryGetForScene(gameObject.scene, out settings);
			}
			// The scene's settings can register after this enabled, and a sea can turn to lava: the simulation's sea follows.
			if (tideSettings != settings || tideLava != surface.IsLava)
			{
				tideSettings = settings;
				tideLava = surface.IsLava;
				RefreshSimulatedTide();
				WaterQuery.RegisterSea(gameObject.scene, SimulatedTide, tideLava);
			}
			return true;
		}

		/// <summary>
		/// Brings the sea up to date with the world. Called every frame; public so anything that
		/// renders without a frame loop — a batch capture, an editor preview — can drive it.
		/// </summary>
		public void Apply()
		{
			if (!ApplySimulation())
			{
				return;
			}

			// The sky's clock: the sea's wind, period and tide keep the same time as the clouds.
			// Without a cycle in the scene, still the world clock (an admin's set or hold included), not this machine's.
			double hours = WorldDayNightCycle.SkyClockHours ?? WorldTime.Hours;
			SolarSystemProfile system = SolarSystemProfile.Active;
			/* The world the sky says this is. It was the atlas entry's body alone, so a scene with no
			 * entry — or a preview of another world — had Earth's 9.81 and a sea that looked the
			 * same on every body in the system. */
			WorldBody body = WorldDayNightCycle.BodyFor(settings);
			float latitude = settings != null ? settings.Latitude : 0f;
			float longitude = settings != null ? settings.Longitude : 0f;

			if (DriveGravity)
			{
				surface.Gravity = SurfaceGravity(body);
			}
			if (DriveWind || DriveStorms || DriveCloudShadow)
			{
				ApplyWeather(latitude, hours);
			}
			if (DriveColor)
			{
				ApplyClimate(system, body);
			}
			surface.TideMetres = DriveTide ? Tide = TideAt(system, body, hours, latitude, longitude) : 0f;
			PublishMoteDrift(system, body, latitude);
		}

		private static readonly int MoteDriftId = Shader.PropertyToID("_FishWaterMoteDrift");
		/// <summary>
		/// Where the motes' grid wraps, m: ten thousand of its 0.6 m cells, the period its hash already
		/// has (FishWaterVolume.shader's CellHash), so the wrap lands on the same motes.
		/// </summary>
		private const double MoteWrapMetres = 6000.0;
		/// <summary>The current the wind sets the water near the surface moving at, as a share of the wind.</summary>
		private const float MoteCurrentShare = 0.008f;
		/// <summary>How fast marine snow sinks, m/s.</summary>
		private const double MoteSinkSpeed = 0.012;

		/// <summary>
		/// How far the water's suspended matter has been carried by now (FishWaterVolume.shader's motes):
		/// the current the surface wind sets up, worked out from the shared clock as the fog banks' drift is
		/// (SurfaceAirDrift), and its slow sinking. The shader multiplied the eased wind by the sea's
		/// clock, so the motes slid whenever the wind changed, and stood elsewhere for every player.
		/// </summary>
		private void PublishMoteDrift(SolarSystemProfile system, WorldBody body, float latitude)
		{
			double seconds = WorldMotion.Seconds;
			// The scene's own air, with what its author and its runtime air add: the same sum the fog and the clouds ride.
			SurfaceAirDrift.At(WeatherField.SceneAir(gameObject.scene, seconds), MoteCurrentShare, seconds, out double x, out double z, out _);
			Shader.SetGlobalVector(MoteDriftId, new Vector4(
				(float)WorldMotion.Repeat(x, MoteWrapMetres),
				(float)WorldMotion.Repeat(-MoteSinkSpeed * seconds, MoteWrapMetres),
				(float)WorldMotion.Repeat(z, MoteWrapMetres),
				1f));
		}

		/// <summary>
		/// Surface gravity of a world, in m/s².
		/// </summary>
		/// <remarks>
		/// g = GM/R², and the project's only size is radius, so mass comes from radius cubed at
		/// constant density — the same convention <c>CelestialMath.TidalHeating</c> uses. The cube
		/// over the square leaves gravity simply proportional to radius: a body half Earth's size
		/// pulls at half a gravity. It over-rates a low-density world, and it is the only answer
		/// available from the data the project carries.
		/// </remarks>
		public static float SurfaceGravity(WorldBody body)
		{
			// One formula for the world's pull, shared with what falls through its air.
			return Mathf.Clamp(SurfacePhysics.Gravity(body), 0.05f, 30f);
		}

		// ── The sea state, a pure function of the world clock ──────────────
		//
		// 2026-10-15 ("everything needs to be deterministic and properly driven by WorldTime"): the sea was eased per client
		// on its own frame steps from whatever wind it had last held, a client that joined snapped to the wind of the
		// moment while the others lagged it, and its fetch was re-measured whenever its own eased heading swung twelve
		// degrees past the last — so two players on one coast could see different seas, surf and breakers for as long as
		// they played. Every figure below is now worked out from the moment alone: the sea's memory of the wind is a
		// weighted average over fixed knots of the world clock, and the fetch is drawn between fixed bins of heading.

		/// <summary>The sea's state at a moment: what every player's sea shows then.</summary>
		private struct SeaState
		{
			public float Wind, Heading, Fetch, Height, Period, Storm, Cover;
		}

		/// <summary>Weights in the sea's memory of the wind: knots back to where the oldest weighs e^-6 of the newest.</summary>
		private const int MemoryKnots = 12;

		/// <summary>The sea's memory's knot spacing, world seconds: half its time constant (1 / <see cref="Responsiveness"/>).</summary>
		private double MemoryKnotSeconds => System.Math.Max(0.25, 0.5 / System.Math.Max(0.01f, Responsiveness));

		/// <summary>The wind sampled at each knot of the world clock, by knot: a pure function of the knot, kept while asked about.</summary>
		private readonly System.Collections.Generic.Dictionary<long, Vector2> windAtKnot = new System.Collections.Generic.Dictionary<long, Vector2>();
		private double windKnotSeconds = double.NaN;
		private uint windRevision;

		/// <summary>
		/// The sustained wind over this sea at a world moment, m/s (blowing toward): the scene's open air with what its
		/// author and its runtime air add (<see cref="WeatherField.OpenAirWithOffsets"/>), not a storm's gusts — a sea is
		/// hours of wind over kilometres of fetch and does not answer a gust. The world's own air where the scene has no
		/// weather registered (an editor preview).
		/// </summary>
		private bool AirAt(double worldSeconds, out WeatherDriver.Synoptic air)
		{
			var position = new Vector2(transform.position.x, transform.position.z);
			if (WeatherQuery.TryGetTimeline(gameObject.scene, out WeatherTimeline timeline)
				&& WeatherField.OpenAirWithOffsets(timeline, settings, position, worldSeconds, out air))
			{
				return true;
			}
			float latitude = settings != null ? settings.Latitude : 0f;
			air = WeatherDriver.Sample(WeatherDriver.WorldSeed, position, worldSeconds, latitude, 0.5f);
			return true;
		}

		private Vector2 WindAtKnot(long knot, double spacing)
		{
			uint revision = WeatherQuery.TryGetTimeline(gameObject.scene, out WeatherTimeline timeline) ? timeline.Revision : 0u;
			if (spacing != windKnotSeconds || revision != windRevision || windAtKnot.Count > 4 * MemoryKnots)
			{
				// Another memory, or the scene's air was changed: what was kept was of another wind.
				windAtKnot.Clear();
				windKnotSeconds = spacing;
				windRevision = revision;
			}
			if (!windAtKnot.TryGetValue(knot, out Vector2 wind))
			{
				AirAt(knot * spacing, out WeatherDriver.Synoptic air);
				wind = air.Wind;
				windAtKnot[knot] = wind;
			}
			return wind;
		}

		/// <summary>
		/// The sea's memory of the wind at a knot: the winds at it and the <see cref="MemoryKnots"/> before it, weighted
		/// down exponentially into the past — a function of the knot alone, so every machine agrees. Speed and the wind's
		/// way (the vector) are remembered apart, so a wind that backs does not read as a calm.
		/// </summary>
		private void MemoryAtKnot(long knot, double spacing, out float speed, out Vector2 way)
		{
			const double Decay = 0.6065306597126334; // e^-0.5: one knot is half the time constant
			double weight = 1.0, total = 0.0, sumSpeed = 0.0;
			Vector2 sumWay = Vector2.zero;
			for (int i = 0; i <= MemoryKnots; i++)
			{
				Vector2 wind = WindAtKnot(knot - i, spacing);
				sumSpeed += weight * wind.magnitude;
				sumWay += (float)weight * wind;
				total += weight;
				weight *= Decay;
			}
			speed = (float)(sumSpeed / total);
			way = sumWay / (float)total;
		}

		/// <summary>The sea at a moment of world time: its sustained wind and heading, fetch, height, period, roughness and cloud over it.</summary>
		private SeaState SeaAt(double worldSeconds)
		{
			var state = new SeaState();
			float speed;
			Vector2 way;
			if (WindOverride >= 0f)
			{
				speed = WindOverride;
				way = Vector2.up;
			}
			else
			{
				// Between the memories at the two knots either side of the moment, so it moves smoothly.
				double spacing = MemoryKnotSeconds;
				long knot = (long)System.Math.Floor(worldSeconds / spacing);
				float t = (float)((worldSeconds - knot * spacing) / spacing);
				MemoryAtKnot(knot, spacing, out float speedA, out Vector2 wayA);
				MemoryAtKnot(knot + 1, spacing, out float speedB, out Vector2 wayB);
				speed = Mathf.Lerp(speedA, speedB, t) * Mathf.Max(0f, Fetch);
				way = Vector2.Lerp(wayA, wayB, t);
			}
			state.Wind = speed;
			state.Heading = HeadingOverride >= 0f ? Mathf.Repeat(HeadingOverride, 360f)
				: way.sqrMagnitude > 1e-6f ? Mathf.Repeat(Mathf.Atan2(way.x, way.y) * Mathf.Rad2Deg, 360f) : 0f;

			AirAt(worldSeconds, out WeatherDriver.Synoptic now);
			/* An unstable, low-pressure sky is a rough sea. Wave height and steepness both go up, which is the
			 * difference between a swell and a storm running: the same wind under a settled high makes a far gentler
			 * sea than under a deepening low. */
			state.Storm = Mathf.Clamp01(now.Instability * 0.7f + Mathf.Clamp01(-now.Pressure) * 0.5f);
			state.Cover = Mathf.Clamp01(now.Mesoscale * WeatherDriver.MesoscaleAmplitude);

			float gravity = surface != null ? surface.Gravity : WaterWaves.EarthGravity;
			state.Fetch = FetchToward(state.Heading);
			float local = WaterSeaState.SignificantHeight(state.Wind, state.Fetch, gravity);
			/* Swell from weather elsewhere, combined in quadrature because the two are independent wave fields and it is
			 * their ENERGIES that add. Without it an offshore wind leaves a mirror-flat sea, and no real coast is ever that. */
			state.Height = Mathf.Sqrt(local * local + SwellMetres * SwellMetres);
			state.Period = PeriodOf(state.Wind, state.Fetch, gravity, local);
			return state;
		}

		/// <summary>
		/// Open water upwind for a heading, m: measured at whole bins of heading (<see cref="FetchBinDegrees"/>) and drawn
		/// between the two either side, so it is a smooth function of the heading alone — never a record of the heading
		/// some client's wind last swung past.
		/// </summary>
		private float FetchToward(float heading)
		{
			WorldBody body = WorldDayNightCycle.BodyFor(settings);
			if (fetchBinsBody != body || fetchBinsSettings != settings)
			{
				fetchByBin.Clear();
				fetchBinsBody = body;
				fetchBinsSettings = settings;
			}
			int bins = Mathf.RoundToInt(360f / FetchBinDegrees);
			float at = Mathf.Repeat(heading, 360f) / FetchBinDegrees;
			int lo = Mathf.FloorToInt(at) % bins, hi = (lo + 1) % bins;
			return Mathf.Lerp(FetchOfBin(body, lo), FetchOfBin(body, hi), at - Mathf.Floor(at));
		}

		private float FetchOfBin(WorldBody body, int bin)
		{
			if (!fetchByBin.TryGetValue(bin, out float fetch))
			{
				fetch = MeasureFetch(body, settings, bin * FetchBinDegrees);
				fetchByBin[bin] = fetch;
			}
			return fetch;
		}

		private void ApplyWeather(float latitude, double hours)
		{
			SeaState sea = SeaAt(hours * 3600.0);
			if (DriveWind)
			{
				windSpeed = sea.Wind;
				windHeading = sea.Heading;
			}
			if (DriveStorms)
			{
				surface.WaveScale = Mathf.Lerp(0.85f, 1.6f, sea.Storm);
				surface.Choppiness = Mathf.Lerp(0.45f, 0.85f, sea.Storm);
			}
			if (DriveCloudShadow)
			{
				// Never to black: even under heavy cloud the sea is lit by the whole sky.
				surface.CloudShadow = Mathf.Lerp(1f, 0.35f, sea.Cover);
			}
			if (DriveWind)
			{
				FetchMetres = sea.Fetch;
				SignificantHeight = sea.Height;
				PeakPeriod = sea.Period;
				/* The FFT is handed the wind that would raise THIS sea if it were developed, so its height and its peak
				 * wavelength both come out consistent with the fetch. The direction is the real wind's. The breakers are
				 * driven from this same sea: the surface works out where they break from SignificantHeight, and the shore
				 * sets their rhythm from PeakPeriod. */
				surface.WindSpeed = WaterSeaState.EquivalentWind(SignificantHeight, surface.Gravity);
				surface.WindDirectionDegrees = windHeading;
				surface.Rebuild();
			}
		}

		/// <summary>The period waves arrive at, s, from the local sea of this wind and fetch and the swell.</summary>
		private float PeriodOf(float wind, float fetch, float gravity, float localHeight)
		{
			float localPeriod = WaterSeaState.PeakPeriod(wind, fetch, gravity);
			const float SwellPeriod = 9f;
			float localEnergy = localHeight * localHeight;
			float swellEnergy = SwellMetres * SwellMetres;
			float energy = localEnergy + swellEnergy;
			return energy > 1e-6f
				? (localEnergy * localPeriod + swellEnergy * SwellPeriod) / energy
				: SwellPeriod;
		}

		/// <summary>
		/// The sea's peak period at a moment of world time, s: the same sea every client shows then
		/// (<see cref="PeakPeriod"/> is this at the moment shown). What the shore holds for each window of its wave count
		/// (WaterShore), which every player must agree on. NaN when the sea is not driven by the weather.
		/// </summary>
		public float PeakPeriodAt(double worldSeconds)
		{
			if (!DriveWind || surface == null)
			{
				return float.NaN;
			}
			return SeaAt(worldSeconds).Period;
		}
		/// <summary>
		/// Open water upwind of a scene, in metres, marched across the planet's own surface.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Walks the great circle UPWIND — against the direction the wind blows toward — sampling
		/// the same surface function the globe is baked from, first across any land the scene
		/// itself stands on, then across water until the first land again. That run of water is the
		/// fetch.
		/// </para>
		/// <para>
		/// It is why an onshore wind and an offshore wind of the same strength make completely
		/// different beaches, and why a sheltered bay behind a headland is calm when the open coast
		/// beside it is not — none of which has to be authored, because the planet already knows
		/// where its coastlines are.
		/// </para>
		/// </remarks>
		private float MeasureFetch(WorldBody body, WorldSceneSettings scene, float windHeading)
		{
			if (body == null || scene == null)
			{
				return FallbackFetchKm * 1000f;
			}

			uint seed = body.ResolvedTerrainSeed;
			/* The atlas radius, the globe scenes are cut from: a kilometre of fetch is a kilometre
			 * of the sea the scene's own coast faces, measured on the same ground. */
			double radiusKm = PlanetSurface.SceneRadiusKm(body);
			double latitude = scene.Latitude * Mathf.Deg2Rad;
			double longitude = scene.Longitude * Mathf.Deg2Rad;
			// Upwind: the heading is where the wind blows TOWARD.
			double bearing = (windHeading + 180.0) * Mathf.Deg2Rad;

			const float StepKm = 1f;
			float maximum = Mathf.Max(StepKm, MaximumFetchKm);
			bool reachedWater = false;
			float waterStart = 0f;

			for (float travelled = StepKm; travelled <= maximum; travelled += StepKm)
			{
				// Great-circle destination.
				double angular = travelled / radiusKm;
				double sinLat = System.Math.Sin(latitude) * System.Math.Cos(angular)
					+ System.Math.Cos(latitude) * System.Math.Sin(angular) * System.Math.Cos(bearing);
				double lat2 = System.Math.Asin(System.Math.Max(-1.0, System.Math.Min(1.0, sinLat)));
				double lon2 = longitude + System.Math.Atan2(
					System.Math.Sin(bearing) * System.Math.Sin(angular) * System.Math.Cos(latitude),
					System.Math.Cos(angular) - System.Math.Sin(latitude) * sinLat);

				float altitude = PlanetSurface.AltitudeMetresAt(seed, body,
					lat2 * Mathf.Rad2Deg, lon2 * Mathf.Rad2Deg);
				bool water = altitude < 0f;

				if (!reachedWater)
				{
					// Still crossing the scene's own land toward the sea it faces.
					if (water)
					{
						reachedWater = true;
						waterStart = travelled;
					}
					else if (travelled > 30f)
					{
						// Thirty kilometres of land upwind: the wind is coming off a continent.
						return 0f;
					}
					continue;
				}
				if (!water)
				{
					return (travelled - waterStart) * 1000f;
				}
			}
			return reachedWater ? (maximum - waterStart) * 1000f : 0f;
		}

		/// <summary>
		/// Colours the water from the world's own climate.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Water is not blue everywhere, and the reason is biology. Cold, barren open ocean is the
		/// clearest water on Earth and a deep blue, because almost nothing in it absorbs the green;
		/// a warm, productive, sediment-fed coast is green and you can see a few metres. So the
		/// absorption and the shallow tint are driven from mean temperature and how much water the
		/// world has — the same two numbers the biome resolver uses.
		/// </para>
		/// <para>
		/// A world with no air is the limiting case: nothing lives in it, nothing clouds it, and it
		/// is as clear as distilled water.
		/// </para>
		/// </remarks>
		private void ApplyClimate(SolarSystemProfile system, WorldBody body)
		{
			if (meshRenderer == null)
			{
				return;
			}
			BiomeWorldConditions conditions = body != null
				? BiomeWorldConditions.For(system, body)
				: BiomeWorldConditions.Earthlike;

			/* Productivity: warm and wet is green soup, cold and barren is blue glass. Measured against
			 * an Earth-like world, because the conditions' temperature is absolute (0 at freezing) and
			 * these weights were set when it read 0 at the home world's own warmth: taken raw, every
			 * temperate sea would have read a fifth greener than it was tuned to. */
			float warmth = conditions.MeanTemperature - BiomeWorldConditions.Earthlike.MeanTemperature;
			float productivity = conditions.Atmosphere == AtmosphereKind.None
				? 0f
				: Mathf.Clamp01(0.45f + warmth * 0.45f + (conditions.Water - 0.5f) * 0.4f);

			/* Absorption per metre, per channel. Red goes first in any water; what productivity
			 * changes is the green and blue — clear ocean lets blue run for tens of metres, a
			 * productive coast eats it within a few. */
			var clear = new Vector4(0.30f, 0.055f, 0.030f, 0f);
			var turbid = new Vector4(0.48f, 0.20f, 0.24f, 0f);
			Vector4 density = Vector4.Lerp(clear, turbid, productivity);

			Color shallow = Color.Lerp(new Color(0.30f, 0.66f, 0.74f), new Color(0.42f, 0.70f, 0.50f), productivity);
			Color deep = Color.Lerp(new Color(0.01f, 0.09f, 0.20f), new Color(0.02f, 0.14f, 0.16f), productivity);

			block ??= new MaterialPropertyBlock();
			meshRenderer.GetPropertyBlock(block);
			block.SetVector(DensityId, density);
			block.SetColor(ShallowId, shallow);
			block.SetColor(DeepId, deep);
			meshRenderer.SetPropertyBlock(block);
		}

		private float TideAt(SolarSystemProfile system, WorldBody body, double hours, float latitude, float longitude)
		{
			if (system == null || body == null)
			{
				return 0f;
			}
			// The simulation's function (SeaTide), so the sea drawn stands where swimmers float.
			return SeaTide.TideMetres(system, body, hours, latitude, longitude, CoastalAmplification, MaximumTideMetres);
		}

		/// <summary>
		/// The tide at the coast, in metres: the equilibrium tide amplified for the coast, and scaled
		/// down WHOLE when it could pass the most this scene allows.
		/// </summary>
		/// <param name="equilibrium">The open-ocean tide now (PlanetTides.HeightMetres).</param>
		/// <param name="reach">The most that tide could stand from mean level here, with the perturbers where they are.</param>
		/// <param name="amplification">How much the coast magnifies it.</param>
		/// <param name="maximum">The most the tide may move the sea.</param>
		/// <remarks>
		/// <para>
		/// <b>Scaled, never clipped.</b> The limit is a level-design tool — the terrain is fixed and
		/// the waterline is not, and two metres of tide on a gentle beach moves the shore tens of
		/// metres — and it used to be a clamp. On Arthis, whose moon Helis orbits under fourteen of
		/// its radii out, the equilibrium tide is ninety metres; amplified and clamped, the sea sat at
		/// the top of the clamp for hours, fell five metres almost at once, sat at the bottom, and
		/// jumped back — the whole sea visibly lurching with the moon. Divided by its own reach
		/// instead, a tide of any size keeps its shape — two highs a moon-day, springs and neaps — and
		/// its highest high water is the maximum. The reach moves only as the perturbers move north
		/// and south and nearer and further, so the scale does too: slowly, and smoothly.
		/// </para>
		/// <para>
		/// The clamp stays, and never binds: the reach bounds the tide, so it is only a guard.
		/// </para>
		/// </remarks>
		public static float ScaledTide(double equilibrium, double reach, float amplification, float maximum)
			=> SeaTide.ScaledTide(equilibrium, reach, amplification, maximum);
	}
}
