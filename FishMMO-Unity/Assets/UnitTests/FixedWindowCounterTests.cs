using System;
using NUnit.Framework;
using FishMMO.Auth.Core.Collections;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="FixedWindowCounter{TKey}"/>: the per-username login and two-factor lockouts, the
	/// per-IP handshake burst window and the account-verification lockout all count through it.
	/// </summary>
	/// <remarks>
	/// Every call takes its (monotonic, seconds) clock from the test, so each assertion is about a
	/// moment the test chose. The sweep tests pin the defect the class replaced: trackers swept by enumerating a
	/// few entries from a dictionary's head, which never reached anything behind them.
	/// </remarks>
	[TestFixture]
	public class FixedWindowCounterTests
	{
		private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
		private const double WindowSeconds = 15 * 60;
		/// <summary>A monotonic reading; only differences matter.</summary>
		private const double T0 = 5000.0;
		private const double Minute = 60.0;

		private FixedWindowCounter<string> counter;

		[SetUp]
		public void SetUp()
		{
			counter = new FixedWindowCounter<string>(Window, StringComparer.OrdinalIgnoreCase);
		}

		[Test]
		public void Increment_CountsWithinTheWindowOpenedByTheFirstEvent()
		{
			LogAssert.AreEqual(1, counter.Increment("alice", T0), "the first event opens a window at one");
			LogAssert.AreEqual(2, counter.Increment("ALICE", T0 + 14 * Minute), "a later event inside the window adds to it (keys compare with the given comparer)");
			LogAssert.AreEqual(2, counter.GetCount("alice", T0 + 14 * Minute), "and reads back");
		}

		[Test]
		public void AWindow_ClosesExactlyOneWindowAfterItOpened_AndTheNextEventOpensAFreshOne()
		{
			counter.Increment("alice", T0);
			counter.Increment("alice", T0 + Minute);

			LogAssert.AreEqual(2, counter.GetCount("alice", T0 + WindowSeconds - 0.001), "still open a millisecond before the window ends");
			LogAssert.AreEqual(0, counter.GetCount("alice", T0 + WindowSeconds), "closed at opened + window, whether or not a sweep has run");
			LogAssert.AreEqual(1, counter.Increment("alice", T0 + WindowSeconds), "the next event starts a new window instead of adding to the stale count");
		}

		[Test]
		public void TryIncrement_StopsAtTheLimit_WithoutExtendingTheWindow()
		{
			for (int i = 0; i < 8; i++)
			{
				LogAssert.IsTrue(counter.TryIncrement("203.0.113.9", T0 + i, 8), $"event {i + 1} of 8 is inside the burst");
			}
			LogAssert.IsFalse(counter.TryIncrement("203.0.113.9", T0 + 10 * Minute, 8), "the ninth is refused");
			LogAssert.AreEqual(8, counter.GetCount("203.0.113.9", T0 + 10 * Minute), "and is not counted");
			LogAssert.IsTrue(counter.TryIncrement("203.0.113.9", T0 + WindowSeconds, 8), "the window still ends where it began: refusals never extend it");
		}

		[Test]
		public void Sweep_ReachesEveryClosedWindow_OldestFirst_AndStopsAtTheFirstOpenOne()
		{
			for (int i = 0; i < 200; i++)
			{
				counter.Increment($"old{i}", T0);
			}
			for (int i = 0; i < 100; i++)
			{
				counter.Increment($"young{i}", T0 + 10 * Minute);
			}

			double now = T0 + WindowSeconds;
			int removed = 0;
			// Small passes, as a per-tick sweep makes them. The dictionary-head sweep this replaced
			// looked at the same first entries every pass and never got past them.
			for (int pass = 0; pass < 10; pass++)
			{
				removed += counter.SweepExpired(now, 64);
			}

			LogAssert.AreEqual(200, removed, "every closed window was reached");
			LogAssert.AreEqual(100, counter.Count, "and nothing still open was touched");
			LogAssert.AreEqual(0, counter.SweepExpired(now, 64), "a pass with nothing closed removes nothing");
		}

		[Test]
		public void AtCapacity_ANewKeyIsRefused_ButAKeyAlreadyCountedStillCounts()
		{
			counter.Increment("a", T0, maxKeys: 2);
			counter.Increment("b", T0, maxKeys: 2);

			LogAssert.AreEqual(0, counter.Increment("c", T0, maxKeys: 2), "a third key is refused at capacity");
			LogAssert.AreEqual(2, counter.Increment("a", T0, maxKeys: 2), "a lockout already under way keeps closing at capacity");
		}

		[Test]
		public void AtCapacity_ClosedWindowsAreReclaimedBeforeAnyoneIsRefused()
		{
			counter.Increment("a", T0, maxKeys: 2);
			counter.Increment("b", T0, maxKeys: 2);

			LogAssert.AreEqual(1, counter.Increment("c", T0 + WindowSeconds, maxKeys: 2), "closed windows are not live keys, so they do not hold capacity");
		}

		[Test]
		public void Remove_DropsTheKey()
		{
			counter.Increment("alice", T0);
			LogAssert.IsTrue(counter.Remove("alice"), "the key was held");
			LogAssert.AreEqual(0, counter.GetCount("alice", T0), "and is gone");
			LogAssert.AreEqual(0, counter.Count, "from the order list too");
		}
	}
}
