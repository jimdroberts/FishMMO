using System;
using System.Threading.Tasks;
using NUnit.Framework;
using FishMMO.Server.Core.Collections;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="SingleFlightCache{TKey,TValue}"/>: the cache in front of every leaderboard read.
	/// </summary>
	/// <remarks>
	/// Driven by hand-held reads — a <see cref="TaskCompletionSource{TResult}"/> per fetch, completed
	/// by the test — and a hand-moved clock, so every assertion is about ordering the test chose,
	/// not about scheduling. The gates complete their continuations inline, so a read the test
	/// completes has finished by the time <c>SetResult</c> returns and the shared task can be read
	/// without awaiting.
	/// </remarks>
	[TestFixture]
	public class SingleFlightCacheTests
	{
		private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

		private DateTime now;
		private SingleFlightCache<string, int> cache;
		private int fetches;
		private TaskCompletionSource<int> gate;

		[SetUp]
		public void SetUp()
		{
			now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
			cache = new SingleFlightCache<string, int>(() => now);
			fetches = 0;
			gate = null;
		}

		/// <summary>A read that stays in flight until the test completes <see cref="gate"/>.</summary>
		private Task<int> HeldFetch()
		{
			++fetches;
			gate = new TaskCompletionSource<int>();
			return gate.Task;
		}

		private Task<int> Get(string key, Func<int, bool> keep = null) => cache.GetOrFetchAsync(key, Ttl, HeldFetch, keep);

		[Test]
		public void CallersArrivingDuringARead_ShareIt()
		{
			Task<int> first = Get("board");
			Task<int> second = Get("board");
			Task<int> third = Get("board");

			LogAssert.AreEqual(1, fetches, "three callers, one database read");
			LogAssert.AreSame(first, second, "the second caller joined the first read");
			LogAssert.AreSame(first, third, "and so did the third");
			LogAssert.IsFalse(first.IsCompleted, "nobody has an answer until the read finishes");

			gate.SetResult(42);

			LogAssert.IsTrue(first.IsCompleted, "the read finished");
			LogAssert.AreEqual(42, first.Result, "every caller gets its value");
		}

		[Test]
		public void AFreshValue_IsServedWithoutReading()
		{
			Get("board");
			gate.SetResult(7);

			now = now.AddSeconds(59);
			Task<int> again = Get("board");

			LogAssert.AreEqual(1, fetches, "59 s into a 60 s lifetime, no second read");
			LogAssert.IsTrue(again.IsCompleted && again.Result == 7, "the cached value is handed back");
		}

		[Test]
		public void AnExpiredValue_IsReadAgain()
		{
			Get("board");
			gate.SetResult(7);

			now = now.AddSeconds(61);
			Task<int> again = Get("board");

			LogAssert.AreEqual(2, fetches, "past its lifetime the value is read again");
			gate.SetResult(8);
			LogAssert.AreEqual(8, again.Result, "and the new value is served");
		}

		[Test]
		public void Freshness_CountsFromWhenTheReadStarted()
		{
			Get("board");
			now = now.AddSeconds(50);   // a slow read
			gate.SetResult(7);

			now = now.AddSeconds(11);   // 61 s after it started, 11 s after it finished
			Get("board");

			LogAssert.AreEqual(2, fetches, "the data is as old as the moment the database was asked");
		}

		[Test]
		public void AFailedRead_IsSharedByItsCallers_ButNeverCached()
		{
			Task<int> first = Get("board");
			Task<int> second = Get("board");

			gate.SetException(new InvalidOperationException("database unavailable"));

			LogAssert.IsTrue(first.IsFaulted && second.IsFaulted, "both callers see the one failure");
			LogAssert.AreEqual(typeof(InvalidOperationException), first.Exception.InnerException.GetType(), "as the read's own exception");

			Task<int> retry = Get("board");
			LogAssert.AreEqual(2, fetches, "the very next caller reads again, rather than being served the failure");
			gate.SetResult(5);
			LogAssert.AreEqual(5, retry.Result, "and gets a value");
		}

		[Test]
		public void AValueTheCallerRejects_IsReturnedButNotKept()
		{
			Task<int> first = Get("board", keep: v => v >= 0);
			gate.SetResult(-1);

			LogAssert.AreEqual(-1, first.Result, "the callers who shared the read still get what it returned");

			Get("board", keep: v => v >= 0);
			LogAssert.AreEqual(2, fetches, "but nobody after them is served it");
		}

		[Test]
		public void Invalidating_DuringARead_LetsItFinish_ThenReadsAfresh()
		{
			Task<int> inFlight = Get("board");
			TaskCompletionSource<int> firstGate = gate;

			cache.Invalidate("board");
			Task<int> next = Get("board");
			LogAssert.AreEqual(2, fetches, "a caller after the invalidation does not join the old read");

			firstGate.SetResult(1);
			gate.SetResult(2);
			LogAssert.AreEqual(1, inFlight.Result, "the old read still answers the callers who were sharing it");
			LogAssert.AreEqual(2, next.Result, "and the new one answers its own");

			Get("board");
			LogAssert.AreEqual(2, fetches, "the old read finishing late does not displace the new entry");
		}

		[Test]
		public void Keys_AreIndependent()
		{
			Get("page 1");
			Get("page 2");
			LogAssert.AreEqual(2, fetches, "a range nobody has read is its own read");
		}

		[Test]
		public void Sweep_RemovesOnlyExpiredCompletedEntries()
		{
			Get("old");
			gate.SetResult(1);

			now = now.AddSeconds(30);
			Get("young");
			gate.SetResult(2);

			now = now.AddSeconds(31);   // "old" is 61 s old, "young" 31 s
			Get("in flight");           // left pending

			LogAssert.AreEqual(3, cache.Count, "three entries before the sweep");
			int removed = cache.SweepExpired(Ttl, 16, 16);

			LogAssert.AreEqual(1, removed, "only the expired entry goes");
			LogAssert.AreEqual(2, cache.Count, "the fresh one and the one in flight stay");

			Get("young");
			LogAssert.AreEqual(3, fetches, "the fresh entry is still served after the sweep");
		}
	}
}
