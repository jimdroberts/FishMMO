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
		public Light Sun;
		public WorldDayNightCycle DayNight;
		public WorldSceneSettings Settings;

		[Header("Assets")]
		public WeatherRenderProfile Profile;
		[Tooltip("Cached here, because the test bed loads no addressables.")]
		public SolarSystemProfile SolarSystem;
		[Tooltip("Sky profiles that can be forced on any body, on top of the body's own.")]
		public List<SkyProfile> SkyProfiles = new List<SkyProfile>();
		public List<WeatherPreset> Presets = new List<WeatherPreset>();
		public List<WeatherLayerTemplate> Templates = new List<WeatherLayerTemplate>();

		[Header("Start")]
		[Tooltip("Hours per real second while the clock runs.")]
		public float TimeScale = 0.05f;
		[Tooltip("The most the ground's clock may run ahead of real time. The sky's clock defaults to a hundred and eighty times real time, and a ground kept to that is wet and dry again inside a second — true to the clock and useless to look at. Capped, a road takes half a minute or so to dry while the day races by overhead.")]
		[Range(1f, 200f)] public float GroundTimeScale = 12f;
		[Min(1f)] public float TickRate = 30f;
		[Tooltip("Preset applied when the scene starts. Empty starts clear.")]
		public string StartPreset = "";

		private const ushort ManualHandleBase = 1000;

		private readonly WeatherTimeline timeline = new WeatherTimeline();
		private readonly List<ICachedObject> registered = new List<ICachedObject>();
		private WeatherPresentation presentation;
		private ushort nextHandle = 1;
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
		private WeatherFrame skyWeather = WeatherFrame.Clear;

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
			runTrace.AppendLine("t,dt,timeOfDay,clockLocal01,hours,driftX,driftZ,driverWeight,"
				+ "fieldCover,fieldFog,fieldRain,shownCover,shownFog,shownRain,instability,humidity,pressure,"
				+ "skyCover,columnType,towerGain,towerHere,mesoHere,climb,shellBottom,shellTop,"
				+ "cumulusCover,cumulusBase,altoCover,cirrusCover,measuredSolid,measuredAny");
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
			float Band(IReadOnlyList<float> list, int index) => list != null && index < list.Count ? list[index] : 0f;
			runTrace.Append(F(runTraceElapsed)).Append(',').Append(F(dt)).Append(',')
				.Append(F(timeOfDay)).Append(',').Append(F(state != null ? state.LocalTime01 : -1)).Append(',').Append(F(hours)).Append(',')
				.Append(F(sky != null ? sky.CloudDrift.x : 0)).Append(',').Append(F(sky != null ? sky.CloudDrift.y : 0)).Append(',')
				.Append(F(lastSample.DriverWeight)).Append(',')
				.Append(F(lastSample.Background[WeatherChannel.CloudCover])).Append(',')
				.Append(F(lastSample.Background[WeatherChannel.FogDensity])).Append(',')
				.Append(F(lastSample.Background[WeatherChannel.Precipitation])).Append(',')
				.Append(F(shown[WeatherChannel.CloudCover])).Append(',').Append(F(shown[WeatherChannel.FogDensity])).Append(',')
				.Append(F(shown[WeatherChannel.Precipitation])).Append(',')
				.Append(F(lastSample.Air.Instability)).Append(',').Append(F(lastSample.Air.Humidity)).Append(',').Append(F(lastSample.Air.Pressure)).Append(',');
			if (sky != null)
			{
				runTrace.Append(F(sky.CloudCover)).Append(',').Append(F(sky.CloudColumnType)).Append(',').Append(F(sky.CloudTowerGain)).Append(',')
					.Append(F(sky.CloudTowerAtCamera)).Append(',').Append(F(sky.CloudMesoscaleAtCamera)).Append(',').Append(F(sky.CloudClimbHeight)).Append(',')
					.Append(F(sky.CloudShellBottom)).Append(',').Append(F(sky.CloudShellTop)).Append(',')
					.Append(F(Band(sky.CloudBandCoverage, 1))).Append(',').Append(F(Band(sky.CloudBandBottom, 1))).Append(',')
					.Append(F(Band(sky.CloudBandCoverage, 2))).Append(',').Append(F(Band(sky.CloudBandCoverage, 3))).Append(',');
			}
			else
			{
				runTrace.Append("0,0,0,0,0,0,0,0,0,0,0,0,");
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
		public WeatherPreset ActivePreset { get; private set; }

		/// <summary>
		/// The sky's own weather, set channel by channel from the panel rather than built out of
		/// layers on a timeline.
		/// </summary>
		/// <remarks>
		/// Two different things are wanted from a bed like this. Most of the time it is "what does a
		/// thunderstorm look like", which is a question about the weather model and has to go through
		/// presets, layers and cells or it proves nothing. Sometimes it is "what does the sky look
		/// like at exactly this much cloud and this much aurora", which the model cannot be asked
		/// directly because no preset lands on those numbers. So the channels can be driven straight,
		/// and <see cref="DriveWeatherDirectly"/> says which of the two is happening.
		/// </remarks>
		public WeatherFrame SkyWeather
		{
			get => skyWeather;
			set
			{
				skyWeather = value;
				DriveWeatherDirectly = true;
			}
		}

		/// <summary>
		/// True while the channel sliders drive the weather and the timeline is ignored. Applying a
		/// preset, a layer or a cell turns it back off, because those are requests to run the model.
		/// </summary>
		public bool DriveWeatherDirectly { get; set; }

		/// <summary>
		/// Whether applying a preset moves the scene to a temperature the preset can actually work
		/// at. On by default: clicking Light Snow in a warm scene otherwise gives rain, because
		/// falling snow turns to rain above freezing and a snow layer does not apply at all outside
		/// its template's range. That is the model behaving correctly and a bed behaving unhelpfully.
		/// </summary>
		public bool MatchTemperature { get; set; } = true;

		/// <summary>The temperature the last preset asked for, or null when it did not care.</summary>
		public float? PresetTemperature { get; private set; }

		/// <summary>
		/// The scene's climate offset. One meaning, not two: the sky bed used to keep a plain float
		/// of its own here while the weather bed wrote the scene's offset, so the same word on two
		/// panels moved two different numbers.
		/// </summary>
		public float Temperature
		{
			get => Settings != null ? Settings.RuntimeTemperatureOffset : 0f;
			set
			{
				if (Settings != null)
				{
					Settings.RuntimeTemperatureOffset = value;
				}
			}
		}

		/// <summary>Fades the preset's layers in and everything else out, like the server's ApplyPreset.</summary>
		public void ApplyPreset(WeatherPreset preset, float seconds)
		{
			DriveWeatherDirectly = false;
			uint now = Tick;
			uint end = now + timeline.SecondsToTicks(seconds);
			for (int i = 0; i < timeline.Layers.Count; i++)
			{
				WeatherLayerEntry entry = timeline.Layers[i];
				if (entry.Handle >= ManualHandleBase)
				{
					continue;
				}
				entry.From = entry.IntensityAt(now);
				entry.To = 0f;
				// The weather going out holds on while the new weather rises, or the sky passes
				// through clear on its way from one to the other.
				WeatherTimeline.OutgoingWindow(now, end, out uint outStart, out uint outEnd);
				entry.StartTick = outStart;
				entry.EndTick = outEnd;
				entry.RemoveWhenDone = true;
				timeline.Layers[i] = entry;
			}
			ActivePreset = preset;
			if (MatchTemperature)
			{
				MatchTemperatureTo(preset);
			}
			if (preset != null)
			{
				foreach (WeatherPresetLayer layer in preset.Layers)
				{
					if (layer?.Template == null)
					{
						continue;
					}
					WeatherTimeline.IncomingWindow(now, end, out uint inStart, out uint inEnd);
					timeline.UpsertLayer(new WeatherLayerEntry
					{
						Handle = nextHandle++,
						TemplateID = layer.Template.ID,
						From = 0f,
						To = layer.Intensity,
						StartTick = inStart,
						EndTick = inEnd,
					});
				}
			}
			timeline.Revision++;
		}

		/// <summary>
		/// Moves the scene's temperature into a range where the preset's layers apply and what falls
		/// is what the preset is named after.
		/// </summary>
		private void MatchTemperatureTo(WeatherPreset preset)
		{
			PresetTemperature = null;
			if (preset == null)
			{
				return;
			}
			float low = -1f, high = 1f;
			bool snow = false, warm = false;
			foreach (WeatherPresetLayer layer in preset.Layers)
			{
				WeatherLayerTemplate template = layer?.Template;
				if (template == null || layer.Intensity <= 0f)
				{
					continue;
				}
				low = Mathf.Max(low, template.MinTemperature);
				high = Mathf.Min(high, template.MaxTemperature);
				snow |= template.Kind == WeatherLayerKind.Snow;
				// Rain and hail are water: they want a scene above freezing, or they arrive as snow.
				warm |= template.Kind == WeatherLayerKind.Rain || template.Kind == WeatherLayerKind.Hail;
			}
			MatchTemperatureFor(snow, warm, low, high);
		}

		/// <summary>
		/// Moves the scene to a temperature at which what is being asked for can actually fall as
		/// itself, within what the templates involved allow.
		/// </summary>
		/// <remarks>
		/// The model retypes what falls by the temperature it finds — water becomes snow below
		/// freezing and snow becomes rain above it — so a scene left cold by an earlier snowfall
		/// turns the next shower into more snow. Presets and hand-set layers both go through here,
		/// because the rain slider is no less a request for rain than the Rain preset is.
		/// </remarks>
		private void MatchTemperatureFor(bool snow, bool warm, float low, float high)
		{
			if (high < low)
			{
				return;
			}
			// Start from the temperature the scene actually has, not from the offset: they are not
			// the same number, and the offset is meaningless on its own.
			float actual = lastSample.Temperature;
			float wanted = actual;
			// Past these the weather model turns what falls into the other thing entirely.
			if (snow && wanted > -0.2f)
			{
				wanted = -0.4f;
			}
			else if (warm && wanted < 0.1f)
			{
				wanted = 0.3f;
			}
			wanted = Mathf.Clamp(wanted, low, high);
			if (Mathf.Approximately(wanted, actual))
			{
				return;
			}
			// The slider is an offset on the scene's own climate, and every gate in the model reads
			// the temperature that comes *out* of that. Setting the offset to -0.4 in a scene whose
			// climate sits at +0.7 leaves it at +0.3, which is still no weather for snow.
			float baseline = actual - Temperature;
			float offset = Mathf.Clamp(wanted - baseline, -1f, 1f);
			if (!Mathf.Approximately(offset, Temperature))
			{
				Temperature = offset;
				PresetTemperature = wanted;
			}
		}

		/// <summary>Sets a manual layer of one kind (0 removes it).</summary>
		public void SetLayer(WeatherLayerKind kind, float intensity, float seconds = 1f)
		{
			WeatherLayerTemplate template = Templates.Find(t => t != null && t.Kind == kind);
			if (template == null)
			{
				return;
			}
			DriveWeatherDirectly = false;
			// A hand-set layer has to be able to fall as what it is, exactly as a preset does.
			if (MatchTemperature && intensity > 0f)
			{
				MatchTemperatureFor(kind == WeatherLayerKind.Snow,
					kind == WeatherLayerKind.Rain || kind == WeatherLayerKind.Hail,
					template.MinTemperature, template.MaxTemperature);
			}
			ushort handle = (ushort)(ManualHandleBase + (int)kind);
			uint now = Tick;
			float from = timeline.TryGetLayer(handle, out int index) ? timeline.Layers[index].IntensityAt(now) : 0f;
			timeline.UpsertLayer(new WeatherLayerEntry
			{
				Handle = handle,
				TemplateID = template.ID,
				From = from,
				To = Mathf.Clamp01(intensity),
				StartTick = now,
				EndTick = now + timeline.SecondsToTicks(seconds),
				RemoveWhenDone = intensity <= 0f,
			});
			timeline.Revision++;
		}

		/// <summary>The manual intensity set for a kind.</summary>
		public float ManualLayer(WeatherLayerKind kind)
		{
			return timeline.TryGetLayer((ushort)(ManualHandleBase + (int)kind), out int index) ? timeline.Layers[index].To : 0f;
		}

		/// <summary>Starts a storm cell upwind that drifts over the camera.</summary>
		public void SpawnCell(WeatherPreset preset, float radius = 250f, float speed = 6f)
		{
			if (preset == null || Camera == null)
			{
				return;
			}
			DriveWeatherDirectly = false;
			Vector3 at = Camera.transform.position;
			float angle = (Tick % 360) * Mathf.Deg2Rad;
			var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
			float distance = radius * 1.5f;
			uint now = Tick;
			uint lifetime = timeline.SecondsToTicks((distance * 2f) / speed + 30f);
			timeline.UpsertCell(new StormCell
			{
				ID = nextCell++,
				PresetID = preset.ID,
				Seed = (uint)(now * 2654435761u),
				OriginX = at.x - direction.x * distance,
				OriginZ = at.z - direction.y * distance,
				VelocityX = direction.x * speed,
				VelocityZ = direction.y * speed,
				RadiusMeters = radius,
				PeakIntensity = 1f,
				MeanderMeters = 20f,
				MotionTick = now,
				BirthTick = now,
				MatureTick = now + timeline.SecondsToTicks(10f),
				DecayTick = now + lifetime - timeline.SecondsToTicks(10f),
				DeathTick = now + lifetime,
			});
			timeline.Revision++;
		}

		public void ClearAll(float seconds)
		{
			ApplyPreset(null, seconds);
			foreach (WeatherLayerKind kind in (WeatherLayerKind[])Enum.GetValues(typeof(WeatherLayerKind)))
			{
				if (ManualLayer(kind) > 0f)
				{
					SetLayer(kind, 0f, seconds);
				}
			}
			timeline.Cells.Clear();
			timeline.Revision++;
			// Clear means clear: the ground goes back to bare as well. Weather stopping does not dry
			// the ground — that takes a quarter of an hour, and the panel can fast-forward it — but
			// this button is the bed's reset, not a forecast.
			ResetCover();
			// And the climate goes back to the scene's own. A snow preset leaves the offset well
			// below freezing, and leaving it there is what makes the next rain fall as snow.
			Temperature = 0f;
			PresetTemperature = null;
			// The sky's own channels are part of "clear" too, and so is the mode they put the bed in.
			skyWeather = WeatherFrame.Clear;
			DriveWeatherDirectly = false;
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
			WeatherPreset start = string.IsNullOrEmpty(StartPreset)
				? null
				: Presets.Find(p => p != null && p.ResolvedName == StartPreset);
			if (start != null)
			{
				ApplyPreset(start, 0f);
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
			WeatherClient.LocalPreview = null;
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
		/// Puts the solar system, its bodies, the calendar, the sky profiles, the weather templates
		/// and the presets in the cache the game would have loaded them into. Anything already
		/// cached (a running client) is left be.
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
			foreach (WeatherLayerTemplate template in Templates)
			{
				RegisterTemplate(template);
			}
			foreach (WeatherPreset preset in Presets)
			{
				if (preset == null)
				{
					continue;
				}
				if (WeatherPreset.Get<WeatherPreset>(preset.ID) != preset)
				{
					preset.AddToCache(preset.name);
					registered.Add(preset);
				}
				foreach (WeatherPresetLayer layer in preset.Layers)
				{
					RegisterTemplate(layer?.Template);
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

		/// <summary>Caches a template unless something (the client's loader) already has.</summary>
		private void RegisterTemplate(WeatherLayerTemplate template)
		{
			if (template != null && WeatherLayerTemplate.Get<WeatherLayerTemplate>(template.ID) != template)
			{
				template.AddToCache(template.name);
				registered.Add(template);
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
			// And on which body, so the weather's season and hour are this body's and not the scene's.
			timeline.BodyOverride = Body;

			WeatherFrame frame;
			WeatherContext context;
			if (DriveWeatherDirectly)
			{
				// The channels are the whole weather: no timeline, no cells, nothing sampled. This is
				// the sky bed's old behaviour, kept because asking for exactly this much cloud is a
				// thing a designer needs and no preset lands on it.
				frame = skyWeather;
				lastSample = new WeatherSample
				{
					Frame = frame,
					Background = frame,
					Temperature = Temperature,
				};
				context = new WeatherContext
				{
					Scene = scene,
					Settings = Settings,
					ViewerPosition = viewer,
					Tick = tick,
					WorldHours = hours,
					LocalTime01 = localTime,
					IsDaylight = DayNight == null || DayNight.DaylightNow,
					Temperature = Temperature,
					Background = frame,
				};
			}
			else
			{
				lastSample = WeatherField.Sample(timeline, Settings, scene, viewer, tick);
				frame = lastSample.Frame;
				context = new WeatherContext
				{
					Scene = scene,
					Settings = Settings,
					Timeline = timeline,
					ViewerPosition = viewer,
					Tick = tick,
					WorldHours = hours,
					Cover = timeline.Cover,
					Background = lastSample.Background,
					DriverWeight = lastSample.DriverWeight,
					Shelter = lastSample.Shelter,
					Temperature = lastSample.Temperature,
					IsDaylight = DayNight == null || DayNight.DaylightNow,
					LocalTime01 = localTime,
				};
			}
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
