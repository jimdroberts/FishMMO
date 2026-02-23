using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Manages character selection, deletion, and listing for player accounts on the login server.
	/// Broadcast replies are marshalled back to the main thread via a thread-safe queue drained in OnLateUpdate.
	/// </summary>
	[CreateAssetMenu(fileName = "CharacterSelectSystem", menuName = "FishMMO/Server/LoginServer/Character Select System", order = 1)]
	[RequiresDataContainer(typeof(CharacterSelectSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(CharacterSelectSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class CharacterSelectSystem : ServerBehaviour, ICharacterSelectSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread response actions processed per frame.
		/// This time-slices response dispatch to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max character-select responses drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadResponsesPerFrame = 100;

		/// <summary>
		/// Minimum interval in milliseconds between successive character-select requests from the same connection.
		/// </summary>
		private const int RequestCooldownMilliseconds = 2000;

		/// <summary>
		/// If true, keeps deleted character data in the database for recovery or auditing.
		/// </summary>
		public bool KeepDeleteData = true;

		/// <summary>
		/// Initializes the character select system, registering broadcast handlers for character list, delete, and select requests.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CharacterSelectSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Verify required data containers
			if (!Server.DataContainerRegistry.TryGet<ICharacterSelectSystemMainThreadQueueData>(out _))
			{
				Log.Error("CharacterSelectSystem", "Failed to initialize: ICharacterSelectSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<CharacterSelectSystemRuntimeData>(out _))
			{
				Log.Error("CharacterSelectSystem", "Failed to initialize: CharacterSelectSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<CharacterRequestListBroadcast>(OnServerCharacterRequestListBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CharacterDeleteBroadcast>(OnServerCharacterDeleteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CharacterSelectBroadcast>(OnServerCharacterSelectBroadcastReceived, true);
			ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			maxMainThreadResponsesPerFrame = Mathf.Max(1, maxMainThreadResponsesPerFrame);

			Log.Debug("CharacterSelectSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the character select system, unregistering broadcast handlers for character list, delete, and select requests.
		/// Drains remaining main-thread responses so clients get their final messages.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("CharacterSelectSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain remaining responses so clients get their final messages.
			DrainMainThreadQueue(drainAll: true);
			if (Server.DataContainerRegistry.TryGet<CharacterSelectSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.Clear();
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<CharacterRequestListBroadcast>(OnServerCharacterRequestListBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CharacterDeleteBroadcast>(OnServerCharacterDeleteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CharacterSelectBroadcast>(OnServerCharacterSelectBroadcastReceived);
			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
			}
		}

		/// <summary>
		/// Handles broadcast to request the list of available characters for the account, queries the database and sends the list to the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CharacterRequestListBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCharacterRequestListBroadcastReceived(NetworkConnection conn, CharacterRequestListBroadcast msg, Channel channel)
		{
			if (!Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				// character is requesting character list before authentication completes, disconnect them...
				conn.Disconnect(true);
			}
			else if (conn.IsActive)
			{
				if (!TryBeginInFlightRequest(conn))
				{
					return;
				}

				if (!TryEnqueueAsyncWork(() => ProcessCharacterListRequestAsync(conn, accountName)))
				{
					EndInFlightRequest(conn);
					Log.Warning("CharacterSelectSystem", $"Failed to enqueue character list request for account '{accountName}'.");
				}
			}
		}

		/// <summary>
		/// Asynchronously fetches the character list from the database and sends it to the client.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="accountName">Account name to fetch characters for.</param>
		private async Task ProcessCharacterListRequestAsync(NetworkConnection conn, string accountName)
		{
			try
			{
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					await Log.Warning("CharacterSelectSystem", "CharacterService unavailable for character list request.");
					return;
				}

				DatabaseResult<IReadOnlyList<CharacterData>> dbResult = await characterService.FetchManyAsync(accountName);

				if (!dbResult.IsSuccess || dbResult.Data == null)
				{
					await Log.Warning("CharacterSelectSystem", $"Failed to fetch character list for account '{accountName}': {dbResult.ErrorMessage}");
					return;
				}

				// Map database DTOs to network broadcast type
				var characterList = new List<CharacterDetails>(dbResult.Data.Count);
				foreach (CharacterData data in dbResult.Data)
				{
					characterList.Add(new CharacterDetails()
					{
						CharacterName = data.Name,
						SceneName = data.SceneName,
						RaceTemplateID = data.RaceID,
					});
				}

				// Marshal response back to main thread - FishNet Broadcast is not thread-safe
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new CharacterListBroadcast()
						{
							Characters = characterList,
						}, true, Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSelectSystem", $"Error processing character list request: {ex}");
			}
			finally
			{
				EndInFlightRequest(conn);
			}
		}

		/// <summary>
		/// Handles broadcast to delete a character for the account, updates the database and notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CharacterDeleteBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCharacterDeleteBroadcastReceived(NetworkConnection conn, CharacterDeleteBroadcast msg, Channel channel)
		{
			if (conn.IsActive && Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				if (!TryBeginInFlightRequest(conn))
				{
					return;
				}

				if (!Authentication.IsAllowedCharacterName(msg.CharacterName))
				{
					EndInFlightRequest(conn);
					return;
				}

				if (!TryEnqueueAsyncWork(() => ProcessCharacterDeleteAsync(conn, accountName, msg.CharacterName), conn.ClientId))
				{
					EndInFlightRequest(conn);
					Log.Warning("CharacterSelectSystem", $"Failed to enqueue character delete request for account '{accountName}'.");
				}
			}
		}

		/// <summary>
		/// Asynchronously deletes a character and all its sub-entity data from the database within
		/// a Unit of Work for atomicity, then notifies the client.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="accountName">Account that owns the character.</param>
		/// <param name="characterName">Name of the character to delete.</param>
		private async Task ProcessCharacterDeleteAsync(NetworkConnection conn, string accountName, string characterName)
		{
			try
			{
				var registry = Server.Database?.ServiceRegistry;
				if (registry == null ||
					!registry.TryGet<ICharacterService>(out var characterService) ||
					!registry.TryGet<IUnitOfWorkService>(out var unitOfWorkService) ||
					!registry.TryGet<ICharacterAbilityService>(out var abilityService) ||
					!registry.TryGet<ICharacterAchievementService>(out var achievementService) ||
					!registry.TryGet<ICharacterAttributeService>(out var attributeService) ||
					!registry.TryGet<ICharacterBankService>(out var bankService) ||
					!registry.TryGet<ICharacterBuffService>(out var buffService) ||
					!registry.TryGet<ICharacterEquipmentService>(out var equipmentService) ||
					!registry.TryGet<ICharacterFactionService>(out var factionService) ||
					!registry.TryGet<ICharacterFriendService>(out var friendService) ||
					!registry.TryGet<ICharacterHotkeyService>(out var hotkeyService) ||
					!registry.TryGet<ICharacterInventoryService>(out var inventoryService) ||
					!registry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService) ||
					!registry.TryGet<ICharacterPetService>(out var petService))
				{
					await Log.Warning("CharacterSelectSystem", "One or more DB services unavailable for character delete.");
					return;
				}

				// --- Begin Unit of Work for atomic fetch + delete ---

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					await Log.Error("CharacterSelectSystem", $"Failed to begin unit of work for character delete: {uowResult.ErrorMessage}");
					return;
				}

				await using (IUnitOfWork uow = uowResult.Data)
				{
					// Fetch character to get ID and version for the delete call
					DatabaseResult<CharacterData?> fetchResult = await characterService.FetchAsync(characterName);
					if (!fetchResult.IsSuccess || fetchResult.Data == null)
					{
						await Log.Warning("CharacterSelectSystem", $"Failed to fetch character '{characterName}' for deletion: {fetchResult.ErrorMessage}");
						return;
					}

					CharacterData character = fetchResult.Data.Value;

					// Verify ownership
					if (!string.Equals(character.Account, accountName, StringComparison.OrdinalIgnoreCase))
					{
						await Log.Warning("CharacterSelectSystem", $"Account '{accountName}' attempted to delete character '{characterName}' owned by '{character.Account}'.");
						return;
					}

					long characterId = character.ID;

					if (!KeepDeleteData)
					{
						// Use long.MaxValue to unconditionally pass the version guard on sub-entity deletes.
						// Sub-entity version streams are independent of the character version,
						// so we must guarantee all sub-entity rows are cleaned up regardless of their version.
						long deleteVersion = long.MaxValue;

						// Delete all sub-entity data before deleting the character row.
						// CharacterService.DeleteAsync already hard-deletes guild and party memberships,
						// so those are excluded here.
						// Log but do not abort on sub-entity delete failures — the character row
						// soft-delete is the critical operation; orphaned sub-entity rows are harmless.
						DatabaseResult r;
						r = await abilityService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete abilities for character {characterId}: {r.ErrorMessage}");
						r = await achievementService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete achievements for character {characterId}: {r.ErrorMessage}");
						r = await attributeService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete attributes for character {characterId}: {r.ErrorMessage}");
						r = await bankService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete bank for character {characterId}: {r.ErrorMessage}");
						r = await buffService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete buffs for character {characterId}: {r.ErrorMessage}");
						r = await equipmentService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete equipment for character {characterId}: {r.ErrorMessage}");
						r = await factionService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete factions for character {characterId}: {r.ErrorMessage}");
						r = await friendService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete friends for character {characterId}: {r.ErrorMessage}");
						r = await hotkeyService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete hotkeys for character {characterId}: {r.ErrorMessage}");
						r = await inventoryService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete inventory for character {characterId}: {r.ErrorMessage}");
						r = await knownAbilityService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete known abilities for character {characterId}: {r.ErrorMessage}");
						r = await petService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete pets for character {characterId}: {r.ErrorMessage}");
					}

					// Soft-delete the character row (also hard-deletes guild/party memberships)
					DatabaseResult deleteResult = await characterService.DeleteAsync(characterId, character.Version + 1);
					if (!deleteResult.IsSuccess)
					{
						await Log.Error("CharacterSelectSystem", $"Failed to delete character '{characterName}': {deleteResult.ErrorMessage}");
						return;
					}

					// Commit the transaction — all sub-entity + character deletes are atomic
					DatabaseResult commitResult = await uow.CommitAsync();
					if (!commitResult.IsSuccess)
					{
						await Log.Error("CharacterSelectSystem", $"Failed to commit character delete: {commitResult.ErrorMessage}");
						return;
					}
				}

				// Marshal response back to main thread - FishNet Broadcast is not thread-safe
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new CharacterDeleteBroadcast()
						{
							CharacterName = characterName,
						}, true, Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSelectSystem", $"Error processing character delete: {ex}");
			}
			finally
			{
				EndInFlightRequest(conn);
			}
		}

		/// <summary>
		/// Handles broadcast to select a character for the account, updates the database and sends the world server list to the client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CharacterSelectBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCharacterSelectBroadcastReceived(NetworkConnection conn, CharacterSelectBroadcast msg, Channel channel)
		{
			if (conn.IsActive && Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				if (!TryBeginInFlightRequest(conn))
				{
					return;
				}

				if (!Authentication.IsAllowedCharacterName(msg.CharacterName))
				{
					EndInFlightRequest(conn);
					return;
				}

				if (!TryEnqueueAsyncWork(() => ProcessCharacterSelectAsync(conn, accountName, msg.CharacterName), conn.ClientId))
				{
					EndInFlightRequest(conn);
					Log.Warning("CharacterSelectSystem", $"Failed to enqueue character select request for account '{accountName}'.");
				}
			}
		}

		/// <summary>
		/// Asynchronously validates character selection within a Unit of Work for consistency,
		/// updates the database, and sends the world server list.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="accountName">Account that owns the character.</param>
		/// <param name="characterName">Name of the character to select.</param>
		private async Task ProcessCharacterSelectAsync(NetworkConnection conn, string accountName, string characterName)
		{
			try
			{
				var registry = Server.Database?.ServiceRegistry;
				if (registry == null ||
					!registry.TryGet<ICharacterService>(out var characterService) ||
					!registry.TryGet<IWorldServerService>(out var worldServerService) ||
					!registry.TryGet<IUnitOfWorkService>(out var unitOfWorkService))
				{
					return;
				}

				// --- Begin Unit of Work for atomic fetch + select ---

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					await Log.Error("CharacterSelectSystem", $"Failed to begin unit of work for character select: {uowResult.ErrorMessage}");
					return;
				}

				await using (IUnitOfWork uow = uowResult.Data)
				{
					// Verify character exists and belongs to this account
					DatabaseResult<CharacterData?> fetchResult = await characterService.FetchAsync(characterName);
					if (!fetchResult.IsSuccess || fetchResult.Data == null)
					{
						TryEnqueueMainThread(() =>
						{
							if (conn != null && conn.IsActive)
							{
								conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							}
						});
						return;
					}

					CharacterData character = fetchResult.Data.Value;
					if (!string.Equals(character.Account, accountName, StringComparison.OrdinalIgnoreCase))
					{
						TryEnqueueMainThread(() =>
						{
							if (conn != null && conn.IsActive)
							{
								conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
							}
						});
						return;
					}

					// Set the character as selected
					DatabaseResult setSelectedResult = await characterService.SetSelectedAsync(accountName, character.ID);
					if (!setSelectedResult.IsSuccess)
					{
						return;
					}

					// Defense-in-depth validation: after selection, confirm this account resolves to the expected selected character.
					// SetSelectedAsync must remain account-scoped at the SQL layer (e.g., WHERE id = @id AND account = @account).
					DatabaseResult<CharacterData?> selectedResult = await characterService.FetchByAccountAsync(accountName, selected: true);
					if (!selectedResult.IsSuccess || selectedResult.Data == null ||
						!string.Equals(selectedResult.Data.Value.Name, characterName, StringComparison.OrdinalIgnoreCase))
					{
						await Log.Warning("CharacterSelectSystem", $"Character select ownership verification failed for account '{accountName}' and character '{characterName}'.");
						return;
					}

					// Commit the transaction
					DatabaseResult commitResult = await uow.CommitAsync();
					if (!commitResult.IsSuccess)
					{
						await Log.Error("CharacterSelectSystem", $"Failed to commit character select: {commitResult.ErrorMessage}");
						return;
					}
				}

				// Fetch active world servers (independent read, outside the UoW)
				DatabaseResult<List<WorldServerData>> worldResult = await worldServerService.FetchActiveAsync();

				if (worldResult.IsSuccess && worldResult.Data != null)
				{
					var worldServerList = new List<WorldServerDetails>(worldResult.Data.Count);
					foreach (WorldServerData data in worldResult.Data)
					{
						worldServerList.Add(new WorldServerDetails()
						{
							Name = data.Name,
							LastPulse = data.LastPulse,
							Address = data.Address,
							Port = data.Port,
							CharacterCount = data.CharacterCount,
							Locked = data.Locked,
						});
					}

					// Marshal response back to main thread - FishNet Broadcast is not thread-safe
					TryEnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Server.NetworkWrapper.Broadcast(conn, new ServerListBroadcast()
							{
								Servers = worldServerList,
							}, true, Channel.Reliable);
						}
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSelectSystem", $"Error processing character select: {ex}");
			}
			finally
			{
				EndInFlightRequest(conn);
			}
		}

		/// <summary>
		/// Drains the main-thread response queue each frame.
		/// All network operations from async workers are marshalled through this queue
		/// to ensure they execute on the main Unity thread.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Drains the main-thread queue via the RuntimeDataContainer.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server?.DataContainerRegistry.TryGet<ICharacterSelectSystemMainThreadQueueData>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadResponsesPerFrame);
				}
			}
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the RuntimeDataContainer.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<ICharacterSelectSystemMainThreadQueueData>(out var queueData) == true)
			{
				return queueData.TryEnqueue(action);
			}
			return false;
		}

		/// <summary>
		/// Attempts to acquire a per-connection in-flight request slot and enforces a cooldown between requests.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <returns><c>true</c> if the slot was acquired; otherwise <c>false</c>.</returns>
		private bool TryBeginInFlightRequest(NetworkConnection conn)
		{
			if (conn == null)
			{
				return false;
			}

			if (!Server.DataContainerRegistry.TryGet<CharacterSelectSystemRuntimeData>(out var runtimeData))
			{
				return false;
			}

			// Enforce per-connection cooldown
			DateTime nowUtc = DateTime.UtcNow;
			if (runtimeData.NextAllowedRequestUtc.TryGetValue(conn.ClientId, out DateTime nextAllowed) &&
				nowUtc < nextAllowed)
			{
				return false;
			}

			return runtimeData.InFlightRequests.TryAdd(conn.ClientId, 0);
		}

		/// <summary>
		/// Releases the per-connection in-flight request slot and records the cooldown timestamp.
		/// </summary>
		/// <param name="conn">Connection to release.</param>
		private void EndInFlightRequest(NetworkConnection conn)
		{
			if (conn != null &&
				Server.DataContainerRegistry.TryGet<CharacterSelectSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.TryRemove(conn.ClientId, out _);
				runtimeData.NextAllowedRequestUtc[conn.ClientId] = DateTime.UtcNow.AddMilliseconds(RequestCooldownMilliseconds);
			}
		}

		/// <summary>
		/// Releases per-connection in-flight request state when a client disconnects.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped &&
				conn != null &&
				Server.DataContainerRegistry.TryGet<CharacterSelectSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.TryRemove(conn.ClientId, out _);
				runtimeData.NextAllowedRequestUtc.TryRemove(conn.ClientId, out _);
			}
		}
	}
}