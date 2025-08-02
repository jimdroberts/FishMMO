using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for a character's guild controller, handling guild membership and events.
	/// </summary>
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
		/// Event triggered when a guild member is added.
		/// </summary>
		event Action<long, long, GuildRank, string> OnAddGuildMember;
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
		/// The unique guild ID.
		/// </summary>
		long ID { get; set; }
		/// <summary>
		/// The rank of the character in the guild.
		/// </summary>
		GuildRank Rank { get; set; }
	}
}