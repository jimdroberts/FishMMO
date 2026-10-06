using NUnit.Framework;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The one clock the world's motion runs on (WorldMotion), and the pure functions the motion is
	/// worked out from on top of it (SurfaceAirDrift), so every player sees the same wave, gust and fog
	/// bank at the same moment (audit 2026-10-06).
	/// </summary>
	[TestFixture]
	public class WorldMotionClockTests
	{
		private const double Frame = 1.0 / 144.0;

		[Test]
		public void KeepingTime_ItRunsWithTheWorldClock()
		{
			double motion = 1000.0, world = 1000.0;
			for (int i = 0; i < 1000; i++)
			{
				double next = world + Frame;
				motion = WorldMotion.Step(motion, world, next, Frame, 1f);
				world = next;
			}
			Assert.That(motion, Is.EqualTo(world).Within(1e-9));
		}

		[Test]
		public void AReestimatedTick_IsLeantAcross_NeverJumped_NorRunBackwards()
		{
			// FishNet sets the client's tick outright about once a second: four ticks either way.
			foreach (double step in new[] { 4.0 / 30.0, -4.0 / 30.0 })
			{
				double motion = 1000.0, world = 1000.0, last = motion;
				world += step;
				double lastWorld = world;
				for (int i = 0; i < 1440; i++)
				{
					double next = lastWorld + Frame;
					motion = WorldMotion.Step(motion, lastWorld, next, Frame, 1f);
					lastWorld = next;
					Assert.That(motion - last, Is.GreaterThanOrEqualTo(Frame * 0.74), "never slower than three quarters of real time");
					Assert.That(motion - last, Is.LessThanOrEqualTo(Frame * 1.26), "never faster than a quarter over");
					last = motion;
				}
				Assert.That(motion, Is.EqualTo(lastWorld).Within(1e-3), "and is back on the world clock within seconds");
			}
		}

		[Test]
		public void AWideGap_IsJumped()
		{
			// A join, a scene hop, the first anchor after the machine's own clock. In its first frame a
			// jump looks like a preview racing, so it is held to real time; the next frame jumps it.
			double motion = WorldMotion.Step(1000.0, 1000.0, 1003.0, Frame, 1f);
			Assert.That(motion, Is.EqualTo(1000.0 + Frame).Within(1e-9));
			motion = WorldMotion.Step(motion, 1003.0, 1003.0 + Frame, Frame, 1f);
			Assert.That(motion, Is.EqualTo(1003.0 + Frame).Within(1e-9));
		}

		[Test]
		public void AStall_KeepsItOnTheClock()
		{
			// Two seconds of frame: the world moved two seconds too, so nothing is lost or jumped.
			double motion = WorldMotion.Step(1000.0, 1000.0, 1002.0, 2.0, 1f);
			Assert.That(motion, Is.EqualTo(1002.0).Within(0.01));
		}

		[Test]
		public void APreview_StopsAndSlowsIt_AndRacingLeavesItAtRealTime()
		{
			Assert.That(WorldMotion.Step(500.0, 0.0, 0.0, 0.1, 0f), Is.EqualTo(500.0), "a stopped clock stops it");
			Assert.That(WorldMotion.Step(500.0, 0.0, 0.1, 0.1, 0.25f), Is.EqualTo(500.025).Within(1e-9), "a slowed clock slows it");
			// The bed at 180 times: 18 world seconds in a tenth of a real one.
			Assert.That(WorldMotion.Step(500.0, 0.0, 18.0, 0.1, 1f), Is.EqualTo(500.1).Within(1e-9), "a racing clock leaves it at real time");
			// Once the preview stops racing, the motion rejoins the clock.
			Assert.That(WorldMotion.Step(500.1, 18.0, 18.1, 0.1, 1f), Is.EqualTo(18.1).Within(1e-9));
		}

		[Test]
		public void Follow_HoldsWhenTheTargetStops_AndKeepsUpWithARace()
		{
			Assert.That(WorldMotion.Follow(100.0, 100.0, 100.0, 0.1), Is.EqualTo(100.0), "a stopped target holds");
			Assert.That(WorldMotion.Follow(100.0, 100.05, 100.0, 0.1), Is.EqualTo(100.0), "a target stepped back holds, never reverses");
			Assert.That(WorldMotion.Follow(100.0, 100.0, 118.0, 0.1), Is.EqualTo(118.0), "a racing target is followed exactly");
			Assert.That(WorldMotion.Follow(double.NaN, 0.0, 42.0, 0.1), Is.EqualTo(42.0), "the first sight is the target");
		}

		[Test]
		public void WholeCycles_MeetThemselvesAtTheWindowsEnd()
		{
			foreach (float rate in new[] { 0.013f, 0.37f, 1.1f, 2.6f / 6.2831853f })
			{
				float snapped = WorldMotion.WholeCycles(rate, 60.0);
				double cycles = snapped * 60.0;
				Assert.That(cycles, Is.EqualTo(System.Math.Round(cycles)).Within(1e-4), $"{rate} snapped to whole cycles");
				Assert.That(snapped, Is.EqualTo(rate).Within(0.5f / 60f + 1e-6f), "within half a cycle per window of the rate");
			}
			Assert.That(WorldMotion.WholeCycles(-1f, 60.0), Is.EqualTo(0f), "never backwards");
		}

		[Test]
		public void Repeat_WrapsExactly_EvenBelowZero()
		{
			Assert.That(WorldMotion.Repeat(25_005_123.25, 10000.0), Is.EqualTo(5123.25).Within(1e-6));
			Assert.That(WorldMotion.Repeat(-1.0, 10000.0), Is.EqualTo(9999.0).Within(1e-9));
			Assert.That(WorldMotion.Repeat(5.0, 0.0), Is.EqualTo(0.0));
		}

		[Test]
		public void SurfaceDrift_IsContinuous_AndTheSameWhicheverWayItIsReached()
		{
			const uint Seed = 238u;
			const float Latitude = 45f;
			WindBelts belts = WindBelts.Home;
			double far = 24_000_000.0;
			// Reached cold, from the epoch in one go.
			SurfaceAirDrift.ResetCache();
			SurfaceAirDrift.At(Seed, Latitude, belts, 0.75f, far, out double coldX, out double coldY, out double coldStir);
			// Reached in stages on a fresh cache: the running sum extended a piece at a time must land on
			// the same number — and asking for another share in between must not disturb it.
			SurfaceAirDrift.ResetCache();
			SurfaceAirDrift.At(Seed, Latitude, belts, 0.75f, far * 0.37, out _, out _, out _);
			SurfaceAirDrift.At(Seed, Latitude, belts, 0.008f, far * 0.5, out _, out _, out _);
			SurfaceAirDrift.At(Seed, Latitude, belts, 0.75f, far * 0.81, out _, out _, out _);
			SurfaceAirDrift.At(Seed, Latitude, belts, 0.75f, far, out double warmX, out double warmY, out double warmStir);
			Assert.That(warmX, Is.EqualTo(coldX).Within(1e-6));
			Assert.That(warmY, Is.EqualTo(coldY).Within(1e-6));
			Assert.That(warmStir, Is.EqualTo(coldStir).Within(1e-6));

			// No step at a knot or at a checkpoint: a millisecond either side moves it by a wind's millisecond.
			double checkpoint = SurfaceAirDrift.KnotSeconds * 64 * 1300;
			foreach (double at in new[] { far + 17.3, SurfaceAirDrift.KnotSeconds * 80_001, checkpoint })
			{
				SurfaceAirDrift.At(Seed, Latitude, belts, 0.75f, at - 0.001, out double ax, out double ay, out double astir);
				SurfaceAirDrift.At(Seed, Latitude, belts, 0.75f, at + 0.001, out double bx, out double by, out double bstir);
				double moved = System.Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
				Assert.That(moved, Is.LessThan(0.002 * 30.0), $"no jump at {at}");
				Assert.That(bstir - astir, Is.InRange(0.002 - 1e-6, 0.004 + 1e-6), "the stirring clock runs at one to two times the clock");
			}
		}

		[Test]
		public void SurfaceDrift_MovesAtTheReportedWind()
		{
			const uint Seed = 238u;
			const float Latitude = 30f;
			WindBelts belts = WindBelts.Home;
			// At a knot the wind is exactly the reported wind there, times the share.
			double knot = SurfaceAirDrift.KnotSeconds * 81_234;
			SurfaceAirDrift.At(Seed, Latitude, belts, 1f, knot, out double ax, out double ay, out _);
			SurfaceAirDrift.At(Seed, Latitude, belts, 1f, knot + 1.0, out double bx, out double by, out _);
			double speed = System.Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
			float reported = WeatherDriver.PrevailingSpeed(Seed, Latitude, knot, belts);
			Assert.That(speed, Is.EqualTo(reported).Within(reported * 0.02 + 0.01));
		}
	}
}
