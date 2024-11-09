using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IGuildController : ICharacterBehaviour
	{
		static Action<long, IPlayerCharacter> OnReadID;

		event Action<long> OnReceiveGuildInvite;
		event Action<long, long, GuildRank, string> OnAddGuildMember;
		event Action<HashSet<long>> OnValidateGuildMembers;
		event Action<long> OnRemoveGuildMember;
		event Action OnLeaveGuild;
		event Action<GuildResultType> OnReceiveGuildResult;

		long ID { get; set; }
		GuildRank Rank { get; set; }
	}
}