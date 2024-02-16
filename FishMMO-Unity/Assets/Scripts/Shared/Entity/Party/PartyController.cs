using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character party controller.
	/// </summary>
	public class PartyController : CharacterBehaviour
	{
		public long ID;
		public PartyRank Rank = PartyRank.None;

		public Action<string> OnPartyCreated;
		public Action<long> OnReceivePartyInvite;
		public Action<long, PartyRank, float> OnAddPartyMember;
		public Action<HashSet<long>> OnValidatePartyMembers;
		public Action<long> OnRemovePartyMember;
		public Action OnLeaveParty;

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

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

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

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

			OnPartyCreated?.Invoke(msg.location);
		}

		/// <summary>
		/// When the character receives an invitation to join a party.
		/// *Note* msg.targetClientID should be our own ClientId but it doesn't matter if it changes. Server has authority.
		/// </summary>
		public void OnClientPartyInviteBroadcastReceived(PartyInviteBroadcast msg, Channel channel)
		{
			OnReceivePartyInvite?.Invoke(msg.inviterCharacterID);
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

			// if this is our own id
			if (Character != null && msg.characterID == Character.ID)
			{
				ID = msg.partyID;
				Rank = msg.rank;
			}

			OnAddPartyMember?.Invoke(msg.characterID, msg.rank, msg.healthPCT);
		}

		/// <summary>
		/// When our local client leaves the party.
		/// </summary>
		public void OnClientPartyLeaveBroadcastReceived(PartyLeaveBroadcast msg, Channel channel)
		{
			ID = 0;
			Rank = PartyRank.None;

			OnLeaveParty?.Invoke();
		}

		/// <summary>
		/// When we need to add party members.
		/// </summary>
		public void OnClientPartyAddMultipleBroadcastReceived(PartyAddMultipleBroadcast msg, Channel channel)
		{
			var newIds = msg.members.Select(x => x.characterID).ToHashSet();

			OnValidatePartyMembers?.Invoke(newIds);

			foreach (PartyAddBroadcast subMsg in msg.members)
			{
				OnAddPartyMember?.Invoke(subMsg.characterID, subMsg.rank, subMsg.healthPCT);
			}
		}

		/// <summary>
		/// When we need to remove party members.
		/// </summary>
		public void OnClientPartyRemoveBroadcastReceived(PartyRemoveBroadcast msg, Channel channel)
		{
			foreach (long memberID in msg.members)
			{
				OnRemovePartyMember?.Invoke(memberID);
			}
		}
#endif
	}
}