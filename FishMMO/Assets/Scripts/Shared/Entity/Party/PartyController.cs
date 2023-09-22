using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;

/// <summary>
/// Character party controller.
/// </summary>
[RequireComponent(typeof(Character))]
public class PartyController : NetworkBehaviour
{
	public Character Character;

	public PartyRank Rank = PartyRank.None;
	public Party Current;

	public delegate void PartyEvent();
	public event PartyEvent OnPartyCreated;
	public event PartyEvent OnLeaveParty;

	public delegate void PartyMemberEvent(long characterID, PartyRank rank);
	public event PartyMemberEvent OnAddMember;
	public event PartyMemberEvent OnUpdateMember;
	public event PartyMemberEvent OnRemoveMember;

	public delegate void PartyAcceptEvent(List<long> partyMemberIDs);
	public event PartyAcceptEvent OnPartyInviteAccepted;

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (!base.IsOwner)
		{
			enabled = false;
			return;
		}

		ClientManager.RegisterBroadcast<PartyCreateBroadcast>(OnClientPartyCreateBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyInviteBroadcast>(OnClientPartyInviteBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyJoinedBroadcast>(OnClientPartyJoinedBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyNewMemberBroadcast>(OnClientPartyNewMemberBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyUpdateMemberBroadcast>(OnClientPartyUpdateMemberBroadcastReceived);
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
			ClientManager.UnregisterBroadcast<PartyUpdateMemberBroadcast>(OnClientPartyUpdateMemberBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyRemoveBroadcast>(OnClientPartyRemoveBroadcastReceived);
		}
	}

	/// <summary>
	/// When the server successfully creates the characters party.
	/// </summary>
	public void OnClientPartyCreateBroadcastReceived(PartyCreateBroadcast msg)
	{
		Party newParty = new Party(msg.partyID, this);
		Current = newParty;
		Rank = PartyRank.Leader;

		OnPartyCreated?.Invoke();
	}

	/// <summary>
	/// When the character receives an invitation to join a party.
	/// *Note* msg.targetClientID should be our own ClientId but it doesn't matter if it changes. Server has authority.
	/// </summary>
	public void OnClientPartyInviteBroadcastReceived(PartyInviteBroadcast msg)
	{
		if (Character.ID == msg.targetCharacterID)
		{
			return;
		}
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
		OnAddMember?.Invoke(msg.memberID, msg.rank);
	}

	/// <summary>
	/// When we need to update a party member status.
	/// </summary>
	public void OnClientPartyUpdateMemberBroadcastReceived(PartyUpdateMemberBroadcast msg)
	{
		OnUpdateMember?.Invoke(msg.memberID, msg.rank);
	}

	/// <summary>
	/// When our local client leaves the party.
	/// </summary>
	public void OnClientPartyLeaveBroadcastReceived(PartyLeaveBroadcast msg)
	{
		Rank = PartyRank.None;
		Current = null;

		OnLeaveParty?.Invoke();
	}

	/// <summary>
	/// When we need to remove a party member.
	/// </summary>
	public void OnClientPartyRemoveBroadcastReceived(PartyRemoveBroadcast msg)
	{
		if (Current != null)
		{
			Current.Members.Remove(msg.memberID);
			OnRemoveMember?.Invoke(msg.memberID, PartyRank.None);
		}
	}
}