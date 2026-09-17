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
		// Made in Awake: the precipitation field allocates a MaterialPropertyBlock, which Unity
		// refuses inside a MonoBehaviour's constructor (field initialisers run there).
		private PrecipitationField precipitation;
		private SkyOcclusionMap occlusion;
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
			precipitation = new PrecipitationField();
			occlusion = new SkyOcclusionMap();
			audioPresenter = new WeatherAudioPresenter(transform);
			var windObject = new GameObject("Weather Wind");
			windObject.transform.SetParent(transform, false);
			wind = windObject.AddComponent<WindZone>();
			wind.mode = WindZoneMode.Directional;
			wind.windMain = 0f;
			WeatherClient.RegisterPresenter(this);
			WeatherClient.AudioCue += OnAudioCue;
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
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
			occlusion?.Dispose();
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
			WeatherRenderProfile profile = ResolveProfile();
			if (profile == null)
			{
				return;
			}
			float dt = Time.deltaTime;
			time += dt;
			shown = WeatherFrame.Lerp(shown, target, 1f - Mathf.Exp(-dt / SmoothingSeconds));

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

			WeatherShaderGlobals.Apply(shown, hasContext ? context.Cover : default, hasContext ? context.Temperature : 0f, shelter, time, LightningFlash);
			WeatherFogPresenter.Apply(shown, profile);
			ApplyWind(shown);
			currentTier = tier;
			audioPresenter.Update(shown, shelter, profile.Audio, dt);
		}

		private WeatherTierSettings currentTier;

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
			precipitation.Render(shown, camera, currentTier, Profile, time);
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
