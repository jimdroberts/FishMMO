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
		/// Registered chat commands for party actions.
		/// </summary>
		private Dictionary<string, ChatCommand> partyChatCommands;

		/// <summary>
		/// Handles party invite chat commands.
		/// </summary>
		public bool OnPartyInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			string targetName = msg.Text.Trim().ToLower();
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
			partyChatCommands = new Dictionary<string, ChatCommand>()
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
			DrainMainThreadQueue();

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
		private void DrainMainThreadQueue()
		{
			if (Server?.DataContainerRegistry.TryGet<IPartySystemMainThreadQueueData>(out var queueData) == true)
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
			if (Server?.DataContainerRegistry.TryGet<IPartySystemMainThreadQueueData>(out var queueData) == true)
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
		/// Periodic callback that fetches and processes party updates from the database asynchronously.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (Initialized && Server.ServerState == ConnectionState.Started)
			{
				EnqueueAsyncWork(() => FetchAndProcessPartyUpdatesAsync());
			}
		}

		/// <summary>
		/// Asynchronously fetches party updates from the database and marshals the processing back to the main thread.
		/// </summary>
		private async Task FetchAndProcessPartyUpdatesAsync()
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

				// Read containers on main thread before awaiting (periodic callback runs on main thread)
				if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData))
				{
					return;
				}
				if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
				{
					return;
				}

				List<long> partyIds = mappingData.PartyCharacterTracker.Keys.ToList();
				DateTime lastFetch = runtimeData.LastFetchTime;

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
				EnqueueMainThread(() =>
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

			EnqueueAsyncWork(() => PersistPartyMemberAndNotifyAsync(characterID, partyID, rank, healthPCT));
		}

		/// <summary>
		/// Handles character disconnect event, removing the character from the party tracker and saving party update.
		/// </summary>
		public void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character != null && Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
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

			if (!character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				// not in a Party
				return;
			}

			RemovePartyCharacterTracker(partyController.ID, character.ID);

			// Fire-and-forget async DB persist
			long partyID = partyController.ID;
			EnqueueAsyncWork(() => PersistPartyUpdateAsync(partyID));
		}

		/// <summary>
		/// Asynchronously persists a party member's data and triggers a party update notification.
		/// </summary>
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
			if (conn.FirstObject == null)
			{
				return;
			}
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

			EnqueueAsyncWork(() => CreatePartyAsync(conn, characterID, sceneName, healthPCT));
		}

		/// <summary>
		/// Asynchronously creates a new party, persists membership, and marshals state changes back to the main thread.
		/// </summary>
		private async Task CreatePartyAsync(NetworkConnection conn, long characterID, string sceneName, float healthPCT)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<IPartyService>(out var partyService))
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
				EnqueueMainThread(() =>
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
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (conn.FirstObject == null)
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

			EnqueueAsyncWork(() => ValidateAndSendPartyInviteAsync(conn, inviterPartyID, inviterCharacterID, targetCharacterID));
		}

		/// <summary>
		/// Asynchronously validates party capacity and marshals the invitation back to the main thread.
		/// </summary>
		private async Task ValidateAndSendPartyInviteAsync(NetworkConnection conn, long inviterPartyID, long inviterCharacterID, long targetCharacterID)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
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
				EnqueueMainThread(() =>
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
					if (!runtimeData.PendingInvitations.ContainsKey(targetCharacterID) &&
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
							return;
						}

						// add to our list of pending invitations... used for validation when accepting/declining a party invite
						runtimeData.PendingInvitations.Add(targetCharacter.ID, inviterCharacterID);
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
			if (conn.FirstObject == null)
			{
				return;
			}
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
			if (runtimeData.PendingInvitations.TryGetValue(partyController.Character.ID, out long pendingPartyID))
			{
				runtimeData.PendingInvitations.Remove(partyController.Character.ID);

				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				// Capture immutable data for the async path
				long characterID = partyController.Character.ID;
				bool attributesExist = partyController.Character.TryGet(out ICharacterAttributeController attributeController);
				float healthPCT = attributesExist ? attributeController.GetHealthResourceAttributeCurrentPercentage() : 1.0f;

				EnqueueAsyncWork(() => AcceptPartyInviteAsync(conn, characterID, pendingPartyID, healthPCT));
			}
		}

		/// <summary>
		/// Asynchronously validates party capacity, persists membership, and marshals state changes back to the main thread.
		/// </summary>
		private async Task AcceptPartyInviteAsync(NetworkConnection conn, long characterID, long partyID, float healthPCT)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
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
				EnqueueMainThread(() =>
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
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character != null && Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
			{
				runtimeData.PendingInvitations.Remove(character.ID);
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
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (conn.FirstObject == null)
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

			// Immediate main-thread state update
			partyController.ID = 0;
			partyController.Rank = PartyRank.None;
			RemovePartyCharacterTracker(partyID, characterID);

			// Tell character that they left the party immediately
			Server.NetworkWrapper.Broadcast(conn, new PartyLeaveBroadcast(), true, Channel.Reliable);

			// Fire-and-forget async DB cleanup
			EnqueueAsyncWork(() => LeavePartyAsync(characterID, partyID, rank));
		}

		/// <summary>
		/// Asynchronously handles party leave DB operations: fetches members, transfers leadership if needed,
		/// deletes the leaving member, and cleans up or notifies other servers.
		/// </summary>
		private async Task LeavePartyAsync(long characterID, long partyID, PartyRank rank)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
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
					CharacterPartyData newLeader = remainingMembers[UnityEngine.Random.Range(0, remainingMembers.Count)];
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
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (conn.FirstObject == null)
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

			EnqueueAsyncWork(() => RemovePartyMemberAsync(partyID, memberID, characterID));
		}

		/// <summary>
		/// Asynchronously removes a member from the party, verifying rank permission and notifying other servers.
		/// </summary>
		private async Task RemovePartyMemberAsync(long partyID, long memberID, long requesterCharacterID)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
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
					EnqueueMainThread(() =>
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
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (conn.FirstObject == null)
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

			EnqueueAsyncWork(() => ChangePartyRankAsync(partyID, leaderCharacterID, targetMemberID));
		}

		/// <summary>
		/// Asynchronously swaps ranks between the current leader and the target member.
		/// </summary>
		private async Task ChangePartyRankAsync(long partyID, long leaderCharacterID, long targetMemberID)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
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

				// Demote the current leader to member
				DatabaseResult demoteResult = await charPartyService.UpdateRankAsync(leaderCharacterID, partyID, (byte)PartyRank.Member, leaderData.Version + 1);
				if (!demoteResult.IsSuccess)
				{
					return;
				}

				// Promote the target to leader
				DatabaseResult promoteResult = await charPartyService.UpdateRankAsync(targetMemberID, partyID, (byte)PartyRank.Leader, targetData.Version + 1);
				if (promoteResult.IsSuccess)
				{
					// Tell the other servers to update their party lists
					await partyUpdateService.PersistAsync(partyID);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error changing party rank (PartyID={partyID}, Leader={leaderCharacterID}, Target={targetMemberID}): {ex}");
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