using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Dungeon finder: validates dungeon entrance interactions and asynchronously assigns or creates dungeon instances.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Handles a dungeon finder request from a dungeon entrance interactable.
		/// </summary>
		/// <param name="conn">Requesting client connection.</param>
		/// <param name="msg">Dungeon finder payload containing interactable id.</param>
		/// <param name="channel">Transport channel used by FishNet.</param>
		public void OnServerDungeonFinderBroadcastReceived(NetworkConnection conn, DungeonFinderBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			// Validate connection character
			if (conn.FirstObject == null)
			{
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}


			// Acquire ingress guard for dungeon finder
			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.DungeonFinder, out long guardKey))
			{
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				// Validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				// Validate Dungeon Entrance
				DungeonEntrance dungeonEntrance = sceneObject.GameObject.GetComponent<DungeonEntrance>();
				if (dungeonEntrance == null ||
					!dungeonEntrance.InRange(character.Transform))
				{
					return;
				}

				// Validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(dungeonEntrance.DungeonName, out WorldSceneDetails details))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + dungeonEntrance.DungeonName);
					return;
				}

				if (details.RespawnPositions == null || details.RespawnPositions.Count < 1)
				{
					Log.Debug("InteractableSystem", $"Missing Scene: {dungeonEntrance.DungeonName} respawn points.");
					return;
				}

				// Capture main-thread state before going async
				long characterID = character.ID;
				long worldServerID = character.WorldServerID;
				long partyID = 0;
				if (character.TryGet(out IPartyController partyController) && partyController.ID != 0)
				{
					partyID = partyController.ID;
				}
				string dungeonName = dungeonEntrance.DungeonName;

				CharacterRespawnPositionDetails respawnDetails = details.RespawnPositions.Values.ToList().GetRandom();

				// Fire-and-forget: process dungeon instance assignment asynchronously.
				// The async task's own finally block will release the guard on completion.
				if (TryEnqueueAsyncWork(() => ProcessDungeonFinderAsync(conn, character, characterID, worldServerID, partyID, dungeonName, respawnDetails, guardKey), characterID))
				{
					asyncOwnsGuard = true;
				}
			}
			finally
			{
				if (!asyncOwnsGuard)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously processes dungeon finder logic: checks for existing instances, party instances, or enqueues a new one.
		/// Marshals character state changes and disconnect back to the main thread.
		/// </summary>
		/// <param name="conn">Owning connection used for disconnecting after assignment.</param>
		/// <param name="character">Character requesting dungeon finder processing.</param>
		/// <param name="characterID">Unique identifier of the requesting character.</param>
		/// <param name="worldServerID">World server identifier where the request originated.</param>
		/// <param name="partyID">Party identifier if the character is grouped; otherwise 0.</param>
		/// <param name="dungeonName">Target dungeon scene name.</param>
		/// <param name="respawnDetails">Respawn position and rotation to apply on entry.</param>
		/// <returns>A task representing asynchronous dungeon finder processing.</returns>
		private async Task ProcessDungeonFinderAsync(
			NetworkConnection conn,
			IPlayerCharacter character,
			long characterID,
			long worldServerID,
			long partyID,
			string dungeonName,
			CharacterRespawnPositionDetails respawnDetails,
			long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}

				// Check the status of the characters instance
				var instanceResult = await sceneService.FetchCharacterInstanceAsync(characterID, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group);
				if (!instanceResult.IsSuccess)
				{
					// No existing instance found
					// Check if any party members currently have an instance
					if (partyID > 0 && await CheckCharacterPartyInstanceAsync(partyID))
					{
						// Notify the connection the party member already has an instance.
						return;
					}

					var enqueueResult = await sceneService.EnqueueAsync(
						worldServerID,
						dungeonName,
						(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
						characterID);

					if (!enqueueResult.IsSuccess)
					{
						await Log.Debug("InteractableSystem", "Failed to enqueue new pending scene load request: " + worldServerID + ":" + dungeonName);
						return;
					}

					long sceneID = enqueueResult.Data;
					TryEnqueueMainThread(() =>
					{
						// Guard against character/connection being destroyed between async DB return and main-thread execution
						if (Server == null || conn == null || !conn.IsActive || character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
						{
							return;
						}

						character.InstanceID = sceneID;
						character.InstancePosition = respawnDetails.Position;
						character.InstanceRotation = respawnDetails.Rotation;
						character.EnableFlags(CharacterFlags.IsInInstance);
						conn.Disconnect(false);
					});
				}
				else
				{
					long existingInstanceID = instanceResult.Data.ID;
					TryEnqueueMainThread(() =>
					{
						// Guard against character/connection being destroyed between async DB return and main-thread execution
						if (Server == null || conn == null || !conn.IsActive || character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
						{
							return;
						}

						character.InstanceID = existingInstanceID;
						character.InstancePosition = respawnDetails.Position;
						character.InstanceRotation = respawnDetails.Rotation;
						character.EnableFlags(CharacterFlags.IsInInstance);
						conn.Disconnect(false);
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error processing dungeon finder: {ex}");
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Checks if a characters party members have a valid instance.
		/// </summary>
		/// <param name="partyID">Party identifier to check for existing instances.</param>
		/// <returns>True if any member has an existing group instance; otherwise false.</returns>
		private async Task<bool> CheckCharacterPartyInstanceAsync(long partyID)
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return false;
			}
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService) ||
				!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				return false;
			}

			var membersResult = await charPartyService.FetchManyAsync(partyID);
			if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count == 0)
			{
				return false;
			}

			foreach (var member in membersResult.Data)
			{
				var instanceResult = await sceneService.FetchCharacterInstanceAsync(member.CharacterID, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group);
				if (instanceResult.IsSuccess)
				{
					return true;
				}
			}

			return false;
		}
	}
}