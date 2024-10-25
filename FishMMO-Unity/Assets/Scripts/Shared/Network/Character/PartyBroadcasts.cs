using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct PartyCreateBroadcast : IBroadcast
	{
		public long PartyID;
		public string Location;
	}

	public struct PartyInviteBroadcast : IBroadcast
	{
		public long InviterCharacterID;
		public long TargetCharacterID;
	}

	public struct PartyAcceptInviteBroadcast : IBroadcast
	{
	}
	public struct PartyDeclineInviteBroadcast : IBroadcast
	{
	}

	public struct PartyAddBroadcast : IBroadcast
	{
		public long PartyID;
		public long CharacterID;
		public PartyRank Rank;
		public float HealthPCT;
	}

	public struct PartyAddMultipleBroadcast : IBroadcast
	{
		public List<PartyAddBroadcast> Members;
	}

	public struct PartyLeaveBroadcast : IBroadcast
	{
	}

	public struct PartyRemoveBroadcast : IBroadcast
	{
		public long MemberID;
	}

	public struct PartyChangeRankBroadcast : IBroadcast
	{
		public long MemberID;
	}
}