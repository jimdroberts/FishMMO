using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Data
{
	/// <summary>A support ticket, as staff and players read it.</summary>
	public sealed class SupportTicketData
	{
		/// <summary>Surrogate key, and the reference a player quotes.</summary>
		public long ID { get; set; }

		/// <summary>When it was filed.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>When anything last happened on it.</summary>
		public DateTime LastActivityUtc { get; set; }

		/// <summary>The account that filed it.</summary>
		public string ReporterAccount { get; set; }

		/// <summary>The character they were playing.</summary>
		public string ReporterCharacterName { get; set; }

		/// <summary>That character's id.</summary>
		public long ReporterCharacterID { get; set; }

		/// <summary>What it is about.</summary>
		public SupportTicketCategory Category { get; set; }

		/// <summary>Where it has got to.</summary>
		public SupportTicketStatus Status { get; set; }

		/// <summary>Staff-set priority, higher is more urgent.</summary>
		public int Priority { get; set; }

		/// <summary>One-line summary.</summary>
		public string Subject { get; set; }

		/// <summary>What the player wrote when they filed it.</summary>
		public string Body { get; set; }

		/// <summary>The accused account, for a player report.</summary>
		public string TargetAccount { get; set; }

		/// <summary>The accused character.</summary>
		public string TargetCharacterName { get; set; }

		/// <summary>The accused character's id.</summary>
		public long TargetCharacterID { get; set; }

		/// <summary>Where it happened.</summary>
		public string SceneName { get; set; }

		/// <summary>The staff account working it.</summary>
		public string AssignedTo { get; set; }

		/// <summary>What staff decided.</summary>
		public string Resolution { get; set; }

		/// <summary>When it was finished.</summary>
		public DateTime? ClosedUtc { get; set; }

		/// <summary>The staff account that finished it.</summary>
		public string ClosedBy { get; set; }

		/// <summary>
		/// The conversation. Empty on a list result; filled on a fetch.
		/// </summary>
		/// <remarks>
		/// Whether internal notes appear here is decided by the fetch, not by the caller
		/// filtering afterwards. See <c>ISupportTicketService.FetchAsync</c>.
		/// </remarks>
		public IReadOnlyList<SupportTicketMessageData> Messages { get; set; } = Array.Empty<SupportTicketMessageData>();

		/// <summary>How many messages the ticket has, including any the reader cannot see.</summary>
		public int MessageCount { get; set; }
	}

	/// <summary>One message on a ticket.</summary>
	public sealed class SupportTicketMessageData
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>The ticket it belongs to.</summary>
		public long TicketID { get; set; }

		/// <summary>When it was written.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>The account that wrote it.</summary>
		public string AuthorAccount { get; set; }

		/// <summary>Whether the author was acting as staff at the time.</summary>
		public bool AuthorIsStaff { get; set; }

		/// <summary>Whether this is a staff-only note.</summary>
		public bool Internal { get; set; }

		/// <summary>The text.</summary>
		public string Body { get; set; }
	}

	/// <summary>Filters for a ticket search. Every field is optional.</summary>
	public sealed class SupportTicketQuery
	{
		/// <summary>Only tickets in these states. Empty means any.</summary>
		public IReadOnlyList<SupportTicketStatus> Statuses { get; set; }

		/// <summary>Only this category.</summary>
		public SupportTicketCategory? Category { get; set; }

		/// <summary>Only tickets assigned to this staff account.</summary>
		public string AssignedTo { get; set; }

		/// <summary>Only tickets with nobody assigned. Overrides <see cref="AssignedTo"/>.</summary>
		public bool UnassignedOnly { get; set; }

		/// <summary>Only tickets filed by this account.</summary>
		public string ReporterAccount { get; set; }

		/// <summary>Only tickets naming this account as the target.</summary>
		public string TargetAccount { get; set; }

		/// <summary>Substring of the subject.</summary>
		public string Subject { get; set; }

		/// <summary>1-based page number.</summary>
		public int Page { get; set; } = 1;

		/// <summary>Rows per page. Clamped by the service.</summary>
		public int PageSize { get; set; } = 25;
	}

	/// <summary>One page of tickets.</summary>
	public sealed class SupportTicketPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<SupportTicketData> Items { get; set; } = Array.Empty<SupportTicketData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter.</summary>
		public int TotalCount { get; set; }
	}

	/// <summary>A new ticket, as filed.</summary>
	/// <remarks>
	/// Carries no status, priority or assignee: those are staff's, and a create shape that
	/// accepted them would be a path for a player to file something already marked urgent.
	/// </remarks>
	public sealed class SupportTicketCreate
	{
		/// <summary>The account filing it.</summary>
		public string ReporterAccount { get; set; }

		/// <summary>The character they were playing.</summary>
		public string ReporterCharacterName { get; set; }

		/// <summary>That character's id.</summary>
		public long ReporterCharacterID { get; set; }

		/// <summary>What it is about.</summary>
		public SupportTicketCategory Category { get; set; }

		/// <summary>One-line summary.</summary>
		public string Subject { get; set; }

		/// <summary>The description.</summary>
		public string Body { get; set; }

		/// <summary>The accused account, for a player report.</summary>
		public string TargetAccount { get; set; }

		/// <summary>The accused character.</summary>
		public string TargetCharacterName { get; set; }

		/// <summary>The accused character's id.</summary>
		public long TargetCharacterID { get; set; }

		/// <summary>Where it happened.</summary>
		public string SceneName { get; set; }
	}
}
