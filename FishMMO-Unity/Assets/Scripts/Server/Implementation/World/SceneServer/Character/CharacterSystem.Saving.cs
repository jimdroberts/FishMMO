using FishNet.Connection;
using FishNet.Object;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Character saving and session management: periodic saves, character data serialization, async persistence, and session lifecycle (claim/release).
	/// </summary>
	public partial class CharacterSystem
	{
		/// <summary>
		/// Periodic callback for saving all characters to the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicSave(float deltaTime)
		{
			if (!Initialized || Server == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			if (data.CharactersByID.Count == 0)
			{
				return;
			}

			Log.Debug("CharacterSystem", "Save" + "[" + DateTime.UtcNow + "]");

			if (!Server.DataContainerRegistry.TryGet<ICharacterSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (!runtimeData.TryBeginSave())
			{
				return;
			}

			// Snapshot character data on the main thread
			var characterDataList = new List<CharacterData>(data.CharactersByID.Count);
			foreach (var character in data.CharactersByID.Values)
			{
				characterDataList.Add(BuildCharacterData(character));
			}

			// Capture session tokens so we can refresh leases even if individual saves fail
			var sessionTokens = new Dictionary<long, CharacterSessionInfo>(data.SessionTokens);

			if (!EnqueueAsyncWork(() => SaveAllCharactersAsync(characterDataList, sessionTokens)))
			{
				runtimeData.EndSave();
				Log.Warning("CharacterSystem", "OnPeriodicSave: Failed to enqueue SaveAllCharactersAsync work item.");
			}
		}

		/// <summary>
		/// Saves the character state, despawns the character from the scene, and fully releases the session.
		/// Always performs a full release (Online → Offline) because the destination
		/// Scene Server's TryClaimAsync requires session_state = Offline (or expired lease).
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">Player character to save and despawn.</param>
		/// <param name="sessionInfo">Session ownership info to release after save, or null if no session is claimed.</param>
		private void SaveAndDespawnCharacter(NetworkConnection conn, IPlayerCharacter character, CharacterSessionInfo? sessionInfo = null)
		{
			// Remove loaded state so when a character is reloaded into a different scene/server, it will properly clamp attributes 
			// and prevent actions until fully loaded in the new scene.
			character.DisableFlags(CharacterFlags.IsLoaded);

			// Snapshot character data on the main thread
			CharacterData charData = BuildCharacterData(character);

			// Save then fully release session (Online → Offline)
			if (!EnqueueAsyncWork(() => SaveAndReleaseCharacterAsync(charData, sessionInfo)))
			{
				Log.Warning("CharacterSystem", $"SaveAndDespawnCharacter: Failed to enqueue save/release for character {charData.ID}.");
			}

			// Immediately log out for now.. we could add a timeout later on..?
			if (character.NetworkObject.IsSpawned)
			{
				OnDespawnCharacter?.Invoke(conn, character);

				ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
			}
		}

		/// <summary>
		/// Builds a CharacterData DTO from an IPlayerCharacter, capturing all fields on the main thread.
		/// Increments character.Version for sequence-based optimistic concurrency.
		/// </summary>
		private CharacterData BuildCharacterData(IPlayerCharacter character)
		{
			character.Version++;
			Vector3 pos = character.Transform.position;
			Quaternion rot = character.Motor != null ? character.Motor.Transform.rotation : character.Transform.rotation;

			return new CharacterData(
				id: character.ID,
				name: character.CharacterName,
				nameLowercase: character.CharacterNameLower,
				account: character.Account,
				selected: true,
				worldServerID: character.WorldServerID,
				sceneName: character.SceneName,
				sceneHandle: character.SceneHandle,
				bindScene: character.BindScene,
				bindX: character.BindPosition.x,
				bindY: character.BindPosition.y,
				bindZ: character.BindPosition.z,
				instanceID: character.InstanceID,
				instanceX: character.InstancePosition.x,
				instanceY: character.InstancePosition.y,
				instanceZ: character.InstancePosition.z,
				instanceRotX: character.InstanceRotation.x,
				instanceRotY: character.InstanceRotation.y,
				instanceRotZ: character.InstanceRotation.z,
				instanceRotW: character.InstanceRotation.w,
				raceID: character.RaceID,
				modelIndex: character.ModelIndex,
				x: pos.x,
				y: pos.y,
				z: pos.z,
				rotX: rot.x,
				rotY: rot.y,
				rotZ: rot.z,
				rotW: rot.w,
				accessLevel: (byte)(int)character.AccessLevel,
				online: true,
				flags: character.Flags,
				version: character.Version,
				timeCreated: character.TimeCreated,
				lastSaved: DateTime.UtcNow
			);
		}

		/// <summary>
		/// Saves a single character asynchronously via the database service.
		/// </summary>
		private async Task SaveCharacterAsync(CharacterData charData)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				DatabaseResult result = await characterService.PersistAsync(charData);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterSystem", $"SaveCharacterAsync DB error for character {charData.ID}: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveCharacterAsync failed for character {charData.ID}: {ex.Message}");
			}
		}

		/// <summary>
		/// Saves a character and then releases the session in a single sequential async task.
		/// This ensures the character data is persisted while we still hold the session lock,
		/// preventing another server from claiming the character before the save completes.
		/// </summary>
		private async Task SaveAndReleaseCharacterAsync(CharacterData charData, CharacterSessionInfo? sessionInfo)
		{
			// Save first — we must persist while still holding the session lock
			await SaveCharacterAsync(charData);

			// Then release the session
			if (sessionInfo.HasValue)
			{
				await ReleaseCharacterSessionAsync(charData.ID, sessionInfo.Value.ServerID, sessionInfo.Value.Token);
			}
		}

		/// <summary>
		/// Saves all characters asynchronously with a processing guard to prevent overlapping saves.
		/// Refreshes session leases for each character to prevent lease expiry even if a save fails.
		/// </summary>
		private async Task SaveAllCharactersAsync(List<CharacterData> characterDataList, Dictionary<long, CharacterSessionInfo> sessionTokens)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				foreach (CharacterData charData in characterDataList)
				{
					try
					{
						DatabaseResult result = await characterService.PersistAsync(charData);
						if (!result.IsSuccess)
						{
							await Log.Warning("CharacterSystem", $"SaveAllCharactersAsync DB error for character {charData.ID}: {result.ErrorCode} - {result.ErrorMessage}");
						}
					}
					catch (Exception ex)
					{
						await Log.Error("CharacterSystem", $"SaveAllCharactersAsync failed for character {charData.ID}: {ex.Message}");
					}

					// Refresh session lease regardless of whether the save succeeded.
					// PersistAsync extends the lease on success, but if it fails we still
					// need to prevent the lease from expiring on a live character.
					if (sessionTokens.TryGetValue(charData.ID, out CharacterSessionInfo si))
					{
						try
						{
							DatabaseResult leaseResult = await characterService.RefreshSessionLeaseAsync(charData.ID, si.ServerID, si.Token);
							if (!leaseResult.IsSuccess)
							{
								await Log.Warning("CharacterSystem", $"SaveAllCharactersAsync: RefreshSessionLeaseAsync DB error for character {charData.ID}: {leaseResult.ErrorCode} - {leaseResult.ErrorMessage}");
							}
						}
						catch (Exception ex)
						{
							await Log.Error("CharacterSystem", $"SaveAllCharactersAsync: RefreshSessionLeaseAsync failed for character {charData.ID}: {ex.Message}");
						}
					}
				}
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<ICharacterSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.EndSave();
				}
			}
		}

		/// <summary>
		/// Removes a character's session token from the mapping data and enqueues an async release.
		/// Returns the extracted CharacterSessionInfo if found, or null if no session was stored.
		/// </summary>
		private CharacterSessionInfo? TryExtractAndReleaseSession(ICharacterMappingData<NetworkConnection> mappingData, long characterID)
		{
			if (mappingData.SessionTokens.TryGetValue(characterID, out CharacterSessionInfo sessionInfo))
			{
				mappingData.SessionTokens.Remove(characterID);
				if (!EnqueueAsyncWork(() => ReleaseCharacterSessionAsync(characterID, sessionInfo.ServerID, sessionInfo.Token)))
				{
					Log.Warning("CharacterSystem", $"Failed to enqueue session release for character {characterID}.");
				}
				return sessionInfo;
			}
			return null;
		}

		/// <summary>
		/// Releases a character session from Online → Offline in a single step.
		/// </summary>
		private async Task ReleaseCharacterSessionAsync(long characterID, long serverID, Guid sessionToken)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: ICharacterService not available for character {characterID}.");
					return;
				}

				if (sessionToken == Guid.Empty || serverID <= 0)
				{
					await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: Invalid session token or server ID for character {characterID}.");
					return;
				}

				DatabaseResult releaseResult = await characterService.ReleaseAsync(characterID, serverID, sessionToken);
				if (!releaseResult.IsSuccess)
				{
					await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: ReleaseAsync failed for character {characterID}: {releaseResult.ErrorCode} - {releaseResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync failed for character {characterID}: {ex.Message}");
			}
		}
	}
}