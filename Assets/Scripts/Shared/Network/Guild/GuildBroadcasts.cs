using FishNet.Broadcast;
using System.Collections.Generic;

public struct GuildCreateBroadcast : IBroadcast
{
	public ulong guildId;
}

public struct GuildInviteBroadcast : IBroadcast
{
	public int targetClientId;
}

public struct GuildAcceptInviteBroadcast : IBroadcast
{
}
public struct GuildDeclineInviteBroadcast : IBroadcast
{
}

public struct GuildJoinedBroadcast : IBroadcast
{
	public List<int> members;
}

public struct GuildNewMemberBroadcast : IBroadcast
{
	public int newMemberClientId;
	public GuildRank rank;
}

public struct GuildLeaveBroadcast : IBroadcast
{
}

public struct GuildRemoveBroadcast : IBroadcast
{
	public int memberId;
}