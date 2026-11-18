using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the arithmetic of a scene instance's lifetime cap, closing warnings and idle timeout.
	/// </summary>
	/// <remarks>
	/// Every time is seconds on the scene server's monotonic clock, and the only input from
	/// outside the process is the row's age as the database measured it. The tests use small,
	/// arbitrary clock readings on purpose: nothing here may depend on what the clock's origin is.
	/// </remarks>
	[TestFixture]
	public class SceneInstanceLifetimeTests
	{
		private static readonly int[] Marks = { 600, 300, 60 };

		// ── Where the row's creation falls ────────────────────────────────────

		[Test]
		public void CreatedAt_IsTheObservationMinusTheDatabaseAge()
		{
			LogAssert.AreEqual(1000.0 - 90.0, SceneInstanceLifetime.CreatedAt(1000.0, 90.0),
				"The row was created its database-measured age before the moment that age was read.");
		}

		[Test]
		public void CreatedAt_TreatsANegativeOrMissingAgeAsNew()
		{
			LogAssert.AreEqual(1000.0, SceneInstanceLifetime.CreatedAt(1000.0, -30.0),
				"A row stamped ahead of the database clock is not older than new.");
			LogAssert.AreEqual(1000.0, SceneInstanceLifetime.CreatedAt(1000.0, double.NaN),
				"No age reads as new, never as a lifetime already spent.");
		}

		// ── Lifetime cap ──────────────────────────────────────────────────────

		[Test]
		public void Expiry_CountsTheQueueAndTheLoad()
		{
			// Queued and loading for 5 minutes before the scene server took it, cap 60 minutes.
			double createdAt = SceneInstanceLifetime.CreatedAt(10_000.0, 300.0);
			double cap = 60 * 60.0;

			LogAssert.IsFalse(SceneInstanceLifetime.IsExpired(10_000.0 + cap - 300.0 - 1.0, createdAt, cap),
				"One second before the cap, measured from creation, the instance is still open.");
			LogAssert.IsTrue(SceneInstanceLifetime.IsExpired(10_000.0 + cap - 300.0, createdAt, cap),
				"The cap is reached 55 minutes after load when the row was 5 minutes old at dequeue.");
		}

		[Test]
		public void RemainingSeconds_IsTheCapLessTheAge()
		{
			LogAssert.AreEqual(3000.0, SceneInstanceLifetime.RemainingSeconds(700.0, 100.0, 3600.0));
			LogAssert.IsTrue(SceneInstanceLifetime.RemainingSeconds(4000.0, 100.0, 3600.0) < 0.0,
				"Past the cap, remaining time goes negative rather than wrapping or clamping to a large value.");
		}

		// ── Idle timeout ──────────────────────────────────────────────────────

		[Test]
		public void IdleExpiry_IsInclusiveOfTheTimeout()
		{
			LogAssert.IsFalse(SceneInstanceLifetime.IsIdleExpired(50.0 + 299.0, 50.0, 300.0));
			LogAssert.IsTrue(SceneInstanceLifetime.IsIdleExpired(50.0 + 300.0, 50.0, 300.0));
		}

		[Test]
		public void IdleExpiry_WithAZeroTimeout_IsImmediate()
		{
			LogAssert.IsTrue(SceneInstanceLifetime.IsIdleExpired(50.0, 50.0, 0.0),
				"An instance everybody chose to leave is reclaimed on the next pulse.");
		}

		// ── Closing warnings ──────────────────────────────────────────────────

		[Test]
		public void Warning_NothingBeforeTheFirstMark()
		{
			var announced = new HashSet<int>();
			LogAssert.AreEqual(0, SceneInstanceLifetime.ResolveExpiryWarning(601.0, Marks, announced));
			LogAssert.AreEqual(0, announced.Count);
		}

		[Test]
		public void Warning_EachMarkOnceInTheOrdinaryCase()
		{
			var announced = new HashSet<int>();
			LogAssert.AreEqual(600, SceneInstanceLifetime.ResolveExpiryWarning(597.0, Marks, announced), "crossing ten minutes says ten minutes");
			LogAssert.AreEqual(0, SceneInstanceLifetime.ResolveExpiryWarning(592.0, Marks, announced), "and only once");
			LogAssert.AreEqual(300, SceneInstanceLifetime.ResolveExpiryWarning(296.0, Marks, announced), "crossing five minutes says five minutes");
			LogAssert.AreEqual(60, SceneInstanceLifetime.ResolveExpiryWarning(57.0, Marks, announced), "crossing one minute says one minute");
			LogAssert.AreEqual(0, SceneInstanceLifetime.ResolveExpiryWarning(12.0, Marks, announced), "nothing after the last mark");
		}

		/// <summary>
		/// Crossing several marks between two checks must announce the tightest, with the time
		/// actually left, and retire the larger ones.
		/// </summary>
		[Test]
		public void Warning_SeveralMarksCrossedAtOnce_SaysWhatIsActuallyLeft()
		{
			var announced = new HashSet<int>();
			int said = SceneInstanceLifetime.ResolveExpiryWarning(240.0, Marks, announced);

			LogAssert.AreEqual(240, said,
				"Four minutes left must not be announced as ten: it was, whenever a check crossed more than one mark.");
			LogAssert.IsTrue(announced.Contains(600) && announced.Contains(300),
				"The skipped ten-minute mark is retired with the five-minute one, so it is never said late.");
			LogAssert.IsFalse(announced.Contains(60), "The one-minute warning is still to come.");
			LogAssert.AreEqual(0, SceneInstanceLifetime.ResolveExpiryWarning(200.0, Marks, announced));
			LogAssert.AreEqual(60, SceneInstanceLifetime.ResolveExpiryWarning(59.0, Marks, announced));
		}

		[Test]
		public void Warning_AShortLifetimeStartsAtItsOwnTightestMark()
		{
			// A difficulty with a five-minute run, first checked 20 seconds in.
			var announced = new HashSet<int>();
			LogAssert.AreEqual(300, SceneInstanceLifetime.ResolveExpiryWarning(280.0, Marks, announced),
				"Rounded up to the minute: five minutes, not ten.");
		}

		[Test]
		public void Warning_NeverUnderAMinute_AndNothingOnceClosed()
		{
			LogAssert.AreEqual(60, SceneInstanceLifetime.ResolveExpiryWarning(5.0, Marks, new HashSet<int>()),
				"Seconds left still reads as a minute, the smallest unit the warning speaks in.");
			LogAssert.AreEqual(0, SceneInstanceLifetime.ResolveExpiryWarning(0.0, Marks, new HashSet<int>()),
				"At zero the instance is closing, and the close says so itself.");
			LogAssert.AreEqual(0, SceneInstanceLifetime.ResolveExpiryWarning(double.NaN, Marks, new HashSet<int>()));
		}
	}
}
