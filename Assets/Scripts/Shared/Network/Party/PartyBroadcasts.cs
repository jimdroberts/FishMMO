using FishNet.Broadcast;
using System.Collections.Generic;

public struct PartyCreateBroadcast : IBroadcast
{
	public ulong partyId;
}

public struct PartyInviteBroadcast : IBroadcast
{
	public int targetClientId;
}

public struct PartyAcceptInviteBroadcast : IBroadcast
{
}
public struct PartyDeclineInviteBroadcast : IBroadcast
{
}

public struct PartyJoinedBroadcast : IBroadcast
{
	public List<int> members;
}

public struct PartyNewMemberBroadcast : IBroadcast
{
	public int newMemberClientId;
	public PartyRank rank;
}

public struct PartyLeaveBroadcast : IBroadcast
{
}

public struct PartyRemoveBroadcast : IBroadcast
{
	public int memberId;
}