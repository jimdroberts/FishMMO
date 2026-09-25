using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>ECA event data for a sky event (full moon, eclipse, conjunction…). Initiator is null.</summary>
	public class CelestialEventData : EventData
	{
		public CelestialEvent Event { get; }

		public CelestialEventData(CelestialEvent celestialEvent)
			: base(null)
		{
			Event = celestialEvent;
		}
	}

	/// <summary>A trigger that runs when a kind of sky event begins.</summary>
	[Serializable]
	public class CelestialTrigger
	{
		public CelestialEventKind Kind;
		public WorldSceneTrigger Trigger = new WorldSceneTrigger();
	}

	/// <summary>
	/// A scene's day and night: whether it is day, what the sky holds, objects that follow the
	/// day, the night or the stars, and the triggers that run on day, night and sky events.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Time is the world clock, the same on every server and client. Day and night come from the
	/// scene's body turning under its sun (<see cref="CelestialState"/>): a scene's latitude,
	/// longitude and heading come from its world atlas entry, and a developer-fixed time shows the
	/// sky at that local time.
	/// </para>
	/// <para>
	/// Rebuilt for issue #238. The old component lerped two skybox materials across the whole
	/// day (so noon was half night), only did so when it had objects to rotate, rotated those
	/// objects incrementally from an assumed dawn, ran each scene server on its own clock,
	/// refreshed ambient lighting every second and wrote the skybox from two places. The client's
	/// SkySystem now owns the sky, lights and ambient; this owns time and triggers, and turns
	/// <see cref="RotateObjects"/> absolutely with the stars.
	/// </para>
	/// </remarks>
	public class WorldDayNightCycle : MonoBehaviour
	{
		[Tooltip("Enable/Disable the day night cycle.")]
		public bool DayNightCycle = true;

		// No sun and no moon here. This component says when and where a scene is; the solar system
		// says where that puts its sun and moons, and the client's SkySystem makes the lights and
		// drives them. It used to carry a Sun Light and a Moon Light — assigned, or found in the scene
		// by their names — and hand them to the sky to be driven. That made every scene place two
		// lights that did nothing of their own, on a component that also runs on the server, where a
		// light means nothing at all.
		[Header("Sky")]
		[Tooltip("This scene's sky. Empty: the sky of the body the scene is on.")]
		public SkyProfile SkyOverride;

		[Tooltip("Objects that turn with the stars, keeping the rotation they were authored with.")]
		public List<GameObject> RotateObjects = new List<GameObject>();
		[Tooltip("These objects will be enabled during the day.")]
		public List<GameObject> DayObjects = new List<GameObject>();
		[Tooltip("These objects will be enabled at night.")]
		public List<GameObject> NightObjects = new List<GameObject>();
		[Tooltip("The time in seconds that objects will take to fade in or out.")]
		public float FadeThreshold = 1f;
		[Tooltip("Objects shown by day, fading at dusk. Transparent materials fade; opaque ones switch at the midpoint.")]
		public List<GameObject> DayFadeObjects = new List<GameObject>();
		[Tooltip("Objects shown at night, fading at dawn.")]
		public List<GameObject> NightFadeObjects = new List<GameObject>();
		[ShowReadonly]
		[SerializeField]
		private bool isDaytime = true;

		[Header("ECA - Day/Night Triggers")]
		[Tooltip("Triggers executed once when this scene loads (e.g. apply default fog). EventData: DayNightEventData.")]
		[SerializeField]
		private List<WorldSceneTrigger> onSceneLoadTriggers = new List<WorldSceneTrigger>();

		[Tooltip("Triggers executed when day begins. EventData: DayNightEventData (IsDaytime = true).")]
		[SerializeField]
		private List<WorldSceneTrigger> onDayStartTriggers = new List<WorldSceneTrigger>();

		[Tooltip("Triggers executed when night begins. EventData: DayNightEventData (IsDaytime = false).")]
		[SerializeField]
		private List<WorldSceneTrigger> onNightStartTriggers = new List<WorldSceneTrigger>();

		[Header("ECA - Sky Event Triggers")]
		[Tooltip("Triggers executed when a sky event begins in this scene's sky. EventData: CelestialEventData.")]
		[SerializeField]
		private List<CelestialTrigger> onCelestialEvents = new List<CelestialTrigger>();

		/// <summary>Seconds between server-side sky evaluations.</summary>
		public const float ServerEvaluationSeconds = 1f;

		private static readonly List<WorldDayNightCycle> active = new List<WorldDayNightCycle>();

		/// <summary>The most recently enabled cycle: the scene the client is in.</summary>
		public static WorldDayNightCycle Current => active.Count > 0 ? active[active.Count - 1] : null;

		/// <summary>A world time to show instead of the clock (test scenes and previews; never on a server).</summary>
		public static double? PreviewHours;
		/// <summary>A local time of day (0.5 noon) to show on the preview date instead of the clock's.</summary>
		public static double? PreviewLocalTime01;
		/// <summary>A latitude to show instead of the scene's.</summary>
		public static double? PreviewLatitude;
		/// <summary>A longitude to show instead of the scene's.</summary>
		public static double? PreviewLongitude;
		/// <summary>A scene heading to show instead of the atlas entry's.</summary>
		public static float? PreviewHeading;
		/// <summary>A planet or moon to stand on instead of the scene's (test scenes and previews).</summary>
		public static WorldBody PreviewBody;

		/// <summary>
		/// The body a scene stands on, as the sky has it: a preview's, else the scene's own, else the
		/// home world. Whatever depends on the world underfoot — the sea's gravity, what falls —
		/// asks here, so it cannot disagree with the sky about which world this is.
		/// </summary>
		public static WorldBody BodyFor(WorldSceneSettings settings)
		{
			return PreviewBody != null ? PreviewBody : SceneTime.BodyOf(settings);
		}
		/// <summary>
		/// Whether a preview's own clock runs on from <see cref="PreviewHours"/>. Off for a preview
		/// that advances <see cref="PreviewHours"/> itself, so time does not run twice.
		/// </summary>
		public static bool PreviewClockAdvances = true;

		/// <summary>The sky of this scene, as last computed.</summary>
		public CelestialState State { get; } = new CelestialState();

		/// <summary>The world time the state was computed for (moved to the fixed time of day, if any).</summary>
		public double Hours { get; private set; }

		/// <summary>
		/// The world clock itself, never moved: what scheduled effects (lightning, meteors, cloud
		/// drift) run on, so they keep going in a scene with a fixed time of day.
		/// </summary>
		public double ClockHours { get; private set; }

		/// <summary>
		/// The world clock in hours as the sky last read it — a preview's fast or stopped clock
		/// included — or null before any cycle has run. What else keeps world time reads this, so it
		/// moves with the sky and not with the wall clock.
		/// </summary>
		/// <remarks>
		/// The sea read the wall clock (<see cref="WorldTime.UnanchoredHours"/>): a preview stopped at
		/// noon still had its wind, its waves' period and its tide drifting on in real time, and one
		/// running a hundred and eighty times as fast had a sea whose weather ran at one.
		/// </remarks>
		public static double? SkyClockHours { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetSkyClock()
		{
			SkyClockHours = null;
		}

		public bool IsDaytime => isDaytime;

		private NetworkManager networkManager;
		private TimeManager timeManager;
		private bool sceneLoadTriggersPending = true;
		private float serverTimer;
		private readonly CelestialEventTracker tracker = new CelestialEventTracker();
		private readonly List<CelestialEvent> events = new List<CelestialEvent>();
		private readonly List<(Transform transform, Quaternion authored)> rotating = new List<(Transform, Quaternion)>();
		private Renderer[] dayFadeRenderers;
		private Renderer[] nightFadeRenderers;
		private float fadeTime;
		private MaterialPropertyBlock fadePropertyBlock;
		private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
		private static readonly int VisiblePropertyId = Shader.PropertyToID("_FishVisible");
		private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

		private void Awake()
		{
			networkManager = FindSceneNetworkManager();
			timeManager = networkManager == null ? null : networkManager.TimeManager;
			if (timeManager != null)
			{
				timeManager.OnTick += TimeManager_OnTick;
			}
			CacheRotating();
#if !UNITY_SERVER
			dayFadeRenderers = CacheRenderers(DayFadeObjects);
			nightFadeRenderers = CacheRenderers(NightFadeObjects);
#endif
			Evaluate();
			UpdateDayNightState(State.IsDaylight, true);
			TryInvokeSceneLoadTriggers();
		}

		private void OnEnable()
		{
			active.Remove(this);
			active.Add(this);
		}

		private void OnDisable()
		{
			active.Remove(this);
		}

		private void OnDestroy()
		{
			if (timeManager != null)
			{
				timeManager.OnTick -= TimeManager_OnTick;
				timeManager = null;
			}
			networkManager = null;
		}

		/// <summary>Remembers each rotating object's authored rotation.</summary>
		private void CacheRotating()
		{
			rotating.Clear();
			foreach (GameObject obj in RotateObjects)
			{
				if (obj == null)
				{
					continue;
				}
				rotating.Add((obj.transform, obj.transform.rotation));
			}
		}

		/// <summary>The time, place and sky of this scene now.</summary>
		private void Evaluate()
		{
			// A preview's clock still runs, from the chosen moment.
			double hours = PreviewHours.HasValue
				? PreviewHours.Value + (PreviewClockAdvances ? Time.timeAsDouble / 3600.0 : 0.0)
				: WorldTime.CurrentHours(timeManager);
			ClockHours = hours;
			SkyClockHours = hours;
			WorldSceneSettings settings = SceneSettings();
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = BodyFor(settings);
			double latitude = PreviewLatitude ?? (settings != null ? settings.Latitude : 0.0);
			double longitude = PreviewLongitude ?? (settings != null ? settings.Longitude : 0.0);
			float heading = PreviewHeading ?? (settings != null && settings.AtlasEntry != null ? settings.AtlasEntry.HeadingDegrees : 0f);

			double? fixedTime = PreviewLocalTime01 ?? (settings != null && settings.TimeMode == SceneTimeMode.Fixed ? settings.FixedTimeOfDay01 : (double?)null);
			if (fixedTime.HasValue && system != null && body != null)
			{
				// Show the sky at the fixed local time: move to the nearest moment that has it.
				double local = CelestialMath.LocalTime01(system, body, hours, longitude);
				double shift = fixedTime.Value - local;
				shift -= Math.Round(shift);
				double day = CelestialMath.SolarDayHours(system, body);
				if (!double.IsInfinity(day))
				{
					hours += shift * day;
				}
			}
			Hours = hours;
			State.Compute(system, body, hours, latitude, longitude, heading);
			if (system == null || body == null)
			{
				// No solar system: fall back to the plain clock so day and night still happen.
				bool day = SceneTime.IsDaylight(settings, hours);
				SetFallbackDaylight(day);
			}
		}

		private WorldSceneSettings sceneSettings;

		/// <summary>This scene's settings (registered once enabled; searched for before that).</summary>
		private WorldSceneSettings SceneSettings()
		{
			if (sceneSettings != null)
			{
				return sceneSettings;
			}
			if (WorldSceneSettings.TryGetForScene(gameObject.scene, out WorldSceneSettings registered))
			{
				return sceneSettings = registered;
			}
			foreach (GameObject root in gameObject.scene.GetRootGameObjects())
			{
				WorldSceneSettings found = root.GetComponentInChildren<WorldSceneSettings>(true);
				if (found != null)
				{
					return sceneSettings = found;
				}
			}
			return null;
		}

		private bool fallbackDaylight = true;

		private void SetFallbackDaylight(bool day) => fallbackDaylight = day;

		/// <summary>Whether it is day now, by the sky or by the plain clock.</summary>
		public bool DaylightNow => State.System != null && State.Observer != null ? State.IsDaylight : fallbackDaylight;

		private void Update()
		{
			TryInvokeSceneLoadTriggers();
			if (!DayNightCycle)
			{
				return;
			}
			bool server = CanExecuteWorldTriggers();
			if (!server)
			{
				Evaluate();
				UpdateDayNightState(DaylightNow);
				ApplyRotations();
				UpdateDayNightFading();
			}
		}

		private void TimeManager_OnTick()
		{
			TryInvokeSceneLoadTriggers();
			if (!DayNightCycle || !CanExecuteWorldTriggers())
			{
				return;
			}
			serverTimer -= (float)timeManager.TickDelta;
			if (serverTimer > 0f)
			{
				return;
			}
			serverTimer = ServerEvaluationSeconds;
			Evaluate();
			UpdateDayNightState(DaylightNow);
			events.Clear();
			tracker.Update(State, events);
			foreach (CelestialEvent celestialEvent in events)
			{
				InvokeCelestialTriggers(celestialEvent);
			}
		}

		private void ApplyRotations()
		{
			Quaternion sky = State.SkyRotation;
			for (int i = 0; i < rotating.Count; i++)
			{
				if (rotating[i].transform != null)
				{
					rotating[i].transform.rotation = sky * rotating[i].authored;
				}
			}
		}

		private void TryInvokeSceneLoadTriggers()
		{
			if (!sceneLoadTriggersPending || !CanExecuteWorldTriggers())
			{
				return;
			}
			sceneLoadTriggersPending = false;
			InvokeTriggers(onSceneLoadTriggers);
		}

		private bool CanExecuteWorldTriggers()
		{
			return networkManager != null && networkManager.IsServerStarted;
		}

		private NetworkManager FindSceneNetworkManager()
		{
			NetworkManager[] networkManagers = FindObjectsByType<NetworkManager>(FindObjectsInactive.Include);
			for (int i = 0; i < networkManagers.Length; i++)
			{
				NetworkManager candidate = networkManagers[i];
				if (candidate != null && candidate.gameObject.scene == gameObject.scene)
				{
					return candidate;
				}
			}
			// Scene servers and clients keep their NetworkManager in their bootstrap scene.
			return networkManagers.Length > 0 ? networkManagers[0] : null;
		}

		private void UpdateDayNightState(bool daylight, bool ignoreCurrentState = false)
		{
			if (daylight == isDaytime && !ignoreCurrentState)
			{
				return;
			}
			isDaytime = daylight;
			UpdateDayNightActivations(daylight, DayObjects);
			UpdateDayNightActivations(!daylight, NightObjects);
			fadeTime = FadeThreshold;
			if (!ignoreCurrentState)
			{
				InvokeTriggers(daylight ? onDayStartTriggers : onNightStartTriggers);
			}
		}

		private static void UpdateDayNightActivations(bool enable, List<GameObject> objects)
		{
			if (objects == null)
			{
				return;
			}
			for (int i = 0; i < objects.Count; ++i)
			{
				if (objects[i] != null)
				{
					objects[i].SetActive(enable);
				}
			}
		}

		private void UpdateDayNightFading()
		{
#if !UNITY_SERVER
			float alpha = 0f;
			if (fadeTime > 0f)
			{
				fadeTime -= Time.deltaTime;
				alpha = Mathf.Clamp01(fadeTime / Mathf.Max(0.01f, FadeThreshold));
			}
			if (dayFadeRenderers != null && dayFadeRenderers.Length > 0)
			{
				SetVisibility(dayFadeRenderers, isDaytime ? 1f - alpha : alpha);
			}
			if (nightFadeRenderers != null && nightFadeRenderers.Length > 0)
			{
				SetVisibility(nightFadeRenderers, isDaytime ? alpha : 1f - alpha);
			}
#endif
		}

		private static Renderer[] CacheRenderers(List<GameObject> objects)
		{
			if (objects == null || objects.Count < 1)
			{
				return Array.Empty<Renderer>();
			}
			var renderers = new List<Renderer>(objects.Count);
			foreach (GameObject obj in objects)
			{
				Renderer r = obj != null ? obj.GetComponent<Renderer>() : null;
				if (r != null)
				{
					renderers.Add(r);
				}
			}
			return renderers.ToArray();
		}

		/// <summary>
		/// Fades renderers out and in. A transparent material fades by alpha. An opaque one on a
		/// weather shader dissolves through its dither pattern, pixel by pixel. An opaque material
		/// on any other shader can do neither, so it switches at its own point in the fade, spread
		/// deterministically across it: the group then thins out instead of popping at once.
		/// </summary>
		private void SetVisibility(Renderer[] renderers, float visibility)
		{
			if (fadePropertyBlock == null)
			{
				fadePropertyBlock = new MaterialPropertyBlock();
			}
			for (int i = 0; i < renderers.Length; ++i)
			{
				Renderer r = renderers[i];
				if (r == null)
				{
					continue;
				}
				Material shared = r.sharedMaterial;
				bool transparent = shared != null && shared.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.GeometryLast + 1;
				if (!transparent)
				{
					if (shared != null && shared.HasProperty(VisiblePropertyId))
					{
						// A weather shader dissolves: it clips the pixels the pattern says are gone.
						r.enabled = visibility > 0.001f;
						r.GetPropertyBlock(fadePropertyBlock);
						fadePropertyBlock.SetFloat(VisiblePropertyId, visibility);
						r.SetPropertyBlock(fadePropertyBlock);
						continue;
					}
					r.enabled = visibility >= SwitchPointOf(r);
					continue;
				}
				r.enabled = visibility > 0.001f;
				r.GetPropertyBlock(fadePropertyBlock);
				Color baseColor = Color.white;
				if (shared.HasProperty(BaseColorPropertyId))
				{
					baseColor = shared.GetColor(BaseColorPropertyId);
				}
				else if (shared.HasProperty(ColorPropertyId))
				{
					baseColor = shared.GetColor(ColorPropertyId);
				}
				baseColor.a *= visibility;
				fadePropertyBlock.SetColor(BaseColorPropertyId, baseColor);
				fadePropertyBlock.SetColor(ColorPropertyId, baseColor);
				r.SetPropertyBlock(fadePropertyBlock);
			}
		}

		/// <summary>
		/// Where in the fade one renderer switches, 0.15..0.85. Stable for a renderer, so the same
		/// object always turns at the same moment and the group's dissolve does not shimmer.
		/// </summary>
		private static float SwitchPointOf(Renderer renderer)
		{
			uint hash = (uint)renderer.GetInstanceID() * 2654435761u;
			return 0.15f + 0.7f * ((hash >> 8 & 0xFFFF) / 65535f);
		}

		private void InvokeTriggers(List<WorldSceneTrigger> triggers)
		{
			if (!CanExecuteWorldTriggers() || triggers == null || triggers.Count == 0)
			{
				return;
			}
			var eventData = new DayNightEventData(isDaytime);
			for (int i = 0; i < triggers.Count; ++i)
			{
				triggers[i]?.Execute(eventData);
			}
		}

		private void InvokeCelestialTriggers(CelestialEvent celestialEvent)
		{
			if (!CanExecuteWorldTriggers() || onCelestialEvents == null)
			{
				return;
			}
			CelestialEventData eventData = null;
			for (int i = 0; i < onCelestialEvents.Count; ++i)
			{
				CelestialTrigger entry = onCelestialEvents[i];
				if (entry == null || entry.Kind != celestialEvent.Kind || entry.Trigger == null)
				{
					continue;
				}
				eventData ??= new CelestialEventData(celestialEvent);
				entry.Trigger.Execute(eventData);
			}
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			SanitizeAll(onSceneLoadTriggers);
			SanitizeAll(onDayStartTriggers);
			SanitizeAll(onNightStartTriggers);
			if (onCelestialEvents != null)
			{
				foreach (CelestialTrigger entry in onCelestialEvents)
				{
					entry?.Trigger?.Sanitize();
				}
			}
		}

		private static void SanitizeAll(List<WorldSceneTrigger> triggers)
		{
			if (triggers == null) return;
			for (int i = 0; i < triggers.Count; ++i)
			{
				triggers[i]?.Sanitize();
			}
		}
#endif
	}
}
