using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;

namespace Server
{
	/// <summary>
	/// Server guild system.
	/// </summary>
	public class GuildSystem : ServerBehaviour
	{
		public CharacterSystem CharacterSystem;

		public ulong nextGuildId = 0;
		public Dictionary<ulong, Guild> guilds = new Dictionary<ulong, Guild>();

		// clientId / guildId
		public readonly Dictionary<long, ulong> pendingInvitations = new Dictionary<long, ulong>();

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

		public void OnServerGuildCreateBroadcastReceived(NetworkConnection conn, GuildCreateBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();
			if (guildController == null || guildController.current != null)
			{
				// already in a guild
				return;
			}

			ulong guildId = ++nextGuildId;
			// this should never happen but check it anyway so we never duplicate guild ids
			while (guilds.ContainsKey(guildId))
			{
				guildId = ++nextGuildId;
			}

			Guild newGuild = new Guild(guildId, guildController);
			guilds.Add(newGuild.id, newGuild);
			guildController.rank = GuildRank.Leader;
			guildController.current = newGuild;

			// tell the character we made their guild successfully
			conn.Broadcast(new GuildCreateBroadcast() { guildId = newGuild.id });
		}

		public void OnServerGuildInviteBroadcastReceived(NetworkConnection conn, GuildInviteBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController leaderGuildController = conn.FirstObject.GetComponent<GuildController>();

			// validate guild leader
			if (leaderGuildController == null ||
				leaderGuildController.current == null ||
				leaderGuildController.rank != GuildRank.Leader ||
				leaderGuildController.current.IsFull)
			{
				return;
			}

			if (!pendingInvitations.ContainsKey(msg.targetCharacterId) &&
				CharacterSystem.charactersById.TryGetValue(msg.targetCharacterId, out Character targetCharacter))
			{
				GuildController targetGuildController = targetCharacter.GetComponent<GuildController>();

				// validate target
				if (targetGuildController == null || targetGuildController.current != null)
				{
					// already in guild
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a guild invite
				pendingInvitations.Add(targetCharacter.id, leaderGuildController.current.id);
				targetCharacter.Owner.Broadcast(new GuildInviteBroadcast() { targetCharacterId = targetCharacter.id });
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
			if (pendingInvitations.TryGetValue(guildController.character.id, out ulong pendingGuildId))
			{
				pendingInvitations.Remove(guildController.character.id);

				if (guilds.TryGetValue(pendingGuildId, out Guild guild) && !guild.IsFull)
				{
					List<long> currentMembers = new List<long>();

					GuildNewMemberBroadcast newMember = new GuildNewMemberBroadcast()
					{
						newMemberCharacterId = guildController.character.id,
						rank = GuildRank.Member,
					};

					for (int i = 0; i < guild.members.Count; ++i)
					{
						// tell our guild members we joined the guild
						guild.members[i].Owner.Broadcast(newMember);
						currentMembers.Add(guild.members[i].character.id);
					}

					guildController.rank = GuildRank.Member;
					guildController.current = guild;

					// add the new guild member
					guild.members.Add(guildController);

					// tell the new member about they joined successfully
					GuildJoinedBroadcast memberBroadcast = new GuildJoinedBroadcast()
					{
						members = currentMembers,
					};
					conn.Broadcast(memberBroadcast);
				}
			}
		}

		public void OnServerGuildDeclineInviteBroadcastReceived(NetworkConnection conn, GuildDeclineInviteBroadcast msg)
		{
			// do we need to validate?
			pendingInvitations.Remove(conn.ClientId);
		}

		public void OnServerGuildLeaveBroadcastReceived(NetworkConnection conn, GuildLeaveBroadcast msg)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			GuildController guildController = conn.FirstObject.GetComponent<GuildController>();

			// validate character
			if (guildController == null || guildController.current == null)
			{
				// not in a guild..
				return;
			}

			// validate guild
			if (guilds.TryGetValue(guildController.current.id, out Guild guild))
			{
				if (guildController.rank == GuildRank.Leader)
				{
					// can we destroy the guild?
					if (guild.members.Count - 1 < 1)
					{
						guild.members.Clear();
						guilds.Remove(guild.id);

						guildController.rank = GuildRank.None;
						guildController.current = null;

						// tell character they left the guild successfully
						conn.Broadcast(new GuildLeaveBroadcast());
						return;
					}
					else
					{
						// next person in the guild becomes the new leader
						guild.members[1].rank = GuildRank.Leader;

						// remove the current leader
						guild.members.RemoveAt(0);

						guildController.rank = GuildRank.None;
						guildController.current = null;

						// tell character they left the guild successfully
						conn.Broadcast(new GuildLeaveBroadcast());
					}
				}

				GuildRemoveBroadcast removeCharacterBroadcast = new GuildRemoveBroadcast()
				{
					memberId = guildController.character.id,
				};

				// tell the remaining guild members we left the guild
				foreach (GuildController member in guild.members)
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
				guildController.current == null ||
				guildController.rank != GuildRank.Leader ||
				guildController.character.id == msg.memberId) // we can't kick ourself
			{
				return;
			}

			// validate guild
			if (guilds.TryGetValue(guildController.current.id, out Guild guild))
			{
				GuildController removedMember = guildController.current.RemoveMember(msg.memberId);
				if (removedMember != null)
				{
					removedMember.rank = GuildRank.None;
					removedMember.current = null;

					GuildRemoveBroadcast removeCharacterBroadcast = new GuildRemoveBroadcast()
					{
						memberId = msg.memberId,
					};

					// tell the remaining guild members someone was removed
					foreach (GuildController member in guild.members)
					{
						member.Owner.Broadcast(removeCharacterBroadcast);
					}
				}
			}
		}
	}
}