using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct GuildCreateBroadcast : IBroadcast
	{
		public string guildName;
	}

	public struct GuildInviteBroadcast : IBroadcast
	{
		public long inviterCharacterID;
		public long targetCharacterID;
	}

	public struct GuildAcceptInviteBroadcast : IBroadcast
	{
	}
	public struct GuildDeclineInviteBroadcast : IBroadcast
	{
	}

	public struct GuildAddBroadcast : IBroadcast
	{
		public long guildID;
		public long characterID;
		public GuildRank rank;
		public string location;
	}

	public struct GuildAddMultipleBroadcast : IBroadcast
	{
		public List<GuildAddBroadcast> members;
	}

	public struct GuildLeaveBroadcast : IBroadcast
	{
	}

	public struct GuildRemoveBroadcast : IBroadcast
	{
		public List<long> members;
	}
}