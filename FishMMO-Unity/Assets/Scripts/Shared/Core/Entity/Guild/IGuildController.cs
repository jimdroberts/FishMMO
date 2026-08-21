using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for a character's guild controller, handling guild membership and events.
	/// </summary>
	/// <remarks>
	/// The controller's copy of the character's standing (<see cref="RankOrder"/>,
	/// <see cref="Permissions"/>) is a CACHE, on both sides of the wire. On the client it drives
	/// what the panel offers; on the server it is a cheap pre-filter that keeps obviously-illegal
	/// requests from reaching the database. Neither is the authority — every guild operation
	/// re-reads the guild's rank rows inside its async path and decides there. See
	/// <c>GuildSystem.ResolveGuildAuthorityAsync</c>.
	/// </remarks>
	public interface IGuildController : ICharacterBehaviour
	{
		/// <summary>
		/// Static event for reading a guild ID and player character.
		/// </summary>
		static Action<long, IPlayerCharacter> OnReadID;

		/// <summary>
		/// Event triggered when a guild invite is received.
		/// </summary>
		event Action<long> OnReceiveGuildInvite;
		/// <summary>
		/// Event triggered when a guild member is added or their roster row changes.
		/// </summary>
		/// <remarks>
		/// Carries the whole broadcast rather than an argument per column. The roster row grew a
		/// level, two notes and a last-seen stamp during E3/E6, and an <c>Action</c> of eight
		/// positional arguments is a signature nobody can call correctly twice — the third
		/// <c>long</c> and the fourth are one transposition away from a silent bug.
		/// </remarks>
		event Action<GuildAddBroadcast> OnAddGuildMember;
		/// <summary>
		/// Event triggered to validate guild members.
		/// </summary>
		event Action<HashSet<long>> OnValidateGuildMembers;
		/// <summary>
		/// Event triggered when a guild member is removed.
		/// </summary>
		event Action<long> OnRemoveGuildMember;
		/// <summary>
		/// Event triggered when leaving a guild.
		/// </summary>
		event Action OnLeaveGuild;
		/// <summary>
		/// Event triggered when a guild result is received.
		/// </summary>
		event Action<GuildResultType> OnReceiveGuildResult;

		/// <summary>
		/// Event triggered when the guild's descriptive text arrives.
		/// Parameters: guild ID, name, notice, message of the day.
		/// </summary>
		event Action<long, string, string, string> OnReceiveGuildInfo;

		/// <summary>
		/// Event triggered when the guild's recent activity log arrives.
		/// Parameters: guild ID, entries (newest first).
		/// </summary>
		event Action<long, GuildLogEntry[]> OnReceiveGuildLog;

		/// <summary>
		/// Event triggered when the guild's rank ladder arrives.
		/// </summary>
		event Action<GuildRankListBroadcast> OnReceiveGuildRanks;

		/// <summary>
		/// Event triggered when the guild's own recruitment advertisement arrives.
		/// </summary>
		event Action<GuildRecruitmentInfoBroadcast> OnReceiveGuildRecruitmentInfo;

		/// <summary>
		/// Event triggered when a page of the recruitment directory arrives.
		/// </summary>
		event Action<GuildDirectoryEntry[]> OnReceiveGuildDirectory;

		/// <summary>
		/// Event triggered when the guild's pending application queue arrives.
		/// </summary>
		event Action<GuildApplicationEntry[]> OnReceiveGuildApplications;

		/// <summary>
		/// The unique guild ID.
		/// </summary>
		long ID { get; set; }

		/// <summary>
		/// The character's position on the guild's rank ladder. Zero means "not in a guild".
		/// </summary>
		/// <remarks>
		/// Higher is more senior. This is the same byte <c>character_guild.rank</c> has always
		/// stored, which is why the move to editable ranks rewrote no membership row.
		/// </remarks>
		byte RankOrder { get; set; }

		/// <summary>
		/// The permissions the character's rank currently holds, as computed by the server.
		/// </summary>
		/// <remarks>
		/// Sent, not derived. The client could look its own rank up in the ladder it was given and
		/// compute this itself, but then two independent implementations would decide what a
		/// player may do — and the one that is wrong would be the one drawing the buttons.
		/// </remarks>
		GuildPermissions Permissions { get; set; }

		/// <summary>
		/// The highest rank order that exists in this guild — the leader's seat.
		/// </summary>
		/// <remarks>
		/// Needed to answer "am I the leader?" without hard-coding a number, now that a guild may
		/// add ranks above the seeded three.
		/// </remarks>
		byte LeaderRankOrder { get; set; }

		/// <summary>
		/// Convenience test for a single permission on the cached mask.
		/// </summary>
		/// <param name="permission">The permission to test.</param>
		/// <returns>True when the cached mask holds every bit in <paramref name="permission"/>.</returns>
		/// <remarks>
		/// Presentation and pre-filtering only. A server-side decision must go through the guild's
		/// rank rows; this reads a value that may be one broadcast out of date.
		/// </remarks>
		bool HasGuildPermission(GuildPermissions permission);

		/// <summary>
		/// Triggers invoked when the character joins a guild.
		/// </summary>
		List<Trigger> OnGuildJoinTriggers { get; }
		/// <summary>
		/// Triggers invoked when the character leaves a guild.
		/// </summary>
		List<Trigger> OnGuildLeaveTriggers { get; }
	}
}
