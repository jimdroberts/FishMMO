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

			// Consume any deliberate-transfer marker for this connection. Read here, before
			// RemoveCharacterConnectionMapping decides whether the body may linger.
			bool deliberateTransfer = deliberateTransferClientIds.TryRemove(conn.ClientId, out _);

			// Clean up per-connection scene-unload rate-limit tracking.
			sceneUnloadLastTimeByClientId.TryRemove(conn.ClientId, out _);

			// Clean up per-connection validated-scene rate-limit tracking.
			validatedSceneLastTimeByClientId.TryRemove(conn.ClientId, out _);

			// The scene handshake can no longer complete or time out for a dead connection.
			sceneLoadDeadlines.TryRemove(conn.ClientId, out _);

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
				// A channel switch is the same kind of transfer and yet reaches the character
				// system only through this handler, so it announces itself in advance — see
				// BeginDeliberateTransfer. Without that, a character pulled into combat between
				// the switch's last state check and the disconnect landing left a body behind in
				// the scene it was leaving, and the destination kicked the arriving client for
				// contention on every retry until the linger ran out.
				RemoveCharacterConnectionMapping(conn, allowCombatLinger: !deliberateTransfer);
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

			// Cache the current scene name
			string currentScene = character.SceneName;

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

				// Tell the connection to unload their current world scene.
				sceneServerSystem.UnloadSceneForConnection(character.Owner, character.SceneName);

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
		/// Connections whose imminent disconnect is a deliberate transfer rather than a player
		/// leaving. See <see cref="BeginDeliberateTransfer"/>.
		/// </summary>
		private readonly ConcurrentDictionary<int, byte> deliberateTransferClientIds =
			new ConcurrentDictionary<int, byte>();

		/// <inheritdoc/>
		public void BeginDeliberateTransfer(NetworkConnection connection)
		{
			if (connection == null)
			{
				return;
			}
			deliberateTransferClientIds[connection.ClientId] = 0;
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
					DispatchCharacterEvent(OnDisconnect, conn, player, nameof(OnDisconnect));

					player.SceneName = player.BindScene;
					player.Motor.SetPositionAndRotationAndVelocity(player.BindPosition, player.Motor.Transform.rotation, Vector3.zero);
					player.DisableFlags(CharacterFlags.IsInInstance);
					player.DisableFlags(CharacterFlags.IsLoaded);

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

				// The resurrector must still be present and in the same scene when it is accepted.
				if (!data.CharactersByID.TryGetValue(offer.ResurrectorID, out IPlayerCharacter resurrector) ||
					resurrector.SceneName != player.SceneName)
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