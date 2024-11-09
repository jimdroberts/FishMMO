using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct GuildCreateBroadcast : IBroadcast
	{
		public string GuildName;
	}

	public struct GuildInviteBroadcast : IBroadcast
	{
		public long InviterCharacterID;
		public long TargetCharacterID;
	}

	public struct GuildAcceptInviteBroadcast : IBroadcast
	{
	}
	public struct GuildDeclineInviteBroadcast : IBroadcast
	{
	}

	public struct GuildAddBroadcast : IBroadcast
	{
		public long GuildID;
		public long CharacterID;
		public GuildRank Rank;
		public string Location;
	}

	public struct GuildAddMultipleBroadcast : IBroadcast
	{
		public List<GuildAddBroadcast> Members;
	}

	public struct GuildLeaveBroadcast : IBroadcast
	{
	}

	public struct GuildRemoveBroadcast : IBroadcast
	{
		public long GuildMemberID;
	}

	public struct GuildChangeRankBroadcast : IBroadcast
	{
		public long GuildMemberID;
		public GuildRank Rank;
	}

	public enum GuildResultType : byte
	{
		Success = 0,
		InvalidGuildName,
		NameAlreadyExists,
		AlreadyInGuild,
	}

	public struct GuildResultBroadcast : IBroadcast
	{
		public GuildResultType Result;
	}
}