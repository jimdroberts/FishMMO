using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Water
{
	/// <summary>
	/// Connects the sea to the world it is in: the wind that raises its waves and the moons that
	/// move its tide.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Separate from <see cref="WaterSurface"/> on purpose. The surface knows how to be a sea — a
	/// wind speed in, waves out, a level in, a surface at that height — and nothing about planets.
	/// Everything that reaches into FishMMO's own weather and celestial systems is here, so a scene
	/// with no atlas entry still gets a working sea from whatever the component is set to.
	/// </para>
	/// <para>
	/// <b>The sustained wind, not the gusts.</b> The waves are driven from the synoptic field
	/// rather than from a storm cell's local wind, and that is physics rather than convenience: a
	/// fully developed sea is the accumulated work of hours of wind over kilometres of fetch, and
	/// it does not answer to a gust that lasts ten seconds. The gusts are already in the picture as
	/// the ripple patches the shader drags across the surface.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[AddComponentMenu("FishMMO/Water/Water Environment")]
	[RequireComponent(typeof(WaterSurface))]
	public sealed class WaterEnvironment : MonoBehaviour
	{
		[Header("Wind")]
		[Tooltip("Take the wind from the world's weather. Off leaves the surface's own settings alone.")]
		public bool DriveWind = true;
		[Tooltip("Scales the wind the weather reports, for a sheltered bay or an exposed cape.")]
		[Range(0f, 2f)] public float Fetch = 1f;
		[Tooltip("How quickly the sea answers a change in the wind. A real sea takes hours.")]
		[Range(0.01f, 2f)] public float Responsiveness = 0.15f;

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

		[Header("Reporting")]
		[Tooltip("Log what the sea is doing once a minute. For tuning a coast.")]
		public bool LogState;

		private WaterSurface surface;
		private WorldSceneSettings settings;
		private float windSpeed;
		private float windHeading;
		private bool primed;
		private double nextLog;

		/// <summary>The tide this frame, in metres above mean sea level.</summary>
		public float Tide { get; private set; }

		/// <summary>The wind this frame, in metres per second.</summary>
		public float Wind => windSpeed;

		private void OnEnable()
		{
			surface = GetComponent<WaterSurface>();
			primed = false;
			Resolve();
		}

		private void Resolve()
		{
			if (settings == null)
			{
				WorldSceneSettings.TryGetForScene(gameObject.scene, out settings);
			}
		}

		private void LateUpdate()
		{
			if (surface == null)
			{
				return;
			}
			Resolve();

			double hours = WorldTime.UnanchoredHours();
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = settings != null ? settings.Body : null;
			float latitude = settings != null ? settings.Latitude : 0f;
			float longitude = settings != null ? settings.Longitude : 0f;

			if (DriveWind)
			{
				ApplyWind(latitude, hours);
			}
			if (DriveTide)
			{
				ApplyTide(system, body, hours, latitude, longitude);
			}
			else
			{
				surface.TideMetres = 0f;
			}

			if (LogState && hours * 3600.0 >= nextLog)
			{
				nextLog = hours * 3600.0 + 60.0;
				Debug.Log($"[Water] {gameObject.scene.name}: wind {windSpeed:0.0} m/s from {windHeading:0}°, " +
					$"tide {Tide:+0.00;-0.00} m, sea at {surface.SeaLevel:0.00} m.", this);
			}
		}

		private void ApplyWind(float latitude, double hours)
		{
			/* The prevailing field: banded by latitude, drifting with the world clock, the same
			 * numbers the clouds and the weather director run on. Nothing here is random, so two
			 * clients looking at the same coast at the same moment see the same sea. */
			Vector2 wind = WeatherDriver.PrevailingWind(latitude)
				* WeatherDriver.PrevailingSpeed(WeatherDriver.WorldSeed, latitude, hours * 3600.0);
			float speed = wind.magnitude * Mathf.Max(0f, Fetch);
			// Heading the wind blows TOWARD, clockwise from north, which is how everything else in
			// this project quotes a heading.
			float heading = Mathf.Repeat(Mathf.Atan2(wind.x, wind.y) * Mathf.Rad2Deg, 360f);

			if (!primed)
			{
				windSpeed = speed;
				windHeading = heading;
				primed = true;
			}
			else
			{
				/* Eased, because a sea has memory. The wind can back forty degrees in a minute and
				 * the swell will still be running the old way for hours — snapping the wave
				 * directions to the current wind makes the whole surface pivot at once, which
				 * nothing in nature does. */
				float step = Mathf.Clamp01(Time.deltaTime * Responsiveness);
				windSpeed = Mathf.Lerp(windSpeed, speed, step);
				windHeading = Mathf.MoveTowardsAngle(windHeading, heading, 360f * step);
			}

			if (!Mathf.Approximately(surface.WindSpeed, windSpeed)
				|| !Mathf.Approximately(surface.WindDirectionDegrees, windHeading))
			{
				surface.WindSpeed = windSpeed;
				surface.WindDirectionDegrees = windHeading;
				surface.Rebuild();
			}
		}

		private void ApplyTide(SolarSystemProfile system, WorldBody body, double hours, float latitude, float longitude)
		{
			if (system == null || body == null)
			{
				Tide = 0f;
				surface.TideMetres = 0f;
				return;
			}
			double equilibrium = PlanetTides.HeightMetres(system, body, hours, latitude, longitude);
			float tide = (float)equilibrium * Mathf.Max(0f, CoastalAmplification);
			/* Clamped, and the clamp is a level-design tool rather than a safety rail. The terrain
			 * is fixed and the waterline is not: a tide of two metres on a gentle beach moves the
			 * shore tens of metres, and everything that was placed on dry sand is then in the sea.
			 * A scene decides how much of that it can take. */
			Tide = Mathf.Clamp(tide, -MaximumTideMetres, MaximumTideMetres);
			surface.TideMetres = Tide;
		}
	}
}
