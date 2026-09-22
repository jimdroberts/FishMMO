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

		// ── The stars belong to the system ───────────────────────────────

		/// <summary>Where a body's celestial pole points, in the system's own star frame.</summary>
		private Vector3 PoleAmongTheStars(WorldBody body, double hours, double latitude, double longitude)
		{
			var state = new CelestialState();
			state.Compute(system, body, hours, latitude, longitude, 0f);
			Vector3 poleInScene = state.EquatorialToScene.GetColumn(2);
			return state.StarsToScene.transpose.MultiplyVector(poleInScene);
		}

		[Test]
		public void AnUprightBodySeesTheStarsInItsOwnFrame()
		{
			home.AxialTiltDegrees = 0f;
			var state = new CelestialState();
			state.Compute(system, home, 40.25, 50.0, -60.0, 10f);
			for (int column = 0; column < 3; column++)
			{
				AssertDirection(state.EquatorialToScene.GetColumn(column), state.StarsToScene.GetColumn(column), $"axis {column}", 0.05f);
			}
		}

		[Test]
		public void ABodysPoleStandsItsTiltFromTheEclipticsPole()
		{
			var state = new CelestialState();
			foreach (float tilt in new[] { 0f, 23.4f, 60f, 97f })
			{
				home.AxialTiltDegrees = tilt;
				state.Compute(system, home, 13.7, 35.0, 20.0, 30f);
				float angle = Vector3.Angle(state.EquatorialToScene.GetColumn(2), state.StarsToScene.GetColumn(2));
				Assert.That(angle, Is.EqualTo(tilt).Within(0.05f), $"a world tilted {tilt}° has its pole {tilt}° from the ecliptic's");
				Assert.That(Mathf.Abs(state.StarsToScene.determinant), Is.EqualTo(1f).Within(1e-3f), "the star frame keeps lengths");
			}
		}

		[Test]
		public void TwoWorldsOfOneSystemSeeTheSameStarsFromDifferentAngles()
		{
			WorldBody other = Make<WorldBody>("Other");
			other.Parent = sun;
			other.Orbit = new OrbitSettings { Distance = 1.6f };
			other.RotationHours = 10f;
			other.AxialTiltDegrees = 60f;
			system.Bodies.Add(other);

			Vector3 homePole = PoleAmongTheStars(home, 13.7, 35.0, 20.0);
			Vector3 otherPole = PoleAmongTheStars(other, 13.7, 35.0, 20.0);
			// This is the test that failed before: with the stars turned by each body's own
			// equatorial frame the tilt cancelled, and both poles came out on the same star.
			Assert.That(Vector3.Angle(homePole, otherPole), Is.EqualTo(60f - 23.4f).Within(0.05f),
				"two worlds tilted differently have different pole stars, the difference of their tilts apart");
		}

		[Test]
		public void ABodysPoleStarDoesNotMoveWithTheHourOrThePlace()
		{
			Vector3 reference = PoleAmongTheStars(home, 13.7, 35.0, 20.0);
			foreach ((double hours, double latitude, double longitude) in new[] { (0.0, 0.0, 0.0), (977.3, -62.0, 140.0), (50000.5, 80.0, -170.0) })
			{
				AssertDirection(reference, PoleAmongTheStars(home, hours, latitude, longitude),
					$"the pole star at hour {hours}, latitude {latitude}, longitude {longitude}", 0.05f);
			}
		}

		[Test]
		public void TheSameSeedScattersTheSameStarsAndAnotherSeedOthers()
		{
			Cubemap first = StarfieldBuilder.Build(64, 238u);
			Cubemap again = StarfieldBuilder.Build(64, 238u);
			Cubemap different = StarfieldBuilder.Build(64, 239u);
			try
			{
				bool anyDifference = false;
				foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeY, CubemapFace.PositiveZ })
				{
					Color[] a = first.GetPixels(face), b = again.GetPixels(face), c = different.GetPixels(face);
					for (int i = 0; i < a.Length; i++)
					{
						Assert.That(b[i], Is.EqualTo(a[i]), $"{face} pixel {i} differs between two builds of one seed");
						anyDifference |= c[i] != a[i];
					}
				}
				Assert.That(anyDifference, Is.True, "a different seed gave the same sky");
			}
			finally
			{
				Object.DestroyImmediate(first);
				Object.DestroyImmediate(again);
				Object.DestroyImmediate(different);
			}
		}

		// ── Which way the axis leans ─────────────────────────────────────

		private WorldBody Twin(string name, float tilt, float poleLongitude)
		{
			WorldBody twin = Make<WorldBody>(name);
			twin.Parent = sun;
			twin.Orbit = home.Orbit;
			twin.RotationHours = home.RotationHours;
			twin.AxialTiltDegrees = tilt;
			twin.PoleLongitudeDegrees = poleLongitude;
			system.Bodies.Add(twin);
			return twin;
		}

		[Test]
		public void TheDefaultLeanIsTheRotationThereAlwaysWas()
		{
			// Every existing world keeps its sky and its seasons: 90° is where every axis leaned
			// before the lean could be chosen.
			Assert.That(home.PoleLongitudeDegrees, Is.EqualTo(90f));
			foreach (var direction in new[] { new Vector3d(1, 0, 0), new Vector3d(0.3, -0.8, 0.5), new Vector3d(-0.6, 0.2, -0.77) })
			{
				double tilt = 23.4 * CelestialMath.Deg2Rad;
				CelestialMath.ToEquatorial(direction, tilt, out double oldRa, out double oldDec);
				CelestialMath.ToEquatorial(direction, tilt, 90.0 * CelestialMath.Deg2Rad, out double ra, out double dec);
				Assert.That(ra, Is.EqualTo(oldRa).Within(1e-12));
				Assert.That(dec, Is.EqualTo(oldDec).Within(1e-12));
			}
		}

		[Test]
		public void ThePoleLeansTowardItsLongitude()
		{
			foreach (float longitude in new[] { 0f, 90f, 200f, 315f })
			{
				home.AxialTiltDegrees = 30f;
				home.PoleLongitudeDegrees = longitude;
				double t = 30.0 * CelestialMath.Deg2Rad, l = longitude * CelestialMath.Deg2Rad;
				var expected = new Vector3((float)(Math.Sin(t) * Math.Cos(l)), (float)(Math.Sin(t) * Math.Sin(l)), (float)Math.Cos(t));
				AssertDirection(expected, PoleAmongTheStars(home, 13.7, 35.0, 20.0), $"the pole of a world leaning toward {longitude}°", 0.05f);
			}
		}

		[Test]
		public void TwoWorldsOfOneTiltThatLeanApartHaveDifferentPoleStars()
		{
			WorldBody one = Twin("One", 23.4f, 0f);
			WorldBody other = Twin("Other", 23.4f, 180f);
			// The case the tilt alone could never separate: the same tilt used to mean the same pole star.
			Assert.That(Vector3.Angle(PoleAmongTheStars(one, 13.7, 35.0, 20.0), PoleAmongTheStars(other, 13.7, 35.0, 20.0)),
				Is.EqualTo(46.8f).Within(0.05f), "leaning opposite ways, their poles are twice the tilt apart");
		}

		[Test]
		public void WorldsThatLeanOppositeWaysHaveOppositeSeasons()
		{
			WorldBody one = Twin("One", 23.4f, 40f);
			WorldBody other = Twin("Other", 23.4f, 220f);
			double year = CelestialMath.OrbitHours(system, one);
			for (int i = 0; i < 12; i++)
			{
				double hours = year * (i + 0.37) / 12.0;
				float a = CelestialMath.Season01(system, one, hours), b = CelestialMath.Season01(system, other, hours);
				Assert.That(Mathf.Abs(Mathf.DeltaAngle(a * 360f, b * 360f)), Is.EqualTo(180f).Within(1.5f),
					$"at {hours:0} h one world is at {a:0.00} of its year and the other at {b:0.00}: half a year apart");
			}
		}

		[Test]
		public void TheWeathersMidsummerIsWhenTheSunStandsHighest()
		{
			// The defect this replaces: the weather took midsummer from the middle of the calendar,
			// a quarter of a year before the sun of the same world actually stood highest.
			double year = CelestialMath.OrbitHours(system, home);
			double highest = double.MinValue, lowest = double.MaxValue, highestAt = 0, lowestAt = 0;
			for (int i = 0; i < 2000; i++)
			{
				double hours = year * i / 2000.0;
				CelestialMath.Equatorial(system, home, sun, hours, out _, out double declination);
				if (declination > highest) { highest = declination; highestAt = hours; }
				if (declination < lowest) { lowest = declination; lowestAt = hours; }
			}
			Assert.That(CelestialMath.Season01(system, home, highestAt), Is.EqualTo(0.5f).Within(0.02f), "midsummer at the sun's highest");
			float midwinter = CelestialMath.Season01(system, home, lowestAt);
			Assert.That(Mathf.Min(midwinter, 1f - midwinter), Is.LessThan(0.02f), "midwinter at the sun's lowest");
		}

		[Test]
		public void AnUprightWorldHasNoSeasonsAndASteepOneHasThemHard()
		{
			WorldBody upright = Twin("Upright", 1.5f, 90f);
			WorldBody steep = Twin("Steep", 60f, 90f);
			double year = CelestialMath.OrbitHours(system, upright);
			float uprightSwing = 0f, steepSwing = 0f;
			for (int i = 0; i < 400; i++)
			{
				double hours = year * i / 400.0;
				// What the weather driver makes of the figure: 0 midwinter, 1 midsummer.
				float Summer(float season01) => Mathf.Sin(season01 * Mathf.PI * 2f - Mathf.PI * 0.5f) * 0.5f + 0.5f;
				uprightSwing = Mathf.Max(uprightSwing, Mathf.Abs(Summer(CelestialMath.Season01(system, upright, hours)) - 0.5f));
				steepSwing = Mathf.Max(steepSwing, Mathf.Abs(Summer(CelestialMath.Season01(system, steep, hours)) - 0.5f));
			}
			Assert.That(uprightSwing, Is.LessThan(0.04f), "a degree and a half of tilt is a degree and a half of season");
			Assert.That(steepSwing, Is.EqualTo(0.5f).Within(0.001f), "sixty degrees reaches full summer and full winter");
		}

		// ── Stars that orbit one another ─────────────────────────────────

		[Test]
		public void ALoneStarStaysAtTheCentre()
		{
			foreach (double hours in new[] { 0.0, 123.4, 99999.0 })
			{
				Vector3d at = CelestialMath.Position(system, sun, hours);
				Assert.That(Math.Sqrt(at.X * at.X + at.Y * at.Y + at.Z * at.Z), Is.EqualTo(0.0).Within(1e-12), "a system of one star is centred on it");
			}
			Assert.That(double.IsInfinity(CelestialMath.OrbitHours(system, sun)), Is.True, "and that star goes round nothing");
		}

		[Test]
		public void TwoStarsAtTheRootGoRoundThePointBetweenThem()
		{
			StarBody companion = Make<StarBody>("Companion");
			companion.Luminosity = 0.2f;
			companion.Orbit = new OrbitSettings { Distance = 0.4f, PeriodMode = OrbitPeriodMode.Authored, PeriodDays = 30f };
			system.Bodies.Add(companion);

			double massA = CelestialMath.StarMass(sun), massB = CelestialMath.StarMass(companion);
			double period = CelestialMath.OrbitHours(system, companion);
			Assert.That(double.IsInfinity(period), Is.False, "a companion has a period");
			Assert.That(CelestialMath.OrbitHours(system, sun), Is.EqualTo(period).Within(1e-9), "and the primary swings in the same time");

			Vector3d first = CelestialMath.Position(system, companion, 0.0);
			bool moved = false;
			for (int i = 0; i < 16; i++)
			{
				double hours = period * i / 16.0;
				Vector3d a = CelestialMath.Position(system, sun, hours), b = CelestialMath.Position(system, companion, hours);
				// The centre of mass stays put: that is what "orbit each other" means.
				Assert.That(a.X * massA + b.X * massB, Is.EqualTo(0.0).Within(1e-9), "centre of mass, x");
				Assert.That(a.Y * massA + b.Y * massB, Is.EqualTo(0.0).Within(1e-9), "centre of mass, y");
				Vector3d apart = b - a;
				Assert.That(Math.Sqrt(apart.X * apart.X + apart.Y * apart.Y + apart.Z * apart.Z), Is.EqualTo(0.4).Within(1e-6), "they stay their separation apart");
				Vector3d shift = b - first;
				moved |= Math.Sqrt(shift.X * shift.X + shift.Y * shift.Y) > 0.1;
			}
			Assert.That(moved, Is.True, "the companion used to hang at one point for ever");
			// The brighter, heavier star is the one that hardly moves.
			Vector3d heavy = CelestialMath.Position(system, sun, period * 0.3), light = CelestialMath.Position(system, companion, period * 0.3);
			Assert.That(heavy.X * heavy.X + heavy.Y * heavy.Y, Is.LessThan(light.X * light.X + light.Y * light.Y));
		}

		[Test]
		public void APlanetGoesWithTheStarItBelongsTo()
		{
			StarBody companion = Make<StarBody>("Companion");
			companion.Orbit = new OrbitSettings { Distance = 0.4f, PeriodMode = OrbitPeriodMode.Authored, PeriodDays = 30f };
			system.Bodies.Add(companion);
			double hours = 777.7;
			Vector3d planet = CelestialMath.Position(system, home, hours) - CelestialMath.Position(system, sun, hours);
			Assert.That(Math.Sqrt(planet.X * planet.X + planet.Y * planet.Y + planet.Z * planet.Z), Is.EqualTo(1.0).Within(1e-6),
				"home is parented to the sun, so it keeps its distance from the sun wherever the pair has swung it");
		}

		// ── What is in front of what ─────────────────────────────────────

		[Test]
		public void TheSkysBodiesAreDrawnFurthestFirstWhateverOrderTheyWereListedIn()
		{
			WorldBody near = Make<WorldBody>("Near");
			near.HasRings = true;
			WorldBody far = Make<WorldBody>("Far");
			far.HasRings = true;
			var state = new CelestialState();
			// Listed nearest first: the order that used to put the far one on top.
			state.Bodies.Add(new SkyBodyState { Body = near, Kind = SkyBodyKind.Moon, Direction = Vector3.up, LightDirection = Vector3.right, AngularRadius = 0.02f, AltitudeDegrees = 40f, Illumination = 1f, DistanceKm = 4.0e5 });
			state.Bodies.Add(new SkyBodyState { Body = far, Kind = SkyBodyKind.Planet, Direction = Vector3.up, LightDirection = Vector3.right, AngularRadius = 0.02f, AltitudeDegrees = 40f, Illumination = 1f, DistanceKm = 9.0e8 });

			var mesh = new SkyBodyMesh();
			try
			{
				mesh.Clear();
				mesh.AddBodies(state, null, new SkyLimits());
				mesh.Upload();
				var rings = new List<CelestialBody>();
				var kinds = new List<SkyBodyMesh.StepKind>();
				foreach (SkyBodyMesh.Step step in mesh.Steps)
				{
					kinds.Add(step.Kind);
					if (step.Kind == SkyBodyMesh.StepKind.Ring)
					{
						rings.Add(step.Body.Body);
					}
				}
				// Disc, its ring, the nearer disc, its ring: each ring straight after its own body,
				// and the nearer pair after — so over — the further one.
				Assert.That(kinds, Is.EqualTo(new[] { SkyBodyMesh.StepKind.Quads, SkyBodyMesh.StepKind.Ring, SkyBodyMesh.StepKind.Quads, SkyBodyMesh.StepKind.Ring }));
				Assert.That(rings, Is.EqualTo(new CelestialBody[] { far, near }), "the far body's ring is drawn first, the near one's last");
				Assert.That(mesh.Mesh.subMeshCount, Is.EqualTo(2), "one sub-mesh to each run of plain quads");
				// And the nearer one hides what is behind its disc whichever part of it is lit: it is
				// listed as an occluder, ranked above the far body, so the far body's pixels inside
				// its disc are not drawn and the sky shows in its dark limb instead of the planet.
				Assert.That(mesh.Occluders.Count, Is.EqualTo(2));
				Assert.That(mesh.OccluderRanks[0].x, Is.EqualTo(0f), "the far body is ranked furthest");
				Assert.That(mesh.OccluderRanks[1].x, Is.EqualTo(1f), "the near body is ranked above it");
				Assert.That(mesh.Steps[1].Rank, Is.EqualTo(0f), "and a ring carries its own body's rank, so its body does not hide it");
			}
			finally
			{
				mesh.Dispose();
			}
		}

		// ── Eclipses as they are drawn ───────────────────────────────────

		[Test]
		public void AnEclipseLooksLikeAnOrdinaryDayUntilTheLastTenth()
		{
			// The adapted eye: half the sun gone is barely a tenth darker; the plunge is at the end.
			Assert.That(SolarEclipseInfo.DarknessOf(0f), Is.EqualTo(0f));
			Assert.That(SolarEclipseInfo.DarknessOf(0.5f), Is.LessThan(0.15f));
			Assert.That(SolarEclipseInfo.DarknessOf(0.9f), Is.EqualTo(0.4f).Within(0.02f));
			Assert.That(SolarEclipseInfo.DarknessOf(0.99f), Is.EqualTo(0.8f).Within(0.02f));
			Assert.That(SolarEclipseInfo.DarknessOf(1f), Is.EqualTo(1f));
		}

		[Test]
		public void TheEclipseHasPhasesAndAnAnnularOneIsNeverTotal()
		{
			Moon();
			var state = new CelestialState();
			bool partial = false, total = false;
			for (int i = 0; i < 40000 && !(partial && total); i++)
			{
				state.Compute(system, home, i * 0.05, 0.0, 0.0, 0f);
				SolarEclipseInfo drawn = state.SolarEclipseAsDrawn(1f, 1f);
				if (drawn.Phase == SolarEclipsePhase.Partial)
				{
					partial = true;
					Assert.That(drawn.Obscuration, Is.GreaterThan(0f).And.LessThan(1f));
					Assert.That(drawn.Totality, Is.LessThan(1f));
				}
				if (drawn.Phase == SolarEclipsePhase.Total)
				{
					total = true;
					Assert.That(drawn.Obscuration, Is.EqualTo(1f).Within(1e-4f));
					Assert.That(drawn.Totality, Is.EqualTo(1f).Within(1e-4f), "wholly covered is totality");
					Assert.That(drawn.Darkness, Is.EqualTo(1f));
					Assert.That(drawn.Magnitude, Is.GreaterThanOrEqualTo(1f));
				}
			}
			if (!partial || !total)
			{
				Assert.Inconclusive("no total eclipse in the hours searched from this fixture's orbit");
			}
			// A cover smaller than the sun: the discs meet, but a ring of sun is always left.
			WorldBody moon = (WorldBody)state.Bodies[state.Moon].Body;
			moon.SkyRadiusKm *= 0.85f;
			for (int i = 0; i < 40000; i++)
			{
				state.Compute(system, home, i * 0.05, 0.0, 0.0, 0f);
				SolarEclipseInfo drawn = state.SolarEclipseAsDrawn(1f, 1f);
				Assert.That(drawn.Phase, Is.Not.EqualTo(SolarEclipsePhase.Total));
				Assert.That(drawn.Totality, Is.EqualTo(0f), "an annular eclipse is broad daylight");
				if (drawn.Phase == SolarEclipsePhase.Annular)
				{
					Assert.That(drawn.Obscuration, Is.LessThan(0.999f));
					return;
				}
			}
			Assert.Inconclusive("no annular eclipse in the hours searched");
		}

		[Test]
		public void APlanetsShadowNarrowsWithDistanceAndTheMoonTakesABite()
		{
			WorldBody moon = Moon();
			var state = new CelestialState();
			CelestialState.PlanetShadowAt(system, moon, 100.0, new Vector3d(0, 0, 0), out double umbra, out double penumbra, out _, out double along);
			if (along > 0.0)
			{
				Assert.That(umbra, Is.LessThan(home.SkyRadiusKm), "the umbra is narrower than the planet by the time it reaches the moon");
				Assert.That(penumbra, Is.GreaterThan(home.SkyRadiusKm), "and the penumbra is wider");
			}
			// Across a whole orbit the moon is never MORE in the umbra than a whole disc, and the phase
			// names follow the umbral share.
			double period = CelestialMath.OrbitHours(system, moon);
			for (int i = 0; i < 400; i++)
			{
				state.Compute(system, home, period * i / 400.0, 0.0, 0.0, 0f);
				if (state.Moon < 0) continue;
				SkyBodyState body = state.Bodies[state.Moon];
				Assert.That(body.Shadowed, Is.InRange(0f, 1f));
				if (body.ShadowPhase == LunarEclipsePhase.Total) Assert.That(body.Shadowed, Is.GreaterThan(0.99f));
				if (body.ShadowPhase == LunarEclipsePhase.Penumbral) Assert.That(body.Shadowed, Is.EqualTo(0f));
				if (body.ShadowPhase != LunarEclipsePhase.None)
				{
					Assert.That(body.UmbraRadius, Is.GreaterThan(0f).And.LessThan(body.PenumbraRadius), "the shadow has an umbra inside its penumbra");
				}
			}
		}

		[Test]
		public void LargerThanLifeDiscsAreInEclipseForAsLongAsTheyOverlapOnScreen()
		{
			Moon();
			var state = new CelestialState();
			// Find a moment the true discs are close but not touching: no eclipse, yet at 1.6 times
			// the size the drawn discs overlap. That is the stretch in which the sun shone through
			// the body in front of it.
			bool found = false;
			for (int i = 0; i < 40000 && !found; i++)
			{
				double hours = i * 0.05;
				state.Compute(system, home, hours, 0.0, 0.0, 0f);
				if (state.Sun < 0 || state.Moon < 0 || state.SolarEclipse > 0f)
				{
					continue;
				}
				SolarEclipseInfo drawn = state.SolarEclipseAsDrawn(1.6f, 1.6f);
				if (drawn.Obscuration > 0.02f)
				{
					found = true;
					Assert.That(drawn.Covering, Is.Not.Null, "and it names the body in front");
					Assert.That(state.SolarEclipseAsDrawn(1f, 1f).Obscuration, Is.EqualTo(state.SolarEclipse), "life-size, it is the true eclipse");
				}
			}
			if (!found)
			{
				// About the fixture's orbit, not about the code under test: nothing to conclude.
				Assert.Inconclusive("no near miss between the sun and the moon in the hours searched, so there was nothing to test with");
			}
		}

		// ── How much air ─────────────────────────────────────────────────

		[Test]
		public void NoAirNoWeatherAndThinAirLittle()
		{
			var storm = new WeatherFrame();
			storm[WeatherChannel.CloudCover] = 0.9f;
			storm[WeatherChannel.CloudDensity] = 0.9f;
			storm[WeatherChannel.Precipitation] = 0.8f;
			storm[WeatherChannel.FogDensity] = 0.4f;
			storm[WeatherChannel.LightningRate] = 0.6f;
			storm[WeatherChannel.WindSpeed] = 0.5f;

			WeatherFrame none = WeatherDriver.UnderAtmosphere(storm, AtmosphereKind.None);
			foreach (WeatherChannel channel in new[] { WeatherChannel.CloudCover, WeatherChannel.Precipitation, WeatherChannel.FogDensity, WeatherChannel.LightningRate, WeatherChannel.WindSpeed })
			{
				Assert.That(none[channel], Is.EqualTo(0f), $"an airless world has no {channel}");
			}
			WeatherFrame thin = WeatherDriver.UnderAtmosphere(storm, AtmosphereKind.Thin);
			WeatherFrame same = WeatherDriver.UnderAtmosphere(storm, AtmosphereKind.Standard);
			WeatherFrame thick = WeatherDriver.UnderAtmosphere(storm, AtmosphereKind.Thick);
			Assert.That(same[WeatherChannel.CloudCover], Is.EqualTo(storm[WeatherChannel.CloudCover]), "standard air is what the field was tuned for");
			Assert.That(thin[WeatherChannel.Precipitation], Is.LessThan(same[WeatherChannel.Precipitation] * 0.5f));
			Assert.That(thin[WeatherChannel.CloudCover], Is.LessThan(same[WeatherChannel.CloudCover]));
			Assert.That(thin[WeatherChannel.WindSpeed], Is.GreaterThan(same[WeatherChannel.WindSpeed]), "thin air runs faster");
			Assert.That(thick[WeatherChannel.CloudCover], Is.GreaterThan(same[WeatherChannel.CloudCover]));
			Assert.That(WeatherDriver.UnderAtmosphere(new WeatherFrame(), AtmosphereKind.Thick)[WeatherChannel.FogDensity], Is.GreaterThan(0f), "thick air never quite loses its haze");
			Assert.That(WeatherDriver.FormationScale(AtmosphereKind.None), Is.EqualTo(0f));
		}

		// ── How far from its suns ────────────────────────────────────────

		[Test]
		public void AWorldFurtherFromItsSunIsColderAndItsAirCarriesLess()
		{
			WorldBody far = Make<WorldBody>("Far");
			far.Parent = sun;
			far.Orbit = new OrbitSettings { Distance = 4f };
			far.RotationHours = 6f;
			system.Bodies.Add(far);

			CelestialMath.ClimateOffsets(system, home, 100.0, out float homeTemperature, out _);
			CelestialMath.ClimateOffsets(system, far, 100.0, out float farTemperature, out _);
			Assert.That(Mathf.Abs(homeTemperature), Is.LessThan(0.1f), "the home world is what the scale is measured from");
			Assert.That(farTemperature, Is.EqualTo(-1f).Within(0.001f), "four times as far out is a sixteenth of the light: frozen");

			// And the weather follows: the same unsettled air is a storm at home and thin dry snow out there.
			var unsettled = new WeatherDriver.Synoptic { Humidity = 0.8f, Instability = 0.7f, Pressure = -0.5f };
			WeatherDriver.Synoptic atHome = WeatherDriver.InClimate(unsettled, homeTemperature);
			WeatherDriver.Synoptic outThere = WeatherDriver.InClimate(unsettled, farTemperature);
			Assert.That(WeatherDriver.InClimate(unsettled, 0f).Humidity, Is.EqualTo(unsettled.Humidity), "at the temperate zero the field is as it was tuned");
			Assert.That(outThere.Humidity, Is.LessThan(atHome.Humidity * 0.55f), "cold air holds well under half the water");
			Assert.That(outThere.Instability, Is.LessThan(atHome.Instability * 0.4f), "and cold air is stable air");
			WeatherFrame homeSky = WeatherDriver.Background(atHome), farSky = WeatherDriver.Background(outThere);
			Assert.That(farSky[WeatherChannel.Precipitation], Is.LessThan(homeSky[WeatherChannel.Precipitation]));
			Assert.That(farSky[WeatherChannel.CloudCover], Is.LessThan(homeSky[WeatherChannel.CloudCover]));
		}

		// ── The sky the air gives ────────────────────────────────────────

		[Test]
		public void StandardAirGivesTheSkyThatWasPainted()
		{
			// The model's free numbers were fitted to the Temperate Sky's gradients, so that the home
			// world keeps its look. These are that asset's own values; if the model drifts, this says so.
			SkySample noon = AtmosphereModel.Evaluate(AtmosphereKind.Standard, 1f, Color.white, 90f);
			Assert.That(noon.Zenith.r, Is.EqualTo(0.20f).Within(0.1f));
			Assert.That(noon.Zenith.g, Is.EqualTo(0.42f).Within(0.1f));
			Assert.That(noon.Zenith.b, Is.EqualTo(0.86f).Within(0.1f));
			Assert.That(noon.Horizon.r, Is.EqualTo(0.70f).Within(0.1f));
			Assert.That(noon.Horizon.b, Is.EqualTo(0.95f).Within(0.1f));
			Assert.That(noon.SunIntensity, Is.EqualTo(1.3f).Within(0.05f), "a clear noon is as bright as it was");
			Assert.That(noon.StarVisibility, Is.EqualTo(0f), "no stars through our own air by day");

			SkySample sunset = AtmosphereModel.Evaluate(AtmosphereKind.Standard, 1f, Color.white, 0f);
			Assert.That(sunset.Horizon.r / sunset.Horizon.b, Is.GreaterThan(noon.Horizon.r / noon.Horizon.b * 2f), "the horizon goes orange as the sun goes down");
			Assert.That(sunset.SunLight.b, Is.LessThan(noon.SunLight.b), "and so does the sunlight");

			SkySample night = AtmosphereModel.Evaluate(AtmosphereKind.Standard, 1f, Color.white, -18f);
			Assert.That(night.Zenith.b, Is.EqualTo(0.03f).Within(0.005f), "deep night is the airglow and nothing else");
			Assert.That(night.SunIntensity, Is.EqualTo(0f));
			Assert.That(night.StarVisibility, Is.EqualTo(1f));
		}

		[Test]
		public void TheAirDecidesTheSky()
		{
			SkySample home = AtmosphereModel.Evaluate(AtmosphereKind.Standard, 1f, Color.white, 60f);
			SkySample thin = AtmosphereModel.Evaluate(AtmosphereKind.Thin, 1f, Color.white, 60f);
			SkySample dusty = AtmosphereModel.Evaluate(AtmosphereKind.Thin, 6f, new Color(0.85f, 0.55f, 0.3f), 60f);
			SkySample thick = AtmosphereModel.Evaluate(AtmosphereKind.Thick, 1f, Color.white, 60f);
			SkySample none = AtmosphereModel.Evaluate(AtmosphereKind.None, 1f, Color.white, 60f);

			Assert.That(thin.Zenith.b, Is.LessThan(home.Zenith.b * 0.4f), "thin air: a dark sky");
			Assert.That(thin.StarVisibility, Is.GreaterThan(0.1f), "through which the stars show by day");
			Assert.That(thin.SunIntensity, Is.GreaterThan(home.SunIntensity), "and a harder sun");
			// The case that scaling the dust by the gas got wrong: every thin-aired world came out blue.
			Assert.That(dusty.Zenith.r, Is.GreaterThan(dusty.Zenith.b), "thin air full of rust dust is tan, not blue");
			Assert.That(thin.Zenith.b, Is.GreaterThan(thin.Zenith.r), "while clean thin air is still blue");
			Assert.That(thick.Zenith.maxColorComponent, Is.LessThanOrEqualTo(1f), "no sky is brighter than what lights it");
			Assert.That(thick.Zenith.r / thick.Zenith.b, Is.GreaterThan(home.Zenith.r / home.Zenith.b), "thick air is paler, not bluer");
			Assert.That(thick.SunIntensity, Is.LessThan(home.SunIntensity), "and its sun is dimmer");
			Assert.That(none.Zenith.maxColorComponent, Is.LessThan(0.01f), "no air, a black sky");
			Assert.That(none.StarVisibility, Is.EqualTo(1f));
		}

		[Test]
		public void APaintedSkyIsUsedOnlyWhenAskedFor()
		{
			SkyProfile profile = Make<SkyProfile>("Cursed");
			profile.Zenith = SkyProfile.Make(Color.magenta, Color.magenta, Color.magenta, Color.magenta, Color.magenta);
			WorldBody dusty = Make<WorldBody>("Dusty");
			dusty.Atmosphere = AtmosphereKind.Thin;

			Assert.That(profile.Evaluate(60f, dusty).Zenith, Is.Not.EqualTo(Color.magenta), "by default the air decides");
			profile.UseAuthoredColours = true;
			Assert.That(profile.Evaluate(60f, dusty).Zenith, Is.EqualTo(Color.magenta), "painted colours, exactly as painted, when the profile says so");
			dusty.Atmosphere = AtmosphereKind.None;
			Assert.That(profile.Evaluate(60f, dusty).Zenith.maxColorComponent, Is.LessThan(0.01f), "but no paint stands in for no air");
		}

		// ── Aurora ───────────────────────────────────────────────────────

		[Test]
		public void TheAuroraStandsInARingRoundThePolesAndAStormDrivesItOut()
		{
			const uint Seed = 238u;
			double quietAt = double.NaN, stormAt = double.NaN;
			for (int i = 0; i < 40000 && (double.IsNaN(quietAt) || double.IsNaN(stormAt)); i++)
			{
				double t = i * 1800.0;
				float activity = WeatherDriver.GeomagneticActivity(Seed, t, 0.25f);
				if (double.IsNaN(quietAt) && activity < 0.01f) quietAt = t;
				if (double.IsNaN(stormAt) && activity > 0.8f) stormAt = t;
			}
			Assert.That(double.IsNaN(quietAt) || double.IsNaN(stormAt), Is.False, "a year of the clock has both a quiet hour and a great storm in it");

			float At(double t, float latitude, float field = 1f) => WeatherDriver.Aurora(Seed, t, latitude, 0.25f, field, 1f);
			// Quiet: a faint ring in the auroral zone and nothing anywhere else.
			Assert.That(At(quietAt, 67f), Is.GreaterThan(0.15f), "the ring is always there, faintly");
			Assert.That(At(quietAt, 50f), Is.LessThan(0.01f), "and the middle latitudes see nothing on a quiet night");
			Assert.That(At(quietAt, 88f), Is.LessThan(0.01f), "nor the polar cap, inside the ring");
			// A great storm: bright in the zone AND reaching the middle latitudes.
			Assert.That(At(stormAt, 67f), Is.GreaterThan(0.7f), "the far north has its best night in the same storm");
			Assert.That(At(stormAt, 52f), Is.GreaterThan(0.5f), "which is what brings it down to fifty degrees");
			Assert.That(At(stormAt, 30f), Is.LessThan(0.01f), "but never to the tropics");
			Assert.That(At(stormAt, -67f), Is.EqualTo(At(stormAt, 67f)), "and there is a ring round the other pole");
			// Needs a field, and the air does the rest.
			Assert.That(At(stormAt, 67f, 0f), Is.EqualTo(0f), "no magnetic field, no aurora");
			var lit = new WeatherFrame();
			lit[WeatherChannel.Aurora] = 1f;
			Assert.That(WeatherDriver.UnderAtmosphere(lit, AtmosphereKind.None)[WeatherChannel.Aurora], Is.EqualTo(0f), "no air to glow");
			Assert.That(WeatherDriver.UnderAtmosphere(lit, AtmosphereKind.Thin)[WeatherChannel.Aurora], Is.EqualTo(0.5f));
		}

		// ── Toward the poles ─────────────────────────────────────────────

		[Test]
		public void FogKeepsTheSunsHoursNotTheClocks()
		{
			// Damp, still air: a fog if the night allows one.
			WeatherDriver.Synoptic At(float localTime01) => new WeatherDriver.Synoptic { Humidity = 0.8f, LocalTime01 = localTime01 };
			float Fog(WeatherDriver.Synoptic air) => WeatherDriver.Background(air)[WeatherChannel.FogDensity];

			float dawnFog = Fog(At(0.25f)), noonFog = Fog(At(0.5f));
			Assert.That(dawnFog, Is.GreaterThan(noonFog * 2f), "an ordinary day: thick at dawn, thin by noon");

			// The midnight sun: the clock says four in the morning, the sun has not set for a month.
			Assert.That(Fog(WeatherDriver.UnderSun(At(0.25f), 1f)), Is.EqualTo(noonFog).Within(1e-4f), "no night, so no night's fog");
			// The polar night: the clock says noon, and there is no sun to burn anything off.
			Assert.That(Fog(WeatherDriver.UnderSun(At(0.5f), 0f)), Is.GreaterThan(noonFog * 2f), "no morning, so the fog stays");
			// And an ordinary place is left exactly alone.
			Assert.That(Fog(WeatherDriver.UnderSun(At(0.25f), 0.5f)), Is.EqualTo(dawnFog).Within(1e-6f));
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
			LogAssert.AreEqual(0f, CelestialState.ShadowDepth(system, home, 0.0, star, out _), "planets are never shadowed");
			double period = CelestialMath.OrbitHours(system, moon);
			float deepest = 0f;
			for (double h = 0; h < period; h += period / 2000.0)
			{
				deepest = Mathf.Max(deepest, CelestialState.ShadowDepth(system, moon, h, CelestialMath.Position(system, sun, h), out _));
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
			// No air is the BODY's to say, not a profile's: any profile, on an airless body.
			SkyProfile profile = Make<SkyProfile>("Moon Sky");
			WorldBody airless = Make<WorldBody>("Airless");
			airless.Atmosphere = AtmosphereKind.None;
			SkySample noon = profile.Evaluate(60f, airless);
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
