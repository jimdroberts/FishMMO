using System;
using System.Collections.Generic;
using FishMMO.Database.Data;
using FishNet.Transporting.WebTransport;

namespace FishMMO.Server.Core
{
	/// <summary>What <see cref="ServerBandwidthLedger.Record"/> did with one snapshot.</summary>
	public enum ServerBandwidthSampleOutcome
	{
		/// <summary>The interval was filed under its minute and that minute is pending a write.</summary>
		Recorded,

		/// <summary>
		/// Nothing changed: the snapshot could not be differenced yet (its QUIC layer is not
		/// measured, or no time has passed). The counters are cumulative, so the next snapshot
		/// covers this interval too — nothing is lost.
		/// </summary>
		Deferred,

		/// <summary>
		/// The snapshot could not be differenced against the last one at all (the totals went
		/// backwards, or a layer was measured at one end only). The ledger restarted from it; the
		/// traffic since the last recorded snapshot is unknown and is not written. Log it.
		/// </summary>
		Rebaselined,
	}

	/// <summary>
	/// Turns a server's cumulative transport counters into the minute rows it writes to
	/// <c>server_bandwidth_minute</c>. Pure bookkeeping, so every rule is testable without a
	/// network manager or a database.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A minute row holds its minute's running total, and is written as a SET.</b> Each
	/// snapshot is filed under the database-clock minute it was taken in. The first snapshot in a
	/// minute opens it with the traffic since the previous snapshot; a second snapshot in the same
	/// minute (a late frame, a clock step) rewrites the row with the minute's running total, which
	/// contains the first. Re-sending any row is therefore harmless — the database takes the newer
	/// total or the same one — which is what lets a failed write simply be retried next minute
	/// instead of lost, and a retried write after a lost commit reply never be counted twice.
	/// </para>
	/// <para>
	/// <b>Every byte lands in exactly one minute.</b> The interval between two consecutive
	/// snapshots belongs to the minute of the later one. A minute never reopens: a snapshot whose
	/// database minute is earlier than the open one (the database clock stepped back) is filed
	/// under the open one.
	/// </para>
	/// <para>
	/// <b>Bounded.</b> At most <see cref="ServerBandwidthMath.MaxPendingMinutes"/> minutes wait for
	/// a write; beyond that the oldest are dropped and counted in <see cref="DroppedMinutes"/>, so a
	/// long outage costs memory for an hour and then costs visible, logged minutes rather than
	/// growing without limit.
	/// </para>
	/// <para>
	/// Not thread-safe. The recorder touches it from one piece of work at a time (its
	/// single-flight gate), which is also what keeps two writes of one minute from racing.
	/// </para>
	/// </remarks>
	public sealed class ServerBandwidthLedger
	{
		private readonly int maxPending;
		private readonly SortedList<DateTime, ServerBandwidthSample> pending = new SortedList<DateTime, ServerBandwidthSample>();

		/// <summary>The latest snapshot already filed into a minute.</summary>
		private TransportTrafficSnapshot last;

		/// <summary>The counters when the open minute began: the last snapshot of the minute before it.</summary>
		private TransportTrafficSnapshot openBase;

		/// <summary>The minute later snapshots add to, until one arrives in a later minute.</summary>
		private DateTime? openMinute;

		/// <summary>The highest session gauge sampled in the open minute.</summary>
		private long openSessionsMax;

		/// <summary>
		/// After a rebaseline, the last minute that already holds traffic measured against the old
		/// baseline; nothing measured against the new one may be filed at or before it.
		/// </summary>
		private DateTime? closedThrough;

		/// <summary>Minutes dropped unwritten because <see cref="ServerBandwidthMath.MaxPendingMinutes"/> were already waiting.</summary>
		public long DroppedMinutes { get; private set; }

		/// <summary>How many minutes are waiting for a write.</summary>
		public int PendingCount => pending.Count;

		/// <summary>The minute later snapshots add to, or null before the first recorded snapshot and after a rebaseline.</summary>
		public DateTime? OpenMinute => openMinute;

