using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;
using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The sky of a place, comets, showers and belts; the atlas entry a scene reads its world
	/// data from; and the checks the designers show.
	/// </summary>
	[TestFixture]
	public class WorldDesignDataTests
	{
		private readonly List<Object> created = new List<Object>();
		private readonly List<ICachedObject> cached = new List<ICachedObject>();
		private SolarSystemProfile system;
		private StarBody sun;
		private WorldBody home;

		[SetUp]
		public void BuildSystem()
		{
			sun = Make<StarBody>("Test Sun");
			home = Make<WorldBody>("Test Home");
			home.Parent = sun;
			home.Orbit = new OrbitSettings { Distance = 1f };
			home.RotationHours = 6f;
			home.AxialTiltDegrees = 23.4f;
			system = Make<SolarSystemProfile>("Test System");
			system.Bodies.Add(sun);
			system.Bodies.Add(home);
			system.HomeWorld = home;
		}

		private UnityEngine.SceneManagement.Scene previewScene;

		[TearDown]
		public void TearDown()
		{
			if (previewScene.IsValid())
			{
				UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
				previewScene = default;
			}
			foreach (ICachedObject entry in cached)
			{
				entry.RemoveFromCache();
			}
			cached.Clear();
			foreach (Object asset in created)
			{
				if (asset != null)
				{
					Object.DestroyImmediate(asset);
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

		// ── Sky ──

		[Test]
		public void HorizontalCoordinatesFollowTheHourAngle()
		{
			CelestialSky.Horizontal(0.0, 0.0, 0.0, out double alt, out _);
			Assert.That(alt, Is.EqualTo(90.0).Within(1e-9), "on the meridian at the equator, declination 0 is overhead");
			CelestialSky.Horizontal(0.0, 0.0, Math.PI / 2.0, out alt, out double az);
			Assert.That(alt, Is.EqualTo(0.0).Within(1e-9));
			Assert.That(az, Is.EqualTo(270.0).Within(1e-9), "six hours after the meridian it sets in the west");
			CelestialSky.Horizontal(0.0, 0.0, -Math.PI / 2.0, out _, out az);
			Assert.That(az, Is.EqualTo(90.0).Within(1e-9), "and rises in the east");
			CelestialSky.Horizontal(50.0, 10.0 * CelestialMath.Deg2Rad, 0.0, out alt, out az);
			Assert.That(alt, Is.EqualTo(50.0).Within(1e-9));
			Assert.That(az, Is.EqualTo(180.0).Within(1e-9), "north of the equator, the meridian sun is due south");
		}

		[Test]
		public void TheSkyListAgreesWithTheSunMaths()
		{
			var views = new List<SkyBodyView>();
			foreach (double hours in new[] { 3.0, 100.25, 5000.5 })
			{
				CelestialSky.Bodies(system, home, hours, 35.0, -20.0, views);
				LogAssert.AreEqual(1, views.Count, "a two-body system shows the sun only");
				double expected = CelestialMath.SunAltitude(system, home, hours, 35.0, -20.0) * CelestialMath.Rad2Deg;
				Assert.That(views[0].Altitude, Is.EqualTo(expected).Within(1e-9));
				LogAssert.AreEqual(1.0, views[0].Illumination);
				LogAssert.IsFalse(views[0].Textured, "stars are never textured");
			}
		}

		[Test]
		public void ACloseLargeMoonIsTextured()
		{
			WorldBody moon = Make<WorldBody>("Test Moon");
			moon.Kind = WorldBodyKind.Moon;
			moon.Parent = home;
			moon.TidallyLocked = true;
			moon.SkyRadiusKm = 1737f;
			moon.Orbit = new OrbitSettings { Distance = 384f, PeriodMode = OrbitPeriodMode.Authored, PeriodDays = 7.5f };
			system.Bodies.Add(moon);
			var views = new List<SkyBodyView>();

			CelestialSky.Bodies(system, home, 10.0, 0.0, 0.0, views);
			SkyBodyView fromHome = views.Find(v => v.Body == moon);
			LogAssert.IsFalse(fromHome.Textured, $"a real-sized moon covers {fromHome.SkyPercent:0.####}% of the sky, under the 0.1% threshold");

			moon.Orbit = new OrbitSettings { Distance = 20f, PeriodMode = OrbitPeriodMode.Authored, PeriodDays = 7.5f };
			CelestialSky.Bodies(system, home, 10.0, 0.0, 0.0, views);
			LogAssert.IsTrue(views.Find(v => v.Body == moon).Textured, "a moon at 20 000 km fills enough sky to draw its surface");

			CelestialSky.Bodies(system, moon, 10.0, 0.0, 0.0, views);
			SkyBodyView planet = views.Find(v => v.Body == home);
			LogAssert.IsTrue(planet.Textured, "and the planet in the moon's sky is huge");
			Assert.That(planet.DistanceKm, Is.EqualTo(20000.0).Within(1.0));
		}

		[Test]
		public void AsteroidsStayInTheirBeltAndRepeat()
		{
			var belt = new AsteroidBelt { InnerAU = 2f, OuterAU = 3f, Count = 200, ThicknessDegrees = 10f, Seed = 7 };
			for (int i = 0; i < belt.Count; i++)
			{
				Vector3d p = CelestialSky.AsteroidPosition(system, belt, i, 1234.5);
				Assert.That(p.Magnitude, Is.InRange(1.999, 3.001), $"asteroid {i}");
				LogAssert.IsTrue(Math.Abs(Math.Asin(p.Z / p.Magnitude)) <= 10.0 * CelestialMath.Deg2Rad + 1e-9, "within the belt's thickness");
				LogAssert.AreEqual(p.ToVector3(), CelestialSky.AsteroidPosition(system, belt, i, 1234.5).ToVector3());
			}
			Vector3d later = CelestialSky.AsteroidPosition(system, belt, 0, 1234.5 + 1000.0);
			LogAssert.AreNotEqual(CelestialSky.AsteroidPosition(system, belt, 0, 1234.5).ToVector3(), later.ToVector3(), "asteroids move");
		}

		[Test]
		public void ACometsTailPointsAwayFromTheStarAndGrowsNearIt()
		{
			CometBody comet = Make<CometBody>("Test Comet");
			comet.Parent = sun;
			comet.TailLengthMillionKm = 20f;
			comet.Orbit = new OrbitSettings { Distance = 10f, Eccentricity = 0.9f, PeriodMode = OrbitPeriodMode.Kepler };
			system.Bodies.Add(comet);
			double period = CelestialMath.OrbitHours(system, comet);

			Vector3d near = CelestialMath.Position(system, comet, 0.0);
			Vector3d nearTip = CelestialSky.CometTailTip(system, comet, 0.0);
			Assert.That(near.Magnitude, Is.EqualTo(1.0).Within(1e-6), "perihelion is a(1 − e)");
			LogAssert.IsTrue(nearTip.Magnitude > near.Magnitude, "the tail points away from the star");

			Vector3d far = CelestialMath.Position(system, comet, period / 2.0);
			Vector3d farTip = CelestialSky.CometTailTip(system, comet, period / 2.0);
			Assert.That(far.Magnitude, Is.EqualTo(19.0).Within(1e-5), "aphelion is a(1 + e)");
			LogAssert.IsTrue((nearTip - near).Magnitude > (farTip - far).Magnitude * 100.0, "the tail is far longer near the star");
			LogAssert.IsTrue(CelestialSky.CometBrightness(system, comet, 0.0) > CelestialSky.CometBrightness(system, comet, period / 2.0));
		}

		[Test]
		public void KeplersEquationHoldsForCometOrbits()
		{
			foreach (double e in new[] { 0.95, 0.97, 0.99 })
			{
				for (double m = 0.01; m < 6.3; m += 0.37)
				{
					double E = CelestialMath.SolveKepler(m, e);
					Assert.That(E - e * Math.Sin(E), Is.EqualTo(m).Within(1e-8), $"e {e} M {m}");
				}
			}
		}

		[Test]
		public void AShowerPeaksOnItsDayAndWrapsTheYear()
		{
			var shower = new MeteorShower { PeakDayOfYear = 2, HalfWidthDays = 3f, PeakPerHour = 60f };
			Assert.That(shower.RateOn(1.0, 365), Is.EqualTo(60f).Within(1e-4f), "day 2 is index 1");
			LogAssert.AreEqual(0f, shower.RateOn(5.0, 365), "three days past the peak it is over");
			LogAssert.IsTrue(shower.RateOn(364.0, 365) > 0f, "two days before the peak, across new year, it is building");
			LogAssert.IsTrue(shower.RateOn(0.0, 365) < 60f && shower.RateOn(0.0, 365) > 0f);

			system.SporadicMeteorsPerHour = 6f;
			system.MeteorShowers.Add(shower);
			Assert.That(CelestialSky.MeteorRate(system, 1.0), Is.EqualTo(66f).Within(1e-4f));
		}

		// ── Checks ──

		[Test]
		public void AGoodSystemHasNoProblems()
		{
			system.Calendar = Make<CalendarProfile>("Test Calendar");
			CollectionAssert.IsEmpty(SolarSystemChecks.Problems(system));
		}

		[Test]
		public void MistakesInTheSystemAreReported()
		{
			LogAssert.AreEqual(1, SolarSystemChecks.Problems(null).Count);

			WorldBody moon = Make<WorldBody>("Orphan Moon");
			moon.Kind = WorldBodyKind.Moon;
			moon.Parent = sun;
			system.Bodies.Add(moon);
			WorldBody stray = Make<WorldBody>("Stray");
			stray.Parent = Make<WorldBody>("Not Listed");
			system.Bodies.Add(stray);
			system.MeteorShowers.Add(new MeteorShower { Name = "Late", PeakDayOfYear = 400 });
			system.AsteroidBelts.Add(new AsteroidBelt { Name = "Inside Out", InnerAU = 3f, OuterAU = 2f });
			system.Calendar = Make<CalendarProfile>("Short Calendar");
			system.Calendar.DaysPerYear = 300;

			List<string> problems = SolarSystemChecks.Problems(system);
			string all = string.Join("\n", problems);
			StringAssert.Contains("Orphan Moon needs a planet", all);
			StringAssert.Contains("Stray orbits Not Listed", all);
			StringAssert.Contains("peaks on day 400", all);
			StringAssert.Contains("Inside Out", all);
			StringAssert.Contains("do not add up to 300", all);
		}

		// ── The scene settings view ──

		[Test]
		public void SceneSettingsKeepTheirSerializedNames()
		{
			/* The three fields became properties over renamed backing fields. Without the aliases
			 * every scene would load them as empty and the next save would write the loss. */
			AssertAlias("maxClients", "MaxClients");
			AssertAlias("climate", "Climate");
			AssertAlias("biomeMap", "BiomeMap");
		}

		private static void AssertAlias(string field, string oldName)
		{
			FieldInfo info = typeof(WorldSceneSettings).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"WorldSceneSettings.{field} must exist");
			LogAssert.IsNotNull(info.GetCustomAttribute<SerializeField>(), $"{field} must be serialized");
			FormerlySerializedAsAttribute alias = info.GetCustomAttribute<FormerlySerializedAsAttribute>();
			LogAssert.IsNotNull(alias, $"{field} must keep its old name");
			LogAssert.AreEqual(oldName, alias.oldName);
		}

		[Test]
		public void ASceneReadsItsWorldDataFromTheAtlas()
		{
			var go = new GameObject("Settings Probe");
			created.Add(go);
			var settings = go.AddComponent<WorldSceneSettings>();

			ClimateSettings own = Make<ClimateSettings>("Own Climate");
			settings.Climate = own;
			settings.MaxClients = 150;

			// An untitled scene has no atlas entry: everything falls back to the scene's own values.
			if (string.IsNullOrEmpty(go.scene.name))
			{
				LogAssert.IsNull(settings.AtlasEntry);
				LogAssert.AreEqual(own, settings.Climate, "no entry: the scene's own climate");
				LogAssert.AreEqual(150, settings.MaxClients);
				LogAssert.IsNull(settings.Body);
				LogAssert.AreEqual(WeatherSceneMode.Auto, settings.WeatherMode);
				LogAssert.IsTrue(settings.WeatherDirector);
				Assert.That(settings.Longitude, Is.EqualTo(0f));
			}

			// A named scene: a world scene opened as a preview, so nothing in the editor changes.
			string path = null;
			foreach (string candidate in UnityEditor.AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes/WorldScene" }))
			{
				path = UnityEditor.AssetDatabase.GUIDToAssetPath(candidate);
				break;
			}
			Assume.That(path, Is.Not.Null, "no world scene to open");
			previewScene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(path);
			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, previewScene);
			string sceneName = go.scene.name;
			Assume.That(sceneName, Is.Not.Null.And.Not.Empty, "the preview scene carries no name to key an atlas entry by");

			WorldAtlasLayer underworld = Make<WorldAtlasLayer>("Test Underworld");
			underworld.DefaultWeather = WeatherSceneMode.None;
			WorldAtlasScene entry = Make<WorldAtlasScene>("Test Entry " + Guid.NewGuid().ToString("N"));
			entry.SceneName = sceneName;
			entry.Body = home;
			entry.Layer = underworld;
			entry.Placed = true;
			entry.Latitude = 42.0;
			entry.Longitude = 100.0;
			entry.MaxClients = 40;
			entry.WeatherDirector = false;
			entry.AddToCache(entry.name);
			cached.Add(entry);

			LogAssert.AreEqual(entry, settings.AtlasEntry);
			LogAssert.AreEqual(home, settings.Body);
			Assert.That(settings.Latitude, Is.EqualTo(42f).Within(1e-4f));
			Assert.That(settings.Longitude, Is.EqualTo(100f).Within(1e-4f));
			LogAssert.AreEqual(40, settings.MaxClients, "the atlas cap wins");
			LogAssert.AreEqual(own, settings.Climate, "the entry names no climate, so the scene's stays");
			LogAssert.AreEqual(WeatherSceneMode.None, settings.WeatherMode, "Auto takes the layer's default");
			LogAssert.IsFalse(settings.WeatherDirector);

			entry.OverrideTimeZone = true;
			entry.TimeZoneHours = -3;
			Assert.That(settings.Longitude, Is.EqualTo(-45f).Within(1e-4f), "an overridden zone sets where the time is taken");
			LogAssert.AreEqual(-3, entry.TimeZone);

			ClimateSettings atlasClimate = Make<ClimateSettings>("Atlas Climate");
			entry.Climate = atlasClimate;
			LogAssert.AreEqual(atlasClimate, settings.Climate, "the atlas climate wins");

			entry.Climate = null;
			settings.Climate = null;
			ClimateSettings bodyClimate = Make<ClimateSettings>("Body Climate");
			home.BaseClimate = bodyClimate;
			LogAssert.AreEqual(bodyClimate, settings.Climate, "with neither, the body's base climate");

			entry.Weather = WeatherSceneMode.Own;
			LogAssert.AreEqual(WeatherSceneMode.Own, settings.WeatherMode, "an explicit mode beats the layer");
		}

		[Test]
		public void TheAtlasListsABodysLayersInOrder()
		{
			WorldAtlas atlas = Make<WorldAtlas>("Test Atlas");
			WorldAtlasLayer surface = Make<WorldAtlasLayer>("Surface");
			WorldAtlasLayer deep = Make<WorldAtlasLayer>("Deep");
			deep.Underground = true;
			deep.SortOrder = 1;
			WorldAtlasLayer sky = Make<WorldAtlasLayer>("Sky");
			sky.SortOrder = 5;
			atlas.DefaultLayers.Add(deep);
			atlas.DefaultLayers.Add(surface);
			atlas.DefaultLayers.Add(deep);

			CollectionAssert.AreEqual(new[] { surface, deep }, atlas.LayersOf(home), "defaults, once each, by sort order");
			LogAssert.AreEqual(surface, atlas.SurfaceLayerOf(home));
			LogAssert.AreEqual(deep, atlas.DungeonLayerOf(home));

			home.Layers.Add(sky);
			home.Layers.Add(surface);
			CollectionAssert.AreEqual(new[] { surface, sky }, atlas.LayersOf(home), "a body with its own layers uses only those");
			LogAssert.AreEqual(surface, atlas.DungeonLayerOf(home), "no underground layer: dungeons go to the surface");
		}

		[Test]
		public void TheAtlasRadiusFollowsItsMode()
		{
			home.MinimumRadiusKm = 30f;
			home.CurrentRadiusKm = 12f;
			Assert.That(home.AtlasRadiusKm, Is.EqualTo(30f), "Auto never goes under the minimum");
			home.CurrentRadiusKm = 55f;
			Assert.That(home.AtlasRadiusKm, Is.EqualTo(55f));
			home.RadiusMode = AtlasRadiusMode.Manual;
			home.ManualRadiusKm = 18f;
			Assert.That(home.AtlasRadiusKm, Is.EqualTo(18f), "Manual ignores the minimum");
		}
	}
}
