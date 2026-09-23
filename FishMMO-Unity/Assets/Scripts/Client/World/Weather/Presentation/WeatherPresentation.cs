using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The built-in weather presenter: shader globals, fog over the region's fog, wind, the
	/// precipitation field, sky occlusion and the audio loops.
	/// </summary>
	/// <remarks>
	/// Weather arrives ten times a second; this eases toward it every frame so nothing steps.
	/// The look and the budgets come from <see cref="WeatherRenderProfile"/>; with none loaded
	/// the presenter does nothing, and the game runs without weather visuals.
	/// </remarks>
	[AddComponentMenu("")]
	public sealed class WeatherPresentation : MonoBehaviour, IWeatherPresenter
	{
		/// <summary>Seconds for the shown weather to cover most of the way to a new frame.</summary>
		public const float SmoothingSeconds = 0.6f;

		private static WeatherPresentation instance;

		/// <summary>An explicit profile (the test scene); otherwise the loaded one.</summary>
		public WeatherRenderProfile Profile;

		/// <summary>The camera to present for; otherwise <see cref="Camera.main"/>.</summary>
		public Camera TargetCamera;

		private WeatherFrame target = WeatherFrame.Clear;
		private WeatherFrame shown = WeatherFrame.Clear;
		private WeatherContext context;
		private bool hasContext;
		private float time;
		// Made in Awake: these allocate a MaterialPropertyBlock, which Unity refuses inside a
		// MonoBehaviour's constructor (field initialisers run there). The exception is thrown out of
		// the constructor, so the field is left NULL and every later use of it throws instead —
		// which reads as an unrelated NullReferenceException in whatever touched it first.
		private PrecipitationField precipitation;
		private PrecipitationSplashField splashes;
		private SkyOcclusionMap occlusion;
		private WeatherCoverMap coverMap;
		private WeatherAudioPresenter audioPresenter;
		private WindZone wind;

		public static WeatherPresentation Instance => instance;
		/// <summary>The last context the weather arrived with.</summary>
		public WeatherContext Context => context;
		public bool HasContext => hasContext;
		/// <summary>0..1 lightning flash, set by the sky each frame.</summary>
		public float LightningFlash { get; set; }
		public WeatherFrame Shown => shown;
		public SkyOcclusionMap Occlusion => occlusion;

		/// <summary>Where snow lies and the ground is wet, around the camera.</summary>
		public WeatherCoverMap CoverMap => coverMap;
		public PrecipitationField Precipitation => precipitation;

		/// <summary>Creates the presenter (once) and registers it.</summary>
		public static WeatherPresentation Ensure()
		{
			if (instance != null)
			{
				return instance;
			}
			var go = new GameObject("Weather Presentation");
			DontDestroyOnLoad(go);
			WeatherPresentation presentation = go.AddComponent<WeatherPresentation>();
			SkySystem.Ensure(go);
			return presentation;
		}

		private void Awake()
		{
			if (instance != null && instance != this)
			{
				Destroy(this);
				return;
			}
			instance = this;
			EnsureParts();
			WeatherClient.RegisterPresenter(this);
			WeatherClient.AudioCue += OnAudioCue;
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		}

		/// <summary>
		/// Builds the parts that are not serialized. Also called from Update, because a domain
		/// reload (a script edited while playing) empties them without running Awake again.
		/// </summary>
		private void EnsureParts()
		{
			precipitation = precipitation ?? new PrecipitationField();
			splashes = splashes ?? new PrecipitationSplashField();
			occlusion = occlusion ?? new SkyOcclusionMap();
			coverMap = coverMap ?? new WeatherCoverMap();
			audioPresenter = audioPresenter ?? new WeatherAudioPresenter(transform);
			if (wind == null)
			{
				Transform existing = transform.Find("Weather Wind");
				GameObject windObject = existing != null ? existing.gameObject : new GameObject("Weather Wind");
				windObject.transform.SetParent(transform, false);
				// Not ??: a missing component is Unity's "fake null", which ?? treats as a value.
				WindZone found = windObject.GetComponent<WindZone>();
				wind = found != null ? found : windObject.AddComponent<WindZone>();
				wind.mode = WindZoneMode.Directional;
				wind.windMain = 0f;
			}
		}

		private void OnDestroy()
		{
			if (instance != this)
			{
				return;
			}
			WeatherClient.UnregisterPresenter(this);
			WeatherClient.AudioCue -= OnAudioCue;
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			precipitation?.Dispose();
			splashes?.Dispose();
			occlusion?.Dispose();
			coverMap?.Dispose();
			audioPresenter?.Dispose();
			FogComposer.SetWeather(0f, Color.gray, 0f, 1000f);
			WeatherShaderGlobals.Clear();
			instance = null;
		}

		public void Apply(in WeatherFrame frame, in WeatherContext ctx)
		{
			target = frame;
			context = ctx;
			hasContext = true;
		}

		public void Reset()
		{
			target = WeatherFrame.Clear;
			shown = WeatherFrame.Clear;
			hasContext = false;
			occlusion?.Invalidate();
			coverMap?.Clear();
			audioPresenter?.Silence();
			FogComposer.SetWeather(0f, Color.gray, 0f, 1000f);
			FogComposer.Reset();
			WeatherShaderGlobals.Clear();
		}

		private void OnAudioCue(WeatherAudioCue cue, float volume)
		{
			audioPresenter?.Play(cue, volume);
		}

		private WeatherRenderProfile ResolveProfile()
		{
			if (Profile == null)
			{
				Profile = WeatherRenderProfile.Active;
			}
			return Profile;
		}

		private void Update()
		{
			Step(Time.deltaTime);
		}

		/// <summary>
		/// Pushes the weather into the globals and the presenters now, instead of on the next frame.
		/// Apply only records what to show; a caller that changes the weather and renders in the same
		/// frame — a probe, or an editor preview — would otherwise render the state before the change.
		/// </summary>
		public void Flush()
		{
			Step(0f);
		}

		private void Step(float deltaTime)
		{
			EnsureParts();
			WeatherRenderProfile profile = ResolveProfile();
			if (profile == null)
			{
				return;
			}
			float dt = deltaTime;
			time += dt;
			shown = dt > 0f ? WeatherFrame.Lerp(shown, target, 1f - Mathf.Exp(-dt / SmoothingSeconds)) : shown;

			Camera camera = TargetCamera != null ? TargetCamera : Camera.main;
			WeatherTierSettings tier = profile.TierFor(QualitySettings.GetQualityLevel());

			if (camera != null)
			{
				Scene scene = hasContext && context.Scene.IsValid() ? context.Scene : camera.gameObject.scene;
				PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
				occlusion.Update(camera.transform.position, physics, tier, profile.OcclusionLayers);
			}
			float shelter = hasContext ? context.Shelter : 0f;
			if (camera != null && occlusion.IsCovered(camera.transform.position))
			{
				shelter = Mathf.Max(shelter, 1f);
			}

			// Where the cover lies, as opposed to how much of it the scene holds. The server's single
			// figure anchors this map; the map is what the ground is actually drawn from.
			if (camera != null && hasContext)
			{
				// The map starts over only when the timeline itself was replaced — a join, a resync,
				// another scene — and is pulled toward the server's figure only when the server
				// actually sent one. It used to start over on every change of revision, which is
				// every delta: each cell that spawned or retired and each preset clicked redrew the
				// whole ground from one number. And on every other frame it was anchored to that
				// same number, which the anchor's own summary says is for "when a snapshot arrives,
				// which is rarely" — so the ground never dried place by place at all. It tracked a
				// single scene-wide figure, and went dry the instant that figure did.
				WeatherTimeline ground = context.Timeline;
				bool reseed = ground != null && (!ReferenceEquals(ground, coverTimeline) || ground.Generation != coverGeneration);
				coverMap.Update(ground, context.Settings, camera.transform.position, (uint)context.Tick,
					context.Temperature, dt, context.Cover, reseed, sunlight: context.IsDaylight ? 1f : 0f);
				if (reseed)
				{
					coverTimeline = ground;
					coverGeneration = ground.Generation;
					coverSnapshots = ground.CoverSnapshots;
				}
				else if (ground != null && ground.CoverSnapshots != coverSnapshots)
				{
					coverSnapshots = ground.CoverSnapshots;
					coverMap.Anchor(context.Cover);
				}
			}

			WeatherShaderGlobals.Apply(shown, hasContext ? context.Cover : default, hasContext ? context.Temperature : 0f, shelter, time, LightningFlash,
				hasContext ? context.Substance : null);
			WeatherShaderGlobals.ApplyTier(tier.TerrainSnowDisplacement);
			WeatherFogPresenter.Apply(shown, profile);
			ApplyWind(shown);
			currentTier = tier;
			audioPresenter.Update(shown, shelter, profile.Audio, dt);
		}

		private WeatherTierSettings currentTier;
		private WeatherTimeline coverTimeline;
		private uint coverGeneration = uint.MaxValue;
		private uint coverSnapshots = uint.MaxValue;

		/// <summary>
		/// Precipitation is submitted as each camera starts rendering, so any render of the target
		/// camera, including one made by hand, draws it.
		/// </summary>
		private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
		{
			if (Profile == null || currentTier == null || camera == null)
			{
				return;
			}
			Camera wanted = TargetCamera != null ? TargetCamera : Camera.main;
			if (camera != wanted)
			{
				return;
			}
			// Rain used to be scaled by whichever cloud stood overhead. With the clouds a field
			// rather than a list of objects there is nothing to ask; the scene's forecast is what
			// falls, and a shower that follows the cloud above you wants the field sampled on the
			// CPU, which is work for when the cloud system has settled.
			// this.context, not the render callback's parameter of the same name.
			WeatherSubstance falling = hasContext ? this.context.Substance : null;
			precipitation.Render(shown, camera, currentTier, Profile, time, falling);
			// Where it lands. Needs the height map, so it draws nothing until that has been built.
			splashes?.Render(shown, camera, currentTier, Profile, time, occlusion != null && occlusion.IsValid, falling);
		}

		private void ApplyWind(in WeatherFrame frame)
		{
			float speed = frame[WeatherChannel.WindSpeed];
			float gust = frame[WeatherChannel.WindGust];
			wind.transform.rotation = Quaternion.Euler(0f, frame[WeatherChannel.WindHeading], 0f);
			wind.windMain = speed * 2f;
			wind.windTurbulence = gust * 1.5f;
			wind.windPulseMagnitude = gust;
			wind.windPulseFrequency = 0.1f + gust * 0.4f;
		}
	}
}
