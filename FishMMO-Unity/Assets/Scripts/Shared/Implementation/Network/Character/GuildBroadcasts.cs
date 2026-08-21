using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for creating a new guild.
	/// Contains the name of the guild to be created.
	/// </summary>
	public struct GuildCreateBroadcast : IBroadcast
	{
		/// <summary>Name of the guild to create.</summary>
		public string GuildName;
	}

	/// <summary>
	/// Broadcast for inviting a character to a guild.
	/// Contains the inviter and target character IDs.
	/// </summary>
	public struct GuildInviteBroadcast : IBroadcast
	{
		/// <summary>Character ID of the player sending the invite.</summary>
		public long InviterCharacterID;
		/// <summary>Character ID of the player being invited.</summary>
		public long TargetCharacterID;
	}

	/// <summary>
	/// Broadcast for accepting a guild invitation.
	/// </summary>
	/// <remarks>
	/// Carries the identity of the invitation being answered. This used to be an empty struct,
	/// which left the server resolving "whatever invitation is pending for this character" — so a
	/// dialog the player left open past the invitation TTL accepted whichever guild invited them
	/// NEXT, silently and with no way for either side to notice. The server re-verifies this
	/// against its own pending record and refuses a mismatch; the field is a claim to be checked,
	/// never a value to be trusted.
	/// </remarks>
	public struct GuildAcceptInviteBroadcast : IBroadcast
	{
		/// <summary>Character ID of the player whose invitation is being accepted.</summary>
		public long InviterCharacterID;
	}
	/// <summary>
	/// Broadcast for declining a guild invitation.
	/// </summary>
	/// <remarks>
	/// Carries the same identity as the accept broadcast for the same reason: a decline that
	/// arrives after the pending slot has been refilled would otherwise throw away an invitation
	/// the player has not seen yet.
	/// </remarks>
	public struct GuildDeclineInviteBroadcast : IBroadcast
	{
		/// <summary>Character ID of the player whose invitation is being declined.</summary>
		public long InviterCharacterID;
	}

	/// <summary>
	/// Broadcast for adding a member to a guild.
	/// Contains guild ID, character ID, rank, and location.
	/// </summary>
	public struct GuildAddBroadcast : IBroadcast
	{
		/// <summary>ID of the guild the member is being added to.</summary>
		public long GuildID;
		/// <summary>Character ID of the member being added.</summary>
		public long CharacterID;
		/// <summary>
		/// The member's rank ORDER — the position on the guild's ladder, not a fixed enum value.
		/// </summary>
		/// <remarks>
		/// This was <c>GuildRank</c>. It is now the raw ordering byte, because a guild's ranks are
		/// rows it owns and their names are whatever it called them; the client resolves the name
		/// from <see cref="GuildRankListBroadcast"/> and renders that. The VALUE is unchanged —
		/// a member who was an Officer sent 2 before and sends 2 now.
		/// </remarks>
		public byte RankOrder;
		/// <summary>Location of the member (may be used for online status or region).</summary>
		public string Location;
		/// <summary>The member's race identifier, for the roster's class column.</summary>
		public int RaceID;
		/// <summary>The member's character level, for the roster's level column.</summary>
		public int Level;
		/// <summary>Note about this member visible to every member of the guild.</summary>
		public string PublicNote;
		/// <summary>
		/// Note about this member visible only to ranks holding <c>ViewOfficerNotes</c>.
		/// </summary>
		/// <remarks>
		/// EMPTY unless the recipient's own rank holds the permission. The filter is applied on
		/// the server, where the roster payload is built, and the column simply is not written
		/// into the message for anybody else. Sending it and hiding it in the panel would put the
		/// text one packet capture away from every member of the guild, which is the opposite of
		/// what an officer-only note is for.
		/// </remarks>
		public string OfficerNote;
		/// <summary>
		/// UTC ticks of the member's last character save, used as a last-seen figure for members
		/// who are not connected.
		/// </summary>
		/// <remarks>
		/// Sent as ticks rather than a <c>DateTime</c> so the wire format is a plain 64-bit
		/// integer with no dependence on how the serializer treats <c>DateTimeKind</c>.
		/// </remarks>
		public long LastOnlineUtcTicks;
	}

	/// <summary>
	/// Broadcast for adding multiple members to a guild at once.
	/// Used for bulk member addition or synchronization.
	/// </summary>
	public struct GuildAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of members to add to the guild.</summary>
		public GuildAddBroadcast[] Members;
	}

	/// <summary>
	/// Broadcast for a member leaving a guild.
	/// No additional data required.
	/// </summary>
	public struct GuildLeaveBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for removing a member from a guild.
	/// Contains the character ID to be removed.
	/// </summary>
	public struct GuildRemoveBroadcast : IBroadcast
	{
		/// <summary>Character ID of the member to remove.</summary>
		public long CharacterID;
	}

	/// <summary>
	/// Broadcast for changing a member's rank within a guild.
	/// Contains the character ID and the new rank.
	/// </summary>
	public struct GuildChangeRankBroadcast : IBroadcast
	{
		/// <summary>Character ID of the member whose rank is changing.</summary>
		public long CharacterID;
		/// <summary>The rank ORDER to move the member to.</summary>
		public byte RankOrder;
	}

	/// <summary>
	/// Result types for guild operations, indicating success or specific failure reasons.
	/// </summary>
	public enum GuildResultType : byte
	{
		/// <summary>Operation succeeded.</summary>
		Success = 0,
		/// <summary>Guild name is invalid.</summary>
		InvalidGuildName = 1,
		/// <summary>Guild name already exists.</summary>
		NameAlreadyExists = 2,
		/// <summary>Character is already in a guild.</summary>
		AlreadyInGuild = 3,
		/// <summary>The guild no longer exists (disbanded between invite and accept).</summary>
		GuildNotFound = 4,
		/// <summary>The guild is at its member cap.</summary>
		GuildFull = 5,
		/// <summary>The invitation expired, or does not match the one being answered.</summary>
		InvitationExpired = 6,
		/// <summary>The target has blocked the requester.</summary>
		TargetIsBlocked = 7,
		/// <summary>The requester invited this target too recently.</summary>
		InviteOnCooldown = 8,
		/// <summary>The requester's guild rank does not permit the operation.</summary>
		InsufficientRank = 9,
		/// <summary>The named rank does not exist in this guild.</summary>
		RankNotFound = 10,
		/// <summary>The rank name was empty or contained nothing usable.</summary>
		InvalidRankName = 11,
		/// <summary>The guild already has as many ranks as it may have.</summary>
		TooManyRanks = 12,
		/// <summary>The rank still has members and cannot be removed.</summary>
		RankInUse = 13,
		/// <summary>The requester already has an application pending with this guild.</summary>
		AlreadyApplied = 14,
		/// <summary>The application no longer exists — withdrawn, or already resolved.</summary>
		ApplicationNotFound = 15,
		/// <summary>The guild is not accepting applications.</summary>
		NotRecruiting = 16,
		/// <summary>The requester applied too recently.</summary>
		ApplyOnCooldown = 17,
		/// <summary>The application was accepted for delivery. Not the same as being admitted.</summary>
		ApplicationSent = 18,
		/// <summary>
		/// The operation would have left the guild with no rank able to administer it.
		/// </summary>
		/// <remarks>
		/// Its own code rather than a generic refusal because it is the one refusal a player is
		/// entitled to find surprising: the request was legal, the requester had the permission,
		/// and it was still refused — to stop the guild soft-locking itself.
		/// </remarks>
		WouldOrphanGuild = 19,
	}

	/// <summary>
	/// Broadcast for sending the result of a guild operation.
	/// Contains the result type indicating success or failure reason.
	/// </summary>
	public struct GuildResultBroadcast : IBroadcast
	{
		/// <summary>Result of the guild operation.</summary>
		public GuildResultType Result;
	}

	/// <summary>
	/// Maximum length the server accepts for a guild message of the day or notice.
	/// </summary>
	/// <remarks>
	/// Matches the <c>character varying(500)</c> the guild table declares for both columns. The
	/// client trims to this before sending so a player sees the limit rather than a silent
	/// truncation, but the server enforces it independently — the client's copy is a courtesy.
	/// </remarks>
	public static class GuildTextLimits
	{
		/// <summary>Maximum message-of-the-day length, in characters.</summary>
		public const int MaxMessageOfTheDayLength = 500;

		/// <summary>Maximum notice length, in characters.</summary>
		public const int MaxNoticeLength = 500;

		/// <summary>Maximum length of either member note, in characters.</summary>
		/// <remarks>Matches <c>character varying(128)</c> on both note columns.</remarks>
		public const int MaxMemberNoteLength = 128;

		/// <summary>Maximum recruitment blurb length, in characters.</summary>
		public const int MaxBlurbLength = 500;

		/// <summary>Maximum length of the whole comma-separated tag list, in characters.</summary>
		public const int MaxTagsLength = 200;

		/// <summary>Maximum length of an application message, in characters.</summary>
		public const int MaxApplicationMessageLength = 300;

		/// <summary>Maximum directory search term length, in characters.</summary>
		public const int MaxDirectorySearchLength = 64;
	}

	/// <summary>
	/// Broadcast carrying a guild's descriptive text to its members.
	/// </summary>
	/// <remarks>
	/// The <c>notice</c> and <c>message_of_the_day</c> columns have existed on the guild table
	/// since it was created, and <c>PersistMessageOfTheDayAsync</c> has existed with no caller —
	/// there was no message on the wire able to carry either value, so nothing could ever set or
	/// read them. This is that message.
	/// </remarks>
	public struct GuildInfoBroadcast : IBroadcast
	{
		/// <summary>The guild this information describes.</summary>
		public long GuildID;
		/// <summary>The guild's display name.</summary>
		public string Name;
		/// <summary>The guild's notice text.</summary>
		public string Notice;
		/// <summary>The guild's message of the day.</summary>
		public string MessageOfTheDay;
	}

	/// <summary>
	/// Broadcast requesting a change to the guild's message of the day.
	/// </summary>
	public struct GuildSetMessageOfTheDayBroadcast : IBroadcast
	{
		/// <summary>The requested message of the day.</summary>
		public string MessageOfTheDay;
	}

	/// <summary>
	/// Broadcast requesting a change to the guild's notice text.
	/// </summary>
	public struct GuildSetNoticeBroadcast : IBroadcast
	{
		/// <summary>The requested notice text.</summary>
		public string Notice;
	}

	/// <summary>
	/// Broadcast requesting that guild leadership be transferred to another member.
	/// </summary>
	/// <remarks>
	/// Guilds had no transfer at all: the ONLY way leadership ever moved was as a side effect of
	/// the leader pressing Leave, which picked a random officer. A leader who simply stopped
	/// playing without leaving left the guild permanently unadministered, because promote, kick,
	/// invite and disband all require <c>GuildRank.Leader</c>.
	/// </remarks>
	public struct GuildTransferLeadershipBroadcast : IBroadcast
	{
		/// <summary>Character ID of the member who should become leader.</summary>
		public long CharacterID;
	}

	/// <summary>
	/// Broadcast requesting that the guild be disbanded.
	/// </summary>
	/// <remarks>
	/// Carries the guild name the client believes it is disbanding. The server compares it against
	/// the guild the requester is actually in, so a confirmation dialog raised against one guild
	/// cannot destroy another after a fast leave-and-rejoin — the same reasoning as the invitation
	/// identity, applied to the one action that cannot be undone.
	/// </remarks>
	public struct GuildDisbandBroadcast : IBroadcast
	{
		/// <summary>The guild name the requester typed to confirm.</summary>
		public string ConfirmationName;
	}

	/// <summary>
	/// The kind of event a guild log row records.
	/// </summary>
	/// <remarks>
	/// Mirrors the database's <c>GuildLogEventType</c>. Duplicated rather than shared because the
	/// shared assembly does not reference the database assembly — and it should not: a wire enum
	/// and a storage enum that must stay in step are two facts, and collapsing them would make the
	/// client's message format depend on the server's schema project.
	///
	/// The text is composed on the CLIENT from this code and the two character IDs, not sent as a
	/// sentence. A log written as prose cannot be filtered and bakes today's wording into rows that
	/// outlive it.
	/// </remarks>
	public enum GuildLogEvent : byte
	{
		/// <summary>Unrecognised — rendered as a plain line rather than dropped.</summary>
		Unknown = 0,
		/// <summary>The guild was created.</summary>
		Created = 1,
		/// <summary>A member joined.</summary>
		Joined = 2,
		/// <summary>A member left of their own accord.</summary>
		Left = 3,
		/// <summary>A member was removed.</summary>
		Kicked = 4,
		/// <summary>A member was promoted.</summary>
		Promoted = 5,
		/// <summary>A member was demoted.</summary>
		Demoted = 6,
		/// <summary>Leadership was transferred.</summary>
		LeadershipTransferred = 7,
		/// <summary>The message of the day was changed.</summary>
		MessageOfTheDayChanged = 8,
		/// <summary>The notice was changed.</summary>
		NoticeChanged = 9,
		/// <summary>A rank's name or permissions were edited.</summary>
		RankEdited = 10,
		/// <summary>A rank was created.</summary>
		RankCreated = 11,
		/// <summary>A rank was deleted.</summary>
		RankDeleted = 12,
		/// <summary>The recruitment advertisement changed.</summary>
		RecruitmentChanged = 13,
		/// <summary>An application was accepted.</summary>
		ApplicationAccepted = 14,
		/// <summary>An application was declined.</summary>
		ApplicationDeclined = 15,
		/// <summary>A member note was edited.</summary>
		NoteChanged = 16,
	}

	/// <summary>
	/// One guild activity log row on the wire.
	/// </summary>
	public struct GuildLogEntry
	{
		/// <summary>What happened.</summary>
		public GuildLogEvent Event;
		/// <summary>The character who performed the action, or zero.</summary>
		public long ActorCharacterID;
		/// <summary>The character the action was performed on, or zero.</summary>
		public long TargetCharacterID;
		/// <summary>Optional short detail, such as a rank name. May be empty.</summary>
		public string Detail;
		/// <summary>UTC ticks of the event.</summary>
		public long TimeUtcTicks;
	}

	/// <summary>
	/// Broadcast requesting the guild's recent activity log.
	/// </summary>
	/// <remarks>
	/// Pull rather than push. The log is read rarely — a player opens the tab, looks, and closes it
	/// — and pushing every entry to every member as it happened would put a message on the wire per
	/// event per member for something almost nobody is looking at.
	/// </remarks>
	public struct GuildLogRequestBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast carrying a guild's recent activity log, newest first.
	/// </summary>
	public struct GuildLogBroadcast : IBroadcast
	{
		/// <summary>The guild the log belongs to.</summary>
		public long GuildID;
		/// <summary>The most recent entries, newest first.</summary>
		public GuildLogEntry[] Entries;
	}
}
