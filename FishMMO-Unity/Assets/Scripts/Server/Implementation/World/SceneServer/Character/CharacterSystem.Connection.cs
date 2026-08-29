using FishNet.Connection;
using FishNet.Object;
using System;
using System.Collections.Concurrent;
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
			// The connection is gone, so the transfer watchdog has nothing left to enforce.
			pendingTransferDisconnects.TryRemove(conn.ClientId, out _);

			// Consume any server-initiated-disconnect marker for this connection. Read here,
			// before RemoveCharacterConnectionMapping decides whether the body may linger.
			bool serverInitiated = suppressCombatLingerClientIds.TryRemove(conn.ClientId, out _);

			// Clean up per-connection scene-unload rate-limit tracking.
			sceneUnloadLastTimeByClientId.TryRemove(conn.ClientId, out _);

			// Clean up per-connection validated-scene rate-limit tracking.
			validatedSceneLastTimeByClientId.TryRemove(conn.ClientId, out _);

			// The scene handshake can no longer complete or time out for a dead connection.
			sceneLoadDeadlines.TryRemove(conn.ClientId, out _);

			// FishNet reuses ClientIds, and this marker says "this connection has already
			// acknowledged its start scenes". Left behind, the next session on the same id would
			// be treated as having completed a handshake it never performed.
			startScenesAckedClientIds.TryRemove(conn.ClientId, out _);

			// Same for the residency backstop that covers the window before the handshake.
			characterResidencyDeadlines.TryRemove(conn.ClientId, out _);

			// Clean up per-account auth callback rate-limit tracking.
			if (Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				authCallbackLastTimeByAccount.TryRemove(accountName, out _);
			}

			if (Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				// This is the only path where combat-logout linger applies: an actual dropped
				// connection. Teleport and bind-point respawn also route through
				// RemoveCharacterConnectionMapping, but those are deliberate transfers whose
				// character must be released promptly for the destination server to claim it.
				//
				// A channel switch is the same kind of transfer, and an administrative kick is a
				// removal rather than a departure; both reach the character system only through
				// this handler, so they announce themselves in advance — see
				// SuppressCombatLingerOnDisconnect. Without that, a character pulled into
				// combat between the switch's last state check and the disconnect landing left
				// a body behind in the scene it was leaving, and the destination kicked the
				// arriving client for contention on every retry until the linger ran out.
				RemoveCharacterConnectionMapping(conn, allowCombatLinger: !serverInitiated);
			}
		}

		/// <summary>
		/// Removes the character connection mapping and saves the character state to the database.
		/// Always performs a full session release (Online → Offline) because
		/// the destination Scene Server's TryClaimAsync requires session_state = Offline (or expired lease).
		/// </summary>
		/// <param name="conn">Network connection to remove.</param>
		/// <param name="skipOnDisconnect">If true, skips OnDisconnect event invocation.</param>
		/// <param name="allowCombatLinger">
		/// If true, a character that is in combat keeps its body in the world instead of being
		/// despawned. Only the genuine disconnect path passes true — deliberate transfers
		/// (teleport, bind-point respawn) must release promptly so the destination server can claim.
		/// </param>
		private void RemoveCharacterConnectionMapping(NetworkConnection conn, bool skipOnDisconnect = false, bool allowCombatLinger = false)
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

				DispatchCharacterEvent(OnDisconnect, conn, waitingSceneCharacter, nameof(OnDisconnect));

				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(waitingSceneCharacter.NetworkObject, true);
			}

			if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				return;
			}

			// Remove the connection->character entry
			data.ConnectionCharacters.Remove(conn);

			/* The instance death count goes with the character, on every route out of this server.
			 *
			 * TryLeaveInstance clears it on the way out of a dungeon, but that is only one of the
			 * ways a character stops being ours — logging out, being kicked, and being moved
			 * between scene servers all arrive here instead. Without this the map would keep one
			 * entry per character the process has ever hosted, and a character that reconnected
			 * into the same instance would resume a count from a run it had already left. */
			ClearInstanceDeathCount(character.ID);

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
				DispatchCharacterEvent(OnDisconnect, conn, character, nameof(OnDisconnect));
			}

			// Combat logout: keep the body in the world rather than despawning it. The session
			// token stays in SessionTokens on purpose — it is what keeps the lease refreshed and
			// this server authoritative over a body that can still be damaged and killed.
			if (allowCombatLinger && TryBeginCombatLinger(character))
			{
				return;
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
				CancelTeleport(character, "in combat");
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				CancelTeleport(character, "SceneServerSystem not found");
				return;
			}

			/* The scene the character is standing in, which is not always SceneName.
			 *
			 * Inside an instance, SceneName still names the open-world scene the character will
			 * return to — that is what makes the return trip possible — while the character is
			 * physically in InstanceSceneName. Looking teleporters up under SceneName therefore
			 * consulted the wrong scene's teleporter list from inside a dungeon, so no teleporter
			 * placed in a dungeon could ever fire, and with no other way out a player who entered
			 * one had no exit at all. */
			bool leavingInstance = character.IsInInstance();
			string currentScene = character.CurrentSceneName();

			if (sceneServerSystem.WorldSceneDetailsCache == null ||
				!sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(currentScene, out WorldSceneDetails details))
			{
				CancelTeleport(character, $"scene '{currentScene}' not found in the world scene details cache");
				return;
			}

			// Check if teleporter is a valid scene teleporter
			if (details.Teleporters.TryGetValue(character.TeleporterName, out SceneTeleporterDetails teleporter))
			{
				//Log.Debug("CharacterSystem", $"Teleporter: {character.TeleporterName} found! Teleporting {character.CharacterName} to {teleporter.ToScene}.");

				//Log.Debug("CharacterSystem", $"Unloading scene for {character.CharacterName}: {character.SceneName}|{character.SceneHandle}");

				// Tell the connection to unload the scene it is actually in, which for an
				// instanced character is the instance rather than SceneName.
				sceneServerSystem.UnloadSceneForConnection(character.Owner, currentScene);

				// Character becomes immortal when teleporting
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					//Log.Debug("CharacterSystem", $"{character.CharacterName} is now immortal.");
					damageController.Immortal = true;
				}

				NetworkConnection owner = character.Owner;

				// Invoke disconnect early when teleporting because we require the scene the character is in.
				DispatchCharacterEvent(OnDisconnect, owner, character, nameof(OnDisconnect));

				character.SceneName = teleporter.ToScene;
				character.Motor.SetPositionAndRotationAndVelocity(teleporter.ToPosition, teleporter.ToRotation, Vector3.zero);

				// Remove the character from an instance if it was in one.
				character.DisableFlags(CharacterFlags.IsInInstance);
				character.DisableFlags(CharacterFlags.IsLoaded);

				if (leavingInstance)
				{
					/* The character is in the open world again, so the transform is once more
					 * the open-world position and the instance bookkeeping must not outlive the
					 * trip. Clearing the id matters: it is what the world server reads to decide
					 * a character belongs in an instance, and a stale one left behind is a
					 * standing invitation to route a subsequent session back into a dungeon the
					 * player already walked out of.
					 *
					 * BuildCharacterData reads IsInInstance() — already false by this point — so
					 * the save below correctly writes the destination as the world position. */
					character.InstanceID = 0;
					character.InstanceSceneName = null;
					character.InstanceSceneHandle = 0;
					character.LastWorldPosition = teleporter.ToPosition;
					character.LastWorldRotation = teleporter.ToRotation;
				}

				// Save the character and fully release the session so the destination scene server can claim it
				RemoveCharacterConnectionMapping(owner, skipOnDisconnect: true);

				// Nothing here disconnects the client: the transfer relies on the client
				// reporting the scene unload above, and that report is the only thing that
				// sends it back to the world server. Arm a deadline so a client that never
				// reports — crashed, modified, or one whose broadcast was dropped — cannot sit
				// forever on a scene server that no longer holds its character.
				ArmTransferDisconnect(owner);
			}
			else
			{
				CancelTeleport(character, $"teleporter '{character.TeleporterName}' not found in scene '{currentScene}'");
			}
		}

		/// <summary>
		/// Abandons an in-progress teleport, clearing the pending destination so the character
		/// is no longer considered mid-teleport.
		/// </summary>
		/// <remarks>
		/// This is mandatory on every path that declines a teleport.
		/// <see cref="IPlayerCharacter.IsTeleporting"/> is derived purely from
		/// <see cref="IPlayerCharacter.TeleporterName"/> being non-empty, and
		/// <see cref="SceneTeleporter"/> sets that name *before* raising the event this handler
		/// serves. Returning without clearing it therefore left the character permanently
		/// "teleporting", which is not a cosmetic state:
		/// <list type="bullet">
		/// <item><description><c>KCCPlayer.OnReplicate</c> drops all movement input.</description></item>
		/// <item><description><c>AbilityController</c> refuses to activate anything.</description></item>
		/// <item><description><c>Region</c> enter/exit callbacks stop firing.</description></item>
		/// <item><description><see cref="SceneTeleporter"/> ignores every future trigger, so the character can never teleport again.</description></item>
		/// </list>
		/// Only a despawn clears it (via <c>PlayerCharacter.ResetState</c>), so the player was
		/// frozen in place until they relogged. The combat rejection above made this trivially
		/// reachable: walk into any scene teleporter while in combat.
		/// </remarks>
		/// <param name="character">Character whose teleport is being cancelled.</param>
		/// <param name="reason">Why the teleport was declined, for diagnostics.</param>
		private void CancelTeleport(IPlayerCharacter character, string reason)
		{
			Log.Debug("CharacterSystem", $"Teleport cancelled for {character.CharacterName}: {reason}.");
			character.TeleporterName = string.Empty;
		}

		/// <summary>
		/// Connections whose imminent disconnect the server initiated, rather than a player
		/// leaving. See <see cref="SuppressCombatLingerOnDisconnect"/>.
		/// </summary>
		private readonly ConcurrentDictionary<int, byte> suppressCombatLingerClientIds =
			new ConcurrentDictionary<int, byte>();

		/// <inheritdoc/>
		public void SuppressCombatLingerOnDisconnect(NetworkConnection connection)
		{
			if (connection == null)
			{
				return;
			}
			suppressCombatLingerClientIds[connection.ClientId] = 0;
		}

		/// <inheritdoc/>
		public bool BeginChannelTransfer(NetworkConnection connection, long targetSceneHandle)
		{
			if (connection == null || !connection.IsActive)
			{
				return false;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
				!data.ConnectionCharacters.TryGetValue(connection, out IPlayerCharacter character))
			{
				return false;
			}

			if (character.SceneHandle == targetSceneHandle)
			{
				return false;
			}

			/* Announce the departure while SceneHandle still names the instance being left.
			 *
			 * This event is what debits the scene's population, and every other subscriber
			 * (party, guild, chat) also reasons about the scene the character is leaving. Doing
			 * it after the rewrite told all of them the wrong scene, and left the source
			 * instance permanently over-populated. Same ordering, and the same reason, as the
			 * bind-point respawn path above. */
			DispatchCharacterEvent(OnDisconnect, connection, character, nameof(OnDisconnect));

			// Now bind the character to the destination channel. BuildCharacterData captures
			// this during the release below, so the world server routes the reconnect here.
			character.SceneHandle = targetSceneHandle;

			// Prevent gameplay actions during the transition.
			character.DisableFlags(CharacterFlags.IsLoaded);

			/* Save and fully release before the connection drops, so the destination scene
			 * server can claim the character the moment the client arrives. Passing
			 * skipOnDisconnect avoids raising the event a second time, and going through this
			 * path rather than the connection-stopped handler means a character pulled into
			 * combat between the caller's checks and here cannot start a combat-logout linger:
			 * a linger would hold the body and its claim on this server while the row says the
			 * character belongs to the destination, and the arriving client would be kicked for
			 * contention on every retry until the linger expired. */
			RemoveCharacterConnectionMapping(connection, skipOnDisconnect: true);

			// Belt and braces: if anything below re-enters the ordinary disconnect path, it must
			// still be treated as a transfer rather than as a player walking out mid-fight.
			SuppressCombatLingerOnDisconnect(connection);

			connection.Disconnect(false);
			return true;
		}

		/// <summary>
		/// Connections whose character has been handed off, mapped to the UTC deadline by which
		/// they must have disconnected on their own.
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> pendingTransferDisconnects =
			new ConcurrentDictionary<int, DateTime>();

		/// <summary>Grace period a client gets to tear down its scene and disconnect by itself.</summary>
		private static readonly TimeSpan TransferDisconnectGrace = TimeSpan.FromSeconds(15);

		/// <summary>Interval in seconds between transfer-watchdog sweeps.</summary>
		private const float TransferDisconnectSweepIntervalSeconds = 5f;

		/// <summary>
		/// Starts the watchdog that force-disconnects <paramref name="conn"/> if it has not left
		/// on its own once its character has been released to another scene server.
		/// </summary>
		private void ArmTransferDisconnect(NetworkConnection conn)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}
			pendingTransferDisconnects[conn.ClientId] = DateTime.UtcNow + TransferDisconnectGrace;
		}

		/// <summary>
		/// Disconnects connections that were handed off but never left.
		/// </summary>
		private void OnPeriodicTransferDisconnectSweep(float deltaTime)
		{
			if (Server == null || Server.ServerState != ConnectionState.Started || pendingTransferDisconnects.IsEmpty)
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData);

			foreach (var kvp in pendingTransferDisconnects)
			{
				if (nowUtc < kvp.Value)
				{
					continue;
				}

				pendingTransferDisconnects.TryRemove(kvp.Key, out _);

				NetworkConnection conn = null;
				if (ServerManager != null)
				{
					ServerManager.Clients.TryGetValue(kvp.Key, out conn);
				}
				if (conn == null || !conn.IsActive)
				{
					continue;
				}

				// FishNet recycles ClientIds. A connection that now holds a character is a
				// different session and must be left alone.
				if (mappingData != null &&
					(mappingData.ConnectionCharacters.ContainsKey(conn) ||
					 mappingData.WaitingSceneLoadCharacters.ContainsKey(conn)))
				{
					continue;
				}

				Log.Warning("CharacterSystem",
					$"Connection {kvp.Key} did not disconnect after its character was transferred; disconnecting.");
				conn.Disconnect(false);
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

				// The scene the character is physically in — see CurrentSceneName. Reading
				// InstanceSceneName without also checking the instance flag was near enough,
				// but it trusted a field that is only meaningful while that flag is set.
				string sceneName = character.CurrentSceneName();

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

			IPlayerCharacter playerCharacter = defender as IPlayerCharacter;

			/* Flag the death before running anything else.
			 *
			 * CharacterDamageController.Kill guards re-entry with this exact flag, but sets
			 * nothing itself — this handler is what makes that guard true, and it is invoked at
			 * the very end of Kill. Anything side-effecting that runs before the flag is set is
			 * therefore executing inside a window where a second Kill on the same character
			 * would sail straight past the guard and recurse. Buff removal below is exactly
			 * such a call: it invokes each buff's removal effects, which run game logic.
			 *
			 * Do NOT revive or teleport here — the player chooses Respawn or Resurrect. */
			if (playerCharacter != null)
			{
				playerCharacter.EnableFlags(CharacterFlags.IsDead);
			}

			if (defender.TryGet(out IBuffController buffController))
			{
				buffController.RemoveAll(false);
			}

			// Handle Player deaths
			if (playerCharacter != null)
			{
				/* A difficulty that limits lives spends one here.
				 *
				 * After the death flag is set and the buffs are gone, so the character is in the
				 * state the rest of the death path expects before anything can move it. */
				ApplyInstanceDeathRules(playerCharacter);

				//Log.Debug("CharacterSystem", $"PlayerCharacter: {playerCharacter.GameObject.name} Died");

				// A combat-logged body has no owner to notify — it was killed precisely because
				// nobody was driving it. Skip the broadcast and end the linger now: the death is
				// the outcome the body was left exposed for, and persisting it immediately means
				// a crash cannot undo it.
				if (playerCharacter.Owner == null || !playerCharacter.Owner.IsActive)
				{
					if (lingeringCharacters.ContainsKey(playerCharacter.ID))
					{
						FinalizeCombatLinger(playerCharacter.ID, "killed while logged out");
					}
					else
					{
						/* Ownerless and not lingering. There is no connection to notify and no
						 * linger to finalise, so the character is flagged dead with nothing
						 * driving it out of that state — the periodic save will persist the
						 * flag and the player will meet the death dialog on their next login.
						 * Not known to be reachable, but silence here would make it invisible
						 * if it ever became so. */
						Log.Warning("CharacterSystem",
							$"Character {playerCharacter.ID} was killed with no active owner and no combat-logout linger; " +
							"no death notification was sent. It will be flagged dead on its next load.");
					}
					return;
				}

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
							/* The owner's NetworkObject is checked rather than assumed. A pet is
							 * despawned alongside its owner's character, but the two can land in
							 * the same frame — an owner despawned first leaves PetOwner pointing
							 * at a destroyed Unity object, and dereferencing it throws out of
							 * this handler, skipping the despawn below and every remaining
							 * OnKilled subscriber. */
							NetworkObject ownerObject = petOwner.NetworkObject;
							if (ownerObject != null)
							{
								OnPetKilled?.Invoke(ownerObject.Owner, petOwner);
							}
						}

						/* Outside the owner check, deliberately. A pet whose owner reference has
						 * already been cleared — or which was never owned by a player — still has
						 * to leave the world. Nested inside, such a pet was left spawned, flagged
						 * dead, with its brain still enabled and nothing anywhere that would ever
						 * collect it: Pet.Despawn skips the corpse timer that reclaims an NPC. */
						pet.Despawn();
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

				// Choosing the bind point declines any resurrect that was on the table.
				pendingResurrectOffers.Remove(player.ID);

				player.DisableFlags(CharacterFlags.IsDead);
				if (player.TryGet(out ICharacterDamageController damageController))
					damageController.Revive(null, 999999);

				if ((player.IsInInstance() && player.InstanceSceneName != player.BindScene) ||
					(!player.IsInInstance() && player.SceneName != player.BindScene))
				{
					// Respawning at a bind point in another scene is a scene-server transfer, so it
					// has to follow the same order as the teleporter path.
					//
					// Previously this only mutated the character and disconnected, leaving the
					// save and session release to the connection-stopped handler. By then SceneName
					// had already been overwritten with BindScene, so every OnDisconnect subscriber
					// — party, guild, and the scene server's own per-scene bookkeeping — was told
					// the player left the scene they were respawning *into* rather than the one
					// they died in.
					bool leavingInstance = player.IsInInstance();

					DispatchCharacterEvent(OnDisconnect, conn, player, nameof(OnDisconnect));

					player.SceneName = player.BindScene;
					player.Motor.SetPositionAndRotationAndVelocity(player.BindPosition, player.Motor.Transform.rotation, Vector3.zero);
					player.DisableFlags(CharacterFlags.IsInInstance);
					player.DisableFlags(CharacterFlags.IsLoaded);

					if (leavingInstance)
					{
						/* Clear the instance bookkeeping, exactly as the teleport and
						 * leave-instance paths do. Dropping only the flag left InstanceID on the
						 * character, and BuildCharacterData persists it unconditionally — so a
						 * player who died in a dungeon and respawned at their bind point kept a
						 * row pointing at that dungeon's scene instance. Nothing reads it while
						 * the flag is clear, which is exactly what makes it dangerous: it is a
						 * live instance reference sitting on a character that is demonstrably not
						 * in one, waiting for anything that sets the flag without also setting
						 * the id.
						 *
						 * The transform is the bind point now, so the open-world position the save
						 * writes must be that too. */
						player.InstanceID = 0;
						player.InstanceSceneName = null;
						player.InstanceSceneHandle = 0;
						player.LastWorldPosition = player.BindPosition;
						player.LastWorldRotation = player.Motor.Transform.rotation;
					}

					// Save and release the session before dropping the connection so the
					// destination scene server can claim the character.
					RemoveCharacterConnectionMapping(conn, skipOnDisconnect: true);

					conn.Disconnect(false);
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
		/// Handles a client asking to leave the instance it is in and return to the open world.
		/// </summary>
		/// <remarks>
		/// This is the system-level guarantee that instanced content cannot trap a player. A
		/// dungeon is expected to provide an exit teleporter, but that is content data and this
		/// does not depend on it. A character bound to an instance is routed back into it on
		/// every login, so "log out and back in" is not an escape — without this, a dungeon
		/// authored without a reachable exit would strand its occupants permanently.
		/// <para>
		/// Gated by <c>CanActOrMove</c>, so it is not a combat escape, and it performs the same
		/// ordered release as the bind-point respawn: announce the departure while the character
		/// still belongs to the instance, then rebind, save, release, and drop the connection so
		/// the world server routes the reconnect to the open world.
		/// </para>
		/// </remarks>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The leave-instance broadcast (empty payload).</param>
		/// <param name="channel">The channel on which the broadcast was received.</param>
		private void OnClientRequestLeaveInstanceBroadcastReceived(NetworkConnection conn, RequestLeaveInstanceBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			if (!TryBeginRespawnResurrectGuard(conn.ClientId, LeaveInstanceOperation, out long guardKey))
			{
				return;
			}

			try
			{
				if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
					!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter player))
				{
					return;
				}

				TryLeaveInstance(player);
			}
			finally
			{
				EndRespawnResurrectGuard(guardKey);
			}
		}

		/// <summary>
		/// Removes <paramref name="player"/> from its instance and returns it to the open world.
		/// </summary>
		/// <remarks>
		/// Shared by the leave-instance broadcast and the <c>/leaveinstance</c> chat command, so
		/// the escape hatch is reachable whether or not the client's UI offers a button for it.
		/// </remarks>
		/// <param name="player">Character to release from its instance.</param>
		/// <param name="enforceState">
		/// True for a player-initiated exit, which is refused in combat or while dead. False when
		/// the instance itself is going away and there is nothing to gate — see
		/// <see cref="ReturnInstanceOccupantsToWorld"/>. A forced exit cannot be an escape: the
		/// scene the character would be escaping from is being unloaded either way.
		/// </param>
		/// <returns><c>true</c> when the transfer was started.</returns>
		private bool TryLeaveInstance(IPlayerCharacter player, bool enforceState = true)
		{
			if (player == null || !player.IsInInstance())
			{
				return false;
			}

			NetworkConnection conn = player.Owner;
			if (conn == null || !conn.IsActive)
			{
				return false;
			}

			/* A dead character keeps the death dialog's own routes — respawn at bind, or accept a
			 * resurrect — so it does not need this one, and allowing it would let a corpse walk
			 * itself out of the dungeon it died in. Combat is refused for the same reason every
			 * other voluntary transfer is. */
			if (enforceState && !CharacterStateValidation.CanActOrMove(player))
			{
				Server.NetworkWrapper.Broadcast(conn,
					new SceneTransferRefusedBroadcast { Reason = SceneTransferRefusalReason.CharacterStateChanged },
					true, FishNet.Transporting.Channel.Reliable);
				return false;
			}

			/* Captured before the dispatch clears them below: the instance being left has to be
			 * identifiable after the character has stopped pointing at it. */
			long departedWorldServerID = player.WorldServerID;
			string departedInstanceScene = player.InstanceSceneName;
			long departedInstanceHandle = player.InstanceSceneHandle;

			// Announce the departure while the character still belongs to the instance, so the
			// instance's population is the one debited. Same ordering as the bind-point respawn
			// and the channel transfer, and for the same reason.
			DispatchCharacterEvent(OnDisconnect, conn, player, nameof(OnDisconnect));

			/* This is the VOLUNTARY route out — the leave-instance broadcast, /leaveinstance, and
			 * the forced return when an instance is closing. Marking it after the debit means an
			 * instance whose last occupant walked out is reclaimed on the next pulse instead of
			 * being held for the idle timeout, which exists to let a DROPPED connection reconnect
			 * into its run. See ISceneInstanceDetails.VacatedDeliberately. */
			if (Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> instanceSceneServerSystem))
			{
				instanceSceneServerSystem.NoteDeliberateInstanceExit(
					departedWorldServerID, departedInstanceScene, departedInstanceHandle);
			}

			// Put the character back where it entered from. SceneName already names the open
			// world scene it came from; LastWorldPosition is the position within it.
			player.Motor.SetPositionAndRotationAndVelocity(player.LastWorldPosition, player.LastWorldRotation, Vector3.zero);
			player.InstanceID = 0;
			player.InstanceSceneName = null;
			player.InstanceSceneHandle = 0;
			player.DisableFlags(CharacterFlags.IsInInstance);
			player.DisableFlags(CharacterFlags.IsLoaded);

			/* The run is over, so its death count goes with it. A limited-lives rule is about one
			 * attempt: coming back to the same dungeon starts a fresh one, and a count that
			 * survived the exit would end that attempt before it began. */
			ClearInstanceDeathCount(player.ID);

			// Save and fully release before dropping the connection so the destination scene
			// server can claim the character.
			RemoveCharacterConnectionMapping(conn, skipOnDisconnect: true);

			// The disconnect below must not be read as a combat logout.
			SuppressCombatLingerOnDisconnect(conn);

			Log.Debug("CharacterSystem", $"{player.CharacterName} left its instance; returning to {player.SceneName}.");
			conn.Disconnect(false);
			return true;
		}

		/// <inheritdoc/>
		public int ReturnInstanceOccupantsToWorld(long instanceSceneID, string reason)
		{
			if (instanceSceneID <= 0 ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return 0;
			}

			/* Snapshotted before anything is moved. Releasing a character mutates the very maps
			 * this walks, and relying on FishNet deferring the disconnect would be relying on its
			 * scheduling rather than on anything this loop controls. */
			List<IPlayerCharacter> occupants = null;
			foreach (var kvp in data.ConnectionCharacters)
			{
				IPlayerCharacter resident = kvp.Value;
				if (resident != null &&
					resident.IsInInstance() &&
					resident.InstanceSceneHandle == instanceSceneID)
				{
					(occupants ??= new List<IPlayerCharacter>()).Add(resident);
				}
			}

			/* Bodies left by a combat logout are not in ConnectionCharacters and have no owner, so
			 * the loop above cannot see them — but they are standing in this scene and they hold
			 * their characters' session claims. Destroying the scene under them would strand those
			 * claims until the lease expired, locking the players out of every scene server for two
			 * minutes. Ending the linger saves the body and hands the claim back, which is its
			 * ordinary conclusion. */
			List<long> lingering = null;
			foreach (var kvp in lingeringCharacters)
			{
				IPlayerCharacter body = kvp.Value.Character;
				if (body != null &&
					body.IsInInstance() &&
					body.InstanceSceneHandle == instanceSceneID)
				{
					(lingering ??= new List<long>()).Add(kvp.Key);
				}
			}

			int moved = 0;

			if (occupants != null)
			{
				for (int i = 0; i < occupants.Count; ++i)
				{
					// enforceState: false — the scene is going away, so there is no state that
					// could make staying the right answer.
					if (TryLeaveInstance(occupants[i], enforceState: false))
					{
						++moved;
					}
				}
			}

			if (lingering != null)
			{
				for (int i = 0; i < lingering.Count; ++i)
				{
					FinalizeCombatLinger(lingering[i], reason ?? "instance closed");
				}
			}

			if (moved > 0 || lingering != null)
			{
				Log.Debug("CharacterSystem",
					$"Instance {instanceSceneID} closed ({reason}): returned {moved} character(s) to the open world" +
					(lingering != null ? $" and ended {lingering.Count} combat-logout linger(s)." : "."));
			}

			return moved;
		}

		/// <summary>
		/// <c>/leaveinstance</c> chat command. Same effect as
		/// <see cref="OnClientRequestLeaveInstanceBroadcastReceived"/>.
		/// </summary>
		/// <remarks>
		/// Deliberately available as a command as well as a broadcast. The broadcast needs a
		/// client UI to send it, and a UI that has not been authored yet is not an escape hatch
		/// — the whole point of this path is that leaving instanced content must not depend on
		/// content or UI having been set up correctly.
		/// </remarks>
		/// <param name="character">Character issuing the command.</param>
		/// <param name="msg">The chat message that carried the command.</param>
		/// <returns>Always <c>true</c>: the command is consumed either way, never echoed to chat.</returns>
		private bool OnLeaveInstanceCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			TryLeaveInstance(character);
			return true;
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

				/* Re-checked here, not just at the offer.
				 *
				 * An offer recorded outside a no-resurrection dungeon stands for thirty seconds,
				 * and a character can be moved into one inside that window — so the offer check
				 * alone is a prompt filter, and this is the rule. Where the character is standing
				 * when it redeems is what decides. */
				if (!IsResurrectionAllowed(player))
				{
					pendingResurrectOffers.Remove(player.ID);
					SendSystemMessage(conn, "The dead cannot be raised in this dungeon.");
					return;
				}

				/* The accept must answer an offer this server actually recorded. Checking only
				 * that the named resurrector existed and shared the scene — as this did — let a
				 * dead player revive themselves at full health whenever anyone else was nearby,
				 * with no resurrect ever cast. */
				if (!pendingResurrectOffers.TryGetValue(player.ID, out PendingResurrectOffer offer))
				{
					return;
				}

				if (DateTime.UtcNow >= offer.ExpiresUtc)
				{
					pendingResurrectOffers.Remove(player.ID);
					return;
				}

				if (msg.ResurrectorID <= 0 || msg.ResurrectorID != offer.ResurrectorID)
				{
					return;
				}

				/* The resurrector must still be present and in the same scene instance when the
				 * offer is accepted.
				 *
				 * Compared by scene handle, not by name. CharactersByID holds every character on
				 * this scene server, across every scene instance it hosts — and scene stacking
				 * means two channels of one open-world scene, or two copies of one dungeon, share
				 * a name. Inside an instance it is worse: SceneName keeps naming the open-world
				 * scene the character will return to, so every character who entered a dungeon
				 * from the same zone compares equal regardless of which instance they are in.
				 * The handle is unambiguous within this process, which is the only place this
				 * check runs. Same rule as BindstoneAction. */
				if (!data.CharactersByID.TryGetValue(offer.ResurrectorID, out IPlayerCharacter resurrector) ||
					resurrector.GameObject == null ||
					player.GameObject == null ||
					resurrector.GameObject.scene.handle != player.GameObject.scene.handle)
				{
					return;
				}

				pendingResurrectOffers.Remove(player.ID);

				player.DisableFlags(CharacterFlags.IsDead);
				if (player.TryGet(out ICharacterDamageController damageController))
				{
					// The offer's amount rather than a blanket full heal, credited to the
					// resurrector so their ECA resurrect triggers fire.
					damageController.Revive(resurrector, offer.Amount);
				}
			}
			finally
			{
				EndRespawnResurrectGuard(guardKey);
			}
		}

		/// <summary>
		/// A resurrect that has been offered to a character and is awaiting their answer.
		/// </summary>
		private sealed class PendingResurrectOffer
		{
			/// <summary>Character that cast the resurrect.</summary>
			public long ResurrectorID;
			/// <summary>Health the offer restores, as configured on the ability that made it.</summary>
			public int Amount;
			/// <summary>When the offer lapses if it is not answered.</summary>
			public DateTime ExpiresUtc;
		}

		/// <summary>
		/// Outstanding resurrect offers, keyed by the character they were made to. Main-thread only.
		/// </summary>
		/// <remarks>
		/// Accepting a resurrect used to be validated only by checking that the named resurrector
		/// existed and stood in the same scene — never that they had offered anything. A dead player
		/// could therefore revive themselves at full health at will, by naming any other character
		/// in the scene, without a resurrect ever being cast. Recording the offer makes the accept
		/// answerable to something real, and carries the ability's configured heal amount through to
		/// the revive instead of substituting a blanket full heal.
		/// </remarks>
		private readonly Dictionary<long, PendingResurrectOffer> pendingResurrectOffers =
			new Dictionary<long, PendingResurrectOffer>();

		/// <summary>How long an unanswered resurrect offer stands.</summary>
		private static readonly TimeSpan ResurrectOfferLifetime = TimeSpan.FromSeconds(30);

		/// <summary>
		/// Records a resurrect offer and tells the target's client, so its death dialog can present
		/// the accept option.
		/// </summary>
		/// <param name="initiator">Character casting the resurrect.</param>
		/// <param name="target">Character being offered the resurrect.</param>
		/// <param name="amount">Health the offer would restore.</param>
		private void CharacterDamageController_OnResurrectOffered(ICharacter initiator, ICharacter target, int amount)
		{
			if (initiator == null || amount <= 0)
			{
				return;
			}

			IPlayerCharacter player = target as IPlayerCharacter;
			if (player == null || player.Owner == null || !player.Owner.IsActive)
			{
				return;
			}

			// Only a dead character has anything to accept.
			if (!player.IsFlagged(CharacterFlags.IsDead))
			{
				return;
			}

			/* Some dungeons forbid resurrection outright, and offering one there would put a
			 * button on a dead player's screen that the accept path is going to refuse. Recording
			 * nothing is also what makes that refusal safe: there is no stale offer to redeem the
			 * moment they leave. */
			if (!IsResurrectionAllowed(player))
			{
				SendSystemMessage(player.Owner, "The dead cannot be raised in this dungeon.");
				return;
			}

			pendingResurrectOffers[player.ID] = new PendingResurrectOffer
			{
				ResurrectorID = initiator.ID,
				Amount = amount,
				ExpiresUtc = DateTime.UtcNow + ResurrectOfferLifetime,
			};

			Server.NetworkWrapper.Broadcast(player.Owner,
				new ResurrectOfferBroadcast { ResurrectorID = initiator.ID }, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>
		/// Ingress guard for respawn and resurrect broadcasts to prevent rate-limit spam.
		/// </summary>
		private readonly IngressGuard respawnResurrectGuard = new IngressGuard();

		private const byte RespawnOperation = 1;
		private const byte ResurrectOperation = 2;
		private const byte LeaveInstanceOperation = 3;
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

			SweepExpiredResurrectOffers();
		}

		/// <summary>
		/// Drops resurrect offers that were never answered.
		/// </summary>
		/// <remarks>
		/// The accept path rejects an expired offer on its own, so this is about not retaining one
		/// entry per unanswered resurrect for the life of the process — a player who is offered a
		/// resurrect and respawns instead, or simply logs out, leaves nothing behind to remove it.
		/// </remarks>
		private void SweepExpiredResurrectOffers()
		{
			if (pendingResurrectOffers.Count == 0)
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			List<long> expired = null;
			foreach (var kvp in pendingResurrectOffers)
			{
				if (nowUtc >= kvp.Value.ExpiresUtc)
				{
					(expired ??= new List<long>()).Add(kvp.Key);
				}
			}

			if (expired == null)
			{
				return;
			}

			for (int i = 0; i < expired.Count; ++i)
			{
				pendingResurrectOffers.Remove(expired[i]);
			}
		}
	}
}