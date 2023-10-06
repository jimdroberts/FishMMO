using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;
#if !UNITY_SERVER
using FishMMO.Client;
#endif

/// <summary>
/// Character party controller.
/// </summary>
[RequireComponent(typeof(Character))]
public class PartyController : NetworkBehaviour
{
	public Character Character;

	public long ID;
	public PartyRank Rank = PartyRank.None;
	public readonly HashSet<long> Members = new HashSet<long>();

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
		ClientManager.RegisterBroadcast<PartyNewMemberBroadcast>(OnClientPartyNewMemberBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyAddBroadcast>(OnClientPartyAddBroadcastReceived);
		ClientManager.RegisterBroadcast<PartyRemoveBroadcast>(OnClientPartyRemoveBroadcastReceived);
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<PartyCreateBroadcast>(OnClientPartyCreateBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyInviteBroadcast>(OnClientPartyInviteBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyNewMemberBroadcast>(OnClientPartyNewMemberBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyAddBroadcast>(OnClientPartyAddBroadcastReceived);
			ClientManager.UnregisterBroadcast<PartyRemoveBroadcast>(OnClientPartyRemoveBroadcastReceived);
		}
	}

	/// <summary>
	/// When the server successfully creates the characters party.
	/// </summary>
	public void OnClientPartyCreateBroadcastReceived(PartyCreateBroadcast msg)
	{
		ID = msg.ID;
		Rank = PartyRank.Leader;

#if !UNITY_SERVER
		if (UIManager.TryGet("UIParty", out UIParty uiParty))
		{
			uiParty.OnPartyCreated(msg.location);
		}
#endif
	}

	/// <summary>
	/// When the character receives an invitation to join a party.
	/// *Note* msg.targetClientID should be our own ClientId but it doesn't matter if it changes. Server has authority.
	/// </summary>
	public void OnClientPartyInviteBroadcastReceived(PartyInviteBroadcast msg)
	{
		ClientManager.Broadcast(new PartyAcceptInviteBroadcast());// instant Party accept, temp for testing
																  //ClientManager.Broadcast(new PartyDeclineInviteBroadcast());// instant decline invite, temp for testing

		// display invitation popup
	}

	/// <summary>
	/// When we add a new party member to the party.
	/// </summary>
	public void OnClientPartyNewMemberBroadcastReceived(PartyNewMemberBroadcast msg)
	{
		// update our Party list with the new Party member
#if !UNITY_SERVER
		if (UIManager.TryGet("UIParty", out UIParty uiParty))
		{
			uiParty.OnPartyAddMember(msg.memberID, msg.rank, msg.location);
		}
#endif
	}

	/// <summary>
	/// When a party members status is updated.
	/// </summary>
	public void OnClientPartyUpdateMemberBroadcastReceived(PartyUpdateMemberBroadcast msg)
	{
#if !UNITY_SERVER
		if (UIManager.TryGet("UIParty", out UIParty uiParty))
		{
			uiParty.OnPartyAddMember(msg.memberID, msg.rank, msg.location);
		}
#endif
	}

	/// <summary>
	/// When our local client leaves the party.
	/// </summary>
	public void OnClientPartyLeaveBroadcastReceived(PartyLeaveBroadcast msg)
	{
		ID = 0;
		Rank = PartyRank.None;
		Members.Clear();
#if !UNITY_SERVER
		if (UIManager.TryGet("UIParty", out UIParty uiParty))
		{
			uiParty.OnLeaveParty();
		}
#endif
	}

	/// <summary>
	/// When we need to add party members.
	/// </summary>
	public void OnClientPartyAddBroadcastReceived(PartyAddBroadcast msg)
	{
		foreach (PartyNewMemberBroadcast member in msg.members)
		{
			if (!Members.Contains(member.memberID))
			{
				Members.Add(member.memberID);
#if !UNITY_SERVER
				if (UIManager.TryGet("UIParty", out UIParty uiParty))
				{
					uiParty.OnPartyAddMember(member.memberID, member.rank, member.location);
				}
#endif
			}
		}
	}

	/// <summary>
	/// When we need to remove party members.
	/// </summary>
	public void OnClientPartyRemoveBroadcastReceived(PartyRemoveBroadcast msg)
	{
		foreach (long memberID in msg.members)
		{
			if (Members.Contains(memberID))
			{
				Members.Remove(memberID);
#if !UNITY_SERVER
				if (UIManager.TryGet("UIParty", out UIParty uiParty))
				{
					uiParty.OnPartyRemoveMember(memberID);
				}
#endif
			}
		}
	}
}