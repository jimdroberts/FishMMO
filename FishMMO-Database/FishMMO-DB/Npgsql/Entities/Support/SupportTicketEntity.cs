using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A support ticket: something a player asked staff to deal with.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Filed from in-game and worked from the Control Panel. The player can follow it and reply;
	/// staff can reply, take notes the player never sees, and act.
	/// </para>
	/// <para>
	/// <b>No foreign keys on any of the account or character names here.</b> The same reasoning
	/// as the audit log: a ticket describes something that happened, and deleting an account
	/// must not erase the report somebody filed about it — which is exactly the account most
	/// likely to be deleted. The names are copied in, and the character id is kept beside the
	/// name so the panel can still link to a character that has since been renamed.
	/// </para>
	/// </remarks>
	public class SupportTicketEntity
	{
		/// <summary>Surrogate key, and the reference a player quotes.</summary>
		public long ID { get; set; }

		/// <summary>Concurrency token. Two staff working one ticket is the normal case.</summary>
		public uint Version { get; set; }

		/// <summary>When it was filed.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>
		/// When anything last happened on it: a reply, a note, a status change.
		/// </summary>
		/// <remarks>
		/// Denormalised so the queue can sort by it without joining the messages. A queue that
		/// cannot cheaply answer "what has been waiting longest" is a queue nobody works from.
		/// </remarks>
		public DateTime LastActivityUtc { get; set; }

		/// <summary>The account that filed it.</summary>
		public string ReporterAccount { get; set; }

		/// <summary>The character they were playing, if any.</summary>
		public string ReporterCharacterName { get; set; }

		/// <summary>That character's id, so a later rename does not break the link.</summary>
		public long ReporterCharacterID { get; set; }

		/// <summary>What it is about.</summary>
		public SupportTicketCategory Category { get; set; }

		/// <summary>Where it has got to.</summary>
		public SupportTicketStatus Status { get; set; }

		/// <summary>
		/// Staff-set priority, higher is more urgent.
		/// </summary>
		/// <remarks>
		/// Set by staff, never by the player. A reporter-set priority is a field where every
		/// ticket is urgent.
		/// </remarks>
		public int Priority { get; set; }

		/// <summary>One-line summary, shown in the queue.</summary>
		public string Subject { get; set; }

		/// <summary>What the player wrote when they filed it.</summary>
		public string Body { get; set; }

		/// <summary>The accused account, for a player report.</summary>
		public string TargetAccount { get; set; }

		/// <summary>The accused character, for a player report.</summary>
		public string TargetCharacterName { get; set; }

		/// <summary>The accused character's id.</summary>
		public long TargetCharacterID { get; set; }

		/// <summary>Where it happened, for context and for searching the chat log around it.</summary>
		public string SceneName { get; set; }

		/// <summary>The staff account working it, or null if nobody has taken it.</summary>
		public string AssignedTo { get; set; }

		/// <summary>
		/// The lowest access level that may see and work this ticket: its support tier.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Tiers are access levels, not a parallel ladder: <c>GameMaster</c> (2) is the first tier
		/// and <c>Admin</c> (3) the second. A separate tier number would be one more thing for an
		/// operator to keep in step with the access level that actually decides what they can do,
		/// and a ticket escalated to "tier 2" that a tier-2 game master can still act on is a
		/// promotion that promotes nothing.
		/// </para>
		/// <para>
		/// Stored as the byte value of <c>AccessLevel</c>, like <c>accounts.access_level</c>.
		/// </para>
		/// </remarks>
		public byte RequiredAccessLevel { get; set; } = 2;

		/// <summary>The staff account that last moved the ticket to another tier.</summary>
		public string EscalatedBy { get; set; }

		/// <summary>When the ticket last moved to another tier.</summary>
		public DateTime? EscalatedUtc { get; set; }

		/// <summary>What staff decided, written when it is resolved or closed.</summary>
		public string Resolution { get; set; }

		/// <summary>When it was resolved or closed.</summary>
		public DateTime? ClosedUtc { get; set; }

		/// <summary>The staff account that finished it.</summary>
		public string ClosedBy { get; set; }

		/// <summary>The conversation, oldest first.</summary>
		public ICollection<SupportTicketMessageEntity> Messages { get; set; }
	}
}
