using FishNet.Broadcast;
using System.Collections.Generic;

public struct GuildCreateBroadcast : IBroadcast
{
	public string guildId;
}

public struct GuildInviteBroadcast : IBroadcast
{
	public long targetCharacterId;
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
	public long memberId;
	public GuildRank rank;
}

public struct GuildUpdateMemberBroadcast : IBroadcast
{
	public long memberId;
	public GuildRank rank;
}

public struct GuildLeaveBroadcast : IBroadcast
{
}

public struct GuildRemoveBroadcast : IBroadcast
{
	public long memberId;
}