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
			// Consume any server-initiated-disconnect marker for this connection. Read here,
			// before RemoveCharacterConnectionMapping decides whether the body may linger.
			bool serverInitiated = suppressCombatLingerClientIds.TryRemove(conn.ClientId, out _);

			// Clean up per-connection scene-unload rate-limit tracking.
			sceneUnloadLastTimeByClientId.TryRemove(conn.ClientId, out _);

			// Clean up per-connection validated-scene rate-limit tracking.
			validatedSceneLastTimeByClientId.TryRemove(conn.ClientId, out _);

			// The scene handshake can no longer complete or time out for a dead connection.
			ClearSceneLoadDeadline(conn.ClientId);

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

				if (waitingSceneCharacter != null)
				{
					TryExtractAndReleaseSession(data, waitingSceneCharacter.ID);

					DispatchCharacterEvent(OnDisconnect, conn, waitingSceneCharacter, nameof(OnDisconnect));

					if (IsGone(waitingSceneCharacter))
					{
						LogGoneCharacter(conn, waitingSceneCharacter, "still loading when its connection closed");
					}
					else
					{
						Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(waitingSceneCharacter.NetworkObject, true);
					}
				}
			}

			if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				return;
			}

			// Remove the connection->character entry
			data.ConnectionCharacters.Remove(conn);

			/* A character whose object was destroyed under it cannot be saved (its transform is gone) or despawned,
			 * but its claim must still be handed back. The save used to throw first, so nothing after it ran: the
			 * session was never released and the player could not log in anywhere until the server restarted.
			 * Its last periodic save stands, as after a crash. */
			if (IsGone(character))
			{
				LogGoneCharacter(conn, character, "in the world when its connection closed");
				if (character == null)
				{
					return;
				}
				ClearInstanceDeathCount(character.ID);
				data.CharactersByID.Remove(character.ID);
				if (!string.IsNullOrEmpty(character.CharacterNameLower))
				{
					data.CharactersByLowerCaseName.Remove(character.CharacterNameLower);
				}
				if (data.CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> goneWorld))
				{
					goneWorld.Remove(character.ID);
				}
				if (!skipOnDisconnect)
				{
					DispatchCharacterEvent(OnDisconnect, conn, character, nameof(OnDisconnect));
				}
				TryExtractAndReleaseSession(data, character.ID);
				return;
			}

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

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				CancelTeleport(character, "SceneServerSystem not found", "The teleporter is not working right now.", ToastSeverity.Error);
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
			string currentScene = character.CurrentSceneName();

			if (sceneServerSystem.WorldSceneDetailsCache == null ||
				!sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(currentScene, out WorldSceneDetails details))
			{
				CancelTeleport(character, $"scene '{currentScene}' not found in the world scene details cache", "The teleporter is not working right now.", ToastSeverity.Error);
				return;
			}

			// Check if teleporter is a valid scene teleporter
			if (!details.Teleporters.TryGetValue(TeleporterKey.Normalize(character.TeleporterName), out SceneTeleporterDetails teleporter))
			{
				CancelTeleport(character, $"teleporter '{character.TeleporterName}' not found in scene '{currentScene}'", "The teleporter leads nowhere.", ToastSeverity.Error);
				return;
			}

			/* A destination in the scene the character is already in is a local move — the same
			 * thing an interactable Teleporter with a Target does — and nothing more. It used to
			 * take the cross-scene path below regardless, so a hop across a courtyard was a full
			 * round trip through the world server, and from inside a dungeon it ejected the player
			 * from the instance whose name it was returning them to. Same-scene teleports also share the Teleporter's
			 * policy of not gating combat ; the transfer below still does. */
			if (string.Equals(teleporter.ToScene, currentScene, StringComparison.Ordinal))
			{
				if (character.Motor == null)
				{
					CancelTeleport(character, "character has no motor", "The teleporter cannot move you right now.", ToastSeverity.Error);
					return;
				}

				character.Motor.SetPositionAndRotationAndVelocity(teleporter.ToPosition, teleporter.ToRotation, Vector3.zero);
				// Not a transfer: the character is no longer teleporting the moment it has moved.
				character.TeleporterName = string.Empty;
				return;
			}

			// Prevent players from teleporting to another scene while in combat.
			// This closes the combat-escape exploit where a player could instantly
			// teleport to a different scene to avoid death or PvP.
			if (character.IsFlagged(CharacterFlags.IsInCombat))
			{
				CancelTeleport(character, "in combat", "You cannot teleport while in combat.", ToastSeverity.Warning);
				return;
			}

			if (character.Motor == null || character.Owner == null || !character.Owner.IsActive)
			{
				CancelTeleport(character, "character has no motor or live connection", "The teleporter cannot move you right now.", ToastSeverity.Error);
				return;
			}

			/* Queued: a scene teleporter fires from a physics trigger, and the release despawns the
			 * character. TeleporterName stays set until then, so the character can do nothing else.
			 * Leaving through a dungeon's exit counts as a deliberate exit; the hand-off clears the
			 * instance state. */
			if (!DeferHandOff(character.Owner, character,
				new TransferDestination(teleporter.ToScene, 0, teleporter.ToPosition, teleporter.ToRotation)))
			{
				CancelTeleport(character, "hand-off could not be queued", "The teleporter is not working right now.", ToastSeverity.Error);
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
		/// <param name="playerText">What the owner is told. A refusal that says nothing is
		/// indistinguishable from a lost packet, which is the dead-keypress failure every other
		/// refusal path here has had to grow an answer for.</param>
		/// <param name="severity">How the toast reads.</param>
		private void CancelTeleport(IPlayerCharacter character, string reason, string playerText, ToastSeverity severity)
		{
			Log.Debug("CharacterSystem", $"Teleport cancelled for {character.CharacterName}: {reason}.");
			character.TeleporterName = string.Empty;

			if (character.Owner != null && character.Owner.IsActive && !string.IsNullOrEmpty(playerText))
			{
				Server.NetworkWrapper.Broadcast(character.Owner, new ToastBroadcast()
				{
					Text = playerText,
					Severity = severity,
				}, true, FishNet.Transporting.Channel.Reliable);
			}
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

			// Instances have no channels, and the position below is an instance position.
			if (character.Motor == null || character.IsInInstance())
			{
				return false;
			}

			/* The same hand-off every transfer makes, to the same scene in another channel: the
			 * departure is announced while SceneHandle still names the channel being left (that
			 * event debits its population), then the character is rebound to the target channel,
			 * saved, released without a combat linger, and disconnected so the world server
			 * routes the reconnect there. */
			HandOffToScene(connection, character,
				new TransferDestination(character.SceneName, targetSceneHandle, character.Motor.Transform.position, character.Motor.Transform.rotation));
			return true;
		}

		/// <summary>
		/// True when <paramref name="character"/> is null or its Unity object has been destroyed.
		/// </summary>
		/// <remarks>
		/// <c>character == null</c> on the interface does not see a destroyed object: Unity's null is the overloaded
		/// <c>==</c> on <see cref="UnityEngine.Object"/>, which an interface comparison never calls. A destroyed
		/// character passes that test and then throws on its first transform or GameObject access. Its plain C#
		/// properties (ID, name, world server) are still readable, which is what lets its claim be released.
		/// </remarks>
		internal static bool IsGone(IPlayerCharacter character)
		{
			return character == null || (character is UnityEngine.Object unityObject && unityObject == null);
		}

		/// <summary>Reports a character found destroyed while this server still tracked it: a bug elsewhere, so loud.</summary>
		private static void LogGoneCharacter(NetworkConnection conn, IPlayerCharacter character, string where)
		{
			Log.Error("CharacterSystem", character == null
				? $"Connection {conn?.ClientId}: a null character was {where}."
				: $"Connection {conn?.ClientId}: character {character.ID} '{character.CharacterNameLower}' was destroyed while {where}; " +
				  "releasing its claim without a save (its last periodic save stands). Something destroyed or reused its object while this server still tracked it.");
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
				// A destroyed character is the disconnect path's to clean up; reading its transform here threw.
				if (IsGone(character) || string.IsNullOrWhiteSpace(character.SceneName))
				{
					continue;
				}

				// The scene the character is physically in — see CurrentSceneName. Reading
				// InstanceSceneName without also checking the instance flag was near enough,
				// but it trusted a field that is only meaningful while that flag is set.
				string sceneName = character.CurrentSceneName();

				/* Below a cloud world's islands is the deck's business, not the boundary's: a character falling there
				 * dies (OnPeriodicCloudWorldFallCheck), and a box drawn round the islands that ends above the death
				 * altitude would otherwise catch the fall first and set the character down on a random respawn. */
				if (character.GameObject != null &&
					SceneCloudWorld.TryGet(character.GameObject.scene, out SceneCloudWorld cloudWorld) &&
					character.Transform.position.y < cloudWorld.IslandFloor)
				{
					continue;
				}

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
		/// Installs a client's reported target frame onto its character's
		/// <see cref="ITargetController"/>, after verifying the claim.
		/// </summary>
		/// <remarks>
		/// Verification is the whole job here — the value feeds the observer-streaming target pin
		/// (see <see cref="TargetSelectionBroadcast"/>), and an unverified id would let a modified
		/// client pin arbitrary objects into its own observer budget. A non-zero id must resolve
		/// to a spawned CHARACTER in the sender's own scene; anything else is stored as no-target
		/// rather than dropped, so a bad report cannot leave a stale pin standing. Range is
		/// deliberately not checked at receipt — the pin bounds it at use, where the live distance
		/// is already in hand, and rejecting here on a moving value would flap.
		/// </remarks>
		private void OnClientTargetSelectionBroadcastReceived(NetworkConnection conn, TargetSelectionBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
				return;
			/* Twenty selections a second is more than any input device produces. The handler is
			 * synchronous, so the in-flight marker is released at once and only the debounce
			 * stamp applies. */
			if (!respawnResurrectGuard.TryBegin(conn.ClientId, TargetSelectionOperation, TargetSelectionDebounceMs, out long selectionGuardKey))
				return;
			respawnResurrectGuard.End(selectionGuardKey);

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
				return;
			if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter player))
				return;
			if (player == null || !player.TryGet(out ITargetController targetController))
				return;

			int verifiedTargetId = 0;
			if (msg.TargetObjectID != 0 &&
				player.NetworkObject != null &&
				player.NetworkObject.NetworkManager != null &&
				player.NetworkObject.NetworkManager.ServerManager.Objects.Spawned.TryGetValue(
					msg.TargetObjectID, out FishNet.Object.NetworkObject targetNob) &&
				targetNob != null &&
				targetNob.gameObject.scene == player.GameObject.scene &&
				targetNob.GetComponent<ICharacter>() != null)
			{
				verifiedTargetId = msg.TargetObjectID;
			}

			targetController.ServerSetClientSelectedTarget(verifiedTargetId);
		}

		/// <summary>
		/// Removes a buff the client asked to dismiss from its own character.
		/// </summary>
		/// <remarks>
		/// The whole job is verification. The client names a template ID and the server decides
		/// whether that means anything: the buff is looked for in the SENDER'S OWN container, so an
		/// id naming a buff somebody else is carrying resolves to nothing here, and
		/// <see cref="BaseBuffTemplate.CanBeDismissedByPlayer"/> is what refuses a debuff. Both
		/// tests are on this side of the wire, which is what makes the tooltip's "Left Mouse Button
		/// to remove" a description of what will happen rather than a hope.
		/// <para>
		/// Nothing is sent back. The removal marks the snapshot dirty, and
		/// the buff's absence reaches the owner on the reconcile it already receives.
		/// </para>
		/// </remarks>
		/// <param name="conn">The connection that asked.</param>
		/// <param name="msg">The buff the client wants gone.</param>
		/// <param name="channel">The channel the broadcast arrived on.</param>
		private void OnClientDismissBuffBroadcastReceived(NetworkConnection conn, DismissBuffBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
				return;
			/* Synchronous handler, so the in-flight marker is released at once and only the debounce
			 * stamp applies — same shape as the target-selection handler above. */
			if (!respawnResurrectGuard.TryBegin(conn.ClientId, BuffDismissOperation, BuffDismissDebounceMs, out long dismissGuardKey))
				return;
			respawnResurrectGuard.End(dismissGuardKey);

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
				return;
			if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter player))
				return;
			if (player == null || !player.TryGet(out ICharacterBuffs buffController))
				return;

			// Refused unless the buff is on and a player may dismiss it (the Buffs module's controller decides).
			buffController.TryDismissByPlayer(msg.TemplateID);
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

			if (player.Motor == null)
			{
				return false;
			}

			/* Back where it entered from: SceneName still names the open-world scene, SceneHandle
			 * its channel, LastWorldPosition the spot. The hand-off announces the departure while
			 * the character still belongs to the instance (so the instance's population is the one
			 * debited), marks the exit deliberate (an instance whose last occupant walked out is
			 * reclaimed on the next pulse, not held for the reconnect window), clears the instance
			 * bookkeeping and the death count with the release, and disconnects. */
			Log.Debug("CharacterSystem", $"{player.CharacterName} is leaving its instance; returning to {player.SceneName}.");
			HandOffToScene(conn, player,
				new TransferDestination(player.SceneName, player.SceneHandle, player.LastWorldPosition, player.LastWorldRotation));
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
				// By the scene the linger recorded, not the object's: a body despawned or reused by now still holds a claim
				// that ending the linger hands back.
				LingeringCharacter entry = kvp.Value;
				if (entry.CountedInInstance && entry.CountedSceneHandle == instanceSceneID)
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
		/// Ingress guard for the instance, leave-instance, target and buff-dismiss broadcasts, against rate-limit spam (named for
		/// the respawn and resurrect it once guarded too; those are the Combat module's).
		/// </summary>
		private readonly IngressGuard respawnResurrectGuard = new IngressGuard();

		// 1 and 2 were respawn and resurrect, which the Combat module's system guards on its own.
		private const byte LeaveInstanceOperation = 3;
		private const int RespawnResurrectDebounceMs = 2000;

		/// <summary>Target selection: cheap, but a Spawned lookup and a GetComponent per packet with no limit at all.</summary>
		private const byte TargetSelectionOperation = 7;
		private const int TargetSelectionDebounceMs = 50;

		/// <summary>Buff dismissal: a click, so an order of magnitude rarer than a target report — and a click that is refused costs a dictionary lookup and nothing else.</summary>
		private const byte BuffDismissOperation = 8;
		private const int BuffDismissDebounceMs = 100;

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