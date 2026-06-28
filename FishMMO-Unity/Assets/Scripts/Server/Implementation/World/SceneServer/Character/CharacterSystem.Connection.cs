using FishNet.Connection;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
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
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			// Clean up per-connection scene-unload rate-limit tracking.
			sceneUnloadLastTimeByClientId.TryRemove(conn.ClientId, out _);

			// Clean up per-connection validated-scene rate-limit tracking.
			validatedSceneLastTimeByClientId.TryRemove(conn.ClientId, out _);

			// Clean up per-account auth callback rate-limit tracking.
			if (Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				authCallbackLastTimeByAccount.TryRemove(accountName, out _);
			}

			if (Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
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

			// Prevent players from teleporting while in combat.
			// This closes the combat-escape exploit where a player could instantly
			// teleport to a different scene to avoid death or PvP.
			if (character.IsFlagged(CharacterFlags.IsInCombat))
			{
				return;
			}

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
				character.DisableFlags(CharacterFlags.IsLoaded);

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
				buffController.RemoveAll(false);
			}

			// Handle Player deaths
			IPlayerCharacter playerCharacter = defender as IPlayerCharacter;
			if (playerCharacter != null)
			{
				//Log.Debug("CharacterSystem", $"PlayerCharacter: {playerCharacter.GameObject.name} Died");

				// Mark the player as dead and show the death dialog.
				// Do NOT revive or teleport here - the player chooses Respawn or Resurrect.
				playerCharacter.EnableFlags(CharacterFlags.IsDead);

				// Send the death broadcast to the owning client so the death dialog appears.
				Server.NetworkWrapper.Broadcast(playerCharacter.Owner,
					new DeathBroadcast(), true, FishNet.Transporting.Channel.Reliable);
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
							pet.Despawn();
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

	/// <summary>
	/// Handles a dead player requesting respawn at their bind point.
	/// Revives the character at full health and teleports to bind.
	/// Rate-limited per-connection to prevent spam.
	/// </summary>
	/// <param name="conn">The network connection of the dead player.</param>
	/// <param name="msg">The respawn-at-bind-point broadcast message.</param>
	/// <param name="channel">The channel on which the broadcast was received.</param>
	private void OnClientRespawnAtBindPointBroadcastReceived(NetworkConnection conn, RespawnAtBindPointBroadcast msg, FishNet.Transporting.Channel channel)
	{
		if (!TryBeginRespawnResurrectGuard(conn.ClientId, RespawnOperation, out long guardKey))
			return;

		try
		{
			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
				return;
			if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter player))
				return;

			// Only dead players can respawn at bind point.
			if (!player.IsFlagged(CharacterFlags.IsDead))
				return;

			player.DisableFlags(CharacterFlags.IsDead);
			if (player.TryGet(out ICharacterDamageController damageController))
				damageController.Revive(null, 999999);

			if ((player.IsInInstance() && player.InstanceSceneName != player.BindScene) ||
				(!player.IsInInstance() && player.SceneName != player.BindScene))
			{
				player.SceneName = player.BindScene;
				player.Motor.SetPositionAndRotationAndVelocity(player.BindPosition, player.Motor.Transform.rotation, Vector3.zero);
				player.DisableFlags(CharacterFlags.IsInInstance);
				player.DisableFlags(CharacterFlags.IsLoaded);
				player.NetworkObject.Owner.Disconnect(false);
			}
			else
			{
				player.Motor.SetPositionAndRotationAndVelocity(player.BindPosition, Quaternion.identity, Vector3.zero);
			}
		}
		finally
		{
			EndRespawnResurrectGuard(guardKey);
		}
	}

	/// <summary>
	/// Handles a dead player accepting a resurrect from another player.
	/// Revives at the current position (corpse location), no teleport.
	/// Rate-limited per-connection to prevent spam.
	/// </summary>
	/// <param name="conn">The network connection of the dead player.</param>
	/// <param name="msg">The resurrect-accept broadcast message.</param>
	/// <param name="channel">The channel on which the broadcast was received.</param>
	private void OnClientResurrectAcceptBroadcastReceived(NetworkConnection conn, ResurrectAcceptBroadcast msg, FishNet.Transporting.Channel channel)
	{
		if (!TryBeginRespawnResurrectGuard(conn.ClientId, ResurrectOperation, out long guardKey))
			return;

		try
		{
			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
				return;
			if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter player))
				return;

			// Only dead players can accept a resurrect.
			if (!player.IsFlagged(CharacterFlags.IsDead))
				return;

			// Validate the resurrector: must be online and in the same scene.
			// Without this check, a dead player could self-revive by sending a
			// fake ResurrectAcceptBroadcast with an arbitrary ResurrectorID.
			if (msg.ResurrectorID <= 0 ||
				!data.CharactersByID.TryGetValue(msg.ResurrectorID, out IPlayerCharacter resurrector) ||
				resurrector.SceneName != player.SceneName)
			{
				return;
			}

			player.DisableFlags(CharacterFlags.IsDead);
			if (player.TryGet(out ICharacterDamageController damageController))
				damageController.Revive(null, 999999);
		}
		finally
		{
			EndRespawnResurrectGuard(guardKey);
		}
	}

	/// <summary>
	/// Ingress guard for respawn and resurrect broadcasts to prevent rate-limit spam.
	/// </summary>
	private readonly IngressGuard respawnResurrectGuard = new IngressGuard();

	private const byte RespawnOperation = 1;
	private const byte ResurrectOperation = 2;
	private const int RespawnResurrectDebounceMs = 2000;

	private bool TryBeginRespawnResurrectGuard(int clientId, byte operation, out long guardKey)
	{
		return respawnResurrectGuard.TryBegin(clientId, operation, RespawnResurrectDebounceMs, out guardKey);
	}

	private void EndRespawnResurrectGuard(long guardKey)
	{
		respawnResurrectGuard.End(guardKey);
	}

	/// <summary>
	/// Periodic sweep to evict stale respawn/resurrect guard entries,
	/// preventing unbounded dictionary growth from aborted or partial client sessions.
	/// </summary>
	private void OnPeriodicRespawnResurrectSweep(float deltaTime)
	{
		if (Server == null || Server.ServerState != ConnectionState.Started)
			return;

		respawnResurrectGuard.Sweep(30f, 120f, 128);
	}
	}
}