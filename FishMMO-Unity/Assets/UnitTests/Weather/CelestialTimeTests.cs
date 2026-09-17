using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Celestial;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Day and night from the solar system, and the world clock every scene reads them from.
	/// </summary>
	/// <remarks>
	/// A body's day is its rotation measured against its sun, not a number typed into a scene. The
	/// tests build a small system in memory and check that the sky agrees with the periods: noon
	/// comes back once per solar day, a locked moon keeps its planet overhead, and starlight falls
	/// off with distance.
	/// </remarks>
	[TestFixture]
	public class CelestialTimeTests
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

		private WorldBody Planet(string name, float distance)
		{
			WorldBody planet = Make<WorldBody>(name);
			planet.Parent = sun;
			planet.Orbit = new OrbitSettings { Distance = distance };
			system.Bodies.Add(planet);
			return planet;
		}

		private WorldBody LockedMoon()
		{
			WorldBody moon = Make<WorldBody>("Moon");
			moon.Parent = home;
			moon.TidallyLocked = true;
			moon.AxialTiltDegrees = 0f;
			moon.Orbit = new OrbitSettings { Distance = 384f, PeriodDays = 27.3f };
			system.Bodies.Add(moon);
			return moon;
		}

		/// <summary>The longitude where the sun is on the meridian at a moment.</summary>
		private double SubsolarLongitude(WorldBody body, double hours)
		{
			CelestialMath.SunEquatorial(system, body, hours, out double ra, out _);
			return CelestialMath.WrapPi(ra - CelestialMath.RotationAngle(system, body, hours)) * CelestialMath.Rad2Deg;
		}

		// ── Periods ───────────────────────────────────────────────────

		[Test]
		public void ASixHourTurnMakesASlightlyLongerSolarDay()
		{
			Assert.That(CelestialMath.HomeSolarDayHours(system), Is.EqualTo(6.0 * (1.0 + 1.0 / 365.0)).Within(1e-9));
			Assert.That(CelestialMath.SolarDayHours(system, home), Is.EqualTo(6.016438).Within(1e-5));
			Assert.That(CelestialMath.YearHours(system), Is.EqualTo(365 * CelestialMath.HomeSolarDayHours(system)).Within(1e-6));
		}

		[Test]
		public void AYearIsTheCalendarsDayCount()
		{
			CalendarProfile calendar = Make<CalendarProfile>("Calendar");
			calendar.DaysPerYear = 100;
			calendar.Months = new List<CalendarMonth> { new CalendarMonth { Name = "A", Days = 60 }, new CalendarMonth { Name = "B", Days = 40 } };
			system.Calendar = calendar;
			Assert.That(CelestialMath.YearHours(system), Is.EqualTo(100 * 6.0 * 1.01).Within(1e-6));

			calendar.ToDate(159, out long year, out int month, out int day);
			LogAssert.AreEqual(2L, year);
			LogAssert.AreEqual(1, month);
			LogAssert.AreEqual(60, day);
			calendar.ToDate(-1, out year, out month, out day);
			LogAssert.AreEqual(0L, year, "the day before the epoch is in the year before");
			LogAssert.AreEqual(2, month);
			LogAssert.AreEqual(40, day);
		}

		[Test]
		public void NoonComesBackOncePerSolarDay()
		{
			home.AxialTiltDegrees = 0f;
			double day = CelestialMath.SolarDayHours(system, home);
			foreach (double hours in new[] { 0.0, 1.25, 500.0, 123456.789 })
			{
				double now = CelestialMath.LocalTime01(system, home, hours, 30.0);
				double next = CelestialMath.LocalTime01(system, home, hours + day, 30.0);
				double difference = Math.Abs(now - next);
				Assert.That(Math.Min(difference, 1.0 - difference), Is.LessThan(1e-6), $"at {hours} h");
			}
		}

		[Test]
		public void LocalTimeAdvancesWithTheClock()
		{
			double day = CelestialMath.SolarDayHours(system, home);
			double a = CelestialMath.LocalTime01(system, home, 10.0, 0.0);
			double b = CelestialMath.LocalTime01(system, home, 10.0 + day / 4.0, 0.0);
			double step = b - a - Math.Floor(b - a);
			Assert.That(step, Is.EqualTo(0.25).Within(0.01), "a quarter of a solar day later is a quarter of the clock later");
		}

		[Test]
		public void EastIsLaterThanWest()
		{
			double west = CelestialMath.LocalTime01(system, home, 10.0, 0.0);
			double east = CelestialMath.LocalTime01(system, home, 10.0, 15.0);
			double step = east - west - Math.Floor(east - west);
			Assert.That(step, Is.EqualTo(1.0 / 24.0).Within(1e-9), "15° of longitude is one hour");
		}

		[Test]
		public void ItIsNoonWhereTheSunIsOverhead()
		{
			const double hours = 777.7;
			double longitude = SubsolarLongitude(home, hours);
			CelestialMath.SunEquatorial(system, home, hours, out _, out double declination);
			Assert.That(CelestialMath.LocalTime01(system, home, hours, longitude), Is.EqualTo(0.5).Within(1e-9));
			double altitude = CelestialMath.SunAltitude(system, home, hours, declination * CelestialMath.Rad2Deg, longitude);
			Assert.That(altitude * CelestialMath.Rad2Deg, Is.EqualTo(90.0).Within(1e-4));
			LogAssert.IsTrue(CelestialMath.IsDaylight(system, home, hours, 0.0, longitude));
			LogAssert.IsFalse(CelestialMath.IsDaylight(system, home, hours, 0.0, longitude + 180.0), "the far side is in night");
		}

		[Test]
		public void AnUprightWorldHasEqualDayAndNight()
		{
			home.AxialTiltDegrees = 0f;
			double day = CelestialMath.SolarDayHours(system, home);
			foreach (double latitude in new[] { 0.0, 45.0, -60.0 })
			{
				Assert.That(CelestialMath.DaylightHours(system, home, 1000.0, latitude), Is.EqualTo(day / 2.0).Within(1e-6), $"at latitude {latitude}");
			}
		}

		[Test]
		public void ATiltedWorldHasSeasonsThatMirrorAcrossTheEquator()
		{
			double year = CelestialMath.YearHours(system);
			double solstice = -1;
			double longest = 0;
			for (int i = 0; i < 100; i++)
			{
				double hours = year * i / 100.0;
				double daylight = CelestialMath.DaylightHours(system, home, hours, 45.0);
				if (daylight > longest)
				{
					longest = daylight;
					solstice = hours;
				}
			}
			double day = CelestialMath.SolarDayHours(system, home);
			LogAssert.IsTrue(longest > day * 0.6, $"45° north at midsummer should have well over half a day of light, got {longest / day:0.00}");
			Assert.That(CelestialMath.DaylightHours(system, home, solstice, -45.0), Is.EqualTo(day - longest).Within(1e-6));
			Assert.That(CelestialMath.DaylightHours(system, home, solstice + year / 2.0, 45.0), Is.EqualTo(day - longest).Within(day * 0.02),
				"half a year later the long day is in the south");
			LogAssert.AreEqual(day, CelestialMath.DaylightHours(system, home, solstice, 89.0), "midnight sun near the summer pole");
		}

		[Test]
		public void RetrogradeRotationShortensTheDay()
		{
			WorldBody prograde = Planet("Prograde", 1.5f);
			WorldBody retrograde = Planet("Retrograde", 1.5f);
			retrograde.Retrograde = true;
			double forward = CelestialMath.SolarDayHours(system, prograde);
			double backward = CelestialMath.SolarDayHours(system, retrograde);
			LogAssert.IsTrue(forward > 6.0, $"prograde {forward}");
			LogAssert.IsTrue(backward < 6.0, $"retrograde {backward}");
		}

		[Test]
		public void FurtherPlanetsHaveLongerYears()
		{
			WorldBody outer = Planet("Outer", 4f);
			Assert.That(CelestialMath.OrbitHours(system, outer), Is.EqualTo(CelestialMath.YearHours(system) * 8.0).Within(1e-6), "Kepler: T² ∝ a³");
			outer.Orbit = new OrbitSettings { Distance = 4f, PeriodMode = OrbitPeriodMode.Authored, PeriodDays = 10f };
			Assert.That(CelestialMath.OrbitHours(system, outer), Is.EqualTo(10.0 * CelestialMath.HomeSolarDayHours(system)).Within(1e-9));
		}

		[Test]
		public void ALockedMoonKeepsItsPlanetOverhead()
		{
			WorldBody moon = LockedMoon();
			double month = CelestialMath.OrbitHours(system, moon);
			Assert.That(CelestialMath.RotationHours(system, moon), Is.EqualTo(month).Within(1e-9), "a locked moon turns once per orbit");
			LogAssert.IsTrue(CelestialMath.SolarDayHours(system, moon) > month, "its sun-to-sun day is its synodic month, longer than the orbit");

			foreach (double hours in new[] { 0.0, month * 0.3, month * 2.7 })
			{
				CelestialMath.Equatorial(system, moon, home, hours, out double ra, out double dec);
				double hourAngle = CelestialMath.HourAngle(system, moon, hours, 0.0, ra);
				double altitude = CelestialMath.Altitude(0.0, dec, hourAngle) * CelestialMath.Rad2Deg;
				LogAssert.IsTrue(altitude > 80.0, $"the planet stays over longitude 0, altitude {altitude:0.0}° at {hours:0} h");
			}
		}

		[Test]
		public void AMoonCirclesItsPlanet()
		{
			WorldBody moon = LockedMoon();
			double distance = (CelestialMath.Position(system, moon, 123.0) - CelestialMath.Position(system, home, 123.0)).Magnitude;
			Assert.That(distance * CelestialMath.AuKm, Is.EqualTo(384000.0).Within(1.0));
		}

		[Test]
		public void KeplersEquationIsSolved()
		{
			foreach (double e in new[] { 0.0, 0.3, 0.9 })
			{
				foreach (double m in new[] { 0.1, 1.0, 3.0, 5.5 })
				{
					double E = CelestialMath.SolveKepler(m, e);
					Assert.That(E - e * Math.Sin(E), Is.EqualTo(m).Within(1e-9), $"e {e} M {m}");
				}
			}
		}

		// ── Appearance ────────────────────────────────────────────────

		[Test]
		public void ACloseBodyFillsMoreOfTheSky()
		{
			double near = CelestialMath.AngularDiameter(1737, 384400);
			double far = CelestialMath.AngularDiameter(1737, 3844000);
			Assert.That(near * CelestialMath.Rad2Deg, Is.EqualTo(0.518).Within(0.002), "the real moon is about half a degree across");
			LogAssert.IsTrue(CelestialMath.SkyFraction(near) > CelestialMath.SkyFraction(far));
			Assert.That(CelestialMath.SkyFraction(CelestialMath.AngularDiameter(7000, 6000)), Is.EqualTo(1.0).Within(1e-9), "standing inside a body, it fills the sky");
		}

		// ── Climate ───────────────────────────────────────────────────

		[Test]
		public void TheHomeWorldHasNoClimateOffset()
		{
			CelestialMath.ClimateOffsets(system, home, 500.0, out float temperature, out float humidity);
			Assert.That(temperature, Is.EqualTo(0f).Within(1e-4f));
			Assert.That(humidity, Is.EqualTo(0f).Within(1e-4f));
		}

		[Test]
		public void CloserToTheSunIsWarmer()
		{
			float previous = float.MaxValue;
			foreach (float distance in new[] { 0.4f, 0.7f, 1f, 1.5f, 3f })
			{
				WorldBody planet = Planet("At " + distance, distance);
				CelestialMath.ClimateOffsets(system, planet, 0.0, out float temperature, out _);
				LogAssert.IsTrue(temperature < previous || temperature <= -1f, $"{distance} AU gave {temperature}, not colder than {previous}");
				LogAssert.IsTrue(temperature >= -1f && temperature <= 1f, "offsets stay in the climate model's range");
				previous = temperature;
			}
		}

		[Test]
		public void AnEccentricOrbitMakesSeasonsOfDistance()
		{
			home.Orbit = new OrbitSettings { Distance = 1f, Eccentricity = 0.2f };
			double year = CelestialMath.YearHours(system);
			CelestialMath.ClimateOffsets(system, home, 0.0, out float perihelion, out _);
			CelestialMath.ClimateOffsets(system, home, year / 2.0, out float aphelion, out _);
			LogAssert.IsTrue(perihelion > 0.05f, $"closest approach is warmer than average, got {perihelion}");
			LogAssert.IsTrue(aphelion < -0.05f, $"furthest point is colder than average, got {aphelion}");
		}

		[Test]
		public void AnAirlessBodyIsDryAndAThickAirIsWarm()
		{
			WorldBody airless = Planet("Airless", 1f);
			airless.Atmosphere = AtmosphereKind.None;
			CelestialMath.ClimateOffsets(system, airless, 0.0, out float coldT, out float dryH);
			LogAssert.AreEqual(-1f, dryH);
			LogAssert.IsTrue(coldT < 0f);
			LogAssert.IsFalse(airless.HasWeather, "no air, no weather");

			WorldBody thick = Planet("Thick", 1f);
			thick.Atmosphere = AtmosphereKind.Thick;
			CelestialMath.ClimateOffsets(system, thick, 0.0, out float warmT, out _);
			LogAssert.IsTrue(warmT > 0f);
		}

		[Test]
		public void TwoSunsAreBrighterThanOne()
		{
			double one = CelestialMath.Insolation(system, home, 0.0);
			StarBody companion = Make<StarBody>("Companion");
			companion.Luminosity = 1f;
			system.Bodies.Add(companion);
			Assert.That(one, Is.EqualTo(1.0).Within(1e-9));
			Assert.That(CelestialMath.Insolation(system, home, 0.0), Is.EqualTo(2.0).Within(1e-9),
				"a second star without a parent sits at the centre with the first");
		}

		[Test]
		public void TheMathHoldsWithNothingLoaded()
		{
			LogAssert.AreEqual(6.0 * (1.0 + 1.0 / 365.0), CelestialMath.HomeSolarDayHours(null));
			CelestialMath.ClimateOffsets(null, null, 0.0, out float t, out float h);
			LogAssert.AreEqual(0f, t);
			LogAssert.AreEqual(0f, h);
			LogAssert.IsTrue(CelestialMath.IsDaylight(null, null, 0.0, 0.0, 0.0));
		}

		[Test]
		public void ASceneWithoutASolarSystemKeepsTheDefaultDay()
		{
			Assume.That(SolarSystemProfile.Active, Is.Null, "a loaded profile would decide the day instead");
			double day = CelestialMath.HomeSolarDayHours(null);
			Assert.That(SceneTime.LocalTime01(null, day * 2.5), Is.EqualTo(0.5).Within(1e-9));
			LogAssert.IsTrue(SceneTime.IsDaylight(null, day * 2.5));
			LogAssert.IsFalse(SceneTime.IsDaylight(null, day * 3.0));
		}

		[Test]
		public void LocalTimeFormatsAsAClock()
		{
			LogAssert.AreEqual("12:00", SceneTime.Format(0.5));
			LogAssert.AreEqual("00:00", SceneTime.Format(0.0));
			LogAssert.AreEqual("06:00", SceneTime.Format(1.25));
			LogAssert.AreEqual("23:59", SceneTime.Format(0.99999));
		}

		// ── World clock ───────────────────────────────────────────────

		private static WorldClock Clock()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			clock.Propose(0, 1000.0, verified: true);
			return clock;
		}

		[Test]
		public void TheFirstReadingAnchorsTheClock()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			LogAssert.IsFalse(clock.HasAnchor);
			LogAssert.AreEqual(0.0, clock.WorldSecondsAt(100));
			LogAssert.IsTrue(clock.Propose(30, 5000.0, verified: false));
			LogAssert.IsTrue(clock.HasAnchor);
			Assert.That(clock.WorldSecondsAt(60), Is.EqualTo(5001.0).Within(1e-9));
			Assert.That(clock.WorldHoursAt(60), Is.EqualTo(5001.0 / 3600.0).Within(1e-12));
		}

		[Test]
		public void ASmallErrorIsLeftAlone()
		{
			WorldClock clock = Clock();
			LogAssert.IsFalse(clock.Propose(3000, 1100.0 + WorldClock.RepublishThresholdSeconds * 0.5, verified: true));
			Assert.That(clock.LastMeasuredError, Is.EqualTo(WorldClock.RepublishThresholdSeconds * 0.5).Within(1e-9));
			LogAssert.AreEqual(0u, clock.Current.Tick, "the anchor did not move");
		}

		[Test]
		public void VerifyingTheHostClockRepublishesWithoutAJump()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			clock.Propose(0, 1000.0, verified: false);
			LogAssert.IsTrue(clock.Propose(300, 1010.1, verified: true), "the database reading upgrades the anchor");
			LogAssert.IsTrue(clock.Current.Verified);
			LogAssert.IsFalse(clock.HasPrevious, "an error inside the threshold needs no slew");
		}

		[Test]
		public void ACorrectionIsEasedInWithoutAJump()
		{
			WorldClock clock = Clock();
			int changes = 0;
			clock.OnAnchorChanged += _ => changes++;
			double before = clock.WorldSecondsAt(3000);
			LogAssert.IsTrue(clock.Propose(3000, 1102.0, verified: true));
			LogAssert.AreEqual(1, changes);
			Assert.That(clock.LastMeasuredError, Is.EqualTo(2.0).Within(1e-9));
			Assert.That(clock.WorldSecondsAt(3000), Is.EqualTo(before).Within(1e-9), "the correction starts where the clock was");

			uint slew = clock.SlewTicks;
			LogAssert.AreEqual(300u, slew, "ten seconds of ticks");
			double previous = clock.WorldSecondsAt(3000);
			for (uint t = 3001; t <= 3000 + slew + 30; t++)
			{
				double now = clock.WorldSecondsAt(t);
				double step = now - previous;
				LogAssert.IsTrue(step > 0.0, $"time never stops or runs backwards (tick {t})");
				LogAssert.IsTrue(step < TickDelta + 0.011, $"no tick jumps ahead (tick {t}, step {step})");
				previous = now;
			}
			Assert.That(clock.WorldSecondsAt(3000 + slew), Is.EqualTo(1102.0 + 10.0).Within(1e-9), "after the slew the clock reads the reference");
		}

		[Test]
		public void ABackwardCorrectionNeverRunsTimeBackwards()
		{
			WorldClock clock = Clock();
			LogAssert.IsTrue(clock.Propose(3000, 1099.0, verified: true));
			double previous = clock.WorldSecondsAt(3000);
			for (uint t = 3001; t <= 3000 + clock.SlewTicks; t++)
			{
				double now = clock.WorldSecondsAt(t);
				LogAssert.IsTrue(now > previous, $"a one-second correction over ten seconds only slows the clock (tick {t})");
				previous = now;
			}
		}

		[Test]
		public void ACorrectionDuringASlewStartsFromWhatIsShown()
		{
			WorldClock clock = Clock();
			clock.Propose(3000, 1102.0, verified: true);
			double shown = clock.WorldSecondsAt(3150);
			clock.Propose(3150, 1110.0, verified: true);
			Assert.That(clock.WorldSecondsAt(3150), Is.EqualTo(shown).Within(1e-9));
		}

		[Test]
		public void TheClientReadsExactlyWhatTheServerPublished()
		{
			WorldClock server = Clock();
			server.Propose(3000, 1102.0, verified: true);
			var client = new WorldClock { TickDelta = TickDelta };
			client.Adopt(server.Current, server.Previous, server.HasPrevious, server.SlewTicks);
			for (double t = 2990; t < 3400; t += 7.25)
			{
				LogAssert.AreEqual(server.WorldSecondsAt(t), client.WorldSecondsAt(t));
			}
			client.Reset();
			LogAssert.IsFalse(client.HasAnchor);
		}

		[Test]
		public void FractionalTicksStayPreciseAfterMonthsOfUptime()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			const uint tick = 90_000_000;   // five weeks at 30 Hz
			clock.Propose(tick, 1_000_000_000.0, verified: true);
			double at = clock.WorldSecondsAt(tick);
			Assert.That(clock.WorldSecondsAt(tick + 0.5) - at, Is.EqualTo(0.5 * TickDelta).Within(1e-6));
			Assert.That(clock.WorldSecondsAt(tick + 30.0 * 3600.0) - at, Is.EqualTo(3600.0).Within(1e-6));
		}

		[Test]
		public void UnixTimeConvertsToWorldSeconds()
		{
			long epoch = CalendarProfile.DefaultEpochUnixSeconds;
			LogAssert.AreEqual(0.0, WorldClock.WorldSecondsFromUnixMilliseconds(epoch * 1000L, epoch));
			LogAssert.AreEqual(90.5, WorldClock.WorldSecondsFromUnixMilliseconds(epoch * 1000L + 90_500L, epoch));
			DateTime utc = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime.AddHours(2);
			Assert.That(WorldClock.WorldSecondsFromUtc(utc, epoch), Is.EqualTo(7200.0).Within(1e-6));
		}
	}
}
