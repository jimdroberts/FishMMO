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
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages party creation, invitations, membership, and updates for the MMO server.
	/// Handles party broadcasts, chat commands, and synchronizes party state with the database.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread state changes or Broadcasts are marshalled
	/// via IPartySystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "PartySystem", menuName = "FishMMO/Server/SceneServer/Party System", order = 1)]
	[RequiresDataContainer(typeof(PartySystemRuntimeData))]
	[RequiresDataContainer(typeof(PartyCharacterMappingData))]
	[RequiresDataContainer(typeof(PartySystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class PartySystem : ServerBehaviour, IPartySystem<NetworkConnection>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max party-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Maximum number of members allowed in a party.
		/// </summary>
		public int MaxPartySize = 6;
		/// <summary>
		/// The server party update pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server party update pump rate limit in seconds.")]
		[SerializeField]
		public float UpdatePumpRate = 1.0f;

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
		/// Debounce window in milliseconds for party ingress operations.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between party requests per connection and operation")]
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
		/// Operation keys used by party ingress guards.
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
		/// Handles party invite chat commands.
		/// </summary>
		public bool OnPartyInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null || string.IsNullOrWhiteSpace(msg.Text))
			{
				return false;
			}

			string targetName = msg.Text.Trim().ToLowerInvariant();
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(targetName, out IPlayerCharacter character))
			{
				OnServerPartyInviteBroadcastReceived(sender.Owner, new PartyInviteBroadcast()
				{
					InviterCharacterID = sender.ID,
					TargetCharacterID = character.ID,
				}, Channel.Reliable);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Called once to initialize the party system. Registers chat commands, broadcast handlers, and character events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("PartySystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IPartySystemMainThreadQueueData>(out _))
			{
				Log.Error("PartySystem", "Failed to initialize: IPartySystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) ||
				characterSystem == null)
			{
				Log.Error("PartySystem", "Failed to initialize: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Chat commands
			Dictionary<string, ChatCommand> partyChatCommands = new Dictionary<string, ChatCommand>()
			{
				{ "/pi", OnPartyInvite },
				{ "/invite", OnPartyInvite },
			};
			ChatHelper.AddCommands(partyChatCommands);

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyChangeRankBroadcast>(OnServerPartyChangeRankBroadcastReceived, true);

			// Character system events
			characterSystem.OnConnect += CharacterSystem_OnConnect;
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(UpdatePumpRate, OnPeriodicUpdate);
			}

			if (!Server.DataContainerRegistry.TryGet<IPartySystemRuntimeData>(out var runtimeData))
			{
				Log.Error("PartySystem", "Failed to initialize: IPartySystemRuntimeData not found");
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

			Log.Debug("PartySystem", $"Initialized (MaxPartySize={MaxPartySize}, UpdatePumpRate={UpdatePumpRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unregisters broadcast handlers and character events.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("PartySystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyChangeRankBroadcast>(OnServerPartyChangeRankBroadcastReceived);

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
		/// Drains queued main-thread actions from the IPartySystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			MainThreadQueueHelper.Drain<IPartySystemMainThreadQueueData>(Server, maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return MainThreadQueueHelper.TryEnqueue<IPartySystemMainThreadQueueData>(Server, action);
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
			if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
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
			if (Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData) == true)
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Performs bounded cleanup of stale ingress guard entries.
		/// </summary>
		private void SweepIngressGuards()
		{
			if (Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Performs a bounded TTL sweep over pending party invitations.
		/// </summary>
		private void SweepPendingInvitations()
		{
			if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
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
		/// Periodic callback that fetches and processes party updates from the database asynchronously.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IPartySystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (!runtimeData.TryBeginUpdatePump())
			{
				return;
			}

			// Snapshot containers on main thread to avoid accessing them from worker threads
			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData))
			{
				runtimeData.EndUpdatePump();
				return;
			}

			List<long> partyIds = new List<long>(mappingData.PartyCharacterTracker.Keys);
			DateTime lastFetch = runtimeData.LastFetchTime;

			if (!TryEnqueueAsyncWork(() => FetchAndProcessPartyUpdatesAsync(partyIds, lastFetch)))
			{
				runtimeData.EndUpdatePump();
			}
		}

		/// <summary>
		/// Asynchronously fetches party updates from the database and marshals the processing back to the main thread.
		/// </summary>
		/// <returns>Asynchronous fetch-and-process task.</returns>
		private async Task FetchAndProcessPartyUpdatesAsync(List<long> partyIds, DateTime lastFetch)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (partyIds == null || partyIds.Count == 0)
				{
					return;
				}

				// Async DB fetch
				DatabaseResult<List<PartyUpdateData>> fetchResult = await partyUpdateService.FetchAsync(partyIds, lastFetch);
				if (!fetchResult.IsSuccess || fetchResult.Data == null || fetchResult.Data.Count < 1)
				{
					return;
				}

				List<PartyUpdateData> updates = fetchResult.Data;

				// For each unique party that was updated, fetch the current members
				HashSet<long> updatedParties = new HashSet<long>();
				Dictionary<long, IReadOnlyList<CharacterPartyData>> partyMembersMap = new Dictionary<long, IReadOnlyList<CharacterPartyData>>();

				foreach (PartyUpdateData update in updates)
				{
					if (updatedParties.Contains(update.PartyID))
					{
						continue;
					}
					updatedParties.Add(update.PartyID);

					DatabaseResult<IReadOnlyList<CharacterPartyData>> membersResult = await charPartyService.FetchManyAsync(update.PartyID);
					if (membersResult.IsSuccess && membersResult.Data != null)
					{
						partyMembersMap[update.PartyID] = membersResult.Data;
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
					if (Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData rtData))
					{
						rtData.LastFetchTime = DateTime.UtcNow;
					}

					if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mapData))
					{
						return;
					}

					foreach (var kvp in partyMembersMap)
					{
						long partyID = kvp.Key;
						IReadOnlyList<CharacterPartyData> dbMembers = kvp.Value;

						var currentMemberIDs = dbMembers.Select(x => x.CharacterID).ToHashSet();

						// Check if we have previously cached the party member list
						if (mapData.PartyMemberTracker.TryGetValue(partyID, out var previousMembers))
						{
							// Compute the difference: members that are in previousMembers but not in currentMemberIDs
							List<long> difference = previousMembers.Except(currentMemberIDs).ToList();

							foreach (long memberID in difference)
							{
								// Tell the member connection to leave their party immediately
								if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var partyCharacterMappingData) &&
									partyCharacterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) &&
									character != null &&
									character.TryGet(out IPartyController targetPartyController))
								{
									targetPartyController.ID = 0;
									Server.NetworkWrapper.Broadcast(character.Owner, new PartyLeaveBroadcast(), true, Channel.Reliable);
								}
							}
						}
						// Cache the party member IDs
						mapData.PartyMemberTracker[partyID] = currentMemberIDs;

						var addBroadcasts = dbMembers.Select(x => new PartyAddBroadcast()
						{
							PartyID = x.PartyID,
							CharacterID = x.CharacterID,
							Rank = (PartyRank)x.Rank,
							HealthPCT = x.HealthPCT,
						}).ToList();

						PartyAddMultipleBroadcast partyAddBroadcast = new PartyAddMultipleBroadcast()
						{
							Members = addBroadcasts,
						};

						if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
						{
							// Tell all of the local party members to update their party member lists
							foreach (CharacterPartyData member in dbMembers)
							{
								if (characterMappingData.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character))
								{
									if (!character.TryGet(out IPartyController partyController) ||
										partyController.ID < 1)
									{
										continue;
									}
									partyController.Rank = (PartyRank)member.Rank;
									Server.NetworkWrapper.Broadcast(character.Owner, partyAddBroadcast, true, Channel.Reliable);
								}
							}
						}
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error fetching/processing party updates: {ex}");
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<IPartySystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.EndUpdatePump();
				}
			}
		}

		/// <summary>
		/// Adds a mapping for the Party to Party Members connected to this Scene Server.
		/// </summary>
		public void AddPartyCharacterTracker(long partyID, long characterID)
		{
			if (partyID == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData))
			{
				return;
			}
			var tracker = mappingData.PartyCharacterTracker;
			if (!tracker.TryGetValue(partyID, out HashSet<long> characterIDs))
			{
				tracker.Add(partyID, characterIDs = new HashSet<long>());
			}
			if (!characterIDs.Contains(characterID))
			{
				characterIDs.Add(characterID);
			}
		}

		/// <summary>
		/// Removes the mapping of Party to Party Members connected to this Scene Server.
		/// </summary>
		public void RemovePartyCharacterTracker(long partyID, long characterID)
		{
			if (partyID == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData))
			{
				return;
			}
			if (mappingData.PartyCharacterTracker.TryGetValue(partyID, out HashSet<long> characterIDs))
			{
				characterIDs.Remove(characterID);

				// If there are no active party members we can remove the character and member trackers for the party.
				if (characterIDs.Count < 1)
				{
					mappingData.PartyCharacterTracker.Remove(partyID);
					mappingData.PartyMemberTracker.Remove(partyID);
				}
			}
		}

		/// <summary>
		/// Handles character connect event, adding the character to the party tracker and saving party update.
		/// </summary>
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

			if (!character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				// not in a Party
				return;
			}

			AddPartyCharacterTracker(partyController.ID, character.ID);

			// Fire-and-forget async DB persist
			long characterID = character.ID;
			long partyID = partyController.ID;
			byte rank = (byte)partyController.Rank;
			float healthPCT = character.TryGet(out ICharacterAttributeController attrController)
				? attrController.GetHealthResourceAttributeCurrentPercentage()
				: 0.0f;

			TryEnqueueAsyncWork(() => PersistPartyMemberAndNotifyAsync(characterID, partyID, rank, healthPCT), characterID);
		}

		/// <summary>
		/// Handles character disconnect event, removing the character from the party tracker and saving party update.
		/// </summary>
		public void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character != null && Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
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

			if (!character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				// not in a Party
				return;
			}

			RemovePartyCharacterTracker(partyController.ID, character.ID);

			// Fire-and-forget async DB persist
			long partyID = partyController.ID;
			TryEnqueueAsyncWork(() => PersistPartyUpdateAsync(partyID), character.ID);
		}

		/// <summary>
		/// Asynchronously persists a party member's data and triggers a party update notification.
		/// </summary>
		/// <param name="characterID">Character identifier to persist.</param>
		/// <param name="partyID">Party identifier associated with the character.</param>
		/// <param name="rank">Party rank value to persist.</param>
		/// <param name="healthPCT">Current health percentage snapshot.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistPartyMemberAndNotifyAsync(long characterID, long partyID, byte rank, float healthPCT)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				// Fetch existing version for sequence-based optimistic concurrency
				long version = 1;
				DatabaseResult<CharacterPartyData?> existingResult = await charPartyService.FetchAsync(characterID);
				if (existingResult.IsSuccess && existingResult.Data.HasValue)
				{
					version = existingResult.Data.Value.Version + 1;
				}

				CharacterPartyData partyData = new CharacterPartyData(0, version, characterID, partyID, rank, healthPCT);
				await charPartyService.PersistAsync(partyData, MaxPartySize);
				await partyUpdateService.PersistAsync(partyID);
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error persisting party member (CharID={characterID}, PartyID={partyID}): {ex}");
			}
		}

		/// <summary>
		/// Asynchronously persists a party update notification.
		/// </summary>
		/// <param name="partyID">Party identifier to mark as updated.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistPartyUpdateAsync(long partyID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				await partyUpdateService.PersistAsync(partyID);
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error persisting party update (PartyID={partyID}): {ex}");
			}
		}

		/// <summary>
		/// Handles party creation broadcast, validates and creates a new party for the requesting character.
		/// </summary>
		public void OnServerPartyCreateBroadcastReceived(NetworkConnection conn, PartyCreateBroadcast msg, Channel channel)
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

				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
				if (partyController == null || partyController.ID > 0)
				{
					// already in a party
					return;
				}

				// Capture immutable data for the async path
				long characterID = partyController.Character.ID;
				string sceneName = conn.FirstObject.gameObject.scene.name;
				float healthPCT = partyController.Character.TryGet(out ICharacterAttributeController attributeController)
					? attributeController.GetHealthResourceAttributeCurrentPercentage()
					: 0.0f;

				deferGuardRelease = TryEnqueueIngressWork(() => CreatePartyAsync(conn, characterID, sceneName, healthPCT), guardKey, characterID);
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
		/// Asynchronously creates a new party, persists membership, and marshals state changes back to the main thread.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="characterID">Requesting character identifier.</param>
		/// <param name="sceneName">Current scene name for broadcast context.</param>
		/// <param name="healthPCT">Current requester health percentage.</param>
		/// <returns>Asynchronous party-creation task.</returns>
		private async Task CreatePartyAsync(NetworkConnection conn, long characterID, string sceneName, float healthPCT)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IPartyService>(out var partyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}

				DatabaseResult<long> createResult = await partyService.CreateAsync();
				if (!createResult.IsSuccess)
				{
					return;
				}

				long newPartyID = createResult.Data;

				CharacterPartyData partyData = new CharacterPartyData(0, 1, characterID, newPartyID, (byte)PartyRank.Leader, healthPCT);
				DatabaseResult persistResult = await charPartyService.PersistAsync(partyData, MaxPartySize);
				if (!persistResult.IsSuccess)
				{
					return;
				}

				// Marshal state changes + broadcast to main thread
				TryEnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					IPartyController pc = conn.FirstObject?.GetComponent<IPartyController>();
					if (pc == null)
					{
						return;
					}

					pc.ID = newPartyID;
					pc.Rank = PartyRank.Leader;

					AddPartyCharacterTracker(newPartyID, characterID);

					// tell the character we made their party successfully
					Server.NetworkWrapper.Broadcast(conn, new PartyCreateBroadcast()
					{
						PartyID = newPartyID,
						Location = sceneName,
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error creating party (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Handles party invitation broadcast, validates inviter and target, and sends invitation to the target character.
		/// Only party leaders can invite, and invitations are tracked to prevent duplicates.
		/// </summary>
		/// <param name="conn">Network connection of the inviter.</param>
		/// <param name="msg">PartyInviteBroadcast message containing inviter and target IDs.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyInviteBroadcastReceived(NetworkConnection conn, PartyInviteBroadcast msg, Channel channel)
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
				IPartyController inviter = conn.FirstObject.GetComponent<IPartyController>();

				// validate party leader is inviting
				if (inviter == null ||
					inviter.ID < 1 ||
					inviter.Rank != PartyRank.Leader)
				{
					return;
				}

				// Capture immutable data for the async path
				long inviterPartyID = inviter.ID;
				long inviterCharacterID = inviter.Character.ID;
				long targetCharacterID = msg.TargetCharacterID;

				deferGuardRelease = TryEnqueueIngressWork(() => ValidateAndSendPartyInviteAsync(conn, inviterPartyID, inviterCharacterID, targetCharacterID), guardKey, inviterCharacterID);
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
		/// Asynchronously validates party capacity and marshals the invitation back to the main thread.
		/// </summary>
		/// <param name="conn">Inviter connection for feedback sends.</param>
		/// <param name="inviterPartyID">Inviter party identifier.</param>
		/// <param name="inviterCharacterID">Inviter character identifier.</param>
		/// <param name="targetCharacterID">Target character identifier.</param>
		/// <returns>Asynchronous invite-validation task.</returns>
		private async Task ValidateAndSendPartyInviteAsync(NetworkConnection conn, long inviterPartyID, long inviterCharacterID, long targetCharacterID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}

				// Check that the party is not full
				DatabaseResult<int> countResult = await charPartyService.CountAsync(inviterPartyID);
				if (!countResult.IsSuccess || countResult.Data >= MaxPartySize)
				{
					return;
				}

				// Marshal the invitation logic back to the main thread
				TryEnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
					{
						return;
					}

					// if the target doesn't already have a pending invite
					if (runtimeData.TryAddPendingInvitation(targetCharacterID, inviterPartyID, DateTime.UtcNow) &&
						Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) &&
						characterMappingData.CharactersByID.TryGetValue(targetCharacterID, out IPlayerCharacter targetCharacter) &&
						targetCharacter.TryGet(out IPartyController targetPartyController))
					{
						// validate target
						if (targetPartyController.ID > 0)
						{
							// we should tell the inviter the target is already in a party
							Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
							{
								Channel = ChatChannel.Party,
								SenderID = targetCharacterID,
								Text = ChatHelper.PARTY_ERROR_TARGET_IN_PARTY + " ",
							}, true, Channel.Reliable);
							runtimeData.RemovePendingInvitation(targetCharacterID);
							return;
						}

						Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new PartyInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
							TargetCharacterID = targetCharacter.ID
						}, true, Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error validating party invite (PartyID={inviterPartyID}): {ex}");
			}
		}

		/// <summary>
		/// Handles acceptance of a party invitation, validates the invite, adds the character to the party, and broadcasts the update.
		/// </summary>
		/// <param name="conn">Network connection of the accepting character.</param>
		/// <param name="msg">PartyAcceptInviteBroadcast message containing acceptance details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyAcceptInviteBroadcastReceived(NetworkConnection conn, PartyAcceptInviteBroadcast msg, Channel channel)
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
				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

				// validate character
				if (partyController == null || partyController.ID > 0)
				{
					return;
				}

				if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
				{
					return;
				}

				// validate party invite
				if (runtimeData.TryGetPendingInvitation(partyController.Character.ID, out long pendingPartyID))
				{
					if (Server?.Database?.ServiceRegistry == null)
					{
						return;
					}

					// Capture immutable data for the async path
					long characterID = partyController.Character.ID;
					bool attributesExist = partyController.Character.TryGet(out ICharacterAttributeController attributeController);
					float healthPCT = attributesExist ? attributeController.GetHealthResourceAttributeCurrentPercentage() : 1.0f;

					deferGuardRelease = TryEnqueueIngressWork(() => AcceptPartyInviteAsync(conn, characterID, pendingPartyID, healthPCT), guardKey, characterID);
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
		/// Asynchronously validates party capacity, persists membership, and marshals state changes back to the main thread.
		/// </summary>
		/// <param name="conn">Accepting connection.</param>
		/// <param name="characterID">Accepting character identifier.</param>
		/// <param name="partyID">Party identifier from pending invitation.</param>
		/// <param name="healthPCT">Current accepter health percentage.</param>
		/// <returns>Asynchronous accept-invite task.</returns>
		private async Task AcceptPartyInviteAsync(NetworkConnection conn, long characterID, long partyID, float healthPCT)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				// Check party capacity
				DatabaseResult<IReadOnlyList<CharacterPartyData>> membersResult = await charPartyService.FetchManyAsync(partyID);
				if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count >= MaxPartySize)
				{
					return;
				}

				CharacterPartyData partyData = new CharacterPartyData(0, 1, characterID, partyID, (byte)PartyRank.Member, healthPCT);
				DatabaseResult persistResult = await charPartyService.PersistAsync(partyData, MaxPartySize);
				if (!persistResult.IsSuccess)
				{
					return;
				}

				// Tell the other servers to update their party lists
				await partyUpdateService.PersistAsync(partyID);

				// Marshal state changes + broadcast to main thread
				TryEnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					IPartyController pc = conn.FirstObject?.GetComponent<IPartyController>();
					if (pc == null)
					{
						return;
					}

					pc.ID = partyID;
					pc.Rank = PartyRank.Member;

					if (Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
					{
						runtimeData.RemovePendingInvitation(characterID);
					}

					AddPartyCharacterTracker(partyID, characterID);

					// tell the new member they joined immediately, other clients will catch up with the PartyUpdate pass
					Server.NetworkWrapper.Broadcast(conn, new PartyAddBroadcast()
					{
						PartyID = partyID,
						CharacterID = characterID,
						Rank = PartyRank.Member,
						HealthPCT = healthPCT,
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error accepting party invite (CharID={characterID}, PartyID={partyID}): {ex}");
			}
		}

		/// <summary>
		/// Handles decline of a party invitation, removes pending invitation for the character.
		/// </summary>
		/// <param name="conn">Network connection of the declining character.</param>
		/// <param name="msg">PartyDeclineInviteBroadcast message containing decline details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyDeclineInviteBroadcastReceived(NetworkConnection conn, PartyDeclineInviteBroadcast msg, Channel channel)
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
				if (character != null && Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
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
		/// Handles party leave broadcast, validates character, transfers leadership if needed, removes member from party, and updates or deletes party as appropriate.
		/// </summary>
		/// <param name="conn">Network connection of the leaving character.</param>
		/// <param name="msg">PartyLeaveBroadcast message containing leave details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyLeaveBroadcastReceived(NetworkConnection conn, PartyLeaveBroadcast msg, Channel channel)
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
				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

				// validate character
				if (partyController == null || partyController.ID < 1)
				{
					// not in a party..
					return;
				}

				// Capture immutable data for the async path
				long partyID = partyController.ID;
				long characterID = partyController.Character.ID;
				PartyRank rank = partyController.Rank;

				deferGuardRelease = TryEnqueueIngressWork(() => LeavePartyAsync(conn, characterID, partyID, rank), guardKey, characterID);
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
		/// Asynchronously handles party leave DB operations: fetches members, transfers leadership if needed,
		/// deletes the leaving member, and cleans up or notifies other servers.
		/// </summary>
		/// <param name="conn">Leaving character connection.</param>
		/// <param name="characterID">Leaving character identifier.</param>
		/// <param name="partyID">Party identifier being left.</param>
		/// <param name="rank">Leaving character rank.</param>
		/// <returns>Asynchronous leave-party task.</returns>
		private async Task LeavePartyAsync(NetworkConnection conn, long characterID, long partyID, PartyRank rank)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyService>(out var partyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				// Fetch current members
				DatabaseResult<IReadOnlyList<CharacterPartyData>> membersResult = await charPartyService.FetchManyAsync(partyID);
				if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count == 0)
				{
					return;
				}

				IReadOnlyList<CharacterPartyData> members = membersResult.Data;

				// Count remaining (excluding the leaving character)
				List<CharacterPartyData> remainingMembers = new List<CharacterPartyData>();
				CharacterPartyData leavingMember = default;
				foreach (CharacterPartyData member in members)
				{
					if (member.CharacterID == characterID)
					{
						leavingMember = member;
						continue;
					}
					remainingMembers.Add(member);
				}

				int remainingCount = remainingMembers.Count;

				// Transfer leadership if the leaving character was leader and others remain
				if (rank == PartyRank.Leader && remainingCount > 0)
				{
					System.Random rng = new System.Random();
					CharacterPartyData newLeader = remainingMembers[rng.Next(0, remainingMembers.Count)];
					await charPartyService.UpdateRankAsync(newLeader.CharacterID, partyID, (byte)PartyRank.Leader, newLeader.Version + 1);
				}

				// Delete the leaving member
				await charPartyService.DeleteAsync(characterID, leavingMember.Version + 1);

				if (remainingCount < 1)
				{
					// Delete the party
					await partyService.DeleteAsync(partyID);
					await partyUpdateService.DeleteAsync(partyID);
				}
				else
				{
					// Tell the other servers to update their party lists
					await partyUpdateService.PersistAsync(partyID);
				}

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null)
					{
						return;
					}

					IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
					if (partyController == null || partyController.Character.ID != characterID || partyController.ID != partyID)
					{
						return;
					}

					partyController.ID = 0;
					partyController.Rank = PartyRank.None;
					RemovePartyCharacterTracker(partyID, characterID);

					Server.NetworkWrapper.Broadcast(conn, new PartyLeaveBroadcast(), true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error leaving party (CharID={characterID}, PartyID={partyID}): {ex}");
			}
		}

		/// <summary>
		/// Handles party member removal broadcast, validates and removes a member from the party in the database.
		/// Only party leaders can remove other members.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">PartyRemoveBroadcast message containing member ID to remove.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyRemoveBroadcastReceived(NetworkConnection conn, PartyRemoveBroadcast msg, Channel channel)
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
				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

				// Validate that the requester is a party leader and not trying to remove themselves.
				if (partyController == null ||
					partyController.ID < 1 ||
					partyController.Rank != PartyRank.Leader)
				{
					return;
				}

				if (msg.MemberID < 1)
				{
					return;
				}

				// Prevent party leaders from kicking themselves.
				if (msg.MemberID == partyController.Character.ID)
				{
					return;
				}

				// Capture immutable data for the async path
				long partyID = partyController.ID;
				long memberID = msg.MemberID;
				long characterID = partyController.Character.ID;

				deferGuardRelease = TryEnqueueIngressWork(() => RemovePartyMemberAsync(partyID, memberID, characterID), guardKey, characterID);
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
		/// Asynchronously removes a member from the party, verifying rank permission and notifying other servers.
		/// </summary>
		/// <param name="partyID">Party identifier containing the member.</param>
		/// <param name="memberID">Target member character identifier.</param>
		/// <param name="requesterCharacterID">Requester character identifier.</param>
		/// <returns>Asynchronous remove-member task.</returns>
		private async Task RemovePartyMemberAsync(long partyID, long memberID, long requesterCharacterID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				// Fetch the target member to get their version for the versioned delete
				DatabaseResult<CharacterPartyData?> fetchResult = await charPartyService.FetchAsync(memberID);
				if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
				{
					return;
				}

				CharacterPartyData targetMember = fetchResult.Data.Value;

				// Verify the target is actually in this party
				if (targetMember.PartyID != partyID)
				{
					return;
				}

				DatabaseResult deleteResult = await charPartyService.DeleteAsync(memberID, targetMember.Version + 1);
				if (deleteResult.IsSuccess)
				{
					// Marshal tracker update to main thread
					TryEnqueueMainThread(() =>
					{
						RemovePartyCharacterTracker(partyID, memberID);
					});

					// Tell the other servers to update their party lists.
					await partyUpdateService.PersistAsync(partyID);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error removing party member (PartyID={partyID}, MemberID={memberID}): {ex}");
			}
		}

		/// <summary>
		/// Handles party rank change broadcast, validates leader and target, and updates ranks in the database.
		/// Only party leaders can promote another member to leader.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">PartyChangeRankBroadcast message containing target member ID.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyChangeRankBroadcastReceived(NetworkConnection conn, PartyChangeRankBroadcast msg, Channel channel)
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
				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

				// validate character
				if (partyController == null ||
					partyController.ID < 1 ||
					partyController.Rank != PartyRank.Leader)
				{
					return;
				}

				if (msg.MemberID < 1)
				{
					return;
				}

				// we can't promote ourself
				if (msg.MemberID == partyController.Character.ID)
				{
					return;
				}

				// Capture immutable data for the async path
				long partyID = partyController.ID;
				long leaderCharacterID = partyController.Character.ID;
				long targetMemberID = msg.MemberID;

				deferGuardRelease = TryEnqueueIngressWork(() => ChangePartyRankAsync(partyID, leaderCharacterID, targetMemberID), guardKey, leaderCharacterID);
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
		/// Asynchronously swaps ranks between the current leader and the target member.
		/// </summary>
		/// <param name="partyID">Party identifier containing both members.</param>
		/// <param name="leaderCharacterID">Current leader character identifier.</param>
		/// <param name="targetMemberID">Target member character identifier.</param>
		/// <returns>Asynchronous rank-change task.</returns>
		private async Task ChangePartyRankAsync(long partyID, long leaderCharacterID, long targetMemberID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				// Fetch both members to get their versions
				DatabaseResult<CharacterPartyData?> leaderResult = await charPartyService.FetchAsync(leaderCharacterID);
				if (!leaderResult.IsSuccess || !leaderResult.Data.HasValue)
				{
					return;
				}

				DatabaseResult<CharacterPartyData?> targetResult = await charPartyService.FetchAsync(targetMemberID);
				if (!targetResult.IsSuccess || !targetResult.Data.HasValue)
				{
					return;
				}

				CharacterPartyData leaderData = leaderResult.Data.Value;
				CharacterPartyData targetData = targetResult.Data.Value;

				// Verify both are in the same party
				if (leaderData.PartyID != partyID || targetData.PartyID != partyID)
				{
					return;
				}

				// Promote the target to leader FIRST so the party is never left leaderless.
				DatabaseResult promoteResult = await charPartyService.UpdateRankAsync(targetMemberID, partyID, (byte)PartyRank.Leader, targetData.Version + 1);
				if (!promoteResult.IsSuccess)
				{
					return;
				}

				// Demote the previous leader to member.
				// The party temporarily has two leaders between these calls, which is harmless
				// and strictly better than the zero-leader state the reverse order could create.
				DatabaseResult demoteResult = await charPartyService.UpdateRankAsync(leaderCharacterID, partyID, (byte)PartyRank.Member, leaderData.Version + 1);
				if (!demoteResult.IsSuccess)
				{
					// Rollback: revert the target back to their original rank to avoid two leaders.
					DatabaseResult rollbackResult = await charPartyService.UpdateRankAsync(targetMemberID, partyID, targetData.Rank, targetData.Version + 2);
					if (!rollbackResult.IsSuccess)
					{
						await Log.Error("PartySystem", $"CRITICAL: Promoted target {targetMemberID} but failed to demote old leader {leaderCharacterID} AND failed to rollback in party {partyID}. Party has two leaders until manually corrected.");
					}
					else
					{
						await Log.Warning("PartySystem", $"Promoted target {targetMemberID} but failed to demote old leader {leaderCharacterID} in party {partyID}. Rolled back promotion successfully.");
					}
					return;
				}

				// Tell the other servers to update their party lists
				await partyUpdateService.PersistAsync(partyID);
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error changing party rank (PartyID={partyID}, Leader={leaderCharacterID}, Target={targetMemberID}): {ex}");
			}
		}

		// Uses ServerBehaviour.TryEnqueueAsyncWork

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