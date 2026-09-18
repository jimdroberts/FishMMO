using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The sky: where bodies appear in a scene, the events a changing sky raises, the colours a
	/// sun altitude gives, the lightning and meteor schedules every client must agree on, and
	/// the rule that one component owns the render settings the sky needs.
	/// </summary>
	[TestFixture]
	public class SkyTests
	{
		private const double TickDelta = 1.0 / 30.0;

		private readonly List<ScriptableObject> created = new List<ScriptableObject>();
		private SolarSystemProfile system;
		private StarBody sun;
		private WorldBody home;

		[SetUp]
		public void BuildSystem()
		{
			sun = Make<StarBody>("Sun");
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
			foreach (ScriptableObject asset in created)
			{
				if (asset is WeatherLayerTemplate template)
				{
					template.RemoveFromCache();
				}
				Object.DestroyImmediate(asset);
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

		private WorldBody Moon()
		{
			WorldBody moon = Make<WorldBody>("Moon");
			moon.Parent = home;
			moon.TidallyLocked = true;
			moon.SkyRadiusKm = 1737f;
			moon.Orbit = new OrbitSettings { Distance = 384f, PeriodDays = 27.3f };
			system.Bodies.Add(moon);
			return moon;
		}

		private static void AssertDirection(Vector3 expected, Vector3 actual, string message, float degrees = 0.05f)
		{
			float angle = Vector3.Angle(expected, actual);
			LogAssert.IsTrue(angle <= degrees, $"{message}: expected {expected}, got {actual} ({angle:0.###}° apart)");
		}

		private static double DayHours(SolarSystemProfile s) => CelestialMath.HomeSolarDayHours(s);

		// ── Directions ────────────────────────────────────────────────

		[Test]
		public void SceneDirectionsFollowTheAtlasFrame()
		{
			AssertDirection(Vector3.forward, CelestialState.SceneDirection(0, 0, 0), "north is +Z");
			AssertDirection(Vector3.right, CelestialState.SceneDirection(0, 90, 0), "east is +X");
			AssertDirection(Vector3.up, CelestialState.SceneDirection(90, 123, 45), "the zenith is up under any heading");
			AssertDirection(Vector3.forward, CelestialState.SceneDirection(0, 90, 90), "a scene turned to face east sees east ahead");
		}

		[Test]
		public void TheCelestialPoleStandsAtTheLatitude()
		{
			var state = new CelestialState();
			foreach (double latitude in new[] { 0.0, 35.0, 72.0, -40.0 })
			{
				state.Compute(system, home, 13.7, latitude, 20.0, 30f);
				Vector3 pole = state.EquatorialToScene.MultiplyVector(Vector3.forward);
				AssertDirection(CelestialState.SceneDirection(latitude, 0, 30), pole, $"pole at latitude {latitude}", 0.2f);
			}
		}

		[Test]
		public void TheStarRotationIsAProperRotationOfTheStarMatrix()
		{
			var state = new CelestialState();
			state.Compute(system, home, 40.25, 50.0, -60.0, 10f);
			Matrix4x4 stars = state.EquatorialToScene;
			Quaternion rotation = state.SkyRotation;
			AssertDirection(stars.GetColumn(0), rotation * Vector3.right, "x axis", 0.2f);
			AssertDirection(stars.GetColumn(1), rotation * Vector3.up, "y axis", 0.2f);
			AssertDirection(-(Vector3)stars.GetColumn(2), rotation * Vector3.forward, "z axis is mirrored", 0.2f);
			Assert.That(Mathf.Abs(stars.determinant), Is.EqualTo(1f).Within(1e-3f), "the star matrix keeps lengths");
		}

		[Test]
		public void TheSunRisesAndSetsOncePerSolarDay()
		{
			var state = new CelestialState();
			double day = DayHours(system);
			int rises = 0, sets = 0;
			bool? wasUp = null;
			for (double h = 0; h < day * 3; h += day / 200.0)
			{
				state.Compute(system, home, h, 20.0, 0.0, 0f);
				LogAssert.IsTrue(state.Sun >= 0, "the primary star is the sun");
				bool up = state.SunAltitude > 0f;
				if (wasUp.HasValue && up != wasUp.Value)
				{
					if (up) rises++; else sets++;
				}
				wasUp = up;
				LogAssert.AreEqual(state.SunAltitude > 0f, Vector3.Dot(state.SunDirection, Vector3.up) > 0f, "altitude and direction agree");
			}
			LogAssert.AreEqual(3, rises + sets > 0 ? Math.Max(rises, sets) : 0, $"three days, {rises} rises, {sets} sets");
		}

		[Test]
		public void NoSystemMeansAnEmptyDaySky()
		{
			var state = new CelestialState();
			state.Compute(null, null, 5.0, 10.0, 10.0, 0f);
			LogAssert.IsTrue(state.IsDaylight);
			LogAssert.AreEqual(0, state.Bodies.Count);
			LogAssert.AreEqual(Quaternion.identity, state.SkyRotation);
			LogAssert.AreEqual(0f, state.MeteorRate);
		}

		[Test]
		public void TheMoonIsFoundAndLitFromTheSunsSide()
		{
			WorldBody moon = Moon();
			var state = new CelestialState();
			state.Compute(system, home, 100.0, 30.0, 0.0, 0f);
			LogAssert.IsTrue(state.Moon >= 0, "a moon of the home world is the moon");
			SkyBodyState body = state.Bodies[state.Moon];
			LogAssert.AreEqual(moon, (WorldBody)body.Body);
			LogAssert.AreEqual(SkyBodyKind.Moon, body.Kind);
			LogAssert.IsTrue(body.AngularRadius > 0f && body.AngularRadius < 0.02f, $"a real-sized moon, radius {body.AngularRadius} rad");
			// The sun and the moon are close enough together that the light comes from the sun's side of the sky.
			LogAssert.IsTrue(Vector3.Dot(body.LightDirection, state.SunDirection) > 0.9f, "the moon is lit from the sun's direction");
		}

		// ── Eclipses ──────────────────────────────────────────────────

		[Test]
		public void DiscOverlapIsZeroApartAndTheSmallerDiscInside()
		{
			LogAssert.AreEqual(0.0, CelestialState.CircleOverlap(1.0, 1.0, 2.5));
			Assert.That(CelestialState.CircleOverlap(2.0, 0.5, 0.2), Is.EqualTo(Math.PI * 0.25).Within(1e-9));
			// Two unit discs one radius apart overlap by 2π/3 − √3/2.
			Assert.That(CelestialState.CircleOverlap(1.0, 1.0, 1.0), Is.EqualTo(2.0 * Math.PI / 3.0 - Math.Sqrt(3.0) / 2.0).Within(1e-9));
		}

		[Test]
		public void OnlyMoonsFallIntoAPlanetsShadow()
		{
			WorldBody moon = Moon();
			Vector3d star = CelestialMath.Position(system, sun, 0.0);
			LogAssert.AreEqual(0f, CelestialState.ShadowDepth(system, home, 0.0, star), "planets are never shadowed");
			double period = CelestialMath.OrbitHours(system, moon);
			float deepest = 0f;
			for (double h = 0; h < period; h += period / 2000.0)
			{
				deepest = Mathf.Max(deepest, CelestialState.ShadowDepth(system, moon, h, CelestialMath.Position(system, sun, h)));
			}
			LogAssert.IsTrue(deepest >= 0f && deepest <= 1f, "depth stays in 0..1");
		}

		// ── Events ────────────────────────────────────────────────────

		[Test]
		public void TheFirstUpdateOnlyPrimes()
		{
			Moon();
			var state = new CelestialState();
			var tracker = new CelestialEventTracker();
			var events = new List<CelestialEvent>();
			// Find a full moon, then start the tracker on it.
			double day = DayHours(system);
			for (double h = 0; h < day * 40; h += 0.5)
			{
				state.Compute(system, home, h, 0.0, 0.0, 0f);
				if (state.Bodies[state.Moon].Illumination >= CelestialEventTracker.FullMoonIllumination)
				{
					break;
				}
			}
			tracker.Update(state, events);
			LogAssert.AreEqual(0, events.Count, "starting under a full moon is not a full moon event");
		}

		[Test]
		public void AMonthHasAFullMoonAndANewMoon()
		{
			Moon();
			var state = new CelestialState();
			var tracker = new CelestialEventTracker();
			var events = new List<CelestialEvent>();
			double day = DayHours(system);
			for (double h = 0; h < day * 32; h += 0.25)
			{
				state.Compute(system, home, h, 0.0, 0.0, 0f);
				tracker.Update(state, events);
			}
			int full = events.FindAll(e => e.Kind == CelestialEventKind.FullMoon).Count;
			int fresh = events.FindAll(e => e.Kind == CelestialEventKind.NewMoon).Count;
			LogAssert.IsTrue(full >= 1 && full <= 2, $"one or two full moons in 32 days, saw {full}");
			LogAssert.IsTrue(fresh >= 1 && fresh <= 2, $"one or two new moons in 32 days, saw {fresh}");
			tracker.Reset();
			events.Clear();
			tracker.Update(state, events);
			LogAssert.AreEqual(0, events.Count, "a reset primes again");
		}

		[Test]
		public void AShowerPeaksOncePerYear()
		{
			system.MeteorShowers.Add(new MeteorShower { Name = "Test Shower", PeakDayOfYear = 10, HalfWidthDays = 2f, PeakPerHour = 80f });
			var state = new CelestialState();
			var tracker = new CelestialEventTracker();
			var events = new List<CelestialEvent>();
			double day = DayHours(system);
			for (double d = 5; d < 15; d += 0.25)
			{
				state.Compute(system, home, d * day, 0.0, 0.0, 0f);
				tracker.Update(state, events);
			}
			List<CelestialEvent> peaks = events.FindAll(e => e.Kind == CelestialEventKind.MeteorShowerPeak);
			LogAssert.AreEqual(1, peaks.Count, "one peak");
			LogAssert.AreEqual("Test Shower", peaks[0].ShowerName);
			LogAssert.IsTrue(peaks[0].Strength > 40f, $"at the peak rate, got {peaks[0].Strength}");
		}

		[Test]
		public void ACometReachesPerihelionOncePerOrbit()
		{
			CometBody comet = Make<CometBody>("Comet");
			comet.Parent = sun;
			comet.SkyRadiusKm = 10f;
			comet.Orbit = new OrbitSettings { Distance = 2f, Eccentricity = 0.6f };
			system.Bodies.Add(comet);
			double period = CelestialMath.OrbitHours(system, comet);
			var state = new CelestialState();
			var tracker = new CelestialEventTracker();
			var events = new List<CelestialEvent>();
			// Perihelion is at the start of each orbit; start a quarter in, so the window holds two.
			for (double h = period * 0.25; h < period * 2.25; h += period / 400.0)
			{
				state.Compute(system, home, h, 0.0, 0.0, 0f);
				tracker.Update(state, events);
			}
			int perihelia = events.FindAll(e => e.Kind == CelestialEventKind.CometPerihelion && e.Primary == comet).Count;
			LogAssert.AreEqual(2, perihelia, "two orbits, two perihelia");
		}

		// ── Colours ───────────────────────────────────────────────────

		[Test]
		public void StarsShowOnlyOnceTheSunIsWellDown()
		{
			SkyProfile profile = Make<SkyProfile>("Sky");
			LogAssert.AreEqual(0f, profile.Evaluate(30f).StarVisibility, "no stars by day");
			LogAssert.AreEqual(0f, profile.Evaluate(-1f).StarVisibility, "nor at sunset");
			LogAssert.AreEqual(1f, profile.Evaluate(-18f).StarVisibility, "all of them at night");
			LogAssert.AreEqual(0f, profile.Evaluate(-18f).SunIntensity, "no sunlight at night");
			LogAssert.IsTrue(profile.Evaluate(-18f).MoonIntensity > 0f, "moonlight at night");
			LogAssert.AreEqual(0f, profile.Evaluate(30f).MoonIntensity, "no moonlight by day");
			LogAssert.IsTrue(profile.Evaluate(60f).Zenith.b > profile.Evaluate(-18f).Zenith.b * 3f, "day is bluer than night");
		}

		[Test]
		public void DiscsAreDrawnLifeSizeUnlessAProfileAsksOtherwise()
		{
			// Jim's decision, 2026-09-17: life-size by default — a moon is as big as its radius and
			// distance make it — with a toggle for a flattered sky.
			SkyProfile profile = Make<SkyProfile>("Sky");
			LogAssert.IsFalse(profile.LargerThanLife, "life-size is the default");
			LogAssert.AreEqual(1f, profile.BodyScale, "moons and planets are drawn life-size");
			LogAssert.AreEqual(1f, profile.SunScale, "and so is the sun");

			profile.LargerThanLife = true;
			LogAssert.AreEqual(profile.BodyDiscScale, profile.BodyScale, "switched on, the profile's scale applies");
			LogAssert.AreEqual(profile.SunDiscScale, profile.SunScale);
			LogAssert.IsTrue(profile.SunDiscScale > 1f && profile.BodyDiscScale > 1f, "and it flatters the sky");

			try
			{
				SkyProfile.LargerThanLifeOverride = false;
				LogAssert.AreEqual(1f, profile.BodyScale, "the override beats the profile, for the test beds");
				SkyProfile.LargerThanLifeOverride = true;
				SkyProfile plain = Make<SkyProfile>("Plain");
				LogAssert.AreEqual(plain.BodyDiscScale, plain.BodyScale, "in both directions");
			}
			finally
			{
				SkyProfile.LargerThanLifeOverride = null;
			}
		}

		[Test]
		public void AnAirlessSkyIsBlackWithStarsAndHarshSun()
		{
			SkyProfile profile = Make<SkyProfile>("Moon Sky");
			profile.Airless = true;
			SkySample noon = profile.Evaluate(60f);
			LogAssert.AreEqual(1f, noon.StarVisibility);
			LogAssert.IsTrue(noon.Zenith.maxColorComponent < 0.01f, "black at noon");
			LogAssert.IsTrue(noon.SunIntensity > 1f, "the sun is harsher");
		}

		[Test]
		public void SamplesBlendEndToEnd()
		{
			SkyProfile profile = Make<SkyProfile>("Sky");
			SkySample night = profile.Evaluate(-18f), day = profile.Evaluate(60f);
			LogAssert.AreEqual(night.Zenith, SkySample.Lerp(night, day, 0f).Zenith);
			LogAssert.AreEqual(day.Zenith, SkySample.Lerp(night, day, 1f).Zenith);
			Assert.That(SkySample.Lerp(night, day, 0.5f).SunIntensity, Is.EqualTo((night.SunIntensity + day.SunIntensity) * 0.5f).Within(1e-5f));
		}

		// ── Schedules ─────────────────────────────────────────────────

		private WeatherTimeline LightningTimeline(float rate)
		{
			var template = ScriptableObject.CreateInstance<WeatherLayerTemplate>();
			template.name = "Test Lightning";
			template.Kind = WeatherLayerKind.Lightning;
			template.Channels.Add(new WeatherChannelCurve { Channel = WeatherChannel.LightningRate, Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f), Scale = 1f });
			template.AddToCache(template.name);
			created.Add(template);
			var timeline = new WeatherTimeline { TickDelta = TickDelta, Seed = 238 };
			timeline.Layers.Add(new WeatherLayerEntry { Handle = 1, TemplateID = template.ID, From = rate, To = rate });
			return timeline;
		}

		[Test]
		public void LightningIsTheSameForEveryoneAndAnyWindow()
		{
			WeatherTimeline timeline = LightningTimeline(1f);
			var whole = new List<LightningStrike>();
			var split = new List<LightningStrike>();
			var again = new List<LightningStrike>();
			SkySchedule.Lightning(timeline, 0, 1000.0, 1600.0, Vector3.zero, whole);
			SkySchedule.Lightning(timeline, 0, 1000.0, 1600.0, Vector3.zero, again);
			for (double t = 1000.0; t < 1600.0; t += 0.37)
			{
				SkySchedule.Lightning(timeline, 0, t, Math.Min(1600.0, t + 0.37), Vector3.zero, split);
			}
			LogAssert.AreEqual(whole.Count, again.Count, "the same window gives the same strikes");
			LogAssert.AreEqual(whole.Count, split.Count, "frame-sized windows give the same strikes as one big one");
			for (int i = 0; i < whole.Count; i++)
			{
				LogAssert.AreEqual(whole[i].Time, split[i].Time);
				LogAssert.AreEqual(whole[i].Seed, split[i].Seed);
				LogAssert.IsTrue(whole[i].Time >= 1000.0 && whole[i].Time < 1600.0, "inside the window");
				float distance = new Vector2(whole[i].Ground.x, whole[i].Ground.z).magnitude;
				LogAssert.IsTrue(distance >= 249f && distance <= 3001f, $"scene lightning keeps its distance, got {distance}");
			}
			// A rate of 1 is a strike about every two seconds.
			LogAssert.IsTrue(whole.Count > 220 && whole.Count < 380, $"about 300 strikes in 600 s, got {whole.Count}");
		}

		[Test]
		public void NoLightningLayerMeansNoStrikes()
		{
			WeatherTimeline timeline = LightningTimeline(0f);
			var strikes = new List<LightningStrike>();
			SkySchedule.Lightning(timeline, 0, 0.0, 600.0, Vector3.zero, strikes);
			LogAssert.AreEqual(0, strikes.Count);
			SkySchedule.Lightning(null, 0, 0.0, 600.0, Vector3.zero, strikes);
			LogAssert.AreEqual(0, strikes.Count);
		}

		[Test]
		public void ABoltRunsFromTheCloudToTheGround()
		{
			var strike = new LightningStrike { Ground = new Vector3(10f, 0f, 20f), CloudHeight = 1200f, Seed = 99 };
			var trunk = new List<Vector3>();
			var branches = new List<List<Vector3>>();
			SkySchedule.BoltPath(strike, trunk, branches);
			LogAssert.AreEqual(strike.Ground + Vector3.up * 1200f, trunk[0]);
			LogAssert.AreEqual(strike.Ground, trunk[trunk.Count - 1]);
			for (int i = 1; i < trunk.Count; i++)
			{
				LogAssert.IsTrue(trunk[i].y < trunk[i - 1].y, "the trunk only goes down");
			}
			var trunk2 = new List<Vector3>();
			var branches2 = new List<List<Vector3>>();
			SkySchedule.BoltPath(strike, trunk2, branches2);
			LogAssert.AreEqual(branches.Count, branches2.Count, "the same seed gives the same bolt");
			LogAssert.AreEqual(trunk[5], trunk2[5]);
		}

		[Test]
		public void MeteorsNeedAirAndFollowTheirShower()
		{
			home.Atmosphere = AtmosphereKind.None;
			var state = new CelestialState();
			state.Compute(system, home, 0.0, 30.0, 0.0, 0f);
			var meteors = new List<Meteor>();
			SkySchedule.Meteors(state, 0.0, 3600.0, 512, meteors);
			LogAssert.AreEqual(0, meteors.Count, "no air, no meteors");

			home.Atmosphere = AtmosphereKind.Standard;
			system.MeteorShowers.Add(new MeteorShower { Name = "Storm", PeakDayOfYear = 1, HalfWidthDays = 5f, PeakPerHour = 2000f });
			state.Compute(system, home, 0.0, 30.0, 0.0, 0f);
			LogAssert.IsTrue(state.MeteorRate >= 2000f, $"the shower is in the rate, got {state.MeteorRate}");
			SkySchedule.Meteors(state, 0.0, 600.0, 512, meteors);
			LogAssert.IsTrue(meteors.Count > 10, $"a meteor storm shows meteors, got {meteors.Count}");
			var again = new List<Meteor>();
			SkySchedule.Meteors(state, 0.0, 600.0, 512, again);
			LogAssert.AreEqual(meteors.Count, again.Count, "the same for everyone");
			var limited = new List<Meteor>();
			SkySchedule.Meteors(state, 0.0, 600.0, 3, limited);
			LogAssert.AreEqual(3, limited.Count, "the tier's budget caps them");
			foreach (Meteor meteor in meteors)
			{
				LogAssert.IsTrue(meteor.Direction.y > 0f, "meteors start above the horizon");
			}
		}

		// ── Maps and textures ────────────────────────────────────────

		[Test]
		public void TheWeatherMapPacksCoverRainSnowAndStorm()
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.CloudCover] = 0.6f;
			frame[WeatherChannel.Precipitation] = 0.5f;
			frame[WeatherChannel.SnowWeight] = 1f;
			Color texel = WeatherMap.Sample(new WeatherTimeline(), frame, Vector3.zero, 0);
			Assert.That(texel.r, Is.EqualTo(0.6f).Within(1e-4f));
			Assert.That(texel.g, Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(texel.b, Is.EqualTo(1f).Within(1e-4f), "all of it is snow");
			Assert.That(texel.a, Is.EqualTo(frame.StormSeverity).Within(1e-4f));
			LogAssert.AreEqual(0f, WeatherMap.Sample(null, new WeatherFrame(), Vector3.zero, 0).b, "no rain, no snow share");
		}

		[Test]
		public void EveryDirectionLandsOnItsCubemapFace()
		{
			const int size = 64;
			var cases = new (Vector3 d, CubemapFace face)[]
			{
				(Vector3.right, CubemapFace.PositiveX), (Vector3.left, CubemapFace.NegativeX),
				(Vector3.up, CubemapFace.PositiveY), (Vector3.down, CubemapFace.NegativeY),
				(Vector3.forward, CubemapFace.PositiveZ), (Vector3.back, CubemapFace.NegativeZ),
			};
			foreach (var (d, expected) in cases)
			{
				StarfieldBuilder.FaceOf(d, size, out CubemapFace face, out float x, out float y);
				LogAssert.AreEqual(expected, face, $"{d}");
				Assert.That(x, Is.EqualTo(size * 0.5f).Within(1e-3f));
				Assert.That(y, Is.EqualTo(size * 0.5f).Within(1e-3f));
			}
			var random = new System.Random(7);
			for (int i = 0; i < 500; i++)
			{
				var d = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
				StarfieldBuilder.FaceOf(d.normalized, size, out _, out float x, out float y);
				LogAssert.IsTrue(x >= 0f && x <= size && y >= 0f && y <= size, $"pixel inside the face for {d}");
			}
			Color hot = StarfieldBuilder.ColorOf(20000f), cool = StarfieldBuilder.ColorOf(3000f);
			LogAssert.IsTrue(hot.b > hot.r && cool.r > cool.b, "hot stars are blue, cool ones red");
		}

		[Test]
		public void SkyBodiesRespectTheLimits()
		{
			Moon();
			for (int i = 0; i < 6; i++)
			{
				WorldBody planet = Make<WorldBody>("Planet " + i);
				planet.Parent = sun;
				planet.Orbit = new OrbitSettings { Distance = 1.5f + i };
				system.Bodies.Add(planet);
			}
			var state = new CelestialState();
			state.Compute(system, home, 3.0, 0.0, 0.0, 0f);
			SkyProfile profile = Make<SkyProfile>("Sky");
			var mesh = new SkyBodyMesh();
			try
			{
				mesh.AddBodies(state, profile, new SkyLimits { Suns = 1, Moons = 0, Planets = 2, Comets = 0 });
				LogAssert.IsTrue(mesh.QuadCount <= 3, $"one sun and two planets at most, got {mesh.QuadCount}");
				mesh.Clear();
				mesh.AddBodies(state, profile, new SkyLimits { Suns = 0, Moons = 0, Planets = 0, Comets = 0 });
				LogAssert.AreEqual(0, mesh.QuadCount, "nothing allowed, nothing drawn");
			}
			finally
			{
				mesh.Dispose();
			}
		}

		// ── Sun colour ────────────────────────────────────────────────

		[Test]
		public void TheReferenceSunLeavesTheSkyAsAuthored()
		{
			sun.Tint = StarBody.ReferenceTint;
			sun.TemperatureK = StarBody.ReferenceTemperatureK;
			AssertColor(StarBody.ReferenceTint, sun.StarColor, "the reference star is its tint");
			AssertColor(Color.white, sun.SkyTint, "and leaves the sky alone");

			SkyProfile profile = Make<SkyProfile>("Sky");
			var state = new CelestialState();
			state.Compute(system, home, NoonHours(), 0.0, 0.0, 0f);
			SkySample authored = profile.Evaluate(state.SunAltitude);
			SkySample tinted = SkySystem.TintBySuns(state, authored, out Color primary);
			AssertColor(Color.white, primary, "primary tint");
			AssertColor(authored.Zenith, tinted.Zenith, "zenith");
			AssertColor(authored.SunLight, tinted.SunLight, "sunlight");
		}

		[Test]
		public void ARedSunReddensTheDaySkyAndItsLight()
		{
			sun.Tint = StarBody.ReferenceTint;
			sun.TemperatureK = 3000f;
			Color tint = sun.SkyTint;
			LogAssert.IsTrue(tint.r > 1f && tint.b < 1f, $"a cool star tints red, got {tint}");
			Assert.That(tint.r * 0.2126f + tint.g * 0.7152f + tint.b * 0.0722f, Is.EqualTo(1f).Within(1e-4f), "the tint keeps brightness");

			SkyProfile profile = Make<SkyProfile>("Sky");
			var state = new CelestialState();
			state.Compute(system, home, NoonHours(), 0.0, 0.0, 0f);
			LogAssert.IsTrue(state.SunAltitude > 20f, "noon at the equator");
			SkySample day = SkySystem.TintBySuns(state, profile.Evaluate(state.SunAltitude), out _);
			SkySample authored = profile.Evaluate(state.SunAltitude);
			LogAssert.IsTrue(day.Horizon.r / day.Horizon.b > authored.Horizon.r / authored.Horizon.b * 1.2f, "the day sky is redder");
			LogAssert.IsTrue(day.SunLight.b < authored.SunLight.b, "the sunlight is redder");
			LogAssert.IsTrue(day.AmbientSky.b < authored.AmbientSky.b, "so is the ambient light");

			SkySample nightAuthored = profile.Evaluate(-30f);
			SkySample night = SkySystem.TintBySuns(state, nightAuthored, out _);
			AssertColor(nightAuthored.Zenith, night.Zenith, "the night sky keeps its own colour");
		}

		[Test]
		public void TheTintAlsoColoursTheStar()
		{
			sun.Tint = new Color(0.5f, 1f, 0.5f);
			sun.TemperatureK = StarBody.ReferenceTemperatureK;
			AssertColor(new Color(0.5f, 1f, 0.5f), sun.StarColor, "a green tint makes a green star");
			LogAssert.IsTrue(sun.SkyTint.g > sun.SkyTint.r, "and a green sky tint");
		}

		private double NoonHours()
		{
			// Local noon at longitude 0 on day 0: search the first solar day.
			double day = DayHours(system), best = 0.0, highest = double.MinValue;
			var state = new CelestialState();
			for (double h = 0; h < day; h += day / 400.0)
			{
				state.Compute(system, home, h, 0.0, 0.0, 0f);
				if (state.SunAltitude > highest)
				{
					highest = state.SunAltitude;
					best = h;
				}
			}
			return best;
		}

		private static void AssertColor(Color expected, Color actual, string message)
		{
			float d = Mathf.Abs(expected.r - actual.r) + Mathf.Abs(expected.g - actual.g) + Mathf.Abs(expected.b - actual.b);
			LogAssert.IsTrue(d < 2e-3f, $"{message}: expected {expected}, got {actual}");
		}

		// ── Fog ───────────────────────────────────────────────────────

		[Test]
		public void FogTakesTheSkyColourUnlessTheRegionSetItsOwn()
		{
			FogState saved = FogState.FromRenderSettings();
			try
			{
				FogComposer.Reset();
				FogComposer.Base = new FogState { Enabled = true, Mode = FogMode.Linear, Color = Color.red, StartDistance = 10f, EndDistance = 200f };
				FogComposer.SetSkyColor(Color.blue);
				LogAssert.AreEqual(Color.blue, RenderSettings.fogColor, "scene fog blends into the horizon");
				FogComposer.SetRegionFog(new FogState { Enabled = true, Mode = FogMode.Linear, Color = Color.green, StartDistance = 10f, EndDistance = 200f });
				LogAssert.AreEqual(Color.green, RenderSettings.fogColor, "a region's own colour wins");
				FogComposer.Reset();
				FogComposer.Base = new FogState { Enabled = true, Mode = FogMode.Linear, Color = Color.red, StartDistance = 10f, EndDistance = 200f };
				LogAssert.AreEqual(Color.red, RenderSettings.fogColor, "a reset forgets the sky colour");
			}
			finally
			{
				FogComposer.Reset();
				saved.WriteToRenderSettings();
			}
		}

		// ── Ownership ────────────────────────────────────────────────

		private static readonly Regex SkyWrite = new Regex(@"RenderSettings\s*\.\s*(skybox|sun|ambientSkyColor|ambientEquatorColor|ambientGroundColor|customReflectionTexture|defaultReflectionMode|ambientMode)\s*=(?!=)");

		private static string CodeOnly(string source)
		{
			var lines = source.Replace("\r\n", "\n").Split('\n');
			var kept = new List<string>();
			foreach (string line in lines)
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
				{
					continue;
				}
				kept.Add(line);
			}
			return string.Join("\n", kept);
		}

		[Test]
		public void OnlyTheSkySystemWritesTheSkysRenderSettings()
		{
			string root = Path.Combine(Application.dataPath, "Scripts");
			var offenders = new List<string>();
			int owner = 0;
			foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
			{
				string normalized = file.Replace('\\', '/');
				if (normalized.Contains("/Editor/"))
				{
					continue;
				}
				string code = CodeOnly(File.ReadAllText(file));
				int writes = SkyWrite.Matches(code).Count;
				if (writes == 0)
				{
					continue;
				}
				if (normalized.EndsWith("/Weather/Sky/SkySystem.cs"))
				{
					owner += writes;
					continue;
				}
				offenders.Add(normalized.Substring(normalized.IndexOf("Assets/", StringComparison.Ordinal)));
			}
			LogAssert.IsTrue(owner > 0, "the scan found the sky system's own writes (else it is looking in the wrong place)");
			LogAssert.AreEqual(0, offenders.Count, "the sky's render settings are written only by SkySystem; also written in: " + string.Join(", ", offenders));
		}

		[Test]
		public void TheOldSkyboxActionIsGone()
		{
			LogAssert.IsNull(typeof(ChangeSkyProfileAction).Assembly.GetType("FishMMO.Shared.ChangeSkyboxAction"), "ChangeSkyProfileAction replaced it");
			string cycle = CodeOnly(File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Shared/Implementation/Entity/WorldSceneDetails/WorldDayNightCycle.cs")));
			LogAssert.IsFalse(cycle.Contains("RenderSettings.skybox"), "the cycle no longer swaps skyboxes itself");
			LogAssert.IsFalse(cycle.Contains("DynamicGI.UpdateEnvironment"), "nor refreshes GI every frame");
		}

		[Test]
		public void ChangingTheSkyProfileRaisesTheEvent()
		{
			SkyProfile profile = Make<SkyProfile>("Storm Sky");
			var action = new ChangeSkyProfileAction { Profile = profile, BlendSeconds = 7f };
			SkyProfile seen = null;
			float seconds = -1f;
			Action<SkyProfile, float> handler = (p, s) => { seen = p; seconds = s; };
			ChangeSkyProfileAction.OnChangeSkyProfile += handler;
			try
			{
				action.Execute(null, null);
			}
			finally
			{
				ChangeSkyProfileAction.OnChangeSkyProfile -= handler;
			}
			LogAssert.AreEqual(profile, seen);
			LogAssert.AreEqual(7f, seconds);
		}
	}
}
