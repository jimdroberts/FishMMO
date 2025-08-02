using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controller for managing party membership, invites, and rank for a character. Handles network broadcasts and event invocation.
	/// </summary>
	public class PartyController : CharacterBehaviour, IPartyController
	{
		/// <summary>
		/// Event triggered when a party is created. Provides the party name/location.
		/// </summary>
		public event Action<string> OnPartyCreated;

		/// <summary>
		/// Event triggered when a party invite is received. Provides the inviter's ID.
		/// </summary>
		public event Action<long> OnReceivePartyInvite;

		/// <summary>
		/// Event triggered when a party member is added. Provides member ID, rank, and health percent.
		/// </summary>
		public event Action<long, PartyRank, float> OnAddPartyMember;

		/// <summary>
		/// Event triggered to validate the current set of party members.
		/// </summary>
		public event Action<HashSet<long>> OnValidatePartyMembers;

		/// <summary>
		/// Event triggered when a party member is removed. Provides member ID.
		/// </summary>
		public event Action<long> OnRemovePartyMember;

		/// <summary>
		/// Event triggered when the character leaves the party.
		/// </summary>
		public event Action OnLeaveParty;

		/// <summary>
		/// The unique ID of the party or party member.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// The rank of the character within the party (e.g., leader, member).
		/// </summary>
		public PartyRank Rank { get; set; }

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts. Registers broadcast listeners for party events if owner.
		/// </summary>
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

		/// <summary>
		/// Called when the character stops. Unregisters broadcast listeners for party events if owner.
		/// </summary>
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
		/// Handles broadcast when the server successfully creates the character's party.
		/// Sets party ID and rank, then invokes OnPartyCreated.
		/// </summary>
		/// <param name="msg">The broadcast message containing party info.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPartyCreateBroadcastReceived(PartyCreateBroadcast msg, Channel channel)
		{
			ID = msg.PartyID;
			Rank = PartyRank.Leader;

			OnPartyCreated?.Invoke(msg.Location);
		}

		/// <summary>
		/// Handles broadcast when the character receives an invitation to join a party.
		/// </summary>
		/// <param name="msg">The broadcast message containing inviter info.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPartyInviteBroadcastReceived(PartyInviteBroadcast msg, Channel channel)
		{
			OnReceivePartyInvite?.Invoke(msg.InviterCharacterID);
		}

		/// <summary>
		/// Handles broadcast when a new party member is added.
		/// Updates local party ID and rank if the member is the local character.
		/// </summary>
		/// <param name="msg">The broadcast message containing member info.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPartyAddBroadcastReceived(PartyAddBroadcast msg, Channel channel)
		{
			// If this is our own character, update party ID and rank.
			if (PlayerCharacter != null && msg.CharacterID == Character.ID)
			{
				ID = msg.PartyID;
				Rank = msg.Rank;
			}

			OnAddPartyMember?.Invoke(msg.CharacterID, msg.Rank, msg.HealthPCT);
		}

		/// <summary>
		/// Handles broadcast when multiple party members are added.
		/// Validates the new set of party members and invokes add for each.
		/// </summary>
		/// <param name="msg">The broadcast message containing multiple members.</param>
		/// <param name="channel">The network channel.</param>
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
		/// Handles broadcast when the local client leaves the party.
		/// Resets party ID and rank, then invokes OnLeaveParty.
		/// </summary>
		/// <param name="msg">The broadcast message for leaving party.</param>
		/// <param name="channel">The network channel.</param>
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
		/// Handles broadcast when a party member is removed.
		/// Invokes OnRemovePartyMember event.
		/// </summary>
		/// <param name="msg">The broadcast message for removing member.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPartyRemoveBroadcastReceived(PartyRemoveBroadcast msg, Channel channel)
		{
			OnRemovePartyMember?.Invoke(msg.MemberID);
		}
#endif
	}
}