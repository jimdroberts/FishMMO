using FishNet.Connection;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
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

				/* Resolve what this character, or their party, already has open.
				 *
				 * One batched query answers all three questions this path has to decide between:
				 * join the instance they already hold, refuse because they hold a different one, or
				 * create. It replaces one round trip per party member plus one for the requester —
				 * and, more importantly, it asks about every dungeon rather than only the one being
				 * requested, which is what makes the one-instance-per-party rule enforceable here
				 * as well as in the insert guard.
				 *
				 * Only enterable rows come back. A Failed row is not an instance: routing to one
				 * sent the player through a full disconnect only for the world server to clear the
				 * instance flag and drop them back where they started, on every single attempt. */
				var partyMemberIDs = new List<long>(8) { characterID };
				if (partyID > 0)
				{
					List<long> members = await FetchPartyMemberIDsAsync(partyID);

					/* A roster this request cannot read is not an empty roster.
					 *
					 * Carrying on with just the requester would look the party up as if it were a
					 * solo character: it would miss an instance a member already holds and create a
					 * second one, splitting the group — which is the failure this whole path exists
					 * to prevent, produced by a transient database error. Refusing costs the player
					 * a retry; guessing costs them the run. */
					if (members == null)
					{
						TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
						return;
					}

					for (int i = 0; i < members.Count; ++i)
					{
						if (members[i] != characterID)
						{
							partyMemberIDs.Add(members[i]);
						}
					}
				}

				var heldResult = await sceneService.FetchCharacterInstancesAsync(
					partyMemberIDs, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group, worldServerID);

				if (!heldResult.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Could not read held instances for character {characterID}: {heldResult.ErrorCode} - {heldResult.ErrorMessage}");
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				long instanceID = 0;

				/* The row this request created, if it created one.
				 *
				 * A party may hold one instance at a time, so an instance nobody ends up entering
				 * is not merely litter — it locks the whole party out of every dungeon until the
				 * world server's stale-row sweep reaps it, which is minutes away. Every path below
				 * that gives up after creating the row releases it again. */
				long createdInstanceID = 0;

				/* A full instance is refused, never worked around.
				 *
				 * Asking for a NEW instance whenever nothing joinable was found is right for
				 * "there is no instance" and badly wrong for "the instance is full": a party member
				 * arriving at a full party instance would silently get a second, empty copy of the
				 * dungeon and be separated from the group they were trying to join. */
				bool destinationFull = false;

				/* Holding a different dungeon is refused rather than worked around, which is the
				 * whole one-instance-per-party rule. Without it a party could hold a live copy of
				 * every dungeon on the shard at once, each one an idle physics scene. */
				bool holdsOtherInstance = false;

				foreach (SceneData held in heldResult.Data)
				{
					if (!IsUsableInstance(held, worldServerID))
					{
						continue;
					}

					if (!string.Equals(held.SceneName, dungeonName, StringComparison.Ordinal))
					{
						holdsOtherInstance = true;
						continue;
					}

					// The instance being asked for. Whether it has room decides the outcome, and
					// either way the search stops: a party has one instance.
					if (HasInstanceCapacity(held, maxInstanceClients))
					{
						instanceID = held.ID;
					}
					else
					{
						destinationFull = true;
					}
					holdsOtherInstance = false;
					break;
				}

				if (instanceID <= 0 && !destinationFull && holdsOtherInstance)
				{
					Log.Debug("InteractableSystem",
						$"Dungeon entry refused for character {characterID}: it or its party already has a different instance open.");
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.PartyInstanceExists));
					return;
				}

				if (destinationFull)
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationFull));
					return;
				}

				// Nothing to join: ask for a new instance to be loaded.
				if (instanceID <= 0)
				{
					/* The search above and this insert are separate statements, and every member of
					 * a party clicking the entrance together runs both at the same time — on
					 * per-character async workers, and potentially on different scene servers. Each
					 * one found no instance, each one created its own, and the party was split
					 * across separate copies of the dungeon: the exact failure the party search
					 * exists to prevent, in the one situation where it matters most.
					 *
					 * EnqueueForPartyAsync folds the existence check into the insert, so the losers
					 * of that race insert nothing and are told to join the winner instead.
					 *
					 * Used for a solo character too, with a list of just themselves. Nobody can race
					 * them — a character has one session, and the ingress guard already refuses a
					 * second request from the same connection — but going through the guarded insert
					 * means the one-instance rule is enforced by the database for everyone, rather
					 * than by the in-memory check above for some and the database for others. */
					DatabaseResult<long> enqueueResult = await sceneService.EnqueueForPartyAsync(
						worldServerID,
						dungeonName,
						(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
						characterID,
						partyMemberIDs);

					if (!enqueueResult.IsSuccess)
					{
						await Log.Debug("InteractableSystem", "Failed to enqueue new pending scene load request: " + worldServerID + ":" + dungeonName);
						TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
						return;
					}

					instanceID = enqueueResult.Data;
					createdInstanceID = instanceID;

					if (instanceID <= 0)
					{
						/* Lost the race: a party member created an instance between our search and
						 * our insert. Look again and join theirs — which is the whole point of
						 * refusing to insert. */
						var raceResult = await sceneService.FetchCharacterInstancesAsync(
							partyMemberIDs, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group, worldServerID);

						bool raceHoldsOther = false;
						if (raceResult.IsSuccess)
						{
							foreach (SceneData held in raceResult.Data)
							{
								if (!IsUsableInstance(held, worldServerID))
								{
									continue;
								}
								if (!string.Equals(held.SceneName, dungeonName, StringComparison.Ordinal))
								{
									raceHoldsOther = true;
									continue;
								}
								if (HasInstanceCapacity(held, maxInstanceClients))
								{
									instanceID = held.ID;
								}
								else
								{
									destinationFull = true;
								}
								raceHoldsOther = false;
								break;
							}
						}

						if (destinationFull)
						{
							TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationFull));
							return;
						}

						if (instanceID <= 0 && raceHoldsOther)
						{
							// A member opened a different dungeon first. One instance per party.
							TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.PartyInstanceExists));
							return;
						}

						if (instanceID <= 0)
						{
							/* Blocked by an instance the re-search could not find. The insert guard
							 * and the search now agree on which rows count, so this needs the two
							 * to have been looking at different rosters: a member who left the
							 * party between the two calls, or a party fetch that failed outright.
							 * Both are transient, and both make "try again" the honest answer —
							 * creating a second instance to work around the block would produce
							 * exactly the split party this guard exists to prevent. */
							await Log.Warning("InteractableSystem",
								$"Dungeon entry for character {characterID} was blocked by an existing but unusable instance of '{dungeonName}'; refusing.");
							TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable));
							return;
						}
					}
				}

				long targetInstanceID = instanceID;
				long createdForRelease = createdInstanceID;
				if (!TryEnqueueMainThread(() => EnterInstance(conn, character, targetInstanceID, respawnDetails, createdForRelease)))
				{
					await Log.Warning("InteractableSystem",
						$"Main-thread queue rejected dungeon entry for character {characterID}; refusing the request.");
					ReleaseCreatedInstance(createdForRelease, "dungeon entry could not be dispatched");
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
			CharacterRespawnPositionDetails respawnDetails,
			long createdInstanceID)
		{
			// Guard against character/connection being destroyed between async DB return and main-thread execution
			if (Server == null || conn == null || !conn.IsActive ||
				character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
			{
				ReleaseCreatedInstance(createdInstanceID, "the requesting connection went away before entry");
				return;
			}

			if (!CharacterStateValidation.CanActOrMove(character))
			{
				Log.Debug("InteractableSystem", $"Dungeon entry aborted for {character.CharacterName}: state changed during validation.");
				ReleaseCreatedInstance(createdInstanceID, "the character's state changed before entry");
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
				characterSystem.SuppressCombatLingerOnDisconnect(conn);
			}

			conn.Disconnect(false);
		}

		/// <summary>
		/// <c>/closedungeon</c> chat command. Ends the instance the caller's party currently holds.
		/// </summary>
		/// <remarks>
		/// A party may hold one instance at a time, so a run they are finished with blocks them
		/// from starting anything else until it empties and ages out — several minutes of being
		/// told they already have a dungeon open, about a dungeon they have already left. This is
		/// how they reclaim it deliberately.
		/// <para>
		/// Restricted to the party leader, because it removes everybody: without that, one member
		/// could end a run for the whole group. A character with no party is its own leader.
		/// </para>
		/// <para>
		/// Two cases, and both matter. Inside the instance, the scene server hosting it is this one,
		/// so it can evict and unload directly. Outside it, the instance may be hosted anywhere and
		/// this server cannot touch its scene — but it can retire the row, which is what frees the
		/// party, and the hosting server's idle sweep reclaims the scene on its own. That case is
		/// allowed only while the instance is empty; closing one out from under people who are
		/// still in it, from a server that cannot even tell them why, is not something a chat
		/// command should be able to do.
		/// </para>
		/// </remarks>
		/// <param name="character">Character issuing the command.</param>
		/// <param name="msg">The chat message that carried the command.</param>
		/// <returns>Always <c>true</c>: the command is consumed either way, never echoed to chat.</returns>
		private bool OnCloseDungeonCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			if (character == null)
			{
				return true;
			}

			NetworkConnection conn = character.Owner;

			long partyID = 0;
			if (character.TryGet(out IPartyController partyController) && partyController.ID != 0)
			{
				partyID = partyController.ID;

				/* Rank is read from the controller the party system keeps in step. A stale value
				 * here at worst lets somebody who was leader a moment ago close a run they were
				 * entitled to close. */
				if (partyController.Rank != PartyRank.Leader)
				{
					SendSystemMessage(conn, "Only the party leader can close the dungeon.");
					return true;
				}
			}

			if (character.IsInInstance())
			{
				/* Gated exactly like every other voluntary way out of an instance.
				 *
				 * Closing from inside evicts everybody, and the eviction deliberately skips state
				 * validation because a lifetime cap expiring leaves nothing to validate against.
				 * Reached from a chat command instead, that is a combat-escape: a leader losing a
				 * fight could end the instance and be returned to the open world instantly, which
				 * is a cleaner escape than any teleporter. The cap remains ungated — it is not
				 * player-triggered, and it announces itself first. */
				if (!CharacterStateValidation.CanActOrMove(character))
				{
					SendSystemMessage(conn, "You cannot close the dungeon right now.");
					return true;
				}

				long instanceSceneID = character.InstanceSceneHandle;

				/* Nobody else may be pulled out of a fight either. A leader standing clear while a
				 * member is being attacked could otherwise extract them on demand — the same escape,
				 * one step removed. */
				if (IsAnyoneInCombatInInstance(instanceSceneID))
				{
					SendSystemMessage(conn, "The dungeon cannot be closed while anyone inside is in combat.");
					return true;
				}

				if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
				{
					SendSystemMessage(conn, "The dungeon could not be closed. Please try again.");
					return true;
				}

				Log.Debug("InteractableSystem", $"{character.CharacterName} closed instance {instanceSceneID} from inside.");

				// Sent before the close, which disconnects this player along with everyone else.
				// Disconnect(false) flushes the tick, so the line still reaches them.
				SendSystemMessage(conn, "Closing the dungeon...");
				sceneServerSystem.CloseInstance(instanceSceneID, "closed by the party leader");
				return true;
			}

			long characterID = character.ID;
			long worldServerID = character.WorldServerID;

			if (!TryEnqueueAsyncWork(() => CloseHeldInstanceAsync(conn, characterID, worldServerID, partyID), characterID))
			{
				SendSystemMessage(conn, "The dungeon could not be closed right now. Please try again.");
			}
			return true;
		}

		/// <summary>
		/// Whether anyone standing in an instance is currently in combat.
		/// </summary>
		/// <remarks>
		/// Only meaningful on the scene server hosting the instance, which is where the sole caller
		/// runs — a character can only close from inside an instance this process hosts.
		/// </remarks>
		/// <param name="instanceSceneID">Scene row of the instance to inspect.</param>
		private bool IsAnyoneInCombatInInstance(long instanceSceneID)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping))
			{
				// Cannot tell. Refusing is the safe direction for a combat gate.
				return true;
			}

			foreach (var kvp in charMapping.ConnectionCharacters)
			{
				IPlayerCharacter resident = kvp.Value;
				if (resident != null &&
					resident.IsInInstance() &&
					resident.InstanceSceneHandle == instanceSceneID &&
					resident.IsFlagged(CharacterFlags.IsInCombat))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Retires the party's instance from outside it, when nobody is in it.
		/// </summary>
		/// <remarks>
		/// Marks the row Failed rather than deleting it, for the same reasons as
		/// <see cref="ReleaseCreatedInstance"/>: Failed is not a state the one-instance guard blocks
		/// on, so the party is free immediately, and a scene server that is mid-load finds a row
		/// that is no longer Loading and declines to bring it into service.
		/// <para>
		/// <c>CharacterCount</c> is refreshed by the hosting scene server's pulse, so it lags by up
		/// to one pulse interval. A member who zoned in within that window could have their
		/// instance retired underneath them — they are not evicted, but their next reconnect routes
		/// them to the open world. The alternative, asking the hosting scene server synchronously,
		/// would put a cross-server round trip inside a chat command; the same soft-count trade the
		/// entry path already makes.
		/// </para>
		/// </remarks>
		private async Task CloseHeldInstanceAsync(NetworkConnection conn, long characterID, long worldServerID, long partyID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
					return;
				}

				var memberIDs = new List<long>(8) { characterID };
				if (partyID > 0)
				{
					List<long> members = await FetchPartyMemberIDsAsync(partyID);
					if (members == null)
					{
						TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
						return;
					}
					for (int i = 0; i < members.Count; ++i)
					{
						if (members[i] != characterID)
						{
							memberIDs.Add(members[i]);
						}
					}
				}

				var heldResult = await sceneService.FetchCharacterInstancesAsync(
					memberIDs, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group, worldServerID);

				if (!heldResult.IsSuccess)
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
					return;
				}

				SceneData held = default;
				bool found = false;
				foreach (SceneData candidate in heldResult.Data)
				{
					if (IsUsableInstance(candidate, worldServerID))
					{
						held = candidate;
						found = true;
						break;
					}
				}

				if (!found)
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn, "You do not have a dungeon open."));
					return;
				}

				if (held.CharacterCount > 0)
				{
					TryEnqueueMainThread(() => SendSystemMessage(conn,
						$"Someone is still inside {held.SceneName}. They must leave before it can be closed."));
					return;
				}

				DatabaseResult result = await sceneService.UpdateStatusAsync(
					held.ID, FishMMO.Database.Data.Enums.SceneStatus.Failed);

				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Failed to close instance {held.ID} for character {characterID}: {result.ErrorCode} - {result.ErrorMessage}");
					TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
					return;
				}

				await Log.Debug("InteractableSystem", $"Character {characterID} closed held instance {held.ID} ({held.SceneName}) from outside.");
				TryEnqueueMainThread(() => SendSystemMessage(conn, $"{held.SceneName} has been closed."));
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error closing a held instance for character {characterID}: {ex}");
				TryEnqueueMainThread(() => SendSystemMessage(conn, "The dungeon could not be closed. Please try again."));
			}
		}

		/// <summary>
		/// Sends one system-channel line to a connection, if it is still there. Main thread only.
		/// </summary>
		private void SendSystemMessage(NetworkConnection conn, string text)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server?.NetworkWrapper?.Broadcast(conn, new ChatBroadcast()
			{
				Channel = ChatChannel.System,
				Text = text,
			}, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>
		/// Marks an instance this request created, but nobody entered, as failed.
		/// </summary>
		/// <remarks>
		/// A party may hold one instance at a time, so a row created for an entry that then fell
		/// through does not merely sit there: it is the party's instance as far as the one-instance
		/// guard is concerned, and it locks every member out of every dungeon until the world
		/// server's stale-row sweep removes it — up to several minutes later, for a dungeon nobody
		/// ever set foot in. The ordinary way to reach that is entirely benign: the player is
		/// pulled into combat while the database work runs.
		/// <para>
		/// Failed rather than deleted. Failed is not one of the states the guard blocks on, so the
		/// party is free immediately, and the row survives as a record until the sweep reaps it. If
		/// a scene server has already dequeued it, that load's <c>SetReadyAsync</c> finds a row that
		/// is no longer Loading and declines, and the scene it produced is unloaded by the idle
		/// sweep like any other empty instance.
		/// </para>
		/// <para>
		/// Only ever called with a row this request created. Releasing an instance the caller merely
		/// joined would close a dungeon other people are in.
		/// </para>
		/// </remarks>
		/// <param name="createdInstanceID">Row to release, or 0 when this request created none.</param>
		/// <param name="reason">Why it is being released, for diagnostics.</param>
		private void ReleaseCreatedInstance(long createdInstanceID, string reason)
		{
			if (createdInstanceID <= 0)
			{
				return;
			}

			Log.Debug("InteractableSystem", $"Releasing unused instance {createdInstanceID}: {reason}.");

			if (!TryEnqueueAsyncWork(() => FailInstanceAsync(createdInstanceID), createdInstanceID))
			{
				Log.Warning("InteractableSystem",
					$"Could not enqueue the release of unused instance {createdInstanceID}; the party is blocked from opening another until the stale-row sweep removes it.");
			}
		}

		/// <summary>
		/// Performs the release. See <see cref="ReleaseCreatedInstance"/>.
		/// </summary>
		private async Task FailInstanceAsync(long instanceID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}

				DatabaseResult result = await sceneService.UpdateStatusAsync(
					instanceID, FishMMO.Database.Data.Enums.SceneStatus.Failed);

				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem",
						$"Failed to release unused instance {instanceID}: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error releasing unused instance {instanceID}: {ex}");
			}
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
		private static bool IsUsableInstance(SceneData sceneData, long worldServerID)
		{
			if (sceneData.ID <= 0 || sceneData.WorldServerID != worldServerID)
			{
				return false;
			}

			/* Deliberately says nothing about which dungeon this is. Callers compare the name
			 * themselves, because holding an instance of a DIFFERENT dungeon is not "no instance" —
			 * it is the one-instance-per-party rule refusing the request, and folding the name in
			 * here is what previously made that case indistinguishable from having none. */
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
		/// Reads a party's roster.
		/// </summary>
		/// <remarks>
		/// The finder needs the membership for two things at once: to ask, in one query, whether
		/// anyone in the party already holds an instance, and to block a racing insert against the
		/// same set of characters. It used to walk the roster itself, issuing a scene lookup per
		/// member — so a six-player party cost seven round trips to answer a question one query
		/// answers, and it could only ever ask about the dungeon being requested.
		/// </remarks>
		/// <param name="partyID">Party to read.</param>
		/// <returns>
		/// The member character IDs, or <c>null</c> when the roster could not be read. Null is
		/// meaningful: the caller must not then treat an empty roster as "this party holds
		/// nothing", because that is exactly when creating a second instance would split it.
		/// </returns>
		private async Task<List<long>> FetchPartyMemberIDsAsync(long partyID)
		{
			if (partyID <= 0 ||
				Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
			{
				return null;
			}

			var membersResult = await charPartyService.FetchManyAsync(partyID);
			if (!membersResult.IsSuccess || membersResult.Data == null)
			{
				await Log.Warning("InteractableSystem", $"Could not read the roster of party {partyID} for a dungeon request.");
				return null;
			}

			var memberIDs = new List<long>(membersResult.Data.Count);
			foreach (var member in membersResult.Data)
			{
				memberIDs.Add(member.CharacterID);
			}
			return memberIDs;
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
