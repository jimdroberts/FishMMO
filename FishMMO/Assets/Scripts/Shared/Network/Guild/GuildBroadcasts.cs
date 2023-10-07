using FishNet.Broadcast;
using System.Collections.Generic;

public struct GuildCreateBroadcast : IBroadcast
{
	public long guildID;
	public string guildName;
	public string location;
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

public struct GuildNewMemberBroadcast : IBroadcast
{
	public long guildID;
	public long characterID;
	public GuildRank rank;
	public string location;
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