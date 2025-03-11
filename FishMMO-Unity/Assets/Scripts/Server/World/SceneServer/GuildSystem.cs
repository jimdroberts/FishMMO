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
		private float nextPump = 0.0f;

		public int MaxGuildSize = 100;
		public int MaxGuildNameLength = 64;
		[Tooltip("The server guild update pump rate limit in seconds.")]
		public float UpdatePumpRate = 1.0f;

		/// <summary>
		/// Tracks all of the members for a guild if any of the guild members are logged in to this server.
		/// </summary>
		private Dictionary<long, HashSet<long>> guildMemberTracker = new Dictionary<long, HashSet<long>>();
		/// <summary>
		/// Tracks all active guilds and currently online guild members on this scene server.
		/// </summary>
		private Dictionary<long, HashSet<long>> guildCharacterTracker = new Dictionary<long, HashSet<long>>();
		/// <summary>
		/// Pending guild invites Dictionary<FromCharacterID, ToCharacterID>
		/// </summary>
		private readonly Dictionary<long, long> pendingInvitations = new Dictionary<long, long>();

		private Dictionary<string, ChatCommand> guildChatCommands;
		public bool OnGuildInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			string characterName = msg.Text.Trim().ToLower();
			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByLowerCaseName.TryGetValue(characterName, out IPlayerCharacter character))
			{
				OnServerGuildInviteBroadcastReceived(sender.Owner, new GuildInviteBroadcast()
				{
					InviterCharacterID = sender.ID,
					TargetCharacterID = character.ID,
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
			List<GuildUpdateEntity> updates = GuildUpdateService.Fetch(dbContext, guildCharacterTracker.Keys.ToList(), lastFetchTime);
			if (updates != null && updates.Count > 0)
			{
				lastFetchTime = DateTime.UtcNow;
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

			// Guilds that have previously been updated, we do this so we aren't updating guilds multiple times
			HashSet<long> updatedGuilds = new HashSet<long>();

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			foreach (GuildUpdateEntity update in updates)
			{
				// Check if we have already updated this guild
				if (updatedGuilds.Contains(update.GuildID))
				{
					continue;
				}
				// Otherwise add the guild to our list and continue with the update
				updatedGuilds.Add(update.GuildID);

				// Get the current guild members from the database
				List<CharacterGuildEntity> dbMembers = CharacterGuildService.Members(dbContext, update.GuildID);

				// Get the current member ids
				var currentMemberIDs = dbMembers.Select(x => x.CharacterID).ToHashSet();

				//Debug.Log($"Current Update Guild: {update.GuildID} MemberCount: {currentMemberIDs.Count}");

				// Check if we have previously cached the guild member list
				if (guildMemberTracker.TryGetValue(update.GuildID, out HashSet<long> previousMembers))
				{
					//Debug.Log($"Previously Cached Guild: {update.GuildID} MemberCount: {previousMembers.Count}");

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
					GuildID = x.GuildID,
					CharacterID = x.CharacterID,
					Rank = (GuildRank)x.Rank,
					Location = x.Location,
				}).ToList();

				GuildAddMultipleBroadcast guildAddBroadcast = new GuildAddMultipleBroadcast()
				{
					Members = addBroadcasts,
				};

				if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					// Tell all of the local guild members to update their guild member lists
					foreach (CharacterGuildEntity entity in dbMembers)
					{
						if (characterSystem.CharactersByID.TryGetValue(entity.CharacterID, out IPlayerCharacter character))
						{
							if (!character.TryGet(out IGuildController guildController) ||
								guildController.ID < 1)
							{
								continue;
							}
							// Update server rank in the case of a membership rank change
							guildController.Rank = (GuildRank)entity.Rank;
							Server.Broadcast(character.Owner, guildAddBroadcast, true, Channel.Reliable);
						}
					}
				}
			}
		}

		/// <summary>
		/// Adds a mapping for the Guild to Guild Members connected to this Scene Server.
		/// </summary>
		public void AddGuildCharacterTracker(long guildID, long characterID)
		{
			if (guildID == 0)
			{
				return;
			}
			if (!guildCharacterTracker.TryGetValue(guildID, out HashSet<long> characterIDs))
			{
				guildCharacterTracker.Add(guildID, characterIDs = new HashSet<long>());
			}
			if (!characterIDs.Contains(characterID))
			{
				characterIDs.Add(characterID);
			}
		}

		/// <summary>
		/// Removes the mapping of Guild to Guild Members connected to this Scene Server.
		/// </summary>
		public void RemoveGuildCharacterTracker(long guildID, long characterID)
		{
			if (guildID == 0)
			{
				return;
			}
			if (guildCharacterTracker.TryGetValue(guildID, out HashSet<long> characterIDs))
			{
				characterIDs.Remove(characterID);

				// If there are no active guild members we can remove the character and member trackers for the guild.
				if (characterIDs.Count < 1)
				{
					guildCharacterTracker.Remove(guildID);
					guildMemberTracker.Remove(guildID);
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

			AddGuildCharacterTracker(guildController.ID, character.ID);

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

			RemoveGuildCharacterTracker(guildController.ID, character.ID);

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
				Server.Broadcast(conn, new GuildResultBroadcast()
				{
					Result = GuildResultType.AlreadyInGuild,
				}, true, Channel.Reliable);
				return;
			}

			// remove white space
			msg.GuildName = msg.GuildName.Trim();

			if (!Constants.Authentication.IsAllowedGuildName(msg.GuildName))
			{
				Server.Broadcast(conn, new GuildResultBroadcast()
				{
					Result = GuildResultType.InvalidGuildName,
				}, true, Channel.Reliable);
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (GuildService.Exists(dbContext, msg.GuildName))
			{
				Server.Broadcast(conn, new GuildResultBroadcast()
				{
					Result = GuildResultType.NameAlreadyExists,
				}, true, Channel.Reliable);
				return;
			}
			if (GuildService.TryCreate(dbContext, msg.GuildName, out GuildEntity newGuild))
			{
				guildController.ID = newGuild.ID;
				guildController.Rank = GuildRank.Leader;
				CharacterGuildService.Save(dbContext, guildController.Character);

				AddGuildCharacterTracker(guildController.ID, guildController.Character.ID);

				// tell the character we made their guild successfully
				Server.Broadcast(conn, new GuildAddBroadcast()
				{
					GuildID = guildController.ID,
					CharacterID = guildController.Character.ID,
					Rank = guildController.Rank,
					Location = conn.FirstObject.gameObject.scene.name,
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
				inviter.Character.ID == msg.TargetCharacterID || 
				!(inviter.Rank == GuildRank.Leader | inviter.Rank == GuildRank.Officer) ||
				!CharacterGuildService.ExistsNotFull(dbContext, inviter.ID, MaxGuildSize))
			{
				return;
			}

			// if the target doesn't already have a pending invite
			if (!pendingInvitations.ContainsKey(msg.TargetCharacterID) &&
				ServerBehaviour.TryGet(out CharacterSystem characterSystem) &&
				characterSystem.CharactersByID.TryGetValue(msg.TargetCharacterID, out IPlayerCharacter targetCharacter) &&
				targetCharacter.TryGet(out IGuildController targetGuildController))
			{
				// validate target
				if (targetGuildController.ID > 0)
				{
					// we should tell the inviter the target is already in a guild
					Server.Broadcast(conn, new ChatBroadcast()
					{
						Channel = ChatChannel.Guild,
						SenderID = msg.TargetCharacterID,
						Text = ChatHelper.GUILD_ERROR_TARGET_IN_GUILD + " ",
					}, true, Channel.Reliable);
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a guild invite
				pendingInvitations.Add(targetCharacter.ID, inviter.ID);
				Server.Broadcast(targetCharacter.Owner, new GuildInviteBroadcast()
				{
					InviterCharacterID = inviter.Character.ID,
					TargetCharacterID = targetCharacter.ID
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

					AddGuildCharacterTracker(guildController.ID, guildController.Character.ID);
					
					CharacterGuildService.Save(dbContext, guildController.Character);
					// tell the other servers to update their guild lists
					GuildUpdateService.Save(dbContext, guildController.ID);

					// tell the new member they joined immediately, other clients will catch up with the GuildUpdate pass
					Server.Broadcast(conn, new GuildAddBroadcast()
					{
						GuildID = guildController.ID,
						CharacterID = guildController.Character.ID,
						Rank = GuildRank.Member,
						Location = conn.FirstObject.gameObject.scene.name,
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

				RemoveGuildCharacterTracker(guildID, guildController.Character.ID);

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

			if (msg.GuildMemberID < 1)
			{
				return;
			}

			// we can't kick ourself
			if (msg.GuildMemberID == guildController.Character.ID)
			{
				return;
			}

			// remove the character from the guild in the database
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			bool result = CharacterGuildService.Delete(dbContext, guildController.Rank, guildController.ID, msg.GuildMemberID);
			if (result)
			{
				RemoveGuildCharacterTracker(guildController.ID, guildController.Character.ID);

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

			if (msg.GuildMemberID < 1)
			{
				return;
			}

			// we can't promote ourself
			if (msg.GuildMemberID == guildController.Character.ID)
			{
				return;
			}

			// update the character rank in the guild in the database
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (CharacterGuildService.TrySaveRank(dbContext, msg.GuildMemberID, guildController.ID, msg.Rank))
			{
				// tell the servers to update their guild lists
				GuildUpdateService.Save(dbContext, guildController.ID);
			}
		}
	}
}