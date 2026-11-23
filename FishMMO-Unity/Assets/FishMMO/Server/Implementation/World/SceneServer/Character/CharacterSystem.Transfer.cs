using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The one way a character leaves this scene server for a place in another scene, and the transfer host shared code
	/// reaches through <see cref="CharacterTransfer"/> (issue #241).
	/// </summary>
	/// <remarks>
	/// <para>The hand-off used to be written out four times — the scene teleporter, the bind-point respawn, the channel
	/// switch and leaving an instance — each with its own idea of which bookkeeping to clear. The teleporter also took a
	/// different road out: it asked the client to unload the scene by NAME (which, with stacked channels, may name
	/// another instance of it) and waited for the client's report before disconnecting. Every route now ends the same
	/// way the other three already did: release, then drop the connection, and the client's reconnect goes back to the
	/// world server, which routes it by the rebound row.</para>
	/// <para><b>Order matters.</b> The departure is announced while the character still names the scene it is leaving
	/// (party, guild, chat and the scene's own population all reason about that scene), and only then is it rebound to
	/// the destination, saved and released — so the destination scene server can claim it the moment the client
	/// arrives.</para>
	/// <para><b>Deferred when it may be inside a tick.</b> The release despawns the character at once. A travel spell takes
	/// effect inside the character's own ability tick, and a scene teleporter fires from a physics trigger, so those two
	/// hand off on the main-thread queue instead (<see cref="DeferHandOff"/>); a character waiting there is refused any
	/// second transfer. The death respawn, leaving an instance and the channel switch run from broadcast handlers and
	/// hand off directly.</para>
	/// </remarks>
	public partial class CharacterSystem : ICharacterTransferHost
	{
		/// <summary>Characters whose hand-off is queued for the main thread. Main thread.</summary>
		private readonly HashSet<long> pendingHandOffCharacterIDs = new HashSet<long>();

		/// <summary>Installs this system as the shared <see cref="CharacterTransfer.Host"/>.</summary>
		private void InstallTransferHost()
		{
			CharacterTransfer.Host = this;
		}

		/// <summary>Removes this system as the shared host, if it still is.</summary>
		private void UninstallTransferHost()
		{
			if (ReferenceEquals(CharacterTransfer.Host, this))
			{
				CharacterTransfer.Host = null;
			}
		}

		/// <inheritdoc/>
		public TransferOutcome Transfer(IPlayerCharacter character, in TransferDestination destination, out TransferRefusal refusal)
		{
			if (!TryResolveTransferable(character, out NetworkConnection conn, out refusal))
			{
				return TransferOutcome.Refused;
			}

			if (string.IsNullOrEmpty(destination.SceneName) || !IsKnownScene(destination.SceneName))
			{
				refusal = TransferRefusal.UnknownScene;
				return TransferOutcome.Refused;
			}

			if (CharacterTransfer.IsLocalMove(character.CurrentSceneName(), character.SceneHandle, character.IsInInstance(),
				destination.SceneName, destination.SceneID))
			{
				character.Motor.SetPositionAndRotationAndVelocity(destination.Position, destination.Rotation, Vector3.zero);
				refusal = TransferRefusal.None;
				return TransferOutcome.MovedLocally;
			}

			if (!DeferHandOff(conn, character, destination))
			{
				refusal = TransferRefusal.NotReady;
				return TransferOutcome.Refused;
			}
			refusal = TransferRefusal.None;
			return TransferOutcome.Transferring;
		}

		/// <inheritdoc/>
		public TransferOutcome TransferToBindPoint(IPlayerCharacter character, out TransferRefusal refusal)
		{
			if (!TryResolveTransferable(character, out NetworkConnection conn, out refusal))
			{
				return TransferOutcome.Refused;
			}

			if (string.IsNullOrEmpty(character.BindScene))
			{
				refusal = TransferRefusal.NoBindPoint;
				return TransferOutcome.Refused;
			}

			refusal = TransferRefusal.None;
			TransferOutcome outcome = SendToBindPoint(conn, character, deferHandOff: true);
			if (outcome == TransferOutcome.Refused)
			{
				refusal = TransferRefusal.NotReady;
			}
			return outcome;
		}

		/// <summary>
		/// The bind-point move both the death respawn and a travel spell make.
		/// </summary>
		/// <remarks>
		/// Local when the bind scene is the scene the character is standing in, with the cloud-world rule
		/// (<see cref="ResolveSameSceneRespawn"/>) choosing the spot; a hand-off otherwise. The comparison is by scene
		/// name, so a bind point in another channel of the same scene is reached by a local move in this one — a bind
		/// point names a place in a scene, never a channel.
		/// </remarks>
		/// <param name="deferHandOff">True when the caller may be inside a tick (see the class remarks).</param>
		/// <returns>What happened.</returns>
		private TransferOutcome SendToBindPoint(NetworkConnection conn, IPlayerCharacter player, bool deferHandOff = false)
		{
			bool sameScene = player.IsInInstance()
				? string.Equals(player.InstanceSceneName, player.BindScene, System.StringComparison.Ordinal)
				: string.Equals(player.SceneName, player.BindScene, System.StringComparison.Ordinal);

			if (sameScene)
			{
				ResolveSameSceneRespawn(player, out Vector3 respawnPosition, out Quaternion respawnRotation);
				player.Motor.SetPositionAndRotationAndVelocity(respawnPosition, respawnRotation, Vector3.zero);
				return TransferOutcome.MovedLocally;
			}

			TransferDestination destination = new TransferDestination(player.BindScene, 0, player.BindPosition, player.Motor.Transform.rotation);
			if (!deferHandOff)
			{
				HandOffToScene(conn, player, destination);
				return TransferOutcome.Transferring;
			}
			return DeferHandOff(conn, player, destination) ? TransferOutcome.Transferring : TransferOutcome.Refused;
		}

		/// <summary>
		/// The checks every transfer request makes before anything moves.
		/// </summary>
		private bool TryResolveTransferable(IPlayerCharacter character, out NetworkConnection conn, out TransferRefusal refusal)
		{
			conn = character?.Owner;
			if (character == null || conn == null || !conn.IsActive ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
				!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter mapped) ||
				!ReferenceEquals(mapped, character))
			{
				refusal = TransferRefusal.NotConnected;
				return false;
			}

			// A character on its way out (a hand-off clears IsLoaded, a queued one is pending) or through a teleporter is already moving.
			if (!character.IsFlagged(CharacterFlags.IsLoaded) || character.IsTeleporting ||
				pendingHandOffCharacterIDs.Contains(character.ID))
			{
				refusal = TransferRefusal.NotReady;
				return false;
			}

			if (character.Motor == null)
			{
				refusal = TransferRefusal.NoMotor;
				return false;
			}

			refusal = TransferRefusal.None;
			return true;
		}

		/// <summary>Whether this world's scene details know <paramref name="sceneName"/>.</summary>
		private bool IsKnownScene(string sceneName)
		{
			return Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) &&
				sceneServerSystem.WorldSceneDetailsCache != null &&
				sceneServerSystem.WorldSceneDetailsCache.Scenes != null &&
				sceneServerSystem.WorldSceneDetailsCache.Scenes.ContainsKey(sceneName);
		}

		/// <summary>
		/// Queues <see cref="HandOffToScene"/> for the main thread, out of whatever tick or callback the caller is in.
		/// </summary>
		/// <remarks>
		/// The character is made immortal at once (it is leaving as it stands) and refused any other transfer until the
		/// queued hand-off runs. When it runs, a character that has meanwhile disconnected or been released by another
		/// route is left alone: that route already saved it where it was.
		/// </remarks>
		/// <returns>False when the queue refused the work; nothing has changed then.</returns>
		private bool DeferHandOff(NetworkConnection conn, IPlayerCharacter character, TransferDestination destination)
		{
			long characterID = character.ID;
			if (!pendingHandOffCharacterIDs.Add(characterID))
			{
				return false;
			}

			if (!TryEnqueueMainThread(() => CompleteDeferredHandOff(conn, character, characterID, destination)))
			{
				pendingHandOffCharacterIDs.Remove(characterID);
				Log.Warning("CharacterSystem", $"Could not queue the hand-off of character {characterID}: the main-thread queue is full.");
				return false;
			}

			if (character.TryGet(out ICharacterDamageController damageController))
			{
				damageController.Immortal = true;
			}
			return true;
		}

		/// <summary>The queued half of <see cref="DeferHandOff"/>.</summary>
		private void CompleteDeferredHandOff(NetworkConnection conn, IPlayerCharacter character, long characterID, TransferDestination destination)
		{
			pendingHandOffCharacterIDs.Remove(characterID);

			// Pooled characters are reused, so the ID is checked as well as the mapping.
			if (conn == null || !conn.IsActive || character == null || character.ID != characterID || character.Motor == null ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
				!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter mapped) ||
				!ReferenceEquals(mapped, character))
			{
				Log.Debug("CharacterSystem", $"Queued hand-off of character {characterID} dropped: it is no longer here.");
				return;
			}

			HandOffToScene(conn, character, destination);
		}

		/// <summary>
		/// Rebinds <paramref name="character"/> to <paramref name="destination"/>, saves and releases it, and drops the
		/// connection so the client's reconnect is routed there by the world server.
		/// </summary>
		/// <remarks>
		/// <para>Leaving an instance by any route clears the instance bookkeeping (a stale <c>InstanceID</c> is what the
		/// world server reads to send a session back into a dungeon) and counts as a deliberate exit, so an instance
		/// whose last occupant left is reclaimed on the next pulse rather than held for the reconnect window.</para>
		/// <para>Callers have already decided the move is allowed; nothing here gates combat.</para>
		/// </remarks>
		/// <param name="conn">The character's connection.</param>
		/// <param name="character">The character leaving.</param>
		/// <param name="destination">Where it is going. <see cref="TransferDestination.SceneID"/> 0 lets the world server choose the channel.</param>
		private void HandOffToScene(NetworkConnection conn, IPlayerCharacter character, in TransferDestination destination)
		{
			bool leavingInstance = character.IsInInstance();

			// Captured before anything changes: the instance being left must be identifiable afterwards.
			long departedWorldServerID = character.WorldServerID;
			string departedInstanceScene = character.InstanceSceneName;
			long departedInstanceHandle = character.InstanceSceneHandle;

			// Announce the departure while the character still names the scene it is leaving.
			DispatchCharacterEvent(OnDisconnect, conn, character, nameof(OnDisconnect));

			// A character mid-hand-off cannot be hurt: it is about to be saved as it stands.
			if (character.TryGet(out ICharacterDamageController damageController))
			{
				damageController.Immortal = true;
			}

			character.SceneName = destination.SceneName;
			character.SceneHandle = destination.SceneID;
			character.Motor.SetPositionAndRotationAndVelocity(destination.Position, destination.Rotation, Vector3.zero);
			character.DisableFlags(CharacterFlags.IsInInstance);
			character.DisableFlags(CharacterFlags.IsLoaded);

			if (leavingInstance)
			{
				// BuildCharacterData reads IsInInstance() — false by now — so the save writes the destination as the world position.
				character.InstanceID = 0;
				character.InstanceSceneName = null;
				character.InstanceSceneHandle = 0;
				character.LastWorldPosition = destination.Position;
				character.LastWorldRotation = destination.Rotation;

				if (Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
				{
					sceneServerSystem.NoteDeliberateInstanceExit(departedWorldServerID, departedInstanceScene, departedInstanceHandle);
				}
			}

			// Save and fully release before the connection drops, so the destination can claim the character.
			// The connection drops even when that throws: by now the character stands at the destination's coordinates in
			// THIS scene, and a client left connected here is stranded there (a portal from Moron 2 left the
			// player under Moron 2's sea at Moron 1's arrival point). Dropped, it reconnects through the world server and
			// the disconnect path releases whatever is left.
			try
			{
				RemoveCharacterConnectionMapping(conn, skipOnDisconnect: true);
			}
			catch (Exception ex)
			{
				Log.Error("CharacterSystem", $"Hand-off of {character.CharacterName} to {destination.SceneName}: the save and release threw; dropping the connection anyway: {ex}");
			}

			// The disconnect below is a transfer, never a combat logout: no body may linger here.
			SuppressCombatLingerOnDisconnect(conn);

			Log.Debug("CharacterSystem", $"{character.CharacterName} handed off to {destination.SceneName} (scene {destination.SceneID}).");
			conn.Disconnect(false);
		}
	}
}