		/// <summary>Starts a ledger from the counters as they are when the process begins recording.</summary>
		/// <param name="start">A capture taken when recording starts; see <see cref="StartingPoint"/>.</param>
		/// <param name="maxPending">Most minutes held for a write.</param>
		public ServerBandwidthLedger(in TransportTrafficSnapshot start, int maxPending = ServerBandwidthMath.MaxPendingMinutes)
		{
			this.maxPending = Math.Max(1, maxPending);
			last = StartingPoint(in start);
			openBase = last;
		}

		/// <summary>
		/// The baseline a ledger starts from: <paramref name="start"/>, except that a native QUIC
		/// layer that is not available yet counts as zero.
		/// </summary>
		/// <remarks>
		/// A server starts recording before its transport opens, when msquic has not been loaded in
		/// this process, and the transport reports that layer as Unavailable. Unavailable there is
		/// not "unknown": msquic's process-lifetime totals are counted from the library's first
		/// initialisation, so before it they are exactly zero. Starting from zero keeps the first
		/// minute — the one with every client's handshake in it — instead of losing it to a
		/// layer that was "unavailable" at one end of the interval. (The other way a native read is
		/// Unavailable, a failed read, is unreachable after the transport's ABI check, and a
		/// browser backend is never a server.)
		/// </remarks>
		public static TransportTrafficSnapshot StartingPoint(in TransportTrafficSnapshot start)
		{
			TransportTrafficSnapshot baseline = start;
			if (baseline.Backend == TransportTrafficBackend.Native && baseline.QuicMeasure == TrafficMeasure.Unavailable)
			{
				baseline.QuicMeasure = TrafficMeasure.Measured;
				baseline.QuicSentBytes = 0;
				baseline.QuicRecvBytes = 0;
				baseline.DatagramMeasure = TrafficMeasure.Measured;
				baseline.UdpSentDatagrams = 0;
				baseline.UdpRecvDatagrams = 0;
				baseline.Process = new TransportProcessCounters { IsValid = true };
			}
			return baseline;
		}

		/// <summary>
		/// Files the traffic up to <paramref name="now"/> under its minute.
		/// </summary>
		/// <param name="now">A capture of the transport's counters.</param>
		/// <param name="databaseUtcAtSnapshot">The database clock at the moment of <paramref name="now"/>.</param>
		public ServerBandwidthSampleOutcome Record(in TransportTrafficSnapshot now, DateTime databaseUtcAtSnapshot)
		{
			DateTime minute = ServerBandwidthMath.FloorToMinute(DateTime.SpecifyKind(databaseUtcAtSnapshot, DateTimeKind.Utc));
			if (closedThrough.HasValue && minute <= closedThrough.Value)
			{
				/* That minute's row is a running total against the baseline before the rebaseline.
				 * Adding traffic measured against a different baseline to it is impossible without
				 * an ADD, so the next minute takes it: at most a minute late, never lost, never
				 * counted twice. */
				minute = closedThrough.Value.AddMinutes(1);
			}
			bool opensMinute = !openMinute.HasValue || minute > openMinute.Value;
			if (!opensMinute)
			{
				// The same minute, or the database clock stepped back: never reopen a closed minute.
				minute = openMinute.Value;
			}

			/* Every snapshot is first differenced against the LAST one, whatever minute it lands in.
			 * Differencing a same-minute snapshot only against the minute's base would let counters
			 * that went backwards inside the minute (a domain reload) pass as a smaller, positive
			 * total and SET the minute to it. */
			if (!TransportTrafficMath.TryComputeRates(in last, in now, out TransportTrafficRates step))
			{
				if (!(now.TimestampSeconds > last.TimestampSeconds))
				{
					return ServerBandwidthSampleOutcome.Deferred;
				}
				Rebaseline(in now);
				return ServerBandwidthSampleOutcome.Rebaselined;
			}

			if (!IsComplete(in step))
			{
				/* The later end has no measured QUIC layer yet (the transport has not loaded msquic):
				 * wait for it, the counters keep accumulating. Anything else means the two ends
				 * were measured differently, which no amount of waiting fixes. */
				if (now.QuicMeasure != TrafficMeasure.Measured || now.DatagramMeasure != TrafficMeasure.Measured || !now.Process.IsValid)
				{
					return ServerBandwidthSampleOutcome.Deferred;
				}
				Rebaseline(in now);
				return ServerBandwidthSampleOutcome.Rebaselined;
			}

			/* The row is the minute's running total: from the minute's base when the minute is
			 * already open, which is monotone through `last` to `now` and so always differences. */
			TransportTrafficRates delta = step;
			if (!opensMinute &&
				(!TransportTrafficMath.TryComputeRates(in openBase, in now, out delta) || !IsComplete(in delta)))
			{
				Rebaseline(in now);
				return ServerBandwidthSampleOutcome.Rebaselined;
			}

			if (opensMinute)
			{
				openBase = last;
				openMinute = minute;
				openSessionsMax = 0;
			}
			last = now;
			openSessionsMax = Math.Max(openSessionsMax, Math.Max(0, now.SessionsActive));

			pending[minute] = new ServerBandwidthSample
			{
				BucketStartUtc = minute,
				IntervalMs = Math.Max(1L, (long)Math.Round(delta.Seconds * 1000.0)),
				AppSentBytes = delta.AppSentBytes,
				AppRecvBytes = delta.AppRecvBytes,
				UdpSentBytes = delta.QuicSentBytes,
				UdpRecvBytes = delta.QuicRecvBytes,
				UdpSentDatagrams = delta.UdpSentDatagrams,
				UdpRecvDatagrams = delta.UdpRecvDatagrams,
				SessionsActive = openSessionsMax,
				SessionsOpened = delta.SessionsOpened,
				ConnectionsRefused = delta.ConnectionsRefused,
				HandshakeFailures = delta.HandshakeFailures,
			};

			while (pending.Count > maxPending)
			{
				pending.RemoveAt(0);
				DroppedMinutes++;
			}
			return ServerBandwidthSampleOutcome.Recorded;
		}

