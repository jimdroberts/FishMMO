using UnityEngine;
using FishNet.Object;
using FishNet.Transporting;
using System.Collections.Generic;
using System.Linq;
#if !UNITY_SERVER
using FishMMO.Client;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Character party controller.
	/// </summary>
	[RequireComponent(typeof(Character))]
	public class PartyController : NetworkBehaviour
	{
		public Character Character;

		public long ID;
		public PartyRank Rank = PartyRank.None;

#if !UNITY_SERVER
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
			ClientManager.RegisterBroadcast<PartyAddBroadcast>(OnClientPartyAddBroadcastReceived);
			ClientManager.RegisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
			ClientManager.RegisterBroadcast<PartyAddMultipleBroadcast>(OnClientPartyAddMultipleBroadcastReceived);
			ClientManager.RegisterBroadcast<PartyRemoveBroadcast>(OnClientPartyRemoveBroadcastReceived);
		}

		public override void OnStopClient()
		{
			base.OnStopClient();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<PartyCreateBroadcast>(OnClientPartyCreateBroadcastReceived);
				ClientManager.UnregisterBroadcast<PartyInviteBroadcast>(OnClientPartyInviteBroadcastReceived);
				ClientManager.UnregisterBroadcast<PartyAddBroadcast>(OnClientPartyAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
				ClientManager.UnregisterBroadcast<PartyAddMultipleBroadcast>(OnClientPartyAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<PartyRemoveBroadcast>(OnClientPartyRemoveBroadcastReceived);
			}
		}

		/// <summary>
		/// When the server successfully creates the characters party.
		/// </summary>
		public void OnClientPartyCreateBroadcastReceived(PartyCreateBroadcast msg, Channel channel)
		{
			ID = msg.partyID;
			Rank = PartyRank.Leader;
			if (UIManager.TryGet("UIParty", out UIParty uiParty))
			{
				uiParty.OnPartyCreated(msg.location);
			}
		}

		/// <summary>
		/// When the character receives an invitation to join a party.
		/// *Note* msg.targetClientID should be our own ClientId but it doesn't matter if it changes. Server has authority.
		/// </summary>
		public void OnClientPartyInviteBroadcastReceived(PartyInviteBroadcast msg, Channel channel)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.inviterCharacterID, (n) =>
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s party. Would you like to join?",
					() =>
					{
						ClientManager.Broadcast(new PartyAcceptInviteBroadcast(), Channel.Reliable);
					},
					() =>
					{
						ClientManager.Broadcast(new PartyDeclineInviteBroadcast(), Channel.Reliable);
					});
				}
			});
		}

		/// <summary>
		/// When we add a new party member to the party.
		/// </summary>
		public void OnClientPartyAddBroadcastReceived(PartyAddBroadcast msg, Channel channel)
		{
			// update our Party list with the new Party member
			if (Character == null)
			{
				return;
			}

			if (!UIManager.TryGet("UIParty", out UIParty uiParty))
			{
				return;
			}

			// if this is our own id
			if (Character != null && msg.characterID == Character.ID.Value)
			{
				ID = msg.partyID;
				Rank = msg.rank;
			}
			uiParty.OnPartyAddMember(msg.characterID, msg.rank, msg.healthPCT);
		}

		/// <summary>
		/// When our local client leaves the party.
		/// </summary>
		public void OnClientPartyLeaveBroadcastReceived(PartyLeaveBroadcast msg, Channel channel)
		{
			ID = 0;
			Rank = PartyRank.None;

			if (UIManager.TryGet("UIParty", out UIParty uiParty))
			{
				uiParty.OnLeaveParty();
			}
		}

		/// <summary>
		/// When we need to add party members.
		/// </summary>
		public void OnClientPartyAddMultipleBroadcastReceived(PartyAddMultipleBroadcast msg, Channel channel)
		{
			if (UIManager.TryGet("UIParty", out UIParty uiParty))
			{
				var newIds = msg.members.Select(x => x.characterID).ToHashSet();
				foreach (long id in new List<long>(uiParty.Members.Keys))
				{
					if (!newIds.Contains(id))
					{
						uiParty.OnPartyRemoveMember(id);
					}
				}
				foreach (PartyAddBroadcast subMsg in msg.members)
				{
					OnClientPartyAddBroadcastReceived(subMsg, channel);
				}
			}
		}

		/// <summary>
		/// When we need to remove party members.
		/// </summary>
		public void OnClientPartyRemoveBroadcastReceived(PartyRemoveBroadcast msg, Channel channel)
		{
			if (UIManager.TryGet("UIParty", out UIParty uiParty))
			{
				foreach (long memberID in msg.members)
				{
					uiParty.OnPartyRemoveMember(memberID);
				}
			}
		}
#endif
	}
}