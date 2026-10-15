using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The air the clouds and the weather field ride (<see cref="AirDrift"/>, 2026-10-15: "Clouds don't seem to be moving
	/// properly with the wind speed" / "It all needs to be deterministic"): its slope is the wind, it is the same however
	/// it is asked, it carries on when the wind is changed, and a calm stills it rather than running it backwards.
	/// </summary>
	public class AirDriftTests
	{
		private const uint Seed = 238u;
		/// <summary>Three weeks into the world, mid-knot.</summary>
		private const double Now = 21.0 * 86400.0 + 137.25;

		[SetUp]
		public void SetUp() => AirDrift.ResetCache();

		private static AirDrift.Air Air(bool driver = true, float authored = 0f, AirOffsetEntry runtime = default) => new AirDrift.Air
		{
			Seed = Seed,
			Latitude = 45f,
			Belts = WindBelts.Earthlike,
			Driver = driver,
			AuthoredWind = authored,
			Runtime = runtime,
		};

		/// <summary>Along the prevailing wind, m, with the field's wander taken out (the still air has none).</summary>
		private static double Along(in AirDrift.Air air, double seconds)
		{
			AirDrift.At(air, seconds, out double x, out double y);
			if (air.Driver)
			{
				WeatherDriver.Wander(air.Seed, seconds, out double wx, out double wy);
				x -= wx;
				y -= wy;
			}
			Vector2 direction = WeatherDriver.PrevailingWind(air.Latitude, air.Belts);
			return x * direction.x + y * direction.y;
		}

		private static AirOffsetEntry Wind(float from, float to, double start, double end, double carried = 0.0) => new AirOffsetEntry
		{
			From = new AirOffsets { Wind = from },
			To = new AirOffsets { Wind = to },
			StartSeconds = start,
			EndSeconds = end,
			Carried = carried,
		};

		[Test]
		public void TheDriftsSlopeIsTheWindAtTheSteeringLevel()
		{
			AirDrift.Air air = Air();
			for (int i = 0; i < 6; i++)
			{
				double t = Now + i * 3917.0;
				double slope = (Along(air, t + 5.0) - Along(air, t - 5.0)) / 10.0;
				float speed = AirDrift.SpeedAt(air, t);
				Assert.That(slope, Is.EqualTo(speed).Within(0.02 * speed + 0.05), $"at {t:0} s the air ran {slope:0.00} m/s against a wind of {speed:0.00}");
			}
		}

		[Test]
		public void TheWindVariesAndTheDriftFollowsIt()
		{
			// Over a few days the reported wind swings, and so does the drift's pace: it was fixed per latitude for ever.
			AirDrift.Air air = Air();
			float slowest = float.MaxValue, fastest = 0f;
			for (int i = 0; i < 200; i++)
			{
				float speed = AirDrift.SpeedAt(air, Now + i * 1800.0);
				slowest = Mathf.Min(slowest, speed);
				fastest = Mathf.Max(fastest, speed);
			}
			Assert.That(fastest, Is.GreaterThan(slowest * 1.5f), $"the steering wind only ranged {slowest:0.0}–{fastest:0.0} m/s");
		}

		[Test]
		public void EveryMachineGetsTheSameMetresWhateverItAskedBefore()
		{
			AirOffsetEntry runtime = Wind(0f, 7f, Now - 9000.0, Now - 8000.0, 1234.5);
			AirDrift.Air air = Air(runtime: runtime);
			AirDrift.At(air, Now, out double freshX, out double freshY);
			double freshTurns = AirDrift.SailTurns(air, 16f, Now);

			// Another machine that joined long ago and has been asking every frame, out of order.
			AirDrift.ResetCache();
			for (double t = Now - 20000.0; t < Now + 5000.0; t += 977.0)
			{
				AirDrift.At(air, t, out _, out _);
				AirDrift.SailTurns(air, 16f, t);
			}
			AirDrift.At(air, Now + 60000.0, out _, out _);
			AirDrift.At(air, Now, out double x, out double y);
			Assert.That(x, Is.EqualTo(freshX), "x differs with what was asked before");
			Assert.That(y, Is.EqualTo(freshY), "y differs with what was asked before");
			Assert.That(AirDrift.SailTurns(air, 16f, Now), Is.EqualTo(freshTurns), "the sails differ with what was asked before");
		}

		[Test]
		public void ChangingTheWindCarriesOnFromWhereTheAirWas()
		{
			AirOffsetEntry first = Wind(0f, 6f, Now - 5000.0, Now - 4900.0);
			AirDrift.Air before = Air(runtime: first);
			double change = Now;
			AirOffsetEntry next = Wind(6f, -3f, change, change + 120.0);
			AirDrift.CarryOver(before, first, ref next);
			AirDrift.Air after = Air(runtime: next);
			AirDrift.At(before, change, out double ax, out double ay);
			AirDrift.At(after, change, out double bx, out double by);
			Assert.That(bx, Is.EqualTo(ax).Within(1e-6), "the clouds jumped in x when the wind was changed");
			Assert.That(by, Is.EqualTo(ay).Within(1e-6), "the clouds jumped in y when the wind was changed");

			// The fog banks (where they are, and how far they have churned) and the sails go on too.
			SurfaceAirDrift.At(before, AirDrift.SurfaceShare, change, out double fx, out double fy, out double fs);
			SurfaceAirDrift.At(after, AirDrift.SurfaceShare, change, out double gx, out double gy, out double gs);
			Assert.That(gx, Is.EqualTo(fx).Within(1e-6), "the fog jumped when the wind was changed");
			Assert.That(gy, Is.EqualTo(fy).Within(1e-6), "the fog jumped when the wind was changed");
			Assert.That(gs, Is.EqualTo(fs).Within(1e-6), "the fog's banks re-formed when the wind was changed");
			Assert.That(AirDrift.SailTurns(after, AirDrift.SailMostRpm, change), Is.EqualTo(AirDrift.SailTurns(before, AirDrift.SailMostRpm, change)).Within(1e-9),
				"the sails jumped when the wind was changed");
		}

		[Test]
		public void AddedWindRunsTheAirAndACalmStillsIt()
		{
			double t = Now;
			float plain = AirDrift.SpeedAt(Air(), t);
			float gale = AirDrift.SpeedAt(Air(authored: 10f), t);
			Assert.That(gale - plain, Is.EqualTo(10f * AirDrift.SteeringGain).Within(0.01f), "ten m/s added at the ground is the steering level's share of it aloft");

			// A calm far below the wind: the air stands, it does not run backwards.
			AirDrift.Air calm = Air(runtime: Wind(-40f, -40f, t - 3600.0, t - 3600.0));
			double slope = (Along(calm, t + 30.0) - Along(calm, t - 30.0)) / 60.0;
			Assert.That(slope, Is.EqualTo(0.0).Within(1e-6), $"a calm carried the air at {slope:0.000} m/s");
			Assert.That(AirDrift.SpeedAt(calm, t), Is.EqualTo(0f));
		}

		[Test]
		public void TheSurfaceAirRidesTheSameWindAsTheClouds()
		{
			// Ten m/s added by the scene's author runs the fog and the motes too, at their share: they ignored it before.
			double t = Now;
			AirDrift.Air plain = Air(), windy = Air(authored: 10f);
			double Slope(in AirDrift.Air air)
			{
				SurfaceAirDrift.At(air, AirDrift.SurfaceShare, t - 30.0, out double ax, out double ay, out _);
				SurfaceAirDrift.At(air, AirDrift.SurfaceShare, t + 30.0, out double bx, out double by, out _);
				return System.Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / 60.0;
			}
			Assert.That(Slope(windy) - Slope(plain), Is.EqualTo(10.0 * AirDrift.SurfaceShare).Within(0.01));
			// And the surface air is the clouds' own, at its share of the ground wind against their steering gain.
			Assert.That(Slope(plain), Is.EqualTo(AirDrift.SpeedAt(plain, t) / AirDrift.SteeringGain * AirDrift.SurfaceShare).Within(0.02));
		}

		[Test]
		public void TheStillAirIsCarriedByItsSteadyWind()
		{
			AirDrift.Air still = Air(driver: false, authored: 2f);
			double slope = (Along(still, Now + 50.0) - Along(still, Now - 50.0)) / 100.0;
			Assert.That(slope, Is.EqualTo((AirDrift.StillWind + 2f) * AirDrift.SteeringGain).Within(1e-4));
		}

		[Test]
		public void SailsTurnWithTheWindAndStandInACalmOrAGale()
		{
			Assert.That(AirDrift.SailRpm(1.5f, 16f), Is.EqualTo(0f), "below the cut-in");
			Assert.That(AirDrift.SailRpm(25f, 16f), Is.EqualTo(0f), "braked in a gale");
			Assert.That(AirDrift.SailRpm(6f, 16f), Is.GreaterThan(AirDrift.SailRpm(4f, 16f)), "faster in more wind");
			Assert.That(AirDrift.SailRpm(15f, 16f), Is.EqualTo(16f), "no faster than the miller lets them");

			// A steady 6 m/s: the sails' turns grow by exactly their pace.
			AirDrift.Air steady = Air(driver: false);
			double perMinute = AirDrift.SailTurns(steady, 16f, Now + 60.0) - AirDrift.SailTurns(steady, 16f, Now);
			Assert.That(perMinute, Is.EqualTo(AirDrift.SailRpm(AirDrift.StillWind, 16f)).Within(1e-6));

			// Still air below the cut-in: they stand.
			AirDrift.Air light = Air(driver: false, authored: -4f);
			Assert.That(AirDrift.SailTurns(light, 16f, Now + 600.0) - AirDrift.SailTurns(light, 16f, Now), Is.EqualTo(0.0));

			// And in the field's own wind they never turn backwards.
			AirDrift.Air air = Air();
			double last = AirDrift.SailTurns(air, 16f, Now);
			for (int i = 1; i < 50; i++)
			{
				double turns = AirDrift.SailTurns(air, 16f, Now + i * 431.0);
				Assert.That(turns, Is.GreaterThanOrEqualTo(last));
				last = turns;
			}
		}
	}
}
