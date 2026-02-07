using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages guild creation, membership, ranks, invitations, and updates with database synchronization.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread state changes or Broadcasts are marshalled
	/// via IGuildSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "GuildSystem", menuName = "FishMMO/Server/SceneServer/Guild System", order = 1)]
	[RequiresDataContainer(typeof(GuildSystemRuntimeData))]
	[RequiresDataContainer(typeof(GuildCharacterMappingData))]
	[RequiresDataContainer(typeof(GuildSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class GuildSystem : ServerBehaviour, IGuildSystem<NetworkConnection>
	{
		[SerializeField]
		private int maxGuildSize = 100;
		[SerializeField]
		private int maxGuildNameLength = 64;
		[Tooltip("The server guild update pump rate limit in seconds.")]
		[SerializeField]
		private float updatePumpRate = 1.0f;

		/// <summary>
		/// Gets or sets the update pump rate for guild synchronization.
		/// </summary>
		public float UpdatePumpRate { get { return updatePumpRate; } set { updatePumpRate = value; } }

		/// <summary>
		/// Maximum number of members allowed in a guild.
		/// </summary>
		public int MaxGuildSize { get { return maxGuildSize; } }
		/// <summary>
		/// Maximum length allowed for a guild name.
		/// </summary>
		public int MaxGuildNameLength { get { return maxGuildNameLength; } }

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
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(characterName, out IPlayerCharacter character))
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
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("GuildSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IGuildSystemMainThreadQueueData>(out _))
			{
				Log.Error("GuildSystem", "Failed to initialize: IGuildSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) ||
				characterSystem == null)
			{
				Log.Error("GuildSystem", "Failed to initialize: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Chat commands
			guildChatCommands = new Dictionary<string, ChatCommand>()
			{
				{ "/gi", OnGuildInvite },
				{ "/ginvite", OnGuildInvite },
			};
			ChatHelper.AddCommands(guildChatCommands);

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildChangeRankBroadcast>(OnServerGuildChangeRankBroadcastReceived, true);

			// Character system events
			characterSystem.OnConnect += CharacterSystem_OnConnect;
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(UpdatePumpRate, OnPeriodicUpdate);
			}

			Log.Debug("GuildSystem", $"Initialized (MaxGuildSize={MaxGuildSize}, UpdatePumpRate={UpdatePumpRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the guild system, unregistering broadcast handlers and character events.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("GuildSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue();

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildChangeRankBroadcast>(OnServerGuildChangeRankBroadcastReceived);

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnConnect -= CharacterSystem_OnConnect;
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicUpdate);
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the IGuildSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue()
		{
			if (Server?.DataContainerRegistry.TryGet<IGuildSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Drain();
			}
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<IGuildSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue();
		}

		/// <summary>
		/// Periodic callback that fetches and processes guild updates from the database asynchronously.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (Initialized && Server.ServerState == ConnectionState.Started)
			{
				EnqueueAsyncWork(() => FetchAndProcessGuildUpdatesAsync());
			}
		}

		/// <summary>
		/// Asynchronously fetches guild updates from the database and marshals the processing back to the main thread.
		/// </summary>
		private async Task FetchAndProcessGuildUpdatesAsync()
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IGuildUpdateService>(out var guildUpdateService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var charGuildService))
				{
					return;
				}

				// Capture data from main-thread containers
				List<long> guildIds = null;
				DateTime lastFetch = DateTime.UtcNow;
				EnqueueMainThread(() =>
				{
					// This runs on main thread to safely read the containers
				});
				// We need the guild IDs from the main-thread tracker. Let's capture them synchronously
				// since this method is fire-and-forget from the periodic callback on main thread.
				// The periodic callback runs on main thread, so we can read containers here before awaiting.
				if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData))
				{
					return;
				}
				if (!Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
				{
					return;
				}

				guildIds = mappingData.GuildCharacterTracker.Keys.ToList();
				lastFetch = runtimeData.LastFetchTime;

				if (guildIds == null || guildIds.Count == 0)
				{
					return;
				}

				// Async DB fetch
				DatabaseResult<List<GuildUpdateData>> fetchResult = await guildUpdateService.FetchAsync(guildIds, lastFetch);
				if (!fetchResult.IsSuccess || fetchResult.Data == null || fetchResult.Data.Count < 1)
				{
					return;
				}

				List<GuildUpdateData> updates = fetchResult.Data;

				// For each unique guild that was updated, fetch the current members
				HashSet<long> updatedGuilds = new HashSet<long>();
				// Collect per-guild member data
				Dictionary<long, IReadOnlyList<CharacterGuildData>> guildMembersMap = new Dictionary<long, IReadOnlyList<CharacterGuildData>>();

				foreach (GuildUpdateData update in updates)
				{
					if (updatedGuilds.Contains(update.GuildID))
					{
						continue;
					}
					updatedGuilds.Add(update.GuildID);

					DatabaseResult<IReadOnlyList<CharacterGuildData>> membersResult = await charGuildService.FetchManyAsync(update.GuildID);
					if (membersResult.IsSuccess && membersResult.Data != null)
					{
						guildMembersMap[update.GuildID] = membersResult.Data;
					}
				}

				// Marshal all main-thread state changes + broadcasts
				EnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					// Update last fetch time
					if (Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData rtData))
					{
						rtData.LastFetchTime = DateTime.UtcNow;
					}

					if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mapData))
					{
						return;
					}

					foreach (var kvp in guildMembersMap)
					{
						long guildID = kvp.Key;
						IReadOnlyList<CharacterGuildData> dbMembers = kvp.Value;

						var currentMemberIDs = dbMembers.Select(x => x.CharacterID).ToHashSet();

						// Check if we have previously cached the guild member list
						if (mapData.GuildMemberTracker.TryGetValue(guildID, out var previousMembers))
						{
							// Compute the difference: members that are in previousMembers but not in currentMemberIDs
							List<long> difference = previousMembers.Except(currentMemberIDs).ToList();

							foreach (long memberID in difference)
							{
								// Tell the member connection to leave their guild immediately
								if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var guildCharacterMappingData) &&
									guildCharacterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) &&
									character != null &&
									character.TryGet(out IGuildController targetGuildController))
								{
									targetGuildController.ID = 0;
									Server.NetworkWrapper.Broadcast(character.Owner, new GuildLeaveBroadcast(), true, Channel.Reliable);
								}
							}
						}
						// Cache the guild member IDs
						mapData.GuildMemberTracker[guildID] = currentMemberIDs;

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

						if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
						{
							// Tell all of the local guild members to update their guild member lists
							foreach (CharacterGuildData member in dbMembers)
							{
								if (characterMappingData.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character))
								{
									if (!character.TryGet(out IGuildController guildController) ||
										guildController.ID < 1)
									{
										continue;
									}
									// Update server rank in the case of a membership rank change
									guildController.Rank = (GuildRank)member.Rank;
									Server.NetworkWrapper.Broadcast(character.Owner, guildAddBroadcast, true, Channel.Reliable);
								}
							}
						}
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error fetching/processing guild updates: {ex}");
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
			if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData))
			{
				return;
			}
			var tracker = mappingData.GuildCharacterTracker;
			if (!tracker.TryGetValue(guildID, out HashSet<long> characterIDs))
			{
				tracker.Add(guildID, characterIDs = new HashSet<long>());
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
			if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData))
			{
				return;
			}
			if (mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> characterIDs))
			{
				characterIDs.Remove(characterID);

				// If there are no active guild members we can remove the character and member trackers for the guild.
				if (characterIDs.Count < 1)
				{
					mappingData.GuildCharacterTracker.Remove(guildID);
					mappingData.GuildMemberTracker.Remove(guildID);
				}
			}
		}

		/// <summary>
		/// Handles character connect event, adding the character to the guild tracker and persisting guild update.
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">The character that connected.</param>
		public void CharacterSystem_OnConnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
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

			// Fire-and-forget async DB persist
			long characterID = character.ID;
			long guildID = guildController.ID;
			byte rank = (byte)guildController.Rank;
			string sceneName = character.SceneName;

			EnqueueAsyncWork(() => PersistGuildMemberAsync(characterID, guildID, rank, sceneName));
		}

		/// <summary>
		/// Handles character disconnect event, removing the character from the guild tracker and persisting guild update.
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">The character that disconnected.</param>
		public void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character != null && Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
			{
				runtimeData.PendingInvitations.Remove(character.ID);
			}

			if (character == null)
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
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

			// Fire-and-forget async DB persist with "Offline" location
			long characterID = character.ID;
			long guildID = guildController.ID;
			byte rank = (byte)guildController.Rank;

			EnqueueAsyncWork(() => PersistGuildMemberAsync(characterID, guildID, rank, "Offline"));
		}

		/// <summary>
		/// Asynchronously persists a guild member's data and triggers a guild update notification.
		/// </summary>
		private async Task PersistGuildMemberAsync(long characterID, long guildID, byte rank, string location)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var charGuildService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IGuildUpdateService>(out var guildUpdateService))
				{
					return;
				}

				// Fetch existing version for sequence-based optimistic concurrency
				long version = 1;
				DatabaseResult<CharacterGuildData?> existingResult = await charGuildService.FetchAsync(characterID);
				if (existingResult.IsSuccess && existingResult.Data.HasValue)
				{
					version = existingResult.Data.Value.Version + 1;
				}

				CharacterGuildData guildData = new CharacterGuildData(0, version, characterID, guildID, rank, location);
				await charGuildService.PersistAsync(guildData, maxGuildSize);
				await guildUpdateService.PersistAsync(guildID);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error persisting guild member (CharID={characterID}, GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Handles guild creation broadcast, validates and creates a new guild for the requesting character.
		/// Fires an async task that checks name availability, creates the guild, persists membership,
		/// and marshals the result back to the main thread.
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
			if (Server?.Database?.ServiceRegistry == null)
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

			// Capture immutable data for the async path
			long characterID = guildController.Character.ID;
			string guildName = msg.GuildName;
			string sceneName = conn.FirstObject.gameObject.scene.name;

			EnqueueAsyncWork(() => CreateGuildAsync(conn, characterID, guildName, sceneName));
		}

		/// <summary>
		/// Asynchronously checks guild name availability, creates the guild, persists membership,
		/// and marshals in-memory state changes + Broadcasts back to the main thread.
		/// </summary>
		private async Task CreateGuildAsync(NetworkConnection conn, long characterID, string guildName, string sceneName)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<IGuildService>(out var guildService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var charGuildService))
				{
					return;
				}

				// Check if guild name already exists
				DatabaseResult<bool> existsResult = await guildService.ExistsAsync(guildName);
				if (!existsResult.IsSuccess)
				{
					return;
				}
				if (existsResult.Data)
				{
					EnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive) return;
						Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
						{
							Result = GuildResultType.NameAlreadyExists,
						}, true, Channel.Reliable);
					});
					return;
				}

				// Create the guild — PersistAsync returns the new guild ID
				DatabaseResult<long?> createResult = await guildService.PersistAsync(guildName);
				if (!createResult.IsSuccess || !createResult.Data.HasValue)
				{
					return;
				}

				long newGuildID = createResult.Data.Value;

				// Save the character as guild leader
				CharacterGuildData memberData = new CharacterGuildData(0, 1, characterID, newGuildID, (byte)GuildRank.Leader, sceneName);
				await charGuildService.PersistAsync(memberData, maxGuildSize);

				// Marshal in-memory state changes + Broadcast back to main thread
				EnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null) return;

					IGuildController gc = conn.FirstObject.GetComponent<IGuildController>();
					if (gc == null || gc.ID > 0) return;

					gc.ID = newGuildID;
					gc.Rank = GuildRank.Leader;

					AddGuildCharacterTracker(gc.ID, characterID);

					// tell the character we made their guild successfully
					Server.NetworkWrapper.Broadcast(conn, new GuildAddBroadcast()
					{
						GuildID = gc.ID,
						CharacterID = characterID,
						Rank = gc.Rank,
						Location = sceneName,
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error creating guild '{guildName}' for CharID={characterID}: {ex}");
			}
		}

		/// <summary>
		/// Handles guild invitation broadcast, validates inviter and target, and sends invitation to the target character.
		/// Only guild leaders or officers can invite, and invitations are tracked to prevent duplicates.
		/// Fires an async task to verify guild capacity before sending the invite.
		/// </summary>
		/// <param name="conn">Network connection of the inviter.</param>
		/// <param name="msg">GuildInviteBroadcast message containing inviter and target IDs.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildInviteBroadcastReceived(NetworkConnection conn, GuildInviteBroadcast msg, Channel channel)
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (conn.FirstObject == null)
			{
				return;
			}
			IGuildController inviter = conn.FirstObject.GetComponent<IGuildController>();

			// validate guild leader or officer is inviting
			if (inviter == null ||
				inviter.ID < 1 ||
				inviter.Character.ID == msg.TargetCharacterID ||
				!(inviter.Rank == GuildRank.Leader | inviter.Rank == GuildRank.Officer))
			{
				return;
			}

			// Capture immutable data for async path
			long inviterCharacterID = inviter.Character.ID;
			long guildID = inviter.ID;
			long targetCharacterID = msg.TargetCharacterID;

			EnqueueAsyncWork(() => InviteToGuildAsync(conn, inviterCharacterID, guildID, targetCharacterID));
		}

		/// <summary>
		/// Asynchronously verifies guild capacity and marshals the invite back to the main thread.
		/// </summary>
		private async Task InviteToGuildAsync(NetworkConnection conn, long inviterCharacterID, long guildID, long targetCharacterID)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var charGuildService))
				{
					return;
				}

				// Check guild is not full
				DatabaseResult<int> countResult = await charGuildService.CountAsync(guildID);
				if (!countResult.IsSuccess || countResult.Data >= maxGuildSize)
				{
					return;
				}

				// Marshal invite logic back to main thread
				EnqueueMainThread(() =>
				{
					if (!Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
					{
						return;
					}

					// if the target doesn't already have a pending invite
					if (!runtimeData.PendingInvitations.ContainsKey(targetCharacterID) &&
						Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) &&
						characterMappingData.CharactersByID.TryGetValue(targetCharacterID, out IPlayerCharacter targetCharacter) &&
						targetCharacter.TryGet(out IGuildController targetGuildController))
					{
						// validate target
						if (targetGuildController.ID > 0)
						{
							// we should tell the inviter the target is already in a guild
							if (conn != null && conn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
								{
									Channel = ChatChannel.Guild,
									SenderID = targetCharacterID,
									Text = ChatHelper.GUILD_ERROR_TARGET_IN_GUILD + " ",
								}, true, Channel.Reliable);
							}
							return;
						}

						// add to our list of pending invitations
						runtimeData.PendingInvitations.Add(targetCharacter.ID, inviterCharacterID);
						Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new GuildInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
							TargetCharacterID = targetCharacter.ID
						}, true, Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error inviting to guild (GuildID={guildID}, TargetID={targetCharacterID}): {ex}");
			}
		}

		/// <summary>
		/// Handles acceptance of a guild invitation, validates the invite, fires an async task to check capacity,
		/// persist membership, and marshal results back to the main thread.
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

			if (!Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
			{
				return;
			}

			// validate guild invite
			if (runtimeData.PendingInvitations.TryGetValue(guildController.Character.ID, out long pendingGuildID))
			{
				runtimeData.PendingInvitations.Remove(guildController.Character.ID);

				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				// Capture immutable data for async path
				long characterID = guildController.Character.ID;
				string sceneName = conn.FirstObject.gameObject.scene.name;

				EnqueueAsyncWork(() => AcceptGuildInviteAsync(conn, characterID, pendingGuildID, sceneName));
			}
		}

		/// <summary>
		/// Asynchronously checks guild capacity, persists membership, notifies other servers,
		/// and marshals state changes + Broadcast back to the main thread.
		/// </summary>
		private async Task AcceptGuildInviteAsync(NetworkConnection conn, long characterID, long guildID, string sceneName)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var charGuildService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IGuildUpdateService>(out var guildUpdateService))
				{
					return;
				}

				// Check guild capacity
				DatabaseResult<int> countResult = await charGuildService.CountAsync(guildID);
				if (!countResult.IsSuccess || countResult.Data >= maxGuildSize)
				{
					return;
				}

				// Persist membership
				CharacterGuildData memberData = new CharacterGuildData(0, 1, characterID, guildID, (byte)GuildRank.Member, sceneName);
				DatabaseResult saveResult = await charGuildService.PersistAsync(memberData, maxGuildSize);
				if (!saveResult.IsSuccess)
				{
					return;
				}

				// Tell the other servers to update their guild lists
				await guildUpdateService.PersistAsync(guildID);

				// Marshal state changes + Broadcast back to main thread
				EnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null) return;

					IGuildController gc = conn.FirstObject.GetComponent<IGuildController>();
					if (gc == null || gc.ID > 0) return;

					gc.ID = guildID;
					gc.Rank = GuildRank.Member;

					AddGuildCharacterTracker(gc.ID, characterID);

					// tell the new member they joined immediately, other clients will catch up with the GuildUpdate pass
					Server.NetworkWrapper.Broadcast(conn, new GuildAddBroadcast()
					{
						GuildID = gc.ID,
						CharacterID = characterID,
						Rank = GuildRank.Member,
						Location = sceneName,
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error accepting guild invite (CharID={characterID}, GuildID={guildID}): {ex}");
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
			if (character != null && Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
			{
				runtimeData.PendingInvitations.Remove(character.ID);
			}
		}

		/// <summary>
		/// Handles guild leave broadcast, validates character, captures necessary data,
		/// and fires an async task that handles leadership transfer, member removal, and guild cleanup.
		/// </summary>
		/// <param name="conn">Network connection of the leaving character.</param>
		/// <param name="msg">GuildLeaveBroadcast message containing leave details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildLeaveBroadcastReceived(NetworkConnection conn, GuildLeaveBroadcast msg, Channel channel)
		{
			if (Server?.Database?.ServiceRegistry == null)
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

			// Capture immutable data for async path
			long characterID = guildController.Character.ID;
			long guildID = guildController.ID;
			GuildRank rank = guildController.Rank;

			// Immediately update in-memory state on main thread
			guildController.ID = 0;
			guildController.Rank = GuildRank.None;

			RemoveGuildCharacterTracker(guildID, characterID);

			// Tell character that they left the guild immediately
			Server.NetworkWrapper.Broadcast(conn, new GuildLeaveBroadcast(), true, Channel.Reliable);

			// Fire-and-forget async DB operations
			EnqueueAsyncWork(() => LeaveGuildAsync(characterID, guildID, rank));
		}

		/// <summary>
		/// Asynchronously handles guild leave: fetches members for leadership transfer,
		/// removes the member, and either deletes or updates the guild.
		/// </summary>
		private async Task LeaveGuildAsync(long characterID, long guildID, GuildRank rank)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var charGuildService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IGuildService>(out var guildService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IGuildUpdateService>(out var guildUpdateService))
				{
					return;
				}

				// Fetch current members to determine leadership transfer
				DatabaseResult<IReadOnlyList<CharacterGuildData>> membersResult = await charGuildService.FetchManyAsync(guildID);
				if (!membersResult.IsSuccess || membersResult.Data == null)
				{
					return;
				}

				IReadOnlyList<CharacterGuildData> members = membersResult.Data;
				int remainingCount = members.Count - 1;

				// Handle leadership transfer if the leaving member is the leader
				if (rank == GuildRank.Leader && remainingCount > 0)
				{
					List<CharacterGuildData> officers = new List<CharacterGuildData>();
					List<CharacterGuildData> remainingMembers = new List<CharacterGuildData>();

					foreach (CharacterGuildData member in members)
					{
						if (member.CharacterID == characterID)
						{
							continue;
						}

						if (member.Rank == (byte)GuildRank.Officer)
						{
							officers.Add(member);
						}
						remainingMembers.Add(member);
					}

					CharacterGuildData? newLeader = null;
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
					if (newLeader.HasValue)
					{
						await charGuildService.UpdateRankAsync(newLeader.Value.CharacterID, newLeader.Value.GuildID, (byte)GuildRank.Leader, newLeader.Value.Version + 1);
					}
				}

				// Remove the guild member
				await charGuildService.DeleteAsync(characterID, long.MaxValue);

				if (remainingCount < 1)
				{
					// Delete the guild entirely
					await guildService.DeleteAsync(guildID);
					await guildUpdateService.DeleteAsync(guildID);
				}
				else
				{
					// Tell the other servers to update their guild lists
					await guildUpdateService.PersistAsync(guildID);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error leaving guild (CharID={characterID}, GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Handles guild member removal broadcast, validates and removes a member from the guild.
		/// Only officers and leaders can remove other members.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">GuildRemoveBroadcast message containing member ID to remove.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildRemoveBroadcastReceived(NetworkConnection conn, GuildRemoveBroadcast msg, Channel channel)
		{
			if (Server?.Database?.ServiceRegistry == null)
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

			// Capture immutable data for async path
			long guildID = guildController.ID;
			long memberID = msg.GuildMemberID;
			long characterID = guildController.Character.ID;
			GuildRank requesterRank = guildController.Rank;

			EnqueueAsyncWork(() => RemoveGuildMemberAsync(guildID, memberID, characterID, requesterRank));
		}

		/// <summary>
		/// Asynchronously removes a guild member, validates rank permissions, and triggers guild update.
		/// Marshals tracker cleanup back to the main thread.
		/// </summary>
		private async Task RemoveGuildMemberAsync(long guildID, long memberID, long requesterCharacterID, GuildRank requesterRank)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var charGuildService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IGuildUpdateService>(out var guildUpdateService))
				{
					return;
				}

				// Verify the target member exists and check rank permission
				DatabaseResult<CharacterGuildData?> memberResult = await charGuildService.FetchAsync(memberID);
				if (!memberResult.IsSuccess || !memberResult.Data.HasValue)
				{
					return;
				}

				CharacterGuildData targetMember = memberResult.Data.Value;

				// Verify target is in the same guild
				if (targetMember.GuildID != guildID)
				{
					return;
				}

				// Rank permission check: can't kick someone of equal or higher rank
				if ((GuildRank)targetMember.Rank >= requesterRank)
				{
					return;
				}

				// Delete the member
				DatabaseResult deleteResult = await charGuildService.DeleteAsync(memberID, long.MaxValue);
				if (!deleteResult.IsSuccess)
				{
					return;
				}

				// Tell the other servers to update their guild lists
				await guildUpdateService.PersistAsync(guildID);

				// Marshal tracker cleanup to main thread
				EnqueueMainThread(() =>
				{
					RemoveGuildCharacterTracker(guildID, memberID);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error removing guild member (GuildID={guildID}, MemberID={memberID}): {ex}");
			}
		}

		/// <summary>
		/// Handles guild rank change broadcast, validates leader and target, and fires an async task
		/// to update ranks in the database.
		/// Only guild leaders can promote another member to a new rank.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">GuildChangeRankBroadcast message containing target member ID and new rank.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildChangeRankBroadcastReceived(NetworkConnection conn, GuildChangeRankBroadcast msg, Channel channel)
		{
			if (Server?.Database?.ServiceRegistry == null)
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

			// Capture immutable data for async path
			long guildID = guildController.ID;
			long memberID = msg.GuildMemberID;
			GuildRank newRank = msg.Rank;

			EnqueueAsyncWork(() => ChangeGuildRankAsync(guildID, memberID, newRank));
		}

		/// <summary>
		/// Asynchronously updates a guild member's rank and triggers a guild update notification.
		/// </summary>
		private async Task ChangeGuildRankAsync(long guildID, long memberID, GuildRank newRank)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var charGuildService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IGuildUpdateService>(out var guildUpdateService))
				{
					return;
				}

				DatabaseResult rankResult = await charGuildService.UpdateRankAsync(memberID, guildID, (byte)newRank, long.MaxValue);
				if (rankResult.IsSuccess)
				{
					// Tell the other servers to update their guild lists
					await guildUpdateService.PersistAsync(guildID);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error changing guild rank (GuildID={guildID}, MemberID={memberID}): {ex}");
			}
		}

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// </summary>
		private void EnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					asyncWorker.Enqueue(work, entityKey, callerName);
				else
					asyncWorker.Enqueue(work, callerName);
			}
		}
	}
}