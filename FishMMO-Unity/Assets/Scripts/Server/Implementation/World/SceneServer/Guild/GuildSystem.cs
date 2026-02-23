using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
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
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max guild-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Maximum number of members allowed per guild.
		/// </summary>
		[SerializeField]
		private int maxGuildSize = 100;
		/// <summary>
		/// Maximum allowed guild name length.
		/// </summary>
		[SerializeField]
		private int maxGuildNameLength = 64;
		/// <summary>
		/// Periodic guild update polling interval in seconds.
		/// </summary>
		[Tooltip("The server guild update pump rate limit in seconds.")]
		[SerializeField]
		private float updatePumpRate = 1.0f;

		/// <summary>
		/// Invitation lifetime in seconds before automatic expiration.
		/// </summary>
		[Header("Invitation Protection")]
		[Tooltip("Invitation lifetime in seconds before automatic expiration")]
		[SerializeField] private float invitationTtlSeconds = 45.0f;

		/// <summary>
		/// Interval between invitation cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded invitation cleanup sweeps")]
		[SerializeField] private float invitationSweepIntervalSeconds = 1.0f;

		/// <summary>
		/// Maximum invitation entries scanned per cleanup sweep.
		/// </summary>
		[Tooltip("Max invitation entries scanned per sweep")]
		[SerializeField] private int invitationSweepMaxScan = 128;

		/// <summary>
		/// Maximum invitation entries removed per cleanup sweep.
		/// </summary>
		[Tooltip("Max invitation entries removed per sweep")]
		[SerializeField] private int invitationSweepMaxRemove = 128;

		/// <summary>
		/// Debounce window in milliseconds for guild ingress operations.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between guild requests per connection and operation")]
		[SerializeField] private int ingressDebounceMilliseconds = 100;

		/// <summary>
		/// Interval in seconds between ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum stale ingress entries removed per sweep.
		/// </summary>
		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>
		/// Operation keys used by guild ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			Create = 1,
			Invite = 2,
			AcceptInvite = 3,
			DeclineInvite = 4,
			Leave = 5,
			Remove = 6,
			ChangeRank = 7,
		}

		/// <summary>
		/// Gets the update pump rate for guild synchronization.
		/// </summary>
		public float UpdatePumpRate { get { return updatePumpRate; } }

		/// <summary>
		/// Maximum number of members allowed in a guild.
		/// </summary>
		public int MaxGuildSize { get { return maxGuildSize; } }
		/// <summary>
		/// Maximum length allowed for a guild name.
		/// </summary>
		public int MaxGuildNameLength { get { return maxGuildNameLength; } }

		/// <summary>
		/// Handles guild invite chat commands.
		/// </summary>
		/// <param name="sender">The character sending the invite.</param>
		/// <param name="msg">Chat broadcast message containing the target character name.</param>
		/// <returns>True if invite was sent, false otherwise.</returns>
		public bool OnGuildInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null || string.IsNullOrWhiteSpace(msg.Text))
			{
				return false;
			}

			string characterName = msg.Text.Trim().ToLowerInvariant();
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
			Dictionary<string, ChatCommand> guildChatCommands = new Dictionary<string, ChatCommand>()
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

			if (!Server.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("GuildSystem", "Failed to initialize: IGuildSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			invitationTtlSeconds = Mathf.Max(5.0f, invitationTtlSeconds);
			invitationSweepIntervalSeconds = Mathf.Max(0.1f, invitationSweepIntervalSeconds);
			invitationSweepMaxScan = Mathf.Max(1, invitationSweepMaxScan);
			invitationSweepMaxRemove = Mathf.Max(1, invitationSweepMaxRemove);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);
			runtimeData.EndUpdatePump();
			runtimeData.NextInvitationSweepUtc = DateTime.UtcNow;

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
			DrainMainThreadQueue(drainAll: true);

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
		private void DrainMainThreadQueue(bool drainAll)
		{
			MainThreadQueueHelper.Drain<IGuildSystemMainThreadQueueData>(Server, maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return MainThreadQueueHelper.TryEnqueue<IGuildSystemMainThreadQueueData>(Server, action);
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			SweepPendingInvitations();
			SweepIngressGuards();
		}

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Releases an ingress in-flight guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server?.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData) == true)
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Performs bounded cleanup of stale ingress guard entries.
		/// </summary>
		private void SweepIngressGuards()
		{
			if (Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Performs a bounded TTL sweep over pending guild invitations.
		/// </summary>
		private void SweepPendingInvitations()
		{
			if (!Server.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < runtimeData.NextInvitationSweepUtc)
			{
				return;
			}

			runtimeData.NextInvitationSweepUtc = nowUtc.AddSeconds(invitationSweepIntervalSeconds);

			runtimeData.SweepExpiredInvitations(
				nowUtc,
				TimeSpan.FromSeconds(invitationTtlSeconds),
				invitationSweepMaxScan,
				invitationSweepMaxRemove);
		}

		/// <summary>
		/// Periodic callback that fetches and processes guild updates from the database asynchronously.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (!runtimeData.TryBeginUpdatePump())
			{
				return;
			}

			// Snapshot main-thread-only Dictionary keys before going async (C4 fix).
			if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) ||
				mappingData.GuildCharacterTracker.Count == 0)
			{
				runtimeData.EndUpdatePump();
				return;
			}

			List<long> guildIds = new List<long>(mappingData.GuildCharacterTracker.Keys);
			DateTime lastFetch = runtimeData.LastFetchTime;

			if (!TryEnqueueAsyncWork(() => FetchAndProcessGuildUpdatesAsync(guildIds, lastFetch)))
			{
				runtimeData.EndUpdatePump();
			}
		}

		/// <summary>
		/// Asynchronously fetches guild updates from the database and marshals the processing back to the main thread.
		/// </summary>
		/// <returns>Asynchronous fetch-and-process task.</returns>
		private async Task FetchAndProcessGuildUpdatesAsync(List<long> guildIds, DateTime lastFetch)
		{
			try
			{
				if (!TryGetDbService(out IGuildUpdateService guildUpdateService) ||
					!TryGetDbService(out ICharacterGuildService charGuildService))
				{
					return;
				}

				if (guildIds.Count == 0)
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
				TryEnqueueMainThread(() =>
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

						var currentMemberIDs = new HashSet<long>(dbMembers.Count);
						for (int i = 0; i < dbMembers.Count; i++)
						{
							currentMemberIDs.Add(dbMembers[i].CharacterID);
						}

						// Check if we have previously cached the guild member list
						if (mapData.GuildMemberTracker.TryGetValue(guildID, out var previousMembers))
						{
							// Compute the difference: members that are in previousMembers but not in currentMemberIDs
							var difference = new List<long>();
							foreach (long prevID in previousMembers)
							{
								if (!currentMemberIDs.Contains(prevID))
								{
									difference.Add(prevID);
								}
							}

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

						var addBroadcasts = new List<GuildAddBroadcast>(dbMembers.Count);
						for (int i = 0; i < dbMembers.Count; i++)
						{
							var x = dbMembers[i];
							addBroadcasts.Add(new GuildAddBroadcast()
							{
								GuildID = x.GuildID,
								CharacterID = x.CharacterID,
								Rank = (GuildRank)x.Rank,
								Location = x.Location,
							});
						}

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
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.EndUpdatePump();
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

			TryEnqueueAsyncWork(() => PersistGuildMemberAsync(characterID, guildID, rank, sceneName), characterID);
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
				runtimeData.RemovePendingInvitation(character.ID);
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

			TryEnqueueAsyncWork(() => PersistGuildMemberAsync(characterID, guildID, rank, "Offline"), characterID);
		}

		/// <summary>
		/// Asynchronously persists a guild member's data and triggers a guild update notification.
		/// </summary>
		/// <param name="characterID">Character identifier to persist.</param>
		/// <param name="guildID">Guild identifier associated with the character.</param>
		/// <param name="rank">Guild rank value to persist.</param>
		/// <param name="location">Current member location label.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistGuildMemberAsync(long characterID, long guildID, byte rank, string location)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Create, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
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

				deferGuardRelease = TryEnqueueIngressWork(() => CreateGuildAsync(conn, characterID, guildName, sceneName), guardKey, characterID);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously checks guild name availability, creates the guild, persists membership,
		/// and marshals in-memory state changes + Broadcasts back to the main thread.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="characterID">Requesting character identifier.</param>
		/// <param name="guildName">Requested guild name.</param>
		/// <param name="sceneName">Requester scene name.</param>
		/// <returns>Asynchronous guild creation task.</returns>
		private async Task CreateGuildAsync(NetworkConnection conn, long characterID, string guildName, string sceneName)
		{
			try
			{
				if (!TryGetDbService(out IGuildService guildService) ||
					!TryGetDbService(out ICharacterGuildService charGuildService))
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
					TryEnqueueMainThread(() =>
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
				TryEnqueueMainThread(() =>
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Invite, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				IGuildController inviter = conn.FirstObject.GetComponent<IGuildController>();

				// validate guild leader or officer is inviting
				if (inviter == null ||
					inviter.ID < 1 ||
					inviter.Character.ID == msg.TargetCharacterID ||
					!(inviter.Rank == GuildRank.Leader || inviter.Rank == GuildRank.Officer))
				{
					return;
				}

				// Capture immutable data for async path
				long inviterCharacterID = inviter.Character.ID;
				long guildID = inviter.ID;
				long targetCharacterID = msg.TargetCharacterID;

				deferGuardRelease = TryEnqueueIngressWork(() => InviteToGuildAsync(conn, inviterCharacterID, guildID, targetCharacterID), guardKey, inviterCharacterID);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously verifies guild capacity and marshals the invite back to the main thread.
		/// </summary>
		/// <param name="conn">Inviter connection for error feedback.</param>
		/// <param name="inviterCharacterID">Inviter character identifier.</param>
		/// <param name="guildID">Inviter guild identifier.</param>
		/// <param name="targetCharacterID">Target character identifier.</param>
		/// <returns>Asynchronous invite task.</returns>
		private async Task InviteToGuildAsync(NetworkConnection conn, long inviterCharacterID, long guildID, long targetCharacterID)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService))
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
				TryEnqueueMainThread(() =>
				{
					if (!Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
					{
						return;
					}

					// if the target doesn't already have a pending invite
					if (runtimeData.TryAddPendingInvitation(targetCharacterID, guildID, DateTime.UtcNow) &&
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
							runtimeData.RemovePendingInvitation(targetCharacterID);
							return;
						}

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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.AcceptInvite, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
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
				if (runtimeData.TryGetPendingInvitation(guildController.Character.ID, out long pendingGuildID))
				{
					if (Server?.Database?.ServiceRegistry == null)
					{
						return;
					}

					// Capture immutable data for async path
					long characterID = guildController.Character.ID;
					string sceneName = conn.FirstObject.gameObject.scene.name;

					deferGuardRelease = TryEnqueueIngressWork(() => AcceptGuildInviteAsync(conn, characterID, pendingGuildID, sceneName), guardKey, characterID);
				}
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously checks guild capacity, persists membership, notifies other servers,
		/// and marshals state changes + Broadcast back to the main thread.
		/// </summary>
		/// <param name="conn">Accepting connection.</param>
		/// <param name="characterID">Accepting character identifier.</param>
		/// <param name="guildID">Guild identifier from pending invitation.</param>
		/// <param name="sceneName">Current scene name.</param>
		/// <returns>Asynchronous accept-invite task.</returns>
		private async Task AcceptGuildInviteAsync(NetworkConnection conn, long characterID, long guildID, string sceneName)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
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
				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null) return;

					IGuildController gc = conn.FirstObject.GetComponent<IGuildController>();
					if (gc == null || gc.ID > 0) return;

					gc.ID = guildID;
					gc.Rank = GuildRank.Member;

					if (Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
					{
						runtimeData.RemovePendingInvitation(characterID);
					}

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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.DeclineInvite, out long guardKey))
			{
				return;
			}

			try
			{
				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
				if (character != null && Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
				{
					runtimeData.RemovePendingInvitation(character.ID);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Leave, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
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

				deferGuardRelease = TryEnqueueIngressWork(() => LeaveGuildAsync(conn, characterID, guildID, rank), guardKey, characterID);
				if (!deferGuardRelease)
				{
					return;
				}
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously handles guild leave: fetches members for leadership transfer,
		/// removes the member, and either deletes or updates the guild.
		/// </summary>
		/// <param name="conn">Leaving character connection.</param>
		/// <param name="characterID">Leaving character identifier.</param>
		/// <param name="guildID">Guild identifier being left.</param>
		/// <param name="rank">Leaving character rank.</param>
		/// <returns>Asynchronous leave-guild task.</returns>
		private async Task LeaveGuildAsync(NetworkConnection conn, long characterID, long guildID, GuildRank rank)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildService guildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
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

				// Find the leaving member's version for optimistic concurrency on delete
				long leavingMemberVersion = 1;
				foreach (CharacterGuildData member in members)
				{
					if (member.CharacterID == characterID)
					{
						leavingMemberVersion = member.Version + 1;
						break;
					}
				}

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
					var rng = new Random();
					if (officers.Count > 0)
					{
						// pick a random officer
						newLeader = officers[rng.Next(officers.Count)];
					}
					else if (remainingMembers.Count > 0)
					{
						// pick a random member
						newLeader = remainingMembers[rng.Next(remainingMembers.Count)];
					}

					// update the guild leader status in the database
					if (newLeader.HasValue)
					{
						await charGuildService.UpdateRankAsync(newLeader.Value.CharacterID, newLeader.Value.GuildID, (byte)GuildRank.Leader, newLeader.Value.Version + 1);
					}
				}

				// Remove the guild member
				await charGuildService.DeleteAsync(characterID, leavingMemberVersion);

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

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null)
					{
						return;
					}

					IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
					if (guildController == null || guildController.Character.ID != characterID || guildController.ID != guildID)
					{
						return;
					}

					guildController.ID = 0;
					guildController.Rank = GuildRank.None;
					RemoveGuildCharacterTracker(guildID, characterID);

					Server.NetworkWrapper.Broadcast(conn, new GuildLeaveBroadcast(), true, Channel.Reliable);
				});
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Remove, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
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

				deferGuardRelease = TryEnqueueIngressWork(() => RemoveGuildMemberAsync(guildID, memberID, characterID, requesterRank), guardKey, characterID);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously removes a guild member, validates rank permissions, and triggers guild update.
		/// Marshals tracker cleanup back to the main thread.
		/// </summary>
		/// <param name="guildID">Guild identifier containing the target member.</param>
		/// <param name="memberID">Target member character identifier.</param>
		/// <param name="requesterCharacterID">Requester character identifier.</param>
		/// <param name="requesterRank">Requester rank for permission checks.</param>
		/// <returns>Asynchronous remove-member task.</returns>
		private async Task RemoveGuildMemberAsync(long guildID, long memberID, long requesterCharacterID, GuildRank requesterRank)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
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
				DatabaseResult deleteResult = await charGuildService.DeleteAsync(memberID, targetMember.Version + 1);
				if (!deleteResult.IsSuccess)
				{
					return;
				}

				// Tell the other servers to update their guild lists
				await guildUpdateService.PersistAsync(guildID);

				// Marshal tracker cleanup to main thread
				TryEnqueueMainThread(() =>
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.ChangeRank, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
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

				// Validate rank is within assignable range — only Member or Officer are valid targets.
				// Leader promotion is handled separately via leadership transfer.
				if (newRank < GuildRank.Member || newRank >= GuildRank.Leader)
				{
					return;
				}

				deferGuardRelease = TryEnqueueIngressWork(() => ChangeGuildRankAsync(guildID, memberID, newRank), guardKey, guildID);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously updates a guild member's rank and triggers a guild update notification.
		/// </summary>
		/// <param name="guildID">Guild identifier containing the member.</param>
		/// <param name="memberID">Member character identifier.</param>
		/// <param name="newRank">New rank to apply.</param>
		/// <returns>Asynchronous rank-change task.</returns>
		private async Task ChangeGuildRankAsync(long guildID, long memberID, GuildRank newRank)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				// Fetch the member's current version for optimistic concurrency
				DatabaseResult<CharacterGuildData?> memberResult = await charGuildService.FetchAsync(memberID);
				if (!memberResult.IsSuccess || !memberResult.Data.HasValue)
				{
					return;
				}

				DatabaseResult rankResult = await charGuildService.UpdateRankAsync(memberID, guildID, (byte)newRank, memberResult.Data.Value.Version + 1);
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
		/// Enqueues ingress work and guarantees guard release when async processing completes.
		/// </summary>
		private bool TryEnqueueIngressWork(Func<Task> work, long guardKey, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			return TryEnqueueAsyncWork(async () =>
			{
				try
				{
					await work();
				}
				finally
				{
					EndIngressGuard(guardKey);
				}
			}, entityKey, callerName);
		}
	}
}