		/// <summary>The minutes waiting for a write, oldest first, copied so the caller may hold them across an await.</summary>
		public List<ServerBandwidthSample> Pending()
		{
			return new List<ServerBandwidthSample>(pending.Values);
		}

		/// <summary>
		/// Forgets the minutes a write stored. A minute that has been re-recorded since the batch
		/// was taken (its running total grew) stays pending, so its newer total is written next.
		/// </summary>
		public void Acknowledge(IReadOnlyList<ServerBandwidthSample> written)
		{
			if (written == null)
			{
				return;
			}
			for (int i = 0; i < written.Count; i++)
			{
				if (pending.TryGetValue(written[i].BucketStartUtc, out ServerBandwidthSample held) &&
					held.IntervalMs == written[i].IntervalMs)
				{
					pending.Remove(written[i].BucketStartUtc);
				}
			}
		}

		/// <summary>Whether a delta measured every column a minute row stores.</summary>
		public static bool IsComplete(in TransportTrafficRates delta)
		{
			return delta.QuicMeasure == TrafficMeasure.Measured &&
				delta.DatagramMeasure == TrafficMeasure.Measured &&
				delta.QuicSentBytes >= 0 && delta.QuicRecvBytes >= 0 &&
				delta.UdpSentDatagrams >= 0 && delta.UdpRecvDatagrams >= 0 &&
				delta.AppSentBytes >= 0 && delta.AppRecvBytes >= 0 &&
				delta.ConnectionsRefused >= 0 && delta.HandshakeFailures >= 0;
		}

		/// <summary>
		/// Restarts from <paramref name="now"/>. Minutes already pending are kept: they were
		/// measured correctly and are still owed a write. The open minute is closed, because its
		/// row is a running total from the old baseline.
		/// </summary>
		private void Rebaseline(in TransportTrafficSnapshot now)
		{
			if (openMinute.HasValue)
			{
				closedThrough = openMinute;
			}
			last = now;
			openBase = now;
			openMinute = null;
			openSessionsMax = 0;
		}
	}
}
