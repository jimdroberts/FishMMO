using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// The rank ladder a guild is seeded with, and the mapping from the legacy
	/// <see cref="GuildRank"/> enum to the permission flags that reproduce its behaviour exactly.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ranks are stored per guild as rows in <c>guild_rank</c>, keyed by an ORDER byte. The order
	/// byte is the same value <c>character_guild.rank</c> has always held, so no membership row is
	/// rewritten by the move to flags: a member whose stored rank was <c>2</c> was an Officer
	/// before and holds rank order <c>2</c> afterwards. That is the whole reason the ladder is
	/// anchored at the legacy numbers rather than renumbered into something tidier.
	/// </para>
	/// <para>
	/// Higher order = more senior. The LEADER is whichever rank row carries the highest order in
	/// that guild, which is <see cref="LeaderRankOrder"/> for every guild that has not added ranks.
	/// "Cannot act on someone at or above your rank" is a comparison on this byte.
	/// </para>
	/// <para>
	/// The permission sets below are not a design opinion. They are a transcription of what the
	/// enum-based checks actually allowed, taken site by site from the pre-change
	/// <c>GuildSystem</c>: invite was <c>Leader || Officer</c>; kick was <c>&gt;= Officer</c>;
	/// MOTD and notice were <c>&gt;= Officer</c>; rank change, leadership transfer and disband
	/// were <c>== Leader</c>. Existing leaders therefore lose nothing and existing members gain
	/// nothing.
	/// </para>
	/// </remarks>
	public static class GuildRankDefaults
	{
		/// <summary>Rank order held by the default lowest rank.</summary>
		public const byte MemberRankOrder = (byte)GuildRank.Member;

		/// <summary>Rank order held by the default middle rank.</summary>
		public const byte OfficerRankOrder = (byte)GuildRank.Officer;

		/// <summary>Rank order held by the default top rank of a freshly seeded guild.</summary>
		/// <remarks>
		/// Not a constant the permission checks consult. Leadership is "the highest order row that
		/// exists in this guild", read from the database, precisely so that a guild which adds
		/// ranks does not end up with a leader seat defined by a number in this file.
		/// </remarks>
		public const byte DefaultLeaderRankOrder = (byte)GuildRank.Leader;

		/// <summary>Alias kept for readability at call sites that mean "the seeded top rank".</summary>
		public const byte LeaderRankOrder = DefaultLeaderRankOrder;

		/// <summary>Lowest legal rank order. Zero means "no rank", i.e. not in a guild.</summary>
		public const byte MinRankOrder = 1;

		/// <summary>
		/// Highest legal rank order. Leaves room above the seeded ladder for guilds that add ranks.
		/// </summary>
		public const byte MaxRankOrder = 200;

		/// <summary>Most rank rows one guild may define.</summary>
		public const int MaxRanksPerGuild = 12;

		/// <summary>Maximum length of a rank name.</summary>
		/// <remarks>Matches <c>character varying(24)</c> on <c>guild_rank.name</c>.</remarks>
		public const int MaxRankNameLength = 24;

		/// <summary>
		/// Permissions the seeded lowest rank holds: none.
		/// </summary>
		public const GuildPermissions MemberPermissions = GuildPermissions.None;

		/// <summary>
		/// Permissions the seeded middle rank holds — the exact set the old
		/// <c>Rank &gt;= GuildRank.Officer</c> checks granted.
		/// </summary>
		/// <remarks>
		/// <see cref="GuildPermissions.ViewOfficerNotes"/> and
		/// <see cref="GuildPermissions.ManageApplications"/> are included even though neither
		/// feature existed under the enum. They gate functionality that did not exist to be
		/// withheld, so including them cannot take anything away from anybody; leaving them out
		/// would ship a recruitment queue and an officer note that no officer could reach.
		/// </remarks>
		public const GuildPermissions OfficerPermissions =
			GuildPermissions.Invite |
			GuildPermissions.Kick |
			GuildPermissions.EditMessageOfTheDay |
			GuildPermissions.EditNotice |
			GuildPermissions.ManageApplications |
			GuildPermissions.EditRecruitment |
			GuildPermissions.ViewOfficerNotes |
			GuildPermissions.EditOfficerNotes |
			GuildPermissions.EditPublicNotes;

		/// <summary>
		/// Permissions the seeded top rank holds: everything.
		/// </summary>
		public const GuildPermissions LeaderPermissions = GuildPermissions.All;

		/// <summary>Default display name of the seeded lowest rank.</summary>
		public const string MemberRankName = "Member";

		/// <summary>Default display name of the seeded middle rank.</summary>
		public const string OfficerRankName = "Officer";

		/// <summary>Default display name of the seeded top rank.</summary>
		public const string LeaderRankName = "Leader";

		/// <summary>
		/// The permission set a legacy <see cref="GuildRank"/> value corresponds to.
		/// </summary>
		/// <param name="rank">The legacy rank.</param>
		/// <returns>The permissions that rank used to imply.</returns>
		public static GuildPermissions PermissionsFor(GuildRank rank)
		{
			switch (rank)
			{
				case GuildRank.Leader:
					return LeaderPermissions;
				case GuildRank.Officer:
					return OfficerPermissions;
				case GuildRank.Member:
					return MemberPermissions;
				default:
					return GuildPermissions.None;
			}
		}

		/// <summary>
		/// The default display name for a legacy <see cref="GuildRank"/> value.
		/// </summary>
		/// <param name="rank">The legacy rank.</param>
		/// <returns>The default rank name.</returns>
		public static string NameFor(GuildRank rank)
		{
			switch (rank)
			{
				case GuildRank.Leader:
					return LeaderRankName;
				case GuildRank.Officer:
					return OfficerRankName;
				case GuildRank.Member:
					return MemberRankName;
				default:
					return string.Empty;
			}
		}

		/// <summary>
		/// The permission set for a rank order in a guild that has never edited its ranks.
		/// </summary>
		/// <param name="rankOrder">The rank order byte from <c>character_guild.rank</c>.</param>
		/// <returns>The seeded permissions for that order.</returns>
		/// <remarks>
		/// The FALLBACK only. Live checks read the guild's own rank rows; this exists for the
		/// seeding path and for the narrow window in which a guild's rows have not been written
		/// yet — during which it is safer to reproduce the historical behaviour than to grant
		/// nothing (which would soft-lock the guild) or everything (which would not be a
		/// permission system).
		/// </remarks>
		public static GuildPermissions SeededPermissionsFor(byte rankOrder)
		{
			if (rankOrder >= DefaultLeaderRankOrder)
			{
				return LeaderPermissions;
			}
			if (rankOrder == OfficerRankOrder)
			{
				return OfficerPermissions;
			}
			if (rankOrder >= MinRankOrder)
			{
				return MemberPermissions;
			}
			return GuildPermissions.None;
		}

		/// <summary>
		/// Trims and caps a proposed rank name, and reports whether anything usable is left.
		/// </summary>
		/// <param name="proposed">The requested name.</param>
		/// <param name="sanitized">The name to store.</param>
		/// <returns>True when the name is acceptable.</returns>
		/// <remarks>
		/// Rank names are rendered in UI Toolkit labels with <c>enableRichText = false</c>, so a
		/// tag in the string is shown, not obeyed. The character filter here is therefore not
		/// about markup — it is about a rank called <c>"‮"</c> reversing the layout of every
		/// row it appears in, and about a name made of spaces that renders as a blank column.
		/// </remarks>
		public static bool TrySanitizeRankName(string proposed, out string sanitized)
		{
			sanitized = string.Empty;

			if (string.IsNullOrWhiteSpace(proposed))
			{
				return false;
			}

			string trimmed = proposed.Trim();
			if (trimmed.Length > MaxRankNameLength)
			{
				trimmed = trimmed.Substring(0, MaxRankNameLength);
			}

			// Rebuilt character by character rather than regex-filtered: this runs on the server's
			// ingress path and a rank rename is not worth a regex engine.
			char[] buffer = new char[trimmed.Length];
			int length = 0;
			for (int i = 0; i < trimmed.Length; ++i)
			{
				char c = trimmed[i];
				if (char.IsLetterOrDigit(c) || c == ' ' || c == '\'' || c == '-')
				{
					buffer[length++] = c;
				}
			}

			sanitized = new string(buffer, 0, length).Trim();
			return sanitized.Length > 0;
		}
	}
}
