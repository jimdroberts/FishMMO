using FishNet.Broadcast;
using System.Collections.Generic;

public struct PartyCreateBroadcast : IBroadcast
{
	public ulong partyId;
}

public struct PartyInviteBroadcast : IBroadcast
{
	public long targetCharacterId;
}

public struct PartyAcceptInviteBroadcast : IBroadcast
{
}
public struct PartyDeclineInviteBroadcast : IBroadcast
{
}

public struct PartyJoinedBroadcast : IBroadcast
{
	public List<long> members;
}

public struct PartyNewMemberBroadcast : IBroadcast
{
	public string newMemberName;
	public PartyRank rank;
}

public struct PartyLeaveBroadcast : IBroadcast
{
}

public struct PartyRemoveBroadcast : IBroadcast
{
	public string memberName;
}