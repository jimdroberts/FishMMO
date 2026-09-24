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
		private static readonly int SurfHeightId = Shader.PropertyToID("_ShoreWaveHeight");
		private static readonly int SurfLengthId = Shader.PropertyToID("_ShoreWaveLength");

		[Header("Celestial")]
		[Tooltip("Take surface gravity from the body. It sets every wavelength and wave speed in the sea.")]
		public bool DriveGravity = true;

		[Header("Wind and weather")]
		[Tooltip("Take the wind from the world's weather. Off leaves the surface's own settings alone.")]
		public bool DriveWind = true;
		[Tooltip("Scales the wind the weather reports, for a sheltered bay or an exposed cape.")]
		[Range(0f, 2f)] public float Fetch = 1f;
		[Tooltip("How quickly the sea answers a change in the wind. A real sea takes hours.")]
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
		[Tooltip("The most the tide may move the sea, in metres, whatever the moons say.")]
		[Range(0f, 30f)] public float MaximumTideMetres = 2.5f;

		private WaterSurface surface;
		private MeshRenderer meshRenderer;
		private MaterialPropertyBlock block;
		private WorldSceneSettings settings;
		private float windSpeed;
		private float windHeading;
		private bool primed;
		private float fetchHeading = float.NaN;
		private WorldBody fetchBody;

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

		private void OnEnable()
		{
			surface = GetComponent<WaterSurface>();
			meshRenderer = GetComponent<MeshRenderer>();
			primed = false;
			settings = null;
		}

		private void LateUpdate() => Apply();

		/// <summary>
		/// Brings the sea up to date with the world. Called every frame; public so anything that
		/// renders without a frame loop — a batch capture, an editor preview — can drive it.
		/// </summary>
		public void Apply()
		{
			if (surface == null)
			{
				surface = GetComponent<WaterSurface>();
				meshRenderer = GetComponent<MeshRenderer>();
			}
			if (surface == null)
			{
				return;
			}
			if (settings == null)
			{
				WorldSceneSettings.TryGetForScene(gameObject.scene, out settings);
			}

			double hours = WorldTime.UnanchoredHours();
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = settings != null ? settings.Body : null;
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
			if (body == null)
			{
				return WaterWaves.EarthGravity;
			}
			float radii = Mathf.Max(1f, body.SkyRadiusKm) / (float)PlanetTides.EarthRadiusKm;
			return Mathf.Clamp(WaterWaves.EarthGravity * radii, 0.05f, 30f);
		}

		private void ApplyWeather(float latitude, double hours)
		{
			/* The prevailing synoptic field: banded by latitude, drifting with the world clock, the
			 * same numbers the clouds and the weather director run on. Nothing is random, so two
			 * clients looking at the same coast at the same moment see the same sea.
			 *
			 * The SUSTAINED wind, not a storm cell's gusts, and that is physics rather than
			 * convenience: a fully developed sea is hours of wind over kilometres of fetch and does
			 * not answer a gust that lasts ten seconds. The gusts are already in the picture, as
			 * the patches of ruffled water the shader drags across the surface. */
			var position = new Vector2(transform.position.x, transform.position.z);
			WeatherDriver.Synoptic air = WeatherDriver.Sample(
				WeatherDriver.WorldSeed, position, hours * 3600.0, latitude, 0.5f);

			if (DriveWind)
			{
				float speed = WindOverride >= 0f ? WindOverride : air.Wind.magnitude * Mathf.Max(0f, Fetch);
				float heading = HeadingOverride >= 0f
					? Mathf.Repeat(HeadingOverride, 360f)
					: Mathf.Repeat(Mathf.Atan2(air.Wind.x, air.Wind.y) * Mathf.Rad2Deg, 360f);

				if (!primed || WindOverride >= 0f)
				{
					windSpeed = speed;
					windHeading = heading;
					primed = true;
				}
				else
				{
					/* Eased, because a sea has memory. The wind can back forty degrees in a minute
					 * and the swell will still run the old way for hours; snapping the wave
					 * directions to the current wind pivots the whole surface at once, which
					 * nothing in nature does. */
					float step = Mathf.Clamp01(Time.deltaTime * Responsiveness);
					windSpeed = Mathf.Lerp(windSpeed, speed, step);
					windHeading = Mathf.MoveTowardsAngle(windHeading, heading, 360f * step);
				}
			}

			if (DriveStorms)
			{
				/* An unstable, low-pressure sky is a rough sea. Wave height and steepness both go
				 * up, which is the difference between a swell and a storm running: the same wind
				 * speed under a settled high makes a far gentler sea than under a deepening low. */
				float storm = Mathf.Clamp01(air.Instability * 0.7f + Mathf.Clamp01(-air.Pressure) * 0.5f);
				surface.WaveScale = Mathf.Lerp(0.85f, 1.6f, storm);
				surface.Choppiness = Mathf.Lerp(0.45f, 0.85f, storm);
			}

			if (DriveCloudShadow)
			{
				float cover = Mathf.Clamp01(WeatherDriver.MesoscaleCoverAt(
					WeatherDriver.WorldSeed, position, hours * 3600.0, latitude, 0.5f));
				// Never to black: even under heavy cloud the sea is lit by the whole sky.
				surface.CloudShadow = Mathf.Lerp(1f, 0.35f, cover);
			}

			if (DriveWind)
			{
				ApplySeaState(latitude);
			}
		}

		/// <summary>
		/// Turns the wind into a sea, through the fetch the planet actually has upwind.
		/// </summary>
		private void ApplySeaState(float latitude)
		{
			float gravity = surface.Gravity;
			WorldBody body = settings != null ? settings.Body : null;

			// The march is a few hundred planet samples, so it is redone only when the wind has
			// swung far enough to be blowing across different water.
			if (float.IsNaN(fetchHeading) || fetchBody != body
				|| Mathf.Abs(Mathf.DeltaAngle(fetchHeading, windHeading)) > 12f)
			{
				FetchMetres = MeasureFetch(body, settings, windHeading);
				fetchHeading = windHeading;
				fetchBody = body;
			}

			float local = WaterSeaState.SignificantHeight(windSpeed, FetchMetres, gravity);
			/* Swell from weather elsewhere, combined in quadrature because the two are independent
			 * wave fields and it is their ENERGIES that add. Without it an offshore wind leaves a
			 * mirror-flat sea, and no real coast is ever that. */
			SignificantHeight = Mathf.Sqrt(local * local + SwellMetres * SwellMetres);
			/* Energy-weighted between the local sea and the swell, not the longer of the two.
			 * Taking the maximum handed a gale's short, steep chop the swell's lazy seven-second
			 * period — the long rolling lines of a calm day under a storm — even though the local
			 * sea was carrying twenty times the energy. Whichever field has the energy sets the
			 * rhythm the waves arrive to. */
			float localPeriod = WaterSeaState.PeakPeriod(windSpeed, FetchMetres, gravity);
			const float SwellPeriod = 9f;
			float localEnergy = local * local;
			float swellEnergy = SwellMetres * SwellMetres;
			float energy = localEnergy + swellEnergy;
			PeakPeriod = energy > 1e-6f
				? (localEnergy * localPeriod + swellEnergy * SwellPeriod) / energy
				: SwellPeriod;

			/* The FFT is handed the wind that would raise THIS sea if it were developed, so its
			 * height and its peak wavelength both come out consistent with the fetch. The direction
			 * is the real wind's. */
			surface.WindSpeed = WaterSeaState.EquivalentWind(SignificantHeight, gravity);
			surface.WindDirectionDegrees = windHeading;
			surface.Rebuild();

			/* The surf is driven from the same sea rather than being a number somebody typed.
			 * Breakers stand at roughly the significant height offshore — the shader's own
			 * shoaling grows them from there as the water shallows — and they arrive at the peak
			 * period, so their spacing on the beach is the sea's own wavelength. */
			if (meshRenderer != null)
			{
				block ??= new MaterialPropertyBlock();
				meshRenderer.GetPropertyBlock(block);
				block.SetFloat(SurfHeightId, SignificantHeight * 0.5f);
				block.SetFloat(SurfLengthId, Mathf.Clamp(WaterSeaState.Wavelength(PeakPeriod, gravity), 6f, 160f));
				meshRenderer.SetPropertyBlock(block);
			}
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
			double radiusKm = Mathf.Max(1f, body.SkyRadiusKm);
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

			// Productivity: warm and wet is green soup, cold and barren is blue glass.
			float productivity = conditions.Atmosphere == AtmosphereKind.None
				? 0f
				: Mathf.Clamp01(0.45f + conditions.MeanTemperature * 0.45f + (conditions.Water - 0.5f) * 0.4f);

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
			double equilibrium = PlanetTides.HeightMetres(system, body, hours, latitude, longitude);
			/* Clamped, and the clamp is a level-design tool rather than a safety rail. The terrain
			 * is fixed and the waterline is not: two metres of tide on a gentle beach moves the
			 * shore tens of metres, and everything placed on dry sand is then in the sea. */
			return Mathf.Clamp((float)equilibrium * Mathf.Max(0f, CoastalAmplification),
				-MaximumTideMetres, MaximumTideMetres);
		}
	}
}
