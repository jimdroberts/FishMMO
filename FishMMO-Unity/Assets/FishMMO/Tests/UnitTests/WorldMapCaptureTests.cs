using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldMaps;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The world map capture's fixed moment and the state it sets up for its one photograph: noon on
	/// the equinox, clear weather, full detail — and every global put back afterwards. Nothing here
	/// renders, so it runs without a graphics device.
	/// </summary>
	[TestFixture]
	public class WorldMapCaptureTests
	{
		private readonly List<Object> created = new List<Object>();
		private SolarSystemProfile system;
		private WorldBody home;

		[SetUp]
		public void BuildSystem()
		{
			StarBody sun = Make<StarBody>("Sun");
			sun.SkyRadiusKm = 696000f;
			home = Make<WorldBody>("Home");
			home.Parent = sun;
			home.Orbit = new OrbitSettings { Distance = 1f };
			home.RotationHours = 6f;
			home.AxialTiltDegrees = 23.4f;
			system = Make<SolarSystemProfile>("System");
			system.Bodies.Add(sun);
			system.Bodies.Add(home);
			system.HomeWorld = home;
		}

		[TearDown]
		public void TearDown()
		{
			for (int i = created.Count - 1; i >= 0; i--)
			{
				if (created[i] != null)
				{
					Object.DestroyImmediate(created[i]);
				}
			}
			created.Clear();
		}

		private T Make<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = name;
			created.Add(asset);
			return asset;
		}

		// ── The moment ─────────────────────────────────────────────────

		[TestCase(0.0, 0.0)]
		[TestCase(41.0, 37.5)]
		[TestCase(-63.0, -120.0)]
		public void TheMapIsPhotographedAtNoonOnTheEquinox(double latitude, double longitude)
		{
			double? hours = WorldMapCaptureMoment.EquinoxNoonHours(system, home, longitude);
			Assert.That(hours.HasValue, "a placed scene has a moment");
			Assert.That(hours.Value, Is.GreaterThanOrEqualTo(-CelestialMath.SolarDayHours(system, home)), "the first equinox, not one before the epoch");
			Assert.That(hours.Value, Is.LessThan(CelestialMath.YearHours(system) + CelestialMath.SolarDayHours(system, home)), "within the first year");

			Assert.That(CelestialMath.LocalTime01(system, home, hours.Value, longitude), Is.EqualTo(0.5).Within(1e-6), "local solar noon");
			CelestialMath.SunEquatorial(system, home, hours.Value, out _, out double declination);
			// Within half a day of the crossing the sun moves a fraction of a degree along its path.
			Assert.That(declination * CelestialMath.Rad2Deg, Is.EqualTo(0.0).Within(0.5), "the sun over the equator");
			double altitude = CelestialMath.SunAltitude(system, home, hours.Value, latitude, longitude) * CelestialMath.Rad2Deg;
			Assert.That(altitude, Is.EqualTo(90.0 - System.Math.Abs(latitude)).Within(0.6), "as high as the sun stands that day");
		}

		[Test]
		public void TheMomentIsTheSameEveryTime()
		{
			double? first = WorldMapCaptureMoment.EquinoxNoonHours(system, home, 12.0);
			double? second = WorldMapCaptureMoment.EquinoxNoonHours(system, home, 12.0);
			Assert.That(first, Is.EqualTo(second), "a pure function of the system and the place: maps do not change between builds");
		}

		[Test]
		public void ASceneWithNoSolarSystemHasNoMoment()
		{
			Assert.That(WorldMapCaptureMoment.EquinoxNoonHours(null, home, 0.0), Is.Null);
			Assert.That(WorldMapCaptureMoment.EquinoxNoonHours(system, null, 0.0), Is.Null);
			Assert.That(WorldMapCaptureMoment.EquinoxNoonHours(Make<SolarSystemProfile>("Starless"), home, 0.0), Is.Null);
		}

		// ── Full detail ────────────────────────────────────────────────

		[Test]
		public void FullDetailIsForcedForTheShotAndPutBackAfter()
		{
			float lodBias = QualitySettings.lodBias;
			int maximumLod = QualitySettings.maximumLODLevel;
			Scene scene = EditorSceneManager.NewPreviewScene();
			var data = new TerrainData();
			created.Add(data);
			try
			{
				data.heightmapResolution = 33;
				data.size = new Vector3(100f, 50f, 100f);
				GameObject tile = Terrain.CreateTerrainGameObject(data);
				SceneManager.MoveGameObjectToScene(tile, scene);
				Terrain terrain = tile.GetComponent<Terrain>();
				terrain.treeDistance = 400f;
				terrain.treeBillboardDistance = 50f;
				terrain.treeMaximumFullLODCount = 25;
				terrain.basemapDistance = 600f;
				terrain.heightmapPixelError = 9f;
				terrain.heightmapMaximumLOD = 1;
				QualitySettings.maximumLODLevel = 1;

				using (WorldMapDetailOverride.Apply(scene, 7000f, 1000f))
				{
					Assert.That(QualitySettings.lodBias, Is.GreaterThanOrEqualTo(WorldMapDetailOverride.LodBiasFor(1000f)), "every LOD group draws its first level from two kilometres up");
					Assert.That(QualitySettings.maximumLODLevel, Is.EqualTo(0));
					Assert.That(terrain.treeDistance, Is.GreaterThanOrEqualTo(7000f), "every tree is drawn");
					Assert.That(terrain.treeBillboardDistance, Is.GreaterThanOrEqualTo(7000f), "as a mesh, not a billboard");
					Assert.That(terrain.treeMaximumFullLODCount, Is.EqualTo(WorldMapDetailOverride.FullLodTrees));
					Assert.That(terrain.basemapDistance, Is.GreaterThanOrEqualTo(7000f), "the full splat, not the base map");
					Assert.That(terrain.heightmapPixelError, Is.EqualTo(1f));
					Assert.That(terrain.heightmapMaximumLOD, Is.EqualTo(0));
				}

				Assert.That(QualitySettings.lodBias, Is.EqualTo(lodBias));
				Assert.That(QualitySettings.maximumLODLevel, Is.EqualTo(1));
				Assert.That(terrain.treeDistance, Is.EqualTo(400f));
				Assert.That(terrain.treeBillboardDistance, Is.EqualTo(50f));
				Assert.That(terrain.treeMaximumFullLODCount, Is.EqualTo(25));
				Assert.That(terrain.basemapDistance, Is.EqualTo(600f));
				Assert.That(terrain.heightmapPixelError, Is.EqualTo(9f));
				Assert.That(terrain.heightmapMaximumLOD, Is.EqualTo(1));
			}
			finally
			{
				QualitySettings.lodBias = lodBias;
				QualitySettings.maximumLODLevel = maximumLod;
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		// ── The sky and the weather ────────────────────────────────────

		private static readonly int AmbientSkyId = Shader.PropertyToID("_FishAmbientSky");
		private static readonly int CoverParamsId = Shader.PropertyToID("_FishCoverParams");

		/// <summary>Every global the stage writes, with a value nothing would write, so a missed restore shows.</summary>
		private static Dictionary<int, Vector4> Sentinels()
		{
			var sentinels = new Dictionary<int, Vector4>();
			int n = 1;
			foreach ((int id, Vector4 _) in WorldMapCaptureSky.ClearWeatherGlobals())
			{
				sentinels[id] = new Vector4(0.1f * n, 0.2f, 0.3f, 0.4f);
				n++;
			}
			return sentinels;
		}

		[Test]
		public void TheWeatherIsClearedForTheShotAndPutBackAfter()
		{
			Dictionary<int, Vector4> sentinels = Sentinels();
			var saved = new Dictionary<int, Vector4>();
			foreach (KeyValuePair<int, Vector4> pair in sentinels)
			{
				saved[pair.Key] = Shader.GetGlobalVector(pair.Key);
				Shader.SetGlobalVector(pair.Key, pair.Value);
			}
			// A fog left over from a play session: the fog passes would draw it into the map.
			var fog = new FogLayerView { Extinction = 0.05f, Depth = 80f };
			FogLayerView savedFog = FogLayerView.ReplaceCurrent(fog);
			Light sunBefore = RenderSettings.sun;
			try
			{
				// No day/night cycle: the weather is cleared, the scene's own light left alone.
				var context = new WorldMapCaptureContext();
				using (WorldMapCaptureSky.Stage(context))
				{
					Assert.That(FogLayerView.Current.Visible, Is.False, "no fog layer for the passes to draw");
					foreach ((int id, Vector4 value) in WorldMapCaptureSky.ClearWeatherGlobals())
					{
						Assert.That(Shader.GetGlobalVector(id), Is.EqualTo(value), $"global {id} is clear");
					}
					Assert.That(Shader.GetGlobalVector(CoverParamsId).x, Is.EqualTo(0f), "no cover map: the scene-wide clear figure everywhere");
					Assert.That(RenderSettings.sun, Is.SameAs(sunBefore), "a scene with no sky keeps its own lighting");
				}

				foreach (KeyValuePair<int, Vector4> pair in sentinels)
				{
					Assert.That(Shader.GetGlobalVector(pair.Key), Is.EqualTo(pair.Value), $"global {pair.Key} is put back");
				}
				Assert.That(FogLayerView.Current.Extinction, Is.EqualTo(fog.Extinction), "the fog layer is put back");
				Assert.That(FogLayerView.Current.Depth, Is.EqualTo(fog.Depth));
			}
			finally
			{
				foreach (KeyValuePair<int, Vector4> pair in saved)
				{
					Shader.SetGlobalVector(pair.Key, pair.Value);
				}
				FogLayerView.ReplaceCurrent(savedFog);
			}
		}

		[Test]
		public void TheNoonSunLightsTheShotAndEverythingIsPutBackAfter()
		{
			const double latitude = 35.0;
			const double longitude = 20.0;

			Light sunBefore = RenderSettings.sun;
			AmbientMode modeBefore = RenderSettings.ambientMode;
			Color skyBefore = RenderSettings.ambientSkyColor;
			Color equatorBefore = RenderSettings.ambientEquatorColor;
			Color groundBefore = RenderSettings.ambientGroundColor;
			SphericalHarmonicsL2 probeBefore = RenderSettings.ambientProbe;
			Vector4 ambientGlobalBefore = Shader.GetGlobalVector(AmbientSkyId);

			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				// Sentinels: values nothing would set, so a missed restore cannot pass by luck.
				RenderSettings.ambientMode = AmbientMode.Flat;
				RenderSettings.ambientSkyColor = new Color(0.11f, 0.22f, 0.33f);
				RenderSettings.ambientEquatorColor = new Color(0.44f, 0.55f, 0.66f);
				RenderSettings.ambientGroundColor = new Color(0.77f, 0.88f, 0.99f);
				SphericalHarmonicsL2 sentinelProbe = RenderSettings.ambientProbe;
				Shader.SetGlobalColor(AmbientSkyId, new Color(0.3f, 0.2f, 0.1f));
				Vector4 sentinelGlobal = Shader.GetGlobalVector(AmbientSkyId);

				var host = new GameObject("Day and night");
				SceneManager.MoveGameObjectToScene(host, scene);
				WorldDayNightCycle cycle = host.AddComponent<WorldDayNightCycle>();

				var context = new WorldMapCaptureContext
				{
					Scene = scene,
					Cycle = cycle,
					System = system,
					Body = home,
					Latitude = latitude,
					Longitude = longitude,
					Hours = WorldMapCaptureMoment.EquinoxNoonHours(system, home, longitude),
				};
				Assume.That(context.HasSky, "an enabled cycle on an active object is a sky");

				Light captureSun;
				using (WorldMapCaptureSky.Stage(context))
				{
					captureSun = RenderSettings.sun;
					Assert.That(captureSun, Is.Not.Null, "the shot has a sun");
					Assert.That(captureSun, Is.Not.SameAs(sunBefore));
					Assert.That(captureSun.type, Is.EqualTo(LightType.Directional));
					Assert.That(captureSun.intensity, Is.GreaterThan(0f), "a noon sun, not a night one");
					Assert.That(captureSun.shadows, Is.EqualTo(LightShadows.None));
					Assert.That((captureSun.gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave, "never saved into the scene");
					float altitude = Mathf.Asin(Mathf.Clamp(-captureSun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
					Assert.That(altitude, Is.EqualTo(90f - (float)latitude).Within(0.75f), "the equinox noon sun, as high as it stands that day");
					Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
					Assert.That(RenderSettings.ambientSkyColor, Is.Not.EqualTo(new Color(0.11f, 0.22f, 0.33f)), "the noon sky's ambient");
				}

				Assert.That(captureSun == null, "the capture's sun is destroyed");
				Assert.That(RenderSettings.sun, Is.SameAs(sunBefore));
				Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
				Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(new Color(0.11f, 0.22f, 0.33f)));
				Assert.That(RenderSettings.ambientEquatorColor, Is.EqualTo(new Color(0.44f, 0.55f, 0.66f)));
				Assert.That(RenderSettings.ambientGroundColor, Is.EqualTo(new Color(0.77f, 0.88f, 0.99f)));
				Assert.That(RenderSettings.ambientProbe == sentinelProbe, "the ambient probe is put back last");
				Assert.That(Shader.GetGlobalVector(AmbientSkyId), Is.EqualTo(sentinelGlobal), "the ambient global is put back exactly");
			}
			finally
			{
				RenderSettings.sun = sunBefore;
				RenderSettings.ambientMode = modeBefore;
				RenderSettings.ambientSkyColor = skyBefore;
				RenderSettings.ambientEquatorColor = equatorBefore;
				RenderSettings.ambientGroundColor = groundBefore;
				RenderSettings.ambientProbe = probeBefore;
				Shader.SetGlobalVector(AmbientSkyId, ambientGlobalBefore);
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		[Test]
		public void TheBakeHasTheClientSkyRegistered()
		{
			// The bake's shared assembly cannot see the client; the client's weather editor hands it the sky.
			Assert.That(WorldMapBaker.CaptureStaging.Count, Is.GreaterThan(0), "a client-target editor registers the capture's sky and weather");
		}

		[Test]
		public void ANoonSampleIsDaylight()
		{
			SkyProfile profile = Make<SkyProfile>("Sky");
			var context = new WorldMapCaptureContext
			{
				System = system,
				Body = home,
				Latitude = 0.0,
				Longitude = 0.0,
				Hours = WorldMapCaptureMoment.EquinoxNoonHours(system, home, 0.0),
			};
			SkySample sample = WorldMapCaptureSky.NoonSample(context, profile, out Vector3 towardSun);
			Assert.That(towardSun.y, Is.GreaterThan(0.99f), "on the equator at the equinox the noon sun is overhead");
			Assert.That(sample.SunIntensity, Is.GreaterThan(0f));

			// Not placed: the sky's own fallback sun, still daylight.
			var unplaced = new WorldMapCaptureContext();
			SkySample fallback = WorldMapCaptureSky.NoonSample(unplaced, profile, out Vector3 fallbackSun);
			Assert.That(fallbackSun, Is.EqualTo(WorldMapCaptureSky.FallbackSunDirection));
			Assert.That(fallback.SunIntensity, Is.GreaterThan(0f));
		}
	}
}
