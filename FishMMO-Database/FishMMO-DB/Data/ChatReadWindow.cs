using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Where a reader of the chat table has got to, and which rows it has already handled, on the
	/// database clock. Pure bookkeeping: no database, no network, one thread at a time.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a window and not a cursor.</b> A chat row is stamped by the database clock when its
	/// INSERT runs, and becomes visible only when its transaction commits. Two writers can commit
	/// in the opposite order to their stamps — and to their IDs, which are also taken at the
	/// INSERT. A reader that pages by "everything after the last row I saw", by ID or by
	/// <c>(time, id)</c>, moves past a row that has not committed yet and never sees it (hot-path
	/// audit H5). So the reader reads from <see cref="Watermark"/>, held
	/// <see cref="CommitWindow"/> behind what it has settled, and skips by ID what it has already
	/// handled (<see cref="SnapshotSeenIds"/> for the query, <see cref="Admit"/> for the rows it
	/// returns). A row is read exactly once as long as it commits within the window of its stamp;
	/// <c>ChatService.PumpCommitWindowSeconds</c> is that window, shared with the writers.
	/// </para>
	/// <para>
	/// <b>Why a floor.</b> Nothing stamped before the reader's first read is ever read. The first
	/// read starts at the database's own "now" and later reads reach back a window from their
	/// start, so without the floor the second read would replay the window before the reader
	/// started: a restarted reader would republish what it had already published.
	/// </para>
	/// <para>
	/// The scene servers' pump (<c>ChatPumpCursor</c>, in the game's server code) keeps the same
	/// window and adds per-recipient relevance on top; this is the window alone, for a reader that
	/// takes every row, such as the Discord relay.
	/// </para>
	/// </remarks>
	public sealed class ChatReadWindow
	{
		private readonly Dictionary<long, DateTime> seen = new Dictionary<long, DateTime>();
		private readonly List<long> idScratch = new List<long>();
		private DateTime? floorUtc;

		/// <summary>
		/// How long after its stamp a row may commit and still be read.
		/// </summary>
		public TimeSpan CommitWindow { get; }

		/// <summary>
		/// The oldest stamp the next read starts from, or null before the first read, which starts
		/// at the database's own "now". Only ever moves forward.
		/// </summary>
		public DateTime? Watermark { get; private set; }

		/// <summary>
		/// The database clock at the start of the last completed read, or null before the first.
		/// </summary>
		public DateTime? LastReadStartedUtc { get; private set; }

		/// <summary>
		/// Rows handled that the next read could still return.
		/// </summary>
		public int SeenCount => seen.Count;

		/// <summary>
		/// Creates a window with the given commit window.
		/// </summary>
		public ChatReadWindow(TimeSpan commitWindow)
		{
			CommitWindow = commitWindow > TimeSpan.Zero ? commitWindow : TimeSpan.Zero;
		}

		/// <summary>
		/// The IDs of the rows handled that the next read could return again, for the read to skip.
		/// </summary>
		public long[] SnapshotSeenIds()
		{
			if (seen.Count == 0)
			{
				return Array.Empty<long>();
			}
			var ids = new long[seen.Count];
			seen.Keys.CopyTo(ids, 0);
			return ids;
		}

		/// <summary>
		/// Records one row the read returned as handled.
		/// </summary>
		/// <param name="id">The row's ID.</param>
		/// <param name="timeCreatedUtc">The row's database-clock stamp.</param>
		/// <returns>True the first time a row is seen; false for a row already handled.</returns>
		public bool Admit(long id, DateTime timeCreatedUtc)
		{
			if (seen.ContainsKey(id))
			{
				return false;
			}
			seen[id] = timeCreatedUtc;
			return true;
		}

		/// <summary>
		/// Settles a completed read: moves the watermark and forgets rows it has passed. Called after
		/// every read that succeeded, including one that returned nothing, or an idle reader's window
		/// never advances. Not called after a read that failed, so its rows are read again.
		/// </summary>
		/// <param name="readStartedUtc">The database clock taken before the read's first page.</param>
		/// <param name="drained">True if the read caught up; false if it stopped at its page limit.</param>
		/// <param name="lastRowTimeUtc">The stamp of the last row read, or null if none were.</param>
		public void CompleteRead(DateTime readStartedUtc, bool drained, DateTime? lastRowTimeUtc)
		{
			if (!floorUtc.HasValue)
			{
				floorUtc = readStartedUtc;
			}

			/* Settled up to where the read got: everything committed before the read started when it
			 * drained, only up to its last row when it stopped at its page limit. */
			DateTime settled = readStartedUtc;
			if (!drained && lastRowTimeUtc.HasValue && lastRowTimeUtc.Value < settled)
			{
				settled = lastRowTimeUtc.Value;
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

			LastReadStartedUtc = readStartedUtc;

			// Rows behind the watermark can never be returned again.
			idScratch.Clear();
			foreach (KeyValuePair<long, DateTime> pair in seen)
			{
				if (pair.Value < Watermark.Value)
				{
					idScratch.Add(pair.Key);
				}
			}
			for (int i = 0; i < idScratch.Count; i++)
			{
				seen.Remove(idScratch[i]);
			}
			idScratch.Clear();
		}

		/// <summary>
		/// Forgets everything, for a reader that is starting again from "now".
		/// </summary>
		public void Reset()
		{
			seen.Clear();
			floorUtc = null;
			Watermark = null;
			LastReadStartedUtc = null;
		}
	}
}
