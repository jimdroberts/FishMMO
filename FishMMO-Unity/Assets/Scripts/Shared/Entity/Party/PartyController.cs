using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character party controller.
	/// </summary>
	public class PartyController : CharacterBehaviour, IPartyController
	{
		public event Action<string> OnPartyCreated;
		public event Action<long> OnReceivePartyInvite;
		public event Action<long, PartyRank, float> OnAddPartyMember;
		public event Action<HashSet<long>> OnValidatePartyMembers;
		public event Action<long> OnRemovePartyMember;
		public event Action OnLeaveParty;

		public long ID { get ; set;}
		public PartyRank Rank { get; set; }

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
			ClientManager.RegisterBroadcast<PartyAddMultipleBroadcast>(OnClientPartyAddMultipleBroadcastReceived);
			ClientManager.RegisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
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
				ClientManager.UnregisterBroadcast<PartyAddMultipleBroadcast>(OnClientPartyAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<PartyLeaveBroadcast>(OnClientPartyLeaveBroadcastReceived);
				ClientManager.UnregisterBroadcast<PartyRemoveBroadcast>(OnClientPartyRemoveBroadcastReceived);
			}
		}

		/// <summary>
		/// When the server successfully creates the characters party.
		/// </summary>
		public void OnClientPartyCreateBroadcastReceived(PartyCreateBroadcast msg, Channel channel)
		{
			ID = msg.PartyID;
			Rank = PartyRank.Leader;

			OnPartyCreated?.Invoke(msg.Location);
		}

		/// <summary>
		/// When the character receives an invitation to join a party.
		/// *Note* msg.targetClientID should be our own ClientId but it doesn't matter if it changes. Server has authority.
		/// </summary>
		public void OnClientPartyInviteBroadcastReceived(PartyInviteBroadcast msg, Channel channel)
		{
			OnReceivePartyInvite?.Invoke(msg.InviterCharacterID);
		}

		/// <summary>
		/// When we add a new party member to the party.
		/// </summary>
		public void OnClientPartyAddBroadcastReceived(PartyAddBroadcast msg, Channel channel)
		{
			// if this is our own id
			if (PlayerCharacter != null && msg.CharacterID == Character.ID)
			{
				ID = msg.PartyID;
				Rank = msg.Rank;
			}

			OnAddPartyMember?.Invoke(msg.CharacterID, msg.Rank, msg.HealthPCT);
		}

		/// <summary>
		/// When we need to add party members.
		/// </summary>
		public void OnClientPartyAddMultipleBroadcastReceived(PartyAddMultipleBroadcast msg, Channel channel)
		{
			var newIds = msg.Members.Select(x => x.CharacterID).ToHashSet();

			OnValidatePartyMembers?.Invoke(newIds);

			foreach (PartyAddBroadcast subMsg in msg.Members)
			{
				OnClientPartyAddBroadcastReceived(subMsg, channel);
			}
		}

		/// <summary>
		/// When our local client leaves the party.
		/// </summary>
		public void OnClientPartyLeaveBroadcastReceived(PartyLeaveBroadcast msg, Channel channel)
		{
			if (PlayerCharacter == null)
			{
				return;
			}
			ID = 0;
			Rank = PartyRank.None;
			OnLeaveParty?.Invoke();
		}

		/// <summary>
		/// When we need to remove party members.
		/// </summary>
		public void OnClientPartyRemoveBroadcastReceived(PartyRemoveBroadcast msg, Channel channel)
		{
			OnRemovePartyMember?.Invoke(msg.MemberID);
		}
#endif
	}
}