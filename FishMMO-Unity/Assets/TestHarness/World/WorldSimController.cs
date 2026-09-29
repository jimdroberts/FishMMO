using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

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
		[Tooltip("Hours per real second while the clock runs.")]
		public float TimeScale = 0.05f;
		[Tooltip("The most the ground's clock may run ahead of real time. The sky's clock defaults to a hundred and eighty times real time, and a ground kept to that is wet and dry again inside a second — true to the clock and useless to look at. Capped, a road takes half a minute or so to dry while the day races by overhead.")]
		[Range(1f, 200f)] public float GroundTimeScale = 12f;
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

		private double hours;
		private double timeOfDay = 0.35;
		private float dayOfYear = 172f;
		private float latitude = 25f;
		private float longitude;
		private float heading;
		private WorldBody body;
		private SkyProfile skyOverride;
		private bool paused = true;
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

		/// <summary>The home calendar day within its year: the season.</summary>
		public float DayOfYear
		{
			get => dayOfYear;
			set
			{
				dayOfYear = Mathf.Clamp(value, 0f, DaysPerYear - 0.0001f);
				ComposeHours();
				ApplyClock();
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
				ApplyClock();
			}
		}

		private int year;

		private static int DaysPerYear => SolarSystemProfile.Active != null ? SolarSystemProfile.Active.DaysPerYear : 365;

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
			hours = Math.Max(0.0, worldHours);
			ReadCalendar();
			ApplyClock();
			CelestialState state = State;
			if (state != null)
			{
				timeOfDay = state.LocalTime01;
			}
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
			double limit = hours + CelestialMath.YearHours(system) * 4.0;
			bool wasIn = true;
			for (double at = hours; at < limit; at += step)
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
			hours = ((double)year * DaysPerYear + dayOfYear) * DayHours;
		}

		/// <summary>The clock into the date, after anything that moved the clock itself.</summary>
		private void ReadCalendar()
		{
			double days = hours / Math.Max(1e-6, DayHours);
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

		public bool Paused
		{
			get => paused;
			set
			{
				bool started = paused && !value;
				paused = value;
				ApplyClock();
				if (started)
				{
					BeginRunTrace();
				}
			}
		}

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
				.Append(F(timeOfDay)).Append(',').Append(F(state != null ? state.LocalTime01 : -1)).Append(',').Append(F(hours)).Append(',')
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
			if (runTraceElapsed >= RunTraceSeconds || paused)
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

		/// <summary>World hours since the epoch, as shown.</summary>
		public double Hours => hours;

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
			timeOfDay = Mathf.Repeat((float)localTime01, 1f);
			dayOfYear = Mathf.Floor(dayOfYear) + (float)timeOfDay;
			ComposeHours();
			ApplyClock();
		}

		/// <summary>Moves the clock to a time of day (0.5 noon) on the current date.</summary>
		/// <remarks>
		/// Always moves the world's clock, paused or not. Paused, <see cref="TimeOfDay"/> alone only
		/// pins where the sky draws its sun; the weather is sampled on the world's clock, which then
		/// stayed at the day's start — on Arthis one in the morning — so a probe's "noon" had a
		/// night's weather under a noon sun: a humid night's fog lit by the midday sun.
		/// </remarks>
		public void JumpTo(double localTime01)
		{
			TimeOfDay = localTime01;
			{
				SolarSystemProfile system = SolarSystemProfile.Active;
				if (system != null && body != null)
				{
					double local = CelestialMath.LocalTime01(system, body, hours, longitude);
					double shift = localTime01 - local;
					shift -= Math.Round(shift);
					double day = CelestialMath.SolarDayHours(system, body);
					if (!double.IsInfinity(day))
					{
						hours = Math.Max(0.0, hours + shift * day);
						ReadCalendar();
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

		/// <summary>Pins the time of day while paused; lets the clock run free otherwise.</summary>
		private void ApplyClock()
		{
			WorldDayNightCycle.PreviewHours = hours;
			WorldDayNightCycle.PreviewLocalTime01 = paused ? timeOfDay : (double?)null;
		}

		// ── The weather ───────────────────────────────────────────────

		public WeatherTimeline Timeline => timeline;
		public WeatherSample LastSample => lastSample;
		public WeatherPresentation Presentation => presentation;
		public uint Tick => (uint)(Time.timeSinceLevelLoad * TickRate);

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
			uint now = Tick;
			timeline.Air = new AirOffsetEntry
			{
				From = timeline.Air.At(now),
				To = offsets,
				StartTick = now,
				EndTick = now + timeline.SecondsToTicks(Mathf.Max(0f, seconds)),
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
					CelestialMath.SeasonalClimateOffsets(system, Body, hours, latitude, out bodyTemperature, out bodyHumidity);
				}
				else
				{
					CelestialMath.ClimateOffsets(system, Body, hours, out bodyTemperature, out bodyHumidity, latitude);
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
			if (overhead)
			{
				speed = 0f;
			}
			// Long enough to cross the camera and go, and no longer than the storm would live.
			uint lifetime = timeline.SecondsToTicks(overhead ? Mathf.Max(600f, life) : Mathf.Min(life * 2f, (distance * 2f) / Mathf.Max(0.5f, speed) + 30f));
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
				MotionTick = now,
				BirthTick = now,
				MatureTick = now + timeline.SecondsToTicks(overhead ? 0.1f : 10f),
				DecayTick = now + lifetime - timeline.SecondsToTicks(10f),
				DeathTick = now + lifetime,
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
		/// Runs the ground's cover forward, everywhere the cover map reaches. Snow takes a quarter
		/// of an hour of weather to lie; this is how a designer sees the end of that without
		/// waiting for it, and how the probe checks that cover really follows the storm.
		/// </summary>
		public void AdvanceCover(float seconds)
		{
			WeatherCoverMap map = Presentation != null ? Presentation.CoverMap : null;
			if (map == null)
			{
				return;
			}
			ForcePresent();
			map.Advance(timeline, Settings, Tick, lastSample.Temperature, seconds);
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
			ComposeHours();
			Body = body != null ? body : SolarSystemProfile.Active != null ? SolarSystemProfile.Active.HomeWorld : null;
			Latitude = latitude;
			Longitude = longitude;
			Heading = heading;
			WorldDayNightCycle.PreviewClockAdvances = false;
			// The bed starts life-size, whatever the profiles say, so what you see is what ships.
			SkyProfile.LargerThanLifeOverride = false;
			ApplyClock();

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
		/// Puts the solar system, its bodies, the calendar, the sky profiles and the weather
		/// substances in the cache the game would have loaded them into. Anything already cached (a
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
			if (!paused)
			{
				hours += Time.deltaTime * TimeScale;
				// The day used to be the clock over the day's length and nothing more, so it ran on
				// past the end of the year — day 365, 366 — off the end of its own slider.
				ReadCalendar();
				ApplyClock();
				CelestialState state = State;
				if (state != null)
				{
					timeOfDay = state.LocalTime01;
				}
			}

			/* One clock for everything. The sky follows the hours above; the sea, the surf and what
			 * is falling have real-time clocks of their own and followed nothing, so a stopped clock
			 * went on surfing under a frozen sky. They stop with it and slow with it, but never run
			 * faster than real time: waves sped up never look like waves (WorldMotion). The sea's
			 * weather keeps the sky's clock all the same (WorldDayNightCycle.SkyClockHours). */
			WorldMotion.FollowClock(paused ? 0.0 : TimeScale * 3600.0);

			uint tick = Tick;
			float dt = Time.deltaTime;
			WriteRunTrace(dt);
			coverTimer += dt;
			if (coverTimer >= 1f)
			{
				timeline.Prune(tick);
				// The ground keeps the bed's clock, not the wall's: at the default rate the sky runs
				// a hundred and eighty times real time, and a road that dried at the speed of the
				// wall clock never seemed to dry at all.
				WeatherCover.TimeScale = paused ? 1f : Mathf.Clamp(TimeScale * 3600f, 1f, Mathf.Max(1f, GroundTimeScale));
				// The bolts are scheduled in world time, so the bed's fast clock would fire a
				// storm's whole night of lightning in a few seconds. Scaled back to about what it
				// would look like at the world's own pace.
				SkySchedule.RateScale = paused ? 1f : 1f / Mathf.Max(1f, TimeScale * 3600f / Mathf.Max(1f, GroundTimeScale));
				timeline.Cover.Integrate(lastSample.Frame, lastSample.Temperature, coverTimer, DayNight == null || DayNight.DaylightNow ? 1f : 0f);
				timeline.CoverTick = tick;
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
			double localTime = now != null ? now.LocalTime01 : timeOfDay;

			// Where and when the bed is, for the weather driver. In the game these come from the
			// world clock and the scene's place on the atlas; here they come from the panel, so
			// dragging the clock really does drive the weather forward.
			timeline.WorldSecondsAtTick = hours * 3600.0;
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
				Tick = tick,
				WorldHours = hours,
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
