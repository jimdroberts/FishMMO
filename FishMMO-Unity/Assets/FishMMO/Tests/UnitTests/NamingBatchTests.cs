using System;
using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Client;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Batched name lookups (hot-path audit M18): the client coalesces a frame's asks into one
	/// request per type, and the server answers what it holds, fetches the rest in one query, and
	/// answers each waiter in the form it asked in.
	/// </summary>
	/// <remarks>
	/// Opening a 100-member roster used to send 100 single-ID requests in one frame, each costing a
	/// message and a database query of its own. The pure seams are pinned here; the handlers that
	/// wire them to FishNet are pinned by source in <see cref="NamingRequestBudgetTests"/>.
	/// </remarks>
	[TestFixture]
	public class NamingBatchTests
	{
		private const double Retry = 2.0;

		// ── Client: coalescing ─────────────────────────────────────────────

		[Test]
		public void AFramesAsksLeaveAsOneBatch()
		{
			var requests = new PendingNameRequests(Retry);
			for (long id = 1; id <= 100; ++id)
			{
				LogAssert.IsTrue(requests.Request(id, null, 0.0), $"ask {id} queues its ID");
			}

			var batch = new List<long>();
			LogAssert.AreEqual(100, requests.TakeBatch(0.0, NamingRequestBatchBroadcast.MaxIDs, batch), "one batch carries the whole roster");
			CollectionAssert.AreEqual(Ids(1, 100), batch, "in the order they were asked for");
			LogAssert.AreEqual(0, requests.QueuedCount, "nothing is left for the next frame");
			LogAssert.AreEqual(100, requests.WaitingCount, "and everything waits for its answer");
		}

		[Test]
		public void MoreThanOneBatchesWorthGoesOverTheFollowingFrames()
		{
			var requests = new PendingNameRequests(Retry);
			for (long id = 1; id <= 300; ++id)
			{
				requests.Request(id, null, 0.0);
			}

			var first = new List<long>();
			var second = new List<long>();
			var third = new List<long>();
			LogAssert.AreEqual(128, requests.TakeBatch(0.0, 128, first), "the first frame sends a full batch");
			LogAssert.AreEqual(128, requests.TakeBatch(0.016, 128, second), "the next frame the next");
			LogAssert.AreEqual(44, requests.TakeBatch(0.033, 128, third), "and the third the rest");
			CollectionAssert.AreEqual(Ids(129, 256), second, "the rest stay queued in order");
			LogAssert.AreEqual(0, requests.TakeBatch(0.05, 128, new List<long>()), "then nothing");
		}

		[Test]
		public void AskingAgainForAnIdInFlightChainsTheCallbackAndSendsNothing()
		{
			var requests = new PendingNameRequests(Retry);
			var heard = new List<string>();
			requests.Request(7, n => heard.Add("a:" + n), 0.0);
			LogAssert.IsFalse(requests.Request(7, n => heard.Add("b:" + n), 0.0), "the same frame: already queued");
			requests.TakeBatch(0.0, 128, new List<long>());
			LogAssert.IsFalse(requests.Request(7, n => heard.Add("c:" + n), 1.0), "inside the retry window: not sent again");

			LogAssert.IsTrue(requests.Resolve(7, out Action<string> callbacks), "the answer finds the chain");
			callbacks("Aria");
			CollectionAssert.AreEqual(new[] { "a:Aria", "b:Aria", "c:Aria" }, heard, "one answer reaches every ask");
			LogAssert.AreEqual(0, requests.WaitingCount, "and nothing is left waiting");
		}

		[Test]
		public void AnUnansweredIdIsSentAgainByTheNextAskAfterTheWindow()
		{
			var requests = new PendingNameRequests(Retry);
			requests.Request(7, null, 0.0);
			requests.TakeBatch(0.5, 128, new List<long>());

			LogAssert.IsFalse(requests.Request(7, null, 2.49), "1.99 s after it was sent: wait");
			LogAssert.IsTrue(requests.Request(7, null, 2.5), "2 s after it was sent (not after it was asked): send again");
			var batch = new List<long>();
			requests.TakeBatch(2.5, 128, batch);
			CollectionAssert.AreEqual(new long[] { 7 }, batch, "the retry goes out in the next batch");
		}

		[Test]
		public void AnAnswerThatArrivesWhileQueuedIsNotAskedForAgain()
		{
			var requests = new PendingNameRequests(Retry);
			requests.Request(1, null, 0.0);
			requests.Request(2, null, 0.0);
			requests.Resolve(1, out _);

			var batch = new List<long>();
			requests.TakeBatch(0.0, 128, batch);
			CollectionAssert.AreEqual(new long[] { 2 }, batch, "only what is still wanted goes out");
		}

		[Test]
		public void NoSuchEntityReleasesTheCallbacksAndHoldsTheRetryWindow()
		{
			var requests = new PendingNameRequests(Retry);
			bool called = false;
			requests.Request(9, _ => called = true, 0.0);
			requests.TakeBatch(0.0, 128, new List<long>());

			LogAssert.IsTrue(requests.ResolveMissing(9, 0.1), "the not-found answer finds what was waiting");
			LogAssert.IsFalse(called, "and does not call it: the callers expect a name");
			LogAssert.AreEqual(0, requests.WaitingCount, "nothing is held for the session any more");

			LogAssert.IsFalse(requests.Request(9, _ => { }, 1.0), "asked again at once: not re-sent every frame");
			LogAssert.IsTrue(requests.Request(9, _ => { }, 2.1), "after the window: asked again");
		}

		// ── Server: budget and request ─────────────────────────────────────

		[Test]
		public void ABatchIsChargedPerIdAndAnsweredInPartPastTheBudget()
		{
			long now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc).Ticks;
			NamingRequestBucket bucket = NamingRequestBucket.Full(200, now);
			LogAssert.AreEqual(128, bucket.TryTakeUpTo(now, 200, 20.0, 128), "a full batch fits a full bucket");
			LogAssert.AreEqual(72, bucket.TryTakeUpTo(now, 200, 20.0, 128), "the next is answered as far as the budget goes");
			LogAssert.AreEqual(0, bucket.TryTakeUpTo(now, 200, 20.0, 128), "then nothing");
			LogAssert.AreEqual(20, bucket.TryTakeUpTo(now + TimeSpan.TicksPerSecond, 200, 20.0, 128), "a second later, one second's refill");
			LogAssert.AreEqual(5, default(NamingRequestBucket).TryTakeUpTo(now, 0, 0.0, 5), "a zero capacity admits everything");
			LogAssert.AreEqual(0, bucket.TryTakeUpTo(now, 200, 20.0, 0), "asking for nothing takes nothing");
		}

		[Test]
		public void TheRequestIsReadOnlyAsFarAsItWasAdmittedAndOnlyOncePerId()
		{
			var seen = new HashSet<long>();
			var ids = new List<long>();
			int added = NamingBatchRequest.SelectIds(new long[] { 5, 0, 5, -3, 6, 7, 8 }, 5, seen, ids);
			CollectionAssert.AreEqual(new long[] { 5, 6 }, ids, "distinct positive IDs among the first five entries");
			LogAssert.AreEqual(2, added, "counted");
			LogAssert.AreEqual(0, NamingBatchRequest.SelectIds(null, 128, seen, new List<long>()), "a null array reads as empty");
		}

		// ── Server: answers ────────────────────────────────────────────────

		[Test]
		public void EachWaiterIsAnsweredInTheFormItAskedIn()
		{
			LogAssert.IsTrue(NamingAnswerRule.TryGetReply(true, "Aria", out string reply) && reply == "Aria", "found, batched");
			LogAssert.IsTrue(NamingAnswerRule.TryGetReply(false, "Aria", out reply) && reply == "Aria", "found, single");
			LogAssert.IsTrue(NamingAnswerRule.TryGetReply(true, null, out reply) && reply == string.Empty, "not found, batched: told so, with an empty name");
			LogAssert.IsFalse(NamingAnswerRule.TryGetReply(false, null, out _), "not found, single: that form never had an answer for it");
			LogAssert.IsFalse(NamingAnswerRule.TryGetReply(false, string.Empty, out _), "an empty name is not a name");
		}

		[Test]
		public void TheSameConnectionAskingInBothFormsWaitsTwice()
		{
			var table = new InFlightLookupTable<long, NamingWaiter<string>>(100, 8);
			DateTime now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
			TimeSpan stale = TimeSpan.FromSeconds(15);
			table.Join(42, new NamingWaiter<string>("alice", batched: true), now, stale);
			table.Join(42, new NamingWaiter<string>("alice", batched: true), now, stale);
			table.Join(42, new NamingWaiter<string>("bob", batched: false), now, stale);
			table.Join(42, new NamingWaiter<string>("bob", batched: true), now, stale);

			var waiters = new List<NamingWaiter<string>>();
			table.TryComplete(42, waiters);
			LogAssert.AreEqual(3, waiters.Count, "alice once; bob once per form, so each form gets its answer");
		}

		[Test]
		public void AnswersAreGroupedPerConnectionAndSplitAtTheReplySize()
		{
			var batches = new NamingReplyBatches<string>();
			for (long id = 1; id <= 5; ++id)
			{
				batches.Add("alice", id, "n" + id);
			}
			batches.Add("bob", 9, null);

			var sent = new List<(string conn, long[] ids, string[] names)>();
			batches.Drain(2, (conn, ids, names) => sent.Add((conn, ids, names)));

			LogAssert.AreEqual(4, sent.Count, "alice's five in replies of two, then bob's one");
			CollectionAssert.AreEqual(new long[] { 1, 2 }, sent[0].ids, "first reply");
			CollectionAssert.AreEqual(new[] { "n1", "n2" }, sent[0].names, "names parallel to IDs");
			CollectionAssert.AreEqual(new long[] { 5 }, sent[2].ids, "the remainder");
			LogAssert.AreEqual("bob", sent[3].conn, "bob after alice, in the order first added");
			LogAssert.AreEqual(string.Empty, sent[3].names[0], "not found travels as an empty name");

			sent.Clear();
			batches.Drain(2, (conn, ids, names) => sent.Add((conn, ids, names)));
			LogAssert.AreEqual(0, sent.Count, "a drain empties the batches");
			LogAssert.AreEqual(0, batches.ConnectionCount, "for the next request");
		}

		[Test]
		public void TheWireConstantsBoundBothDirectionsAlike()
		{
			LogAssert.AreEqual(128, NamingRequestBatchBroadcast.MaxIDs, "request cap");
			LogAssert.IsTrue(NamingBatchBroadcast.MaxEntries >= NamingRequestBatchBroadcast.MaxIDs,
				"a whole request's answers fit one reply");
		}

		private static long[] Ids(long first, long last)
		{
			var ids = new long[last - first + 1];
			for (long i = first; i <= last; ++i)
			{
				ids[i - first] = i;
			}
			return ids;
		}
	}
}
