using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Why a player is being reported, as chosen from the report panel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A byte on the wire. The values are part of the wire contract and are never renumbered: a
	/// client one patch behind the server must still mean the same thing by the same number. New
	/// reasons are appended before nothing — after <see cref="Other"/> — and
	/// <see cref="PlayerReportReasons.Count"/> moves with them.
	/// </para>
	/// <para>
	/// The reason is not a separate database column. It is carried in the ticket's derived subject,
	/// which is the one field the staff queue lists, so a moderator scanning the queue sees
	/// "Report: Name (Cheating)" without opening anything — and the support ticket schema stays
	/// the one the chat command already writes.
	/// </para>
	/// </remarks>
	public enum PlayerReportReason : byte
	{
		/// <summary>Abuse, threats or targeted unpleasantness.</summary>
		Harassment = 0,
		/// <summary>Third-party software, speed or damage hacks.</summary>
		Cheating = 1,
		/// <summary>Automated play.</summary>
		Botting = 2,
		/// <summary>Repeated or advertising chat.</summary>
		Spam = 3,
		/// <summary>A trade or promise made to take something from the reporter.</summary>
		Scam = 4,
		/// <summary>A character name that breaks the naming rules.</summary>
		OffensiveName = 5,
		/// <summary>Abusing a bug for advantage.</summary>
		Exploiting = 6,
		/// <summary>Anything the other reasons do not cover.</summary>
		Other = 7,
	}

	/// <summary>
	/// The one place the report reasons are validated and named.
	/// </summary>
	/// <remarks>
	/// Shared by the panel, which lists the names, and the server, which writes the same name into
	/// the ticket subject. Two copies of the wording would drift, and a player who chose "Offensive
	/// name" should not find staff reading about an "OffensiveName".
	/// </remarks>
	public static class PlayerReportReasons
	{
		/// <summary>How many reasons are defined. Every value below this is valid.</summary>
		public const int Count = 8;

		/// <summary>True when <paramref name="reason"/> is one of the defined values.</summary>
		/// <remarks>
		/// A byte arriving from a client can be anything from 0 to 255; the enum type does not
		/// constrain it. The server tests this before using the value for anything.
		/// </remarks>
		public static bool IsDefined(PlayerReportReason reason)
		{
			return (byte)reason < Count;
		}

		/// <summary>The player-facing name of a reason.</summary>
		/// <returns>The name, or "Other" for an undefined value.</returns>
		public static string DisplayName(PlayerReportReason reason)
		{
			switch (reason)
			{
				case PlayerReportReason.Harassment: return "Harassment";
				case PlayerReportReason.Cheating: return "Cheating";
				case PlayerReportReason.Botting: return "Botting";
				case PlayerReportReason.Spam: return "Spam";
				case PlayerReportReason.Scam: return "Scam";
				case PlayerReportReason.OffensiveName: return "Offensive name";
				case PlayerReportReason.Exploiting: return "Exploiting";
				default: return "Other";
			}
		}
	}

	/// <summary>
	/// Client to server: file a player report from the report panel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything here is a claim. The server resolves the reporter from the connection, resolves
	/// the target by <see cref="TargetCharacterID"/> against its own online mapping and only falls
	/// back to <see cref="TargetCharacterName"/> when that fails — exactly as <c>/report</c> does —
	/// and clamps both strings whatever length arrives.
	/// </para>
	/// <para>
	/// The same filing path as the chat command, so the same limits: five unfinished tickets per
	/// account and a cooldown between filings, both enforced by the support ticket service inside
	/// its insert transaction. The answer always arrives as a <see cref="ReportPlayerResultBroadcast"/>.
	/// </para>
	/// </remarks>
	public struct ReportPlayerBroadcast : IBroadcast
	{
		/// <summary>Longest description the server keeps. The panel's counter counts against this.</summary>
		public const int MaxDescriptionLength = 512;

		/// <summary>Longest target name the server reads.</summary>
		/// <remarks>
		/// Generously above any legal character name. It bounds what a hostile client can make the
		/// server copy into a ticket; it is not the naming rule.
		/// </remarks>
		public const int MaxTargetNameLength = 64;

		/// <summary>The reported character's id as the client knows it, or 0 when it knows only a name.</summary>
		public long TargetCharacterID;

		/// <summary>The reported character's name as the client shows it.</summary>
		public string TargetCharacterName;

		/// <summary>The reason chosen.</summary>
		public PlayerReportReason Reason;

		/// <summary>What happened, in the reporter's words.</summary>
		public string Description;
	}

	/// <summary>
	/// Server to client: the outcome of a <see cref="ReportPlayerBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// Sent for every request the server could attribute to a character — refused, filed, or failed
	/// in the database — so the panel never waits on an answer that is not coming.
	/// <see cref="Message"/> is the same player-facing text the chat command would have replied
	/// with, including the service's own wording for a refusal.
	/// </remarks>
	public struct ReportPlayerResultBroadcast : IBroadcast
	{
		/// <summary>Longest message the server sends.</summary>
		public const int MaxMessageLength = 256;

		/// <summary>True when a ticket was created.</summary>
		public bool Filed;

		/// <summary>The new ticket's number when <see cref="Filed"/>; otherwise 0.</summary>
		public long TicketID;

		/// <summary>What to tell the player.</summary>
		public string Message;
	}
}
