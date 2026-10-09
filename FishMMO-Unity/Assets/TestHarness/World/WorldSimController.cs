using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// The world test bed: one scene that stands anywhere in the solar system, at any date and time,
	/// under any weather, and draws exactly what the game would draw there.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This was two beds. They were separate when the sky was one thing and the weather another, and
	/// they stopped being separate some time ago: cloud cover comes from the weather, cloud shadows
	/// fall on weather-lit ground, the height a cloud's base sits at is read from a weather channel,
	/// and the aurora is gated on temperature. Keeping two scenes meant every change to any of that
	/// had to be judged twice, in two places that had drifted apart — and the parts that only one bed
	/// had (a terrain to lie snow on; a solar system to stand on another moon of) were exactly the
	/// parts the other one needed to show its own work honestly.
	/// </para>
	/// <para>
	/// There is one camera, one clock, one set of scene settings and one presentation here, which is
	/// the whole point: two of any of them was the reason they could not simply be put in the same
	/// scene. The weather runs on the real timeline through the real model, the way the server would
	/// drive it, and the sky's own sliders can take that over directly when what is wanted is a
	/// particular sky rather than a particular forecast.
	/// </para>
	/// </remarks>
	[DefaultExecutionOrder(-50)]
	public sealed class WorldSimController : MonoBehaviour
	{
		[Header("Scene")]
		public Camera Camera;
		public WorldDayNightCycle DayNight;
		public WorldSceneSettings Settings;

		[Header("Assets")]
		public WeatherRenderProfile Profile;
		[Tooltip("Cached here, because the test bed loads no addressables.")]
		public SolarSystemProfile SolarSystem;
		[Tooltip("Sky profiles that can be forced on any body, on top of the body's own.")]
		public List<SkyProfile> SkyProfiles = new List<SkyProfile>();
		[Tooltip("The weather substances, cached because the bed loads no addressables: what the physics picks from for what falls and what the ground lets loose.")]
		public List<WeatherSubstance> Substances = new List<WeatherSubstance>();

		[Header("Start")]
		[Tooltip("World hours per real second while the clock runs (0.05 is 180 times real time).")]
		[UnityEngine.Serialization.FormerlySerializedAs("TimeScale")]
		[SerializeField] private float timeScale = 0.05f;
		/// <summary>
		/// The pace past which lightning is thinned (SkySchedule.RateScale): bolts are scheduled in world time, so at a
		/// hundred and eighty times real time a storm's whole night would fire in a few seconds.
		/// </summary>
		private const float LightningPace = 12f;
		[Min(1f)] public float TickRate = 30f;
		[Tooltip("What is added to the air when the scene starts. Zero starts with the air as it is.")]
		public AirOffsets StartAir;

		private readonly WeatherTimeline timeline = new WeatherTimeline();
		private readonly List<ICachedObject> registered = new List<ICachedObject>();
		private WeatherPresentation presentation;
		private ushort nextCell = 1;
		private float presentTimer;
		private float coverTimer;
		private bool forcing;
		private WeatherSample lastSample;

		/// <summary>
		/// The bed's tick: FishNet's stand-in offline, counting at <see cref="TickRate"/> a real second
		/// whatever the world's pace, and driving the shared world clock as a scene server does.
		/// </summary>
		private LocalWorldClock clock;
		private const double StartTimeOfDay = 0.35;
		private float dayOfYear = 172f;
		private float latitude = 25f;
		private float longitude;
		private float heading;
		private WorldBody body;
		private SkyProfile skyOverride;
		private WeatherCover? coverOverride;

		/// <summary>Every planet and moon in the system, in the order the system lists them.</summary>
		public readonly List<WorldBody> Bodies = new List<WorldBody>();

		/// <summary>Raised after every frame is presented, for the panel's readout.</summary>
		public event Action<WorldSimController> Presented;

		// ── The sky ───────────────────────────────────────────────────

		public WorldBody Body
		{
			get => body;
			set
			{
				body = value;
				WorldDayNightCycle.PreviewBody = value;
			}
		}

		/// <summary>
		/// Local time of day where the bed stands, 0.5 noon. Setting it moves the world clock to the
		/// nearest moment that has it (<see cref="JumpTo"/>): the sun, the weather and everything else
		/// move together, as they do in the game. (It used to pin only where the sun was drawn while
		/// paused, a second clock the weather never saw.)
		/// </summary>
		public double TimeOfDay
		{
			get
			{
				CelestialState state = State;
				if (state != null)
				{
					return state.LocalTime01;
				}
				SolarSystemProfile system = SolarSystemProfile.Active;
				return system != null && body != null ? CelestialMath.LocalTime01(system, body, Hours, longitude) : StartTimeOfDay;
			}
			set => JumpTo(value);
		}

		/// <summary>The home calendar day within its year: the season.</summary>
		public float DayOfYear
		{
			get => dayOfYear;
			set
			{
				dayOfYear = Mathf.Clamp(value, 0f, DaysPerYear - 0.0001f);
				ComposeHours();
			}
		}

		/// <summary>
		/// Which year of the world clock, counted from its start. With the day, this is the whole
		/// date, and the whole date is what it takes to have a moment back.
		/// </summary>
		/// <remarks>
		/// The day alone never was. The weather is a function of the world's seconds, so the same day
		/// of another year is another sky entirely; and no moon, planet or comet goes round in a whole
		/// fraction of the year, so day 172 finds them somewhere different every time — an eclipse
		/// that falls on it in year three does not in year four. The bed only ever had year nought,
		/// and a sky reported from a server that had been up longer than that could not be set up here.
		/// </remarks>
		public int Year
		{
			get => year;
			set
			{
				year = Mathf.Max(0, value);
				ComposeHours();
			}
		}

		private int year;

		private static int DaysPerYear => SolarSystemProfile.Active != null ? SolarSystemProfile.Active.DaysPerYear : 365;

		/// <summary>Moves the clock to a day of this year, keeping the hour of the day it is at.</summary>
		public void SetDay(float day)
		{
			float hourOfDay = dayOfYear - Mathf.Floor(dayOfYear);
			DayOfYear = Mathf.Floor(Mathf.Clamp(day, 0f, DaysPerYear - 1f)) + hourOfDay;
		}

		/// <summary>The world hours at a day of the year the bed is in.</summary>
		public double HoursOfDay(float day) => ((double)year * DaysPerYear + day) * DayHours;

		/// <summary>
		/// The day of this year on which the body stood on comes nearest a season, where the camera
		/// is: 0 midwinter, 0.25 spring, 0.5 midsummer, 0.75 autumn.
		/// </summary>
		/// <remarks>
		/// Searched for, because it cannot be known: when a body's midsummer falls follows which way
		/// its axis leans and how long its year is, and south of the equator it is the other half of
		/// the year. The season buttons used to jump to fixed fractions of the home calendar, which
		/// was the right day for no body at all — the home world's own midsummer fell a quarter of a
		/// year after the one the button chose.
		/// </remarks>
		public float DayOfSeason(float season01)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (system == null || Body == null)
			{
				return Mathf.Repeat(season01, 1f) * DaysPerYear;
			}
			// The figure is the northern hemisphere's; the southern has the opposite season.
			float wanted = Mathf.Repeat(season01 + (latitude < 0f ? 0.5f : 0f), 1f);
			float best = 0f, bestDistance = float.MaxValue;
			for (int day = 0; day < DaysPerYear; day++)
			{
				float season = CelestialMath.Season01(system, Body, HoursOfDay(day + 0.5f));
				float distance = Mathf.Abs(Mathf.DeltaAngle(season * 360f, wanted * 360f));
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = day;
				}
			}
			return best;
		}

		/// <summary>Sets the clock to a world hour outright, and the date to match.</summary>
		public void JumpToHours(double worldHours)
		{
			SetHours(worldHours);
		}

		/// <summary>Sets the world clock outright, to the millisecond, keeping its pace.</summary>
		public void SetWorldSeconds(double worldSeconds)
		{
			SetHours(worldSeconds / 3600.0);
		}

		/// <summary>Moves the world clock on (or back) by world seconds, held or running.</summary>
		public void StepWorld(double worldSeconds)
		{
			SetHours(Hours + worldSeconds / 3600.0);
		}

		/// <summary>Sets the clock, and reads the calendar back from it.</summary>
		private void SetHours(double worldHours)
		{
			clock?.SetWorldSeconds(Math.Max(0.0, worldHours) * 3600.0);
			ReadCalendar();
		}

		/// <summary>
		/// The next eclipse from now, as seen from where the camera stands: the world hour of first
		/// contact. Searched, a few minutes at a time, over the next few years.
		/// </summary>
		/// <remarks>
		/// With the discs as they are drawn, so what is found is what will be seen; and a solar
		/// eclipse only counts with the sun up, since one below the horizon is not an eclipse to
		/// anybody here. A lunar one counts with the moon up.
		/// </remarks>
		public bool FindNextEclipse(bool solar, out double hoursAtFirstContact)
		{
			hoursAtFirstContact = 0.0;
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (system == null || Body == null)
			{
				return false;
			}
			var probe = new CelestialState();
			float sunScale = Sky != null && Sky.ActiveSky != null ? Sky.ActiveSky.SunScale : 1f;
			float bodyScale = Sky != null && Sky.ActiveSky != null ? Sky.ActiveSky.BodyScale : 1f;
			double step = 2.0 / 60.0;
			double now = Hours;
			double limit = now + CelestialMath.YearHours(system) * 4.0;
			bool wasIn = true;
			for (double at = now; at < limit; at += step)
			{
				probe.Compute(system, Body, at, latitude, longitude, heading);
				bool isIn = solar
					? probe.Sun >= 0 && probe.SunAltitude > -0.5f && probe.SolarEclipseAsDrawn(sunScale, bodyScale).Obscuration > 0f
					: probe.Moon >= 0 && probe.Bodies[probe.Moon].AltitudeDegrees > -0.5f && probe.LunarPhase != LunarEclipsePhase.None;
				if (isIn && !wasIn)
				{
					hoursAtFirstContact = at;
					return true;
				}
				wasIn = isIn;
			}
			return false;
		}

		/// <summary>The date into the clock. In double: a float day stops holding the hour after a few years.</summary>
		private void ComposeHours()
		{
			clock?.SetWorldSeconds(((double)year * DaysPerYear + dayOfYear) * DayHours * 3600.0);
		}

		/// <summary>The clock into the date, after anything that moved the clock itself.</summary>
		private void ReadCalendar()
		{
			double days = Hours / Math.Max(1e-6, DayHours);
			year = Math.Max(0, (int)Math.Floor(days / DaysPerYear));
			dayOfYear = (float)(days - (double)year * DaysPerYear);
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

		/// <summary>
		/// Whether the world is held. Held, the world clock stands still — sun, sky, weather, the sea and
		/// everything on the world's motion — while the tick goes on counting, exactly as an admin's
		/// <c>/admin time hold</c> does on a live server.
		/// </summary>
		public bool Paused
		{
			get => clock == null || clock.Held;
			set
			{
				if (clock == null)
				{
					return;
				}
				bool started = clock.Held && !value;
				if (value)
				{
					clock.Hold();
				}
				else
				{
					clock.SetRate(RunRate);
				}
				if (started)
				{
					BeginRunTrace();
				}
			}
		}

		/// <summary>World hours per real second while the clock runs (held or not, what Run runs at).</summary>
		public float TimeScale
		{
			get => timeScale;
			set
			{
				timeScale = Mathf.Max(0f, value);
				if (clock != null && !clock.Held)
				{
					clock.SetRate(RunRate);
				}
			}
		}

		/// <summary>World seconds per real second while running: <see cref="TimeScale"/> in the world clock's own terms.</summary>
		public double RunRate
		{
			get => Math.Max(1e-6, timeScale * 3600.0);
			set => TimeScale = (float)(Math.Max(0.0, value) / 3600.0);
		}

		/// <summary>The world clock's pace now: 0 held, else <see cref="RunRate"/>.</summary>
		public double Rate => clock != null ? clock.Rate : 0.0;

		/// <summary>World seconds since the epoch now.</summary>
		public double WorldSeconds => clock != null ? clock.WorldSeconds : 0.0;

		// ── The run trace ─────────────────────────────────────────────
		// What the sky was doing, frame by frame, for the first seconds after Run is pressed. A
		// cloud that swells and shrinks in that second was explained twice by reading the code and
		// both explanations were wrong; this writes down every number that decides how much cloud
		// is drawn and how tall, so the next explanation is read off a file instead.

		/// <summary>Where the last run's trace was written, under the project's Logs folder.</summary>
		public const string RunTracePath = "Logs/WorldSimRunTrace.csv";
		private const float RunTraceSeconds = 8f;
		private System.Text.StringBuilder runTrace;
		private float runTraceElapsed;

		private void BeginRunTrace()
		{
			runTrace = new System.Text.StringBuilder(64 * 1024);
			runTraceElapsed = 0f;
			runTrace.AppendLine("t,dt,timeOfDay,clockLocal01,hours,driftX,driftZ,"
				+ "fieldCover,fieldFog,fieldRain,shownCover,shownFog,shownRain,instability,humidity,pressure,"
				+ "skyCover,base,tops,towersTo,vigour,towerHere,climb,shellBottom,shellTop,"
				+ "lowCover,lowBase,midCover,highCover,measuredSolid,measuredAny");
		}

		private void WriteRunTrace(float dt)
		{
			if (runTrace == null)
			{
				return;
			}
			runTraceElapsed += dt;
			SkySystem sky = SkySystem.Instance;
			CelestialState state = State;
			WeatherFrame shown = presentation != null ? presentation.Shown : lastSample.Frame;
			var culture = System.Globalization.CultureInfo.InvariantCulture;
			string F(double v) => v.ToString("0.#####", culture);
			float Cover(SkySystem s, int index) => s != null && s.CloudBands != null && index < s.CloudBands.Count ? s.CloudBands[index].Coverage : 0f;
			runTrace.Append(F(runTraceElapsed)).Append(',').Append(F(dt)).Append(',')
				.Append(F(TimeOfDay)).Append(',').Append(F(state != null ? state.LocalTime01 : -1)).Append(',').Append(F(Hours)).Append(',')
				.Append(F(sky != null ? sky.CloudDrift.x : 0)).Append(',').Append(F(sky != null ? sky.CloudDrift.y : 0)).Append(',')
				.Append(F(lastSample.Background[WeatherChannel.CloudCover])).Append(',')
				.Append(F(lastSample.Background[WeatherChannel.FogDensity])).Append(',')
				.Append(F(lastSample.Background[WeatherChannel.Precipitation])).Append(',')
				.Append(F(shown[WeatherChannel.CloudCover])).Append(',').Append(F(shown[WeatherChannel.FogDensity])).Append(',')
				.Append(F(shown[WeatherChannel.Precipitation])).Append(',')
				.Append(F(lastSample.Air.Instability)).Append(',').Append(F(lastSample.Air.Humidity)).Append(',').Append(F(lastSample.Air.Pressure)).Append(',');
			if (sky != null)
			{
				AirColumn column = sky.CloudViewerColumn;
				runTrace.Append(F(sky.CloudCover)).Append(',').Append(F(column.Base)).Append(',').Append(F(column.Top)).Append(',')
					.Append(F(column.TowerCeiling)).Append(',').Append(F(column.Vigour)).Append(',')
					.Append(F(sky.CloudTowerAtCamera)).Append(',').Append(F(sky.CloudClimbHeight)).Append(',')
					.Append(F(sky.CloudShellBottom)).Append(',').Append(F(sky.CloudShellTop)).Append(',')
					.Append(F(Cover(sky, 0))).Append(',').Append(F(column.Base)).Append(',')
					.Append(F(Cover(sky, 1))).Append(',').Append(F(Cover(sky, 2))).Append(',');
			}
			else
			{
				runTrace.Append("0,0,0,0,0,0,0,0,0,0,0,0,0,");
			}
			runTrace.Append(F(CloudStats.MeasuredCover)).Append(',').Append(F(CloudStats.MeasuredAnyCloud)).AppendLine();
			if (runTraceElapsed >= RunTraceSeconds || Paused)
			{
				try
				{
					System.IO.Directory.CreateDirectory("Logs");
					System.IO.File.WriteAllText(RunTracePath, runTrace.ToString());
					Debug.Log($"[World Sim] Run trace written: {RunTracePath} ({runTraceElapsed:0.0} s).");
				}
				catch (Exception e)
				{
					Debug.LogWarning($"[World Sim] Could not write the run trace: {e.Message}");
				}
				runTrace = null;
			}
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

		/// <summary>World hours since the epoch, as shown: the bed's clock.</summary>
		public double Hours => clock != null ? clock.WorldSeconds / 3600.0 : 0.0;

		public SkySystem Sky => SkySystem.Instance;

		public CelestialState State => DayNight != null ? DayNight.State : null;

		private static double DayHours => SolarSystemProfile.Active != null ? CelestialMath.HomeSolarDayHours(SolarSystemProfile.Active) : 6.0;

		/// <summary>The solar day of the body being stood on, in real hours.</summary>
		public double BodyDayHours
		{
			get
			{
				SolarSystemProfile system = SolarSystemProfile.Active;
				return system != null && body != null ? CelestialMath.SolarDayHours(system, body) : DayHours;
			}
		}

		/// <summary>
		/// Moves the world clock to a time of day within the current day, and the sun with it.
		/// </summary>
		/// <remarks>
		/// Separate from setting <see cref="TimeOfDay"/>, which only says where the sun is drawn. The
		/// weather driver reads the world clock, so the panel's slider has to move the clock or
		/// scrubbing the time slides the sun across a sky whose weather never changes. It cannot be
		/// folded into the property, though: the probe sets a *fractional* day — a precise moment
		/// found by searching for a moonlit night — and then sets the time of day, and a property
		/// that rewrote the day from the time threw that search away and landed on whatever moment
		/// happened to share the hour. Once, that was a total lunar eclipse, and the moon the stage
		/// existed to photograph was not there at all.
		/// </remarks>
		public void ScrubTo(double localTime01)
		{
			dayOfYear = Mathf.Floor(dayOfYear) + Mathf.Repeat((float)localTime01, 1f);
			ComposeHours();
		}

		/// <summary>Moves the clock to a time of day (0.5 noon) on the current date.</summary>
		/// <remarks>
		/// To the nearest moment that has it, held or not: the one world clock moves, and with it the sun,
		/// the weather and everything else. (When <see cref="TimeOfDay"/> only pinned where the sky drew
		/// its sun, the weather stayed at the day's start — on Arthis one in the morning — and a probe's
		/// "noon" had a night's weather under a noon sun.)
		/// </remarks>
		public void JumpTo(double localTime01)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (system == null || body == null)
			{
				ScrubTo(localTime01);
				return;
			}
			double now = Hours;
			double local = CelestialMath.LocalTime01(system, body, now, longitude);
			double shift = Mathf.Repeat((float)localTime01, 1f) - local;
			shift -= Math.Round(shift);
			double day = CelestialMath.SolarDayHours(system, body);
			if (!double.IsInfinity(day))
			{
				SetHours(now + shift * day);
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


		// ── The weather ───────────────────────────────────────────────

		public WeatherTimeline Timeline => timeline;
		public WeatherSample LastSample => lastSample;
		public WeatherPresentation Presentation => presentation;
		/// <summary>The bed's tick, whole (the local clock's, which never changes pace).</summary>
		public uint Tick => clock != null ? (uint)clock.Tick : 0u;

		/// <summary>The bed's tick, fractional: where the storms are drawn this frame.</summary>
		public double PreciseTick => clock != null ? clock.Tick : 0.0;

		/// <summary>
		/// What is added to the scene's air at runtime — the same thing an admin's
		/// <c>/admin weather air</c> sets. Additions, never a replacement: zero is the air as it is.
		/// </summary>
		/// <remarks>
		/// There is no other way to ask this bed for weather. It used to take presets, layers and
		/// channels set straight — "this much cloud, this much rain" — and every one of those could
		/// ask for weather the air could not have made. Now it asks the air to be different, and
		/// everything the sky, the rain and the ground do follows from that air, exactly as in game.
		/// </remarks>
		public AirOffsets Air
		{
			get => timeline.Air.To;
			set => SetAir(value, 0f);
		}

		/// <summary>Moves what is added to the air to new values over a transition.</summary>
		public void SetAir(AirOffsets offsets, float seconds)
		{
			// In world time, as the server's are: a transition holds while the world is held.
			double now = WorldSeconds;
			timeline.Air = new AirOffsetEntry
			{
				From = timeline.Air.AtSeconds(now),
				To = offsets,
				StartSeconds = now,
				EndSeconds = now + Mathf.Max(0f, seconds),
			};
			timeline.Revision++;
			ApplyClimate();
			if (seconds <= 0f && SkySystem.Instance != null)
			{
				// All at once: the sky shows the new air now, not a slice of it at a time.
				SkySystem.Instance.RebuildCloudAir();
			}
		}

		/// <summary>
		/// Whether the drifting weather field runs under the air. On, the air is the world's own
		/// weather — highs, lows, fronts — plus what is added; off, it is a still, ordinary air plus
		/// what is added, which is what a probe measuring one air wants.
		/// </summary>
		public bool FieldDriven
		{
			get => timeline.Driver;
			set => timeline.Driver = value;
		}

		/// <summary>What the body stood on adds to the scene's temperature and humidity, now: see <see cref="ApplyClimate"/>.</summary>
		public float BodyTemperature { get; private set; }
		public float BodyHumidity { get; private set; }

		/// <summary>
		/// The scene's runtime climate: the warmth added to its air — authored and here — plus what
		/// the body stood on gets from its suns. The same sum the server and the client make in the
		/// game.
		/// </summary>
		private void ApplyClimate()
		{
			BodyTemperature = 0f;
			BodyHumidity = 0f;
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (system != null && Body != null)
			{
				// The scene's own climate already carries its body's orbit mean (SampleClimate adds it),
				// so only the season on top of that is added here — the same as the server and client
				// do. A bed scene with no atlas body of its own has no mean in it, and the bed's chosen
				// body then contributes all of it.
				float bodyTemperature, bodyHumidity;
				if (Settings != null && Settings.Body == Body)
				{
					CelestialMath.SeasonalClimateOffsets(system, Body, Hours, latitude, out bodyTemperature, out bodyHumidity);
				}
				else
				{
					CelestialMath.ClimateOffsets(system, Body, Hours, out bodyTemperature, out bodyHumidity, latitude);
				}
				BodyTemperature = bodyTemperature;
				BodyHumidity = bodyHumidity;
			}
			if (Settings != null)
			{
				float added = (Settings.AuthoredAir + timeline.AirAt(Tick)).TemperatureScale;
				Settings.RuntimeTemperatureOffset = Mathf.Clamp(added + BodyTemperature, -2f, 2f);
				Settings.RuntimeHumidityOffset = Mathf.Clamp(BodyHumidity, -2f, 2f);
			}
		}

		/// <summary>
		/// Starts a storm of a kind upwind, drifting over the camera — or, <paramref name="overhead"/>,
		/// standing on it. Its size and life come from the air where it starts unless a radius is given.
		/// </summary>
		public void SpawnCell(StormKind kind, float radius = 0f, float speed = 6f, bool overhead = false)
		{
			if (Camera == null)
			{
				return;
			}
			Vector3 at = Camera.transform.position;
			float angle = (Tick % 360) * Mathf.Deg2Rad;
			var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
			uint now = Tick;
			WeatherSample here = WeatherField.Sample(timeline, Settings, gameObject.scene, at, now);
			// The game's rule (WeatherHost.SpawnCell): no air, no storm of any kind.
			if (!StormPhysics.CanForm(here.Planet))
			{
				return;
			}
			StormPhysics.Dimensions(kind, here.OpenColumn, 0.5f, 0.5f, out float airRadius, out float extent, out float life);
			if (radius > 0f)
			{
				extent *= radius / Mathf.Max(1f, airRadius);
			}
			else
			{
				radius = airRadius;
			}
			float reach = Mathf.Max(radius, extent);
			float distance = overhead ? 0f : reach * 1.5f;
			if (overhead || kind == StormKind.Eruption)
			{
				// Nor does a vent travel: an eruption stays put and its plume is what the wind carries.
				speed = 0f;
			}
			// Long enough to cross the camera and go, and no longer than the storm would live.
			// In world seconds, as the server's storms are: it holds with the world, and races with it.
			double lifetime = overhead ? Mathf.Max(600f, life) : Mathf.Min(life * 2f, (distance * 2f) / Mathf.Max(0.5f, speed) + 30f);
			double born = WorldSeconds;
			timeline.UpsertCell(new StormCell
			{
				ID = nextCell++,
				Kind = kind,
				Seed = (uint)(now * 2654435761u),
				OriginX = at.x - direction.x * distance,
				OriginZ = at.z - direction.y * distance,
				VelocityX = direction.x * speed,
				VelocityZ = direction.y * speed,
				RadiusMeters = radius,
				ExtentMeters = extent,
				Shape = StormPhysics.ShapeOf(kind),
				PeakIntensity = 1f,
				MeanderMeters = 20f,
				MotionSeconds = born,
				BirthSeconds = born,
				MatureSeconds = born + (overhead ? 0.1 : 10.0),
				DecaySeconds = born + lifetime - 10.0,
				DeathSeconds = born + lifetime,
			});
			timeline.Revision++;
		}

		/// <summary>The bed's reset: the air back to the world's own, no storms, bare ground.</summary>
		public void ClearAll(float seconds)
		{
			SetAir(default, seconds);
			timeline.Cells.Clear();
			timeline.Revision++;
			// Clear means clear: the ground goes back to bare as well. Weather stopping does not dry
			// the ground — that takes a quarter of an hour, and the panel can fast-forward it — but
			// this button is the bed's reset, not a forecast.
			ResetCover();
			if (Presentation != null && Presentation.CoverMap != null)
			{
				Presentation.CoverMap.Hold(default);
			}
		}

		public void ResetCover()
		{
			timeline.Cover = default;
			CoverOverride = null;
		}

		/// <summary>
		/// Snow, wet, ash and sand held at a chosen depth instead of accumulating. Cover takes
		/// fifteen minutes of weather to build, which is no way to look at a wet street or a snowed
		/// -in courtyard: set this and the surfaces show that depth at once.
		/// </summary>
		public WeatherCover? CoverOverride
		{
			get => coverOverride;
			set
			{
				coverOverride = value;
				// The ground is drawn from the cover map, not from this figure, so holding a depth
				// has to reach the map or nothing changes on screen.
				if (value.HasValue && Presentation != null && Presentation.CoverMap != null)
				{
					timeline.Cover = value.Value;
					Presentation.CoverMap.Hold(value.Value);
				}
			}
		}

		/// <summary>
		/// Moves the world on by <paramref name="seconds"/> and brings the ground through them at once — the panel's
		/// "+15 min" and the probe's settle. The ground keeps the world's one clock, so fast-forwarding it means
		/// fast-forwarding the world: the storms move on and the sun with them, and the cover is what that weather
		/// left (it was the cover alone, run on under the weather standing still).
		/// </summary>
		public void FastForward(float seconds)
		{
			StepWorld(seconds);
			ForcePresent();
			SceneCoverSampling.Advance(timeline, Settings, gameObject.scene, timeline.WorldSecondsAt(Tick));
			WeatherCoverMap map = Presentation != null ? Presentation.CoverMap : null;
			map?.CatchUp(timeline, Settings, Tick);
		}

		// ── Life ──────────────────────────────────────────────────────

		/// <summary>Leaves the world moving: the rate is global, and the game runs at 1.</summary>
		private void OnDisable()
		{
			WorldMotion.FollowClock(1.0);
		}

		private void Awake()
		{
			Cache();
			/* The bed's own tick, standing in for FishNet's: it drives the shared world clock, so the sky,
			 * the weather, the sea and the storms all read the time down the game's own path. Held at
			 * the start, as the bed always was. */
			clock = new LocalWorldClock(TickRate, ((double)year * DaysPerYear + dayOfYear) * DayHours * 3600.0);
			LocalWorldClock.Activate(clock);
			clock.Hold();
			// No preview pins: the cycle reads the local clock (WorldTime), as it reads the server's in game.
			WorldDayNightCycle.PreviewHours = null;
			WorldDayNightCycle.PreviewLocalTime01 = null;
			/* Stands where the scene stands: on its own body, at its own place on the atlas. It used
			 * to start on the home world at 25° whatever scene it was dressed into, so a generated
			 * scene on an airless moon was simulated under the home world's wet air — rain and snow
			 * on Seli Waste. Only the bed's own scene, which has no atlas entry, starts at the
			 * defaults. */
			WorldAtlasScene placed = Settings != null ? Settings.AtlasEntry : null;
			Body = body != null ? body : ScenePlacementClimate.ResolveBody(SolarSystemProfile.Active, placed);
			if (placed != null && placed.Placed)
			{
				latitude = (float)placed.EffectiveSunLatitude;
				longitude = (float)placed.TimeLongitude;
				heading = placed.HeadingDegrees;
			}
			Latitude = latitude;
			Longitude = longitude;
			Heading = heading;
			// The bed starts life-size, whatever the profiles say, so what you see is what ships.
			SkyProfile.LargerThanLifeOverride = false;
			// At its starting hour of the day, where the bed stands.
			JumpTo(StartTimeOfDay);

			Scene scene = gameObject.scene;
			timeline.SceneName = scene.name;
			timeline.SceneMode = WeatherSceneMode.Own;
			timeline.TickDelta = 1.0 / TickRate;
			timeline.Seed = 238;
			WeatherQuery.TickSource = () => Tick;
			WeatherQuery.Register(scene, timeline);

			presentation = WeatherPresentation.Ensure();
			presentation.Profile = Profile;
			presentation.TargetCamera = Camera;
			// The game's camera renders post-processing, which is where the frame is tonemapped; the
			// bed's is built from code and would otherwise show a frame the game never does.
			SkySystem.EnablePostProcessing(Camera);
		}

		private void Start()
		{
			if (!StartAir.IsZero)
			{
				SetAir(StartAir, 0f);
			}
		}

		private void OnDestroy()
		{
			WorldDayNightCycle.PreviewHours = null;
			WorldDayNightCycle.PreviewLocalTime01 = null;
			if (LocalWorldClock.Active == clock)
			{
				LocalWorldClock.Activate(null);
			}
			// Back to the game's own: the bed scales this to its clock's pace.
			SkySchedule.RateScale = 1f;
			WorldDayNightCycle.PreviewLatitude = null;
			WorldDayNightCycle.PreviewLongitude = null;
			WorldDayNightCycle.PreviewHeading = null;
			WorldDayNightCycle.PreviewBody = null;
			WorldDayNightCycle.PreviewClockAdvances = true;
			SkyProfile.LargerThanLifeOverride = null;
			WeatherQuery.Unregister(gameObject.scene);
			WeatherQuery.TickSource = null;
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
		/// Puts the solar system, its bodies, the calendar, the sky profiles, the weather substances
		/// and the scene's atlas entry in the cache the game would have loaded them into. Anything already cached (a
		/// running client) is left be.
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
			foreach (WeatherSubstance substance in Substances)
			{
				if (substance != null && WeatherSubstance.Get<WeatherSubstance>(substance.ID) != substance)
				{
					substance.AddToCache(substance.name);
					registered.Add(substance);
				}
			}
			/* And the scene's atlas entry, which is where its body, its place, its biome map and its
			 * authored air live. The game finds it among the addressables it loaded; the bed loads
			 * none, so WorldAtlasScene.Find answered null in play, the scene read as unplaced, and its
			 * weather fell back to the home world. */
			WorldAtlasScene entry = null;
#if UNITY_EDITOR
			entry = WorldAtlasScene.EditorLookup.Find(gameObject.scene.name);
#endif
			if (entry != null && WorldAtlasScene.Find(entry.SceneName) == null)
			{
				entry.AddToCache(entry.name);
				registered.Add(entry);
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

		private void Update()
		{
			/* The tick counts on at its own fixed rate, held or not, as FishNet's does; the world's pace
			 * (held, real time, raced) is the clock's anchor, set by the panel. One clock for everything:
			 * the sky, the weather, the storms and the sea all read it, and the world's motion (waves,
			 * wind, what falls) follows its pace — stopped when held, slowed when slowed, but never faster
			 * than real time (WorldMotion). */
			clock?.Advance(Time.unscaledDeltaTime);
			ReadCalendar();

			uint tick = Tick;
			float dt = Time.deltaTime;
			WriteRunTrace(dt);
			coverTimer += dt;
			if (coverTimer >= 1f)
			{
				timeline.Prune(tick);
				// The bolts are scheduled in world time, so the bed's fast clock would fire a
				// storm's whole night of lightning in a few seconds. Scaled back to about what it
				// would look like at the world's own pace.
				double pace = Rate;
				SkySchedule.RateScale = pace <= 0.0 ? 1f : 1f / Mathf.Max(1f, (float)pace / LightningPace);
				// The ground keeps the world's clock, as the server's does (SceneCoverSampling.Advance):
				// held, nothing dries or settles; raced, it races. To watch a road dry, slow the world.
				SceneCoverSampling.Advance(timeline, Settings, gameObject.scene, timeline.WorldSecondsAt(tick));
				coverTimer = 0f;
			}
			presentTimer -= dt;
			if (presentTimer > 0f || Camera == null)
			{
				return;
			}
			presentTimer = 0.1f;
			Present();
		}

		/// <summary>
		/// Samples the weather where the camera stands and hands it to the presenters at once,
		/// instead of on the next tenth of a second. A probe that changes something and renders the
		/// very next frame needs this, or it renders the state before the change.
		/// </summary>
		public void ForcePresent()
		{
			if (Camera == null)
			{
				return;
			}
			forcing = true;
			try
			{
				Present();
			}
			finally
			{
				forcing = false;
			}
		}

		private void Present()
		{
			uint tick = Tick;
			// Held cover wins over what has accumulated, and it is applied here rather than in the
			// loop so that setting it and presenting at once shows it.
			if (CoverOverride.HasValue)
			{
				timeline.Cover = CoverOverride.Value;
			}
			Scene scene = gameObject.scene;
			Vector3 viewer = Camera.transform.position;
			CelestialState now = State;
			double localTime = now != null ? now.LocalTime01 : TimeOfDay;

			// Where and when the bed is, for the weather driver. In the game these come from the
			// world clock and the scene's place on the atlas; here they come from the panel, so
			// dragging the clock really does drive the weather forward.
			// The timeline's own anchor, for anything that reads it alone; the weather reads the world clock
			// itself while one is anchored (WeatherTimeline.WorldSecondsAt), pace and all.
			timeline.WorldSecondsAtTick = WorldClock.Shared.WorldSecondsAt(tick);
			timeline.WorldSecondsTick = tick;
			timeline.LatitudeDegrees = latitude;
			timeline.LongitudeDegrees = longitude;
			// Every time, not only when the slider moves: the body's share changes with the clock — an
			// eccentric orbit swings a world nearer and further, a second sun comes and goes, and the
			// season moves the sun up and down this latitude's sky.
			ApplyClimate();
			// And on which body, so the weather's season and hour are this body's and not the scene's.
			timeline.BodyOverride = Body;

			lastSample = WeatherField.Sample(timeline, Settings, scene, viewer, tick);
			WeatherFrame frame = lastSample.Frame;
			var context = new WeatherContext
			{
				Scene = scene,
				Settings = Settings,
				Timeline = timeline,
				ViewerPosition = viewer,
				Tick = PreciseTick,
				WorldHours = Hours,
				Cover = timeline.Cover,
				Background = lastSample.Background,
				Sample = lastSample,
				Shelter = lastSample.Shelter,
				Temperature = lastSample.Temperature,
				Substance = lastSample.Substance,
				IsDaylight = DayNight == null || DayNight.DaylightNow,
				LocalTime01 = localTime,
			};
			WeatherClient.Present(frame, context);
			if (forcing && presentation != null)
			{
				// Apply only records; this is what writes the globals a render is about to read.
				presentation.Flush();
			}
			Presented?.Invoke(this);
		}
	}
}
