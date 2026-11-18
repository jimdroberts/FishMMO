using System;
using System.Linq;
using FishMMO.Database.Data;
using FishMMO.Server.Core;
using FishMMO.Server.Implementation;
using FishNet.Transporting.WebTransport;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The rules a server's bandwidth sampler files its transport counters by: which minute a
	/// byte lands in, how a minute's running total is rewritten, what a restart and a missing
	/// layer do, and the bucket arithmetic the database side shares.
	/// </summary>
	/// <remarks>
	/// The ledger is pure, so every rule here runs without a network manager or a database. The
	/// SQL that stores and rolls the rows up was validated against a throwaway PostgreSQL; the
	/// pins in <see cref="ServerBandwidthPinsTests"/> keep its shape.
	/// </remarks>
	[TestFixture]
	public class ServerBandwidthLedgerTests
	{
		private static readonly DateTime Noon = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

		private static DateTime At(int minute, int second) => Noon.AddMinutes(minute).AddSeconds(second);

		/// <summary>A native snapshot with every layer measured.</summary>
		private static TransportTrafficSnapshot Snap(double seconds, long app, long quic, long datagrams,
			int sessionsActive = 0, long sessionsOpened = 0, long refused = 0, long handshakeFailures = 0)
		{
			return new TransportTrafficSnapshot
			{
				TimestampSeconds = seconds,
				Backend = TransportTrafficBackend.Native,
				AppSentReliableBytes = app,
				AppRecvReliableBytes = app / 2,
				QuicMeasure = TrafficMeasure.Measured,
				QuicSentBytes = quic,
				QuicRecvBytes = quic / 2,
				DatagramMeasure = TrafficMeasure.Measured,
				UdpSentDatagrams = datagrams,
				UdpRecvDatagrams = datagrams / 2,
				SessionsActive = sessionsActive,
				SessionsOpened = sessionsOpened,
				Process = new TransportProcessCounters { IsValid = true, RefusedByLimits = refused, HandshakeFailures = handshakeFailures },
			};
		}

		/// <summary>A native snapshot taken before msquic was ever loaded in the process.</summary>
		private static TransportTrafficSnapshot BeforeLoad(double seconds)
		{
			return new TransportTrafficSnapshot
			{
				TimestampSeconds = seconds,
				Backend = TransportTrafficBackend.Native,
				QuicMeasure = TrafficMeasure.Unavailable,
				QuicSentBytes = -1,
				QuicRecvBytes = -1,
				DatagramMeasure = TrafficMeasure.Unavailable,
				UdpSentDatagrams = -1,
				UdpRecvDatagrams = -1,
				Process = TransportProcessCounters.Unknown,
			};
		}

		// ── Shared constants and bucket arithmetic ─────────────────────────

		[Test]
		public void TheStoredTierNumbers_AreTheServerTypes()
		{
			LogAssert.AreEqual((int)ServerType.Login, ServerBandwidthKind.Login, "login");
			LogAssert.AreEqual((int)ServerType.World, ServerBandwidthKind.World, "world");
			LogAssert.AreEqual((int)ServerType.Scene, ServerBandwidthKind.Scene, "scene");
			LogAssert.AreEqual(ServerBandwidthKind.Login, ServerBandwidthRecorder.KindOf(ServerType.Login), "the recorder maps login");
			LogAssert.AreEqual(ServerBandwidthKind.Scene, ServerBandwidthRecorder.KindOf(ServerType.Scene), "the recorder maps scene");
			LogAssert.AreEqual(0, ServerBandwidthRecorder.KindOf(ServerType.Invalid), "an invalid type records nothing");
		}

		[Test]
		public void TheWireEstimate_UsesTheTransportsHeaderSize()
		{
			/* The database assembly cannot reference the transport, so the constant is written
			 * twice. If IPv6 ever becomes real, both move together or the page and the client
			 * panel disagree about the same bytes. */
			LogAssert.AreEqual(TransportTrafficMath.IpV4UdpHeaderBytes, ServerBandwidthMath.IpV4UdpHeaderBytes, "header bytes per datagram");
			LogAssert.AreEqual(1000L + (10L * 28L), ServerBandwidthMath.EstimateWireBytes(1000, 10), "payload + 28 per datagram");
		}

		[Test]
		public void BucketArithmetic_TruncatesAndStitchesOnWholeUnits()
		{
			var t = new DateTime(2026, 9, 26, 12, 34, 56, 789, DateTimeKind.Utc);
			LogAssert.AreEqual(new DateTime(2026, 9, 26, 12, 34, 0, DateTimeKind.Utc), ServerBandwidthMath.FloorToMinute(t), "minute");
			LogAssert.AreEqual(new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc), ServerBandwidthMath.FloorToHour(t), "hour");
			LogAssert.AreEqual(new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc), ServerBandwidthMath.StitchPoint(t), "the stitch is three whole hours back");
			LogAssert.AreEqual(new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc), ServerBandwidthMath.MonthWindowStart(t), "30 days, whole hours");
			LogAssert.AreEqual(new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc), ServerBandwidthMath.MinuteRetentionCutoff(t), "14 days, whole hours");
			LogAssert.IsTrue(double.IsNaN(ServerBandwidthMath.PerSecond(100, 0)), "an empty interval has no rate, not a rate of zero");
			LogAssert.AreEqual(50.0, ServerBandwidthMath.PerSecond(3000, 60_000), "bytes over the row's own interval");
		}

		[Test]
		public void TheRollupWindow_ReachesPastTheLatestARetriedMinuteCanLand()
		{
			/* A server re-sends a minute for up to MaxPendingMinutes; if the rollup stopped
			 * re-rolling that minute's hour before the row could arrive, the hour would be short. */
			LogAssert.IsTrue(ServerBandwidthMath.RollupWindowHours * 60 > ServerBandwidthMath.MaxPendingMinutes + 60,
				"the rolling window must cover the retry horizon plus the hour it lands in");
			LogAssert.IsTrue(ServerBandwidthMath.RollupWindowHours > ServerBandwidthMath.StitchHours,
				"the page's hour rows must be re-rolled at least as far back as it reads them");
		}

		// ── The ledger ──────────────────────────────────────────────────────

		[Test]
		public void TheFirstSample_CountsFromTheStart_IncludingTrafficBeforeMsquicLoaded()
		{
			/* Recording starts before the transport opens, when the QUIC layer is Unavailable.
			 * That is zero by construction, and the first minute — every client's handshake — must
			 * not be lost to it. */
			var ledger = new ServerBandwidthLedger(BeforeLoad(100));
			var outcome = ledger.Record(Snap(160, app: 5000, quic: 9000, datagrams: 40, sessionsActive: 3, sessionsOpened: 3, refused: 2, handshakeFailures: 1), At(1, 5));

			LogAssert.AreEqual(ServerBandwidthSampleOutcome.Recorded, outcome, "recorded");
			var row = ledger.Pending().Single();
			LogAssert.AreEqual(At(1, 0), row.BucketStartUtc, "filed under the database minute it was taken in");
			LogAssert.AreEqual(60_000L, row.IntervalMs, "the interval since recording began");
			LogAssert.AreEqual(9000L, row.UdpSentBytes, "UDP payload from zero");
			LogAssert.AreEqual(40L, row.UdpSentDatagrams, "datagrams from zero");
			LogAssert.AreEqual(5000L, row.AppSentBytes, "application bytes");
			LogAssert.AreEqual(3L, row.SessionsActive, "the session gauge");
			LogAssert.AreEqual(3L, row.SessionsOpened, "sessions opened");
			LogAssert.AreEqual(2L, row.ConnectionsRefused, "refusals");
			LogAssert.AreEqual(1L, row.HandshakeFailures, "handshake failures");
			LogAssert.IsNull(row.Invalid(), "a recorded row is always writable");
		}

		[Test]
		public void ConsecutiveMinutes_EachHoldTheirOwnInterval_AndTheTotalIsConserved()
		{
			var ledger = new ServerBandwidthLedger(Snap(0, 0, 0, 0));
			ledger.Record(Snap(60, 1000, 2000, 10), At(1, 0));
			ledger.Record(Snap(120, 1500, 2600, 13), At(2, 0));
			ledger.Record(Snap(180, 4000, 9000, 30), At(3, 0));

			var rows = ledger.Pending();
			LogAssert.AreEqual(3, rows.Count, "one row per minute");
			LogAssert.AreEqual(2000L, rows[0].UdpSentBytes, "first minute");
			LogAssert.AreEqual(600L, rows[1].UdpSentBytes, "second minute: its own delta");
			LogAssert.AreEqual(6400L, rows[2].UdpSentBytes, "third minute");
			LogAssert.AreEqual(9000L, rows.Sum(r => r.UdpSentBytes), "every byte lands in exactly one minute");
			LogAssert.AreEqual(30L, rows.Sum(r => r.UdpSentDatagrams), "and every datagram");
		}

		[Test]
		public void TwoSamplesInOneMinute_RewriteThatMinutesRunningTotal()
		{
			/* A late frame or a clock step can put two samples in one minute. The row is a SET of
			 * the minute's running total, so the second contains the first and nothing is lost or
			 * counted twice. */
			var ledger = new ServerBandwidthLedger(Snap(0, 0, 0, 0));
			ledger.Record(Snap(60, 1000, 2000, 10, sessionsActive: 5), At(1, 0));
			ledger.Record(Snap(119, 1800, 3000, 16, sessionsActive: 2), At(1, 59));

			var row = ledger.Pending().Single();
			LogAssert.AreEqual(At(1, 0), row.BucketStartUtc, "one minute");
			LogAssert.AreEqual(3000L, row.UdpSentBytes, "the running total, not the second delta");
			LogAssert.AreEqual(119_000L, row.IntervalMs, "the interval covers both samples");
			LogAssert.AreEqual(5L, row.SessionsActive, "the higher gauge of the two");
		}

		[Test]
		public void TheDatabaseClockSteppingBack_NeverReopensAClosedMinute()
		{
			var ledger = new ServerBandwidthLedger(Snap(0, 0, 0, 0));
			ledger.Record(Snap(60, 1000, 2000, 10), At(5, 0));
			ledger.Record(Snap(120, 1500, 2500, 12), At(3, 30));

			var rows = ledger.Pending();
			LogAssert.AreEqual(1, rows.Count, "no row for the earlier minute");
			LogAssert.AreEqual(At(5, 0), rows[0].BucketStartUtc, "filed under the open minute");
			LogAssert.AreEqual(2500L, rows[0].UdpSentBytes, "as its running total");
		}

		[Test]
		public void AnUnmeasuredLayer_DefersTheSample_AndTheNextCoversIt()
		{
			var ledger = new ServerBandwidthLedger(BeforeLoad(0));
			LogAssert.AreEqual(ServerBandwidthSampleOutcome.Deferred, ledger.Record(BeforeLoad(60), At(1, 0)),
				"msquic not loaded yet: nothing to difference");
			LogAssert.AreEqual(0, ledger.PendingCount, "nothing filed");

			LogAssert.AreEqual(ServerBandwidthSampleOutcome.Recorded, ledger.Record(Snap(120, 700, 1400, 9), At(2, 0)), "measured now");
			var row = ledger.Pending().Single();
			LogAssert.AreEqual(1400L, row.UdpSentBytes, "the deferred interval is carried, not lost");
			LogAssert.AreEqual(120_000L, row.IntervalMs, "over the whole stretch");
		}

		[Test]
		public void CountersGoingBackwards_Rebaseline_AndTheOldMinuteIsNeverRewritten()
		{
			/* Not one process lifetime (an editor domain reload). The interval is unknown and is
			 * not written; the minute that holds traffic against the old baseline is closed, so
			 * nothing measured against the new one is SET over it. */
			var ledger = new ServerBandwidthLedger(Snap(0, 0, 0, 0));
			ledger.Record(Snap(60, 10_000, 20_000, 100), At(1, 0));
			LogAssert.AreEqual(ServerBandwidthSampleOutcome.Rebaselined, ledger.Record(Snap(90, 50, 70, 1), At(1, 30)), "backwards");
			ledger.Record(Snap(100, 150, 270, 3), At(1, 40));

			var rows = ledger.Pending();
			LogAssert.AreEqual(2, rows.Count, "the old minute and a new one");
			LogAssert.AreEqual(20_000L, rows[0].UdpSentBytes, "the old minute keeps what was measured against the old baseline");
			LogAssert.AreEqual(At(2, 0), rows[1].BucketStartUtc, "the next traffic takes the next minute");
			LogAssert.AreEqual(200L, rows[1].UdpSentBytes, "measured from the new baseline");
		}

		[Test]
		public void ARestartedProcess_CountsFromZeroInItsOwnLedger()
		{
			/* A restart is a new process: new counters from zero, a new ledger, a new instance id
			 * (the recorder's). Both write the same minute without touching each other's row. */
			var before = new ServerBandwidthLedger(Snap(0, 0, 0, 0));
			before.Record(Snap(30, 900_000, 1_000_000, 900), At(1, 30));
			var after = new ServerBandwidthLedger(BeforeLoad(0));
			after.Record(Snap(20, 100, 3_000, 5), At(1, 50));

			LogAssert.AreEqual(1_000_000L, before.Pending().Single().UdpSentBytes, "the old process's last minute");
			LogAssert.AreEqual(3_000L, after.Pending().Single().UdpSentBytes, "the new process's first minute, never negative");
			LogAssert.AreEqual(before.Pending().Single().BucketStartUtc, after.Pending().Single().BucketStartUtc, "the same minute, kept apart by instance id");
		}

		[Test]
		public void AcknowledgedMinutes_AreForgotten_UnlessTheyGrewSince()
		{
			var ledger = new ServerBandwidthLedger(Snap(0, 0, 0, 0));
			ledger.Record(Snap(60, 100, 200, 1), At(1, 0));
			ledger.Record(Snap(120, 200, 400, 2), At(2, 0));
			var batch = ledger.Pending();
			ledger.Record(Snap(150, 300, 600, 3), At(2, 30));

			ledger.Acknowledge(batch);
			var left = ledger.Pending();
			LogAssert.AreEqual(1, left.Count, "the unchanged minute is forgotten");
			LogAssert.AreEqual(At(2, 0), left[0].BucketStartUtc, "the minute that grew stays");
			LogAssert.AreEqual(400L, left[0].UdpSentBytes, "with its newer running total");
		}

		[Test]
		public void AFailedWrite_KeepsTheMinutes_UpToTheBound_ThenDropsTheOldestVisibly()
		{
			var ledger = new ServerBandwidthLedger(Snap(0, 0, 0, 0), maxPending: 3);
			for (int i = 1; i <= 5; i++)
			{
				ledger.Record(Snap(i * 60, i * 10, i * 100, i), At(i, 0));
			}
			var held = ledger.Pending();
			LogAssert.AreEqual(3, held.Count, "at most the bound");
			LogAssert.AreEqual(At(3, 0), held[0].BucketStartUtc, "the oldest went first");
			LogAssert.AreEqual(2L, ledger.DroppedMinutes, "and every drop is counted for the log");
		}

		[Test]
		public void ARowTheDatabaseWouldRefuse_IsRefusedBeforeIt()
		{
			var good = new ServerBandwidthSample { BucketStartUtc = Noon, IntervalMs = 60_000 };
			LogAssert.IsNull(FishMMO.Database.Npgsql.Services.ServerBandwidthService.Validate(ServerBandwidthKind.World, "w1", Guid.NewGuid(), new[] { good }), "a good row");
			LogAssert.IsNotNull(FishMMO.Database.Npgsql.Services.ServerBandwidthService.Validate(ServerBandwidthKind.World, "w1", Guid.NewGuid(), new[] { good, good }), "the same minute twice");
			LogAssert.IsNotNull(FishMMO.Database.Npgsql.Services.ServerBandwidthService.Validate(4, "w1", Guid.NewGuid(), new[] { good }), "not a tier");
			LogAssert.IsNotNull(FishMMO.Database.Npgsql.Services.ServerBandwidthService.Validate(ServerBandwidthKind.World, " ", Guid.NewGuid(), new[] { good }), "no name");
			LogAssert.IsNotNull(FishMMO.Database.Npgsql.Services.ServerBandwidthService.Validate(ServerBandwidthKind.World, "w1", Guid.Empty, new[] { good }), "no instance id");
			var unknown = good;
			unknown.UdpSentBytes = -1;
			LogAssert.IsNotNull(unknown.Invalid(), "an unknown (-1) counter is never stored as a number");
			var partial = good;
			partial.BucketStartUtc = Noon.AddSeconds(5);
			LogAssert.IsNotNull(partial.Invalid(), "a bucket that is not a whole minute");
		}
	}
}
