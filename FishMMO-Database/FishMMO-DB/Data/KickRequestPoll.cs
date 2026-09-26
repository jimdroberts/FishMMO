using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One poll of the kick requests: where it reads from, and what the reader has already handled.
	/// </summary>
	/// <remarks>
	/// Built by <see cref="KickRequestReadWindow.BuildQuery"/>. Every instant in it is on the
	/// DATABASE clock, the one the rows are stamped with; see <see cref="KickRequestReadWindow"/>.
	/// </remarks>
	public sealed class KickRequestPollQuery
	{
		/// <summary>
		/// The oldest stamp to read, on the database clock, or null for the reader's first read.
		/// </summary>
		public DateTime? FromUtc { get; set; }

		/// <summary>
		/// For a first read only (<see cref="FromUtc"/> null): how many seconds ago, by the reader's
		/// own monotonic clock, it began watching. The read starts that long before the database's
		/// "now", so a kick issued between the reader starting and its first successful read is not
		/// skipped. A duration taken from the reader's clock and subtracted from the database's
		/// "now" is an instant on the database's clock; no two host clocks are ever compared.
		/// </summary>
		public double FirstReadLookbackSeconds { get; set; }

		/// <summary>
		/// IDs of the requests the reader has already handled that this read could return again,
		/// paired by index with <see cref="HandledStamps"/>.
		/// </summary>
		public long[] HandledIds { get; set; } = Array.Empty<long>();

		/// <summary>
		/// The stamp each of <see cref="HandledIds"/> was handled at. A request row is upserted per
		/// account, so a second kick of the same account keeps its ID and moves its stamp: the pair
		/// is what identifies one kick, and a re-stamped row is read again.
		/// </summary>
		public DateTime[] HandledStamps { get; set; } = Array.Empty<DateTime>();

		/// <summary>Most requests to return.</summary>
		public int PageSize { get; set; } = 100;
	}

	/// <summary>
	/// What one poll of the kick requests returned.
	/// </summary>
	public sealed class KickRequestPage
	{
		/// <summary>The requests, in <c>(time_created, id)</c> order, each with its account's last login.</summary>
		public List<KickRequestData> Requests { get; } = new List<KickRequestData>();

		/// <summary>The database clock taken before the page was read.</summary>
		public DateTime ReadStartedUtc { get; set; }

		/// <summary>The stamp the read started from, on the database clock.</summary>
		public DateTime ReadFromUtc { get; set; }

		/// <summary>True when the page came back short: everything after <see cref="ReadFromUtc"/> was read.</summary>
		public bool Drained { get; set; }
	}
}
