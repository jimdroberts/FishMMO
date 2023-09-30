using FishNet.Broadcast;
using System.Collections.Generic;

public struct GuildCreateBroadcast : IBroadcast
{
	public long ID;
	public string guildName;
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

public struct GuildAddBroadcast : IBroadcast
{
	public List<GuildNewMemberBroadcast> members;

}
public struct GuildRemoveBroadcast : IBroadcast
{
	public List<long> members;
}