using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// One rank on a guild's ladder, on the wire.
	/// </summary>
	/// <remarks>
	/// <see cref="Permissions"/> travels as a plain <c>long</c> rather than as the
	/// <see cref="GuildPermissions"/> enum so the serializer has nothing to infer about enum
	/// widths, and so a client built against an older flag set reads an unknown bit as a bit it
	/// does not render rather than failing to read the message at all.
	/// </remarks>
	public struct GuildRankEntry
	{
		/// <summary>Ordering position. Higher is more senior.</summary>
		public byte RankOrder;
		/// <summary>Display name.</summary>
		public string Name;
		/// <summary>Permission bit mask.</summary>
		public long Permissions;
	}

	/// <summary>
	/// Broadcast carrying a guild's rank ladder and the recipient's own standing in it.
	/// </summary>
	/// <remarks>
	/// The viewer's rank and permissions are included in the SAME message as the ladder, rather
	/// than being derived on the client by looking their own rank up in it. That derivation is
	/// exactly the mistake this whole change exists to avoid: what a client believes it may do is
	/// presentation, and the value it presents should be the one the server computed, so that a
	/// disagreement between the two shows up as a greyed button rather than as a request the
	/// server silently drops.
	/// </remarks>
	public struct GuildRankListBroadcast : IBroadcast
	{
		/// <summary>The guild the ladder belongs to.</summary>
		public long GuildID;
		/// <summary>The ladder, ordered by rank order ascending.</summary>
		public GuildRankEntry[] Ranks;
		/// <summary>The recipient's own rank order.</summary>
		public byte ViewerRankOrder;
		/// <summary>The recipient's own permission mask, as computed by the server.</summary>
		public long ViewerPermissions;
		/// <summary>The highest rank order that exists in this guild — the leader's seat.</summary>
		public byte LeaderRankOrder;
	}

	/// <summary>
	/// Broadcast requesting the guild's rank ladder.
	/// </summary>
	/// <remarks>
	/// Carries no guild ID, for the same reason the log request does not: the server takes the
	/// guild from the requester's own controller, so there is nothing in the message to forge.
	/// </remarks>
	public struct GuildRankListRequestBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast requesting a change to one rank's name and permissions.
	/// </summary>
	public struct GuildEditRankBroadcast : IBroadcast
	{
		/// <summary>The rank position to edit.</summary>
		public byte RankOrder;
		/// <summary>The requested display name.</summary>
		public string Name;
		/// <summary>The requested permission mask.</summary>
		public long Permissions;
	}

	/// <summary>
	/// Broadcast requesting a new rank at a given position.
	/// </summary>
	public struct GuildCreateRankBroadcast : IBroadcast
	{
		/// <summary>The requested position. Must be below the leader's.</summary>
		public byte RankOrder;
		/// <summary>The requested display name.</summary>
		public string Name;
		/// <summary>The requested permission mask.</summary>
		public long Permissions;
	}

	/// <summary>
	/// Broadcast requesting removal of a rank.
	/// </summary>
	public struct GuildDeleteRankBroadcast : IBroadcast
	{
		/// <summary>The rank position to remove.</summary>
		public byte RankOrder;
	}

	/// <summary>
	/// Broadcast requesting a change to one of a member's two guild notes.
	/// </summary>
	public struct GuildSetMemberNoteBroadcast : IBroadcast
	{
		/// <summary>The member the note is about.</summary>
		public long CharacterID;
		/// <summary>The note text.</summary>
		public string Note;
		/// <summary>True to write the officer-only note, false for the public one.</summary>
		public bool IsOfficerNote;
	}

	/// <summary>
	/// Broadcast carrying a guild's recruitment advertisement to its own members.
	/// </summary>
	/// <remarks>
	/// Sent to MEMBERS. Non-members see the advertisement through
	/// <see cref="GuildDirectoryBroadcast"/>, which is a different message because it answers a
	/// different question — "which guilds are recruiting" rather than "what does my guild
	/// currently advertise".
	/// </remarks>
	public struct GuildRecruitmentInfoBroadcast : IBroadcast
	{
		/// <summary>The guild the advertisement belongs to.</summary>
		public long GuildID;
		/// <summary>The advertisement text.</summary>
		public string Blurb;
		/// <summary>Comma-separated tags.</summary>
		public string Tags;
		/// <summary>Whether the guild is listed in the directory.</summary>
		public bool IsRecruiting;
	}

	/// <summary>
	/// Broadcast requesting a change to the guild's recruitment advertisement.
	/// </summary>
	public struct GuildSetRecruitmentBroadcast : IBroadcast
	{
		/// <summary>The requested advertisement text.</summary>
		public string Blurb;
		/// <summary>The requested comma-separated tags.</summary>
		public string Tags;
		/// <summary>Whether the guild should be listed.</summary>
		public bool IsRecruiting;
	}

	/// <summary>
	/// One guild as it appears in the recruitment directory.
	/// </summary>
	public struct GuildDirectoryEntry
	{
		/// <summary>Guild identifier, used to apply.</summary>
		public long GuildID;
		/// <summary>Guild display name.</summary>
		public string Name;
		/// <summary>Advertisement text.</summary>
		public string Blurb;
		/// <summary>Comma-separated tags.</summary>
		public string Tags;
		/// <summary>Current member count.</summary>
		public int MemberCount;
		/// <summary>Maximum member count, so the client can render "37 / 100" without a constant.</summary>
		public int MaxMemberCount;
	}

	/// <summary>
	/// Broadcast requesting a page of the recruitment directory.
	/// </summary>
	public struct GuildDirectoryRequestBroadcast : IBroadcast
	{
		/// <summary>Optional search term matched against name, blurb and tags.</summary>
		public string SearchTerm;
	}

	/// <summary>
	/// Broadcast carrying a page of the recruitment directory.
	/// </summary>
	public struct GuildDirectoryBroadcast : IBroadcast
	{
		/// <summary>The matching guilds.</summary>
		public GuildDirectoryEntry[] Entries;
	}

	/// <summary>
	/// Broadcast requesting to join a guild through its directory listing.
	/// </summary>
	/// <remarks>
	/// This one DOES carry a guild ID, unlike the member-only requests: the requester is by
	/// definition not in the guild, so the server has no controller to read it from. Everything
	/// else about the request — that the guild exists, is recruiting, has room, has not blocked
	/// the applicant, and that the applicant is guildless — is re-established server-side.
	/// </remarks>
	public struct GuildApplyBroadcast : IBroadcast
	{
		/// <summary>The guild being applied to.</summary>
		public long GuildID;
		/// <summary>The applicant's message. May be empty.</summary>
		public string Message;
	}

	/// <summary>
	/// One pending application on the wire.
	/// </summary>
	public struct GuildApplicationEntry
	{
		/// <summary>Application identifier, used to accept or decline.</summary>
		public long ApplicationID;
		/// <summary>The applying character.</summary>
		public long CharacterID;
		/// <summary>The applicant's message.</summary>
		public string Message;
		/// <summary>UTC ticks of submission.</summary>
		public long TimeUtcTicks;
	}

	/// <summary>
	/// Broadcast requesting the guild's pending application queue.
	/// </summary>
	public struct GuildApplicationListRequestBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast carrying the guild's pending application queue, oldest first.
	/// </summary>
	public struct GuildApplicationListBroadcast : IBroadcast
	{
		/// <summary>The guild the queue belongs to.</summary>
		public long GuildID;
		/// <summary>The pending applications.</summary>
		public GuildApplicationEntry[] Entries;
	}

	/// <summary>
	/// Broadcast accepting or declining one pending application.
	/// </summary>
	public struct GuildResolveApplicationBroadcast : IBroadcast
	{
		/// <summary>The application to resolve.</summary>
		public long ApplicationID;
		/// <summary>True to admit the applicant, false to decline.</summary>
		public bool Accept;
	}
}
