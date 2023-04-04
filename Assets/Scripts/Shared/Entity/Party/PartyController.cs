using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;

/// <summary>
/// Character party controller.
/// </summary>
[RequireComponent(typeof(Character))]
public class PartyController : NetworkBehaviour
{
	public Character character;

	public PartyRank rank = PartyRank.None;
	public Party current;

	public delegate void PartyEvent();
	public event PartyEvent OnPartyCreated;
	public event PartyEvent OnLeaveParty;

	public delegate void PartyMemberEvent(int partyMemberId, PartyRank rank);
	public event PartyMemberEvent OnAddMember;
	public event PartyMemberEvent OnRemoveMember;

	public delegate void PartyAcceptEvent(List<int> partyMemberIds);
	public event PartyAcceptEvent OnPartyInviteAccepted;

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (character == null || !base.IsOwner)
		{
			enabled = false;
			return;
		}

		ClientManager.RegisterBroadcast<PartyCreateBroadcast>(OnClientPartyCreateBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyInviteBroadcast>(OnClientPartyInviteBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyJoinedBroadcast>(OnClientPartyJoinedBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyNewMemberBroadcast>(OnClientPartyNewMemberBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyRemoveBroadcast>(OnClientPartyRemoveBroadcastReceived);
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<PartyCreateBroadcast>(OnClientPartyCreateBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyInviteBroadcast>(OnClientPartyInviteBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyJoinedBroadcast>(OnClientPartyJoinedBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyNewMemberBroadcast>(OnClientPartyNewMemberBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyRemoveBroadcast>(OnClientPartyRemoveBroadcastReceived);
		}
	}

	/// <summary>
	/// When the server successfully creates the characters party.
	/// </summary>
	public void OnClientPartyCreateBroadcastReceived(PartyCreateBroadcast msg)
	{
		Party newParty = new Party(msg.partyId, this);
		current = newParty;
		rank = PartyRank.Leader;

		OnPartyCreated?.Invoke();
	}

	/// <summary>
	/// When the character receives an invitation to join a party.
	/// *Note* msg.targetClientId should be our own ClientId but it doesn't matter if it changes. Server has authority.
	/// </summary>
	public void OnClientPartyInviteBroadcastReceived(PartyInviteBroadcast msg)
	{
		ClientManager.Broadcast(new PartyAcceptInviteBroadcast());// instant party accept, temp for testing
		//ClientManager.Broadcast(new PartyDeclineInviteBroadcast());// instant decline invite, temp for testing

		// display invitation popup
	}

	/// <summary>
	/// When the character successfully joins a party.
	/// </summary>
	public void OnClientPartyJoinedBroadcastReceived(PartyJoinedBroadcast msg)
	{
		// update our party list with the party!
		OnPartyInviteAccepted?.Invoke(msg.members);
	}

	/// <summary>
	/// When we add a new party member to the party.
	/// </summary>
	public void OnClientPartyNewMemberBroadcastReceived(PartyNewMemberBroadcast msg)
	{
		// update our party list with the new party member
		OnAddMember?.Invoke(msg.newMemberClientId, msg.rank);
	}

	/// <summary>
	/// When our local client leaves the party.
	/// </summary>
	public void OnClientPartyLeaveBroadcastReceived(PartyLeaveBroadcast msg)
	{
		rank = PartyRank.None;
		current = null;

		OnLeaveParty?.Invoke();
	}

	/// <summary>
	/// When we need to remove a party member.
	/// </summary>
	public void OnClientPartyRemoveBroadcastReceived(PartyRemoveBroadcast msg)
	{
		if (current != null)
		{
			PartyController removedMember = current.RemoveMember(msg.memberId);
			OnRemoveMember?.Invoke(msg.memberId, PartyRank.None);
		}
	}
}