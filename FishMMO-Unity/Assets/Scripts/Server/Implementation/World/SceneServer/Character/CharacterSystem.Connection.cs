using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Character connection lifecycle: disconnect handling, connection-mapping cleanup, teleportation, out-of-bounds checks, and death/respawn logic.
	/// </summary>
	public partial class CharacterSystem
	{
		/// <summary>
		/// When a connection disconnects the server removes all known instances of the character and saves it to the database.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped &&
				Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				RemoveCharacterConnectionMapping(conn);
			}
		}

		/// <summary>
		/// Removes the character connection mapping and saves the character state to the database.
		/// Always performs a full session release (Online → Offline) because
		/// the destination Scene Server's TryClaimAsync requires session_state = Offline (or expired lease).
		/// </summary>
		/// <param name="conn">Network connection to remove.</param>
		/// <param name="skipOnDisconnect">If true, skips OnDisconnect event invocation.</param>
		private void RemoveCharacterConnectionMapping(NetworkConnection conn, bool skipOnDisconnect = false)
		{
			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			// Remove the waiting scene load character if it exists, these characters exist but are not spawned
			if (data.WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter waitingSceneCharacter))
			{
				data.WaitingSceneLoadCharacters.Remove(conn);

				TryExtractAndReleaseSession(data, waitingSceneCharacter.ID);

				OnDisconnect?.Invoke(conn, waitingSceneCharacter);

				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(waitingSceneCharacter.NetworkObject, true);
			}

			if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				return;
			}

			// Remove the connection->character entry
			data.ConnectionCharacters.Remove(conn);

			// Remove the characterID->character entry
			data.CharactersByID.Remove(character.ID);
			// Remove the characterName->character entry
			data.CharactersByLowerCaseName.Remove(character.CharacterNameLower);
			// Remove the worldid<characterID->character> entry
			if (data.CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> characters))
			{
				characters.Remove(character.ID);
			}

			if (!skipOnDisconnect)
			{
				OnDisconnect?.Invoke(conn, character);
			}

			// Extract session info so SaveAndDespawnCharacter can release AFTER the save completes
			CharacterSessionInfo? sessionInfo = null;
			if (data.SessionTokens.TryGetValue(character.ID, out CharacterSessionInfo si))
			{
				data.SessionTokens.Remove(character.ID);
				sessionInfo = si;
				// Note: NOT released here — SaveAndReleaseCharacterAsync handles it after the save
			}

			SaveAndDespawnCharacter(conn, character, sessionInfo);
		}

		/// <summary>
		/// Handles character teleport events, validates teleporter and scene, updates position, and saves state.
		/// </summary>
		/// <param name="character">Player character to teleport.</param>
		public void IPlayerCharacter_OnTeleport(IPlayerCharacter character)
		{
			if (character == null)
			{
				Log.Debug("CharacterSystem", "Character doesn't exist..");
				return;
			}

			if (!character.IsTeleporting)
			{
				Log.Debug("CharacterSystem", "Character is not teleporting..");
				return;
			}

			// Should we prevent players from moving to a different scene if they are in combat?
			/*if (character.TryGet(out CharacterDamageController damageController) &&
				  damageController.Attackers.Count > 0)
			{
				return;
			}*/

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				Log.Debug("CharacterSystem", "SceneServerSystem not found!");
				return;
			}

			// Cache the current scene name
			string currentScene = character.SceneName;

			if (sceneServerSystem.WorldSceneDetailsCache == null ||
				!sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(currentScene, out WorldSceneDetails details))
			{
				Log.Debug("CharacterSystem", currentScene + " not found!");
				return;
			}

			// Check if teleporter is a valid scene teleporter
			if (details.Teleporters.TryGetValue(character.TeleporterName, out SceneTeleporterDetails teleporter))
			{
				//Log.Debug("CharacterSystem", $"Teleporter: {character.TeleporterName} found! Teleporting {character.CharacterName} to {teleporter.ToScene}.");

				//Log.Debug("CharacterSystem", $"Unloading scene for {character.CharacterName}: {character.SceneName}|{character.SceneHandle}");

				// Tell the connection to unload their current world scene.
				sceneServerSystem.UnloadSceneForConnection(character.Owner, character.SceneName);

				// Character becomes immortal when teleporting
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					//Log.Debug("CharacterSystem", $"{character.CharacterName} is now immortal.");
					damageController.Immortal = true;
				}

				// Invoke disconnect early when teleporting because we require the scene the character is in.
				OnDisconnect?.Invoke(character.Owner, character);

				character.SceneName = teleporter.ToScene;
				character.Motor.SetPositionAndRotationAndVelocity(teleporter.ToPosition, teleporter.ToRotation, Vector3.zero);

				// Remove the character from an instance if it was in one.
				character.DisableFlags(CharacterFlags.IsInInstance);

				// Save the character and fully release the session so the destination scene server can claim it
				RemoveCharacterConnectionMapping(character.Owner, skipOnDisconnect: true);
			}
			else
			{
				Log.Debug("CharacterSystem", $"{character.TeleporterName} not found!");
			}
		}

		/// <summary>
		/// Periodic callback for out-of-bounds character checks.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicOutOfBoundsCheck(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			if (sceneServerSystem.WorldSceneDetailsCache == null || data.ConnectionCharacters == null)
			{
				return;
			}

			// TODO: Should the character be doing this and more often?
			// They'd need a cached world boundaries to check themselves against
			// which would prevent the need to do all of this lookup stuff.
			foreach (IPlayerCharacter character in data.ConnectionCharacters.Values)
			{
				if (character == null || string.IsNullOrWhiteSpace(character.SceneName))
				{
					continue;
				}

				var sceneName = string.IsNullOrWhiteSpace(character.InstanceSceneName)
							? character.SceneName
							: character.InstanceSceneName;

				if (sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details))
				{
					// Check if they are within some bounds, if not we need to move them to a respawn location!
					// TODO: Try to prevent combat escape, maybe this needs to be handled on the game design level?
					if (!details.Boundaries.PointContainedInBoundaries(character.Transform.position))
					{
						if (details.RespawnPositions.Count < 1)
						{
							continue;
						}

						CharacterRespawnPositionDetails spawnPoint = details.RespawnPositions.Values.ToList().GetRandom();
						if (spawnPoint == null ||
							character == null ||
							character.Motor == null)
						{
							continue;
						}

						Log.Debug("CharacterSystem", $"{character.CharacterName} is out of bounds.");

						character.Motor.SetPositionAndRotationAndVelocity(spawnPoint.Position, spawnPoint.Rotation, Vector3.zero);
					}
				}
			}
		}

		/// <summary>
		/// Handles character killed events, processes player and NPC deaths, respawns, and updates state.
		/// </summary>
		/// <param name="killer">Character that performed the kill.</param>
		/// <param name="defender">Character that was killed.</param>
		private void CharacterDamageController_OnKilled(ICharacter killer, ICharacter defender)
		{
			if (defender == null)
			{
				return;
			}

			if (defender.TryGet(out IBuffController buffController))
			{
				buffController.RemoveAll(true);
			}

			// Handle Player deaths
			IPlayerCharacter playerCharacter = defender as IPlayerCharacter;
			if (playerCharacter != null)
			{
				//Log.Debug("CharacterSystem", $"PlayerCharacter: {playerCharacter.GameObject.name} Died");

				if (playerCharacter.TryGet(out ICharacterDamageController damageController))
				{
					// Full heal the character
					damageController.Heal(null, 999999, true);
				}

				if ((playerCharacter.IsInInstance() && playerCharacter.InstanceSceneName != playerCharacter.BindScene) ||
					(!playerCharacter.IsInInstance() && playerCharacter.SceneName != playerCharacter.BindScene))
				{
					// Update scene and position to bind point before saving
					playerCharacter.SceneName = playerCharacter.BindScene;
					playerCharacter.Motor.SetPositionAndRotationAndVelocity(playerCharacter.BindPosition, playerCharacter.Motor.Transform.rotation, Vector3.zero);

					// Remove instance flag before disconnect so the save captures the correct state
					playerCharacter.DisableFlags(CharacterFlags.IsInInstance);

					// Disconnect to world server — reconnects to bind scene via World Server
					playerCharacter.NetworkObject.Owner.Disconnect(false);
				}
				else
				{
					playerCharacter.Motor.SetPositionAndRotationAndVelocity(playerCharacter.BindPosition, Quaternion.identity, Vector3.zero);
				}
			}
			else
			{
				// Handle NPC deaths
				NPC npc = defender as NPC;
				if (npc != null)
				{
					Pet pet = defender as Pet;
					if (pet != null)
					{
						//Log.Debug("CharacterSystem", $"Pet: {pet.GameObject.name} Died");

						IPlayerCharacter petOwner = pet.PetOwner as IPlayerCharacter;
						if (petOwner != null)
						{
							OnPetKilled?.Invoke(petOwner.NetworkObject.Owner, petOwner);
						}
					}
					else
					{
						//Log.Debug("CharacterSystem", $"NPC: {npc.GameObject.name} Died");
						npc.Despawn();
					}
				}
			}
		}
	}
}