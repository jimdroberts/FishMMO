using System;
using NUnit.Framework;
using FishMMO.Database.Data;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The kick poll keeps its place on the database clock, reads a commit window behind what it
	/// has settled, and knows a kick by (id, stamp).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What was wrong.</b> Every game server's kick poll kept a <c>(time_created, id)</c> cursor
	/// seeded from its own host clock and compared with stamps the database wrote: a host running
	/// ahead of the database skipped every kick stamped inside its lead, and a kick whose
	/// transaction committed after a later-stamped kick's (a ban writes its kick inside a longer
	/// transaction, stamped at that transaction's start) was passed before it was visible. Either
	/// way the operator's kick never landed.
	/// </para>
	/// <para>
	/// The window is pure, so its rules are pinned here. The SQL behind it — the lookback of a first
	/// read, the (id, stamp) exclusion, a re-stamped row read again, a kick committed out of order
	/// read once it commits, a refused kick read again, the database-clock pending check and the
	/// database-clock last login — was run against a throwaway PostgreSQL.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class KickRequestReadWindowTests
	{
		private static readonly DateTime T0 = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

		[Test]
		public void AFirstQuery_ReachesBackToWhenTheReaderStarted()
		{
			var window = new KickRequestReadWindow(Window);
			KickRequestPollQuery query = window.BuildQuery(100, 42.5);
			LogAssert.IsNull(query.FromUtc, "a first read has no start of its own: the database's now less the lookback");
			LogAssert.AreEqual(42.5, query.FirstReadLookbackSeconds, "the lookback is the reader's own measured duration");
			LogAssert.AreEqual(100, query.PageSize);
			LogAssert.AreEqual(0, query.HandledIds.Length, "nothing handled yet");

			window.CompleteRead(T0.AddSeconds(-42.5), T0, null);
			KickRequestPollQuery second = window.BuildQuery(100, 99.0);
			LogAssert.IsNotNull(second.FromUtc, "a later read starts at the watermark");
			LogAssert.AreEqual(0.0, second.FirstReadLookbackSeconds, "and never reaches back again");
			LogAssert.IsTrue(window.BuildQuery(100, double.NaN).FirstReadLookbackSeconds == 0.0, "a nonsense lookback is none");
		}

		[Test]
		public void TheWatermark_TrailsWhatWasSettledByTheCommitWindow_ButNeverFallsBelowTheFloor()
		{
			var window = new KickRequestReadWindow(Window);
			window.CompleteRead(T0.AddSeconds(-3), T0, null);
			LogAssert.AreEqual(T0.AddSeconds(-3), window.Watermark, "held at the floor: the window would reach behind the reader's start");

			window.CompleteRead(T0.AddSeconds(-3), T0.AddSeconds(60), null);
			LogAssert.AreEqual(T0.AddSeconds(50), window.Watermark, "a drained read settles to its start, less the commit window");

			window.CompleteRead(T0.AddSeconds(50), T0.AddSeconds(55), null);
			LogAssert.AreEqual(T0.AddSeconds(50), window.Watermark, "the watermark never moves back");
		}

		[Test]
		public void UnsettledFrom_IsTheTruthTable()
		{
			DateTime last = T0.AddSeconds(5);
			DateTime refused = T0.AddSeconds(2);
			LogAssert.IsNull(KickRequestReadWindow.UnsettledFrom(true, last, null), "a short page with nothing refused settled everything");
			LogAssert.IsNull(KickRequestReadWindow.UnsettledFrom(true, null, null), "so did an empty one");
			LogAssert.AreEqual(last, KickRequestReadWindow.UnsettledFrom(false, last, null), "a full page settled only up to its last row");
			LogAssert.AreEqual(refused, KickRequestReadWindow.UnsettledFrom(true, last, refused), "a refusal holds the window at the refused kick");
			LogAssert.AreEqual(refused, KickRequestReadWindow.UnsettledFrom(false, last, refused), "whether or not the page was full");
		}

		[Test]
		public void ARefusedKick_IsReadAgain_AndTheOnesBeforeItAreNot()
		{
			var window = new KickRequestReadWindow(Window);
			window.CompleteRead(T0.AddSeconds(-30), T0.AddSeconds(-20), null);

			// Three kicks read; the first handed on, the second refused by the main-thread queue.
			DateTime a = T0.AddSeconds(1), b = T0.AddSeconds(2);
			window.MarkHandled(1, a);
			window.CompleteRead(window.Watermark.Value, T0.AddSeconds(30), KickRequestReadWindow.UnsettledFrom(true, T0.AddSeconds(3), b));

			LogAssert.IsTrue(window.Watermark.Value <= b, "the next read starts at or before the refused kick");
			KickRequestPollQuery next = window.BuildQuery(100, 0);
			LogAssert.AreEqual(1, next.HandledIds.Length, "and skips the one already handed on");
			LogAssert.AreEqual(1L, next.HandledIds[0]);
			LogAssert.AreEqual(a, next.HandledStamps[0], "by its stamp too");
		}

		[Test]
		public void AKick_IsKnownByIdAndStamp_SoAReStampedRowIsANewKick()
		{
			var window = new KickRequestReadWindow(Window);
			LogAssert.IsTrue(window.MarkHandled(7, T0), "first handled");
			LogAssert.IsTrue(window.IsHandled(7, T0), "known");
			LogAssert.IsFalse(window.MarkHandled(7, T0), "handling it twice is refused");

			// The account kicked again: the row keeps id 7 and moves its stamp.
			LogAssert.IsFalse(window.IsHandled(7, T0.AddSeconds(4)), "the re-stamped row is a new kick");
			LogAssert.IsTrue(window.MarkHandled(7, T0.AddSeconds(4)), "and is handled in its own right");
			window.SnapshotHandled(out long[] ids, out DateTime[] stamps);
			LogAssert.AreEqual(1, ids.Length, "one row, recorded at its latest stamp");
			LogAssert.AreEqual(T0.AddSeconds(4), stamps[0]);
		}

		[Test]
		public void HandledKicks_AreForgottenOnceTheWatermarkPassesThem()
		{
			var window = new KickRequestReadWindow(Window);
			window.CompleteRead(T0.AddSeconds(-5), T0, null);
			window.MarkHandled(1, T0.AddSeconds(1));
			window.MarkHandled(2, T0.AddSeconds(20));
			window.CompleteRead(T0.AddSeconds(-5), T0.AddSeconds(25), null);
			LogAssert.AreEqual(T0.AddSeconds(15), window.Watermark);
			LogAssert.AreEqual(1, window.HandledCount, "the kick behind the watermark can never be returned again");
			LogAssert.IsTrue(window.IsHandled(2, T0.AddSeconds(20)), "the one inside the window is still skipped");
		}

		[Test]
		public void Reset_StartsAgainFromAFirstRead()
		{
			var window = new KickRequestReadWindow(Window);
			window.CompleteRead(T0, T0.AddSeconds(30), null);
			window.MarkHandled(3, T0.AddSeconds(29));
			window.Reset();
			LogAssert.IsNull(window.Watermark);
			LogAssert.AreEqual(0, window.HandledCount);
			LogAssert.IsNull(window.BuildQuery(10, 5).FromUtc, "the next read is a first read again");
		}
	}
}
