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

		private static readonly int ZenithId = Shader.PropertyToID("_FishSkyZenith");
		private static readonly int HorizonId = Shader.PropertyToID("_FishSkyHorizon");
		private static readonly int GroundId = Shader.PropertyToID("_FishSkyGround");
		private static readonly int FogColorId = Shader.PropertyToID("_FishSkyFogColor");
		private static readonly int ParamsId = Shader.PropertyToID("_FishSkyParams");
		private static readonly int SunShapeId = Shader.PropertyToID("_FishSunShape");
		private static readonly int EclipseCoverId = Shader.PropertyToID("_FishSkyEclipseCover");
		private static readonly int LunarShadowId = Shader.PropertyToID("_FishLunarShadow");
		private static readonly int LunarShadowEdgeId = Shader.PropertyToID("_FishLunarShadowEdge");
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
		private static readonly int CloudShearId = Shader.PropertyToID("_FishCloudShear");
		private static readonly int CloudMesoId = Shader.PropertyToID("_FishCloudMeso");
		private static readonly int CloudMesoParamsId = Shader.PropertyToID("_FishCloudMesoParams");
		private static readonly int CloudMesoSeedId = Shader.PropertyToID("_FishCloudMesoSeed");
		private static readonly int CloudColumnId = Shader.PropertyToID("_FishCloudColumn");
		private static readonly int CloudTowerDriftId = Shader.PropertyToID("_FishCloudTowerDrift");
		private static readonly int CloudTowerSeedId = Shader.PropertyToID("_FishCloudTowerSeed");
		private static readonly int CloudSubId = Shader.PropertyToID("_FishCloudSub");
		private static readonly int CloudViewerId = Shader.PropertyToID("_FishCloudViewer");
		private static readonly int CloudFlowId = Shader.PropertyToID("_FishCloudFlow");
		private static readonly int FogFlowId = Shader.PropertyToID("_FishFogFlow");
		private static readonly int MistId = Shader.PropertyToID("_FishMist");

		/// <summary>
		/// How much of the height the air could climb (<see cref="CloudClimbHeight"/>) the clouds' steering round
		/// the terrain honours, 0..1 (<c>_FishCloudFlow.y</c>). 0: every peak taller than a cloud parts it. The
		/// physical figure, all of it, is right for a great range and wrong for the look this game wants: on an
		/// ordinary day the air can climb about a kilometre, and in a scene whose peaks stand a kilometre over its
		/// valleys every cloud cleared every peak and nothing went round anything.
		/// </summary>
		public const float CloudSteeringClimbShare = 0f;

		/// <summary>How far the ground mist is drawn from the camera, m (FishMist.hlsl's march).</summary>
		private const float MistRange = 300f;

		/// <summary>
		/// The fog's own stability, N (1/s): it is the air the night has chilled under an inversion,
		/// some four kelvin warmer every hundred metres up — N² = (g/T)·dθ/dz ≈ 9.8/280 × 0.04 — far
		/// stiffer than the air above it. So a fog climbs almost nothing and flows round any hill that
		/// stands out of it (FishFogStructure).
		/// </summary>
		private const float FogBuoyancy = 0.035f;

		/// <summary>The most cover a hill's cap cloud adds (FISH_CLOUD_CAP_COVER, FishCloudVolume.hlsl).</summary>
		private const float CapCover = 0.7f;

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
		private static readonly int AmbientSkyId = Shader.PropertyToID("_FishAmbientSky");
		private static readonly int AmbientEquatorId = Shader.PropertyToID("_FishAmbientEquator");
		private static readonly int AmbientGroundId = Shader.PropertyToID("_FishAmbientGround");
		private static readonly int CloudTypeParamsId = Shader.PropertyToID("_FishCloudTypeParams");
		private static readonly int CloudSunDirId = Shader.PropertyToID("_FishCloudSunDir");
		private static readonly int CloudSunColorId = Shader.PropertyToID("_FishCloudSunColor");
		private static readonly int CloudAmbientId = Shader.PropertyToID("_FishCloudAmbient");
		private static readonly int CloudHazeId = Shader.PropertyToID("_FishCloudHaze");
		private static readonly int CloudLayerAId = Shader.PropertyToID("_FishCloudLayerA");
		private static readonly int CloudLayerBId = Shader.PropertyToID("_FishCloudLayerB");
		private static readonly int CloudLayerCId = Shader.PropertyToID("_FishCloudLayerC");
		private static readonly int CloudLayerDId = Shader.PropertyToID("_FishCloudLayerD");
		private static readonly int CloudLayerSunId = Shader.PropertyToID("_FishCloudLayerSun");
		private static readonly int CloudIceId = Shader.PropertyToID("_FishCloudIce");
		private static readonly int CloudGroundId = Shader.PropertyToID("_FishCloudGround");
		private static readonly int CloudAirExtinctionId = Shader.PropertyToID("_FishCloudAirExtinction");
		private static readonly int CloudAerosolId = Shader.PropertyToID("_FishCloudAerosol");
		private static readonly int CloudLayerCountId = Shader.PropertyToID("_FishCloudLayerCount");
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

		/// <summary>The solar eclipse as the discs are drawn: what the sky is showing.</summary>
		public SolarEclipseInfo DrawnEclipse { get; private set; }

		/// <summary>
		/// The colours of this world's air, for what is drawn in it: mist, the haze of rain and snow,
		/// the shaded side of a cloud. Derived from the sky sample, so a dusty world's mist is tan and
		/// a dusk mist is warm; the render profile's colours are what a standard noon gives.
		/// </summary>
		/// <remarks>
		/// Each is a CHROMA — a colour scaled to a fixed brightness — because the brightness is
		/// somebody else's: the fog composer dims mist by the sky's light, and the cloud shader lights
		/// a shaded side by the sky it faces. Handed a colour that was already dark at dusk, both
		/// darkened it again.
		/// </remarks>
		public Color AirChroma { get; private set; } = new Color(0.7f, 0.73f, 0.76f, 1f);
		public Color CloudShadeChroma { get; private set; } = new Color(0.62f, 0.68f, 0.82f, 1f);

		private static Color Chroma(Color c, float brightness)
		{
			float lum = Mathf.Max(1e-4f, c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f);
			return new Color(c.r / lum * brightness, c.g / lum * brightness, c.b / lum * brightness, 1f);
		}

		/// <summary>A cloud's shaded colour recoloured by the skylight: its own brightness, the sky's hue.</summary>
		public Color InCloudShade(Color authored)
		{
			float lum = authored.r * 0.2126f + authored.g * 0.7152f + authored.b * 0.0722f;
			Color chroma = Chroma(CloudShadeChroma, 1f);
			float chromaLum = chroma.r * 0.2126f + chroma.g * 0.7152f + chroma.b * 0.0722f;
			Color recoloured = new Color(authored.r * chroma.r, authored.g * chroma.g, authored.b * chroma.b, 1f) * (1f / Mathf.Max(1e-4f, chromaLum));
			recoloured.a = authored.a;
			return recoloured;
		}

		/// <summary>A profile colour recoloured by the air: its own brightness, the air's hue.</summary>
		public Color InAir(Color authored)
		{
			Color chroma = Chroma(AirChroma, 1f);
			float chromaLum = chroma.r * 0.2126f + chroma.g * 0.7152f + chroma.b * 0.0722f;
			Color recoloured = new Color(authored.r * chroma.r, authored.g * chroma.g, authored.b * chroma.b, 1f) * (1f / Mathf.Max(1e-4f, chromaLum));
			recoloured.a = authored.a;
			return recoloured;
		}
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
		private bool warnedCamera;
		private SkyBodyMesh bodies;
		private LightningPresenter lightning;
		private CurtainPresenter curtains;
		private VortexPresenter vortices;
		private CloudShadowPresenter cloudShadows;
		private CloudTerrainMap cloudTerrain;
		private CloudFlowField cloudFlow;
		private MistGroundMap mistGround;
		private WeatherMap weatherMap;
		private readonly Vector4[] layerA = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerB = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerC = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerD = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerSun = new Vector4[MaxCloudLayers];
		private readonly float[] layerSunHeight = new float[MaxCloudLayers];

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
		public VortexPresenter Vortices => vortices;
		public SkyBodyMesh Bodies => bodies;
		public WeatherMap Map => weatherMap;

		/// <summary>The most bands the shader takes.</summary>
		public const int MaxCloudLayers = 6;

		/// <summary>The regimes the sky is made of, as the renderer last worked them out from the air.</summary>
		public IReadOnlyList<CloudBand> CloudBands => cloudBands;

		/// <summary>How many of <see cref="CloudBands"/> the air makes here and are drawn.</summary>
		public int CloudBandCount => cloudBandCount;
		/// <summary>
		/// Rebuilds the air over the whole visible sky on the next frame, whole: for when the air has
		/// just changed all at once — an admin's instant change, a teleport, a test stage.
		/// </summary>
		public void RebuildCloudAir()
		{
			cloudAir?.Invalidate();
			// And the storms' cloud, which is worked out in that air.
			weatherMap?.Invalidate();
		}

		/// <summary>The extremes of the air over the whole visible sky: where its cloud can be at all.</summary>
		public CloudClimate.MapExtremes CloudMapExtremes => cloudAir != null ? cloudAir.Extremes : default;

		/// <summary>
		/// The state the bands were last built from: what the forecast asked for, and what the wind
		/// and the sun were doing when it did. Read by the sim panels' statistics, which would
		/// otherwise have to guess at numbers the renderer already knows.
		/// </summary>
		public float CloudCover => cloudBackgroundCover;
		/// <summary>How much storm the sky is under, 0..1: what fills the bands that grow storms.</summary>
		/// <summary>How much precipitation the sky is under, 0..1: what thickens the bands that carry rain.</summary>
		public float CloudPrecipitation => cloudPrecipitation;
		/// <summary>The wind the bands drift on: a unit direction on the ground plane.</summary>
		public Vector2 CloudWind => cloudWind;

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
			vortices = vortices ?? new VortexPresenter();
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
			vortices?.Dispose();
			cloudShadows?.Dispose();
			cloudTerrain?.Dispose();
			cloudTerrain = null;
			cloudFlow?.Dispose();
			cloudFlow = null;
			mistGround?.Dispose();
			mistGround = null;
			Shader.SetGlobalVector(MistId, Vector4.zero);
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

			// The air's colours, from the sample as it now stands: the fog is what mist looks like,
			// and the skylight is what a cloud's underside is lit by.
			AirChroma = Chroma(sample.Fog, 0.73f);
			CloudShadeChroma = Chroma(sample.AmbientSky, 0.68f);

			float overcast = Mathf.Clamp01(weather[WeatherChannel.CloudCover] * (0.4f + 0.6f * weather[WeatherChannel.CloudDensity]));
			// The eclipse of the discs as they are drawn. Life-size that is the true one; larger than
			// life the discs meet sooner and part later, and the sky has to go dark with what it shows.
			SolarEclipseInfo solar = state.SolarEclipseAsDrawn(blendTo != null ? blendTo.SunScale : 1f, blendTo != null ? blendTo.BodyScale : 1f);
			drawnEclipsingBody = solar.Covering;
			DrawnEclipse = solar;
			// What the day is dimmed by: the adapted eye's darkness, not the covered area. A partial
			// eclipse is an ordinary-looking day with a bite out of the sun until the last tenth.
			float eclipse = solar.Darkness;
			if (solar.Totality > 0f)
			{
				// Totality is a twilight in the middle of the day: the sky overhead goes deep blue, the
				// horizon all round takes the colour of dusk from the sunlit air beyond the shadow,
				// and the brighter stars come out. The sample for a sun six degrees down is all of that,
				// and everything that reads the sample — fog, ambient, the clouds — follows it.
				SkySample twilight = blendTo.Evaluate(-6f, state.Observer);
				twilight = TintBySuns(state, twilight, out _);
				twilight.SunLight = sample.SunLight;
				twilight.SunIntensity = 0f;
				twilight.MoonLight = sample.MoonLight;
				twilight.MoonIntensity = sample.MoonIntensity;
				sample = SkySample.Lerp(sample, twilight, solar.Totality);
				current = sample;
			}

			// Lightning first: its flash reaches the sky, the lights and the weather globals.
			Vector3 viewer = camera != null ? camera.transform.position : Vector3.zero;
			// With the lightning of the weather here — the field's storms and heavy rain — and not only
			// the scene layers' and the cells'.
			lightning.Update(timeline, tick, worldSeconds, viewer, camera, profile.BoltMaterial, context.Sample);
			if (presentation != null)
			{
				presentation.LightningFlash = lightning.Flash;
			}

			// The storms' own cloud, before the bands are packed: the shells the march skips empty air
			// by have to take in their bases, towers and anvils as they are now.
			if (camera != null)
			{
				weatherMap.Update(timeline, viewer, tick, context.Sample, dt, WeatherMapRefreshSeconds);
			}

			SetGodRays(state, sample, weather, overcast, eclipse, tier, context);
			SetSkyGlobals(state, sample, blendTo, weather, overcast, eclipse, context, tier, profile);
			ApplyLights(state, sample, overcast, eclipse, weather, dt, profile, tier);
			ApplyAmbient(sample, overcast, eclipse, lightning.Flash);
			FogComposer.SetSkyColor(Color.Lerp(sample.Fog, sample.Fog * 0.7f + new Color(0.25f, 0.26f, 0.28f) * 0.3f, overcast));
			UpdateReflection(state, dt, tier);
			CheckCamera(camera);

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
			// Only the distance fog's share of the weather: the fog's own drops are drawn along the sky's
			// rays by the cloud march, out to where the world curves from under them, and a band painted
			// on the dome as well was a second fog at the horizon — flat, and as thick in a mist as in a
			// dense fog (WeatherFogPresenter.UniformAmount).
			float fogBlend = Mathf.Clamp01((RenderSettings.fog ? 0.45f : 0f) + WeatherFogPresenter.UniformAmount(weather, FogLayerView.Drawn) * 0.55f) * (1f - airless);
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
			if (DrawnEclipse.Obscuration > 0.02f)
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
			// x how dark the day looks (the adapted eye), y the lunar eclipse's umbral share, z airless,
			// w totality: the corona, the last of the disc.
			Shader.SetGlobalVector(EclipseId, new Vector4(eclipse, state.LunarEclipse, airless, DrawnEclipse.Totality));
			// And how much air the covering body has: a world's atmosphere lit from behind is the
			// bright rim round it, which an airless moon does not have.
			float coveringAir = DrawnEclipse.Covering is WorldBody coveringWorld ? AtmosphereModel.Density(coveringWorld.Atmosphere) : 0f;
			Shader.SetGlobalVector(EclipseCoverId, new Vector4(DrawnEclipse.Obscuration, DrawnEclipse.Magnitude, (float)DrawnEclipse.Phase, coveringAir));
			// The planet's shadow at the moon, for the moon to be darkened against pixel by pixel.
			if (state.Moon >= 0 && state.Bodies[state.Moon].ShadowPhase != LunarEclipsePhase.None)
			{
				SkyBodyState shadowed = state.Bodies[state.Moon];
				Shader.SetGlobalVector(LunarShadowId, new Vector4(shadowed.ShadowDirection.x, shadowed.ShadowDirection.y, shadowed.ShadowDirection.z, shadowed.UmbraRadius * sky.BodyScale));
				Shader.SetGlobalVector(LunarShadowEdgeId, new Vector4(shadowed.PenumbraRadius * sky.BodyScale, 1f, 0f, 0f));
			}
			else
			{
				Shader.SetGlobalVector(LunarShadowEdgeId, Vector4.zero);
			}
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
		/// Hands the shader the clouds the air makes: three regimes — the column from the cloud base
		/// up, the middle sheet, the high ice — each where the air puts it, and the air map that says
		/// what each of them does place by place.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Nothing here is authored. Where a cloud's base is, how high it can grow, whether it is a
		/// deck or a heap or a tower, where the ice starts, how opaque a cloud is and how big its cells
		/// are all follow from the air — its temperature, humidity, pressure pattern and stability —
		/// on this world, with its pull, its air and its spin. The only way to change the sky is to
		/// change the air.
		/// </para>
		/// <para>
		/// Two kinds of number, kept apart. The shape and drift scales are the world's and are worked
		/// out once from its average air: the noise is read at hundreds of kilometres of accumulated
		/// drift divided by them, so a scale that moved with the weather would zoom and teleport the
		/// whole sky. Heights, cover and opacity are the weather's and follow the air.
		/// </para>
		/// </remarks>
		private void SetCloudLayers(in WeatherContext context, Vector2 axis, double driftX, double driftY, float latitude, float season01, double worldSeconds, Vector3 viewerAt)
		{
			WeatherTimeline timeline = context.Timeline;
			WorldSceneSettings settings = context.Settings;
			WeatherSample sample = context.Sample;
			if (timeline == null || timeline.SceneMode != WeatherSceneMode.Own || !sample.Planet.HasAir || sample.Planet.Gravity <= 0f)
			{
				Shader.SetGlobalInt(CloudLayerCountId, 0);
				CloudAirMap.Unpublish();
				cloudBandCount = 0;
				// No cloud of its own, but a fog can still lie here, and the march that draws it runs
				// through the stack's shell: the fog's shell alone.
				cloudTerrain ??= new CloudTerrainMap();
				cloudTerrain.Update(viewerAt);
				PublishFlow(axis);
				MistPotential = 0f;
				MistBestPotential = 0f;
				Shader.SetGlobalVector(MistId, Vector4.zero);
				Vector2 alone = PublishFogShell();
				bool fogAlone = alone.y > alone.x;
				CloudShellBottom = fogAlone ? alone.x : 0f;
				CloudShellTop = fogAlone ? alone.y : 0f;
				Shader.SetGlobalVector(CloudLayerId, fogAlone
					? new Vector4(alone.x, alone.y, Mathf.Max(10000f, sample.Planet.RadiusMetres), 0f)
					: Vector4.zero);
				return;
			}
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = timeline.BodyOverride != null ? timeline.BodyOverride : SceneTime.BodyOf(settings);
			// The world's own air for the scales — never an offset to it, which would move them.
			PlanetAir plain = PlanetAir.For(system, body);
			PlanetAir planet = sample.Planet;
			WindBelts belts = WindBelts.For(plain);
			if (!cloudScalesValid || cloudScalesBody != body || !Mathf.Approximately(cloudScalesLatitude, latitude))
			{
				cloudScales = CloudClimate.ScalesFor(plain, latitude, WeatherDriver.WorldSeed);
				cloudScalesBody = body;
				cloudScalesLatitude = latitude;
				cloudScalesValid = true;
			}

			// The air over the whole visible sky, place by place.
			uint tick = (uint)context.Tick;
			double worldHours = worldSeconds / 3600.0;
			var inputs = new CloudAirMap.Inputs
			{
				Timeline = timeline,
				Settings = settings,
				Planet = planet,
				Offsets = (settings != null ? settings.AuthoredAir : default) + timeline.AirAt(tick),
				Atmosphere = body != null ? body.Atmosphere : AtmosphereKind.Standard,
				Belts = belts,
				WorldSeconds = worldSeconds,
				Season01 = season01,
				LocalTime01 = system != null && body != null ? (float)CelestialMath.LocalTime01(system, body, worldHours, timeline.LongitudeDegrees) : 0.5f,
				Tick = tick,
			};
			if (system != null && body != null)
			{
				double day = CelestialMath.SolarDayHours(system, body);
				if (!double.IsInfinity(day) && day > 1e-6)
				{
					inputs.DaylightShare = (float)(CelestialMath.DaylightHours(system, body, worldHours, timeline.LatitudeDegrees) / day);
					inputs.HasDaylight = true;
				}
			}
			cloudAir ??= new CloudAirMap();
			cloudAir.Update(inputs, viewerAt, Time.deltaTime);
			CloudAirMap.Probe(inputs, new Vector2(viewerAt.x, viewerAt.z), out WeatherDriver.Synoptic air, out AirColumn column);
			CloudViewerAir = air;
			CloudViewerColumn = column;
			CloudClimate.Bands(cloudScales, planet, air, column, cloudAir.Extremes, cloudBands);

			// The formations: where in this sky the banks and the gaps are, computed per sample in the
			// shader from the same seed as the server's. The air map carries the systems' cover
			// without them, so nothing is taken back out at the camera.
			float mesoAmplitude = WeatherDriver.MesoscaleAmplitude * WeatherDriver.FormationScale(inputs.Atmosphere);
			float mesoContrast = WeatherDriver.MesoscaleContrast(air.Instability);
			double mesoPeriod = WeatherDriver.MesoscaleMetres * WeatherDriver.MesoscalePeriodTiles;
			Shader.SetGlobalVector(CloudMesoId, new Vector4((float)WrapMetres(driftX, mesoPeriod), (float)WrapMetres(driftY, mesoPeriod), 0f, mesoContrast));
			Shader.SetGlobalVector(CloudMesoParamsId, new Vector4(mesoAmplitude, WeatherDriver.MesoscaleMetres, WeatherDriver.MesoscalePeriodTiles, 0f));
			Shader.SetGlobalInt(CloudMesoSeedId, unchecked((int)(WeatherDriver.WorldSeed ^ WeatherDriver.MesoscaleSeedMix)));
			// Where, within a sky, the air goes all the way up: the tower lattice, the server's twin.
			// How far a tower can climb there is the air map's to say.
			double towerPeriod = WeatherDriver.TowerMetres * WeatherDriver.TowerPeriodTiles;
			Shader.SetGlobalVector(CloudColumnId, new Vector4(1f, WeatherDriver.TowerMetres, WeatherDriver.TowerPeriodTiles, 0f));
			Shader.SetGlobalVector(CloudTowerDriftId, new Vector4(
				(float)WrapMetres(driftX, towerPeriod), (float)WrapMetres(driftY, towerPeriod), 1f, 0f));
			Shader.SetGlobalInt(CloudTowerSeedId, unchecked((int)(WeatherDriver.WorldSeed ^ WeatherDriver.TowerSeedMix)));
			CloudTowerAtCamera = air.Tower;

			// What hangs below a column's base: the rain haze under a wet one, from the frame the
			// presentation is actually showing, which is eased — at the extinction the world's own fog
			// is given for what is falling. The column's ground fog (x, z) is gone: the fog is the fog
			// layer's, walked by the march in a shell of its own (PublishFogShell).
			Shader.SetGlobalVector(CloudSubId, new Vector4(0f, shownRain, 0f, shownFallingExtinction));
			Shader.SetGlobalVector(CloudViewerId, new Vector4(viewerAt.x, viewerAt.y, viewerAt.z, 0f));
			cloudTerrain ??= new CloudTerrainMap();
			cloudTerrain.Update(viewerAt);
			PublishFlow(axis);
			Vector2 fogShell = PublishFogShell();
			bool fogDrawn = fogShell.y > fogShell.x;
			// Over, or round: the Froude number U/(N h). N is the air's own buoyancy frequency,
			// N² = (g/T)(Γd − Γ), from how much more slowly it cools with height than a dry parcel
			// lifted through it would — a stable morning resists, an unsettled afternoon barely does.
			float n2 = planet.Gravity / Mathf.Max(20f, column.SurfaceKelvin) * Mathf.Max(0f, column.DryLapse - column.EnvironmentLapse);
			float buoyancy = Mathf.Max(0.002f, Mathf.Sqrt(n2));
			CloudClimbHeight = Mathf.Max(1f, cloudWindSpeed) / buoyancy;
			// The floor: a mountain's height to the air is measured from the land round it. y: how much of
			// the climb the steering round the terrain honours (CloudSteeringClimbShare).
			Shader.SetGlobalVector(CloudFlowId, new Vector4(CloudClimbHeight, CloudClimbHeight * CloudSteeringClimbShare, cloudTerrain.Floor, 0f));

			// The drift in the frame the noise is read in: along the axis and across it.
			var across = new Vector2(-axis.y, axis.x);
			double driftAlong = driftX * axis.x + driftY * axis.y;
			double driftAcross = driftX * across.x + driftY * across.y;

			float lowest = float.MaxValue, highest = 0f;
			int count = 0;
			// Where the storms' own cloud is (the weather map): their towers and lowered bases belong
			// to the column, their anvils to the high ice. Each band keeps its own floor and top — its
			// noise and its profile are laid out in them — and is handed a wider shell as well, which
			// is what the march skips empty air by: a storm's base below the lowest base the air makes
			// anywhere, its tower above the highest, and an anvil at the tropopause, were all air the
			// march stepped straight over.
			WeatherMap.Extremes storms = weatherMap != null ? weatherMap.StormExtremes : default;
			for (int i = 0; i < CloudClimate.BandCount && count < MaxCloudLayers; i++)
			{
				CloudBand band = cloudBands[i];
				bool anvils = band.Regime == CloudRegime.Ice && storms.Anvils;
				if (!band.Present)
				{
					if (!anvils)
					{
						continue;
					}
					// No cirrus in this air, but a storm has driven its ice up to the tropopause all
					// the same: the band stands where its anvils are, and its own cover stays what the
					// air map says.
					band.Bottom = storms.AnvilBottom;
					band.Top = Mathf.Max(storms.AnvilBottom + 100f, storms.AnvilTop);
					band.Thinnest = Mathf.Max(150f, 0.5f * storms.AnvilDepth);
					band.Extinction = Mathf.Max(band.Extinction, 1e-6f);
				}
				float shellBottom = band.Bottom, shellTop = band.Top;
				if (band.Column && storms.Any)
				{
					shellBottom = Mathf.Min(shellBottom, storms.LowestFloor);
					shellTop = Mathf.Max(shellTop, storms.HighestTop);
					band.MaxCoverage = 1f;
				}
				if (anvils)
				{
					shellBottom = Mathf.Min(shellBottom, storms.AnvilBottom);
					shellTop = Mathf.Max(shellTop, storms.AnvilTop);
					band.MaxCoverage = 1f;
				}
				// A hill that reaches the condensation level makes its own cloud on its windward face
				// whatever the day's cover (FishCloudDensityAt's cap cloud), and the march skips a band
				// whose most cover in view is nothing: so where the ground stands into the deck's base,
				// the band is never skipped for want of cover.
				if (band.Column && cloudFlow != null && cloudFlow.HighestRock > band.Bottom)
				{
					band.MaxCoverage = Mathf.Max(band.MaxCoverage, CapCover);
				}
				float thickness = Mathf.Max(1f, band.Top - band.Bottom);
				float verticalScale = band.Column
					? band.NoiseScale * 0.2f * band.VerticalScale / thickness
					: band.VerticalScale;
				lowest = Mathf.Min(lowest, shellBottom);
				highest = Mathf.Max(highest, shellTop);
				layerA[count] = new Vector4(band.Bottom, band.Top, band.MaxCoverage, band.Extinction);
				layerB[count] = new Vector4(band.NoiseScale, band.DetailScale, band.DetailStrength, Mathf.Max(1f, band.Stretch));
				// Rain in the units and 2 on top for the band that grows storms.
				layerC[count] = new Vector4(band.WindScale, band.BaseSoftness, band.TopSoftness,
					(band.CarriesRain ? cloudPrecipitation : 0f) + (band.GrowsStorms ? 2f : 0f));
				// y, z the shell: the band's own floor and top, widened by its storms'.
				layerD[count] = new Vector4(band.Convection, shellBottom, shellTop, verticalScale);
				// This band's drift, wrapped to its own period so the float is exact and the wrap is
				// invisible: 42 tiles along the wind (the warp's period, which the shape's and the
				// lift's divide) stretched by the band's own stretch, 42 across, and the detail
				// volume's single tile — in the same wind frame, drawn out along it by the same stretch,
				// so cirrus's fine structure is fibres along the wind like its shape and not round
				// blobs on the world's axes (it used to be read on the world axes, unstretched). All in
				// double until the last moment.
				double scale = band.WindScale;
				double alongPeriod = band.NoiseScale * Mathf.Max(1f, band.Stretch) * 42.0;
				double acrossPeriod = band.NoiseScale * 42.0;
				Vector2 detailPeriod = CloudClimate.DetailDriftPeriod(band.DetailScale, Mathf.Max(1f, band.Stretch));
				layerE[count] = new Vector4(
					(float)WrapMetres(driftAlong * scale, alongPeriod),
					(float)WrapMetres(driftAcross * scale, acrossPeriod),
					(float)WrapMetres(driftAlong * scale, detailPeriod.x),
					(float)WrapMetres(driftAcross * scale, detailPeriod.y));
				// x which regime (the shader reads its cover from the air map by it), y the mean
				// diameter of its drops (0 for ice: how it scatters), z 1 for the column, w the
				// thinnest this regime's cloud gets, for the stride guard.
				layerF[count] = new Vector4((int)band.Regime, band.DropletDiameter, band.Column ? 1f : 0f, band.Thinnest);
				// Where this band's sun is worked out for (SetCloudLight, once the light is chosen): an
				// ordinary cloud's middle for the column, the middle of a layer.
				layerSunHeight[count] = band.Column ? 0.5f * (column.Base + Mathf.Max(column.Base, column.Top)) : 0.5f * (band.Bottom + band.Top);
				count++;
			}
			for (int i = count; i < MaxCloudLayers; i++)
			{
				layerA[i] = Vector4.zero;
				layerB[i] = Vector4.zero;
				layerC[i] = Vector4.zero;
				layerD[i] = Vector4.zero;
				layerE[i] = Vector4.zero;
				layerF[i] = Vector4.zero;
				layerSunHeight[i] = 0f;
			}
			cloudBandCount = count;
			Shader.SetGlobalVectorArray(CloudLayerAId, layerA);
			Shader.SetGlobalVectorArray(CloudLayerBId, layerB);
			Shader.SetGlobalVectorArray(CloudLayerCId, layerC);
			Shader.SetGlobalVectorArray(CloudLayerDId, layerD);
			Shader.SetGlobalVectorArray(CloudLayerEId, layerE);
			Shader.SetGlobalVectorArray(CloudLayerFId, layerF);
			Shader.SetGlobalInt(CloudLayerCountId, count);

			// The wind with height over the viewer, for how far a cloud leans: the weather of the
			// moment (WindProfile.Of), since a lean does not accumulate — unlike the drift, which is the
			// world's and must never change with the weather. The cloud base tops the mixed layer; a
			// heap rises at its own updraught, at least a fair-weather cumulus's; above the ordinary
			// cloud top only towers stand, rising at a storm's (FishCloudLean). It was a lean of the
			// carve alone, by two minutes of the gusting surface wind.
			WindProfile windNow = WindProfile.Of(cloudWindSpeed, column, planet, latitude, belts);
			CloudLeanShear = windNow.Shear;
			float heapRise = Mathf.Max(CloudClimate.HeapUpdraught, column.Updraft);
			Shader.SetGlobalVector(CloudShearId, new Vector4(windNow.Shear, heapRise, column.Base, Mathf.Max(column.Base, column.Top)));
			// The ice: where every drop has frozen, what a storm's glaciated cloud takes out against its
			// liquid, how fast cirrus ice falls on this world (its fall streaks), and the tropopause.
			Shader.SetGlobalVector(CloudIceId, new Vector4(column.IceLevel, CloudClimate.GlaciatedRatio(cloudBands[0].DropletDiameter, planet),
				cloudScales.IceFallSpeed, column.Tropopause));
			// How much of the light falling on the cloud overhead reaches the viewer through it, by
			// diffusion — for readouts, and for anything that must know how dark it is under the sky.
			// Through the gaps everything, through the cloud what diffuses through it.
			float overheadCover = Mathf.Clamp01(cloudBands[0].Coverage);
			CloudOverheadDepth = CloudClimate.ColumnOpticalDepth(column, planet);
			float diffuseThrough = CloudClimate.DiffuseTransmission(CloudOverheadDepth, cloudBands[0].Asymmetry);
			CloudDiffuseOverhead = Mathf.Lerp(1f, diffuseThrough, overheadCover);
			CloudOverheadCover = overheadCover;
			CloudOverheadAsymmetry = cloudBands[0].Asymmetry;
			// The sky's light under the deck: through the gaps all of it; through the cloud what
			// diffuses through, and again what the ground sends back up and the base returns — the
			// ground and the base are two mirrors facing, and the light between them sums as
			// 1/(1 − ρ_ground·R_base), with R_base = 1 − T_d for a cloud that absorbs nothing. About a
			// fifth more over land, nearly half again over snow, a few per cent over the sea.
			float bounce = 1f / Mathf.Max(0.05f, 1f - CloudClimate.GroundAlbedo(planet) * (1f - diffuseThrough));
			CloudSkyThrough = Mathf.Lerp(1f, diffuseThrough * bounce, overheadCover);

			// The shell the march runs through, and the deck a reprojection has to be right about. Down
			// to the ground when there is fog or rain haze under the base to draw; from the lowest
			// storm base to the highest storm tower when there is a storm about (the bands' shells).
			// The ground mist lies on the ground, so with any about the march's floor goes down to it, as
			// it does for the fog — and the steadying's "below every cloud" shortcut with it.
			bool mistOn = PublishMist(column, viewerAt, fogDrawn);
			CloudShellBottom = CloudStackFloor(lowest, shownRain >= 0.01f, fogDrawn || mistOn, fogDrawn ? Mathf.Min(fogShell.x, -2f) : -2f);
			CloudShellTop = Mathf.Max(Mathf.Max(CloudShellBottom + 100f, highest), fogDrawn ? fogShell.y : 0f);
			CloudLayerCentre = column.Base + 600f;
			// The bands curve down to the horizon over the radius of the world they are on.
			float radiusMeters = Mathf.Max(10000f, planet.RadiusMetres);
			Shader.SetGlobalVector(CloudLayerId, new Vector4(CloudShellBottom, CloudShellTop, radiusMeters, cloudBands[0].Coverage));
		}

		/// <summary>How ready the air over open ground is to make mist, 0..1 (<see cref="GroundMist.Potential"/>).</summary>
		public float MistPotential { get; private set; }
		/// <summary>How ready the most favoured spot could be — a hollow under trees at the water's edge (<see cref="GroundMist.BestPotential"/>).</summary>
		public float MistBestPotential { get; private set; }

		/// <summary>
		/// Works out how far short of saturation the air over open ground stays, from the air over the
		/// camera and the weather being shown (<see cref="GroundMist"/>), keeps the fine ground round the
		/// camera that the mist lies on, with its water, trees and terrain shadow (<see cref="MistGroundMap"/>) —
		/// kept while a fog is drawn as well, which is shaded by the same — and publishes both. True when any
		/// spot might hold mist.
		/// </summary>
		private bool PublishMist(in AirColumn column, Vector3 viewerAt, bool fogDrawn)
		{
			WeatherPresentation presentation = WeatherPresentation.Instance;
			WeatherFrame weather = presentation != null ? presentation.Shown : WeatherFrame.Clear;
			CelestialState state = cycle != null ? cycle.State : null;
			float spread = column.SurfaceKelvin - column.DewPointKelvin;
			float wind = Mathf.Clamp01(weather[WeatherChannel.WindSpeed]) * 30f;
			float rain = Mathf.Clamp01(weather[WeatherChannel.Precipitation] * weather[WeatherChannel.RainWeight]);
			float clear = 1f - Mathf.Clamp01(weather[WeatherChannel.CloudCover] * (0.4f + 0.6f * weather[WeatherChannel.CloudDensity]));
			float sunAltitude = state != null ? state.SunAltitude : 45f;
			float wetness = weather[WeatherChannel.WetnessTarget];
			float deficit = GroundMist.Deficit(spread, wind, wetness, rain, clear, sunAltitude);
			float stirred = GroundMist.Stirred(wind);
			float night = GroundMist.NightCalm(wind, clear, sunAltitude);
			MistPotential = GroundMist.Potential(spread, wind, wetness, rain, clear, sunAltitude);
			MistBestPotential = GroundMist.BestPotential(deficit, stirred, night);
			bool on = MistBestPotential > 0.001f;
			// Kept for the fog too: the fine ground round the camera carries the near terrain shadow every fog
			// is shaded by (FishTerrainSunlit), mist or none.
			if (on || fogDrawn)
			{
				mistGround ??= new MistGroundMap();
				mistGround.Update(viewerAt, cloudSunDirection, cloudFlow);
			}
			Shader.SetGlobalVector(MistId, on ? new Vector4(deficit, stirred, night, MistRange) : Vector4.zero);
			return on;
		}

		/// <summary>
		/// Builds the scene's flow round its terrain once (<see cref="CloudFlowField"/>) and publishes
		/// what the fog needs to read it: the prevailing wind's axis, and how high the fog's own air
		/// can climb — its wind over its inversion's stiffness (<see cref="FogBuoyancy"/>).
		/// </summary>
		private void PublishFlow(Vector2 axis)
		{
			cloudFlow ??= new CloudFlowField();
			// The light that leads the sky — the sun, or the moon after it sets — which the fog and the mist
			// are lit by, for the terrain's shadow over the scene.
			cloudFlow.Update(cloudSunDirection);
			FogLayerView fog = FogLayerView.Current;
			float fogClimb = Mathf.Max(1f, fog.WindSpeed) / FogBuoyancy;
			Shader.SetGlobalVector(FogFlowId, new Vector4(axis.x, axis.y, fogClimb, 0f));
		}

		/// <summary>
		/// Publishes where the cloud march walks the fog (<see cref="FogLayerView.Shell"/>) over the
		/// ground the terrain map holds, and returns it: (0, −1) when there is no fog.
		/// </summary>
		private Vector2 PublishFogShell()
		{
			FogLayerView fog = FogLayerView.Current;
			Vector2 shell = cloudTerrain != null
				? fog.Shell(cloudTerrain.HighestGround, cloudTerrain.HighestPooled)
				: new Vector2(0f, -1f);
			bool on = shell.y > shell.x;
			Shader.SetGlobalVector(FogLayerView.ShellId, on ? new Vector4(shell.x, shell.y, 1f, 0f) : Vector4.zero);
			return shell;
		}

		/// <summary>
		/// The floor of the shell the cloud march runs through, m: the lowest band anywhere in view, the
		/// ground under a wet base whose rain haze reaches it, and the fog's own floor — sea level, under
		/// every ground it can lie on — while there is a fog.
		/// </summary>
		/// <remarks>
		/// With neither rain haze nor fog, nothing is under the lowest base, and the march starts there:
		/// the whole of the air between the ground and the clouds is crossed in one step. A fog used to
		/// take the floor to the ground and make every height under the cloud tops "possible", so every
		/// ray walked the kilometre of empty air between a fog tens of metres deep and the deck; now the
		/// fog has its own shell, and the air between is skipped.
		/// </remarks>
		/// <param name="lowestCloud">The lowest any band's shell reaches, m; float.MaxValue for none.</param>
		/// <param name="rainHangs">Whether rain haze hangs under a wet base down to the ground.</param>
		/// <param name="fogDrawn">Whether there is a fog for the march to walk.</param>
		/// <param name="fogFloor">The fog's floor (<see cref="FogLayerView.Shell"/>), m.</param>
		public static float CloudStackFloor(float lowestCloud, bool rainHangs, bool fogDrawn, float fogFloor)
		{
			float floor = rainHangs || lowestCloud == float.MaxValue ? 0f : Mathf.Max(0f, lowestCloud);
			return fogDrawn ? Mathf.Min(floor, fogFloor) : floor;
		}

		/// <summary>A drift reduced to one period, in double, so that the float it becomes is exact.</summary>
		private static double WrapMetres(double metres, double period)
		{
			return period <= 0.0 ? metres : metres - System.Math.Floor(metres / period) * period;
		}

		/// <summary>0..1: how strongly a tower stands over the camera right now.</summary>
		public float CloudTowerAtCamera { get; private set; }

		/// <summary>How fast the wind grows with height over the viewer now, (m/s) per m: what leans the clouds.</summary>
		public float CloudLeanShear { get; private set; }

		/// <summary>The optical depth of an ordinary low cloud over the viewer, as the air makes it.</summary>
		public float CloudOverheadDepth { get; private set; }

		/// <summary>
		/// How much of the sky's light reaches the viewer through the low cloud overhead, 0..1: all of it
		/// through the gaps, and through the cloud what diffuses through
		/// (<see cref="CloudClimate.DiffuseTransmission"/>) — about 0.15 under a cumulus, a few hundredths
		/// under a storm. What an exposure that adapts to the sky would read.
		/// </summary>
		public float CloudDiffuseOverhead { get; private set; } = 1f;

		/// <summary>How much of the sky the low cloud covers over the viewer, 0..1.</summary>
		public float CloudOverheadCover { get; private set; }

		/// <summary>The low cloud's drops' asymmetry, g: how much of each bounce keeps going forward.</summary>
		public float CloudOverheadAsymmetry { get; private set; } = 0.86f;

		/// <summary>
		/// How much of the sky's own light reaches the ground under the low cloud, 0..1 and a little
		/// over: <see cref="CloudDiffuseOverhead"/> with the light the ground and the cloud base trade
		/// between them. What the scene's ambient is scaled by.
		/// </summary>
		public float CloudSkyThrough { get; private set; } = 1f;

		/// <summary>
		/// How much of a sun or moon at an altitude reaches the ground, on average over the view: all of
		/// it through the gaps, and through the low cloud what comes straight through and what is
		/// scattered through (<see cref="CloudClimate.GroundTransmission"/>, the cloud shadow's own).
		/// </summary>
		public float CloudLightThrough(float altitudeDegrees)
		{
			float mu = Mathf.Max(0.05f, Mathf.Sin(Mathf.Max(0f, altitudeDegrees) * Mathf.Deg2Rad));
			float through = CloudClimate.GroundTransmission(CloudOverheadDepth / mu, mu, CloudOverheadAsymmetry);
			return Mathf.Lerp(1f, through, CloudOverheadCover);
		}

		/// <summary>The air over the viewer, at sea level, as the clouds were drawn from it.</summary>
		public WeatherDriver.Synoptic CloudViewerAir { get; private set; }

		/// <summary>The viewer's air column: cloud base, freezing level, how far a cloud can grow.</summary>
		public AirColumn CloudViewerColumn { get; private set; }

		/// <summary>The world's cloud scales: what its clouds are sized and moved by.</summary>
		public CloudClimate.Scales CloudScales => cloudScales;

		private float shownRain;
		private float shownFallingExtinction;

		/// <summary>The bottom and top of the whole stack of bands, in metres.</summary>
		public float CloudShellBottom { get; private set; }
		public float CloudShellTop { get; private set; } = 12000f;

		private readonly Vector4[] layerE = new Vector4[MaxCloudLayers];
		private readonly Vector4[] layerF = new Vector4[MaxCloudLayers];
		private readonly CloudBand[] cloudBands = new CloudBand[CloudClimate.BandCount];
		private int cloudBandCount;
		private CloudAirMap cloudAir;
		private CloudClimate.Scales cloudScales;
		private bool cloudScalesValid;
		private WorldBody cloudScalesBody;
		private float cloudScalesLatitude;
		private float cloudBackgroundCover;
		private float cloudPrecipitation;
		private Vector2 cloudWind = Vector2.up;
		private float cloudWindSpeed = 6f;
		private Vector3 cloudSunDirection = Vector3.up;

		/// <summary>The renderer's own calibration of the baked shape noise: where its cut sits for a cover.</summary>
		/// <remarks>
		/// Not weather and not a setting: these describe the baked noise volume. It does not span
		/// 0..1 — measured, it runs 0.33 to 0.76 about 0.57 — so the cut that keeps a given share of
		/// it has to sit in that narrow window. Re-measure them (noisestats) if the volume is re-baked.
		/// <para>
		/// Both cuts rose by 0.020 (were 0.662 and 0.575) when a cloud's edge became a physical mixing
		/// shell a hundred metres wide outside a cut that holds the cloud's whole water, instead of a
		/// softness 0.14 of the noise wide (~800 m) inside it: the cut now marks where the visible edge
		/// is. Calibrated on a CPU port of the density at the probe's own camera, with the tower
		/// lattice's cover, so each sky keeps the old edge's "any cloud" share (the probe's floor): the
		/// shift that does so is 0.0195 in dry air (RH 0.37), 0.023 at RH 0.64 and 0.029 at 0.9 — the
		/// dry end is taken, so no sky has less cloud than it had and a humid one a little more, which
		/// is what humid air does to an edge. (A first pass shifted them 0.0088 with the shell inside the
		/// cut and lost every small cumulus in a dry sky — scattered fell from 7 % to 1 %.)
		/// </para>
		/// </remarks>
		public const float CloudCutClear = 0.682f;
		public const float CloudCutFull = 0.595f;
		public const float CloudCutBend = 0.15f;
		/// <summary>
		/// How fast the baked shape noise changes across the ground, where the cut meets it: its median
		/// gradient, 1.6 per tile (measured off the volume at mip 0, 1.67e-4 per metre on the 9.6 km
		/// tile). What turns a mixing shell in metres into the noise's own units, softness = this × width
		/// ÷ tile. Re-measure it with the cuts if the volume is re-baked.
		/// </summary>
		public const float CloudCutGradient = 1.605f;
		/// <summary>
		/// How much of a marched texel's cone the finest detail is drawn down to: half. The steadying
		/// looks through a different place in each texel every frame and rebuilds the screen from sixteen
		/// of them, so what is half a texel across still lands on the right pixels.
		/// </summary>
		public const float CloudDetailConeShare = 0.5f;
		/// <summary>How far the shape lookup is bent so the noise does not visibly repeat.</summary>
		public const float CloudShapeWarp = 0.17f;

		private static readonly int CloudDiagId = Shader.PropertyToID("_FishCloudDiag");
		private static readonly int CloudDiagLightId = Shader.PropertyToID("_FishCloudDiagLight");
		private static readonly int CloudDiagScaleId = Shader.PropertyToID("_FishCloudDiagScale");
		private static readonly int CloudStepTauId = Shader.PropertyToID("_FishCloudStepTau");
		private static readonly int CloudFixId = Shader.PropertyToID("_FishCloudFix");
		private static readonly int CloudFixBId = Shader.PropertyToID("_FishCloudFixB");
		private static readonly int CloudBaseId = Shader.PropertyToID("_FishCloudBase");

		/// <summary>
		/// The profile's cloud diagnostics this frame (<see cref="VolumetricCloudSettings.Diagnostics"/>),
		/// for the cloud feature's reconstruction switches; never null.
		/// </summary>
		public VolumetricCloudDiagnostics CloudDiagnostics { get; private set; } = new VolumetricCloudDiagnostics();

		private void SetCloudGlobals(CelestialState state, in SkySample sample, in WeatherFrame weather, in WeatherFrame background, WeatherRenderProfile profile, WeatherTierSettings tier, in WeatherContext context)
		{
			VolumetricCloudSettings clouds = profile.Clouds;
			VolumetricCloudDiagnostics diagnostics = clouds.Diagnostics ?? (clouds.Diagnostics = new VolumetricCloudDiagnostics());
			CloudDiagnostics = diagnostics;
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
				Temporal = diagnostics.TemporalFor(tier.CloudTemporal),
				TemporalBlend = clouds.TemporalBlend,
				HistoryScale = tier.CloudHistoryScale,
			};
			CloudFarDistance = clouds.MaxDistance;
			// The inspector's diagnostic switches, live every frame; all zeros is the clouds as they
			// ship, so a shader that never sees these draws the default (VolumetricCloudDiagnostics).
			Shader.SetGlobalVector(CloudDiagId, diagnostics.MarchVector);
			Shader.SetGlobalVector(CloudDiagLightId, diagnostics.LightVector);
			Shader.SetGlobalVector(CloudDiagScaleId, diagnostics.ScaleVector);
			Shader.SetGlobalVector(CloudStepTauId, diagnostics.StepVector);
			Shader.SetGlobalVector(CloudFixId, diagnostics.FixVector);
			Shader.SetGlobalVector(CloudFixBId, diagnostics.FixVectorB);
			Shader.SetGlobalVector(CloudBaseId, diagnostics.BaseVector);
			if (!cloudsReady || airless)
			{
				// Nothing to march: make sure no stale coverage is left behind — nor a fog the march is
				// no longer there to draw (the fallback passes draw it: FogLayerView.DrawnByClouds).
				Shader.SetGlobalVector(CloudLayerId, Vector4.zero);
				Shader.SetGlobalInt(CloudLayerCountId, 0);
				Shader.SetGlobalVector(FogLayerView.ShellId, Vector4.zero);
				return;
			}

			Shader.SetGlobalTexture(CloudShapeTexId, profile.CloudShape);
			Shader.SetGlobalTexture(CloudDetailTexId, profile.CloudDetail);
			// The background forecast, NOT the weather where the camera happens to stand: for the
			// readouts and the sky's own brightness. The clouds themselves come from the air map.
			cloudBackgroundCover = Mathf.Clamp01(background[WeatherChannel.CloudCover]);
			cloudPrecipitation = Mathf.Clamp01(background[WeatherChannel.Precipitation]);
			// x how much of a marched texel's cone the finest detail is drawn down to: each octave of
			// the eddies is drawn while the screen can resolve it and fades to its mean after, so the
			// detail no longer has a fixed distance (it was faded out whole between 2.5 and 11.5 km,
			// nearer than most of the clouds in view). y unused. Option C under trial raises the share to its
			// footprint floor (CloudOptions.DetailConeShare, FishCloudsFeature.cs); off, it is the share as it was.
			// The diagnostics' Detail LOD Scale multiplies it (1 as shipped).
			Shader.SetGlobalVector(CloudShapeParamsId, new Vector4(FishCloudsFeature.Options.DetailConeShare(CloudDetailConeShare) * Mathf.Clamp(diagnostics.DetailLodScale, 0.25f, 8f), 0f, CloudShapeWarp, 1f));
			// y how fast the noise changes across the ground at the cut, which turns the mixing shell's
			// width in metres into the noise's units (it was a softness in those units, from humidity).
			Shader.SetGlobalVector(CloudCoverageId, new Vector4(CloudCutClear, CloudCutGradient, CloudCutFull, CloudCutBend));
			// The noise is drawn out along the steady prevailing wind of this latitude on this world —
			// never the gusting one the weather reports: the lookup is rotated about the world origin
			// on it, which is harmless only while it does not move.
			float latitude = state != null ? (float)state.Latitude : 0f;
			WorldBody body = context.Timeline != null && context.Timeline.BodyOverride != null ? context.Timeline.BodyOverride : SceneTime.BodyOf(context.Settings);
			WindBelts belts = WindBelts.For(PlanetAir.For(SolarSystemProfile.Active, body));
			Vector2 wind = WeatherShaderGlobals.WindDirection(weather[WeatherChannel.WindHeading]);
			float speed = Mathf.Lerp(2f, 26f, weather[WeatherChannel.WindSpeed]);
			Vector2 axis = WeatherDriver.PrevailingWind(latitude, belts);
			if (axis.sqrMagnitude < 1e-6f)
			{
				axis = Vector2.up;
			}
			cloudWind = wind;
			cloudWindSpeed = speed;
			// Where the air has got to by now, worked out from the world clock rather than added up
			// frame by frame, and the same drift the weather field is sampled through, so the cloud
			// overhead and the weather underneath are the same air. Exact, in double: each band
			// wraps it to its own period on the way to the shader.
			WeatherDriver.DriftExact(WeatherDriver.WorldSeed, latitude, worldSeconds, belts, out double driftX, out double driftY);
			cloudDrift = WeatherDriver.Drift(WeatherDriver.WorldSeed, latitude, worldSeconds, belts);
			Shader.SetGlobalVector(CloudWindId, new Vector4(cloudDrift.x, cloudDrift.y, 2.5f, 1f));
			Shader.SetGlobalVector(CloudWindDirId, new Vector4(axis.x, axis.y, speed, 0f));
			shownRain = Mathf.Clamp01(weather[WeatherChannel.Precipitation]);
			shownFallingExtinction = AirPhysics.PrecipitationExtinction(shownRain, weather[WeatherChannel.RainWeight],
				weather[WeatherChannel.SnowWeight], weather[WeatherChannel.HailWeight], weather[WeatherChannel.AshWeight], weather[WeatherChannel.SandWeight]);
			SolarSystemProfile solar = SolarSystemProfile.Active;
			float season01 = CelestialMath.Season01(solar, state != null ? state.Observer : null, worldSeconds / 3600.0);
			Camera viewCamera = TargetCamera != null ? TargetCamera : Camera.main;
			Vector3 viewerAt = viewCamera != null ? viewCamera.transform.position : transform.position;
			// The bands themselves, and the shell they add up to.
			SetCloudLayers(context, axis, driftX, driftY, latitude, season01, worldSeconds, viewerAt);
			PlanetAir planet = context.Sample.Planet;
			// The light march's steps, and the column's drops' asymmetry for readers that want one
			// figure; the march itself scatters by each band's own drops (FishCloudPhaseMie). y, the sky's
			// share of the ambient, is set with the light below.

			// What lights the clouds: the sun while it is up, the moon after it sets. A sun below
			// the horizon must not keep lighting them, or a midnight sky glows like a sunset.
			float sunUp = Mathf.Clamp01(state.SunAltitude / 6f + 0.35f);
			Vector3 lightDirection = state.Sun >= 0 ? state.SunDirection : new Vector3(0.3f, 0.9f, 0.4f).normalized;
			Color lightColour = sample.SunLight * sample.SunIntensity;
			float strength = sunUp;
			// The moon that is lighting the sky NOW, not the one with the biggest disc: the clouds have
			// one light to be lit from, and with the big moon down it has to be the one that is up.
			float moonLight = 0f;
			bool moonLit = false;
			int brightest = sunUp < 0.5f ? BrightestMoon(state, sample, 5f, out moonLight) : -1;
			if (brightest >= 0)
			{
				SkyBodyState moonBody = state.Bodies[brightest];
				if (moonLight > strength * 0.5f)
				{
					lightDirection = moonBody.Direction;
					lightColour = sample.MoonLight;
					strength = Mathf.Max(sunUp, moonLight);
					moonLit = true;
				}
			}
			cloudSunDirection = lightDirection;
			Shader.SetGlobalVector(CloudSunDirId, new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, strength));
			Shader.SetGlobalColor(CloudSunColorId, lightColour);

			// The sky's own light over a cloud: the upper hemisphere's radiance, in the units the world is
			// lit in — what a white surface facing up shows under this sky, which is exactly the light
			// that falls on a cloud's top (the march diffuses it down through the cloud). It was a mix
			// of the sample's painted-by-fit cloud colours, lerp(shadow, lit, 0.35) × 0.85, which stood
			// in for the light a cloud's inside never got. With nothing above the horizon it keeps a
			// floor of the night sky's own glow: an overcast night is dark, but it is still a sky with
			// shapes in it, not a blank field.
			Color skyLight = sample.AmbientSky;
			float night = 1f - Mathf.Clamp01(strength * 2f);
			Color glow = (sample.AmbientSky * 1.2f + new Color(0.010f, 0.012f, 0.018f)) * night;
			skyLight = new Color(Mathf.Max(skyLight.r, glow.r), Mathf.Max(skyLight.g, glow.g), Mathf.Max(skyLight.b, glow.b), 1f);
			Shader.SetGlobalColor(CloudAmbientId, skyLight);
			// How much of that ambient is the sky's real light on a cloud (CloudClimate.SkyShareOfAmbient):
			// the ambient is set to light a scene with no bounce light, half the sun at noon, and the real
			// clear sky is under a fifth of it — at 1 the clouds' shaded sides and bases were lit two to
			// three times over and thin cloud took the sky's colour. Times the diagnostics' Sky Ambient.
			// Never under the night's own glow: the floor that keeps an overcast night a sky with shapes.
			Color skyOnClouds = skyLight;
			if (!moonLit)
			{
				float lightAltitude = Mathf.Asin(Mathf.Clamp(lightDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
				float share = CloudClimate.SkyShareOfAmbient(lightAltitude, Luminance(lightColour) * strength, Luminance(sample.AmbientSky), planet.AirRelative);
				Color day = sample.AmbientSky * share;
				skyOnClouds = new Color(Mathf.Max(day.r, glow.r), Mathf.Max(day.g, glow.g), Mathf.Max(day.b, glow.b), 1f);
			}
			float skyShare = Luminance(skyLight) > 1e-5f ? Mathf.Clamp01(Luminance(skyOnClouds) / Luminance(skyLight)) : 1f;
			Shader.SetGlobalVector(CloudLightId, new Vector4(clouds.LightSteps, Mathf.Max(0.01f, skyShare), cloudBands[0].Asymmetry, 0f));
			// The sun's light is what the air let through to the ground; the moon's is not drawn
			// through the air at all (CloudClimate.LightGainAloft).
			SetCloudLight(planet, lightDirection, lightColour * strength, skyOnClouds * Mathf.Clamp(diagnostics.SkyAmbient, 0f, 2f), !moonLit);

			// No storm is handed over for the sky as a whole. A storm is a cell and stands where the
			// weather map puts it; the towers of an unstable day are the air map's. A scene-wide storm
			// figure — which counted a plain wind as stormy — raised every column in view to the
			// tropopause at once and closed a dry, windy sky into a grey veil thirteen kilometres deep.
			Shader.SetGlobalVector(CloudTypeParamsId, new Vector4(0f, 0f, 0f, cloudPrecipitation));

			// What distance does to a cloud: the air in front of it — its molecules and the dust and
			// salt in it, swollen by the humidity. Two e-foldings of that air is where the haze curve
			// is at its strongest: about 26 km on an ordinary day under our own air.
			Color haze = Color.Lerp(sample.Horizon, sample.Fog, 0.5f);
			float hazeDistance = AirPhysics.HazeDistance(planet.AirRelative, CloudViewerColumn.RelativeHumidity);
			Shader.SetGlobalVector(CloudHazeId, new Vector4(haze.r, haze.g, haze.b, Mathf.Max(2000f, hazeDistance)));
		}

		/// <summary>
		/// The light around the clouds that is not the sky's: the sun each band sees, the ground under
		/// them, and the air between them and the camera — all from this world's own air.
		/// </summary>
		/// <remarks>
		/// <list type="bullet">
		/// <item>Each band's sun is the ground's, raised by the air under the band
		/// (<see cref="CloudClimate.SunGainAloft"/>): at noon a few per cent, at dusk several times in the
		/// blue, so high cloud stays gold and white while the ground goes grey. A moon is the ground's
		/// as it is: its light was never drawn through the air, so there is no reddening to take back
		/// out (<see cref="CloudClimate.LightGainAloft"/>).</item>
		/// <item>The open ground sends back its albedo (<see cref="CloudClimate.GroundAlbedo"/>) of the sun
		/// and sky on it. That lights a cloud's base from below; the shader shades it by the cloud's
		/// own cover over it. It is a radiance in the same units as the sky's (<c>_FishCloudAmbient</c>):
		/// both are what a surface shows in the world's light units, the π of E = πL folded into the
		/// light's intensity the way Unity's own lights fold it — a white surface facing up under a sky
		/// of radiance L shows L, and one facing a light of intensity I shows I·cos. At night it is
		/// several times the sky's own light because the moon lights the ground and the night sky has
		/// next to none of its own, which is so; how much of it a base sees under a deck is the
		/// shader's to shade.</item>
		/// <item>The air between is this world's molecules — our own Rayleigh coefficients times how much
		/// air it has, thinning over its own scale height — and its haze, from the same humidity-swollen
		/// aerosol figure the sky's haze distance uses.</item>
		/// </list>
		/// </remarks>
		/// <summary>A colour's luminance (Rec. 709).</summary>
		private static float Luminance(Color c) => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;

		private void SetCloudLight(in PlanetAir planet, Vector3 lightDirection, Color light, Color skyLight, bool lightCrossedAir)
		{
			float humidity = CloudViewerColumn.RelativeHumidity;
			float aerosol = CloudClimate.AerosolExtinction(planet.AirRelative, humidity);
			float lightAltitude = Mathf.Asin(Mathf.Clamp(lightDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
			for (int i = 0; i < MaxCloudLayers; i++)
			{
				Vector3 gain = i < cloudBandCount ? CloudClimate.LightGainAloft(planet, aerosol, lightAltitude, layerSunHeight[i], lightCrossedAir) : Vector3.one;
				layerSun[i] = new Vector4(gain.x, gain.y, gain.z, 1f);
			}
			Shader.SetGlobalVectorArray(CloudLayerSunId, layerSun);

			float albedo = CloudClimate.GroundAlbedo(planet);
			float onGround = Mathf.Max(0f, lightDirection.y);
			Shader.SetGlobalVector(CloudGroundId, new Vector4(
				albedo * (light.r * onGround + skyLight.r),
				albedo * (light.g * onGround + skyLight.g),
				albedo * (light.b * onGround + skyLight.b), albedo));

			Vector3 air = CloudClimate.RayleighSeaLevel * Mathf.Max(0f, planet.AirRelative);
			Shader.SetGlobalVector(CloudAirExtinctionId, new Vector4(air.x, air.y, air.z, Mathf.Max(100f, planet.ScaleHeight)));
			Shader.SetGlobalVector(CloudAerosolId, new Vector4(aerosol, CloudClimate.AerosolScaleHeight(planet), 0f, 0f));
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
			float totality = DrawnEclipse.Totality;
			if (totality > 0.02f && TryFindBody(state, drawnEclipsingBody, out Vector3 eclipsing))
			{
				// The rays now come from around the body in front of the sun, and they are what is
				// left of the sun to see, so the deeper the eclipse the more they carry.
				direction = eclipsing;
				strength = Mathf.Max(strength, up * 0.35f) + totality * 0.5f * up;
				medium = Mathf.Max(medium, 0.5f * totality);
				GodRayEclipse = totality;
				colour = Color.Lerp(colour, new Color(1f, 0.93f, 0.85f), totality * 0.6f);
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
			// The halo is scattered sunlight, and there is as much of it as there is sun uncovered.
			sunColors[count] = new Vector4(color.r, color.g, color.b, sky.SunHalo * (1f - DrawnEclipse.Obscuration));
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
			// The light is dimmed as the eye would have it (an ordinary day until the last tenth), and
			// goes out at totality: the sample's own intensity is already lerped to nought there.
			// Through the cloud by what the cloud lets through (CloudLightThrough) — the average over
			// the view; the light that carries the cloud shadow has it put back below, since its cookie
			// does the same thing patch by patch. It was × (1 − 0.6·overcast) on top of the cookie:
			// the ground under a cloud was dimmed twice, and never lit by what diffuses through.
			float sunIntensity = sample.SunIntensity * (1f - eclipse * 0.9f) * CloudLightThrough(sunAltitude);
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
				float light = MoonLight(sample, body, 3f) * CloudLightThrough(body.AltitudeDegrees);
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
				companion.intensity = own * relative * CloudLightThrough(star.AltitudeDegrees);
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

			// The shadow is the volume's own, marched from the ground toward the light: what the cloud
			// lets through of it, straight and scattered, at full strength. What fills it besides is the
			// sky's light, which the scene's ambient carries.
			//
			// The light that carries it is dimmed by the cookie alone, patch by patch, so the average
			// it was given above comes off again; past the cookie's window the cookie fades to that
			// average rather than to full sun, so the land beyond the window is neither sunlit under
			// an overcast nor dimmed twice inside it.
			Light shadowed = leader != null ? leader : sun;
			bool cookie = tier.CloudShadows && DrawCloudShadows && profile.CloudMaterial != null && CloudsReady;
			float far = 1f;
			if (cookie && shadowed != null)
			{
				far = Mathf.Max(1e-3f, CloudLightThrough(Mathf.Asin(Mathf.Clamp(-shadowed.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg));
				float flashPart = shadowed == sun ? flash * 1.5f : 0f;
				shadowed.intensity = flashPart + (shadowed.intensity - flashPart) / far;
			}
			Vector3 viewer = TargetCamera != null ? TargetCamera.transform.position : transform.position;
			cloudShadows.Update(shadowed, profile.CloudMaterial, viewer, profile.Clouds.ShadowAreaMeters,
				1f, 8 * MaxCloudLayers, cookie, far);
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
			// The sky's light through the cloud (CloudSkyThrough): under a deck the sky the ground sees
			// is the cloud's base, lit by what diffuses through it. It was the clear sky's light ×
			// (1 − 0.3·overcast), which left the ground under a bright overcast base seven times too
			// dark for the sky over it. The sun's share of what comes through is in the sun's light.
			float dim = CloudSkyThrough * (1f - eclipse * 0.8f);
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
				// The same trilight for what Graphics.RenderMesh draws (FishAmbient.hlsl): no light-probe
				// coefficients reach such a draw, so SampleSH read zero there.
				Shader.SetGlobalColor(AmbientSkyId, skyColor);
				Shader.SetGlobalColor(AmbientEquatorId, equator);
				Shader.SetGlobalColor(AmbientGroundId, ground);
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
					curtains.Draw(presentation.Context.Timeline, (uint)presentation.Context.Tick, camera, profile.CurtainMaterial, profile, tier.Curtains, (float)(worldSeconds % 100000.0),
						presentation.Context.Sample, State != null ? State.Observer : null, weatherMap);
					// Tornadoes and dust devils, hung from the cloud base of the air they stand in.
					// With the same context the weather map reads, so the funnel hangs from the very wall
					// cloud the cloud shader draws.
					vortices.Draw(presentation.Context, (uint)presentation.Context.Tick, camera, profile.VortexMaterial, profile.VortexDebrisMaterial,
						profile.CloudShape, tier.Vortices, tier.VortexParticles, (float)(worldSeconds % 100000.0));
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
			// w: the world's radius in thousands of km, so a body's distance can be set against where
			// the ring is along the line to it — a moon INSIDE the ring's radius is not behind the ring.
			Shader.SetGlobalVector(OwnRingSunId, new Vector4(sun.x, sun.y, sun.z, observer.SkyRadiusKm * 0.001f));
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
		private readonly Vector4[] occluderRings = new Vector4[SkyBodyMesh.MaxOccluders];
		private readonly Vector4[] occluderRingShapes = new Vector4[SkyBodyMesh.MaxOccluders];
		private static readonly int OccluderRingsId = Shader.PropertyToID("_FishOccluderRings");
		private static readonly int OccluderRingShapesId = Shader.PropertyToID("_FishOccluderRingShapes");

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
				occluderRings[i] = bodies.OccluderRings[total - count + i];
				occluderRingShapes[i] = bodies.OccluderRingShapes[total - count + i];
			}
			Shader.SetGlobalVectorArray(OccluderRingsId, occluderRings);
			Shader.SetGlobalVectorArray(OccluderRingShapesId, occluderRingShapes);
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
						quad.AddQuad(body.Direction, SkyBodyMesh.Kind.Disc, body.AngularRadius * scale, body.Illumination, body.Shadowed, body.LightDirection, 1.2f, Vector3.up, 0f, tint, step.Rank, (float)body.DistanceKm);
						properties = texturedBlocks[textured];
						properties.Clear();
						properties.SetTexture(BodyTexId, PlanetSurfaceLibrary.Get(body.Body));
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
						quad.AddRing(body.Direction, body.AngularRadius * scale, pole, rings, body.LightDirection, step.Rank, (float)body.DistanceKm);
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
