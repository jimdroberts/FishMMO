using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
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
		[SerializeField] private bool keepDeleteData = true;

		/// <summary>
		/// If true, keeps deleted character data in the database for recovery or auditing.
		/// </summary>
		public bool KeepDeleteData => keepDeleteData;

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
			SubscribeToConnectionEvents();

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
			UnsubscribeFromConnectionEvents();
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
					try
					{
						conn.Disconnect(true);
					}
					catch (Exception ex)
					{
						Log.Warning("CharacterSelectSystem", $"conn.Disconnect threw: {ex.Message}");
					}
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
					SendServerBusy(conn);
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
				if (!TryGetDbService(out ICharacterService characterService))
				{
					await Log.Warning("CharacterSelectSystem", "CharacterService unavailable for character list request.");
					SendEmptyCharacterList(conn);
					return;
				}

				DatabaseResult<IReadOnlyList<CharacterData>> dbResult = await characterService.FetchManyAsync(accountName);

				if (!dbResult.IsSuccess || dbResult.Data == null)
				{
					await Log.Warning("CharacterSelectSystem", $"Failed to fetch character list for account '{accountName}': [{dbResult.ErrorCode}] {dbResult.ErrorMessage}");
					SendEmptyCharacterList(conn);
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
						IsCombatLogged = data.Flags.IsFlagged(CharacterFlags.IsCombatLogged),
					});
				}

				// Marshal response back to main thread - FishNet Broadcast is not thread-safe
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new CharacterListBroadcast()
						{
							Characters = characterList.ToArray(),
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
				// Account resolved successfully.
				// Process character select below.
				if (!TryBeginInFlightRequest(conn))
				{
					return;
				}

				if (!Authentication.IsAllowedCharacterName(msg.CharacterName))
				{
					EndInFlightRequest(conn);
					SendDeleteFailure(conn);
					return;
				}

				if (!TryEnqueueAsyncWork(() => ProcessCharacterDeleteAsync(conn, accountName, msg.CharacterName), conn.ClientId))
				{
					EndInFlightRequest(conn);
					SendDeleteFailure(conn);
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
				if (!TryGetDbService(out ICharacterService characterService) ||
					!TryGetDbService(out IUnitOfWorkService unitOfWorkService) ||
					!TryGetDbService(out ICharacterAbilityService abilityService) ||
					!TryGetDbService(out ICharacterAchievementService achievementService) ||
					!TryGetDbService(out ICharacterAttributeService attributeService) ||
					!TryGetDbService(out ICharacterBankService bankService) ||
					!TryGetDbService(out ICharacterBuffService buffService) ||
					!TryGetDbService(out ICharacterEquipmentService equipmentService) ||
					!TryGetDbService(out ICharacterFactionService factionService) ||
					!TryGetDbService(out ICharacterFriendService friendService) ||
					!TryGetDbService(out ICharacterHotkeyService hotkeyService) ||
					!TryGetDbService(out ICharacterInventoryService inventoryService) ||
					!TryGetDbService(out ICharacterKnownAbilityService knownAbilityService) ||
					!TryGetDbService(out ICharacterPetService petService))
				{
					await Log.Warning("CharacterSelectSystem", "One or more DB services unavailable for character delete.");
					SendDeleteFailure(conn);
					return;
				}

				// --- Begin Unit of Work for atomic fetch + delete ---

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					await Log.Error("CharacterSelectSystem", $"Failed to begin unit of work for character delete: [{uowResult.ErrorCode}] {uowResult.ErrorMessage}");
					SendDeleteFailure(conn);
					return;
				}

				await using (IUnitOfWork uow = uowResult.Data)
				{
					// Fetch character to get ID and version for the delete call
					DatabaseResult<CharacterData?> fetchResult = await characterService.FetchAsync(characterName);
					if (!fetchResult.IsSuccess || fetchResult.Data == null)
					{
						await Log.Warning("CharacterSelectSystem", $"Failed to fetch character '{characterName}' for deletion: [{fetchResult.ErrorCode}] {fetchResult.ErrorMessage}");
						SendDeleteFailure(conn);
						return;
					}

					CharacterData character = fetchResult.Data.Value;

					// Verify ownership
					if (!string.Equals(character.Account, accountName, StringComparison.OrdinalIgnoreCase))
					{
						await Log.Warning("CharacterSelectSystem", $"Account '{accountName}' attempted to delete character '{characterName}' owned by '{character.Account}'.");
						SendDeleteFailure(conn);
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
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete abilities for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await achievementService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete achievements for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await attributeService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete attributes for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await bankService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete bank for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await buffService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete buffs for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await equipmentService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete equipment for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await factionService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete factions for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await friendService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete friends for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await hotkeyService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete hotkeys for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await inventoryService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete inventory for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await knownAbilityService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete known abilities for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
						r = await petService.DeleteAsync(characterId, deleteVersion);
						if (!r.IsSuccess) await Log.Warning("CharacterSelectSystem", $"Failed to delete pets for character {characterId}: [{r.ErrorCode}] {r.ErrorMessage}");
					}

					// Soft-delete the character row (also hard-deletes guild/party memberships)
					DatabaseResult deleteResult = await characterService.DeleteAsync(characterId, character.Version + 1);
					if (!deleteResult.IsSuccess)
					{
						await Log.Error("CharacterSelectSystem", $"Failed to delete character '{characterName}': [{deleteResult.ErrorCode}] {deleteResult.ErrorMessage}");
						SendDeleteFailure(conn);
						return;
					}

					// Commit the transaction — all sub-entity + character deletes are atomic
					DatabaseResult commitResult = await uow.CommitAsync();
					if (!commitResult.IsSuccess)
					{
						await Log.Error("CharacterSelectSystem", $"Failed to commit character delete: [{commitResult.ErrorCode}] {commitResult.ErrorMessage}");
						SendDeleteFailure(conn);
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
				SendDeleteFailure(conn);
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
			if (!Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}
			if (conn.IsActive)
			{
				if (!TryBeginInFlightRequest(conn))
				{
					return;
				}

				if (!Authentication.IsAllowedCharacterName(msg.CharacterName))
				{
					EndInFlightRequest(conn);
					SendEmptyServerList(conn);
					return;
				}

				if (!TryEnqueueAsyncWork(() => ProcessCharacterSelectAsync(conn, accountName, msg.CharacterName), conn.ClientId))
				{
					EndInFlightRequest(conn);
					SendEmptyServerList(conn);
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
				if (!TryGetDbService(out ICharacterService characterService) ||
				!TryGetDbService(out IWorldServerService worldServerService) ||
				!TryGetDbService(out IUnitOfWorkService unitOfWorkService))
				{
					await Log.Warning("CharacterSelectSystem", "DB services unavailable for character select.");
					SendEmptyServerList(conn);
					return;
				}

				// --- Begin Unit of Work for atomic fetch + select ---

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					await Log.Error("CharacterSelectSystem", $"Failed to begin unit of work for character select: [{uowResult.ErrorCode}] {uowResult.ErrorMessage}");
					SendEmptyServerList(conn);
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

					/* Refuse to switch characters while one is still in the world.
					 *
					 * SetSelectedAsync below rewrites `selected` across the whole account, so
					 * without this an account with a character still held by a scene server —
					 * a live session mid-handover, or a combat-logout body running out its
					 * timer — could point `selected` at a different character and walk a second
					 * one into the world. The scene server would then be holding a claim for a
					 * character the account is no longer considered to be playing, and the
					 * lingering body's own save (which always writes selected = true) would
					 * race the new selection, leaving two rows claiming to be selected.
					 *
					 * Re-selecting the SAME character is allowed: that is exactly the path a
					 * player takes to rejoin the body they left behind. */
					DatabaseResult<CharacterData?> inWorldResult = await characterService.FetchInWorldCharacterAsync(accountName);
					if (inWorldResult.IsSuccess &&
						inWorldResult.Data.HasValue &&
						inWorldResult.Data.Value.ID != character.ID)
					{
						CharacterData inWorld = inWorldResult.Data.Value;
						await Log.Warning("CharacterSelectSystem",
							$"Account '{accountName}' tried to select '{characterName}' while '{inWorld.Name}' is still in the world; refusing.");

						TryEnqueueMainThread(() =>
						{
							if (conn != null && conn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(conn, new CharacterSelectResultBroadcast()
								{
									Result = CharacterSelectResult.OtherCharacterInWorld,
									CharacterName = inWorld.Name,
								}, true, Channel.Reliable);
							}
						});
						return;
					}

					// Set the character as selected
					DatabaseResult setSelectedResult = await characterService.SetSelectedAsync(accountName, character.ID);
					if (!setSelectedResult.IsSuccess)
					{
						await Log.Warning("CharacterSelectSystem", $"SetSelectedAsync failed for account '{accountName}': [{setSelectedResult.ErrorCode}] {setSelectedResult.ErrorMessage}");
						SendEmptyServerList(conn);
						return;
					}

					// Defense-in-depth validation: after selection, confirm this account resolves to the expected selected character.
					// SetSelectedAsync must remain account-scoped at the SQL layer (e.g., WHERE id = @id AND account = @account).
					DatabaseResult<CharacterData?> selectedResult = await characterService.FetchByAccountAsync(accountName, selected: true);
					if (!selectedResult.IsSuccess || selectedResult.Data == null ||
						!string.Equals(selectedResult.Data.Value.Name, characterName, StringComparison.OrdinalIgnoreCase))
					{
						await Log.Warning("CharacterSelectSystem", $"Character select ownership verification failed for account '{accountName}' and character '{characterName}'.");
						SendEmptyServerList(conn);
						return;
					}

					// Commit the transaction
					DatabaseResult commitResult = await uow.CommitAsync();
					if (!commitResult.IsSuccess)
					{
						await Log.Error("CharacterSelectSystem", $"Failed to commit character select: [{commitResult.ErrorCode}] {commitResult.ErrorMessage}");
						SendEmptyServerList(conn);
						return;
					}
				}

				// Fetch active world servers (independent read, outside the UoW)
				DatabaseResult<List<WorldServerData>> worldResult = await worldServerService.FetchActiveAsync();

				if (worldResult.IsSuccess && worldResult.Data != null)
				{
					WorldServerDetails[] worldServerList = new WorldServerDetails[worldResult.Data.Count];
					for (int i = 0; i < worldResult.Data.Count; i++)
					{
						WorldServerData data = worldResult.Data[i];
						worldServerList[i] = new WorldServerDetails()
						{
							Name = data.Name,
							LastPulseUtcTicks = data.LastPulse.Ticks,
							Port = (ushort)data.Port,
							CharacterCount = data.CharacterCount,
							Locked = data.Locked,
						};
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
				else
				{
					await Log.Warning("CharacterSelectSystem", $"Failed to fetch active world servers after character select: [{worldResult.ErrorCode}] {worldResult.ErrorMessage}");
					SendEmptyServerList(conn);
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
			DrainMainThreadQueue<ICharacterSelectSystemMainThreadQueueData>(maxMainThreadResponsesPerFrame, drainAll);
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the RuntimeDataContainer.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<ICharacterSelectSystemMainThreadQueueData>(action);
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
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			if (Server.DataContainerRegistry.TryGet<CharacterSelectSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.TryRemove(conn.ClientId, out _);
				runtimeData.NextAllowedRequestUtc.TryRemove(conn.ClientId, out _);
			}
		}

		/// <summary>
		/// Sends an empty server list to the client when the character select flow fails.
		/// Prevents the client from hanging indefinitely waiting for a response.
		/// </summary>
		/// <param name="conn">Network connection to send the empty list to.</param>
		private void SendEmptyServerList(NetworkConnection conn)
		{
			TryEnqueueMainThread(() =>
			{
				if (conn != null && conn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(conn, new ServerListBroadcast()
					{
						Servers = Array.Empty<WorldServerDetails>(),
					}, true, Channel.Reliable);
				}
			});
		}

		/// <summary>
		/// Sends an empty character list to the client when the fetch operation fails.
		/// Prevents the client from hanging indefinitely waiting for a response.
		/// </summary>
		/// <param name="conn">Network connection to send the empty list to.</param>
		private void SendEmptyCharacterList(NetworkConnection conn)
		{
			TryEnqueueMainThread(() =>
			{
				if (conn != null && conn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(conn, new CharacterListBroadcast()
					{
						Characters = System.Array.Empty<CharacterDetails>(),
					}, true, Channel.Reliable);
				}
			});
		}

		/// <summary>
		/// Sends a delete-failure response to the client (empty character name).
		/// Prevents the client from remaining in an indeterminate state when deletion fails.
		/// </summary>
		/// <param name="conn">Network connection to notify.</param>
		private void SendDeleteFailure(NetworkConnection conn)
		{
			TryEnqueueMainThread(() =>
			{
				if (conn != null && conn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(conn, new CharacterDeleteBroadcast()
					{
						CharacterName = string.Empty,
					}, true, Channel.Reliable);
				}
			});
		}
	}
}