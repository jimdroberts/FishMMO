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
		[Tooltip("How quickly the sea answers a change in the wind. A real sea takes hours.")]
		[Range(0.01f, 2f)] public float Responsiveness = 0.15f;
		[Tooltip("Let an unstable, stormy sky raise the sea and sharpen its crests.")]
		public bool DriveStorms = true;
		[Tooltip("Darken the water under cloud, the same way the ground is darkened.")]
		public bool DriveCloudShadow = true;

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

		/// <summary>The tide this frame, in metres above mean sea level.</summary>
		public float Tide { get; private set; }

		/// <summary>The sustained wind this frame, in metres per second.</summary>
		public float Wind => windSpeed;

		/// <summary>Surface gravity in use, in m/s².</summary>
		public float Gravity => surface != null ? surface.Gravity : WaterWaves.EarthGravity;

		private void OnEnable()
		{
			surface = GetComponent<WaterSurface>();
			meshRenderer = GetComponent<MeshRenderer>();
			primed = false;
			settings = null;
		}

		private void LateUpdate()
		{
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
				float speed = air.Wind.magnitude * Mathf.Max(0f, Fetch);
				float heading = Mathf.Repeat(Mathf.Atan2(air.Wind.x, air.Wind.y) * Mathf.Rad2Deg, 360f);

				if (!primed)
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
				if (!Mathf.Approximately(surface.WindSpeed, windSpeed)
					|| !Mathf.Approximately(surface.WindDirectionDegrees, windHeading))
				{
					surface.WindSpeed = windSpeed;
					surface.WindDirectionDegrees = windHeading;
				}
				surface.Rebuild();
			}
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
