using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins that a state update is told the real interval it covers, not one brain tick.
	/// </summary>
	/// <remarks>
	/// Before this, a state with a 1 s update rate on an 8 Hz brain advanced its timers by
	/// 0.125 s per second: attack cooldowns, stuck detection and retreat patience all ran 8x slow.
	/// </remarks>
	[TestFixture]
	public class AIStateClockTests
	{
		private const float Tick = 0.125f;

		[Test]
		public void UpdateIsDueAfterTheRate_AndReportsTheWholeInterval()
		{
			AIStateClock clock = default;
			clock.Rearm(1f);

			int updates = 0;
			float reported = 0f;
			for (int i = 0; i < 16; i++)
			{
				if (clock.Advance(Tick, out float elapsed))
				{
					updates++;
					reported += elapsed;
					clock.Rearm(1f, Tick);
				}
			}

			Assert.That(updates, Is.EqualTo(1));
			// Rearm(1) needs the wait to go NEGATIVE: eight ticks bring it to 0, the ninth below,
			// and the tenth reports due — so the update covers all ten ticks since the state was entered.
			Assert.That(reported, Is.EqualTo(10 * Tick).Within(1e-5f), "the first update covers every tick since the state was entered");
		}

		[Test]
		public void SteadyState_IntervalsSumToWallClock()
		{
			AIStateClock clock = default;
			clock.Rearm(0.5f);

			float reported = 0f;
			int updates = 0;
			const int ticks = 800; // 100 s
			for (int i = 0; i < ticks; i++)
			{
				if (clock.Advance(Tick, out float elapsed))
				{
					reported += elapsed;
					updates++;
					clock.Rearm(0.5f, Tick);
				}
			}

			float wall = ticks * Tick;
			Assert.That(reported, Is.EqualTo(wall).Within(1f), "time handed to the state must be wall-clock time, not tick count");
			// Rearm(0.5, tick) then four more ticks before the fifth reports due: 0.625 s per update.
			Assert.That(updates, Is.EqualTo(160).Within(2));
		}

		[Test]
		public void FreshClock_FiresOnTheSecondTick_LikeTheOldLoop()
		{
			// A default clock has NextUpdate 0: not yet due on the first tick, due on the next.
			AIStateClock clock = default;
			Assert.That(clock.Advance(Tick, out _), Is.False);
			Assert.That(clock.Advance(Tick, out float elapsed), Is.True);
			Assert.That(elapsed, Is.EqualTo(2 * Tick).Within(1e-6f));
		}
	}
}
