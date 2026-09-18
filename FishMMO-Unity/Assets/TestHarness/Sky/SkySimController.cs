using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.TestHarness.Sky
{
	/// <summary>
	/// The sky test bed: stand on any planet or moon in the solar system, at any latitude, date and
	/// time, and watch the sky the game would draw there.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything the sky needs comes from <see cref="WorldDayNightCycle"/>, which reads the scene
	/// it is in. This controller overrides that with the cycle's preview fields, so no atlas entry
	/// and no server are needed: the body, place, date and time all come from the panel. It runs
	/// the clock itself, so <see cref="WorldDayNightCycle.PreviewClockAdvances"/> is off.
	/// </para>
	/// <para>
	/// The weather sliders feed <see cref="WeatherClient"/> directly, the same call the real client
	/// makes once a frame, so clouds, aurora and lightning appear without a timeline or a server.
	/// </para>
	/// </remarks>
	[DefaultExecutionOrder(-50)]
	public sealed class SkySimController : MonoBehaviour
	{
		[Header("Scene")]
		public Camera Camera;
		public WorldDayNightCycle DayNight;
		public WorldSceneSettings Settings;
		public Transform Ground;

		[Header("Assets")]
		public WeatherRenderProfile Profile;
		[Tooltip("Cached here, because the test bed loads no addressables.")]
		public SolarSystemProfile SolarSystem;
		[Tooltip("Sky profiles that can be forced on any body, on top of the body's own.")]
		public List<SkyProfile> SkyProfiles = new List<SkyProfile>();

		[Header("Start")]
		[Tooltip("Hours per real second while the clock runs.")]
		public float TimeScale = 0.05f;

		private readonly List<ICachedObject> registered = new List<ICachedObject>();
		private WeatherPresentation presentation;
		private double hours;
		private double timeOfDay = 0.3;
		private float dayOfYear = 172f;
		private float latitude = 25f;
		private float longitude;
		private float heading;
		private WorldBody body;
		private SkyProfile skyOverride;
		private bool paused = true;
		private WeatherFrame weather = WeatherFrame.Clear;
		private float temperature = 0.2f;

		/// <summary>Every planet and moon in the system, in the order the system lists them.</summary>
		public readonly List<WorldBody> Bodies = new List<WorldBody>();

		/// <summary>Raised after every frame is presented, for the panel's readout.</summary>
		public event Action<SkySimController> Presented;

		public WorldBody Body
		{
			get => body;
			set
			{
				body = value;
				WorldDayNightCycle.PreviewBody = value;
			}
		}

		/// <summary>Local time of day, 0.5 noon. Held while the clock is paused.</summary>
		public double TimeOfDay
		{
			get => timeOfDay;
			set
			{
				timeOfDay = Mathf.Repeat((float)value, 1f);
				ApplyClock();
			}
		}

		/// <summary>The home calendar day: the season, and where the moons are.</summary>
		public float DayOfYear
		{
			get => dayOfYear;
			set
			{
				dayOfYear = value;
				hours = value * DayHours;
				ApplyClock();
			}
		}

		public float Latitude
		{
			get => latitude;
			set
			{
				latitude = Mathf.Clamp(value, -90f, 90f);
				WorldDayNightCycle.PreviewLatitude = latitude;
			}
		}

		public float Longitude
		{
			get => longitude;
			set
			{
				longitude = Mathf.Repeat(value + 180f, 360f) - 180f;
				WorldDayNightCycle.PreviewLongitude = longitude;
			}
		}

		/// <summary>Which way the scene's +Z faces, clockwise from north.</summary>
		public float Heading
		{
			get => heading;
			set
			{
				heading = Mathf.Repeat(value, 360f);
				WorldDayNightCycle.PreviewHeading = heading;
			}
		}

		/// <summary>A sky profile forced on whatever body is selected, or null for the body's own.</summary>
		public SkyProfile SkyOverride
		{
			get => skyOverride;
			set
			{
				skyOverride = value;
				// The same event a region's ECA action raises on the owning client.
				new ChangeSkyProfileAction { Profile = value, BlendSeconds = 0.5f }.Execute(null, null);
			}
		}

		public bool Paused
		{
			get => paused;
			set
			{
				paused = value;
				ApplyClock();
			}
		}

		/// <summary>The weather the sky is drawn under: clouds, aurora, lightning and the rest.</summary>
		public WeatherFrame Weather
		{
			get => weather;
			set => weather = value;
		}

		/// <summary>
		/// Draws every disc bigger than life, the way most games flatter the sky. Off is what this
		/// world ships: a moon is as big as its radius and distance make it. Only for this session;
		/// the sky profile assets are not touched.
		/// </summary>
		public bool LargerThanLife
		{
			get => SkyProfile.LargerThanLifeOverride ?? false;
			set => SkyProfile.LargerThanLifeOverride = value;
		}

		/// <summary>The temperature reported to the presenters, which gates the aurora.</summary>
		public float Temperature
		{
			get => temperature;
			set => temperature = value;
		}

		/// <summary>World hours since the epoch, as shown.</summary>
		public double Hours => hours;

		public WeatherPresentation Presentation => presentation;

		public SkySystem Sky => SkySystem.Instance;

		public CelestialState State => DayNight != null ? DayNight.State : null;

		private double DayHours => SolarSystemProfile.Active != null ? CelestialMath.HomeSolarDayHours(SolarSystemProfile.Active) : 6.0;

		/// <summary>The solar day of the body being stood on, in real hours.</summary>
		public double BodyDayHours
		{
			get
			{
				SolarSystemProfile system = SolarSystemProfile.Active;
				return system != null && body != null ? CelestialMath.SolarDayHours(system, body) : DayHours;
			}
		}

		private void Awake()
		{
			Cache();
			hours = dayOfYear * DayHours;
			Body = body != null ? body : SolarSystemProfile.Active != null ? SolarSystemProfile.Active.HomeWorld : null;
			Latitude = latitude;
			Longitude = longitude;
			Heading = heading;
			WorldDayNightCycle.PreviewClockAdvances = false;
			// The beds start life-size, whatever the profiles say, so what you see is what ships.
			SkyProfile.LargerThanLifeOverride = false;
			ApplyClock();

			presentation = WeatherPresentation.Ensure();
			presentation.Profile = Profile;
			presentation.TargetCamera = Camera;
		}

		private void OnDestroy()
		{
			WorldDayNightCycle.PreviewHours = null;
			WorldDayNightCycle.PreviewLocalTime01 = null;
			WorldDayNightCycle.PreviewLatitude = null;
			WorldDayNightCycle.PreviewLongitude = null;
			WorldDayNightCycle.PreviewHeading = null;
			WorldDayNightCycle.PreviewBody = null;
			WorldDayNightCycle.PreviewClockAdvances = true;
			SkyProfile.LargerThanLifeOverride = null;
			WeatherClient.ResetPresenters();
			if (presentation != null)
			{
				Destroy(presentation.gameObject);
			}
			foreach (ICachedObject cached in registered)
			{
				cached.RemoveFromCache();
			}
			registered.Clear();
		}

		/// <summary>
		/// Puts the solar system, its bodies, the calendar and the sky profiles in the cache the
		/// game would have loaded them into. Anything already cached (a running client) is left be.
		/// </summary>
		private void Cache()
		{
			if (SolarSystem != null && SolarSystemProfile.GetFirst<SolarSystemProfile>() == null)
			{
				SolarSystem.AddToCache(SolarSystem.name);
				registered.Add(SolarSystem);
				foreach (CelestialBody celestial in SolarSystem.Bodies)
				{
					if (celestial != null && CelestialBody.Get<CelestialBody>(celestial.ID) != celestial)
					{
						celestial.AddToCache(celestial.name);
						registered.Add(celestial);
					}
				}
				if (SolarSystem.Calendar != null && CalendarProfile.Get<CalendarProfile>(SolarSystem.Calendar.ID) != SolarSystem.Calendar)
				{
					SolarSystem.Calendar.AddToCache(SolarSystem.Calendar.name);
					registered.Add(SolarSystem.Calendar);
				}
			}
			foreach (SkyProfile profile in SkyProfiles)
			{
				if (profile != null && SkyProfile.Get<SkyProfile>(profile.ID) != profile)
				{
					profile.AddToCache(profile.name);
					registered.Add(profile);
				}
			}
			Bodies.Clear();
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (system != null)
			{
				foreach (CelestialBody celestial in system.Bodies)
				{
					if (celestial is WorldBody world)
					{
						Bodies.Add(world);
					}
				}
			}
		}

		/// <summary>Pins the time of day while paused; lets the clock run free otherwise.</summary>
		private void ApplyClock()
		{
			WorldDayNightCycle.PreviewHours = hours;
			WorldDayNightCycle.PreviewLocalTime01 = paused ? timeOfDay : (double?)null;
		}

		/// <summary>Moves the clock to a time of day (0.5 noon) on the current date.</summary>
		public void JumpTo(double localTime01)
		{
			TimeOfDay = localTime01;
			if (!paused)
			{
				// Running: shift the clock itself, since nothing is pinned.
				SolarSystemProfile system = SolarSystemProfile.Active;
				if (system != null && body != null)
				{
					double local = CelestialMath.LocalTime01(system, body, hours, longitude);
					double shift = localTime01 - local;
					shift -= Math.Round(shift);
					double day = CelestialMath.SolarDayHours(system, body);
					if (!double.IsInfinity(day))
					{
						hours += shift * day;
					}
				}
				ApplyClock();
			}
		}

		/// <summary>Points the camera at a direction in the scene, keeping it level.</summary>
		public void LookAt(Vector3 direction)
		{
			if (Camera == null || direction.sqrMagnitude < 1e-6f)
			{
				return;
			}
			Camera.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
		}

		private void Update()
		{
			if (!paused)
			{
				hours += Time.deltaTime * TimeScale;
				dayOfYear = (float)(hours / Math.Max(1e-6, DayHours));
				ApplyClock();
				CelestialState state = State;
				if (state != null)
				{
					timeOfDay = state.LocalTime01;
				}
			}

			if (Camera == null)
			{
				return;
			}
			CelestialState now = State;
			var context = new WeatherContext
			{
				Scene = gameObject.scene,
				Settings = Settings,
				ViewerPosition = Camera.transform.position,
				WorldHours = hours,
				LocalTime01 = now != null ? now.LocalTime01 : timeOfDay,
				IsDaylight = DayNight == null || DayNight.DaylightNow,
				Temperature = temperature,
				// The sky bed has no storm cells: what the sliders say is the background.
				Background = weather,
			};
			WeatherClient.Present(weather, context);
			Presented?.Invoke(this);
		}
	}
}
