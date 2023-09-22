using FishNet.Broadcast;
using System.Collections.Generic;

public struct GuildCreateBroadcast : IBroadcast
{
	public string guildID;
}

public struct GuildInviteBroadcast : IBroadcast
{
	public long targetCharacterID;
}

public struct GuildAcceptInviteBroadcast : IBroadcast
{
}
public struct GuildDeclineInviteBroadcast : IBroadcast
{
}

public struct GuildJoinedBroadcast : IBroadcast
{
	public List<long> members;
}

public struct GuildNewMemberBroadcast : IBroadcast
{
	public long memberID;
	public GuildRank rank;
}

public struct GuildUpdateMemberBroadcast : IBroadcast
{
	public long memberID;
	public GuildRank rank;
}

public struct GuildLeaveBroadcast : IBroadcast
{
}

public struct GuildRemoveBroadcast : IBroadcast
{
	public long memberID;
}