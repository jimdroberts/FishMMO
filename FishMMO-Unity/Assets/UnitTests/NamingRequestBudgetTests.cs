using System;
using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Server.Core.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Name lookups: the per-connection budget and the coalescing of lookups already in flight
	/// (hot-path audit M18).
	/// </summary>
	/// <remarks>
	/// The server dropped every naming request inside 75 ms of the previous one from the same
	/// connection, so a 100-member roster opened in one frame had 99 of its requests thrown away;
	/// and a request for an ID another connection was already waiting on was dropped as well.
	/// Requests are now charged against a token bucket, and a second request joins the lookup in
	/// flight and receives its answer.
	/// </remarks>
	[TestFixture]
	public class NamingRequestBudgetTests
	{
		private const string NamingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Naming/NamingSystem.cs";
		private static readonly DateTime T0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Stale = TimeSpan.FromSeconds(15);

		// ── The budget ────────────────────────────────────────────────────────

		[Test]
		public void AFullRosterOpenedInOneFrameIsAnsweredInFull()
		{
			long now = T0.Ticks;
			NamingRequestBucket bucket = NamingRequestBucket.Full(200, now);
			for (int i = 0; i < 100; ++i)
			{
				LogAssert.IsTrue(bucket.TryTake(now, 200, 20.0), $"request {i + 1} of a 100-member roster, all in one frame");
			}
		}

		[Test]
		public void TheBudgetRefusesPastItsBurstAndRefillsAtItsRate()
		{
			long now = T0.Ticks;
			NamingRequestBucket bucket = NamingRequestBucket.Full(3, now);
			LogAssert.IsTrue(bucket.TryTake(now, 3, 2.0), "1");
			LogAssert.IsTrue(bucket.TryTake(now, 3, 2.0), "2");
			LogAssert.IsTrue(bucket.TryTake(now, 3, 2.0), "3");
			LogAssert.IsFalse(bucket.TryTake(now, 3, 2.0), "the burst is spent");

			now += TimeSpan.TicksPerSecond;
			LogAssert.IsTrue(bucket.TryTake(now, 3, 2.0), "a second refills two: one");
			LogAssert.IsTrue(bucket.TryTake(now, 3, 2.0), "two");
			LogAssert.IsFalse(bucket.TryTake(now, 3, 2.0), "no third");

			now += TimeSpan.TicksPerSecond * 60;
			LogAssert.IsTrue(bucket.TryTake(now, 3, 2.0) && bucket.TryTake(now, 3, 2.0) && bucket.TryTake(now, 3, 2.0), "a long idle refills to capacity");
			LogAssert.IsFalse(bucket.TryTake(now, 3, 2.0), "and no further");
		}

		[Test]
		public void AZeroCapacityTurnsTheBudgetOff()
		{
			NamingRequestBucket bucket = default;
			LogAssert.IsTrue(bucket.TryTake(T0.Ticks, 0, 0.0), "disabled");
		}

		// ── Coalescing ────────────────────────────────────────────────────────

		private static InFlightLookupTable<long, string> Table(int maxEntries = 100, int maxWaiters = 8) =>
			new InFlightLookupTable<long, string>(maxEntries, maxWaiters);

		[Test]
		public void ASecondRequestJoinsTheLookupInFlightAndGetsItsAnswer()
		{
			var table = Table();
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Started, table.Join(42, "alice", T0, Stale), "the first request starts the fetch");
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Joined, table.Join(42, "bob", T0, Stale), "the second joins it instead of being dropped");
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Joined, table.Join(42, "bob", T0, Stale), "asking twice waits once");

			var waiters = new List<string>();
			LogAssert.IsTrue(table.TryComplete(42, waiters), "the fetch completes the key");
			CollectionAssert.AreEqual(new[] { "alice", "bob" }, waiters, "and the answer goes to everyone who asked");
			LogAssert.AreEqual(0, table.Count, "the key is free again");
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Started, table.Join(42, "carol", T0, Stale), "a later request starts a new fetch");
		}

		[Test]
		public void ALookupWhoseAnswerNeverCameBackIsRestartedNotWaitedOnForever()
		{
			var table = Table();
			table.Join(42, "alice", T0, Stale);
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Joined, table.Join(42, "bob", T0.AddSeconds(14), Stale), "inside the bound: wait");
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Started, table.Join(42, "carol", T0.AddSeconds(15), Stale), "past it: start again");

			var waiters = new List<string>();
			table.TryComplete(42, waiters);
			CollectionAssert.AreEqual(new[] { "alice", "bob", "carol" }, waiters, "the restarted fetch answers the earlier waiters too");
		}

		[Test]
		public void TheSweepDropsOnlyLookupsPastTheBound()
		{
			var table = Table();
			table.Join(1, "a", T0, Stale);
			table.Join(2, "b", T0.AddSeconds(10), Stale);
			LogAssert.AreEqual(1, table.SweepStale(T0.AddSeconds(16), Stale), "only the lookup out for 16 s is dropped");
			LogAssert.IsFalse(table.TryComplete(1, null), "gone");
			LogAssert.IsTrue(table.TryComplete(2, null), "kept");
		}

		[Test]
		public void TheTableAndEachKeyAreBounded()
		{
			var table = Table(maxEntries: 2, maxWaiters: 2);
			table.Join(1, "a", T0, Stale);
			table.Join(2, "a", T0, Stale);
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Refused, table.Join(3, "a", T0, Stale), "no third key");

			table.Join(1, "b", T0, Stale);
			LogAssert.AreEqual(InFlightLookupTable<long, string>.JoinResult.Refused, table.Join(1, "c", T0, Stale), "no third waiter on one key");
		}

		// ── Source pin, with its control run ─────────────────────────────────

		[Test]
		public void EveryLookupGoesThroughTheBudgetAndTheCoalescingTable()
		{
			string code = SourceScanPins.ReadCode(NamingPath);
			Func<string, string> check = c =>
			{
				if (c.Contains("requestDebounceMilliseconds") || c.Contains("IsRequestDebounced("))
				{
					return "the 75 ms drop window is back";
				}
				if (c.Contains(".TryAdd(") || c.Contains(".TryRemove("))
				{
					return "a lookup is flagged in flight by hand again, which drops the second requester";
				}
				string single = SourceScanPins.Body(c, "private void OnServerNamingBroadcastReceived(");
				string batch = SourceScanPins.Body(c, "private void OnServerNamingRequestBatchBroadcastReceived(");
				string reverse = SourceScanPins.Body(c, "private void OnServerReverseNamingBroadcastReceived(");
				string resolve = SourceScanPins.Body(c, "private void ResolveNames(");
				if (single == null || batch == null || reverse == null || resolve == null)
				{
					return "a request handler, or the shared resolver, is missing";
				}
				if (!single.Contains("MayRequestNames(conn, msg.Type)") || !batch.Contains("MayRequestNames(conn, msg.Type)"))
				{
					return "a forward handler skips the requester gate";
				}
				if (!single.Contains("TryTakeRequestToken(conn)") || !reverse.Contains("TryTakeRequestToken(conn)"))
				{
					return "a single-ID handler skips the budget";
				}
				if (!batch.Contains("TryTakeRequestTokens(conn, Math.Min(msg.IDs.Length, NamingRequestBatchBroadcast.MaxIDs))") ||
					!batch.Contains("SelectIds(msg.IDs, admitted,"))
				{
					return "the batch handler is not charged per ID, or reads past what was admitted";
				}
				if (!single.Contains("ResolveNames(conn, msg.Type, requestIds, batched: false)") ||
					!batch.Contains("ResolveNames(conn, msg.Type, requestIds, batched: true)"))
				{
					return "a forward handler does not resolve through the shared, coalescing path";
				}
				string joinThenFetch = SourceScanPins.InOrder(resolve,
					"table.Join(id, waiter, now, InFlightStaleAfter) == InFlightLookupTable<long, NamingWaiter<NetworkConnection>>.JoinResult.Started",
					"idsToFetch.Add(id)",
					"FetchNamesAsync(type, fetchIds)");
				if (joinThenFetch != null)
				{
					return "the resolver does not start one fetch for the IDs it started: " + joinThenFetch;
				}
				return reverse.Contains("BeginLookup(runtimeData.CharacterByNameInFlight") ? null : "the reverse lookup does not coalesce";
			};
			SourceScanPins.HoldsAndFires("M18 naming", code, check,
				SourceScanPins.Replace("table.Join(id, waiter, now, InFlightStaleAfter) == InFlightLookupTable<long, NamingWaiter<NetworkConnection>>.JoinResult.Started",
					"table.Count < 5000"),
				"a lookup that bypasses the coalescing table");
		}

		[Test]
		public void ABatchedLookupIsOneQueryAndItsWaitersAreAnsweredInTheirOwnForm()
		{
			string code = SourceScanPins.ReadCode(NamingPath);
			Func<string, string> check = c =>
			{
				string fetch = SourceScanPins.Body(c, "private async Task FetchNamesAsync(NamingSystemType type, long[] ids)");
				string complete = SourceScanPins.Body(c, "private void CompleteNameLookups(");
				string answer = SourceScanPins.Body(c, "private void Answer(");
				if (fetch == null || complete == null || answer == null)
				{
					return "the batched fetch, its completion or the answer is missing";
				}
				if (!fetch.Contains("characterService.FetchNamesAsync(ids)") || !fetch.Contains("guildService.FetchNamesAsync(ids)"))
				{
					return "the batched fetch does not resolve its IDs in one query";
				}
				if (fetch.Contains("for (") || fetch.Contains("FetchAsync(") || fetch.Contains("FetchNameAsync("))
				{
					return "the batched fetch reads one ID at a time";
				}
				if (!fetch.Contains("TryEnqueueMainThread(() => CompleteNameLookups(type, ids, answered))"))
				{
					return "the batched fetch does not always hand its IDs back to be completed";
				}
				if (!complete.Contains("table.TryComplete(id, completedNameWaiters)") || !complete.Contains("SendReplyBatches(type)"))
				{
					return "the completion does not release every waiter or send the batched replies";
				}
				return answer.Contains("NamingAnswerRule.TryGetReply(waiter.Batched, name, out string reply)") ? null : "a waiter is not answered in the form it asked in";
			};
			SourceScanPins.HoldsAndFires("M18 batched fetch", code, check,
				SourceScanPins.Replace("characterService.FetchNamesAsync(ids)", "characterService.FetchAsync(ids[0])"),
				"a batched fetch that reads one character");
		}
	}
}
