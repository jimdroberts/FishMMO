using System;
using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Auth.Core.Collections;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The authenticator's own trackers (in <c>FishMMO-Auth</c>) are timed in monotonic seconds, and
	/// the counter behind the in-memory two-factor lockout can say how long a lockout has left.
	/// </summary>
	/// <remarks>
	/// The debounces and the IP cache in the SRP core were timed on <c>DateTime.UtcNow</c>, so a host
	/// clock stepped back held every key inside its window for the size of the step. These copies now
	/// take only monotonic readings; the tests drive them with arbitrary ones, because only the
	/// differences may matter.
	/// </remarks>
	[TestFixture]
	public class AuthTrackerClockTests
	{
		/// <summary>A monotonic reading; its origin means nothing.</summary>
		private const double T0 = 7000.0;

		[Test]
		public void ADebounce_RefusesInsideItsWindow_AndAllowsAfter()
		{
			var tracker = new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);
			TimeSpan window = TimeSpan.FromSeconds(2);

			LogAssert.IsTrue(tracker.TryBegin("alice", T0, window), "the first attempt is allowed");
			LogAssert.IsFalse(tracker.TryBegin("ALICE", T0 + 1.999, window), "inside the window it is refused");
			LogAssert.IsTrue(tracker.TryBegin("bob", T0 + 1, window), "another key is independent");
			LogAssert.IsTrue(tracker.TryBegin("alice", T0 + 2, window), "and it is allowed once the window has passed");
		}

		[Test]
		public void ADebounceSweep_RemovesOnlyWhatHasExpired()
		{
			var tracker = new ExpiringKeyTracker<int>();
			TimeSpan window = TimeSpan.FromSeconds(3);
			tracker.TryBegin(1, T0, window);
			tracker.TryBegin(2, T0 + 10, window);

			LogAssert.AreEqual(1, tracker.SweepExpired(T0 + 5, 64, 64), "the first key's window ended at +3");
			LogAssert.AreEqual(1, tracker.Count, "the second key's has not");
			LogAssert.AreEqual(1, tracker.SweepExpired(T0 + 13, 64, 64), "and it goes when its own does");
			LogAssert.IsTrue(tracker.TryBegin(1, 0.0, window), "a reading at the clock's origin is a reading like any other");
		}

		[Test]
		public void TheIpCache_ExpiresByLastSeen_AndATouchKeepsAnEntry()
		{
			var cache = new LastSeenCacheTracker<int, string>();
			TimeSpan ttl = TimeSpan.FromSeconds(120);
			cache.Upsert(1, "203.0.113.7", T0);
			cache.Upsert(2, "198.51.100.9", T0 + 10);

			LogAssert.IsTrue(cache.TryGetAndTouch(1, T0 + 100, out string ip) && ip == "203.0.113.7", "a live entry is served and touched");
			LogAssert.AreEqual(1, cache.SweepExpired(T0 + 131, ttl, 64, 64), "the untouched entry lapses 120 s after it was last seen");
			LogAssert.IsFalse(cache.TryGetAndTouch(2, T0 + 131, out _), "and is gone");
			LogAssert.IsTrue(cache.TryGetAndTouch(1, T0 + 131, out _), "the touched one is kept");
		}

		[Test]
		public void TheArrivalTracker_KeepsMonotonicFirstSeenTimes()
		{
			var tracker = new ArrivalOrderTracker<int>();
			tracker.TrackIfMissing(1, T0);
			tracker.TrackIfMissing(2, T0 + 1);
			tracker.TrackIfMissing(1, T0 + 5);

			LogAssert.IsTrue(tracker.TryPeekOldest(out int key, out double firstSeen) && key == 1 && firstSeen == T0,
				"the oldest keeps its first reading; tracking it again does not move it");
			var seen = new List<double>();
			tracker.ForEachInOrder((_, seconds, _) => seen.Add(seconds));
			LogAssert.AreEqual(2, seen.Count, "two tracked");
			LogAssert.AreEqual(T0 + 1, seen[1], "in arrival order");
		}

		[Test]
		public void AWindowCounter_SaysHowLongItsWindowHasLeft()
		{
			var counter = new FixedWindowCounter<string>(TimeSpan.FromMinutes(5));
			LogAssert.AreEqual(0.0, counter.SecondsUntilClose("nobody", T0), "a key with no window has nothing left");

			counter.Increment("locked", T0);
			LogAssert.AreEqual(300.0, counter.SecondsUntilClose("locked", T0), "a window has its whole length at its first event");
			LogAssert.AreEqual(60.0, counter.SecondsUntilClose("locked", T0 + 240), "and counts down from there");
			LogAssert.AreEqual(0.0, counter.SecondsUntilClose("locked", T0 + 300), "a closed window has nothing left");
			LogAssert.AreEqual(0, counter.GetCount("locked", T0 + 300), "and counts nothing");
		}
	}
}
