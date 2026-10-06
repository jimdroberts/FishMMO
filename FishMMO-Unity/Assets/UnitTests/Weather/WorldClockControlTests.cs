using NUnit.Framework;
using FishMMO.Shared.Celestial;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Setting, holding and racing the world clock (an admin's /admin time, the Control Panel, the World
	/// Sim bed): the tick keeps its pace, the anchor says what time it is, and nothing drifts out of line.
	/// </summary>
	[TestFixture]
	public class WorldClockControlTests
	{
		private const double TickDelta = 1.0 / 30.0;

		[TearDown]
		public void TearDown()
		{
			LocalWorldClock.Activate(null);
			WorldClock.Shared.Reset();
		}

		[Test]
		public void TheRate_ScalesWorldTimePerTick_AndZeroHoldsIt()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			clock.Override(1000u, 5000.0, 1.0, true);
			Assert.That(clock.WorldSecondsAt(1030.0), Is.EqualTo(5001.0).Within(1e-9), "a second of ticks is a second of world");
			clock.Override(1030u, 5001.0, 60.0, true);
			Assert.That(clock.WorldSecondsAt(1060.0), Is.EqualTo(5061.0).Within(1e-9), "raced: a minute a second");
			clock.Override(1060u, 5061.0, 0.0, true);
			Assert.That(clock.WorldSecondsAt(99999.0), Is.EqualTo(5061.0).Within(1e-9), "held: every tick reads the same moment");
			Assert.That(clock.Rate, Is.EqualTo(0.0));
		}

		[Test]
		public void AnOverride_IsExactAtOnce_NotEased()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			clock.Propose(100u, 1000.0, true);
			clock.Override(130u, 86400.250, 1.0, true);
			Assert.That(clock.WorldSecondsAt(130.0), Is.EqualTo(86400.250).Within(1e-9), "to the millisecond, on the tick it was set");
			Assert.That(clock.HasPrevious, Is.False, "and no blend from the old time");
		}

		[Test]
		public void ADriftCorrection_KeepsThePaceAnAdminSet()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			clock.Override(0u, 0.0, 0.0, true);
			clock.Propose(300u, 10.0, true);
			Assert.That(clock.Rate, Is.EqualTo(0.0), "a held world stays held through a correction");
		}

		[Test]
		public void TickAt_IsTheInverse_AtAnyPace()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			foreach (double rate in new[] { 1.0, 0.25, 60.0 })
			{
				clock.Override(5000u, 12345.678, rate, true);
				double tick = clock.TickAt(12400.0);
				Assert.That(clock.WorldSecondsAt(tick), Is.EqualTo(12400.0).Within(1e-6), $"at {rate}×");
			}
			clock.Override(5000u, 12345.678, 0.0, true);
			Assert.That(clock.TickAt(99999.0), Is.EqualTo(5000.0), "held: the anchor's tick");
		}

		[Test]
		public void TheLocalClock_TicksAtAFixedRate_WhateverTheWorldsPace()
		{
			var local = new LocalWorldClock(30.0, 1000.0);
			LocalWorldClock.Activate(local);
			double startTick = local.Tick;
			local.Advance(2.0);
			Assert.That(local.Tick - startTick, Is.EqualTo(60.0).Within(1e-9));
			Assert.That(local.WorldSeconds, Is.EqualTo(1002.0).Within(1e-6));

			local.Hold();
			local.Advance(5.0);
			Assert.That(local.Tick - startTick, Is.EqualTo(210.0).Within(1e-9), "the tick goes on counting while held");
			Assert.That(local.WorldSeconds, Is.EqualTo(1002.0).Within(1e-6), "the world does not");

			local.SetWorldSeconds(5000.125);
			Assert.That(local.WorldSeconds, Is.EqualTo(5000.125).Within(1e-6), "set to the millisecond, held");
			local.Resume();
			local.Advance(0.5);
			Assert.That(local.WorldSeconds, Is.EqualTo(5000.625).Within(1e-6), "and runs on from it at its old pace");

			local.SetRate(60.0);
			local.Advance(1.0);
			Assert.That(local.WorldSeconds, Is.EqualTo(5060.625).Within(1e-6), "raced");
			local.Step(-60.0);
			Assert.That(local.WorldSeconds, Is.EqualTo(5000.625).Within(1e-6), "stepped back");
		}

		[Test]
		public void TheLocalClock_IsWhatOfflineTimeReads()
		{
			var local = new LocalWorldClock(30.0, 7200.0);
			LocalWorldClock.Activate(local);
			Assert.That(WorldTime.CurrentHours(null), Is.EqualTo(2.0).Within(1e-9));
			local.SetWorldSeconds(10800.0);
			Assert.That(WorldTime.CurrentHours(null), Is.EqualTo(3.0).Within(1e-9));
		}

		[Test]
		public void WorldTimeText_RoundTripsToTheMillisecond()
		{
			const long Epoch = CalendarProfile.DefaultEpochUnixSeconds;
			string text = WorldTimeText.Write(24_000_000.250, Epoch);
			Assert.That(WorldTimeText.TryRead(text, Epoch, 0.0, out double back, out _), Is.True);
			Assert.That(back, Is.EqualTo(24_000_000.250).Within(5e-4));
			Assert.That(WorldTimeText.TryRead("2026-01-01 00:00:01.500", Epoch, 0.0, out double early, out _), Is.True);
			Assert.That(early, Is.EqualTo(1.5).Within(1e-6), "the epoch is 2026-01-01");
		}

		[Test]
		public void WorldTimeText_ReadsChanges_AndRefusesNonsense()
		{
			Assert.That(WorldTimeText.TryRead("+1h30m", 0, 100.0, out double later, out _), Is.True);
			Assert.That(later, Is.EqualTo(100.0 + 5400.0).Within(1e-9));
			Assert.That(WorldTimeText.TryRead("-90s", 0, 100.0, out double earlier, out _), Is.True);
			Assert.That(earlier, Is.EqualTo(10.0).Within(1e-9));
			Assert.That(WorldTimeText.TryRead("+2d3h4m5.25s", 0, 0.0, out double long_, out _), Is.True);
			Assert.That(long_, Is.EqualTo(2 * 86400 + 3 * 3600 + 4 * 60 + 5.25).Within(1e-9));
			Assert.That(WorldTimeText.TryRead("+250ms", 0, 0.0, out double tiny, out _), Is.True);
			Assert.That(tiny, Is.EqualTo(0.25).Within(1e-9));
			Assert.That(WorldTimeText.TryRead("tomorrow", 0, 0.0, out _, out string error), Is.False);
			Assert.That(error, Is.Not.Empty);
			Assert.That(WorldTimeText.TryRead("+1x", 0, 0.0, out _, out _), Is.False);
		}
	}
}
