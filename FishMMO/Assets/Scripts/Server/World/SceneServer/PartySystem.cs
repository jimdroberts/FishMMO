using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;

namespace FishMMO.Server
{
	/// <summary>
	/// Server party system.
	/// </summary>
	public class PartySystem : ServerBehaviour
	{
		public CharacterSystem CharacterSystem;

		public ulong nextPartyId = 0;
		private Dictionary<ulong, Party> parties = new Dictionary<ulong, Party>();

		// clientId / partyId
		private readonly Dictionary<long, ulong> pendingInvitations = new Dictionary<long, ulong>();

		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived);
				ServerManager.UnregisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived);
				ServerManager.UnregisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived);
			}
		}

		public void OnServerPartyCreateBroadcastReceived(NetworkConnection conn, PartyCreateBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			PartyController partyController = conn.FirstObject.GetComponent<PartyController>();
			if (partyController == null || partyController.Current != null)
			{
				// already in a party
				return;
			}

			ulong partyId = ++nextPartyId;
			// this should never happen but check it anyway so we never duplicate party ids
			while (parties.ContainsKey(partyId))
			{
				partyId = ++nextPartyId;
			}

			Party newParty = new Party(partyId, partyController);
			parties.Add(newParty.ID, newParty);
			partyController.Rank = PartyRank.Leader;
			partyController.Current = newParty;

			// tell the Character we made their party successfully
			conn.Broadcast(new PartyCreateBroadcast() { partyId = newParty.ID });
		}

		public void OnServerPartyInviteBroadcastReceived(NetworkConnection conn, PartyInviteBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			PartyController leaderPartyController = conn.FirstObject.GetComponent<PartyController>();

			// validate party leader
			if (leaderPartyController == null ||
				leaderPartyController.Current == null ||
				leaderPartyController.Rank != PartyRank.Leader ||
				leaderPartyController.Current.IsFull)
			{
				return;
			}

			if (!pendingInvitations.ContainsKey(msg.targetCharacterId) &&
				CharacterSystem.CharactersById.TryGetValue(msg.targetCharacterId, out Character targetCharacter))
			{
				PartyController targetPartyController = targetCharacter.GetComponent<PartyController>();

				// validate target
				if (targetPartyController == null || targetPartyController.Current != null)
				{
					// already in party
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a party invite
				pendingInvitations.Add(msg.targetCharacterId, leaderPartyController.Current.ID);
				targetCharacter.Owner.Broadcast(new PartyInviteBroadcast() { targetCharacterId = leaderPartyController.Character.ID });
			}
		}

		public void OnServerPartyAcceptInviteBroadcastReceived(NetworkConnection conn, PartyAcceptInviteBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			PartyController partyController = conn.FirstObject.GetComponent<PartyController>();

			// validate Character
			if (partyController == null)
			{
				return;
			}

			// validate party invite
			if (pendingInvitations.TryGetValue(partyController.Character.ID, out ulong pendingPartyId))
			{
				pendingInvitations.Remove(partyController.Character.ID);

				if (parties.TryGetValue(pendingPartyId, out Party party) && !party.IsFull)
				{
					List<long> CurrentMembers = new List<long>();

					PartyNewMemberBroadcast newMember = new PartyNewMemberBroadcast()
					{
						newMemberName = partyController.Character.name,
						rank = PartyRank.Member,
					};

					for (int i = 0; i < party.Members.Count; ++i)
					{
						// tell our party members we joined the party
						party.Members[i].Owner.Broadcast(newMember);
						CurrentMembers.Add(party.Members[i].Character.ID);
					}

					partyController.Rank = PartyRank.Member;
					partyController.Current = party;

					// add the new party member
					party.Members.Add(partyController);

					// tell the new member about they joined successfully
					PartyJoinedBroadcast memberBroadcast = new PartyJoinedBroadcast()
					{
						members = CurrentMembers,
					};
					conn.Broadcast(memberBroadcast);
				}
			}
		}

		public void OnServerPartyDeclineInviteBroadcastReceived(NetworkConnection conn, PartyDeclineInviteBroadcast msg)
		{
			// do we need to validate?
			pendingInvitations.Remove(conn.ClientId);
		}

		public void OnServerPartyLeaveBroadcastReceived(NetworkConnection conn, PartyLeaveBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			PartyController partyController = conn.FirstObject.GetComponent<PartyController>();

			// validate Character
			if (partyController == null || partyController.Current == null)
			{
				// not in a party..
				return;
			}

			// validate party
			if (parties.TryGetValue(partyController.Current.ID, out Party party))
			{
				if (partyController.Rank == PartyRank.Leader)
				{
					// can we destroy the party?
					if (party.Members.Count - 1 < 1)
					{
						party.Members.Clear();
						parties.Remove(party.ID);

						partyController.Rank = PartyRank.None;
						partyController.Current = null;

						// tell Character they left the party successfully
						conn.Broadcast(new PartyLeaveBroadcast());
						return;
					}
					else
					{
						// next person in the party becomes the new leader
						party.Members[1].Rank = PartyRank.Leader;

						// remove the Current leader
						party.Members.RemoveAt(0);

						partyController.Rank = PartyRank.None;
						partyController.Current = null;

						// tell Character they left the party successfully
						conn.Broadcast(new PartyLeaveBroadcast());
					}
				}

				PartyRemoveBroadcast removeCharacterBroadcast = new PartyRemoveBroadcast()
				{
					memberName = partyController.Character.CharacterName,
				};

				// tell the remaining party members we left the party
				foreach (PartyController member in party.Members)
				{
					member.Owner.Broadcast(removeCharacterBroadcast);
				}
			}
		}

		public void OnServerPartyRemoveBroadcastReceived(NetworkConnection conn, PartyRemoveBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			PartyController partyController = conn.FirstObject.GetComponent<PartyController>();

			// validate Character
			if (partyController == null ||
				partyController.Current == null ||
				partyController.Rank != PartyRank.Leader ||
				partyController.Character.CharacterName.Equals(msg.memberName)) // we can't kick ourself
			{
				return;
			}

			// validate party
			if (parties.TryGetValue(partyController.Current.ID, out Party party))
			{
				PartyController removedMember = partyController.Current.RemoveMember(msg.memberName);
				if (removedMember != null)
				{
					removedMember.Rank = PartyRank.None;
					removedMember.Current = null;

					PartyRemoveBroadcast removeCharacterBroadcast = new PartyRemoveBroadcast()
					{
						memberName = msg.memberName,
					};

					// tell the remaining party members someone was removed
					foreach (PartyController member in party.Members)
					{
						member.Owner.Broadcast(removeCharacterBroadcast);
					}
				}
			}
		}
	}
}