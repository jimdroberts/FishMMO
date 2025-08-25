using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Server guild system.
	/// </summary>
	public class GuildSystem : ServerBehaviour, IGuildSystem<NetworkConnection>
	{
		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private LocalConnectionState serverState;
		/// <summary>
		/// Timestamp of the last successful fetch from the database.
		/// </summary>
		private DateTime lastFetchTime = DateTime.UtcNow;
		/// <summary>
		/// Time remaining until the next database poll for guild updates.
		/// </summary>
		private float nextPump = 0.0f;

		[SerializeField]
		private int maxGuildSize = 100;
		[SerializeField]
		private int maxGuildNameLength = 64;
		[SerializeField]
		private float updatePumpRate = 1.0f;

		/// <summary>
		/// Maximum number of members allowed in a guild.
		/// </summary>
		public int MaxGuildSize { get { return maxGuildSize; } }
		/// <summary>
		/// Maximum length allowed for a guild name.
		/// </summary>
		public int MaxGuildNameLength { get { return maxGuildNameLength; } }
		/// <summary>
		/// The server guild update pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server guild update pump rate limit in seconds.")]
		public float UpdatePumpRate { get { return updatePumpRate; } }

		/// <summary>
		/// Tracks all of the members for a guild if any of the guild members are logged in to this server.
		/// Key: Guild ID, Value: Set of Character IDs.
		/// </summary>
		private Dictionary<long, HashSet<long>> guildMemberTracker = new Dictionary<long, HashSet<long>>();
		/// <summary>
		/// Tracks all active guilds and currently online guild members on this scene server.
		/// Key: Guild ID, Value: Set of Character IDs.
		/// </summary>
		private Dictionary<long, HashSet<long>> guildCharacterTracker = new Dictionary<long, HashSet<long>>();
		/// <summary>
		/// Tracks pending guild invitations by client ID and guild ID.
		/// Key: FromCharacterID, Value: ToCharacterID.
		/// </summary>
		private readonly Dictionary<long, long> pendingInvitations = new Dictionary<long, long>();

		/// <summary>
		/// Registered chat commands for guild actions.
		/// </summary>
		private Dictionary<string, ChatCommand> guildChatCommands;

		/// <summary>
		/// Handles guild invite chat commands.
		/// </summary>
		/// <param name="sender">The character sending the invite.</param>
		/// <param name="msg">Chat broadcast message containing the target character name.</param>
		/// <returns>True if invite was sent, false otherwise.</returns>
		public bool OnGuildInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			string characterName = msg.Text.Trim().ToLower();
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) &&
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

		/// <summary>
		/// Initializes the guild system, registering chat commands and broadcast handlers, and character events.
		/// </summary>
		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				Server != null &&
				Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) &&
				characterSystem != null)
			{
				guildChatCommands = new Dictionary<string, ChatCommand>()
				{
					{ "/gi", OnGuildInvite },
					{ "/ginvite", OnGuildInvite },
				};
				ChatHelper.AddCommands(guildChatCommands);

				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				Server.NetworkWrapper.RegisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived, true);
				Server.NetworkWrapper.RegisterBroadcast<GuildChangeRankBroadcast>(OnServerGuildChangeRankBroadcastReceived, true);

				// remove the characters pending guild invite request on disconnect
				characterSystem.OnConnect += CharacterSystem_OnConnect;
				characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Cleans up the guild system, unregistering broadcast handlers and character events.
		/// </summary>
		public override void Destroying()
		{
			if (Server != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<GuildChangeRankBroadcast>(OnServerGuildChangeRankBroadcastReceived);

				// remove the characters pending guild invite request on disconnect
				if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
				{
					characterSystem.OnConnect -= CharacterSystem_OnConnect;
					characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
				}
			}
		}

		/// <summary>
		/// Handles changes in the server's connection state.
		/// </summary>
		/// <param name="args">Arguments containing the new connection state.</param>
		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		/// <summary>
		/// Unity LateUpdate callback. Polls the database for guild updates at the specified rate and processes them.
		/// </summary>
		void LateUpdate()
		{
			if (Initialized && serverState == LocalConnectionState.Started)
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

		/// <summary>
		/// Fetches new guild updates from the database since the last fetch.
		/// </summary>
		/// <returns>List of new guild update entities.</returns>
		private List<GuildUpdateEntity> FetchGuildUpdates()
		{
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

			// fetch guild updates from the database
			List<GuildUpdateEntity> updates = GuildUpdateService.Fetch(dbContext, guildCharacterTracker.Keys.ToList(), lastFetchTime);
			if (updates != null && updates.Count > 0)
			{
				lastFetchTime = DateTime.UtcNow;
			}
			return updates;
		}

		/// <summary>
		/// Processes a list of guild updates, synchronizing guild membership and broadcasting changes to clients.
		/// </summary>
		/// <param name="updates">List of guild update entities to process.</param>
		private void ProcessGuildUpdates(List<GuildUpdateEntity> updates)
		{
			if (Server == null || Server.CoreServer.NpgsqlDbContextFactory == null || updates == null || updates.Count < 1)
			{
				return;
			}

			// Guilds that have previously been updated, we do this so we aren't updating guilds multiple times
			HashSet<long> updatedGuilds = new HashSet<long>();

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
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

				//Log.Debug($"Current Update Guild: {update.GuildID} MemberCount: {currentMemberIDs.Count}");

				// Check if we have previously cached the guild member list
				if (guildMemberTracker.TryGetValue(update.GuildID, out HashSet<long> previousMembers))
				{
					//Log.Debug($"Previously Cached Guild: {update.GuildID} MemberCount: {previousMembers.Count}");

					// Compute the difference: members that are in previousMembers but not in currentMemberIDs
					List<long> difference = previousMembers.Except(currentMemberIDs).ToList();

					foreach (long memberID in difference)
					{
						// Tell the member connection to leave their guild immediately
						if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> cs) &&
							cs.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) &&
							character != null &&
							character.TryGet(out IGuildController targetGuildController))
						{
							targetGuildController.ID = 0;
							Server.NetworkWrapper.Broadcast(character.Owner, new GuildLeaveBroadcast(), true, Channel.Reliable);
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

				if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
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
							Server.NetworkWrapper.Broadcast(character.Owner, guildAddBroadcast, true, Channel.Reliable);
						}
					}
				}
			}
		}

		/// <summary>
		/// Adds a mapping for the Guild to Guild Members connected to this Scene Server.
		/// </summary>
		/// <param name="guildID">ID of the guild.</param>
		/// <param name="characterID">ID of the character to add.</param>
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
		/// <param name="guildID">ID of the guild.</param>
		/// <param name="characterID">ID of the character to remove.</param>
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

		/// <summary>
		/// Handles character connect event, adding the character to the guild tracker and saving guild update.
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">The character that connected.</param>
		public void CharacterSystem_OnConnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (Server.CoreServer.NpgsqlDbContextFactory == null)
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

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			CharacterGuildService.Save(dbContext, guildController.Character, character.SceneName);
			GuildUpdateService.Save(dbContext, guildController.ID);
		}

		/// <summary>
		/// Handles character disconnect event, removing the character from the guild tracker and saving guild update.
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">The character that disconnected.</param>
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

			if (Server.CoreServer.NpgsqlDbContextFactory == null)
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

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			CharacterGuildService.Save(dbContext, guildController.Character, "Offline");
			GuildUpdateService.Save(dbContext, guildController.ID);
		}

		/// <summary>
		/// Handles guild creation broadcast, validates and creates a new guild for the requesting character.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">GuildCreateBroadcast message containing guild creation details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildCreateBroadcastReceived(NetworkConnection conn, GuildCreateBroadcast msg, Channel channel)
		{
			if (conn.FirstObject == null)
			{
				return;
			}
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}

			IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
			if (guildController == null || guildController.ID > 0)
			{
				// already in a guild
				Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
				{
					Result = GuildResultType.AlreadyInGuild,
				}, true, Channel.Reliable);
				return;
			}

			// remove white space
			msg.GuildName = msg.GuildName.Trim();

			if (!Constants.Authentication.IsAllowedGuildName(msg.GuildName))
			{
				Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
				{
					Result = GuildResultType.InvalidGuildName,
				}, true, Channel.Reliable);
				return;
			}

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (GuildService.Exists(dbContext, msg.GuildName))
			{
				Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
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
				Server.NetworkWrapper.Broadcast(conn, new GuildAddBroadcast()
				{
					GuildID = guildController.ID,
					CharacterID = guildController.Character.ID,
					Rank = guildController.Rank,
					Location = conn.FirstObject.gameObject.scene.name,
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles guild invitation broadcast, validates inviter and target, and sends invitation to the target character.
		/// Only guild leaders or officers can invite, and invitations are tracked to prevent duplicates.
		/// </summary>
		/// <param name="conn">Network connection of the inviter.</param>
		/// <param name="msg">GuildInviteBroadcast message containing inviter and target IDs.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildInviteBroadcastReceived(NetworkConnection conn, GuildInviteBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IGuildController inviter = conn.FirstObject.GetComponent<IGuildController>();
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

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
				Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) &&
				characterSystem.CharactersByID.TryGetValue(msg.TargetCharacterID, out IPlayerCharacter targetCharacter) &&
				targetCharacter.TryGet(out IGuildController targetGuildController))
			{
				// validate target
				if (targetGuildController.ID > 0)
				{
					// we should tell the inviter the target is already in a guild
					Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
					{
						Channel = ChatChannel.Guild,
						SenderID = msg.TargetCharacterID,
						Text = ChatHelper.GUILD_ERROR_TARGET_IN_GUILD + " ",
					}, true, Channel.Reliable);
					return;
				}

				// add to our list of pending invitations... used for validation when accepting/declining a guild invite
				pendingInvitations.Add(targetCharacter.ID, inviter.ID);
				Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new GuildInviteBroadcast()
				{
					InviterCharacterID = inviter.Character.ID,
					TargetCharacterID = targetCharacter.ID
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles acceptance of a guild invitation, validates the invite, adds the character to the guild, and broadcasts the update.
		/// </summary>
		/// <param name="conn">Network connection of the accepting character.</param>
		/// <param name="msg">GuildAcceptInviteBroadcast message containing acceptance details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
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

				if (Server == null || Server.CoreServer.NpgsqlDbContextFactory == null)
				{
					return;
				}
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
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
					Server.NetworkWrapper.Broadcast(conn, new GuildAddBroadcast()
					{
						GuildID = guildController.ID,
						CharacterID = guildController.Character.ID,
						Rank = GuildRank.Member,
						Location = conn.FirstObject.gameObject.scene.name,
					}, true, Channel.Reliable);
				}
			}
		}

		/// <summary>
		/// Handles decline of a guild invitation, removes pending invitation for the character.
		/// </summary>
		/// <param name="conn">Network connection of the declining character.</param>
		/// <param name="msg">GuildDeclineInviteBroadcast message containing decline details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildDeclineInviteBroadcastReceived(NetworkConnection conn, GuildDeclineInviteBroadcast msg, Channel channel)
		{
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character != null)
			{
				pendingInvitations.Remove(character.ID);
			}
		}

		/// <summary>
		/// Handles guild leave broadcast, validates character, transfers leadership if needed, removes member from guild, and updates or deletes guild as appropriate.
		/// </summary>
		/// <param name="conn">Network connection of the leaving character.</param>
		/// <param name="msg">GuildLeaveBroadcast message containing leave details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildLeaveBroadcastReceived(NetworkConnection conn, GuildLeaveBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
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

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

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
				Server.NetworkWrapper.Broadcast(conn, new GuildLeaveBroadcast(), true, Channel.Reliable);

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

		/// <summary>
		/// Handles guild member removal broadcast, validates and removes a member from the guild in the database.
		/// Only officers and leaders can remove other members.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">GuildRemoveBroadcast message containing member ID to remove.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildRemoveBroadcastReceived(NetworkConnection conn, GuildRemoveBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
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
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			bool result = CharacterGuildService.Delete(dbContext, guildController.Rank, guildController.ID, msg.GuildMemberID);
			if (result)
			{
				RemoveGuildCharacterTracker(guildController.ID, guildController.Character.ID);

				// tell the servers to update their guild lists
				GuildUpdateService.Save(dbContext, guildController.ID);
			}
		}

		/// <summary>
		/// Handles guild rank change broadcast, validates leader and target, and updates ranks in the database.
		/// Only guild leaders can promote another member to a new rank.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">GuildChangeRankBroadcast message containing target member ID and new rank.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildChangeRankBroadcastReceived(NetworkConnection conn, GuildChangeRankBroadcast msg, Channel channel)
		{
			if (Server.CoreServer.NpgsqlDbContextFactory == null)
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
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (CharacterGuildService.TrySaveRank(dbContext, msg.GuildMemberID, guildController.ID, msg.Rank))
			{
				// tell the servers to update their guild lists
				GuildUpdateService.Save(dbContext, guildController.ID);
			}
		}
	}
}