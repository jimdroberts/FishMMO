using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The one owner of everything the sky touches on the client: the skybox material, the sun and
	/// moon lights, ambient light, the sky's reflection, the fog's sky colour, and the sky's
	/// moons, planets, comets, meteors, clouds, lightning and curtains.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It reads the scene's <see cref="WorldDayNightCycle"/> (time and <see cref="CelestialState"/>),
	/// the sky profile (region override, then scene, then body, then the default) and the weather
	/// being shown, and writes <c>RenderSettings.skybox</c>, <c>RenderSettings.sun</c>, the
	/// ambient colours and the reflection. Nothing else may write those (a source-scan test
	/// enforces it); brightness keeps <c>ambientIntensity</c>.
	/// </para>
	/// <para>
	/// A scene with no day/night cycle (login, character select) is left exactly as authored.
	/// </para>
	/// </remarks>
	[DefaultExecutionOrder(200)]
	[AddComponentMenu("")]
	public sealed class SkySystem : MonoBehaviour
	{
		public const float ReflectionRefreshSeconds = 10f;
		public const float ReflectionSunDegrees = 5f;
		public const float BodyRefreshSeconds = 0.25f;
		public const float WeatherMapRefreshSeconds = 0.35f;
		public const int MaxSuns = 4;

		/// <summary>
		/// The lowest cloud-base reading taken as a real one. The weather's cloud-base channel runs
		/// from about 0.8 (dry, high base) to 0.3 (damp, low base); anything under this is a frame
		/// that never set the channel, not a cloud deck at ground level.
		/// </summary>
		public const float CondensationFloor = 0.2f;

		private static readonly int ZenithId = Shader.PropertyToID("_FishSkyZenith");
		private static readonly int HorizonId = Shader.PropertyToID("_FishSkyHorizon");
		private static readonly int GroundId = Shader.PropertyToID("_FishSkyGround");
		private static readonly int FogColorId = Shader.PropertyToID("_FishSkyFogColor");
		private static readonly int ParamsId = Shader.PropertyToID("_FishSkyParams");
		private static readonly int SunShapeId = Shader.PropertyToID("_FishSunShape");
		private static readonly int EclipseId = Shader.PropertyToID("_FishSkyEclipse");
		private static readonly int EclipseBodyId = Shader.PropertyToID("_FishSkyEclipseBody");
		private static readonly int SunDirId = Shader.PropertyToID("_FishSunDir");
		private static readonly int SunColorId = Shader.PropertyToID("_FishSunColor");
		private static readonly int SunCountId = Shader.PropertyToID("_FishSunCount");
		private static readonly int StarMatrixId = Shader.PropertyToID("_FishStarMatrix");
		private static readonly int StarCubeId = Shader.PropertyToID("_FishStarCube");
		private static readonly int GalaxyCubeId = Shader.PropertyToID("_FishGalaxyCube");
		private static readonly int GalaxyParamsId = Shader.PropertyToID("_FishGalaxyParams");
		private static readonly int NoiseId = Shader.PropertyToID("_FishWeatherNoise");
		private static readonly int CloudLitId = Shader.PropertyToID("_FishCloudLit");
		private static readonly int CloudShadowId = Shader.PropertyToID("_FishCloudShadow");
		private static readonly int CloudLayerId = Shader.PropertyToID("_FishCloudLayer");
		private static readonly int CloudShapeParamsId = Shader.PropertyToID("_FishCloudShapeParams");
		private static readonly int CloudWindId = Shader.PropertyToID("_FishCloudWind");
		private static readonly int CloudLayerEId = Shader.PropertyToID("_FishCloudLayerE");
		private static readonly int CloudLayerFId = Shader.PropertyToID("_FishCloudLayerF");
		private static readonly int CloudLayerGId = Shader.PropertyToID("_FishCloudLayerG");
		private static readonly int CloudMesoId = Shader.PropertyToID("_FishCloudMeso");
		private static readonly int CloudMesoParamsId = Shader.PropertyToID("_FishCloudMesoParams");
		private static readonly int CloudMesoSeedId = Shader.PropertyToID("_FishCloudMesoSeed");
		private static readonly int CloudColumnId = Shader.PropertyToID("_FishCloudColumn");
		private static readonly int CloudTowerDriftId = Shader.PropertyToID("_FishCloudTowerDrift");
		private static readonly int CloudTowerSeedId = Shader.PropertyToID("_FishCloudTowerSeed");
		private static readonly int CloudSubId = Shader.PropertyToID("_FishCloudSub");
		private static readonly int CloudViewerId = Shader.PropertyToID("_FishCloudViewer");
		private static readonly int CloudFlowId = Shader.PropertyToID("_FishCloudFlow");

		/// <summary>
		/// The height of ground this air has the energy to climb, in metres: the wind's speed over
		/// the air's stability (U/N). Ground lower than this is crossed — the cloud rides up and
		/// over it; ground higher splits the flow and the cloud goes round.
		/// </summary>
		public float CloudClimbHeight { get; private set; } = 800f;

		/// <summary>The cloud shadow as the light reads it, for a panel to show.</summary>
		public Texture CloudShadowCookie => cloudShadows != null ? cloudShadows.Cookie : null;
		private static readonly int CloudWindDirId = Shader.PropertyToID("_FishCloudWindDir");
		private static readonly int CloudScreenId = Shader.PropertyToID("_FishCloudScreen");
		private static readonly int CloudLightId = Shader.PropertyToID("_FishCloudLight");
		private static readonly int CloudTypeParamsId = Shader.PropertyToID("_FishCloudTypeParams");
		private static readonly int CloudSunDirId = Shader.PropertyToID("_FishCloudSunDir");
		private static readonly int CloudSunColorId = Shader.PropertyToID("_FishCloudSunColor");
		private static readonly int CloudAmbientId = Shader.PropertyToID("_FishCloudAmbient");
		private static readonly int CloudHazeId = Shader.PropertyToID("_FishCloudHaze");
		private static readonly int CloudLayerAId = Shader.PropertyToID("_FishCloudLayerA");
		private static readonly int CloudLayerBId = Shader.PropertyToID("_FishCloudLayerB");
		private static readonly int CloudLayerCId = Shader.PropertyToID("_FishCloudLayerC");
		private static readonly int CloudLayerDId = Shader.PropertyToID("_FishCloudLayerD");
		private static readonly int CloudLayerTintId = Shader.PropertyToID("_FishCloudLayerTint");
		private static readonly int CloudLayerCountId = Shader.PropertyToID("_FishCloudLayerCount");
		private static readonly int CloudTintId = Shader.PropertyToID("_FishCloudTint");
		private static readonly int CloudCoverageId = Shader.PropertyToID("_FishCloudCoverage");
		private static readonly int CloudShapeTexId = Shader.PropertyToID("_FishCloudShape");
		private static readonly int CloudDetailTexId = Shader.PropertyToID("_FishCloudDetail");
		private static readonly int AuroraParamsId = Shader.PropertyToID("_FishAuroraParams");
		private static readonly int AuroraAId = Shader.PropertyToID("_FishAuroraA");
		private static readonly int AuroraBId = Shader.PropertyToID("_FishAuroraB");
		private static readonly int RainbowId = Shader.PropertyToID("_FishRainbow");
		private static readonly int BodyTexId = Shader.PropertyToID("_BodyTex");
		private static readonly int UseTextureId = Shader.PropertyToID("_UseTexture");

		private static SkySystem instance;
		public static SkySystem Instance => instance;

		/// <summary>
		/// True when the volumetric clouds can be drawn: a sky is bound, the volumes are baked and
		/// the globals have been set at least once. The renderer feature asks before doing anything.
		/// </summary>
		/// <remarks>
		/// False on a body with no air, whatever else is ready: the cloud pass asks this before it
		/// does anything, so an airless moon pays for no march at all and cannot show a cloud.
		/// </remarks>
		public static bool CloudsReady => instance != null && instance.cloudsReady && !instance.airless;

		/// <summary>The body stood on has no atmosphere: no cloud, no weather, nothing in the sky but the sky.</summary>
		private bool airless;

		/// <summary>How much air the body stood on has.</summary>
		private AtmosphereKind atmosphere = AtmosphereKind.Standard;

		/// <summary>What the clouds cost on the quality level being drawn.</summary>
		public CloudTierSettings CloudTier { get; private set; }

		/// <summary>How far a cloud ray may travel, in metres.</summary>
		public float CloudFarDistance { get; private set; } = 90000f;

		/// <summary>Half way up the cloud layer: where a screen point is reprojected against.</summary>
		public float CloudLayerCentre { get; private set; } = 3000f;


		/// <summary>Where the light shafts come from: a direction into the sky, or zero for none.</summary>
		public Vector3 GodRayDirection { get; private set; }

		/// <summary>The colour of those shafts.</summary>
		public Color GodRayColor { get; private set; } = Color.white;

		/// <summary>How strong they are; zero when the tier or the sky has no use for them.</summary>
		public float GodRayIntensity { get; private set; }

		/// <summary>Above zero while the shafts come from around an eclipsing body.</summary>
		public float GodRayEclipse { get; private set; }

		/// <summary>
		/// How much there is in the air for a shaft to light, 0..1. A shaft is sunlight scattered
		/// toward the eye by haze, mist and rain; in clean dry air there is almost nothing to see.
		/// </summary>
		public float GodRayMedium { get; private set; }

		/// <summary>The least the air ever carries, so a clean day still shows a faint shaft.</summary>
		private const float GodRayMediumFloor = 0.25f;

		/// <summary>
		/// Turns post-processing on for a camera, which is what runs the tonemapper. The test beds
		/// build their cameras from code and have no reference to URP of their own.
		/// </summary>
		public static void EnablePostProcessing(Camera camera, bool on = true)
		{
			if (camera == null)
			{
				return;
			}
			var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera);
			if (data != null)
			{
				data.renderPostProcessing = on;
			}
		}

		/// <summary>
		/// Held false, the shafts are not drawn even where the sky asks for them. The probe uses
		/// this to render the same frame with and without them and measure the difference.
		/// </summary>
		public static bool DrawGodRays = true;

		/// <summary>
		/// Held false, the clouds throw no shadow on the world even where the tier allows one. The
		/// sim panels switch it; nothing in the game does.
		/// </summary>
		public static bool DrawCloudShadows = true;

		/// <summary>An explicit render profile (the test scene); otherwise the loaded one.</summary>
		public WeatherRenderProfile Profile;
		/// <summary>The camera to present for; otherwise <see cref="Camera.main"/>.</summary>
		public Camera TargetCamera;

		private WorldDayNightCycle cycle;
		private Material skyMaterial;
		private Material bodyMaterial;
		private SkyProfile defaultSky;
		private SkyProfile regionSky;
		private bool hasRegionSky;
		private SkyProfile blendFrom;
		private SkyProfile blendTo;

		/// <summary>The sky profile being drawn, for a panel that wants to tune it.</summary>
		public SkyProfile ActiveSky => blendTo;
		private float blendElapsed;
		private float blendSeconds;
		private Cubemap stars;
		private int starSize;
		/// <summary>The body in front of the sun, by the discs as drawn. Null when nothing is.</summary>
		private CelestialBody drawnEclipsingBody;
		private uint starSeed;
		private Light sun;
		private Light moon;
		private Light createdSun;
		private Light createdMoon;

		/// <summary>One of the sky's own directional lights, made once and kept under the sky system.</summary>
		private Light OwnLight(ref Light light, string name)
		{
			if (light == null)
			{
				var holder = new GameObject(name);
				holder.transform.SetParent(transform, false);
				light = holder.AddComponent<Light>();
				light.type = LightType.Directional;
				// Dark until the sky says otherwise: it sets colour, intensity and which of the two
				// casts the shadows on every frame it runs.
				light.intensity = 0f;
				light.shadows = LightShadows.None;
			}
			light.enabled = true;
			return light;
		}
		private readonly List<Light> companions = new List<Light>();
		private readonly List<Light> companionMoons = new List<Light>();
		private readonly List<(int index, float light)> moonOrder = new List<(int index, float light)>();

		/// <summary>As many moons as light the ground at once. A forward renderer counts every extra light per object.</summary>
		private const int MaxMoonLights = 4;

		/// <summary>Our own moon's angular radius, in radians: the size the profile's moon intensity is authored for.</summary>
		private const float ReferenceMoonRadius = 0.0045f;

		/// <summary>What a star sends here, in arbitrary units: its luminosity over the square of its distance.</summary>
		private static float Flux(in SkyBodyState star)
		{
			float luminosity = star.Body is StarBody body ? Mathf.Max(0f, body.Luminosity) : 1f;
			double distance = System.Math.Max(1.0, star.DistanceKm);
			return (float)(luminosity / (distance / 1.0e8 * (distance / 1.0e8)));
		}

		/// <summary>
		/// What a moon sends down, before the weather takes its share: how much of it is lit, how much
		/// of that the world's own shadow has taken, how high it stands, and its size against our own
		/// moon's — twice as wide is four times the light.
		/// </summary>
		private static float MoonLight(in SkySample sample, in SkyBodyState body, float rampDegrees)
		{
			float up = Mathf.Clamp01(body.AltitudeDegrees / rampDegrees + 0.3f);
			float size = Mathf.Clamp(body.AngularRadius / ReferenceMoonRadius, 0f, 1.75f);
			return sample.MoonIntensity * body.Illumination * (1f - body.Shadowed * 0.9f) * up * size * size;
		}

		/// <summary>The moon giving the most light at this moment, or -1 when none is up.</summary>
		private static int BrightestMoon(CelestialState state, in SkySample sample, float rampDegrees, out float light)
		{
			int best = -1;
			light = 0f;
			for (int i = 0; i < state.Bodies.Count; i++)
			{
				SkyBodyState body = state.Bodies[i];
				if (body.Kind != SkyBodyKind.Moon || body.AltitudeDegrees <= -2f)
				{
					continue;
				}
				float sends = MoonLight(sample, body, rampDegrees);
				if (sends > light)
				{
					light = sends;
					best = i;
				}
			}
			return best;
		}

		/// <summary>The n-th light of a pool of the sky's own, made when first wanted.</summary>
		private Light Pooled(List<Light> pool, int index, string name)
		{
			while (pool.Count <= index)
			{
				var holder = new GameObject(name);
				holder.transform.SetParent(transform, false);
				Light light = holder.AddComponent<Light>();
				light.type = LightType.Directional;
				light.shadows = LightShadows.None;
				light.intensity = 0f;
				pool.Add(light);
			}
			return pool[index];
		}
		private ReflectionProbe probe;
		private float probeTimer;
		private Vector3 probeSun;
		private Color lastAmbientSky, lastAmbientEquator, lastAmbientGround;
		private float bodyTimer;
		private float mapTimer;
		private bool warnedCamera;
		private SkyBodyMesh bodies;
		private LightningPresenter lightning;
		private CurtainPresenter curtains;
		private CloudShadowPresenter cloudShadows;
		private CloudTerrainMap cloudTerrain;
		private WeatherMap weatherMap;
		private readonly Vector4[] layerA = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerB = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerC = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerD = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerTint = new Vector4[MaxCloudLayers];

		/// <summary>
		/// Draws every cloud as a wire box in the Scene and Game views (gizmos on). The test beds
		/// switch it; nothing in the game does.
		/// </summary>
		public static bool DrawCloudGizmos;
		private readonly List<Meteor> meteors = new List<Meteor>();
		private double meteorsUntil = double.NaN;
		private readonly Vector4[] sunDirections = new Vector4[MaxSuns];
		private readonly Vector4[] sunColors = new Vector4[MaxSuns];
		private SkySample current;
		private double worldSeconds;

		/// <summary>The world clock the sky schedules against, in seconds. Read by the test beds.</summary>
		public double WorldSeconds => worldSeconds;

		// Made in Awake: Unity refuses these inside a behaviour's constructor.
		private MaterialPropertyBlock block;

		public SkySample Current => current;
		public CelestialState State => cycle != null ? cycle.State : null;
		public LightningPresenter Lightning => lightning;
		public CurtainPresenter Curtains => curtains;
		public SkyBodyMesh Bodies => bodies;
		public WeatherMap Map => weatherMap;

		/// <summary>The most bands the shader takes.</summary>
		public const int MaxCloudLayers = 6;

		/// <summary>The bands the sky is made of, as the renderer last saw them.</summary>
		public IReadOnlyList<CloudLayer> CloudBands => cloudSettings != null ? cloudSettings.Layers : null;

		/// <summary>How much of each band the weather is asking for, in band order.</summary>
		public IReadOnlyList<float> CloudBandCoverage => layerCoverage;

		/// <summary>
		/// Where each band's floor ended up, in band order and in metres. A band that follows the
		/// condensation level is not where it was authored, so this is what the sky is drawn with.
		/// </summary>
		public IReadOnlyList<float> CloudBandBottom => layerBottom;

		/// <summary>
		/// The state the bands were last built from: what the forecast asked for, and what the wind
		/// and the sun were doing when it did. Read by the sim panels' statistics, which would
		/// otherwise have to guess at numbers the renderer already knows.
		/// </summary>
		public float CloudCover => cloudBackgroundCover;
		/// <summary>How much storm the sky is under, 0..1: what fills the bands that grow storms.</summary>
		public float CloudStorm => cloudBackgroundStorm;
		/// <summary>How much precipitation the sky is under, 0..1: what thickens the bands that carry rain.</summary>
		public float CloudPrecipitation => cloudPrecipitation;
		/// <summary>The wind the bands drift on: a unit direction on the ground plane.</summary>
		public Vector2 CloudWind => cloudWind;

		/// <summary>
		/// Held true, the clouds take their wind from <see cref="CloudWindHeading"/> and
		/// <see cref="CloudWindSpeed"/> instead of from the weather. The sim panels switch it so a
		/// designer can point the sky where they want it; nothing in the game does.
		/// </summary>
		public static bool CloudWindOverride;
		/// <summary>The direction that wind blows toward, in degrees clockwise from north.</summary>
		public static float CloudWindHeadingOverride = 60f;
		/// <summary>How fast it blows, in metres per second.</summary>
		public static float CloudWindSpeedOverride = 8f;
		/// <summary>How fast that wind runs at the ground, in metres per second. A band scales it.</summary>
		public float CloudWindSpeed => cloudWindSpeed;
		/// <summary>The direction the cloud light comes from.</summary>
		public Vector3 CloudSunDirection => cloudSunDirection;
		public Light Sun => sun;
		public Light Moon => moon;

		public static SkySystem Ensure(GameObject host)
		{
			if (instance != null)
			{
				return instance;
			}
			return host.AddComponent<SkySystem>();
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
			ChangeSkyProfileAction.OnChangeSkyProfile += OnChangeSkyProfile;
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		}

		/// <summary>
		/// Builds the parts that are not serialized. Also called from Update, because a domain
		/// reload (a script edited while playing) empties them without running Awake again.
		/// </summary>
		private void EnsureParts()
		{
			block = block ?? new MaterialPropertyBlock();
			bodies = bodies ?? new SkyBodyMesh();
			lightning = lightning ?? new LightningPresenter();
			curtains = curtains ?? new CurtainPresenter();
			cloudShadows = cloudShadows ?? new CloudShadowPresenter();
			weatherMap = weatherMap ?? new WeatherMap();
			if (defaultSky == null)
			{
				defaultSky = ScriptableObject.CreateInstance<SkyProfile>();
				defaultSky.hideFlags = HideFlags.DontSave;
				defaultSky.name = "Default Sky";
			}
		}

		private void OnDestroy()
		{
			if (instance != this)
			{
				return;
			}
			ChangeSkyProfileAction.OnChangeSkyProfile -= OnChangeSkyProfile;
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			bodies?.Dispose();
			foreach (SkyBodyMesh mesh in texturedMeshes)
			{
				mesh.Dispose();
			}
			lightning?.Dispose();
			curtains?.Dispose();
			cloudShadows?.Dispose();
			cloudTerrain?.Dispose();
			cloudTerrain = null;
			weatherMap?.Dispose();
			DestroyOwned(skyMaterial);
			DestroyOwned(bodyMaterial);
			DestroyOwned(defaultSky);
			DestroyOwned(stars);
			instance = null;
		}

		private static void DestroyOwned(Object obj)
		{
			if (obj != null)
			{
				Destroy(obj);
			}
		}

		private void OnChangeSkyProfile(SkyProfile profile, float seconds)
		{
			regionSky = profile;
			hasRegionSky = profile != null;
			if (seconds <= 0f)
			{
				blendFrom = null;
			}
			else
			{
				blendSeconds = seconds;
			}
		}

		private WeatherRenderProfile RenderProfile()
		{
			if (Profile == null)
			{
				Profile = WeatherPresentation.Instance != null && WeatherPresentation.Instance.Profile != null ? WeatherPresentation.Instance.Profile : WeatherRenderProfile.Active;
			}
			return Profile;
		}

		private SkyProfile TargetSky(CelestialState state)
		{
			if (hasRegionSky && regionSky != null)
			{
				return regionSky;
			}
			if (cycle != null && cycle.SkyOverride != null)
			{
				return cycle.SkyOverride;
			}
			WorldBody body = state.Observer;
			if (body != null && body.Sky != null)
			{
				return body.Sky;
			}
			// One default for every body. Whether there is air, how much and what is in it are the
			// body's, and the sample is worked out from them; a profile is only how it is presented.
			return defaultSky;
		}

		private void Update()
		{
			EnsureParts();
			WeatherRenderProfile profile = RenderProfile();
			WorldDayNightCycle next = WorldDayNightCycle.Current;
			if (profile == null || profile.SkyMaterial == null || next == null || !next.isActiveAndEnabled)
			{
				if (cycle != null)
				{
					Unbind();
				}
				return;
			}
			if (next != cycle)
			{
				Bind(next, profile);
			}

			CelestialState state = cycle.State;
			float dt = Time.deltaTime;
			worldSeconds = cycle.ClockHours * 3600.0;
			Camera camera = TargetCamera != null ? TargetCamera : Camera.main;
			WeatherTierSettings tier = profile.TierFor(QualitySettings.GetQualityLevel());

			WeatherPresentation presentation = WeatherPresentation.Instance;
			WeatherFrame weather = presentation != null ? presentation.Shown : WeatherFrame.Clear;
			WeatherContext context = presentation != null ? presentation.Context : default;
			// No air, no weather. The weather field already says so for a scene on an airless body;
			// the sky did not ask, and drew the driver's cloud over a moon all the same. Said here as
			// well as there, so nothing that hands the sky a frame — a preset, the bed's sliders —
			// can rain on a world with nothing to rain out of.
			atmosphere = state != null && state.Observer != null ? state.Observer.Atmosphere : AtmosphereKind.Standard;
			airless = atmosphere == AtmosphereKind.None;
			if (airless)
			{
				weather = WeatherFrame.Clear;
				context.Background = WeatherFrame.Clear;
			}
			WeatherTimeline timeline = presentation != null && presentation.HasContext ? context.Timeline : null;
			uint tick = presentation != null && presentation.HasContext ? (uint)context.Tick : 0u;

			// The profile, blended when it changes.
			SkyProfile target = TargetSky(state);
			if (blendTo != target)
			{
				blendFrom = blendTo;
				blendTo = target;
				blendElapsed = 0f;
			}
			float sunAltitude = state.SunAltitude;
			SkySample sample = blendTo.Evaluate(sunAltitude, state.Observer);
			if (blendFrom != null && blendSeconds > 0f && blendElapsed < blendSeconds)
			{
				blendElapsed += dt;
				sample = SkySample.Lerp(blendFrom.Evaluate(sunAltitude, state.Observer), sample, Mathf.SmoothStep(0f, 1f, blendElapsed / blendSeconds));
			}
			// The profile is authored for the reference sun; the suns that are up recolour it.
			baseSunLight = sample.SunLight;
			sample = TintBySuns(state, sample, out primaryTint);
			current = sample;

			float overcast = Mathf.Clamp01(weather[WeatherChannel.CloudCover] * (0.4f + 0.6f * weather[WeatherChannel.CloudDensity]));
			// The eclipse of the discs as they are drawn. Life-size that is the true one; larger than
			// life the discs meet sooner and part later, and the sky has to go dark with what it shows.
			float eclipse = state.SolarEclipseAsDrawn(blendTo != null ? blendTo.SunScale : 1f, blendTo != null ? blendTo.BodyScale : 1f, out drawnEclipsingBody);

			// Lightning first: its flash reaches the sky, the lights and the weather globals.
			Vector3 viewer = camera != null ? camera.transform.position : Vector3.zero;
			// With the lightning of the weather here — the field's storms and heavy rain — and not only
			// the scene layers' and the cells'.
			lightning.Update(timeline, tick, worldSeconds, viewer, camera, profile.BoltMaterial, context.Background[WeatherChannel.LightningRate]);
			if (presentation != null)
			{
				presentation.LightningFlash = lightning.Flash;
			}

			SetGodRays(state, sample, weather, overcast, eclipse, tier, context);
			SetSkyGlobals(state, sample, blendTo, weather, overcast, eclipse, context, tier, profile);
			ApplyLights(state, sample, overcast, eclipse, weather, dt, profile, tier);
			ApplyAmbient(sample, overcast, eclipse, lightning.Flash);
			FogComposer.SetSkyColor(Color.Lerp(sample.Fog, sample.Fog * 0.7f + new Color(0.25f, 0.26f, 0.28f) * 0.3f, overcast));
			UpdateReflection(state, dt, tier);
			CheckCamera(camera);

			mapTimer -= dt;
			if (mapTimer <= 0f && camera != null)
			{
				mapTimer = WeatherMapRefreshSeconds;
				weatherMap.Build(timeline, viewer, tick);
			}

			bodyTimer -= dt;
			if (bodyTimer <= 0f)
			{
				bodyTimer = BodyRefreshSeconds;
			}
			// Meteors are scheduled a few seconds ahead and drawn every frame.
			if (double.IsNaN(meteorsUntil) || worldSeconds > meteorsUntil || worldSeconds < meteorsUntil - 10.0)
			{
				meteors.RemoveAll(m => m.Time + m.Duration < worldSeconds);
				double from = double.IsNaN(meteorsUntil) || worldSeconds < meteorsUntil - 10.0 ? worldSeconds : meteorsUntil;
				meteorsUntil = worldSeconds + 4.0;
				if (sample.StarVisibility > 0.2f)
				{
					SkySchedule.Meteors(state, from, meteorsUntil, profile.TierFor(QualitySettings.GetQualityLevel()).MeteorBudget, meteors);
				}
			}
			// In the order they are drawn, furthest first: a belt's specks, then the bodies by their own
			// distances, then the meteors, which burn in this world's air and are nearer than anything.
			bodies.Clear();
			if (tier.Asteroids)
			{
				bodies.AddAsteroids(state, state.System != null ? state.System.Limits : new SkyLimits());
			}
			bodies.AddBodies(state, blendTo, state.System != null ? state.System.Limits : new SkyLimits());
			bodies.AddMeteors(meteors, worldSeconds);
			bodies.Upload();
			UploadOccluders();
		}

		private void Bind(WorldDayNightCycle next, WeatherRenderProfile profile)
		{
			if (cycle != null)
			{
				Unbind();
			}
			cycle = next;
			if (skyMaterial == null)
			{
				skyMaterial = new Material(profile.SkyMaterial) { name = "Sky (runtime)", hideFlags = HideFlags.DontSave };
			}
			if (bodyMaterial == null && profile.SkyBodyMaterial != null)
			{
				bodyMaterial = new Material(profile.SkyBodyMaterial) { name = "Sky Bodies (runtime)", hideFlags = HideFlags.DontSave };
			}
			RenderSettings.skybox = skyMaterial;

			// The sky's lights are the sky's own. Where the sun and the moon stand, what colour they
			// are and how bright is worked out from the solar system every frame, so there is nothing
			// for a scene to author in them. They used to be handed over by the scene's day/night
			// cycle — a Sun and a Moon placed in every scene, found by their names if not assigned —
			// which made the lighting depend on scene objects that did nothing but wait to be driven,
			// and left a scene without them with no moonlight at all, since only a sun was ever made.
			sun = OwnLight(ref createdSun, "Sky Sun");
			moon = OwnLight(ref createdMoon, "Sky Moon");
			RenderSettings.sun = sun;
			RenderSettings.ambientMode = AmbientMode.Trilight;
			lastAmbientSky = new Color(-1f, 0f, 0f);
			probeTimer = 0f;
			blendFrom = null;
			blendTo = null;
			hasRegionSky = false;
			regionSky = null;
			meteors.Clear();
			meteorsUntil = double.NaN;
			lightning.Reset();
			warnedCamera = false;
		}

		private void Unbind()
		{
			cloudShadows.Clear(sun);
			foreach (Light companion in companions)
			{
				if (companion != null)
				{
					companion.enabled = false;
				}
			}
			foreach (Light companion in companionMoons)
			{
				if (companion != null)
				{
					companion.enabled = false;
				}
			}
			if (createdMoon != null)
			{
				createdMoon.enabled = false;
			}
			if (createdSun != null)
			{
				createdSun.enabled = false;
			}
			cycle = null;
			sun = null;
			moon = null;
			weatherMap.Publish(false);
		}

		private void SetSkyGlobals(CelestialState state, in SkySample sample, SkyProfile sky, in WeatherFrame weather, float overcast, float eclipse, in WeatherContext context, WeatherTierSettings tier, WeatherRenderProfile profile)
		{
			// The system's stars: one sky for every world and moon in it. Rebuilt when the system
			// changes as well as when the tier does, or a second system would wear the first one's sky.
			uint seed = state != null && state.System != null ? state.System.StarSeed : 238u;
			if (stars == null || starSize != tier.StarCubemapSize || starSeed != seed)
			{
				DestroyOwned(stars);
				starSize = tier.StarCubemapSize;
				starSeed = seed;
				stars = StarfieldBuilder.Build(starSize, seed);
			}
			// From the body, not from the profile: a profile that forgot to say so gave an airless
			// moon a blue noon.
			float airless = state.Observer != null && !state.Observer.HasWeather ? 1f : 0f;
			float fogBlend = Mathf.Clamp01((RenderSettings.fog ? 0.45f : 0f) + WeatherFogPresenter.Amount(weather) * 0.55f) * (1f - airless);
			Shader.SetGlobalVector(ZenithId, sample.Zenith);
			Shader.SetGlobalVector(HorizonId, sample.Horizon);
			Shader.SetGlobalVector(GroundId, sample.Ground);
			Color fog = RenderSettings.fogColor;
			fog.a = fogBlend;
			Shader.SetGlobalVector(FogColorId, fog);
			float starVisibility = sample.StarVisibility * (1f - overcast * 0.9f);
			Shader.SetGlobalVector(ParamsId, new Vector4(starVisibility * sky.StarBrightness, sky.MilkyWay, sky.StarTwinkle, sky.Exposure));
			// How bright the sun is, in its three parts: the halo the air scatters at you, the disc
			// itself, and the glow along the horizon toward a low sun. They were three constants in
			// the shader, which is no use to anyone judging the sky by eye.
			Shader.SetGlobalVector(SunShapeId, new Vector4(0f, sky.SunDisc, sky.SunGlow, 0f));
			// Where the covering body is and how big it looks: the corona is drawn at its limb.
			var covering = Vector4.zero;
			if (eclipse > 0.02f)
			{
				for (int i = 0; i < state.Bodies.Count; i++)
				{
					if (state.Bodies[i].Body == drawnEclipsingBody)
					{
						Vector3 direction = state.Bodies[i].Direction;
						covering = new Vector4(direction.x, direction.y, direction.z, state.Bodies[i].AngularRadius * sky.BodyScale);
						break;
					}
				}
			}
			Shader.SetGlobalVector(EclipseBodyId, covering);
			Shader.SetGlobalVector(EclipseId, new Vector4(eclipse, state.LunarEclipse, airless, 0f));
			// The system's frame and not the body's own: see CelestialState.StarsToScene. The galaxy
			// below is read through the same matrix, so it keeps its place among the stars.
			Shader.SetGlobalMatrix(StarMatrixId, state.StarsToScene.transpose);
			SetOwnRing(state);
			Shader.SetGlobalTexture(StarCubeId, stars);
			// A galaxy of the sky's own, if it has one. It sits in the same frame as the stars, so it
			// turns with them; with none supplied the shader draws its own band instead. The flag is
			// what chooses, and an unbound cubemap must never be sampled — it reads as black and
			// would put a dark patch across the night sky where the galaxy should be.
			Shader.SetGlobalTexture(GalaxyCubeId, sky.Galaxy != null ? (Texture)sky.Galaxy : stars);
			Shader.SetGlobalVector(GalaxyParamsId, new Vector4(sky.Galaxy != null ? 1f : 0f, 0f, 0f, 0f));
			if (profile.Noise != null)
			{
				Shader.SetGlobalTexture(NoiseId, profile.Noise);
			}

			int count = 0;
			if (state.Sun >= 0)
			{
				AddSun(state.Bodies[state.Sun], sample, sky, eclipse, ref count);
			}
			for (int i = 0; i < state.Bodies.Count && count < MaxSuns; i++)
			{
				if (i != state.Sun && state.Bodies[i].Kind == SkyBodyKind.Star)
				{
					AddSun(state.Bodies[i], sample, sky, 0f, ref count);
				}
			}
			if (count == 0)
			{
				// No solar system: a default sun overhead-ish so the sky is still lit.
				sunDirections[0] = new Vector4(0.3f, 0.8f, 0.5f, 0.0047f * sky.SunScale);
				sunColors[0] = new Vector4(1f, 0.97f, 0.9f, sky.SunHalo);
				count = 1;
			}
			for (int i = count; i < MaxSuns; i++)
			{
				sunDirections[i] = Vector4.zero;
				sunColors[i] = Vector4.zero;
			}
			Shader.SetGlobalVectorArray(SunDirId, sunDirections);
			Shader.SetGlobalVectorArray(SunColorId, sunColors);
			Shader.SetGlobalFloat(SunCountId, count);

			Shader.SetGlobalVector(CloudLitId, sample.CloudLit);
			Shader.SetGlobalVector(CloudShadowId, sample.CloudShadow);

			// The far sky follows the background forecast; the weather map draws the cells onto it.
			// A context that carries no background at all (an adapter, an old caller) falls back to
			// the weather at the viewer, which is what the sky used before there was a difference.
			WeatherFrame background = context.Background[WeatherChannel.CloudCover] > 0f
				|| context.Background[WeatherChannel.CloudDensity] > 0f
				? context.Background
				: weather;
			SetCloudGlobals(state, sample, weather, background, profile, tier, context);

			// Dark and clear. WHERE an aurora stands is the aurora's own business now — a ring round each
			// magnetic pole that a storm widens toward the equator — and the profile's flat "not below
			// this latitude" laid over that cut a great storm's display off in the low forties, exactly
			// where it is rarest and most worth seeing. Nor "cold": an aurora
			// is a hundred kilometres up and has nothing to do with the ground's temperature. That gate
			// dated from when the only aurora was one a preset asked for, in scenes made cold for it; it
			// would have put out a storm's aurora over any mild country it reached.
			float aurora = weather[WeatherChannel.Aurora] * sample.StarVisibility * (1f - overcast);
			Shader.SetGlobalVector(AuroraParamsId, new Vector4(aurora, (float)(worldSeconds % 100000.0), 0f, 0f));
			Shader.SetGlobalVector(AuroraAId, sky.AuroraA);
			Shader.SetGlobalVector(AuroraBId, sky.AuroraB);

			// Rainbow: rain falling while the sun is low and not hidden.
			float rain = weather[WeatherChannel.Precipitation] * weather[WeatherChannel.RainWeight];
			float low = state.Sun >= 0 ? Mathf.Clamp01(1f - Mathf.Abs(state.SunAltitude - 20f) / 22f) : 0f;
			float rainbow = Mathf.Clamp01(rain * 4f) * Mathf.Clamp01(1.4f - rain * 1.5f) * low * (1f - overcast * 0.85f) * (1f - airless);
			Shader.SetGlobalVector(RainbowId, new Vector4(rainbow, 0f, 0f, 0f));
		}

		private bool cloudsReady;
		private Color baseSunLight = Color.white;
		private Color primaryTint = Color.white;

		/// <summary>
		/// Recolours a sample by the suns in the sky. Sky, fog, ambient and clouds take the hue of
		/// the suns that are up, weighted by brightness and height, and fade back to the profile's
		/// own colours as night falls. Sunlight takes the primary sun's hue; moonlight, being
		/// reflected sunlight, takes it too.
		/// </summary>
		public static SkySample TintBySuns(CelestialState state, SkySample sample, out Color primary)
		{
			primary = Color.white;
			Color sum = Color.black;
			float weight = 0f;
			if (state != null)
			{
				for (int i = 0; i < state.Bodies.Count; i++)
				{
					if (state.Bodies[i].Kind != SkyBodyKind.Star || !(state.Bodies[i].Body is StarBody star))
					{
						continue;
					}
					Color tint = star.SkyTint;
					if (i == state.Sun)
					{
						primary = tint;
					}
					float w = Mathf.Max(0f, star.Luminosity) * Mathf.Clamp01(state.Bodies[i].AltitudeDegrees / 10f + 0.3f);
					sum += tint * w;
					weight += w;
				}
			}
			Color sky = weight > 1e-4f ? sum / weight : primary;
			sky.a = 1f;
			Color day = Color.Lerp(sky, Color.white, sample.StarVisibility);
			sample.Zenith = Mul(sample.Zenith, day);
			sample.Horizon = Mul(sample.Horizon, day);
			sample.Ground = Mul(sample.Ground, day);
			sample.Fog = Mul(sample.Fog, day);
			sample.AmbientSky = Mul(sample.AmbientSky, day);
			sample.AmbientEquator = Mul(sample.AmbientEquator, day);
			sample.AmbientGround = Mul(sample.AmbientGround, day);
			sample.CloudLit = Mul(sample.CloudLit, day);
			sample.CloudShadow = Mul(sample.CloudShadow, day);
			sample.SunLight = Mul(sample.SunLight, primary);
			sample.MoonLight = Mul(sample.MoonLight, primary);
			return sample;
		}

		private static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);

		/// <summary>
		/// Hands the cloud volume everything it needs: where the layer sits, what it is carved from,
		/// which way the wind blows it, how it is lit, and how much cloud the weather asks for.
		/// </summary>
		/// <summary>
		/// Hands the bands to the shader: where each one sits, how much of it the weather is asking
		/// for, and what it is made of.
		/// </summary>
		/// <remarks>
		/// The forecast is one number for the whole sky, and each band decides what that means for
		/// it: the deck follows it directly, a sheet only arrives once a front has closed the sky
		/// over, and cirrus thins as everything below thickens. A band can also be told to ignore
		/// the forecast entirely, which is how the weather band at ground level stays empty until
		/// there is fog to put in it.
		/// </remarks>
		private void SetCloudLayers(WeatherRenderProfile profile, in WeatherFrame background, bool driverActive, float driverWeight, Vector2 axis, double driftX, double driftY)
		{
			VolumetricCloudSettings clouds = profile.Clouds;
			List<CloudLayer> bands = clouds.Layers;
			if (bands == null || bands.Count == 0)
			{
				Shader.SetGlobalInt(CloudLayerCountId, 0);
				return;
			}

			float cover = cloudBackgroundCover;
			float fog = Mathf.Clamp01(background[WeatherChannel.FogDensity]);
			CelestialState skyState = State;
			float latitude = skyState != null ? (float)skyState.Latitude : 0f;
			SolarSystemProfile solar = SolarSystemProfile.Active;
			// The same season the weather field works out, by the same function: the clouds drawn and
			// the weather sampled must never disagree about what time of year it is.
			float season01 = CelestialMath.Season01(solar, skyState != null ? skyState.Observer : null, worldSeconds / 3600.0);
			Camera viewCamera = TargetCamera != null ? TargetCamera : Camera.main;
			Vector3 viewerAt = viewCamera != null ? viewCamera.transform.position : transform.position;

			// The formations: where in this sky the banks and the gaps are. The shader computes the
			// same term per sample from the same seed; what it needs from here is the drift the field
			// is read through, wrapped to the lattice's own period so a float holds it, and the
			// term's value at the camera — the cover the bands are given below was measured there
			// and already contains it, so the shader takes it back out before adding its own.
			// With the sliders or a pinned preset deciding the weather there are no formations: the
			// sky is exactly what was asked for, which is what the probe's cloud stages check.
			float mesoAtCamera = 0f;
			float mesoContrast = 0f;
			float mesoAmplitude = 0f;
			// The cloud map's tall part: how far up the columns of this sky get. What the day starts
			// every column at comes from the air when the field decides the weather and from the
			// weather's own density channel when a preset or the sliders do — that channel has always
			// been described as "flat stratus to heaped cumulus" and was uploaded for exactly this,
			// and nothing in the shader ever read it. The towers belong to the field alone.
			CloudTowerAtCamera = 0f;
			// How hard the air resists being lifted, as the buoyancy frequency N (per second): about
			// 0.02 for a very stable layer, 0.004 for unsettled air. With no field to say, ordinary.
			float stability = 0.01f;
			float presetType = Mathf.Clamp01(background[WeatherChannel.CloudDensity]) * 0.5f;
			float columnType = presetType;
			float towerGain = 0f;
			double mesoPeriod = WeatherDriver.MesoscaleMetres * WeatherDriver.MesoscalePeriodTiles;
			double mesoDriftX = driftX - System.Math.Floor(driftX / mesoPeriod) * mesoPeriod;
			double mesoDriftY = driftY - System.Math.Floor(driftY / mesoPeriod) * mesoPeriod;
			if (driverActive)
			{
				WeatherDriver.Synoptic air = WeatherDriver.Sample(WeatherDriver.WorldSeed,
					new Vector2(viewerAt.x, viewerAt.z), worldSeconds, latitude, season01, (float)(skyState != null ? skyState.LocalTime01 : 0.5));
				mesoAmplitude = WeatherDriver.MesoscaleAmplitude * driverWeight * WeatherDriver.FormationScale(atmosphere);
				mesoAtCamera = air.Mesoscale * mesoAmplitude;
				mesoContrast = WeatherDriver.MesoscaleContrast(air.Instability);
				columnType = Mathf.Lerp(presetType, WeatherDriver.BaseColumnType(air.Instability), driverWeight);
				towerGain = WeatherDriver.TowerGain(air.Instability) * driverWeight;
				CloudTowerAtCamera = air.Tower;
				stability = Mathf.Lerp(0.02f, 0.004f, Mathf.Clamp01(air.Instability));
			}
			cloudMesoscaleAtCamera = mesoAtCamera;
			CloudColumnType = columnType;
			CloudTowerGain = towerGain;
			double towerPeriod = WeatherDriver.TowerMetres * WeatherDriver.TowerPeriodTiles;
			Shader.SetGlobalVector(CloudColumnId, new Vector4(towerGain, WeatherDriver.TowerMetres, WeatherDriver.TowerPeriodTiles, columnType));
			Shader.SetGlobalVector(CloudTowerDriftId, new Vector4(
				(float)WrapMetres(driftX, towerPeriod), (float)WrapMetres(driftY, towerPeriod), Mathf.Max(0f, clouds.Carve), 0f));
			Shader.SetGlobalInt(CloudTowerSeedId, unchecked((int)(WeatherDriver.WorldSeed ^ WeatherDriver.TowerSeedMix)));
			// What hangs below a column's base: the ground fog that rises into it, and the scud and
			// rain haze under a wet one.
			// From the frame the presentation is actually showing, which is eased, and not from the
			// forecast, which is not: read off the forecast the fog in the sky cut in and out the
			// instant a preset or a volume changed, while the fog on the ground eased in beside it.
			Shader.SetGlobalVector(CloudSubId, new Vector4(shownFog, shownRain, 0f, 0f));
			Shader.SetGlobalVector(CloudViewerId, new Vector4(viewerAt.x, viewerAt.y, viewerAt.z, 0f));
			// The ground the clouds pass over: cloud gathers on the windward side of high terrain and
			// clears in its lee, and the fog lies on the ground that is there. Rebuilt only when the
			// viewer has moved a few hundred metres.
			cloudTerrain ??= new CloudTerrainMap();
			cloudTerrain.Update(viewerAt);
			// Over, or round. The Froude number, U / (N h), says whether air meeting high ground
			// climbs it or is split by it; U/N is the sky's half of that — how high this air can
			// climb — and the shader sets it against the ground's height place by place. So a calm
			// stable morning (2 m/s, N 0.02: a hundred metres) goes round every hill there is, and
			// an unsettled windy afternoon (14 m/s, N 0.005: nearly three kilometres) pours over a
			// mountain. The second figure is how far a blocked flow is turned aside.
			CloudClimbHeight = Mathf.Max(1f, cloudWindSpeed) / Mathf.Max(0.001f, stability);
			// 250 m, not 1800. The lookup is moved by this times the slope across the wind, and a
			// displacement that changes faster than the distance it moves folds the field: at 1800 m
			// the fold factor over the bed's mountain worked out at 9.7 — anything over about a half
			// smears — and the sky overhead was concentric rings. What "going round" mostly looks
			// like is the other half of it anyway: the layer is not lifted, so the cloud banks
			// against the slopes at its own level and the summit stands clear.
			Shader.SetGlobalVector(CloudFlowId, new Vector4(CloudClimbHeight, 250f, 0f, 0f));
			Shader.SetGlobalVector(CloudMesoId, new Vector4((float)mesoDriftX, (float)mesoDriftY, mesoAtCamera, mesoContrast));
			Shader.SetGlobalVector(CloudMesoParamsId, new Vector4(mesoAmplitude, WeatherDriver.MesoscaleMetres, WeatherDriver.MesoscalePeriodTiles, 0f));
			Shader.SetGlobalInt(CloudMesoSeedId, unchecked((int)(WeatherDriver.WorldSeed ^ WeatherDriver.MesoscaleSeedMix)));
			// The drift in the frame the noise is read in: along the axis and across it.
			var across = new Vector2(-axis.y, axis.x);
			double driftAlong = driftX * axis.x + driftY * axis.y;
			double driftAcross = driftX * across.x + driftY * across.y;

			// Which way the cloud is thickening across the ground. A front is a gradient, and without
			// one the sky has a single cover everywhere: the cut drops all at once and cloud appears
			// in place across the whole sky instead of arriving from upwind. Measured either side of
			// the camera from the same field the weather comes from, so the sky thickens where the
			// weather says it is thickening.
			Vector2 coverGradient = Vector2.zero;
			// Only when the drifting field is what is actually deciding the weather. With the sky on
			// the panel's own sliders, or a scene pinned to a preset, the cover the renderer is given
			// has nothing to do with the field — so tilting it by the field's slope lays a front
			// across a sky that was never asked to have one, and the tilt is unrelated to anything
			// the viewer set.
			if (viewCamera != null && skyState != null && driverActive && clouds.CoverageTilt > 0.001f)
			{
				WeatherDriver.CoverField(WeatherDriver.WorldSeed, new Vector2(viewerAt.x, viewerAt.z), worldSeconds,
					latitude, season01, (float)skyState.LocalTime01,
					out float fieldCover, out coverGradient);
				// The gradient belongs to the field's own cover; scale it by however much of that
				// cover actually survived into the weather here, so a scene that damps the weather
				// down damps the slope with it.
				float scale = fieldCover > 0.01f ? Mathf.Clamp01(cover / fieldCover) : 1f;
				coverGradient *= scale * clouds.CoverageTilt * driverWeight;
			}
			float lowest = float.MaxValue, highest = 0f;
			int count = Mathf.Min(bands.Count, MaxCloudLayers);
			if (layerCoverage.Length < count)
			{
				layerCoverage = new float[count];
			}
			if (layerBottom.Length < count)
			{
				layerBottom = new float[count];
			}
			// How high air has to rise before it condenses, as the weather reports it: 1 is a high
			// base on a dry warm day, 0 a low one when the air is already damp.
			float condensation = Mathf.Clamp01(background[WeatherChannel.CloudBase]);
			for (int i = 0; i < count; i++)
			{
				CloudLayer band = bands[i];
				if (band == null)
				{
					continue;
				}
				// A band that sits on the ground is the weather's own: fog and mist fill it, not the
				// cloud forecast, or a fair day would have a cloud deck at eye level.
				bool ground = band.Bottom < 200f;
				float coverage = ground
					? Mathf.Clamp01(fog * band.CoverageScale + band.CoverageBias)
					: band.CoverageFor(cover);
				layerCoverage[i] = coverage;

				// Where this band actually sits. A cloud's base is the height at which air rising
				// off the ground has cooled to its dew point, and that height is not fixed: damp air
				// condenses low and dry warm air condenses high, which is why a rainy deck hangs
				// close over the hills and a fair-weather one rides well above them. The weather
				// carries that height, and a band that follows it slides to it with its own
				// thickness intact — the base moves, the depth does not.
				float bottom = band.Bottom;
				// The channel runs from about 0.8 on a dry fair day down to 0.3 under thick cloud.
				// A frame that never set it at all reads as zero, and zero here does not mean "the
				// lowest base there can be" — it means nobody said. Below the channel's own floor
				// the band stays where it was authored, or a forecast that mentions only how much
				// cloud there is would drag the deck down onto the hills.
				if (band.BaseFollowsCondensation && condensation >= CondensationFloor)
				{
					float dryness = Mathf.InverseLerp(0.3f, 0.8f, condensation);
					float wanted = band.Bottom * Mathf.Lerp(0.65f, 1.3f, dryness);
					// And with no cloud in the sky there is no condensation level to speak of, so
					// the authored height stands until there is something to put at it.
					bottom = Mathf.Lerp(band.Bottom, wanted, Mathf.Clamp01(cover / 0.15f));
				}
				float top = bottom + band.Thickness;
				layerBottom[i] = bottom;
				// A column is as deep as a tower and, most places, as thin as a deck. Its noise must
				// turn over on the scale of a cloud, not of the band — tied to seven kilometres of
				// band a deck would be one value floor to ceiling, the flat slab again — so its
				// vertical tile follows its horizontal one instead.
				float verticalScale = Mathf.Max(0.25f, band.VerticalScale);
				if (band.Column)
				{
					verticalScale = band.NoiseScale * 0.2f * verticalScale / Mathf.Max(1f, band.Thickness);
				}

				lowest = Mathf.Min(lowest, bottom);
				highest = Mathf.Max(highest, top);
				layerA[i] = new Vector4(bottom, top, coverage, band.Density);
				layerB[i] = new Vector4(band.NoiseScale, band.DetailScale, band.DetailStrength, Mathf.Max(1f, band.Stretch));
				// Rain in the units and 2 on top for a band that grows storms: the shader tests the 2,
				// so a band that merely carries rain can no longer trip the storm test at half rain.
				layerC[i] = new Vector4(band.WindScale, band.BaseSoftness, band.TopSoftness,
					(band.CarriesRain ? cloudPrecipitation : 0f) + (band.GrowsStorms ? 2f : 0f));
				// The band's own slope: how fast *this* band's coverage changes across the ground,
				// which is the field's slope put through the band's own response to it.
				float ahead = band.CoverageFor(Mathf.Clamp01(cover + 0.01f));
				float behind = band.CoverageFor(Mathf.Clamp01(cover - 0.01f));
				// Clamped: the clear-sky gate is steep just above nothing, and a formation put
				// through an unclamped slope there would swing a band from empty to full.
				float response = ground ? 0f : Mathf.Clamp((ahead - behind) / 0.02f, 0f, 2f);
				// The slope of the SKY's cover, the same for every band: each band now answers the
				// cover at a place with its own function, in the shader, rather than being handed the
				// slope of that function at the camera and told to extrapolate.
				Vector2 bandGradient = ground ? Vector2.zero : coverGradient;
				layerD[i] = new Vector4(band.Convection, bandGradient.x, bandGradient.y, verticalScale);
				// This band's drift, wrapped to its own period so the float is exact and the wrap is
				// invisible: 42 tiles along the wind (the warp's period, which the shape's and the
				// lift's divide) stretched by the band's own stretch, 42 across, and the detail
				// volume's single tile on the world axes. All in double until the last moment.
				double scale = band.WindScale;
				double alongPeriod = band.NoiseScale * Mathf.Max(1f, band.Stretch) * 42.0;
				double acrossPeriod = band.NoiseScale * 42.0;
				double detailPeriod = Mathf.Max(20f, band.DetailScale);
				layerE[i] = new Vector4(
					(float)WrapMetres(driftAlong * scale, alongPeriod),
					(float)WrapMetres(driftAcross * scale, acrossPeriod),
					(float)WrapMetres(driftX * scale, detailPeriod),
					(float)WrapMetres(driftY * scale, detailPeriod));
				// z marks a column; w is the thinnest its cloud gets — the deck, seven hundredths of
				// the band — which is what the march's stride must not be able to step over.
				layerF[i] = new Vector4(response, ground ? 0f : 1f, band.Column ? 1f : 0f, band.Column ? band.Thickness * 0.07f : 0f);
				layerG[i] = new Vector4(band.CoverageOnset, band.CoverageScale, band.CoverageBias, 0f);
				Color tint = band.ShadedTint;
				layerTint[i] = new Vector4(tint.r, tint.g, tint.b, 1f);
			}
			for (int i = count; i < MaxCloudLayers; i++)
			{
				layerA[i] = Vector4.zero;
				layerB[i] = Vector4.zero;
				layerC[i] = Vector4.zero;
				layerD[i] = Vector4.zero;
				layerE[i] = Vector4.zero;
				layerF[i] = Vector4.zero;
				layerG[i] = Vector4.zero;
				layerTint[i] = Vector4.one;
			}
			Shader.SetGlobalVectorArray(CloudLayerAId, layerA);
			Shader.SetGlobalVectorArray(CloudLayerBId, layerB);
			Shader.SetGlobalVectorArray(CloudLayerCId, layerC);
			Shader.SetGlobalVectorArray(CloudLayerDId, layerD);
			Shader.SetGlobalVectorArray(CloudLayerEId, layerE);
			Shader.SetGlobalVectorArray(CloudLayerFId, layerF);
			Shader.SetGlobalVectorArray(CloudLayerGId, layerG);
			Shader.SetGlobalVectorArray(CloudLayerTintId, layerTint);
			Shader.SetGlobalInt(CloudLayerCountId, count);

			// The shell the march runs through, and the deck a reprojection has to be right about.
			CloudShellBottom = lowest == float.MaxValue ? 0f : lowest;
			CloudShellTop = Mathf.Max(CloudShellBottom + 100f, highest);
			// The depth a reprojection is right about: the deck, which is where most cloud is. A
			// column's middle is kilometres above anything usually there.
			CloudLayerCentre = bands.Count > 1
				? (bands[1].Column ? bands[1].Bottom + 600f : (bands[1].Bottom + bands[1].Top) * 0.5f)
				: (CloudShellBottom + CloudShellTop) * 0.5f;
			Shader.SetGlobalVector(CloudLayerId, new Vector4(CloudShellBottom, CloudShellTop, clouds.CurvatureRadiusKm * 1000f, cover));
		}

		/// <summary>A drift reduced to one period, in double, so that the float it becomes is exact.</summary>
		private static double WrapMetres(double metres, double period)
		{
			return period <= 0.0 ? metres : metres - System.Math.Floor(metres / period) * period;
		}

		/// <summary>What every column starts from here: 0.1 a flat deck, 0.5 a heaped sky.</summary>
		public float CloudColumnType { get; private set; }
		/// <summary>0..1: how strongly a tower stands over the camera right now.</summary>
		public float CloudTowerAtCamera { get; private set; }
		/// <summary>How much of a tower the air allows on top of that, 0..0.6.</summary>
		public float CloudTowerGain { get; private set; }

		/// <summary>The formations' share of the cover at the camera, in cover units, for readouts.</summary>
		public float CloudMesoscaleAtCamera => cloudMesoscaleAtCamera;
		private float cloudMesoscaleAtCamera;
		private float shownFog;
		private float shownRain;

		/// <summary>The bottom and top of the whole stack of bands, in metres.</summary>
		public float CloudShellBottom { get; private set; }
		public float CloudShellTop { get; private set; } = 12000f;

		private readonly Vector4[] layerE = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerF = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerG = new Vector4[MaxCloudLayers];
		private float[] layerCoverage = new float[MaxCloudLayers];
		private float[] layerBottom = new float[MaxCloudLayers];
		private VolumetricCloudSettings cloudSettings;
		private float cloudBackgroundCover;
		private float cloudBackgroundStorm;
		private float cloudPrecipitation;
		private Vector2 cloudWind = Vector2.up;
		private float cloudWindSpeed = 6f;
		private Vector3 cloudSunDirection = Vector3.up;

		private void SetCloudGlobals(CelestialState state, in SkySample sample, in WeatherFrame weather, in WeatherFrame background, WeatherRenderProfile profile, WeatherTierSettings tier, in WeatherContext context)
		{
			VolumetricCloudSettings clouds = profile.Clouds;
			cloudsReady = profile.CloudShape != null && profile.CloudDetail != null && cycle != null;
			// Cleared here every frame, before anything renders, and raised again by the cloud
			// feature only once it has actually published a buffer. The sky bodies attenuate
			// themselves by that buffer, and an unbound one samples as zero — which would read as
			// "fully occluded" and empty the sky of moons and planets — so the default has to be
			// "do not attenuate".
			Shader.SetGlobalVector(CloudScreenId, Vector4.zero);
			CloudTier = new CloudTierSettings
			{
				Resolution = tier.CloudResolution,
				Steps = tier.CloudSteps,
				Detail = tier.CloudDetail,
				Temporal = tier.CloudTemporal,
				TemporalBlend = clouds.TemporalBlend,
			};
			CloudFarDistance = clouds.MaxDistance;
			cloudSettings = clouds;
			if (!cloudsReady || airless)
			{
				// Nothing to march: make sure no stale coverage is left behind.
				Shader.SetGlobalVector(CloudLayerId, Vector4.zero);
				return;
			}

			Shader.SetGlobalTexture(CloudShapeTexId, profile.CloudShape);
			Shader.SetGlobalTexture(CloudDetailTexId, profile.CloudDetail);
			// The background forecast, NOT the weather where the camera happens to stand. A storm cell
			// overhead must draw a cloud mass over *there*, through the weather map, and not raise
			// the coverage of the whole sky: standing under a cell would otherwise put a lid on the
			// world, which is exactly what it does not do.
			float cover = Mathf.Clamp01(background[WeatherChannel.CloudCover]);
			cloudBackgroundCover = cover;
			// Rain is a low-deck business: the clouds that carry it are the thick ones down there.
			cloudPrecipitation = Mathf.Clamp01(background[WeatherChannel.Precipitation]);
			Shader.SetGlobalVector(CloudShapeParamsId,
				new Vector4(clouds.DetailFadeStart, Mathf.Max(1f, clouds.DetailFadeRange), clouds.ShapeWarp, clouds.Density));
			Shader.SetGlobalVector(CloudCoverageId, new Vector4(clouds.CoverageCutClear, clouds.EdgeSoftness, clouds.CoverageCutFull, clouds.CoverageBend));
			// What the wind is doing to the clouds. The reported wind is what the panel shows and
			// what the weather says; the *axis* the noise is drawn out along is a different thing and
			// has to hold still. The lookup is rotated about the world origin on that axis, which is
			// harmless only while the axis does not move: the reported heading turns whenever a cell
			// drifts by or a layer fades in, and a sky read on it circled the scene every time. The
			// steady prevailing wind at this latitude is the axis; a pinned preset's heading is
			// constant anyway, and the panel's override moves only when the viewer moves it.
			bool driverActive = context.Timeline != null && context.Timeline.Driver;
			// How much of the sky the field owns right now. A preset at full strength owns all of
			// it, and the field's own terms — the formations, the front's slope — have to step back
			// with it or they are laid across a sky that was asked for exactly.
			float driverWeight = driverActive ? Mathf.Clamp01(context.DriverWeight) : 0f;
			driverActive = driverWeight > 0.001f;
			float latitude = state != null ? (float)state.Latitude : 0f;
			Vector2 wind = WeatherShaderGlobals.WindDirection(weather[WeatherChannel.WindHeading]);
			float speed = Mathf.Lerp(2f, 26f, weather[WeatherChannel.WindSpeed]);
			Vector2 axis = driverActive ? WeatherDriver.PrevailingWind(latitude) : wind;
			if (CloudWindOverride)
			{
				wind = WeatherShaderGlobals.WindDirection(CloudWindHeadingOverride);
				axis = wind;
				speed = CloudWindSpeedOverride;
			}
			if (axis.sqrMagnitude < 1e-6f)
			{
				axis = Vector2.up;
			}
			cloudWind = wind;
			cloudWindSpeed = speed;
			// Where the air has got to by now, worked out from the world clock rather than added up
			// frame by frame. The accumulator this replaces made a cloud's position a record of the
			// client's own frame times and join moment: two players stood side by side saw different
			// skies, a hitch shifted one of them permanently, and a reconnect snapped the sky
			// sideways. It is the same drift the weather field is sampled through, so the cloud
			// overhead and the weather underneath are the same air. Exact, in double: each band
			// wraps it to its own period on the way to the shader.
			WeatherDriver.DriftExact(WeatherDriver.WorldSeed, latitude, worldSeconds, out double driftX, out double driftY);
			cloudDrift = WeatherDriver.Drift(WeatherDriver.WorldSeed, latitude, worldSeconds);
			Shader.SetGlobalVector(CloudWindId, new Vector4(cloudDrift.x, cloudDrift.y, 2.5f, 1f));
			Shader.SetGlobalVector(CloudWindDirId, new Vector4(axis.x, axis.y, speed, 0f));
			shownFog = Mathf.Clamp01(weather[WeatherChannel.FogDensity]);
			shownRain = Mathf.Clamp01(weather[WeatherChannel.Precipitation]);
			// The bands themselves, and the shell they add up to.
			SetCloudLayers(profile, background, driverActive, driverWeight, axis, driftX, driftY);
			Shader.SetGlobalVector(CloudLightId, new Vector4(clouds.LightSteps, clouds.Powder, clouds.ForwardScatter, clouds.Ambient));

			// What lights the clouds: the sun while it is up, the moon after it sets. A sun below
			// the horizon must not keep lighting them, or a midnight sky glows like a sunset.
			float sunUp = Mathf.Clamp01(state.SunAltitude / 6f + 0.35f);
			Vector3 lightDirection = state.Sun >= 0 ? state.SunDirection : new Vector3(0.3f, 0.9f, 0.4f).normalized;
			Color lightColour = sample.SunLight * sample.SunIntensity;
			float strength = sunUp;
			// The moon that is lighting the sky NOW, not the one with the biggest disc: the clouds have
			// one light to be lit from, and with the big moon down it has to be the one that is up.
			float moonLight = 0f;
			int brightest = sunUp < 0.5f ? BrightestMoon(state, sample, 5f, out moonLight) : -1;
			if (brightest >= 0)
			{
				SkyBodyState moonBody = state.Bodies[brightest];
				if (moonLight > strength * 0.5f)
				{
					lightDirection = moonBody.Direction;
					lightColour = sample.MoonLight;
					strength = Mathf.Max(sunUp, moonLight);
				}
			}
			cloudSunDirection = lightDirection;
			Shader.SetGlobalVector(CloudSunDirId, new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, strength));
			Shader.SetGlobalColor(CloudSunColorId, lightColour);

			// The skylight a cloud sits in. With nothing above the horizon this is all a cloud has,
			// so it keeps a floor of the night sky's own glow: an overcast night is dark, but it is
			// still a sky with shapes in it, not a blank field.
			Color ambient = Color.Lerp(sample.CloudShadow, sample.CloudLit, 0.35f) * clouds.Ambient;
			float night = 1f - Mathf.Clamp01(strength * 2f);
			Color glow = (sample.AmbientSky * 1.2f + new Color(0.010f, 0.012f, 0.018f)) * night;
			Shader.SetGlobalColor(CloudAmbientId, new Color(Mathf.Max(ambient.r, glow.r),
				Mathf.Max(ambient.g, glow.g), Mathf.Max(ambient.b, glow.b), 1f));

			// What kind of sky this is. Flat stratus in still, damp weather; heaped cumulus as the
			// cloud builds. The mid sheet arrives as the forecast closes over, and there is nearly
			// always some cirrus up there — most of all in fair weather, when nothing hides it.
			float density = Mathf.Clamp01(background[WeatherChannel.CloudDensity]);
			float storm = Mathf.Clamp01(background.StormSeverity);
			cloudBackgroundStorm = storm;
			Shader.SetGlobalVector(CloudTypeParamsId, new Vector4(Mathf.Clamp01(density * 0.85f + 0.15f), storm, 0f, cloudPrecipitation));

			// What distance does to a cloud: the air in front of it. Without this the far side of a
			// sky is as white as the near side and the whole thing reads as a painted backdrop.
			Color haze = Color.Lerp(sample.Horizon, sample.Fog, 0.5f);
			Shader.SetGlobalVector(CloudHazeId, new Vector4(haze.r, haze.g, haze.b, Mathf.Max(2000f, clouds.HazeDistance)));

			// What an underside is made of. A fair-weather cloud's base is grey-blue skylight; a
			// storm's is slate, and much less of anything.
			Color tint = clouds.ShadedTint;
			float tintStrength = Mathf.Clamp01(clouds.ShadedTintStrength + storm * 0.35f);
			Shader.SetGlobalVector(CloudTintId, new Vector4(tint.r, tint.g, tint.b, tintStrength));
		}

		/// <summary>
		/// Decides the light shafts: where they come from, what colour they are and how strong.
		/// </summary>
		/// <remarks>
		/// Shafts need something to shine between, so they are strongest through broken cloud and
		/// fade out under a clear sky and under a solid overcast alike. A low sun makes longer ones.
		/// During a solar eclipse the shafts come from the body covering the sun instead, so the
		/// rays rake out around its silhouette the way a corona does.
		/// </remarks>
		private void SetGodRays(CelestialState state, in SkySample sample, in WeatherFrame weather, float overcast, float eclipse, WeatherTierSettings tier, in WeatherContext context)
		{
			GodRayIntensity = 0f;
			GodRayEclipse = 0f;
			GodRayMedium = 0f;
			if (!tier.GodRays || state == null || state.Sun < 0)
			{
				return;
			}
			// Crepuscular rays outlast the sun: it still lights the air overhead from a little below
			// the horizon, which is when they are at their longest.
			float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-3f, 1.5f, state.SunAltitude));
			if (up <= 0f)
			{
				return;
			}
			// The medium. Humidity itself does not reach the client, but everything it makes does:
			// fog, rain, a wet ground steaming off, and the cloud that condensed out of it.
			float fog = Mathf.Clamp01(weather[WeatherChannel.FogDensity]);
			float rain = Mathf.Clamp01(weather[WeatherChannel.Precipitation]);
			float medium = GodRayMediumFloor
				+ 0.9f * Mathf.Sqrt(fog)
				+ 0.35f * rain
				+ 0.25f * Mathf.Clamp01(context.Cover.Wet)
				+ 0.2f * overcast;
			medium = Mathf.Clamp01(medium);

			// Low sun: the light crosses far more air on its way, and rakes across it sideways to
			// the eye, so the lit air is brightest at the horizon and nearly gone at noon.
			float low = Mathf.Lerp(1f, 0.2f, Mathf.Clamp01(state.SunAltitude / 40f));
			// No factor for "is there anything to interrupt it": the gather finds that for itself,
			// pixel by pixel, and where nothing interrupts the light what is left is the glow of lit
			// air round the sun, which is right. Scaling the whole pass by how broken the sky is
			// took that glow away on exactly the clear mornings that show it best. Only a solid
			// deck ends it, because under one the sun is not reaching the air below at all.
			// Mathf.SmoothStep is an interpolation from a to b, not the shader's smoothstep(edge, edge, x):
			// handed (0.85, 1, overcast) it returns about 0.86 whatever the sky is doing.
			float strength = low * up * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, overcast)));

			Vector3 direction = state.SunDirection;
			Color colour = sample.SunLight;
			if (eclipse > 0.02f && TryFindBody(state, drawnEclipsingBody, out Vector3 eclipsing))
			{
				// The rays now come from around the body in front of the sun, and they are what is
				// left of the sun to see, so the deeper the eclipse the more they carry.
				direction = eclipsing;
				strength = Mathf.Max(strength, up * 0.35f) + eclipse * 0.5f * up;
				medium = Mathf.Max(medium, 0.5f * eclipse);
				GodRayEclipse = eclipse;
				colour = Color.Lerp(colour, new Color(1f, 0.93f, 0.85f), eclipse * 0.6f);
			}
			GodRayDirection = direction;
			GodRayColor = colour;
			GodRayMedium = medium;
			GodRayIntensity = Mathf.Clamp(strength, 0f, 3f);
		}

		/// <summary>The direction of a body in the sky, when it is one of the bodies drawn.</summary>
		private static bool TryFindBody(CelestialState state, CelestialBody body, out Vector3 direction)
		{
			direction = Vector3.zero;
			if (body == null)
			{
				return false;
			}
			for (int i = 0; i < state.Bodies.Count; i++)
			{
				if (state.Bodies[i].Body == body)
				{
					direction = state.Bodies[i].Direction;
					return true;
				}
			}
			return false;
		}

		private Vector2 cloudDrift;
		/// <summary>How far the air has carried the clouds, wrapped for a float: for readouts and traces.</summary>
		public Vector2 CloudDrift => cloudDrift;

		private void AddSun(in SkyBodyState star, in SkySample sample, SkyProfile sky, float eclipse, ref int count)
		{
			var starBody = star.Body as StarBody;
			Color tint = starBody != null ? starBody.SkyTint : Color.white;
			float luminosity = starBody != null ? starBody.Luminosity : 1f;
			// Each disc and halo in its own star's colour, whatever the sky's blend.
			Color color = Mul(baseSunLight, tint) * (0.8f + 0.2f * Mathf.Sqrt(luminosity));
			float radius = Mathf.Max(star.AngularRadius * sky.SunScale, 0.0015f);
			sunDirections[count] = new Vector4(star.Direction.x, star.Direction.y, star.Direction.z, radius);
			sunColors[count] = new Vector4(color.r, color.g, color.b, sky.SunHalo * (1f - eclipse));
			count++;
		}

		private void ApplyLights(CelestialState state, in SkySample sample, float overcast, float eclipse, in WeatherFrame weather, float dt, WeatherRenderProfile profile, WeatherTierSettings tier)
		{
			if (sun == null)
			{
				return;
			}
			float flash = lightning.Flash;
			Vector3 sunDirection = state.Sun >= 0 ? state.SunDirection : new Vector3(0.3f, 0.8f, 0.5f).normalized;
			float sunAltitude = state.Sun >= 0 ? state.SunAltitude : 50f;
			float sunIntensity = sample.SunIntensity * (1f - eclipse * 0.95f) * (1f - overcast * 0.6f);
			sun.transform.rotation = LightRotation(sunDirection);
			sun.color = Color.Lerp(sample.SunLight, new Color(0.8f, 0.85f, 1f), flash * 0.6f);
			sun.intensity = sunIntensity + flash * 1.5f;

			// ── Every moon that is up ──
			// Each lights the ground by what it sends: how much of it is lit, how much of that the
			// world's own shadow has taken, how high it stands, and how big it is in the sky — a moon
			// twice as wide is four times the light. The size is against our own moon's, which is
			// what the profile's moon intensity was authored for, so a sky with one ordinary moon is
			// exactly as it was.
			//
			// There used to be one moon light, and it was given to the moon with the LARGEST DISC,
			// wherever that was. With the big moon under the horizon and a small one riding high and
			// full, there was no moonlight at all; and a second moon never lit anything.
			int moonsLit = 0;
			float brightestMoon = 0f;
			int moonLimit = Mathf.Clamp(state.System != null ? state.System.Limits.Moons : 4, 0, MaxMoonLights);
			moonOrder.Clear();
			for (int i = 0; i < state.Bodies.Count; i++)
			{
				SkyBodyState body = state.Bodies[i];
				if (body.Kind != SkyBodyKind.Moon || body.AltitudeDegrees <= -2f)
				{
					continue;
				}
				float light = MoonLight(sample, body, 3f) * (1f - overcast * 0.7f);
				if (light > 0.001f)
				{
					moonOrder.Add((i, light));
				}
			}
			moonOrder.Sort((x, y) => y.light.CompareTo(x.light));
			for (int n = 0; n < moonOrder.Count && n < moonLimit; n++)
			{
				SkyBodyState body = state.Bodies[moonOrder[n].index];
				// The brightest is "the" moon light, the one the rest of the sky knows about; the
				// others come from the pool.
				Light lamp = n == 0 ? moon : Pooled(companionMoons, n - 1, "Sky Companion Moon");
				if (lamp == null)
				{
					continue;
				}
				lamp.enabled = true;
				lamp.transform.rotation = LightRotation(body.Direction);
				lamp.intensity = moonOrder[n].light;
				// Mostly the sky's moonlight, a little the moon's own colour.
				Color tint = body.Body != null ? Color.Lerp(Color.white, body.Body.Tint, 0.35f) : Color.white;
				lamp.color = Mul(sample.MoonLight, tint);
				lamp.shadows = LightShadows.None;
				moonsLit++;
				brightestMoon = Mathf.Max(brightestMoon, moonOrder[n].light);
			}
			if (moon != null && moonsLit == 0)
			{
				moon.intensity = 0f;
				moon.enabled = false;
			}
			for (int i = Mathf.Max(0, moonsLit - 1); i < companionMoons.Count; i++)
			{
				companionMoons[i].enabled = false;
			}

			// ── Every other sun that is up ──
			// By its own height in the sky and by what actually arrives from it. It used to take the
			// PRIMARY's intensity — so every companion went dark the moment the primary set, however
			// high it stood itself — and its luminosity with no regard to distance, so a companion
			// thirty times further off than the primary lit the ground six tenths as brightly. Light
			// falls off as the square of the distance: that one is a thousandth.
			SkyBodyState primaryStar = state.Sun >= 0 ? state.Bodies[state.Sun] : default;
			float primaryFlux = state.Sun >= 0 ? Flux(primaryStar) : 0f;
			int sunLimit = Mathf.Clamp((state.System != null ? state.System.Limits.Suns : 4) - 1, 0, 3);
			int needed = 0;
			float brightestCompanion = 0f;
			Light brightestCompanionLight = null;
			for (int i = 0; i < state.Bodies.Count && needed < sunLimit; i++)
			{
				if (i == state.Sun || state.Bodies[i].Kind != SkyBodyKind.Star)
				{
					continue;
				}
				SkyBodyState star = state.Bodies[i];
				Light companion = Pooled(companions, needed++, "Sky Companion Sun");
				float relative = primaryFlux > 1e-12f ? Mathf.Clamp(Flux(star) / primaryFlux, 0f, 2f) : 1f;
				float own = blendTo != null ? Mathf.Max(0f, blendTo.SunIntensity.Evaluate(star.AltitudeDegrees)) : sample.SunIntensity;
				companion.transform.rotation = LightRotation(star.Direction);
				companion.intensity = own * relative * (1f - overcast * 0.6f);
				companion.color = Mul(baseSunLight, ((StarBody)star.Body).SkyTint);
				companion.shadows = LightShadows.None;
				companion.enabled = star.AltitudeDegrees > -2f && companion.intensity > 0.001f;
				if (companion.enabled && companion.intensity > brightestCompanion)
				{
					brightestCompanion = companion.intensity;
					brightestCompanionLight = companion;
				}
			}
			for (int i = needed; i < companions.Count; i++)
			{
				companions[i].enabled = false;
			}

			// ── One light casts the shadows: the brightest thing in the sky ──
			// The primary while it is properly up, as before, so a day does not swap shadows about;
			// after that whatever is lighting the scene most — a companion sun at the primary's dusk,
			// the brightest moon at night. Nothing used to cast a shadow once the primary was down
			// unless "the" moon happened to be the one that was up.
			bool sunLeads = sunAltitude > 0.5f && sun.intensity > 0.02f;
			Light leader = sunLeads ? sun : null;
			if (leader == null)
			{
				float most = Mathf.Max(sun.intensity, 0.02f);
				leader = sun.intensity > 0.02f ? sun : null;
				if (brightestCompanionLight != null && brightestCompanion > most)
				{
					most = brightestCompanion;
					leader = brightestCompanionLight;
				}
				if (moon != null && moonsLit > 0 && brightestMoon > most)
				{
					leader = moon;
				}
			}
			sun.shadows = LightShadows.None;
			if (leader != null)
			{
				leader.shadows = LightShadows.Soft;
			}
			RenderSettings.sun = leader != null ? leader : sun;

			// The shadow is the volume's own, marched from the ground toward the light.
			VolumetricCloudSettings cloudSettings = profile.Clouds;
			Vector3 viewer = TargetCamera != null ? TargetCamera.transform.position : transform.position;
			cloudShadows.Update(leader != null ? leader : sun, profile.CloudMaterial, viewer, cloudSettings.ShadowAreaMeters,
				cloudSettings.ShadowStrength, 8 * MaxCloudLayers, tier.CloudShadows && DrawCloudShadows);
		}

		/// <summary>A directional light shining along −direction.</summary>
		public static Quaternion LightRotation(Vector3 towardLight)
		{
			Vector3 forward = -towardLight.normalized;
			Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
			return Quaternion.LookRotation(forward, up);
		}

		private void ApplyAmbient(in SkySample sample, float overcast, float eclipse, float flash)
		{
			float dim = (1f - overcast * 0.3f) * (1f - eclipse * 0.8f);
			Color flashColor = new Color(0.3f, 0.32f, 0.38f) * flash;
			Color skyColor = sample.AmbientSky * dim + flashColor;
			Color equator = sample.AmbientEquator * dim + flashColor * 0.6f;
			Color ground = sample.AmbientGround * dim;
			if (Changed(skyColor, lastAmbientSky) || Changed(equator, lastAmbientEquator) || Changed(ground, lastAmbientGround))
			{
				RenderSettings.ambientMode = AmbientMode.Trilight;
				RenderSettings.ambientSkyColor = skyColor;
				RenderSettings.ambientEquatorColor = equator;
				RenderSettings.ambientGroundColor = ground;
				lastAmbientSky = skyColor;
				lastAmbientEquator = equator;
				lastAmbientGround = ground;
			}
		}

		private static bool Changed(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 0.003f;

		private void UpdateReflection(CelestialState state, float dt, WeatherTierSettings tier)
		{
			if (tier.ReflectionResolution <= 0)
			{
				return;
			}
			if (probe == null)
			{
				var go = new GameObject("Sky Reflection");
				go.transform.SetParent(transform, false);
				probe = go.AddComponent<ReflectionProbe>();
				probe.mode = ReflectionProbeMode.Realtime;
				probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
				probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
				probe.clearFlags = ReflectionProbeClearFlags.Skybox;
				probe.cullingMask = 0;
				probe.size = Vector3.one * 100000f;
				probe.importance = 0;
			}
			probe.resolution = tier.ReflectionResolution;
			probeTimer -= dt;
			bool moved = Vector3.Angle(probeSun, state.SunDirection) > ReflectionSunDegrees;
			if (probeTimer <= 0f || moved)
			{
				probeTimer = ReflectionRefreshSeconds;
				probeSun = state.SunDirection;
				probe.RenderProbe();
			}
			if (probe.realtimeTexture != null)
			{
				RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
				RenderSettings.customReflectionTexture = probe.realtimeTexture;
			}
		}

		private void CheckCamera(Camera camera)
		{
			if (warnedCamera || camera == null)
			{
				return;
			}
			warnedCamera = true;
			if (camera.clearFlags != CameraClearFlags.Skybox)
			{
				FishMMO.Logging.Log.Warning("SkySystem", $"{camera.name} clears to {camera.clearFlags}, not Skybox: the sky will not show.");
			}
		}

		private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
		{
			if (cycle == null || camera == null)
			{
				return;
			}
			Camera wanted = TargetCamera != null ? TargetCamera : Camera.main;
			if (camera != wanted)
			{
				return;
			}
			WeatherRenderProfile profile = Profile;
			if (bodyMaterial != null)
			{
				DrawBodies(camera);
			}
			if (profile != null)
			{
				WeatherPresentation presentation = WeatherPresentation.Instance;
				WeatherTierSettings tier = profile.TierFor(QualitySettings.GetQualityLevel());
				if (presentation != null && presentation.HasContext)
				{
					curtains.Draw(presentation.Context.Timeline, (uint)presentation.Context.Tick, camera, profile.CurtainMaterial, profile, tier.Curtains, (float)(worldSeconds % 100000.0));
				}
				lightning.Draw(camera, profile.BoltMaterial);
			}
		}

		// One mesh and property block per textured body: RenderMesh uses them when the frame renders.
		private readonly List<SkyBodyMesh> texturedMeshes = new List<SkyBodyMesh>();
		private readonly List<MaterialPropertyBlock> texturedBlocks = new List<MaterialPropertyBlock>();

		private static readonly int RingMatrixId = Shader.PropertyToID("_FishRingMatrix");
		private static readonly int OwnRingId = Shader.PropertyToID("_FishOwnRing");
		private static readonly int OwnRingTintId = Shader.PropertyToID("_FishOwnRingTint");
		private static readonly int OwnRingZenithId = Shader.PropertyToID("_FishOwnRingZenith");
		private static readonly int OwnRingSunId = Shader.PropertyToID("_FishOwnRingSun");
		private static readonly int OwnRingTexId = Shader.PropertyToID("_FishOwnRingTex");

		/// <summary>
		/// The rings of the world stood on, for the sky shader: an arc along the celestial equator.
		/// </summary>
		/// <remarks>
		/// In the observer's own equatorial frame, where the ring plane is simply z = 0 and the
		/// observer stands one body radius out along "straight up". Which way the world has turned
		/// does not matter to a ring that is the same all the way round, so no hour angle comes into
		/// it: up and the sun, both taken into that frame, are all the shader needs.
		/// </remarks>
		private void SetOwnRing(CelestialState state)
		{
			CelestialBody observer = state != null ? state.Observer : null;
			RingSettings rings = observer != null && observer.HasRings ? observer.Rings : null;
			if (rings == null || rings.Opacity <= 0.001f)
			{
				Shader.SetGlobalVector(OwnRingId, Vector4.zero);
				return;
			}
			Matrix4x4 toEquatorial = state.EquatorialToScene.transpose;
			Vector3 zenith = toEquatorial.MultiplyVector(Vector3.up).normalized;
			Vector3 sun = toEquatorial.MultiplyVector(state.SunDirection).normalized;
			Shader.SetGlobalMatrix(RingMatrixId, toEquatorial);
			Shader.SetGlobalVector(OwnRingId, new Vector4(rings.Inner, rings.Outer, rings.Bands, rings.Opacity));
			Shader.SetGlobalVector(OwnRingTintId, rings.Tint);
			Shader.SetGlobalVector(OwnRingZenithId, new Vector4(zenith.x, zenith.y, zenith.z, rings.Texture != null ? 1f : 0f));
			Shader.SetGlobalVector(OwnRingSunId, new Vector4(sun.x, sun.y, sun.z, 0f));
			if (rings.Texture != null)
			{
				Shader.SetGlobalTexture(OwnRingTexId, rings.Texture);
			}
		}

		private readonly List<SkyBodyMesh> ringMeshes = new List<SkyBodyMesh>();
		private readonly List<MaterialPropertyBlock> ringBlocks = new List<MaterialPropertyBlock>();

		private static readonly int OccludersId = Shader.PropertyToID("_FishOccluders");
		private static readonly int OccluderRanksId = Shader.PropertyToID("_FishOccluderRanks");
		private static readonly int OccluderCountId = Shader.PropertyToID("_FishOccluderCount");
		private readonly Vector4[] occluders = new Vector4[SkyBodyMesh.MaxOccluders];
		private readonly Vector4[] occluderRanks = new Vector4[SkyBodyMesh.MaxOccluders];

		/// <summary>
		/// The discs that hide what is behind them, for the body shader. The list is furthest first,
		/// so when there are more than the shader tests, the ones kept are the last: the nearest,
		/// which are the largest in the sky and the ones whose dark limbs anything is seen through.
		/// </summary>
		private void UploadOccluders()
		{
			int total = bodies.Occluders.Count;
			int count = Mathf.Min(total, SkyBodyMesh.MaxOccluders);
			for (int i = 0; i < count; i++)
			{
				occluders[i] = bodies.Occluders[total - count + i];
				occluderRanks[i] = bodies.OccluderRanks[total - count + i];
			}
			// Arrays are sized by their first upload, so the whole of each goes up every time.
			Shader.SetGlobalVectorArray(OccludersId, occluders);
			Shader.SetGlobalVectorArray(OccluderRanksId, occluderRanks);
			Shader.SetGlobalFloat(OccluderCountId, count);
		}

		/// <summary>
		/// Draws the sky's bodies in the sequence the mesh worked out, furthest first.
		/// </summary>
		/// <remarks>
		/// Every draw carries a rising priority. They all share one material and one set of bounds
		/// round the camera, so the pipeline's own back-to-front sort measures them all the same
		/// distance away and is free to put them in any order it likes; the priority is sorted on
		/// before distance, and makes the order the sequence's.
		/// </remarks>
		private void DrawBodies(Camera camera)
		{
			CelestialState state = State;
			float scale = blendTo != null ? blendTo.BodyScale : 1f;
			var bounds = new Bounds(camera.transform.position, Vector3.one * 20000f);
			int textured = 0, ringed = 0;
			for (int i = 0; i < bodies.Steps.Count; i++)
			{
				SkyBodyMesh.Step step = bodies.Steps[i];
				MaterialPropertyBlock properties;
				Mesh mesh;
				int subMesh = 0;
				switch (step.Kind)
				{
					case SkyBodyMesh.StepKind.Quads:
					{
						if (bodies.Mesh == null || bodies.Mesh.vertexCount == 0 || step.SubMesh >= bodies.Mesh.subMeshCount)
						{
							continue;
						}
						block.Clear();
						block.SetFloat(UseTextureId, 0f);
						properties = block;
						mesh = bodies.Mesh;
						subMesh = step.SubMesh;
						break;
					}
					case SkyBodyMesh.StepKind.Textured:
					{
						while (texturedMeshes.Count <= textured)
						{
							texturedMeshes.Add(new SkyBodyMesh());
							texturedBlocks.Add(new MaterialPropertyBlock());
						}
						SkyBodyState body = step.Body;
						SkyBodyMesh quad = texturedMeshes[textured];
						quad.Clear();
						Color tint = body.Body.Tint;
						tint.a = body.Body is WorldBody world && world.HasWeather ? 0.4f : 1f;
						quad.AddQuad(body.Direction, SkyBodyMesh.Kind.Disc, body.AngularRadius * scale, body.Illumination, body.Shadowed, body.LightDirection, 1.2f, Vector3.up, 0f, tint, step.Rank);
						properties = texturedBlocks[textured];
						properties.Clear();
						properties.SetTexture(BodyTexId, body.Body.SurfaceTexture);
						properties.SetFloat(UseTextureId, 1f);
						mesh = quad.Upload();
						textured++;
						break;
					}
					default:
					{
						if (state == null)
						{
							continue;
						}
						while (ringMeshes.Count <= ringed)
						{
							ringMeshes.Add(new SkyBodyMesh());
							ringBlocks.Add(new MaterialPropertyBlock());
						}
						SkyBodyState body = step.Body;
						RingSettings rings = body.Body.Rings;
						SkyBodyMesh quad = ringMeshes[ringed];
						quad.Clear();
						// The ring plane's normal is the body's own pole, brought from the ecliptic into
						// this sky the way every other direction in it is.
						Vector3 pole = state.HeliocentricDirection(CelestialMath.PoleOf(body.Body));
						quad.AddRing(body.Direction, body.AngularRadius * scale, pole, rings, body.LightDirection, step.Rank);
						properties = ringBlocks[ringed];
						properties.Clear();
						properties.SetFloat(UseTextureId, rings.Texture != null ? 1f : 0f);
						if (rings.Texture != null)
						{
							properties.SetTexture(BodyTexId, rings.Texture);
						}
						mesh = quad.Upload();
						ringed++;
						break;
					}
				}
				var rp = new RenderParams(bodyMaterial)
				{
					camera = camera,
					matProps = properties,
					worldBounds = bounds,
					shadowCastingMode = ShadowCastingMode.Off,
					receiveShadows = false,
					rendererPriority = i,
				};
				Graphics.RenderMesh(rp, mesh, subMesh, Matrix4x4.identity);
			}
		}
	}
}
