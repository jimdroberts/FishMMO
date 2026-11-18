using NUnit.Framework;
using FishMMO.Server.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="IngressGuard"/>: the per-connection, per-operation debounce and in-flight guard
	/// in front of most scene-server requests.
	/// </summary>
	/// <remarks>
	/// Driven on a hand-moved clock. The capacity and sweep tests pin the defects the guard used
	/// to have: a full tracker refused every request from everyone, and the sweep walked the whole
	/// dictionary from its head each pass.
	/// </remarks>
	[TestFixture]
	public class IngressGuardTests
	{
		private const byte Op = 7;
		private double now;
		private IngressGuard guard;

		[SetUp]
		public void SetUp()
		{
			now = 1000.0;
			guard = new IngressGuard(() => now);
		}

		[Test]
		public void ARequest_InsideItsDebounce_IsRefused_AndOneAfterIsAllowed()
		{
			LogAssert.IsTrue(guard.TryBegin(1, Op, 250, out long key), "the first request is allowed");
			guard.End(key);

			now += 0.125;
			LogAssert.IsFalse(guard.TryBegin(1, Op, 250, out _), "125 ms later is inside the 250 ms debounce");

			now += 0.125;
			LogAssert.IsTrue(guard.TryBegin(1, Op, 250, out key), "250 ms after the first it is allowed");
			guard.End(key);
		}

		[Test]
		public void ARefusedRequest_DoesNotExtendTheWindow()
		{
			guard.TryBegin(1, Op, 250, out long key);
			guard.End(key);
			now += 0.125;
			guard.TryBegin(1, Op, 250, out _);
			now += 0.125;
			LogAssert.IsTrue(guard.TryBegin(1, Op, 250, out key), "the window still closes 250 ms after the accepted request");
			guard.End(key);
		}

		[Test]
		public void AnOperationInFlight_BlocksTheSameKey_UntilEnd()
		{
			LogAssert.IsTrue(guard.TryBegin(1, Op, 0, out long key), "acquired");
			now += 60;
			LogAssert.IsFalse(guard.TryBegin(1, Op, 0, out _), "still running a minute later: refused");
			LogAssert.IsTrue(guard.TryBegin(2, Op, 0, out long other), "another connection is independent");
			guard.End(other);
			guard.End(key);
			LogAssert.IsTrue(guard.TryBegin(1, Op, 0, out key), "released: allowed");
			guard.End(key);
		}

		[Test]
		public void TheGlobalRate_SpansOperations()
		{
			LogAssert.IsTrue(guard.TryBegin(1, 3, 0, out long key, globalRateMilliseconds: 125), "first operation");
			guard.End(key);
			LogAssert.IsFalse(guard.TryBegin(1, 4, 0, out _, globalRateMilliseconds: 125), "a different operation inside the global rate is refused");
			now += 0.125;
			LogAssert.IsTrue(guard.TryBegin(1, 4, 0, out key, globalRateMilliseconds: 125), "after it, allowed");
			guard.End(key);
		}

		[Test]
		public void AFullTracker_OfClosedWindows_StillAdmitsNewKeys()
		{
			for (int connection = 0; connection < 10000; connection++)
			{
				LogAssert.IsTrue(guard.TryBegin(connection, Op, 100, out long key), "filling the tracker");
				guard.End(key);
			}
			LogAssert.AreEqual(10000, guard.TrackedEntryCount, "the tracker is at its cap");

			// Every window has closed, but no sweep has run: this used to refuse everyone.
			now += 1.0;
			LogAssert.IsTrue(guard.TryBegin(20000, Op, 100, out long fresh), "a new player's first request is allowed");
			guard.End(fresh);
			LogAssert.IsTrue(guard.TryBegin(5, Op, 100, out long existing), "and an existing key is decided on its own window");
			guard.End(existing);
		}

		[Test]
		public void OnlyAFloodOfOpenWindows_RefusesANewKey()
		{
			LogAssert.IsTrue(guard.TryBegin(0, Op, 0, out long quick), "a key whose window closes at once");
			guard.End(quick);
			for (int connection = 1; connection < 10000; connection++)
			{
				guard.TryBegin(connection, Op, 10000, out long key);
				guard.End(key);
			}

			LogAssert.IsTrue(guard.TryBegin(50000, Op, 10000, out long reclaimed), "the closed entry is reclaimed to make room");
			guard.End(reclaimed);
			LogAssert.IsFalse(guard.TryBegin(50001, Op, 10000, out long refused), "with all 10,000 windows open, a new key is refused");
			LogAssert.AreEqual(0L, refused, "and gets no guard key");
		}

		[Test]
		public void Sweep_ReachesEveryStaleEntry_AndStopsAtTheFirstFreshOne()
		{
			for (int connection = 0; connection < 300; connection++)
			{
				guard.TryBegin(connection, Op, 100, out long key);
				guard.End(key);
			}
			now += 20;
			for (int connection = 300; connection < 350; connection++)
			{
				guard.TryBegin(connection, Op, 100, out long key);
				guard.End(key);
			}

			now += 15;   // the first 300 closed 35 s ago, the rest 15 s ago
			for (int pass = 0; pass < 5; pass++)
			{
				guard.Sweep(0f, 30f, 128);
			}
			LogAssert.AreEqual(50, guard.TrackedEntryCount, "every entry past the TTL went, and none younger");
		}

		[Test]
		public void Sweep_KeepsTheDebounceOfAnOperationStillRunning()
		{
			guard.TryBegin(1, Op, 100, out long key);
			now += 60;
			guard.Sweep(0f, 30f, 128);
			LogAssert.AreEqual(1, guard.TrackedEntryCount, "a running operation keeps its entry");
			LogAssert.IsFalse(guard.TryBegin(1, Op, 100, out _), "and its lock");

			guard.End(key);
			guard.Sweep(0f, 30f, 128);
			LogAssert.AreEqual(0, guard.TrackedEntryCount, "once it ends, the entry goes on the next pass");
		}

		[Test]
		public void ALeakedMarker_IsReclaimedAfterFiveMinutes()
		{
			guard.TryBegin(1, Op, 0, out _);   // End never called
			now += 299;
			guard.Sweep(0f, 30f, 128);
			LogAssert.IsFalse(guard.TryBegin(1, Op, 0, out _), "under five minutes it is still presumed running");
			now += 1;
			guard.Sweep(0f, 30f, 128);
			LogAssert.IsTrue(guard.TryBegin(1, Op, 0, out long key), "at five minutes the leaked marker is reclaimed");
			guard.End(key);
		}
	}
}
