using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Linq;
using System;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

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

			/* CanActOrMove, not CanAct.
			 *
			 * Entering a dungeon is a voluntary move to another scene instance, and it is
			 * implemented as a disconnect — so in combat it is both a cleaner escape than any
			 * teleporter (instant, and it lands the player somewhere their attacker cannot
			 * follow) and actively corrupting. The disconnect lands in
			 * CharacterSystem.OnRemoteConnectionStopped, which for an unannounced drop starts a
			 * combat-logout linger: the body stays on THIS scene server holding the character's
			 * session claim, while the row now says the character is in an instance. The world
			 * server routes the reconnect to the instance's scene server, which has no body to
			 * reattach, loses the claim race, and kicks the player — on every retry, until the
			 * linger runs out. The channel switch is gated the same way for the same reasons. */
			if (!CharacterStateValidation.CanActOrMove(character))
			{
				SendTransferRefused(conn, SceneTransferRefusalReason.CharacterStateChanged);
				return;
			}

			// Already inside an instance: the entrance is not a way to hop between them.
			if (character.IsInInstance())
			{
				SendTransferRefused(conn, SceneTransferRefusalReason.CharacterStateChanged);
				return;
			}

			// Acquire ingress guard for dungeon finder
			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				// Debounced or already in flight; say so rather than appearing to ignore the click.
				SendTransferRefused(conn, SceneTransferRefusalReason.OnCooldown);
				return;
			}

			bool asyncOwnsGuard = false;
			try
			{
				// Validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable);
					return;
				}

				// Validate Dungeon Entrance
				IDungeonEntrance dungeonEntrance = sceneObject.GameObject.GetComponent<IDungeonEntrance>();
				if (dungeonEntrance == null ||
					!dungeonEntrance.InRange(character.Transform))
				{
					SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable);
					return;
				}

				// Validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(dungeonEntrance.DungeonName, out WorldSceneDetails details))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + dungeonEntrance.DungeonName);
					SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable);
					return;
				}

				if (details.RespawnPositions == null || details.RespawnPositions.Count < 1)
				{
					Log.Debug("InteractableSystem", $"Missing Scene: {dungeonEntrance.DungeonName} respawn points.");
					SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable);
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

				/* Read the cap on the main thread, with the rest of the scene details.
				 *
				 * Nothing capped instanced scenes at all: the world server's instance routing
				 * sends a character to whichever scene server hosts its instance without ever
				 * consulting a limit, and joining a party member's instance did not either — so
				 * a Group scene could be filled without bound, by a party that kept growing or
				 * simply by everyone in it re-entering. The open-world path has always respected
				 * MaxClients; this applies the same number to instanced content, at the point
				 * where a refusal can still be reported to the player. */
				int maxInstanceClients = Math.Max(1, details.MaxClients);

				CharacterRespawnPositionDetails respawnDetails = details.RespawnPositions.Values.ToList().GetRandom();

				// Increment achievement for entering a dungeon
				if (dungeonEntrance.AchievementTemplate != null &&
					character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(dungeonEntrance.AchievementTemplate, 1);
				}

				// Fire-and-forget: process dungeon instance assignment asynchronously.
				// The async task's own finally block will release the guard on completion.
				if (TryEnqueueAsyncWork(() => ProcessDungeonFinderAsync(conn, character, characterID, worldServerID, partyID, dungeonName, maxInstanceClients, respawnDetails, guardKey), conn, characterID))
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
		/// Asynchronously processes dungeon finder logic: reuses an existing usable instance,
		/// joins a party member's instance, or enqueues a new one. Marshals character state
		/// changes and the disconnect back to the main thread.
		/// </summary>
		/// <param name="conn">Owning connection used for disconnecting after assignment.</param>
		/// <param name="character">Character requesting dungeon finder processing.</param>
		/// <param name="characterID">Unique identifier of the requesting character.</param>
		/// <param name="worldServerID">World server identifier where the request originated.</param>
		/// <param name="partyID">Party identifier if the character is grouped; otherwise 0.</param>
		/// <param name="dungeonName">Target dungeon scene name.</param>
		/// <param name="maxInstanceClients">Maximum characters allowed in one instance of this dungeon.</param>
		/// <param name="respawnDetails">Respawn position and rotation to apply on entry.</param>
		/// <param name="guardKey">Ingress guard key released when this task completes.</param>
		/// <returns>A task representing asynchronous dungeon finder processing.</returns>
		private async Task ProcessDungeonFinderAsync(
			NetworkConnection conn,
			IPlayerCharacter character,
			long characterID,
			long worldServerID,
			long partyID,
			string dungeonName,
			int maxInstanceClients,
			CharacterRespawnPositionDetails respawnDetails,
			long guardKey)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				/* Reuse this character's own instance when it is still usable.
				 *
				 * FetchCharacterInstanceAsync matches on character_id alone, so it returns the
				 * row whatever state it is in — including Failed. Routing to a Failed row sent
				 * the player through a full disconnect only for the world server to clear the
				 * instance flag and drop them back where they started, and because nothing ever
				 * deleted that row it happened on every single attempt: the dungeon became
				 * permanently unenterable for that character. Anything not usable is treated as
				 * absent, and a fresh instance is requested instead. */
				long instanceID = 0;

				/* A full instance is refused, never worked around.
				 *
				 * The fallback below asks for a NEW instance whenever nothing joinable was
				 * found, which is right for "there is no instance" and badly wrong for "the
				 * instance is full": a party member arriving at a full party instance would
				 * silently get a second, empty copy of the dungeon and be separated from the
				 * group they were trying to join. Distinguishing the two is what lets the player
				 * be told the truth. */
				bool destinationFull = false;

				var ownInstance = await sceneService.FetchCharacterInstanceAsync(
					characterID, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group);
				if (ownInstance.IsSuccess && IsUsableInstance(ownInstance.Data, worldServerID, dungeonName))
				{
					if (HasInstanceCapacity(ownInstance.Data, maxInstanceClients))
					{
						instanceID = ownInstance.Data.ID;
					}
					else
					{
						destinationFull = true;
					}
				}

				// Otherwise join a party member's instance, so a group ends up in one dungeon.
				if (instanceID <= 0 && !destinationFull && partyID > 0)
				{
					(instanceID, destinationFull) = await FindPartyInstanceAsync(
						partyID, characterID, worldServerID, dungeonName, maxInstanceClients);
				}

				if (destinationFull)
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationFull));
					return;
				}

				// Nothing to join: ask for a new instance to be loaded.
				if (instanceID <= 0)
				{
					var enqueueResult = await sceneService.EnqueueAsync(
						worldServerID,
						dungeonName,
						(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
						characterID);

					if (!enqueueResult.IsSuccess)
					{
						await Log.Debug("InteractableSystem", "Failed to enqueue new pending scene load request: " + worldServerID + ":" + dungeonName);
						TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
						return;
					}

					instanceID = enqueueResult.Data;
				}

				long targetInstanceID = instanceID;
				if (!TryEnqueueMainThread(() => EnterInstance(conn, character, targetInstanceID, respawnDetails)))
				{
					await Log.Warning("InteractableSystem",
						$"Main-thread queue rejected dungeon entry for character {characterID}; refusing the request.");
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error processing dungeon finder: {ex}");
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Binds a character to a dungeon instance and drops its connection so the world server
		/// routes it to the scene server hosting that instance. Main thread only.
		/// </summary>
		/// <remarks>
		/// The state is re-checked here rather than trusted from the request. Everything between
		/// the broadcast and this call was asynchronous database work, and a character can be
		/// pulled into combat or killed while it runs — at which point the disconnect below is no
		/// longer a transfer but a combat logout, with the consequences described in
		/// <see cref="OnServerDungeonFinderBroadcastReceived"/>.
		/// </remarks>
		private void EnterInstance(
			NetworkConnection conn,
			IPlayerCharacter character,
			long instanceID,
			CharacterRespawnPositionDetails respawnDetails)
		{
			// Guard against character/connection being destroyed between async DB return and main-thread execution
			if (Server == null || conn == null || !conn.IsActive ||
				character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
			{
				return;
			}

			if (!CharacterStateValidation.CanActOrMove(character))
			{
				Log.Debug("InteractableSystem", $"Dungeon entry aborted for {character.CharacterName}: state changed during validation.");
				SendTransferRefused(conn, SceneTransferRefusalReason.CharacterStateChanged);
				return;
			}

			character.InstanceID = instanceID;
			character.InstancePosition = respawnDetails.Position;
			character.InstanceRotation = respawnDetails.Rotation;
			character.EnableFlags(CharacterFlags.IsInInstance);

			// Prevent gameplay actions during the transition.
			character.DisableFlags(CharacterFlags.IsLoaded);

			/* Announce the hand-off before dropping the connection.
			 *
			 * Without this the character system cannot tell this disconnect from a player
			 * quitting, and a character that entered combat in the last instant would have its
			 * body — and its session claim — held on this scene server while the row says it
			 * belongs to the instance. The arriving client would then be kicked for claim
			 * contention on every retry until the linger expired. */
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.BeginDeliberateTransfer(conn);
			}

			conn.Disconnect(false);
		}

		/// <summary>
		/// Whether a scene row can still be entered as a dungeon instance.
		/// </summary>
		/// <remarks>
		/// Pending and Loading count as usable: the instance has been requested and the world
		/// server holds the client in its instance queue until the scene becomes ready, which is
		/// the designed wait. Failed — and any state the enum does not cover — does not, because
		/// nothing will ever move that row forward.
		/// </remarks>
		/// <param name="sceneData">Candidate scene row.</param>
		/// <param name="worldServerID">World server the requesting character belongs to.</param>
		/// <param name="dungeonName">Dungeon the character asked to enter.</param>
		private static bool IsUsableInstance(SceneData sceneData, long worldServerID, string dungeonName)
		{
			if (sceneData.ID <= 0 || sceneData.WorldServerID != worldServerID)
			{
				return false;
			}

			// A saved instance of a different dungeon must not swallow this request; the
			// character would be routed into unrelated content.
			if (!string.Equals(sceneData.SceneName, dungeonName, StringComparison.Ordinal))
			{
				return false;
			}

			SceneStatus status = (SceneStatus)sceneData.SceneStatus;
			return status == SceneStatus.Ready ||
				   status == SceneStatus.Pending ||
				   status == SceneStatus.Loading;
		}

		/// <summary>
		/// Whether an instance still has room for one more character.
		/// </summary>
		/// <remarks>
		/// Deliberately separate from <see cref="IsUsableInstance"/>: "not joinable because it is
		/// full" and "not there" lead to opposite actions — the first must refuse, the second
		/// must create.
		/// <para>
		/// <c>CharacterCount</c> is refreshed by the hosting scene server's pulse, so it lags by
		/// up to one pulse interval and simultaneous entries can overshoot slightly. That is the
		/// same soft-cap behaviour the open-world routing path has always had, and the alternative
		/// — asking the hosting scene server synchronously — would put a cross-server round trip
		/// in front of every dungeon entry.
		/// </para>
		/// <para>
		/// A Pending or Loading instance has no occupants yet, so it always has room.
		/// </para>
		/// </remarks>
		/// <param name="sceneData">Candidate scene row.</param>
		/// <param name="maxClients">Maximum characters allowed in one instance of this dungeon.</param>
		private static bool HasInstanceCapacity(SceneData sceneData, int maxClients)
		{
			if ((SceneStatus)sceneData.SceneStatus != SceneStatus.Ready)
			{
				return true;
			}

			return sceneData.CharacterCount < maxClients;
		}

		/// <summary>
		/// Finds a usable dungeon instance already held by one of the character's party members.
		/// </summary>
		/// <remarks>
		/// This is what makes a group dungeon a group activity. The previous behaviour detected
		/// that a party member had an instance and then returned without doing anything at all —
		/// the comment said "notify the connection", but no notification was ever sent — so a
		/// party member clicking the entrance got silence, and could never reach the instance
		/// their party was in.
		/// </remarks>
		/// <param name="partyID">Party to search.</param>
		/// <param name="requestingCharacterID">Character making the request, skipped in the search.</param>
		/// <param name="worldServerID">World server the requesting character belongs to.</param>
		/// <param name="dungeonName">Dungeon the character asked to enter.</param>
		/// <param name="maxClients">Maximum characters allowed in one instance of this dungeon.</param>
		/// <returns>
		/// The instance's scene ID, or 0 when no party member holds a usable one. <c>Full</c> is
		/// <c>true</c> when a party member's instance was found but has no room — the caller must
		/// refuse rather than create a second instance and split the group.
		/// </returns>
		private async Task<(long InstanceID, bool Full)> FindPartyInstanceAsync(
			long partyID, long requestingCharacterID, long worldServerID, string dungeonName, int maxClients)
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return (0, false);
			}
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService) ||
				!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				return (0, false);
			}

			var membersResult = await charPartyService.FetchManyAsync(partyID);
			if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count == 0)
			{
				return (0, false);
			}

			foreach (var member in membersResult.Data)
			{
				if (member.CharacterID == requestingCharacterID)
				{
					continue;
				}

				var instanceResult = await sceneService.FetchCharacterInstanceAsync(
					member.CharacterID, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group);

				if (!instanceResult.IsSuccess || !IsUsableInstance(instanceResult.Data, worldServerID, dungeonName))
				{
					continue;
				}

				// The party's instance, found. Whether it has room decides the outcome; either
				// way the search stops here, because a party has one instance of a dungeon and
				// looking further could only find the same row through another member.
				return HasInstanceCapacity(instanceResult.Data, maxClients)
					? (instanceResult.Data.ID, false)
					: (0L, true);
			}

			return (0, false);
		}

		/// <summary>
		/// Tells a client its dungeon entry was declined, and why.
		/// </summary>
		/// <remarks>
		/// Every rejection on this path used to be a bare <c>return</c>. The player clicked the
		/// entrance and nothing happened, with no way to tell a refusal from a dropped request —
		/// so the natural response was to click again, which the ingress guard then swallowed
		/// too. Reliable channel: this is a one-shot transition that unblocks the client's UI.
		/// </remarks>
		/// <param name="conn">Connection to notify. Main thread only.</param>
		/// <param name="reason">Why the request was refused.</param>
		private void SendTransferRefused(NetworkConnection conn, SceneTransferRefusalReason reason)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server?.NetworkWrapper?.Broadcast(conn,
				new SceneTransferRefusedBroadcast { Reason = reason },
				true,
				FishNet.Transporting.Channel.Reliable);
		}
	}
}
