using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Server
{
	/// <summary>
	/// Server guild system.
	/// </summary>
	public class GuildSystem : ServerBehaviour
	{
		public ulong nextGuildID = 0;
		private Dictionary<string, Guild> guilds = new Dictionary<string, Guild>();

		// clientID / guildID
		private readonly Dictionary<long, string> pendingInvitations = new Dictionary<long, string>();

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
				ServerManager.RegisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived, true);
				ServerManager.RegisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ServerManager.UnregisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived);
				ServerManager.UnregisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived);
			}
		}

		public void RemovePending(long id)
		{
			pendingInvitations.Remove(id);
		}

		public void OnServerGuildCreateBroadcastReceived(NetworkConnection conn, GuildCreateBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();
			if (guildController == null || guildController.Current != null)
			{
				// already in a guild
				return;
			}

			if (!Guild.GuildNameValid(msg.guildID))
			{
				// we should tell the player the guild name is not valid
				return;
			}

			// this should never happen but check it anyway so we never duplicate guild ids
			if (guilds.ContainsKey(msg.guildID))
			{
				// we should tell the player the guild name already exists
				return;
			}

			Guild newGuild = new Guild(msg.guildID, guildController);
			guilds.Add(newGuild.ID, newGuild);
			guildController.Rank = GuildRank.Leader;
			guildController.Current = newGuild;

			// tell the character we made their guild successfully
			conn.Broadcast(new GuildCreateBroadcast() { guildID = newGuild.ID });
		}

		public void OnServerGuildInviteBroadcastReceived(NetworkConnection conn, GuildInviteBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController inviter = conn.FirstObject.GetComponent<GuildController>();

			// validate guild leader or officer is inviting
			if (inviter == null ||
				inviter.Current == null ||
				inviter.Rank != GuildRank.Leader ||
				inviter.Rank != GuildRank.Officer ||
				inviter.Current.IsFull)
			{
				return;
			}

			// if the target doesn't already have a pending invite
			if (!pendingInvitations.ContainsKey(msg.targetCharacterID) &&
				Server.CharacterSystem.CharactersByID.TryGetValue(msg.targetCharacterID, out Character targetCharacter))
			{
				GuildController targetGuildController = targetCharacter.GetComponent<GuildController>();

				// validate target
				if (targetGuildController == null || targetGuildController.Current != null)
				{
					// we should tell the inviter the target is already in a guild
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a guild invite
				pendingInvitations.Add(targetCharacter.ID, inviter.Current.ID);
				targetCharacter.Owner.Broadcast(new GuildInviteBroadcast() { targetCharacterID = targetCharacter.ID });
			}
		}

		public void OnServerGuildAcceptInviteBroadcastReceived(NetworkConnection conn, GuildAcceptInviteBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();

			// validate character
			if (guildController == null)
			{
				return;
			}

			// validate guild invite
			if (pendingInvitations.TryGetValue(guildController.Character.ID, out string pendingGuildID))
			{
				pendingInvitations.Remove(guildController.Character.ID);

				if (guilds.TryGetValue(pendingGuildID, out Guild guild) && !guild.IsFull)
				{
					List<long> CurrentMembers = new List<long>();

					GuildNewMemberBroadcast newMember = new GuildNewMemberBroadcast()
					{
						memberID = guildController.Character.ID,
						rank = GuildRank.Member,
					};

					foreach (GuildController member in guild.Members.Values)
					{
						// tell our guild members we joined the guild
						member.Owner.Broadcast(newMember);
						CurrentMembers.Add(member.Character.ID);
					}

					guildController.Rank = GuildRank.Member;
					guildController.Current = guild;

					// add the new guild member
					guild.Members.Add(guildController.Character.ID, guildController);

					// tell the new member they joined successfully
					GuildJoinedBroadcast memberBroadcast = new GuildJoinedBroadcast()
					{
						members = CurrentMembers,
					};
					conn.Broadcast(memberBroadcast);
				}
			}
		}

		public void OnServerGuildDeclineInviteBroadcastReceived(NetworkConnection conn, GuildDeclineInviteBroadcast msg)
		{
			Character character = conn.FirstObject.GetComponent<Character>();
			if (character != null)
			{
				pendingInvitations.Remove(character.ID);
			}
		}

		public void OnServerGuildLeaveBroadcastReceived(NetworkConnection conn, GuildLeaveBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();

			// validate character
			if (guildController == null || guildController.Current == null)
			{
				// not in a guild..
				return;
			}

			// validate guild
			if (guilds.TryGetValue(guildController.Current.ID, out Guild guild))
			{
				if (guildController.Rank == GuildRank.Leader)
				{
					// can we destroy the guild?
					if (guild.Members.Count - 1 < 1)
					{
						guild.ID = "";
						guild.LeaderID = 0;
						guild.Members.Clear();
						guild.Officers.Clear();
						guilds.Remove(guild.ID);

						guildController.Rank = GuildRank.None;
						guildController.Current = null;

						// tell character they left the guild successfully
						conn.Broadcast(new GuildLeaveBroadcast());
						return;
					}
					else
					{
						GuildController newLeader = null;
						// pick a random officer to take over the guild
						if (guild.Officers.Count > 0)
						{
							List<GuildController> officers = new List<GuildController>(guild.Officers.Values);
							if (officers != null && officers.Count > 0)
							{
								newLeader = officers[Random.Range(0, officers.Count)];
							}
						}
						// pick a random guild member to take over the guild
						else
						{
							List<GuildController> Members = new List<GuildController>(guild.Members.Values);
							if (Members != null && Members.Count > 0)
							{
								newLeader = Members[Random.Range(0, Members.Count)];
							}
						}

						// remove the member
						guild.Members.Remove(guildController.Character.ID);
						guildController.Rank = GuildRank.None;
						guildController.Current = null;
						// tell character they left the guild successfully
						conn.Broadcast(new GuildLeaveBroadcast());

						// update the guild leader status and send it to the other guild members
						if (newLeader != null)
						{
							guild.LeaderID = newLeader.Character.ID;
							guild.Officers.Remove(guild.LeaderID);
							newLeader.Rank = GuildRank.Leader;

							GuildUpdateMemberBroadcast update = new GuildUpdateMemberBroadcast()
							{
								memberID = newLeader.Character.ID,
								rank = newLeader.Rank,
							};

							foreach (GuildController member in guild.Members.Values)
							{
								member.Owner.Broadcast(update);
							}
						}
					}
				}

				GuildRemoveBroadcast removeCharacterBroadcast = new GuildRemoveBroadcast()
				{
					memberID = guildController.Character.ID,
				};

				// tell the remaining guild members we left the guild
				foreach (GuildController member in guild.Members.Values)
				{
					member.Owner.Broadcast(removeCharacterBroadcast);
				}
			}
		}

		public void OnServerGuildRemoveBroadcastReceived(NetworkConnection conn, GuildRemoveBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();

			// validate character
			if (guildController == null ||
				guildController.Current == null ||
				guildController.Rank != GuildRank.Leader ||
				guildController.Character.ID == msg.memberID) // we can't kick ourself
			{
				return;
			}

			// validate guild
			if (guilds.TryGetValue(guildController.Current.ID, out Guild guild))
			{
				GuildController removedMember = guildController.Current.RemoveMember(msg.memberID);
				if (removedMember != null)
				{
					removedMember.Rank = GuildRank.None;
					removedMember.Current = null;

					GuildRemoveBroadcast removeCharacterBroadcast = new GuildRemoveBroadcast()
					{
						memberID = msg.memberID,
					};

					// tell the remaining guild members someone was removed
					foreach (GuildController member in guild.Members.Values)
					{
						member.Owner.Broadcast(removeCharacterBroadcast);
					}
				}
			}
		}
	}
}