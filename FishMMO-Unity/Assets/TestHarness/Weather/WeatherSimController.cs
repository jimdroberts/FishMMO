using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.TestHarness.Weather
{
	/// <summary>
	/// The weather test bed: a local weather timeline run through the real weather model and the
	/// real presenters, driven from a control panel. No server, no network.
	/// </summary>
	/// <remarks>
	/// Presets and layers are applied exactly as the server's weather service would apply them
	/// (layer entries with transitions); storm cells are real cells moving across the camera;
	/// the temperature slider shifts the scene climate so the same storm re-types. Nothing here
	/// reaches the game.
	/// </remarks>
	public sealed class WeatherSimController : MonoBehaviour
	{
		public WeatherRenderProfile Profile;
		public Camera Camera;
		public Light Sun;
		public WorldSceneSettings Settings;
		public WorldDayNightCycle DayNight;
		[Tooltip("The solar system the sky is computed from (cached here, since the test bed loads no addressables).")]
		public SolarSystemProfile SolarSystem;
		public List<WeatherPreset> Presets = new List<WeatherPreset>();
		public List<WeatherLayerTemplate> Templates = new List<WeatherLayerTemplate>();
		[Min(1f)] public float TickRate = 30f;
		[Tooltip("Preset applied when the scene starts.")]
		public string StartPreset = "Medium Rain";

		private const ushort ManualHandleBase = 1000;

		private readonly WeatherTimeline timeline = new WeatherTimeline();
		private readonly List<ICachedObject> registered = new List<ICachedObject>();
		private WeatherPresentation presentation;
		private ushort nextHandle = 1;
		private ushort nextCell = 1;
		private float presentTimer;
		private float coverTimer;
		private WeatherSample lastSample;

		public WeatherTimeline Timeline => timeline;
		public WeatherSample LastSample => lastSample;
		public WeatherPresentation Presentation => presentation;
		public uint Tick => (uint)(Time.timeSinceLevelLoad * TickRate);
		public WeatherPreset ActivePreset { get; private set; }

		/// <summary>Raised after every presented frame, for the panel.</summary>
		public event Action<WeatherSimController> Presented;

		private void Awake()
		{
			if (SolarSystem != null && SolarSystemProfile.GetFirst<SolarSystemProfile>() == null)
			{
				SolarSystem.AddToCache(SolarSystem.name);
				registered.Add(SolarSystem);
				foreach (CelestialBody body in SolarSystem.Bodies)
				{
					if (body != null && CelestialBody.Get<CelestialBody>(body.ID) != body)
					{
						body.AddToCache(body.name);
						registered.Add(body);
					}
				}
			}
			WorldDayNightCycle.PreviewHours = DayOfYear * DayHours;
			WorldDayNightCycle.PreviewLocalTime01 = timeOfDay;
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
		}

		private void Start()
		{
			WeatherPreset start = Presets.Find(p => p != null && p.ResolvedName == StartPreset);
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
			WeatherQuery.Unregister(gameObject.scene);
			WeatherQuery.TickSource = null;
			WeatherClient.LocalPreview = null;
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

		/// <summary>Caches a template unless something (the client's loader) already has.</summary>
		private void RegisterTemplate(WeatherLayerTemplate template)
		{
			if (template != null && WeatherLayerTemplate.Get<WeatherLayerTemplate>(template.ID) != template)
			{
				template.AddToCache(template.name);
				registered.Add(template);
			}
		}

		// ── Controls ──

		/// <summary>Fades the preset's layers in and everything else out, like the server's ApplyPreset.</summary>
		public void ApplyPreset(WeatherPreset preset, float seconds)
		{
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
		/// Whether applying a preset moves the scene to a temperature the preset can actually work
		/// at. On by default: clicking Light Snow in a warm scene otherwise gives rain, because
		/// falling snow turns to rain above freezing and a snow layer does not apply at all outside
		/// its template's range. That is the model behaving correctly and a bed behaving unhelpfully.
		/// </summary>
		public bool MatchTemperature { get; set; } = true;

		/// <summary>The temperature the last preset asked for, or null when it did not care.</summary>
		public float? PresetTemperature { get; private set; }

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
			if (Presentation != null && Presentation.CoverMap != null)
			{
				Presentation.CoverMap.Hold(default);
			}
		}

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

		private double timeOfDay = 0.45;
		private float dayOfYear = 172f;

		private static double DayHours => SolarSystemProfile.Active != null ? CelestialMath.HomeSolarDayHours(SolarSystemProfile.Active) : 6.0;

		/// <summary>Local time of day, 0.5 noon.</summary>
		public double TimeOfDay
		{
			get => timeOfDay;
			set
			{
				timeOfDay = value;
				WorldDayNightCycle.PreviewLocalTime01 = value;
			}
		}

		/// <summary>Home calendar day: seasons and moon phases.</summary>
		public float DayOfYear
		{
			get => dayOfYear;
			set
			{
				dayOfYear = value;
				WorldDayNightCycle.PreviewHours = value * DayHours;
			}
		}

		/// <summary>Latitude the sky is shown at.</summary>
		public float Latitude
		{
			get => (float)(WorldDayNightCycle.PreviewLatitude ?? 0.0);
			set => WorldDayNightCycle.PreviewLatitude = value;
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

		private WeatherCover? coverOverride;

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

		// ── Loop ──

		private void Update()
		{
			uint tick = Tick;
			float dt = Time.deltaTime;
			coverTimer += dt;
			if (coverTimer >= 1f)
			{
				timeline.Prune(tick);
				timeline.Cover.Integrate(lastSample.Frame, lastSample.Temperature, coverTimer);
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

		private bool forcing;

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
			lastSample = WeatherField.Sample(timeline, Settings, scene, viewer, tick);
			var context = new WeatherContext
			{
				Scene = scene,
				Settings = Settings,
				Timeline = timeline,
				ViewerPosition = viewer,
				Tick = tick,
				Cover = timeline.Cover,
				Background = lastSample.Background,
				Shelter = lastSample.Shelter,
				Temperature = lastSample.Temperature,
				IsDaylight = DayNight == null || DayNight.DaylightNow,
				LocalTime01 = timeOfDay,
			};
			WeatherClient.Present(lastSample.Frame, context);
			if (forcing && Presentation != null)
			{
				// Apply only records; this is what writes the globals a render is about to read.
				Presentation.Flush();
			}
			Presented?.Invoke(this);
		}
	}
}
