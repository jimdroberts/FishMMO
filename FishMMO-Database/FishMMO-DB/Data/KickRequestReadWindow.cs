using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Where a game server's poll of the kick requests has got to, and which requests it has already
	/// handled, on the database clock. Pure bookkeeping: no database, no network, one poll at a time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the database clock.</b> A request is stamped by the database when it is written.
	/// The poller's cursor used to be seeded from its own host clock and compared with those stamps:
	/// a host running ahead of the database skipped every kick stamped inside the lead, for good.
	/// Every instant here comes from the database, through <see cref="KickRequestPage"/>, and the one
	/// local measurement — how long ago the reader started — only ever reaches the database as a
	/// duration (<see cref="KickRequestPollQuery.FirstReadLookbackSeconds"/>).
	/// </para>
	/// <para>
	/// <b>Why a window and not a cursor.</b> A request becomes visible when its transaction commits,
	/// some time after its stamp: a ban writes its kick inside a longer transaction, stamped at that
	/// transaction's start. Two writers can therefore commit in the opposite order to their stamps,
	/// and a strict <c>(time, id)</c> cursor that has moved past a stamp before its row committed
	/// never sees the row. So the poll reads from <see cref="Watermark"/>, held
	/// <see cref="CommitWindow"/> behind what it has settled, and skips what it has already handled
	/// (<see cref="SnapshotHandled"/>), as the chat pump does (<see cref="ChatReadWindow"/>). A kick
	/// is acted on exactly once as long as it commits within the window of its stamp.
	/// </para>
	/// <para>
	/// <b>Why handled is (id, stamp).</b> Requests are upserted per account: kicking an account that
	/// already has a row keeps the row's ID and moves its stamp. Keyed on the ID alone, that second
	/// kick would be skipped as already handled.
	/// </para>
	/// <para>
	/// <b>Why a floor.</b> Nothing before the reader's first read is re-read by the window's reach
	/// back; the first read itself starts at the moment the reader began watching.
	/// </para>
	/// </remarks>
	public sealed class KickRequestReadWindow
	{
		private readonly Dictionary<long, DateTime> handled = new Dictionary<long, DateTime>();
		private readonly List<long> idScratch = new List<long>();
		private DateTime? floorUtc;

		/// <summary>How long after its stamp a request may commit and still be read.</summary>
		public TimeSpan CommitWindow { get; }

		/// <summary>
		/// The oldest stamp the next read starts from, or null before the first read. Only ever
		/// moves forward.
		/// </summary>
		public DateTime? Watermark { get; private set; }

		/// <summary>Requests handled that the next read could still return.</summary>
		public int HandledCount => handled.Count;

		/// <summary>Creates a window with the given commit window.</summary>
		public KickRequestReadWindow(TimeSpan commitWindow)
		{
			CommitWindow = commitWindow > TimeSpan.Zero ? commitWindow : TimeSpan.Zero;
		}

		/// <summary>
		/// The query for the next read.
		/// </summary>
		/// <param name="pageSize">Most requests to return.</param>
		/// <param name="secondsSinceReaderStarted">
		/// How long ago the reader began watching, by its own monotonic clock. Used only by a first
		/// read; see <see cref="KickRequestPollQuery.FirstReadLookbackSeconds"/>.
		/// </param>
		public KickRequestPollQuery BuildQuery(int pageSize, double secondsSinceReaderStarted)
		{
			SnapshotHandled(out long[] ids, out DateTime[] stamps);
			return new KickRequestPollQuery
			{
				FromUtc = Watermark,
				FirstReadLookbackSeconds = Watermark.HasValue || !(secondsSinceReaderStarted > 0.0) ? 0.0 : secondsSinceReaderStarted,
				HandledIds = ids,
				HandledStamps = stamps,
				PageSize = pageSize,
			};
		}

		/// <summary>
		/// The requests handled that the next read could return again, for the read to skip.
		/// </summary>
		public void SnapshotHandled(out long[] ids, out DateTime[] stamps)
		{
			if (handled.Count == 0)
			{
				ids = Array.Empty<long>();
				stamps = Array.Empty<DateTime>();
				return;
			}
			ids = new long[handled.Count];
			stamps = new DateTime[handled.Count];
			int i = 0;
			foreach (KeyValuePair<long, DateTime> pair in handled)
			{
				ids[i] = pair.Key;
				stamps[i] = pair.Value;
				++i;
			}
		}

		/// <summary>Whether this request, at this stamp, has already been handled.</summary>
		public bool IsHandled(long id, DateTime timeCreatedUtc)
		{
			return handled.TryGetValue(id, out DateTime stamp) && stamp == timeCreatedUtc;
		}

		/// <summary>
		/// Records one request as handled: its kick is on its way, or it was found stale. Only a
		/// request that was actually settled is recorded, so one that could not be handed on is read
		/// again.
		/// </summary>
		/// <returns>True the first time; false for a request already handled at this stamp.</returns>
		public bool MarkHandled(long id, DateTime timeCreatedUtc)
		{
			if (IsHandled(id, timeCreatedUtc))
			{
				return false;
			}
			/* A re-stamped row replaces its earlier stamp: the row no longer carries that one, so no
			 * read can return it again. */
			handled[id] = timeCreatedUtc;
			return true;
		}

		/// <summary>
		/// Where a read left requests unsettled, as the stamp to read from again, or null when it
		/// settled everything up to the moment it started.
		/// </summary>
		/// <param name="drained">Whether the page came back short.</param>
		/// <param name="lastRowUtc">The stamp of the page's last request, or null for an empty page.</param>
		/// <param name="firstUnsettledUtc">
		/// The stamp of the first request the read could not hand on, or null when it handed on
		/// every one it was given.
		/// </param>
		/// <remarks>
		/// A request that could not be handed on, and everything after it, is still owed; a full page
		/// says nothing about the requests after its last row. Only a short page with nothing refused
		/// has read everything that had committed when the read started.
		/// </remarks>
		public static DateTime? UnsettledFrom(bool drained, DateTime? lastRowUtc, DateTime? firstUnsettledUtc)
		{
			if (firstUnsettledUtc.HasValue)
			{
				return firstUnsettledUtc;
			}
			return drained ? null : lastRowUtc;
		}

		/// <summary>
		/// Settles a completed read: moves the watermark and forgets requests it has passed. Called
		/// after every read that returned, including one that returned nothing, or an idle reader's
		/// window never advances. Not called after a read that failed, so it is read again.
		/// </summary>
		/// <param name="readFromUtc">The stamp the read started from (<see cref="KickRequestPage.ReadFromUtc"/>).</param>
		/// <param name="readStartedUtc">The database clock taken before the read (<see cref="KickRequestPage.ReadStartedUtc"/>).</param>
		/// <param name="unsettledFromUtc">See <see cref="UnsettledFrom"/>.</param>
		public void CompleteRead(DateTime readFromUtc, DateTime readStartedUtc, DateTime? unsettledFromUtc)
		{
			if (!floorUtc.HasValue)
			{
				floorUtc = readFromUtc;
			}

			DateTime settled = readStartedUtc;
			if (unsettledFromUtc.HasValue && unsettledFromUtc.Value < settled)
			{
				settled = unsettledFromUtc.Value;
			}

			DateTime candidate = settled - CommitWindow;
			if (candidate < floorUtc.Value)
			{
				candidate = floorUtc.Value;
			}
			if (!Watermark.HasValue || candidate > Watermark.Value)
			{
				Watermark = candidate;
			}

			// Requests stamped behind the watermark can never be returned again.
			idScratch.Clear();
			foreach (KeyValuePair<long, DateTime> pair in handled)
			{
				if (pair.Value < Watermark.Value)
				{
					idScratch.Add(pair.Key);
				}
			}
			for (int i = 0; i < idScratch.Count; i++)
			{
				handled.Remove(idScratch[i]);
			}
			idScratch.Clear();
		}

		/// <summary>Forgets everything, for a reader that is starting again.</summary>
		public void Reset()
		{
			handled.Clear();
			floorUtc = null;
			Watermark = null;
		}
	}
}
