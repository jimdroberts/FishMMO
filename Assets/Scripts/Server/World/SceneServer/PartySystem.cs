using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;

namespace Server
{
	/// <summary>
	/// Server party system.
	/// </summary>
	public class PartySystem : ServerBehaviour
	{
		public ulong nextPartyId = 0;
		public Dictionary<ulong, Party> parties = new Dictionary<ulong, Party>();

		// clientId / partyId
		public readonly Dictionary<int, ulong> pendingInvitations = new Dictionary<int, ulong>();

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

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Started)
			{
				ServerManager.RegisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived, true);
				ServerManager.RegisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived, true);
			}
			else if (obj.ConnectionState == LocalConnectionState.Stopped)
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
			if (partyController == null || partyController.current != null)
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
			parties.Add(newParty.id, newParty);
			partyController.rank = PartyRank.Leader;
			partyController.current = newParty;

			// tell the character we made their party successfully
			conn.Broadcast(new PartyCreateBroadcast() { partyId = newParty.id });
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
				leaderPartyController.current == null ||
				leaderPartyController.rank != PartyRank.Leader ||
				leaderPartyController.current.IsFull)
			{
				return;
			}

			if (!pendingInvitations.ContainsKey(msg.targetClientId) &&
				ServerManager.Clients.TryGetValue(msg.targetClientId, out NetworkConnection targetConn))
			{
				if (targetConn.FirstObject == null)
				{
					return;
				}
				PartyController targetPartyController = targetConn.FirstObject.GetComponent<PartyController>();

				// validate target
				if (targetPartyController == null || targetPartyController.current != null)
				{
					// already in party
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a party invite
				pendingInvitations.Add(targetConn.ClientId, leaderPartyController.current.id);
				targetConn.Broadcast(new PartyInviteBroadcast() { targetClientId = targetConn.ClientId });
			}
		}

		public void OnServerPartyAcceptInviteBroadcastReceived(NetworkConnection conn, PartyAcceptInviteBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			PartyController partyController = conn.FirstObject.GetComponent<PartyController>();

			// validate character
			if (partyController == null)
			{
				return;
			}

			// validate party invite
			if (pendingInvitations.TryGetValue(conn.ClientId, out ulong pendingPartyId))
			{
				pendingInvitations.Remove(conn.ClientId);

				if (parties.TryGetValue(pendingPartyId, out Party party) && !party.IsFull)
				{
					List<int> currentMembers = new List<int>();

					PartyNewMemberBroadcast newMember = new PartyNewMemberBroadcast()
					{
						newMemberClientId = conn.ClientId,
						rank = PartyRank.Member,
					};

					for (int i = 0; i < party.members.Count; ++i)
					{
						// tell our party members we joined the party
						party.members[i].Owner.Broadcast(newMember);
						currentMembers.Add(party.members[i].OwnerId);
					}

					partyController.rank = PartyRank.Member;
					partyController.current = party;

					// add the new party member
					party.members.Add(partyController);

					// tell the new member about they joined successfully
					PartyJoinedBroadcast memberBroadcast = new PartyJoinedBroadcast()
					{
						members = currentMembers,
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

			// validate character
			if (partyController == null || partyController.current == null)
			{
				// not in a party..
				return;
			}

			// validate party
			if (parties.TryGetValue(partyController.current.id, out Party party))
			{
				if (partyController.rank == PartyRank.Leader)
				{
					// can we destroy the party?
					if (party.members.Count - 1 < 1)
					{
						party.members.Clear();
						parties.Remove(party.id);

						partyController.rank = PartyRank.None;
						partyController.current = null;

						// tell character they left the party successfully
						conn.Broadcast(new PartyLeaveBroadcast());
						return;
					}
					else
					{
						// next person in the party becomes the new leader
						party.members[1].rank = PartyRank.Leader;

						// remove the current leader
						party.members.RemoveAt(0);

						partyController.rank = PartyRank.None;
						partyController.current = null;

						// tell character they left the party successfully
						conn.Broadcast(new PartyLeaveBroadcast());
					}
				}

				PartyRemoveBroadcast removeCharacterBroadcast = new PartyRemoveBroadcast()
				{
					memberId = conn.ClientId,
				};

				// tell the remaining party members we left the party
				foreach (PartyController member in party.members)
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

			// validate character
			if (partyController == null ||
				partyController.current == null ||
				partyController.rank != PartyRank.Leader ||
				conn.ClientId == msg.memberId) // we can't kick ourself
			{
				return;
			}

			// validate party
			if (parties.TryGetValue(partyController.current.id, out Party party))
			{
				PartyController removedMember = partyController.current.RemoveMember(msg.memberId);
				if (removedMember != null)
				{
					removedMember.rank = PartyRank.None;
					removedMember.current = null;

					PartyRemoveBroadcast removeCharacterBroadcast = new PartyRemoveBroadcast()
					{
						memberId = msg.memberId,
					};

					// tell the remaining party members someone was removed
					foreach (PartyController member in party.members)
					{
						member.Owner.Broadcast(removeCharacterBroadcast);
					}
				}
			}
		}
	}
}