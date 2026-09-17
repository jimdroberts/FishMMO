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
		private static readonly int EclipseId = Shader.PropertyToID("_FishSkyEclipse");
		private static readonly int SunDirId = Shader.PropertyToID("_FishSunDir");
		private static readonly int SunColorId = Shader.PropertyToID("_FishSunColor");
		private static readonly int SunCountId = Shader.PropertyToID("_FishSunCount");
		private static readonly int StarMatrixId = Shader.PropertyToID("_FishStarMatrix");
		private static readonly int StarCubeId = Shader.PropertyToID("_FishStarCube");
		private static readonly int NoiseId = Shader.PropertyToID("_FishWeatherNoise");
		private static readonly int CloudLitId = Shader.PropertyToID("_FishCloudLit");
		private static readonly int CloudShadowId = Shader.PropertyToID("_FishCloudShadow");
		private static readonly int CloudParamsId = Shader.PropertyToID("_FishCloudParams");
		private static readonly int AuroraParamsId = Shader.PropertyToID("_FishAuroraParams");
		private static readonly int AuroraAId = Shader.PropertyToID("_FishAuroraA");
		private static readonly int AuroraBId = Shader.PropertyToID("_FishAuroraB");
		private static readonly int RainbowId = Shader.PropertyToID("_FishRainbow");
		private static readonly int BodyTexId = Shader.PropertyToID("_BodyTex");
		private static readonly int UseTextureId = Shader.PropertyToID("_UseTexture");

		private static SkySystem instance;
		public static SkySystem Instance => instance;

		/// <summary>An explicit render profile (the test scene); otherwise the loaded one.</summary>
		public WeatherRenderProfile Profile;
		/// <summary>The camera to present for; otherwise <see cref="Camera.main"/>.</summary>
		public Camera TargetCamera;

		private WorldDayNightCycle cycle;
		private Material skyMaterial;
		private Material bodyMaterial;
		private SkyProfile defaultSky;
		private SkyProfile airlessSky;
		private SkyProfile regionSky;
		private bool hasRegionSky;
		private SkyProfile blendFrom;
		private SkyProfile blendTo;
		private float blendElapsed;
		private float blendSeconds;
		private Cubemap stars;
		private int starSize;
		private Light sun;
		private Light moon;
		private Light createdSun;
		private readonly List<Light> companions = new List<Light>();
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
		private WeatherMap weatherMap;
		private readonly List<Meteor> meteors = new List<Meteor>();
		private double meteorsUntil = double.NaN;
		private readonly Vector4[] sunDirections = new Vector4[MaxSuns];
		private readonly Vector4[] sunColors = new Vector4[MaxSuns];
		private SkySample current;
		private double worldSeconds;

		// Made in Awake: Unity refuses these inside a behaviour's constructor.
		private MaterialPropertyBlock block;

		public SkySample Current => current;
		public CelestialState State => cycle != null ? cycle.State : null;
		public LightningPresenter Lightning => lightning;
		public CurtainPresenter Curtains => curtains;
		public SkyBodyMesh Bodies => bodies;
		public WeatherMap Map => weatherMap;
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
			block = new MaterialPropertyBlock();
			bodies = new SkyBodyMesh();
			lightning = new LightningPresenter();
			curtains = new CurtainPresenter();
			cloudShadows = new CloudShadowPresenter();
			weatherMap = new WeatherMap();
			defaultSky = ScriptableObject.CreateInstance<SkyProfile>();
			defaultSky.hideFlags = HideFlags.DontSave;
			defaultSky.name = "Default Sky";
			airlessSky = ScriptableObject.CreateInstance<SkyProfile>();
			airlessSky.hideFlags = HideFlags.DontSave;
			airlessSky.name = "Airless Sky";
			airlessSky.Airless = true;
			ChangeSkyProfileAction.OnChangeSkyProfile += OnChangeSkyProfile;
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
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
			weatherMap?.Dispose();
			DestroyOwned(skyMaterial);
			DestroyOwned(bodyMaterial);
			DestroyOwned(defaultSky);
			DestroyOwned(airlessSky);
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
			return body != null && !body.HasWeather ? airlessSky : defaultSky;
		}

		private void Update()
		{
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
			SkySample sample = blendTo.Evaluate(sunAltitude);
			if (blendFrom != null && blendSeconds > 0f && blendElapsed < blendSeconds)
			{
				blendElapsed += dt;
				sample = SkySample.Lerp(blendFrom.Evaluate(sunAltitude), sample, Mathf.SmoothStep(0f, 1f, blendElapsed / blendSeconds));
			}
			// The profile is authored for the reference sun; the suns that are up recolour it.
			baseSunLight = sample.SunLight;
			sample = TintBySuns(state, sample, out primaryTint);
			current = sample;

			float overcast = Mathf.Clamp01(weather[WeatherChannel.CloudCover] * (0.4f + 0.6f * weather[WeatherChannel.CloudDensity]));
			float eclipse = state.SolarEclipse;

			// Lightning first: its flash reaches the sky, the lights and the weather globals.
			Vector3 viewer = camera != null ? camera.transform.position : Vector3.zero;
			lightning.Update(timeline, tick, worldSeconds, viewer, camera, profile.BoltMaterial);
			if (presentation != null)
			{
				presentation.LightningFlash = lightning.Flash;
			}

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
			bodies.Clear();
			bodies.AddBodies(state, blendTo, state.System != null ? state.System.Limits : new SkyLimits());
			if (tier.Asteroids)
			{
				bodies.AddAsteroids(state, state.System != null ? state.System.Limits : new SkyLimits());
			}
			bodies.AddMeteors(meteors, worldSeconds);
			bodies.Upload();
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

			sun = cycle.SunLight;
			moon = cycle.MoonLight;
			if (sun == null)
			{
				if (createdSun == null)
				{
					var sunObject = new GameObject("Sky Sun");
					sunObject.transform.SetParent(transform, false);
					createdSun = sunObject.AddComponent<Light>();
					createdSun.type = LightType.Directional;
				}
				createdSun.enabled = true;
				sun = createdSun;
			}
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
			if (stars == null || starSize != tier.StarCubemapSize)
			{
				DestroyOwned(stars);
				starSize = tier.StarCubemapSize;
				stars = StarfieldBuilder.Build(starSize, 238);
			}
			float airless = sky.Airless ? 1f : 0f;
			float fogBlend = Mathf.Clamp01((RenderSettings.fog ? 0.45f : 0f) + WeatherFogPresenter.Amount(weather) * 0.55f) * (1f - airless);
			Shader.SetGlobalVector(ZenithId, sample.Zenith);
			Shader.SetGlobalVector(HorizonId, sample.Horizon);
			Shader.SetGlobalVector(GroundId, sample.Ground);
			Color fog = RenderSettings.fogColor;
			fog.a = fogBlend;
			Shader.SetGlobalVector(FogColorId, fog);
			float starVisibility = sample.StarVisibility * (1f - overcast * 0.9f);
			Shader.SetGlobalVector(ParamsId, new Vector4(starVisibility * sky.StarBrightness, sky.MilkyWay, sky.StarTwinkle, sky.Exposure));
			Shader.SetGlobalVector(EclipseId, new Vector4(eclipse, state.LunarEclipse, airless, 0f));
			Shader.SetGlobalMatrix(StarMatrixId, state.EquatorialToScene.transpose);
			Shader.SetGlobalTexture(StarCubeId, stars);
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
				sunDirections[0] = new Vector4(0.3f, 0.8f, 0.5f, 0.0047f * sky.SunDiscScale);
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
			Shader.SetGlobalVector(CloudParamsId, new Vector4(sky.CloudScale, sky.CirrusAmount, (float)(worldSeconds % 100000.0), 1f));

			// Aurora: needs the weather's aurora, night, a high latitude and the cold.
			float latitudeGate = Mathf.InverseLerp(sky.AuroraMinLatitude - 10f, sky.AuroraMinLatitude + 5f, Mathf.Abs((float)state.Latitude));
			if (state.Observer == null)
			{
				latitudeGate = 1f;
			}
			float cold = Mathf.InverseLerp(0.35f, 0f, context.Temperature);
			float aurora = weather[WeatherChannel.Aurora] * sample.StarVisibility * latitudeGate * cold * (1f - overcast);
			Shader.SetGlobalVector(AuroraParamsId, new Vector4(aurora, (float)(worldSeconds % 100000.0), 0f, 0f));
			Shader.SetGlobalVector(AuroraAId, sky.AuroraA);
			Shader.SetGlobalVector(AuroraBId, sky.AuroraB);

			// Rainbow: rain falling while the sun is low and not hidden.
			float rain = weather[WeatherChannel.Precipitation] * weather[WeatherChannel.RainWeight];
			float low = state.Sun >= 0 ? Mathf.Clamp01(1f - Mathf.Abs(state.SunAltitude - 20f) / 22f) : 0f;
			float rainbow = Mathf.Clamp01(rain * 4f) * Mathf.Clamp01(1.4f - rain * 1.5f) * low * (1f - overcast * 0.85f) * (1f - airless);
			Shader.SetGlobalVector(RainbowId, new Vector4(rainbow, 0f, 0f, 0f));
		}

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

		private void AddSun(in SkyBodyState star, in SkySample sample, SkyProfile sky, float eclipse, ref int count)
		{
			var starBody = star.Body as StarBody;
			Color tint = starBody != null ? starBody.SkyTint : Color.white;
			float luminosity = starBody != null ? starBody.Luminosity : 1f;
			// Each disc and halo in its own star's colour, whatever the sky's blend.
			Color color = Mul(baseSunLight, tint) * (0.8f + 0.2f * Mathf.Sqrt(luminosity));
			float radius = Mathf.Max(star.AngularRadius * sky.SunDiscScale, 0.0015f);
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

			float moonIntensity = 0f;
			if (moon != null)
			{
				if (state.Moon >= 0)
				{
					SkyBodyState body = state.Bodies[state.Moon];
					moon.transform.rotation = LightRotation(body.Direction);
					float up = Mathf.Clamp01(body.AltitudeDegrees / 3f + 0.3f);
					moonIntensity = sample.MoonIntensity * body.Illumination * (1f - body.Shadowed * 0.9f) * up * (1f - overcast * 0.7f);
				}
				moon.color = sample.MoonLight;
				moon.intensity = moonIntensity;
				moon.enabled = moonIntensity > 0.001f;
			}

			// One light casts shadows: whichever is up and brighter. Hand over near the horizon.
			bool sunLeads = sunAltitude > 0.5f || moon == null || moonIntensity <= 0.02f;
			sun.shadows = sunLeads && sun.intensity > 0.02f ? LightShadows.Soft : LightShadows.None;
			if (moon != null)
			{
				moon.shadows = !sunLeads ? LightShadows.Soft : LightShadows.None;
			}
			RenderSettings.sun = sunLeads || moon == null ? sun : moon;

			// Extra suns add light without shadows.
			int needed = 0;
			for (int i = 0; i < state.Bodies.Count; i++)
			{
				if (i == state.Sun || state.Bodies[i].Kind != SkyBodyKind.Star)
				{
					continue;
				}
				SkyBodyState star = state.Bodies[i];
				while (companions.Count <= needed)
				{
					var go = new GameObject("Sky Companion Sun");
					go.transform.SetParent(transform, false);
					Light light = go.AddComponent<Light>();
					light.type = LightType.Directional;
					light.shadows = LightShadows.None;
					companions.Add(light);
				}
				Light companion = companions[needed++];
				companion.enabled = star.AltitudeDegrees > -2f;
				companion.transform.rotation = LightRotation(star.Direction);
				float strength = (star.Body as StarBody)?.Luminosity ?? 1f;
				companion.intensity = sample.SunIntensity * Mathf.Clamp01(star.AltitudeDegrees / 10f + 0.2f) * Mathf.Clamp(strength, 0f, 2f) * 0.6f * (1f - overcast * 0.6f);
				companion.color = Mul(baseSunLight, ((StarBody)star.Body).SkyTint);
			}
			for (int i = needed; i < companions.Count; i++)
			{
				companions[i].enabled = false;
			}

			Vector2 wind = WeatherShaderGlobals.WindDirection(weather[WeatherChannel.WindHeading]);
			cloudShadows.Update(sun, profile.Noise, profile.CloudCookieMaterial, weather[WeatherChannel.CloudCover], weather[WeatherChannel.CloudDensity], wind, weather[WeatherChannel.WindSpeed], dt, tier.CloudShadows && sunLeads);
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
			if (bodyMaterial != null && bodies.Mesh != null && bodies.Mesh.vertexCount > 0)
			{
				block.Clear();
				block.SetFloat(UseTextureId, 0f);
				var rp = new RenderParams(bodyMaterial) { camera = camera, matProps = block, worldBounds = new Bounds(camera.transform.position, Vector3.one * 20000f), shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
				Graphics.RenderMesh(rp, bodies.Mesh, 0, Matrix4x4.identity);
			}
			if (bodyMaterial != null && bodies.Textured.Count > 0)
			{
				DrawTextured(camera);
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

		private void DrawTextured(Camera camera)
		{
			float scale = blendTo != null ? blendTo.BodyDiscScale : 1f;
			for (int i = 0; i < bodies.Textured.Count; i++)
			{
				while (texturedMeshes.Count <= i)
				{
					texturedMeshes.Add(new SkyBodyMesh());
					texturedBlocks.Add(new MaterialPropertyBlock());
				}
				SkyBodyState body = bodies.Textured[i];
				SkyBodyMesh mesh = texturedMeshes[i];
				mesh.Clear();
				Color tint = body.Body.Tint;
				tint.a = body.Body is WorldBody world && world.HasWeather ? 0.4f : 1f;
				mesh.AddQuad(body.Direction, SkyBodyMesh.Kind.Disc, body.AngularRadius * scale, body.Illumination, body.Shadowed, body.LightDirection, 1.2f, Vector3.up, 0f, tint);
				MaterialPropertyBlock properties = texturedBlocks[i];
				properties.SetTexture(BodyTexId, body.Body.SurfaceTexture);
				properties.SetFloat(UseTextureId, 1f);
				var rp = new RenderParams(bodyMaterial) { camera = camera, matProps = properties, worldBounds = new Bounds(camera.transform.position, Vector3.one * 20000f), shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
				Graphics.RenderMesh(rp, mesh.Upload(), 0, Matrix4x4.identity);
			}
		}
	}
}
