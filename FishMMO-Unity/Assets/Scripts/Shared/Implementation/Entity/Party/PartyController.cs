using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

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
		/// Event triggered when the scene server pushes live state for the party members sharing
		/// the local character's scene. See <see cref="IPartyController.OnUpdatePartyVitals"/>.
		/// </summary>
		public event Action<PartyMemberVitalsEntry[]> OnUpdatePartyVitals;

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

		[Header("ECA - Party")]
		[Tooltip("Triggers invoked when the character joins or creates a party.")]
		[SerializeField]
		private List<Trigger> onPartyJoinTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when the character leaves a party.")]
		[SerializeField]
		private List<Trigger> onPartyLeaveTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnPartyJoinTriggers => onPartyJoinTriggers;
		/// <inheritdoc />
		public List<Trigger> OnPartyLeaveTriggers => onPartyLeaveTriggers;

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
			ClientManager.RegisterBroadcast<PartyMemberVitalsUpdateBroadcast>(OnClientPartyMemberVitalsUpdateBroadcastReceived);
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
				ClientManager.UnregisterBroadcast<PartyMemberVitalsUpdateBroadcast>(OnClientPartyMemberVitalsUpdateBroadcastReceived);
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
			Character.Invoke(onPartyJoinTriggers, new PartyEventData(Character, ID, Rank));
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
				/* The JOIN triggers fire on an actual join, not on every refresh.
				 *
				 * This handler is reached once per member of every roster payload, and the server
				 * sends a roster payload each time anything about the party changes — somebody
				 * joining, leaving, being promoted, logging in or logging out. Firing the triggers
				 * unconditionally meant "the character joined a party" was raised again for every
				 * one of those, so an achievement counting party joins climbed while the player
				 * stood still and any ECA reaction bound to joining replayed itself all evening.
				 *
				 * Rank changes are deliberately NOT a join. Being promoted is not joining, and the
				 * event data carries the rank precisely so a listener that cares can read it. */
				bool joined = ID != msg.PartyID;

				ID = msg.PartyID;
				Rank = msg.Rank;

				if (joined)
				{
					Character.Invoke(onPartyJoinTriggers, new PartyEventData(Character, ID, Rank));
				}
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
			if (msg.Members == null)
			{
				return;
			}

			HashSet<long> newIds = new HashSet<long>(msg.Members.Length);
			for (int i = 0; i < msg.Members.Length; i++)
			{
				newIds.Add(msg.Members[i].CharacterID);
			}

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

			/* Leaving twice is not leaving twice.
			 *
			 * More than one server path sends this message for the same departure — the leave or
			 * kick that caused it tells the client immediately, and the periodic roster pump tells
			 * it again when it notices the membership row is gone. Without this test the LEAVE
			 * triggers fired once for each, so an ECA reaction bound to leaving a party ran twice
			 * for a single click, and it also ran for a character that had no party to leave. */
			if (ID == 0 && Rank == PartyRank.None)
			{
				return;
			}

			ID = 0;
			Rank = PartyRank.None;
			OnLeaveParty?.Invoke();
			Character.Invoke(onPartyLeaveTriggers, new PartyEventData(Character, 0, PartyRank.None));
		}

		/// <summary>
		/// Handles broadcast when a party member is removed.
		/// Invokes OnRemovePartyMember event.
		/// </summary>
		/// <param name="msg">The broadcast message for removing member.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPartyRemoveBroadcastReceived(PartyRemoveBroadcast msg, Channel channel)
		{
			OnRemovePartyMember?.Invoke(msg.CharacterID);
		}

		/// <summary>
		/// Handles the live party vitals payload, raising it as a single event.
		/// </summary>
		/// <param name="msg">The broadcast message carrying member state.</param>
		/// <param name="channel">The network channel.</param>
		/// <remarks>
		/// Raised whole rather than split per member. The payload is complete for the recipient's
		/// scene, so a roster member missing from it is a member in another zone — a fact that
		/// only exists in the payload as a set and would be destroyed by iterating it away here.
		/// A null <c>Members</c> is normalised to an empty array so a subscriber can treat every
		/// delivery as an authoritative set without a null check of its own; it means "nobody
		/// here", which is a real answer and not a missing one.
		/// </remarks>
		public void OnClientPartyMemberVitalsUpdateBroadcastReceived(PartyMemberVitalsUpdateBroadcast msg, Channel channel)
		{
			OnUpdatePartyVitals?.Invoke(msg.Members ?? Array.Empty<PartyMemberVitalsEntry>());
		}
#endif
	}
}