using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One guild, as a support agent needs to see it in a list.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Deliberately not <see cref="GuildData"/>. That type is the game's transfer shape: it
	/// carries the three advertisement strings a client renders and nothing about who is in the
	/// guild, because a client that is in a guild already has its roster. A support agent has
	/// neither, and the first two questions asked of any guild in a ticket are how many members
	/// it has and who leads it.
	/// </para>
	/// <para>
	/// <b>There is no leader column, and <see cref="LeaderName"/> is a derivation, not a fact.</b>
	/// A guild's ladder is the set of <c>guild_rank</c> rows it owns and a membership row stores
	/// a <c>rank_order</c>; the leader's seat is the highest rung the ladder defines, and a
	/// member leads by reaching it — the same test the game server applies. Nothing stops two
	/// members reaching it, so when several do, the earliest to join is named and
	/// <see cref="LeaderIsAmbiguous"/> is set rather than picking one silently. A guild with no
	/// ranks has no seat, and so no leader to name.
	/// </para>
	/// </remarks>
	public sealed class GuildAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>Display name. Player-supplied text.</summary>
		public string Name { get; set; }

		/// <summary>Members in the guild. Zero is an ordinary answer, not a missing read.</summary>
		public int MemberCount { get; set; }

		/// <summary>Rungs on the guild's rank ladder.</summary>
		public int RankCount { get; set; }

		/// <summary>The highest-ranked member's character ID, or zero when the guild is empty.</summary>
		public long LeaderCharacterID { get; set; }

		/// <summary>The highest-ranked member's character name, or null when the guild is empty.</summary>
		public string LeaderName { get; set; }

		/// <summary>Whether more than one member shares the highest rank present.</summary>
		public bool LeaderIsAmbiguous { get; set; }

		/// <summary>Whether the guild is listed in the recruitment directory.</summary>
		public bool IsRecruiting { get; set; }

		/// <summary>Comma-separated recruitment tags, as the guild wrote them.</summary>
		public string Tags { get; set; }

		/// <summary>The recruitment advertisement, which non-members can read in game.</summary>
		public string Blurb { get; set; }

		/// <summary>When the guild was founded.</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>Row version, which every write moves.</summary>
		public long Version { get; set; }
	}

	/// <summary>One rung of a guild's rank ladder.</summary>
	/// <remarks>
	/// <see cref="RankOrder"/> IS the value a membership row stores, which is why members are
	/// matched to ranks by it rather than by <see cref="ID"/>.
	/// </remarks>
	public sealed class GuildRankAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>Ordering position. Higher is more senior, and unique within the guild.</summary>
		public byte RankOrder { get; set; }

		/// <summary>Display name. Player-supplied text.</summary>
		public string Name { get; set; }

		/// <summary>
		/// Permission bit mask, matching the shared <c>GuildPermissions</c> flags enum.
		/// </summary>
		/// <remarks>
		/// The mask, not a list of names. This assembly cannot see the game's enum — it is a
		/// Unity assembly and this one is referenced by it, not the other way round — so naming
		/// the bits here would mean a second copy of the enum that drifts from the first one
		/// silently. The presentation layer decodes it.
		/// </remarks>
		public long Permissions { get; set; }

		/// <summary>How many members hold this rank.</summary>
		public int MemberCount { get; set; }

		/// <summary>When the rank was created.</summary>
		public DateTime TimeCreated { get; set; }
	}

	/// <summary>One member of a guild, with the character behind the membership row.</summary>
	/// <remarks>
	/// <b>The officer note is not here.</b> <c>character_guild.officer_note</c> is filtered by
	/// permission on the game server precisely so it never reaches a member who may not read it,
	/// and a support agent is not a member of the guild at all. The public note is carried
	/// because every member can already read it in game.
	/// </remarks>
	public sealed class GuildMemberAdminData
	{
		/// <summary>The membership row's own key.</summary>
		public long ID { get; set; }

		/// <summary>The member's character ID, which the panel links to.</summary>
		public long CharacterID { get; set; }

		/// <summary>
		/// Character name, with the deletion marker stripped when the character row is deleted.
		/// </summary>
		public string CharacterName { get; set; }

		/// <summary>Whether the character row behind this membership is soft-deleted.</summary>
		public bool CharacterDeleted { get; set; }

		/// <summary>0 offline, 1 online. Mirrors <c>CharacterSessionState</c>.</summary>
		public int SessionState { get; set; }

		/// <summary>
		/// When the session lease lapses. The epoch means no lease was ever taken.
		/// </summary>
		/// <remarks>
		/// Carried alongside <see cref="SessionState"/> because the flag alone is not presence: a
		/// scene server that crashed leaves the flag set behind it. The caller decides, and in
		/// the panel it decides the same way the character editor does.
		/// </remarks>
		public DateTime SessionLeaseExpiresUtc { get; set; }

		/// <summary>The rank order the membership row stores.</summary>
		public byte Rank { get; set; }

		/// <summary>
		/// The name of the rank row holding that order, or null when no rank row matches.
		/// </summary>
		/// <remarks>
		/// Null is a real state worth showing, not a gap to paper over: it means a membership row
		/// points at a rung that no longer exists on the ladder.
		/// </remarks>
		public string RankName { get; set; }

		/// <summary>
		/// Whether this member's rank reaches the highest rung the guild's ladder defines.
		/// </summary>
		/// <remarks>
		/// The game's own test is "at or above", not "equal to", so a member left holding an
		/// order above every surviving rank row still leads — and more than one member can.
		/// </remarks>
		public bool IsLeader { get; set; }

		/// <summary>The member's last recorded location within the guild context.</summary>
		public string Location { get; set; }

		/// <summary>The note every member of the guild can read. Player-supplied text.</summary>
		public string PublicNote { get; set; }

		/// <summary>When the character joined.</summary>
		public DateTime TimeCreated { get; set; }
	}

	/// <summary>One pending application to join a guild.</summary>
	public sealed class GuildApplicantAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>The applying character.</summary>
		public long CharacterID { get; set; }

		/// <summary>The applicant's character name, deletion marker stripped.</summary>
		public string CharacterName { get; set; }

		/// <summary>The applicant's message. May be empty. Player-supplied text.</summary>
		public string Message { get; set; }

		/// <summary>When the application was filed.</summary>
		public DateTime TimeCreated { get; set; }
	}

	/// <summary>One guild in full: the row, its ladder, its roster and its pending applications.</summary>
	/// <remarks>
	/// Assembled in one read so that the member count, the ranks and the roster all describe the
	/// same instant. Four separate calls could show a roster of six under a count of five.
	/// </remarks>
	public sealed class GuildAdminDetail
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>Display name. Player-supplied text.</summary>
		public string Name { get; set; }

		/// <summary>The notice, written for members.</summary>
		public string Notice { get; set; }

		/// <summary>The message of the day, written for members.</summary>
		public string MessageOfTheDay { get; set; }

		/// <summary>The recruitment advertisement, written for everybody else.</summary>
		public string Blurb { get; set; }

		/// <summary>Comma-separated recruitment tags.</summary>
		public string Tags { get; set; }

		/// <summary>Whether the guild is listed in the recruitment directory.</summary>
		public bool IsRecruiting { get; set; }

		/// <summary>When the guild was founded.</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>Row version, which every write moves.</summary>
		public long Version { get; set; }

		/// <summary>The rank ladder, most senior first.</summary>
		public IReadOnlyList<GuildRankAdminData> Ranks { get; set; } = Array.Empty<GuildRankAdminData>();

		/// <summary>The roster, most senior first. Empty is an ordinary state.</summary>
		public IReadOnlyList<GuildMemberAdminData> Members { get; set; } = Array.Empty<GuildMemberAdminData>();

		/// <summary>Pending applications, oldest first.</summary>
		public IReadOnlyList<GuildApplicantAdminData> Applications { get; set; } = Array.Empty<GuildApplicantAdminData>();
	}

	/// <summary>One row of a guild's activity log.</summary>
	/// <remarks>
	/// The log stores character IDs, not names, and it is append-only — so a row naming a
	/// character who has since been deleted still names them. The two names here are resolved at
	/// read time from the characters table and are null when the ID is zero (the log's "nobody")
	/// or when no such character exists any more.
	/// </remarks>
	public sealed class GuildLogAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>The guild the entry belongs to.</summary>
		public long GuildID { get; set; }

		/// <summary>The event kind.</summary>
		public GuildLogEventType EventType { get; set; }

		/// <summary>The character who acted, or zero.</summary>
		public long ActorCharacterID { get; set; }

		/// <summary>The actor's name, or null.</summary>
		public string ActorName { get; set; }

		/// <summary>The character acted upon, or zero.</summary>
		public long TargetCharacterID { get; set; }

		/// <summary>The target's name, or null.</summary>
		public string TargetName { get; set; }

		/// <summary>Optional short detail, such as a rank name. Player-supplied text.</summary>
		public string Detail { get; set; }

		/// <summary>When the entry was written.</summary>
		public DateTime TimeCreated { get; set; }
	}

	/// <summary>One member of a party.</summary>
	public sealed class PartyMemberAdminData
	{
		/// <summary>The membership row's own key.</summary>
		public long ID { get; set; }

		/// <summary>The member's character ID, which the panel links to.</summary>
		public long CharacterID { get; set; }

		/// <summary>Character name, deletion marker stripped.</summary>
		public string CharacterName { get; set; }

		/// <summary>Whether the character row behind this membership is soft-deleted.</summary>
		public bool CharacterDeleted { get; set; }

		/// <summary>The scene the character was last recorded in.</summary>
		public string SceneName { get; set; }

		/// <summary>0 offline, 1 online. Mirrors <c>CharacterSessionState</c>.</summary>
		public int SessionState { get; set; }

		/// <summary>When the session lease lapses. The epoch means no lease was ever taken.</summary>
		public DateTime SessionLeaseExpiresUtc { get; set; }

		/// <summary>Rank within the party, matching the shared <c>PartyRank</c> enum.</summary>
		public byte Rank { get; set; }

		/// <summary>
		/// Last health percentage reported for the party frame, 0 to 1.
		/// </summary>
		/// <remarks>
		/// Written by the owning scene server for the other members' party frames, so it is as
		/// old as that character's last update — a number for the frame, not a live vital sign.
		/// </remarks>
		public float HealthPCT { get; set; }

		/// <summary>When the character joined the party.</summary>
		public DateTime TimeCreated { get; set; }
	}

	/// <summary>One party, with its roster.</summary>
	/// <remarks>
	/// A party is scoped to one world server — see <c>PartyEntity.WorldServerID</c> — so
	/// <see cref="WorldServerID"/> is part of its identity to an operator, not a detail.
	/// </remarks>
	public sealed class PartyAdminData
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>The world server the party lives on.</summary>
		public long WorldServerID { get; set; }

		/// <summary>Members in the party. Zero is an ordinary answer.</summary>
		public int MemberCount { get; set; }

		/// <summary>When the party was formed.</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>Row version, which every write moves.</summary>
		public long Version { get; set; }

		/// <summary>The roster, leader first. Empty is an ordinary state.</summary>
		public IReadOnlyList<PartyMemberAdminData> Members { get; set; } = Array.Empty<PartyMemberAdminData>();
	}

	/// <summary>One page of guilds, with the total the pager needs.</summary>
	public sealed class GuildAdminPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<GuildAdminData> Items { get; set; } = Array.Empty<GuildAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter, across all pages.</summary>
		public int TotalCount { get; set; }
	}

	/// <summary>One page of a guild's activity log.</summary>
	public sealed class GuildLogPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<GuildLogAdminData> Items { get; set; } = Array.Empty<GuildLogAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows in this guild's log.</summary>
		public int TotalCount { get; set; }
	}

	/// <summary>One page of parties, each carrying its roster.</summary>
	public sealed class PartyAdminPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<PartyAdminData> Items { get; set; } = Array.Empty<PartyAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter, across all pages.</summary>
		public int TotalCount { get; set; }
	}
}
