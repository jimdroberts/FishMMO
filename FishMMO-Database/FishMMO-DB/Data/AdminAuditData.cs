using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One recorded operator action.
	/// </summary>
	/// <remarks>
	/// The write shape and the read shape are the same type on purpose. An audit row is written
	/// once and read back verbatim; a separate "create" model would only be an opportunity for
	/// the two to drift, and a field that reads back differently from how it was written is
	/// exactly the defect this table must not have.
	/// </remarks>
	public sealed class AdminAuditData
	{
		/// <summary>Surrogate key. Zero on a row that has not been written yet.</summary>
		public long ID { get; set; }

		/// <summary>When it happened, UTC. Set by the service if left at default.</summary>
		public DateTime OccurredUtc { get; set; }

		/// <summary>The account that acted.</summary>
		public string ActorName { get; set; }

		/// <summary>The actor's access level at the time, not now.</summary>
		public byte ActorAccessLevel { get; set; }

		/// <summary>The panel session it came from, or null for an in-game action.</summary>
		public long? ActorSessionID { get; set; }

		/// <summary>Stable dotted identifier, such as <c>character.rename</c>.</summary>
		public string Action { get; set; }

		/// <summary>What kind of thing was acted on.</summary>
		public string TargetType { get; set; }

		/// <summary>The target's identifier as text.</summary>
		public string TargetID { get; set; }

		/// <summary>The target's name as it read at the time.</summary>
		public string TargetName { get; set; }

		/// <summary>The reason the operator gave.</summary>
		public string Reason { get; set; }

		/// <summary>Whether the action was carried out.</summary>
		public bool Succeeded { get; set; }

		/// <summary>Why it was not, when it was not.</summary>
		public string Outcome { get; set; }

		/// <summary>Compact JSON describing what changed, or null.</summary>
		public string Details { get; set; }

		/// <summary>Where the request came from.</summary>
		public string IpAddress { get; set; }

		/// <summary>Where the action was taken from: the panel, or in-game chat.</summary>
		public string Source { get; set; }
	}

	/// <summary>Filters for an audit-log search. Every field is optional.</summary>
	public sealed class AdminAuditQuery
	{
		/// <summary>Only actions by this account.</summary>
		public string ActorName { get; set; }

		/// <summary>Only actions on this kind of target.</summary>
		public string TargetType { get; set; }

		/// <summary>Only actions on this target.</summary>
		public string TargetID { get; set; }

		/// <summary>Only this action.</summary>
		public string Action { get; set; }

		/// <summary>Only successes, only refusals, or both when null.</summary>
		public bool? Succeeded { get; set; }

		/// <summary>Inclusive lower bound on the timestamp.</summary>
		public DateTime? FromUtc { get; set; }

		/// <summary>Exclusive upper bound on the timestamp.</summary>
		public DateTime? ToUtc { get; set; }

		/// <summary>1-based page number.</summary>
		public int Page { get; set; } = 1;

		/// <summary>Rows per page. Clamped by the service.</summary>
		public int PageSize { get; set; } = 50;
	}

	/// <summary>One page of audit rows, newest first.</summary>
	public sealed class AdminAuditPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<AdminAuditData> Items { get; set; } = Array.Empty<AdminAuditData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter.</summary>
		public int TotalCount { get; set; }
	}
}
