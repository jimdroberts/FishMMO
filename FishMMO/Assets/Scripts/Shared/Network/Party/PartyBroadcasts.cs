using FishNet.Broadcast;
using System.Collections.Generic;

public struct PartyCreateBroadcast : IBroadcast
{
	public long ID;
	public string location;
}

public struct PartyInviteBroadcast : IBroadcast
{
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
	public long memberID;
	public PartyRank rank;
	public string location;
}

public struct PartyUpdateMemberBroadcast : IBroadcast
{
	public long memberID;
	public PartyRank rank;
	public string location;
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