using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Server
{
	/// <summary>
	/// Server party system.
	/// </summary>
	public class PartySystem : ServerBehaviour
	{
		public ulong nextPartyID = 0;
		private Dictionary<ulong, Party> parties = new Dictionary<ulong, Party>();

		// clientID / partyID
		private readonly Dictionary<long, ulong> pendingInvitations = new Dictionary<long, ulong>();

		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server.CharacterSystem != null)
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

		public void RemovePending(long id)
		{
			pendingInvitations.Remove(id);
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

			ulong partyID = ++nextPartyID;
			// this should never happen but check it anyway so we never duplicate party ids
			while (parties.ContainsKey(partyID))
			{
				partyID = ++nextPartyID;
			}

			Party newParty = new Party(partyID, partyController);
			parties.Add(newParty.ID, newParty);
			partyController.Rank = PartyRank.Leader;
			partyController.Current = newParty;

			// tell the Character we made their party successfully
			conn.Broadcast(new PartyCreateBroadcast() { partyID = newParty.ID });
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

			if (!pendingInvitations.ContainsKey(msg.targetCharacterID) &&
				Server.CharacterSystem.CharactersByID.TryGetValue(msg.targetCharacterID, out Character targetCharacter))
			{
				PartyController targetPartyController = targetCharacter.GetComponent<PartyController>();

				// validate target
				if (targetPartyController == null || targetPartyController.Current != null)
				{
					// already in party
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a party invite
				pendingInvitations.Add(targetCharacter.ID, leaderPartyController.Current.ID);
				targetCharacter.Owner.Broadcast(new PartyInviteBroadcast() { targetCharacterID = leaderPartyController.Character.ID });
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
			if (pendingInvitations.TryGetValue(partyController.Character.ID, out ulong pendingPartyID))
			{
				pendingInvitations.Remove(partyController.Character.ID);

				if (parties.TryGetValue(pendingPartyID, out Party party) && !party.IsFull)
				{
					List<long> CurrentMembers = new List<long>();

					PartyNewMemberBroadcast newMember = new PartyNewMemberBroadcast()
					{
						memberID = partyController.Character.ID,
						rank = PartyRank.Member,
					};

					foreach (PartyController member in party.Members.Values)
					{
						// tell our party members we joined the party
						member.Owner.Broadcast(newMember);
						CurrentMembers.Add(member.Character.ID);
					}

					partyController.Rank = PartyRank.Member;
					partyController.Current = party;

					// add the new party member
					party.Members.Add(partyController.Character.ID, partyController);

					// tell the new member they joined successfully
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
			Character character = conn.FirstObject.GetComponent<Character>();
			if (character != null)
			{
				pendingInvitations.Remove(character.ID);
			}
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
						party.ID = 0;
						party.LeaderID = 0;
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
						PartyController newLeader = null;
						// pick a random party member to take over leadership
						List<PartyController> Members = new List<PartyController>(party.Members.Values);
						if (Members != null && Members.Count > 0)
						{
							newLeader = Members[Random.Range(0, Members.Count)];
						}

						// remove the current leader
						party.Members.Remove(partyController.Character.ID);
						partyController.Rank = PartyRank.None;
						partyController.Current = null;
						// tell Character they left the party successfully
						conn.Broadcast(new PartyLeaveBroadcast());

						// update the party leader status and send it to the other party members
						if (newLeader != null)
						{
							party.LeaderID = newLeader.Character.ID;
							newLeader.Rank = PartyRank.Leader;

							PartyUpdateMemberBroadcast update = new PartyUpdateMemberBroadcast()
							{
								memberID = newLeader.Character.ID,
								rank = newLeader.Rank,
							};

							foreach (PartyController member in party.Members.Values)
							{
								member.Owner.Broadcast(update);
							}
						}
					}
				}

				PartyRemoveBroadcast removeCharacterBroadcast = new PartyRemoveBroadcast()
				{
					memberID = partyController.Character.ID,
				};

				// tell the remaining party members we left the party
				foreach (PartyController member in party.Members.Values)
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
				partyController.Character.ID == msg.memberID) // we can't kick ourself
			{
				return;
			}

			// validate party
			if (parties.TryGetValue(partyController.Current.ID, out Party party))
			{
				PartyController removedMember = partyController.Current.RemoveMember(msg.memberID);
				if (removedMember != null)
				{
					removedMember.Rank = PartyRank.None;
					removedMember.Current = null;

					PartyRemoveBroadcast removeCharacterBroadcast = new PartyRemoveBroadcast()
					{
						memberID = msg.memberID,
					};

					// tell the remaining party members someone was removed
					foreach (PartyController member in party.Members.Values)
					{
						member.Owner.Broadcast(removeCharacterBroadcast);
					}
				}
			}
		}
	}
}