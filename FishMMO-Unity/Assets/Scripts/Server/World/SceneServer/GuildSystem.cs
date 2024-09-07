using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server
{
	/// <summary>
	/// Server guild system.
	/// </summary>
	public class GuildSystem : ServerBehaviour
	{
		private LocalConnectionState serverState;
		private DateTime lastFetchTime = DateTime.UtcNow;
		private long lastPosition = 0;
		private float nextPump = 0.0f;

		public int MaxGuildSize = 100;
		public int MaxGuildNameLength = 64;
		[Tooltip("The server guild update pump rate limit in seconds.")]
		public float UpdatePumpRate = 1.0f;
		public int UpdateFetchCount = 100;

		// <GuildID <MemberIDs>>
		private Dictionary<long, HashSet<long>> guildMemberTracker = new Dictionary<long, HashSet<long>>();
		// clientID / guildID
		private readonly Dictionary<long, long> pendingInvitations = new Dictionary<long, long>();

		private Dictionary<string, ChatCommand> guildChatCommands;
		public bool OnGuildInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			string characterName = msg.text.Trim().ToLower();
			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByLowerCaseName.TryGetValue(characterName, out IPlayerCharacter character))
			{
				OnServerGuildInviteBroadcastReceived(sender.Owner, new GuildInviteBroadcast()
				{
					inviterCharacterID = sender.ID,
					targetCharacterID = character.ID,
				}, Channel.Reliable);
				return true;
			}
			return false;
		}

		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server != null &&
				ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem != null)
			{
				guildChatCommands = new Dictionary<string, ChatCommand>()
				{
					{ "/gi", OnGuildInvite },
					{ "/ginvite", OnGuildInvite },
				};
				ChatHelper.AddCommands(guildChatCommands);

				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				Server.RegisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived, true);
				Server.RegisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived, true);
				Server.RegisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived, true);
				Server.RegisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived, true);
				Server.RegisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived, true);
				Server.RegisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived, true);
				Server.RegisterBroadcast<GuildChangeRankBroadcast>(OnServerGuildChangeRankBroadcastReceived, true);

				// remove the characters pending guild invite request on disconnect
				characterSystem.OnConnect += CharacterSystem_OnConnect;
				characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (Server != null)
			{
				Server.UnregisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived);
				Server.UnregisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived);
				Server.UnregisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived);
				Server.UnregisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived);
				Server.UnregisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived);
				Server.UnregisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived);
				Server.UnregisterBroadcast<GuildChangeRankBroadcast>(OnServerGuildChangeRankBroadcastReceived);

				// remove the characters pending guild invite request on disconnect
				if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					characterSystem.OnConnect -= CharacterSystem_OnConnect;
					characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
				}
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPump < 0)
				{
					nextPump = UpdatePumpRate;

					List<GuildUpdateEntity> updates = FetchGuildUpdates();
					ProcessGuildUpdates(updates);

				}
				nextPump -= Time.deltaTime;
			}
		}

		private List<GuildUpdateEntity> FetchGuildUpdates()
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			// fetch guild updates from the database
			List<GuildUpdateEntity> updates = GuildUpdateService.Fetch(dbContext, lastFetchTime, lastPosition, UpdateFetchCount);
			if (updates != null && updates.Count > 0)
			{
				GuildUpdateEntity latest = updates[updates.Count - 1];
				if (latest != null)
				{
					lastFetchTime = latest.TimeCreated;
					lastPosition = latest.ID;
				}
			}
			return updates;
		}

		// process updates from the database
		private void ProcessGuildUpdates(List<GuildUpdateEntity> updates)
		{
			if (Server == null || Server.NpgsqlDbContextFactory == null || updates == null || updates.Count < 1)
			{
				return;
			}

			// guilds that have previously been updated, we do this so we aren't updating guilds multiple times
			HashSet<long> updatedGuilds = new HashSet<long>();

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			foreach (GuildUpdateEntity update in updates)
			{
				// check if we have already updated this guild
				if (updatedGuilds.Contains(update.GuildID))
				{
					continue;
				}
				// otherwise add the guild to our list and continue with the update
				updatedGuilds.Add(update.GuildID);

				// get the current guild members from the database
				List<CharacterGuildEntity> dbMembers = CharacterGuildService.Members(dbContext, update.GuildID);

				// Get the current member ids
				var currentMemberIDs = dbMembers.Select(x => x.CharacterID).ToHashSet();

				// Check if we have previously cached the guild member list
				if (guildMemberTracker.TryGetValue(update.GuildID, out HashSet<long> previousMembers))
				{
					// Compute the difference: members that are in previousMembers but not in currentMemberIDs
					List<long> difference = previousMembers.Except(currentMemberIDs).ToList();

					foreach (long memberID in difference)
					{
						// Tell the member connection to leave their guild immediately
						if (ServerBehaviour.TryGet(out CharacterSystem cs) &&
							cs.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) &&
							character != null &&
							character.TryGet(out IGuildController targetGuildController))
						{
							targetGuildController.ID = 0;
							Server.Broadcast(character.Owner, new GuildLeaveBroadcast(), true, Channel.Reliable);
						}
					}
				}
				// Cache the guild member IDs
				guildMemberTracker[update.GuildID] = currentMemberIDs;

				var addBroadcasts = dbMembers.Select(x => new GuildAddBroadcast()
				{
					guildID = x.GuildID,
					characterID = x.CharacterID,
					rank = (GuildRank)x.Rank,
					location = x.Location,
				}).ToList();

				GuildAddMultipleBroadcast guildAddBroadcast = new GuildAddMultipleBroadcast()
				{
					members = addBroadcasts,
				};

				if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					// tell all of the local guild members to update their guild member lists
					foreach (CharacterGuildEntity entity in dbMembers)
					{
						if (characterSystem.CharactersByID.TryGetValue(entity.CharacterID, out IPlayerCharacter character))
						{
							if (!character.TryGet(out IGuildController guildController) ||
								guildController.ID < 1)
							{
								continue;
							}
							// update server rank in the case of a membership rank change
							guildController.Rank = (GuildRank)entity.Rank;
							Server.Broadcast(character.Owner, guildAddBroadcast, true, Channel.Reliable);
						}
					}
				}
			}
		}

		public void CharacterSystem_OnConnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			if (!character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1)
			{
				// not in a guild
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			CharacterGuildService.Save(dbContext, guildController.Character, character.SceneName);
			GuildUpdateService.Save(dbContext, guildController.ID);
		}

		public void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character != null)
			{
				pendingInvitations.Remove(character.ID);
			}

			if (character == null ||
				character.IsTeleporting)
			{
				return;
			}

			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			if (!character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1)
			{
				// not in a guild
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			CharacterGuildService.Save(dbContext, guildController.Character, "Offline");
			GuildUpdateService.Save(dbContext, guildController.ID);
		}

		public void OnServerGuildCreateBroadcastReceived(NetworkConnection conn, GuildCreateBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}

			IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
			if (guildController == null || guildController.ID > 0)
			{
				// already in a guild
				return;
			}

			// remove white space
			msg.guildName = msg.guildName.Trim();

			if (!Constants.Authentication.IsAllowedGuildName(msg.guildName))
			{
				// we should tell the player the guild name is not valid TODO
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (GuildService.Exists(dbContext, msg.guildName))
			{
				// guild name is taken
				return;
			}
			if (GuildService.TryCreate(dbContext, msg.guildName, out GuildEntity newGuild))
			{
				guildController.ID = newGuild.ID;
				guildController.Rank = GuildRank.Leader;
				CharacterGuildService.Save(dbContext, guildController.Character);

				// tell the character we made their guild successfully
				Server.Broadcast(conn, new GuildAddBroadcast()
				{
					guildID = guildController.ID,
					characterID = guildController.Character.ID,
					rank = guildController.Rank,
					location = conn.FirstObject.gameObject.scene.name,
				}, true, Channel.Reliable);
			}
		}

		public void OnServerGuildInviteBroadcastReceived(NetworkConnection conn, GuildInviteBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IGuildController inviter = conn.FirstObject.GetComponent<IGuildController>();
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			// validate guild leader or officer is inviting
			if (inviter == null ||
				inviter.ID < 1 ||
				inviter.Character.ID == msg.targetCharacterID || 
				!(inviter.Rank == GuildRank.Leader | inviter.Rank == GuildRank.Officer) ||
				!CharacterGuildService.ExistsNotFull(dbContext, inviter.ID, MaxGuildSize))
			{
				return;
			}

			// if the target doesn't already have a pending invite
			if (!pendingInvitations.ContainsKey(msg.targetCharacterID) &&
				ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByID.TryGetValue(msg.targetCharacterID, out IPlayerCharacter targetCharacter) &&
				targetCharacter.TryGet(out IGuildController targetGuildController))
			{
				// validate target
				if (targetGuildController.ID > 0)
				{
					// we should tell the inviter the target is already in a guild
					Server.Broadcast(conn, new ChatBroadcast()
					{
						channel = ChatChannel.Guild,
						senderID = msg.targetCharacterID,
						text = ChatHelper.GUILD_ERROR_TARGET_IN_GUILD + " ",
					}, true, Channel.Reliable);
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a guild invite
				pendingInvitations.Add(targetCharacter.ID, inviter.ID);
				Server.Broadcast(targetCharacter.Owner, new GuildInviteBroadcast()
				{
					inviterCharacterID = inviter.Character.ID,
					targetCharacterID = targetCharacter.ID
				}, true, Channel.Reliable);
			}
		}

		public void OnServerGuildAcceptInviteBroadcastReceived(NetworkConnection conn, GuildAcceptInviteBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();

			// validate character
			if (guildController == null || guildController.ID > 0)
			{
				return;
			}

			// validate guild invite
			if (pendingInvitations.TryGetValue(guildController.Character.ID, out long pendingGuildID))
			{
				pendingInvitations.Remove(guildController.Character.ID);

				if (Server == null || Server.NpgsqlDbContextFactory == null)
				{
					return;
				}
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				List<CharacterGuildEntity> members = CharacterGuildService.Members(dbContext, pendingGuildID);
				if (members != null &&
					members.Count < MaxGuildSize)
				{
					guildController.ID = pendingGuildID;
					guildController.Rank = GuildRank.Member;
					
					CharacterGuildService.Save(dbContext, guildController.Character);
					// tell the other servers to update their guild lists
					GuildUpdateService.Save(dbContext, pendingGuildID);

					// tell the new member they joined immediately, other clients will catch up with the GuildUpdate pass
					Server.Broadcast(conn, new GuildAddBroadcast()
					{
						guildID = guildController.ID,
						characterID = guildController.Character.ID,
						rank = GuildRank.Member,
						location = conn.FirstObject.gameObject.scene.name,
					}, true, Channel.Reliable);
				}
			}
		}

		public void OnServerGuildDeclineInviteBroadcastReceived(NetworkConnection conn, GuildDeclineInviteBroadcast msg, Channel channel)
		{
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character != null)
			{
				pendingInvitations.Remove(character.ID);
			}
		}

		public void OnServerGuildLeaveBroadcastReceived(NetworkConnection conn, GuildLeaveBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();

			// validate character
			if (guildController == null || guildController.ID < 1)
			{
				// not in a guild..
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			// validate guild
			List<CharacterGuildEntity> members = CharacterGuildService.Members(dbContext, guildController.ID);
			if (members != null &&
				members.Count > 0)
			{
				int remainingCount = members.Count - 1;

				List<CharacterGuildEntity> remainingMembers = new List<CharacterGuildEntity>();

				// are there any other members in the guild? if so we transfer leadership to officers first and then members
				if (guildController.Rank == GuildRank.Leader && remainingCount > 0)
				{
					List<CharacterGuildEntity> officers = new List<CharacterGuildEntity>();

					foreach (CharacterGuildEntity member in members)
					{
						if (member.CharacterID == guildController.Character.ID)
						{
							continue;
						}

						if (member.Rank == (byte)GuildRank.Officer)
						{
							officers.Add(member);
						}
						remainingMembers.Add(member);
					}

					CharacterGuildEntity newLeader = null;
					if (officers.Count > 0)
					{
						// pick a random officer
						newLeader = officers[UnityEngine.Random.Range(0, officers.Count)];
					}
					else if (remainingMembers.Count > 0)
					{
						// pick a random member
						newLeader = remainingMembers[UnityEngine.Random.Range(0, remainingMembers.Count)];
					}

					// update the guild leader status in the database
					if (newLeader != null)
					{
						CharacterGuildService.Save(dbContext, newLeader.CharacterID, newLeader.GuildID, GuildRank.Leader, newLeader.Location);
					}
				}

				long guildID = guildController.ID;

				guildController.ID = 0;
				guildController.Rank = GuildRank.None;

				// tell character that they left the guild immediately, other clients will catch up with the GuildUpdate pass
				Server.Broadcast(conn, new GuildLeaveBroadcast(), true, Channel.Reliable);

				// remove the guild member
				CharacterGuildService.Delete(dbContext, guildController.Character.ID);

				if (remainingCount < 1)
				{
					// delete the guild
					GuildService.Delete(dbContext, guildID);
					GuildUpdateService.Delete(dbContext, guildID);
				}
				else
				{
					// tell the other servers to update their guild lists
					GuildUpdateService.Save(dbContext, guildID);
				}
			}
		}

		public void OnServerGuildRemoveBroadcastReceived(NetworkConnection conn, GuildRemoveBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();

			// validate character
			if (guildController == null ||
				guildController.ID < 1 ||
				guildController.Rank < GuildRank.Officer) 
			{
				return;
			}

			if (msg.guildMemberID < 1)
			{
				return;
			}

			// we can't kick ourself
			if (msg.guildMemberID == guildController.Character.ID)
			{
				return;
			}

			// remove the character from the guild in the database
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			bool result = CharacterGuildService.Delete(dbContext, guildController.Rank, guildController.ID, msg.guildMemberID);
			if (result)
			{
				// tell the servers to update their guild lists
				GuildUpdateService.Save(dbContext, guildController.ID);
			}
		}

		public void OnServerGuildChangeRankBroadcastReceived(NetworkConnection conn, GuildChangeRankBroadcast msg, Channel channel)
		{
			if (Server.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();

			// validate character
			if (guildController == null ||
				guildController.ID < 1 ||
				guildController.Rank != GuildRank.Leader)
			{
				return;
			}

			if (msg.guildMemberID < 1)
			{
				return;
			}

			// we can't promote ourself
			if (msg.guildMemberID == guildController.Character.ID)
			{
				return;
			}

			// update the character rank in the guild in the database
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (CharacterGuildService.TrySaveRank(dbContext, msg.guildMemberID, guildController.ID, msg.rank))
			{
				// tell the servers to update their guild lists
				GuildUpdateService.Save(dbContext, guildController.ID);
			}
		}
	}
}