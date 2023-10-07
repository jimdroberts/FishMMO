using FishNet.Broadcast;
using System.Collections.Generic;

public struct PartyCreateBroadcast : IBroadcast
{
	public long partyID;
	public string location;
}

public struct PartyInviteBroadcast : IBroadcast
{
	public long inviterCharacterID;
	public long targetCharacterID;
}

public struct PartyAcceptInviteBroadcast : IBroadcast
{
}
public struct PartyDeclineInviteBroadcast : IBroadcast
{
}

public struct PartyNewMemberBroadcast : IBroadcast
{
	public long partyID;
	public long characterID;
	public PartyRank rank;
	public float healthPCT;
}

public struct PartyLeaveBroadcast : IBroadcast
{
}

public struct PartyAddBroadcast : IBroadcast
{
	public List<PartyNewMemberBroadcast> members;

}
public struct PartyRemoveBroadcast : IBroadcast
{
	public List<long> members;
}